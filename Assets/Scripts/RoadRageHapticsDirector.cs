using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadRage.UnityRemake
{
    /// <summary>
    /// Multi-platform Haptic & Force Feedback Director:
    /// Delivers tactical gamepad rumble and mobile vibration for near-misses,
    /// nitro boosts, takedowns, and Crashbreaker explosions.
    /// </summary>
    public sealed class RoadRageHapticsDirector : MonoBehaviour
    {
        public static RoadRageHapticsDirector Instance { get; private set; }

        private Coroutine rumbleCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
        }

        public void TriggerLightHaptic()
        {
            Rumble(0.15f, 0.25f, 0.08f);
        }

        public void TriggerMediumHaptic()
        {
            Rumble(0.45f, 0.65f, 0.18f);
            #if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
            #endif
        }

        public void TriggerHeavyCrashHaptic()
        {
            Rumble(0.95f, 1.0f, 0.45f);
            #if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
            #endif
        }

        private void Rumble(float lowFreq, float highFreq, float duration)
        {
            var pad = Gamepad.current;
            if (pad == null) return;

            if (rumbleCoroutine != null) StopCoroutine(rumbleCoroutine);
            rumbleCoroutine = StartCoroutine(RumbleRoutine(pad, lowFreq, highFreq, duration));
        }

        private IEnumerator RumbleRoutine(Gamepad pad, float lowFreq, float highFreq, float duration)
        {
            try
            {
                pad.SetMotorSpeeds(lowFreq, highFreq);
            }
            catch {}

            yield return new WaitForSecondsRealtime(duration);

            try
            {
                pad.SetMotorSpeeds(0f, 0f);
            }
            catch {}
        }

        private void OnDisable()
        {
            var pad = Gamepad.current;
            if (pad != null)
            {
                try { pad.SetMotorSpeeds(0f, 0f); } catch {}
            }
        }
    }
}
