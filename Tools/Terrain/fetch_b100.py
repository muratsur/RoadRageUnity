"""Downloads what build_b100.py needs for the Great Ocean Road, B100, Lavers Hill to Peterborough:
down through the Otways and along the coast past the Twelve Apostles.

    python Tools/Terrain/fetch_b100.py

Writes Tools/Terrain/b100_line.json, the road as [lon, lat] points in driving order, from
OpenStreetMap ((c) OpenStreetMap contributors, ODbL), and to <data>/australia:
b100_osm.json, the same ways with their tags, dem/*.tif, Copernicus DEM GLO-30 (ESA, free to
use with attribution), and out/worldcover.npy, ESA WorldCover. <data> is RR_DATA, or
D:/Projects/RoadRageData. Needs rasterio. Same steps as fetch_ss163.py.
"""
import json
import os
import urllib.request

import numpy as np

import fetch_bc99 as bc99
import fetch_ss163 as ss163

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "australia")
SOUTH, WEST, NORTH, EAST = -38.78, 142.85, -38.55, 143.45
COVER_BOUNDS = (142.55, -39.0, 143.75, -38.3)
QUERY = f"""[out:json][timeout:120];
way["highway"~"^(trunk|primary|secondary)$"]["ref"~"(^|;)B ?100($|;)"]({SOUTH},{WEST},{NORTH},{EAST});
out body geom;"""
DEM_TILES = ("S39_00_E142_00", "S39_00_E143_00")
COVER_TILES = ("S39E141",)
FROM = (-38.6822, 143.3906)   # Lavers Hill
TO = (-38.6034, 142.8780)     # Peterborough


def main():
    import rasterio
    from rasterio.merge import merge

    os.makedirs(os.path.join(DATA, "dem"), exist_ok=True)
    os.makedirs(os.path.join(DATA, "out"), exist_ok=True)
    bc99.QUERY, bc99.FROM, bc99.TO = QUERY, FROM, TO
    osm = bc99.overpass()
    json.dump(osm, open(os.path.join(DATA, "b100_osm.json"), "w"))
    ways = [dict(w, tags={k: v for k, v in w.get("tags", {}).items() if k != "oneway"}) for w in osm["elements"]]
    route = bc99.chain(ways + ss163.bridges(ways))
    length = sum(bc99.metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(ways)} points {len(route)} length {length / 1000:.1f} km", "from", route[0], "to", route[-1])
    assert length > 40000, "the road did not chain up"
    assert bc99.metres(route[-1], TO) < 1500, "the road stopped short of Peterborough"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "b100_line.json"), "w"))
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
