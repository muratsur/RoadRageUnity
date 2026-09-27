"""Builds Greenwood's road from the real Schwarzwaldhochstrasse (B500), Baden-Baden
to Freudenstadt: the real sequence of bends and the real hills, fitted to what the
game's road can be.

Input: Tools/Terrain/b500.kml, a Google My Maps directions export. Its first leg is
Baden-Baden -> Freudenstadt along the B500 (past Buehlerhoehe, the Mummelsee,
Ruhestein and Kniebis). The export carries further legs to a wrongly matched
"Buehler Hoehe" in Dornhan and back, so only the first leg is used: the line is cut
where it first reaches Freudenstadt.

Elevation: AWS Terrain Tiles (EU-DEM / SRTM, terrarium PNG), sampled along the line.

What the game can take, and what is changed to fit:
  * Road distance is world Z, so the road can never turn back on itself. Heading
    is limited to +-MAX_HEADING from straight ahead and eased back towards it over
    RESTORE_LENGTH, so every bend keeps its real sharpness and direction but a
    hairpin opens out into a sweeping switchback.
  * Tightest radius MIN_RADIUS: roadside bands reach ~30 m out, and a tighter bend
    folds them over on its inside.
  * The horizon ring sits at a fixed height, so the real profile (~200 m at
    Baden-Baden to ~1000 m on the ridge) is scaled by ELEV_SCALE and has its
    trends longer than TREND_LENGTH taken out, staying within MAX_RISE and
    MAX_GRADE.
  * Both ends are eased flat and straight so the game can run the route there
    and back (Freudenstadt -> Baden-Baden) without a kink at the turnaround.

Output: Assets/Resources/Biomes/Routes/b500.bytes (little endian):
    char[4] "RRRT", int32 count, float32 step (m of Z between samples),
    count x (float32 x, float32 y)   - road centre at Z = i * step
plus ./out_b500/preview.png (plan and profile).
"""
import io
import math
import os
import struct
import sys
import urllib.request
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
KML = os.path.join(HERE, "b500.kml")
OUT = os.environ.get("RR_OUT", os.path.join(HERE, "out_b500"))
ASSET = os.path.join(ROOT, "Assets", "Resources", "Biomes", "Routes", "b500.bytes")
os.makedirs(OUT, exist_ok=True)

END_NEAR = (48.4646, 8.4181)        # Freudenstadt: the first leg ends here
END_RADIUS = 400.0                  # m
STEP = 5.0                          # m, resampling along the real road
OUT_STEP = 4.0                      # m of Z between output samples
MAX_HEADING = math.radians(42.0)
RESTORE_LENGTH = 450.0              # m over which the heading eases back to straight
MIN_RADIUS = 55.0                   # m
CURVE_SMOOTH = 25.0                 # m, removes GPS jitter from the curvature
TREND_LENGTH = 7000.0               # m, longer trends than this are removed
ELEV_SCALE = 0.35
ELEV_SMOOTH = 250.0                 # m, DEM noise (cuttings, embankments)
MAX_GRADE = 0.06
MAX_RISE = 30.0                     # m, either side of the horizon's height
END_EASE = 300.0                    # m, flat and straight at both ends
ZOOM = 14
TILE_URL = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png"


