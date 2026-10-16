using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresCore.UI;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Bars, from Fire's boards BarsEditor and BarsSwap: a card per bar (its weapon, the archetypes that weapon
    /// allows as toggles, the eight slot tiles), the hybrid line, and the pool of skills the edited bar's weapon allows.
    /// A pool tile goes onto a slot by dragging it there, or by clicking a slot and then the tile (or just the tile, which
    /// fills the first empty slot); right-click clears a slot, and dragging one slot onto another swaps them. After a
    /// weapon change the card shows the swap banner: skills the new weapon can't drive stay in place, greyed, with
    /// "Use &lt;Archetype&gt;" and "Load saved bar" beside them (doc 14 section 2).
    ///
    /// class_test: archetype toggles "Arch_&lt;Front|Back&gt;_&lt;Archetype&gt;"; slot tiles "Slot_&lt;Front|Back&gt;&lt;n&gt;"
    /// (the tile is the Button; clicking it picks the slot) with an "icon" Image; pool tiles "Pick_&lt;id&gt;" (skills) and
    /// "PickUlt_&lt;id&gt;" (ultimates). Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class BarsView : IClassesView, IClassesOverlay
    {
        private const float Pad = 16f;
        private const float CardGap = 10f;
        private const float CardPadX = 14f;
        private const float Tile = 58f;
        private const float TileGap = 8f;
        private const float CardHeight = 181f;
        private const float BannerHeight = 96f;
        private const float PoolRow = 54f;
        private const float PoolRowTall = 70f;
        private const float PoolGap = 6f;

        private static readonly Color WeaponRing = ClassesStyle.Hex("#b8925a");
        private static readonly Color Shade = new Color(10f / 255f, 8f / 255f, 6f / 255f, 0.62f);
        private static readonly Color BannerFill = ClassesStyle.Hex("#2a2112");

        public ClassesTab Tab => ClassesTab.Bars;

        public string Title => "Bars";

        private RectTransform _page;
        private RectTransform _top;
        private UIBuilderHelper.ScrollAreaResult _pool;

        /// <summary>The bar the pool fills; the active bar until a card, toggle or slot on the other one is used.</summary>
        private BarSide _editing = BarSide.Front;
        private bool _editingChosen;

        /// <summary>The slot clicked last, which the next pool click fills; -1 for none.</summary>
        private int _pickingSlot = -1;

        private ArchetypeClass _poolArchetype = ArchetypeClass.None;

        /// <summary>The bar and bar archetype the pool last followed.</summary>
        private BarSide _poolSide = BarSide.Front;
        private ArchetypeClass _poolSideArchetype = ArchetypeClass.None;

        /// <summary>The card whose saved-bars list is open, and where each card's "Saved (N)" button sits (right offset, top).</summary>
        private BarSide? _savedFor;
        private readonly Dictionary<BarSide, Vector2> _savedAnchor = new Dictionary<BarSide, Vector2>();

        private BarsDragSource _dragging;
        private bool _dropped;
        private BarSide _dropSide;
        private int _dropSlot;

        public void Build(RectTransform page)
        {
            _page = page;
            _top = ClassesStyle.Rect(page, "BarsBoard", Vector2.zero, Vector2.one);
            _pool = UIBuilderHelper.CreateScrollArea(page, "SkillPool", Vector2.zero, Vector2.one, Color.clear);
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null || !ClassTrees.IsLoaded) return;
            if (!_editingChosen)
            {
                _editing = data.ActiveBar;
                _editingChosen = true;
            }

            ClassesStyle.Clear(_top);
            float y = 12f;
            y = BarCard(data, BarSide.Front, y) + CardGap;
            y = BarCard(data, BarSide.Back, y) + CardGap;
            if (data.HasHybridPair) y = HybridLine(data, y) + CardGap;
            y = PoolHeader(data, y) + 8f;

            // The scroll area's viewport sits 5 px in and its scrollbar takes 18 px on the right; this lines the pool up
            // with the cards above it.
            RectTransform poolRect = _pool.Root.GetComponent<RectTransform>();
            poolRect.offsetMin = new Vector2(Pad - 5f, 8f);
            poolRect.offsetMax = new Vector2(18f - Pad, -y);
            BuildPool(data);

            if (_savedFor.HasValue && data.SavedBars.Count == 0) _savedFor = null;
            if (_savedFor.HasValue) SavedList(data, _savedFor.Value);
            // The board layer above the pool, so the saved-bars list can hang over it (it has no graphic of its own,
            // so it never blocks clicks meant for the pool).
            _top.SetAsLastSibling();
        }

        /// <summary>
        /// The saved bars, opened from "Saved (N)" on a card: one row per bar with Load (onto that card's bar) and Delete.
        /// Saving under an existing name overwrites it, so this is where old ones go.
        /// </summary>
        private void SavedList(ClassPlayerData data, BarSide side)
        {
            if (!_savedAnchor.TryGetValue(side, out Vector2 anchor)) return;
            const float width = 380f, rowHeight = 40f;
            List<SavedBar> saved = data.SavedBars.Take(10).ToList();
            RectTransform list = ClassesStyle.Rect(_top, "SavedBarsList", Vector2.one, Vector2.one);
            list.pivot = Vector2.one;
            list.sizeDelta = new Vector2(width, saved.Count * rowHeight + 16f);
            // Under the card's header, lined up with its right edge.
            list.anchoredPosition = new Vector2(-Pad - CardPadX, -anchor.y);
            ClassesStyle.Fill(list, ClassesStyle.Box(8, ClassesStyle.Tooltip, ClassesStyle.Gold, 1), Color.white, true);

            for (int i = 0; i < saved.Count; i++)
            {
                string name = saved[i].Name;
                RectTransform row = ClassesStyle.Rect(list, "SavedBar_" + name, new Vector2(0f, 1f), new Vector2(1f, 1f));
                row.offsetMin = new Vector2(12f, -8f - (i + 1) * rowHeight + 4f);
                row.offsetMax = new Vector2(-8f, -8f - i * rowHeight);
                TextMeshProUGUI label = ClassesStyle.Label(row, "Name", name, 15f, ClassesStyle.Text, Vector2.zero, new Vector2(0.55f, 1f));
                label.textWrappingMode = TextWrappingModes.NoWrap;
                ClassesStyle.Button(row, "Load", "Load", ClassesStyle.ButtonKind.Neutral, new Vector2(0.56f, 0f), new Vector2(0.77f, 1f),
                    () => { _savedFor = null; Load(side, name); }, 14f);
                ClassesStyle.Button(row, "Delete", "Delete", ClassesStyle.ButtonKind.Outline, new Vector2(0.79f, 0f), new Vector2(1f, 1f),
                    () =>
                    {
                        ClassPlayerData current = ClassPlayerStore.Local;
                        if (current != null && current.DeleteSavedBar(name)) ClassPlayerStore.SaveLocal();
                        Refresh();
                    }, 14f);
            }
        }

        // ---- one bar (boards BarsEditor / BarsSwap) ----------------------------------------------------------

        private float BarCard(ClassPlayerData data, BarSide side, float top)
        {
            BarLayout bar = data.Bar(side);
            bool front = side == BarSide.Front;
            // With VAInventory's bar slots (0.1.51) each bar's weapon and off hand come from its own equipment slots;
            // without them, from the prefab the bar recorded (0.1.50's behaviour).
            bool slots = BarController.HasBarSlots;
            ItemDrop.ItemData weapon = slots ? BarController.BarWeapon(side) : Item(bar.WeaponPrefab);
            ItemDrop.ItemData offHand = slots ? BarController.BarOffHand(side) : Item(bar.OffHandPrefab);
            bool weaponGone = !slots && !string.IsNullOrEmpty(bar.WeaponPrefab) && BarController.FindInInventory(bar.WeaponPrefab) == null;
            bool holdingOther = slots && side == data.ActiveBar && !BarController.HoldingBarWeapon(side);
            IReadOnlyList<ArchetypeClass> allowed = slots ? WeaponArchetypes.Allowed(weapon, offHand) : Allowed(bar);
            List<ArchetypeClass> undriven = Undriven(side, bar);
            // One banner at a time: a temporary hotbar swap first, then a missing weapon, then skills it can't drive.
            // Only the last one carries the "Use <Archetype>" / "Load saved bar" row.
            bool actions = !holdingOther && !weaponGone && undriven.Count > 0;
            bool banner = holdingOther || weaponGone || actions;
            float extra = !banner ? 0f : actions ? BannerHeight : BannerHeight - ClassesStyle.Target - 8f;

            RectTransform card = Band(_top, "Bar_" + side, top, CardHeight + extra, Pad, Pad);
            Image face = ClassesStyle.Fill(card,
                ClassesStyle.Box(8, ClassesStyle.Well, side == _editing ? ClassesStyle.Border : ClassesStyle.Line, 1), Color.white, true);
            // Clicking the card itself points the pool at this bar.
            Button focus = card.gameObject.AddComponent<Button>();
            focus.targetGraphic = face;
            focus.transition = Selectable.Transition.None;
            focus.onClick.AddListener(() => Edit(side));

            Text(card, "Title", front ? "Front bar" : "Back bar", 18f, front ? ClassesStyle.Gold : ClassesStyle.Blue,
                CardPadX, 13f, 380f, 22f, TextAlignmentOptions.MidlineLeft, true);
            string weaponLine = slots
                ? (weapon == null ? "Unarmed" : WeaponLine(bar, weapon, offHand)) + "  ·  set in the equipment panel"
                : WeaponLine(bar, weapon, offHand);
            Text(card, "Weapon", weaponLine, 14f, ClassesStyle.Muted, CardPadX, 35f, 460f, 18f);

            // The archetypes this weapon allows, the chosen one lit; the first is the weapon's default.
            float right = CardPadX;
            for (int i = allowed.Count - 1; i >= 0; i--)
            {
                ArchetypeClass archetype = allowed[i];
                string caption = ClassNames.Of(archetype) + (i == 0 ? " (default)" : "");
                RightButton(card, "Arch_" + side + "_" + archetype, caption,
                    bar.Archetype == archetype ? ClassesStyle.ButtonKind.Primary : ClassesStyle.ButtonKind.Outline,
                    ref right, 13f, 40f, () => Choose(side, archetype), 15f);
            }
            right += 6f;
            RightButton(card, "SaveBar_" + side, "Save bar", ClassesStyle.ButtonKind.Outline, ref right, 13f, 40f,
                () => Save(side), 14f);
            if (data.SavedBars.Count > 0)
            {
                RightButton(card, "SavedBars_" + side, "Saved (" + data.SavedBars.Count + ")",
                    _savedFor == side ? ClassesStyle.ButtonKind.Primary : ClassesStyle.ButtonKind.Outline, ref right, 13f, 40f,
                    () => { _savedFor = _savedFor == side ? (BarSide?)null : side; Refresh(); }, 14f);
                _savedAnchor[side] = new Vector2(right - 6f, top + 57f);
            }

            float tilesTop = 63f;
            if (banner)
            {
                RectTransform strip = Band(card, "Banner", 61f, 36f, CardPadX, CardPadX);
                ClassesStyle.Fill(strip, ClassesStyle.Box(6, BannerFill, ClassesStyle.Amber, 1), Color.white);
                string text = holdingOther
                    ? "Holding another weapon: this bar's skills are dimmed until you switch bars or re-equip its weapon."
                    : BannerText(bar, weapon, weaponGone, undriven);
                TextMeshProUGUI note = ClassesStyle.Label(strip, "Text", text, 14f, ClassesStyle.Text, Vector2.zero, Vector2.one);
                note.rectTransform.offsetMin = new Vector2(12f, 0f);
                note.rectTransform.offsetMax = new Vector2(-12f, 0f);
                // The weapons list can run long: shrink to fit the strip rather than cut it off.
                note.enableAutoSizing = true;
                note.fontSizeMin = 12f;   // 0.1.151: never below 12 (Fire: readable after the hub scale); wraps instead
                note.textWrappingMode = TextWrappingModes.Normal; note.overflowMode = TextOverflowModes.Ellipsis;
                note.fontSizeMax = 14f;

                if (actions)
                {
                    float left = CardPadX;
                    for (int i = 0; i < allowed.Count; i++)
                    {
                        ArchetypeClass archetype = allowed[i];
                        LeftButton(card, "Use_" + side + "_" + archetype, "Use " + ClassNames.Of(archetype),
                            i == 0 ? ClassesStyle.ButtonKind.Primary : ClassesStyle.ButtonKind.Neutral,
                            ref left, 105f, ClassesStyle.Target, () => Choose(side, archetype), 16f);
                    }
                    SavedBar saved = SavedFor(data, bar);
                    if (saved != null)
                    {
                        string name = saved.Name;
                        LeftButton(card, "LoadSaved_" + side, "Load saved bar: " + name, ClassesStyle.ButtonKind.Outline,
                            ref left, 105f, ClassesStyle.Target, () => Load(side, name), 16f);
                    }
                }
                tilesTop += extra;
            }

            for (int slot = 1; slot <= BarLayout.SlotCount; slot++)
                SlotTile(data, card, side, bar, slot, CardPadX + (slot - 1) * (Tile + TileGap), tilesTop,
                    weapon, offHand, slots, holdingOther);
            return top + CardHeight + extra;
        }

        private static readonly System.Collections.Generic.HashSet<string> s_hiddenLogged = new System.Collections.Generic.HashSet<string>();

        private void SlotTile(ClassPlayerData data, RectTransform card, BarSide side, BarLayout bar, int slot, float x, float y,
            ItemDrop.ItemData weapon, ItemDrop.ItemData offHand, bool slots, bool holdingOther)
        {
            bool weaponSlot = slot == BarLayout.WeaponSlot, ultimateSlot = slot == BarLayout.UltimateSlot;
            string id = weaponSlot ? "" : ultimateSlot ? bar.UltimateId : bar.SkillAt(slot);
            SlotAvailability availability = weaponSlot ? SlotAvailability.Ready : BarController.Availability(side, slot);
            bool ultimateLocked = ultimateSlot && data.Progress.Level < UltimateCatalog.SlotUnlockLevel;
            bool dim = ultimateLocked
                       || (weaponSlot && holdingOther)
                       || availability == SlotAvailability.WrongWeapon
                       || availability == SlotAvailability.WrongArchetype
                       || availability == SlotAvailability.BarUnusable
                       || availability == SlotAvailability.NotLearned
                       || availability == SlotAvailability.LevelLocked;
            // 0.1.143 [classes-ui] (Fire: "no unusable skills shown"), always on since 0.1.148 (no key): a skill this bar's weapon can't drive is drawn
            // as an empty slot (BarController.CanDrive / the wrong-weapon states) instead of a greyed tile. Ultimate lock unchanged.
            if (!weaponSlot && !string.IsNullOrEmpty(id)
                && (availability == SlotAvailability.WrongWeapon || availability == SlotAvailability.WrongArchetype
                    || availability == SlotAvailability.BarUnusable || !BarController.CanDrive(side, id)))
            {
                if (s_hiddenLogged.Add(side + "/" + slot + "/" + id)) FiresRPGClassesPlugin.Log.LogDebug($"[Bars] card {side} slot {slot}: '{id}' hidden ({availability}, CanDrive {BarController.CanDrive(side, id)})");
                id = "";
                dim = ultimateLocked;
            }
            bool picked = side == _editing && slot == _pickingSlot;

            RectTransform tile = At(card, "Slot_" + side + slot, x, y, Tile, Tile);
            Image face = ClassesStyle.Fill(tile, ClassesStyle.Box(8,
                weaponSlot || picked ? ClassesStyle.Selected : ClassesStyle.Neutral,
                picked ? ClassesStyle.Gold : weaponSlot ? WeaponRing : ClassesStyle.Border, 2), Color.white, true);

            Sprite sprite;
            string name;
            if (weaponSlot)
            {
                // A read-out: with bar slots the weapon is set only in VAInventory's equipment panel.
                sprite = weapon?.GetIcon();
                name = slots && weapon == null ? "Unarmed" : "Weapon";
            }
            else if (ultimateSlot)
            {
                UltimateDefinition ultimate = UltimateCatalog.Get(id);
                sprite = UltimateIcon(ultimate);
                name = ultimate != null ? UltimateName(ultimate) : ultimateLocked ? "Level " + UltimateCatalog.SlotUnlockLevel : "";
            }
            else
            {
                sprite = ClassIcons.For(id);
                name = ClassTrees.Node(id)?.DisplayName ?? "";
            }

            RectTransform glyph = ClassesStyle.Fixed(tile, "icon", new Vector2(0.5f, 0.5f), new Vector2(34f, 34f));
            Image icon = ClassesStyle.Fill(glyph, sprite, weaponSlot ? Color.white : ClassesStyle.Available);
            icon.preserveAspect = true;
            if (sprite == null) icon.enabled = false;

            // The bar's off hand as a small badge in the weapon tile's corner.
            Sprite offIcon = weaponSlot ? offHand?.GetIcon() : null;
            if (offIcon != null)
            {
                RectTransform badge = ClassesStyle.Fixed(tile, "offhand", new Vector2(1f, 0f), new Vector2(22f, 22f));
                badge.anchoredPosition = new Vector2(-12f, 12f);
                ClassesStyle.Fill(badge, ClassesStyle.Box(4, ClassesStyle.Well, WeaponRing, 1), Color.white);
                RectTransform offGlyph = ClassesStyle.Fixed(badge, "icon", new Vector2(0.5f, 0.5f), new Vector2(18f, 18f));
                ClassesStyle.Fill(offGlyph, offIcon, Color.white).preserveAspect = true;
            }

            if (dim)
            {
                RectTransform shade = ClassesStyle.Rect(tile, "Shade", Vector2.zero, Vector2.one);
                shade.offsetMin = new Vector2(2f, 2f);
                shade.offsetMax = new Vector2(-2f, -2f);
                ClassesStyle.Fill(shade, ClassesStyle.Box(6, Shade), Color.white);
            }

            TextMeshProUGUI number = ClassesStyle.Label(tile, "Number", slot.ToString(), 12f, ClassesStyle.Muted,
                new Vector2(0f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Center);
            number.rectTransform.offsetMin = new Vector2(0f, -18f);
            number.rectTransform.offsetMax = new Vector2(0f, -3f);
            TextMeshProUGUI label = ClassesStyle.Label(tile, "Name", name, 11f, dim ? ClassesStyle.LockedSub : ClassesStyle.Muted,
                new Vector2(0f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Top);
            label.rectTransform.offsetMin = new Vector2(-3f, -46f);
            label.rectTransform.offsetMax = new Vector2(3f, -19f);

            if (weaponSlot) return;
            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            int chosen = slot;
            button.onClick.AddListener(() => PickSlot(side, chosen));
            BarsDropTarget target = tile.gameObject.AddComponent<BarsDropTarget>();
            target.Init(this, side, slot);
            if (!string.IsNullOrEmpty(id))
                tile.gameObject.AddComponent<BarsDragSource>().Init(this, id, ultimateSlot, side, slot, sprite);
        }

        /// <summary>Archetypes of slotted skills this bar's weapon can't drive (board BarsSwap's "can't drive Paladin").</summary>
        private static List<ArchetypeClass> Undriven(BarSide side, BarLayout bar)
        {
            var list = new List<ArchetypeClass>();
            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.UltimateSlot; slot++)
            {
                if (BarController.Availability(side, slot) != SlotAvailability.WrongArchetype) continue;
                ArchetypeClass archetype = slot == BarLayout.UltimateSlot
                    ? UltimateCatalog.Get(bar.UltimateId)?.Archetype ?? ArchetypeClass.None
                    : ClassTrees.Node(bar.SkillAt(slot))?.Archetype ?? ArchetypeClass.None;
                if (archetype != ArchetypeClass.None && !list.Contains(archetype)) list.Add(archetype);
            }
            return list;
        }

        private static string BannerText(BarLayout bar, ItemDrop.ItemData weapon, bool weaponGone, List<ArchetypeClass> undriven)
        {
            string noun = WeaponNoun(weapon);
            if (weaponGone)
                return "Your " + noun + " isn't in your inventory, so this bar can't fire. Pick it up again, or load a saved bar.";
            // Say what each of them does take, so the fix is obvious (Fire 10-01).
            string takes = string.Join("; ", undriven.Select(a => a + " takes " + WeaponArchetypes.WeaponsLine(a).ToLowerInvariant()).ToArray());
            return "Your " + noun + " can't drive " + string.Join(" or ", undriven.Select(a => a.ToString()).ToArray())
                   + " (" + takes + "). Those skills stay on the bar, greyed out, until you slot skills this weapon allows.";
        }

        private float HybridLine(ClassPlayerData data, float top)
        {
            HybridArchetypeDefinitions.HybridDefinition hybrid =
                HybridArchetypeDefinitions.GetHybrid(data.Front.Archetype, data.Back.Archetype);
            if (hybrid == null) return top - CardGap;
            RectTransform line = Band(_top, "Hybrid", top, 18f, Pad + 10f, Pad);
            ClassesStyle.Label(line, "Text", ClassNames.Of(data.Front.Archetype) + " front, " + ClassNames.Of(data.Back.Archetype) + " back: "
                    + hybrid.DisplayName + " (" + hybrid.SpecialAbilityName + ") is active.", 14f, ClassesStyle.Purple,
                Vector2.zero, Vector2.one);
            return top + 18f;
        }

        // ---- the skill pool ---------------------------------------------------------------------------------

        private float PoolHeader(ClassPlayerData data, float top)
        {
            RectTransform head = Band(_top, "PoolHeader", top, 40f, Pad, Pad);
            BarLayout bar = data.Bar(_editing);
            IReadOnlyList<ArchetypeClass> allowed = Allowed(bar);
            // The pool follows the edited bar's archetype whenever the bar or its archetype changes; a pill picks another.
            if (_poolSide != _editing || _poolSideArchetype != bar.Archetype)
            {
                _poolSide = _editing;
                _poolSideArchetype = bar.Archetype;
                _poolArchetype = bar.Archetype;
            }
            if (!allowed.Contains(_poolArchetype))
                _poolArchetype = allowed.Contains(bar.Archetype) ? bar.Archetype : allowed.Count > 0 ? allowed[0] : ArchetypeClass.None;

            float right = 0f;
            for (int i = allowed.Count - 1; i >= 0; i--)
            {
                ArchetypeClass archetype = allowed[i];
                Button pill = ClassesStyle.Pill(head, "Pool_" + archetype, ClassNames.Of(archetype), Vector2.one, Vector2.one,
                    () => { _poolArchetype = archetype; Refresh(); }, 14f);
                float width = Width(pill, ClassNames.Of(archetype));
                RectTransform rect = (RectTransform)pill.transform;
                rect.pivot = Vector2.one;
                rect.sizeDelta = new Vector2(width, 40f);
                rect.anchoredPosition = new Vector2(-right, 0f);
                ClassesStyle.PaintPill(pill, archetype == _poolArchetype);
                right += width + 6f;
            }

            Text(head, "Title", "Skills this weapon allows", 16f, ClassesStyle.Text, 0f, 0f, 230f, 40f,
                TextAlignmentOptions.MidlineLeft, true);
            string which = _editing == BarSide.Front ? "front" : "back";
            string hint = BarLayout.IsSkillSlot(_pickingSlot) || _pickingSlot == BarLayout.UltimateSlot
                ? "Choose one for " + which + " slot " + _pickingSlot
                : "For the " + which + " bar: drag one onto a slot, or click a slot and then a skill";
            TextMeshProUGUI hintLabel = ClassesStyle.Label(head, "Hint", hint, 13f,
                _pickingSlot > 0 ? ClassesStyle.Gold : ClassesStyle.Muted, Vector2.zero, Vector2.one);
            hintLabel.rectTransform.offsetMin = new Vector2(236f, 0f);
            hintLabel.rectTransform.offsetMax = new Vector2(-right - 6f, 0f);
            return top + 40f;
        }

        private sealed class PoolEntry
        {
            public string Id;
            public string Name;
            public string Sub;
            public string Third;
            public bool Ultimate;
            public bool Variant;
            public bool Usable;
            public bool Held;
            public Sprite Icon;
        }

        private void BuildPool(ClassPlayerData data)
        {
            ClassesStyle.Clear(_pool.Content);
            List<PoolEntry> entries = PoolEntries(data);
            if (entries.Count == 0)
            {
                TextMeshProUGUI empty = ClassesStyle.Label(_pool.Content, "Empty",
                    "This bar's weapon doesn't drive any archetype.", 14f, ClassesStyle.Muted,
                    new Vector2(0f, 1f), new Vector2(1f, 1f));
                empty.rectTransform.offsetMin = new Vector2(4f, -30f);
                empty.rectTransform.offsetMax = new Vector2(0f, 0f);
                _pool.Content.sizeDelta = new Vector2(0f, 30f);
                return;
            }

            float y = 0f;
            for (int start = 0; start < entries.Count; start += 3)
            {
                List<PoolEntry> row = entries.Skip(start).Take(3).ToList();
                float height = row.Any(entry => entry.Third != null) ? PoolRowTall : PoolRow;
                for (int column = 0; column < row.Count; column++) PoolTile(row[column], column, y, height);
                y += height + PoolGap;
            }
            _pool.Content.sizeDelta = new Vector2(0f, y);
        }

        private List<PoolEntry> PoolEntries(ClassPlayerData data)
        {
            var entries = new List<PoolEntry>();
            if (_poolArchetype == ArchetypeClass.None) return entries;
            BarLayout bar = data.Bar(_editing);
            int level = data.Progress.Level;

            foreach (ClassTreeNode node in ClassTrees.ActivesFor(_poolArchetype).OrderBy(n => n.MinLevel).ThenBy(n => n.Tier))
            {
                bool learned = data.Progress.TalentRank(node.Archetype, node.Id) > 0;
                bool levelOk = level >= node.MinLevel;
                entries.Add(new PoolEntry
                {
                    Id = node.Id,
                    Name = node.DisplayName,
                    Sub = TalentsView.Describe(node.Targeting),
                    Third = !levelOk ? "Level " + node.MinLevel : !learned ? "Learn it in the " + ClassNames.Of(node.Archetype) + " tree" : null,
                    Variant = !string.IsNullOrEmpty(node.VariantGroup),
                    Usable = learned && levelOk,
                    Held = bar.Holds(node.Id),
                    Icon = ClassIcons.For(node.Id),
                });
            }

            var ultimates = new List<UltimateDefinition>(UltimateCatalog.For(_poolArchetype));
            if (data.HasHybridPair)
                ultimates.AddRange(UltimateCatalog.ForHybrid(data.Front.Archetype, data.Back.Archetype)
                    .Where(u => u.Archetype == _poolArchetype || u.HybridPartner == _poolArchetype));
            foreach (UltimateDefinition ultimate in ultimates)
            {
                string why = null;
                if (level < UltimateCatalog.SlotUnlockLevel || level < ultimate.UnlockLevel)
                    why = "Level " + Mathf.Max(UltimateCatalog.SlotUnlockLevel, ultimate.UnlockLevel);
                else if (!UltimateController.MeetsPointGate(data, ultimate))
                    why = ultimate.IsHybrid
                        ? "Needs " + UltimateCatalog.PointsInEachArchetypeForHybrid + " points in each tree"
                        : "Needs " + (ultimate.UnlockLevel >= UltimateCatalog.UltimateTwoLevel
                            ? UltimateCatalog.PointsInPathForUltimateTwo
                            : UltimateCatalog.PointsInTreeForUltimateOne) + " points in the " + ClassNames.Of(ultimate.Archetype) + " tree";
                else if (ultimate.IsHybrid && (ultimate.Archetype != data.Front.Archetype || ultimate.HybridPartner != data.Back.Archetype))
                    why = "Needs " + ClassNames.Of(ultimate.Archetype) + " front, " + ClassNames.Of(ultimate.HybridPartner) + " back";

                entries.Add(new PoolEntry
                {
                    Id = ultimate.Id,
                    Name = UltimateName(ultimate),
                    Sub = ultimate.IsHybrid ? HybridName(ultimate) : TalentsView.Describe(ClassTrees.Node(ultimate.Id)?.Targeting),
                    Third = why,
                    Ultimate = true,
                    Usable = why == null,
                    Held = bar.UltimateId == ultimate.Id,
                    Icon = UltimateIcon(ultimate),
                });
            }
            return entries;
        }

        private void PoolTile(PoolEntry entry, int column, float y, float height)
        {
            RectTransform tile = ClassesStyle.Rect(_pool.Content, (entry.Ultimate ? "PickUlt_" : "Pick_") + entry.Id,
                new Vector2(column / 3f, 1f), new Vector2((column + 1) / 3f, 1f));
            tile.pivot = new Vector2(0.5f, 1f);
            tile.offsetMin = new Vector2(column * PoolGap / 3f, -y - height);
            tile.offsetMax = new Vector2(-(2 - column) * PoolGap / 3f, -y);
            Image face = ClassesStyle.Fill(tile, ClassesStyle.Box(6, ClassesStyle.Panel, entry.Held ? ClassesStyle.Gold : ClassesStyle.Line, 1),
                Color.white, true);
            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.08f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            colors.colorMultiplier = 1.2f;
            button.colors = colors;
            button.interactable = entry.Usable;
            button.onClick.AddListener(() => PoolClick(entry));

            RectTransform glyph = ClassesStyle.Fixed(tile, "icon", new Vector2(0f, 1f), new Vector2(26f, 26f));
            glyph.anchoredPosition = new Vector2(11f + 13f, -8f - 13f);
            Image icon = ClassesStyle.Fill(glyph, entry.Icon, entry.Usable ? ClassesStyle.Available : ClassesStyle.LockedSub);
            icon.preserveAspect = true;
            if (entry.Icon == null) icon.enabled = false;

            Text(tile, "Name", entry.Name, 14f, entry.Usable ? ClassesStyle.Text : ClassesStyle.LockedText, 45f, 8f, 230f, 18f);
            TextMeshProUGUI sub = Text(tile, "Sub", entry.Sub ?? "", 12f, ClassesStyle.Muted, 45f, 27f, 150f, 16f);
            float badgeX = 45f + (string.IsNullOrEmpty(entry.Sub) ? 0f : sub.GetPreferredValues(entry.Sub, 999f, 16f).x + 6f);
            if (entry.Ultimate) BadgeAt(tile, "Ultimate", "Ultimate", ClassesStyle.Gold, badgeX, 27f, 58f);
            else if (entry.Variant) BadgeAt(tile, "Variant", "Variant", ClassesStyle.Available, badgeX, 27f, 54f, true);
            if (entry.Third != null)
                Text(tile, "Why", entry.Third, 11f, ClassesStyle.LockedText, 45f, 45f, 240f, 15f);

            if (entry.Usable)
                tile.gameObject.AddComponent<BarsDragSource>().Init(this, entry.Id, entry.Ultimate, _editing, 0, entry.Icon);
        }

        private static void BadgeAt(RectTransform parent, string name, string text, Color color, float x, float y, float width,
            bool dashed = false)
        {
            TextMeshProUGUI badge = ClassesStyle.Badge(parent, name, text, color, new Vector2(0f, 1f), new Vector2(0f, 1f));
            badge.fontSize = 11f;
            RectTransform rect = (RectTransform)badge.rectTransform.parent;
            // The board draws "Variant" with a dashed outline and "Ultimate" with a solid one.
            if (dashed) ClassesStyle.FillDashed(rect, 6, Color.clear, color, 1);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, 16f);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        // ---- actions ----------------------------------------------------------------------------------------

        private void Edit(BarSide side)
        {
            if (_editing == side && _pickingSlot < 0) return;
            _editing = side;
            _pickingSlot = -1;
            Refresh();
        }

        /// <summary>
        /// Picks a slot for the next pool click. Picking it again keeps it picked (a click on the card clears it), so a
        /// second click never silently sends the skill to another slot. The pool turns to the archetype of the skill
        /// already there, when this weapon allows it.
        /// </summary>
        private void PickSlot(BarSide side, int slot)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            BarLayout bar = data.Bar(side);
            if (_editing != side) _poolArchetype = bar.Archetype;
            _editing = side;
            _pickingSlot = slot;
            _poolSide = side;
            _poolSideArchetype = bar.Archetype;
            ArchetypeClass held = slot == BarLayout.UltimateSlot
                ? UltimateCatalog.Get(bar.UltimateId)?.Archetype ?? ArchetypeClass.None
                : ClassTrees.Node(bar.SkillAt(slot))?.Archetype ?? ArchetypeClass.None;
            if (held != ArchetypeClass.None && Allowed(bar).Contains(held)) _poolArchetype = held;
            Refresh();
        }

        private void Choose(BarSide side, ArchetypeClass archetype)
        {
            _editing = side;
            _poolArchetype = archetype;
            _pickingSlot = -1;
            // BarsChanged refreshes the window when the choice lands.
            if (!BarController.TryChooseArchetype(side, archetype)) Refresh();
        }

        private void PoolClick(PoolEntry entry)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || !entry.Usable) return;
            BarSide side = _editing;
            if (entry.Ultimate)
            {
                _pickingSlot = -1;
                if (!BarController.TrySlotUltimate(side, entry.Id, MorphFor(data, side, entry.Id))) Refresh();
                return;
            }
            int slot = BarLayout.IsSkillSlot(_pickingSlot) ? _pickingSlot : FirstEmptySkillSlot(data.Bar(side));
            if (slot < 0)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Click a slot on the bar first");
                return;
            }
            _pickingSlot = -1;
            if (!BarController.TrySlotSkill(side, slot, entry.Id)) Refresh();
        }

        internal void ClearSlot(BarSide side, int slot)
        {
            _pickingSlot = -1;
            if (slot == BarLayout.UltimateSlot) BarController.TrySlotUltimate(side, "", 0);
            else if (BarLayout.IsSkillSlot(slot)) BarController.TrySlotSkill(side, slot, "");
        }

        private void Save(BarSide side)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            string name = SavedName(data.Bar(side));
            if (data.SaveBar(name, side))
            {
                ClassPlayerStore.SaveLocal();
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Saved bar: " + name);
            }
            Refresh();
        }

        private void Load(BarSide side, string name)
        {
            _editing = side;
            _pickingSlot = -1;
            if (!BarController.TryLoadSavedBar(side, name)) Refresh();
        }

        // ---- drag and drop ----------------------------------------------------------------------------------

        internal void BeginDrag(BarsDragSource source)
        {
            _dragging = source;
            _dropped = false;
        }

        /// <summary>
        /// Only records the target. The source's EndDrag applies it: the drop runs first, and applying it here would rebuild
        /// the board (BarsChanged) and destroy the dragged tile before its EndDrag could clear the ghost.
        /// </summary>
        internal void DropOn(BarSide side, int slot)
        {
            if (_dragging == null) return;
            _dropped = true;
            _dropSide = side;
            _dropSlot = slot;
        }

        internal void EndDrag(BarsDragSource source)
        {
            if (_dragging != source) return;
            _dragging = null;
            if (!_dropped) return;
            _dropped = false;

            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            BarSide side = _dropSide;
            int slot = _dropSlot;
            _editing = side;
            _pickingSlot = -1;

            if (source.Ultimate)
            {
                if (slot != BarLayout.UltimateSlot || (source.FromSlot == slot && source.FromSide == side)) return;
                BarController.TrySlotUltimate(side, source.NodeId, MorphFor(data, side, source.NodeId));
                return;
            }
            if (!BarLayout.IsSkillSlot(slot) || (source.FromSlot == slot && source.FromSide == side)) return;
            // Within one bar a slot dropped on a slot swaps the two; TrySlotSkill already empties the one it came from.
            string displaced = source.FromSlot > 0 && source.FromSide == side ? data.Bar(side).SkillAt(slot) : "";
            if (!BarController.TrySlotSkill(side, slot, source.NodeId)) return;
            if (!string.IsNullOrEmpty(displaced) && displaced != source.NodeId)
                BarController.TrySlotSkill(side, source.FromSlot, displaced);
        }

        // ---- helpers ----------------------------------------------------------------------------------------

        private static int FirstEmptySkillSlot(BarLayout bar)
        {
            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.LastSkillSlot; slot++)
                if (string.IsNullOrEmpty(bar.SkillAt(slot))) return slot;
            return -1;
        }

        private static int MorphFor(ClassPlayerData data, BarSide side, string ultimateId) =>
            data.Bar(side).UltimateId == ultimateId ? data.Bar(side).UltimateMorph : 0;

        /// <summary>A saved bar for this bar's weapon: same weapon and off-hand first, then the same weapon.</summary>
        private static SavedBar SavedFor(ClassPlayerData data, BarLayout bar)
        {
            if (string.IsNullOrEmpty(bar.WeaponPrefab)) return null;
            return data.SavedBars.FirstOrDefault(saved => saved.Layout.WeaponPrefab == bar.WeaponPrefab
                                                          && saved.Layout.OffHandPrefab == bar.OffHandPrefab)
                   ?? data.SavedBars.FirstOrDefault(saved => saved.Layout.WeaponPrefab == bar.WeaponPrefab);
        }

        /// <summary>The board's saved-bar name: "Ranger, crude bow".</summary>
        private static string SavedName(BarLayout bar) =>
            (bar.Archetype == ArchetypeClass.None ? "No archetype" : ClassNames.Of(bar.Archetype)) + ", " + WeaponNoun(Item(bar.WeaponPrefab));

        /// <summary>
        /// The archetypes the bar's own weapon allows. BarController.AllowedArchetypes reads only the inventory, so a bar
        /// whose sword was dropped would offer the unarmed Monk / Berserker; this reads the prefab when the item is gone.
        /// </summary>
        private static IReadOnlyList<ArchetypeClass> Allowed(BarLayout bar) =>
            WeaponArchetypes.Allowed(Item(bar.WeaponPrefab), Item(bar.OffHandPrefab));

        /// <summary>The weapon to show for a bar: the one in the inventory, else the prefab's own data (for its name and icon).</summary>
        private static ItemDrop.ItemData Item(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            ItemDrop.ItemData held = BarController.FindInInventory(prefab);
            if (held != null) return held;
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData : null;
        }

        private static string ItemName(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return "";
            return Localization.instance != null ? Localization.instance.Localize(item.m_shared.m_name) : item.m_shared.m_name;
        }

        private static string WeaponNoun(ItemDrop.ItemData weapon)
        {
            string name = ItemName(weapon);
            return name.Length == 0 ? "bare hands" : LowerFirst(name);
        }

        private static string WeaponLine(BarLayout bar, ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            string main = ItemName(weapon);
            if (main.Length == 0) return string.IsNullOrEmpty(bar.WeaponPrefab) ? "Unarmed" : bar.WeaponPrefab;
            string off = ItemName(offHand);
            return off.Length == 0 ? main : main + " and " + LowerFirst(off);
        }

        private static string LowerFirst(string text) =>
            string.IsNullOrEmpty(text) ? "" : char.ToLowerInvariant(text[0]) + text.Substring(1);

        /// <summary>Esc or a background click: closes the saved-bars list, else drops the picked slot; false when neither.</summary>
        public bool CloseOverlay()
        {
            if (_savedFor.HasValue) _savedFor = null;
            else if (_pickingSlot >= 0) _pickingSlot = -1;
            else return false;
            Refresh();
            return true;
        }

        /// <summary>A hybrid's catalog name is "High Priest - Mass Restoration": the tile shows the ability, the sub-line the hybrid.</summary>
        private static string UltimateName(UltimateDefinition ultimate)
        {
            if (!ultimate.IsHybrid) return ultimate.DisplayName;
            int dash = ultimate.DisplayName.IndexOf(" - ", System.StringComparison.Ordinal);
            return dash < 0 ? ultimate.DisplayName : ultimate.DisplayName.Substring(dash + 3);
        }

        private static string HybridName(UltimateDefinition ultimate)
        {
            int dash = ultimate.DisplayName.IndexOf(" - ", System.StringComparison.Ordinal);
            return dash < 0 ? "Hybrid" : ultimate.DisplayName.Substring(0, dash);
        }

        private static Sprite UltimateIcon(UltimateDefinition ultimate)
        {
            if (ultimate == null) return null;
            return ClassIcons.ForOrNull(ultimate.Id) ?? ClassCrests.For(ultimate.Archetype);
        }

        /// <summary>A rect under a parent's top edge, stretched across it.</summary>
        private static RectTransform Band(Transform parent, string name, float top, float height, float left, float right)
        {
            RectTransform rect = ClassesStyle.Rect(parent, name, new Vector2(0f, 1f), new Vector2(1f, 1f));
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>A fixed-size rect placed from a parent's top-left corner.</summary>
        private static RectTransform At(Transform parent, string name, float x, float y, float width, float height)
        {
            RectTransform rect = ClassesStyle.Rect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f));
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
            return rect;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            float x, float y, float width, float height, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
            bool bold = false)
        {
            TextMeshProUGUI label = ClassesStyle.Label(parent, name, text, size, color, new Vector2(0f, 1f), new Vector2(0f, 1f),
                align, bold);
            RectTransform rect = label.rectTransform;
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        /// <summary>A button's width from its caption, with the boards' 16 px side padding.</summary>
        private static float Width(Button button, string caption)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            return (label != null ? label.GetPreferredValues(caption, 9999f, 60f).x : caption.Length * 8f) + 34f;
        }

        private static Button RightButton(Transform parent, string name, string caption, ClassesStyle.ButtonKind kind,
            ref float fromRight, float top, float height, UnityAction onClick, float size)
        {
            Button button = ClassesStyle.Button(parent, name, caption, kind, Vector2.one, Vector2.one, onClick, size);
            float width = Width(button, caption);
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(-fromRight, -top);
            fromRight += width + 6f;
            return button;
        }

        private static Button LeftButton(Transform parent, string name, string caption, ClassesStyle.ButtonKind kind,
            ref float fromLeft, float top, float height, UnityAction onClick, float size)
        {
            Button button = ClassesStyle.Button(parent, name, caption, kind, new Vector2(0f, 1f), new Vector2(0f, 1f), onClick, size);
            float width = Width(button, caption);
            RectTransform rect = (RectTransform)button.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(fromLeft, -top);
            fromLeft += width + 8f;
            return button;
        }
    }

    /// <summary>A pool tile or a filled slot being dragged: a ghost of its icon follows the pointer (board BarsEditor).</summary>
    internal sealed class BarsDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal string NodeId;
        internal bool Ultimate;
        internal BarSide FromSide;

        /// <summary>The slot it was dragged out of, or 0 for the pool.</summary>
        internal int FromSlot;

        private BarsView _view;
        private Sprite _icon;
        private RectTransform _ghost;

        internal void Init(BarsView view, string nodeId, bool ultimate, BarSide side, int slot, Sprite icon)
        {
            _view = view;
            NodeId = nodeId;
            Ultimate = ultimate;
            FromSide = side;
            FromSlot = slot;
            _icon = icon;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _ghost = ClassesStyle.Fixed(canvas.rootCanvas.transform, "BarsDragGhost", new Vector2(0.5f, 0.5f), new Vector2(46f, 46f));
            ClassesStyle.Fill(_ghost, ClassesStyle.Box(8, ClassesStyle.Selected, ClassesStyle.Gold, 2), Color.white);
            RectTransform glyph = ClassesStyle.Fixed(_ghost, "icon", new Vector2(0.5f, 0.5f), new Vector2(30f, 30f));
            Image image = ClassesStyle.Fill(glyph, _icon, ClassesStyle.Available);
            image.preserveAspect = true;
            image.enabled = _icon != null;
            CanvasGroup group = _ghost.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            _ghost.SetAsLastSibling();
            Follow(eventData);
            _view?.BeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData) => Follow(eventData);

        public void OnEndDrag(PointerEventData eventData)
        {
            DropGhost();
            _view?.EndDrag(this);
        }

        private void OnDisable() => DropGhost();

        private void Follow(PointerEventData eventData)
        {
            if (_ghost == null || !(_ghost.parent is RectTransform parent)) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera,
                    out Vector2 local))
                _ghost.localPosition = local;
        }

        private void DropGhost()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
        }
    }

    /// <summary>A bar slot tile: takes drops from the pool or another slot, and clears on right-click.</summary>
    internal sealed class BarsDropTarget : MonoBehaviour, IDropHandler, IPointerClickHandler
    {
        private BarsView _view;
        private BarSide _side;
        private int _slot;

        internal void Init(BarsView view, BarSide side, int slot)
        {
            _view = view;
            _side = side;
            _slot = slot;
        }

        public void OnDrop(PointerEventData eventData) => _view?.DropOn(_side, _slot);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) _view?.ClearSlot(_side, _slot);
        }
    }
}
