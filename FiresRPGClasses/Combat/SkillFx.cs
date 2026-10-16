using System.Collections.Generic;
using FiresCore.Npc.Archetypes;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.102 (Fire 10-12, the placeholder audit: five skills showed nothing): one-shot vanilla effects for them, each prefab name
    /// checked against the game's own assets. Spawned through Core's AbilityRPCManager.SpawnFX (the class family's FX path), and
    /// throttled per key so a flurry of hits can't spam them. No per-frame cost when nothing fires.
    /// </summary>
    internal static class SkillFx
    {
        internal const string ShieldStart = "fx_shield_start";          // Shield Wall raised
        internal const string ShieldHit = "fx_GoblinShieldHit";         // a blocked / walled hit (Iron Wall, Shield Wall)
        internal const string StoneSparks = "vfx_HitSparks";            // Stone Skin turning a physical hit
        internal const string SupportGlow = "fx_DvergerMage_Support_start"; // Tailwind's party buff
        internal const string Stomp = "fx_eikthyr_stomp";               // Breakwall's first step
        internal const string GroundSlam = "vfx_lox_groundslam";        // Breakwall's dust along the charge
        internal const string HeavyHit = "vfx_sledge_hit";              // Breakwall hitting someone

        private static readonly Dictionary<string, float> Next = new Dictionary<string, float>();

        /// <summary>Spawns <paramref name="fx"/> at <paramref name="at"/> unless the same key fired under <paramref name="gap"/> s ago.</summary>
        internal static void Play(string key, Vector3 at, string fx, float gap = 0.5f)
        {
            if (Next.TryGetValue(key, out float t) && Time.time < t) return;
            Next[key] = Time.time + gap;
            AbilityRPCManager.SpawnFX(at, fx);
        }
    }
}