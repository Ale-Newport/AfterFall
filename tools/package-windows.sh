#!/usr/bin/env bash
# Packages the Windows player (Builds/Windows) as a zip with a SHA-256 checksum.
# Build it first with: <Unity> -batchmode -quit -projectPath . -executeMethod Afterfall.EditorTools.BuildCommands.BuildWindows
# (needs the Unity "Windows Build Support (Mono)" module).
# Usage: tools/package-windows.sh <version-tag> [--upload]
#   --upload attaches both files to the GitHub Release (or draft) for <version-tag> (needs gh).
set -euo pipefail
cd "$(dirname "$0")/.." || exit 1

tag="${1:?usage: tools/package-windows.sh vX.Y.Z [--upload]}"
src="Builds/Windows"
[ -f "$src/AFTERFALL.exe" ] || { echo "missing $src/AFTERFALL.exe: build the Windows player first" >&2; exit 1; }

name="AFTERFALL-${tag}-windows-x64"
stage="Builds/.package/$name"
zip="Builds/$name.zip"
rm -rf "Builds/.package" "$zip" "$zip.sha256"
mkdir -p "$stage"
# Ship the player only: Burst debug symbols are for crash analysis, not for players.
rsync -a --exclude '*_BurstDebugInformation_DoNotShip' "$src/" "$stage/"
(cd Builds/.package && zip -qr -X "../$name.zip" "$name")
rm -rf "Builds/.package"
(cd Builds && shasum -a 256 "$name.zip" > "$name.zip.sha256")
ls -lh "$zip"
cat "$zip.sha256"

if [ "${2:-}" = "--upload" ]; then
  gh release upload "$tag" "$zip" "$zip.sha256" --clobber
fi
