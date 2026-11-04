"""Draws FDR Drive's facade textures: one bay (4 m) by one floor (3.5 m) of a New York wall, tiling
in both directions, as the game maps them onto extruded OSM buildings (FacadeBay, FacadeFloor).

    python Tools/Terrain/make_facades.py

Writes Assets/Resources/Biomes/NewYork/Textures/T_facade_{brick,stone,glass}_D.png.
"""
import os
import random

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "NewYork", "Textures")
W, H = 256, 224   # 4 m x 3.5 m at 64 px per metre


def noise(d, base, spread, rnd):
    for _ in range(3000):
        x, y = rnd.randrange(W), rnd.randrange(H)
        k = rnd.randint(-spread, spread)
        d.point((x, y), tuple(max(0, min(255, c + k)) for c in base))


def window(d, x0, y0, x1, y1, frame, glass):
    d.rectangle([x0, y0, x1, y1], fill=frame)
    d.rectangle([x0 + 5, y0 + 5, x1 - 5, y1 - 5], fill=glass)
    d.line([(x0 + 5, (y0 + y1) // 2), (x1 - 5, (y0 + y1) // 2)], fill=frame, width=3)
    # A little sky in the upper pane.
    d.rectangle([x0 + 5, y0 + 5, x1 - 5, y0 + 5 + (y1 - y0) // 5], fill=tuple(min(255, c + 25) for c in glass))


def brick(rnd):
    im = Image.new("RGB", (W, H), (128, 62, 46))
    d = ImageDraw.Draw(im)
    for y in range(0, H, 8):
        off = 0 if (y // 8) % 2 else 12
        for x in range(-24 + off, W, 24):
            c = (rnd.randint(112, 148), rnd.randint(50, 70), rnd.randint(38, 52))
            d.rectangle([x, y, x + 22, y + 6], fill=c)
    window(d, 70, 54, 186, 190, (222, 218, 205), (52, 62, 74))
    d.rectangle([62, 190, 194, 198], fill=(196, 190, 176))   # sill
    return im


def stone(rnd):
    im = Image.new("RGB", (W, H), (196, 184, 160))
    d = ImageDraw.Draw(im)
    noise(d, (196, 184, 160), 10, rnd)
    for y in range(0, H, 28):
        d.line([(0, y), (W, y)], fill=(176, 164, 142), width=2)
    window(d, 58, 44, 198, 196, (92, 84, 72), (48, 58, 70))
    d.rectangle([0, H - 14, W, H], fill=(182, 170, 148))       # floor band
    return im


def glass(rnd):
    im = Image.new("RGB", (W, H), (70, 96, 120))
    d = ImageDraw.Draw(im)
    for x in (0, 128):
        d.rectangle([x + 4, 4, x + 124, H - 30], fill=(rnd.randint(64, 84), rnd.randint(92, 110), rnd.randint(118, 136)))
        d.rectangle([x + 4, 4, x + 124, 40], fill=(120, 146, 170))
    for x in (0, 126, 252):
        d.rectangle([x, 0, x + 4, H], fill=(150, 156, 162))      # mullions
    d.rectangle([0, H - 30, W, H], fill=(46, 56, 66))               # spandrel
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    rnd = random.Random(7)
    for name, draw in (("brick", brick), ("stone", stone), ("glass", glass)):
        path = os.path.join(OUT, f"T_facade_{name}_D.png")
        draw(rnd).save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
