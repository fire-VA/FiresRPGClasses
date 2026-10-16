using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresCore.Npc.Archetypes.StatusEffects.Expert;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The class passives and automatics on the player while their node is Active in the passive grid (learned, equipped,
    /// conditions met) and off when it is not. The Active set is read once a second; the vanilla hooks (PassiveHooks.cs)
    /// only look it up.
    /// - Core status effects, kept on the way the companion keeps them (ArchetypeAbilitySystem.EnsurePassive: ApplySelfBuff
    ///   with duration 0, only when missing, so a running cooldown or stack count is never reset): Unyielding, Execute,
    ///   Death Wish, Rampage (stacks from the player's kills). Berserk Rage (automatic) is the companion's trigger: 12 s of
    ///   rage below 35% health, then a 20 s cooldown.
    /// - Core's ability text, built for players (Fire, popups 2026-09-28 20:4x and 21:1x): Iron Wall, Parry Mastery,
    ///   Blessed Defense, Bloodlust, Backstab, Assassinate, Chain Casting, Eitr Mastery / Spiritual Connection, Endurance
    ///   Training, Fury, Martial Arts, Steady Aim, Headshot, Emergency Heal; and the companion-AI automatics as small
    ///   bonuses: Shield Block, Blessed Shield, Spellcasting, Healing Staff, Kiting Expertise, Intercept.
    /// </summary>
    public static class PassiveKit
    {
        private const float TickSeconds = 1f;
        private const string RageNodeId = "berserker_rage";
        private const float RageHealthShare = 0.35f;
        private const float RageSeconds = 12f;
        private const float RageCooldownSeconds = 20f;

        // Fire 10-01 "make the passive ranks scale": every Core-backed passive below reads the player's rank in it (rank 1 = the
        // value it always had), the shape of the yaml talents beside them. Ranks are read once a second in Tick (RankIn).
        private const string IronWallNodeId = "tank_iron_wall";
        private static readonly float[] IronWallBlockedCut = { 0.6f, 0.65f, 0.7f };
        private const string ParryNodeId = "tank_parry_restore";
        private const float ParryStaminaBase = 5f;
        private const float ParryStaminaPerLevel = 0.5f;
        private static readonly float[] ParryScale = { 1f, 1.25f, 1.5f };
        // Vanilla's parry window (Humanoid.BlockAttack): a timed-block shield raised under 0.25 s ago.
        private const float ParryWindowSeconds = 0.25f;
        private const string BlessedDefenseNodeId = "paladin_heal_block";
        private static readonly float[] BlessedDefenseChance = { 0.2f, 0.3f, 0.4f };
        private const float BlessedDefenseHealShare = 0.05f;
        private const string ShieldBlockNodeId = "tank_block";
        private const float ShieldBlockPower = 1.2f;
        private const string BlessedShieldNodeId = "paladin_block";
        private const float BlessedShieldPower = 1.1f;
        private const float BlessedShieldChance = 0.1f;
        private const float BlessedShieldHealShare = 0.03f;
        private const string InterceptNodeId = "tank_intercept";
        private const float InterceptRange = 5f;
        private static readonly float[] InterceptCut = { 0.1f, 0.15f, 0.2f };
        private const string InterceptZdoKey = "FiresRPGClasses_Intercept";
        // The guarding player's rank, for the party members' peers (0.1.62; a peer that only sets the bool counts as rank 1).
        private const string InterceptRankZdoKey = "FiresRPGClasses_InterceptRank";

        private const string BloodlustNodeId = "berserker_bloodlust";
        private static readonly float[] BloodlustHealShare = { 0.05f, 0.075f, 0.1f };
        private const string ExecuteNodeId = "berserker_execute";
        private static readonly float[] ExecuteBelow = { 0.2f, 0.25f, 0.3f };
        private const string RampageNodeId = "berserker_rampage";
        private static readonly float[] RampagePerStack = { 0.1f, 0.15f, 0.2f };
        private const string FuryNodeId = "berserker_fury";
        // 0.1.131 (Fire 10-14): the stack COUNT is a hard max for the HUD/network ([13 - Balance] "Fury max stacks", 10 -> 20); the bonus
        // starts at "Fury full stacks" (10) at +30% as before, and each stack past it adds the same share again through SoftCap.Soft (knee 30%).
        private static int FuryMax => Mathf.Max(1, BalanceConfig.Value(BalanceConfig.FuryMaxStacks, 20));
        private static int FuryFull => Mathf.Max(1, BalanceConfig.Value(BalanceConfig.FuryFullStacks, 10));
        private const float FuryDamage = 1.3f;   // the fallback for the knee: +30% at full stacks
        private const float FuryDrainSeconds = 5f;

        private const string CaltropsNodeId = "rogue_caltrops";
        private const float CaltropsRange = 3f;
        private const float CaltropsSeconds = 5f;
        private const float CaltropsCooldownSeconds = 5f;
        private const string BackstabNodeId = "rogue_backstab";
        private static readonly float[] BackstabBonus = { 0.5f, 0.65f, 0.8f };
        private const string AssassinateNodeId = "rogue_assassinate";
        // +20 a rank, not x1.5/x2: knives already carry vanilla's large sneak-attack multiplier.
        private static readonly float[] AssassinateBonus = { 1f, 1.2f, 1.4f };
        // Vanilla's own "from behind" test (Character.RPC_Damage's sneak attack): the hit runs along the target's facing.
        private const float BehindDot = 0.3f;

        private const string MartialArtsNodeId = "monk_martial_arts";
        private const float MartialArtsDamage = 1.2f;
        private const float ComboStep = 0.05f;
        private const float ComboMax = 0.25f;   // 0.1.131: the fallback knee of the combo bonus (SoftCap.Soft), no longer a ceiling
        private static int ComboMaxHits => Mathf.Max(1, BalanceConfig.Value(BalanceConfig.ComboMaxHits, 20));   // the hard hit COUNT
        private const float ComboWindowSeconds = 2f;
        private const string MonkStaminaNodeId = "monk_stamina";
        private static readonly float[] MonkStaminaRegenBonus = { 0.25f, 0.35f, 0.45f };
        private static readonly float[] MonkStaminaCostCut = { 0.15f, 0.2f, 0.25f };

        private const string AimNodeId = "ranger_aim";
        private static readonly float[] AimBonus = { 0.25f, 0.35f, 0.45f };
        private const float FullDraw = 0.99f;
        private const float ShotMemorySeconds = 10f;
        private const string HeadshotNodeId = "ranger_headshot";
        private static readonly float[] HeadshotBonus = { 0.5f, 0.65f, 0.8f };
        private const string KitingNodeId = "ranger_kiting";
        private static readonly float[] KitingSpeedBonus = { 0.15f, 0.2f, 0.25f };

        private const string SpellcastingNodeId = "mage_cast";
        private const float SpellcastingCost = 0.9f;
        private const string ChainNodeId = "mage_chain";
        private static readonly float[] ChainFreeChance = { 0.3f, 0.35f, 0.4f };
        private const string MageEitrNodeId = "mage_eitr_regen";
        private const string HealerEitrNodeId = "healer_eitr_regen";
        private static readonly float[] EitrRegenBonus = { 0.3f, 0.45f, 0.6f };
        private const string HealingStaffNodeId = "healer_heal";
        private const float HealingStaffHeal = 1.15f;
        // Design-only passives with full text (Tools\CLASS_KIT_DESIGN_GAPS.md): their numbers are the yaml values per rank.
        private const string BloodForBloodNodeId = "berserker_blood_for_blood";
        private const float BloodForBloodBelow = 0.5f;
        private const string CounterstrikeNodeId = "rogue_counterstrike";
        private const float CounterstrikeWindowSeconds = 3f;
        private const string StoneSkinNodeId = "monk_stone_skin";
        // Headtaker: full stamina on a killing blow, and (Fire, popup 2026-09-28 23:5x) +30% DAMAGE for 5 s in place of the
        // text's attack speed, which Valheim has no stat for.
        private const string HeadtakerNodeId = "berserker_headtaker";
        private const float HeadtakerDamage = 1.3f;
        private const float HeadtakerSeconds = 5f;
        private const string EmergencyNodeId = "healer_emergency";
        private const float EmergencyHealthShare = 0.2f;
        private static readonly float[] EmergencyHealBonus = { 0.5f, 0.75f, 1f };

        private static readonly Dictionary<string, string> AlwaysOn = new Dictionary<string, string>
        {
            { "tank_unyielding",     StatusEffectManager.EFFECT_UNYIELDING },
            { "berserker_execute",   StatusEffectManager.EFFECT_EXECUTE },
            { "berserker_deathwish", StatusEffectManager.EFFECT_DEATH_WISH },
            { "berserker_rampage",   StatusEffectManager.EFFECT_RAMPAGE },
        };

        private static readonly HashSet<string> Hooked = new HashSet<string>
        {
            RageNodeId, IronWallNodeId, ParryNodeId, BlessedDefenseNodeId, ShieldBlockNodeId, BlessedShieldNodeId, InterceptNodeId,
            BloodlustNodeId, FuryNodeId, CaltropsNodeId, BackstabNodeId, AssassinateNodeId, MartialArtsNodeId, MonkStaminaNodeId, AimNodeId,
            HeadshotNodeId, KitingNodeId, SpellcastingNodeId, ChainNodeId, MageEitrNodeId, HealerEitrNodeId, HealingStaffNodeId,
            EmergencyNodeId, BloodForBloodNodeId, CounterstrikeNodeId, StoneSkinNodeId, HeadtakerNodeId, PressurePointsNodeId,
        };

        // Pressure Points: "every {5th / 4th / 3rd} consecutive hit on the same target staggers it". The yaml's "5th" doesn't
        // parse as a number, so the rank picks it: 6 - rank. A chain breaks after 2 s without a hit (Fire's default rule).
        private const string PressurePointsNodeId = "monk_pressure_points";
        private const float PressureChainSeconds = 2f;
        private const float InstantStagger = 100f;
        private static Character _pressureTarget;
        private static int _pressureHits;
        private static float _lastPressureHit = float.NegativeInfinity;

        private static float _counterstrikeUntil = float.NegativeInfinity;
        private static float _headtakerUntil = float.NegativeInfinity;

        /// <summary>A passive's yaml value at the local player's rank in it.</summary>
        private static float ValueOf(string nodeId, string key)
        {
            ClassTreeNode node = ClassTrees.Node(nodeId);
            return node == null ? 0f : node.Value(key, DesignKit.RankOf(node));
        }

        private static readonly HashSet<string> Ours = new HashSet<string>();
        private static readonly HashSet<string> Active = new HashSet<string>();
        // Each on passive's learned rank, read with the Active set once a second, so no hook looks it up per hit or frame.
        private static readonly Dictionary<string, int> Ranks = new Dictionary<string, int>();
        private static float _nextTick;
        private static float _lastRage = float.NegativeInfinity;
        private static int _interceptRankShown;
        private static int _fury;
        private static float _lastFuryHit = float.NegativeInfinity;
        private static int _combo;
        private static float _lastComboHit = float.NegativeInfinity;
        private static float _lastCaltrops = float.NegativeInfinity;
        private static bool _lastShotFull;
        /// <summary>0.1.95: Deadeye reads whether the last shot left at full draw.</summary>
        internal static bool LastShotFull => _lastShotFull;

        /// <summary>0.1.95: Pain Fuels Me adds Fury from hits taken (the same counter and drain as Fury's hits dealt).</summary>
        /// <summary>0.1.131: Fury's damage bonus at <paramref name="stacks"/> (>= full): knee x stacks / full, through SoftCap.Soft.</summary>
        private static float FuryBonus(int stacks)
        {
            float knee = BalanceConfig.Value(BalanceConfig.FuryBonusKnee, FuryDamage - 1f);
            return SoftCap.Soft(knee * stacks / FuryFull, knee);
        }

        internal static void AddFury(int amount)
        {
            if (Time.time - _lastFuryHit > FuryDrainSeconds) _fury = 0;
            _fury = Mathf.Min(_fury + amount, FuryMax);
            _lastFuryHit = Time.time;
        }

        /// <summary>For FiresClassTest: Fury now.</summary>
        public static int FuryNow => Time.time - _lastFuryHit > FuryDrainSeconds ? 0 : _fury;
        private static float _lastShotTime = float.NegativeInfinity;

        public static bool Handles(string nodeId) => nodeId != null && (AlwaysOn.ContainsKey(nodeId) || Hooked.Contains(nodeId));

        /// <summary>
        /// What a Core-backed passive does at a rank, as a player sentence from the very per-rank tables the hooks above read
        /// (Fire 10-01: every passive card states its effect with real numbers, now and at the next rank; [classes-ui]'s
        /// TalentsView asks this first). Parry Mastery also scales with the class level. Null for a node this does not
        /// describe (its yaml text, or Core's description, is the card's).
        /// </summary>
        public static string EffectLine(string nodeId, int rank)
        {
            switch (nodeId)
            {
                case BloodlustNodeId:
                    return $"Each kill heals you for {Pct(AtRank(BloodlustHealShare, rank))} of your maximum health.";
                case ExecuteNodeId:
                    var execute = ObjectDB.instance?.GetStatusEffect(StatusEffectManager.EFFECT_EXECUTE.GetStableHashCode()) as ExecuteEffect;
                    return execute == null ? null
                        : $"Your melee and area hits deal {Pct(execute.DamageBonus - 1f)} more damage to enemies below {Pct(AtRank(ExecuteBelow, rank))} health.";
                case RampageNodeId:
                    float perStack = AtRank(RampagePerStack, rank);
                    return $"Each kill adds {Pct(perStack)} damage, up to {RampageEffect.MAX_STACKS} stacks (+{Pct(perStack * RampageEffect.MAX_STACKS)}); "
                        + $"they fall off {RampageEffect.STACK_DURATION:0} s after your last kill.";
                case HealerEitrNodeId:
                case MageEitrNodeId:
                    return $"Your eitr regenerates {Pct(AtRank(EitrRegenBonus, rank))} faster. The Mage's and the Healer's do not stack.";
                case EmergencyNodeId:
                    return $"Your class heals heal {Pct(AtRank(EmergencyHealBonus, rank))} more on a target below {Pct(EmergencyHealthShare)} health.";
                case ChainNodeId:
                    return $"Each attack with a magic staff has a {Pct(AtRank(ChainFreeChance, rank))} chance to cost no eitr.";
                case MonkStaminaNodeId:
                    return $"Everything that uses stamina costs you {Pct(AtRank(MonkStaminaCostCut, rank))} less, and your stamina regenerates "
                        + $"{Pct(AtRank(MonkStaminaRegenBonus, rank))} faster.";
                case BlessedDefenseNodeId:
                    return $"Each hit you block has a {Pct(AtRank(BlessedDefenseChance, rank))} chance to heal you for {Pct(BlessedDefenseHealShare)} of your maximum health.";
                case AimNodeId:
                    return $"A bow shot fired at full draw deals {Pct(AtRank(AimBonus, rank))} more damage; every crossbow bolt does.";
                case HeadshotNodeId:
                    return $"Bow and crossbow hits on an enemy that has not noticed you deal {Pct(AtRank(HeadshotBonus, rank))} more damage.";
                case KitingNodeId:
                    return $"While drawing a bow you move {Pct(AtRank(KitingSpeedBonus, rank))} faster.";
                case AssassinateNodeId:
                    return $"Hits while sneaking or in stealth deal {Pct(AtRank(AssassinateBonus, rank))} more damage (in place of Backstab's bonus).";
                case BackstabNodeId:
                    return $"Hits from behind deal {Pct(AtRank(BackstabBonus, rank))} more damage.";
                case InterceptNodeId:
                    return $"While a party member is within {InterceptRange:0} m, you both take {Pct(AtRank(InterceptCut, rank))} less damage "
                        + "(the best rank in range counts; it does not stack).";
                case IronWallNodeId:
                    return $"Hits you block deal {Pct(AtRank(IronWallBlockedCut, rank))} less damage to you.";
                case ParryNodeId:
                    int level = ClassPlayerStore.Local?.Progress.Level ?? 1;
                    float scale = AtRank(ParryScale, rank);
                    return $"A parry restores {ParryStaminaBase * scale:0.#} stamina plus {ParryStaminaPerLevel * scale:0.##} per class level "
                        + $"({(ParryStaminaBase + ParryStaminaPerLevel * level) * scale:0.#} at level {level}).";
                default: return null;
            }
        }

        /// <summary>The table behind each Core-backed passive, rank 1 first (for the READY note and class_test).</summary>
        public static IReadOnlyDictionary<string, string> RankTables => new Dictionary<string, string>
        {
            { BloodlustNodeId, "heal on kill " + Row(BloodlustHealShare) + " of max health" },
            { ExecuteNodeId, "below " + Row(ExecuteBelow) + " health (Core's damage bonus)" },
            { RampageNodeId, Row(RampagePerStack) + " a stack, " + RampageEffect.MAX_STACKS + " stacks" },
            { HealerEitrNodeId, "eitr regen +" + Row(EitrRegenBonus) },
            { MageEitrNodeId, "eitr regen +" + Row(EitrRegenBonus) },
            { EmergencyNodeId, "heal +" + Row(EmergencyHealBonus) + " below " + Pct(EmergencyHealthShare) },
            { ChainNodeId, "free cast " + Row(ChainFreeChance) },
            { MonkStaminaNodeId, "stamina cost -" + Row(MonkStaminaCostCut) + ", regen +" + Row(MonkStaminaRegenBonus) },
            { BlessedDefenseNodeId, "block heal chance " + Row(BlessedDefenseChance) + " for " + Pct(BlessedDefenseHealShare) },
            { AimNodeId, "full-draw +" + Row(AimBonus) },
            { HeadshotNodeId, "unaware +" + Row(HeadshotBonus) },
            { KitingNodeId, "speed +" + Row(KitingSpeedBonus) },
            { AssassinateNodeId, "stealth +" + Row(AssassinateBonus) },
            { BackstabNodeId, "behind +" + Row(BackstabBonus) },
            { InterceptNodeId, "damage taken -" + Row(InterceptCut) },
            { IronWallNodeId, "blocked damage -" + Row(IronWallBlockedCut) },
            { ParryNodeId, "stamina x" + string.Join(" / ", System.Array.ConvertAll(ParryScale, v => v.ToString("0.##"))) + " of (5 + 0.5 per level)" },
        };

        private static string Row(float[] values) => string.Join(" / ", System.Array.ConvertAll(values, Pct));

        private static string Pct(float fraction) => (Mathf.Round(fraction * 1000f) / 10f).ToString("0.#") + "%";

        private static bool On(string nodeId) => Active.Contains(nodeId);

        private static int RankIn(string nodeId) => Ranks.TryGetValue(nodeId, out int rank) ? rank : 1;

        /// <summary>A per-rank value at a rank (1-based; past the table, its last entry).</summary>
        private static float AtRank(float[] values, int rank) => values[Mathf.Clamp(rank, 1, values.Length) - 1];

        /// <summary>A per-rank value at the local player's rank in that passive.</summary>
        private static float Of(float[] values, string nodeId) => AtRank(values, RankIn(nodeId));

        private static bool IsLocal(Character character) => character != null && character == Player.m_localPlayer;

        /// <summary>Called every frame by ClassCombatDriver; does its work once a second.</summary>
        public static void Tick()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickSeconds;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) { Active.Clear(); Ranks.Clear(); return; }

            Active.Clear();
            Ranks.Clear();
            foreach (string nodeId in Hooked)
            {
                ClassTreeNode node = ClassTrees.Node(nodeId);
                if (node == null || PassiveLoadout.Status(node) != PassiveStatus.Active) continue;
                Active.Add(nodeId);
                Ranks[nodeId] = DesignKit.RankOf(node);
            }

            foreach (KeyValuePair<string, string> passive in AlwaysOn)
            {
                ClassTreeNode node = ClassTrees.Node(passive.Key);
                bool wanted = node != null && PassiveLoadout.Status(node) == PassiveStatus.Active;
                if (wanted) Ranks[passive.Key] = DesignKit.RankOf(node);
                bool has = StatusEffectManager.HasEffect(player, passive.Value);
                if (wanted && !has)
                {
                    if (AbilityRPCManager.ApplySelfBuff(player, passive.Value, 0f))
                    {
                        if (Ours.Add(passive.Value)) FiresRPGClassesPlugin.Log?.LogInfo($"[Passive] {passive.Key} on ({passive.Value})");
                    }
                }
                else if (!wanted && has && Ours.Remove(passive.Value))
                {
                    AbilityRPCManager.RemoveSelfBuff(player, passive.Value);
                    FiresRPGClassesPlugin.Log?.LogInfo($"[Passive] {passive.Key} off ({passive.Value})");
                }
            }

            if (On(RageNodeId) && player.GetHealthPercentage() < RageHealthShare && Time.time - _lastRage >= RageCooldownSeconds
                && AbilityRPCManager.ApplySelfBuff(player, StatusEffectManager.EFFECT_BERSERK_RAGE, RageSeconds))
            {
                _lastRage = Time.time;
                FiresRPGClassesPlugin.Log?.LogInfo($"[Passive] {RageNodeId}: rage for {RageSeconds} s at {player.GetHealthPercentage():P0} health");
            }

            // Execute's threshold is its rank's: the player's own copy of Core's effect, which reads it on every hit.
            if (Ours.Contains(StatusEffectManager.EFFECT_EXECUTE)
                && player.GetSEMan()?.GetStatusEffect(StatusEffectManager.EFFECT_EXECUTE.GetStableHashCode()) is ExecuteEffect execute)
                execute.HealthThreshold = Of(ExecuteBelow, ExecuteNodeId);

            // Intercept's aura is read by the party members' own peers, so it is on the player's ZDO (written only on change):
            // the bool older peers read, and the rank.
            int interceptRank = On(InterceptNodeId) ? RankIn(InterceptNodeId) : 0;
            if (interceptRank != _interceptRankShown)
            {
                ZDO zdo = player.m_nview != null ? player.m_nview.GetZDO() : null;
                if (zdo != null)
                {
                    zdo.Set(InterceptZdoKey, interceptRank > 0);
                    zdo.Set(InterceptRankZdoKey, interceptRank);
                    _interceptRankShown = interceptRank;
                }
            }

            if (_fury > 0 && Time.time - _lastFuryHit > FuryDrainSeconds) _fury = 0;
            DruidNecroPassives.Tick();
        }

        /// <summary>Kills credited to the local player (vanilla sends RPC_RegisterKill to every player who hit the victim).</summary>
        internal static void OnLocalKill()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;
            if (Ours.Contains(StatusEffectManager.EFFECT_RAMPAGE))
            {
                StatusEffect effect = player.GetSEMan()?.GetStatusEffect(StatusEffectManager.EFFECT_RAMPAGE.GetStableHashCode());
                (effect as RampageEffect)?.OnKill();
            }
            if (On(BloodlustNodeId)) player.Heal(player.GetMaxHealth() * Of(BloodlustHealShare, BloodlustNodeId), true);
            if (On(HeadtakerNodeId))
            {
                player.AddStamina(player.GetMaxStamina());
                _headtakerUntil = Time.time + HeadtakerSeconds;
            }
        }

        /// <summary>
        /// The local player's hit, before it is sent: Fury, Martial Arts, Steady Aim, Headshot (a ranged hit on an unaware
        /// target), and Assassinate (+100% from stealth) or else Backstab (+50% from behind).
        /// </summary>
        internal static void OnLocalHitSending(Character target, HitData hit)
        {
            if (hit == null || target == null || !IsLocal(hit.GetAttacker()) || target == hit.GetAttacker()) return;
            Player player = Player.m_localPlayer;
            float factor = 1f;

            if (On(FuryNodeId))
            {
                if (Time.time - _lastFuryHit > FuryDrainSeconds) _fury = 0;
                _fury = Mathf.Min(_fury + 1, FuryMax);
                _lastFuryHit = Time.time;
                if (_fury >= FuryFull) factor *= 1f + FuryBonus(_fury);
            }

            Skills.SkillType skill = hit.m_skill;
            if (On(MartialArtsNodeId) && (skill == Skills.SkillType.Unarmed || skill == Skills.SkillType.Clubs))
            {
                _combo = Time.time - _lastComboHit <= ComboWindowSeconds ? Mathf.Min(_combo + 1, ComboMaxHits) : 0;
                _lastComboHit = Time.time;
                factor *= MartialArtsDamage + SoftCap.Soft(ComboStep * _combo, BalanceConfig.Value(BalanceConfig.ComboBonusKnee, ComboMax));
            }

            bool ranged = skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows;
            if (ranged && On(AimNodeId)
                && (skill == Skills.SkillType.Crossbows || (_lastShotFull && Time.time - _lastShotTime < ShotMemorySeconds)))
                factor *= 1f + Of(AimBonus, AimNodeId);
            if (ranged && On(HeadshotNodeId))
            {
                BaseAI ai = target.GetBaseAI();
                if (ai != null && !ai.IsAlerted()) factor *= 1f + Of(HeadshotBonus, HeadshotNodeId);
            }

            bool stealthy = player.IsSneaking() || StatusEffectManager.HasEffect(player, StatusEffectManager.EFFECT_STEALTH);
            if (On(AssassinateNodeId) && stealthy) factor *= 1f + Of(AssassinateBonus, AssassinateNodeId);
            else if (On(BackstabNodeId) && Vector3.Dot(hit.m_dir, target.transform.forward) > BehindDot) factor *= 1f + Of(BackstabBonus, BackstabNodeId);
            // Rampage above rank 1: Core's effect already added its own 10% a stack to this hit, so top it up to the rank's.
            if (Ours.Contains(StatusEffectManager.EFFECT_RAMPAGE) && RankIn(RampageNodeId) > 1
                && player.GetSEMan()?.GetStatusEffect(StatusEffectManager.EFFECT_RAMPAGE.GetStableHashCode()) is RampageEffect rampage
                && rampage.CurrentStacks > 0)
                factor *= (1f + rampage.CurrentStacks * Of(RampagePerStack, RampageNodeId))
                          / (1f + rampage.CurrentStacks * RampageEffect.DAMAGE_PER_STACK);

            if (On(HeadtakerNodeId) && Time.time <= _headtakerUntil) factor *= HeadtakerDamage;

            // Counterstrike: "after a parry, your next attack within 3 s deals {bonus} more damage", once.
            if (On(CounterstrikeNodeId) && Time.time <= _counterstrikeUntil)
            {
                factor *= 1f + ValueOf(CounterstrikeNodeId, "bonus");
                _counterstrikeUntil = float.NegativeInfinity;
            }

            if (On(PressurePointsNodeId))
            {
                if (target != _pressureTarget || Time.time - _lastPressureHit > PressureChainSeconds) _pressureHits = 0;
                _pressureTarget = target;
                _lastPressureHit = Time.time;
                int every = Mathf.Max(1, 6 - DesignKit.RankOf(ClassTrees.Node(PressurePointsNodeId)));
                if (++_pressureHits >= every)
                {
                    hit.m_staggerMultiplier = Mathf.Max(hit.m_staggerMultiplier, InstantStagger);
                    _pressureHits = 0;
                }
            }

            CcKit.OnLocalHitSending(target, hit);

            if (factor != 1f) hit.m_damage.Modify(factor);

            // Blood for Blood: "below 50% health, {steal} of the damage you deal heals you" (the hit as sent, before the
            // target's armour, which only its owner knows).
            if (On(BloodForBloodNodeId) && player.GetHealthPercentage() < BloodForBloodBelow)
            {
                float heal = hit.GetTotalDamage() * ValueOf(BloodForBloodNodeId, "steal");
                if (heal > 0f) player.Heal(heal, true);
            }
        }

        /// <summary>
        /// Caltrops (Fire, 21:2x: "your way"): when the vanilla roll runs, Core's Caltrops (slow + pierce ticks, 5 s) goes on
        /// the enemies within 3 m of where the roll started. ApplyAoEEffect centres on the caster and follows ClassTargeting,
        /// so only enemies are hit. Once per 5 s.
        /// </summary>
        internal static void OnLocalRoll(Player player)
        {
            if (!IsLocal(player) || !On(CaltropsNodeId) || Time.time - _lastCaltrops < CaltropsCooldownSeconds) return;
            _lastCaltrops = Time.time;
            int hit = AbilityRPCManager.ApplyAoEEffect(player, StatusEffectManager.EFFECT_CALTROPS, CaltropsRange, CaltropsSeconds);
            FiresRPGClassesPlugin.Log?.LogInfo($"[Passive] {CaltropsNodeId}: caltrops behind the roll, {hit} enemy(ies) within {CaltropsRange} m");
        }

        /// <summary>Steady Aim: whether the local player's bow was fully drawn when this arrow left.</summary>
        internal static void OnLocalShot(Humanoid attacker)
        {
            if (!IsLocal(attacker)) return;
            _lastShotFull = attacker.GetAttackDrawPercentage() >= FullDraw;
            _lastShotTime = Time.time;
        }

        /// <summary>
        /// Before vanilla blocks a hit on the local player: Iron Wall takes 60% off it. Returns whether this block is a parry
        /// (vanilla's own test), for Parry Mastery after the block.
        /// </summary>
        internal static bool OnLocalBlocking(Humanoid defender, HitData hit)
        {
            if (!IsLocal(defender) || hit == null) return false;
            if (On(IronWallNodeId)) { hit.m_damage.Modify(1f - Of(IronWallBlockedCut, IronWallNodeId)); SkillFx.Play(IronWallNodeId, defender.GetCenterPoint(), SkillFx.ShieldHit); }
            ItemDrop.ItemData blocker = defender.GetCurrentBlocker();
            return blocker != null && blocker.m_shared.m_timedBlockBonus > 1f
                && defender.m_blockTimer != -1f && defender.m_blockTimer < ParryWindowSeconds;
        }

        /// <summary>After a block: Parry Mastery restores stamina on a parry; Blessed Defense and Blessed Shield may heal.</summary>
        internal static void OnLocalBlocked(Humanoid defender, bool blocked, bool parry)
        {
            if (!blocked || !IsLocal(defender)) return;
            Player player = (Player)defender;
            if (parry && On(ParryNodeId))
            {
                int level = ClassPlayerStore.Local?.Progress.Level ?? 0;
                player.AddStamina((ParryStaminaBase + ParryStaminaPerLevel * level) * Of(ParryScale, ParryNodeId));
            }
            if (parry && On(CounterstrikeNodeId)) _counterstrikeUntil = Time.time + CounterstrikeWindowSeconds;
            if (On(BlessedDefenseNodeId) && Random.value < Of(BlessedDefenseChance, BlessedDefenseNodeId))
                player.Heal(player.GetMaxHealth() * BlessedDefenseHealShare, true);
            else if (On(BlessedShieldNodeId) && Random.value < BlessedShieldChance)
                player.Heal(player.GetMaxHealth() * BlessedShieldHealShare, true);
        }

        /// <summary>Shield Block (+20%) and Blessed Shield (+10%) raise the block power of the local player's raised shield.</summary>
        internal static float BlockPowerFactor(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (player == null || item == null || player.GetCurrentBlocker() != item) return 1f;
            float factor = 1f;
            if (On(ShieldBlockNodeId)) factor *= ShieldBlockPower;
            if (On(BlessedShieldNodeId)) factor *= BlessedShieldPower;
            return factor;
        }

        /// <summary>
        /// Intercept's guard aura, on the local player's own peer: 10% less damage while a party member is within 5 m and
        /// either of you has Intercept on (theirs is read from their ZDO). It does not stack.
        /// </summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (!IsLocal(victim) || hit == null) return;

            // Stone Skin: "{dr} less physical damage taken".
            if (On(StoneSkinNodeId))
            {
                float keep = 1f - ValueOf(StoneSkinNodeId, "dr");
                hit.m_damage.m_blunt *= keep;
                hit.m_damage.m_slash *= keep;
                hit.m_damage.m_pierce *= keep;
                if (hit.m_damage.m_blunt + hit.m_damage.m_slash + hit.m_damage.m_pierce > 0f) SkillFx.Play(StoneSkinNodeId, victim.GetCenterPoint(), SkillFx.StoneSparks);
            }

            // The best rank among you and the party members in range, once: the aura does not stack.
            int mine = On(InterceptNodeId) ? RankIn(InterceptNodeId) : 0;
            int best = 0;
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == victim || other.IsDead()) continue;
                if (Vector3.Distance(other.transform.position, victim.transform.position) > InterceptRange) continue;
                if (!ClassTargeting.IsPartyMember(victim, other)) continue;
                ZDO zdo = other.m_nview != null ? other.m_nview.GetZDO() : null;
                int theirs = zdo == null ? 0 : zdo.GetInt(InterceptRankZdoKey, zdo.GetBool(InterceptZdoKey) ? 1 : 0);
                best = Mathf.Max(best, mine, theirs);
            }
            if (best > 0) hit.m_damage.Modify(1f - AtRank(InterceptCut, best));
        }

        /// <summary>Healing Staff (+15%) and Emergency Heal (+50% on a target below 20%): the local player's Core class heals.</summary>
        internal static float HealFactor(Character healer, Character target)
        {
            if (!IsLocal(healer) || target == null) return 1f;
            float factor = 1f;
            if (On(HealingStaffNodeId)) factor *= HealingStaffHeal;
            if (On(EmergencyNodeId) && target.GetHealthPercentage() < EmergencyHealthShare) factor *= 1f + Of(EmergencyHealBonus, EmergencyNodeId);
            return factor;
        }

        private static bool CastingMagic(Player player)
        {
            if (!player.InAttack()) return false;
            Skills.SkillType skill = player.GetCurrentWeapon()?.m_shared.m_skillType ?? Skills.SkillType.None;
            return skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic;
        }

        /// <summary>Spellcasting: a magic weapon's attack costs 10% less eitr.</summary>
        internal static float SpellCostFactor(Player player) =>
            IsLocal(player) && On(SpellcastingNodeId) && CastingMagic(player) ? SpellcastingCost : 1f;

        /// <summary>Chain Casting: a spell's eitr (a magic weapon's attack) has a 30% chance to cost nothing.</summary>
        internal static bool FreeCast(Player player, float eitr) =>
            eitr > 0f && IsLocal(player) && On(ChainNodeId) && CastingMagic(player) && Random.value < Of(ChainFreeChance, ChainNodeId);

        internal static float StaminaCostFactor(Player player) =>
            IsLocal(player) && On(MonkStaminaNodeId) ? 1f - Of(MonkStaminaCostCut, MonkStaminaNodeId) : 1f;

        internal static float StaminaRegenFactor(Character character) =>
            IsLocal(character) && On(MonkStaminaNodeId) ? 1f + Of(MonkStaminaRegenBonus, MonkStaminaNodeId) : 1f;

        // The Mage's and the Healer's are the same table; equipping both does not stack (the higher rank counts).
        internal static float EitrRegenFactor(Character character)
        {
            if (!IsLocal(character)) return 1f;
            int rank = Mathf.Max(On(MageEitrNodeId) ? RankIn(MageEitrNodeId) : 0, On(HealerEitrNodeId) ? RankIn(HealerEitrNodeId) : 0);
            return rank > 0 ? 1f + AtRank(EitrRegenBonus, rank) : 1f;
        }

        /// <summary>Kiting Expertise: faster (by rank) while drawing a bow.</summary>
        internal static float SpeedFactor(Character character) =>
            IsLocal(character) && On(KitingNodeId) && character.IsDrawingBow() ? 1f + Of(KitingSpeedBonus, KitingNodeId) : 1f;
    }
}
