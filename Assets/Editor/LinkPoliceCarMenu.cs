using System.IO;
using System.Linq;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

/// Road Rage > Link Police Car Pack: finds the car in an installed Realistic Mobile Car
/// pack and lists it in Resources/Police/PoliceCarPack, which the pursuit director
/// builds its cruisers from. Run it again after adding, updating or removing the pack.
public static class LinkPoliceCarMenu
{
    private const string RegistryPath = "Assets/Resources/Police/PoliceCarPack.asset";

    private static readonly string[] PackFolders = { "mobile car", "mobilecar", "surdov" };
    // Parts of the pack that are not the whole car.
    private static readonly string[] Skip =
    {
        "wheel", "tire", "tyre", "door", "glass", "light", "interior", "steering", "brake", "caliper",
        "camera", "scene", "demo", "ground", "road", "floor", "env", "sky", "canvas", "button", "particle",
    };

    [MenuItem("Road Rage/Link Police Car Pack")]
    public static void Link()
    {
        var candidates = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
            .Concat(AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath))
            .Distinct()
            .Where(p => PackFolders.Any(f => p.ToLowerInvariant().Contains(f)))
            .Where(p => !Skip.Any(s => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(s)))
            .Select(p => (path: p, go: AssetDatabase.LoadAssetAtPath<GameObject>(p)))
            .Where(x => x.go != null && x.go.GetComponentsInChildren<Renderer>(true).Length > 0)
            .ToList();

        // The car is the candidate with the most renderers that is car-sized; a prefab
        // beats the raw model it was made from, since it carries the pack's own setup.
        (string path, GameObject go) best = default;
        var bestScore = -1f;
        var report = new System.Text.StringBuilder();
        foreach (var c in candidates)
        {
            var size = Size(c.go);
            var length = Mathf.Max(size.x, size.z);
            var renderers = c.go.GetComponentsInChildren<Renderer>(true).Length;
            var carSized = length > 2.5f && length < 8f && size.y > 0.8f && size.y < 3f;
            var score = (carSized ? 1000f : 0f) + renderers + (c.path.EndsWith(".prefab") ? 500f : 0f);
            report.AppendLine($"  {c.path}  {size.x:0.0}x{size.y:0.0}x{size.z:0.0} m, {renderers} renderers");
            if (score > bestScore) { bestScore = score; best = c; }
        }

        var registry = AssetDatabase.LoadAssetAtPath<PoliceCarPack>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<PoliceCarPack>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.Car = best.go;
        registry.Source = best.path ?? "";
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (best.go == null)
            Debug.LogWarning("Link Police Car Pack: no car found in a 'Mobile Car' or 'Surdov' folder. " +
                             "The cruisers keep their own model.");
        else
            Debug.Log($"Link Police Car Pack: cruisers use {best.path}\nCandidates:\n{report}");
    }

    private static Vector3 Size(GameObject prefab)
    {
        var instance = Object.Instantiate(prefab);
        instance.transform.position = Vector3.zero;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        var size = Vector3.zero;
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            size = b.size;
        }
        Object.DestroyImmediate(instance);
        return size;
    }
}
