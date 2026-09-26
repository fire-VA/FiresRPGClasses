using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Doc 14: an archetype's level-20 evasion replaces the dodge roll while that archetype is on the active bar.
    /// Evasions are never slotted. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class EvasionController
    {
        public static ClassTreeNode ActiveEvasion()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return null;
            ClassTreeNode node = ClassTrees.EvasionFor(data.Active.Archetype);
            if (node == null) return null;
            if (data.Progress.Level < node.MinLevel) return null;
            if (data.Progress.TalentRank(node.Archetype, node.Id) <= 0) return null;
            if (!BarController.IsUsable(data.Active)) return null;
            return node;
        }

        /// <summary>True when the evasion took the dodge, so the vanilla roll must not also run.</summary>
        public static bool TryTakeDodge(Player player, Vector3 direction)
        {
            ClassTreeNode node = ActiveEvasion();
            if (node == null || player == null) return false;
            if (!CooldownTracker.IsReady(node.Id)) return false;

            Vector3 point = player.transform.position + direction.normalized * node.Targeting.Range;
            ClassCastExecutor.PlayLocal(node, player, CastTarget.At(point));
            CooldownTracker.Start(node.Id, node.Cooldown);
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "Dodge")]
    internal static class PlayerDodgeEvasionPatch
    {
        [HarmonyPrefix]
        private static bool EvasionInsteadOfRoll(Player __instance, Vector3 dodgeDir)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !EvasionController.TryTakeDodge(__instance, dodgeDir);
        }
    }
}
