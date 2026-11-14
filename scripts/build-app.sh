#!/bin/zsh
# Builds "build/Shift Happens.app" (release, universal: Apple Silicon + Intel) and signs it.
#   SIGN_IDENTITY="Apple Development: ..." scripts/build-app.sh   stable signature, Accessibility grant survives rebuilds
#   scripts/build-app.sh                                          ad-hoc signature, macOS asks for Accessibility again after each rebuild
#   VERSION=1.0.1 scripts/build-app.sh                            sets the app version (the release workflow passes the tag)
set -euo pipefail
cd "$(dirname "$0")/.."
BINS=()
for ARCH in arm64 x86_64; do
  swift build -c release --product shift-happens --arch "$ARCH"
  BINS+=("$(swift build -c release --arch "$ARCH" --show-bin-path)/shift-happens")
done
APP="build/Shift Happens.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "${BINS[@]}" -output "$APP/Contents/MacOS/ShiftHappens"
cp packaging/Info.plist "$APP/Contents/Info.plist"
cp packaging/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
if [[ -n "${VERSION:-}" ]]; then
  plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
  plutil -replace CFBundleVersion -string "$VERSION" "$APP/Contents/Info.plist"
fi
codesign --force --options runtime --sign "${SIGN_IDENTITY:--}" "$APP"
codesign --verify --verbose=1 "$APP"
lipo -info "$APP/Contents/MacOS/ShiftHappens"
echo "Built: $APP"
