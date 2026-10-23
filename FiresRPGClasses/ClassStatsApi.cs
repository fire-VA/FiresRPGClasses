using System;
using System.Collections.Generic;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.ExtraSkills;
using FiresRPGClasses.Paragon;
using UnityEngine;

namespace FiresRPGClasses
{
    /// <summary>
    /// For the Player -> Stats page: add-only, read-only. Every class-side contribution applied to one stat
    /// for the local player, from the same calls the hooks use. No new math; nothing is applied.
    /// Never a lump "Class: Skills" row: ONE row PER SOURCE with a player-facing
    /// name: "&lt;Attribute&gt; (attribute)", "&lt;Skill&gt; (skill)", "&lt;Talent&gt; (passive/buff/form)", "Paragon: &lt;star&gt;".
    /// stat = an AttributeEffects.Stat name. Each entry: (label, flat, percent); flat = absolute amount added, percent = +8 means x1.08.
    /// </summary>
    public static class ClassStatsApi
    {
        private static readonly Dictionary<string, int> _lastCount = new Dictionary<string, int>(StringComparer.Ordinal);

        public static List<(string label, float flat, float percent)> Breakdown(Player p, string stat)
        {
            var list = new List<(string label, float flat, float percent)>();
            try
            {
                if (p == null || p != Player.m_localPlayer || !Enum.TryParse(stat, out AttributeEffects.Stat s)) return list;

                // 1. the attribute that drives this stat
                float attr = AttributeEffects.Current(s);
                if (attr != 0f && AttributeEffects.RowInfo(s, out var kind, out bool pct))
                    list.Add(($"{kind} (attribute)", pct ? 0f : attr, pct ? attr : 0f));

                switch (s)
                {
                    case AttributeEffects.Stat.MaxHealth:
                        Skill(list, ExtraSkillSystem.VigorId, ExtraSkillSystem.HealthPart(p));
                        break;
                    case AttributeEffects.Stat.MaxStamina:
                        Skill(list, ExtraSkillSystem.AgilityId, ExtraSkillSystem.StaminaPart(p));
                        break;
                    case AttributeEffects.Stat.MaxEitr:
                        Skill(list, ExtraSkillSystem.IntellectId, ExtraSkillSystem.EitrPart(p));
                        break;
                    case AttributeEffects.Stat.CarryWeight:
                        Skill(list, ExtraSkillSystem.HoarderId, ExtraSkillSystem.CarryPart(p));
                        Para(list, ParagonEffects.Stat.Carry, false);
                        break;
                    case AttributeEffects.Stat.Armour:
                        Factor(list, PassiveKit2.ThickHideId, "Thick Hide", "passive", PassiveKit2.ArmorFactor(p));
                        Para(list, ParagonEffects.Stat.Armour, true);
                        break;
                    case AttributeEffects.Stat.AttackSpeed:
                        Factor(list, BuffKit.BattleTranceId, "Battle Trance", "buff", BuffKit.AttackSpeedFactor(p));
                        Factor(list, ActiveKit.BloodbathNodeId, "Bloodbath", "buff", ActiveKit.AttackSpeedFactor(p));
                        Factor(list, PassiveKit2.ZealId, "Zeal", "passive", PassiveKit2.AttackSpeedFactor(p));
                        Factor(list, WerewolfKit.WolfFormId, "Wolf Form", "form", WerewolfKit.AttackSpeedFactor(p));
                        Factor(list, VampireKit.BloodFrenzyId, "Blood Frenzy", "buff", VampireKit.AttackSpeedFactor(p));
                        break;
                    case AttributeEffects.Stat.HealthRegen: Para(list, ParagonEffects.Stat.HealthRegen, true); break;
                    case AttributeEffects.Stat.StaminaRegen: Para(list, ParagonEffects.Stat.StaminaRegen, true); break;
                    case AttributeEffects.Stat.EitrRegen: Para(list, ParagonEffects.Stat.EitrRegen, true); break;
                    case AttributeEffects.Stat.PhysicalDamage: Para(list, ParagonEffects.Stat.PhysicalDamage, true); break;
                    case AttributeEffects.Stat.MagicDamage: Para(list, ParagonEffects.Stat.MagicDamage, true); break;
                    case AttributeEffects.Stat.ElementalTaken: Para(list, ParagonEffects.Stat.ElementalTaken, true, -1f); break;
                    case AttributeEffects.Stat.MoveStamina: Para(list, ParagonEffects.Stat.RunJumpStamina, true, -1f); break;
                    case AttributeEffects.Stat.AttackStamina: Para(list, ParagonEffects.Stat.BlockStamina, true, -1f); break;
                }

                // 4b. the [ParagonEffectSet] Fitness stars (pools, Beast Hide armour), per star, as ParagonFitness applies them
                FitnessRows(list, p, stat);

                if (!_lastCount.TryGetValue(stat, out int was) || was != list.Count)
                {
                    _lastCount[stat] = list.Count;
                    FiresRPGClassesPlugin.Log?.LogDebug($"[ClassStatsApi] {stat}: {list.Count} row(s)");
                }
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug($"[ClassStatsApi] {stat}: {e.GetType().Name}"); }
            return list;
        }

