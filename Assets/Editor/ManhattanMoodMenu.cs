using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Switches Manhattan between its night and daylight moods from the menu bar.
    ///
    /// Both moods are kept deliberately - which one Manhattan should wear is a call for
    /// whoever is looking at it - but the switch was reachable only through
    /// -manhattan=day or by calling SetManhattanDaylight by hand, so in practice nobody
    /// could see the other one.
    ///
    /// Toggling rebuilds the biome when Manhattan is loaded. Moods are applied by
    /// BuildLighting, which ReloadBiome re-runs, so without the rebuild the pref changes
    /// and the scene in front of you does not.
    ///
    /// Night is the default: the window emission, street lamps, neon and baked grime were
    /// all tuned against it. In daylight the lit-window pattern and the neon read much
    /// weaker - expected, not a bug.
    public static class ManhattanMoodMenu
    {
        private const string DaylightItem = "Road Rage/Manhattan Daylight";
        private const string GoldenHourItem = "Road Rage/Manhattan Golden-Hour Dusk";

        [MenuItem(DaylightItem, true)]
        private static bool ValidateDaylight()
        {
            Menu.SetChecked(DaylightItem, RoadRageBootstrap.ManhattanIsDaylight);
            return true;
        }

        [MenuItem(DaylightItem)]
        private static void ToggleDaylight()
        {
            var daylight = !RoadRageBootstrap.ManhattanIsDaylight;
            RoadRageBootstrap.SetManhattanDaylight(daylight);
            Menu.SetChecked(DaylightItem, daylight);
            Menu.SetChecked(GoldenHourItem, RoadRageBootstrap.ManhattanIsGoldenHour);
            RebuildIfActive();
        }

        [MenuItem(GoldenHourItem, true)]
        private static bool ValidateGoldenHour()
        {
            Menu.SetChecked(GoldenHourItem, RoadRageBootstrap.ManhattanIsGoldenHour);
            return true;
        }

        [MenuItem(GoldenHourItem)]
        private static void ToggleGoldenHour()
        {
            var golden = !RoadRageBootstrap.ManhattanIsGoldenHour;
            RoadRageBootstrap.SetManhattanGoldenHour(golden);
            Menu.SetChecked(GoldenHourItem, golden);
            Menu.SetChecked(DaylightItem, RoadRageBootstrap.ManhattanIsDaylight);
            RebuildIfActive();
        }

        private static void RebuildIfActive()
        {
            var world = RoadRageBootstrap.Instance;
            if (world == null) return;
            if (world.BiomeName == "MANHATTAN" || world.BiomeName == "FDR DRIVE")
                world.ReloadBiome(world.BiomeName);
            else
                Debug.Log($"RR_MOOD {world.BiomeName} is loaded, so nothing rebuilt. " +
                          "The mood applies next time Manhattan or FDR Drive is built.");
        }
    }
}
