using System;
using System.Collections.Generic;
using FiresRPGClasses.Data;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The server's half of Wolf Form (Tools\WEREWOLF_CLASS_DESIGN.md §3 "Why not Wildshape": Wildshape's validator pattern is reused
    /// for "may this player be in wolf form"). Wildshape asks Core's PossessionApi, whose validator runs on the server; Wolf Form is no
    /// possession, so it asks here with its own routed RPCs:
    ///   client -> server  FiresRPGClasses_WolfRequest (int reason, float seconds)
    ///   server -> client  FiresRPGClasses_WolfAnswer  (int reason, bool granted, float seconds, string why)
    /// The server reads the requester's player ZDO (the race, <see cref="WerewolfRace.Has"/>), refuses the full-moon reason unless its
    /// config opts in, caps the seconds, and refuses a repeat inside the trigger's cooldown. Like Wildshape's check it cannot see the
    /// talent ranks (they live in the client's own record), so it allows the skill's base numbers.
    /// Registered on every ZNet start: clients and listen hosts from Ascend's ZNet.Start patch, a dedicated server from its own patch
    /// installed by <see cref="InstallServer"/> (the dedicated server otherwise patches nothing but the death broadcast).
    /// </summary>
    public static class WerewolfServer
    {
        internal const string RpcRequest = "FiresRPGClasses_WolfRequest";
        internal const string RpcAnswer = "FiresRPGClasses_WolfAnswer";

        internal enum Reason { Skill = 0, Rage = 1, Moon = 2 }

        // What the server allows at most, per trigger (the skill's design numbers; Blood Rage's and the moon's from config).
        // x1.6 = the soft max of Paragon's Wild Form ("+{value}% Bat and Wolf Form duration"). The server never sees a
        // client's Paragon stars (they are local), so it allows the most a star can add; the client asks for its own real length.
        private const float SkillSecondsCap = 30f * 1.6f;
        private const float SkillCooldown = 120f;
        private const float MoonSecondsCap = 1200f;   // a forced night form; the client ends it at dawn
        private const float MoonCooldown = 60f;
        // The client starts its cooldown when it asks; the server's clock may lag a little.
        private const float CooldownSlack = 5f;

        private static ZRoutedRpc _registeredOn;
        private static readonly Dictionary<long, float> LastGrant = new Dictionary<long, float>();

        /// <summary>Called by the client when the server answers (WerewolfKit).</summary>
        internal static event Action<Reason, bool, float, string> Answered;

        /// <summary>A dedicated server: a ZNet.Start postfix that registers the request handler, under its own Harmony id.</summary>
        public static void InstallServer(string harmonyId)
        {
            new Harmony(harmonyId).Patch(AccessTools.Method(typeof(ZNet), "Start"),
                postfix: new HarmonyMethod(typeof(WerewolfServer), nameof(AfterZNetStart)));
        }

        private static void AfterZNetStart() => Initialize();

        /// <summary>Registers the handlers on this session's ZRoutedRpc (it is rebuilt every session).</summary>
        public static void Initialize()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(_registeredOn, rpc)) return;
            _registeredOn = rpc;
            LastGrant.Clear();
            if (ZNet.instance != null && ZNet.instance.IsServer()) rpc.Register<int, float>(RpcRequest, RPC_Request);
            if (!FiresCore.Lifecycle.FiresMod.IsDedicatedServer) rpc.Register<int, bool, float, string>(RpcAnswer, RPC_Answer);
        }

        // ── server ──────────────────────────────────────────────────────────────────────────────────────────────────

        private static void RPC_Request(long sender, int reasonValue, float seconds)
        {
            Reason reason = (Reason)Mathf.Clamp(reasonValue, 0, 2);
            string why = Validate(sender, reason, ref seconds);
            bool granted = why == null;
            if (granted) LastGrant[Key(sender, reason)] = Time.time;
            FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] server: Wolf Form request from peer {sender} ({reason}, {seconds:0} s): "
                + (granted ? "granted" : "refused, " + why));
            ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcAnswer, reasonValue, granted, seconds, why ?? "");
        }

        private static string Validate(long sender, Reason reason, ref float seconds)
        {
            ZDO zdo = PlayerZdo(sender);
            if (zdo == null) return "the server can't see your character";
            if (!WerewolfRace.Has(zdo, out string raceWhy)) return "not of the werewolf race (" + raceWhy + ")";

            float cap, cooldown;
            switch (reason)
            {
                case Reason.Rage:
                    cap = WerewolfConfig.Value(WerewolfConfig.RageSeconds, 15f);
                    cooldown = WerewolfConfig.Value(WerewolfConfig.RageCooldown, 300f);
                    break;
                case Reason.Moon:
                    if (!WerewolfConfig.Value(WerewolfConfig.FullMoonForcesForm, false)) return "this server doesn't force the form at full moon";
                    cap = MoonSecondsCap;
                    cooldown = MoonCooldown;
                    break;
                default:
                    cap = SkillSecondsCap;
                    cooldown = SkillCooldown;
                    break;
            }
            if (LastGrant.TryGetValue(Key(sender, reason), out float last) && Time.time < last + cooldown - CooldownSlack)
                return $"again in {last + cooldown - CooldownSlack - Time.time:0} s";
            seconds = Mathf.Clamp(seconds, 1f, cap);
            return null;
        }

        private static long Key(long sender, Reason reason) => sender * 4 + (int)reason;

        // The requester's player ZDO: its peer's character on a server, the local player when the host asks itself.
        private static ZDO PlayerZdo(long sender)
        {
            if (ZNet.instance == null || ZDOMan.instance == null) return null;
            if (sender == ZNet.GetUID())
            {
                Player local = Player.m_localPlayer;
                return local != null && local.m_nview != null && local.m_nview.IsValid() ? local.m_nview.GetZDO() : null;
            }
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            return peer == null || peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
        }

        // ── client ──────────────────────────────────────────────────────────────────────────────────────────────────

        private static void RPC_Answer(long sender, int reasonValue, bool granted, float seconds, string why)
        {
            // 0.1.193 security: only the server answers (the routed sender id can be spoofed by any peer).
            if (!FromServer(sender)) { FiresRPGClassesPlugin.Log?.LogWarning($"[Werewolf] refused a Wolf Form answer from peer {sender}: not the server"); return; }
            Reason reason = (Reason)Mathf.Clamp(reasonValue, 0, 2);
            float cap = reason == Reason.Rage ? WerewolfConfig.Value(WerewolfConfig.RageSeconds, 15f)
                      : reason == Reason.Moon ? MoonSecondsCap : SkillSecondsCap;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 1f;
            float clamped = Mathf.Clamp(seconds, 1f, cap);
            if (granted && clamped != seconds) FiresRPGClassesPlugin.Log?.LogWarning($"[Werewolf] server answer {seconds} s clamped to {clamped:0} s");
            Answered?.Invoke(reason, granted, clamped, why);
        }

        /// <summary>0.1.193: true when <paramref name="sender"/> is the server (this machine on a listen host / single player).</summary>
        internal static bool FromServer(long sender)
        {
            ZNet net = ZNet.instance;
            if (net == null) return false;
            if (net.IsServer()) return sender == ZDOMan.GetSessionID();
            ZNetPeer server = net.GetServerPeer();
            return server != null && server.m_uid == sender;
        }

        /// <summary>Asks the server (on a listen host or single player: this machine) to start Wolf Form.</summary>
        internal static bool Ask(Reason reason, float seconds)
        {
            if (ZRoutedRpc.instance == null) return false;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRequest, (int)reason, seconds);
            return true;
        }
    }
}
