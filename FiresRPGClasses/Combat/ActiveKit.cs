using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresCore.Summon;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Design-only actives built on seams that already exist. Every number is the node's yaml value
    /// at the caster's rank. Self-only effects keep a local timer (no network: only the caster is changed); anything that touches
    /// another character goes through Core (AbilityHeals / ApplySingleEffect / SummonApi).
    /// - Swift Mend: an instant heal on the ally under the crosshair (Mending Light's path).
    /// - Killshot: arms the next hit; on a target below 30% health it deals 200% more.
    /// - Glacial Ward: 10 s of frost resistance; a melee attacker is slowed 30%.
    /// - Tracker: minimap pins on every creature within range for 10 s, refreshed each second (not saved).
    /// - Call of the Wild: a tamed Wolf (stars by rank) in Attack mode for 60 s.
    /// </summary>
    public static class ActiveKit
    {
        private const string SwiftMendId = "healer_swift_mend";
        private const string KillshotId = "ranger_killshot";
        private const string GlacialWardId = "mage_glacial_ward";
        private const string TrackerId = "ranger_tracker";
        private const string CallWildId = "ranger_call_of_the_wild";
        private const string BloodbathId = "berserker_bloodbath";
        internal const string BloodbathNodeId = BloodbathId;   // ClassStatsApi row name
        private const float BloodbathSteal = 0.15f;          // "15% of the damage you deal heals you"
        private static float _bathUntil = -1f, _bathSpeed = 1f;

        private const float KillshotArmSeconds = 30f;
        private const float KillshotThreshold = 0.3f;
        private const float KillshotMultiplier = 3f;           // "200% more"
        private const float WardMeleeReach = 4f;
        private const float WardSlowSeconds = 3f;
        private const float TrackerRefresh = 1f;

        private static float _killshotUntil = -1f;
        private static float _wardUntil = -1f;
        private static float _wardRes;
        private static float _trackUntil = -1f, _trackNext, _trackRange;
        private static readonly List<Minimap.PinData> TrackPins = new List<Minimap.PinData>();
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(SwiftMendId, SwiftMend);
            ClassSkillKit.Register(KillshotId, Killshot);
            ClassSkillKit.Register(GlacialWardId, GlacialWard);
            ClassSkillKit.Register(TrackerId, Tracker);
            ClassSkillKit.Register(CallWildId, CallOfTheWild);
            ClassSkillKit.Register(BloodbathId, Bloodbath);
        }

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Classes] " + s);

        // "Instantly heal an ally within 20 m for {heal} of their max health."
        private static bool SwiftMend(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character ally = target.TargetCharacter != null ? target.TargetCharacter : caster;
            if (!ClassTargeting.IsPartyMember(caster, ally) || ally.IsDead()) return false;
            float amount = ally.GetMaxHealth() * node.Value("heal", DesignKit.RankOf(node));
            AbilityHeals.Apply(caster, ally, amount);
            Log($"{SwiftMendId}: healed {ally.GetHoverName()} for {amount:0}");
            return true;
        }

        // "Your next shot deals 200% more damage to a target below 30% health."
        private static bool Killshot(ClassTreeNode node, Character caster, CastTarget target)
        {
            _killshotUntil = Time.time + KillshotArmSeconds;
            Log($"{KillshotId}: armed for {KillshotArmSeconds:0} s");
            return true;
        }

        // "Frost armor for 10 s: {res} frost resistance, and melee attackers are slowed 30%."
        private static bool GlacialWard(ClassTreeNode node, Character caster, CastTarget target)
        {
            _wardRes = Mathf.Clamp01(node.Value("res", DesignKit.RankOf(node)));
            _wardUntil = Time.time + (node.EffectDuration > 0f ? node.EffectDuration : 10f);
            AbilityRPCManager.SpawnFX(caster.transform.position, "vfx_Frost");
            Log($"{GlacialWardId}: {_wardRes:0%} frost resistance until +{_wardUntil - Time.time:0} s");
            return true;
        }

        // "Reveal creatures within {range} through terrain for 10 s."
        private static bool Tracker(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (Minimap.instance == null) return false;
            _trackRange = node.Value("range", DesignKit.RankOf(node), 50f);
            _trackUntil = Time.time + (node.EffectDuration > 0f ? node.EffectDuration : 10f);
            _trackNext = 0f;
            Tick();
            Log($"{TrackerId}: tracking {TrackPins.Count} creature(s) within {_trackRange:0} m");
            return true;
        }

        // "Summon a {stars} tamed wolf for 60 s that attacks your target."
        private static bool CallOfTheWild(ClassTreeNode node, Character caster, CastTarget target)
        {
            int stars = Mathf.Clamp(Mathf.RoundToInt(node.Value("stars", DesignKit.RankOf(node))), 0, 2);
            var options = new SummonOptions
            {
                Level = stars + 1,
                Duration = node.EffectDuration > 0f ? node.EffectDuration : 60f,
                Mode = SummonMode.Attack,
                Cap = SummonKit.Cap(),
                SourceTag = node.Id,
            };
            ZDOID id = SummonApi.Summon("Wolf", options, out string why);
            Log($"{CallWildId}: {stars}-star Wolf {(id.IsNone() ? "refused: " + why : "summoned")}");
            if (id.IsNone()) { (caster as Player)?.Message(MessageHud.MessageType.Center, node.DisplayName + ": " + why); return false; }
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
            if (go != null) AbilityRPCManager.SpawnFX(go.transform.position, "vfx_spawn");
            return true;
        }

        // Ultimate: "For 10 s: +{as} attack speed, and 15% of the damage you deal heals you." The health cost is the
        // ultimate's own (UltimateController). Lifesteal reads the hit as sent (before the victim's armor): the attacker's peer
        // never sees the damage the victim's owner applies.
        private static bool Bloodbath(ClassTreeNode node, Character caster, CastTarget target)
        {
            _bathSpeed = 1f + node.Value("as", DesignKit.RankOf(node), 0.25f);
            _bathUntil = Time.time + (node.EffectDuration > 0f ? node.EffectDuration : 10f);
            AbilityRPCManager.SpawnFX(caster.transform.position, "vfx_MeadBzerker");
            Log($"{BloodbathId}: attack speed x{_bathSpeed:0.00}, lifesteal {BloodbathSteal:0%} for {_bathUntil - Time.time:0} s");
            return true;
        }

        /// <summary>Bloodbath's attack-speed factor for the local player (AttributeEffects' attack-speed hook).</summary>
        public static float AttackSpeedFactor(Character c) => c != null && c == Player.m_localPlayer && _bathUntil >= Time.time ? _bathSpeed : 1f;
        public static bool BloodbathActive => _bathUntil >= Time.time;

        /// <summary>Called every frame by ClassCombatDriver (Tracker's pins).</summary>
        public static void Tick()
        {
            if (_trackUntil < 0f) return;
            if (Time.time > _trackUntil || Minimap.instance == null) { ClearPins(); _trackUntil = -1f; return; }
            if (Time.time < _trackNext) return;
            _trackNext = Time.time + TrackerRefresh;
            ClearPins();
            Player me = Player.m_localPlayer;
            if (me == null) return;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsDead()) continue;
                if (Vector3.Distance(c.transform.position, me.transform.position) > _trackRange) continue;
                TrackPins.Add(Minimap.instance.AddPin(c.transform.position, Minimap.PinType.Icon3, c.GetHoverName(), false, false));
            }
        }

        private static void ClearPins()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in TrackPins) Minimap.instance.RemovePin(pin);
            TrackPins.Clear();
        }

        /// <summary>The local player's hit being sent (Character.Damage prefix): an armed Killshot.</summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            if (_bathUntil >= Time.time && victim != null && hit != null && victim != Player.m_localPlayer
                && Player.m_localPlayer != null && hit.m_attacker == Player.m_localPlayer.GetZDOID())
            {
                float steal = hit.GetTotalDamage() * BloodbathSteal;
                if (steal > 0f) AbilityHeals.Apply(Player.m_localPlayer, Player.m_localPlayer, steal);
            }
            if (_killshotUntil < 0f || victim == null || hit == null) return;
            if (Time.time > _killshotUntil) { _killshotUntil = -1f; return; }
            Player local = Player.m_localPlayer;
            if (local == null || victim == local || hit.m_attacker != local.GetZDOID()) return;
            _killshotUntil = -1f;
            float share = victim.GetMaxHealth() > 0f ? victim.GetHealth() / victim.GetMaxHealth() : 1f;
            if (share < KillshotThreshold) hit.m_damage.Modify(KillshotMultiplier);
            Log($"{KillshotId}: spent on {victim.GetHoverName()} at {share:0%} health: {(share < KillshotThreshold ? "x3 damage" : "above 30%, no bonus")}");
        }

        /// <summary>Damage the local player takes (RPC_Damage on their owner): Glacial Ward.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (_wardUntil < 0f || victim != Player.m_localPlayer || hit == null) return;
            if (Time.time > _wardUntil) { _wardUntil = -1f; return; }
            if (hit.m_damage.m_frost > 0f)
            {
                float was = hit.m_damage.m_frost;
                hit.m_damage.m_frost *= 1f - _wardRes;
                Log($"{GlacialWardId}: frost {was:0.0} -> {hit.m_damage.m_frost:0.0} ({_wardRes:0%} resistance, before armor)");   // evidence the ward applied
            }
            Character attacker = hit.GetAttacker();
            float reach = attacker != null ? Vector3.Distance(attacker.transform.position, victim.transform.position) : -1f;
            bool slowed = attacker != null && !hit.m_ranged && reach <= WardMeleeReach
                && AbilityRPCManager.ApplySingleEffect(victim, attacker, StatusEffectManager.EFFECT_CLASS_SLOW30, WardSlowSeconds);
            Log($"{GlacialWardId}: attacker {(attacker != null ? attacker.GetHoverName() : "none")} at {reach:0.0} m, ranged {hit.m_ranged}: slowed {slowed}");   // evidence the slow applied
        }

        /// <summary>For ClassTest: whether Killshot is armed and Glacial Ward's resistance now (0 when off).</summary>
        public static bool KillshotArmed => _killshotUntil >= Time.time;
        public static float WardResistance => _wardUntil >= Time.time ? _wardRes : 0f;
        public static int TrackedPins => TrackPins.Count;
    }
}
