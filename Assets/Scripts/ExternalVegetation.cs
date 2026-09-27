using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Trees from a third-party pack installed in this copy of the project (for
    /// Greenwood: NatureManufacture's Mountain Trees). Asset Store packs are licensed
    /// for use, not redistribution, so the pack is never committed - and it installs
    /// outside Resources, where the game cannot load it by name. Road Rage > Link
    /// Installed Tree Pack finds its prefabs and writes this list to
    /// Resources/Biomes/ExternalVegetation (also not committed). Where the list is
    /// missing or empty, Greenwood uses its own Blender-built trees.
    public sealed class ExternalVegetation : ScriptableObject
    {
        public GameObject[] Trees = System.Array.Empty<GameObject>();
        /// The pack's smaller trees, planted as the understory between the big ones.
        public GameObject[] YoungTrees = System.Array.Empty<GameObject>();
        public string Source = "";
    }
}
