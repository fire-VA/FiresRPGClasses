using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresRPGClasses.Combat;
using FiresCore.Input;
using FiresCore.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    public enum ClassesTab
    {
        Skills,
        Character,
        Talents,
        Ultimates,
        Bars,
        Passives,
        Achievements,
        Class,
        Summons
    }

    /// <summary>
    /// A board with something open over it (a talent card, a list). Esc closes that first and only closes the window when
    /// nothing was open (Fire 10-01).
    /// </summary>
    public interface IClassesOverlay
    {
        /// <summary>Closes what is open; false when nothing was.</summary>
        bool CloseOverlay();
    }

    /// <summary>One board in the Classes tab. Builds once into its page, then refreshes from data.</summary>
    public interface IClassesView
    {
        ClassesTab Tab { get; }
        string Title { get; }
        void Build(RectTransform page);
        void Refresh();
    }

    /// <summary>
    /// The Classes window, laid out as Fire's mockups (Tools\ClassesUI_Mockups, layout B): three top tabs - Skills,
    /// Achievements, Classes - and inside Classes a row of pills for its views. It still opens full size over a hidden
    /// inventory (Fire, 2026-09-29: there is no room beside VAInventory), on its own canvas so no vanilla panel is forked.
    /// class_test drives it through <c>_tabButtons</c> / <c>_pages</c> / <c>_current</c>, keyed by <see cref="ClassesTab"/>:
    /// the Skills and Achievements tabs and the Classes pills are all registered there.
    /// Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class ClassesPanel : MonoBehaviour
    {
        private const string CanvasName = "FiresRPGClasses_Canvas";
        private const string Section = "UI";
        /// <summary>
        /// Vanilla's ladder, read out of IngameGui prefabs in the rip - all overrideSorting = 1:
        /// Chat 300, HUD 400, InventoryGui and its crafting page 600, Store 700, TextInput 1100,
        /// Esc menu 1700. 1000 is over the inventory and under TextInput and the Esc menu.
        /// </summary>
        private const int SortOrder = 1000;
        private const float PanelWidthFraction = 0.72f;
        private const float PanelHeightFraction = 0.78f;
        private static readonly Vector2 DefaultPanelSize = new Vector2(1440f, 920f);

        public static ClassesPanel Instance { get; private set; }

        public static KeyBinding OpenKey { get; private set; }

        public static ConfigEntry<Vector2> PanelPosition { get; private set; }
        public static ConfigEntry<Vector2> PanelSize { get; private set; }


        private readonly List<IClassesView> _views = new List<IClassesView>();
        private readonly Dictionary<ClassesTab, RectTransform> _pages = new Dictionary<ClassesTab, RectTransform>();
        private readonly Dictionary<ClassesTab, Button> _tabButtons = new Dictionary<ClassesTab, Button>();

        private GameObject _root;
        private ClassesTab _current = ClassesTab.Skills;
        private ClassesTab _lastClassesView = ClassesTab.Talents;
        private Button _classesTab;
        private RectTransform _pillRow;

        /// <summary>The views under the Classes tab, in pill order; Skills and Achievements are tabs of their own.</summary>
        private static bool IsClassesView(ClassesTab tab) => tab != ClassesTab.Skills && tab != ClassesTab.Achievements;

        // Opened over the inventory / crafting menu (its Classes tab, the Skills button, or the key): the inventory is hidden
        // while this window is up, not drawn behind it, and comes back on close with the chest it had open (Fire, 2026-09-29).
        private bool _hidInventory;
        private Container _inventoryContainer;

        public bool IsOpen => _root != null && _root.activeSelf;

        public static void BindConfig(ConfigFile config)
        {
            if (OpenKey != null) return;
            OpenKey = new KeyBinding(config, Section, "OpenClasses", KeyCode.K, BindingContext.Gameplay);
            PanelPosition = config.Bind(Section, "ClassesWindowPosition", Vector2.zero,
                "Where the Classes window sits. Drag it by its header to set this.");
            PanelSize = config.Bind(Section, "ClassesWindowSize", DefaultPanelSize,
                "How big the Classes window is. Drag the bottom-right corner to set this.");
            ScrollSpeed = config.Bind(Section, "ScrollSpeed", 1f,
                new ConfigDescription("How fast the Classes window's lists scroll, as a multiple of vanilla's Skills list (1 = the same feel).",
                    new AcceptableValueRange<float>(0.25f, 8f)));

            // Fire (2026-09-29): larger by default, ~85% of the screen height (the canvas scales from 1920x1080). A window
            // saved smaller comes back to the new default once, centred; resizing and dragging still work after.
            ConfigEntry<bool> resized = config.Bind(Section, "ClassesWindowEnlarged", false,
                "Set by the mod once the Classes window was brought up to the larger default (a one-time reset). Leave it alone.");
            if (!resized.Value)
            {
                if (PanelSize.Value.y < DefaultPanelSize.y)
                {
                    PanelSize.Value = DefaultPanelSize;
                    PanelPosition.Value = Vector2.zero;
                }
                resized.Value = true;
            }
        }

        private void Awake()
        {
            Instance = this;
            _views.Add(new SkillsView());
            _views.Add(new CharacterView());
            _views.Add(new TalentsView());
            _views.Add(new UltimatesView());
            _views.Add(new BarsView());
            _views.Add(new PassivesView());
            _views.Add(new ClassPickerView());
            // Last pill, so hiding it for other classes leaves no gap (Fire 10-08: its own tab).
            _views.Add(new SummonsView());
            _views.Add(new AchievementsView());
            BarController.BarsChanged += OnBarsChanged;
        }

        /// <summary>
        /// The second and final attempt at the inventory tab. Every plugin Awake has run by the time Start does,
        /// so VAInventory's assembly is loaded and the reflective lookup can succeed even when BepInEx loaded us
        /// first. This component only exists on a client, so the retry never runs headless.
        /// </summary>
        private void Start() => ClassesInventoryTab.Register(finalAttempt: true);

        private void OnDestroy()
        {
            BarController.BarsChanged -= OnBarsChanged;
            if (Instance == this) Instance = null;
        }

        private void OnBarsChanged(State.BarSide side) => RefreshCurrent();

        private void Update()
        {
            if (OpenKey == null) return;
            if (Player.m_localPlayer == null) { Close(); return; }
            // Fire 10-09: typing anywhere (chat, F5 console, a search bar) never opens or closes the window ("k" in chat did).
            if (FiresCore.Input.FiresInputBlock.IsTyping()) return;
            if (OpenKey.IsPressed()) Toggle();
            if (IsOpen && FiresKeys.GetKeyDown(KeyCode.Escape) && !CloseCurrentOverlay()) Close();
        }

        private bool CloseCurrentOverlay()
        {
            foreach (IClassesView view in _views)
                if (view.Tab == _current && view is IClassesOverlay overlay) return overlay.CloseOverlay();
            return false;
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        /// <summary>
        /// Opens on a given tab. The Skills button routes here: calling Show alone leaves the root
        /// inactive after a Close, which is what made the window impossible to reopen.
        /// </summary>
        public void Open(ClassesTab tab)
        {
            _current = tab;
            Open();
        }

        public void Open()
        {
            EnsureBuilt();
            if (!_hidInventory && InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                _inventoryContainer = InventoryGui.instance.m_currentContainer;
                InventoryGui.instance.Hide();
                _hidInventory = true;
            }
            _root.SetActive(true);
            ApplyScrollSpeed();
            RaiseAboveSiblings();
            InputBlock.Block(true);
            Show(_current);
        }

        public static ConfigEntry<float> ScrollSpeed { get; private set; }

        // Core's scroll areas start at 120, which crawled in game (Fire, 2026-09-29: "dead slow"): Valheim's input module hands
        // the lists a small wheel delta and its own lists make up for it with a higher sensitivity. Match vanilla's Skills list,
        // times the ScrollSpeed setting, on every list in the window (the boards build theirs once; run on each open).
        private const float FallbackScrollSensitivity = 120f;
        private static bool _loggedScroll;

        private void ApplyScrollSpeed()
        {
            SkillsDialog skills = InventoryGui.instance != null ? InventoryGui.instance.m_skillsDialog : null;
            float vanilla = skills != null && skills.skillListScrollRect != null ? skills.skillListScrollRect.scrollSensitivity : 0f;
            float target = Mathf.Max(vanilla, FallbackScrollSensitivity) * (ScrollSpeed?.Value ?? 1f);
            foreach (ScrollRect scroll in _root.GetComponentsInChildren<ScrollRect>(true))
                scroll.scrollSensitivity = target;
            if (_loggedScroll) return;
            _loggedScroll = true;
            FiresRPGClassesPlugin.Log.LogInfo($"Classes window lists scroll at {target:0.#} (vanilla Skills list {vanilla:0.#}, x{ScrollSpeed?.Value ?? 1f:0.##}).");
        }

        public void Close()
        {
            if (_root == null) return;
            _root.SetActive(false);
            InputBlock.Block(false);
            ReturnToInventory();
        }

        private void ReturnToInventory()
        {
            if (!_hidInventory) return;
            _hidInventory = false;
            Container container = _inventoryContainer != null ? _inventoryContainer : null;
            _inventoryContainer = null;
            Player player = Player.m_localPlayer;
            if (InventoryGui.instance == null || player == null || player.IsDead()) return;
            InventoryGui.instance.Show(container);
        }

        public void Show(ClassesTab tab)
        {
            EnsureBuilt();
            // The Summons pill only shows for a Druid or Necromancer (or while a summon is still out).
            bool summons = SummonsView.Available();
            if (_tabButtons.TryGetValue(ClassesTab.Summons, out Button summonsPill)) summonsPill.gameObject.SetActive(summons);
            if (tab == ClassesTab.Summons && !summons) tab = ClassesTab.Talents;
            _current = tab;
            bool classes = IsClassesView(tab);
            if (classes) _lastClassesView = tab;

            foreach (KeyValuePair<ClassesTab, Button> button in _tabButtons)
            {
                if (IsClassesView(button.Key)) ClassesStyle.PaintPill(button.Value, button.Key == tab);
                else ClassesStyle.PaintTab(button.Value, button.Key == tab);
            }
            ClassesStyle.PaintTab(_classesTab, classes);
            if (_pillRow != null) _pillRow.gameObject.SetActive(classes);

            foreach (KeyValuePair<ClassesTab, RectTransform> page in _pages)
                page.Value.gameObject.SetActive(page.Key == tab);
            foreach (IClassesView view in _views)
                if (view.Tab == tab) view.Refresh();
        }

        public void RefreshCurrent()
        {
            if (IsOpen) Show(_current);
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;

            Canvas canvas = FindOrCreateCanvas();
            _root = UIBuilderHelper.CreatePanel(canvas.transform, "ClassesPanel",
                new Vector2(0.5f - PanelWidthFraction / 2f, 0.5f - PanelHeightFraction / 2f),
                new Vector2(0.5f + PanelWidthFraction / 2f, 0.5f + PanelHeightFraction / 2f));

            RectTransform root = _root.GetComponent<RectTransform>();
            ClassesStyle.PanelFrame(root);

            BuildTabStrip(root);
            BuildPages(root);

            Button close = ClassesStyle.Button(root, "Close", "X", ClassesStyle.ButtonKind.Outline,
                new Vector2(1f, 1f), new Vector2(1f, 1f), Close, 20f);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(ClassesStyle.Target, ClassesStyle.Target);
            closeRect.anchoredPosition = new Vector2(-12f, -8f);
            BuildHandles(root);
        }

        /// <summary>
        /// A drag strip across the header and a grip in the bottom-right corner. Both are near-invisible
        /// raycast targets - a RectTransform with no Graphic cannot receive pointer events at all.
        /// </summary>
        private void BuildHandles(RectTransform root)
        {
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = PanelSize.Value;
            root.anchoredPosition = PanelPosition.Value;

            Image header = UIBuilderHelper.CreateImage(root, "MoveStrip",
                new Vector2(0f, 1f), new Vector2(0.88f, 1f), new Color(0f, 0f, 0f, 0.001f));
            header.rectTransform.pivot = new Vector2(0.5f, 1f);
            header.rectTransform.sizeDelta = new Vector2(0f, ClassesStyle.HeaderHeight);
            header.raycastTarget = true;
            header.gameObject.AddComponent<ClassesPanelHandle>()
                .Initialize(root, PanelPosition, PanelSize, false);
            // The tabs share the header now: the drag strip goes BEHIND them, so a tab takes its own click and the
            // gaps between the tabs still drag the window.
            header.transform.SetSiblingIndex(0);

            // Fire 10-09: the corner drew a plain square (a tinted Image with no sprite). It wears a drawn grip now: three
            // diagonal ridges in the corner, the usual resize mark.
            Image grip = UIBuilderHelper.CreateImage(root, "ResizeGrip",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Color(0.9f, 0.75f, 0.45f, 0.85f));
            grip.sprite = GripSprite();
            grip.rectTransform.pivot = new Vector2(1f, 0f);
            grip.rectTransform.anchoredPosition = new Vector2(-3f, 3f);
            grip.rectTransform.sizeDelta = new Vector2(18f, 18f);
            grip.raycastTarget = true;
            grip.gameObject.AddComponent<ClassesPanelHandle>()
                .Initialize(root, PanelPosition, PanelSize, true);
        }

        private static Sprite _gripSprite;

        /// <summary>
        /// The resize mark, drawn once: white ridges on a clear 36 px square (tinted by the Image), three diagonal lines
        /// stepping into the bottom-right corner. Drawn rather than a font glyph, which the game font lacks (tofu).
        /// </summary>
        private static Sprite GripSprite()
        {
            if (_gripSprite != null) return _gripSprite;
            const int size = 36;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            // Ridge k is the line x - y = size - offset (bottom-right corner is x = size-1, y = 0), 3 px thick.
            int[] offsets = { 8, 18, 28 };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    byte a = 0;
                    foreach (int o in offsets)
                    {
                        float d = Mathf.Abs((x - y) - (size - o));
                        if (d <= 2.5f && x - y >= size - o - 3) a = (byte)Mathf.Max(a, (int)(255f * Mathf.Clamp01(2.5f - d + 0.5f)));
                    }
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _gripSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(1f, 0f), 100f);
            _gripSprite.name = "FiresClassesResizeGrip";
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesPanel] resize grip sprite: '{_gripSprite.name}' (drawn {size}x{size}, 3 ridges)");
            return _gripSprite;
        }

        private const float TabWidth = 170f;
        private const float PillWidth = 132f;

        /// <summary>The header's three tabs, and under it the pill row for the Classes views (hidden on the other two).</summary>
        private void BuildTabStrip(RectTransform root)
        {
            RectTransform strip = ClassesStyle.Rect(root, "Tabs", new Vector2(0f, 1f), new Vector2(1f, 1f));
            strip.pivot = new Vector2(0.5f, 1f);
            strip.sizeDelta = new Vector2(0f, ClassesStyle.HeaderHeight);
            RectTransform rule = ClassesStyle.Rect(strip, "Rule", new Vector2(0f, 0f), new Vector2(1f, 0f));
            rule.sizeDelta = new Vector2(0f, 1f);
            ClassesStyle.Fill(rule, null, ClassesStyle.Line);

            _tabButtons[ClassesTab.Skills] = TopTab(strip, 0, "Skills", () => Show(ClassesTab.Skills));
            _tabButtons[ClassesTab.Achievements] = TopTab(strip, 1, "Achievements", () => Show(ClassesTab.Achievements));
            _classesTab = TopTab(strip, 2, "Classes", () => Show(_lastClassesView));

            _pillRow = ClassesStyle.Rect(root, "Pills", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _pillRow.pivot = new Vector2(0.5f, 1f);
            _pillRow.sizeDelta = new Vector2(0f, ClassesStyle.PillRowHeight);
            _pillRow.anchoredPosition = new Vector2(0f, -ClassesStyle.HeaderHeight);

            int index = 0;
            foreach (IClassesView view in _views)
            {
                if (!IsClassesView(view.Tab)) continue;
                ClassesTab tab = view.Tab;
                Button pill = ClassesStyle.Pill(_pillRow, "Pill_" + tab, view.Title,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), () => Show(tab));
                RectTransform rect = pill.GetComponent<RectTransform>();
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(PillWidth, ClassesStyle.Target);
                rect.anchoredPosition = new Vector2(16f + index++ * (PillWidth + 8f), -4f);
                _tabButtons[tab] = pill;
            }
        }

        private static Button TopTab(RectTransform strip, int index, string caption, UnityEngine.Events.UnityAction onClick)
        {
            Button tab = ClassesStyle.Tab(strip, "Tab_" + caption, caption, new Vector2(0f, 0f), new Vector2(0f, 1f), onClick);
            RectTransform rect = tab.GetComponent<RectTransform>();
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(TabWidth, 0f);
            rect.anchoredPosition = new Vector2(14f + index * (TabWidth + 4f), 0f);
            return tab;
        }

        private void BuildPages(RectTransform root)
        {
            foreach (IClassesView view in _views)
            {
                float top = ClassesStyle.HeaderHeight + (IsClassesView(view.Tab) ? ClassesStyle.PillRowHeight : 8f);
                RectTransform rect = ClassesStyle.Rect(root, "Page_" + view.Tab, Vector2.zero, Vector2.one);
                rect.offsetMax = new Vector2(-8f, -top);
                rect.offsetMin = new Vector2(8f, 12f);
                _pages[view.Tab] = rect;
                view.Build(rect);
                rect.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Valheim's own canvas when it can be reached, a standalone overlay canvas otherwise. Both work:
        /// InventoryGui's canvas carries overrideSorting with order 600, so it IS ordered against an
        /// overlay canvas and a higher sortingOrder wins. Inside one canvas the draw order is hierarchy
        /// order instead, which is what RaiseAboveSiblings handles.
        /// </summary>
        private static Canvas FindOrCreateCanvas()
        {
            Canvas game = Hud.instance != null ? Hud.instance.GetComponentInParent<Canvas>() : null;
            if (game != null) return game;

            GameObject existing = GameObject.Find(CanvasName);
            if (existing != null)
            {
                Canvas found = existing.GetComponent<Canvas>();
                // Re-apply: a canvas built by an older build carried the old order and kept it.
                if (found != null) found.sortingOrder = SortOrder;
                return found;
            }

            GameObject host = new GameObject(CanvasName);
            Canvas canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;
            CanvasScaler scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            host.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// Raises the window above everything else in whatever canvas it landed in. A nested Canvas with
        /// overrideSorting re-sorts its whole subtree, and its own GraphicRaycaster is what lets the
        /// header and resize grip receive the pointer instead of the inventory underneath.
        /// </summary>
        private void RaiseAboveSiblings()
        {
            _root.transform.SetAsLastSibling();

            Canvas lift = _root.GetComponent<Canvas>() ?? _root.AddComponent<Canvas>();
            lift.overrideSorting = true;
            lift.sortingOrder = SortOrder;
            if (_root.GetComponent<GraphicRaycaster>() == null) _root.AddComponent<GraphicRaycaster>();
        }
    }

    internal static class RectTransformFill
    {
        /// <summary>Pins a top-anchored strip to a fixed height under the panel's top edge.</summary>
        public static void Fill(this RectTransform rect, float topOffset, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, topOffset);
            rect.sizeDelta = new Vector2(0f, Mathf.Abs(height));
        }
    }
}
