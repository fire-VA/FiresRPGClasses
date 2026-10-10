using FiresCore.Npc.Archetypes;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Every vanilla hook the class passives need, one patch class per game method (the family's one-hook rule). Each hook
    /// hands off to PassiveKit, which decides from the passive grid whether the effect is on. All of them act only for the
    /// local player, on the peer that owns the thing being changed: Character.Damage runs on the attacker's peer,
    /// BlockAttack / UseStamina / UseEitr / the regen reads on the player's own.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class ClassDamageHooks
    {
        // Backstab / Assassinate scale the hit before it is sent.
        [HarmonyPrefix]
        private static void BeforeDamage(Character __instance, HitData hit)
        {
            PassiveKit.OnLocalHitSending(__instance, hit);
            DruidNecroPassives.OnHitSending(__instance, hit); // Life Tap, Bone Harvest's watch
            AttributeEffects.OnLocalHitSending(hit); // Strength / Intellect: physical and magic damage dealt
        }

        // A poisoned blade (CoreSkillKit) poisons the local player's next hit.
        [HarmonyPostfix]
        private static void AfterDamage(Character __instance, HitData hit) => CoreSkillKit.OnLocalHit(__instance, hit);
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class ClassBlockHooks
    {
        [HarmonyPrefix]
        private static void BeforeBlock(Humanoid __instance, HitData hit, out bool __state) =>
            __state = PassiveKit.OnLocalBlocking(__instance, hit);

        [HarmonyPostfix]
        private static void AfterBlock(Humanoid __instance, bool __result, bool __state) =>
            PassiveKit.OnLocalBlocked(__instance, __result, __state);
    }

    [HarmonyPatch(typeof(Game), nameof(Game.RPC_RegisterKill))]
    internal static class ClassKillHooks
    {
        [HarmonyPostfix]
        private static void AfterKill()
        {
            PassiveKit.OnLocalKill();
            DruidNecroPassives.OnLocalKill(); // Bone Harvest
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    internal static class ClassStaminaCostHooks
    {
        [HarmonyPrefix]
        private static void BeforeUse(Player __instance, ref float v) =>
            v *= PassiveKit.StaminaCostFactor(__instance) * AttributeEffects.StaminaCostFactor(__instance); // + Agility
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseEitr))]
    internal static class ClassEitrCostHooks
    {
        // Spellcasting makes a spell cheaper; Chain Casting's false skips the spend (a free cast).
        [HarmonyPrefix]
        private static bool BeforeUse(Player __instance, ref float v)
        {
            v *= PassiveKit.SpellCostFactor(__instance);
            if (DruidNecroKit.LichPays(__instance, v)) return false;   // Lich Form: health instead of eitr
            return !PassiveKit.FreeCast(__instance, v);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
    internal static class ClassStaminaRegenHooks
    {
        [HarmonyPostfix]
        private static void AfterModify(SEMan __instance, ref float staminaMultiplier) =>
            staminaMultiplier *= PassiveKit.StaminaRegenFactor(__instance.m_character) * AttributeEffects.StaminaRegenFactor(__instance.m_character);
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyEitrRegen))]
    internal static class ClassEitrRegenHooks
    {
        [HarmonyPostfix]
        private static void AfterModify(SEMan __instance, ref float eitrMultiplier) =>
            eitrMultiplier *= PassiveKit.EitrRegenFactor(__instance.m_character) * AttributeEffects.EitrRegenFactor(__instance.m_character);
    }

    // Kiting Expertise: faster while drawing.
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
    internal static class ClassSpeedHooks
    {
        [HarmonyPostfix]
        private static void AfterSpeedMods(SEMan __instance, ref float speed) => speed *= PassiveKit.SpeedFactor(__instance.m_character);
    }

    // Shield Block / Blessed Shield: the local player's raised shield blocks more. The tooltip reads GetBlockPowerTooltip.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float))]
    internal static class ClassBlockPowerHooks
    {
        [HarmonyPostfix]
        private static void AfterBlockPower(ItemDrop.ItemData __instance, ref float __result) => __result *= PassiveKit.BlockPowerFactor(__instance);
    }

    // Steady Aim: how far the local player's bow was drawn when the arrow left.
    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    internal static class ClassShotHooks
    {
        [HarmonyPrefix]
        private static void BeforeFire(Attack __instance) => PassiveKit.OnLocalShot(__instance.m_character);
    }

    // Healing Staff / Emergency Heal: Core's class heals carry the healer (AbilityHeals.Apply runs on the healer's peer).
    [HarmonyPatch(typeof(AbilityHeals), nameof(AbilityHeals.Apply))]
    internal static class ClassHealHooks
    {
        [HarmonyPrefix]
        private static void BeforeHeal(Character healer, Character target, ref float amount) => amount *= PassiveKit.HealFactor(healer, target);
    }

    // Intercept's guard aura: damage the local player takes, on the peer that owns them.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class ClassDamageTakenHooks
    {
        [HarmonyPrefix]
        private static void BeforeDamageTaken(Character __instance, HitData hit)
        {
            PassiveKit.OnLocalHitTaken(__instance, hit);
            DruidNecroPassives.OnHitTaken(__instance, hit); // Thick Hide on the possessed form
            AttributeEffects.OnLocalHitTaken(__instance, hit); // Vigour: elemental damage taken
        }
    }
}
