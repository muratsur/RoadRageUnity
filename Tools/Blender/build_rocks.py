"""Builds Greenwood's rocks in headless Blender: six boulders and three rock-face
sections for road cuttings, sharing one baked texture atlas.

Boulders are noise-displaced spheres with flattened, buried bases. Rock faces are
8 m sections of a road cutting: layered strata with ledges, a face leaning back into
the slope, an irregular crest that rolls back over the top, and ends that taper down
into the ground so sections can stand alone or overlap in a run.

The look is one procedural stone material - grey-brown weathered rock, darker cracks,
horizontal strata on the faces, dirt settled on upward-facing surfaces - baked into a
shared atlas: colour, normal (tangent space, OpenGL) and MSO (R metallic, G occlusion,
A smoothness, like the biome kits).

Runs without the Blender app, through Blender's Python module (needs Python 3.11):

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1
    .venv/bin/python Tools/Blender/build_rocks.py

Output (./out_rocks next to this script, or $RR_OUT):
  SM_rock_boulder_01..06.fbx, SM_rock_face_01..03.fbx
  T_rocks_D.jpg, T_rocks_N.png, T_rocks_MSO.png (half size: occlusion and
  smoothness are soft maps, and these are kept out of LFS so size matters)
  preview.png
The game loads them from Assets/Resources/Biomes/Rocks (Meshes/, Textures/).
Axes: Blender +Z up. Rock faces run along X, centred on the origin, and face -Y (the
road); the game measures each mesh to orient it, so the export axes do not matter.
"""
import bpy  # noqa: must load before bmesh and mathutils
import bmesh
import math
import os
import random

import numpy as np
from mathutils import Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_rocks"))
os.makedirs(OUT, exist_ok=True)
TEX = int(os.environ.get("RR_TEX", "2048"))
SAMPLES = int(os.environ.get("RR_SAMPLES", "96"))
rng = random.Random(23)


def fbm(p, octaves=5, lac=2.0, gain=0.5):
    total, amp, norm = 0.0, 1.0, 0.0
    q = Vector(p)
    for _ in range(octaves):
        total += noise.noise(q) * amp
        norm += amp
        amp *= gain
        q *= lac
    return total / norm


# ---------------------------------------------------------------- meshes

def to_object(name, bm):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def build_boulder(i):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
    seed = Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), rng.uniform(-50, 50)))
    squash = rng.uniform(0.55, 0.8)
    stretch = rng.uniform(0.9, 1.35)
    for v in bm.verts:
        d = v.co.normalized()
        big = fbm(d * 1.1 + seed, 3)
        mid = fbm(d * 3.0 + seed * 1.7, 4)
        # Faceted planes: quantise the big noise a little so the rock has flatter faces
        # and sharper breaks, like fractured stone rather than a potato.
        facet = round(big * 3.0) / 3.0
        r = 1.0 + 0.22 * (0.6 * big + 0.4 * facet) + 0.08 * mid
        v.co = d * r
        v.co.x *= stretch
        v.co.z *= squash
    zs = [v.co.z for v in bm.verts]
    floor = min(zs) + (max(zs) - min(zs)) * 0.18
    for v in bm.verts:
        if v.co.z < floor:
            v.co.z = floor + (v.co.z - floor) * 0.1       # flattened, buried base
    for v in bm.verts:
        v.co.z -= floor
    ob = to_object(f"SM_rock_boulder_{i:02d}", bm)
    dec = ob.modifiers.new("decimate", "DECIMATE")
    dec.ratio = 0.8
    return ob


