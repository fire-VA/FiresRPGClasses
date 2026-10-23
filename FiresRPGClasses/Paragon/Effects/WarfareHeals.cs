using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Combat;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// The Warfare stars about healing. Hooks: one AbilityHeals.Apply prefix (after RPGClasses' ClassHealHooks; every
    /// class heal and life-steal heal goes through it), the life steal from ParagonWarfare's hit hook, one Game.RPC_RegisterKill postfix
    /// (the local player's kill credit, as ClassKillHooks) and the heal-over-time scale from ParagonFitness' SE_Stats.Setup hook.
    /// </summary>
    [ParagonEffectSet]
    internal static class ParagonWarfareHeals
    {
        private const float TriageHealthShare = 0.35f;   // Triage's "under 35% health" threshold, matches its effect text

        internal const string Mender = "+{value}% healing done";
        internal const string Triage = "+{value}% healing on targets under 35% health";
        internal const string Renewing = "+{value}% heal-over-time strength";
        internal const string LifeStealF = "+{value}% life steal";
        internal const string SharedVigour = "your heals also restore {value}% of the heal as stamina";
        internal const string Overflow = "life-steal overheal becomes a shield, up to {value}% max health";
        internal const string FieldMedic = "your crit chance applies to heals; heal crits +{value}%";
        internal const string RedCovenant = "killing blows heal allies within 10 m for {value}% of the damage";

        public static readonly string[] Formats = { Mender, Triage, Renewing, LifeStealF, SharedVigour, Overflow, FieldMedic, RedCovenant };

        private const float ShieldSeconds = 10f, CovenantReach = 10f, KillMemory = 2f;
        internal const string ShieldName = "RPG_ParagonOverflow";
        private static readonly int ShieldHash = ShieldName.GetStableHashCode();
        private static SE_Shield _shieldTemplate;
        private static bool _inLifeSteal;

        private static float X(string f) => ParagonFx.X(f);

        /// <summary>The heal factor of a heal by the local player on <paramref name="target"/>, before a crit.</summary>
        public static float HealFactor(bool under35)
        {
            float f = 1f + X(Mender);
            if (under35) f += X(Triage);
            return f;
        }

        /// <summary>Field Medic: a heal crit (Keen Eye's chance) heals x2 + the star's value.</summary>
        public static float HealCritMultiplier() => 2f + X(FieldMedic);

        public static float HotFactor() => 1f + X(Renewing);
        public static float VigourShare() => X(SharedVigour);
        public static float ShieldCap(Character c) => c != null ? c.GetMaxHealth() * X(Overflow) : 0f;
        /// <summary>The Crimson Overflow shield's new absorb: what is left + the overheal, up to x% max health.</summary>
        public static float ShieldAfter(Character c, float left, float overheal) => Mathf.Min(ShieldCap(c), Mathf.Max(0f, left) + Mathf.Max(0f, overheal));

        internal static void BeforeHeal(Character healer, Character target, ref float amount)
        {
            if (!ParagonFx.IsMe(healer) || target == null || amount <= 0f) return;
            Player me = (Player)healer;
            bool lifeSteal = _inLifeSteal || (ParagonWarfare.SendingHit && target == me);
            float was = amount;
            amount *= HealFactor(target.GetHealthPercentage() < TriageHealthShare);
            if (amount != was) { if (X(Mender) > 0f) ParagonFx.Once(Mender, $"heal {was:0.#} -> {amount:0.#}"); if (target.GetHealthPercentage() < TriageHealthShare && X(Triage) > 0f) ParagonFx.Once(Triage, "target under 35%"); }
            if (X(FieldMedic) > 0f)
            {
                float chance = ParagonWarfare.CritChance(false);
                if (chance > 0f && Random.value < chance) { amount *= HealCritMultiplier(); ParagonFx.Once(FieldMedic, $"heal crit x{HealCritMultiplier():0.##}"); }
            }
            float v = VigourShare();
            if (v > 0f)
            {
                float st = amount * v;
                if (target == me) me.AddStamina(st);
                else if (target is Player other && other.m_nview != null && other.m_nview.IsValid()) other.m_nview.InvokeRPC("UseStamina", -st);   // vanilla's only remote stamina RPC
                ParagonFx.Once(SharedVigour, $"{st:0.#} stamina from a {amount:0.#} heal");
            }
            if (!lifeSteal) ParagonWarfare.HealedAt = Time.time;   // Bloodlust: "after you heal" = a heal you cast, not life steal
            if (lifeSteal && target == me && X(Overflow) > 0f)
            {
                float over = amount - (me.GetMaxHealth() - me.GetHealth());
                if (over > 0f) AddShield(me, over);
            }
        }

        private static void AddShield(Player me, float over)
        {
            SEMan seman = me.GetSEMan();
            if (seman == null) return;
            if (seman.GetStatusEffect(ShieldHash) is SE_Shield have)
            {
                float left = have.m_totalAbsorbDamage - have.m_damage;
                have.m_totalAbsorbDamage = have.m_damage + ShieldAfter(me, left, over);
                have.m_time = 0f;
                return;
            }
            if (_shieldTemplate == null)
            {
                _shieldTemplate = ScriptableObject.CreateInstance<SE_Shield>();
                _shieldTemplate.name = ShieldName;
                _shieldTemplate.m_name = "Crimson Overflow";
                _shieldTemplate.m_tooltip = "Life-steal overheal, held as a shield.";
                _shieldTemplate.m_absorbDamageWorldLevel = 0f;
            }
            _shieldTemplate.m_ttl = ShieldSeconds;
            _shieldTemplate.m_absorbDamage = ShieldAfter(me, 0f, over);
            if (_shieldTemplate.m_absorbDamage <= 0f) return;
            seman.AddStatusEffect(_shieldTemplate, true);
            ParagonFx.Once(Overflow, $"{over:0.#} overheal -> a {_shieldTemplate.m_absorbDamage:0.#} shield");
        }

        /// <summary>Sanguine Thirst: x% of the hit as a heal (VampireKit's life-steal rules: enemies only, half on bosses, Soft knee 5% max health).</summary>
        public static float LifeStealHeal(Player me, Character victim, float dealt)
        {
            float x = X(LifeStealF);
            if (x <= 0f || me == null || dealt <= 0f) return 0f;
            float heal = dealt * x;
            if (victim != null && victim.IsBoss()) heal *= VampireConfig.Value(VampireConfig.DrainBossShare, 0.5f);
            return FiresCore.Curves.SoftCap.Soft(heal, me.GetMaxHealth() * VampireConfig.Value(VampireConfig.DrainCap, 0.05f));
        }

        internal static void LifeSteal(Player me, Character victim, HitData hit)
        {
            if (X(LifeStealF) <= 0f || victim.IsTamed() || !ClassTargeting.IsEnemyTarget(me, victim)) return;
            float heal = LifeStealHeal(me, victim, hit.GetTotalDamage());
            if (heal <= 0f) return;
            _inLifeSteal = true;
            try { AbilityHeals.Apply(me, me, heal); }
            finally { _inLifeSteal = false; }
            ParagonFx.Once(LifeStealF, $"{heal:0.#} healed from {hit.GetTotalDamage():0.#} dealt");
        }

        /// <summary>Red Covenant: the killing blow's damage x the value, to every player within 10 m (the killer too).</summary>
        public static float CovenantHeal(float dealt) => Mathf.Max(0f, dealt) * X(RedCovenant);

        internal static void OnKill()
        {
            Player me = ParagonFx.Me;
            if (me == null || X(RedCovenant) <= 0f || Time.time - ParagonWarfare.LastDealtAt > KillMemory) return;
            float heal = CovenantHeal(ParagonWarfare.LastDealt);
            if (heal <= 0f) return;
            var near = new List<Player>();
            Player.GetPlayersInRange(me.transform.position, CovenantReach, near);
            foreach (Player p in near) if (p != null && !p.IsDead()) AbilityHeals.Apply(me, p, heal);
            ParagonFx.Once(RedCovenant, $"kill: {heal:0.#} to {near.Count} player(s)");
        }

        /// <summary>Renewing Pulse, from the SE_Stats.Setup hook: a healing status effect on the local player heals x% more over time.</summary>
        internal static void ScaleHot(SE_Stats se)
        {
            float f = HotFactor();
            if (f == 1f) return;
            bool any = false;
            if (se.m_healthOverTime > 0f) { se.m_healthOverTime *= f; any = true; }
            if (se.m_healthPerTick > 0f) { se.m_healthPerTick *= f; any = true; }
            if (any) ParagonFx.Once(Renewing, $"{se.name} heal over time x{f:0.###}");
        }
    }

    [HarmonyPatch(typeof(AbilityHeals), nameof(AbilityHeals.Apply))]
    internal static class ParagonWarfareHealHooks
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Low)]
        private static void Before(Character healer, Character target, ref float amount) => ParagonWarfareHeals.BeforeHeal(healer, target, ref amount);
    }

    [HarmonyPatch(typeof(Game), nameof(Game.RPC_RegisterKill))]
    internal static class ParagonWarfareKillHooks
    {
        [HarmonyPostfix]
        private static void After() => ParagonWarfareHeals.OnKill();
    }
}
