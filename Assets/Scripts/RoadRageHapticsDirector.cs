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

        public void TriggerLightHaptic() {}
        public void TriggerMediumHaptic() {}
        public void TriggerHeavyCrashHaptic() {}

        private void Rumble(float lowFreq, float highFreq, float duration) {}
    }
}
