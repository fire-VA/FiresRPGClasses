using System;
using BepInEx.Configuration;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.215 (Fire 10-30: "Core shouldn't even have configs for other mods ... Climbing movement and the crosshair can go in the RPG
    /// classes"; plan Tools\CORE_CONFIG_MOVE_PLAN_20261030.md step 1): the settings for Core's shared code that RPGClasses owns. Each is
    /// bound HERE under the same section + key Core used, handed to Core with CoreSettings.Provide (Core's code reads it; with RPGClasses
    /// absent Core leaves the feature off), and on the first load its value is copied once from Core's own cfg so nobody's setting resets.
    /// Also: RPGClasses is the run slide's consumer (it ships the slide's clips), so Core's Auto turns the slide on only with RPGClasses.
    /// Everything goes through one method that needs Core 0.2.1036+ (CoreSettings); on an older Core it logs once and Core keeps its own keys.
    /// </summary>
    internal static class CoreSettingsOwned
    {
        private const string Owner = "FiresRPGClasses";

        internal static void Bind(ConfigFile config)
        {
            try { BindAll(config); }
            catch (Exception e) when (e is MissingMethodException || e is TypeLoadException || e is MissingMemberException)
            {
                FiresRPGClassesPlugin.Log?.LogDebug("[CoreSettings] the loaded Core has no CoreSettings (older than 0.2.1036): Core keeps its own melee-aim / crosshair / run-slide keys");
            }
        }

        // 0.1.216 (Fire 11-02, config list): RPGClasses owns the settings of Core's progression, ground rings and locational damage. Core's own
        // typed classes are bound with THIS mod's ConfigFile and ConfigSync (one implementation; Core reads the same entries) on client and
        // dedicated server. A Core that still binds them itself (older than 0.2.1086) has them initialised already: then nothing happens here.
        private static FiresCore.Sync.ConfigSync s_sync;
        private static bool s_ownsCoreFeatures;

        internal static void BindCoreFeatures(ConfigFile config, HarmonyLib.Harmony harmony)
        {
            if (FiresCore.Classes.ProgressionSettings.IsInitialized)
            {
                FiresRPGClassesPlugin.Log?.LogDebug("[CoreSettings] this Core still binds progression, ground rings and locational damage itself (older than 0.2.1086)");
                return;
            }
            s_ownsCoreFeatures = true;
            var saved = SavedKeys(config);
            s_sync = new FiresCore.Sync.ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".core")
            {
                DisplayName = FiresRPGClassesPlugin.PluginName + " progression",
                CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                MinimumRequiredVersion = "0.1.216",
                ModRequired = false,
            };
            FiresCore.Classes.ProgressionSettings.Initialize(config);
            FiresCore.Classes.ProgressionSettings.BindToSync(s_sync);
            FiresCore.Npc.Archetypes.Effects.GroundRingConfig.Initialize(config);
            FiresCore.Combat.LocationalDamage.Register(harmony, config);
            FiresCore.Combat.LocationalDamage.BindToSync(s_sync);
            int moved = 0;
            foreach (var def in new System.Collections.Generic.List<ConfigDefinition>(config.Keys))
                if (def.Section == "FiresRPGClasses.Progression" || def.Section == "GroundRings" || def.Key.StartsWith("Locational Damage"))
                    moved += CopyFromCore(config, def, saved) ? 1 : 0;
            FiresRPGClassesPlugin.Log?.LogDebug($"[CoreSettings] progression, ground rings and locational damage bound here ({moved} value(s) copied from Core's cfg)");
        }

        private static bool CopyFromCore(ConfigFile config, ConfigDefinition def, System.Collections.Generic.HashSet<ConfigDefinition> saved)
        {
            if (saved.Contains(def) || !FiresCore.Config.CoreSettings.TryReadCoreSaved(def.Section, def.Key, out string old)) return false;
            try
            {
                ConfigEntryBase entry = config[def];
                entry.BoxedValue = TomlTypeConverter.ConvertToValue(old, entry.SettingType);
                FiresRPGClassesPlugin.Log?.LogDebug($"[CoreSettings] migrated {def.Section}/{def.Key} from Core: {old}");
                return true;
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[CoreSettings] {def.Section}/{def.Key}: Core's saved value '{old}' did not convert ({e.Message}); default kept"); return false; }
        }

        private static System.Collections.Generic.HashSet<ConfigDefinition> SavedKeys(ConfigFile config)
        {
            var set = new System.Collections.Generic.HashSet<ConfigDefinition>();
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config) as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
                if (orphans != null) foreach (var k in orphans.Keys) set.Add(k);
            }
            catch { }
            return set;
        }

        private static void BindAll(ConfigFile config)
        {
            Own(config, "Combat", "Melee aims with the crosshair", true,
                "Your melee swings tilt up or down toward the crosshair (a crab at your feet, a bird or a wall above), up to Melee aim max pitch. The swing's arc and reach stay the weapon's own.");
            Own(config, "Camera", "Crosshair red over an enemy", true,
                "The crosshair turns red while it is over a hostile creature (not tamed, not a player); friends and things you can use keep the game's yellow.");
            FiresCore.Movement.MovementFeature.Register("Run slide", Owner);
            // 0.1.216: Melee aim max pitch (Core's melee aim class is internal, so bound here and read by Core through CoreSettings)
            if (s_ownsCoreFeatures)
            {
                var saved = SavedKeys(config);
                ConfigEntry<float> pitch = config.Bind("Combat", "Melee aim max pitch", 60f,
                    new ConfigDescription("Degrees a swing may tilt up or down toward the crosshair. An attack whose own limit is higher keeps it.", new AcceptableValueRange<float>(0f, 89f)));
                CopyFromCore(config, pitch.Definition, saved);
                FiresCore.Config.CoreSettings.Provide("Combat", "Melee aim max pitch", pitch, Owner);
            }
        }

        private static void Own(ConfigFile config, string section, string key, bool def, string description)
        {
            var def0 = new ConfigDefinition(section, key);
            bool hadOwn = HasSaved(config, def0);
            ConfigEntry<bool> entry = config.Bind(def0, def, new ConfigDescription(description));
            if (!hadOwn && FiresCore.Config.CoreSettings.TryReadCoreSaved(section, key, out string old) && bool.TryParse(old, out bool v))
            {
                entry.Value = v;
                FiresRPGClassesPlugin.Log?.LogDebug($"[CoreSettings] migrated {section}/{key} from Core: {v}");
            }
            FiresCore.Config.CoreSettings.Provide(section, key, () => entry.Value, Owner);
        }

        // whether THIS mod's cfg already holds the key (then it's never overwritten from Core's): BepInEx keeps not-yet-bound saved keys
        // as orphans until the first Bind
        private static bool HasSaved(ConfigFile config, ConfigDefinition def)
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config) as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
                return orphans != null && orphans.ContainsKey(def);
            }
            catch { return false; }
        }
    }
}
