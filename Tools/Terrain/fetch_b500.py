"""Downloads Greenwood's road, the Schwarzwaldhochstrasse (B500) from Baden-Baden to Kniebis and the
B28 on to Freudenstadt,
from OpenStreetMap ((c) OpenStreetMap contributors, ODbL), and writes Tools/Terrain/b500_line.json
as [lon, lat] points in driving order. build_b500_road.py reads it. Same steps as fetch_bc99.py.

    python Tools/Terrain/fetch_b500.py
"""
import json
import os

import fetch_bc99 as bc99
import fetch_ss163 as ss163

HERE = os.path.dirname(os.path.abspath(__file__))
SOUTH, WEST, NORTH, EAST = 48.44, 8.15, 48.78, 8.45
QUERY = f"""[out:json][timeout:120];
way["highway"~"^(trunk|primary|secondary)$"]["ref"~"(^|;)B ?(500|28)($|;)"]({SOUTH},{WEST},{NORTH},{EAST});
out body geom;"""
FROM = (48.76567, 8.22789)   # Baden-Baden, where the drive's first 8.4 km (trimmed off) begin
TO = (48.4646, 8.4181)       # Freudenstadt


def main():
    bc99.QUERY, bc99.FROM, bc99.TO = QUERY, FROM, TO
    osm = bc99.overpass()
    ways = [dict(w, tags={k: v for k, v in w.get("tags", {}).items() if k != "oneway"}) for w in osm["elements"]]
    route = bc99.chain(ways + ss163.bridges(ways))
    length = sum(bc99.metres(a, b) for a, b in zip(route, route[1:]))
    print(f"ways {len(ways)} points {len(route)} length {length / 1000:.1f} km", "from", route[0], "to", route[-1])
    assert length > 45000, "the road did not chain up"
    assert bc99.metres(route[-1], TO) < 1500, "the road stopped short of Freudenstadt"
    json.dump([[lon, lat] for lat, lon in route], open(os.path.join(HERE, "b500_line.json"), "w"))


if __name__ == "__main__":
    main()
