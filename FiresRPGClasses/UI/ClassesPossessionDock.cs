using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// 0.1.166 ([wishbone]; INVENTORY.md row classes.possession, Fire "every HUD element movable"): the possession strip and the summons
    /// chip join Core's HUD dock (override ON, Core 0.2.522+):
    ///   - "rpgclasses.possession": registered the first time PlaceStrip has found its real spot under the hotbar (registering at build
    ///     would freeze it at the build-time default), default-docked under Core's "hud.hotbar.vanilla" so it keeps today's "under the
    ///     hotbar" relation; while registered PlaceStrip writes nothing (Core holds the position).
    ///   - "rpgclasses.summons": registered at build, floating where it is.
    /// Both are Visible only while shown, so a hidden one leaves its group's flow. On override OFF both unregister and today's placement
    /// comes back (PlaceStrip every frame, the summons chip at (16,-40)).
    /// </summary>
    public partial class ClassesPossessionHud
    {
        internal const string StripDockId = "rpgclasses.possession", SummonsDockId = "rpgclasses.summons";
        private bool _stripDocked, _summonsDocked, _stripWaitLogged;
        private int _seenFlips;

        /// <summary>True while Core's dock engine owns the strip's position (PlaceStrip must not move it).</summary>
        private bool StripDocked => _stripDocked;

        private void SyncPossessionDock(bool stripPlacedUnderHotbar)
        {
            ClassesHudDock.HookOverride();
            if (_seenFlips != ClassesHudDock.OverrideFlips)
            {
                _seenFlips = ClassesHudDock.OverrideFlips;
                if (!ClassesHudDock.OverrideOn()) { UndockPossession(); return; }
            }
            if (_root == null) return;
            if (!_summonsDocked && _summons != null)
            {
                string why = ClassesHudDock.Register(SummonsDockId, "Summons", _summons, () => _summons != null && _summons.gameObject.activeSelf,
                    null, null, false, OnSummonsResized, SummonsMinSize);
                _summonsDocked = why == null;
                FiresRPGClassesPlugin.Log?.LogInfo(why == null ? $"[Classes] summons chip registered with Core's HUD dock as '{SummonsDockId}' (floating)" : $"[Classes] summons chip not docked: {why}");
                if (why != null) _summonsDocked = false;
            }
            // 0.1.167: say once why the strip is not registering yet (no hotbar found)
            if (!_stripDocked && !stripPlacedUnderHotbar && _strip != null && !_stripWaitLogged && _placedHow != null)
            {
                _stripWaitLogged = true;
                FiresRPGClassesPlugin.Log?.LogInfo($"[Classes] possession strip not docked yet: placed {_placedHow}; it registers once it sits under the hotbar");
            }
            if (!_stripDocked && stripPlacedUnderHotbar && _strip != null)
            {
                string why = ClassesHudDock.Register(StripDockId, "Possession strip", _strip, () => _strip != null && _strip.gameObject.activeSelf, "hud.hotbar.vanilla", "Bottom", true,
                    OnStripResized, StripMinSize);
                _stripDocked = why == null;
                // 0.1.173: a size the player gave the strip before is re-applied (the attack slots re-flow into it when a form shows)
                if (_stripDocked && ClassesHudSizes.TryGet(StripDockId, out Vector2 saved)) { ClassesHudSizes.ResizeKeepTopLeft(_strip, saved); RelayoutStripKit(saved); }
                FiresRPGClassesPlugin.Log?.LogInfo(why == null
                    ? $"[Classes] possession strip registered with Core's HUD dock as '{StripDockId}' (default dock under hud.hotbar.vanilla; PlaceStrip off)"
                    : $"[Classes] possession strip not docked: {why} (PlaceStrip stays)");
            }
        }

        // 0.1.173 (Fire "REAL resize"; Core 0.2.574+ SizeChanged, VAInventory 1.0.231 pattern): Core sizes the rect per axis, the content
        // re-flows at its own size (never stretched): the strip's attack slots go cols x rows with the caption beside or under them, the
        // summon list's entries go into columns. Sizes saved by ClassesHudSizes; no saved size = today's layout.
        private static readonly Vector2 SummonsMinSize = new Vector2(SummonWidth, 26f + SummonRow + 8f);
        private static Vector2 StripMinSize => new Vector2(64f, 81f);   // one attack slot + its key chip

        private void OnStripResized(Vector2 want)
        {
            ClassesHudSizes.Set(StripDockId, want);
            RelayoutStripKit(want);
        }

        private void OnSummonsResized(Vector2 want)
        {
            ClassesHudSizes.Set(SummonsDockId, want);
            _sigSummons = int.MinValue;   // re-laid on the next repaint (0.2 s cadence), not rebuilt every drag frame
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RelayoutStripKit(Vector2 want)
        {
            if (_kitMode && _kit != null) RelayoutKitStrip(want);
        }

        private void UndockPossession()
        {
            if (_stripDocked) { ClassesHudDock.Unregister(StripDockId); _stripDocked = false; if (_strip != null) _strip.localScale = Vector3.one; _placedHow = null; }
            if (_summonsDocked)
            {
                ClassesHudDock.Unregister(SummonsDockId); _summonsDocked = false; _sigSummons = int.MinValue;
                if (_summons != null) { _summons.anchoredPosition = new Vector2(16f, -40f); _summons.localScale = Vector3.one; }
            }
            FiresRPGClassesPlugin.Log?.LogInfo("[Classes] override OFF: possession strip + summons chip left Core's HUD dock; their own placement is back");
        }
    }
}
