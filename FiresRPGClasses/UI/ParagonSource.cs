using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The Paragon tab's only door to the data: FiresRPGClasses.Paragon.ParagonUi ([wishbone], contract in
    /// Tools\RPG_PARAGON_TALENT_PLAN.md §9), read by reflection so the tab builds and shows "not available" before that API
    /// exists. Every view field is read by name; a missing field reads as its default and is logged once.
    /// </summary>
    internal static class ParagonSource
    {
        internal sealed class Cluster { public string Id, Name; public float AngleDeg; }

        internal sealed class Tree
        {
            public string Id, Name;
            public Color Colour;
            public int[] TierGates = { 0, 25, 75, 150 };
            public List<Cluster> Clusters = new List<Cluster>();
        }

        internal sealed class Star
        {
            public string Id, Name, ShortName, Cluster, LockReason;
            public int Tier, MaxRank, Rank;
            public bool Slottable, Capstone, Bridge, Unlocked;
            public Vector2 Pos;
            public string[] Links = new string[0];
        }

        private const string TypeName = "FiresRPGClasses.Paragon.ParagonUi";
        private static Type _api;
        private static bool _looked;
        private static readonly HashSet<string> _missing = new HashSet<string>();

        internal static bool Available => Api != null;

        private static Type Api
        {
            get
            {
                if (_looked) return _api;
                _looked = true;
                _api = AccessTools.TypeByName(TypeName);
                FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] UI source: " + (_api != null ? TypeName : "none (" + TypeName + " not found)"));
                return _api;
            }
        }

        internal static List<Tree> Trees()
        {
            var list = new List<Tree>();
            if (!(Get("Trees") is IEnumerable trees)) return list;
            foreach (object t in trees)
            {
                if (t == null) continue;
                var tree = new Tree
                {
                    Id = Field<string>(t, "id"), Name = Field<string>(t, "name"),
                    Colour = Field<Color>(t, "colour", Color.white)
                };
                int[] gates = Field<int[]>(t, "tierGates");
                if (gates != null && gates.Length > 0) tree.TierGates = gates;
                if (Field<object>(t, "clusters") is IEnumerable clusters)
                    foreach (object c in clusters)
                        if (c != null)
                            tree.Clusters.Add(new Cluster { Id = Field<string>(c, "id"), Name = Field<string>(c, "name"), AngleDeg = Field<float>(c, "angleDeg") });
                list.Add(tree);
            }
            return list;
        }

        internal static List<Star> Stars(string treeId)
        {
            var list = new List<Star>();
            if (!(Call("Stars", treeId) is IEnumerable stars)) return list;
            foreach (object s in stars)
            {
                if (s == null) continue;
                string kind = Convert.ToString(Field<object>(s, "kind"));
                list.Add(new Star
                {
                    Id = Field<string>(s, "id"), Name = Field<string>(s, "name"), ShortName = Field<string>(s, "shortName"),
                    Cluster = Field<string>(s, "cluster"), Tier = Field<int>(s, "tier"), MaxRank = Field<int>(s, "maxRank"),
                    Rank = Field<int>(s, "rank"), Slottable = string.Equals(kind, "Slottable", StringComparison.OrdinalIgnoreCase),
                    Capstone = Field<bool>(s, "capstone"), Bridge = Field<bool>(s, "bridge"), Unlocked = Field<bool>(s, "unlocked"),
                    LockReason = Field<string>(s, "lockReason"), Pos = Field<Vector2>(s, "pos"),
                    Links = Field<string[]>(s, "links") ?? new string[0]
                });
            }
            return list;
        }

        internal static string Tooltip(string starId) => Call("Tooltip", starId) as string ?? "";
        internal static string[] TooltipLines(string starId) => Call("TooltipLines", starId) as string[] ?? new string[0];
        internal static int SpentInTree(string treeId) => Call("SpentInTree", treeId) is int n ? n : 0;
        internal static int Unspent => Get("Unspent") is int n ? n : 0;
        internal static string[] Slotted(string treeId) => Call("Slotted", treeId) as string[] ?? new string[4];

        internal static bool TryBuy(string starId, out string why) => Try("TryBuy", out why, starId);
        internal static bool TrySlot(string treeId, int slot, string starId, out string why) => Try("TrySlot", out why, treeId, slot, starId);
        internal static bool TryRespec(string treeId, out string why) => Try("TryRespec", out why, treeId);
        internal static bool TryUnslot(string treeId, int slot) => Call("TryUnslot", treeId, slot) is bool ok && ok;

        /// <summary>Subscribes to ParagonUi.Changed (once); false when the API has no such event.</summary>
        internal static bool Subscribe(Action onChanged)
        {
            EventInfo ev = Api?.GetEvent("Changed", BindingFlags.Public | BindingFlags.Static);
            if (ev == null) return false;
            try { ev.AddEventHandler(null, onChanged); return true; }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] Changed subscribe failed: " + e.Message); return false; }
        }

        internal static void Unsubscribe(Action onChanged)
        {
            EventInfo ev = Api?.GetEvent("Changed", BindingFlags.Public | BindingFlags.Static);
            try { ev?.RemoveEventHandler(null, onChanged); } catch { }
        }

        // ---- reflection -----------------------------------------------------------------------------------

        private static object Get(string name)
        {
            if (Api == null) return null;
            try
            {
                PropertyInfo p = Api.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (p != null) return p.GetValue(null);
                FieldInfo f = Api.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (f != null) return f.GetValue(null);
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] " + name + " failed: " + (e.InnerException?.Message ?? e.Message)); return null; }
            Missing(name);
            return null;
        }

        private static object Call(string name, params object[] args)
        {
            MethodInfo m = Find(name, args.Length, false);
            if (m == null) return null;
            try { return m.Invoke(null, args); }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] " + name + " failed: " + (e.InnerException?.Message ?? e.Message)); return null; }
        }

        /// <summary>A bool method with a trailing <c>out string why</c>.</summary>
        private static bool Try(string name, out string why, params object[] args)
        {
            why = null;
            MethodInfo m = Find(name, args.Length + 1, true);
            if (m == null) { why = "Paragon is not available yet."; return false; }
            var all = new object[args.Length + 1];
            Array.Copy(args, all, args.Length);
            try
            {
                bool ok = m.Invoke(null, all) is bool b && b;
                why = all[args.Length] as string;
                return ok;
            }
            catch (Exception e) { why = e.InnerException?.Message ?? e.Message; return false; }
        }

        private static MethodInfo Find(string name, int arity, bool trailingOut)
        {
            if (Api == null) return null;
            foreach (MethodInfo m in Api.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != arity) continue;
                if (trailingOut != (ps.Length > 0 && ps[ps.Length - 1].IsOut)) continue;
                return m;
            }
            Missing(name + "/" + arity);
            return null;
        }

        private static T Field<T>(object o, string name, T fallback = default)
        {
            Type t = o.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            try
            {
                FieldInfo f = t.GetField(name, flags);
                object v = f != null ? f.GetValue(o) : t.GetProperty(name, flags)?.GetValue(o);
                if (v is T typed) return typed;
                if (v == null) { if (f == null && t.GetProperty(name, flags) == null) Missing(t.Name + "." + name); return fallback; }
                if (typeof(T) == typeof(string)) return (T)(object)v.ToString();
                return (T)Convert.ChangeType(v, typeof(T));
            }
            catch { return fallback; }
        }

        private static void Missing(string what)
        {
            if (_missing.Add(what)) FiresRPGClassesPlugin.Log?.LogDebug("[Paragon] ParagonUi has no '" + what + "' (contract §9); using a default");
        }
    }
}
