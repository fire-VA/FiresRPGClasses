using System;
using System.Collections.Generic;
using System.Linq;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Npc.Archetypes;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    public enum SlotAvailability
    {
        Empty,
        Ready,
        OnCooldown,
        NotLearned,
        WrongArchetype,
        BarUnusable,
        LevelLocked,

        /// <summary>
        /// The active bar's weapon comes from VAInventory's bar slot, but the hands hold something else (a weapon
        /// equipped from the hotbar): a temporary swap that dims the bar until the player switches bars or re-equips.
        /// </summary>
        WrongWeapon
    }

    /// <summary>
    /// Doc 14 sections 1 and 2: which bar is active, swapping to the other bar's weapon, and why a slot is
    /// greyed. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class BarController
    {
        public const float SwapLockoutSeconds = 0.5f;

        private static float _swapReadyAt;

        public static event Action<BarSide> BarsChanged;

        /// <summary>0.1.142: after a front / back swap (from, to), local player. [classes-ui]'s HUD cue.</summary>
        public static event Action<BarSide, BarSide> BarSwapped;

        /// <summary>0.1.142: after per-weapon-type memory saved / restored a layout (side, old type key, new type key, restored).</summary>
        public static event Action<BarSide, string, string, bool> BarWeaponTypeChanged;

        internal static void RaiseWeaponTypeChanged(BarSide side, string oldType, string newType, bool restored) => BarWeaponTypeChanged?.Invoke(side, oldType, newType, restored);

        /// <summary>0.1.142: the bar's current weapon type key ("BloodMagic.2H", "Swords.1H+Shield", "Unarmed").</summary>
        public static string WeaponTypeKey(BarSide side)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return data == null ? "" : HasBarSlots ? BarMemory.TypeKey(BarWeapon(side), BarOffHand(side)) : BarMemory.TypeKey(data.Bar(side).WeaponPrefab, data.Bar(side).OffHandPrefab);
        }

        /// <summary>0.1.142: true when the bar's class can drive that skill (the UI hides, not greys, the rest).</summary>
        public static bool CanDrive(BarSide side, string nodeId) => BarMemory.CanDrive(ClassPlayerStore.Local?.Bar(side), nodeId);

        public static bool SwapLocked => Time.time < _swapReadyAt;

        public static float SwapLockRemaining => Mathf.Max(0f, _swapReadyAt - Time.time);

        public static BarSide ActiveSide
        {
            get
            {
                ClassPlayerData data = ClassPlayerStore.Local;
                return data?.ActiveBar ?? BarSide.Front;
            }
        }

        public static ArchetypeClass ActiveArchetype
        {
            get
            {
                ClassPlayerData data = ClassPlayerStore.Local;
                return data?.Active.Archetype ?? ArchetypeClass.None;
            }
        }

        /// <summary>Doc 14: a bar whose weapon has left the inventory cannot fire until one is back in slot 1.</summary>
        public static bool IsUsable(BarLayout bar)
        {
            if (bar == null) return false;
            if (string.IsNullOrEmpty(bar.WeaponPrefab)) return bar.Archetype != ArchetypeClass.None;
            return FindInInventory(bar.WeaponPrefab) != null;
        }

        // ---- VAInventory's bar slots (Fire, 2026-09-30) ------------------------------------------------------
        // VAInventory (a soft dependency, reached by reflection like ClassesInventoryTab) adds four equipment slots:
        // Front bar, Front offhand, Back bar, Back offhand. When they exist, each bar takes its weapon and off hand
        // from its slots instead of from whatever is equipped (Fire's popup, 20:2x):
        //   - switching bars equips that bar's weapon and off hand;
        //   - the hotbar cannot change a bar: a weapon equipped some other way is a temporary swap that dims the
        //     active bar (SlotAvailability.WrongWeapon) until the player switches bars or re-equips its weapon;
        //   - an empty weapon slot is an unarmed bar.
        // BarLayout.WeaponPrefab / OffHandPrefab mirror the slots (PollBarSlots), so the archetype rules, the
        // saved bars and the UI keep reading them as before.

        private const string VaSlotsTypeName = "VerdantsAscent.Slots";
        private const string FrontWeaponSlot = "FrontWeapon";
        private const string FrontOffhandSlot = "FrontOffhand";
        private const string BackWeaponSlot = "BackWeapon";
        private const string BackOffhandSlot = "BackOffhand";
        private const float VaRetrySeconds = 10f;
        private const float PollSeconds = 0.2f;

        private static bool _vaResolved;
        private static float _vaRetryAt;
        private static System.Reflection.PropertyInfo _vaActive;
        private static System.Reflection.MethodInfo _vaGetItem;
        private static System.Reflection.MethodInfo _vaPut;
        private static int _slotsFrame = -1;
        private static bool _hasBarSlots;
        private static float _pollAt;
        private static BarSide _lastHoldingSide;
        private static bool _lastHolding = true;

        /// <summary>VAInventory's bar slots exist; false keeps the old behaviour (a bar follows what is equipped).</summary>
        public static bool HasBarSlots
        {
            get
            {
                if (_slotsFrame == Time.frameCount) return _hasBarSlots;
                _slotsFrame = Time.frameCount;
                // 0.1.130 (Fire's profiler: ClassesHud 183 MB / 8.6 min): a bound getter, not PropertyInfo.GetValue (boxed bool per frame).
                if (!ResolveVa() || _vaActive == null) _hasBarSlots = false;
                else if (_vaActiveGetter != null)
                {
                    try { _hasBarSlots = _vaActiveGetter(); }
                    catch (Exception ex) { _hasBarSlots = false; Debug.LogWarning($"[FiresRPGClasses] VAInventory bar slot call failed: {ex.GetBaseException().Message}"); }
                }
                else _hasBarSlots = Invoke(() => (bool)_vaActive.GetValue(null));
                return _hasBarSlots;
            }
        }

        /// <summary>The weapon in a bar's VAInventory slot, or null (an empty slot, which is an unarmed bar).</summary>
        public static ItemDrop.ItemData BarWeapon(BarSide side) =>
            HasBarSlots ? SlotItem(side == BarSide.Front ? FrontWeaponSlot : BackWeaponSlot) : null;

        /// <summary>The off hand in a bar's VAInventory slot, or null.</summary>
        public static ItemDrop.ItemData BarOffHand(BarSide side) =>
            HasBarSlots ? SlotItem(side == BarSide.Front ? FrontOffhandSlot : BackOffhandSlot) : null;

        /// <summary>
        /// The hands hold this bar's slot pair: its weapon (or nothing, for an unarmed bar) and its off hand where the
        /// weapon leaves the off hand free. False is a temporary swap. Always true without the bar slots.
        /// </summary>
        public static bool HoldingBarWeapon(BarSide side)
        {
            Player player = Player.m_localPlayer;
            if (!HasBarSlots || player == null) return true;
            ItemDrop.ItemData weapon = BarWeapon(side);
            ItemDrop.ItemData offHand = BarOffHand(side);
            bool weaponHeld = weapon != null
                ? player.IsItemEquiped(weapon)
                : player.RightItem == null && (player.LeftItem == null || player.LeftItem == offHand || IsOffhandType(player.LeftItem));
            bool offHandHeld = offHand == null || !OffhandUsableWith(weapon) || player.IsItemEquiped(offHand);
            return weaponHeld && offHandHeld;
        }

        /// <summary>
        /// Puts an item from the player's inventory into a bar's VAInventory slot: a shield (or a torch, when the bar
        /// already has a weapon) goes to the off hand, a weapon to the weapon slot. For the bot bodies and the tests;
        /// the player does it in VAInventory's equipment panel. Equips it when the bar is active.
        /// </summary>
        public static bool TrySetBarItem(BarSide side, ItemDrop.ItemData item)
        {
            if (!HasBarSlots || item?.m_shared == null || _vaPut == null) return false;
            bool offHand = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield
                || (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch && BarWeapon(side) != null && BarWeapon(side) != item);
            string slotId = side == BarSide.Front ? (offHand ? FrontOffhandSlot : FrontWeaponSlot) : (offHand ? BackOffhandSlot : BackWeaponSlot);
            ForgetSlotItems();
            if (!Invoke(() => (bool)_vaPut.Invoke(null, new object[] { slotId, item }))) return false;
            _slotsFrame = -1;
            _pollAt = 0f;
            PollBarSlots();
            return true;
        }

        /// <summary>For the bot bodies: the weapon and off hand they hold become the active bar's slot pair.</summary>
        public static bool AssignHeldToActiveBar()
        {
            Player player = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (!HasBarSlots || player == null || data == null) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon == player.m_unarmedWeapon?.m_itemData) weapon = null;
            bool done = weapon == null || TrySetBarItem(data.ActiveBar, weapon);
            ItemDrop.ItemData left = player.LeftItem;
            if (left != null && left != weapon && IsOffhandType(left)) done &= TrySetBarItem(data.ActiveBar, left);
            return done;
        }

        /// <summary>
        /// Keeps each bar's recorded weapon and off hand in step with its VAInventory slots, re-checks its archetype,
        /// equips the active bar's pair when its slots change, and reports a temporary swap starting or ending.
        /// ClassCombatDriver calls it every frame; it does the work five times a second.
        /// </summary>
        public static void PollBarSlots()
        {
            if (Time.time < _pollAt) return;
            _pollAt = Time.time + PollSeconds;
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (data == null || player == null || !HasBarSlots) return;

            foreach (BarSide side in new[] { BarSide.Front, BarSide.Back })
            {
                ItemDrop.ItemData weapon = BarWeapon(side);
                ItemDrop.ItemData offHand = BarOffHand(side);
                string weaponPrefab = weapon?.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
                string offHandPrefab = offHand?.m_dropPrefab != null ? offHand.m_dropPrefab.name : "";
                BarLayout bar = data.Bar(side);
                if (bar.WeaponPrefab == weaponPrefab && bar.OffHandPrefab == offHandPrefab) continue;

                // 0.1.142: per-weapon-type memory first (it reads the OLD weapon still on the bar), then the bar takes the new one
                bool remembered = BarMemory.OnWeaponChange(data, side, weapon, offHand, FallbackArchetype(weapon, offHand, data.Bar(ClassPlayerData.Other(side)).Archetype));
                bar.WeaponPrefab = weaponPrefab;
                bar.OffHandPrefab = offHandPrefab;
                if (!remembered && !WeaponArchetypes.Allows(weapon, offHand, bar.Archetype))
                    bar.Archetype = FallbackArchetype(weapon, offHand, data.Bar(ClassPlayerData.Other(side)).Archetype);
                if (side == data.ActiveBar && !player.IsDead() && !player.InAttack()) EquipBar(player, bar);
                ClassPlayerStore.SaveLocal();
                BarsChanged?.Invoke(side);
            }

            bool holding = HoldingBarWeapon(data.ActiveBar);
            if (holding != _lastHolding || data.ActiveBar != _lastHoldingSide)
            {
                _lastHolding = holding;
                _lastHoldingSide = data.ActiveBar;
                BarsChanged?.Invoke(data.ActiveBar);
            }
        }

        // 0.1.130 (Fire's profiler: ClassesHud 183 MB / 8.6 min): this was a closure + Func + object[] + MethodInfo.Invoke per call, and the
        // HUD makes up to ~18 calls a frame (weapon art, HoldingBarWeapon per lit skill slot). Now a bound delegate (no reflection, no
        // allocation), and each of the four slots is read at most once a frame. A put through TryPutInClassBarSlot drops the frame cache.
        private static Func<string, ItemDrop.ItemData> _vaGetItemFn;
        private static Func<bool> _vaActiveGetter;
        private static readonly string[] SlotIds = { FrontWeaponSlot, FrontOffhandSlot, BackWeaponSlot, BackOffhandSlot };
        private static readonly ItemDrop.ItemData[] SlotItemCache = new ItemDrop.ItemData[4];
        private static readonly int[] SlotItemFrame = { -1, -1, -1, -1 };
        private static readonly object[] SlotItemArgs = new object[1];

        private static void ForgetSlotItems()
        {
            for (int i = 0; i < SlotItemFrame.Length; i++) SlotItemFrame[i] = -1;
        }

        private static ItemDrop.ItemData SlotItem(string slotId)
        {
            if (_vaGetItem == null) return null;
            int index = Array.IndexOf(SlotIds, slotId);
            int frame = Time.frameCount;
            if (index >= 0 && SlotItemFrame[index] == frame) return SlotItemCache[index];
            ItemDrop.ItemData item;
            try
            {
                if (_vaGetItemFn != null) item = _vaGetItemFn(slotId);
                else { SlotItemArgs[0] = slotId; item = _vaGetItem.Invoke(null, SlotItemArgs) as ItemDrop.ItemData; SlotItemArgs[0] = null; }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGClasses] VAInventory bar slot call failed: {ex.GetBaseException().Message}");
                item = null;
            }
            if (index >= 0) { SlotItemCache[index] = item; SlotItemFrame[index] = frame; }
            return item;
        }

        private static void BindVaDelegates()
        {
            try
            {
                System.Reflection.MethodInfo getter = _vaActive?.GetGetMethod();
                if (getter != null && getter.ReturnType == typeof(bool))
                    _vaActiveGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter, false);
                if (_vaGetItem != null && _vaGetItem.ReturnType == typeof(ItemDrop.ItemData))
                    _vaGetItemFn = (Func<string, ItemDrop.ItemData>)Delegate.CreateDelegate(typeof(Func<string, ItemDrop.ItemData>), _vaGetItem, false);
            }
            catch (Exception ex)
            {
                _vaActiveGetter = null;
                _vaGetItemFn = null;
                Debug.LogWarning($"[FiresRPGClasses] VAInventory bar slot delegates unavailable, using reflection: {ex.GetBaseException().Message}");
            }
            FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] VAInventory bar slots: active getter {(_vaActiveGetter != null ? "bound" : "reflection")}, item getter {(_vaGetItemFn != null ? "bound" : "reflection")}.");
        }

        private static bool ResolveVa()
        {
            if (_vaResolved) return true;
            if (Time.realtimeSinceStartup < _vaRetryAt) return false;
            _vaRetryAt = Time.realtimeSinceStartup + VaRetrySeconds;
            Type slots = HarmonyLib.AccessTools.TypeByName(VaSlotsTypeName);
            if (slots == null) return false;
            const System.Reflection.BindingFlags Public = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            _vaActive = slots.GetProperty("ClassBarSlotsActive", Public);
            _vaGetItem = slots.GetMethod("GetClassBarItem", Public, null, new[] { typeof(string) }, null);
            _vaPut = slots.GetMethod("TryPutInClassBarSlot", Public, null, new[] { typeof(string), typeof(ItemDrop.ItemData) }, null);
            _vaResolved = true;
            BindVaDelegates();
            if (_vaActive == null || _vaGetItem == null)
                Debug.LogWarning("[FiresRPGClasses] VAInventory is loaded but has no class bar slot API (needs VAInventory 1.0.47+); bars follow the equipped weapon.");
            return true;
        }

        private static T Invoke<T>(Func<T> call)
        {
            try { return call(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGClasses] VAInventory bar slot call failed: {ex.GetBaseException().Message}");
                return default;
            }
        }

        private static bool IsOffhandType(ItemDrop.ItemData item) =>
            item?.m_shared != null && (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield
                                       || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch);

        // Vanilla's hand rules: an off hand sits next to a one-handed weapon, a torch, or nothing; a two-handed
        // weapon or a bow needs both hands.
        private static bool OffhandUsableWith(ItemDrop.ItemData weapon)
        {
            if (weapon?.m_shared == null) return true;
            switch (weapon.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Torch:
                    return true;
                default:
                    return false;
            }
        }

        private static BarSide SideOf(BarLayout bar)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return data != null && ReferenceEquals(bar, data.Back) ? BarSide.Back : BarSide.Front;
        }

        /// <summary>
        /// Whether a swap would succeed, without attempting it or producing a refusal. Lets a key that
        /// would be refused fall through to whatever else wants it - vanilla's hotbar, in slot 1's case.
        /// </summary>
        public static bool CanSwap()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (data == null || player == null) return false;
            if (SwapLocked || player.InAttack() || player.IsDead()) return false;
            return IsUsable(data.Inactive);
        }

        /// <summary>
        /// Puts the active bar's slot pair into the hands when they hold anything else, or nothing (sheathed). False, changing
        /// nothing, when the pair is already in hand, the player is mid-attack or dead, or there are no bar slots.
        /// </summary>
        public static bool DrawActiveBar()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (!HasBarSlots || data == null || player == null) return false;
            if (player.InAttack() || player.IsDead()) return false;
            if (HoldingBarWeapon(data.ActiveBar)) return false;
            EquipSlotPair(player, data.ActiveBar);
            return true;
        }

        public static bool TrySwap(out string refusal)
        {
            refusal = null;
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (data == null || player == null) { refusal = "no player"; return false; }
            if (SwapLocked) { refusal = "swapping"; return false; }
            if (player.InAttack() || player.IsDead()) { refusal = "mid-cast"; return false; }

            BarLayout target = data.Inactive;
            if (!IsUsable(target)) { refusal = "the other bar has no weapon"; return false; }

            BarSide from = data.ActiveBar;
            data.ActiveBar = ClassPlayerData.Other(data.ActiveBar);
            EquipBar(player, data.Active);
            // 0.1.142 (Fire: "a bar swap animation ... so we can tell when we're swapping"): vanilla's hide / show weapons trigger
            if (BarMemory.SwapAnimation) player.m_zanim?.SetTrigger("equip_hip");
            FiresRPGClassesPlugin.Log?.LogDebug($"[Bars] swap {from} -> {data.ActiveBar}: {BarMemory.TypeKey(BarWeapon(data.ActiveBar), BarOffHand(data.ActiveBar))}{(BarMemory.SwapAnimation ? ", anim equip_hip" : "")}");
            BarSwapped?.Invoke(from, data.ActiveBar);
            _swapReadyAt = Time.time + SwapLockoutSeconds;
            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(data.ActiveBar);
            return true;
        }

        /// <summary>Records what the player is actually holding onto the active bar, and picks a default archetype.</summary>
        public static void CaptureEquippedWeapon()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (data == null || player == null) return;
            // With VAInventory's bar slots the bar's weapon is the slot's, never what happens to be held (Fire).
            if (HasBarSlots) return;
            // Wolf Form unequips the bar's weapon and puts it back after: the bar keeps its record meanwhile.
            if (WerewolfKit.FormActive) return;

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            ItemDrop.ItemData offHand = player.LeftItem;
            BarLayout bar = data.Active;

            string weaponPrefab = weapon?.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
            string offHandPrefab = offHand?.m_dropPrefab != null ? offHand.m_dropPrefab.name : "";
            if (bar.WeaponPrefab == weaponPrefab && bar.OffHandPrefab == offHandPrefab) return;

            bool remembered = BarMemory.OnWeaponChange(data, data.ActiveBar, weapon, offHand, FallbackArchetype(weapon, offHand, data.Inactive.Archetype));   // 0.1.142
            bar.WeaponPrefab = weaponPrefab;
            bar.OffHandPrefab = offHandPrefab;

            if (!remembered && !WeaponArchetypes.Allows(weapon, offHand, bar.Archetype))
                bar.Archetype = FallbackArchetype(weapon, offHand, data.Inactive.Archetype);

            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(data.ActiveBar);
        }

        public static IReadOnlyList<ArchetypeClass> AllowedArchetypes(BarLayout bar)
        {
            ItemDrop.ItemData weapon = FindInInventory(bar?.WeaponPrefab);
            ItemDrop.ItemData offHand = FindInInventory(bar?.OffHandPrefab);
            return WeaponArchetypes.Allowed(weapon, offHand);
        }

        public static bool BarAllows(BarLayout bar, ArchetypeClass archetype) =>
            WeaponArchetypes.Allows(FindInInventory(bar?.WeaponPrefab),
                FindInInventory(bar?.OffHandPrefab), archetype);

        public static bool TryChooseArchetype(BarSide side, ArchetypeClass archetype)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return false;
            BarLayout bar = data.Bar(side);
            if (!BarAllows(bar, archetype)) return false;
            bar.Archetype = archetype;
            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(side);
            return true;
        }

        public static bool TrySlotSkill(BarSide side, int slot, string nodeId)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || !BarLayout.IsSkillSlot(slot)) return false;
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (!string.IsNullOrEmpty(nodeId) && (node == null || node.Slot != NodeSlot.Skill)) return false;
            if (node != null && data.Progress.TalentRank(node.Archetype, nodeId) <= 0) return false;

            BarLayout bar = data.Bar(side);
            for (int other = BarLayout.FirstSkillSlot; other <= BarLayout.LastSkillSlot; other++)
                if (other != slot && bar.SkillAt(other) == nodeId) bar.SetSkill(other, "");
            bar.SetSkill(slot, nodeId);
            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(side);
            return true;
        }

        public static bool TrySlotUltimate(BarSide side, string ultimateId, int morphIndex)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return false;
            if (data.Progress.Level < UltimateCatalog.SlotUnlockLevel) return false;

            BarLayout bar = data.Bar(side);
            if (string.IsNullOrEmpty(ultimateId))
            {
                bar.UltimateId = "";
                bar.UltimateMorph = 0;
            }
            else
            {
                UltimateDefinition definition = UltimateCatalog.Get(ultimateId);
                if (definition == null) return false;
                if (data.Progress.Level < definition.UnlockLevel) return false;
                bar.UltimateId = ultimateId;
                bar.UltimateMorph = UltimateCatalog.MorphsUnlocked(data.UltimateLevel(ultimateId))
                    ? Mathf.Clamp(morphIndex, 0, Mathf.Max(0, definition.Morphs.Count - 1))
                    : 0;
            }
            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(side);
            return true;
        }

        public static bool TryLoadSavedBar(BarSide side, string name)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            SavedBar saved = data?.FindSavedBar(name);
            if (saved == null) return false;

            BarLayout restored = saved.Layout.Clone();
            if (HasBarSlots)
            {
                // The slots own the weapon now: a saved bar brings its archetype and skills, the bar keeps its slots'
                // weapon and off hand, and an archetype that weapon can't drive falls back as on any weapon change.
                BarLayout current = data.Bar(side);
                restored.WeaponPrefab = current.WeaponPrefab;
                restored.OffHandPrefab = current.OffHandPrefab;
                ItemDrop.ItemData weapon = BarWeapon(side);
                ItemDrop.ItemData offHand = BarOffHand(side);
                if (!WeaponArchetypes.Allows(weapon, offHand, restored.Archetype))
                    restored.Archetype = FallbackArchetype(weapon, offHand, data.Bar(ClassPlayerData.Other(side)).Archetype);
            }
            if (side == BarSide.Front) data.Front = restored; else data.Back = restored;
            if (side == data.ActiveBar) EquipBar(Player.m_localPlayer, restored);
            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(side);
            return true;
        }

        /// <summary>Doc 14: why a slot is lit, dim or dead. The UI and the cast path both read this.</summary>
        public static SlotAvailability Availability(BarSide side, int slot)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return SlotAvailability.BarUnusable;

            BarLayout bar = data.Bar(side);
            string nodeId = slot == BarLayout.UltimateSlot ? bar.UltimateId : bar.SkillAt(slot);
            if (string.IsNullOrEmpty(nodeId)) return SlotAvailability.Empty;
            if (!IsUsable(bar)) return SlotAvailability.BarUnusable;
            // Wolf Form empties the hands on purpose (the claws are the weapon): not a temporary swap.
            // 0.1.126 (R-1013BS): the race classes (Werewolf, Vampire) have no weapon - the race is their requirement - so a held weapon that
            // differs from the bar's recorded one is never "WrongWeapon" for them (Howl was refused with a lent Club in hand).
            bool raceClass = bar.Archetype == WerewolfClass.Archetype || bar.Archetype == VampireClass.Archetype;
            if (side == data.ActiveBar && !raceClass && !WerewolfKit.FormActive && !HoldingBarWeapon(side)) return SlotAvailability.WrongWeapon;

            if (slot == BarLayout.UltimateSlot)
            {
                UltimateDefinition ultimate = UltimateCatalog.Get(nodeId);
                if (ultimate == null) return SlotAvailability.NotLearned;
                if (data.Progress.Level < ultimate.UnlockLevel) return SlotAvailability.LevelLocked;
                if (!UltimateAllowedOnBar(bar, data, ultimate)) return SlotAvailability.WrongArchetype;
                return CooldownTracker.IsReady(nodeId) ? SlotAvailability.Ready : SlotAvailability.OnCooldown;
            }

            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null || data.Progress.TalentRank(node.Archetype, nodeId) <= 0)
                return SlotAvailability.NotLearned;
            if (!BarAllows(bar, node.Archetype)) return SlotAvailability.WrongArchetype;
            if (data.Progress.Level < node.MinLevel) return SlotAvailability.LevelLocked;
            return CooldownTracker.IsReady(nodeId) ? SlotAvailability.Ready : SlotAvailability.OnCooldown;
        }

        /// <summary>Doc 16: an archetype ultimate needs a bar whose weapon allows it; a hybrid needs the pair set.</summary>
        private static bool UltimateAllowedOnBar(BarLayout bar, ClassPlayerData data, UltimateDefinition ultimate)
        {
            if (!ultimate.IsHybrid) return BarAllows(bar, ultimate.Archetype);
            return data.HasHybridPair
                && ultimate.Archetype == data.Front.Archetype
                && ultimate.HybridPartner == data.Back.Archetype;
        }

        // The HUD asks Availability for all sixteen slots every frame, and each answer needs the bar's weapon
        // and off-hand. Without this the inventory would be walked dozens of times a frame.
        private static readonly Dictionary<string, ItemDrop.ItemData> LookupCache =
            new Dictionary<string, ItemDrop.ItemData>();

        private static int _cacheFrame = -1;

        public static ItemDrop.ItemData FindInInventory(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            if (_cacheFrame != Time.frameCount)
            {
                LookupCache.Clear();
                _cacheFrame = Time.frameCount;
            }
            if (LookupCache.TryGetValue(prefabName, out ItemDrop.ItemData cached)) return cached;

            ItemDrop.ItemData found = null;
            Inventory inventory = Player.m_localPlayer?.GetInventory();
            if (inventory != null)
            {
                foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                    if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName)
                    {
                        found = item;
                        break;
                    }
            }
            LookupCache[prefabName] = found;
            return found;
        }

        /// <summary>
        /// Slot 1: records which weapon a bar uses, from an item dragged onto it. The bar does not STORE the
        /// item - VAInventory's hotbars settled that pattern, the bar is a readout and the weapon stays in the
        /// inventory - so this only records the prefab. Assigning the ACTIVE bar also equips, because
        /// CaptureEquippedWeapon would otherwise stamp the held weapon straight back over the assignment the
        /// moment the inventory closes. Shields go to the off hand. Returns false for anything else, which is
        /// what leaves the drag with the item so it returns to where it came from.
        /// </summary>
        public static bool TryAssignWeapon(BarSide side, ItemDrop.ItemData item)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            Player player = Player.m_localPlayer;
            if (data == null || player == null || item?.m_shared == null) return false;
            // With VAInventory's bar slots, assigning means moving the item into the bar's slot.
            if (HasBarSlots) return TrySetBarItem(side, item);

            bool shield = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            if (!shield && !IsBarWeapon(item)) return false;

            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
            if (string.IsNullOrEmpty(prefab)) return false;

            BarLayout bar = data.Bar(side);
            if (shield) bar.OffHandPrefab = prefab;
            else bar.WeaponPrefab = prefab;

            ItemDrop.ItemData weapon = FindInInventory(bar.WeaponPrefab);
            ItemDrop.ItemData offHand = FindInInventory(bar.OffHandPrefab);
            if (!WeaponArchetypes.Allows(weapon, offHand, bar.Archetype))
                bar.Archetype = FallbackArchetype(weapon, offHand, data.Bar(ClassPlayerData.Other(side)).Archetype);

            if (side == data.ActiveBar) EquipBar(player, bar);

            ClassPlayerStore.SaveLocal();
            BarsChanged?.Invoke(side);
            return true;
        }

        /// <summary>
        /// Which archetype a bar falls back to when a weapon change invalidates its own. Doc 14's hybrid is the
        /// ORDERED PAIR of the two bars' archetypes and requires them to DIFFER, so taking the first allowed one
        /// blindly can land on the other bar's archetype and cost the player the hybrid without saying so -
        /// sword+shield allows only Paladin/Tank, so a Rogue bar opposite a Paladin bar collapses onto Paladin.
        /// Prefer the first allowed archetype that keeps the pair distinct; fall back only if every one collides.
        /// </summary>
        private static ArchetypeClass FallbackArchetype(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand,
            ArchetypeClass otherBar)
        {
            IReadOnlyList<ArchetypeClass> allowed = WeaponArchetypes.Allowed(weapon, offHand);
            for (int i = 0; i < allowed.Count; i++)
            {
                if (allowed[i] != otherBar) return allowed[i];
            }
            return WeaponArchetypes.Default(weapon, offHand);
        }

        private static bool IsBarWeapon(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Torch:
                    return true;
                default:
                    return false;
            }
        }

        private static void EquipBar(Player player, BarLayout bar)
        {
            if (player == null || bar == null) return;
            if (HasBarSlots)
            {
                EquipSlotPair(player, SideOf(bar));
                return;
            }
            ItemDrop.ItemData weapon = FindInInventory(bar.WeaponPrefab);
            if (weapon != null && !player.IsItemEquiped(weapon)) player.EquipItem(weapon);

            ItemDrop.ItemData offHand = FindInInventory(bar.OffHandPrefab);
            if (offHand != null && !player.IsItemEquiped(offHand)) player.EquipItem(offHand);
            else if (offHand == null && player.LeftItem != null) player.UnequipItem(player.LeftItem);
        }

        // The bar's slot pair into the hands. Anything else held is put away first, so vanilla's hand rules (a torch
        // takes the left hand only while it is free) see a clean start. An empty weapon slot leaves the bar unarmed.
        private static void EquipSlotPair(Player player, BarSide side)
        {
            ItemDrop.ItemData weapon = BarWeapon(side);
            ItemDrop.ItemData offHand = BarOffHand(side);
            if (player.RightItem != null && player.RightItem != weapon && player.RightItem != offHand) player.UnequipItem(player.RightItem, false);
            if (player.LeftItem != null && player.LeftItem != weapon && player.LeftItem != offHand) player.UnequipItem(player.LeftItem, false);
            if (weapon != null && !player.IsItemEquiped(weapon)) player.EquipItem(weapon);
            if (offHand != null && OffhandUsableWith(weapon) && !player.IsItemEquiped(offHand)) player.EquipItem(offHand);
        }
    }
}
