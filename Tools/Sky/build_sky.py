"""Renders a 360-degree sky panorama with a procedural cloud deck.

Each pixel is a view direction. Its ray is intersected with a cloud layer at a fixed
altitude, and the cloud at that point is shaded analytically:
  - density is fractal noise (billowy + smooth), cut by a coverage threshold;
  - optical thickness darkens the cloud base, Beer's law lets thin cloud show the sky;
  - a second density sample offset towards the sun gives self-shadowing and the bright
    rims on the sun side;
  - aerial perspective hazes distant cloud into the horizon colour.
Noise octaves too fine for a pixel's footprint are faded out, so distant cloud near the
horizon compresses into streaks instead of aliasing.

Only the upper hemisphere is produced (elevation 0-90 degrees, 4:1): below the horizon
the game shows terrain and fog, and the skybox shader fades into the fog colour there.

A volumetric Cycles render was tried first; it took 10 minutes for a
low-resolution frame and was very hard to tune. This takes seconds at full size.

    python3 -m pip install numpy pillow
    python3 Tools/Sky/build_sky.py            # all presets
    RR_SKY=overcast python3 Tools/Sky/build_sky.py

Output: T_sky_<preset>.jpg in ./out next to this script (or $RR_OUT). The game loads it
from Assets/Resources/Sky/Textures.
"""
import math
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out"))
WIDTH = int(os.environ.get("RR_WIDTH", "4096"))
HEIGHT = WIDTH // 4

PRESETS = {
    # A low stratocumulus deck: almost total cover, lumpy grey base, a few thin patches
    # where brighter sky shows through, sun hidden but lifting one side of the sky.
    "overcast": dict(
        seed=11,
        altitude=1400.0,          # m, cloud base
        scale=5200.0,             # m, size of the largest cloud masses
        billow_scale=900.0,       # m, size of the lumps in the base
        coverage=0.86,            # 0-1 share of sky that is cloud
        thickness=2.6,            # optical depth of full cover
        sun_azimuth=35.0,         # degrees
        sun_elevation=24.0,
        # Behind the lower deck is a bright, featureless high layer, not blue sky:
        # that is what makes the thin seams between the lumps lighter than the lumps.
        sky_zenith=(0.80, 0.81, 0.83), sky_horizon=(0.86, 0.87, 0.88),
        cloud_lit=(0.70, 0.71, 0.73), cloud_base=(0.40, 0.42, 0.45),
        haze=(0.74, 0.75, 0.77), haze_distance=26000.0,
        sun_glow=(1.0, 0.97, 0.92), sun_glow_strength=0.10,
    ),
}


# ---------------------------------------------------------------- noise

class ValueNoise:
    """Smooth 2D value noise on a random lattice, evaluated at arbitrary points."""

    def __init__(self, rng, size=512):
        self.size = size
        self.grid = rng.random((size, size)).astype(np.float32)

    def __call__(self, x, y):
        n = self.size
        xi = np.floor(x).astype(np.int64)
        yi = np.floor(y).astype(np.int64)
        fx = (x - xi).astype(np.float32)
        fy = (y - yi).astype(np.float32)
        fx = fx * fx * fx * (fx * (fx * 6 - 15) + 10)   # quintic fade
        fy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
        x0, x1 = xi % n, (xi + 1) % n
        y0, y1 = yi % n, (yi + 1) % n
        g = self.grid
        a = g[y0, x0] + (g[y0, x1] - g[y0, x0]) * fx
        b = g[y1, x0] + (g[y1, x1] - g[y1, x0]) * fx
        return a + (b - a) * fy


def fbm(noise, x, y, footprint, octaves=8, lacunarity=2.03, gain=0.5, billow=False):
    """Fractal sum. Octaves whose wavelength is below the pixel footprint fade out."""
    total = np.zeros_like(x, dtype=np.float32)
    norm = 0.0
    amp, freq = 1.0, 1.0
    for o in range(octaves):
        v = noise(x * freq + o * 17.3, y * freq - o * 29.1)
        if billow:
            # Rounded masses: high in the middle of each blob, zero along the creases
            # between blobs. (1 - this is ridged noise, which draws thin veins.)
            v = np.abs(v * 2.0 - 1.0)
        # wavelength of this octave in noise units is 1/freq
        fade = np.clip(1.0 / (freq * footprint + 1e-6) - 0.5, 0.0, 1.0).astype(np.float32)
        total += v * amp * fade
        norm += amp
        amp *= gain
        freq *= lacunarity
    return total / norm


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- render

