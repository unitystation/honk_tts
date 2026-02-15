"""
Test script for HonkTTS server.
Imports constants (VOICES, HOST, PORT) from tts_server.py to stay in sync.

Usage:
    python test_server.py              # start server, run tests, kill server
    python test_server.py --play       # also open generated WAVs in default player
    python test_server.py --keep       # keep generated WAV files after test
    python test_server.py --no-start   # skip server lifecycle, test an already-running server
"""

import argparse
import json
import os
import platform
import signal
import struct
import subprocess
import sys
import tempfile
import threading
import time
import urllib.request

# Resolve the directory containing tts_server.py:
#   - Installed layout: test_server.py and tts_server.py are siblings
#   - Dev layout: scripts/test_server.py, ../tts_server.py
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_PARENT_DIR = os.path.normpath(os.path.join(_SCRIPT_DIR, ".."))

if os.path.isfile(os.path.join(_SCRIPT_DIR, "tts_server.py")):
    _SERVER_DIR = _SCRIPT_DIR
elif os.path.isfile(os.path.join(_PARENT_DIR, "tts_server.py")):
    _SERVER_DIR = _PARENT_DIR
else:
    print("ERROR: Could not locate tts_server.py")
    sys.exit(1)

if _SERVER_DIR not in sys.path:
    sys.path.insert(0, _SERVER_DIR)

from tts_server import HOST, PORT, VOICES  # noqa: E402

BASE_URL = f"http://{HOST}:{PORT}"



def find_server_script() -> str:
    candidate = os.path.join(_SERVER_DIR, "tts_server.py")
    if os.path.isfile(candidate):
        return candidate

    print("  ERROR: Could not locate tts_server.py")
    sys.exit(1)


def is_server_up() -> bool:
    try:
        with urllib.request.urlopen(f"{BASE_URL}/health", timeout=2):
            return True
    except Exception:
        return False


def kill_existing_server():
    """Kill any existing server on the port before starting a fresh one."""
    if not is_server_up():
        return

    print("  Found existing server, waiting for it to stop...")
    time.sleep(1)
    if not is_server_up():
        return

    print(f"  WARNING: Existing server still running on port {PORT}.")
    print("  Kill it manually before running tests, or use --no-start to test it as-is.")
    sys.exit(1)


def start_server() -> subprocess.Popen:
    script = find_server_script()
    print(f"  Starting server: {sys.executable} {script}")

    proc = subprocess.Popen(
        [sys.executable, script],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )

    # Drain stdout in a background thread to prevent the pipe buffer from
    # filling up and blocking the server (TTS model loading dumps a lot of text).
    server_output: list[str] = []

    def _drain():
        assert proc.stdout is not None
        for line in proc.stdout:
            server_output.append(line.rstrip())

    drain_thread = threading.Thread(target=_drain, daemon=True)
    drain_thread.start()

    print("  Waiting for server to be ready (this may take a minute on first run)...")
    deadline = time.time() + 120  # 2 minute timeout for model loading
    while time.time() < deadline:
        if proc.poll() is not None:
            drain_thread.join(timeout=2)
            print(f"  FAIL: Server exited with code {proc.returncode} during startup")
            for line in server_output[-20:]:
                print(f"    {line}")
            sys.exit(1)

        if is_server_up():
            print("  Server is ready.")
            return proc

        time.sleep(1)

    proc.kill()
    print("  FAIL: Server did not become ready within 120 seconds.")
    sys.exit(1)


def stop_server(proc: subprocess.Popen):
    print("\n  Stopping server...")
    if proc.poll() is not None:
        return

    if platform.system() == "Windows":
        proc.terminate()
    else:
        proc.send_signal(signal.SIGTERM)

    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        print("  Server didn't stop gracefully, killing...")
        proc.kill()
        proc.wait(timeout=5)

    print("  Server stopped.")



