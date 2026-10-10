using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.Classes;
using FiresCore.Sync;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// What an attribute point does (doc 12 §8; Fire 2026-09-29 22:4x chose the defaults in Tools\CLASS_TEST_PLAN.md §6 Q6).
    /// Until 0.1.48 attributes were bought and saved but applied nowhere. Each row is a per-point value with a cap, bound in
    /// the "Attributes" config section and synced from the server; the effects apply to the local player only (every client
    /// applies its own), through the hooks in this file and the shared ones in PassiveHooks. Preview() is the pure
    /// before -> after the Character view shows for points not yet confirmed.
    /// </summary>
    public static class AttributeEffects
    {
        private const string Section = "Attributes";

        public enum Stat
        {
            PhysicalDamage, CarryWeight,
            AttackStamina, MoveStamina, AttackSpeed,
            MagicDamage, MaxEitr, EitrRegen,
            MaxHealth, Armour, HealthRegen,
            MaxStamina, StaminaRegen, ElementalTaken,
        }

        /// <summary>One row of the table: the attribute that drives it, the default per point, the cap, and how it reads.</summary>
        private sealed class Row
        {
            public Stat Stat;
            public AttributeKind Attribute;
            public float PerPoint;
            public float Cap;
            public bool Percent;
            public string Label;
            public ConfigEntry<float> Entry;
        }

        // Negative values reduce (stamina costs, elemental damage taken); the cap bounds the magnitude.
        private static readonly Row[] Rows =
        {
            new Row { Stat = Stat.PhysicalDamage, Attribute = AttributeKind.Strength, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Physical damage" },
            new Row { Stat = Stat.CarryWeight, Attribute = AttributeKind.Strength, PerPoint = 2f, Cap = 160f, Label = "Carry weight" },
            new Row { Stat = Stat.AttackStamina, Attribute = AttributeKind.Agility, PerPoint = -0.3f, Cap = 30f, Percent = true, Label = "Attack and block stamina" },
            new Row { Stat = Stat.MoveStamina, Attribute = AttributeKind.Agility, PerPoint = -0.3f, Cap = 30f, Percent = true, Label = "Run, jump and dodge stamina" },
            new Row { Stat = Stat.AttackSpeed, Attribute = AttributeKind.Agility, PerPoint = 0.2f, Cap = 15f, Percent = true, Label = "Attack speed" },
            new Row { Stat = Stat.MagicDamage, Attribute = AttributeKind.Intellect, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Elemental and magic damage" },
            new Row { Stat = Stat.MaxEitr, Attribute = AttributeKind.Intellect, PerPoint = 1f, Cap = 80f, Label = "Eitr" },
            new Row { Stat = Stat.EitrRegen, Attribute = AttributeKind.Intellect, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Eitr regeneration" },
            new Row { Stat = Stat.MaxHealth, Attribute = AttributeKind.Endurance, PerPoint = 1f, Cap = 80f, Label = "Health" },
            new Row { Stat = Stat.Armour, Attribute = AttributeKind.Endurance, PerPoint = 0.25f, Cap = 20f, Label = "Armour" },
            new Row { Stat = Stat.HealthRegen, Attribute = AttributeKind.Endurance, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Health regeneration" },
            new Row { Stat = Stat.MaxStamina, Attribute = AttributeKind.Vigour, PerPoint = 1f, Cap = 80f, Label = "Stamina" },
            new Row { Stat = Stat.StaminaRegen, Attribute = AttributeKind.Vigour, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Stamina regeneration" },
            new Row { Stat = Stat.ElementalTaken, Attribute = AttributeKind.Vigour, PerPoint = -0.2f, Cap = 20f, Percent = true, Label = "Elemental damage taken" },
        };

        private static ConfigSync _sync;

        internal static void Bind(ConfigFile config)
        {
            foreach (Row row in Rows)
            {
                string unit = row.Percent ? "%" : "";
                row.Entry = config.Bind(Section, $"{row.Attribute} - {row.Label} per point", row.PerPoint,
                    new ConfigDescription($"{row.Label} per {row.Attribute} point, in {(row.Percent ? "percent" : "points")}; capped at "
                        + $"{(row.PerPoint < 0 ? "-" : "+")}{row.Cap}{unit}. Default {row.PerPoint}{unit} (Fire, 2026-09-29). [Synced with Server]",
                        new AcceptableValueRange<float>(-10f, 10f)));
            }
            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".attributes")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " attributes",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    MinimumRequiredVersion = "0.0.0",
                    ModRequired = false,
                };
                foreach (Row row in Rows) _sync.AddConfigEntry(row.Entry);
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"attribute config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
        }

        /// <summary>The effect of <paramref name="points"/> in the row's attribute: per point x points, capped (sign kept).</summary>
        private static float Value(Row row, int points)
        {
            float perPoint = row.Entry != null ? row.Entry.Value : row.PerPoint;
            float raw = perPoint * Math.Max(0, points);
            return Mathf.Clamp(raw, -row.Cap, row.Cap);
        }

        private static Row RowFor(Stat stat)
        {
            foreach (Row row in Rows) if (row.Stat == stat) return row;
            return null;
        }

        /// <summary>The local player's current effect for one stat (0 without a class record).</summary>
        public static float Current(Stat stat)
        {
            Row row = RowFor(stat);
            ClassProgressState progress = ClassPlayerStore.Local?.Progress;
            return row == null || progress == null ? 0f : Value(row, progress.Attribute(row.Attribute));
        }

        /// <summary>A multiplier for a percent stat: +10 % -> 1.10, -6 % -> 0.94.</summary>
        public static float Factor(Stat stat) => 1f + Current(stat) / 100f;

        private static bool IsLocal(Character character) => character != null && character == Player.m_localPlayer;

        public struct StatPreview
        {
            public Stat Stat;
            public AttributeKind Attribute;
            public string Label;
            public float Before;
            public float After;
            public bool Percent;

            /// <summary>"+10 % -> +12 %" or "+20 -> +24".</summary>
            public string Text => $"{Format(Before)} -> {Format(After)}";

            private string Format(float value) => (value >= 0f ? "+" : "") + (Percent ? $"{value:0.#} %" : $"{value:0.##}");
        }

        /// <summary>
        /// Pure before -> after for the Character view ([classes-ui]): every stat a pending attribute point would change, from
        /// <paramref name="progress"/>'s bought points to those plus <paramref name="pending"/>. Nothing is applied.
        /// </summary>
        public static List<StatPreview> Preview(ClassProgressState progress, IReadOnlyDictionary<AttributeKind, int> pending)
        {
            var result = new List<StatPreview>();
            if (progress == null) return result;
            foreach (Row row in Rows)
            {
                int now = progress.Attribute(row.Attribute);
                int added = pending != null && pending.TryGetValue(row.Attribute, out int more) ? more : 0;
                result.Add(new StatPreview
                {
                    Stat = row.Stat, Attribute = row.Attribute, Label = row.Label, Percent = row.Percent,
                    Before = Value(row, now), After = Value(row, now + added),
                });
            }
            return result;
        }

        // ---- applying ------------------------------------------------------------------------------------------------------

        /// <summary>From ClassDamageHooks: the local player's outgoing hit, before it is sent.</summary>
        internal static void OnLocalHitSending(HitData hit)
        {
            if (hit == null || !IsLocal(hit.GetAttacker())) return;
            float physical = Factor(Stat.PhysicalDamage), magic = Factor(Stat.MagicDamage);
            hit.m_damage.m_blunt *= physical;
            hit.m_damage.m_slash *= physical;
            hit.m_damage.m_pierce *= physical;
            hit.m_damage.m_chop *= physical;
            hit.m_damage.m_pickaxe *= physical;
            hit.m_damage.m_fire *= magic;
            hit.m_damage.m_frost *= magic;
            hit.m_damage.m_lightning *= magic;
            hit.m_damage.m_poison *= magic;
            hit.m_damage.m_spirit *= magic;
        }

        /// <summary>From ClassDamageTakenHooks: elemental damage the local player takes, on its own peer.</summary>
        internal static void OnLocalHitTaken(Character target, HitData hit)
        {
            if (hit == null || !IsLocal(target)) return;
            float taken = Factor(Stat.ElementalTaken);
            hit.m_damage.m_fire *= taken;
            hit.m_damage.m_frost *= taken;
            hit.m_damage.m_lightning *= taken;
            hit.m_damage.m_poison *= taken;
        }

        /// <summary>From ClassStaminaCostHooks: attacks/blocks and movement both take Agility's cut (the same rate by default).</summary>
        internal static float StaminaCostFactor(Player player) =>
            !IsLocal(player) ? 1f : player.InAttack() || player.IsBlocking() ? Factor(Stat.AttackStamina) : Factor(Stat.MoveStamina);

        internal static float StaminaRegenFactor(Character character) => IsLocal(character) ? Factor(Stat.StaminaRegen) : 1f;

        internal static float EitrRegenFactor(Character character) => IsLocal(character) ? Factor(Stat.EitrRegen) : 1f;

        // ---- the hooks on methods nothing else in RPGClasses patches (the family's one-hook rule) --------------------------

        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        private static class FoodPools
        {
            [HarmonyPostfix]
            private static void AddPools(Player __instance, ref float hp, ref float stamina, ref float eitr)
            {
                if (!IsLocal(__instance)) return;
                hp += Current(Stat.MaxHealth);
                stamina += Current(Stat.MaxStamina);
                eitr += Current(Stat.MaxEitr);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        private static class Carry
        {
            [HarmonyPostfix]
            private static void AddCarry(Player __instance, ref float __result)
            {
                if (IsLocal(__instance)) __result += Current(Stat.CarryWeight);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
        private static class Armour
        {
            [HarmonyPostfix]
            private static void AddArmour(Player __instance, ref float __result)
            {
                if (IsLocal(__instance)) __result += Current(Stat.Armour);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
        private static class HealthRegen
        {
            [HarmonyPostfix]
            private static void Scale(SEMan __instance, ref float regenMultiplier)
            {
                if (IsLocal(__instance.m_character)) regenMultiplier *= Factor(Stat.HealthRegen);
            }
        }

        // Attack speed: an attack clip sets its animator speed through CharacterAnimEvent.Speed events, and vanilla only resets
        // it to 1 outside actions. While the local player attacks, keep the clip's speed times Agility's factor.
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
        private static class AttackClipSpeed
        {
            internal static float ClipSpeed = 1f;

            [HarmonyPrefix]
            private static void Remember(Character ___m_character, float speedScale)
            {
                if (IsLocal(___m_character)) ClipSpeed = speedScale;
            }
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        private static class AttackSpeed
        {
            [HarmonyPostfix]
            private static void Apply(Character ___m_character, Animator ___m_animator)
            {
                if (!IsLocal(___m_character) || ___m_animator == null) return;
                if (!___m_character.InAttack()) { AttackClipSpeed.ClipSpeed = 1f; return; }
                if (___m_animator.speed < 0.01f) return; // a hit's freeze frame
                float factor = Factor(Stat.AttackSpeed);
                if (factor != 1f) ___m_animator.speed = AttackClipSpeed.ClipSpeed * factor;
            }
        }
    }
}
