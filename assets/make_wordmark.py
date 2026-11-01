"""Builds the README's Lucent wordmark: the slim lens beside "Lucent" set in Jost, as plain outlines.

    python make_wordmark.py <Jost-Regular.ttf> <output folder>

Writes lucent-wordmark-dark.svg (warm white, for GitHub's dark mode) and lucent-wordmark-light.svg (black).
The letters are converted to paths so GitHub draws them without the font. Needs fontTools.
"""
import math
import sys
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

INK = {"dark": "#EEEAE2", "light": "#000000"}


def wave(u):
    """The icon's inner wave (make_icon.py): one sine wave, eased to leave both tips level."""
    return math.sin(2 * math.pi * u) * abs(math.sin(math.pi * u)) ** 0.6 / 0.84113


def lens(x0, cy, width, stroke):
    """The vesica lens and its wave, as two stroked paths."""
    h = width / (2 * math.sqrt(3))
    r = width / math.sqrt(3)
    x1 = x0 + width
    outline = f"M{x0:.2f} {cy:.2f}A{r:.2f} {r:.2f} 0 0 1 {x1:.2f} {cy:.2f}A{r:.2f} {r:.2f} 0 0 1 {x0:.2f} {cy:.2f}Z"
    pts = [(x0 + width * i / 64, cy - 0.43 * h * wave(i / 64)) for i in range(65)]
    middle = "M" + " L".join(f"{x:.2f} {y:.2f}" for x, y in pts)
    return (f'<g fill="none" stroke-width="{stroke}" stroke-linecap="round" stroke-linejoin="round">'
            f'<path d="{outline}"/><path d="{middle}"/></g>'), h


def word(font, text, x, baseline, size, tracking):
    glyphs = font.getGlyphSet()
    cmap = font.getBestCmap()
    scale = size / font["head"].unitsPerEm
    paths = []
    for ch in text:
        name = cmap[ord(ch)]
        pen = SVGPathPen(glyphs)
        glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, x, baseline)))
        paths.append(pen.getCommands())
        x += glyphs[name].width * scale + tracking
    return f'<path d="{" ".join(paths)}"/>', x - tracking


def main(font_path, out):
    font = TTFont(font_path)
    size, cap = 120, font["OS/2"].sCapHeight / font["head"].unitsPerEm * 120
    pad, stroke, width = 6, 7, 132
    mark, h = lens(pad + stroke / 2, pad + cap / 2 + 4, width, stroke)
    text, end = word(font, "Lucent", pad + width + stroke + 34, pad + cap + 4, size, 2)
    w, ht = math.ceil(end + pad), math.ceil(cap + 8 + 2 * pad)
    for mode, ink in INK.items():
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{ht}" viewBox="0 0 {w} {ht}">'
               f'<title>Lucent</title><g fill="{ink}" stroke="{ink}">{mark}<g stroke="none">{text}</g></g></svg>\n')
        Path(out, f"lucent-wordmark-{mode}.svg").write_text(svg)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
