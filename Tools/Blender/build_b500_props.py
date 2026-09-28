"""Builds the B500's roadside furniture in headless Blender, on one generated atlas:

  SM_leitpfosten        German delineator post (white, black band, reflector)
  SM_sign_b500          yellow "B 500" route marker on a post
  SM_sign_place_NN      white place-name sign per named stop (Mummelsee, Ruhestein ...)
  SM_signpost_hike      Westweg trail signpost (the trail follows the B500)
  SM_woodpile           a stacked log pile (Holzpolter), as along every forest road
  SM_log_fallen         a fallen spruce log
  SM_stump              a cut stump
  SM_hotel              a Black Forest hotel: plaster ground floor, dark timber upper
                        floor, a big steep hipped roof

Place names come from Assets/Resources/Biomes/Routes/b500_places.txt (written by
Tools/Terrain/build_b500_road.py); sign NN is the line index there.

Runs without the Blender app, through Blender's Python module (needs Python 3.11):

    .venv/bin/python Tools/Blender/build_b500_props.py

Output (./out_b500_props next to this script, or $RR_OUT): the meshes above as FBX,
T_b500props_D.jpg, T_b500props_N.jpg, preview.png. The game loads them from
Assets/Resources/Biomes/BlackForest (Meshes/, Textures/).
Axes: +Z up, fronts (sign faces, the hotel's front) facing -Y; base at the origin.
"""
import bpy  # noqa: must load before mathutils
import math
import os
import random

import numpy as np
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_b500_props"))
PLACES = os.path.join(ROOT, "Assets", "Resources", "Biomes", "Routes", "b500_places.txt")
os.makedirs(OUT, exist_ok=True)
ATLAS = 2048
rng = random.Random(77)


def font(size, bold=True):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf",
                 "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                 "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


# ---------------------------------------------------------------- atlas

REGIONS = {
    "bark": (0, 0, 256, 2048),
    "endgrain": (256, 0, 512, 256),
    "leitpfosten": (256, 256, 320, 1024),
    "metal": (320, 256, 512, 448),
    "wood": (320, 448, 512, 640),
    "white": (320, 640, 512, 832),
    "b500": (512, 0, 1024, 256),
    "westweg": (1024, 0, 1536, 256),
    "arrow": (1536, 0, 2048, 256),
    "plaster": (256, 1024, 1024, 1536),
    "timber": (1024, 1024, 1792, 1536),
    "door": (1792, 1024, 2048, 1536),
    "roof": (256, 1536, 1024, 2048),
    "trim": (1024, 1536, 1536, 2048),
    "plain": (1536, 1536, 2048, 2048),
}
PLACE_SLOT = (512, 256, 512, 128)   # x0, y0, slot w, slot h: 3 columns x 6 rows


def place_region(i):
    x0, y0, w, h = PLACE_SLOT
    col, row = i % 3, i // 3
    return (x0 + col * w, y0 + row * h, x0 + (col + 1) * w, y0 + (row + 1) * h)


def region(name):
    if name.startswith("place"):
        return place_region(int(name[5:]))
    return REGIONS[name]


def uv(name, u, v):
    x0, y0, x1, y1 = region(name)
    px = x0 + 1 + u * (x1 - x0 - 2)
    py = y1 - 1 - v * (y1 - y0 - 2)
    return (px / ATLAS, 1.0 - py / ATLAS)


def noise(w, h, scale, seed, octaves=4):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        g = r.random((max(2, int(h / scale * 2 ** o)) + 1, max(2, int(w / scale * 2 ** o)) + 1))
        out += np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), float) / 255 * amp
        tot += amp
        amp *= 0.5
    return out / tot


