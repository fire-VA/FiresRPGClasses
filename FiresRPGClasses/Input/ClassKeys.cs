using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using FiresCore.Input;
using UnityEngine;

namespace FiresRPGClasses.Input
{
    public enum SkillModifier
    {
        None,
        Alt,
        Ctrl,
        Shift,
        ShiftAlt
    }

    /// <summary>
    /// Doc 14's four key schemes. Every slot key is a Core KeyBinding so it gets strict modifier matching, a
    /// context, and a declared hand-off from Valheim's Hotbar 1-8. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class ClassKeys
    {
        // Numbered sections, the keys in their own sections. Old values move over once at load
        // (FiresRPGClassesPlugin.TidyConfig).
        public const string BarsSection = "1 - Bars";
        public const string BarKeysSection = "2 - Bar keys";
        public const string ItemHotbarSection = "3 - Item hotbar";
        public const string ItemHotbarKeysSection = "4 - Item hotbar keys";

        private const int SlotCount = 8;
        private const string VanillaHotbarButtonPrefix = "Hotbar";

        private static readonly KeyCode[] DefaultSlotKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8,
        };

        private static readonly KeyBinding[] SlotKeys = new KeyBinding[SlotCount];
        private static readonly KeyBinding[] ItemHotbarKeys = new KeyBinding[SlotCount];

        public static KeyBinding BarSwap { get; private set; }

        public static ConfigEntry<bool> SkillsNeedDrawnWeapon { get; private set; }
        public static ConfigEntry<SkillModifier> Modifier { get; private set; }
        public static ConfigEntry<bool> SlotOneSwapsBars { get; private set; }
        public static ConfigEntry<SkillModifier> ItemHotbarModifier { get; private set; }

        public static bool IsBound => SlotKeys[0] != null;

        public static void Bind(ConfigFile config)
        {
            if (IsBound) return;

            SkillsNeedDrawnWeapon = config.Bind(BarsSection, "Skills need a drawn weapon", true,
                "Keys 1-8 cast skills only while a weapon is drawn; sheathed, they use the item hotbar.");
            // Shift+Alt, since players are likely sprinting while swapping bars, and plain Alt+1-4 stays
            // VAInventory's tool hotbar. A new key name, so the old "Skill modifier = Alt" in existing configs does not win.
            Modifier = config.Bind(BarsSection, "Skill keys modifier", SkillModifier.ShiftAlt,
                "The modifier held with the skill slot keys while they are on their default keys.");
            SlotOneSwapsBars = config.Bind(BarsSection, "Slot 1 swaps bars", true,
                "Slot 1's key also swaps bars, as well as the dedicated swap key.");
            ItemHotbarModifier = config.Bind(ItemHotbarSection, "Item hotbar modifier", SkillModifier.Alt,
                "The modifier held with the item hotbar keys while they are on their default keys.");

            for (int slot = 0; slot < SlotCount; slot++)
            {
                KeyBinding binding = new KeyBinding(config, BarKeysSection, "Slot" + (slot + 1),
                    DefaultSlotKeys[slot], BindingContext.WeaponDrawn);
                string vanillaButton = VanillaHotbarButtonPrefix + (slot + 1);
                binding.Replaces(vanillaButton, BindingContext.WeaponDrawn);
                binding.Replaces(vanillaButton, BindingContext.Gameplay);
                SlotKeys[slot] = binding;

                ItemHotbarKeys[slot] = new KeyBinding(config, ItemHotbarKeysSection, "Item" + (slot + 1),
                    DefaultSlotKeys[slot], BindingContext.Gameplay, defaultAlt: true);
                // the item keys stand in for vanilla's HotbarN too (read only while the slot keys
                // own 1-8, when UseHotbarItem is suppressed and these route through UseThroughHandoff), so say so to Core's checker.
                ItemHotbarKeys[slot].Replaces(vanillaButton, BindingContext.Gameplay);
            }

            BarSwap = new KeyBinding(config, BarKeysSection, "BarSwap", KeyCode.Mouse3, BindingContext.WeaponDrawn);

            ApplyScheme();
            SkillsNeedDrawnWeapon.SettingChanged += (s, e) => ApplyScheme();
            Modifier.SettingChanged += (s, e) => ApplyScheme();
            ItemHotbarModifier.SettingChanged += (s, e) => ApplyScheme();
        }

        public static KeyBinding Slot(int slot) =>
            slot >= 1 && slot <= SlotCount ? SlotKeys[slot - 1] : null;

