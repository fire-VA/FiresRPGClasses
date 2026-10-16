using System.Collections.Generic;
using System.Linq;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;

namespace FiresRPGClasses.Data
{
    public enum NodeTier
    {
        Starter,
        Basic,
        Advanced,
        Expert,
        Master,
        Ultimate
    }

    public enum NodeSlot
    {
        Passive,
        Automatic,
        Skill,
        Ultimate,
        Evasion
    }

    public enum TargetingMode
    {
        Self,
        AroundYou,
        Melee,
        Aimed,
        Placed,
        Ally,
        NextAttack,
        Dash,
        Dodge,
        Automatic
    }

    public class TargetingSpec
    {
        public TargetingMode Mode = TargetingMode.Self;
        public float Range;
        public float Radius;

        public TargetingSpec() { }

        public TargetingSpec(TargetingMode mode, float range = 0f, float radius = 0f)
        {
            Mode = mode;
            Range = range;
            Radius = radius;
        }

        public bool NeedsGroundPoint => Mode == TargetingMode.Placed;
        public bool NeedsAim => Mode == TargetingMode.Aimed || Mode == TargetingMode.Dash;
        public bool NeedsAlly => Mode == TargetingMode.Ally;
    }

    /// <summary>One tree node: the unit a talent point buys and, when it is an active, the thing a bar slots.</summary>
    public class ClassTreeNode
    {
        public string Id = "";
        public string DisplayName = "";
        public string Description = "";
        public ArchetypeClass Archetype;
        public string PathId = AllPaths;
        public NodeTier Tier;
        public int MinLevel = 1;
        public int MaxRank = 1;
        public NodeSlot Slot = NodeSlot.Skill;
        public TargetingSpec Targeting = new TargetingSpec();
        public string VariantGroup = "";
        public string RequiresNodeId = "";
        public float Cooldown;
        public string CostText = "";
        // 0.1.104: an ultimate's yaml "ultimates:" entry, kept for UltimateCatalog (the catalog's data gap: only 18 were hand-written).
        public string UltimateResource = "";
        public float UltimateAmount;
        public bool UltimatePercent;
        public readonly System.Collections.Generic.List<(string morph, string capstone)> UltimateMorphs = new System.Collections.Generic.List<(string, string)>();
        public string IconColor = "#FFFFFF";

        /// <summary>Core status-effect name this node applies. Empty until the tree data names one.</summary>
        public string EffectName = "";

        public float EffectDuration;

        /// <summary>
        /// Riders a node gains at a given rank and keeps above it. They never change a node's rank
        /// count or point cost, which is how the counters in doc 21 were added without moving the
        /// 612-point budget.
        /// </summary>
        public readonly List<NodeRankEffect> RankEffects = new List<NodeRankEffect>();

        /// <summary>The rider that applies at this rank, or null.</summary>
        public NodeRankEffect RankEffectAt(int rank)
        {
            foreach (NodeRankEffect rider in RankEffects)
                if (rider.Rank == rank) return rider;
            return null;
        }

        /// <summary>Every rider a node has earned by this rank, lowest rank first.</summary>
        public IEnumerable<NodeRankEffect> RankEffectsUpTo(int rank) =>
            RankEffects.Where(rider => rider.Rank <= rank).OrderBy(rider => rider.Rank);

        /// <summary>The yaml's `values:` per key, an entry per rank; a percentage is already a fraction (15% = 0.15).</summary>
        public readonly Dictionary<string, float[]> Values = new Dictionary<string, float[]>();

        /// <summary>A value at a rank (1-based; past the list, the last entry), or the fallback when the node has no such key.</summary>
        public float Value(string key, int rank, float fallback = 0f)
        {
            if (!Values.TryGetValue(key, out float[] ranks) || ranks.Length == 0) return fallback;
            if (rank > ranks.Length && ranks.Length >= 2) return Extrapolate(ranks, rank);   // 0.1.132: Paragon ranks past the table
            int index = System.Math.Max(0, System.Math.Min(rank, ranks.Length) - 1);
            return ranks[index];
        }

