using System.Collections.Generic;
using FiresCore.Bridge;
using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 12's Achievements board. Shows every achievement catalogue registered with Core's
    /// <see cref="AchievementBridge"/> - RPG Maker's when that mod is loaded, Valheim's own always - under
    /// one filter, so a player sees one list rather than three screens.
    /// Plan: Docs/PLAN_AchievementsBoard.md
    /// </summary>
    public class AchievementsView : IClassesView
    {
        private const float HeaderTextHeight = 34f;
        private const float FilterHeight = 26f;
        private const float RowHeight = 38f;
        private const float SectionHeight = 26f;
        private const string AllSources = "";

        public ClassesTab Tab => ClassesTab.Achievements;

        public string Title => "Achievements";

        private RectTransform _page;
        private RectTransform _filters;
        private UIBuilderHelper.ScrollAreaResult _scroll;
        private TextMeshProUGUI _header;
        private string _source = AllSources;

        public void Build(RectTransform page)
        {
            _page = page;

            _header = ClassesTheme.Text(page, "AchievementHeader", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.RowFontSize);
            _header.rectTransform.Fill(-6f, HeaderTextHeight);

            GameObject filters = UIBuilderHelper.CreatePanel(page, "AchievementFilters",
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), UIFontConfig.Colors.TabBarBackground);
            _filters = filters.GetComponent<RectTransform>();
            _filters.pivot = new Vector2(0.5f, 1f);
            _filters.anchoredPosition = new Vector2(0f, -HeaderTextHeight);
            _filters.sizeDelta = new Vector2(0f, FilterHeight);

            _scroll = UIBuilderHelper.CreateScrollArea(page, "AchievementScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            _scroll.Root.GetComponent<RectTransform>().offsetMax =
                new Vector2(0f, -(HeaderTextHeight + FilterHeight + 10f));
        }

        public void Refresh()
        {
            if (_page == null) return;

            List<string> sources = new List<string>(AchievementBridge.SourceIds);
            if (!string.IsNullOrEmpty(_source) && !sources.Contains(_source)) _source = AllSources;

            BuildFilters(sources);
            BuildRows(sources);
        }

        private void BuildFilters(List<string> sources)
        {
            ClassesTheme.ClearChildren(_filters);
            if (sources.Count <= 1) return;

            int count = sources.Count + 1;
            float width = 1f / count;
            FilterButton(0, width, "All", AllSources);
            for (int i = 0; i < sources.Count; i++)
                FilterButton(i + 1, width, AchievementBridge.DisplayNameOf(sources[i]), sources[i]);
        }

        private void FilterButton(int index, float width, string label, string source)
        {
            string chosen = source;
            ClassesTheme.Button(_filters, _source == source ? "> " + label + " <" : label,
                new Vector2(index * width + 0.004f, 0.06f), new Vector2((index + 1) * width - 0.004f, 0.94f),
                () => { _source = chosen; Refresh(); });
        }

        private void BuildRows(List<string> sources)
        {
            ClassesTheme.ClearChildren(_scroll.Content);

            int earned = 0;
            int total = 0;
            float offset = 0f;

            foreach (string source in sources)
            {
                if (!string.IsNullOrEmpty(_source) && source != _source) continue;

                IReadOnlyList<AchievementRow> rows = AchievementBridge.Read(source);
                int sourceEarned = 0;
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i] != null && rows[i].Earned) sourceEarned++;

                earned += sourceEarned;
                total += rows.Count;

                offset = Section(offset, AchievementBridge.DisplayNameOf(source)
                    + "   " + sourceEarned + " / " + rows.Count, BlockedNoteFor(source, rows));

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == null) continue;
                    offset = Row(offset, source, i, rows[i]);
                }
            }

            _scroll.Content.sizeDelta = new Vector2(_scroll.Content.sizeDelta.x, offset + ClassesTheme.RowGap);
            _header.text = sources.Count == 0
                ? "No achievement source is loaded."
                : "Earned " + earned + " / " + total;
        }

        private static string BlockedNoteFor(string source, IReadOnlyList<AchievementRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && !string.IsNullOrEmpty(rows[i].Blocked)) return rows[i].Blocked;
            return string.Empty;
        }

        private float Section(float offset, string title, string note)
        {
            RectTransform row = StackedRow(_scroll.Content, "Section_" + title, offset, SectionHeight);
            ClassesTheme.Text(row, "Title", title, UIFontConfig.Colors.BrightGold,
                new Vector2(0.02f, 0f), new Vector2(0.45f, 1f), ClassesTheme.RowFontSize);
            if (!string.IsNullOrEmpty(note))
            {
                ClassesTheme.Text(row, "Note", "none can be earned: " + note, ClassesTheme.Locked,
                    new Vector2(0.45f, 0f), new Vector2(0.99f, 1f), ClassesTheme.SmallFontSize);
            }
            return offset + SectionHeight + ClassesTheme.RowGap;
        }

        private float Row(float offset, string source, int index, AchievementRow data)
        {
            RectTransform row = StackedRow(_scroll.Content, "Ach_" + source + "_" + index, offset, RowHeight);

            UIBuilderHelper.CreateImage(row, "Accent", new Vector2(0.02f, 0.15f), new Vector2(0.032f, 0.85f),
                data.Earned ? data.Accent : Faded(data.Accent));

            ClassesTheme.Text(row, "Name", data.Name,
                data.Earned ? ClassesTheme.Good : ClassesTheme.Ready,
                new Vector2(0.045f, 0.48f), new Vector2(0.80f, 1f), ClassesTheme.RowFontSize);

            ClassesTheme.Text(row, "Description", data.Description, ClassesTheme.Dim,
                new Vector2(0.045f, 0f), new Vector2(0.80f, 0.52f), ClassesTheme.SmallFontSize);

            ClassesTheme.Text(row, "Status", StatusTextFor(data), StatusColorFor(data),
                new Vector2(0.80f, 0f), new Vector2(0.98f, 1f), ClassesTheme.RowFontSize,
                TextAlignmentOptions.MidlineRight);

            return offset + RowHeight + ClassesTheme.RowGap;
        }

        private static string StatusTextFor(AchievementRow data)
        {
            if (data.Earned) return "Earned";
            if (data.HasProgress) return data.Progress + " / " + data.Target;
            return string.IsNullOrEmpty(data.Blocked) ? "Locked" : "Unreachable";
        }

        private static Color StatusColorFor(AchievementRow data)
        {
            if (data.Earned) return ClassesTheme.Good;
            if (!string.IsNullOrEmpty(data.Blocked)) return ClassesTheme.Locked;
            return data.HasProgress && data.Progress > 0 ? ClassesTheme.Cooling : ClassesTheme.Dim;
        }

        private static Color Faded(Color color) => new Color(color.r, color.g, color.b, 0.35f);

        /// <summary>
        /// A row placed at an explicit pixel offset, because this board stacks two row heights rather than
        /// the uniform grid <see cref="ClassesTheme.Row"/> assumes.
        /// </summary>
        private static RectTransform StackedRow(RectTransform content, string name, float offset, float height)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(content, false);
            RectTransform rect = row.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = new Vector2(0f, -offset);
            rect.sizeDelta = new Vector2(0f, height);
            return rect;
        }
    }
}
