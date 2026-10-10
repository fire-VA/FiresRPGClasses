using FiresCore.Npc.AI;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Core's CompanionBrain.ClassWeapon (Core 0.2.196, [generator]): which weapon kind drives the body's ACTIVE class bar, so
    /// Core's weapon pick keeps it (R72: a Tank was handed a bow at 2.2 m and a Mage a club, and every slot went
    /// WrongArchetype). Class bars exist only for this client's own player, so any other body answers None.
    /// </summary>
    internal static class ClassWeaponHook
    {
        /// <summary>Throws (MissingField / TypeLoad) on a Core older than 0.2.196; the caller catches it.</summary>
        internal static void Install() => CompanionBrain.ClassWeapon = Want;

        private static CompanionBrain.ClassWeaponWant Want(Humanoid body)
        {
            Player local = Player.m_localPlayer;
            if (body == null || local == null || body != local) return CompanionBrain.ClassWeaponWant.None;
            ClassPlayerData data = ClassPlayerStore.Local;
            BarLayout bar = data?.Bar(data.ActiveBar);
            if (bar == null || bar.Archetype == ArchetypeClass.None) return CompanionBrain.ClassWeaponWant.None;

            // The weapon the bar was built on decides; a bar without one falls back to its archetype.
            GameObject prefab = !string.IsNullOrEmpty(bar.WeaponPrefab) && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(bar.WeaponPrefab) : null;
            ItemDrop.ItemData.SharedData shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            if (shared != null) return IsRanged(shared) ? CompanionBrain.ClassWeaponWant.Ranged : CompanionBrain.ClassWeaponWant.Melee;
            return bar.Archetype == ArchetypeClass.Mage || bar.Archetype == ArchetypeClass.Ranger
                ? CompanionBrain.ClassWeaponWant.Ranged : CompanionBrain.ClassWeaponWant.Melee;
        }

        private static bool IsRanged(ItemDrop.ItemData.SharedData shared) =>
            shared.m_itemType == ItemDrop.ItemData.ItemType.Bow
            || shared.m_skillType == Skills.SkillType.Bows || shared.m_skillType == Skills.SkillType.Crossbows
            || shared.m_skillType == Skills.SkillType.ElementalMagic || shared.m_skillType == Skills.SkillType.BloodMagic;
    }
}
