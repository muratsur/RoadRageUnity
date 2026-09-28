using System.IO;
using System.Linq;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

/// Road Rage > Link Installed Tree Pack: finds the tree prefabs of an installed tree
/// pack and lists them in Resources/Biomes/ExternalVegetation, which Greenwood plants
/// in place of its own trees. Run it again after adding, updating or removing a pack.
///
/// Packs, best first: European Forests - Realistic Trees (MysticForge), then
/// NatureManufacture's Mountain Trees. Conifers (spruce, fir, pine) make the forest,
/// broadleaf trees (beech, oak ...) are mixed in, small or young trees are the
/// understory.
public static class LinkTreePackMenu
{
    private const string RegistryPath = "Assets/Resources/Biomes/ExternalVegetation.asset";

    // Parts of a pack that are not a whole, grown, snow-free standing tree.
    private static readonly string[] Skip =
    {
        "snow", "branch", "billboard", "stump", "trunk", "root", "debris", "fallen", "dead", "impostor", "demo",
        "plant", "log", "leaf", "leaves", "bark", "material", "bush", "grass", "fern", "rock", "scene", "sample",
    };

    private static readonly string[] Conifer = { "fir", "spruce", "pine", "larch", "conifer", "abies", "picea", "pinus" };
    private static readonly string[] Broadleaf =
        { "beech", "oak", "birch", "maple", "alder", "hornbeam", "linden", "lime", "ash", "fagus", "quercus", "betula" };
    private static readonly string[] Young = { "small", "young", "sapling", "medium" };

    private static readonly (string label, string[] match)[] Packs =
    {
        // European Forests installs into "Realistic Tree".
        ("European Forests", new[] { "european", "mysticforge", "realistic tree" }),
        ("NatureManufacture", new[] { "naturemanufacture" }),
    };

    /// Whether a folder in the path names that pipeline ("HDRP", "URP", "Prefabs_URP" ...).
    private static bool InFolder(string path, string pipeline) =>
        path.Replace('\\', '/').Split('/').Reverse().Skip(1).Any(folder =>
            folder.ToLowerInvariant().Split(' ', '_', '-', '(', ')', '[', ']').Contains(pipeline));

    [MenuItem("Road Rage/Link Installed Tree Pack")]
    public static void Link()
    {
        var all = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
            .Concat(AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".st", System.StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".spm", System.StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToList();

        string source = null;
        var candidates = new System.Collections.Generic.List<(string path, string name)>();
        foreach (var (label, match) in Packs)
        {
            candidates = all
                .Where(p => match.Any(m => p.IndexOf(m, System.StringComparison.OrdinalIgnoreCase) >= 0))
                // Vegetation Studio versions need that (separate, paid) plugin to render.
                .Where(p => p.IndexOf("Vegetation Studio", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                            !Path.GetFileName(p).StartsWith("VS_", System.StringComparison.OrdinalIgnoreCase))
                .Select(p => (path: p, name: Path.GetFileNameWithoutExtension(p).ToLowerInvariant()))
                .Where(x => Conifer.Any(x.name.Contains) || Broadleaf.Any(x.name.Contains) || x.name.Contains("tree"))
                .Where(x => !Skip.Any(s => x.name.Contains(s)))
                .ToList();
            // Packs ship one prefab set per render pipeline (Prefabs/HDRP, Prefabs/URP,
            // Prefabs/Built-in). HDRP and Built-in materials draw magenta in URP.
            candidates = candidates.Where(x => !InFolder(x.path, "hdrp")).ToList();
            var urp = candidates.Where(x => InFolder(x.path, "urp")).ToList();
            if (urp.Count > 0) candidates = urp;
            if (candidates.Count == 0) continue;
            source = label;
            break;
        }

        // "Cheap" versions share one bark material, so a forest of them batches into a
        // handful of draw calls - use them where the pack has them.
        var cheap = candidates.Where(x => x.name.Contains("cheap")).ToList();
        if (cheap.Count >= 3) candidates = cheap;
        // Where a pack ships both prefabs and the raw models, the prefabs carry the LODs.
        var prefabs = candidates.Where(x => x.path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (prefabs.Count >= 3) candidates = prefabs;

        bool IsYoung((string path, string name) x) => Young.Any(x.name.Contains);
        bool IsBroadleaf((string path, string name) x) => Broadleaf.Any(x.name.Contains) && !Conifer.Any(x.name.Contains);
        var young = candidates.Where(IsYoung).ToList();
        var broadleaf = candidates.Where(x => !IsYoung(x) && IsBroadleaf(x)).ToList();
        var trees = candidates.Where(x => !IsYoung(x) && !IsBroadleaf(x)).ToList();
        // A pack that names none of its species: everything grown is the forest.
        if (trees.Count == 0) (trees, broadleaf) = (broadleaf, new System.Collections.Generic.List<(string, string)>());
        // Young conifers make the understory; a pack without any uses its grown ones.
        var understory = young.Where(x => !IsBroadleaf(x)).ToList();

        var registry = AssetDatabase.LoadAssetAtPath<ExternalVegetation>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<ExternalVegetation>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        GameObject[] Load(System.Collections.Generic.IEnumerable<(string path, string name)> list) =>
            list.Select(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.path)).Where(g => g != null).ToArray();
        registry.Trees = Load(trees);
        registry.Broadleaf = Load(broadleaf);
        registry.YoungTrees = Load(understory);
        registry.Source = source != null && trees.Count > 0 ? $"{source} ({Path.GetDirectoryName(trees[0].path)})" : "";
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        var broken = registry.Trees.Concat(registry.Broadleaf).Count(t => t.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials)
            .Any(m => m == null || m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader"));
        if (broken > 0)
            Debug.LogWarning($"Link Installed Tree Pack: {broken} trees have shaders URP cannot draw (magenta). " +
                             "Import the pack's URP support package, then run this again. Until then Greenwood skips them.");

        if (registry.Trees.Length == 0)
            Debug.LogWarning("Link Installed Tree Pack: no tree prefabs found in a European Forests or NatureManufacture " +
                             "folder. Greenwood keeps its own trees.");
        else
            Debug.Log($"Link Installed Tree Pack: {source}: {registry.Trees.Length} conifers, {registry.Broadleaf.Length} " +
                      $"broadleaf, {registry.YoungTrees.Length} understory trees linked for Greenwood:\n" +
                      string.Join("\n", trees.Concat(broadleaf).Concat(understory).Select(x => x.path)));
    }
}
