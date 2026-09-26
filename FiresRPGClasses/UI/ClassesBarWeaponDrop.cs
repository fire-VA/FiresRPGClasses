using System.Reflection;
using FiresRPGClasses.Combat;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Drop-to-assign for a bar's weapon slot, on the terms VAInventory's hotbars proved: the bar is a
    /// readout, not storage, so the drop records WHICH weapon the bar uses and leaves the item in the
    /// inventory. A refused drop keeps the drag alive, so the item goes back where it came from exactly
    /// as it does over any other invalid target.
    ///
    /// The row this sits on decides which bar it means at drop time, not at build time: ClassesHud paints
    /// the active bar on the front row and the other one behind it, so the front row IS whichever bar is
    /// active this frame.
    /// </summary>
    internal sealed class ClassesBarWeaponDrop : MonoBehaviour, IDropHandler
    {
        private static readonly FieldInfo DragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static readonly MethodInfo SetupDragItemMethod = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");

        private bool _frontRow;

        public void Initialize(bool frontRow) => _frontRow = frontRow;

        /// <summary>The item the player is dragging, or null. Also the gate the bar's reposition drag uses.</summary>
        internal static ItemDrop.ItemData DraggedItem()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || DragItemField == null) return null;
            return DragItemField.GetValue(gui) as ItemDrop.ItemData;
        }

        public void OnDrop(PointerEventData eventData)
        {
            ItemDrop.ItemData item = DraggedItem();
            if (item == null) return;

            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;

            BarSide side = _frontRow ? data.ActiveBar : ClassPlayerData.Other(data.ActiveBar);
            if (!BarController.TryAssignWeapon(side, item)) return;

            // Cancel the drag rather than completing a move: the weapon never left the inventory.
            SetupDragItemMethod?.Invoke(InventoryGui.instance, new object[] { null, null, 1 });
        }
    }
}
