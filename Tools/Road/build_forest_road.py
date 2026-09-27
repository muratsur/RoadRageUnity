"""Generates Greenwood's road surface: a worn two-lane country road, 9 m wide.

The road ribbon's UVs run across the carriageway once (u = 0 at the left edge, 1 at the
right) and along it continuously, so this texture is laid out for the road itself:
wheel tracks where tyres actually run, a sealed centre joint, crumbling edges that
break into the dirt verge. It repeats seamlessly every TILE_LENGTH metres along.

Layers, all derived from one height field plus masks:
  asphalt     grey binder with exposed aggregate, large-scale tone drift
  wheel tracks darker and smoother where traffic polishes and stains the surface
  oil strip   faint stain down the middle of each lane
  cracks      a longitudinal centre joint and some transverse and edge cracks, most
              sealed with dark glossy tar ("tar snakes")
  patches     rectangular repairs in fresher, darker asphalt with crisp edges
  edges       ragged break-up into gravel and dust over the outer ~40 cm
  dust        lighter, rougher film away from the tracks, heaviest near the edges

    python3 -m pip install numpy pillow
    python3 Tools/Road/build_forest_road.py

Output (./out next to this script, or $RR_OUT):
  T_forest_road_D.jpg    colour (sRGB)
  T_forest_road_N.png    tangent-space normal, OpenGL (+Y), Unity's convention
  T_forest_road_MSO.png  R=metallic G=occlusion A=smoothness, like the biome kits
  preview.jpg            the tile repeated twice along, at half size
The game loads them from Assets/Resources/Biomes/ForestRoad/Textures.
"""
import math
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out"))
ROAD_WIDTH = 9.0        # m, Greenwood: one 4.5 m lane each way
TILE_LENGTH = 12.0      # m of road per texture repeat
W = int(os.environ.get("RR_WIDTH", "1024"))     # pixels across
# Along: the power of two nearest the same texel size, so Unity does not resample it.
H = 1 << int(round(math.log2(W * TILE_LENGTH / ROAD_WIDTH)))
SEED = int(os.environ.get("RR_SEED", "5"))

rng = np.random.default_rng(SEED)
# Pixel centres in metres. x across the road, y along it.
X = ((np.arange(W, dtype=np.float32) + 0.5) / W * ROAD_WIDTH)[None, :].repeat(H, 0)
Y = ((np.arange(H, dtype=np.float32) + 0.5) / H * TILE_LENGTH)[:, None].repeat(W, 1)


# ---------------------------------------------------------------- noise, periodic along y

def value_noise(cells_x, cells_y, seed):
    """Value noise with an integer number of cells along the tile, so it wraps along y."""
    r = np.random.default_rng(seed)
    grid = r.random((cells_y, cells_x + 2)).astype(np.float32)
    gx = X / ROAD_WIDTH * cells_x
    gy = Y / TILE_LENGTH * cells_y
    xi = np.floor(gx).astype(np.int64)
    yi = np.floor(gy).astype(np.int64)
    fx = gx - xi
    fy = gy - yi
    fx = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    fy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
    y0, y1 = yi % cells_y, (yi + 1) % cells_y
    x0, x1 = np.clip(xi, 0, cells_x + 1), np.clip(xi + 1, 0, cells_x + 1)
    a = grid[y0, x0] + (grid[y0, x1] - grid[y0, x0]) * fx
    b = grid[y1, x0] + (grid[y1, x1] - grid[y1, x0]) * fx
    return a + (b - a) * fy


def fbm(size_m, octaves, seed, gain=0.5):
    """Fractal noise whose largest features are ~size_m; periodic along the tile."""
    total = np.zeros((H, W), np.float32)
    norm = 0.0
    amp = 1.0
    for o in range(octaves):
        feature = size_m / (2 ** o)
        cy = max(1, int(round(TILE_LENGTH / feature)))
        cx = max(1, int(round(ROAD_WIDTH / feature)))
        total += value_noise(cx, cy, seed * 131 + o) * amp
        norm += amp
        amp *= gain
    return total / norm


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def blur(a, radius):
    """Separable box blur (3 passes ~ gaussian), wrapping along y."""
    out = a.astype(np.float32)
    k = max(1, int(radius))
    for _ in range(3):
        c = np.cumsum(np.pad(out, ((k, k), (0, 0)), mode="wrap"), axis=0)
        out = (c[2 * k:] - c[:-2 * k]) / (2 * k)
        out = out[:H]
        c = np.cumsum(np.pad(out, ((0, 0), (k, k)), mode="edge"), axis=1)
        out = (c[:, 2 * k:] - c[:, :-2 * k]) / (2 * k)
        out = out[:, :W]
    return out


