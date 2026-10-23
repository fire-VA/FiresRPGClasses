using System.Linq;
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
    public partial class ClassesHud : MonoBehaviour
    {
        private const string Section = "5 - HUD";
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
        // The key hints sit in a strip UNDER the active (front) row, clear of every slot.
        private const float HintSpace = 18f;
        private float _nextHintRefresh;

        private static ConfigEntry<Vector2> _position;
        private static ConfigEntry<Vector2> _legacyPosition;
        private static ConfigEntry<bool> _migrated;

        /// <summary>The shipped default place, from the bottom-right corner.</summary>
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
            public int AmountShown = -1;
            public TMP_Text Binding;
            public int HintSignature = int.MinValue;
            public string HintText;
        }

        private static ConfigEntry<bool> _swapCue;
        private static ConfigEntry<bool> _dockUnderGroupHud;   // default-dock the bars under Core's "hud.grouphud" (Core 0.2.528+)
        // BarsView always hides skills the weapon cannot use (no setting)
        private static bool _swapHooked;
        private Image _flash;
        private static float _cueStart = -10f;
        private const float CueTime = 0.35f;

        private static void OnBarSwapped(BarSide from, BarSide to)
        {
            if (_swapCue == null || !_swapCue.Value) return;
            _cueStart = Time.unscaledTime;
            FiresRPGClassesPlugin.Log.LogDebug($"[Bars] swap cue: {from} -> {to} (flash + pulse {CueTime:0.##} s)");
        }

        private void AnimateSwapCue()
        {
            if (_flash == null || _container == null) return;
            float t = (Time.unscaledTime - _cueStart) / CueTime;
            if (t < 0f || t >= 1f)
            {
                if (_flash.color.a != 0f) _flash.color = new Color(1f, 0.82f, 0.4f, 0f);
                // Back to the scale from before the pulse, not to 1: under Core's dock engine the scale is Core's (corner grip)
                if (_pulsing) { _container.localScale = _cueBase; _pulsing = false; }
                return;
            }
            if (!_pulsing) { _cueBase = _container.localScale; _pulsing = true; }
            float k = 1f - t;
            _flash.color = new Color(1f, 0.82f, 0.4f, 0.35f * k * k);
            _container.localScale = _cueBase * (1f + 0.06f * Mathf.Sin(t * Mathf.PI));
        }

        private bool _pulsing;
        private Vector3 _cueBase = Vector3.one;

        // (Core 0.2.522 FiresShell.RegisterHudElement, Tools\CORE_HUD_LAYOUT_PLAN.md) With the Fires UI override
        // ON the bars join Core's HUD dock engine: in layout mode (Esc menu or the hub toggle) they show an outline + "Class bars", can be
        // dragged, scaled with the corner grip and docked to the other HUD elements (the Group HUD area among them); Core saves it per
        // character + resolution. Core then owns the position, so the bars' own drag handle steps aside (BarsOffset stays the first place).
        // By reflection, so this loads on any Core: older than 0.2.522, or the override OFF, keeps today's own drag. Nothing moves
        // until someone drags it in layout mode, unless "Dock bars under the Group HUD" (default OFF) asks for the default dock.
        internal const string DockId = "rpgclasses.bars";
        private RectTransform _dockedFor, _dockTriedFor;
        private static bool s_liveEdit;   // Core took LiveEdit + DragHandle at the last register

        private void TryDock()
        {
            if (_container == null || _dockedFor == _container || _dockTriedFor == _container) return;
            _dockTriedFor = _container;   // one attempt (and one log line) per container; an override flip to ON tries again
            string why;
            try { why = DockWithCore(_container, OnBarsResized, BarsMinSize()); }
            catch (System.Exception e) { why = "failed: " + e.GetBaseException().Message; }
            if (why == null)
            {
                _dockedFor = _container;
                var own = _container.GetComponent<ClassesBarDragHandler>();
                if (own != null) own.enabled = false;   // a disabled handler gets no drag events: Core's layout mode drags it
                // A size the player gave the bars before (Core 0.2.574+ resize) is re-applied and the slots re-flowed into it
                if (ClassesHudSizes.TryGet(DockId, out Vector2 saved)) KitResize(saved, true);
                if (s_liveEdit) FiresRPGClassesPlugin.Log?.LogDebug($"[HUD] {DockId} live edit on (handle {_container.name})");
            }
            FiresRPGClassesPlugin.Log?.LogInfo(why == null
                ? $"[ClassesHud] bars registered with Core's HUD dock engine as '{DockId}' (layout mode: drag / scale / dock; own drag off; default dock {(_dockUnderGroupHud != null && _dockUnderGroupHud.Value ? "under hud.grouphud" : "none")})"
                : $"[ClassesHud] bars not docked: {why} (own drag stays)");
        }

        // Core never releases another mod's dock element: on FiresShell.OverrideChanged(false) the bars leave the
        // dock engine (UnregisterHudElement) and go back to their own place + scale + drag; on (true) they register again. The event
        // only flags the change; Update applies it on the main thread.
        private static int s_overrideFlip = -1;   // -1 none, 0 went OFF, 1 went ON
        private static bool s_overrideHooked;

        private void SyncDock()
        {
            if (!s_overrideHooked)
            {
                s_overrideHooked = true;
                try
                {
                    var ev = System.Type.GetType("FiresCore.UI.Shell.FiresShell, FiresUnifiedCore", false)?.GetEvent("OverrideChanged");
                    if (ev != null) ev.AddEventHandler(null, new System.Action<bool>(on => s_overrideFlip = on ? 1 : 0));
                }
                catch (System.Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[ClassesHud] OverrideChanged subscribe failed ({e.GetType().Name}): the dock follows the override only at build"); }
            }
            int flip = s_overrideFlip;
            if (flip < 0) return;
            s_overrideFlip = -1;
            if (flip == 1) { _dockTriedFor = null; TryDock(); return; }
            if (_dockedFor == null) return;
            try { UndockFromCore(); } catch (System.Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[ClassesHud] UnregisterHudElement failed: {e.GetBaseException().Message}"); }
            _dockedFor = null;
            if (_container != null)
            {
                _container.anchoredPosition = _position.Value;
                _container.localScale = Vector3.one;
                var own = _container.GetComponent<ClassesBarDragHandler>();
                if (own != null) own.enabled = true;
            }
            FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] override OFF: bars left Core's dock engine; own place {_position.Value} + scale 1 + own drag back");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void UndockFromCore()
        {
            System.Type.GetType("FiresCore.UI.Shell.FiresShell, FiresUnifiedCore", false)?.GetMethod("UnregisterHudElement", new[] { typeof(string) })?.Invoke(null, new object[] { DockId });
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string DockWithCore(RectTransform rect, System.Action<Vector2> sizeChanged, Vector2 minSize)
        {
            System.Type shell = System.Type.GetType("FiresCore.UI.Shell.FiresShell, FiresUnifiedCore", false);
            // HudElementOptions lives in FiresCore.UI.Shell although its FILE is UI\Dock; guessing the namespace finds nothing,
            // so the type comes from RegisterHudElement's own 3rd parameter.
            var reg = shell?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RegisterHudElement" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == typeof(RectTransform));
            System.Type optT = reg?.GetParameters()[2].ParameterType;
            if (reg == null) return "Core has no RegisterHudElement (older than 0.2.522)";
            if (!(shell.GetProperty("OverrideOn")?.GetValue(null) is bool on && on)) return "the Fires UI override is off";
            object opts = System.Activator.CreateInstance(optT);
            optT.GetField("Owner")?.SetValue(opts, "FiresRPGClasses");
            optT.GetField("Label")?.SetValue(opts, "Class bars");
            optT.GetField("Resizable")?.SetValue(opts, true);
            // Key (default OFF): start docked under Core's Group HUD ("hud.grouphud", Core 0.2.528; a target that
            // registers later is still joined when it arrives)
            if (_dockUnderGroupHud != null && _dockUnderGroupHud.Value)
            {
                optT.GetField("DefaultDockTo")?.SetValue(opts, "hud.grouphud");
                var edgeF = optT.GetField("DefaultEdge");
                if (edgeF != null) edgeF.SetValue(opts, System.Enum.Parse(edgeF.FieldType, "Bottom"));
            }
            ClassesHudDock.SetSizeOptions(optT, opts, sizeChanged, minSize);   // real resize (Core 0.2.574+), the slots re-flow
            // (Core 0.2.583) Live drag / resize with no editor. Handle = the container itself: its existing alpha-0.001 "grab"
            // Image, the same surface the pre-dock ClassesBarDragHandler dragged from (no visual change). The slots' weapon drops
            // (ClassesBarWeaponDrop, IDropHandler on the slot) still reach the slot, which is the deepest raycast hit.
            s_liveEdit = ClassesHudDock.SetLiveEdit(optT, opts, rect);
            reg.Invoke(null, new object[] { DockId, rect, opts });
            return null;
        }

        public static void BindConfig(ConfigFile config)
        {
            if (_position != null) return;
            _swapCue = config.Bind(Section, "Bar swap cue", false,
                "Flash the bars gold with a short pulse when you swap front and back bars.");
            _dockUnderGroupHud = config.Bind(Section, "Dock bars under the Group HUD", false,
                "With the Fires UI override on, the class bars start docked below the Group HUD unless you moved them yourself.");
            // The default place is on the right side, just above the hammer's tooltips. Anchored to the screen's bottom-right
            // corner, so it lands there at any resolution and aspect. It was measured as a drag (BarsPosition 1003.5 / 164.5 from the centre on a 2580-unit-wide HUD canvas,
            // i.e. 3440x1440 at GUI scale 1) converted to the corner.
            _position = config.Bind(Section, "BarsOffset", DefaultOffset,
                new ConfigDescription("Where the class bars sit on screen; drag them in game to set this.", null,
                    new System.ComponentModel.BrowsableAttribute(false)));
            _legacyPosition = config.Bind(Section, "BarsPosition", LegacyDefault,
                new ConfigDescription("Old bar position, read once to carry it over; leave it alone.", null,
                    new System.ComponentModel.BrowsableAttribute(false)));
            _migrated = config.Bind(Section, "BarsOffsetMigrated", false,
                new ConfigDescription("Set by the mod once the old bar position was carried over; leave it alone.", null,
                    new System.ComponentModel.BrowsableAttribute(false)));
            _backBarAlpha = config.Bind(Section, "BackBarAlpha", 0.55f,
                new ConfigDescription("How solid the inactive back bar looks.",
                    new AcceptableValueRange<float>(0.1f, 1f), new FiresCore.Sync.ConfigurationManagerAttributes { IsAdvanced = true }));
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
            ClassesHudSizes.Tick();   // saves a settled resize (nothing while idle)
            ClassPlayerData data = ClassPlayerStore.Local;
            // Typing in the chat hides the bars only when the chat window's drawn rect actually intersects the bars' drawn
            // rect; with the chat elsewhere (e.g. chat bottom-left, bars bottom-right) they stay.
            bool typing = ChatOpen();
            bool chat = typing && ChatOverlapsBars();
            if (chat != _chatShown || typing != _typingShown)
            {
                _chatShown = chat; _typingShown = typing;
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] chat typing {typing}, overlaps the bars {(typing ? chat.ToString() : "n/a")}: bars {(chat ? "hidden" : "shown")} (source {(s_chatEvent ? "Core ChatOpenChanged" : "Chat.HasFocus poll")})");
            }
            if (data == null || Player.m_localPlayer == null || chat || OverlayUp()) { SetVisible(false); return; }
            if (!EnsureBuiltAny()) return;   // UI overhaul #14: the kit rows under the override (ClassesHudKit.cs)
            SyncDock();

            SetVisible(true);
            if (_builtKit) KitPaint(data.ActiveBar);
            else
            {
                Paint(data.ActiveBar, _front);
                Paint(ClassPlayerData.Other(data.ActiveBar), _back);
            }
            _backGroup.alpha = _builtKit ? KitBackAlpha() : _backBarAlpha.Value;
            AnimateSwapCue();

            // The hints follow the configured keys live (twice a second; ConfigurationManager edits land without a rebuild).
            if (Time.time >= _nextHintRefresh)
            {
                _nextHintRefresh = Time.time + 0.5f;
                for (int i = 0; i < _front.Length; i++)
                {
                    if (_front[i]?.Binding == null) continue;
                    // SlotLabel allocates (it concatenates and KeyCode.ToString()s
                    // every call); compare the binding's own values first and only rebuild the label when one changed.
                    int signature = HintSignature(i + 1);
                    if (_front[i].HintSignature == signature && _front[i].HintText != null) continue;
                    _front[i].HintSignature = signature;
                    int slot = i + 1;
                    string text = ClassKeys.Slot(slot) == null ? slot.ToString() : ClassKeys.SlotLabel(slot);
                    _front[i].HintText = text;
                    _rebuilds++;
                    if (_front[i].Binding.text != text) _front[i].Binding.text = text;
                }
            }

            if (Time.unscaledTime >= _nextRebuildReport)
            {
                if (_nextRebuildReport > 0f)
                    FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHud] rebuilt {_rebuilds} strings in 60 s");
                _rebuilds = 0;
                _nextRebuildReport = Time.unscaledTime + 60f;
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
        // The per-minute self-check. Every string the HUD path builds (cooldown seconds, key hints, race-check reasons)
        // bumps this; the Debug log then shows "rebuilt N strings in 60 s" instead of thousands per minute.
        private static int _rebuilds;
        private static float _nextRebuildReport;

        internal static void CountRebuild() => _rebuilds++;

        // Everything SlotLabel reads, packed in an int: bound?, the key, and the three modifiers.
        private static int HintSignature(int slot)
        {
            global::FiresCore.Input.KeyBinding binding = ClassKeys.Slot(slot);
            if (binding == null) return -1;
            return ((int)binding.Key.Value << 3)
                | (binding.Ctrl.Value ? 1 : 0) | (binding.Shift.Value ? 2 : 0) | (binding.Alt.Value ? 4 : 0);
        }

        private bool ContainerAlive()
        {
            if (!_built) return false;
            if (_container != null) return true;
            _built = false;
            return false;
        }

        /// <summary>
        /// The bars have their own sorting canvas above the HUD, so they drew over the loading and quit/save screens.
        /// Hidden while the HUD is hidden by the player, a menu is open, the loading screen shows, or the game
        /// is shutting down.
        /// </summary>
        private static bool OverlayUp()
        {
            if (Hud.instance == null || Hud.IsUserHidden() || !Hud.instance.IsVisible()) return true;
            // The black screen (death, teleport, sleep, loading, quitting) fades its alpha while its object stays active.
            CanvasGroup black = Hud.instance.m_loadingScreen;
            if (black != null && black.gameObject.activeInHierarchy && black.alpha > 0.01f) return true;
            if (Menu.IsVisible()) return true;
            // Their own sorting canvas would draw them over the large map (M): hidden while the large map is open, as
            // vanilla's hotbar is
            if (Minimap.IsOpen()) return true;
            return Game.instance != null && Game.instance.IsShuttingDown();
        }

        // So the bars never draw over the chat text and tabs, they hide while the chat is open. Core's add-only
        // FiresShell.ChatOpenChanged(bool) drives it when present; before that Core, Chat.instance.HasFocus() is polled (one call
        // a frame). Only the typing state counts: the chat window fading in on someone else's message does not hide the bars.
        private bool _chatShown;
        private bool _typingShown;

        /// <summary>The chat window's screen rect overlaps the bars' screen rect (read from their RectTransforms, so it works
        /// whether the bars are hidden or shown). No chat window or no bars = no overlap.</summary>
        internal bool ChatOverlapsBars()
        {
            RectTransform chat = Chat.instance != null ? Chat.instance.m_chatWindow : null;
            if (chat == null || _container == null) return false;
            return ScreenRect(chat).Overlaps(ScreenRect(_container));
        }

        private static readonly Vector3[] s_corners = new Vector3[4];
        private static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(s_corners);
            Canvas c = rt.GetComponentInParent<Canvas>();
            Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, s_corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, s_corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
        private static bool s_chatLooked, s_chatEvent, s_chatOpenByEvent;
        private static bool ChatOpen()
        {
            if (!s_chatLooked)
            {
                s_chatLooked = true;
                try
                {
                    var ev = HarmonyLib.AccessTools.TypeByName("FiresCore.UI.Shell.FiresShell")?.GetEvent("ChatOpenChanged");
                    if (ev != null && ev.EventHandlerType == typeof(System.Action<bool>))
                    {
                        ev.AddEventHandler(null, new System.Action<bool>(open => s_chatOpenByEvent = open));
                        s_chatEvent = true;
                    }
                }
                catch (System.Exception ex) { FiresRPGClassesPlugin.Log?.LogWarning($"[ClassesHud] ChatOpenChanged subscribe failed ({ex.GetType().Name}); polling Chat.HasFocus"); }
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] chat hide source: {(s_chatEvent ? "Core FiresShell.ChatOpenChanged" : "Chat.HasFocus poll (no Core event)")}");
            }
            if (s_chatEvent) return s_chatOpenByEvent;
            return Chat.instance != null && Chat.instance.HasFocus();
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
                    // The weapon icon must not be faint: Availability() reads a skill node, and slot 1 has none,
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
                // Only when the second changes: otherwise a new "Ns" string and a TMP mesh rebuild per cooling slot per frame.
                if (cooling)
                {
                    int secs = Mathf.CeilToInt(CooldownTracker.Remaining(nodeId));
                    if (view.AmountShown != secs) { view.AmountShown = secs; view.Amount.text = secs + "s"; _rebuilds++; }
                }
                else view.AmountShown = -1;
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
            TryDock();

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

            // A visual cue for a bar swap: a gold flash over the bars plus a short scale
            // pulse on BarController.BarSwapped. Behind [5 - HUD] Bar swap cue (default off).
            RectTransform flash = NewRect(_container, "SwapFlash");
            flash.anchorMin = Vector2.zero; flash.anchorMax = Vector2.one; flash.offsetMin = new Vector2(-6f, -6f); flash.offsetMax = new Vector2(6f, 6f);
            _flash = flash.gameObject.AddComponent<Image>();
            _flash.color = new Color(1f, 0.82f, 0.4f, 0f);
            _flash.raycastTarget = false;
            if (!_swapHooked) { _swapHooked = true; BarController.BarSwapped += OnBarSwapped; }

            // Dump once, unprompted. Four diagnoses of the slot colouring have been wrong, and every one
            // was reasoning about values that only exist at runtime. This puts them in the log the first
            // time the bars exist, so the next report carries the answer instead of another guess.
#if FIRES_TESTS
            ClassesUiDump.DumpOnce("bars built");
#endif
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
                // grid shifted the whole row sideways.
                RectTransform element = go.transform as RectTransform;
                float pivotShift = element != null ? (0.5f - element.pivot.x) * element.rect.width : 0f;
                go.transform.localPosition = new Vector3(origin + i * space - pivotShift, 0f, 0f);
                slots[i] = Adopt(go, i + 1, reference);
                // The key hint: under the active row's slot, never on top of one; the back row shows none
                // (the slot keys always act on the active bar).
                RectTransform hint = slots[i].Binding.rectTransform;
                if (frontRow)
                {
                    // The hint must sit below the square, not inside its top edge over the icon and the count:
                    // anchoring to the element ROOT's bottom edge assumed the root is the square, but vanilla's HotKeyElement root
                    // is not sized like it, so "bottom" landed at the square's top and the top-pivot label hung down into it. Now
                    // it is measured from the visible square ("bkg") itself: centred under it, 2 px below its bottom edge, the
                    // same place as Core 0.2.528's KitHotbarSlot key chip.
                    RectTransform square = go.transform.Find("bkg") as RectTransform;
                    if (square == null) square = element;
                    var corners = new Vector3[4];
                    square.GetWorldCorners(corners);   // 0 bottom-left, 3 bottom-right
                    Vector3 bl = go.transform.InverseTransformPoint(corners[0]), br = go.transform.InverseTransformPoint(corners[3]);
                    hint.anchorMin = hint.anchorMax = element != null ? element.pivot : new Vector2(0.5f, 0.5f);   // the anchor = the root's own origin
                    hint.pivot = new Vector2(0.5f, 1f);
                    hint.anchoredPosition = new Vector2((bl.x + br.x) * 0.5f, bl.y - 2f);
                    hint.sizeDelta = new Vector2(Mathf.Max(br.x - bl.x, 1f), HintSpace - 2f);
                    slots[i].Binding.verticalAlignment = VerticalAlignmentOptions.Top;
                    slots[i].Binding.horizontalAlignment = HorizontalAlignmentOptions.Center;
                    if (i == 0)
                        FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] key hints under the squares: square '{square.name}' bottom y {bl.y:0.#} "
                            + $"(root rect h {(element != null ? element.rect.height : 0f):0.#}), hint top at {bl.y - 2f:0.#}, width {br.x - bl.x:0.#}");
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
