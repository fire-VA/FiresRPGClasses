using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Mouse-drag-to-reposition for the class bars, on the same terms VAInventory's HotBarDragHandle
    /// proved: a near-transparent raycast Image on the bar root catches the pointer, and the drag only
    /// engages when <c>InventoryGui.m_dragItem</c> is null, so moving an inventory item never gets
    /// stolen. It is deliberately NOT gated on the inventory being closed - that gate was why the bars
    /// could not be moved at all.
    /// </summary>
    internal sealed class ClassesBarDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private static readonly FieldInfo DragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");

        private RectTransform _bar;
        private ConfigEntry<Vector2> _position;
        private Vector2 _grabLocal;
        private Vector2 _startAnchored;
        private bool _dragging;

        public void Initialize(RectTransform bar, ConfigEntry<Vector2> position)
        {
            _bar = bar;
            _position = position;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (HasInventoryDrag()) return;
            if (_bar == null) _bar = GetComponent<RectTransform>();
            if (!(_bar?.parent is RectTransform parent)) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, eventData.position, eventData.pressEventCamera, out _grabLocal);
            _startAnchored = _bar.anchoredPosition;
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || !(_bar?.parent is RectTransform parent)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            _bar.anchoredPosition = _startAnchored + (local - _grabLocal);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_bar == null || _position == null) return;
            try { _position.Value = _bar.anchoredPosition; }
            catch (Exception ex)
            {
                Debug.LogWarning("[FiresRPGClasses] Could not persist the bar position: " + ex.Message);
            }
        }

        private static bool HasInventoryDrag()
        {
            try
            {
                InventoryGui gui = InventoryGui.instance;
                return gui != null && DragItemField != null && DragItemField.GetValue(gui) != null;
            }
            catch { return false; }
        }
    }
}