def read_route():
    ns = {"k": "http://www.opengis.net/kml/2.2"}
    root = ET.parse(KML).getroot()
    lines = [e.text for e in root.iter("{%s}coordinates" % ns["k"])
             if e.text and len(e.text.split()) > 10]
    if not lines:
        sys.exit("no LineString in " + KML)
    pts = np.array([[float(v) for v in c.split(",")[:2]] for c in lines[0].split()])
    lon, lat = pts[:, 0], pts[:, 1]
    d = haversine(lat, lon, *END_NEAR)
    hits = np.nonzero(d < END_RADIUS)[0]
    end = int(hits[0]) if len(hits) else int(np.argmin(d[: len(d) // 2]))
    # Keep going to the closest point of that first approach.
    while end + 1 < len(d) and d[end + 1] < d[end]:
        end += 1
    print(f"route: {len(pts)} points, first leg ends at {end}")
    return lat[: end + 1], lon[: end + 1]


def haversine(lat, lon, lat0, lon0):
    r = 6371000.0
    p1, p2 = np.radians(lat), math.radians(lat0)
    dp, dl = p2 - p1, np.radians(lon0 - lon)
    a = np.sin(dp / 2) ** 2 + np.cos(p1) * math.cos(p2) * np.sin(dl / 2) ** 2
    return 2 * r * np.arcsin(np.sqrt(a))


def mercator(lat, lon, z=ZOOM):
    n = 2 ** z
    x = (lon + 180.0) / 360.0 * n
    y = (1 - np.log(np.tan(np.radians(lat)) + 1 / np.cos(np.radians(lat))) / math.pi) / 2 * n
    return x, y


tiles = {}


def tile(x, y):
    key = (x, y)
    if key not in tiles:
        path = os.path.join(OUT, f"t{ZOOM}_{x}_{y}.png")
        if not os.path.exists(path):
            with urllib.request.urlopen(TILE_URL.format(z=ZOOM, x=x, y=y), timeout=30) as r:
                open(path, "wb").write(r.read())
        a = np.asarray(Image.open(path).convert("RGB"), np.float64)
        tiles[key] = a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768
    return tiles[key]


def elevation(lat, lon):
    fx, fy = mercator(lat, lon)
    out = np.empty(len(lat))
    for i, (x, y) in enumerate(zip(fx, fy)):
        tx, ty = int(x), int(y)
        px, py = (x - tx) * 256 - 0.5, (y - ty) * 256 - 0.5
        ix, iy = int(math.floor(px)), int(math.floor(py))
        ax, ay = px - ix, py - iy
        ix, iy = min(max(ix, 0), 254), min(max(iy, 0), 254)
        t = tile(tx, ty)
        v = (t[iy, ix] * (1 - ax) * (1 - ay) + t[iy, ix + 1] * ax * (1 - ay)
             + t[iy + 1, ix] * (1 - ax) * ay + t[iy + 1, ix + 1] * ax * ay)
        out[i] = v
    return out


def smooth(a, metres, step):
    k = max(1, int(round(metres / step)))
    if k < 2:
        return a.copy()
    pad = np.concatenate([np.full(k, a[0]), a, np.full(k, a[-1])])
    kern = np.ones(k) / k
    return np.convolve(pad, kern, mode="same")[k:-k]


def resample(lat, lon):
    lat0 = float(np.mean(lat))
    e = (lon - lon[0]) * 111320.0 * math.cos(math.radians(lat0))
    n = (lat - lat[0]) * 110540.0
    seg = np.hypot(np.diff(e), np.diff(n))
    s = np.concatenate([[0.0], np.cumsum(seg)])
    keep = np.concatenate([[True], seg > 0.01])
    s, e, n, lat, lon = s[keep], e[keep], n[keep], lat[keep], lon[keep]
    grid = np.arange(0.0, s[-1], STEP)
    return (grid, np.interp(grid, s, e), np.interp(grid, s, n),
            np.interp(grid, s, lat), np.interp(grid, s, lon))


def ease(i, count, metres):
    k = metres / STEP
    t = min(i / k, (count - 1 - i) / k, 1.0)
    return t * t * (3 - 2 * t)


def main():
    lat, lon = read_route()
    s, e, n, rlat, rlon = resample(lat, lon)
    print(f"real length {s[-1] / 1000:.1f} km")

    # Real heading and curvature (per metre), smoothed.
    heading = np.unwrap(np.arctan2(np.gradient(e), np.gradient(n)))   # clockwise from north
    curvature = smooth(np.gradient(heading) / STEP, CURVE_SMOOTH, STEP)

    elev = smooth(elevation(rlat, rlon), ELEV_SMOOTH, STEP)
    print(f"elevation {elev.min():.0f} .. {elev.max():.0f} m")

    # Game heading: the real curvature, limited, eased back towards straight ahead.
    count = len(s)
    phi = np.zeros(count)
    for i in range(1, count):
        k = float(np.clip(curvature[i], -1 / MIN_RADIUS, 1 / MIN_RADIUS))
        p = phi[i - 1] + k * STEP
        p -= p * STEP / RESTORE_LENGTH
        phi[i] = float(np.clip(p, -MAX_HEADING, MAX_HEADING)) * ease(i, count, END_EASE)

    # Hills: the real profile scaled down, less its longest trend. The ring's
    # fixed height rules out the full 900 m, but taking out everything longer than
    # a couple of km left DEM noise as a roller coaster: the ridge road's climbs
    # and dips are long and gentle. So the real profile is scaled (a 7 % climb
    # stays a readable ~2.5 %), and only the trend beyond TREND_LENGTH is removed
    # to keep it within MAX_RISE of the horizon.
    y = (elev - smooth(elev, TREND_LENGTH, STEP)) * ELEV_SCALE
    y = MAX_RISE * np.tanh(y / MAX_RISE)
    grade = np.clip(np.gradient(y) / STEP, -MAX_GRADE, MAX_GRADE)
    y = np.concatenate([[0.0], np.cumsum(grade[1:] * STEP)])
    # Both ends flat for the there-and-back turnaround: ease into the level the
    # road has where the end stretch begins.
    k = int(END_EASE / STEP)
    w = np.array([ease(i, count, END_EASE) for i in range(count)])
    ya, yb = y[k], y[count - 1 - k]
    y[:k] = ya + (y[:k] - ya) * w[:k]
    y[count - k:] = yb + (y[count - k:] - yb) * w[count - k:]
    y -= y[0]

    # Integrate the game road: Z forward, X to the right.
    x = np.concatenate([[0.0], np.cumsum(np.sin(phi[1:]) * STEP)])
    z = np.concatenate([[0.0], np.cumsum(np.cos(phi[1:]) * STEP)])
    zs = np.arange(0.0, z[-1], OUT_STEP)
    xs = np.interp(zs, z, x)
    ys = np.interp(zs, z, y)
    print(f"game length {zs[-1] / 1000:.1f} km of Z, x {xs.min():.0f}..{xs.max():.0f} m, "
          f"y {ys.min():.1f}..{ys.max():.1f} m")
    radius = 1 / max(1e-6, np.max(np.abs(np.gradient(phi) / STEP)))
    print(f"tightest game radius {radius:.0f} m, "
          f"max heading {math.degrees(np.max(np.abs(phi))):.0f} deg, "
          f"max grade {np.max(np.abs(np.gradient(ys) / OUT_STEP)) * 100:.1f} %")

    os.makedirs(os.path.dirname(ASSET), exist_ok=True)
    with open(ASSET, "wb") as f:
        f.write(b"RRRT")
        f.write(struct.pack("<if", len(zs), OUT_STEP))
        f.write(np.stack([xs, ys], axis=1).astype("<f4").tobytes())
    print("wrote", ASSET, os.path.getsize(ASSET), "bytes")

    preview(e, n, elev, s, xs, ys, zs)


def preview(e, n, elev, s, xs, ys, zs):
    W, H = 1600, 1000
    img = Image.new("RGB", (W, H), (245, 245, 240))
    d = ImageDraw.Draw(img)
    # Left: the real road in plan.
    def plot(px, py, box, colour):
        x0, y0, x1, y1 = box
        sx = (x1 - x0) / max(1e-6, px.max() - px.min())
        sy = (y1 - y0) / max(1e-6, py.max() - py.min())
        k = min(sx, sy)
        pts = [(x0 + (a - px.min()) * k, y1 - (b - py.min()) * k) for a, b in zip(px[::4], py[::4])]
        d.line(pts, fill=colour, width=2)
    plot(e, n, (20, 40, 520, 960), (40, 90, 40))
    d.text((20, 10), "real B500 (plan)", fill=(0, 0, 0))
    # Middle: the game road in plan, Z up.
    plot(xs, zs, (560, 40, 1060, 960), (60, 60, 160))
    d.text((560, 10), "game road (plan, Z up)", fill=(0, 0, 0))
    # Right: elevation, real (grey) and game (blue), along the road.
    def profile(v, top, colour, label):
        d.text((1100, top - 20), label, fill=(0, 0, 0))
        xs_ = np.linspace(1100, 1580, len(v))
        lo, hi = v.min(), v.max()
        pts = [(a, top + 180 - (b - lo) / max(1e-6, hi - lo) * 180) for a, b in zip(xs_[::4], v[::4])]
        d.line(pts, fill=colour, width=2)
        d.text((1100, top + 185), f"{lo:.0f} .. {hi:.0f} m", fill=(0, 0, 0))
    profile(elev, 60, (120, 120, 120), "real elevation")
    profile(ys, 360, (60, 60, 160), "game elevation")
    img.save(os.path.join(OUT, "preview.png"))


main()
