"""The thermal/fan cache must never publish a reading it is no longer refreshing.

Regression test for the reported bug: the fan ran at 2200 rpm, stopped, and the popover
still showed 2200. The daemon refreshes temperatures/fans only while someone is looking
(the popover is open), and it used to keep serving the cached fan value the rest of the
time. Linux and macOS share this design; this is the Python reference they mirror.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from clearpowerd.sampler import Sampler  # noqa: E402

UNKNOWN = {"temp_cpu": -1.0, "temp_gpu": -1.0, "temp_nvme": -1.0, "fan1": -1, "fan2": -1}


class FakeHwmon:
    """Stands in for sources/hwmon.py; counts how often the EC was actually read."""

    def __init__(self, fan=2200):
        self.calls = 0
        self.value = fan

    def read(self):
        self.calls += 1
        return {"temp_cpu": 50.0, "temp_gpu": 45.0, "temp_nvme": 44.0,
                "fan1": self.value, "fan2": self.value}


def make_sampler():
    # Bypass __init__: it opens the real hwmon, battery, RAPL and backlight. We only want
    # to exercise the thermal cache, exactly as macos/scripts/gen-fixtures.py does.
    s = Sampler.__new__(Sampler)
    s.hwmon = FakeHwmon()
    s._thermal = dict(UNKNOWN)
    s._thermal_at = -1e9
    s._thermal_hot = False
    return s


class ThermalSemantics(unittest.TestCase):
    def test_cold_reports_unknown_without_touching_the_hardware(self):
        s = make_sampler()
        r = s._thermal_read(False)
        self.assertEqual((r["fan1"], r["fan2"]), (-1, -1))
        self.assertEqual(s.hwmon.calls, 0)

    def test_first_hot_sample_reads_immediately(self):
        s = make_sampler()
        s._thermal_read(False)
        r = s._thermal_read(True)
        self.assertEqual(r["fan1"], 2200)
        self.assertEqual(s.hwmon.calls, 1)

    def test_hot_keeps_the_three_second_ttl(self):
        s = make_sampler()
        s._thermal_read(True)
        s._thermal_read(True)
        self.assertEqual(s.hwmon.calls, 1)
        s._thermal_at -= 3.1
        s.hwmon.value = 0
        r = s._thermal_read(True)
        self.assertEqual(r["fan1"], 0)
        self.assertEqual(s.hwmon.calls, 2)

    def test_cold_keeps_temperature_but_not_the_fan(self):
        s = make_sampler()
        s._thermal_read(True)
        r = s._thermal_read(False)
        self.assertEqual(r["fan1"], -1)
        # temp_cpu is in History.FIELDS; a -1 while idle would put holes in the curve.
        self.assertEqual(r["temp_cpu"], 50.0)

    def test_reopen_reads_even_inside_the_ttl(self):
        s = make_sampler()
        s._thermal_read(True)
        s._thermal_read(False)
        s.hwmon.value = 1800
        r = s._thermal_read(True)
        self.assertEqual(r["fan1"], 1800)
        self.assertEqual(s.hwmon.calls, 2)

    def test_cold_dict_is_a_copy_of_the_cache(self):
        s = make_sampler()
        s._thermal_read(True)
        s._thermal_read(False)["temp_cpu"] = 999.0
        self.assertEqual(s._thermal["temp_cpu"], 50.0)

    def test_stopped_fan_is_not_republished_while_idle(self):
        s = make_sampler()
        s._thermal_read(True)          # popover was open: the fan read 2200
        s.hwmon.value = 0              # then it stopped
        for _ in range(6):             # idle ticks the daemon keeps emitting
            r = s._thermal_read(False)
        self.assertEqual(r["fan1"], -1)


if __name__ == "__main__":
    unittest.main()
