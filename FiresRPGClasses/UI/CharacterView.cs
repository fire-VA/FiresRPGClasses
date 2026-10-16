using System;
using System.Collections.Generic;
using System.Linq;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Character, from Fire's boards Mastery + LayoutB1: every archetype's mastery on the left (crest, front /
    /// back badge, points in its tree, "Open tree"), the five attributes on the right with − / + that stage points in
    /// gold until Confirm, the live before → after of every stat those points move (AttributeEffects.Preview), and a
    /// footer with level, XP, Valor and the points to spend.
    ///
    /// class_test: rows "Attr_&lt;Kind&gt;" (first Button "+", then "−"; the value in "Value"), "ConfirmAttributes" commits;
    /// mastery rows "Tree_&lt;Archetype&gt;" with an "OpenTree" button. Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    public class CharacterView : IClassesView
    {
        private const float FooterHeight = 76f;
        private const float Gap = 12f;

        public ClassesTab Tab => ClassesTab.Character;

        public string Title => "Character";

        private RectTransform _page;
        private RectTransform _mastery;
        private RectTransform _attributes;
        private RectTransform _stats;
        private RectTransform _footer;

        /// <summary>Points staged with + and not yet bought (the boards' gold "pending" values).</summary>
        private readonly Dictionary<AttributeKind, int> _pending = new Dictionary<AttributeKind, int>();

        public void Build(RectTransform page)
        {
            _page = page;
            if (_open == null)
            {
                ClassXp.Awarded += (amount, source) => OnXpChanged();
                ClassXp.LevelledUp += level => OnXpChanged();
            }
            _open = this;

            _footer = ClassesStyle.Rect(page, "Footer", new Vector2(0f, 0f), new Vector2(1f, 0f));
            _footer.pivot = new Vector2(0.5f, 0f);
            _footer.sizeDelta = new Vector2(-2f * Gap, FooterHeight);
            _footer.anchoredPosition = new Vector2(0f, 4f);

            _mastery = ClassesStyle.Rect(page, "Mastery", new Vector2(0f, 0f), new Vector2(0.56f, 1f));
            _mastery.offsetMin = new Vector2(Gap, FooterHeight + Gap);
            _mastery.offsetMax = new Vector2(-Gap / 2f, -4f);
            ClassesStyle.WellFrame(_mastery);

            _attributes = ClassesStyle.Rect(page, "Attributes", new Vector2(0.56f, 0.46f), new Vector2(1f, 1f));
            _attributes.offsetMin = new Vector2(Gap / 2f, Gap / 2f);
            _attributes.offsetMax = new Vector2(-Gap, -4f);
            ClassesStyle.WellFrame(_attributes);

            _stats = ClassesStyle.Rect(page, "Stats", new Vector2(0.56f, 0f), new Vector2(1f, 0.46f));
            _stats.offsetMin = new Vector2(Gap / 2f, FooterHeight + Gap);
            _stats.offsetMax = new Vector2(-Gap, -Gap / 2f);
            ClassesStyle.WellFrame(_stats);
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null) return;
            TrimPending(data);

            BuildMastery(data);
            BuildAttributes(data);
            BuildStats(data);
            BuildFooter(data);
        }

        /// <summary>Staged points can't exceed what is unspent (a level-down, a respec elsewhere).</summary>
        private void TrimPending(ClassPlayerData data)
        {
            int free = data.Progress.UnspentAttributePoints(TalentRules.Rates);
            foreach (AttributeKind kind in _pending.Keys.ToList())
            {
                int keep = Mathf.Clamp(_pending[kind], 0, free);
                free -= keep;
                if (keep == 0) _pending.Remove(kind); else _pending[kind] = keep;
            }
        }

        private int PendingTotal => _pending.Values.Sum();

        private int Pending(AttributeKind kind) => _pending.TryGetValue(kind, out int points) ? points : 0;

        // ---- archetype mastery (board Mastery) -------------------------------------------------------------

        private void BuildMastery(ClassPlayerData data)
        {
            ClassesStyle.Clear(_mastery);
            List<ArchetypeClass> archetypes = ClassTrees.Archetypes.ToList();
            int spentAll = archetypes.Sum(a => SpentIn(data, a));
            int totalAll = archetypes.Sum(a => ClassTrees.Tree(a)?.TotalPointCost(ClassTrees.RankCosts) ?? 0);

            RectTransform head = ClassesStyle.Rect(_mastery, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f));
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(-32f, 48f);
            head.anchoredPosition = new Vector2(0f, -6f);
            ClassesStyle.Label(head, "Title", "Archetype mastery", 19f, ClassesStyle.Text, Vector2.zero, new Vector2(0.6f, 1f),
                TextAlignmentOptions.MidlineLeft, true);
            ClassesStyle.Label(head, "Total", spentAll + " of " + totalAll + " tree points", 14f, ClassesStyle.Muted,
                new Vector2(0.5f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);

            RectTransform rows = ClassesStyle.Rect(_mastery, "Rows", Vector2.zero, Vector2.one);
            rows.offsetMin = new Vector2(16f, 10f);
            rows.offsetMax = new Vector2(-16f, -58f);

            // Front bar first, then back, then by points spent - the board's order.
            archetypes = archetypes
                .OrderBy(a => a == data.Front.Archetype ? 0 : a == data.Back.Archetype ? 1 : 2)
                .ThenByDescending(a => SpentIn(data, a))
                .ToList();
            int count = Mathf.Max(1, archetypes.Count);
            for (int i = 0; i < archetypes.Count; i++)
                MasteryRow(data, rows, archetypes[i], i, count);
        }

        private void MasteryRow(ClassPlayerData data, RectTransform parent, ArchetypeClass archetype, int index, int count)
        {
            float top = 1f - (float)index / count, bottom = 1f - (float)(index + 1) / count;
            RectTransform row = ClassesStyle.Rect(parent, "Tree_" + archetype, new Vector2(0f, bottom), new Vector2(1f, top));
            RectTransform rule = ClassesStyle.Rect(row, "Rule", new Vector2(0f, 0f), new Vector2(1f, 0f));
            rule.sizeDelta = new Vector2(0f, 1f);
            ClassesStyle.Fill(rule, null, ClassesStyle.Line);

            bool front = archetype == data.Front.Archetype, back = archetype == data.Back.Archetype;
            Color accent = front ? ClassesStyle.Gold : back ? ClassesStyle.Blue : ClassesStyle.Text;
            Color ring = front ? ClassesStyle.Gold : back ? ClassesStyle.Blue : ClassesStyle.Line;

            RectTransform crest = ClassesStyle.Fixed(row, "Crest", new Vector2(0f, 0.5f), new Vector2(44f, 44f));
            crest.anchoredPosition = new Vector2(22f, 0f);
            ClassesStyle.Fill(crest, ClassesStyle.Circle(44, ClassesStyle.Panel, ring, 2f), Color.white);
            RectTransform glyph = ClassesStyle.Fixed(crest, "Glyph", new Vector2(0.5f, 0.5f), new Vector2(24f, 24f));
            ClassesStyle.Fill(glyph, ClassCrests.For(archetype), accent);

            ClassesStyle.Label(row, "Name", ClassNames.Of(archetype), 17f, accent,
                new Vector2(0f, 0.5f), new Vector2(0.45f, 0.95f), TextAlignmentOptions.BottomLeft, true)
                .rectTransform.offsetMin = new Vector2(58f, 0f);
            if (front || back)
            {
                TextMeshProUGUI badge = ClassesStyle.Badge(row, "Badge", front ? "Front bar" : "Back bar", accent,
                    new Vector2(0.3f, 0.56f), new Vector2(0.3f, 0.9f));
                RectTransform badgeRect = (RectTransform)badge.rectTransform.parent;
                badgeRect.sizeDelta = new Vector2(72f, 0f);
                badgeRect.pivot = new Vector2(0f, 0.5f);
            }

            ClassTree tree = ClassTrees.Tree(archetype);
            int spent = SpentIn(data, archetype);
            int total = tree?.TotalPointCost(ClassTrees.RankCosts) ?? 0;
            RectTransform bar = ClassesStyle.Bar(row, "Progress", new Vector2(0f, 0.28f), new Vector2(0.7f, 0.38f),
                total > 0 ? (float)spent / total : 0f).parent as RectTransform;
            bar.offsetMin = new Vector2(58f, 0f);

            ClassesStyle.Label(row, "Spent", spent + " / " + total, 20f, spent > 0 ? ClassesStyle.Gold : ClassesStyle.Muted,
                new Vector2(0.7f, 0f), new Vector2(0.83f, 1f), TextAlignmentOptions.MidlineRight, true);

            ArchetypeClass open = archetype;
            ClassesStyle.Button(row, "OpenTree", "Open tree", ClassesStyle.ButtonKind.Outline,
                new Vector2(0.85f, 0.18f), new Vector2(1f, 0.82f), () =>
                {
                    TalentsView.Focus(open);
                    ClassesPanel.Instance?.Show(ClassesTab.Talents);
                }, 14f);
        }

        // ---- attributes (boards Mastery / LayoutB1) --------------------------------------------------------

        private void BuildAttributes(ClassPlayerData data)
        {
            ClassesStyle.Clear(_attributes);
            int free = data.Progress.UnspentAttributePoints(TalentRules.Rates) - PendingTotal;
            bool locked = ClassPlayerStore.LocalIsProtected;

            RectTransform head = ClassesStyle.Rect(_attributes, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f));
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(-32f, 44f);
            head.anchoredPosition = new Vector2(0f, -6f);
            ClassesStyle.Label(head, "Title", "Attributes", 19f, ClassesStyle.Text, Vector2.zero, new Vector2(0.5f, 1f),
                TextAlignmentOptions.MidlineLeft, true);
            ClassesStyle.Label(head, "ToSpend", locked ? "Progress protected" : free + " to spend", 15f,
                free > 0 && !locked ? ClassesStyle.Gold : ClassesStyle.Muted,
                new Vector2(0.5f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);

            RectTransform rows = ClassesStyle.Rect(_attributes, "Rows", Vector2.zero, Vector2.one);
            rows.offsetMin = new Vector2(16f, 62f);
            rows.offsetMax = new Vector2(-16f, -52f);
            AttributeKind[] kinds = (AttributeKind[])Enum.GetValues(typeof(AttributeKind));
            for (int i = 0; i < kinds.Length; i++)
                AttributeRow(data, rows, kinds[i], i, kinds.Length, free > 0 && !locked);

            RectTransform actions = ClassesStyle.Rect(_attributes, "Actions", new Vector2(0f, 0f), new Vector2(1f, 0f));
            actions.pivot = new Vector2(0.5f, 0f);
            actions.sizeDelta = new Vector2(-32f, ClassesStyle.Target);
            actions.anchoredPosition = new Vector2(0f, 10f);
            Button undo = ClassesStyle.Button(actions, "UndoAttributes", "Undo", ClassesStyle.ButtonKind.Outline,
                new Vector2(0f, 0f), new Vector2(0.32f, 1f), () => { _pending.Clear(); Refresh(); });
            ClassesStyle.Paint(undo, ClassesStyle.ButtonKind.Outline, PendingTotal > 0);
            Button confirm = ClassesStyle.Button(actions, "ConfirmAttributes",
                PendingTotal > 0 ? "Confirm " + TalentRules.Points(PendingTotal) : "Confirm", ClassesStyle.ButtonKind.Primary,
                new Vector2(0.56f, 0f), new Vector2(1f, 1f), Confirm);
            ClassesStyle.Paint(confirm, ClassesStyle.ButtonKind.Primary, PendingTotal > 0 && !locked);
        }

        private void AttributeRow(ClassPlayerData data, RectTransform parent, AttributeKind kind, int index, int count, bool canAdd)
        {
            float top = 1f - (float)index / count, bottom = 1f - (float)(index + 1) / count;
            RectTransform row = ClassesStyle.Rect(parent, "Attr_" + kind, new Vector2(0f, bottom), new Vector2(1f, top));
            RectTransform rule = ClassesStyle.Rect(row, "Rule", new Vector2(0f, 0f), new Vector2(1f, 0f));
            rule.sizeDelta = new Vector2(0f, 1f);
            ClassesStyle.Fill(rule, null, ClassesStyle.Line);

            // 0.1.151 (Fire 10-16, 114.png: "text descriptions jumbled and overlapped"): the name filled the top 55 % and the
            // one-line description the bottom 50 %, overlapping by 5 % with no gap, and the description auto-shrank to 10 px to fit
            // one line. Now: name in the top 42 %, a 4 px gap, then the description in the rest, wrapping to two lines at 13-15 px
            // (never below 13) and ellipsizing past that, so the two never touch at any window size.
            TextMeshProUGUI nameLabel = ClassesStyle.Label(row, "Name", kind.ToString(), 17f, ClassesStyle.Text,
                new Vector2(0f, 0.58f), new Vector2(0.6f, 1f), TextAlignmentOptions.BottomLeft);
            nameLabel.rectTransform.offsetMax = new Vector2(0f, -2f);
            TextMeshProUGUI effect = ClassesStyle.Label(row, "Effect", EffectOf(kind), 14f, ClassesStyle.Muted,
                new Vector2(0f, 0f), new Vector2(0.66f, 0.58f), TextAlignmentOptions.TopLeft);
            effect.rectTransform.offsetMin = new Vector2(0f, 3f);
            effect.rectTransform.offsetMax = new Vector2(0f, -4f);
            effect.textWrappingMode = TextWrappingModes.Normal;
            effect.enableAutoSizing = true;
            effect.fontSizeMin = 13f;
            effect.fontSizeMax = 15f;
            effect.maxVisibleLines = 2;
            effect.overflowMode = TextOverflowModes.Ellipsis;

            int pending = Pending(kind);
            AttributeKind staged = kind;

            // "+" is the row's FIRST button (class_test's contract); it sits visually to the right of the value.
            Button plus = ClassesStyle.Button(row, "Plus", "+", ClassesStyle.ButtonKind.Neutral,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), () => { _pending[staged] = Pending(staged) + 1; Refresh(); }, 20f);
            Stepper(plus, 0f);
            ClassesStyle.Paint(plus, ClassesStyle.ButtonKind.Neutral, canAdd);

            ClassesStyle.Label(row, "Value", (data.Progress.Attribute(kind) + pending).ToString(), 18f,
                pending > 0 ? ClassesStyle.Gold : ClassesStyle.Text,
                new Vector2(1f, 0f), new Vector2(1f, 1f), TextAlignmentOptions.Center, true)
                .rectTransform.offsetMin = new Vector2(-ClassesStyle.Target - 44f, 0f);
            row.Find("Value").GetComponent<RectTransform>().offsetMax = new Vector2(-ClassesStyle.Target - 4f, 0f);

            Button minus = ClassesStyle.Button(row, "Minus", "-", ClassesStyle.ButtonKind.Neutral,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), () =>
                {
                    int now = Pending(staged) - 1;
                    if (now <= 0) _pending.Remove(staged); else _pending[staged] = now;
                    Refresh();
                }, 20f);
            Stepper(minus, ClassesStyle.Target + 44f);
            ClassesStyle.Paint(minus, ClassesStyle.ButtonKind.Neutral, pending > 0);
        }

        private static void Stepper(Button button, float fromRight)
        {
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(ClassesStyle.Target, ClassesStyle.Target);
            rect.anchoredPosition = new Vector2(-fromRight, 0f);
        }

        private void Confirm()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || ClassPlayerStore.LocalIsProtected) return;
            bool bought = false;
            foreach (KeyValuePair<AttributeKind, int> entry in _pending.ToList())
                for (int i = 0; i < entry.Value; i++)
                    bought |= data.Progress.TryBuyAttributePoint(entry.Key, TalentRules.Rates);
            _pending.Clear();
            if (bought) ClassPlayerStore.SaveLocal();
            Refresh();
        }

        /// <summary>Doc 12 section 8: what each attribute moves (the row's sub-line).</summary>
        /// <summary>
        /// What one point of this attribute does, with the numbers ("Each point: -0.3% attack stamina, -0.3% move stamina,
        /// +0.2% attack speed"). Fire 10-01 asked whether Agility's attack speed is real; it is, so the row now says how much.
        /// The rates are read through AttributeEffects.Preview (a blank record plus one point), i.e. the live, server-synced
        /// config values, so the text always matches what the point buys.
        /// </summary>
        private static string EffectOf(AttributeKind kind)
        {
            var one = new Dictionary<AttributeKind, int> { { kind, 1 } };
            var parts = new List<string>();
            foreach (AttributeEffects.StatPreview stat in AttributeEffects.Preview(new ClassProgressState(), one))
            {
                if (stat.Attribute != kind) continue;
                float perPoint = stat.After - stat.Before;
                if (Mathf.Abs(perPoint) < 0.0001f) continue;
                parts.Add((perPoint > 0f ? "+" : "") + perPoint.ToString("0.##") + (stat.Percent ? "%" : "") + " " + ShortName(stat));
            }
            return parts.Count == 0 ? "" : "Each point: " + string.Join(", ", parts.ToArray());
        }

        private static string ShortName(AttributeEffects.StatPreview stat)
        {
            switch (stat.Stat)
            {
                case AttributeEffects.Stat.PhysicalDamage: return "physical damage";
                case AttributeEffects.Stat.CarryWeight: return "carry weight";
                case AttributeEffects.Stat.AttackStamina: return "attack stamina";
                case AttributeEffects.Stat.MoveStamina: return "move stamina";
                case AttributeEffects.Stat.AttackSpeed: return "attack speed";
                case AttributeEffects.Stat.MagicDamage: return "magic damage";
                case AttributeEffects.Stat.MaxEitr: return "eitr";
                case AttributeEffects.Stat.EitrRegen: return "eitr regen";
                case AttributeEffects.Stat.MaxHealth: return "health";
                case AttributeEffects.Stat.Armour: return "armour";
                case AttributeEffects.Stat.HealthRegen: return "health regen";
                case AttributeEffects.Stat.MaxStamina: return "stamina";
                case AttributeEffects.Stat.StaminaRegen: return "stamina regen";
                case AttributeEffects.Stat.ElementalTaken: return "elemental damage taken";
                default: return stat.Label.ToLowerInvariant();
            }
        }

        // ---- your stats (board LayoutB1) -------------------------------------------------------------------

        private void BuildStats(ClassPlayerData data)
        {
            ClassesStyle.Clear(_stats);
            RectTransform head = ClassesStyle.Rect(_stats, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f));
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(-32f, 40f);
            head.anchoredPosition = new Vector2(0f, -4f);
            ClassesStyle.Label(head, "Title", "From your attributes", 19f, ClassesStyle.Text, Vector2.zero, new Vector2(0.55f, 1f),
                TextAlignmentOptions.MidlineLeft, true);
            ClassesStyle.Label(head, "Hint", PendingTotal > 0 ? "Gold: after Confirm" : "", 13f, ClassesStyle.Muted,
                new Vector2(0.5f, 0f), Vector2.one, TextAlignmentOptions.MidlineRight);

            List<AttributeEffects.StatPreview> preview = AttributeEffects.Preview(data.Progress, _pending);
            RectTransform list = ClassesStyle.Rect(_stats, "Rows", Vector2.zero, Vector2.one);
            list.offsetMin = new Vector2(16f, 8f);
            list.offsetMax = new Vector2(-16f, -46f);

            // Two columns so all 14 fit without a scroll bar.
            int perColumn = Mathf.Max(1, Mathf.CeilToInt(preview.Count / 2f));
            for (int i = 0; i < preview.Count; i++)
            {
                AttributeEffects.StatPreview stat = preview[i];
                int column = i / perColumn, line = i % perColumn;
                float top = 1f - (float)line / perColumn, bottom = 1f - (float)(line + 1) / perColumn;
                RectTransform row = ClassesStyle.Rect(list, "Stat_" + stat.Stat,
                    new Vector2(column * 0.5f, bottom), new Vector2(column * 0.5f + 0.5f, top));
                row.offsetMin = new Vector2(column == 0 ? 0f : 10f, 0f);
                row.offsetMax = new Vector2(column == 0 ? -10f : 0f, 0f);
                bool changes = Mathf.Abs(stat.After - stat.Before) > 0.0001f;
                ClassesStyle.Label(row, "Label", stat.Label, 14f, ClassesStyle.Muted, Vector2.zero, new Vector2(0.6f, 1f));
                string value = Format(stat.Before, stat.Percent)
                               + (changes ? "  <color=#f0b44c>to " + Format(stat.After, stat.Percent) + "</color>" : "");
                ClassesStyle.Label(row, "Value", value, 14f, ClassesStyle.Text, new Vector2(0.45f, 0f), Vector2.one,
                    TextAlignmentOptions.MidlineRight);
            }
        }

        private static string Format(float value, bool percent) =>
            (value >= 0f ? "+" : "") + (percent ? value.ToString("0.#") + "%" : value.ToString("0.##"));

        // ---- footer (board LayoutB1) -----------------------------------------------------------------------

        private void BuildFooter(ClassPlayerData data)
        {
            ClassesStyle.Clear(_footer);
            RectTransform rule = ClassesStyle.Rect(_footer, "Rule", new Vector2(0f, 1f), new Vector2(1f, 1f));
            rule.sizeDelta = new Vector2(0f, 1f);
            ClassesStyle.Fill(rule, null, ClassesStyle.Line);

            ProgressionRates rates = TalentRules.Rates;
            ClassProgressState progress = data.Progress;
            long toNext = ProgressionEngine.XpForNextLevel(progress.Level, progress.ValorLevel, rates);

            Stat(_footer, "Level", "Level", progress.Level.ToString(), 0f, 0.12f, 28f);
            RectTransform xp = ClassesStyle.Rect(_footer, "Xp", new Vector2(0.13f, 0f), new Vector2(0.45f, 1f));
            ClassesStyle.Bar(xp, "Bar", new Vector2(0f, 0.52f), new Vector2(1f, 0.66f), toNext > 0 ? (float)progress.Xp / toNext : 1f);
            ClassesStyle.Label(xp, "Text", progress.Xp.ToString("N0") + " / " + toNext.ToString("N0") + " XP"
                    + (progress.ValorLevel > 0 ? "   ·   Valor " + progress.ValorLevel : ""), 13f, ClassesStyle.Muted,
                new Vector2(0f, 0.12f), new Vector2(1f, 0.48f), TextAlignmentOptions.TopLeft);
            ClassesStyle.Label(xp, "Session", SessionLine(), 12f, ClassesStyle.Gold,
                new Vector2(0f, 0.7f), new Vector2(1f, 1f), TextAlignmentOptions.BottomLeft);

            Stat(_footer, "TalentPoints", "Talent points", progress.UnspentSkillPoints(rates).ToString(), 0.5f, 0.66f, 22f);
            Stat(_footer, "AttributePoints", "Attribute points", (progress.UnspentAttributePoints(rates) - PendingTotal).ToString(),
                0.66f, 0.82f, 22f);
            Stat(_footer, "PassiveSlots", "Passive slots", progress.PassiveSlots.ToString(), 0.82f, 0.94f, 22f);

            // Fire 10-14: the Character Creator opens from the Appearance header tab only ("put it as a header tab"), not from here.
        }



        /// <summary>VCS's public VcsApi.OpenEditor() (0.8.15+), found by reflection so VCS stays optional.</summary>
        private static class VcsEditor
        {
            private static System.Reflection.MethodInfo _open;
            private static bool _looked;

            internal static bool Available
            {
                get
                {
                    if (!_looked)
                    {
                        _looked = true;
                        // 0.1.138: Fires Character Creator's FccApi first (the VCS rebrand), VCS's VcsApi as the fallback.
                        System.Type api = Combat.VcsBridge.Api;
                        _open = api?.GetMethod("OpenEditor", System.Type.EmptyTypes);
                        FiresRPGClassesPlugin.Log?.LogDebug(_open != null ? $"[Classes] {api.FullName}.OpenEditor found: Edit appearance shown."
                            : "[Classes] no OpenEditor (Fires Character Creator / VCS 0.8.15+ missing): Edit appearance hidden.");
                    }
                    return _open != null;
                }
            }

            internal static void Open()
            {
                try
                {
                    ClassesPanel.Instance?.Close();
                    _open?.Invoke(null, null);
                }
                catch (System.Exception ex)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] Edit appearance could not open VCS ({ex.GetType().Name}: {ex.Message}).");
                }
            }
        }

        /// <summary>
        /// Class XP earned this session by source, from [wishbone]'s ClassXp ("This session +1,240 XP: kills 900, gathering 340").
        /// </summary>
        private static string SessionLine()
        {
            IReadOnlyDictionary<string, long> bySource = ClassXp.SessionXpBySource;
            if (bySource == null || bySource.Count == 0) return "";
            long total = bySource.Values.Sum();
            if (total <= 0) return "";
            string parts = string.Join(", ", bySource.Where(p => p.Value > 0).OrderByDescending(p => p.Value)
                .Select(p => SourceLabel(p.Key) + " " + p.Value.ToString("N0")).ToArray());
            return "This session +" + total.ToString("N0") + " XP: " + parts;
        }

        private static string SourceLabel(string source)
        {
            switch (source)
            {
                case "kill": return "kills";
                case "discovery": return "discoveries";
                case "skill": return "skill levels";
                default: return source;
            }
        }

        // The footer follows class XP while this view is open, at most once a second (gathering awards come in bursts).
        private static CharacterView _open;
        private static float _nextXpRefresh;

        private static void OnXpChanged()
        {
            CharacterView view = _open;
            if (view?._page == null || !view._page.gameObject.activeInHierarchy) return;
            if (Time.unscaledTime < _nextXpRefresh) return;
            _nextXpRefresh = Time.unscaledTime + 1f;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data != null) view.BuildFooter(data);
        }

        private static void Stat(RectTransform parent, string name, string caption, string value, float from, float to, float size)
        {
            RectTransform cell = ClassesStyle.Rect(parent, name, new Vector2(from, 0f), new Vector2(to, 1f));
            ClassesStyle.Label(cell, "Caption", caption, 14f, ClassesStyle.Muted, new Vector2(0f, 0f), new Vector2(0.62f, 1f),
                TextAlignmentOptions.MidlineRight);
            ClassesStyle.Label(cell, "Value", value, size, ClassesStyle.Gold, new Vector2(0.66f, 0f), Vector2.one,
                TextAlignmentOptions.MidlineLeft, true);
        }

        internal static int SpentIn(ClassPlayerData data, ArchetypeClass archetype)
        {
            if (!data.Progress.Talents.TryGetValue(archetype, out Dictionary<string, int> ranks)) return 0;
            int spent = 0;
            foreach (KeyValuePair<string, int> entry in ranks)
                for (int rank = 1; rank <= entry.Value; rank++)
                    spent += ClassTrees.RankCost(rank);
            return spent;
        }
    }
}
