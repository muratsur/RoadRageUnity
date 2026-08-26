using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Reports world-space size of Resources meshes at scale 1, so placement code can be
    /// checked against reality (a mesh that imports tiny makes its LOD0 invisible while
    /// the hand-built LOD1 silhouette dominates).
    public static class MeshAudit
    {
        public static void Report()
        {
            foreach (var folder in new[] { "Hideout/Meshes", "Biomes/RedCanyon/Meshes/Tree" })
            foreach (var prefab in Resources.LoadAll<GameObject>(folder).OrderBy(p => p.name))
            {
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.Log($"RR_MESH {folder}/{prefab.name}: NO RENDERERS");
                    continue;
                }
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                Debug.Log($"RR_MESH {folder}/{prefab.name}: size={bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00}");
            }
        }
    }
}
