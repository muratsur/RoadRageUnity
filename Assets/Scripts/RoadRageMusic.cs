using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Background music: the Road Rage 3D soundtrack (Assets/Resources/Audio/MusicRR3D,
    /// from muratsur/roadrage3d). Every run starts a random track, never the one that
    /// was just playing; when a track ends the next one crossfades in, so the music never
    /// stops or cuts. Settings > MUSIC turns it off (kept in PlayerPrefs). Two voices,
    /// the same way the original game crossfaded its loop.
    public sealed class RoadRageMusic : MonoBehaviour
    {
        public static RoadRageMusic Instance { get; private set; }

        private const string PrefKey = "RR_MUSIC";
        private const float Volume = 0.32f;
        private const float Crossfade = 2.5f;

        private static readonly string[] Tracks =
        {
            "Audio/MusicRR3D/music",         // the original synthwave bed
            "Audio/MusicRR3D/music_drive",   // dusty open road
            "Audio/MusicRR3D/music_calm",    // green, serene
            "Audio/MusicRR3D/music_zen",     // zen
        };

        private AudioSource a;
        private AudioSource b;
        private AudioSource lead;
        private int current = -1;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (Instance == null) return;
                if (value) Instance.Next();
                else Instance.StopAll();
            }
        }

        private void Awake()
        {
            Instance = this;
            a = MakeVoice();
            b = MakeVoice();
            lead = a;
        }

        private void Start()
        {
            if (Enabled) Next();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private AudioSource MakeVoice()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            // Unaffected by the audio bridge's filters and by pausing gameplay.
            source.ignoreListenerPause = true;
            source.priority = 0;
            return source;
        }

        /// A new run: a different track, crossfaded in.
        public void Next()
        {
            if (!Enabled) return;
            var pick = Random.Range(0, Tracks.Length);
            if (Tracks.Length > 1 && pick == current) pick = (pick + 1 + Random.Range(0, Tracks.Length - 1)) % Tracks.Length;
            var clip = Resources.Load<AudioClip>(Tracks[pick]);
            if (clip == null)
            {
                Debug.LogWarning($"Missing music {Tracks[pick]}");
                return;
            }
            current = pick;
            var incoming = lead == a ? b : a;
            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();
            lead = incoming;
        }

        private void StopAll()
        {
            a.Stop();
            b.Stop();
        }

        private void Update()
        {
            if (!Enabled) return;
            // Near the end of the lead track, bring in the next one.
            if (lead.clip != null && lead.isPlaying && lead.clip.length - lead.time <= Crossfade)
                Next();
            else if (!lead.isPlaying && lead.clip != null && Time.frameCount % 30 == 0)
                Next();

            var step = Time.unscaledDeltaTime / Crossfade * Volume;
            foreach (var voice in new[] { a, b })
            {
                var target = voice == lead ? Volume : 0f;
                voice.volume = Mathf.MoveTowards(voice.volume, target, step);
                if (voice != lead && voice.isPlaying && voice.volume <= 0.001f) voice.Stop();
            }
        }
    }
}
