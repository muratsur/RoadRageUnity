"""Builds a game-ready W-beam highway guard rail section in headless Blender.

Runs without the Blender app, through Blender's Python module (needs Python 3.11):

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1
    .venv/bin/python Tools/Blender/build_guardrail.py

The game loads the FBX and the three textures from Assets/Resources/Biomes/Guardrail
(Meshes/ and Textures/); copy them there after a rebuild. RR_SAMPLES sets preview
render samples.

Output (in ./out next to this script, or $RR_OUT):
  SM_guardrail_section_4m.fbx / .glb  - one mesh, one material, rail along +X, 4 m, tiles end to end
  T_guardrail_D.png   base colour
  T_guardrail_N.png   tangent-space normal (OpenGL / +Y, Unity's convention)
  T_guardrail_MSO.png R=metallic G=occlusion A=smoothness (same packing as the biome kits)
  preview.png         Cycles render of the baked asset in a roadside scene
"""
import math
import os
import sys

import bpy  # noqa: must load before bmesh
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out"))
os.makedirs(OUT, exist_ok=True)
TEX = 1024
SECTION = 4.0          # tile length along X
POST_SPACING = 2.0     # two posts per section: x=0.5 and x=2.5
RAIL_CENTRE_Z = 0.55   # W-beam centre height; top edge ends up ~0.71 m


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    return scene


