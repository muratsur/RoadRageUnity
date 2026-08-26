using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Reports the material slots each vehicle mesh exposes, so vehicle materials can be
    /// assigned per-submesh (paint / glass / tyre / chrome) instead of one flat material.
    public static class VehicleAudit
    {
        public static void Report()
        {
            foreach (var prefab in Resources.LoadAll<GameObject>("Vehicles").OrderBy(p => p.name))
            {
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                var slots = renderers
                    .SelectMany(r => r.sharedMaterials)
                    .Select(m => m == null ? "<null>" : m.name)
                    .Distinct()
                    .ToArray();
                var tris = renderers
                    .OfType<SkinnedMeshRenderer>()
                    .Select(r => r.sharedMesh)
                    .Concat(prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh))
                    .Where(m => m != null)
                    .Sum(m => m.triangles.Length / 3);
                Debug.Log($"RR_VEH {prefab.name}: renderers={renderers.Length} tris={tris} slots=[{string.Join(", ", slots)}]");
            }
        }
    }
}
