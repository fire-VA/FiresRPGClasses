using System.Collections.Generic;
using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 12's Skills tab: Valheim's own skills, shown here because the inventory's Skills button now opens
    /// this window instead of the vanilla dialog. Read-only - raising a skill is still the game's job.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class SkillsView : IClassesView
    {
        private const float HeaderTextHeight = 34f;
        private const float SkillRowHeight = 30f;
        private const float MaxSkillLevel = 100f;

        public ClassesTab Tab => ClassesTab.Skills;

        public string Title => "Skills";

        private RectTransform _page;
        private UIBuilderHelper.ScrollAreaResult _scroll;
        private TextMeshProUGUI _header;

        public void Build(RectTransform page)
        {
            _page = page;
            _header = ClassesTheme.Text(page, "SkillHeader", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.RowFontSize);
            _header.rectTransform.Fill(-6f, HeaderTextHeight);

            _scroll = UIBuilderHelper.CreateScrollArea(page, "SkillScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            _scroll.Root.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -(HeaderTextHeight + 10f));
        }

        public void Refresh()
        {
            if (_page == null) return;
            ClassesTheme.ClearChildren(_scroll.Content);

            Player player = Player.m_localPlayer;
            Skills skills = player == null ? null : player.GetSkills();
            if (skills == null)
            {
                _header.text = "No character loaded.";
                ClassesTheme.SetContentHeight(_scroll.Content, 0, SkillRowHeight);
                return;
            }

            List<Skills.Skill> list = skills.GetSkillList();
            int total = 0;
            int row = 0;
            foreach (Skills.Skill skill in list)
            {
                if (skill == null || skill.m_info == null) continue;
                total += Mathf.FloorToInt(skill.m_level);
                row = SkillRow(row, skills, skill);
            }

            _header.text = "Valheim skills   " + list.Count + " tracked, " + total + " levels total";
            ClassesTheme.SetContentHeight(_scroll.Content, row, SkillRowHeight);
        }

        private int SkillRow(int index, Skills skills, Skills.Skill skill)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content,
                "Skill_" + skill.m_info.m_skill, index++, SkillRowHeight);

            float baseLevel = Mathf.Floor(skill.m_level);
            float effective = skills.GetSkillLevel(skill.m_info.m_skill);

            ClassesTheme.Text(row, "Name", NameOf(skill), UIFontConfig.Colors.Gold,
                new Vector2(0.03f, 0f), new Vector2(0.55f, 1f), ClassesTheme.RowFontSize);

            ClassesTheme.Text(row, "Level", Mathf.FloorToInt(baseLevel).ToString(),
                ClassesTheme.Ready, new Vector2(0.55f, 0f), new Vector2(0.68f, 1f),
                ClassesTheme.RowFontSize, TextAlignmentOptions.MidlineRight);

            if (!Mathf.Approximately(effective, baseLevel))
            {
                float delta = effective - baseLevel;
                ClassesTheme.Text(row, "Bonus", delta.ToString("+0;-0"),
                    delta > 0f ? ClassesTheme.Good : ClassesTheme.Locked,
                    new Vector2(0.69f, 0f), new Vector2(0.78f, 1f), ClassesTheme.SmallFontSize);
            }

            ClassesTheme.Text(row, "Share", Mathf.RoundToInt(effective / MaxSkillLevel * 100f) + "%",
                ClassesTheme.Dim, new Vector2(0.80f, 0f), new Vector2(0.98f, 1f),
                ClassesTheme.SmallFontSize, TextAlignmentOptions.MidlineRight);

            return index;
        }

        private static string NameOf(Skills.Skill skill)
        {
            string token = "$skill_" + skill.m_info.m_skill.ToString().ToLower();
            Localization localization = Localization.instance;
            string name = localization == null ? token : localization.Localize(token);
            return name == token ? skill.m_info.m_skill.ToString() : name;
        }
    }
}
