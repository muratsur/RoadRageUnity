using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// Road Rage > Bake B500 Terrain: the real ground beside the B500, from the
/// Baden-Wuerttemberg DGM1 (1 m terrain model, LGL open data, dl-de/by-2.0,
/// "Datenquelle: LGL, www.lgl-bw.de"), baked into a road-relative height table.
///
/// The same bake as Tools/Terrain/bake_b500_terrain.py, inside the editor so it needs
/// nothing installed. The game road is the real B500 with its bends capped and eased,
/// so the terrain is taken relative to the road: for every 4 m of game road and each
/// lateral offset, the real ground height at that offset beside the real road, minus
/// the real road's own height there.
///
/// Input: Tools/Terrain/b500_realmap.csv (export_b500_realmap.py) and the DGM1 zips
/// from Tools/Terrain/download_lgl_b500.ps1 in ../RoadRageData/lgl/dgm1. Each zip's
/// ASCII .xyz ("east north height" per 1 m cell) is read once and cached as raw
/// 1 km grids in dgm1/cache.
/// Output: Assets/Resources/Biomes/Routes/b500_terrain.bytes (format in the .py) and
/// ../RoadRageData/lgl/b500_terrain_preview.png.
public static class BakeB500TerrainMenu
{
    private const float ZStep = 4f;
    private const float RoadHalf = 3f;
    private const string Asset = "Assets/Resources/Biomes/Routes/b500_terrain.bytes";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string DataRoot => Path.GetFullPath(Path.Combine(ProjectRoot, "..", "RoadRageData", "lgl"));

