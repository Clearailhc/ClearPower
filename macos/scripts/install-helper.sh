#!/bin/sh
# Install or remove the ClearPower privileged helper. Run as root (the app invokes it
# through an administrator-password prompt). Mirrors install.sh on Linux.
#   install-helper.sh install <helper-binary> <launchd-plist>
#   install-helper.sh remove
set -e
LABEL=org.clearpower.helper
BIN=/Library/PrivilegedHelperTools/$LABEL
PLIST=/Library/LaunchDaemons/$LABEL.plist

case "$1" in
  install)
    SRC="$2"; PSRC="$3"
    [ -f "$SRC" ] && [ -f "$PSRC" ] || { echo "usage: $0 install <helper> <plist>" >&2; exit 2; }
    mkdir -p /Library/PrivilegedHelperTools /Library/Logs/ClearPower "/Library/Application Support/ClearPower"
    launchctl bootout system/$LABEL 2>/dev/null || true
    cp -f "$SRC" "$BIN"; chown root:wheel "$BIN"; chmod 755 "$BIN"
    cp -f "$PSRC" "$PLIST"; chown root:wheel "$PLIST"; chmod 644 "$PLIST"
    if [ -f "$SRC.requirement" ]; then cp -f "$SRC.requirement" "$BIN.requirement"; else rm -f "$BIN.requirement"; fi
    launchctl bootstrap system "$PLIST"
    launchctl kickstart -k system/$LABEL
    echo "installed $LABEL"
    ;;
  remove)
    launchctl bootout system/$LABEL 2>/dev/null || true
    rm -f "$BIN" "$BIN.requirement" "$PLIST"
    echo "removed $LABEL"
    ;;
  *)
    echo "usage: $0 install <helper> <plist> | remove" >&2; exit 2;;
esac
