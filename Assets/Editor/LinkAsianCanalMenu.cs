using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

/// Road Rage > Link Asian Canal Pack: turns the Asian Canal Environment, exported from
/// Unreal (Bulk Export) into Assets/AsianCanal/{Meshes,Textures}, into something URP
/// can draw, and lists it for the game.
///
/// Unreal's materials do not come across, only their texture names, so this rebuilds
/// one URP Lit material per texture set, by suffix:
///   _B  base colour           _N  normal (DirectX: the green channel is flipped)
///   _M  opacity mask          _E  emissive
///   _ORM  AO, roughness, metal   _RAOM  roughness, AO, metal
///   _RHAO roughness, height, AO  _RHD   roughness, height, (unused)
/// Packed maps are repacked into URP's layout (metallic R, occlusion G, smoothness A).
/// Each mesh's material slots are then remapped to those materials by name.
/// Everything it writes stays inside Assets/AsianCanal or the ignored registry.
public static class LinkAsianCanalMenu
{
    private const string Root = "Assets/AsianCanal";
    private const string Generated = Root + "/URP";
    private const string RegistryPath = "Assets/Resources/Biomes/AsianCanalPack.asset";

    [MenuItem("Road Rage/Link Asian Canal Pack")]
    public static void Link()
    {
        if (!AssetDatabase.IsValidFolder(Root))
        {
            Debug.LogWarning($"Link Asian Canal Pack: no {Root} folder. Export the pack from Unreal and copy it there.");
            return;
        }
        Directory.CreateDirectory(Generated);

        stems = null;
        var sets = CollectTextureSets();
        setNames.Clear();
        foreach (var set in sets.Values) setNames[set.Key] = set.Name;
        var materials = new Dictionary<string, Material>();
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var set in sets.Values) ConfigureImporters(set);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        var n = 0;
        foreach (var set in sets.Values)
        {
            EditorUtility.DisplayProgressBar("Link Asian Canal Pack", $"Material {set.Key}", n++ / (float)sets.Count);
            var material = BuildMaterial(set);
            if (material != null) materials[set.Key] = material;
        }
        EditorUtility.ClearProgressBar();

