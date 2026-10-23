using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.AI;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// What a player-driven bot may press right now, for Core's one brain (CompanionBrain.PickClassSkill): every
    /// Ready slot on the active bar as a Core SkillOption. FDT reads this by reflection, hands it to the brain and presses
    /// the returned slot's key through FiresKeys, so targeting, gates and cooldowns stay RPGClasses' own (the skills drill).
    /// Category / range / flags come from the node's targeting and its yaml values; the table below is the one place to
    /// tune how a skill is classed.
    /// </summary>
    public static class ClassSkillOptions
    {
        private const float MeleeReach = 3f;
        private const float RangedReach = 30f;
        private const float DefaultRadius = 5f;

        // Value keys that mark a skill as helping its targets, or as a defensive one (reactive rather than an opener).
        private static readonly string[] HealKeys = { "heal", "hot", "regen" };
        private static readonly string[] DefensiveKeys = { "dr", "shield", "absorb", "block" };
        private static readonly string[] DamageKeys = { "blunt", "slash", "pierce", "fire", "frost", "lightning", "spirit", "poison", "dps", "damage", "bonus" };

        public static IReadOnlyList<CompanionBrain.SkillOption> ReadyOnActiveBar(Character target)
        {
            var options = new List<CompanionBrain.SkillOption>();
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || ClassPlayerStore.LocalIsProtected) return options;
            BarSide side = data.ActiveBar;
            BarLayout bar = data.Bar(side);
            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.UltimateSlot; slot++)
            {
                if (BarController.Availability(side, slot) != SlotAvailability.Ready) continue;
                string id = slot == BarLayout.UltimateSlot ? bar.UltimateId : bar.SkillAt(slot);
                ClassTreeNode node = ClassTrees.Node(id);
                if (node != null) options.Add(Describe(slot, node));
            }
            LogForBrain(bar, options, target);
            return options;
        }

        // What the brain was offered, at most every few seconds (the skills drill pairs it with Core's choice and FDT's press).
        private const float LogEverySeconds = 3f;
        private static float _nextLog;
        private const float LogRepeatSeconds = 30f;
        private static string _lastLine;
        private static float _lastLineAt;

        private static void LogForBrain(BarLayout bar, List<CompanionBrain.SkillOption> options, Character target)
        {
            if (UnityEngine.Time.time < _nextLog) return;
            _nextLog = UnityEngine.Time.time + LogEverySeconds;
            string offered = options.Count == 0
                ? "none ready (" + string.Join(", ", Enumerable.Range(BarLayout.FirstSkillSlot, BarLayout.UltimateSlot - BarLayout.FirstSkillSlot + 1)
                    .Where(slot => !string.IsNullOrEmpty(slot == BarLayout.UltimateSlot ? bar.UltimateId : bar.SkillAt(slot)))
                    .Select(slot => slot + ":" + BarController.Availability(ClassPlayerStore.Local.ActiveBar, slot))) + ")"
                : string.Join(", ", options.Select(o => $"{o.Slot}:{o.Id} {o.Category}{(o.Aoe ? " aoe" : "")}{(o.Heal ? " heal" : "")}{(o.Buff ? " buff" : "")} 0-{o.MaxRange:0} m"));
            // Debug level, and an unchanged offer for the same target is logged again only after LogRepeatSeconds, so a
            // long fight doesn't flood the log with identical lines.
            string line = $"ready for the brain vs {(target != null ? target.m_name : "no target")}: {offered}";
            if (line == _lastLine && UnityEngine.Time.time < _lastLineAt + LogRepeatSeconds) return;
            _lastLine = line; _lastLineAt = UnityEngine.Time.time;
            FiresRPGClassesPlugin.Log.LogDebug(line);
        }

        internal static CompanionBrain.SkillOption Describe(int slot, ClassTreeNode node)
        {
            TargetingSpec aim = node.Targeting;
            // Without this, Eitr Well's "regen" would read as a HEAL and Core would keep it for "hurt under 40 %". An eitr restorer is cast when the caster's eitr runs low, and not held back otherwise.
            if (RestoresEitr(node))
            {
                Player me = Player.m_localPlayer;
                bool low = me != null && me.GetMaxEitr() > 0f && me.GetEitr() < me.GetMaxEitr() * LowEitrShare;
                return new CompanionBrain.SkillOption
                {
                    Slot = slot,
                    Id = node.Id,
                    Category = low ? SkillDecisionSystem.SkillCategory.Sustained : SkillDecisionSystem.SkillCategory.Situational,
                    MinRange = 0f,
                    MaxRange = Reach(aim),
                    Buff = true,
                    Aoe = aim.Mode == TargetingMode.AroundYou || aim.Mode == TargetingMode.Placed,
                };
            }
            bool heal = aim.Mode == TargetingMode.Ally || Has(node, HealKeys);
            // Core-backed skills carry no yaml values (their numbers live in Core), so a strike mode is damage by default. Without this
            // Chi Strike / Flurry read as "Situational" and the brain only tried them early in a fight.
            bool damage = Has(node, DamageKeys) || aim.Mode == TargetingMode.Melee || aim.Mode == TargetingMode.NextAttack
                || aim.Mode == TargetingMode.Aimed || aim.Mode == TargetingMode.Dash;
            bool helpful = heal || Has(node, DefensiveKeys) || (!damage && (aim.Mode == TargetingMode.Self || aim.Mode == TargetingMode.Ally));
            bool aoe = aim.Mode == TargetingMode.AroundYou || aim.Mode == TargetingMode.Placed;

            SkillDecisionSystem.SkillCategory category =
                heal || Has(node, DefensiveKeys) ? SkillDecisionSystem.SkillCategory.Reactive
                : helpful ? SkillDecisionSystem.SkillCategory.Opener
                : aoe || !damage ? SkillDecisionSystem.SkillCategory.Situational
                : SkillDecisionSystem.SkillCategory.Sustained;

            return new CompanionBrain.SkillOption
            {
                Slot = slot,
                Id = node.Id,
                Category = category,
                MinRange = 0f,
                MaxRange = Reach(aim),
                Heal = heal,
                Buff = helpful && !heal,
                Aoe = aoe,
                Debuff = !helpful,
            };
        }

        private const float LowEitrShare = 0.25f;

        /// <summary>A skill whose purpose is eitr (Eitr Well, eitr regen): its text speaks of eitr and it carries a regen/restore value.</summary>
        internal static bool RestoresEitr(ClassTreeNode node) =>
            node != null && node.IsActive && (node.Description ?? "").IndexOf("eitr", System.StringComparison.OrdinalIgnoreCase) >= 0
            && node.Values != null && node.Values.Keys.Any(key => key == "regen" || key == "restore" || key == "eitr");

        private static float Reach(TargetingSpec aim)
        {
            switch (aim.Mode)
            {
                case TargetingMode.Melee: return MeleeReach;
                case TargetingMode.NextAttack: return WieldsRanged() ? RangedReach : MeleeReach;
                case TargetingMode.Self:
                case TargetingMode.AroundYou: return aim.Radius > 0f ? aim.Radius : DefaultRadius;
                default: return aim.Range > 0f ? aim.Range : RangedReach;
            }
        }

        private static bool WieldsRanged()
        {
            ItemDrop.ItemData weapon = Player.m_localPlayer?.GetCurrentWeapon();
            return weapon != null && (weapon.m_shared.m_skillType == Skills.SkillType.Bows || weapon.m_shared.m_skillType == Skills.SkillType.Crossbows
                || weapon.m_shared.m_skillType == Skills.SkillType.ElementalMagic || weapon.m_shared.m_skillType == Skills.SkillType.BloodMagic);
        }

        private static bool Has(ClassTreeNode node, string[] keys) =>
            node.Values.Keys.Any(key => keys.Any(part => key.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0));
    }
}
