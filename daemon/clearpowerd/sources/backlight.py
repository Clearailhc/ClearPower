"""Backlight brightness (raw and fraction). Display power is derived from it via calibration."""
import glob
import os

from ..sysfs import read_int


class Backlight:
    def __init__(self):
        cands = sorted(glob.glob("/sys/class/backlight/*"))
        self.base = cands[0] if cands else None
        self.max = (read_int(os.path.join(self.base, "max_brightness")) if self.base else None) or 1

    def read(self):
        if not self.base:
            return {"brightness_pct": -1.0, "brightness_raw": -1, "display_on": True}
        b = read_int(os.path.join(self.base, "actual_brightness"))
        if b is None:
            b = read_int(os.path.join(self.base, "brightness")) or 0
        bl_power = read_int(os.path.join(self.base, "bl_power"))
        on = (bl_power in (None, 0)) and b > 0
        return {"brightness_pct": 100.0 * b / self.max, "brightness_raw": int(b), "display_on": on}
