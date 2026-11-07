#!/usr/bin/env bash
# Runs the in-Unity checks: EditMode tests, PlayMode tests, a macOS build and the scripted tour.
# Needs Unity 6000.3.28f1 with an activated license. Results go to Logs/unity-checks/.
# Usage: tools/run-unity-checks.sh [editmode|playmode|build|tour ...]   (default: all four)
set -uo pipefail
cd "$(dirname "$0")/.."

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity}"
OUT="Logs/unity-checks"
mkdir -p "$OUT"
steps=("$@")
[ ${#steps[@]} -eq 0 ] && steps=(editmode playmode build tour)
status=0

run_tests() {
  local platform="$1" name="$2"
  "$UNITY" -batchmode -projectPath . -runTests -testPlatform "$platform" \
    -testResults "$PWD/$OUT/$name.xml" -logFile "$OUT/$name.log"
  local code=$?
  if [ -f "$OUT/$name.xml" ]; then
    python3 - "$OUT/$name.xml" <<'EOF'
import sys, xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
print(f"{sys.argv[1]}: {run.get('passed')} passed, {run.get('failed')} failed, {run.get('skipped')} skipped of {run.get('total')}")
for case in run.iter('test-case'):
    if case.get('result') == 'Failed':
        msg = case.find('./failure/message')
        print(f"  FAILED {case.get('fullname')}: {(msg.text or '').strip()[:300] if msg is not None else ''}")
EOF
  else
    echo "$name: no results file (exit $code); see $OUT/$name.log"
  fi
  return $code
}

for step in "${steps[@]}"; do
  case "$step" in
    editmode) run_tests EditMode editmode || status=1 ;;
    playmode) run_tests PlayMode playmode || status=1 ;;
    build)
      "$UNITY" -batchmode -quit -projectPath . -executeMethod Afterfall.EditorTools.BuildCommands.BuildMacOS -logFile "$OUT/build.log" \
        && echo "build: Builds/macOS/AFTERFALL.app" || { echo "build: FAILED, see $OUT/build.log"; status=1; } ;;
    tour)
      rm -rf "$OUT/tour"
      open -W -n Builds/macOS/AFTERFALL.app --args -afterfall-tour "$PWD/$OUT/tour" \
        -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "$PWD/$OUT/tour-player.log"
      if [ -f "$OUT/tour/tour-report.txt" ]; then cat "$OUT/tour/tour-report.txt" | head -40; else echo "tour: no report, see $OUT/tour-player.log"; status=1; fi ;;
    *) echo "unknown step: $step"; status=2 ;;
  esac
done
exit $status
