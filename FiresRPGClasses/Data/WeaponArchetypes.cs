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
        // Druid / Necromancer (Fire 10-07): one more allowed archetype on their rows; the row's first stays the default.
        private static readonly ArchetypeClass[] MonkBerserkerDruid =
            { ArchetypeClass.Monk, ArchetypeClass.Berserker, ArchetypeClass.Druid };
        private static readonly ArchetypeClass[] RogueMonkNecromancer =
            { ArchetypeClass.Rogue, ArchetypeClass.Monk, ArchetypeClass.Necromancer };
        private static readonly ArchetypeClass[] MageHealerDruid =
            { ArchetypeClass.Mage, ArchetypeClass.Healer, ArchetypeClass.Druid };
        private static readonly ArchetypeClass[] HealerMageNecromancer =
            { ArchetypeClass.Healer, ArchetypeClass.Mage, ArchetypeClass.Necromancer };

        public static IReadOnlyList<ArchetypeClass> Allowed(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            IReadOnlyList<ArchetypeClass> byWeapon = AllowedByWeapon(weapon, offHand);
            // The Werewolf (Tools\WEREWOLF_CLASS_DESIGN.md §2): no weapon decides it, the local player's race does. With the race it is
            // allowed on any bar (last, so it is never a bar's default); without it the class is greyed out like a missing weapon.
            // The Vampire (Tools\VAMPIRE_CLASS_DESIGN.md §2) the same way, by the vampire race (rpg_race = Vampire).
            int races = (WerewolfRace.Local ? 1 : 0) | (VampireRace.Local ? 2 : 0);
            return races == 0 ? byWeapon : WithRaces(byWeapon, races);
        }

        // One appended copy per shared array and race mix, built once: the HUD asks for sixteen slots a frame.
        private static readonly Dictionary<IReadOnlyList<ArchetypeClass>, ArchetypeClass[][]> RaceSets =
            new Dictionary<IReadOnlyList<ArchetypeClass>, ArchetypeClass[][]>();

        private static IReadOnlyList<ArchetypeClass> WithRaces(IReadOnlyList<ArchetypeClass> set, int races)
        {
            if (!RaceSets.TryGetValue(set, out ArchetypeClass[][] byMix)) RaceSets[set] = byMix = new ArchetypeClass[4][];
            if (byMix[races] != null) return byMix[races];
            var list = new List<ArchetypeClass>(set);
            if ((races & 1) != 0) list.Add(WerewolfClass.Archetype);
            if ((races & 2) != 0) list.Add(VampireClass.Archetype);
            byMix[races] = list.ToArray();
            return byMix[races];
        }

        private static IReadOnlyList<ArchetypeClass> AllowedByWeapon(ItemDrop.ItemData weapon, ItemDrop.ItemData offHand)
        {
            if (weapon == null) return MonkBerserker;

            bool shield = offHand != null
                && offHand.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            bool twoHanded = weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon
                || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
            return Allowed(weapon.m_shared.m_skillType, twoHanded, shield);
        }

        /// <summary>The same rule from a weapon's skill type, whether it is two-handed and whether a shield is in the off hand.</summary>
        public static IReadOnlyList<ArchetypeClass> Allowed(Skills.SkillType skill, bool twoHanded, bool shield)
        {
            switch (skill)
            {
                case Skills.SkillType.Swords:
                    if (twoHanded) return BerserkerPaladin;
                    return shield ? PaladinTank : RogueBerserker;

                case Skills.SkillType.Axes:
                    if (twoHanded) return BerserkerTank;
                    return shield ? TankPaladinBerserker : BerserkerRogue;

                case Skills.SkillType.Clubs:
                    if (twoHanded) return BerserkerMonk;
                    return shield ? PaladinTankHealer : MonkBerserkerDruid;

                case Skills.SkillType.Spears:
                    return shield ? TankRanger : RangerRogue;

                case Skills.SkillType.Knives:
                    return RogueMonkNecromancer;

                case Skills.SkillType.Polearms:
                    return TankBerserkerMonk;

                case Skills.SkillType.Bows:
                case Skills.SkillType.Crossbows:
                    return RangerRogue;

                case Skills.SkillType.ElementalMagic:
                    return MageHealerDruid;

                case Skills.SkillType.BloodMagic:
                    return HealerMageNecromancer;

                case Skills.SkillType.Unarmed:
                    return MonkBerserker;

                default:
                    return NoArchetype;
            }
        }

        /// <summary>Every weapon setup a bar can hold, in the order the UI lists them (doc 14's table).</summary>
        private static readonly (string Label, Skills.SkillType Skill, bool TwoHanded, bool Shield)[] Setups =
        {
            ("Sword and shield", Skills.SkillType.Swords, false, true),
            ("Sword", Skills.SkillType.Swords, false, false),
            ("Two-handed sword", Skills.SkillType.Swords, true, false),
            ("Axe and shield", Skills.SkillType.Axes, false, true),
            ("Axe", Skills.SkillType.Axes, false, false),
            ("Battleaxe", Skills.SkillType.Axes, true, false),
            ("Club or mace and shield", Skills.SkillType.Clubs, false, true),
            ("Club or mace", Skills.SkillType.Clubs, false, false),
            ("Sledge", Skills.SkillType.Clubs, true, false),
            ("Spear and shield", Skills.SkillType.Spears, false, true),
            ("Spear", Skills.SkillType.Spears, false, false),
            ("Knives", Skills.SkillType.Knives, false, false),
            ("Atgeir", Skills.SkillType.Polearms, true, false),
            ("Bow", Skills.SkillType.Bows, true, false),
            ("Crossbow", Skills.SkillType.Crossbows, true, false),
            ("Elemental staff", Skills.SkillType.ElementalMagic, true, false),
            ("Blood-magic staff", Skills.SkillType.BloodMagic, true, false),
            ("Unarmed or fists", Skills.SkillType.Unarmed, false, false),
        };

        /// <summary>
        /// The other direction: every weapon setup that can drive this archetype ("Sword and shield", "Battleaxe"...), read
        /// from the same rule so the labels can never disagree with what a bar accepts. Fire 10-01: say plainly which
        /// weapons each class takes.
        /// </summary>
        public static IReadOnlyList<string> WeaponsFor(ArchetypeClass archetype)
        {
            var list = new List<string>();
            foreach (var setup in Setups)
                if (Allowed(setup.Skill, setup.TwoHanded, setup.Shield).Contains(archetype)) list.Add(setup.Label);
            return list;
        }

        /// <summary>"Sword and shield, Axe and shield, Club or mace and shield" - one line for a label.</summary>
        public static string WeaponsLine(ArchetypeClass archetype) => archetype == VampireClass.Archetype
            ? "Any weapon or none: needs the Vampire race"
            : archetype == WerewolfClass.Archetype
            ? "Any weapon or none: needs the Werewolf race"
            : string.Join(", ", WeaponsFor(archetype).ToArray());

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
                : string.Join(" / ", allowed.Select(ClassNames.Of).ToArray());
        }
    }
}
