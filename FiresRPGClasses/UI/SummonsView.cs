using System.Collections.Generic;
using System.Text;
using FiresCore.Input;
using FiresCore.Npc.Archetypes;
using FiresCore.Summon;
using FiresRPGClasses.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Classes > Summons, from the mockup Fire picked (10-08, Tools\ClassesUI_Mockups\SummonControls_mockup.png): one row
    /// per summon (name, health, time left, a Follow / Guard / Attack toggle and X to dismiss it), "Dismiss all" at the top
    /// and an "All summons" toggle under the list. Dismissing asks first ("Dismiss Bear?"; Enter = yes, Esc = no). Everything
    /// goes through Core's SummonApi. The pill shows only for a Druid or Necromancer, or while a summon is still out.
    /// class_test: rows "Summon_&lt;i&gt;" with buttons "Mode_Follow" / "Mode_Guard" / "Mode_Attack" and "Dismiss";
    /// "DismissAll"; "AllMode_&lt;mode&gt;"; the confirm box "SummonConfirm" with "Yes" / "No".
    /// </summary>
    public class SummonsView : IClassesView, IClassesOverlay
    {
        private const float ColumnWidth = 820f;
        private const float RowHeight = 64f;
        private const float RowGap = 8f;
        private const float ModeWidth = 76f;
        private static readonly SummonMode[] Modes = { SummonMode.Follow, SummonMode.Guard, SummonMode.Attack };

        public ClassesTab Tab => ClassesTab.Summons;
        public string Title => "Summons";

        private RectTransform _page, _column, _list, _allRow;
        private TextMeshProUGUI _count, _empty, _hint;
        private Button _dismissAll;
        private RectTransform _confirm;
        private TextMeshProUGUI _confirmText;
        private System.Action _onYes;
        private string _signature;
        private readonly List<RowRefs> _rows = new List<RowRefs>();

        private sealed class RowRefs
        {
            public ZDOID Id;
            public RectTransform HealthFill;
            public TextMeshProUGUI Health, Time;
        }

        /// <summary>A Druid or Necromancer on either bar, or a summon still out after a class change.</summary>
        public static bool Available()
        {
            if (SummonApi.MyCount > 0) return true;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return false;
            return IsSummoner(data.Front.Archetype) || IsSummoner(data.Back.Archetype);
        }

        private static bool IsSummoner(ArchetypeClass archetype) =>
            archetype == ArchetypeClass.Druid || archetype == ArchetypeClass.Necromancer;

        public void Build(RectTransform page)
        {
            _page = page;
            _column = ClassesStyle.Rect(page, "Summons", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f));
            _column.sizeDelta = new Vector2(ColumnWidth, 0f);

            RectTransform head = ClassesStyle.Rect(_column, "Head", new Vector2(0f, 1f), new Vector2(1f, 1f));
            head.Fill(-6f, 52f);
            ClassesStyle.Label(head, "Title", "Summons", 26f, ClassesStyle.Gold, Vector2.zero, new Vector2(0.3f, 1f),
                TextAlignmentOptions.MidlineLeft, true);
            _count = ClassesStyle.Label(head, "Count", "", 18f, ClassesStyle.Muted, new Vector2(0.3f, 0f), new Vector2(0.6f, 1f));
            _dismissAll = ClassesStyle.Button(head, "DismissAll", "Dismiss all", ClassesStyle.ButtonKind.Neutral,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), AskDismissAll, 15f);
            Place(_dismissAll.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(170f, ClassesStyle.Target), Vector2.zero);

            _list = ClassesStyle.Rect(_column, "List", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _list.Fill(-68f, 0f);

            _empty = ClassesStyle.Label(_column, "Empty", "No summons out. Call one with a Druid Pack or Necromancer Bone skill.",
                16f, ClassesStyle.Muted, new Vector2(0f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.Center);
            _empty.rectTransform.Fill(-80f, 40f);

            _allRow = ClassesStyle.Rect(_column, "AllRow", new Vector2(0f, 1f), new Vector2(1f, 1f));
            ClassesStyle.Label(_allRow, "Label", "All summons", 16f, ClassesStyle.Muted, Vector2.zero, new Vector2(0.4f, 1f));
            for (int i = 0; i < Modes.Length; i++)
            {
                SummonMode mode = Modes[i];
                Button b = ClassesStyle.Button(_allRow, "AllMode_" + mode, mode.ToString(), ClassesStyle.ButtonKind.Outline,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), () => SetAll(mode), 14f);
                Place(b.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(ModeWidth, ClassesStyle.Target),
                    new Vector2(-(ClassesStyle.Target + 12f) - (Modes.Length - 1 - i) * (ModeWidth + 4f), 0f));
            }

            _hint = ClassesStyle.Label(_column, "Hint",
                "Follow walks with you, Guard holds its spot, Attack goes for your target. X sends one home; it is gone, with no drops.",
                13f, ClassesStyle.Muted, new Vector2(0f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.TopLeft);

            BuildConfirm(page);
            page.gameObject.AddComponent<SummonsTicker>().View = this;
        }

        public void Refresh()
        {
            _signature = null;
            Repaint();
        }

        /// <summary>
        /// Rebuilds the rows only when the summons or their modes change; health and timers are updated in place every tick,
        /// so a button is never destroyed under the pointer mid-click.
        /// </summary>
        internal void Repaint()
        {
            if (_column == null) return;
            IReadOnlyList<SummonInfo> summons = SummonApi.MySummons();
            int count = summons?.Count ?? 0;
            _count.text = SummonApi.MyCount + " / " + SummonApi.MyCap;
            ClassesStyle.Paint(_dismissAll, ClassesStyle.ButtonKind.Neutral, count > 0);

            string signature = Signature(summons);
            if (signature != _signature)
            {
                _signature = signature;
                BuildRows(summons);
            }
            for (int i = 0; i < _rows.Count && i < count; i++) PaintLive(_rows[i], summons[i]);
        }

        private static string Signature(IReadOnlyList<SummonInfo> summons)
        {
            var sb = new StringBuilder();
            if (summons != null) foreach (SummonInfo s in summons) sb.Append(s.Id).Append(':').Append((int)s.Mode).Append(';');
            return sb.ToString();
        }

        private void BuildRows(IReadOnlyList<SummonInfo> summons)
        {
            ClassesStyle.Clear(_list);
            _rows.Clear();
            int count = summons?.Count ?? 0;
            float height = count * (RowHeight + RowGap);
            _list.Fill(-68f, Mathf.Max(height, 1f));
            for (int i = 0; i < count; i++) _rows.Add(BuildRow(i, summons[i]));

            _empty.gameObject.SetActive(count == 0);
            _allRow.gameObject.SetActive(count > 0);
            float below = -68f - (count == 0 ? 56f : height + 4f);
            _allRow.Fill(below, 52f);
            if (count > 0) below -= 60f;
            _hint.rectTransform.Fill(below, 40f);
            PaintAllModes(summons);
        }

        private RowRefs BuildRow(int index, SummonInfo s)
        {
            RectTransform row = ClassesStyle.Rect(_list, "Summon_" + index, new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.Fill(-index * (RowHeight + RowGap), RowHeight);
            ClassesStyle.Fill(row, ClassesStyle.Box(8, ClassesStyle.Neutral, ClassesStyle.Line, 1), Color.white);

            RectTransform icon = ClassesStyle.Rect(row, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            Place(icon, new Vector2(0f, 0.5f), new Vector2(48f, 48f), new Vector2(8f, 0f));
            ClassesStyle.Fill(icon, ClassesStyle.Box(6, ClassesStyle.Well, ClassesStyle.Border, 1), Color.white);
            ArchetypeClass archetype = (s.SourceTag ?? "").StartsWith("rpg-necro") || (s.SourceTag ?? "").StartsWith("necromancer")
                ? ArchetypeClass.Necromancer : ArchetypeClass.Druid;
            RectTransform crest = ClassesStyle.Rect(icon, "Crest", Vector2.zero, Vector2.one);
            crest.offsetMin = new Vector2(6f, 6f);
            crest.offsetMax = new Vector2(-6f, -6f);
            ClassesStyle.Fill(crest, ClassCrests.For(archetype), ClassesStyle.ArchetypeColor(archetype));

            // Right side: X, then the three modes.
            Button dismiss = ClassesStyle.Button(row, "Dismiss", "X", ClassesStyle.ButtonKind.Outline,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), () => AskDismiss(s), 18f);
            Place(dismiss.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(ClassesStyle.Target, ClassesStyle.Target),
                new Vector2(-10f, 0f));
            dismiss.GetComponentInChildren<TMP_Text>(true).color = ClassesStyle.Hex("#d9826a");
            for (int m = 0; m < Modes.Length; m++)
            {
                SummonMode mode = Modes[m];
                ZDOID id = s.Id;
                bool on = s.Mode == mode;
                Button b = ClassesStyle.Button(row, "Mode_" + mode, mode.ToString(),
                    on ? ClassesStyle.ButtonKind.Primary : ClassesStyle.ButtonKind.Outline,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), () => SetMode(id, mode), 14f);
                Place(b.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(ModeWidth, ClassesStyle.Target),
                    new Vector2(-(ClassesStyle.Target + 22f) - (Modes.Length - 1 - m) * (ModeWidth + 4f), 0f));
            }

            float rightReserve = ClassesStyle.Target + 22f + Modes.Length * (ModeWidth + 4f) + 12f;
            RectTransform info = ClassesStyle.Rect(row, "Info", Vector2.zero, Vector2.one);
            info.offsetMin = new Vector2(68f, 8f);
            info.offsetMax = new Vector2(-rightReserve, -8f);
            ClassesStyle.Label(info, "Name", Localized(s.DisplayName), 17f, ClassesStyle.Text,
                new Vector2(0f, 0.55f), new Vector2(1f, 1f), TextAlignmentOptions.MidlineLeft, true);
            RectTransform fill = ClassesStyle.Bar(info, "Health", new Vector2(0f, 0.36f), new Vector2(1f, 0.5f), 1f);
            var refs = new RowRefs
            {
                Id = s.Id,
                HealthFill = fill,
                Health = ClassesStyle.Label(info, "HealthText", "", 13f, ClassesStyle.Muted, new Vector2(0f, 0f), new Vector2(0.6f, 0.34f)),
                Time = ClassesStyle.Label(info, "TimeText", "", 13f, ClassesStyle.Muted, new Vector2(0.6f, 0f), new Vector2(1f, 0.34f),
                    TextAlignmentOptions.MidlineRight),
            };
            PaintLive(refs, s);
            return refs;
        }

        private static void PaintLive(RowRefs row, SummonInfo s)
        {
            if (row.HealthFill == null) return;
            float frac = s.HealthMax > 0f ? Mathf.Clamp01(s.Health / s.HealthMax) : 0f;
            row.HealthFill.anchorMax = new Vector2(frac, 1f);
            Image fill = row.HealthFill.GetComponent<Image>();
            if (fill != null) fill.color = Color.Lerp(ClassesStyle.Hex("#d9644a"), ClassesStyle.Hex("#7fb069"), frac);
            row.Health.text = Mathf.CeilToInt(s.Health) + " / " + Mathf.CeilToInt(s.HealthMax);
            row.Time.text = s.Remaining >= 0f ? Clock(s.Remaining) : "until dismissed";
        }

        private void PaintAllModes(IReadOnlyList<SummonInfo> summons)
        {
            // Lit only when every summon is in that mode.
            foreach (SummonMode mode in Modes)
            {
                Transform t = _allRow.Find("AllMode_" + mode);
                Button b = t != null ? t.GetComponent<Button>() : null;
                bool all = summons != null && summons.Count > 0;
                if (summons != null) foreach (SummonInfo s in summons) if (s.Mode != mode) { all = false; break; }
                ClassesStyle.Paint(b, all ? ClassesStyle.ButtonKind.Primary : ClassesStyle.ButtonKind.Outline, true);
            }
        }

        // ---- actions ----------------------------------------------------------------------------------------

        private void SetMode(ZDOID id, SummonMode mode)
        {
            SummonApi.SetMode(id, mode);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] summon {id} set to {mode} from the Summons tab.");
            Refresh();
        }

        private void SetAll(SummonMode mode)
        {
            IReadOnlyList<SummonInfo> summons = SummonApi.MySummons();
            if (summons == null) return;
            foreach (SummonInfo s in summons) SummonApi.SetMode(s.Id, mode);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] all {summons.Count} summons set to {mode} from the Summons tab.");
            Refresh();
        }

        private void AskDismiss(SummonInfo s)
        {
            ZDOID id = s.Id;
            string name = Localized(s.DisplayName);
            Ask("Dismiss " + name + "?", () =>
            {
                SummonApi.Dismiss(id);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] dismissed {name} ({id}) from the Summons tab.");
            });
        }

        private void AskDismissAll()
        {
            int count = SummonApi.MyCount;
            if (count == 0) return;
            Ask(count == 1 ? "Dismiss your summon?" : "Dismiss all " + count + " summons?", () =>
            {
                SummonApi.DismissAll();
                FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] dismissed all {count} summons from the Summons tab.");
            });
        }

        // ---- confirm ------------------------------------------------------------------------------------------

        private void BuildConfirm(RectTransform page)
        {
            _confirm = ClassesStyle.Rect(page, "SummonConfirm", Vector2.zero, Vector2.one);
            ClassesStyle.Fill(_confirm, null, new Color(0f, 0f, 0f, 0.55f), true);
            RectTransform box = ClassesStyle.Fixed(_confirm, "Box", new Vector2(0.5f, 0.5f), new Vector2(440f, 180f));
            ClassesStyle.PanelFrame(box);
            _confirmText = ClassesStyle.Label(box, "Question", "", 22f, ClassesStyle.Text,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f), TextAlignmentOptions.Center, true);
            ClassesStyle.Label(box, "Keys", "Enter = Yes   Esc = No", 12f, ClassesStyle.Muted,
                new Vector2(0f, 0.38f), new Vector2(1f, 0.52f), TextAlignmentOptions.Center);
            Button yes = ClassesStyle.Button(box, "Yes", "Yes", ClassesStyle.ButtonKind.Primary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Confirm, 17f);
            Place(yes.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(140f, ClassesStyle.Target), new Vector2(-78f, 18f));
            Button no = ClassesStyle.Button(box, "No", "No", ClassesStyle.ButtonKind.Outline,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), () => CloseOverlay(), 17f);
            Place(no.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(140f, ClassesStyle.Target), new Vector2(78f, 18f));
            _confirm.gameObject.SetActive(false);
        }

        private void Ask(string question, System.Action onYes)
        {
            _onYes = onYes;
            _confirmText.text = question;
            _confirm.SetAsLastSibling();
            _confirm.gameObject.SetActive(true);
        }

        internal bool Asking => _confirm != null && _confirm.gameObject.activeSelf;

        internal void Confirm()
        {
            System.Action yes = _onYes;
            CloseOverlay();
            yes?.Invoke();
            Refresh();
        }

        /// <summary>Esc closes the question first (No); the window only closes when nothing was open.</summary>
        public bool CloseOverlay()
        {
            if (!Asking) return false;
            _onYes = null;
            _confirm.gameObject.SetActive(false);
            return true;
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        private static void Place(RectTransform rect, Vector2 pivot, Vector2 size, Vector2 position)
        {
            rect.anchorMin = pivot;
            rect.anchorMax = pivot;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static string Localized(string text) =>
            string.IsNullOrEmpty(text) ? "Summon" : Localization.instance != null ? Localization.instance.Localize(text) : text;

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return s >= 60 ? (s / 60) + ":" + (s % 60).ToString("00") : s + "s";
        }

        /// <summary>Keeps health and timers moving while the tab is open, and takes Enter on the question.</summary>
        private sealed class SummonsTicker : MonoBehaviour
        {
            internal SummonsView View;
            private float _next;

            private void Update()
            {
                if (View == null) return;
                if (View.Asking && (FiresKeys.GetKeyDown(KeyCode.Return) || FiresKeys.GetKeyDown(KeyCode.KeypadEnter)))
                {
                    View.Confirm();
                    return;
                }
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.25f;
                View.Repaint();
            }
        }
    }
}
