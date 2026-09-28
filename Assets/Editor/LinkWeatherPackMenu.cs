using System.IO;
using System.Linq;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

/// Road Rage > Link Weather Pack: finds the rain, storm and wind loops of an installed
/// weather pack (URP Dynamic Weather System) and lists them in
/// Resources/Weather/WeatherPack, which the weather system plays under its own rain
/// and snow. Run it again after adding, updating or removing the pack.
public static class LinkWeatherPackMenu
{
    private const string RegistryPath = "Assets/Resources/Weather/WeatherPack.asset";

    private static readonly string[] PackFolders = { "dynamic weather", "dynamicweather", "weather system", "weathersystem" };

    [MenuItem("Road Rage/Link Weather Pack")]
    public static void Link()
    {
        var clips = AssetDatabase.FindAssets("t:AudioClip").Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => PackFolders.Any(f => p.ToLowerInvariant().Contains(f)))
            .Select(p => (path: p, name: Path.GetFileNameWithoutExtension(p).ToLowerInvariant(),
                clip: AssetDatabase.LoadAssetAtPath<AudioClip>(p)))
            .Where(x => x.clip != null)
            .ToList();

        AudioClip Pick(System.Func<string, bool> match) =>
            clips.Where(x => match(x.name)).Select(x => x.clip).FirstOrDefault();

        var storm = Pick(n => n.Contains("storm") || n.Contains("thunder"));
        var rain = Pick(n => n.Contains("rain") && !n.Contains("storm"));
        var wind = Pick(n => n.Contains("wind") && !n.Contains("storm"));

        var registry = AssetDatabase.LoadAssetAtPath<WeatherPack>(RegistryPath);
        if (registry == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
            registry = ScriptableObject.CreateInstance<WeatherPack>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.Rain = rain;
        registry.Storm = storm;
        registry.Wind = wind;
        registry.Source = clips.Count > 0 ? Path.GetDirectoryName(clips[0].path) : "";
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        string Show(AudioClip c) => c != null ? AssetDatabase.GetAssetPath(c) : "none";
        if (rain == null && storm == null && wind == null)
            Debug.LogWarning("Link Weather Pack: no rain, storm or wind sound found in a 'Dynamic Weather' folder. " +
                             "Weather stays silent.");
        else
            Debug.Log($"Link Weather Pack: rain {Show(rain)}, storm {Show(storm)}, wind {Show(wind)}");
    }
}
