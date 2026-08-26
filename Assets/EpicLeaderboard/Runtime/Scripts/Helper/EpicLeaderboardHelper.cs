using UnityEngine;

namespace EpicLeaderboard
{
    public static class EpicLeaderboardHelpers
    {
        public static void DestroyAllChildren(this Transform transform)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(child);
                else
                    UnityEngine.Object.DestroyImmediate(child);
            }
        }

        public static bool IsPrefabMode(this GameObject gameObject)
        {
#if UNITY_EDITOR
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            bool isInPrefabMode = prefabStage != null && prefabStage.IsPartOfPrefabContents(gameObject);
            bool isInScene = gameObject.scene.isLoaded;

            // wir sind das raw Prefab Asset
            return (!isInPrefabMode && !isInScene);
#else
            return false;
#endif
        }
    }
}