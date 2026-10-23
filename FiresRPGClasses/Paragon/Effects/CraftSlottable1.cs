using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// CRAFT slottable Paragon stars, part 1. Live only while slotted (ParagonEffects
    /// counts a slottable's TX only when it sits in one of its tree's 4 slots). Local player only; chances via CraftBatch1.Chance (30/60).
    /// </summary>
    [ParagonEffectSet]
    internal static class CraftSlottable1
    {
        internal const string Bountiful = "{value}% chance any gather yields double";
        internal const string Treasure = "+{value}% chance of an extra loot roll in chests";
        internal const string Masterwork = "items you craft get +{value}% damage or armour";
        internal const string SeaWolf = "while you steer: +{value}% ship speed, crew take half as much less damage";
        internal const string PackLeader = "your summons and tames within 20 m: +{value}% damage and health";
        internal const string Steeds = "mount +{value}% stamina regen, half as much health regen";
        internal const string RidersCharge = "a sprinting mount's first hit deals +{value}% and knocks back";

        public static readonly string[] Formats = { Bountiful, Treasure, Masterwork, SeaWolf, PackLeader, Steeds, RidersCharge };

        private static bool Mine(Character c) => CraftBatch1.Mine(c);
        private static void Once(string f, string what) => CraftBatch1.Once(f, what);

        // ── Bountiful Harvest: a pick, or a tree / rock the local player breaks, yields its drops twice.
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class BountifulPick
        {
            private static void Prefix(Pickable __instance, Humanoid character, out bool __state) => __state = Mine(character) && !__instance.m_picked && __instance.m_enabled != 0;
            private static void Postfix(Pickable __instance, bool __state)
            {
                if (!__state || __instance.m_itemPrefab == null || !CraftBatch1.Roll(Bountiful)) return;
                Vector3 p = __instance.transform.position + Vector3.up * 0.5f;
                for (int i = 0; i < Mathf.Max(1, __instance.m_amount); i++)
                    ItemDrop.OnCreateNew(UnityEngine.Object.Instantiate(__instance.m_itemPrefab, p, Quaternion.identity));
                Once(Bountiful, "pick x2 " + __instance.m_itemPrefab.name);
            }
        }

        // trees / logs / rocks: CraftBatch2's local-hit flag; the whole drop list is doubled on a roll
        [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
        private static class BountifulDrops
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(List<GameObject> __result)
            {
                if (!CraftBatch2.LocalBreaking || __result == null || __result.Count == 0 || !CraftBatch1.Roll(Bountiful)) return;
                __result.AddRange(__result.ToArray());
                Once(Bountiful, "drops x2");
            }
        }

        // ── Treasure Hunter: a chest filled (first load, on this client) near the local player rolls its loot table once more.
        [HarmonyPatch(typeof(Container), "AddDefaultItems")]
        private static class TreasurePatch
        {
            private static void Postfix(Container __instance)
            {
                Player me = Player.m_localPlayer;
                if (me == null || Vector3.Distance(me.transform.position, __instance.transform.position) > 64f || !CraftBatch1.Roll(Treasure)) return;
                Inventory inv = __instance.GetInventory();
                if (inv == null) return;
                foreach (ItemDrop.ItemData it in __instance.m_defaultItems.GetDropListItems()) inv.AddItem(it);
                Once(Treasure, __instance.name);
            }
        }

        // ── Masterwork: weapons / armour the local player crafted (m_crafterID) deal / give more. Tooltips read the same calls.
        private static bool MyCraft(ItemDrop.ItemData i) { Player me = Player.m_localPlayer; return i != null && me != null && i.m_crafterID != 0L && i.m_crafterID == me.GetPlayerID(); }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float))]
        private static class MasterworkDamage
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
            {
                float k = ParagonEffects.TX(Masterwork);
                if (k <= 0f || !MyCraft(__instance)) return;
                __result.Modify(1f + k);
                Once(Masterwork, "damage " + __instance.m_shared.m_name);
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetArmor), typeof(int), typeof(float))]
        private static class MasterworkArmour
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                float k = ParagonEffects.TX(Masterwork);
                if (k <= 0f || __result <= 0f || !MyCraft(__instance)) return;
                __result *= 1f + k;
                Once(Masterwork, "armour " + __instance.m_shared.m_name);
            }
        }

        // ── Sea Wolf: sail force while the local player steers (as Seafarer, its own size), and half the value off the damage taken by the
        // steerer and by tames standing on that ship. Other players' damage is handled on their own clients, which can't see this star.
        [HarmonyPatch(typeof(Ship), "GetSailForce")]
        private static class SeaWolfSpeed
        {
            private static void Postfix(Ship __instance, ref Vector3 __result)
            {
                float k = ParagonEffects.TX(SeaWolf);
                Player me = Player.m_localPlayer;
                if (k <= 0f || me == null || me.GetControlledShip() != __instance) return;
                __result *= 1f + k;
                Once(SeaWolf, "sail force");
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
        private static class SeaWolfCrew
        {
            private static void Postfix(SEMan __instance, HitData hit)
            {
                float k = ParagonEffects.TX(SeaWolf) * 0.5f;
                Player me = Player.m_localPlayer;
                Ship ship = me != null ? me.GetControlledShip() : null;
                Character c = __instance.m_character;
                if (k <= 0f || hit == null || ship == null || c == null) return;
                if (!(c == me || (c.IsTamed() && c.GetStandingOnShip() == ship))) return;
                hit.m_damage.Modify(1f - FiresCore.Curves.SoftCap.Asymptote(k, 0.30f, 0.60f));
                Once(SeaWolf, "crew damage");
            }
        }

        // ── Pack Leader: tames and summons (summons are tamed creatures) within 20 m: +x% damage, take 1/(1+x) damage.
        private static bool Pack(Character c)
        {
            Player me = Player.m_localPlayer;
            return c != null && me != null && !(c is Player) && c.IsTamed() && Vector3.Distance(c.transform.position, me.transform.position) <= 20f;
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
        private static class PackDamage
        {
            private static void Postfix(SEMan __instance, ref HitData hitData)
            {
                float k = ParagonEffects.TX(PackLeader);
                if (k <= 0f || hitData == null || !Pack(__instance.m_character)) return;
                hitData.m_damage.Modify(1f + k);
                Once(PackLeader, "damage");
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
        private static class PackHealth
        {
            private static void Postfix(SEMan __instance, HitData hit)
            {
                float k = ParagonEffects.TX(PackLeader);
                if (k <= 0f || hit == null || !Pack(__instance.m_character)) return;
                hit.m_damage.Modify(1f / (1f + k));
                Once(PackLeader, "health");
            }
        }

        // ── Steed's Blessing: the ridden mount's stamina regen fields for this UpdateStamina call; its health regen through its SEMan.
        [HarmonyPatch(typeof(Sadle), "UpdateStamina")]
        private static class SteedStamina
        {
            private static void Prefix(Sadle __instance, out Vector2 __state)
            {
                __state = new Vector2(__instance.m_staminaRegen, __instance.m_staminaRegenHungry);
                float k = ParagonEffects.TX(Steeds);
                if (k <= 0f || CraftBatch2.RiddenSadle() != __instance) return;
                __instance.m_staminaRegen = __state.x * (1f + k);
                __instance.m_staminaRegenHungry = __state.y * (1f + k);
                Once(Steeds, "stamina regen");
            }
            private static void Postfix(Sadle __instance, Vector2 __state) { __instance.m_staminaRegen = __state.x; __instance.m_staminaRegenHungry = __state.y; }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
        private static class SteedHealth
        {
            private static void Postfix(SEMan __instance, ref float regenMultiplier)
            {
                float k = ParagonEffects.TX(Steeds) * 0.5f;
                Sadle s = k > 0f ? CraftBatch2.RiddenSadle() : null;
                if (s == null || s.GetCharacter() != __instance.m_character) return;
                regenMultiplier *= 1f + k;
                Once(Steeds, "health regen");
            }
        }

        // ── Rider's Charge: the first hit the ridden mount lands while running (once per sprint) deals +x% and pushes twice as hard.
        private static readonly System.Reflection.FieldInfo SadleSpeed = AccessTools.Field(typeof(Sadle), "m_speed");
        private static bool _chargeUsed;

        private static bool Sprinting(Sadle s) => s != null && SadleSpeed != null && Convert.ToInt32(SadleSpeed.GetValue(s)) == 2;   // Sadle.Speed.Run (Stop, Walk, Run, Turn, NoChange)

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
        private static class ChargeHit
        {
            private static void Postfix(SEMan __instance, ref HitData hitData)
            {
                float k = ParagonEffects.TX(RidersCharge);
                Sadle s = k > 0f ? CraftBatch2.RiddenSadle() : null;
                if (s == null || hitData == null || s.GetCharacter() != __instance.m_character) return;
                if (!Sprinting(s)) { _chargeUsed = false; return; }
                if (_chargeUsed) return;
                _chargeUsed = true;
                hitData.m_damage.Modify(1f + k);
                hitData.m_pushForce *= 2f;
                Once(RidersCharge, $"+{k * 100f:0.#}%");
            }
        }

        [HarmonyPatch(typeof(Sadle), "UpdateRiding")]
        private static class ChargeReset
        {
            private static void Postfix(Sadle __instance) { if (_chargeUsed && CraftBatch2.RiddenSadle() == __instance && !Sprinting(__instance)) _chargeUsed = false; }
        }
    }
}
