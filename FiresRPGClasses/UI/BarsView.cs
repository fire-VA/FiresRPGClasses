using System.Collections.Generic;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.Npc.Archetypes;
using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 14's Bars board: both bars with their weapon, chosen archetype, six skills and ultimate; the archetype
    /// buttons the weapon allows; and named saved bars. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class BarsView : IClassesView
    {
        private const float BarBlockHeight = 46f;
        private const float SlotRowHeight = 32f;
        private const int MaxPickerSlots = 14;

        public ClassesTab Tab => ClassesTab.Bars;

        public string Title => "Bars";

        private RectTransform _page;
        private UIBuilderHelper.ScrollAreaResult _scroll;
        private TextMeshProUGUI _header;
        private BarSide _editing = BarSide.Front;
        private int _pickingSlot;

        public void Build(RectTransform page)
        {
            _page = page;
            _header = ClassesTheme.Text(page, "BarsHeader", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.SmallFontSize);
            _header.rectTransform.Fill(-6f, BarBlockHeight);
            _header.alignment = TextAlignmentOptions.TopLeft;

            _scroll = UIBuilderHelper.CreateScrollArea(page, "BarsScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            _scroll.Root.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -(BarBlockHeight + 10f));
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null) return;

            _header.text = "Active bar: " + data.ActiveBar
                + "   Front " + Describe(data, BarSide.Front)
                + "   Back " + Describe(data, BarSide.Back);

            ClassesTheme.ClearChildren(_scroll.Content);
            int row = 0;
            row = BarBlock(row, data, BarSide.Front);
            row = BarBlock(row, data, BarSide.Back);
            row = SavedBars(row, data);
            if (_pickingSlot != 0) row = SkillPicker(row, data);
            ClassesTheme.SetContentHeight(_scroll.Content, row, SlotRowHeight);
        }

        private static string Describe(ClassPlayerData data, BarSide side)
        {
            BarLayout bar = data.Bar(side);
            string weapon = string.IsNullOrEmpty(bar.WeaponPrefab) ? "unarmed" : bar.WeaponPrefab;
            string usable = BarController.IsUsable(bar) ? "" : " (no weapon in inventory)";
            return "[" + bar.Archetype + " with " + weapon + usable + "]";
        }

        private int BarBlock(int index, ClassPlayerData data, BarSide side)
        {
            BarLayout bar = data.Bar(side);
            RectTransform head = ClassesTheme.Row(_scroll.Content, "BarHead_" + side, index++, SlotRowHeight);
            ClassesTheme.Text(head, "Label",
                side + " bar - " + Describe(data, side),
                side == data.ActiveBar ? ClassesTheme.Good : ClassesTheme.Ready,
                new Vector2(0.01f, 0f), new Vector2(0.7f, 1f), ClassesTheme.RowFontSize);

            BarSide named = side;
            ClassesTheme.Button(head, "Save as bar", new Vector2(0.72f, 0.1f), new Vector2(0.99f, 0.9f), () =>
            {
                data.SaveBar(named + " " + data.Bar(named).Archetype, named);
                ClassPlayerStore.SaveLocal();
                Refresh();
            });

            index = ArchetypeChoices(index, bar, side);

            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.LastSkillSlot; slot++)
                index = SlotRow(index, data, side, slot);
            return SlotRow(index, data, side, BarLayout.UltimateSlot);
        }

        /// <summary>Doc 14: the bar shows "Use Ranger / Use Rogue" for whatever the weapon allows.</summary>
        private int ArchetypeChoices(int index, BarLayout bar, BarSide side)
        {
            IReadOnlyList<ArchetypeClass> allowed = BarController.AllowedArchetypes(bar);
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Archetypes_" + side, index++, SlotRowHeight);
            if (allowed.Count == 0)
            {
                ClassesTheme.Text(row, "None", "this weapon drives no archetype", ClassesTheme.Locked,
                    new Vector2(0.03f, 0f), new Vector2(0.99f, 1f), ClassesTheme.SmallFontSize);
                return index;
            }
            for (int i = 0; i < allowed.Count; i++)
            {
                ArchetypeClass archetype = allowed[i];
                float min = 0.03f + i * 0.2f;
                ClassesTheme.Button(row, "Use " + archetype, new Vector2(min, 0.1f),
                    new Vector2(min + 0.18f, 0.9f), () =>
                    {
                        BarController.TryChooseArchetype(side, archetype);
                        Refresh();
                    });
            }
            return index;
        }

        private int SlotRow(int index, ClassPlayerData data, BarSide side, int slot)
        {
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Slot_" + side + slot, index++, SlotRowHeight);
            BarLayout bar = data.Bar(side);
            string nodeId = slot == BarLayout.UltimateSlot ? bar.UltimateId : bar.SkillAt(slot);
            SlotAvailability availability = BarController.Availability(side, slot);

            string label = slot == BarLayout.UltimateSlot
                ? "8  " + (UltimateCatalog.Get(nodeId)?.DisplayName ?? "empty ultimate")
                : slot + "  " + (ClassTrees.Node(nodeId)?.DisplayName ?? "empty");

            ClassesTheme.Text(row, "Label", label + "   " + ExplainAvailability(availability),
                ClassesTheme.ForAvailability(availability),
                new Vector2(0.05f, 0f), new Vector2(0.78f, 1f), ClassesTheme.RowFontSize);

            if (slot == BarLayout.UltimateSlot) return index;

            BarSide pickSide = side;
            int pickSlot = slot;
            ClassesTheme.Button(row, "Set", new Vector2(0.8f, 0.1f), new Vector2(0.88f, 0.9f), () =>
            {
                _editing = pickSide;
                _pickingSlot = pickSlot;
                Refresh();
            });
            ClassesTheme.Button(row, "Clear", new Vector2(0.89f, 0.1f), new Vector2(0.99f, 0.9f), () =>
            {
                BarController.TrySlotSkill(pickSide, pickSlot, "");
                Refresh();
            });
            return index;
        }

        private static string ExplainAvailability(SlotAvailability availability)
        {
            switch (availability)
            {
                case SlotAvailability.Ready: return "";
                case SlotAvailability.OnCooldown: return "on cooldown";
                case SlotAvailability.NotLearned: return "not learned";
                case SlotAvailability.WrongArchetype: return "greyed: this weapon cannot drive it";
                case SlotAvailability.BarUnusable: return "greyed: the bar has no weapon";
                case SlotAvailability.LevelLocked: return "locked by level";
                default: return "";
            }
        }

        private int SavedBars(int index, ClassPlayerData data)
        {
            RectTransform head = ClassesTheme.Row(_scroll.Content, "SavedHead", index++, SlotRowHeight);
            ClassesTheme.Text(head, "Label", "Saved bars", UIFontConfig.Colors.BrightGold,
                new Vector2(0.01f, 0f), new Vector2(0.99f, 1f), ClassesTheme.RowFontSize);

            foreach (SavedBar saved in data.SavedBars)
            {
                RectTransform row = ClassesTheme.Row(_scroll.Content, "Saved_" + saved.Name, index++, SlotRowHeight);
                ClassesTheme.Text(row, "Name", saved.Name + "  (" + saved.Layout.Archetype + ")",
                    ClassesTheme.Ready, new Vector2(0.05f, 0f), new Vector2(0.6f, 1f), ClassesTheme.RowFontSize);

                string name = saved.Name;
                ClassesTheme.Button(row, "Load front", new Vector2(0.62f, 0.1f), new Vector2(0.78f, 0.9f),
                    () => { BarController.TryLoadSavedBar(BarSide.Front, name); Refresh(); });
                ClassesTheme.Button(row, "Load back", new Vector2(0.79f, 0.1f), new Vector2(0.9f, 0.9f),
                    () => { BarController.TryLoadSavedBar(BarSide.Back, name); Refresh(); });
                ClassesTheme.Button(row, "Delete", new Vector2(0.91f, 0.1f), new Vector2(0.99f, 0.9f),
                    () => { data.DeleteSavedBar(name); ClassPlayerStore.SaveLocal(); Refresh(); });
            }
            return index;
        }

        /// <summary>
        /// Doc 14: a bar slots actives from any archetype its weapon allows, so the picker lists all of them.
        /// </summary>
        private int SkillPicker(int index, ClassPlayerData data)
        {
            RectTransform head = ClassesTheme.Row(_scroll.Content, "PickHead", index++, SlotRowHeight);
            ClassesTheme.Text(head, "Label",
                "Pick a skill for " + _editing + " slot " + _pickingSlot, UIFontConfig.Colors.BrightGold,
                new Vector2(0.01f, 0f), new Vector2(0.8f, 1f), ClassesTheme.RowFontSize);
            ClassesTheme.Button(head, "Cancel", new Vector2(0.86f, 0.1f), new Vector2(0.99f, 0.9f),
                () => { _pickingSlot = 0; Refresh(); });

            int shown = 0;
            foreach (ArchetypeClass archetype in BarController.AllowedArchetypes(data.Bar(_editing)))
            {
                foreach (ClassTreeNode node in ClassTrees.ActivesFor(archetype))
                {
                    if (data.Progress.TalentRank(archetype, node.Id) <= 0) continue;
                    if (shown++ >= MaxPickerSlots) return index;

                    RectTransform row = ClassesTheme.Row(_scroll.Content, "Pick_" + node.Id, index++, SlotRowHeight);
                    ClassesTheme.Text(row, "Name",
                        node.DisplayName + "  (" + archetype + ")  " + TalentsView.Describe(node.Targeting),
                        ClassesTheme.Ready, new Vector2(0.05f, 0f), new Vector2(0.84f, 1f), ClassesTheme.SmallFontSize);

                    string chosen = node.Id;
                    ClassesTheme.Button(row, "Slot", new Vector2(0.86f, 0.1f), new Vector2(0.99f, 0.9f), () =>
                    {
                        BarController.TrySlotSkill(_editing, _pickingSlot, chosen);
                        _pickingSlot = 0;
                        Refresh();
                    });
                }
            }
            return index;
        }
    }
}
