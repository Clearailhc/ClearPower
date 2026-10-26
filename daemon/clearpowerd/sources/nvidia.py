"""NVIDIA GPU power and temperature readings via libnvidia-ml.so.1."""
import ctypes
import glob
import os


class NvidiaGpu:
    def __init__(self):
        self._dev_path = None
        for d in glob.glob("/sys/bus/pci/devices/*"):
            vpath = os.path.join(d, "vendor")
            cpath = os.path.join(d, "class")
            if os.path.exists(vpath) and os.path.exists(cpath):
                try:
                    with open(vpath) as vf, open(cpath) as cf:
                        if vf.read().strip() == "0x10de" and cf.read().strip().startswith("0x03"):
                            self._dev_path = d
                            break
                except OSError:
                    pass
        self._rpath = os.path.join(self._dev_path, "power", "runtime_status") if self._dev_path else None
        self._nvml = None
        self._dev = None
        self._last_power_w = 0.0
        self._max_power_w = 150.0
        self.available = self._dev_path is not None

    def is_suspended(self):
        if not self._rpath or not os.path.exists(self._rpath):
            return False
        try:
            with open(self._rpath) as f:
                return f.read().strip() == "suspended"
        except OSError:
            return False

    def _ensure_nvml(self):
        if self._dev is not None:
            return True
        try:
            self._nvml = ctypes.CDLL("libnvidia-ml.so.1")
            self._nvml.nvmlInit.restype = ctypes.c_int
            self._nvml.nvmlDeviceGetHandleByIndex.argtypes = [ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p)]
            self._nvml.nvmlDeviceGetHandleByIndex.restype = ctypes.c_int
            self._nvml.nvmlDeviceGetPowerUsage.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint)]
            self._nvml.nvmlDeviceGetPowerUsage.restype = ctypes.c_int
            self._nvml.nvmlDeviceGetTemperature.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.POINTER(ctypes.c_uint)]
            self._nvml.nvmlDeviceGetTemperature.restype = ctypes.c_int
            if self._nvml.nvmlInit() == 0:
                self._dev = ctypes.c_void_p()
                if self._nvml.nvmlDeviceGetHandleByIndex(0, ctypes.byref(self._dev)) == 0:
                    try:
                        pmin = ctypes.c_uint()
                        pmax = ctypes.c_uint()
                        self._nvml.nvmlDeviceGetPowerManagementLimitConstraints.argtypes = [
                            ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint)
                        ]
                        self._nvml.nvmlDeviceGetPowerManagementLimitConstraints.restype = ctypes.c_int
                        if self._nvml.nvmlDeviceGetPowerManagementLimitConstraints(self._dev, ctypes.byref(pmin), ctypes.byref(pmax)) == 0:
                            self._max_power_w = max(pmax.value / 1000.0 * 1.2, 100.0)
                    except Exception:
                        pass
                    return True
        except Exception:
            pass
        self._dev = None
        return False

    def read(self):
        if not self.available:
            return {"gpu_power_w": 0.0, "temp_gpu": -1.0}
        # If suspended in D3cold/D3hot, do NOT touch NVML to avoid waking the card up!
        if self.is_suspended():
            self._last_power_w = 0.0
            return {"gpu_power_w": 0.0, "temp_gpu": -1.0}
        if not self._ensure_nvml():
            return {"gpu_power_w": 0.0, "temp_gpu": -1.0}
        power_w = 0.0
        temp_gpu = -1.0
        try:
            mw = ctypes.c_uint()
            if self._nvml.nvmlDeviceGetPowerUsage(self._dev, ctypes.byref(mw)) == 0:
                val_w = mw.value / 1000.0
                # Filter out EC/SBIOS phantom reading glitch (e.g. 752W / 753W on mobile GPUs)
                if 0.0 <= val_w <= self._max_power_w:
                    power_w = val_w
                    self._last_power_w = val_w
                else:
                    power_w = self._last_power_w
        except Exception:
            pass
        try:
            temp = ctypes.c_uint()
            if self._nvml.nvmlDeviceGetTemperature(self._dev, 0, ctypes.byref(temp)) == 0:
                temp_gpu = float(temp.value)
        except Exception:
            pass
        return {"gpu_power_w": power_w, "temp_gpu": temp_gpu}
