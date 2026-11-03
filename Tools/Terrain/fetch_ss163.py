"""Downloads what build_ss163.py needs for the Amalfi Coast road, SS163 Amalfitana, Positano to
Vietri sul Mare.

    python Tools/Terrain/fetch_ss163.py

Writes Tools/Terrain/ss163_line.json, the road as [lon, lat] points west to east, from
OpenStreetMap ((c) OpenStreetMap contributors, ODbL), and to <data>/italy:
ss163_osm.json, the same ways with their tags, dem/*.tif, Copernicus DEM GLO-30 (ESA, free to
use with attribution), and out/worldcover.npy, ESA WorldCover. <data> is RR_DATA, or
D:/Projects/RoadRageData. Needs rasterio. Same steps as fetch_bc99.py.
"""
import json
import os

import numpy as np

import fetch_bc99 as bc99

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "italy")
SOUTH, WEST, NORTH, EAST = 40.58, 14.38, 40.70, 14.76
COVER_BOUNDS = (14.2, 40.4, 14.95, 40.9)
QUERY = f"""[out:json][timeout:120];
way["highway"~"^(trunk|primary|secondary)$"]["ref"~"(^|;)SS ?163($|;)"]({SOUTH},{WEST},{NORTH},{EAST});
out body geom;"""
DEM_TILES = ("N40_00_E014_00",)
COVER_TILES = ("N39E012",)
FROM = (40.6280, 14.4300)   # where the SS163 leaves the Sorrento road above Positano
TO = (40.6730, 14.7270)     # Vietri sul Mare


def bridges(ways, reach=200.0):
    """Short made-up ways joining pieces of the road that OSM leaves apart: in Maiori the
    SS163 runs along a town street that does not carry the ref."""
    links, points = {}, {}
    for w in ways:
        ids = w["nodes"]
        points.update(zip(ids, [(g["lat"], g["lon"]) for g in w["geometry"]]))
        for a, b in zip(ids, ids[1:]):
            links.setdefault(a, set()).add(b)
            links.setdefault(b, set()).add(a)
    piece, pieces = {}, []
    for start in points:
        if start in piece:
            continue
        stack, members = [start], []
        piece[start] = len(pieces)
        while stack:
            n = stack.pop()
            members.append(n)
            for m in links.get(n, ()):
                if m not in piece:
                    piece[m] = len(pieces)
                    stack.append(m)
        pieces.append(members)
    made = []
    for i in range(len(pieces)):
        for j in range(i + 1, len(pieces)):
            a, b = min(((a, b) for a in pieces[i] for b in pieces[j]),
                       key=lambda p: bc99.metres(points[p[0]], points[p[1]]))
            gap = bc99.metres(points[a], points[b])
            if gap < reach:
                print(f"joined two pieces of the road across {gap:.0f} m at {points[a]}")
                made.append({"nodes": [a, b], "tags": {},
                             "geometry": [{"lat": points[n][0], "lon": points[n][1]} for n in (a, b)]})
    return made


def main():
    import rasterio
    from rasterio.merge import merge
    import urllib.request

    os.makedirs(os.path.join(DATA, "dem"), exist_ok=True)
    os.makedirs(os.path.join(DATA, "out"), exist_ok=True)
    bc99.QUERY, bc99.FROM, bc99.TO = QUERY, FROM, TO
    osm = bc99.overpass()
    json.dump(osm, open(os.path.join(DATA, "ss163_osm.json"), "w"))
    # The few one-way pieces are town couplets that only join up the other way; one line will do.
    ways = [dict(w, tags={k: v for k, v in w.get("tags", {}).items() if k != "oneway"}) for w in osm["elements"]]
    route = bc99.chain(ways + bridges(ways))
    length = sum(bc99.metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(osm['elements'])} points {len(route)} length {length / 1000:.1f} km",
          "from", route[0], "to", route[-1])
    assert length > 35000, "the road did not chain up"
    assert route[-1][1] > 14.7, "the road stopped short of Vietri"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "ss163_line.json"), "w"))
    for tile in DEM_TILES:
        path = os.path.join(DATA, "dem", f"{tile}.tif")
        if not os.path.exists(path):
            urllib.request.urlretrieve(bc99.DEM_URL.format(tile), path)
        print("DEM", path, os.path.getsize(path) // 1000000, "MB")
    cache = os.path.join(DATA, "out", "worldcover.npy")
    if not os.path.exists(cache):
        tiles = [rasterio.open("/vsicurl/" + bc99.COVER_URL.format(t)) for t in COVER_TILES]
        np.save(cache, merge(tiles, bounds=COVER_BOUNDS)[0][0])
    print("cover", np.load(cache).shape)


if __name__ == "__main__":
    main()