def request_json(method: str, path: str, body: dict | None = None) -> tuple[int, dict | bytes]:
    url = f"{BASE_URL}{path}"
    data = json.dumps(body).encode() if body else None
    headers = {"Content-Type": "application/json"} if body else {}
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            content_type = resp.headers.get("Content-Type", "")
            raw = resp.read()
            if "application/json" in content_type:
                return resp.status, json.loads(raw)
            return resp.status, raw
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, {"raw": raw.decode(errors="replace")}
    except urllib.error.URLError as e:
        print(f"  FAIL: Could not connect to {url}: {e.reason}")
        sys.exit(1)



def validate_wav(data: bytes) -> list[str]:
    """Validate WAV header and return list of issues (empty = valid)."""
    issues = []

    if len(data) < 44:
        issues.append(f"File too small for WAV header: {len(data)} bytes")
        return issues

    riff = data[0:4]
    if riff != b"RIFF":
        issues.append(f"Missing RIFF magic: got {riff!r}")

    wave = data[8:12]
    if wave != b"WAVE":
        issues.append(f"Missing WAVE marker: got {wave!r}")

    file_size_field = struct.unpack_from("<I", data, 4)[0]
    expected_file_size = len(data) - 8
    if file_size_field != expected_file_size:
        issues.append(f"RIFF size mismatch: header says {file_size_field}, actual {expected_file_size}")

    pos = 12
    fmt_found = False
    data_found = False
    sample_rate = 0
    channels = 0
    bits_per_sample = 0
    while pos < len(data) - 8:
        chunk_id = data[pos:pos + 4]
        chunk_size = struct.unpack_from("<I", data, pos + 4)[0]

        if chunk_id == b"fmt ":
            fmt_found = True
            if chunk_size >= 16:
                audio_fmt = struct.unpack_from("<H", data, pos + 8)[0]
                channels = struct.unpack_from("<H", data, pos + 10)[0]
                sample_rate = struct.unpack_from("<I", data, pos + 12)[0]
                bits_per_sample = struct.unpack_from("<H", data, pos + 22)[0]
                print(f"  WAV format: {audio_fmt} (1=PCM), {channels}ch, {sample_rate}Hz, {bits_per_sample}bit")
                if audio_fmt != 1:
                    issues.append(f"Non-PCM audio format: {audio_fmt}")

        if chunk_id == b"data":
            data_found = True
            duration = 0.0
            if fmt_found and sample_rate and channels and bits_per_sample:
                bytes_per_sample = bits_per_sample // 8
                duration = chunk_size / (sample_rate * channels * bytes_per_sample)
            print(f"  WAV data: {chunk_size} bytes (~{duration:.2f}s)")
            if chunk_size == 0:
                issues.append("Data chunk is empty (0 bytes)")

        pos += 8 + chunk_size

    if not fmt_found:
        issues.append("No fmt chunk found")
    if not data_found:
        issues.append("No data chunk found")

    return issues


def open_file(path: str):
    system = platform.system()
    if system == "Windows":
        os.startfile(path)
    elif system == "Darwin":
        subprocess.run(["open", path], check=False)
    else:
        subprocess.run(["xdg-open", path], check=False)



def test_health(fail_warnings: bool):
    print("\n=== /health ===")
    status, body = request_json("GET", "/health")

    if status != 200:
        print(f"  FAIL: status {status}")
        print(f"  {body}")
        return False

    for key in ("espeak_binary", "espeak_version", "espeak_data_path",
                "python_executable", "tts_model", "voices_count", "variant_voices_count",
                "voices", "variant_voices"):
        val = body.get(key, "MISSING")
        print(f"  {key}: {val}")

    espeak_path = body.get("espeak_binary", "")
    espeak_data = body.get("espeak_data_path", "")
    python_exe = body.get("python_executable", "")

    warnings = []
    if espeak_data == "not set":
        warnings.append("ESPEAK_DATA_PATH is not set — server may be using system espeak data")

    if "StationHub" not in espeak_path and "espeak-ng" not in os.path.dirname(espeak_path).split(os.sep)[-1:]:
        warnings.append(f"eSpeak binary appears to be system-wide: {espeak_path}")
        warnings.append("Expected it under the installer's espeak-ng/ directory")

    if "venv" not in python_exe:
        warnings.append(f"Python does not appear to be from a venv: {python_exe}")

    if warnings:
        print()
        for w in warnings:
            print(f"  WARNING: {w}")
        if fail_warnings:
            print("  FAIL: warnings treated as errors (--fail-warnings)")
            return False
    else:
        print("\n  All paths look correct (installer-provided)")

    return True


