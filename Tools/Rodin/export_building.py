"""Re-exports a Rodin building for the game: transforms baked, base on the ground,
centred, scaled to a real height, front on Unity's +Z.

    blender -b -P Tools/Rodin/export_building.py -- <source.fbx> <out.fbx> <height m> [more triples]
"""
import sys

import bpy
from mathutils import Vector

MAX_FOOTPRINT = 16.0
args = sys.argv[sys.argv.index("--") + 1:]
for source, target, height in zip(args[0::3], args[1::3], args[2::3]):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    bpy.ops.import_scene.fbx(filepath=source)
    ob = next(o for o in bpy.data.objects if o.type == "MESH")
    for o in list(bpy.data.objects):
        if o is not ob:
            bpy.data.objects.remove(o)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    corners = [Vector(c) for c in ob.bound_box]
    lo = Vector(min(c[i] for c in corners) for i in range(3))
    hi = Vector(max(c[i] for c in corners) for i in range(3))
    k = float(height) / (hi.z - lo.z)
    # Must stand inside a village plot, hedge to hedge.
    k = min(k, MAX_FOOTPRINT / max(hi.x - lo.x, hi.y - lo.y))
    for v in ob.data.vertices:
        v.co = Vector(((v.co.x - (lo.x + hi.x) / 2) * k, (v.co.y - (lo.y + hi.y) / 2) * k, (v.co.z - lo.z) * k))
    ob.data.update()
    print("BUILDING", target[-22:], "size", [round((hi[i] - lo[i]) * k, 1) for i in range(3)])
    bpy.ops.export_scene.fbx(filepath=target, use_selection=True, axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE",
                             bake_space_transform=True, path_mode="STRIP", embed_textures=False)
