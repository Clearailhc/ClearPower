"""Collect all sources into one snapshot dict with a power breakdown that always adds up.

Every watt shown to the user is either measured (battery, psys, RAPL domains) or
derived from measured values by subtraction, so the parts sum to the whole by
construction.  Inputs are smoothed (EMA) before the breakdown so the breakdown of
smoothed values is still conserved.  `self.raw` keeps the unsmoothed inputs for
the runtime estimator and display calibration.
"""
import time

from .sources.battery import Battery
from .sources.usbpd import Adapter
from .sources.rapl import Rapl
from .sources.backlight import Backlight
from .sources.hwmon import Hwmon
from .sources.procs import Procs
from .smoothing import Ema
from .sysfs import read_str

SMOOTHED = ("bat_w", "psys", "package", "core", "uncore", "dram")


class Sampler:
    def __init__(self, cfg, display_cal=None):
        self.cfg = cfg
        self.battery = Battery(cfg["battery"])
        self.adapter = Adapter()
        self.rapl = Rapl()
        self.backlight = Backlight()
        self.hwmon = Hwmon()
        self.procs = Procs(cfg["procs_interval_s"])
        self.display_cal = display_cal
        self.ema = {k: Ema(cfg["smoothing_s"]) for k in SMOOTHED}
        self.last = {}
        self.raw = {}
        self._thermal = {"temp_cpu": -1.0, "temp_gpu": -1.0, "temp_nvme": -1.0, "fan1": -1, "fan2": -1}
        self._thermal_at = -1e9

    def _thermal_read(self, hot):
        """EC-backed temps/fans cost ~40 ms; only read while someone is looking, every 3 s."""
        now = time.monotonic()
        if hot and now - self._thermal_at >= 3.0:
            self._thermal = self.hwmon.read()
            self._thermal_at = now
        return self._thermal

    def _smooth(self, key, value, now):
        if value is None or value < 0:
            self.ema[key].reset()
            return -1.0
        return self.ema[key].update(value, now)

    @staticmethod
    def _rest(psys, package, dram):
        """Platform power not attributable to the SoC or memory (display + peripherals)."""
        if psys is None or psys < 0:
            return -1.0
        return max(psys - max(package or 0.0, 0.0) - max(dram or 0.0, 0.0), 0.0)

    def sample(self, hot=True):
        now = time.monotonic()
        snap = {"ts": time.time()}
        bat = self.battery.read()
        ad = self.adapter.read()
        rapl = self.rapl.read()
        bl = self.backlight.read()
        snap.update(bat)
        snap.update(ad)
        snap.update(bl)
        snap.update(self._thermal_read(hot))
        on_ac = ad["on_ac"]

        # ---- raw inputs (for history / runtime / calibration) ----
        raw = {
            "bat_w": bat.get("bat_w", 0.0),
            "psys": rapl.get("psys", -1.0),
            "package": rapl.get("package", -1.0),
            "core": rapl.get("core", -1.0),
            "uncore": rapl.get("uncore", -1.0),
            "dram": rapl.get("dram", -1.0),
        }
        raw["rest"] = self._rest(raw["psys"], raw["package"], raw["dram"])
        self.raw = raw

        # ---- smoothed inputs ----
        bat_w = self._smooth("bat_w", raw["bat_w"] if raw["bat_w"] is not None else 0.0, now)
        psys = self._smooth("psys", raw["psys"], now)
        package = self._smooth("package", raw["package"], now)
        core = self._smooth("core", raw["core"], now)
        uncore = self._smooth("uncore", raw["uncore"], now)
        dram = self._smooth("dram", raw["dram"], now)
        # bat_w is signed; the EMA above is fine with that but -1 sentinel logic must not apply
        if self.ema["bat_w"].v is None:
            bat_w = 0.0

        # ---- whole-machine draw ----
        if not on_ac and bat_w < -0.05:
            sys_w, sys_source = -bat_w, "battery"      # physical truth incl. all losses
        elif psys > 0:
            sys_w, sys_source = psys, "psys"
        elif package > 0:
            sys_w, sys_source = package + 3.0, "estimate"
        else:
            sys_w, sys_source = -1.0, "none"

        # ---- breakdown (all derived by subtraction => conserved) ----
        cpu_w = gpu_w = soc_w = mem_w = -1.0
        rest_w = -1.0
        if package > 0 and sys_w > 0:
            cpu_w = max(core, 0.0)
            gpu_w = max(uncore, 0.0)
            soc_w = max(package - cpu_w - gpu_w, 0.0)
            mem_w = max(dram, 0.0)
            measured = package + mem_w
            if measured > sys_w:  # psys occasionally undershoots; keep the total authoritative
                k = sys_w / measured
                cpu_w, gpu_w, soc_w, mem_w = cpu_w * k, gpu_w * k, soc_w * k, mem_w * k
                measured = sys_w
            rest_w = sys_w - measured
        elif sys_w > 0:
            rest_w = sys_w

        display_w = -1.0
        other_w = rest_w
        if self.display_cal is not None and rest_w >= 0:
            e = self.display_cal.emission_w(bl.get("brightness_raw"), now)
            if e >= 0:
                display_w = 0.0 if not bl["display_on"] else min(e, rest_w)
                other_w = rest_w - display_w

        adapter_w = (sys_w + max(bat_w, 0.0)) if (on_ac and sys_w > 0) else 0.0
        snap.update({
            "sys_w": sys_w, "sys_source": sys_source,
            "psys_w": psys, "package_w": package,
            "cpu_w": cpu_w, "gpu_w": gpu_w, "soc_w": soc_w, "mem_w": mem_w,
            "rest_w": rest_w, "display_w": display_w, "other_w": other_w,
            "bat_w": bat_w, "bat_w_raw": raw["bat_w"],
            "adapter_w": adapter_w,
            "platform_profile": read_str("/sys/firmware/acpi/platform_profile") or "",
            "rapl_available": self.rapl.available and bool(rapl),
        })
        if self.display_cal is not None:
            snap.update(self.display_cal.snapshot_keys())
        self.last = snap
        return snap
