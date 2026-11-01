"""Models and textures for the Iceland biome: a white farmhouse with a red roof, a barn,
Vik's white church with its red roof and steeple, the waterfall's streaked water and
basalt cliff, lupine, farm fence and sheep.
Needs only Pillow.

Output: Assets/Resources/Biomes/Iceland/Meshes/SM_farm.obj, SM_barn.obj, SM_church.obj,
Textures/T_iceland_D.jpg, T_falls_D.jpg. Doors face +Z.
"""
import math
import os
import random

from PIL import Image, ImageDraw

from build_mosel_assets import Mesh

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "Iceland")
SIZE = 1024
# Atlas: left half top white wall with windows, left half bottom plain wall, right half
# top red corrugated roof, right half bottom dark grey corrugated barn.
WALL = (0.0, 0.5, 0.5, 1.0)
PLAIN = (0.0, 0.0, 0.5, 0.5)
ROOF = (0.5, 0.5, 1.0, 1.0)
BARN = (0.5, 0.0, 1.0, 0.5)


def atlas():
    img = Image.new("RGB", (SIZE, SIZE), (232, 232, 226))
    d = ImageDraw.Draw(img)
    h = SIZE // 2
    for y in range(0, h, 14):
        d.line((0, y, h, y), fill=(214, 214, 208))
        d.line((0, h + y, h, h + y), fill=(214, 214, 208))
    for x in (60, 200, 340):
        d.rectangle((x, 120, x + 96, 280), fill=(52, 62, 72), outline=(250, 250, 248), width=8)
        d.line((x + 48, 120, x + 48, 280), fill=(250, 250, 248), width=5)
    d.rectangle((222, 340, 290, h), fill=(150, 40, 34))
    for x in range(h, SIZE, 14):
        s = 12 if (x // 14) % 2 else -8
        d.rectangle((x, 0, x + 14, h), fill=(158 + s, 44 + s, 36 + s))
        g = 74 + s
        d.rectangle((x, h, x + 14, SIZE), fill=(g, g + 4, g + 8))
    img.save(os.path.join(OUT, "Textures", "T_iceland_D.jpg"), quality=88)


def falls():
    img = Image.new("RGB", (512, 1024), (212, 226, 232))
    d = ImageDraw.Draw(img)
    for _ in range(1600):
        x, y, n = random.randint(0, 511), random.randint(0, 1023), random.randint(60, 320)
        s = random.randint(-46, 34)
        c = (226 + s // 2, 236 + s // 2, 240 + s // 2)
        c = tuple(min(255, v) for v in c)
        w = random.randint(1, 5)
        d.line((x, y, x, y + n), fill=c, width=w)
        d.line((x, y - 1024, x, y - 1024 + n), fill=c, width=w)
    img.save(os.path.join(OUT, "Textures", "T_falls_D.jpg"), quality=86)


def gabled(m, hx, hz, wall, roof, gable, height=3.0, pitch=0.7, base=-4.0):
    # Below ground, so the house meets a slope without a gap.
    plain = (PLAIN[0], PLAIN[1], PLAIN[2], PLAIN[1] + 0.001)
    m.quad((-hx, base, hz), (hx, base, hz), (hx, 0, hz), (-hx, 0, hz), plain)
    m.quad((hx, base, -hz), (-hx, base, -hz), (-hx, 0, -hz), (hx, 0, -hz), plain)
    m.quad((hx, base, hz), (hx, base, -hz), (hx, 0, -hz), (hx, 0, hz), plain)
    m.quad((-hx, base, -hz), (-hx, base, hz), (-hx, 0, hz), (-hx, 0, -hz), plain)
    m.quad((-hx, 0, hz), (hx, 0, hz), (hx, height, hz), (-hx, height, hz), wall)
    m.quad((hx, 0, -hz), (-hx, 0, -hz), (-hx, height, -hz), (hx, height, -hz), gable)
    m.quad((hx, 0, hz), (hx, 0, -hz), (hx, height, -hz), (hx, height, hz), gable)
    m.quad((-hx, 0, -hz), (-hx, 0, hz), (-hx, height, hz), (-hx, height, -hz), gable)
    ridge = height + hz * pitch * 2
    e, z = 0.4, hz + 0.5
    m.quad((-hx - e, height, z), (hx + e, height, z), (hx + e, ridge, 0), (-hx - e, ridge, 0), roof)
    m.quad((hx + e, height, -z), (-hx - e, height, -z), (-hx - e, ridge, 0), (hx + e, ridge, 0), roof)
    uv = [(gable[0], gable[1]), (gable[2], gable[1]), (gable[0], gable[3])]
    m.tri((hx, height, hz), (hx, height, -hz), (hx, ridge, 0), uv)
    m.tri((-hx, height, -hz), (-hx, height, hz), (-hx, ridge, 0), uv)
    return ridge


def farm():
    m = Mesh()
    gabled(m, 6.0, 4.0, WALL, ROOF, PLAIN)
    m.save(os.path.join(OUT, "Meshes", "SM_farm.obj"))


def barn():
    m = Mesh()
    gabled(m, 11.0, 6.0, BARN, BARN, BARN, height=3.6, pitch=0.45)
    m.save(os.path.join(OUT, "Meshes", "SM_barn.obj"))


def church():
    m = Mesh()
    ridge = gabled(m, 8.0, 4.0, WALL, ROOF, PLAIN, height=4.0, pitch=1.0)
    # The tower at the west end, under a steep red spire.
    x0, x1, z0, z1, top = -11.0, -7.8, -1.6, 1.6, ridge + 3.0
    m.box(x0, x1, -4, top, z0, z1, PLAIN)
    apex = (-9.4, top + 8.0, 0.0)
    c = (ROOF[0], ROOF[1], ROOF[0] + 0.1, ROOF[1] + 0.3)
    uv = [(c[0], c[1]), (c[2], c[1]), (c[0], c[3])]
    m.tri((x0, top, z1), (x1, top, z1), apex, uv)
    m.tri((x1, top, z1), (x1, top, z0), apex, uv)
    m.tri((x1, top, z0), (x0, top, z0), apex, uv)
    m.tri((x0, top, z0), (x0, top, z1), apex, uv)
    m.save(os.path.join(OUT, "Meshes", "SM_church.obj"))


def cliff():
    """A basalt amphitheatre 72 m high, open towards +Z, with moss on the top: what the
    falls drop down. Column noise gives the wall its ribbed, broken face."""
    m = Mesh()
    rock = (0.0, 0.0, 1.0, 1.0)
    random.seed(7)
    radius, top, base, steps, levels = 46.0, 72.0, -8.0, 36, 7
    out = [[radius + random.uniform(-2.6, 2.6) + 3.0 * math.sin(j * 0.9) for j in range(steps + 1)] for _ in range(levels + 1)]

    def point(level, j):
        a = math.radians(-125 + 250 * j / steps)
        y = base + (top - base) * level / levels
        r = out[level][j] + (level / levels) * -3.0
        return (r * math.sin(a), y, -r * math.cos(a))

    for level in range(levels):
        v0, v1 = level / levels * 3, (level + 1) / levels * 3
        for j in range(steps):
            u0, u1 = j / 6, (j + 1) / 6
            # Seen from inside the bowl, where the road is.
            m.quad(point(level, j), point(level, j + 1), point(level + 1, j + 1), point(level + 1, j), (u0, v0, u1, v1))
    # The mass behind the wall, its top sloping back into the land.
    green = (0.02, 0.02, 0.03, 0.03)
    for j in range(steps):
        a, b = point(levels, j), point(levels, j + 1)
        ra, rb = (a[0] * 1.45, top - 8, a[2] * 1.45), (b[0] * 1.45, top - 8, b[2] * 1.45)
        m.quad(a, b, rb, ra, (0.6, 0.0, 0.62, 0.02))
        ga, gb = (ra[0], base, ra[2]), (rb[0], base, rb[2])
        m.quad(ga, gb, rb, ra, (0.0, 0.0, 1.0, 1.0))
    m.save(os.path.join(OUT, "Meshes", "SM_cliff.obj"))


def rock_texture():
    img = Image.new("RGB", (1024, 1024), (44, 44, 48))
    d = ImageDraw.Draw(img)
    x = 0
    while x < 1024:
        w = random.randint(14, 44)
        s = random.randint(-14, 16)
        d.rectangle((x, 0, x + w, 1024), fill=(46 + s, 46 + s, 50 + s))
        d.line((x, 0, x, 1024), fill=(20, 20, 24), width=3)
        x += w
    for _ in range(900):
        px, py, r = random.randint(0, 1023), random.randint(0, 1023), random.randint(3, 16)
        d.ellipse((px - r, py - r // 2, px + r, py + r // 2), fill=(68 + random.randint(-14, 14), 92 + random.randint(-14, 14), 40))
    img.save(os.path.join(OUT, "Textures", "T_cliff_D.jpg"), quality=86)


def lupine():
    """Purple Nootka lupine, a clump of crossed cards."""
    img = Image.new("RGB", (256, 256), (58, 88, 44))
    d = ImageDraw.Draw(img)
    for _ in range(1500):
        x, y = random.randint(0, 255), random.randint(0, 255)
        c = (92 + random.randint(-20, 26), 66 + random.randint(-14, 14), 168 + random.randint(-24, 30)) if y < 170 else (58, 92 + random.randint(-16, 20), 44)
        d.ellipse((x - 3, y - 4, x + 3, y + 4), fill=c)
    img.save(os.path.join(OUT, "Textures", "T_lupine_D.jpg"), quality=86)
    m = Mesh()
    uv = (0, 0, 1, 1)
    for a in (0.0, 1.05, 2.1):
        c, n, h, w = math.cos(a), math.sin(a), 0.9, 1.1
        p0, p1 = (-w * c, 0, -w * n), (w * c, 0, w * n)
        p2, p3 = (w * c * 0.7, h, w * n * 0.7), (-w * c * 0.7, h, -w * n * 0.7)
        m.quad(p0, p1, p2, p3, uv)
        m.quad(p1, p0, p3, p2, uv)
    m.save(os.path.join(OUT, "Meshes", "SM_lupine.obj"))


def fence():
    """24 m of farm fence along X: posts and three wires."""
    m = Mesh()
    post = (BARN[0], BARN[1], BARN[0] + 0.02, BARN[1] + 0.02)
    for i in range(5):
        x = -12 + i * 6
        m.box(x - 0.05, x + 0.05, -0.4, 1.15, -0.05, 0.05, post)
    for y in (0.45, 0.75, 1.05):
        m.box(-12, 12, y - 0.012, y + 0.012, -0.012, 0.012, post)
    m.save(os.path.join(OUT, "Meshes", "SM_fence.obj"))


def sheep():
    m = Mesh()
    wool = (PLAIN[0], PLAIN[1], PLAIN[0] + 0.02, PLAIN[1] + 0.02)
    dark = (BARN[0], BARN[1], BARN[0] + 0.02, BARN[1] + 0.02)
    m.box(-0.55, 0.55, 0.42, 1.0, -0.32, 0.32, wool)
    m.box(0.5, 0.86, 0.72, 1.06, -0.15, 0.15, dark)
    for x, z in ((-0.42, -0.2), (-0.42, 0.2), (0.42, -0.2), (0.42, 0.2)):
        m.box(x - 0.05, x + 0.05, 0, 0.45, z - 0.05, z + 0.05, dark)
    m.save(os.path.join(OUT, "Meshes", "SM_sheep.obj"))


if __name__ == "__main__":
    random.seed(1)
    os.makedirs(os.path.join(OUT, "Meshes"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "Textures"), exist_ok=True)
    atlas()
    falls()
    farm()
    barn()
    church()
    cliff()
    rock_texture()
    lupine()
    fence()
    sheep()
    print("wrote", os.path.abspath(OUT))
