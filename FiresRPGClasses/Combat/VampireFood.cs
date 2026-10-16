using System.Collections.Generic;
using System.Reflection;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Data;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Duskbloom Stew (Tools\VAMPIRE_CLASS_DESIGN.md §12): RPG_DuskbloomStew, a clone of vanilla's Black Soup recoloured dark red, cooked
    /// at the Cauldron (level 1) from 2 Bloodbag + 2 Thistle + 1 Turnip + 1 Ooze. Food 30 HP / 30 stamina / 0 eitr, 1 HP a tick, 30 min.
    /// The recipe is known only to vampires (rpg_race == Vampire): Player.HaveRequirements(Recipe...) refuses it to others, so they never
    /// discover or craft it, and GetAvailableRecipes drops it for them; anyone can still eat one. A vampire who eats it gets RPG_SunWard for
    /// the food's time (VampireKit lifts the daylight penalty while it lasts, and removes it when the food runs out).
    /// The prefab is made once per process under an inactive holder and added to every ObjectDB (items + recipe) and ZNetScene (so it
    /// spawns and syncs) that wakes.
    /// </summary>
    internal static class VampireFood
    {
        internal const string PrefabName = "RPG_DuskbloomStew";
        internal const string ItemName = "Duskbloom Stew";
        private const string SourcePrefab = "BlackSoup";
        private static readonly Color Tint = new Color(0.45f, 0.06f, 0.09f);

        private static GameObject _holder;
        private static GameObject _prefab;
        private static Recipe _recipe;

        internal static GameObject Prefab => _prefab;

        /// <summary>The recipe this mod added (null before an ObjectDB woke with Black Soup in it).</summary>
        internal static bool IsOurs(Recipe r) => r != null && _recipe != null && (r == _recipe || (r.m_item != null && r.m_item.gameObject == _prefab));

        internal static bool IsStew(ItemDrop.ItemData item) => item?.m_shared != null && item.m_shared.m_name == ItemName;

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Vampire] " + s);

        private static GameObject EnsurePrefab(GameObject source)
        {
            if (_prefab != null) return _prefab;
            if (source == null) return null;
            _holder = new GameObject("FiresRPGClasses_VampirePrefabs");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
            _prefab = Object.Instantiate(source, _holder.transform);
            _prefab.name = PrefabName;

            ItemDrop drop = _prefab.GetComponent<ItemDrop>();
            // The shared data is a reference shared with Black Soup: a copy first, so the vanilla soup keeps its numbers.
            MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            var shared = (ItemDrop.ItemData.SharedData)clone.Invoke(drop.m_itemData.m_shared, null);
            drop.m_itemData.m_shared = shared;
            drop.m_itemData.m_dropPrefab = _prefab;
            shared.m_name = ItemName;
            shared.m_description = "A dark stew of blood and dusk-bitter herbs. Vampires who eat it can walk under the sun.";
            shared.m_food = 30f;
            shared.m_foodStamina = 30f;
            shared.m_foodEitr = 0f;
            shared.m_foodRegen = 1f;
            shared.m_foodBurnTime = VampireConfig.Value(VampireConfig.SunWardSeconds, 1800f);
            shared.m_consumeStatusEffect = null;

            int tinted = 0;
            foreach (Renderer r in _prefab.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                    mats[i] = new Material(mats[i]) { color = Tint };
                    tinted++;
                }
                r.sharedMaterials = mats;
            }
            Log($"{PrefabName} made from {SourcePrefab} ({tinted} material(s) tinted dark red, food 30/30/0, {shared.m_foodBurnTime:0} s)");

            if (VampireConfig.SunWardSeconds != null) VampireConfig.SunWardSeconds.SettingChanged += (s, e) => ApplyConfig();
            if (VampireConfig.SunWardEnabled != null) VampireConfig.SunWardEnabled.SettingChanged += (s, e) => ApplyConfig();
            return _prefab;
        }

        // The synced values arrive after the ObjectDB woke: the food time and the recipe's switch follow them.
        private static void ApplyConfig()
        {
            if (_prefab != null) _prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_foodBurnTime = VampireConfig.Value(VampireConfig.SunWardSeconds, 1800f);
            if (_recipe != null) _recipe.m_enabled = VampireConfig.Value(VampireConfig.SunWardEnabled, true);
            Log($"Duskbloom Stew config: recipe {(_recipe != null && _recipe.m_enabled ? "on" : "off")}, {VampireConfig.Value(VampireConfig.SunWardSeconds, 1800f):0} s");
        }

        /// <summary>ObjectDB.Awake postfix: the item and the Cauldron recipe, once per ObjectDB.</summary>
        internal static void RegisterItem(ObjectDB db)
        {
            if (db == null || db.m_items == null || db.m_items.Count == 0) return;
            try
            {
                GameObject source = db.GetItemPrefab(SourcePrefab);
                if (source == null) { Log($"no {SourcePrefab} in this ObjectDB: Duskbloom Stew not added"); return; }
                GameObject prefab = EnsurePrefab(source);
                if (prefab == null) return;
                if (db.GetItemPrefab(PrefabName) == null)
                {
                    db.m_items.Add(prefab);
                    db.UpdateRegisters();
                }
                if (_recipe == null) _recipe = MakeRecipe(db, prefab);
                if (_recipe != null && !db.m_recipes.Contains(_recipe)) db.m_recipes.Add(_recipe);
                ApplyConfig();
            }
            catch (System.Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[Vampire] Duskbloom Stew not registered: {e.GetType().Name}: {e.Message}"); }
        }

        private static Recipe MakeRecipe(ObjectDB db, GameObject prefab)
        {
            // The Cauldron is Black Soup's own station (a recipe of the same ObjectDB), so ZNetScene isn't needed here.
            CraftingStation cauldron = null;
            foreach (Recipe r in db.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject.name == SourcePrefab) { cauldron = r.m_craftingStation; break; }
            var parts = new List<Piece.Requirement>();
            foreach (var (name, amount) in new[] { ("Bloodbag", 2), ("Thistle", 2), ("Turnip", 1), ("Ooze", 1) })
            {
                ItemDrop res = db.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (res == null) { Log($"Duskbloom Stew recipe skipped: no {name} item"); return null; }
                parts.Add(new Piece.Requirement { m_resItem = res, m_amount = amount, m_amountPerLevel = 0 });
            }
            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + PrefabName;
            recipe.m_item = prefab.GetComponent<ItemDrop>();
            recipe.m_amount = 1;
            recipe.m_craftingStation = cauldron;
            recipe.m_minStationLevel = 1;
            recipe.m_resources = parts.ToArray();
            recipe.m_enabled = VampireConfig.Value(VampireConfig.SunWardEnabled, true);
            Log($"Duskbloom Stew recipe: {(cauldron != null ? cauldron.m_name : "no station found (hand-craft)")} level 1, 2 Bloodbag + 2 Thistle + 1 Turnip + 1 Ooze");
            return recipe;
        }

        /// <summary>ZNetScene.Awake postfix: the prefab in this scene's lists, so it spawns and syncs.</summary>
        internal static void RegisterNet(ZNetScene scene)
        {
            if (scene == null) return;
            GameObject prefab = EnsurePrefab(scene.GetPrefab(SourcePrefab));
            if (prefab == null) return;
            int hash = PrefabName.GetStableHashCode();
            if (scene.m_namedPrefabs.ContainsKey(hash)) return;
            scene.m_prefabs.Add(prefab);
            scene.m_namedPrefabs.Add(hash, prefab);
            Log($"{PrefabName} added to ZNetScene");
        }

        /// <summary>A recipe check for the player: ours is refused to anyone who isn't a vampire (and to everyone when switched off).</summary>
        internal static bool Hidden(Player player, Recipe recipe)
        {
            if (!IsOurs(recipe)) return false;
            if (!VampireConfig.Value(VampireConfig.SunWardEnabled, true)) return true;
            return !VampireRace.Of(player);
        }

        /// <summary>Player.EatFood postfix: a vampire who ate the stew gets RPG_SunWard for the food's time.</summary>
        internal static void OnAte(Player player, ItemDrop.ItemData item)
        {
            if (player == null || player != Player.m_localPlayer || !IsStew(item)) return;
            if (!VampireConfig.Value(VampireConfig.SunWardEnabled, true)) { Log("Duskbloom Stew eaten: sun ward switched off on this server"); return; }
            if (!VampireRace.Local) { Log("Duskbloom Stew eaten by a non-vampire: food only, no sun ward"); return; }
            float seconds = item.m_shared.m_foodBurnTime > 0f ? item.m_shared.m_foodBurnTime : VampireConfig.Value(VampireConfig.SunWardSeconds, 1800f);
            bool ok = AbilityRPCManager.ApplySelfBuff(player, VampireEffects.SunWard, seconds);
            Log($"Duskbloom Stew eaten: {VampireEffects.SunWard} {(ok ? "on" : "NOT applied")} for {seconds:0} s");
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class VampireFoodNetPatch
    {
        [HarmonyPostfix]
        private static void AfterAwake(ZNetScene __instance) => VampireFood.RegisterNet(__instance);
    }

    // Player.HaveRequirements(Recipe, bool discover, int qualityLevel, int amount): the one Recipe overload (the other takes a Piece).
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
    internal static class VampireRecipeRequirements
    {
        [HarmonyPrefix]
        private static bool Before(Player __instance, Recipe recipe, ref bool __result)
        {
            if (!VampireFood.Hidden(__instance, recipe)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
    internal static class VampireRecipeList
    {
        [HarmonyPostfix]
        private static void After(Player __instance, ref List<Recipe> available)
        {
            if (available == null) return;
            for (int i = available.Count - 1; i >= 0; i--)
                if (VampireFood.Hidden(__instance, available[i])) available.RemoveAt(i);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
    internal static class VampireEatFood
    {
        [HarmonyPostfix]
        private static void After(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result) VampireFood.OnAte(__instance, item);
        }
    }
}
