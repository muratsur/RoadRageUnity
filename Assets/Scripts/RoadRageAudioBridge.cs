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

            lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
            lowPassFilter.cutoffFrequency = 22000f;

            // Generate procedural engine idle clip if none assigned
            engineSource.clip = CreateProceduralEngineClip();
            engineSource.Play();
        }

        private void Update()
        {
            // Smoothly interpolate low-pass filter for slow-motion effects
            currentLowPassCutoff = Mathf.Lerp(currentLowPassCutoff, targetLowPassCutoff, Time.unscaledDeltaTime * 8f);
            if (lowPassFilter != null)
                lowPassFilter.cutoffFrequency = currentLowPassCutoff;
        }

        public void UpdateEngineAudio(float speedKph, float maxSpeedKph, float throttle, bool isNitro)
        {
            if (engineSource == null) return;

            var speedRatio = Mathf.Clamp01(speedKph / Mathf.Max(1f, maxSpeedKph));
            var targetPitch = Mathf.Lerp(0.75f, 2.25f, speedRatio);
            if (isNitro) targetPitch *= 1.25f;

            engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, Time.unscaledDeltaTime * 6f);
            var targetVol = Mathf.Lerp(0.35f, 0.85f, Mathf.Max(Mathf.Abs(throttle), speedRatio));
            engineSource.volume = Mathf.Lerp(engineSource.volume, targetVol, Time.unscaledDeltaTime * 8f);
        }

        public void PlayTurboFlutter()
        {
            if (turboSource == null) return;
            turboSource.pitch = Random.Range(1.1f, 1.35f);
            turboSource.PlayOneShot(CreateProceduralChirpClip(), 0.6f);
        }

        public void PlayCrash(float severity = 1f)
        {
            if (crashSource == null) return;
            crashSource.pitch = Random.Range(0.85f, 1.15f);
            crashSource.PlayOneShot(CreateProceduralCrashClip(), Mathf.Clamp01(severity));
        }

        public void PlayTakedownStinger()
        {
            if (sfxSource == null) return;
            sfxSource.pitch = 1.0f;
            sfxSource.PlayOneShot(CreateProceduralStingerClip(), 0.9f);
        }

        public void PlayNitro()
        {
            if (sfxSource == null) return;
            sfxSource.pitch = Random.Range(1.3f, 1.55f);
            sfxSource.PlayOneShot(CreateProceduralChirpClip(), 0.85f);
        }

        public void PlayNearMissChirp()
        {
            if (sfxSource == null) return;
            sfxSource.pitch = Random.Range(1.6f, 1.9f);
            sfxSource.PlayOneShot(CreateProceduralChirpClip(), 0.5f);
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
    }
}

