using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The race potions (Tools\VAMPIRE_CLASS_DESIGN.md §6; Fire 10-13 LOCKED; amounts are [wishbone]'s placeholders for Fire):
    /// - Cleansing Mead: cures Moon-touched / Tainted Blood (the disease, before turning). Thistle 2 + Dandelion 2 + Honey 2.
    /// - Purifying Draught (vampirism): cures a turned vampire. Silver 5 + Moder trophy 1 + Honey 2.
    /// - Purifying Draught (lycanthropy): cures a turned werewolf. Silver 5 + Bonemass trophy 1 + Honey 2.
    /// - Blood Chalice: turns you into a vampire at once (opt-in). Bloodbag 10 + Silver 2.
    /// - Wolfsbane Brew: turns you into a werewolf at once (opt-in; "inverted" wolfsbane). Wolf fang 6 + Thistle 4 + Silver 2.
    /// All at the Cauldron (level 2 for the draughts). Clones of vanilla meads, recoloured; prefabs made once per process and added to
    /// every ObjectDB + ZNetScene that wakes. Drinking runs through Player.ConsumeItem: the item is used up only when its effect
    /// happened (a refusal says why and keeps the potion). The turning potions are hidden on a creation-only server or when the race is off.
    /// </summary>
    internal static class RaceItems
    {
        internal enum Kind { Cleansing, PurifyVampire, PurifyWerewolf, BloodChalice, Wolfsbane }

        private sealed class Def
        {
            public Kind Kind; public string Prefab, Name, Desc; public string[] Sources; public Color Tint; public int Station;
            public (string item, int amount)[] Parts;
        }

        private static readonly Def[] Defs =
        {
            new Def { Kind = Kind.Cleansing, Prefab = "RPG_CleansingMead", Name = "Cleansing Mead", Station = 1, Tint = new Color(0.55f, 0.85f, 0.55f),
                Desc = "A bitter, bright mead. Drives out Moon-touched or Tainted Blood before it takes hold.",
                Sources = new[] { "MeadPoisonResist", "MeadHealthMinor" }, Parts = new[] { ("Thistle", 2), ("Dandelion", 2), ("Honey", 2) } },
            new Def { Kind = Kind.PurifyVampire, Prefab = "RPG_PurifyingDraughtVampire", Name = "Purifying Draught (vampirism)", Station = 2, Tint = new Color(0.85f, 0.85f, 1f),
                Desc = "Silver and dragon frost. Cures a vampire and returns them to their old self. The class stays learned, dormant.",
                Sources = new[] { "MeadHealthMajor", "MeadHealthMinor" }, Parts = new[] { ("Silver", 5), ("TrophyDragonQueen", 1), ("Honey", 2) } },
            new Def { Kind = Kind.PurifyWerewolf, Prefab = "RPG_PurifyingDraughtWerewolf", Name = "Purifying Draught (lycanthropy)", Station = 2, Tint = new Color(0.9f, 0.9f, 0.75f),
                Desc = "Silver and the marrow of Bonemass. Cures a werewolf and returns them to their old self. The class stays learned, dormant.",
                Sources = new[] { "MeadHealthMajor", "MeadHealthMinor" }, Parts = new[] { ("Silver", 5), ("TrophyBonemass", 1), ("Honey", 2) } },
            new Def { Kind = Kind.BloodChalice, Prefab = "RPG_BloodChalice", Name = "Blood Chalice", Station = 1, Tint = new Color(0.45f, 0.04f, 0.06f),
                Desc = "Drink, and the night will claim you: you become a vampire at once.",
                Sources = new[] { "MeadEitrMinor", "MeadHealthMinor" }, Parts = new[] { ("Bloodbag", 10), ("Silver", 2) } },
            new Def { Kind = Kind.Wolfsbane, Prefab = "RPG_WolfsbaneBrew", Name = "Wolfsbane Brew", Station = 1, Tint = new Color(0.35f, 0.3f, 0.5f),
                Desc = "Wolfsbane brewed backwards. Drink, and the moon will claim you: you become a werewolf at once.",
                Sources = new[] { "MeadStaminaMedium", "MeadHealthMinor" }, Parts = new[] { ("WolfFang", 6), ("Thistle", 4), ("Silver", 2) } },
        };

        private static GameObject _holder;
        private static readonly Dictionary<Kind, GameObject> Prefabs = new Dictionary<Kind, GameObject>();
        private static readonly Dictionary<Kind, Recipe> Recipes = new Dictionary<Kind, Recipe>();

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Races] " + s);

        // 0.1.136 drink trace ([lead]: "bag 1 -> 1" three rounds running): identities, so a copy and the original can be told apart.
        internal static string Ident(ItemDrop.ItemData it) =>
            it == null ? "null" : $"#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(it):X} x{it.m_stack} @({it.m_gridPos.x},{it.m_gridPos.y}) shared #{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(it.m_shared):X}";

        internal static string InvIdent(Inventory inv) =>
            inv == null ? "null" : $"'{inv.GetName()}' #{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inv):X}";

        internal static string Instances(Inventory inv, ItemDrop.ItemData like)
        {
            if (inv == null || like == null) return "";
            var parts = new List<string>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
                if (it != null && (it.m_shared?.m_name == like.m_shared?.m_name || (it.m_dropPrefab != null && it.m_dropPrefab == like.m_dropPrefab)))
                    parts.Add($"{it.m_dropPrefab?.name ?? "?"} {Ident(it)}");
            return string.Join("; ", parts.ToArray());
        }

        // The managed callers (Harmony's DMD wrappers named as they come), outermost last.
        internal static string Caller(int skip)
        {
            var frames = new System.Diagnostics.StackTrace(skip, false).GetFrames();
            if (frames == null) return "?";
            var names = new List<string>();
            foreach (var f in frames)
            {
                MethodBase m = f.GetMethod();
                if (m == null) continue;
                names.Add((m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name);
                if (names.Count >= 8) break;
            }
            return string.Join(" < ", names.ToArray());
        }

        private static GameObject Source(Def d, Func<string, GameObject> find)
        {
            foreach (string s in d.Sources) { GameObject go = find(s); if (go != null) return go; }
            return null;
        }

        private static GameObject Ensure(Def d, GameObject source)
        {
            if (Prefabs.TryGetValue(d.Kind, out GameObject have) && have != null) return have;
            if (source == null) return null;
            if (_holder == null) { _holder = new GameObject("FiresRPGClasses_RacePrefabs"); _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder); }
            GameObject go = UnityEngine.Object.Instantiate(source, _holder.transform);
            go.name = d.Prefab;
            ItemDrop drop = go.GetComponent<ItemDrop>();
            MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            var shared = (ItemDrop.ItemData.SharedData)clone.Invoke(drop.m_itemData.m_shared, null);   // the vanilla mead keeps its data
            drop.m_itemData.m_shared = shared;
            drop.m_itemData.m_dropPrefab = go;
            shared.m_name = d.Name;
            shared.m_description = d.Desc;
            shared.m_consumeStatusEffect = null;
            shared.m_food = shared.m_foodStamina = shared.m_foodEitr = 0f;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            // [classes-ui]'s potion art (Icons\Items\item_<Prefab>.png); the source mead's icon stays when it's missing.
            Sprite icon = UI.ClassIcons.ForItem(d.Prefab);
            if (icon != null) shared.m_icons = new[] { icon };
            Log($"{d.Prefab}: icon {(icon != null ? "item_" + d.Prefab + ".png" : "the source mead's (no art embedded)")}");
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].HasProperty("_Color")) mats[i] = new Material(mats[i]) { color = d.Tint };
                r.sharedMaterials = mats;
            }
            Prefabs[d.Kind] = go;
            Log($"{d.Prefab} made from {source.name}");
            return go;
        }

        internal static void RegisterItems(ObjectDB db)
        {
            if (db == null || db.m_items == null || db.m_items.Count == 0) return;
            CraftingStation cauldron = null;
            foreach (Recipe r in db.m_recipes)
                if (r?.m_item != null && r.m_item.gameObject.name.StartsWith("Mead", StringComparison.Ordinal) && r.m_craftingStation != null) { cauldron = r.m_craftingStation; break; }
            bool added = false;
            foreach (Def d in Defs)
            {
                try
                {
                    GameObject go = Ensure(d, Source(d, db.GetItemPrefab));
                    if (go == null) { Log($"{d.Prefab}: no source mead in this ObjectDB"); continue; }
                    if (db.GetItemPrefab(d.Prefab) == null) { db.m_items.Add(go); added = true; }
                    if (!Recipes.TryGetValue(d.Kind, out Recipe rec) || rec == null) { rec = MakeRecipe(db, d, go, cauldron); if (rec != null) Recipes[d.Kind] = rec; }
                    if (rec != null && !db.m_recipes.Contains(rec)) db.m_recipes.Add(rec);
                }
                catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[Races] {d.Prefab} not registered: {e.GetType().Name}: {e.Message}"); }
            }
            if (added) db.UpdateRegisters();
        }

        private static Recipe MakeRecipe(ObjectDB db, Def d, GameObject prefab, CraftingStation station)
        {
            var parts = new List<Piece.Requirement>();
            foreach (var (item, amount) in d.Parts)
            {
                ItemDrop res = db.GetItemPrefab(item)?.GetComponent<ItemDrop>();
                if (res == null) { Log($"{d.Prefab} recipe skipped: no {item}"); return null; }
                parts.Add(new Piece.Requirement { m_resItem = res, m_amount = amount, m_amountPerLevel = 0 });
            }
            Recipe r = ScriptableObject.CreateInstance<Recipe>();
            r.name = "Recipe_" + d.Prefab;
            r.m_item = prefab.GetComponent<ItemDrop>();
            r.m_amount = 1;
            r.m_craftingStation = station;
            r.m_minStationLevel = d.Station;
            r.m_resources = parts.ToArray();
            Log($"{d.Prefab} recipe: {(station != null ? station.m_name : "hand-craft")} level {d.Station}");
            return r;
        }

        internal static void RegisterNet(ZNetScene scene)
        {
            if (scene == null) return;
            foreach (Def d in Defs)
            {
                GameObject go = Ensure(d, Source(d, scene.GetPrefab));
                if (go == null) continue;
                int hash = d.Prefab.GetStableHashCode();
                if (scene.m_namedPrefabs.ContainsKey(hash)) continue;
                scene.m_prefabs.Add(go);
                scene.m_namedPrefabs.Add(hash, go);
            }
        }

        internal static Kind? KindOf(ItemDrop.ItemData item)
        {
            string prefab = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (prefab == null) return null;
            foreach (Def d in Defs) if (d.Prefab == prefab) return d.Kind;
            return null;
        }

        /// <summary>A turning potion this server doesn't allow (creation-only, or the race off): its recipe is hidden.</summary>
        internal static bool Hidden(Recipe r)
        {
            if (r == null) return false;
            foreach (var kv in Recipes)
            {
                if (kv.Value != r) continue;
                if (kv.Key == Kind.BloodChalice) return RaceConfig.CreationOnly?.Value == true || !RaceConfig.On(RaceAcquire.Vampire);
                if (kv.Key == Kind.Wolfsbane) return RaceConfig.CreationOnly?.Value == true || !RaceConfig.On(RaceAcquire.Werewolf);
                return false;
            }
            return false;
        }

        /// <summary>Drinks one: null when it did something (the potion is used up), else the reason (it's kept).</summary>
        internal static string Drink(Player p, Kind kind)
        {
            string race = RaceAcquire.CurrentRace(p);
            bool diseased = RaceAcquire.TryGetDisease(p, out string disease, out _);
            switch (kind)
            {
                case Kind.Cleansing:
                    if (!diseased) return race.Length > 0 ? "too late for a mead: only a Purifying Draught helps now" : "there's nothing in your blood to cleanse";
                    RaceAcquire.ClearDisease(p, "Cleansing Mead");
                    p.Message(MessageHud.MessageType.Center, "Your blood runs clean.");
                    return null;
                case Kind.PurifyVampire:
                case Kind.PurifyWerewolf:
                    string target = kind == Kind.PurifyVampire ? RaceAcquire.Vampire : RaceAcquire.Werewolf;
                    if (RaceConfig.CureMode?.Value == RaceConfig.CureShrine) return "on this server only a shrine can cure a turned player";
                    if (race != target) return race.Length > 0 ? "this draught cures " + target.ToLowerInvariant() + "s, not " + race.ToLowerInvariant() + "s" : "you are not a " + target.ToLowerInvariant();
                    return RaceAcquire.CureTurned(p, target) ? null : "the cure couldn't restore your look (VCS refused)";
                case Kind.BloodChalice:
                case Kind.Wolfsbane:
                    string to = kind == Kind.BloodChalice ? RaceAcquire.Vampire : RaceAcquire.Werewolf;
                    if (RaceConfig.CreationOnly?.Value == true) return "this server gives races only at character creation";
                    if (!RaceConfig.On(to)) return to + " is switched off on this server";
                    if (race.Length > 0) return "you are already a " + race.ToLowerInvariant();
                    if (diseased) RaceAcquire.ClearDisease(p, "drank a turning potion");
                    return RaceAcquire.Turn(p, to, kind == Kind.BloodChalice ? "Blood Chalice" : "Wolfsbane Brew") ? null : "the change won't take hold (the server doesn't allow that look)";
            }
            return "unknown potion";
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class RaceItemsDbPatch
    {
        [HarmonyPostfix]
        private static void After(ObjectDB __instance)
        {
            RaceItems.RegisterItems(__instance);
            RaceEffects.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class RaceItemsNetPatch
    {
        [HarmonyPostfix] private static void After(ZNetScene __instance) => RaceItems.RegisterNet(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class RacePotionDrink
    {
        [HarmonyPrefix]
        private static bool Before(Player __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __result)
        {
            var kind = RaceItems.KindOf(item);
            if (kind == null || __instance != Player.m_localPlayer) return true;
            string why = RaceItems.Drink(__instance, kind.Value);
            if (why != null)
            {
                __instance.Message(MessageHud.MessageType.Center, why);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Races] {item.m_shared.m_name} not used: {why}");
                __result = false;
                return false;
            }
            Inventory inv = inventory ?? __instance.GetInventory();
            int before = inv.CountItems(item.m_shared.m_name);
            int stack = item.m_stack;
            // 0.1.136 ([lead], strike 3 on "bag 1 -> 1"): every instance of this potion in the bag (identity, stack, grid) right before
            // and after the removal, the drunk instance and who called. 0.1.137: kept at Debug ([lead]); it showed the cause - the
            // tester's god mode with ServerDevcommands' "No item usage with god mode" leaves the potion (not a game bug).
            string drunkId = RaceItems.Ident(item);
            string beforeList = RaceItems.Instances(inv, item);
            bool removed = inv.RemoveOneItem(item);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Races] {item.m_shared.m_name} drunk: RemoveOneItem {removed}, stack {stack}, bag {before} -> {inv.CountItems(item.m_shared.m_name)}"
                + $" ({(inventory != null && inventory != __instance.GetInventory() ? "another inventory" : "the player's bag")})");
            FiresRPGClassesPlugin.Log?.LogDebug($"[Races] drink trace: drunk {drunkId} (still in the bag {inv.GetAllItems().Contains(item)}); before [{beforeList}] -> after [{RaceItems.Instances(inv, item)}]; "
                + $"inventory {RaceItems.InvIdent(inv)} vs the player's {RaceItems.InvIdent(__instance.GetInventory())}; called from {RaceItems.Caller(3)}");
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
    internal static class RaceRecipeList
    {
        [HarmonyPostfix]
        private static void After(ref List<Recipe> available)
        {
            if (available == null) return;
            for (int i = available.Count - 1; i >= 0; i--) if (RaceItems.Hidden(available[i])) available.RemoveAt(i);
        }
    }

    /// <summary>The two disease templates in ObjectDB (readable names + their family icons), once per ObjectDB.</summary>
    internal static class RaceEffects
    {
        internal static void Register(ObjectDB db)
        {
            if (db == null) return;
            Add<MoonTouchedEffect>(db, RaceAcquire.MoonTouched);
            Add<TaintedBloodEffect>(db, RaceAcquire.TaintedBlood);
        }

        private static void Add<T>(ObjectDB db, string name) where T : FiresCore.Npc.Archetypes.StatusEffects.CompanionStatusEffectBase
        {
            if (db.GetStatusEffect(name.GetStableHashCode()) != null) return;
            var e = ScriptableObject.CreateInstance<T>();
            e.name = name;
            e.m_name = e.DisplayName;
            e.m_tooltip = e.Description;
            e.EffectIcon = e.m_icon = UI.ClassIcons.ForEffect(name);
            db.m_StatusEffects.Add(e);
        }
    }
}
