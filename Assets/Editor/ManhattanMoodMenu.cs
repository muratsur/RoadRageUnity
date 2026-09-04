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

            var world = RoadRageBootstrap.Instance;
            if (world == null) return;
            // Only worth rebuilding what the change can be seen in. Reloading Greenwood
            // to show a Manhattan mood would throw away the world you are standing in for
            // no visible reason.
            if (world.BiomeName == "MANHATTAN") world.ReloadBiome(world.BiomeName);
            else Debug.Log($"RR_MOOD {world.BiomeName} is loaded, so nothing rebuilt. " +
                           "The mood applies next time Manhattan is built.");
        }
    }
}
