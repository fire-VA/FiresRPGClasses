using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Paragon constellation figures (from the warfare / fitness / craft mockup sheets). One figure per
    /// branch (W1..W4, F1..F4, C1..C4): named outline stars in a 600 x 700 frame, the lines and smooth curves between them (the
    /// constellation itself), and which outline star each Paragon role (1..6, S1..S4) sits on. The data is generated from the
    /// sheet scripts (ParagonFigures.Data.cs), so the game and the approved sheets can't drift. Off: the tier-ring map as before.
    /// </summary>
    internal static partial class ParagonFigures
    {
        internal const float FrameW = 600f, FrameH = 700f;
        internal static ConfigEntry<bool> Enabled;

        internal sealed class Figure
        {
            public string Branch, Title, BranchName;
            public Dictionary<string, Vector2> Points;   // frame units, y down
            public string[][] Lines, Curves;
            public Dictionary<string, string> Nodes;     // role ("1", "S2") -> point key
        }

        private static readonly Dictionary<string, Figure> All = new Dictionary<string, Figure>();

        private static void Add(string branch, string title, string branchName, Dictionary<string, Vector2> pts, string[][] lines,
            string[][] curves, Dictionary<string, string> nodes)
        {
            All[branch] = new Figure { Branch = branch, Title = title, BranchName = branchName, Points = pts, Lines = lines, Curves = curves, Nodes = nodes };
        }

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("Paragon", "Constellation figures", false,
                "Draw each Paragon branch as its constellation figure instead of the tier-ring star map.");
        }

        // On with its own key, or whenever the Classes window is hosted in the Fires hub (the whole overhaul follows the override)
        internal static bool On => (Enabled != null && Enabled.Value) || ClassesHub.Hosted;

        /// <summary>The branch of a star id ("W2.S1" -> "W2"); bridges ("WB1") have no figure.</summary>
        internal static string BranchOf(string starId)
        {
            int dot = starId?.IndexOf('.') ?? -1;
            return dot > 0 ? starId.Substring(0, dot) : null;
        }

        internal static string RoleOf(string starId)
        {
            int dot = starId?.LastIndexOf('.') ?? -1;
            return dot > 0 ? starId.Substring(dot + 1) : null;
        }

        internal static bool TryGet(string branch, out Figure figure) => All.TryGetValue(branch ?? "", out figure);

        /// <summary>Catmull-Rom through the keyed points (tension 0.5 like the sheets' GDI AddCurve), sampled per segment.</summary>
        internal static List<Vector2> Sample(Figure f, string[] keys, int perSegment = 8)
        {
            var p = new List<Vector2>();
            foreach (string k in keys) if (f.Points.TryGetValue(k, out Vector2 v)) p.Add(v);
            var result = new List<Vector2>();
            if (p.Count < 2) return result;
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector2 p0 = p[Mathf.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(p.Count - 1, i + 2)];
                for (int s = 0; s < perSegment; s++)
                {
                    float t = s / (float)perSegment, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(p[p.Count - 1]);
            return result;
        }
    }
}
