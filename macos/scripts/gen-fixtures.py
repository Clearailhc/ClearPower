#!/usr/bin/env python3
"""Generate golden fixtures for the Swift port from the Python reference implementation.

Run from anywhere:  python3 macos/scripts/gen-fixtures.py
Writes macos/Tests/ClearPowerCoreTests/Fixtures/*.json
"""
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "daemon"))
OUT = os.path.join(HERE, "..", "Tests", "ClearPowerCoreTests", "Fixtures")
os.makedirs(OUT, exist_ok=True)

# Make gi optional: the modules we import must not touch GLib at import time.
from clearpowerd.smoothing import Ema  # noqa: E402
from clearpowerd.runtime import Runtime  # noqa: E402
from clearpowerd.history import History  # noqa: E402
from clearpowerd import sampler as sampler_mod  # noqa: E402
from clearpowerd import charge_control as cc_mod  # noqa: E402
from clearpowerd import display_cal as dc_mod  # noqa: E402


def dump(name, obj):
    with open(os.path.join(OUT, name + ".json"), "w") as f:
        json.dump(obj, f, indent=1, sort_keys=True)
    print("wrote", name)


# ---- EMA -------------------------------------------------------------------
def gen_ema():
    cases = []
    for tau in (0.0, 5.0):
        e = Ema(tau)
        steps = []
        seq = [(0.0, 10.0), (1.0, 10.0), (2.0, 0.0), (2.5, 0.0), (7.5, 20.0), (20.0, 20.0), (20.0, -3.0), (21.0, -3.0)]
        for t, x in seq:
            steps.append({"t": t, "x": x, "v": e.update(x, t)})
        e.reset()
        steps.append({"t": 30.0, "x": 5.0, "v": e.update(5.0, 30.0)})
        cases.append({"tau": tau, "steps": steps})
    dump("ema", cases)


# ---- Runtime ----------------------------------------------------------------
def gen_runtime():
    rt = Runtime()
    steps = []
    t = 0.0
    e = 60.0  # Wh
    full = 74.0
    limit = 90
    # 30 min discharging at 12 W (samples every 10 s), then 20 min charging at 30 W, then idle
    plan = [("Discharging", 12.0, 180), ("Charging", -30.0, 120), ("Not charging", 0.0, 12), ("Discharging", 8.0, 60)]
    i = 0
    for status, w, n in plan:
        for _ in range(n):
            t += 10.0
            e -= w * 10.0 / 3600.0
            rt.add(t, e, status)
            i += 1
            if i % 7 == 0 or i < 8:
                est = rt.estimate(e, full * limit / 100.0, abs(w) if w else 0.0)
                steps.append({"t": t, "energy_wh": e, "status": status, "fallback_w": abs(w), "target_wh": full * limit / 100.0, "out": est})
    dump("runtime", steps)


# ---- Power model (sampler arithmetic) ---------------------------------------
class _Fake:
    def __init__(self, fn):
        self.fn = fn

    def read(self):
        return self.fn()


