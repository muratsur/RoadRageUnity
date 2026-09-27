"""Bakes a tileable forest-floor texture set in headless Blender.

The floor is modelled rather than painted: dark soil under thousands of individual
fallen leaves, pine needles, twigs and stones, rendered straight down with an
orthographic camera. Anything near a tile edge is repeated on the opposite edge,
so the result tiles without seams.

Runs without the Blender app, through Blender's Python module (needs Python 3.11):

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1
    .venv/bin/python Tools/Blender/build_forest_floor.py

Output (./out_floor next to this script, or $RR_OUT):
  T_forest_floor_D.png    base colour (sRGB)
  T_forest_floor_N.png    tangent-space normal, OpenGL (+Y), Unity's convention
  T_forest_floor_MSO.png  R=metallic G=occlusion A=smoothness, like the biome kits
  preview.png             the tile repeated 3x3, to check for seams
The game loads the three textures from Assets/Resources/Biomes/ForestFloor/Textures.
"""
import bpy  # noqa: must load before bmesh
import bmesh
import math
import os
import random

import numpy as np
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_floor"))
os.makedirs(OUT, exist_ok=True)
RES = int(os.environ.get("RR_RES", "1024"))
TILE = 2.0          # metres covered by one texture repeat
MARGIN = 0.3        # items this close to an edge are repeated across it
SEED = 7

rng = random.Random(SEED)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.render.resolution_x = RES
    scene.render.resolution_y = RES
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.display_settings.display_device = "sRGB"
    return scene


def node(nt, kind, **values):
    n = nt.nodes.new(kind)
    for k, v in values.items():
        if k in n.inputs:
            n.inputs[k].default_value = v
        else:
            setattr(n, k, v)
    return n


# ---------------------------------------------------------------- materials
#
# Every material exposes its albedo and roughness on two named reroute nodes, so the
# pass renders can route either (or occlusion / normals) into an emission shader.

def finish_material(m, albedo_socket, rough_socket, normal_socket=None):
    nt = m.node_tree
    a = node(nt, "NodeReroute", label="albedo")
    a.name = "albedo"
    nt.links.new(albedo_socket, a.inputs[0])
    r = node(nt, "NodeReroute", label="rough")
    r.name = "rough"
    nt.links.new(rough_socket, r.inputs[0])
    if normal_socket is not None:
        nrm = node(nt, "NodeReroute", label="normal")
        nrm.name = "normal"
        nt.links.new(normal_socket, nrm.inputs[0])
    return m


def item_random(nt):
    """0..1 per scattered item, read from its "seed" property so the copies of an item
    placed across a tile edge get the same value and the seam matches."""
    attr = node(nt, "ShaderNodeAttribute", attribute_type="OBJECT", attribute_name="seed")
    s = node(nt, "ShaderNodeMath", operation="SINE")
    nt.links.new(attr.outputs["Fac"], s.inputs[0])
    m = node(nt, "ShaderNodeMath", operation="MULTIPLY")
    m.inputs[1].default_value = 43758.5453
    nt.links.new(s.outputs[0], m.inputs[0])
    f = node(nt, "ShaderNodeMath", operation="FRACT")
    nt.links.new(m.outputs[0], f.inputs[0])
    return f.outputs[0]


def tile_noise(nt, scale, detail=6.0, roughness=0.6, w_offset=0.0):
    """Noise that repeats exactly every TILE metres: position mapped onto a 4D torus."""
    coord = node(nt, "ShaderNodeTexCoord")
    sep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(coord.outputs["Object"], sep.inputs[0])
    parts = []
    for axis in ("X", "Y"):
        ang = node(nt, "ShaderNodeMath", operation="MULTIPLY")
        ang.inputs[1].default_value = 2 * math.pi / TILE
        nt.links.new(sep.outputs[axis], ang.inputs[0])
        for op in ("COSINE", "SINE"):
            f = node(nt, "ShaderNodeMath", operation=op)
            nt.links.new(ang.outputs[0], f.inputs[0])
            s = node(nt, "ShaderNodeMath", operation="MULTIPLY")
            s.inputs[1].default_value = scale
            nt.links.new(f.outputs[0], s.inputs[0])
            parts.append(s)
    comb = node(nt, "ShaderNodeCombineXYZ")
    nt.links.new(parts[0].outputs[0], comb.inputs[0])
    nt.links.new(parts[1].outputs[0], comb.inputs[1])
    nt.links.new(parts[2].outputs[0], comb.inputs[2])
    w = node(nt, "ShaderNodeMath", operation="ADD")
    w.inputs[1].default_value = w_offset
    nt.links.new(parts[3].outputs[0], w.inputs[0])
    noise = node(nt, "ShaderNodeTexNoise", Detail=detail, Roughness=roughness)
    noise.noise_dimensions = "4D"
    nt.links.new(comb.outputs[0], noise.inputs["Vector"])
    nt.links.new(w.outputs[0], noise.inputs["W"])
    return noise