        var report = new StringBuilder();
        var meshes = new List<GameObject>();
        var sizes = new List<Vector3>();
        var highs = new List<Vector3>();
        var unmatched = 0;
        var fbxs = AssetDatabase.FindAssets("t:Model", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
        n = 0;
        var rows = new List<GameObject>();
        var rowFronts = new List<Vector3>();
        var rowSkip = new HashSet<string>();
        foreach (var path in fbxs)
        {
            EditorUtility.DisplayProgressBar("Link Asian Canal Pack", Path.GetFileName(path), n++ / (float)fbxs.Count);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var meshName = Path.GetFileNameWithoutExtension(path);
            var slots = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name).Distinct().ToList();
            var line = new StringBuilder();
            foreach (var slot in slots)
            {
                var match = Match(slot, meshName, materials);
                if (match == null)
                {
                    unmatched++;
                    line.Append($" [{slot}: no textures]");
                    continue;
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), match);
                line.Append($" [{slot} -> {match.name}]");
            }
            importer.SaveAndReimport();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (path.Replace('\\', '/').Contains("/Assemblies/"))
            {
                var front = AnalyseRow(prefab, rowSkip, out var rowSize);
                rows.Add(prefab);
                rowFronts.Add(front);
                report.AppendLine($"ROW {prefab.name}  size={rowSize.x:0.0}x{rowSize.y:0.0}x{rowSize.z:0.0} " +
                                  $"front={front.x:0},{front.z:0}{line}");
                continue;
            }
            var size = Size(prefab, out var high);
            meshes.Add(prefab);
            sizes.Add(size);
            highs.Add(high);
            report.AppendLine($"{prefab.name}  size={size.x:0.00}x{size.y:0.00}x{size.z:0.00} high={high.x:0.00},{high.z:0.00}{line}");
        }
        EditorUtility.ClearProgressBar();

        var registry = AssetDatabase.LoadAssetAtPath<CanalPack>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<CanalPack>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.Meshes = meshes.ToArray();
        registry.Sizes = sizes.ToArray();
        registry.Rows = rows.ToArray();
        registry.RowFronts = rowFronts.ToArray();
        registry.RowSkip = rowSkip.ToArray();
        registry.Highs = highs.ToArray();
        registry.Materials = AssetDatabase.FindAssets("t:Material", new[] { Generated })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null).ToArray();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        File.WriteAllText(Path.Combine(Root, "catalog.txt"), report.ToString());
        Debug.Log($"Link Asian Canal Pack: {rows.Count} house rows, {materials.Count} materials, {meshes.Count} meshes, {unmatched} material " +
                  $"slots without textures. Catalogue: {Root}/catalog.txt");
    }

    // ------------------------------------------------------------ textures

    private sealed class TextureSet
    {
        public string Key;
        public string Name;
        public readonly Dictionary<string, string> Maps = new();
    }

    private static readonly string[] Suffixes = { "ORM", "RAOM", "RHAO", "RHD", "B", "N", "M", "E", "H" };

    private static Dictionary<string, TextureSet> CollectTextureSets()
    {
        var sets = new Dictionary<string, TextureSet>();
        foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath))
        {
            if (path.StartsWith(Generated)) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var cut = name.LastIndexOf('_');
            if (cut < 0) continue;
            var suffix = name.Substring(cut + 1).ToUpperInvariant();
            if (!Suffixes.Contains(suffix)) continue;
            var baseName = name.Substring(0, cut);
            var key = Key(baseName);
            if (!sets.TryGetValue(key, out var set)) sets[key] = set = new TextureSet { Key = key, Name = baseName };
            set.Maps[suffix] = path;
        }
        return sets;
    }

    private static void ConfigureImporters(TextureSet set)
    {
        foreach (var pair in set.Maps)
        {
            var suffix = pair.Key;
            var path = pair.Value;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var dirty = false;
            if (suffix == "N")
            {
                if (importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; dirty = true; }
                // Unreal's normal maps are DirectX: green points down.
                if (!importer.flipGreenChannel) { importer.flipGreenChannel = true; dirty = true; }
            }
            else
            {
                var colour = suffix == "B" || suffix == "E";
                if (importer.sRGBTexture != colour) { importer.sRGBTexture = colour; dirty = true; }
                // Packed maps are read back to be repacked below.
                if (!colour && !importer.isReadable) { importer.isReadable = true; dirty = true; }
            }
            if (importer.maxTextureSize > 2048) { importer.maxTextureSize = 2048; dirty = true; }
            if (dirty) importer.SaveAndReimport();
        }
    }

    private static Material BuildMaterial(TextureSet set)
    {
        if (!set.Maps.ContainsKey("B")) return null;
        var path = $"{Generated}/M_{(set.Name.StartsWith("T_") ? set.Name.Substring(2) : set.Name)}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", Color.white);

        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(set.Maps["B"]);
        if (set.Maps.TryGetValue("M", out var maskPath))
        {
            // Opacity in its own map; URP clips on the base map's alpha.
            baseMap = WriteBaseWithAlpha(set, set.Maps["B"], maskPath) ?? baseMap;
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.4f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 0f);   // cloth and leaves are single sheets
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        material.SetTexture("_BaseMap", baseMap);
        material.mainTexture = baseMap;

        if (set.Maps.TryGetValue("N", out var normal))
        {
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            material.EnableKeyword("_NORMALMAP");
        }

        var packed = WriteMetallicSmoothness(set);
        if (packed != null)
        {
            material.SetTexture("_MetallicGlossMap", packed);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetTexture("_OcclusionMap", packed);
            material.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.25f);
        }

        if (set.Maps.TryGetValue("E", out var emissive))
        {
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissive));
            material.SetColor("_EmissionColor", Color.white * 1.5f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// URP Lit reads metallic from R, occlusion from G and smoothness from A.
    private static Texture2D WriteMetallicSmoothness(TextureSet set)
    {
        string source = null;
        int rough = -1, ao = -1, metal = -1;
        if (set.Maps.TryGetValue("ORM", out source)) { ao = 0; rough = 1; metal = 2; }
        else if (set.Maps.TryGetValue("RAOM", out source)) { rough = 0; ao = 1; metal = 2; }
        else if (set.Maps.TryGetValue("RHAO", out source)) { rough = 0; ao = 2; }
        else if (set.Maps.TryGetValue("RHD", out source)) { rough = 0; }
        if (source == null) return null;

        var input = Load(source);
        if (input == null) return null;
        var pixels = input.GetPixels32();
        var output = new Color32[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            output[i] = new Color32(
                metal >= 0 ? Channel(p, metal) : (byte)0,
                ao >= 0 ? Channel(p, ao) : (byte)255,
                0,
                rough >= 0 ? (byte)(255 - Channel(p, rough)) : (byte)64);
        }
        return Save($"{Generated}/T_{set.Name}_MS.png", input.width, input.height, output, false);
    }

    private static byte Channel(Color32 c, int i) => i == 0 ? c.r : i == 1 ? c.g : c.b;

    private static Texture2D WriteBaseWithAlpha(TextureSet set, string basePath, string maskPath)
    {
        var colour = Load(basePath);
        var mask = Load(maskPath);
        if (colour == null || mask == null) return null;
        if (mask.width != colour.width || mask.height != colour.height) mask = Resize(mask, colour.width, colour.height);
        var c = colour.GetPixels32();
        var m = mask.GetPixels32();
        for (var i = 0; i < c.Length; i++) c[i].a = m[i].r;
        return Save($"{Generated}/T_{set.Name}_BA.png", colour.width, colour.height, c, true);
    }

    private static Texture2D Load(string assetPath)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        return tex.LoadImage(File.ReadAllBytes(assetPath)) ? tex : null;
    }

    private static Texture2D Resize(Texture2D source, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private static Texture2D Save(string assetPath, int width, int height, Color32[] pixels, bool colour)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, !colour);
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(assetPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.sRGBTexture = colour;
        importer.alphaIsTransparency = colour;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // ------------------------------------------------------------ matching

    /// "MI_CobbleStone_01B", "M_CobbleStone_01B_Inst", "T_CobbleStone_01B" all key to
    /// "cobblestone_01b".
    private static string Key(string name)
    {
        var k = name.ToLowerInvariant();
        foreach (var prefix in new[] { "mi_", "m_", "mat_", "t_", "sm_" })
            if (k.StartsWith(prefix)) { k = k.Substring(prefix.Length); break; }
        foreach (var suffix in new[] { "_inst", "_mat", "_mi" })
            if (k.EndsWith(suffix)) k = k.Substring(0, k.Length - suffix.Length);
        return k;
    }

    /// Unreal material instances share texture sets: MI_WoodPainted_02, _04 and _05 are
    /// all T_WoodPainted_01 with different parameters, MI_Fabric_02 is T_Fabric_01,
    /// MI_Barrel_01 is T_Barrels_01, MI_StoneFloor_01 is T_Stone_Floor_01A. So slots
    /// and texture sets are compared by their words - split on underscores and case,
    /// numbers and variant letters dropped, plurals made singular.
    private static string Stem(string name)
    {
        var n = name;
        foreach (var prefix in new[] { "MI_", "M_", "MAT_", "T_", "SM_" })
            if (n.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) { n = n.Substring(prefix.Length); break; }
        var words = new List<string>();
        foreach (var part in n.Split('_'))
        foreach (var w in System.Text.RegularExpressions.Regex.Split(part, "(?<=[a-z])(?=[A-Z])"))
        {
            var word = w.ToLowerInvariant();
            if (word.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(word, "^[0-9]+[a-z]?$") ||
                word.Length == 1 || word == "inst" || word == "mat") continue;
            if (word.Length > 3 && word.EndsWith("s") && !word.EndsWith("ss")) word = word.Substring(0, word.Length - 1);
            words.Add(word);
        }
        return string.Join("", words);
    }

    /// Slots whose words name no texture set of their own.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        { "woodmaskedred", "woodpainted" },
        { "woodmasked", "woodpainted" },
    };

    private static Dictionary<string, Material> stems;
    /// Texture set key -> its original name, whose capitals split the words.
    private static readonly Dictionary<string, string> setNames = new();

    private static Material Match(string slot, string meshName, Dictionary<string, Material> materials)
    {
        if (materials.TryGetValue(Key(slot), out var exact)) return exact;
        if (stems == null || stems.Count == 0)
        {
            // Shortest key first, so T_WoodPainted_01 wins over T_WoodPainted_01_Trim_01.
            stems = new Dictionary<string, Material>();
            foreach (var kv in materials.OrderBy(kv => kv.Key.Length))
            {
                var stem = Stem(setNames.TryGetValue(kv.Key, out var original) ? original : kv.Key);
                if (!stems.ContainsKey(stem)) stems[stem] = kv.Value;
            }
        }
        var slotStem = Stem(slot);
        var found = Lookup(slotStem);
        if (found == null && Aliases.TryGetValue(slotStem, out var alias)) found = Lookup(alias);
        if (found == null)
        {
            // Drop trailing words: "stonefloorwetdark" -> ... -> "stonefloor".
            var words = System.Text.RegularExpressions.Regex.Split(slot.Contains("_") ? slot.Substring(slot.IndexOf('_') + 1) : slot,
                "(?<=[a-z])(?=[A-Z])|_");
            for (var n = words.Length - 1; n >= 1 && found == null; n--)
                found = Lookup(Stem(string.Join("_", words.Take(n))));
        }
        found ??= Lookup(Stem(meshName));
        if (found == null) return null;
        // Colour variants of a shared texture set get a tinted copy.
        var tint = Tint(slot);
        return tint == Color.white ? found : Tinted(found, slot, tint);
    }

    private static Material Lookup(string stem) =>
        !string.IsNullOrEmpty(stem) && stems.TryGetValue(stem, out var m) ? m : null;

    private static Color Tint(string slot)
    {
        var s = slot.ToLowerInvariant();
        if (s.Contains("red")) return new Color(0.78f, 0.22f, 0.16f);
        if (s.Contains("dark")) return new Color(0.55f, 0.55f, 0.55f);
        if (s.Contains("green")) return new Color(0.35f, 0.62f, 0.38f);
        if (s.Contains("blue")) return new Color(0.35f, 0.48f, 0.75f);
        return Color.white;
    }

    private static Material Tinted(Material source, string slot, Color tint)
    {
        var path = $"{Generated}/M_{slot}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.CopyPropertiesFromMaterial(source);
        }
        material.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// Bounds size, and where the top of the mesh is: the mean (x, z) of its highest
    /// vertices, from the middle of the bounds, in the prefab's own orientation.
    private static Vector3 Size(GameObject prefab, out Vector3 high)
    {
        high = Vector3.zero;
        if (prefab == null) return Vector3.zero;
        var instance = Object.Instantiate(prefab);
        instance.transform.position = Vector3.zero;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        var size = Vector3.zero;
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            size = bounds.size;
            var lod = instance.GetComponent<LODGroup>();
            var lod0 = lod != null && lod.GetLODs().Length > 0 ? lod.GetLODs()[0].renderers : renderers;
            var points = new List<Vector3>();
            foreach (var r in lod0)
            {
                if (r == null || !r.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                foreach (var v in filter.sharedMesh.vertices) points.Add(r.transform.TransformPoint(v));
            }
            if (points.Count > 0)
            {
                var top = bounds.max.y - size.y * 0.15f;
                var upper = points.Where(p => p.y >= top).ToList();
                if (upper.Count > 0)
                    high = new Vector3(upper.Average(p => p.x) - bounds.center.x, 0f, upper.Average(p => p.z) - bounds.center.z);
            }
        }
        Object.DestroyImmediate(instance);
        return size;
    }

    /// A row of houses from the showcase map: which way its canal side faces, and its
    /// size without the parts that are not buildings.
    private static Vector3 AnalyseRow(GameObject prefab, HashSet<string> skip, out Vector3 size)
    {
        size = Vector3.zero;
        var instance = Object.Instantiate(prefab);
        instance.transform.position = Vector3.zero;
        var kept = new List<Renderer>();
        foreach (var r in instance.GetComponentsInChildren<Renderer>())
        {
            var n = r.name.ToLowerInvariant();
            if (r.bounds.size.magnitude > 600f || n.Contains("sky") || n.Contains("hdri") || n.Contains("dome") ||
                n.Contains("atmos") || n.Contains("cloud") || n.Contains("backdrop"))
            {
                skip.Add(r.name);
                continue;
            }
            kept.Add(r);
        }
        if (kept.Count == 0) { Object.DestroyImmediate(instance); return Vector3.back; }
        var bounds = kept[0].bounds;
        foreach (var r in kept) bounds.Encapsulate(r.bounds);
        size = bounds.size;
        var longX = size.x >= size.z;
        // The canal side is where the stone wall goes down to the water: the mean
        // position, across the row, of its lowest vertices.
        var low = bounds.min.y + size.y * 0.06f;
        double sum = 0;
        var count = 0;
        foreach (var r in kept)
        {
            if (!r.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
            var verts = filter.sharedMesh.vertices;
            for (var i = 0; i < verts.Length; i += 4)
            {
                var p = r.transform.TransformPoint(verts[i]);
                if (p.y > low) continue;
                sum += longX ? p.z - bounds.center.z : p.x - bounds.center.x;
                count++;
            }
        }
        Object.DestroyImmediate(instance);
        var sign = count > 0 && sum < 0 ? -1f : 1f;
        return longX ? new Vector3(0f, 0f, sign) : new Vector3(sign, 0f, 0f);
    }
}
