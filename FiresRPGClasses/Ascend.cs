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
        public const string PluginVersion = "0.1.88";

        private const string HostObjectName = "FiresRPGClasses_Host";

        internal static ManualLogSource Log;

        private Harmony _harmony;
        private GameObject _host;

        private void Awake()
        {
            Log = Logger;
            FiresRPGClassesBanner.PrintBig();

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
                // 0.1.78: the [Synced with Server] attribute keys need their ConfigSync here too, or clients keep their local values.
                AttributeEffects.Bind(Config);
                Log.LogInfo(PluginName + " " + PluginVersion + " loaded on a dedicated server: locational hits enabled, deaths broadcast for class XP, attribute keys synced, nothing else runs here.");
                return;
            }

            ClassTreeLoader.Initialize();
            CoreSkillKit.RegisterAll();
            EvasionController.RegisterAll();
            DesignKit.RegisterAll();
            ZoneKit.RegisterAll();
            CcKit.RegisterAll();
            PossessionKit.RegisterAll();
            SummonKit.RegisterAll();
            DruidNecroPassives.RegisterAll();   // 0.1.79: the Druid / Necromancer passives (Fire 10-08)
            DruidNecroKit.RegisterAll();   // 0.1.80: Regrowth, Barkskin, Bone Wall
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
            ClassesHud.BindConfig(Config);
            ClassIcons.BindConfig(Config);
            InventoryGuiOnOpenSkillsPatch.BindConfig(Config);

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(FiresRPGClassesPlugin).Assembly);

            _host = new GameObject(HostObjectName);
            _host.transform.SetParent(transform, false);
            _host.AddComponent<ClassCombatDriver>();
            if (!Application.isBatchMode)
            {
                _host.AddComponent<ClassesPanel>();
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
            ClassesUiDump.Register();
            ClassAdminCommands.Register();
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
