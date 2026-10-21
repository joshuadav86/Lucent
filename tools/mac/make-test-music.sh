#!/bin/bash
# Makes the music the Mac self-test plays: a few seconds of tones as WAV, then the formats people's music
# comes in, made with macOS's own encoder. Runs on a Mac. Usage: tools/mac/make-test-music.sh [folder]
set -euo pipefail
cd "${1:-.}"
python3 - << 'PY'
import math, struct, wave
with wave.open("test.wav", "wb") as f:
    f.setnchannels(2); f.setsampwidth(2); f.setframerate(44100)
    frames = bytearray()
    for i in range(44100 * 3):
        v = int(12000 * math.sin(2 * math.pi * 220 * i / 44100) * (1 if (i // 11025) % 2 == 0 else 0.3))
        frames += struct.pack("<hh", v, v)
    f.writeframes(bytes(frames))
PY
afconvert -f m4af -d aac test.wav test.m4a
afconvert -f m4af -d alac test.wav test-alac.m4a
afconvert -f flac -d flac test.wav test.flac
afconvert -f AIFF -d BEI16 test.wav test.aiff
