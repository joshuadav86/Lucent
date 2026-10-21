#!/usr/bin/env python3
"""
Draws Lucent Classic, Lucent's own classic Winamp skin, and packs it as a .wsz.

Every pixel is drawn here from scratch, in Lucent's colours, to the layout every Winamp 2 skin uses (the sheet
sizes and sprite positions are what make skins interchangeable). No art from Winamp or any other skin is used.

    python3 tools/ClassicSkin/make_lucent_classic.py            # writes src/Lucent.Core/ClassicSkins/LucentClassic.wsz
    python3 tools/ClassicSkin/make_lucent_classic.py --sheets dir   # also each sheet as a 4x .png, to look at

Needs Pillow.
"""

import argparse
import io
import os
import zipfile

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUTPUT = os.path.join(HERE, "..", "..", "src", "Lucent.Core", "ClassicSkins", "LucentClassic.wsz")

# ---- Colours (Lucent's Midnight theme) ----

BLACK = (5, 5, 8)
LCD = (7, 7, 12)
BODY = (22, 22, 30)
BODY_LIGHT = (40, 40, 54)
BODY_DARK = (11, 11, 16)
EDGE_LIGHT = (62, 62, 82)
EDGE_DARK = (4, 4, 6)
GROOVE = (12, 12, 18)
FACE = (34, 34, 46)
FACE_LIGHT = (70, 70, 92)
FACE_DARK = (14, 14, 20)
TEXT = (236, 236, 241)
MUTED = (125, 125, 140)
FAINT = (74, 74, 87)
VIOLET = (139, 92, 246)
CYAN = (34, 211, 238)
ACCENT = (139, 124, 255)
LCD_TEXT = (158, 235, 255)
RED = (239, 68, 68)
GREEN = (52, 211, 153)


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(t):
    """Lucent's accent gradient, violet to cyan."""
    return mix(VIOLET, CYAN, max(0.0, min(1.0, t)))


# ---- A 4 x 5 pixel font, Lucent's own, for text.bmp and the labels ----

FONT = {
    "A": [".##.", "#..#", "####", "#..#", "#..#"],
    "B": ["###.", "#..#", "###.", "#..#", "###."],
    "C": [".###", "#...", "#...", "#...", ".###"],
    "D": ["###.", "#..#", "#..#", "#..#", "###."],
    "E": ["####", "#...", "###.", "#...", "####"],
    "F": ["####", "#...", "###.", "#...", "#..."],
    "G": [".###", "#...", "#.##", "#..#", ".###"],
    "H": ["#..#", "#..#", "####", "#..#", "#..#"],
    "I": ["###.", ".#..", ".#..", ".#..", "###."],
    "J": ["..##", "...#", "...#", "#..#", ".##."],
    "K": ["#..#", "#.#.", "##..", "#.#.", "#..#"],
    "L": ["#...", "#...", "#...", "#...", "####"],
    "M": ["#..#", "####", "####", "#..#", "#..#"],
    "N": ["#..#", "##.#", "#.##", "#..#", "#..#"],
    "O": [".##.", "#..#", "#..#", "#..#", ".##."],
    "P": ["###.", "#..#", "###.", "#...", "#..."],
    "Q": [".##.", "#..#", "#..#", "#.#.", ".#.#"],
    "R": ["###.", "#..#", "###.", "#.#.", "#..#"],
    "S": [".###", "#...", ".##.", "...#", "###."],
    "T": ["###.", ".#..", ".#..", ".#..", ".#.."],
    "U": ["#..#", "#..#", "#..#", "#..#", ".##."],
    "V": ["#..#", "#..#", "#..#", ".##.", ".##."],
    "W": ["#..#", "#..#", "####", "####", "#..#"],
    "X": ["#..#", ".##.", ".##.", ".##.", "#..#"],
    "Y": ["#.#.", "#.#.", ".#..", ".#..", ".#.."],
    "Z": ["####", "..#.", ".#..", "#...", "####"],
    "0": [".##.", "#.##", "##.#", "#..#", ".##."],
    "1": [".#..", "##..", ".#..", ".#..", "###."],
    "2": ["###.", "...#", ".##.", "#...", "####"],
    "3": ["###.", "...#", ".##.", "...#", "###."],
    "4": ["#..#", "#..#", "####", "...#", "...#"],
    "5": ["####", "#...", "###.", "...#", "###."],
    "6": [".##.", "#...", "###.", "#..#", ".##."],
    "7": ["####", "...#", "..#.", ".#..", ".#.."],
    "8": [".##.", "#..#", ".##.", "#..#", ".##."],
    "9": [".##.", "#..#", ".###", "...#", ".##."],
    '"': ["#.#.", "#.#.", "....", "....", "...."],
    "@": [".##.", "#..#", "#.##", "#...", ".###"],
    " ": ["....", "....", "....", "....", "...."],
    "…": ["....", "....", "....", "....", "#.#."],
    ".": ["....", "....", "....", "....", ".#.."],
    ":": ["....", ".#..", "....", ".#..", "...."],
    "(": ["..#.", ".#..", ".#..", ".#..", "..#."],
    ")": [".#..", "..#.", "..#.", "..#.", ".#.."],
    "-": ["....", "....", "###.", "....", "...."],
    "'": [".#..", ".#..", "....", "....", "...."],
    "!": [".#..", ".#..", ".#..", "....", ".#.."],
    "_": ["....", "....", "....", "....", "####"],
    "+": ["....", ".#..", "###.", ".#..", "...."],
    "\\": ["#...", "#...", ".#..", "..#.", "..#."],
    "/": ["..#.", "..#.", ".#..", "#...", "#..."],
    "[": [".##.", ".#..", ".#..", ".#..", ".##."],
    "]": [".##.", "..#.", "..#.", "..#.", ".##."],
    "^": [".#..", "#.#.", "....", "....", "...."],
    "&": [".#..", "#.#.", ".#..", "#.#.", ".#.#"],
    "%": ["#..#", "..#.", ".#..", "#...", "#..#"],
    ",": ["....", "....", "....", ".#..", "#..."],
    "=": ["....", "###.", "....", "###.", "...."],
    "$": [".###", "##..", ".##.", "..##", "###."],
    "#": [".#.#", "####", ".#.#", "####", ".#.#"],
    "Å": [".##.", ".##.", "#..#", "####", "#..#"],
    "Ö": ["#..#", ".##.", "#..#", "#..#", ".##."],
    "Ä": ["#..#", ".##.", "#..#", "####", "#..#"],
    "?": ["###.", "...#", ".##.", "....", ".#.."],
    "*": ["....", "#.#.", ".#..", "#.#.", "...."],
}

