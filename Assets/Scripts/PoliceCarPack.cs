using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// The police cruiser's body from a third-party car pack installed in this copy of
    /// the project (Realistic Mobile Car #26, Asset Store). The pack is licensed for
    /// use, not redistribution, so it is never committed, and it installs outside
    /// Resources. Road Rage > Link Police Car Pack finds the car and writes this to
    /// Resources/Police/PoliceCarPack (also not committed). Without it the cruisers
    /// keep their Synty sedan.
    public sealed class PoliceCarPack : ScriptableObject
    {
        public GameObject Car;
        public string Source = "";

        private static PoliceCarPack loaded;
        private static bool tried;

        public static GameObject LinkedCar
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    loaded = Resources.Load<PoliceCarPack>("Police/PoliceCarPack");
                }
                return loaded != null ? loaded.Car : null;
            }
        }
    }
}
