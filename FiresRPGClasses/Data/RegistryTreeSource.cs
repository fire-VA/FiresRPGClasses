using System.Collections.Generic;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// A tree per archetype derived from Core's ability registry: one path, tiers from AbilityCategory, slots
    /// and targeting from CoreAbilityTargeting. Stands in until the yaml loader supplies the real three-path
    /// trees and the 65 new skills. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class RegistryTreeSource : IClassTreeSource
    {
        private const int DefaultMaxRank = 3;
        private const int SingleRank = 1;

        public string SourceName => "Core ability registry";

        public IEnumerable<ClassTree> Load()
        {
            foreach (ArchetypeClass archetype in ClassAbilityRegistry.Archetypes)
            {
                if (archetype == ArchetypeClass.None) continue;

                ClassTree tree = new ClassTree { Archetype = archetype };
                tree.Paths.Add(new ClassTreePath
                {
                    Id = ClassTreeNode.AllPaths,
                    DisplayName = archetype.ToString(),
                    Theme = "Every ability, until the tree data loads",
                });

                foreach (ClassAbility ability in ClassAbilityRegistry.GetAbilitiesForArchetype(archetype))
                    tree.Nodes.Add(ToNode(ability));

                yield return tree;
            }
        }

        private static ClassTreeNode ToNode(ClassAbility ability)
        {
            NodeSlot slot = SlotFor(ability);
            return new ClassTreeNode
            {
                Id = ability.Id,
                DisplayName = ability.DisplayName,
                Description = ability.Description,
                Archetype = ability.RequiredArchetype,
                PathId = ClassTreeNode.AllPaths,
                Tier = TierFor(ability.Category),
                MinLevel = slot == NodeSlot.Ultimate ? CoreAbilityTargeting.UltimateTwoLevel : ability.UnlockLevel,
                MaxRank = ability.Category == AbilityCategory.Master || ability.Category == AbilityCategory.Ultimate
                    ? SingleRank
                    : DefaultMaxRank,
                Slot = slot,
                Targeting = TargetingFor(ability, slot),
                IconColor = ability.IconColor,
            };
        }

        private static NodeSlot SlotFor(ClassAbility ability)
        {
            if (ability.IsPassive) return NodeSlot.Passive;
            if (CoreAbilityTargeting.IsEvasion(ability.Id)) return NodeSlot.Evasion;
            if (CoreAbilityTargeting.IsAutomatic(ability.Id)) return NodeSlot.Automatic;
            if (ability.Category == AbilityCategory.Ultimate) return NodeSlot.Ultimate;
            return NodeSlot.Skill;
        }

        private static TargetingSpec TargetingFor(ClassAbility ability, NodeSlot slot)
        {
            switch (slot)
            {
                case NodeSlot.Passive:
                case NodeSlot.Automatic:
                    return new TargetingSpec(TargetingMode.Automatic);
                case NodeSlot.Evasion:
                    return new TargetingSpec(TargetingMode.Dodge);
                default:
                    return CoreAbilityTargeting.For(ability.Id);
            }
        }

        private static NodeTier TierFor(AbilityCategory category)
        {
            switch (category)
            {
                case AbilityCategory.Starter: return NodeTier.Starter;
                case AbilityCategory.Basic: return NodeTier.Basic;
                case AbilityCategory.Advanced: return NodeTier.Advanced;
                case AbilityCategory.Expert: return NodeTier.Expert;
                case AbilityCategory.Master: return NodeTier.Master;
                default: return NodeTier.Ultimate;
            }
        }
    }
}