# Where each character goes in text.bmp (row, column), the layout every skin's font follows.
TEXT_LAYOUT = [
    "ABCDEFGHIJKLMNOPQRSTUVWXYZ\"@   ",
    "0123456789….:()-'!_+\\/[]^&%,=$#",
    "ÅÖÄ?*",
]


def text(draw, x, y, string, colour):
    for i, c in enumerate(string.upper()):
        glyph = FONT.get(c, FONT[" "])
        for row, line in enumerate(glyph):
            for column, bit in enumerate(line):
                if bit == "#":
                    draw.point((x + i * 5 + column, y + row), fill=colour)


def text_width(string):
    return len(string) * 5 - 1


# ---- Drawing helpers ----


def bevel(draw, x0, y0, x1, y1, light, dark, fill=None):
    """A raised box (light top and left, dark bottom and right); swap the colours for a sunken one."""
    if fill is not None:
        draw.rectangle((x0, y0, x1, y1), fill=fill)
    draw.line((x0, y0, x1, y0), fill=light)
    draw.line((x0, y0, x0, y1), fill=light)
    draw.line((x0, y1, x1, y1), fill=dark)
    draw.line((x1, y0, x1, y1), fill=dark)


def well(draw, x0, y0, x1, y1, fill=LCD):
    """A sunken display window."""
    draw.rectangle((x0, y0, x1, y1), fill=fill)
    bevel(draw, x0 - 1, y0 - 1, x1 + 1, y1 + 1, EDGE_DARK, EDGE_LIGHT)


def vertical(draw, x0, y0, x1, y1, top, bottom):
    height = max(1, y1 - y0)
    for y in range(y0, y1 + 1):
        draw.line((x0, y, x1, y), fill=mix(top, bottom, (y - y0) / height))


def horizontal(draw, x0, y0, x1, y1, colour_at):
    width = max(1, x1 - x0)
    for x in range(x0, x1 + 1):
        draw.line((x, y0, x, y1), fill=colour_at((x - x0) / width))


