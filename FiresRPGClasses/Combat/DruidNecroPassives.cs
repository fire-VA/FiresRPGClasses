using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Possession;
using FiresCore.Summon;
using FiresRPGClasses.Data;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The Druid and Necromancer passives. The yaml's
    /// numbers at the learned rank, read once a second like PassiveKit's. The hooks are PassiveHooks' / ClassXpHooks' patches
    /// (one patch class per game method); only the hostility check is new (BaseAI.IsEnemy, below).
    /// - Thick Hide: in a form (Wildshape: you possess a beast) the form takes {armor} less physical damage.
    /// - Beast Tongue / Deathless Ward: wild animals / undead within {range} m of you don't take you as an enemy until hurt. The
    ///   range rides on your player ZDO, since a creature's AI runs on its owner's peer.
    /// - Forager: berries and mushrooms you pick have a {chance} chance to give one more.
    /// - Bone Harvest: an undead you kill has a {chance} chance to drop one more bone fragment.
    /// - Grave Sense: undead within {range} m get a marker over them that shows through walls (vanilla's NPC text).
    /// - Life Tap: your summons' and your thrall's hits heal you for {heal} of their damage.
    /// - Twin Link: while you possess a thrall, your first summon is kept on Follow.
    /// - Plague Bearer: a Withered enemy that dies passes Wither to the nearest enemy within {range} m.
    /// </summary>
    public static class DruidNecroPassives
    {
        private const string ThickHide = "druid_thick_hide";
        private const string BeastTongue = "druid_beast_tongue";
        private const string Forager = "druid_forager";
        private const string BoneHarvest = "necromancer_bone_harvest";
        private const string DeathlessWard = "necromancer_deathless_ward";
        private const string GraveSense = "necromancer_grave_sense";
        private const string LifeTap = "necromancer_life_tap";
        private const string TwinLink = "necromancer_twin_link";
        private const string PlagueBearer = "necromancer_plague_bearer";
        private static readonly string[] Ids = { ThickHide, BeastTongue, Forager, BoneHarvest, DeathlessWard, GraveSense, LifeTap, TwinLink, PlagueBearer };

        // Plague Bearer: the enemies the local player has Withered, with when it wears off; a dead one passes Wither on.
        private static readonly Dictionary<Character, float> WitheringSet = new Dictionary<Character, float>();
        private const float WitherWatchSeconds = 10f;

        internal const string BeastTongueZdo = "rpg_beast_tongue_range";
        internal const string DeathlessWardZdo = "rpg_deathless_ward_range";

        // Wild animals for Beast Tongue: vanilla's AnimalsVeg faction plus the animals in other factions (the Boar is
        // ForestMonsters, the wolves MountainMonsters, the Lox PlainsMonsters).
        private static readonly HashSet<string> WildAnimals = new HashSet<string>
        {
            "Boar", "Boar_piggy", "Deer", "Neck", "Wolf", "Wolf_cub", "Lox", "Lox_Calf", "Bjorn", "Unbjorn", "Asksvin", "Asksvin_hatchling", "Hare",
        };

        private static readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Shown = new Dictionary<string, float>();
        private static float _nextTick, _nextSense;
        private static Character _lastHitUndead;

        public static void RegisterAll()
        {
            foreach (string id in Ids) ClassSkillKit.RegisterPassive(id);
        }

        private static bool On(string id) => Values.ContainsKey(id);

        /// <summary>For FiresClassTest: the passive is learned, equipped and read this second, at this value (0 when off).</summary>
        public static bool IsOn(string id, out float value) => Values.TryGetValue(id, out value);

        /// <summary>For FiresClassTest: the ids this kit handles.</summary>
        public static IReadOnlyList<string> Handled => Ids;
        private static float V(string id) => Values.TryGetValue(id, out float v) ? v : 0f;

        /// <summary>PassiveKit.Tick's tail: the learned ranks, the ZDO ranges, Grave Sense's markers, Twin Link.</summary>
        internal static void Tick()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;
            Player player = Player.m_localPlayer;
            Values.Clear();
            if (player == null || player.IsDead()) return;
            Read(ThickHide, "armor");
            Read(BeastTongue, "range");
            Read(Forager, "chance");
            Read(BoneHarvest, "chance");
            Read(DeathlessWard, "range");
            Read(GraveSense, "range");
            Read(LifeTap, "heal");
            Read(TwinLink, null);
            Read(PlagueBearer, "range");
            SpreadPlague(player);

            Publish(player, BeastTongueZdo, V(BeastTongue));
            Publish(player, DeathlessWardZdo, V(DeathlessWard));

            if (On(GraveSense) && Time.time >= _nextSense && Chat.instance != null)
            {
                _nextSense = Time.time + 2f;
                float range = V(GraveSense);
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c.IsDead() || c.GetFaction() != Character.Faction.Undead || c.IsTamed()) continue;
                    if (Vector3.Distance(c.transform.position, player.transform.position) > range) continue;
                    Chat.instance.SetNpcText(c.gameObject, Ring.Up(c.transform.position) * (c.GetHeight() + 0.4f), range + 5f, 2.5f, "", "<color=#9fe7ff>(undead)</color>", false);
                }
            }

            if (On(TwinLink) && PossessionApi.IsPossessing(player, out _))
            {
                IReadOnlyList<SummonInfo> mine = SummonApi.MySummons();
                if (mine != null && mine.Count > 0 && mine[0].Mode != SummonMode.Follow)
                {
                    SummonApi.SetMode(mine[0].Id, SummonMode.Follow);
                    FiresRPGClassesPlugin.Log?.LogDebug($"[Passive] {TwinLink}: {mine[0].DisplayName} keeps following while you possess a thrall");
                }
            }
        }

        /// <summary>DruidNecroKit: the local player Withered this enemy (Wither, Grave Pestilence).</summary>
        internal static void Withering(Character enemy)
        {
            if (enemy != null) WitheringSet[enemy] = Time.time + WitherWatchSeconds;
        }


        // "When a Withered enemy dies, Wither jumps to the nearest enemy within {range} m."
        private static void SpreadPlague(Player player)
        {
            if (WitheringSet.Count == 0) return;
            var done = new List<Character>();
            var jumps = new List<Character>();
            foreach (KeyValuePair<Character, float> w in WitheringSet)
            {
                if (w.Key == null || Time.time > w.Value) { done.Add(w.Key); continue; }
                if (!w.Key.IsDead()) continue;
                done.Add(w.Key);
                if (On(PlagueBearer)) jumps.Add(w.Key);
            }
            foreach (Character c in done) WitheringSet.Remove(c);
            foreach (Character dead in jumps)
            {
                float range = V(PlagueBearer);
                Character next = null;
                float best = float.MaxValue;
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c == dead || c.IsDead() || !ClassTargeting.IsEnemyTarget(player, c)) continue;
                    float d = Vector3.Distance(c.transform.position, dead.transform.position);
                    if (d <= range && d < best) { best = d; next = c; }
                }
                if (next == null) continue;
                DruidNecroKit.ApplyWither(player, next, DruidNecroKit.WitherDps());
                FiresRPGClassesPlugin.Log?.LogDebug($"[Passive] {PlagueBearer}: Wither jumped from {dead.m_name} to {next.m_name} ({best:0.0} m)");
            }
        }

        private static void Read(string id, string key)
        {
            ClassTreeNode node = ClassTrees.Node(id);
            if (node == null || PassiveLoadout.Status(node) != PassiveStatus.Active) return;
            float v = key == null ? 1f : node.Value(key, DesignKit.RankOf(node));
            Values[id] = v;
            if (!Shown.TryGetValue(id, out float was) || Mathf.Abs(was - v) > 1e-4f)
            {
                Shown[id] = v;
                FiresRPGClassesPlugin.Log?.LogDebug($"[Passive] {id} on ({(key == null ? "on" : key + " " + v.ToString("0.###"))})");
            }
        }

        private static void Publish(Player player, string key, float range)
        {
            ZDO zdo = player.m_nview != null && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
            if (zdo != null && Mathf.Abs(zdo.GetFloat(key, 0f) - range) > 0.01f) zdo.Set(key, range);
        }

        /// <summary>The local player's hit before it is sent (Character.Damage prefix): Life Tap, and the undead Bone Harvest watches.</summary>
        internal static void OnHitSending(Character target, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if (player == null || hit == null || target == null) return;
            Character attacker = hit.GetAttacker();
            // Alpha Howl: your hits and your summons' while it runs.
            float howl = DruidNecroKit.HowlBonus;
            if (howl > 0f && attacker != null && (attacker == player || IsMine(player, attacker))) hit.m_damage.Modify(1f + howl);
            if (attacker == player)
            {
                if (target.GetFaction() == Character.Faction.Undead) _lastHitUndead = target;
                return;
            }
            if (!On(LifeTap) || attacker == null || !IsMine(player, attacker)) return;
            float heal = hit.GetTotalDamage() * V(LifeTap);
            if (heal <= 0f) return;
            player.Heal(heal, true);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Passive] {LifeTap}: {attacker.m_name} hit {target.m_name}, healed you {heal:0.#}");
        }

        // A summon of yours, or the thrall you possess.
        private static bool IsMine(Player player, Character c)
        {
            if (PossessionApi.IsPossessing(player, out Character possessed) && possessed == c) return true;
            ZNetView nview = c.m_nview;
            if (nview == null || !nview.IsValid()) return false;
            ZDOID id = nview.GetZDO().m_uid;
            IReadOnlyList<SummonInfo> mine = SummonApi.MySummons();
            if (mine == null) return false;
            foreach (SummonInfo s in mine) if (s.Id == id) return true;
            return false;
        }

        /// <summary>Damage taken, on the owner (Character.RPC_Damage prefix): Thick Hide on the form you possess.</summary>
        internal static void OnHitTaken(Character victim, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if (!On(ThickHide) || player == null || victim == null || hit == null || victim == player) return;
            if (!PossessionApi.IsPossessing(player, out Character form) || form != victim) return;
            float keep = 1f - Mathf.Clamp01(V(ThickHide));
            hit.m_damage.m_blunt *= keep;
            hit.m_damage.m_slash *= keep;
            hit.m_damage.m_pierce *= keep;
        }

        /// <summary>A pick the local player made (ClassXpHooks' Pickable.Interact postfix): Forager.</summary>
        internal static void Picked(Pickable pickable)
        {
            if (!On(Forager) || pickable == null || pickable.m_itemPrefab == null) return;
            string item = pickable.m_itemPrefab.name;
            bool forage = item.IndexOf("berr", System.StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("Mushroom", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!forage || Random.value >= V(Forager)) return;
            Object.Instantiate(pickable.m_itemPrefab, pickable.transform.position + Ring.Up(pickable.transform.position) * 0.4f, Quaternion.identity);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Passive] {Forager}: one more {item}");
        }

        /// <summary>A kill credited to the local player (Game.RPC_RegisterKill postfix): Bone Harvest.</summary>
        internal static void OnLocalKill()
        {
            Character undead = _lastHitUndead;
            _lastHitUndead = null;
            if (!On(BoneHarvest) || undead == null || !undead.IsDead() || Random.value >= V(BoneHarvest)) return;
            GameObject bone = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("BoneFragments") : null;
            if (bone == null) return;
            Object.Instantiate(bone, undead.transform.position + Ring.Up(undead.transform.position) * 0.5f, Quaternion.identity);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Passive] {BoneHarvest}: {undead.m_name} dropped one more bone fragment");
        }

        // Beast Tongue / Deathless Ward, on the creature owner's peer: a wild animal / an undead that is still unhurt doesn't take a
        // player as an enemy while it is within the range that player's ZDO shows.
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
        internal static class CalmerCreatures
        {
            [HarmonyPostfix]
            private static void Postfix(Character a, Character b, ref bool __result)
            {
                if (!__result || a == null || b == null) return;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                Player player = a as Player ?? b as Player;
                Character creature = player == a ? b : a;
                if (player == null || creature == null || creature.IsPlayer() || creature.IsTamed()) return;
                // This runs for every creature's target scans, and GetPrefabName allocates a string per call, so the
                // player's range is read first (0 for anyone without the passive - the common case returns here), undead before the
                // animal test, and the wild-animal test cached per creature instance.
                ZDO zdo = player.m_nview != null && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
                if (zdo == null) return;
                bool undead = creature.GetFaction() == Character.Faction.Undead;
                float range = zdo.GetFloat(undead ? DeathlessWardZdo : BeastTongueZdo, 0f);
                if (range <= 0f) return;
                if (!undead && !IsWildAnimal(creature)) return;
                if (creature.GetHealth() < creature.GetMaxHealth() - 0.01f) return;   // hurt: hostile as usual
                if (Vector3.Distance(player.transform.position, creature.transform.position) <= range) __result = false;
                }
                finally { PerfProbe.Add("IsEnemy postfix", System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
            }

            private static readonly System.Collections.Generic.Dictionary<int, bool> AnimalByInstance = new System.Collections.Generic.Dictionary<int, bool>();

            private static bool IsWildAnimal(Character creature)
            {
                int id = creature.GetInstanceID();
                if (AnimalByInstance.TryGetValue(id, out bool animal)) return animal;
                if (AnimalByInstance.Count > 4096) AnimalByInstance.Clear();
                animal = creature.GetFaction() == Character.Faction.AnimalsVeg || WildAnimals.Contains(Utils.GetPrefabName(creature.gameObject));
                AnimalByInstance[id] = animal;
                return animal;
            }
        }
    }
}
