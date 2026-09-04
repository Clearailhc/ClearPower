"""Charge control state machine: limit (normal) / topup / discharge.

Owned by the daemon so it keeps working when the UI is closed.
"""
import json
import logging
import os

from .config import STATE_PATH
from .sysfs import write_str, read_int, read_bracketed

log = logging.getLogger("clearpower.charge")

MODES = ("limit", "topup", "discharge")


class ChargeControl:
    def __init__(self, battery_name, cfg):
        self.base = f"/sys/class/power_supply/{battery_name}"
        self.p_start = os.path.join(self.base, "charge_control_start_threshold")
        self.p_end = os.path.join(self.base, "charge_control_end_threshold")
        self.p_beh = os.path.join(self.base, "charge_behaviour")
        self.floor = int(cfg["discharge_floor_pct"])
        self.supported = os.path.exists(self.p_end)
        self.behaviour_supported = os.path.exists(self.p_beh)
        _, self.behaviours = read_bracketed(self.p_beh) if self.behaviour_supported else (None, [])
        self.mode = "limit"
        self.target = 0
        self.limit = 100
        self._load()

    # ---- persistence -------------------------------------------------
    def _load(self):
        try:
            with open(STATE_PATH) as f:
                st = json.load(f)
            self.limit = int(st.get("limit", 100))
        except (OSError, ValueError):
            cur = read_int(self.p_end) if self.supported else None
            self.limit = int(cur) if cur else 100
        self.limit = self._clamp_limit(self.limit)

    def _save(self):
        try:
            os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
            with open(STATE_PATH, "w") as f:
                json.dump({"limit": self.limit}, f)
        except OSError as e:
            log.warning("cannot save state: %s", e)

    # ---- helpers -----------------------------------------------------
    @staticmethod
    def _clamp_limit(v):
        v = max(50, min(100, int(v)))
        return v

    def _write_thresholds(self, start, end):
        """Write in an order that never violates start < end at any moment.

        Raising the limit: end first (start still below the old end), then start.
        Lowering it: start first (drops below the new end), then end.
        """
        if start >= end:
            start = max(end - 1, 0)
        cur_end = read_int(self.p_end) or 100
        order = (self.p_end, end), (self.p_start, start)
        if end <= cur_end:
            order = order[::-1]
        try:
            for path, value in order:
                write_str(path, value)
        except OSError:
            # firmware quirk: try the other order once before giving up
            for path, value in order[::-1]:
                write_str(path, value)

    def _write_behaviour(self, b):
        if not self.behaviour_supported:
            return
        if b not in self.behaviours:
            raise OSError(22, f"charge_behaviour {b!r} unsupported")
        write_str(self.p_beh, b)

    def _apply_limit(self):
        if not self.supported:
            return
        end = self.limit
        start = 95 if end >= 100 else end - 5
        self._write_thresholds(start, end)

    # ---- public API --------------------------------------------------
    def apply_startup(self):
        """Make sysfs consistent with saved state; special modes never survive restart."""
        if not self.supported:
            log.warning("charge thresholds not supported on this machine")
            return
        try:
            self._write_behaviour("auto")
            self._apply_limit()
        except OSError as e:
            log.warning("startup apply failed: %s", e)

    def shutdown(self):
        try:
            if self.mode != "limit":
                self._write_behaviour("auto")
                self._apply_limit()
        except OSError as e:
            log.warning("shutdown restore failed: %s", e)

    def set_limit(self, pct):
        prev = self.limit
        self.limit = self._clamp_limit(pct)
        try:
            if self.mode == "limit":
                self._apply_limit()
        except OSError:
            self.limit = prev  # keep state and sysfs consistent
            raise
        if self.mode == "discharge" and self.target < self.limit:
            self.target = self.limit
        self._save()

    def start_topup(self):
        self.mode = "topup"
        self._write_behaviour("auto")
        self._write_thresholds(95, 100)

    def start_discharge(self, target):
        if "force-discharge" not in self.behaviours:
            raise OSError(95, "force-discharge not supported")
        target = int(target) if target > 0 else self.limit
        self.target = max(self.floor, min(99, target))
        self.mode = "discharge"
        self._write_behaviour("force-discharge")

    def cancel(self):
        self.mode = "limit"
        self.target = 0
        self._write_behaviour("auto")
        self._apply_limit()

    def tick(self, snap):
        """Called every sample; ends special modes when their goal is reached."""
        if self.mode == "limit":
            return
        pct = snap.get("bat_pct", 0)
        status = snap.get("bat_status", "")
        try:
            if self.mode == "topup" and (status == "Full" or pct >= 100):
                log.info("top-up complete, restoring limit %d", self.limit)
                self.cancel()
            elif self.mode == "discharge" and pct <= self.target:
                log.info("discharge reached %d%%, restoring auto", pct)
                self.cancel()
        except OSError as e:
            log.warning("tick restore failed: %s", e)

    def state(self):
        return {"charge_mode": self.mode, "charge_limit": self.limit,
                "charge_target": self.target}
