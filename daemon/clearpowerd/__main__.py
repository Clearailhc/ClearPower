"""clearpowerd entry point."""
import argparse
import logging
import signal
import sys

import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

from . import config  # noqa: E402
from .sampler import Sampler  # noqa: E402
from .charge_control import ChargeControl  # noqa: E402
from .history import History  # noqa: E402
from .dbus_service import Service  # noqa: E402

log = logging.getLogger("clearpower")


def main(argv=None):
    ap = argparse.ArgumentParser(prog="clearpowerd")
    ap.add_argument("--bus", choices=("system", "session"), default="system",
                    help="session = dev mode (no polkit, no root)")
    ap.add_argument("-v", "--verbose", action="store_true")
    ap.add_argument("--once", action="store_true", help="print one snapshot and exit")
    args = ap.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO,
                        format="%(levelname)s %(name)s: %(message)s")

    cfg = config.load()
    sampler = Sampler(cfg)
    charge = ChargeControl(cfg["battery"], cfg)
    history = History(cfg["history_seconds"], cfg["history_step_s"])

    if args.once:
        import json, time
        sampler.sample(); time.sleep(1.0)
        print(json.dumps(sampler.sample(), indent=1, default=str))
        return 0

    charge.apply_startup()
    bus = Gio.BusType.SESSION if args.bus == "session" else Gio.BusType.SYSTEM
    service = Service(sampler, charge, history, bus, insecure=(args.bus == "session"))
    loop = GLib.MainLoop()

    import time
    state = {"interval": cfg["sample_interval_ms"]}

    def tick():
        hot = (time.monotonic() - service.last_client_activity) < cfg["hot_seconds"]
        try:
            snap = sampler.sample(hot)
            charge.tick(snap)
            history.add(snap)
            service.emit_sample(snap)
        except Exception:  # keep the loop alive no matter what
            log.exception("sample failed")
        # Adaptive rate: 1 Hz while someone is looking at details, slower otherwise.
        # Special charge modes need prompt reaction, so keep them at full rate too.
        want = cfg["sample_interval_ms"] if (hot or charge.mode != "limit") else cfg["idle_interval_ms"]
        if want != state["interval"]:
            state["interval"] = want
            GLib.timeout_add(want, tick)
            return GLib.SOURCE_REMOVE
        return GLib.SOURCE_CONTINUE

    def stop(*_):
        log.info("shutting down")
        charge.shutdown()
        loop.quit()
        return GLib.SOURCE_REMOVE

    GLib.timeout_add(state["interval"], tick)
    try:
        gi.require_version("GLibUnix", "2.0")
        from gi.repository import GLibUnix
        add_signal = GLibUnix.signal_add
    except (ValueError, ImportError):
        add_signal = GLib.unix_signal_add
    add_signal(GLib.PRIORITY_HIGH, signal.SIGTERM, stop)
    add_signal(GLib.PRIORITY_HIGH, signal.SIGINT, stop)
    log.info("clearpowerd started (rapl=%s, thresholds=%s, behaviours=%s)",
             sampler.rapl.available, charge.supported, charge.behaviours)
    loop.run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
