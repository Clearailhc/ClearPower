"""USB-C PD source readings (ucsi-source-psy-*) and legacy AC adapter online flag."""
import glob
import os

from ..sysfs import read_int, read_str


class Adapter:
    def __init__(self):
        self.sources = sorted(glob.glob("/sys/class/power_supply/ucsi-source-psy-*"))
        self.ac = [p for p in glob.glob("/sys/class/power_supply/*")
                   if read_str(os.path.join(p, "type")) == "Mains"]

    def read(self):
        # A real Mains supply (ThinkPad "AC") is authoritative; a USB-C partner that
        # merely advertises 5V/0.1A (phone, hub) must not count as being plugged in.
        mains = [read_int(os.path.join(p, "online")) == 1 for p in self.ac]
        on_ac = any(mains)
        max_w = 0.0
        volt = 0.0
        for src in self.sources:
            if read_int(os.path.join(src, "online")) != 1:
                continue
            if not self.ac:
                on_ac = True
            v = (read_int(os.path.join(src, "voltage_now")) or 0) / 1e6
            vmax = (read_int(os.path.join(src, "voltage_max")) or 0) / 1e6
            imax = (read_int(os.path.join(src, "current_max")) or 0) / 1e6
            inow = (read_int(os.path.join(src, "current_now")) or 0) / 1e6
            # Negotiated PDO: prefer max fields; fall back to *_now (often the PDO too).
            w = (vmax or v) * (imax or inow)
            if w > max_w:
                max_w = w
                volt = v or vmax
        return {"on_ac": on_ac, "adapter_max_w": max_w, "adapter_v": volt}
