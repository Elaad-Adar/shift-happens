#!/bin/zsh
# Builds packaging/app.ico (Windows) from the macOS icon set made by make-icon.swift.
# Windows icons use the full square, so the Mac margin and shadow are cropped off first.
#   swift scripts/make-icon.swift packaging/logo-gemini.jpeg build/AppIcon.iconset && scripts/make-ico.sh
set -euo pipefail
cd "$(dirname "$0")/.."
TMP="$(mktemp -d)"
sips -c 840 840 build/AppIcon.iconset/icon_512x512@2x.png --out "$TMP/body.png" >/dev/null
for z in 16 24 32 48 64 128 256; do sips -z $z $z "$TMP/body.png" --out "$TMP/$z.png" >/dev/null; done
python3 - "$TMP" packaging/app.ico <<'PY'
import os, struct, sys
d, out = sys.argv[1], sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
blobs = [open(os.path.join(d, f"{s}.png"), "rb").read() for s in sizes]
offset = 6 + 16 * len(sizes)
entries = b""
for s, b in zip(sizes, blobs):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(b), offset)
    offset += len(b)
open(out, "wb").write(struct.pack("<HHH", 0, 1, len(sizes)) + entries + b"".join(blobs))
print("wrote", out)
PY
