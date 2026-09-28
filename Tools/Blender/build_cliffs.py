"""Builds Greenwood's roadside cliffs in headless Blender: three 24 m wall sections
peaking at 22, 30 and 36 m, and a 16 m end cap, sharing one baked texture atlas.

Each section is a road cutting at landscape scale: thick strata with ledges, big
fractured blocks and vertical joints, a face leaning back into the hillside, and a
crest that rolls over into a short top and a back slope down to the ground - a closed
hill, not a sheet. Every section ends in the same join profile, so they butt together
in any order as one continuous wall; the cap starts from that profile and slopes
down to the ground to close a run. The stone is weathered grey with strata banding, sparse
fractures, water streaks and moss on the ledges.

Runs without the Blender app, through Blender's Python module (needs Python 3.11):

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1
    .venv/bin/python Tools/Blender/build_cliffs.py

Output (./out_cliffs next to this script, or $RR_OUT):
  SM_cliff_01..03.fbx
  SM_cliff_end.fbx (run end cap)
  T_cliffs_D.jpg, T_cliffs_N.jpg, T_cliffs_MSO.png (half size)
  preview.png
The game loads them from Assets/Resources/Biomes/Cliffs (Meshes/, Textures/).
Axes: Blender +Z up. Sections run along X, centred on the origin, and face -Y (the
road). The cap has its origin at the tall end. The game bends each piece along the
road's cliff line (it measures which way a mesh runs and which side is the face), so
runs follow bends without gaps.
"""
import bpy  # noqa: must load before bmesh and mathutils
import bmesh
import math
import os
import random

import numpy as np
from mathutils import Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_cliffs"))
os.makedirs(OUT, exist_ok=True)
TEX = int(os.environ.get("RR_TEX", "1536"))   # atlas is 2 x TEX square
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


ATLAS_HEIGHT = 100.0  # m of profile the atlas covers vertically: face, crest, top and back slope


def terrace(v, steps):
    """Soft quantisation: flat terraces joined by short ramps. Hard rounding made
    vertical jumps that folded faces over and rendered as black shards."""
    q = v * steps
    base = math.floor(q)
    f = q - base
    f = min(1.0, max(0.0, (f - 0.35) / 0.3))
    return (base + f * f * (3 - 2 * f)) / steps


END_HEIGHT = 24.0     # crest height where sections join - identical on every section
STRATA = 1.7          # m per layer, shared so strata line up across joins
JOIN_SEED = Vector((31.0, -17.0, 0.0))
JOIN_BLEND = 3.0      # m over which a section's own relief fades into the join profile
CAP_LENGTH = 16.0
SLOTS = 4             # atlas columns: three wall sections and the end cap


