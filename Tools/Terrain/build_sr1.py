"""Builds the Big Sur biome's road from the real California Highway 1, Garrapata to Lucia, with
the same rules as Greenwood's B500: this runs build_b500_road.py, export_b500_realmap.py and
bake_b500_terrain.py with their inputs and outputs pointed at this road.

Inputs come from fetch_sr1.py. The land cover is ESA WorldCover. California is far outside
the UTM zone the B500 tools use, so "east" and "north" here are a local flat projection.
Needs rasterio.
"""
import json
import math
import os
import struct
import sys

import numpy as np
import rasterio

import bake_b500_terrain as bake
import build_b500_road as road
import export_b500_realmap as realmap

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(road.ROOT, "Assets", "Resources", "Biomes", "Routes")
LINE = os.path.join(HERE, "sr1_line.json")
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "california")
DEM = os.path.join(DATA, "dem", "USGS_13_n37w122.tif")
LAT0 = 36.3
KX = 111320.0 * math.cos(math.radians(LAT0))
KY = 110950.0

road.OUT = os.path.join(HERE, "out_sr1")
road.ASSET = os.path.join(ROUTES, "sr1.bytes")
road.COVER_ASSET = os.path.join(ROUTES, "sr1_cover.bytes")
road.PLACES_ASSET = os.path.join(ROUTES, "sr1_places.txt")
road.COVER_URL = ("https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/"
                  "ESA_WorldCover_10m_2021_v200_N36W123_Map.tif")
road.COVER_BOUNDS = (-122.0, 36.0, -121.3, 36.56)
road.PLACES = [
    ("Garrapata", 36.4668, -121.9200, "Garrapata"),
    ("Bixby Bridge", 36.3716, -121.9017, "Bixby Bridge"),
    ("Point Sur", 36.3063, -121.8900, "Point Sur"),
    ("Big Sur", 36.2704, -121.8081, "Big Sur"),
    ("Nepenthe", 36.2217, -121.7604, "Nepenthe"),
    ("McWay Falls", 36.1578, -121.6722, "McWay Falls"),
    ("Lucia", 36.0196, -121.5490, "Lucia"),
]
realmap.OUT = os.path.join(HERE, "sr1_realmap.csv")
bake.REALMAP = realmap.OUT
bake.DGM = os.path.dirname(DEM)
bake.ASSET = os.path.join(ROUTES, "sr1_terrain.bytes")
bake.PREVIEW = os.path.join(DATA, "sr1_terrain_preview.png")
bake.ROAD_HALF = 8.0    # the DEM has 10 m cells
DECK_MARGIN = 20.0
GROUND_LIMIT = 260.0    # the game's MoselGroundLimit: it squeezes heights by tanh
SEA_HEIGHT = 1.5        # m: the DEM has the sea at about 0
SEA_BELOW_ROAD = 55.0   # m under the road's lowest point: the game road has no real height, this is its cliff
SEA_BED = 3.0
LAND_ABOVE_SEA = 0.6
START_LAT = 36.476       # the first 8 km from Carmel run inland: start where the Pacific is in view
ROAD_BLEND = 300.0      # m from the road over which land goes from the road's height to its own


def read_route():
    pts = np.array(json.load(open(LINE)))
    pts = pts[np.argmax(pts[:, 1] < START_LAT):]
    return pts[:, 1], pts[:, 0]


def local(lat, lon):
    return np.asarray(lon) * KX, np.asarray(lat) * KY


class Dem:
    def __init__(self, folder):
        self.zips = {}
        self.missing = set()
        with rasterio.open(DEM) as d:
            self.bounds, self.transform = d.bounds, d.transform
            # The sea has no heights in 3DEP: it is at sea level.
            self.grid = np.where(d.read(1) < -100, 0.0, d.read(1)).astype(np.float32)

    def height(self, east, north):
        lon, lat = east / KX, north / KY
        t, a = self.transform, self.grid
        fx = (lon - t.c) / t.a - 0.5
        fy = (lat - t.f) / t.e - 0.5
        ix = np.clip(np.floor(fx).astype(int), 0, a.shape[1] - 2)
        iy = np.clip(np.floor(fy).astype(int), 0, a.shape[0] - 2)
        ax, ay = np.clip(fx - ix, 0, 1), np.clip(fy - iy, 0, 1)
        return (a[iy, ix] * (1 - ax) * (1 - ay) + a[iy, ix + 1] * ax * (1 - ay)
                + a[iy + 1, ix] * (1 - ax) * ay + a[iy + 1, ix + 1] * ax * ay)


def offsets():
    right = list(bake_offsets()[bake_offsets() > 0]) + list(np.arange(320.0, 1100.1, 20.0))
    return np.array([-o for o in reversed(right)] + [0.0] + right, np.float64)


def deck_heights(z, heights):
    """Straight across each bridge, from the ground just beyond one end to just beyond the other."""
    out = heights.copy()
    path = os.path.join(ROUTES, "sr1_structures.txt")
    for line in open(path):
        f = line.split()
        if not f or f[0] != "deck":
            continue
        a, b = float(f[1]) - DECK_MARGIN, float(f[2]) + DECK_MARGIN
        span = (z >= a) & (z <= b)
        out[span] = np.interp(z[span], [a, b], np.interp([a, b], z, heights))
    return out


def game_road_heights(z):
    with open(road.ASSET, "rb") as f:
        data = f.read()
    count, step = struct.unpack_from("<if", data, 4)
    pairs = np.frombuffer(data, "<f4", count * 2, 12).reshape(count, 2)
    return np.interp(z, np.arange(count) * step, pairs[:, 1])


def ground_table(z, offs, ground, road_real):
    """The Pacific as one flat level. The game's road is not at its real height, so ground
    kept relative to it put the sea at a different height on every row. Here land has its real
    height above one sea level, easing into the game road's height near the road."""
    game_y = game_road_heights(z)
    sea_level = float(game_y.min() - SEA_BELOW_ROAD)
    fall = np.clip(np.abs(offs) / ROAD_BLEND, 0.0, 1.0)
    fall = 1.0 - fall * fall * (3.0 - 2.0 * fall)
    world = sea_level + ground + (game_y - sea_level - road_real)[:, None] * fall[None, :]
    world = np.maximum(world, sea_level + LAND_ABOVE_SEA)
    world[ground < SEA_HEIGHT] = sea_level - SEA_BED
    rel = world - game_y[:, None]
    # Undo the squeeze the game will apply, so the sea stays flat. Hills may stay squeezed.
    below = rel < 0.0
    rel[below] = GROUND_LIMIT * np.arctanh(np.clip(rel[below] / GROUND_LIMIT, -0.95, 0.0))
    path = os.path.join(ROUTES, "sr1_structures.txt")
    lines = [l for l in open(path).read().split("\n") if l and not l.startswith("sea ")]
    open(path, "w", newline="\n").write("\n".join(lines + [f"sea {sea_level:.2f}"]) + "\n")
    print("sea level", round(sea_level, 2), "sea share", round(float((ground < SEA_HEIGHT).mean()), 3))
    return rel


bake_offsets = bake.offsets
road.read_route = read_route
realmap.utm32 = local
bake.Dgm = Dem
bake.offsets = offsets
bake.ROAD_HEIGHTS = deck_heights
bake.GROUND_TABLE = ground_table

if __name__ == "__main__":
    os.makedirs(road.OUT, exist_ok=True)
    # "terrain": only the ground, which needs the structures from the road's first bake.
    if sys.argv[1:] != ["terrain"]:
        road.main()
        realmap.main()
    bake.main()
