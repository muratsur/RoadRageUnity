"""Builds Greenwood's horizon from real terrain: the northern Black Forest around the
Acher valley, below the Mummelsee and the Hornisgrinde on the Black Forest High Road
(Schwarzwaldhochstrasse, B500).

The game's mountain ring stands 420 m - 1 km from the camera, because the road fog
erases anything further. The real ridges are 1-9 km away. Each point of the ring is a
real point on the same bearing from the viewpoint, and its height is scaled by
(ring distance / real distance), so every ridge stands at exactly the angle above the
horizon it does in reality: the skyline is the real one, only brought closer.

Elevation: AWS Terrain Tiles (Tilezen), which for this area come from EU-DEM and SRTM.
Attribution is required; see Assets/Resources/Biomes/Mountains/ATTRIBUTION.txt.
The tiles are downloaded once and cached next to the output.

Colour is classified from the real terrain: dense spruce and fir forest almost
everywhere, open heath (the "Grinden") on the flat summit plateaus, pale meadow on
gentle valley floors, a little rock on the steepest slopes. No snow.

    python3.11 -m venv .venv && .venv/bin/pip install bpy==5.0.1 pillow
    .venv/bin/python Tools/Terrain/build_black_forest_ring.py

Output (./out_black_forest next to this script, or $RR_OUT): the same files the game
loads for the ring - SM_mountain_ring.fbx, T_mountain_ring_D.jpg, T_mountain_ring_N.jpg -
plus preview.png, a panorama from the viewpoint.
Axes: Blender +Z up, +Y is the direction the road runs (up the valley, towards the
Hornisgrinde ridge); the game keeps the ring unrotated, so +Y becomes the road's forward.
"""
import bpy  # noqa: must load before anything that touches bpy data
import io
import math
import os
import urllib.request

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_black_forest"))
os.makedirs(OUT, exist_ok=True)

# Viewpoint: Seebach, in the Acher valley below the Mummelsee (382 m). The valley runs
# up to the north-east (Mummelsee / Hornisgrinde ridge ahead) and opens to the
# south-west towards the Rhine plain behind.
VIEW_LAT, VIEW_LON = 48.5770, 8.1700
FORWARD_AZIMUTH = 40.0              # degrees clockwise from north
EYE_HEIGHT = 2.0

R_MIN, R_MAX = 420.0, 1000.0        # game ring, m from the camera
REAL_MIN, REAL_MAX = 900.0, 8500.0  # real distances mapped onto it (log scale)
TEX_W, TEX_H = 4096, 1024
MESH_W, MESH_H = 480, 48

ZOOM = 12
BBOX = (48.44, 7.99, 48.72, 8.36)   # south, west, north, east
TILE_URL = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png"


# ---------------------------------------------------------------- elevation

def mercator(lat, lon, z=ZOOM):
    n = 2 ** z
    x = (lon + 180.0) / 360.0 * n
    lat_r = np.radians(lat)
    y = (1.0 - np.log(np.tan(lat_r) + 1.0 / np.cos(lat_r)) / math.pi) / 2.0 * n
    return x, y


def load_dem():
    """Elevation in metres for BBOX, as one array, plus the tile origin."""
    s, w, n, e = BBOX
    x0, y0 = mercator(n, w)
    x1, y1 = mercator(s, e)
    xs = range(int(x0), int(x1) + 1)
    ys = range(int(y0), int(y1) + 1)
    cache = os.path.join(OUT, f"dem_z{ZOOM}_{xs[0]}_{ys[0]}_{len(xs)}x{len(ys)}.npy")
    if os.path.exists(cache):
        return np.load(cache), xs[0], ys[0]
    rows = []
    for ty in ys:
        row = []
        for tx in xs:
            data = urllib.request.urlopen(TILE_URL.format(z=ZOOM, x=tx, y=ty), timeout=30).read()
            a = np.asarray(Image.open(io.BytesIO(data)).convert("RGB")).astype(np.float64)
            row.append(a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768)
        rows.append(np.concatenate(row, 1))
    dem = np.concatenate(rows, 0).astype(np.float32)
    np.save(cache, dem)
    return dem, xs[0], ys[0]


DEM, TX0, TY0 = load_dem()
GY, GX = np.gradient(DEM)
# Metres per DEM pixel at this latitude (web mercator).
PIXEL_M = 156543.03392 * math.cos(math.radians(VIEW_LAT)) / (2 ** ZOOM)


