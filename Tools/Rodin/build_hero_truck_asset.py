"""Turns the Rodin hero truck download into the game's hero car.

    blender -b -P Tools/Rodin/build_hero_truck_asset.py [-- preview_prefix]

Takes Assets/Resources/TestAssets/hero_truck_h2 (git-ignored), keeps its 62,500 triangle
detail level, scales it to 5.5 m long on the ground with its origin under the middle, cuts
the four wheels out of the body so they can spin (Wheel_FL, FR, RL, RR), paints the baked
reflections off the glass and bed, and writes it to Assets/Resources/Vehicles, which is
committed and ships. Metallic and roughness are packed the way URP Lit reads them:
R metallic, A smoothness. The file's front is -Y.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Resources", "TestAssets", "hero_truck_h2")
OUT = os.path.join(ROOT, "Assets", "Resources", "Vehicles")
LENGTH = 5.5
PREVIEW = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else None
# Where each axle roughly is, along the truck, to start the circle fit from.
AXLES = {"F": -1.9, "R": 1.42}

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, "hero_truck_h2.fbx"))
for o in list(bpy.data.objects):
    if o.name != "model_LOD3":
        bpy.data.objects.remove(o)
ob = bpy.data.objects["model_LOD3"]
ob.name = "HeroTruck"
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
corners = [Vector(c) for c in ob.bound_box]
lo = Vector(min(c[i] for c in corners) for i in range(3))
hi = Vector(max(c[i] for c in corners) for i in range(3))
k = LENGTH / (hi.y - lo.y)
for v in ob.data.vertices:
    v.co = Vector(((v.co.x - (lo.x + hi.x) / 2) * k, (v.co.y - (lo.y + hi.y) / 2) * k, (v.co.z - lo.z) * k))
ob.data.update()
print("SIZE", [round((hi[i] - lo[i]) * k, 2) for i in range(3)], "TRIS", sum(len(p.vertices) - 2 for p in ob.data.polygons))

# ---- paint: glass and bed cover, on the colour texture
image = bpy.data.images.load(os.path.join(SRC, "texture_diffuse.png"))
w, h = image.size
source = np.array(image.pixels[:], np.float32).reshape(h, w, 4)
pixels = source.copy()
me = ob.data
uv = me.uv_layers.active.data
size = Vector((hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)) * k
GLASS, COVER = (0.03, 0.04, 0.05), (0.035, 0.035, 0.038)


def fill(tri, colour):
    (ax, ay), (bx, by), (cx, cy) = tri
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-6:
        return
    x0, x1 = max(0, int(min(ax, bx, cx)) - 1), min(w, int(max(ax, bx, cx)) + 2)
    y0, y1 = max(0, int(min(ay, by, cy)) - 1), min(h, int(max(ay, by, cy)) + 2)
    if x1 <= x0 or y1 <= y0:
        return
    xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
    a = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
    b = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
    pixels[y0:y1, x0:x1, :3][(a >= -0.08) & (b >= -0.08) & (a + b <= 1.08)] = colour


glass = cover = 0
for p in me.polygons:
    c = sum((me.vertices[i].co for i in p.vertices), Vector()) / len(p.vertices)
    n = p.normal
    height = c.z / size.z
    across = abs(c.x) / (size.x / 2)
    along = (c.y + LENGTH / 2) / LENGTH
    tri = [(uv[l].uv[0] * w, uv[l].uv[1] * h) for l in p.loop_indices]
    if n.y < -0.4 and 0.3 < n.z < 0.92 and height > 0.62 and across < 0.72 and along < 0.62:
        fill(tri, GLASS)
        glass += 1
    elif along > 0.38 and height > 0.35 and across < 0.82 and (n.z > 0.2 or n.y > 0.2):
        # Bed cover and rear glass: whatever is bright and colourless there. The red
        # paint and the roof stay as they are.
        mx, my = sum(t[0] for t in tri) / 3, sum(t[1] for t in tri) / 3
        r, g, b = source[min(h - 1, max(0, int(my))), min(w - 1, max(0, int(mx))), :3]
        if min(r, g, b) > 0.28 and max(r, g, b) - min(r, g, b) < 0.18:
            fill(tri, COVER)
            cover += 1
print("PAINT glass", glass, "cover", cover)
painted = bpy.data.images.new("hero_d", w, h)
painted.pixels = pixels.ravel().tolist()
painted.filepath_raw = os.path.join(OUT, "T_hero_truck_D.png")
painted.file_format = "PNG"
painted.save()

# ---- wheels: fit a circle to the underside at each axle, then cut along it
verts = np.array([v.co[:] for v in me.vertices])
bm = bmesh.new()
bm.from_mesh(me)
bm.faces.ensure_lookup_table()
centres = np.array([[f.calc_center_median()[i] for i in range(3)] for f in bm.faces])
wheels = {}
for axle, guess in AXLES.items():
    for side, sign in (("L", -1), ("R", 1)):
        pts = verts[(verts[:, 0] * sign > 0.5) & (np.abs(verts[:, 1] - guess) < 0.5) & (verts[:, 2] < 0.3)]
        bins = {}
        for x, y, z in pts:
            key = round(y / 0.03)
            if key not in bins or z < bins[key][1]:
                bins[key] = (y, z)
        e = np.array(list(bins.values()))
        # y^2 + z^2 + a y + b z + c = 0
        a, b, c = np.linalg.lstsq(np.c_[e, np.ones(len(e))], -(e ** 2).sum(1), rcond=None)[0]
        cy, cz = -a / 2, -b / 2
        radius = math.sqrt(max(0.0, cy * cy + cz * cz - c))
        # The tyre reaches the ground: trust the lowest point for its size.
        low = verts[(verts[:, 0] * sign > 0.5) & (np.abs(verts[:, 1] - cy) < 0.15)][:, 2].min()
        radius = min(radius, cz - low + 0.01) if radius > 0.3 else cz - low
        print("WHEEL", axle + side, "y", round(cy, 3), "z", round(cz, 3), "r", round(radius, 3))
        wheels[axle + side] = (cy, cz, radius, sign)

for name, (cy, cz, radius, sign) in wheels.items():
    d = np.hypot(centres[:, 1] - cy, centres[:, 2] - cz)
    picked = (centres[:, 0] * sign > 0.5) & (d < radius * 0.97)
    faces = [bm.faces[i] for i in np.nonzero(picked)[0]]
    copy = bm.copy()
    copy.faces.ensure_lookup_table()
    keep = set(np.nonzero(picked)[0].tolist())
    bmesh.ops.delete(copy, geom=[f for i, f in enumerate(copy.faces) if i not in keep], context="FACES")
    wheel_mesh = bpy.data.meshes.new("Wheel_" + name)
    copy.to_mesh(wheel_mesh)
    copy.free()
    wheel_mesh.materials.append(me.materials[0])
    wheel = bpy.data.objects.new("Wheel_" + name, wheel_mesh)
    bpy.context.collection.objects.link(wheel)
    # The wheel's origin on its axle, so nothing else needs to measure it.
    wheel_mesh.transform(__import__("mathutils").Matrix.Translation((0, -cy, -cz)))
    wheel.location = (0, cy, cz)
    print("CUT", name, len(faces))
    wheels[name] = wheels[name] + (wheel,)
removed = set()
for name, (cy, cz, radius, sign, _) in wheels.items():
    d = np.hypot(centres[:, 1] - cy, centres[:, 2] - cz)
    removed |= set(np.nonzero((centres[:, 0] * sign > 0.5) & (d < radius * 0.97))[0].tolist())
bmesh.ops.delete(bm, geom=[bm.faces[i] for i in removed], context="FACES")
bm.to_mesh(me)
bm.free()

if PREVIEW:
    # With the wheels turned, so a bad cut shows.
    m = bpy.data.materials.new("v")
    m.use_nodes = True
    nodes = m.node_tree.nodes
    bsdf = next(x for x in nodes if x.type == "BSDF_PRINCIPLED")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = painted
    m.node_tree.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    originals = [o.data.materials[0] for o in meshes]
    for o in meshes:
        o.data.materials[0] = m
        if o.name.startswith("Wheel_"):
            o.rotation_euler = (math.radians(47), 0, 0)
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError as e:
        scene.render.engine = str(e).split("'")[1]
    scene.render.resolution_x, scene.render.resolution_y = 800, 500
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    next(x for x in world.node_tree.nodes if x.type == "BACKGROUND").inputs[0].default_value = (0.62, 0.68, 0.76, 1)
    scene.world = world
    cam = bpy.data.cameras.new("c")
    cam.lens = 55
    co = bpy.data.objects.new("c", cam)
    bpy.context.collection.objects.link(co)
    scene.camera = co
    for label, eye in (("front", (5, -8, 2)), ("side", (10, 0, 1)), ("rear", (-2.5, 8, 5))):
        co.location = eye
        co.rotation_euler = (Vector((0, 0, 0.9)) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{PREVIEW}_{label}.png"
        bpy.ops.render.render(write_still=True)
    for o, material in zip(meshes, originals):
        o.data.materials[0] = material
        o.rotation_euler = (0, 0, 0)
    bpy.data.objects.remove(co)

bpy.ops.object.select_all(action="SELECT")
# The file's front is Blender's -Y; these axes put it on Unity's +Z, upright.
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "SM_hero_truck.fbx"), use_selection=True,
                         axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
                         mesh_smooth_type="FACE", bake_space_transform=True, path_mode="STRIP", embed_textures=False)

load = lambda name: bpy.data.images.load(os.path.join(SRC, name))
normal, metal, rough = load("texture_normal.png"), load("texture_metallic.png"), load("texture_roughness.png")
normal.save_render(os.path.join(OUT, "T_hero_truck_N.png"))
w, h = metal.size
assert tuple(rough.size) == (w, h)
m, r = np.array(metal.pixels[:]), np.array(rough.pixels[:])
out = np.zeros(w * h * 4)
out[0::4] = out[1::4] = out[2::4] = m[0::4]
out[3::4] = 1.0 - r[0::4]
ms = bpy.data.images.new("ms", w, h, alpha=True, float_buffer=False)
ms.colorspace_settings.name = "Non-Color"
ms.pixels = out.tolist()
ms.filepath_raw = os.path.join(OUT, "T_hero_truck_MS.png")
ms.file_format = "PNG"
ms.save()
print("DONE", w, h)
