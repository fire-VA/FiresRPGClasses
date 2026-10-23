using HarmonyLib;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Placed skills queue, then LMB places them: while a placement marker is up, LMB places the skill and RMB cancels
    /// it, so the same clicks must not also swing the weapon or raise the shield. Movement, jump and run pass untouched. The
    /// local player only; one patch on Player.SetControls (RPGClasses had none).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class PlacementInput
    {
        [HarmonyPrefix]
        private static void HoldBackClicks(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
            ref bool secondaryAttackHold, ref bool block, ref bool blockHold)
        {
            if (__instance != Player.m_localPlayer || !TargetingController.IsAiming) return;
            attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
        }
    }
}
