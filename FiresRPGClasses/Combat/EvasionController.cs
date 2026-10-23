using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Combat.EmergencyEvasion;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Doc 14: an archetype's level-20 evasion replaces the dodge roll while that archetype is on the active bar.
    /// Evasions are never slotted. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// Each evasion is Core's EmergencyEvasionManager.TryPlayerEvasion: the archetype's evasion (Infernal Blink for the
    /// Mage + Berserker pair) in the dodge direction, for Core's stamina/eitr cost and cooldown.
    /// When it isn't ready or the player can't pay, the vanilla roll runs.
    /// </summary>
    public static class EvasionController
    {
        private static bool _registered;

        /// <summary>Gives every archetype's evasion node its handler (ClassSkillKit). Safe to call more than once.</summary>
        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            foreach (ArchetypeClass archetype in ClassTrees.Archetypes)
            {
                ClassTreeNode node = ClassTrees.EvasionFor(archetype);
                if (node != null) ClassSkillKit.Register(node.Id, Evade);
            }
        }

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
            // a race dodge (vampire mist-step, werewolf pounce) goes first when unlocked; else the class evasion
            if (ClassMovement.TryRaceDodge(player, direction)) return true;
            ClassTreeNode node = ActiveEvasion();
            if (node == null || player == null) return false;
            // Until the kit gives an evasion its behaviour, taking the roll left the player with no dodge at all.
            if (!ClassSkillKit.Implements(node.Id)) return false;

            Vector3 point = player.transform.position + direction.normalized * node.Targeting.Range;
            if (!ClassSkillKit.TryRun(node, player, CastTarget.At(point), out bool evaded) || !evaded) return false;
            CooldownTracker.Start(node.Id, EmergencyEvasionManager.PlayerCooldownRemaining(player));
            return true;
        }

        private static bool Evade(ClassTreeNode node, Character caster, CastTarget target)
        {
            Player player = caster as Player;
            if (player == null || player != Player.m_localPlayer) return false;

            ClassPlayerData data = ClassPlayerStore.Local;
            ArchetypeClass sub = data != null ? data.Inactive.Archetype : ArchetypeClass.None;
            EmergencyEvasionManager.EvasionType type = EmergencyEvasionManager.TypeFor(node.Archetype, sub);
            bool evaded = EmergencyEvasionManager.TryPlayerEvasion(player, type, target.Point - player.transform.position);

            EmergencyEvasionManager.CostOf(type, out float cost, out bool usesEitr, out float cooldown);
            // one line per roll press is a per-event diagnostic - Debug.
            FiresRPGClassesPlugin.Log?.LogDebug(evaded
                ? $"[Evasion] {node.Id} -> {type}: cast ({cost} {(usesEitr ? "eitr" : "stamina")}, cooldown {cooldown} s)"
                : $"[Evasion] {node.Id} -> {type}: refused (cooldown {EmergencyEvasionManager.PlayerCooldownRemaining(player):0.0} s, " +
                  $"stamina {player.GetStamina():0}, eitr {player.GetEitr():0}); vanilla roll");
            return evaded;
        }
    }

    [HarmonyPatch(typeof(Player), "Dodge")]
    internal static class PlayerDodgeEvasionPatch
    {
        [HarmonyPrefix]
        private static bool EvasionInsteadOfRoll(Player __instance, Vector3 dodgeDir)
        {
            if (__instance != Player.m_localPlayer) return true;
            if (EvasionController.TryTakeDodge(__instance, dodgeDir)) return false;
            PassiveKit.OnLocalRoll(__instance);
            return true;
        }
    }
}
