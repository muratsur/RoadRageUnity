"""Draws FDR Drive's street-level storey: four 6 m shop bays side by side (24 m of wall), 5.5 m
tall, the bottom metre below the pavement. Each bay has stone piers, a coloured sign band, a door
and a shop window with mullions; a granite base runs along the foot. Writes
Assets/Resources/Biomes/NewYork/Textures/T_shopfront_D.png (the game repeats it every 24 m)."""
import os
import random

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "NewYork", "Textures", "T_shopfront_D.png")
PX = 64                      # pixels per metre
BAY, HIGH, BELOW = 6.0, 5.5, 1.0
SIGNS = [(120, 28, 30), (24, 70, 48), (26, 40, 82), (30, 30, 32), (140, 96, 30), (90, 30, 70)]


def m(x):
    return int(round(x * PX))


def main():
    random.seed(7)
    w, h = m(BAY * 4), m(HIGH)
    img = Image.new("RGB", (w, h), (150, 142, 130))
    d = ImageDraw.Draw(img)
    # Up is down in image rows: row 0 is the top of the storey.
    ground = h - m(BELOW)
    d.rectangle([0, ground - m(0.45), w, h], fill=(78, 76, 74))           # granite base, below and at the foot
    for bay in range(4):
        x0 = m(bay * BAY)
        x1 = m((bay + 1) * BAY)
        pier = m(0.45)
        d.rectangle([x0, 0, x0 + pier, ground], fill=(168, 160, 146))      # stone pier
        d.rectangle([x1 - pier, 0, x1, ground], fill=(168, 160, 146))
        sign = SIGNS[(bay * 2 + random.randint(0, 5)) % len(SIGNS)]
        d.rectangle([x0 + pier, m(0.25), x1 - pier, m(1.15)], fill=sign)   # sign band
        for _ in range(random.randint(4, 7)):                               # lettering
            lx = random.randint(x0 + pier + m(0.4), x1 - pier - m(0.9))
            d.rectangle([lx, m(0.5), lx + m(random.uniform(0.25, 0.6)), m(0.9)], fill=(232, 226, 210))
        top = m(1.35)
        door = (x1 - pier - m(1.6), x1 - pier - m(0.3)) if bay % 2 else (x0 + pier + m(0.3), x0 + pier + m(1.6))
        win = (x0 + pier + m(0.2), door[0] - m(0.2)) if bay % 2 else (door[1] + m(0.2), x1 - pier - m(0.2))
        # Shop window: dark glass, a little sky reflected at the top, warm interior below.
        for y in range(top, ground - m(0.5)):
            t = (y - top) / max(1, ground - m(0.5) - top)
            c = (int(70 + 50 * (1 - t)), int(82 + 46 * (1 - t)), int(96 + 40 * (1 - t)))
            if t > 0.55:
                c = (int(c[0] + 40 * (t - 0.55)), int(c[1] + 26 * (t - 0.55)), int(c[2] + 8 * (t - 0.55)))
            d.line([win[0], y, win[1], y], fill=c)
        for k in range(1, 3):                                                 # mullions
            mx = win[0] + (win[1] - win[0]) * k // 3
            d.rectangle([mx - 3, top, mx + 3, ground - m(0.5)], fill=(40, 40, 42))
        d.rectangle([win[0], top, win[1], top + 5], fill=(40, 40, 42))
        d.rectangle([win[0], ground - m(0.55), win[1], ground - m(0.5)], fill=(40, 40, 42))
        # Door: a framed glass door with a transom.
        d.rectangle([door[0], top, door[1], ground - m(0.45)], fill=(36, 34, 32))
        d.rectangle([door[0] + 6, top + m(0.6), door[1] - 6, ground - m(0.55)], fill=(64, 74, 84))
        d.rectangle([door[0] + 6, top + 6, door[1] - 6, top + m(0.45)], fill=(90, 100, 110))
        d.rectangle([door[0] + (door[1] - door[0]) // 2 - 2, top + m(1.6), door[0] + (door[1] - door[0]) // 2 + 2,
                     top + m(2.1)], fill=(190, 170, 110))                     # handle
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print("wrote", OUT, img.size)


if __name__ == "__main__":
    main()