def body(draw, width, height):
    """The window's brushed, softly lit body."""
    vertical(draw, 0, 0, width - 1, height - 1, BODY_LIGHT, BODY_DARK)
    for y in range(0, height, 2):
        draw.line((1, y, width - 2, y), fill=mix(mix(BODY_LIGHT, BODY_DARK, y / height), BLACK, 0.08))
    bevel(draw, 0, 0, width - 1, height - 1, EDGE_LIGHT, EDGE_DARK)


def lucent_mark(image, cx, cy, size):
    """Lucent's spark: a four-pointed star filled with the accent gradient."""
    for y in range(cy - size, cy + size + 1):
        for x in range(cx - size, cx + size + 1):
            dx, dy = abs(x - cx), abs(y - cy)
            # A concave diamond: thin arms, fat middle.
            if (dx ** 0.6 + dy ** 0.6) <= size ** 0.6 + 0.01:
                t = ((x - cx) + (y - cy) + 2 * size) / (4 * size)
                colour = mix(gradient(t), TEXT, 0.35 if dx + dy <= 1 else 0)
                image.putpixel((x, y), colour)


# ---- The sheets ----


def main_bmp():
    image = Image.new("RGB", (275, 116), BODY)
    draw = ImageDraw.Draw(image)
    body(draw, 275, 116)

    # The display: clutter bar, time and visualizer on the left; title and rates on the right.
    well(draw, 9, 21, 103, 64)
    draw.line((19, 22, 19, 63), fill=(16, 16, 24))
    well(draw, 109, 24, 266, 35)
    well(draw, 109, 41, 127, 51)
    well(draw, 154, 41, 167, 51)
    text(draw, 130, 43, "kbps", MUTED)
    text(draw, 170, 43, "khz", MUTED)
    # A hairline of accent under the display, Lucent's signature.
    horizontal(draw, 9, 66, 103, 66, lambda t: mix(gradient(t), BODY, 0.45))

    # Grooves behind the sliders and buttons, so a skin's sprites sit in something.
    well(draw, 16, 72, 263, 81, GROOVE)
    bevel(draw, 13, 86, 160, 107, BODY_DARK, EDGE_LIGHT)

    # Lucent's spark, bottom right.
    lucent_mark(image, 254, 97, 7)
    return image


