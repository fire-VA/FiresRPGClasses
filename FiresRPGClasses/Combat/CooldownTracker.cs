using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>Per-ability cooldowns for the local player, keyed by node id. Cleared when the world unloads.</summary>
    public static class CooldownTracker
    {
        private static readonly Dictionary<string, float> ReadyAt = new Dictionary<string, float>();

        public static void Clear() => ReadyAt.Clear();

        /// <summary>
        /// 0.1.131 (Fire 10-14): the one generic class cooldown-reduction hook. Gear, talents or other mods add a source returning a
        /// reduction share for a node id (0.10 = -10%); the shares add, then SoftCap.Asymptote ([13 - Balance] knee 40%, limit 80%)
        /// so stacking keeps paying but no skill becomes free. Nothing registers a source yet, so today this changes no cooldown.
        /// </summary>
        public static readonly List<Func<string, float>> ReductionSources = new List<Func<string, float>>();

        /// <summary>A class skill's cooldown after the generic reduction (every class cast and Bat Form go through here).</summary>
        public static float Scaled(string nodeId, float seconds)
        {
            if (seconds <= 0f || ReductionSources.Count == 0) return seconds;
            float share = 0f;
            for (int i = 0; i < ReductionSources.Count; i++)
            {
                try { share += Mathf.Max(0f, ReductionSources[i](nodeId)); }
                catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug($"[Cooldown] a reduction source threw ({e.GetType().Name}); skipped"); }
            }
            if (share <= 0f) return seconds;
            float cut = SoftCap.Asymptote(share, BalanceConfig.Value(BalanceConfig.CooldownReductionKnee, 0.40f),
                BalanceConfig.Value(BalanceConfig.CooldownReductionLimit, 0.80f));
            return seconds * (1f - cut);
        }

        public static void Start(string nodeId, float seconds)
        {
            if (string.IsNullOrEmpty(nodeId) || seconds <= 0f) return;
            ReadyAt[nodeId] = Time.time + seconds;
        }

        /// <summary>Makes a node ready again (a cast the server refused after the cooldown had started).</summary>
        public static void Reset(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId)) ReadyAt.Remove(nodeId);
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
