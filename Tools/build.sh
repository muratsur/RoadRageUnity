#!/usr/bin/env bash
# Batchmode build wrapper (macOS/Linux). See build.ps1 for the Windows equivalent.
#   ./Tools/build.sh            # Windows player
#   ./Tools/build.sh Android
#   ./Tools/build.sh Scenes     # just report what the build would ship
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$(sed -n 's/^m_EditorVersion: //p' "$root/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
unity="${UNITY_EXE:-$HOME/Unity/Hub/Editor/$version/Editor/Unity}"

[ -x "$unity" ] || { echo "Unity $version not found at $unity - set UNITY_EXE." >&2; exit 1; }

case "${1:-Windows}" in
  Android) method=BuildAndroid ;;
  Scenes)  method=ReportSceneList ;;
  *)       method=BuildWindows ;;
esac

mkdir -p "$root/Build"
log="$root/Build/$method.log"
echo "Unity $version -> RoadRageCLI.$method"
echo "log: $log"

"$unity" -quit -batchmode -nographics -projectPath "$root" \
  -executeMethod "RoadRage.Editor.RoadRageCLI.$method" -logFile "$log" || {
    code=$?; grep -E 'RR_BUILD' "$log" || true; echo "Unity exited $code - see $log" >&2; exit $code; }

grep -E 'RR_BUILD' "$log" || true
echo "OK"
