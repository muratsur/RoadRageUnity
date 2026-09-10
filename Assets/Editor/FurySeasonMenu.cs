using System;
using System.Globalization;
using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Moves the Fury Pass season clock, so the pass can be tested on demand.
    ///
    /// Seasons run 28 days from a fixed epoch every install shares. That is right for
    /// shipping and awkward for testing: whether you get four days or twenty-four to
    /// look at the pass depends on the date you happen to open the project, and a season
    /// can wipe itself mid-test. These repoint the clock for this install only.
    public static class FurySeasonMenu
    {
        [MenuItem("Road Rage/Fury Pass/Start a Fresh Season Now")]
        private static void StartFreshSeason()
        {
            GameState.SetFurySeasonEpoch(DateTime.Now.Date);
            GameState.RollFurySeason();
            Report("season restarted from today");
        }

        [MenuItem("Road Rage/Fury Pass/End This Season (test the wipe)")]
        private static void EndSeason()
        {
            // Pull the epoch back a whole season: the index moves on by one, so the next
            // roll treats it as a new season and wipes the track - what a player sees at
            // a real rollover, without waiting for one.
            GameState.SetFurySeasonEpoch(GameState.FurySeasonEpoch.AddDays(-GameState.FurySeasonDays));
            GameState.RollFurySeason();
            Report("season rolled over - track wiped");
        }

        [MenuItem("Road Rage/Fury Pass/Restore Default Schedule")]
        private static void RestoreDefault()
        {
            GameState.ClearFurySeasonEpoch();
            GameState.RollFurySeason();
            Report("back on the shipping schedule");
        }

        [MenuItem("Road Rage/Fury Pass/Report Season Window")]
        private static void ReportOnly() => Report("current window");

        private static void Report(string what)
        {
            var fmt = "yyyy-MM-dd";
            Debug.Log($"RR_SEASON {what}: season={GameState.FurySeason} " +
                      $"{GameState.FurySeasonStart.ToString(fmt, CultureInfo.InvariantCulture)} -> " +
                      $"{GameState.FurySeasonEnd.ToString(fmt, CultureInfo.InvariantCulture)} " +
                      $"daysLeft={GameState.FuryDaysLeft} tier={GameState.FuryTier}/{GameState.FuryTiers} " +
                      $"xp={GameState.FuryXp} pro={GameState.FuryPro} " +
                      $"epoch={GameState.FurySeasonEpoch.ToString(fmt, CultureInfo.InvariantCulture)}" +
                      $"{(GameState.FurySeasonEpochOverridden ? " (overridden)" : " (default)")}");
        }
    }
}
