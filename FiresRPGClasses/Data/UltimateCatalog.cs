using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.Data
{
    public enum UltimateResource
    {
        Stamina,
        Eitr,
        Adrenaline,
        Health
    }

    public class UltimateCost
    {
        public UltimateResource Resource;
        public float Amount;
        public bool IsPercentOfMax;

        public UltimateCost(UltimateResource resource, float amount, bool percentOfMax = false)
        {
            Resource = resource;
            Amount = amount;
            IsPercentOfMax = percentOfMax;
        }

        public override string ToString() =>
            IsPercentOfMax ? Amount + "% " + Resource : Amount + " " + Resource;
    }

    public class UltimateMorph
    {
        public string Name = "";
        public string Effect = "";
        public string CapstoneName = "";
        public string CapstoneEffect = "";
    }

    public class UltimateDefinition
    {
        public string Id = "";
        public string DisplayName = "";
        public ArchetypeClass Archetype;
        public ArchetypeClass HybridPartner = ArchetypeClass.None;
        public int UnlockLevel;
        public UltimateCost Cost;
        public float Cooldown;
        public readonly List<UltimateMorph> Morphs = new List<UltimateMorph>();

        public bool IsHybrid => HybridPartner != ArchetypeClass.None;
    }

    /// <summary>
    /// Doc 16. Owns the shared mechanics: the eight-node level tree every ultimate walks, the hybrid template,
    /// and a fallback definition for Core's eight capstones until the yaml supplies the numbers.
    /// </summary>
    public static class UltimateCatalog
    {
        public const int SlotUnlockLevel = 10;
        public const int UltimateOneLevel = 10;
        public const int UltimateTwoLevel = 20;
        public const int HybridUnlockLevel = 25;

        public const int MaxUltimateLevel = 100;
        public const int MorphLevel = 25;
        public const int CapstoneLevel = 85;

        public const int PointsInTreeForUltimateOne = 10;
        public const int PointsInPathForUltimateTwo = 20;
        public const int PointsInEachArchetypeForHybrid = 10;

        public const float BaseStrengthFraction = 0.5f;
        public const float EmpoweredPotencyBonus = 0.25f;
        public const float RefineCostReduction = 0.15f;
        public const float ExtendMagnitudeBonus = 0.25f;
        public const float HastenCooldownReduction = 0.15f;

        public const float HybridAdrenalineCost = 50f;
        public const float HybridCooldown = 180f;
        public const float BloodHybridHealthPercent = 20f;

        /// <summary>Doc 16: the nodes an ultimate unlocks by its own level, no points spent.</summary>
        public static readonly int[] NodeLevels = { 1, 10, 25, 40, 55, 70, 85, 100 };

        public static readonly string[] NodeNames =
            { "Base", "Empowered", "Morph", "Refine", "Extend", "Hasten", "Capstone", "Mastery" };

        private static readonly HashSet<string> BloodHybridNames = new HashSet<string>
        {
            "Ravager", "Blood Mage", "Blood Knight", "Pain Shaman",
        };

        private static readonly Dictionary<ArchetypeClass, string> ArchetypeAspects =
            new Dictionary<ArchetypeClass, string>
            {
                { ArchetypeClass.Tank, "damage reduction and taunts" },
                { ArchetypeClass.Paladin, "spirit damage and protection" },
                { ArchetypeClass.Berserker, "attack speed and lifesteal" },
                { ArchetypeClass.Rogue, "stealth and poison" },
                { ArchetypeClass.Monk, "stagger and stamina" },
                { ArchetypeClass.Ranger, "range and marks" },
                { ArchetypeClass.Mage, "elemental damage and slows" },
                { ArchetypeClass.Healer, "healing and cleansing" },
            };

        private static readonly Dictionary<string, UltimateDefinition> Definitions =
            new Dictionary<string, UltimateDefinition>();

        public static IEnumerable<UltimateDefinition> All => Definitions.Values;

        public static void Rebuild()
        {
            Definitions.Clear();
            foreach (UltimateDefinition definition in CoreCapstoneFallbacks())
                Definitions[definition.Id] = definition;
            foreach (UltimateDefinition definition in HybridDefinitions())
                Definitions[definition.Id] = definition;
        }

        public static void Register(UltimateDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return;
            Definitions[definition.Id] = definition;
        }

        public static UltimateDefinition Get(string ultimateId) =>
            ultimateId != null && Definitions.TryGetValue(ultimateId, out UltimateDefinition def) ? def : null;

        public static IEnumerable<UltimateDefinition> For(ArchetypeClass archetype) =>
            Definitions.Values
                .Where(def => !def.IsHybrid && def.Archetype == archetype)
                .OrderBy(def => def.UnlockLevel);

        public static IEnumerable<UltimateDefinition> ForHybrid(ArchetypeClass front, ArchetypeClass back) =>
            Definitions.Values.Where(def => def.IsHybrid
                && ((def.Archetype == front && def.HybridPartner == back)
                    || (def.Archetype == back && def.HybridPartner == front)));

        public static string HybridId(ArchetypeClass front, ArchetypeClass back) =>
            "hybrid_" + front.ToString().ToLowerInvariant() + "_" + back.ToString().ToLowerInvariant();

        /// <summary>Doc 16: the node at or below an ultimate's own level, as an index into NodeLevels.</summary>
        public static int NodeIndexFor(int ultimateLevel)
        {
            int index = 0;
            for (int i = 0; i < NodeLevels.Length; i++)
                if (ultimateLevel >= NodeLevels[i]) index = i;
            return index;
        }

        public static bool MorphsUnlocked(int ultimateLevel) => ultimateLevel >= MorphLevel;

        /// <summary>Half strength at level 1, full at 100, with the Empowered step folded in.</summary>
        public static float PotencyFor(int ultimateLevel)
        {
            float span = MaxUltimateLevel - 1;
            float progress = span <= 0f ? 1f : (ultimateLevel - 1) / span;
            float scaled = BaseStrengthFraction + (1f - BaseStrengthFraction) * progress;
            return ultimateLevel >= NodeLevels[1] ? scaled * (1f + EmpoweredPotencyBonus) : scaled;
        }

        public static float CostMultiplierFor(int ultimateLevel) =>
            ultimateLevel >= NodeLevels[3] ? 1f - RefineCostReduction : 1f;

        public static float CooldownMultiplierFor(int ultimateLevel) =>
            ultimateLevel >= NodeLevels[5] ? 1f - HastenCooldownReduction : 1f;

        public static float MagnitudeMultiplierFor(int ultimateLevel) =>
            ultimateLevel >= NodeLevels[4] ? 1f + ExtendMagnitudeBonus : 1f;

        private static IEnumerable<UltimateDefinition> CoreCapstoneFallbacks()
        {
            yield return Capstone("tank_immortal_stance", "Immortal Stance", ArchetypeClass.Tank,
                new UltimateCost(UltimateResource.Adrenaline, 60f), 240f,
                "Stone Sentinel", "Mountain Heart", "Undying Roar", "Rally the Fallen");

            yield return Capstone("paladin_avatar", "Avatar of Light", ArchetypeClass.Paladin,
                new UltimateCost(UltimateResource.Adrenaline, 60f), 240f,
                "Avatar of Dawn", "Sunrise", "Avatar of Judgement", "Hammerfall");

            yield return Capstone("berserker_avatar_war", "Avatar of War", ArchetypeClass.Berserker,
                new UltimateCost(UltimateResource.Adrenaline, 70f), 240f,
                "Unstoppable Wrath", "Warpath", "Warlord", "Horn of Ragnarok");

            yield return Capstone("rogue_shadow_dance", "Shadow Dance", ArchetypeClass.Rogue,
                new UltimateCost(UltimateResource.Adrenaline, 50f), 180f,
                "Dance of Blades", "Thousand Cuts", "Dance of Shadows", "Encore");

            yield return Capstone("monk_perfection", "Way of Perfection", ArchetypeClass.Monk,
                new UltimateCost(UltimateResource.Adrenaline, 60f), 240f,
                "Way of the Fist", "Perfect Form", "Way of the Wind", "Empty Mind");

            yield return Capstone("ranger_perfect_shot", "Perfect Shot", ArchetypeClass.Ranger,
                new UltimateCost(UltimateResource.Adrenaline, 40f), 120f,
                "Ballista Shot", "Split Arrow", "Hunter's Culling", "Apex Shot");

            yield return Capstone("mage_arcane_form", "Arcane Form", ArchetypeClass.Mage,
                new UltimateCost(UltimateResource.Adrenaline, 60f), 240f,
                "Arcane Surge", "Overflow", "Astral Projection", "Singularity");

            yield return Capstone("healer_avatar_life", "Avatar of Life", ArchetypeClass.Healer,
                new UltimateCost(UltimateResource.Health, 25f, true), 240f,
                "Avatar of Mercy", "Lifebloom", "Avatar of Sacrifice", "Martyr's Grace");
        }

        private static UltimateDefinition Capstone(string id, string name, ArchetypeClass archetype,
            UltimateCost cost, float cooldown,
            string firstMorph, string firstCapstone, string secondMorph, string secondCapstone)
        {
            UltimateDefinition definition = new UltimateDefinition
            {
                Id = id,
                DisplayName = name,
                Archetype = archetype,
                UnlockLevel = UltimateTwoLevel,
                Cost = cost,
                Cooldown = cooldown,
            };
            definition.Morphs.Add(new UltimateMorph { Name = firstMorph, CapstoneName = firstCapstone });
            definition.Morphs.Add(new UltimateMorph { Name = secondMorph, CapstoneName = secondCapstone });
            return definition;
        }

        /// <summary>
        /// Doc 16: every ordered pair's two hybrid ultimates come from Core's own hybrid names, on one template.
        /// </summary>
        private static IEnumerable<UltimateDefinition> HybridDefinitions()
        {
            foreach (KeyValuePair<(ArchetypeClass, ArchetypeClass), HybridArchetypeDefinitions.HybridDefinition> pair
                in HybridArchetypeDefinitions.GetAllHybrids())
            {
                ArchetypeClass front = pair.Key.Item1;
                ArchetypeClass back = pair.Key.Item2;
                HybridArchetypeDefinitions.HybridDefinition hybrid = pair.Value;
                if (hybrid == null) continue;

                bool blood = BloodHybridNames.Contains(hybrid.DisplayName);
                UltimateDefinition definition = new UltimateDefinition
                {
                    Id = HybridId(front, back),
                    DisplayName = hybrid.DisplayName + " - " + hybrid.SpecialAbilityName,
                    Archetype = front,
                    HybridPartner = back,
                    UnlockLevel = HybridUnlockLevel,
                    Cost = blood
                        ? new UltimateCost(UltimateResource.Health, BloodHybridHealthPercent, true)
                        : new UltimateCost(UltimateResource.Adrenaline, HybridAdrenalineCost),
                    Cooldown = HybridCooldown,
                };
                definition.Morphs.Add(Leaning(front, "Resonance", hybrid.DisplayName));
                definition.Morphs.Add(Leaning(back, "Echo", hybrid.DisplayName));
                yield return definition;
            }
        }

        private static UltimateMorph Leaning(ArchetypeClass archetype, string capstoneName, string hybridName)
        {
            string aspect = ArchetypeAspects.TryGetValue(archetype, out string text) ? text : "its own aspect";
            return new UltimateMorph
            {
                Name = archetype + "-leaning " + hybridName,
                Effect = "Adds " + aspect + ".",
                CapstoneName = capstoneName,
                CapstoneEffect = "Also triggers " + archetype + "'s Ultimate I at 50% strength.",
            };
        }
    }
}
