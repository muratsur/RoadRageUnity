"""Paints a Rodin car's windscreen clean. Rodin bakes fake reflections into the glass,
which read as cracks. The glass is found from the mesh: forward-facing, steeply raked,
central faces above the bonnet. Their texture islands become plain dark tinted glass.

    blender -b -P Tools/Rodin/clean_car_glass.py -- <source.fbx> <diffuse in> <diffuse out> [min height] [max up]

The car faces -Y in Rodin's file. Pillow is not needed; Blender's own images are used.
"""
import sys

import bpy
from mathutils import Vector

source, diffuse_in, diffuse_out, *rest = sys.argv[sys.argv.index("--") + 1:]
# Per car: lowest height of the glass (share of the car), steepest-to-flat limit.
MIN_HEIGHT = float(rest[0]) if rest else 0.56
MAX_UP = float(rest[1]) if len(rest) > 1 else 0.92
GLASS = (0.035, 0.045, 0.055)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=source)
ob = next(o for o in bpy.data.objects if o.type == "MESH")
me = ob.data
world = ob.matrix_world
rotation = world.to_3x3().normalized()
verts = [world @ v.co for v in me.vertices]
lo = Vector(min(v[i] for v in verts) for i in range(3))
hi = Vector(max(v[i] for v in verts) for i in range(3))
size = hi - lo

image = bpy.data.images.load(diffuse_in)
w, h = image.size
pixels = list(image.pixels)
uv = me.uv_layers.active.data


def fill(tri):
    (ax, ay), (bx, by), (cx, cy) = tri
    x0, x1 = int(min(ax, bx, cx)) - 1, int(max(ax, bx, cx)) + 2
    y0, y1 = int(min(ay, by, cy)) - 1, int(max(ay, by, cy)) + 2
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-6:
        return
    for y in range(max(0, y0), min(h, y1)):
        for x in range(max(0, x0), min(w, x1)):
            px, py = x + 0.5, y + 0.5
            a = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d
            b = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d
            if a >= -0.08 and b >= -0.08 and a + b <= 1.08:
                i = (y * w + x) * 4
                pixels[i:i + 3] = GLASS


count = 0
for p in me.polygons:
    n = (rotation @ p.normal).normalized()
    c = sum((verts[i] for i in p.vertices), Vector()) / len(p.vertices)
    height = (c.z - lo.z) / size.z
    across = abs(c.x - (lo.x + hi.x) / 2) / (size.x / 2)
    along = (c.y - lo.y) / size.y
    # Forward and up, raked at 25-65 degrees, above the bonnet and between the pillars.
    if n.y < -0.4 and 0.3 < n.z < MAX_UP and height > MIN_HEIGHT and across < 0.8 and along < 0.62:
        tri = [(uv[l].uv[0] * w, uv[l].uv[1] * h) for l in p.loop_indices]
        fill(tri)
        count += 1
print("GLASS faces", count, "of", len(me.polygons))
image.pixels = pixels
image.filepath_raw = diffuse_out
image.file_format = "PNG"
image.save()
