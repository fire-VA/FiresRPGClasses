using System;
using System.Reflection;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    // Halo ring local up. Core 0.2.815+ FiresCore.World.GravityUp (FiresUnifiedCore), read by reflection so an older Core
    // still loads. Off the ring (or no Core API) every method gives exactly the old world-Y answer. Never throws.
    internal static class Ring
    {
        private static bool s_resolved;
        private static MethodInfo s_inField, s_at;
        private static bool s_logged;

        internal static readonly int SurfaceMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");

        private static void Resolve()
        {
            if (s_resolved) return;
            s_resolved = true;
            try
            {
                var t = Type.GetType("FiresCore.World.GravityUp, FiresUnifiedCore", false);
                if (t == null) return;
                s_inField = t.GetMethod("InField", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
                s_at = t.GetMethod("At", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
                var active = t.GetProperty("Active", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                if (s_inField == null || s_inField.ReturnType != typeof(bool) || s_at == null || s_at.ReturnType != typeof(Vector3) || active == null) { s_inField = null; s_at = null; return; }
                // bound once as delegates: no reflection Invoke / object[] per call
                s_active = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), active);
                s_inFieldFn = (Func<Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, bool>), s_inField);
                s_atFn = (Func<Vector3, Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3>), s_at);
            }
            catch { s_inField = null; s_at = null; s_active = null; s_inFieldFn = null; s_atFn = null; }
        }

        // Fire 10-26 ("none of our ringworld logic ... ever runs on any world that is not a ring world"): Core's GravityUp.Active (the ONE
        // ring authority, a cached bool) is asked first, so off the ring every call is one cheap bool check: no position test, no allocation.
        private static Func<bool> s_active;
        private static Func<Vector3, bool> s_inFieldFn;
        private static Func<Vector3, Vector3> s_atFn;

        public static bool InField(Vector3 pos)
        {
            Resolve();
            if (s_active == null) return false;
            try { return s_active() && s_inFieldFn(pos); }
            catch { return false; }
        }

        public static Vector3 Up(Vector3 pos)
        {
            if (!InField(pos)) return Vector3.up;
            Vector3 up;
            try { up = s_atFn(pos); }
            catch { return Vector3.up; }
            if (up.sqrMagnitude < 0.0001f) return Vector3.up;
            up = up.normalized;
            if (!s_logged && up != Vector3.up)
            {
                s_logged = true;
                FiresRPGClassesPlugin.Log?.LogDebug($"[Ring] halo ring active at {pos:F1}: up {up:F2}, RPGClasses movement/AoE/knockback use the local up");
            }
            return up;
        }

        // 0.1.198 (halo ring map): FAT unrolls the ring onto the minimap at k map metres per ring metre (k = half the map's extent / (pi R),
        // FatHaloMap.Unroll), while vanilla's explore radius stays in map metres, so a metre walked reveals k times the map area. This is
        // that k (FatApi.HaloActive + HaloAxis radius, by reflection) when pos is on the ring; exactly 1 everywhere else.
        private static bool s_fatResolved, s_stretchLogged;
        private static PropertyInfo s_haloActive;
        private static MethodInfo s_haloAxis;

        public static float MapStretch(Vector3 pos, float pixelSize, int textureSize)
        {
            if (!InField(pos)) return 1f;
            if (!s_fatResolved)
            {
                s_fatResolved = true;
                try
                {
                    var t = Type.GetType("VerdantsAscent.FatApi, FiresAdminTerrain", false);
                    s_haloActive = t?.GetProperty("HaloActive", BindingFlags.Public | BindingFlags.Static);
                    s_haloAxis = t?.GetMethod("HaloAxis", BindingFlags.Public | BindingFlags.Static);
                }
                catch { s_haloActive = null; s_haloAxis = null; }
            }
            if (s_haloAxis == null) return 1f;   // the ring itself is decided by Core (InField above); FAT only gives the radius
            try
            {
                var args = new object[] { Vector3.zero, Vector3.up, 0f };
                if (!(bool)s_haloAxis.Invoke(null, args)) return 1f;
                float r = (float)args[2];
                if (r < 1f) return 1f;
                float k = textureSize * 0.5f * pixelSize / (Mathf.PI * r);
                if (k <= 0f || float.IsNaN(k)) return 1f;
                if (!s_stretchLogged)
                {
                    s_stretchLogged = true;
                    FiresRPGClassesPlugin.Log?.LogDebug($"[Ring] exploring XP on the halo ring: map stretch {k:0.00} map m per ring m (R {r:0}); explored area divided by it, so XP per metre walked matches flat worlds");
                }
                return k;
            }
            catch { return 1f; }
        }

        // off the ring: v.y = 0 (the caller normalises as before); on the ring: the tangent-plane part
        public static Vector3 Flat(Vector3 v, Vector3 pos)
        {
            if (!InField(pos)) { v.y = 0f; return v; }
            return Vector3.ProjectOnPlane(v, Up(pos));
        }

        // raycast along -up from 1 m above, up to 50 m
        public static bool DropToSurface(Vector3 from, out Vector3 point)
        {
            Vector3 up = Up(from);
            if (Physics.Raycast(from + up, -up, out RaycastHit hit, 51f, SurfaceMask, QueryTriggerInteraction.Ignore)) { point = hit.point; return true; }
            point = from;
            return false;
        }
    }
}
