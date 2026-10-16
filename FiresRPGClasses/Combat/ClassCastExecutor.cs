using System;
using System.Collections.Generic;
using FiresRPGClasses.Data;
using FiresCore.Npc.Archetypes;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Turns a resolved cast into an effect. Nodes that name a Core status effect go through AbilityRPCManager;
    /// anything else raises Unmapped so the kit work can hook it, and says so once.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class ClassCastExecutor
    {
        private const float CastRingSeconds = 3f;
        private const float DefaultEffectDuration = 10f;

        private static readonly HashSet<string> WarnedNodes = new HashSet<string>();

        /// <summary>Raised for a node with no Core effect name, so the class-kit work can supply the behaviour.</summary>
        public static event Action<ClassTreeNode, Character, Vector3, ZDOID> Unmapped;

        /// <summary>The last <see cref="PlayLocal"/> ran a handler that refused (no target, refused): the cast didn't happen.</summary>
        internal static bool LastHandlerRefused { get; private set; }

        public static void PlayLocal(ClassTreeNode node, Character caster, CastTarget target)
        {
            LastHandlerRefused = false;
            if (node == null || caster == null) return;
            // A cast (a heal or a buff too) is taking part in the fight, for class XP's kill share.
            ClassXp.MarkActive();
            TargetingController.ShowCastRing(node, target.Point, CastRingSeconds);
            ClassCastNetwork.Broadcast(caster, node.Id, target.Point, target.TargetZdo);
            if (ClassSkillKit.TryRun(node, caster, target, out bool succeeded))
            {
                LastHandlerRefused = !succeeded;
                return;
            }
            Apply(node, caster, target.TargetCharacter, target.Point, target.TargetZdo);
        }

        /// <summary>True when <see cref="Apply"/> would hand this node to Core rather than to the unmapped hook.</summary>
        public static bool AppliesCoreEffect(ClassTreeNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.EffectName)) return false;
            switch (node.Targeting.Mode)
            {
                case TargetingMode.Self:
                case TargetingMode.NextAttack:
                case TargetingMode.Ally:
                case TargetingMode.AroundYou:
                case TargetingMode.Melee:
                case TargetingMode.Aimed:
                case TargetingMode.Dash:
                    return true;
                default:
                    return false;
            }
        }

        public static void PlayRemote(ClassTreeNode node, Character caster, Vector3 point, ZDOID targetZdo)
        {
            if (node == null) return;
            TargetingController.ShowCastRing(node, point, CastRingSeconds);
            CoreSkillKit.OnRemoteCast(node, caster, point);
            WerewolfKit.OnRemoteCast(node, caster);   // Howl: the caster's howl is heard here too
        }

        private static void Apply(ClassTreeNode node, Character caster, Character target, Vector3 point, ZDOID targetZdo)
        {
            if (string.IsNullOrEmpty(node.EffectName))
            {
                if (Unmapped == null && WarnedNodes.Add(node.Id))
                    Debug.LogWarning("[FiresRPGClasses] " + node.DisplayName + " (" + node.Id
                        + ") has no Core effect name and nothing is listening for unmapped casts.");
                Unmapped?.Invoke(node, caster, point, targetZdo);
                return;
            }

            float duration = node.EffectDuration > 0f ? node.EffectDuration : DefaultEffectDuration;
            switch (node.Targeting.Mode)
            {
                case TargetingMode.Self:
                case TargetingMode.NextAttack:
                    AbilityRPCManager.ApplySelfBuff(caster, node.EffectName, duration);
                    break;
                case TargetingMode.Ally:
                    AbilityRPCManager.ApplySingleEffect(caster, target ?? caster, node.EffectName, duration);
                    break;
                case TargetingMode.AroundYou:
                    AbilityRPCManager.ApplyGroupBuff(caster, node.EffectName, node.Targeting.Radius, duration);
                    break;
                case TargetingMode.Melee:
                case TargetingMode.Aimed:
                case TargetingMode.Dash:
                    AbilityRPCManager.ApplyAoEEffect(caster, node.EffectName,
                        node.Targeting.Radius > 0f ? node.Targeting.Radius : 1f, duration);
                    break;
                case TargetingMode.Placed:
                    // Core's AoE centres on the caster; a placed effect needs a point-centred apply it does not have.
                    if (WarnedNodes.Add(node.Id))
                        Debug.LogWarning("[FiresRPGClasses] " + node.DisplayName
                            + " is placed, and Core has no point-centred AoE apply; handing it to the unmapped hook.");
                    Unmapped?.Invoke(node, caster, point, targetZdo);
                    break;
                default:
                    Unmapped?.Invoke(node, caster, point, targetZdo);
                    break;
            }
        }
    }
}
