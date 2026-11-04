"""Downloads what build_fdr.py needs for FDR Drive, Manhattan, from East 125th Street down the
East River to the Battery.

    python Tools/Terrain/fetch_fdr.py

Writes Tools/Terrain/fdr_line.json, the southbound road as [lon, lat] points, from OpenStreetMap
((c) OpenStreetMap contributors, ODbL), and to <data>/newyork:
fdr_osm.json, the road's ways with their tags; buildings_osm.json, building footprints with
roof heights from NYC Open Data (see buildings()); dem/*.tif, USGS 3DEP
1 arc-second bare-earth heights (public domain: Copernicus GLO-30 is a surface model and would
turn Manhattan's towers into hills); and out/worldcover.npy, ESA WorldCover. <data> is RR_DATA,
or D:/Projects/RoadRageData. Needs rasterio. Same steps as fetch_b100.py.
"""
import json
import os
import urllib.parse
import urllib.request

import numpy as np

import fetch_bc99 as bc99
import fetch_ss163 as ss163

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.environ.get("RR_DATA", "D:/Projects/RoadRageData"), "newyork")
SOUTH, WEST, NORTH, EAST = 40.695, -74.025, 40.81, -73.92
COVER_BOUNDS = (-74.3, 40.5, -73.65, 41.0)
QUERY = f"""[out:json][timeout:120];
way["highway"~"^(motorway|trunk|primary|motorway_link)$"]["name"~"Roosevelt East River Drive|FDR Drive"]({SOUTH},{WEST},{NORTH},{EAST});
out body geom;"""
# The towers seen from the road: Manhattan, and the Brooklyn and Queens waterfront.
BUILDING_BOX = (40.68, -74.03, 40.82, -73.90)
BUILDING_QUERY = """[out:json][timeout:600][maxsize:1073741824];
way["building"]({},{},{},{});
out tags geom;"""
DEM_TILES = ("USGS_1_n41w074", "USGS_1_n41w075")
DEM_URL = "https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1/TIFF/current/{0}/USGS_1_{0}.tif"
COVER_TILES = ("N39W075",)
FROM = (40.7985, -73.9295)   # East 125th Street, where Harlem River Drive becomes FDR Drive
TO = (40.7032, -74.0128)     # the Battery


TILES = 4    # per side: 16 tiles, each small enough for the public servers


def fetch_tile(query):
    for attempt in range(4):
        for url in bc99.SERVERS:
            try:
                req = urllib.request.Request(url, data=urllib.parse.urlencode({"data": query}).encode(),
                                             headers={"User-Agent": "RoadRageDev/1.0"})
                return json.loads(urllib.request.urlopen(req, timeout=600).read())
            except Exception as ex:
                print("   ", url, str(ex)[:60], flush=True)
    raise SystemExit("OpenStreetMap servers unavailable")


NYC = "https://data.cityofnewyork.us/resource/5zhs-2jue.json"
NEAR_BOX = (40.695, -74.02, 40.805, -73.925)   # along the road and the waterfront opposite
SKYLINE_FEET = 250                              # beyond it, only the towers the far ring shows
PAGE = 20000


def nyc_page(where, offset):
    query = urllib.parse.urlencode({"$select": "doitt_id,height_roof,the_geom", "$where": where,
                                    "$order": "doitt_id", "$limit": PAGE, "$offset": offset})
    for attempt in range(4):
        try:
            return json.loads(urllib.request.urlopen(f"{NYC}?{query}", timeout=900).read())
        except Exception as ex:
            print("   ", str(ex)[:60], flush=True)
    raise SystemExit("NYC Open Data unavailable")


def buildings():
    """Building footprints with roof heights from NYC Open Data (Building Footprints, OTI): OSM's
    public servers time out on Manhattan. Written as OSM-like ways (lat/lon geometry, height in
    metres) so build_fdr.py reads either. Pages already fetched are kept on disk."""
    path = os.path.join(DATA, "buildings_osm.json")
    if os.path.exists(path):
        return
    s, w, n, e = NEAR_BOX
    S, W, N, E = BUILDING_BOX
    found = {}
    for name, where in (("near", f"within_box(the_geom, {n}, {w}, {s}, {e})"),
                        ("tall", f"within_box(the_geom, {N}, {W}, {S}, {E}) AND height_roof > {SKYLINE_FEET}")):
        offset = 0
        while True:
            part = os.path.join(DATA, f"nyc_{name}_{offset}.json")
            if not os.path.exists(part):
                json.dump(nyc_page(where, offset), open(part, "w"))
            rows = json.load(open(part))
            for row in rows:
                geom = row.get("the_geom") or {}
                if geom.get("type") != "MultiPolygon" or not row.get("height_roof"):
                    continue
                ring = geom["coordinates"][0][0]
                found[row["doitt_id"]] = {
                    "geometry": [{"lat": lat, "lon": lon} for lon, lat in ring],
                    "tags": {"height": str(float(row["height_roof"]) * 0.3048)},
                }
            print(f"  {name} buildings from {offset}: {len(found)} so far", flush=True)
            if len(rows) < PAGE:
                break
            offset += PAGE
    json.dump({"elements": list(found.values())}, open(path, "w"))


def main():
    import rasterio
    from rasterio.merge import merge

    os.makedirs(os.path.join(DATA, "dem"), exist_ok=True)
    os.makedirs(os.path.join(DATA, "out"), exist_ok=True)
    bc99.QUERY, bc99.FROM, bc99.TO = QUERY, FROM, TO
    osm = bc99.overpass()
    json.dump(osm, open(os.path.join(DATA, "fdr_osm.json"), "w"))
    # A divided motorway: keep the one-way tags so the chain stays on the southbound side.
    route = bc99.chain(osm["elements"] + ss163.bridges(osm["elements"], 60.0))
    length = sum(bc99.metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(osm['elements'])} points {len(route)} length {length / 1000:.1f} km", "from", route[0], "to", route[-1])
    assert length > 12000, "the road did not chain up"
    assert bc99.metres(route[-1], TO) < 1000, "the road stopped short of the Battery"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "fdr_line.json"), "w"))
    for tile in DEM_TILES:
        path = os.path.join(DATA, "dem", f"{tile}.tif")
        if not os.path.exists(path):
            urllib.request.urlretrieve(DEM_URL.format(tile.split("_")[-1]), path)
        print("DEM", path, os.path.getsize(path) // 1000000, "MB")
    cache = os.path.join(DATA, "out", "worldcover.npy")
    if not os.path.exists(cache):
        tiles = [rasterio.open("/vsicurl/" + bc99.COVER_URL.format(t)) for t in COVER_TILES]
        np.save(cache, merge(tiles, bounds=COVER_BOUNDS)[0][0])
    print("cover", np.load(cache).shape)
    buildings()
    print("buildings", len(json.load(open(os.path.join(DATA, "buildings_osm.json")))["elements"]))


if __name__ == "__main__":
    main()
