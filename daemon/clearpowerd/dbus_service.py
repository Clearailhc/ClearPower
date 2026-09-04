"""D-Bus server for org.clearpower.Daemon1 (Gio, no python-dbus)."""
import logging
import os

from gi.repository import Gio, GLib

from . import BUS_NAME, OBJECT_PATH, IFACE, POLKIT_ACTION, VERSION
from . import polkit

log = logging.getLogger("clearpower.dbus")
XML_PATH = os.path.join(os.path.dirname(__file__), "..", "data", "org.clearpower.Daemon1.xml")
ERR_NOAUTH = IFACE + ".Error.NotAuthorized"
ERR_FAILED = IFACE + ".Error.Failed"


def to_variant(value):
    if isinstance(value, bool):
        return GLib.Variant("b", value)
    if isinstance(value, int):
        return GLib.Variant("i", value)
    if isinstance(value, float):
        return GLib.Variant("d", value)
    if isinstance(value, str):
        return GLib.Variant("s", value)
    if isinstance(value, list):  # top_procs: [(name, w, cpu)]
        return GLib.Variant("a(sdd)", [(n, float(w), float(c)) for n, w, c in value])
    return GLib.Variant("s", str(value))


def snapshot_variant(snap):
    return {k: to_variant(v) for k, v in snap.items()}


class Service:
    def __init__(self, sampler, charge, history, bus_type=Gio.BusType.SYSTEM, insecure=False):
        self.sampler = sampler
        self.charge = charge
        self.history = history
        self.insecure = insecure  # skip polkit (session-bus dev mode only)
        self.last_client_activity = 0.0  # monotonic time of the last detail request
        with open(XML_PATH) as f:
            self.node = Gio.DBusNodeInfo.new_for_xml(f.read())
        self.conn = None
        self.reg_id = 0
        self.owner_id = Gio.bus_own_name(bus_type, BUS_NAME, Gio.BusNameOwnerFlags.NONE,
                                         self._on_bus_acquired, self._on_name_acquired,
                                         self._on_name_lost)

    # ---- bus lifecycle -----------------------------------------------
    def _on_bus_acquired(self, conn, name):
        self.conn = conn
        self.reg_id = conn.register_object(OBJECT_PATH, self.node.interfaces[0],
                                           self._on_method, self._on_get_property, None)
        log.info("registered %s at %s", IFACE, OBJECT_PATH)

    def _on_name_acquired(self, conn, name):
        log.info("owning %s", name)

    def _on_name_lost(self, conn, name):
        log.error("lost bus name %s (is the dbus policy installed?)", name)

    # ---- properties ----------------------------------------------------
    def _props(self):
        st = self.charge.state()
        return {
            "Version": GLib.Variant("s", VERSION),
            "Snapshot": GLib.Variant("a{sv}", snapshot_variant(self.sampler.last)),
            "ChargeMode": GLib.Variant("s", st["charge_mode"]),
            "ChargeLimit": GLib.Variant("i", st["charge_limit"]),
            "ChargeTarget": GLib.Variant("i", st["charge_target"]),
            "ChargeControlSupported": GLib.Variant("b", self.charge.supported),
            "DischargeSupported": GLib.Variant("b", "force-discharge" in self.charge.behaviours),
        }

    def _on_get_property(self, conn, sender, path, iface, prop):
        return self._props().get(prop)

    def emit_charge_changed(self):
        if not self.conn:
            return
        p = self._props()
        changed = {k: p[k] for k in ("ChargeMode", "ChargeLimit", "ChargeTarget")}
        self.conn.emit_signal(None, OBJECT_PATH, "org.freedesktop.DBus.Properties",
                              "PropertiesChanged", GLib.Variant("(sa{sv}as)", (IFACE, changed, [])))

    # ---- sampling tick -------------------------------------------------
    def emit_sample(self, snap):
        if not self.conn:
            return
        payload = snapshot_variant(snap)
        payload.update({k: to_variant(v) for k, v in self.charge.state().items()})
        self.conn.emit_signal(None, OBJECT_PATH, IFACE, "Sample", GLib.Variant("(a{sv})", (payload,)))

    # ---- methods -------------------------------------------------------
    def _on_method(self, conn, sender, path, iface, method, params, invocation):
        if method in ("SetChargeLimit", "StartTopUp", "StartDischarge", "CancelSpecial"):
            def go(ok, err):
                if not ok:
                    invocation.return_dbus_error(ERR_NOAUTH, err or "Not authorized")
                    return
                try:
                    if method == "SetChargeLimit":
                        self.charge.set_limit(params.unpack()[0])
                    elif method == "StartTopUp":
                        self.charge.start_topup()
                    elif method == "StartDischarge":
                        self.charge.start_discharge(params.unpack()[0])
                    else:
                        self.charge.cancel()
                    log.info("%s%s by %s -> %s", method, params.unpack(), sender, self.charge.state())
                    invocation.return_value(None)
                    self.sampler.battery.invalidate()
                    self.emit_charge_changed()
                except OSError as e:
                    msg = f"{e.strerror or e} (errno {e.errno})"
                    log.warning("%s failed: %s", method, msg)
                    invocation.return_dbus_error(ERR_FAILED, msg)
            if self.insecure:
                go(True, None)
            else:
                polkit.check(conn, sender, POLKIT_ACTION, go)
        elif method == "GetTopProcesses":
            import time
            self.last_client_activity = time.monotonic()
            n = params.unpack()[0]
            soc_w = self.sampler.last.get("soc_w", -1.0)
            procs = [(nm, float(w), float(c)) for nm, w, c in self.sampler.procs.maybe_sample(soc_w, n)]
            invocation.return_value(GLib.Variant("(a(sdd))", (procs,)))
        elif method == "GetHistory":
            field, secs = params.unpack()
            invocation.return_value(GLib.Variant("(a(dd))", (self.history.get(field, secs),)))
        else:
            invocation.return_dbus_error("org.freedesktop.DBus.Error.UnknownMethod", method)
