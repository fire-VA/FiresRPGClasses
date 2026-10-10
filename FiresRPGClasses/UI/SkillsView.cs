using System.Collections.Generic;
using System.Linq;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The Skills tab, from Fire's board Main: category pills (All, Combat, Gathering, Movement, Crafting), a two-column
    /// grid of Valheim's skills (icon, name, level, a bar to the next level), and a detail panel for the selected skill
    /// (level, "% to level N", what it does, and any bonus from effects). The board's "Class bonus" line has no data
    /// yet, so it is left out.
    ///
    /// Names: pills "SkillCat_&lt;name&gt;", rows "Skill_&lt;SkillType&gt;" (as before). Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class SkillsView : IClassesView
    {
        private const float Pad = 16f;
        private const float DetailWidth = 288f;
        private const float RowHeight = 54f;
        private const string All = "All";

        private static readonly Dictionary<string, string[]> Categories = new Dictionary<string, string[]>
        {
            { "Combat", new[] { "Swords", "Knives", "Clubs", "Polearms", "Spears", "Axes", "Bows", "Crossbows",
                "ElementalMagic", "BloodMagic", "Unarmed", "Blocking" } },
            { "Gathering", new[] { "WoodCutting", "Pickaxes", "Fishing", "Farming" } },
            { "Movement", new[] { "Run", "Jump", "Swim", "Sneak", "Ride", "Dodge" } },
            { "Crafting", new[] { "Cooking", "Crafting" } },
        };

        private static readonly string[] Order = { All, "Combat", "Gathering", "Movement", "Crafting", "Other" };

        public ClassesTab Tab => ClassesTab.Skills;

        public string Title => "Skills";

        private RectTransform _page;
        private RectTransform _pills;
        private RectTransform _detail;
        private UIBuilderHelper.ScrollAreaResult _grid;
        private string _category = All;
        private string _selected;

        public void Build(RectTransform page)
        {
            _page = page;
            _pills = ClassesStyle.Rect(page, "SkillCategories", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _pills.pivot = new Vector2(0.5f, 1f);
            _pills.offsetMin = new Vector2(Pad, -8f - ClassesStyle.Target);
            _pills.offsetMax = new Vector2(-Pad - DetailWidth - 16f, -8f);

            _detail = ClassesStyle.Rect(page, "SkillDetail", new Vector2(1f, 0f), new Vector2(1f, 1f));
            _detail.pivot = new Vector2(1f, 0.5f);
            _detail.offsetMin = new Vector2(-Pad - DetailWidth, 12f);
            _detail.offsetMax = new Vector2(-Pad, -8f);
            ClassesStyle.WellFrame(_detail);

            _grid = UIBuilderHelper.CreateScrollArea(page, "SkillGrid", Vector2.zero, Vector2.one, Color.clear);
            RectTransform grid = _grid.Root.GetComponent<RectTransform>();
            grid.offsetMin = new Vector2(Pad - 5f, 8f);
            grid.offsetMax = new Vector2(-Pad - DetailWidth - 16f + 18f, -8f - ClassesStyle.Target - 10f);
        }

        public void Refresh()
        {
            if (_page == null) return;
            Player player = Player.m_localPlayer;
            Skills skills = player == null ? null : player.GetSkills();
            List<Skills.Skill> list = skills == null ? new List<Skills.Skill>()
                : skills.GetSkillList().Where(s => s?.m_info != null).ToList();

            List<string> present = Order.Where(c => c == All || list.Any(s => CategoryOf(s) == c)).ToList();
            if (!present.Contains(_category)) _category = All;
            List<Skills.Skill> shown = list.Where(s => _category == All || CategoryOf(s) == _category).ToList();
            if (_selected == null || list.All(s => s.m_info.m_skill.ToString() != _selected))
                _selected = list.OrderByDescending(s => s.m_level).FirstOrDefault()?.m_info.m_skill.ToString();

            BuildPills(present);
            BuildGrid(skills, shown);
            BuildDetail(skills, list.FirstOrDefault(s => s.m_info.m_skill.ToString() == _selected));
        }

        private static string CategoryOf(Skills.Skill skill)
        {
            string name = skill.m_info.m_skill.ToString();
            foreach (KeyValuePair<string, string[]> category in Categories)
                if (category.Value.Contains(name)) return category.Key;
            return "Other";
        }

        private void BuildPills(List<string> present)
        {
            ClassesStyle.Clear(_pills);
            float x = 0f;
            foreach (string category in present)
            {
                string chosen = category;
                Button pill = ClassesStyle.Pill(_pills, "SkillCat_" + category, category, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    () => { _category = chosen; Refresh(); }, 15f);
                TMP_Text label = pill.GetComponentInChildren<TMP_Text>(true);
                float width = (label != null ? label.GetPreferredValues(category, 999f, 40f).x : category.Length * 8f) + 34f;
                RectTransform rect = (RectTransform)pill.transform;
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(width, ClassesStyle.Target);
                rect.anchoredPosition = new Vector2(x, 0f);
                ClassesStyle.PaintPill(pill, category == _category);
                x += width + 8f;
            }
        }

        private void BuildGrid(Skills skills, List<Skills.Skill> shown)
        {
            RectTransform content = _grid.Content;
            ClassesStyle.Clear(content);
            if (skills == null)
            {
                ClassesStyle.Label(content, "Empty", "No character loaded.", 14f, ClassesStyle.Muted,
                    new Vector2(0f, 1f), new Vector2(1f, 1f)).rectTransform.offsetMin = new Vector2(4f, -30f);
                content.sizeDelta = new Vector2(0f, 30f);
                return;
            }
            for (int i = 0; i < shown.Count; i++) Row(skills, content, shown[i], i / 2, i % 2);
            content.sizeDelta = new Vector2(0f, ((shown.Count + 1) / 2) * (RowHeight + 2f));
        }

        private void Row(Skills skills, RectTransform content, Skills.Skill skill, int row, int column)
        {
            string id = skill.m_info.m_skill.ToString();
            bool selected = id == _selected;
            float y = row * (RowHeight + 2f);
            RectTransform rect = ClassesStyle.Rect(content, "Skill_" + skill.m_info.m_skill,
                new Vector2(column * 0.5f, 1f), new Vector2(column * 0.5f + 0.5f, 1f));
            rect.offsetMin = new Vector2(column == 0 ? 0f : 7f, -y - RowHeight);
            rect.offsetMax = new Vector2(column == 0 ? -7f : 0f, -y);
            Image face = ClassesStyle.Fill(rect, selected ? ClassesStyle.Box(6, ClassesStyle.Neutral, ClassesStyle.Gold, 1)
                : ClassesStyle.Box(6, new Color(0f, 0f, 0f, 0.001f)), Color.white, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => { _selected = id; Refresh(); });

            RectTransform glyph = ClassesStyle.Fixed(rect, "icon", new Vector2(0f, 1f), new Vector2(22f, 22f));
            glyph.anchoredPosition = new Vector2(11f + 11f, -9f - 11f);
            Image icon = ClassesStyle.Fill(glyph, skill.m_info.m_icon, ClassesStyle.Muted);
            icon.preserveAspect = true;
            icon.enabled = skill.m_info.m_icon != null;

            TextMeshProUGUI name = ClassesStyle.Label(rect, "Name", NameOf(skill), 16f, ClassesStyle.Text, new Vector2(0f, 1f), new Vector2(1f, 1f));
            name.rectTransform.offsetMin = new Vector2(43f, -31f);
            name.rectTransform.offsetMax = new Vector2(-40f, -9f);
            TextMeshProUGUI level = ClassesStyle.Label(rect, "Level", Mathf.FloorToInt(skill.m_level).ToString(), 18f, ClassesStyle.Gold,
                new Vector2(1f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.MidlineRight, true);
            level.rectTransform.pivot = Vector2.one;
            level.rectTransform.sizeDelta = new Vector2(40f, 22f);
            level.rectTransform.anchoredPosition = new Vector2(-11f, -9f);

            RectTransform bar = ClassesStyle.Bar(rect, "Progress", new Vector2(0f, 1f), new Vector2(1f, 1f),
                skill.GetLevelPercentage()).parent as RectTransform;
            bar.offsetMin = new Vector2(11f, -45f);
            bar.offsetMax = new Vector2(-9f, -37f);
        }

        private void BuildDetail(Skills skills, Skills.Skill skill)
        {
            ClassesStyle.Clear(_detail);
            if (skills == null || skill == null) return;
            float y = 19f;
            if (skill.m_info.m_icon != null)
            {
                RectTransform glyph = ClassesStyle.Fixed(_detail, "icon", new Vector2(0f, 1f), new Vector2(45f, 45f));
                glyph.anchoredPosition = new Vector2(19f + 22f, -y - 22f);
                ClassesStyle.Fill(glyph, skill.m_info.m_icon, ClassesStyle.Gold).preserveAspect = true;
            }
            y += 53f;
            y = Text("Name", NameOf(skill), 24f, ClassesStyle.Gold, y, 28f, true);
            int level = Mathf.FloorToInt(skill.m_level);
            y = Text("Level", "Level " + level, 16f, ClassesStyle.Text, y, 20f);
            RectTransform bar = ClassesStyle.Bar(_detail, "Progress", new Vector2(0f, 1f), new Vector2(1f, 1f),
                skill.GetLevelPercentage()).parent as RectTransform;
            bar.offsetMin = new Vector2(19f, -y - 12f);
            bar.offsetMax = new Vector2(-19f, -y);
            y += 20f;
            y = Text("ToNext", level >= 100 ? "Maximum level" : Mathf.FloorToInt(skill.GetLevelPercentage() * 100f) + "% to level " + (level + 1),
                13f, ClassesStyle.Muted, y, 16f) + 10f;

            string description = Localize(skill.m_info.m_description);
            if (!string.IsNullOrEmpty(description))
                y = Text("Description", description, 15f, ClassesStyle.Text, y, 64f) + 6f;

            float effective = skills.GetSkillLevel(skill.m_info.m_skill);
            float delta = effective - Mathf.Floor(skill.m_level);
            if (Mathf.Abs(delta) >= 0.5f)
            {
                y = Text("BonusHead", "From effects", 13f, ClassesStyle.Muted, y, 16f);
                Text("Bonus", delta.ToString("+0;-0") + " levels while they last", 15f,
                    delta > 0f ? ClassesStyle.Gold : ClassesStyle.Orange, y, 18f);
            }
        }

        private float Text(string name, string text, float size, Color color, float y, float height, bool bold = false)
        {
            TextMeshProUGUI label = ClassesStyle.Label(_detail, name, text, size, color, new Vector2(0f, 1f), new Vector2(1f, 1f),
                TextAlignmentOptions.TopLeft, bold);
            label.rectTransform.offsetMin = new Vector2(19f, -y - height);
            label.rectTransform.offsetMax = new Vector2(-19f, -y);
            return y + height + 4f;
        }

        private static string Localize(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            Localization localization = Localization.instance;
            string text = localization == null ? token : localization.Localize(token);
            return text.StartsWith("$") ? "" : text;
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
