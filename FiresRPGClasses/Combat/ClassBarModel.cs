using FiresRPGClasses.Data;
using FiresRPGClasses.Input;
using FiresRPGClasses.State;
using FiresRPGClasses.UI;
using FiresCore.Input;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>The kit slot state (mirrors the spec's HotbarSlotState; kept here so this file needs no UI / kit types).</summary>
    public enum ClassBarSlotState { Normal, Empty, Disabled, Selected }

    /// <summary>One class-bar slot, as the hotbar kit draws it. Reused per slot; Fill only changes what differs.</summary>
    public sealed class ClassBarSlotModel
    {
        public Sprite Icon;
        public Color Tint = Color.white;
        public float Cooldown01;          // 0 = ready (no sweep)
        public int SecondsShown = -1;     // whole seconds left, -1 = none; the TEXT is rebuilt only when this changes
        public bool SecondsDirty;         // true on the frame SecondsShown changed (repaint only on change)
        public int Charges = -1, MaxCharges;   // class skills have no charges today: -1 = no pips
        public string KeyLabel = "";
        public string KeyTooltip = "";    // the full chord ("Shift+Alt+1") for the tooltip
        public ClassBarSlotState State = ClassBarSlotState.Empty;
        public string DisabledReason;     // why it's Disabled (the Availability name), null otherwise
        public bool IsWeapon, IsUltimate;
        internal int KeySig = int.MinValue;   // the binding last labelled; the strings rebuild only when it changes
    }

    /// <summary>
    /// The DATA side of the class front/back bars
    /// for Core's kit HotbarSlot/HotbarRow, with no UI or kit types, so it compiles on any Core. It is drawn on the ON path
    /// (two HotbarRows in ClassesHud); OFF stays the clone path, which doesn't use this. Same sources and rules as ClassesHud.Paint
    /// today: ClassIcons / the bar's weapon art, the readiness tint, CooldownTracker fractions, the whole-second text only on change.
    /// Look choices (back-bar opacity, the well fill, possession chips) are NOT here: they're draw-side keys.
    /// </summary>
    public static class ClassBarModel
    {
        /// <summary>Fills <paramref name="slots"/> (length >= BarLayout.SlotCount) for one bar. No allocations once warm.</summary>
        public static void Fill(BarSide side, ClassBarSlotModel[] slots)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || slots == null) return;
            BarLayout bar = data.Bar(side);
            bool activeBar = side == data.ActiveBar;
            for (int i = 0; i < BarLayout.SlotCount && i < slots.Length; i++)
            {
                int slot = i + 1;
                ClassBarSlotModel m = slots[i] ?? (slots[i] = new ClassBarSlotModel());
                m.IsWeapon = slot == BarLayout.WeaponSlot;
                m.IsUltimate = slot == BarLayout.UltimateSlot;
                string nodeId = m.IsUltimate ? bar.UltimateId : (m.IsWeapon ? null : bar.SkillAt(slot));

                m.Icon = m.IsWeapon ? WeaponArt(side, bar) : ClassIcons.For(nodeId);
                SlotAvailability a = m.IsWeapon ? (activeBar && !BarController.HoldingBarWeapon(side) ? SlotAvailability.WrongWeapon : SlotAvailability.Ready)
                                               : BarController.Availability(side, slot);
                m.Tint = TintFor(a);
                m.State = m.Icon == null ? ClassBarSlotState.Empty
                        : a == SlotAvailability.Ready || a == SlotAvailability.OnCooldown ? (m.IsWeapon && activeBar ? ClassBarSlotState.Selected : ClassBarSlotState.Normal)
                        : ClassBarSlotState.Disabled;
                m.DisabledReason = m.State == ClassBarSlotState.Disabled ? a.ToString() : null;

                float total = m.IsWeapon ? 0f : TotalCooldown(data, bar, slot);
                m.Cooldown01 = m.IsWeapon || string.IsNullOrEmpty(nodeId) ? 0f : CooldownTracker.Fraction(nodeId, total);
                int secs = m.Cooldown01 > 0f ? Mathf.CeilToInt(CooldownTracker.Remaining(nodeId)) : -1;
                m.SecondsDirty = secs != m.SecondsShown;
                m.SecondsShown = secs;

                KeyBinding key = ClassKeys.Slot(slot);
                int sig = key == null ? -1 : ((int)key.Key.Value << 3) | (key.Ctrl.Value ? 1 : 0) | (key.Shift.Value ? 2 : 0) | (key.Alt.Value ? 4 : 0);
                if (sig != m.KeySig) { m.KeySig = sig; m.KeyTooltip = ClassKeys.SlotLabel(slot); m.KeyLabel = key == null ? slot.ToString() : Abbreviate(key); }
            }
        }

        /// <summary>The spec's chip text: "S+A+1" for Shift+Alt+1, "C+A+1", "Alt+1"; the full chord goes in KeyTooltip.</summary>
        public static string Abbreviate(KeyBinding key)
        {
            if (key == null) return "";
            string main = KeyName(key.Key.Value);
            int mods = (key.Ctrl.Value ? 1 : 0) + (key.Shift.Value ? 1 : 0) + (key.Alt.Value ? 1 : 0);
            if (mods == 0) return main;
            if (mods == 1) return (key.Ctrl.Value ? "Ctrl+" : key.Shift.Value ? "Shift+" : "Alt+") + main;
            return (key.Ctrl.Value ? "C+" : "") + (key.Shift.Value ? "S+" : "") + (key.Alt.Value ? "A+" : "") + main;
        }

        private static string KeyName(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "N" + (int)(k - KeyCode.Keypad0);
            return k == KeyCode.None ? "" : k.ToString();
        }

        // As ClassesHud.Paint today (kept in step): white art tinted only by readiness
        private static Color TintFor(SlotAvailability a)
        {
            switch (a)
            {
                case SlotAvailability.Ready: return Color.white;
                case SlotAvailability.OnCooldown: return new Color(0.70f, 0.75f, 1f, 1f);
                default: return new Color(1f, 1f, 1f, 0.45f);
            }
        }

        private static Sprite WeaponArt(BarSide side, BarLayout bar)
        {
            ItemDrop.ItemData weapon = BarController.HasBarSlots ? BarController.BarWeapon(side) : BarController.FindInInventory(bar.WeaponPrefab);
            return weapon?.GetIcon();
        }

        private static float TotalCooldown(ClassPlayerData data, BarLayout bar, int slot)
        {
            if (slot == BarLayout.UltimateSlot)
            {
                UltimateDefinition definition = UltimateCatalog.Get(bar.UltimateId);
                return definition == null ? 0f : UltimateController.EffectiveCooldown(definition, data.UltimateLevel(definition.Id));
            }
            return ClassTrees.Node(bar.SkillAt(slot))?.Cooldown ?? 0f;
        }
    }
}