def gen_power_model():
    cfg = {"battery": "BAT0", "procs_interval_s": 3, "smoothing_s": 5.0}
    s = sampler_mod.Sampler.__new__(sampler_mod.Sampler)
    s.cfg = cfg
    s.ema = {k: Ema(cfg["smoothing_s"]) for k in sampler_mod.SMOOTHED}
    s.last, s.raw = {}, {}
    s._thermal = {"temp_cpu": -1.0, "temp_gpu": -1.0, "temp_nvme": -1.0, "fan1": -1, "fan2": -1}
    s._thermal_at = -1e9
    s.hwmon = None
    s._thermal_read = lambda hot: s._thermal
    s.rapl = type("R", (), {"available": True})()
    s.procs = None
    sampler_mod.read_str = lambda p: ""

    class Cal:
        def __init__(self):
            self.emission = -1.0

        def emission_w(self, raw, now):
            return self.emission

        def snapshot_keys(self):
            return {}

    cal = Cal()
    s.display_cal = cal

    # (bat_w, psys, package, core, uncore, dram, on_ac, emission, display_on)
    seq = [
        # on AC, charging 10 W, full RAPL
        (10.0, 20.0, 8.0, 3.0, 2.0, 1.0, True, -1.0, True),
        (10.0, 20.0, 8.0, 3.0, 2.0, 1.0, True, -1.0, True),
        # weak adapter: battery assists 4 W
        (-4.0, 30.0, 12.0, 6.0, 3.0, 1.5, True, -1.0, True),
        (-4.0, 30.0, 12.0, 6.0, 3.0, 1.5, True, -1.0, True),
        # unplug: battery truth 15 W; psys still 14
        (-15.0, 14.0, 9.0, 4.0, 2.0, 1.0, False, 2.0, True),
        (-15.0, 14.0, 9.0, 4.0, 2.0, 1.0, False, 2.0, True),
        # display off
        (-15.0, 14.0, 9.0, 4.0, 2.0, 1.0, False, 2.0, False),
        # psys undershoots package+dram -> renormalised
        (-8.0, 7.0, 9.0, 4.0, 2.0, 1.0, False, 5.0, True),
        # no RAPL
        (-8.0, -1.0, -1.0, -1.0, -1.0, -1.0, False, -1.0, True),
        # AC, no psys, package only -> estimate
        (0.0, -1.0, 5.0, 2.0, 1.0, -1.0, True, -1.0, True),
        # nothing
        (0.0, -1.0, -1.0, -1.0, -1.0, -1.0, True, -1.0, True),
        # emission larger than rest -> clamped
        (2.0, 10.0, 8.0, 3.0, 2.0, 1.0, True, 50.0, True),
    ]
    out = []
    t = 1000.0
    import clearpowerd.sampler as sm
    for bat_w, psys, package, core, uncore, dram, on_ac, emission, disp_on in seq:
        t += 1.0
        cal.emission = emission
        s.battery = _Fake(lambda: {"bat_present": True, "bat_status": "Charging" if bat_w > 0 else "Discharging", "bat_pct": 50, "bat_w": bat_w})
        s.adapter = _Fake(lambda: {"on_ac": on_ac, "adapter_max_w": 65.0, "adapter_v": 20.0})
        s.rapl = _Fake(lambda: {k: v for k, v in {"psys": psys, "package": package, "core": core, "uncore": uncore, "dram": dram}.items() if v >= 0})
        s.rapl.available = True
        s.backlight = _Fake(lambda: {"brightness_pct": 50.0, "brightness_raw": 500, "display_on": disp_on})
        sm.time.monotonic = lambda: t
        snap = s.sample(True)
        keys = ["sys_w", "sys_source", "psys_w", "package_w", "cpu_w", "gpu_w", "soc_w", "mem_w", "rest_w", "display_w", "other_w", "bat_w", "bat_w_raw", "adapter_w"]
        out.append({"t": t, "in": {"bat_w": bat_w, "psys": psys, "package": package, "core": core, "uncore": uncore, "dram": dram, "on_ac": on_ac, "emission": emission, "display_on": disp_on},
                    "raw_rest": s.raw["rest"], "out": {k: snap[k] for k in keys}})
    dump("power_model", out)