        private static void Skill(List<(string, float, float)> list, string codeId, float v)
        {
            if (ExtraSkillSystem.Ready && v != 0f) list.Add(($"{ExtraSkillNames.Get(codeId)} (skill)", v, 0f));
        }

        private static string TalentName(string nodeId, string fallback)
        {
            string n = ClassTrees.Node(nodeId)?.DisplayName;
            return string.IsNullOrEmpty(n) ? fallback : n;
        }

        private static void Factor(List<(string, float, float)> list, string nodeId, string fallback, string kind, float factor)
        {
            if (factor != 1f) list.Add(($"{TalentName(nodeId, fallback)} ({kind})", 0f, (factor - 1f) * 100f));
        }

        // One row per counted star whose Map text gives this Stat. A star's share = its own fraction (ValueAt(rank) / 100), x100 here
        // (SE_Paragon carry = total * 100 flat). Damage-taken stats: the hook uses the guarded sum (Asymptote), rows show raw shares.
        private static void Para(List<(string, float, float)> list, ParagonEffects.Stat st, bool percent, float sign = 1f)
        {
            var stars = ParagonEffects.ActiveStars;
            for (int i = 0; i < stars.Count; i++)
            {
                ParagonEffects.Stat[] stats = ParagonEffects.StatsOfStar(stars[i].star);
                if (stats == null || Array.IndexOf(stats, st) < 0) continue;
                float v = stars[i].fraction * 100f * sign;
                if (v != 0f) list.Add(("Paragon: " + stars[i].star.Name, percent ? 0f : v, percent ? v : 0f));
            }
        }

        private static void FitnessRows(List<(string, float, float)> list, Player p, string stat)
        {
            var rows = ParagonFitness.StatRows;
            var stars = ParagonEffects.ActiveStars;
            bool? form = null;
            for (int r = 0; r < rows.Length; r++)
            {
                if (!string.Equals(rows[r].stat, stat, StringComparison.Ordinal)) continue;
                for (int i = 0; i < stars.Count; i++)
                {
                    if (!string.Equals(stars[i].star.EffectFormat, rows[r].format, StringComparison.Ordinal)) continue;
                    if (rows[r].form && !(form ?? (form = ParagonFx.InBeastForm()).Value)) continue;
                    float v;
                    if (rows[r].food)
                    {
                        ParagonFitness.FoodSums(p, out float fh, out float fs);
                        v = (stat == "MaxHealth" ? fh : fs) * stars[i].fraction;
                        if (v != 0f) list.Add(("Paragon: " + stars[i].star.Name, v, 0f));
                    }
                    else
                    {
                        v = stars[i].fraction * 100f;
                        if (v != 0f) list.Add(("Paragon: " + stars[i].star.Name, 0f, v));
                    }
                }
            }
        }
    }
}
