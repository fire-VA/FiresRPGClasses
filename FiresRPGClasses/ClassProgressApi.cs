using FiresCore.Classes;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses
{
    /// <summary>
    /// For FiresMonsters' group scaling (boss strength by group size AND player power): each player's class progress
    /// on their own ZDO, so whoever owns a boss can read every player's, wherever they're owned. CONTRACT with FiresMonsters -
    /// never change a key: "rpg_class_level" (1..max), "rpg_class_points" (talent points spent), "rpg_class_maxlevel".
    /// Absent / 0 = no RPGClasses or no class yet ("expected" for FMO). Written by the player's own client when a value changes
    /// (checked once a second); nothing on the dedicated server.
    /// </summary>
    public static class ClassProgressApi
    {
        public const string LevelKey = "rpg_class_level";
        public const string PointsKey = "rpg_class_points";
        public const string MaxLevelKey = "rpg_class_maxlevel";
        private static readonly int LevelHash = LevelKey.GetStableHashCode();
        private static readonly int PointsHash = PointsKey.GetStableHashCode();
        private static readonly int MaxHash = MaxLevelKey.GetStableHashCode();

        private static float _next;
        private static int _level = -1, _points = -1, _max = -1;

        /// <summary>Called every frame by ClassCombatDriver; publishes once a second, only when a value changed.</summary>
        internal static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            Player me = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            ZNetView nview = me != null ? me.GetComponent<ZNetView>() : null;
            if (data == null || nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            int level = data.Progress.Level, points = data.Progress.SpentSkillPoints, max = ProgressionEngine.MaxLevel;
            if (level == _level && points == _points && max == _max) return;
            ZDO zdo = nview.GetZDO();
            zdo.Set(LevelHash, level);
            zdo.Set(PointsHash, points);
            zdo.Set(MaxHash, max);
            _level = level; _points = points; _max = max;
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassProgress] published level {level}/{max}, {points} talent point(s) spent");
        }

        /// <summary>Any client: a player's published class progress (false = nothing published).</summary>
        public static bool TryGet(Player p, out int level, out int pointsSpent, out int maxLevel)
        {
            level = pointsSpent = maxLevel = 0;
            ZNetView nview = p != null ? p.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid()) return false;
            ZDO zdo = nview.GetZDO();
            level = zdo.GetInt(LevelHash);
            pointsSpent = zdo.GetInt(PointsHash);
            maxLevel = zdo.GetInt(MaxHash);
            return level > 0 && maxLevel > 0;
        }

        /// <summary>Any client: level / max level, 0 when nothing is published.</summary>
        public static float Progress01(Player p) =>
            TryGet(p, out int level, out _, out int max) ? Mathf.Clamp01((float)level / max) : 0f;
    }
}