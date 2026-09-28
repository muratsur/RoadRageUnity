using System.IO;
using System.Linq;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

/// Road Rage > Link Weather Pack: finds the rain, storm and snow effects of an
/// installed weather pack and lists them in Resources/Weather/WeatherPack, which the
/// weather system plays over the car in place of its own particles. Run it again
/// after adding, updating or removing the pack.
public static class LinkWeatherPackMenu
{
    private const string RegistryPath = "Assets/Resources/Weather/WeatherPack.asset";

    private static readonly string[] PackFolders = { "dynamic weather", "dynamicweather", "weather system", "weathersystem" };
    // Effects that are part of a weather, not the weather itself.
    private static readonly string[] Skip =
        { "splash", "puddle", "ripple", "drip", "window", "screen", "lens", "decal", "impact", "demo", "scene", "ui" };

    [MenuItem("Road Rage/Link Weather Pack")]
    public static void Link()
    {
        var effects = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => PackFolders.Any(f => p.ToLowerInvariant().Contains(f)))
            .Select(p => (path: p, name: Path.GetFileNameWithoutExtension(p).ToLowerInvariant(),
                go: AssetDatabase.LoadAssetAtPath<GameObject>(p)))
            .Where(x => x.go != null && x.go.GetComponentsInChildren<ParticleSystem>(true).Length > 0)
            .Where(x => !Skip.Any(s => x.name.Contains(s)))
            .ToList();

        GameObject Pick(System.Func<string, bool> match, System.Func<string, bool> prefer = null)
        {
            var found = effects.Where(x => match(x.name)).ToList();
            if (prefer != null && found.Any(x => prefer(x.name))) found = found.Where(x => prefer(x.name)).ToList();
            // The fullest effect: the one with the most particle systems.
            return found.OrderByDescending(x => x.go.GetComponentsInChildren<ParticleSystem>(true).Length)
                .Select(x => x.go).FirstOrDefault();
        }

        var storm = Pick(n => n.Contains("storm") || n.Contains("thunder") || (n.Contains("rain") && n.Contains("heavy")));
        var rain = Pick(n => n.Contains("rain") && !n.Contains("heavy") && !n.Contains("storm"),
                       n => n.Contains("medium") || n.Contains("normal")) ??
                   Pick(n => n.Contains("rain"));
        var snow = Pick(n => n.Contains("snow") && !n.Contains("storm") && !n.Contains("blizzard"),
                       n => n.Contains("medium") || n.Contains("normal")) ??
                   Pick(n => n.Contains("snow") || n.Contains("blizzard"));

        var registry = AssetDatabase.LoadAssetAtPath<WeatherPack>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<WeatherPack>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.Rain = rain;
        registry.Storm = storm;
        registry.Snow = snow;
        registry.Source = rain != null ? AssetDatabase.GetAssetPath(rain) : snow != null ? AssetDatabase.GetAssetPath(snow) : "";
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        string Show(GameObject g) => g != null ? AssetDatabase.GetAssetPath(g) : "none (the game's own)";
        if (rain == null && snow == null && storm == null)
            Debug.LogWarning("Link Weather Pack: no rain or snow effects found in a 'Dynamic Weather' folder. " +
                             "The game keeps its own weather particles.");
        else
            Debug.Log($"Link Weather Pack:\n  rain  {Show(rain)}\n  storm {Show(storm)}\n  snow  {Show(snow)}\n" +
                      "Candidates:\n" + string.Join("\n", effects.Select(x => "  " + x.path)));
    }
}
