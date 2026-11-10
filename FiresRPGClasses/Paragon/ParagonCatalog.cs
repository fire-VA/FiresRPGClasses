using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using FiresCore.Storage;
using FiresRPGClasses.Data;
using UnityEngine;
using YamlDotNet.Serialization;

namespace FiresRPGClasses.Paragon
{
    public enum ParagonStarKind { Passive, Slottable }

    public enum ParagonCurve { Soft, Asymptote }

    public sealed class ParagonClusterDef
    {
        public string Id, Name;
        public float Angle;
    }

    public sealed class ParagonTreeDef
    {
        public string Id, Name, Colour;
        public int[] TierGates = { 0, 25, 75, 150 };
        public readonly List<ParagonClusterDef> Clusters = new List<ParagonClusterDef>();

        /// <summary>Points spent in this tree a tier needs (tier 1-based).</summary>
        public int GateFor(int tier) => TierGates == null || TierGates.Length == 0 ? 0 : TierGates[Mathf.Clamp(tier - 1, 0, TierGates.Length - 1)];
    }

    public sealed class ParagonStarDef
    {
        public string Id, Name, ShortName, Tree, Cluster, EffectFormat;
        public int Tier, MaxRank, Order;
        public ParagonStarKind Kind;
        public bool Capstone, Bridge;
        public float PerRank, Knee, Limit;
        public ParagonCurve Curve;
        public string[] Links = new string[0];
        public Vector2 Pos;
        /// <summary>Stars whose links point at this one (the path prerequisite).</summary>
        public readonly List<string> LinkedFrom = new List<string>();

        public bool IsEntry => Tier <= 1;

        /// <summary>
        /// This star's own value at <paramref name="rank"/>: per point x points, then the star's curve (plan §8): Soft on the rank
        /// count past the knee, or an Asymptote on the value toward the limit.
        /// </summary>
        public float ValueAt(int rank)
        {
            if (rank <= 0) return 0f;
            if (Curve == ParagonCurve.Asymptote && Limit > 0f)
                return FiresCore.Curves.SoftCap.Asymptote(PerRank * rank, PerRank * Knee, Limit);
            return PerRank * FiresCore.Curves.SoftCap.Soft(rank, Knee);
        }

