using RoadRage.UnityRemake;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Forces the mobile detail budget on desktop, so RR_COST can say how much geometry
    /// the budget actually removes without producing an Android build to find out.
    ///
    /// The override already existed - ForceLowDetailBudget, and -lowdetail on the command
    /// line - but the only way to reach it from the editor was to call the static by
    /// hand. That is how the two identical GREENWOOD readings happened: the budget looked
    /// set and the world had not rebuilt under it, so both runs measured the rich path.
    ///
    /// So this does both halves. Toggling rebuilds the current biome immediately, because
    /// chunks bake their scatter density at build time and a budget change is invisible to
    /// RR_COST until the world is built again.
    ///
    /// It does NOT emulate a device GPU. A desktop frame rate under this budget says
    /// nothing about a handset; what it measures is how much the thinning takes out.
    public static class DetailBudgetMenu
    {
        private const string ForceLowItem = "Road Rage/Force Low Detail Budget";

        [MenuItem(ForceLowItem, true)]
        private static bool ValidateForceLow()
        {
            Menu.SetChecked(ForceLowItem, RoadRageBootstrap.LowDetailBudgetForced);
            return true;
        }

        [MenuItem(ForceLowItem)]
        private static void ToggleForceLow()
        {
            var low = !RoadRageBootstrap.LowDetailBudgetForced;
            // Clearing rather than forcing rich on the way back out: forcing rich would
            // pin desktop to a path it already takes, and leave a pref behind that
            // outlives the editor session and silently takes every later reading off the
            // shipping behaviour.
            if (low) RoadRageBootstrap.ForceLowDetailBudget(true);
            else RoadRageBootstrap.ClearDetailBudgetOverride();
            Menu.SetChecked(ForceLowItem, low);

            var world = RoadRageBootstrap.Instance;
            if (world == null)
            {
                Debug.Log($"RR_QUALITY budget set before play. RichDetailBudget=" +
                          $"{RoadRageBootstrap.RichDetailBudget}; the pref survives entering " +
                          "play mode, so the world builds under it.");
                return;
            }
            world.ReloadBiome(world.BiomeName);
        }
    }
}
