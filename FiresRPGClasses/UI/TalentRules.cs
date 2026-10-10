using System.Collections.Generic;
using System.Linq;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.UI
{
    public enum NodeState
    {
        Locked,
        Available,
        Owned
    }

    /// <summary>
    /// What the Talents board needs to know about a node - its state, whether it can be learned now and why not -
    /// in one place, so the tree, the tooltip card and the respec preview agree. The rules are the ones the old
    /// list board used (doc 13's prerequisite); nothing here changes what can be bought.
    /// </summary>
    public static class TalentRules
    {
        public static ProgressionRates Rates => ProgressionSettings.IsInitialized
            ? ProgressionSettings.CurrentRates
            : ProgressionSettings.DefaultRates;

        public static int Rank(ClassPlayerData data, ClassTreeNode node) =>
            data == null || node == null ? 0 : data.Progress.TalentRank(node.Archetype, node.Id);

        /// <summary>Ultimates are not bought: they open at their level and grow by use (doc 16).</summary>
        public static bool IsUltimate(ClassTreeNode node) =>
            node.Slot == NodeSlot.Ultimate || node.Tier == NodeTier.Ultimate;

        public static NodeState State(ClassPlayerData data, ClassTreeNode node)
        {
            if (data == null || node == null) return NodeState.Locked;
            if (IsUltimate(node)) return data.Progress.Level >= node.MinLevel ? NodeState.Owned : NodeState.Locked;
            if (Rank(data, node) > 0) return NodeState.Owned;
            return data.Progress.Level >= node.MinLevel && PrerequisiteMet(data, node) && MasterPointsMet(data, node, out _)
                ? NodeState.Available
                : NodeState.Locked;
        }

        /// <summary>
        /// Whether the next rank can be bought now; <paramref name="why"/> is the disabled button's caption when not
        /// ("Unlocks at level 40"), <paramref name="cost"/> the next rank's price.
        /// </summary>
        public static bool CanLearn(ClassPlayerData data, ClassTreeNode node, out int cost, out string why)
        {
            cost = 0;
            why = "";
            if (data == null || node == null) { why = "No class data"; return false; }
            if (IsUltimate(node)) { why = "Grows by use"; return false; }
            int rank = Rank(data, node);
            if (rank >= node.MaxRank) { why = "All ranks learned"; return false; }
            cost = ClassTrees.RankCost(rank + 1);
            if (ClassPlayerStore.LocalIsProtected) { why = "Progress protected (see the log)"; return false; }
            if (data.Progress.Level < node.MinLevel) { why = "Unlocks at level " + node.MinLevel; return false; }
            if (!PrerequisiteMet(data, node)) { why = "Learn a node in the tier above first"; return false; }
            if (!MasterPointsMet(data, node, out int inPath))
            {
                why = "Needs " + ClassTrees.MasterPointsRequiredInPath + " points in " + PathName(node) + " (you have " + inPath + ")";
                return false;
            }
            if (data.Progress.UnspentSkillPoints(Rates) < cost) { why = "Needs " + Points(cost); return false; }
            return true;
        }

        public static bool TryLearn(ClassPlayerData data, ClassTreeNode node)
        {
            if (!CanLearn(data, node, out _, out _)) return false;
            if (!data.Progress.TryBuyTalentRank(node.Archetype, node.Id, node.MaxRank, Rates)) return false;
            ClassPlayerStore.SaveLocal();
            return true;
        }

        public static string Points(int points) => points + (points == 1 ? " point" : " points");

        /// <summary>Doc 13: one learned node in the previous tier of the same path; the starter counts for every path.</summary>
        public static bool PrerequisiteMet(ClassPlayerData data, ClassTreeNode node)
        {
            if (!string.IsNullOrEmpty(node.RequiresNodeId))
                return data.Progress.TalentRank(node.Archetype, node.RequiresNodeId) > 0;
            if (node.Tier == NodeTier.Starter) return true;

            ClassTree tree = ClassTrees.Tree(node.Archetype);
            if (tree == null) return false;
            NodeTier previous = (NodeTier)((int)node.Tier - 1);
            foreach (ClassTreeNode other in tree.Nodes)
            {
                if (other.Tier != previous) continue;
                if (!other.CountsForPath(node.PathId) && !node.CountsForPath(other.PathId)) continue;
                if (data.Progress.TalentRank(node.Archetype, other.Id) > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Doc 13 / Fire 10-01 ("enforce the master points rule"): a Master node needs MasterPointsRequiredInPath points spent
        /// in its own path first. <paramref name="inPath"/> is what is spent there now. Other tiers always pass.
        /// </summary>
        public static bool MasterPointsMet(ClassPlayerData data, ClassTreeNode node, out int inPath)
        {
            inPath = 0;
            if (data == null || node == null || node.Tier != NodeTier.Master) return true;
            SpentByPath(data, node.Archetype).TryGetValue(node.PathId, out inPath);
            return inPath >= ClassTrees.MasterPointsRequiredInPath;
        }

        /// <summary>
        /// At load: un-learns every Master node whose path now holds fewer than MasterPointsRequiredInPath points. Its points
        /// come back on their own (spent points are summed from the ranks owned), so nothing is lost. It also leaves the
        /// passive grid and any bar slot. Repeats until stable, since dropping one Master lowers its path's total. Tells the
        /// player once and logs each at Info. Returns how many were refunded; the caller saves.
        /// </summary>
        public static int RevalidateMasters(ClassPlayerData data, Player player)
        {
            if (data == null || ClassPlayerStore.LocalIsProtected || !ClassTrees.IsLoaded) return 0;
            var refunded = new List<ClassTreeNode>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (KeyValuePair<ArchetypeClass, Dictionary<string, int>> tree in data.Progress.Talents.ToList())
                {
                    foreach (string nodeId in tree.Value.Keys.ToList())
                    {
                        ClassTreeNode node = ClassTrees.Node(nodeId);
                        if (node == null || node.Tier != NodeTier.Master || tree.Value[nodeId] <= 0) continue;
                        if (MasterPointsMet(data, node, out _)) continue;
                        tree.Value.Remove(nodeId);
                        data.Progress.UnequipPassive(nodeId);
                        foreach (BarLayout bar in new[] { data.Front, data.Back })
                            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.LastSkillSlot; slot++)
                                if (bar.SkillAt(slot) == nodeId) bar.SetSkill(slot, "");
                        refunded.Add(node);
                        changed = true;
                    }
                }
            }
            if (refunded.Count == 0) return 0;

            foreach (ClassTreeNode node in refunded)
                FiresRPGClassesPlugin.Log.LogInfo("[Classes] Master talent " + node.DisplayName + " (" + node.Archetype
                    + ") refunded: it now needs " + ClassTrees.MasterPointsRequiredInPath + " points in " + PathName(node) + ".");
            string names = string.Join(", ", refunded.Select(n => n.DisplayName).ToArray());
            string paths = string.Join(", ", refunded.Select(PathName).Distinct().ToArray());
            player?.Message(MessageHud.MessageType.Center, (refunded.Count == 1 ? "Master talent " : "Master talents ") + names
                + " refunded: " + (refunded.Count == 1 ? "it now needs " : "they now need ") + ClassTrees.MasterPointsRequiredInPath
                + " points in " + paths);
            return refunded.Count;
        }

        private static string PathName(ClassTreeNode node)
        {
            string name = ClassTrees.Tree(node.Archetype)?.Paths.FirstOrDefault(p => p.Id == node.PathId)?.DisplayName;
            return string.IsNullOrEmpty(name) ? "its path" : name;
        }

        /// <summary>Points a reset of this archetype would refund, per path id (the starter's under "all").</summary>
        public static Dictionary<string, int> SpentByPath(ClassPlayerData data, ArchetypeClass archetype)
        {
            var spent = new Dictionary<string, int>();
            ClassTree tree = ClassTrees.Tree(archetype);
            if (data == null || tree == null) return spent;
            foreach (ClassTreeNode node in tree.Nodes)
            {
                int rank = Rank(data, node);
                if (rank <= 0 || IsUltimate(node)) continue;
                int points = 0;
                for (int r = 1; r <= rank; r++) points += ClassTrees.RankCost(r);
                spent.TryGetValue(node.PathId, out int sum);
                spent[node.PathId] = sum + points;
            }
            return spent;
        }

        /// <summary>The same reset the Character board's Respec does: ranks refunded, this tree's passives unequipped.</summary>
        public static void Reset(ClassPlayerData data, ArchetypeClass archetype)
        {
            if (data == null || ClassPlayerStore.LocalIsProtected) return;
            var passives = new List<string>();
            foreach (ClassTreeNode node in ClassTrees.PassivesFor(archetype)) passives.Add(node.Id);
            data.Progress.RespecArchetype(archetype, passives);
            ClassPlayerStore.SaveLocal();
        }
    }
}
