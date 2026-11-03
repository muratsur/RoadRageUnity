"""Downloads what build_dn7c.py needs for the Transfagarasan, DN7C, from Cartisoara over the Fagaras
mountains past Balea Lake to the Vidraru dam.

    python Tools/Terrain/fetch_dn7c.py

Writes Tools/Terrain/dn7c_line.json, the road as [lon, lat] points in driving order, from
OpenStreetMap ((c) OpenStreetMap contributors, ODbL), and to <data>/romania:
dn7c_osm.json, the same ways with their tags, dem/*.tif, Copernicus DEM GLO-30 (ESA, free to
use with attribution), and out/worldcover.npy, ESA WorldCover. <data> is RR_DATA, or
D:/Projects/RoadRageData. Needs rasterio. Same steps as fetch_b100.py.
"""
import json
import os
import urllib.request

import numpy as np

import fetch_bc99 as bc99
import fetch_ss163 as ss163

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "romania")
SOUTH, WEST, NORTH, EAST = 45.33, 24.50, 45.76, 24.72
COVER_BOUNDS = (24.25, 45.1, 24.95, 45.98)
QUERY = f"""[out:json][timeout:120];
way["highway"~"^(trunk|primary|secondary)$"]["ref"~"(^|;)DN ?7C($|;)"]({SOUTH},{WEST},{NORTH},{EAST});
out body geom;"""
DEM_TILES = ("N45_00_E024_00", "N44_00_E024_00")
COVER_TILES = ("N45E024", "N42E024")
FROM = (45.7370, 24.5830)     # Cartisoara, where the DN7C leaves the DN1
TO = (45.3670, 24.6330)       # the Vidraru dam


def main():
    import rasterio
    from rasterio.merge import merge

    os.makedirs(os.path.join(DATA, "dem"), exist_ok=True)
    os.makedirs(os.path.join(DATA, "out"), exist_ok=True)
    bc99.QUERY, bc99.FROM, bc99.TO = QUERY, FROM, TO
    osm = bc99.overpass()
    json.dump(osm, open(os.path.join(DATA, "dn7c_osm.json"), "w"))
    ways = [dict(w, tags={k: v for k, v in w.get("tags", {}).items() if k != "oneway"}) for w in osm["elements"]]
    route = bc99.chain(ways + ss163.bridges(ways))
    length = sum(bc99.metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(ways)} points {len(route)} length {length / 1000:.1f} km", "from", route[0], "to", route[-1])
    assert length > 40000, "the road did not chain up"
    assert bc99.metres(route[-1], TO) < 1500, "the road stopped short of the Vidraru dam"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "dn7c_line.json"), "w"))
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
