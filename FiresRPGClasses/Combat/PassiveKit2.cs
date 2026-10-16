using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.94 (Fire #11 batch 3a): design-only passives on the existing vanilla hooks. Like DruidNecroPassives: each id is
    /// read once a second (learned, equipped, Active in the passive grid) at the player's rank; the hooks only look the value up.
    /// All local player only. Perf: one Dictionary lookup per hook call, no allocation.
    /// - Thick Hide (Tank): +armor%, less stagger taken.        - Immovable (Tank): no stagger / knockback while blocking; block stamina -%.
    /// - Zeal (Paladin): each hit +5% attack speed for 6 s, up to {stacks}.
    /// - Unbreakable (Paladin): once per 3 min, dropping below 25% health grants Divine Shield for 2 s.
    /// - Cleave (Berserker): secondary (heavy) attacks also hit enemies in a 120° arc within 3 m for {dmg} of the hit.
    /// - Thick Skull (Berserker): less stagger taken while Berserk Rage is on.
    /// - Quickstep (Rogue): dodging costs less stamina.
    /// - Swift Feet (Monk): unarmed or with a club: +speed, running costs less stamina.
    /// - Leaf on the Wind (Monk): a dodge has a {chance} to cost no stamina (the untargetable second is not in this build).
    /// </summary>
    public static class PassiveKit2
    {
        private const string ThickHide = "tank_thick_hide";
        private const string Immovable = "tank_immovable";
        private const string Zeal = "paladin_zeal";
        private const string Unbreakable = "paladin_unbreakable";
        private const string Cleave = "berserker_cleave";
        private const string ThickSkull = "berserker_thick_skull";
        private const string Quickstep = "rogue_quickstep";
        private const string SwiftFeet = "monk_swift_feet";
        private const string Leaf = "monk_leaf_on_the_wind";
        private static readonly string[] Ids = { ThickHide, Immovable, Zeal, Unbreakable, Cleave, ThickSkull, Quickstep, SwiftFeet, Leaf };

        private const float ZealPerStack = 0.05f;
        private const float ZealSeconds = 6f;
        private const float UnbreakableBelow = 0.25f;
        private const float UnbreakableCooldown = 180f;
        private const float UnbreakableShield = 2f;
        private const float CleaveReach = 3f;
        private const float CleaveHalfArc = 60f;

        // per id: value 1 / value 2 (0 when the id has one)
        private static readonly Dictionary<string, float> A = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> B = new Dictionary<string, float>();
        private static float _nextTick;
        private static int _zealStacks;
        private static float _zealUntil = -1f;
        private static float _unbreakableReady;
        private static bool _cleaving;
        private static Attack _cleaved;   // one cleave per swing, however many it hits

        public static void RegisterAll()
        {
            foreach (string id in Ids) ClassSkillKit.RegisterPassive(id);
        }

        private static bool IsLocal(Character c) => c != null && c == Player.m_localPlayer;
        private static bool On(string id) => A.ContainsKey(id);
        private static float V(string id) => A.TryGetValue(id, out float v) ? v : 0f;
        private static float W(string id) => B.TryGetValue(id, out float v) ? v : 0f;

        /// <summary>For FiresClassTest: the passive is Active now, with its first value (0 when off).</summary>
        public static bool IsOn(string id, out float value) => A.TryGetValue(id, out value);
        public static IReadOnlyList<string> Handled => Ids;
        public static int ZealStacks => _zealUntil >= Time.time ? _zealStacks : 0;

        private static void Read(string id, string key1, string key2)
        {
            ClassTreeNode node = ClassTrees.Node(id);
            if (node == null || PassiveLoadout.Status(node) != PassiveStatus.Active) return;
            int rank = DesignKit.RankOf(node);
            A[id] = key1 == null ? 1f : node.Value(key1, rank);
            if (key2 != null) B[id] = node.Value(key2, rank);
        }

        /// <summary>Every frame from ClassCombatDriver; the reads once a second, Unbreakable's watch every frame.</summary>
        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) { A.Clear(); B.Clear(); return; }
            if (Time.time >= _nextTick)
            {
                _nextTick = Time.time + 1f;
                A.Clear(); B.Clear();
                Read(ThickHide, "armor", "stagger");
                Read(Immovable, "block", null);
                Read(Zeal, "stacks", null);
                Read(Unbreakable, null, null);
                Read(Cleave, "dmg", null);
                Read(ThickSkull, "stagger", null);
                Read(Quickstep, "cost", null);
                Read(SwiftFeet, "speed", "run");
                Read(Leaf, "chance", null);
            }
            if (On(Unbreakable) && Time.time >= _unbreakableReady && me.GetHealth() < me.GetMaxHealth() * UnbreakableBelow)
            {
                _unbreakableReady = Time.time + UnbreakableCooldown;
                AbilityRPCManager.ApplySelfBuff(me, StatusEffectManager.EFFECT_DIVINE_SHIELD, UnbreakableShield);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {Unbreakable}: below {UnbreakableBelow:0%}: Divine Shield {UnbreakableShield:0} s, next in {UnbreakableCooldown:0} s");
            }
        }

        private static bool UnarmedOrClub(Player p)
        {
            ItemDrop.ItemData w = p.GetCurrentWeapon();
            return w == null || w == p.m_unarmedWeapon?.m_itemData || w.m_shared.m_skillType == Skills.SkillType.Unarmed || w.m_shared.m_skillType == Skills.SkillType.Clubs;
        }

        /// <summary>Body armor factor (Player.GetBodyArmor postfix).</summary>
        public static float ArmorFactor(Player p) => IsLocal(p) && On(ThickHide) ? 1f + V(ThickHide) : 1f;

        /// <summary>Attack-speed factor (AttributeEffects' attack-speed hook): Zeal's stacks.</summary>
        public static float AttackSpeedFactor(Character c) => IsLocal(c) && On(Zeal) && _zealUntil >= Time.time ? 1f + ZealPerStack * _zealStacks : 1f;

        /// <summary>Movement factor: Swift Feet.</summary>
        public static float SpeedFactor(Character c) => IsLocal(c) && On(SwiftFeet) && UnarmedOrClub((Player)c) ? 1f + V(SwiftFeet) : 1f;

        /// <summary>Stamina cost factor (UseStamina prefix): Immovable (blocking), Quickstep / Leaf (dodging), Swift Feet (running).</summary>
        public static float StaminaCostFactor(Player p)
        {
            if (!IsLocal(p)) return 1f;
            float f = 1f;
            if (p.IsBlocking() && On(Immovable)) f *= 1f - V(Immovable);
            if (p.InDodge())
            {
                if (On(Quickstep)) f *= 1f - V(Quickstep);
                if (On(Leaf) && Random.value < V(Leaf)) f = 0f;
            }
            else if (p.IsRunning() && On(SwiftFeet) && UnarmedOrClub(p)) f *= 1f - W(SwiftFeet);
            return f;
        }

        /// <summary>Damage the local player takes (RPC_Damage prefix): Thick Hide / Thick Skull / Immovable stagger and push.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (!IsLocal(victim) || hit == null) return;
            if (On(ThickHide)) hit.m_staggerMultiplier *= 1f - W(ThickHide);
            if (On(ThickSkull) && StatusEffectManager.HasEffect(victim, StatusEffectManager.EFFECT_BERSERK_RAGE)) hit.m_staggerMultiplier *= 1f - V(ThickSkull);
            if (On(Immovable) && ((Player)victim).IsBlocking()) { hit.m_staggerMultiplier = 0f; hit.m_pushForce = 0f; }
        }

        /// <summary>The local player's hit being sent (Character.Damage prefix): Zeal's stacks, Cleave's arc.</summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            Player me = Player.m_localPlayer;
            if (me == null || victim == null || victim == me || hit == null || hit.m_attacker != me.GetZDOID() || _cleaving) return;
            if (On(Zeal))
            {
                _zealStacks = _zealUntil >= Time.time ? Mathf.Min(_zealStacks + 1, Mathf.RoundToInt(V(Zeal))) : 1;
                _zealUntil = Time.time + ZealSeconds;
            }
            if (On(Cleave) && me.InAttack() && me.m_currentAttackIsSecondary && !hit.m_ranged && me.m_currentAttack != _cleaved)
            {
                _cleaving = true;
                _cleaved = me.m_currentAttack;
                try
                {
                    Vector3 fwd = me.transform.forward;
                    foreach (Character c in Character.GetAllCharacters())
                    {
                        if (c == null || c == victim || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                        Vector3 to = c.transform.position - me.transform.position; to.y = 0f;
                        if (to.magnitude > CleaveReach || Vector3.Angle(fwd, to) > CleaveHalfArc) continue;
                        HitData copy = hit.Clone();
                        copy.m_damage.Modify(V(Cleave));
                        copy.m_point = c.GetCenterPoint();
                        copy.m_dir = to.normalized;
                        c.Damage(copy);
                    }
                }
                finally { _cleaving = false; }
            }
        }
    }
}
