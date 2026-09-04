using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Camera shake, and nothing else.
    ///
    /// This was a haptics director: screen shake plus gamepad dual-motor rumble, mobile
    /// Handheld.Vibrate on a heavy crash, and a continuous micro-rumble above 175 km/h.
    /// All of the vibration is gone. The shipped build has no haptics of any kind - not
    /// one reference to vibration in 8,500 lines of game.gd - and it sells an impact with
    /// a single scalar shake, which is what its own comment on a takedown says outright:
    /// "a strong shake reads as the crunch (no slow-mo)".
    ///
    /// The shake stays because the shipped build has that too. What went is everything
    /// that buzzed a device: a constant rumble at speed gives a player nothing to read,
    /// and on a phone it is a battery drain you cannot turn off.
    public sealed class RoadRageImpactShakeDirector : MonoBehaviour
    {
        public static RoadRageImpactShakeDirector Instance { get; private set; }

        public float Trauma { get; private set; } = 0f;
        public Vector3 CurrentShakeOffset { get; private set; } = Vector3.zero;
        public Quaternion CurrentShakeRotation { get; private set; } = Quaternion.identity;

        private float noiseSeed;

        private const float MaxShakeTranslation = 0.45f;
        private const float MaxShakeRotation = 4.5f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            noiseSeed = Random.value * 100f;
        }

        private void Update()
        {
            // Exponential trauma decay
            if (Trauma > 0f)
            {
                Trauma = Mathf.Max(0f, Trauma - Time.unscaledDeltaTime * 1.6f);
            }

            var effectiveTrauma = Mathf.Clamp01(Trauma);
            var shakePower = effectiveTrauma * effectiveTrauma; // non-linear power curve

            if (shakePower > 0.001f)
            {
                var time = Time.unscaledTime * 28f;
                var nx = (Mathf.PerlinNoise(noiseSeed, time) - 0.5f) * 2f;
                var ny = (Mathf.PerlinNoise(noiseSeed + 10f, time) - 0.5f) * 2f;
                var nz = (Mathf.PerlinNoise(noiseSeed + 20f, time) - 0.5f) * 2f;

                CurrentShakeOffset = new Vector3(nx, ny, nz) * (MaxShakeTranslation * shakePower);

                var rx = (Mathf.PerlinNoise(noiseSeed + 30f, time) - 0.5f) * 2f * MaxShakeRotation * shakePower;
                var ry = (Mathf.PerlinNoise(noiseSeed + 40f, time) - 0.5f) * 2f * MaxShakeRotation * shakePower;
                var rz = (Mathf.PerlinNoise(noiseSeed + 50f, time) - 0.5f) * 2f * MaxShakeRotation * shakePower;

                CurrentShakeRotation = Quaternion.Euler(rx, ry, rz);
            }
            else
            {
                CurrentShakeOffset = Vector3.zero;
                CurrentShakeRotation = Quaternion.identity;
            }
        }

        public void AddTrauma(float amount)
        {
            Trauma = Mathf.Clamp01(Trauma + amount);
        }

        /// Impact shake, scaled the way the shipped build scales it: a glancing knock, a
        /// solid hit, and a wreck. No vibration attached to any of them.
        public void TriggerLightShake(float trauma = 0.25f) => AddTrauma(trauma);

        public void TriggerMediumShake(float trauma = 0.55f) => AddTrauma(trauma);

        public void TriggerHeavyCrashShake(float trauma = 1.0f) => AddTrauma(trauma);
    }
}
