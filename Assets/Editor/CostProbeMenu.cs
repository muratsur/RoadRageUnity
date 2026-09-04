using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Runs the RR_COST probe from the menu bar.
    ///
    /// The probe has always been bound to P, read in RoadRageBootstrap.Update. That only
    /// fires while the Game view holds keyboard focus, and the one thing anybody does
    /// before pressing P is click the Console to filter it for RR_COST - which takes the
    /// focus away. The keypress then goes to the editor chrome, Update never sees it, and
    /// the Console the user is staring at stays empty with nothing to explain why. The
    /// menu item has no focus to lose, so the measurement no longer depends on where the
    /// last click landed. P still works.
    public static class CostProbeMenu
    {
        [MenuItem("Road Rage/Measure Live World Cost %#p", true)]
        private static bool ValidateMeasure() => Application.isPlaying;

        [MenuItem("Road Rage/Measure Live World Cost %#p")]
        private static void Measure() => RoadRageBootstrap.MeasureLiveWorldCost();
    }
}
