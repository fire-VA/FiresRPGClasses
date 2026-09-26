using System.Collections.Generic;
using System.Linq;
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
                for (int rank = 0; rank < node.MaxRank; rank++)
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
        public static readonly int[] RankCosts = { 1, 2, 3 };

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
                    NodesById[node.Id] = node;
            }
            LoadedFrom = source.SourceName;
        }

        public static ClassTree Tree(ArchetypeClass archetype) =>
            Trees.TryGetValue(archetype, out ClassTree tree) ? tree : null;

        public static ClassTreeNode Node(string nodeId) =>
            nodeId != null && NodesById.TryGetValue(nodeId, out ClassTreeNode node) ? node : null;

        public static IEnumerable<ClassTreeNode> AllNodes => NodesById.Values;

        public static int TotalPointCostOfEveryTree() =>
            Trees.Values.Sum(tree => tree.TotalPointCost(RankCosts));

        public static int RankCost(int nextRank) =>
            RankCosts[nextRank - 1 < RankCosts.Length ? nextRank - 1 : RankCosts.Length - 1];

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
