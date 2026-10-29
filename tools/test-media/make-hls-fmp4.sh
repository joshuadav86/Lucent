#!/bin/bash
# Makes tools/test-media/hls-fmp4: a second of a 440 Hz tone with a quiet sweep as an HLS stream in MP4 pieces
# (fragmented MP4, as SomaFM sends its 320k AAC), with a song title in an ID3 event box (emsg) in the second piece.
# The self-test reads the pieces as the radio player does (Lucent.Core's Radio/RadioSelfTest.cs).
# Needs FFmpeg and Python 3.
# Usage: tools/test-media/make-hls-fmp4.sh
set -euo pipefail
cd "$(dirname "$0")"
rm -rf hls-fmp4 && mkdir hls-fmp4 && cd hls-fmp4
tone="0.4*sin(2*PI*440*t)+0.1*sin(2*PI*(300+2000*t)*t)"
ffmpeg -nostdin -loglevel error -y -f lavfi -i "aevalsrc=$tone|$tone:s=44100:d=1" -c:a aac -b:a 128k \
  -f hls -hls_time 0.4 -hls_segment_type fmp4 -hls_playlist_type vod -hls_segment_filename 'piece%d.m4s' program.m3u8
python3 - <<'PY'
import struct

def frame(name, text):
    body = b'\x03' + text.encode() + b'\x00'
    return name.encode() + struct.pack('>I', len(body)) + b'\x00\x00' + body

def syncsafe(n):
    return bytes([(n >> 21) & 127, (n >> 14) & 127, (n >> 7) & 127, n & 127])

tag = frame('TPE1', 'Lucent') + frame('TIT2', 'Self-test')
id3 = b'ID3\x04\x00\x00' + syncsafe(len(tag)) + tag
# A version 1 event box, in AOM's ID3 scheme: timescale, time, duration and id, then scheme and value, then the tag.
body = b'\x01\x00\x00\x00' + struct.pack('>IQII', 1000, 0, 0, 1) + b'https://aomedia.org/emsg/ID3\x00\x00' + id3
emsg = struct.pack('>I', len(body) + 8) + b'emsg' + body
piece = open('piece1.m4s', 'rb').read()
at = piece.index(b'moof') - 4
open('piece1.m4s', 'wb').write(piece[:at] + emsg + piece[at:])
PY
