using System;
using System.Reflection;
using BepInEx.Configuration;
using FiresCore.Animation;
using FiresCore.Movement;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Class and race SECOND-JUMP styles and race DODGES, unlocked by play. Plugs into Core's movement contract
    /// (FiresCore.Movement.MovementGates / MovementStyles):
    /// Core owns the double jump itself (its key, Jump-skill gate, stamina, stand-down for Pix.Movement, refusals); this adds
    /// - gates: the double jump also unlocks at CLASS level N ("[14 - Movement] Second jump at class level", 10), for the active class;
    /// - styles for "second_jump_style" (priority: race 200 > class 100): Rogue / Monk flip (back, or front while moving forward),
    ///   Mage blink (3 m up + forward, raycast-safe, smoke at both ends, eitr), Ranger vault, Berserker heavy leap, Healer / Druid /
    ///   Necromancer soft float; Vampire bat-hop glide; Werewolf pounce leap. Tank / Paladin keep Core's plain second jump.
    /// - race dodges at "Race dodge at level" (20), ahead of the class evasion: Vampire Mist-step (5 m dash in a red mist, eitr),
    ///   Werewolf Pounce (a forward lunge, stamina). Off = the class evasion / vanilla roll, unchanged.
    /// All behind "[14 - Movement] Class movement styles" (default false: new behaviour ships off). RPGClasses registers with Core only
    /// when it's on, so Core's Auto keys stay off without it. Every use logs a point-of-use line. Clips: Core's ClipSlots when present
    /// (the flip / vault ids below; the Quaternius clips get a bundle in a later build), else a vanilla jump; FX via AbilityRPCManager.
    /// </summary>
    public static partial class ClassMovement   // 0.1.202: partial (+ ClassMovement.Npc.cs: companions use the class second-jump styles)
    {
        public const string Section = "14 - Movement";
        private static ConfigEntry<bool> _enabled, _earlyOut;
        private static ConfigEntry<int> _jumpAtLevel, _dodgeAtLevel;
        private static ConfigEntry<float> _blinkEitr, _mistEitr, _pounceStamina;
        private static bool _registered;

        public static bool On => _enabled != null && _enabled.Value;

        private static ConfigDescription Adv(string text) =>
            new ConfigDescription(text, null, new FiresCore.Sync.ConfigurationManagerAttributes { IsAdvanced = true });

        public static void Bind(ConfigFile cfg)
        {
            _enabled = cfg.Bind(Section, "Class movement styles", false,
                "Class and race styles for the double jump and dodge (needs Fires Unified Core's double jump).");
            _jumpAtLevel = cfg.Bind(Section, "Second jump at class level", 10, Adv("Class level that unlocks the double jump and its class style."));
            _dodgeAtLevel = cfg.Bind(Section, "Race dodge at level", 20, Adv("Character level that unlocks the race dodge."));
            _blinkEitr = cfg.Bind(Section, "Mage blink jump eitr", 8f, Adv("Eitr the mage's blink jump costs."));
            _mistEitr = cfg.Bind(Section, "Mist-step eitr", 12f, Adv("Eitr the vampire's mist-step dodge costs."));
            _pounceStamina = cfg.Bind(Section, "Pounce stamina", 18f, Adv("Stamina the werewolf's pounce dodge costs."));
            _earlyOut = cfg.Bind(Section, "Slow fall early-out", true, Adv("Perf: the slow fall check returns at once for every character while no float window is open (it runs for every loaded character each physics step). Off = the old per-call checks."));
            _enabled.SettingChanged += (_, __) => Register();
        }

        /// <summary>Client start-up (and when the key turns on): the gates + styles go to Core once.</summary>
        public static void Register()
        {
            if (!On || _registered) return;
            try
            {
                _registered = true;
                MovementClips.RegisterAll();   // the clips only when this key is on (new = OFF: no consumer, Core's Clip slots stay Auto-off)
                const string me = "FiresRPGClasses";
                MovementGates.Register(MovementGates.DoubleJump, "rpg.class_level", p => Level(p) >= _jumpAtLevel.Value, "Double jump (class level " + _jumpAtLevel.Value + ")", me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.vampire.bathop", 200, p => Race(p) == "Vampire" && Level(p) >= _jumpAtLevel.Value, BatHop, me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.werewolf.pounce", 200, p => Race(p) == "Werewolf" && Level(p) >= _jumpAtLevel.Value, PounceJump, me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.flip", 100, p => Is(p, ArchetypeClass.Rogue, ArchetypeClass.Monk), Flip, me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.mage.blink", 100, p => Is(p, ArchetypeClass.Mage), BlinkJump, me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.ranger.vault", 100, p => Is(p, ArchetypeClass.Ranger), Vault, me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.berserker.leap", 100, p => Is(p, ArchetypeClass.Berserker), HeavyLeap, me);
                MovementStyles.Register(MovementGates.SecondJumpStyle, "rpg.float", 100, p => Is(p, ArchetypeClass.Healer, ArchetypeClass.Druid, ArchetypeClass.Necromancer), Float, me);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] RPGClasses registered: double-jump gate at class level {_jumpAtLevel.Value}; 7 second-jump styles; race dodges at level {_dodgeAtLevel.Value}");
            }
            catch (Exception e)
            {
                _registered = false;
                FiresRPGClassesPlugin.Log?.LogWarning("[Movement] RPGClasses could not register with Core's movement (Core older than the movement contract?): " + e.GetBaseException().Message);
            }
        }

        // ── who the player is ────────────────────────────────────────────────────────────────────────────────────────────
        private static int Level(Player p) => p == Player.m_localPlayer ? ClassPlayerStore.Local?.Progress?.Level ?? 0 : 0;
        private static string Race(Player p) => p == Player.m_localPlayer ? VcsBridge.GetRace(p) ?? "" : "";
        private static bool Is(Player p, params ArchetypeClass[] any)
        {
            var data = p == Player.m_localPlayer ? ClassPlayerStore.Local : null;
            if (data == null || data.Progress.Level < _jumpAtLevel.Value) return false;
            return Array.IndexOf(any, data.Active.Archetype) >= 0;
        }

        // ── physics helpers (the local player's own body; vanilla syncs its position) ────────────────────────────────────
        private static readonly FieldInfo BodyField = AccessTools.Field(typeof(Character), "m_body");
        private static Rigidbody Body(Player p) => BodyField?.GetValue(p) as Rigidbody;
        private static float Size(Player p) => Mathf.Clamp(p.transform.localScale.y, 0.25f, 4f);

        // every caller is the local player (On/Is gates); off the halo ring Ring.Flat is exactly v.y = 0
        private static Vector3 RingPos() => Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
        private static Vector3 Flat(Vector3 v) { v = Ring.Flat(v, RingPos()); return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero; }

        private static bool Impulse(Player p, float up, float forward, string id, string clip)
        {
            var rb = Body(p);
            if (rb == null) return false;
            Vector3 fwd = Flat(p.GetMoveDir()); if (fwd == Vector3.zero) fwd = Flat(p.transform.forward);
            Vector3 v = rb.linearVelocity;
            float vUp;
            if (!Ring.InField(p.transform.position))
            {
                v.y = Mathf.Max(v.y, 0f) + p.m_jumpForce * up * Size(p);
                vUp = v.y;
            }
            else
            {
                Vector3 ru = Ring.Up(p.transform.position);
                float along = Vector3.Dot(v, ru);
                vUp = Mathf.Max(along, 0f) + p.m_jumpForce * up * Size(p);
                v = v - ru * along + ru * vUp;
            }
            v += fwd * forward * Size(p);
            rb.linearVelocity = v;
            bool clipOk = ClipSlots.Play(p, clip);
            if (!clipOk) p.m_zanim?.SetTrigger("jump");
            FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] second jump {id} by {p.GetPlayerName()}: up {vUp:0.0}, forward {forward * Size(p):0.0}, anim {(clipOk ? clip : "vanilla jump")}");
            return true;
        }

        // ── second-jump styles ───────────────────────────────────────────────────────────────────────────────────────────
        private static bool Flip(Player p)
        {
            bool front = Vector3.Dot(Flat(p.GetMoveDir()), Flat(p.transform.forward)) > 0.5f;   // forward held = front flip, else back
            if (!front)
            {
                var rb = Body(p);
                if (rb != null) rb.linearVelocity += -Flat(p.transform.forward) * 2.5f * Size(p);
            }
            return Impulse(p, 0.9f, front ? 2.5f : 0f, front ? "rpg.flip (front)" : "rpg.flip (back)", front ? "rpgclasses.flip.front" : "rpgclasses.flip.back");
        }

        private static bool Vault(Player p) => Impulse(p, 0.8f, 3f, "rpg.ranger.vault", "rpgclasses.vault");
        private static bool HeavyLeap(Player p) => Impulse(p, 0.7f, 4.5f, "rpg.berserker.leap", "rpgclasses.leap");
        private static bool PounceJump(Player p) => Impulse(p, 0.75f, 6f, "rpg.werewolf.pounce", "rpgclasses.pounce");

        private static float _floatUntil;
        private static bool Float(Player p)
        {
            _floatUntil = Time.time + 1.2f;
            return Impulse(p, 0.6f, 0f, "rpg.float (slow fall 1.2 s)", "rpgclasses.float");
        }

        private static bool BatHop(Player p)
        {
            _floatUntil = Time.time + 1.8f;
            AbilityRPCManager.SpawnFX(p.transform.position + Ring.Up(p.transform.position), "vfx_ghost_hit", 0.8f);
            return Impulse(p, 0.7f, 2.5f, "rpg.vampire.bathop (glide 1.8 s)", "rpgclasses.bathop");
        }

        // Mage: a short blink up and forward, never into terrain / walls / ceilings (a capsule cast along the path), smoke at both ends.
        private static bool BlinkJump(Player p)
        {
            float cost = _blinkEitr.Value;
            if (!p.HaveEitr(cost)) { FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] second jump rpg.mage.blink by {p.GetPlayerName()}: refused (eitr {p.GetEitr():0} < {cost:0}); Core's plain jump instead"); return false; }
            Vector3 fwd = Flat(p.GetMoveDir()); if (fwd == Vector3.zero) fwd = Flat(p.transform.forward);
            Vector3 from = p.transform.position;
            Vector3 dir = (fwd * 2.5f + Ring.Up(from) * 1.6f).normalized;
            float want = 3f * Size(p);
            if (!SafeBlink(p, from, dir, want, out Vector3 to, out string why))
            { FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] second jump rpg.mage.blink by {p.GetPlayerName()}: refused ({why}); Core's plain jump instead"); return false; }
            p.UseEitr(cost);
            AbilityRPCManager.SpawnFX(from + Ring.Up(from), "vfx_ghost_death", 1f);
            p.transform.position = to;
            var rb = Body(p);
            if (rb != null)
            {
                rb.position = to; var v = rb.linearVelocity;
                if (!Ring.InField(to)) v.y = Mathf.Max(v.y, 0f);
                else { Vector3 ru = Ring.Up(to); float along = Vector3.Dot(v, ru); if (along < 0f) v -= ru * along; }
                rb.linearVelocity = v;
            }
            AbilityRPCManager.SpawnFX(to + Ring.Up(to), "vfx_ghost_death", 1f);
            ClipSlots.Play(p, "rpgclasses.mage.blink");
            FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] second jump rpg.mage.blink by {p.GetPlayerName()}: {from:F1} -> {to:F1} ({Vector3.Distance(from, to):0.0} m), {cost:0} eitr, smoke at both ends");
            return true;
        }

        private static readonly int BlockMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle", "Default_small");

        // the body's capsule swept along the path; stops short of anything solid, and refuses if that leaves less than 1 m
        private static bool SafeBlink(Player p, Vector3 from, Vector3 dir, float want, out Vector3 to, out string why)
        {
            to = from;
            var col = p.GetComponent<CapsuleCollider>();
            float r = col != null ? col.radius * Size(p) : 0.4f, h = col != null ? col.height * Size(p) : 1.8f;
            Vector3 up = Ring.Up(from);   // Vector3.up off the halo ring
            Vector3 a = from + up * r, b = from + up * (h - r);
            float go = want;
            if (Physics.CapsuleCast(a, b, r * 0.9f, dir, out RaycastHit hit, want, BlockMask)) go = Mathf.Max(0f, hit.distance - 0.2f);
            if (go < 1f) { why = $"blocked after {go:0.0} m"; return false; }
            to = from + dir * go;
            if (Physics.CheckCapsule(to + up * r, to + up * (h - r), r * 0.9f, BlockMask)) { why = "the end point is inside something"; return false; }
            why = null;
            return true;
        }

        // the float / glide: a capped fall speed while the window runs (owner-side, before vanilla's physics step)
        // Not "FixedUpdate": Character doesn't have one, and an undefined target throws out of PatchAll and skips the rest of
        // Awake. CustomFixedUpdate is vanilla's physics step (Core's CharacterSnowCover patches it too).
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        private static class SlowFall
        {
            private static void Prepare(MethodBase original)
            {
                if (original != null) FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] SlowFall attached to {original.DeclaringType?.Name}.{original.Name}");
            }

            // 0.1.204: runs for every loaded character every physics step (~3.7 M calls a dense round), so when no float window is
            // open anywhere (the usual case) it returns before any Unity call. Counts skipped vs worked calls for the 30 s Debug line.
            private static long s_skipped, s_worked;
            private static float s_nextReport;

            private static void Postfix(Character __instance)
            {
                if (_earlyOut != null && _earlyOut.Value && _floatUntil == 0f && s_npcFloat.Count == 0)
                {
                    if ((++s_skipped & 4095) == 0) Report();
                    return;
                }
                s_worked++;
                if ((s_worked & 255) == 0) Report();
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) { NpcSlowFall(__instance); return; }   // 0.1.202: a companion's own float window (none = nothing)
                if (Time.time >= _floatUntil) { _floatUntil = 0f; return; }
                var rb = Body((Player)__instance);
                if (rb == null || __instance.IsOnGround()) { _floatUntil = 0f; return; }
                var v = rb.linearVelocity;
                Vector3 pos = __instance.transform.position;
                if (!Ring.InField(pos)) { if (v.y < -2f) { v.y = -2f; rb.linearVelocity = v; } }
                else
                {
                    Vector3 ru = Ring.Up(pos);
                    float along = Vector3.Dot(v, ru);
                    if (along < -2f) { v += ru * (-2f - along); rb.linearVelocity = v; }
                }
            }
            private static void Report()
            {
                float now = Time.realtimeSinceStartup;
                if (now < s_nextReport) return;
                if (s_nextReport > 0f)
                    FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] slow fall early-out: {s_skipped} call(s) skipped (no float window open), {s_worked} worked in the last 30 s");
                s_nextReport = now + 30f;
                s_skipped = 0; s_worked = 0;
            }
        }

        // ── race dodges (ahead of the class evasion; EvasionController.TryTakeDodge asks first) ─────────────────────────────
        public static bool TryRaceDodge(Player p, Vector3 direction)
        {
            if (!On || p == null || p != Player.m_localPlayer || p.IsDead() || Level(p) < _dodgeAtLevel.Value) return false;
            string race = Race(p);
            Vector3 dir = Flat(direction); if (dir == Vector3.zero) dir = Flat(p.transform.forward);
            if (race == "Vampire") return MistStep(p, dir);
            if (race == "Werewolf") return Pounce(p, dir);
            return false;
        }

        private static bool MistStep(Player p, Vector3 dir)
        {
            float cost = _mistEitr.Value;
            if (!p.HaveEitr(cost)) { FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] dodge rpg.vampire.miststep by {p.GetPlayerName()}: refused (eitr {p.GetEitr():0} < {cost:0}); the class dodge instead"); return false; }
            Vector3 from = p.transform.position;
            if (!SafeBlink(p, from, dir, 5f * Size(p), out Vector3 to, out string why)) { FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] dodge rpg.vampire.miststep by {p.GetPlayerName()}: refused ({why}); the class dodge instead"); return false; }
            p.UseEitr(cost);
            AbilityRPCManager.SpawnFX(from + Ring.Up(from), "vfx_ghost_death", 1.2f);
            p.transform.position = to;
            var rb = Body(p); if (rb != null) rb.position = to;
            AbilityRPCManager.SpawnFX(to + Ring.Up(to), "vfx_ghost_death", 1.2f);
            ClipSlots.Play(p, "rpgclasses.vampire.miststep");
            FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] dodge rpg.vampire.miststep by {p.GetPlayerName()}: {from:F1} -> {to:F1} ({Vector3.Distance(from, to):0.0} m), {cost:0} eitr, mist at both ends");
            return true;
        }

        private static bool Pounce(Player p, Vector3 dir)
        {
            float cost = _pounceStamina.Value;
            if (!p.HaveStamina(cost)) { FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] dodge rpg.werewolf.pounce by {p.GetPlayerName()}: refused (stamina {p.GetStamina():0} < {cost:0}); the class dodge instead"); return false; }
            var rb = Body(p);
            if (rb == null) return false;
            p.UseStamina(cost);
            rb.linearVelocity = dir * 11f * Size(p) + Ring.Up(p.transform.position) * 3f;

            bool clip = ClipSlots.Play(p, "rpgclasses.werewolf.pounce");
            if (!clip) p.m_zanim?.SetTrigger("jump");
            FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] dodge rpg.werewolf.pounce by {p.GetPlayerName()}: lunge {11f * Size(p):0.0} m/s {dir:F2}, {cost:0} stamina, anim {(clip ? "clip slot" : "vanilla jump")}");
            return true;
        }
    }
}
