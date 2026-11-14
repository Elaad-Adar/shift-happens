#!/bin/zsh
# Builds "build/Shift Happens.app" (release) and signs it.
#   SIGN_IDENTITY="Apple Development: ..." scripts/build-app.sh   stable signature, Accessibility grant survives rebuilds
#   scripts/build-app.sh                                          ad-hoc signature, macOS asks for Accessibility again after each rebuild
#   VERSION=1.0.1 scripts/build-app.sh                            sets the app version (the release workflow passes the tag)
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
if [[ -n "${VERSION:-}" ]]; then
  plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
  plutil -replace CFBundleVersion -string "$VERSION" "$APP/Contents/Info.plist"
fi
codesign --force --options runtime --sign "${SIGN_IDENTITY:--}" "$APP"
codesign --verify --verbose=1 "$APP"
echo "Built: $APP"
