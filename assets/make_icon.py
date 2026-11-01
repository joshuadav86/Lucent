"""Builds Lucent's icon: a slim lens with a wave running through it.

    python make_icon.py <output folder>

The lens is a vesica: two equal circles, each passing through the other's centre, so it is exactly sqrt 3 times as
long as it is thick. The wave is one full sine wave, eased to leave each tip level, so the three lines meet in a clean
point instead of the wave folding into the outline.

Writes lucent-icon.svg (the 1024 x 1024 master), lucent-icon.png (README logo and the Mac icon's source),
Lucent.ico (app and window icon, 16 to 256 px) and Lucent-64.png (the title bar). Below 40 px the mark is drawn
larger and bolder, so it still reads as a wave in Explorer and the taskbar. Needs Pillow, NumPy and CairoSVG.
"""
import io
import math
import struct
import sys

import cairosvg
import numpy as np
from PIL import Image


def num(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def curve(f, n):
    """A smooth path through f(0)..f(1), as n cubic Beziers built from points and tangents."""
    ts = np.linspace(0.0, 1.0, n + 1)
    h = 1e-6
    pts = [np.array(f(t)) for t in ts]
    tangents = [(np.array(f(t + h)) - np.array(f(t - h))) / (2 * h) / n for t in ts]
    d = [f"M{num(pts[0][0])} {num(pts[0][1])}"]
    for i in range(n):
        c1 = pts[i] + tangents[i] / 3
        c2 = pts[i + 1] - tangents[i + 1] / 3
        d.append(f"C{num(c1[0])} {num(c1[1])} {num(c2[0])} {num(c2[1])} {num(pts[i + 1][0])} {num(pts[i + 1][1])}")
    return " ".join(d)


def squircle(half, n=5.0):
    def f(t):
        c, s = math.cos(2 * math.pi * t), math.sin(2 * math.pi * t)
        r = half / (abs(c) ** n + abs(s) ** n) ** (1 / n)
        return (512 + r * c, 512 + r * s)
    return curve(f, 48) + " Z"


def wave_shape(u):
    """One full sine wave, eased by sin(pi u) ** 0.6 so it leaves both tips level, scaled to peak at +-1."""
    return math.sin(2 * math.pi * u) * abs(math.sin(math.pi * u)) ** 0.6 / 0.84113


def icon_svg(stroke=46, width=680, wave=0.43):
    """The icon in a 1024 frame. stroke and width size the lens (its thickness follows from the vesica);
    wave is the inner wave's height relative to the lens's half-thickness."""
    x0 = 512 - width / 2
    height = width / (2 * math.sqrt(3))       # half-thickness of a vesica
    radius = width / math.sqrt(3)             # each arc's circle

    def arc(sign):
        def f(u):
            x = x0 + width * u
            return (x, 512 - sign * (math.sqrt(radius * radius - (x - 512) ** 2) - (radius - height)))
        return f

    upper = curve(arc(1), 16)
    lower = curve(arc(-1), 16)
    middle = curve(lambda u: (x0 + width * u, 512 - wave * height * wave_shape(u)), 32)
    tile = squircle(496)
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
<defs>
<linearGradient id="tileFill" gradientUnits="userSpaceOnUse" x1="150" y1="40" x2="870" y2="990"><stop offset="0" stop-color="#13214D"/><stop offset="1" stop-color="#060A19"/></linearGradient>
<radialGradient id="tileGlow" gradientUnits="userSpaceOnUse" cx="512" cy="470" r="520"><stop offset="0" stop-color="#2A55FF" stop-opacity="0.22"/><stop offset="1" stop-color="#2A55FF" stop-opacity="0"/></radialGradient>
<linearGradient id="tileEdge" gradientUnits="userSpaceOnUse" x1="0" y1="16" x2="0" y2="1008"><stop offset="0" stop-color="#FFFFFF" stop-opacity="0.16"/><stop offset="0.5" stop-color="#FFFFFF" stop-opacity="0.04"/><stop offset="1" stop-color="#FFFFFF" stop-opacity="0.07"/></linearGradient>
<linearGradient id="markPaint" gradientUnits="userSpaceOnUse" x1="{num(x0)}" y1="{num(512 - height)}" x2="{num(x0 + width)}" y2="{num(512 + height)}"><stop offset="0" stop-color="#4775FF"/><stop offset="1" stop-color="#2BD9F6"/></linearGradient>
</defs>
<path d="{tile}" fill="url(#tileFill)"/>
<path d="{tile}" fill="url(#tileGlow)"/>
<path d="{squircle(494)}" fill="none" stroke="url(#tileEdge)" stroke-width="3"/>
<g fill="none" stroke="url(#markPaint)" stroke-width="{num(stroke)}" stroke-linecap="round" stroke-linejoin="round">
<path d="{upper}"/>
<path d="{lower}"/>
<path d="{middle}"/>
</g>
</svg>
"""


def design_for(size):
    if size <= 24:
        return dict(stroke=84, width=860)
    if size <= 32:
        return dict(stroke=66, width=780)
    if size <= 40:
        return dict(stroke=56, width=720)
    return {}


def render(svg, size):
    """Renders 16x larger and averages down in linear light, so thin bright lines keep their brightness."""
    scale = 16 if size <= 64 else 4
    png = cairosvg.svg2png(bytestring=svg.encode("utf-8"), output_width=size * scale, output_height=size * scale)
    a = np.asarray(Image.open(io.BytesIO(png)).convert("RGBA")).astype(np.float64) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:4]
    light = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4) * alpha

    def shrink(x):
        return x.reshape(size, scale, size, scale, x.shape[-1]).mean(axis=(1, 3))

    light, alpha = shrink(light), shrink(alpha)
    rgb = np.clip(np.where(alpha > 0, light / np.maximum(alpha, 1e-9), 0.0), 0.0, 1.0)
    rgb = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * rgb ** (1 / 2.4) - 0.055)
    out = np.concatenate([rgb, alpha], axis=-1)
    return Image.fromarray((np.clip(out, 0.0, 1.0) * 255 + 0.5).astype(np.uint8), "RGBA")


def write_ico(path, images):
    """An .ico with one PNG per size (read by Windows Vista and later at every size)."""
    blobs = []
    for image in images:
        buffer = io.BytesIO()
        image.save(buffer, "PNG", optimize=True)
        blobs.append(buffer.getvalue())
    offset = 6 + 16 * len(images)
    entries = b""
    for image, blob in zip(images, blobs):
        width, height = image.size
        entries += struct.pack("<BBBBHHII", width % 256, height % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(images)) + entries + b"".join(blobs))


if __name__ == "__main__":
    out = sys.argv[1]
    with open(f"{out}/lucent-icon.svg", "w", encoding="utf-8") as f:
        f.write(icon_svg())
    render(icon_svg(), 512).save(f"{out}/lucent-icon.png", optimize=True)
    sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    write_ico(f"{out}/Lucent.ico", [render(icon_svg(**design_for(s)), s) for s in sizes])
    # The title bar shows this at 16 px (more on high-DPI screens), so it uses the bold small-size design.
    render(icon_svg(**design_for(24)), 64).save(f"{out}/Lucent-64.png", optimize=True)
