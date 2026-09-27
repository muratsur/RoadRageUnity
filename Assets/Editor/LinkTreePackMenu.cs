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

    // Parts of a pack that are not a whole standing tree.
    private static readonly string[] Skip =
    {
        "snow", "branch", "billboard", "stump", "trunk", "root", "debris", "fallen", "dead", "impostor", "demo",
    };

    [MenuItem("Road Rage/Link Installed Tree Pack")]
    public static void Link()
    {
        var candidates = AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.IndexOf("NatureManufacture", System.StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(p => (path: p, name: Path.GetFileNameWithoutExtension(p).ToLowerInvariant()))
            .Where(x => x.name.Contains("fir") || x.name.Contains("spruce") || x.name.Contains("pine") ||
                        x.name.Contains("tree"))
            .Where(x => !Skip.Any(s => x.name.Contains(s)))
            .ToList();
        // The pack's "forest" versions are made for dense stands - the roadside forest.
        var forest = candidates.Where(x => x.name.Contains("forest")).ToList();
        var chosen = forest.Count >= 3 ? forest : candidates;

        var registry = AssetDatabase.LoadAssetAtPath<ExternalVegetation>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<ExternalVegetation>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.Trees = chosen.Select(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.path))
            .Where(g => g != null).ToArray();
        registry.Source = chosen.Count > 0 ? Path.GetDirectoryName(chosen[0].path) : "";
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (registry.Trees.Length == 0)
            Debug.LogWarning("Link Installed Tree Pack: no tree prefabs found under a NatureManufacture folder. " +
                             "Greenwood keeps its own trees.");
        else
            Debug.Log($"Link Installed Tree Pack: {registry.Trees.Length} trees linked for Greenwood:\n" +
                      string.Join("\n", chosen.Select(x => x.path)));
    }
}
