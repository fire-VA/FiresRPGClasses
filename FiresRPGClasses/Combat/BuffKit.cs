using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.91 (Fire #11): the party buffs whose stats Core doesn't have. Core 0.2.395+ only carries the name
    /// (ClassTrance10/15/20, ClassShieldWall40/50/60, given by AbilityRPCManager.ApplyGroupBuff to each party member's
    /// owner); this kit reads them in the existing hooks, on the peer that owns the buffed player. Perf (Fire's rule):
    /// int hashes, no allocation, local player only.
    /// </summary>
    public static class BuffKit
    {
        private const string TranceId = "berserker_battle_trance";
        private const string ShieldWallId = "paladin_shield_wall";

        private static readonly string[] TranceByRank = { "ClassTrance10", "ClassTrance15", "ClassTrance20" };
        private static readonly float[] TranceSpeed = { 1.10f, 1.15f, 1.20f };
        private const float TranceStamina = 0.8f;            // "attacks cost 20% less stamina"
        private static readonly int[] TranceHash = Hashes(TranceByRank);

        private static readonly string[] WallByRank = { "ClassShieldWall40", "ClassShieldWall50", "ClassShieldWall60" };
        private static readonly float[] WallTaken = { 0.60f, 0.50f, 0.40f };   // 40/50/60% less from the front
        private const float WallSlow = 0.7f;                  // "You move 30% slower" (the caster only)
        private const float WallReach = 8f;                   // allies behind the shield, within 8 m
        private static readonly int[] WallHash = Hashes(WallByRank);

        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(TranceId, Trance);
            ClassSkillKit.Register(ShieldWallId, ShieldWall);
        }

        private static int[] Hashes(string[] names)
        {
            var h = new int[names.Length];
            for (int i = 0; i < names.Length; i++) h[i] = names[i].GetStableHashCode();
            return h;
        }

        // "You and allies within 10 m gain {as} attack speed, and attacks cost 20% less stamina, for 12 s."
        private static bool Trance(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = Mathf.Clamp(DesignKit.RankOf(node), 1, TranceByRank.Length);
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 10f;
            float duration = node.EffectDuration > 0f ? node.EffectDuration : 12f;
            int n = AbilityRPCManager.ApplyGroupBuff(caster, TranceByRank[rank - 1], radius, duration);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {TranceId} rank {rank}: {TranceByRank[rank - 1]} on {n} party member(s) within {radius:0} m for {duration:0} s");
            return n > 0;
        }

        // "Raise your shield for 6 s: frontal attacks on you and allies behind you are reduced by {dr}. You move 30% slower."
        // (Rank 3's taunt is not in this build.)
        private static bool ShieldWall(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = Mathf.Clamp(DesignKit.RankOf(node), 1, WallByRank.Length);
            float duration = node.EffectDuration > 0f ? node.EffectDuration : 6f;
            SkillFx.Play(ShieldWallId, caster.GetCenterPoint(), SkillFx.ShieldStart, 0.1f);
            int n = AbilityRPCManager.ApplyGroupBuff(caster, WallByRank[rank - 1], WallReach, duration);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {ShieldWallId} rank {rank}: {WallByRank[rank - 1]} on {n} party member(s) for {duration:0} s");
            return n > 0;
        }

        private static int Rank(SEMan seman, int[] hashes)
        {
            for (int i = hashes.Length - 1; i >= 0; i--) if (seman.HaveStatusEffect(hashes[i])) return i + 1;
            return 0;
        }

        private static bool IsLocal(Character c) => c != null && c == Player.m_localPlayer;

        /// <summary>Attack-animation speed factor for the local player (AttributeEffects' attack-speed hook).</summary>
        public static float AttackSpeedFactor(Character c)
        {
            if (!IsLocal(c)) return 1f;
            int r = Rank(c.GetSEMan(), TranceHash);
            return r > 0 ? TranceSpeed[r - 1] : 1f;
        }

        /// <summary>Stamina cost factor while attacking (the UseStamina hook).</summary>
        public static float StaminaCostFactor(Player p) =>
            IsLocal(p) && p.InAttack() && Rank(p.GetSEMan(), TranceHash) > 0 ? TranceStamina : 1f;

        /// <summary>Movement factor: the Shield Wall's caster moves 30% slower.</summary>
        public static float SpeedFactor(Character c)
        {
            if (!IsLocal(c)) return 1f;
            int r = Rank(c.GetSEMan(), WallHash);
            if (r == 0) return 1f;
            return Source(c, WallHash[r - 1]) == c ? WallSlow : 1f;
        }

        /// <summary>Damage taken by the local player (RPC_Damage, on their owner): reduced when the attacker stands in front
        /// of the wall's caster and the player is the caster or behind them.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (!IsLocal(victim)) return;
            int r = Rank(victim.GetSEMan(), WallHash);
            if (r == 0) return;
            Character wall = Source(victim, WallHash[r - 1]) ?? victim;
            Character attacker = hit.GetAttacker();
            Vector3 front = wall.transform.forward;
            Vector3 from = attacker != null ? attacker.transform.position - wall.transform.position : -hit.m_dir;
            if (Vector3.Dot(front, from) <= 0f) return;   // not a frontal attack
            if (wall != victim && Vector3.Dot(front, victim.transform.position - wall.transform.position) > 0f) return;   // not behind the shield
            hit.m_damage.Modify(WallTaken[r - 1]);
            SkillFx.Play(ShieldWallId + "_hit", victim.GetCenterPoint(), SkillFx.ShieldHit, 0.3f);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {ShieldWallId}: frontal hit on {victim.GetHoverName()} reduced to {WallTaken[r - 1]:0%} (rank {r}, wall {(wall == victim ? "own" : wall.GetHoverName())})");
        }

        private static Character Source(Character c, int hash) =>
            (c.GetSEMan().GetStatusEffect(hash) as CompanionStatusEffectBase)?.SourceCharacter;
    }
}
