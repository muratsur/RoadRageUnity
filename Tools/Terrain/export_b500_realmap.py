"""Exports where each point of the game's B500 lies on the real map, for the
real-terrain baker (bake_b500_terrain.py).

The game road is the real B500 with its bends capped and eased (build_b500_road.py),
so a position on it cannot be looked up on the map directly. This walks the same
route with the same heading rules and writes, for every STEP m of real road, the
game's road distance Z, the real position in UTM zone 32 (ETRS89, EPSG:25832, the
grid of the Baden-Wuerttemberg open data) and the real road's direction there.

Output: Tools/Terrain/b500_realmap.csv
  z, east, north, dir_east, dir_north
"""
import math
import os

import numpy as np

import build_b500_road as road

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "b500_realmap.csv")


def utm32(lat, lon):
    """WGS84 / ETRS89 latitude, longitude to UTM zone 32 east, north (metres)."""
    a = 6378137.0
    f = 1 / 298.257223563
    k0 = 0.9996
    e2 = f * (2 - f)
    ep2 = e2 / (1 - e2)
    phi = np.radians(lat)
    lam = np.radians(lon) - math.radians(9.0)
    n = a / np.sqrt(1 - e2 * np.sin(phi) ** 2)
    t = np.tan(phi) ** 2
    c = ep2 * np.cos(phi) ** 2
    aa = np.cos(phi) * lam
    m = a * ((1 - e2 / 4 - 3 * e2 ** 2 / 64 - 5 * e2 ** 3 / 256) * phi
             - (3 * e2 / 8 + 3 * e2 ** 2 / 32 + 45 * e2 ** 3 / 1024) * np.sin(2 * phi)
             + (15 * e2 ** 2 / 256 + 45 * e2 ** 3 / 1024) * np.sin(4 * phi)
             - (35 * e2 ** 3 / 3072) * np.sin(6 * phi))
    east = k0 * n * (aa + (1 - t + c) * aa ** 3 / 6
                     + (5 - 18 * t + t * t + 72 * c - 58 * ep2) * aa ** 5 / 120) + 500000.0
    north = k0 * (m + n * np.tan(phi) * (aa * aa / 2 + (5 - t + 9 * c + 4 * c * c) * aa ** 4 / 24
                                         + (61 - 58 * t + t * t + 600 * c - 330 * ep2) * aa ** 6 / 720))
    return east, north


def main():
    lat, lon = road.read_route()
    s, e, n, rlat, rlon = road.resample(lat, lon)
    # The same game heading as build_b500_road.main, so Z matches b500.bytes.
    heading = np.unwrap(np.arctan2(np.gradient(e), np.gradient(n)))
    curvature = road.smooth(np.gradient(heading) / road.STEP, road.CURVE_SMOOTH, road.STEP)
    count = len(s)
    phi = np.zeros(count)
    for i in range(1, count):
        k = float(np.clip(curvature[i], -1 / road.MIN_RADIUS, 1 / road.MIN_RADIUS))
        p = phi[i - 1] + k * road.STEP
        p -= p * road.STEP / road.RESTORE_LENGTH
        phi[i] = float(np.clip(p, -road.MAX_HEADING, road.MAX_HEADING)) * road.ease(i, count, road.END_EASE)
    z = np.concatenate([[0.0], np.cumsum(np.cos(phi[1:]) * road.STEP)])

    east, north = utm32(rlat, rlon)
    de, dn = np.gradient(east), np.gradient(north)
    length = np.hypot(de, dn)
    de, dn = de / length, dn / length
    with open(OUT, "w") as f:
        f.write("z,east,north,dir_east,dir_north\n")
        for row in zip(z, east, north, de, dn):
            f.write("%.2f,%.2f,%.2f,%.5f,%.5f\n" % row)
    print(f"wrote {OUT}: {count} samples, Z 0..{z[-1]:.0f} m, "
          f"east {east.min():.0f}..{east.max():.0f}, north {north.min():.0f}..{north.max():.0f}")


if __name__ == "__main__":
    main()
