using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.95 (Fire #11 batch 3b): the remaining design-only passives, same pattern as PassiveKit2 (read once a second at the
    /// player's rank; the hooks look the value up; local player only).
    /// - Pain Fuels Me (Berserker): each hit taken adds {fury} Fury (PassiveKit's Fury; max [13 - Balance] Fury max stacks, 20).
    /// - Unseen (Rogue): hits from Stealth deal double damage, and the first one keeps Stealth (it is put back for the time left).
    /// - Plague Burst (Rogue): an enemy the player poisoned bursts on death: Poison on every enemy within 6 m.
    /// - Deadeye (Ranger): a fully drawn shot has a {crit} chance to deal double damage.
    /// - Apex Predator (Ranger): +25% damage to Hunter's-Marked enemies; when one dies, the mark jumps to the nearest enemy within 15 m.
    /// - Frostbite (Mage): a frost hit slows the target (Core's 20% slow preset) for {dur}.
    /// - Ignite (Mage): a magic fire hit leaves 3 s of burning ground (2 m) dealing {dps} fire per second (at most one per second).
    /// - Overflow (Healer): the overheal part of the player's heal gives the target a class ward (Core's ward presets) for 10 s.
    /// - Virulence (Rogue, 0.1.96): poison stacks up to {stacks} (Core 0.2.400's ApplyPoisonStack, called by reflection where the
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

        private static readonly Dictionary<string, float> A = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> B = new Dictionary<string, float>();
        private static readonly HashSet<Character> Poisoned = new HashSet<Character>();
        private static readonly HashSet<Character> Marked = new HashSet<Character>();
        private static readonly List<Character> Gone = new List<Character>();
        private static float _nextTick, _nextIgnite;
        private static bool _unseenSpent;
        private static float _stealthUntil = -1f;

        public static void RegisterAll()
        {
            foreach (string id in Ids) ClassSkillKit.RegisterPassive(id);
        }

        private static bool IsLocal(Character c) => c != null && c == Player.m_localPlayer;
        private static bool On(string id) => A.ContainsKey(id);
        private static float V(string id) => A.TryGetValue(id, out float v) ? v : 0f;
        private static float W(string id) => B.TryGetValue(id, out float v) ? v : 0f;
        public static bool IsOn(string id, out float value) => A.TryGetValue(id, out value);
        public static IReadOnlyList<string> Handled => Ids;
        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Classes] " + s);

        private static void Read(string id, string key1, string key2)
        {
            ClassTreeNode node = ClassTrees.Node(id);
            if (node == null || PassiveLoadout.Status(node) != PassiveStatus.Active) return;
            int rank = DesignKit.RankOf(node);
            A[id] = key1 == null ? 1f : node.Value(key1, rank);
            if (key2 != null) B[id] = node.Value(key2, rank);
        }

        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) { A.Clear(); B.Clear(); Poisoned.Clear(); Marked.Clear(); return; }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 0.25f;
            A.Clear(); B.Clear();
            Read(PainFuels, "fury", null);
            Read(Unseen, null, null);
            Read(PlagueBurst, null, null);
            Read(Deadeye, "crit", null);
            Read(Apex, null, null);
            Read(Frostbite, "slow", "dur");
            Read(Ignite, "dps", null);
            Read(Overflow, "cap", null);
            Read(Virulence, "stacks", null);

            if (!StatusEffectManager.HasEffect(me, StatusEffectManager.EFFECT_STEALTH)) _unseenSpent = false;
            Burst(me);
            JumpMark(me);
        }

        private static void Burst(Player me)
        {
            if (Poisoned.Count == 0) return;
            foreach (Character c in Poisoned) if (c != null && !c.IsDead()) { int s = Stacks(c); if (s > 0) LastStacks[c] = s; }
            Gone.Clear();
            foreach (Character c in Poisoned) if (c == null || c.IsDead()) Gone.Add(c);
            foreach (Character dead in Gone)
            {
                Poisoned.Remove(dead);
                int half = dead != null && LastStacks.TryGetValue(dead, out int had) ? Mathf.Max(1, had / 2) : 1;
                if (dead != null) LastStacks.Remove(dead);
                if (dead == null || !On(PlagueBurst)) continue;
                Vector3 at = dead.transform.position;
                int n = 0;
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c == dead || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                    if (Vector3.Distance(c.transform.position, at) > BurstReach) continue;
                    if (ApplyPoison(me, c, BurstPoisonSeconds)) { Poisoned.Add(c); n++; if (half > 1) SetStacks(c, half); }
                }
                AbilityRPCManager.SpawnFX(at, "vfx_blob_attack");
                Log($"{PlagueBurst}: {dead.GetHoverName()} burst, {n} poisoned");
            }
        }

        private static void JumpMark(Player me)
        {
            if (Marked.Count == 0) return;
            Gone.Clear();
            foreach (Character c in Marked) if (c == null || c.IsDead()) Gone.Add(c);
            foreach (Character dead in Gone)
            {
                Marked.Remove(dead);
                if (dead == null || !On(Apex)) continue;
                Character next = null; float best = ApexJump;
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c == dead || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                    float d = Vector3.Distance(c.transform.position, dead.transform.position);
                    if (d <= best) { best = d; next = c; }
                }
                if (next != null && AbilityRPCManager.ApplySingleEffect(me, next, StatusEffectManager.EFFECT_HUNTERS_MARK, ApexMarkSeconds))
                {
                    Marked.Add(next);
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

        /// <summary>The local player's poison on a target: stacked to Virulence's cap where the target is owned here, else Core's routed poison.</summary>
        internal static bool ApplyPoison(Character source, Character target, float seconds)
        {
            Look();
            if (On(Virulence) && _applyStack != null && OwnedHere(target))
            {
                _applyStack.Invoke(null, new object[] { target, source, seconds, PoisonTickDamage, Mathf.RoundToInt(V(Virulence)) });
                return true;
            }
            return AbilityRPCManager.ApplySingleEffect(source, target, StatusEffectManager.EFFECT_POISON, seconds);
        }

        /// <summary>For FiresClassTest: the local player's poison on a target through the same path as the poisoned blade (Virulence's cap).</summary>
        public static bool PoisonForTest(Character target, float seconds) => Player.m_localPlayer != null && ApplyPoison(Player.m_localPlayer, target, seconds);

        /// <summary>CoreSkillKit's poisoned blade hit something: Plague Burst watches it.</summary>
        internal static void NotePoisoned(Character c) { if (c != null) Poisoned.Add(c); }

        /// <summary>Damage the local player takes: Pain Fuels Me.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (IsLocal(victim) && hit != null && On(PainFuels) && hit.GetTotalDamage() > 0f) PassiveKit.AddFury(Mathf.RoundToInt(V(PainFuels)));
        }

        /// <summary>The local player's hit being sent: Unseen, Deadeye, Apex Predator, Frostbite, Ignite.</summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            Player me = Player.m_localPlayer;
            if (me == null || victim == null || victim == me || hit == null || hit.m_attacker != me.GetZDOID()) return;

            if (On(Unseen) && StatusEffectManager.HasEffect(me, StatusEffectManager.EFFECT_STEALTH))
            {
                hit.m_damage.Modify(2f);
                if (!_unseenSpent)
                {
                    _unseenSpent = true;
                    StatusEffect se = me.GetSEMan().GetStatusEffect(StatusEffectManager.EFFECT_STEALTH.GetStableHashCode());
                    _stealthUntil = se != null ? Time.time + se.GetRemaningTime() : -1f;
                }
            }
            if (On(Deadeye) && hit.m_ranged && PassiveKit.LastShotFull && Random.value < V(Deadeye)) hit.m_damage.Modify(2f);
            if (On(Apex) && StatusEffectManager.HasEffect(victim, StatusEffectManager.EFFECT_HUNTERS_MARK))
            {
                hit.m_damage.Modify(ApexBonus);
                Marked.Add(victim);
            }
            if (On(Frostbite) && hit.m_damage.m_frost > 0f)
                AbilityRPCManager.ApplySingleEffect(me, victim, StatusEffectManager.EFFECT_CLASS_SLOW20, Mathf.Max(0.5f, W(Frostbite)));
            if (On(Ignite) && hit.m_damage.m_fire > 0f && hit.m_skill == Skills.SkillType.ElementalMagic && Time.time >= _nextIgnite)
            {
                _nextIgnite = Time.time + 1f;
                float dps = V(Ignite);
                ClassZones.ZoneAt(me, victim.transform.position, IgniteRadius, IgniteSeconds, ZoneKind.Harmful, c =>
                {
                    var burn = new HitData { m_point = c.GetCenterPoint() };
                    burn.m_damage.m_fire = dps;
                    burn.SetAttacker(me);
                    c.Damage(burn);
                }, 1f, "vfx_FireballHit", 1f);
            }
        }

        /// <summary>After the Unseen hit: Stealth is put back for the time it had left (the first hit only).</summary>
        public static void LateTick()
        {
            if (_stealthUntil < 0f) return;
            Player me = Player.m_localPlayer;
            float left = _stealthUntil - Time.time;
            _stealthUntil = -1f;
            if (me == null || left <= 0.2f || StatusEffectManager.HasEffect(me, StatusEffectManager.EFFECT_STEALTH)) return;
            AbilityRPCManager.ApplySelfBuff(me, StatusEffectManager.EFFECT_STEALTH, left);
            Log($"{Unseen}: first hit from Stealth kept it ({left:0.0} s left)");
        }

        /// <summary>A class heal by the local player (AbilityHeals.Apply prefix): Overflow's ward from the overheal.</summary>
        internal static void OnLocalHeal(Character healer, Character target, float amount)
        {
            if (!IsLocal(healer) || target == null || !On(Overflow) || amount <= 0f) return;
            float over = amount - (target.GetMaxHealth() - target.GetHealth());
            if (over <= target.GetMaxHealth() * 0.01f) return;
            float cap = V(Overflow);
            string ward = cap >= 0.12f ? StatusEffectManager.EFFECT_CLASS_WARD15 : StatusEffectManager.EFFECT_CLASS_WARD10;
            AbilityRPCManager.ApplySingleEffect(healer, target, ward, OverflowSeconds);
            Log($"{Overflow}: overheal {over:0} on {target.GetHoverName()} -> {ward} for {OverflowSeconds:0} s");
        }
    }
}
