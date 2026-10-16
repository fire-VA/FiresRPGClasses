using System.Linq;
using BepInEx;
using BepInEx.Logging;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.Input;
using FiresRPGClasses.State;
using FiresRPGClasses.UI;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses
{
    // The player half of the class system: doc 14's two bars and targeting, doc 16's ultimates, doc 19's passive
    // grid, and the five Classes boards. Progression, abilities and luck stay in FiresUnifiedCore.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", "0.2.180")]
    [BepInDependency("com.Fire.FiresRPGmaker", BepInDependency.DependencyFlags.SoftDependency)]
    // Ordering, not a requirement. ClassesInventoryTab resolves VAInventory's registry by reflection, and
    // AccessTools.TypeByName only sees assemblies ALREADY LOADED - so without this BepInEx was free to load us
    // first (it did: FiresRPGClasses at log line 218, VAInventory at 280) and the lookup came back null.
    [BepInDependency("com.Fire.VAInventory", BepInDependency.DependencyFlags.SoftDependency)]
    public class FiresRPGClassesPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.Fire.FiresRPGClasses";
        public const string PluginName = "FiresRPGClasses";
        public const string PluginVersion = "0.1.174";

        private const string HostObjectName = "FiresRPGClasses_Host";

        internal static ManualLogSource Log;

        private Harmony _harmony;
        private GameObject _host;

        // 0.1.111 (Fire 10-13: every config accurate and easy to read; Tools\CONFIG_AUDIT_20261013.md) + 0.1.112 (sensible grouping). Runs
        // at the top of Awake, BEFORE any bind, when every value in the file is still an "orphan" BepInEx reads at bind time:
        // 1) the legacy keys nothing reads any more (the split-bool MoveBars set, the old "Skill modifier") are removed;
        // 2) each value under an old section moves to its new numbered section (same key, same value), so the bind finds it there.
        // Once: when nothing old is left it does nothing. Logged; the file is saved when anything changed.
        private void TidyConfig()
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(BepInEx.Configuration.ConfigFile), "OrphanedEntries")?.GetValue(Config)
                    as System.Collections.Generic.Dictionary<BepInEx.Configuration.ConfigDefinition, string>;
                if (orphans == null) return;
                int removed = 0, moved = 0;
                foreach (var def in new[]
                {
                    new BepInEx.Configuration.ConfigDefinition("HUD", "MoveBars"),
                    new BepInEx.Configuration.ConfigDefinition("HUD", "MoveBars_Ctrl"),
                    new BepInEx.Configuration.ConfigDefinition("HUD", "MoveBars_Shift"),
                    new BepInEx.Configuration.ConfigDefinition("HUD", "MoveBars_Alt"),
                    new BepInEx.Configuration.ConfigDefinition("Bars", "Skill modifier"),
                })
                    if (orphans.Remove(def)) removed++;

                foreach (var old in new System.Collections.Generic.List<BepInEx.Configuration.ConfigDefinition>(orphans.Keys))
                {
                    string to = NewSection(old.Section, old.Key);
                    if (to == null) continue;
                    var target = new BepInEx.Configuration.ConfigDefinition(to, old.Key);
                    if (!orphans.ContainsKey(target)) { orphans[target] = orphans[old]; moved++; }
                    orphans.Remove(old);
                }
                if (removed == 0 && moved == 0) return;
                Config.Save();
                Logger.LogInfo($"config: tidied once - {removed} legacy key(s) removed, {moved} value(s) moved to the numbered sections (1 - Bars ... 9 - Targeting)");
            }
            catch (System.Exception e) { Logger.LogWarning("config: tidy-up skipped: " + e.Message); }
        }

        // Old section (+ key) -> new section; null = not ours to move.
        private static string NewSection(string section, string key)
        {
            bool slotKey = key.StartsWith("Slot", System.StringComparison.Ordinal) && key.Length > 4 && char.IsDigit(key[4]);
            switch (section)
            {
                case "Bars": return slotKey || key.StartsWith("BarSwap", System.StringComparison.Ordinal) ? Input.ClassKeys.BarKeysSection : Input.ClassKeys.BarsSection;
                case "Item hotbar": return key.StartsWith("Item", System.StringComparison.Ordinal) && key.Length > 4 && char.IsDigit(key[4]) ? Input.ClassKeys.ItemHotbarKeysSection : Input.ClassKeys.ItemHotbarSection;
                case "HUD": return "5 - HUD";
                case "UI": case "Hints": return "6 - UI";
                case "Attributes": return "7 - Attributes";
                case "ExtraSkills": return "8 - Extra skills";
                case "Targeting": return "9 - Targeting";
                default: return null;
            }
        }
        private void Awake()
        {
            Log = Logger;
            TidyConfig();   // 0.1.111/0.1.112 (Fire 10-13 config audit): before any bind
            FiresRPGClassesBanner.PrintBig();
            ExtraSkills.ExtraSkillSystem.Init(Config); // Agility, Endurance, Vigor, Intellect, Hoarder (config + sync on client and server)
            // 0.1.122 (Fire 10-13, Tools\OCEAN_TO_FAT_PLAN.md stage 2): the ocean (breath, drowning, treading water, the Pearl effects) moved to
            // FAT 0.2.890's VerdantsAscent.Ocean.OceanKit, which activates once this assembly no longer has FiresRPGClasses.Ocean.OceanKit and
            // imports the [10 - Ocean] values from this cfg (left in place for it).

            // Classes turns on Core's locational hits (Fire, 2026-09-28 19:41; Tools\LOCATIONAL_HITS.md). Only a server's (or
            // single player's) call counts, so this mod goes on servers too; servers without Classes stay vanilla. Safe before
            // Core has bound its sync: Core's BindToSync recomputes with every enabler recorded so far.
            FiresCore.Combat.LocationalDamage.Enable(PluginName);

            // A dedicated server needs nothing else: the rest of the mod is the local player's (bars, window, casts, passives).
            // No Harmony patch and no object at all there, so no client-only patch can reach Mono's IL rewriter headless.
            // Core's test, not batch mode: the headless test bot is a batch-mode CLIENT and keeps the whole mod.
            if (FiresCore.Lifecycle.FiresMod.IsDedicatedServer)
            {
                // The one server patch (0.1.55): monsters the server owns die here, and class XP needs their death broadcast.
                // Character.OnDeath is in the server build too.
                try { ClassKillNetwork.InstallServer(PluginGUID + ".deaths"); }
                catch (System.Exception e) { Log.LogWarning("class XP death broadcast not installed: " + e.Message); }
                // Wildshape's and Bind the Dead's grants are checked here (Core runs the validator on the server).
                try { PossessionKit.RegisterValidators(); }
                catch (System.Exception e) { Log.LogWarning("Druid / Necromancer possession checks not registered: " + e.Message); }
                // Wolf Form's grant is checked here too (Wildshape's pattern, WerewolfServer): its request handler and the synced
                // [11 - Werewolf] keys, whose server values are the ones clients get.
                try { WerewolfConfig.Bind(Config); WerewolfServer.InstallServer(PluginGUID + ".werewolf"); }
                catch (System.Exception e) { Log.LogWarning("Werewolf server check not installed: " + e.Message); }
                // Bat Form's grant (a Core possession, Wildshape's path) is checked here by its PossessionApi validator; the [12 - Vampire]
                // keys bind here once so their server values sync; the Bat Form skill is registered on both peers (FiresSkillRegistry).
                try { VampireConfig.Bind(Config); VampireServer.RegisterValidators(); VampireSkill.Register(false); }
                catch (System.Exception e) { Log.LogWarning("Vampire server check not installed: " + e.Message); }
                // 0.1.133: [13 - Races] (catching / curing vampirism + lycanthropy) binds here once so its server values sync.
                try { RaceConfig.Bind(Config); }
                catch (System.Exception e) { Log.LogWarning("Races config not bound: " + e.Message); }
                // 0.1.78: the [Synced with Server] attribute keys need their ConfigSync here too, or clients keep their local values.
                AttributeEffects.Bind(Config);
                // 0.1.134: Paragon is client-only (ranks + slots in the local class record, SE_Paragon applied by the owner, no server
                // validation or sync), so the catalog isn't loaded here; said once so a server log shows it was decided, not missed.
                Log.LogDebug("[Paragon] catalog: not loaded on a dedicated server (Paragon picks and effects are the local player's)");
                Log.LogInfo(PluginName + " " + PluginVersion + " loaded on a dedicated server: locational hits enabled, deaths broadcast for class XP, attribute keys synced, nothing else runs here.");
                return;
            }

            ClassTreeLoader.Initialize();
            // 0.1.134 (R2: the Paragon tab was empty): 0.1.133 never loaded the constellations, so ParagonUi had 0 trees. Its own
            // try/catch: a bad paragon-trees.yaml must not skip the rest of set-up.
            try { Paragon.ParagonCatalog.Initialize(); Log.LogDebug($"[Paragon] catalog: {Paragon.ParagonCatalog.Trees.Count} tree(s), loaded from {Paragon.ParagonCatalog.LoadedFrom}"); }
            catch (System.Exception e) { Log.LogWarning("[Paragon] catalog not loaded: " + e); }
            CoreSkillKit.RegisterAll();
            EvasionController.RegisterAll();
            DesignKit.RegisterAll();
            BuffKit.RegisterAll();
            ActiveKit.RegisterAll();
            MoveKit.RegisterAll();
            LinkKit.RegisterAll();
            PassiveKit2.RegisterAll();
            PassiveKit3.RegisterAll();
            ZoneKit.RegisterAll();
            CcKit.RegisterAll();
            PossessionKit.RegisterAll();
            SummonKit.RegisterAll();
            DruidNecroPassives.RegisterAll();   // 0.1.79: the Druid / Necromancer passives (Fire 10-08)
            DruidNecroKit.RegisterAll();   // 0.1.80: Regrowth, Barkskin, Bone Wall
            WerewolfConfig.Bind(Config);   // the Werewolf (Tools\WEREWOLF_CLASS_DESIGN.md): [11 - Werewolf], synced
            WerewolfKit.RegisterAll();     // Wolf Form, Howl, Alpha and the werewolf passives
            VampireConfig.Bind(Config);    // the Vampire (Tools\VAMPIRE_CLASS_DESIGN.md): [12 - Vampire], synced
            VampireKit.RegisterAll();      // Bat Form, Blood Frenzy, Mesmerise, Crimson Tide, the vampire passives, the Bat Form skill
            RaceConfig.Bind(Config);       // 0.1.133: catching / curing vampirism + lycanthropy (Tools\VAMPIRE_CLASS_DESIGN.md §6), [13 - Races], synced
            // 0.1.141 ([wishbone], Fire 10-14): class / race movement styles; registers with Core's movement contract only when its key is on
            try { BarMemory.Bind(Config); } catch (System.Exception e) { Log.LogWarning("[Bars] memory keys not bound: " + e.Message); }
            try { ClassMovement.Bind(Config); ClassMovement.Register(); } catch (System.Exception e) { Log.LogWarning("[Movement] class movement not set up: " + e.Message); }
            // Core 0.2.196's class-weapon hook; an older Core lacks it and the weapon pick keeps Core's own fallback.
            try { ClassWeaponHook.Install(); Log.LogInfo("class weapon hook set (Core's weapon pick keeps the class bar's weapon kind)"); }
            catch (System.Exception e) { Log.LogInfo($"class weapon hook not set: this Core has none ({e.GetType().Name})"); }
            UltimateCatalog.Rebuild();
            VanillaAchievements.Register();

            ClassKeys.Bind(Config);
            Hints.ClassRavenHints.Bind(Config);
            TargetingController.Bind(Config);
            AttributeEffects.Bind(Config);
            ClassesPanel.BindConfig(Config);
            ClassesHub.BindConfig(Config);   // UI overhaul 2a: [UI] Classes in hub (default off)
            ParagonFigures.BindConfig(Config);   // [classes-ui] constellation figures (default off)
            ClassesHud.BindConfig(Config);
            ClassIcons.BindConfig(Config);
            InventoryGuiOnOpenSkillsPatch.BindConfig(Config);
            SkillsDialogSkin.BindConfig(Config);   // UI overhaul #19 ([classes-ui])

            _harmony = new Harmony(PluginGUID);
            // 0.1.145 ([lead] R-PROOF2): one patch class at a time, so a bad target logs its name and the rest still attach
            // (0.1.141's SlowFall threw out of PatchAll and skipped everything below it in Awake).
            int patched = 0, failed = 0;
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(FiresRPGClassesPlugin).Assembly))
            {
                try { var done = _harmony.CreateClassProcessor(type).Patch(); if (done != null && done.Count > 0) patched++; }
                catch (System.Exception e) { failed++; Log.LogError($"Harmony patch class {type.FullName} not attached: {e.GetBaseException().Message}"); }
            }
            string patchLine = $"{PluginName} {PluginVersion}: {patched} Harmony patch class(es) attached, {failed} failed";
            if (failed > 0) Log.LogWarning(patchLine); else Log.LogInfo(patchLine);   // 0.1.146 ([seasons]): readable at the default level

            _host = new GameObject(HostObjectName);
            _host.transform.SetParent(transform, false);
            _host.AddComponent<ClassCombatDriver>();
            if (!Application.isBatchMode)
            {
                _host.AddComponent<ClassesPanel>();
                try { ClassesHub.Register(); } catch (System.Exception e) { Log.LogWarning("[ClassesHub] " + e.Message); }
                // 0.1.159: the hub Stats tab card (Core 0.2.511+). Wrapped HERE too: on an older Core the card class's own type load (its
                // StatRow field) throws before its try/catch runs, and that must not stop the rest of Awake (the 0.1.141 SlowFall lesson).
                try { ClassStatsSection.Register(); } catch (System.Exception e) { Log.LogInfo("[ClassStats] card not registered (" + e.GetBaseException().GetType().Name + "): Core older than 0.2.511?"); }
                _host.AddComponent<ClassesHud>();
                _host.AddComponent<ClassesPossessionHud>();
                // First of two attempts. VAInventory replays registrations when the inventory next opens, so
                // this does not wait on InventoryGui - but it DOES depend on VAInventory's assembly already
                // being loaded, which is why the soft dependency above exists and why ClassesPanel.Start
                // retries. A no-op without VAInventory.
                ClassesInventoryTab.Register();
            }

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded. Trees from " + ClassTrees.LoadedFrom
                + "; " + ClassTrees.TotalPointCostOfEveryTree() + " points fill every tree. Hotbar hand-offs: "
                + string.Join(", ", ClassKeys.DeclaredHandOffs().ToArray()));
            UI.StatusIcons.Install();   // class buff/debuff icons = the skill's tree icon (Core 0.2.404's IconResolver; older Core: swatches)
            Log.LogDebug($"{PluginName} {PluginVersion}: Awake finished (every set-up step ran)");   // 0.1.125: a throw above can't hide again (R-1013BQ)
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            ClassTreeLoader.Shutdown();
            if (_host != null) Destroy(_host);
        }
    }

    /// <summary>ZNet rebuilds ZRoutedRpc every session, so the cast RPC registers from ZNet.Start.</summary>
    [HarmonyPatch(typeof(ZNet), "Start")]
    internal static class ZNetStartRegisterCastRpc
    {
        [HarmonyPostfix]
        private static void RegisterCastRpc()
        {
            ClassCastNetwork.Initialize();
            ClassKillNetwork.Initialize();
            WerewolfServer.Initialize();   // Wolf Form's request (a listen host answers it) and the server's answer
            WerewolfKit.ResetSession();
            VampireKit.ResetSession();
            ClassesUiDump.Register();
            ClassAdminCommands.Register();
            FiresRPGClassesPlugin.Log?.LogDebug("[ClassAdmin] class_* console commands registered");
            CooldownTracker.Clear();
            ClassPlayerStore.Forget();
        }
    }

    /// <summary>A fresh Player object means a fresh record, reconciled against the current server rates.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerOnSpawnedLoadClassRecord
    {
        [HarmonyPostfix]
        private static void LoadRecord(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            ClassPlayerStore.Forget();
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            int adjusted = ClassPlayerStore.ReconcileWithSettings(data);
            if (adjusted > 0)
                FiresRPGClassesPlugin.Log.LogInfo("Reconciled " + adjusted
                    + " point or passive entries against the current progression settings.");
            // Fire 10-01, "enforce the master points rule": a Master talent bought before the rule existed (or on a path that
            // no longer holds enough points) is refunded here, once, at load.
            int refunded = UI.TalentRules.RevalidateMasters(data, __instance);
            if (adjusted > 0 || refunded > 0) ClassPlayerStore.SaveLocal();
        }
    }
}