def test_generate_audio(play: bool, keep: bool):
    print("\n=== /generate-audio (Coqui TTS) ===")
    voice = next(iter(VOICES))
    print(f"  Using voice: {voice}")
    status, body = request_json("POST", "/generate-audio", {
        "input_string": "I'm only human, after all. I'm only human, after all. Don't put the blame on me!.",
        "voice": voice,
    })

    if status != 200:
        print(f"  FAIL: status {status}")
        print(f"  {body}")
        return False

    if not isinstance(body, bytes):
        print(f"  FAIL: expected binary WAV data, got JSON: {body}")
        return False

    print(f"  Received {len(body)} bytes")
    issues = validate_wav(body)

    if issues:
        print(f"  FAIL: WAV validation errors:")
        for issue in issues:
            print(f"    - {issue}")

        # Dump first 64 bytes for debugging
        print(f"  First 64 bytes (hex): {body[:64].hex(' ')}")
        return False

    print("  PASS: WAV is valid")

    if play or keep:
        path = os.path.join(tempfile.gettempdir(), "honktts_test_coqui.wav")
        with open(path, "wb") as f:
            f.write(body)
        print(f"  Saved to: {path}")
        if play:
            open_file(path)

    return True


def test_generate_robotic(play: bool, keep: bool):
    print("\n=== /generate_audio_robotic (eSpeak) ===")

    _, health = request_json("GET", "/health")
    if not isinstance(health, dict):
        print("  FAIL: could not query /health")
        return False

    variant_voices = health.get("variant_voices", [])
    if not variant_voices:
        print("  SKIP: no variant voices available")
        return True

    voice = variant_voices[0]
    print(f"  Using variant voice: {voice}")
    status, body = request_json("POST", "/generate_audio_robotic", {
        "input_string": "I'm sorry, Dave. I'm afraid I can't do that. I think you know what the problem is just as well as I do.",
        "voice": voice,
    })

    if status != 200:
        print(f"  FAIL: status {status}")
        print(f"  {body}")
        return False

    if not isinstance(body, bytes):
        print(f"  FAIL: expected binary WAV data, got JSON: {body}")
        return False

    print(f"  Received {len(body)} bytes")
    issues = validate_wav(body)

    if issues:
        print(f"  FAIL: WAV validation errors:")
        for issue in issues:
            print(f"    - {issue}")
        return False

    print("  PASS: WAV is valid")

    if play or keep:
        path = os.path.join(tempfile.gettempdir(), "honktts_test_robotic.wav")
        with open(path, "wb") as f:
            f.write(body)
        print(f"  Saved to: {path}")
        if play:
            open_file(path)

    return True



def main():
    parser = argparse.ArgumentParser(description="Test HonkTTS server")
    parser.add_argument("--play", action="store_true", help="Open generated WAV files in default player")
    parser.add_argument("--keep", action="store_true", help="Save generated WAV files to temp dir")
    parser.add_argument("--no-start", action="store_true", help="Don't manage the server — test an already-running one")
    parser.add_argument("--fail-warnings", action="store_true",
                        help="Treat health warnings as failures (useful for CI)")
    args = parser.parse_args()

    print(f"Testing HonkTTS server at {BASE_URL}")

    server_proc = None
    if not args.no_start:
        kill_existing_server()
        server_proc = start_server()

    try:
        results = {}
        results["health"] = test_health(args.fail_warnings)
        results["generate_audio"] = test_generate_audio(args.play, args.keep or args.play)
        results["generate_robotic"] = test_generate_robotic(args.play, args.keep or args.play)
    finally:
        if server_proc is not None:
            stop_server(server_proc)

    print("\n=== Summary ===")
    all_pass = True
    for name, passed in results.items():
        status = "PASS" if passed else "FAIL"
        print(f"  {name}: {status}")
        if not passed:
            all_pass = False

    sys.exit(0 if all_pass else 1)


if __name__ == "__main__":
    main()
