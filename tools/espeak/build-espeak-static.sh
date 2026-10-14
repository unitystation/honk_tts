#!/bin/sh
# Runs inside alpine. Builds a fully static espeak-ng plus its data into /out/espeak-ng.
set -eu
VERSION="${ESPEAK_VERSION:-1.51}"

apk add --no-cache build-base autoconf automake libtool pkgconf curl file > /dev/null

mkdir -p /src && cd /src
curl -fsSL "https://github.com/espeak-ng/espeak-ng/archive/refs/tags/${VERSION}.tar.gz" | tar -xz --strip-components=1
./autogen.sh > /dev/null
./configure --prefix=/usr --disable-shared --enable-static \
    --without-pcaudiolib --without-sonic --without-mbrola --without-async > /dev/null
# libtool drops plain -static for programs, -all-static is its spelling for a fully static link
make -j"$(nproc)" LDFLAGS="-all-static" > /tmp/make.log 2>&1 || { tail -40 /tmp/make.log; exit 1; }
make install DESTDIR=/stage > /dev/null

rm -rf /out/espeak-ng
mkdir -p /out/espeak-ng
cp /stage/usr/bin/espeak-ng /out/espeak-ng/
strip /out/espeak-ng/espeak-ng
cp -r /stage/usr/share/espeak-ng-data /out/espeak-ng/
file /out/espeak-ng/espeak-ng
du -sh /out/espeak-ng /out/espeak-ng/espeak-ng
chown -R "${HOST_UID:-0}:${HOST_GID:-0}" /out
