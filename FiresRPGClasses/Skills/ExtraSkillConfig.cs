using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.Sync;

namespace FiresRPGClasses.ExtraSkills
{
    /// <summary>
    /// The extra skills' settings, section "ExtraSkills" of RPGClasses' own config. Defaults and ranges are the old
    /// VACombinedSkills values; key names carry the code id (stable), descriptions carry the display name.
    /// </summary>
    public static class ExtraSkillConfig
    {
        public const string Section = "8 - Extra skills";

        public static ConfigEntry<float> AgilityRunGain, AgilityJumpGain, AgilityAirGain, AgilityDodgeStaminaCost,
            AgilityBaseStaminaIncrease, AgilityStaminaPerLevel;
        public static ConfigEntry<int> AgilityBonusLevel;

        public static ConfigEntry<float> EnduranceHitsTakenGain, EnduranceTimeOutGain, EnduranceEffectFactor;

        public static ConfigEntry<float> VigorEnemyHitGain, VigorEnemyKillGain, VigorLastHitGain, VigorTimeOutGain,
            VigorBaseHPIncrease, VigorHPPerLevel, VigorRegenPerTick, VigorRegenTickInterval;
        public static ConfigEntry<int> VigorBonusRegenLevel;

        public static ConfigEntry<float> IntellectBaseEitrIncrease, IntellectEitrPerLevel, IntellectEitrCostReductionPercent;
        public static ConfigEntry<int> IntellectBonusLevel;

        public static ConfigEntry<float> HoarderBaseCarryWeightIncrease, HoarderCarryWeightPerLevel;
        public static ConfigEntry<int> HoarderExtraRows, HoarderBonusLevel;

        private static ConfigSync _sync;
        private static readonly List<ConfigEntry<float>> Floats = new List<ConfigEntry<float>>();
        private static readonly List<ConfigEntry<int>> Ints = new List<ConfigEntry<int>>();

        private static ConfigEntry<float> F(ConfigFile c, string key, float def, string desc, float min, float max)
        {
            ConfigEntry<float> e = c.Bind(Section, key, def, new ConfigDescription(desc + " [Synced with Server]", new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { IsAdvanced = true }));
            Floats.Add(e);
            return e;
        }

        private static ConfigEntry<int> I(ConfigFile c, string key, int def, string desc, int min, int max)
        {
            ConfigEntry<int> e = c.Bind(Section, key, def, new ConfigDescription(desc + " [Synced with Server]", new AcceptableValueRange<int>(min, max), new ConfigurationManagerAttributes { IsAdvanced = true }));
            Ints.Add(e);
            return e;
        }

