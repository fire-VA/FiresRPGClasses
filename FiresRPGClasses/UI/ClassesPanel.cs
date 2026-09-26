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
        Achievements
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
    /// The Classes window: a tab strip over five boards, on its own canvas so no vanilla panel is forked.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
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

        public static ClassesPanel Instance { get; private set; }

        public static KeyBinding OpenKey { get; private set; }

        public static ConfigEntry<Vector2> PanelPosition { get; private set; }
        public static ConfigEntry<Vector2> PanelSize { get; private set; }


        private readonly List<IClassesView> _views = new List<IClassesView>();
        private readonly Dictionary<ClassesTab, RectTransform> _pages = new Dictionary<ClassesTab, RectTransform>();
        private readonly Dictionary<ClassesTab, Button> _tabButtons = new Dictionary<ClassesTab, Button>();

        private GameObject _root;
        private ClassesTab _current = ClassesTab.Skills;

        public bool IsOpen => _root != null && _root.activeSelf;

        public static void BindConfig(ConfigFile config)
        {
            if (OpenKey != null) return;
            OpenKey = new KeyBinding(config, Section, "OpenClasses", KeyCode.K, BindingContext.Gameplay);
            PanelPosition = config.Bind(Section, "ClassesWindowPosition", Vector2.zero,
                "Where the Classes window sits. Drag it by its header to set this.");
            PanelSize = config.Bind(Section, "ClassesWindowSize", new Vector2(1180f, 760f),
                "How big the Classes window is. Drag the bottom-right corner to set this.");
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
            _views.Add(new AchievementsView());
            BarController.BarsChanged += OnBarsChanged;
        }

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
            if (OpenKey.IsPressed()) Toggle();
            if (IsOpen && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Close();
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
            _root.SetActive(true);
            RaiseAboveSiblings();
            InputBlock.Block(true);
            Show(_current);
        }

        public void Close()
        {
            if (_root == null) return;
            _root.SetActive(false);
            InputBlock.Block(false);
        }

        public void Show(ClassesTab tab)
        {
            EnsureBuilt();
            _current = tab;
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
            ClassesTheme.Text(root, "Title", "Classes", UIFontConfig.Colors.BrightGold,
                new Vector2(0f, 1f), new Vector2(1f, 1f), ClassesTheme.TitleFontSize,
                TMPro.TextAlignmentOptions.Center).rectTransform.Fill(0f, ClassesTheme.HeaderHeight);

            BuildTabStrip(root);
            BuildPages(root);

            ClassesTheme.Button(root, "Close", new Vector2(0.9f, 0.965f), new Vector2(0.99f, 1f), Close);
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
            header.rectTransform.sizeDelta = new Vector2(0f, ClassesTheme.HeaderHeight);
            header.raycastTarget = true;
            header.gameObject.AddComponent<ClassesPanelHandle>()
                .Initialize(root, PanelPosition, PanelSize, false);

            Image grip = UIBuilderHelper.CreateImage(root, "ResizeGrip",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Color(0.9f, 0.75f, 0.45f, 0.35f));
            grip.rectTransform.pivot = new Vector2(1f, 0f);
            grip.rectTransform.sizeDelta = new Vector2(18f, 18f);
            grip.raycastTarget = true;
            grip.gameObject.AddComponent<ClassesPanelHandle>()
                .Initialize(root, PanelPosition, PanelSize, true);
        }

        private void BuildTabStrip(RectTransform root)
        {
            GameObject strip = UIBuilderHelper.CreatePanel(root, "Tabs",
                new Vector2(0f, 1f), new Vector2(1f, 1f), UIFontConfig.Colors.TabBarBackground);
            RectTransform stripRect = strip.GetComponent<RectTransform>();
            stripRect.pivot = new Vector2(0.5f, 1f);
            stripRect.anchoredPosition = new Vector2(0f, -ClassesTheme.HeaderHeight);
            stripRect.sizeDelta = new Vector2(0f, ClassesTheme.TabHeight);

            int count = _views.Count;
            for (int i = 0; i < count; i++)
            {
                IClassesView view = _views[i];
                float min = (float)i / count;
                float max = (float)(i + 1) / count;
                ClassesTab tab = view.Tab;
                _tabButtons[tab] = ClassesTheme.Button(stripRect, view.Title,
                    new Vector2(min, 0f), new Vector2(max, 1f), () => Show(tab));
            }
        }

        private void BuildPages(RectTransform root)
        {
            float top = ClassesTheme.HeaderHeight + ClassesTheme.TabHeight;
            foreach (IClassesView view in _views)
            {
                GameObject page = UIBuilderHelper.CreatePanel(root, "Page_" + view.Tab,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), UIFontConfig.Colors.SectionBackgroundLight);
                RectTransform rect = page.GetComponent<RectTransform>();
                rect.offsetMax = new Vector2(0f, -top);
                rect.offsetMin = new Vector2(0f, ClassesTheme.FooterHeight);
                _pages[view.Tab] = rect;
                view.Build(rect);
                page.SetActive(false);
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