# ---------------------------------------------------------------- line drawing

def stamp_polyline(mask, points_m, radius_m, value=1.0):
    """Max-stamps a thick polyline (points in metres) into mask, wrapping along y."""
    px_per_m_x = W / ROAD_WIDTH
    px_per_m_y = H / TILE_LENGTH
    r = radius_m
    for (x0, y0), (x1, y1) in zip(points_m[:-1], points_m[1:]):
        for wrap in (-TILE_LENGTH, 0.0, TILE_LENGTH):
            ya, yb = y0 + wrap, y1 + wrap
            lo_y = int(max(0, (min(ya, yb) - r) * px_per_m_y))
            hi_y = int(min(H, (max(ya, yb) + r) * px_per_m_y + 2))
            lo_x = int(max(0, (min(x0, x1) - r) * px_per_m_x))
            hi_x = int(min(W, (max(x0, x1) + r) * px_per_m_x + 2))
            if lo_y >= hi_y or lo_x >= hi_x:
                continue
            xs = X[lo_y:hi_y, lo_x:hi_x]
            ys = Y[lo_y:hi_y, lo_x:hi_x]
            dx, dy = x1 - x0, yb - ya
            L2 = dx * dx + dy * dy + 1e-9
            t = np.clip(((xs - x0) * dx + (ys - ya) * dy) / L2, 0, 1)
            d = np.hypot(xs - (x0 + t * dx), ys - (ya + t * dy))
            v = np.clip(1.0 - d / r, 0, 1) * value
            mask[lo_y:hi_y, lo_x:hi_x] = np.maximum(mask[lo_y:hi_y, lo_x:hi_x], v)


def wander(x0, y0, heading, length, step, jitter):
    """A crack path: a random walk that keeps roughly to its heading."""
    pts = [(x0, y0)]
    x, y, a = x0, y0, heading
    for _ in range(int(length / step)):
        a += rng.normal(0, jitter)
        a = heading + (a - heading) * 0.8
        x += math.cos(a) * step
        y += math.sin(a) * step
        pts.append((x, y))
    return pts


# ---------------------------------------------------------------- build

