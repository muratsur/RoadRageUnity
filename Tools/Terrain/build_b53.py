"""Builds the Mosel Valley's road from the real B53 along the Mosel, Piesport to Zell,
with the same rules as Greenwood's B500: this runs build_b500_road.py,
export_b500_realmap.py and bake_b500_terrain.py with their inputs and outputs pointed
at this road.

Inputs:
  Tools/Terrain/b53_line.json: the road as [lon, lat] points, from OpenStreetMap (ref
    "B 53", (c) OpenStreetMap contributors, ODbL).
  D:/Projects/RoadRageData/rlp/dgm1/*.tif, or RR_DGM1=<folder>: Rhineland-Palatinate
    DGM1, 1 m GeoTIFF, 1 km tiles in UTM 32, "(c) GeoBasis-DE / LVermGeoRP, dl-de/by-2-0,
    www.lvermgeo.rlp.de [Daten bearbeitet]".

Land cover needs rasterio, and only on its first run; the window is cached.
"""
import glob
import json
import os
import re

import numpy as np
from PIL import Image

import bake_b500_terrain as bake
import build_b500_road as road
import export_b500_realmap as realmap

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(road.ROOT, "Assets", "Resources", "Biomes", "Routes")
LINE = os.path.join(HERE, "b53_line.json")
DGM = os.environ.get("RR_DGM1", "D:/Projects/RoadRageData/rlp/dgm1")
TRIM_START_KM = 0.5
TRIM_END_KM = 0.5

road.OUT = os.path.join(HERE, "out_b53")
road.ASSET = os.path.join(ROUTES, "b53.bytes")
road.COVER_ASSET = os.path.join(ROUTES, "b53_cover.bytes")
road.PLACES_ASSET = os.path.join(ROUTES, "b53_places.txt")
road.COVER_BOUNDS = (6.85, 49.82, 7.25, 50.07)
road.PLACES = [
    ("Mosel B53", 49.8810, 6.9250, "Mosel Valley B53"),
    ("Brauneberg", 49.9050, 6.9800, "Brauneberg"),
    ("Bernkastel-Kues", 49.9160, 7.0700, "Bernkastel-Kues"),
    ("Zeltingen", 49.9550, 7.0100, "Zeltingen"),
    ("Ürzig", 49.9770, 7.0100, "Urzig"),
    ("Kröv", 49.9820, 7.0850, "Krov"),
    ("Traben-Trarbach", 49.9500, 7.1150, "Traben-Trarbach"),
    ("Reil", 50.0200, 7.1130, "Reil"),
    ("Zell", 50.0280, 7.1800, "Zell"),
]
realmap.OUT = os.path.join(HERE, "b53_realmap.csv")
bake.REALMAP = realmap.OUT
bake.DGM = DGM
bake.ASSET = os.path.join(ROUTES, "b53_terrain.bytes")
bake.PREVIEW = os.path.join(os.path.dirname(DGM), "b53_terrain_preview.png")


def read_route():
    pts = np.array(json.load(open(LINE)))
    lon, lat = pts[:, 0], pts[:, 1]
    along = np.zeros(len(lat))
    for i in range(len(lat) - 1):
        along[i + 1] = along[i] + float(road.haversine(lat[i:i + 1], lon[i:i + 1], lat[i + 1], lon[i + 1])[0])
    keep = (along >= TRIM_START_KM * 1000) & (along <= along[-1] - TRIM_END_KM * 1000)
    print(f"route: {along[keep][0] / 1000:.1f}-{along[keep][-1] / 1000:.1f} km of {along[-1] / 1000:.1f}")
    return lat[keep], lon[keep]


class TifDgm(bake.Dgm):
    """The same 1 km grids, from GeoTIFF tiles instead of LGL's zipped .xyz."""

    def __init__(self, folder):
        self.folder = folder
        self.zips = {}
        self.grids = {}
        self.missing = set()
        self.tiles = {}
        for path in glob.glob(os.path.join(folder, "*.tif")):
            m = re.search(r"_32_(\d{3})_(\d{4})_", path)
            self.tiles[(int(m.group(1)), int(m.group(2)))] = path

    def _load(self, ekm, nkm):
        path = self.tiles.get((ekm, nkm))
        if path is None:
            self.missing.add((ekm, nkm))
            self.grids[(ekm, nkm)] = None
            return
        Image.MAX_IMAGE_PIXELS = None
        # First row is the north edge; the baker's grids start at the south.
        grid = np.flipud(np.asarray(Image.open(path), np.float32)).copy()
        grid[grid < -1000] = np.nan
        self.grids[(ekm, nkm)] = grid


def offsets():
    """The B500's offsets, on out to the far side of the valley: the river is 150 m wide
    and the slopes across it are what the drive looks at."""
    right = list(bake_offsets()[bake_offsets() > 0]) + list(np.arange(320.0, 1100.1, 20.0))
    return np.array([-o for o in reversed(right)] + [0.0] + right, np.float64)


bake_offsets = bake.offsets
road.read_route = read_route
bake.Dgm = TifDgm
bake.offsets = offsets


if __name__ == "__main__":
    os.makedirs(road.OUT, exist_ok=True)
    road.main()
    realmap.main()
    bake.main()
