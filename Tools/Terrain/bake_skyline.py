"""Bakes the real skyline seen from a road, for the mountains beyond the game's drawn ground.

The ground grid ends 1.2 km out (GridFarReach), but along a valley road like BC 99 the view is
peaks 3 to 20 km away. From each point of road (every STEP of game distance), this casts rays
all round, relative to the real road's heading, and keeps per distance band the highest
angle above the eye, with that point's distance and height. The game draws each band as one
silhouette strip at that angle (RoadRage.Skyline), so every ridge sits where it really is
in view.

File "RRSK": int rows, float step, int azimuths, int bands, then float band starts (bands + 1),
then per row, band and azimuth: int16 angle (centidegrees), uint8 distance (100 m),
uint8 height (16 m). Azimuth 0 is straight ahead, clockwise.
"""
import os
import struct

import numpy as np

STEP = 300.0
AZIMUTHS = 180
RAYS_PER_AZIMUTH = 3
BANDS = (1200.0, 3500.0, 9000.0, 25000.0)
EYE = 2.0
EARTH = 6371000.0
REFRACTION = 0.87  # light bends back down by about 1/7 of the earth's curvature


def main(height, realmap_path, asset):
    """height(east, north) -> metres, vectorised, in the realmap's coordinates."""
    realmap = np.loadtxt(realmap_path, delimiter=",", skiprows=1)
    z, east, north, de, dn = realmap.T
    zs = np.arange(0.0, z[-1], STEP)
    pick = np.searchsorted(z, zs).clip(0, len(z) - 1)

    rays = AZIMUTHS * RAYS_PER_AZIMUTH
    a = (np.arange(rays) - (RAYS_PER_AZIMUTH - 1) / 2) * (2 * np.pi / rays)
    dists = [BANDS[0]]
    while dists[-1] < BANDS[-1]:
        dists.append(dists[-1] + max(30.0, dists[-1] * 0.012))
    dists = np.array(dists)
    drop = dists ** 2 / (2 * EARTH) * REFRACTION
    band_of = np.searchsorted(BANDS, dists, side="right") - 1
    band_of = band_of.clip(0, len(BANDS) - 2)

    out = np.zeros((len(zs), len(BANDS) - 1, AZIMUTHS, 4), np.uint8)
    for r, i in enumerate(pick):
        fe, fn = de[i], dn[i]
        re_, rn = fn, -fe
        ce, cs = np.cos(a)[:, None], np.sin(a)[:, None]
        pe = east[i] + (ce * fe + cs * re_) * dists[None, :]
        pn = north[i] + (ce * fn + cs * rn) * dists[None, :]
        eye = float(height(np.array([east[i]]), np.array([north[i]]))[0]) + EYE
        h = height(pe.ravel(), pn.ravel()).reshape(pe.shape)
        angle = np.degrees(np.arctan2(h - drop[None, :] - eye, dists[None, :]))
        for b in range(len(BANDS) - 1):
            cols = np.where(band_of == b)[0]
            k = cols[np.argmax(angle[:, cols], axis=1)]
            best = angle[np.arange(rays), k].reshape(AZIMUTHS, RAYS_PER_AZIMUTH)
            j = np.argmax(best, axis=1)
            ray = np.arange(AZIMUTHS) * RAYS_PER_AZIMUTH + j
            centi = np.clip(np.round(best[np.arange(AZIMUTHS), j] * 100), -32767, 32767).astype("<i2")
            out[r, b, :, 0:2] = centi.view(np.uint8).reshape(AZIMUTHS, 2)
            out[r, b, :, 2] = np.clip(np.round(dists[k[ray]] / 100), 0, 255)
            out[r, b, :, 3] = np.clip(np.round(h[ray, k[ray]] / 16), 0, 255)
        if r % 50 == 0:
            print(f"  {r}/{len(zs)} rows, eye {eye:.0f} m, highest {angle.max():.1f} deg", flush=True)

    with open(asset, "wb") as f:
        f.write(b"RRSK")
        f.write(struct.pack("<ifii", len(zs), STEP, AZIMUTHS, len(BANDS) - 1))
        f.write(np.array(BANDS, "<f4").tobytes())
        f.write(out.tobytes())
    print(f"wrote {asset}: {len(zs)} rows, {os.path.getsize(asset) // 1024} KB")
