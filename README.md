# HonkTTS

This repo will be the future local TTS server for Unitystation. Idea is for the client to run locally their own instance of 
HonkTTS and their game to request audio data from it, then deserialize and convert into ``wav`` so they can be set as clip
in an AudioSource and played using the same pipeline any ingame sound uses, being affected by ambient, spatial and 
following their source.

## Development Requirements

- [Python 3.11](https://www.python.org/downloads/release/python-3110/)
- [espeak-ng installed](https://github.com/espeak-ng/espeak-ng/blob/master/docs/guide.md)

## User requirements
Idea is to compile the application using Nuitka and uploading an artifact for each supported platform. The final users 
would need to download this executable and set its path inside the game for it to establish communication with it.

Sadly, the final user also needs [espeak-ng installed](https://github.com/espeak-ng/espeak-ng/blob/master/docs/guide.md). 
In the case of a Mac user, they would have to [use this homebrew](https://formulae.brew.sh/formula/espeak-ng) (untested).