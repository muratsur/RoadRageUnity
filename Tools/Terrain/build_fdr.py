"""Builds the FDR Drive biome from the real FDR Drive, Manhattan: East 125th Street down the East
River to the Battery, with the same rules as build_dn7c.py: this runs build_b500_road.py,
export_b500_realmap.py and bake_b500_terrain.py with their inputs and outputs pointed at this road.

Inputs come from fetch_fdr.py. "east" and "north" here are a local flat projection. The road runs
a few metres above the East River: one sea level, land at its real height above it. Tunnels
become cuttings as in build_dn7c.py.

Also bakes the city (`buildings`): every building near the road (NYC Open Data, via fetch_fdr.py) as a footprint in road
distance and lateral offset, with its height, for the game to extrude; and (`skyline`) the far
ring with building heights added to the ground, so Midtown stands on the horizon.
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
import fetch_fdr as fetch

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(road.ROOT, "Assets", "Resources", "Biomes", "Routes")
LINE = os.path.join(HERE, "fdr_line.json")
DATA = fetch.DATA
LAT0 = 40.75
KX = 111320.0 * math.cos(math.radians(LAT0))
KY = 111050.0

road.OUT = os.path.join(DATA, "out")
road.ASSET = os.path.join(ROUTES, "fdr.bytes")
road.COVER_ASSET = os.path.join(ROUTES, "fdr_cover.bytes")
road.PLACES_ASSET = os.path.join(ROUTES, "fdr_places.txt")
road.COVER_BOUNDS = fetch.COVER_BOUNDS
road.PLACES = [
    ("East 125th Street", 40.7985, -73.9295, "East 125th Street"),
    ("Gracie Mansion", 40.7760, -73.9430, "Gracie Mansion"),
    ("Queensboro Bridge", 40.7570, -73.9590, "Queensboro Bridge"),
    ("United Nations", 40.7489, -73.9680, "United Nations"),
    ("Williamsburg Bridge", 40.7135, -73.9760, "Williamsburg Bridge"),
    ("Manhattan Bridge", 40.7076, -73.9907, "Manhattan Bridge"),
    ("Brooklyn Bridge", 40.7061, -73.9969, "Brooklyn Bridge"),
    ("South Street Seaport", 40.7060, -74.0030, "South Street Seaport"),
    ("The Battery", 40.7032, -74.0128, "The Battery"),
]
realmap.OUT = os.path.join(HERE, "fdr_realmap.csv")
bake.REALMAP = realmap.OUT
bake.DGM = os.path.join(DATA, "dem")
bake.ASSET = os.path.join(ROUTES, "fdr_terrain.bytes")
bake.PREVIEW = os.path.join(DATA, "fdr_terrain_preview.png")
bake.ROAD_HALF = 20.0   # the DEM has 30 m cells
DECK_MARGIN = 20.0
TUNNEL_MARGIN = 40.0    # m of cutting beyond each portal
TUNNEL_FLOOR = 14.0     # m either side of the centreline kept level with the road
TUNNEL_SLOPE = 1.2      # rise per metre out from there
GROUND_LIMIT = 900.0    # the game's SeaToSkyGroundLimit: it squeezes heights by tanh
SEA_HEIGHT = 1.5        # m: the DEM has the sea at about 0
SEA_BELOW_ROAD = 6.0    # m under the road's lowest point: FDR Drive runs just above the river
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
    path = os.path.join(ROUTES, "fdr_structures.txt")
    for line in open(path) if os.path.exists(path) else ():
        f = line.split()
        if not f or f[0] not in ("deck", "cut"):
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
    # The river is always on the left; Manhattan's landfill on the right is as low as the
    # river in the DEM (0.4-1.7 m) and became strips of water beside the road.
    water = (ground < SEA_HEIGHT) & (offs < 0.0)[None, :]
    coast = np.convolve(water.any(axis=1).astype(float), np.ones(COAST_EASE_ROWS * 2 + 1), "same") > 0
    coast = np.convolve(coast.astype(float), np.ones(COAST_EASE_ROWS * 2 + 1) / (COAST_EASE_ROWS * 2 + 1), "same")
    world = coast[:, None] * coast_world + (1.0 - coast[:, None]) * inland_world
    world = np.maximum(world, sea_level + LAND_ABOVE_SEA)
    world[water] = sea_level - SEA_BED
    rel = world - game_y[:, None]
    path = os.path.join(ROUTES, "fdr_structures.txt")
    # Through a tunnel's span: a V-shaped cutting, level with the road at its foot.
    for line in open(path) if os.path.exists(path) else ():
        f = line.split()
        if f and f[0] == "cut":
            rows = (z >= float(f[1]) - TUNNEL_MARGIN) & (z <= float(f[2]) + TUNNEL_MARGIN)
            wall = np.maximum(0.0, np.abs(offs) - TUNNEL_FLOOR) * TUNNEL_SLOPE
            rel[rows] = np.minimum(rel[rows], wall[None, :])
    # Undo the squeeze the game will apply, so the sea stays flat. Hills may stay squeezed.
    below = rel < 0.0
    rel[below] = GROUND_LIMIT * np.arctanh(np.clip(rel[below] / GROUND_LIMIT, -0.95, 0.0))
    lines =[l for l in open(path).read().split("\n") if l and not l.startswith("sea ")]
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
# Decimetres: the towers in the skyline stand far more than the 327 m centimetres can hold.
bake.UNITS_PER_METRE, bake.MAGIC = 10.0, b"RRTD"

BUILDINGS_ASSET = os.path.join(ROUTES, "fdr_buildings.bytes")
BUILDING_REACH = 1150.0     # m out from the road; the skyline ring takes over past the ground
BUILDING_CLEAR = 18.0       # m: nothing closer to the centreline (FDR runs under some buildings)
LEVEL_HEIGHT = 3.2
DEFAULT_HEIGHT = 12.0
SKYLINE_CELL = 15.0         # m, the building height raster for the far ring


def building_height(tags):
    for key in ("height", "building:height"):
        try:
            return float(str(tags.get(key, "")).replace("m", "").strip())
        except ValueError:
            pass
    try:
        return float(tags.get("building:levels")) * LEVEL_HEIGHT + 1.0
    except (TypeError, ValueError):
        return DEFAULT_HEIGHT


def footprints():
    """(east, north) arrays and height of every OSM building, in the local projection."""
    for way in json.load(open(os.path.join(DATA, "buildings_osm.json")))["elements"]:
        geom = way.get("geometry") or []
        if len(geom) < 4:
            continue
        lon = np.array([g["lon"] for g in geom])
        lat = np.array([g["lat"] for g in geom])
        e, n = local(lat, lon)
        yield e[:-1], n[:-1], building_height(way.get("tags") or {})


def bake_buildings():
    """FDRB: int count, then per building: float height, byte corners, corners x (float z, float
    lateral), in road distance and offset from the centreline, as the terrain table is."""
    rm = np.loadtxt(realmap.OUT, delimiter=",", skiprows=1)
    z, east, north, de, dn = rm.T
    out, kept, cleared = [], 0, 0
    for e, n, h in footprints():
        if len(e) > 255:
            continue
        # Nearest point of road for each corner.
        d2 = (e[:, None] - east[None, :]) ** 2 + (n[:, None] - north[None, :]) ** 2
        i = d2.argmin(axis=1)
        along = (e - east[i]) * de[i] + (n - north[i]) * dn[i]
        lat = (e - east[i]) * dn[i] - (n - north[i]) * de[i]
        if np.abs(lat).min() > BUILDING_REACH:
            continue
        if np.abs(lat).min() < BUILDING_CLEAR:
            cleared += 1
            continue
        zz = z[i] + along
        out.append(struct.pack("<fB", h, len(e)) + np.column_stack([zz, lat]).astype("<f4").tobytes())
        kept += 1
    with open(BUILDINGS_ASSET, "wb") as f:
        f.write(b"FDRB")
        f.write(struct.pack("<i", kept))
        for b in out:
            f.write(b)
    print(f"wrote {BUILDINGS_ASSET}: {kept} buildings ({cleared} over the road left out), "
          f"{os.path.getsize(BUILDINGS_ASSET) // 1024} KB")


def city_height(dem):
    """Ground plus buildings, for the far ring: the tallest building in each SKYLINE_CELL."""
    fps = list(footprints())
    e0 = min(f[0].min() for f in fps)
    n0 = min(f[1].min() for f in fps)
    w = int((max(f[0].max() for f in fps) - e0) / SKYLINE_CELL) + 2
    hgt = int((max(f[1].max() for f in fps) - n0) / SKYLINE_CELL) + 2
    grid = np.zeros((hgt, w), np.float32)
    for e, n, h in fps:
        x0, x1 = int((e.min() - e0) / SKYLINE_CELL), int((e.max() - e0) / SKYLINE_CELL)
        y0, y1 = int((n.min() - n0) / SKYLINE_CELL), int((n.max() - n0) / SKYLINE_CELL)
        grid[y0:y1 + 1, x0:x1 + 1] = np.maximum(grid[y0:y1 + 1, x0:x1 + 1], h)

    def height(east, north):
        x = ((east - e0) / SKYLINE_CELL).astype(int)
        y = ((north - n0) / SKYLINE_CELL).astype(int)
        inside = (x >= 0) & (x < w) & (y >= 0) & (y < hgt)
        tower = np.where(inside, grid[np.clip(y, 0, hgt - 1), np.clip(x, 0, w - 1)], 0.0)
        return dem.height(east, north) + tower
    return height

if __name__ == "__main__":
    os.makedirs(road.OUT, exist_ok=True)
    # "skyline": only the far mountains (bake_skyline.py), from the road's realmap.
    if sys.argv[1:] == ["skyline"]:
        skyline.main(city_height(Dem(bake.DGM)), realmap.OUT, os.path.join(ROUTES, "fdr_skyline.bytes"))
        sys.exit()
    if sys.argv[1:] == ["buildings"]:
        bake_buildings()
        sys.exit()
    # "road": only the road and its realmap, before export_b53_structures.py fdr can run.
    if sys.argv[1:] == ["road"]:
        road.main()
        realmap.main()
        sys.exit()
    # "terrain": only the ground, which needs the structures from the road's first bake.
    if sys.argv[1:] != ["terrain"]:
        road.main()
        realmap.main()
    bake.main()
