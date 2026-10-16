using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FiresCore.Possession;
using FiresCore.UI.Shell;
using FiresCore.UI.Shell.Kit;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// UI overhaul #14, possession chips (Fire approved Tools\golden\ui_overhaul\hotbars\bar_rpg_possess.png): with the override ON and
    /// Core's hotbar kit present, the possessed form's attacks are a kit HotbarRow in the same spot as the strip (under the vanilla
    /// hotbar's bottom-left, PlaceStrip). Keys LMB / MMB / Alt under each slot (Core 0.2.528 layout); the Alt + wheel pick is Selected;
    /// a caption keeps the form's name, time left and health. Built once per form, then only repainted. Every kit type lives in the
    /// nested KitStrip (held as object, reached from NoInlining methods), so an older Core keeps today's text strip.
    /// </summary>
    public partial class ClassesPossessionHud
    {
        private object _kit;      // a KitStrip while the kit look is showing
        private bool _kitMode;

        private static bool KitOn() { try { return ClassesHud.KitAvailable && FiresShell.OverrideOn; } catch { return false; } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PaintPossessionKit(PossessHandle handle)
        {
            var k = _kit as KitStrip;
            if (k == null) _kit = k = new KitStrip(this);
            k.Paint(handle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RelayoutKitStrip(Vector2 want) => (_kit as KitStrip)?.Relayout(want);

        private sealed class KitStrip
        {
            private readonly ClassesPossessionHud _h;
            private KitHotbarRow _row;
            private TextMeshProUGUI _caption;
            private int _form = int.MinValue;

            internal KitStrip(ClassesPossessionHud hud) { _h = hud; }

            internal void Paint(PossessHandle handle)
            {
                RectTransform strip = _h._strip;
                bool on = handle != null && handle.Creature != null;
                strip.gameObject.SetActive(on);
                if (!on) return;
                IReadOnlyList<PossessAttackInfo> attacks = handle.Attacks;
                int count = attacks?.Count ?? 0;

                int form = handle.Creature.GetInstanceID() * 31 + count;
                if (_row == null || form != _form)
                {
                    ClassesStyle.Clear(strip);
                    _row = null; _caption = null;
                    _form = form;
                    if (count == 0) return;
                    _row = HotbarKit.Row(strip, "Possession", count, 0f, 0f, HotbarStyle.Normal);
                    if (_row == null) { FiresRPGClassesPlugin.Log?.LogWarning("[Classes] possession: kit HotbarRow returned null"); return; }
                    _row.Root.anchorMin = _row.Root.anchorMax = new Vector2(0f, 1f);
                    _row.Root.pivot = new Vector2(0f, 1f);
                    _row.Root.anchoredPosition = Vector2.zero;
                    _caption = ClassesStyle.Label(strip, "Form", "", 14f, ClassesStyle.Gold, new Vector2(0f, 1f), new Vector2(0f, 1f), TextAlignmentOptions.TopLeft);
                    _caption.rectTransform.pivot = new Vector2(0f, 1f);
                    _caption.rectTransform.anchoredPosition = new Vector2(_row.Width + 12f, -4f);
                    _caption.rectTransform.sizeDelta = new Vector2(220f, 60f);
                    strip.sizeDelta = new Vector2(_row.Width + 12f + 220f, 64f + 17f);   // 17 = Core's key chip under the slot
                    for (int i = 0; i < count; i++)
                    {
                        KitHotbarSlot s = _row.Slots[i];
                        s.SetCount(0); s.SetDurability(-1f); s.SetCharges(-1, 0); s.SetCooldown(0f, 0f);
                        Sprite icon = AttackIcon(attacks[i].Name);
                        s.SetIcon(icon, Color.white);
                        if (icon == null)   // creature attacks often have no icon: the short name stands in, inside the slot
                            ClassesStyle.Label(s.Root, "AttackName", Pretty(attacks[i].Name), 12f, ClassesStyle.Text, Vector2.zero, Vector2.one, TextAlignmentOptions.Center).raycastTarget = false;
                    }
                    // 0.1.173: a player-given strip size (Core resize) re-flows the new form's slots into it; none = today's 1 x n row
                    if (_h._stripDocked && ClassesHudSizes.TryGet(StripDockId, out Vector2 saved)) { ClassesHudSizes.ResizeKeepTopLeft(strip, saved); Relayout(saved); }
                    FiresRPGClassesPlugin.Log?.LogInfo($"[Classes] possession: kit row for {handle.Creature.name}: {count} attack(s) (LMB {handle.MainIndex}, MMB {handle.SpecialIndex}, current {handle.CurrentIndex})");
                }
                if (_row == null) return;

                for (int i = 0; i < count && i < _row.Slots.Length; i++)
                {
                    KitHotbarSlot s = _row.Slots[i];
                    s.SetKey(i == handle.MainIndex ? "LMB" : i == handle.SpecialIndex ? "MMB" : "Alt");
                    s.SetState(i == handle.CurrentIndex ? HotbarSlotState.Selected : HotbarSlotState.Normal);
                }
                string name = Localization.instance != null ? Localization.instance.Localize(handle.Creature.m_name) : handle.Creature.m_name;
                float hp = handle.FormHealthMax > 0f ? handle.FormHealth : handle.Creature.GetHealth();
                float max = handle.FormHealthMax > 0f ? handle.FormHealthMax : handle.Creature.GetMaxHealth();
                string time = handle.Remaining >= 0f ? "  ·  " + Clock(handle.Remaining) : "";
                if (_caption != null)
                    _caption.text = name + time + "\n<size=12>" + (max > 0f ? Mathf.RoundToInt(hp / max * 100f) + "% health" : "") + (count > 2 ? "  ·  Alt + wheel" : "") + "</size>";
            }

            private const float CaptionW = 220f, CaptionGap = 12f, CaptionBelowH = 40f, Chip = 17f, RowStep = 18f;

            /// <summary>0.1.173: the caption stays beside the slots while the width leaves room for it (today), else goes under them; the
            /// slots take cols = what fits (fewer when the height fits more rows), rows a key chip apart. Today's size = today's 1 x n row.</summary>
            internal void Relayout(Vector2 want)
            {
                if (_row == null || _row.Root == null) return;
                RectTransform strip = _h._strip;
                int n = _row.Slots.Length;
                float size = FiresTokens.HotbarSlotSize, gap = FiresTokens.HotbarSlotGap;
                bool side = want.x - CaptionGap - CaptionW >= size;
                float gridW = side ? want.x - CaptionGap - CaptionW : want.x;
                int fit = 1;
                for (int r = 2; r <= n; r++)
                {
                    float h = r * size + (r - 1) * RowStep + Chip + (side ? 0f : 4f + CaptionBelowH);
                    if (h <= want.y + 0.5f) fit = r; else break;
                }
                int cols = ClassesHudSizes.Cols(n, gridW, HotbarKit.Pitch, gap, 1, fit);
                int rows = (n + cols - 1) / cols;
                HotbarKit.Grid(_row, cols, size, gap, RowStep);
                float w = _row.Root.rect.width, gh = _row.Root.rect.height;
                if (_caption != null)
                {
                    RectTransform c = _caption.rectTransform;
                    if (side) { c.anchoredPosition = new Vector2(w + CaptionGap, -4f); c.sizeDelta = new Vector2(CaptionW, 60f); }
                    else { c.anchoredPosition = new Vector2(0f, -(gh + Chip + 4f)); c.sizeDelta = new Vector2(Mathf.Max(want.x, w), CaptionBelowH); }
                }
                ClassesHudSizes.GrowHeightKeepTop(strip, side ? Mathf.Max(gh + Chip, 64f) : gh + Chip + 4f + CaptionBelowH);
                ClassesHudSizes.NoteGrid(StripDockId, cols, rows);
            }
        }

        private static readonly Dictionary<string, Sprite> s_attackIcons = new Dictionary<string, Sprite>();
        private static Sprite AttackIcon(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            if (s_attackIcons.TryGetValue(prefab, out var cached)) return cached;
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            Sprite icon = null;
            try { icon = drop != null && drop.m_itemData?.m_shared?.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0 ? drop.m_itemData.GetIcon() : null; } catch { }
            s_attackIcons[prefab] = icon;
            return icon;
        }
    }
}