# ---- Charge state machine ----------------------------------------------------
def gen_charge():
    writes = []
    fs = {"end": 100, "start": 95, "beh": "auto"}
    cc_mod.STATE_PATH = "/nonexistent/clearpower-fixture/state.json"

    def read_int(p):
        return fs["end"] if p.endswith("end_threshold") else fs["start"]

    def write_str(p, v):
        key = "end" if p.endswith("end_threshold") else ("start" if p.endswith("start_threshold") else "beh")
        fs[key] = v
        writes.append([key, v])

    cc_mod.read_int = read_int
    cc_mod.write_str = write_str
    cc_mod.read_bracketed = lambda p: ("auto", ["auto", "inhibit-charge", "force-discharge"])
    cc_mod.os.path.exists = lambda p: True
    cc_mod.os.makedirs = lambda *a, **k: (_ for _ in ()).throw(OSError("ro"))
    c = cc_mod.ChargeControl("BAT0", {"discharge_floor_pct": 20})
    log = []

    def rec(op, **kw):
        log.append({"op": op, "args": kw, "state": c.state(), "writes": [w for w in writes]})
        writes.clear()

    c.apply_startup(); rec("startup")
    c.set_limit(80); rec("set_limit", pct=80)
    c.set_limit(120); rec("set_limit", pct=120)
    c.set_limit(30); rec("set_limit", pct=30)
    c.set_limit(90); rec("set_limit", pct=90)
    c.start_topup(); rec("start_topup")
    c.tick({"bat_pct": 97, "bat_status": "Charging"}); rec("tick", pct=97, status="Charging")
    c.tick({"bat_pct": 100, "bat_status": "Full"}); rec("tick", pct=100, status="Full")
    c.start_discharge(0); rec("start_discharge", target=0)
    c.set_limit(95); rec("set_limit", pct=95)
    c.tick({"bat_pct": 96, "bat_status": "Discharging"}); rec("tick", pct=96, status="Discharging")
    c.tick({"bat_pct": 95, "bat_status": "Discharging"}); rec("tick", pct=95, status="Discharging")
    c.start_discharge(10); rec("start_discharge", target=10)
    c.cancel(); rec("cancel")
    c.start_topup(); rec("start_topup")
    c.shutdown(); rec("shutdown")
    dump("charge", log)


# ---- Display calibration -------------------------------------------------------
def gen_display_cal():
    class BL:
        base = "/fake"
        max = 1000

    dc_mod.CAL_PATH = "/nonexistent/clearpower-fixture/display_cal.json"
    dc_mod.os.makedirs = lambda *a, **k: (_ for _ in ()).throw(OSError("ro"))
    brightness = {"v": 700}
    dc_mod.read_int = lambda p: brightness["v"]
    d = dc_mod.DisplayCalibration(BL())
    sets = []
    d._set_raw = lambda v: (sets.append(int(v)), brightness.__setitem__("v", int(v)))
    # Drive a full sweep: rest = 3 + 6 * (level) + noise, sample every 0.5 s
    now = 100.0
    d.start(now, True)
    steps = []
    while d.state == "running":
        now += 0.5
        lvl = brightness["v"] / 1000.0
        noise = [0.3, -0.2, 0.1, 0.0, -0.1][int(now * 2) % 5]
        rest = 3.0 + 6.0 * lvl + noise
        d.tick(rest, now)
        steps.append({"now": now, "rest": rest, "progress": d.progress, "state": d.state})
    table = [list(p) for p in d.table]
    # interpolation cases
    interp = []
    for raw in (-5, 0, 5, 10, 100, 250, 333, 500, 999, 1000, 1500):
        interp.append({"raw": raw, "apl": -1, "w": d.emission_w(raw, now)})
    d.set_content(0.5, now)
    for raw in (0, 500, 1000):
        interp.append({"raw": raw, "apl": 0.5, "w": d.emission_w(raw, now)})
    d.set_content(1.5, now)
    interp.append({"raw": 500, "apl": 1.5, "w": d.emission_w(500, now)})
    interp.append({"raw": 500, "apl": 0.5, "w_stale": d.emission_w(500, now + 61)})
    dump("display_cal", {"sets": sets, "table": table, "rest0": d.rest0, "steps": steps[::7], "interp": interp, "final_state": d.state})


# ---- History ------------------------------------------------------------------
def gen_history():
    h = History(seconds=120, step_s=10)
    snaps = []
    for i in range(40):
        ts = 1000 + i * 3.3
        snap = {"ts": ts, "sys_w": 10 + i * 0.5, "soc_w": -1.0 if i % 5 == 0 else 3.0, "bat_w": -2.0, "bat_pct": 50,
                "display_w": 1.0, "temp_cpu": 40.0, "adapter_w": 0.0}
        h.add(snap)
        snaps.append(snap)
    out = {f: h.get(f, 60) for f in History.__module__ and ["sys_w", "soc_w", "bat_pct"]}
    out["all_sys"] = h.get("sys_w", 1e9)
    dump("history", {"snaps": snaps, "get": out})


if __name__ == "__main__":
    gen_ema(); gen_runtime(); gen_power_model(); gen_charge(); gen_display_cal(); gen_history()