def titlebar_bmp():
    image = Image.new("RGB", (344, 87), BLACK)
    draw = ImageDraw.Draw(image)

    def bar(y, active, shade):
        top = (46, 44, 66) if active else (30, 30, 40)
        bottom = (20, 20, 30) if active else (16, 16, 22)
        vertical(draw, 27, y, 301, y + 13, top, bottom)
        bevel(draw, 27, y, 301, y + 13, EDGE_LIGHT if active else (44, 44, 56), EDGE_DARK)
        if shade:
            # The shade strip has a little window for the time, at 125 to 155.
            well(draw, 27 + 124, y + 3, 27 + 155, y + 10)
            text(draw, 27 + 22, y + 4, "LUCENT", TEXT if active else MUTED)
            # A slim seek-and-level groove, for show.
            horizontal(draw, 27 + 166, y + 6, 27 + 230, y + 7,
                       lambda t: mix(gradient(t), BODY_DARK, 0.2 if active else 0.75))
            return
        title = "LUCENT"
        x = 27 + (275 - text_width(title)) // 2
        # Grip ridges either side of the name.
        for gx0, gx1 in ((27 + 20, x - 6), (x + text_width(title) + 6, 27 + 238)):
            for gy in (y + 4, y + 7, y + 10):
                draw.line((gx0, gy, gx1, gy), fill=(64, 60, 92) if active else (40, 40, 52))
                draw.line((gx0, gy + 1, gx1, gy + 1), fill=EDGE_DARK)
        text(draw, x, y + 4, title, TEXT if active else MUTED)
        if active:
            horizontal(draw, x, y + 10, x + text_width(title), y + 10, gradient)

    bar(0, True, False)
    bar(15, False, False)
    bar(29, True, True)
    bar(42, False, True)
    bar(57, True, False)
    bar(72, False, False)

    def button(x, y, glyph, pressed):
        face = (18, 18, 26) if pressed else (40, 40, 56)
        draw.rectangle((x, y, x + 8, y + 8), fill=face)
        bevel(draw, x, y, x + 8, y + 8, EDGE_DARK if pressed else FACE_LIGHT, FACE_LIGHT if pressed else EDGE_DARK)
        colour = ACCENT if pressed else TEXT
        for px, py in glyph:
            draw.point((x + px, y + py), fill=colour)

    options = [(4, 2), (3, 3), (4, 3), (5, 3), (2, 4), (3, 4), (5, 4), (6, 4), (3, 5), (4, 5), (5, 5), (4, 6)]
    minimize = [(2, 6), (3, 6), (4, 6), (5, 6), (6, 6)]
    close = [(2, 2), (6, 2), (3, 3), (5, 3), (4, 4), (3, 5), (5, 5), (2, 6), (6, 6)]
    shade = [(2, 2), (3, 2), (4, 2), (5, 2), (6, 2), (2, 3), (6, 3), (2, 4), (6, 4), (2, 5), (6, 5), (2, 6), (3, 6), (4, 6), (5, 6), (6, 6)]
    unshade = [(2, 3), (3, 3), (4, 3), (5, 3), (6, 3), (2, 5), (3, 5), (4, 5), (5, 5), (6, 5)]
    button(0, 0, options, False)
    button(0, 9, options, True)
    button(9, 0, minimize, False)
    button(9, 9, minimize, True)
    button(18, 0, close, False)
    button(18, 9, close, True)
    button(0, 18, shade, False)
    button(9, 18, shade, True)
    button(0, 27, unshade, False)
    button(9, 27, unshade, True)

    # The clutter bar: O A I D V, up (304), greyed (312), and each letter pressed (304 + 8n, 44 + its top).
    letters = [("O", 3, 8), ("A", 11, 7), ("I", 18, 7), ("D", 25, 8), ("V", 33, 7)]
    tiny = {
        "O": ["###", "#.#", "#.#", "#.#", "###"],
        "A": [".#.", "#.#", "###", "#.#", "#.#"],
        "I": ["###", ".#.", ".#.", ".#.", "###"],
        "D": ["##.", "#.#", "#.#", "#.#", "##."],
        "V": ["#.#", "#.#", "#.#", "#.#", ".#."],
    }

    def clutter(x, y, colour, background):
        draw.rectangle((x, y, x + 7, y + 42), fill=background)
        draw.line((x + 7, y, x + 7, y + 42), fill=EDGE_DARK)
        for letter, top, height in letters:
            glyph = tiny[letter]
            oy = y + top + (height - 5) // 2
            for row, line in enumerate(glyph):
                for column, bit in enumerate(line):
                    if bit == "#":
                        draw.point((x + 2 + column, oy + row), fill=colour)

    clutter(304, 0, MUTED, (12, 12, 18))
    clutter(312, 0, (40, 40, 50), (12, 12, 18))
    for index, (letter, top, height) in enumerate(letters):
        x, y = 304 + 8 * index, 44 + top
        draw.rectangle((x, y, x + 7, y + height - 1), fill=(30, 24, 60))
        glyph = tiny[letter]
        oy = y + (height - 5) // 2
        for row, line in enumerate(glyph):
            for column, bit in enumerate(line):
                if bit == "#":
                    draw.point((x + 2 + column, oy + row), fill=CYAN)
    return image


