#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/build"

echo "Cleaning output directory..."
rm -rf "$OUT"
mkdir -p "$OUT/scripts"

echo "Copying server files..."
cp "$ROOT/server/tts_server.py" "$OUT/"
cp "$ROOT/server/requirements.txt" "$OUT/"
cp "$ROOT/server/scripts/start_tts.bat" "$OUT/scripts/"
cp "$ROOT/server/scripts/start_tts.sh" "$OUT/scripts/"
cp "$ROOT/server/scripts/test_server.py" "$OUT/scripts/"
cp "$ROOT/server/scripts/test_tts.bat" "$OUT/scripts/"
cp "$ROOT/server/scripts/test_tts.sh" "$OUT/scripts/"

ARCH="$(uname -m)"
case "$(uname -s)" in
    Linux*)  RID="linux-x64"; [ "$ARCH" = "aarch64" ] && RID="linux-arm64" ;;
    Darwin*) RID="osx-x64";   [ "$ARCH" = "arm64" ]   && RID="osx-arm64"  ;;
    *)       echo "Unsupported OS"; exit 1 ;;
esac

echo "Building installer (Release, Native AOT, $RID)..."
dotnet publish "$ROOT/installer/src/HonkTTS.Installer/HonkTTS.Installer.csproj" \
    -r "$RID" -c Release \
    -o "$OUT" \
    --nologo

echo ""
echo "Build complete: $OUT"
ls "$OUT"
