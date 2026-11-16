#!/usr/bin/env bash
# Prints the release notes for one version: its CHANGELOG.md section, then SHA-256 checksums of the
# release files. Fails when the changelog has no section for the version, so a release cannot go out
# without one.
#   scripts/release-notes.sh 1.4.0 dist > notes.md
set -euo pipefail
version="${1:?usage: release-notes.sh <version> <dir with release files>}"
dir="${2:?usage: release-notes.sh <version> <dir with release files>}"
cd "$(dirname "$0")/.."

section="$(awk -v v="$version" '
  $0 == "## " v { found = 1; next }
  found && /^## / { exit }
  found { print }
' CHANGELOG.md | sed -e '/./,$!d')"   # drop leading blank lines
if [[ -z "${section//[[:space:]]/}" ]]; then
  echo "CHANGELOG.md has no '## $version' section" >&2
  exit 1
fi

if command -v sha256sum >/dev/null; then sum() { sha256sum "$@"; }; else sum() { shasum -a 256 "$@"; }; fi

# This version's download count (GitHub's own counter, through shields.io).
printf '[![Downloads](https://img.shields.io/github/downloads/Elaad-Adar/shift-happens/v%s/total?label=downloads)](https://github.com/Elaad-Adar/shift-happens/releases/tag/v%s)\n\n' "$version" "$version"
printf '%s\n\n' "$section"
printf '### Downloads\n\n'
printf 'Install steps are in the [README](https://github.com/Elaad-Adar/shift-happens#download).\n\n'
printf '| File | SHA-256 |\n|---|---|\n'
for f in "$dir"/*.zip; do
  [[ -e "$f" ]] || continue
  hash="$(sum "$f" | awk '{print $1}')"
  # shellcheck disable=SC2016  # backticks are Markdown code spans, not command substitution
  printf '| `%s` | `%s` |\n' "$(basename "$f")" "$hash"
done
