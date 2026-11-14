#!/bin/zsh
# Runs `swift test`. With only the Command Line Tools installed (no Xcode), Swift Testing lives in a
# separate folder and its Foundation overlay module is missing, so point the compiler at it.
set -euo pipefail
cd "$(dirname "$0")/.."
CLT=/Library/Developer/CommandLineTools/Library/Developer
if [[ "$(xcode-select -p)" == /Library/Developer/CommandLineTools ]]; then
  swift test \
    -Xswiftc -F -Xswiftc "$CLT/Frameworks" \
    -Xswiftc -Xfrontend -Xswiftc -disable-cross-import-overlays \
    -Xlinker -F -Xlinker "$CLT/Frameworks" \
    -Xlinker -rpath -Xlinker "$CLT/Frameworks" \
    -Xlinker -rpath -Xlinker "$CLT/usr/lib" "$@"
else
  swift test "$@"
fi
