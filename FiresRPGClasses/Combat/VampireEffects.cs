using FiresCore.Npc.Archetypes.StatusEffects;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The Vampire's status effects (Tools\VAMPIRE_CLASS_DESIGN.md §4, §5, §12), registered into ObjectDB beside Core's and applied
    /// through Core's AbilityRPCManager by name (the Werewolf's path: any CompanionStatusEffectBase template in ObjectDB). Each works on
    /// the peer that owns the character it sits on. Numbers come from [12 - Vampire], synced from the server.
    /// </summary>
    internal static class VampireEffects
    {
        internal const string Sunlight = "ClassSunlight";
        internal const string NightStalker = "ClassNightStalker";
        internal const string BloodFrenzy = "ClassBloodFrenzy";
        internal const string CrimsonTide = "ClassCrimsonTide";
        internal const string SunWard = "RPG_SunWard";

        internal static readonly int SunlightHash = Sunlight.GetStableHashCode();
        internal static readonly int NightStalkerHash = NightStalker.GetStableHashCode();
        internal static readonly int BloodFrenzyHash = BloodFrenzy.GetStableHashCode();
        internal static readonly int CrimsonTideHash = CrimsonTide.GetStableHashCode();
        internal static readonly int SunWardHash = SunWard.GetStableHashCode();

        /// <summary>Adds the templates to this ObjectDB once (ObjectDB.Awake runs again on every world load).</summary>
        internal static void Register(ObjectDB db)
        {
            if (db == null) return;
            int added = 0;
            added += Add<SunlightEffect>(db, Sunlight);
            added += Add<NightStalkerEffect>(db, NightStalker);
            added += Add<BloodFrenzyEffect>(db, BloodFrenzy);
            added += Add<CrimsonTideEffect>(db, CrimsonTide);
            added += Add<SunWardEffect>(db, SunWard);
            if (added > 0) FiresRPGClassesPlugin.Log?.LogDebug($"[Vampire] {added} status effect template(s) added to ObjectDB");
        }

        private static int Add<T>(ObjectDB db, string name) where T : CompanionStatusEffectBase
        {
            if (db.GetStatusEffect(name.GetStableHashCode()) != null) return 0;
            var effect = ScriptableObject.CreateInstance<T>();
            effect.name = name;
            effect.m_name = effect.DisplayName;       // 0.1.132: the template never shows the internal id
            effect.m_tooltip = effect.Description;
            // 10-14 (SE icon inventory: Sunburnt is added by hash, so the template's icon is the only one it ever gets):
            // Core's Setup copies EffectIcon into m_icon, and a hash add clones the template, so set both here.
            effect.EffectIcon = effect.m_icon = UI.ClassIcons.ForEffect(name);
            db.m_StatusEffects.Add(effect);
            return 1;
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class VampireEffectsRegisterPatch
    {
        [HarmonyPostfix]
        private static void AfterAwake(ObjectDB __instance)
        {
            VampireEffects.Register(__instance);
            VampireFood.RegisterItem(__instance);   // Duskbloom Stew + its Cauldron recipe (§12)
            VampireSkill.Register(true);            // the Bat Form skill's icon, once sprites can be made
        }
    }

    /// <summary>§5 mild sunlight: 10% less stamina regeneration (Life Drain's "off by day" is VampireKit's).</summary>
    public class SunlightEffect : CompanionStatusEffectBase
    {
        // 0.1.132 (Fire's shot: "Human granted you ClassSunlight (86400s)"): a readable name, no timer (removed on leaving the sun).
        public override string DisplayName => "Sunburnt";
        public override string Description => "In direct sunlight: -10% stamina regeneration and no Life Drain. A Duskbloom Stew wards it off.";

        public override void Setup(Character character)
        {
            base.Setup(character);
            m_ttl = 0f;   // permanent while applied; VampireKit removes it out of the sun
        }

        public override void ModifyStaminaRegen(ref float staminaRegen) =>
            staminaRegen *= 1f - VampireConfig.Value(VampireConfig.SunStaminaPenalty, 0.10f);
    }

    /// <summary>§4 Night Stalker at night: harder to notice (+15% stealth; the +10% damage is VampireKit's outgoing hit).</summary>
    public class NightStalkerEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Night Stalker";
        public override string Description => "The night hides you: harder to notice, more damage.";

        // Vanilla's stealth factor is how noticeable you are (SE_Stats adds base x modifier): lower = stealthier.
        public override void ModifyStealth(float baseStealth, ref float stealth) =>
            stealth -= baseStealth * VampireConfig.Value(VampireConfig.StalkerStealth, 0.15f);
    }

    /// <summary>§4 Blood Frenzy: life-steal x2 (VampireKit) and +15% attack speed (AttributeEffects' factor hook, VampireKit).</summary>
    public class BloodFrenzyEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Blood Frenzy";
        public override string Description => "Frenzied: double life-steal, faster attacks.";
    }

    /// <summary>§4 Crimson Tide (ultimate): every hit by the holder life-steals 10% (each holder's own client heals itself, VampireKit).</summary>
    public class CrimsonTideEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Crimson Tide";
        public override string Description => "Every hit you land heals you a share of its damage.";
    }

    /// <summary>§12 Duskbloom Stew on a vampire: the daylight penalty is off (mild: stamina and Life Drain as at night; harsh: no burn).</summary>
    public class SunWardEffect : CompanionStatusEffectBase
    {
        public override string DisplayName => "Sun Ward";
        public override string Description => "Duskbloom Stew shields you from the sun while it lasts.";
    }
}
