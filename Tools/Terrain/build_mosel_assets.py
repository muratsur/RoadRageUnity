"""Models and textures for the Mosel Valley: half-timbered village houses with slate
roofs, a ruined hilltop castle, and the vine leaf texture. Needs only Pillow.

Output: Assets/Resources/Biomes/MoselVillage/Meshes/SM_fachwerk_<n>.obj, SM_burg.obj,
Textures/T_fachwerk_D.jpg, T_burg_D.jpg, T_vine_D.jpg. The house door faces +Z.
"""
import math
import os
import random

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "MoselVillage")
ATLAS = 2048
TIMBER = (58, 38, 26)
SLATE = (54, 58, 66)
# Plaster per house: white, ochre, rose, grey-blue.
PLASTER = [(226, 220, 205), (216, 186, 128), (205, 156, 138), (176, 186, 190)]


class Mesh:
    def __init__(self):
        self.v, self.t, self.f = [], [], []

    def quad(self, a, b, c, d, uv):
        """Corners counter-clockwise from outside; uv = (u0, v0, u1, v1) over a-b / a-d."""
        u0, v0, u1, v1 = uv
        base = len(self.v)
        self.v += [a, b, c, d]
        self.t += [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        self.f.append((base, base + 1, base + 2, base + 3))

    def tri(self, a, b, c, uv):
        base = len(self.v)
        self.v += [a, b, c]
        self.t += uv
        self.f.append((base, base + 1, base + 2))

    def box(self, x0, x1, y0, y1, z0, z1, uv, top=None):
        self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), uv)
        self.quad((x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0), uv)
        self.quad((x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), uv)
        self.quad((x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0), uv)
        self.quad((x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0), top or uv)

    def save(self, path):
        with open(path, "w") as f:
            f.write("o model\n")
            for v in self.v:
                f.write("v %.3f %.3f %.3f\n" % v)
            for t in self.t:
                f.write("vt %.4f %.4f\n" % t)
            for face in self.f:
                f.write("f " + " ".join(f"{i + 1}/{i + 1}" for i in face) + "\n")


def noise(draw, box, base, spread, count):
    x0, y0, x1, y1 = box
    for _ in range(count):
        x, y = random.randint(x0, x1 - 1), random.randint(y0, y1 - 1)
        d = random.randint(-spread, spread)
        draw.point((x, y), tuple(max(0, min(255, c + d)) for c in base))


# The atlas is 4 rows (plaster colours) x 4 cells: ground floor, timbered floor, gable,
# gable-end wall. The roof is one strip down the right.
CELL = 448


def cell_uv(row, col):
    x0, y0 = col * CELL, row * CELL
    return (x0 / ATLAS, 1 - (y0 + CELL) / ATLAS, (x0 + CELL) / ATLAS, 1 - y0 / ATLAS)


def window(d, x, y, w, h):
    d.rectangle((x - 5, y - 5, x + w + 5, y + h + 5), fill=TIMBER)
    d.rectangle((x, y, x + w, y + h), fill=(62, 78, 92))
    d.line((x + w / 2, y, x + w / 2, y + h), fill=(232, 228, 215), width=4)
    d.line((x, y + h / 2, x + w, y + h / 2), fill=(232, 228, 215), width=4)