        /// <summary>
        /// 0.1.132 (plan §3): a rank past the yaml table extends the last step through Core's SoftCap. The first extra rank gets
        /// the full step, later ones less (logarithmic, never nothing). A value falling toward zero (a cooldown, a cost) instead
        /// approaches 10 % of its last value asymptotically and never crosses zero.
        /// </summary>
        internal static float Extrapolate(float[] ranks, int rank)
        {
            float last = ranks[ranks.Length - 1], step = last - ranks[ranks.Length - 2];
            if (step == 0f || float.IsNaN(step)) return last;
            float extra = System.Math.Abs(step) * (rank - ranks.Length);
            bool towardZero = last != 0f && System.Math.Sign(step) != System.Math.Sign(last);
            if (towardZero)
            {
                float room = System.Math.Abs(last) * 0.9f;
                float knee = System.Math.Min(System.Math.Abs(step), room * 0.5f);
                return last - System.Math.Sign(last) * FiresCore.Curves.SoftCap.Asymptote(extra, knee, room);
            }
            return last + System.Math.Sign(step) * FiresCore.Curves.SoftCap.Soft(extra, System.Math.Abs(step));
        }

        private static readonly System.Text.RegularExpressions.Regex ValueSlot =
            new System.Text.RegularExpressions.Regex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}");

        /// <summary>
        /// The yaml text with each {key} filled in from `values:` at this rank (an unlearned node reads rank 1), written the way the
        /// cards' rank table writes it. A {key} the node has no values for stays as written. (Fire 10-01: the cards showed "{stagger}".)
        /// </summary>
        public string DescriptionAt(int rank)
        {
            if (string.IsNullOrEmpty(Description) || Description.IndexOf('{') < 0) return Description;
            int at = System.Math.Max(1, rank);
            return ValueSlot.Replace(Description, slot =>
            {
                string key = slot.Groups[1].Value;
                return Values.TryGetValue(key, out float[] ranks) && ranks.Length > 0 ? FormatValue(key, Value(key, at)) : slot.Value;
            });
        }

        /// <summary>A value as the Classes window writes it: a fraction (or a pct/percent/chance key) as a percentage, else a number.</summary>
        public static string FormatValue(string key, float value)
        {
            string lower = (key ?? "").ToLowerInvariant();
            bool percent = lower.Contains("pct") || lower.Contains("percent") || lower.Contains("chance")
                           || (value > 0f && value < 1f && !lower.Contains("second") && !lower.Contains("duration"));
            return percent ? UnityEngine.Mathf.RoundToInt(value * 100f) + "%" : value.ToString("0.##");
        }

        public const string AllPaths = "all";

