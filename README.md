# HonkTTS

HonkTTS is the text-to-speech service used by Unitystation to generate immersive in-game voices.  
It is an optional module that runs alongside the game and is orchestrated by StationHub.

This repository contains:
- The Python TTS server (`server/tts_server.py`)
- Scripts to run and test the server
- A cross-platform .NET installer (`installer/`) that packages and sets up everything

## Latest Installer

Download the latest installer build from GitHub Releases:

`https://github.com/unitystation/honk_tts/releases`

If you only want to use HonkTTS (not develop it), start there.

## Contributing Prerequisites

Required:
- Git (clone/push workflow)
- `.NET SDK 10` (project targets `net10.0`)

Useful depending on what you change:
- Python 3.10+ (for direct server work and local script testing)

## Start Contributing

1. Clone the repo.
2. Build and run the local end-to-end flow:
   - Windows: `dev.bat`
   - Linux/macOS: `./dev.sh`
3. This will:
   - Build the installer
   - Install into `./tts` (including Python runtime and dependencies)
   - Run server tests (`test_tts`)

The `dev` flow is enough to start iterating even without a system Python install.  
You can edit the checked-in scripts/source in this repo, rerun `dev`, and validate changes against the locally installed `./tts` instance.

If you only want to build artifacts:
- Windows: `build.bat`
- Linux/macOS: `./build.sh`

## Project Layout

- `server/` - TTS API server and runtime scripts
- `installer/` - Native AOT .NET installer source
- `build*.{bat,sh}` - local build/package scripts
- `dev*.{bat,sh}` - quick contributor workflow scripts