def cbuttons_bmp():
    image = Image.new("RGB", (136, 36), BLACK)
    draw = ImageDraw.Draw(image)

    def face(x, y, w, h, pressed):
        if pressed:
            vertical(draw, x, y, x + w - 1, y + h - 1, (16, 14, 28), (28, 24, 48))
            bevel(draw, x, y, x + w - 1, y + h - 1, EDGE_DARK, (60, 52, 100))
        else:
            vertical(draw, x, y, x + w - 1, y + h - 1, (52, 52, 70), (26, 26, 36))
            bevel(draw, x, y, x + w - 1, y + h - 1, FACE_LIGHT, EDGE_DARK)
            draw.line((x + 1, y + 1, x + w - 2, y + 1), fill=(84, 84, 108))

    def glyph(x, y, w, h, shape, pressed):
        cx, cy = x + w // 2, y + h // 2
        colour = CYAN if pressed else TEXT
        pts = []
        if shape == "previous":
            pts += [(cx - 5, cy + d) for d in range(-3, 4)]
            for i in range(4):
                pts += [(cx - 4 + i, cy + d) for d in range(-i, i + 1)]
                pts += [(cx + i, cy + d) for d in range(-i, i + 1)]
        elif shape == "play":
            for i in range(6):
                pts += [(cx - 2 + i, cy + d) for d in range(-(5 - i), 6 - i)]
        elif shape == "pause":
            for d in range(-4, 5):
                pts += [(cx - 3, cy + d), (cx - 2, cy + d), (cx + 1, cy + d), (cx + 2, cy + d)]
        elif shape == "stop":
            for d in range(-4, 4):
                pts += [(cx - 4 + e, cy + d) for e in range(8)]
        elif shape == "next":
            for i in range(4):
                pts += [(cx - 4 + (3 - i), cy + d) for d in range(-i, i + 1)]
                pts += [(cx + (3 - i), cy + d) for d in range(-i, i + 1)]
            pts += [(cx + 4, cy + d) for d in range(-3, 4)]
        elif shape == "eject":
            for i in range(5):
                pts += [(cx + d, cy - 4 + i) for d in range(-i, i + 1)]
            pts += [(cx + d, cy + 3) for d in range(-4, 5)]
            pts += [(cx + d, cy + 4) for d in range(-4, 5)]
        for px, py in pts:
            draw.point((px, py), fill=colour)
        if pressed:
            # A soft violet glow round the pressed glyph.
            for px, py in pts:
                for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    if (px + ox, py + oy) not in pts:
                        current = image.getpixel((px + ox, py + oy))
                        image.putpixel((px + ox, py + oy), mix(current, VIOLET, 0.35))

    buttons = [("previous", 0, 23, 18), ("play", 23, 23, 18), ("pause", 46, 23, 18), ("stop", 69, 23, 18),
               ("next", 92, 22, 18), ("eject", 114, 22, 16)]
    for shape, x, w, h in buttons:
        for pressed in (False, True):
            y = h if pressed else 0
            face(x, y, w, h, pressed)
            glyph(x, y, w, h, shape, pressed)
    return image


# Seven segments in a 9 x 13 digit, two pixels thick: (x0, y0, x1, y1) of each bar.
SEGMENTS = {
    "a": (2, 0, 6, 1), "b": (7, 2, 8, 5), "c": (7, 7, 8, 10), "d": (2, 11, 6, 12),
    "e": (0, 7, 1, 10), "f": (0, 2, 1, 5), "g": (2, 6, 6, 6),
}
DIGITS = ["abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg"]


def numbers_bmp():
    image = Image.new("RGB", (99, 13), LCD)
    draw = ImageDraw.Draw(image)
    for digit in range(10):
        x = digit * 9
        for name, (x0, y0, x1, y1) in SEGMENTS.items():
            lit = name in DIGITS[digit]
            for y in range(y0, y1 + 1):
                colour = gradient(y / 12) if lit else (13, 13, 21)
                draw.line((x + x0, y, x + x1, y), fill=colour)
    return image


def playpaus_bmp():
    image = Image.new("RGB", (42, 9), LCD)
    draw = ImageDraw.Draw(image)
    for i in range(4):
        draw.line((2 + i, 1 + i, 2 + i, 7 - i), fill=gradient(i / 3))
    draw.line((2 + 4, 4, 2 + 4, 4), fill=CYAN)
    draw.rectangle((11, 2, 12, 6), fill=ACCENT)
    draw.rectangle((14, 2, 15, 6), fill=ACCENT)
    draw.rectangle((20, 2, 24, 6), fill=MUTED)
    # Not working (red) and working (green) ticks, 3 wide.
    draw.rectangle((37, 2, 37, 6), fill=RED)
    draw.rectangle((40, 2, 40, 6), fill=GREEN)
    return image


