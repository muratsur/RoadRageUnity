using UnityEditor;
using UnityEngine;
using RoadRage.UnityRemake;

namespace RoadRage.Editor
{
    /// Editor helper: runs the LoopSelfTest without relaunching the Editor with the
    /// -selftest command-line flag. Normally the bootstrap adds LoopSelfTest in Awake only
    /// when -selftest is present; when the Editor is already open (no such flag), this menu
    /// item enters Play mode if needed and attaches LoopSelfTest to the live bootstrap.
    ///
    /// Entering Play mode triggers a domain reload, which clears static delegates - so a
    /// plain "subscribe then EnterPlaymode" from the menu loses its callback before it can
    /// fire. Two things make it survive: [InitializeOnLoad] re-subscribes to
    /// playModeStateChanged after every domain reload (including the play-mode one), and the
    /// pending request is stashed in SessionState, which persists across reloads for the
    /// editor session. So the menu works whether or not the game is already playing.
    ///
    /// The test snapshots and restores the player's save around itself, so running it does
    /// not cost real progress. Watch the Console for the "RR_TEST RESULT PASS/FAIL" line,
    /// then stop Play mode. This script lives under Assets/Editor so it never ships in a
    /// player build; delete it freely.
    [InitializeOnLoad]
    public static class RoadRageSelfTest
    {
        private const string PendingKey = "RoadRage.SelfTest.Pending";

        // Runs after every domain reload, re-registering the handler that a reload wiped.
        static RoadRageSelfTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Road Rage/Run Self-Test (Play)")]
        public static void RunSelfTest()
        {
            if (EditorApplication.isPlaying)
            {
                Attach();
                return;
            }

            // SessionState survives the play-mode domain reload; the reloaded static ctor
            // re-subscribes OnPlayModeChanged, which then reads this flag and attaches.
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            // One editor tick after entering Play mode so the bootstrap's
            // RuntimeInitializeOnLoadMethod has created the world and its Instance.
            EditorApplication.delayCall += Attach;
        }

        private static void Attach()
        {
            var bootstrap = Object.FindAnyObjectByType<RoadRageBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogWarning("RR_TEST: no RoadRageBootstrap yet - once the game is visible, " +
                                 "click 'Road Rage/Run Self-Test (Play)' again.");
                return;
            }

            if (bootstrap.GetComponent<LoopSelfTest>() != null)
            {
                Debug.Log("RR_TEST: self-test already running on this play session.");
                return;
            }

            bootstrap.gameObject.AddComponent<LoopSelfTest>();
            Debug.Log("RR_TEST: self-test attached - watch the Console for 'RR_TEST RESULT PASS/FAIL'.");
        }
    }
}
