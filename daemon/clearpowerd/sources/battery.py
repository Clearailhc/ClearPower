"""Battery readings from /sys/class/power_supply/<BAT>."""
import os

from ..sysfs import read_int, read_str, read_bracketed


class Battery:
    def __init__(self, name="BAT0"):
        self.base = f"/sys/class/power_supply/{name}"
        self.present = os.path.isdir(self.base)

    def _i(self, f):
        return read_int(os.path.join(self.base, f))

    def _s(self, f):
        return read_str(os.path.join(self.base, f))

    def read(self):
        if not self.present:
            return {"bat_present": False}
        status = self._s("status") or "Unknown"
        power_uw = self._i("power_now") or 0
        energy_now = self._i("energy_now") or 0
        energy_full = self._i("energy_full") or 0
        energy_design = self._i("energy_full_design") or 0
        cap = self._i("capacity")
        if cap is None and energy_full:
            cap = round(100 * energy_now / energy_full)
        behaviour, _choices = read_bracketed(os.path.join(self.base, "charge_behaviour"))
        # Sign convention: positive = flowing INTO the battery (charging),
        # negative = flowing OUT (discharging). power_now is unsigned on ThinkPads.
        bat_w = power_uw / 1e6
        if status == "Discharging":
            bat_w = -bat_w
        elif status != "Charging":
            bat_w = 0.0
        return {
            "bat_present": True,
            "bat_status": status,
            "bat_pct": int(cap or 0),
            "bat_w": bat_w,
            "bat_energy_wh": energy_now / 1e6,
            "bat_full_wh": energy_full / 1e6,
            "bat_design_wh": energy_design / 1e6,
            "bat_v": (self._i("voltage_now") or 0) / 1e6,
            "cycle_count": int(self._i("cycle_count") or 0),
            "charge_behaviour": behaviour or "auto",
            "charge_start_threshold": int(self._i("charge_control_start_threshold") or 0),
            "charge_end_threshold": int(self._i("charge_control_end_threshold") or 100),
            "bat_model": self._s("model_name") or "",
            "bat_manufacturer": self._s("manufacturer") or "",
        }