def sample(arr, lat, lon):
    """Bilinear sample of a DEM-shaped array at lat/lon."""
    x, y = mercator(lat, lon)
    px = (x - TX0) * 256 - 0.5
    py = (y - TY0) * 256 - 0.5
    x0 = np.clip(np.floor(px).astype(np.int64), 0, arr.shape[1] - 2)
    y0 = np.clip(np.floor(py).astype(np.int64), 0, arr.shape[0] - 2)
    fx = np.clip(px - x0, 0, 1)
    fy = np.clip(py - y0, 0, 1)
    a = arr[y0, x0] + (arr[y0, x0 + 1] - arr[y0, x0]) * fx
    b = arr[y0 + 1, x0] + (arr[y0 + 1, x0 + 1] - arr[y0 + 1, x0]) * fx
    return a + (b - a) * fy


VIEW_ELEV = float(sample(DEM, np.array(VIEW_LAT), np.array(VIEW_LON))) + EYE_HEIGHT


def ring_field(w, h):
    """Game height, real elevation and real slope over a (radius x angle) grid.
    Angle 0 is the road's forward direction (+Y)."""
    a = (np.arange(w, dtype=np.float64) + 0.5) / w * 2 * math.pi
    t = (np.arange(h, dtype=np.float64) + 0.5) / h
    angle = a[None, :].repeat(h, 0)
    tt = t[:, None].repeat(w, 1)
    r_game = R_MIN + tt * (R_MAX - R_MIN)
    r_real = REAL_MIN * (REAL_MAX / REAL_MIN) ** tt
    bearing = np.radians(FORWARD_AZIMUTH) + angle
    north = r_real * np.cos(bearing)
    east = r_real * np.sin(bearing)
    lat = VIEW_LAT + north / 111320.0
    lon = VIEW_LON + east / (111320.0 * math.cos(math.radians(VIEW_LAT)))
    elev = sample(DEM, lat, lon)
    slope = np.hypot(sample(GX, lat, lon), sample(GY, lat, lon)) / PIXEL_M
    # Same angle above the horizon as in reality; ground below eye level is hidden by
    # the game's own ground, so it only needs to sit below it.
    rel = np.maximum(elev - VIEW_ELEV, 0.0)
    height = rel * r_game / r_real
    return height.astype(np.float32), elev.astype(np.float32), slope.astype(np.float32), angle, r_game


# ---------------------------------------------------------------- helpers

def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def blur(a, k):
    out = a.astype(np.float32)
    for _ in range(3):
        c = np.cumsum(np.pad(out, ((0, 0), (k, k)), mode="wrap"), axis=1)
        out = ((c[:, 2 * k:] - c[:, :-2 * k]) / (2 * k))[:, :a.shape[1]]
        c = np.cumsum(np.pad(out, ((k, k), (0, 0)), mode="edge"), axis=0)
        out = ((c[2 * k:] - c[:-2 * k]) / (2 * k))[:a.shape[0]]
    return out


def value_noise(w, h, cells_x, cells_y, seed):
    """Periodic around the ring (x), for forest mottling and canopy texture."""
    r = np.random.default_rng(seed)
    g = r.random((cells_y + 2, cells_x)).astype(np.float32)
    x = (np.arange(w) + 0.5) / w * cells_x
    y = (np.arange(h) + 0.5) / h * cells_y
    xi = np.floor(x).astype(int)
    yi = np.floor(y).astype(int)
    fx = x - xi
    fy = y - yi
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    a = g[yi][:, xi % cells_x] * (1 - fx) + g[yi][:, (xi + 1) % cells_x] * fx
    b = g[yi + 1][:, xi % cells_x] * (1 - fx) + g[yi + 1][:, (xi + 1) % cells_x] * fx
    return (a * (1 - fy[:, None]) + b * fy[:, None]).astype(np.float32)


# ---------------------------------------------------------------- build

