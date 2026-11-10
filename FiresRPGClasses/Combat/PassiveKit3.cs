using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The remaining design-only passives, same pattern as PassiveKit2 (read once a second at the
    /// player's rank; the hooks look the value up; local player only).
    /// - Pain Fuels Me (Berserker): each hit taken adds {fury} Fury (PassiveKit's Fury; max [13 - Balance] Fury max stacks, 20).
    /// - Unseen (Rogue): hits from Stealth deal double damage, and the first one keeps Stealth (it is put back for the time left).
    /// - Plague Burst (Rogue): an enemy the player poisoned bursts on death: Poison on every enemy within 6 m.
    /// - Deadeye (Ranger): a fully drawn shot has a {crit} chance to deal double damage.
    /// - Apex Predator (Ranger): +25% damage to Hunter's-Marked enemies; when one dies, the mark jumps to the nearest enemy within 15 m.
    /// - Frostbite (Mage): a frost hit slows the target (Core's 20% slow preset) for {dur}.
    /// - Ignite (Mage): a magic fire hit leaves 3 s of burning ground (2 m) dealing {dps} fire per second (at most one per second).
    /// - Overflow (Healer): the overheal part of the player's heal gives the target a class ward (Core's ward presets) for 10 s.
    /// - Virulence (Rogue): poison stacks up to {stacks} (Core 0.2.400's ApplyPoisonStack, called by reflection where the
    ///   target is owned here; elsewhere, or on an older Core, the plain routed poison with Core's own cap). Plague Burst passes
    ///   on half the dead one's stacks (at least 1) the same way.
    /// </summary>
    public static class PassiveKit3
    {
        private const string PainFuels = "berserker_pain_fuels_me";
        private const string Unseen = "rogue_unseen";
        private const string PlagueBurst = "rogue_plague_burst";
        private const string Deadeye = "ranger_deadeye";
        private const string Apex = "ranger_apex_predator";
        private const string Frostbite = "mage_frostbite";
        private const string Ignite = "mage_ignite";
        private const string Overflow = "healer_overflow";
        private const string Virulence = "rogue_virulence";
        private const float ReadInterval = 0.25f;   // seconds between re-reads of the passives' ranks
        private const float PoisonTickDamage = 4f;   // Core's poison tick before 0.2.400 kept property values (what players saw)
        private static readonly Dictionary<Character, int> LastStacks = new Dictionary<Character, int>();
        private static System.Reflection.MethodInfo _applyStack, _getStacks, _setStacks;
        private static bool _looked;
        private static readonly string[] Ids = { PainFuels, Unseen, PlagueBurst, Deadeye, Apex, Frostbite, Ignite, Overflow, Virulence };

        private const float BurstReach = 6f;
        private const float BurstPoisonSeconds = 8f;
        private const float ApexBonus = 1.25f;
        private const float ApexJump = 15f;
        private const float ApexMarkSeconds = 20f;
        private const float IgniteRadius = 2f;
        private const float IgniteSeconds = 3f;
        private const float OverflowSeconds = 10f;

        // 0.1.214 ([lead] / [ghost] step 3b, the Studio god clone's passives): this kit's state per BODY - the local player one instance
        // read from its record exactly as before, a registered class body (ClassBodies) its own from ITS record and gear. Every hook reads
        // the state of the body it is about (the attacker, the victim, the healer).
        private sealed class State
        {
            public readonly Dictionary<string, float> A = new Dictionary<string, float>();
            public readonly Dictionary<string, float> B = new Dictionary<string, float>();
            public readonly HashSet<Character> Poisoned = new HashSet<Character>();
            public readonly HashSet<Character> Marked = new HashSet<Character>();
            public float NextTick, NextIgnite;
            public bool UnseenSpent;
            public float StealthUntil = -1f;
            public bool On(string id) => A.ContainsKey(id);
            public float V(string id) => A.TryGetValue(id, out float v) ? v : 0f;
            public float W(string id) => B.TryGetValue(id, out float v) ? v : 0f;
        }
        private static readonly State S_local = new State();
        private static readonly Dictionary<Character, State> S_bodies = new Dictionary<Character, State>();
        private static readonly List<Character> Gone = new List<Character>();

        private static State Of(Character c)
        {
            if (c == null) return null;
            if (c == Player.m_localPlayer) return S_local;
            if (!ClassBodies.IsRegistered(c)) return null;
            if (!S_bodies.TryGetValue(c, out State s)) S_bodies[c] = s = new State();
            return s;
        }

        public static void RegisterAll()
        {
            foreach (string id in Ids) ClassSkillKit.RegisterPassive(id);
        }

        public static bool IsOn(string id, out float value) => S_local.A.TryGetValue(id, out value);
        public static IReadOnlyList<string> Handled => Ids;
        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Classes] " + s);

        private static void Read(State st, ClassPlayerData record, Humanoid body, string id, string key1, string key2)
        {
            ClassTreeNode node = ClassTrees.Node(id);
            if (node == null) return;
            bool local = record == null;
            if ((local ? PassiveLoadout.Status(node) : PassiveLoadout.Status(node, record, body)) != PassiveStatus.Active) return;
            int rank = local ? DesignKit.RankOf(node) : DesignKit.RankOf(node, record);
            st.A[id] = key1 == null ? 1f : node.Value(key1, rank);
            if (key2 != null) st.B[id] = node.Value(key2, rank);
        }

        private static void TickBody(State st, Character me, ClassPlayerData record)
        {
            if (Time.time < st.NextTick) return;
            st.NextTick = Time.time + ReadInterval;
            st.A.Clear(); st.B.Clear();
            Humanoid h = me as Humanoid;
            Read(st, record, h, PainFuels, "fury", null);
            Read(st, record, h, Unseen, null, null);
            Read(st, record, h, PlagueBurst, null, null);
            Read(st, record, h, Deadeye, "crit", null);
            Read(st, record, h, Apex, null, null);
            Read(st, record, h, Frostbite, "slow", "dur");
            Read(st, record, h, Ignite, "dps", null);
            Read(st, record, h, Overflow, "cap", null);
            Read(st, record, h, Virulence, "stacks", null);

            if (!StatusEffectManager.HasEffect(me, StatusEffectManager.EFFECT_STEALTH)) st.UnseenSpent = false;
            Burst(st, me);
            JumpMark(st, me);
        }

        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) { S_local.A.Clear(); S_local.B.Clear(); S_local.Poisoned.Clear(); S_local.Marked.Clear(); }
            else TickBody(S_local, me, null);
            foreach (Character body in ClassBodies.Bodies())
            {
                State st = Of(body);
                if (st == null) continue;
                if (body.IsDead()) { st.A.Clear(); st.B.Clear(); st.Poisoned.Clear(); st.Marked.Clear(); continue; }
                TickBody(st, body, ClassBodies.Record(body));
            }
            foreach (Character gone in new List<Character>(S_bodies.Keys)) if (gone == null || !ClassBodies.IsRegistered(gone)) S_bodies.Remove(gone);
        }

        private static void Burst(State st, Character me)
        {
            if (st.Poisoned.Count == 0) return;
            foreach (Character c in st.Poisoned) if (c != null && !c.IsDead()) { int s = Stacks(c); if (s > 0) LastStacks[c] = s; }
            Gone.Clear();
            foreach (Character c in st.Poisoned) if (c == null || c.IsDead()) Gone.Add(c);
            foreach (Character dead in Gone)
            {
                st.Poisoned.Remove(dead);
                int half = dead != null && LastStacks.TryGetValue(dead, out int had) ? Mathf.Max(1, had / 2) : 1;
                if (dead != null) LastStacks.Remove(dead);
                if (dead == null || !st.On(PlagueBurst)) continue;
                Vector3 at = dead.transform.position;
                int n = 0;
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c == dead || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                    if (Vector3.Distance(c.transform.position, at) > BurstReach) continue;
                    if (ApplyPoison(me, c, BurstPoisonSeconds)) { st.Poisoned.Add(c); n++; if (half > 1) SetStacks(c, half); }
                }
                AbilityRPCManager.SpawnFX(at, "vfx_blob_attack");
                Log($"{PlagueBurst}: {dead.GetHoverName()} burst, {n} poisoned");
            }
        }

        private static void JumpMark(State st, Character me)
        {
            if (st.Marked.Count == 0) return;
            Gone.Clear();
            foreach (Character c in st.Marked) if (c == null || c.IsDead()) Gone.Add(c);
            foreach (Character dead in Gone)
            {
                st.Marked.Remove(dead);
                if (dead == null || !st.On(Apex)) continue;
                Character next = null; float best = ApexJump;
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c == dead || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                    float d = Vector3.Distance(c.transform.position, dead.transform.position);
                    if (d <= best) { best = d; next = c; }
                }
                if (next != null && AbilityRPCManager.ApplySingleEffect(me, next, StatusEffectManager.EFFECT_HUNTERS_MARK, ApexMarkSeconds))
                {
                    st.Marked.Add(next);
                    Log($"{Apex}: mark jumped from {dead.GetHoverName()} to {next.GetHoverName()} ({best:0.0} m)");
                }
            }
        }

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            System.Type t = typeof(StatusEffectManager);
            _applyStack = t.GetMethod("ApplyPoisonStack", new[] { typeof(Character), typeof(Character), typeof(float), typeof(float), typeof(int) });
            _getStacks = t.GetMethod("GetPoisonStacks", new[] { typeof(Character) });
            _setStacks = t.GetMethod("SetPoisonStacks", new[] { typeof(Character), typeof(int) });
            Log($"poison stacks: Core {(_applyStack != null && _getStacks != null && _setStacks != null ? "has" : "LACKS")} ApplyPoisonStack/GetPoisonStacks/SetPoisonStacks (0.2.400+)");
        }

        private static bool OwnedHere(Character c) { var nv = c != null ? c.GetComponent<ZNetView>() : null; return nv != null && nv.IsValid() && nv.IsOwner(); }

        /// <summary>The live poison stacks on a character (0 when not poisoned or on an older Core).</summary>
        public static int Stacks(Character c) { Look(); return _getStacks != null && c != null ? (int)_getStacks.Invoke(null, new object[] { c }) : 0; }

        private static void SetStacks(Character c, int n) { Look(); if (_setStacks != null && OwnedHere(c)) _setStacks.Invoke(null, new object[] { c, n }); }

        /// <summary>A class body's poison on a target: stacked to ITS Virulence cap where the target is owned here, else Core's routed poison.</summary>
        internal static bool ApplyPoison(Character source, Character target, float seconds)
        {
            Look();
            State st = Of(source);
            if (st != null && st.On(Virulence) && _applyStack != null && OwnedHere(target))
            {
                _applyStack.Invoke(null, new object[] { target, source, seconds, PoisonTickDamage, Mathf.RoundToInt(st.V(Virulence)) });
                return true;
            }
            return AbilityRPCManager.ApplySingleEffect(source, target, StatusEffectManager.EFFECT_POISON, seconds);
        }

        /// <summary>For FiresClassTest: the local player's poison on a target through the same path as the poisoned blade (Virulence's cap).</summary>
        public static bool PoisonForTest(Character target, float seconds) => Player.m_localPlayer != null && ApplyPoison(Player.m_localPlayer, target, seconds);

        /// <summary>CoreSkillKit's poisoned blade (the local player's) hit something: Plague Burst watches it.</summary>
        internal static void NotePoisoned(Character c) { if (c != null) S_local.Poisoned.Add(c); }

        /// <summary>Damage a class body takes: Pain Fuels Me (into that body's own fury).</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            State st = Of(victim);
            if (st != null && hit != null && st.On(PainFuels) && hit.GetTotalDamage() > 0f) PassiveKit.AddFury(victim, Mathf.RoundToInt(st.V(PainFuels)));
        }

        /// <summary>A class body's hit being sent: Unseen, Deadeye, Apex Predator, Frostbite, Ignite.</summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            if (victim == null || hit == null) return;
            Character me = hit.GetAttacker();
            State st = Of(me);
            if (st == null || victim == me || hit.m_attacker != me.GetZDOID()) return;

            if (st.On(Unseen) && StatusEffectManager.HasEffect(me, StatusEffectManager.EFFECT_STEALTH))
            {
                hit.m_damage.Modify(2f);
                if (!st.UnseenSpent)
                {
                    st.UnseenSpent = true;
                    StatusEffect se = me.GetSEMan().GetStatusEffect(StatusEffectManager.EFFECT_STEALTH.GetStableHashCode());
                    st.StealthUntil = se != null ? Time.time + se.GetRemaningTime() : -1f;
                }
            }
            if (st.On(Deadeye) && hit.m_ranged && me == Player.m_localPlayer && PassiveKit.LastShotFull && Random.value < st.V(Deadeye)) hit.m_damage.Modify(2f);
            if (st.On(Apex) && StatusEffectManager.HasEffect(victim, StatusEffectManager.EFFECT_HUNTERS_MARK))
            {
                hit.m_damage.Modify(ApexBonus);
                st.Marked.Add(victim);
            }
            if (st.On(Frostbite) && hit.m_damage.m_frost > 0f)
                AbilityRPCManager.ApplySingleEffect(me, victim, StatusEffectManager.EFFECT_CLASS_SLOW20, Mathf.Max(0.5f, st.W(Frostbite)));
            if (st.On(Ignite) && hit.m_damage.m_fire > 0f && hit.m_skill == Skills.SkillType.ElementalMagic && Time.time >= st.NextIgnite)
            {
                st.NextIgnite = Time.time + 1f;
                float dps = st.V(Ignite);
                ClassZones.ZoneAt(me, victim.transform.position, IgniteRadius, IgniteSeconds, ZoneKind.Harmful, c =>
                {
                    var burn = new HitData { m_point = c.GetCenterPoint() };
                    burn.m_damage.m_fire = dps;
                    burn.SetAttacker(me);
                    c.Damage(burn);
                }, 1f, "vfx_FireballHit", 1f);
            }
        }

        /// <summary>After the Unseen hit: Stealth is put back for the time it had left (the first hit only), each body.</summary>
        public static void LateTick()
        {
            LateTickBody(S_local, Player.m_localPlayer);
            foreach (var kv in S_bodies) LateTickBody(kv.Value, kv.Key);
        }

        private static void LateTickBody(State st, Character me)
        {
            if (st.StealthUntil < 0f) return;
            float left = st.StealthUntil - Time.time;
            st.StealthUntil = -1f;
            if (me == null || left <= 0.2f || StatusEffectManager.HasEffect(me, StatusEffectManager.EFFECT_STEALTH)) return;
            AbilityRPCManager.ApplySelfBuff(me, StatusEffectManager.EFFECT_STEALTH, left);
            Log($"{Unseen}: first hit from Stealth kept it ({left:0.0} s left)");
        }

        /// <summary>A class heal by a class body (AbilityHeals.Apply prefix): Overflow's ward from the overheal.</summary>
        internal static void OnLocalHeal(Character healer, Character target, float amount)
        {
            State st = Of(healer);
            if (st == null || target == null || !st.On(Overflow) || amount <= 0f) return;
            float over = amount - (target.GetMaxHealth() - target.GetHealth());
            if (over <= target.GetMaxHealth() * 0.01f) return;
            float cap = st.V(Overflow);
            string ward = cap >= 0.12f ? StatusEffectManager.EFFECT_CLASS_WARD15 : StatusEffectManager.EFFECT_CLASS_WARD10;
            AbilityRPCManager.ApplySingleEffect(healer, target, ward, OverflowSeconds);
            Log($"{Overflow}: overheal {over:0} on {target.GetHoverName()} -> {ward} for {OverflowSeconds:0} s");
        }
    }
}
