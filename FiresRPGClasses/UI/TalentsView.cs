using System.Collections.Generic;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;
using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 13's Talents board: one archetype at a time, its nodes by tier and path, with rank pips and a buy
    /// button per node. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class TalentsView : IClassesView
    {
        private const float PickerHeight = 34f;
        private const float NodeRowHeight = 40f;

        public ClassesTab Tab => ClassesTab.Talents;

        public string Title => "Talents";

        private RectTransform _page;
        private UIBuilderHelper.ScrollAreaResult _scroll;
        private TextMeshProUGUI _budget;
        private ArchetypeClass _shown = ArchetypeClass.Tank;

        public void Build(RectTransform page)
        {
            _page = page;
            BuildPicker(page);

            _budget = ClassesTheme.Text(page, "Budget", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.SmallFontSize);
            _budget.rectTransform.Fill(-(PickerHeight + 4f), PickerHeight);

            _scroll = UIBuilderHelper.CreateScrollArea(page, "TalentScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            _scroll.Root.GetComponent<RectTransform>().offsetMax =
                new Vector2(0f, -(PickerHeight * 2f + 12f));
        }

        private void BuildPicker(RectTransform page)
        {
            GameObject strip = UIBuilderHelper.CreatePanel(page, "ArchetypePicker",
                new Vector2(0f, 1f), new Vector2(1f, 1f), UIFontConfig.Colors.HeaderBackground);
            RectTransform rect = strip.GetComponent<RectTransform>();
            rect.Fill(0f, PickerHeight);

            List<ArchetypeClass> archetypes = new List<ArchetypeClass>(ClassTrees.Archetypes);
            for (int i = 0; i < archetypes.Count; i++)
            {
                ArchetypeClass archetype = archetypes[i];
                float min = (float)i / archetypes.Count;
                float max = (float)(i + 1) / archetypes.Count;
                ClassesTheme.Button(rect, archetype.ToString(), new Vector2(min, 0f), new Vector2(max, 1f),
                    () => { _shown = archetype; Refresh(); });
            }
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            ClassTree tree = ClassTrees.Tree(_shown);
            if (data == null || tree == null || _page == null) return;

            ProgressionRates rates = ProgressionSettings.IsInitialized
                ? ProgressionSettings.CurrentRates
                : ProgressionSettings.DefaultRates;

            _budget.text = _shown + " - " + PathNames(tree) + "   |   "
                + data.Progress.UnspentSkillPoints(rates) + " points unspent, "
                + CharacterView.SpentIn(data, _shown) + " spent here";

            ClassesTheme.ClearChildren(_scroll.Content);
            int row = 0;
            NodeTier? lastTier = null;
            foreach (ClassTreeNode node in Sorted(tree))
            {
                if (lastTier != node.Tier)
                {
                    TierHeader(row++, node.Tier);
                    lastTier = node.Tier;
                }
                NodeRow(row++, data, rates, node);
            }
            ClassesTheme.SetContentHeight(_scroll.Content, row, NodeRowHeight);
        }

        private static string PathNames(ClassTree tree)
        {
            List<string> names = new List<string>();
            foreach (ClassTreePath path in tree.Paths) names.Add(path.DisplayName);
            return string.Join(" / ", names.ToArray());
        }

        private static IEnumerable<ClassTreeNode> Sorted(ClassTree tree)
        {
            List<ClassTreeNode> nodes = new List<ClassTreeNode>(tree.Nodes);
            nodes.Sort((a, b) =>
            {
                int byTier = a.Tier.CompareTo(b.Tier);
                if (byTier != 0) return byTier;
                int byLevel = a.MinLevel.CompareTo(b.MinLevel);
                return byLevel != 0 ? byLevel : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
            return nodes;
        }

        private void TierHeader(int index, NodeTier tier)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Tier_" + tier, index, NodeRowHeight);
            ClassesTheme.Text(row, "Label", tier.ToString().ToUpperInvariant(), UIFontConfig.Colors.BrightGold,
                new Vector2(0.01f, 0f), new Vector2(0.99f, 1f), ClassesTheme.RowFontSize);
        }

        private void NodeRow(int index, ClassPlayerData data, ProgressionRates rates, ClassTreeNode node)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Node_" + node.Id, index, NodeRowHeight);
            int rank = data.Progress.TalentRank(node.Archetype, node.Id);
            bool levelMet = data.Progress.Level >= node.MinLevel;

            ClassesTheme.Text(row, "Name", node.DisplayName + "  " + Pips(rank, node.MaxRank),
                levelMet ? (rank > 0 ? ClassesTheme.Good : ClassesTheme.Ready) : ClassesTheme.Locked,
                new Vector2(0.02f, 0.45f), new Vector2(0.78f, 1f), ClassesTheme.RowFontSize);

            ClassesTheme.Text(row, "Detail",
                "level " + node.MinLevel + "   " + node.Slot + "   " + Describe(node.Targeting),
                ClassesTheme.Dim,
                new Vector2(0.02f, 0f), new Vector2(0.78f, 0.5f), ClassesTheme.SmallFontSize);

            if (rank >= node.MaxRank || !levelMet) return;
            int nextRank = rank + 1;
            if (data.Progress.UnspentSkillPoints(rates) < ClassTrees.RankCost(nextRank)) return;
            if (!PrerequisiteMet(data, node)) return;

            ClassesTheme.Button(row, "Buy " + ClassTrees.RankCost(nextRank) + "p",
                new Vector2(0.8f, 0.15f), new Vector2(0.98f, 0.85f), () =>
                {
                    ProgressionRates now = ProgressionSettings.IsInitialized
                        ? ProgressionSettings.CurrentRates
                        : ProgressionSettings.DefaultRates;
                    if (!data.Progress.TryBuyTalentRank(node.Archetype, node.Id, node.MaxRank, now)) return;
                    ClassPlayerStore.SaveLocal();
                    Refresh();
                });
        }

        /// <summary>Doc 13: one learned node in the previous tier of the same path; the starter counts for every path.</summary>
        private static bool PrerequisiteMet(ClassPlayerData data, ClassTreeNode node)
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

        private static string Pips(int rank, int maxRank)
        {
            string pips = "";
            for (int i = 0; i < maxRank; i++) pips += i < rank ? "+" : "-";
            return "[" + pips + "]";
        }

        internal static string Describe(TargetingSpec spec)
        {
            string text = spec.Mode.ToString();
            if (spec.Range > 0f) text += " " + spec.Range + " m range";
            if (spec.Radius > 0f) text += " " + spec.Radius + " m radius";
            return text;
        }
    }
}
