#!/bin/sh
# Build ClearPower.app with SwiftPM + Command Line Tools only (no Xcode project).
#   scripts/build-app.sh                 release build -> dist/ClearPower.app, ad-hoc signed
#   CONFIG=debug scripts/build-app.sh    debug build (faster, for development)
#   SIGN_IDENTITY="Developer ID Application: ..." scripts/build-app.sh
#                                        Developer ID signing with hardened runtime; also
#                                        writes the app's designated requirement next to the
#                                        helper so it can verify its XPC clients.
set -e
cd "$(dirname "$0")/.."
ROOT=$(cd .. && pwd)
VERSION=$(tr -d '[:space:]' < "$ROOT/VERSION")
CONFIG=${CONFIG:-release}
APP=dist/ClearPower.app
C="$APP/Contents"

# Version stamp (SwiftPM has no -D for plain builds).
sed -i '' "s/public static let string = \"[^\"]*\"/public static let string = \"$VERSION\"/" Sources/ClearPowerCore/Version.swift

echo "== swift build ($CONFIG, arm64)"
swift build -c "$CONFIG" --arch arm64 2>&1 | grep -E "error|Build" || true
[ -x ".build/arm64-apple-macosx/$CONFIG/ClearPower" ] || { echo "build failed" >&2; exit 1; }
BIN=.build/arm64-apple-macosx/$CONFIG

echo "== assemble $APP"
rm -rf "$APP"
mkdir -p "$C/MacOS" "$C/Resources"
cp "$BIN/ClearPower" "$C/MacOS/ClearPower"
cp "$BIN/clearpower-helper" "$C/MacOS/org.clearpower.helper"
cp scripts/install-helper.sh Resources/org.clearpower.helper.plist "$C/Resources/"
sed "s/@VERSION@/$VERSION/g" Resources/Info.plist > "$C/Info.plist"
printf 'APPL????' > "$C/PkgInfo"
# SwiftPM resource bundle for test fixtures is not needed at runtime.

# Icon: rasterise the shared SVG with Quick Look, then iconutil.
# macOS applies its own squircle mask (and a white plate around icons with transparent
# margins), so Resources/icon-macos.svg is a full-bleed variant of the shared icon.
if [ ! -f Resources/ClearPower.icns ] || [ Resources/icon-macos.svg -nt Resources/ClearPower.icns ]; then
  echo "== icon"
  T=$(mktemp -d); mkdir -p "$T/ClearPower.iconset"
  swiftc -O -o "$T/svg2png" scripts/svg2png.swift 2>/dev/null
  for s in 16 32 128 256 512; do
    "$T/svg2png" Resources/icon-macos.svg "$T/ClearPower.iconset/icon_${s}x${s}.png" $s 2>/dev/null
    "$T/svg2png" Resources/icon-macos.svg "$T/ClearPower.iconset/icon_${s}x${s}@2x.png" $((s*2)) 2>/dev/null
  done
  iconutil -c icns "$T/ClearPower.iconset" -o Resources/ClearPower.icns
  rm -rf "$T"
fi
cp Resources/ClearPower.icns "$C/Resources/ClearPower.icns"

echo "== codesign"
if [ -n "$SIGN_IDENTITY" ]; then
  codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$C/MacOS/org.clearpower.helper"
  codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$APP"
  # Designated requirement of the app, enforced by the helper's XPC listener.
  codesign -d -r- "$APP" 2>&1 | sed -n 's/^designated => //p' > "$C/MacOS/org.clearpower.helper.requirement"
else
  codesign --force --sign - "$C/MacOS/org.clearpower.helper"
  codesign --force --sign - "$APP"
fi
codesign --verify --deep --strict "$APP" && echo "signed: $(codesign -dv "$APP" 2>&1 | grep -E '^Signature|^Authority' | head -1)"
echo "built $APP (version $VERSION)"