def build_face(i, length=8.0, height=3.4, cols=128, rows=56):
    bm = bmesh.new()
    seed = Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), 0))
    strata = rng.uniform(0.32, 0.5)                          # m per layer
    grid = []
    for r in range(rows + 1):
        row = []
        t = r / rows                                        # 0 bottom .. 1 over the crest
        for c in range(cols + 1):
            s = c / cols
            x = (s - 0.5) * length
            # Height tapers to the ground at both ends, and the crest line wanders.
            taper = min(1.0, s / 0.22, (1 - s) / 0.22)
            taper = taper * taper * (3 - 2 * taper)
            crest = height * taper * (0.8 + 0.35 * fbm(Vector((x * 0.25, 0, 3)) + seed, 3))
            # The last fifth of the rows rolls back over the top into the slope.
            face_t = min(t / 0.8, 1.0)
            z = -0.3 + (crest + 0.3) * face_t
            y = 0.28 * z                                    # face leans back into the slope
            if t > 0.8:
                over = (t - 0.8) / 0.2
                z = crest + 0.35 * math.sin(over * math.pi / 2) * taper
                y += 1.6 * over * taper
            # Strata: a stepped profile per layer, each ledge sticking out differently.
            layer = math.floor(z / strata)
            frac = z / strata - layer
            ledge = fbm(Vector((layer * 3.1, x * 0.08, 0)) + seed, 2)
            step = (0.5 * ledge + 0.22 * (frac ** 3)) * taper
            # Fractured blocks: quantised noise gives flat planes and sharp breaks.
            blocks = round(fbm(Vector((x * 0.45, z * 0.6, 7)) + seed, 3) * 4) / 4 * 0.7
            bumps = 0.16 * fbm(Vector((x * 0.9, z * 1.2, 0)) + seed, 4)
            y -= (step + blocks + bumps) * taper
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for r in range(rows):
        for c in range(cols):
            bm.faces.new((grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]))
    ob = to_object(f"SM_rock_face_{i:02d}", bm)
    # Blasted rock is angular: flat-shaded facets, smoothed only across shallow angles.
    for p in ob.data.polygons:
        p.use_smooth = False
    # Winding (x then z) already makes the face normals point at the road, -Y.
    dec = ob.modifiers.new("decimate", "DECIMATE")
    dec.ratio = 0.45
    return ob


# ---------------------------------------------------------------- material

def node(nt, kind, **values):
    n = nt.nodes.new(kind)
    for k, v in values.items():
        if k in n.inputs:
            n.inputs[k].default_value = v
        else:
            setattr(n, k, v)
    return n


