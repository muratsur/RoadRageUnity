using UnityEngine;

namespace RoadRage.UnityRemake
{
    /// Camera shake, and the haptic pulse that goes with it (RoadRageHaptics).
    ///
    /// Every impact in the game - a knock, a solid hit, a wreck - comes through the
    /// three Trigger calls below, so they are also where the gamepad and phone pulse.
    /// Only discrete pulses: the constant rumble at speed this once had told a player
    /// nothing and drained a phone.
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

        /// Impact shake and haptics, in three strengths: a glancing knock, a solid hit,
        /// and a wreck.
        public void TriggerLightShake(float trauma = 0.25f)
        {
            AddTrauma(trauma);
            RoadRageHaptics.Light();
        }

        public void TriggerMediumShake(float trauma = 0.55f)
        {
            AddTrauma(trauma);
            RoadRageHaptics.Medium();
        }

        public void TriggerHeavyCrashShake(float trauma = 1.0f)
        {
            AddTrauma(trauma);
            RoadRageHaptics.Heavy();
        }
    }
}
