"""Turns the Rodin hero truck download into the game's hero car.

    blender -b -P Tools/Rodin/build_hero_truck_asset.py

Takes Assets/Resources/TestAssets/hero_truck_h2 (git-ignored), keeps its 62,500 triangle
detail level, scales it to 5.5 m long on the ground with its origin under the middle, and
writes it to Assets/Resources/Vehicles, which is committed and ships. Metallic and
roughness are packed the way URP Lit reads them: R metallic, A smoothness.
"""
import os

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Resources", "TestAssets", "hero_truck_h2")
OUT = os.path.join(ROOT, "Assets", "Resources", "Vehicles")
LENGTH = 5.5

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
# The file's front is Blender's -Y; these axes put it on Unity's +Z, upright.
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "SM_hero_truck.fbx"), use_selection=True,
                         axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
                         mesh_smooth_type="FACE", bake_space_transform=True, path_mode="STRIP", embed_textures=False)

load = lambda name: bpy.data.images.load(os.path.join(SRC, name))
base, normal, metal, rough = load("texture_diffuse.png"), load("texture_normal.png"), load("texture_metallic.png"), load("texture_roughness.png")
for image, name in ((base, "T_hero_truck_D.png"), (normal, "T_hero_truck_N.png")):
    image.save_render(os.path.join(OUT, name))
w, h = metal.size
assert tuple(rough.size) == (w, h)
m, r = list(metal.pixels), list(rough.pixels)
out = [0.0] * (w * h * 4)
for i in range(w * h):
    out[i * 4] = out[i * 4 + 1] = out[i * 4 + 2] = m[i * 4]
    out[i * 4 + 3] = 1.0 - r[i * 4]
ms = bpy.data.images.new("ms", w, h, alpha=True, float_buffer=False)
ms.colorspace_settings.name = "Non-Color"
ms.pixels = out
ms.filepath_raw = os.path.join(OUT, "T_hero_truck_MS.png")
ms.file_format = "PNG"
ms.save()
print("DONE", w, h)
