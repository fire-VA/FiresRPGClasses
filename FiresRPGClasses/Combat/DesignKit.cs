using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The design-only active skills whose tree text fully specifies them (Tools\CLASS_KIT_DESIGN_GAPS.md, "fully
    /// specified"). Every number is the node's yaml value at the caster's rank. Heals go through Core's AbilityHeals.Apply
    /// (it routes to the target's owner and carries the healer, so Healing Staff / Emergency Heal apply), and who a skill may
    /// touch is Core's ClassTargeting: heals only the party.
    /// </summary>
    public static class DesignKit
    {
        private const string MendingLightId = "paladin_mending_light";
        private const string RadiantAuraId = "paladin_radiant_aura";
        private const string RenewId = "healer_renew";
        private const string MiracleId = "healer_miracle";
        private const string EarthshakerId = "tank_earthshaker";
        private const string TailwindId = "monk_tailwind";
        private const string AegisId = "healer_aegis";

        // Earthshaker: vanilla's instant stagger (a hit with a multiplier of 100+), then Core's 30% slow for 4 s.
        private const float InstantStagger = 100f;
        private const float EarthshakerSlowSeconds = 4f;
        private const string EarthshakerFx = "vfx_sledge_hit";
        private static readonly string[] TailwindByRank =
        {
            StatusEffectManager.EFFECT_CLASS_HASTE15, StatusEffectManager.EFFECT_CLASS_HASTE20, StatusEffectManager.EFFECT_CLASS_HASTE25,
        };
        private const float AegisSeconds = 10f;

        private const float MiracleFloor = 0.5f;
        private const string AuraFx = "fx_shaman_protect";
        private const float AuraFxSeconds = 3f;
        private const float HotTickSeconds = 1f;

        private static readonly List<HealOverTime> Hots = new List<HealOverTime>();
        private static bool _registered;

        private sealed class HealOverTime
        {
            public Character Caster;
            public Character Target;
            public float Share;
            public float Until;
            public float NextTick;
        }

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(MendingLightId, MendingLight);
            ClassSkillKit.Register(RadiantAuraId, RadiantAura);
            ClassSkillKit.Register(RenewId, Renew);
            ClassSkillKit.Register(MiracleId, Miracle);
            ClassSkillKit.Register(EarthshakerId, Earthshaker);
            ClassSkillKit.Register(TailwindId, Tailwind);
            ClassSkillKit.Register(AegisId, Aegis);
        }

        /// <summary>Called every frame by ClassCombatDriver: the heal-over-time ticks.</summary>
        public static void Tick()
        {
            if (Hots.Count == 0) return;
            float now = Time.time;
            for (int i = Hots.Count - 1; i >= 0; i--)
            {
                HealOverTime hot = Hots[i];
                if (hot.Caster == null || hot.Target == null || hot.Target.IsDead() || now > hot.Until) { Hots.RemoveAt(i); continue; }
                if (now < hot.NextTick) continue;
                hot.NextTick = now + HotTickSeconds;
                AbilityHeals.Apply(hot.Caster, hot.Target, hot.Target.GetMaxHealth() * hot.Share);
            }
        }

        /// <summary>0.1.214: the rank in a given record (a registered class body's).</summary>
        internal static int RankOf(ClassTreeNode node, ClassPlayerData data) =>
            node == null || data == null ? 0 : Mathf.Max(1, data.Progress.TalentRank(node.Archetype, node.Id));

        internal static int RankOf(ClassTreeNode node)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return node == null || data == null ? 0 : Mathf.Max(1, data.Progress.TalentRank(node.Archetype, node.Id));
        }

        private static Character PartyTarget(Character caster, CastTarget target)
        {
            Character chosen = target.TargetCharacter != null ? target.TargetCharacter : caster;
            return ClassTargeting.IsPartyMember(caster, chosen) ? chosen : null;
        }

        // "Heal the ally under your crosshair within 25 m for {heal} of their max health" (the targeting picks the most
        // injured ally in range when nobody is under the crosshair).
        private static bool MendingLight(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character ally = PartyTarget(caster, target);
            if (ally == null || ally.IsDead()) return false;
            AbilityHeals.Apply(caster, ally, ally.GetMaxHealth() * node.Value("heal", RankOf(node)));
            return true;
        }

        // "An ally regenerates {hot} of max health per second for 8 s." A new cast on the same ally replaces the old one.
        private static bool Renew(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character ally = PartyTarget(caster, target);
            if (ally == null || ally.IsDead()) return false;
            Hots.RemoveAll(hot => hot.Target == ally && hot.Caster == caster);
            float duration = node.EffectDuration > 0f ? node.EffectDuration : 8f;
            Hots.Add(new HealOverTime
            {
                Caster = caster,
                Target = ally,
                Share = node.Value("hot", RankOf(node)),
                Until = Time.time + duration,
                NextTick = Time.time + HotTickSeconds,
            });
            return true;
        }

        // "Allies within 8 m regenerate {heal} of max health per second for 12 s": a helpful zone where it was cast.
        private static bool RadiantAura(ClassTreeNode node, Character caster, CastTarget target)
        {
            float share = node.Value("heal", RankOf(node));
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 8f;
            float duration = node.EffectDuration > 0f ? node.EffectDuration : 12f;
            ClassZone zone = ClassZones.ZoneAt(caster, caster.transform.position, radius, duration, ZoneKind.Helpful,
                ally => AbilityHeals.Apply(caster, ally, ally.GetMaxHealth() * share), 1f, AuraFx, AuraFxSeconds);
            if (zone != null) TargetingController.ShowCastRing(node, caster.transform.position, duration);
            return zone != null;
        }

        // "Slam the ground: {blunt} blunt damage to enemies within 5 m, stagger them all, 30% slow for 4 s." Each hit goes
        // through Character.Damage (routed to the victim's owner, where vanilla staggers a 100+ multiplier at once).
        private static bool Earthshaker(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 5f;
            float blunt = node.Value("blunt", RankOf(node));
            Vector3 center = caster.transform.position;
            AbilityRPCManager.SpawnFX(center, EarthshakerFx);
            foreach (Character enemy in Character.GetAllCharacters())
            {
                if (enemy == null || enemy.IsDead()) continue;
                if (Vector3.Distance(enemy.transform.position, center) > radius) continue;
                if (!ClassTargeting.IsEnemyTarget(caster, enemy)) continue;
                Vector3 away = enemy.transform.position - center;
                away = Ring.Flat(away, enemy.transform.position);   // off the halo ring: away.y = 0
                var hit = new HitData
                {
                    m_point = enemy.GetCenterPoint(),
                    m_dir = away.sqrMagnitude > 0.01f ? away.normalized : caster.transform.forward,
                    m_staggerMultiplier = InstantStagger,
                };
                hit.m_damage.m_blunt = blunt;
                hit.SetAttacker(caster);
                enemy.Damage(hit);
                AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_CLASS_SLOW30, EarthshakerSlowSeconds);
            }
            return true;
        }

        // "You and allies within 10 m move {speed} faster for 10 s": Core's haste preset for the rank, as a party buff.
        private static bool Tailwind(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = Mathf.Clamp(RankOf(node), 1, TailwindByRank.Length);
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 10f;
            float duration = node.EffectDuration > 0f ? node.EffectDuration : 10f;
            SkillFx.Play(TailwindId, caster.transform.position, SkillFx.SupportGlow, 0.1f);
            return AbilityRPCManager.ApplyGroupBuff(caster, TailwindByRank[rank - 1], radius, duration) > 0;
        }

        // "Allies within 10 m gain a shield that absorbs 25% of their max health for 10 s": Core's Aegis preset sizes it on
        // each carrier's owner.
        private static bool Aegis(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 10f;
            return AbilityRPCManager.ApplyGroupBuff(caster, StatusEffectManager.EFFECT_CLASS_AEGIS, radius, AegisSeconds) > 0;
        }

        // "Heal every ally within 15 m to at least 50% health." Refused when nobody in reach is below half.
        private static bool Miracle(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 15f;
            bool any = false;
            foreach (Character ally in Character.GetAllCharacters())
            {
                if (ally == null || ally.IsDead()) continue;
                if (Vector3.Distance(ally.transform.position, caster.transform.position) > radius) continue;
                if (!ClassTargeting.IsPartyMember(caster, ally)) continue;
                float missing = ally.GetMaxHealth() * MiracleFloor - ally.GetHealth();
                if (missing <= 0f) continue;
                AbilityHeals.Apply(caster, ally, missing);
                any = true;
            }
            return any;
        }
    }
}
