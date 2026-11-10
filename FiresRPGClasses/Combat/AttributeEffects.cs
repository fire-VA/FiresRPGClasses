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
    /// What an attribute point does (design doc 12 §8). Each row is a per-point value with a cap, bound in
    /// the "Attributes" config section and synced from the server; the effects apply to the local player only (every client
    /// applies its own), through the hooks in this file and the shared ones in PassiveHooks. Preview() is the pure
    /// before -> after the Character view shows for points not yet confirmed.
    /// </summary>
    public static class AttributeEffects
    {
        private const string Section = "7 - Attributes";

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
            public float Cap;      // the knee of SoftCap's curve (the old hard cap), not a ceiling
            public float Limit;    // > 0: a true asymptote (SoftCap.Asymptote) - reductions that must never reach immunity / free
            public bool Percent;
            public string Label;
            public ConfigEntry<float> Entry, KneeEntry, LimitEntry;
            public float LastLogged = float.NaN;   // the effective value last logged (Debug, once per change)
        }

        // Negative values reduce (stamina costs, elemental damage taken). Cap is the knee of the soft curve
        // (SoftCap.Soft, rising forever); rows with a Limit approach it asymptotically instead (SoftCap.Asymptote).
        private static readonly Row[] Rows =
        {
            new Row { Stat = Stat.PhysicalDamage, Attribute = AttributeKind.Strength, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Physical damage" },
            new Row { Stat = Stat.CarryWeight, Attribute = AttributeKind.Strength, PerPoint = 2f, Cap = 160f, Label = "Carry weight" },
            new Row { Stat = Stat.AttackStamina, Attribute = AttributeKind.Agility, PerPoint = -0.3f, Cap = 30f, Limit = 80f, Percent = true, Label = "Attack and block stamina" },
            new Row { Stat = Stat.MoveStamina, Attribute = AttributeKind.Agility, PerPoint = -0.3f, Cap = 30f, Limit = 80f, Percent = true, Label = "Run, jump and dodge stamina" },
            new Row { Stat = Stat.AttackSpeed, Attribute = AttributeKind.Agility, PerPoint = 0.2f, Cap = 15f, Percent = true, Label = "Attack speed" },
            new Row { Stat = Stat.MagicDamage, Attribute = AttributeKind.Intellect, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Elemental and magic damage" },
            new Row { Stat = Stat.MaxEitr, Attribute = AttributeKind.Intellect, PerPoint = 1f, Cap = 80f, Label = "Eitr" },
            new Row { Stat = Stat.EitrRegen, Attribute = AttributeKind.Intellect, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Eitr regeneration" },
            new Row { Stat = Stat.MaxHealth, Attribute = AttributeKind.Endurance, PerPoint = 1f, Cap = 80f, Label = "Health" },
            new Row { Stat = Stat.Armour, Attribute = AttributeKind.Endurance, PerPoint = 0.25f, Cap = 20f, Label = "Armour" },
            new Row { Stat = Stat.HealthRegen, Attribute = AttributeKind.Endurance, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Health regeneration" },
            new Row { Stat = Stat.MaxStamina, Attribute = AttributeKind.Vigour, PerPoint = 1f, Cap = 80f, Label = "Stamina" },
            new Row { Stat = Stat.StaminaRegen, Attribute = AttributeKind.Vigour, PerPoint = 0.5f, Cap = 40f, Percent = true, Label = "Stamina regeneration" },
            new Row { Stat = Stat.ElementalTaken, Attribute = AttributeKind.Vigour, PerPoint = -0.2f, Cap = 20f, Limit = 75f, Percent = true, Label = "Elemental damage taken" },
        };

        private static ConfigSync _sync;

        internal static void Bind(ConfigFile config)
        {
            foreach (Row row in Rows)
            {
                string unit = row.Percent ? "%" : "";
                row.Entry = config.Bind(Section, $"{row.Attribute} - {row.Label} per point", row.PerPoint,
                    new ConfigDescription($"{row.Label} gained per {row.Attribute} point, in {(row.Percent ? "percent" : "points")}. [Synced with Server]",
                        new AcceptableValueRange<float>(-10f, 10f), new ConfigurationManagerAttributes { IsAdvanced = true }));
                string tail = row.Limit > 0f
                    ? "past it, gains shrink toward the limit"
                    : "past it, gains shrink but never stop";
                row.KneeEntry = config.Bind(Section, $"{row.Attribute} - {row.Label} knee", row.Cap,
                    new ConfigDescription($"{row.Label}: full value up to this total{unit}; " + tail + ". [Synced with Server]",
                        new AcceptableValueRange<float>(0f, 100000f), new ConfigurationManagerAttributes { IsAdvanced = true }));
                if (row.Limit > 0f)
                    row.LimitEntry = config.Bind(Section, $"{row.Attribute} - {row.Label} limit", row.Limit,
                        new ConfigDescription($"{row.Label}: the total{unit} can approach but never reach this. [Synced with Server]",
                            new AcceptableValueRange<float>(1f, 99f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            }
            BalanceConfig.Bind(config);   // [13 - Balance]: Fury, Monk combo, summon limits, cooldown reduction
            try
            {
                _sync = new ConfigSync(FiresRPGClassesPlugin.PluginGUID + ".attributes")
                {
                    DisplayName = FiresRPGClassesPlugin.PluginName + " attributes",
                    CurrentVersion = FiresRPGClassesPlugin.PluginVersion,
                    // A fixed constant, raised only when a synced key's name/type/meaning changes (last: caps became soft-curve
                    // knees and the knee/limit keys were added), so older clients can't sync these keys.
                    MinimumRequiredVersion = "0.1.131",
                    ModRequired = false,
                };
                foreach (Row row in Rows)
                {
                    _sync.AddConfigEntry(row.Entry);
                    _sync.AddConfigEntry(row.KneeEntry);
                    if (row.LimitEntry != null) _sync.AddConfigEntry(row.LimitEntry);
                }
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning($"attribute config not synced ({e.GetType().Name}: {e.Message}); local values apply"); }
        }

        /// <summary>
        /// The effect of <paramref name="points"/> in the row's attribute: per point x points through SoftCap (sign kept):
        /// linear below the knee, diminishing above it (asymptotic for reductions).
        /// </summary>
        private static float Value(Row row, int points) => Effective(row, Raw(row, points));

        private static float Raw(Row row, int points) => (row.Entry != null ? row.Entry.Value : row.PerPoint) * Math.Max(0, points);

        private static float Knee(Row row) => row.KneeEntry != null ? row.KneeEntry.Value : row.Cap;

        private static float Effective(Row row, float raw)
        {
            if (row.Limit <= 0f) return SoftCap.Soft(raw, Knee(row));
            return SoftCap.Asymptote(raw, Knee(row), row.LimitEntry != null ? row.LimitEntry.Value : row.Limit);
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
            if (row == null || progress == null) return 0f;
            float raw = Raw(row, progress.Attribute(row.Attribute));
            float effective = Effective(row, raw);
            if (effective != row.LastLogged)   // a float compare per call; the line is built only when the value changes
            {
                row.LastLogged = effective;
                string unit = row.Percent ? "%" : "";
                FiresRPGClassesPlugin.Log?.LogDebug($"[Attributes] {row.Label.ToLowerInvariant()} raw {raw:+0.##;-0.##;0}{unit} -> {effective:+0.##;-0.##;0}{unit} "
                    + $"(knee {Knee(row):0.##}{unit}{(row.Limit > 0f ? $", limit {(row.LimitEntry != null ? row.LimitEntry.Value : row.Limit):0.##}{unit}" : "")})");
            }
            return effective;
        }

        /// <summary>For ClassStatsApi rows: the attribute that drives a stat, and whether the stat reads in percent.</summary>
        internal static bool RowInfo(Stat stat, out AttributeKind attribute, out bool percent)
        {
            Row row = RowFor(stat);
            attribute = row != null ? row.Attribute : default; percent = row != null && row.Percent;
            return row != null;
        }

        /// <summary>A multiplier for a percent stat: +10 % -> 1.10, -6 % -> 0.94.</summary>
        public static float Factor(Stat stat) => 1f + Current(stat) / 100f;

        private static bool IsLocal(Character character) => character != null && character == Player.m_localPlayer;

        // 0.1.214 ([lead] / [ghost], step 3: the god clone's stats): the same rows for any body - the local player through Current(stat)
        // above (unchanged), a registered class body (ClassBodies) through its own record
        private static bool IsClassBody(Character character) => IsLocal(character) || ClassBodies.IsRegistered(character);

        /// <summary>This body's current effect for one stat: the local player's = <see cref="Current(Stat)"/>; a registered class body's from its record; else 0.</summary>
        public static float Current(Stat stat, Character body)
        {
            if (IsLocal(body)) return Current(stat);
            ClassProgressState progress = ClassBodies.Record(body)?.Progress;
            Row row = RowFor(stat);
            return row == null || progress == null ? 0f : Effective(row, Raw(row, progress.Attribute(row.Attribute)));
        }

        public static float Factor(Stat stat, Character body) => 1f + Current(stat, body) / 100f;

        // one Debug line per (re)read record of a class body: every stat it gets, the proof the clone's numbers match the record
        internal static void LogBody(Character body, ClassPlayerData data)
        {
            if (data == null || FiresRPGClassesPlugin.Log == null) return;
            var parts = new List<string>();
            foreach (Row row in Rows)
            {
                float v = Effective(row, Raw(row, data.Progress.Attribute(row.Attribute)));
                if (v != 0f) parts.Add($"{row.Label.ToLowerInvariant()} {v:+0.##;-0.##}{(row.Percent ? "%" : "")}");
            }
            FiresRPGClassesPlugin.Log.LogDebug($"[ClassStats] {body.m_name}: {(parts.Count > 0 ? string.Join(", ", parts) : "no attribute effects")}");
        }

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
        /// Pure before -> after for the Character view: every stat a pending attribute point would change, from
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
            Character attacker = hit?.GetAttacker();
            if (hit == null || !IsClassBody(attacker)) return;
            float physical = Factor(Stat.PhysicalDamage, attacker), magic = Factor(Stat.MagicDamage, attacker);
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
            if (hit == null || !IsClassBody(target)) return;
            float taken = Factor(Stat.ElementalTaken, target);
            hit.m_damage.m_fire *= taken;
            hit.m_damage.m_frost *= taken;
            hit.m_damage.m_lightning *= taken;
            hit.m_damage.m_poison *= taken;
        }

        /// <summary>From ClassStaminaCostHooks: attacks/blocks and movement both take Agility's cut (the same rate by default).</summary>
        internal static float StaminaCostFactor(Player player) =>
            !IsLocal(player) ? 1f : player.InAttack() || player.IsBlocking() ? Factor(Stat.AttackStamina) : Factor(Stat.MoveStamina);

        internal static float StaminaRegenFactor(Character character) => IsClassBody(character) ? Factor(Stat.StaminaRegen, character) : 1f;

        internal static float EitrRegenFactor(Character character) => IsClassBody(character) ? Factor(Stat.EitrRegen, character) : 1f;

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
                ExtraSkills.ExtraSkillSystem.AddFoodPools(__instance, ref hp, ref stamina, ref eitr); // + Vigor, Agility, Intellect skills
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        private static class Carry
        {
            [HarmonyPostfix]
            private static void AddCarry(Player __instance, ref float __result)
            {
                if (IsLocal(__instance)) __result += Current(Stat.CarryWeight);
                ExtraSkills.ExtraSkillSystem.AddCarry(__instance, ref __result); // + Hoarder skill
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
        private static class Armour
        {
            [HarmonyPostfix]
            private static void AddArmour(Player __instance, ref float __result)
            {
                if (IsLocal(__instance)) __result = (__result + Current(Stat.Armour)) * PassiveKit2.ArmorFactor(__instance); // + Thick Hide
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
        private static class HealthRegen
        {
            [HarmonyPostfix]
            private static void Scale(SEMan __instance, ref float regenMultiplier)
            {
                if (IsClassBody(__instance.m_character)) regenMultiplier *= Factor(Stat.HealthRegen, __instance.m_character);
            }
        }

        // Attack speed: an attack clip sets its animator speed through CharacterAnimEvent.Speed events, and vanilla only resets
        // it to 1 outside actions. While the local player attacks, keep the clip's speed times Agility's factor.
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
        private static class AttackClipSpeed
        {
            internal static float ClipSpeed = 1f;
            // 0.1.214: a registered class body keeps its own clip speed (the local player keeps the field above)
            internal static readonly Dictionary<Character, float> BodyClipSpeed = new Dictionary<Character, float>();

            [HarmonyPrefix]
            private static void Remember(Character ___m_character, float speedScale)
            {
                if (IsLocal(___m_character)) ClipSpeed = speedScale;
                else if (ClassBodies.IsRegistered(___m_character)) BodyClipSpeed[___m_character] = speedScale;
            }
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        private static class AttackSpeed
        {
            [HarmonyPostfix]
            private static void Apply(Character ___m_character, Animator ___m_animator)
            {
                if (___m_animator == null) return;
                if (!IsLocal(___m_character))
                {
                    // a registered class body: its own Agility, its own clip speed (the kits' factors stay local-player for now - step 3b)
                    if (!ClassBodies.IsRegistered(___m_character)) return;
                    if (!___m_character.InAttack()) { AttackClipSpeed.BodyClipSpeed[___m_character] = 1f; return; }
                    if (___m_animator.speed < 0.01f) return;
                    float bodyFactor = Factor(Stat.AttackSpeed, ___m_character);
                    if (bodyFactor != 1f) ___m_animator.speed = (AttackClipSpeed.BodyClipSpeed.TryGetValue(___m_character, out float clip) ? clip : 1f) * bodyFactor;
                    return;
                }
                if (!___m_character.InAttack()) { AttackClipSpeed.ClipSpeed = 1f; return; }
                if (___m_animator.speed < 0.01f) return; // a hit's freeze frame
                float factor = Factor(Stat.AttackSpeed) * BuffKit.AttackSpeedFactor(___m_character) * ActiveKit.AttackSpeedFactor(___m_character) * PassiveKit2.AttackSpeedFactor(___m_character) * WerewolfKit.AttackSpeedFactor(___m_character) * VampireKit.AttackSpeedFactor(___m_character); // + Battle Trance, Bloodbath, Zeal, claws, Blood Frenzy
                if (factor != 1f) ___m_animator.speed = AttackClipSpeed.ClipSpeed * factor;
            }
        }
    }
}
