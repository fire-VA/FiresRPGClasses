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
    // first, and then the lookup comes back null.
    [BepInDependency("com.Fire.VAInventory", BepInDependency.DependencyFlags.SoftDependency)]
    public class FiresRPGClassesPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.Fire.FiresRPGClasses";
        public const string PluginName = "FiresRPGClasses";
        public const string PluginVersion = "0.1.220";   // 0.1.220 ([wishbone], [classes-ui] / [generator]'s piece-create audit row 9, TOWN50; 0.9 us per piece): ONE WearNTear.Awake postfix (BoneWallLook.DressOnWake) instead of two: Sturdy Builder's load raise (CraftBatch4.Loaded patch removed, now CraftBatch4.OnWake called from it) tests the passive's value before any GetComponent<Piece>, so a player without it pays one dictionary lookup per piece. Same behaviour. No keys.   // 0.1.219 ([wishbone], [ghost] quit-freeze rollout approved by [lead], Fire's 28 s quit freeze): OnDestroy skips _harmony.UnpatchSelf() once Application.quitting has fired (Core FiresMod pattern): unpatching at quit made Harmony rebuild every shared method and re-run every other mod's transpilers while the game exits. A non-quit destroy still unpatches. No keys.   // 0.1.218 ([wishbone], Fire 11-03): recurring lines at BepInEx Debug. On live 0.1.216: 0.1.217's held blink change is not in. Previous:   // 0.1.216 ([wishbone], Fire 11-02 config list): owns [FiresRPGClasses.Progression], [GroundRings], Locational Damage and Melee aim max pitch: Core's classes bound with this mod's config + sync (needs Core 0.2.1086; an older Core keeps its own), values copied once from Core's cfg. 0.1.215: 0.1.215 ([wishbone]; Fire 10-30, plan step 1: "the crosshair can go in the RPG classes"): [Combat] Melee aims with the crosshair + [Camera] Crosshair red over an enemy are bound here (copied once from Core's cfg, Info line) and provided to Core 0.2.1036+ (CoreSettings); RPGClasses registers as the run slide's consumer. Needs Core 0.2.1036 for the hand-over (older Core: logs once, Core keeps its keys). was 0.1.214: 0.1.214 ([wishbone]; [lead] / [ghost] step 3, Fire 10-30: the Studio god clone fights as its class - its stats and passives): ClassBodies.Register / Unregister / PoolBonus for a non-player body with a class record; AttributeEffects reads such a body's own record (damage dealt, elemental taken, health / stamina / eitr regen, attack speed); PassiveKit / PassiveKit2 / PassiveKit3 keep their state per body (the local player one instance, read exactly as before); PassiveLoadout.Status / ConditionsMet and DesignKit.RankOf add record + body overloads. Debug [ClassStats] line per body. was 0.1.213: 0.1.213 ([wishbone]; [lead] / [ghost] 10-30, Fire: the Studio god clone fights as its class): ClassCaster.CastFor(Character, customData, slot or node, target) casts for a non-player body through the SAME ClassCastExecutor.PlayLocal path as the bar, record from a customData dictionary, per-body cooldowns (CooldownTracker.*For by ZDOID; the local player keeps its own clock); Info line per cast. was 0.1.212: 0.1.212 ([wishbone]; [ghost] / [visual] 10-30, the server-side Studio god clone has no local player): GodApi.StateFor(customData) / EditFor(customData, method, args) run the same god edits on a class record read from a customData dictionary (GodProfile.Shadow.m_customData) and write it back; the local bar / Paragon window paths are skipped for such a target. was 0.1.211: 0.1.211 ([wishbone]; Fire 10-30: "There are no limits ... We are a god"): GodApi writes the record directly, both ways, any value (level, xp, Valor, attributes, bonus attribute points, node ranks, Paragon ranks, bar class / slots) with no point / prerequisite / weapon checks; new AddXp(+/-), AddLevels(+/-), SetXp, SetValorXp, SetBonusAttributePoints, SetNodeRank, SetGod; a god-edited record (ClassPlayerData.God) is not reconciled at load until SetGod(false). was 0.1.210: 0.1.210 ([wishbone]; Fire 10-30 via [lead], the Studio God client: "adjust all attribute levels, skill levels, classes ... without actually needing the points"): add-only FiresRPGClasses.Combat.GodApi (string in, JSON out: State, SetLevel, SetValorLevel, SetWeapon, SetArchetype, Learn, Unlearn, SlotSkill, SetAttribute, SetParagonRank, ParagonSlot / Unslot / Respec) over the Classes window paths; God grants exactly the points a buy needs; class_* console commands call it. was 0.1.209: 0.1.209 ([wishbone]; Studio round 5: the Mage skills needed the starter tier learned first): console class_learn <nodeId> (cheat) learns one rank of any class node through the Classes window learn path (TalentRules.TryLearn). was 0.1.208: 0.1.208 ([wishbone]; Studio round 4: class_set refused Mage because the test bar had no weapon): class_slot slot 1 puts a carried weapon on the bar (BarController.TryAssignWeapon, the drag path). was 0.1.207: 0.1.207 ([wishbone]; [lead] 10-30, Studio walk-mode hotbar icons need a real class bar on the test character): console class_slot <front|back> <slot 2-8> <nodeId> (cheat) learns a class skill through the Classes window learn path (TalentRules.TryLearn) and slots it through the bar path (BarController.TrySlotSkill; slot 8 = TrySlotUltimate); Info line with the result. was 0.1.206: 0.1.206 ([wishbone]; Fire 10-30: "the class hotbars were never meant to move ... put back where it was"): 0.1.205 undone, the class bars use their bottom-right BarsOffset as before. was 0.1.205: 0.1.205 ([wishbone]; Fire 10-30 HARD RULE: every hotbar defaults bottom left above the health HUD): with the Fires UI override off the class bars start bottom left (an untouched BarsOffset = the default; a drag is kept); Info line once.

        private const string HostObjectName = "FiresRPGClasses_Host";

        internal static ManualLogSource Log;
        internal static FiresRPGClassesPlugin Instance;   // hosts rpgclasses_paragon_test's coroutine

        private Harmony _harmony;
        private static bool s_quitting;   // 0.1.219: set by Application.quitting; at quit OnDestroy skips UnpatchSelf (Core FiresMod pattern)
        private GameObject _host;

        // Keeps old config files readable after the sections were renamed and regrouped. Runs
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
                Logger.LogDebug($"config: tidied once - {removed} legacy key(s) removed, {moved} value(s) moved to the numbered sections (1 - Bars ... 9 - Targeting)");
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
            Instance = this;
            Application.quitting += () => s_quitting = true;   // 0.1.219 ([wishbone], [ghost] quit-freeze rollout): see OnDestroy
            TidyConfig();   // before any bind
            FiresRPGClassesBanner.PrintBig();
            ExtraSkills.ExtraSkillSystem.Init(Config); // Agility, Endurance, Vigor, Intellect, Hoarder (config + sync on client and server)
            // The ocean (breath, drowning, treading water, the Pearl effects) lives in FAT's VerdantsAscent.Ocean.OceanKit, which
            // activates once this assembly has no FiresRPGClasses.Ocean.OceanKit and imports the [10 - Ocean] values from this cfg
            // (left in place for it).

            // Classes turns on Core's locational hits (Tools\LOCATIONAL_HITS.md). Only a server's (or
            // single player's) call counts, so this mod goes on servers too; servers without Classes stay vanilla. Safe before
            // Core has bound its sync: Core's BindToSync recomputes with every enabler recorded so far.
            try { CoreSettingsOwned.BindCoreFeatures(Config, new Harmony(PluginGUID + ".core")); }
            catch (System.Exception e) { Log.LogWarning("[CoreSettings] progression / ground rings / locational damage settings not bound: " + e.Message); }
            FiresCore.Combat.LocationalDamage.Enable(PluginName);

            // A dedicated server needs nothing else: the rest of the mod is the local player's (bars, window, casts, passives).
            // No Harmony patch and no object at all there, so no client-only patch can reach Mono's IL rewriter headless.
            // Core's test, not batch mode: the headless test bot is a batch-mode CLIENT and keeps the whole mod.
            if (FiresCore.Lifecycle.FiresMod.IsDedicatedServer)
            {
                // The one server patch: monsters the server owns die here, and class XP needs their death broadcast.
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
                // [13 - Races] (catching / curing vampirism + lycanthropy) binds here once so its server values sync.
                try { RaceConfig.Bind(Config); }
                catch (System.Exception e) { Log.LogWarning("Races config not bound: " + e.Message); }
                // The [Synced with Server] attribute keys need their ConfigSync here too, or clients keep their local values.
                AttributeEffects.Bind(Config);
                // Paragon is client-only (ranks + slots in the local class record, SE_Paragon applied by the owner, no server
                // validation or sync), so the catalog isn't loaded here; said once so a server log shows it was decided, not missed.
                Log.LogDebug("[Paragon] catalog: not loaded on a dedicated server (Paragon picks and effects are the local player's)");
                Log.LogInfo(PluginName + " " + PluginVersion + " loaded on a dedicated server: locational hits enabled, deaths broadcast for class XP, attribute keys synced, nothing else runs here.");
                return;
            }

            ClassTreeLoader.Initialize();
            // The Paragon constellations, in their own try/catch: a bad paragon-trees.yaml must not skip the rest of set-up.
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
            DruidNecroPassives.RegisterAll();   // the Druid / Necromancer passives
            DruidNecroKit.RegisterAll();   // Regrowth, Barkskin, Bone Wall
            WerewolfConfig.Bind(Config);   // the Werewolf (Tools\WEREWOLF_CLASS_DESIGN.md): [11 - Werewolf], synced
            WerewolfKit.RegisterAll();     // Wolf Form, Howl, Alpha and the werewolf passives
            VampireConfig.Bind(Config);    // the Vampire (Tools\VAMPIRE_CLASS_DESIGN.md): [12 - Vampire], synced
            VampireKit.RegisterAll();      // Bat Form, Blood Frenzy, Mesmerise, Crimson Tide, the vampire passives, the Bat Form skill
            RaceConfig.Bind(Config);       // catching / curing vampirism + lycanthropy (Tools\VAMPIRE_CLASS_DESIGN.md §6), [13 - Races], synced
            CoreSettingsOwned.Bind(Config);   // 0.1.215: Core's melee aim / red crosshair settings live here now; RPGClasses = the run slide's consumer
            // Class / race movement styles; registers with Core's movement contract only when its key is on
            try { BarMemory.Bind(Config); } catch (System.Exception e) { Log.LogWarning("[Bars] memory keys not bound: " + e.Message); }
            try { ClassMovement.Bind(Config); ClassMovement.Register(); } catch (System.Exception e) { Log.LogWarning("[Movement] class movement not set up: " + e.Message); }
            // Core's class-weapon hook; an older Core lacks it and the weapon pick keeps Core's own fallback.
            try { ClassWeaponHook.Install(); Log.LogDebug("class weapon hook set (Core's weapon pick keeps the class bar's weapon kind)"); }
            catch (System.Exception e) { Log.LogDebug($"class weapon hook not set: this Core has none ({e.GetType().Name})"); }
            UltimateCatalog.Rebuild();
            VanillaAchievements.Register();

            ClassKeys.Bind(Config);
            Hints.ClassRavenHints.Bind(Config);
            TargetingController.Bind(Config);
            AttributeEffects.Bind(Config);
            ClassesPanel.BindConfig(Config);
            ClassesHub.BindConfig(Config);   // [6 - UI] Classes in hub
            ParagonFigures.BindConfig(Config);   // constellation figures (default off)
            ClassesHud.BindConfig(Config);
            ClassIcons.BindConfig(Config);
            InventoryGuiOnOpenSkillsPatch.BindConfig(Config);
            SkillsDialogSkin.BindConfig(Config);

            _harmony = new Harmony(PluginGUID);
            // One patch class at a time, so a bad target logs its name and the rest still attach
            // (a single throw out of PatchAll would skip everything below it in Awake).
            int patched = 0, failed = 0;
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(FiresRPGClassesPlugin).Assembly))
            {
                try { var done = _harmony.CreateClassProcessor(type).Patch(); if (done != null && done.Count > 0) patched++; }
                catch (System.Exception e) { failed++; Log.LogError($"Harmony patch class {type.FullName} not attached: {e.GetBaseException().Message}"); }
            }
            string patchLine = $"{PluginName} {PluginVersion}: {patched} Harmony patch class(es) attached, {failed} failed";
            if (failed > 0) Log.LogWarning(patchLine); else Log.LogInfo(patchLine);   // readable at the default log level

            _host = new GameObject(HostObjectName);
            _host.transform.SetParent(transform, false);
            _host.AddComponent<ClassCombatDriver>();
            if (!Application.isBatchMode)
            {
                _host.AddComponent<ClassesPanel>();
                try { ClassesHub.Register(); } catch (System.Exception e) { Log.LogWarning("[ClassesHub] " + e.Message); }
                // The hub Stats tab card. Wrapped HERE too: on an older Core the card class's own type load (its
                // StatRow field) throws before its try/catch runs, and that must not stop the rest of Awake.
                try { ClassStatsSection.Register(); } catch (System.Exception e) { Log.LogDebug("[ClassStats] card not registered (" + e.GetBaseException().GetType().Name + "): Core older than 0.2.511?"); }
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
            UI.StatusIcons.Install();   // class buff/debuff icons = the skill's tree icon (Core's IconResolver; older Core: swatches)
            Log.LogDebug($"{PluginName} {PluginVersion}: Awake finished (every set-up step ran)");   // proves no throw above cut set-up short
        }

        private void OnDestroy()
        {
            // 0.1.219: at application quit, unpatching makes Harmony rebuild every shared method (re-running every other mod's
            // transpilers) while the game exits (Fire's 28 s quit freeze); the process is ending anyway, so skip it then.
            if (!s_quitting) _harmony?.UnpatchSelf();
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
            // 0.1.201 (load/save audit M17): each step guarded on its own, so one throw never skips the rest (Forget above all)
            // nor, by escaping this postfix, the later mods' ZNet.Start postfixes.
            Guarded.Run("ClassCastNetwork.Initialize", ClassCastNetwork.Initialize);
            Guarded.Run("ClassKillNetwork.Initialize", ClassKillNetwork.Initialize);
            Guarded.Run("WerewolfServer.Initialize", WerewolfServer.Initialize);   // Wolf Form's request (a listen host answers it) and the server's answer
            Guarded.Run("WerewolfKit.ResetSession", WerewolfKit.ResetSession);
            Guarded.Run("VampireKit.ResetSession", VampireKit.ResetSession);
#if FIRES_TESTS
            Guarded.Run("ClassesUiDump.Register", ClassesUiDump.Register);
#endif
            Guarded.Run("ClassAdminCommands.Register", ClassAdminCommands.Register);
#if FIRES_TESTS
            Guarded.Run("RpgParagonTest.Register", Paragon.RpgParagonTest.Register);   // rpgclasses_paragon_test (cheat, Test worlds only)
#endif
            FiresRPGClassesPlugin.Log?.LogDebug("[ClassAdmin] class_* console commands registered");
            Guarded.Run("CooldownTracker.Clear", CooldownTracker.Clear);
            Guarded.Run("ClassPlayerStore.Forget", ClassPlayerStore.Forget);
        }
    }

    /// <summary>0.1.201 (load/save audit M17): runs one load/save-path step; a throw is logged (with the step name) and contained.</summary>
    internal static class Guarded
    {
        internal static bool Run(string what, System.Action step)
        {
            try { step(); return true; }
            catch (System.Exception e) { FiresRPGClassesPlugin.Log?.LogError($"[Classes] {what} failed: {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); return false; }
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
            // 0.1.201 (load/save audit M17): every step guarded; a reconcile that threw part-way never gets saved over the good record.
            if (!Guarded.Run("ClassPlayerStore.Forget", ClassPlayerStore.Forget)) return;
            ClassPlayerData data = null;
            if (!Guarded.Run("ClassPlayerStore.Local", () => data = ClassPlayerStore.Local) || data == null) return;
            int adjusted = 0, refunded = 0;
            bool reconciled = Guarded.Run("ReconcileWithSettings", () => adjusted = ClassPlayerStore.ReconcileWithSettings(data));
            if (reconciled && adjusted > 0)
                FiresRPGClassesPlugin.Log.LogDebug("Reconciled " + adjusted
                    + " point or passive entries against the current progression settings.");
            // The master points rule: a Master talent bought before the rule existed (or on a path that
            // no longer holds enough points) is refunded here, once, at load.
            bool revalidated = reconciled && Guarded.Run("RevalidateMasters", () => refunded = UI.TalentRules.RevalidateMasters(data, __instance));
            if (reconciled && (adjusted > 0 || (revalidated && refunded > 0))) Guarded.Run("ClassPlayerStore.SaveLocal", () => ClassPlayerStore.SaveLocal());
        }
    }
}