def monoster_bmp():
    image = Image.new("RGB", (58, 24), BODY_DARK)
    draw = ImageDraw.Draw(image)
    for y, lit in ((0, True), (12, False)):
        for x, w, label in ((0, 29, "STEREO"), (29, 27, "MONO")):
            draw.rectangle((x, y, x + w - 1, y + 11), fill=LCD)
            bevel(draw, x, y, x + w - 1, y + 11, EDGE_DARK, EDGE_LIGHT)
            # The labels are too wide for 4-pixel letters, so these use 3-pixel ones.
            colour = CYAN if lit else (36, 36, 48)
            tiny_text(draw, x + (w - (len(label) * 4 - 1)) // 2, y + 4, label, colour)
    return image


TINY = {
    "S": ["###", "#..", "###", "..#", "###"], "T": ["###", ".#.", ".#.", ".#.", ".#."],
    "E": ["###", "#..", "##.", "#..", "###"], "R": ["##.", "#.#", "##.", "#.#", "#.#"],
    "O": ["###", "#.#", "#.#", "#.#", "###"], "M": ["#.#", "###", "###", "#.#", "#.#"],
    "N": ["##.", "#.#", "#.#", "#.#", "#.#"], "H": ["#.#", "#.#", "###", "#.#", "#.#"],
    "U": ["#.#", "#.#", "#.#", "#.#", "###"], "F": ["###", "#..", "##.", "#..", "#.."],
    "L": ["#..", "#..", "#..", "#..", "###"], "Q": ["###", "#.#", "#.#", "###", "..#"],
    "P": ["###", "#.#", "###", "#..", "#.."],
}


def tiny_text(draw, x, y, string, colour):
    for i, c in enumerate(string):
        for row, line in enumerate(TINY[c]):
            for column, bit in enumerate(line):
                if bit == "#":
                    draw.point((x + i * 4 + column, y + row), fill=colour)


def posbar_bmp():
    image = Image.new("RGB", (307, 10), BLACK)
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 247, 9), fill=GROOVE)
    bevel(draw, 0, 0, 247, 9, EDGE_DARK, (40, 40, 54))
    horizontal(draw, 3, 4, 244, 5, lambda t: mix(gradient(t), GROOVE, 0.55))
    for x, pressed in ((248, False), (278, True)):
        thumb(draw, x, 0, 29, 10, pressed)
    return image


def thumb(draw, x, y, w, h, pressed):
    vertical(draw, x, y, x + w - 1, y + h - 1, (96, 96, 122) if not pressed else (70, 60, 130), (40, 40, 56) if not pressed else (36, 28, 80))
    bevel(draw, x, y, x + w - 1, y + h - 1, (140, 140, 168) if not pressed else (150, 130, 255), EDGE_DARK)
    mid = x + w // 2
    for gx in (mid - 2, mid, mid + 2):
        draw.line((gx, y + 3, gx, y + h - 4), fill=CYAN if pressed else (24, 24, 34))


