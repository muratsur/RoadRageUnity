using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Runs the RR_TEST loop self-test from the menu bar, in Play mode.
    ///
    /// The test has only ever been reachable through the -selftest command-line flag,
    /// which means a full player build - several minutes and ~600 MB - to check a change
    /// to the run economy. HasCommandLineFlag reads the process arguments, and in the
    /// Editor those belong to the Editor, so the flag can never be set from Play mode.
    /// This attaches the component directly instead, the same way CostProbeMenu reaches
    /// the RR_COST probe.
    ///
    /// The test takes roughly 25 seconds and drives the economy directly. It restores
    /// the save it started with when it finishes, so running it does not cost progress.
    public static class SelfTestMenu
    {
        [MenuItem("Road Rage/Run Loop Self Test (RR_TEST) %#t", true)]
        private static bool ValidateRun() =>
            Application.isPlaying && Object.FindAnyObjectByType<LoopSelfTest>() == null;

        [MenuItem("Road Rage/Run Loop Self Test (RR_TEST) %#t")]
        private static void Run()
        {
            var host = new GameObject("RR Loop Self Test");
            host.AddComponent<LoopSelfTest>();
            Debug.Log("RR_TEST started from the menu - filter the Console for RR_TEST. " +
                      "Takes about 25 seconds; your save is restored at the end.");
        }
    }
}
