using System;
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
    /// Doc 19's Character board: level and Valor, unspent points, the five attributes with buy buttons, points
    /// spent per archetype, the front and back bars, and respec. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class CharacterView : IClassesView
    {
        private const float SummaryHeight = 96f;
        private const float AttributeRowHeight = 30f;

        public ClassesTab Tab => ClassesTab.Character;

        public string Title => "Character";

        private RectTransform _page;
        private TextMeshProUGUI _summary;
        private UIBuilderHelper.ScrollAreaResult _scroll;

        public void Build(RectTransform page)
        {
            _page = page;
            _summary = ClassesTheme.Text(page, "Summary", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.RowFontSize);
            _summary.rectTransform.Fill(-8f, SummaryHeight);
            _summary.alignment = TextAlignmentOptions.TopLeft;

            _scroll = UIBuilderHelper.CreateScrollArea(page, "CharacterScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            RectTransform rect = _scroll.Root.GetComponent<RectTransform>();
            rect.offsetMax = new Vector2(0f, -(SummaryHeight + 12f));
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null) return;
            ProgressionRates rates = CurrentRates();

            _summary.text = Summary(data, rates);
            ClassesTheme.ClearChildren(_scroll.Content);

            int row = 0;
            Header(row++, "Attributes - " + data.Progress.UnspentAttributePoints(rates) + " points unspent");
            foreach (AttributeKind kind in (AttributeKind[])Enum.GetValues(typeof(AttributeKind)))
                AttributeRow(row++, data, rates, kind);

            Header(row++, "Points spent per archetype");
            foreach (ArchetypeClass archetype in ClassTrees.Archetypes)
                ArchetypeRow(row++, data, archetype);

            ClassesTheme.SetContentHeight(_scroll.Content, row, AttributeRowHeight);
        }

        private static ProgressionRates CurrentRates() => ProgressionSettings.IsInitialized
            ? ProgressionSettings.CurrentRates
            : ProgressionSettings.DefaultRates;

        private static string Summary(ClassPlayerData data, ProgressionRates rates)
        {
            long toNext = ProgressionEngine.XpForNextLevel(data.Progress.Level, rates);
            PacePreview preview = ProgressionEngine.Preview(rates, ClassPlayerData.TotalTreeCost());
            string valor = data.Progress.ValorLevel > 0 ? "  Valor " + data.Progress.ValorLevel : "";
            return "Level " + data.Progress.Level + " / " + ProgressionEngine.MaxCharacterLevel + valor + "\n"
                + "XP " + data.Progress.Xp + " / " + toNext + "\n"
                + "Skill points " + data.Progress.UnspentSkillPoints(rates) + " unspent of "
                + data.Progress.EarnedSkillPoints(rates) + " earned, " + data.Progress.SpentSkillPoints + " spent\n"
                + "Passive slots " + data.Progress.PassiveSlots
                + "   Trees full at Valor " + preview.ValorLevelsToFillEveryTree
                + "   Front " + data.Front.Archetype + " / Back " + data.Back.Archetype;
        }

        private void Header(int index, string text)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Header", index, AttributeRowHeight);
            ClassesTheme.Text(row, "Label", text, UIFontConfig.Colors.BrightGold,
                new Vector2(0.01f, 0f), new Vector2(0.99f, 1f), ClassesTheme.RowFontSize);
        }

        private void AttributeRow(int index, ClassPlayerData data, ProgressionRates rates, AttributeKind kind)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Attr_" + kind, index, AttributeRowHeight);
            ClassesTheme.Text(row, "Name", kind + "   " + data.Progress.Attribute(kind), ClassesTheme.Ready,
                new Vector2(0.02f, 0f), new Vector2(0.6f, 1f), ClassesTheme.RowFontSize);
            ClassesTheme.Text(row, "Effect", EffectOf(kind), ClassesTheme.Dim,
                new Vector2(0.6f, 0f), new Vector2(0.88f, 1f), ClassesTheme.SmallFontSize);

            bool canBuy = data.Progress.UnspentAttributePoints(rates) > 0;
            if (!canBuy) return;
            AttributeKind bought = kind;
            ClassesTheme.Button(row, "+", new Vector2(0.9f, 0.1f), new Vector2(0.98f, 0.9f), () =>
            {
                if (!data.Progress.TryBuyAttributePoint(bought, CurrentRates())) return;
                ClassPlayerStore.SaveLocal();
                Refresh();
            });
        }

        /// <summary>Doc 12 section 8: what each attribute moves.</summary>
        private static string EffectOf(AttributeKind kind)
        {
            switch (kind)
            {
                case AttributeKind.Strength: return "physical damage, carry weight";
                case AttributeKind.Agility: return "attack and movement stamina, attack speed";
                case AttributeKind.Intellect: return "magic damage, eitr and its regen";
                case AttributeKind.Endurance: return "health, armour, health regen";
                default: return "stamina and its regen, elemental resistance";
            }
        }

        private void ArchetypeRow(int index, ClassPlayerData data, ArchetypeClass archetype)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Tree_" + archetype, index, AttributeRowHeight);
            ClassTree tree = ClassTrees.Tree(archetype);
            int spent = SpentIn(data, archetype);
            int total = tree?.TotalPointCost(ClassTrees.RankCosts) ?? 0;

            ClassesTheme.Text(row, "Name", archetype + "   " + spent + " / " + total,
                spent > 0 ? ClassesTheme.Ready : ClassesTheme.Dim,
                new Vector2(0.02f, 0f), new Vector2(0.7f, 1f), ClassesTheme.RowFontSize);

            if (spent <= 0) return;
            ArchetypeClass respec = archetype;
            ClassesTheme.Button(row, "Respec", new Vector2(0.78f, 0.1f), new Vector2(0.98f, 0.9f), () =>
            {
                List<string> passivesInTree = new List<string>();
                foreach (ClassTreeNode node in ClassTrees.PassivesFor(respec)) passivesInTree.Add(node.Id);
                data.Progress.RespecArchetype(respec, passivesInTree);
                ClassPlayerStore.SaveLocal();
                ClassesPanel.Instance?.RefreshCurrent();
            });
        }

        internal static int SpentIn(ClassPlayerData data, ArchetypeClass archetype)
        {
            if (!data.Progress.Talents.TryGetValue(archetype, out Dictionary<string, int> ranks)) return 0;
            int spent = 0;
            foreach (KeyValuePair<string, int> entry in ranks)
                for (int rank = 1; rank <= entry.Value; rank++)
                    spent += ClassTrees.RankCost(rank);
            return spent;
        }
    }
}
