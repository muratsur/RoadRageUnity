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
        /// Where each mesh's top is, from the middle of its bounds (x, z): the ridge of a
        /// roof, the high edge of an awning. Lets the builder turn a sloped piece so it
        /// runs down towards the street without guessing which way it was modelled.
        public Vector3[] Highs = System.Array.Empty<Vector3>();
        /// The materials built for the pack (cobbles, stone floor, canal water ...).
        public Material[] Materials = System.Array.Empty<Material>();

        /// Whole rows of houses exported from the pack's showcase map (Unreal: File >
        /// Export Selected) into Assets/AsianCanal/Assemblies, every piece where the
        /// artist put it. RowFronts is the side of each row that faced the canal (its
        /// stone wall reaches lowest), in the model's own axes; RowSkip names parts
        /// that are not buildings - sky domes, HDRI spheres, the level's backdrop.
        public GameObject[] Rows = System.Array.Empty<GameObject>();
        public Vector3[] RowFronts = System.Array.Empty<Vector3>();
        public string[] RowSkip = System.Array.Empty<string>();

        public Vector3 HighOf(GameObject mesh)
        {
            var i = System.Array.IndexOf(Meshes, mesh);
            return i >= 0 && i < Highs.Length ? Highs[i] : Vector3.zero;
        }

        public Material FindMaterial(string name)
        {
            foreach (var m in Materials)
                if (m != null && string.Equals(m.name, name, System.StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

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
