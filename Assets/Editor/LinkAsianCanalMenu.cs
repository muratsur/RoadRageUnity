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

        var sets = CollectTextureSets();
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
        var unmatched = 0;
        var fbxs = AssetDatabase.FindAssets("t:Model", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
        n = 0;
        foreach (var path in fbxs)
        {
            EditorUtility.DisplayProgressBar("Link Asian Canal Pack", Path.GetFileName(path), n++ / (float)fbxs.Count);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var meshKey = Key(Path.GetFileNameWithoutExtension(path));
            var slots = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name).Distinct().ToList();
            var line = new StringBuilder();
            foreach (var slot in slots)
            {
                var match = Match(Key(slot), meshKey, materials);
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
            var size = Size(prefab);
            meshes.Add(prefab);
            sizes.Add(size);
            report.AppendLine($"{prefab.name}  size={size.x:0.00}x{size.y:0.00}x{size.z:0.00}{line}");
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
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        File.WriteAllText(Path.Combine(Root, "catalog.txt"), report.ToString());
        Debug.Log($"Link Asian Canal Pack: {materials.Count} materials, {meshes.Count} meshes, {unmatched} material " +
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
        var path = $"{Generated}/M_{set.Name}.mat";
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

    private static Material Match(string slotKey, string meshKey, Dictionary<string, Material> materials)
    {
        if (materials.TryGetValue(slotKey, out var exact)) return exact;
        // "wood_01_red" -> "wood_01"; "barrel_01_ropes" -> "barrel_01"
        for (var k = slotKey; k.Contains('_'); k = k.Substring(0, k.LastIndexOf('_')))
            if (materials.TryGetValue(k, out var shorter)) return shorter;
        var containing = materials.Where(kv => slotKey.Contains(kv.Key) || kv.Key.Contains(slotKey))
            .OrderByDescending(kv => kv.Key.Length).FirstOrDefault();
        if (containing.Value != null) return containing.Value;
        return materials.TryGetValue(meshKey, out var byMesh) ? byMesh : null;
    }

    private static Vector3 Size(GameObject prefab)
    {
        if (prefab == null) return Vector3.zero;
        var instance = Object.Instantiate(prefab);
        var renderers = instance.GetComponentsInChildren<Renderer>();
        var size = Vector3.zero;
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            size = bounds.size;
        }
        Object.DestroyImmediate(instance);
        return size;
    }
}
