using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// Paragon CRAFT star effects, batch 2. Local player only; sizes from ParagonEffects.TX;
    /// chances through CraftBatch1.Chance (SoftCap.Asymptote 30/60).
    /// </summary>
    [ParagonEffectSet]
    internal static class CraftBatch2
    {
        internal const string Lumberjack = "+{value}% extra wood drop chance";
        internal const string Prospector = "+{value}% extra ore and stone drop chance";
        internal const string Gallop = "+{value}% mount run speed";
        internal const string SwiftTaming = "+{value}% taming speed";
        internal const string WildForm = "+{value}% Bat and Wolf Form duration";
        internal const string Weathered = "-{value}% wet, cold and freezing duration";
        internal const string Wayfarer = "+{value}% move speed out of combat";

        public static readonly string[] Formats = { Lumberjack, Prospector, Gallop, SwiftTaming, WildForm, Weathered, Wayfarer };

        private static bool Mine(Character c) => CraftBatch1.Mine(c);
        private static void Once(string f, string what) => CraftBatch1.Once(f, what);

        // ── Lumberjack / Prospector: while a tree / log / rock the local player hit is breaking on this client, one DropTable roll
        // may add a copy of one of its drops (the drop code itself spawns it, so positions and conversions stay vanilla).
        private static string _dropFormat;
        internal static bool LocalBreaking => _dropFormat != null;   // CraftSlottable1 (Bountiful Harvest) reads it

        [HarmonyPatch]
        private static class DropSource
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                // Valheim 1.0.20 renamed MineRock.RPC_Damage to RPC_Hit (same sender/hit/hitAreaIndex); both names are tried, and a
                // target missing on this game version is skipped (otherwise one missing method would drop the whole patch class)
                var targets = new[] { (typeof(TreeBase), "RPC_Damage"), (typeof(TreeLog), "RPC_Damage"), (typeof(MineRock), "RPC_Hit"),
                    (typeof(MineRock), "RPC_Damage"), (typeof(MineRock5), "RPC_Damage") };
                foreach (var (type, name) in targets)
                {
                    // Looked up in the declared list, not AccessTools.DeclaredMethod, so the probe for a name this game version lacks
                    // (MineRock.RPC_Damage on 1.0.20) logs no HarmonyX "Could not find method" warning
                    var m = System.Linq.Enumerable.FirstOrDefault(AccessTools.GetDeclaredMethods(type), x => x.Name == name);
                    if (m != null) yield return m;
                }
            }
            private static void Prefix(object __instance, HitData hit)
            {
                _dropFormat = null;
                if (hit == null || !Mine(hit.GetAttacker())) return;
                _dropFormat = __instance is TreeBase || __instance is TreeLog ? Lumberjack : Prospector;
            }
            private static void Postfix() => _dropFormat = null;
            private static Exception Finalizer(Exception __exception) { _dropFormat = null; return __exception; }
        }

        [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
        private static class DropExtra
        {
            private static void Postfix(List<GameObject> __result)
            {
                string f = _dropFormat;
                if (f == null || __result == null || __result.Count == 0 || !CraftBatch1.Roll(f)) return;
                GameObject extra = __result[UnityEngine.Random.Range(0, __result.Count)];
                __result.Add(extra);
                Once(f, extra != null ? extra.name : "?");
            }
        }

        // ── Gallop: the run speed factor of the mount the local player rides.
        internal static Sadle RiddenSadle()
        {
            Player me = Player.m_localPlayer;
            return me != null && me.IsRiding() ? me.GetDoodadController() as Sadle : null;
        }

        [HarmonyPatch(typeof(Character), "GetRunSpeedFactor")]
        private static class GallopPatch
        {
            private static void Postfix(Character __instance, ref float __result)
            {
                float k = ParagonEffects.TX(Gallop);
                if (k <= 0f || __instance is Player) return;
                Sadle s = RiddenSadle();
                if (s == null || s.GetCharacter() != __instance) return;
                __result *= 1f + k;
                Once(Gallop, $"x{1f + k:0.###}");
            }
        }

        // ── Swift Taming: each taming tick of a creature the local player is near (vanilla's own taming-boost range) counts more.
        [HarmonyPatch(typeof(Tameable), "DecreaseRemainingTime")]
        private static class SwiftTamingPatch
        {
            private static void Prefix(Tameable __instance, ref float time)
            {
                float k = ParagonEffects.TX(SwiftTaming);
                Player me = Player.m_localPlayer;
                if (k <= 0f || me == null || time <= 0f) return;
                if (Vector3.Distance(me.transform.position, __instance.transform.position) > __instance.m_tamingSpeedMultiplierRange) return;
                time *= 1f + k;
                Once(SwiftTaming, $"tick x{1f + k:0.###}");
            }
        }

        // ── Wild Form: Bat Form's duration (VampireKit.BatDuration) and the Wolf Form skill's requested seconds.
        // The werewolf SERVER caps a skill form at WerewolfServer.SkillSecondsCap (30 s): the wolf part only shows once that cap
        // allows it.
        [HarmonyPatch(typeof(FiresRPGClasses.Combat.VampireKit), nameof(FiresRPGClasses.Combat.VampireKit.BatDuration))]
        private static class WildFormBat
        {
            private static void Postfix(ref float __result)
            {
                float k = ParagonEffects.TX(WildForm);
                if (k <= 0f) return;
                __result *= 1f + k;
                Once(WildForm, $"bat {__result:0} s");
            }
        }

        [HarmonyPatch(typeof(FiresRPGClasses.Combat.WerewolfKit), "Request")]
        private static class WildFormWolf
        {
            private static void Prefix(object reason, ref float seconds)
            {
                float k = ParagonEffects.TX(WildForm);
                if (k <= 0f || Convert.ToInt32(reason) != 0) return;   // Reason.Skill only (Rage / Moon are not "your form")
                seconds *= 1f + k;
                Once(WildForm, $"wolf {seconds:0} s requested");
            }
        }

        // ── Weathered: Wet's time to live on the local player. Cold and Freezing have no timer in vanilla (they last while the weather
        // does), so this star shortens Wet, the only one of the three with a duration.
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short))]
        private static class WeatheredPatch
        {
            private static void Postfix(SEMan __instance, StatusEffect __result)
            {
                if (__result == null || __result.NameHash() != SEMan.s_statusEffectWet || !Mine(__instance.m_character)) return;
                float k = Mathf.Clamp(ParagonEffects.TX(Weathered), 0f, 0.8f);
                StatusEffect tpl = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectWet) : null;
                if (k <= 0f || tpl == null || tpl.m_ttl <= 0f) return;
                __result.m_ttl = tpl.m_ttl * (1f - k);
                Once(Weathered, $"Wet {tpl.m_ttl:0} -> {__result.m_ttl:0} s");
            }
        }

        // ── Wayfarer: move speed while the local player hasn't hit or been hit for InCombatSeconds (WerewolfKit's "in combat" window).
        private const float InCombatSeconds = 10f;
        private static float _lastCombatAt = float.NegativeInfinity;

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.OnDamaged))]
        private static class CombatTaken
        {
            private static void Postfix(SEMan __instance, Character attacker) { if (attacker != null && Mine(__instance.m_character)) _lastCombatAt = Time.time; }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
        private static class CombatDealt
        {
            private static void Postfix(SEMan __instance) { if (Mine(__instance.m_character)) _lastCombatAt = Time.time; }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
        private static class WayfarerPatch
        {
            private static void Postfix(SEMan __instance, ref float speed)
            {
                float k = ParagonEffects.TX(Wayfarer);
                Character c = __instance.m_character;
                if (k <= 0f || !Mine(c) || c.IsSwimming() || c.IsRiding() || Time.time - _lastCombatAt < InCombatSeconds) return;
                speed *= 1f + k;
                Once(Wayfarer, $"x{1f + k:0.###}");
            }
        }
    }
}