        /// <summary>
        /// Presses slot N's BOUND chord (its key plus whatever modifiers the player set) through Core's FiresKeys, the way a
        /// player's keyboard would, for a bot driven by Core's brain (automated fights, the skills drill). Takes the seam for
        /// <paramref name="owner"/>; false when another owner holds it or the slot is unbound. The caller releases the seam
        /// (FiresKeys.Release(owner)) when its fight ends.
        /// </summary>
        public static bool PressSlot(object owner, int slot)
        {
            KeyBinding binding = Slot(slot);
            if (owner == null || binding == null || binding.Key.Value == KeyCode.None) return false;
            if (!FiresKeys.Acquire(owner)) return false;
            var modifiers = new List<KeyCode>();
            if (binding.Ctrl.Value) modifiers.Add(KeyCode.LeftControl);
            if (binding.Shift.Value) modifiers.Add(KeyCode.LeftShift);
            if (binding.Alt.Value) modifiers.Add(KeyCode.LeftAlt);
            return FiresKeys.Press(owner, binding.Key.Value, modifiers.ToArray());
        }

        /// <summary>
        /// PressSlot with a point (Core's brain choosing where): a Placed or Aimed skill on that slot is cast
        /// straight at <paramref name="point"/> (clamped to its range), with no aiming step; any other skill ignores it.
        /// </summary>
        public static bool PressSlotAt(object owner, int slot, Vector3 point)
        {
            if (!PressSlot(owner, slot)) return false;
            _placedSlot = slot;
            _placedPoint = point;
            _placedFrame = Time.frameCount;
            return true;
        }

        // The press lands on the next frame; a point older than a few frames is stale.
        private const int PlacedFrames = 5;
        private static int _placedSlot;
        private static Vector3 _placedPoint;
        private static int _placedFrame = -1000;

        internal static bool TakePlacedPoint(int slot, out Vector3 point)
        {
            point = _placedPoint;
            if (_placedSlot != slot || Time.frameCount - _placedFrame > PlacedFrames) return false;
            _placedSlot = 0;
            return true;
        }

        /// <summary>
        /// The compact label the HUD prints in a slot's binding corner. Vanilla shows one digit there, so a
        /// modifier reads as "Alt+" rather than the full "Alt+Alpha1" the binding reports, and the HUD shrinks the
        /// label to fit its slot instead of wrapping it.
        /// </summary>
        public static string SlotLabel(int slot)
        {
            KeyBinding binding = Slot(slot);
            if (binding == null) return slot.ToString();

            string prefix = string.Empty;
            if (binding.Ctrl.Value) prefix += "Ctrl+";
            if (binding.Shift.Value) prefix += "Shift+";
            if (binding.Alt.Value) prefix += "Alt+";
            return prefix + ShortKey(binding.Key.Value);
        }

        /// <summary>The bound key as the HUD prints it: "1" for Alpha1, "Num1" for Keypad1, "M3" for Mouse3, else the key's name.</summary>
        private static string ShortKey(KeyCode key)
        {
            string name = key.ToString();
            if (name.StartsWith("Alpha", System.StringComparison.Ordinal)) return name.Substring(5);
            if (name.StartsWith("Keypad", System.StringComparison.Ordinal)) return "Num" + name.Substring(6);
            if (name.StartsWith("Mouse", System.StringComparison.Ordinal)) return "M" + name.Substring(5);
            return key == KeyCode.None ? "-" : name;
        }

        public static KeyBinding ItemHotbarKey(int slot) =>
            slot >= 1 && slot <= SlotCount ? ItemHotbarKeys[slot - 1] : null;

        /// <summary>True when the scheme puts the item hotbar behind its own modifier instead of 1-8.</summary>
        public static bool ItemHotbarNeedsOwnKeys =>
            IsBound && !SkillsNeedDrawnWeapon.Value && Modifier.Value == SkillModifier.None;

        /// <summary>
        /// Doc 14: the vanilla hotbar is suppressed exactly while our slot keys own 1-8.
        ///
        /// It is NOT enough to ask whether a weapon is drawn. Holding any weapon or tool made this true,
        /// so pressing 1 with a hammer out stopped selecting the hammer and reported a refusal instead.
        /// The bar must also have something to cast: an unusable bar hands 1-8 straight back to vanilla,
        /// which is what a player pressing a number key expects.
        /// </summary>
        public static bool SuppressVanillaHotbar()
        {
            if (!IsBound) return false;
            if (!BarHasSomethingToCast()) return false;
            if (Modifier.Value != SkillModifier.None) return ModifierHeld(Modifier.Value);
            if (!SkillsNeedDrawnWeapon.Value) return true;
            return WeaponDrawn();
        }

