#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
TARGET="$ROOT/tts"

"$ROOT/build.sh"

INSTALLER="$ROOT/build/HonkTTS.Installer"
if [ -x "$INSTALLER" ]; then
  "$INSTALLER" "$TARGET"
elif [ -x "$INSTALLER.exe" ]; then
  "$INSTALLER.exe" "$TARGET"
else
  echo "Installer binary not found in $ROOT/build"
  exit 1
fi

"$ROOT/tts/test_tts.sh" --keep "$@"
