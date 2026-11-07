"""Builds one Manhattan Bridge tower for FDR Drive and exports it for Unity.

Run: blender -b -P Tools/Blender/build_manhattan_tower.py -- [render_dir]

The real towers: steel, about 100 m above the water on granite piers in the river, two built-up
legs either side of the double deck (roadway about 38 m up), a broad arched portal joining the legs
near the top under a top girder that carries the four main cables' saddles, and ornamental caps on
the legs. Metres; X across the bridge, Y along it, Z up from the water. Two materials, "Stone" and
"Steel", which the game swaps for its own. Writes
Assets/Resources/Biomes/NewYork/Models/ManhattanTower.fbx and, with a render_dir, check renders.
"""
import math
import os
import sys

import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_brooklyn_tower as kit  # noqa: E402  (box, battered, box_uv, render)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "NewYork", "Models", "ManhattanTower.fbx")

DECK = 38.0
TOP = 98.0            # top of the legs
PIER_TOP = 12.0       # granite pier, from the river bed
LEG_X = 21.0          # leg centres either side of the middle (deck 37 m wide)
LEG_BOTTOM = (6.0, 8.0)   # leg width (across) and depth (along) at the pier
LEG_TOP = (4.5, 6.0)
ARCH_SPRING = 76.0    # the portal arch, between the legs' inner faces
ARCH_APEX = 88.0
ARCH_BAND = 2.6       # depth of the arch rib, radially
GIRDER = (90.0, 94.0) # top girder carrying the saddles
CABLES = (-17.5, -6.1, 6.1, 17.5)


def material(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1.0)
    return m


def tag(obj, mat):
    obj.data.materials.append(mat)
    return obj


def arch(name):
    """The portal: a segmental arch rib between the legs' inner faces, 3 m deep along the bridge."""
    inner = LEG_X - LEG_TOP[0] / 2
    span, rise = 2 * inner, ARCH_APEX - ARCH_SPRING
    radius = (span * span / 4 + rise * rise) / (2 * rise)
    centre = ARCH_APEX - radius
    half = math.asin(inner / radius)
    steps = 20
    ring = []
    for k in range(steps + 1):
        a = -half + 2 * half * k / steps
        ring.append((radius * math.sin(a), centre + radius * math.cos(a)))
    for k in range(steps, -1, -1):
        a = -half + 2 * half * k / steps
        ring.append(((radius + ARCH_BAND) * math.sin(a), centre + (radius + ARCH_BAND) * math.cos(a)))
    bm = bmesh.new()
    face = bm.faces.new([bm.verts.new((x, -1.5, z)) for x, z in ring])
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.y = 1.5
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def leg(x, steel):
    """A built-up steel leg, tapering, with corner ribs and bands every 12 m, and an ornamental cap."""
    parts = [kit.battered("Leg", LEG_BOTTOM, LEG_TOP, PIER_TOP, TOP)]
    parts[0].location.x = x
    # Corner ribs: the riveted plates standing proud of the leg.
    for sx in (-1, 1):
        for sy in (-1, 1):
            rib = kit.battered("Rib", (0.7, 0.7), (0.6, 0.6), PIER_TOP, TOP)
            rib.location = (x + sx * (LEG_BOTTOM[0] + LEG_TOP[0]) / 4, sy * (LEG_BOTTOM[1] + LEG_TOP[1]) / 4, 0)
            parts.append(rib)
    for z in range(int(PIER_TOP) + 12, int(TOP), 12):
        f = (z - PIER_TOP) / (TOP - PIER_TOP)
        w = LEG_BOTTOM[0] + (LEG_TOP[0] - LEG_BOTTOM[0]) * f + 0.6
        d = LEG_BOTTOM[1] + (LEG_TOP[1] - LEG_BOTTOM[1]) * f + 0.6
        parts.append(kit.box("Band", x - w / 2, x + w / 2, -d / 2, d / 2, z, z + 0.8))
    # The cap: a stepped crown and a short spire.
    parts.append(kit.box("Crown", x - LEG_TOP[0] / 2 - 0.6, x + LEG_TOP[0] / 2 + 0.6, -LEG_TOP[1] / 2 - 0.6, LEG_TOP[1] / 2 + 0.6, TOP, TOP + 1.2))
    spire = kit.battered("Spire", (LEG_TOP[0] - 0.5, LEG_TOP[1] - 0.5), (0.4, 0.4), TOP + 1.2, TOP + 6.0)
    spire.location.x = x
    parts.append(spire)
    for p in parts:
        tag(p, steel)
    return parts


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    stone = material("Stone", (0.72, 0.66, 0.56))
    steel = material("Steel", (0.52, 0.62, 0.72))
    parts = [tag(kit.battered("Pier", (2 * LEG_X + 16, 26), (2 * LEG_X + 12, 20), -6.0, PIER_TOP), stone),
             tag(kit.box("PierCap", -(LEG_X + 6.6), LEG_X + 6.6, -10.6, 10.6, PIER_TOP - 1.0, PIER_TOP), stone)]
    for x in (-LEG_X, LEG_X):
        parts += leg(x, steel)
    inner = LEG_X - LEG_TOP[0] / 2
    # The portal under the deck, and the arch and top girder near the top.
    parts.append(tag(kit.box("DeckPortal", -inner, inner, -2.0, 2.0, DECK - 6.0, DECK - 3.0), steel))
    parts.append(tag(arch("Arch"), steel))
    parts.append(tag(kit.box("Girder", -(LEG_X + LEG_TOP[0] / 2), LEG_X + LEG_TOP[0] / 2, -2.2, 2.2, *GIRDER), steel))
    # Spandrel posts from the top of the arch rib up to the girder.
    span, rise = 2 * inner, ARCH_APEX - ARCH_SPRING
    radius = (span * span / 4 + rise * rise) / (2 * rise)
    centre = ARCH_APEX - radius
    for x in (-14.0, -9.0, -4.5, 4.5, 9.0, 14.0):
        foot = centre + math.sqrt((radius + ARCH_BAND) ** 2 - x * x) - 0.3
        if foot < GIRDER[0] - 0.5:
            parts.append(tag(kit.box("Post", x - 0.5, x + 0.5, -1.2, 1.2, foot, GIRDER[0]), steel))
    # Saddles where the four main cables cross the top.
    for x in CABLES:
        parts.append(tag(kit.box("Saddle", x - 1.2, x + 1.2, -3.0, 3.0, GIRDER[1], GIRDER[1] + 2.0), steel))
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    tower = bpy.context.view_layer.objects.active
    tower.name = "ManhattanTower"
    kit.box_uv(tower)
    return tower


def main():
    tower = build()
    print("MANHATTAN_TOWER triangles", sum(len(p.vertices) - 2 for p in tower.data.polygons),
          "size", tuple(round(v, 1) for v in tower.dimensions), "materials", [m.name for m in tower.data.materials])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    tower.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE")
    print("wrote", OUT)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if args:
        kit.render(tower, args[0], "manhattan_tower", 55)


if __name__ == "__main__":
    main()
