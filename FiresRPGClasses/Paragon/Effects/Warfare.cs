using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresCore.Possession;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// The shared bits of the Warfare + Fitness passive star effects - the local-player
    /// test, the one Debug line per effect on its first use, and the target classes (boss / elite / beast / tier 3+).
    /// </summary>
    internal static class ParagonFx
    {
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static readonly int FmoTier = "fmo_tier".GetStableHashCode();   // FMO's synced tier (WerewolfKit / ClassKillNetwork read it too)
        private const int EliteTier = 3;                                         // = WerewolfKit.EliteTier

        // Wild animals = DruidNecroPassives.WildAnimals (Beast Tongue's list, copied: it is private there) + vanilla's AnimalsVeg faction.
        private static readonly HashSet<string> WildAnimals = new HashSet<string>
        {
            "Boar", "Boar_piggy", "Deer", "Neck", "Wolf", "Wolf_cub", "Lox", "Lox_Calf", "Bjorn", "Unbjorn", "Asksvin", "Asksvin_hatchling", "Hare",
        };

        internal static Player Me => Player.m_localPlayer;
        internal static bool IsMe(Character c) => c != null && c == Player.m_localPlayer;
        internal static float X(string format) => ParagonEffects.TX(format);

        /// <summary>One Debug line the first time an effect changes something this session.</summary>
        internal static void Once(string format, string detail)
        {
            if (!Logged.Add(format)) return;
            FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] effect live: '{format}' = {X(format) * 100f:0.##} -> {detail}");
        }

        internal static int Tier(Character c)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() && view.GetZDO() != null ? view.GetZDO().GetInt(FmoTier, 0) : 0;
        }

        /// <summary>Elite = FMO tier 3+ (WerewolfKit's Howl rule); without FMO, a 2-star (level 3+) creature.</summary>
        public static bool IsElite(Character c) => c != null && !c.IsPlayer() && (Tier(c) >= EliteTier || c.GetLevel() >= 3);
        public static bool IsBossOrElite(Character c) => c != null && (c.IsBoss() || IsElite(c));

        public static bool IsBeast(Character c) =>
            c != null && !c.IsPlayer() && (c.GetFaction() == Character.Faction.AnimalsVeg || WildAnimals.Contains(global::Utils.GetPrefabName(c.gameObject)));

        /// <summary>
        /// "Tier 3+ monster": Valheim has no creature tier, so the creature's biome tier where it stands (Meadows 1, Black Forest 2,
        /// Swamp 3, Mountain 4, Plains 5, Mistlands 6, Ashlands / Deep North 7), or a boss, or an FMO tier 3+ creature.
        /// </summary>
        public static bool IsTier3Plus(Character c)
        {
            if (c == null || c.IsPlayer() || c.IsTamed()) return false;
            if (c.IsBoss() || Tier(c) >= EliteTier) return true;
            if (WorldGenerator.instance == null) return false;
            Heightmap.Biome b = WorldGenerator.instance.GetBiome(c.transform.position);
            return b == Heightmap.Biome.Swamp || b == Heightmap.Biome.Mountain || b == Heightmap.Biome.Plains || b == Heightmap.Biome.Mistlands
                || b == Heightmap.Biome.AshLands || b == Heightmap.Biome.DeepNorth;
        }

        /// <summary>Wolf Form (WerewolfKit) or Bat Form (VampireKit).</summary>
        public static bool InBeastForm() => WerewolfKit.FormActive || VampireKit.BatActive;
    }

    /// <summary>
    /// The Warfare passive / slottable stars about hits (damage dealt and taken, crits, marks, stagger, ultimates, forsaken
    /// power, adrenaline, the bow / crossbow and blood magic). Hooks: one Character.Damage prefix (after RPGClasses' ClassDamageHooks)
    /// for every outgoing effect, one RPC_Damage prefix for the taken ones, one Humanoid.BlockAttack postfix, CooldownTracker.Start
    /// (the one place an ultimate's cooldown starts), Player.ActivateGuardianPower, SEMan.ModifyAdrenaline, Attack.GetAttackHealth,
    /// Humanoid.GetAttackDrawPercentage and Player.QueueReloadAction. Local player only. Heals: WarfareHeals.cs.
    /// Crit (Valheim has none): a crit is a double-damage hit (PassiveKit3 Deadeye's crit), the crit-damage stars add to the x2.
    /// </summary>
    [ParagonEffectSet]
    internal static class ParagonWarfare
    {
        internal const string Executioner = "+{value}% damage to targets under 25% health";
        internal const string Giantslayer = "+{value}% damage to bosses and elites";
        internal const string MonsterLore = "+{value}% damage to tier 3+ monsters";
        internal const string Delver = "in dungeons: +{value}% damage, half as much less damage taken";
        internal const string Gravebane = "+{value}% damage to undead";
        internal const string LongShot = "+{value}% damage to targets beyond 20 m";
        internal const string Huntsman = "+{value}% damage to beasts";
        internal const string Exploiter = "+{value}% damage to staggered enemies";
        internal const string Bloodlust = "+{value}% damage for 5 s after you heal";
        internal const string HuntersMark = "crits mark the target for 6 s: +{value}% damage to it";
        internal const string Stonebreaker = "after you block a boss hit, your next hit deals +{value}%";
        internal const string Momentum = "hits within 3 s stack (max 10): +{value}% damage per stack";
        internal const string Wrath = "ultimates deal +{value}% damage and refund a fifth as much adrenaline";
        internal const string KeenEye = "+{value}% crit chance";
        internal const string Weakpoint = "+{value}% crit chance against bosses";
        internal const string DeadlyStrikes = "+{value}% crit damage";
        internal const string Predator = "+{value}% crit damage against bosses and elites";
        internal const string DeadlyAim = "the first hit within 2 s of a dodge crits, +{value}% crit damage";
        internal const string Backstabber = "+{value}% sneak attack damage";
        internal const string HeavyHand = "+{value}% stagger damage dealt";
        internal const string StavingDeath = "-{value}% damage taken from bosses and elites";
        internal const string UltimateCd = "-{value}% ultimate cooldown";
        internal const string Adrenal = "+{value}% adrenaline gain";
        internal const string ForsakenEcho = "+{value}% forsaken power duration";
        internal const string BaneCrown = "each 10% of boss health you remove: -{value}% forsaken power cooldown";
        internal const string BloodPrice = "-{value}% blood magic health cost";
        internal const string SteadyDraw = "+{value}% bow draw and crossbow reload speed";
        internal const string BatsEye = "in Bat Form, hostiles within 10 + {value} m are marked";

        public static readonly string[] Formats =
        {
            Executioner, Giantslayer, MonsterLore, Delver, Gravebane, LongShot, Huntsman, Exploiter, Bloodlust, HuntersMark, Stonebreaker,
            Momentum, Wrath, KeenEye, Weakpoint, DeadlyStrikes, Predator, DeadlyAim, Backstabber, HeavyHand, StavingDeath, UltimateCd,
            Adrenal, ForsakenEcho, BaneCrown, BloodPrice, SteadyDraw, BatsEye,
        };

        private const float MomentumWindow = 3f, MarkSeconds = 6f, BloodlustSeconds = 5f, AimWindow = 2f, UltimateWindow = 3f;
        private const int MomentumMax = 10;
        private const float CritBase = 2f;   // a crit = double damage (PassiveKit3 Deadeye)

        private static float X(string f) => ParagonFx.X(f);

        // state
        private static readonly Dictionary<Character, float> Marks = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, int> BossBands = new Dictionary<Character, int>();
        private static int _stacks;
        private static float _lastHitAt = -99f, _ultUntil = -99f, _aimDodgeUsed = -99f;
        private static bool _stoneArmed;
        internal static float HealedAt = -99f;                    // WarfareHeals: the local player's last heal
        internal static Character LastVictim;                     // Red Covenant (WarfareHeals) reads the killing blow
        internal static float LastDealt, LastDealtAt = -99f;
        internal static bool SendingHit;                          // inside the local player's Character.Damage prefixes (life-steal window)

        public static int MomentumStacks => _stacks;
        public static bool StonebreakerArmed => _stoneArmed;
        public static float UltimateWindowLeft => Mathf.Max(0f, _ultUntil - Time.time);

        // the target / moment conditions of the damage bonuses (an int so a test can pass them by reflection)
        public const int LowHealth = 1, BossElite = 2, Tier3 = 4, Inside = 8, Undead = 16, Far = 32, Beast = 64, Staggered = 128,
            AfterHeal = 256, Marked = 512, Stone = 1024, Ultimate = 2048;
        private static readonly (int cond, string format)[] Bonuses =
        {
            (LowHealth, Executioner), (BossElite, Giantslayer), (Tier3, MonsterLore), (Inside, Delver), (Undead, Gravebane), (Far, LongShot),
            (Beast, Huntsman), (Staggered, Exploiter), (AfterHeal, Bloodlust), (Marked, HuntersMark), (Stone, Stonebreaker), (Ultimate, Wrath),
        };

        /// <summary>The additive damage bonus (1 = none) for the conditions <paramref name="cond"/> and <paramref name="stacks"/> Berserk Momentum stacks.</summary>
        public static float Bonus(int cond, int stacks)
        {
            float add = 0f;
            foreach (var (c, f) in Bonuses) if ((cond & c) != 0) add += X(f);
            return 1f + add + X(Momentum) * Mathf.Clamp(stacks, 0, MomentumMax);
        }

        /// <summary>The conditions that hold for the local player's hit on <paramref name="victim"/> now.</summary>
        public static int Conditions(Player me, Character victim)
        {
            int c = 0;
            if (victim.GetHealthPercentage() < 0.25f) c |= LowHealth;
            if (ParagonFx.IsBossOrElite(victim)) c |= BossElite;
            if (ParagonFx.IsTier3Plus(victim)) c |= Tier3;
            if (me.InInterior()) c |= Inside;
            if (victim.GetFaction() == Character.Faction.Undead) c |= Undead;
            if (Vector3.Distance(me.transform.position, victim.transform.position) > 20f) c |= Far;
            if (ParagonFx.IsBeast(victim)) c |= Beast;
            if (victim.IsStaggering()) c |= Staggered;
            if (Time.time <= HealedAt + BloodlustSeconds) c |= AfterHeal;
            if (Marks.TryGetValue(victim, out float until) && Time.time <= until) c |= Marked;
            if (_stoneArmed) c |= Stone;
            if (Time.time <= _ultUntil) c |= Ultimate;
            return c;
        }

        /// <summary>The additive damage bonus of the local player's hit on <paramref name="victim"/> (1 = none). consume: this is a real hit.</summary>
        public static float DamageFactor(Character victim, bool consume)
        {
            Player me = ParagonFx.Me;
            if (me == null || victim == null || victim == me) return 1f;
            int cond = Conditions(me, victim);
            if (consume)
            {
                if (X(Stonebreaker) > 0f) _stoneArmed = false;
                if (X(Momentum) > 0f)
                {
                    _stacks = Time.time - _lastHitAt <= MomentumWindow ? Mathf.Min(MomentumMax, _stacks + 1) : 0;
                    _lastHitAt = Time.time;
                    if (_stacks > 0) ParagonFx.Once(Momentum, $"{_stacks} stack(s)");
                }
                foreach (var (c, f) in Bonuses) if ((cond & c) != 0 && X(f) > 0f) ParagonFx.Once(f, $"{victim.m_name} (condition {c})");
            }
            return Bonus(cond, X(Momentum) > 0f ? _stacks : 0);
        }

        /// <summary>Crit chance (boss: + Weakpoint). A heal rolls with boss = false (Keen Eye only).</summary>
        public static float CritChance(bool boss) => Mathf.Clamp01(X(KeenEye) + (boss ? X(Weakpoint) : 0f));

        /// <summary>A crit's damage multiplier: x2, + Deadly Strikes, + Predator's Patience on bosses/elites, + Deadly Aim when it forced the crit.</summary>
        public static float CritMultiplier(bool bossElite, bool aimForced) =>
            CritBase + X(DeadlyStrikes) + (bossElite ? X(Predator) : 0f) + (aimForced ? X(DeadlyAim) : 0f);

        /// <summary>Deadly Aim: the first hit within 2 s of a dodge (FitnessPassive's dodge edge) is a crit.</summary>
        public static bool AimReady() => X(DeadlyAim) > 0f && ParagonFitness.LastDodgeAt > _aimDodgeUsed && Time.time - ParagonFitness.LastDodgeAt <= AimWindow;

        public static void MarkFor(Character c, float seconds) { if (c != null) Marks[c] = Time.time + seconds; }

        /// <summary>The local player's outgoing hit, after ClassDamageHooks: bonuses, crit, sneak attack, stagger, Bane Crown, life steal.</summary>
        internal static void OnHitSending(Character victim, HitData hit)
        {
            Player me = ParagonFx.Me;
            if (me == null || victim == null || victim == me || hit == null || hit.m_attacker != me.GetZDOID()) return;
            float f = DamageFactor(victim, true);
            bool forced = AimReady();
            if (forced) { _aimDodgeUsed = ParagonFitness.LastDodgeAt; ParagonFx.Once(DeadlyAim, "hit within 2 s of a dodge"); }
            float chance = CritChance(victim.IsBoss());
            bool crit = forced || (chance > 0f && Random.value < chance);
            if (crit)
            {
                f *= CritMultiplier(ParagonFx.IsBossOrElite(victim), forced);
                if (X(KeenEye) > 0f) ParagonFx.Once(KeenEye, "a crit");
                if (victim.IsBoss() && X(Weakpoint) > 0f) ParagonFx.Once(Weakpoint, "a crit on a boss");
                if (X(DeadlyStrikes) > 0f) ParagonFx.Once(DeadlyStrikes, "crit damage");
                if (X(Predator) > 0f && ParagonFx.IsBossOrElite(victim)) ParagonFx.Once(Predator, "crit on a boss/elite");
                if (X(HuntersMark) > 0f) MarkFor(victim, MarkSeconds);
            }
            if (f != 1f) hit.m_damage.Modify(f);
            if (X(Backstabber) > 0f && hit.m_backstabBonus > 1f) { hit.m_backstabBonus = BackstabBonus(hit.m_backstabBonus); ParagonFx.Once(Backstabber, "sneak attack bonus x" + hit.m_backstabBonus.ToString("0.##")); }
            if (X(HeavyHand) > 0f) { hit.m_staggerMultiplier = StaggerDealt(hit.m_staggerMultiplier); ParagonFx.Once(HeavyHand, "stagger x" + hit.m_staggerMultiplier.ToString("0.##")); }
            if (X(BaneCrown) > 0f && victim.IsBoss()) BaneCrownOn(me, victim);
            LastVictim = victim; LastDealt = hit.GetTotalDamage(); LastDealtAt = Time.time;
            ParagonWarfareHeals.LifeSteal(me, victim, hit);
        }

        /// <summary>Bane Crown: each 10% band of the boss's health gone since the first hit seen takes x% of the full forsaken cooldown off.</summary>
        private static void BaneCrownOn(Player me, Character boss)
        {
            int band = Mathf.FloorToInt((1f - boss.GetHealthPercentage()) * 10f + 0.0001f);
            if (!BossBands.TryGetValue(boss, out int last)) { BossBands[boss] = band; return; }
            if (band <= last) return;
            BossBands[boss] = band;
            float full = me.m_guardianSE != null ? me.m_guardianSE.m_cooldown : 0f;
            float cut = full * X(BaneCrown) * (band - last);
            me.m_guardianPowerCooldown = Mathf.Max(0f, me.m_guardianPowerCooldown - cut);
            ParagonFx.Once(BaneCrown, $"{band - last} band(s): -{cut:0.#} s forsaken cooldown");
        }

        public static float BaneCrownCut(int bands, float fullCooldown) => fullCooldown * X(BaneCrown) * bands;

        /// <summary>Damage the local player takes: bosses/elites (Staving Death) and in dungeons (half Dungeon Delver), one Asymptote (knee 30%, max 60%).</summary>
        public static float TakenFactor(bool bossElite, bool inside)
        {
            float raw = 0f;
            if (bossElite) raw += X(StavingDeath);
            if (inside) raw += X(Delver) * 0.5f;
            if (raw <= 0f) return 1f;
            return 1f - FiresCore.Curves.SoftCap.Asymptote(raw, 0.30f, 0.60f);
        }

        internal static void OnHitTaken(Character victim, HitData hit)
        {
            if (!ParagonFx.IsMe(victim) || hit == null) return;
            float f = TakenFactor(ParagonFx.IsBossOrElite(hit.GetAttacker()), victim.InInterior());
            if (f >= 1f) return;
            hit.m_damage.Modify(f);
            ParagonFx.Once(ParagonFx.IsBossOrElite(hit.GetAttacker()) ? StavingDeath : Delver, $"damage taken x{f:0.###}");
        }

        /// <summary>An ultimate's cooldown after Ultimate Mastery (BalanceConfig's CDR knee/limit, as CooldownTracker.Scaled).</summary>
        public static float UltimateCooldown(float seconds)
        {
            float x = X(UltimateCd);
            if (x <= 0f || seconds <= 0f) return seconds;
            return seconds * (1f - FiresCore.Curves.SoftCap.Asymptote(x, BalanceConfig.Value(BalanceConfig.CooldownReductionKnee, 0.40f),
                BalanceConfig.Value(BalanceConfig.CooldownReductionLimit, 0.80f)));
        }

        /// <summary>CooldownTracker.Start for an ultimate (ClassCombatDriver.CastUltimate, right after Pay): cooldown, Wrath's window + refund.</summary>
        internal static void OnUltimateStart(string id, ref float seconds)
        {
            UltimateDefinition def = UltimateCatalog.Get(id);
            Player me = ParagonFx.Me;
            if (def == null || me == null) return;
            float was = seconds;
            seconds = UltimateCooldown(seconds);
            if (seconds != was) ParagonFx.Once(UltimateCd, $"{id} cooldown {was:0.#} -> {seconds:0.#} s");
            float w = X(Wrath);
            if (w <= 0f) return;
            _ultUntil = Time.time + UltimateWindow;
            if (def.Cost != null && def.Cost.Resource == UltimateResource.Adrenaline)
            {
                ClassPlayerData data = ClassPlayerStore.Local;
                float cost = UltimateController.EffectiveCost(def, data != null ? data.UltimateLevel(id) : 0);
                float refund = cost * w * 0.2f;
                me.m_adrenaline = Mathf.Min(me.GetMaxAdrenaline(), me.m_adrenaline + refund);   // straight in: no gain multipliers on a refund
                ParagonFx.Once(Wrath, $"{id}: {refund:0.#} adrenaline refunded, +{w * 100f:0.#}% damage for {UltimateWindow:0} s");
            }
        }

        /// <summary>Backstabber: vanilla's sneak-attack multiplier (when > 1) x (1 + value).</summary>
        public static float BackstabBonus(float bonus) => bonus > 1f ? bonus * (1f + X(Backstabber)) : bonus;
        /// <summary>Heavy Hand: the hit's stagger multiplier x (1 + value).</summary>
        public static float StaggerDealt(float mult) => mult * (1f + X(HeavyHand));
        public static float AdrenalineFactor() => 1f + X(Adrenal);
        public static float ForsakenDurationFactor() => 1f + X(ForsakenEcho);
        public static float BloodCostFactor() => 1f - Mathf.Clamp(X(BloodPrice), 0f, 0.9f);   // the star's own Asymptote stops at 40%
        public static float DrawSpeedFactor() => 1f + X(SteadyDraw);
        public static float BatsEyeRange() => 10f + X(BatsEye) * 100f;   // a flat amount: TX = value / 100

        internal static void BlockedBy(Character attacker)
        {
            if (X(Stonebreaker) > 0f && attacker != null && attacker.IsBoss()) _stoneArmed = true;
        }

        private static float _nextBatScan;
        /// <summary>Bat's Eye, once a second from ParagonFitness' tick: hostiles near the bat get Core's Hunter's Mark for 2 s.</summary>
        internal static void Tick(Player me)
        {
            if (Time.time < _nextBatScan) return;
            _nextBatScan = Time.time + 1f;
            if (Marks.Count > 64) { var dead = new List<Character>(); foreach (var kv in Marks) if (kv.Key == null || kv.Value < Time.time) dead.Add(kv.Key); foreach (var d in dead) Marks.Remove(d); }
            if (BossBands.Count > 16) BossBands.Clear();
            if (X(BatsEye) <= 0f || !VampireKit.BatActive || !PossessionApi.IsPossessing(me, out Character bat) || bat == null) return;
            float r = BatsEyeRange();
            int n = 0;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == bat || c == me || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                if (Vector3.Distance(c.transform.position, bat.transform.position) > r) continue;
                if (AbilityRPCManager.ApplySingleEffect(me, c, StatusEffectManager.EFFECT_HUNTERS_MARK, 2f)) n++;
            }
            if (n > 0) ParagonFx.Once(BatsEye, $"{n} hostile(s) marked within {r:0.#} m");
        }
    }

    // ── hooks (Warfare) ──

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class ParagonWarfareDamageHooks
    {
        // First: opens the life-steal window (VampireKit / WerewolfKit heal the player inside ClassDamageHooks' prefix).
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Open(HitData hit)
        {
            Player me = Player.m_localPlayer;
            ParagonWarfare.SendingHit = me != null && hit != null && hit.m_attacker == me.GetZDOID();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class ParagonWarfareDamageHooksLate
    {
        // Low: after ClassDamageHooks (Normal), so the crit doubles the class-scaled hit.
        [HarmonyPrefix, HarmonyPriority(Priority.Low)]
        private static void Before(Character __instance, HitData hit)
        {
            try { ParagonWarfare.OnHitSending(__instance, hit); }
            finally { ParagonWarfare.SendingHit = false; }
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class ParagonWarfareTakenHooks
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Low)]
        private static void Before(Character __instance, HitData hit) => ParagonWarfare.OnHitTaken(__instance, hit);
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class ParagonWarfareBlockHooks
    {
        [HarmonyPostfix]
        private static void After(Humanoid __instance, Character attacker, bool __result)
        {
            if (__result && ParagonFx.IsMe(__instance)) ParagonWarfare.BlockedBy(attacker);
        }
    }

    [HarmonyPatch(typeof(CooldownTracker), nameof(CooldownTracker.Start))]
    internal static class ParagonWarfareCooldownHooks
    {
        [HarmonyPrefix]
        private static void Before(string nodeId, ref float seconds) => ParagonWarfare.OnUltimateStart(nodeId, ref seconds);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ActivateGuardianPower))]
    internal static class ParagonWarfareGuardianHooks
    {
        [HarmonyPrefix]
        private static void Before(Player __instance, out bool __state) =>
            __state = ParagonFx.IsMe(__instance) && __instance.m_guardianPowerCooldown <= 0f && __instance.m_guardianSE != null;

        [HarmonyPostfix]
        private static void After(Player __instance, bool __state)
        {
            if (!__state) return;
            float f = ParagonWarfare.ForsakenDurationFactor();
            if (f == 1f) return;
            StatusEffect se = __instance.GetSEMan().GetStatusEffect(__instance.m_guardianSE.NameHash());
            if (se == null) return;
            se.m_ttl *= f;
            ParagonFx.Once(ParagonWarfare.ForsakenEcho, $"{__instance.m_guardianSE.name} {se.m_ttl / f:0} -> {se.m_ttl:0} s");
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAdrenaline))]
    internal static class ParagonWarfareAdrenalineHooks
    {
        [HarmonyPostfix]
        private static void After(SEMan __instance, ref float use)
        {
            if (use <= 0f || !ParagonFx.IsMe(__instance.m_character)) return;
            float f = ParagonWarfare.AdrenalineFactor();
            if (f == 1f) return;
            use *= f;
            ParagonFx.Once(ParagonWarfare.Adrenal, $"adrenaline gain x{f:0.###}");
        }
    }

    [HarmonyPatch(typeof(Attack), "GetAttackHealth")]
    internal static class ParagonWarfareBloodCostHooks
    {
        [HarmonyPostfix]
        private static void After(Attack __instance, ref float __result)
        {
            if (__result <= 0f || !ParagonFx.IsMe(__instance.m_character)) return;
            if (__instance.m_weapon == null || __instance.m_weapon.m_shared.m_skillType != Skills.SkillType.BloodMagic) return;
            float f = ParagonWarfare.BloodCostFactor();
            if (f == 1f) return;
            __result *= f;
            ParagonFx.Once(ParagonWarfare.BloodPrice, $"health cost x{f:0.###}");
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
    internal static class ParagonWarfareDrawHooks
    {
        // draw% = drawTime / duration (clamped), so x(1+v) on the fraction = a (1+v) faster draw
        [HarmonyPostfix]
        private static void After(Humanoid __instance, ref float __result)
        {
            if (__result <= 0f || __result >= 1f || !ParagonFx.IsMe(__instance)) return;
            float f = ParagonWarfare.DrawSpeedFactor();
            if (f == 1f) return;
            __result = Mathf.Min(1f, __result * f);
            ParagonFx.Once(ParagonWarfare.SteadyDraw, $"bow draw x{f:0.###}");
        }
    }

    [HarmonyPatch(typeof(Player), "QueueReloadAction")]
    internal static class ParagonWarfareReloadHooks
    {
        [HarmonyPostfix]
        private static void After(Player __instance)
        {
            if (!ParagonFx.IsMe(__instance) || __instance.m_actionQueue == null || __instance.m_actionQueue.Count == 0) return;
            float f = ParagonWarfare.DrawSpeedFactor();
            if (f == 1f) return;
            var a = __instance.m_actionQueue[__instance.m_actionQueue.Count - 1];
            if (a == null || a.m_type != Player.MinorActionData.ActionType.Reload || a.m_time > 0f) return;
            a.m_duration /= f;
            ParagonFx.Once(ParagonWarfare.SteadyDraw, $"crossbow reload {a.m_duration * f:0.##} -> {a.m_duration:0.##} s");
        }
    }
}
