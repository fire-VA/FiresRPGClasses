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
        // Fire 10-08: the key hints sit in a strip UNDER the active (front) row, clear of every slot.
        private const float HintSpace = 18f;
        private float _nextHintRefresh;

        private static ConfigEntry<Vector2> _position;
        private static ConfigEntry<Vector2> _legacyPosition;
        private static ConfigEntry<bool> _migrated;

        /// <summary>The shipped place: Fire's own (10-08), from the bottom-right corner.</summary>
        private static readonly Vector2 DefaultOffset = new Vector2(-286.5f, 164.5f);

        /// <summary>The old centre-anchored default; an old BarsPosition equal to it was never dragged.</summary>
        private static readonly Vector2 LegacyDefault = new Vector2(0f, 190f);
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
            // Fire (10-08, hand test 4): ship the place he dragged them to as the default, "on the right side just above the
            // tooltips for the hammer". Anchored to the screen's bottom-right corner, so it lands there at any resolution and
            // aspect. The default is his own drag (BarsPosition 1003.5 / 164.5 from the centre on a 2580-unit-wide HUD canvas,
            // i.e. 3440x1440 at GUI scale 1) converted to the corner.
            _position = config.Bind(Section, "BarsOffset", DefaultOffset,
                "Where the class bars sit: the centre of their bottom edge, measured from the screen's bottom-right corner in HUD "
                + "units. Drag them in game to set this.");
            _legacyPosition = config.Bind(Section, "BarsPosition", LegacyDefault,
                "Old setting (measured from the bottom centre). Read once to carry a dragged position over to BarsOffset. Leave it alone.");
            _migrated = config.Bind(Section, "BarsOffsetMigrated", false,
                "Set by the mod once an old BarsPosition was carried over to BarsOffset. Leave it alone.");
            _backBarAlpha = config.Bind(Section, "BackBarAlpha", 0.55f,
                new ConfigDescription("How solid the inactive back bar is. Its cooldowns run either way.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
        }

        /// <summary>
        /// Once: a bar someone had dragged under the old centre anchor keeps its place on screen. The same point measured from
        /// the right edge is the old x minus half the HUD's width (read live, so it matches this player's resolution).
        /// An untouched old setting just takes the new default.
        /// </summary>
        private static void CarryOverOldPosition(RectTransform hudRoot)
        {
            if (_migrated == null || _migrated.Value) return;
            Vector2 old = _legacyPosition.Value;
            float width = hudRoot != null ? hudRoot.rect.width : 0f;
            if (old != LegacyDefault)
            {
                if (width <= 0f) return;    // not laid out yet: try again on the next build

                _position.Value = new Vector2(old.x - width * 0.5f, old.y);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] class bars: old position {old} on a {width:0}-unit HUD carried over to BarsOffset {_position.Value}.");
            }
            _migrated.Value = true;
        }

        private void Update()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || Player.m_localPlayer == null || OverlayUp()) { SetVisible(false); return; }
            if (!EnsureBuilt()) return;

            SetVisible(true);
            Paint(data.ActiveBar, _front);
            Paint(ClassPlayerData.Other(data.ActiveBar), _back);
            _backGroup.alpha = _backBarAlpha.Value;

            // The hints follow the configured keys live (twice a second; ConfigurationManager edits land without a rebuild).
            if (Time.time >= _nextHintRefresh)
            {
                _nextHintRefresh = Time.time + 0.5f;
                for (int i = 0; i < _front.Length; i++)
                {
                    if (_front[i]?.Binding == null) continue;
                    int slot = i + 1;
                    string text = ClassKeys.Slot(slot) == null ? slot.ToString() : ClassKeys.SlotLabel(slot);
                    if (_front[i].Binding.text != text) _front[i].Binding.text = text;
                }
            }

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

        /// <summary>
        /// The bars have their own sorting canvas above the HUD, so they drew over the loading and quit/save screens (Fire,
        /// 2026-09-28). Hidden while the HUD is hidden by the player, a menu is open, the loading screen shows, or the game
        /// is shutting down.
        /// </summary>
        private static bool OverlayUp()
        {
            if (Hud.instance == null || Hud.IsUserHidden() || !Hud.instance.IsVisible()) return true;
            // The black screen (death, teleport, sleep, loading, quitting) fades its alpha while its object stays active.
            CanvasGroup black = Hud.instance.m_loadingScreen;
            if (black != null && black.gameObject.activeInHierarchy && black.alpha > 0.01f) return true;
            if (Menu.IsVisible()) return true;
            return Game.instance != null && Game.instance.IsShuttingDown();
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
                // Fire (2026-10-01): "make the front bar/back bar weapons display in the front and back bars on the screen".
                // Slot 1 is the bar's weapon (BarLayout.WeaponSlot), not a skill node, so ClassIcons had no art for it and
                // the slot stayed empty; it shows the weapon in that bar's VAInventory slot (or the recorded prefab
                // without bar slots), the same item the Classes window and the inventory pane show.
                Sprite art = slot == BarLayout.WeaponSlot ? WeaponArt(side, bar) : ClassIcons.For(nodeId);

                // Vanilla switches the icon OFF for an unused slot. Leaving it on with a null sprite is
                // what filled every slot with a white quad.
                view.Icon.gameObject.SetActive(art != null);
                if (art != null)
                {
                    view.Icon.sprite = art;
                    // R31 shots (the lead): the weapon icon was very faint. Availability() reads a skill node, and slot 1 has none,
                    // so it answered Empty and TintFor's default (alpha 0.45) applied, x0.55 more on the inactive row. The weapon
                    // slot is full white; dimmed only during a temporary swap (the active bar's hands hold something else).
                    view.Icon.color = slot == BarLayout.WeaponSlot
                        ? (side == data.ActiveBar && !BarController.HoldingBarWeapon(side) ? TintFor(SlotAvailability.WrongWeapon) : Color.white)
                        : TintFor(BarController.Availability(side, slot));
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

        // The bar's weapon icon: its VAInventory bar slot when those exist, else the weapon the bar recorded (as BarsView).
        private static Sprite WeaponArt(BarSide side, BarLayout bar)
        {
            ItemDrop.ItemData weapon = BarController.HasBarSlots ? BarController.BarWeapon(side) : BarController.FindInInventory(bar.WeaponPrefab);
            return weapon?.GetIcon();
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
            _container.anchorMin = new Vector2(1f, 0f);
            _container.anchorMax = new Vector2(1f, 0f);
            _container.pivot = new Vector2(0.5f, 0f);
            CarryOverOldPosition(hudRoot as RectTransform);
            _container.anchoredPosition = _position.Value;
            _container.sizeDelta = new Vector2(BarLayout.SlotCount * space, SlotHeight * 2f + RowGap + HintSpace);

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
                SlotHeight * BackBarScale + RowGap + HintSpace, BackBarScale, _back, false, reference);
            _backGroup = backRow.gameObject.AddComponent<CanvasGroup>();
            _backGroup.blocksRaycasts = false;
            _backGroup.interactable = false;

            BuildRow(_container, "FrontBar", hotkeyBar.m_elementPrefab, space, HintSpace, 1f, _front, true, reference);

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
                // Place each slot by its CENTRE: vanilla's element pivot is not centred, so placing the pivot on the
                // grid shifted the whole row sideways (Fire, 2026-09-28 quit-screen shot).
                RectTransform element = go.transform as RectTransform;
                float pivotShift = element != null ? (0.5f - element.pivot.x) * element.rect.width : 0f;
                go.transform.localPosition = new Vector3(origin + i * space - pivotShift, 0f, 0f);
                slots[i] = Adopt(go, i + 1, reference);
                // The key hint: under the active row's slot, never on top of one; the back row shows none
                // (the slot keys always act on the active bar).
                RectTransform hint = slots[i].Binding.rectTransform;
                if (frontRow)
                {
                    hint.anchorMin = new Vector2(0f, 0f);
                    hint.anchorMax = new Vector2(1f, 0f);
                    hint.pivot = new Vector2(0.5f, 1f);
                    hint.anchoredPosition = new Vector2(0f, -2f);
                    hint.sizeDelta = new Vector2(0f, HintSpace - 2f);
                    slots[i].Binding.verticalAlignment = VerticalAlignmentOptions.Top;
                }
                else slots[i].Binding.gameObject.SetActive(false);

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
            // The label spans the slot's width and centres on it: in vanilla's one-digit box "Alt+1" hung off to the
            // side and the label row read as misaligned with the slots.
            RectTransform label = view.Binding.rectTransform;
            label.anchorMin = new Vector2(0f, label.anchorMin.y);
            label.anchorMax = new Vector2(1f, label.anchorMax.y);
            label.offsetMin = new Vector2(0f, label.offsetMin.y);
            label.offsetMax = new Vector2(0f, label.offsetMax.y);
            view.Binding.horizontalAlignment = HorizontalAlignmentOptions.Center;
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
