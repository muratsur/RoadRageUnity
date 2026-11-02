"""Paints a Rodin car's windscreen clean. Rodin bakes fake reflections into the glass,
which read as cracks. The glass is found from the mesh: forward-facing, steeply raked,
central faces above the bonnet. Their texture islands become plain dark tinted glass.

    blender -b -P Tools/Rodin/clean_car_glass.py -- <source.fbx> <car folder> [min height] [max up] [preview.png]

Works in place on the folder's texture_diffuse.png and, where the car has them, its
texture_paint_base.png and texture_paint_mask.png, so repainting never tints the glass.
The car faces -Y in Rodin's file.

The lines that still looked like cracks ran along the seams between the glass's texture
islands: the bare gutter between them kept Rodin's colour and bled in when filtered. So the
paint also spreads into any gutter around it, and takes in a stray face between glass faces.
"""
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

source, folder, *rest = sys.argv[sys.argv.index("--") + 1:]
# Per car: lowest height of the glass (share of the car), steepest-to-flat limit.
MIN_HEIGHT = float(rest[0]) if rest else 0.56
MAX_UP = float(rest[1]) if len(rest) > 1 else 0.92
PREVIEW = rest[2] if len(rest) > 2 else None
GLASS = (0.035, 0.045, 0.055)
GUTTER_PIXELS = 10

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=source)
ob = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.polygons))
me = ob.data
world = ob.matrix_world
rotation = world.to_3x3().normalized()
verts = [world @ v.co for v in me.vertices]
lo = Vector(min(v[i] for v in verts) for i in range(3))
hi = Vector(max(v[i] for v in verts) for i in range(3))
size = hi - lo

glass = set()
for p in me.polygons:
    n = (rotation @ p.normal).normalized()
    c = sum((verts[i] for i in p.vertices), Vector()) / len(p.vertices)
    height = (c.z - lo.z) / size.z
    across = abs(c.x - (lo.x + hi.x) / 2) / (size.x / 2)
    along = (c.y - lo.y) / size.y
    # Forward and up, raked at 25-65 degrees, above the bonnet and between the pillars.
    if n.y < -0.4 and 0.3 < n.z < MAX_UP and height > MIN_HEIGHT and across < 0.8 and along < 0.62:
        glass.add(p.index)
found = len(glass)

by_edge = {}
for p in me.polygons:
    for key in p.edge_keys:
        by_edge.setdefault(key, []).append(p.index)

# The windscreen is one big connected patch. Small patches that merely lean the same way
# are bonnet creases and headlights, and painted black they looked like burn marks.
parts = []
unseen = set(glass)
while unseen:
    part, stack = set(), [unseen.pop()]
    while stack:
        f = stack.pop()
        part.add(f)
        for k in me.polygons[f].edge_keys:
            for o in by_edge[k]:
                if o in unseen:
                    unseen.remove(o)
                    stack.append(o)
    parts.append(part)
biggest = max(map(len, parts), default=0)
dropped = set().union(*[q for q in parts if len(q) < biggest * 0.15], set())
glass -= dropped

# A face with glass on two or more sides is glass whatever way it happens to lean.
for _ in range(3):
    grown = set()
    for p in me.polygons:
        if p.index not in glass and sum(any(o != p.index and o in glass for o in by_edge[k]) for k in p.edge_keys) >= 2:
            grown.add(p.index)
    if not grown:
        break
    glass |= grown

uv = me.uv_layers.active.data


def mask_for(w, h, glass=glass):
    """The given faces' pixels, spread into the gutter around them."""
    painted = np.zeros((h, w), bool)
    other = np.zeros((h, w), bool)
    for p in me.polygons:
        is_glass = p.index in glass
        target, margin = (painted, 0.08) if is_glass else (other, 0.0)
        loops = list(p.loop_indices)
        for k in range(1, len(loops) - 1):
            (ax, ay), (bx, by), (cx, cy) = [(uv[l].uv[0] * w, uv[l].uv[1] * h) for l in (loops[0], loops[k], loops[k + 1])]
            d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(d) < 1e-6:
                continue
            x0, x1 = max(0, int(min(ax, bx, cx)) - 1), min(w, int(max(ax, bx, cx)) + 2)
            y0, y1 = max(0, int(min(ay, by, cy)) - 1), min(h, int(max(ay, by, cy)) + 2)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            a = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
            b = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
            target[y0:y1, x0:x1] |= (a >= -margin) & (b >= -margin) & (a + b <= 1 + margin)
    other &= ~painted
    for _ in range(GUTTER_PIXELS):
        spread = painted.copy()
        spread[1:] |= painted[:-1]
        spread[:-1] |= painted[1:]
        spread[:, 1:] |= painted[:, :-1]
        spread[:, :-1] |= painted[:, 1:]
        painted = spread & ~other
    return painted


masks = {}
for name, colour in (("texture_diffuse.png", GLASS), ("texture_paint_base.png", GLASS), ("texture_paint_mask.png", (0.0, 0.0, 0.0))):
    path = os.path.join(folder, name)
    if not os.path.exists(path):
        continue
    image = bpy.data.images.load(path)
    w, h = image.size
    pixels = np.empty(w * h * 4, np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(h, w, 4)
    mask = masks.setdefault((w, h), mask_for(w, h))
    pixels[..., :3][mask] = colour
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
# REPAIR=1: this car's dropped patches were painted black by an older run; fill them with
# the colour around them.
if os.environ.get("REPAIR") and dropped:
    image = bpy.data.images.load(os.path.join(folder, "texture_diffuse.png"))
    w, h = image.size
    pixels = np.empty(w * h * 4, np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(h, w, 4)
    marks = mask_for(w, h, dropped)
    ring = marks.copy()
    for _ in range(14):
        wider = ring.copy()
        wider[1:] |= ring[:-1]
        wider[:-1] |= ring[1:]
        wider[:, 1:] |= ring[:, :-1]
        wider[:, :-1] |= ring[:, 1:]
        ring = wider
    around = pixels[..., :3][ring & ~marks]
    pixels[..., :3][marks] = np.median(around[around.sum(1) > 0.3], axis=0)
    image.pixels.foreach_set(pixels.ravel())
    image.save()
print("GLASS", os.path.basename(folder), "faces", found, "grown to", len(glass), "of", len(me.polygons))

if PREVIEW:
    material = bpy.data.materials.new("v")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(folder, "texture_diffuse.png"), check_existing=True)
    tex.image.reload()
    material.node_tree.links.new(tex.outputs[0], next(x for x in nodes if x.type == "BSDF_PRINCIPLED").inputs["Base Color"])
    for o in bpy.data.objects:
        if o is not ob:
            bpy.data.objects.remove(o)
    me.materials.clear()
    me.materials.append(material)
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError as e:
        scene.render.engine = str(e).split("'")[1]
    scene.render.resolution_x, scene.render.resolution_y = 500, 360
    w_ = bpy.data.worlds.new("w")
    w_.use_nodes = True
    next(x for x in w_.node_tree.nodes if x.type == "BACKGROUND").inputs[0].default_value = (0.7, 0.72, 0.76, 1)
    scene.world = w_
    camera = bpy.data.cameras.new("c")
    camera.lens = 60
    co = bpy.data.objects.new("c", camera)
    bpy.context.collection.objects.link(co)
    scene.camera = co
    mid = (lo + hi) / 2
    eye = mid + Vector((size.y * 0.5, -size.y * 1.5, size.y * 0.7))
    co.location = eye
    co.rotation_euler = (mid - eye).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
