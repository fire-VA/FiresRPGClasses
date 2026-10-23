using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// What the Paragon stars DO (Tools\RPG_PARAGON_TALENT_PLAN.md §8). Every star whose effect is a plain stat is live
    /// through one status effect on the local player, RPG_Paragon; the others say "(effect coming soon)" in their tooltip and are listed
    /// once in the log. Active = a passive with a rank, or a slottable with a rank that is slotted (4 per tree).
    /// A star's value is ParagonStarDef.ValueAt(rank) (its own Soft / Asymptote curve), in percent. The same stat from several stars is
    /// summed, then the stacking guard (plan §8): damage-taken reductions through one Asymptote, knee 30% / max 60%.
    /// The star -> stat map is matched on the yaml's effectFormat text, so a renamed star keeps working and a new one with a known
    /// text is live at once.
    /// </summary>
    public static class ParagonEffects
    {
        public enum Stat
        {
            StaminaRegen, HealthRegen, EitrRegen, RunSpeed, Carry, Armour, PhysicalTaken, ElementalTaken, RunJumpStamina, DodgeStamina,
            BlockStamina, SkillXp, PhysicalDamage, MagicDamage, SwimSpeed, Jump, MaxCarryFlat,
        }

        // effectFormat text -> stat(s). Texts are the yaml's, exactly.
        private static readonly (string format, Stat[] stats)[] Map =
        {
            ("+{value}% stamina regen", new[] { Stat.StaminaRegen }),
            ("+{value}% health regen", new[] { Stat.HealthRegen }),
            ("+{value}% eitr regen", new[] { Stat.EitrRegen }),
            ("+{value}% run speed", new[] { Stat.RunSpeed }),
            ("+{value} carry weight", new[] { Stat.Carry }),
            ("+{value}% armour", new[] { Stat.Armour }),
            ("-{value}% physical damage taken", new[] { Stat.PhysicalTaken }),
            ("-{value}% fire, frost, poison and lightning damage taken", new[] { Stat.ElementalTaken }),
            ("-{value}% run and jump stamina", new[] { Stat.RunJumpStamina }),
            ("-{value}% dodge stamina", new[] { Stat.DodgeStamina }),
            ("-{value}% block stamina", new[] { Stat.BlockStamina }),
            ("+{value}% skill XP", new[] { Stat.SkillXp }),
            ("+{value}% physical weapon damage", new[] { Stat.PhysicalDamage }),
            ("+{value}% elemental and blood magic damage", new[] { Stat.MagicDamage }),
            ("+{value}% swim speed and -{value}% swim stamina", new[] { Stat.SwimSpeed }),
            ("+{value}% jump force, five times as much less fall damage", new[] { Stat.Jump }),
        };

        internal const string SeName = "RPG_Paragon";
        internal static readonly int SeHash = SeName.GetStableHashCode();
        private static readonly Dictionary<Stat, float> _totals = new Dictionary<Stat, float>();
        // Effects outside the Stat enum, keyed by the yaml's effectFormat text.
        // Each owner's file (Paragon\Effects\*.cs) declares its texts with [ParagonEffectSet] and reads its total with TX(format).
        private static readonly Dictionary<string, float> _xtotals = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>The summed FRACTION (5% = 0.05; a flat amount = value / 100) of every bought star whose effect text is <paramref name="format"/>.</summary>
        public static float TX(string format) => format != null && _xtotals.TryGetValue(format, out float v) ? v : 0f;
        private static bool _loggedDormant;
        private static SE_Paragon _template;

        public static IReadOnlyDictionary<Stat, float> Totals => _totals;

        /// <summary>True when this star's effect is implemented (its text maps to a stat).</summary>
        public static bool IsLive(string starId) { var s = ParagonCatalog.Star(starId); return StatsOf(s) != null || (s != null && ParagonEffectSets.Has(s.EffectFormat)); }

        private static Stat[] StatsOf(ParagonStarDef s)
        {
            if (s == null) return null;
            foreach (var (format, stats) in Map) if (string.Equals(s.EffectFormat, format, StringComparison.Ordinal)) return stats;
            return null;
        }

        /// <summary>Recomputes the totals from the local record and (re)applies RPG_Paragon. Cheap; called after every Paragon action.</summary>
        public static void Refresh()
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            _totals.Clear();
            _xtotals.Clear();
            _active.Clear();
            var slotted = new HashSet<string>();
            foreach (ParagonTreeDef t in ParagonCatalog.Trees) foreach (string id in ParagonUi.Slotted(t.Id)) if (id != null) slotted.Add(id);
            int live = 0, dormant = 0;
            var dormantNames = new List<string>();
            foreach (ParagonStarDef s in ParagonCatalog.Stars)
            {
                int rank = ParagonUi.Rank(s.Id);
                if (rank <= 0) continue;
                if (s.Kind == ParagonStarKind.Slottable && !slotted.Contains(s.Id)) continue;
                Stat[] stats = StatsOf(s);
                float v = s.ValueAt(rank) / 100f;
                _active.Add((s, v));   // every counted star (bought; slottables only while slotted), its own fraction
                if (stats == null)
                {
                    if (ParagonEffectSets.Has(s.EffectFormat)) { _xtotals[s.EffectFormat] = TX(s.EffectFormat) + v; live++; continue; }
                    dormant++; dormantNames.Add(s.Id); continue;
                }
                foreach (Stat st in stats) _totals[st] = (_totals.TryGetValue(st, out float x) ? x : 0f) + v;
                live++;
            }
            // Stacking guard: damage taken never reaches immunity (knee 30%, max 60%).
            foreach (Stat st in new[] { Stat.PhysicalTaken, Stat.ElementalTaken })
                if (_totals.TryGetValue(st, out float raw)) _totals[st] = FiresCore.Curves.SoftCap.Asymptote(raw, 0.30f, 0.60f);
            Apply(me);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] effects: {live} live star(s), {dormant} with effects coming soon; "
                + string.Join(", ", _totals.Select(kv => $"{kv.Key} {kv.Value * 100f:0.#}%")));
            if (dormant > 0 && !_loggedDormant) { _loggedDormant = true; FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] effects not built yet for: " + string.Join(", ", dormantNames)); }
        }

        private static void Apply(Player me)
        {
            SEMan seman = me.GetSEMan();
            if (seman == null) return;
            bool any = _totals.Values.Any(v => Mathf.Abs(v) > 0.00001f) || _xtotals.Values.Any(v => Mathf.Abs(v) > 0.00001f);
            bool has = seman.HaveStatusEffect(SeHash);
            if (!any) { if (has) seman.RemoveStatusEffect(SeHash, true); return; }
            if (has) return;   // SE_Paragon reads the live totals every call
            if (_template == null)
            {
                _template = ScriptableObject.CreateInstance<SE_Paragon>();
                _template.name = SeName;
                _template.m_name = "Paragon";
                _template.m_tooltip = "Your Paragon stars.";
                _template.m_icon = UI.ClassIcons.ForEffect(SeName);   // Icons\Effects\se_RPG_Paragon.png
                _template.m_ttl = 0f;
            }
            seman.AddStatusEffect(_template, true);
        }

        // For ClassStatsApi (one row per star): the stars Refresh counted, each with its own FRACTION (ValueAt(rank) / 100).
        private static readonly List<(ParagonStarDef star, float fraction)> _active = new List<(ParagonStarDef star, float fraction)>();
        internal static IReadOnlyList<(ParagonStarDef star, float fraction)> ActiveStars => _active;
        /// <summary>The Stat-enum stats this star's text maps to (Map), or null.</summary>
        internal static Stat[] StatsOfStar(ParagonStarDef s) => StatsOf(s);

        internal static float T(Stat s) => _totals.TryGetValue(s, out float v) ? v : 0f;
    }

    /// <summary>The live Paragon stats; reads ParagonEffects' totals on every call (no copy to go stale).</summary>
    public class SE_Paragon : SE_Stats
    {
        private static float T(ParagonEffects.Stat s) => ParagonEffects.T(s);

        public override void UpdateStatusEffect(float dt) { base.UpdateStatusEffect(dt); m_time = 0f; }
        public override void ModifyStaminaRegen(ref float r) { base.ModifyStaminaRegen(ref r); r *= 1f + T(ParagonEffects.Stat.StaminaRegen); }
        public override void ModifyHealthRegen(ref float r) { base.ModifyHealthRegen(ref r); r *= 1f + T(ParagonEffects.Stat.HealthRegen); }
        public override void ModifyEitrRegen(ref float r) { base.ModifyEitrRegen(ref r); r *= 1f + T(ParagonEffects.Stat.EitrRegen); }
        public override void ModifyMaxCarryWeight(float b, ref float l) { base.ModifyMaxCarryWeight(b, ref l); l += T(ParagonEffects.Stat.Carry) * 100f; }
        public override void ModifyArmorMods(ref float a) { base.ModifyArmorMods(ref a); a *= 1f + T(ParagonEffects.Stat.Armour); }
        public override void ModifyRunStaminaDrain(float b, ref float d, Vector3 dir) { base.ModifyRunStaminaDrain(b, ref d, dir); d *= 1f - Mathf.Clamp01(T(ParagonEffects.Stat.RunJumpStamina)); }
        public override void ModifyJumpStaminaUsage(float b, ref float u) { base.ModifyJumpStaminaUsage(b, ref u); u *= 1f - Mathf.Clamp01(T(ParagonEffects.Stat.RunJumpStamina)); }
        public override void ModifyDodgeStaminaUsage(float b, ref float u) { base.ModifyDodgeStaminaUsage(b, ref u); u *= 1f - Mathf.Clamp01(T(ParagonEffects.Stat.DodgeStamina)); }
        public override void ModifyBlockStaminaUsage(float b, ref float u) { base.ModifyBlockStaminaUsage(b, ref u); u *= 1f - Mathf.Clamp01(T(ParagonEffects.Stat.BlockStamina)); }
        public override void ModifySwimStaminaUsage(float b, ref float u) { base.ModifySwimStaminaUsage(b, ref u); u *= 1f - Mathf.Clamp01(T(ParagonEffects.Stat.SwimSpeed)); }
        public override void ModifyRaiseSkill(Skills.SkillType s, ref float v) { base.ModifyRaiseSkill(s, ref v); v *= 1f + T(ParagonEffects.Stat.SkillXp); }
        public override void ModifyJump(Vector3 b, ref Vector3 j) { base.ModifyJump(b, ref j); j.y *= 1f + T(ParagonEffects.Stat.Jump); }

        public override void ModifySpeed(float b, ref float speed, Character c, Vector3 dir)
        {
            base.ModifySpeed(b, ref speed, c, dir);
            speed *= 1f + (c != null && c.IsSwimming() ? T(ParagonEffects.Stat.SwimSpeed) : T(ParagonEffects.Stat.RunSpeed));
        }

        public override void ModifyFallDamage(float b, ref float d) { base.ModifyFallDamage(b, ref d); d *= 1f - Mathf.Clamp01(T(ParagonEffects.Stat.Jump) * 5f); }

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hit)
        {
            base.ModifyAttack(skill, ref hit);
            float phys = 1f + T(ParagonEffects.Stat.PhysicalDamage), magic = 1f + T(ParagonEffects.Stat.MagicDamage);
            hit.m_damage.m_blunt *= phys; hit.m_damage.m_slash *= phys; hit.m_damage.m_pierce *= phys;
            hit.m_damage.m_fire *= magic; hit.m_damage.m_frost *= magic; hit.m_damage.m_lightning *= magic; hit.m_damage.m_poison *= magic; hit.m_damage.m_spirit *= magic;
        }

        public override void OnDamaged(HitData hit, Character attacker)
        {
            base.OnDamaged(hit, attacker);
            if (hit == null) return;
            float phys = 1f - T(ParagonEffects.Stat.PhysicalTaken), elem = 1f - T(ParagonEffects.Stat.ElementalTaken);
            hit.m_damage.m_blunt *= phys; hit.m_damage.m_slash *= phys; hit.m_damage.m_pierce *= phys;
            hit.m_damage.m_fire *= elem; hit.m_damage.m_frost *= elem; hit.m_damage.m_lightning *= elem; hit.m_damage.m_poison *= elem;
        }
    }

    // On spawn (and after a world load) the totals come back from the saved record.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class ParagonEffectsSpawn
    {
        [HarmonyPostfix]
        private static void After(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                try { ParagonEffects.Refresh(); } catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] effects on spawn: " + e.Message); }
        }
    }
}