        public bool IsActive => Slot == NodeSlot.Skill || Slot == NodeSlot.Ultimate;
        public bool IsPassive => Slot == NodeSlot.Passive || Slot == NodeSlot.Automatic;
        public bool CountsForPath(string pathId) => PathId == AllPaths || PathId == pathId;
    }

    /// <summary>A counter a node gains at a rank - anti-heal, shield breaking, an interrupt and so on.</summary>
    public class NodeRankEffect
    {
        public int Rank;
        public string Counter = "";
        public string Effect = "";
    }

    public class ClassTreePath
    {
        public string Id = "";
        public string DisplayName = "";
        public string Theme = "";
    }

    public class ClassTree
    {
        public ArchetypeClass Archetype;
        public readonly List<ClassTreePath> Paths = new List<ClassTreePath>();
        public readonly List<ClassTreeNode> Nodes = new List<ClassTreeNode>();

        /// <summary>
        /// Talent points needed to fill this tree. Ultimates are excluded: they are not bought with
        /// points, they level by use, so counting them overstates the budget by 7 per tree.
        /// </summary>
        public int TotalPointCost(IReadOnlyList<int> rankCosts)
        {
            int total = 0;
            foreach (ClassTreeNode node in Nodes)
            {
                if (node.Slot == NodeSlot.Ultimate) continue;
                // 0.1.132: ranks from ParagonTalentFromRank up are paid in Paragon points, not talent points.
                int talentRanks = System.Math.Min(node.MaxRank, ClassProgressState.ParagonTalentFromRank - 1);
                for (int rank = 0; rank < talentRanks; rank++)
                    total += rankCosts[rank < rankCosts.Count ? rank : rankCosts.Count - 1];
            }
            return total;
        }
    }

    /// <summary>
    /// Where tree data comes from. The yaml loader is the real implementation; RegistryTreeSource stands in
    /// until it lands. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public interface IClassTreeSource
    {
        string SourceName { get; }
        IEnumerable<ClassTree> Load();
    }

    public static class ClassTrees
    {
        // Core owns the prices (Fire 2026-09-29: ranks cost 1/2/3, as the board shows); the board and Core must agree.
        public static int[] RankCosts => ClassProgressState.RankCosts;

        public const int MasterPointsRequiredInPath = 10;

        private static readonly Dictionary<ArchetypeClass, ClassTree> Trees =
            new Dictionary<ArchetypeClass, ClassTree>();

        private static readonly Dictionary<string, ClassTreeNode> NodesById =
            new Dictionary<string, ClassTreeNode>();

        public static string LoadedFrom { get; private set; } = "";

        public static IEnumerable<ArchetypeClass> Archetypes => Trees.Keys;

        public static bool IsLoaded => Trees.Count > 0;

        public static void Load(IClassTreeSource source)
        {
            Trees.Clear();
            NodesById.Clear();
            foreach (ClassTree tree in source.Load())
            {
                Trees[tree.Archetype] = tree;
                foreach (ClassTreeNode node in tree.Nodes)
                {
                    NodesById[node.Id] = node;
                    if (node.IsActive && node.Cooldown <= 0f && node.Targeting.Mode != TargetingMode.Dodge
                        && node.Targeting.Mode != TargetingMode.Automatic)
                        node.Cooldown = CoreCompanionCooldown(node);
                }
            }
            LoadedFrom = source.SourceName;
        }

        /// <summary>
        /// A Core-backed skill's yaml node carries no cooldown (its numbers live in Core), so it had NONE for players and could be
        /// cast every frame (class_test class_drill, R63: Inner Peace on consecutive ticks). It takes Core's own companion
        /// cooldown for its kind (ArchetypeAbilitySystem's defaults: self buff 20 s, group 30 s, utility 8 s, expert 45 s, master
        /// 120 s, ultimate 300 s) - Fire's rule that Core-backed numbers come from Core. Listed in CLASS_KIT_DESIGN_GAPS.md.
        /// </summary>
        private static float CoreCompanionCooldown(ClassTreeNode node)
        {
            if (node.Slot == NodeSlot.Ultimate || node.Tier == NodeTier.Ultimate) return 300f;
            if (node.Tier == NodeTier.Master) return 120f;
            if (node.Tier == NodeTier.Expert) return 45f;
            switch (node.Targeting.Mode)
            {
                case TargetingMode.Self: return 20f;
                case TargetingMode.AroundYou: return 30f;
                default: return 8f;
            }
        }

        public static ClassTree Tree(ArchetypeClass archetype) =>
            Trees.TryGetValue(archetype, out ClassTree tree) ? tree : null;

        public static ClassTreeNode Node(string nodeId) =>
            nodeId != null && NodesById.TryGetValue(nodeId, out ClassTreeNode node) ? node : null;

        public static IEnumerable<ClassTreeNode> AllNodes => NodesById.Values;

        public static int TotalPointCostOfEveryTree() =>
            Trees.Values.Sum(tree => tree.TotalPointCost(RankCosts));

        /// <summary>Talent points a rank costs; 0 for a rank paid in Paragon points (0.1.132: ParagonTalentFromRank and up).</summary>
        public static int RankCost(int nextRank) =>
            nextRank >= ClassProgressState.ParagonTalentFromRank ? 0 : ClassProgressState.RankCost(nextRank);

        /// <summary>0.1.132: buying <paramref name="nextRank"/> costs 1 Paragon point instead of talent points.</summary>
        public static bool IsParagonRank(int nextRank) => nextRank >= ClassProgressState.ParagonTalentFromRank;

        public static IEnumerable<ClassTreeNode> ActivesFor(ArchetypeClass archetype)
        {
            ClassTree tree = Tree(archetype);
            if (tree == null) return Enumerable.Empty<ClassTreeNode>();
            return tree.Nodes.Where(node => node.Slot == NodeSlot.Skill);
        }

        public static IEnumerable<ClassTreeNode> UltimatesFor(ArchetypeClass archetype)
        {
            ClassTree tree = Tree(archetype);
            if (tree == null) return Enumerable.Empty<ClassTreeNode>();
            return tree.Nodes.Where(node => node.Slot == NodeSlot.Ultimate).OrderBy(node => node.MinLevel);
        }

        public static ClassTreeNode EvasionFor(ArchetypeClass archetype)
        {
            ClassTree tree = Tree(archetype);
            return tree?.Nodes.FirstOrDefault(node => node.Slot == NodeSlot.Evasion);
        }

        public static IEnumerable<ClassTreeNode> PassivesFor(ArchetypeClass archetype)
        {
            ClassTree tree = Tree(archetype);
            if (tree == null) return Enumerable.Empty<ClassTreeNode>();
            return tree.Nodes.Where(node => node.IsPassive);
        }
    }
}
