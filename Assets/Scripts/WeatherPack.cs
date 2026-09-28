using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Weather sound from a third-party pack installed in this copy of the project
    /// (URP Dynamic Weather System, Asset Store): its rain, storm and wind loops. The
    /// pack's own rain particles are thinner than the game's (400 drops a second
    /// falling straight down in a 25 m square), so the game keeps its precipitation
    /// and only takes the sound, which it had none of. The pack is licensed for use,
    /// not redistribution, so it is never committed, and it installs outside
    /// Resources. Road Rage > Link Weather Pack finds the loops and writes this to
    /// Resources/Weather/WeatherPack (also not committed).
    public sealed class WeatherPack : ScriptableObject
    {
        public AudioClip Rain;
        public AudioClip Storm;
        public AudioClip Wind;
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

        /// The loop for a weather, and how loud it sits under the engine and music.
        public (AudioClip clip, float volume) For(WeatherKind kind) => kind switch
        {
            WeatherKind.Rain => (Rain, 0.45f),
            WeatherKind.Storm => (Storm != null ? Storm : Rain, 0.6f),
            WeatherKind.Snow => (Wind, 0.35f),
            WeatherKind.Fog => (Wind, 0.12f),
            _ => (null, 0f),
        };
    }
}
