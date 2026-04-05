#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TMP="$ROOT/tmp"

if [[ -d "$TMP" ]]; then
    rm -rf "${TMP:?}"
fi

mkdir -p "$TMP/screenshots"
echo "Cleared $TMP (recreated with tmp/screenshots/)."
