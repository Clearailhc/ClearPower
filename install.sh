#!/usr/bin/env bash
# Install ClearPower: root daemon (systemd + D-Bus + polkit) and the GNOME Shell extension.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
UUID=clearpower@lhc
EXT_DEST="$HOME/.local/share/gnome-shell/extensions/$UUID"

echo "[1/2] daemon -> /usr/local/lib/clearpower (sudo)"
sudo install -d /usr/local/lib/clearpower
sudo rm -rf /usr/local/lib/clearpower/clearpowerd /usr/local/lib/clearpower/data
sudo cp -r "$HERE/daemon/clearpowerd" "$HERE/daemon/data" /usr/local/lib/clearpower/
sudo find /usr/local/lib/clearpower -name __pycache__ -type d -exec rm -rf {} + || true
sudo install -m644 "$HERE/packaging/clearpowerd.service" /etc/systemd/system/clearpowerd.service
sudo install -m644 "$HERE/packaging/org.clearpower.Daemon1.conf" /etc/dbus-1/system.d/org.clearpower.Daemon1.conf
sudo install -m644 "$HERE/packaging/org.clearpower.policy" /usr/share/polkit-1/actions/org.clearpower.policy
sudo busctl --system call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ReloadConfig >/dev/null || true
sudo systemctl daemon-reload
sudo systemctl enable clearpowerd >/dev/null
sudo systemctl restart clearpowerd
sleep 1
systemctl --no-pager --lines=5 status clearpowerd || true

echo "[2/2] extension -> $EXT_DEST"
rm -rf "$EXT_DEST"
mkdir -p "$EXT_DEST"
cp -r "$HERE/extension/$UUID/." "$EXT_DEST/"
glib-compile-schemas "$EXT_DEST/schemas"
gnome-extensions enable "$UUID" 2>/dev/null || true

cat <<MSG

Done. If this is the first install (or extension files changed) on Wayland,
log out and back in so GNOME Shell loads the extension.
Check: busctl --system get-property org.clearpower.Daemon1 /org/clearpower/Daemon org.clearpower.Daemon1 Snapshot
Logs:  journalctl -u clearpowerd -f
MSG
