using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>Per-ability cooldowns for the local player, keyed by node id. Cleared when the world unloads.</summary>
    public static class CooldownTracker
    {
        private static readonly Dictionary<string, float> ReadyAt = new Dictionary<string, float>();

        public static void Clear() => ReadyAt.Clear();

        public static void Start(string nodeId, float seconds)
        {
            if (string.IsNullOrEmpty(nodeId) || seconds <= 0f) return;
            ReadyAt[nodeId] = Time.time + seconds;
        }

        public static bool IsReady(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return false;
            return !ReadyAt.TryGetValue(nodeId, out float readyAt) || Time.time >= readyAt;
        }

        public static float Remaining(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return 0f;
            if (!ReadyAt.TryGetValue(nodeId, out float readyAt)) return 0f;
            return Mathf.Max(0f, readyAt - Time.time);
        }

        /// <summary>0 when ready, 1 when the cooldown just started - the HUD sweep.</summary>
        public static float Fraction(string nodeId, float total)
        {
            if (total <= 0f) return 0f;
            return Mathf.Clamp01(Remaining(nodeId) / total);
        }
    }
}
