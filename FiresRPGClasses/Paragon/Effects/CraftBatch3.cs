using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// CRAFT batch 3: the remaining Craft Paragon passives. Local player only;
    /// sizes from ParagonEffects.TX.
    /// </summary>
    [ParagonEffectSet]
    internal static class CraftBatch3
    {
        internal const string Brewer = "+{value}% fermenter speed";
        internal const string KilnMaster = "+{value}% speed of stations you fill";
        internal const string Angler = "+{value}% bite speed and catch chance";
        internal const string Beastmaster = "+{value}% tamed creature health and damage";
        internal const string FieldKitchen = "+{value}% food stamina and eitr from food you cooked or grew";

        public static readonly string[] Formats = { Brewer, KilnMaster, Angler, Beastmaster, FieldKitchen };

        private const float MaxHookChance = 0.95f;   // Angler never makes a bite certain

        private static bool Mine(Character c) => CraftBatch1.Mine(c);
        private static void Once(string f, string what) => CraftBatch1.Once(f, what);

        // ── Stations the local player filled this session (ZDO ids). Their time runs faster while this client simulates them (the
        // ZDO owner, normally the player standing at them).
        private static readonly HashSet<ZDOID> _filledSmelters = new HashSet<ZDOID>(), _filledFermenters = new HashSet<ZDOID>();
        private static ZDOID Id(MonoBehaviour m) { var nv = m != null ? m.GetComponent<ZNetView>() : null; return nv != null && nv.IsValid() ? nv.GetZDO().m_uid : ZDOID.None; }

        [HarmonyPatch]
        private static class SmelterFill
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                // A target missing on this game version is skipped, never a null that drops the whole class
                foreach (var name in new[] { "OnAddOre", "OnAddFuel" })
                {
                    var m = AccessTools.Method(typeof(Smelter), name);
                    if (m != null) yield return m;
                }
            }
            private static void Postfix(Smelter __instance, Humanoid user, bool __result) { if (__result && Mine(user)) _filledSmelters.Add(Id(__instance)); }
        }

        // Kiln Master: more elapsed time per smelter / kiln / kiln-like station update.
        [HarmonyPatch(typeof(Smelter), "GetDeltaTime")]
        private static class KilnMasterPatch
        {
            private static void Postfix(Smelter __instance, ref double __result)
            {
                float k = ParagonEffects.TX(KilnMaster);
                if (k <= 0f || __result <= 0d || !_filledSmelters.Contains(Id(__instance))) return;
                __result *= 1d + k;
                Once(KilnMaster, __instance.name);
            }
        }

        [HarmonyPatch(typeof(Fermenter), "AddItem")]
        private static class FermenterFill
        {
            private static void Postfix(Fermenter __instance, Humanoid user, bool __result) { if (__result && Mine(user)) _filledFermenters.Add(Id(__instance)); }
        }

        // Brewer: a fermenter the local player filled counts its elapsed time faster (ready check and progress both read this).
        [HarmonyPatch(typeof(Fermenter), "GetFermentationTime")]
        private static class BrewerPatch
        {
            private static void Postfix(Fermenter __instance, ref double __result)
            {
                float k = ParagonEffects.TX(Brewer);
                if (k <= 0f || __result <= 0d || !_filledFermenters.Contains(Id(__instance))) return;
                __result *= 1d + k;
                Once(Brewer, __instance.name);
            }
        }

        // ── Angler: a fish deciding whether to go for a float uses a higher hook chance while the local player's float is out. Bites come
        // sooner and more often (one roll is both "bite speed" and "catch chance"); capped at 95%.
        private static readonly System.Reflection.MethodInfo FloatOwner = AccessTools.Method(typeof(FishingFloat), "GetOwner");

        private static bool MyFloatOut()
        {
            Player me = Player.m_localPlayer;
            if (me == null || FloatOwner == null) return false;
            foreach (FishingFloat f in FishingFloat.GetAllInstances())
                if (f != null && (object)FloatOwner.Invoke(f, null) == me) return true;
            return false;
        }

        [HarmonyPatch(typeof(Fish), "FindFloat")]
        private static class AnglerPatch
        {
            private static void Prefix(Fish __instance, out float __state)
            {
                __state = __instance.m_baseHookChance;
                float k = ParagonEffects.TX(Angler);
                if (k <= 0f || !MyFloatOut()) return;
                __instance.m_baseHookChance = Mathf.Min(MaxHookChance, __state * (1f + k));
                Once(Angler, $"hook {__state:0.##} -> {__instance.m_baseHookChance:0.##}");
            }
            private static void Postfix(Fish __instance, float __state) => __instance.m_baseHookChance = __state;
        }

        // ── Beastmaster: tamed creatures within 30 m of the local player deal +x% and take 1/(1+x) damage (= +x% effective health).
        private const float BeastRange = 30f;
        private static bool MyTame(Character c)
        {
            Player me = Player.m_localPlayer;
            return c != null && me != null && !(c is Player) && c.IsTamed() && Vector3.Distance(c.transform.position, me.transform.position) <= BeastRange;
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
        private static class BeastDamage
        {
            private static void Postfix(SEMan __instance, ref HitData hitData)
            {
                float k = ParagonEffects.TX(Beastmaster);
                if (k <= 0f || hitData == null || !MyTame(__instance.m_character)) return;
                hitData.m_damage.Modify(1f + k);
                Once(Beastmaster, "damage");
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
        private static class BeastHealth
        {
            private static void Postfix(SEMan __instance, HitData hit)
            {
                float k = ParagonEffects.TX(Beastmaster);
                if (k <= 0f || hit == null || !MyTame(__instance.m_character)) return;
                hit.m_damage.Modify(1f / (1f + k));
                Once(Beastmaster, "health");
            }
        }

        // ── Field Kitchen: food the local player crafted (cauldron / workbench recipes record the crafter) gives more stamina and eitr.
        // Vanilla keeps no grower on crops or cooking-station output, so "grew" can't be told apart: crafted food only.
        [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
        private static class FieldKitchenPatch
        {
            private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
            {
                float k = ParagonEffects.TX(FieldKitchen);
                if (!__result || k <= 0f || !Mine(__instance) || item == null || item.m_crafterID == 0L || item.m_crafterID != __instance.GetPlayerID()) return;
                foreach (Player.Food f in __instance.m_foods)
                    if (f.m_item != null && f.m_item.m_shared.m_name == item.m_shared.m_name && Mathf.Approximately(f.m_stamina, item.m_shared.m_foodStamina))
                    {
                        f.m_stamina = item.m_shared.m_foodStamina * (1f + k);
                        f.m_eitr = item.m_shared.m_foodEitr * (1f + k);
                        Once(FieldKitchen, $"{item.m_shared.m_name} stamina {f.m_stamina:0.#}, eitr {f.m_eitr:0.#}");
                    }
            }
        }
    }
}
