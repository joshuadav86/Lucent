#!/bin/bash
# Makes tools/test-media/formats: half a second of a 440 Hz tone with a quiet sweep (so no two moments are alike), in
# each format and codec people's music comes in. The self-test decodes every one on Windows and the Mac
# (Lucent.Core's Audio/FormatSelfTest.cs). Needs FFmpeg built with libmp3lame, libopus, libvorbis and
# libspeex, and Monkey's Audio's mac command (monkeysaudio.com).
# Usage: tools/test-media/make-formats.sh
set -euo pipefail
cd "$(dirname "$0")/formats"
tone="0.4*sin(2*PI*440*t)+0.1*sin(2*PI*(300+2000*t)*t)"
source=(-f lavfi -i "aevalsrc=$tone|$tone:s=44100:d=0.5")
make() { local out=$1; shift; ffmpeg -nostdin -loglevel error -y "${source[@]}" "$@" "$out"; }

# Uncompressed, every way round (Lucent's own reader, Audio/PcmFileReader.cs).
make wav-u8.wav -c:a pcm_u8
make wav-16.wav -c:a pcm_s16le
make wav-24.wav -c:a pcm_s24le
make wav-32.wav -c:a pcm_s32le
make wav-float32.wav -c:a pcm_f32le
make wav-float64.wav -c:a pcm_f64le -ac 1
make wav-alaw.wav -c:a pcm_alaw -ac 1
make wav-24-quad.wav -c:a pcm_s24le -af "pan=quad|c0=c0|c1=c1|c2=c0|c3=c1"
make rf64.rf64 -c:a pcm_s16le -rf64 always -f wav
make w64.w64 -c:a pcm_s24le
make aiff-16.aiff -c:a pcm_s16be
make aiff-24.aiff -c:a pcm_s24be
make aifc-sowt.aifc -c:a pcm_s16le -f aiff
make aifc-fl32.aifc -c:a pcm_f32be -f aiff
make aifc-fl64.aifc -c:a pcm_f64be -f aiff -ac 1
make aifc-ulaw.aifc -c:a pcm_mulaw -f aiff -ac 1
make caf-pcm24.caf -c:a pcm_s24le
make caf-float.caf -c:a pcm_f32be

# Uncompressed in QuickTime and MP4, as OBS and cameras record it.
make mov-sowt.mov -c:a pcm_s16le
make mov-twos.mov -c:a pcm_s16be
make mov-in24.mov -c:a pcm_s24le
make mov-fl32.mov -c:a pcm_f32be
make mp4-ipcm.mp4 -c:a pcm_s16le

# Compressed.
make flac.flac -c:a flac
make alac.m4a -c:a alac
make caf-alac.caf -c:a alac
make aac.m4a -c:a aac -b:a 96k
make aac-adts.aac -c:a aac -b:a 96k
make mp3.mp3 -c:a libmp3lame -b:a 96k
make mp2.mp2 -c:a mp2 -b:a 128k
make opus.opus -c:a libopus -b:a 48k
make vorbis.ogg -c:a libvorbis -q:a 2
make wma.wma -c:a wmav2 -b:a 96k
make ac3.ac3 -c:a ac3 -b:a 96k -ar 48000
make eac3.eac3 -c:a eac3 -b:a 96k -ar 48000
make dts.dts -strict -2 -c:a dca -b:a 768k -ar 48000
make tta.tta -c:a tta
make wavpack.wv -c:a wavpack -sample_fmt s16p
make wavpack-float.wv -c:a wavpack -sample_fmt fltp
make ogg-flac.oga -c:a flac -f ogg
# FFmpeg doesn't write Monkey's Audio, so the tone goes through mac (it takes only plain 16-bit WAV).
make ape.wav -c:a pcm_s16le && mac ape.wav ape.ape -c2000 >/dev/null && rm ape.wav
make speex.spx -c:a libspeex -ar 32000 -compression_level 10 -q:a 10 -f ogg
make mp4-flac.mp4 -c:a flac
make mp4-opus.mp4 -c:a libopus -b:a 48k
make mka-flac.mka -c:a flac
make mka-ac3.mka -c:a ac3 -ar 48000
make webm-opus.weba -c:a libopus -f webm
