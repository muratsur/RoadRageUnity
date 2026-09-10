using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Wipes the save back to a first-launch profile, from the menu bar.
    ///
    /// Wanted because the loop self-test used to bank cash into the real save on every
    /// run, which inflated a dev profile to millions and made the whole economy
    /// untestable - at that balance the pass, the spin ladder and the revive fees are
    /// all free, so no price can be judged. The test brackets itself now, but an
    /// already-inflated profile still needs a way back.
    ///
    /// Destructive and not undoable, so it asks first, and it asks separately about the
    /// best score: that is a record rather than progression and is usually worth keeping.
    public static class ResetProfileMenu
    {
        [MenuItem("Road Rage/Reset Profile...")]
        private static void Reset()
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "Reset Road Rage profile?",
                "Deletes cash, cars, upgrades, tuning, daily missions and the login streak, " +
                "plus the wheel, the Fury Pass, gems and revive tokens.\n\n" +
                "The chosen track and your pilot tag are kept. This cannot be undone.",
                "Reset, keep best score",
                "Cancel",
                "Reset everything");

            if (choice == 1) return;

            var keepHighScore = choice == 0;
            GameState.ResetProfile(keepHighScore);

            Debug.Log($"RR_RESET profile wiped. cash={GameState.Cash} gems={GameState.Gems} " +
                      $"spins={GameState.WheelSpins} charges={GameState.DoubleCharges} " +
                      $"tokens={GameState.ReviveTokens} furyXp={GameState.FuryXp} " +
                      $"cars={GameState.OwnedCars.Count} streak={GameState.LoginStreak} " +
                      $"bestScore={(keepHighScore ? "kept" : "cleared")}");

            if (!Application.isPlaying)
                Debug.Log("RR_RESET done outside Play mode - the keys are gone, so the next play starts fresh.");
        }
    }
}
