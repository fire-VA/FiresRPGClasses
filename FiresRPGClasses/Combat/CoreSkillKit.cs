using System.Collections.Generic;
using FiresCore.Bridge;
using FiresCore.Npc;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The kit for the tree nodes Core already has as companion abilities. Each casts what the companion's
    /// ArchetypeAbilitySystem.Use* method casts (one AbilityRPCManager call with the same effect and duration), so the
    /// player's skill and the companion's are one effect. A party skill reaches the tree's radius (the player design) when
    /// the node has one, else the companion's. Core decides who a cast may touch (ClassTargeting, Core 0.2.138): heals and
    /// buffs only the party (group or guild), debuffs only enemies, a player only when both have PvP on.
    /// Fire's picks (popup 2026-09-28 18:0x):
    /// - the five placed skills (Consecration, Rain of Arrows, Meteor, Sanctuary, Purifying Circle) go off at your feet for
    ///   now, until Core has a point-centred apply;
    /// - Resurrection revives the party's defeated companions within reach, each on its owner's peer;
    /// - Poison is a poisoned blade: your next hit within 10 s poisons what it hits.
    /// </summary>
    public static class CoreSkillKit
    {
        private enum Reach
        {
            /// <summary>On the caster (a buff whose effect may reach around them, e.g. Chi Explosion, Divine Hymn).</summary>
            Self,

            /// <summary>On the targeted party member, or the caster when nobody is targeted.</summary>
            Ally,

            /// <summary>On the targeted enemy; no target, no cast.</summary>
            Enemy,

            /// <summary>On every party member in range, the caster included.</summary>
            Party,
        }

        private struct CoreCast
        {
            public Reach Reach;
            public string Effect;
            public float Duration;
            public float Range;
        }

        // Tank Taunt: TauntManager's taunt, not a status effect cast. Duration and range from Core's ability text.
        private const string TauntNodeId = "tank_taunt";
        private const float TauntSeconds = 30f;
        private const float TauntRange = 5f;

        // Healer Resurrection: the companion healer's cast (a 3 s Resurrection effect on the caster, then
        // CompanionRespawnManager.RequestImmediateRespawn), for every defeated party companion in reach.
        private const string ResurrectionNodeId = "healer_resurrection";
        private const float ResurrectionSeconds = 3f;
        private const float ResurrectionRange = 10f;

        // Rogue Poison: the companion rogue's on-hit poison (8 s), armed for the caster's next hit.
        private const string PoisonNodeId = "rogue_poison";
        private const float PoisonArmedSeconds = 10f;
        private const float PoisonSeconds = 8f;

        private static float _poisonArmedUntil = -1f;

        // Durations and ranges: ArchetypeAbilitySystem's Use* methods and constants (Core 0.2.138).
        private static readonly Dictionary<string, CoreCast> Casts = new Dictionary<string, CoreCast>
        {
            { "tank_fortify",          Cast(Reach.Self,  StatusEffectManager.EFFECT_FORTIFY, 10f) },
            { "tank_immortal_stance",  Cast(Reach.Self,  StatusEffectManager.EFFECT_IMMORTAL_STANCE, 5f) },

            { "paladin_smite",         Cast(Reach.Self,  StatusEffectManager.EFFECT_HOLY_SMITE, 15f) },
            { "paladin_protection",    Cast(Reach.Party, StatusEffectManager.EFFECT_DIVINE_PROTECTION, 12f, 12f) },
            { "paladin_consecration",  Cast(Reach.Self,  StatusEffectManager.EFFECT_CONSECRATION, 10f) },
            { "paladin_divine_shield", Cast(Reach.Self,  StatusEffectManager.EFFECT_DIVINE_SHIELD, 3f) },
            { "paladin_lay_hands",     Cast(Reach.Ally,  StatusEffectManager.EFFECT_LAY_ON_HANDS, 1f) },
            { "paladin_avatar",        Cast(Reach.Self,  StatusEffectManager.EFFECT_AVATAR_OF_LIGHT, 30f) },

            { "berserker_warcry",      Cast(Reach.Party, StatusEffectManager.EFFECT_WARCRY, 15f, 15f) },
            { "berserker_avatar_war",  Cast(Reach.Self,  StatusEffectManager.EFFECT_AVATAR_OF_WAR, 20f) },

            { "rogue_stealth",         Cast(Reach.Self,  StatusEffectManager.EFFECT_STEALTH, 12f) },
            { "rogue_evasion",         Cast(Reach.Self,  StatusEffectManager.EFFECT_EVASION, 8f) },
            { "rogue_death_mark",      Cast(Reach.Enemy, StatusEffectManager.EFFECT_DEATH_MARK, 30f) },
            { "rogue_shadow_dance",    Cast(Reach.Self,  StatusEffectManager.EFFECT_SHADOW_DANCE, 10f) },

            { "monk_chi_strike",       Cast(Reach.Self,  StatusEffectManager.EFFECT_CHI_STRIKE, 15f) },
            { "monk_inner_peace",      Cast(Reach.Self,  StatusEffectManager.EFFECT_INNER_PEACE, 8f) },
            { "monk_flurry",           Cast(Reach.Self,  StatusEffectManager.EFFECT_FLURRY_OF_BLOWS, 5f) },
            { "monk_iron_body",        Cast(Reach.Self,  StatusEffectManager.EFFECT_IRON_BODY, 10f) },
            { "monk_chi_explosion",    Cast(Reach.Self,  StatusEffectManager.EFFECT_CHI_EXPLOSION, 1f) },
            { "monk_perfection",       Cast(Reach.Self,  StatusEffectManager.EFFECT_WAY_OF_PERFECTION, 15f) },

            { "ranger_mark",           Cast(Reach.Enemy, StatusEffectManager.EFFECT_HUNTERS_MARK, 20f) },
            { "ranger_eagle_eye",      Cast(Reach.Self,  StatusEffectManager.EFFECT_EAGLE_EYE, 15f) },
            { "ranger_multishot",      Cast(Reach.Self,  StatusEffectManager.EFFECT_MULTISHOT, 15f) },
            { "ranger_perfect_shot",   Cast(Reach.Self,  StatusEffectManager.EFFECT_PERFECT_SHOT, 15f) },
            { "ranger_rain_arrows",    Cast(Reach.Self,  StatusEffectManager.EFFECT_RAIN_OF_ARROWS, 5f) },

            { "mage_infusion",         Cast(Reach.Self,  StatusEffectManager.EFFECT_ELEMENTAL_INFUSION, 20f) },
            { "mage_shield",           Cast(Reach.Self,  StatusEffectManager.EFFECT_ARCANE_SHIELD, 15f) },
            { "mage_overcharge",       Cast(Reach.Self,  StatusEffectManager.EFFECT_OVERCHARGE, 20f) },
            { "mage_arcane_form",      Cast(Reach.Self,  StatusEffectManager.EFFECT_ARCANE_FORM, 15f) },
            { "mage_meteor",           Cast(Reach.Self,  StatusEffectManager.EFFECT_METEOR, 3f) },

            { "healer_purify",         Cast(Reach.Ally,  StatusEffectManager.EFFECT_PURIFY, 10f) },
            { "healer_sanctuary",      Cast(Reach.Party, StatusEffectManager.EFFECT_SANCTUARY, 10f, 10f) },
            { "healer_purify_circle",  Cast(Reach.Party, StatusEffectManager.EFFECT_PURIFYING_CIRCLE, 12f, 10f) },
            { "healer_divine_hymn",    Cast(Reach.Self,  StatusEffectManager.EFFECT_DIVINE_HYMN, 8f) },
            { "healer_avatar_life",    Cast(Reach.Self,  StatusEffectManager.EFFECT_AVATAR_OF_LIFE, 20f) },
        };

        private static bool _registered;

        /// <summary>Gives every node above its handler (ClassSkillKit). Safe to call more than once.</summary>
        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            foreach (KeyValuePair<string, CoreCast> entry in Casts)
            {
                CoreCast cast = entry.Value;
                ClassSkillKit.Register(entry.Key, (node, caster, target) => Run(cast, node, caster, target));
            }
            ClassSkillKit.Register(TauntNodeId, Taunt);
            ClassSkillKit.Register(ResurrectionNodeId, Resurrect);
            ClassSkillKit.Register(PoisonNodeId, ArmPoison);
        }

        /// <summary>
        /// Another player's cast, arriving on this peer (ClassCastNetwork). Only what must run where a thing is owned runs
        /// here: a party member's Resurrection revives this player's own defeated companions in reach.
        /// </summary>
        public static void OnRemoteCast(ClassTreeNode node, Character caster, Vector3 point)
        {
            if (node == null || caster == null || node.Id != ResurrectionNodeId) return;
            Player local = Player.m_localPlayer;
            if (local == null || !ClassTargeting.IsPartyMember(caster, local)) return;
            ReviveOwnCompanions(local.GetPlayerID(), caster.transform.position, Range(node, ResurrectionRange));
        }

        /// <summary>The local player's hit landing (a Character.Damage postfix): an armed poisoned blade poisons it.</summary>
        internal static void OnLocalHit(Character hit, HitData data)
        {
            if (_poisonArmedUntil < 0f || hit == null || data == null) return;
            if (Time.time > _poisonArmedUntil) { _poisonArmedUntil = -1f; return; }
            Player local = Player.m_localPlayer;
            if (local == null || hit == local || data.m_attacker != local.GetZDOID()) return;
            _poisonArmedUntil = -1f;
            AbilityRPCManager.ApplySingleEffect(local, hit, StatusEffectManager.EFFECT_POISON, PoisonSeconds);
        }

        private static bool Run(CoreCast cast, ClassTreeNode node, Character caster, CastTarget target)
        {
            switch (cast.Reach)
            {
                case Reach.Self:
                    // R75: a placed skill cast as a self effect (Rain of Arrows, Meteor) never handed its aim to Core, which then
                    // landed it 8 m in front of the caster. Core 0.2.200 takes the point for the next placed effect.
                    if (node.Targeting.Mode == TargetingMode.Placed && target.Resolved) CorePlacement.Hand(target.Point);
                    return AbilityRPCManager.ApplySelfBuff(caster, cast.Effect, cast.Duration);
                case Reach.Ally:
                    return AbilityRPCManager.ApplySingleEffect(caster, target.TargetCharacter ?? caster, cast.Effect, cast.Duration);
                case Reach.Enemy:
                    return target.TargetCharacter != null
                        && AbilityRPCManager.ApplySingleEffect(caster, target.TargetCharacter, cast.Effect, cast.Duration);
                default:
                    float range = Radius(node, cast.Range);
                    return AbilityRPCManager.ApplyGroupBuff(caster, cast.Effect, range, cast.Duration) > 0;
            }
        }

        // The companion's taunt without its companion-only shockwave emote: every enemy creature in range turns on you, and
        // you get the Taunting buff that keeps catching new ones. Players are never taunted (they have no AI to force).
        private static bool Taunt(ClassTreeNode node, Character caster, CastTarget target)
        {
            float range = Radius(node, TauntRange);
            TauntManager.ApplyTaunt(caster, range, TauntSeconds);
            return true;
        }

        // Your own defeated companions revive here; a party member's revive on their owner's peer (OnRemoteCast). The cast
        // is refused when nobody in reach is down.
        private static bool Resurrect(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            float range = Range(node, ResurrectionRange);
            bool anyDown = false;
            foreach (CompanionController companion in CompanionController.AllCompanions)
            {
                if (!IsDownInReach(companion, caster.transform.position, range)) continue;
                if (companion.ownerPlayerId == player.GetPlayerID() || NpcCompanionBridge.AreOwnersAllied(player.GetPlayerID(), companion.ownerPlayerId))
                    anyDown = true;
            }
            if (!anyDown) return false;

            AbilityRPCManager.ApplySelfBuff(caster, StatusEffectManager.EFFECT_RESURRECTION, ResurrectionSeconds);
            ReviveOwnCompanions(player.GetPlayerID(), caster.transform.position, range);
            return true;
        }

        private static void ReviveOwnCompanions(long owner, Vector3 from, float range)
        {
            var down = new List<CompanionController>();
            foreach (CompanionController companion in CompanionController.AllCompanions)
                if (companion.ownerPlayerId == owner && IsDownInReach(companion, from, range)) down.Add(companion);
            foreach (CompanionController companion in down)
                CompanionRespawnManager.RequestImmediateRespawn(companion.companionId, from);
        }

        private static bool IsDownInReach(CompanionController companion, Vector3 from, float range) =>
            companion != null && companion.isDefeated && Vector3.Distance(companion.transform.position, from) <= range;

        // The next hit within 10 s poisons what it hits (OnLocalHit); Core's rule keeps it to enemies.
        private static bool ArmPoison(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (caster != Player.m_localPlayer) return false;
            _poisonArmedUntil = Time.time + PoisonArmedSeconds;
            caster.Message(MessageHud.MessageType.TopLeft, $"{node.DisplayName}: your next hit within {PoisonArmedSeconds:0} s poisons");
            return true;
        }

        // The tree's numbers (the player design) where the node has them, else the companion's.
        private static float Range(ClassTreeNode node, float fallback) =>
            node.Targeting != null && node.Targeting.Range > 0f ? node.Targeting.Range : fallback;

        private static float Radius(ClassTreeNode node, float fallback) =>
            node.Targeting != null && node.Targeting.Radius > 0f ? node.Targeting.Radius : fallback;

        private static CoreCast Cast(Reach reach, string effect, float duration, float range = 0f) =>
            new CoreCast { Reach = reach, Effect = effect, Duration = duration, Range = range };
    }
}
