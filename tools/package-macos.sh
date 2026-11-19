#!/usr/bin/env bash
# Packages the macOS player (Builds/macOS/AFTERFALL.app) as a zip with a SHA-256 checksum.
# Build it first with: tools/run-unity-checks.sh build tour
# Usage: tools/package-macos.sh <version-tag> [--upload]
#   --upload attaches both files to the existing GitHub Release for <version-tag> (needs gh).
set -euo pipefail
cd "$(dirname "$0")/.." || exit 1

tag="${1:?usage: tools/package-macos.sh vX.Y.Z [--upload]}"
app="Builds/macOS/AFTERFALL.app"
[ -d "$app" ] || { echo "missing $app: run tools/run-unity-checks.sh build first" >&2; exit 1; }

zip="Builds/AFTERFALL-${tag}-macOS-universal.zip"
rm -f "$zip" "$zip.sha256"
# ditto keeps the bundle's symlinks, permissions and code signature intact.
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
(cd Builds && shasum -a 256 "$(basename "$zip")" > "$(basename "$zip").sha256")
ls -lh "$zip"
cat "$zip.sha256"

if [ "${2:-}" = "--upload" ]; then
  gh release upload "$tag" "$zip" "$zip.sha256" --clobber
fi
