using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// The 13 FITNESS slottable Paragon stars. Live only while slotted. Local player
    /// only. Kill = vanilla's Game.RPC_RegisterKill (the local player's kill, as PassiveKit.OnLocalKill); parry = PassiveKit's test (a timed-
    /// block bonus blocker, m_blockTimer under 0.25 s); damage taken = Character.RPC_Damage on the local player (as PassiveHooks). Cuts to
    /// damage taken go through SoftCap.Asymptote (30/60) like SE_Paragon's guard.
    /// </summary>
    [ParagonEffectSet]
    internal static class FitnessSlottable
    {
        internal const string IronGuard = "+{value}% block power for 3 s after a parry";
        internal const string DeepDiver = "-{value}% swim stamina, a fifth as much swim speed";
        internal const string LastStand = "a lethal hit is negated (240 s cooldown, 2 s less per point) with a {value}% max health shield";
        internal const string Brimming = "above 90% eitr: spells cost -{value}%";
        internal const string Moonbound = "at night, kills in Bat or Wolf Form restore {value}% stamina and eitr";
        internal const string Unchained = "breaking a stun, stagger or root: +{value}% speed for 4 s, slows cleared";
        internal const string SecondWind = "dropping under 30% health restores {value}% health and stamina (90 s cooldown)";
        internal const string GaleRunner = "each dodge: 2 s windwalk, +{value}% speed, refunds half as much dodge stamina";
        internal const string Warden = "enemies within 8 m prefer you; -{value}% damage from them";
        internal const string Bloodthirst = "kills restore {value}% max health";
        internal const string ShieldWall = "perfect blocks refund {value}% of the block's stamina";
        internal const string UndyingSpite = "under 40% health: +{value}% regen, a third as much less damage taken";
        internal const string WolfsPace = "Wolf Form: +{value}% move speed, -{value}% sprint stamina";

        public static readonly string[] Formats =
        {
            IronGuard, DeepDiver, LastStand, Brimming, Moonbound, Unchained, SecondWind, GaleRunner, Warden, Bloodthirst, ShieldWall, UndyingSpite, WolfsPace,
        };

        private static float TX(string f) => ParagonEffects.TX(f);
        private static Player Me => Player.m_localPlayer;
        private static bool IsMe(Character c) => c != null && c == Player.m_localPlayer;
        private static float Cut(float raw) => FiresCore.Curves.SoftCap.Asymptote(raw, 0.30f, 0.60f);
        private static void Once(string f, string what) => CraftBatch1.Once(f, what);

        // ── kills: Bloodthirst, Moonbound Hunger
        [HarmonyPatch(typeof(Game), nameof(Game.RPC_RegisterKill))]
        private static class Kill
        {
            private static void Postfix()
            {
                Player me = Me;
                if (me == null || me.IsDead()) return;
                float b = TX(Bloodthirst);
                if (b > 0f) { me.Heal(me.GetMaxHealth() * b, true); Once(Bloodthirst, $"+{me.GetMaxHealth() * b:0.#} hp"); }
                float m = TX(Moonbound);
                if (m > 0f && EnvMan.IsNight() && (FiresRPGClasses.Combat.WerewolfKit.FormActive || FiresRPGClasses.Combat.VampireKit.BatActive))
                {
                    me.AddStamina(me.GetMaxStamina() * m);
                    me.AddEitr(me.GetMaxEitr() * m);
                    Once(Moonbound, $"{m * 100f:0.#}% stamina and eitr");
                }
            }
        }

        // ── blocks: parry -> Iron Guard window; perfect block (a parry) -> Shield Wall Master refund
        private static float _ironUntil = float.NegativeInfinity;

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class Block
        {
            private static void Prefix(Humanoid __instance, out Vector2 __state)
            {
                __state = new Vector2(-1f, 0f);
                if (!IsMe(__instance)) return;
                ItemDrop.ItemData blocker = __instance.GetCurrentBlocker();
                bool parry = blocker != null && blocker.m_shared.m_timedBlockBonus > 1f && __instance.m_blockTimer != -1f && __instance.m_blockTimer < 0.25f;
                __state = new Vector2(parry ? 1f : 0f, ((Player)__instance).GetStamina());
            }
            private static void Postfix(Humanoid __instance, bool __result, Vector2 __state)
            {
                if (!__result || __state.x < 1f || !(__instance is Player me)) return;
                if (TX(IronGuard) > 0f) { _ironUntil = Time.time + 3f; Once(IronGuard, "parry window"); }
                float s = Mathf.Clamp01(TX(ShieldWall)), used = __state.y - me.GetStamina();
                if (s > 0f && used > 0f) { me.AddStamina(used * s); Once(ShieldWall, $"refund {used * s:0.#}"); }
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float))]
        private static class IronGuardPower
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                float k = TX(IronGuard);
                Player me = Me;
                if (k <= 0f || Time.time >= _ironUntil || me == null || me.GetCurrentBlocker() != __instance) return;
                __result *= 1f + k;
            }
        }

        // ── damage taken on the local player: Last Stand (+ its shield), Warden's Challenge, Undying Spite; then Second Wind after it lands
        private static float _lastStandReadyAt, _shield, _shieldUntil, _secondWindReadyAt;

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Taken
        {
            private static void Prefix(Character __instance, HitData hit, out float __state)
            {
                __state = -1f;
                if (!IsMe(__instance) || hit == null || __instance.IsDead()) return;
                Player me = (Player)__instance;
                __state = me.GetHealth();
                // Warden's Challenge: hits from an enemy within 8 m
                float w = TX(Warden);
                Character a = hit.GetAttacker();
                if (w > 0f && a != null && a != me && Vector3.Distance(a.transform.position, me.transform.position) <= 8f) { hit.m_damage.Modify(1f - Cut(w)); Once(Warden, "damage from a near enemy"); }
                // Undying Spite: under 40% health, a third of the value off
                float u = TX(UndyingSpite);
                if (u > 0f && me.GetHealthPercentage() < 0.4f) { hit.m_damage.Modify(1f - Cut(u / 3f)); Once(UndyingSpite, "damage taken"); }
                // the Last Stand shield absorbs first
                float total = hit.GetTotalDamage();
                if (_shield > 0f && Time.time < _shieldUntil && total > 0f)
                {
                    float take = Mathf.Min(_shield, total);
                    _shield -= take;
                    hit.m_damage.Modify((total - take) / total);
                    total -= take;
                }
                // Last Stand: a hit that would kill is negated, on its cooldown
                float l = TX(LastStand);
                if (l > 0f && Time.time >= _lastStandReadyAt && total >= me.GetHealth())
                {
                    hit.m_damage.Modify(0f);
                    _lastStandReadyAt = Time.time + Mathf.Max(30f, 240f - 2f * ParagonRankOf(LastStand));
                    _shield = me.GetMaxHealth() * l;
                    _shieldUntil = Time.time + 10f;
                    me.Message(MessageHud.MessageType.Center, "Last Stand");
                    Once(LastStand, $"lethal hit negated, shield {_shield:0}");
                }
            }
            private static void Postfix(Character __instance, float __state)
            {
                if (__state < 0f || !(__instance is Player me) || me.IsDead()) return;
                float k = TX(SecondWind), max = me.GetMaxHealth();
                if (k > 0f && Time.time >= _secondWindReadyAt && __state >= max * 0.3f && me.GetHealth() < max * 0.3f)
                {
                    _secondWindReadyAt = Time.time + 90f;
                    me.Heal(max * k, true);
                    me.AddStamina(me.GetMaxStamina() * k);
                    Once(SecondWind, $"+{max * k:0} hp");
                }
            }
        }

        private static int ParagonRankOf(string format)
        {
            foreach (ParagonStarDef s in ParagonCatalog.Stars) if (s.EffectFormat == format) return ParagonUi.Rank(s.Id);
            return 0;
        }

        // ── Undying Spite regen; Deep Diver / Wolf's Pace / Gale Runner / Unchained speed; Wolf's Pace sprint drain
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
        private static class SpiteRegen
        {
            private static void Postfix(SEMan __instance, ref float regenMultiplier)
            {
                float u = TX(UndyingSpite);
                if (u > 0f && IsMe(__instance.m_character) && __instance.m_character.GetHealthPercentage() < 0.4f) regenMultiplier *= 1f + u;
            }
        }

        private static float _galeUntil = float.NegativeInfinity, _unchainedUntil = float.NegativeInfinity;

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
        private static class Speed
        {
            private static void Postfix(SEMan __instance, ref float speed)
            {
                Character c = __instance.m_character;
                if (!IsMe(c)) return;
                float m = 1f;
                if (c.IsSwimming()) m *= 1f + TX(DeepDiver) / 5f;
                if (FiresRPGClasses.Combat.WerewolfKit.FormActive) m *= 1f + TX(WolfsPace);
                if (Time.time < _galeUntil) m *= 1f + TX(GaleRunner);
                if (Time.time < _unchainedUntil) m *= 1f + TX(Unchained);
                speed *= m;
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyRunStaminaDrain))]
        private static class WolfDrain
        {
            private static void Postfix(SEMan __instance, ref float drain)
            {
                float k = Mathf.Clamp01(TX(WolfsPace));
                if (k > 0f && IsMe(__instance.m_character) && FiresRPGClasses.Combat.WerewolfKit.FormActive) { drain *= 1f - k; Once(WolfsPace, "sprint drain"); }
            }
        }

        // Deep Diver's "breath": vanilla players don't drown; their underwater limit is swim stamina, so the breath share cuts swim drain.
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifySwimStaminaUsage))]
        private static class DiverBreath
        {
            private static void Postfix(SEMan __instance, ref float staminaUse)
            {
                float k = TX(DeepDiver);
                if (k <= 0f || !IsMe(__instance.m_character)) return;
                staminaUse /= 1f + k;
                Once(DeepDiver, "swim stamina");
            }
        }

        // ── Gale Runner: a dodge starts (UpdateDodge spends its stamina): 2 s of +x% speed and x/2 of that stamina back
        [HarmonyPatch(typeof(Player), "UpdateDodge")]
        private static class Dodge
        {
            private static void Prefix(Player __instance, out Vector2 __state) => __state = IsMe(__instance) ? new Vector2(__instance.m_queuedDodgeTimer, __instance.GetStamina()) : new Vector2(-1f, 0f);
            private static void Postfix(Player __instance, Vector2 __state)
            {
                float k = TX(GaleRunner);
                if (k <= 0f || __state.x <= 0f || __instance.m_queuedDodgeTimer != 0f) return;
                float used = __state.y - __instance.GetStamina();
                if (used <= 0f) return;
                _galeUntil = Time.time + 2f;
                __instance.AddStamina(used * Mathf.Clamp01(k * 0.5f));
                Once(GaleRunner, $"dodge, refund {used * k * 0.5f:0.#}");
            }
        }

        // ── Unchained: the moment a stagger ends on the local player: 4 s of +x% speed and slowing status effects removed. (Stuns and
        // roots in this pack are staggers or speed-cutting status effects.)
        private static bool _wasStaggered;

        [HarmonyPatch(typeof(Player), "Update")]
        private static class StaggerWatch
        {
            private static void Postfix(Player __instance)
            {
                if (!IsMe(__instance)) return;
                bool now = __instance.IsStaggering();
                if (_wasStaggered && !now && TX(Unchained) > 0f)
                {
                    _unchainedUntil = Time.time + 4f;
                    SEMan se = __instance.GetSEMan();
                    var slows = new List<int>();
                    foreach (StatusEffect e in se.GetStatusEffects()) if (e is SE_Stats st && st.m_speedModifier < 0f) slows.Add(e.NameHash());
                    foreach (int h in slows) se.RemoveStatusEffect(h, true);
                    Once(Unchained, $"stagger broken, {slows.Count} slow(s) cleared");
                }
                _wasStaggered = now;
            }
        }

        // ── Brimming Well: above 90% eitr a spell (an eitr spend) costs less
        [HarmonyPatch(typeof(Player), nameof(Player.UseEitr))]
        private static class Brim
        {
            [HarmonyPriority(Priority.High)]
            private static void Prefix(Player __instance, ref float v)
            {
                float k = Mathf.Clamp(TX(Brimming), 0f, 0.8f);
                if (k <= 0f || v <= 0f || !IsMe(__instance) || __instance.GetMaxEitr() <= 0f || __instance.GetEitr() / __instance.GetMaxEitr() <= 0.9f) return;
                v *= 1f - k;
                Once(Brimming, "spell cost");
            }
        }

        // ── Warden's Challenge: every 2 s (timer check), monsters within 8 m that the local player owns (so their AI runs here) target the player.
        private static float _nextTaunt;
        private static readonly System.Reflection.FieldInfo TargetCreature = AccessTools.Field(typeof(MonsterAI), "m_targetCreature");

        [HarmonyPatch(typeof(Player), "Update")]
        private static class Taunt
        {
            private static void Postfix(Player __instance)
            {
                if (!IsMe(__instance) || Time.time < _nextTaunt) return;
                _nextTaunt = Time.time + 2f;
                if (TX(Warden) <= 0f || TargetCreature == null) return;
                int n = 0;
                foreach (BaseAI ai in BaseAI.BaseAIInstances)
                {
                    if (!(ai is MonsterAI m) || m.m_nview == null || !m.m_nview.IsOwner()) continue;
                    Character c = m.m_character;
                    if (c == null || c.IsTamed() || !BaseAI.IsEnemy(c, __instance) || Vector3.Distance(c.transform.position, __instance.transform.position) > 8f) continue;
                    TargetCreature.SetValue(m, __instance);
                    n++;
                }
                if (n > 0) Once(Warden, $"{n} enemy/enemies taunted");
            }
        }
    }
}
