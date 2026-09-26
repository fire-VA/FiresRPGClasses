using System.Collections.Generic;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// Slot and targeting for Core's 73 abilities, from design doc 14 section 6. Superseded per node by the
    /// tree data once the yaml loader lands. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class CoreAbilityTargeting
    {
        /// <summary>Doc 16: Core's capstones become Ultimate II, reachable at 20 rather than 100.</summary>
        public const int UltimateTwoLevel = 20;

        private static readonly Dictionary<string, TargetingSpec> Actives =
            new Dictionary<string, TargetingSpec>
            {
                { "tank_taunt",             new TargetingSpec(TargetingMode.AroundYou, 0f, 10f) },
                { "tank_fortify",           new TargetingSpec(TargetingMode.Self) },
                { "tank_immortal_stance",   new TargetingSpec(TargetingMode.AroundYou, 0f, 10f) },

                { "paladin_smite",          new TargetingSpec(TargetingMode.NextAttack) },
                { "paladin_protection",     new TargetingSpec(TargetingMode.AroundYou, 0f, 8f) },
                { "paladin_consecration",   new TargetingSpec(TargetingMode.Placed, 20f, 6f) },
                { "paladin_divine_shield",  new TargetingSpec(TargetingMode.Self) },
                { "paladin_lay_hands",      new TargetingSpec(TargetingMode.Ally, 20f) },
                { "paladin_avatar",         new TargetingSpec(TargetingMode.Self) },

                { "berserker_warcry",       new TargetingSpec(TargetingMode.AroundYou, 0f, 10f) },
                { "berserker_avatar_war",   new TargetingSpec(TargetingMode.Self) },

                { "rogue_poison",           new TargetingSpec(TargetingMode.NextAttack) },
                { "rogue_stealth",          new TargetingSpec(TargetingMode.Self) },
                { "rogue_evasion",          new TargetingSpec(TargetingMode.Self) },
                { "rogue_death_mark",       new TargetingSpec(TargetingMode.Aimed, 30f) },
                { "rogue_shadow_dance",     new TargetingSpec(TargetingMode.Self) },

                { "monk_chi_strike",        new TargetingSpec(TargetingMode.NextAttack) },
                { "monk_inner_peace",       new TargetingSpec(TargetingMode.Self) },
                { "monk_flurry",            new TargetingSpec(TargetingMode.Melee) },
                { "monk_iron_body",         new TargetingSpec(TargetingMode.Self) },
                { "monk_chi_explosion",     new TargetingSpec(TargetingMode.AroundYou, 0f, 6f) },
                { "monk_perfection",        new TargetingSpec(TargetingMode.Self) },

                { "ranger_mark",            new TargetingSpec(TargetingMode.Aimed, 50f) },
                { "ranger_eagle_eye",       new TargetingSpec(TargetingMode.Self) },
                { "ranger_multishot",       new TargetingSpec(TargetingMode.NextAttack) },
                { "ranger_rain_arrows",     new TargetingSpec(TargetingMode.Placed, 40f, 8f) },
                { "ranger_perfect_shot",    new TargetingSpec(TargetingMode.NextAttack) },

                { "mage_infusion",          new TargetingSpec(TargetingMode.Self) },
                { "mage_shield",            new TargetingSpec(TargetingMode.Self) },
                { "mage_overcharge",        new TargetingSpec(TargetingMode.Self) },
                { "mage_meteor",            new TargetingSpec(TargetingMode.Placed, 40f, 8f) },
                { "mage_arcane_form",       new TargetingSpec(TargetingMode.Self) },

                { "healer_purify",          new TargetingSpec(TargetingMode.Ally, 20f) },
                { "healer_sanctuary",       new TargetingSpec(TargetingMode.Placed, 20f, 8f) },
                { "healer_purify_circle",   new TargetingSpec(TargetingMode.Placed, 20f, 6f) },
                { "healer_resurrection",    new TargetingSpec(TargetingMode.Ally, 10f) },
                { "healer_divine_hymn",     new TargetingSpec(TargetingMode.AroundYou, 0f, 12f) },
                { "healer_avatar_life",     new TargetingSpec(TargetingMode.Self) },
            };

        /// <summary>Doc 14: these replace the dodge roll rather than taking a slot.</summary>
        private static readonly HashSet<string> Evasions = new HashSet<string>
        {
            "tank_shield_charge",
            "paladin_divine_retreat",
            "berserker_leap",
            "rogue_shadow_escape",
            "monk_wind_step",
            "ranger_disengage",
            "mage_blink",
            "healer_fade",
        };

        /// <summary>Doc 14: non-passive abilities that still take no slot - they run on their own trigger.</summary>
        private static readonly HashSet<string> Automatics = new HashSet<string>
        {
            "tank_block",
            "tank_unyielding",
            "paladin_block",
            "berserker_fury",
            "berserker_rage",
            "berserker_deathwish",
            "rogue_caltrops",
            "monk_martial_arts",
            "mage_cast",
            "healer_heal",
        };

        public static bool IsEvasion(string abilityId) => Evasions.Contains(abilityId);

        public static bool IsAutomatic(string abilityId) => Automatics.Contains(abilityId);

        public static TargetingSpec For(string abilityId) =>
            Actives.TryGetValue(abilityId, out TargetingSpec spec) ? spec : new TargetingSpec(TargetingMode.Self);

        public static bool HasEntry(string abilityId) => Actives.ContainsKey(abilityId);
    }
}
