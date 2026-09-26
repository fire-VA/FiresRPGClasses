using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Doc 16: slot 8's resource cost and cooldown, and the ultimate's own level, which grows by use.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class UltimateController
    {
        public const float MaxAdrenalineWithoutTrinket = 100f;

        private const long XpPerCast = 100L;
        private const long XpPerTargetAffected = 25L;
        private const float XpCurveBase = 1.06f;
        private const long XpForSecondLevel = 400L;

        public static long XpForUltimateLevel(int level) =>
            (long)(XpForSecondLevel * Mathf.Pow(XpCurveBase, Mathf.Max(0, level - 1)));

        /// <summary>Doc 16: a slotted adrenaline ultimate lets the bar build without a trinket, up to 100.</summary>
        public static bool WantsAdrenalineFloor()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return false;
            return SlotWantsAdrenaline(data, BarSide.Front) || SlotWantsAdrenaline(data, BarSide.Back);
        }

        private static bool SlotWantsAdrenaline(ClassPlayerData data, BarSide side)
        {
            UltimateDefinition definition = UltimateCatalog.Get(data.Bar(side).UltimateId);
            return definition?.Cost != null && definition.Cost.Resource == UltimateResource.Adrenaline;
        }

        public static float EffectiveCost(UltimateDefinition definition, int ultimateLevel)
        {
            if (definition?.Cost == null) return 0f;
            return definition.Cost.Amount * UltimateCatalog.CostMultiplierFor(ultimateLevel);
        }

        public static float EffectiveCooldown(UltimateDefinition definition, int ultimateLevel)
        {
            if (definition == null) return 0f;
            return definition.Cooldown * UltimateCatalog.CooldownMultiplierFor(ultimateLevel);
        }

        public static bool CanPay(Player player, UltimateDefinition definition, int ultimateLevel, out string refusal)
        {
            refusal = null;
            if (player == null || definition?.Cost == null) { refusal = "no cost"; return false; }
            float amount = EffectiveCost(definition, ultimateLevel);

            switch (definition.Cost.Resource)
            {
                case UltimateResource.Stamina:
                    if (player.HaveStamina(amount)) return true;
                    refusal = "not enough stamina";
                    return false;
                case UltimateResource.Eitr:
                    if (player.HaveEitr(amount)) return true;
                    refusal = "not enough eitr";
                    return false;
                case UltimateResource.Adrenaline:
                    if (player.GetAdrenaline() >= amount) return true;
                    refusal = "not enough adrenaline";
                    return false;
                case UltimateResource.Health:
                    float health = HealthCost(player, definition, ultimateLevel);
                    if (player.GetHealth() > health) return true;
                    refusal = "not enough health";
                    return false;
                default:
                    refusal = "unknown resource";
                    return false;
            }
        }

        public static void Pay(Player player, UltimateDefinition definition, int ultimateLevel)
        {
            if (player == null || definition?.Cost == null) return;
            float amount = EffectiveCost(definition, ultimateLevel);

            switch (definition.Cost.Resource)
            {
                case UltimateResource.Stamina:
                    player.UseStamina(amount);
                    break;
                case UltimateResource.Eitr:
                    player.UseEitr(amount);
                    break;
                case UltimateResource.Adrenaline:
                    player.AddAdrenaline(-amount);
                    break;
                case UltimateResource.Health:
                    player.SetHealth(player.GetHealth() - HealthCost(player, definition, ultimateLevel));
                    break;
            }
        }

        private static float HealthCost(Player player, UltimateDefinition definition, int ultimateLevel)
        {
            float amount = EffectiveCost(definition, ultimateLevel);
            return definition.Cost.IsPercentOfMax ? player.GetMaxHealth() * amount / 100f : amount;
        }

        /// <summary>Doc 16: XP per cast plus a share per target, with nodes unlocking by level alone.</summary>
        public static int AwardCastXp(ClassPlayerData data, string ultimateId, int targetsAffected)
        {
            UltimateProgress progress = data?.UltimateState(ultimateId);
            if (progress == null) return 0;

            progress.Xp += XpPerCast + XpPerTargetAffected * Mathf.Max(0, targetsAffected);
            int levelsGained = 0;
            while (progress.Level < UltimateCatalog.MaxUltimateLevel)
            {
                long needed = XpForUltimateLevel(progress.Level);
                if (progress.Xp < needed) break;
                progress.Xp -= needed;
                progress.Level++;
                levelsGained++;
            }
            if (progress.Level >= UltimateCatalog.MaxUltimateLevel) progress.Xp = 0L;
            return levelsGained;
        }

        /// <summary>Doc 19: the character's own progression gates which ultimates may be slotted at all.</summary>
        public static bool MeetsPointGate(ClassPlayerData data, UltimateDefinition definition)
        {
            if (data == null || definition == null) return false;
            if (definition.IsHybrid)
            {
                return PointsInTree(data, definition.Archetype) >= UltimateCatalog.PointsInEachArchetypeForHybrid
                    && PointsInTree(data, definition.HybridPartner) >= UltimateCatalog.PointsInEachArchetypeForHybrid;
            }
            int required = definition.UnlockLevel >= UltimateCatalog.UltimateTwoLevel
                ? UltimateCatalog.PointsInPathForUltimateTwo
                : UltimateCatalog.PointsInTreeForUltimateOne;
            return PointsInTree(data, definition.Archetype) >= required;
        }

        private static int PointsInTree(ClassPlayerData data, FiresCore.Npc.Archetypes.ArchetypeClass archetype)
        {
            if (!data.Progress.Talents.TryGetValue(archetype,
                    out System.Collections.Generic.Dictionary<string, int> ranks))
                return 0;
            int spent = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, int> entry in ranks)
                for (int rank = 1; rank <= entry.Value; rank++)
                    spent += ClassTrees.RankCost(rank);
            return spent;
        }
    }
}
