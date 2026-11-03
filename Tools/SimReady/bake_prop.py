"""Turns an NVIDIA SimReady asset (OpenUSD, CC BY 4.0) into a game prop: one low-poly mesh with
one baked colour and normal texture, base on the ground, centred, front on Unity's +Z.

    blender -b -P Tools/SimReady/bake_prop.py -- <asset.usd> <out dir> <name> <triangles> [texture size]

Writes <out dir>/Meshes/SM_<name>.fbx and <out dir>/Textures/T_<name>_D.png, _N.png.
SimReady assets are made for simulation, at 100k-500k triangles and several materials each.
"""
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, out, name, target = args[0], args[1], args[2], int(args[3])
size = int(args[4]) if len(args) > 4 else 1024

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.usd_import(filepath=src)
# The meshes come in as USD instances: hidden prototype collections placed by empties.
# Real copies of them are what gets joined and baked; the prototypes go.
bpy.ops.object.select_all(action="DESELECT")
instancers = [o for o in bpy.context.view_layer.objects if o.instance_type == "COLLECTION"]
for o in instancers:
    o.select_set(True)
if instancers:
    bpy.context.view_layer.objects.active = instancers[0]
    bpy.ops.object.duplicates_make_real()
for c in [c for c in bpy.data.collections if c.name.startswith("proto")]:
    for o in list(c.objects):
        bpy.data.objects.remove(o)
    bpy.data.collections.remove(c)
bpy.context.view_layer.update()
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
if len(meshes) > 1:
    bpy.ops.object.join()
high = bpy.context.view_layer.objects.active

low = high.copy()
low.data = high.data.copy()
bpy.context.scene.collection.objects.link(low)
before = sum(len(p.vertices) - 2 for p in high.data.polygons)
dec = low.modifiers.new("dec", "DECIMATE")
dec.ratio = min(1.0, target / before)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = low
low.select_set(True)
bpy.ops.object.modifier_apply(modifier="dec")
while low.data.uv_layers:
    low.data.uv_layers.remove(low.data.uv_layers[0])
low.data.uv_layers.new(name="bake")
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(island_margin=0.01)
bpy.ops.object.mode_set(mode="OBJECT")

# One material on the low mesh, with an image node the bakes write into.
mat = bpy.data.materials.new("bake")
mat.use_nodes = True
low.data.materials.clear()
low.data.materials.append(mat)
node = mat.node_tree.nodes.new("ShaderNodeTexImage")
mat.node_tree.nodes.active = node

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 4
scene.render.bake.use_selected_to_active = True
dims = Vector(high.dimensions)
scene.render.bake.cage_extrusion = max(dims) * 0.02
scene.render.bake.max_ray_distance = max(dims) * 0.05
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
low.select_set(True)
bpy.context.view_layer.objects.active = low
os.makedirs(os.path.join(out, "Textures"), exist_ok=True)
os.makedirs(os.path.join(out, "Meshes"), exist_ok=True)
# The colour bake reads base colour through an emission shader: Cycles' diffuse pass is black
# on metallic surfaces (the drums, the barrier's frame).
for m in {s.material for s in high.material_slots if s.material}:
    tree = m.node_tree
    shader = next((n for n in tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    output = next((n for n in tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if shader is None or output is None:
        continue
    emit = tree.nodes.new("ShaderNodeEmission")
    base = shader.inputs["Base Color"]
    if base.links:
        tree.links.new(base.links[0].from_socket, emit.inputs["Color"])
    else:
        emit.inputs["Color"].default_value = base.default_value
    m["rr_surface"] = output.inputs["Surface"].links[0].from_socket.path_from_id() if output.inputs["Surface"].links else ""
    m["rr_emit"] = emit.name
for kind, suffix, colour in (("EMIT", "D", True), ("NORMAL", "N", False)):
    # Emission for the colour, the real shaders for the normals.
    for m in {s.material for s in high.material_slots if s.material}:
        tree = m.node_tree
        output = next((n for n in tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
        emit = tree.nodes.get(m.get("rr_emit", ""))
        shader = next((n for n in tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if output is None or emit is None or shader is None:
            continue
        tree.links.new((emit if kind == "EMIT" else shader).outputs[0], output.inputs["Surface"])
    img = bpy.data.images.new(f"T_{name}_{suffix}", size, size, alpha=False, is_data=not colour)
    node.image = img
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    bpy.ops.object.bake(type=kind, margin=8)
    img.filepath_raw = os.path.join(out, "Textures", f"T_{name}_{suffix}.png")
    img.file_format = "PNG"
    img.save()

# Base on the ground, centred, as export_building.py does.
bpy.data.objects.remove(high)
lo = Vector(min(v.co[i] for v in low.data.vertices) for i in range(3))
hi = Vector(max(v.co[i] for v in low.data.vertices) for i in range(3))
for v in low.data.vertices:
    v.co = Vector((v.co.x - (lo.x + hi.x) / 2, v.co.y - (lo.y + hi.y) / 2, v.co.z - lo.z))
low.data.update()
low.name = f"SM_{name}"
bpy.ops.object.select_all(action="DESELECT")
low.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Meshes", f"SM_{name}.fbx"), use_selection=True,
                         axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
                         mesh_smooth_type="FACE", bake_space_transform=True, path_mode="STRIP", embed_textures=False)
print(f"PROP {name}: {before} -> {sum(len(p.vertices) - 2 for p in low.data.polygons)} triangles, "
      f"size {[round(hi[i] - lo[i], 2) for i in range(3)]} m")
