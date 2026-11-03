"""Builds the Great Ocean Road biome's road from the real B100, Lavers Hill to Peterborough, with
the same rules as build_ss163.py: this runs build_b500_road.py, export_b500_realmap.py and
bake_b500_terrain.py with their inputs and outputs pointed at this road.

Inputs come from fetch_b100.py. "east" and "north" here are a local flat projection (south of
the equator north is negative). The road comes down through the Otway forest to the coast and
runs along the limestone cliffs: one sea level, land at its real height above it.
Needs rasterio.
"""
import json
import math
import os
import struct
import sys

import numpy as np
import rasterio
from rasterio.enums import Resampling
from rasterio.merge import merge

import bake_b500_terrain as bake
import bake_skyline as skyline
import build_b500_road as road
import export_b500_realmap as realmap
import fetch_b100 as fetch

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(road.ROOT, "Assets", "Resources", "Biomes", "Routes")
LINE = os.path.join(HERE, "b100_line.json")
DATA = fetch.DATA
LAT0 = -38.65
KX = 111320.0 * math.cos(math.radians(LAT0))
KY = 111010.0

road.OUT = os.path.join(DATA, "out")
road.ASSET = os.path.join(ROUTES, "b100.bytes")
road.COVER_ASSET = os.path.join(ROUTES, "b100_cover.bytes")
road.PLACES_ASSET = os.path.join(ROUTES, "b100_places.txt")
road.COVER_BOUNDS = fetch.COVER_BOUNDS
road.PLACES = [
    ("Lavers Hill", -38.6822, 143.3906, "Lavers Hill"),
    ("Princetown", -38.6970, 143.1530, "Princetown"),
    ("Gibson Steps", -38.6683, 143.1102, "Gibson Steps"),
    ("Twelve Apostles", -38.6656, 143.1043, "Twelve Apostles"),
    ("Loch Ard Gorge", -38.6455, 143.0700, "Loch Ard Gorge"),
    ("Port Campbell", -38.6190, 142.9950, "Port Campbell"),
    ("London Bridge", -38.6216, 142.9310, "London Bridge"),
    ("Peterborough", -38.6034, 142.8780, "Peterborough"),
]
realmap.OUT = os.path.join(HERE, "b100_realmap.csv")
bake.REALMAP = realmap.OUT
bake.DGM = os.path.join(DATA, "dem")
bake.ASSET = os.path.join(ROUTES, "b100_terrain.bytes")
bake.PREVIEW = os.path.join(DATA, "b100_terrain_preview.png")
bake.ROAD_HALF = 20.0   # the DEM has 30 m cells
DECK_MARGIN = 20.0
GROUND_LIMIT = 900.0    # the game's SeaToSkyGroundLimit: it squeezes heights by tanh
SEA_HEIGHT = 1.5        # m: the DEM has the sea at about 0
SEA_BELOW_ROAD = 55.0   # m under the road's lowest point
SEA_BED = 3.0
LAND_ABOVE_SEA = 0.6
ROAD_BLEND = 300.0      # m from the road over which land goes from the road's height to its own
COAST_EASE_ROWS = 40    # rows over which coast ground eases into inland ground along the road


def read_route():
    pts = np.array(json.load(open(LINE)))
    return pts[:, 1], pts[:, 0]


def local(lat, lon):
    return np.asarray(lon) * KX, np.asarray(lat) * KY


class Dem:
    def __init__(self, folder):
        self.zips = {}
        self.missing = set()
        # Above 50 N the tiles have coarser columns, so all are resampled onto one grid.
        tiles = [rasterio.open(os.path.join(folder, f"{t}.tif")) for t in fetch.DEM_TILES]
        grid, self.transform = merge(tiles, bounds=fetch.COVER_BOUNDS, res=1.0 / 3600.0, resampling=Resampling.bilinear)
        self.grid = np.maximum(grid[0], 0.0).astype(np.float32)

    def height(self, east, north):
        lon, lat = east / KX, north / KY
        t, a = self.transform, self.grid
        fx = (lon - t.c) / t.a - 0.5
        fy = (lat - t.f) / t.e - 0.5
        ix = np.clip(np.floor(fx).astype(int), 0, a.shape[1] - 2)
        iy = np.clip(np.floor(fy).astype(int), 0, a.shape[0] - 2)
        ax, ay = np.clip(fx - ix, 0, 1), np.clip(fy - iy, 0, 1)
        h = (a[iy, ix] * (1 - ax) * (1 - ay) + a[iy, ix + 1] * ax * (1 - ay)
             + a[iy + 1, ix] * (1 - ax) * ay + a[iy + 1, ix + 1] * ax * ay)
        # Off the tiles (the skyline looks 25 km out): sea level, not the edge repeated.
        inside = (fx >= 0) & (fx <= a.shape[1] - 1) & (fy >= 0) & (fy <= a.shape[0] - 1)
        return np.where(inside, h, 0.0)


