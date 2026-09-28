"""Bakes the real ground beside the B500 into a height table the game can read, from
the Baden-Wuerttemberg DGM1 (1 m digital terrain model, LGL open data,
dl-de/by-2.0: "Datenquelle: LGL, www.lgl-bw.de").

The game road is the real B500 with its bends capped and eased, so the terrain is
taken relative to the road: for each point of the game road (every Z_STEP m of road
distance) and each lateral offset, the real ground height at that offset beside the
real road, minus the real road's own height there. The slopes, cuttings, banks and
valleys beside the road keep their real shape and size; where the road itself goes
up or down is still the game's own profile.

Road Rage > Bake B500 Terrain (Assets/Editor/BakeB500TerrainMenu.cs) does the same
bake inside Unity with nothing to install; this script is the same thing for a
machine with Python. Needs only numpy and the standard library, so a plain Python or
Blender's bundled one:
    py -3 Tools\\Terrain\\bake_b500_terrain.py
    "C:\\Program Files\\Blender Foundation\\Blender 4.x\\blender.exe" -b -P Tools\\Terrain\\bake_b500_terrain.py

Input:
  Tools/Terrain/b500_realmap.csv (export_b500_realmap.py): game Z -> real position.
  ../RoadRageData/lgl/dgm1/*.zip (download_lgl_b500.ps1), or RR_DGM1=<folder>.
  The zips hold ASCII .xyz files, "east north height" per 1 m cell; each is read
  once and cached as .npy beside the zips.

Output:
  Assets/Resources/Biomes/Routes/b500_terrain.bytes (little endian):
    char[4] "RRTR", int32 rows, float32 z_step, int32 cols,
    cols x float32 lateral offset (m, right of the road positive),
    rows x cols x int16 height relative to the road, centimetres.
  <DGM1 folder>/../b500_terrain_preview.png: the table as a shaded relief,
    road distance downwards, left to right across the road.
"""
import io
import math
import os
import re
import struct
import sys
import zipfile
import zlib

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
REALMAP = os.path.join(HERE, "b500_realmap.csv")
DATA = os.path.abspath(os.path.join(ROOT, "..", "RoadRageData", "lgl"))
DGM = os.environ.get("RR_DGM1", os.path.join(DATA, "dgm1"))
ASSET = os.path.join(ROOT, "Assets", "Resources", "Biomes", "Routes", "b500_terrain.bytes")
# Next to the DGM1 folder, wherever that is.
PREVIEW = os.path.join(os.path.dirname(os.path.abspath(DGM)), "b500_terrain_preview.png")

Z_STEP = 4.0            # m of road distance between rows, as b500.bytes
ROAD_HALF = 3.0         # m either side of the centre averaged for the road's own height


def offsets():
    """Lateral sample positions: dense by the road, where banks and cuttings are
    seen up close, sparser out to the valley sides."""
    right = list(np.arange(2.0, 40.0, 2.0)) + list(np.arange(40.0, 120.0, 5.0)) + \
        list(np.arange(120.0, 300.1, 10.0))
    return np.array([-o for o in reversed(right)] + [0.0] + right, np.float64)


class Dgm:
    """The DGM1 tiles as 1 km grids, loaded on first use."""

    def __init__(self, folder):
        self.folder = folder
        self.cache = os.path.join(folder, "npy")
        os.makedirs(self.cache, exist_ok=True)
        self.zips = {}
        for name in os.listdir(folder):
            m = re.search(r"_32_(\d{3})_(\d{4})", name)
            if m and name.lower().endswith(".zip"):
                self.zips[(int(m.group(1)), int(m.group(2)))] = os.path.join(folder, name)
        if not self.zips:
            sys.exit(f"No DGM1 zips (…_32_<east>_<north>….zip) in {folder}. "
                     "Run Tools/Terrain/download_lgl_b500.ps1 first, or set RR_DGM1.")
        self.grids = {}
        self.missing = set()

    def _zip_for(self, ekm, nkm):
        for (e, n), path in self.zips.items():
            if e <= ekm < e + 2 and n <= nkm < n + 2:
                return path
        return None

    def _load(self, ekm, nkm):
        key = (ekm, nkm)
        path = os.path.join(self.cache, f"dgm1_{ekm}_{nkm}.npy")
        if os.path.exists(path):
            self.grids[key] = np.load(path)
            return
        zpath = self._zip_for(ekm, nkm)
        if zpath is None:
            self.missing.add(key)
            self.grids[key] = None
            return
        print(f"  reading {os.path.basename(zpath)} ...", flush=True)
        with zipfile.ZipFile(zpath) as z:
            for member in z.namelist():
                if not member.lower().endswith(".xyz"):
                    continue
                raw = z.read(member).replace(b",", b" ").replace(b";", b" ").replace(b"\t", b" ")
                values = np.array(raw.split(), dtype=np.float64)
                pts = values[: len(values) // 3 * 3].reshape(-1, 3)
                # Split into 1 km cells (a file may hold one or several).
                ce = np.floor(pts[:, 0] / 1000.0).astype(int)
                cn = np.floor(pts[:, 1] / 1000.0).astype(int)
                for (e, n) in set(zip(ce.tolist(), cn.tolist())):
                    sel = (ce == e) & (cn == n)
                    p = pts[sel]
                    grid = np.full((1000, 1000), np.nan, np.float32)
                    ix = np.clip(np.floor(p[:, 0] - e * 1000).astype(int), 0, 999)
                    iy = np.clip(np.floor(p[:, 1] - n * 1000).astype(int), 0, 999)
                    grid[iy, ix] = p[:, 2]
                    np.save(os.path.join(self.cache, f"dgm1_{e}_{n}.npy"), grid)
                    self.grids[(e, n)] = grid
        if key not in self.grids:
            self.missing.add(key)
            self.grids[key] = None

    def height(self, east, north):
        """Bilinear height at the given UTM positions (arrays); NaN where no data."""
        out = np.full(east.shape, np.nan)
        # Cell centres are at +0.5 m.
        fx, fy = east - 0.5, north - 0.5
        ekm = np.floor(east / 1000.0).astype(int)
        nkm = np.floor(north / 1000.0).astype(int)
        for key in set(zip(ekm.tolist(), nkm.tolist())):
            if key not in self.grids:
                self._load(*key)
            grid = self.grids[key]
            if grid is None:
                continue
            sel = (ekm == key[0]) & (nkm == key[1])
            x = np.clip(fx[sel] - key[0] * 1000, 0, 998.999)
            y = np.clip(fy[sel] - key[1] * 1000, 0, 998.999)
            ix, iy = x.astype(int), y.astype(int)
            ax, ay = x - ix, y - iy
            out[sel] = (grid[iy, ix] * (1 - ax) * (1 - ay) + grid[iy, ix + 1] * ax * (1 - ay)
                        + grid[iy + 1, ix] * (1 - ax) * ay + grid[iy + 1, ix + 1] * ax * ay)
        return out


def write_png(path, rgb):
    """A plain RGB PNG with only the standard library."""
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[r].astype(np.uint8).tobytes() for r in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 6)))
        f.write(chunk(b"IEND", b""))


