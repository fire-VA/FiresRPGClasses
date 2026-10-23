using System.Collections.Generic;
using System.Linq;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;
using FiresCore.UI;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Passives, from the board PassivesView (doc 19's always-on grid): the equipped strip with every slot the
    /// character will ever have (locked ones say the level or Valor that opens them), filter chips, a card for every tree
    /// passive and automatic ability with its rank pips and status, and a detail well for the selected one with Equip /
    /// Unequip and "Open in tree". Passives are always on, whichever bar is active.
    ///
    /// Names: strip slots "PassiveSlot_&lt;n&gt;", cards "Passive_&lt;id&gt;", chips "Filter_&lt;name&gt;", the well's buttons "Equip" /
    /// "Unequip" and "OpenInTree". Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class PassivesView : IClassesView
    {
        private const float Pad = 24f;
        private const float HeaderHeight = 34f;
        private const float StripHeight = 144f;
        private const float ChipHeight = 36f;
        private const float WellWidth = 334f;
        private const float CardHeight = 88f;
        private const float CardGap = 10f;

        private static readonly Color IconFill = ClassesStyle.Hex("#241a0f");

        private static readonly string[] Filters = { "All", "Equipped", "Learned", "Not learned" };

        public ClassesTab Tab => ClassesTab.Passives;

        public string Title => "Passives";

        private RectTransform _page;
        private RectTransform _header;
        private RectTransform _strip;
        private RectTransform _chips;
        private RectTransform _well;
        private UIBuilderHelper.ScrollAreaResult _grid;

        private string _filter = "All";
        private string _selected;

        public void Build(RectTransform page)
        {
            _page = page;
            float top = 8f;
            _header = Band(page, "Header", top, HeaderHeight, Pad, Pad);
            top += HeaderHeight + 12f;
            _strip = Band(page, "Equipped", top, StripHeight, Pad, Pad);
            ClassesStyle.WellFrame(_strip);
            top += StripHeight + 14f;
            _chips = Band(page, "Filters", top, ChipHeight, Pad, Pad);
            top += ChipHeight + 14f;

            _well = ClassesStyle.Rect(page, "Detail", new Vector2(1f, 0f), new Vector2(1f, 1f));
            _well.pivot = new Vector2(1f, 0.5f);
            _well.offsetMin = new Vector2(-Pad - WellWidth, 14f);
            _well.offsetMax = new Vector2(-Pad, -top);
            ClassesStyle.WellFrame(_well);

            // The viewport sits 5 px in and the scrollbar takes 18 px on the right.
            _grid = UIBuilderHelper.CreateScrollArea(page, "PassiveGrid", Vector2.zero, Vector2.one, Color.clear);
            RectTransform grid = _grid.Root.GetComponent<RectTransform>();
            grid.offsetMin = new Vector2(Pad - 5f, 9f);
            grid.offsetMax = new Vector2(-Pad - WellWidth - 14f + 18f, -top + 5f);
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null || !ClassTrees.IsLoaded) return;

            List<ClassTreeNode> pool = Ordered(data);
            if (_selected == null || ClassTrees.Node(_selected) == null)
                _selected = data.Progress.PassiveLoadout.FirstOrDefault(id => ClassTrees.Node(id) != null) ?? pool.FirstOrDefault()?.Id;

            BuildHeader(data);
            BuildStrip(data);
            BuildChips();
            BuildGrid(data, pool);
            BuildWell(data);
        }

        /// <summary>Equipped first (in slot order), then learned, then the rest; each by archetype and level.</summary>
        private static List<ClassTreeNode> Ordered(ClassPlayerData data)
        {
            List<ClassTreeNode> pool = PassiveLoadout.Pool();
            List<string> loadout = data.Progress.PassiveLoadout;
            return pool
                .OrderBy(node => loadout.Contains(node.Id) ? 0 : PassiveLoadout.IsLearned(data, node) ? 1 : 2)
                .ThenBy(node => loadout.Contains(node.Id) ? loadout.IndexOf(node.Id) : 0)
                .ToList();
        }

        // ---- header ----------------------------------------------------------------------------------------

        private void BuildHeader(ClassPlayerData data)
        {
            ClassesStyle.Clear(_header);
            ClassesStyle.Label(_header, "Title", "Passives", 26f, ClassesStyle.Gold, Vector2.zero, new Vector2(0.4f, 1f),
                TextAlignmentOptions.MidlineLeft, true);

            int points = data.Progress.UnspentSkillPoints(TalentRules.Rates);
            float right = 0f;
            if (points > 0)
            {
                string text = TalentRules.Points(points) + " to spend";
                TextMeshProUGUI pill = ClassesStyle.Badge(_header, "ToSpend", text, ClassesStyle.Gold, Vector2.one, Vector2.one);
                pill.fontSize = 15f;
                float width = pill.GetPreferredValues(text, 999f, 30f).x + 22f;
                RectTransform rect = (RectTransform)pill.rectTransform.parent;
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(width, 24f);
                rect.anchoredPosition = Vector2.zero;
                right = width + 16f;
            }

            string slots = data.Progress.PassiveSlots + " of " + ProgressionEngine.MaxPassiveSlots + " slots open";
            TextMeshProUGUI open = ClassesStyle.Label(_header, "Slots", slots, 15f, ClassesStyle.Muted,
                new Vector2(0.4f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);
            open.rectTransform.offsetMax = new Vector2(-right, 0f);
            right += open.GetPreferredValues(slots, 999f, 30f).x + 16f;

            string level = data.Progress.ValorLevel > 0 ? "Valor " + data.Progress.ValorLevel : "Level " + data.Progress.Level;
            TextMeshProUGUI levelLabel = ClassesStyle.Label(_header, "Level", level, 15f, ClassesStyle.Text,
                new Vector2(0.4f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);
            levelLabel.rectTransform.offsetMax = new Vector2(-right, 0f);
        }

        // ---- the equipped strip ----------------------------------------------------------------------------

        private void BuildStrip(ClassPlayerData data)
        {
            ClassesStyle.Clear(_strip);
            TextMeshProUGUI caption = ClassesStyle.Label(_strip, "Caption", "Equipped — always on", 14f, ClassesStyle.Muted,
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            caption.rectTransform.offsetMin = new Vector2(15f, -30f);
            caption.rectTransform.offsetMax = new Vector2(-15f, -12f);

            RectTransform row = ClassesStyle.Rect(_strip, "Slots", Vector2.zero, Vector2.one);
            row.offsetMin = new Vector2(12f, 8f);
            row.offsetMax = new Vector2(-12f, -38f);

            List<string> loadout = data.Progress.PassiveLoadout;
            int open = data.Progress.PassiveSlots;
            int max = Mathf.Max(ProgressionEngine.MaxPassiveSlots, loadout.Count);
            for (int i = 0; i < max; i++)
            {
                RectTransform cell = ClassesStyle.Rect(row, "PassiveSlot_" + (i + 1),
                    new Vector2((float)i / max, 0f), new Vector2((float)(i + 1) / max, 1f));
                cell.offsetMin = new Vector2(3f, 0f);
                cell.offsetMax = new Vector2(-3f, 0f);

                ClassTreeNode node = i < loadout.Count ? ClassTrees.Node(loadout[i]) : null;
                if (node != null) EquippedSlot(data, cell, node);
                else if (i < open) EmptySlot(cell, "Empty", ClassesStyle.Line);
                else EmptySlot(cell, UnlockLabel(i + 1), ClassesStyle.Line);
            }
        }

        private void EquippedSlot(ClassPlayerData data, RectTransform cell, ClassTreeNode node)
        {
            bool selected = node.Id == _selected;
            Image face = ClassesStyle.Fill(cell, ClassesStyle.Box(8, Color.clear, selected ? ClassesStyle.Gold : Color.clear, 2),
                Color.white, true);
            Button button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            string id = node.Id;
            button.onClick.AddListener(() => Select(id));

            RectTransform frame = ClassesStyle.Fixed(cell, "Frame", new Vector2(0.5f, 1f), new Vector2(56f, 56f));
            frame.anchoredPosition = new Vector2(0f, -32f);
            Crest(frame, node, 56, PassiveLoadout.Status(node) == PassiveStatus.Active);

            TextMeshProUGUI name = ClassesStyle.Label(cell, "Name", node.DisplayName, 12f, ClassesStyle.Text,
                new Vector2(0f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Top);
            name.rectTransform.offsetMin = new Vector2(0f, 2f);
            name.rectTransform.offsetMax = new Vector2(0f, 30f);
        }

        private static void EmptySlot(RectTransform cell, string text, Color stroke)
        {
            RectTransform frame = ClassesStyle.Fixed(cell, "Frame", new Vector2(0.5f, 1f), new Vector2(56f, 56f));
            frame.anchoredPosition = new Vector2(0f, -32f);
            ClassesStyle.FillDashed(frame, 8, Color.clear, stroke, 2);
            TextMeshProUGUI label = ClassesStyle.Label(cell, "Name", text, 12f, ClassesStyle.Muted,
                new Vector2(0f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Top);
            label.rectTransform.offsetMin = new Vector2(0f, 2f);
            label.rectTransform.offsetMax = new Vector2(0f, 30f);
        }

        /// <summary>
        /// The node's icon in its archetype's colour inside a framed square, or its initials when there is no icon (the
        /// board's "SB" for Shield Block). Dimmed while an equipped passive's conditions are unmet.
        /// </summary>
        private static void Crest(RectTransform frame, ClassTreeNode node, int size, bool lit)
        {
            Color color = ClassesStyle.ArchetypeColor(node.Archetype);
            if (!lit) color = Color.Lerp(color, ClassesStyle.LockedSub, 0.55f);
            ClassesStyle.Fill(frame, ClassesStyle.Box(8, IconFill, color, 2), Color.white);
            Sprite sprite = ClassIcons.For(node.Id);
            if (sprite != null)
            {
                RectTransform glyph = ClassesStyle.Fixed(frame, "icon", new Vector2(0.5f, 0.5f), new Vector2(size * 0.6f, size * 0.6f));
                ClassesStyle.Fill(glyph, sprite, color).preserveAspect = true;
                return;
            }
            ClassesStyle.Label(frame, "Initials", Initials(node.DisplayName), size * 0.36f, color, Vector2.zero, Vector2.one,
                TextAlignmentOptions.Center, true);
        }

        private static string Initials(string name)
        {
            string[] words = (name ?? "").Split(new[] { ' ', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            return new string(words.Take(2).Select(word => char.ToUpperInvariant(word[0])).ToArray());
        }

        /// <summary>
        /// What opens slot n ("Level 80", "Valor 15"), read back from Core's own slot rule so the two can't disagree:
        /// the first character level whose slot count reaches n, else the first Valor level at max level.
        /// </summary>
        private static string UnlockLabel(int slot)
        {
            for (int level = ProgressionEngine.FirstLevel; level <= ProgressionEngine.MaxLevel; level++)
                if (ProgressionEngine.PassiveSlots(level, 0) >= slot) return "Level " + level;
            for (int valor = 1; valor <= 1000; valor++)
                if (ProgressionEngine.PassiveSlots(ProgressionEngine.MaxLevel, valor) >= slot) return "Valor " + valor;
            return "Locked";
        }

        // ---- filter chips ----------------------------------------------------------------------------------

        private void BuildChips()
        {
            ClassesStyle.Clear(_chips);
            float x = 0f;
            foreach (string filter in Filters.Concat(ClassTrees.Archetypes.Select(ClassNames.Of)))
            {
                string chosen = filter;
                Button chip = ClassesStyle.Pill(_chips, "Filter_" + filter.Replace(" ", ""), filter,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), () => { _filter = chosen; Refresh(); }, 13f);
                TMP_Text label = chip.GetComponentInChildren<TMP_Text>(true);
                float width = (label != null ? label.GetPreferredValues(filter, 999f, 30f).x : filter.Length * 7f) + 26f;
                RectTransform rect = (RectTransform)chip.transform;
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(width, ChipHeight);
                rect.anchoredPosition = new Vector2(x, 0f);
                ClassesStyle.PaintPill(chip, filter == _filter);
                x += width + 8f;
            }
        }

        private bool Shown(ClassPlayerData data, ClassTreeNode node)
        {
            switch (_filter)
            {
                case "All": return true;
                case "Equipped": return data.Progress.PassiveLoadout.Contains(node.Id);
                case "Learned": return PassiveLoadout.IsLearned(data, node);
                case "Not learned": return !PassiveLoadout.IsLearned(data, node);
                default: return ClassNames.Of(node.Archetype) == _filter;
            }
        }

        // ---- the card grid ---------------------------------------------------------------------------------

        private void BuildGrid(ClassPlayerData data, List<ClassTreeNode> pool)
        {
            ClassesStyle.Clear(_grid.Content);
            List<ClassTreeNode> shown = pool.Where(node => Shown(data, node)).ToList();
            for (int i = 0; i < shown.Count; i++) Card(data, shown[i], i / 3, i % 3);
            int rows = (shown.Count + 2) / 3;
            _grid.Content.sizeDelta = new Vector2(0f, Mathf.Max(0f, rows * (CardHeight + CardGap) - CardGap));
            if (shown.Count == 0)
            {
                TextMeshProUGUI empty = ClassesStyle.Label(_grid.Content, "Empty", "Nothing here for this filter.", 14f,
                    ClassesStyle.Muted, new Vector2(0f, 1f), new Vector2(1f, 1f));
                empty.rectTransform.offsetMin = new Vector2(4f, -30f);
                _grid.Content.sizeDelta = new Vector2(0f, 30f);
            }
        }

        private void Card(ClassPlayerData data, ClassTreeNode node, int row, int column)
        {
            bool learned = PassiveLoadout.IsLearned(data, node);
            bool selected = node.Id == _selected;
            float y = row * (CardHeight + CardGap);

            RectTransform card = ClassesStyle.Rect(_grid.Content, "Passive_" + node.Id,
                new Vector2(column / 3f, 1f), new Vector2((column + 1) / 3f, 1f));
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = new Vector2(column * CardGap / 3f, -y - CardHeight);
            card.offsetMax = new Vector2(-(2 - column) * CardGap / 3f, -y);
            Image face = ClassesStyle.Fill(card, ClassesStyle.Box(8, learned ? ClassesStyle.Well : Color.clear,
                selected ? ClassesStyle.Gold : ClassesStyle.Line, selected ? 2 : 1), Color.white, true);
            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            string id = node.Id;
            button.onClick.AddListener(() => Select(id));
            if (!learned) card.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;

            Color archetype = ClassesStyle.ArchetypeColor(node.Archetype);
            string badgeText = ClassNames.Of(node.Archetype);
            TextMeshProUGUI badge = ClassesStyle.Badge(card, "Archetype", badgeText, archetype, Vector2.one, Vector2.one);
            badge.fontSize = 11f;
            float badgeWidth = badge.GetPreferredValues(badgeText, 999f, 20f).x + 14f;
            RectTransform badgeRect = (RectTransform)badge.rectTransform.parent;
            badgeRect.pivot = Vector2.one;
            badgeRect.sizeDelta = new Vector2(badgeWidth, 17f);
            badgeRect.anchoredPosition = new Vector2(-12f, -10f);

            TextMeshProUGUI name = ClassesStyle.Label(card, "Name", node.DisplayName, 15f,
                learned ? ClassesStyle.Text : ClassesStyle.Muted, new Vector2(0f, 1f), new Vector2(1f, 1f));
            name.rectTransform.offsetMin = new Vector2(13f, -28f);
            name.rectTransform.offsetMax = new Vector2(-badgeWidth - 18f, -9f);
            name.textWrappingMode = TextWrappingModes.NoWrap;

            float x = Pips(card, node, TalentRules.Rank(data, node), 13f, 36f);
            if (node.Slot == NodeSlot.Automatic)
            {
                TextMeshProUGUI auto = ClassesStyle.Label(card, "Automatic", "automatic", 11f, ClassesStyle.Muted,
                    new Vector2(0f, 1f), new Vector2(1f, 1f));
                auto.rectTransform.offsetMin = new Vector2(x + 8f, -48f);
                auto.rectTransform.offsetMax = new Vector2(-12f, -32f);
            }

            StatusLine(data, node, out string status, out Color color);
            TextMeshProUGUI line = ClassesStyle.Label(card, "Status", status, 12f, color, new Vector2(0f, 1f), new Vector2(1f, 1f));
            line.rectTransform.offsetMin = new Vector2(13f, -72f);
            line.rectTransform.offsetMax = new Vector2(-12f, -54f);
        }

        /// <summary>Rank pips from (x, y) under the parent's top-left; returns where they end.</summary>
        private static float Pips(RectTransform parent, ClassTreeNode node, int rank, float x, float y)
        {
            const float pip = 9f, gap = 4f;
            for (int r = 0; r < node.MaxRank; r++)
            {
                RectTransform dot = ClassesStyle.Fixed(parent, "Pip" + (r + 1), new Vector2(0f, 1f), new Vector2(pip, pip));
                dot.anchoredPosition = new Vector2(x + r * (pip + gap) + pip / 2f, -y - pip / 2f);
                bool filled = r < rank;
                ClassesStyle.Fill(dot, ClassesStyle.Circle((int)pip, filled ? ClassesStyle.Gold : Color.clear, ClassesStyle.Gold, 1f),
                    Color.white);
            }
            return x + node.MaxRank * (pip + gap) - gap;
        }

        private static void StatusLine(ClassPlayerData data, ClassTreeNode node, out string text, out Color color)
        {
            switch (PassiveLoadout.Status(node))
            {
                case PassiveStatus.Active:
                    text = "Equipped · active now";
                    color = ClassesStyle.ActiveGreen;
                    return;
                case PassiveStatus.ConditionsUnmet:
                    text = "Equipped · needs a shield";
                    color = ClassesStyle.Orange;
                    return;
                case PassiveStatus.NotEquipped:
                    text = "Ready — equip it";
                    color = ClassesStyle.Gold;
                    return;
                case PassiveStatus.NoSlotsLeft:
                    text = "Learned · every slot is full";
                    color = ClassesStyle.Amber;
                    return;
                default:
                    text = "Learn in the " + ClassNames.Of(node.Archetype) + " tree"
                           + (data.Progress.Level < node.MinLevel ? " at level " + node.MinLevel : "");
                    color = ClassesStyle.Muted;
                    return;
            }
        }

        // ---- the detail well -------------------------------------------------------------------------------

        private void BuildWell(ClassPlayerData data)
        {
            ClassesStyle.Clear(_well);
            ClassTreeNode node = ClassTrees.Node(_selected);
            if (node == null)
            {
                WellText("Hint", "Pick a passive to see it here.", 14f, ClassesStyle.Muted, 16f, 40f);
                return;
            }

            bool learned = PassiveLoadout.IsLearned(data, node);
            bool equipped = data.Progress.PassiveLoadout.Contains(node.Id);
            PassiveStatus status = PassiveLoadout.Status(node);
            int rank = TalentRules.Rank(data, node);

            float y = 16f;
            WellText("Title", node.DisplayName, 22f, ClassesStyle.Gold, y, 28f, true);
            y += 37f;

            string badgeText = ClassNames.Of(node.Archetype);
            TextMeshProUGUI badge = ClassesStyle.Badge(_well, "Archetype", badgeText, ClassesStyle.ArchetypeColor(node.Archetype),
                new Vector2(0f, 1f), new Vector2(0f, 1f));
            badge.fontSize = 11f;
            float badgeWidth = badge.GetPreferredValues(badgeText, 999f, 20f).x + 14f;
            RectTransform badgeRect = (RectTransform)badge.rectTransform.parent;
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.sizeDelta = new Vector2(badgeWidth, 17f);
            badgeRect.anchoredPosition = new Vector2(17f, -y);
            float x = Pips(_well, node, rank, 17f + badgeWidth + 10f, y + 4f);
            if (node.Slot == NodeSlot.Automatic)
                WellText("Automatic", "automatic", 11f, ClassesStyle.Muted, y, 17f, false, x + 8f);
            y += 27f;

            string state = equipped ? "Equipped. Always on, on either bar."
                : learned ? "Learned. Equip it and it stays on, on either bar."
                : "Not learned yet. Learn it in the " + ClassNames.Of(node.Archetype) + " tree"
                  + (data.Progress.Level < node.MinLevel ? " from level " + node.MinLevel : "") + ".";
            y += WellText("State", state, 14f, ClassesStyle.Text, y, 0f) + 10f;

            if (status == PassiveStatus.ConditionsUnmet)
                y += WellText("Inactive", "Inactive right now: needs a shield.", 14f, ClassesStyle.Orange, y, 0f) + 10f;
            else if (status == PassiveStatus.NoSlotsLeft)
                y += WellText("Full", "Every slot is full. Unequip one to make room.", 14f, ClassesStyle.Amber, y, 0f) + 10f;

            y += WellText("Rank", "Rank " + rank + " of " + node.MaxRank, 13f, ClassesStyle.Muted, y, 18f) + 12f;
            string effectText = TalentsView.EffectText(node, Mathf.Max(1, rank));
            if (effectText.Length > 0)
                WellText("Description", effectText, 14f, ClassesStyle.Text, y, 0f);

            RectTransform actions = ClassesStyle.Rect(_well, "Actions", new Vector2(0f, 0f), new Vector2(1f, 0f));
            actions.pivot = new Vector2(0.5f, 0f);
            actions.offsetMin = new Vector2(17f, 16f);
            actions.offsetMax = new Vector2(-17f, 16f + ClassesStyle.Target);

            string id = node.Id;
            bool locked = ClassPlayerStore.LocalIsProtected;
            if (equipped)
            {
                Button unequip = ClassesStyle.Button(actions, "Unequip", "Unequip", ClassesStyle.ButtonKind.Neutral,
                    new Vector2(0f, 0f), new Vector2(0.42f, 1f), () => { PassiveLoadout.TryUnequip(id); Refresh(); });
                ClassesStyle.Paint(unequip, ClassesStyle.ButtonKind.Neutral, !locked);
            }
            else
            {
                Button equip = ClassesStyle.Button(actions, "Equip", "Equip", ClassesStyle.ButtonKind.Primary,
                    new Vector2(0f, 0f), new Vector2(0.42f, 1f), () => { PassiveLoadout.TryEquip(id); Refresh(); });
                ClassesStyle.Paint(equip, ClassesStyle.ButtonKind.Primary, status == PassiveStatus.NotEquipped && !locked);
            }
            ArchetypeClass archetype = node.Archetype;
            ClassesStyle.Button(actions, "OpenInTree", "Open in tree", ClassesStyle.ButtonKind.Outline,
                new Vector2(0.46f, 0f), new Vector2(1f, 1f), () =>
                {
                    TalentsView.Focus(archetype);
                    ClassesPanel.Instance?.Show(ClassesTab.Talents);
                });
        }

        /// <summary>A line in the well from its top-left; height 0 sizes it to its wrapped text.</summary>
        private float WellText(string name, string text, float size, Color color, float y, float height,
            bool bold = false, float x = 17f)
        {
            TextMeshProUGUI label = ClassesStyle.Label(_well, name, text, size, color, new Vector2(0f, 1f), new Vector2(1f, 1f),
                TextAlignmentOptions.TopLeft, bold);
            label.overflowMode = TextOverflowModes.Overflow;
            RectTransform rect = label.rectTransform;
            rect.pivot = new Vector2(0.5f, 1f);
            float width = WellWidth - x - 17f;
            float fit = height > 0f ? height : label.GetPreferredValues(text, width, 999f).y;
            rect.offsetMin = new Vector2(x, -y - fit);
            rect.offsetMax = new Vector2(-17f, -y);
            return fit;
        }

        private void Select(string id)
        {
            _selected = id;
            Refresh();
        }

        private static RectTransform Band(Transform parent, string name, float top, float height, float left, float right)
        {
            RectTransform rect = ClassesStyle.Rect(parent, name, new Vector2(0f, 1f), new Vector2(1f, 1f));
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }
    }
}