def offsets():
    # Out to the game's ground grid (GridFarReach): the valley walls are the view.
    right = list(bake_offsets()[bake_offsets() > 0]) + list(np.arange(320.0, 1200.1, 20.0))
    return np.array([-o for o in reversed(right)] + [0.0] + right, np.float64)


def deck_heights(z, heights):
    """Straight across each bridge, from the ground just beyond one end to just beyond the other."""
    out = heights.copy()
    path = os.path.join(ROUTES, "b100_structures.txt")
    for line in open(path) if os.path.exists(path) else ():
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
    """Along the sound as build_sr1's: one flat sea, land at its real height above it. Inland,
    where the road's real height is hundreds of metres above that sea, ground relative to the road."""
    game_y = game_road_heights(z)
    sea_level = float(game_y.min() - SEA_BELOW_ROAD)
    fall = np.clip(np.abs(offs) / ROAD_BLEND, 0.0, 1.0)
    fall = 1.0 - fall * fall * (3.0 - 2.0 * fall)
    coast_world = sea_level + ground + (game_y - sea_level - road_real)[:, None] * fall[None, :]
    inland_world = game_y[:, None] + (ground - road_real[:, None])
    water = ground < SEA_HEIGHT
    coast = np.convolve(water.any(axis=1).astype(float), np.ones(COAST_EASE_ROWS * 2 + 1), "same") > 0
    coast = np.convolve(coast.astype(float), np.ones(COAST_EASE_ROWS * 2 + 1) / (COAST_EASE_ROWS * 2 + 1), "same")
    world = coast[:, None] * coast_world + (1.0 - coast[:, None]) * inland_world
    world = np.maximum(world, sea_level + LAND_ABOVE_SEA)
    world[water] = sea_level - SEA_BED
    rel = world - game_y[:, None]
    # Undo the squeeze the game will apply, so the sea stays flat. Hills may stay squeezed.
    below = rel < 0.0
    rel[below] = GROUND_LIMIT * np.arctanh(np.clip(rel[below] / GROUND_LIMIT, -0.95, 0.0))
    path = os.path.join(ROUTES, "b100_structures.txt")
    lines = [l for l in open(path).read().split("\n") if l and not l.startswith("sea ")]
    open(path, "w", newline="\n").write("\n".join(lines + [f"sea {sea_level:.2f}"]) + "\n")
    print("sea level", round(sea_level, 2), "coast share", round(float((coast > 0.5).mean()), 3))
    return rel


bake_offsets = bake.offsets
road.read_route = read_route
realmap.utm32 = local
bake.Dgm = Dem
bake.offsets = offsets
bake.ROAD_HEIGHTS = deck_heights
bake.GROUND_TABLE = ground_table
# Decimetres: the Otway ranges stand far more than the 327 m centimetres can hold.
bake.UNITS_PER_METRE, bake.MAGIC = 10.0, b"RRTD"

if __name__ == "__main__":
    os.makedirs(road.OUT, exist_ok=True)
    # "skyline": only the far mountains (bake_skyline.py), from the road's realmap.
    if sys.argv[1:] == ["skyline"]:
        skyline.main(Dem(bake.DGM).height, realmap.OUT, os.path.join(ROUTES, "b100_skyline.bytes"))
        sys.exit()
    # "road": only the road and its realmap, before export_b53_structures.py b100 can run.
    if sys.argv[1:] == ["road"]:
        road.main()
        realmap.main()
        sys.exit()
    # "terrain": only the ground, which needs the structures from the road's first bake.
    if sys.argv[1:] != ["terrain"]:
        road.main()
        realmap.main()
    bake.main()