def soil_material():
    m = bpy.data.materials.new("soil")
    nt = m.node_tree
    big = tile_noise(nt, 1.6, detail=8.0)
    fine = tile_noise(nt, 14.0, detail=4.0, w_offset=3.0)
    mix = node(nt, "ShaderNodeMath", operation="MULTIPLY_ADD")
    mix.inputs[1].default_value = 0.35
    nt.links.new(fine.outputs["Fac"], mix.inputs[0])
    nt.links.new(big.outputs["Fac"], mix.inputs[2])
    ramp = node(nt, "ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.035, 0.024, 0.016, 1)
    ramp.color_ramp.elements[1].position = 0.95
    ramp.color_ramp.elements[1].color = (0.13, 0.09, 0.055, 1)
    nt.links.new(mix.outputs[0], ramp.inputs["Fac"])
    rough = node(nt, "ShaderNodeValue")
    rough.outputs[0].default_value = 0.95
    return finish_material(m, ramp.outputs["Color"], rough.outputs[0])


LEAF_COLOURS = [
    # Decayed browns and greys dominate a real forest floor; only a few leaves keep
    # an orange cast. None are green.
    (0.17, 0.10, 0.05), (0.24, 0.15, 0.08), (0.32, 0.20, 0.10), (0.38, 0.28, 0.16),
    (0.13, 0.09, 0.06), (0.22, 0.17, 0.12), (0.29, 0.23, 0.16), (0.10, 0.075, 0.055),
]


def leaf_material(i, base):
    m = bpy.data.materials.new(f"leaf_{i}")
    nt = m.node_tree
    # Per-leaf mottling and a darker, decayed edge, from the leaf's own UVs.
    coord = node(nt, "ShaderNodeTexCoord")
    spots = node(nt, "ShaderNodeTexNoise", Scale=9.0, Detail=5.0)
    nt.links.new(coord.outputs["Generated"], spots.inputs["Vector"])
    shade = node(nt, "ShaderNodeMath", operation="MULTIPLY_ADD")
    shade.inputs[1].default_value = 0.55
    shade.inputs[2].default_value = 0.72
    nt.links.new(item_random(nt), shade.inputs[0])
    spot = node(nt, "ShaderNodeMapRange")
    spot.inputs["To Min"].default_value = 0.7
    spot.inputs["To Max"].default_value = 1.15
    nt.links.new(spots.outputs["Fac"], spot.inputs["Value"])
    k = node(nt, "ShaderNodeMath", operation="MULTIPLY")
    nt.links.new(shade.outputs[0], k.inputs[0])
    nt.links.new(spot.outputs["Result"], k.inputs[1])
    col = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    col.inputs[0].default_value = 1.0
    col.inputs[6].default_value = (*base, 1)
    comb = node(nt, "ShaderNodeCombineColor")
    for c in range(3):
        nt.links.new(k.outputs[0], comb.inputs[c])
    nt.links.new(comb.outputs[0], col.inputs[7])
    rough = node(nt, "ShaderNodeValue")
    rough.outputs[0].default_value = 0.78
    return finish_material(m, col.outputs[2], rough.outputs[0])


def flat_material(name, colour, rough_value, jitter=0.25):
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    k = node(nt, "ShaderNodeMath", operation="MULTIPLY_ADD")
    k.inputs[1].default_value = 2 * jitter
    k.inputs[2].default_value = 1 - jitter
    nt.links.new(item_random(nt), k.inputs[0])
    col = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    col.inputs[0].default_value = 1.0
    col.inputs[6].default_value = (*colour, 1)
    comb = node(nt, "ShaderNodeCombineColor")
    for c in range(3):
        nt.links.new(k.outputs[0], comb.inputs[c])
    nt.links.new(comb.outputs[0], col.inputs[7])
    rough = node(nt, "ShaderNodeValue")
    rough.outputs[0].default_value = rough_value
    return finish_material(m, col.outputs[2], rough.outputs[0])


# ---------------------------------------------------------------- meshes

def leaf_mesh(name, length, width, lobes=0):
    """Pointed leaf outline, fan-filled, cupped slightly and bent along its length."""
    bm = bmesh.new()
    n = 18
    outline = []
    for i in range(n + 1):
        t = i / n                       # 0 = stem, 1 = tip
        w = width * math.sin(math.pi * t) ** 0.8 * (1 - 0.35 * t)
        if lobes:
            w *= 1 + 0.18 * math.sin(t * math.pi * 2 * lobes)
        outline.append((t, w))
    right = [bm.verts.new((length * t - length / 2, w / 2, 0)) for t, w in outline]
    left = [bm.verts.new((length * t - length / 2, -w / 2, 0)) for t, w in outline[1:-1]]
    ring = right + left[::-1]
    centre = bm.verts.new((0, 0, 0))
    for i in range(len(ring)):
        bm.faces.new((centre, ring[i], ring[(i + 1) % len(ring)]))
    for v in bm.verts:
        x, y = v.co.x / length, v.co.y / max(width, 1e-4)
        v.co.z = 0.18 * width * (2 * y) ** 2 + 0.12 * length * (x ** 2)   # cup + curl
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    uv = me.uv_layers.new()
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (co.x / length + 0.5, co.y / max(width, 1e-4) + 0.5)
    for p in me.polygons:
        p.use_smooth = True
    return me


def box_mesh(name, sx, sy, sz):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Diagonal((sx, sy, sz, 1)))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return me


