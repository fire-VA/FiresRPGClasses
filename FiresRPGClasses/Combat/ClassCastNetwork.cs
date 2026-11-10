using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Doc 14: a cast carries its target point or target ZDO id so every peer applies the same effect in the same
    /// place. Core's AbilityRPCManager syncs effects but not a cast point, so this owns that one message.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class ClassCastNetwork
    {
        private const string RpcCast = "RPC_FiresRPGClassesCast";

        private static ZRoutedRpc _registeredOn;

        public static void Initialize()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(_registeredOn, rpc)) return;
            rpc.Register<ZDOID, string, Vector3, ZDOID>(RpcCast, RPC_Cast);
            _registeredOn = rpc;
        }

        public static void Broadcast(Character caster, string nodeId, Vector3 point, ZDOID targetZdo)
        {
            if (caster == null || string.IsNullOrEmpty(nodeId)) return;
            ZNetView view = caster.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return;
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcCast,
                view.GetZDO().m_uid, nodeId, point, targetZdo);
        }

        private static void RPC_Cast(long sender, ZDOID casterZdo, string nodeId, Vector3 point, ZDOID targetZdo)
        {
            // 0.1.193 security: the caster must be the sender's own character.
            ZDO casterData = ZDOMan.instance?.GetZDO(casterZdo);
            if (casterData == null || casterData.GetOwner() != sender)
            {
                FiresRPGClassesPlugin.Log?.LogDebug($"[Cast] refused {nodeId} from peer {sender}: caster {casterZdo} is not its character");
                return;
            }
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null) return;
            Character caster = ZNetScene.instance?.FindInstance(casterZdo)?.GetComponent<Character>();
            if (caster == Player.m_localPlayer) return;
            ClassCastExecutor.PlayRemote(node, caster, point, targetZdo);
        }
    }
}
