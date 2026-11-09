#!/usr/bin/env bash
# Builds the macOS player and photographs every model for visual review (Presentation/Game/ModelGallery.cs).
# Usage: tools/gallery.sh [sections] [outdir]
#   sections  comma list of structures,nodes,props,characters,icons,world (default: all)
#   outdir    default Logs/gallery; screenshots land in <outdir>/<section>/, report in <outdir>/gallery-report.txt
# Works in git worktrees: the first run clones the main checkout's Library (copy-on-write) to skip a
# full import. At most AFTERFALL_UNITY_SLOTS (default 3) builds/galleries run at once across checkouts.
# Do not run it while the project is open in the editor.
set -uo pipefail
cd "$(dirname "$0")/.."
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity}"
SECTIONS="${1:-structures,nodes,props,characters,icons,world}"
OUT="${2:-Logs/gallery}"
mkdir -p Logs

if [ ! -d Library ]; then
  main="$(git worktree list --porcelain | sed -n '1s/^worktree //p')"
  if [ -n "$main" ] && [ "$main" != "$PWD" ] && [ -d "$main/Library" ]; then
    echo "gallery: cloning Library from $main"
    cp -cR "$main/Library" Library 2>/dev/null || cp -R "$main/Library" Library
  fi
fi

slots="${AFTERFALL_UNITY_SLOTS:-3}"
lockroot="${TMPDIR:-/tmp}/afterfall-unity-slots"
mkdir -p "$lockroot"
slot=""
while [ -z "$slot" ]; do
  for i in $(seq 1 "$slots"); do
    d="$lockroot/$i"
    if mkdir "$d" 2>/dev/null; then
      echo $$ > "$d/pid"
      slot="$d"
      break
    fi
    pid="$(cat "$d/pid" 2>/dev/null || true)"
    if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then rm -rf "$d"; fi
  done
  [ -z "$slot" ] && sleep 5
done
trap 'rm -rf "$slot"' EXIT

echo "gallery: building the macOS player (slot $(basename "$slot"))"
"$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod Afterfall.EditorTools.BuildCommands.BuildMacOS -logFile Logs/gallery-build.log
code=$?
grep -E "Shader error|Shader warning in 'Afterfall" Logs/gallery-build.log | sort -u | head -20
if [ $code -ne 0 ] || [ ! -d Builds/macOS/AFTERFALL.app ]; then
  echo "gallery: BUILD FAILED (exit $code). Errors from Logs/gallery-build.log:"
  grep -E "error CS|Shader error|Error building|Exception|BuildFailedException" Logs/gallery-build.log | sort -u | head -40
  exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT"
out="$(cd "$OUT" && pwd)"
echo "gallery: photographing $SECTIONS"
open -W -n Builds/macOS/AFTERFALL.app --args -afterfall-gallery "$out" -afterfall-gallery-sections "$SECTIONS" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 800 -logFile "$out/player.log"
if [ ! -f "$out/gallery-report.txt" ]; then
  echo "gallery: no report, see $out/player.log"
  exit 1
fi
sed -n '1,/^Errors logged/p' "$out/gallery-report.txt"
echo "gallery: $(find "$out" -name '*.png' | wc -l | tr -d ' ') screenshots in $out"
errors="$(sed -n 's/^Errors logged: //p' "$out/gallery-report.txt")"
[ "${errors:-1}" -eq 0 ]
