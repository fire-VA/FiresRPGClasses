using System.Collections.Generic;
using System.Linq;
using FiresCore.Bridge;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The Achievements tab, from Fire's board Achievements. On the left, the categories with their counts: each
    /// catalogue's own groups, "All" first. On the right, "N of M completed" with a bar, then a row per achievement:
    /// icon, name, description, then "Completed" or a progress bar with "6 / 10", and the score on the right. It shows
    /// every catalogue registered with Core's <see cref="AchievementBridge"/>: Valheim's own always, RPG Maker's when
    /// that mod is loaded.
    ///
    /// Names: categories "Cat_&lt;name&gt;", rows "Ach_&lt;source&gt;_&lt;index&gt;" (as before). Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class AchievementsView : IClassesView
    {
        private const float Pad = 16f;
        private const float ListWidth = 210f;
        private const float RowHeight = 99f;
        private const float DoneRowHeight = 86f;
        private const string All = "All";

        public ClassesTab Tab => ClassesTab.Achievements;

        public string Title => "Achievements";

        private RectTransform _page;
        private RectTransform _categories;
        private RectTransform _summary;
        private UIBuilderHelper.ScrollAreaResult _list;
        private string _category = All;

        private sealed class Entry
        {
            public string Source;
            public int Index;
            public string Category;
            public AchievementRow Row;
        }

        public void Build(RectTransform page)
        {
            _page = page;
            _categories = ClassesStyle.Rect(page, "Categories", new Vector2(0f, 0f), new Vector2(0f, 1f));
            _categories.pivot = new Vector2(0f, 0.5f);
            _categories.offsetMin = new Vector2(Pad, 12f);
            _categories.offsetMax = new Vector2(Pad + ListWidth, -8f);

            _summary = ClassesStyle.Rect(page, "Summary", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _summary.pivot = new Vector2(0.5f, 1f);
            _summary.offsetMin = new Vector2(Pad + ListWidth + 28f, -58f);
            _summary.offsetMax = new Vector2(-Pad - 12f, -8f);

            _list = UIBuilderHelper.CreateScrollArea(page, "AchievementList", Vector2.zero, Vector2.one, Color.clear);
            RectTransform list = _list.Root.GetComponent<RectTransform>();
            list.offsetMin = new Vector2(Pad + ListWidth + 16f - 5f, 8f);
            list.offsetMax = new Vector2(-Pad + 18f - 12f, -66f);
        }

        public void Refresh()
        {
            if (_page == null) return;
            List<Entry> entries = Entries();
            List<string> categories = entries.Select(e => e.Category).Distinct().ToList();
            if (_category != All && !categories.Contains(_category)) _category = All;

            BuildCategories(entries, categories);
            List<Entry> shown = entries.Where(e => _category == All || e.Category == _category).ToList();
            BuildSummary(shown, entries.Count == 0);
            BuildRows(shown);
        }

        /// <summary>Every row of every catalogue; a row's category is its group, else its catalogue's name.</summary>
        private static List<Entry> Entries()
        {
            var entries = new List<Entry>();
            foreach (string source in AchievementBridge.SourceIds)
            {
                IReadOnlyList<AchievementRow> rows = AchievementBridge.Read(source);
                string fallback = AchievementBridge.DisplayNameOf(source);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == null) continue;
                    entries.Add(new Entry
                    {
                        Source = source,
                        Index = i,
                        Category = string.IsNullOrEmpty(rows[i].Group) ? fallback : rows[i].Group,
                        Row = rows[i],
                    });
                }
            }
            return entries;
        }

        // ---- categories -------------------------------------------------------------------------------------

        private void BuildCategories(List<Entry> entries, List<string> categories)
        {
            ClassesStyle.Clear(_categories);
            float y = 0f;
            Category(All, entries, ref y);
            foreach (string category in categories)
                Category(category, entries.Where(e => e.Category == category).ToList(), ref y);
        }

        private void Category(string name, List<Entry> entries, ref float y)
        {
            bool on = name == _category;
            RectTransform row = ClassesStyle.Rect(_categories, "Cat_" + name.Replace(" ", ""), new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.offsetMin = new Vector2(0f, -y - ClassesStyle.Target);
            row.offsetMax = new Vector2(0f, -y);
            Image face = ClassesStyle.Fill(row, on ? ClassesStyle.Box(6, ClassesStyle.Selected, ClassesStyle.Gold, 1)
                : ClassesStyle.Box(6, new Color(0f, 0f, 0f, 0.001f)), Color.white, true);
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            string chosen = name;
            button.onClick.AddListener(() => { _category = chosen; Refresh(); });

            // The name is the button's first text.
            TextMeshProUGUI label = ClassesStyle.Label(row, "Name", name, 16f, on ? ClassesStyle.Gold : ClassesStyle.Text,
                Vector2.zero, new Vector2(0.72f, 1f));
            label.rectTransform.offsetMin = new Vector2(13f, 0f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            TextMeshProUGUI count = ClassesStyle.Label(row, "Count", entries.Count(e => e.Row.Earned) + " / " + entries.Count, 14f,
                ClassesStyle.Muted, new Vector2(0.6f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);
            count.rectTransform.offsetMax = new Vector2(-13f, 0f);
            y += ClassesStyle.Target + 4f;
        }

        // ---- summary + rows ---------------------------------------------------------------------------------

        private void BuildSummary(List<Entry> shown, bool nothingLoaded)
        {
            ClassesStyle.Clear(_summary);
            int done = shown.Count(e => e.Row.Earned);
            ClassesStyle.Label(_summary, "Completed",
                nothingLoaded ? "No achievement source is loaded." : done + " of " + shown.Count + " completed", 18f, ClassesStyle.Text,
                new Vector2(0f, 1f), new Vector2(0.7f, 1f)).rectTransform.offsetMin = new Vector2(0f, -26f);
            string blocked = shown.Select(e => e.Row.Blocked).FirstOrDefault(b => !string.IsNullOrEmpty(b));
            if (!string.IsNullOrEmpty(blocked))
            {
                TextMeshProUGUI note = ClassesStyle.Label(_summary, "Blocked", "None can be earned: " + blocked, 13f,
                    ClassesStyle.Orange, new Vector2(0.45f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.MidlineRight);
                note.rectTransform.offsetMin = new Vector2(0f, -26f);
            }
            ClassesStyle.Bar(_summary, "Progress", new Vector2(0f, 0f), new Vector2(1f, 0f), shown.Count > 0 ? (float)done / shown.Count : 0f)
                .parent.GetComponent<RectTransform>().offsetMax = new Vector2(0f, 12f);
        }

        private void BuildRows(List<Entry> shown)
        {
            RectTransform content = _list.Content;
            ClassesStyle.Clear(content);
            float y = 0f;
            foreach (Entry entry in shown)
                y = Row(content, entry, y);
            content.sizeDelta = new Vector2(0f, y);
        }

        private static float Row(RectTransform content, Entry entry, float y)
        {
            AchievementRow data = entry.Row;
            bool hidden = data.Secret && !data.Earned;
            bool showBar = !data.Earned && data.HasProgress;
            float height = showBar ? RowHeight : DoneRowHeight;

            RectTransform row = ClassesStyle.Rect(content, "Ach_" + entry.Source + "_" + entry.Index, new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.offsetMin = new Vector2(0f, -y - height);
            row.offsetMax = new Vector2(0f, -y);
            RectTransform rule = ClassesStyle.Rect(row, "Rule", Vector2.zero, new Vector2(1f, 0f));
            rule.sizeDelta = new Vector2(0f, 1f);
            ClassesStyle.Fill(rule, null, ClassesStyle.Line);

            RectTransform frame = ClassesStyle.Fixed(row, "Frame", new Vector2(0f, 0.5f), new Vector2(56f, 56f));
            frame.anchoredPosition = new Vector2(12f + 28f, 0f);
            Color accent = data.Earned ? ClassesStyle.Gold : ClassesStyle.Line;
            ClassesStyle.Fill(frame, ClassesStyle.Box(10, ClassesStyle.Well, accent, 2), Color.white);
            if (data.Icon != null && !hidden)
            {
                RectTransform glyph = ClassesStyle.Fixed(frame, "icon", new Vector2(0.5f, 0.5f), new Vector2(36f, 36f));
                Image icon = ClassesStyle.Fill(glyph, data.Icon, data.Earned ? Color.white : new Color(1f, 1f, 1f, 0.45f));
                icon.preserveAspect = true;
            }
            else
            {
                RectTransform dot = ClassesStyle.Fixed(frame, "Accent", new Vector2(0.5f, 0.5f), new Vector2(18f, 18f));
                Color tint = data.Earned ? data.Accent : new Color(data.Accent.r, data.Accent.g, data.Accent.b, 0.35f);
                ClassesStyle.Fill(dot, ClassesStyle.Circle(18, tint, tint, 0f), Color.white);
            }

            float top = (height - (showBar ? 74f : 61f)) / 2f;
            Line(row, "Name", hidden ? "Hidden achievement" : data.Name, 17f, ClassesStyle.Text, top, 21f);
            Line(row, "Description", hidden ? "Keep playing to find this one." : data.Description, 14f, ClassesStyle.Muted, top + 25f, 16f);
            if (data.Earned)
            {
                Line(row, "Status", "Completed", 14f, ClassesStyle.Gold, top + 45f, 16f);
            }
            else if (showBar)
            {
                RectTransform bar = ClassesStyle.Bar(row, "Progress", new Vector2(0f, 1f), new Vector2(1f, 1f),
                    data.Target > 0 ? (float)data.Progress / data.Target : 0f).parent as RectTransform;
                bar.offsetMin = new Vector2(70f, -top - 55f);
                bar.offsetMax = new Vector2(-156f, -top - 45f);
                Line(row, "Status", data.Progress + " / " + data.Target, 13f, ClassesStyle.Muted, top + 58f, 16f);
            }
            else
            {
                Line(row, "Status", string.IsNullOrEmpty(data.Blocked) ? "Not yet" : "Can't be earned here", 14f,
                    ClassesStyle.LockedText, top + 45f, 16f);
            }

            if (data.Score > 0)
            {
                TextMeshProUGUI caption = ClassesStyle.Label(row, "RewardCaption", "Score", 12f, ClassesStyle.Muted,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                caption.rectTransform.pivot = new Vector2(1f, 0f);
                caption.rectTransform.sizeDelta = new Vector2(130f, 15f);
                caption.rectTransform.anchoredPosition = new Vector2(-12f, 1f);
                TextMeshProUGUI value = ClassesStyle.Label(row, "Reward", "+" + data.Score, 15f,
                    data.Earned ? ClassesStyle.Muted : ClassesStyle.Gold, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                value.rectTransform.pivot = new Vector2(1f, 1f);
                value.rectTransform.sizeDelta = new Vector2(130f, 18f);
                value.rectTransform.anchoredPosition = new Vector2(-12f, 0f);
            }
            return y + height;
        }

        private static void Line(RectTransform row, string name, string text, float size, Color color, float top, float height)
        {
            TextMeshProUGUI label = ClassesStyle.Label(row, name, text, size, color, new Vector2(0f, 1f), new Vector2(1f, 1f));
            label.rectTransform.offsetMin = new Vector2(82f, -top - height);
            label.rectTransform.offsetMax = new Vector2(-156f, -top);
        }
    }
}
