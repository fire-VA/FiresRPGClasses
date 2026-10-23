using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Kills for class XP. In Valheim 1.0 Character.OnDeath does its work on the creature's OWNER only, and a server-owned
    /// monster dies on the dedicated server. So the owner sends one routed "FiresRPGClasses_Died" to everybody (the creature's
    /// prefab, max health, level, boss and summoned flags and position), and each client decides whether its own character earned
    /// it (ClassXp.OnDeath: 50 m, active in a fight in the last 30 s). This patch is the one RPGClasses installs on a dedicated
    /// server (Ascend). The server registers no handler, and ZRoutedRpc ignores a name nobody registered. Player.OnDeath
    /// overrides without base, so players never broadcast.
    /// </summary>
    public static class ClassKillNetwork
    {
        private const string RpcDied = "FiresRPGClasses_Died";
        private const int MaxRemembered = 256;

        private static ZRoutedRpc _registeredOn;
        private static readonly HashSet<ZDOID> _sent = new HashSet<ZDOID>();

        /// <summary>Clients: from ZNet.Start, as ZRoutedRpc is rebuilt every session.</summary>
        public static void Initialize()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(_registeredOn, rpc)) return;
            rpc.Register<ZPackage>(RpcDied, RPC_Died);
            _registeredOn = rpc;
            _sent.Clear();
        }

        /// <summary>A dedicated server: only the death broadcast, under its own Harmony id.</summary>
        public static void InstallServer(string harmonyId) =>
            new Harmony(harmonyId).CreateClassProcessor(typeof(CharacterOnDeathBroadcast)).Patch();

        private static void RPC_Died(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            pkg.ReadZDOID();
            string prefab = pkg.ReadString();
            float maxHealth = pkg.ReadSingle();
            int level = pkg.ReadInt();
            bool boss = pkg.ReadBool();
            bool summoned = pkg.ReadBool();
            Vector3 position = pkg.ReadVector3();
            // The tier multiplier rides at the end; an older sender stops after the position.
            float tier = pkg.GetPos() < pkg.Size() ? pkg.ReadSingle() : 1f;
            ClassXp.OnDeath(prefab, maxHealth, level, boss, summoned, position, tier);
        }

        // Fires Monster Overhaul's tiers (kill XP x1 / x1.5 / x3 / x6): its public
        // MonsterTiers.XpMultiplier(Character) when it is installed, so the table has one owner; otherwise the same synced ZDO
        // int it keeps ("fmo_tier", 0 Normal .. 3 Champion) read here. No Fires Monster Overhaul, no tier: x1.
        private static readonly int ZdoTier = "fmo_tier".GetStableHashCode();
        private static readonly float[] TierXpFallback = { 1f, 1.5f, 3f, 6f };
        private static bool _tierApiLooked;
        private static System.Reflection.MethodInfo _tierXp;

        private static float TierMultiplier(Character character, ZDO zdo)
        {
            if (!_tierApiLooked)
            {
                _tierApiLooked = true;
                var api = AccessTools.TypeByName("FiresMonsterOverhaul.Api.MonsterTiers");
                _tierXp = api != null ? AccessTools.Method(api, "XpMultiplier", new[] { typeof(Character) }) : null;
            }
            if (_tierXp != null)
            {
                try { return Mathf.Max(0f, (float)_tierXp.Invoke(null, new object[] { character })); }
                catch { _tierXp = null; }
            }
            int tier = zdo != null ? zdo.GetInt(ZdoTier, 0) : 0;
            return TierXpFallback[Mathf.Clamp(tier, 0, TierXpFallback.Length - 1)];
        }

        // The ZDO is still valid in a prefix: the owner destroys the creature at the end of OnDeath.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        internal static class CharacterOnDeathBroadcast
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.IsPlayer() || __instance.IsTamed() || ZRoutedRpc.instance == null) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) return;
                ZDOID id = view.GetZDO().m_uid;
                if (_sent.Count >= MaxRemembered) _sent.Clear();
                if (!_sent.Add(id)) return;

                Tameable tameable = __instance.GetComponent<Tameable>();
                var pkg = new ZPackage();
                pkg.Write(id);
                pkg.Write(Utils.GetPrefabName(__instance.gameObject));
                pkg.Write(__instance.GetMaxHealth());
                pkg.Write(__instance.GetLevel());
                pkg.Write(__instance.IsBoss());
                pkg.Write(tameable != null && tameable.m_unsummonDistance > 0f);
                pkg.Write(__instance.transform.position);
                pkg.Write(TierMultiplier(__instance, view.GetZDO()));
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcDied, pkg);
            }
        }
    }
}
