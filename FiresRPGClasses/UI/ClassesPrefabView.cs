using System;
using System.Collections.Generic;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The Classes screen driven by the authored prefab instead of built in code, for mounting inside
    /// another panel (the inventory's Crafting area).
    ///
    /// <para><b>Where the boundary sits.</b> The prefab owns LAYOUT and COLOUR - tab strip position and
    /// height, the page rect's insets, every anchor, every tint. Editing those in Unity and rebuilding the
    /// bundle is the whole workflow and nothing here overrides them. What this binds at runtime is only
    /// what cannot ship inside a 7 KB bundle: the TMP font asset and Core's generated rounded sprite.
    /// Tab CAPTIONS come from the views, because the set of tabs is defined by
    /// <see cref="ClassesPanel"/>'s view list rather than by the prefab.</para>
    ///
    /// <para>The boards themselves still build their rows procedurally into the page rect the prefab
    /// supplies. That is the same split the Marketplace panels use - prefab chrome, cloned rows - and it
    /// means converting the inner boards is a later increment, not a prerequisite.</para>
    /// </summary>
    internal sealed class ClassesPrefabView
    {
        private const string TabsNode = "Tabs";
        private const string PagesNode = "Pages";
        private const string TabPrefix = "Tab_";
        private const string PagePrefix = "Page_";
        private const string LabelNode = "Label";

        private readonly List<IClassesView> _views = new List<IClassesView>();
        private readonly Dictionary<ClassesTab, RectTransform> _pages = new Dictionary<ClassesTab, RectTransform>();
        private readonly Dictionary<ClassesTab, Button> _tabs = new Dictionary<ClassesTab, Button>();

        private GameObject _root;
        private RectTransform _parent;
        private ClassesTab _current = ClassesTab.Skills;
        private Vector2 _laidOutFor = new Vector2(float.NaN, float.NaN);

        internal bool IsBuilt => _root != null;

        /// <summary>
        /// Mounts the prefab into <paramref name="parent"/>. False when the bundle is unavailable, which
        /// leaves the tab unregistered rather than showing an empty board.
        /// </summary>
        internal bool TryBuild(RectTransform parent)
        {
            if (parent == null) return false;
            GameObject prefab = ClassesUiBundle.PanelPrefab;
            if (prefab == null) return false;

            _parent = parent;
            _views.Clear();
            // Fresh instances: a view builds into exactly one page, so the standalone window's views
            // cannot be reused here or one of the two mounts ends up empty.
            _views.Add(new SkillsView());
            _views.Add(new CharacterView());
            _views.Add(new TalentsView());
            _views.Add(new UltimatesView());
            _views.Add(new BarsView());
            _views.Add(new PassivesView());
            _views.Add(new AchievementsView());

            // Instantiated DETACHED so the art binding below walks a whole prefab. Binding after mounting
            // is the trap that leaves every label fontless - invisible rather than missing - because the
            // walk no longer reaches children that have been re-parented away.
            _root = UnityEngine.Object.Instantiate(prefab);
            _root.name = "FiresClassesPanel";
            BindArt(_root);

            _root.transform.SetParent(parent, false);
            StripAuthoringCanvas(_root);

            var rect = _root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (!BindNodes(rect)) { Teardown(); return false; }

            foreach (IClassesView view in _views)
            {
                RectTransform page;
                if (!_pages.TryGetValue(view.Tab, out page)) continue;
                try { view.Build(page); }
                catch (Exception ex)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] board '{view.Tab}' failed to build in the inventory tab "
                        + $"({ex.GetType().Name}: {ex.Message}). The other boards are unaffected.");
                }
            }

            Relayout();
            Show(_current);
            return true;
        }

        /// <summary>
        /// Called on every open. The host panel is laid out by an InventoryGui.Show postfix, so its size
        /// can change between opens - a prefab edit, a bundle rebuild or a layout toggle all resize it
        /// underneath us. Rather than assume an ordering, re-check the size we last laid out for.
        /// </summary>
        internal void OnVisibility(bool shown)
        {
            if (!shown || _root == null) return;
            if (_parent != null && _parent.rect.size != _laidOutFor) Relayout();
            Show(_current);
        }

        private void Relayout()
        {
            var rect = _root != null ? _root.GetComponent<RectTransform>() : null;
            if (rect == null) return;
            // Rows cloned in after the prefab was authored do not lay themselves out; without this they
            // pile up at the content origin, which reads as a data bug and is not one.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            if (_parent != null) _laidOutFor = _parent.rect.size;
        }

        private void Show(ClassesTab tab)
        {
            _current = tab;
            foreach (KeyValuePair<ClassesTab, RectTransform> page in _pages)
                page.Value.gameObject.SetActive(page.Key == tab);
            foreach (IClassesView view in _views)
            {
                if (view.Tab != tab) continue;
                try { view.Refresh(); }
                catch (Exception ex)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] board '{tab}' failed to refresh "
                        + $"({ex.GetType().Name}: {ex.Message}).");
                }
            }
        }

        /// <summary>Resolves the prefab's named nodes. A missing node means the prefab and this code have
        /// drifted, so it is reported by name rather than failing silently later.</summary>
        private bool BindNodes(RectTransform root)
        {
            _pages.Clear();
            _tabs.Clear();

            Transform tabs = root.Find(TabsNode);
            Transform pages = root.Find(PagesNode);
            if (tabs == null || pages == null)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the UI prefab is missing '{TabsNode}' or '{PagesNode}'. "
                    + "Rebuild the bundle from the authored prefab.");
                return false;
            }

            foreach (IClassesView view in _views)
            {
                string name = view.Tab.ToString();

                Transform page = pages.Find(PagePrefix + name);
                var pageRect = page != null ? page as RectTransform : null;
                if (pageRect == null)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the UI prefab has no '{PagesNode}/{PagePrefix}{name}'.");
                    return false;
                }
                _pages[view.Tab] = pageRect;

                Transform tab = tabs.Find(TabPrefix + name);
                Button button = tab != null ? tab.GetComponent<Button>() : null;
                if (button == null)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the UI prefab has no clickable '{TabsNode}/{TabPrefix}{name}'.");
                    return false;
                }

                ClassesTab captured = view.Tab;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => Show(captured));
                _tabs[view.Tab] = button;

                // Caption from the view: the tab set is defined by the view list, not by the prefab.
                Transform labelNode = tab.Find(LabelNode);
                var label = labelNode != null ? labelNode.GetComponent<TMP_Text>() : null;
                if (label != null) label.text = view.Title;
            }

            return true;
        }

        /// <summary>
        /// Supplies the two things the bundle deliberately does not carry. Fonts are stripped because the
        /// TMP font asset dwarfs everything else, and the rounded corners are generated by Core rather
        /// than shipped, so neither can come out of the prefab.
        /// </summary>
        private static void BindArt(GameObject root)
        {
            TMP_FontAsset font = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaSerif);
            if (font != null)
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                    if (text != null) text.font = font;

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                var image = button != null ? button.targetGraphic as Image : null;
                if (image != null) FiresRoundedSprite.Apply(image);
            }
        }

        /// <summary>
        /// Drops the Canvas, CanvasScaler and GraphicRaycaster the prefab root carries. Those exist so the
        /// prefab opens flat and measurable in Unity; mounted inside the inventory's canvas a nested
        /// CanvasScaler would apply its own scaleFactor on top of the one GuiScaler drives every frame.
        /// Canvas goes LAST - the other two RequireComponent it.
        /// </summary>
        private static void StripAuthoringCanvas(GameObject root)
        {
            var raycaster = root.GetComponent<GraphicRaycaster>();
            if (raycaster != null) UnityEngine.Object.Destroy(raycaster);

            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null) UnityEngine.Object.Destroy(scaler);

            var canvas = root.GetComponent<Canvas>();
            if (canvas != null) UnityEngine.Object.Destroy(canvas);
        }

        private void Teardown()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _pages.Clear();
            _tabs.Clear();
        }
    }
}
