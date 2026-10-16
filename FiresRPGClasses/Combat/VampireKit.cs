using System.Collections;
using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Possession;
using FiresCore.SkillSystem;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The Vampire class kit (Tools\VAMPIRE_CLASS_DESIGN.md §3-§5, §10-§12, LOCKED 10-13). The class needs the vampire race
    /// (<see cref="VampireRace"/>, read by WeaponArchetypes: without it the class is greyed out like a missing weapon, points kept).
    /// - Bat Form (skill, 3 ranks): Core possession on Druid Wildshape's path. A vanilla Bat is spawned at the player, made harmless while
    ///   the server is asked (players' faction, AI off), and possessed a second later (the server must have its ZDO, Wildshape's
    ///   ShiftInto lesson) under the "rpg-vampire-bat" validator (VampireServer: the race + a Bat). Body Hidden as Wildshape asks (the
    ///   engine parks a non-humanoid form's body frozen in place) + PlayerInvulnerable; ConsumeTarget (the bat goes at the end); the bat's
    ///   pool = 30% of max health, its hits do nothing (DamageScale 0, and the engine fires attacks only for humanoids). Duration
    ///   45 + 1.35 x skill (config base/max), cooldown 90 + 0.45 x skill from the END of the form, rank 2 -20%, rank 3 +15% speed and
    ///   perching. 15 eitr once, no drain. Use opens doors / picks items up near the bat (into your own inventory). Ends: time, the
    ///   skill pressed again, Core's release key, landing (ranks 1-2), losing 25% of the pool, a portal/teleport (Core), death. The
    ///   player then stands where the bat was. A pool emptied by one hit ends it and the overflow reaches you at half.
    /// - Life Drain (passive, 3 ranks): 4/6/8% of melee damage heals you; capped at 5% max health a hit; half on bosses; none from tamed.
    /// - Blood Frenzy (skill): 8 s life-steal x2 and +15% attack speed, costs 10% of current health.
    /// - Night Stalker (passive): at night +10% damage and ClassNightStalker (+15% stealth); its "by day" is the sunlight rule.
    /// - Mesmerise (skill): the enemy under the crosshair (non-boss, max health at most 600) is stunned 3 s (Core's ClassStun).
    /// - Crimson Tide (ultimate): 10 s, you and allies within 15 m (ClassCrimsonTide) life-steal 10% of every hit.
    /// - Sunlight (race, [12 - Vampire] Sunlight, default Mild): in direct daylight (EnvMan daylight, not InShelter, not inside) Mild =
    ///   ClassSunlight (-10% stamina regen) and no Life Drain; Harsh = 1% max health every 5 s; None. RPG_SunWard (Duskbloom Stew) lifts it.
    /// Everything acts for the local player; other peers see the bat through Core's possession sync.
    /// </summary>
    public static class VampireKit
    {
        internal const string BatFormId = "vampire_bat_form";
        internal const string LifeDrainId = "vampire_life_drain";
        internal const string BloodFrenzyId = "vampire_blood_frenzy";
        internal const string NightStalkerId = "vampire_night_stalker";
        internal const string MesmeriseId = "vampire_mesmerise";
        internal const string CrimsonTideId = "vampire_crimson_tide";
        private static readonly string[] PassiveIds = { LifeDrainId, NightStalkerId };

        private const float BatSpawnDelay = 1f;          // the 1 s cast: the server must see the bat's ZDO before the grant
        private const float BatSpawnHeight = 1.5f;
        private const float FrenzySeconds = 8f;
        private const float MesmeriseRange = 20f;
        private const float TideRadius = 15f;
        private const float TideSeconds = 10f;
        private const string BatFx = "vfx_odin_despawn";

        private static bool _registered;

        // Bat Form
        private static bool _batPending;
        private static PossessHandle _bat;
        private static GameObject _batGo;
        private static int _batRank;
        private static float _batSeconds, _batLevel, _pendingEitr, _flown, _lastHurtAt = float.NegativeInfinity;
        private static bool _flewOnce;
        private static Vector3 _lastBatPos;
        private static int _halveSpillFrame = -1;

        // Sunlight / passives
        private static bool _sunPenalty, _stalkerOn, _sunlit;
        private static float _nextBurn, _nextRead;
        private static readonly Dictionary<string, float> Passives = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Shown = new Dictionary<string, float>();
        private static string _lastSunLine = "";

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(BatFormId, BatForm);
            ClassSkillKit.Register(BloodFrenzyId, BloodFrenzy);
            ClassSkillKit.Register(MesmeriseId, Mesmerise);
            ClassSkillKit.Register(CrimsonTideId, CrimsonTide);
            foreach (string id in PassiveIds) ClassSkillKit.RegisterPassive(id);
            PossessionApi.Started += OnStarted;
            PossessionApi.Ended += OnEnded;
            VampireServer.RegisterValidators();
            VampireSkill.Register(false);
        }

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Vampire] " + s);

        private static bool Refuse(Player player, ClassTreeNode node, string why)
        {
            player?.Message(MessageHud.MessageType.Center, node.DisplayName + ": " + why);
            Log($"{node.Id} refused: {why}");
            return false;
        }

        // ── Bat Form ──────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>True while the local player is a bat.</summary>
        public static bool BatActive => _bat != null;   // cleared in OnEnded

        /// <summary>Duration from the Bat Form skill (§11): base + (max - base) x level / 100; 45 / 113 / 180 s at 0 / 50 / 100.</summary>
        public static float BatDuration(float level)
        {
            float b = VampireConfig.Value(VampireConfig.BatBaseSeconds, 45f), m = VampireConfig.Value(VampireConfig.BatMaxSeconds, 180f);
            return b + (m - b) * Mathf.Clamp(level, 0f, 100f) / 100f;
        }

        /// <summary>
        /// Cooldown from the skill (§11), counted from the form's end; rank 2+ cuts it 20%. 0.1.131 (Fire 10-14): then the generic
        /// class cooldown reduction (CooldownTracker.Scaled, the same path every class skill's cooldown takes) applies on top.
        /// </summary>
        public static float BatCooldown(float level, int rank)
        {
            float b = VampireConfig.Value(VampireConfig.BatCooldownBase, 90f), m = VampireConfig.Value(VampireConfig.BatCooldownMax, 135f);
            float cd = b + (m - b) * Mathf.Clamp(level, 0f, 100f) / 100f;
            if (rank >= 2) cd *= 1f - VampireConfig.Value(VampireConfig.BatRank2Cooldown, 0.20f);
            return CooldownTracker.Scaled(BatFormId, cd);
        }

        private static bool BatForm(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            if (BatActive) { EndBat("cancelled (pressed again)"); return true; }
            if (!VampireRace.Local) return Refuse(player, node, "you are not of the vampire race");
            if (_batPending) return Refuse(player, node, "waiting for the server");
            if (PossessionApi.Current != null) return Refuse(player, node, "you are already in another form");
            float lockout = VampireConfig.Value(VampireConfig.BatCombatLock, 5f);
            if (Time.time - _lastHurtAt < lockout) return Refuse(player, node, $"you were hurt {Time.time - _lastHurtAt:0.0} s ago (wait {lockout:0} s)");
            float eitr = VampireConfig.Value(VampireConfig.BatEitr, 15f);
            if (!player.HaveEitr(eitr)) return Refuse(player, node, "not enough eitr");
            GameObject source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(VampireServer.BatPrefab) : null;
            if (source == null) return Refuse(player, node, "no Bat prefab in this world");

            _batRank = DesignKit.RankOf(node);
            _batLevel = VampireSkill.Level(player);
            _batSeconds = BatDuration(_batLevel);
            Vector3 at = player.transform.position + Vector3.up * BatSpawnHeight;
            GameObject go = Object.Instantiate(source, at, player.transform.rotation);
            Character bat = go.GetComponent<Character>();
            if (bat == null) { ZNetScene.instance.Destroy(go); return Refuse(player, node, "the Bat has no body"); }
            // Harmless while the server is asked: the players' faction and its AI off (the engine drives it once granted).
            bat.m_faction = Character.Faction.Players;
            BaseAI ai = bat.GetComponent<BaseAI>();
            if (ai != null) ai.enabled = false;
            float speed = 1f + VampireConfig.Value(VampireConfig.BatSpeed, 0.30f) + (_batRank >= 3 ? VampireConfig.Value(VampireConfig.BatRank3Speed, 0.15f) : 0f);
            bat.m_flyFastSpeed *= speed;
            bat.m_flySlowSpeed *= speed;
            bat.m_runSpeed *= speed;
            bat.m_speed *= speed;

            player.UseEitr(eitr);
            _pendingEitr = eitr;
            _batPending = true;
            _batGo = go;
            AbilityRPCManager.SpawnFX(player.transform.position, BatFx);

            var options = new PossessOptions
            {
                Body = PossessBody.Hidden,          // Wildshape's ask; a bat isn't humanoid-rigged, so the engine parks the body frozen
                PlayerInvulnerable = true,          // the parked body can't be hurt (§3)
                ConsumeTarget = true,               // the bat goes at the end
                ReturnToAnchor = false,
                MaxDuration = _batSeconds,
                CarryHiddenBody = true,             // Core 0.2.447 (Fire §13): the body vanishes, is carried inside the bat, the world streams around it - no leash
                BodyVanishFx = BatFx,               // the puff where the body vanishes and where it reappears
                CarryEndAtCreature = true,          // a normal end sets you down under the bat; the bat dying sends you back to the body, unharmed
                BodyRiseBeforeVanish = 2f,          // Core 0.2.453 (Fire, R-1013BU): float up like the Necromancer first, THEN the puff
                BodyRiseSeconds = 0.8f,
                FormHealth = player.GetMaxHealth() * VampireConfig.Value(VampireConfig.BatHealthShare, 0.30f),
                DamageScale = 0f,                   // no attacking (§10)
                Filter = c => IsBat(c) ? null : "only a bat",
                SourceTag = VampireServer.BatTag,
            };
            Log($"{BatFormId}: Bat spawned at {at:F1}; asking for it in {BatSpawnDelay:0} s (rank {_batRank}, skill {_batLevel:0.0}, {_batSeconds:0} s, "
                + $"pool {options.FormHealth:0}, speed x{speed:0.00}, {eitr:0} eitr paid)");
            player.StartCoroutine(BeginBatLater(player, node, bat, options));
            return true;
        }

        private static bool IsBat(Character c) => c != null && global::Utils.GetPrefabName(c.gameObject) == VampireServer.BatPrefab;

        private static IEnumerator BeginBatLater(Player player, ClassTreeNode node, Character bat, PossessOptions options)
        {
            yield return new WaitForSeconds(BatSpawnDelay);
            if (!_batPending) yield break;
            string why = null;
            if (player == null || player.IsDead()) why = "you died";
            else if (bat == null || bat.IsDead()) why = "the bat is gone";
            else if (!PossessionApi.CanPossess(bat, options, out string cant)) why = cant;
            if (why != null) { RefuseBat(why); if (player != null) player.Message(MessageHud.MessageType.Center, node.DisplayName + ": " + why); yield break; }
            PossessionApi.Begin(bat, options);   // Started when the server grants it; Ended(Revoked) when refused (refunded in OnEnded)
        }

        // Before the grant: the eitr and the cooldown come back, the bat goes.
        private static void RefuseBat(string why)
        {
            _batPending = false;
            Player p = Player.m_localPlayer;
            if (p != null && _pendingEitr > 0f) p.AddEitr(_pendingEitr);
            CooldownTracker.Reset(BatFormId);
            DestroyBat();
            Log($"{BatFormId} refused ({why}): {_pendingEitr:0} eitr and the cooldown refunded, bat removed");
            _pendingEitr = 0f;
        }

        private static void DestroyBat()
        {
            if (_batGo == null) return;
            ZNetView nv = _batGo.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid() && ZNetScene.instance != null) ZNetScene.instance.Destroy(_batGo);
            _batGo = null;
        }

        private static void OnStarted(PossessHandle handle)
        {
            if (handle == null || handle.SourceTag != VampireServer.BatTag) return;
            _bat = handle;
            _batPending = false;
            _pendingEitr = 0f;
            _flown = 0f;
            _flewOnce = false;
            if (handle.Creature != null) _lastBatPos = handle.Creature.transform.position;
            SetZdoFlag(handle.Player, true);
            Log($"{BatFormId} on: in the bat for {handle.Options.MaxDuration:0} s, pool {handle.FormHealthMax:0}, rank {_batRank} "
                + $"({(_batRank >= 3 ? "perching" : "landing ends it")}), skill {_batLevel:0.0}");
        }

        private static void OnEnded(PossessHandle handle, PossessEndReason reason)
        {
            if (handle == null || handle.SourceTag != VampireServer.BatTag) return;
            if (handle != _bat)
            {
                if (_batPending) RefuseBat("server/engine: " + reason);
                return;
            }
            _bat = null;
            Player player = handle.Player != null ? handle.Player : Player.m_localPlayer;
            Vector3 at = handle.Creature != null ? handle.Creature.transform.position : _lastBatPos;
            string placed = player != null && !player.IsDead() ? Place(player, at) : "not placed (no living player)";
            if (player != null) SetZdoFlag(player, false);
            float cooldown = BatCooldown(_batLevel, _batRank);
            CooldownTracker.Start(BatFormId, cooldown);
            if (reason == PossessEndReason.Death) _halveSpillFrame = Time.frameCount;   // Core's overflow hit follows in this frame
            DestroyBat();
            if (player != null) AbilityRPCManager.SpawnFX(player.transform.position, BatFx);
            Log($"{BatFormId} off ({reason}) after {handle.Elapsed:0.0} s: {placed}; cooldown {cooldown:0} s (skill {_batLevel:0.0}, rank {_batRank})");
        }

        /// <summary>Ends the bat now (Core's End, which raises Ended → the player returns where the bat is).</summary>
        internal static void EndBat(string why)
        {
            if (!BatActive) return;
            Log($"{BatFormId} ending: {why}");
            PossessionApi.End(_bat, PossessEndReason.Released);
        }

        // The player stands where the bat was: on the first surface under it (a roof, a branch, the ground).
        private static string Place(Player player, Vector3 at)
        {
            Vector3 to = at;
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore))
                to = hit.point + Vector3.up * 0.05f;
            else if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float ground) && ground < at.y)
                to = new Vector3(at.x, ground + 0.05f, at.z);
            player.transform.position = to;
            if (player.m_body != null) { player.m_body.position = to; player.m_body.linearVelocity = Vector3.zero; }
            player.m_maxAirAltitude = to.y;   // no fall damage from the parked spot
            return $"returned at {to:F1} (bat at {at:F1})";
        }

        private static void SetZdoFlag(Player player, bool on)
        {
            ZDO zdo = player != null && player.m_nview != null && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
            int key = VampireRace.BatActiveKey.GetStableHashCode();
            if (zdo != null && zdo.GetBool(key, false) != on) zdo.Set(key, on);
        }

        /// <summary>ClassCombatDriver.Press: pressing Bat Form while a bat ends it (its slot is on cooldown then). True = used up.</summary>
        internal static bool CancelOnPress(string nodeId)
        {
            if (nodeId != BatFormId) return false;
            if (BatActive) { EndBat("cancelled (pressed again)"); return true; }
            return false;
        }

        private static void TickBat(Player player)
        {
            PossessHandle h = _bat;
            Character bat = h.Creature;
            if (bat == null) return;
            _lastBatPos = bat.transform.position;

            float share = VampireConfig.Value(VampireConfig.BatEarlyEndShare, 0.25f);
            if (share < 1f && h.FormHealthMax > 0f && h.FormHealthMax - h.FormHealth >= h.FormHealthMax * share)
            {
                EndBat($"the bat lost {(h.FormHealthMax - h.FormHealth):0} of {h.FormHealthMax:0} ({share:0%} ends it)");
                return;
            }

            if (bat.IsFlying()) _flewOnce = true;
            else if (_flewOnce && _batRank < 3) { EndBat("landed (perching needs rank 3)"); return; }

            _flown += Time.deltaTime;
            if (_flown >= 1f)
            {
                _flown -= 1f;
                float gain = VampireConfig.Value(VampireConfig.BatSkillPerSecond, 0.1f);
                if (gain > 0f) player.RaiseFiresSkill(VampireSkill.Type, gain);
            }

            if (!BlockedByUi() && ZInput.GetButtonDown("Use")) BatUse(player, bat);
        }

        // §10: doors and item pickups are allowed (the picked item goes to the player's own inventory: vanilla's weight rules); nothing else.
        private static void BatUse(Player player, Character bat)
        {
            float reach = VampireConfig.Value(VampireConfig.BatReach, 3f);
            Vector3 at = bat.GetCenterPoint();
            Component best = null;
            float bestDist = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(at, reach, ~0, QueryTriggerInteraction.Collide))
            {
                if (col == null) continue;
                Component c = (Component)col.GetComponentInParent<Door>() ?? col.GetComponentInParent<ItemDrop>();
                if (c == null) continue;
                float d = Vector3.Distance(at, col.ClosestPoint(at));
                if (d < bestDist) { bestDist = d; best = c; }
            }
            if (best == null) { Log($"Use as a bat: nothing to open or pick up within {reach:0.#} m"); return; }
            bool done = best is Door door ? door.Interact(player, false, false) : ((ItemDrop)best).Interact(player, false, false);
            string what = global::Utils.GetPrefabName(best.gameObject);
            Log($"Use as a bat: {(best is Door ? "door" : "item")} {what} at {bestDist:0.0} m: {(done ? "done" : "refused")}");
        }

        private static bool BlockedByUi()
        {
            if (FiresCore.Input.FiresInputBlock.IsTyping()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            return Menu.IsVisible() || InventoryGui.IsVisible() || TextInput.IsVisible() || Console.IsVisible();
        }

        // ── Blood Frenzy, Mesmerise, Crimson Tide ─────────────────────────────────────────────────────────────────────

        // "8 s: life-steal x2, +15% attack speed; costs 10% of your current health."
        private static bool BloodFrenzy(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            float cost = player.GetHealth() * VampireConfig.Value(VampireConfig.FrenzyHealthCost, 0.10f);
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : FrenzySeconds;
            if (!AbilityRPCManager.ApplySelfBuff(player, VampireEffects.BloodFrenzy, seconds)) return Refuse(player, node, "it didn't take hold");
            player.SetHealth(Mathf.Max(1f, player.GetHealth() - cost));
            Log($"{BloodFrenzyId}: {seconds:0} s, paid {cost:0.0} health (now {player.GetHealth():0}/{player.GetMaxHealth():0})");
            return true;
        }

        // "The enemy under your crosshair (not a boss, at most 600 max health) is held still for 3 s."
        private static bool Mesmerise(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            float range = node.Targeting.Range > 0f ? node.Targeting.Range : MesmeriseRange;
            Character enemy = target.TargetCharacter != null && target.TargetCharacter != player ? target.TargetCharacter : Crosshair(player, range);
            if (enemy == null || enemy.IsDead()) return Refuse(player, node, "no creature under your crosshair");
            if (!ClassTargeting.IsEnemyTarget(player, enemy)) return Refuse(player, node, "not an enemy");
            if (enemy.IsBoss()) return Refuse(player, node, "a boss's will is too strong");
            float cap = VampireConfig.Value(VampireConfig.MesmeriseMaxHealth, 600f);
            if (enemy.GetMaxHealth() > cap) return Refuse(player, node, $"too strong ({enemy.GetMaxHealth():0} health, at most {cap:0})");
            float seconds = VampireConfig.Value(VampireConfig.MesmeriseSeconds, 3f);
            CrowdControlResult result = ClassCrowdControl.Stun(enemy, seconds, player);
            Log($"{MesmeriseId}: {enemy.m_name} ({enemy.GetMaxHealth():0} max health) stun {seconds:0.#} s: {result}");
            return result == CrowdControlResult.Applied || Refuse(player, node, "it resisted (" + result + ")");
        }

        private static Character Crosshair(Player player, float range)
        {
            if (GameCamera.instance == null) return null;
            Transform cam = GameCamera.instance.transform;
            int mask = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv", "Default", "static_solid", "terrain", "piece");
            RaycastHit[] hits = Physics.RaycastAll(cam.position, cam.forward, range + 12f, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (RaycastHit hit in hits)
            {
                Character found = hit.collider != null ? hit.collider.GetComponentInParent<Character>() : null;
                if (found == null)
                {
                    if (hit.collider != null && hit.collider.attachedRigidbody == null) return null;   // a wall in the way
                    continue;
                }
                if (found == player || found.IsDead()) continue;
                return Vector3.Distance(player.transform.position, found.transform.position) <= range + 1f ? found : null;
            }
            return null;
        }

        // "For 10 s every hit by you and your allies within 15 m life-steals 10%."
        private static bool CrimsonTide(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : TideRadius;
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : TideSeconds;
            int n = AbilityRPCManager.ApplyGroupBuff(player, VampireEffects.CrimsonTide, radius, seconds);
            if (!player.GetSEMan().HaveStatusEffect(VampireEffects.CrimsonTideHash)) AbilityRPCManager.ApplySelfBuff(player, VampireEffects.CrimsonTide, seconds);
            AbilityRPCManager.SpawnFX(player.transform.position, AbilityFXManager.FX_TAUNT_SHOCKWAVE, Mathf.Clamp(radius / 8f, 0.5f, 2.5f));
            Log($"{CrimsonTideId}: ClassCrimsonTide on {n} party member(s) within {radius:0} m for {seconds:0} s ({VampireConfig.Value(VampireConfig.TideShare, 0.10f):0%} life-steal)");
            return true;
        }

        // ── hooks ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private static bool Has(Player p, int hash) => p != null && p.GetSEMan() != null && p.GetSEMan().HaveStatusEffect(hash);

        /// <summary>
        /// Character.Damage prefix (PassiveHooks), for every hit this client sends. The local player's hits: Night Stalker's damage, then
        /// life-steal (Life Drain, Blood Frenzy, Crimson Tide). And Core's bat overflow hit on the player, halved (§3).
        /// </summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if (player == null || victim == null || hit == null) return;
            if (victim == player)
            {
                if (_halveSpillFrame == Time.frameCount)
                {
                    _halveSpillFrame = -1;
                    float share = VampireConfig.Value(VampireConfig.BatOverflowShare, 0.5f);
                    float was = hit.GetTotalDamage();
                    hit.m_damage.Modify(share);
                    Log($"bat overflow on the player: {was:0.0} -> {hit.GetTotalDamage():0.0} (x{share:0.##})");
                }
                return;
            }
            if (hit.m_attacker != player.GetZDOID()) return;

            if (PassiveOn(NightStalkerId) && EnvMan.IsNight())
                hit.m_damage.Modify(1f + VampireConfig.Value(VampireConfig.StalkerDamage, 0.10f));

            float steal = 0f;
            string parts = "";
            bool drainOff = DrainBlockedBySun(player);
            if (!hit.m_ranged && PassiveOn(LifeDrainId))
            {
                if (drainOff) parts += "Life Drain off in sunlight; ";
                else { steal += Passive(LifeDrainId); parts += $"drain {Passive(LifeDrainId):0%}; "; }
            }
            if (steal > 0f && Has(player, VampireEffects.BloodFrenzyHash))
            {
                float f = VampireConfig.Value(VampireConfig.FrenzyDrainFactor, 2f);
                steal *= f;
                parts += $"frenzy x{f:0.#}; ";
            }
            if (Has(player, VampireEffects.CrimsonTideHash))
            {
                float t = VampireConfig.Value(VampireConfig.TideShare, 0.10f);
                steal += t;
                parts += $"tide {t:0%}; ";
            }
            if (steal <= 0f) { if (parts.Length > 0) Log($"life-steal on {victim.m_name}: none ({parts.TrimEnd(' ', ';')})"); return; }
            if (victim.IsTamed()) { Log($"life-steal on {victim.m_name}: none (tamed)"); return; }
            if (!ClassTargeting.IsEnemyTarget(player, victim)) return;

            float dealt = hit.GetTotalDamage();
            float heal = dealt * steal;
            if (victim.IsBoss()) heal *= VampireConfig.Value(VampireConfig.DrainBossShare, 0.5f);
            // 0.1.131 (Fire 10-14): the 5% of max health is the knee of SoftCap's curve, not a hard stop - heal past it diminishes.
            float knee = player.GetMaxHealth() * VampireConfig.Value(VampireConfig.DrainCap, 0.05f);
            float rawHeal = heal;
            heal = SoftCap.Soft(heal, knee);
            bool softened = rawHeal > knee;
            if (heal <= 0f) return;
            AbilityHeals.Apply(player, player, heal);
            Log($"life-steal on {victim.m_name}{(victim.IsBoss() ? " (boss, half)" : "")}: {dealt:0.0} dealt x {steal:0.###} = {heal:0.0} healed"
                + $"{(softened ? $" (raw {rawHeal:0.0}, knee {knee:0.0})" : "")} [{parts.TrimEnd(' ', ';')}]");
        }

        /// <summary>RPC_Damage prefix (PassiveHooks): the local player was hit - Bat Form's 5 s lockout.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (victim != null && victim == Player.m_localPlayer && hit != null && hit.GetAttacker() != null && hit.GetTotalDamage() > 0f)
                _lastHurtAt = Time.time;
        }

        /// <summary>The attack-speed hook (AttributeEffects): Blood Frenzy's +15%.</summary>
        public static float AttackSpeedFactor(Character c)
        {
            if (!(c is Player player) || player != Player.m_localPlayer || !Has(player, VampireEffects.BloodFrenzyHash)) return 1f;
            return 1f + VampireConfig.Value(VampireConfig.FrenzyAttackSpeed, 0.15f);
        }

        // ── sunlight ──────────────────────────────────────────────────────────────────────────────────────────────────

        private static bool Warded(Player p) => Has(p, VampireEffects.SunWardHash);

        private static bool DrainBlockedBySun(Player p) => _sunlit && VampireConfig.SunMode == VampireConfig.SunMild && !Warded(p);

        // "Direct daylight": vanilla's daylight (EnvMan: day, and not an always-dark place), not under a roof (Player.InShelter, the
        // check rain uses: cover >= 80% and under a roof) and not inside a dungeon / cave (InInterior).
        private static bool InSunlight(Player p) => EnvMan.IsDaylight() && !p.InShelter() && !p.InInterior();

        private static void TickSun(Player player)
        {
            bool vampire = VampireRace.Local;
            string mode = VampireConfig.SunMode;
            _sunlit = vampire && mode != VampireConfig.SunNone && InSunlight(player);
            bool warded = Warded(player);

            // The ward lasts as long as the stew in a food slot (§12): eaten again it refreshes, gone it goes.
            if (warded && !HasStew(player))
            {
                AbilityRPCManager.RemoveSelfBuff(player, VampireEffects.SunWard);
                warded = false;
                Log("sun ward ended: no Duskbloom Stew in the food slots");
            }

            bool penalty = _sunlit && !warded && mode == VampireConfig.SunMild;
            if (penalty != _sunPenalty)
            {
                _sunPenalty = penalty;
                // 0.1.132: vanilla SEMan directly, so no "<source> granted you ..." message; no timer (m_ttl 0 in SunlightEffect).
                if (penalty) player.GetSEMan().AddStatusEffect(VampireEffects.SunlightHash, true);
                else player.GetSEMan().RemoveStatusEffect(VampireEffects.SunlightHash);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Vampire] Sunburnt {(penalty ? "on" : "off")}");
            }

            if (_sunlit && !warded && mode == VampireConfig.SunHarsh && Time.time >= _nextBurn)
            {
                _nextBurn = Time.time + VampireConfig.Value(VampireConfig.SunBurnInterval, 5f);
                float burn = player.GetMaxHealth() * VampireConfig.Value(VampireConfig.SunBurnShare, 0.01f);
                var hit = new HitData { m_point = player.GetCenterPoint(), m_dir = Vector3.down };
                hit.m_damage.m_damage = burn;
                player.Damage(hit);
                Log($"sunlight burns {burn:0.0} (harsh)");
            }

            string line = $"{mode}: {(vampire ? "vampire" : "not a vampire")}, daylight {EnvMan.IsDaylight()}, shelter {player.InShelter()}, inside {player.InInterior()}, "
                + $"sunlit {_sunlit}, ward {warded} -> {(penalty ? "ClassSunlight (stamina regen -" + VampireConfig.Value(VampireConfig.SunStaminaPenalty, 0.10f).ToString("0%") + ", no Life Drain)" : _sunlit && !warded && mode == VampireConfig.SunHarsh ? "burning" : "no penalty")}";
            if (line != _lastSunLine) { _lastSunLine = line; Log("sunlight " + line); }
        }

        private static bool HasStew(Player p)
        {
            foreach (Player.Food food in p.GetFoods())
                if (VampireFood.IsStew(food.m_item)) return true;
            return false;
        }

        // ── tick ──────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>ClassCombatDriver, every frame: the bat's end conditions, skill and Use key; once a second sunlight and passives.</summary>
        internal static void Tick()
        {
            Player player = Player.m_localPlayer;
            if (player != null && BatActive) TickBat(player);

            if (Time.time < _nextRead) return;
            _nextRead = Time.time + 1f;
            ReadPassives();
            if (player == null || player.IsDead()) return;
            TickSun(player);

            bool stalker = PassiveOn(NightStalkerId) && EnvMan.IsNight();
            if (stalker != _stalkerOn)
            {
                _stalkerOn = stalker;
                if (stalker) AbilityRPCManager.ApplySelfBuff(player, VampireEffects.NightStalker, 86400f);
                else AbilityRPCManager.RemoveSelfBuff(player, VampireEffects.NightStalker);
                Log($"Night Stalker {(stalker ? "on (night)" : "off")}");
            }
        }

        private static void ReadPassives()
        {
            Passives.Clear();
            if (Player.m_localPlayer == null || !VampireRace.Local) return;
            Read(LifeDrainId, "drain");
            Read(NightStalkerId, null);
        }

        private static void Read(string id, string valueKey)
        {
            ClassTreeNode node = ClassTrees.Node(id);
            if (node == null || PassiveLoadout.Status(node) != PassiveStatus.Active) return;
            float v = valueKey == null ? 1f : node.Value(valueKey, DesignKit.RankOf(node));
            Passives[id] = v;
            if (!Shown.TryGetValue(id, out float was) || Mathf.Abs(was - v) > 1e-4f)
            {
                Shown[id] = v;
                Log($"passive {id} on ({(valueKey == null ? "on" : valueKey + " " + v.ToString("0.###"))})");
            }
        }

        private static bool PassiveOn(string id) => Passives.ContainsKey(id);
        private static float Passive(string id) => Passives.TryGetValue(id, out float v) ? v : 0f;

        /// <summary>A new session (Ascend's ZNet.Start patch): nothing of the last world's bat, sun or passives carries over.</summary>
        internal static void ResetSession()
        {
            _bat = null;
            _batPending = false;
            _batGo = null;
            _pendingEitr = 0f;
            _halveSpillFrame = -1;
            _lastHurtAt = float.NegativeInfinity;
            _sunPenalty = _stalkerOn = _sunlit = false;
            _nextBurn = _nextRead = 0f;
            _lastSunLine = "";
            Passives.Clear();
        }

        // ── for FiresClassTest ────────────────────────────────────────────────────────────────────────────────────────

        public static float BatRemaining => BatActive ? _bat.Remaining : 0f;
        public static bool Sunlit => _sunlit;
        public static bool SunPenaltyOn => _sunPenalty;
        public static bool IsPassiveOn(string id, out float value) => Passives.TryGetValue(id, out value);
        public static IReadOnlyList<string> HandledPassives => PassiveIds;
    }
}