        /// <summary>
        /// The same question asked of ONE slot. BarHasSomethingToCast is a whole-bar test, so a player with any
        /// skill slotted anywhere lost every number key - press 1 with a hammer in hotbar 1 and the row was
        /// suppressed for a class slot that was empty, so the hammer neither selected nor swung. A key we are not
        /// going to do anything with belongs to vanilla.
        /// </summary>
        public static bool SuppressVanillaHotbar(int slot)
        {
            // A modifier must stop the plain number key from firing too. With a modifier, holding it means the key
            // is ours whatever the bar holds; the per-slot test below is only for the no-modifier scheme.
            if (IsBound && Modifier.Value != SkillModifier.None) return ModifierHeld(Modifier.Value);
            return SuppressVanillaHotbar() && SlotHasSomethingToCast(slot);
        }

        /// <summary>True only when this slot would actually do something: a slotted skill, the ultimate, or slot 1
        /// when it is set to swap bars and the swap can succeed.</summary>
        public static bool SlotHasSomethingToCast(int slot)
        {
            try
            {
                if (slot == State.BarLayout.WeaponSlot)
                    return SlotOneSwapsBars.Value && Combat.BarController.CanSwap();

                State.ClassPlayerData data = State.ClassPlayerStore.Local;
                State.BarLayout bar = data?.Bar(data.ActiveBar);
                if (bar == null) return false;

                if (slot == State.BarLayout.UltimateSlot) return true;
                return !string.IsNullOrEmpty(bar.SkillAt(slot));
            }
            catch { return false; }
        }

        /// <summary>
        /// False when the active bar cannot cast - no archetype, no weapon, nothing slotted. Keeps this
        /// mod out of the number keys entirely until the player has actually set a bar up, which is the
        /// state every new character is in.
        /// </summary>
        public static bool BarHasSomethingToCast()
        {
            try
            {
                State.ClassPlayerData data = State.ClassPlayerStore.Local;
                if (data == null) return false;
                State.BarLayout bar = data.Bar(data.ActiveBar);
                return bar != null && Combat.BarController.IsUsable(bar);
            }
            catch { return false; }
        }

        /// <summary>Doc 14: in the drawn-weapon scheme the slot keys only cast while a weapon is out.</summary>
        public static bool SlotKeysLive()
        {
            if (!IsBound) return false;
            return !SkillsNeedDrawnWeapon.Value || WeaponDrawn();
        }

        public static bool WeaponDrawn()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return false;
            return player.GetCurrentWeapon() != null || player.LeftItem != null;
        }

        public static bool ModifierHeld(SkillModifier modifier)
        {
            switch (modifier)
            {
                case SkillModifier.Alt:
                    return FiresKeys.GetKey(KeyCode.LeftAlt) || FiresKeys.GetKey(KeyCode.RightAlt);
                case SkillModifier.Ctrl:
                    return FiresKeys.GetKey(KeyCode.LeftControl) || FiresKeys.GetKey(KeyCode.RightControl);
                case SkillModifier.Shift:
                    return FiresKeys.GetKey(KeyCode.LeftShift) || FiresKeys.GetKey(KeyCode.RightShift);
                case SkillModifier.ShiftAlt:
                    return ModifierHeld(SkillModifier.Shift) && ModifierHeld(SkillModifier.Alt);
                default:
                    return false;
            }
        }

        private static void ApplyScheme()
        {
            BindingContext context = SkillsNeedDrawnWeapon.Value
                ? BindingContext.WeaponDrawn
                : BindingContext.Gameplay;

            for (int slot = 0; slot < SlotCount; slot++)
            {
                KeyBinding binding = SlotKeys[slot];
                // Slot 1 draws the bar's weapon, so it has to work sheathed too.
                binding.WithContext(slot == 0 ? BindingContext.Gameplay : context);
                if (binding.Key.Value == DefaultSlotKeys[slot]) WriteModifier(binding, Modifier.Value);

                KeyBinding itemKey = ItemHotbarKeys[slot];
                if (itemKey.Key.Value == DefaultSlotKeys[slot]) WriteModifier(itemKey, ItemHotbarModifier.Value);
            }

            BarSwap.WithContext(context);
            KeybindRegistry.Invalidate();
        }

        private static void WriteModifier(KeyBinding binding, SkillModifier modifier)
        {
            binding.Ctrl.Value = modifier == SkillModifier.Ctrl;
            binding.Shift.Value = modifier == SkillModifier.Shift || modifier == SkillModifier.ShiftAlt;
            binding.Alt.Value = modifier == SkillModifier.Alt || modifier == SkillModifier.ShiftAlt;
        }

        public static IEnumerable<string> DeclaredHandOffs() =>
            SlotKeys.Concat(ItemHotbarKeys).Where(binding => binding != null)
                .SelectMany(binding => binding.Replacements.Select(r => r.ButtonName + " (" + BindingContexts.Describe(r.Context) + ")"))
                .Distinct();
    }
}
