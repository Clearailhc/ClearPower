"""Battery readings from /sys/class/power_supply/<BAT>.

One `uevent` read (~0.03 ms) carries every regular attribute. The charge-control
attributes go through the embedded controller and cost milliseconds each, so they
are refreshed only every `slow_every_s` seconds or when `invalidate()` is called
after the daemon itself writes them.
"""
import os
import time

from ..sysfs import read_int, read_str, read_bracketed


class Battery:
    def __init__(self, name="BAT0", slow_every_s=30):
        self.base = f"/sys/class/power_supply/{name}"
        self.present = os.path.isdir(self.base)
        self.slow_every = slow_every_s
        self._slow = {}
        self._slow_at = -1e9

    def _uevent(self):
        out = {}
        s = read_str(os.path.join(self.base, "uevent")) or ""
        for line in s.splitlines():
            k, _, v = line.partition("=")
            if k.startswith("POWER_SUPPLY_"):
                out[k[len("POWER_SUPPLY_"):]] = v
        return out

    @staticmethod
    def _i(d, key, default=0):
        try:
            return int(d.get(key, default))
        except ValueError:
            return default

    def invalidate(self):
        self._slow_at = -1e9

    def _slow_attrs(self):
        now = time.monotonic()
        if now - self._slow_at < self.slow_every:
            return self._slow
        behaviour, _ = read_bracketed(os.path.join(self.base, "charge_behaviour"))
        self._slow = {
            "charge_behaviour": behaviour or "auto",
            "charge_start_threshold": int(read_int(os.path.join(self.base, "charge_control_start_threshold")) or 0),
            "charge_end_threshold": int(read_int(os.path.join(self.base, "charge_control_end_threshold")) or 100),
        }
        self._slow_at = now
        return self._slow

    def read(self):
        if not self.present:
            return {"bat_present": False}
        u = self._uevent()
        status = u.get("STATUS", "Unknown")
        power_uw = self._i(u, "POWER_NOW")
        energy_now = self._i(u, "ENERGY_NOW")
        energy_full = self._i(u, "ENERGY_FULL")
        energy_design = self._i(u, "ENERGY_FULL_DESIGN")
        cap = self._i(u, "CAPACITY", -1)
        if cap < 0 and energy_full:
            cap = round(100 * energy_now / energy_full)
        # Sign convention: positive = into the battery (charging), negative = out.
        bat_w = power_uw / 1e6
        if status == "Discharging":
            bat_w = -bat_w
        elif status != "Charging":
            bat_w = 0.0
        out = {
            "bat_present": True,
            "bat_status": status,
            "bat_pct": int(max(cap, 0)),
            "bat_w": bat_w,
            "bat_energy_wh": energy_now / 1e6,
            "bat_full_wh": energy_full / 1e6,
            "bat_design_wh": energy_design / 1e6,
            "bat_v": self._i(u, "VOLTAGE_NOW") / 1e6,
            "cycle_count": self._i(u, "CYCLE_COUNT"),
            "bat_model": u.get("MODEL_NAME", ""),
            "bat_manufacturer": u.get("MANUFACTURER", ""),
        }
        out.update(self._slow_attrs())
        return out
