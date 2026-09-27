"""Builds Greenwood's horizon: a closed ring of mountains around the player.

The ring is a heightfield in polar coordinates (angle around the player, distance out)
that the game keeps centred on the camera, so the range always stands at the same
distance. It opens into a valley ahead and behind, so the road runs along a valley
floor rather than into a wall.

Everything is generated here with numpy at texture resolution:
  height   ridged multifractal noise (sharp ridgelines, eroded spurs), shaped by a
           radial profile (foothills rising to a main crest, then falling away) and the
           valley opening
  colour   dark conifer forest on gentle slopes below the tree line, bare rock on steep
           faces and above it, scree in gullies, a dusting of snow on the highest peaks
  normal   tangent-space detail from the part of the height the mesh is too coarse for
The mesh is sampled from the same height at a lower resolution. Blender's Python
module (bpy) only builds and exports it.

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1 pillow
    .venv/bin/python Tools/Terrain/build_mountain_ring.py

Output (./out next to this script, or $RR_OUT):
  SM_mountain_ring.fbx, T_mountain_ring_D.jpg, T_mountain_ring_N.jpg, preview.png
  (the normal map is half size and JPG: seen from 400 m and more, and kept out of
  LFS, so size matters)
The game loads them from Assets/Resources/Biomes/Mountains.
Axes: Blender +Z up; the valley runs along +/-Y (Unity forward/back after export);
the game measures the mesh and turns the valley onto the road.
"""
import bpy  # noqa: must load before anything that touches bpy data
import math
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out"))
os.makedirs(OUT, exist_ok=True)
SEED = int(os.environ.get("RR_SEED", "3"))

R_MIN, R_MAX = 420.0, 1000.0        # m from the player
TEX_W, TEX_H = 4096, 1024           # around x outwards
MESH_W, MESH_H = 480, 48            # mesh vertices around x outwards
PEAK = 380.0                        # m, tallest possible summit
TREE_LINE = 280.0                   # m above the base
SNOW_LINE = 300.0

rng = np.random.default_rng(SEED)


# ---------------------------------------------------------------- noise

class Noise2:
    """Value noise over (angle, radius): periodic around the ring, so the texture and
    the mesh close without a seam."""

    def __init__(self, seed):
        self.r = np.random.default_rng(seed)
        self.cache = {}

    def grid(self, cells_a, cells_r):
        key = (cells_a, cells_r)
        if key not in self.cache:
            self.cache[key] = self.r.random((cells_r + 2, cells_a)).astype(np.float32)
        return self.cache[key]

    def __call__(self, a01, r01, cells_a, cells_r):
        g = self.grid(cells_a, cells_r)
        x = a01 * cells_a
        y = r01 * cells_r
        xi = np.floor(x).astype(np.int64)
        yi = np.floor(y).astype(np.int64)
        fx = x - xi
        fy = y - yi
        fx = fx * fx * (3 - 2 * fx)
        fy = fy * fy * (3 - 2 * fy)
        x0, x1 = xi % cells_a, (xi + 1) % cells_a
        y0 = np.clip(yi, 0, cells_r + 1)
        y1 = np.clip(yi + 1, 0, cells_r + 1)
        a = g[y0, x0] + (g[y0, x1] - g[y0, x0]) * fx
        b = g[y1, x0] + (g[y1, x1] - g[y1, x0]) * fx
        return a + (b - a) * fy


def ridged(noise, a01, r01, base_cells_a, octaves=8, gain=0.5):
    """Ridged multifractal: sharp crests where the noise crosses its midpoint, with
    each octave weighted by the previous one so detail gathers on the ridges."""
    circumference = 2 * math.pi * (R_MIN + R_MAX) / 2
    radial = R_MAX - R_MIN
    total = np.zeros_like(a01, dtype=np.float32)
    weight = np.ones_like(a01, dtype=np.float32)
    amp, norm = 1.0, 0.0
    cells_a = base_cells_a
    for _ in range(octaves):
        cells_r = max(1, int(round(cells_a * radial / circumference)))
        n = noise(a01, r01, cells_a, cells_r)
        ridge = (1.0 - np.abs(n * 2 - 1)) ** 2
        ridge *= weight
        weight = np.clip(ridge * 1.6, 0, 1)
        total += ridge * amp
        norm += amp
        amp *= gain
        cells_a *= 2
    return total / norm


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def blur(a, k):
    """Box blur x3, periodic around the ring (axis 1), clamped outwards (axis 0)."""
    out = a.astype(np.float32)
    for _ in range(3):
        c = np.cumsum(np.pad(out, ((0, 0), (k, k)), mode="wrap"), axis=1)
        out = ((c[:, 2 * k:] - c[:, :-2 * k]) / (2 * k))[:, :a.shape[1]]
        c = np.cumsum(np.pad(out, ((k, k), (0, 0)), mode="edge"), axis=0)
        out = ((c[2 * k:] - c[:-2 * k]) / (2 * k))[:a.shape[0]]
    return out


# ---------------------------------------------------------------- terrain

