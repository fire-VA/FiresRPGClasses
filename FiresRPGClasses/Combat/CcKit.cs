using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Crowd control, built simply: stun = can't move or attack (a
    /// held stagger), knockdown = stagger + push, flee = the creature's own flee AI, lose target = its target cleared, bosses get a
    /// slow instead; all through Core's ClassCrowdControl, which runs each on the target's owner. Boss = vanilla IsBoss(),
    /// undead = the Undead faction. Numbers are the node's yaml values at the caster's rank; unstated ones follow the default rule.
    /// </summary>
    public static class CcKit
    {
        private const string ShieldBashId = "tank_shield_bash";
        private const string HammerId = "paladin_hammer_of_justice";
        private const string HornId = "berserker_horn_of_valhalla";
        private const string AbsoluteZeroId = "mage_absolute_zero";
        private const string FeintId = "rogue_feint";
        private const string TrapperId = "ranger_trapper";
        private const string HoldTheLineId = "tank_hold_the_line";
        private const string PalmId = "monk_palm_of_the_storm";
        private const string PiercingShotId = "ranger_piercing_shot";

        private const float MeleeReach = 3f;
        private const float InstantStagger = 100f;
        private const float HammerStunSeconds = 2f;
        private const float HammerUndeadStunSeconds = 4f;
        // "Your next attack": no window is stated, so it waits this long (listed under "Defaults used").
        private const float NextAttackWindowSeconds = 8f;
        private const float FeintWindowSeconds = 4f;
        private const float HornFleeRadius = 10f;
        private const float HornFleeSeconds = 4f;
        private const float HornBuffSeconds = 15f;
        private const float FreezeSeconds = 3f;
        private const float TrapSeconds = 30f;
        private const float TrapPierce = 30f;
        private const float HoldSeconds = 8f;
        private const int PalmStrikes = 4;
        private const float PalmStrikeGapSeconds = 0.15f;
        private const float PalmStunSeconds = 2f;
        private const float PiercingBonus = 0.2f;
        private const int ConcussiveRank = 3;
        // Core's silence: by name/reflection so this builds against a Core without it.
        private const string SilenceEffect = "ClassSilence";

        // Presets that Core may not have: +25% damage for the horn, 25% less damage taken for rank 3 of
        // Hold the Line. By name, so this builds and runs now; until Core registers them the buff part is refused and logged once.
        private const string Damage25Preset = "ClassDamage25";
        private static readonly string[] HoldByRank = { StatusEffectManager.EFFECT_CLASS_WARD15, StatusEffectManager.EFFECT_CLASS_WARD20, "ClassWard25" };

        private static float _hammerUntil = float.NegativeInfinity;
        private static float _hammerBonus;
        private static float _feintUntil = float.NegativeInfinity;
        private static float _piercingUntil = float.NegativeInfinity;
        private static bool _piercingConcussive;
        private static bool _piercingBreach;
        private static int _piercingTargets = 2;
        private static bool _registered;
        private static bool _silenceResolved;
        private static System.Reflection.MethodInfo _coreSilence;
        private static bool _silenceMissingLogged;
        private static readonly FiresCore.Logging.FiresLog DebugLog = new FiresCore.Logging.FiresLog("FiresRPGClasses", () => false);

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(ShieldBashId, ShieldBash);
            ClassSkillKit.Register(HammerId, Hammer);
            ClassSkillKit.Register(HornId, Horn);
            ClassSkillKit.Register(AbsoluteZeroId, AbsoluteZero);
            ClassSkillKit.Register(FeintId, Feint);
            ClassSkillKit.Register(TrapperId, Trapper);
            ClassSkillKit.Register(HoldTheLineId, HoldTheLine);
            ClassSkillKit.Register(PalmId, Palm);
            ClassSkillKit.Register(PiercingShotId, PiercingShot);
            MageKit.RegisterAll();
        }

        // Ultimate: "Four rapid palm strikes on the enemy in front, {dmg} weapon damage each; the last knocks back and stuns for
        // 2 s." Strikes are spaced on the combat driver; the stun is Core's ClassStun so it interrupts a cast.
        private static bool Palm(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character enemy = InFront(caster, MeleeReach);
            if (enemy == null) return false;
            float dmg = node.Value("dmg", DesignKit.RankOf(node), 0.6f);
            if (ClassCombatDriver.Instance != null) ClassCombatDriver.Instance.StartCoroutine(PalmStrikesRoutine(caster, enemy, dmg));
            else for (int i = 1; i <= PalmStrikes; i++) PalmStrike(caster, enemy, dmg, i == PalmStrikes);
            return true;
        }

        private static System.Collections.IEnumerator PalmStrikesRoutine(Character caster, Character enemy, float dmg)
        {
            for (int i = 1; i <= PalmStrikes; i++)
            {
                if (caster == null || enemy == null || enemy.IsDead()) yield break;
                PalmStrike(caster, enemy, dmg, i == PalmStrikes);
                if (i < PalmStrikes) yield return new WaitForSeconds(PalmStrikeGapSeconds);
            }
        }

        private static void PalmStrike(Character caster, Character enemy, float dmg, bool last)
        {
            HitData hit = HitFrom(caster, enemy);
            ItemDrop.ItemData weapon = (caster as Humanoid)?.GetCurrentWeapon();
            if (weapon != null) hit.m_damage = weapon.GetDamage();
            else hit.m_damage.m_blunt = 10f;
            hit.m_damage.Modify(dmg);
            enemy.Damage(hit);
            AbilityRPCManager.SpawnFX(enemy.transform.position, last ? "fx_Lightning" : "vfx_HitSparks");
            if (!last) return;
            ClassCrowdControl.Knockdown(enemy, caster);
            ClassCrowdControl.Stun(enemy, PalmStunSeconds, caster);
            DebugLog.Debug($"[CcKit] Palm of the Storm: 4th strike, stun {PalmStunSeconds:0} s on {enemy.m_name}");
        }

        // "Your next arrow passes through up to {targets} enemies and deals 20% more damage." Rank 3, Concussive: "the first
        // enemy hit is staggered". The cast arms a window; the next bow/crossbow projectile the local player fires
        // takes it (ClassProjectileSetupHooks) and becomes piercing. Pass-through is vanilla's own m_onlyStopOnTerrain path
        // (Projectile.OnHit: the hit is dealt, m_didHit stays false, the arrow flies on), switched on per call only while the
        // projectile hits a creature with targets left, so terrain/rocks/buildings still stop it. Each enemy hit once (dedup by
        // Character, vanilla's m_hitList is per collider), +20% each, stagger on the first only; the {targets}-th enemy stops
        // it the vanilla way. Rank 2's Breach: every pierced hit is unblockable (ApplyBreachHere).
        private static bool PiercingShot(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = DesignKit.RankOf(node);
            _piercingUntil = Time.time + NextAttackWindowSeconds;
            _piercingConcussive = rank >= ConcussiveRank;
            _piercingBreach = rank >= BreachRank;
            _piercingTargets = Mathf.Max(1, Mathf.RoundToInt(node.Value("targets", rank, 2f)));
            AbilityRPCManager.SpawnFX(caster.transform.position, "fx_shield_start");
            return true;
        }

        internal sealed class PierceState
        {
            public int Left;
            public bool Concussive;
            public bool Breach;
            public bool First = true;
            public readonly System.Collections.Generic.HashSet<Character> Hit = new System.Collections.Generic.HashSet<Character>();
        }

        // What a piercing OnHit call changed, so the postfix puts the prefab values back.
        internal sealed class PierceCall
        {
            public PierceState State;
            public bool PassThrough;
            public bool OnlyStopOnTerrain, AttachToClosestBone, AttachToRigidBody, StayAfterHitDynamic, StayAfterHitStatic;
        }

        private static readonly System.Collections.Generic.Dictionary<Projectile, PierceState> _piercingShots =
            new System.Collections.Generic.Dictionary<Projectile, PierceState>();
        private static PierceState _pierceHit;   // set only while a piercing projectile's OnHit runs (owner's peer)

        /// <summary>From ClassProjectileSetupHooks: the local player's bow/crossbow projectile takes an armed Piercing Shot.</summary>
        internal static void OnProjectileSetup(Projectile projectile, Character owner, ItemDrop.ItemData item)
        {
            if (projectile == null || owner == null || owner != Player.m_localPlayer || Time.time > _piercingUntil) return;
            Skills.SkillType skill = item?.m_shared?.m_skillType ?? Skills.SkillType.None;
            if (skill != Skills.SkillType.Bows && skill != Skills.SkillType.Crossbows) return;
            _piercingUntil = float.NegativeInfinity;
            foreach (Projectile gone in _piercingShots.Keys.Where(k => k == null).ToList()) _piercingShots.Remove(gone);
            _piercingShots[projectile] = new PierceState { Left = Mathf.Max(1, _piercingTargets), Concussive = _piercingConcussive, Breach = _piercingBreach };
            DebugLog.Debug($"[CcKit] Piercing Shot armed on {projectile.name}: {_piercingTargets} target(s){(_piercingConcussive ? ", concussive" : "")}");
        }

        /// <summary>Projectile.OnHit prefix. False = skip (a creature this arrow already hit, through another collider).</summary>
        internal static bool BeforeProjectileHit(Projectile projectile, Collider collider, out PierceCall call)
        {
            call = null;
            _pierceHit = null;
            if (projectile == null || !_piercingShots.TryGetValue(projectile, out PierceState state)) return true;
            GameObject hitObject = collider ? Projectile.FindHitObject(collider) : null;
            Character creature = hitObject ? hitObject.GetComponent<Character>() : null;
            if (creature == null) return true;   // terrain / static / water: vanilla stops it as usual
            if (state.Hit.Contains(creature)) return false;
            call = new PierceCall { State = state, PassThrough = state.Left > 1 };
            _pierceHit = state;
            if (call.PassThrough)
            {
                call.OnlyStopOnTerrain = projectile.m_onlyStopOnTerrain;
                call.AttachToClosestBone = projectile.m_attachToClosestBone;
                call.AttachToRigidBody = projectile.m_attachToRigidBody;
                call.StayAfterHitDynamic = projectile.m_stayAfterHitDynamic;
                call.StayAfterHitStatic = projectile.m_stayAfterHitStatic;
                projectile.m_onlyStopOnTerrain = true;      // vanilla: hit dealt, m_didHit not set, keeps flying
                projectile.m_attachToClosestBone = false;   // don't stick the flying arrow to this creature
                projectile.m_attachToRigidBody = false;
                projectile.m_stayAfterHitDynamic = true;    // don't destroy it on this creature
                projectile.m_stayAfterHitStatic = true;
            }
            return true;
        }

        /// <summary>Projectile.OnHit postfix: restore the prefab values; forget the arrow once it has stopped.</summary>
        internal static void AfterProjectileHit(Projectile projectile, PierceCall call)
        {
            _pierceHit = null;
            if (call == null || projectile == null) return;
            if (call.PassThrough)
            {
                projectile.m_onlyStopOnTerrain = call.OnlyStopOnTerrain;
                projectile.m_attachToClosestBone = call.AttachToClosestBone;
                projectile.m_attachToRigidBody = call.AttachToRigidBody;
                projectile.m_stayAfterHitDynamic = call.StayAfterHitDynamic;
                projectile.m_stayAfterHitStatic = call.StayAfterHitStatic;
            }
            if (call.State.Left <= 0) _piercingShots.Remove(projectile);
        }

        // Rank 2 Breach (yaml counter shieldBreak): the hit can't be blocked or parried by the enemy it passes, reusing the
        // unblockable-hit path Feint already uses (hit.m_blockable = false).
        private const int BreachRank = 2;
        private static void ApplyBreachHere(PierceState state, HitData hit) { if (state.Breach) hit.m_blockable = false; }

        private static bool IsBowHit(Player player, HitData hit)
        {
            Skills.SkillType skill = hit.m_skill != Skills.SkillType.None ? hit.m_skill
                : player.GetCurrentWeapon()?.m_shared.m_skillType ?? Skills.SkillType.None;
            return skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows;
        }

        /// <summary>
        /// Silence (can't cast) for mage_ice_lance r3, mage_arcane_torrent r1 and Mana Drain. Core's ClassCrowdControl.Silence when
        /// this Core has it (found by reflection, exactly (Character, Character, float)), else the "ClassSilence" effect by name; neither = one
        /// Debug line and nothing.
        /// </summary>
        internal static bool SilenceSoft(Character target, Character caster, float seconds)
        {
            if (target == null || seconds <= 0f) return false;
            if (!_silenceResolved)
            {
                _silenceResolved = true;
                _coreSilence = typeof(ClassCrowdControl).GetMethod("Silence", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(Character), typeof(Character), typeof(float) }, null);
            }
            // Core's ClassCrowdControl.Silence(Character target, Character caster, float seconds).
            // No by-name fallback: an unknown SE name would still go out over the network to other peers.
            if (_coreSilence != null)
            {
                _coreSilence.Invoke(null, new object[] { target, caster, seconds });
                return true;
            }
            if (!_silenceMissingLogged)
            {
                _silenceMissingLogged = true;
                DebugLog.Debug("[CcKit] ClassSilence not in this Core; silence skipped");
            }
            return false;
        }

        // "Bash the enemy in front: {blunt} blunt damage, full stagger damage, knockback."
        private static bool ShieldBash(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character enemy = InFront(caster, MeleeReach);
            if (enemy == null) return false;
            HitData hit = HitFrom(caster, enemy);
            hit.m_damage.m_blunt = node.Value("blunt", DesignKit.RankOf(node));
            hit.m_staggerMultiplier = InstantStagger;
            enemy.Damage(hit);
            ClassCrowdControl.Knockdown(enemy, caster);
            AbilityRPCManager.SpawnFX(enemy.transform.position, "vfx_sledge_hit");
            return true;
        }

        // "Your next attack deals {bonus} bonus spirit damage and stuns for 2 s (4 s against undead)."
        private static bool Hammer(ClassTreeNode node, Character caster, CastTarget target)
        {
            _hammerBonus = node.Value("bonus", DesignKit.RankOf(node));
            _hammerUntil = Time.time + NextAttackWindowSeconds;
            AbilityRPCManager.SpawnFX(caster.transform.position, "fx_shield_start");
            return true;
        }

        // "Within 4 s, your next attack ignores block and staggers."
        private static bool Feint(ClassTreeNode node, Character caster, CastTarget target)
        {
            _feintUntil = Time.time + FeintWindowSeconds;
            AbilityRPCManager.SpawnFX(caster.transform.position, "vfx_odin_despawn");
            return true;
        }

        /// <summary>From PassiveKit's outgoing-hit hook: the local player's hit before it is sent.</summary>
        internal static void OnLocalHitSending(Character target, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if (player == null || target == null || hit == null || hit.GetAttacker() != player) return;
            if (!ClassTargeting.IsEnemyTarget(player, target)) return;

            if (Time.time <= _hammerUntil)
            {
                _hammerUntil = float.NegativeInfinity;
                // the bonus is holy (blunt + half again as spirit); spirit alone added nothing on non-undead.
                ZoneKit.AddHoly(hit, hit.GetTotalDamage() * _hammerBonus);
                bool undead = target.m_faction == Character.Faction.Undead;
                ClassCrowdControl.Stun(target, undead ? HammerUndeadStunSeconds : HammerStunSeconds, player);
                AbilityRPCManager.SpawnFX(target.transform.position, "fx_Lightning");
            }
            if (Time.time <= _feintUntil)
            {
                _feintUntil = float.NegativeInfinity;
                hit.m_blockable = false;
                hit.m_staggerMultiplier = Mathf.Max(hit.m_staggerMultiplier, InstantStagger);
            }
            if (_pierceHit != null)
            {
                // A piercing projectile's hit (inside its Projectile.OnHit): every enemy +20%, the first one staggered.
                PierceState state = _pierceHit;
                _pierceHit = null;
                state.Hit.Add(target);
                state.Left--;
                hit.m_damage.Modify(1f + PiercingBonus);
                bool stagger = state.Concussive && state.First;
                if (stagger) hit.m_staggerMultiplier = Mathf.Max(hit.m_staggerMultiplier, InstantStagger);
                state.First = false;
                ApplyBreachHere(state, hit);
                DebugLog.Debug($"[CcKit] Piercing Shot hit {target.m_name}, {state.Left} left{(stagger ? ", concussive stagger" : "")}");
            }
            else if (Time.time <= _piercingUntil && IsBowHit(player, hit))
            {
                // Fallback: the window is still armed (projectile hook didn't take it) - first bow hit only.
                _piercingUntil = float.NegativeInfinity;
                hit.m_damage.Modify(1f + PiercingBonus);
                if (_piercingConcussive) hit.m_staggerMultiplier = Mathf.Max(hit.m_staggerMultiplier, InstantStagger);
                DebugLog.Debug($"[CcKit] Piercing Shot hit {target.m_name}{(_piercingConcussive ? ", concussive stagger" : "")}");
            }
        }

        // "Sound a war horn: allies within 15 m deal 25% more damage for 15 s, and non-boss enemies within 10 m flee for 4 s."
        private static bool Horn(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 15f;
            AbilityRPCManager.SpawnFX(caster.transform.position, "vfx_MeadBzerker");
            int buffed = AbilityRPCManager.ApplyGroupBuff(caster, Damage25Preset, radius, HornBuffSeconds);
            int fled = 0;
            foreach (Character enemy in EnemiesAround(caster, caster.transform.position, HornFleeRadius))
            {
                if (enemy.IsBoss()) continue;
                ClassCrowdControl.Flee(enemy, HornFleeSeconds, caster);
                fled++;
            }
            return buffed > 0 || fled > 0;
        }

        // "Freeze every enemy within 8 m solid for 3 s. Bosses are slowed 60% instead." Core's Stun holds a stagger and gives
        // bosses its slow.
        private static bool AbsoluteZero(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 8f;
            // Only vfx_ice_destroyed: fx_fenring_icenova's ground decal and the player-attached vfx_Cold aura can render as a
            // black square when spawned loose on voxel ground.
            AbilityRPCManager.SpawnFX(caster.transform.position, "vfx_ice_destroyed");
            bool any = false;
            foreach (Character enemy in EnemiesAround(caster, caster.transform.position, radius))
            {
                // a boss gets the named slow instead of the stun - "Bosses are slowed 60% instead".
                ClassCrowdControl.Stun(enemy, FreezeSeconds, caster, StatusEffectManager.EFFECT_CLASS_SLOW60);
                AbilityRPCManager.SpawnFX(enemy.transform.position, "vfx_ice_destroyed");
                any = true;
            }
            return any;
        }

        // "Place a snare within 15 m for 30 s: the first enemy within 3 m of it is rooted for {root} and takes 30 pierce damage."
        private static bool Trapper(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 3f;
            float root = node.Value("root", DesignKit.RankOf(node), 3f);
            Vector3 point = target.Resolved ? target.Point : caster.transform.position;
            ClassZone trap = null;
            trap = ClassZones.ZoneAt(caster, point, radius, TrapSeconds, ZoneKind.Harmful, enemy =>
            {
                if (trap == null || !trap.Running) return;
                AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_ROOTED, root);
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_pierce = TrapPierce;
                enemy.Damage(hit);
                AbilityRPCManager.SpawnFX(enemy.transform.position, "vfx_arrowhit");
                trap.Stop();
            }, 0.25f, "vfx_Place_raise", 5f);
            if (trap != null) TargetingController.ShowCastRing(node, point, TrapSeconds);
            return trap != null;
        }

        // Ultimate: "Plant your feet for 8 s: you and allies within 6 m can't be staggered and take {dr} less damage."
        private static bool HoldTheLine(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 6f;
            string ward = HoldByRank[Mathf.Clamp(DesignKit.RankOf(node), 1, HoldByRank.Length) - 1];
            AbilityRPCManager.ApplyGroupBuff(caster, ward, radius, HoldSeconds);
            bool any = false;
            foreach (Character ally in Character.GetAllCharacters())
            {
                if (ally == null || ally.IsDead() || Vector3.Distance(ally.transform.position, caster.transform.position) > radius) continue;
                if (!ClassTargeting.IsPartyMember(caster, ally) && ally != caster) continue;
                ClassCrowdControl.SetStaggerImmune(ally, HoldSeconds, caster);
                any = true;
            }
            AbilityRPCManager.SpawnFX(caster.transform.position, "fx_eikthyr_stomp");
            return any;
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private static System.Collections.Generic.IEnumerable<Character> EnemiesAround(Character caster, Vector3 center, float radius) =>
            Character.GetAllCharacters().Where(enemy => enemy != null && !enemy.IsDead()
                && Vector3.Distance(enemy.transform.position, center) <= radius && ClassTargeting.IsEnemyTarget(caster, enemy)).ToList();

        private static Character InFront(Character caster, float reach)
        {
            Vector3 at = caster.transform.position;
            Vector3 forward = caster.transform.forward;
            return EnemiesAround(caster, at, reach)
                .Where(enemy => Vector3.Dot((enemy.transform.position - at).normalized, forward) > 0.3f)
                .OrderBy(enemy => Vector3.Distance(enemy.transform.position, at))
                .FirstOrDefault();
        }

        private static HitData HitFrom(Character caster, Character enemy)
        {
            Vector3 away = enemy.transform.position - caster.transform.position;
            away = Ring.Flat(away, enemy.transform.position);   // off the halo ring: away.y = 0
            var hit = new HitData
            {
                m_point = enemy.GetCenterPoint(),
                m_dir = away.sqrMagnitude > 0.01f ? away.normalized : caster.transform.forward,
            };
            hit.SetAttacker(caster);
            return hit;
        }
    }
}