def render(name, p):
    rng = np.random.default_rng(p["seed"])
    base_noise = ValueNoise(rng)
    lump_noise = ValueNoise(rng)
    fine_noise = ValueNoise(rng)

    # Pixel centres: u -> azimuth 0..2pi, v -> elevation 0..90 degrees (row 0 = zenith).
    u = (np.arange(WIDTH, dtype=np.float32) + 0.5) / WIDTH
    v = 1.0 - (np.arange(HEIGHT, dtype=np.float32) + 0.5) / HEIGHT
    az = (u * 2 * math.pi)[None, :]
    el = (v * math.pi / 2)[:, None]
    el = np.maximum(el, math.radians(0.35))
    sin_el = np.sin(el)
    cos_el = np.cos(el)

    # Ray to the cloud layer.
    dist = p["altitude"] / sin_el
    horiz = dist * cos_el
    wx = horiz * np.sin(az)
    wy = horiz * np.cos(az)
    # Size of one pixel on the cloud layer, in metres (grows fast towards the horizon).
    pixel_angle = (2 * math.pi) / WIDTH
    footprint_m = dist * pixel_angle / np.maximum(sin_el, 0.02)

    def density(ox=0.0, oy=0.0):
        """Cover (where cloud is at all) times lumps (how thick it is there). The lumps
        modulate the whole deck, so a near-total overcast still shows its structure:
        rounded dark masses with thinner, lighter seams between them."""
        big = fbm(base_noise, (wx + ox) / p["scale"], (wy + oy) / p["scale"],
                  footprint_m / p["scale"], octaves=6)
        lumps = fbm(lump_noise, (wx + ox) / p["billow_scale"], (wy + oy) / p["billow_scale"],
                    footprint_m / p["billow_scale"], octaves=5, billow=True)
        c = p["coverage"]
        cover = smoothstep(0.5 - c * 0.45, 0.62 - c * 0.35, big)
        body = smoothstep(0.02, 0.72, lumps)
        return cover * (0.18 + 0.82 * body)

    cloud = density()
    sun_az = math.radians(p["sun_azimuth"])
    sun_dx, sun_dy = math.sin(sun_az), math.cos(sun_az)
    # Self-shadow: density a few hundred metres towards the sun.
    toward_sun = density(sun_dx * 420.0, sun_dy * 420.0)

    fine = fbm(fine_noise, wx / 260.0, wy / 260.0, footprint_m / 260.0, octaves=4)
    tau = cloud * p["thickness"] * (0.8 + 0.4 * fine)
    transmit = np.exp(-tau)

    # Cloud colour: thick cloud has a dark base; cloud whose sun side is thinner than
    # itself catches light on that side.
    base_t = np.clip(tau / p["thickness"], 0, 1)[..., None]
    lit = np.array(p["cloud_lit"], np.float32)
    dark = np.array(p["cloud_base"], np.float32)
    col = lit * (1 - base_t) + dark * base_t
    rim = np.clip((cloud - toward_sun) * 1.6, 0, 1)[..., None]
    col = col + (lit - col) * rim * 0.55
    col = col * (0.92 + 0.16 * fine[..., None])

    # Sky behind the cloud: vertical gradient plus a soft glow around the hidden sun.
    t = (el / (math.pi / 2)) ** 0.6
    zen = np.array(p["sky_zenith"], np.float32)
    hor = np.array(p["sky_horizon"], np.float32)
    sky = hor * (1 - t[..., None]) + zen * t[..., None]
    dx = np.cos(el) * np.sin(az)
    dy = np.cos(el) * np.cos(az)
    dz = np.sin(el)
    s_el = math.radians(p["sun_elevation"])
    sun_vec = (math.cos(s_el) * sun_dx, math.cos(s_el) * sun_dy, math.sin(s_el))
    cos_sun = dx * sun_vec[0] + dy * sun_vec[1] + dz * sun_vec[2]
    glow = (np.clip(cos_sun, 0, 1) ** 6)[..., None] * p["sun_glow_strength"]
    sky = sky + np.array(p["sun_glow"], np.float32) * glow

    out = col * (1 - transmit[..., None]) + sky * transmit[..., None]
    # The sun's glow also brightens thin cloud in front of it.
    out = out + np.array(p["sun_glow"], np.float32) * glow * 0.8

    # Aerial perspective towards the horizon.
    haze_amount = (1 - np.exp(-dist / p["haze_distance"]))[..., None]
    out = out * (1 - haze_amount) + np.array(p["haze"], np.float32) * haze_amount

    img = np.clip(out, 0, 1)
    # Light dither so the gradients do not band after JPEG compression.
    img = img + (rng.random(img.shape, dtype=np.float32) - 0.5) / 255.0
    img = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, f"T_sky_{name}.jpg")
    Image.fromarray(img, "RGB").save(path, quality=92, subsampling=0)
    print(f"RR_SKY {name} {WIDTH}x{HEIGHT} mean={img.mean() / 255:.3f} "
          f"horizon={img[-8:].mean() / 255:.3f} zenith={img[:8].mean() / 255:.3f} -> {path}")


if __name__ == "__main__":
    names = os.environ.get("RR_SKY", ",".join(PRESETS)).split(",")
    for n in names:
        render(n.strip(), PRESETS[n.strip()])
