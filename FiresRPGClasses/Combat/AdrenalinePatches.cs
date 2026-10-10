using HarmonyLib;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Doc 16: a slotted adrenaline ultimate lets the bar build without a trinket, and a trinket's own threshold
    /// still fires. Raising the maximum is all that is needed - vanilla holds the bar at full when nothing pops it.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.GetMaxAdrenaline))]
    internal static class PlayerGetMaxAdrenalineFloor
    {
        [HarmonyPostfix]
        private static void RaiseFloorForSlottedUltimate(Player __instance, ref float __result)
        {
            if (__instance != Player.m_localPlayer) return;
            if (__result >= UltimateController.MaxAdrenalineWithoutTrinket) return;
            if (!UltimateController.WantsAdrenalineFloor()) return;
            __result = UltimateController.MaxAdrenalineWithoutTrinket;
        }
    }
}
