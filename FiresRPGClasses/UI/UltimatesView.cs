using System.Collections.Generic;
using System.Linq;
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
    /// Classes > Ultimates, from the UltimatesView board (doc 16). On the left, every ultimate this character can reach,
    /// grouped by the front archetype, the back archetype and the hybrid pair. Each card has its cost and cooldown, which
    /// bar slots it, and its own level with the XP toward the next. On the right, the selected one's level tree: Base and
    /// Empowered on the trunk, then the two morph branches (Morph 25, Refine 40, Extend 55, Hasten 70, Capstone 85,
    /// Mastery 100), lit up to its level. "Switch morph" (out of combat) and the active morph's well sit with it.
    ///
    /// Names: cards "Ult_&lt;id&gt;", tree nodes "Node_&lt;branch&gt;_&lt;level&gt;", "SwitchMorph". Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class UltimatesView : IClassesView
    {
        private const float Pad = 18f;
        private const float ListWidth = 360f;
        private const float CardHeight = 80f;
        private const float Ring = 48f;
        private const float WellHeight = 84f;

        private static readonly Color Muted = ClassesStyle.Muted;

        public ClassesTab Tab => ClassesTab.Ultimates;

        public string Title => "Ultimates";

        private RectTransform _page;
        private UIBuilderHelper.ScrollAreaResult _list;
        private RectTransform _detail;
        private string _selected;

        public void Build(RectTransform page)
        {
            _page = page;
            _list = UIBuilderHelper.CreateScrollArea(page, "UltimateList", new Vector2(0f, 0f), new Vector2(0f, 1f), Color.clear);
            RectTransform list = _list.Root.GetComponent<RectTransform>();
            list.pivot = new Vector2(0f, 0.5f);
            list.offsetMin = new Vector2(Pad - 5f, 8f);
            list.offsetMax = new Vector2(Pad + ListWidth + 13f, -8f);

            _detail = ClassesStyle.Rect(page, "UltimateDetail", Vector2.zero, Vector2.one);
            _detail.offsetMin = new Vector2(Pad + ListWidth + 24f, 12f);
            _detail.offsetMax = new Vector2(-Pad, -12f);
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null) return;
            List<(string Heading, Color Color, List<UltimateDefinition> Items)> groups = Groups(data);
            List<UltimateDefinition> all = groups.SelectMany(g => g.Items).ToList();
            if (_selected == null || all.All(u => u.Id != _selected))
                _selected = all.FirstOrDefault(u => data.Front.UltimateId == u.Id || data.Back.UltimateId == u.Id)?.Id ?? all.FirstOrDefault()?.Id;

            BuildList(data, groups);
            BuildDetail(data, UltimateCatalog.Get(_selected));
        }

        /// <summary>Doc 16: the front and back archetypes' ultimates, plus the ordered pair's hybrids.</summary>
        private static List<(string, Color, List<UltimateDefinition>)> Groups(ClassPlayerData data)
        {
            var groups = new List<(string, Color, List<UltimateDefinition>)>();
            ArchetypeClass front = data.Front.Archetype, back = data.Back.Archetype;
            if (front != ArchetypeClass.None)
                groups.Add((front.ToString(), ClassesStyle.Gold, UltimateCatalog.For(front).ToList()));
            if (back != ArchetypeClass.None && back != front)
                groups.Add((back.ToString(), ClassesStyle.Blue, UltimateCatalog.For(back).ToList()));
            if (data.HasHybridPair)
                groups.Add(("Hybrid pair: " + front + " and " + back, ClassesStyle.Purple,
                    UltimateCatalog.ForHybrid(front, back).OrderBy(u => u.Archetype == front ? 0 : 1).ToList()));
            groups.RemoveAll(g => g.Item3.Count == 0);
            return groups;
        }

        // ---- the list --------------------------------------------------------------------------------------

        private void BuildList(ClassPlayerData data, List<(string Heading, Color Color, List<UltimateDefinition> Items)> groups)
        {
            RectTransform content = _list.Content;
            ClassesStyle.Clear(content);
            float y = 0f;
            if (groups.Count == 0)
            {
                Heading(content, "Pick a class for your bars to see their ultimates.", Muted, ref y);
            }
            foreach (var group in groups)
            {
                Heading(content, group.Heading, group.Color, ref y);
                foreach (UltimateDefinition ultimate in group.Items)
                {
                    Card(data, content, ultimate, y);
                    y += CardHeight + 6f;
                }
                y += 6f;
            }
            content.sizeDelta = new Vector2(0f, y);
        }

        private static void Heading(RectTransform content, string text, Color color, ref float y)
        {
            TextMeshProUGUI label = ClassesStyle.Label(content, "Heading", text, 14f, color, new Vector2(0f, 1f), new Vector2(1f, 1f));
            label.rectTransform.offsetMin = new Vector2(0f, -y - 20f);
            label.rectTransform.offsetMax = new Vector2(0f, -y);
            y += 24f;
        }

        private void Card(ClassPlayerData data, RectTransform content, UltimateDefinition ultimate, float y)
        {
            bool selected = ultimate.Id == _selected;
            int level = data.UltimateLevel(ultimate.Id);
            bool unlocked = data.Progress.Level >= ultimate.UnlockLevel && UltimateController.MeetsPointGate(data, ultimate);

            RectTransform card = ClassesStyle.Rect(content, "Ult_" + ultimate.Id, new Vector2(0f, 1f), new Vector2(1f, 1f));
            card.offsetMin = new Vector2(0f, -y - CardHeight);
            card.offsetMax = new Vector2(0f, -y);
            Image face = ClassesStyle.Fill(card, ClassesStyle.Box(8, selected ? ClassesStyle.Selected : ClassesStyle.Panel,
                selected ? ClassesStyle.Gold : ClassesStyle.Line, selected ? 2 : 1), Color.white, true);
            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            string id = ultimate.Id;
            button.onClick.AddListener(() => { _selected = id; Refresh(); });
            if (!unlocked) card.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;

            RectTransform glyph = ClassesStyle.Fixed(card, "icon", new Vector2(0f, 1f), new Vector2(28f, 28f));
            glyph.anchoredPosition = new Vector2(14f + 14f, -12f - 15f);
            Sprite sprite = ClassIcons.ForOrNull(ultimate.Id) ?? ClassCrests.For(ultimate.Archetype);
            Image icon = ClassesStyle.Fill(glyph, sprite, selected ? ClassesStyle.Gold : ClassesStyle.Available);
            icon.preserveAspect = true;
            icon.enabled = sprite != null;

            string bar = data.Front.UltimateId == ultimate.Id ? "Front bar" : data.Back.UltimateId == ultimate.Id ? "Back bar" : null;
            float badgeWidth = 0f;
            if (bar != null)
            {
                Color badgeColor = bar == "Front bar" ? ClassesStyle.Gold : ClassesStyle.Blue;
                TextMeshProUGUI badge = ClassesStyle.Badge(card, "BarBadge", bar, badgeColor, Vector2.one, Vector2.one);
                badge.fontSize = 11f;
                badgeWidth = badge.GetPreferredValues(bar, 999f, 20f).x + 14f;
                RectTransform rect = (RectTransform)badge.rectTransform.parent;
                rect.pivot = Vector2.one;
                rect.sizeDelta = new Vector2(badgeWidth, 16f);
                rect.anchoredPosition = new Vector2(-13f, -13f);
            }

            TextMeshProUGUI name = ClassesStyle.Label(card, "Name", NameOf(ultimate), 16f, selected ? ClassesStyle.Gold : ClassesStyle.Text,
                new Vector2(0f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.MidlineLeft, selected);
            name.rectTransform.offsetMin = new Vector2(50f, -32f);
            name.rectTransform.offsetMax = new Vector2(-badgeWidth - 20f, -11f);
            name.textWrappingMode = TextWrappingModes.NoWrap;

            TextMeshProUGUI sub = ClassesStyle.Label(card, "Sub", CostLine(ultimate, level), 12f, Muted,
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            sub.rectTransform.offsetMin = new Vector2(50f, -50f);
            sub.rectTransform.offsetMax = new Vector2(-13f, -34f);

            TextMeshProUGUI levelLabel = ClassesStyle.Label(card, "Level",
                unlocked ? "Level " + level : "Unlocks at level " + ultimate.UnlockLevel, 13f,
                unlocked ? ClassesStyle.Text : ClassesStyle.LockedText, new Vector2(0f, 1f), new Vector2(1f, 1f));
            levelLabel.rectTransform.offsetMin = new Vector2(50f, -70f);
            levelLabel.rectTransform.offsetMax = new Vector2(-150f, -53f);

            if (unlocked)
            {
                RectTransform track = ClassesStyle.Bar(card, "Xp", Vector2.one, Vector2.one, XpFraction(data, ultimate.Id, level)).parent as RectTransform;
                track.pivot = Vector2.one;
                track.anchorMin = track.anchorMax = Vector2.one;
                track.sizeDelta = new Vector2(120f, 8f);
                track.anchoredPosition = new Vector2(-14f, -58f);
            }
        }

        private static float XpFraction(ClassPlayerData data, string id, int level)
        {
            if (level >= UltimateCatalog.MaxUltimateLevel) return 1f;
            long need = UltimateController.XpForUltimateLevel(level);
            UltimateProgress progress = data.UltimateState(id);
            return need > 0 && progress != null ? Mathf.Clamp01((float)progress.Xp / need) : 0f;
        }

        // ---- the selected ultimate's tree ------------------------------------------------------------------

        private void BuildDetail(ClassPlayerData data, UltimateDefinition ultimate)
        {
            ClassesStyle.Clear(_detail);
            if (ultimate == null) return;
            int level = data.UltimateLevel(ultimate.Id);
            int morph = ActiveMorph(data, ultimate);
            bool morphsOpen = UltimateCatalog.MorphsUnlocked(level);

            // Header: name, the level line, and Switch morph (out of combat).
            TextMeshProUGUI title = ClassesStyle.Label(_detail, "Title", NameOf(ultimate), 24f, ClassesStyle.Gold,
                new Vector2(0f, 1f), new Vector2(0.65f, 1f), TextAlignmentOptions.MidlineLeft, true);
            title.rectTransform.offsetMin = new Vector2(0f, -30f);
            TextMeshProUGUI line = ClassesStyle.Label(_detail, "Line",
                "Ultimate level " + level + ". " + CostLine(ultimate, level) + ". Grows each time you cast it.", 14f, Muted,
                new Vector2(0f, 1f), new Vector2(0.75f, 1f));
            line.rectTransform.offsetMin = new Vector2(0f, -50f);
            line.rectTransform.offsetMax = new Vector2(0f, -31f);

            if (ultimate.Morphs.Count > 1)
            {
                TextMeshProUGUI note = ClassesStyle.Label(_detail, "OutOfCombat", "Out of combat", 12f, Muted,
                    Vector2.one, Vector2.one, TextAlignmentOptions.MidlineRight);
                note.rectTransform.pivot = Vector2.one;
                note.rectTransform.sizeDelta = new Vector2(84f, ClassesStyle.Target);
                note.rectTransform.anchoredPosition = Vector2.zero;
                Button swap = ClassesStyle.Button(_detail, "SwitchMorph", "Switch morph", ClassesStyle.ButtonKind.Outline,
                    Vector2.one, Vector2.one, () => SwitchMorph(ultimate), 16f);
                RectTransform rect = (RectTransform)swap.transform;
                rect.pivot = Vector2.one;
                rect.sizeDelta = new Vector2(142f, ClassesStyle.Target);
                rect.anchoredPosition = new Vector2(-92f, 0f);
                ClassesStyle.Paint(swap, ClassesStyle.ButtonKind.Outline, morphsOpen && Slotted(data, ultimate));
            }

            RectTransform tree = ClassesStyle.Rect(_detail, "Tree", Vector2.zero, Vector2.one);
            tree.offsetMin = new Vector2(0f, WellHeight + 12f);
            tree.offsetMax = new Vector2(0f, -60f);
            BuildTree(tree, ultimate, level, morph, morphsOpen, data);

            BuildWell(ultimate, level, morph);
        }

        /// <summary>
        /// Columns are the eight node levels. Base and Empowered sit on the trunk (mid-height); from Morph on, two rows
        /// (one per morph), the active one drawn gold.
        /// </summary>
        private void BuildTree(RectTransform tree, UltimateDefinition ultimate, int level, int morph, bool morphsOpen, ClassPlayerData data)
        {
            RectTransform linesRect = ClassesStyle.Rect(tree, "Lines", Vector2.zero, Vector2.one);
            UILineGraphic lines = linesRect.gameObject.AddComponent<UILineGraphic>();
            lines.raycastTarget = false;

            int columns = UltimateCatalog.NodeLevels.Length;
            int branches = Mathf.Max(1, Mathf.Min(2, ultimate.Morphs.Count));
            RectTransform[] trunk = new RectTransform[2];
            RectTransform[,] rows = new RectTransform[branches, columns];
            for (int c = 0; c < columns; c++)
            {
                float x = (c + 0.5f) / columns;
                if (c < 2)
                {
                    trunk[c] = Node(tree, "Node_Trunk_" + UltimateCatalog.NodeLevels[c], ultimate, c, -1, x, 0.5f, level, true);
                    continue;
                }
                for (int b = 0; b < branches; b++)
                {
                    float yAnchor = branches == 1 ? 0.5f : b == 0 ? 0.78f : 0.22f;
                    rows[b, c] = Node(tree, "Node_" + (b + 1) + "_" + UltimateCatalog.NodeLevels[c], ultimate, c, b, x, yAnchor,
                        level, b == morph);
                }
            }

            lines.Add(trunk[0], trunk[1], 3f, level >= UltimateCatalog.NodeLevels[1] ? ClassesStyle.Gold : ClassesStyle.Line);
            for (int b = 0; b < branches; b++)
            {
                bool active = b == morph;
                for (int c = 2; c < columns; c++)
                {
                    RectTransform from = c == 2 ? trunk[1] : rows[b, c - 1];
                    bool lit = active && level >= UltimateCatalog.NodeLevels[c];
                    lines.Add(from, rows[b, c], lit ? 3f : 2f, lit ? ClassesStyle.Gold : ClassesStyle.Line, !active && c == 2);
                }
            }

            // The note at the fork (the board's dashed bubble).
            string bar = data.Front.UltimateId == ultimate.Id ? "front bar" : data.Back.UltimateId == ultimate.Id ? "back bar" : null;
            string fork = !morphsOpen
                ? "Morphs unlock at ultimate level " + UltimateCatalog.MorphLevel + "."
                : bar != null
                    ? "Both morphs unlocked. The " + bar + " slots " + MorphName(ultimate, morph) + "."
                    : "Both morphs unlocked. Slot it on a bar to pick one.";
            RectTransform bubble = ClassesStyle.Rect(tree, "ForkNote", new Vector2(1.5f / columns, 0.5f), new Vector2(1.5f / columns, 0.5f));
            bubble.pivot = new Vector2(0.5f, 0.5f);
            bubble.sizeDelta = new Vector2(190f, 44f);
            bubble.anchoredPosition = new Vector2(30f, -46f);
            ClassesStyle.FillDashed(bubble, 8, ClassesStyle.Panel, ClassesStyle.Available, 1);
            TextMeshProUGUI text = ClassesStyle.Label(bubble, "Text", fork, 12f, ClassesStyle.Available, Vector2.zero, Vector2.one,
                TextAlignmentOptions.Center);
            text.rectTransform.offsetMin = new Vector2(8f, 2f);
            text.rectTransform.offsetMax = new Vector2(-8f, -2f);
        }

        private static RectTransform Node(RectTransform tree, string name, UltimateDefinition ultimate, int column, int branch,
            float x, float y, int level, bool activeBranch)
        {
            int nodeLevel = UltimateCatalog.NodeLevels[column];
            bool reached = level >= nodeLevel && activeBranch;
            bool reachedOther = level >= nodeLevel && !activeBranch;

            RectTransform ring = ClassesStyle.Rect(tree, name, new Vector2(x, y), new Vector2(x, y));
            ring.pivot = new Vector2(0.5f, 0.5f);
            ring.sizeDelta = new Vector2(Ring, Ring);
            Color stroke = reached ? ClassesStyle.Gold : reachedOther ? ClassesStyle.AvailableLine : ClassesStyle.LockedStroke;
            ClassesStyle.Fill(ring, ClassesStyle.Circle((int)Ring, reached ? ClassesStyle.Selected : ClassesStyle.Well, stroke,
                reached ? 3f : 2f), Color.white);
            ClassesStyle.Label(ring, "Level", nodeLevel.ToString(), 14f,
                reached ? ClassesStyle.Gold : reachedOther ? ClassesStyle.Available : ClassesStyle.LockedSub,
                Vector2.zero, Vector2.one, TextAlignmentOptions.Center, reached);

            NodeText(ultimate, column, branch, out string title, out string sub);
            TextMeshProUGUI label = ClassesStyle.Label(ring, "Name", title, 14f,
                reached || reachedOther ? ClassesStyle.Text : ClassesStyle.LockedText,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), TextAlignmentOptions.Top);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.sizeDelta = new Vector2(124f, 18f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            TextMeshProUGUI subLabel = ClassesStyle.Label(ring, "Sub", sub, 12f,
                reached || reachedOther ? Muted : ClassesStyle.LockedSub,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), TextAlignmentOptions.Top);
            subLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            subLabel.rectTransform.sizeDelta = new Vector2(124f, 16f);
            subLabel.rectTransform.anchoredPosition = new Vector2(0f, -26f);
            return ring;
        }

        /// <summary>Doc 16's eight nodes: what each adds (UltimateCatalog's numbers), the morph and capstone by name.</summary>
        private static void NodeText(UltimateDefinition ultimate, int column, int branch, out string title, out string sub)
        {
            title = UltimateCatalog.NodeNames[column];
            switch (column)
            {
                case 0: sub = Mathf.RoundToInt(UltimateCatalog.BaseStrengthFraction * 100f) + "% strength"; break;
                case 1: sub = "+" + Mathf.RoundToInt(UltimateCatalog.EmpoweredPotencyBonus * 100f) + "% potency"; break;
                case 2: title = MorphName(ultimate, branch); sub = "Morph"; break;
                case 3: sub = "Cost -" + Mathf.RoundToInt(UltimateCatalog.RefineCostReduction * 100f) + "%"; break;
                case 4: sub = "+" + Mathf.RoundToInt(UltimateCatalog.ExtendMagnitudeBonus * 100f) + "% magnitude"; break;
                case 5: sub = "Cooldown -" + Mathf.RoundToInt(UltimateCatalog.HastenCooldownReduction * 100f) + "%"; break;
                case 6:
                    title = branch >= 0 && branch < ultimate.Morphs.Count && !string.IsNullOrEmpty(ultimate.Morphs[branch].CapstoneName)
                        ? ultimate.Morphs[branch].CapstoneName : "Capstone";
                    sub = "Capstone";
                    break;
                default: sub = "Full strength"; break;
            }
        }

        private void BuildWell(UltimateDefinition ultimate, int level, int morph)
        {
            RectTransform well = ClassesStyle.Rect(_detail, "ActiveMorph", new Vector2(0f, 0f), new Vector2(1f, 0f));
            well.pivot = new Vector2(0.5f, 0f);
            well.sizeDelta = new Vector2(0f, WellHeight);
            ClassesStyle.WellFrame(well);

            UltimateMorph active = morph >= 0 && morph < ultimate.Morphs.Count ? ultimate.Morphs[morph] : null;
            bool open = UltimateCatalog.MorphsUnlocked(level);
            string heading = active == null ? NameOf(ultimate)
                : active.Name + (open ? " (active)" : " (from ultimate level " + UltimateCatalog.MorphLevel + ")");
            string effect = active != null && !string.IsNullOrEmpty(active.Effect) ? active.Effect
                : "Potency " + Mathf.RoundToInt(UltimateCatalog.PotencyFor(level) * 100f) + "% at level " + level + ".";
            string capstone = active == null || string.IsNullOrEmpty(active.CapstoneName) ? ""
                : "Capstone at " + UltimateCatalog.CapstoneLevel + ": " + active.CapstoneName
                  + (string.IsNullOrEmpty(active.CapstoneEffect) ? "." : ". " + active.CapstoneEffect);

            WellLine(well, "Heading", heading, 16f, ClassesStyle.Gold, 10f, true);
            WellLine(well, "Effect", effect, 14f, ClassesStyle.Text, 33f);
            if (capstone.Length > 0) WellLine(well, "Capstone", capstone, 13f, Muted, 54f);
        }

        private static void WellLine(RectTransform well, string name, string text, float size, Color color, float top, bool bold = false)
        {
            TextMeshProUGUI label = ClassesStyle.Label(well, name, text, size, color, new Vector2(0f, 1f), new Vector2(1f, 1f),
                TextAlignmentOptions.MidlineLeft, bold);
            label.rectTransform.offsetMin = new Vector2(15f, -top - 20f);
            label.rectTransform.offsetMax = new Vector2(-15f, -top);
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>The morph a bar slots it with (front first), else the first.</summary>
        private static int ActiveMorph(ClassPlayerData data, UltimateDefinition ultimate)
        {
            if (data.Front.UltimateId == ultimate.Id) return data.Front.UltimateMorph;
            if (data.Back.UltimateId == ultimate.Id) return data.Back.UltimateMorph;
            return 0;
        }

        private static bool Slotted(ClassPlayerData data, UltimateDefinition ultimate) =>
            data.Front.UltimateId == ultimate.Id || data.Back.UltimateId == ultimate.Id;

        private static string MorphName(UltimateDefinition ultimate, int index) =>
            index >= 0 && index < ultimate.Morphs.Count && !string.IsNullOrEmpty(ultimate.Morphs[index].Name)
                ? ultimate.Morphs[index].Name : "Morph " + (index + 1);

        /// <summary>A hybrid's catalog name is "High Priest - Mass Restoration": the card shows the ability.</summary>
        private static string NameOf(UltimateDefinition ultimate)
        {
            int dash = ultimate.DisplayName.IndexOf(" - ", System.StringComparison.Ordinal);
            return ultimate.IsHybrid && dash >= 0 ? ultimate.DisplayName.Substring(dash + 3) : ultimate.DisplayName;
        }

        private static string CostLine(UltimateDefinition ultimate, int level)
        {
            string prefix = "";
            int dash = ultimate.DisplayName.IndexOf(" - ", System.StringComparison.Ordinal);
            if (ultimate.IsHybrid && dash >= 0) prefix = ultimate.DisplayName.Substring(0, dash) + ". ";
            string cost = "";
            if (ultimate.Cost != null)
            {
                float amount = UltimateController.EffectiveCost(ultimate, level);
                cost = ultimate.Cost.IsPercentOfMax
                    ? Mathf.RoundToInt(amount) + "% " + ultimate.Cost.Resource.ToString().ToLowerInvariant()
                    : Mathf.RoundToInt(amount) + " " + ultimate.Cost.Resource.ToString().ToLowerInvariant();
            }
            return prefix + cost + (cost.Length > 0 ? ", " : "")
                   + Mathf.RoundToInt(UltimateController.EffectiveCooldown(ultimate, level)) + " s cooldown";
        }

        /// <summary>Each bar that slots it moves to its next morph (doc 16: out of combat only).</summary>
        private void SwitchMorph(UltimateDefinition ultimate)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (data == null) return;
            if (player != null && player.InAttack())
            {
                player.Message(MessageHud.MessageType.Center, "Morphs switch out of combat");
                return;
            }
            foreach (BarSide side in new[] { BarSide.Front, BarSide.Back })
            {
                BarLayout bar = data.Bar(side);
                if (bar.UltimateId != ultimate.Id) continue;
                int next = (bar.UltimateMorph + 1) % Mathf.Max(1, ultimate.Morphs.Count);
                BarController.TrySlotUltimate(side, ultimate.Id, next);
            }
            Refresh();
        }
    }
}
