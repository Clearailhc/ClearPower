"""Backlight brightness fraction (display power is estimated from it)."""
import glob
import os

from ..sysfs import read_int


class Backlight:
    def __init__(self):
        cands = sorted(glob.glob("/sys/class/backlight/*"))
        self.base = cands[0] if cands else None

    def read(self):
        if not self.base:
            return {"brightness_pct": -1.0, "display_on": True}
        b = read_int(os.path.join(self.base, "brightness")) or 0
        m = read_int(os.path.join(self.base, "max_brightness")) or 1
        bl_power = read_int(os.path.join(self.base, "bl_power"))
        on = (bl_power in (None, 0)) and b > 0
        return {"brightness_pct": 100.0 * b / m, "display_on": on}
