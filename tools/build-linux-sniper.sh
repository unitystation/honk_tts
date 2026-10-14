#!/usr/bin/env bash
# Builds the Linux installer inside Valve's Steam Linux Runtime 3.0 "sniper" SDK.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE="${SNIPER_SDK_IMAGE:-registry.gitlab.steamos.cloud/steamrt/sniper/sdk:latest}"
MAX_GLIBC="2.31"

docker run --rm -v "$ROOT:/src" -w /src \
    -e HOST_UID="$(id -u)" -e HOST_GID="$(id -g)" \
    "$IMAGE" bash -c '
        set -euo pipefail
        curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /opt/dotnet > /dev/null
        export PATH=/opt/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
        status=0
        bash ./build.sh || status=$?
        chown -R "$HOST_UID:$HOST_GID" build installer/src/HonkTTS.Installer/bin installer/src/HonkTTS.Installer/obj 2>/dev/null || true
        exit $status
    '

# Gate: the binary must not need a glibc newer than sniper's.
needed="$(objdump -T "$ROOT/build/HonkTTS.Installer" | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | sort -V -u | tail -1)"
echo "Highest glibc symbol version needed: $needed (max allowed $MAX_GLIBC)"
if [ "$(printf '%s\n%s\n' "$needed" "$MAX_GLIBC" | sort -V | tail -1)" != "$MAX_GLIBC" ]; then
    echo "ERROR: installer requires glibc $needed, newer than $MAX_GLIBC"
    exit 1
fi
