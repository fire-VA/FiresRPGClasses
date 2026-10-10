using System;
using System.Reflection;
using FiresRPGClasses.Combat;
using HarmonyLib;

namespace FiresRPGClasses.Integration
{
    /// <summary>
    /// FiresLoreBooks reward verbs (its Hooks registry; no reference to LoreBooks, found by reflection):
    /// - "GiveClassXP &lt;n&gt;"       : class XP, the normal grant with source "lorebook";
    /// - "RaiseSkill &lt;Skill&gt; &lt;n&gt;" : a vanilla skill raised by n (Swords, Run, Woodcutting ...).
    /// LoreBooks runs a book's hooks on the SERVER (once per player) with the reader's player id. Class data lives on the
    /// player's own client, so the server forwards the reward to that player's client by RPC, which applies it there.
    /// </summary>
    internal static class LoreBooksRewards
    {
        private const string RpcReward = "FRC_LoreReward";   // server -> the reader's client: verb, args joined by ' '
        private static ZRoutedRpc s_registeredOn;
        private static bool s_hooked;

        [HarmonyPatch(typeof(ZNet), "Start")]
        private static class RegisterOnStart
        {
            private static void Postfix()
            {
                ZRoutedRpc rpc = ZRoutedRpc.instance;
                if (rpc != null && rpc != s_registeredOn)
                {
                    s_registeredOn = rpc;
                    rpc.Register<string, string>(RpcReward, OnReward);
                }
                if (!s_hooked) s_hooked = HookVerbs();
            }
        }

        private static bool HookVerbs()
        {
            Type hooks = AccessTools.TypeByName("FiresLoreBooks.Books.Hooks");
            MethodInfo register = hooks?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(Action<long, string[]>) }, null);
            if (register == null) return false;
            register.Invoke(null, new object[] { "GiveClassXP", (Action<long, string[]>)((pid, args) => Forward(pid, "GiveClassXP", args)) });
            register.Invoke(null, new object[] { "RaiseSkill", (Action<long, string[]>)((pid, args) => Forward(pid, "RaiseSkill", args)) });
            register.Invoke(null, new object[] { "GrantAttributePoints", (Action<long, string[]>)((pid, args) => Forward(pid, "GrantAttributePoints", args)) });
            FiresRPGClassesPlugin.Log.LogDebug("[LoreRewards] registered GiveClassXP + RaiseSkill with FiresLoreBooks");
            return true;
        }

        /// <summary>Server: send the reward to the client that plays this player id.</summary>
        private static void Forward(long playerId, string verb, string[] args)
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null) return;
            string joined = string.Join(" ", args ?? new string[0]);
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == playerId) { ZRoutedRpc.instance.InvokeRoutedRPC(ZDOMan.GetSessionID(), RpcReward, verb, joined); return; }
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_characterID.IsNone()) continue;
                ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo == null || zdo.GetLong(ZDOVars.s_playerID, 0L) != playerId) continue;
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcReward, verb, joined);
                return;
            }
            FiresRPGClassesPlugin.Log.LogDebug($"[LoreRewards] {verb}: player {playerId} is not online");
        }

        /// <summary>The reader's client applies the reward to the local player.</summary>
        private static void OnReward(long sender, string verb, string joined)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            string[] args = (joined ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (verb)
            {
                case "GiveClassXP":
                    if (args.Length > 0 && long.TryParse(args[0], out long xp))
                        FiresRPGClassesPlugin.Log.LogDebug($"[LoreRewards] GiveClassXP {xp}: granted {ClassXp.GrantFromLore(xp, "lorebook")}");
                    break;
                case "GrantAttributePoints":
                    // Fire 10-09: lore books may grant attribute points. Core 0.2.384's bonus field counts into the spendable
                    // total (capped at ClassProgressState.MaxBonusAttributePoints); the record is saved like after a spend.
                    if (args.Length > 0 && int.TryParse(args[0], out int pts))
                    {
                        State.ClassPlayerData data = State.ClassPlayerStore.Local;
                        if (data == null || State.ClassPlayerStore.LocalIsProtected) break;
                        int added = data.Progress.AddBonusAttributePoints(pts);
                        if (added > 0) State.ClassPlayerStore.SaveLocal();
                        FiresRPGClassesPlugin.Log.LogDebug($"[LoreRewards] GrantAttributePoints {pts}: added {added}");
                    }
                    break;
                case "RaiseSkill":
                    if (args.Length > 1 && Enum.TryParse(args[0], true, out Skills.SkillType skill)
                        && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float amount))
                    {
                        player.RaiseSkill(skill, amount);
                        FiresRPGClassesPlugin.Log.LogDebug($"[LoreRewards] RaiseSkill {skill} {amount}");
                    }
                    break;
            }
        }
    }
}
