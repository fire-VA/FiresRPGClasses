using BepInEx.Configuration;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.Input;
using FiresRPGClasses.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 14's two bars. Built from Valheim's own hotbar element prefab and parented under the vanilla
    /// HUD root, so they inherit the HUD's CanvasScaler and come out at EXACTLY the game's grid size at
    /// any resolution - a private canvas with its own reference resolution is what made them the wrong
    /// size before. The back bar is the same bar smaller, sitting behind and above the front one.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class ClassesHud : MonoBehaviour
    {
        private const string Section = "HUD";
        private const string ContainerName = "FiresClassBars";
        private const string VanillaBarName = "HotKeyBar";

        /// <summary>Over the vanilla HUD canvas (400, from the rip) and under InventoryGui (600).</summary>
        private const int BarSortOrder = 450;

        // Floor for the slot's key label. "Ctrl+Shift+1" is the widest it can get, and below this it stops being
        // readable at all, which is worse than overflowing the slot a little.
        private const float BindingFontSizeMin = 7f;

        private const float FallbackElementSpace = 70f;
        private const float BackBarScale = 0.72f;
        private const float RowGap = 10f;
        private const float SlotHeight = 64f;

        private static ConfigEntry<Vector2> _position;
        private static ConfigEntry<float> _backBarAlpha;

        private readonly Slot[] _front = new Slot[BarLayout.SlotCount];
        private readonly Slot[] _back = new Slot[BarLayout.SlotCount];

        private RectTransform _container;
        private CanvasGroup _backGroup;
        private bool _built;

        private class Slot
        {
            public Image Icon;
            public GuiBar Cooldown;
            public TMP_Text Amount;
            public TMP_Text Binding;
        }

        public static void BindConfig(ConfigFile config)
        {
            if (_position != null) return;
            _position = config.Bind(Section, "BarsPosition", new Vector2(0f, 190f),
                "Where the class bars sit relative to the HUD root. Drag them in game to set this.");
            _backBarAlpha = config.Bind(Section, "BackBarAlpha", 0.55f,
                new ConfigDescription("How solid the inactive back bar is. Its cooldowns run either way.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
        }

        private void Update()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || Player.m_localPlayer == null) { SetVisible(false); return; }
            if (!EnsureBuilt()) return;

            SetVisible(true);
            Paint(data.ActiveBar, _front);
            Paint(ClassPlayerData.Other(data.ActiveBar), _back);
            _backGroup.alpha = _backBarAlpha.Value;

            // The inactive row ignores the pointer so the bars can be dragged across it - but while an item
            // is on the cursor it has to be droppable, or the back bar's weapon could never be set.
            _backGroup.blocksRaycasts = ClassesBarWeaponDrop.DraggedItem() != null;
        }

        /// <summary>
        /// Returning to the menu destroys the HUD root and our container under it, but this component
        /// survives, so _built on its own becomes a lie at that moment - SetVisible then dereferenced a
        /// destroyed RectTransform once per frame and threw an NRE from Component.get_gameObject. The
        /// container is the only honest answer, and clearing the flag here is what rebuilds the bars on
        /// the next world load.
        /// </summary>
        private bool ContainerAlive()
        {
            if (!_built) return false;
            if (_container != null) return true;
            _built = false;
            return false;
        }

        private void SetVisible(bool visible)
        {
            if (!ContainerAlive()) return;
            if (_container.gameObject.activeSelf != visible) _container.gameObject.SetActive(visible);
        }

        // -- painting ---------------------------------------------------------------------------

        private void Paint(BarSide side, Slot[] slots)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            BarLayout bar = data.Bar(side);

            for (int i = 0; i < BarLayout.SlotCount; i++)
            {
                int slot = i + 1;
                Slot view = slots[i];
                if (view == null) continue;

                string nodeId = NodeIdFor(bar, slot);
                float total = TotalCooldown(data, bar, slot);
                float remaining = CooldownTracker.Fraction(nodeId, total);
                bool cooling = remaining > 0f;
                Sprite art = ClassIcons.For(nodeId);

                // Vanilla switches the icon OFF for an unused slot. Leaving it on with a null sprite is
                // what filled every slot with a white quad.
                view.Icon.gameObject.SetActive(art != null);
                if (art != null)
                {
                    view.Icon.sprite = art;
                    view.Icon.color = TintFor(BarController.Availability(side, slot));
                }

                view.Cooldown.gameObject.SetActive(cooling);
                if (cooling)
                {
                    view.Cooldown.SetValue(remaining);
                    view.Cooldown.SetColor(ClassesTheme.Cooling);
                }

                view.Amount.gameObject.SetActive(cooling);
                if (cooling) view.Amount.text = Mathf.CeilToInt(CooldownTracker.Remaining(nodeId)) + "s";
            }
        }

        private static string NodeIdFor(BarLayout bar, int slot) =>
            slot == BarLayout.UltimateSlot ? bar.UltimateId : bar.SkillAt(slot);

        /// <summary>White art tinted only by readiness - per-node colours fought the vanilla frame.</summary>
        private static Color TintFor(SlotAvailability availability)
        {
            switch (availability)
            {
                case SlotAvailability.Ready: return Color.white;
                case SlotAvailability.OnCooldown: return new Color(0.70f, 0.75f, 1f, 1f);
                default: return new Color(1f, 1f, 1f, 0.45f);
            }
        }

        private static float TotalCooldown(ClassPlayerData data, BarLayout bar, int slot)
        {
            if (slot == BarLayout.UltimateSlot)
            {
                UltimateDefinition definition = UltimateCatalog.Get(bar.UltimateId);
                return definition == null
                    ? 0f
                    : UltimateController.EffectiveCooldown(definition, data.UltimateLevel(definition.Id));
            }
            return ClassTrees.Node(bar.SkillAt(slot))?.Cooldown ?? 0f;
        }

        // -- building ---------------------------------------------------------------------------

        /// <summary>
        /// False until the HUD and its hotbar exist. The bars are made of the same element prefab the
        /// hotbar uses, under the same HUD root, so vanilla's scaler governs their size.
        /// </summary>
        private bool EnsureBuilt()
        {
            if (ContainerAlive()) return true;
            if (Hud.instance == null || Hud.instance.m_rootObject == null) return false;

            Transform hudRoot = Hud.instance.m_rootObject.transform;
            Transform vanillaBar = hudRoot.Find(VanillaBarName);
            HotkeyBar hotkeyBar = vanillaBar != null
                ? vanillaBar.GetComponent<HotkeyBar>()
                : FindAnyObjectByType<HotkeyBar>();
            if (hotkeyBar == null || hotkeyBar.m_elementPrefab == null) return false;

            float space = hotkeyBar.m_elementSpace > 0f ? hotkeyBar.m_elementSpace : FallbackElementSpace;

            _container = NewRect(hudRoot, ContainerName);
            _container.anchorMin = new Vector2(0.5f, 0f);
            _container.anchorMax = new Vector2(0.5f, 0f);
            _container.pivot = new Vector2(0.5f, 0f);
            _container.anchoredPosition = _position.Value;
            _container.sizeDelta = new Vector2(BarLayout.SlotCount * space, SlotHeight * 2f + RowGap);

            // Its own sorting canvas, so pointer events reach the drag handle rather than the HUD behind.
            Canvas canvas = _container.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = BarSortOrder;
            _container.gameObject.AddComponent<GraphicRaycaster>();

            // A near-invisible raycast target: with no Graphic on the root there is nothing to grab.
            Image grab = _container.gameObject.AddComponent<Image>();
            grab.color = new Color(0f, 0f, 0f, 0.001f);
            grab.raycastTarget = true;
            _container.gameObject.AddComponent<ClassesBarDragHandler>().Initialize(_container, _position);

            // The LIVE slot, not the prefab: the prefab is what we already clone and it is coming out
            // wrong, so the only reliable statement of "what a slot looks like right now" is the one
            // vanilla currently has on screen. Null when the player has no bound items, in which case
            // the clone keeps the prefab's own values.
            Image reference = hotkeyBar.transform.childCount > 0
                ? hotkeyBar.transform.GetChild(0).GetComponent<Image>()
                : null;

            RectTransform backRow = BuildRow(_container, "BackBar", hotkeyBar.m_elementPrefab, space,
                SlotHeight * BackBarScale + RowGap, BackBarScale, _back, false, reference);
            _backGroup = backRow.gameObject.AddComponent<CanvasGroup>();
            _backGroup.blocksRaycasts = false;
            _backGroup.interactable = false;

            BuildRow(_container, "FrontBar", hotkeyBar.m_elementPrefab, space, 0f, 1f, _front, true, reference);

            _built = true;

            // Dump once, unprompted. Four diagnoses of the slot colouring have been wrong, and every one
            // was reasoning about values that only exist at runtime. This puts them in the log the first
            // time the bars exist, so the next report carries the answer instead of another guess.
            ClassesUiDump.DumpOnce("bars built");
            return true;
        }

        private RectTransform BuildRow(RectTransform parent, string name, GameObject elementPrefab,
            float space, float bottom, float scale, Slot[] slots, bool frontRow, Image reference)
        {
            RectTransform row = NewRect(parent, name);
            row.anchorMin = new Vector2(0.5f, 0f);
            row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, bottom);
            row.sizeDelta = new Vector2((BarLayout.SlotCount - 1) * space, SlotHeight);
            row.localScale = new Vector3(scale, scale, 1f);

            float origin = -(BarLayout.SlotCount - 1) * space * 0.5f;
            for (int i = 0; i < BarLayout.SlotCount; i++)
            {
                GameObject go = Instantiate(elementPrefab, row);
                go.SetActive(true);
                go.name = name + "_Slot" + (i + 1);
                go.transform.localPosition = new Vector3(origin + i * space, 0f, 0f);
                slots[i] = Adopt(go, i + 1, reference);

                // Slot 1 takes a weapon dragged out of the inventory. Only that slot: the others hold
                // skills, which are placed from the Bars board, not from the item grid.
                if (i + 1 == BarLayout.WeaponSlot)
                    go.AddComponent<ClassesBarWeaponDrop>().Initialize(frontRow);
            }
            return row;
        }

        /// <summary>
        /// Repurposes a vanilla hotbar element: the durability bar becomes the cooldown and the stack
        /// text the seconds left. Only the item-specific children are switched off, and the element's
        /// own background art is left exactly as vanilla built it.
        /// </summary>
        private static Slot Adopt(GameObject go, int slot, Image reference)
        {
            // THE SLOT'S DARK LOOK IS THE BUTTON. Read out of the ripped HotKeyElement.prefab rather than
            // guessed: `bkg` is a WHITE sprite with a white tint, and the Selectable tints it through
            // CrossFadeColor to m_NormalColor {0, 0, 0, 0.502} - black at half alpha. That IS vanilla's
            // semi-transparent slot; there is no dark sprite anywhere in the element.
            //
            // So each earlier attempt removed the darkness instead of a bug:
            //   interactable = false        -> m_DisabledColor {0.784, 0.784, 0.784, 0.502}, pale grey
            //   Destroy + SetColor(white)   -> white, because white erases the tint outright
            //
            // A clickable slot is still unwanted, so the Button goes - and the colour it was responsible
            // for is then applied by hand, which cannot depend on transition timing.
            Button button = go.GetComponent<Button>();
            Color slotTint = button != null ? button.colors.normalColor : new Color(0f, 0f, 0f, 0.502f);
            if (button != null) Destroy(button);

            Transform bkg = go.transform.Find("bkg");
            Image bkgImage = bkg != null ? bkg.GetComponent<Image>() : null;
            if (bkgImage != null && bkgImage.canvasRenderer != null)
                bkgImage.canvasRenderer.SetColor(slotTint);

            // NOT a blanket canvasRenderer reset. Forcing every Image to white removes whatever darkening
            // the prefab carries, so with no live element to copy from it makes the slot MORE washed out,
            // not less. Only the graphic the Button actually drove gets cleared, and only when we have a
            // known-good colour to put back.

            // A slot's look is sprite x tint x type. The vanilla sprite is PALE and vanilla darkens it
            // with the tint, so copying one field and keeping our own for the rest yields a white tile.
            // Take all three from the LIVE element rather than the prefab, which is what is actually
            // on screen.
            if (reference != null)
            {
                Image background = go.GetComponent<Image>();
                if (background != null)
                {
                    background.sprite = reference.sprite;
                    background.type = reference.type;
                    background.color = reference.color;
                    background.material = reference.material;
                    if (background.canvasRenderer != null)
                        background.canvasRenderer.SetColor(reference.canvasRenderer != null
                            ? reference.canvasRenderer.GetColor()
                            : Color.white);
                }
            }

            Hide(go, "equiped");
            Hide(go, "queued");
            Hide(go, "selected");

            var view = new Slot
            {
                Icon = go.transform.Find("icon").GetComponent<Image>(),
                Cooldown = go.transform.Find("durability").GetComponent<GuiBar>(),
                Amount = go.transform.Find("amount").GetComponent<TMP_Text>(),
                Binding = go.transform.Find("binding").GetComponent<TMP_Text>(),
            };

            view.Binding.text = ClassKeys.Slot(slot) == null ? slot.ToString() : ClassKeys.SlotLabel(slot);

            // The element is vanilla's hotbar number, sized for one digit. "Alt+1" wrapped in it and rendered as
            // "A" above "1", which read as though the key were the letter A. Shrink to fit on one line instead:
            // auto-sizing never grows past the size vanilla chose, so an unmodified "1" still looks the same.
            view.Binding.enableWordWrapping = false;
            view.Binding.overflowMode = TextOverflowModes.Overflow;
            view.Binding.fontSizeMax = view.Binding.fontSize;
            view.Binding.fontSizeMin = BindingFontSizeMin;
            view.Binding.enableAutoSizing = true;
            view.Icon.gameObject.SetActive(false);
            view.Cooldown.gameObject.SetActive(false);
            view.Amount.gameObject.SetActive(false);
            return view;
        }

        private static void Hide(GameObject go, string child)
        {
            Transform found = go.transform.Find(child);
            if (found != null) found.gameObject.SetActive(false);
        }

        private static RectTransform NewRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }
    }
}
