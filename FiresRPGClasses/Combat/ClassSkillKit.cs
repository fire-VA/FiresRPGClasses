using System.Collections.Generic;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The class kit: what each tree node really does. A node counts as implemented when a handler is registered for it
    /// here, or when its Core effect binding reaches an effect Core applies. Everything else is not implemented yet, and
    /// the rest of the mod treats it that way: an unimplemented evasion leaves the vanilla dodge alone.
    /// FiresClassTest reads <see cref="Implements"/> by reflection (Tools\CLASS_TEST_PLAN.md).
    /// </summary>
    public static class ClassSkillKit
    {
        /// <summary>Runs a cast; returns false when it could not happen (no target, refused), true once it did.</summary>
        public delegate bool CastHandler(ClassTreeNode node, Character caster, CastTarget target);

        private static readonly Dictionary<string, CastHandler> Handlers = new Dictionary<string, CastHandler>();
        // Passives another kit's handler reads (Lasting Form lengthens Wildshape): implemented, with nothing to cast.
        private static readonly HashSet<string> ReadPassives = new HashSet<string>();

        /// <summary>Gives a node its behaviour. A later registration for the same node replaces the earlier one.</summary>
        public static void Register(string nodeId, CastHandler handler)
        {
            if (string.IsNullOrEmpty(nodeId) || handler == null) return;
            Handlers[nodeId] = handler;
        }

        /// <summary>A passive that works through another node's handler, so it counts as implemented.</summary>
        public static void RegisterPassive(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId)) ReadPassives.Add(nodeId);
        }

        public static bool HasHandler(string nodeId) => nodeId != null && Handlers.ContainsKey(nodeId);

        /// <summary>True when casting this node does something: a kit handler, or a Core effect the cast path applies.</summary>
        public static bool Implements(string nodeId)
        {
            if (HasHandler(nodeId) || PassiveKit.Handles(nodeId) || (nodeId != null && ReadPassives.Contains(nodeId))) return true;
            ClassTreeNode node = ClassTrees.Node(nodeId);
            return node != null && ClassCastExecutor.AppliesCoreEffect(node);
        }

        /// <summary>The cast path's first stop: the node's handler, when it has one.</summary>
        internal static bool TryRun(ClassTreeNode node, Character caster, CastTarget target, out bool succeeded)
        {
            succeeded = false;
            if (node == null || !Handlers.TryGetValue(node.Id, out CastHandler handler)) return false;
            try { succeeded = handler(node, caster, target); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FiresRPGClasses] {node.DisplayName} ({node.Id}) handler threw {ex.GetType().Name}: {ex.Message}");
            }
            return true;
        }
    }
}
