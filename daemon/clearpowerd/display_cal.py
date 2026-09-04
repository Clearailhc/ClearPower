"""Display power calibration for panels without a power sensor.

First principles: the only knobs we control are brightness and (indirectly)
picture content; the only truth we have is platform power (psys) minus the
RAPL-measured SoC and DRAM, i.e. "everything else".  Sweeping brightness while
the rest of the machine is idle yields the panel's emission power per level.
For OLED panels the emission also scales with the average picture luminance,
so the table is normalised to the content level measured during calibration and
re-scaled at runtime with the live content level supplied by the UI.
"""
import json
import logging
import os
import statistics
import time

from .config import STATE_DIR
from .sysfs import read_int, write_str

log = logging.getLogger("clearpower.display")
CAL_PATH = os.path.join(STATE_DIR, "display_cal.json")
LEVELS = (0.0, 0.01, 0.1, 0.25, 0.5, 0.75, 1.0)
SETTLE_S = 1.5
SAMPLES = 5
SAMPLE_GAP_S = 1.0


class DisplayCalibration:
    def __init__(self, backlight):
        self.bl = backlight  # sources.backlight.Backlight (has base path)
        self.state = "idle"  # idle | running | done | failed
        self.progress = 0.0
        self.message = ""
        self.table = []      # [(raw_brightness, emission_w)], monotone
        self.apl_cal = -1.0  # content level during calibration (-1 = unknown)
        self.rest0 = -1.0
        self.calibrated_at = 0.0
        self.apl = -1.0      # latest content level from the UI
        self.apl_ts = 0.0
        self._load()
        self._run = None

    # ---- persistence ------------------------------------------------------
    def _load(self):
        try:
            with open(CAL_PATH) as f:
                d = json.load(f)
            self.table = [tuple(p) for p in d["table"]]
            self.apl_cal = 1.0  # table is normalised to a white screen (see _finish)
            self.rest0 = float(d.get("rest0", -1.0))
            self.calibrated_at = float(d.get("calibrated_at", 0.0))
            self.state = "done" if self.table else "idle"
        except (OSError, ValueError, KeyError):
            pass

    def _save(self):
        try:
            os.makedirs(STATE_DIR, exist_ok=True)
            with open(CAL_PATH, "w") as f:
                json.dump({"table": self.table, "apl_cal": self.apl_cal, "apl_measured": getattr(self, "apl_measured", -1.0), "rest0": self.rest0,
                           "calibrated_at": self.calibrated_at, "max_brightness": self.bl.max}, f)
        except OSError as e:
            log.warning("cannot save calibration: %s", e)

    @property
    def calibrated(self):
        return len(self.table) >= 2

    # ---- content level from the UI -------------------------------------------
    def set_content(self, apl, now):
        self.apl = max(0.0, min(1.0, float(apl)))
        self.apl_ts = now
        if self.state == "running" and self._run is not None:
            self._run["apls"].append(self.apl)

    def _fresh_apl(self, now):
        return self.apl if (self.apl >= 0 and now - self.apl_ts < 60) else -1.0

    # ---- runtime estimate -------------------------------------------------
    def emission_w(self, raw_brightness, now):
        if not self.calibrated or raw_brightness is None:
            return -1.0
        pts = self.table
        if raw_brightness <= pts[0][0]:
            e = pts[0][1]
        elif raw_brightness >= pts[-1][0]:
            e = pts[-1][1]
        else:
            e = pts[-1][1]
            for (b0, e0), (b1, e1) in zip(pts, pts[1:]):
                if b0 <= raw_brightness <= b1:
                    e = e0 + (e1 - e0) * (raw_brightness - b0) / max(b1 - b0, 1)
                    break
        apl = self._fresh_apl(now)
        if apl >= 0 and self.apl_cal > 0.02:
            e *= max(0.02, min(1.2, apl / self.apl_cal))
        return e

    # ---- brightness control -----------------------------------------------
    def _set_raw(self, value):
        path = os.path.join(self.bl.base, "brightness")
        try:
            write_str(path, int(value))
            return
        except PermissionError:
            pass
        # dev mode (unprivileged): ask logind on behalf of the active session
        from gi.repository import Gio, GLib
        bus = Gio.bus_get_sync(Gio.BusType.SYSTEM, None)
        bus.call_sync("org.freedesktop.login1", "/org/freedesktop/login1/session/auto",
                      "org.freedesktop.login1.Session", "SetBrightness",
                      GLib.Variant("(ssu)", ("backlight", os.path.basename(self.bl.base), int(value))),
                      None, Gio.DBusCallFlags.NONE, 5000, None)

    # ---- state machine ------------------------------------------------------
    def start(self, now, rapl_ok):
        if self.state == "running":
            return
        if not self.bl.base:
            self.state, self.message = "failed", "no backlight device"
            return
        if not rapl_ok:
            self.state, self.message = "failed", "RAPL not readable; cannot isolate the display"
            return
        orig = read_int(os.path.join(self.bl.base, "brightness")) or self.bl.max
        self._run = {"orig": orig, "idx": 0, "phase_t": now, "samples": [], "results": [], "apls": []}
        self.state, self.progress, self.message = "running", 0.0, ""
        self._apply_level(now)
        log.info("display calibration started (orig brightness %d)", orig)

    def _apply_level(self, now):
        r = self._run
        raw = int(round(LEVELS[r["idx"]] * self.bl.max))
        try:
            self._set_raw(raw)
        except Exception as e:  # noqa: BLE001
            self._finish(failed=f"cannot set brightness: {e}")
            return
        r["level_raw"] = raw
        r["phase_t"] = now
        r["samples"] = []
        r["last_sample_t"] = 0.0

    def tick(self, rest_raw_w, now):
        if self.state != "running" or self._run is None:
            return
        r = self._run
        if now - r["phase_t"] < SETTLE_S:
            return
        if rest_raw_w is None or rest_raw_w < 0:
            self._finish(failed="platform power unavailable")
            return
        if now - r["last_sample_t"] >= SAMPLE_GAP_S:
            r["samples"].append(rest_raw_w)
            r["last_sample_t"] = now
        self.progress = (r["idx"] + len(r["samples"]) / SAMPLES) / len(LEVELS)
        if len(r["samples"]) >= SAMPLES:
            r["results"].append((r["level_raw"], statistics.median(r["samples"])))
            r["idx"] += 1
            if r["idx"] >= len(LEVELS):
                self._finish()
            else:
                self._apply_level(now)

    def cancel(self):
        if self.state == "running":
            self._finish(failed="cancelled")

    def _finish(self, failed=None):
        r = self._run or {}
        try:
            if "orig" in r:
                self._set_raw(r["orig"])
        except Exception as e:  # noqa: BLE001
            log.warning("could not restore brightness: %s", e)
        if failed:
            self.state, self.message = ("done" if self.calibrated else "failed"), failed
            log.warning("display calibration aborted: %s", failed)
        else:
            res = sorted(r["results"])
            rest0 = res[0][1]
            table, running_max = [], 0.0
            for raw, rest in res:
                running_max = max(running_max, rest - rest0)  # emission can only grow with brightness
                table.append((raw, round(running_max, 3)))
            self.table, self.rest0 = table, round(rest0, 3)
            # The UI shows a white screen during the sweep, so by construction the table is
            # the emission for content level 1.0. The measured level is only a sanity check.
            apls = r.get("apls", [])
            measured = statistics.median(apls[len(apls) // 3:]) if len(apls) >= 2 else -1.0
            if 0 <= measured < 0.8:
                log.warning("calibration screen content level was %.2f (expected ~1.0); "
                            "was the white screen visible on the panel?", measured)
            self.apl_cal = 1.0
            self.apl_measured = measured
            self.calibrated_at = time.time()
            self.state, self.progress, self.message = "done", 1.0, ""
            self._save()
            log.info("display calibration done: %s (rest0 %.2f W, apl_cal %.3f)", table, rest0, self.apl_cal)
        self._run = None

    def snapshot_keys(self):
        return {
            "display_calibrated": self.calibrated,
            "calib_state": self.state,
            "calib_progress": float(self.progress),
            "calib_message": self.message,
            "calibrated_at": float(self.calibrated_at),
            "content_apl": float(self.apl),
        }
