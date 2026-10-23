using System;
using BepInEx.Configuration;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Bars never keep showing skills the new weapon can't use: each bar remembers its layout (class, 6 skills, ultimate +
    /// morph) PER WEAPON TYPE. On a weapon change the current layout is saved under the old type and the new type's saved layout comes
    /// back; a type never seen starts with the class that weapon can drive and EMPTY skill slots. Either way, a skill the bar's class
    /// can't drive is cleared (logged). Stored in the class record (ClassPlayerData.WeaponBars, per character). The first time, the
    /// two bars simply become their weapons' entries (nothing to migrate beyond that).
    /// The memory is always on. Key "[1 - Bars] Bar swap animation" (default false,
    /// a new visual: vanilla's R-key draw trigger "equip_hip" on a front / back swap).
    /// </summary>
    public static class BarMemory
    {
        public const string Section = "1 - Bars";   // the existing bars section
        private static ConfigEntry<bool> _swapAnim;

        // The per-weapon-type bar memory is always on; it has no config key.
        public static bool Remember => true;
        public static bool SwapAnimation => _swapAnim != null && _swapAnim.Value;

        public static void Bind(ConfigFile cfg)
        {
            _swapAnim = cfg.Bind(Section, "Bar swap animation", false,
                "Play the draw-weapon animation when you swap front and back bars.");
        }

        /// <summary>"BloodMagic.2H", "Swords.1H+Shield", "Unarmed": the bar's weapon type (skill + hands + shield).</summary>
        public static string TypeKey(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            if (weapon?.m_shared == null) return offHand?.m_shared != null && offHand.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield ? "Unarmed+Shield" : "Unarmed";
            var t = weapon.m_shared.m_itemType;
            bool twoHanded = t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || t == ItemDrop.ItemData.ItemType.Bow;
            bool shield = offHand?.m_shared != null && offHand.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            return $"{weapon.m_shared.m_skillType}.{(twoHanded ? "2H" : "1H")}{(shield ? "+Shield" : "")}";
        }

        /// <summary>The type key of a prefab pair as the bar recorded them (ObjectDB's item data; the item may be gone from the bag).</summary>
        public static string TypeKey(string weaponPrefab, string offHandPrefab) => TypeKey(ItemOf(weaponPrefab), ItemOf(offHandPrefab));

        private static ItemDrop.ItemData ItemOf(string prefab) =>
            string.IsNullOrEmpty(prefab) || ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData;

        private static string Key(BarSide side, string type) => side + "|" + type;

        /// <summary>
        /// Called with the bar's OLD weapon still recorded, before it takes the new one. Saves the layout under the old type and loads
        /// the new type's (or an empty one for <paramref name="fallback"/>). False = memory off (the caller does what it always did).
        /// </summary>
        public static bool OnWeaponChange(ClassPlayerData data, BarSide side, ItemDrop.ItemData newWeapon, ItemDrop.ItemData newOffHand, ArchetypeClass fallback)
        {
            if (!Remember || data == null) return false;
            BarLayout bar = data.Bar(side);
            string oldType = TypeKey(bar.WeaponPrefab, bar.OffHandPrefab), newType = TypeKey(newWeapon, newOffHand);
            if (oldType == newType) { int cleared = ClearUndriven(bar); if (cleared > 0) Debug(side, $"same type {newType}: {cleared} skill(s) its class can't drive cleared"); return true; }

            data.WeaponBars[Key(side, oldType)] = bar.Clone();
            bool restored = data.WeaponBars.TryGetValue(Key(side, newType), out BarLayout saved);
            if (restored)
            {
                bar.Archetype = saved.Archetype;
                bar.UltimateId = saved.UltimateId; bar.UltimateMorph = saved.UltimateMorph;
                for (int i = 0; i < bar.SkillNodeIds.Count && i < saved.SkillNodeIds.Count; i++) bar.SkillNodeIds[i] = saved.SkillNodeIds[i];
                if (!WeaponArchetypes.Allows(newWeapon, newOffHand, bar.Archetype)) bar.Archetype = fallback;
            }
            else
            {
                bar.Archetype = fallback;
                for (int i = 0; i < bar.SkillNodeIds.Count; i++) bar.SkillNodeIds[i] = "";
                bar.UltimateId = ""; bar.UltimateMorph = 0;
            }
            int dropped = ClearUndriven(bar);
            Debug(side, $"weapon {oldType} -> {newType}: saved '{oldType}' ({Count(data.WeaponBars[Key(side, oldType)])} skill(s)), "
                + (restored ? $"restored '{newType}' ({bar.Archetype}, {Count(bar)} skill(s))" : $"no layout yet: {bar.Archetype}, empty")
                + (dropped > 0 ? $"; {dropped} skill(s) its class can't drive cleared" : ""));
            BarController.RaiseWeaponTypeChanged(side, oldType, newType, restored);
            return true;
        }

        /// <summary>Clears every slot (and the ultimate) whose node belongs to another class than the bar's.</summary>
        public static int ClearUndriven(BarLayout bar)
        {
            int n = 0;
            for (int i = 0; i < bar.SkillNodeIds.Count; i++)
            {
                string id = bar.SkillNodeIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                var node = ClassTrees.Node(id);
                if (node == null || node.Archetype != bar.Archetype) { bar.SkillNodeIds[i] = ""; n++; }
            }
            if (!string.IsNullOrEmpty(bar.UltimateId))
            {
                var u = ClassTrees.Node(bar.UltimateId);
                if (u != null && u.Archetype != bar.Archetype) { bar.UltimateId = ""; bar.UltimateMorph = 0; n++; }
            }
            return n;
        }

        public static bool CanDrive(BarLayout bar, string nodeId)
        {
            if (bar == null || string.IsNullOrEmpty(nodeId)) return false;
            var node = ClassTrees.Node(nodeId);
            return node != null && node.Archetype == bar.Archetype;
        }

        private static int Count(BarLayout b) { int n = 0; foreach (var s in b.SkillNodeIds) if (!string.IsNullOrEmpty(s)) n++; return n; }
        private static void Debug(BarSide side, string what) => FiresRPGClassesPlugin.Log?.LogDebug($"[Bars] {side.ToString().ToLowerInvariant()}: {what}");
    }
}
