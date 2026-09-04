"""Intel RAPL powercap energy counters -> watts (root required for energy_uj)."""
import glob
import os
import time

from ..sysfs import read_int, read_str


class Rapl:
    def __init__(self):
        self.domains = {}  # name -> path
        for d in sorted(glob.glob("/sys/class/powercap/intel-rapl:*")):
            base = os.path.basename(d)
            if base.startswith("intel-rapl-mmio"):
                continue
            name = read_str(os.path.join(d, "name"))
            if not name:
                continue
            key = name.split("-")[0]  # package-0 -> package
            self.domains[key] = d
        self._wrap = {k: read_int(os.path.join(p, "max_energy_range_uj")) or (1 << 32)
                      for k, p in self.domains.items()}
        self._last = {}  # key -> (t, uj)
        first = next(iter(self.domains.values()), None)
        self.available = bool(first) and read_int(os.path.join(first, "energy_uj")) is not None

    def read(self):
        now = time.monotonic()
        out = {}
        for key, path in self.domains.items():
            uj = read_int(os.path.join(path, "energy_uj"))
            if uj is None:
                continue
            prev = self._last.get(key)
            self._last[key] = (now, uj)
            if prev is None:
                continue
            dt = now - prev[0]
            if dt <= 0:
                continue
            duj = uj - prev[1]
            if duj < 0:
                duj += self._wrap[key]
            out[key] = duj / dt / 1e6
        return out  # keys: package, core, uncore, dram, psys (subset)