def stone_material():
    m = bpy.data.materials.new("M_rocks")
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    geo = node(nt, "ShaderNodeNewGeometry")
    pos = geo.outputs["Position"]

    mottle = node(nt, "ShaderNodeTexNoise", Scale=1.3, Detail=8.0, Roughness=0.6)
    nt.links.new(pos, mottle.inputs["Vector"])
    grain = node(nt, "ShaderNodeTexNoise", Scale=22.0, Detail=6.0, Roughness=0.7)
    nt.links.new(pos, grain.inputs["Vector"])
    # Sparse fractures: thin lines where a large-scale noise crosses 0.5. (A Voronoi
    # edge network read as crazy paving.)
    frac_n = node(nt, "ShaderNodeTexNoise", Scale=0.55, Detail=3.0, Roughness=0.5)
    nt.links.new(pos, frac_n.inputs["Vector"])
    centred = node(nt, "ShaderNodeMath", operation="SUBTRACT")
    centred.inputs[1].default_value = 0.5
    nt.links.new(frac_n.outputs["Fac"], centred.inputs[0])
    dist = node(nt, "ShaderNodeMath", operation="ABSOLUTE")
    nt.links.new(centred.outputs[0], dist.inputs[0])
    crack_mask = node(nt, "ShaderNodeMapRange")
    crack_mask.inputs["From Min"].default_value = 0.0
    crack_mask.inputs["From Max"].default_value = 0.006
    crack_mask.inputs["To Min"].default_value = 1.0
    crack_mask.inputs["To Max"].default_value = 0.0
    nt.links.new(dist.outputs[0], crack_mask.inputs["Value"])
    # Horizontal strata banding (shows on the cut faces, subtle on boulders).
    sep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(pos, sep.inputs[0])
    bands = node(nt, "ShaderNodeTexWave", Scale=1.6, Distortion=3.0, Detail=3.0, bands_direction="Z")
    nt.links.new(pos, bands.inputs["Vector"])

    ramp = node(nt, "ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.3
    ramp.color_ramp.elements[0].color = (0.085, 0.08, 0.072, 1)
    ramp.color_ramp.elements[1].position = 0.75
    ramp.color_ramp.elements[1].color = (0.22, 0.205, 0.185, 1)
    mix_in = node(nt, "ShaderNodeMath", operation="MULTIPLY_ADD")
    mix_in.inputs[1].default_value = 0.07
    nt.links.new(bands.outputs["Fac"], mix_in.inputs[0])
    nt.links.new(mottle.outputs["Fac"], mix_in.inputs[2])
    nt.links.new(mix_in.outputs[0], ramp.inputs["Fac"])

    grain_k = node(nt, "ShaderNodeMapRange")
    grain_k.inputs["To Min"].default_value = 0.85
    grain_k.inputs["To Max"].default_value = 1.12
    nt.links.new(grain.outputs["Fac"], grain_k.inputs["Value"])
    col = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY", Factor=1.0)
    nt.links.new(ramp.outputs["Color"], col.inputs[6])
    comb = node(nt, "ShaderNodeCombineColor")
    for c in range(3):
        nt.links.new(grain_k.outputs["Result"], comb.inputs[c])
    nt.links.new(comb.outputs[0], col.inputs[7])

    # Cracks darken.
    crack_col = node(nt, "ShaderNodeMix", data_type="RGBA", Factor=0.0)
    crack_col.inputs[7].default_value = (0.045, 0.04, 0.035, 1)
    crack_k = node(nt, "ShaderNodeMath", operation="MULTIPLY")
    crack_k.inputs[1].default_value = 0.85
    nt.links.new(crack_mask.outputs["Result"], crack_k.inputs[0])
    nt.links.new(crack_k.outputs[0], crack_col.inputs[0])
    nt.links.new(col.outputs[2], crack_col.inputs[6])

    # Dirt settles on surfaces facing up.
    nsep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], nsep.inputs[0])
    up = node(nt, "ShaderNodeMapRange")
    up.inputs["From Min"].default_value = 0.55
    up.inputs["From Max"].default_value = 0.9
    nt.links.new(nsep.outputs["Z"], up.inputs["Value"])
    dirt_noise = node(nt, "ShaderNodeTexNoise", Scale=6.0, Detail=5.0)
    nt.links.new(pos, dirt_noise.inputs["Vector"])
    dirt_k = node(nt, "ShaderNodeMath", operation="MULTIPLY")
    nt.links.new(up.outputs["Result"], dirt_k.inputs[0])
    nt.links.new(dirt_noise.outputs["Fac"], dirt_k.inputs[1])
    dirt_k2 = node(nt, "ShaderNodeMath", operation="MULTIPLY", use_clamp=True)
    dirt_k2.inputs[1].default_value = 1.6
    nt.links.new(dirt_k.outputs[0], dirt_k2.inputs[0])
    dirt = node(nt, "ShaderNodeMix", data_type="RGBA")
    dirt.inputs[7].default_value = (0.075, 0.055, 0.035, 1)
    nt.links.new(dirt_k2.outputs[0], dirt.inputs[0])
    nt.links.new(crack_col.outputs[2], dirt.inputs[6])
    # Water staining: dark streaks running down the faces.
    streak_map = node(nt, "ShaderNodeMapping")
    streak_map.inputs["Scale"].default_value = (7.0, 7.0, 0.5)
    nt.links.new(pos, streak_map.inputs["Vector"])
    streak = node(nt, "ShaderNodeTexNoise", Scale=1.0, Detail=3.0)
    nt.links.new(streak_map.outputs["Vector"], streak.inputs["Vector"])
    streak_k = node(nt, "ShaderNodeMapRange")
    streak_k.inputs["From Min"].default_value = 0.5
    streak_k.inputs["From Max"].default_value = 0.72
    streak_k.inputs["To Max"].default_value = 0.55
    nt.links.new(streak.outputs["Fac"], streak_k.inputs["Value"])
    stained = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    stained.inputs[7].default_value = (0.45, 0.43, 0.40, 1)
    nt.links.new(streak_k.outputs["Result"], stained.inputs[0])
    nt.links.new(dirt.outputs[2], stained.inputs[6])
    nt.links.new(stained.outputs[2], bsdf.inputs["Base Color"])

    rough = node(nt, "ShaderNodeMapRange")
    rough.inputs["To Min"].default_value = 0.72
    rough.inputs["To Max"].default_value = 0.95
    nt.links.new(grain.outputs["Fac"], rough.inputs["Value"])
    nt.links.new(rough.outputs["Result"], bsdf.inputs["Roughness"])

    # Surface relief: grain, cracks cut in, larger lumps.
    h = node(nt, "ShaderNodeMath", operation="SUBTRACT")
    nt.links.new(grain.outputs["Fac"], h.inputs[0])
    nt.links.new(crack_k.outputs[0], h.inputs[1])
    bump = node(nt, "ShaderNodeBump", Strength=0.55, Distance=0.02)
    nt.links.new(h.outputs[0], bump.inputs["Height"])
    bump2 = node(nt, "ShaderNodeBump", Strength=0.35, Distance=0.08)
    nt.links.new(mottle.outputs["Fac"], bump2.inputs["Height"])
    nt.links.new(bump2.outputs["Normal"], bump.inputs["Normal"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return m


# ---------------------------------------------------------------- bake

def bake(objs, mat, image, kind, via_emission=None, samples=16):
    scene = bpy.context.scene
    scene.cycles.samples = samples
    nt = mat.node_tree
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = image
    nt.nodes.active = img
    restore = None
    if via_emission:
        bsdf = nt.nodes["Principled BSDF"]
        out = nt.nodes["Material Output"]
        emit = nt.nodes.new("ShaderNodeEmission")
        src = bsdf.inputs[via_emission]
        nt.links.new(src.links[0].from_socket, emit.inputs["Color"])
        restore = (out, out.inputs["Surface"].links[0].from_socket, emit)
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
    kw = dict(type=kind, margin=8, use_clear=True)
    if kind == "DIFFUSE":
        kw["pass_filter"] = {"COLOR"}
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.bake(**kw)
    if restore:
        out, old, emit = restore
        nt.links.new(old, out.inputs["Surface"])
        nt.nodes.remove(emit)
    nt.nodes.remove(img)


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def save_png(name, arr, colorspace, fmt="PNG"):
    img = bpy.data.images.new(name, arr.shape[1], arr.shape[0], alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
    ext = ".jpg" if fmt == "JPEG" else ".png"
    img.filepath_raw = os.path.join(OUT, name + ext)
    img.file_format = fmt
    if fmt == "JPEG":
        bpy.context.scene.render.image_settings.quality = 92
    img.save()


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"

    boulders = [build_boulder(i + 1) for i in range(6)]
    faces = [build_face(i + 1) for i in range(3)]
    objs = boulders + faces
    for o in objs:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.modifier_apply(modifier="decimate")
        o.select_set(False)
    mat = stone_material()
    for o in objs:
        o.data.materials.append(mat)

    # One shared UV atlas: unwrap every object, then pack all islands together.
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.003, area_weight=0.0,
                             scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.003, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")

    def new_img(name, non_color):
        img = bpy.data.images.new(name, TEX, TEX, alpha=False, float_buffer=True)
        if non_color:
            img.colorspace_settings.name = "Non-Color"
        return img

    d_img = new_img("d", False)
    bake(objs, mat, d_img, "DIFFUSE")
    n_img = new_img("n", True)
    bake(objs, mat, n_img, "NORMAL")
    r_img = new_img("r", True)
    bake(objs, mat, r_img, "ROUGHNESS")
    ao_img = new_img("ao", True)
    bake(objs, mat, ao_img, "AO", samples=64)

    d = pixels(d_img)
    d[..., :3] = np.where(d[..., :3] <= 0.0031308, d[..., :3] * 12.92,
                          1.055 * np.power(np.clip(d[..., :3], 0, None), 1 / 2.4) - 0.055)
    d[..., 3] = 1
    save_png("T_rocks_D", d, "sRGB", fmt="JPEG")
    n = pixels(n_img)
    n[..., 3] = 1
    save_png("T_rocks_N", n, "Non-Color")
    mso = np.zeros_like(d)
    mso[..., 1] = pixels(ao_img)[..., 0]
    mso[..., 3] = 1 - pixels(r_img)[..., 0]
    half = mso.reshape(TEX // 2, 2, TEX // 2, 2, 4).mean(axis=(1, 3))
    save_png("T_rocks_MSO", half, "Non-Color")

    # Swap to the baked material (what Unity will show) and export each mesh.
    baked = bpy.data.materials.new("M_rocks_baked")
    nt = baked.node_tree
    b = nt.nodes["Principled BSDF"]
    t_d = node(nt, "ShaderNodeTexImage")
    t_d.image = bpy.data.images.load(os.path.join(OUT, "T_rocks_D.jpg"))
    t_n = node(nt, "ShaderNodeTexImage")
    t_n.image = bpy.data.images.load(os.path.join(OUT, "T_rocks_N.png"))
    t_n.image.colorspace_settings.name = "Non-Color"
    t_m = node(nt, "ShaderNodeTexImage")
    t_m.image = bpy.data.images.load(os.path.join(OUT, "T_rocks_MSO.png"))
    t_m.image.colorspace_settings.name = "Non-Color"
    nt.links.new(t_d.outputs["Color"], b.inputs["Base Color"])
    inv = node(nt, "ShaderNodeMath", operation="SUBTRACT")
    inv.inputs[0].default_value = 1.0
    nt.links.new(t_m.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], b.inputs["Roughness"])
    nm = node(nt, "ShaderNodeNormalMap")
    nt.links.new(t_n.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(baked)

    for o in objs:
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, o.name + ".fbx"), use_selection=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=True,
                                 path_mode="STRIP", add_leaf_bones=False)
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        print(f"RR_ROCK {o.name} tris={tris} size={tuple(round(v, 2) for v in o.dimensions)}")

    # Preview: a cutting beside a road with boulders in front.
    for k, f in enumerate(faces):
        f.location = (k * 7.2 - 7.2, 3.0, 0)
    for k, b_ in enumerate(boulders):
        b_.location = (-9 + k * 3.4, 1.4 + (k % 2) * 0.8, 0)
        s = 0.5 + (k % 3) * 0.35
        b_.scale = (s, s, s)
        b_.rotation_euler = (0, 0, k * 1.3)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, -3, 0.01))
    road = bpy.context.active_object
    road.scale = (60, 6, 1)
    rm = bpy.data.materials.new("road")
    rm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.07, 0.07, 0.072, 1)
    road.data.materials.append(rm)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 20, 0))
    ground = bpy.context.active_object
    ground.scale = (80, 46, 1)
    gm = bpy.data.materials.new("ground")
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.035, 0.022, 1)
    ground.data.materials.append(gm)
    world = bpy.data.worlds.new("w")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.64, 0.68, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 1.5
    sun.data.angle = math.radians(10)
    sun.rotation_euler = (math.radians(55), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 28
    cam.location = (-2.0, -9.5, 2.6)
    cam.rotation_euler = (math.radians(82), 0, math.radians(-8))
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)
    print("RR_DONE")


main()