        /// <summary>The effect text at a value (Latin-1, from the yaml's effectFormat).</summary>
        public string EffectText(float value) =>
            (EffectFormat ?? "").Replace("{value}", value.ToString(Math.Abs(value) >= 10f ? "0.#" : "0.##", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The Paragon constellations from paragon-trees.yaml (plan §8/§9). Ships like classes-trees.yaml: a bundled copy seeds
    /// the config folder; a newer bundled revision backs the old file up and replaces it; the file on disk is the data of record and
    /// reloads on edit. If the file can't be read, the bundled copy loads.
    /// </summary>
    public static class ParagonCatalog
    {
        public const string FileName = "paragon-trees.yaml";
        private const string BundledResourceName = "FiresRPGClasses.Data.paragon-trees.yaml";

        private static readonly List<ParagonTreeDef> _trees = new List<ParagonTreeDef>();
        private static readonly List<ParagonStarDef> _stars = new List<ParagonStarDef>();
        private static readonly Dictionary<string, ParagonStarDef> _byId = new Dictionary<string, ParagonStarDef>();
        private static readonly Dictionary<string, ParagonTreeDef> _treeById = new Dictionary<string, ParagonTreeDef>();
        private static readonly Dictionary<string, List<ParagonStarDef>> _byTree = new Dictionary<string, List<ParagonStarDef>>();
        private static FileSystemWatcher _watcher;
        private static volatile bool _reloadPending;
        private static DateTime _reloadAtUtc;

        public static IReadOnlyList<ParagonTreeDef> Trees => _trees;
        public static IReadOnlyList<ParagonStarDef> Stars => _stars;
        public static int Revision { get; private set; }
        public static string LoadedFrom { get; private set; } = "nothing";

        /// <summary>Raised after every (re)load.</summary>
        public static event Action Loaded;

        /// <summary>Maps a star id to its tree id (Core's ParagonSpentInTree takes it).</summary>
        public static readonly Func<string, string> TreeOf = id => id != null && _byId.TryGetValue(id, out var s) ? s.Tree : null;

        public static string FilePath => Path.Combine(FiresConfigPaths.Classes, FileName);

        public static ParagonStarDef Star(string id) => id != null && _byId.TryGetValue(id, out var s) ? s : null;

        public static ParagonTreeDef Tree(string id) => id != null && _treeById.TryGetValue(id, out var t) ? t : null;

        public static IReadOnlyList<ParagonStarDef> StarsIn(string treeId) =>
            treeId != null && _byTree.TryGetValue(treeId, out var list) ? list : (IReadOnlyList<ParagonStarDef>)Array.Empty<ParagonStarDef>();

        public static void Initialize()
        {
            SeedOrUpgradeFile();
            Load();
            StartWatching();
        }

        public static void Load()
        {
            string text = null, from = null;
            try { if (File.Exists(FilePath)) { text = File.ReadAllText(FilePath); from = FileName; } }
            catch (Exception e) { Debug.LogWarning($"[Paragon] {FilePath} could not be read ({e.GetType().Name}: {e.Message})"); }
            if (text != null && TryParse(text, from)) return;
            string bundled = ReadBundled();
            if (bundled != null && TryParse(bundled, "the bundled " + FileName))
                Debug.LogWarning($"[Paragon] Using the bundled {FileName}; fix the file on disk and save it to reload.");
        }

        private static bool TryParse(string yaml, string from)
        {
            try
            {
                var root = new DeserializerBuilder().IgnoreUnmatchedProperties().Build()
                    .Deserialize<Dictionary<object, object>>(new StringReader(yaml));
                if (root == null) throw new InvalidDataException("empty or not a mapping");
                var trees = new List<ParagonTreeDef>();
                var stars = new List<ParagonStarDef>();
                foreach (object o in List(root, "trees"))
                {
                    var m = o as Dictionary<object, object>;
                    if (m == null) continue;
                    var t = new ParagonTreeDef { Id = Str(m, "id"), Name = Str(m, "name"), Colour = Str(m, "colour") ?? "#FFFFFF" };
                    var gates = new List<int>();
                    foreach (object g in List(m, "tierGates")) gates.Add(ToInt(g));
                    if (gates.Count > 0) t.TierGates = gates.ToArray();
                    foreach (object c in List(m, "clusters"))
                        if (c is Dictionary<object, object> cm)
                            t.Clusters.Add(new ParagonClusterDef { Id = Str(cm, "id"), Name = Str(cm, "name"), Angle = ToFloat(Get(cm, "angle")) });
                    if (!string.IsNullOrEmpty(t.Id)) trees.Add(t);
                }
                int order = 0;
                foreach (object o in List(root, "stars"))
                {
                    var m = o as Dictionary<object, object>;
                    if (m == null) continue;
                    var s = new ParagonStarDef
                    {
                        Id = Str(m, "id"), Name = Str(m, "name"), ShortName = Str(m, "shortName"), Tree = Str(m, "tree"),
                        Cluster = Str(m, "cluster"), EffectFormat = Str(m, "effectFormat"), Tier = ToInt(Get(m, "tier")),
                        MaxRank = ToInt(Get(m, "maxRank")), Order = order++,
                        Kind = string.Equals(Str(m, "kind"), "slottable", StringComparison.OrdinalIgnoreCase) ? ParagonStarKind.Slottable : ParagonStarKind.Passive,
                        Capstone = ToBool(Get(m, "capstone")), Bridge = ToBool(Get(m, "bridge")),
                        PerRank = ToFloat(Get(m, "perRank")), Knee = ToFloat(Get(m, "knee")), Limit = ToFloat(Get(m, "limit")),
                        Curve = string.Equals(Str(m, "curve"), "asymptote", StringComparison.OrdinalIgnoreCase) ? ParagonCurve.Asymptote : ParagonCurve.Soft,
                    };
                    var links = new List<string>();
                    foreach (object l in List(m, "links")) if (l != null) links.Add(l.ToString());
                    s.Links = links.ToArray();
                    var pos = List(m, "pos");
                    if (pos.Count >= 2) s.Pos = new Vector2(ToFloat(pos[0]), ToFloat(pos[1]));
                    if (string.IsNullOrEmpty(s.ShortName)) s.ShortName = s.Name;
                    if (string.IsNullOrEmpty(s.Id) || string.IsNullOrEmpty(s.Tree)) continue;
                    stars.Add(s);
                }
                if (trees.Count == 0 || stars.Count == 0) throw new InvalidDataException("no trees or no stars");

                _trees.Clear(); _stars.Clear(); _byId.Clear(); _treeById.Clear(); _byTree.Clear();
                _trees.AddRange(trees);
                foreach (var t in trees) { _treeById[t.Id] = t; _byTree[t.Id] = new List<ParagonStarDef>(); }
                foreach (var s in stars)
                {
                    if (_byId.ContainsKey(s.Id)) { Debug.LogWarning($"[Paragon] {from}: duplicate star {s.Id} skipped"); continue; }
                    if (!_byTree.TryGetValue(s.Tree, out var list)) { Debug.LogWarning($"[Paragon] {from}: star {s.Id} names unknown tree {s.Tree}; skipped"); continue; }
                    _byId[s.Id] = s; _stars.Add(s); list.Add(s);
                }
                foreach (var s in _stars)
                    foreach (string l in s.Links)
                    {
                        var target = Star(l);
                        if (target == null) Debug.LogWarning($"[Paragon] {from}: {s.Id} links to unknown star {l}");
                        else target.LinkedFrom.Add(s.Id);
                    }
                Revision = ClassTreeLoader.RevisionOf(yaml);
                LoadedFrom = from;
                var counts = new System.Text.StringBuilder();
                foreach (var t in _trees) counts.Append(counts.Length > 0 ? ", " : "").Append(t.Id).Append(' ').Append(_byTree[t.Id].Count);
                if (FiresCore.Logging.FiresLogLevel.DebugOn) Debug.Log($"[Paragon] Loaded revision {Revision} from {from}: {_stars.Count} star(s) ({counts})");
                try { Loaded?.Invoke(); }
                catch (Exception e) { Debug.LogError($"[Paragon] a Loaded subscriber threw ({e.GetType().Name}: {e.Message})"); }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Paragon] {from} could not be read ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }

        private static void SeedOrUpgradeFile()
        {
            string path = FilePath;
            try
            {
                string bundled = ReadBundled();
                if (bundled == null) return;
                if (!File.Exists(path)) { File.WriteAllText(path, bundled); Debug.Log($"[Paragon] Wrote the bundled {FileName} to {path}."); return; }
                int onDisk = ClassTreeLoader.RevisionOf(File.ReadAllText(path)), shipped = ClassTreeLoader.RevisionOf(bundled);
                if (onDisk >= shipped) return;
                // No .bak copy of the old file is kept.
                File.WriteAllText(path, bundled);
                Debug.LogWarning($"[Paragon] {FileName} on disk was revision {onDisk}, older than this version's {shipped}: replaced (no backup copy kept).");
            }
            catch (Exception e) { Debug.LogError($"[Paragon] Could not write {path} ({e.GetType().Name}: {e.Message})."); }
        }

        private static string ReadBundled()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledResourceName))
            {
                if (stream == null) { Debug.LogError($"[Paragon] The bundled {FileName} is missing (EmbeddedResource {BundledResourceName})."); return null; }
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        private static void StartWatching()
        {
            if (_watcher != null) return;
            try
            {
                _watcher = new FileSystemWatcher(FiresConfigPaths.Classes, FileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
                _watcher.Changed += (_, __) => QueueReload();
                _watcher.Created += (_, __) => QueueReload();
                _watcher.Renamed += (_, __) => QueueReload();
            }
            catch (Exception e) { Debug.LogWarning($"[Paragon] Hot reload unavailable ({e.GetType().Name}: {e.Message})."); }
        }

        private static void QueueReload() { _reloadAtUtc = DateTime.UtcNow.AddSeconds(0.5); _reloadPending = true; }

        /// <summary>Once per frame from the class driver (main thread).</summary>
        public static void Tick()
        {
            if (!_reloadPending || DateTime.UtcNow < _reloadAtUtc) return;
            _reloadPending = false;
            Load();
        }

        // ---- yaml helpers ----
        private static object Get(Dictionary<object, object> m, string key) => m.TryGetValue(key, out object v) ? v : null;
        private static string Str(Dictionary<object, object> m, string key) => Get(m, key)?.ToString();
        private static List<object> List(Dictionary<object, object> m, string key) => Get(m, key) as List<object> ?? new List<object>();
        private static int ToInt(object o) => o != null && int.TryParse(o.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static float ToFloat(object o) => o != null && float.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        private static bool ToBool(object o) => o != null && string.Equals(o.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }
}
