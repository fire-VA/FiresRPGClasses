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
            ActiveKit.OnLocalHitSending(__instance, hit); // Killshot
            PassiveKit2.OnLocalHitSending(__instance, hit); // Zeal, Cleave
            PassiveKit3.OnLocalHitSending(__instance, hit); // Unseen, Deadeye, Apex Predator, Frostbite, Ignite
            WerewolfKit.OnLocalHitSending(__instance, hit); // claws, Lunar Hunger, Alpha's heal
            VampireKit.OnLocalHitSending(__instance, hit); // Night Stalker, life-steal, the bat overflow at half
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
            v *= PassiveKit.StaminaCostFactor(__instance) * AttributeEffects.StaminaCostFactor(__instance) // + Agility
                * BuffKit.StaminaCostFactor(__instance) // + Battle Trance
                * PassiveKit2.StaminaCostFactor(__instance); // + Immovable, Quickstep, Leaf on the Wind, Swift Feet
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
        private static void AfterSpeedMods(SEMan __instance, ref float speed) => speed *= PassiveKit.SpeedFactor(__instance.m_character) * BuffKit.SpeedFactor(__instance.m_character) // + Shield Wall's slow
            * PassiveKit2.SpeedFactor(__instance.m_character); // + Swift Feet
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

    // Piercing Shot: the local player's next bow/crossbow projectile takes the armed window.
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class ClassProjectileSetupHooks
    {
        [HarmonyPostfix]
        private static void AfterSetup(Projectile __instance, Character owner, ItemDrop.ItemData item) => CcKit.OnProjectileSetup(__instance, owner, item);
    }

    // Piercing Shot: a piercing projectile passes through creatures (vanilla's own m_onlyStopOnTerrain path, per call).
    // OnHit runs on the projectile's owner (FixedUpdate returns on non-owners), i.e. the shooter.
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class ClassProjectileHitHooks
    {
        [HarmonyPrefix]
        private static bool BeforeHit(Projectile __instance, Collider collider, out CcKit.PierceCall __state) =>
            CcKit.BeforeProjectileHit(__instance, collider, out __state);

        [HarmonyPostfix]
        private static void AfterHit(Projectile __instance, CcKit.PierceCall __state) => CcKit.AfterProjectileHit(__instance, __state);
    }

    // Healing Staff / Emergency Heal: Core's class heals carry the healer (AbilityHeals.Apply runs on the healer's peer).
    [HarmonyPatch(typeof(AbilityHeals), nameof(AbilityHeals.Apply))]
    internal static class ClassHealHooks
    {
        [HarmonyPrefix]
        private static void BeforeHeal(Character healer, Character target, ref float amount)
        {
            amount *= PassiveKit.HealFactor(healer, target);
            PassiveKit3.OnLocalHeal(healer, target, amount); // Overflow
        }
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
            BuffKit.OnLocalHitTaken(__instance, hit); // Shield Wall: frontal hits
            ActiveKit.OnLocalHitTaken(__instance, hit); // Glacial Ward
            MoveKit.OnLocalHitTaken(__instance, hit); // Breakwall's charge
            LinkKit.OnLocalHitTaken(__instance, hit); // Guardian's Oath, Spirit Link
            PassiveKit2.OnLocalHitTaken(__instance, hit); // Thick Hide, Thick Skull, Immovable
            PassiveKit3.OnLocalHitTaken(__instance, hit); // Pain Fuels Me
            WerewolfKit.OnLocalHitTaken(__instance, hit); // Blood Rage's "in a fight"
            VampireKit.OnLocalHitTaken(__instance, hit); // Bat Form's 5 s lockout after damage
            ExtraSkills.ExtraSkillSystem.OnLocalHitTaken(__instance, hit); // Endurance skill
        }
    }
}
