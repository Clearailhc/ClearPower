"""Collect all sources into one snapshot dict (plain python values)."""
import time

from .sources.battery import Battery
from .sources.usbpd import Adapter
from .sources.rapl import Rapl
from .sources.backlight import Backlight
from .sources.hwmon import Hwmon
from .sources.procs import Procs
from .sysfs import read_str


class Sampler:
    def __init__(self, cfg):
        self.cfg = cfg
        self.battery = Battery(cfg["battery"])
        self.adapter = Adapter()
        self.rapl = Rapl()
        self.backlight = Backlight()
        self.hwmon = Hwmon()
        self.procs = Procs(cfg["procs_interval_s"])
        self.last = {}
        self._thermal = {"temp_cpu": -1.0, "temp_gpu": -1.0, "temp_nvme": -1.0, "fan1": -1, "fan2": -1}
        self._thermal_at = -1e9

    def _display_w(self, bl):
        if not bl["display_on"]:
            return 0.0
        frac = bl["brightness_pct"] / 100.0 if bl["brightness_pct"] >= 0 else 0.6
        pmin, pmax = self.cfg["display_p_min_w"], self.cfg["display_p_max_w"]
        return pmin + (pmax - pmin) * frac

    def _thermal_read(self, hot):
        """EC-backed temps/fans cost ~40 ms; only read while someone is looking, every 3 s."""
        now = time.monotonic()
        if hot and now - self._thermal_at >= 3.0:
            self._thermal = self.hwmon.read()
            self._thermal_at = now
        return self._thermal

    def sample(self, hot=True):
        snap = {"ts": time.time()}
        bat = self.battery.read()
        ad = self.adapter.read()
        rapl = self.rapl.read()
        bl = self.backlight.read()
        snap.update(bat)
        snap.update(ad)
        snap.update(bl)
        snap.update(self._thermal_read(hot))

        soc_w = rapl.get("package", -1.0)
        psys_w = rapl.get("psys", -1.0)
        display_w = self._display_w(bl)
        bat_w = bat.get("bat_w", 0.0)
        on_ac = ad["on_ac"]

        # Whole-machine draw. Prefer psys; else derive from battery when on battery.
        sys_source = "psys"
        if psys_w >= 0 and psys_w >= soc_w * 0.9:
            sys_w = psys_w
        elif not on_ac and bat_w < 0:
            sys_w = -bat_w
            sys_source = "battery"
        else:
            sys_w = max(soc_w, 0.0) + display_w + 2.0
            sys_source = "estimate"

        # On battery, and when charging, the physical truth is the battery; use it to
        # bound the flow diagram so it stays consistent.
        if not on_ac and bat_w < 0 and sys_source == "psys":
            # psys can lag; keep it, but expose battery draw too.
            pass

        adapter_w = (sys_w + max(bat_w, 0.0)) if on_ac else 0.0
        other_w = max(sys_w - max(soc_w, 0.0) - display_w, 0.0)

        snap.update({
            "sys_w": sys_w, "sys_source": sys_source,
            "soc_w": soc_w,
            "core_w": rapl.get("core", -1.0),
            "gpu_w": rapl.get("uncore", -1.0),
            "dram_w": rapl.get("dram", -1.0),
            "psys_w": psys_w,
            "display_w": display_w,
            "other_w": other_w,
            "adapter_w": adapter_w,
            "platform_profile": read_str("/sys/firmware/acpi/platform_profile") or "",
            "rapl_available": bool(rapl),
        })
        self.last = snap
        return snap
