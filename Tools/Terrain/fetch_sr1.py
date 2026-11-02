"""Downloads what build_sr1.py needs for California Highway 1 through Big Sur, Carmel to Lucia.

    python Tools/Terrain/fetch_sr1.py

Writes Tools/Terrain/sr1_line.json, the road as [lon, lat] points north to south, from
OpenStreetMap ((c) OpenStreetMap contributors, ODbL), and to <data>/california:
sr1_osm.json, the same ways with their tags, and dem/USGS_13_n37w122.tif, USGS 3DEP 1/3
arc-second (public domain). <data> is RR_DATA, or D:/Projects/RoadRageData.
"""
import json
import math
import os
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "california")
SOUTH, WEST, NORTH, EAST = 36.02, -122.0, 36.53, -121.3
QUERY = f"""[out:json][timeout:120];
way["highway"]["ref"~"(^|;)(CA|SR) ?1($|;)"]({SOUTH},{WEST},{NORTH},{EAST});
out tags geom;"""
DEM_URL = "https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/current/n37w122/USGS_13_n37w122.tif"
SERVERS = ("https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
           "https://overpass.private.coffee/api/interpreter")
LINK_METRES = 40.0


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
    """Walks south from the northernmost end, joining each way to the nearest unused way end."""
    lines = [[(p["lat"], p["lon"]) for p in w["geometry"]] for w in ways if w.get("geometry")]
    at = max((p for line in lines for p in (line[0], line[-1])), key=lambda p: p[0])
    route = []
    while lines:
        best, flip, gap = None, False, LINK_METRES if route else 1e9
        for line in lines:
            for reverse, end in ((False, line[0]), (True, line[-1])):
                d = metres(at, end)
                if d < gap:
                    best, flip, gap = line, reverse, d
        if best is None:
            break
        lines.remove(best)
        route += best[::-1] if flip else best
        at = route[-1]
    return route


def main():
    os.makedirs(os.path.join(DATA, "dem"), exist_ok=True)
    osm = overpass()
    json.dump(osm, open(os.path.join(DATA, "sr1_osm.json"), "w"))
    route = chain(osm["elements"])
    length = sum(metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(osm['elements'])} points {len(route)} length {length / 1000:.1f} km",
          "from", route[0], "to", route[-1])
    assert length > 40000, "the road did not chain up"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "sr1_line.json"), "w"))
    path = os.path.join(DATA, "dem", os.path.basename(DEM_URL))
    if not os.path.exists(path):
        urllib.request.urlretrieve(DEM_URL, path)
    print("DEM", path, os.path.getsize(path) // 1000000, "MB")


if __name__ == "__main__":
    main()