def main():
    realmap = np.loadtxt(REALMAP, delimiter=",", skiprows=1)
    z, east, north, de, dn = realmap.T
    offs = offsets()
    dgm = Dgm(DGM)
    print(f"{len(dgm.zips)} DGM1 zips in {DGM}; {len(z)} road samples, {len(offs)} offsets")

    # Right of the road, as the game sees it: (dir_north, -dir_east).
    re_, rn = dn, -de
    rel = np.empty((len(z), len(offs)))
    batch = 400
    for start in range(0, len(z), batch):
        sl = slice(start, min(len(z), start + batch))
        e0, n0 = east[sl][:, None], north[sl][:, None]
        ex = e0 + re_[sl][:, None] * offs[None, :]
        nx = n0 + rn[sl][:, None] * offs[None, :]
        ground = dgm.height(ex.ravel(), nx.ravel()).reshape(ex.shape)
        across = np.linspace(-ROAD_HALF, ROAD_HALF, 5)
        er = e0 + re_[sl][:, None] * across[None, :]
        nr = n0 + rn[sl][:, None] * across[None, :]
        road = np.nanmean(dgm.height(er.ravel(), nr.ravel()).reshape(er.shape), axis=1)
        rel[sl] = ground - road[:, None]
        print(f"  {min(len(z), start + batch)}/{len(z)} road samples", flush=True)

    holes = int(np.isnan(rel).sum())
    if holes:
        print(f"{holes} of {rel.size} samples have no DGM1 data ({len(dgm.missing)} 1 km cells missing: "
              f"{sorted(dgm.missing)[:8]}...); they are left flat.")
    rel = np.nan_to_num(rel, nan=0.0)

    # Onto the game's road distance grid.
    zs = np.arange(0.0, z[-1], Z_STEP)
    table = np.stack([np.interp(zs, z, rel[:, c]) for c in range(len(offs))], axis=1)
    cm = np.clip(np.round(table * 100.0), -32767, 32767).astype("<i2")

    os.makedirs(os.path.dirname(ASSET), exist_ok=True)
    with open(ASSET, "wb") as f:
        f.write(b"RRTR")
        f.write(struct.pack("<ifi", len(zs), Z_STEP, len(offs)))
        f.write(offs.astype("<f4").tobytes())
        f.write(cm.tobytes())
    print(f"wrote {ASSET}: {len(zs)} rows x {len(offs)} offsets, {os.path.getsize(ASSET) // 1024} KB; "
          f"relative height {table.min():.0f} .. {table.max():.0f} m")

    # Preview: shaded relief, a row per 8 m of road, each offset a few pixels wide.
    rows = table[:: 2]
    widths = np.clip(np.diff(np.concatenate([offs, [offs[-1] + 10]])) / 2, 1, 5).astype(int)
    img = np.repeat(rows, widths, axis=1)
    gy, gx = np.gradient(img)
    shade = np.clip(0.5 + (gx - gy) * 0.08, 0, 1)
    height = np.clip((img + 60) / 180, 0, 1)
    rgb = np.stack([shade * 200 + height * 55, shade * 200 + height * 40, shade * 190], axis=2)
    road_col = int(widths[: len(offs) // 2].sum())
    rgb[:, road_col: road_col + 2] = (220, 40, 40)
    try:
        write_png(PREVIEW, rgb[:4000])
        print("preview", PREVIEW)
    except OSError as err:
        print("no preview:", err)


if __name__ == "__main__":
    main()
