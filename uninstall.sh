#!/usr/bin/env bash
set -uo pipefail
gnome-extensions disable clearpower@lhc 2>/dev/null || true
rm -rf "$HOME/.local/share/gnome-shell/extensions/clearpower@lhc"
sudo systemctl disable --now clearpowerd 2>/dev/null || true
sudo rm -f /etc/systemd/system/clearpowerd.service /etc/dbus-1/system.d/org.clearpower.Daemon1.conf /usr/share/polkit-1/actions/org.clearpower.policy
sudo rm -rf /usr/local/lib/clearpower
sudo systemctl daemon-reload
echo "removed (state in /var/lib/clearpower kept; delete manually if desired)"
