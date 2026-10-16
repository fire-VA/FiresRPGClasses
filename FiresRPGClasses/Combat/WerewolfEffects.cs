using FiresCore.Npc.Archetypes.StatusEffects;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The Werewolf's status effects (Tools\WEREWOLF_CLASS_DESIGN.md §3, §4, §5). They are RPGClasses' own, registered into ObjectDB
    /// beside Core's, and applied through Core's AbilityRPCManager by name: Core's generic path (ApplyRegisteredEffect) takes any
    /// ObjectDB template that is a CompanionStatusEffectBase, so no Core change is needed. Each effect works on the peer that owns the
    /// character it sits on (vanilla's SE hooks: speed, stamina regen, jump, armour, damage dealt / taken). Numbers come from
    /// [11 - Werewolf], synced from the server.
    /// </summary>
    internal static class WerewolfEffects
    {
        internal const string WolfForm = "ClassWolfForm";
        internal const string Howl = "ClassHowl";
        internal const string HowlPack = "ClassHowlPack";     // Howl's ally buff with Pack Bond's armour
        internal const string HowlDread = "ClassHowlDread";   // Howl's boss debuff
        internal const string Alpha = "ClassAlpha";

        internal const float HowlDamage = 1.15f, HowlSpeed = 0.10f, HowlStamina = 0.30f, PackArmour = 1.10f;
        internal const float DreadDealt = 0.90f, DreadTaken = 1.10f;
        internal const float AlphaDamage = 1.20f;

        internal static readonly int WolfFormHash = WolfForm.GetStableHashCode();
        internal static readonly int AlphaHash = Alpha.GetStableHashCode();
        internal static readonly int HowlDreadHash = HowlDread.GetStableHashCode();

        /// <summary>Adds the templates to this ObjectDB once (ObjectDB.Awake runs again on every world load).</summary>
        internal static void Register(ObjectDB db)
        {
            if (db == null) return;
            int added = 0;
            added += Add<WolfFormEffect>(db, WolfForm);
            added += Add<HowlEffect>(db, Howl);
            added += Add<HowlPackEffect>(db, HowlPack);
            added += Add<HowlDreadEffect>(db, HowlDread);
            added += Add<AlphaEffect>(db, Alpha);
            if (added > 0) FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] {added} status effect template(s) added to ObjectDB");
        }

        private static int Add<T>(ObjectDB db, string name) where T : CompanionStatusEffectBase
        {
            if (db.GetStatusEffect(name.GetStableHashCode()) != null) return 0;
            var effect = ScriptableObject.CreateInstance<T>();
            effect.name = name;
            // 10-14 (SE icon inventory: Dread is added by hash, its template empty): the readable name + its family icon.
            effect.m_name = effect.DisplayName;
            effect.m_tooltip = effect.Description;
            effect.EffectIcon = effect.m_icon = UI.ClassIcons.ForEffect(name);
            db.m_StatusEffects.Add(effect);
            return 1;
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class WerewolfEffectsRegisterPatch
    {
        [HarmonyPostfix]
        private static void AfterAwake(ObjectDB __instance) => WerewolfEffects.Register(__instance);
    }

    /// <summary>§3 "While transformed": +20% move speed, +25% stamina regen, +20% jump, 15% less slash, 20% more fire taken.</summary>
    public class WolfFormEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Wolf Form";
        public override string Description => "Transformed: faster, stronger lungs and legs; claws instead of weapons. Weak to fire.";

        public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir) =>
            speed += baseSpeed * WerewolfConfig.Value(WerewolfConfig.MoveSpeed, 0.20f);

        // Vanilla SE_Stats' form for a bonus: added to the multiplier.
        public override void ModifyStaminaRegen(ref float staminaRegen) =>
            staminaRegen += WerewolfConfig.Value(WerewolfConfig.StaminaRegen, 0.25f);

        public override void ModifyJump(Vector3 baseJump, ref Vector3 jump) =>
            jump += baseJump * WerewolfConfig.Value(WerewolfConfig.Jump, 0.20f);

        public override void OnDamaged(HitData hit, Character attacker)
        {
            base.OnDamaged(hit, attacker);
            if (hit == null) return;
            hit.m_damage.m_slash *= 1f - WerewolfConfig.Value(WerewolfConfig.SlashResist, 0.15f);
            hit.m_damage.m_fire *= 1f + WerewolfConfig.Value(WerewolfConfig.FireWeakness, 0.20f);
        }
    }

    /// <summary>§4 Howl's ally buff: +15% damage, +10% move speed, +30% stamina regen.</summary>
    public class HowlEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Howl";
        public override string Description => "The pack's howl: more damage, speed and stamina regeneration.";

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            base.ModifyAttack(skill, ref hitData);
            hitData.m_damage.Modify(WerewolfEffects.HowlDamage);
        }

        public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir) =>
            speed += baseSpeed * WerewolfEffects.HowlSpeed;

        public override void ModifyStaminaRegen(ref float staminaRegen) => staminaRegen += WerewolfEffects.HowlStamina;
    }

    /// <summary>Howl with Pack Bond (§5): the same buff and +10% armour.</summary>
    public class HowlPackEffect : HowlEffect
    {
        public override string DisplayName => "Howl (Pack Bond)";
        public override string Description => "The pack's howl: more damage, speed, stamina regeneration and armour.";

        public override void ModifyArmorMods(ref float armor) => armor *= WerewolfEffects.PackArmour;
    }

    /// <summary>§4 / §8.4 the boss's Howl debuff: 10% less damage dealt AND 10% more damage taken, 8 s. No fear, no stagger.</summary>
    public class HowlDreadEffect : CompanionStatusEffectBase
    {
        public HowlDreadEffect() { Duration = 8f; }   // 0.1.127: applied by hash (vanilla SEMan), so the template carries the 8 s

        public override string DisplayName => "Dread";
        public override string Description => "Shaken by a werewolf's howl: deals less damage and takes more.";

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            base.ModifyAttack(skill, ref hitData);
            hitData.m_damage.Modify(WerewolfEffects.DreadDealt);
        }

        public override void OnDamaged(HitData hit, Character attacker)
        {
            base.OnDamaged(hit, attacker);
            if (hit != null) hit.m_damage.Modify(WerewolfEffects.DreadTaken);
        }
    }

    /// <summary>§5 Alpha: the party gains +20% damage (the caster's heal per hit is WerewolfKit's, on the caster's peer).</summary>
    public class AlphaEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Alpha";
        public override string Description => "Led by the Alpha: more damage.";

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            base.ModifyAttack(skill, ref hitData);
            hitData.m_damage.Modify(WerewolfEffects.AlphaDamage);
        }
    }
}
