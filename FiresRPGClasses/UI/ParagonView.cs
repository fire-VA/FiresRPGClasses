using System.Collections.Generic;
using System.Linq;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Paragon: ESO-style Champion-Point constellations. One tree
    /// at a time (tree tabs), drawn as a star map: 4 cluster quadrants, tier rings T1 (centre) to T4 (outer), bridges on the
    /// quadrant borders, link lines lit between learned stars. Right column: the selected star's card (Buy rank / Slot), the
    /// tier progress, the 4 slots of every tree, a legend.
    ///
    /// Layout rules: every button sized to its caption; shared fonts (ClassesStyle.Label = Core UIFontConfig, Latin-1);
    /// star labels never overlap (short names on the map, full name in the hover; a label that can't be placed clear of the
    /// others is dropped and the hover still names it); every star has a hover tooltip (FiresHoverTip + ParagonUi.Tooltip).
    /// Data only through <see cref="ParagonSource"/> (ParagonUi, contract §9 of Tools\RPG_PARAGON_TALENT_PLAN.md).
    ///
    /// Names: tree tabs "ParagonTree_&lt;id&gt;", stars "Star_&lt;id&gt;", slots "Slot_&lt;tree&gt;_&lt;n&gt;", buttons "BuyRank",
    /// "SlotStar", "Respec".
    /// </summary>
    public class ParagonView : IClassesView
    {
        private const float Pad = 24f;
        private const float HeaderHeight = 40f;
        private const float SideWidth = 340f;
        // One constellation per page leaves room for a wider side panel with bigger text.
        private const float FigureSideWidth = 560f;
        private const float FigureFontScale = 1.3f;
        private static float SideW => ParagonFigures.On ? FigureSideWidth : SideWidth;
        // Slot cells are sized from the panel's inner width (Place insets 14 each side) so the 4th cell never runs past the edge.
        private const float SlotLabelW = 104f, SlotGap = 8f;
        private float SlotCellW() { float inner = (_side != null && _side.rect.width > 1f ? _side.rect.width : SideW) - 28f; return Mathf.Max(48f, (inner - SlotLabelW - 6f - 3f * SlotGap) / 4f); }
        private static float Fs(float size) => ParagonFigures.On ? Mathf.Round(size * FigureFontScale) : size;
        private const float LabelSize = 12f;
        // The single-figure view draws bigger stars and labels.
        private const float SingleFigureStarMul = 1.5f, SingleFigureLabelMul = 1.3f;
        private float _starMul = 1f, _labelMul = 1f;
        private readonly Dictionary<string, int> _figIndex = new Dictionary<string, int>();
        private bool _skyHooked;
        private UnityEngine.UI.RawImage _skyImage;
        // tier ring radii as a fraction of the outer ring, and the per-tier spread of one cluster's stars (degrees);
        // the same rule produced Tools\paragon_star_positions.txt, so generated and yaml positions agree
        private static readonly float[] Ring = { 0f, 0.374f, 0.610f, 0.825f, 1f };
        private static readonly float[] Spread = { 0f, 26f, 34f, 34f, 0f };

        private static readonly Color Sky = ClassesStyle.Hex("#0d0f1c");
        private static readonly Color LockedStar = ClassesStyle.Hex("#55556a");
        private static readonly Color OpenStar = ClassesStyle.Hex("#cdcddc");
        private static readonly Color FaintLine = new Color(0.66f, 0.66f, 0.78f, 0.32f);

        public ClassesTab Tab => ClassesTab.Paragon;
        public string Title => "Paragon";

        private RectTransform _page, _header, _map, _side;
        private UILineGraphic _lines;
        private string _tree;
        private string _selected;
        private string _status;
        private bool _subscribed;

        public void Build(RectTransform page)
        {
            _page = page;
            _header = ClassesStyle.Rect(page, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _header.pivot = new Vector2(0.5f, 1f);
            _header.offsetMin = new Vector2(Pad, -8f - HeaderHeight);
            _header.offsetMax = new Vector2(-Pad, -8f);

            _side = ClassesStyle.Rect(page, "Side", new Vector2(1f, 0f), new Vector2(1f, 1f));
            _side.pivot = new Vector2(1f, 0.5f);
            _side.offsetMin = new Vector2(-Pad - SideW, 14f);
            _side.offsetMax = new Vector2(-Pad, -8f - HeaderHeight - 10f);
            ClassesStyle.WellFrame(_side);

            _map = ClassesStyle.Rect(page, "Map", Vector2.zero, Vector2.one);
            _map.offsetMin = new Vector2(Pad, 14f);
            _map.offsetMax = new Vector2(-Pad - SideW - 14f, -8f - HeaderHeight - 10f);
            ClassesStyle.Fill(_map, ClassesStyle.Box(8, Sky, ClassesStyle.Line, 1), Color.white);

            if (!_subscribed) _subscribed = ParagonSource.Subscribe(Refresh);
        }

        public void Refresh()
        {
            if (_page == null) return;
            if (!_subscribed) _subscribed = ParagonSource.Subscribe(Refresh);
            if (!ParagonSource.Available)
            {
                ClassesStyle.Clear(_header); ClassesStyle.Clear(_map); ClassesStyle.Clear(_side);
                ClassesStyle.Label(_map, "NotYet", "Paragon is not available in this version of the class system.", 18f,
                    ClassesStyle.Muted, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
                return;
            }
            List<ParagonSource.Tree> trees = ParagonSource.Trees();
            if (trees.Count == 0) return;
            if (_tree == null || trees.All(t => t.Id != _tree)) _tree = trees[0].Id;
            ParagonSource.Tree tree = trees.First(t => t.Id == _tree);
            List<ParagonSource.Star> stars = ParagonSource.Stars(_tree);
            if (_selected == null || stars.All(s => s.Id != _selected))
                _selected = stars.FirstOrDefault(s => s.Capstone)?.Id ?? stars.FirstOrDefault()?.Id;

            // the side/map split follows the mode (figure mode = the wider panel), re-applied on every refresh
            _side.offsetMin = new Vector2(-Pad - SideW, 14f);
            _map.offsetMax = new Vector2(-Pad - SideW - 14f, -8f - HeaderHeight - 10f);
            Canvas.ForceUpdateCanvases();
            BuildHeader(trees);
            BuildMap(tree, stars);
            BuildSide(trees, tree, stars);
        }

        // ---- header: title, tree tabs, points, respec ---------------------------------------------------------

        private void BuildHeader(List<ParagonSource.Tree> trees)
        {
            ClassesStyle.Clear(_header);
            TextMeshProUGUI title = ClassesStyle.Label(_header, "Title", "Paragon", 26f, ClassesStyle.Gold,
                new Vector2(0f, 0f), new Vector2(0f, 1f), TextAlignmentOptions.MidlineLeft, true);
            float x = FitLabel(title, 0f) + 20f;
            foreach (ParagonSource.Tree t in trees)
            {
                string id = t.Id;
                string caption = t.Name + "  " + ParagonSource.SpentInTree(id);
                Button tab = ClassesStyle.Pill(_header, "ParagonTree_" + id, caption, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    () => { _tree = id; _selected = null; _status = null; Refresh(); }, 16f);
                x += FitButton(tab, x, 36f) + 8f;
                ClassesStyle.PaintPill(tab, id == _tree);
                if (id == _tree) tab.GetComponentInChildren<TMP_Text>().color = t.Colour;
            }
            Button respec = ClassesStyle.Button(_header, "Respec", "Respec", ClassesStyle.ButtonKind.Outline,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), OnRespec, 15f);
            float rw = FitButton(respec, 0f, 34f, true);
            string points = ParagonSource.Unspent + " Paragon points to spend";
            TextMeshProUGUI pts = ClassesStyle.Label(_header, "Points", points, 15f, ClassesStyle.Text,
                new Vector2(0f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);
            pts.rectTransform.offsetMax = new Vector2(-rw - 16f, 0f);
            pts.rectTransform.offsetMin = new Vector2(x + 12f, 0f);
        }

        private void OnRespec()
        {
            _status = ParagonSource.TryRespec(_tree, out string why) ? "Points in this tree refunded." : why;
            Refresh();
        }

        // ---- the star map ----------------------------------------------------------------------------------

        private void BuildMap(ParagonSource.Tree tree, List<ParagonSource.Star> stars)
        {
            ClassesStyle.Clear(_map);
            float w = _map.rect.width, h = _map.rect.height;
            if (w < 10f || h < 10f) return;
            int spent = ParagonSource.SpentInTree(tree.Id);
            float side = Mathf.Min(w, h);
            float outer = side * 0.46f;   // panel units: pos (0..1) maps onto a side x side square in the middle
            _labelSegments.Clear(); _labelBlocks.Clear();
            _starMul = 1f; _labelMul = 1f;   // only the one-figure view enlarges
            { Image plate = _map.GetComponent<Image>(); if (plate != null) plate.enabled = true; }
            if (ParagonFigures.On && BuildSingleFigure(tree, stars, w, h)) return;   // one figure per page; else the ring map below

            // rings + their gate captions (on the right of the horizontal axis, clear of the stars)
            for (int t = 1; t <= 4; t++)
            {
                bool open = spent >= Gate(tree, t);
                float d = 2f * outer * Ring[t];
                RectTransform ring = ClassesStyle.Fixed(_map, "Ring" + t, new Vector2(0.5f, 0.5f), new Vector2(d, d));
                ClassesStyle.Fill(ring, ClassesStyle.Circle(256, Color.clear, Color.white, 2f, true),
                    open ? new Color(tree.Colour.r, tree.Colour.g, tree.Colour.b, 0.28f) : new Color(1f, 1f, 1f, 0.12f));
                TextMeshProUGUI gate = ClassesStyle.Label(_map, "Gate" + t, "T" + t + "  " + Gate(tree, t), 11f,
                    open ? ClassesStyle.Muted : ClassesStyle.LockedSub, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
                gate.rectTransform.sizeDelta = new Vector2(48f, 14f);
                gate.rectTransform.anchoredPosition = new Vector2(outer * Ring[t] - 26f, 9f);
            }
            // cluster titles in the panel corners nearest their quadrant
            foreach (ParagonSource.Cluster c in tree.Clusters)
            {
                float a = c.AngleDeg * Mathf.Deg2Rad;
                bool right = Mathf.Cos(a) > 0f, bottom = Mathf.Sin(a) > 0f;   // angles are screen degrees, y down
                var anchor = new Vector2(right ? 1f : 0f, bottom ? 0f : 1f);
                TextMeshProUGUI cl = ClassesStyle.Label(_map, "Cluster_" + c.Id, c.Name.ToUpperInvariant(), 13f,
                    new Color(tree.Colour.r, tree.Colour.g, tree.Colour.b, 0.85f), anchor, anchor,
                    right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, true);
                cl.rectTransform.pivot = anchor;
                cl.rectTransform.sizeDelta = new Vector2(220f, 20f);
                cl.rectTransform.anchoredPosition = new Vector2(right ? -10f : 10f, bottom ? 8f : -8f);
            }

            RectTransform linesRect = ClassesStyle.Rect(_map, "Lines", Vector2.zero, Vector2.one);
            _lines = linesRect.gameObject.AddComponent<UILineGraphic>();
            _lines.raycastTarget = false;

            Dictionary<string, Vector2> pos = Positions(tree, stars, outer);
            var rects = new Dictionary<string, RectTransform>();
            var byId = stars.ToDictionary(s => s.Id);
            foreach (ParagonSource.Star s in stars)
                rects[s.Id] = DrawStar(tree, s, pos[s.Id]);

            // links: lit when both ends have ranks
            var drawn = new HashSet<string>();
            foreach (ParagonSource.Star s in stars)
                foreach (string l in s.Links)
                {
                    if (!rects.ContainsKey(l) || !drawn.Add(string.CompareOrdinal(s.Id, l) < 0 ? s.Id + "|" + l : l + "|" + s.Id)) continue;
                    bool lit = s.Rank > 0 && byId[l].Rank > 0;
                    _lines.Add(rects[s.Id], rects[l], lit ? 2.5f : 1.2f, lit ? tree.Colour : FaintLine);
                }
            linesRect.SetAsFirstSibling();

            PlaceLabels(stars, pos);

            // centre badge: points spent in this tree
            RectTransform badge = ClassesStyle.Fixed(_map, "Centre", new Vector2(0.5f, 0.5f), new Vector2(58f, 58f));
            ClassesStyle.Fill(badge, ClassesStyle.Circle(128, ClassesStyle.Hex("#1e1212"), tree.Colour, 3f), Color.white);
            ClassesStyle.Label(badge, "Name", tree.Name, 11f, tree.Colour, new Vector2(0f, 0.5f), new Vector2(1f, 1f), TextAlignmentOptions.Bottom);
            ClassesStyle.Label(badge, "Spent", spent.ToString(), 17f, ClassesStyle.Text, Vector2.zero, new Vector2(1f, 0.55f), TextAlignmentOptions.Top, true);
        }

        private static readonly Color FigureLine = new Color(0.85f, 0.92f, 1f, 0.85f);    // the sheets' #D8ECFF
        private static readonly Color ShapeStar = new Color(0.96f, 0.98f, 1f, 0.9f);
        private static readonly Color LinkHover = new Color(1f, 0.78f, 0.38f, 0.86f);    // the sheets' hover gold

        private static readonly HashSet<string> _labelLogged = new HashSet<string>();

        // ---- one constellation per page ---------------------------------------------------------------------
        // A top bar (< prev name . "The Axe . Gatherer" . 4 dots . next name >) floats on the sky; ONE figure fills the rest; the
        // map has no panel: the hub's live sky (FiresShell.SkyTexture) shows through; bridge stars sit at the map
        // edge toward the neighbour they link. The mouse wheel over the map steps figures too.
        private const float TopBar = 70f;

        private (ParagonSource.Cluster c, ParagonFigures.Figure fig) FigureOf(ParagonSource.Cluster c, List<ParagonSource.Star> stars)
        {
            if (ParagonFigures.TryGet(c.Id, out var fig)) return (c, fig);
            ParagonSource.Star any = stars.FirstOrDefault(s => s.Cluster == c.Id && !s.Bridge);
            return ParagonFigures.TryGet(ParagonFigures.BranchOf(any?.Id), out fig) ? (c, fig) : (c, null);
        }

        internal void StepFigure(int delta)
        {
            if (_tree == null) return;
            _figIndex.TryGetValue(_tree, out int i);
            _figIndex[_tree] = ((i + delta) % 4 + 4) % 4;
            _selected = null;
            FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] tree {_tree}: figure {_figIndex[_tree] + 1}/4");
            Refresh();
        }

        private void UpdateSky()
        {
            if (_map == null) return;
            if (_skyImage == null)
            {
                RectTransform skyRect = ClassesStyle.Rect(_map, "SkyBehind", Vector2.zero, Vector2.one);
                _skyImage = skyRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
                _skyImage.raycastTarget = false;
            }
            _skyImage.transform.SetAsFirstSibling();
            Texture sky = FiresCore.UI.Shell.FiresShell.SkyTexture;
            _skyImage.texture = sky;
            _skyImage.enabled = sky != null;
            // no hub sky (standalone window, hub closed): keep the old dark-blue map plate so the stars never sit on the bare window
            Image plate = _map.GetComponent<Image>();
            if (plate != null) plate.enabled = sky == null;
            if (sky != null)
            {
                // seamless with the sky around the window: sample the RT where the map sits on screen
                var corners = new Vector3[4];
                _map.GetWorldCorners(corners);
                Canvas canvas = _map.GetComponentInParent<Canvas>();
                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                _skyImage.uvRect = new Rect(a.x / Screen.width, a.y / Screen.height, (b.x - a.x) / Screen.width, (b.y - a.y) / Screen.height);
            }
        }

        private bool BuildSingleFigure(ParagonSource.Tree tree, List<ParagonSource.Star> stars, float w, float h)
        {
            var figs = tree.Clusters.Select(c => FigureOf(c, stars)).Where(x => x.fig != null).ToList();
            if (figs.Count == 0) return false;
            _figIndex.TryGetValue(tree.Id, out int idx);
            idx = Mathf.Clamp(idx, 0, figs.Count - 1); _figIndex[tree.Id] = idx;
            var cur = figs[idx];
            _starMul = SingleFigureStarMul; _labelMul = SingleFigureLabelMul;

            // no panel: the sky shows through
            // (the plate follows the sky: UpdateSky turns it off only while a sky texture is shown)
            if (!_skyHooked) { _skyHooked = true; FiresCore.UI.Shell.FiresShell.SkyTextureChanged += UpdateSky; }
            _skyImage = null;   // ClassesStyle.Clear destroyed the old one
            UpdateSky();
            if (_map.GetComponent<ParagonWheel>() == null) _map.gameObject.AddComponent<ParagonWheel>().View = this;

            // top bar
            Color shadow = new Color(0f, 0f, 0f, 0.75f);
            float barY = h * 0.5f - TopBar * 0.5f;
            // legibility on the stars: a dark copy of the text, 1.5 units down-right, drawn just behind it (TMP ignores uGUI Shadow)
            TextMeshProUGUI Shadowed(TextMeshProUGUI t)
            {
                var copy = Object.Instantiate(t.gameObject, t.transform.parent, false).GetComponent<TextMeshProUGUI>();
                copy.name = t.name + "_Shadow"; copy.color = shadow; copy.raycastTarget = false;
                copy.rectTransform.anchoredPosition = t.rectTransform.anchoredPosition + new Vector2(1.5f, -1.5f);
                copy.transform.SetSiblingIndex(t.transform.GetSiblingIndex());
                return t;
            }
            string prevName = figs[(idx + figs.Count - 1) % figs.Count].fig.Title, nextName = figs[(idx + 1) % figs.Count].fig.Title;
            foreach (int side in new[] { -1, 1 })
            {
                Button arrow = ClassesStyle.Button(_map, side < 0 ? "ParagonPrev" : "ParagonNext", side < 0 ? "<" : ">", ClassesStyle.ButtonKind.Outline,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), () => StepFigure(side), 22f);
                RectTransform ar = arrow.GetComponent<RectTransform>();
                ar.sizeDelta = new Vector2(44f, 44f);
                ar.anchoredPosition = new Vector2(side * (w * 0.5f - 34f), barY);
                TextMeshProUGUI nm = Shadowed(ClassesStyle.Label(_map, side < 0 ? "PrevName" : "NextName", side < 0 ? prevName : nextName, 15f, ClassesStyle.Muted,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), side < 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight));
                nm.rectTransform.sizeDelta = new Vector2(200f, 24f);
                nm.rectTransform.anchoredPosition = new Vector2(side * (w * 0.5f - 64f - 100f), barY);
            }
            TextMeshProUGUI title = Shadowed(ClassesStyle.Label(_map, "FigureTitle", cur.fig.Title + "  ·  " + cur.c.Name, 26f, ClassesStyle.Gold,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, true));
            title.rectTransform.sizeDelta = new Vector2(w - 520f, 34f);
            title.rectTransform.anchoredPosition = new Vector2(0f, barY + 8f);
            for (int d = 0; d < figs.Count; d++)
            {
                RectTransform dot = ClassesStyle.Fixed(_map, "FigureDot" + d, new Vector2(0.5f, 0.5f), new Vector2(10f, 10f));
                dot.anchoredPosition = new Vector2((d - (figs.Count - 1) * 0.5f) * 22f, barY - 20f);
                ClassesStyle.Fill(dot, ClassesStyle.Circle(16, d == idx ? ClassesStyle.Gold : Color.clear, ClassesStyle.Border, 2f), Color.white);
                int to = d;
                Button db = dot.gameObject.AddComponent<Button>();
                db.onClick.AddListener(() => { _figIndex[_tree] = to; _selected = null; Refresh(); });
            }

            // the figure, big, under the bar, with room at the sides for bridges and labels
            float bodyH = h - TopBar - 40f, bodyW = w - 300f;
            float scale = Mathf.Min(bodyW / ParagonFigures.FrameW, bodyH / ParagonFigures.FrameH);
            Vector2 centre = new Vector2(0f, -TopBar * 0.5f - 10f);
            Vector2 At(Vector2 p) => centre + new Vector2((p.x - ParagonFigures.FrameW * 0.5f) * scale, (ParagonFigures.FrameH * 0.5f - p.y) * scale);

            var drawn = stars.Where(s => !s.Bridge && s.Cluster == cur.c.Id).ToList();
            var pos = new Dictionary<string, Vector2>();
            foreach (ParagonSource.Star s in drawn)
            {
                string role = ParagonFigures.RoleOf(s.Id);
                if (role != null && cur.fig.Nodes.TryGetValue(role, out string key) && cur.fig.Points.TryGetValue(key, out Vector2 p)) pos[s.Id] = At(p);
            }
            drawn = drawn.Where(s => pos.ContainsKey(s.Id)).ToList();
            // bridges touching this figure: left edge if they reach the previous figure, right edge otherwise
            string prevCluster = figs[(idx + figs.Count - 1) % figs.Count].c.Id;
            // the yaml bridges can have an empty links list and a cluster (CB1 in C1...), so a bridge shows on its OWN cluster's figure (right edge, toward the next) as well as on any figure it links to (either direction)
            var drawnIds = new HashSet<string>(drawn.Select(d => d.Id));
            var bridges = stars.Where(s => s.Bridge && (s.Cluster == cur.c.Id || s.Links.Any(drawnIds.Contains) || drawn.Any(d => d.Links.Contains(s.Id)))).ToList();
            int li = 0, ri = 0;
            foreach (ParagonSource.Star b in bridges)
            {
                bool left = b.Cluster != cur.c.Id && (b.Cluster == prevCluster || b.Links.Any(l => stars.FirstOrDefault(o => o.Id == l)?.Cluster == prevCluster));
                int n = left ? li++ : ri++;
                pos[b.Id] = new Vector2((left ? -1f : 1f) * (w * 0.5f - 70f), -TopBar * 0.5f - n * 90f);
                drawn.Add(b);
            }
            if (_selected == null || !drawn.Any(s => s.Id == _selected))
                _selected = drawn.FirstOrDefault(s => s.Capstone)?.Id ?? drawn.FirstOrDefault()?.Id;

            RectTransform figRect = ClassesStyle.Rect(_map, "FigureLines", Vector2.zero, Vector2.one);
            UILineGraphic figLines = figRect.gameObject.AddComponent<UILineGraphic>();
            figLines.raycastTarget = false;
            RectTransform anchors = ClassesStyle.Rect(_map, "FigureAnchors", Vector2.zero, Vector2.one);
            RectTransform Anchor(Vector2 p) { RectTransform r = ClassesStyle.Fixed(anchors, "A", new Vector2(0.5f, 0.5f), Vector2.one); r.anchoredPosition = p; return r; }
            int segs = 0;
            foreach (string[] l in cur.fig.Lines)
            {
                if (!cur.fig.Points.TryGetValue(l[0], out Vector2 a) || !cur.fig.Points.TryGetValue(l[1], out Vector2 b)) continue;
                figLines.Add(Anchor(At(a)), Anchor(At(b)), 2.2f, FigureLine);
                _labelSegments.Add(new[] { At(a), At(b) }); segs++;
            }
            foreach (string[] cv in cur.fig.Curves)
            {
                List<Vector2> pts = ParagonFigures.Sample(cur.fig, cv);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    figLines.Add(Anchor(At(pts[i])), Anchor(At(pts[i + 1])), 2.2f, FigureLine);
                    _labelSegments.Add(new[] { At(pts[i]), At(pts[i + 1]) }); segs++;
                }
            }
            var used = new HashSet<string>(cur.fig.Nodes.Values);
            foreach (KeyValuePair<string, Vector2> p in cur.fig.Points)
            {
                if (used.Contains(p.Key)) continue;
                RectTransform dot = ClassesStyle.Fixed(_map, "Shape_" + p.Key, new Vector2(0.5f, 0.5f), new Vector2(7f, 7f));
                dot.anchoredPosition = At(p.Value);
                ClassesStyle.Fill(dot, ClassesStyle.Circle(16, ShapeStar, ShapeStar, 1f), Color.white);
            }
            foreach (ParagonSource.Star b in bridges)   // a short line from each bridge toward the map
                figLines.Add(Anchor(pos[b.Id]), Anchor(pos[b.Id] + new Vector2(pos[b.Id].x < 0f ? 70f : -70f, 0f)), 1.4f, FigureLine);
            figRect.SetAsFirstSibling(); anchors.SetAsFirstSibling();
            if (_skyImage != null) _skyImage.transform.SetAsFirstSibling();
            // the title bar is a label obstacle too
            _labelBlocks.Add(new Rect(-w * 0.5f, h * 0.5f - TopBar, w, TopBar));

            _lines = ClassesStyle.Rect(_map, "Lines", Vector2.zero, Vector2.one).gameObject.AddComponent<UILineGraphic>();
            _lines.raycastTarget = false;
            var rects = new Dictionary<string, RectTransform>();
            var byId = drawn.ToDictionary(s => s.Id);
            foreach (ParagonSource.Star s in drawn) rects[s.Id] = DrawStar(tree, s, pos[s.Id]);
            var done = new HashSet<string>();
            foreach (ParagonSource.Star s in drawn)
                foreach (string l in s.Links)
                {
                    if (!rects.ContainsKey(l) || !done.Add(string.CompareOrdinal(s.Id, l) < 0 ? s.Id + "|" + l : l + "|" + s.Id)) continue;
                    bool lit = s.Rank > 0 && byId[l].Rank > 0, hover = s.Id == _selected || l == _selected;
                    if (lit || hover) _lines.Add(rects[s.Id], rects[l], lit ? 3f : 2.2f, lit ? tree.Colour : LinkHover, !lit);
                }
            PlaceLabels(drawn, pos);
            foreach (TextMeshProUGUI t in _map.GetComponentsInChildren<TextMeshProUGUI>(true).ToList())
                if (t.name.StartsWith("Label_") && !t.name.EndsWith("_Shadow")) Shadowed(t);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] tree {tree.Id}: one figure {idx + 1}/{figs.Count} '{cur.fig.Title}' ({cur.c.Name}), {drawn.Count} stars ({bridges.Count} bridges), {segs} segments, scale x{scale:0.##}, sky {(_skyImage != null && _skyImage.enabled ? "on" : "none")}");
            return true;
        }

        private static int Gate(ParagonSource.Tree tree, int tier) =>
            tree.TierGates != null && tier - 1 < tree.TierGates.Length && tier >= 1 ? tree.TierGates[tier - 1] : 0;

        /// <summary>Map-local positions (centre 0,0, y UP). yaml pos wins; (0,0) is generated by the mockup rule and logged.</summary>
        private static Dictionary<string, Vector2> Positions(ParagonSource.Tree tree, List<ParagonSource.Star> stars, float outer)
        {
            var result = new Dictionary<string, Vector2>();
            var angle = tree.Clusters.ToDictionary(c => c.Id, c => c.AngleDeg);
            int generated = 0;
            foreach (ParagonSource.Star s in stars)
            {
                if (s.Pos != Vector2.zero) { result[s.Id] = new Vector2((s.Pos.x - 0.5f) / 0.46f, (0.5f - s.Pos.y) / 0.46f) * outer; continue; }
                generated++;
                float deg;
                float r = Ring[Mathf.Clamp(s.Tier, 1, 4)];
                if (s.Bridge)
                {
                    // between the two clusters it links, between its tier ring and the next
                    var ends = s.Links.Select(l => stars.FirstOrDefault(o => o.Id == l)?.Cluster).Where(c => c != null && angle.ContainsKey(c)).Distinct().Take(2).ToList();
                    float a1 = ends.Count > 0 ? angle[ends[0]] : 0f, a2 = ends.Count > 1 ? angle[ends[1]] : a1;
                    if (Mathf.Abs(a1 - a2) > 180f) { if (a1 < a2) a1 += 360f; else a2 += 360f; }
                    deg = (a1 + a2) * 0.5f;
                    r = (r + Ring[Mathf.Clamp(s.Tier + 1, 1, 4)]) * 0.5f;
                }
                else
                {
                    List<ParagonSource.Star> row = stars.Where(o => !o.Bridge && o.Cluster == s.Cluster && o.Tier == s.Tier)
                        .OrderBy(o => o.Slottable ? 1 : 0).ThenBy(o => o.Id, System.StringComparer.Ordinal).ToList();
                    int i = row.IndexOf(s);
                    float spread = Spread[Mathf.Clamp(s.Tier, 1, 4)];
                    float off = row.Count <= 1 ? 0f : -spread + 2f * spread * i / (row.Count - 1);
                    deg = (angle.TryGetValue(s.Cluster ?? "", out float ca) ? ca : 0f) + off;
                }
                float rad = deg * Mathf.Deg2Rad;
                result[s.Id] = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * (r * outer);   // screen degrees are y-down
            }
            if (generated > 0) FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] tree {tree.Id}: {generated} star(s) had no pos in the data; placed by the layout rule");
            return result;
        }

        private RectTransform DrawStar(ParagonSource.Tree tree, ParagonSource.Star s, Vector2 at)
        {
            float size = (s.Capstone ? 30f : s.Slottable ? 22f : 16f) * _starMul;   // _starMul enlarges the one-figure view
            RectTransform rect = ClassesStyle.Fixed(_map, "Star_" + s.Id, new Vector2(0.5f, 0.5f), new Vector2(size + 12f, size + 12f));
            rect.anchoredPosition = at;
            Color col = s.Rank > 0 ? tree.Colour : s.Unlocked ? OpenStar : LockedStar;
            // hit area (the hover/click target) and the learned glow
            Image hit = ClassesStyle.Fill(rect, ClassesStyle.Circle(64, s.Rank > 0 ? new Color(col.r, col.g, col.b, 0.25f) : Color.clear, Color.clear, 0f), Color.white, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            string id = s.Id;
            button.onClick.AddListener(() => { _selected = id; _status = null; Refresh(); });

            RectTransform shape = ClassesStyle.Fixed(rect, "Shape", new Vector2(0.5f, 0.5f), new Vector2(size, size));
            if (s.Slottable)
            {
                shape.sizeDelta = new Vector2(size * 0.72f, size * 0.72f);
                shape.localRotation = Quaternion.Euler(0f, 0f, 45f);
                bool slotted = ParagonSource.Slotted(tree.Id).Contains(s.Id);
                ClassesStyle.Fill(shape, ClassesStyle.Box(2, slotted ? col : ClassesStyle.Hex("#14121c"), col, 2), Color.white);
            }
            else
                ClassesStyle.Fill(shape, ClassesStyle.Circle(64, s.Rank > 0 ? col : ClassesStyle.Hex("#14121c"), col, 6f), Color.white);
            if (s.Capstone)
            {
                RectTransform halo = ClassesStyle.Fixed(rect, "Halo", new Vector2(0.5f, 0.5f), new Vector2(size + 10f, size + 10f));
                ClassesStyle.Fill(halo, ClassesStyle.Circle(96, Color.clear, s.Unlocked ? ClassesStyle.Gold : ClassesStyle.LockedStroke, 4f), Color.white);
            }
            if (s.Id == _selected)
            {
                RectTransform sel = ClassesStyle.Fixed(rect, "Selected", new Vector2(0.5f, 0.5f), new Vector2(size + 18f, size + 18f));
                ClassesStyle.Fill(sel, ClassesStyle.Circle(96, Color.clear, ClassesStyle.Gold, 4f), Color.white);
            }
            // every star: the full name + ParagonUi's explanation on hover
            FiresHoverTip.Attach(rect.gameObject, s.Name, "", () => ParagonSource.Tooltip(id));
            return rect;
        }

        /// <summary>
        /// Short names under (else above, right, left of) each star, never overlapping another label or star.
        /// Passives also get their rank. A label with no clear spot is left off; its hover still names it (logged).
        /// </summary>
        // In figure mode the figure's line segments and title bar are label obstacles too, so labels never sit on the lines.
        private readonly List<Vector2[]> _labelSegments = new List<Vector2[]>();
        private readonly List<Rect> _labelBlocks = new List<Rect>();

        private static bool SegmentHits(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0f, t1 = 1f, dx = b.x - a.x, dy = b.y - a.y;
            float[] p = { -dx, dx, -dy, dy }, q = { a.x - r.xMin, r.xMax - a.x, a.y - r.yMin, r.yMax - a.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f) { if (q[i] < 0f) return false; continue; }
                float t = q[i] / p[i];
                if (p[i] < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return true;
        }

        private void PlaceLabels(List<ParagonSource.Star> stars, Dictionary<string, Vector2> pos)
        {
            var taken = new List<Rect>(_labelBlocks);
            foreach (ParagonSource.Star s in stars)
            {
                float r = (s.Capstone ? 30f : s.Slottable ? 22f : 16f) * _starMul * 0.5f + 2f;
                taken.Add(new Rect(pos[s.Id].x - r, pos[s.Id].y - r, 2f * r, 2f * r));
            }
            int dropped = 0, labels = 0; var droppedNames = new List<string>(); var fallbackNames = new List<string>();
            // capstones and learned stars claim their spots first
            foreach (ParagonSource.Star s in stars.OrderByDescending(s => s.Capstone).ThenByDescending(s => s.Rank > 0))
            {
                string name = string.IsNullOrEmpty(s.ShortName) ? s.Name : s.ShortName;
                string text = s.Slottable ? name : name + "\n" + s.Rank + "/" + s.MaxRank;
                TextMeshProUGUI label = ClassesStyle.Label(_map, "Label_" + s.Id, text, LabelSize * _labelMul,
                    s.Unlocked ? ClassesStyle.Text : ClassesStyle.LockedText, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                // a capstone's label clears its halo (size + 10), not just the star
                float r = ((s.Capstone ? 30f : s.Slottable ? 22f : 16f) * _starMul + (s.Capstone ? 10f : 0f)) * 0.5f + 4f;
                Vector2 p = pos[s.Id];
                // Small screens crowd the labels: before giving up, the same two rings are tried with the name wrapped onto two lines (multi-word names), then at 0.85 and 0.72 size. Logged per figure (fallbacks named).
                float baseSize = LabelSize * _labelMul;
                int sp = name.IndexOf(' ');
                string wrapped = sp > 0 ? name.Substring(0, sp) + "\n" + name.Substring(sp + 1) : null;
                string Compose(string n) => s.Slottable ? n : n + "\n" + s.Rank + "/" + s.MaxRank;
                var variants = new List<(string text, float mul, string how)> { (text, 1f, null) };
                if (wrapped != null) variants.Add((Compose(wrapped), 1f, "wrapped"));
                variants.Add((text, 0.85f, "x0.85"));
                if (wrapped != null) variants.Add((Compose(wrapped), 0.85f, "wrapped x0.85"));
                variants.Add((text, 0.72f, "x0.72"));
                if (wrapped != null) variants.Add((Compose(wrapped), 0.72f, "wrapped x0.72"));
                bool placed = false; labels++;
                foreach (var v in variants)
                {
                    label.text = v.text;
                    label.fontSize = baseSize * v.mul;
                    Vector2 size = label.GetPreferredValues(v.text) + new Vector2(2f, 0f);
                    label.rectTransform.sizeDelta = size;
                    Vector2[] tries =
                    {
                        p + new Vector2(0f, -r - size.y * 0.5f), p + new Vector2(0f, r + size.y * 0.5f),
                        p + new Vector2(r + size.x * 0.5f, 0f), p - new Vector2(r + size.x * 0.5f, 0f),
                        p + new Vector2(r + size.x * 0.5f, -r - size.y * 0.5f), p + new Vector2(-r - size.x * 0.5f, -r - size.y * 0.5f),
                        p + new Vector2(r + size.x * 0.5f, r + size.y * 0.5f), p + new Vector2(-r - size.x * 0.5f, r + size.y * 0.5f),
                        // a second, wider ring before giving up
                        p + new Vector2(0f, -2f * r - size.y * 0.5f - 6f), p + new Vector2(0f, 2f * r + size.y * 0.5f + 6f),
                        p + new Vector2(2f * r + size.x * 0.5f + 6f, 0f), p - new Vector2(2f * r + size.x * 0.5f + 6f, 0f),
                        p + new Vector2(2f * r + size.x * 0.5f, -2f * r - size.y * 0.5f), p + new Vector2(-2f * r - size.x * 0.5f, -2f * r - size.y * 0.5f),
                        p + new Vector2(2f * r + size.x * 0.5f, 2f * r + size.y * 0.5f), p + new Vector2(-2f * r - size.x * 0.5f, 2f * r + size.y * 0.5f)
                    };
                    foreach (Vector2 c in tries)
                    {
                        var box = new Rect(c - size * 0.5f, size);
                        if (taken.Any(t => t.Overlaps(box)) || !Inside(box)) continue;
                        if (_labelSegments.Any(sg => SegmentHits(sg[0], sg[1], box))) continue;
                        label.rectTransform.anchoredPosition = c;
                        taken.Add(box);
                        placed = true;
                        break;
                    }
                    if (placed) { if (v.how != null) fallbackNames.Add(name + " (" + v.how + ")"); break; }
                }
                if (!placed) { Object.Destroy(label.gameObject); dropped++; droppedNames.Add(name); }
            }
            // Logged at the default level, per figure, with the names, so the automated class test can count dropped labels.
            // Same text once per figure + count (no per-frame spam).
            int fig = _figIndex.TryGetValue(_tree ?? "", out int fi) ? fi : 0;
            string key = $"{_tree}|{fig}|{dropped}|{_map.rect.width:0}";
            if (_labelLogged.Add(key))
            {
                string msg = $"[Paragon] labels: tree {_tree} figure {fig}: {labels - dropped}/{labels} placed at {_map.rect.width:0}x{_map.rect.height:0}" +
                    (fallbackNames.Count > 0 ? $"; fitted by fallback: {string.Join(", ", fallbackNames)}" : "") +
                    (dropped > 0 ? $"; DROPPED {dropped}: {string.Join(", ", droppedNames)} (hover names them)" : "");
                if (dropped > 0) FiresRPGClassesPlugin.Log?.LogWarning(msg); else FiresRPGClassesPlugin.Log?.LogDebug(msg);
            }
        }

        private bool Inside(Rect box)
        {
            float hw = _map.rect.width * 0.5f - 4f, hh = _map.rect.height * 0.5f - 4f;
            return box.xMin > -hw && box.xMax < hw && box.yMin > -hh && box.yMax < hh;
        }

        // ---- side column: card, tier progress, slots, legend --------------------------------------------------

        // A long description could bleed past the bottom edge, so the panel is built, measured against its inner height, and rebuilt with the description at a smaller size (1.3 -> 1.15 -> 1.0, then
        // at most 2 lines per description line with an ellipsis) until it fits. The legend is ONE hover row ("Legend (hover)").
        private float _descMul = 1f;
        private int _descMaxLines;

        private void BuildSide(List<ParagonSource.Tree> trees, ParagonSource.Tree tree, List<ParagonSource.Star> stars)
        {
            float[] muls = ParagonFigures.On ? new[] { 1f, 0.885f, 0.77f, 0.77f } : new[] { 1f, 1f };
            float inner = _side.rect.height - 12f, used = 0f;
            for (int attempt = 0; attempt < muls.Length; attempt++)
            {
                _descMul = muls[attempt];
                _descMaxLines = attempt == muls.Length - 1 ? 2 : 0;
                used = BuildSideOnce(trees, tree, stars);
                if (used <= inner || inner < 50f) break;
            }
            string fit = used <= inner ? "fits" : "OVERFLOW";
            string key = _selected + "|" + _side.rect.height.ToString("0") + "|" + fit;
            if (_sideLogged != key)
            {
                _sideLogged = key;
                { string msg = $"[Paragon] side panel: content {used:0} of {inner:0} ({fit}, description x{_descMul:0.##}{(_descMaxLines > 0 ? ", 2 lines max" : "")}) for '{_selected}'"; if (used > inner) FiresRPGClassesPlugin.Log?.LogWarning(msg); else FiresRPGClassesPlugin.Log?.LogDebug(msg); }   // visible at the default log level
            }
        }
        private string _sideLogged;
        private static readonly HashSet<string> _tickLogged = new HashSet<string>();

        private float BuildSideOnce(List<ParagonSource.Tree> trees, ParagonSource.Tree tree, List<ParagonSource.Star> stars)
        {
            ClassesStyle.Clear(_side);
            ParagonSource.Star sel = stars.FirstOrDefault(s => s.Id == _selected);
            float y = 12f;
            if (sel != null)
            {
                ClassesStyle.Label(Row(ref y, Fs(16f)), "Kicker", "SELECTED STAR", Fs(11f), ClassesStyle.Muted, Vector2.zero, Vector2.one);
                ClassesStyle.Label(Row(ref y, Fs(26f)), "Name", sel.Name, Fs(20f), ClassesStyle.Gold, Vector2.zero, Vector2.one, TextAlignmentOptions.MidlineLeft, true);
                string[] lines = ParagonSource.TooltipLines(sel.Id);
                foreach (string line in lines.Skip(1))   // [0] is the title, shown above
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    TextMeshProUGUI l = ClassesStyle.Label(_side, "Line", line, Fs(13f) * _descMul, ClassesStyle.Text, new Vector2(0f, 1f), new Vector2(1f, 1f));
                    l.richText = true;
                    float lh = l.GetPreferredValues(line, SideW - 28f, 999f).y + 2f;
                    if (_descMaxLines > 0)
                    {
                        l.maxVisibleLines = _descMaxLines; l.overflowMode = TextOverflowModes.Ellipsis;
                        lh = Mathf.Min(lh, l.fontSize * 1.25f * _descMaxLines + 2f);
                        FiresHoverTip.Attach(l.gameObject, sel.Name, line);
                    }
                    Place(l.rectTransform, ref y, lh);
                }
                y += 6f;
                RectTransform buttons = Row(ref y, Fs(34f));
                float bx = 0f;
                bool full = sel.Rank >= sel.MaxRank && sel.MaxRank > 0;
                Button buy = ClassesStyle.Button(buttons, "BuyRank", full ? "Max rank" : "Buy rank", ClassesStyle.ButtonKind.Primary,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), OnBuy, 15f);
                ClassesStyle.Paint(buy, ClassesStyle.ButtonKind.Primary, sel.Unlocked && !full && ParagonSource.Unspent > 0);
                bx += FitButton(buy, bx, 34f) + 8f;
                if (sel.Slottable)
                {
                    bool isSlotted = ParagonSource.Slotted(tree.Id).Contains(sel.Id);
                    Button slot = ClassesStyle.Button(buttons, "SlotStar", isSlotted ? "Unslot" : "Slot", ClassesStyle.ButtonKind.Outline,
                        new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), OnSlot, 15f);
                    ClassesStyle.Paint(slot, ClassesStyle.ButtonKind.Outline, sel.Rank > 0 || isSlotted);
                    FitButton(slot, bx, 34f);
                }
                if (!string.IsNullOrEmpty(_status))
                    ClassesStyle.Label(Row(ref y, Fs(34f)), "Status", _status, Fs(13f), ClassesStyle.Orange, Vector2.zero, Vector2.one);
            }
            y += 8f;
            Divider(ref y);

            // tier progress for this tree
            int spent = ParagonSource.SpentInTree(tree.Id);
            int last = Gate(tree, 4);
            int next = Enumerable.Range(1, 4).Select(t => Gate(tree, t)).FirstOrDefault(g => g > spent);
            string prog = next > 0 ? $"Spent in {tree.Name}: {spent}  -  next tier at {next}" : $"Spent in {tree.Name}: {spent}  -  every tier open";
            ClassesStyle.Label(Row(ref y, Fs(18f)), "Progress", prog, Fs(13f), ClassesStyle.Text, Vector2.zero, Vector2.one);
            // the bar is inset by TierInset on both sides, so the last label centres
            // on its own tick fully inside the panel; a small tick mark on the bar at each real threshold (gate / last gate).
            const float TierInset = 22f;
            RectTransform barRow = Row(ref y, Fs(12f));
            barRow.offsetMin = new Vector2(barRow.offsetMin.x + TierInset, barRow.offsetMin.y);
            barRow.offsetMax = new Vector2(barRow.offsetMax.x - TierInset, barRow.offsetMax.y);
            ClassesStyle.Bar(barRow, "TierBar", Vector2.zero, Vector2.one, last > 0 ? Mathf.Clamp01(spent / (float)last) : 1f);
            RectTransform ticks = Row(ref y, Fs(16f));
            ticks.offsetMin = new Vector2(ticks.offsetMin.x + TierInset, ticks.offsetMin.y);
            ticks.offsetMax = new Vector2(ticks.offsetMax.x - TierInset, ticks.offsetMax.y);
            var tickLog = new System.Text.StringBuilder();
            for (int t = 2; t <= 4; t++)
            {
                float f = last > 0 ? Gate(tree, t) / (float)last : 0f;
                RectTransform mark = ClassesStyle.Rect(barRow, "TickMark" + t, new Vector2(f, 0f), new Vector2(f, 1f));
                mark.sizeDelta = new Vector2(2f, 6f);
                ClassesStyle.Fill(mark, null, ClassesStyle.Text);
                TextMeshProUGUI tick = ClassesStyle.Label(ticks, "Tick" + t, "T" + t, Fs(11f), ClassesStyle.Muted, new Vector2(f, 0f), new Vector2(f, 1f), TextAlignmentOptions.Center);
                tick.rectTransform.sizeDelta = new Vector2(28f, 0f);
                tickLog.Append($" T{t}@{f:0.###} (gate {Gate(tree, t)})");
            }
            if (_tickLogged.Add(tree.Id))
                FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] tier bar {tree.Id}: inset {TierInset:0} each side; label centre = tick x for{tickLog}");
            y += 6f;
            Divider(ref y);

            // the 4 slots of every tree
            ClassesStyle.Label(Row(ref y, Fs(16f)), "SlotsTitle", "SLOTTED STARS  (4 per tree; swap out of combat)", Fs(11f), ClassesStyle.Muted, Vector2.zero, Vector2.one);
            foreach (ParagonSource.Tree t in trees)
            {
                RectTransform row = Row(ref y, Fs(46f));
                // pin the label's left edge to the row: a zero-width anchor with a centred pivot would put half of it
                // left of the panel.
                RectTransform treeLabel = ClassesStyle.Label(row, "Tree", t.Name, Fs(14f), t.Colour, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    TextAlignmentOptions.MidlineLeft, true).rectTransform;
                treeLabel.pivot = new Vector2(0f, 0.5f);
                treeLabel.anchoredPosition = Vector2.zero;
                treeLabel.sizeDelta = new Vector2(ParagonFigures.On ? 104f : 80f, 0f);
                string[] slotted = ParagonSource.Slotted(t.Id);
                List<ParagonSource.Star> treeStars = t.Id == tree.Id ? stars : ParagonSource.Stars(t.Id);
                for (int i = 0; i < 4; i++)
                {
                    string starId = i < slotted.Length ? slotted[i] : null;
                    ParagonSource.Star star = starId != null ? treeStars.FirstOrDefault(s => s.Id == starId) : null;
                    RectTransform cell = ClassesStyle.Fixed(row, "Slot_" + t.Id + "_" + i, new Vector2(0f, 0.5f), (ParagonFigures.On ? new Vector2(SlotCellW(), 56f) : new Vector2(54f, 44f)));
                    cell.pivot = new Vector2(0f, 0.5f);
                    cell.anchoredPosition = (ParagonFigures.On ? new Vector2(SlotLabelW + 6f + i * (SlotCellW() + SlotGap), 0f) : new Vector2(84f + i * 60f, 0f));
                    Image face = ClassesStyle.Fill(cell, ClassesStyle.Box(4, ClassesStyle.Hex("#1a140f"), star != null ? t.Colour : ClassesStyle.Line, 2), Color.white, true);
                    string text = star != null ? (string.IsNullOrEmpty(star.ShortName) ? star.Name : star.ShortName) : "empty";
                    TextMeshProUGUI cap = ClassesStyle.Label(cell, "Name", text, 10f, star != null ? ClassesStyle.Text : ClassesStyle.LockedText, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
                    cap.enableAutoSizing = true; cap.fontSizeMin = Fs(9f); cap.fontSizeMax = Fs(11f);   // wraps to 2 lines, ellipsis past that (hover names it)
                    cap.textWrappingMode = TextWrappingModes.Normal; cap.overflowMode = TextOverflowModes.Ellipsis; cap.maxVisibleLines = 2;
                    if (star != null)
                    {
                        string tid = t.Id, sid = star.Id; int slot = i;
                        Button b = cell.gameObject.AddComponent<Button>();
                        b.targetGraphic = face;
                        b.onClick.AddListener(() => { _status = ParagonSource.TryUnslot(tid, slot) ? null : "Can't unslot in combat."; Refresh(); });
                        FiresHoverTip.Attach(cell.gameObject, star.Name, "Click to unslot.", () => ParagonSource.Tooltip(sid) + "\n\nClick to unslot.");
                    }
                    else FiresHoverTip.Attach(cell.gameObject, "Empty slot", "Select a learned slottable star and press Slot.");
                }
            }
            y += 4f;
            Divider(ref y);
            string legendText = string.Join("\n", new[]
            {
                "Circle = passive (ranks)    Diamond = slottable",
                "Big diamond with a halo = capstone",
                ParagonFigures.On ? "Each branch is a constellation; links show when learned or selected" : "Rings: tier 1 (centre) to tier 4 (outer)",
                "Coloured = learned   White = open   Grey = locked",
                "Hover any star for what it does"
            });
            if (ParagonFigures.On)
            {
                // one row; the five lines are its hover
                TextMeshProUGUI lg = ClassesStyle.Label(Row(ref y, Fs(18f)), "Legend", "Legend (hover)   -   hover any star for what it does", Fs(12f), ClassesStyle.Muted, Vector2.zero, Vector2.one);
                lg.raycastTarget = true;
                FiresHoverTip.Attach(lg.gameObject, "Paragon legend", legendText);
            }
            else
                foreach (string legend in legendText.Split('\n'))
                    ClassesStyle.Label(Row(ref y, Fs(16f)), "Legend", legend, Fs(12f), ClassesStyle.Muted, Vector2.zero, Vector2.one);
            return y;
        }

        private void OnBuy()
        {
            _status = ParagonSource.TryBuy(_selected, out string why) ? null : why;
            Refresh();
        }

        private void OnSlot()
        {
            string[] slotted = ParagonSource.Slotted(_tree);
            int at = System.Array.IndexOf(slotted, _selected);
            if (at >= 0) { _status = ParagonSource.TryUnslot(_tree, at) ? null : "Can't unslot in combat."; Refresh(); return; }
            int free = System.Array.FindIndex(slotted, s => string.IsNullOrEmpty(s));
            if (free < 0) { _status = "All 4 slots are full: click a slotted star below to free one."; Refresh(); return; }
            _status = ParagonSource.TrySlot(_tree, free, _selected, out string why) ? null : why;
            Refresh();
        }

        // ---- layout helpers ----------------------------------------------------------------------------------

        private RectTransform Row(ref float y, float height)
        {
            RectTransform r = ClassesStyle.Rect(_side, "Row", new Vector2(0f, 1f), new Vector2(1f, 1f));
            Place(r, ref y, height);
            return r;
        }

        private static void Place(RectTransform r, ref float y, float height)
        {
            r.pivot = new Vector2(0.5f, 1f);
            r.offsetMin = new Vector2(14f, -y - height);
            r.offsetMax = new Vector2(-14f, -y);
            y += height + 2f;
        }

        private void Divider(ref float y)
        {
            RectTransform d = Row(ref y, 1f);
            ClassesStyle.Fill(d, null, ClassesStyle.Line);
            y += 6f;
        }

        /// <summary>Sizes a button to its caption plus padding so labels always fit their button; returns its width.</summary>
        private static float FitButton(Button b, float x, float height, bool fromRight = false)
        {
            TMP_Text cap = b.GetComponentInChildren<TMP_Text>(true);
            cap.textWrappingMode = TextWrappingModes.NoWrap;
            cap.overflowMode = TextOverflowModes.Overflow;
            float width = Mathf.Max(64f, cap.GetPreferredValues(cap.text).x + 32f);
            RectTransform rect = b.GetComponent<RectTransform>();
            rect.pivot = new Vector2(fromRight ? 1f : 0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(fromRight ? 0f : x, 0f);
            return width;
        }

        private static float FitLabel(TextMeshProUGUI label, float x)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float width = label.GetPreferredValues(label.text).x + 4f;
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(width, 0f);
            label.rectTransform.anchoredPosition = new Vector2(x, 0f);
            return width;
        }
    }
}
