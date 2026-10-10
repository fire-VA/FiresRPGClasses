using System.Collections.Generic;
using FiresCore.Possession;
using FiresCore.Summon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The Druid / Necromancer HUD (Tools\CLASSES_DRUID_NECROMANCER.md §6c, §8b, §8d), read from Core's PossessionApi and
    /// SummonApi. Two pieces, both under the vanilla HUD root like the class bars:
    /// - while possessing, a strip above the bars: the form's name, its health and time left, and its attacks with the one
    ///   on LMB / MMB marked and the Alt + wheel selection highlighted;
    /// - while any summon is out, a list at the left: "Summons n / cap", then each one's name, health, time left and mode.
    /// Display only: both hide with the HUD, menus and the loading screen.
    /// </summary>
    public class ClassesPossessionHud : MonoBehaviour
    {
        private const string ContainerName = "FiresClassPossession";
        private const int SortOrder = 450;           // the class bars' order: over the HUD, under InventoryGui
        private const float RepaintSeconds = 0.2f;
        private const float StripWidth = 520f;
        private const float StripBottom = 330f;      // above the default bars (190) and their two rows
        private const float SummonWidth = 230f;
        private const float SummonRow = 34f;

        private RectTransform _root;
        private RectTransform _strip;
        private RectTransform _summons;
        private float _nextPaint;
        private bool _dirty = true;
        private bool _subscribed;

        private void OnEnable() => Subscribe(true);
        private void OnDisable() => Subscribe(false);

        private void Subscribe(bool on)
        {
            if (on == _subscribed) return;
            _subscribed = on;
            if (on)
            {
                PossessionApi.Started += OnPossession;
                PossessionApi.Ended += OnEnded;
                PossessionApi.AttackChanged += OnPossession;
                SummonApi.Changed += OnSummons;
            }
            else
            {
                PossessionApi.Started -= OnPossession;
                PossessionApi.Ended -= OnEnded;
                PossessionApi.AttackChanged -= OnPossession;
                SummonApi.Changed -= OnSummons;
            }
        }

        private void OnPossession(PossessHandle _) => _dirty = true;
        private void OnEnded(PossessHandle _, PossessEndReason __) => _dirty = true;
        private void OnSummons() => _dirty = true;

        private void Update()
        {
            if (Player.m_localPlayer == null || HudHidden()) { SetActive(false); return; }
            if (!EnsureBuilt()) return;
            SetActive(true);

            // Health and timers move every frame; a rebuild every 0.2 s (or on an event) is plenty for numbers.
            if (!_dirty && Time.unscaledTime < _nextPaint) return;
            _dirty = false;
            _nextPaint = Time.unscaledTime + RepaintSeconds;
            PaintPossession(PossessionApi.Current);
            PaintSummons(SummonApi.MySummons());
        }

        private static bool HudHidden()
        {
            if (Hud.instance == null || Hud.IsUserHidden() || !Hud.instance.IsVisible()) return true;
            CanvasGroup black = Hud.instance.m_loadingScreen;
            if (black != null && black.gameObject.activeInHierarchy && black.alpha > 0.01f) return true;
            if (Menu.IsVisible()) return true;
            return Game.instance != null && Game.instance.IsShuttingDown();
        }

        private void SetActive(bool on)
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        // ---- possession strip -----------------------------------------------------------------------------

        private void PaintPossession(PossessHandle handle)
        {
            ClassesStyle.Clear(_strip);
            bool on = handle != null && handle.Creature != null;
            _strip.gameObject.SetActive(on);
            if (!on) return;

            ClassesStyle.Fill(_strip, ClassesStyle.Box(8, ClassesStyle.Panel, ClassesStyle.Border, 2), new Color(1f, 1f, 1f, 0.92f));

            string name = Localization.instance != null ? Localization.instance.Localize(handle.Creature.m_name) : handle.Creature.m_name;
            string time = handle.Remaining >= 0f ? "  ·  " + Clock(handle.Remaining) : "";
            ClassesStyle.Label(_strip, "Form", name + time, 16f, ClassesStyle.Gold,
                new Vector2(0f, 0.62f), new Vector2(0.55f, 1f), TextAlignmentOptions.MidlineLeft, true)
                .rectTransform.offsetMin = new Vector2(12f, 0f);

            // The form's own pool when it has one, else the creature's health.
            float hp = handle.FormHealthMax > 0f ? handle.FormHealth : handle.Creature.GetHealth();
            float max = handle.FormHealthMax > 0f ? handle.FormHealthMax : handle.Creature.GetMaxHealth();
            RectTransform bar = ClassesStyle.Rect(_strip, "Health", new Vector2(0.56f, 0.70f), new Vector2(1f, 0.90f));
            bar.offsetMax = new Vector2(-12f, 0f);
            RectTransform fill = ClassesStyle.Bar(bar, "Track", Vector2.zero, Vector2.one, max > 0f ? hp / max : 0f);
            fill.GetComponent<Image>().color = Color.Lerp(ClassesStyle.Hex("#d9644a"), ClassesStyle.Hex("#7fb069"), max > 0f ? hp / max : 0f);

            IReadOnlyList<PossessAttackInfo> attacks = handle.Attacks;
            int count = attacks?.Count ?? 0;
            if (count == 0) return;
            float width = 1f / count;
            for (int i = 0; i < count; i++)
            {
                PossessAttackInfo attack = attacks[i];
                RectTransform chip = ClassesStyle.Rect(_strip, "Attack_" + i, new Vector2(i * width, 0.08f), new Vector2((i + 1) * width, 0.56f));
                chip.offsetMin = new Vector2(i == 0 ? 10f : 3f, 0f);
                chip.offsetMax = new Vector2(i == count - 1 ? -10f : -3f, 0f);
                bool current = i == handle.CurrentIndex;
                ClassesStyle.Fill(chip, current
                    ? ClassesStyle.Box(6, ClassesStyle.Selected, ClassesStyle.Gold, 2)
                    : ClassesStyle.Box(6, ClassesStyle.Well, ClassesStyle.Line, 1), Color.white);

                string key = i == handle.MainIndex ? "LMB  " : i == handle.SpecialIndex ? "MMB  " : "";
                ClassesStyle.Label(chip, "Name", key + Pretty(attack.Name) + (attack.Ranged ? " (ranged)" : ""), 13f,
                    current ? ClassesStyle.Gold : key.Length > 0 ? ClassesStyle.Text : ClassesStyle.Muted,
                    Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            }
            if (count > 2)
                ClassesStyle.Label(_strip, "Hint", "Alt + wheel", 11f, ClassesStyle.Muted,
                    new Vector2(0.56f, 0.56f), new Vector2(1f, 0.70f), TextAlignmentOptions.MidlineRight)
                    .rectTransform.offsetMax = new Vector2(-12f, 0f);
        }

        /// <summary>"Bite_Wolf" / "attack_slash" read as "Bite" / "Slash".</summary>
        private static string Pretty(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Attack";
            string s = raw.Replace("attack_", "").Replace("Attack_", "");
            int cut = s.IndexOf('_');
            if (cut > 0) s = s.Substring(0, cut);
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : "Attack";
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return s >= 60 ? (s / 60) + ":" + (s % 60).ToString("00") : s + "s";
        }

        // ---- summon list ----------------------------------------------------------------------------------

        private void PaintSummons(IReadOnlyList<SummonInfo> summons)
        {
            ClassesStyle.Clear(_summons);
            int count = summons?.Count ?? 0;
            _summons.gameObject.SetActive(count > 0);
            if (count == 0) return;

            _summons.sizeDelta = new Vector2(SummonWidth, 26f + count * SummonRow + 8f);
            ClassesStyle.Fill(_summons, ClassesStyle.Box(8, ClassesStyle.Panel, ClassesStyle.Border, 2), new Color(1f, 1f, 1f, 0.88f));
            RectTransform head = ClassesStyle.Rect(_summons, "Head", new Vector2(0f, 1f), new Vector2(1f, 1f));
            head.offsetMin = new Vector2(10f, -26f);
            head.offsetMax = new Vector2(-10f, -4f);
            ClassesStyle.Label(head, "Title", "Summons  " + SummonApi.MyCount + " / " + SummonApi.MyCap, 13f, ClassesStyle.Gold,
                Vector2.zero, Vector2.one, TextAlignmentOptions.MidlineLeft, true);

            for (int i = 0; i < count; i++)
            {
                SummonInfo s = summons[i];
                RectTransform row = ClassesStyle.Rect(_summons, "Summon_" + i, new Vector2(0f, 1f), new Vector2(1f, 1f));
                float top = -26f - i * SummonRow;
                row.offsetMin = new Vector2(10f, top - SummonRow + 3f);
                row.offsetMax = new Vector2(-10f, top);

                string time = s.Remaining >= 0f ? "  " + Clock(s.Remaining) : "";
                ClassesStyle.Label(row, "Name", Localized(s.DisplayName) + time, 12f, ClassesStyle.Text,
                    new Vector2(0f, 0.45f), new Vector2(0.72f, 1f), TextAlignmentOptions.BottomLeft);
                ClassesStyle.Label(row, "Mode", s.Mode.ToString(), 11f, ModeColor(s.Mode),
                    new Vector2(0.72f, 0.45f), new Vector2(1f, 1f), TextAlignmentOptions.BottomRight);
                float frac = s.HealthMax > 0f ? Mathf.Clamp01(s.Health / s.HealthMax) : 0f;
                RectTransform fill = ClassesStyle.Bar(row, "Health", new Vector2(0f, 0.08f), new Vector2(1f, 0.36f), frac);
                fill.GetComponent<Image>().color = Color.Lerp(ClassesStyle.Hex("#d9644a"), ClassesStyle.Hex("#7fb069"), frac);
            }
        }

        private static string Localized(string text) =>
            string.IsNullOrEmpty(text) ? "Summon" : Localization.instance != null ? Localization.instance.Localize(text) : text;

        private static Color ModeColor(SummonMode mode)
        {
            switch (mode)
            {
                case SummonMode.Attack: return ClassesStyle.Hex("#d9644a");
                case SummonMode.Guard: return ClassesStyle.Blue;
                default: return ClassesStyle.Muted;
            }
        }

        // ---- building -------------------------------------------------------------------------------------

        private bool EnsureBuilt()
        {
            if (_root != null) return true;
            if (Hud.instance == null || Hud.instance.m_rootObject == null) return false;

            var go = new GameObject(ContainerName);
            go.transform.SetParent(Hud.instance.m_rootObject.transform, false);
            _root = go.AddComponent<RectTransform>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortOrder;

            _strip = ClassesStyle.Fixed(_root, "PossessionStrip", new Vector2(0.5f, 0f), new Vector2(StripWidth, 74f));
            _strip.pivot = new Vector2(0.5f, 0f);
            _strip.anchoredPosition = new Vector2(0f, StripBottom);
            _strip.gameObject.SetActive(false);

            _summons = ClassesStyle.Fixed(_root, "SummonList", new Vector2(0f, 0.5f), new Vector2(SummonWidth, 60f));
            _summons.pivot = new Vector2(0f, 0.5f);
            _summons.anchoredPosition = new Vector2(16f, -40f);
            _summons.gameObject.SetActive(false);

            _dirty = true;
            return true;
        }
    }
}
