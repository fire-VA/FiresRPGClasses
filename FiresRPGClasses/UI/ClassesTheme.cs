using FiresRPGClasses.Combat;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>Shared sizes, colours and row scaffolding for the five Classes views.</summary>
    public static class ClassesTheme
    {
        public const float RowHeight = 34f;
        public const float RowGap = 4f;
        public const float HeaderHeight = 44f;
        public const float TabHeight = 34f;
        public const float FooterHeight = 30f;

        public const float TitleFontSize = 22f;
        public const float RowFontSize = 14f;
        public const float SmallFontSize = 12f;

        public static readonly Color Ready = UIFontConfig.Colors.Gold;
        public static readonly Color Dim = UIFontConfig.Colors.Gray;
        public static readonly Color Locked = UIFontConfig.Colors.Red;
        public static readonly Color Cooling = UIFontConfig.Colors.CooldownBlue;
        public static readonly Color Good = UIFontConfig.Colors.Green;

        public static Color ForAvailability(SlotAvailability availability)
        {
            switch (availability)
            {
                case SlotAvailability.Ready: return Ready;
                case SlotAvailability.OnCooldown: return Cooling;
                case SlotAvailability.Empty: return Dim;
                default: return Locked;
            }
        }

        public static Color ForPassive(PassiveStatus status)
        {
            switch (status)
            {
                case PassiveStatus.Active: return Good;
                case PassiveStatus.NotEquipped: return Ready;
                case PassiveStatus.ConditionsUnmet: return Cooling;
                default: return Dim;
            }
        }

        /// <summary>One stacked row inside a scroll content rect, returning the row's own transform.</summary>
        public static RectTransform Row(RectTransform content, string name, int index, float height = RowHeight)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(content, false);
            RectTransform rect = row.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(0f, -index * (height + RowGap));
            rect.sizeDelta = new Vector2(0f, height);
            return rect;
        }

        public static void SetContentHeight(RectTransform content, int rows, float height = RowHeight)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, rows * (height + RowGap) + RowGap);
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, Color color,
            Vector2 anchorMin, Vector2 anchorMax, float fontSize = RowFontSize,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft) =>
            UIBuilderHelper.CreateLabel(parent, name, text, fontSize, color, anchorMin, anchorMax, alignment);

        public static Button Button(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
            UnityEngine.Events.UnityAction onClick) =>
            UIBuilderHelper.CreateParchmentButton(parent, label, anchorMin, anchorMax, onClick);

        /// <summary>
        /// DestroyImmediate, not Destroy. Every board calls this at the top of Refresh and then repopulates
        /// in the SAME call, and Object.Destroy is deferred to end of frame - so the old rows were still
        /// alive and still drawing while the new ones were added on top of them, at identical positions
        /// because Row() places by index. The result reads as wrong DATA (stale numbers, doubled rows)
        /// rather than as the lifetime bug it is, which is exactly how it was misdiagnosed elsewhere in the
        /// family. This matters more since the inventory tab refreshes on every open.
        /// </summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }
}
