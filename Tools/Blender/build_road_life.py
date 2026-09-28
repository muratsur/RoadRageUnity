"""Builds the B500's road life in headless Blender, flat-shaded low poly like the
Synty cars, on one small palette atlas:

  SM_rider_moto       a motorcyclist in leathers, seated and leaning into the bars;
                      origin at the seat, so it drops onto any of the bikes
  SM_cyclist          a road bike with its rider
  SM_tractor          a green farm tractor (big rear wheels, glass cab)
  SM_logging_truck    a timber lorry loaded with spruce logs
  SM_deer             a red deer stag mid-leap

    .venv/bin/python Tools/Blender/build_road_life.py

Output (./out_road_life next to this script, or $RR_OUT): the meshes as FBX,
T_roadlife_D.png and preview.png. The game loads them from
Assets/Resources/Biomes/BlackForest (Meshes/, Textures/).
Axes: +Z up, the front facing -Y (+Z in Unity), base at the origin.
"""
import bpy  # noqa: must load before mathutils
import math
import os

import numpy as np
from mathutils import Euler, Vector
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_road_life"))
os.makedirs(OUT, exist_ok=True)

# 4 x 4 palette, 32 px cells: each part is mapped to the centre of one colour.
PALETTE = [
    ("tyre", (0.03, 0.03, 0.03)), ("dark", (0.13, 0.13, 0.14)), ("grey", (0.42, 0.43, 0.45)),
    ("silver", (0.72, 0.73, 0.75)), ("white", (0.90, 0.90, 0.88)), ("green", (0.16, 0.40, 0.14)),
    ("red", (0.60, 0.07, 0.06)), ("glass", (0.07, 0.10, 0.13)), ("leather", (0.05, 0.045, 0.045)),
    ("jersey", (0.08, 0.28, 0.68)), ("yellow", (0.88, 0.66, 0.08)), ("skin", (0.78, 0.58, 0.46)),
    ("deer", (0.42, 0.27, 0.15)), ("belly", (0.76, 0.66, 0.52)), ("bark", (0.30, 0.20, 0.13)),
    ("wood", (0.80, 0.64, 0.42)),
]
CELL, GRID = 32, 4


def build_atlas():
    img = np.zeros((CELL * GRID, CELL * GRID, 3))
    rng = np.random.default_rng(3)
    for i, (_, col) in enumerate(PALETTE):
        r, c = divmod(i, GRID)
        cell = np.ones((CELL, CELL, 3)) * col
        cell *= 0.94 + 0.06 * rng.random((CELL, CELL, 1))   # a little grain, no banding
        img[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = cell
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "T_roadlife_D.png"))


def cell_uv(name):
    i = [p[0] for p in PALETTE].index(name)
    r, c = divmod(i, GRID)
    return ((c + 0.5) / GRID, 1 - (r + 0.5) / GRID)


PARTS = []


def part(obj, colour):
    """Maps every face of a primitive to one palette colour and keeps it for joining."""
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new()
    u, v = cell_uv(colour)
    for loop in me.uv_layers.active.data:
        loop.uv = (u, v)
    PARTS.append(obj)
    return obj


def box(colour, centre, size, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=centre, rotation=rot)
    o = bpy.context.active_object
    o.scale = size
    return part(o, colour)


def cyl(colour, centre, radius, depth, rot=(0, 0, 0), sides=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=depth, location=centre, rotation=rot)
    return part(bpy.context.active_object, colour)


def ball(colour, centre, size, rot=(0, 0, 0), segments=10, rings=6):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=centre, rotation=rot)
    o = bpy.context.active_object
    o.scale = size
    return part(o, colour)


