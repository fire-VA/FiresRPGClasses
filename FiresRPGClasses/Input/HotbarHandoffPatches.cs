using HarmonyLib;

namespace FiresRPGClasses.Input
{
    /// <summary>
    /// Core declares the hand-off from Valheim's Hotbar 1-8; enforcing it is this mod's job. One prefix on
    /// UseHotbarItem is the whole enforcement. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    internal static class PlayerUseHotbarItemHandoff
    {
        private static bool _routedByUs;

        /// <summary>Lets our own item-hotbar keys through the suppression they would otherwise trip.</summary>
        internal static void UseThroughHandoff(Player player, int index)
        {
            if (player == null) return;
            _routedByUs = true;
            try { player.UseHotbarItem(index); }
            finally { _routedByUs = false; }
        }

        [HarmonyPrefix]
        private static bool SuppressWhileSlotKeysOwnTheRow(Player __instance, int index)
        {
            if (_routedByUs) return true;
            if (__instance != Player.m_localPlayer) return true;
            return !ClassKeys.SuppressVanillaHotbar(index);
        }
    }
}
