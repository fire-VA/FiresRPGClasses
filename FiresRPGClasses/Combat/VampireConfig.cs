using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.Sync;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// [12 - Vampire] (Tools\VAMPIRE_CLASS_DESIGN.md §3, §4, §5, §10-§12). Every key is synced from the server; the tuning numbers are
    /// advanced keys. Bound ONCE per process (the ConfigSync-once rule: 0.1.120 bound a section twice on the dedi and Core's ZNet.Awake
    /// threw "same key"), on clients and on the dedicated server alike, so the server's values are the ones that sync.
    /// </summary>
    internal static class VampireConfig
    {
        private const string Section = "12 - Vampire";
        // 10-13 config rule: a fixed constant, raised only when a synced key's name/type/meaning changes.
        private const string MinimumSyncedVersion = "0.1.125";

        internal const string SunMild = "Mild", SunHarsh = "Harsh", SunNone = "None";

        internal static ConfigEntry<string> Sunlight;
        internal static ConfigEntry<float> SunStaminaPenalty, SunBurnShare, SunBurnInterval;
        internal static ConfigEntry<float> BatBaseSeconds, BatMaxSeconds, BatCooldownBase, BatCooldownMax, BatRank2Cooldown;
        internal static ConfigEntry<float> BatEitr, BatHealthShare, BatOverflowShare, BatEarlyEndShare;
        internal static ConfigEntry<float> BatSpeed, BatRank3Speed, BatSkillPerSecond, BatCombatLock, BatReach;
        internal static ConfigEntry<float> DrainCap, DrainBossShare;
        internal static ConfigEntry<float> FrenzyHealthCost, FrenzyAttackSpeed, FrenzyDrainFactor;
        internal static ConfigEntry<float> StalkerDamage, StalkerStealth;
        internal static ConfigEntry<float> MesmeriseMaxHealth, MesmeriseSeconds;
        internal static ConfigEntry<float> TideShare;
        internal static ConfigEntry<bool> SunWardEnabled;
        internal static ConfigEntry<float> SunWardSeconds;

        private static bool _bound;
        private static ConfigSync _sync;

        internal static void Bind(ConfigFile config)
        {
            if (_bound) return;
            _bound = true;
            var synced = new List<ConfigEntryBase>();

            Sunlight = Add(synced, config.Bind(Section, "Sunlight", SunMild, new ConfigDescription(
                "What direct daylight (day, not under a roof) does to a vampire. Mild: 10% less stamina regeneration and no Life Drain. "
                + "Harsh: burns 1% of max health every 5 s. None: cosmetic only. Duskbloom Stew wards it off. [Synced with Server]",
                new AcceptableValueList<string>(SunMild, SunHarsh, SunNone))));
            SunStaminaPenalty = Add(synced, config.Bind(Section, "Sunlight stamina regen penalty", 0.10f, Range("Mild sunlight: less stamina regeneration (0.10 = -10%). [Synced with Server]", 0f, 0.9f)));
            SunBurnShare = Add(synced, config.Bind(Section, "Sunlight burn share", 0.01f, Range("Harsh sunlight: share of max health burned each tick. [Synced with Server]", 0f, 0.5f)));
            SunBurnInterval = Add(synced, config.Bind(Section, "Sunlight burn seconds", 5f, Range("Harsh sunlight: seconds between burns. [Synced with Server]", 1f, 60f)));

            BatBaseSeconds = Add(synced, config.Bind(Section, "Bat Form Base Seconds", 45f, Range("Bat Form's duration at Bat Form skill 0 (Fire 10-13 §11). [Synced with Server]", 5f, 600f)));
            BatMaxSeconds = Add(synced, config.Bind(Section, "Bat Form Max Seconds", 180f, Range("Bat Form's duration at Bat Form skill 100; linear in between. [Synced with Server]", 5f, 1200f)));
            BatCooldownBase = Add(synced, config.Bind(Section, "Bat Form Cooldown Base", 90f, Range("Bat Form's cooldown at skill 0, counted from the end of the form. [Synced with Server]", 5f, 1200f)));
            BatCooldownMax = Add(synced, config.Bind(Section, "Bat Form Cooldown Max", 135f, Range("Bat Form's cooldown at skill 100; linear in between. [Synced with Server]", 5f, 1200f)));
            BatRank2Cooldown = Add(synced, config.Bind(Section, "Bat Form rank 2 cooldown cut", 0.20f, Range("Bat Form rank 2: less cooldown (0.20 = -20%). [Synced with Server]", 0f, 0.9f)));
            BatEitr = Add(synced, config.Bind(Section, "Bat Form eitr", 15f, Range("The one-off eitr cost of transforming (no drain). [Synced with Server]", 0f, 200f)));
            BatHealthShare = Add(synced, config.Bind(Section, "Bat Form health share", 0.30f, Range("The bat's health pool as a share of your max health. [Synced with Server]", 0.05f, 2f)));
            BatOverflowShare = Add(synced, config.Bind(Section, "Bat Form overflow share", 0.5f, Range("Damage beyond the bat's pool reaches you at this share. [Synced with Server]", 0f, 1f)));
            BatEarlyEndShare = Add(synced, config.Bind(Section, "Bat Form early end damage share", 0.25f, Range("The form ends once the bat has lost this share of its pool (design §3); 1 = only at 0. [Synced with Server]", 0.05f, 1f)));
            BatSpeed = Add(synced, config.Bind(Section, "Bat Form speed", 0.30f, Range("Extra flight speed over the bat's own (0.30 = +30%). [Synced with Server]", 0f, 3f)));
            BatRank3Speed = Add(synced, config.Bind(Section, "Bat Form rank 3 speed", 0.15f, Range("Bat Form rank 3: more flight speed on top. [Synced with Server]", 0f, 3f)));
            BatSkillPerSecond = Add(synced, config.Bind(Section, "Bat Form skill gain per second", 0.1f, Range("The Bat Form skill's raise per second of flight (vanilla RaiseSkill factor). [Synced with Server]", 0f, 10f)));
            BatCombatLock = Add(synced, config.Bind(Section, "Bat Form combat lockout seconds", 5f, Range("Bat Form can't start this long after you took damage. [Synced with Server]", 0f, 60f)));
            BatReach = Add(synced, config.Bind(Section, "Bat Form reach metres", 3f, Range("How close the bat must be to open a door or pick an item up (the Use key). [Synced with Server]", 1f, 10f)));

            DrainCap = Add(synced, config.Bind(Section, "Life Drain cap", 0.05f, Range("Life-steal healing per hit: full value up to this share of your max health (the knee), diminishing returns past it, never a hard stop (0.1.131). [Synced with Server]", 0f, 1f)));
            DrainBossShare = Add(synced, config.Bind(Section, "Life Drain boss share", 0.5f, Range("Life-steal on bosses at this share. [Synced with Server]", 0f, 1f)));
            FrenzyHealthCost = Add(synced, config.Bind(Section, "Blood Frenzy health cost", 0.10f, Range("Blood Frenzy costs this share of your current health. [Synced with Server]", 0f, 0.9f)));
            FrenzyAttackSpeed = Add(synced, config.Bind(Section, "Blood Frenzy attack speed", 0.15f, Range("Blood Frenzy: more attack speed (0.15 = +15%). [Synced with Server]", 0f, 2f)));
            FrenzyDrainFactor = Add(synced, config.Bind(Section, "Blood Frenzy life-steal factor", 2f, Range("Blood Frenzy multiplies life-steal by this. [Synced with Server]", 1f, 5f)));
            StalkerDamage = Add(synced, config.Bind(Section, "Night Stalker damage", 0.10f, Range("Night Stalker: more damage at night. [Synced with Server]", 0f, 2f)));
            StalkerStealth = Add(synced, config.Bind(Section, "Night Stalker stealth", 0.15f, Range("Night Stalker: harder to notice at night (0.15 = 15% lower stealth factor). [Synced with Server]", 0f, 0.9f)));
            MesmeriseMaxHealth = Add(synced, config.Bind(Section, "Mesmerise max health", 600f, Range("Mesmerise holds only non-bosses up to this max health. [Synced with Server]", 1f, 100000f)));
            MesmeriseSeconds = Add(synced, config.Bind(Section, "Mesmerise seconds", 3f, Range("Mesmerise's stun (Core's class stun). [Synced with Server]", 0.5f, 30f)));
            TideShare = Add(synced, config.Bind(Section, "Crimson Tide life-steal", 0.10f, Range("Crimson Tide: every hit by you and your allies within range life-steals this share. [Synced with Server]", 0f, 1f)));

            SunWardEnabled = Add(synced, config.Bind(Section, "Sun-ward food enabled", true,
                Describe("Duskbloom Stew (RPG_DuskbloomStew) can be cooked at the Cauldron by vampires and wards off sunlight. [Synced with Server]", true)));
            SunWardSeconds = Add(synced, config.Bind(Section, "Sun-ward food duration", 1800f, Range("Duskbloom Stew's food time and its sun ward, seconds. [Synced with Server]", 60f, 7200f)));

            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".vampire")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " vampire",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    MinimumRequiredVersion = MinimumSyncedVersion,
                    ModRequired = false,
                };
                foreach (ConfigEntryBase entry in synced) AddToSync(entry);
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"vampire config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
        }

        private static void AddToSync(ConfigEntryBase entry)
        {
            switch (entry)
            {
                case ConfigEntry<bool> b: _sync.AddConfigEntry(b); break;
                case ConfigEntry<float> f: _sync.AddConfigEntry(f); break;
                case ConfigEntry<string> s: _sync.AddConfigEntry(s); break;
            }
        }

        private static ConfigEntry<T> Add<T>(List<ConfigEntryBase> list, ConfigEntry<T> entry)
        {
            list.Add(entry);
            return entry;
        }

        private static ConfigDescription Describe(string text, bool advanced = false) => advanced
            ? new ConfigDescription(text, null, new ConfigurationManagerAttributes { IsAdvanced = true })
            : new ConfigDescription(text);

        private static ConfigDescription Range(string text, float min, float max) =>
            new ConfigDescription(text, new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { IsAdvanced = true });

        internal static float Value(ConfigEntry<float> entry, float fallback) => entry != null ? entry.Value : fallback;
        internal static bool Value(ConfigEntry<bool> entry, bool fallback) => entry != null ? entry.Value : fallback;
        internal static string Value(ConfigEntry<string> entry, string fallback) => entry != null ? entry.Value : fallback;

        /// <summary>The sunlight mode, normalised (an unknown value reads as Mild).</summary>
        internal static string SunMode
        {
            get
            {
                string v = Value(Sunlight, SunMild);
                if (string.Equals(v, SunHarsh, StringComparison.OrdinalIgnoreCase)) return SunHarsh;
                if (string.Equals(v, SunNone, StringComparison.OrdinalIgnoreCase)) return SunNone;
                return SunMild;
            }
        }
    }
}
