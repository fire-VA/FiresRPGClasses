using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.93 (Fire #11 batch 2b): the two movement actives. Both move ONLY the local player (their own peer owns the body),
    /// one step per frame from ClassCombatDriver; hits on enemies go through Character.Damage (routed to each victim's owner)
    /// and Core's knockdown preset.
    /// - Breakwall (Tank): charge up to 10 m along the aim; every enemy within 1.5 m of the path is hit once for {blunt} blunt
    ///   and knocked down; the tank takes 50% less damage while charging.
    /// - Thousand Steps (Monk): up to 5 enemies within 10 m, nearest first; the monk appears beside each in turn (0.25 s apart)
    ///   and strikes it for 150% of the current weapon's damage.
    /// </summary>
    public static class MoveKit
    {
        private const string BreakwallId = "tank_breakwall";
        private const string StepsId = "monk_thousand_steps";

        private const float ChargeSpeed = 20f;
        private const float ChargeHitReach = 1.5f;
        private const float ChargeTaken = 0.5f;
        private const float KnockdownSeconds = 1.5f;
        private const int StepsMax = 5;
        private const float StepsReach = 10f;
        private const float StepsGap = 0.25f;
        private const float StepsWeaponShare = 1.5f;

        private static float _chargeUntil = -1f;
        private static Vector3 _chargeDir;
        private static float _chargeBlunt;
        private static readonly HashSet<Character> ChargeHit = new HashSet<Character>();
        private static readonly List<Character> StepTargets = new List<Character>();
        private static float _stepNext;
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(BreakwallId, Breakwall);
            ClassSkillKit.Register(StepsId, ThousandSteps);
        }

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Classes] " + s);

        // "Charge 10 m through enemies: knock down everything in your path for {blunt} blunt damage. You take 50% less damage while charging."
        private static bool Breakwall(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            float range = node.Targeting.Range > 0f ? node.Targeting.Range : 10f;
            Vector3 dir = target.Resolved ? target.Point - player.transform.position : player.transform.forward;
            dir.y = 0f;
            _chargeDir = dir.sqrMagnitude > 0.01f ? dir.normalized : player.transform.forward;
            _chargeBlunt = node.Value("blunt", DesignKit.RankOf(node), 60f);
            _chargeUntil = Time.time + range / ChargeSpeed;
            ChargeHit.Clear();
            LastChargeHits = 0;
            _closestMiss = float.MaxValue; _closestMissName = null;
            player.transform.rotation = Quaternion.LookRotation(_chargeDir);
            SkillFx.Play(BreakwallId, player.transform.position, SkillFx.Stomp, 0.1f);
            Log($"{BreakwallId}: charging {range:0} m, {_chargeBlunt:0} blunt");
            return true;
        }

        // "Dash between up to 5 enemies within 10 m, striking each for 150% weapon damage."
        private static bool ThousandSteps(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            StepTargets.Clear();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || !ClassTargeting.IsEnemyTarget(player, c)) continue;
                if (Vector3.Distance(c.transform.position, player.transform.position) <= StepsReach) StepTargets.Add(c);
            }
            if (StepTargets.Count == 0) { player.Message(MessageHud.MessageType.Center, node.DisplayName + ": no enemy within 10 m"); return false; }
            Vector3 from = player.transform.position;
            StepTargets.Sort((a, b) => Vector3.Distance(a.transform.position, from).CompareTo(Vector3.Distance(b.transform.position, from)));
            if (StepTargets.Count > StepsMax) StepTargets.RemoveRange(StepsMax, StepTargets.Count - StepsMax);
            _stepNext = 0f;
            StepsStruck = 0;
            Log($"{StepsId}: {StepTargets.Count} target(s)");
            return true;
        }

        /// <summary>Called every frame by ClassCombatDriver.</summary>
        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null) { _chargeUntil = -1f; StepTargets.Clear(); return; }
            if (_chargeUntil >= 0f) TickCharge(me);
            if (StepTargets.Count > 0 && Time.time >= _stepNext) TickStep(me);
        }

        private static void TickCharge(Player me)
        {
            if (Time.time > _chargeUntil || me.IsDead())
            {
                _chargeUntil = -1f; LastChargeHits = ChargeHit.Count;
                Log($"{BreakwallId}: charge over, {ChargeHit.Count} hit{(_closestMissName != null ? $"; closest miss {_closestMissName} at {_closestMiss:0.0} m off the path" : "")}");
                return;
            }
            Vector3 from = me.transform.position;
            // 0.1.98 (R-1012I: "charged 0.0 m"): the player's own FixedUpdate writes the body's velocity from its move input, so a
            // velocity set here never held. The charge now moves the body a step a frame, held to the ground under it.
            Vector3 next = me.transform.position + _chargeDir * ChargeSpeed * Time.deltaTime;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(next, out float ground)) next.y = Mathf.Max(ground, next.y - 0.5f);
            me.transform.position = next;
            SkillFx.Play(BreakwallId + "_dust", next, SkillFx.GroundSlam, 0.3f);
            if (me.m_body != null) { me.m_body.position = next; me.m_body.linearVelocity = Vector3.zero; }
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || ChargeHit.Contains(c) || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                // 0.1.115 (R-1013P: a Greydwarf 5.6 m ahead, 0.8 m off the line, "charge over, 0 hit"): the whole step this frame is swept
                // (a long frame or the body shoving the target aside skipped it when only the end point was checked), on the flat.
                // 0.1.118 (R-1013W: "closest miss Greydwarf at 1.9 m off the path" - centre to centre; its body overlapped the charge): the
                // target's own radius counts, so a charge "through enemies" hits whatever its path touches.
                float off = FlatDistanceToSegment(c.transform.position, from, next) - c.GetRadius();
                if (off > ChargeHitReach) { if (off < _closestMiss) { _closestMiss = off; _closestMissName = c.GetHoverName(); } continue; }
                ChargeHit.Add(c);
                var hit = new HitData { m_point = c.GetCenterPoint(), m_dir = _chargeDir, m_pushForce = 30f };
                hit.m_damage.m_blunt = _chargeBlunt;
                hit.SetAttacker(me);
                c.Damage(hit);
                AbilityRPCManager.ApplySingleEffect(me, c, StatusEffectManager.EFFECT_CLASS_KNOCKDOWN, KnockdownSeconds);
                SkillFx.Play(BreakwallId + "_hit" + c.GetInstanceID(), c.GetCenterPoint(), SkillFx.HeavyHit, 1f);
            }
        }

        private static void TickStep(Player me)
        {
            Character c = StepTargets[0];
            StepTargets.RemoveAt(0);
            _stepNext = Time.time + StepsGap;
            if (c == null || c.IsDead()) return;
            Vector3 away = me.transform.position - c.transform.position;
            away.y = 0f;
            Vector3 spot = c.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : -c.transform.forward) * 1.2f;
            me.transform.position = spot;
            if (me.m_body != null) { me.m_body.position = spot; me.m_body.linearVelocity = Vector3.zero; }
            me.transform.rotation = Quaternion.LookRotation(Flat(c.transform.position - spot, me.transform.forward));
            ItemDrop.ItemData weapon = me.GetCurrentWeapon();
            var hit = new HitData { m_point = c.GetCenterPoint(), m_dir = Flat(c.transform.position - spot, me.transform.forward) };
            hit.m_damage = weapon != null ? weapon.GetDamage() : new HitData.DamageTypes { m_blunt = 10f };
            hit.m_damage.Modify(StepsWeaponShare);
            hit.SetAttacker(me);
            c.Damage(hit);
            AbilityRPCManager.SpawnFX(spot, "vfx_HitSparks");
            StepsStruck++;
            Log($"{StepsId}: struck {c.GetHoverName()} for {hit.GetTotalDamage():0}");
        }

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.01f ? v.normalized : fallback;
        }

        /// <summary>Damage the local player takes: halved while Breakwall charges.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (_chargeUntil >= Time.time && victim == Player.m_localPlayer && hit != null) hit.m_damage.Modify(ChargeTaken);
        }

        /// <summary>0.1.107, for ClassTest (R-1012P: health read-backs on the subjects drifted): the last charge's hits, the last
        /// Thousand Steps' strikes - the skills' own counts.</summary>
        public static int LastChargeHits { get; private set; }
        private static float _closestMiss = float.MaxValue;
        private static string _closestMissName;

        private static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = 0f; a.y = 0f; b.y = 0f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
        public static int StepsStruck { get; private set; }

        /// <summary>For ClassTest.</summary>
        public static bool Charging => _chargeUntil >= Time.time;
        public static int StepsLeft => StepTargets.Count;
    }
}
