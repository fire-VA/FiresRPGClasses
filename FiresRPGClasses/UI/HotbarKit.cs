using FiresCore.UI.Shell.Kit;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>The one seam to Core's hotbar kit (Widgets.HotbarRow + tokens).</summary>
    internal static class HotbarKit
    {
        internal static float Pitch => FiresTokens.HotbarSlotSize + FiresTokens.HotbarSlotGap;
        internal static KitHotbarRow Row(RectTransform parent, string id, int slots, float x, float y, HotbarStyle style) =>
            Widgets.HotbarRow(parent, id, slots, x, y, style);

        /// <summary>Places a kit row's slots on a cols-wide grid from its top-left (the kit's own Place convention), slot size unchanged.</summary>
        internal static void Grid(KitHotbarRow row, int cols, float size, float gapX, float gapY)
        {
            int n = row.Slots.Length;
            if (n == 0) return;
            cols = Mathf.Clamp(cols, 1, n);
            int rows = (n + cols - 1) / cols;
            row.Root.sizeDelta = new Vector2(cols * size + (cols - 1) * gapX, rows * size + (rows - 1) * gapY);
            for (int i = 0; i < n; i++)
            {
                RectTransform s = row.Slots[i].Root;
                if (s == null) continue;
                s.anchoredPosition = new Vector2((i % cols) * (size + gapX), -(i / cols) * (size + gapY));
            }
        }
    }
}