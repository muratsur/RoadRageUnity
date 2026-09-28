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

Output, in Assets/Resources/Biomes/Routes (little endian):
  b500.bytes: char[4] "RRRT", int32 count, float32 step (m of Z between samples),
    count x (float32 x, float32 y)   - road centre at Z = i * step
  b500_cover.bytes: what really lines the road, from ESA WorldCover (10 m):
    char[4] "RRLC", int32 count, int32 bands, float32 step, bands x float32 (band
    start, m from the centreline), then count x [left, right] x bands bytes:
    1 forest, 2 open, 3 built-up, 4 water
  b500_places.txt: "<Z> <name>|<English label>" per named stop (Mummelsee, Ruhestein,
                   ...): the local name for the road signs, the English one for the HUD
plus ./out_b500/preview.png (plan and profile).
"""
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
# The drive is the mountain road: it starts where the forest closes in above
# Geroldsau, past Baden-Baden's streets, and stops short of Freudenstadt's. The
# towns are built-up land the game has no buildings for - it drew them as bare
# ground, and every run started there.
TRIM_START_KM = 8.4
TRIM_END_KM = 2.0
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

# Land cover: ESA WorldCover 2021 v200, 10 m (CC BY 4.0), read as a window of the
# cloud-optimised GeoTIFF over HTTP (needs rasterio).
COVER_URL = ("https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/"
             "ESA_WorldCover_10m_2021_v200_N48E006_Map.tif")
COVER_BOUNDS = (8.15, 48.44, 8.45, 48.78)          # west, south, east, north
COVER_ASSET = os.path.join(ROOT, "Assets", "Resources", "Biomes", "Routes", "b500_cover.bytes")
PLACES_ASSET = os.path.join(ROOT, "Assets", "Resources", "Biomes", "Routes", "b500_places.txt")
COVER_STEP = 10.0                                  # m of Z between cover samples
# Bands out from the centreline, each summarised to one class per side.
COVER_BANDS = ((12.0, 40.0), (40.0, 100.0), (100.0, 220.0))
COVER_WINDOW = 30.0                                # m along the road a sample spans
# WorldCover class -> game class: 1 forest, 2 open (grass, heath, crops, bare,
# wetland), 3 built-up (hotels, villages), 4 water.
COVER_CLASSES = {10: 1, 20: 2, 30: 2, 40: 2, 60: 2, 90: 2, 95: 1, 100: 2, 50: 3, 80: 4}
# Named stops, snapped to the nearest point of the road. Shown as the player
# passes them.
PLACES = [
    ("Schwarzwaldhochstraße B500", 48.7166, 8.2313, "Black Forest High Road B500"),
    ("Bühlerhöhe", 48.6787, 8.2345, "Buhlerhohe Castle"),
    ("Sand", 48.6560, 8.2350, "Sand"),
    ("Hundseck", 48.6450, 8.2210, "Hundseck"),
    ("Unterstmatt", 48.6306, 8.2057, "Unterstmatt Ski Area"),
    ("Mummelsee", 48.5973, 8.2026, "Mummelsee Lake"),
    ("Ruhestein", 48.5598, 8.2245, "Ruhestein Pass"),
    ("Schliffkopf", 48.5406, 8.2199, "Schliffkopf Summit"),
    ("Zuflucht", 48.5019, 8.2267, "Zuflucht Pass"),
    ("Alexanderschanze", 48.4934, 8.2621, "Alexanderschanze Pass"),
    ("Kniebis", 48.4732, 8.2975, "Kniebis"),
    ("Freudenstadt 2 km", 48.4556, 8.4045, "Freudenstadt 2 km"),
]


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
    lat, lon = lat[: end + 1], lon[: end + 1]
    along = np.concatenate([[0.0], np.cumsum([
        float(haversine(np.array([lat[i]]), np.array([lon[i]]), lat[i + 1], lon[i + 1])[0])
        for i in range(len(lat) - 1)])])
    keep = (along >= TRIM_START_KM * 1000) & (along <= along[-1] - TRIM_END_KM * 1000)
    print(f"  trimmed to {along[keep][0] / 1000:.1f}-{along[keep][-1] / 1000:.1f} km of {along[-1] / 1000:.1f}")
    return lat[keep], lon[keep]


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

    cover_and_places(s, e, n, rlat, rlon, z)
    preview(e, n, elev, s, xs, ys, zs)


def cover_and_places(s, e, n, rlat, rlon, z):
    """Land cover beside the road and the named stops, both keyed to road
    distance (Z) the way the game sees it."""
    import rasterio
    from rasterio.windows import from_bounds
    cache = os.path.join(OUT, "worldcover.npy")
    if os.path.exists(cache):
        wc = np.load(cache)
    else:
        with rasterio.Env(GDAL_DISABLE_READDIR_ON_OPEN="EMPTY_DIR",
                          CURL_CA_BUNDLE=os.environ.get("CURL_CA_BUNDLE", "")):
            with rasterio.open("/vsicurl/" + COVER_URL) as ds:
                wc = ds.read(1, window=from_bounds(*COVER_BOUNDS, ds.transform))
        np.save(cache, wc)
    west, south, east, north = COVER_BOUNDS
    res_lat = (north - south) / wc.shape[0]
    res_lon = (east - west) / wc.shape[1]
    lat0 = float(np.mean(rlat))
    te, tn = np.gradient(e), np.gradient(n)
    length = np.hypot(te, tn)
    te, tn = te / length, tn / length

    def cover(i, offset):
        """Class at `offset` m to the right (negative: left) of real sample i."""
        de, dn = tn[i] * offset, -te[i] * offset
        la = rlat[i] + dn / 110540.0
        lo = rlon[i] + de / (111320.0 * math.cos(math.radians(lat0)))
        r = int(np.clip((north - la) / res_lat, 0, wc.shape[0] - 1))
        c = int(np.clip((lo - west) / res_lon, 0, wc.shape[1] - 1))
        return COVER_CLASSES.get(int(wc[r, c]), 1)

    zs = np.arange(0.0, z[-1], COVER_STEP)
    half = int(COVER_WINDOW / 2 / STEP)
    out = np.zeros((len(zs), 2, len(COVER_BANDS)), np.uint8)
    for k, zz in enumerate(zs):
        i = int(np.clip(np.searchsorted(z, zz), 0, len(z) - 1))
        for si, sign in enumerate((-1, 1)):
            for b, (near, far) in enumerate(COVER_BANDS):
                votes = [0] * 5
                for j in range(max(0, i - half), min(len(s), i + half + 1), 2):
                    for off in np.linspace(near, far, 4):
                        votes[cover(j, sign * off)] += 1
                # A lake beside the road is the one thing that must not be
                # outvoted: it is narrow next to the forest around it.
                out[k, si, b] = 4 if votes[4] >= 0.2 * sum(votes) else int(np.argmax(votes))
    with open(COVER_ASSET, "wb") as f:
        f.write(b"RRLC")
        f.write(struct.pack("<iif", len(zs), len(COVER_BANDS), COVER_STEP))
        f.write(struct.pack("<%df" % len(COVER_BANDS), *[b[0] for b in COVER_BANDS]))
        f.write(out.tobytes())                     # [sample][left, right][band]
    names = {1: "forest", 2: "open", 3: "built", 4: "water"}
    share = {names[c]: f"{100 * np.mean(out == c):.0f}%" for c in names}
    print("wrote", COVER_ASSET, os.path.getsize(COVER_ASSET), "bytes", share)

    lines = []
    for name, la, lo, english in PLACES:
        d = haversine(rlat, rlon, la, lo)
        i = int(np.argmin(d))
        lines.append(f"{z[i]:.0f} {name}|{english}")
        print(f"  {name:18s} {s[i] / 1000:5.1f} km real, Z {z[i]:7.0f}, {d[i]:.0f} m from the road")
    open(PLACES_ASSET, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("wrote", PLACES_ASSET)


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


if __name__ == "__main__":
    main()
