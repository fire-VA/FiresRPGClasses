using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FiresCore.Npc.Archetypes;
using YamlDotNet.Serialization;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// Reads the eight skill trees from classes-trees.yaml. The yaml is the data of record: node
    /// placement, ranks, levels, targeting, variants and the rank riders all come from it, and nothing
    /// here invents a value the file does not carry. Plan: Docs/PLAN_ClassTreeLoader.md
    /// </summary>
    public class YamlClassTreeSource : IClassTreeSource
    {
        public const int SupportedSchema = 5;

        private const string AllPathsId = ClassTreeNode.AllPaths;
        private const int UltimateOneUnlockLevel = 10;

        private readonly string _path;
        private readonly string _yaml;

        public string SourceName { get; private set; }

        /// <summary>Problems found while reading. A tree still loads; a caller decides how loud to be.</summary>
        public readonly List<string> Warnings = new List<string>();

        public int Schema { get; private set; }

        public YamlClassTreeSource(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _yaml = File.ReadAllText(path);
            SourceName = Path.GetFileName(path);
        }

        private YamlClassTreeSource(string yaml, string sourceName)
        {
            _yaml = yaml ?? throw new ArgumentNullException(nameof(yaml));
            _path = "";
            SourceName = sourceName;
        }

        public static YamlClassTreeSource FromText(string yaml, string sourceName) =>
            new YamlClassTreeSource(yaml, sourceName);

        public IEnumerable<ClassTree> Load()
        {
            Warnings.Clear();

            var root = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<Dictionary<object, object>>(new StringReader(_yaml));

            if (root == null) throw new InvalidDataException($"{SourceName} is empty or not a YAML mapping.");

            Schema = ReadInt(root, "schema", 0);
            if (Schema != SupportedSchema)
                Warnings.Add($"schema is {Schema}, this loader was written for {SupportedSchema} - "
                    + "fields it does not know are ignored and fields it expects may be missing.");

            var trees = new List<ClassTree>();
            var byArchetype = new Dictionary<ArchetypeClass, ClassTree>();

            foreach (object entry in ReadList(root, "classes"))
            {
                ClassTree tree = ReadTree(AsMap(entry));
                if (tree == null) continue;
                trees.Add(tree);
                byArchetype[tree.Archetype] = tree;
            }

            ReadUltimates(root, byArchetype);
            return trees;
        }

        #region classes

        private ClassTree ReadTree(Dictionary<object, object> map)
        {
            if (map == null) return null;

            string className = ReadString(map, "class");
            if (!TryParseArchetype(className, out ArchetypeClass archetype))
            {
                Warnings.Add($"unknown class '{className}' - skipped with all its nodes.");
                return null;
            }

            var tree = new ClassTree { Archetype = archetype };

            foreach (object entry in ReadList(map, "paths"))
            {
                var pathMap = AsMap(entry);
                if (pathMap == null) continue;
                tree.Paths.Add(new ClassTreePath
                {
                    Id = ReadString(pathMap, "id"),
                    DisplayName = ReadString(pathMap, "name"),
                    Theme = ReadString(pathMap, "theme"),
                });
            }

            foreach (object entry in ReadList(map, "nodes"))
            {
                ClassTreeNode node = ReadNode(AsMap(entry), archetype, tree);
                if (node != null) tree.Nodes.Add(node);
            }

            return tree;
        }

        private ClassTreeNode ReadNode(Dictionary<object, object> map, ArchetypeClass archetype, ClassTree tree)
        {
            if (map == null) return null;

            string id = ReadString(map, "id");
            if (id.Length == 0)
            {
                Warnings.Add($"{archetype}: a node has no id - skipped.");
                return null;
            }

            string kind = ReadString(map, "kind");
            string slotText = ReadString(map, "slot");
            string pathId = ReadString(map, "path");
            if (pathId.Length == 0) pathId = AllPathsId;

            if (pathId != AllPathsId && !HasPath(tree, pathId))
                Warnings.Add($"{id}: path '{pathId}' is not declared for {archetype}.");

            var node = new ClassTreeNode
            {
                Id = id,
                DisplayName = ReadString(map, "name"),
                Description = ReadString(map, "text"),
                Archetype = archetype,
                PathId = pathId,
                Tier = ParseTier(ReadString(map, "tier"), id),
                MinLevel = ReadInt(map, "level", 1),
                MaxRank = Math.Max(1, ReadInt(map, "ranks", 1)),
                Slot = ParseSlot(slotText, kind, id),
                VariantGroup = ReadString(map, "variant"),
                RequiresNodeId = ReadString(map, "after"),
                Cooldown = ReadFloat(map, "cooldown", 0f),
                CostText = ReadString(map, "cost"),
                EffectDuration = ReadFloat(map, "duration", 0f),
            };

            node.Targeting = ReadTargeting(map, node);
            ReadRankEffects(map, node);
            ReadValues(map, node);
            return node;
        }

        /// <summary>
        /// The node's `values:` block, one list per key with an entry per rank ("15%" reads as 0.15). A single value serves
        /// every rank.
        /// </summary>
        private void ReadValues(Dictionary<object, object> map, ClassTreeNode node)
        {
            var values = AsMap(Read(map, "values"));
            if (values == null) return;
            foreach (KeyValuePair<object, object> entry in values)
            {
                string key = entry.Key?.ToString() ?? "";
                if (key.Length == 0) continue;
                List<object> list = entry.Value as List<object> ?? new List<object> { entry.Value };
                var parsed = new List<float>();
                foreach (object item in list)
                {
                    string text = (item?.ToString() ?? "").Trim();
                    bool percent = text.EndsWith("%", StringComparison.Ordinal);
                    if (percent) text = text.Substring(0, text.Length - 1);
                    // Seconds read as numbers: "3 s" -> 3 (Smoke Bomb's stealth).
                    else if (text.EndsWith("s", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).TrimEnd();
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        parsed.Add(percent ? number / 100f : number);
                    else
                        Warnings.Add($"{node.Id}: value '{item}' of '{key}' is not a number - dropped.");
                }
                if (parsed.Count > 0) node.Values[key] = parsed.ToArray();
            }
        }

        private TargetingSpec ReadTargeting(Dictionary<object, object> map, ClassTreeNode node)
        {
            var targeting = AsMap(Read(map, "targeting"));
            if (targeting == null)
                return new TargetingSpec(node.IsPassive ? TargetingMode.Automatic : TargetingMode.Self);

            string modeText = ReadString(targeting, "mode");
            if (!TryParseEnumLabel(modeText, out TargetingMode mode))
            {
                Warnings.Add($"{node.Id}: unknown targeting mode '{modeText}' - treated as Self.");
                mode = TargetingMode.Self;
            }

            return new TargetingSpec(mode, ReadFloat(targeting, "range", 0f), ReadFloat(targeting, "radius", 0f));
        }

        private void ReadRankEffects(Dictionary<object, object> map, ClassTreeNode node)
        {
            foreach (object entry in ReadList(map, "rankEffects"))
            {
                var riderMap = AsMap(entry);
                if (riderMap == null) continue;

                int rank = ReadInt(riderMap, "rank", 0);
                if (rank < 1 || rank > node.MaxRank)
                {
                    Warnings.Add($"{node.Id}: rank rider at rank {rank} is outside its 1..{node.MaxRank} ranks - dropped.");
                    continue;
                }

                node.RankEffects.Add(new NodeRankEffect
                {
                    Rank = rank,
                    Counter = ReadString(riderMap, "counter"),
                    Effect = ReadString(riderMap, "effect"),
                });
            }
        }

        #endregion

        #region ultimates

        /// <summary>
        /// The ultimates live in their own top-level section, two per archetype, and are the slot-8
        /// nodes. Without them an archetype has no Ultimate I at all.
        /// </summary>
        private void ReadUltimates(Dictionary<object, object> root, Dictionary<ArchetypeClass, ClassTree> byArchetype)
        {
            var ultimates = AsMap(Read(root, "ultimates"));
            if (ultimates == null)
            {
                Warnings.Add("no 'ultimates' section - no archetype will have an ultimate.");
                return;
            }

            var archetypes = AsMap(Read(ultimates, "archetypes"));
            if (archetypes == null)
            {
                Warnings.Add("'ultimates' has no 'archetypes' map - no archetype will have an ultimate.");
                return;
            }

            foreach (KeyValuePair<object, object> pair in archetypes)
            {
                string className = pair.Key?.ToString() ?? "";
                if (!TryParseArchetype(className, out ArchetypeClass archetype))
                {
                    Warnings.Add($"ultimates: unknown class '{className}' - its ultimates are skipped.");
                    continue;
                }

                if (!byArchetype.TryGetValue(archetype, out ClassTree tree))
                {
                    Warnings.Add($"ultimates: {archetype} has ultimates but no tree - skipped.");
                    continue;
                }

                foreach (object entry in AsList(pair.Value))
                {
                    ClassTreeNode node = ReadUltimate(AsMap(entry), archetype, tree);
                    if (node != null) tree.Nodes.Add(node);
                }
            }
        }

        /// <summary>
        /// Every ultimate is declared twice on purpose: the class tree places it (tier, path, level,
        /// ranks) and this section gives it its resource cost, cooldown and morphs. So this ENRICHES the
        /// tree's node rather than adding a second one; only an ultimate the tree never placed is added.
        /// </summary>
        private ClassTreeNode ReadUltimate(Dictionary<object, object> map, ArchetypeClass archetype, ClassTree tree)
        {
            if (map == null) return null;

            string id = ReadString(map, "id");
            if (id.Length == 0)
            {
                Warnings.Add($"{archetype}: an ultimate has no id - skipped.");
                return null;
            }

            var cost = AsMap(Read(map, "cost"));
            string costText = cost == null
                ? ""
                : string.Format(CultureInfo.InvariantCulture, "{0} {1}",
                    ReadInt(cost, "amount", 0), ReadString(cost, "resource")).Trim();
            float cooldown = ReadFloat(map, "cooldown", 0f);
            int unlock = ReadInt(map, "unlock", UltimateOneUnlockLevel);

            ClassTreeNode placed = FindNode(tree, id);
            if (placed != null)
            {
                if (costText.Length > 0) placed.CostText = costText;
                if (cooldown > 0f) placed.Cooldown = cooldown;
                placed.MinLevel = unlock;
                if (placed.Slot != NodeSlot.Ultimate)
                    Warnings.Add($"{id}: listed as an ultimate but its tree node is slotted as {placed.Slot}.");
                return null;
            }

            Warnings.Add($"{id}: an ultimate that no tree node places - added at the edge of {archetype}'s tree.");
            return new ClassTreeNode
            {
                Id = id,
                DisplayName = ReadString(map, "name"),
                Archetype = archetype,
                PathId = AllPathsId,
                Tier = NodeTier.Ultimate,
                MinLevel = unlock,
                MaxRank = 1,
                Slot = NodeSlot.Ultimate,
                Targeting = new TargetingSpec(TargetingMode.Self),
                Cooldown = cooldown,
                CostText = costText,
            };
        }

        #endregion

        #region yaml reading

        /// <summary>
        /// The yaml is written for people, so its enum labels carry spaces: "Around you", "Next attack".
        /// Enum.TryParse matches neither, and silently taking the fallback turned 25 abilities into Self on
        /// 2026-09-24. Every enum label the yaml can name is read through here, so a spaced, hyphenated or
        /// underscored spelling resolves while a genuinely unknown word still fails and warns.
        /// </summary>
        private static bool TryParseEnumLabel<T>(string text, out T value) where T : struct
        {
            value = default(T);
            if (string.IsNullOrEmpty(text)) return false;
            string collapsed = text.Replace(" ", "").Replace("-", "").Replace("_", "");
            return Enum.TryParse(collapsed, true, out value);
        }

        private static bool TryParseArchetype(string name, out ArchetypeClass archetype)
        {
            archetype = ArchetypeClass.None;
            if (string.IsNullOrEmpty(name)) return false;
            return TryParseEnumLabel(name, out archetype) && archetype != ArchetypeClass.None;
        }

        private NodeTier ParseTier(string tier, string nodeId)
        {
            if (TryParseEnumLabel(tier, out NodeTier parsed)) return parsed;
            Warnings.Add($"{nodeId}: unknown tier '{tier}' - treated as Basic.");
            return NodeTier.Basic;
        }

        /// <summary>
        /// The yaml's slot vocabulary is auto / skill / ultimate / dodge, and a node with no slot is
        /// decided by its kind.
        /// </summary>
        private NodeSlot ParseSlot(string slot, string kind, string nodeId)
        {
            switch ((slot ?? "").ToLowerInvariant())
            {
                case "auto": return NodeSlot.Automatic;
                case "skill": return NodeSlot.Skill;
                case "ultimate": return NodeSlot.Ultimate;
                case "dodge": return NodeSlot.Evasion;
                case "passive": return NodeSlot.Passive;
                case "": break;
                default:
                    Warnings.Add($"{nodeId}: unknown slot '{slot}' - decided from kind '{kind}' instead.");
                    break;
            }

            if (string.Equals(kind, "passive", StringComparison.OrdinalIgnoreCase)) return NodeSlot.Passive;
            if (string.Equals(kind, "active", StringComparison.OrdinalIgnoreCase)) return NodeSlot.Skill;

            Warnings.Add($"{nodeId}: no slot and unknown kind '{kind}' - treated as a passive.");
            return NodeSlot.Passive;
        }

        private static bool HasPath(ClassTree tree, string pathId)
        {
            foreach (ClassTreePath path in tree.Paths)
                if (path.Id == pathId) return true;
            return false;
        }

        private static ClassTreeNode FindNode(ClassTree tree, string nodeId)
        {
            foreach (ClassTreeNode node in tree.Nodes)
                if (node.Id == nodeId) return node;
            return null;
        }

        private static Dictionary<object, object> AsMap(object value) => value as Dictionary<object, object>;

        private static IEnumerable<object> AsList(object value) =>
            value as List<object> ?? (IEnumerable<object>)Array.Empty<object>();

        private static object Read(Dictionary<object, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value : null;

        private static IEnumerable<object> ReadList(Dictionary<object, object> map, string key) =>
            AsList(Read(map, key));

        private static string ReadString(Dictionary<object, object> map, string key) =>
            Read(map, key)?.ToString() ?? "";

        private static int ReadInt(Dictionary<object, object> map, string key, int fallback) =>
            int.TryParse(ReadString(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;

        private static float ReadFloat(Dictionary<object, object> map, string key, float fallback) =>
            float.TryParse(ReadString(map, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;

        #endregion
    }
}
