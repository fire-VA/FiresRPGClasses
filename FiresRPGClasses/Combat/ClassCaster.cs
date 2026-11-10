using System;
using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using Newtonsoft.Json;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.213 ([lead] / [ghost], Fire 10-30: the server-side Studio god clone "must FIGHT AS ITS CLASS"): one class system - a
    /// non-player body casts through the SAME path as the local player's bar (ClassCastExecutor.PlayLocal -> ClassCastNetwork ->
    /// ClassSkillKit / Core's effects), with the class record taken from a customData dictionary (GodProfile.Shadow.m_customData) and
    /// its own cooldown clock (CooldownTracker.*For, keyed by the body's ZDOID). The local player's path (ClassCombatDriver) is untouched.
    /// God rules, like GodApi: a node must be learned in that record, but no level gate and no ultimate cost; the cooldown still runs.
    /// Owner only (the peer that owns the body casts it - the dedi for the clone). Kits whose handler still reads the LOCAL player
    /// (see Docs: the per-kit audit) refuse or do nothing for such a body; their "[ClassCaster]" line says which.
    /// </summary>
    public static class ClassCaster
    {
        private const float AutoTargetRange = 30f;

        /// <summary>
        /// Cast for <paramref name="caster"/>. what = a bar slot "2".."8" (read from the record's active bar) or a node id.
        /// target = "" (self for a self / around-you skill, else the nearest enemy within 30 m), "x,y,z" (a point), or "zdo:user:id" (a body).
        /// JSON {"ok","why","node","target","cooldown"}.
        /// </summary>
        public static string CastFor(Character caster, IDictionary<string, string> customData, string what, string target)
        {
            if (caster == null || caster.IsDead()) return Out(false, "no live caster");
            var view = caster.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return Out(false, "not the owner of that body");
            ClassPlayerData data = Read(customData);
            BarLayout bar = data.Bar(data.ActiveBar);
            string nodeId = int.TryParse(what, out int slot) ? (slot == BarLayout.UltimateSlot ? bar.UltimateId : BarLayout.IsSkillSlot(slot) ? bar.SkillAt(slot) : "") : what;
            if (string.IsNullOrEmpty(nodeId)) return Out(false, $"nothing on {data.ActiveBar} slot {what}");
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null) return Out(false, $"no class node '{nodeId}'");
            bool ultimate = UltimateCatalog.Get(nodeId) != null && bar.UltimateId == nodeId;
            if (!ultimate && data.Progress.TalentRank(node.Archetype, nodeId) <= 0) return Out(false, $"{nodeId} is not learned in this record", nodeId);
            ZDOID body = view.GetZDO().m_uid;
            if (!CooldownTracker.IsReadyFor(body, nodeId)) return Out(false, $"{nodeId} on cooldown ({CooldownTracker.RemainingFor(body, nodeId):0.0} s)", nodeId);

            CastTarget t = Resolve(node, caster, target);
            if (!t.Resolved) return Out(false, string.IsNullOrEmpty(t.Refusal) ? "no target" : t.Refusal, nodeId);

            ClassCastExecutor.PlayLocal(node, caster, t);
            bool refused = ClassCastExecutor.LastHandlerRefused;
            float cd = refused ? Mathf.Min(node.Cooldown, 1f) : CooldownTracker.Scaled(nodeId, node.Cooldown);
            CooldownTracker.StartFor(body, nodeId, cd);
            string on = t.TargetCharacter != null ? t.TargetCharacter.m_name : "point " + t.Point.ToString("F1");
            FiresRPGClassesPlugin.Log.LogDebug($"[ClassCaster] {caster.m_name} cast {nodeId} on {on} ({node.Targeting.Mode}){(refused ? " - the skill's handler refused" : "")}");
            return Out(!refused, refused ? "the skill's handler refused (its kit may still need the local player)" : "cast", nodeId, on, cd);
        }

        private static CastTarget Resolve(ClassTreeNode node, Character caster, string target)
        {
            target = (target ?? "").Trim();
            if (target.StartsWith("zdo:", StringComparison.Ordinal))
            {
                string[] p = target.Substring(4).Split(':');
                if (p.Length == 2 && long.TryParse(p[0], out long u) && uint.TryParse(p[1], out uint i))
                {
                    var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(new ZDOID(u, i)) : null;
                    var c = go != null ? go.GetComponent<Character>() : null;
                    return c != null ? CastTarget.On(c) : CastTarget.No("that body isn't loaded here");
                }
                return CastTarget.No("bad zdo target (zdo:user:id)");
            }
            if (target.Length > 0)
            {
                string[] p = target.Split(',');
                if (p.Length == 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                    return CastTarget.At(new Vector3(x, y, z));
                return CastTarget.No("bad point target (x,y,z)");
            }
            switch (node.Targeting.Mode)
            {
                case TargetingMode.Self:
                case TargetingMode.AroundYou:
                case TargetingMode.NextAttack:
                case TargetingMode.Ally:
                    return CastTarget.On(caster);
            }
            Character best = null; float bestD = node.Targeting.Range > 0f ? node.Targeting.Range : AutoTargetRange;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || !ClassTargeting.IsEnemyTarget(caster, c)) continue;
                float d = Vector3.Distance(c.transform.position, caster.transform.position);
                if (d <= bestD) { best = c; bestD = d; }
            }
            return best != null ? CastTarget.On(best) : CastTarget.No("no enemy within range");
        }

        private static readonly JsonSerializerSettings ReadJson = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Auto };
        private static ClassPlayerData Read(IDictionary<string, string> customData)
        {
            if (customData != null && customData.TryGetValue(ClassPlayerStore.CustomDataKey, out string raw) && !string.IsNullOrEmpty(raw))
            {
                try { return JsonConvert.DeserializeObject<ClassPlayerData>(raw, ReadJson) ?? new ClassPlayerData(); } catch { }
            }
            return new ClassPlayerData();
        }

        private static string Out(bool ok, string why, string node = "", string target = "", float cooldown = 0f) =>
            JsonConvert.SerializeObject(new { ok, why, node, target, cooldown });
    }
}
