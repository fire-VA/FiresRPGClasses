using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// CRAFT Paragon stars, batch 1. Every hook here is local-player only and
    /// reads its size with ParagonEffects.TX. Chances and damage-taken cuts go through Core's SoftCap.Asymptote (knee 30%, max 60%), like
    /// SE_Paragon's damage-taken guard, so stacking never reaches "always" or immunity.
    /// </summary>
    [ParagonEffectSet]
    internal static class CraftBatch1
    {
        internal const string Chef = "+{value}% food duration";
        internal const string LongHaul = "-{value}% stamina drain while encumbered";
        internal const string Roadwright = "-{value}% build and repair stamina away from your base";
        internal const string ToolCare = "-{value}% tool durability loss";
        internal const string Cartographer = "+{value}% map explore radius";
        internal const string Seafarer = "+{value}% ship speed while you steer";
        internal const string SteadySeat = "-{value}% damage taken while mounted";
        internal const string MountedCombat = "+{value}% damage from the saddle";
        internal const string MasterSmith = "{value}% chance to refund crafting materials";
        internal const string Architect = "{value}% chance a build piece refunds its materials";
        internal const string Forager = "+{value}% extra wild pick chance";
        internal const string GreenThumb = "+{value}% extra crop yield chance";
        internal const string HorseSense = "+{value}% mount max stamina";

        public static readonly string[] Formats =
        {
            Chef, LongHaul, Roadwright, ToolCare, Cartographer, Seafarer, SteadySeat, MountedCombat, MasterSmith, Architect, Forager, GreenThumb, HorseSense,
        };

        internal static float Chance(string f) => FiresCore.Curves.SoftCap.Asymptote(ParagonEffects.TX(f), 0.30f, 0.60f);
        internal static bool Roll(string f) { float c = Chance(f); return c > 0f && UnityEngine.Random.value < c; }
        internal static bool Mine(Character c) => c != null && c == Player.m_localPlayer;

        private static readonly System.Collections.Generic.HashSet<string> _logged = new System.Collections.Generic.HashSet<string>();
        internal static void Once(string f, string what)
        {
            if (_logged.Add(f)) FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] craft effect live: '{f}' {what}");
        }

        // Chef: a food you eat lasts longer (its timer starts above the item's burn time; the bar clamps at full).
        [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
        private static class ChefPatch
        {
            private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
            {
                float k = ParagonEffects.TX(Chef);
                if (!__result || k <= 0f || !Mine(__instance) || item == null) return;
                foreach (Player.Food f in __instance.m_foods)
                    if (f.m_item != null && f.m_item.m_shared.m_name == item.m_shared.m_name && Mathf.Approximately(f.m_time, item.m_shared.m_foodBurnTime))
                    {
                        f.m_time = item.m_shared.m_foodBurnTime * (1f + k);
                        Once(Chef, $"{item.m_shared.m_name} {item.m_shared.m_foodBurnTime:0}s -> {f.m_time:0}s");
                    }
            }
        }

        // Long Haul: the encumbered drain field, scaled for this UpdateStats only.
        [HarmonyPatch(typeof(Player), "UpdateStats", typeof(float))]
        private static class LongHaulPatch
        {
            private static void Prefix(Player __instance, out float __state)
            {
                __state = __instance.m_encumberedStaminaDrain;
                float k = Mathf.Clamp01(ParagonEffects.TX(LongHaul));
                if (k > 0f && Mine(__instance)) __instance.m_encumberedStaminaDrain = __state * (1f - k);
            }
            private static void Postfix(Player __instance, float __state) => __instance.m_encumberedStaminaDrain = __state;
        }

        // Roadwright: build and repair stamina (both use GetBuildStamina) outside any player base area.
        [HarmonyPatch(typeof(Player), "GetBuildStamina")]
        private static class RoadwrightPatch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                float k = Mathf.Clamp01(ParagonEffects.TX(Roadwright));
                if (k <= 0f || !Mine(__instance) || EffectArea.IsPointInsideArea(__instance.transform.position, EffectArea.Type.PlayerBase)) return;
                __result *= 1f - k;
                Once(Roadwright, "away from base");
            }
        }

        // Tool Care: refund part of the durability a tool just lost. Tools = Tool-type items (hammer, hoe, cultivator...) and pickaxes.
        internal static bool IsTool(ItemDrop.ItemData i) => i != null && (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool || i.m_shared.m_skillType == Skills.SkillType.Pickaxes);
        internal static void Refund(ItemDrop.ItemData i, float before)
        {
            float k = Mathf.Clamp(ParagonEffects.TX(ToolCare), 0f, 0.8f);
            if (k <= 0f || i == null || i.m_durability >= before) return;
            i.m_durability += (before - i.m_durability) * k;
            Once(ToolCare, i.m_shared.m_name);
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        private static class ToolCarePlace
        {
            private static void Prefix(Player __instance, out float __state)
            {
                var r = __instance.GetRightItem();
                __state = Mine(__instance) && IsTool(r) ? r.m_durability : -1f;
                if (Mine(__instance)) _placing = true;
            }
            private static void Postfix(Player __instance, float __state)
            {
                _placing = false;
                if (__state >= 0f) Refund(__instance.GetRightItem(), __state);
            }
            private static Exception Finalizer(Exception __exception) { _placing = false; return __exception; }
        }

        [HarmonyPatch(typeof(Player), "Repair")]
        private static class ToolCareRepair
        {
            private static void Prefix(Player __instance, ItemDrop.ItemData toolItem, out float __state) => __state = Mine(__instance) && IsTool(toolItem) ? toolItem.m_durability : -1f;
            private static void Postfix(ItemDrop.ItemData toolItem, float __state) { if (__state >= 0f) Refund(toolItem, __state); }
        }

        [HarmonyPatch]
        private static class ToolCareAttack
        {
            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                // A target missing on this game version is skipped, never a null that drops the whole class
                foreach (var name in new[] { "DoMeleeAttack", "DoNonAttack" })
                {
                    var m = AccessTools.Method(typeof(Attack), name);
                    if (m != null) yield return m;
                }
            }
            private static void Prefix(Attack __instance, out float __state) => __state = Mine(__instance.m_character) && IsTool(__instance.m_weapon) ? __instance.m_weapon.m_durability : -1f;
            private static void Postfix(Attack __instance, float __state) { if (__state >= 0f) Refund(__instance.m_weapon, __state); }
        }

        // Cartographer: the explore radius, scaled for this UpdateExplore only.
        [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
        private static class CartographerPatch
        {
            private static void Prefix(Minimap __instance, Player player, out float __state)
            {
                __state = __instance.m_exploreRadius;
                float k = ParagonEffects.TX(Cartographer);
                if (k > 0f && Mine(player)) { __instance.m_exploreRadius = __state * (1f + k); Once(Cartographer, $"{__state:0} -> {__instance.m_exploreRadius:0} m"); }
            }
            private static void Postfix(Minimap __instance, float __state) => __instance.m_exploreRadius = __state;
        }

        // Seafarer: sail force while the local player holds this ship's rudder (the steering client simulates the ship).
        [HarmonyPatch(typeof(Ship), "GetSailForce")]
        private static class SeafarerPatch
        {
            private static void Postfix(Ship __instance, ref Vector3 __result)
            {
                float k = ParagonEffects.TX(Seafarer);
                Player me = Player.m_localPlayer;
                if (k <= 0f || me == null || me.GetControlledShip() != __instance) return;
                __result *= 1f + k;
                Once(Seafarer, "sail force");
            }
        }

        // Steady Seat / Mounted Combat: through the rider's SEMan, the same calls SE_Paragon's OnDamaged / ModifyAttack use.
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
        private static class SteadySeatPatch
        {
            private static void Postfix(SEMan __instance, HitData hit)
            {
                if (hit == null || !Mine(__instance.m_character) || !__instance.m_character.IsRiding()) return;
                float k = Chance(SteadySeat);
                if (k <= 0f) return;
                hit.m_damage.Modify(1f - k);
                Once(SteadySeat, $"-{k * 100f:0.#}%");
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
        private static class MountedCombatPatch
        {
            private static void Postfix(SEMan __instance, ref HitData hitData)
            {
                float k = ParagonEffects.TX(MountedCombat);
                if (k <= 0f || hitData == null || !Mine(__instance.m_character) || !__instance.m_character.IsRiding()) return;
                hitData.m_damage.Modify(1f + k);
                Once(MountedCombat, $"+{k * 100f:0.#}%");
            }
        }

        // Master Smith / Architect: a roll that skips one ConsumeResources call made by crafting / by placing a piece.
        private static bool _crafting, _placing;

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class CraftFlag
        {
            private static void Prefix(Player player) => _crafting = Mine(player);
            private static void Postfix() => _crafting = false;
            private static Exception Finalizer(Exception __exception) { _crafting = false; return __exception; }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        private static class RefundPatch
        {
            private static bool Prefix(Player __instance)
            {
                if (!Mine(__instance)) return true;
                string f = _crafting ? MasterSmith : _placing ? Architect : null;
                if (f == null || !Roll(f)) return true;
                __instance.Message(MessageHud.MessageType.TopLeft, "Materials refunded");
                Once(f, "materials kept");
                return false;
            }
        }

        // Forager / Green Thumb: one extra item on a pick. Crops (planted, gone once picked) roll Green Thumb; everything else Forager.
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class PickPatch
        {
            private static void Prefix(Pickable __instance, Humanoid character, out bool __state) => __state = Mine(character) && !__instance.m_picked && __instance.m_enabled != 0;
            private static void Postfix(Pickable __instance, Humanoid character, bool __state)
            {
                if (!__state || __instance.m_itemPrefab == null) return;
                bool crop = __instance.m_respawnTimeMinutes <= 0f && !__instance.name.StartsWith("Pickable_", StringComparison.Ordinal);
                string f = crop ? GreenThumb : Forager;
                if (!Roll(f)) return;
                Vector3 p = __instance.transform.position + Vector3.up * 0.5f;
                ItemDrop.OnCreateNew(UnityEngine.Object.Instantiate(__instance.m_itemPrefab, p, Quaternion.identity));
                Once(f, __instance.m_itemPrefab.name);
            }
        }

        // Horse Sense: the max stamina of the mount the local player rides.
        [HarmonyPatch(typeof(Sadle), nameof(Sadle.GetMaxStamina))]
        private static class HorseSensePatch
        {
            private static void Postfix(Sadle __instance, ref float __result)
            {
                float k = ParagonEffects.TX(HorseSense);
                Player me = Player.m_localPlayer;
                if (k <= 0f || me == null || !me.IsRiding() || (object)me.GetDoodadController() != __instance) return;
                __result *= 1f + k;
                Once(HorseSense, $"{__result:0}");
            }
        }
    }
}
