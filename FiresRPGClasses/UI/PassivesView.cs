using System.Collections.Generic;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresCore.UI;
using TMPro;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 19's passive grid: every tree passive and automatic ability, with its status and why, equipped up to
    /// the slot count. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class PassivesView : IClassesView
    {
        private const float HeaderTextHeight = 34f;
        private const float PassiveRowHeight = 34f;

        public ClassesTab Tab => ClassesTab.Passives;

        public string Title => "Passives";

        private RectTransform _page;
        private UIBuilderHelper.ScrollAreaResult _scroll;
        private TextMeshProUGUI _header;

        public void Build(RectTransform page)
        {
            _page = page;
            _header = ClassesTheme.Text(page, "PassiveHeader", "", UIFontConfig.Colors.Gold,
                new Vector2(0.02f, 1f), new Vector2(0.98f, 1f), ClassesTheme.RowFontSize);
            _header.rectTransform.Fill(-6f, HeaderTextHeight);

            _scroll = UIBuilderHelper.CreateScrollArea(page, "PassiveScroll",
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 1f), UIFontConfig.Colors.SectionBackgroundDark);
            _scroll.Root.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -(HeaderTextHeight + 10f));
        }

        public void Refresh()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || _page == null) return;

            int slots = PassiveLoadout.Slots();
            _header.text = "Passives equipped " + PassiveLoadout.Equipped() + " / " + slots
                + "   always on, whichever bar is active";

            ClassesTheme.ClearChildren(_scroll.Content);
            List<ClassTreeNode> pool = PassiveLoadout.Pool();
            int row = 0;
            foreach (ClassTreeNode node in pool) row = PassiveRow(row, data, node);
            ClassesTheme.SetContentHeight(_scroll.Content, row, PassiveRowHeight);
        }

        private int PassiveRow(int index, ClassPlayerData data, ClassTreeNode node)
        {
            PassiveStatus status = PassiveLoadout.Status(node);
            RectTransform row = ClassesTheme.Row(_scroll.Content, "Passive_" + node.Id, index++, PassiveRowHeight);

            ClassesTheme.Text(row, "Name",
                node.DisplayName + "  (" + node.Archetype + ", level " + node.MinLevel + ")  "
                + PassiveLoadout.ExplainStatus(status),
                ClassesTheme.ForPassive(status),
                new Vector2(0.03f, 0f), new Vector2(0.84f, 1f), ClassesTheme.RowFontSize);

            string nodeId = node.Id;
            bool equipped = data.Progress.PassiveLoadout.Contains(nodeId);
            if (equipped)
            {
                ClassesTheme.Button(row, "Unequip", new Vector2(0.86f, 0.1f), new Vector2(0.99f, 0.9f),
                    () => { PassiveLoadout.TryUnequip(nodeId); Refresh(); });
            }
            else if (status == PassiveStatus.NotEquipped)
            {
                ClassesTheme.Button(row, "Equip", new Vector2(0.86f, 0.1f), new Vector2(0.99f, 0.9f),
                    () => { PassiveLoadout.TryEquip(nodeId); Refresh(); });
            }
            return index;
        }
    }
}