def twig_mesh(name, length, radius):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=radius, radius2=radius * 0.55,
                          depth=length, matrix=Matrix.Rotation(math.radians(90), 4, "Y"))
    for v in bm.verts:
        v.co.z += 0.015 * math.sin(v.co.x * 9.0)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return me


def stone_mesh(name, size):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=size)
    for v in bm.verts:
        v.co *= 0.75 + 0.5 * rng.random()
        v.co.z *= 0.45
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return me


def place(me, mat, x, y, z, rot_z, tilt=0.0, scale=1.0):
    """Adds the instance, plus copies across any tile edge it is near."""
    offsets = [(0.0, 0.0)]
    near_x = [-TILE] if x > TILE - MARGIN else ([TILE] if x < MARGIN else [])
    near_y = [-TILE] if y > TILE - MARGIN else ([TILE] if y < MARGIN else [])
    for dx in near_x:
        offsets.append((dx, 0.0))
    for dy in near_y:
        offsets.append((0.0, dy))
    for dx in near_x:
        for dy in near_y:
            offsets.append((dx, dy))
    seed_rot = (rng.uniform(-tilt, tilt), rng.uniform(-tilt, tilt))
    seed = rng.uniform(0, 1000)
    if not me.materials:
        me.materials.append(None)
    for dx, dy in offsets:
        ob = bpy.data.objects.new(me.name, me)
        ob.location = (x + dx, y + dy, z)
        ob.rotation_euler = (seed_rot[0], seed_rot[1], rot_z)
        ob.scale = (scale, scale, scale)
        bpy.context.scene.collection.objects.link(ob)
        ob.material_slots[0].link = "OBJECT"
        ob.material_slots[0].material = mat
        ob["seed"] = seed


def build_floor():
    soil = soil_material()
    bpy.ops.mesh.primitive_plane_add(size=TILE * 3, location=(TILE / 2, TILE / 2, 0))
    plane = bpy.context.active_object
    plane.data.materials.append(soil)

    leaf_mats = [leaf_material(i, c) for i, c in enumerate(LEAF_COLOURS)]
    shapes = [leaf_mesh("oak", 0.075, 0.042, lobes=3), leaf_mesh("beech", 0.065, 0.036),
              leaf_mesh("birch", 0.045, 0.030), leaf_mesh("willow", 0.085, 0.018),
              leaf_mesh("broad", 0.095, 0.060, lobes=2)]
    needle = box_mesh("needle", 0.05, 0.0012, 0.0012)
    twigs = [twig_mesh(f"twig{i}", rng.uniform(0.12, 0.45), rng.uniform(0.003, 0.009)) for i in range(6)]
    stones = [stone_mesh(f"stone{i}", rng.uniform(0.008, 0.03)) for i in range(5)]
    needle_mat = flat_material("needle", (0.30, 0.17, 0.07), 0.8, 0.3)
    bark_mat = flat_material("bark", (0.16, 0.10, 0.06), 0.9, 0.3)
    stone_mat = flat_material("stone", (0.20, 0.19, 0.17), 0.85, 0.25)

    # Leaves in drifts: density follows a coarse random field, so some soil shows
    # through between heaps instead of an even carpet.
    drifts = [(rng.uniform(0, TILE), rng.uniform(0, TILE), rng.uniform(0.25, 0.7)) for _ in range(9)]

    def drift_density(x, y):
        d = 0.35
        for cx, cy, r in drifts:
            for ox in (-TILE, 0, TILE):
                for oy in (-TILE, 0, TILE):
                    dist = math.hypot(x - cx - ox, y - cy - oy)
                    d += 0.9 * math.exp(-(dist / r) ** 2)
        return min(1.0, d)

    placed = 0
    layer = 0.0
    while placed < 3400:
        x, y = rng.uniform(0, TILE), rng.uniform(0, TILE)
        if rng.random() > drift_density(x, y):
            continue
        layer += 0.0000045
        place(rng.choice(shapes), rng.choice(leaf_mats), x, y, 0.002 + layer,
              rng.uniform(0, 2 * math.pi), tilt=0.35, scale=rng.uniform(0.75, 1.3))
        placed += 1
    for _ in range(1600):
        place(needle, needle_mat, rng.uniform(0, TILE), rng.uniform(0, TILE),
              0.0015 + rng.uniform(0, 0.012), rng.uniform(0, 2 * math.pi), tilt=0.1,
              scale=rng.uniform(0.7, 1.2))
    for _ in range(26):
        place(rng.choice(twigs), bark_mat, rng.uniform(0, TILE), rng.uniform(0, TILE),
              0.012, rng.uniform(0, 2 * math.pi), tilt=0.05)
    for _ in range(40):
        place(rng.choice(stones), stone_mat, rng.uniform(0, TILE), rng.uniform(0, TILE),
              0.003, rng.uniform(0, 2 * math.pi))


