"""Daemon configuration (optional /etc/clearpower/config.json) and state paths."""
import json
import os

CONFIG_PATH = "/etc/clearpower/config.json"
STATE_DIR = os.environ.get("STATE_DIRECTORY", "/var/lib/clearpower")
STATE_PATH = os.path.join(STATE_DIR, "state.json")

DEFAULTS = {
    "battery": "BAT0",
    "sample_interval_ms": 1000,     # while a client is watching (popover open)
    "idle_interval_ms": 2000,       # nobody asked for details in the last `hot_seconds`
    "hot_seconds": 6,
    "procs_interval_s": 3,
    # Display power model (estimate): W = p_min + (p_max - p_min) * brightness_fraction
    "display_p_min_w": 0.8,
    "display_p_max_w": 3.5,
    # Safety floor for force-discharge target
    "discharge_floor_pct": 20,
    "history_seconds": 24 * 3600,
    "history_step_s": 10,
}


def load():
    cfg = dict(DEFAULTS)
    try:
        with open(CONFIG_PATH) as f:
            cfg.update(json.load(f))
    except (OSError, ValueError):
        pass
    return cfg
