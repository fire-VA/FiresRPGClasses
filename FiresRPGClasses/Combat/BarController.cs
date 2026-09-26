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
        LevelLocked
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

            data.ActiveBar = ClassPlayerData.Other(data.ActiveBar);
            EquipBar(player, data.Active);
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

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            ItemDrop.ItemData offHand = player.LeftItem;
            BarLayout bar = data.Active;

            string weaponPrefab = weapon?.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
            string offHandPrefab = offHand?.m_dropPrefab != null ? offHand.m_dropPrefab.name : "";
            if (bar.WeaponPrefab == weaponPrefab && bar.OffHandPrefab == offHandPrefab) return;

            bar.WeaponPrefab = weaponPrefab;
            bar.OffHandPrefab = offHandPrefab;

            if (!WeaponArchetypes.Allows(weapon, offHand, bar.Archetype))
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
            ItemDrop.ItemData weapon = FindInInventory(bar.WeaponPrefab);
            if (weapon != null && !player.IsItemEquiped(weapon)) player.EquipItem(weapon);

            ItemDrop.ItemData offHand = FindInInventory(bar.OffHandPrefab);
            if (offHand != null && !player.IsItemEquiped(offHand)) player.EquipItem(offHand);
            else if (offHand == null && player.LeftItem != null) player.UnequipItem(player.LeftItem);
        }
    }
}
