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
        /// The one generic class cooldown-reduction hook. Gear, talents or other mods add a source returning a
        /// reduction share for a node id (0.10 = -10%); the shares add, then SoftCap.Asymptote ([13 - Balance] knee 40%, limit 80%)
        /// so stacking keeps paying but no skill becomes free. With no source registered it changes no cooldown.
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

        // 0.1.213 ([lead] / [ghost], the Studio god clone fights as its class): one clock per OTHER body (a non-player caster's ZDOID),
        // add-only; the local player keeps the node-id key above, unchanged
        private static readonly Dictionary<string, float> BodyReadyAt = new Dictionary<string, float>();
        private static string BodyKey(ZDOID body, string nodeId) => body.UserID + ":" + body.ID + "|" + nodeId;

        public static void StartFor(ZDOID body, string nodeId, float seconds)
        {
            if (string.IsNullOrEmpty(nodeId) || seconds <= 0f) return;
            BodyReadyAt[BodyKey(body, nodeId)] = Time.time + seconds;
        }

        public static bool IsReadyFor(ZDOID body, string nodeId) =>
            !string.IsNullOrEmpty(nodeId) && (!BodyReadyAt.TryGetValue(BodyKey(body, nodeId), out float at) || Time.time >= at);

        public static float RemainingFor(ZDOID body, string nodeId) =>
            !string.IsNullOrEmpty(nodeId) && BodyReadyAt.TryGetValue(BodyKey(body, nodeId), out float at) ? Mathf.Max(0f, at - Time.time) : 0f;

        public static void ClearFor(ZDOID body)
        {
            string prefix = body.UserID + ":" + body.ID + "|";
            foreach (var k in new List<string>(BodyReadyAt.Keys)) if (k.StartsWith(prefix, StringComparison.Ordinal)) BodyReadyAt.Remove(k);
        }

        /// <summary>0 when ready, 1 when the cooldown just started - the HUD sweep.</summary>
        public static float Fraction(string nodeId, float total)
        {
            if (total <= 0f) return 0f;
            return Mathf.Clamp01(Remaining(nodeId) / total);
        }
    }
}
