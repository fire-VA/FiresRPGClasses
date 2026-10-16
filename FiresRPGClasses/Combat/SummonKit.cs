using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using FiresCore.Summon;
using FiresRPGClasses.Data;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The summoning ladders (Tools\CLASSES_DRUID_NECROMANCER.md section 5b, Fire's S1-S4) on Core's SummonApi: the Druid's Pack
    /// path calls beasts, the Necromancer's Bone path raises the dead. Each rung is a vanilla creature at the design's scale,
    /// spawned tamed by Core next to the caster, with the node's {time} (or its duration) as the timer. Core keeps the shared
    /// cap (2, +1 per 15 levels, max 6; the oldest goes first), the timer, portals and logout.
    /// Defaults used: every rung starts in Follow (Core's Guard and Attack leave the summon where it stands); the Raise Archer
    /// is the Dead Raiser's skeleton armed only with its bow sets (or the vanilla skeleton bow when it has none).
    /// </summary>
    public static class SummonKit
    {
        private sealed class Rung
        {
            public string Prefab;
            public float Scale = 1f;
            public string CountKey;
            public int Count = 1;
            public bool FillCap;
            public string Loadout;
        }

        private const string ArcherLoadout = "bow";
        private const string MeleeLoadout = "melee";
        private const string SkeletonBow = "skeleton_bow";
        private const string NecroFx = "fx_summon_skeleton_spawn";
        private const string DruidFx = "vfx_spawn";
        private const int BaseCap = 2;
        private const int LevelsPerCap = 15;
        private const int MaxCap = 6;   // 0.1.131: the fallback of [13 - Balance] "Summon server limit" (synced, performance)
        private const float DefaultSeconds = 60f;

        // The ZDO mark a summon's Humanoid.Start reads to pick its loadout, so a reload keeps it an archer.
        private static readonly int LoadoutKey = "rpg_summon_loadout".GetStableHashCode();

        private static readonly Dictionary<string, Rung> Rungs = new Dictionary<string, Rung>
        {
            { "druid_call_bear",              new Rung { Prefab = "Bjorn", Scale = 0.5f } },
            { "druid_call_unbjorn",           new Rung { Prefab = "Unbjorn", Scale = 0.5f } },
            { "druid_wolf_pack",              new Rung { Prefab = "Wolf", CountKey = "wolves", Count = 2 } },
            { "druid_call_lox",               new Rung { Prefab = "Lox", Scale = 0.6f } },
            { "druid_call_asksvin",           new Rung { Prefab = "Asksvin" } },
            { "druid_great_bear",             new Rung { Prefab = "Bjorn" } },
            { "necromancer_raise_skeleton",   new Rung { Prefab = "Skeleton_Friendly", Loadout = MeleeLoadout } },
            { "necromancer_raise_archer",     new Rung { Prefab = "Skeleton_Friendly", Loadout = ArcherLoadout } },
            { "necromancer_raise_draugr",     new Rung { Prefab = "Draugr" } },
            { "necromancer_raise_wraith",     new Rung { Prefab = "Wraith" } },
            { "necromancer_raise_charred",    new Rung { Prefab = "Charred_Melee" } },
            { "necromancer_army_of_the_dead", new Rung { Prefab = "Skeleton_Friendly", FillCap = true } },
        };

        private static readonly FiresCore.Logging.FiresLog DebugLog = new FiresCore.Logging.FiresLog("FiresRPGClasses", () => false);
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            foreach (KeyValuePair<string, Rung> entry in Rungs)
            {
                Rung rung = entry.Value;
                ClassSkillKit.Register(entry.Key, (node, caster, target) => Summon(rung, node, caster));
            }
        }

        /// <summary>Fire S2: 2, +1 per 15 class levels, at most 6.</summary>
        internal static int Cap() => Mathf.Min(Mathf.Max(1, BalanceConfig.Value(BalanceConfig.SummonServerMax, MaxCap)), BaseCap + PossessionKit.ClassLevel() / LevelsPerCap);

        private static bool Summon(Rung rung, ClassTreeNode node, Character caster)
        {
            if (!(caster is Player player)) return false;
            int rank = DesignKit.RankOf(node);
            float eitr = PossessionKit.EitrCost(node);
            if (!player.HaveEitr(eitr)) return Refuse(player, node, "not enough eitr");

            int cap = Cap();
            int count = rung.FillCap ? Mathf.Max(1, cap - SummonApi.MyCount)
                : rung.CountKey != null ? Mathf.Max(1, Mathf.RoundToInt(node.Value(rung.CountKey, rank, rung.Count))) : rung.Count;
            float seconds = node.Value("time", rank, node.EffectDuration > 0f ? node.EffectDuration : DefaultSeconds);
            bool necro = node.Archetype == ArchetypeClass.Necromancer;
            var options = new SummonOptions
            {
                Scale = rung.Scale,
                Duration = seconds,
                Mode = SummonMode.Follow,
                Cap = cap,
                SourceTag = node.Id,
            };

            int made = 0;
            string why = null;
            for (int i = 0; i < count; i++)
            {
                ZDOID id = SummonApi.Summon(rung.Prefab, options, out why);
                if (id.IsNone()) break;
                made++;
                GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
                if (go == null) continue;
                if (rung.Loadout != null) go.GetComponent<ZNetView>()?.GetZDO()?.Set(LoadoutKey, rung.Loadout);
                AbilityRPCManager.SpawnFX(go.transform.position, necro ? NecroFx : DruidFx);
            }
            if (made == 0) return Refuse(player, node, why ?? "nothing answered the call");
            player.UseEitr(eitr);
            FiresRPGClassesPlugin.Log.LogInfo($"[SummonKit] {node.Id}: {made} x {rung.Prefab} (scale {rung.Scale:0.##}, {seconds:0} s, cap {cap}, now {SummonApi.MyCount})");
            return true;
        }

        private static bool Refuse(Player player, ClassTreeNode node, string why)
        {
            player?.Message(MessageHud.MessageType.Center, node.DisplayName + ": " + why);
            DebugLog.Debug($"[SummonKit] {node.Id} refused: {why}");
            return false;
        }

        // The Raise Archer keeps only the skeleton's bow sets (or gets the vanilla skeleton bow), every time it loads; the Raise Skeleton
        // (round 12: vanilla's random sets sometimes made it a second archer) keeps only the sets and weapons without a bow.
        [HarmonyPatch(typeof(Humanoid), "Start")]
        internal static class SummonLoadoutOnStart
        {
            [HarmonyPrefix]
            private static void Prefix(Humanoid __instance)
            {
                if (__instance == null || __instance.IsPlayer()) return;
                ZNetView nv = __instance.GetComponent<ZNetView>();
                ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                string loadout = zdo != null ? zdo.GetString(LoadoutKey, "") : "";
                if (loadout == ArcherLoadout) ArmWithBow(__instance);
                else if (loadout == MeleeLoadout) ArmMelee(__instance);
            }
        }

        private static void ArmWithBow(Humanoid h)
        {
            var bowSets = new List<Humanoid.ItemSet>();
            if (h.m_randomSets != null)
                foreach (Humanoid.ItemSet set in h.m_randomSets)
                    if (set?.m_items != null && System.Array.Exists(set.m_items, IsBow)) bowSets.Add(set);
            h.m_randomWeapon = new GameObject[0];
            h.m_randomShield = new GameObject[0];
            h.m_randomSets = bowSets.ToArray();
            if (bowSets.Count > 0) return;
            var keep = new List<GameObject>();
            if (h.m_defaultItems != null)
                foreach (GameObject item in h.m_defaultItems)
                    if (item != null && !IsWeaponOrShield(item)) keep.Add(item);
            GameObject bow = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(SkeletonBow) : null;
            if (bow != null) keep.Add(bow);
            h.m_defaultItems = keep.ToArray();
        }

        // A bow by its item type: vanilla's enemy bows (skeleton_bow, _bow2, _bow_meadows) are ItemType.Bow on the Swords skill.
        private static bool IsBow(GameObject item)
        {
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            if (drop == null) return false;
            var shared = drop.m_itemData.m_shared;
            return shared.m_itemType == ItemDrop.ItemData.ItemType.Bow || shared.m_skillType == Skills.SkillType.Bows;
        }

        private static void ArmMelee(Humanoid h)
        {
            var meleeSets = new List<Humanoid.ItemSet>();
            if (h.m_randomSets != null)
                foreach (Humanoid.ItemSet set in h.m_randomSets)
                    if (set?.m_items != null && !System.Array.Exists(set.m_items, IsBow)) meleeSets.Add(set);
            if (h.m_randomSets != null && h.m_randomSets.Length > 0 && meleeSets.Count > 0) h.m_randomSets = meleeSets.ToArray();
            if (h.m_randomWeapon != null) h.m_randomWeapon = System.Array.FindAll(h.m_randomWeapon, w => w != null && !IsBow(w));
            if (h.m_defaultItems != null) h.m_defaultItems = System.Array.FindAll(h.m_defaultItems, w => w != null && !IsBow(w));
        }

        private static bool IsWeaponOrShield(GameObject item)
        {
            ItemDrop drop = item.GetComponent<ItemDrop>();
            if (drop == null) return false;
            switch (drop.m_itemData.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                    return true;
                default:
                    return false;
            }
        }
    }
}
