using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// High-performance audio bridge with support for procedural synthesis,
    /// dynamic RPM pitch modulation, crash impacts, gear pops, turbo flutter,
    /// slow-mo low-pass filtering, and FMOD event hooks.
    /// </summary>
    public class RoadRageAudioBridge : MonoBehaviour
    {
        public static RoadRageAudioBridge Instance { get; private set; }

        private AudioSource engineSource;
        private AudioSource turboSource;
        private AudioSource sfxSource;
        private AudioSource crashSource;
        private AudioLowPassFilter lowPassFilter;

        private float targetLowPassCutoff = 22000f;
        private float currentLowPassCutoff = 22000f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            InitializeAudioSources();
        }

        private void InitializeAudioSources()
        {
            engineSource = gameObject.AddComponent<AudioSource>();
            engineSource.loop = true;
            engineSource.playOnAwake = false;
            engineSource.spatialBlend = 0f;
            engineSource.volume = 0.65f;

            turboSource = gameObject.AddComponent<AudioSource>();
            turboSource.loop = false;
            turboSource.playOnAwake = false;
            turboSource.spatialBlend = 0f;
            turboSource.volume = 0.5f;

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.volume = 0.85f;

            crashSource = gameObject.AddComponent<AudioSource>();
            crashSource.loop = false;
            crashSource.playOnAwake = false;
            crashSource.spatialBlend = 0f;
            crashSource.volume = 1f;

            skidSource = gameObject.AddComponent<AudioSource>();
            skidSource.loop = true;
            skidSource.playOnAwake = false;
            skidSource.spatialBlend = 0f;
            skidSource.volume = 0f;
            skidSource.clip = CreateProceduralSkidClip();

            lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
            lowPassFilter.cutoffFrequency = 22000f;

            // Engine loop starts silent (no continuous buzz at rest)
            engineSource.volume = 0f;
        }

        private AudioSource skidSource;

        private void Update()
        {
            // Smoothly interpolate low-pass filter for slow-motion effects
            currentLowPassCutoff = Mathf.Lerp(currentLowPassCutoff, targetLowPassCutoff, Time.unscaledDeltaTime * 8f);
            if (lowPassFilter != null)
                lowPassFilter.cutoffFrequency = currentLowPassCutoff;
        }

        public void PlayTireSqueal(float intensity)
        {
            if (skidSource == null) return;
            if (!GameState.IsAftertouchActive)
            {
                if (skidSource.isPlaying) skidSource.Stop();
                skidSource.volume = 0f;
                return;
            }
            var targetVol = Mathf.Clamp01(intensity) * 0.75f;
            skidSource.volume = Mathf.Lerp(skidSource.volume, targetVol, Time.deltaTime * 14f);
            skidSource.pitch = Mathf.Lerp(0.85f, 1.25f, intensity);
            if (targetVol > 0.05f && !skidSource.isPlaying) skidSource.Play();
            else if (targetVol <= 0.05f && skidSource.isPlaying && skidSource.volume < 0.02f) skidSource.Stop();
        }

        public void PlayBackfirePop()
        {
            if (sfxSource == null) return;
            sfxSource.pitch = Random.Range(0.9f, 1.3f);
            sfxSource.PlayOneShot(CreateProceduralPopClip(), 0.75f);
        }

        public void UpdateEngineAudio(float speedKph, float maxSpeedKph, float throttle, bool isNitro)
        {
            if (engineSource == null) return;

            var speedRatio = Mathf.Clamp01(speedKph / Mathf.Max(1f, maxSpeedKph));
            var targetPitch = Mathf.Lerp(0.8f, 2.1f, speedRatio);
            if (isNitro) targetPitch *= 1.25f;

            engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, Time.unscaledDeltaTime * 6f);
            var isMoving = speedKph > 2f || Mathf.Abs(throttle) > 0.1f;
            var targetVol = isMoving ? Mathf.Lerp(0.15f, 0.65f, Mathf.Max(Mathf.Abs(throttle), speedRatio)) : 0f;
            engineSource.volume = Mathf.Lerp(engineSource.volume, targetVol, Time.unscaledDeltaTime * 10f);
            if (isMoving && !engineSource.isPlaying && engineSource.clip != null) engineSource.Play();
            else if (!isMoving && engineSource.volume < 0.02f && engineSource.isPlaying) engineSource.Stop();
        }

        // Recorded sounds. The synthesized beeps these replace - a falling sine
        // "chirp" for nitro, turbo and near misses, and a sine chord "stinger" on every
        // takedown - were what made a car game sound like a space shooter.
        private AudioClip[] crashHeavy;
        private AudioClip[] crashMedium;
        private AudioClip[] crashLight;
        private float lastCrashAt = -1f;
        private float lastCrashSeverity;
        private AudioClip turboClip;
        private AudioClip nitroClip;
        private AudioClip whooshClip;
        private bool clipsLoaded;

        private void LoadClips()
        {
            if (clipsLoaded) return;
            clipsLoaded = true;
            // The recorded crash from Road Rage 3D, cut to three lengths
            // (Tools/Audio/import_crash_recording.py); the CC0 layered set is the
            // fallback if it is missing.
            crashHeavy = LoadReal("heavy") ?? LoadSet("heavy", 4);
            crashMedium = LoadReal("medium") ?? LoadSet("medium", 4);
            crashLight = LoadReal("light") ?? LoadSet("light", 3);
            turboClip = Resources.Load<AudioClip>("Audio/VPP/turbo");
            nitroClip = Resources.Load<AudioClip>("Audio/SFX/NOS/NOSWhoosh2") ?? Resources.Load<AudioClip>("Audio/SFX/NOS/NOS");
            whooshClip = CreatePassByWhooshClip();
        }

        private static AudioClip[] LoadReal(string weight)
        {
            var clip = Resources.Load<AudioClip>($"Audio/CrashReal/crash_real_{weight}");
            return clip != null ? new[] { clip } : null;
        }

        private static AudioClip[] LoadSet(string weight, int count)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            for (var i = 0; i < count; i++)
            {
                var clip = Resources.Load<AudioClip>($"Audio/CrashCC0/crash_{weight}_{i}");
                if (clip != null) clips.Add(clip);
            }
            return clips.ToArray();
        }

        public void PlayTurboFlutter()
        {
            if (turboSource == null) return;
            LoadClips();
            if (turboClip == null) return;
            turboSource.pitch = Random.Range(0.95f, 1.1f);
            turboSource.PlayOneShot(turboClip, 0.55f);
        }

        /// A real crash: body thump, sheet metal, glass on the big ones, debris
        /// settling (Tools/Audio/build_crash_sounds.py). Heavier hits pick the
        /// heavier set; the pitch varies so no two sound the same.
        public void PlayCrash(float severity = 1f)
        {
            if (crashSource == null) return;
            LoadClips();
            // One hit, one crash: a takedown reports the same impact from the car and
            // from the takedown, and two crashes on top of each other sound like two.
            if (Time.unscaledTime - lastCrashAt < 0.25f && severity <= lastCrashSeverity) return;
            lastCrashAt = Time.unscaledTime;
            lastCrashSeverity = severity;
            var set = severity >= 1f ? crashHeavy : severity >= 0.65f ? crashMedium : crashLight;
            if (set.Length == 0) set = crashMedium.Length > 0 ? crashMedium : crashHeavy;
            crashSource.pitch = Random.Range(0.92f, 1.06f);
            if (set.Length > 0)
                crashSource.PlayOneShot(set[Random.Range(0, set.Length)], Mathf.Clamp(severity, 0.35f, 1f));
            else
                crashSource.PlayOneShot(CreateProceduralCrashClip(), Mathf.Clamp01(severity));
        }

        /// Takedowns already play the crash itself; the musical sting that went with
        /// them is gone.
        public void PlayTakedownStinger()
        {
        }

        public void PlayNitro()
        {
            if (sfxSource == null) return;
            LoadClips();
            if (nitroClip == null) return;
            sfxSource.pitch = Random.Range(0.95f, 1.08f);
            sfxSource.PlayOneShot(nitroClip, 0.7f);
        }

        /// A near miss is the rush of air as a car goes past the window.
        public void PlayNearMissChirp()
        {
            if (sfxSource == null) return;
            LoadClips();
            sfxSource.pitch = Random.Range(0.9f, 1.1f);
            sfxSource.PlayOneShot(whooshClip, 0.55f);
        }

        private AudioClip shutterClip;

        /// A speed camera going off: the double click of a shutter.
        public void PlayCameraShutter()
        {
            if (sfxSource == null) return;
            if (shutterClip == null)
            {
                const int sampleRate = 44100;
                var samples = sampleRate / 8;
                var data = new float[samples];
                for (var i = 0; i < samples; i++)
                {
                    var t = (float)i / sampleRate;
                    var click = Mathf.Exp(-t * 900f) + 0.7f * (t > 0.06f ? Mathf.Exp(-(t - 0.06f) * 700f) : 0f);
                    data[i] = (Random.value * 2f - 1f) * click;
                }
                shutterClip = AudioClip.Create("CameraShutter", samples, 1, sampleRate, false);
                shutterClip.SetData(data, 0);
            }
            sfxSource.pitch = 1f;
            sfxSource.PlayOneShot(shutterClip, 0.8f);
        }

        /// Band-limited noise swelling and falling away: air, not a tone.
        private static AudioClip CreatePassByWhooshClip()
        {
            const int sampleRate = 44100;
            var samples = sampleRate * 6 / 10;
            var data = new float[samples];
            var low = 0f;
            var band = 0f;
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / samples;
                var noise = Random.value * 2f - 1f;
                // Two one-pole filters: a moving band, brightest as the car passes.
                var cutoff = Mathf.Lerp(0.04f, 0.22f, Mathf.Sin(t * Mathf.PI));
                low += (noise - low) * cutoff;
                band += (low - band) * 0.02f;
                var envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.6f) * (t < 0.45f ? t / 0.45f : 1f);
                data[i] = (low - band) * envelope * 1.6f;
            }
            var clip = AudioClip.Create("PassByWhoosh", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void SetSlowMotionFilter(bool enabled)
        {
            targetLowPassCutoff = enabled ? 850f : 22000f;
        }

        // ==================== PROCEDURAL SYNTHESIZERS ====================

        private static AudioClip CreateProceduralEngineClip()
        {
            var sampleRate = 44100;
            var samples = sampleRate / 2; // 0.5 sec loop
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                // Rich multi-harmonic engine rumble
                var wave1 = Mathf.Sin(2f * Mathf.PI * 55f * t);
                var wave2 = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.5f;
                var wave3 = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.25f;
                var noise = (Random.value * 2f - 1f) * 0.08f;
                data[i] = (wave1 + wave2 + wave3 + noise) * 0.4f;
            }
            var clip = AudioClip.Create("ProceduralEngineLoop", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateProceduralCrashClip()
        {
            var sampleRate = 44100;
            var samples = sampleRate; // 1.0 sec
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                var envelope = Mathf.Exp(-t * 5.5f);
                var noise = (Random.value * 2f - 1f);
                var crunch = Mathf.Sin(2f * Mathf.PI * 65f * t * (1f - t * 0.5f));
                data[i] = (noise * 0.7f + crunch * 0.3f) * envelope;
            }
            var clip = AudioClip.Create("ProceduralCrash", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateProceduralChirpClip()
        {
            var sampleRate = 44100;
            var samples = sampleRate / 3; // 0.33 sec
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                var freq = Mathf.Lerp(1200f, 400f, t * 3f);
                var flutter = Mathf.Sin(2f * Mathf.PI * 35f * t);
                var envelope = Mathf.Exp(-t * 9f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * (0.8f + flutter * 0.2f) * envelope * 0.5f;
            }
            var clip = AudioClip.Create("ProceduralTurbo", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateProceduralStingerClip()
        {
            var sampleRate = 44100;
            var samples = sampleRate * 3 / 2; // 1.5 sec
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                var envelope = Mathf.Exp(-t * 2.2f);
                var bass = Mathf.Sin(2f * Mathf.PI * 48f * t);
                var chord1 = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.3f;
                var chord2 = Mathf.Sin(2f * Mathf.PI * 330f * t) * 0.25f;
                var chord3 = Mathf.Sin(2f * Mathf.PI * 440f * t) * 0.2f;
                data[i] = (bass * 0.5f + chord1 + chord2 + chord3) * envelope;
            }
            var clip = AudioClip.Create("ProceduralStinger", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateProceduralSkidClip()
        {
            var sampleRate = 44100;
            var samples = sampleRate / 2; // 0.5 sec loop
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                var noise = (Random.value * 2f - 1f) * 0.4f;
                var squeal1 = Mathf.Sin(2f * Mathf.PI * 1600f * t) * 0.25f;
                var squeal2 = Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.15f;
                data[i] = noise + squeal1 + squeal2;
            }
            var clip = AudioClip.Create("ProceduralSkidLoop", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateProceduralPopClip()
        {
            var sampleRate = 44100;
            var samples = sampleRate / 6; // 0.16 sec pop
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / sampleRate;
                var env = Mathf.Exp(-t * 35f);
                var boom = Mathf.Sin(2f * Mathf.PI * 90f * t);
                var crack = (Random.value * 2f - 1f) * 0.8f;
                data[i] = (boom * 0.6f + crack * 0.4f) * env;
            }
            var clip = AudioClip.Create("ProceduralPop", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

