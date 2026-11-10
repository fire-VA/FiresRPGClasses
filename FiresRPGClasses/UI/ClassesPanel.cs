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
        Summons,
        Paragon   // appended last so the existing values keep their numbers
    }

    /// <summary>
    /// A board with something open over it (a talent card, a list). Esc closes that first and only closes the window when
    /// nothing was open.
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
    /// The Classes window, laid out as the design mockups (layout B): three top tabs - Skills,
    /// Achievements, Classes - and inside Classes a row of pills for its views. It still opens full size over a hidden
    /// inventory (there is no room beside VAInventory), on its own canvas so no vanilla panel is forked.
    /// class_test drives it through <c>_tabButtons</c> / <c>_pages</c> / <c>_current</c>, keyed by <see cref="ClassesTab"/>:
    /// the Skills and Achievements tabs and the Classes pills are all registered there.
    /// Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class ClassesPanel : MonoBehaviour
    {
        private const string CanvasName = "FiresRPGClasses_Canvas";
        private const string Section = "6 - UI";
        /// <summary>
        /// Vanilla's ladder, read out of IngameGui prefabs in the rip - all overrideSorting = 1:
        /// Chat 300, HUD 400, InventoryGui and its crafting page 600, Store 700, TextInput 1100,
        /// Esc menu 1700. 2000 (the same as Core's ShellHub canvas) puts this window above the Esc menu
        /// at 1700; under it, clicks fell through to pause-menu buttons such as Logout.
        /// </summary>
        private const int SortOrder = 2000;
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
        internal ClassesTab CurrentTab => _current;

        // While hosted in the Fires hub the pages live in the shell's content area; their home
        // parent and offsets are kept so they go back to this window unchanged (override OFF = the standalone window, unchanged).
        private readonly Dictionary<RectTransform, (Vector2 min, Vector2 max)> _pageHome = new Dictionary<RectTransform, (Vector2, Vector2)>();
        private bool _pagesAdopted;

        // Stretching the pages to the hub's smaller content area would relay every view (icons over the next row, pills on
        // the bars, tiny talent labels). Instead the pages keep the OLD window's
        // exact page rects inside a scaler the size of the old window minus its header, and the scaler gets ONE uniform scale to
        // fit the hub content. Every view renders exactly as in the old window, only scaled.
        private RectTransform _hubScaler;

        internal void AdoptPages(RectTransform content)
        {
            EnsureBuilt();
            HideLegacyRoot("the hub adopted the pages");
            if (_pagesAdopted) { FitScaler(content); return; }
            RectTransform root = _root.GetComponent<RectTransform>();
            // never smaller than the size the views were designed at (a window dragged smaller than that overflowed there too)
            Vector2 old = Vector2.Max(root.rect.size, DefaultPanelSize);
            if (_hubScaler == null)
            {
                _hubScaler = new GameObject("ClassesHubScaler", typeof(RectTransform)).GetComponent<RectTransform>();
                _hubScaler.anchorMin = _hubScaler.anchorMax = _hubScaler.pivot = new Vector2(0.5f, 0.5f);
            }
            _hubScaler.SetParent(content, false);
            _hubScaler.sizeDelta = new Vector2(old.x, old.y - ClassesStyle.HeaderHeight);
            foreach (RectTransform page in _pages.Values)
            {
                if (!_pageHome.ContainsKey(page)) _pageHome[page] = (page.offsetMin, page.offsetMax);
                (Vector2 min, Vector2 max) home = _pageHome[page];
                page.SetParent(_hubScaler, false);
                page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one;
                // the old rect, with the old header band (now the hub's) taken off the top
                page.offsetMin = home.min; page.offsetMax = new Vector2(home.max.x, home.max.y + ClassesStyle.HeaderHeight);
            }
            _pagesAdopted = true;
            FitScaler(content);
            ApplyScrollSpeed();
        }

        private float _loggedScale = -1f;
        internal void RefitHub(RectTransform content) { if (_pagesAdopted) FitScaler(content); }

        private void FitScaler(RectTransform content)
        {
            if (_hubScaler == null) return;
            Canvas.ForceUpdateCanvases();
            Vector2 have = content.rect.size, need = _hubScaler.sizeDelta;
            float s = need.x > 1f && need.y > 1f ? Mathf.Min(have.x / need.x, have.y / need.y) : 1f;
            _hubScaler.localScale = new Vector3(s, s, 1f);
            _hubScaler.anchoredPosition = Vector2.zero;
            if (Mathf.Abs(s - _loggedScale) > 0.01f) { _loggedScale = s; FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] {_pages.Count} pages in the hub at the old page size {need.x:0}x{need.y:0}, scaled x{s:0.###} to fit {have.x:0}x{have.y:0}"); }
        }

        internal void ReturnPages()
        {
            if (!_pagesAdopted || _root == null) return;
            RectTransform root = _root.GetComponent<RectTransform>();
            foreach (KeyValuePair<RectTransform, (Vector2 min, Vector2 max)> home in _pageHome)
            {
                if (home.Key == null) continue;
                home.Key.SetParent(root, false);
                home.Key.anchorMin = Vector2.zero; home.Key.anchorMax = Vector2.one;
                home.Key.offsetMin = home.Value.min; home.Key.offsetMax = home.Value.max;
                home.Key.gameObject.SetActive(false);
            }
            // the old window's own children order: pages sit after the tab strip and before the close button / handles
            _pagesAdopted = false;
            if (ClassesHub.Hosted) HideLegacyRoot("the hub closed");
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] {_pageHome.Count} pages returned to the Classes window");
        }

        internal void ShowHosted(ClassesTab tab) => Show(tab);
        internal bool CloseOverlayHosted() => CloseCurrentOverlay();
        private ClassesTab _lastClassesView = ClassesTab.Talents;
        private Button _classesTab;
        private Button _appearanceTab;
        private int _appearanceShown = -1;

        private void RefreshAppearanceTab()
        {
            if (_appearanceTab == null) return;
            bool show = AppearanceEditor.Available;
            _appearanceTab.gameObject.SetActive(show);
            if ((show ? 1 : 0) == _appearanceShown) return;
            _appearanceShown = show ? 1 : 0;
            FiresRPGClassesPlugin.Log?.LogDebug(show ? "[Classes] Appearance header tab shown (Fires Character Creator found)." : "[Classes] Appearance header tab hidden (no Fires Character Creator).");
        }
        private RectTransform _pillRow;

        /// <summary>The views under the Classes tab, in pill order; Skills and Achievements are tabs of their own.</summary>
        private static bool IsClassesView(ClassesTab tab) => tab != ClassesTab.Skills && tab != ClassesTab.Achievements;

        // Opened over the inventory / crafting menu (its Classes tab, the Skills button, or the key): the inventory is hidden
        // while this window is up, not drawn behind it, and comes back on close with the chest it had open.
        private bool _hidInventory;
        private Container _inventoryContainer;

        // Open = the standalone window, OR the hub showing our pages.
        // Under the override the legacy root is first built by AdoptPages -> EnsureBuilt, and CreatePanel makes it ACTIVE. The hub
        // covers it, but on hub close the pages go home inactive and the empty root (tab strip + X) would stay on screen. Hosted, the
        // root is never shown: hidden on adopt, on hub close and if ever found up. Override OFF: untouched (Open/Close own it).
        private int _legacyHides;
        private void HideLegacyRoot(string why)
        {
            if (_root == null || !_root.activeSelf) return;
            _root.SetActive(false);
            _legacyHides++;
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] legacy Classes window hidden ({why}); hides this session {_legacyHides}");
        }

        public bool IsOpen => (_root != null && _root.activeSelf)
            || (_pagesAdopted && FiresCore.UI.Shell.FiresShell.IsOpen && _hubScaler != null && _hubScaler.gameObject.activeInHierarchy);

        public static void BindConfig(ConfigFile config)
        {
            if (OpenKey != null) return;
            OpenKey = new KeyBinding(config, Section, "OpenClasses", KeyCode.K, BindingContext.Gameplay);
            PanelPosition = config.Bind(Section, "ClassesWindowPosition", Vector2.zero,
                new ConfigDescription("Where the Classes window sits; drag it by its header to set this.", null,
                    new System.ComponentModel.BrowsableAttribute(false)));
            PanelSize = config.Bind(Section, "ClassesWindowSize", DefaultPanelSize,
                new ConfigDescription("How big the Classes window is; drag its bottom-right corner to set this.", null,
                    new System.ComponentModel.BrowsableAttribute(false)));
            ScrollSpeed = config.Bind(Section, "ScrollSpeed", 1f,
                new ConfigDescription("How fast the Classes window's lists scroll (1 = like vanilla's Skills list).",
                    new AcceptableValueRange<float>(0.25f, 8f), new FiresCore.Sync.ConfigurationManagerAttributes { IsAdvanced = true }));

            // Larger by default, ~85% of the screen height (the canvas scales from 1920x1080). A window
            // saved smaller comes back to the new default once, centred; resizing and dragging still work after.
            ConfigEntry<bool> resized = config.Bind(Section, "ClassesWindowEnlarged", false,
                new ConfigDescription("Set by the mod after a one-time window resize; leave it alone.", null,
                    new System.ComponentModel.BrowsableAttribute(false)));
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
            _views.Add(new ParagonView());   // ESO-style Champion-Point constellations
            _views.Add(new ClassPickerView());
            // Last pill, so hiding it for other classes leaves no gap.
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
            if (ClassesHub.Hosted && _root != null && _root.activeSelf) HideLegacyRoot("found active while hosted");
            if (Player.m_localPlayer == null) { Close(); return; }
            // typing anywhere (chat, F5 console, a search bar) never opens or closes the window.
            if (FiresCore.Input.FiresInputBlock.IsTyping()) return;
            if (OpenKey.IsPressed()) Toggle();
            // hosted: the hub owns Esc (it asks ClassesHub.OnEscape first, so an open card still closes before the hub)
            if (IsOpen && !_pagesAdopted && FiresKeys.GetKeyDown(KeyCode.Escape) && !CloseCurrentOverlay()) Close();
        }

        private bool CloseCurrentOverlay()
        {
            foreach (IClassesView view in _views)
                if (view.Tab == _current && view is IClassesOverlay overlay) return overlay.CloseOverlay();
            return false;
        }

        public void Toggle()
        {
            if (ClassesHub.Hosted) { if (FiresCore.UI.Shell.FiresShell.IsOpen) FiresCore.UI.Shell.FiresShell.Close(); else Open(); return; }
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
            // every entry point (the inventory tab, the Skills button, the key) lands here; hosted = the Fires hub instead.
            if (ClassesHub.Hosted)
            {
                FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] open -> Fires hub '{ClassesHub.ModuleId}' on '{ClassesHub.SubId(_current)}'");
                FiresCore.UI.Shell.FiresShell.Open(ClassesHub.ModuleId, ClassesHub.SubId(_current));
                return;
            }
            ReturnPages();
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
            RefreshAppearanceTab();
            Show(_current);
        }

        public static ConfigEntry<float> ScrollSpeed { get; private set; }

        // Core's scroll areas start at 120, which crawls in game: Valheim's input module hands
        // the lists a small wheel delta and its own lists make up for it with a higher sensitivity. Match vanilla's Skills list,
        // times the ScrollSpeed setting, on every list in the window (the boards build theirs once; run on each open).
        private const float FallbackScrollSensitivity = 120f;
        // Under the hub the pages live in ClassesHubScaler, outside _root, so both trees are covered; and one flat value is
        // ~1 row on Passives' 88 px cards. Every list gets one rule: at least RowsPerNotch rows (short rows: 3 lines) per wheel notch, never below vanilla.
        private const float RowsPerNotchTall = 2f, LinesPerNotch = 3f, TallRow = 60f;
        private static readonly HashSet<string> _loggedScroll = new HashSet<string>();

        private void ApplyScrollSpeed()
        {
            SkillsDialog skills = InventoryGui.instance != null ? InventoryGui.instance.m_skillsDialog : null;
            float vanilla = skills != null && skills.skillListScrollRect != null ? skills.skillListScrollRect.scrollSensitivity : 0f;
            float floor = Mathf.Max(vanilla, FallbackScrollSensitivity);
            float mul = ScrollSpeed?.Value ?? 1f;
            var seen = new HashSet<ScrollRect>();
            foreach (Transform tree in new Transform[] { _root != null ? _root.transform : null, _hubScaler })
            {
                if (tree == null) continue;
                foreach (ScrollRect scroll in tree.GetComponentsInChildren<ScrollRect>(true))
                {
                    if (!seen.Add(scroll)) continue;
                    float row = RowStep(scroll);
                    float rows = row >= TallRow ? RowsPerNotchTall : LinesPerNotch;
                    float target = Mathf.Max(floor, row * rows) * mul;
                    scroll.scrollSensitivity = target;
                    string view = scroll.gameObject.name;
                    if (row > 0f && _loggedScroll.Add(view + "|" + Mathf.RoundToInt(row)))
                        FiresRPGClassesPlugin.Log.LogDebug($"[ClassesHub] scroll: {view} sensitivity {target:0.#} (rows per notch {target / row:0.#}; row {row:0.#} px, vanilla {vanilla:0.#}, x{mul:0.##})");
                }
            }
        }

        /// <summary>The list's row step in content units: the smallest gap between distinct child tops, else a child's height.</summary>
        internal static float RowStep(ScrollRect scroll)
        {
            RectTransform content = scroll != null ? scroll.content : null;
            if (content == null || content.childCount == 0) return 0f;
            var tops = new List<float>(); float height = 0f;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                tops.Add(Mathf.Round(child.offsetMax.y * 10f) / 10f);
                if (height <= 0f) height = child.rect.height;
            }
            tops.Sort();
            float step = float.MaxValue;
            for (int i = 1; i < tops.Count; i++) { float d = tops[i] - tops[i - 1]; if (d > 1f && d < step) step = d; }
            return step < float.MaxValue ? step : height;
        }

        /// <summary>Golden probe (no OS input): scrolls the named list by N wheel notches through ScrollRect.OnScroll and returns
        /// the rows it moved, or -1 if not found. FDT calls it by reflection: ClassesPanel.ScrollProbe("PassiveGrid", 3).</summary>
        public static float ScrollProbe(string view, int notches)
        {
            var panel = Instance; if (panel == null) return -1f;
            foreach (Transform tree in new Transform[] { panel._root != null ? panel._root.transform : null, panel._hubScaler })
            {
                if (tree == null) continue;
                foreach (ScrollRect scroll in tree.GetComponentsInChildren<ScrollRect>(false))
                {
                    if (scroll.gameObject.name != view) continue;
                    float row = RowStep(scroll); if (row <= 0f) return -1f;
                    scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; Canvas.ForceUpdateCanvases();
                    float y0 = scroll.content.anchoredPosition.y;
                    var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { scrollDelta = new Vector2(0f, -1f) };
                    for (int i = 0; i < notches; i++) scroll.OnScroll(data);
                    float rows = (scroll.content.anchoredPosition.y - y0) / row;
                    FiresRPGClassesPlugin.Log.LogDebug($"[ClassesHub] scroll probe: {view} {notches} notches moved {rows:0.##} rows (row {row:0.#} px, sensitivity {scroll.scrollSensitivity:0.#})");
                    scroll.verticalNormalizedPosition = 1f;
                    return rows;
                }
            }
            return -1f;
        }

        /// <summary>
        /// The Appearance tab: this window steps aside (not Close: the inventory it hid stays hidden), FCC's editor opens, and
        /// when the editor closes this window comes back on the view it was on. Clear-every-UI: only one of the two is ever up.
        /// </summary>
        internal void OpenAppearance()
        {
            if (_root == null) return;
            _root.SetActive(false);
            InputBlock.Block(false);
            if (!AppearanceEditor.Open())
            {
                FiresRPGClassesPlugin.Log?.LogWarning("[Classes] Appearance: the Fires Character Creator did not open; back to the Classes window.");
                Open();
                return;
            }
            FiresRPGClassesPlugin.Log?.LogDebug("[Classes] Appearance: Classes window hidden, Fires Character Creator open.");
            StartCoroutine(ReturnAfterAppearance());
        }

        private System.Collections.IEnumerator ReturnAfterAppearance()
        {
            yield return null;
            while (AppearanceEditor.IsOpen()) yield return null;
            if (_root != null && !_root.activeSelf)
            {
                Open();
                FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] Appearance: Fires Character Creator closed, Classes window back on {_current}.");
            }
        }

        /// <summary>FiresCharacterCreator.FccApi (or VCS's VcsApi) OpenEditor / IsEditorOpen, found through Combat.VcsBridge.</summary>
        internal static class AppearanceEditor
        {
            private static System.Reflection.MethodInfo _open;
            private static System.Reflection.PropertyInfo _isOpen;
            private static System.Reflection.MethodInfo _close;
            private static bool _looked;

            internal static bool Available
            {
                get
                {
                    // a miss is retried (VcsBridge.Api throttles it); a hit is kept
                    if (!_looked || _open == null)
                    {
                        System.Type api = Combat.VcsBridge.Api;
                        if (api == null) { _looked = true; return false; }
                        _looked = true;
                        _open = api?.GetMethod("OpenEditor", System.Type.EmptyTypes);
                        _isOpen = api?.GetProperty("IsEditorOpen");
                        _close = api?.GetMethod("CloseEditor", System.Type.EmptyTypes);
                        FiresRPGClassesPlugin.Log?.LogDebug(_open != null ? $"[Classes] Appearance tab: {api.FullName}.OpenEditor found."
                            : "[Classes] Appearance tab hidden: no Fires Character Creator OpenEditor.");
                    }
                    return _open != null;
                }
            }

            internal static bool Open()
            {
                try { return Available && _open.Invoke(null, null) is bool ok && ok; }
                catch (System.Exception ex) { FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] Appearance: OpenEditor threw {ex.GetType().Name}: {ex.Message}"); return false; }
            }

            internal static void Close()
            {
                try { _close?.Invoke(null, null); }
                catch (System.Exception ex) { FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] Appearance: CloseEditor threw {ex.GetType().Name}: {ex.Message}"); }
            }

            internal static bool IsOpen()
            {
                try { return _isOpen != null && _isOpen.GetValue(null) is bool open && open; }
                catch { return false; }
            }
        }

        public void Close()
        {
            // hosted, closing the Classes "window" closes the hub (tests and the key call Close too)
            if (_pagesAdopted && FiresCore.UI.Shell.FiresShell.IsOpen) { FiresCore.UI.Shell.FiresShell.Close(); return; }
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
            ApplyScrollSpeed();   // after Refresh, so lists filled on show are measured
            if (_pagesAdopted) ClassesHub.MirrorSubTab(_current);   // 2a: a view's own "go to" keeps the hub's sub-tab row in step
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

            // a tinted Image with no sprite would draw a plain square, so the corner wears a drawn grip: three diagonal ridges in the corner, the usual resize mark.
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
            // A top-level launcher for the Fires Character Creator.
            // Top level, not a Classes pill: the look isn't a class view, and it opens FCC's own window rather than a page here.
            // Soft link (FccApi by reflection); no FCC, no tab.
            // Built always and shown / hidden on every Open (RefreshAppearanceTab): if the Character Creator loads after this
            // window is built, the tab still appears. It is the last header tab.
            _appearanceTab = TopTab(strip, 3, "Appearance", OpenAppearance);
            RefreshAppearanceTab();

            _pillRow = ClassesStyle.Rect(root, "Pills", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _pillRow.pivot = new Vector2(0.5f, 1f);
            _pillRow.sizeDelta = new Vector2(0f, ClassesStyle.PillRowHeight);
            _pillRow.anchoredPosition = new Vector2(0f, -ClassesStyle.HeaderHeight);

            float pillX = 16f;
            int index = 0;
            foreach (IClassesView view in _views)
            {
                if (!IsClassesView(view.Tab)) continue;
                ClassesTab tab = view.Tab;
                Button pill = ClassesStyle.Pill(_pillRow, "Pill_" + tab, view.Title,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), () => Show(tab));
                RectTransform rect = pill.GetComponent<RectTransform>();
                rect.pivot = new Vector2(0f, 0.5f);
                // every label fits its button: a pill grows to its caption, never below PillWidth
                TMPro.TMP_Text caption = pill.GetComponentInChildren<TMPro.TMP_Text>(true);
                float width = caption != null ? Mathf.Max(PillWidth, caption.GetPreferredValues(caption.text).x + 32f) : PillWidth;
                rect.sizeDelta = new Vector2(width, ClassesStyle.Target);
                rect.anchoredPosition = new Vector2(pillX, -4f);
                pillX += width + 8f;
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