def slider_bmp(width, balance):
    """volume.bmp and balance.bmp: 28 backgrounds, 15 pixels apart, then the thumbs at y 422."""
    image = Image.new("RGB", (68, 433), BLACK)
    draw = ImageDraw.Draw(image)
    x0 = 9 if balance else 0
    for frame in range(28):
        y = frame * 15
        level = frame / 27
        draw.rectangle((x0, y, x0 + width - 1, y + 12), fill=GROOVE)
        bevel(draw, x0, y, x0 + width - 1, y + 12, EDGE_DARK, (40, 40, 54))
        if balance:
            # A centre notch, and a glow that warms the further the balance leans.
            draw.line((x0 + width // 2, y + 2, x0 + width // 2, y + 10), fill=(44, 44, 60))
            colour = mix((40, 40, 60), mix(CYAN, VIOLET, level), 0.25 + 0.75 * level)
            draw.line((x0 + 3, y + 6, x0 + width - 4, y + 6), fill=colour)
        else:
            # A ramp that fills from the left as the volume rises.
            for i in range(width - 6):
                t = i / (width - 7)
                height = 1 + round(t * 7)
                lit = t <= level
                colour = gradient(t) if lit else (26, 26, 36)
                draw.line((x0 + 3 + i, y + 10 - height, x0 + 3 + i, y + 10), fill=colour)
    for x, pressed in ((15, False), (0, True)):
        thumb(draw, x, 422, 14, 11, pressed)
    return image


def shufrep_bmp():
    image = Image.new("RGB", (92, 85), BLACK)
    draw = ImageDraw.Draw(image)

    def toggle(x, y, w, h, label, on, pressed):
        if pressed:
            vertical(draw, x, y, x + w - 1, y + h - 1, (16, 14, 28), (30, 26, 50))
            bevel(draw, x, y, x + w - 1, y + h - 1, EDGE_DARK, (60, 52, 100))
        else:
            vertical(draw, x, y, x + w - 1, y + h - 1, (52, 52, 70), (26, 26, 36))
            bevel(draw, x, y, x + w - 1, y + h - 1, FACE_LIGHT, EDGE_DARK)
        # An LED on the left, lit when the option is on.
        led = CYAN if on else (40, 40, 52)
        draw.rectangle((x + 3, y + h // 2 - 1, x + 4, y + h // 2), fill=led)
        tiny_text(draw, x + 7, y + (h - 5) // 2, label, TEXT if on else MUTED)

    for row, (on, pressed) in enumerate(((False, False), (False, True), (True, False), (True, True))):
        toggle(0, row * 15, 28, 15, "REP", on, pressed)
        toggle(28, row * 15, 47, 15, "SHUFFLE", on, pressed)
    for x, pressed in ((0, False), (46, True)):
        toggle(x, 61, 23, 12, "EQ", False, pressed)
        toggle(x + 23, 61, 23, 12, "PL", False, pressed)
        toggle(x, 73, 23, 12, "EQ", True, pressed)
        toggle(x + 23, 73, 23, 12, "PL", True, pressed)
    return image


def text_bmp():
    image = Image.new("RGB", (155, 18), LCD)
    draw = ImageDraw.Draw(image)
    for row, line in enumerate(TEXT_LAYOUT):
        for column, c in enumerate(line):
            glyph = FONT.get(c, FONT[" "])
            for y, bits in enumerate(glyph):
                for x, bit in enumerate(bits):
                    if bit == "#":
                        draw.point((column * 5 + x, row * 6 + y), fill=LCD_TEXT)
    return image


def viscolor_txt():
    # Background, dots, 16 bar colours (top to bottom), 5 oscilloscope colours, peaks.
    colours = [LCD, (28, 28, 40)]
    colours += [mix(mix((240, 171, 252), VIOLET, min(1, i / 7)), CYAN, max(0, (i - 7) / 8)) for i in range(16)]
    colours += [(245, 245, 255), (214, 204, 255), (180, 164, 255), (143, 124, 240), (108, 90, 208)]
    colours += [(255, 255, 255)]
    names = ["background", "dots"] + [f"bar {i + 1}" for i in range(16)] + [f"scope {i + 1}" for i in range(5)] + ["peaks"]
    return "".join(f"{r},{g},{b}, // {name}\n" for (r, g, b), name in zip(colours, names))


README = """Lucent Classic
==============

Lucent's own classic skin, drawn for Lucent in Lucent's colours. It uses the Winamp 2 skin layout, so it
also works in other players that load .wsz skins.

Released under the same licence as Lucent.
"""


def bmp_bytes(image):
    out = io.BytesIO()
    image.save(out, format="BMP")
    return out.getvalue()


def build():
    sheets = {
        "main.bmp": main_bmp(),
        "titlebar.bmp": titlebar_bmp(),
        "cbuttons.bmp": cbuttons_bmp(),
        "numbers.bmp": numbers_bmp(),
        "playpaus.bmp": playpaus_bmp(),
        "monoster.bmp": monoster_bmp(),
        "posbar.bmp": posbar_bmp(),
        "volume.bmp": slider_bmp(68, balance=False),
        "balance.bmp": slider_bmp(38, balance=True),
        "shufrep.bmp": shufrep_bmp(),
        "text.bmp": text_bmp(),
    }
    return sheets


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default=OUTPUT)
    parser.add_argument("--sheets", help="also save each sheet as a .png in this folder")
    args = parser.parse_args()

    sheets = build()
    # A fixed date, so the same drawing gives the same file.
    stamp = (2026, 10, 20, 0, 0, 0)
    with zipfile.ZipFile(args.output, "w", zipfile.ZIP_DEFLATED) as wsz:
        for name, image in sheets.items():
            wsz.writestr(zipfile.ZipInfo(name, stamp), bmp_bytes(image), zipfile.ZIP_DEFLATED)
        wsz.writestr(zipfile.ZipInfo("viscolor.txt", stamp), viscolor_txt(), zipfile.ZIP_DEFLATED)
        wsz.writestr(zipfile.ZipInfo("readme.txt", stamp), README, zipfile.ZIP_DEFLATED)

    if args.sheets:
        os.makedirs(args.sheets, exist_ok=True)
        for name, image in sheets.items():
            image.resize((image.width * 4, image.height * 4), Image.NEAREST).save(
                os.path.join(args.sheets, name.replace(".bmp", ".png")))
    print(f"Wrote {os.path.normpath(args.output)}")


if __name__ == "__main__":
    main()
