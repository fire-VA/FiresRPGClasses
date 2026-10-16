using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresRPGClasses.Combat;
using UnityEngine;

namespace FiresRPGClasses.ExtraSkills
{
    /// <summary>
    /// Five extra Valheim skills ported from VACombinedSkills: Agility, Endurance, Vigor, Intellect, Hoarder (code ids;
    /// players see ExtraSkillNames). Each effect is a public static calculation on a skill level so a test can read the
    /// skill's own part; the game hooks that apply them are RPGClasses' existing ones, which call in here.
    /// </summary>
    public static class ExtraSkillSystem
    {
        public const string AgilityId = "Agility";
        public const string EnduranceId = "Endurance";
        public const string VigorId = "Vigor";
        public const string IntellectId = "Intellect";
        public const string HoarderId = "Hoarder";

        public static readonly string[] Ids = { AgilityId, EnduranceId, VigorId, IntellectId, HoarderId };

        // The old mod's ids: |stable hash of the code name|. Saved profiles hold these numbers, so they never change.
        public static readonly Skills.SkillType Agility = TypeOf(AgilityId);
        public static readonly Skills.SkillType Endurance = TypeOf(EnduranceId);
        public static readonly Skills.SkillType Vigor = TypeOf(VigorId);
        public static readonly Skills.SkillType Intellect = TypeOf(IntellectId);
        public static readonly Skills.SkillType Hoarder = TypeOf(HoarderId);

        private static readonly Dictionary<Skills.SkillType, string> CodeIds = new Dictionary<Skills.SkillType, string>
        {
            { Agility, AgilityId }, { Endurance, EnduranceId }, { Vigor, VigorId }, { Intellect, IntellectId }, { Hoarder, HoarderId },
        };

        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            { AgilityId, "Increases maximum stamina and reduces dodge stamina cost at higher levels." },
            { EnduranceId, "Reduces damage taken." },
            { VigorId, "Increases maximum health and provides bonuses at higher levels." },
            { IntellectId, "Increases maximum eitr and reduces eitr cost for magic attacks at higher levels." },
            { HoarderId, "Increases carry weight and adds extra inventory rows at higher levels." },
        };

        public static Skills.SkillType TypeOf(string codeId) => (Skills.SkillType)Math.Abs(codeId.GetStableHashCode());

        public static bool IsExtra(Skills.SkillType type) => CodeIds.ContainsKey(type);

        public static string CodeIdOf(Skills.SkillType type) => CodeIds.TryGetValue(type, out string id) ? id : null;

        public static string DescriptionOf(string codeId) => codeId != null && Descriptions.TryGetValue(codeId, out string d) ? d : "";

        public static IEnumerable<KeyValuePair<Skills.SkillType, string>> All => CodeIds;

        /// <summary>Called once from the plugin's Awake (client and dedicated server, so the sync entries exist on both).</summary>
        public static void Init(ConfigFile config)
        {
            ExtraSkillConfig.Bind(config);
            foreach (var kv in CodeIds)
                FiresRPGClassesPlugin.Log.LogDebug($"extra skill {kv.Value} = SkillType {(int)kv.Key}, shown as {ExtraSkillNames.Get(kv.Value)}");
        }

        // ---- the skill's own part, from a level (0-100) -------------------------------------------------------------

        public static bool BonusActive(int requiredLevel, float level) => requiredLevel > 0 && level >= requiredLevel;

        public static float AgilityStaminaBonus(float level) => ExtraSkillConfig.AgilityBaseStaminaIncrease.Value + level * ExtraSkillConfig.AgilityStaminaPerLevel.Value;

        /// <summary>The dodge cost the skill sets (an absolute value, as before), or the game's cost when the bonus is off.</summary>
        public static float AgilityDodgeCost(float level, float gameCost) =>
            BonusActive(ExtraSkillConfig.AgilityBonusLevel.Value, level) ? ExtraSkillConfig.AgilityDodgeStaminaCost.Value : gameCost;

        /// <summary>Multiplier on damage taken: 1 - level/100 x 0.2 x effect factor.</summary>
        public static float EnduranceDamageFactor(float level) => 1f - level / 100f * 0.2f * ExtraSkillConfig.EnduranceEffectFactor.Value;

        public static float VigorHealthBonus(float level) => ExtraSkillConfig.VigorBaseHPIncrease.Value + level * ExtraSkillConfig.VigorHPPerLevel.Value;

        public static bool VigorRegenActive(float level) => BonusActive(ExtraSkillConfig.VigorBonusRegenLevel.Value, level);

        public static float IntellectEitrBonus(float level) => ExtraSkillConfig.IntellectBaseEitrIncrease.Value + level * ExtraSkillConfig.IntellectEitrPerLevel.Value;

        public static float IntellectEitrCostFactor(float level) =>
            BonusActive(ExtraSkillConfig.IntellectBonusLevel.Value, level) ? 1f - ExtraSkillConfig.IntellectEitrCostReductionPercent.Value / 100f : 1f;

        public static float HoarderCarryBonus(float level) => ExtraSkillConfig.HoarderBaseCarryWeightIncrease.Value + level * ExtraSkillConfig.HoarderCarryWeightPerLevel.Value;

        public static int HoarderExtraRows(float level) => BonusActive(ExtraSkillConfig.HoarderBonusLevel.Value, level) ? ExtraSkillConfig.HoarderExtraRows.Value : 0;

        public static float Level(Player player, Skills.SkillType type) =>
            player != null && player.GetSkills() != null ? player.GetSkills().GetSkillLevel(type) : 0f;

