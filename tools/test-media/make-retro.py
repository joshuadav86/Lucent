#!/usr/bin/env python3
"""Makes the MIDI, tracker and game music files in tools/test-media/formats: half a second of A (440 Hz) in each.

The self-test (Lucent.Core's Audio/FormatSelfTest.cs) plays them on Windows and the Mac: the MIDI file with Lucent's
own synthesizer and SoundFont, the module and the VGM file with libVLC's mod and gme players. Synthesizers ring on
and trackers round to their rows, so these only have to come out about half a second long and audible.

Usage: python3 tools/test-media/make-retro.py
"""
import math
import os
import struct

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "formats")


def midi():
    """A standard MIDI file: one piano note, A4, held for half a second at 120 beats a minute."""
    ticks_per_beat = 480
    half_second = ticks_per_beat  # one beat at 120 bpm

    def number(value):
        out = [value & 0x7F]
        value >>= 7
        while value:
            out.insert(0, 0x80 | (value & 0x7F))
            value >>= 7
        return bytes(out)

    track = b"".join([
        number(0) + b"\xff\x51\x03" + (500000).to_bytes(3, "big"),  # 120 bpm
        number(0) + b"\xc0\x00",  # piano
        number(0) + b"\xb0\x07\x7f",  # full channel volume
        number(0) + b"\x90\x45\x7f",  # A4 on
        number(half_second) + b"\x80\x45\x40",  # and off
        number(0) + b"\xff\x2f\x00",
    ])
    header = b"MThd" + struct.pack(">IHHH", 6, 0, 1, ticks_per_beat)
    return header + b"MTrk" + struct.pack(">I", len(track)) + track


def module():
    """A four-channel ProTracker module: a looped sine wave played as A for 25 rows at one tick a row (0.5 s)."""
    rate = 8363  # what the sample plays at for C-2 (period 428)
    # A sample that's 19 cycles long at 8363 Hz gives 440 Hz at C-2; it loops, so it holds as long as the note does.
    length = 2 * round(rate * 19 / 440 / 2)
    wave = bytes((int(round(100 * math.sin(2 * math.pi * 19 * i / length))) & 0xFF) for i in range(length))
    title = b"lucent test".ljust(20, b"\0")
    samples = b""
    for index in range(31):
        name = b"sine".ljust(22, b"\0") if index == 0 else b"\0" * 22
        if index == 0:
            # Length in words, finetune, volume (64), loop start and length in words.
            samples += name + struct.pack(">HBBHH", length // 2, 0, 64, 0, length // 2)
        else:
            samples += name + struct.pack(">HBBHH", 0, 0, 0, 0, 1)
    order = bytes([1, 127]) + bytes([0] * 128)
    rows = []
    for row in range(64):
        channels = []
        for channel in range(4):
            sample, period, effect, parameter = 0, 0, 0, 0
            if channel == 0 and row == 0:
                sample, period, effect, parameter = 1, 428, 0xF, 1  # C-2 with sample 1, and one tick a row
            if channel == 1 and row == 0:
                effect, parameter = 0xF, 0x7D  # 125 bpm: 20 ms a tick
            if channel == 0 and row == 24:
                effect, parameter = 0xD, 0  # the song ends after this row
            channels.append(bytes([
                (sample & 0xF0) | (period >> 8), period & 0xFF, ((sample & 0x0F) << 4) | effect, parameter,
            ]))
        rows.append(b"".join(channels))
    return title + samples + order + b"M.K." + b"".join(rows) + wave


def vgm():
    """A VGM file for the SN76489 (Sega Master System) sound chip: its first tone channel at 440 Hz for half a second."""
    clock = 3579545
    divider = round(clock / (32 * 440))
    commands = bytes([
        0x50, 0x80 | (divider & 0x0F),  # tone 0, low four bits
        0x50, (divider >> 4) & 0x3F,  # and the high six
        0x50, 0x90,  # tone 0 at full volume
        0x50, 0xBF, 0x50, 0xDF, 0x50, 0xFF,  # the rest silent
        0x61, *struct.pack("<H", 22050),  # half a second at 44.1 kHz
        0x50, 0x9F,  # tone 0 silent
        0x66,
    ])
    header = bytearray(0x40)
    header[0:4] = b"Vgm "
    struct.pack_into("<I", header, 0x08, 0x150)  # version 1.50
    struct.pack_into("<I", header, 0x0C, clock)  # SN76489 clock
    struct.pack_into("<I", header, 0x18, 22050)  # samples in all
    struct.pack_into("<H", header, 0x28, 0x0009)  # the chip's noise feedback
    header[0x2A] = 16  # its shift register width
    struct.pack_into("<I", header, 0x34, 0x40 - 0x34)  # where the commands start, from here
    body = bytes(header) + commands
    struct.pack_into("<I", header, 0x04, len(body) - 4)  # where the file ends, from here
    return bytes(header) + commands


for name, make in (("midi.mid", midi), ("tracker.mod", module), ("chip.vgm", vgm)):
    with open(os.path.join(HERE, name), "wb") as out:
        out.write(make())
    print("made", name)
