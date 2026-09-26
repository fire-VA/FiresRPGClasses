using System;
using System.Collections.Generic;
using FiresCore.Bridge;
using UnityEngine;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// Valheim's own achievements, offered through Core's <see cref="AchievementBridge"/> so the
    /// Achievements board reads them the same way it reads every other catalogue. Read-only: nothing here
    /// unlocks, publishes or writes a stat. Plan: Docs/PLAN_AchievementsBoard.md
    /// </summary>
    public static class VanillaAchievements
    {
        public const string SourceId = "valheim";

        private const string DisplayName = "Valheim";
        private const int DisplayOrder = 20;
        private const int UnknownTier = 9;

        public static void Register()
        {
            if (FiresCore.Lifecycle.FiresMod.IsHeadless) return;
            AchievementBridge.RegisterCatalogue(SourceId, DisplayName, Read, DisplayOrder);
        }

        /// <summary>
        /// Why no further achievement can be earned right now, or empty when they can. The three durable
        /// causes are permanent for this character, world or session; a cheated item is the only one that
        /// clears by itself.
        /// </summary>
        public static string EarningBlockedReason()
        {
            if (PlayerProfile.s_bypassCheatChecks) return string.Empty;

            if (Game.isModded)
                return "a loaded mod has flagged this session as modded, so no stat is recorded";

            if (Game.instance != null)
            {
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile != null && profile.m_usedCheats)
                    return "this character has used cheats - permanent";
            }

            if (WorldIsCheated())
                return "this world carries cheated modifiers - permanent";

            if (Player.m_localPlayer != null)
            {
                Inventory inventory = Player.m_localPlayer.GetInventory();
                if (inventory != null && inventory.AnyCheatedItem())
                    return "a cheated item is in your inventory - clears when you drop it";
            }

            return string.Empty;
        }

        private static bool WorldIsCheated()
        {
            try { return Achievements.IsWorldCheated(); }
            catch { return false; }
        }

        private static IReadOnlyList<AchievementRow> Read()
        {
            var rows = new List<AchievementRow>();

            Achievements achievements = Achievements.m_instance;
            if (achievements == null || achievements.m_achievementLists == null) return rows;

            string blocked = EarningBlockedReason();

            foreach (AchievementList list in achievements.m_achievementLists)
            {
                if (list == null || list.m_achievements == null) continue;
                foreach (Achievement achievement in list.m_achievements)
                {
                    if (achievement == null) continue;
                    rows.Add(ToRow(achievement, blocked));
                }
            }

            rows.Sort(CompareRows);
            return rows;
        }

        private static AchievementRow ToRow(Achievement achievement, string blocked)
        {
            bool earned = achievement.m_unlocked;
            bool hidden = achievement.m_isSecret && !earned;
            int tier = TierOf(achievement);

            return new AchievementRow
            {
                Id = achievement.m_id ?? string.Empty,
                Name = hidden ? "Secret" : Localize(achievement.m_name),
                Description = hidden ? "Earn it to find out." : Localize(achievement.m_description),
                Group = "Tier " + tier,
                Earned = earned,
                Secret = achievement.m_isSecret,
                Score = tier,
                Icon = earned ? achievement.m_icon : achievement.m_iconLocked,
                Accent = AccentForTier(tier),
                Blocked = earned ? string.Empty : blocked
            };
        }

        /// <summary>
        /// The tier is the two-digit prefix on the locked icon's name, the same field vanilla's own list
        /// sorts on. An icon that does not carry one sorts last rather than throwing.
        /// </summary>
        private static int TierOf(Achievement achievement)
        {
            Sprite locked = achievement.m_iconLocked;
            if (locked == null || locked.name == null || locked.name.Length < 2) return UnknownTier;
            return int.TryParse(locked.name.Substring(0, 2), out int tier) ? tier : UnknownTier;
        }

        private static Color AccentForTier(int tier)
        {
            switch (tier)
            {
                case 1: return new Color(0.70f, 0.70f, 0.70f, 1f);
                case 2: return new Color(0.75f, 0.60f, 0.42f, 1f);
                case 3: return new Color(0.60f, 0.75f, 0.85f, 1f);
                case 4: return new Color(0.55f, 0.85f, 0.60f, 1f);
                case 5: return new Color(0.85f, 0.80f, 0.45f, 1f);
                case 6: return new Color(0.85f, 0.60f, 0.85f, 1f);
                case 7: return new Color(0.95f, 0.65f, 0.40f, 1f);
                case 8: return new Color(0.95f, 0.45f, 0.45f, 1f);
                default: return AchievementRow.DefaultAccent;
            }
        }

        private static int CompareRows(AchievementRow a, AchievementRow b)
        {
            int byTier = a.Score.CompareTo(b.Score);
            return byTier != 0 ? byTier : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Localize(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            Localization localization = Localization.instance;
            return localization == null ? token : localization.Localize(token);
        }
    }
}
