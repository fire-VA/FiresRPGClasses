using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.Sync;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// [13 - Balance] (ESO-style builds: gate by effort, not flat caps). Stack COUNTS keep a hard max (UI and
    /// network); the BONUS they give runs through SoftCap.Soft with the old cap as its knee. Summon counts stay hard limits, but as
    /// server performance keys. Every key is synced from the server and advanced. Bound once per process (ConfigSync-once rule),
    /// from AttributeEffects.Bind (both the client and the dedicated-server paths call that).
    /// </summary>
    internal static class BalanceConfig
    {
        private const string Section = "13 - Balance";
        private const string MinimumSyncedVersion = "0.1.131";

        internal static ConfigEntry<int> FuryMaxStacks, FuryFullStacks, ComboMaxHits, SummonServerMax, LegionServerMax;
        internal static ConfigEntry<float> FuryBonusKnee, ComboBonusKnee, CooldownReductionKnee, CooldownReductionLimit;

        private static bool _bound;
        private static ConfigSync _sync;

        internal static void Bind(ConfigFile config)
        {
            if (_bound) return;
            _bound = true;
            var ints = new List<ConfigEntry<int>>();
            var floats = new List<ConfigEntry<float>>();

            FuryMaxStacks = Int(ints, config, "Fury max stacks", 20, 1, 100,
                "Berserker Fury: the most stacks it can hold.");
            FuryFullStacks = Int(ints, config, "Fury full stacks", 10, 1, 100,
                "Fury's full bonus is reached at this many stacks; extra stacks add a softened bonus.");
            FuryBonusKnee = Float(floats, config, "Fury bonus knee", 0.30f, 0f, 5f,
                "Fury's damage bonus at full stacks (0.30 = +30%), softened beyond that.");
            ComboMaxHits = Int(ints, config, "Monk combo max hits", 20, 1, 200,
                "Martial Arts: the most combo hits counted (+5% per hit).");
            ComboBonusKnee = Float(floats, config, "Monk combo bonus knee", 0.25f, 0f, 5f,
                "Martial Arts combo bonus at full value (0.25 = +25%), softened beyond that.");
            SummonServerMax = Int(ints, config, "Summon server limit", 6, 1, 50,
                "The most class summons one player can have at once.");
            LegionServerMax = Int(ints, config, "Legion server limit", 10, 1, 100,
                "The most thralls Necromancer Legion raises at once.");
            CooldownReductionKnee = Float(floats, config, "Cooldown reduction knee", 0.40f, 0f, 0.95f,
                "Class cooldown reduction counts fully up to this share, then shrinks toward the limit.");
            CooldownReductionLimit = Float(floats, config, "Cooldown reduction limit", 0.80f, 0f, 0.95f,
                "Class cooldown reduction never reaches this share, so no skill becomes free.");

            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".balance")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " balance",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    MinimumRequiredVersion = MinimumSyncedVersion,
                    ModRequired = false,
                };
                foreach (ConfigEntry<int> e in ints) _sync.AddConfigEntry(e);
                foreach (ConfigEntry<float> e in floats) _sync.AddConfigEntry(e);
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"balance config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
        }

        private static ConfigEntry<int> Int(List<ConfigEntry<int>> list, ConfigFile config, string key, int value, int min, int max, string text)
        {
            ConfigEntry<int> entry = config.Bind(Section, key, value, new ConfigDescription(text + " [Synced with Server]",
                new AcceptableValueRange<int>(min, max), new ConfigurationManagerAttributes { IsAdvanced = true }));
            list.Add(entry);
            return entry;
        }

        private static ConfigEntry<float> Float(List<ConfigEntry<float>> list, ConfigFile config, string key, float value, float min, float max, string text)
        {
            ConfigEntry<float> entry = config.Bind(Section, key, value, new ConfigDescription(text + " [Synced with Server]",
                new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { IsAdvanced = true }));
            list.Add(entry);
            return entry;
        }

        internal static int Value(ConfigEntry<int> entry, int fallback) => entry != null ? entry.Value : fallback;
        internal static float Value(ConfigEntry<float> entry, float fallback) => entry != null ? entry.Value : fallback;
    }
}
