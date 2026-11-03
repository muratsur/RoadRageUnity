using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Blue lights and the German two-tone horn on an ambulance or fire engine, until it is
    /// wrecked.
    public sealed class EmergencyVehicle : MonoBehaviour
    {
        private const float FlashesPerSecond = 3f;
        private const int SampleRate = 22050;
        private const float ToneSeconds = 0.7f;
        private const float LowTone = 440f;
        private const float HighTone = 585f;
        private static AudioClip horn;

        private TrafficCarController car;
        private Renderer[] beacons;
        private Light glow;
        private AudioSource siren;

        public void Bind(TrafficCarController controller, Renderer[] lights)
        {
            car = controller;
            beacons = lights;
            var holder = new GameObject("Emergency Glow");
            holder.transform.SetParent(transform, false);
            holder.transform.position = lights[0].transform.position + Vector3.up * 0.4f;
            glow = holder.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.2f, 0.35f, 1f);
            glow.range = 16f;
            glow.intensity = 6f;
            glow.shadows = LightShadows.None;
            siren = gameObject.AddComponent<AudioSource>();
            siren.clip = horn != null ? horn : horn = MakeHorn();
            siren.loop = true;
            siren.spatialBlend = 1f;
            siren.rolloffMode = AudioRolloffMode.Linear;
            siren.minDistance = 15f;
            siren.maxDistance = 140f;
            siren.volume = 0.28f;
        }

        private void Update()
        {
            if (car == null) return;
            var on = car.OnCall && !car.IsWreck;
            var phase = Mathf.Repeat(Time.time * FlashesPerSecond, 1f) < 0.5f;
            for (var i = 0; i < beacons.Length; i++)
                if (beacons[i] != null) beacons[i].enabled = on && (i % 2 == 0) == phase;
            glow.enabled = on && phase;
            if (!on && siren.isPlaying) siren.Stop();
            else if (on && !siren.isPlaying) siren.Play();
        }

        private static AudioClip MakeHorn()
        {
            var samples = Mathf.RoundToInt(SampleRate * ToneSeconds * 2f);
            var data = new float[samples];
            var angle = 0f;
            for (var i = 0; i < samples; i++)
            {
                var tone = i < samples / 2 ? LowTone : HighTone;
                angle += tone * 2f * Mathf.PI / SampleRate;
                // Harmonics: a horn, not a pure whistle.
                data[i] = 0.5f * Mathf.Sin(angle) + 0.25f * Mathf.Sin(angle * 2f) + 0.15f * Mathf.Sin(angle * 3f);
            }
            var clip = AudioClip.Create("Martinshorn", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
