using System.IO;
using System.Linq;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

/// Road Rage > Link Installed Tree Pack: finds the tree prefabs of an installed
/// NatureManufacture pack and lists them in Resources/Biomes/ExternalVegetation,
/// which Greenwood plants in place of its own trees. Run it again after updating
/// or removing the pack.
public static class LinkTreePackMenu
{
    private const string RegistryPath = "Assets/Resources/Biomes/ExternalVegetation.asset";

    // Parts of a pack that are not a whole, grown, snow-free standing tree. Mountain
    // Trees has plant/small/medium/big/forest stages, dead trees, and snow versions.
    private static readonly string[] Skip =
    {
        "snow", "branch", "billboard", "stump", "trunk", "root", "debris", "fallen", "dead", "impostor", "demo",
        "plant",
    };

    [MenuItem("Road Rage/Link Installed Tree Pack")]
    public static void Link()
    {
        var candidates = AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.IndexOf("NatureManufacture", System.StringComparison.OrdinalIgnoreCase) >= 0)
            // Vegetation Studio versions need that (separate, paid) plugin to render.
            .Where(p => p.IndexOf("Vegetation Studio", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        !Path.GetFileName(p).StartsWith("VS_", System.StringComparison.OrdinalIgnoreCase))
            .Select(p => (path: p, name: Path.GetFileNameWithoutExtension(p).ToLowerInvariant()))
            .Where(x => x.name.Contains("fir") || x.name.Contains("spruce") || x.name.Contains("pine") ||
                        x.name.Contains("tree"))
            .Where(x => !Skip.Any(s => x.name.Contains(s)))
            .ToList();
        // "Cheap" versions share one bark material, so a whole forest of them batches
        // into a handful of draw calls - use them where the pack has them. Every
        // grown species goes in: picking only the "forest" versions left two.
        var cheap = candidates.Where(x => x.name.Contains("cheap")).ToList();
        if (cheap.Count >= 3) candidates = cheap;
        // Small and medium trees are the understory; the rest stand as the forest.
        var young = candidates.Where(x => x.name.Contains("small") || x.name.Contains("medium")).ToList();
        var chosen = candidates.Where(x => !x.name.Contains("small")).ToList();

        var registry = AssetDatabase.LoadAssetAtPath<ExternalVegetation>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<ExternalVegetation>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.Trees = chosen.Select(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.path))
            .Where(g => g != null).ToArray();
        registry.YoungTrees = young.Select(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.path))
            .Where(g => g != null).ToArray();
        registry.Source = chosen.Count > 0 ? Path.GetDirectoryName(chosen[0].path) : "";
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        var broken = registry.Trees.Count(t => t.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials)
            .Any(m => m == null || m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader"));
        if (broken > 0)
            Debug.LogWarning($"Link Installed Tree Pack: {broken} of {registry.Trees.Length} trees have shaders URP cannot " +
                             "draw (magenta). Import the URP package from the pack's 'HD and URP support' folder, " +
                             "then run this again. Until then Greenwood skips them.");

        if (registry.Trees.Length == 0)
            Debug.LogWarning("Link Installed Tree Pack: no tree prefabs found under a NatureManufacture folder. " +
                             "Greenwood keeps its own trees.");
        else
            Debug.Log($"Link Installed Tree Pack: {registry.Trees.Length} trees and {registry.YoungTrees.Length} " +
                      "understory trees linked for Greenwood:\n" +
                      string.Join("\n", chosen.Concat(young).Select(x => x.path)));
    }
}
