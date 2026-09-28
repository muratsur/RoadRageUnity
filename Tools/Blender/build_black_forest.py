"""Builds Greenwood's Black Forest vegetation in headless Blender: Norway spruce, silver
fir, young spruce, a dead snag, and ground cover (fern, bilberry, moor grass), all on
one generated texture atlas so every piece is one material and one draw call.

The kit trees Greenwood used were stylised broadleaf and "pine" models that read as
neither the Black Forest nor any real forest, and the handful of them repeated
everywhere. These are built the way game foliage is: a tapered trunk and whorls of
alpha-cut branch cards, each card tilted and drooped so the crown has depth from the
road, with the spruce's hanging "comb" shoots and dark needles, the fir's flatter
two-ranked sprays and paler undersides.

Runs without the Blender app, through Blender's Python module (needs Python 3.11):

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1 numpy pillow
    .venv/bin/python Tools/Blender/build_black_forest.py

Output (./out_black_forest next to this script, or $RR_OUT):
  SM_spruce_01..04.fbx, SM_fir_01..02.fbx, SM_spruce_young.fbx, SM_snag.fbx,
  SM_fern.fbx, SM_bilberry.fbx, SM_moor_grass.fbx
  T_blackforest_D.png (RGBA, alpha = cutout), T_blackforest_N.jpg
  preview.png
The game loads them from Assets/Resources/Biomes/BlackForest (Meshes/, Textures/).
Axes: +Z up in Blender, exported Y up; the base of every piece is at the origin.
"""
import bpy  # noqa: must load before bmesh and mathutils
import math
import os
import random

import numpy as np
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_black_forest"))
os.makedirs(OUT, exist_ok=True)
ATLAS = 2048
SS = 2                      # supersampling for the drawn cards
rng = random.Random(500)

# ---------------------------------------------------------------- atlas layout
# (x0, y0, x1, y1) in atlas pixels, y down (image space).
REGIONS = {
    "spruce_bark": (0, 0, 192, 2048),
    "fir_bark": (192, 0, 384, 2048),
    "snag_bark": (384, 0, 512, 2048),
    "spruce_a": (512, 0, 1280, 512),
    "spruce_b": (512, 512, 1280, 1024),
    "fir": (1280, 0, 2048, 512),
    "young": (1280, 512, 2048, 1024),
    "dead": (512, 1024, 1280, 1536),
    "fern": (1280, 1024, 2048, 1536),
    "bilberry": (512, 1536, 1280, 2048),
    "grass": (1280, 1536, 2048, 2048),
}


def uv_of(region, u, v):
    """Card coordinates (u along 0..1 left->right, v 0..1 bottom->top) to atlas UV."""
    x0, y0, x1, y1 = REGIONS[region]
    px = x0 + 2 + u * (x1 - x0 - 4)
    py = y1 - 2 - v * (y1 - y0 - 4)
    return (px / ATLAS, 1.0 - py / ATLAS)


# ---------------------------------------------------------------- textures

def noise2(w, h, scale, seed, octaves=4):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cw, ch = max(2, int(w / scale * 2 ** o)), max(2, int(h / scale * 2 ** o))
        g = r.random((ch + 1, cw + 1))
        img = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += np.asarray(img, np.float64) / 255 * amp
        total += amp
        amp *= 0.5
    return out / total


