using System.Collections.Generic;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.State
{
    public enum BarSide
    {
        Front,
        Back
    }

    /// <summary>
    /// Doc 14 section 1: slot 1 holds the bar's weapon, 2-7 six actives, 8 one ultimate. Item names are prefab
    /// names so a bar survives the item instance being re-created by the inventory.
    /// </summary>
    public class BarLayout
    {
        public const int SkillSlotCount = 6;
        public const int FirstSkillSlot = 2;
        public const int LastSkillSlot = 7;
        public const int WeaponSlot = 1;
        public const int UltimateSlot = 8;
        public const int SlotCount = 8;

        public string WeaponPrefab = "";
        public string OffHandPrefab = "";
        public ArchetypeClass Archetype = ArchetypeClass.None;
        public string UltimateId = "";
        public int UltimateMorph;

        public readonly List<string> SkillNodeIds = NewSkillSlots();

        public static List<string> NewSkillSlots()
        {
            List<string> slots = new List<string>(SkillSlotCount);
            for (int i = 0; i < SkillSlotCount; i++) slots.Add("");
            return slots;
        }

        public void EnsureSlotCount()
        {
            while (SkillNodeIds.Count < SkillSlotCount) SkillNodeIds.Add("");
            while (SkillNodeIds.Count > SkillSlotCount) SkillNodeIds.RemoveAt(SkillNodeIds.Count - 1);
        }

        public static bool IsSkillSlot(int slot) => slot >= FirstSkillSlot && slot <= LastSkillSlot;

        public string SkillAt(int slot)
        {
            if (!IsSkillSlot(slot)) return "";
            EnsureSlotCount();
            return SkillNodeIds[slot - FirstSkillSlot];
        }

        public void SetSkill(int slot, string nodeId)
        {
            if (!IsSkillSlot(slot)) return;
            EnsureSlotCount();
            SkillNodeIds[slot - FirstSkillSlot] = nodeId ?? "";
        }

        public bool Holds(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return false;
            if (UltimateId == nodeId) return true;
            EnsureSlotCount();
            return SkillNodeIds.Contains(nodeId);
        }

        public void ClearSkills()
        {
            EnsureSlotCount();
            for (int i = 0; i < SkillNodeIds.Count; i++) SkillNodeIds[i] = "";
        }

        public BarLayout Clone()
        {
            BarLayout copy = new BarLayout
            {
                WeaponPrefab = WeaponPrefab,
                OffHandPrefab = OffHandPrefab,
                Archetype = Archetype,
                UltimateId = UltimateId,
                UltimateMorph = UltimateMorph,
            };
            EnsureSlotCount();
            for (int i = 0; i < SkillNodeIds.Count; i++) copy.SkillNodeIds[i] = SkillNodeIds[i];
            return copy;
        }
    }

    public class SavedBar
    {
        public string Name = "";
        public BarLayout Layout = new BarLayout();
    }

    /// <summary>Doc 16: an ultimate's own level and the XP toward the next, plus which morph each bar slots.</summary>
    public class UltimateProgress
    {
        public int Level = 1;
        public long Xp;
    }
}
