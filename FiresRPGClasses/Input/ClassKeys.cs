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
        Shift
    }

    /// <summary>
    /// Doc 14's four key schemes. Every slot key is a Core KeyBinding so it gets strict modifier matching, a
    /// context, and a declared hand-off from Valheim's Hotbar 1-8. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class ClassKeys
    {
        public const string BarsSection = "Bars";
        public const string ItemHotbarSection = "Item hotbar";

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
                "On: slots 1-8 are skills only while a weapon is drawn, and the item hotbar takes them while sheathed.");
            Modifier = config.Bind(BarsSection, "Skill modifier", SkillModifier.Alt,
                "Held with every slot key still on its default. Alt by default so 1-8 stay Valheim's hotbar: without a "
                + "modifier this mod owns the number row, and a hammer in hotbar 1 could not be selected or swung. "
                + "Vanilla runs on Shift and crouches on Ctrl, which is why it is Alt rather than either of those.");
            SlotOneSwapsBars = config.Bind(BarsSection, "Slot 1 swaps bars", true,
                "On: slot 1's key swaps bars as well as the dedicated swap key.");
            ItemHotbarModifier = config.Bind(ItemHotbarSection, "Item hotbar modifier", SkillModifier.Alt,
                "Used only when skills do not need a drawn weapon and the skill modifier is None.");

            for (int slot = 0; slot < SlotCount; slot++)
            {
                KeyBinding binding = new KeyBinding(config, BarsSection, "Slot" + (slot + 1),
                    DefaultSlotKeys[slot], BindingContext.WeaponDrawn);
                string vanillaButton = VanillaHotbarButtonPrefix + (slot + 1);
                binding.Replaces(vanillaButton, BindingContext.WeaponDrawn);
                binding.Replaces(vanillaButton, BindingContext.Gameplay);
                SlotKeys[slot] = binding;

                ItemHotbarKeys[slot] = new KeyBinding(config, ItemHotbarSection, "Item" + (slot + 1),
                    DefaultSlotKeys[slot], BindingContext.Gameplay, defaultAlt: true);
            }

            BarSwap = new KeyBinding(config, BarsSection, "BarSwap", KeyCode.Mouse3, BindingContext.WeaponDrawn);

            ApplyScheme();
            SkillsNeedDrawnWeapon.SettingChanged += (s, e) => ApplyScheme();
            Modifier.SettingChanged += (s, e) => ApplyScheme();
            ItemHotbarModifier.SettingChanged += (s, e) => ApplyScheme();
        }

        public static KeyBinding Slot(int slot) =>
            slot >= 1 && slot <= SlotCount ? SlotKeys[slot - 1] : null;

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
            return prefix + slot;
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
            => SuppressVanillaHotbar() && SlotHasSomethingToCast(slot);

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
                    return UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                case SkillModifier.Ctrl:
                    return UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
                case SkillModifier.Shift:
                    return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
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
                binding.WithContext(context);
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
            binding.Shift.Value = modifier == SkillModifier.Shift;
            binding.Alt.Value = modifier == SkillModifier.Alt;
        }

        public static IEnumerable<string> DeclaredHandOffs() =>
            SlotKeys.Where(binding => binding != null)
                .SelectMany(binding => binding.Replacements.Select(r => r.ButtonName + " (" + BindingContexts.Describe(r.Context) + ")"))
                .Distinct();
    }
}
