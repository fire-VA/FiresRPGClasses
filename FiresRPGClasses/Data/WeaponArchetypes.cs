using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// Doc 14 section 2: the weapon in bar slot 1 decides which archetypes that bar can drive. Resolved from the
    /// item's skill type, whether it is a two-handed weapon, and whether a shield sits in the off-hand.
    /// Every set is a shared static array - the HUD asks this for sixteen slots a frame.
    /// </summary>
    public static class WeaponArchetypes
    {
        private static readonly ArchetypeClass[] NoArchetype = new ArchetypeClass[0];

        private static readonly ArchetypeClass[] MonkBerserker =
            { ArchetypeClass.Monk, ArchetypeClass.Berserker };
        private static readonly ArchetypeClass[] PaladinTank =
            { ArchetypeClass.Paladin, ArchetypeClass.Tank };
        private static readonly ArchetypeClass[] RogueBerserker =
            { ArchetypeClass.Rogue, ArchetypeClass.Berserker };
        private static readonly ArchetypeClass[] BerserkerPaladin =
            { ArchetypeClass.Berserker, ArchetypeClass.Paladin };
        private static readonly ArchetypeClass[] TankPaladinBerserker =
            { ArchetypeClass.Tank, ArchetypeClass.Paladin, ArchetypeClass.Berserker };
        private static readonly ArchetypeClass[] BerserkerRogue =
            { ArchetypeClass.Berserker, ArchetypeClass.Rogue };
        private static readonly ArchetypeClass[] BerserkerTank =
            { ArchetypeClass.Berserker, ArchetypeClass.Tank };
        private static readonly ArchetypeClass[] PaladinTankHealer =
            { ArchetypeClass.Paladin, ArchetypeClass.Tank, ArchetypeClass.Healer };
        private static readonly ArchetypeClass[] BerserkerMonk =
            { ArchetypeClass.Berserker, ArchetypeClass.Monk };
        private static readonly ArchetypeClass[] TankRanger =
            { ArchetypeClass.Tank, ArchetypeClass.Ranger };
        private static readonly ArchetypeClass[] RangerRogue =
            { ArchetypeClass.Ranger, ArchetypeClass.Rogue };
        private static readonly ArchetypeClass[] RogueMonk =
            { ArchetypeClass.Rogue, ArchetypeClass.Monk };
        private static readonly ArchetypeClass[] TankBerserkerMonk =
            { ArchetypeClass.Tank, ArchetypeClass.Berserker, ArchetypeClass.Monk };
        private static readonly ArchetypeClass[] MageHealer =
            { ArchetypeClass.Mage, ArchetypeClass.Healer };
        private static readonly ArchetypeClass[] HealerMage =
            { ArchetypeClass.Healer, ArchetypeClass.Mage };

        public static IReadOnlyList<ArchetypeClass> Allowed(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            if (weapon == null) return MonkBerserker;

            bool shield = offHand != null
                && offHand.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            bool twoHanded = weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon
                || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;

            switch (weapon.m_shared.m_skillType)
            {
                case Skills.SkillType.Swords:
                    if (twoHanded) return BerserkerPaladin;
                    return shield ? PaladinTank : RogueBerserker;

                case Skills.SkillType.Axes:
                    if (twoHanded) return BerserkerTank;
                    return shield ? TankPaladinBerserker : BerserkerRogue;

                case Skills.SkillType.Clubs:
                    if (twoHanded) return BerserkerMonk;
                    return shield ? PaladinTankHealer : MonkBerserker;

                case Skills.SkillType.Spears:
                    return shield ? TankRanger : RangerRogue;

                case Skills.SkillType.Knives:
                    return RogueMonk;

                case Skills.SkillType.Polearms:
                    return TankBerserkerMonk;

                case Skills.SkillType.Bows:
                case Skills.SkillType.Crossbows:
                    return RangerRogue;

                case Skills.SkillType.ElementalMagic:
                    return MageHealer;

                case Skills.SkillType.BloodMagic:
                    return HealerMage;

                case Skills.SkillType.Unarmed:
                    return MonkBerserker;

                default:
                    return NoArchetype;
            }
        }

        public static ArchetypeClass Default(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            IReadOnlyList<ArchetypeClass> allowed = Allowed(weapon, offHand);
            return allowed.Count > 0 ? allowed[0] : ArchetypeClass.None;
        }

        public static bool Allows(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand, ArchetypeClass archetype)
        {
            if (archetype == ArchetypeClass.None) return false;
            IReadOnlyList<ArchetypeClass> allowed = Allowed(weapon, offHand);
            for (int i = 0; i < allowed.Count; i++) if (allowed[i] == archetype) return true;
            return false;
        }

        public static string Describe(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            IReadOnlyList<ArchetypeClass> allowed = Allowed(weapon, offHand);
            return allowed.Count == 0
                ? "no archetype"
                : string.Join(" / ", allowed.Select(a => a.ToString()).ToArray());
        }
    }
}
