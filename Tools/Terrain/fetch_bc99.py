"""Downloads what build_bc99.py needs for BC Highway 99, the Sea-to-Sky, Horseshoe Bay to Whistler.

    python Tools/Terrain/fetch_bc99.py

Writes Tools/Terrain/bc99_line.json, the road as [lon, lat] points south to north, from
OpenStreetMap ((c) OpenStreetMap contributors, ODbL), and to <data>/canada:
bc99_osm.json, the same ways with their tags, dem/*.tif, Copernicus DEM GLO-30 (ESA, free to
use with attribution), and out/worldcover.npy, ESA WorldCover. <data> is RR_DATA, or
D:/Projects/RoadRageData. Needs rasterio.
"""
import heapq
import json
import math
import os
import time
import urllib.parse
import urllib.request

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "canada")
SOUTH, WEST, NORTH, EAST = 49.38, -123.35, 50.125, -122.85
COVER_BOUNDS = (-123.55, 49.3, -122.65, 50.2)
QUERY = f"""[out:json][timeout:120];
way["highway"~"^(motorway|trunk|primary)$"]["ref"~"(^|;)(BC )?99($|;)"]({SOUTH},{WEST},{NORTH},{EAST});
out body geom;"""
DEM_TILES = ("N49_00_W124_00", "N49_00_W123_00", "N50_00_W124_00", "N50_00_W123_00")
DEM_URL = "https://copernicus-dem-30m.s3.amazonaws.com/Copernicus_DSM_COG_10_{0}_DEM/Copernicus_DSM_COG_10_{0}_DEM.tif"
COVER_URL = "https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/ESA_WorldCover_10m_2021_v200_{}_Map.tif"
COVER_TILES = ("N48W126", "N48W123")
SERVERS = ("https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
           "https://overpass.private.coffee/api/interpreter")
FROM = (49.3790, -123.2720)
TO = (50.1163, -122.9574)


def overpass():
    for attempt in range(6):
        for url in SERVERS:
            try:
                req = urllib.request.Request(url, data=urllib.parse.urlencode({"data": QUERY}).encode(),
                                             headers={"User-Agent": "RoadRageDev/1.0"})
                return json.loads(urllib.request.urlopen(req, timeout=150).read())
            except Exception as ex:
                print(url, str(ex)[:70])
        time.sleep(15 * (attempt + 1))
    raise SystemExit("OpenStreetMap servers unavailable")


def metres(a, b):
    return math.hypot((a[0] - b[0]) * 110900, (a[1] - b[1]) * 110900 * math.cos(math.radians(a[0])))


def chain(ways):
    """The shortest way from Horseshoe Bay to Whistler. The road is dual in places and one-way
    pieces are only followed in their own direction, so this stays on the northbound side.
    Searched back from Whistler: the point nearest Horseshoe Bay may be on the southbound side,
    which leads nowhere, so the start is the nearest point that does reach Whistler."""
    points, into = {}, {}
    for w in ways:
        ids = w["nodes"]
        points.update(zip(ids, [(g["lat"], g["lon"]) for g in w["geometry"]]))
        for x, y in zip(ids, ids[1:]):
            into.setdefault(y, []).append(x)
            if w.get("tags", {}).get("oneway") != "yes":
                into.setdefault(x, []).append(y)
    finish = min(points, key=lambda i: metres(points[i], TO))
    best, after, heap = {finish: 0.0}, {}, [(0.0, finish)]
    while heap:
        cost, i = heapq.heappop(heap)
        if cost > best[i]:
            continue
        for j in into.get(i, ()):
            c = cost + metres(points[i], points[j])
            if c < best.get(j, 1e18):
                best[j], after[j] = c, i
                heapq.heappush(heap, (c, j))
    route = [min(best, key=lambda i: metres(points[i], FROM))]
    while route[-1] != finish:
        route.append(after[route[-1]])
    return [points[i] for i in route]


def main():
    import rasterio
    from rasterio.merge import merge

    os.makedirs(os.path.join(DATA, "dem"), exist_ok=True)
    os.makedirs(os.path.join(DATA, "out"), exist_ok=True)
    osm = overpass()
    json.dump(osm, open(os.path.join(DATA, "bc99_osm.json"), "w"))
    route = chain(osm["elements"])
    length = sum(metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(osm['elements'])} points {len(route)} length {length / 1000:.1f} km",
          "from", route[0], "to", route[-1])
    assert length > 85000, "the road did not chain up"
    assert route[-1][0] > 50.05, "the road stopped short of Whistler"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "bc99_line.json"), "w"))
    for tile in DEM_TILES:
        path = os.path.join(DATA, "dem", f"{tile}.tif")
        if not os.path.exists(path):
            urllib.request.urlretrieve(DEM_URL.format(tile), path)
        print("DEM", path, os.path.getsize(path) // 1000000, "MB")
    cache = os.path.join(DATA, "out", "worldcover.npy")
    if not os.path.exists(cache):
        tiles = [rasterio.open("/vsicurl/" + COVER_URL.format(t)) for t in COVER_TILES]
        np.save(cache, merge(tiles, bounds=COVER_BOUNDS)[0][0])
    print("cover", np.load(cache).shape)


if __name__ == "__main__":
    main()
