#!/usr/bin/env bash
# Build dist/clearpower_<version>_all.deb with plain dpkg-deb (no debhelper needed).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
UUID=clearpower@lhc
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
PKG="$STAGE/clearpower_${VERSION}_all"
D="$PKG"

inst() { install -D -m "$1" "$2" "$3"; }

# daemon
mkdir -p "$D/usr/lib/clearpower"
cp -r "$ROOT/daemon/clearpowerd" "$ROOT/daemon/data" "$D/usr/lib/clearpower/"
find "$D/usr/lib/clearpower" -name __pycache__ -type d -prune -exec rm -rf {} +
inst 0755 "$ROOT/bin/clearpower" "$D/usr/bin/clearpower"
inst 0644 "$ROOT/packaging/clearpowerd.service" "$D/usr/lib/systemd/system/clearpowerd.service"
inst 0644 "$ROOT/packaging/org.clearpower.Daemon1.conf" "$D/usr/share/dbus-1/system.d/org.clearpower.Daemon1.conf"
inst 0644 "$ROOT/packaging/org.clearpower.policy" "$D/usr/share/polkit-1/actions/org.clearpower.policy"

# GNOME Shell extension (system-wide; schema goes to the system schema dir)
mkdir -p "$D/usr/share/gnome-shell/extensions/$UUID"
cp -r "$ROOT/extension/$UUID/." "$D/usr/share/gnome-shell/extensions/$UUID/"
rm -rf "$D/usr/share/gnome-shell/extensions/$UUID/schemas"
inst 0644 "$ROOT/extension/$UUID/schemas/org.gnome.shell.extensions.clearpower.gschema.xml" \
     "$D/usr/share/glib-2.0/schemas/org.gnome.shell.extensions.clearpower.gschema.xml"

# desktop integration
inst 0644 "$ROOT/packaging/org.clearpower.ClearPower.desktop" "$D/usr/share/applications/org.clearpower.ClearPower.desktop"
inst 0644 "$ROOT/packaging/org.clearpower.ClearPower-autostart.desktop" "$D/etc/xdg/autostart/org.clearpower.ClearPower.desktop"
inst 0644 "$ROOT/icons/org.clearpower.ClearPower.svg" "$D/usr/share/icons/hicolor/scalable/apps/org.clearpower.ClearPower.svg"
inst 0644 "$ROOT/icons/clearpower-symbolic.svg" "$D/usr/share/icons/hicolor/symbolic/apps/org.clearpower.ClearPower-symbolic.svg"
inst 0644 "$ROOT/README.md" "$D/usr/share/doc/clearpower/README.md"
[ -f "$ROOT/LICENSE" ] && inst 0644 "$ROOT/LICENSE" "$D/usr/share/doc/clearpower/copyright"

# control files
mkdir -p "$D/DEBIAN"
sed "s/@VERSION@/$VERSION/" "$ROOT/packaging/deb/control.in" > "$D/DEBIAN/control"
for s in postinst prerm postrm; do install -m 0755 "$ROOT/packaging/deb/$s" "$D/DEBIAN/$s"; done
echo "/etc/xdg/autostart/org.clearpower.ClearPower.desktop" > "$D/DEBIAN/conffiles"
find "$D" -type f -not -path "$D/DEBIAN/*" -printf '%P\n' | sort | while read -r f; do
  (cd "$D" && md5sum "$f"); done > "$D/DEBIAN/md5sums"
chmod -R go-w "$D"

mkdir -p "$ROOT/dist"
OUT="$ROOT/dist/clearpower_${VERSION}_all.deb"
dpkg-deb --root-owner-group --build "$PKG" "$OUT" >/dev/null
echo "$OUT"
