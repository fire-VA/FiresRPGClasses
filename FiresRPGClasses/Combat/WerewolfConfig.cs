using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.Sync;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// [11 - Werewolf] (Tools\WEREWOLF_CLASS_DESIGN.md §3, §4, §8). Every key is synced from the server (the server decides how strong
    /// a werewolf is and whether the full moon forces the form); the tuning numbers are advanced keys. Bound once per process (the
    /// ConfigSync-once rule: 0.1.120 bound a section twice on the dedi and Core's ZNet.Awake threw "same key"), on clients and on the
    /// dedicated server alike, so the server's values are the ones that sync.
    /// </summary>
    internal static class WerewolfConfig
    {
        private const string Section = "11 - Werewolf";
        // 10-13 config rule: a fixed constant, raised only when a synced key's name/type/meaning changes.
        private const string MinimumSyncedVersion = "0.1.123";

        /// <summary>The design's default per-creature rules, in order (first match wins). See <see cref="HowlRules"/>.</summary>
        internal const string DefaultHowlRules =
            "Tamed:None, Player:None, Boss:Debuff, Elite:StaggerHalf, Dverger:Stagger, Undead:Stagger, Demon:Stagger, "
            + "PlainsMonsters:Stagger, Tough:Stagger, *:Fear";

        internal static ConfigEntry<bool> FullMoonForcesForm;
        internal static ConfigEntry<bool> NeedServerAnswer;
        internal static ConfigEntry<string> FormModel;
        internal static ConfigEntry<float> MoveSpeed, StaminaRegen, Jump, SlashResist, FireWeakness;
        internal static ConfigEntry<float> ClawBase, ClawPerLevel, ClawAttackSpeed;
        internal static ConfigEntry<float> RageHealth, RageSeconds, RageCooldown;
        internal static ConfigEntry<string> HowlRules;
        internal static ConfigEntry<float> HowlFearHealth, HowlFearSeconds, HowlTauntSeconds;

        private static bool _bound;
        private static ConfigSync _sync;

        internal static void Bind(ConfigFile config)
        {
            if (_bound) return;
            _bound = true;
            var synced = new List<ConfigEntryBase>();

            FullMoonForcesForm = Add(synced, config.Bind(Section, "Full moon forces Wolf Form", false,
                Describe("Werewolves are forced into Wolf Form at night under a full moon, until dawn (Fire 10-13: off by default, a server "
                    + "opt-in). Needs a moon-phase source; none is published yet, so this stays without effect. [Synced with Server]")));
            NeedServerAnswer = Add(synced, config.Bind(Section, "Wolf Form needs the server answer"   /* 0.1.125: no apostrophe - BepInEx refuses ' in keys and the throw skipped the rest of Awake (R-1013BQ) */, true,
                Describe("Wolf Form starts only when the server confirms the race (as Wildshape's grant). Off: a client whose server never "
                    + "answers (no FiresRPGClasses on the server) transforms anyway after 3 s. [Synced with Server]", true)));
            FormModel = Add(synced, config.Bind(Section, "Wolf Form look", "Fenring",
                Describe("The VCS form shown while transformed (VcsApi.SetForm). It must be on VCS's model allowlist. [Synced with Server]", true)));

            MoveSpeed = Add(synced, config.Bind(Section, "Wolf Form move speed", 0.20f, Range("Extra move speed in Wolf Form (0.20 = +20%). [Synced with Server]", 0f, 1f)));
            StaminaRegen = Add(synced, config.Bind(Section, "Wolf Form stamina regen", 0.25f, Range("Extra stamina regeneration in Wolf Form. [Synced with Server]", 0f, 2f)));
            Jump = Add(synced, config.Bind(Section, "Wolf Form jump", 0.20f, Range("Extra jump in Wolf Form. [Synced with Server]", 0f, 1f)));
            SlashResist = Add(synced, config.Bind(Section, "Wolf Form slash resistance", 0.15f, Range("Less slash damage taken in Wolf Form. [Synced with Server]", 0f, 0.9f)));
            FireWeakness = Add(synced, config.Bind(Section, "Wolf Form fire weakness", 0.20f, Range("More fire damage taken in Wolf Form (folklore). [Synced with Server]", 0f, 2f)));

            ClawBase = Add(synced, config.Bind(Section, "Claws slash damage", 30f, Range("Claw slash damage at class level 0, before the Unarmed skill and Claws' rank. [Synced with Server]", 0f, 500f)));
            ClawPerLevel = Add(synced, config.Bind(Section, "Claws slash per class level", 2f, Range("Claw slash damage added per class level. [Synced with Server]", 0f, 20f)));
            ClawAttackSpeed = Add(synced, config.Bind(Section, "Claws attack speed", 1.3f, Range("Unarmed attack speed in Wolf Form (1.3 = x1.3). [Synced with Server]", 1f, 3f)));

            RageHealth = Add(synced, config.Bind(Section, "Blood Rage health", 0.25f, Range("Blood Rage: below this share of max health in a fight you turn. [Synced with Server]", 0.05f, 0.9f)));
            RageSeconds = Add(synced, config.Bind(Section, "Blood Rage seconds", 15f, Range("Blood Rage: how long the forced form lasts. [Synced with Server]", 1f, 120f)));
            RageCooldown = Add(synced, config.Bind(Section, "Blood Rage cooldown", 300f, Range("Blood Rage: seconds before it can trigger again. [Synced with Server]", 10f, 3600f)));

            HowlRules = Add(synced, config.Bind(Section, "Howl creature rules", DefaultHowlRules,
                new ConfigDescription("What Howl does to each enemy within its radius: an ordered list of 'match:effect', first match wins. "
                    + "Match = a faction (AnimalsVeg, ForestMonsters, Undead, Demon, MountainMonsters, SeaMonsters, PlainsMonsters, Dverger, "
                    + "MistlandsMonsters, AshlandsMonsters...), a prefab name (Troll), or a tag: Boss, Elite (Fires Monster Overhaul tier 3+), "
                    + "Tough (max health at or above 'Howl fear health'), Tamed, Player, * (anything). Effect = Fear, Stagger, StaggerHalf, "
                    + "Debuff (ClassHowlDread: -10% damage dealt, +10% taken, 8 s) or None. [Synced with Server]")));
            HowlFearHealth = Add(synced, config.Bind(Section, "Howl fear health", 600f, Range("Howl: creatures at or above this max health count as Tough. [Synced with Server]", 1f, 100000f)));
            HowlFearSeconds = Add(synced, config.Bind(Section, "Howl fear seconds", 4f, Range("Howl: how long a frightened creature flees. [Synced with Server]", 0.5f, 30f)));
            HowlTauntSeconds = Add(synced, config.Bind(Section, "Howl hybrid taunt seconds", 6f, Range("Howl with a taunt class on the other bar: how long enemies are taunted. [Synced with Server]", 0.5f, 30f)));

            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".werewolf")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " werewolf",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    MinimumRequiredVersion = MinimumSyncedVersion,
                    ModRequired = false,
                };
                foreach (ConfigEntryBase entry in synced) AddToSync(entry);
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"werewolf config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
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

        // Tuning numbers are advanced: hidden until "Show advanced" in the configuration manager.
        private static ConfigDescription Range(string text, float min, float max) =>
            new ConfigDescription(text, new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { IsAdvanced = true });

        // Read helpers: a value before Bind (the class test can run early) is the design's default.
        internal static float Value(ConfigEntry<float> entry, float fallback) => entry != null ? entry.Value : fallback;
        internal static bool Value(ConfigEntry<bool> entry, bool fallback) => entry != null ? entry.Value : fallback;
        internal static string Value(ConfigEntry<string> entry, string fallback) => entry != null ? entry.Value : fallback;
    }
}