        internal static void Bind(ConfigFile c)
        {
            string ag = ExtraSkillNames.Get(ExtraSkillSystem.AgilityId);
            string en = ExtraSkillNames.Get(ExtraSkillSystem.EnduranceId);
            string vi = ExtraSkillNames.Get(ExtraSkillSystem.VigorId);
            string it = ExtraSkillNames.Get(ExtraSkillSystem.IntellectId);
            string ho = ExtraSkillNames.Get(ExtraSkillSystem.HoarderId);

            AgilityRunGain = F(c, "Agility - Gain from DistanceRun (per 100m)", 1f, ag + ": skill XP gained per 100 meters run.", 0f, 10f);
            AgilityJumpGain = F(c, "Agility - Gain from Jumps", 0.5f, ag + ": skill XP gained per jump.", 0f, 5f);
            AgilityAirGain = F(c, "Agility - Gain from DistanceAir (per 100m)", 0.5f, ag + ": skill XP gained per 100 meters in air (falling/gliding).", 0f, 5f);
            AgilityDodgeStaminaCost = F(c, "Agility - Dodge Stamina Cost", 10f, ag + ": stamina cost for a dodge when the bonus is active. Range 1-10 (vanilla = 10).", 1f, 10f);
            AgilityBonusLevel = I(c, "Agility - Bonus Level Requirement", 0, ag + ": level that turns on the custom dodge stamina cost (0 = off).", 0, 100);
            AgilityBaseStaminaIncrease = F(c, "Agility - Base Stamina Increase", 0f, ag + ": fixed stamina added from the start (independent of level).", 0f, 100f);
            AgilityStaminaPerLevel = F(c, "Agility - Additional Stamina Per Level", 1f, ag + ": stamina gained per level (1 = +100 at level 100).", 0f, 10f);

            EnduranceHitsTakenGain = F(c, "Endurance - Gain from HitsTakenEnemies", 2f, en + ": skill XP per hit taken from an enemy.", 0f, 10f);
            EnduranceTimeOutGain = F(c, "Endurance - Gain from TimeOutOfBase (per hour)", 5f, en + ": skill XP per hour spent outside base.", 0f, 20f);
            EnduranceEffectFactor = F(c, "Endurance - Effect Strength Factor", 1f, en + ": multiplier for the damage reduction (1 = up to 20% less damage at level 100).", 0.01f, 5f);

            VigorEnemyHitGain = F(c, "Vigor - Gain from EnemyHits", 1f, vi + ": skill XP per hit landed on an enemy.", 0f, 5f);
            VigorEnemyKillGain = F(c, "Vigor - Gain from EnemyKills", 10f, vi + ": skill XP per enemy killed.", 0f, 50f);
            VigorLastHitGain = F(c, "Vigor - Gain from EnemyKillsLastHits", 15f, vi + ": extra skill XP for the killing blow.", 0f, 50f);
            VigorTimeOutGain = F(c, "Vigor - Gain from TimeOutOfBase (per hour)", 2f, vi + ": skill XP per hour outside base.", 0f, 10f);
            VigorBaseHPIncrease = F(c, "Vigor - Base HP Increase", 0f, vi + ": fixed health added from the start (independent of level).", 0f, 100f);
            VigorHPPerLevel = F(c, "Vigor - Additional HP Per Level", 0.5f, vi + ": health gained per level (0.5 = +50 at level 100).", 0f, 10f);
            VigorBonusRegenLevel = I(c, "Vigor - Level Required for bonus HP Regen", 30, vi + ": level for the passive health regeneration (0 = off).", 0, 100);
            VigorRegenPerTick = F(c, "Vigor - Bonus HP Regen Amount Per Tick", 1f, vi + ": health healed per regeneration tick.", 0.1f, 10f);
            VigorRegenTickInterval = F(c, "Vigor - Regen Tick Interval (seconds)", 1f, vi + ": seconds between regeneration ticks.", 0.1f, 10f);

            IntellectBaseEitrIncrease = F(c, "Intellect - Base Eitr Increase", 0f, it + ": fixed eitr added from the start (independent of level).", 0f, 100f);
            IntellectEitrPerLevel = F(c, "Intellect - Additional Eitr Per Level", 0.5f, it + ": eitr gained per level (0.5 = +50 at level 100).", 0f, 10f);
            IntellectEitrCostReductionPercent = F(c, "Intellect - Eitr Cost Reduction (%)", 50f, it + ": percent less eitr per weapon attack when the bonus is active.", 0f, 99f);
            IntellectBonusLevel = I(c, "Intellect - Bonus Level Requirement", 0, it + ": level that turns on the eitr cost reduction (0 = off).", 0, 100);

            HoarderBaseCarryWeightIncrease = F(c, "Hoarder - Base Carry Weight Increase", 0f, ho + ": fixed carry weight added from the start (independent of level).", 0f, 300f);
            HoarderCarryWeightPerLevel = F(c, "Hoarder - Additional Carry Weight Per Level", 3f, ho + ": carry weight gained per level (3 = +300 at level 100).", 0f, 10f);
            HoarderExtraRows = I(c, "Hoarder - Extra Inventory Rows", 1, ho + ": extra inventory rows when the bonus is active.", 0, 4);
            HoarderBonusLevel = I(c, "Hoarder - Bonus Level Requirement", 50, ho + ": level for the extra inventory rows (0 = off).", 0, 100);

            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".extraskills")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " extra skills",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    // Raised only when a synced key changes (last: the section was renamed "ExtraSkills" -> "8 - Extra skills").
                    MinimumRequiredVersion = "0.1.112",
                    ModRequired = false,
                };
                foreach (ConfigEntry<float> e in Floats) _sync.AddConfigEntry(e);
                foreach (ConfigEntry<int> e in Ints) _sync.AddConfigEntry(e);
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"extra skills config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
        }
    }
}
