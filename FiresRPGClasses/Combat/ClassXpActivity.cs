using System;
using System.Collections.Generic;
using System.Reflection;
using FiresCore.Logging;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// A little class XP for the everyday actions, map exploration
    /// included. Each pays a share of the current level's cost through ClassXp.Award on the local
    /// player's own client (the class record lives in the character's .fch), under its own rolling hourly cap of
    /// <see cref="HourlyCapShare"/> of a level, so an hour of one thing never shuts out another:
    /// - exploring: every <see cref="ExploreAreaPerAward"/> m2 of map the player's own walk or sail reveals for the first time,
    ///   0.5 %. Counted in Minimap.Explore around the player; vanilla marks a cell explored once per character per world, so
    ///   known ground pays nothing, and a cartography table's shared map, 'exploremap', the intro flight and debug fly pay nothing;
    /// - crafting: an item made, 0.5 % (an upgrade 1 %), on vanilla's own CraftsOrUpgrades count (its cheated flag pays nothing).
    ///   The same item again within 10 minutes pays half the last one, and nothing after the 4th;
    /// - cooking: a cooked (not burnt) item taken off a cooking station or oven, or a food made at the cauldron, 0.5 %;
    /// - building: a piece vanilla counts free of build debt (BuiltPiecesNoDebt: a piece placed after removing one pays off the
    ///   debt instead, so place/remove loops pay nothing), or a crop planted, 0.25 %;
    /// - processing: an item put into a smelter, kiln, furnace, windmill, spinning wheel or refinery, or a fermenter, 0.25 %;
    /// - fishing: a fish landed, 1 %;
    /// - sailing: every 1 km steered at the helm (vanilla's DistanceSailHelm), 0.5 %;
    /// - honey and sap taken pay as gathering (ClassXp.Gathered), like a pick.
    /// Not counted: known ground, jumps, doors, eating, emotes, petting, item stands - repeatable without any work.
    /// </summary>
    internal static class ClassXpActivity
    {
        internal const float HourlyCapShare = 0.10f;
        internal const float ExploreAreaPerAward = 30000f;
        private const float ExploreShare = 0.005f;
        private const float CraftShare = 0.005f;
        private const float UpgradeShare = 0.01f;
        private const float CookShare = 0.005f;
        private const float BuildShare = 0.0025f;
        private const float ProcessShare = 0.0025f;
        private const float FishShare = 0.01f;
        private const float SailShare = 0.005f;
        private const float SailMetresPerAward = 1000f;
        private const float RepeatWindowSeconds = 600f;
        private const int RepeatsThatPay = 4;

        private static readonly FiresLog DebugLog = new FiresLog("FiresRPGClasses", () => false);
        private const string Tag = "[ClassXp] ";

        private static Player Local => Player.m_localPlayer;

        private static void Pay(string source, float share, string detail) => ClassXp.Activity(source, share, detail);

        // -- exploring ------------------------------------------------------------------------------------------------------

        private static bool _counting;
        private static int _newCells;
        private static float _unpaidArea;

        [HarmonyPatch(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) })]
        private static class ExploreAround
        {
            private static void Prefix() { _counting = true; _newCells = 0; }

            private static void Postfix(Minimap __instance, Vector3 p)
            {
                _counting = false;
                if (_newCells > 0) Explored(_newCells, __instance.m_pixelSize, p);
            }
        }

        [HarmonyPatch(typeof(Minimap), "Explore", new[] { typeof(int), typeof(int) })]
        private static class ExploreCell
        {
            private static void Postfix(bool __result) { if (_counting && __result) _newCells++; }
        }

        private static void Explored(int cells, float pixelSize, Vector3 at)
        {
            Player player = Local;
            if (player == null || player.IsDead() || player.InIntro() || player.InDebugFlyMode()) return;
            _unpaidArea += cells * pixelSize * pixelSize;
            while (_unpaidArea >= ExploreAreaPerAward)
            {
                _unpaidArea -= ExploreAreaPerAward;
                Pay("exploring", ExploreShare, $"map revealed {ExploreAreaPerAward:0} m2 near ({at.x:0}, {at.z:0})");
            }
        }

        // -- crafting and cauldron food: vanilla's CraftsOrUpgrades count, for the recipe DoCrafting is making -----------------

        private sealed class PendingCraft
        {
            public string Prefab;
            public bool Upgrade, Food;
            public int Quality;
        }

        private static PendingCraft _craft;
        private static readonly Dictionary<string, KeyValuePair<int, float>> _repeats = new Dictionary<string, KeyValuePair<int, float>>();

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class Craft
        {
            private static void Prefix(Player player, Recipe ___m_craftRecipe, ItemDrop.ItemData ___m_craftUpgradeItem)
            {
                _craft = null;
                ItemDrop made = ___m_craftRecipe != null ? ___m_craftRecipe.m_item : null;
                if (player == null || player != Local || made == null || made.m_itemData?.m_shared == null) return;
                ItemDrop.ItemData.SharedData shared = made.m_itemData.m_shared;
                _craft = new PendingCraft
                {
                    Prefab = Utils.GetPrefabName(made.gameObject),
                    Upgrade = ___m_craftUpgradeItem != null,
                    Quality = ___m_craftUpgradeItem != null ? ___m_craftUpgradeItem.m_quality + 1 : 1,
                    Food = shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                        && (shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f),
                };
            }

            private static void Postfix() => _craft = null;
        }

        private static void Crafted(bool cheated)
        {
            PendingCraft craft = _craft;
            _craft = null;
            if (craft == null || cheated) return;
            if (craft.Food) { Pay("cooking", CookShare, "made " + craft.Prefab); return; }
            if (craft.Upgrade) { Pay("crafting", UpgradeShare, $"upgraded {craft.Prefab} to q{craft.Quality}"); return; }

            float now = Time.time;
            int earlier = _repeats.TryGetValue(craft.Prefab, out KeyValuePair<int, float> seen) && now - seen.Value < RepeatWindowSeconds ? seen.Key : 0;
            _repeats[craft.Prefab] = new KeyValuePair<int, float>(earlier + 1, now);
            if (earlier >= RepeatsThatPay)
            {
                DebugLog.Debug($"{Tag}crafting {craft.Prefab}: made {earlier + 1} times within {RepeatWindowSeconds / 60f:0} min, pays nothing until a gap");
                return;
            }
            Pay("crafting", CraftShare / (1 << earlier), $"made {craft.Prefab}" + (earlier > 0 ? $" (repeat {earlier + 1}, 1/{1 << earlier} pay)" : ""));
        }

        // -- cooking stations: a done item taken off (OnInteract raises the cook stat for it, the burnt stat for a burnt one) --

        private static readonly MethodInfo HaveDoneItem = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
        private static readonly MethodInfo NextItemStatus = AccessTools.Method(typeof(CookingStation), "NextItemStatus");
        private const int StatusDone = 1;

        [HarmonyPatch(typeof(CookingStation), "OnInteract")]
        private static class CookTaken
        {
            private static void Prefix(CookingStation __instance, Humanoid user, out bool __state)
            {
                __state = false;
                if (user == null || user != Local || HaveDoneItem == null || NextItemStatus == null) return;
                try
                {
                    __state = (bool)HaveDoneItem.Invoke(__instance, null)
                        && Convert.ToInt32(NextItemStatus.Invoke(__instance, null)) == StatusDone;
                }
                catch (Exception) { __state = false; }
            }

            private static void Postfix(CookingStation __instance, bool __result, bool __state)
            {
                if (__state && __result) Pay("cooking", CookShare, "cooked at " + Utils.GetPrefabName(__instance.gameObject));
            }
        }

        // -- building and planting -------------------------------------------------------------------------------------------

        private static string _placed;

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class Place
        {
            private static void Postfix(Player __instance, Piece piece, bool __result)
            {
                if (__instance != Local || !__result || piece == null) return;
                _placed = Utils.GetPrefabName(piece.gameObject);
                // The cultivator's crops are not hammer pieces (no BuiltPieces count) and can't be taken back down for a refund.
                if (piece.GetComponent<Plant>() != null && !__instance.NoCostCheat()) Pay("building", BuildShare, "planted " + _placed);
            }
        }

        // -- processing: one item into a smelter-type station or a fermenter --------------------------------------------------

        [HarmonyPatch(typeof(Smelter), "OnAddOre")]
        private static class AddOre
        {
            private static void Postfix(Smelter __instance, Humanoid user, bool __result)
            {
                if (__result && user != null && user == Local) Pay("processing", ProcessShare, "added to " + Utils.GetPrefabName(__instance.gameObject));
            }
        }

        [HarmonyPatch(typeof(Fermenter), "AddItem")]
        private static class AddMeadBase
        {
            private static void Postfix(Fermenter __instance, Humanoid user, bool __result)
            {
                if (__result && user != null && user == Local) Pay("processing", ProcessShare, "added to " + Utils.GetPrefabName(__instance.gameObject));
            }
        }

        // -- fishing: the catch reaching the rod runs on the caster's own client ----------------------------------------------

        [HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Catch))]
        private static class FishLanded
        {
            private static void Postfix(Fish fish, Character owner)
            {
                if (owner != null && owner == Local && fish != null) Pay("fishing", FishShare, "landed " + Utils.GetPrefabName(fish.gameObject));
            }
        }

        // -- vanilla's own stat counts: builds free of debt, helm distance, honey and sap, crafts -----------------------------

        private static float _sailed;

        [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.IncrementStat))]
        private static class Stat
        {
            private static void Postfix(PlayerProfile __instance, PlayerStatType stat, float amount, bool cheated)
            {
                if (Local == null || Game.instance == null || __instance != Game.instance.GetPlayerProfile()) return;
                ExtraSkills.ExtraSkillSystem.OnStat(stat, amount); // extra skills' XP
                switch (stat)
                {
                    case PlayerStatType.CraftsOrUpgrades:
                        Crafted(cheated);
                        break;
                    case PlayerStatType.BuiltPiecesNoDebt:
                        if (!cheated && !Local.NoCostCheat()) Pay("building", BuildShare, "built " + (_placed ?? "a piece"));
                        break;
                    case PlayerStatType.DistanceSailHelm:
                        _sailed += amount;
                        while (_sailed >= SailMetresPerAward)
                        {
                            _sailed -= SailMetresPerAward;
                            Vector3 at = Local.transform.position;
                            Pay("sailing", SailShare, $"steered {SailMetresPerAward:0} m near ({at.x:0}, {at.z:0})");
                        }
                        break;
                    case PlayerStatType.BeesHarvested:
                        ClassXp.Gathered("honey taken");
                        break;
                    case PlayerStatType.SapHarvested:
                        ClassXp.Gathered("sap taken");
                        break;
                }
            }
        }
    }
}
