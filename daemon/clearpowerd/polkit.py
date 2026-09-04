"""Asynchronous polkit authorization check for a D-Bus caller."""
import logging

from gi.repository import Gio, GLib

log = logging.getLogger("clearpower.polkit")
ALLOW_USER_INTERACTION = 1


def check(conn, sender, action_id, callback):
    """callback(authorized: bool, error: str|None)."""
    subject = ("system-bus-name", {"name": GLib.Variant("s", sender)})
    args = GLib.Variant("((sa{sv})sa{ss}us)", (subject, action_id, {}, ALLOW_USER_INTERACTION, ""))

    def done(source, res):
        try:
            reply = source.call_finish(res)
            ok, _challenge, _details = reply.unpack()[0]
            callback(bool(ok), None)
        except GLib.Error as e:
            log.warning("polkit check failed: %s", e.message)
            callback(False, e.message)

    conn.call("org.freedesktop.PolicyKit1", "/org/freedesktop/PolicyKit1/Authority",
              "org.freedesktop.PolicyKit1.Authority", "CheckAuthorization", args,
              GLib.VariantType("((bba{ss}))"), Gio.DBusCallFlags.NONE, -1, None, done)
