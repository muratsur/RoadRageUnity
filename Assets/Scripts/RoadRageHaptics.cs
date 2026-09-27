using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadRage.UnityRemake
{
    /// Haptics: short, distinct pulses for things that happen to the car, on a gamepad
    /// and on a phone. Three strengths - a knock, a solid hit, a wreck - fired from the
    /// same places as the impact shake (RoadRageImpactShakeDirector), plus a faint
    /// gamepad rumble while nitro is burning.
    ///
    /// They were taken out once as a constant rumble at speed, which tells a player
    /// nothing and drains a phone. Nothing here runs continuously on a phone; each
    /// pulse is tens of milliseconds. Settings > HAPTICS turns them off (kept in
    /// PlayerPrefs).
    ///
    /// Android: VibrationEffect with a strength (API 26+), a plain timed buzz below.
    /// iOS: the only vibration Unity exposes without a native plugin is the long system
    /// buzz, so there only a wreck vibrates.
    public static class RoadRageHaptics
    {
        private const string PrefKey = "RR_HAPTICS";
        private static float padUntil;
        private static HapticsRunner runner;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) StopPad();
            }
        }

        public static void Light() => Pulse(0.10f, 0.30f, 0.07f, 18, 70);
        public static void Medium() => Pulse(0.35f, 0.60f, 0.14f, 40, 150);
        public static void Heavy() => Pulse(0.85f, 1.00f, 0.32f, 90, 255);

        private static void Pulse(float low, float high, float seconds, int phoneMs, int phoneAmplitude)
        {
            if (!Enabled) return;
            var pad = Gamepad.current;
            if (pad != null)
            {
                pad.SetMotorSpeeds(low, high);
                padUntil = Time.unscaledTime + seconds;
                EnsureRunner();
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidVibrate(phoneMs, phoneAmplitude);
#elif UNITY_IOS && !UNITY_EDITOR
            if (phoneAmplitude >= 255) Handheld.Vibrate();
#endif
        }

        private static void StopPad()
        {
            var pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
            padUntil = 0f;
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("Road Rage Haptics") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<HapticsRunner>();
        }

        /// Ends each gamepad pulse on time, holds the faint nitro rumble, and never
        /// leaves a motor running when the game is paused or loses focus.
        private sealed class HapticsRunner : MonoBehaviour
        {
            private void Update()
            {
                var pad = Gamepad.current;
                if (pad == null || Time.unscaledTime < padUntil) return;
                var boosting = Enabled && Time.timeScale > 0f && RoadRageBoostDirector.Instance != null &&
                               RoadRageBoostDirector.Instance.IsBoosting;
                pad.SetMotorSpeeds(boosting ? 0.08f : 0f, boosting ? 0.04f : 0f);
            }

            private void OnApplicationFocus(bool focused)
            {
                if (!focused) StopPad();
            }

            private void OnApplicationPause(bool paused)
            {
                if (paused) StopPad();
            }

            private void OnDisable() => StopPad();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject vibrator;
        private static int sdk;

        private static void AndroidVibrate(int milliseconds, int amplitude)
        {
            try
            {
                if (vibrator == null)
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    using var version = new AndroidJavaClass("android.os.Build$VERSION");
                    sdk = version.GetStatic<int>("SDK_INT");
                }
                if (vibrator == null) return;
                if (sdk >= 26)
                {
                    using var effects = new AndroidJavaClass("android.os.VibrationEffect");
                    using var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", (long)milliseconds,
                        Mathf.Clamp(amplitude, 1, 255));
                    vibrator.Call("vibrate", effect);
                }
                else
                {
                    vibrator.Call("vibrate", (long)milliseconds);
                }
            }
            catch (System.Exception)
            {
                // No vibrator, or the call is refused: haptics are a nicety, never a crash.
            }
        }

        // Never called. Unity adds the VIBRATE permission to the Android manifest only
        // when it finds Handheld.Vibrate in the game's code.
        private static void DeclareVibratePermission() => Handheld.Vibrate();
#endif
    }
}