        internal static bool IsLocal(Character c) => c != null && c == Player.m_localPlayer;

        internal static bool Ready => ExtraSkillConfig.AgilityRunGain != null;

        // ---- call-ins from RPGClasses' existing hooks --------------------------------------------------------------------

        /// <summary>From AttributeEffects' GetTotalFoodValue hook, after the attribute pools: Vigor health, Agility stamina, Intellect eitr.</summary>
        public static void AddFoodPools(Player player, ref float hp, ref float stamina, ref float eitr)
        {
            if (!Ready || !IsLocal(player)) return;
            float h = VigorHealthBonus(Level(player, Vigor));
            float s = AgilityStaminaBonus(Level(player, Agility));
            float e = IntellectEitrBonus(Level(player, Intellect));
            hp += h;
            stamina += s;
            eitr += e;
            LogOnce("pools", $"pools: skill +{h} health (Vigor) +{s} stamina (Agility) +{e} eitr (Intellect); combined with attributes {hp}/{stamina}/{eitr}");
        }

        /// <summary>From AttributeEffects' GetMaxCarryWeight hook, after the attribute carry: Hoarder.</summary>
        public static void AddCarry(Player player, ref float carry)
        {
            if (!Ready || !IsLocal(player)) return;
            float c = HoarderCarryBonus(Level(player, Hoarder));
            carry += c;
            LogOnce("carry", $"carry: skill +{c} (Hoarder); combined with attributes {carry}");
        }

        /// <summary>From PassiveHooks' RPC_Damage hook (damage the local player takes): Endurance.</summary>
        public static void OnLocalHitTaken(Character target, HitData hit)
        {
            if (!Ready || hit == null || !(target is Player p) || !IsLocal(p)) return;
            float f = EnduranceDamageFactor(Level(p, Endurance));
            float before = hit.GetTotalDamage();
            hit.ApplyModifier(f);
            LogOnce("damagetaken", $"damage taken: skill factor {f} (Endurance); combined factor {f} x attribute 1 x passive 1 on this hook (hit {before} -> {hit.GetTotalDamage()}; elemental attribute and passives scale the hit separately)");
        }

        /// <summary>From ClassXpActivity's PlayerProfile.IncrementStat hook (local profile only): skill XP from player stats.</summary>
        public static void OnStat(PlayerStatType stat, float amount)
        {
            Player p = Player.m_localPlayer;
            if (!Ready || p == null) return;
            switch (stat)
            {
                // Agility, Endurance, Vigor: the old Game.IncrementPlayerStat gains (configurable)
                case PlayerStatType.DistanceRun: Raise(p, Agility, amount / 100f * ExtraSkillConfig.AgilityRunGain.Value); break;
                case PlayerStatType.Jumps: Raise(p, Agility, amount * ExtraSkillConfig.AgilityJumpGain.Value); break;
                case PlayerStatType.DistanceAir: Raise(p, Agility, amount / 100f * ExtraSkillConfig.AgilityAirGain.Value); break;
                case PlayerStatType.HitsTakenEnemies: Raise(p, Endurance, amount * ExtraSkillConfig.EnduranceHitsTakenGain.Value); break;
                case PlayerStatType.TimeOutOfBase:
                    Raise(p, Endurance, amount / 3600f * ExtraSkillConfig.EnduranceTimeOutGain.Value);
                    Raise(p, Vigor, amount / 3600f * ExtraSkillConfig.VigorTimeOutGain.Value);
                    break;
                case PlayerStatType.EnemyHits: Raise(p, Vigor, amount * ExtraSkillConfig.VigorEnemyHitGain.Value); break;
                case PlayerStatType.EnemyKills: Raise(p, Vigor, amount * ExtraSkillConfig.VigorEnemyKillGain.Value); break;
                case PlayerStatType.EnemyKillsLastHits: Raise(p, Vigor, amount * ExtraSkillConfig.VigorLastHitGain.Value); break;
                // Intellect: fixed values (not configurable, as before)
                case PlayerStatType.DistanceTraveled: Raise(p, Intellect, amount * 0.05f); break;
                case PlayerStatType.WorldLoads: Raise(p, Intellect, 3f); break;
                case PlayerStatType.RavenTalk: Raise(p, Intellect, 2f); break;
                case PlayerStatType.BossLastHits: Raise(p, Intellect, 20f); break;
                // Hoarder: fixed values; crafts-or-upgrades also feeds Intellect
                case PlayerStatType.Crafts: Raise(p, Hoarder, 0.5f); break;
                case PlayerStatType.CraftsOrUpgrades: Raise(p, Intellect, 0.5f); Raise(p, Hoarder, 0.5f); break;
                case PlayerStatType.Upgrades: Raise(p, Hoarder, 1f); break;
                case PlayerStatType.ItemStandUses: Raise(p, Hoarder, 0.3f); break;
                case PlayerStatType.ArmorStandUses: Raise(p, Hoarder, 0.3f); break;
                case PlayerStatType.ItemsPickedUp: Raise(p, Hoarder, 0.1f); break;
            }
        }

        private static void Raise(Player p, Skills.SkillType type, float xp)
        {
            if (xp > 0f) p.GetSkills().RaiseSkill(type, xp);
        }

        // ---- logging: one Debug line per kind per session --------------------------------------------------------------

        private static readonly HashSet<string> Logged = new HashSet<string>();

        internal static void LogOnce(string kind, string message)
        {
            if (Logged.Add(kind)) FiresRPGClassesPlugin.Log.LogDebug("extra skills " + message);
        }
    }
}