# ---------------------------------------------------------------- pass renders

def route(pass_name):
    """Point every material's output at an emission of the wanted channel."""
    for m in bpy.data.materials:
        nt = m.node_tree
        out = nt.nodes.get("Material Output")
        for n in [n for n in nt.nodes if n.name.startswith("pass_")]:
            nt.nodes.remove(n)
        emit = node(nt, "ShaderNodeEmission")
        emit.name = "pass_emit"
        if pass_name == "albedo":
            nt.links.new(nt.nodes["albedo"].outputs[0], emit.inputs["Color"])
        elif pass_name == "rough":
            nt.links.new(nt.nodes["rough"].outputs[0], emit.inputs["Color"])
        elif pass_name == "ao":
            ao = node(nt, "ShaderNodeAmbientOcclusion", samples=16, Distance=0.04)
            ao.name = "pass_ao"
            nt.links.new(ao.outputs["AO"], emit.inputs["Color"])
        elif pass_name == "normal":
            geo = node(nt, "ShaderNodeNewGeometry")
            geo.name = "pass_geo"
            half = node(nt, "ShaderNodeVectorMath", operation="MULTIPLY_ADD")
            half.name = "pass_half"
            half.inputs[1].default_value = (0.5, 0.5, 0.5)
            half.inputs[2].default_value = (0.5, 0.5, 0.5)
            nt.links.new(geo.outputs["Normal"], half.inputs[0])
            nt.links.new(half.outputs["Vector"], emit.inputs["Color"])
        nt.links.new(emit.outputs[0], out.inputs["Surface"])


def render(pass_name, samples):
    scene = bpy.context.scene
    route(pass_name)
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    # Data passes must not go through the sRGB display transform.
    scene.view_settings.view_transform = "Standard" if pass_name == "albedo" else "Raw"
    path = os.path.join(OUT, f"_{pass_name}.png")
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_depth = "16"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = "sRGB" if pass_name == "albedo" else "Non-Color"
    a = np.empty(RES * RES * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(RES, RES, 4)


def save(name, arr, colorspace):
    img = bpy.data.images.new(name, RES, RES, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()


def main():
    scene = reset()
    build_floor()
    world = bpy.data.worlds.new("black")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0

    cam_data = bpy.data.cameras.new("top")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = TILE
    cam = bpy.data.objects.new("top", cam_data)
    cam.location = (TILE / 2, TILE / 2, 5.0)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.clip_end = 20.0

    albedo = render("albedo", 16)
    rough = render("rough", 8)
    normal = render("normal", 16)
    ao = render("ao", 32)

    albedo[..., 3] = 1.0
    save("T_forest_floor_D", albedo, "sRGB")
    n = normal.copy()
    # Renormalise after anti-aliasing blended neighbouring normals together.
    v = n[..., :3] * 2 - 1
    v /= np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-4)
    n[..., :3] = v * 0.5 + 0.5
    n[..., 3] = 1.0
    save("T_forest_floor_N", n, "Non-Color")
    mso = np.zeros_like(albedo)
    mso[..., 1] = ao[..., 0]
    mso[..., 3] = 1.0 - rough[..., 0]
    save("T_forest_floor_MSO", mso, "Non-Color")
    for p in ("albedo", "rough", "normal", "ao"):
        os.remove(os.path.join(OUT, f"_{p}.png"))

    # 3x3 repeat of the colour map, shaded a little by occlusion, to check seams.
    shaded = albedo[..., :3] * (0.55 + 0.45 * ao[..., :1])
    grid = np.tile(shaded, (3, 3, 1))
    small = grid[::3, ::3]
    prev = np.concatenate([small, np.ones((*small.shape[:2], 1), np.float32)], axis=-1)
    img = bpy.data.images.new("preview", prev.shape[1], prev.shape[0])
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(prev.ravel())
    img.filepath_raw = os.path.join(OUT, "preview.png")
    img.file_format = "PNG"
    img.save()
    print("RR_DONE")


main()
