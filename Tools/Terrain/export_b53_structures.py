"""Writes where the Mosel Valley road's real bridges are, for the game.

    python Tools/Terrain/export_b53_structures.py [sr1|bc99]

With sr1 or bc99: the same for Big Sur's Highway 1 or BC Highway 99, their own bridges only.

Inputs, both from OpenStreetMap ((c) OpenStreetMap contributors, ODbL):
  D:/Projects/RoadRageData/rlp/b53_osm.json: the B53's own ways.
  D:/Projects/RoadRageData/rlp/b53_crossings_osm.json: other roads' bridges nearby.
And Tools/Terrain/b53_realmap.csv, which ties the real map to the game's road distance.

Output: Assets/Resources/Biomes/Routes/b53_structures.txt, one per line:
  deck <from z> <to z>                  the road itself on a bridge
  side <z> <along,lat;...>              a bridge leaving the road across the river
  high <z> <height> <along,lat;...>     a viaduct passing high over the road
Points are metres from the road at z: along the real road's direction, and to its right.
"""
import csv
import json
import os
import re
import sys

import numpy as np

import export_b500_realmap as rm

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = "D:/Projects/RoadRageData/rlp"
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "Routes", "b53_structures.txt")
MIN_DECK = 25.0        # shorter ones are culverts: nothing to see from the road
MERGE_GAP = 25.0
SIDE_MIN_LENGTH = 100.0
SIDE_MAX_GAP = 40.0
HIGH_MIN_LENGTH = 1000.0
HIGH_MAX_GAP = 60.0
# The Hochmoselbruecke stands 158 m over the river, and the road runs a little above that.
HIGH_HEIGHT = 150.0

if sys.argv[1:] == ["sr1"]:
    import build_sr1  # noqa: F401 - points the map projection at California
    NAME, DATA, CROSSINGS = "sr1", build_sr1.DATA, None
elif sys.argv[1:] == ["bc99"]:
    import build_bc99  # noqa: F401 - points the map projection at British Columbia
    NAME, DATA, CROSSINGS = "bc99", build_bc99.DATA, None
else:
    NAME, CROSSINGS = "b53", f"{DATA}/b53_crossings_osm.json"
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Biomes", "Routes", f"{NAME}_structures.txt")

rows = np.array([[float(x) for x in r] for r in list(csv.reader(open(os.path.join(HERE, f"{NAME}_realmap.csv"))))[1:]])
z, east, north, dir_e, dir_n = rows.T


def points(way):
    g = way["geometry"]
    e, n = rm.utm32(np.array([p["lat"] for p in g]), np.array([p["lon"] for p in g]))
    return e, n


def densify(e, n, step=5.0):
    out_e, out_n = [e[0]], [n[0]]
    for i in range(len(e) - 1):
        k = max(1, int(np.hypot(e[i + 1] - e[i], n[i + 1] - n[i]) / step))
        for f in np.arange(1, k + 1) / k:
            out_e.append(e[i] + (e[i + 1] - e[i]) * f)
            out_n.append(n[i] + (n[i + 1] - n[i]) * f)
    return np.array(out_e), np.array(out_n)


def nearest(e, n):
    """(index on the road, distance) of the point of this line closest to the road."""
    best, at = 1e18, 0
    for a, b in zip(*densify(e, n)):
        d = (east - a) ** 2 + (north - b) ** 2
        i = int(d.argmin())
        if d[i] < best:
            best, at = float(d[i]), i
    return at, best ** 0.5


def relative(e, n, i):
    along = (e - east[i]) * dir_e[i] + (n - north[i]) * dir_n[i]
    lat = (e - east[i]) * dir_n[i] - (n - north[i]) * dir_e[i]
    return ";".join(f"{a:.1f},{l:.1f}" for a, l in zip(along, lat))


lines = []
spans = []
for way in json.load(open(f"{DATA}/{NAME}_osm.json"))["elements"]:
    if "bridge" not in (way.get("tags") or {}) or not way.get("geometry"):
        continue
    e, n = points(way)
    idx = [int(((east - a) ** 2 + (north - b) ** 2).argmin()) for a, b in zip(e, n)]
    if max(np.hypot(east[i] - a, north[i] - b) for i, a, b in zip(idx, e, n)) < 15.0:
        spans.append((z[min(idx)], z[max(idx)]))
spans.sort()
merged = []
for a, b in spans:
    if merged and a - merged[-1][1] < MERGE_GAP:
        merged[-1][1] = max(merged[-1][1], b)
    else:
        merged.append([a, b])
for a, b in merged:
    if b - a >= MIN_DECK:
        lines.append(f"deck {a:.0f} {b:.0f}")

seen = []
for way in (json.load(open(CROSSINGS))["elements"] if CROSSINGS else []):
    t = way.get("tags") or {}
    if "bridge" not in t or "highway" not in t or re.search(r"B ?53\b", t.get("ref", "")):
        continue
    e, n = points(way)
    length = float(np.hypot(np.diff(e), np.diff(n)).sum())
    i, gap = nearest(e, n)
    if length >= HIGH_MIN_LENGTH and gap < HIGH_MAX_GAP:
        lines.append(f"high {z[i]:.0f} {HIGH_HEIGHT:.0f} {relative(e, n, i)}")
    elif length >= SIDE_MIN_LENGTH and gap < SIDE_MAX_GAP and not any(abs(z[i] - s) < 60 for s in seen):
        # Road end first.
        if np.hypot(e[0] - east[i], n[0] - north[i]) > np.hypot(e[-1] - east[i], n[-1] - north[i]):
            e, n = e[::-1], n[::-1]
        seen.append(z[i])
        lines.append(f"side {z[i]:.0f} {relative(e, n, i)}")

open(OUT, "w", newline="\n").write("\n".join(lines) + "\n")
for line in lines:
    print(line[:70])
assert any(line.startswith("deck") for line in lines), "no decks found"