    [MenuItem("Road Rage/Bake B500 Terrain")]
    public static void Bake()
    {
        var realmap = Path.Combine(ProjectRoot, "Tools", "Terrain", "b500_realmap.csv");
        var dgmFolder = Path.Combine(DataRoot, "dgm1");
        if (!File.Exists(realmap)) { Debug.LogError($"Bake B500 Terrain: missing {realmap}"); return; }
        if (!Directory.Exists(dgmFolder))
        {
            Debug.LogError($"Bake B500 Terrain: no {dgmFolder}. Run Tools/Terrain/download_lgl_b500.ps1 first.");
            return;
        }

        try
        {
            var dgm = new Dgm(dgmFolder);
            if (dgm.ZipCount == 0) { Debug.LogError($"Bake B500 Terrain: no DGM1 zips in {dgmFolder}"); return; }

            // Game road distance -> real position and direction (UTM 32).
            var z = new List<double>(); var east = new List<double>(); var north = new List<double>();
            var de = new List<double>(); var dn = new List<double>();
            foreach (var line in File.ReadLines(realmap))
            {
                if (line.Length == 0 || line[0] == 'z') continue;
                var p = line.Split(',');
                z.Add(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture));
                east.Add(double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                north.Add(double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
                de.Add(double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
                dn.Add(double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture));
            }

            var offsets = Offsets();
            var rel = new double[z.Count, offsets.Length];
            var holes = 0;
            for (var i = 0; i < z.Count; i++)
            {
                if (i % 200 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar("Bake B500 Terrain",
                        $"Road sample {i}/{z.Count} ({dgm.Loaded} km² of DGM1 read)", (float)i / z.Count))
                {
                    Debug.LogWarning("Bake B500 Terrain: cancelled.");
                    return;
                }
                // Right of the road, as the game sees it.
                double re = dn[i], rn = -de[i];
                double road = 0; var roadCount = 0;
                for (var a = -RoadHalf; a <= RoadHalf + 0.01; a += RoadHalf / 2)
                {
                    var h = dgm.Height(east[i] + re * a, north[i] + rn * a);
                    if (!double.IsNaN(h)) { road += h; roadCount++; }
                }
                road = roadCount > 0 ? road / roadCount : double.NaN;
                for (var c = 0; c < offsets.Length; c++)
                {
                    var h = dgm.Height(east[i] + re * offsets[c], north[i] + rn * offsets[c]);
                    var v = h - road;
                    if (double.IsNaN(v)) { holes++; v = 0; }
                    rel[i, c] = v;
                }
            }

            // Onto the game's road distance grid.
            var rows = (int)(z[z.Count - 1] / ZStep);
            var table = new float[rows, offsets.Length];
            var k = 0;
            float lo = float.MaxValue, hi = float.MinValue;
            for (var r = 0; r < rows; r++)
            {
                var zz = r * ZStep;
                while (k < z.Count - 2 && z[k + 1] < zz) k++;
                var t = (float)System.Math.Clamp((zz - z[k]) / System.Math.Max(1e-6, z[k + 1] - z[k]), 0, 1);
                for (var c = 0; c < offsets.Length; c++)
                {
                    var v = Mathf.Lerp((float)rel[k, c], (float)rel[k + 1, c], t);
                    table[r, c] = v;
                    lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                }
            }

            var assetPath = Path.Combine(ProjectRoot, Asset);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            using (var w = new BinaryWriter(File.Create(assetPath)))
            {
                w.Write(new[] { (byte)'R', (byte)'R', (byte)'T', (byte)'R' });
                w.Write(rows);
                w.Write(ZStep);
                w.Write(offsets.Length);
                foreach (var o in offsets) w.Write(o);
                for (var r = 0; r < rows; r++)
                    for (var c = 0; c < offsets.Length; c++)
                        w.Write((short)Mathf.Clamp(Mathf.Round(table[r, c] * 100f), -32767, 32767));
            }
            AssetDatabase.ImportAsset(Asset);

            var preview = Path.Combine(DataRoot, "b500_terrain_preview.png");
            WritePreview(preview, table, offsets);

            var total = z.Count * offsets.Length;
            Debug.Log($"Bake B500 Terrain: wrote {Asset}, {rows} rows x {offsets.Length} offsets, relative height " +
                      $"{lo:0} .. {hi:0} m. {holes} of {total} samples had no DGM1 data and are flat" +
                      (dgm.Missing.Count > 0 ? $" (1 km cells missing: {string.Join(", ", dgm.Missing)})" : "") +
                      $". Preview: {preview}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// Dense by the road, where banks and cuttings are seen up close; sparser out to
    /// the valley sides. The same list as the Python baker.
    private static float[] Offsets()
    {
        var right = new List<float>();
        for (var o = 2f; o < 40f; o += 2f) right.Add(o);
        for (var o = 40f; o < 120f; o += 5f) right.Add(o);
        for (var o = 120f; o < 300.1f; o += 10f) right.Add(o);
        var all = new List<float>();
        for (var i = right.Count - 1; i >= 0; i--) all.Add(-right[i]);
        all.Add(0f);
        all.AddRange(right);
        return all.ToArray();
    }

    private static void WritePreview(string path, float[,] table, float[] offsets)
    {
        var rows = Mathf.Min(table.GetLength(0) / 2, 8000);
        var cols = table.GetLength(1);
        var widths = new int[cols];
        var width = 0;
        for (var c = 0; c < cols; c++)
        {
            var gap = c + 1 < cols ? offsets[c + 1] - offsets[c] : 10f;
            widths[c] = Mathf.Clamp(Mathf.RoundToInt(gap / 2f), 1, 5);
            width += widths[c];
        }
        var tex = new Texture2D(width, rows, TextureFormat.RGB24, false);
        var pixels = new Color32[width * rows];
        for (var r = 0; r < rows; r++)
        {
            var x = 0;
            for (var c = 0; c < cols; c++)
            {
                var h = table[r * 2, c];
                var dx = c + 1 < cols ? table[r * 2, c + 1] - h : 0f;
                var dy = r + 1 < rows ? table[(r + 1) * 2, c] - h : 0f;
                var shade = Mathf.Clamp01(0.5f + (dx - dy) * 0.08f);
                var tone = Mathf.Clamp01((h + 60f) / 180f);
                var col = c == cols / 2
                    ? new Color32(220, 40, 40, 255)
                    : new Color32((byte)(shade * 200 + tone * 55), (byte)(shade * 200 + tone * 40), (byte)(shade * 190), 255);
                for (var i = 0; i < widths[c]; i++) pixels[(rows - 1 - r) * width + x + i] = col;
                x += widths[c];
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    /// The DGM1 tiles as 1 km grids, read from the zips on first use and cached raw.
    private sealed class Dgm
    {
        private readonly string cacheFolder;
        private readonly Dictionary<(int, int), string> zips = new();
        private readonly Dictionary<(int, int), float[]> grids = new();
        public readonly List<string> Missing = new();
        public int ZipCount => zips.Count;
        public int Loaded => grids.Count;

        public Dgm(string folder)
        {
            cacheFolder = Path.Combine(folder, "cache");
            Directory.CreateDirectory(cacheFolder);
            foreach (var file in Directory.GetFiles(folder, "*.zip"))
            {
                var m = Regex.Match(Path.GetFileName(file), @"_32_(\d{3})_(\d{4})");
                if (m.Success) zips[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = file;
            }
        }

        /// Bilinear height at a UTM position; NaN where there is no data.
        public double Height(double east, double north)
        {
            var fx = east - 0.5;
            var fy = north - 0.5;
            var ekm = (int)System.Math.Floor(east / 1000.0);
            var nkm = (int)System.Math.Floor(north / 1000.0);
            var grid = Grid(ekm, nkm);
            if (grid == null) return double.NaN;
            var x = System.Math.Clamp(fx - ekm * 1000.0, 0, 998.999);
            var y = System.Math.Clamp(fy - nkm * 1000.0, 0, 998.999);
            int ix = (int)x, iy = (int)y;
            double ax = x - ix, ay = y - iy;
            return grid[iy * 1000 + ix] * (1 - ax) * (1 - ay) + grid[iy * 1000 + ix + 1] * ax * (1 - ay) +
                   grid[(iy + 1) * 1000 + ix] * (1 - ax) * ay + grid[(iy + 1) * 1000 + ix + 1] * ax * ay;
        }

        private float[] Grid(int ekm, int nkm)
        {
            if (grids.TryGetValue((ekm, nkm), out var grid)) return grid;
            var cache = Path.Combine(cacheFolder, $"dgm1_{ekm}_{nkm}.f32");
            if (File.Exists(cache))
            {
                var bytes = File.ReadAllBytes(cache);
                grid = new float[1000 * 1000];
                System.Buffer.BlockCopy(bytes, 0, grid, 0, bytes.Length);
                grids[(ekm, nkm)] = grid;
                return grid;
            }
            string zip = null;
            foreach (var pair in zips)
                if (pair.Key.Item1 <= ekm && ekm < pair.Key.Item1 + 2 && pair.Key.Item2 <= nkm && nkm < pair.Key.Item2 + 2)
                    zip = pair.Value;
            if (zip == null)
            {
                grids[(ekm, nkm)] = null;
                Missing.Add($"{ekm}_{nkm}");
                return null;
            }
            ReadZip(zip);
            if (!grids.ContainsKey((ekm, nkm)))
            {
                grids[(ekm, nkm)] = null;
                Missing.Add($"{ekm}_{nkm}");
            }
            return grids[(ekm, nkm)];
        }

        /// Every .xyz in the zip into 1 km grids (a file may span several), cached.
        private void ReadZip(string path)
        {
            var found = new Dictionary<(int, int), float[]>();
            using (var archive = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".xyz", System.StringComparison.OrdinalIgnoreCase)) continue;
                    using var stream = new BufferedStream(entry.Open(), 1 << 20);
                    var values = new double[3];
                    var n = 0;
                    while (ReadNumber(stream, out var v))
                    {
                        values[n++] = v;
                        if (n < 3) continue;
                        n = 0;
                        var e = (int)System.Math.Floor(values[0] / 1000.0);
                        var no = (int)System.Math.Floor(values[1] / 1000.0);
                        if (!found.TryGetValue((e, no), out var g))
                        {
                            g = new float[1000 * 1000];
                            for (var i = 0; i < g.Length; i++) g[i] = float.NaN;
                            found[(e, no)] = g;
                        }
                        var ix = System.Math.Clamp((int)System.Math.Floor(values[0] - e * 1000.0), 0, 999);
                        var iy = System.Math.Clamp((int)System.Math.Floor(values[1] - no * 1000.0), 0, 999);
                        g[iy * 1000 + ix] = (float)values[2];
                    }
                }
            }
            foreach (var pair in found)
            {
                var bytes = new byte[pair.Value.Length * 4];
                System.Buffer.BlockCopy(pair.Value, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(Path.Combine(cacheFolder, $"dgm1_{pair.Key.Item1}_{pair.Key.Item2}.f32"), bytes);
                grids[pair.Key] = pair.Value;
            }
        }

        /// The next number in an ASCII stream; separators are anything that is not
        /// part of a number. Much faster than splitting 4 million lines into strings.
        private static bool ReadNumber(Stream s, out double value)
        {
            value = 0;
            int b;
            do { b = s.ReadByte(); if (b < 0) return false; }
            while (!(b >= '0' && b <= '9') && b != '-' && b != '.');
            var negative = false;
            if (b == '-') { negative = true; b = s.ReadByte(); }
            double whole = 0;
            while (b >= '0' && b <= '9') { whole = whole * 10 + (b - '0'); b = s.ReadByte(); }
            if (b == '.')
            {
                var scale = 0.1;
                b = s.ReadByte();
                while (b >= '0' && b <= '9') { whole += (b - '0') * scale; scale *= 0.1; b = s.ReadByte(); }
            }
            value = negative ? -whole : whole;
            return true;
        }
    }
}
