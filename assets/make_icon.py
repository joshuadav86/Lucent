import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

S = 1024
out_dir = sys.argv[1]

def gradient(c1, c2, diagonal=True):
    y, x = np.mgrid[0:S, 0:S] / (S - 1)
    t = (x + y) / 2 if diagonal else y
    t = t[..., None]
    rgb = np.array(c1) * (1 - t) + np.array(c2) * t
    return Image.fromarray(rgb.astype(np.uint8), "RGB")

# Background: a dark rounded square with a faint violet lift at the top.
bg = gradient((24, 19, 38), (6, 6, 9), diagonal=False).convert("RGBA")
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((24, 24, S - 24, S - 24), radius=230, fill=255)


# A tunnel of rings shrinking towards a vanishing point: the "flying inward" look.
# Each ring shifts from violet on the outside to cyan deep inside.
colours = [(168, 85, 247), (139, 92, 246), (99, 102, 241), (59, 130, 246), (14, 165, 233), (34, 211, 238), (103, 232, 249)]
rings = Image.new("L", (S, S), 0)
coloured = Image.new("RGBA", (S, S), (0, 0, 0, 0))
cx, cy = S / 2, S / 2
radius, width, alpha = 330.0, 44.0, 255.0
for k in range(7):
    ox, oy = cx + k * 9, cy - k * 13
    ring = Image.new("L", (S, S), 0)
    ImageDraw.Draw(ring).ellipse((ox - radius, oy - radius, ox + radius, oy + radius), outline=int(alpha), width=max(int(width), 3))
    coloured = Image.composite(Image.new("RGBA", (S, S), colours[k] + (255,)), coloured, ring)
    rings = ImageChops.lighter(rings, ring)
    radius *= 0.70
    width *= 0.74
    alpha *= 0.93
accent = coloured

# The bright point at the end of the tunnel.
end = Image.new("L", (S, S), 0)
ex, ey = cx + 7 * 9, cy - 7 * 13
ImageDraw.Draw(end).ellipse((ex - 26, ey - 26, ex + 26, ey + 26), fill=255)

# Soft coloured light around the rings, added on top of the background.
lit = Image.new("RGB", (S, S), (0, 0, 0))
lit = Image.composite(coloured.convert("RGB"), lit, rings)
halo = ImageChops.add(lit.filter(ImageFilter.GaussianBlur(22)).point(lambda v: int(v * 0.45)),
                     lit.filter(ImageFilter.GaussianBlur(80)).point(lambda v: int(v * 0.35)))
icon = ImageChops.add(bg.convert("RGB"), halo)
icon = Image.composite(coloured.convert("RGB"), icon, rings)
white = Image.new("RGB", (S, S), (255, 255, 255))
cyan = Image.new("RGB", (S, S), (165, 243, 252))
icon = Image.composite(cyan, icon, end.filter(ImageFilter.GaussianBlur(40)).point(lambda v: min(255, int(v * 1.6))))
icon = Image.composite(white, icon, end)
icon = icon.convert("RGBA")

final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
final.paste(icon, (0, 0), mask)
final.save(f"{out_dir}/Lucid.png")
final.resize((256, 256), Image.LANCZOS).save(f"{out_dir}/Lucid.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
final.resize((512, 512), Image.LANCZOS).save(f"{out_dir}/preview.png")