def limb(colour, a, b, radius, sides=8):
    """A cylinder from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = d.to_track_quat("Z", "Y").to_euler()
    return cyl(colour, (a + b) / 2, radius, d.length, rot=rot, sides=sides)


def wheel(centre, radius, width, rim="silver", rim_scale=0.55, sides=16):
    cyl("tyre", centre, radius, width, rot=(0, math.pi / 2, 0), sides=sides)
    cyl(rim, centre, radius * rim_scale, width * 1.04, rot=(0, math.pi / 2, 0), sides=sides)


def finish(name):
    """Joins the parts into one flat-shaded mesh."""
    global PARTS
    bpy.ops.object.select_all(action="DESELECT")
    for o in PARTS:
        o.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    ob = bpy.context.active_object
    ob.name = name
    ob.data.name = name
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    for p in ob.data.polygons:
        p.use_smooth = False
    PARTS = []
    return ob


# ---------------------------------------------------------------- models

def rider_moto():
    # Origin at the seat. Leaning into the bars, knees tucked, full-face helmet.
    lean = math.radians(48)
    hip = Vector((0, 0.05, 0.05))
    shoulder = hip + Vector((0, -math.sin(lean) * 0.62, math.cos(lean) * 0.62))
    box("leather", (hip + shoulder) / 2, (0.40, 0.24, 0.66), rot=(-lean, 0, 0))            # torso
    box("dark", (hip + shoulder) / 2 + Vector((0, 0.16, 0.05)), (0.30, 0.10, 0.40), rot=(-lean, 0, 0))  # back hump
    ball("red", shoulder + Vector((0, -0.12, 0.22)), (0.15, 0.17, 0.15))                    # helmet
    box("glass", shoulder + Vector((0, -0.27, 0.22)), (0.20, 0.03, 0.08))                   # visor
    for x in (-0.19, 0.19):
        # Hands on the bars: on the game's bikes (2.15 m) they sit 0.9 m ahead of the
        # seat and a little below it.
        hand = Vector((x * 1.3, -0.86, -0.02))
        elbow = Vector((x * 1.3, -0.58, 0.14))
        limb("leather", shoulder + Vector((x, 0, -0.02)), elbow, 0.055)
        limb("leather", elbow, hand, 0.05)
        knee = Vector((x * 1.05, -0.42, -0.02))
        foot = Vector((x * 1.0, -0.08, -0.40))
        limb("leather", hip + Vector((x * 0.6, 0, 0)), knee, 0.08)
        limb("leather", knee, foot, 0.065)
        box("dark", foot + Vector((0, -0.08, -0.03)), (0.10, 0.24, 0.08))                  # boot
    return finish("SM_rider_moto")


def cyclist():
    # Road bike, 1.75 m long, 0.68 m wheels; rider in a blue jersey, white helmet.
    r = 0.34
    for y in (-0.52, 0.52):
        cyl("tyre", (0, y, r), r, 0.035, rot=(0, math.pi / 2, 0), sides=18)
        cyl("silver", (0, y, r), 0.05, 0.08, rot=(0, math.pi / 2, 0), sides=8)
    bb = Vector((0, 0.02, 0.30))
    seat = Vector((0, 0.18, 0.86))
    head = Vector((0, -0.38, 0.84))
    for a, b in ((bb, seat), (bb, head), (seat, head), (bb, (0, 0.52, r)), (seat, (0, 0.52, r)), (head, (0, -0.52, r))):
        limb("red", a, b, 0.022, sides=6)
    box("dark", seat + Vector((0, 0, 0.03)), (0.07, 0.22, 0.04))                            # saddle
    box("dark", head + Vector((0, -0.05, 0.08)), (0.42, 0.04, 0.04))                         # bars
    # Rider: bent over the bars.
    hip = seat + Vector((0, 0.02, 0.10))
    shoulder = Vector((0, -0.22, 1.36))
    box("jersey", (hip + shoulder) / 2, (0.36, 0.22, (shoulder - hip).length),
        rot=((shoulder - hip).to_track_quat("Z", "Y").to_euler()))
    ball("skin", shoulder + Vector((0, -0.16, 0.14)), (0.10, 0.11, 0.11))
    ball("white", shoulder + Vector((0, -0.14, 0.21)), (0.13, 0.16, 0.08))                  # helmet
    for x in (-0.16, 0.16):
        limb("jersey", shoulder + Vector((x, 0, -0.03)), head + Vector((x * 1.2, -0.05, 0.10)), 0.04)
        knee = Vector((x * 0.75, -0.12, 0.62))
        foot = Vector((x * 0.7, 0.02, 0.26))
        limb("dark", hip + Vector((x * 0.6, 0, 0)), knee, 0.065)
        limb("skin", knee, foot, 0.045)
        box("white", foot + Vector((0, -0.04, -0.02)), (0.08, 0.20, 0.06))
    return finish("SM_cyclist")


def tractor():
    # Rear is +Y. 4.3 m long, 2.3 m wide, 2.95 m to the cab roof.
    for x in (-0.86, 0.86):
        wheel((x, 1.05, 0.82), 0.82, 0.52, rim="yellow", rim_scale=0.58, sides=20)
        wheel((x, -1.35, 0.52), 0.52, 0.36, rim="yellow", rim_scale=0.55, sides=16)
        box("green", (x, 1.05, 1.72), (0.62, 1.30, 0.08))                                   # rear fender
    box("dark", (0, -0.2, 0.62), (0.70, 3.2, 0.36))                                         # chassis
    box("green", (0, -1.05, 1.18), (0.92, 1.95, 0.78))                                      # bonnet
    box("dark", (0, -2.02, 1.12), (0.80, 0.06, 0.55))                                       # grille
    box("white", (0, -1.05, 1.58), (0.94, 1.9, 0.03))                                       # bonnet stripe
    box("green", (0, 0.72, 1.35), (1.25, 1.35, 0.50))                                       # cab base
    box("glass", (0, 0.72, 2.13), (1.24, 1.30, 1.06))                                       # cab glass
    for x in (-0.62, 0.62):
        for y in (0.07, 1.37):
            box("green", (x, y, 2.13), (0.06, 0.06, 1.08))                                  # posts
    box("green", (0, 0.72, 2.72), (1.40, 1.46, 0.12))                                       # roof
    box("white", (0, 0.72, 2.80), (1.20, 1.20, 0.04))
    cyl("dark", (0.36, -0.55, 2.2), 0.05, 1.1)                                              # exhaust
    box("yellow", (0.52, 0.08, 2.84), (0.14, 0.14, 0.08))                                   # beacon
    box("grey", (0, 1.82, 0.72), (0.9, 0.12, 0.28))                                         # hitch
    return finish("SM_tractor")


def logging_truck():
    # 10.6 m, cab at the front (-Y), four bunks of spruce logs behind it.
    box("dark", (0, 0.3, 0.72), (0.95, 9.4, 0.30))                                          # chassis
    box("red", (0, -4.2, 1.95), (2.40, 1.80, 1.90))                                         # cab
    box("red", (0, -4.9, 1.05), (2.40, 0.60, 0.70))                                         # nose
    box("glass", (0, -5.12, 2.30), (2.10, 0.04, 0.85))                                      # windscreen
    box("glass", (1.21, -4.4, 2.35), (0.03, 1.0, 0.70))
    box("glass", (-1.21, -4.4, 2.35), (0.03, 1.0, 0.70))
    box("silver", (0, -5.24, 1.10), (1.90, 0.08, 0.50))                                     # grille / bumper
    box("white", (0, -4.2, 2.98), (2.30, 1.60, 0.16))                                       # roof visor
    for x in (-1.0, 1.0):
        wheel((x, -4.25, 0.52), 0.52, 0.34)
        for y in (1.7, 3.0, 4.2):
            wheel((x, y, 0.52), 0.52, 0.34)
    box("grey", (0, -2.9, 1.9), (0.5, 0.5, 1.6))                                            # crane post
    limb("grey", (0, -2.9, 2.7), (0, -1.2, 3.4), 0.12)                                      # crane arm
    logs_y0, logs_y1 = -2.3, 5.2
    for y in np.linspace(logs_y0 + 0.4, logs_y1 - 0.4, 4):
        box("grey", (0, y, 0.98), (2.35, 0.18, 0.18))                                       # bunk
        for x in (-1.12, 1.12):
            box("grey", (x, y, 1.75), (0.10, 0.10, 1.55))                                   # stanchion
    rows = [(-0.75, 0), (-0.25, 0), (0.25, 0), (0.75, 0), (-0.5, 1), (0.0, 1), (0.5, 1), (-0.25, 2), (0.25, 2)]
    for i, (x, row) in enumerate(rows):
        r = 0.24 + 0.03 * ((i * 7) % 3)
        z = 1.33 + row * 0.44
        length = logs_y1 - logs_y0 - 0.2 * (i % 3)
        yc = (logs_y0 + logs_y1) / 2 + 0.1 * (i % 2)
        cyl("bark", (x, yc, z), r, length, rot=(math.pi / 2, 0, 0), sides=9)
        cyl("wood", (x, yc + length / 2 + 0.005, z), r * 0.92, 0.02, rot=(math.pi / 2, 0, 0), sides=9)
        cyl("wood", (x, yc - length / 2 - 0.005, z), r * 0.92, 0.02, rot=(math.pi / 2, 0, 0), sides=9)
    return finish("SM_logging_truck")


def deer():
    # Red deer stag mid-leap, 1.9 m nose to tail, 1.9 m to the antler tips.
    ball("deer", (0, 0.05, 1.08), (0.24, 0.62, 0.30))                                       # body
    ball("belly", (0, 0.05, 0.95), (0.20, 0.52, 0.16))
    limb("deer", (0, -0.45, 1.20), (0, -0.78, 1.62), 0.11)                                  # neck
    ball("deer", (0, -0.92, 1.66), (0.10, 0.22, 0.11), rot=(math.radians(20), 0, 0))       # head
    ball("dark", (0, -1.12, 1.62), (0.05, 0.05, 0.05))                                      # nose
    for x in (-1, 1):
        base = Vector((x * 0.06, -0.82, 1.76))
        tip = base + Vector((x * 0.28, 0.12, 0.42))
        limb("wood", base, tip, 0.022, sides=5)
        for t in (0.35, 0.65):
            p = base.lerp(tip, t)
            limb("wood", p, p + Vector((x * 0.04, -0.14, 0.12)), 0.016, sides=5)
        box("deer", (x * 0.1, -0.84, 1.80), (0.05, 0.03, 0.10), rot=(0, x * 0.5, 0))          # ears
        # Legs: front reaching forward, hind pushing back - a leap.
        limb("deer", (x * 0.13, -0.40, 0.95), (x * 0.13, -0.78, 0.52), 0.045)
        limb("deer", (x * 0.13, -0.78, 0.52), (x * 0.13, -0.95, 0.22), 0.035)
        limb("deer", (x * 0.13, 0.48, 0.98), (x * 0.13, 0.82, 0.58), 0.055)
        limb("deer", (x * 0.13, 0.82, 0.58), (x * 0.13, 1.12, 0.30), 0.035)
    ball("white", (0, 0.66, 1.16), (0.10, 0.06, 0.12))                                      # rump patch
    return finish("SM_deer")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    build_atlas()
    objs = [rider_moto(), cyclist(), tractor(), logging_truck(), deer()]

    mat = bpy.data.materials.new("M_roadlife")
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(OUT, "T_roadlife_D.png"))
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(mat)
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, o.name + ".fbx"), use_selection=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=True,
                                 path_mode="STRIP", add_leaf_bones=False)
        dims = o.dimensions
        print(f"RR_MESH {o.name} tris={sum(len(p.vertices) - 2 for p in o.data.polygons)} "
              f"size={dims.x:.2f}x{dims.y:.2f}x{dims.z:.2f}")
    preview(objs)
    print("RR_DONE")


def preview(objs):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = int(os.environ.get("RR_SAMPLES", "24"))
    scene.cycles.use_denoising = True
    x = -9.0
    for o in objs:
        o.location = (x, 0, 0.8 if o.name == "SM_rider_moto" else 0)
        x += {"SM_rider_moto": 2.5, "SM_cyclist": 3.5, "SM_tractor": 5.0, "SM_logging_truck": 4.5}.get(o.name, 3)
        o.rotation_euler = Euler((0, 0, math.radians(-60)))
    bpy.ops.mesh.primitive_plane_add(size=200)
    g = bpy.context.active_object
    gm = bpy.data.materials.new("g")
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.2, 0.2, 0.21, 1)
    g.data.materials.append(gm)
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.66, 0.72, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 30
    cam.location = (0.0, -17.0, 4.5)
    cam.rotation_euler = (Vector((0.0, 0, 1.2)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1600, 700
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
