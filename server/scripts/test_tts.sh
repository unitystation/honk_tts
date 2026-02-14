#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
export ESPEAK_DATA_PATH="$SCRIPT_DIR/espeak-ng/espeak-ng-data"
export PATH="$SCRIPT_DIR/espeak-ng:$PATH"

source "$SCRIPT_DIR/venv/bin/activate"
python "$SCRIPT_DIR/scripts/test_server.py" "$@"
