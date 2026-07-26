#!/usr/bin/env bash
# build-windows.sh — headless Windows build wrapper for CloSim (online-multiplayer effort).
#
# WHAT IT DOES
#   Invokes the Unity Editor in batch mode and runs CloSimBuild.BuildWindows
#   (Assets/Editor/Build/CloSimBuild.cs), producing a Standalone Windows x64
#   player under Build/Windows/CloSim.exe. Works from Git Bash / WSL / Linux+Wine CI.
#
# ── WHERE TO SET THE UNITY PATH ───────────────────────────────────────────────
#   This project uses Unity 2023.2.22f1 (see ProjectSettings/ProjectVersion.txt).
#   Point the script at the Unity executable using ONE of, in priority order:
#     1. UNITY_PATH=/path/to/Unity   ./Tools/build-windows.sh   (env var, wins)
#     2. Auto-detect the default Unity Hub install for the version below.
#   Typical locations:
#     Windows (Git Bash): /c/Program Files/Unity/Hub/Editor/2023.2.22f1/Editor/Unity.exe
#     Linux (game-ci)   : /opt/unity/Editor/Unity
#
# EXACT COMMAND THIS SCRIPT RUNS
#   "$UNITY" -batchmode -quit -nographics \
#       -projectPath "$REPO_ROOT" \
#       -executeMethod CloSimBuild.BuildWindows \
#       -logFile "-" \
#       -buildOutput "Build/Windows/CloSim.exe"
#
# USAGE
#   ./Tools/build-windows.sh
#   UNITY_PATH="/c/Program Files/Unity/Hub/Editor/2023.2.22f1/Editor/Unity.exe" ./Tools/build-windows.sh
#   OUTPUT="Build/Windows/CloSim.exe" DEVELOPMENT=1 ./Tools/build-windows.sh
#
# EXIT CODES: passes through Unity's exit code (0 = success, non-zero = failure).
set -euo pipefail

UNITY_VERSION="2023.2.22f1"
OUTPUT="${OUTPUT:-Build/Windows/CloSim.exe}"
DEVELOPMENT="${DEVELOPMENT:-0}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

resolve_unity() {
    if [[ -n "${UNITY_PATH:-}" ]]; then
        if [[ -x "$UNITY_PATH" || -f "$UNITY_PATH" ]]; then echo "$UNITY_PATH"; return 0; fi
        echo "ERROR: UNITY_PATH='$UNITY_PATH' does not exist." >&2; exit 2
    fi
    local candidates=(
        "/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
        "/opt/unity/editors/$UNITY_VERSION/Editor/Unity"
        "/opt/unity/Editor/Unity"
        "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
    )
    for c in "${candidates[@]}"; do
        if [[ -f "$c" ]]; then echo "$c"; return 0; fi
    done
    cat >&2 <<EOF
ERROR: Could not locate Unity $UNITY_VERSION.
Set the Unity path and re-run, e.g.:
  UNITY_PATH="/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe" ./Tools/build-windows.sh
EOF
    exit 2
}

UNITY="$(resolve_unity)"
echo "[build-windows] Unity   : $UNITY"
echo "[build-windows] Project : $REPO_ROOT"
echo "[build-windows] Output  : $OUTPUT"

args=(
    -batchmode -quit -nographics
    -projectPath "$REPO_ROOT"
    -executeMethod CloSimBuild.BuildWindows
    -logFile "-"
    -buildOutput "$OUTPUT"
)
if [[ "$DEVELOPMENT" == "1" ]]; then args+=(-development); fi

"$UNITY" "${args[@]}"
echo "[build-windows] Done. Player at: $REPO_ROOT/$OUTPUT"
