using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoadRage.Editor
{
    /// Adds SSAO to the URP renderer. Renderer features are sub-assets with generated
    /// fileIDs, so this has to go through the asset API rather than YAML editing.
    public static class RendererFeatureSetup
    {
        private const string RendererPath = "Assets/Settings/RoadRageRenderer.asset";

        public static void AddSsao()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                Debug.LogError($"RR_SSAO renderer data not found at {RendererPath}");
                EditorApplication.Exit(1);
                return;
            }

            var ssaoType = typeof(ScriptableRendererFeature).Assembly
                .GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (ssaoType == null)
            {
                Debug.LogError("RR_SSAO ScreenSpaceAmbientOcclusion type not found");
                EditorApplication.Exit(1);
                return;
            }

            if (data.rendererFeatures.Any(feature => feature != null && feature.GetType() == ssaoType))
            {
                Debug.Log("RR_SSAO already present, nothing to do");
                return;
            }

            var ssao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
            ssao.name = "ScreenSpaceAmbientOcclusion";
            data.rendererFeatures.Add(ssao);
            AssetDatabase.AddObjectToAsset(ssao, data);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"RR_SSAO added; renderer now has {data.rendererFeatures.Count} feature(s)");
        }
    }
}