def main():
    H, elev, slope, angle, r_game = ring_field(TEX_W, TEX_H)

    mottle = value_noise(TEX_W, TEX_H, 1400, 140, 1) * 0.6 + value_noise(TEX_W, TEX_H, 4000, 400, 2) * 0.4
    forest = np.array([0.022, 0.032, 0.024], np.float32)
    forest_light = np.array([0.042, 0.054, 0.036], np.float32)
    col = forest + (forest_light - forest) * mottle[..., None]
    # Grinden: open heath on the flat summit plateaus above ~1,050 m.
    heath = smoothstep(1020, 1080, elev) * (1 - smoothstep(0.12, 0.25, slope))
    heath *= smoothstep(0.35, 0.6, value_noise(TEX_W, TEX_H, 300, 30, 3))
    col += (np.array([0.085, 0.07, 0.045], np.float32) - col) * heath[..., None]
    # Meadows on gentle valley floors.
    meadow = (1 - smoothstep(420, 560, elev)) * (1 - smoothstep(0.08, 0.2, slope))
    col += (np.array([0.07, 0.078, 0.045], np.float32) - col) * meadow[..., None] * 0.85
    # A little bare rock on the steepest ground (granite and gneiss outcrops).
    rock = smoothstep(0.75, 1.1, slope)
    col += (np.array([0.10, 0.095, 0.088], np.float32) - col) * rock[..., None]
    cavity = np.clip((blur(H, 10) - H) / 20.0, 0, 1)
    col *= (1 - cavity * 0.4)[..., None]

    # Detail normal: height the mesh is too coarse for, plus canopy texture on forest.
    ds_a = 2 * math.pi * r_game / TEX_W
    ds_r = (R_MAX - R_MIN) / TEX_H
    detail = H - blur(H, max(2, TEX_W // MESH_W))
    canopy = (value_noise(TEX_W, TEX_H, 9000, 900, 4) - 0.5) * 2.5 * (1 - heath - meadow).clip(0, 1)
    detail = detail + canopy
    dd_a = (np.roll(detail, -1, 1) - np.roll(detail, 1, 1)) / (2 * ds_a)
    dd_r = np.gradient(detail, axis=0) / ds_r
    nrm = np.stack([-dd_a * 3.0, -dd_r * 3.0, np.ones_like(H)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)

    def to8(a):
        return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)

    srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, None), 1 / 2.4) - 0.055)
    Image.fromarray(to8(srgb[::-1]), "RGB").save(os.path.join(OUT, "T_mountain_ring_D.jpg"), quality=90)
    half = nrm.reshape(TEX_H // 2, 2, TEX_W // 2, 2, 3).mean(axis=(1, 3))
    half /= np.linalg.norm(half, axis=-1, keepdims=True)
    Image.fromarray(to8(half[::-1] * 0.5 + 0.5), "RGB").save(os.path.join(OUT, "T_mountain_ring_N.jpg"), quality=92)

    # Mesh from the same field at lower resolution.
    Hm, _, _, _, _ = ring_field(MESH_W, MESH_H)
    cols = MESH_W + 1
    verts, uvs = [], []
    for j in range(MESH_H):
        for i in range(cols):
            a = (i / MESH_W) * 2 * math.pi
            t = (j + 0.5) / MESH_H if j > 0 else 0.0
            r = R_MIN + t * (R_MAX - R_MIN)
            verts.append((r * math.sin(a), r * math.cos(a), float(Hm[j, i % MESH_W])))
            uvs.append((i / MESH_W, t))
    faces = []
    for j in range(MESH_H - 1):
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
    mid = me.polygons[len(me.polygons) // 2]
    if mid.normal.to_2d().dot(-mid.center.to_2d().normalized()) < 0:
        me.flip_normals()
    for p in me.polygons:
        p.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.01)
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "SM_mountain_ring.fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=True,
                             path_mode="STRIP", add_leaf_bones=False)
    ahead = H[:, :TEX_W // 36].max()
    print(f"RR_BLACKFOREST view={VIEW_ELEV:.0f}m tallest={H.max():.0f}m ahead={ahead:.0f}m "
          f"heath={heath.mean():.3f} meadow={meadow.mean():.3f} rock={rock.mean():.3f}")

    # Preview: the panorama from the viewpoint, as the game sees it (haze included).
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
    cam_data_node = nt.nodes.new("ShaderNodeCameraData")
    k = nt.nodes.new("ShaderNodeMath")
    k.operation = "MULTIPLY"
    k.inputs[1].default_value = 0.0010
    nt.links.new(cam_data_node.outputs["View Distance"], k.inputs[0])
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
    emit.inputs["Color"].default_value = (0.55, 0.58, 0.60, 1)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(fog.outputs[0], mix.inputs[0])
    nt.links.new(b.outputs["BSDF"], mix.inputs[1])
    nt.links.new(emit.outputs["Emission"], mix.inputs[2])
    nt.links.new(mix.outputs[0], nt.nodes["Material Output"].inputs["Surface"])
    ob.data.materials.append(mat)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    world = bpy.data.worlds.new("w")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.58, 0.60, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 2.2
    sun.data.angle = math.radians(10)
    sun.rotation_euler = (math.radians(55), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    bpy.ops.mesh.primitive_plane_add(size=900, location=(0, 0, 0))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("g")
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.03, 0.035, 0.025, 1)
    ground.data.materials.append(gm)
    cd = bpy.data.cameras.new("cam")
    cd.type = "PANO"
    try:
        cd.panorama_type = "EQUIRECTANGULAR"
    except AttributeError:
        cd.cycles.panorama_type = "EQUIRECTANGULAR"
    cd.clip_end = 5000
    cam = bpy.data.objects.new("cam", cd)
    cam.location = (0, 0, EYE_HEIGHT)
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 2048, 1024
    # Only the band around the horizon.
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    scene.render.border_min_y, scene.render.border_max_y = 0.45, 0.66
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = os.path.join(OUT, "preview.png")
    bpy.ops.render.render(write_still=True)
    print("RR_DONE")


main()
