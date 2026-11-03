"""Builds the Road Rage hero car, a muscular armoured pickup, from lofted body sections.
Runs headless, with no addon:
    blender -b -P Tools/Blender/build_hero_truck.py -- <out.fbx> [preview_prefix]

The car faces +Y, 5.3 m long, wheels on Z = 0, in metres. Materials: Paint, Trim, Glass,
Lamp, LampRed, Rubber, Rim. Each wheel is its own object named Wheel_*.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "hero.fbx"
PREVIEW = argv[1] if len(argv) > 1 else None

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)


def material(name, colour, metal=0.0, rough=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    n = next(x for x in m.node_tree.nodes if x.type == "BSDF_PRINCIPLED")
    n.inputs["Base Color"].default_value = (*colour, 1)
    n.inputs["Metallic"].default_value = metal
    n.inputs["Roughness"].default_value = rough
    return m


PAINT = material("Paint", (0.48, 0.055, 0.03), 0.55, 0.28)
TRIM = material("Trim", (0.018, 0.018, 0.02), 0.2, 0.55)
GLASS = material("Glass", (0.012, 0.022, 0.03), 0.0, 0.06)
LAMP = material("Lamp", (0.95, 0.95, 0.88), 0.0, 0.1)
RED = material("LampRed", (0.6, 0.015, 0.012), 0.0, 0.15)
RUBBER = material("Rubber", (0.012, 0.012, 0.013), 0.0, 0.85)
RIM = material("Rim", (0.42, 0.43, 0.45), 0.9, 0.3)
STEEL = material("Steel", (0.2, 0.21, 0.22), 0.85, 0.42)


def mesh(name, verts, faces, mat, smooth=True, sub=0, parent=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    if sub:
        m = ob.modifiers.new("sub", "SUBSURF")
        m.levels = m.render_levels = sub
    return ob


def box(name, c, s, mat, bevel=0.0):
    x, y, z = c
    a, b, d = s[0] / 2, s[1] / 2, s[2] / 2
    v = [(x + i * a, y + j * b, z + k * d) for i in (-1, 1) for j in (-1, 1) for k in (-1, 1)]
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    ob = mesh(name, v, f, mat, smooth=False)
    if bevel:
        m = ob.modifiers.new("bev", "BEVEL")
        m.width, m.segments = bevel, 2
    return ob


def loft(name, stations, mat, sub=2):
    """stations: (y, half_width, bottom, belt, top, top_half_width). A closed tube of
    eight-point sections, which subdivision rounds into a pressed-steel body."""
    rings = []
    for y, w, bottom, belt, top, tw in stations:
        rings.append([(-w * 0.86, y, bottom), (-w, y, bottom + (belt - bottom) * 0.3), (-w, y, belt),
                      (-tw, y, top), (tw, y, top), (w, y, belt),
                      (w, y, bottom + (belt - bottom) * 0.3), (w * 0.86, y, bottom)])
    verts = [p for r in rings for p in r]
    n = 8
    faces = []
    for i in range(len(rings) - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            faces.append((a, a + n, b + n, b))
    faces.append(tuple(range(n - 1, -1, -1)))
    last = (len(rings) - 1) * n
    faces.append(tuple(last + j for j in range(n)))
    return mesh(name, verts, faces, mat, sub=sub)


# Lower body, nose to tail: high, square shoulders and a long bonnet.
body = loft("Body", [
    (2.66, 0.84, 0.50, 0.92, 1.02, 0.72),
    (2.52, 0.96, 0.44, 1.02, 1.17, 0.84),
    (1.95, 1.00, 0.42, 1.08, 1.27, 0.88),
    (1.05, 1.01, 0.42, 1.10, 1.30, 0.90),
    (0.40, 1.01, 0.42, 1.12, 1.30, 0.92),
    (-0.90, 1.01, 0.42, 1.12, 1.26, 0.94),
    (-2.25, 1.01, 0.44, 1.13, 1.27, 0.96),
    (-2.62, 0.97, 0.50, 1.10, 1.25, 0.93),
], PAINT)

# Cab: windscreen raked back, roof, upright rear window.
cab = loft("Cab", [
    (1.02, 0.86, 1.20, 1.28, 1.30, 0.84),
    (0.55, 0.84, 1.22, 1.50, 1.82, 0.70),
    (0.10, 0.82, 1.24, 1.58, 1.92, 0.70),
    (-0.75, 0.82, 1.24, 1.58, 1.91, 0.70),
    (-0.98, 0.84, 1.22, 1.45, 1.83, 0.72),
], PAINT, sub=1)

# Glass: stands just proud of the cab's faces.
mesh("Windscreen", [(-0.70, 0.86, 1.36), (0.70, 0.86, 1.36), (0.60, 0.46, 1.79), (-0.60, 0.46, 1.79)], [(0, 1, 2, 3)], GLASS, smooth=False)
mesh("RearWindow", [(0.62, -0.97, 1.43), (-0.62, -0.97, 1.43), (-0.56, -0.99, 1.78), (0.56, -0.99, 1.78)], [(0, 1, 2, 3)], GLASS, smooth=False)
for s in (-1, 1):
    x0, x1 = s * 0.845, s * 0.74
    v = [(x0, 0.74, 1.38), (x0, -0.02, 1.38), (x1, -0.02, 1.80), (x1, 0.46, 1.80)]
    mesh(f"SideFront{s}", v, [(0, 1, 2, 3) if s > 0 else (3, 2, 1, 0)], GLASS, smooth=False)
    v = [(x0, -0.10, 1.38), (x0, -0.84, 1.38), (x1, -0.84, 1.78), (x1, -0.10, 1.80)]
    mesh(f"SideRear{s}", v, [(0, 1, 2, 3) if s > 0 else (3, 2, 1, 0)], GLASS, smooth=False)
    # Door line and handle.
    box(f"DoorCut{s}", (s * 1.018, 0.02, 0.80), (0.012, 0.016, 0.62), TRIM)
    box(f"DoorCutB{s}", (s * 1.018, -0.92, 0.80), (0.012, 0.016, 0.62), TRIM)
    box(f"Handle{s}", (s * 1.03, -0.06, 1.02), (0.03, 0.2, 0.035), STEEL)
    box(f"Mirror{s}", (s * 1.03, 0.62, 1.42), (0.22, 0.09, 0.15), TRIM, 0.02)
    # Side step and rocker.
    box(f"Step{s}", (s * 1.03, -0.15, 0.40), (0.16, 2.2, 0.06), STEEL, 0.015)

# Pickup bed: dark liner let into the top, with a roll bar.
box("BedLiner", (0, -1.72, 1.285), (1.72, 1.34, 0.03), TRIM)
for x in (-0.82, 0.82):
    box("RollPost", (x, -1.12, 1.56), (0.08, 0.08, 0.6), TRIM)
box("RollBar", (0, -1.12, 1.84), (1.72, 0.08, 0.08), TRIM)

# Grille, bull bar, bumpers and lamps.
box("Grille", (0, 2.665, 0.93), (1.28, 0.03, 0.32), TRIM, 0.01)
for i in range(6):
    box("GrilleBar", (0, 2.685, 0.81 + i * 0.048), (1.24, 0.012, 0.016), STEEL)
box("FrontBumper", (0, 2.66, 0.56), (1.96, 0.26, 0.24), TRIM, 0.03)
box("RearBumper", (0, -2.68, 0.58), (1.92, 0.2, 0.2), TRIM, 0.03)
for x in (-0.46, 0.46):
    box("BullUpright", (x, 2.84, 0.86), (0.075, 0.075, 0.72), STEEL)
box("BullTop", (0, 2.84, 1.22), (1.06, 0.075, 0.075), STEEL)
box("BullMid", (0, 2.84, 0.88), (1.06, 0.06, 0.06), STEEL)
box("BullLow", (0, 2.82, 0.58), (1.56, 0.1, 0.12), STEEL, 0.02)
for s in (-1, 1):
    box(f"Head{s}", (s * 0.77, 2.655, 0.94), (0.28, 0.05, 0.2), LAMP, 0.015)
    box(f"Tail{s}", (s * 0.87, -2.635, 1.0), (0.2, 0.05, 0.3), RED, 0.012)
    box(f"Fog{s}", (s * 0.72, 2.80, 0.56), (0.18, 0.03, 0.1), LAMP)
for i in range(4):
    box("RoofLamp", (-0.45 + i * 0.3, 0.32, 1.97), (0.22, 0.09, 0.09), LAMP, 0.012)
box("LampBar", (0, 0.27, 1.93), (1.3, 0.05, 0.04), TRIM)


def wheel(name, x, y):
    """A chunky tyre and a five-spoke rim, as one object spinning about X."""
    radius, half, inner = 0.47, 0.17, 0.29
    seg = 40
    tyre_profile = [(inner, -half), (radius - 0.03, -half), (radius, -half * 0.6), (radius, half * 0.6),
                    (radius - 0.03, half), (inner, half)]
    verts, faces = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        # Knobbly tread: every other segment stands proud.
        k = 1.0 if i % 2 else 0.975
        for r, t in tyre_profile:
            rr = inner + (r - inner) * (k if r >= radius - 0.001 else 1)
            verts.append((t, rr * math.sin(a), rr * math.cos(a)))
    m = len(tyre_profile)
    for i in range(seg):
        for j in range(m - 1):
            a, b = i * m + j, ((i + 1) % seg) * m + j
            faces.append((a, b, b + 1, a + 1))
    ob = mesh(name, verts, faces, RUBBER)
    # Rim disc and spokes joined in.
    ob.data.materials.append(RIM)
    rim_v, rim_f = [], []
    side = 1 if x > 0 else -1
    z = side * (half - 0.03)
    rim_v.append((z, 0, 0))
    for i in range(seg):
        a = 2 * math.pi * i / seg
        r = inner * (1.0 if math.cos(5 * a) > 0.2 else 0.5)
        rim_v.append((z, r * math.sin(a), r * math.cos(a)))
    for i in range(seg):
        rim_f.append((0, 1 + i, 1 + (i + 1) % seg))
    base = len(ob.data.vertices)
    disc = mesh("disc", rim_v, rim_f, RIM, smooth=False)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    disc.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()
    ob.location = (x, y, radius)
    return ob


# Dark wheel wells, set into the body sides, and flared arches over them.
for y in (1.62, -1.58):
    for s in (-1, 1):
        wheel(f"Wheel_{'F' if y > 0 else 'R'}{'R' if s > 0 else 'L'}", s * 0.92, y)
        verts, faces = [], []
        steps = 16
        for i in range(steps + 1):
            a = math.pi * i / steps
            for r, ox in ((0.56, s * 1.0), (0.56, s * 1.09), (0.66, s * 1.09), (0.66, s * 1.0)):
                verts.append((ox, y + r * math.cos(a), 0.47 + r * math.sin(a)))
        for i in range(steps):
            for j in range(3):
                a, b = i * 4 + j, (i + 1) * 4 + j
                faces.append((a, a + 1, b + 1, b) if s > 0 else (a, b, b + 1, a + 1))
        mesh(f"Arch{s}{y}", verts, faces, TRIM)
        # The well: a dark disc on the body side behind the wheel.
        well = [(s * 1.03, y, 0.47)] + [(s * 1.03, y + 0.56 * math.cos(math.pi * i / 16), 0.47 + 0.56 * math.sin(math.pi * i / 16)) for i in range(17)]
        well += [(s * 1.03, y - 0.56, 0.2), (s * 1.03, y + 0.56, 0.2)]
        wf = [(0, i, i + 1) for i in range(1, 17)] + [(0, 17, 18), (0, 18, 19), (0, 19, 1)]
        mesh(f"Well{s}{y}", well, [f if s > 0 else f[::-1] for f in wf], RUBBER, smooth=False)

# Apply modifiers, so what is exported is what was seen.
bpy.ops.object.select_all(action="DESELECT")
dg = bpy.context.evaluated_depsgraph_get()
tris = 0
for ob in bpy.data.objects:
    if ob.type != "MESH":
        continue
    bpy.context.view_layer.objects.active = ob
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    tris += sum(len(p.vertices) - 2 for p in ob.data.polygons)
print("TRIANGLES", tris)

# Everything but the wheels into one body, so the game has few objects to draw.
parts = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Wheel_")]
bpy.ops.object.select_all(action="DESELECT")
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = next(o for o in parts if o.name == "Body")
bpy.ops.object.join()
bpy.context.object.name = "HeroBody"

if PREVIEW:
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError as e:
        scene.render.engine = str(e).split("'")[1]
    scene.render.resolution_x, scene.render.resolution_y = 1000, 620
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.62, 0.68, 0.76, 1)
    bg.inputs[1].default_value = 1.0
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=60)
    floor = bpy.context.object
    floor.data.materials.append(material("Floor", (0.25, 0.25, 0.26), 0, 0.8))
    sun = bpy.data.lights.new("s", "SUN")
    sun.energy = 3.5
    so = bpy.data.objects.new("s", sun)
    so.rotation_euler = (math.radians(50), 0, math.radians(-40))
    bpy.context.collection.objects.link(so)
    cam = bpy.data.cameras.new("c")
    cam.lens = 55
    co = bpy.data.objects.new("c", cam)
    bpy.context.collection.objects.link(co)
    scene.camera = co
    for label, eye in (("front", (6.5, 8.5, 2.6)), ("rear", (-6.5, -8.5, 3.2)), ("side", (12, 0.0, 1.5))):
        co.location = eye
        direction = Vector((0, 0, 0.9)) - Vector(eye)
        co.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{PREVIEW}_{label}.png"
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(floor)

bpy.ops.object.select_all(action="SELECT")
for o in list(bpy.data.objects):
    if o.type != "MESH":
        o.select_set(False)
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="Y", axis_up="Z", bake_space_transform=False, mesh_smooth_type="FACE")
print("WROTE", OUT)