def main():
    u = X / ROAD_WIDTH
    edge_dist = np.minimum(X, ROAD_WIDTH - X)             # m to the nearer road edge

    # --- base asphalt
    tone = fbm(4.0, 4, 1)                                   # large drift
    grain = fbm(0.06, 3, 2)                                 # binder texture
    speck = rng.random((H, W)).astype(np.float32)
    aggregate = (speck > 0.72).astype(np.float32) * (0.6 + 0.4 * rng.random((H, W)).astype(np.float32))
    aggregate = np.maximum(aggregate, blur(aggregate, 1) * 0.9)
    height = grain * 0.4 + aggregate * 0.6

    # --- wheel tracks: two per lane, tyre-width bands with wandering edges
    lane_centres = [ROAD_WIDTH * 0.25, ROAD_WIDTH * 0.75]
    wobble = (fbm(3.0, 3, 3) - 0.5) * 0.18
    track = np.zeros((H, W), np.float32)
    for c in lane_centres:
        for side in (-1, 1):
            centre = c + side * 0.80 + wobble
            d = np.abs(X - centre)
            track = np.maximum(track, 1 - smoothstep(0.14, 0.40, d))
    track *= 0.75 + 0.25 * fbm(1.5, 3, 4)
    oil = np.zeros((H, W), np.float32)
    for c in lane_centres:
        oil = np.maximum(oil, (1 - smoothstep(0.1, 0.45, np.abs(X - c - wobble * 0.5))))
    oil *= smoothstep(0.35, 0.75, fbm(2.0, 4, 5)) * 0.8

    # --- patches: fresher asphalt rectangles
    patch = np.zeros((H, W), np.float32)
    patch_edge = np.zeros((H, W), np.float32)
    # No patches by default: the texture repeats every TILE_LENGTH metres, and a
    # rectangle that recurs at a fixed interval reads as a stamp, not a repair.
    for x0, x1, y0, y1 in []:
        ex = fbm(0.4, 2, 7) * 0.05
        inside = (X > x0 + ex) & (X < x1 - ex) & (Y > y0 + ex) & (Y < y1 - ex)
        patch = np.maximum(patch, inside.astype(np.float32))
    patch_edge = np.clip(blur(patch, 2) - blur(patch, 6), 0, 1) * 4 if patch.any() else patch
    patch_grain = fbm(0.05, 3, 8)

    # --- cracks: centre joint, transverse, and edge cracking; most sealed with tar
    crack = np.zeros((H, W), np.float32)
    tar = np.zeros((H, W), np.float32)
    # The centre joint runs the whole length, so it must meet itself at the tile seam:
    # its sideways wander is a sum of waves that each fit the tile a whole number of
    # times. (A random walk ended somewhere else and jumped sideways every tile.)
    ys = np.linspace(0.0, TILE_LENGTH, 161)
    offset = sum(amp * np.sin(2 * math.pi * k * ys / TILE_LENGTH + phase)
                 for k, amp, phase in [(1, 0.06, 0.4), (2, 0.035, 2.1), (5, 0.012, 0.9), (11, 0.005, 3.3)])
    joint = list(zip((ROAD_WIDTH / 2 + offset).tolist(), ys.tolist()))
    stamp_polyline(tar, joint, 0.05)
    stamp_polyline(crack, joint, 0.008)
    # Transverse cracks are hairlines only, unsealed: like the patches, anything bold
    # across the road would be seen recurring every tile.
    hair = np.zeros((H, W), np.float32)
    for y in (3.1, 8.4):
        x_start = rng.uniform(0.3, 1.2)
        path = wander(x_start, y, 0.0 + rng.normal(0, 0.05), rng.uniform(2.0, 4.0), 0.12, 0.25)
        stamp_polyline(hair, path, 0.004)
    crack = np.maximum(crack, hair * 0.5)
    for _ in range(26):                                      # edge cracking
        side = rng.integers(0, 2)
        x_start = 0.15 if side == 0 else ROAD_WIDTH - 0.15
        heading = (0.0 if side == 0 else math.pi) + rng.normal(0, 0.5)
        path = wander(x_start, rng.uniform(0, TILE_LENGTH), heading, rng.uniform(0.3, 1.1), 0.05, 0.6)
        stamp_polyline(crack, path, 0.005)
    crack *= 1 - patch                                      # patches were laid over them
    tar *= 1 - patch * 0.9

    # --- edges: ragged break-up into gravel and dust
    ragged = 0.28 + (fbm(0.8, 4, 9) - 0.5) * 0.35
    broken = 1 - smoothstep(ragged - 0.06, ragged + 0.02, edge_dist)   # 1 = off the asphalt
    dust = np.clip((1 - smoothstep(0.2, 1.4, edge_dist)) * 0.8 + (1 - track) * 0.18, 0, 1)
    dust *= 0.6 + 0.4 * fbm(1.2, 4, 10)
    gravel = (rng.random((H, W)) > 0.55).astype(np.float32) * broken
    gravel = np.maximum(gravel, blur(gravel, 1))

    # ---------------------------------------------------------------- colour
    # Linear albedo. Worn asphalt reflects ~10-15%; 0.30 read as pale concrete.
    asphalt = np.array([0.115, 0.115, 0.118], np.float32)
    col = np.ones((H, W, 3), np.float32) * asphalt
    col *= (0.90 + 0.18 * tone)[..., None]
    col *= (0.86 + 0.28 * grain)[..., None]
    col += (aggregate * 0.05)[..., None] * np.array([1.0, 0.98, 0.94], np.float32)
    stains = smoothstep(0.55, 0.8, fbm(1.2, 4, 12))
    col *= (1 - stains * 0.14)[..., None]
    col *= (1 - track * 0.22)[..., None]
    col *= (1 - oil * 0.12)[..., None]
    fresh = np.array([0.08, 0.08, 0.085], np.float32) * (0.9 + 0.2 * patch_grain)[..., None]
    col = col * (1 - patch[..., None]) + fresh * patch[..., None]
    col *= (1 - patch_edge * 0.25)[..., None]
    col = col * (1 - (tar * 0.9)[..., None]) + np.array([0.025, 0.025, 0.026], np.float32) * (tar * 0.9)[..., None]
    col *= (1 - crack * 0.6)[..., None]
    dust_col = np.array([0.20, 0.18, 0.15], np.float32)
    col = col * (1 - dust[..., None] * 0.35) + dust_col * dust[..., None] * 0.35
    dirt = np.array([0.09, 0.075, 0.055], np.float32) * (0.8 + 0.4 * fbm(0.3, 3, 11))[..., None]
    stones = np.array([0.17, 0.16, 0.145], np.float32) * (0.75 + 0.5 * rng.random((H, W, 1)).astype(np.float32))
    offroad = dirt * (1 - gravel[..., None]) + stones * gravel[..., None]
    col = col * (1 - broken[..., None]) + offroad * broken[..., None]

    # ---------------------------------------------------------------- height, normal, AO
    h = height * 0.002                                        # ~2 mm aggregate relief
    h -= track * 0.0006                                       # rutting
    h += patch * 0.0025 + patch_edge * 0.0008
    h -= crack * 0.004
    h += tar * 0.0006 * (1 - crack)
    h -= broken * 0.012 * (1 - gravel * 0.4)                  # edge drops into the verge
    h += gravel * broken * 0.004
    sx = W / ROAD_WIDTH                                       # pixels per metre
    sy = H / TILE_LENGTH
    dhdx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * sx / 2
    dhdx[:, 0] = dhdx[:, 1]
    dhdx[:, -1] = dhdx[:, -2]
    dhdy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * sy / 2
    strength = 2.5
    # Tangent space: u runs with x and v with y (the arrays are flipped on save so that
    # v = 0 is the bottom row, as Unity samples it), so n = (-dh/dx, -dh/dy, 1).
    n = np.stack([-dhdx * strength, -dhdy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    cavity = np.clip((blur(h, 3) - h) * 900, 0, 1)
    ao = 1 - cavity * 0.6 - broken * 0.15

    smooth = 0.14 + track * 0.14 + patch * 0.08 + tar * 0.40 - dust * 0.08 - broken * 0.06
    smooth = np.clip(smooth + (grain - 0.5) * 0.04, 0.03, 0.8)

    # ---------------------------------------------------------------- save
    # Arrays are indexed [y, x] with y increasing along the road; Unity samples
    # v = 0 at the bottom of the image, so flip vertically on save.
    os.makedirs(OUT, exist_ok=True)

    def to8(a):
        return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)

    srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, None), 1 / 2.4) - 0.055)
    Image.fromarray(to8(srgb[::-1]), "RGB").save(os.path.join(OUT, "T_forest_road_D.jpg"),
                                                   quality=92, subsampling=0)
    Image.fromarray(to8(n[::-1] * 0.5 + 0.5), "RGB").save(os.path.join(OUT, "T_forest_road_N.png"),
                                                          optimize=True)
    mso = np.zeros((H, W, 4), np.float32)
    mso[..., 1] = ao
    mso[..., 3] = smooth
    Image.fromarray(to8(mso[::-1]), "RGBA").save(os.path.join(OUT, "T_forest_road_MSO.png"), optimize=True)

    shade = srgb * (0.6 + 0.4 * ao[..., None])
    prev = np.concatenate([shade, shade], 0)[::-2, ::2]
    Image.fromarray(to8(prev), "RGB").save(os.path.join(OUT, "preview.jpg"), quality=90)
    print(f"RR_ROAD {W}x{H} colour mean={col.mean():.3f} smooth mean={smooth.mean():.3f}")


main()
