import io
import os
import re
import shutil
import struct
import subprocess
import sys
import time
from dataclasses import dataclass
from typing import Any

import numpy as np
from flask import Flask, jsonify, request, send_file
from scipy.io.wavfile import write as write_wav
from TTS.api import TTS
from waitress import serve

app = Flask(__name__)

VOICES: dict[str, str] = {
    "Male 01": "p226",
    "Male 02": "p228",
    "Male 03": "p229",
    "Male 04": "p230",
    "Male 05": "p231",
    "Male 06": "p232",
    "Male 07": "p233",
    "Male 08": "p234",
    "Male 09": "p236",
    "Male 10": "p238",
    "Male 11": "p239",
    "Male 12": "p241",
    "Male 13": "p251",
    "Male 14": "p252",
    "Male 15": "p253",
    "Male 16": "p254",
    "Male 17": "p255",
    "Male 18": "p256",
    "Male 19": "p257",
    "Male 20": "p258",
    "Male 21": "p262",
    "Male 22": "p264",
    "Male 23": "p265",
    "Male 24": "p266",
    "Male 25": "p267",
    "Male 26": "p269",
    "Male 27": "p272",
    "Male 28": "p279",
    "Male 29": "p281",
    "Male 30": "p285",
    "Male 31": "p286",
    "Male 32": "p287",
    "Male 33": "p298",
    "Female 01": "p225",
    "Female 02": "p227",
    "Female 03": "p237",
    "Female 04": "p240",
    "Female 05": "p243",
    "Female 06": "p244",
    "Female 07": "p245",
    "Female 08": "p246",
    "Female 09": "p247",
    "Female 10": "p248",
    "Female 11": "p249",
    "Female 12": "p250",
    "Female 13": "p259",
    "Female 14": "p260",
    "Female 15": "p261",
    "Female 16": "p263",
    "Female 17": "p270",
    "Female 18": "p271",
    "Female 19": "p273",
    "Female 20": "p274",
    "Female 21": "p275",
    "Female 22": "p276",
    "Female 23": "p277",
    "Female 24": "p278",
    "Female 25": "p280",
    "Female 26": "p283",
    "Female 27": "p284",
    "Female 28": "p288",
    "Female 29": "p293",
    "Female 30": "p294",
    "Female 31": "p295",
    "Female 32": "p297",
}


@dataclass(frozen=True)
class AudioRequest:
    input_string: str
    voice: str


def parse_audio_request(payload: Any) -> AudioRequest:
    if not isinstance(payload, dict):
        raise ValueError("Request body must be a JSON object.")

    input_string = payload.get("input_string")
    voice = payload.get("voice")

    if not isinstance(input_string, str) or not input_string.strip():
        raise ValueError("input_string cannot be empty.")
    if not isinstance(voice, str) or not voice.strip():
        raise ValueError("voice cannot be empty.")

    sanitized_input = re.sub(r"[^a-zA-Z0-9?!,.;@'\" ]", "", input_string)
    if not sanitized_input:
        raise ValueError("input_string cannot be empty.")

    return AudioRequest(input_string=sanitized_input, voice=voice.strip())


def get_espeak_binary() -> str:
    for candidate in ("espeak-ng", "espeak"):
        if shutil.which(candidate):
            return candidate
    raise RuntimeError("Could not find espeak-ng or espeak in PATH.")


