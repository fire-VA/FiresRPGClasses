using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// CRAFT slottable Paragon stars, part 2 - the last four Craft stars. Live only while
    /// slotted. Local player only.
    /// </summary>
    [ParagonEffectSet]
    internal static class CraftSlottable2
    {
        internal const string Pathfinder = "on paths: +{value}% speed, no run stamina";
        internal const string VeinSense = "ore veins within 10 + {value} m chime and pin";
        internal const string WindCaller = "wind follows your heading for 60 s (600 s cooldown, {value} s less)";
        internal const string Gemcutter = "{value}% chance socketing keeps the gem";

        public static readonly string[] Formats = { Pathfinder, VeinSense, WindCaller, Gemcutter };

        private static void Once(string f, string what) => CraftBatch1.Once(f, what);

        // ── Pathfinder: on dirt or paved terrain paint (hoe / pickaxe paths, roads): +x% move speed, and running there costs no stamina
        // ("walk stamina": vanilla walking is free, so the drain that goes is the run drain on the path).
        internal static bool OnPath(Player me)
        {
            if (me == null || !me.IsOnGround() || me.IsSwimming() || me.IsRiding()) return false;
            Vector3 p = me.transform.position;
            Heightmap h = Heightmap.FindHeightmap(p);
            if (h == null) return false;
            Color c = h.GetPaintMask(p);
            return c.r > 0.5f && c.g < 0.5f || c.b > 0.5f;   // dirt (red) or paved (blue); not cultivated (green)
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
        private static class PathSpeed
        {
            private static void Postfix(SEMan __instance, ref float speed)
            {
                float k = ParagonEffects.TX(Pathfinder);
                if (k <= 0f || __instance.m_character != Player.m_localPlayer || !OnPath(Player.m_localPlayer)) return;
                speed *= 1f + k;
                Once(Pathfinder, "speed on a path");
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyRunStaminaDrain))]
        private static class PathStamina
        {
            private static void Postfix(SEMan __instance, ref float drain)
            {
                if (ParagonEffects.TX(Pathfinder) <= 0f || __instance.m_character != Player.m_localPlayer || !OnPath(Player.m_localPlayer)) return;
                drain = 0f;
                Once(Pathfinder, "no run drain on a path");
            }
        }

        // ── Vein Sense: every 2 s (a timer check, no scene scan per frame) ore rocks within 10 + value m get a map pin (not saved) and one
        // chime message, once per rock.
        private static float _nextScan;
        private static readonly HashSet<int> _pinned = new HashSet<int>();
        private static readonly Collider[] _hits = new Collider[256];

        private static bool IsOre(DropTable t)
        {
            if (t?.m_drops == null) return false;
            foreach (var d in t.m_drops) if (d.m_item != null && d.m_item.name.IndexOf("Ore", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        [HarmonyPatch(typeof(Player), "Update")]
        private static class VeinScan
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || Time.time < _nextScan) return;
                _nextScan = Time.time + 2f;
                float v = ParagonEffects.TX(VeinSense);
                if (v <= 0f || Minimap.instance == null) return;
                float r = 10f + v * 100f;
                int n = Physics.OverlapSphereNonAlloc(__instance.transform.position, r, _hits);
                int found = 0;
                for (int i = 0; i < n; i++)
                {
                    var c = _hits[i]; if (c == null) continue;
                    Component rock = (Component)c.GetComponentInParent<MineRock5>() ?? c.GetComponentInParent<MineRock>();
                    if (rock == null || !_pinned.Add(rock.GetInstanceID())) continue;
                    DropTable t = rock is MineRock5 m5 ? m5.m_dropItems : ((MineRock)rock).m_dropItems;
                    if (!IsOre(t)) continue;
                    Minimap.instance.AddPin(rock.transform.position, Minimap.PinType.Icon3, "Ore", false, false);
                    found++;
                }
                if (found > 0)
                {
                    __instance.Message(MessageHud.MessageType.TopLeft, found == 1 ? "Vein Sense: an ore vein nearby" : $"Vein Sense: {found} ore veins nearby");
                    Once(VeinSense, $"{found} vein(s) within {r:0} m");
                }
            }
        }

        // ── Wind Caller: when the local player takes a ship's rudder and it's ready, the wind (on this client, which simulates the
        // steered ship) blows along the ship's heading for 60 s; then 600 - value s of cooldown.
        private static float _windUntil = float.NegativeInfinity, _windReadyAt;

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindDir))]
        private static class WindDir
        {
            private static void Postfix(ref Vector3 __result)
            {
                float v = ParagonEffects.TX(WindCaller);
                Player me = Player.m_localPlayer;
                Ship ship = v > 0f && me != null ? me.GetControlledShip() : null;
                if (ship == null) return;
                if (Time.time >= _windUntil && Time.time >= _windReadyAt)
                {
                    _windUntil = Time.time + 60f;
                    _windReadyAt = _windUntil + Mathf.Max(60f, 600f - v * 100f);
                    me.Message(MessageHud.MessageType.TopLeft, "Wind Caller: the wind follows your heading");
                    Once(WindCaller, $"cooldown {600f - v * 100f:0} s");
                }
                if (Time.time < _windUntil) __result = ship.transform.forward;
            }
        }

        // ── Gemcutter: FLO 0.1.28+ FloHooks.KeepGemChance (add-only); FLO calls it once per successful socket on the socketing
        // player's client and puts one gem back when it returns true. Set by reflection once; an older FLO has no field = nothing.
        private static bool _gemHooked;

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class GemHook
        {
            private static void Postfix(Player __instance)
            {
                if (_gemHooked || __instance != Player.m_localPlayer) return;
                _gemHooked = true;
                try
                {
                    var f = (Type.GetType("FiresLootOverhaul.FloHooks, FiresLootOverhaul", false) ?? Type.GetType("FiresLootOverhaul.Api.FloHooks, FiresLootOverhaul", false))?.GetField("KeepGemChance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (f == null || f.FieldType != typeof(Func<Player, ItemDrop.ItemData, bool>)) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] Gemcutter: FLO has no FloHooks.KeepGemChance (needs FLO 0.1.28+)"); return; }
                    f.SetValue(null, (Func<Player, ItemDrop.ItemData, bool>)((p, gem) =>
                    {
                        if (p == null || p != Player.m_localPlayer || !CraftBatch1.Roll(Gemcutter)) return false;
                        Once(Gemcutter, gem?.m_shared?.m_name ?? "gem");
                        return true;
                    }));
                    FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] Gemcutter: hooked FLO FloHooks.KeepGemChance");
                }
                catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] Gemcutter hook: " + e.Message); }
            }
        }
    }
}
