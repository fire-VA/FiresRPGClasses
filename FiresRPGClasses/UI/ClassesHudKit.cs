using System.Runtime.CompilerServices;
using FiresCore.UI.Shell;
using FiresCore.UI.Shell.Kit;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// UI overhaul #14 (Tools\golden\ui_overhaul\hotbars\PLAN.md, port row "Class front/back"): with Core's [UI] Fires UI override ON and
    /// Core's hotbar kit present (0.2.528+), the class bars are two kit HotbarRows (front Normal, back Behind) in the same FiresClassBars
    /// container, keeping its offset, drag and config keys, fed by [wishbone]'s ClassBarModel. Otherwise (override off, OR an older Core)
    /// the cloned HotKeyElement path in ClassesHud.cs runs unchanged.
    /// 0.1.164 ([wishbone]: kit-typed fields on ClassesHud made the whole HUD fail to load on Core &lt; 0.2.528, override off included):
    /// every kit type now lives in the nested KitBars class, held as object and reached only from NoInlining methods behind KitAvailable,
    /// so ClassesHud never touches a kit type on an older Core.
    /// </summary>
    public partial class ClassesHud
    {
        private object _kit;      // a KitBars while the kit path built the container
        private bool _builtKit;   // which path built the live container

        private static bool? s_kitAvailable;
        internal static bool KitAvailable
        {
            get
            {
                if (s_kitAvailable == null)
                {
                    s_kitAvailable = System.Type.GetType("FiresCore.UI.Shell.Kit.KitHotbarRow, FiresUnifiedCore", false) != null;
                    FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] Core hotbar kit {(s_kitAvailable.Value ? "found (0.2.528+)" : "absent (Core < 0.2.528): the class bars keep the cloned hotbar look")}");
                }
                return s_kitAvailable.Value;
            }
        }

        private static bool KitOn() { try { return KitAvailable && FiresShell.OverrideOn; } catch { return false; } }

        /// <summary>Builds via the path the override asks for; a live override flip tears the container down and rebuilds.</summary>
        private bool EnsureBuiltAny()
        {
            bool kit = KitOn();
            if (ContainerAlive() && _builtKit != kit)
            {
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] override {(kit ? "ON" : "OFF")}: rebuilding the bars as {(kit ? "kit HotbarRows" : "cloned hotbar elements")}");
                Destroy(_container.gameObject);
                _container = null; _built = false;
                _kit = null;
                for (int i = 0; i < _front.Length; i++) { _front[i] = null; _back[i] = null; }
            }
            bool ok = kit ? KitEnsureBuilt() : EnsureBuilt();
            if (ok) _builtKit = kit;
            // 0.1.167 ([wishbone]; Fire's hand test: NO dock line for the bars): TryDock was only called inside the clone path's EnsureBuilt,
            // so the kit bars never registered. Now after either build; TryDock returns at once when this container is already registered
            // and logs "registered" or "not docked: <why>" the first time for each container.
            if (ok) TryDock();
            return ok;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool KitEnsureBuilt()
        {
            if (ContainerAlive()) return true;
            var k = new KitBars(this);
            if (!k.Build()) return false;
            _kit = k;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void KitPaint(BarSide active) => (_kit as KitBars)?.Paint(active);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private float KitBackAlpha() => KitBars.BackAlpha(_backBarAlpha.Value);

        // 0.1.173 (Fire "REAL resize"; Core 0.2.574+ SizeChanged, VAInventory 1.0.231 hotbar pattern): Core sets the bars' rect size per axis
        // (diagonal grip = both, straight grip = one) and calls this; the 8 slots of each bar re-flow into cols x rows at their own size
        // (never stretched or scaled): a wide rect = 8x1 (today), narrower = 4x2, 3x3, 2x4. The back bar follows the front's grid above it.
        private void OnBarsResized(Vector2 want)
        {
            ClassesHudSizes.Set(DockId, want);
            KitResize(want, false);
        }

        /// <summary>2 slots wide (the smallest legible grid, 2x4), one front + back row tall.</summary>
        private static Vector2 BarsMinSize()
        {
            try { return KitAvailable ? KitBars.MinSize() : new Vector2(140f, 138f); } catch { return new Vector2(140f, 138f); }
        }

        /// <summary>Re-flows the kit bars into <paramref name="size"/>; applyRect = also set the rect (a saved size at register).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void KitResize(Vector2 size, bool applyRect)
        {
            var k = _kit as KitBars;
            if (k == null || _container == null) return;
            if (applyRect) ClassesHudSizes.ResizeKeepTopLeft(_container, size);
            k.Relayout(size);
        }

        /// <summary>Every kit-typed member of the class bars. Only touched when KitAvailable.</summary>
        private sealed class KitBars
        {
            private readonly ClassesHud _h;
            private KitHotbarRow _front, _back;
            private readonly ClassBarSlotModel[] _mFront = NewModels(), _mBack = NewModels();

            internal KitBars(ClassesHud hud) { _h = hud; }

            private static ClassBarSlotModel[] NewModels()
            {
                var m = new ClassBarSlotModel[BarLayout.SlotCount];
                for (int i = 0; i < m.Length; i++) m[i] = new ClassBarSlotModel();
                return m;
            }

            /// <summary>Fire's back-bar opacity (0.70 = the kit's BehindAlpha) unless the player changed [5 - HUD] BackBarAlpha from
            /// its 0.55 default; then their value. OFF keeps reading the key as before.</summary>
            internal static float BackAlpha(float configured) => Mathf.Approximately(configured, 0.55f) ? FiresTokens.BehindAlpha : configured;

            internal bool Build()
            {
                if (Hud.instance == null || Hud.instance.m_rootObject == null) return false;
                Transform hudRoot = Hud.instance.m_rootObject.transform;
                float space = HotbarKit.Pitch;

                _h._container = NewRect(hudRoot, ContainerName);
                _h._container.anchorMin = new Vector2(1f, 0f);
                _h._container.anchorMax = new Vector2(1f, 0f);
                _h._container.pivot = new Vector2(0.5f, 0f);
                CarryOverOldPosition(hudRoot as RectTransform);
                _h._container.anchoredPosition = _position.Value;
                _h._container.sizeDelta = new Vector2(BarLayout.SlotCount * space, SlotHeight * 2f + RowGap + HintSpace);
                Canvas canvas = _h._container.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = BarSortOrder;
                _h._container.gameObject.AddComponent<GraphicRaycaster>();
                Image grab = _h._container.gameObject.AddComponent<Image>();
                grab.color = new Color(0f, 0f, 0f, 0.001f);
                grab.raycastTarget = true;
                _h._container.gameObject.AddComponent<ClassesBarDragHandler>().Initialize(_h._container, _position);

                _back = HotbarKit.Row(_h._container, "BackBar", BarLayout.SlotCount, 0f, 0f, HotbarStyle.Behind);
                _front = HotbarKit.Row(_h._container, "FrontBar", BarLayout.SlotCount, 0f, 0f, HotbarStyle.Normal);
                if (_back == null || _front == null)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning("[ClassesHud] kit HotbarRow returned null; bars not built");
                    Destroy(_h._container.gameObject); _h._container = null; return false;
                }
                // Core 0.2.528: the key chip sits centred BELOW each slot (2 px gap, 15 px tall), so the front row keeps today's HintSpace
                // (18) under it; the Behind row (pivot bottom-centre, x0.72, set by the kit) goes BehindBottom (74.1) up, today's back row.
                CentreRow(_front.Root, HintSpace);
                // 0.1.168 ([wishbone]; Fire 10-15: the FRONT bar overlapped the BACK bar): FiresTokens.BehindBottom (74.1 = 64 x 0.72 + 10 + 18)
                // is a top-of-back sum used as the back's BOTTOM, so the back row started inside the front row. The back row's bottom is
                // now the front row's real top (its laid-out height x its scale, above the hint strip) + RowGap, as the approved mockup.
                float frontTop = HintSpace + _front.Root.rect.height * _front.Root.localScale.y;
                BackBottom = frontTop + RowGap;
                CentreRow(_back.Root, BackBottom);
                _back.Root.SetAsFirstSibling();

                _h._backGroup = _back.Root.GetComponent<CanvasGroup>() ?? _back.Root.gameObject.AddComponent<CanvasGroup>();
                _h._backGroup.blocksRaycasts = false;
                _h._backGroup.interactable = false;

                foreach (var row in new[] { _front, _back })
                    foreach (var s in row.Slots) { s.SetCount(0); s.SetDurability(-1f); }   // class slots carry neither
                _front.Slots[BarLayout.WeaponSlot - 1].Root.gameObject.AddComponent<ClassesBarWeaponDrop>().Initialize(true);
                _back.Slots[BarLayout.WeaponSlot - 1].Root.gameObject.AddComponent<ClassesBarWeaponDrop>().Initialize(false);

                _h._built = true;
                RectTransform flash = NewRect(_h._container, "SwapFlash");
                flash.anchorMin = Vector2.zero; flash.anchorMax = Vector2.one; flash.offsetMin = new Vector2(-6f, -6f); flash.offsetMax = new Vector2(6f, 6f);
                _h._flash = flash.gameObject.AddComponent<Image>();
                _h._flash.color = new Color(1f, 0.82f, 0.4f, 0f);
                _h._flash.raycastTarget = false;
                if (!_swapHooked) { _swapHooked = true; BarController.BarSwapped += OnBarSwapped; }
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHud] kit bars built: front HotbarRow {_front.Width:0} wide at {HintSpace:0}, back HotbarRow (Behind) at {BackBottom:0.#} (front top {HintSpace + _front.Root.rect.height * _front.Root.localScale.y:0.#} + gap {RowGap:0}), back alpha {BackAlpha(_backBarAlpha.Value):0.##} (BackBarAlpha {_backBarAlpha.Value:0.##}), container at {_h._container.anchoredPosition}");
                return true;
            }

            internal float BackBottom;

            internal const int MinCols = 2;

            internal static Vector2 MinSize()
            {
                float size = FiresTokens.HotbarSlotSize;
                return new Vector2(MinCols * HotbarKit.Pitch, HintSpace + size + RowGap + size * FiresTokens.BehindScale);
            }

            /// <summary>Height the bars need for a grid of <paramref name="rows"/> rows.</summary>
            private static float Needed(int rows)
            {
                float size = FiresTokens.HotbarSlotSize, gap = FiresTokens.HotbarSlotGap;
                return HintSpace + (rows * size + (rows - 1) * HintSpace) + RowGap + (rows * size + (rows - 1) * gap) * FiresTokens.BehindScale;
            }

            /// <summary>0.1.173: cols = the slots that fit the width (fewer when the height fits more rows), rows = ceil(8 / cols); the front
            /// grid's rows are a key chip apart, the back grid sits RowGap above it. At today's size this is exactly today's 8x1 layout.</summary>
            internal void Relayout(Vector2 want)
            {
                if (_front == null || _back == null || _h._container == null) return;
                int n = BarLayout.SlotCount;
                float size = FiresTokens.HotbarSlotSize, gap = FiresTokens.HotbarSlotGap;
                int fit = 1;
                for (int r = 2; r <= n; r++) { if (Needed(r) <= want.y + 0.5f) fit = r; else break; }
                int cols = ClassesHudSizes.Cols(n, want.x, HotbarKit.Pitch, gap, MinCols, fit);
                int rows = (n + cols - 1) / cols;
                HotbarKit.Grid(_front, cols, size, gap, HintSpace);
                HotbarKit.Grid(_back, cols, size, gap, gap);
                CentreRow(_front.Root, HintSpace);
                float frontTop = HintSpace + _front.Root.rect.height * _front.Root.localScale.y;
                BackBottom = frontTop + RowGap;
                CentreRow(_back.Root, BackBottom);
                ClassesHudSizes.GrowHeightKeepTop(_h._container, BackBottom + _back.Root.rect.height * _back.Root.localScale.y);
                ClassesHudSizes.NoteGrid(DockId, cols, rows);
            }



            private static void CentreRow(RectTransform row, float bottom)
            {
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 0f);
                row.pivot = new Vector2(0.5f, 0f);
                row.anchoredPosition = new Vector2(0f, bottom);
            }

            internal void Paint(BarSide active)
            {
                PaintRow(active, _front, _mFront);
                PaintRow(ClassPlayerData.Other(active), _back, _mBack);
            }

            private static void PaintRow(BarSide side, KitHotbarRow row, ClassBarSlotModel[] models)
            {
                if (row == null) return;
                ClassBarModel.Fill(side, models);
                for (int i = 0; i < models.Length && i < row.Slots.Length; i++)
                {
                    ClassBarSlotModel m = models[i];
                    KitHotbarSlot s = row.Slots[i];
                    s.SetIcon(m.Icon, m.Tint);
                    s.SetCooldown(m.Cooldown01, m.SecondsShown);   // the kit rebuilds the text only when the shown value changes
                    if (m.SecondsDirty) CountRebuild();
                    s.SetCharges(m.Charges, m.MaxCharges);
                    s.SetKey(m.KeyLabel);
                    s.SetState((HotbarSlotState)(int)m.State);      // same order: Normal, Empty, Disabled, Selected
                }
            }
        }
    }
}
