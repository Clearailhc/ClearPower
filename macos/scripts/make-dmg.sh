#!/bin/sh
# Package dist/ClearPower.app into dist/ClearPower-<version>-arm64.dmg with a background
# that says "drag to Applications" and a fixed icon layout.
#   NOTARIZE=1 with a Developer ID build also submits the DMG to Apple and staples the ticket
#   (needs `xcrun notarytool store-credentials clearpower` done once).
set -e
cd "$(dirname "$0")/.."
VERSION=$(tr -d '[:space:]' < ../VERSION)
APP=dist/ClearPower.app
DMG=dist/ClearPower-$VERSION-arm64.dmg
VOL="ClearPower $VERSION"
[ -d "$APP" ] || { echo "run scripts/build-app.sh first" >&2; exit 1; }

STAGE=$(mktemp -d)
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
mkdir -p "$STAGE/.background"
python3 scripts/dmg-background.py "$STAGE/.background/background.png"

# 1. writable image, 2. lay it out with Finder, 3. compress.
RW=$(mktemp -d)/rw.dmg
rm -f "$DMG"
hdiutil create -volname "$VOL" -srcfolder "$STAGE" -ov -format UDRW -fs HFS+ "$RW" >/dev/null
MNT=$(hdiutil attach "$RW" -readwrite -noverify -noautoopen -nobrowse | awk -F'\t' '/\/Volumes\//{print $NF}')
osascript <<APPLESCRIPT || echo "note: Finder layout skipped (automation permission?)"
tell application "Finder"
  tell disk "$VOL"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set the bounds of container window to {200, 120, 860, 520}
    set opts to the icon view options of container window
    set arrangement of opts to not arranged
    set icon size of opts to 112
    set text size of opts to 13
    set background picture of opts to POSIX file "$MNT/.background/background.png"
    set position of item "ClearPower" of container window to {165, 190}
    set position of item "Applications" of container window to {495, 190}
    update without registering applications
    delay 2
    close
  end tell
end tell
APPLESCRIPT
sleep 2
sync
ls -la "$MNT/.DS_Store" >/dev/null 2>&1 || echo "note: Finder did not write .DS_Store; layout will be lost"
hdiutil detach "$MNT" -quiet
hdiutil convert "$RW" -format UDZO -imagekey zlib-level=9 -o "$DMG" >/dev/null
rm -rf "$STAGE" "$(dirname "$RW")"

if [ -n "$NOTARIZE" ]; then
  xcrun notarytool submit "$DMG" --keychain-profile "${NOTARY_PROFILE:-clearpower}" --wait
  xcrun stapler staple "$DMG"
fi
shasum -a 256 "$DMG"
ls -lh "$DMG"
