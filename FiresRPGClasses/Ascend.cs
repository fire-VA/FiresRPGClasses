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
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.Fire.FiresRPGmaker", BepInDependency.DependencyFlags.SoftDependency)]
    public class FiresRPGClassesPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.Fire.FiresRPGClasses";
        public const string PluginName = "FiresRPGClasses";
        public const string PluginVersion = "0.1.19";

        private const string HostObjectName = "FiresRPGClasses_Host";

        internal static ManualLogSource Log;

        private Harmony _harmony;
        private GameObject _host;

        private void Awake()
        {
            Log = Logger;
            FiresRPGClassesBanner.PrintBig();

            ClassTreeLoader.Initialize();
            UltimateCatalog.Rebuild();
            VanillaAchievements.Register();

            ClassKeys.Bind(Config);
            TargetingController.Bind(Config);
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
                // Safe this early: VAInventory replays registrations the next time the inventory opens, so
                // this waits on neither InventoryGui nor load order. A no-op without VAInventory.
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
            ClassesUiDump.Register();
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
            if (adjusted <= 0) return;
            FiresRPGClassesPlugin.Log.LogInfo("Reconciled " + adjusted
                + " point or passive entries against the current progression settings.");
            ClassPlayerStore.SaveLocal();
        }
    }
}
