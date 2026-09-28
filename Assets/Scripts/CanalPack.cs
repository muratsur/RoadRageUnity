using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// The Asian Canal Environment (Leartes Studios, from Fab), exported from Unreal to
    /// FBX into Assets/AsianCanal in this copy of the project. The pack is licensed for
    /// use, not redistribution, so it is never committed, and it sits outside Resources.
    /// Road Rage > Link Asian Canal Pack builds its URP materials and writes this list
    /// to Resources/Biomes/AsianCanalPack (also not committed): every mesh, with its
    /// size in metres. The CANAL TOWN biome looks meshes up by name.
    public sealed class CanalPack : ScriptableObject
    {
        public GameObject[] Meshes = System.Array.Empty<GameObject>();
        /// Bounds size of each mesh, metres, in the same order.
        public Vector3[] Sizes = System.Array.Empty<Vector3>();

        public GameObject Find(string name, out Vector3 size)
        {
            for (var i = 0; i < Meshes.Length; i++)
            {
                if (Meshes[i] == null || !string.Equals(Meshes[i].name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                size = i < Sizes.Length ? Sizes[i] : Vector3.one;
                return Meshes[i];
            }
            size = Vector3.zero;
            return null;
        }
    }
}
