using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Class, from Fire's board ClassPicker as he ruled it (Tools\CLASSES_UI_PLAN.md Q1). There is no primary /
    /// secondary class record: the archetype belongs to a bar. So the board's Primary / Secondary toggle becomes Front bar /
    /// Back bar, and the eight cards are that bar's choices. The ones its weapon can't drive are dimmed and say so.
    /// The right side previews the picked card: crest, description, signature abilities, and the hybrid each pairing
    /// makes, with the current pair in purple. "Choose X" sets the bar's archetype (BarController.TryChooseArchetype).
    ///
    /// Names: toggle "Side_Front" / "Side_Back", cards "Pick_&lt;Archetype&gt;", buttons "ChooseClass" and "CancelClass".
    /// The board's live character preview is the crest here; the 3D preview belongs to VAInventory.
    /// </summary>
    public class ClassPickerView : IClassesView
    {
        private const float Pad = 24f;
        private const float PreviewWidth = 400f;

        /// <summary>The board's short lines under each name.</summary>
        private static readonly Dictionary<ArchetypeClass, string> Taglines = new Dictionary<ArchetypeClass, string>
        {
            { ArchetypeClass.Tank, "Frontline defender" },
            { ArchetypeClass.Paladin, "Holy defender" },
            { ArchetypeClass.Berserker, "Melee damage" },
            { ArchetypeClass.Rogue, "Melee damage" },
            { ArchetypeClass.Monk, "Unarmed fighter" },
            { ArchetypeClass.Ranger, "Ranged damage" },
            { ArchetypeClass.Mage, "Magic damage" },
            { ArchetypeClass.Healer, "Support" },
            { ArchetypeClass.Druid, "Shapeshifter. Wears the beasts it hunts." },
            { ArchetypeClass.Necromancer, "Death-caller. Walks in the dead." },
            { WerewolfClass.Archetype, "Shapeshifter by blood. Needs the werewolf race." },
            { VampireClass.Archetype, "Night-hunter that feeds on blood. Needs the vampire race." },
        };

        public ClassesTab Tab => ClassesTab.Class;

        public string Title => "Class";

        private RectTransform _page;
        private RectTransform _body;
        private BarSide _side = BarSide.Front;
        private bool _sideChosen;
        private ArchetypeClass _picked = ArchetypeClass.None;

        public void Build(RectTransform page)
        {
            _page = page;
            _body = ClassesStyle.Rect(page, "ClassPicker", Vector2.zero, Vector2.one);
            _body.offsetMin = new Vector2(Pad, 12f);
            _body.offsetMax = new Vector2(-Pad, -8f);
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null || !ClassTrees.IsLoaded) return;
            if (!_sideChosen)
            {
                _side = data.ActiveBar;
                _sideChosen = true;
            }
            BarLayout bar = data.Bar(_side);
            if (_picked == ArchetypeClass.None || ClassTrees.Tree(_picked) == null)
                _picked = bar.Archetype != ArchetypeClass.None ? bar.Archetype : ClassTrees.Archetypes.First();

            ClassesStyle.Clear(_body);
            IReadOnlyList<ArchetypeClass> allowed = BarController.AllowedArchetypes(bar);

            ClassesStyle.Label(_body, "Title", "Choose your classes", 26f, ClassesStyle.Gold,
                new Vector2(0f, 1f), new Vector2(0.6f, 1f), TextAlignmentOptions.MidlineLeft, true)
                .rectTransform.offsetMin = new Vector2(0f, -40f);

            SideToggle(data);
            Cards(data, bar, allowed);
            Preview(data, bar, allowed);
        }

        // ---- Front bar / Back bar ----------------------------------------------------------------------------

        private void SideToggle(ClassPlayerData data)
        {
            float x = 0f;
            foreach (BarSide side in new[] { BarSide.Front, BarSide.Back })
            {
                bool on = side == _side;
                string caption = (side == BarSide.Front ? "Front bar" : "Back bar")
                                 + (data.Bar(side).Archetype != ArchetypeClass.None ? ": " + data.Bar(side).Archetype : "");
                BarSide chosen = side;
                Button button = ClassesStyle.Button(_body, "Side_" + side, caption,
                    on ? ClassesStyle.ButtonKind.Primary : ClassesStyle.ButtonKind.Outline,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), () =>
                    {
                        _side = chosen;
                        _picked = ArchetypeClass.None;
                        Refresh();
                    }, 16f);
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                float width = label.GetPreferredValues(caption, 999f, 40f).x + 40f;
                RectTransform rect = (RectTransform)button.transform;
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(width, ClassesStyle.Target);
                rect.anchoredPosition = new Vector2(x, -56f);
                x += width;
            }
        }

        // ---- the eight cards --------------------------------------------------------------------------------

        private void Cards(ClassPlayerData data, BarLayout bar, IReadOnlyList<ArchetypeClass> allowed)
        {
            RectTransform grid = ClassesStyle.Rect(_body, "Cards", Vector2.zero, Vector2.one);
            grid.offsetMin = new Vector2(0f, 0f);
            grid.offsetMax = new Vector2(-PreviewWidth - 24f, -116f);
            List<ArchetypeClass> archetypes = ClassTrees.Archetypes.ToList();
            int columns = 4, rows = Mathf.Max(1, Mathf.CeilToInt(archetypes.Count / (float)columns));
            for (int i = 0; i < archetypes.Count; i++)
            {
                int column = i % columns, row = i / columns;
                RectTransform cell = ClassesStyle.Rect(grid, "Pick_" + archetypes[i],
                    new Vector2((float)column / columns, 1f - (float)(row + 1) / rows),
                    new Vector2((float)(column + 1) / columns, 1f - (float)row / rows));
                cell.offsetMin = new Vector2(column == 0 ? 0f : 7f, row == rows - 1 ? 0f : 7f);
                cell.offsetMax = new Vector2(column == columns - 1 ? 0f : -7f, row == 0 ? 0f : -7f);
                Card(data, bar, cell, archetypes[i], allowed.Contains(archetypes[i]));
            }
        }

        private void Card(ClassPlayerData data, BarLayout bar, RectTransform cell, ArchetypeClass archetype, bool allowed)
        {
            bool picked = archetype == _picked;
            bool current = archetype == bar.Archetype;
            Image face = ClassesStyle.Fill(cell, ClassesStyle.Box(10, picked ? ClassesStyle.Selected : ClassesStyle.Well,
                picked ? ClassesStyle.Gold : ClassesStyle.Line, picked ? 2 : 1), Color.white, true);
            Button button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => { _picked = archetype; Refresh(); });
            if (!allowed) cell.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;

            Color accent = picked ? ClassesStyle.Gold : ClassesStyle.Muted;
            RectTransform crest = ClassesStyle.Fixed(cell, "Crest", new Vector2(0.5f, 1f), new Vector2(68f, 68f));
            crest.anchoredPosition = new Vector2(0f, -18f - 34f);
            ClassesStyle.Fill(crest, ClassesStyle.Circle(68, ClassesStyle.Well, accent, 2f), Color.white);
            RectTransform glyph = ClassesStyle.Fixed(crest, "Glyph", new Vector2(0.5f, 0.5f), new Vector2(38f, 38f));
            ClassesStyle.Fill(glyph, ClassCrests.For(archetype), picked ? ClassesStyle.Gold : ClassesStyle.Text).preserveAspect = true;

            Line(cell, "Name", ClassNames.Of(archetype), 21f, picked ? ClassesStyle.Gold : ClassesStyle.Text, 92f, 26f, picked);
            Line(cell, "Tagline", Taglines.TryGetValue(archetype, out string tag) ? tag : "", 14f, ClassesStyle.Muted, 120f, 18f);
            ClassTreeNode starter = Starter(archetype);
            if (allowed)
            {
                Line(cell, "StartsWith", current ? "On this bar now" : "Starts with", 12f,
                    current ? ClassesStyle.Gold : ClassesStyle.Muted, 146f, 16f);
                Line(cell, "Starter", starter?.DisplayName ?? "", 15f, ClassesStyle.Text, 164f, 18f);
            }
            else
            {
                Line(cell, "StartsWith", "This bar's weapon", 12f, ClassesStyle.Muted, 146f, 16f);
                Line(cell, "Starter", "can't drive it", 15f, ClassesStyle.LockedText, 164f, 18f);
            }
        }

        private static void Line(RectTransform parent, string name, string text, float size, Color color, float top, float height,
            bool bold = false)
        {
            TextMeshProUGUI label = ClassesStyle.Label(parent, name, text, size, color, new Vector2(0f, 1f), new Vector2(1f, 1f),
                TextAlignmentOptions.Center, bold);
            label.rectTransform.offsetMin = new Vector2(6f, -top - height);
            label.rectTransform.offsetMax = new Vector2(-6f, -top);
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        // ---- the preview of the picked card -----------------------------------------------------------------

        private void Preview(ClassPlayerData data, BarLayout bar, IReadOnlyList<ArchetypeClass> allowed)
        {
            RectTransform panel = ClassesStyle.Rect(_body, "Preview", new Vector2(1f, 0f), new Vector2(1f, 1f));
            panel.pivot = new Vector2(1f, 0.5f);
            panel.offsetMin = new Vector2(-PreviewWidth, 0f);
            panel.offsetMax = new Vector2(0f, -56f);
            ArchetypeClass archetype = _picked;

            // The board's live preview box: the crest, large.
            RectTransform box = ClassesStyle.Rect(panel, "Portrait", new Vector2(0f, 1f), new Vector2(1f, 1f));
            box.offsetMin = new Vector2(0f, -130f);
            box.offsetMax = Vector2.zero;
            ClassesStyle.Fill(box, ClassesStyle.Box(10, ClassesStyle.Well, ClassesStyle.Line, 1), Color.white);
            RectTransform crest = ClassesStyle.Fixed(box, "Crest", new Vector2(0.5f, 0.5f), new Vector2(100f, 100f));
            ClassesStyle.Fill(crest, ClassesStyle.Circle(100, ClassesStyle.Panel, ClassesStyle.Gold, 3f), Color.white);
            RectTransform glyph = ClassesStyle.Fixed(crest, "Glyph", new Vector2(0.5f, 0.5f), new Vector2(58f, 58f));
            ClassesStyle.Fill(glyph, ClassCrests.For(archetype), ClassesStyle.Gold).preserveAspect = true;

            float y = 140f;
            y = Row(panel, "Name", ClassNames.Of(archetype), 28f, ClassesStyle.Gold, y, 34f, true);
            ArchetypeDefinition definition = ArchetypeRegistry.GetDefinition(archetype);
            string about = (Taglines.TryGetValue(archetype, out string tag) ? tag + ". " : "") + (definition?.Description ?? "");
            y = Row(panel, "About", about, 15f, ClassesStyle.Text, y, 40f) + 6f;

            // Which weapons drive it (the bars' own rule): Fire 10-01, "clearly state what weapons are accepted".
            y = Row(panel, "WeaponsHead", "Weapons", 13f, ClassesStyle.Muted, y, 18f);
            y = Row(panel, "Weapons", WeaponArchetypes.WeaponsLine(archetype), 15f, ClassesStyle.Gold, y, 40f) + 4f;

            y = Row(panel, "AbilitiesHead", "Signature abilities", 13f, ClassesStyle.Muted, y, 18f);
            foreach (KeyValuePair<string, int> ability in Signature(archetype))
            {
                RectTransform row = ClassesStyle.Rect(panel, "Ability", new Vector2(0f, 1f), new Vector2(1f, 1f));
                row.offsetMin = new Vector2(0f, -y - 30f);
                row.offsetMax = new Vector2(0f, -y);
                RectTransform rule = ClassesStyle.Rect(row, "Rule", Vector2.zero, new Vector2(1f, 0f));
                rule.sizeDelta = new Vector2(0f, 1f);
                ClassesStyle.Fill(rule, null, ClassesStyle.Line);
                ClassesStyle.Label(row, "Name", ability.Key, 16f, ClassesStyle.Text, Vector2.zero, new Vector2(0.75f, 1f));
                ClassesStyle.Label(row, "Level", "Level " + ability.Value, 14f, ClassesStyle.Muted, new Vector2(0.6f, 0f), Vector2.one,
                    TextAlignmentOptions.MidlineRight);
                y += 30f;
            }
            y += 8f;

            // Doc 14: the hybrid is the ordered pair (front, back). Each row is the pair this pick makes with every
            // archetype on the other bar; the current pair is purple.
            ArchetypeClass other = data.Bar(ClassPlayerData.Other(_side)).Archetype;
            y = Row(panel, "HybridHead", _side == BarSide.Front ? "Hybrid with each back bar" : "Hybrid with each front bar",
                13f, ClassesStyle.Muted, y, 18f);
            foreach (ArchetypeClass partner in ClassTrees.Archetypes.Where(a => a != archetype))
            {
                HybridArchetypeDefinitions.HybridDefinition hybrid = _side == BarSide.Front
                    ? HybridArchetypeDefinitions.GetHybrid(archetype, partner)
                    : HybridArchetypeDefinitions.GetHybrid(partner, archetype);
                if (hybrid == null) continue;
                RectTransform row = ClassesStyle.Rect(panel, "Hybrid_" + partner, new Vector2(0f, 1f), new Vector2(1f, 1f));
                row.offsetMin = new Vector2(0f, -y - 20f);
                row.offsetMax = new Vector2(0f, -y);
                ClassesStyle.Label(row, "With", "with " + partner, 14f, ClassesStyle.Muted, Vector2.zero, new Vector2(0.5f, 1f));
                ClassesStyle.Label(row, "Hybrid", hybrid.DisplayName, 14f, partner == other ? ClassesStyle.Purple : ClassesStyle.Text,
                    new Vector2(0.4f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);
                y += 20f;
            }

            // Actions along the bottom.
            RectTransform actions = ClassesStyle.Rect(panel, "Actions", new Vector2(0f, 0f), new Vector2(1f, 0f));
            actions.pivot = new Vector2(0.5f, 0f);
            actions.sizeDelta = new Vector2(0f, ClassesStyle.Target);
            bool can = allowed.Contains(archetype) && bar.Archetype != archetype;
            string caption = bar.Archetype == archetype ? ClassNames.Of(archetype) + " is on this bar"
                : allowed.Contains(archetype) ? "Choose " + ClassNames.Of(archetype) : "This weapon can't drive it";
            Button choose = ClassesStyle.Button(actions, "ChooseClass", caption, ClassesStyle.ButtonKind.Primary,
                new Vector2(0.42f, 0f), Vector2.one, () =>
                {
                    if (!BarController.TryChooseArchetype(_side, _picked)) Refresh();
                }, 16f);
            ClassesStyle.Paint(choose, ClassesStyle.ButtonKind.Primary, can);
            ClassesStyle.Button(actions, "CancelClass", "Cancel", ClassesStyle.ButtonKind.Outline,
                new Vector2(0.14f, 0f), new Vector2(0.4f, 1f), () =>
                {
                    _picked = ArchetypeClass.None;
                    ClassesPanel.Instance?.Show(ClassesTab.Character);
                }, 16f);
        }

        private static float Row(RectTransform parent, string name, string text, float size, Color color, float y, float height,
            bool bold = false)
        {
            TextMeshProUGUI label = ClassesStyle.Label(parent, name, text, size, color, new Vector2(0f, 1f), new Vector2(1f, 1f),
                TextAlignmentOptions.TopLeft, bold);
            label.rectTransform.offsetMin = new Vector2(0f, -y - height);
            label.rectTransform.offsetMax = new Vector2(0f, -y);
            return y + height + 4f;
        }

        // ---- data -------------------------------------------------------------------------------------------

        /// <summary>The tree's first node (lowest level, not the dodge): what a new bar starts with.</summary>
        private static ClassTreeNode Starter(ArchetypeClass archetype) =>
            ClassTrees.Tree(archetype)?.Nodes
                .Where(n => n.Slot != NodeSlot.Evasion && n.Slot != NodeSlot.Ultimate)
                .OrderBy(n => n.MinLevel).ThenBy(n => n.Tier).FirstOrDefault();

        /// <summary>Three that sum the class up, as on the board: the starter, its first master skill, and its ultimate.</summary>
        private static List<KeyValuePair<string, int>> Signature(ArchetypeClass archetype)
        {
            var list = new List<KeyValuePair<string, int>>();
            ClassTreeNode starter = Starter(archetype);
            if (starter != null) list.Add(new KeyValuePair<string, int>(starter.DisplayName, starter.MinLevel));
            ClassTreeNode master = ClassTrees.ActivesFor(archetype).Where(n => n.Tier == NodeTier.Master)
                .OrderBy(n => n.MinLevel).FirstOrDefault()
                ?? ClassTrees.ActivesFor(archetype).OrderByDescending(n => n.MinLevel).FirstOrDefault();
            if (master != null && master != starter) list.Add(new KeyValuePair<string, int>(master.DisplayName, master.MinLevel));
            UltimateDefinition ultimate = UltimateCatalog.For(archetype).LastOrDefault();
            if (ultimate != null) list.Add(new KeyValuePair<string, int>(ultimate.DisplayName, ultimate.UnlockLevel));
            return list;
        }
    }
}
