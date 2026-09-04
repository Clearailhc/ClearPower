"""Temperatures and fans from hwmon (thinkpad, coretemp, nvme)."""
import glob
import os

from ..sysfs import read_int, read_str


def _find(name):
    for h in glob.glob("/sys/class/hwmon/hwmon*"):
        if read_str(os.path.join(h, "name")) == name:
            return h
    return None


def _labelled(h, prefix):
    """Return {label: input_path} for temp*/fan* entries under hwmon dir h."""
    out = {}
    if not h:
        return out
    for inp in glob.glob(os.path.join(h, f"{prefix}*_input")):
        stem = inp[: -len("_input")]
        label = read_str(stem + "_label") or os.path.basename(stem)
        out[label] = inp
    return out


class Hwmon:
    def __init__(self):
        self.thinkpad = _find("thinkpad")
        self.coretemp = _find("coretemp")
        self.nvme = _find("nvme")
        self.tp_temps = _labelled(self.thinkpad, "temp")
        self.tp_fans = _labelled(self.thinkpad, "fan")
        self.core_temps = _labelled(self.coretemp, "temp")
        self.nvme_temps = _labelled(self.nvme, "temp")

    @staticmethod
    def _c(path):
        v = read_int(path)
        return v / 1000.0 if v is not None else -1.0

    def read(self):
        cpu = self._c(self.tp_temps["CPU"]) if "CPU" in self.tp_temps else -1.0
        if cpu < 0:
            for lbl, p in self.core_temps.items():
                if lbl.startswith("Package"):
                    cpu = self._c(p)
                    break
        gpu = self._c(self.tp_temps["GPU"]) if "GPU" in self.tp_temps else -1.0
        nvme = -1.0
        for lbl in ("Composite", "temp1"):
            if lbl in self.nvme_temps:
                nvme = self._c(self.nvme_temps[lbl])
                break
        fans = sorted(self.tp_fans.items())
        fan1 = read_int(fans[0][1]) if len(fans) > 0 else -1
        fan2 = read_int(fans[1][1]) if len(fans) > 1 else -1
        return {
            "temp_cpu": cpu, "temp_gpu": gpu, "temp_nvme": nvme,
            "fan1": int(fan1 if fan1 is not None else -1),
            "fan2": int(fan2 if fan2 is not None else -1),
        }
