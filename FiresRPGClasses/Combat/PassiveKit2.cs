using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Design-only passives on the existing vanilla hooks. Like DruidNecroPassives: each id is
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
        internal const string ThickHideId = ThickHide, ZealId = "paladin_zeal";   // ClassStatsApi row names
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
        // 0.1.214 ([lead] / [ghost] step 3b, the Studio god clone's passives): this kit's state per BODY. The local player is one
        // instance (S_local) read from its record exactly as before; a registered class body (ClassBodies) gets its own, read from ITS
        // record and gear, with its own Zeal stacks / Unbreakable cooldown / cleave guard. Every hook reads the state of the body it is about.
        private sealed class State
        {
            public readonly Dictionary<string, float> A = new Dictionary<string, float>();   // per id: value 1 / value 2 (0 when the id has one)
            public readonly Dictionary<string, float> B = new Dictionary<string, float>();
            public float NextTick;
            public int ZealStacks;
            public float ZealUntil = -1f;
            public float UnbreakableReady;
            public bool Cleaving;
            public Attack Cleaved;   // one cleave per swing, however many it hits
            public bool On(string id) => A.ContainsKey(id);
            public float V(string id) => A.TryGetValue(id, out float v) ? v : 0f;
            public float W(string id) => B.TryGetValue(id, out float v) ? v : 0f;
        }
        private static readonly State S_local = new State();
        private static readonly Dictionary<Character, State> S_bodies = new Dictionary<Character, State>();

        /// <summary>The state of the body a hook is about: the local player's, a registered class body's, else null (no class passives).</summary>
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

        /// <summary>For FiresClassTest: the passive is Active now, with its first value (0 when off).</summary>
        public static bool IsOn(string id, out float value) => S_local.A.TryGetValue(id, out value);
        public static IReadOnlyList<string> Handled => Ids;
        public static int ZealStacks => S_local.ZealUntil >= Time.time ? S_local.ZealStacks : 0;

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

        private static void ReadAll(State st, ClassPlayerData record, Humanoid body)
        {
            st.A.Clear(); st.B.Clear();
            Read(st, record, body, ThickHide, "armor", "stagger");
            Read(st, record, body, Immovable, "block", null);
            Read(st, record, body, Zeal, "stacks", null);
            Read(st, record, body, Unbreakable, null, null);
            Read(st, record, body, Cleave, "dmg", null);
            Read(st, record, body, ThickSkull, "stagger", null);
            Read(st, record, body, Quickstep, "cost", null);
            Read(st, record, body, SwiftFeet, "speed", "run");
            Read(st, record, body, Leaf, "chance", null);
        }

        private static void Watch(State st, Character me)
        {
            if (st.On(Unbreakable) && Time.time >= st.UnbreakableReady && me.GetHealth() < me.GetMaxHealth() * UnbreakableBelow)
            {
                st.UnbreakableReady = Time.time + UnbreakableCooldown;
                AbilityRPCManager.ApplySelfBuff(me, StatusEffectManager.EFFECT_DIVINE_SHIELD, UnbreakableShield);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {Unbreakable}: {me.m_name} below {UnbreakableBelow:0%}: Divine Shield {UnbreakableShield:0} s, next in {UnbreakableCooldown:0} s");
            }
        }

        /// <summary>Every frame from ClassCombatDriver; the reads once a second, Unbreakable's watch every frame - the local player, then
        /// each registered class body (its own record and gear).</summary>
        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) { S_local.A.Clear(); S_local.B.Clear(); }
            else
            {
                if (Time.time >= S_local.NextTick) { S_local.NextTick = Time.time + 1f; ReadAll(S_local, null, me); }
                Watch(S_local, me);
            }
            foreach (Character body in ClassBodies.Bodies())
            {
                State st = Of(body);
                if (st == null || body.IsDead() || !(body is Humanoid h)) { if (st != null) { st.A.Clear(); st.B.Clear(); } continue; }
                if (Time.time >= st.NextTick) { st.NextTick = Time.time + 1f; ReadAll(st, ClassBodies.Record(body), h); }
                Watch(st, body);
            }
            foreach (Character gone in new List<Character>(S_bodies.Keys)) if (gone == null || !ClassBodies.IsRegistered(gone)) S_bodies.Remove(gone);
        }

        private static bool UnarmedOrClub(Humanoid p)
        {
            ItemDrop.ItemData w = p.GetCurrentWeapon();
            return w == null || w == p.m_unarmedWeapon?.m_itemData || w.m_shared.m_skillType == Skills.SkillType.Unarmed || w.m_shared.m_skillType == Skills.SkillType.Clubs;
        }

        /// <summary>Body armor factor (Player.GetBodyArmor postfix; a class body's armour through ClassBodies.PoolBonus).</summary>
        public static float ArmorFactor(Character p) { State st = Of(p); return st != null && st.On(ThickHide) ? 1f + st.V(ThickHide) : 1f; }

        /// <summary>Attack-speed factor (AttributeEffects' attack-speed hook): Zeal's stacks.</summary>
        public static float AttackSpeedFactor(Character c) { State st = Of(c); return st != null && st.On(Zeal) && st.ZealUntil >= Time.time ? 1f + ZealPerStack * st.ZealStacks : 1f; }

        /// <summary>Movement factor: Swift Feet.</summary>
        public static float SpeedFactor(Character c) { State st = Of(c); return st != null && st.On(SwiftFeet) && c is Humanoid h && UnarmedOrClub(h) ? 1f + st.V(SwiftFeet) : 1f; }

        /// <summary>Stamina cost factor (UseStamina prefix): Immovable (blocking), Quickstep / Leaf (dodging), Swift Feet (running).</summary>
        public static float StaminaCostFactor(Player p)
        {
            State st = Of(p);
            if (st == null) return 1f;
            float f = 1f;
            if (p.IsBlocking() && st.On(Immovable)) f *= 1f - st.V(Immovable);
            if (p.InDodge())
            {
                if (st.On(Quickstep)) f *= 1f - st.V(Quickstep);
                if (st.On(Leaf) && Random.value < st.V(Leaf)) f = 0f;
            }
            else if (p.IsRunning() && st.On(SwiftFeet) && UnarmedOrClub(p)) f *= 1f - st.W(SwiftFeet);
            return f;
        }

        /// <summary>Damage the body takes (RPC_Damage prefix): Thick Hide / Thick Skull / Immovable stagger and push.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            State st = Of(victim);
            if (st == null || hit == null) return;
            if (st.On(ThickHide)) hit.m_staggerMultiplier *= 1f - st.W(ThickHide);
            if (st.On(ThickSkull) && StatusEffectManager.HasEffect(victim, StatusEffectManager.EFFECT_BERSERK_RAGE)) hit.m_staggerMultiplier *= 1f - st.V(ThickSkull);
            if (st.On(Immovable) && victim.IsBlocking()) { hit.m_staggerMultiplier = 0f; hit.m_pushForce = 0f; }
        }

        /// <summary>The body's hit being sent (Character.Damage prefix): Zeal's stacks, Cleave's arc.</summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            if (victim == null || hit == null) return;
            Character me = hit.GetAttacker();
            State st = Of(me);
            if (st == null || victim == me || hit.m_attacker != me.GetZDOID() || st.Cleaving) return;
            if (st.On(Zeal))
            {
                st.ZealStacks = st.ZealUntil >= Time.time ? Mathf.Min(st.ZealStacks + 1, Mathf.RoundToInt(st.V(Zeal))) : 1;
                st.ZealUntil = Time.time + ZealSeconds;
            }
            if (st.On(Cleave) && me is Humanoid h && h.InAttack() && h.m_currentAttackIsSecondary && !hit.m_ranged && h.m_currentAttack != st.Cleaved)
            {
                st.Cleaving = true;
                st.Cleaved = h.m_currentAttack;
                try
                {
                    Vector3 fwd = me.transform.forward;
                    foreach (Character c in Character.GetAllCharacters())
                    {
                        if (c == null || c == victim || c.IsDead() || !ClassTargeting.IsEnemyTarget(me, c)) continue;
                        Vector3 to = c.transform.position - me.transform.position; to = Ring.Flat(to, c.transform.position);   // off the halo ring: to.y = 0
                        if (to.magnitude > CleaveReach || Vector3.Angle(fwd, to) > CleaveHalfArc) continue;
                        HitData copy = hit.Clone();
                        copy.m_damage.Modify(st.V(Cleave));
                        copy.m_point = c.GetCenterPoint();
                        copy.m_dir = to.normalized;
                        c.Damage(copy);
                    }
                }
                finally { st.Cleaving = false; }
            }
        }
    }
}
