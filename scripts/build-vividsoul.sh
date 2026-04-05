#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=lib/common.sh
source "$SCRIPT_DIR/lib/common.sh"

UNITY="$(unity_executable)"
PROJECT="${UNITY_PROJECT:-$ROOT/VividSoul}"
LOG_DIR="$ROOT/Exports/VividSoul/logs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/unity-batch-build-$(date +%Y%m%d-%H%M%S).log"

echo "Unity: $UNITY"
echo "Project: $PROJECT"
echo "Log: $LOG_FILE"

"$UNITY" -batchmode -quit -nographics \
    -projectPath "$PROJECT" \
    -executeMethod VividSoul.Editor.VividSoulBuildTools.BuildMacOS \
    -logFile "$LOG_FILE"

echo "Build finished. Log: $LOG_FILE"
