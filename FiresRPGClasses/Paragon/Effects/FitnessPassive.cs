using FiresCore.Npc.Archetypes;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// The Fitness PASSIVE star effects. Hooks: Player.GetTotalFoodValue (the pools,
    /// after AttributeEffects' flat adds: Priority.Last), Player.GetBodyArmor (Beast Hide), the SEMan regen reads (as ClassStaminaRegenHooks /
    /// AttributeEffects.HealthRegen), SEMan.ModifyStagger (shared with Warfare's Battle-Hardened), SEMan.ModifyTimedBlockBonus,
    /// SEMan.ApplyStatusEffectSpeedMods (as ClassSpeedHooks), Player.GetEquipmentMovementModifier, SE_Rested.UpdateTTL, Player.ConsumeItem +
    /// SE_Stats.Setup (mead strength; Warfare's Renewing Pulse shares the Setup hook) and Player.UpdateDodge (the dodge edge, shared with
    /// Warfare's Deadly Aim, and the 1 s tick for Spite's count and Warfare's Bat's Eye). Local player only.
    /// </summary>
    [ParagonEffectSet]
    internal static class ParagonFitness
    {
        internal const string Stalwart = "-{value}% move penalty from shields and heavy armour";
        internal const string NightBlood = "+{value}% all regen at night";
        internal const string IronStomach = "+{value}% food health and stamina";
        internal const string Wellspring = "+{value}% max eitr";
        internal const string Hearty = "+{value}% max health";
        internal const string ThickBlood = "+{value}% max health and stamina";
        internal const string DeepLungs = "+{value}% max stamina";
        internal const string MeadSense = "+{value}% mead and potion strength and duration";
        internal const string Parry = "+{value}% parry bonus";
        internal const string Spite = "+{value}% regen per enemy within 8 m (max 3)";
        internal const string Rested = "+{value}% rested duration";
        internal const string Slipstream = "+{value}% speed for 3 s after a dodge";
        internal const string Unshakeable = "+{value}% stagger threshold";
        internal const string Tireless = "+{value}% stamina regen while moving";
        internal const string BeastHide = "in Bat or Wolf Form: +{value}% armour and max health";
        // Warfare's, here because it shares the stagger hook
        internal const string BattleHardened = "+{value}% stagger resistance while attacking";

        public static readonly string[] Formats =
        {
            Stalwart, NightBlood, IronStomach, Wellspring, Hearty, ThickBlood, DeepLungs, MeadSense, Parry, Spite, Rested, Slipstream,
            Unshakeable, Tireless, BeastHide, BattleHardened,
        };

        // For ClassStatsApi (one row per star): which Fitness texts change which Player > Stats stat, as Pools / ArmourFactor apply them.
        // stat = an AttributeEffects.Stat name; food = the share is (eaten foods' part) x fraction, flat (Iron Stomach), else percent;
        // form = only in Bat or Wolf Form (ParagonFx.InBeastForm).
        internal static readonly (string format, string stat, bool food, bool form)[] StatRows =
        {
            (IronStomach, "MaxHealth", true, false), (IronStomach, "MaxStamina", true, false),
            (Hearty, "MaxHealth", false, false), (ThickBlood, "MaxHealth", false, false), (BeastHide, "MaxHealth", false, true),
            (DeepLungs, "MaxStamina", false, false), (ThickBlood, "MaxStamina", false, false),
            (Wellspring, "MaxEitr", false, false),
            (BeastHide, "Armour", false, true),
        };

        private const float SpiteReach = 8f, SlipstreamSeconds = 3f, MovingSpeed = 0.5f;
        private const int SpiteMax = 3;

        public static float LastDodgeAt { get; private set; } = -99f;
        public static int SpiteEnemies { get; private set; }
        private static bool _wasDodging, _consuming;
        private static float _nextTick;

        private static float X(string f) => ParagonFx.X(f);

        // ── pools ──
        /// <summary>The pools after the Fitness stars. foodHp/foodStamina = the eaten foods' current part of hp/stamina.</summary>
        public static void Pools(float foodHp, float foodStamina, bool inForm, ref float hp, ref float stamina, ref float eitr)
        {
            float iron = X(IronStomach);
            hp += foodHp * iron;
            stamina += foodStamina * iron;
            hp *= 1f + X(Hearty) + X(ThickBlood) + (inForm ? X(BeastHide) : 0f);
            stamina *= 1f + X(DeepLungs) + X(ThickBlood);
            eitr *= 1f + X(Wellspring);
        }

        internal static void FoodSums(Player p, out float hp, out float stamina)
        {
            hp = 0f; stamina = 0f;
            if (p?.m_foods == null) return;
            foreach (Player.Food f in p.m_foods) { hp += f.m_health; stamina += f.m_stamina; }
        }

        // ── regen ──
        /// <summary>The regen factor: Night Blood (all), Spite (all, per enemy, max 3), Tireless (stamina, while moving).</summary>
        public static float RegenFactor(bool stamina, bool night, int enemies, bool moving)
        {
            float add = 0f;
            if (night) add += X(NightBlood);
            add += X(Spite) * Mathf.Clamp(enemies, 0, SpiteMax);
            if (stamina && moving) add += X(Tireless);
            return 1f + add;
        }

        internal static float LiveRegen(Character c, bool stamina)
        {
            if (!ParagonFx.IsMe(c)) return 1f;
            bool night = EnvMan.IsNight();
            bool moving = c.GetVelocity().magnitude > MovingSpeed;
            float f = RegenFactor(stamina, night, SpiteEnemies, moving);
            if (f != 1f)
            {
                if (night && X(NightBlood) > 0f) ParagonFx.Once(NightBlood, "night regen");
                if (SpiteEnemies > 0 && X(Spite) > 0f) ParagonFx.Once(Spite, $"{SpiteEnemies} enemy(ies) within 8 m");
                if (stamina && moving && X(Tireless) > 0f) ParagonFx.Once(Tireless, "stamina regen while moving");
            }
            return f;
        }

        // ── stagger / parry / speed / movement / rested ──
        /// <summary>Stagger damage the local player takes: / (1 + threshold), x (1 - resistance while attacking). Both stars stop below 60%.</summary>
        public static float StaggerFactor(bool attacking)
        {
            float f = 1f / (1f + X(Unshakeable));
            if (attacking) f *= 1f - Mathf.Clamp(X(BattleHardened), 0f, 0.6f);
            return f;
        }

        /// <summary>Parry (timed block) bonus: the parry's block power x (1 + value).</summary>
        public static float ParryFactor() => 1f + X(Parry);
        public static float SlipstreamFactor(float sinceDodge) => sinceDodge >= 0f && sinceDodge <= SlipstreamSeconds ? 1f + X(Slipstream) : 1f;
        /// <summary>A negative (penalty) equipment movement modifier x (1 - value); the star's own Asymptote stops at 60%.</summary>
        public static float MoveModifier(float raw) => raw < 0f ? raw * (1f - Mathf.Clamp(X(Stalwart), 0f, 0.9f)) : raw;
        public static float RestedFactor() => 1f + X(Rested);
        public static float ArmourFactor(bool inForm) => inForm ? 1f + X(BeastHide) : 1f;

        // ── mead ──
        /// <summary>Mead Sense on a fresh mead/potion status effect (before its Setup): duration and every heal / regen / up-front amount x (1 + value).</summary>
        public static void ScaleMead(SE_Stats se)
        {
            float f = 1f + X(MeadSense);
            if (f == 1f || se == null) return;
            se.m_ttl *= f;
            se.m_healthOverTime *= f; se.m_staminaOverTime *= f; se.m_eitrOverTime *= f;
            se.m_healthUpFront *= f; se.m_staminaUpFront *= f; se.m_eitrUpFront *= f;
            if (se.m_healthOverTimeDuration > 0f) se.m_healthOverTimeDuration *= f;
            if (se.m_staminaOverTimeDuration > 0f) se.m_staminaOverTimeDuration *= f;
            if (se.m_eitrOverTimeDuration > 0f) se.m_eitrOverTimeDuration *= f;
            if (se.m_healthPerTick > 0f) se.m_healthPerTick *= f;
            if (se.m_healthRegenMultiplier > 1f) se.m_healthRegenMultiplier = 1f + (se.m_healthRegenMultiplier - 1f) * f;
            if (se.m_staminaRegenMultiplier > 1f) se.m_staminaRegenMultiplier = 1f + (se.m_staminaRegenMultiplier - 1f) * f;
            if (se.m_eitrRegenMultiplier > 1f) se.m_eitrRegenMultiplier = 1f + (se.m_eitrRegenMultiplier - 1f) * f;
            ParagonFx.Once(MeadSense, $"{se.name}: x{f:0.###} strength and duration");
        }

        internal static void BeginConsume(Player p, ItemDrop.ItemData item) =>
            _consuming = ParagonFx.IsMe(p) && item?.m_shared != null && item.m_shared.m_consumeStatusEffect != null && item.m_shared.m_food <= 0f;
        internal static void EndConsume() => _consuming = false;

        internal static void OnSetup(SE_Stats se, Character c)
        {
            if (!ParagonFx.IsMe(c) || se == null || se is SE_Rested) return;
            if (_consuming) { _consuming = false; ScaleMead(se); }
            ParagonWarfareHeals.ScaleHot(se);
        }

        // ── tick (dodge edge, Spite's count, Warfare's Bat's Eye) ──
        internal static void Tick(Player me)
        {
            bool d = me.InDodge();
            if (d && !_wasDodging) LastDodgeAt = Time.time;
            _wasDodging = d;
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;
            int n = 0;
            if (X(Spite) > 0f)
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c == me || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                    if (Vector3.Distance(c.transform.position, me.transform.position) <= SpiteReach && ++n >= SpiteMax) break;
                }
            SpiteEnemies = n;
            ParagonWarfare.Tick(me);
        }
    }

    // ── hooks (Fitness) ──

    [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
    internal static class ParagonFitnessFoodHooks
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void After(Player __instance, ref float hp, ref float stamina, ref float eitr)
        {
            if (!ParagonFx.IsMe(__instance)) return;
            float h0 = hp, s0 = stamina, e0 = eitr;
            ParagonFitness.FoodSums(__instance, out float fh, out float fs);
            bool form = ParagonFx.InBeastForm();
            ParagonFitness.Pools(fh, fs, form, ref hp, ref stamina, ref eitr);
            if (hp != h0 || stamina != s0 || eitr != e0)
            {
                if (ParagonFx.X(ParagonFitness.IronStomach) > 0f) ParagonFx.Once(ParagonFitness.IronStomach, $"food hp {fh:0.#} stamina {fs:0.#}");
                if (ParagonFx.X(ParagonFitness.Hearty) > 0f) ParagonFx.Once(ParagonFitness.Hearty, $"max health {h0:0.#} -> {hp:0.#}");
                if (ParagonFx.X(ParagonFitness.ThickBlood) > 0f) ParagonFx.Once(ParagonFitness.ThickBlood, $"health {h0:0.#} -> {hp:0.#}, stamina {s0:0.#} -> {stamina:0.#}");
                if (ParagonFx.X(ParagonFitness.DeepLungs) > 0f) ParagonFx.Once(ParagonFitness.DeepLungs, $"max stamina {s0:0.#} -> {stamina:0.#}");
                if (ParagonFx.X(ParagonFitness.Wellspring) > 0f) ParagonFx.Once(ParagonFitness.Wellspring, $"max eitr {e0:0.#} -> {eitr:0.#}");
                if (form && ParagonFx.X(ParagonFitness.BeastHide) > 0f) ParagonFx.Once(ParagonFitness.BeastHide, "max health in a beast form");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
    internal static class ParagonFitnessArmourHooks
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void After(Player __instance, ref float __result)
        {
            if (!ParagonFx.IsMe(__instance)) return;
            float f = ParagonFitness.ArmourFactor(ParagonFx.InBeastForm());
            if (f != 1f) { __result *= f; ParagonFx.Once(ParagonFitness.BeastHide + " (armour)", $"armour x{f:0.###}"); }
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
    internal static class ParagonFitnessHealthRegenHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float regenMultiplier) => regenMultiplier *= ParagonFitness.LiveRegen(__instance.m_character, false);
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
    internal static class ParagonFitnessStaminaRegenHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float staminaMultiplier) => staminaMultiplier *= ParagonFitness.LiveRegen(__instance.m_character, true);
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyEitrRegen))]
    internal static class ParagonFitnessEitrRegenHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float eitrMultiplier) => eitrMultiplier *= ParagonFitness.LiveRegen(__instance.m_character, false);
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStagger))]
    internal static class ParagonFitnessStaggerHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float use)
        {
            Character c = __instance.m_character;
            if (use <= 0f || !ParagonFx.IsMe(c)) return;
            bool attacking = c.InAttack();
            float f = ParagonFitness.StaggerFactor(attacking);
            if (f == 1f) return;
            use *= f;
            if (ParagonFx.X(ParagonFitness.Unshakeable) > 0f) ParagonFx.Once(ParagonFitness.Unshakeable, $"stagger taken x{f:0.###}");
            if (attacking && ParagonFx.X(ParagonFitness.BattleHardened) > 0f) ParagonFx.Once(ParagonFitness.BattleHardened, $"stagger taken while attacking x{f:0.###}");
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyTimedBlockBonus))]
    internal static class ParagonFitnessParryHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float timedBlockBonus)
        {
            if (!ParagonFx.IsMe(__instance.m_character)) return;
            float f = ParagonFitness.ParryFactor();
            if (f == 1f) return;
            timedBlockBonus *= f;
            ParagonFx.Once(ParagonFitness.Parry, $"parry block power x{f:0.###}");
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
    internal static class ParagonFitnessSpeedHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float speed)
        {
            if (!ParagonFx.IsMe(__instance.m_character)) return;
            float f = ParagonFitness.SlipstreamFactor(Time.time - ParagonFitness.LastDodgeAt);
            if (f == 1f) return;
            speed *= f;
            ParagonFx.Once(ParagonFitness.Slipstream, $"speed x{f:0.###} after a dodge");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetEquipmentMovementModifier))]
    internal static class ParagonFitnessMoveHooks
    {
        [HarmonyPostfix]
        private static void After(Player __instance, ref float __result)
        {
            if (__result >= 0f || !ParagonFx.IsMe(__instance)) return;
            float was = __result;
            __result = ParagonFitness.MoveModifier(__result);
            if (__result != was) ParagonFx.Once(ParagonFitness.Stalwart, $"equipment move modifier {was:0.###} -> {__result:0.###}");
        }
    }

    [HarmonyPatch(typeof(SE_Rested), "UpdateTTL")]
    internal static class ParagonFitnessRestedHooks
    {
        // UpdateTTL sets m_ttl = base + comfort and m_time = 0 when it extends; stretch that fresh ttl.
        [HarmonyPostfix]
        private static void After(SE_Rested __instance)
        {
            if (!ParagonFx.IsMe(__instance.m_character) || !(__instance.m_character is Player p) || __instance.m_time > 0f) return;
            float f = ParagonFitness.RestedFactor();
            if (f == 1f) return;
            float fresh = __instance.m_baseTTL + (p.GetComfortLevel() - 1) * __instance.m_TTLPerComfortLevel;
            if (Mathf.Abs(__instance.m_ttl - fresh) > 0.01f) return;   // not just set (already stretched, or not extended)
            __instance.m_ttl = fresh * f;
            ParagonFx.Once(ParagonFitness.Rested, $"rested {fresh:0} -> {__instance.m_ttl:0} s");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class ParagonFitnessConsumeHooks
    {
        [HarmonyPrefix]
        private static void Before(Player __instance, ItemDrop.ItemData item) => ParagonFitness.BeginConsume(__instance, item);

        [HarmonyPostfix]
        private static void After() => ParagonFitness.EndConsume();
    }

    [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.Setup))]
    internal static class ParagonFitnessSeSetupHooks
    {
        [HarmonyPrefix]
        private static void Before(SE_Stats __instance, Character character) => ParagonFitness.OnSetup(__instance, character);
    }

    [HarmonyPatch(typeof(Player), "UpdateDodge")]
    internal static class ParagonFitnessTickHooks
    {
        [HarmonyPostfix]
        private static void After(Player __instance)
        {
            if (ParagonFx.IsMe(__instance)) ParagonFitness.Tick(__instance);
        }
    }
}