def relief(x, z, seed):
    """How far the face is pushed back into the hillside at (x, z): strata ledges,
    fractured blocks, vertical joints and surface bumps."""
    layer = math.floor(z / STRATA)
    frac = z / STRATA - layer
    ledge = fbm(Vector((layer * 3.1, x * 0.03, 0)) + seed, 2)
    step = 1.6 * ledge + 0.7 * (frac ** 3)
    blocks = terrace(fbm(Vector((x * 0.11, z * 0.15, 7)) + seed, 3), 4) * 2.4
    joints = terrace(fbm(Vector((x * 0.3, 0, z * 0.04 + 11)) + seed, 2), 3) * 1.1
    bumps = 0.55 * fbm(Vector((x * 0.22, z * 0.3, 0)) + seed, 4)
    fine = 0.14 * fbm(Vector((x * 0.9, z * 1.2, 5)) + seed, 3)
    return step + blocks + joints + bumps + fine


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def build_cliff(name, slot, kind, height=END_HEIGHT, rows=96):
    """kind "mid": a 24 m wall section. Both ends have exactly the same profile (the
    join profile: END_HEIGHT tall, relief sampled at a fixed x with a shared seed), so
    any section butts against any other without a seam or a step, and a run reads as
    one continuous wall. The old sections tapered to the ground at both ends and a
    run of them read as a row of separate spikes.

    kind "cap_r"/"cap_l": a 16 m piece that starts with the join profile at x = 0 (the
    mesh origin - the game uses it to tell which end is which) and slopes down to the
    ground towards +x / -x, closing a run off."""
    bm = bmesh.new()
    seed = Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), 0))
    length = 24.0 if kind == "mid" else CAP_LENGTH
    cols = int(length * 4.7)
    grid, arc = [], []
    for c in range(cols + 1):
        s = c / cols
        if kind == "mid":
            x = (s - 0.5) * length
            join_w = smooth(min(s * length, (1 - s) * length) / JOIN_BLEND)
            rise = math.sin(math.pi * s)
            crest = END_HEIGHT + (height * (0.85 + 0.3 * fbm(Vector((x * 0.07, 0, 3)) + seed, 3))
                                  - END_HEIGHT) * rise * join_w
            taper = 1.0
        else:
            x = s * length
            join_w = smooth(x / JOIN_BLEND)
            taper = 1.0 - smooth(s / 0.95)
            wobble = 1.0 + 0.2 * fbm(Vector((x * 0.09, 0, 13)) + seed, 3) * join_w
            crest = END_HEIGHT * taper * wobble

        def joined(fn):
            return fn(0.0, JOIN_SEED) * (1 - join_w) + fn(x, seed) * join_w

        # The face, then the crest rolling over, a short top, and a back slope down to
        # the ground: a closed hill rather than a sheet. A sheet showed its edge as a
        # thin spike whenever a bend put the camera off to one side of it.
        profile = []
        for r in range(rows + 1):
            t = r / rows
            z = -0.8 + (crest + 0.8) * t
            y = 0.2 * z - joined(lambda xx, sd: relief(xx, z, sd)) * taper
            profile.append((y, z))
        y0, z0 = profile[-1]
        top = crest + 2.5 * taper
        for k in range(1, 9):                                   # roll over the crest
            o = k / 8
            profile.append((y0 + 9.0 * o * max(taper, 0.05),
                            z0 + (top - z0) * math.sin(o * math.pi / 2)))
        y1 = profile[-1][0]
        for k in range(1, 7):                                   # top
            o = k / 6
            lump = joined(lambda xx, sd: fbm(Vector((xx * 0.12, o * 2.0, 21)) + sd, 3))
            profile.append((y1 + 8.0 * o * max(taper, 0.05), top + 1.2 * lump * taper))
        y2, z2 = profile[-1]
        for k in range(1, 15):                                  # back slope
            o = k / 14
            lump = joined(lambda xx, sd: fbm(Vector((xx * 0.1, o * 3.0, 27)) + sd, 3))
            z = z2 + (-0.8 - z2) * smooth(o) + 1.5 * lump * math.sin(o * math.pi) * taper
            profile.append((y2 + 18.0 * o * max(taper, 0.05), z))
        column, run, prev = [], [], None
        for (y, z) in profile:
            run.append(0.0 if prev is None else run[-1] + math.dist(prev, (y, z)))
            prev = (y, z)
            px = -x if kind == "cap_l" else x
            column.append(bm.verts.new((px, y, z)))
        grid.append(column)
        arc.append(run)
    rows = len(grid[0]) - 1
    grid = [[grid[c][r] for c in range(cols + 1)] for r in range(rows + 1)]
    arc = [[arc[c][r] for c in range(cols + 1)] for r in range(rows + 1)]
    for r in range(rows):
        for c in range(cols):
            quad = (grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c])
            bm.faces.new(tuple(reversed(quad)) if kind == "cap_l" else quad)
    bm.verts.index_update()
    arc_of = {grid[r][c].index: arc[r][c] for r in range(rows + 1) for c in range(cols + 1)}
    ob = to_object(name, bm)
    # UVs: a front projection, one island per piece, side by side in the atlas (both
    # caps share a column). Automatic unwrapping cut this faceted surface into
    # thousands of tiny islands whose edges sampled the atlas background and showed
    # as black shards.
    # v runs along the profile (arc length), so the top and back are not smeared.
    uv = ob.data.uv_layers.new()
    for loop in ob.data.loops:
        co = ob.data.vertices[loop.vertex_index].co
        f = co.x / length + 0.5 if kind == "mid" else abs(co.x) / length
        u = (slot + 0.02 + 0.96 * f) / SLOTS
        v = 0.01 + 0.98 * min(1.0, arc_of[loop.vertex_index] / ATLAS_HEIGHT)
        uv.data[loop.index].uv = (u, v)
    # Smooth shaded, so vertices are shared: the game bends every piece to the road
    # at run time, and flat facets split each triangle's corners into its own
    # vertices - six times the vertices to bend and keep in memory. The baked normal
    # map carries the angular detail.
    for p in ob.data.polygons:
        p.use_smooth = True
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
    m = bpy.data.materials.new("M_cliffs")
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    geo = node(nt, "ShaderNodeNewGeometry")
    pos = geo.outputs["Position"]

    mottle = node(nt, "ShaderNodeTexNoise", Scale=0.35, Detail=8.0, Roughness=0.6)
    nt.links.new(pos, mottle.inputs["Vector"])
    grain = node(nt, "ShaderNodeTexNoise", Scale=8.0, Detail=6.0, Roughness=0.7)
    nt.links.new(pos, grain.inputs["Vector"])
    # Sparse fractures: thin lines where a large-scale noise crosses 0.5. (A Voronoi
    # edge network read as crazy paving.)
    frac_n = node(nt, "ShaderNodeTexNoise", Scale=0.14, Detail=3.0, Roughness=0.5)
    nt.links.new(pos, frac_n.inputs["Vector"])
    centred = node(nt, "ShaderNodeMath", operation="SUBTRACT")
    centred.inputs[1].default_value = 0.5
    nt.links.new(frac_n.outputs["Fac"], centred.inputs[0])
    dist = node(nt, "ShaderNodeMath", operation="ABSOLUTE")
    nt.links.new(centred.outputs[0], dist.inputs[0])
    crack_mask = node(nt, "ShaderNodeMapRange")
    crack_mask.inputs["From Min"].default_value = 0.0
    crack_mask.inputs["From Max"].default_value = 0.0025
    crack_mask.inputs["To Min"].default_value = 1.0
    crack_mask.inputs["To Max"].default_value = 0.0
    nt.links.new(dist.outputs[0], crack_mask.inputs["Value"])
    # Horizontal strata banding.
    sep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(pos, sep.inputs[0])
    bands = node(nt, "ShaderNodeTexWave", Scale=0.4, Distortion=3.0, Detail=3.0, bands_direction="Z")
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
    crack_k.inputs[1].default_value = 0.4
    nt.links.new(crack_mask.outputs["Result"], crack_k.inputs[0])
    nt.links.new(crack_k.outputs[0], crack_col.inputs[0])
    nt.links.new(col.outputs[2], crack_col.inputs[6])

    # Moss and needle litter cover everything facing up - the crest, the top and
    # the gentle back slope are forest floor, not bare stone. (A patchy dusting here
    # left the tops pale, and from the road they read as a bare slab.)
    nsep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], nsep.inputs[0])
    up = node(nt, "ShaderNodeMapRange")
    up.inputs["From Min"].default_value = 0.35
    up.inputs["From Max"].default_value = 0.7
    nt.links.new(nsep.outputs["Z"], up.inputs["Value"])
    dirt_noise = node(nt, "ShaderNodeTexNoise", Scale=1.5, Detail=5.0)
    nt.links.new(pos, dirt_noise.inputs["Vector"])
    dirt_k = node(nt, "ShaderNodeMath", operation="MULTIPLY")
    nt.links.new(up.outputs["Result"], dirt_k.inputs[0])
    nt.links.new(dirt_noise.outputs["Fac"], dirt_k.inputs[1])
    dirt_k2 = node(nt, "ShaderNodeMath", operation="MULTIPLY", use_clamp=True)
    dirt_k2.inputs[1].default_value = 3.0
    nt.links.new(dirt_k.outputs[0], dirt_k2.inputs[0])
    dirt = node(nt, "ShaderNodeMix", data_type="RGBA")
    dirt.inputs[7].default_value = (0.05, 0.055, 0.025, 1)   # moss and needle litter
    nt.links.new(dirt_k2.outputs[0], dirt.inputs[0])
    nt.links.new(crack_col.outputs[2], dirt.inputs[6])
    # Water staining: dark streaks running down the faces.
    streak_map = node(nt, "ShaderNodeMapping")
    streak_map.inputs["Scale"].default_value = (2.0, 2.0, 0.12)
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
    bump = node(nt, "ShaderNodeBump", Strength=0.55, Distance=0.05)
    nt.links.new(h.outputs[0], bump.inputs["Height"])
    bump2 = node(nt, "ShaderNodeBump", Strength=0.35, Distance=0.25)
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

    faces = [build_cliff(f"SM_cliff_{i + 1:02d}", i, "mid", h) for i, h in enumerate((22.0, 30.0, 36.0))]
    # One cap: the game bends it into place at either end of a run, mirroring it
    # as needed. (The preview uses a mirrored copy.)
    caps = [build_cliff("SM_cliff_end", 3, "cap_r"), build_cliff("preview_cap_l", 3, "cap_l")]
    objs = faces + caps
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
    # UVs are set per cliff in build_cliff (front projection).
    bpy.ops.object.mode_set(mode="OBJECT")

    def new_img(name, non_color):
        img = bpy.data.images.new(name, TEX * 2, TEX * 2, alpha=False, float_buffer=True)
        if non_color:
            img.colorspace_settings.name = "Non-Color"
        return img

    # The two caps are mirror images sharing one atlas column: bake the right one.
    bakes = faces + caps[:1]
    d_img = new_img("d", False)
    bake(bakes, mat, d_img, "DIFFUSE")
    n_img = new_img("n", True)
    bake(bakes, mat, n_img, "NORMAL")
    r_img = new_img("r", True)
    bake(bakes, mat, r_img, "ROUGHNESS")
    ao_img = new_img("ao", True)
    bake(bakes, mat, ao_img, "AO", samples=64)

    d = pixels(d_img)
    d[..., :3] = np.where(d[..., :3] <= 0.0031308, d[..., :3] * 12.92,
                          1.055 * np.power(np.clip(d[..., :3], 0, None), 1 / 2.4) - 0.055)
    d[..., 3] = 1
    save_png("T_cliffs_D", d, "sRGB", fmt="JPEG")
    n = pixels(n_img)
    n[..., 3] = 1
    save_png("T_cliffs_N", n, "Non-Color", fmt="JPEG")
    mso = np.zeros_like(d)
    mso[..., 1] = pixels(ao_img)[..., 0]
    mso[..., 3] = 1 - pixels(r_img)[..., 0]
    half = mso.reshape(TEX, 2, TEX, 2, 4).mean(axis=(1, 3))
    save_png("T_cliffs_MSO", half, "Non-Color")

    # Swap to the baked material (what Unity will show) and export each mesh.
    baked = bpy.data.materials.new("M_cliffs_baked")
    nt = baked.node_tree
    b = nt.nodes["Principled BSDF"]
    t_d = node(nt, "ShaderNodeTexImage")
    t_d.image = bpy.data.images.load(os.path.join(OUT, "T_cliffs_D.jpg"))
    t_n = node(nt, "ShaderNodeTexImage")
    t_n.image = bpy.data.images.load(os.path.join(OUT, "T_cliffs_N.jpg"))
    t_n.image.colorspace_settings.name = "Non-Color"
    t_m = node(nt, "ShaderNodeTexImage")
    t_m.image = bpy.data.images.load(os.path.join(OUT, "T_cliffs_MSO.png"))
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

    for o in faces + caps[:1]:
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, o.name + ".fbx"), use_selection=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="OFF", use_tspace=True,
                                 path_mode="STRIP", add_leaf_bones=False)
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        print(f"RR_CLIFF {o.name} tris={tris} size={tuple(round(v, 2) for v in o.dimensions)}")

    # Preview: the three sections in a run beside a road.
    # Laid out the way the game does it: cap, three sections end to end, cap.
    caps[1].location = (-36.0, 4.0, 0)
    for k, f in enumerate(faces):
        f.location = (k * 24.0 - 24.0, 4.0, 0)
    caps[0].location = (36.0, 4.0, 0)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, -3, 0.01))
    road = bpy.context.active_object
    road.scale = (220, 8, 1)
    rm = bpy.data.materials.new("road")
    rm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.07, 0.07, 0.072, 1)
    road.data.materials.append(rm)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 20, 0))
    ground = bpy.context.active_object
    ground.scale = (220, 46, 1)
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
    cam.data.lens = 18
    cam.location = (-80.0, -5.0, 3.0)                   # a chase camera, down the road

    def look_at(target):
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()

    look_at((0.0, 0.0, 6.0))
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)
    # And from the far side of the road, looking straight at the run.
    cam.location = (10.0, -40.0, 3.0)
    look_at((0.0, 4.0, 14.0))
    scene.render.filepath = os.path.join(OUT, "preview_side.png")
    bpy.ops.render.render(write_still=True)
    print("RR_DONE")


main()
