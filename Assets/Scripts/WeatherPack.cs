using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Rain, storm and snow effects from a third-party weather pack installed in this
    /// copy of the project (URP Dynamic Weather System, Asset Store). The pack is
    /// licensed for use, not redistribution, so it is never committed, and it installs
    /// outside Resources. Road Rage > Link Weather Pack finds its effects and writes this
    /// to Resources/Weather/WeatherPack (also not committed). Where an effect is missing
    /// the game keeps its own particles for that weather.
    public sealed class WeatherPack : ScriptableObject
    {
        public GameObject Rain;
        public GameObject Storm;
        public GameObject Snow;
        public string Source = "";

        private static WeatherPack loaded;
        private static bool tried;

        public static WeatherPack Linked
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    loaded = Resources.Load<WeatherPack>("Weather/WeatherPack");
                }
                return loaded;
            }
        }

        public GameObject For(WeatherKind kind) => kind switch
        {
            WeatherKind.Rain => Rain,
            WeatherKind.Storm => Storm != null ? Storm : Rain,
            WeatherKind.Snow => Snow,
            _ => null,
        };
    }
}
