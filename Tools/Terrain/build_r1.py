"""Builds the Iceland biome's road from the real Route 1 along the south coast, from
below Seljalandsfoss past Skogafoss to Vik, with the same rules as Greenwood's B500: this
runs build_b500_road.py, export_b500_realmap.py and bake_b500_terrain.py with their inputs
and outputs pointed at this road.

Inputs:
  Tools/Terrain/r1_line.json: the road as [lon, lat] points, from OpenStreetMap (ref "1",
    (c) OpenStreetMap contributors, ODbL).
  D:/Projects/RoadRageData/iceland/dem/<N63_00_Wxxx>.tif, or RR_DEM=<folder>: Copernicus
    DEM GLO-30, one tile per degree of longitude. Iceland is far outside the UTM zone
    the B500 tools use, so "east" and "north" here are a local flat projection.

Needs rasterio (reads the DEM and the land cover window).
"""
import glob
import json
import math
import os
import re

import numpy as np
import rasterio

import bake_b500_terrain as bake
import build_b500_road as road
import export_b500_realmap as realmap

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(road.ROOT, "Assets", "Resources", "Biomes", "Routes")
LINE = os.path.join(HERE, "r1_line.json")
DEM = os.environ.get("RR_DEM", "D:/Projects/RoadRageData/iceland/dem")
LAT0 = 63.5
KX = 111320.0 * math.cos(math.radians(LAT0))
KY = 110540.0

road.OUT = os.path.join(HERE, "out_r1")
road.ASSET = os.path.join(ROUTES, "r1.bytes")
road.COVER_ASSET = os.path.join(ROUTES, "r1_cover.bytes")
road.PLACES_ASSET = os.path.join(ROUTES, "r1_places.txt")
road.COVER_URL = ("https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/"
                  "ESA_WorldCover_10m_2021_v200_N63W021_Map.tif")
road.COVER_BOUNDS = (-20.15, 63.33, -18.85, 63.72)
# The game reads the falls by these positions in the list.
road.PLACES = [
    ("Seljalandsfoss", 63.6156, -19.9886, "Seljalandsfoss"),
    ("Skógafoss", 63.5320, -19.5114, "Skogafoss"),
    ("Sólheimajökull", 63.5300, -19.3700, "Solheimajokull Glacier"),
    ("Vík", 63.4186, -19.0060, "Vik"),
]
realmap.OUT = os.path.join(HERE, "r1_realmap.csv")
bake.REALMAP = realmap.OUT
bake.DGM = DEM
bake.ASSET = os.path.join(ROUTES, "r1_terrain.bytes")
bake.PREVIEW = os.path.join(os.path.dirname(DEM), "r1_terrain_preview.png")
bake.ROAD_HALF = 15.0   # the DEM has 30 m cells


def read_route():
    pts = np.array(json.load(open(LINE)))
    return pts[:, 1], pts[:, 0]


def local(lat, lon):
    return np.asarray(lon) * KX, np.asarray(lat) * KY


class Dem:
    def __init__(self, folder):
        self.tiles = []
        self.zips = {}
        self.missing = set()
        for path in glob.glob(os.path.join(folder, "*.tif")):
            with rasterio.open(path) as d:
                self.tiles.append((d.bounds, d.transform, d.read(1).astype(np.float32)))

    def height(self, east, north):
        lon, lat = east / KX, north / KY
        out = np.full(east.shape, np.nan)
        for b, t, a in self.tiles:
            sel = (lon >= b.left) & (lon < b.right) & (lat >= b.bottom) & (lat < b.top)
            if not sel.any():
                continue
            fx = (lon[sel] - t.c) / t.a - 0.5
            fy = (lat[sel] - t.f) / t.e - 0.5
            ix = np.clip(np.floor(fx).astype(int), 0, a.shape[1] - 2)
            iy = np.clip(np.floor(fy).astype(int), 0, a.shape[0] - 2)
            ax, ay = np.clip(fx - ix, 0, 1), np.clip(fy - iy, 0, 1)
            out[sel] = (a[iy, ix] * (1 - ax) * (1 - ay) + a[iy, ix + 1] * ax * (1 - ay)
                        + a[iy + 1, ix] * (1 - ax) * ay + a[iy + 1, ix + 1] * ax * ay)
        return out


def offsets():
    right = list(bake_offsets()[bake_offsets() > 0]) + list(np.arange(320.0, 1100.1, 20.0))
    return np.array([-o for o in reversed(right)] + [0.0] + right, np.float64)


bake_offsets = bake.offsets
road.read_route = read_route
realmap.utm32 = local
bake.Dgm = Dem
bake.offsets = offsets

if __name__ == "__main__":
    os.makedirs(road.OUT, exist_ok=True)
    road.main()
    realmap.main()
    bake.main()
