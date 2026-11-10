using System.Collections.Generic;
using System.Linq;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.Combat
{
    public enum PassiveStatus
    {
        Active,
        NotEquipped,
        NotLearned,
        NoSlotsLeft,
        ConditionsUnmet
    }

    /// <summary>
    /// Doc 19's passive grid: the pool is every learned tree passive and automatic ability, equipped up to the
    /// slot count and always on whichever bar is active. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class PassiveLoadout
    {
        /// <summary>Every passive or automatic node in every tree, learned or not - the grid draws all of them.</summary>
        public static List<ClassTreeNode> Pool() =>
            ClassTrees.AllNodes.Where(node => node.IsPassive).OrderBy(node => node.Archetype)
                .ThenBy(node => node.MinLevel).ToList();

        public static int Slots()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return data?.Progress.PassiveSlots ?? 0;
        }

        public static int Equipped()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return data?.Progress.PassiveLoadout.Count ?? 0;
        }

        public static bool IsLearned(ClassPlayerData data, ClassTreeNode node) =>
            data != null && node != null && data.Progress.TalentRank(node.Archetype, node.Id) > 0;

        public static PassiveStatus Status(ClassTreeNode node)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || node == null) return PassiveStatus.NotLearned;
            if (!IsLearned(data, node)) return PassiveStatus.NotLearned;
            if (!data.Progress.PassiveLoadout.Contains(node.Id))
                return Equipped() >= Slots() ? PassiveStatus.NoSlotsLeft : PassiveStatus.NotEquipped;
            return ConditionsMet(node) ? PassiveStatus.Active : PassiveStatus.ConditionsUnmet;
        }

        /// <summary>Doc 19: a passive's own conditions still apply - Shield Block does nothing without a shield.</summary>
        private static readonly HashSet<string> NeedAShield = new HashSet<string>
        {
            "tank_block",
            "tank_parry_restore",
            "tank_iron_wall",
            "paladin_block",
            "paladin_heal_block",
        };

        /// <summary>0.1.214 (step 3b, a registered class body - the Studio god clone): a passive's status for THAT body, from its own record
        /// and its own gear. The local player's <see cref="Status(ClassTreeNode)"/> above is unchanged.</summary>
        public static PassiveStatus Status(ClassTreeNode node, ClassPlayerData data, Humanoid body)
        {
            if (data == null || node == null || body == null) return PassiveStatus.NotLearned;
            if (!IsLearned(data, node)) return PassiveStatus.NotLearned;
            if (!data.Progress.PassiveLoadout.Contains(node.Id))
                return data.Progress.PassiveLoadout.Count >= data.Progress.PassiveSlots ? PassiveStatus.NoSlotsLeft : PassiveStatus.NotEquipped;
            return ConditionsMet(node, body) ? PassiveStatus.Active : PassiveStatus.ConditionsUnmet;
        }

        public static bool ConditionsMet(ClassTreeNode node, Humanoid body)
        {
            if (body == null || node == null) return false;
            if (!NeedAShield.Contains(node.Id)) return true;
            var left = body.GetLeftItem();
            return left != null && left.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }

        public static bool ConditionsMet(ClassTreeNode node)
        {
            Player player = Player.m_localPlayer;
            if (player == null || node == null) return false;
            if (!NeedAShield.Contains(node.Id)) return true;
            return player.LeftItem != null
                && player.LeftItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }

        public static string ExplainStatus(PassiveStatus status)
        {
            switch (status)
            {
                case PassiveStatus.Active: return "active";
                case PassiveStatus.NotEquipped: return "learned, not equipped";
                case PassiveStatus.NotLearned: return "not learned";
                case PassiveStatus.NoSlotsLeft: return "no passive slots left";
                default: return "its conditions are not met";
            }
        }

        public static bool TryEquip(string nodeId)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (data == null || node == null || !node.IsPassive) return false;
            if (!IsLearned(data, node)) return false;
            if (!data.Progress.TryEquipPassive(nodeId)) return false;
            ClassPlayerStore.SaveLocal();
            return true;
        }

        public static bool TryUnequip(string nodeId)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return false;
            if (!data.Progress.UnequipPassive(nodeId)) return false;
            ClassPlayerStore.SaveLocal();
            return true;
        }

        /// <summary>Slots shrink when a rate drops; Core trims the loadout and this persists the result.</summary>
        public static int TrimToSlots()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return 0;
            int removed = data.Progress.TrimPassivesToSlots();
            if (removed > 0) ClassPlayerStore.SaveLocal();
            return removed;
        }
    }
}
