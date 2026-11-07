"""Builds one Brooklyn Bridge tower for FDR Drive and exports it for Unity.

Run: blender -b -P Tools/Blender/build_brooklyn_tower.py -- [render_dir]

The real towers: granite, 84 m above the water, the roadway about 38 m up and passing through two
pointed Gothic arches that spring 25 m above it; a battered base down into the river, belt
courses at the deck and at the springing line, a cornice and coping at the top, and the four main
cables crossing in saddles on top. Metres; X across the bridge, Y along it, Z up from the water.
Writes Assets/Resources/Biomes/NewYork/Models/BrooklynTower.fbx and, with a render_dir, check
renders. UVs are box-projected at 4 m per unit, as the game's VillageMesh does, so the game's
"Bridge Stone" material tiles the same way on it.
"""
import math
import os
import sys

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "NewYork", "Models", "BrooklynTower.fbx")

DECK = 38.0          # roadway, above the water
TOP = 84.0           # top of the coping
WIDTH = 42.0         # across, above the deck
DEPTH = 16.0         # along the bridge, above the deck
ARCH_SPAN = 10.5     # each roadway arch
ARCH_X = 9.25        # arch centres either side of the middle
SPRING = DECK + 25.0 # where the pointed arches spring
UV_METRES = 4.0
REVEAL = 1.3         # the recessed border round each arch
REVEAL_DEPTH = 0.9


def box(name, x0, x1, y0, y1, z0, z1):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 if v.co.x < 0 else x1
        v.co.y = y0 if v.co.y < 0 else y1
        v.co.z = z0 if v.co.z < 0 else z1
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def battered(name, bottom, top, z0, z1):
    """A block whose sides lean in: `bottom` and `top` are (width, depth)."""
    obj = box(name, -1, 1, -1, 1, z0, z1)
    for v in obj.data.vertices:
        w, d = top if v.co.z > (z0 + z1) / 2 else bottom
        v.co.x *= w / 2
        v.co.y *= d / 2
    return obj


def arch_cutter(name, cx, span=ARCH_SPAN, y0=-DEPTH, y1=DEPTH):
    """A pointed (equilateral) arch `span` wide, straight-sided from below the deck to the
    springing line, through the tower from y0 to y1."""
    half = span / 2
    pts = [(cx - half, DECK - 1.0), (cx + half, DECK - 1.0), (cx + half, SPRING)]
    # Right arc, centred on the left springing point; left arc, on the right one.
    for k in range(1, 13):
        a = math.radians(60 * k / 12)
        pts.append((cx - half + span * math.cos(a), SPRING + span * math.sin(a)))
    for k in range(1, 13):
        a = math.radians(120 + 60 * k / 12)
        pts.append((cx + half + span * math.cos(a), SPRING + span * math.sin(a)))
    bm = bmesh.new()
    front = [bm.verts.new((x, y0, z)) for x, z in pts[:-1]]
    face = bm.faces.new(front)
    bmesh.ops.recalc_face_normals(bm, faces=[face])
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.y = y1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def cut(target, cutter):
    mod = target.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def box_uv(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        axis = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            c = loop.vert.co
            u, v = ((c.y, c.z), (c.x, c.z), (c.x, c.y))[axis]
            loop[uv].uv = (u / UV_METRES, v / UV_METRES)
    bm.to_mesh(obj.data)
    bm.free()


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = [
        # The base, from the river bed up to the roadway, leaning in.
        battered("Base", (WIDTH + 6, DEPTH + 8), (WIDTH + 1, DEPTH + 1), -6.0, DECK),
        # Belt course at the roadway.
        box("DeckBelt", -(WIDTH / 2 + 1.0), WIDTH / 2 + 1.0, -(DEPTH / 2 + 1.0), DEPTH / 2 + 1.0, DECK - 1.2, DECK),
    ]
    body = box("Body", -WIDTH / 2, WIDTH / 2, -DEPTH / 2, DEPTH / 2, DECK, TOP - 4.0)
    for cx in (-ARCH_X, ARCH_X):
        cut(body, arch_cutter("Arch", cx))
        # Each arch framed by a recessed border on both faces, as the real ones are.
        for y0, y1 in ((-DEPTH, -DEPTH / 2 + REVEAL_DEPTH), (DEPTH / 2 - REVEAL_DEPTH, DEPTH)):
            cut(body, arch_cutter("Reveal", cx, ARCH_SPAN + 2 * REVEAL, y0, y1))
    parts.append(body)
    # Belt at the springing line, on the three piers only (cut by the arches like the body).
    belt = box("SpringBelt", -(WIDTH / 2 + 0.5), WIDTH / 2 + 0.5, -(DEPTH / 2 + 0.5), DEPTH / 2 + 0.5, SPRING - 0.8, SPRING)
    for cx in (-ARCH_X, ARCH_X):
        cut(belt, arch_cutter("Arch", cx))
    parts.append(belt)
    # Cornice and coping.
    parts.append(box("Cornice", -(WIDTH / 2 + 1.2), WIDTH / 2 + 1.2, -(DEPTH / 2 + 1.2), DEPTH / 2 + 1.2, TOP - 4.0, TOP - 2.6))
    parts.append(box("Coping", -(WIDTH / 2 - 0.3), WIDTH / 2 - 0.3, -(DEPTH / 2 - 0.3), DEPTH / 2 - 0.3, TOP - 2.6, TOP))
    # Saddle housings where the four main cables cross the top.
    for x in (-12.0, -4.5, 4.5, 12.0):
        parts.append(box("Saddle", x - 1.3, x + 1.3, -3.5, 3.5, TOP, TOP + 2.2))
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    tower = bpy.context.view_layer.objects.active
    tower.name = "BrooklynTower"
    box_uv(tower)
    mat = bpy.data.materials.new("BridgeStone")
    mat.diffuse_color = (0.72, 0.64, 0.52, 1.0)
    tower.data.materials.append(mat)
    return tower


def render(tower, folder, prefix="brooklyn_tower", look_at=45):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x, scene.render.resolution_y = 900, 900
    world = bpy.data.worlds.new("Sky")
    world.color = (0.55, 0.65, 0.8)
    scene.world = world
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, eye in (("front", (0, -130, 45)), ("three_quarter", (80, -100, 60))):
        cam.location = eye
        d = [0 - eye[0], 0 - eye[1], look_at - eye[2]]
        cam.rotation_euler = (math.atan2(math.hypot(d[0], d[1]), -d[2]), 0, math.atan2(d[1], d[0]) - math.pi / 2)
        scene.render.filepath = os.path.join(folder, f"{prefix}_{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    tower = build()
    print("BROOKLYN_TOWER triangles", sum(len(p.vertices) - 2 for p in tower.data.polygons),
          "size", tuple(round(v, 1) for v in tower.dimensions))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    tower.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE")
    print("wrote", OUT)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if args:
        render(tower, args[0])


if __name__ == "__main__":
    main()
