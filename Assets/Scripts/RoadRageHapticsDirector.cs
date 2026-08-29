using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Multi-platform Haptic & Screen Shake Director:
    /// Delivers camera screen-shake trauma, micro-rumble at high speed,
    /// gamepad dual-motor rumble, and mobile haptic impulses.
    /// </summary>
    public sealed class RoadRageHapticsDirector : MonoBehaviour
    {
        public static RoadRageHapticsDirector Instance { get; private set; }

        public float Trauma { get; private set; } = 0f;
        public Vector3 CurrentShakeOffset { get; private set; } = Vector3.zero;
        public Quaternion CurrentShakeRotation { get; private set; } = Quaternion.identity;

        private Coroutine rumbleCoroutine;
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

            // High-speed speed rumble (subtle continuous vibration when driving >175 km/h)
            var speedRumble = 0f;
            var playerSpeed = GameState.CurrentSpeedKph;
            if (playerSpeed > 175f)
            {
                var factor = Mathf.Clamp01((playerSpeed - 175f) / 75f);
                speedRumble = factor * 0.18f;
            }

            var effectiveTrauma = Mathf.Clamp01(Trauma + speedRumble);
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

        public void TriggerLightHaptic(float trauma = 0.25f)
        {
            AddTrauma(trauma);
            Rumble(0.15f, 0.35f, 0.12f);
        }

        public void TriggerMediumHaptic(float trauma = 0.55f)
        {
            AddTrauma(trauma);
            Rumble(0.45f, 0.75f, 0.22f);
        }

        public void TriggerHeavyCrashHaptic(float trauma = 1.0f)
        {
            AddTrauma(trauma);
            Rumble(0.95f, 1.0f, 0.45f);
#if UNITY_ANDROID || UNITY_IOS
            try { Handheld.Vibrate(); } catch { }
#endif
        }

        private void Rumble(float lowFreq, float highFreq, float duration)
        {
            var pad = Gamepad.current;
            if (pad == null) return;

            if (rumbleCoroutine != null) StopCoroutine(rumbleCoroutine);
            rumbleCoroutine = StartCoroutine(RumbleRoutine(pad, lowFreq, highFreq, duration));
        }

        private IEnumerator RumbleRoutine(Gamepad pad, float low, float high, float dur)
        {
            pad.SetMotorSpeeds(low, high);
            var elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = 1f - (elapsed / dur);
                pad.SetMotorSpeeds(low * t, high * t);
                yield return null;
            }
            pad.SetMotorSpeeds(0f, 0f);
            rumbleCoroutine = null;
        }

        private void OnDisable()
        {
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }
    }
}
