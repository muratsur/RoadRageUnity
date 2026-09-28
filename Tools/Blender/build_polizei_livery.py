"""Builds the German police decal texture (Pillow only, no Blender needed):

    python3 Tools/Blender/build_polizei_livery.py

Writes Assets/Resources/Police/T_polizei.png, 1024 x 512:
  top half     the side and rear band: POLIZEI in white on police blue, a thin
               silver line above and below (the Baden-Wuerttemberg blue-silver cars)
  bottom half  the light bar's sign panel: POLIZEI lit up on black, used with
               emission so it reads at night and in fog
"""
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Police", "T_polizei.png")
W, H = 1024, 512
BLUE, SILVER, WHITE = (18, 52, 128), (200, 204, 210), (250, 250, 250)


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                 "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def fit(text, width, size):
    while font(size).getlength(text) > width and size > 20:
        size -= 4
    return font(size)


img = Image.new("RGB", (W, H), BLUE)
d = ImageDraw.Draw(img)
d.rectangle([0, 0, W, 14], fill=SILVER)
d.rectangle([0, 242, W, 256], fill=SILVER)
d.text((W / 2, 128), "POLIZEI", fill=WHITE, font=fit("POLIZEI", W - 120, 180), anchor="mm")
d.rectangle([0, 256, W, H], fill=(6, 6, 8))
d.text((W / 2, 384), "POLIZEI", fill=(215, 230, 255), font=fit("POLIZEI", W - 100, 200), anchor="mm")
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(os.path.abspath(OUT), optimize=True)
print("RR_DONE", os.path.abspath(OUT))
