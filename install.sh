#!/usr/bin/env bash
# Build the .deb from this checkout and install it (asks for sudo). Cleans up an older manual install.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
UUID=clearpower@lhc
DEB="$("$HERE/packaging/deb/build.sh")"
echo "built $DEB"

# Remove the pre-package manual layout if present (it would shadow the packaged files).
if [ -e /etc/systemd/system/clearpowerd.service ] || [ -d /usr/local/lib/clearpower ]; then
  echo "removing legacy manual install"
  sudo systemctl disable --now clearpowerd >/dev/null 2>&1 || true
  sudo rm -rf /etc/systemd/system/clearpowerd.service /etc/dbus-1/system.d/org.clearpower.Daemon1.conf \
              /usr/share/polkit-1/actions/org.clearpower.policy /usr/local/lib/clearpower
  sudo systemctl daemon-reload
fi
if [ -d "$HOME/.local/share/gnome-shell/extensions/$UUID" ]; then
  echo "removing per-user extension copy (the package installs it system-wide)"
  gnome-extensions disable "$UUID" 2>/dev/null || true
  rm -rf "$HOME/.local/share/gnome-shell/extensions/$UUID"
fi

sudo apt install -y "$DEB"
echo
echo "Installed. Log out and back in once; the indicator is enabled automatically on login."
echo "Afterwards: 'clearpower status' shows daemon/extension state, 'clearpower' opens settings."