def build_atlas(places):
    img = Image.new("RGB", (ATLAS, ATLAS), (128, 128, 128))
    height = np.full((ATLAS, ATLAS), 0.5)

    def fill(name, arr, hgt=None):
        x0, y0, x1, y1 = region(name)
        img.paste(Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8)), (x0, y0))
        if hgt is not None:
            height[y0:y1, x0:x1] = hgt

    def size(name):
        x0, y0, x1, y1 = region(name)
        return x1 - x0, y1 - y0

    # Bark: reddish spruce, vertical furrows.
    w, h = size("bark")
    n = noise(w, h, 40, 1)
    streak = np.asarray(Image.fromarray((noise(max(2, w // 8), h, 8, 2) * 255).astype(np.uint8)).resize((w, h)), float) / 255
    t = np.clip(0.5 * n + 0.5 * np.clip((np.abs(streak - 0.5) - 0.03) * 8, 0, 1), 0, 1)
    fill("bark", (np.array([0.16, 0.10, 0.07]) * (1 - t)[..., None] + np.array([0.42, 0.28, 0.19]) * t[..., None]), t)
    # End grain: rings, pale fresh-cut wood, darker bark rim.
    w, h = size("endgrain")
    yy, xx = np.mgrid[0:h, 0:w]
    r = np.hypot(xx - w / 2, yy - h / 2) / (w / 2)
    rings = 0.5 + 0.5 * np.sin(r * 38 + noise(w, h, 20, 3) * 3)
    wood = np.array([0.78, 0.62, 0.40]) * (0.85 + 0.15 * rings)[..., None]
    wood = np.where((r > 0.9)[..., None], np.array([0.25, 0.16, 0.10]), wood)
    fill("endgrain", wood, rings * 0.3)
    # Leitpfosten: white post, black band near the top, orange/white reflector in it.
    w, h = size("leitpfosten")
    arr = np.ones((h, w, 3)) * 0.92
    arr[int(h * 0.12):int(h * 0.36)] = 0.06
    arr[int(h * 0.17):int(h * 0.31), int(w * 0.25):int(w * 0.75)] = (1.0, 0.55, 0.1)
    fill("leitpfosten", arr)
    fill("metal", np.ones(size("metal")[::-1] + (3,)) * 0.55 + (noise(*size("metal"), 10, 4)[..., None] - 0.5) * 0.1)
    wn = noise(*size("wood"), 12, 5)
    fill("wood", np.array([0.52, 0.38, 0.22]) * (0.8 + 0.3 * wn)[..., None], wn * 0.5)
    fill("white", np.ones(size("white")[::-1] + (3,)) * 0.93)

    def sign(name, bg, border, text, fg, text_size, diamond=False, arrow=False):
        w, h = size(name)
        s = Image.new("RGB", (w, h), bg)
        d = ImageDraw.Draw(s)
        d.rectangle([6, 6, w - 7, h - 7], outline=border, width=9)
        f = font(text_size)
        tx = w / 2
        if diamond:
            cx, cy, rr = 70, h / 2, 48
            d.polygon([(cx, cy - rr), (cx + rr * 0.62, cy), (cx, cy + rr), (cx - rr * 0.62, cy)], fill=(200, 20, 30))
            tx = w / 2 + 40
        if arrow:
            d.polygon([(w - 12, h / 2), (w - 70, 18), (w - 70, h - 18)], fill=fg)
            tx = w / 2 - 30
        d.text((tx, h / 2), text, fill=fg, font=f, anchor="mm")
        fill(name, np.asarray(s, float) / 255)

    sign("b500", (250, 200, 10), (10, 10, 10), "B 500", (10, 10, 10), 150)
    sign("westweg", (245, 245, 240), (40, 40, 40), "Westweg", (30, 30, 30), 88, diamond=True)
    sign("arrow", (250, 205, 20), (30, 30, 30), "Wanderweg", (30, 30, 30), 70, arrow=True)
    for i, name in enumerate(places[:18]):
        w, h = size(f"place{i}")
        text = name
        fsize = 78
        while font(fsize).getlength(text) > w - 50 and fsize > 30:
            fsize -= 4
        sign(f"place{i}", (245, 245, 240), (20, 20, 20), text, (15, 15, 15), fsize)

    # Hotel: plaster ground floor bay with a window and flower box.
    def facade(name, wall_col, frame_col, boards=False):
        w, h = size(name)
        n = noise(w, h, 30, 11 + len(name))
        base = np.array(wall_col)[None, None, :] * (0.9 + 0.12 * n)[..., None]
        if boards:
            xx = np.arange(w)[None, :]
            base *= (0.85 + 0.15 * (np.sin(xx / w * math.tau * 9) > -0.9))[..., None]
            base *= (1 - 0.35 * (np.abs(((xx / (w / 9)) % 1) - 0.5) > 0.47))[..., None]
        s = Image.fromarray((np.clip(base, 0, 1) * 255).astype(np.uint8))
        d = ImageDraw.Draw(s)
        wx0, wx1, wy0, wy1 = w * 0.32, w * 0.68, h * 0.22, h * 0.72
        d.rectangle([wx0 - 14, wy0 - 14, wx1 + 14, wy1 + 14], fill=frame_col)
        d.rectangle([wx0, wy0, wx1, wy1], fill=(34, 40, 48))
        d.line([((wx0 + wx1) / 2, wy0), ((wx0 + wx1) / 2, wy1)], fill=frame_col, width=10)
        d.line([(wx0, (wy0 + wy1) / 2), (wx1, (wy0 + wy1) / 2)], fill=frame_col, width=10)
        for sx in (wx0 - 70, wx1 + 14):   # shutters
            d.rectangle([sx, wy0 - 14, sx + 56, wy1 + 14], fill=(40, 70, 45))
        d.rectangle([wx0 - 20, wy1 + 14, wx1 + 20, wy1 + 50], fill=(90, 55, 30))   # flower box
        for k in range(26):
            fx = rng.uniform(wx0 - 16, wx1 + 16)
            d.ellipse([fx - 9, wy1 + 2, fx + 9, wy1 + 20], fill=rng.choice([(200, 30, 40), (220, 60, 80), (60, 120, 40)]))
        fill(name, np.asarray(s, float) / 255)

    facade("plaster", (0.92, 0.9, 0.84), (240, 240, 235))
    facade("timber", (0.30, 0.19, 0.11), (235, 230, 220), boards=True)
    w, h = size("door")
    s = Image.new("RGB", (w, h), (232, 228, 218))
    d = ImageDraw.Draw(s)
    d.rectangle([w * 0.18, h * 0.25, w * 0.82, h], fill=(82, 50, 28))
    d.rectangle([w * 0.18, h * 0.25, w * 0.82, h], outline=(50, 30, 15), width=8)
    fill("door", np.asarray(s, float) / 255)
    # Roof: dark shingles in rows.
    w, h = size("roof")
    yy, xx = np.mgrid[0:h, 0:w]
    row = (yy // 22)
    jitter = noise(w, h, 6, 21)
    shingle = 0.18 + 0.06 * jitter + 0.03 * ((xx + (row % 2) * 16) // 32 % 3)
    shingle *= 1 - 0.45 * ((yy % 22) < 3)
    fill("roof", np.stack([shingle * 1.05, shingle * 0.95, shingle * 0.9], -1), 1 - ((yy % 22) < 3))
    fill("trim", np.array([0.24, 0.15, 0.09]) * (0.85 + 0.2 * noise(*size("trim"), 10, 22))[..., None])
    fill("plain", np.array([0.9, 0.88, 0.82]) * (0.92 + 0.1 * noise(*size("plain"), 20, 23))[..., None])

    img.save(os.path.join(OUT, "T_b500props_D.jpg"), quality=92)
    hb = np.asarray(Image.fromarray((height * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5)), float) / 255
    gx, gy = np.gradient(hb, axis=1) * 4, np.gradient(hb, axis=0) * 4
    nrm = np.stack([-gx, gy, np.ones_like(hb)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    Image.fromarray(((nrm * 0.5 + 0.5) * 255).astype(np.uint8)).save(os.path.join(OUT, "T_b500props_N.jpg"), quality=92)


# ---------------------------------------------------------------- meshes

class Builder:
    def __init__(self):
        self.v, self.f, self.uvs = [], [], []

    def poly(self, pts, uvs):
        base = len(self.v)
        self.v.extend(Vector(p) for p in pts)
        self.f.append(tuple(range(base, base + len(pts))))
        self.uvs.append(uvs)

    def quad(self, a, b, c, d, reg, u0=0, v0=0, u1=1, v1=1):
        self.poly([a, b, c, d], [uv(reg, u0, v0), uv(reg, u1, v0), uv(reg, u1, v1), uv(reg, u0, v1)])

    def box(self, x0, x1, y0, y1, z0, z1, reg, front=None, back=None):
        """Axis box; front is the -Y face (towards the road), back the +Y face."""
        P = lambda x, y, z: (x, y, z)
        self.quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1), front or reg)
        self.quad(P(x1, y1, z0), P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), back or reg)
        self.quad(P(x0, y1, z0), P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), reg)
        self.quad(P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1), reg)
        self.quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1), reg)

    def log(self, a, b, radius, sides=8, cap=True):
        """Cylinder from a to b, bark sides, end-grain caps."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        side = axis.cross(Vector((0, 0, 1)))
        if side.length < 1e-3:
            side = Vector((1, 0, 0))
        side.normalize()
        up = side.cross(axis)
        ring = [side * math.cos(t) * radius + up * math.sin(t) * radius
                for t in (math.tau * k / sides for k in range(sides + 1))]
        length = (b - a).length
        for k in range(sides):
            self.quad(a + ring[k], a + ring[k + 1], b + ring[k + 1], b + ring[k], "bark",
                      k / sides, 0, (k + 1) / sides, min(1, length / 12))
        if cap:
            for end, sign in ((a, -1), (b, 1)):
                pts = [end + ring[k] for k in range(sides)]
                if sign < 0:
                    pts = pts[::-1]
                self.poly(pts, [uv("endgrain", 0.5 + 0.5 * math.cos(math.tau * k / sides) * (1 if sign > 0 else -1),
                                   0.5 + 0.5 * math.sin(math.tau * k / sides)) for k in range(sides)][::(1 if sign > 0 else -1)])

    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(p) for p in self.v], [], self.f)
        layer = me.uv_layers.new()
        for poly, face_uvs in zip(me.polygons, self.uvs):
            for li, loop in enumerate(poly.loop_indices):
                layer.data[loop].uv = face_uvs[li]
        me.update()
        me.validate()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        # Faces were written without caring for their winding; make them face out.
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        ob.select_set(False)
        return ob


def leitpfosten():
    b = Builder()
    b.box(-0.06, 0.06, -0.05, 0.05, 0.0, 1.0, "white", front="leitpfosten", back="leitpfosten")
    return b.to_object("SM_leitpfosten")


def sign_on_post(name, reg, width, height, bottom, posts=1):
    b = Builder()
    xs = [0.0] if posts == 1 else [-width * 0.3, width * 0.3]
    for x in xs:
        b.box(x - 0.04, x + 0.04, 0.02, 0.1, 0.0, bottom + height, "metal")
    b.box(-width / 2, width / 2, -0.02, 0.02, bottom, bottom + height, "metal", front=reg)
    return b.to_object(name)


def signpost_hike():
    b = Builder()
    b.box(-0.06, 0.06, -0.06, 0.06, 0.0, 2.6, "wood")
    b.box(-0.14, 0.14, -0.075, -0.06, 1.9, 2.18, "westweg")
    for z, rot in ((2.3, 0.35), (1.55, -0.5)):
        # An arrow board sticking out from the post, turned about the post.
        c, s = math.cos(rot), math.sin(rot)
        L, H = 0.85, 0.2
        pts = [Vector((0.06, -0.02, z)), Vector((0.06 + L, -0.02, z)), Vector((0.06 + L, -0.02, z + H)),
               Vector((0.06, -0.02, z + H))]
        pts = [Vector((p.x * c - p.y * s, p.x * s + p.y * c, p.z)) for p in pts]
        b.poly(pts, [uv("arrow", 0, 0), uv("arrow", 1, 0), uv("arrow", 1, 1), uv("arrow", 0, 1)])
        back = [p + Vector((-s * 0.03, c * 0.03, 0)) for p in pts][::-1]
        b.poly(back, [uv("arrow", 0, 1), uv("arrow", 1, 1), uv("arrow", 1, 0), uv("arrow", 0, 0)][::-1])
    return b.to_object("SM_signpost_hike")


def woodpile():
    b = Builder()
    r0 = 0.2
    rows = [8, 7, 6, 5, 3]
    for level, count in enumerate(rows):
        z = r0 + level * r0 * 1.72
        for k in range(count):
            x = (k - (count - 1) / 2) * r0 * 2.02
            rad = r0 * rng.uniform(0.85, 1.1)
            off = rng.uniform(-0.25, 0.25)
            b.log((x, -2.0 + off, z), (x, 2.0 + off, z), rad)
    # Posts holding the stack at the ends.
    for x in (-1.75, 1.75):
        b.box(x - 0.06, x + 0.06, -0.06, 0.06, 0.0, 1.6, "bark")
    return b.to_object("SM_woodpile")


def fallen_log():
    b = Builder()
    b.log((-4.5, 0, 0.28), (4.5, 0.3, 0.2), 0.32, sides=9)
    for x, a in ((-2.0, 0.8), (0.5, -1.0), (2.6, 0.6)):
        b.log((x, 0.1, 0.35), (x + 0.3, 0.1 + math.sin(a) * 0.9, 0.35 + math.cos(a) * 0.5), 0.05, sides=5)
    return b.to_object("SM_log_fallen")


def stump():
    b = Builder()
    b.log((0, 0, -0.1), (0, 0, 0.55), 0.38, sides=10)
    return b.to_object("SM_stump")


def hotel():
    """Two storeys (plaster below, dark timber above) under a steep hipped roof."""
    b = Builder()
    W, D = 16.0, 11.0
    g, u = 3.2, 3.0           # storey heights
    bay = 4.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2

    def wall(p0, p1, z0, z1, reg, door_at=None):
        span = (Vector(p1) - Vector(p0)).length
        n = max(1, round(span / bay))
        for k in range(n):
            a = Vector(p0).lerp(Vector(p1), k / n)
            c = Vector(p0).lerp(Vector(p1), (k + 1) / n)
            r = "door" if door_at is not None and k == door_at else reg
            b.quad(Vector((a.x, a.y, z0)), Vector((c.x, c.y, z0)), Vector((c.x, c.y, z1)), Vector((a.x, a.y, z1)), r)

    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    for i in range(4):
        p0, p1 = corners[i], corners[(i + 1) % 4]
        wall(p0, p1, 0.0, g, "plaster", door_at=2 if i == 0 else None)
        wall(p0, p1, g, g + u, "timber")
    # Balcony band on the front between the storeys.
    b.box(x0 + 1, x1 - 1, y0 - 1.2, y0, g - 0.05, g + 0.15, "trim")
    b.box(x0 + 1, x1 - 1, y0 - 1.25, y0 - 1.15, g + 0.15, g + 1.1, "trim")
    # Hipped roof.
    eave = g + u
    o = 1.3
    pitch = math.radians(50)
    ex0, ex1, ey0, ey1 = x0 - o, x1 + o, y0 - o, y1 + o
    rise = (D / 2 + o) * math.tan(pitch)
    ridge_half = max(0.5, (W - D) / 2)
    top = eave - 0.3 + rise
    r0, r1 = Vector((-ridge_half, 0, top)), Vector((ridge_half, 0, top))
    E = lambda x, y: Vector((x, y, eave - 0.3))
    b.poly([E(ex0, ey0), E(ex1, ey0), r1, r0], [uv("roof", 0, 0), uv("roof", 1, 0), uv("roof", 0.7, 1), uv("roof", 0.3, 1)])
    b.poly([E(ex1, ey1), E(ex0, ey1), r0, r1], [uv("roof", 0, 0), uv("roof", 1, 0), uv("roof", 0.7, 1), uv("roof", 0.3, 1)])
    b.poly([E(ex1, ey0), E(ex1, ey1), r1], [uv("roof", 0, 0), uv("roof", 1, 0), uv("roof", 0.5, 1)])
    b.poly([E(ex0, ey1), E(ex0, ey0), r0], [uv("roof", 0, 0), uv("roof", 1, 0), uv("roof", 0.5, 1)])
    # Roof underside (seen from the road below the eaves).
    b.poly([E(ex0, ey0), E(ex0, ey1), E(ex1, ey1), E(ex1, ey0)],
           [uv("trim", 0, 0), uv("trim", 0, 1), uv("trim", 1, 1), uv("trim", 1, 0)])
    # Chimney.
    b.box(2.0, 2.8, 1.0, 1.8, top - 2.5, top + 1.2, "plain")
    return b.to_object("SM_hotel")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    places = [line.split(" ", 1)[1].strip() for line in open(PLACES, encoding="utf-8") if " " in line]
    build_atlas(places)
    objs = [leitpfosten(), sign_on_post("SM_sign_b500", "b500", 1.0, 0.5, 1.6), signpost_hike(),
            woodpile(), fallen_log(), stump(), hotel()]
    for i, _ in enumerate(places[:18]):
        objs.append(sign_on_post(f"SM_sign_place_{i:02d}", f"place{i}", 2.6, 0.65, 1.7, posts=2))

    mat = bpy.data.materials.new("M_b500props")
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(OUT, "T_b500props_D.jpg"))
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    for o in objs:
        o.data.materials.append(mat)
        for p in o.data.polygons:
            p.use_smooth = False
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, o.name + ".fbx"), use_selection=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=True,
                                 path_mode="STRIP", add_leaf_bones=False)
        print(f"RR_MESH {o.name} tris={sum(len(p.vertices) - 2 for p in o.data.polygons)}")
    preview(objs[:7] + objs[7:10])
    print("RR_DONE")


def preview(objs):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = int(os.environ.get("RR_SAMPLES", "32"))
    scene.cycles.use_denoising = True
    x = -14.0
    for o in objs:
        o.location = (x, 0, 0)
        x += 20.0 if o.name == "SM_hotel" else 4.5
    bpy.ops.mesh.primitive_plane_add(size=300)
    g = bpy.context.active_object
    gm = bpy.data.materials.new("g")
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.08, 0.06, 0.04, 1)
    g.data.materials.append(gm)
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.66, 0.72, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 24
    cam.location = (14.0, -32.0, 6.0)
    cam.rotation_euler = (Vector((14.0, 0, 3.0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1600, 700
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
