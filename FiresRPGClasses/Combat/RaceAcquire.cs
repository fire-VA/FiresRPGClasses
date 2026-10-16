using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresCore.Sync;
using FiresRPGClasses.Data;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// [13 - Races] Catching and curing vampirism / lycanthropy during play (Tools\VAMPIRE_CLASS_DESIGN.md §6, §9, §10;
    /// WEREWOLF_CLASS_DESIGN.md §9; Fire 10-13, LOCKED). Every key is synced from the server; bound ONCE per process, on the client
    /// and the dedicated server alike (the ConfigSync-once rule).
    /// </summary>
    internal static class RaceConfig
    {
        private const string Section = "13 - Races";
        private const string MinimumSyncedVersion = "0.1.133";
        internal const string CurePotions = "Potions", CureShrine = "Shrine", CureBoth = "Both";

        internal static ConfigEntry<bool> Lycanthropy, Vampirism, CreationOnly;
        internal static ConfigEntry<float> InfectionChance, DaysToTurn;
        internal static ConfigEntry<string> CureMode, LycanSources, VampSources;

        private static bool _bound;
        private static ConfigSync _sync;

        internal static void Bind(ConfigFile config)
        {
            if (_bound) return;
            _bound = true;
            var synced = new List<ConfigEntryBase>();
            Lycanthropy = Add(synced, config.Bind(Section, "Lycanthropy", true, "Players can catch lycanthropy (Moon-touched) and turn into werewolves during play. [Synced with Server]"));
            Vampirism = Add(synced, config.Bind(Section, "Vampirism", true, "Players can catch vampirism (Tainted Blood) and turn into vampires during play. [Synced with Server]"));
            CreationOnly = Add(synced, config.Bind(Section, "Creation only", false, "On: the races come only from character creation; no infection, no turning potions. Cures still work. [Synced with Server]"));
            InfectionChance = Add(synced, config.Bind(Section, "Infection chance per hit", 0.03f, new ConfigDescription(
                "The chance a hit from an infecting creature gives the disease (0.03 = 3%, Fire 10-13). [Synced with Server]", new AcceptableValueRange<float>(0f, 1f))));
            DaysToTurn = Add(synced, config.Bind(Section, "Days to turn", 3f, new ConfigDescription(
                "In-game days with the disease, uncured, before the player turns (Fire 10-13). [Synced with Server]", new AcceptableValueRange<float>(0.1f, 30f))));
            CureMode = Add(synced, config.Bind(Section, "Cure after turning", CurePotions, new ConfigDescription(
                "How a turned player is cured: the Purifying Draught, a shrine, or both. Fire 10-14: potions now, the shrine later (not built yet; Both works as Potions until it is). [Synced with Server]",
                new AcceptableValueList<string>(CurePotions, CureShrine, CureBoth))));
            LycanSources = Add(synced, config.Bind(Section, "Lycanthropy carriers", "Fenring,Fenring_Cultist,Fenring_Cultist_Hildir", "Creature prefabs whose hits can pass on lycanthropy (comma separated). [Synced with Server]"));
            VampSources = Add(synced, config.Bind(Section, "Vampirism carriers", "Bat", "Creature prefabs whose hits can pass on vampirism (comma separated). [Synced with Server]"));
            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".races")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " races",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    MinimumRequiredVersion = MinimumSyncedVersion,
                    ModRequired = false,
                };
                foreach (ConfigEntryBase entry in synced)
                    switch (entry)
                    {
                        case ConfigEntry<bool> b: _sync.AddConfigEntry(b); break;
                        case ConfigEntry<float> f: _sync.AddConfigEntry(f); break;
                        case ConfigEntry<string> s: _sync.AddConfigEntry(s); break;
                    }
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"races config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
        }

        private static ConfigEntry<T> Add<T>(List<ConfigEntryBase> list, ConfigEntry<T> e) { list.Add(e); return e; }

        internal static bool On(string race) =>
            race == RaceAcquire.Werewolf ? (Lycanthropy?.Value ?? true) : race == RaceAcquire.Vampire ? (Vampirism?.Value ?? true) : false;

        internal static bool IsCarrier(string race, string prefab)
        {
            string list = race == RaceAcquire.Werewolf ? LycanSources?.Value : VampSources?.Value;
            if (string.IsNullOrEmpty(list) || string.IsNullOrEmpty(prefab)) return false;
            foreach (string s in list.Split(','))
                if (string.Equals(s.Trim(), prefab, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>
    /// The disease, the turning and the cure, for the LOCAL player (inventories, customData and the look are owner-side):
    /// - a hit from a carrier (Fenring / Cultist: lycanthropy; Bat: vampirism) rolls the infection chance; the disease is the
    ///   player customData "rpg_disease" = "&lt;race&gt;|&lt;start time s&gt;" (it survives relog) and the countdown status
    ///   RPG_MoonTouched / RPG_TaintedBlood;
    /// - after DaysToTurn in-game days uncured: VcsApi.SetRace(race). VCS publishes rpg_race and the class unlocks. A refusal (VCS model
    ///   swaps off, Fenring not allowlisted, VCS missing) keeps the disease and retries every login;
    /// - Blood Chalice / Wolfsbane Brew: turn at once (opt-in); Cleansing Mead: cures the disease; Purifying Draught (Moder trophy for
    ///   vampirism, Bonemass trophy for lycanthropy, + Silver): cures a turned player, VcsApi.SetRace("Human"); the class and its points
    ///   stay, dormant (its race requirement fails) until the race comes back.
    /// Never a second race: a werewolf can't catch vampirism and the reverse; a diseased player can't catch the other disease.
    /// </summary>
    internal static class RaceAcquire
    {
        internal const string Werewolf = "Werewolf", Vampire = "Vampire", Human = "Human";
        internal const string DiseaseKey = "rpg_disease";
        internal const string MoonTouched = "RPG_MoonTouched", TaintedBlood = "RPG_TaintedBlood";

        private static float _nextTick;
        private static bool _announcedRetry;

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Races] " + s);

        internal static string SeFor(string race) => race == Werewolf ? MoonTouched : TaintedBlood;
        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0d;
        private static double DayLength => EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0 ? EnvMan.instance.m_dayLengthSec : 1800d;

        /// <summary>The local player's current race from its ZDO (rpg_race), "" = human.</summary>
        internal static string CurrentRace(Player p)
        {
            ZDO zdo = p != null && p.m_nview != null && p.m_nview.IsValid() ? p.m_nview.GetZDO() : null;
            return zdo != null ? zdo.GetString(WerewolfRace.RaceKey, "") : "";
        }

        internal static bool TryGetDisease(Player p, out string race, out double start)
        {
            race = null; start = 0;
            if (p == null || !p.m_customData.TryGetValue(DiseaseKey, out string v) || string.IsNullOrEmpty(v)) return false;
            int bar = v.IndexOf('|');
            if (bar <= 0) return false;
            race = v.Substring(0, bar);
            return double.TryParse(v.Substring(bar + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out start);
        }

        /// <summary>Gives the local player the disease (no roll). Returns why not, or null.</summary>
        internal static string Infect(Player p, string race, string source)
        {
            if (p == null || p != Player.m_localPlayer) return "not the local player";
            if (!RaceConfig.On(race)) return race + " is switched off on this server";
            if (RaceConfig.CreationOnly?.Value == true) return "creation-only server";
            if (!string.IsNullOrEmpty(CurrentRace(p))) return "already " + CurrentRace(p);
            if (TryGetDisease(p, out string has, out _)) return "already carrying " + SeFor(has);
            p.m_customData[DiseaseKey] = race + "|" + Now.ToString("0.0", CultureInfo.InvariantCulture);
            ShowDisease(p);
            p.Message(MessageHud.MessageType.Center, race == Werewolf ? "Something feral stirs in your blood..." : "A cold thirst creeps into your veins...");
            Log($"{p.GetPlayerName()} caught {SeFor(race)} from {source}; turns in {RaceConfig.DaysToTurn?.Value ?? 3f:0.#} day(s) unless cured");
            return null;
        }

        /// <summary>The countdown status, (re)applied with the time that's left.</summary>
        private static void ShowDisease(Player p)
        {
            if (!TryGetDisease(p, out string race, out double start)) return;
            double left = start + (RaceConfig.DaysToTurn?.Value ?? 3f) * DayLength - Now;
            if (left < 1) left = 1;
            AbilityRPCManager.ApplySelfBuff(p, SeFor(race), (float)left);
        }

        internal static void ClearDisease(Player p, string why)
        {
            if (p == null || !TryGetDisease(p, out string race, out _)) return;
            p.m_customData.Remove(DiseaseKey);
            p.GetSEMan()?.RemoveStatusEffect(SeFor(race).GetStableHashCode(), true);
            Log($"{p.GetPlayerName()}: {SeFor(race)} cleared ({why})");
        }

        /// <summary>Turns the local player now. False (and the disease kept) when VCS refused.</summary>
        internal static bool Turn(Player p, string race, string why)
        {
            if (p == null) return false;
            bool ok = VcsBridge.SetRace(p, race);
            if (!ok)
            {
                if (!_announcedRetry) { _announcedRetry = true; p.Message(MessageHud.MessageType.TopLeft, "The change won't take hold yet (the server doesn't allow that look)."); }
                Log($"turning {p.GetPlayerName()} into a {race} ({why}): VCS refused or is missing (SetRace {(VcsBridge.RaceAvailable ? "found" : "missing")}); the disease stays, retried at the next login");
                return false;
            }
            p.m_customData.Remove(DiseaseKey);
            p.GetSEMan()?.RemoveStatusEffect(SeFor(race).GetStableHashCode(), true);
            p.Message(MessageHud.MessageType.Center, "The blood has taken you");
            Log($"{p.GetPlayerName()} turned into a {race} ({why}); rpg_race published by VCS, the {race} class unlocks");
            return true;
        }

        /// <summary>Cures a turned player (back to Human; the class stays, dormant).</summary>
        internal static bool CureTurned(Player p, string race)
        {
            if (p == null || CurrentRace(p) != race) return false;
            bool ok = VcsBridge.SetRace(p, Human);
            Log($"{p.GetPlayerName()} cured of {race}: SetRace(Human) {(ok ? "done; the class keeps its points, dormant" : "REFUSED")}");
            if (ok) p.Message(MessageHud.MessageType.Center, "The curse lifts. You are yourself again.");
            return ok;
        }

        /// <summary>Player.Update (local): restores the countdown after a login and turns the player when the time is up.</summary>
        internal static void Tick(Player p)
        {
            if (p == null || p != Player.m_localPlayer || Time.time < _nextTick || ZNet.instance == null) return;
            _nextTick = Time.time + 2f;
            if (!TryGetDisease(p, out string race, out double start)) return;
            if (!string.IsNullOrEmpty(CurrentRace(p))) { ClearDisease(p, "already " + CurrentRace(p)); return; }
            if (!RaceConfig.On(race)) { ClearDisease(p, race + " switched off on this server"); return; }
            double due = start + (RaceConfig.DaysToTurn?.Value ?? 3f) * DayLength;
            if (Now >= due) { if (!Turn(p, race, "the disease ran its course")) _nextTick = Time.time + 600f; return; }
            if (p.GetSEMan()?.GetStatusEffect(SeFor(race).GetStableHashCode()) == null) ShowDisease(p);
        }

        /// <summary>A hit landed on the local player: a carrier's hit rolls the infection.</summary>
        internal static void OnHit(Player p, HitData hit)
        {
            if (p == null || p != Player.m_localPlayer || hit == null || hit.GetTotalDamage() <= 0f) return;
            Character attacker = hit.GetAttacker();
            if (attacker == null || attacker is Player) return;
            string prefab = Utils.GetPrefabName(attacker.gameObject);
            foreach (string race in new[] { Werewolf, Vampire })
            {
                if (!RaceConfig.IsCarrier(race, prefab)) continue;
                float chance = RaceConfig.InfectionChance?.Value ?? 0.03f;
                float roll = UnityEngine.Random.value;
                if (roll >= chance) return;
                string why = Infect(p, race, prefab);
                if (why != null) Log($"a {prefab} hit rolled {roll:0.000} < {chance:0.###} for {race}, but no disease: {why}");
                return;
            }
        }
    }

    // The two diseases: readable names + tooltips; the countdown is the effect's own timer.
    public class MoonTouchedEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Moon-touched";
        public override string Description => "Lycanthropy runs in your blood. Unless a Cleansing Mead cures it, you will become a werewolf when the timer ends.";
    }

    public class TaintedBloodEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Tainted Blood";
        public override string Description => "Vampirism runs in your blood. Unless a Cleansing Mead cures it, you will become a vampire when the timer ends.";
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class RaceTickPatch
    {
        [HarmonyPostfix] private static void After(Player __instance) => RaceAcquire.Tick(__instance);
    }

    // Owner side, so a hit from a monster owned by another peer counts too.
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class RaceInfectionPatch
    {
        [HarmonyPostfix]
        private static void After(Character __instance, HitData hit)
        {
            if (__instance is Player p && p == Player.m_localPlayer)
                try { RaceAcquire.OnHit(p, hit); } catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Races] infection hook: " + e.Message); }
        }
    }
}
