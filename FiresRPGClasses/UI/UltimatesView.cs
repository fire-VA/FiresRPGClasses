using System.Collections.Generic;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 16's Ultimates board: every ultimate this character can reach, its own level and node, its morphs,
    /// and which bar slots it. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class UltimatesView : IClassesView
    {
        private const float HeaderTextHeight = 40f;
        private const float UltimateRowHeight = 58f;

        public ClassesTab Tab => ClassesTab.Ultimates;

        public string Title => "Ultimates";

        private RectTransform _page;
        private UIBuilderHelper.ScrollAreaResult _scroll;
        private TextMeshProUGUI _header;

        public void Build(RectTransform page)
        {
            _page = page;
            _header = ClassesTheme.Text(page, "UltimateHeader", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.SmallFontSize);
            _header.rectTransform.Fill(-6f, HeaderTextHeight);
            _header.alignment = TextAlignmentOptions.TopLeft;

            _scroll = UIBuilderHelper.CreateScrollArea(page, "UltimateScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            _scroll.Root.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -(HeaderTextHeight + 10f));
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null) return;

            bool slotOpen = data.Progress.Level >= UltimateCatalog.SlotUnlockLevel;
            _header.text = slotOpen
                ? "Slot 8 is open. Each ultimate levels by use, 1 to 100; morphs unlock at "
                  + UltimateCatalog.MorphLevel + " and a capstone at " + UltimateCatalog.CapstoneLevel + "."
                : "Slot 8 unlocks at character level " + UltimateCatalog.SlotUnlockLevel + ".";

            ClassesTheme.ClearChildren(_scroll.Content);
            int row = 0;
            foreach (UltimateDefinition definition in Reachable(data))
                row = UltimateRow(row, data, definition);
            ClassesTheme.SetContentHeight(_scroll.Content, row, UltimateRowHeight);
        }

        /// <summary>Doc 16: the front and back archetypes' four, plus the ordered pair's two.</summary>
        private static IEnumerable<UltimateDefinition> Reachable(ClassPlayerData data)
        {
            List<UltimateDefinition> list = new List<UltimateDefinition>();
            list.AddRange(UltimateCatalog.For(data.Front.Archetype));
            if (data.Back.Archetype != data.Front.Archetype)
                list.AddRange(UltimateCatalog.For(data.Back.Archetype));
            if (data.HasHybridPair)
                list.AddRange(UltimateCatalog.ForHybrid(data.Front.Archetype, data.Back.Archetype));
            return list;
        }

        private int UltimateRow(int index, ClassPlayerData data, UltimateDefinition definition)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Ult_" + definition.Id, index, UltimateRowHeight);
            int level = data.UltimateLevel(definition.Id);
            bool unlocked = data.Progress.Level >= definition.UnlockLevel
                && UltimateController.MeetsPointGate(data, definition);

            string node = UltimateCatalog.NodeNames[UltimateCatalog.NodeIndexFor(level)];
            ClassesTheme.Text(row, "Name",
                definition.DisplayName + "   ultimate level " + level + " (" + node + ")",
                unlocked ? ClassesTheme.Ready : ClassesTheme.Locked,
                new Vector2(0.02f, 0.55f), new Vector2(0.74f, 1f), ClassesTheme.RowFontSize);

            ClassesTheme.Text(row, "Cost",
                definition.Cost + ", " + UltimateController.EffectiveCooldown(definition, level) + " s cooldown"
                + "   unlocks at level " + definition.UnlockLevel
                + "   potency " + Mathf.RoundToInt(UltimateCatalog.PotencyFor(level) * 100f) + "%",
                ClassesTheme.Dim,
                new Vector2(0.02f, 0.28f), new Vector2(0.74f, 0.58f), ClassesTheme.SmallFontSize);

            ClassesTheme.Text(row, "Morphs", MorphText(definition, level), ClassesTheme.Cooling,
                new Vector2(0.02f, 0f), new Vector2(0.74f, 0.3f), ClassesTheme.SmallFontSize);

            if (unlocked)
            {
                UltimateDefinition slotted = definition;
                ClassesTheme.Button(row, "Front", new Vector2(0.76f, 0.55f), new Vector2(0.87f, 0.95f),
                    () => Slot(BarSide.Front, slotted));
                ClassesTheme.Button(row, "Back", new Vector2(0.88f, 0.55f), new Vector2(0.99f, 0.95f),
                    () => Slot(BarSide.Back, slotted));

                if (UltimateCatalog.MorphsUnlocked(level) && definition.Morphs.Count > 1)
                    ClassesTheme.Button(row, "Swap morph", new Vector2(0.76f, 0.08f), new Vector2(0.99f, 0.5f),
                        () => SwapMorph(data, slotted));
            }
            return index + 1;
        }

        private static string MorphText(UltimateDefinition definition, int level)
        {
            if (!UltimateCatalog.MorphsUnlocked(level))
                return "morphs unlock at ultimate level " + UltimateCatalog.MorphLevel;
            List<string> parts = new List<string>();
            foreach (UltimateMorph morph in definition.Morphs)
                parts.Add(morph.Name + " -> " + morph.CapstoneName);
            return string.Join("   |   ", parts.ToArray());
        }

        private static void Slot(BarSide side, UltimateDefinition definition)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            int morph = data.Bar(side).UltimateId == definition.Id ? data.Bar(side).UltimateMorph : 0;
            BarController.TrySlotUltimate(side, definition.Id, morph);
            ClassesPanel.Instance?.RefreshCurrent();
        }

        private static void SwapMorph(ClassPlayerData data, UltimateDefinition definition)
        {
            Player player = Player.m_localPlayer;
            if (player != null && player.InAttack())
            {
                player.Message(MessageHud.MessageType.Center, "morphs switch out of combat");
                return;
            }
            foreach (BarSide side in new[] { BarSide.Front, BarSide.Back })
            {
                BarLayout bar = data.Bar(side);
                if (bar.UltimateId != definition.Id) continue;
                int next = (bar.UltimateMorph + 1) % Mathf.Max(1, definition.Morphs.Count);
                BarController.TrySlotUltimate(side, definition.Id, next);
            }
            ClassesPanel.Instance?.RefreshCurrent();
        }
    }
}