def bark(w, h, seed, base, dark, plates):
    """Vertical bark: plates (spruce: small flaky reddish scales; fir: smooth grey)."""
    n1 = noise2(w, h, 40, seed)
    n2 = noise2(w, h, 8, seed + 1, 3)
    streak = noise2(max(2, w // 6), h, 6, seed + 2, 3)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)) / 255.0
    cracks = np.clip((np.abs(streak - 0.5) - 0.02) * 12, 0, 1)
    scales = np.clip((n2 - 0.35) * plates, 0, 1)
    t = np.clip(0.55 * n1 + 0.45 * scales, 0, 1) * cracks
    col = np.array(dark)[None, None, :] * (1 - t[..., None]) + np.array(base)[None, None, :] * t[..., None]
    height = t
    return col, height


def draw_line(d, p0, p1, width, fill):
    d.line([p0, p1], fill=fill, width=max(1, int(round(width))))


def needle_card(w, h, seed, kind):
    """A conifer branch spray seen from above: stem from the left edge (the trunk)
    to the tip on the right, side shoots, needles. Returns RGBA float array."""
    r = random.Random(seed)
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    mid = H * 0.5
    stem = (78, 56, 38, 255)

    if kind in ("spruce", "young"):
        needle_cols = [(18, 38, 20), (22, 46, 24), (28, 55, 28), (34, 62, 30), (44, 74, 36)]
        needle_len, needle_w, density = 7 * SS, 1.6 * SS, 1.0
    elif kind == "fir":
        needle_cols = [(30, 58, 28), (38, 68, 32), (46, 80, 38), (58, 92, 46)]
        needle_len, needle_w, density = 9 * SS, 2.0 * SS, 0.8
    else:  # dead
        needle_cols = []
        needle_len, needle_w, density = 0, 0, 0

    def shoot(x0, y0, ang, length, level):
        pts = [(x0, y0)]
        x, y, a = x0, y0, ang
        steps = max(4, int(length / (6 * SS)))
        for i in range(steps):
            a += r.uniform(-0.05, 0.05)
            if kind == "spruce" and level > 0:
                a += 0.012            # hanging comb shoots curve down
            x += math.cos(a) * length / steps
            y += math.sin(a) * length / steps
            pts.append((x, y))
        width = (3.2 if level == 0 else 1.8 if level == 1 else 1.1) * SS
        if kind == "dead":
            width *= 0.8
        for p, q in zip(pts, pts[1:]):
            draw_line(d, p, q, width, stem if kind != "dead" else (96, 84, 72, 255))
        if level < 2 and kind != "dead":
            spacing = (22 if level == 0 else 13) * SS
            count = int(length / spacing)
            for k in range(1, count):
                t = k / count
                px, py = pts[min(len(pts) - 1, int(t * (len(pts) - 1)))]
                for side in (-1, 1):
                    if kind == "spruce" and level == 0 and side == 1 and r.random() < 0.35:
                        continue
                    sub_len = length * (0.42 if level == 0 else 0.35) * (1 - 0.55 * t) * r.uniform(0.8, 1.15)
                    sub_ang = a + side * r.uniform(0.7, 1.05)
                    if kind == "spruce" and level == 0 and side == 1:
                        sub_ang = a + r.uniform(1.1, 1.4)   # combs hanging below the branch
                    shoot(px, py, sub_ang, sub_len, level + 1)
        elif kind == "dead" and level < 2:
            for k in range(1, 7):
                px, py = pts[min(len(pts) - 1, int(k / 7 * (len(pts) - 1)))]
                shoot(px, py, a + r.choice((-1, 1)) * r.uniform(0.6, 1.1), length * 0.3 * (1 - k / 8), level + 1)
        # Needles all along the shoot.
        if needle_cols:
            for p, q in zip(pts, pts[1:]):
                seg = math.hypot(q[0] - p[0], q[1] - p[1])
                n = int(seg / (1.4 * SS) * density) + 1
                for j in range(n):
                    t = j / n
                    cx, cy = p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t
                    base = math.atan2(q[1] - p[1], q[0] - p[0])
                    for side in (-1, 1):
                        if kind == "fir":
                            na = base + side * r.uniform(1.2, 1.5)      # two-ranked, flat
                        else:
                            na = base + side * r.uniform(0.5, 1.4) + r.uniform(-0.3, 0.3)
                        ln = needle_len * r.uniform(0.7, 1.1) * (0.8 if level == 2 else 1.0)
                        c = r.choice(needle_cols)
                        shade = r.uniform(0.85, 1.12)
                        colour = tuple(int(min(255, ch * shade)) for ch in c) + (255,)
                        draw_line(d, (cx, cy), (cx + math.cos(na) * ln, cy + math.sin(na) * ln), needle_w, colour)
        return pts

    droop = 0.04 if kind == "fir" else 0.08
    shoot(2 * SS, mid - H * 0.08, droop, W * 0.96, 0)
    img = img.resize((w, h), Image.LANCZOS)
    arr = np.asarray(img, np.float64) / 255.0
    if kind != "dead":
        # A spray tapers to its tip and has a ragged edge. Without this the needles
        # filled the card to its rectangle and the crown read as stacked plates.
        yy, xx = np.mgrid[0:h, 0:w]
        u = xx / w
        v = (yy - h * 0.46) / (h * 0.5)
        half = 1.0 * (1 - 0.55 * u ** 1.6) * np.clip(u / 0.06, 0, 1) ** 0.5
        edge = noise2(w, h, 18, seed + 7, 3)
        envelope = np.clip((half - np.abs(v)) * 5 + (edge - 0.5) * 1.6, 0, 1)
        holes = np.clip((noise2(w, h, 10, seed + 9, 2) - 0.18) * 4, 0, 1)
        arr[..., 3] *= envelope * holes
    return arr


def fern_card(w, h, seed):
    """A bracken/fern frond: rachis from bottom-left curving up and right, pinnae."""
    r = random.Random(seed)
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = []
    for i in range(40):
        t = i / 39
        pts.append((W * (0.03 + 0.94 * t), H * (0.55 - 0.25 * math.sin(t * math.pi * 0.9))))
    for p, q in zip(pts, pts[1:]):
        draw_line(d, p, q, 2.4 * SS, (70, 90, 40, 255))
    cols = [(52, 96, 34), (64, 110, 40), (78, 124, 46), (58, 104, 38)]
    for i in range(2, len(pts) - 1):
        t = i / len(pts)
        px, py = pts[i]
        a = math.atan2(pts[i + 1][1] - py, pts[i + 1][0] - px)
        for side in (-1, 1):
            ln = H * 0.36 * math.sin(min(1, t * 1.15) * math.pi) * r.uniform(0.85, 1.05)
            pa = a + side * 1.25
            x, y = px, py
            segs = 8
            for s in range(segs):
                nx, ny = x + math.cos(pa) * ln / segs, y + math.sin(pa) * ln / segs
                lobe = (1 - s / segs) * 4.2 * SS
                d.polygon([(x, y), (nx, ny), (nx + math.cos(pa + 1.57) * lobe, ny + math.sin(pa + 1.57) * lobe)],
                          fill=r.choice(cols) + (255,))
                d.polygon([(x, y), (nx, ny), (nx - math.cos(pa + 1.57) * lobe, ny - math.sin(pa + 1.57) * lobe)],
                          fill=r.choice(cols) + (255,))
                x, y = nx, ny
    img = img.resize((w, h), Image.LANCZOS)
    return np.asarray(img, np.float64) / 255.0


def bilberry_card(w, h, seed):
    """Low bilberry (Vaccinium) sprigs: green angular stems, small oval leaves."""
    r = random.Random(seed)
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    leaf = [(62, 104, 40), (74, 118, 46), (88, 128, 52), (100, 120, 50)]
    for s in range(34):
        x = r.uniform(0.05, 0.95) * W
        y = H
        a = -math.pi / 2 + r.uniform(-0.5, 0.5)
        length = r.uniform(0.45, 0.95) * H
        steps = 10
        for i in range(steps):
            a += r.uniform(-0.35, 0.35)
            nx, ny = x + math.cos(a) * length / steps, y + math.sin(a) * length / steps
            draw_line(d, (x, y), (nx, ny), 1.8 * SS, (66, 98, 44, 255))
            if i > 1:
                for side in (-1, 1):
                    la = a + side * r.uniform(0.6, 1.1)
                    lx, ly = nx + math.cos(la) * 9 * SS, ny + math.sin(la) * 9 * SS
                    rx, ry = 8 * SS, 5 * SS
                    d.ellipse([lx - rx, ly - ry, lx + rx, ly + ry], fill=r.choice(leaf) + (255,))
            if r.random() < 0.05:
                d.ellipse([nx - 3 * SS, ny - 3 * SS, nx + 3 * SS, ny + 3 * SS], fill=(40, 44, 90, 255))
            x, y = nx, ny
    img = img.resize((w, h), Image.LANCZOS)
    return np.asarray(img, np.float64) / 255.0


def grass_card(w, h, seed):
    """Moor grass (Molinia) tussock: dense blades, green below, straw above."""
    r = random.Random(seed)
    W, H = w * SS, h * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for b in range(220):
        x = W * (0.5 + r.gauss(0, 0.16))
        y = H
        a = -math.pi / 2 + r.gauss(0, 0.35)
        length = r.uniform(0.4, 0.98) * H
        dry = r.random()
        col = (int(90 + 90 * dry), int(110 + 60 * dry), int(50 + 40 * dry))
        steps = 8
        for i in range(steps):
            a += 0.03 * (1 if a > -math.pi / 2 else -1)
            nx, ny = x + math.cos(a) * length / steps, y + math.sin(a) * length / steps
            width = 2.6 * SS * (1 - i / steps) + 0.6 * SS
            c = tuple(int(ch * (0.75 + 0.35 * i / steps)) for ch in col)
            draw_line(d, (x, y), (nx, ny), width, c + (255,))
            x, y = nx, ny
    img = img.resize((w, h), Image.LANCZOS)
    return np.asarray(img, np.float64) / 255.0


def build_atlas():
    rgba = np.zeros((ATLAS, ATLAS, 4))
    height = np.zeros((ATLAS, ATLAS))

    def put(region, arr, hgt=None):
        x0, y0, x1, y1 = REGIONS[region]
        rgba[y0:y1, x0:x1] = arr
        height[y0:y1, x0:x1] = hgt if hgt is not None else arr[..., 3] * 0.6

    for region, seed, base, dark, plates in (
            ("spruce_bark", 1, (0.40, 0.25, 0.17), (0.16, 0.10, 0.07), 3.0),
            ("fir_bark", 2, (0.46, 0.44, 0.40), (0.24, 0.23, 0.21), 1.2),
            ("snag_bark", 3, (0.52, 0.49, 0.45), (0.26, 0.24, 0.22), 2.0)):
        x0, y0, x1, y1 = REGIONS[region]
        col, hgt = bark(x1 - x0, y1 - y0, seed, base, dark, plates)
        put(region, np.concatenate([col, np.ones(col.shape[:2] + (1,))], -1), hgt)

    def card(region, arr):
        x0, y0, x1, y1 = REGIONS[region]
        a = arr.copy()
        # Colour bleeds into the transparent texels so mips don't halo black.
        rgb = Image.fromarray((a[..., :3] * 255).astype(np.uint8))
        alpha = a[..., 3]
        for _ in range(6):
            grown = np.asarray(rgb.filter(ImageFilter.MaxFilter(5)), np.float64) / 255
            mask = alpha[..., None] > 0.05
            a[..., :3] = np.where(mask, a[..., :3], grown)
            rgb = Image.fromarray((a[..., :3] * 255).astype(np.uint8))
        put(region, a)

    for region, seed, kind in (("spruce_a", 11, "spruce"), ("spruce_b", 12, "spruce"),
                               ("fir", 13, "fir"), ("young", 14, "young"), ("dead", 15, "dead")):
        x0, y0, x1, y1 = REGIONS[region]
        card(region, needle_card(x1 - x0, y1 - y0, seed, kind))
    x0, y0, x1, y1 = REGIONS["fern"]
    card("fern", fern_card(x1 - x0, y1 - y0, 21))
    x0, y0, x1, y1 = REGIONS["bilberry"]
    card("bilberry", bilberry_card(x1 - x0, y1 - y0, 22))
    x0, y0, x1, y1 = REGIONS["grass"]
    card("grass", grass_card(x1 - x0, y1 - y0, 23))

    Image.fromarray((np.clip(rgba, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, "T_blackforest_D.png"))
    # Tangent-space normal from the height field.
    hb = np.asarray(Image.fromarray((height * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2)), np.float64) / 255
    gx = np.gradient(hb, axis=1) * 6
    gy = np.gradient(hb, axis=0) * 6
    n = np.stack([-gx, gy, np.ones_like(hb)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "T_blackforest_N.jpg"), quality=92)
    print("RR_ATLAS written")


# ---------------------------------------------------------------- meshes

class MeshBuilder:
    def __init__(self):
        self.verts, self.faces, self.uvs = [], [], []

    def quad(self, corners, uvs):
        base = len(self.verts)
        self.verts.extend(corners)
        self.faces.append((base, base + 1, base + 2, base + 3))
        self.uvs.append(uvs)

    def trunk(self, height, radius, region, sides=8, rings=8, lean=(0.0, 0.0)):
        rows = []
        for i in range(rings + 1):
            t = i / rings
            r = radius * (1 - t) ** 0.9 + 0.01
            flare = 1 + 0.6 * max(0.0, 0.08 - t) / 0.08
            cx, cy = lean[0] * t * t * height, lean[1] * t * t * height
            rows.append([Vector((cx + math.cos(a) * r * flare, cy + math.sin(a) * r * flare, t * height))
                         for a in (2 * math.pi * k / sides for k in range(sides + 1))])
        for i in range(rings):
            for k in range(sides):
                u0, u1 = k / sides, (k + 1) / sides
                v0, v1 = i / rings, (i + 1) / rings
                self.quad([rows[i][k], rows[i][k + 1], rows[i + 1][k + 1], rows[i + 1][k]],
                          [uv_of(region, u0, v0), uv_of(region, u1, v0), uv_of(region, u1, v1), uv_of(region, u0, v1)])

    def card(self, origin, direction, length, width, region, droop, roll, segments=3, tipup=0.0):
        """A branch card: from the trunk outwards along `direction` (horizontal unit
        vector), dropping by `droop` (radians) and curving back up by `tipup`, the
        card plane rolled about its own axis by `roll`."""
        d = Vector((direction.x, direction.y, 0)).normalized()
        side = Vector((-d.y, d.x, 0))
        up = Vector((0, 0, 1))
        pts = [Vector(origin)]
        ang = -droop
        for s in range(segments):
            ang += tipup / segments
            step = length / segments
            pts.append(pts[-1] + (d * math.cos(ang) + up * math.sin(ang)) * step)
        normal_side = (side * math.cos(roll) + up * math.sin(roll))
        for s in range(segments):
            a, b = pts[s], pts[s + 1]
            u0, u1 = s / segments, (s + 1) / segments
            ha = normal_side * width * 0.5 * (0.55 + 0.45 * (1 - u0))
            hb = normal_side * width * 0.5 * (0.55 + 0.45 * (1 - u1))
            self.quad([a - ha, b - hb, b + hb, a + ha],
                      [uv_of(region, u0, 0.0), uv_of(region, u1, 0.0), uv_of(region, u1, 1.0), uv_of(region, u0, 1.0)])

    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        uv = me.uv_layers.new()
        for poly, face_uvs in zip(me.polygons, self.uvs):
            for li, loop in enumerate(poly.loop_indices):
                uv.data[loop].uv = face_uvs[li]
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        for p in me.polygons:
            p.use_smooth = True
        return ob


def conifer(name, height, kind, seed, crown_base=0.28, radius_frac=0.17):
    """Norway spruce ("spruce") or silver fir ("fir"): whorls of branch cards on a
    conical crown. Spruce: branches droop and curve back up, with hanging combs;
    fir: level branches, a rounder, flatter top."""
    r = random.Random(seed)
    b = MeshBuilder()
    b.trunk(height, height / 62, "spruce_bark" if kind != "fir" else "fir_bark",
            lean=(r.uniform(-0.01, 0.01), r.uniform(-0.01, 0.01)))
    whorl_step = 0.55 if height > 12 else 0.35
    z = height * crown_base
    phase = r.uniform(0, math.tau)
    while z < height - 0.3:
        t = (z - height * crown_base) / (height * (1 - crown_base))       # 0 crown base .. 1 top
        if kind == "fir":
            radius = height * radius_frac * (1 - t ** 1.35) * (0.9 + 0.2 * (1 - t))
        else:
            radius = height * radius_frac * (1 - t) ** 1.05
        radius = max(radius, 0.35)
        per = 5 if radius < 1.2 else 6 if radius < 3 else 8
        phase += 2.4
        for k in range(per):
            a = phase + math.tau * k / per + r.uniform(-0.25, 0.25)
            direction = Vector((math.cos(a), math.sin(a), 0))
            length = radius * r.uniform(0.85, 1.12)
            region = ("spruce_a" if r.random() < 0.5 else "spruce_b") if kind == "spruce" else kind
            if kind == "fir":
                droop, tipup = math.radians(r.uniform(2, 10)), math.radians(r.uniform(0, 6))
            else:
                droop = math.radians(r.uniform(18, 32) * (1 - 0.5 * t))
                tipup = math.radians(r.uniform(18, 30))
            b.card(Vector((0, 0, z)), direction, length, length * 0.75, region, droop,
                   math.radians(r.uniform(10, 70)) * r.choice((-1, 1)))
        z += whorl_step * r.uniform(0.85, 1.15)
    # Leader: two crossed vertical cards.
    for a in (0.0, math.pi / 2):
        d = Vector((math.cos(a), math.sin(a), 0))
        top = Vector((0, 0, height))
        base = Vector((0, 0, height - 1.6))
        w = d * 0.35
        b.quad([base - w, base + w, top + w * 0.2, top - w * 0.2],
               [uv_of("young", 0.0, 0.2), uv_of("young", 0.0, 0.8), uv_of("young", 1.0, 0.6), uv_of("young", 1.0, 0.4)])
    # A few dead lower branches below the crown.
    zz = height * 0.08
    while zz < height * crown_base:
        a = r.uniform(0, math.tau)
        b.card(Vector((0, 0, zz)), Vector((math.cos(a), math.sin(a), 0)), r.uniform(0.8, 1.8), 0.7, "dead",
               math.radians(r.uniform(5, 25)), math.radians(r.uniform(-40, 40)), segments=1)
        zz += r.uniform(0.8, 1.6)
    return b.to_object(name)


def snag(name, height, seed):
    r = random.Random(seed)
    b = MeshBuilder()
    b.trunk(height, height / 55, "snag_bark", lean=(r.uniform(-0.03, 0.03), r.uniform(-0.03, 0.03)))
    z = height * 0.2
    while z < height - 0.5:
        a = r.uniform(0, math.tau)
        ln = (height - z) * 0.12 + r.uniform(0.3, 0.9)
        b.card(Vector((0, 0, z)), Vector((math.cos(a), math.sin(a), 0)), ln, ln * 0.6, "dead",
               math.radians(r.uniform(10, 35)), math.radians(r.uniform(-50, 50)), segments=1)
        z += r.uniform(0.5, 1.4)
    return b.to_object(name)


def ground_plant(name, region, count, length, width, droop, seed, upright=False):
    """Fronds / sprigs / blades radiating from the base: arching cards (fern) or
    crossed upright cards (bilberry, grass)."""
    r = random.Random(seed)
    b = MeshBuilder()
    for k in range(count):
        a = math.tau * k / count + r.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        if upright:
            w = d * width * 0.5 * r.uniform(0.8, 1.2)
            h = length * r.uniform(0.8, 1.15)
            lean = Vector((-d.y, d.x, 0)) * h * r.uniform(-0.12, 0.12)
            b.quad([-w, w, w + lean + Vector((0, 0, h)), -w + lean + Vector((0, 0, h))],
                   [uv_of(region, 0, 0), uv_of(region, 1, 0), uv_of(region, 1, 1), uv_of(region, 0, 1)])
        else:
            b.card(Vector((0, 0, 0.05)), d, length * r.uniform(0.8, 1.15), width, region,
                   -math.radians(droop), math.radians(r.uniform(-25, 25)), segments=4,
                   tipup=-math.radians(droop * 2.2))
    return b.to_object(name)


# ---------------------------------------------------------------- material, export, preview

def atlas_material():
    m = bpy.data.materials.new("M_blackforest")
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(OUT, "T_blackforest_D.png"))
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.85
    try:
        m.blend_method = "CLIP"
    except AttributeError:
        pass
    return m


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    build_atlas()
    objs = [
        conifer("SM_spruce_01", 28.0, "spruce", 101),
        conifer("SM_spruce_02", 32.0, "spruce", 102, crown_base=0.34, radius_frac=0.15),
        conifer("SM_spruce_03", 24.0, "spruce", 103, crown_base=0.22, radius_frac=0.19),
        conifer("SM_spruce_04", 30.0, "spruce", 104, crown_base=0.40, radius_frac=0.14),
        conifer("SM_fir_01", 30.0, "fir", 201, crown_base=0.35, radius_frac=0.16),
        conifer("SM_fir_02", 26.0, "fir", 202, crown_base=0.30, radius_frac=0.18),
        conifer("SM_spruce_young", 6.0, "spruce", 301, crown_base=0.04, radius_frac=0.24),
        snag("SM_snag", 22.0, 401),
        ground_plant("SM_fern", "fern", 9, 1.1, 0.55, 35, 501),
        ground_plant("SM_bilberry", "bilberry", 5, 0.5, 0.8, 0, 502, upright=True),
        ground_plant("SM_moor_grass", "grass", 4, 0.8, 0.9, 0, 503, upright=True),
    ]
    mat = atlas_material()
    for o in objs:
        o.data.materials.append(mat)
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, o.name + ".fbx"), use_selection=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="OFF", use_tspace=True,
                                 path_mode="STRIP", add_leaf_bones=False)
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        print(f"RR_MESH {o.name} tris={tris} size={tuple(round(v, 1) for v in o.dimensions)}")
    preview(objs)
    print("RR_DONE")


def preview(objs):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = int(os.environ.get("RR_SAMPLES", "48"))
    scene.cycles.use_denoising = True
    x = -40.0
    for o in objs:
        o.location = (x, 0, 0)
        x += 9.0 if o.dimensions.z > 10 else 3.0
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
    g = bpy.context.active_object
    gm = bpy.data.materials.new("g")
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.09, 0.06, 0.04, 1)
    g.data.materials.append(gm)
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.66, 0.72, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 22
    cam.location = (0.0, -48.0, 6.0)
    cam.rotation_euler = (Vector((0, 0, 11)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1600, 800
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
