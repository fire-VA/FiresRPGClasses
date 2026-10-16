using System;
using System.Collections.Generic;
using FiresRPGClasses.Combat;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.ExtraSkills
{
    /// <summary>
    /// Registration of the five skills as real Valheim skills. Valheim 1.0 keeps the skill definitions on each player's
    /// Skills component (Skills.m_skills), not on ObjectDB (the old mod's ObjectDB.m_skills is gone), so the defs are
    /// added there when the component wakes; the vanilla dialog, level-up message, save and load then work unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.Awake))]
    internal static class ExtraSkillsRegisterDefs
    {
        [HarmonyPostfix]
        private static void AddDefs(Skills __instance)
        {
            if (__instance == null || __instance.m_skills == null) return;
            foreach (var kv in ExtraSkillSystem.All)
            {
                if (__instance.m_skills.Exists(d => d != null && d.m_skill == kv.Key)) continue;
                __instance.m_skills.Add(new Skills.SkillDef
                {
                    m_skill = kv.Key,
                    m_icon = IconFor(__instance, kv.Value),
                    m_description = ExtraSkillSystem.DescriptionOf(kv.Value),
                    m_increseStep = 1f,
                });
            }
            ExtraSkillNamesLocalization.Apply();
        }

        // The old mod's icons came from its own asset bundle; until ours exist, each borrows a vanilla skill's icon.
        private static Sprite IconFor(Skills skills, string codeId)
        {
            Skills.SkillType borrow;
            switch (codeId)
            {
                case ExtraSkillSystem.AgilityId: borrow = Skills.SkillType.Jump; break;
                case ExtraSkillSystem.EnduranceId: borrow = Skills.SkillType.Blocking; break;
                case ExtraSkillSystem.VigorId: borrow = Skills.SkillType.Unarmed; break;
                case ExtraSkillSystem.IntellectId: borrow = Skills.SkillType.ElementalMagic; break;
                default: borrow = Skills.SkillType.Run; break;
            }
            Skills.SkillDef def = skills.m_skills.Find(d => d != null && d.m_skill == borrow);
            return def != null ? def.m_icon : null;
        }
    }

    /// <summary>Saved levels of the five ids survive Skills.Load (vanilla drops SkillType values outside the enum).</summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.IsSkillValid))]
    internal static class ExtraSkillsValid
    {
        [HarmonyPrefix]
        private static bool Prefix(Skills.SkillType type, ref bool __result)
        {
            if (!ExtraSkillSystem.IsExtra(type)) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>raiseskill accepts the code id or the display name (spaces removed, any case).</summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.CheatRaiseSkill))]
    internal static class ExtraSkillsCheatRaise
    {
        [HarmonyPrefix]
        private static bool Prefix(Skills __instance, string name, float value)
        {
            if (!ExtraSkillsCheatNames.TryFind(name, out Skills.SkillType type, out string codeId)) return true;
            Skills.Skill skill = __instance.GetSkill(type);
            skill.m_level = Mathf.Clamp(skill.m_level + value, 0f, 100f);
            skill.m_accumulator = 0f;
            string shown = ExtraSkillNames.Get(codeId);
            __instance.m_player?.Message(MessageHud.MessageType.TopLeft, $"Skill increased {shown}: {(int)skill.m_level}", 0, skill.m_info?.m_icon);
            Console.instance?.Print($"Skill {shown} = {skill.m_level}");
            return false;
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.CheatResetSkill))]
    internal static class ExtraSkillsCheatReset
    {
        [HarmonyPrefix]
        private static bool Prefix(Skills __instance, string name)
        {
            if (!ExtraSkillsCheatNames.TryFind(name, out Skills.SkillType type, out string codeId)) return true;
            __instance.ResetSkill(type);
            Console.instance?.Print($"Skill {ExtraSkillNames.Get(codeId)} reset");
            return false;
        }
    }

    internal static class ExtraSkillsCheatNames
    {
        internal static bool TryFind(string name, out Skills.SkillType type, out string codeId)
        {
            string n = (name ?? "").Replace(" ", "").ToLowerInvariant();
            foreach (var kv in ExtraSkillSystem.All)
            {
                if (n == kv.Value.ToLowerInvariant() || n == ExtraSkillNames.Get(kv.Value).Replace(" ", "").ToLowerInvariant())
                {
                    type = kv.Key;
                    codeId = kv.Value;
                    return true;
                }
            }
            type = Skills.SkillType.None;
            codeId = null;
            return false;
        }
    }

    /// <summary>
    /// The vanilla dialog and level-up message show Localize("$skill_" + type), which for these ids is the number;
    /// the display names are added as those words. Re-applied while playing because a language change clears them.
    /// </summary>
    internal static class ExtraSkillNamesLocalization
    {
        // Localization lives in assembly_guiutils (not publicized); AddWord(string, string) is private there.
        private static readonly System.Reflection.MethodInfo AddWord = AccessTools.Method(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) });

        internal static void Apply()
        {
            Localization loc = Localization.instance;
            if (loc == null) return;
            ExtraSkillSystem.LogOnce("words", AddWord != null ? "display names: Localization.AddWord found, names added as $skill_<id>" : "display names: Localization.AddWord not found, skills dialog shows the numeric id");
            foreach (var kv in ExtraSkillSystem.All)
            {
                string key = "skill_" + ((int)kv.Key).ToString();
                string name = ExtraSkillNames.Get(kv.Value);
                if (loc.Localize("$" + key) != name) AddWord?.Invoke(loc, new object[] { key, name });
            }
        }
    }

    /// <summary>Agility: the dodge's stamina cost when its bonus is active (the old absolute value). Feeds UseStamina's factors after.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.GetDodgeStaminaUse))]
    internal static class ExtraSkillsDodgeCost
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ref float __result)
        {
            if (!ExtraSkillSystem.Ready || !ExtraSkillSystem.IsLocal(__instance)) return;
            float level = ExtraSkillSystem.Level(__instance, ExtraSkillSystem.Agility);
            if (!ExtraSkillSystem.BonusActive(ExtraSkillConfig.AgilityBonusLevel.Value, level)) return;
            float game = __result;
            __result = ExtraSkillSystem.AgilityDodgeCost(level, game);
            float combined = __result * PassiveKit.StaminaCostFactor(__instance) * AttributeEffects.StaminaCostFactor(__instance)
                * BuffKit.StaminaCostFactor(__instance) * PassiveKit2.StaminaCostFactor(__instance);
            ExtraSkillSystem.LogOnce("dodge", $"dodge cost: skill sets {__result} (Agility, game {game}); combined with attribute x passive stamina factors {combined}");
        }
    }

    /// <summary>
    /// Intellect: weapon attack eitr cost. Valheim 1.0 has GetAttackEitr() and GetAttackEitr(Character, ItemData); the
    /// attack path (Attack.Start, the spend, the hold check) calls the parameterless one, which is patched here by name.
    /// </summary>
    [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackEitr), new Type[0])]
    internal static class ExtraSkillsAttackEitr
    {
        [HarmonyPostfix]
        private static void Postfix(Attack __instance, ref float __result)
        {
            if (!ExtraSkillSystem.Ready || __result <= 0f) return;
            Player p = __instance.m_character as Player;
            if (!ExtraSkillSystem.IsLocal(p)) return;
            float f = ExtraSkillSystem.IntellectEitrCostFactor(ExtraSkillSystem.Level(p, ExtraSkillSystem.Intellect));
            if (f >= 1f) return;
            __result *= f;
            if (__result < 1f) __result = 1f;
            ExtraSkillSystem.LogOnce("eitrcost", $"attack eitr: skill factor {f} (Intellect); combined with passive spell factor {f * PassiveKit.SpellCostFactor(p)}");
        }
    }

    /// <summary>Per-frame upkeep of the local player: Vigor's regeneration ticks, Hoarder's rows, the display-name words.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class ExtraSkillsPlayerTick
    {
        private static float _regenTimer;
        private static float _wordsTimer;

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (!ExtraSkillSystem.Ready || __instance != Player.m_localPlayer) return;
            float dt = Time.deltaTime;

            if (ExtraSkillSystem.VigorRegenActive(ExtraSkillSystem.Level(__instance, ExtraSkillSystem.Vigor)))
            {
                _regenTimer += dt;
                float interval = ExtraSkillConfig.VigorRegenTickInterval.Value;
                if (_regenTimer >= interval)
                {
                    _regenTimer -= interval;
                    float amount = ExtraSkillConfig.VigorRegenPerTick.Value;
                    if (amount > 0f && !__instance.IsDead())
                    {
                        __instance.Heal(amount, false);
                        ExtraSkillSystem.LogOnce("regen", $"health regen: skill heals {amount} every {interval}s (Vigor); attribute x passive regen factors do not scale this tick");
                    }
                }
            }
            else _regenTimer = 0f;

            HoarderRows.Tick(__instance);

            _wordsTimer -= dt;
            if (_wordsTimer <= 0f)
            {
                _wordsTimer = 5f;
                ExtraSkillNamesLocalization.Apply();
            }
        }
    }
}
