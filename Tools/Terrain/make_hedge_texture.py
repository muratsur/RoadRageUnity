"""Opaque clipped-hedge texture: small leaves of close colours over dark green, tiling.
Low contrast on purpose: a garden hedge drawn with the see-through vine texture shimmered.

    python Tools/Terrain/make_hedge_texture.py <out.png>
"""
import math
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

S = 1024
random.seed(7)
im = Image.new("RGB", (S, S), (24, 42, 18))
d = ImageDraw.Draw(im)
for _ in range(26000):
    x, y, r, a = random.uniform(0, S), random.uniform(0, S), random.uniform(7, 13), random.uniform(0, 6.3)
    k = random.uniform(0.72, 1.12)
    c = (int(50 * k), int(84 * k), int(32 * k))
    pts = [(r * math.cos(a + i * 1.05) * (1 if i % 2 else 0.6), r * 0.8 * math.sin(a + i * 1.05)) for i in range(6)]
    for ox in (-S, 0, S):
        for oy in (-S, 0, S):
            d.polygon([(x + p + ox, y + q + oy) for p, q in pts], fill=c)
im.filter(ImageFilter.GaussianBlur(0.8)).save(sys.argv[1])
