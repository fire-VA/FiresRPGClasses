using System.Collections.Generic;
using FiresCore.Animation;
using FiresCore.Movement;
using FiresCore.Npc.Archetypes;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.202 ([wishbone], Fire via lead / [ghost] 10-27: "companions use the RPG class second-jump STYLES"). Add-only; the local
    /// player's path (Core's double jump -> MovementStyles -> the Player styles above) is untouched.
    ///   StyleFor(who): the second-jump style id this body would use, or null. A companion's class comes from Core's
    ///     ArchetypeController.CurrentArchetypeClass (the companion's own archetype, synced by Core); companions have no race, so the
    ///     race styles (bat hop, pounce) and race dodges stay player-only.
    ///   TryStyle(who, dir, out why): performs it on <paramref name="who"/>: owner only (ZNetView.IsOwner), the same impulses / blink /
    ///     float as the player's, from who's own eitr / stamina (Character.HaveEitr / UseEitr), through Core's ClipSlots (vanilla
    ///     jump trigger as the fallback). "[ClassMovement] ..." why-line at Debug. Needs [RPG Classes] Class movement styles on.
    /// No RPC: the owner moves its own body and vanilla / ZSyncAnimation sync it.
    /// </summary>
    public static partial class ClassMovement
    {
        /// <summary>The second-jump style id <paramref name="who"/> would use, or null (off, not a companion with a class, or the local player).</summary>
        public static string StyleFor(Character who)
        {
            if (!On || who == null || who is Player) return null;   // the player keeps Core's own MovementStyles path
            var ac = who.GetComponent<ArchetypeController>();
            if (ac == null) return null;
            switch (ac.CurrentArchetypeClass)
            {
                case ArchetypeClass.Rogue: case ArchetypeClass.Monk: return "rpg.flip";
                case ArchetypeClass.Mage: return "rpg.mage.blink";
                case ArchetypeClass.Ranger: return "rpg.ranger.vault";
                case ArchetypeClass.Berserker: return "rpg.berserker.leap";
                case ArchetypeClass.Healer: case ArchetypeClass.Druid: case ArchetypeClass.Necromancer: return "rpg.float";
                default: return null;
            }
        }

        /// <summary>Performs <see cref="StyleFor"/>'s style on <paramref name="who"/> toward <paramref name="dir"/>; false (with why) when
        /// none applies or it is refused, so the caller does its plain double jump.</summary>
        public static bool TryStyle(Character who, Vector3 dir, out string why)
        {
            why = null;
            if (who == null) { why = "no body"; return false; }
            if (who is Player) { why = "the player uses Core's own double-jump styles"; return false; }
            var nv = who.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) { why = "not the owner"; return false; }
            if (who.IsDead()) { why = "dead"; return false; }
            string id = StyleFor(who);
            if (id == null) { why = !On ? "[RPG Classes] Class movement styles is off" : "no class style for this companion"; return false; }
            Vector3 fwd = Flat(dir, who.transform.position); if (fwd == Vector3.zero) fwd = Flat(who.transform.forward, who.transform.position);
            bool ok;
            switch (id)
            {
                case "rpg.flip": ok = NpcImpulse(who, fwd, 0.9f, 2.5f, "rpg.flip (front)", "rpgclasses.flip.front", out why); break;
                case "rpg.ranger.vault": ok = NpcImpulse(who, fwd, 0.8f, 3f, id, "rpgclasses.vault", out why); break;
                case "rpg.berserker.leap": ok = NpcImpulse(who, fwd, 0.7f, 4.5f, id, "rpgclasses.leap", out why); break;
                case "rpg.float": ok = NpcImpulse(who, fwd, 0.6f, 0f, "rpg.float (slow fall 1.2 s)", "rpgclasses.float", out why); if (ok) s_npcFloat[who.GetInstanceID()] = Time.time + 1.2f; break;
                case "rpg.mage.blink": ok = NpcBlink(who, fwd, out why); break;
                default: ok = false; why = "unknown style " + id; break;
            }
            if (!ok) FiresRPGClassesPlugin.Log?.LogDebug($"[ClassMovement] {id} by {who.GetHoverName()}: refused ({why}); plain double jump instead");
            return ok;
        }

        private static readonly Dictionary<int, float> s_npcFloat = new Dictionary<int, float>();

        private static Vector3 Flat(Vector3 v, Vector3 at) { v = Ring.Flat(v, at); return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero; }
        private static float SizeOf(Character c) => Mathf.Clamp(c.transform.localScale.y, 0.25f, 4f);

        private static bool NpcImpulse(Character c, Vector3 fwd, float up, float forward, string id, string clip, out string why)
        {
            why = null;
            var rb = BodyField?.GetValue(c) as Rigidbody;
            if (rb == null) { why = "no rigidbody"; return false; }
            Vector3 v = rb.linearVelocity, pos = c.transform.position;
            float vUp, size = SizeOf(c);
            if (!Ring.InField(pos)) { v.y = Mathf.Max(v.y, 0f) + c.m_jumpForce * up * size; vUp = v.y; }
            else
            {
                Vector3 ru = Ring.Up(pos);
                float along = Vector3.Dot(v, ru);
                vUp = Mathf.Max(along, 0f) + c.m_jumpForce * up * size;
                v = v - ru * along + ru * vUp;
            }
            v += fwd * forward * size;
            rb.linearVelocity = v;
            bool clipOk = ClipSlots.Play(c, clip);
            if (!clipOk) c.m_zanim?.SetTrigger("jump");
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassMovement] second jump {id} by {c.GetHoverName()}: up {vUp:0.0}, forward {forward * size:0.0}, anim {(clipOk ? clip : "vanilla jump")}");
            return true;
        }

        private static bool NpcBlink(Character c, Vector3 fwd, out string why)
        {
            float cost = _blinkEitr.Value;
            if (!c.HaveEitr(cost)) { why = $"not enough eitr ({cost:0} needed)"; return false; }
            Vector3 from = c.transform.position;
            Vector3 dir = (fwd * 2.5f + Ring.Up(from) * 1.6f).normalized;
            float size = SizeOf(c), want = 3f * size;
            var col = c.GetComponent<CapsuleCollider>();
            float r = col != null ? col.radius * size : 0.4f, h = col != null ? col.height * size : 1.8f;
            Vector3 up = Ring.Up(from);
            float go = want;
            if (Physics.CapsuleCast(from + up * r, from + up * (h - r), r * 0.9f, dir, out RaycastHit hit, want, BlockMask)) go = Mathf.Max(0f, hit.distance - 0.2f);
            if (go < 1f) { why = $"blocked after {go:0.0} m"; return false; }
            Vector3 to = from + dir * go;
            if (Physics.CheckCapsule(to + up * r, to + up * (h - r), r * 0.9f, BlockMask)) { why = "the end point is inside something"; return false; }
            c.UseEitr(cost);
            AbilityRPCManager.SpawnFX(from + up, "vfx_ghost_death", 1f);
            c.transform.position = to;
            var rb = BodyField?.GetValue(c) as Rigidbody;
            if (rb != null) { rb.position = to; var v = rb.linearVelocity; if (!Ring.InField(to)) v.y = Mathf.Max(v.y, 0f); else { Vector3 ru = Ring.Up(to); float along = Vector3.Dot(v, ru); if (along < 0f) v -= ru * along; } rb.linearVelocity = v; }
            AbilityRPCManager.SpawnFX(to + Ring.Up(to), "vfx_ghost_death", 1f);
            ClipSlots.Play(c, "rpgclasses.mage.blink");
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassMovement] second jump rpg.mage.blink by {c.GetHoverName()}: {from:F1} -> {to:F1} ({Vector3.Distance(from, to):0.0} m), {cost:0} eitr");
            why = null;
            return true;
        }

        // the companion float: the same capped fall as the player's, while its own window runs (owner side; called from SlowFall)
        private static void NpcSlowFall(Character c)
        {
            if (s_npcFloat.Count == 0 || c == null) return;
            int id = c.GetInstanceID();
            if (!s_npcFloat.TryGetValue(id, out float until)) return;
            if (Time.time >= until || c.IsOnGround()) { s_npcFloat.Remove(id); return; }
            var rb = BodyField?.GetValue(c) as Rigidbody;
            if (rb == null) return;
            var v = rb.linearVelocity; Vector3 pos = c.transform.position;
            if (!Ring.InField(pos)) { if (v.y < -2f) { v.y = -2f; rb.linearVelocity = v; } }
            else { Vector3 ru = Ring.Up(pos); float along = Vector3.Dot(v, ru); if (along < -2f) { v += ru * (-2f - along); rb.linearVelocity = v; } }
        }
    }
}
