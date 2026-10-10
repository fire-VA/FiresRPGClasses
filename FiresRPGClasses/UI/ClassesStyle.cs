using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The look of Fire's Classes mockups (Tools\ClassesUI_Mockups, "layout B + tree T3"): the parchment palette,
    /// 44 px targets and the small widget set every board is made of - tabs, pills, the three button styles,
    /// wells, badges, bars and rings. Frames are generated here rather than shipped, so the window needs no bundle.
    /// Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public static class ClassesStyle
    {
        public static readonly Color Page = Hex("#16110c");
        public static readonly Color Panel = Hex("#241a11");
        public static readonly Color Border = Hex("#8a6538");
        public static readonly Color Line = Hex("#4a3824");
        public static readonly Color Well = Hex("#1b140d");
        public static readonly Color Selected = Hex("#3a2a12");
        public static readonly Color Neutral = Hex("#2e2113");
        public static readonly Color Tooltip = Hex("#2a1f14");
        public static readonly Color Gold = Hex("#f0b44c");
        public static readonly Color GoldHover = Hex("#ffd48a");
        public static readonly Color Text = Hex("#efe2c4");
        public static readonly Color Muted = Hex("#b3a181");
        public static readonly Color LockedText = Hex("#9a8768");
        public static readonly Color LockedSub = Hex("#7d6b53");
        public static readonly Color LockedStroke = Hex("#5c4a35");
        public static readonly Color Available = Hex("#e8d9b5");
        public static readonly Color AvailableLine = Hex("#cdb98f");
        public static readonly Color Blue = Hex("#6fa9cc");
        public static readonly Color Purple = Hex("#c39be8");
        public static readonly Color Amber = Hex("#e0b24a");
        public static readonly Color ActiveGreen = Hex("#3fd67f");
        public static readonly Color Orange = Hex("#e0843a");

        public const float Target = 44f;
        public const float HeaderHeight = 60f;
        public const float PillRowHeight = 58f;

        /// <summary>
        /// Each archetype's badge colour. Tank, Paladin, Berserker, Rogue and Healer are the PassivesView board's; the board
        /// shows no Monk, Ranger or Mage card, so those three are picked to stay apart from the rest.
        /// </summary>
        public static Color ArchetypeColor(ArchetypeClass archetype)
        {
            switch (archetype)
            {
                case ArchetypeClass.Tank: return TankColor;
                case ArchetypeClass.Paladin: return PaladinColor;
                case ArchetypeClass.Berserker: return BerserkerColor;
                case ArchetypeClass.Rogue: return RogueColor;
                case ArchetypeClass.Monk: return MonkColor;
                case ArchetypeClass.Ranger: return RangerColor;
                case ArchetypeClass.Mage: return MageColor;
                case ArchetypeClass.Healer: return ActiveGreen;
                case ArchetypeClass.Druid: return DruidColor;
                case ArchetypeClass.Necromancer: return NecromancerColor;
                default: return Muted;
            }
        }

        private static readonly Color TankColor = Hex("#8fa3b8");
        private static readonly Color PaladinColor = Hex("#e8c46a");
        private static readonly Color BerserkerColor = Hex("#d9644a");
        private static readonly Color RogueColor = Hex("#9b7fd1");
        private static readonly Color MonkColor = Hex("#6fc2b4");
        private static readonly Color RangerColor = Hex("#a3bf5f");
        private static readonly Color MageColor = Hex("#7f98e8");
        private static readonly Color DruidColor = Hex("#7fb069");          // moss green (Fire, 10-07)
        private static readonly Color NecromancerColor = Hex("#6ad1e8");    // spectral cyan (Fire, 10-07; apart from Monk's teal)

        public static Color Hex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;

        // ---- sprites ----------------------------------------------------------------------------------------

        private static readonly Dictionary<string, Sprite> Rings = new Dictionary<string, Sprite>();

        /// <summary>A rounded 9-slice with a baked border (Core's generator; cached there).</summary>
        public static Sprite Box(int radius, Color fill, Color? border = null, int borderPx = 0) =>
            FiresRoundedSkin.RoundedSprite(radius, fill, border, borderPx);

        /// <summary>
        /// A circle of <paramref name="diameter"/> px: a fill, an optional ring, and the ring optionally dashed (the
        /// boards' "new skill" frame). Drawn at 2x and anti-aliased by coverage, then cached per look.
        /// </summary>
        public static Sprite Circle(int diameter, Color fill, Color ring, float ringPx, bool dashed = false)
        {
            string key = diameter + "_" + fill + "_" + ring + "_" + ringPx + "_" + dashed;
            if (Rings.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            int size = diameter * 2;
            float radius = size * 0.5f;
            float stroke = ringPx * 2f;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius, dy = y + 0.5f - radius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Clamp01(radius - dist);
                    float inRing = Mathf.Clamp01(dist - (radius - stroke));
                    if (dashed && inRing > 0f)
                    {
                        // 12 dashes around the ring, 60% on.
                        float turn = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
                        if ((turn * 12f) % 1f > 0.6f) inRing = 0f;
                    }
                    Color color = Color.Lerp(fill, ring, ringPx > 0f ? inRing : 0f);
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * outer));
                }
            }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 200f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Rings[key] = sprite;
            return sprite;
        }

        private static readonly Dictionary<string, Sprite> Dashes = new Dictionary<string, Sprite>();

        private const int DashPeriod = 8;
        private const int DashOn = 4;

        /// <summary>
        /// A rounded box with a dashed border (the boards' locked slots, "Variant" badges and notes). The sprite is one dash
        /// period between its corners and is drawn Tiled, so the edges repeat that period and the dashes keep their length
        /// at any size. Use <see cref="FillDashed"/>.
        /// </summary>
        public static Sprite DashedBox(int radius, Color fill, Color border, int borderPx)
        {
            string key = radius + "_" + fill + "_" + border + "_" + borderPx;
            if (Dashes.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            int size = radius * 2 + DashPeriod;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded rect (negative inside).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float coverage = Mathf.Clamp01(0.5f - d);
                    float inBorder = Mathf.Clamp01(d + borderPx + 0.5f);

                    // The middle period of each edge carries one dash; the corners are solid.
                    bool midX = x >= radius && x < radius + DashPeriod, midY = y >= radius && y < radius + DashPeriod;
                    if (midX && !midY && (x - radius) % DashPeriod >= DashOn) inBorder = 0f;
                    else if (midY && !midX && (y - radius) % DashPeriod >= DashOn) inBorder = 0f;

                    Color color = Color.Lerp(fill, border, inBorder);
                    float alpha = Mathf.Lerp(fill.a, border.a, inBorder) * coverage;
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
                }
            }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Dashes[key] = sprite;
            return sprite;
        }

        /// <summary>A rect drawn as a dashed box (Tiled, so the dashes repeat rather than stretch).</summary>
        public static Image FillDashed(RectTransform rect, int radius, Color fill, Color border, int borderPx = 1)
        {
            Image image = Fill(rect, DashedBox(radius, fill, border, borderPx), Color.white);
            image.type = Image.Type.Tiled;
            image.fillCenter = true;
            return image;
        }

        // ---- scaffolding -----------------------------------------------------------------------------------

        /// <summary>A plain RectTransform child with the given anchors, zero offsets.</summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>A rect of a fixed pixel size centred on an anchor point.</summary>
        public static RectTransform Fixed(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            RectTransform rect = Rect(parent, name, anchor, anchor);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        public static Image Fill(RectTransform rect, Sprite sprite, Color tint, bool raycast = false)
        {
            Image image = rect.gameObject.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = tint;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>The parchment panel: fill, 2 px border, radius 8.</summary>
        public static Image PanelFrame(RectTransform rect) => Fill(rect, Box(8, Panel, Border, 2), Color.white, true);

        /// <summary>An inset well: darker fill, 1 px inner line, radius 8.</summary>
        public static Image WellFrame(RectTransform rect) => Fill(rect, Box(8, Well, Line, 1), Color.white);

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
            bool bold = false)
        {
            TextMeshProUGUI label = UIBuilderHelper.CreateLabel(parent, name, text, size, color, anchorMin, anchorMax, align);
            TMP_FontAsset serif = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaSerif);
            if (serif != null) label.font = serif;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        // ---- buttons ---------------------------------------------------------------------------------------

        public enum ButtonKind { Primary, Outline, Neutral }

        /// <summary>
        /// A 44 px button in one of the boards' three styles. The GameObject is named <paramref name="name"/>
        /// (class_test finds some buttons by name) and the caption is the first TMP text under it.
        /// </summary>
        public static Button Button(Transform parent, string name, string caption, ButtonKind kind,
            Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick, float fontSize = 16f)
        {
            RectTransform rect = Rect(parent, name, anchorMin, anchorMax);
            Image face = Fill(rect, null, Color.white, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            Label(rect, "Label", caption, fontSize, Color.white, Vector2.zero, Vector2.one, TextAlignmentOptions.Center,
                kind == ButtonKind.Primary);
            Paint(button, kind, true);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>Re-skins a button for its kind and whether it can be pressed ("Unlocks at level 40").</summary>
        public static void Paint(Button button, ButtonKind kind, bool interactable)
        {
            if (button == null) return;
            var face = button.targetGraphic as Image;
            TMP_Text caption = button.GetComponentInChildren<TMP_Text>(true);
            button.interactable = interactable;

            Sprite sprite;
            Color text;
            switch (kind)
            {
                case ButtonKind.Primary:
                    sprite = interactable ? Box(6, Selected, Gold, 2) : Box(6, Color.clear, Line, 1);
                    text = interactable ? Gold : LockedText;
                    break;
                case ButtonKind.Neutral:
                    sprite = Box(6, Neutral, Border, 1);
                    text = interactable ? Text : LockedText;
                    break;
                default:
                    sprite = Box(6, Color.clear, Line, 1);
                    text = interactable ? Muted : LockedText;
                    break;
            }
            if (face != null)
            {
                face.sprite = sprite;
                face.type = Image.Type.Sliced;
                face.color = Color.white;
            }
            if (caption != null) caption.color = text;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.08f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.75f);
            colors.colorMultiplier = 1.2f;
            button.colors = colors;
        }

        /// <summary>A top tab: text only, gold and underlined when active.</summary>
        public static Button Tab(Transform parent, string name, string caption, Vector2 anchorMin, Vector2 anchorMax,
            UnityAction onClick)
        {
            RectTransform rect = Rect(parent, name, anchorMin, anchorMax);
            Image hit = Fill(rect, null, new Color(0f, 0f, 0f, 0.001f), true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            Label(rect, "Label", caption, 21f, Muted, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            RectTransform underline = Rect(rect, "Underline", new Vector2(0.12f, 0f), new Vector2(0.88f, 0f));
            underline.sizeDelta = new Vector2(0f, 3f);
            underline.pivot = new Vector2(0.5f, 0f);
            Fill(underline, null, Gold).enabled = false;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        public static void PaintTab(Button tab, bool active)
        {
            if (tab == null) return;
            TMP_Text caption = tab.GetComponentInChildren<TMP_Text>(true);
            if (caption != null)
            {
                caption.color = active ? Gold : Muted;
                caption.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
            Transform underline = tab.transform.Find("Underline");
            Image bar = underline != null ? underline.GetComponent<Image>() : null;
            if (bar != null) bar.enabled = active;
        }

        /// <summary>A view pill (44 tall, radius 22) or a smaller chip.</summary>
        public static Button Pill(Transform parent, string name, string caption, Vector2 anchorMin, Vector2 anchorMax,
            UnityAction onClick, float fontSize = 17f)
        {
            RectTransform rect = Rect(parent, name, anchorMin, anchorMax);
            Image face = Fill(rect, null, Color.white, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            Label(rect, "Label", caption, fontSize, Muted, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            PaintPill(button, false);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        public static void PaintPill(Button pill, bool active)
        {
            if (pill == null) return;
            var face = pill.targetGraphic as Image;
            if (face != null)
            {
                face.sprite = active ? Box(20, Selected, Gold, 1) : Box(20, Color.clear, Line, 1);
                face.type = Image.Type.Sliced;
            }
            TMP_Text caption = pill.GetComponentInChildren<TMP_Text>(true);
            if (caption != null) caption.color = active ? Gold : Muted;
        }

        /// <summary>A thin horizontal progress bar (track + gold fill), returning the fill to size.</summary>
        public static RectTransform Bar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float fraction)
        {
            RectTransform track = Rect(parent, name, anchorMin, anchorMax);
            Fill(track, Box(4, Well, Line, 1), Color.white);
            RectTransform fill = Rect(track, "Fill", Vector2.zero, new Vector2(Mathf.Clamp01(fraction), 1f));
            Fill(fill, Box(3, Gold), Color.white);
            return fill;
        }

        /// <summary>A small outlined badge ("Front bar", "Variant").</summary>
        public static TextMeshProUGUI Badge(Transform parent, string name, string text, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = Rect(parent, name, anchorMin, anchorMax);
            Fill(rect, Box(8, Color.clear, color, 1), Color.white);
            return Label(rect, "Label", text, 12f, color, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
        }

        /// <summary>DestroyImmediate for the same reason as <see cref="ClassesTheme.ClearChildren"/>.</summary>
        public static void Clear(Transform parent) => ClassesTheme.ClearChildren(parent);
    }
}