def fachwerk(d, x0, y0, plaster, pattern):
    w = CELL
    d.rectangle((x0, y0, x0 + w, y0 + w), fill=plaster)
    noise(d, (x0, y0, x0 + w, y0 + w), plaster, 10, 9000)
    b = 15
    d.rectangle((x0, y0, x0 + w, y0 + b), fill=TIMBER)
    d.rectangle((x0, y0 + w - b, x0 + w, y0 + w), fill=TIMBER)
    d.rectangle((x0, y0 + w // 2 - b // 2, x0 + w, y0 + w // 2 + b // 2), fill=TIMBER)
    for i in range(5):
        px = x0 + i * w // 4
        d.rectangle((px - b // 2, y0, px + b // 2, y0 + w), fill=TIMBER)
    for i in range(4):
        px = x0 + i * w // 4
        if (i + pattern) % 2 == 0:
            d.line((px, y0 + w // 2, px + w // 4, y0 + w), fill=TIMBER, width=b)
            d.line((px + w // 4, y0 + w // 2, px, y0 + w), fill=TIMBER, width=b)
        else:
            d.line((px, y0 + w, px + w // 4, y0 + w // 2), fill=TIMBER, width=b)
        window(d, px + 32, y0 + 44, w // 4 - 64, w // 2 - 92) if i % 2 == pattern % 2 else None


def build_atlas():
    img = Image.new("RGB", (ATLAS, ATLAS), SLATE)
    d = ImageDraw.Draw(img)
    for row, plaster in enumerate(PLASTER):
        # Ground floor: stone base, door and windows.
        x0, y0 = 0, row * CELL
        d.rectangle((x0, y0, x0 + CELL, y0 + CELL), fill=plaster)
        noise(d, (x0, y0, x0 + CELL, y0 + CELL), plaster, 12, 9000)
        d.rectangle((x0, y0 + 330, x0 + CELL, y0 + CELL), fill=(112, 104, 94))
        noise(d, (x0, y0 + 330, x0 + CELL, y0 + CELL), (112, 104, 94), 24, 9000)
        d.rectangle((x0 + 186, y0 + 150, x0 + 262, y0 + CELL), fill=(70, 40, 24))
        window(d, x0 + 50, y0 + 110, 80, 130)
        window(d, x0 + 318, y0 + 110, 80, 130)
        fachwerk(d, CELL, y0, plaster, row)
        fachwerk(d, 2 * CELL, y0, plaster, row + 1)
    # Slate roof strip, far right.
    rx = 3 * CELL
    d.rectangle((rx, 0, ATLAS, ATLAS), fill=SLATE)
    for y in range(0, ATLAS, 16):
        for x in range(rx + (y // 16 % 2) * 14, ATLAS, 28):
            s = random.randint(-10, 10)
            d.rectangle((x, y, x + 26, y + 14), fill=(SLATE[0] + s, SLATE[1] + s, SLATE[2] + s), outline=(30, 32, 38))
    img.save(os.path.join(OUT, "Textures", "T_fachwerk_D.jpg"), quality=88)


def build_house(n, plaster, width, depth, floors, chimney):
    m = Mesh()
    roof = (3 * CELL / ATLAS, 0.0, 1.0, 1.0)
    ground = cell_uv(plaster, 0)
    upper = [cell_uv(plaster, 1), cell_uv(plaster, 2)]
    hx, hz, fh = width / 2, depth / 2, 2.9
    # Below ground, so the house meets a slope without a gap.
    m.quad((-hx, -4, hz), (hx, -4, hz), (hx, 0, hz), (-hx, 0, hz), (ground[0], ground[1], ground[2], ground[1] + 0.001))
    m.quad((hx, -4, -hz), (-hx, -4, -hz), (-hx, 0, -hz), (hx, 0, -hz), (ground[0], ground[1], ground[2], ground[1] + 0.001))
    m.quad((hx, -4, hz), (hx, -4, -hz), (hx, 0, -hz), (hx, 0, hz), (ground[0], ground[1], ground[2], ground[1] + 0.001))
    m.quad((-hx, -4, -hz), (-hx, -4, hz), (-hx, 0, hz), (-hx, 0, -hz), (ground[0], ground[1], ground[2], ground[1] + 0.001))
    top = 0.0
    for f in range(floors):
        uv = ground if f == 0 else upper[f % 2]
        # Upper floors jetty out over the one below, as real half-timbering does.
        o = 0.0 if f == 0 else 0.22
        y0, y1 = f * fh, (f + 1) * fh
        m.quad((-hx - o, y0, hz + o), (hx + o, y0, hz + o), (hx + o, y1, hz + o), (-hx - o, y1, hz + o), uv)
        m.quad((hx + o, y0, -hz - o), (-hx - o, y0, -hz - o), (-hx - o, y1, -hz - o), (hx + o, y1, -hz - o), uv)
        m.quad((hx + o, y0, hz + o), (hx + o, y0, -hz - o), (hx + o, y1, -hz - o), (hx + o, y1, hz + o), uv)
        m.quad((-hx - o, y0, -hz - o), (-hx - o, y0, hz + o), (-hx - o, y1, hz + o), (-hx - o, y1, -hz - o), uv)
        top = y1
    # Steep slate gable roof, ridge along X, with an overhang.
    e, ridge = 0.5, top + depth * 0.62
    z = hz + 0.7
    m.quad((-hx - e, top, z), (hx + e, top, z), (hx + e, ridge, 0), (-hx - e, ridge, 0), roof)
    m.quad((hx + e, top, -z), (-hx - e, top, -z), (-hx - e, ridge, 0), (hx + e, ridge, 0), roof)
    # Gable ends in timbered plaster.
    u = upper[0]
    tri_uv = [(u[0], u[1]), (u[2], u[1]), ((u[0] + u[2]) / 2, u[3])]
    m.tri((hx, top, hz), (hx, top, -hz), (hx, ridge, 0), tri_uv)
    m.tri((-hx, top, -hz), (-hx, top, hz), (-hx, ridge, 0), tri_uv)
    if chimney:
        c = (roof[0], 0.0, 1.0, 0.04)
        m.box(hx * 0.4, hx * 0.4 + 0.8, top, ridge + 1.2, -0.4, 0.4, c)
    m.save(os.path.join(OUT, "Meshes", f"SM_fachwerk_{n}.obj"))


def build_stone():
    img = Image.new("RGB", (1024, 1024), (118, 106, 92))
    d = ImageDraw.Draw(img)
    for y in range(0, 1024, 40):
        for x in range((y // 40 % 2) * 40 - 40, 1024, 80):
            s = random.randint(-26, 22)
            c = (118 + s, 106 + s, 90 + s)
            d.rectangle((x + 2, y + 2, x + 78, y + 38), fill=c, outline=(66, 58, 50))
    img.save(os.path.join(OUT, "Textures", "T_burg_D.jpg"), quality=88)


def build_castle():
    m = Mesh()

    def ring(cx, cz, r, y0, y1, u_tile, v_tile, sides=16):
        for i in range(sides):
            a0, a1 = 2 * math.pi * i / sides, 2 * math.pi * (i + 1) / sides
            p0 = (cx + r * math.sin(a0), cz + r * math.cos(a0))
            p1 = (cx + r * math.sin(a1), cz + r * math.cos(a1))
            uv = (i * u_tile / sides, 0, (i + 1) * u_tile / sides, v_tile)
            m.quad((p0[0], y0, p0[1]), (p1[0], y0, p1[1]), (p1[0], y1, p1[1]), (p0[0], y1, p0[1]), uv)
        # The crown of merlons; a ruin keeps most of them.
        for i in range(sides):
            if random.random() < 0.2:
                continue
            a = 2 * math.pi * (i + 0.5) / sides
            x, z = cx + r * math.sin(a), cz + r * math.cos(a)
            m.box(x - 0.8, x + 0.8, y1, y1 + 1.5, z - 0.8, z + 0.8, (0, 0, 0.5, 0.5))

    def wall(x0, z0, x1, z1, y0, y1):
        length = math.hypot(x1 - x0, z1 - z0)
        nx, nz = (z1 - z0) / length * 0.8, -(x1 - x0) / length * 0.8
        uv = (0, 0, length / 12, (y1 - y0) / 12)
        for a, b, c, d in ((0, 1, 1, 0), (1, 0, 0, 1)):
            s = 1 if a == 0 else -1
            ax, az = (x0, z0) if a == 0 else (x1, z1)
            bx, bz = (x1, z1) if a == 0 else (x0, z0)
            m.quad((ax + nx * s, y0, az + nz * s), (bx + nx * s, y0, bz + nz * s),
                   (bx + nx * s, y1, bz + nz * s), (ax + nx * s, y1, az + nz * s), uv)
        m.quad((x0 + nx, y1, z0 + nz), (x1 + nx, y1, z1 + nz), (x1 - nx, y1, z1 - nz), (x0 - nx, y1, z0 - nz), (0, 0, 1, 0.1))
        count = int(length / 3)
        for i in range(count):
            if random.random() < 0.25:
                continue
            t = (i + 0.5) / count
            x, z = x0 + (x1 - x0) * t, z0 + (z1 - z0) * t
            m.box(x - 0.7, x + 0.7, y1, y1 + 1.2, z - 0.7, z + 0.7, (0, 0, 0.3, 0.3))

    corners = [(-26, -18), (26, -18), (30, 16), (-22, 22)]
    for i in range(4):
        a, b = corners[i], corners[(i + 1) % 4]
        wall(a[0], a[1], b[0], b[1], -6, 9)
        ring(a[0], a[1], 4.0, -6, 13, 3, 2)
    ring(0, 0, 7.5, -6, 30, 4, 4)
    m.box(-18, 2, -6, 15, 6, 16, (0, 0, 2, 2))
    m.save(os.path.join(OUT, "Meshes", "SM_burg.obj"))


def build_boat():
    """A white Mosel excursion boat, 30 m, using the house atlas's plaster and slate."""
    m = Mesh()
    white = cell_uv(0, 0)
    w = (white[0], white[3] - 0.01, white[0] + 0.02, white[3])
    glass = (cell_uv(0, 1)[0] + 0.03, cell_uv(0, 1)[3] - 0.11, cell_uv(0, 1)[0] + 0.05, cell_uv(0, 1)[3] - 0.09)
    roof = (3 * CELL / ATLAS, 0.0, 1.0, 0.1)
    m.box(-3.4, 3.4, -0.6, 1.3, -13, 13, w)
    m.quad((-3.4, -0.6, 13), (3.4, -0.6, 13), (0, 1.3, 18), (-3.4, 1.3, 13), w)
    m.tri((-3.4, 1.3, 13), (3.4, 1.3, 13), (0, 1.3, 18), [(w[0], w[1]), (w[2], w[1]), (w[0], w[3])])
    m.tri((3.4, -0.6, 13), (3.4, 1.3, 13), (0, 1.3, 18), [(w[0], w[1]), (w[2], w[1]), (w[0], w[3])])
    m.box(-2.9, 2.9, 1.3, 3.5, -11, 11, glass, top=w)
    m.box(-2.6, 2.6, 3.5, 5.3, -5, 8, glass, top=roof)
    m.box(-0.8, 0.8, 3.5, 6.6, -9, -7, w)
    m.save(os.path.join(OUT, "Meshes", "SM_boat.obj"))


def build_vine():
    img = Image.new("RGB", (512, 512), (24, 40, 18))
    d = ImageDraw.Draw(img)
    for _ in range(9000):
        x, y, r = random.randint(0, 511), random.randint(0, 511), random.randint(3, 9)
        s = random.randint(-22, 34)
        d.ellipse((x - r, y - r, x + r, y + r), fill=(50 + s, 76 + s, 30 + s // 2))
    img.save(os.path.join(OUT, "Textures", "T_vine_D.jpg"), quality=85)


if __name__ == "__main__":
    random.seed(53)
    os.makedirs(os.path.join(OUT, "Meshes"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "Textures"), exist_ok=True)
    build_atlas()
    build_house(1, 0, 8.0, 7.0, 3, True)
    build_house(2, 1, 6.5, 6.5, 3, False)
    build_house(3, 2, 10.0, 7.5, 2, True)
    build_house(4, 3, 7.0, 8.0, 4, True)
    build_stone()
    build_castle()
    build_boat()
    build_vine()
    print("wrote", os.path.abspath(OUT))
