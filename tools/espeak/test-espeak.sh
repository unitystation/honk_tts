#!/bin/sh
set -u
INSTALL=/tmp/honk
mkdir -p "$INSTALL"
cp -r /bundle "$INSTALL/espeak-ng"
export ESPEAK_DATA_PATH="$INSTALL/espeak-ng/espeak-ng-data"
export PATH="$INSTALL/espeak-ng:$PATH"
fail=0

echo "host: $(. /etc/os-release && echo "$PRETTY_NAME"), espeak resolved to: $(command -v espeak-ng)"
espeak-ng --version || fail=1

# Coqui's phonemizer call
ipa=$(espeak-ng -q -b 1 -v en-us --ipa=1 "hello world, the clown honked")
echo "ipa: $ipa"
[ -n "$ipa" ] || { echo "FAIL: empty IPA"; fail=1; }

# Robotic voices: same parsing as tts_server.load_variant_voices (4th column)
variants=$(espeak-ng --voices=variant | tail -n +2 | awk 'NF >= 4 {print $4}')
count=$(echo "$variants" | grep -c .)
echo "variant voices: $count"
[ "$count" -gt 0 ] || fail=1
bad=0
for v in $variants; do
    out="/tmp/v.wav"
    rm -f "$out"
    if ! espeak-ng -v "en+$v" -w "$out" "testing robotic voice" 2> /tmp/err || [ ! -s "$out" ] || [ "$(wc -c < "$out")" -lt 2000 ]; then
        echo "FAIL voice $v: $(cat /tmp/err)"
        bad=$((bad + 1))
    fi
done
echo "variant renders failed: $bad / $count"
[ "$bad" -eq 0 ] || fail=1

[ "$fail" -eq 0 ] && echo "ALL ESPEAK CHECKS PASSED"
exit "$fail"