def mesh_obj(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ---------------------------------------------------------------- geometry

def w_profile():
    """Half-thickness-free W-beam cross-section in (y, z), y = towards traffic.

    Real AASHTO M180 beam: 312 mm tall, 83 mm deep, two ridges facing traffic.
    """
    h, d = 0.312, 0.083
    pts = []
    # smooth W: z from -h/2..h/2, y = ridge offset; two cosine humps
    n = 28
    for i in range(n + 1):
        t = i / n
        z = -h / 2 + t * h
        y = d * (0.5 - 0.5 * math.cos(t * 4 * math.pi))
        # flatten the outer lips slightly like the real flanges
        if t < 0.06 or t > 0.94:
            y = d * 0.12
        pts.append((y, z))
    return pts


# Layout across the rail (Y), traffic on +Y:
#   y = 0            back of the beam valley, bolted to the blockout
#   y = 0 .. 0.083   W-beam humps towards traffic
#   y = 0 .. -0.20   timber blockout
#   y < -0.20        I-section post
BLOCK_DEPTH = 0.20
POST_DEPTH = 0.152


def build_rail():
    bm = bmesh.new()
    prof = w_profile()
    thick = 0.004
    segs = 8
    rows = []
    for s in range(segs + 1):
        x = SECTION * s / segs
        front = [bm.verts.new((x, y + thick, RAIL_CENTRE_Z + z)) for y, z in prof]
        back = [bm.verts.new((x, y, RAIL_CENTRE_Z + z)) for y, z in prof]
        rows.append((front, back))
    for s in range(segs):
        (f0, b0), (f1, b1) = rows[s], rows[s + 1]
        for i in range(len(prof) - 1):
            bm.faces.new((f0[i], f0[i + 1], f1[i + 1], f1[i]))
            bm.faces.new((b0[i], b1[i], b1[i + 1], b0[i + 1]))
        bm.faces.new((f0[-1], b0[-1], b1[-1], f1[-1]))
        bm.faces.new((f0[0], f1[0], b1[0], b0[0]))
    bm.normal_update()
    return mesh_obj("Rail", bm)


def box(bm, cx, cy, cz, sx, sy, sz):
    bmesh.ops.create_cube(bm, size=1.0,
                          matrix=Matrix.Translation((cx, cy, cz)) @ Matrix.Diagonal((sx, sy, sz, 1)))


def build_posts_and_blocks():
    steel = bmesh.new()
    wood = bmesh.new()
    top, bottom = 0.69, -0.15          # post top sits just under the beam's top edge
    h, cz = top - bottom, (top + bottom) / 2
    for px in (0.5, 0.5 + POST_SPACING):
        y0 = -BLOCK_DEPTH
        box(steel, px, y0 - 0.004, cz, 0.10, 0.008, h)                  # front flange
        box(steel, px, y0 - POST_DEPTH + 0.004, cz, 0.10, 0.008, h)     # rear flange
        box(steel, px, y0 - POST_DEPTH / 2, cz, 0.006, POST_DEPTH, h)   # web
        box(wood, px, -BLOCK_DEPTH / 2, RAIL_CENTRE_Z - 0.01, 0.15, BLOCK_DEPTH - 0.002, 0.28)
        # post bolt through the beam valley, head on the traffic face
        bmesh.ops.create_cone(steel, cap_ends=True, segments=10, radius1=0.016, radius2=0.013,
                              depth=0.014,
                              matrix=Matrix.Translation((px, 0.004 + 0.007, RAIL_CENTRE_Z))
                              @ Matrix.Rotation(math.radians(90), 4, "X"))
    # splice bolts at the section joint (x=0), where the next beam overlaps
    for dz in (-0.1, -0.035, 0.035, 0.1):
        for dx in (0.06, 0.16):
            if abs(dz) < 0.05:
                z = RAIL_CENTRE_Z + dz
                bmesh.ops.create_cone(steel, cap_ends=True, segments=8, radius1=0.013, radius2=0.011,
                                      depth=0.012,
                                      matrix=Matrix.Translation((dx, 0.004 + 0.006, z))
                                      @ Matrix.Rotation(math.radians(90), 4, "X"))
    return mesh_obj("Posts", steel), mesh_obj("Blocks", wood)


# ---------------------------------------------------------------- materials

def node(nt, kind, loc, **inputs):
    n = nt.nodes.new(kind)
    n.location = loc
    for k, v in inputs.items():
        if k in n.inputs:
            n.inputs[k].default_value = v
        else:
            setattr(n, k, v)
    return n


def galvanised_material():
    m = bpy.data.materials.new("M_guardrail_steel")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    coord = node(nt, "ShaderNodeTexCoord", (-1200, 0))
    # spangle mottling of hot-dip zinc
    spangle = node(nt, "ShaderNodeTexVoronoi", (-900, 200), Scale=90.0)
    nt.links.new(coord.outputs["Object"], spangle.inputs["Vector"])
    grime = node(nt, "ShaderNodeTexNoise", (-900, -100), Scale=14.0, Detail=12.0, Roughness=0.7)
    nt.links.new(coord.outputs["Object"], grime.inputs["Vector"])
    # road spray: dirt collects low down on the beam and posts
    sep = node(nt, "ShaderNodeSeparateXYZ", (-900, -400))
    nt.links.new(coord.outputs["Object"], sep.inputs[0])
    low = node(nt, "ShaderNodeMapRange", (-700, -400))
    low.inputs["From Min"].default_value = 0.1
    low.inputs["From Max"].default_value = 0.6
    low.inputs["To Min"].default_value = 1.0
    low.inputs["To Max"].default_value = 0.0
    nt.links.new(sep.outputs["Z"], low.inputs["Value"])
    dirt = node(nt, "ShaderNodeMath", (-500, -300), operation="MULTIPLY")
    nt.links.new(grime.outputs["Fac"], dirt.inputs[0])
    nt.links.new(low.outputs["Result"], dirt.inputs[1])
    dirt_c = node(nt, "ShaderNodeMath", (-350, -300), operation="MULTIPLY_ADD")
    dirt_c.inputs[1].default_value = 1.4
    dirt_c.inputs[2].default_value = -0.15
    dirt_c.use_clamp = True
    nt.links.new(dirt.outputs[0], dirt_c.inputs[0])

    zinc = node(nt, "ShaderNodeValToRGB", (-600, 200))
    zinc.color_ramp.elements[0].color = (0.30, 0.31, 0.31, 1)
    zinc.color_ramp.elements[1].color = (0.42, 0.43, 0.43, 1)
    nt.links.new(spangle.outputs["Distance"], zinc.inputs["Fac"])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.location = (-150, 150)
    mix.inputs[7].default_value = (0.22, 0.19, 0.15, 1)   # road grime
    nt.links.new(dirt_c.outputs[0], mix.inputs[0])
    nt.links.new(zinc.outputs["Color"], mix.inputs[6])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])

    rough = node(nt, "ShaderNodeMapRange", (-150, -100))
    rough.inputs["To Min"].default_value = 0.52
    rough.inputs["To Max"].default_value = 0.9
    nt.links.new(dirt_c.outputs[0], rough.inputs["Value"])
    nt.links.new(rough.outputs["Result"], bsdf.inputs["Roughness"])
    metal = node(nt, "ShaderNodeMapRange", (-150, -300))
    metal.inputs["To Min"].default_value = 0.7
    metal.inputs["To Max"].default_value = 0.15
    nt.links.new(dirt_c.outputs[0], metal.inputs["Value"])
    nt.links.new(metal.outputs["Result"], bsdf.inputs["Metallic"])

    bump = node(nt, "ShaderNodeBump", (-150, -550), Strength=0.12, Distance=0.001)
    nt.links.new(grime.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return m


def timber_material():
    m = bpy.data.materials.new("M_guardrail_timber")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    coord = node(nt, "ShaderNodeTexCoord", (-1100, 0))
    mapping = node(nt, "ShaderNodeMapping", (-900, 0))
    mapping.inputs["Scale"].default_value = (1.0, 1.0, 14.0)
    nt.links.new(coord.outputs["Object"], mapping.inputs["Vector"])
    grain = node(nt, "ShaderNodeTexWave", (-700, 0), Scale=6.0, Distortion=6.0, Detail=4.0)
    grain.wave_type = "RINGS"
    nt.links.new(mapping.outputs["Vector"], grain.inputs["Vector"])
    ramp = node(nt, "ShaderNodeValToRGB", (-450, 0))
    ramp.color_ramp.elements[0].color = (0.16, 0.11, 0.07, 1)   # weathered creosote brown
    ramp.color_ramp.elements[1].color = (0.30, 0.22, 0.15, 1)
    nt.links.new(grain.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.88
    bsdf.inputs["Metallic"].default_value = 0.0
    bump = node(nt, "ShaderNodeBump", (-200, -300), Strength=0.4, Distance=0.004)
    nt.links.new(grain.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return m


# ---------------------------------------------------------------- baking

def bake_pass(ob, mats, image, bake_type, via_emission=None):
    """Bake one pass into `image`. via_emission routes a BSDF input to emission first."""
    saved = []
    for m in mats:
        nt = m.node_tree
        img_node = nt.nodes.new("ShaderNodeTexImage")
        img_node.image = image
        nt.nodes.active = img_node
        if via_emission:
            bsdf = nt.nodes["Principled BSDF"]
            out = nt.nodes["Material Output"]
            emit = nt.nodes.new("ShaderNodeEmission")
            src = bsdf.inputs[via_emission]
            if src.is_linked:
                nt.links.new(src.links[0].from_socket, emit.inputs["Color"])
            else:
                v = src.default_value
                emit.inputs["Color"].default_value = (v, v, v, 1)
            old = out.inputs["Surface"].links[0].from_socket
            nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
            saved.append((nt, out, old, emit, img_node))
        else:
            saved.append((nt, None, None, None, img_node))
    kwargs = dict(type=bake_type, margin=8, use_clear=True)
    if bake_type == "DIFFUSE":
        kwargs["pass_filter"] = {"COLOR"}
    bpy.ops.object.bake(**kwargs)
    for nt, out, old, emit, img_node in saved:
        if out is not None:
            nt.links.new(old, out.inputs["Surface"])
            nt.nodes.remove(emit)
        nt.nodes.remove(img_node)


def image_pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def save_png(name, arr, colorspace="Non-Color"):
    img = bpy.data.images.new(name, arr.shape[1], arr.shape[0], alpha=True, float_buffer=False)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


def main():
    scene = reset()
    rail = build_rail()
    posts, blocks = build_posts_and_blocks()
    steel, timber = galvanised_material(), timber_material()
    for ob in (rail, posts):
        ob.data.materials.append(steel)
    blocks.data.materials.append(timber)

    # one mesh, two slots
    bpy.ops.object.select_all(action="DESELECT")
    for ob in (rail, posts, blocks):
        ob.select_set(True)
    bpy.context.view_layer.objects.active = rail
    bpy.ops.object.join()
    asset = bpy.context.active_object
    asset.name = "SM_guardrail_section_4m"
    asset.data.name = asset.name

    # smooth the curved W profile, keep boxes crisp
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0001)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.mesh.tris_convert_to_quads()
    bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=0.004, area_weight=0.0)
    bpy.ops.uv.pack_islands(margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_auto_smooth(angle=math.radians(35))

    # ---- bake the procedural look into textures
    scene.cycles.samples = 64
    scene.render.bake.margin = 8
    bpy.ops.object.select_all(action="DESELECT")
    asset.select_set(True)
    bpy.context.view_layer.objects.active = asset
    mats = [steel, timber]

    def new_img(n, non_color=True):
        img = bpy.data.images.new(n, TEX, TEX, alpha=False, float_buffer=True)
        if non_color:
            img.colorspace_settings.name = "Non-Color"
        return img

    img_d = new_img("bake_D", non_color=False)
    bake_pass(asset, mats, img_d, "DIFFUSE")
    img_n = new_img("bake_N")
    bake_pass(asset, mats, img_n, "NORMAL")
    img_r = new_img("bake_R")
    bake_pass(asset, mats, img_r, "ROUGHNESS")
    img_m = new_img("bake_M")
    bake_pass(asset, mats, img_m, "EMIT", via_emission="Metallic")
    scene.cycles.samples = 128
    img_ao = new_img("bake_AO")
    bake_pass(asset, mats, img_ao, "AO")

    d = image_pixels(img_d)
    d[..., 3] = 1.0
    # float-buffer bake is linear; PNG in sRGB wants gamma-encoded values
    d[..., :3] = np.where(d[..., :3] <= 0.0031308, d[..., :3] * 12.92,
                          1.055 * np.power(np.clip(d[..., :3], 0, None), 1 / 2.4) - 0.055)
    save_png("T_guardrail_D", d, "sRGB")
    n = image_pixels(img_n)
    n[..., 3] = 1.0
    save_png("T_guardrail_N", n)
    mso = np.zeros_like(d)
    mso[..., 0] = image_pixels(img_m)[..., 0]
    mso[..., 1] = image_pixels(img_ao)[..., 0]
    mso[..., 2] = 0.0
    mso[..., 3] = 1.0 - image_pixels(img_r)[..., 0]
    save_png("T_guardrail_MSO", mso)

    # ---- swap to a single baked material, as it will look in Unity
    baked = bpy.data.materials.new("M_guardrail")
    baked.use_nodes = True
    nt = baked.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    load = lambda f, cs: bpy.data.images.load(os.path.join(OUT, f))
    tex_d = node(nt, "ShaderNodeTexImage", (-800, 300))
    tex_d.image = load("T_guardrail_D.png", "sRGB")
    tex_n = node(nt, "ShaderNodeTexImage", (-800, -300))
    tex_n.image = load("T_guardrail_N.png", "Non-Color")
    tex_n.image.colorspace_settings.name = "Non-Color"
    tex_m = node(nt, "ShaderNodeTexImage", (-800, 0))
    tex_m.image = load("T_guardrail_MSO.png", "Non-Color")
    tex_m.image.colorspace_settings.name = "Non-Color"
    nt.links.new(tex_d.outputs["Color"], bsdf.inputs["Base Color"])
    sep = node(nt, "ShaderNodeSeparateColor", (-500, 0))
    nt.links.new(tex_m.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    inv = node(nt, "ShaderNodeMath", (-300, -100), operation="SUBTRACT")
    inv.inputs[0].default_value = 1.0
    nt.links.new(tex_m.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nmap = node(nt, "ShaderNodeNormalMap", (-400, -300))
    nt.links.new(tex_n.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    asset.data.materials.clear()
    asset.data.materials.append(baked)

    tris = sum(len(p.vertices) - 2 for p in asset.data.polygons)
    print(f"RR_ASSET {asset.name} verts={len(asset.data.vertices)} tris={tris}")

    # ---- export (asset alone, at origin)
    bpy.ops.object.select_all(action="DESELECT")
    asset.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, asset.name + ".fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=True,
                             path_mode="STRIP", add_leaf_bones=False)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, asset.name + ".glb"), use_selection=True,
                              export_format="GLB")

    # ---- preview scene
    for i in range(1, 4):
        dup = asset.copy()
        dup.location.x = i * SECTION
        scene.collection.objects.link(dup)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(8, 2, 0))
    ground = bpy.context.active_object
    ground.scale = (60, 30, 1)
    gm = bpy.data.materials.new("ground")
    gm.use_nodes = True
    gnt = gm.node_tree
    gb = gnt.nodes["Principled BSDF"]
    gn = node(gnt, "ShaderNodeTexNoise", (-600, 0), Scale=900.0, Detail=6.0)
    gr = node(gnt, "ShaderNodeValToRGB", (-350, 0))
    gr.color_ramp.elements[0].color = (0.035, 0.035, 0.037, 1)
    gr.color_ramp.elements[1].color = (0.09, 0.09, 0.09, 1)
    gnt.links.new(gn.outputs["Fac"], gr.inputs["Fac"])
    gnt.links.new(gr.outputs["Color"], gb.inputs["Base Color"])
    gb.inputs["Roughness"].default_value = 0.9
    ground.data.materials.append(gm)
    # grass verge behind the rail
    bpy.ops.mesh.primitive_plane_add(size=1, location=(8, -8, 0.005))
    verge = bpy.context.active_object
    verge.scale = (60, 14, 1)
    vm = bpy.data.materials.new("verge")
    vm.use_nodes = True
    vnt = vm.node_tree
    vb = vnt.nodes["Principled BSDF"]
    vn = node(vnt, "ShaderNodeTexNoise", (-600, 0), Scale=40.0, Detail=10.0)
    vr = node(vnt, "ShaderNodeValToRGB", (-350, 0))
    vr.color_ramp.elements[0].color = (0.025, 0.04, 0.012, 1)
    vr.color_ramp.elements[1].color = (0.06, 0.08, 0.025, 1)
    vnt.links.new(vn.outputs["Fac"], vr.inputs["Fac"])
    vnt.links.new(vr.outputs["Color"], vb.inputs["Base Color"])
    vb.inputs["Roughness"].default_value = 0.95
    verge.data.materials.append(vm)

    bpy.ops.mesh.primitive_plane_add(size=1, location=(8, 0.9, 0.004))
    line = bpy.context.active_object
    line.scale = (60, 0.15, 1)
    lm = bpy.data.materials.new("line")
    lm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.55, 0.52, 1)
    lm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.7
    line.data.materials.append(lm)

    world = bpy.data.worlds.new("sky")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    try:
        sky.sky_type = "MULTIPLE_SCATTERING"
    except TypeError:
        sky.sky_type = "NISHITA"
    sky.sun_elevation = math.radians(32)
    sky.sun_rotation = math.radians(35)
    world.node_tree.links.new(sky.outputs["Color"], world.node_tree.nodes["Background"].inputs["Color"])
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.3

    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 40
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (-1.6, 2.4, 1.15)
    target = Vector((5.0, -0.4, 0.45))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.cycles.samples = int(os.environ.get("RR_SAMPLES", "96"))
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = -1.6
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)

    # close-up
    cam.location = (1.4, 1.1, 0.95)
    target = Vector((2.5, -0.35, 0.5))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, "preview_closeup.png")
    bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "guardrail.blend"))
    print("RR_DONE")


main()
