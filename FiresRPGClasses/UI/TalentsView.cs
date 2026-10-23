using System.Collections.Generic;
using System.Linq;
using System.Text;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Npc.Archetypes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Talents: tree layout T3 (boards TreePaths and ClassTank..ClassHealer). A crest strip on the left picks
    /// the archetype; the tree has the tier labels down the left, three path columns with their
    /// headers, and each node as a ring with its icon, rank pips, name and level. Two nodes of one variant group share a
    /// dashed "Variants" box. Connectors are one mesh (<see cref="UILineGraphic"/>). Clicking a node opens its card:
    /// this rank and the next, cost, and "Learn for N points". Reset previews the refund before it runs.
    ///
    /// class_test (ui_points) finds a node as "Node_&lt;id&gt;" and learns through the card's "Learn" button; the crest
    /// buttons' first text is the archetype name. Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class TalentsView : IClassesView, IClassesOverlay
    {
        private const float StripWidth = 176f;
        private const float HeaderHeight = 76f;
        private const float LegendHeight = 40f;
        private const float TierLabelWidth = 128f;
        private const float PathHeaderHeight = 54f;
        // Hosted in the Fires hub this could use the shared kit NodeLayout (pitch 118), but that layout overlapped nodes, so it
        // stays OFF until a scrolling graph canvas exists; hosted pages are the old page size scaled to fit, so the known-good node numbers below apply in the hub too.
        private static bool Hub => false;
        private static float NodeWidth => Hub ? 150f : 112f;
        private static float NodeHeight => Hub ? FiresCore.UI.Shell.Kit.NodeLayout.Pitch : 96f;
        private static float RingSize => Hub ? FiresCore.UI.Shell.Kit.NodeLayout.Ring : 44f;
        private const float CardWidth = 340f;

        private static readonly NodeTier[] Tiers =
            { NodeTier.Starter, NodeTier.Basic, NodeTier.Advanced, NodeTier.Expert, NodeTier.Master, NodeTier.Ultimate };

        public ClassesTab Tab => ClassesTab.Talents;

        public string Title => "Talents";

        private RectTransform _page;
        private RectTransform _strip;
        private RectTransform _header;
        private RectTransform _tree;
        private RectTransform _grid;
        private UILineGraphic _lines;
        private RectTransform _card;
        private ArchetypeClass _shown = ArchetypeClass.None;
        private string _selected;
        private bool _confirmingReset;
        private float _nodeScale = 1f;

        /// <summary>The row a node sits in: ultimates in the Ultimates row whatever tier the data gives them (e.g. Tank's
        /// Hold the Line is tier "starter" in the yaml and landed in the Starter row).</summary>
        private static NodeTier RowOf(ClassTreeNode node) => TalentRules.IsUltimate(node) ? NodeTier.Ultimate : node.Tier;

        /// <summary>An archetype asked for from elsewhere (Character's "Open tree"), taken on the next Refresh.</summary>
        private static ArchetypeClass _focus = ArchetypeClass.None;

        /// <summary>Shows this archetype's tree the next time the board refreshes.</summary>
        public static void Focus(ArchetypeClass archetype) => _focus = archetype;

        /// <summary>Esc or a background click: closes the open card (or the reset prompt); false when nothing was open.</summary>
        public bool CloseOverlay()
        {
            if (_confirmingReset)
            {
                _confirmingReset = false;
                Refresh();
                return true;
            }
            if (_selected == null) return false;
            _selected = null;
            BuildCard(ClassPlayerStore.Local);
            RepaintSelection();
            return true;
        }

        /// <summary>Ring rects by node id, for the connectors and for placing the card next to a node.</summary>
        private readonly Dictionary<string, RectTransform> _rings = new Dictionary<string, RectTransform>();

        public void Build(RectTransform page)
        {
            _page = page;

            // A click on the window's empty background closes the open card. This sits under everything on the
            // page, so it only gets clicks nothing else took.
            RectTransform backdrop = ClassesStyle.Rect(page, "Backdrop", Vector2.zero, Vector2.one);
            Button close = backdrop.gameObject.AddComponent<Button>();
            close.targetGraphic = ClassesStyle.Fill(backdrop, null, new Color(0f, 0f, 0f, 0.001f), true);
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(() => CloseOverlay());

            _strip = ClassesStyle.Rect(page, "ArchetypeStrip", new Vector2(0f, 0f), new Vector2(0f, 1f));
            _strip.pivot = new Vector2(0f, 0.5f);
            _strip.sizeDelta = new Vector2(StripWidth, -16f);
            _strip.anchoredPosition = new Vector2(12f, 0f);

            RectTransform body = ClassesStyle.Rect(page, "TreeBody", Vector2.zero, Vector2.one);
            body.offsetMin = new Vector2(StripWidth + 24f, 8f);
            body.offsetMax = new Vector2(-12f, -8f);

            _header = ClassesStyle.Rect(body, "TreeHeader", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _header.pivot = new Vector2(0.5f, 1f);
            _header.sizeDelta = new Vector2(0f, HeaderHeight);

            _tree = ClassesStyle.Rect(body, "Tree", Vector2.zero, Vector2.one);
            _tree.offsetMin = new Vector2(0f, LegendHeight + 6f);
            _tree.offsetMax = new Vector2(0f, -(HeaderHeight + 4f));

            RectTransform legend = ClassesStyle.Rect(body, "Legend", new Vector2(0f, 0f), new Vector2(1f, 0f));
            legend.pivot = new Vector2(0.5f, 0f);
            legend.sizeDelta = new Vector2(0f, LegendHeight);
            BuildLegend(legend);
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null || !ClassTrees.IsLoaded) return;
            if (_focus != ArchetypeClass.None && ClassTrees.Tree(_focus) != null)
            {
                _shown = _focus;
                _selected = null;
                _confirmingReset = false;
            }
            _focus = ArchetypeClass.None;

            if (_shown == ArchetypeClass.None || ClassTrees.Tree(_shown) == null)
                _shown = ClassTrees.Tree(data.Front.Archetype) != null ? data.Front.Archetype : ClassTrees.Archetypes.First();
            ClassTree tree = ClassTrees.Tree(_shown);
            if (tree == null) return;
            if (_selected != null && tree.Nodes.All(node => node.Id != _selected)) _selected = null;

            BuildStrip(data);
            BuildHeader(data, tree);
            BuildTree(data, tree);
            BuildCard(data);
        }

        // ---- crest strip -----------------------------------------------------------------------------------

        private void BuildStrip(ClassPlayerData data)
        {
            ClassesStyle.Clear(_strip);
            List<ArchetypeClass> archetypes = ClassTrees.Archetypes.ToList();
            int count = Mathf.Max(1, archetypes.Count);
            for (int i = 0; i < archetypes.Count; i++)
            {
                ArchetypeClass archetype = archetypes[i];
                bool active = archetype == _shown;
                float top = 1f - (float)i / count, bottom = 1f - (float)(i + 1) / count;

                RectTransform row = ClassesStyle.Rect(_strip, "Arch_" + archetype, new Vector2(0f, bottom), new Vector2(1f, top));
                row.offsetMin = new Vector2(0f, 3f);
                row.offsetMax = new Vector2(0f, -3f);
                Image face = ClassesStyle.Fill(row, active
                    ? ClassesStyle.Box(10, ClassesStyle.Selected, ClassesStyle.Gold, 2)
                    : ClassesStyle.Box(10, ClassesStyle.Well, ClassesStyle.Line, 1), Color.white, true);
                Button button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                ArchetypeClass picked = archetype;
                button.onClick.AddListener(() => { _shown = picked; _selected = null; _confirmingReset = false; Refresh(); });

                // The archetype name is the button's FIRST text: class_test clicks the crest labelled "Tank".
                ClassesStyle.Label(row, "Name", ClassNames.Of(archetype), 16f, active ? ClassesStyle.Gold : ClassesStyle.Text,
                    new Vector2(0f, 0.45f), new Vector2(1f, 0.95f), TextAlignmentOptions.BottomLeft, true)
                    .rectTransform.offsetMin = new Vector2(52f, 0f);

                ClassTree tree = ClassTrees.Tree(archetype);
                int spent = CharacterView.SpentIn(data, archetype);
                int total = tree?.TotalPointCost(ClassTrees.RankCosts) ?? 0;
                ClassesStyle.Label(row, "Points", spent + " / " + total, 12f, ClassesStyle.Muted,
                    new Vector2(0f, 0.05f), new Vector2(1f, 0.45f), TextAlignmentOptions.TopLeft)
                    .rectTransform.offsetMin = new Vector2(52f, 0f);

                RectTransform crest = ClassesStyle.Fixed(row, "Crest", new Vector2(0f, 0.5f), new Vector2(38f, 38f));
                crest.anchoredPosition = new Vector2(27f, 0f);
                Color crestColor = archetype == data.Front.Archetype ? ClassesStyle.Gold
                    : archetype == data.Back.Archetype ? ClassesStyle.Blue
                    : active ? ClassesStyle.Gold : ClassesStyle.Muted;
                ClassesStyle.Fill(crest, ClassesStyle.Circle(38, ClassesStyle.Panel, crestColor, 2f), Color.white);
                RectTransform glyph = ClassesStyle.Fixed(crest, "Glyph", new Vector2(0.5f, 0.5f), new Vector2(22f, 22f));
                ClassesStyle.Fill(glyph, ClassCrests.For(archetype), crestColor);

                string bar = archetype == data.Front.Archetype ? "Front" : archetype == data.Back.Archetype ? "Back" : null;
                if (bar != null)
                {
                    ClassesStyle.Badge(row, "BarBadge", bar, bar == "Front" ? ClassesStyle.Gold : ClassesStyle.Blue,
                        new Vector2(1f, 0.55f), new Vector2(1f, 0.92f)).rectTransform.parent.GetComponent<RectTransform>()
                        .offsetMin = new Vector2(-48f, 0f);
                }
            }
        }

        // ---- header: name, points, reset with preview ------------------------------------------------------

        private void BuildHeader(ClassPlayerData data, ClassTree tree)
        {
            ClassesStyle.Clear(_header);
            int unspent = data.Progress.UnspentSkillPoints(TalentRules.Rates);

            // Two rows: the name, points and Reset on top; under them, the weapons this class takes, stated
            // plainly. Read from the bars' own rule, WeaponArchetypes.
            RectTransform top = ClassesStyle.Rect(_header, "Top", new Vector2(0f, 0.38f), Vector2.one);
            RectTransform weapons = ClassesStyle.Rect(_header, "Weapons", Vector2.zero, new Vector2(1f, 0.38f));
            ClassesStyle.Label(weapons, "Text", "<color=#f0b44c>Weapons:</color>  " + WeaponArchetypes.WeaponsLine(_shown), 14f,
                ClassesStyle.Text, Vector2.zero, Vector2.one);

            if (_confirmingReset)
            {
                Dictionary<string, int> refund = TalentRules.SpentByPath(data, _shown);
                int total = refund.Values.Sum();
                var parts = new List<string>();
                foreach (ClassTreePath path in tree.Paths)
                    if (refund.TryGetValue(path.Id, out int points)) parts.Add(path.DisplayName + " " + points);
                if (refund.TryGetValue(ClassTreeNode.AllPaths, out int starter)) parts.Add("starter " + starter);
                string text = total > 0
                    ? "Reset " + ClassNames.Of(_shown) + "? Refunds " + TalentRules.Points(total) + " (" + string.Join(", ", parts.ToArray())
                        + ") and unequips this tree's passives."
                    : "Nothing to reset: no points spent in " + ClassNames.Of(_shown) + ".";
                ClassesStyle.Label(top, "ResetPreview", text, 15f, ClassesStyle.Amber,
                    new Vector2(0f, 0f), new Vector2(0.62f, 1f));
                Button confirm = ClassesStyle.Button(top, "ConfirmReset", "Confirm reset", ClassesStyle.ButtonKind.Primary,
                    new Vector2(0.64f, 0.08f), new Vector2(0.82f, 0.92f), () =>
                    {
                        TalentRules.Reset(ClassPlayerStore.Local, _shown);
                        _confirmingReset = false;
                        ClassesPanel.Instance?.RefreshCurrent();
                        Refresh();
                    });
                ClassesStyle.Paint(confirm, ClassesStyle.ButtonKind.Primary, total > 0 && !ClassPlayerStore.LocalIsProtected);
                ClassesStyle.Button(top, "CancelReset", "Cancel", ClassesStyle.ButtonKind.Outline,
                    new Vector2(0.84f, 0.08f), new Vector2(1f, 0.92f), () => { _confirmingReset = false; Refresh(); });
                return;
            }

            ClassesStyle.Label(top, "Title", _shown.ToString(), 24f, ClassesStyle.Gold,
                new Vector2(0f, 0f), new Vector2(0.3f, 1f), TextAlignmentOptions.MidlineLeft, true);
            ClassesStyle.Label(top, "Summary", "Level " + data.Progress.Level + "   ·   "
                    + CharacterView.SpentIn(data, _shown) + " points spent here", 14f, ClassesStyle.Muted,
                new Vector2(0.22f, 0f), new Vector2(0.62f, 1f));

            ClassesStyle.Label(top, "PointsLabel", "Points", 14f, ClassesStyle.Muted,
                new Vector2(0.62f, 0f), new Vector2(0.72f, 1f), TextAlignmentOptions.MidlineRight);
            ClassesStyle.Label(top, "Points", unspent.ToString(), 22f, ClassesStyle.Gold,
                new Vector2(0.73f, 0f), new Vector2(0.8f, 1f), TextAlignmentOptions.MidlineLeft, true);
            Button reset = ClassesStyle.Button(top, "Reset", "Reset", ClassesStyle.ButtonKind.Outline,
                new Vector2(0.84f, 0.08f), new Vector2(1f, 0.92f), () => { _confirmingReset = true; Refresh(); });
            ClassesStyle.Paint(reset, ClassesStyle.ButtonKind.Outline, !ClassPlayerStore.LocalIsProtected);
        }

        // ---- the tree --------------------------------------------------------------------------------------

        private void BuildTree(ClassPlayerData data, ClassTree tree)
        {
            ClassesStyle.Clear(_tree);
            _rings.Clear();
            _card = null;

            // Tier labels down the left.
            RectTransform labels = ClassesStyle.Rect(_tree, "Tiers", new Vector2(0f, 0f), new Vector2(0f, 1f));
            labels.pivot = new Vector2(0f, 0.5f);
            labels.sizeDelta = new Vector2(TierLabelWidth, -PathHeaderHeight);
            labels.anchoredPosition = new Vector2(0f, -PathHeaderHeight / 2f);
            for (int row = 0; row < Tiers.Length; row++)
            {
                RectTransform cell = RowRect(labels, "Tier_" + Tiers[row], row);
                ClassesStyle.Label(cell, "Name", TierName(Tiers[row]), 16f, ClassesStyle.Text,
                    new Vector2(0f, 0.5f), new Vector2(1f, 0.9f), TextAlignmentOptions.BottomLeft, true);
                ClassesStyle.Label(cell, "Levels", TierLevels(tree, Tiers[row]), 12f, ClassesStyle.Muted,
                    new Vector2(0f, 0.1f), new Vector2(1f, 0.5f), TextAlignmentOptions.TopLeft);
            }

            // Path headers over the columns.
            RectTransform headers = ClassesStyle.Rect(_tree, "PathHeaders", new Vector2(0f, 1f), new Vector2(1f, 1f));
            headers.pivot = new Vector2(0.5f, 1f);
            headers.offsetMin = new Vector2(TierLabelWidth, -PathHeaderHeight);
            headers.offsetMax = Vector2.zero;
            List<ClassTreePath> paths = tree.Paths.Take(3).ToList();
            int columns = Mathf.Max(1, paths.Count);
            for (int c = 0; c < paths.Count; c++)
            {
                RectTransform head = ClassesStyle.Rect(headers, "Path_" + paths[c].Id,
                    new Vector2((float)c / columns, 0f), new Vector2((float)(c + 1) / columns, 1f));
                head.offsetMin = new Vector2(8f, 6f);
                head.offsetMax = new Vector2(-8f, 0f);
                bool progressed = tree.Nodes.Any(n => n.PathId == paths[c].Id && TalentRules.Rank(data, n) > 0);
                ClassesStyle.Label(head, "Name", paths[c].DisplayName, 19f, ClassesStyle.Gold,
                    new Vector2(0f, 0.45f), new Vector2(1f, 1f), TextAlignmentOptions.Center, true);
                ClassesStyle.Label(head, "Theme", paths[c].Theme, 13f, ClassesStyle.Muted,
                    new Vector2(0f, 0.08f), new Vector2(1f, 0.48f), TextAlignmentOptions.Center);
                RectTransform rule = ClassesStyle.Rect(head, "Underline", new Vector2(0f, 0f), new Vector2(1f, 0f));
                rule.sizeDelta = new Vector2(0f, 2f);
                ClassesStyle.Fill(rule, null, progressed ? ClassesStyle.Gold : ClassesStyle.Border);
            }

            // The grid: rows = tiers, columns = paths; the connectors sit under the nodes.
            _grid = ClassesStyle.Rect(_tree, "Grid", Vector2.zero, Vector2.one);
            _grid.offsetMin = new Vector2(TierLabelWidth, 0f);
            _grid.offsetMax = new Vector2(0f, -PathHeaderHeight);
            // Six tiers share the tree's height; at the default window a row is ~75 px, less than a full-size node (96),
            // which made nodes overlap the rows around them. Nodes shrink to fit their row, never grow past 1.
            float rowHeight = _grid.rect.height / Tiers.Length;
            _nodeScale = rowHeight > 1f ? Mathf.Clamp(rowHeight / (NodeHeight + 6f), 0.55f, 1f) : 1f;

            RectTransform linesRect = ClassesStyle.Rect(_grid, "Lines", Vector2.zero, Vector2.one);
            _lines = linesRect.gameObject.AddComponent<UILineGraphic>();
            _lines.raycastTarget = false;

            // Cells: a spanning cell for the starter and ultimate tiers, one per path otherwise.
            var cellNodes = new List<KeyValuePair<RectTransform, List<ClassTreeNode>>>();
            for (int row = 0; row < Tiers.Length; row++)
            {
                NodeTier tier = Tiers[row];
                List<ClassTreeNode> inTier = tree.Nodes.Where(n => RowOf(n) == tier).OrderBy(n => n.MinLevel).ToList();
                if (inTier.Count == 0) continue;
                bool spans = tier == NodeTier.Starter || tier == NodeTier.Ultimate
                             || inTier.All(n => n.PathId == ClassTreeNode.AllPaths);
                if (spans)
                {
                    RectTransform cell = RowRect(_grid, "Cell_" + tier, row);
                    cellNodes.Add(new KeyValuePair<RectTransform, List<ClassTreeNode>>(cell, inTier));
                    continue;
                }
                for (int c = 0; c < paths.Count; c++)
                {
                    List<ClassTreeNode> here = inTier.Where(n => n.PathId == paths[c].Id).ToList();
                    if (here.Count == 0) continue;
                    RectTransform cell = RowRect(_grid, "Cell_" + tier + "_" + paths[c].Id, row);
                    cell.anchorMin = new Vector2((float)c / columns, cell.anchorMin.y);
                    cell.anchorMax = new Vector2((float)(c + 1) / columns, cell.anchorMax.y);
                    cellNodes.Add(new KeyValuePair<RectTransform, List<ClassTreeNode>>(cell, here));
                }
                // Nodes of this tier on no listed path (data drift) still show, in the middle.
                List<ClassTreeNode> stray = inTier.Where(n => n.PathId != ClassTreeNode.AllPaths
                                                               && paths.All(p => p.Id != n.PathId)).ToList();
                if (stray.Count > 0)
                    cellNodes.Add(new KeyValuePair<RectTransform, List<ClassTreeNode>>(RowRect(_grid, "Cell_" + tier + "_other", row), stray));
            }

            foreach (KeyValuePair<RectTransform, List<ClassTreeNode>> cell in cellNodes)
                PlaceNodes(data, cell.Key, cell.Value);

            AddConnectors(data, tree, paths);

            // The card goes last so it draws over the tree.
            _card = ClassesStyle.Rect(_tree, "NodeCard", new Vector2(0f, 1f), new Vector2(0f, 1f));
            _card.pivot = new Vector2(0f, 1f);
            _card.gameObject.SetActive(false);
        }

        private static RectTransform RowRect(RectTransform parent, string name, int row)
        {
            float top = 1f - (float)row / Tiers.Length, bottom = 1f - (float)(row + 1) / Tiers.Length;
            return ClassesStyle.Rect(parent, name, new Vector2(0f, bottom), new Vector2(1f, top));
        }

        private void PlaceNodes(ClassPlayerData data, RectTransform cell, List<ClassTreeNode> nodes)
        {
            int count = nodes.Count;
            float spacing = (NodeWidth + 12f) * _nodeScale;
            float first = -(count - 1) * spacing / 2f;

            // A variant group (both learnable, one slotted) gets the boards' box. Its caption lives in the legend: inside a
            // tier row there is no room for a line of text without it running into the row above.
            bool variants = count > 1 && nodes.All(n => !string.IsNullOrEmpty(n.VariantGroup))
                            && nodes.Select(n => n.VariantGroup).Distinct().Count() == 1;
            if (variants)
            {
                RectTransform box = ClassesStyle.Fixed(cell, "Variants", new Vector2(0.5f, 0.5f),
                    new Vector2(count * spacing + 4f, (NodeHeight + 6f) * _nodeScale));
                ClassesStyle.Fill(box, ClassesStyle.Box(12, ClassesStyle.Panel, ClassesStyle.Available, 1), new Color(1f, 1f, 1f, 0.9f));
            }

            for (int i = 0; i < count; i++)
                BuildNode(data, cell, nodes[i], new Vector2(first + i * spacing, 0f));
        }

        private void BuildNode(ClassPlayerData data, RectTransform cell, ClassTreeNode node, Vector2 offset)
        {
            NodeState state = TalentRules.State(data, node);
            int rank = TalentRules.Rank(data, node);
            bool selected = node.Id == _selected;

            RectTransform root = ClassesStyle.Fixed(cell, "Node_" + node.Id, new Vector2(0.5f, 0.5f), new Vector2(NodeWidth, NodeHeight));
            root.anchoredPosition = offset;
            root.localScale = new Vector3(_nodeScale, _nodeScale, 1f);
            // The panel-coloured back masks the connector running behind the labels, as on the boards.
            Image back = ClassesStyle.Fill(root, ClassesStyle.Box(6, ClassesStyle.Panel), Color.white, true);
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            string id = node.Id;
            button.onClick.AddListener(() => { _selected = _selected == id ? null : id; BuildCard(ClassPlayerStore.Local); RepaintSelection(); });

            Color stroke, fill, icon, name, sub;
            float strokePx;
            switch (state)
            {
                case NodeState.Owned:
                    fill = ClassesStyle.Selected; stroke = ClassesStyle.Gold; strokePx = 3f;
                    icon = ClassesStyle.Gold; name = ClassesStyle.Text; sub = ClassesStyle.Muted;
                    break;
                case NodeState.Available:
                    fill = ClassesStyle.Panel; stroke = ClassesStyle.Available; strokePx = 2.5f;
                    icon = ClassesStyle.Available; name = ClassesStyle.Text; sub = ClassesStyle.Muted;
                    break;
                default:
                    fill = ClassesStyle.Well; stroke = ClassesStyle.LockedStroke; strokePx = 2f;
                    icon = ClassesStyle.LockedSub; name = ClassesStyle.LockedText; sub = ClassesStyle.LockedSub;
                    break;
            }

            // 2b: one vertical stack from the kit's NodeLayout when hosted: PadTop | Ring | RingToPips | Pips | PipsToName | Name | Sub | PadBottom
            bool hub = Hub;
            float haloSize = hub ? FiresCore.UI.Shell.Kit.NodeLayout.Halo : RingSize + 14f;
            float ringY = hub ? -(FiresCore.UI.Shell.Kit.NodeLayout.PadTop + RingSize / 2f) : -RingSize / 2f - 2f;
            RectTransform halo = ClassesStyle.Fixed(root, "Halo", new Vector2(0.5f, 1f), new Vector2(haloSize, haloSize));
            halo.anchoredPosition = new Vector2(0f, ringY);
            ClassesStyle.Fill(halo, ClassesStyle.Circle((int)haloSize, Color.clear, ClassesStyle.Gold, 2f), Color.white)
                .enabled = selected;

            RectTransform ring = ClassesStyle.Fixed(root, "Ring", new Vector2(0.5f, 1f), new Vector2(RingSize, RingSize));
            ring.anchoredPosition = new Vector2(0f, ringY);
            ClassesStyle.Fill(ring, ClassesStyle.Circle((int)RingSize, fill, stroke, strokePx), Color.white);
            _rings[node.Id] = ring;

            RectTransform glyph = ClassesStyle.Fixed(ring, "icon", new Vector2(0.5f, 0.5f), new Vector2(24f, 24f));
            Sprite sprite = ClassIcons.For(node.Id);
            Image glyphImage = ClassesStyle.Fill(glyph, sprite, icon);
            if (sprite == null) glyphImage.enabled = false;

            // Rank pips under the ring (ultimates grow by use and have none).
            if (!TalentRules.IsUltimate(node) && node.MaxRank > 0)
            {
                float pip = hub ? FiresCore.UI.Shell.Kit.NodeLayout.PipSize : 7f, gap = hub ? FiresCore.UI.Shell.Kit.NodeLayout.PipGap : 4f;
                float pipY = hub ? -(FiresCore.UI.Shell.Kit.NodeLayout.PadTop + RingSize + FiresCore.UI.Shell.Kit.NodeLayout.RingToPips + pip / 2f) : -RingSize - 10f;
                float start = -(node.MaxRank - 1) * (pip + gap) / 2f;
                for (int r = 0; r < node.MaxRank; r++)
                {
                    RectTransform dot = ClassesStyle.Fixed(root, "Pip" + (r + 1), new Vector2(0.5f, 1f), new Vector2(pip, pip));
                    dot.anchoredPosition = new Vector2(start + r * (pip + gap), pipY);
                    // An empty ring, with a "Fill" child active only for a learned rank: class_test (ui_persist) reads
                    // a node's rank back as the number of active Pip<n>/Fill children.
                    ClassesStyle.Fill(dot, ClassesStyle.Circle((int)pip, ClassesStyle.Well, ClassesStyle.LockedStroke, 1f), Color.white);
                    RectTransform learned = ClassesStyle.Rect(dot, "Fill", Vector2.zero, Vector2.one);
                    ClassesStyle.Fill(learned, ClassesStyle.Circle((int)pip, ClassesStyle.Gold, ClassesStyle.Gold, 1f), Color.white);
                    learned.gameObject.SetActive(r < rank);
                }
            }

            TextMeshProUGUI nameLabel = ClassesStyle.Label(root, "Name", node.DisplayName, hub ? 15f : 14f, name,
                new Vector2(0f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Top);
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            TextMeshProUGUI subLabel = ClassesStyle.Label(root, "Sub", SubLabel(node), hub ? 11.5f : 12f, sub,
                new Vector2(0f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Top);
            subLabel.textWrappingMode = TextWrappingModes.NoWrap;
            if (hub)
            {
                // measured from the bottom of the Pitch-tall block: Sub sits on PadBottom, Name right above it
                float sb = FiresCore.UI.Shell.Kit.NodeLayout.PadBottom, sh = FiresCore.UI.Shell.Kit.NodeLayout.SubH, nh = FiresCore.UI.Shell.Kit.NodeLayout.NameH;
                subLabel.rectTransform.offsetMin = new Vector2(-4f, sb); subLabel.rectTransform.offsetMax = new Vector2(4f, sb + sh);
                nameLabel.rectTransform.offsetMin = new Vector2(-4f, sb + sh); nameLabel.rectTransform.offsetMax = new Vector2(4f, sb + sh + nh);
                nameLabel.enableAutoSizing = true; nameLabel.fontSizeMin = 12f; nameLabel.fontSizeMax = 15f;
                nameLabel.overflowMode = TextOverflowModes.Ellipsis; subLabel.overflowMode = TextOverflowModes.Ellipsis;
            }
            else
            {
                nameLabel.rectTransform.offsetMin = new Vector2(-4f, 14f);
                nameLabel.rectTransform.offsetMax = new Vector2(4f, 34f);
                subLabel.rectTransform.offsetMin = new Vector2(-4f, 0f);
                subLabel.rectTransform.offsetMax = new Vector2(4f, 15f);
            }

            // A node with no handler yet must not read as a working talent: greyed
            // icon, and the sub line says so.
            if (!FiresRPGClasses.Combat.ClassSkillKit.Implements(node.Id))
            {
                glyphImage.color = new Color(ClassesStyle.LockedSub.r, ClassesStyle.LockedSub.g, ClassesStyle.LockedSub.b, 0.55f);
                subLabel.text = "Not yet implemented";
                subLabel.color = ClassesStyle.Orange;
            }
        }

        private static string SubLabel(ClassTreeNode node)
        {
            if (node.Slot == NodeSlot.Evasion) return "Replaces your dodge";
            if (node.Slot == NodeSlot.Automatic) return "Always on, level " + node.MinLevel;
            if (TalentRules.IsUltimate(node)) return "Ultimate, level " + node.MinLevel;
            return "Level " + node.MinLevel;
        }

        private void RepaintSelection()
        {
            foreach (KeyValuePair<string, RectTransform> entry in _rings)
            {
                Transform halo = entry.Value.parent.Find("Halo");
                Image image = halo != null ? halo.GetComponent<Image>() : null;
                if (image != null) image.enabled = entry.Key == _selected;
            }
        }

        /// <summary>
        /// The path spines: each path's cells joined tier to tier, the starter feeding every path and every path
        /// feeding the ultimates. Plus any explicit RequiresNodeId the spines don't already draw.
        /// </summary>
        private void AddConnectors(ClassPlayerData data, ClassTree tree, List<ClassTreePath> paths)
        {
            _lines.ClearSegments();
            var drawn = new HashSet<string>();
            ClassTreeNode trunk = tree.Nodes.Where(n => RowOf(n) == NodeTier.Starter)
                .OrderBy(n => n.Slot == NodeSlot.Evasion ? 1 : 0).ThenBy(n => n.MinLevel).FirstOrDefault();
            List<ClassTreeNode> ultimates = tree.Nodes.Where(TalentRules.IsUltimate).ToList();

            for (int c = 0; c < paths.Count; c++)
            {
                ClassTreePath path = paths[c];
                ClassTreeNode[] previous = trunk != null ? new[] { trunk } : new ClassTreeNode[0];
                foreach (NodeTier tier in Tiers)
                {
                    if (tier == NodeTier.Starter || tier == NodeTier.Ultimate) continue;
                    ClassTreeNode[] here = tree.Nodes.Where(n => RowOf(n) == tier && n.PathId == path.Id).ToArray();
                    if (here.Length == 0) continue;
                    foreach (ClassTreeNode from in previous)
                        foreach (ClassTreeNode to in here)
                            Connect(data, from, to, drawn);
                    previous = here;
                }
                // Each path runs into the ultimate below it (left paths to the first, the right one to the last),
                // rather than every path to every ultimate, which crossed the lines over the bottom row.
                if (ultimates.Count == 0) continue;
                int nearest = Mathf.FloorToInt(c * (ultimates.Count - 1) / (float)Mathf.Max(1, paths.Count - 1) + 0.49f);
                ClassTreeNode below = ultimates[Mathf.Clamp(nearest, 0, ultimates.Count - 1)];
                foreach (ClassTreeNode from in previous)
                    if (from != trunk) Connect(data, from, below, drawn);
            }

            foreach (ClassTreeNode node in tree.Nodes)
            {
                ClassTreeNode required = ClassTrees.Node(node.RequiresNodeId);
                if (required != null && required.Archetype == node.Archetype) Connect(data, required, node, drawn);
            }
        }

        private void Connect(ClassPlayerData data, ClassTreeNode from, ClassTreeNode to, HashSet<string> drawn)
        {
            if (!drawn.Add(from.Id + ">" + to.Id)) return;
            if (!_rings.TryGetValue(from.Id, out RectTransform a) || !_rings.TryGetValue(to.Id, out RectTransform b)) return;
            NodeState fromState = TalentRules.State(data, from), toState = TalentRules.State(data, to);
            if (fromState == NodeState.Owned && toState == NodeState.Owned)
                _lines.Add(a, b, 3f, ClassesStyle.Gold);
            else if (fromState == NodeState.Owned && toState == NodeState.Available)
                _lines.Add(a, b, 2f, ClassesStyle.AvailableLine);
            else
                _lines.Add(a, b, 2f, ClassesStyle.Line);
        }

        // ---- the node card ---------------------------------------------------------------------------------

        private void BuildCard(ClassPlayerData data)
        {
            if (_card == null) return;
            ClassesStyle.Clear(_card);
            ClassTreeNode node = ClassTrees.Node(_selected);
            if (data == null || node == null || !_rings.TryGetValue(node.Id, out RectTransform ring))
            {
                _card.gameObject.SetActive(false);
                return;
            }
            _card.gameObject.SetActive(true);

            ClassesStyle.Fill(_card, ClassesStyle.Box(8, ClassesStyle.Tooltip, ClassesStyle.Gold, 1), Color.white, true);
            var layout = _card.gameObject.GetComponent<VerticalLayoutGroup>() ?? _card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = _card.gameObject.GetComponent<ContentSizeFitter>() ?? _card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _card.sizeDelta = new Vector2(CardWidth, 0f);

            int rank = TalentRules.Rank(data, node);
            bool ultimate = TalentRules.IsUltimate(node);
            ClassTree tree = ClassTrees.Tree(node.Archetype);
            string pathName = tree?.Paths.FirstOrDefault(p => p.Id == node.PathId)?.DisplayName;

            // The card says what kind of talent it is and what it does, in plain words, never the slot numbers or
            // enum names; then what the next rank adds and what it costs.
            CardText("Title", node.DisplayName, 20f, ClassesStyle.Gold, true);
            if (!FiresRPGClasses.Combat.ClassSkillKit.Implements(node.Id))
                CardText("NotImplemented", "Not yet implemented: learning it does nothing yet.", 13f, ClassesStyle.Orange);
            CardText("Type", TypeLine(node), 14f, ClassesStyle.Blue);
            CardText("Where", (pathName != null ? pathName + " path  ·  " : "") + TierName(RowOf(node)) + "  ·  Level " + node.MinLevel,
                13f, ClassesStyle.Muted);

            string effect = EffectText(node, Mathf.Max(1, rank));
            if (effect.Length > 0) CardText("Effect", effect, 15f, ClassesStyle.Text);
            string usage = UsageNote(node);
            if (usage.Length > 0) CardText("Usage", usage, 13f, ClassesStyle.Muted);

            if (!ultimate)
            {
                if (rank > 0 && rank < node.MaxRank)
                {
                    string next = EffectText(node, rank + 1);
                    if (next.Length > 0 && next != effect) CardText("NextRank", "Next rank: " + next, 14f, ClassesStyle.Available);
                }
                CardText("Rank", RankLine(data, node, rank), 13f, ClassesStyle.Muted);
                string ranks = RankTable(node, rank);
                if (ranks.Length > 0) CardText("Ranks", ranks, 13f, ClassesStyle.Text);
            }

            RectTransform actions = ClassesStyle.Rect(_card, "Actions", Vector2.zero, Vector2.one);
            actions.gameObject.AddComponent<LayoutElement>().preferredHeight = ClassesStyle.Target;
            if (TalentRules.IsUltimate(node))
            {
                ClassesStyle.Button(actions, "OpenUltimates", "Open ultimates", ClassesStyle.ButtonKind.Outline,
                    Vector2.zero, Vector2.one, () => ClassesPanel.Instance?.Show(ClassesTab.Ultimates), 15f);
            }
            else
            {
                bool can = TalentRules.CanLearn(data, node, out int price, out string why);
                Button learn = ClassesStyle.Button(actions, "Learn",
                    can ? "Learn rank " + (rank + 1) + " for " + TalentRules.Points(price) : why,
                    ClassesStyle.ButtonKind.Primary, Vector2.zero, Vector2.one, () =>
                    {
                        if (!TalentRules.TryLearn(ClassPlayerStore.Local, ClassTrees.Node(_selected))) return;
                        Refresh();
                    }, 15f);
                ClassesStyle.Paint(learn, ClassesStyle.ButtonKind.Primary, can);
            }

            // Beside the node: to its right when it sits in the left half of the tree, else to its left. No
            // docked panel; a click on empty space or Esc closes it (CloseOverlay).
            LayoutRebuilder.ForceRebuildLayoutImmediate(_card);
            Vector2 at = _tree.InverseTransformPoint(ring.TransformPoint(ring.rect.center));
            Rect area = _tree.rect;
            float x = at.x < area.center.x ? at.x + RingSize : at.x - RingSize - CardWidth;
            float height = _card.rect.height;
            float y = Mathf.Clamp(at.y + 40f, area.yMin + height, area.yMax);
            _card.anchorMin = _card.anchorMax = new Vector2(0f, 1f);
            _card.anchoredPosition = new Vector2(x - area.xMin, y - area.yMax);
            _card.SetAsLastSibling();
        }

        private void CardText(string name, string text, float size, Color color, bool bold = false)
        {
            TextMeshProUGUI label = ClassesStyle.Label(_card, name, text, size, color, Vector2.zero, Vector2.one,
                TextAlignmentOptions.TopLeft, bold);
            label.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>"Active skill  ·  10 m AoE  ·  30 s cooldown  ·  30 stamina": the type first, then how it is used.</summary>
        private static string TypeLine(ClassTreeNode node)
        {
            var parts = new List<string> { KindOf(node) };
            if (node.Slot == NodeSlot.Skill || node.Slot == NodeSlot.Ultimate)
            {
                string aim = Describe(node.Targeting);
                if (aim.Length > 0) parts.Add(aim);
            }
            string cost = CostLine(node);
            if (cost.Length > 0) parts.Add(cost);
            return string.Join("  ·  ", parts.ToArray());
        }

        /// <summary>The three kinds. A dodge replacement and an automatic ability are passives the player doesn't cast.</summary>
        internal static string KindOf(ClassTreeNode node)
        {
            if (TalentRules.IsUltimate(node)) return "Ultimate";
            return node.Slot == NodeSlot.Skill ? "Active skill" : "Passive skill";
        }

        /// <summary>How the player puts it to use, where that isn't obvious from the kind.</summary>
        private static string UsageNote(ClassTreeNode node)
        {
            switch (node.Slot)
            {
                case NodeSlot.Skill: return "Put it on a bar in Bars, then cast it from that bar.";
                case NodeSlot.Ultimate: return "Grows each time you cast it. Put it on a bar in Bars.";
                case NodeSlot.Evasion: return "Replaces your dodge while this class is on your active bar.";
                default: return "Equip it in Passives; it is always on, whichever bar you use.";
            }
        }

        /// <summary>
        /// What it does at a rank: the yaml text with its {key}s filled, or, for a node Core implements (it has no yaml
        /// text), Core's own ability description.
        /// </summary>
        internal static string EffectText(ClassTreeNode node, int rank)
        {
            if (node == null) return "";
            if (!string.IsNullOrEmpty(node.Description)) return node.DescriptionAt(rank);
            // A passive Core runs through this mod's PassiveKit: its sentence with the real numbers at this rank, so every
            // passive explains what it does. From PassiveKit's own constants, so it can't drift.
            string kit = Combat.PassiveKit.EffectLine(node.Id, rank);
            if (!string.IsNullOrEmpty(kit)) return kit;
            return FiresCore.Classes.ClassAbilityRegistry.GetAbility(node.Id)?.Description ?? "";
        }

        /// <summary>"Rank 1 of 3. Rank 2 costs 2 points (you have 40). Ranks cost 1, 2 and 3 points, 6 in all."</summary>
        private static string RankLine(ClassPlayerData data, ClassTreeNode node, int rank)
        {
            var costs = new List<int>();
            for (int r = 1; r <= node.MaxRank; r++) costs.Add(ClassTrees.RankCost(r));
            string all = node.MaxRank <= 1 ? "" : " Ranks cost " + JoinAnd(costs.Select(c => c.ToString()).ToList())
                + " points, " + costs.Sum() + " in all.";
            if (rank >= node.MaxRank)
                return (node.MaxRank == 1 ? "Learned." : "All " + node.MaxRank + " ranks learned.") + all;
            if (ClassTrees.IsParagonRank(rank + 1))   // ranks 4+ cost 1 Paragon point each
                return "Rank " + rank + " of " + node.MaxRank + ". Rank " + (rank + 1) + " costs 1 Paragon point (you have "
                       + data.Progress.UnspentParagonPoints() + ")." + all;
            int next = ClassTrees.RankCost(rank + 1);
            return "Rank " + rank + " of " + node.MaxRank + ". Rank " + (rank + 1) + " costs " + TalentRules.Points(next)
                   + " (you have " + data.Progress.UnspentSkillPoints(TalentRules.Rates) + ")." + all;
        }

        private static string JoinAnd(List<string> items) =>
            items.Count <= 1 ? string.Join("", items.ToArray())
                : string.Join(", ", items.Take(items.Count - 1).ToArray()) + " and " + items[items.Count - 1];

        /// <summary>The yaml's per-rank values as a small table, the current rank in gold.</summary>
        private static string RankTable(ClassTreeNode node, int rank)
        {
            if (node.Values.Count == 0 || node.MaxRank <= 1 && node.Values.All(v => v.Value.Length <= 1))
                return string.Join("   ", node.Values.Where(v => v.Value.Length > 0)
                    .Select(v => Pretty(v.Key) + " " + Number(v.Key, node.Value(v.Key, 1))).ToArray());
            var text = new StringBuilder();
            int columns = Mathf.Max(1, node.MaxRank);
            foreach (KeyValuePair<string, float[]> entry in node.Values)
            {
                text.Append(Pretty(entry.Key));
                for (int r = 1; r <= columns; r++)
                {
                    int pos = 38 + (r - 1) * (60 / columns);
                    string value = Number(entry.Key, node.Value(entry.Key, r));
                    text.Append("<pos=" + pos + "%>").Append(r == rank ? "<color=#f0b44c>" + value + "</color>" : value);
                }
                text.Append('\n');
            }
            return text.ToString().TrimEnd('\n');
        }

        private static string Pretty(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            var words = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i > 0 && char.IsUpper(c)) words.Append(' ');
                words.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }
            return words.ToString();
        }

        /// <summary>The yaml keeps percentages as fractions (15% = 0.15); keys that read as percentages show as such.</summary>
        // One formatter for the rank table and the {key}s in a card's text, so the two can't disagree.
        private static string Number(string key, float value) => ClassTreeNode.FormatValue(key, value);

        private static string CostLine(ClassTreeNode node)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(node.CostText)) parts.Add(node.CostText);
            if (node.Cooldown > 0f && node.IsActive) parts.Add(node.Cooldown.ToString("0") + " s cooldown");
            if (node.EffectDuration > 0f) parts.Add("lasts " + node.EffectDuration.ToString("0") + " s");
            return string.Join(", ", parts.ToArray());
        }

        // ---- legend ----------------------------------------------------------------------------------------

        private static void BuildLegend(RectTransform legend)
        {
            RectTransform rule = ClassesStyle.Rect(legend, "Rule", new Vector2(0f, 1f), new Vector2(1f, 1f));
            rule.sizeDelta = new Vector2(0f, 1f);
            ClassesStyle.Fill(rule, null, ClassesStyle.Line);

            (string label, Sprite swatch)[] items =
            {
                ("Learned", ClassesStyle.Circle(22, ClassesStyle.Selected, ClassesStyle.Gold, 3f)),
                ("Available", ClassesStyle.Circle(22, ClassesStyle.Panel, ClassesStyle.Available, 2.5f)),
                ("Locked", ClassesStyle.Circle(22, ClassesStyle.Well, ClassesStyle.LockedStroke, 2f)),
                ("Ranks learned", ClassesStyle.Circle(10, ClassesStyle.Gold, ClassesStyle.Gold, 1f)),
                ("Variants: learn both, slot one", ClassesStyle.Box(6, ClassesStyle.Panel, ClassesStyle.Available, 1))
            };
            for (int i = 0; i < items.Length; i++)
            {
                float min = i / (float)items.Length, max = (i + 1) / (float)items.Length;
                RectTransform item = ClassesStyle.Rect(legend, "Legend_" + i, new Vector2(min, 0f), new Vector2(max, 1f));
                RectTransform swatch = ClassesStyle.Fixed(item, "Swatch", new Vector2(0f, 0.5f), new Vector2(22f, 22f));
                swatch.anchoredPosition = new Vector2(14f, 0f);
                ClassesStyle.Fill(swatch, items[i].swatch, Color.white);
                ClassesStyle.Label(item, "Label", items[i].label, 14f, ClassesStyle.Muted, Vector2.zero, Vector2.one)
                    .rectTransform.offsetMin = new Vector2(32f, 0f);
            }
        }

        // ---- text helpers shared with other boards -----------------------------------------------------------

        private static string TierName(NodeTier tier) => tier == NodeTier.Ultimate ? "Ultimates" : tier.ToString();

        private static string TierLevels(ClassTree tree, NodeTier tier)
        {
            List<int> levels = tree.Nodes.Where(n => RowOf(n) == tier).Select(n => n.MinLevel).Distinct().OrderBy(l => l).ToList();
            if (levels.Count == 0) return "";
            if (levels.Count == 1) return "Level " + levels[0];
            if (tier == NodeTier.Ultimate) return "Levels " + string.Join(" and ", levels.Select(l => l.ToString()).ToArray());
            return "Levels " + levels.First() + " to " + levels.Last();
        }

        /// <summary>
        /// How a skill is aimed, in player words ("10 m AoE", never "AroundYou 10 m radius"). Every targeting
        /// mode has a phrase here, so no enum name reaches a card.
        /// </summary>
        internal static string Describe(TargetingSpec spec)
        {
            if (spec == null) return "";
            string range = spec.Range > 0f ? spec.Range.ToString("0.#") + " m" : "";
            string area = spec.Radius > 0f ? spec.Radius.ToString("0.#") + " m AoE" : "";
            switch (spec.Mode)
            {
                case TargetingMode.Self: return area.Length > 0 ? area + " around you" : "On yourself";
                case TargetingMode.AroundYou: return area.Length > 0 ? area + " around you" : "Around you";
                case TargetingMode.Melee: return "Melee";
                case TargetingMode.Aimed: return "Aimed" + (range.Length > 0 ? ", " + range + " range" : "") + (area.Length > 0 ? ", " + area : "");
                case TargetingMode.Placed: return "Placed" + (range.Length > 0 ? " up to " + range + " away" : "") + (area.Length > 0 ? ", " + area : "");
                case TargetingMode.Ally: return "On an ally" + (range.Length > 0 ? " within " + range : "");
                case TargetingMode.NextAttack: return "Empowers your next attack";
                case TargetingMode.Dash: return "Dash" + (range.Length > 0 ? " " + range : "");
                case TargetingMode.Dodge: return "Replaces your dodge";
                case TargetingMode.Automatic: return "Triggers on its own";
                default: return "";
            }
        }
    }
}
