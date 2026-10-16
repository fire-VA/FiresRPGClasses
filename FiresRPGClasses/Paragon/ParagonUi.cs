using System;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Classes;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    public sealed class ParagonClusterView
    {
        public string id, name;
        public float angleDeg;
    }

    public sealed class ParagonTreeView
    {
        public string id, name;
        public Color colour;
        public int[] tierGates;
        public List<ParagonClusterView> clusters = new List<ParagonClusterView>();
    }

    public sealed class ParagonStarView
    {
        public string id, name, shortName, cluster, lockReason;
        public int tier, maxRank, rank;
        public ParagonStarKind kind;
        public bool capstone, bridge, unlocked, slotted;
        public Vector2 pos;
        public string[] links;
    }

    /// <summary>
    /// The Paragon UI contract (Tools\RPG_PARAGON_TALENT_PLAN.md §9, agreed with [classes-ui] 10-14). [classes-ui]'s Paragon tab reflects
    /// this type (UI\ParagonSource.cs), so names are the contract: add-only. State lives in Core's ClassProgressState (schema 5:
    /// ParagonRanks, ParagonSlotted, earned points from the Valor level); the star definitions in ParagonCatalog (paragon-trees.yaml).
    /// Rules (plan §6 / §8):
    /// - a star unlocks when its tree has the tier's gate spent (0 / 25 / 75 / 150) AND it is an entry (tier 1) or a linked star
    ///   (either direction) has a point;
    /// - a rank costs 1 Paragon point (Fire: linear); refused past maxRank or with none unspent;
    /// - slottables count only while slotted, 4 per tree; slotting is refused in combat (an enemy within 15 m) and needs rank 1;
    /// - respec refunds everything in a tree (or all trees with null), like talents.
    /// Every action says why it was refused, saves the record and raises <see cref="Changed"/>.
    /// </summary>
    public static class ParagonUi
    {
        public const int SlotsPerTree = ClassProgressState.ParagonSlotsPerTree;
        private const float CombatRange = 15f;

        public static event Action Changed;

        private static ClassProgressState Progress => ClassPlayerStore.Local?.Progress;

        public static IReadOnlyList<ParagonTreeView> Trees
        {
            get
            {
                var list = new List<ParagonTreeView>();
                foreach (ParagonTreeDef t in ParagonCatalog.Trees)
                {
                    var v = new ParagonTreeView { id = t.Id, name = t.Name, colour = ColourOf(t.Colour), tierGates = t.TierGates };
                    foreach (ParagonClusterDef c in t.Clusters) v.clusters.Add(new ParagonClusterView { id = c.Id, name = c.Name, angleDeg = c.Angle });
                    list.Add(v);
                }
                return list;
            }
        }

        public static IReadOnlyList<ParagonStarView> Stars(string treeId)
        {
            var list = new List<ParagonStarView>();
            string[] slotted = Slotted(treeId);
            foreach (ParagonStarDef s in ParagonCatalog.StarsIn(treeId))
            {
                bool unlocked = Unlocked(s, out string why);
                list.Add(new ParagonStarView
                {
                    id = s.Id, name = s.Name, shortName = s.ShortName, cluster = s.Cluster, tier = s.Tier, maxRank = s.MaxRank,
                    rank = Rank(s.Id), kind = s.Kind, capstone = s.Capstone, bridge = s.Bridge, unlocked = unlocked,
                    lockReason = unlocked ? null : why, pos = s.Pos, links = s.Links, slotted = slotted.Contains(s.Id),
                });
            }
            return list;
        }

        public static int Rank(string starId) => Progress?.ParagonRank(starId) ?? 0;

        public static int SpentInTree(string treeId) => Progress?.ParagonSpentInTree(treeId, ParagonCatalog.TreeOf) ?? 0;

        public static int Unspent => Progress?.UnspentParagonPoints() ?? 0;

        public static int Earned => Progress?.EarnedParagonPoints() ?? 0;

        public static string[] Slotted(string treeId)
        {
            var slots = new string[SlotsPerTree];
            ClassProgressState p = Progress;
            if (p != null) for (int i = 0; i < SlotsPerTree; i++) slots[i] = p.ParagonSlot(treeId, i);
            return slots;
        }

        /// <summary>Unlock test: the tier gate in its tree, then the path (entry or a linked star with a point).</summary>
        public static bool Unlocked(ParagonStarDef s, out string why)
        {
            why = null;
            if (s == null) { why = "unknown star"; return false; }
            ParagonTreeDef tree = ParagonCatalog.Tree(s.Tree);
            int gate = tree != null ? tree.GateFor(s.Tier) : 0, spent = SpentInTree(s.Tree);
            if (spent < gate) { why = $"Locked: spend {gate - spent} more in {tree?.Name ?? s.Tree}"; return false; }
            if (s.IsEntry || Rank(s.Id) > 0) return true;
            foreach (string l in s.Links) if (Rank(l) > 0) return true;
            foreach (string l in s.LinkedFrom) if (Rank(l) > 0) return true;
            why = "Locked: put a point in a linked star first";
            return false;
        }

        public static bool TryBuy(string starId, out string why)
        {
            why = Refuse(out ClassProgressState p);
            if (why != null) return false;
            ParagonStarDef s = ParagonCatalog.Star(starId);
            if (s == null) { why = "unknown star"; return false; }
            if (!Unlocked(s, out why)) return false;
            if (s.MaxRank > 0 && p.ParagonRank(starId) >= s.MaxRank) { why = "already at max rank"; return false; }
            if (p.UnspentParagonPoints() < 1) { why = "no unspent Paragon points"; return false; }
            if (!p.TryBuyParagonRank(starId, s.MaxRank)) { why = "refused"; return false; }
            return Commit($"bought {starId} rank {p.ParagonRank(starId)}");
        }

        public static bool TrySlot(string treeId, int slot, string starId, out string why)
        {
            why = Refuse(out ClassProgressState p);
            if (why != null) return false;
            ParagonStarDef s = ParagonCatalog.Star(starId);
            if (s == null || s.Tree != treeId) { why = "that star isn't in this tree"; return false; }
            if (s.Kind != ParagonStarKind.Slottable) { why = "passive stars are always on; only slottables go in a slot"; return false; }
            if (p.ParagonRank(starId) < 1) { why = "buy a rank first"; return false; }
            if (InCombat()) { why = "can't change slots in combat"; return false; }
            for (int i = 0; i < SlotsPerTree; i++) if (i != slot && p.ParagonSlot(treeId, i) == starId) p.UnslotParagon(treeId, i);   // a move, not a copy
            if (!p.TrySlotParagon(treeId, slot, starId)) { why = "refused"; return false; }
            return Commit($"slotted {starId} in {treeId} slot {slot + 1}");
        }

        public static bool TryUnslot(string treeId, int slot)
        {
            if (Refuse(out ClassProgressState p) != null || InCombat()) return false;
            return p.UnslotParagon(treeId, slot) && Commit($"unslotted {treeId} slot {slot + 1}");
        }

        public static bool TryRespec(string treeId, out string why)
        {
            why = Refuse(out ClassProgressState p);
            if (why != null) return false;
            if (InCombat()) { why = "can't respec in combat"; return false; }
            int refunded = 0;
            if (treeId == null) refunded = p.RespecParagon();
            else
            {
                foreach (string id in p.ParagonRanks.Keys.ToList())
                    if (ParagonCatalog.TreeOf(id) == treeId) { refunded += Mathf.Max(0, p.ParagonRanks[id]); p.ParagonRanks.Remove(id); }
                p.ParagonSlotted.Remove(treeId);
            }
            if (refunded == 0) { why = "nothing to refund"; return false; }
            return Commit($"respec {(treeId ?? "all trees")}: {refunded} point(s) back");
        }

        public static string Tooltip(string starId) => string.Join("\n", TooltipLines(starId));

        /// <summary>title, kind/tier, rank, effect, current, next, requirement (Latin-1, only &lt;b&gt;/&lt;color&gt;).</summary>
        public static string[] TooltipLines(string starId)
        {
            ParagonStarDef s = ParagonCatalog.Star(starId);
            if (s == null) return new string[0];
            int rank = Rank(starId);
            ParagonTreeDef tree = ParagonCatalog.Tree(s.Tree);
            string cluster = tree?.Clusters.FirstOrDefault(c => c.Id == s.Cluster)?.Name ?? s.Cluster;
            string kind = s.Kind == ParagonStarKind.Slottable ? (s.Capstone ? "Slottable capstone" : "Slottable") : (s.Bridge ? "Passive bridge" : "Passive");
            var lines = new List<string>
            {
                "<b>" + s.Name + "</b>",
                $"{kind} - {cluster} - tier {s.Tier} ({tree?.GateFor(s.Tier) ?? 0} spent)",
                $"Ranks {rank} / {s.MaxRank}",
                s.EffectText(s.ValueAt(s.MaxRank)) + "  <color=#9a8a6a>(at max)</color>",
                rank > 0 ? "Now: " + s.EffectText(s.ValueAt(rank)) : "Now: -",
                rank < s.MaxRank ? "Next: " + s.EffectText(s.ValueAt(rank + 1)) : "Maxed",
            };
            if (!Unlocked(s, out string why)) lines.Add("<color=#e0664c>" + why + "</color>");
            else if (s.Kind == ParagonStarKind.Slottable && !Slotted(s.Tree).Contains(s.Id)) lines.Add("<color=#9a8a6a>Slot it to make it count (4 per tree)</color>");
            if (!ParagonEffects.IsLive(starId)) lines.Add("<color=#9a8a6a>(effect coming soon)</color>");
            return lines.ToArray();
        }

        // ---- helpers ----
        private static string Refuse(out ClassProgressState p)
        {
            p = Progress;
            if (Player.m_localPlayer == null || p == null) return "no character loaded";
            if (ClassPlayerStore.LocalIsProtected) return "this character's class record is protected (read-only)";
            return null;
        }

        private static bool Commit(string what)
        {
            ClassPlayerStore.SaveLocal();
            ParagonEffects.Refresh();
            FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] {what}; unspent {Unspent}");
            try { Changed?.Invoke(); } catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] a Changed handler threw: " + e.Message); }
            return true;
        }

        private static bool InCombat()
        {
            Player me = Player.m_localPlayer;
            return me != null && (me.IsTargeted() || BaseAI.HaveEnemyInRange(me, me.transform.position, CombatRange));
        }

        private static Color ColourOf(string hex) => ColorUtility.TryParseHtmlString(hex ?? "#FFFFFF", out Color c) ? c : Color.white;
    }
}