def height_field(w, h):
    a01 = ((np.arange(w, dtype=np.float32) + 0.5) / w)[None, :].repeat(h, 0)
    r01 = ((np.arange(h, dtype=np.float32) + 0.5) / h)[:, None].repeat(w, 1)
    angle = a01 * 2 * math.pi
    radius = R_MIN + r01 * (R_MAX - R_MIN)

    main = ridged(Noise2(SEED), a01, r01, 14)
    broad = ridged(Noise2(SEED + 1), a01, r01, 5, octaves=4)
    # Radial profile: foothills from the inner edge up to a main crest ~60% out,
    # then falling away behind it.
    t = (radius - R_MIN) / (R_MAX - R_MIN)
    profile = smoothstep(0.0, 0.55, t) * (1.0 - 0.45 * smoothstep(0.7, 1.0, t))
    # Valley along the road: lower where the ring crosses straight ahead/behind
    # (angle 0 and pi here, i.e. +/-Y), full height at the flanks.
    across = np.abs(np.sin(angle))
    valley = 0.35 + 0.65 * smoothstep(0.08, 0.6, across)
    height = PEAK * profile * valley * (0.35 * broad + 0.65 * main) * 1.45
    return height, angle, radius


def main():
    H, angle, radius = height_field(TEX_W, TEX_H)
    ds_a = 2 * math.pi * radius / TEX_W                  # metres per texel around
    ds_r = (R_MAX - R_MIN) / TEX_H                        # metres per texel outwards
    dh_a = (np.roll(H, -1, 1) - np.roll(H, 1, 1)) / (2 * ds_a)
    dh_r = np.gradient(H, axis=0) / ds_r
    slope = np.sqrt(dh_a ** 2 + dh_r ** 2)               # rise over run

    # ---- colour
    n = Noise2(SEED + 7)
    a01 = angle / (2 * math.pi)
    r01 = (radius - R_MIN) / (R_MAX - R_MIN)
    mottle = n(a01, r01, 900, 140) * 0.6 + n(a01, r01, 3000, 470) * 0.4
    forest = np.array([0.030, 0.042, 0.032], np.float32)
    forest_light = np.array([0.055, 0.068, 0.048], np.float32)
    rock = np.array([0.13, 0.125, 0.118], np.float32)
    rock_dark = np.array([0.055, 0.052, 0.05], np.float32)
    scree = np.array([0.17, 0.165, 0.155], np.float32)
    snow = np.array([0.78, 0.80, 0.84], np.float32)

    col = forest[None, None] + (forest_light - forest)[None, None] * mottle[..., None]
    tree_line = TREE_LINE + (n(a01, r01, 200, 30) - 0.5) * 60
    # Forest clings to fairly steep ground; only real faces are bare.
    steep = smoothstep(1.05, 1.7, slope)
    bare = np.clip(np.maximum(steep, smoothstep(tree_line - 20, tree_line + 25, H)), 0, 1)
    rock_col = rock_dark[None, None] + (rock - rock_dark)[None, None] * mottle[..., None]
    col = col * (1 - bare[..., None]) + rock_col * bare[..., None]
    # Scree: gullies (where the height sits below its surroundings) on bare ground.
    gully = np.clip((blur(H, 6) - H) / 6.0, 0, 1)
    col = col + (scree - col) * (gully * bare)[..., None] * 0.7
    # Snow on the highest ground, not on the steepest faces.
    snow_amt = smoothstep(SNOW_LINE - 15, SNOW_LINE + 30, H + (mottle - 0.5) * 30) * (1 - smoothstep(1.0, 1.6, slope))
    col = col * (1 - snow_amt[..., None]) + snow * snow_amt[..., None]
    # Cavity: valleys and ravines darker.
    cavity = np.clip((blur(H, 12) - H) / 25.0, 0, 1)
    col *= (1 - cavity * 0.45)[..., None]

    # ---- detail normal: what the mesh cannot carry
    mesh_scale = TEX_W // MESH_W
    coarse = blur(H, max(2, mesh_scale))
    detail = H - coarse
    dd_a = (np.roll(detail, -1, 1) - np.roll(detail, 1, 1)) / (2 * ds_a)
    dd_r = np.gradient(detail, axis=0) / ds_r
    # Forest canopy texture on the forested slopes.
    canopy = (n(a01, r01, 6000, 900) - 0.5) * (1 - bare) * 3.0
    dd_a += (np.roll(canopy, -1, 1) - np.roll(canopy, 1, 1)) / (2 * ds_a)
    # Tangent space: u runs around the ring (angle), v outwards (radius).
    nrm = np.stack([-dd_a * 3.0, -dd_r * 3.0, np.ones_like(H)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)

    # Image row 0 is v = 1 (the outer edge), so flip on save.
    def to8(a):
        return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)

    srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, None), 1 / 2.4) - 0.055)
    Image.fromarray(to8(srgb[::-1]), "RGB").save(os.path.join(OUT, "T_mountain_ring_D.jpg"), quality=90)
    half = nrm.reshape(TEX_H // 2, 2, TEX_W // 2, 2, 3).mean(axis=(1, 3))
    half /= np.linalg.norm(half, axis=-1, keepdims=True)
    Image.fromarray(to8(half[::-1] * 0.5 + 0.5), "RGB").save(os.path.join(OUT, "T_mountain_ring_N.jpg"), quality=92)

    # ---- mesh, sampled from the same field (the seam column is shared, not duplicated
    # in position; UVs need a duplicate column so u runs 0..1 without wrapping back).
    Hm, am, rm = height_field(MESH_W, MESH_H)
    rows, cols = MESH_H, MESH_W + 1
    verts, uvs = [], []
    for j in range(rows):
        for i in range(cols):
            ii = i % MESH_W
            a = (i / MESH_W) * 2 * math.pi
            r = R_MIN + (j + 0.5) / MESH_H * (R_MAX - R_MIN)
            if j == 0:
                r = R_MIN
            z = float(Hm[j, ii])
            # Around the ring clockwise seen from above, angle 0 on +Y.
            verts.append((r * math.sin(a), r * math.cos(a), z))
            uvs.append((i / MESH_W, (r - R_MIN) / (R_MAX - R_MIN)))
    faces = []
    for j in range(rows - 1):
        for i in range(cols - 1):
            v0 = j * cols + i
            faces.append((v0, v0 + cols, v0 + cols + 1, v0 + 1))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    me = bpy.data.meshes.new("SM_mountain_ring")
    me.from_pydata(verts, [], faces)
    me.update()
    uv = me.uv_layers.new()
    for loop in me.loops:
        uv.data[loop.index].uv = uvs[loop.vertex_index]
    ob = bpy.data.objects.new("SM_mountain_ring", me)
    bpy.context.scene.collection.objects.link(ob)
    # Faces must point inwards, at the player at the centre.
    me.calc_loop_triangles()
    mid = me.polygons[len(me.polygons) // 2]
    to_centre = -mid.center.to_2d().normalized()
    if mid.normal.to_2d().dot(to_centre) < 0:
        me.flip_normals()
    for p in me.polygons:
        p.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.01)   # merges the u-seam positions only where UVs allow
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "SM_mountain_ring.fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=True,
                             path_mode="STRIP", add_leaf_bones=False)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print(f"RR_MOUNTAINS tris={tris} peak={H.max():.0f}m mean={H.mean():.0f}m "
          f"bare={bare.mean():.2f} snow={snow_amt.mean():.3f}")

    # ---- preview: render from the valley floor looking along the flank.
    mat = bpy.data.materials.new("ring")
    nt = mat.node_tree
    b = nt.nodes["Principled BSDF"]
    td = nt.nodes.new("ShaderNodeTexImage")
    td.image = bpy.data.images.load(os.path.join(OUT, "T_mountain_ring_D.jpg"))
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(os.path.join(OUT, "T_mountain_ring_N.jpg"))
    tn.image.colorspace_settings.name = "Non-Color"
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(td.outputs["Color"], b.inputs["Base Color"])
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Roughness"].default_value = 0.95
    ob.data.materials.append(mat)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    world = bpy.data.worlds.new("w")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.6, 0.63, 0.66, 1)
    # Game-like aerial perspective, as Unity's exp2 fog at Greenwood's density: mix
    # towards the fog colour by camera distance in the material.
    cam_dist = nt.nodes.new("ShaderNodeCameraData")
    k = nt.nodes.new("ShaderNodeMath")
    k.operation = "MULTIPLY"
    k.inputs[1].default_value = 0.0010   # the backdrop shader's own haze, not the scene fog
    nt.links.new(cam_dist.outputs["View Distance"], k.inputs[0])
    sq = nt.nodes.new("ShaderNodeMath")
    sq.operation = "POWER"
    sq.inputs[1].default_value = 2.0
    nt.links.new(k.outputs[0], sq.inputs[0])
    neg = nt.nodes.new("ShaderNodeMath")
    neg.operation = "MULTIPLY"
    neg.inputs[1].default_value = -1.0
    nt.links.new(sq.outputs[0], neg.inputs[0])
    ex = nt.nodes.new("ShaderNodeMath")
    ex.operation = "EXPONENT"
    nt.links.new(neg.outputs[0], ex.inputs[0])
    fog = nt.nodes.new("ShaderNodeMath")
    fog.operation = "SUBTRACT"
    fog.inputs[0].default_value = 1.0
    nt.links.new(ex.outputs[0], fog.inputs[1])
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (0.62, 0.64, 0.67, 1)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(fog.outputs[0], mix.inputs[0])
    nt.links.new(b.outputs["BSDF"], mix.inputs[1])
    nt.links.new(emit.outputs["Emission"], mix.inputs[2])
    nt.links.new(mix.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 2.0
    sun.data.angle = math.radians(8)
    sun.rotation_euler = (math.radians(50), 0, math.radians(40))
    scene.collection.objects.link(sun)
    bpy.ops.mesh.primitive_plane_add(size=900, location=(0, 0, -1))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("g")
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.03, 0.04, 0.03, 1)
    ground.data.materials.append(gm)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 22
    cam.data.clip_end = 5000
    cam.location = (0, 0, 4)
    cam.rotation_euler = (math.radians(88), 0, math.radians(-35))
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1280, 640
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)
    print("RR_DONE")


main()
