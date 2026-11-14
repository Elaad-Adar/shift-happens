#!/bin/zsh
# Builds "build/Shift Happens.app" (release) and signs it.
#   SIGN_IDENTITY="Apple Development: ..." scripts/build-app.sh   stable signature, Accessibility grant survives rebuilds
#   scripts/build-app.sh                                          ad-hoc signature, macOS asks for Accessibility again after each rebuild
set -euo pipefail
cd "$(dirname "$0")/.."
swift build -c release --product shift-happens
BIN="$(swift build -c release --show-bin-path)/shift-happens"
APP="build/Shift Happens.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/ShiftHappens"
cp packaging/Info.plist "$APP/Contents/Info.plist"
cp packaging/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
codesign --force --options runtime --sign "${SIGN_IDENTITY:--}" "$APP"
codesign --verify --verbose=1 "$APP"
echo "Built: $APP"
