#!/usr/bin/env bash
# Shared helpers. Source from scripts with:
#   SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
#   source "$SCRIPT_DIR/lib/common.sh"

unity_executable() {
    if [[ -n "${UNITY_EDITOR:-}" && -x "$UNITY_EDITOR" ]]; then
        echo "$UNITY_EDITOR"
        return 0
    fi
    local candidate
    candidate=$(ls -1d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity 2>/dev/null | sort -V | tail -1 || true)
    if [[ -n "$candidate" && -x "$candidate" ]]; then
        echo "$candidate"
        return 0
    fi
    echo "error: Unity not found. Set UNITY_EDITOR to the Unity binary path." >&2
    echo "example: export UNITY_EDITOR=\"/Applications/Unity/Hub/Editor/6000.3.3f1/Unity.app/Contents/MacOS/Unity\"" >&2
    return 1
}