def load_variant_voices(espeak_bin: str) -> set[str]:
    proc = subprocess.run(
        [espeak_bin, "--voices=variant"],
        check=False,
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        stderr = proc.stderr.strip() or proc.stdout.strip()
        raise RuntimeError(f"Failed to load variant voices: {stderr}")

    voices: set[str] = set()
    lines = proc.stdout.splitlines()
    for line in lines[1:]:
        parts = line.split()
        if len(parts) >= 4:
            voices.add(parts[3])
    if not voices:
        raise RuntimeError("No variant voices found from espeak.")
    return voices


def generate_wav(text: str, voice: str) -> io.BytesIO:
    code = VOICES[voice]
    wav = tts.tts(text=text, speaker=code)
    sample_rate = int(tts.synthesizer.output_sample_rate)

    wav = np.array(wav)
    wav_norm = (wav * 32767 / max(0.01, np.max(np.abs(wav)))).astype(np.int16)

    wav_io = io.BytesIO()
    write_wav(wav_io, sample_rate, wav_norm)
    wav_io.seek(0)
    return wav_io


def generate_robotic_wav(text: str, voice: str) -> io.BytesIO:
    # Write to a temp file instead of --stdout to avoid Windows pipe
    # binary/text mode corruption of PCM data.
    import tempfile as _tempfile

    with _tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp_path = tmp.name

    try:
        proc = subprocess.run(
            [ESPEAK_BINARY, "-v", f"en+{voice}", "-w", tmp_path, text],
            check=False,
            capture_output=True,
        )
        if proc.returncode != 0:
            stderr = proc.stderr.decode(errors="replace").strip()
            raise RuntimeError(stderr or "espeak failed to generate audio")

        with open(tmp_path, "rb") as f:
            wav_io = io.BytesIO(f.read())
        wav_io.seek(0)
        return wav_io
    finally:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass


@app.route("/generate-audio", methods=["POST"])
def generate_audio():
    start_time = time.time()

    try:
        parsed = parse_audio_request(request.get_json(silent=True))
    except ValueError as e:
        return jsonify({"error": str(e)}), 400

    if parsed.voice not in VOICES:
        return jsonify({"error": f"Invalid voice. Valid options are: {list(VOICES.keys())}"}), 400

    try:
        wav_io = generate_wav(parsed.input_string, parsed.voice)
    except Exception as e:
        return jsonify({"error": f"Error generating audio: {str(e)}"}), 500

    duration = time.time() - start_time
    print(f"Generated audio in {duration:.4f}s")

    return send_file(wav_io, mimetype="audio/wav", as_attachment=True, download_name="output.wav")


@app.route("/generate_audio_robotic", methods=["POST"])
def generate_audio_robotic():
    start_time = time.time()

    try:
        parsed = parse_audio_request(request.get_json(silent=True))
    except ValueError as e:
        return jsonify({"error": str(e)}), 400

    if parsed.voice not in VARIANT_VOICES:
        return jsonify({"error": f"Invalid voice. Valid options are: {sorted(VARIANT_VOICES)}"}), 400

    try:
        wav_io = generate_robotic_wav(parsed.input_string, parsed.voice)
    except Exception as e:
        return jsonify({"error": f"Error generating audio: {str(e)}"}), 500

    duration = time.time() - start_time
    print(f"Generated robotic audio in {duration:.4f}s")

    return send_file(wav_io, mimetype="audio/wav", as_attachment=True, download_name="output_robotic.wav")


@app.route("/health", methods=["GET"])
def health():
    espeak_path = shutil.which(ESPEAK_BINARY) or ESPEAK_BINARY
    espeak_data = os.environ.get("ESPEAK_DATA_PATH", "not set")

    version_proc = subprocess.run(
        [ESPEAK_BINARY, "--version"],
        capture_output=True, text=True, check=False,
    )
    espeak_version = version_proc.stdout.strip() or version_proc.stderr.strip() or "unknown"

    return jsonify({
        "status": "ok",
        "espeak_binary": espeak_path,
        "espeak_version": espeak_version,
        "espeak_data_path": espeak_data,
        "python_executable": sys.executable,
        "tts_model": "tts_models/en/vctk/vits",
        "voices": sorted(VOICES.keys()),
        "voices_count": len(VOICES),
        "variant_voices": sorted(VARIANT_VOICES),
        "variant_voices_count": len(VARIANT_VOICES),
    })


TTS_MODEL = "tts_models/en/vctk/vits"
HOST = "127.0.0.1"
PORT = 5234

tts: TTS | None = None
ESPEAK_BINARY: str | None = None
VARIANT_VOICES: set[str] = set()


def start():
    global tts, ESPEAK_BINARY, VARIANT_VOICES
    tts = TTS(TTS_MODEL, progress_bar=False, gpu=False)
    ESPEAK_BINARY = get_espeak_binary()
    VARIANT_VOICES = load_variant_voices(ESPEAK_BINARY)
    serve(app, host=HOST, port=PORT, threads=4, backlog=8, connection_limit=24, channel_timeout=10)


if __name__ == "__main__":
    start()
