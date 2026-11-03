"""Re-exports Rodin cars for the game: transforms baked, on the ground, centred, front on
Unity's +Z. Rodin's own FBX carries a node rotation the traffic code reads as a car
standing on its nose.

    blender -b -P Tools/Rodin/export_country_car.py -- <source.fbx> <out.fbx> [more pairs]
"""
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
for source, target in zip(args[0::2], args[1::2]):
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
    for v in ob.data.vertices:
        v.co = Vector((v.co.x - (lo.x + hi.x) / 2, v.co.y - (lo.y + hi.y) / 2, v.co.z - lo.z))
    ob.data.update()
    print("CAR", target, "size", [round(hi[i] - lo[i], 2) for i in range(3)])
    bpy.ops.export_scene.fbx(filepath=target, use_selection=True, axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE",
                             bake_space_transform=True, path_mode="STRIP", embed_textures=False)
