using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Move-by-header and resize-by-corner for the Classes window. One component serves both: the header
    /// strip and the corner grip each get one, and <see cref="Resize"/> decides which it is.
    /// Position and size persist, so the window stays where it was put.
    /// </summary>
    internal sealed class ClassesPanelHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float MinWidth = 520f;
        private const float MinHeight = 320f;

        private RectTransform _panel;
        private ConfigEntry<Vector2> _position;
        private ConfigEntry<Vector2> _size;
        private Vector2 _grabLocal;
        private Vector2 _startValue;
        private bool _dragging;

        public bool Resize;

        public void Initialize(RectTransform panel, ConfigEntry<Vector2> position, ConfigEntry<Vector2> size, bool resize)
        {
            _panel = panel;
            _position = position;
            _size = size;
            Resize = resize;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!(_panel?.parent is RectTransform parent)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, eventData.position, eventData.pressEventCamera, out _grabLocal);
            _startValue = Resize ? _panel.sizeDelta : _panel.anchoredPosition;
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || !(_panel?.parent is RectTransform parent)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;

            Vector2 delta = local - _grabLocal;
            if (Resize)
            {
                // The panel is centre-pivoted, so a corner drag moves the edge at twice the pointer delta.
                _panel.sizeDelta = new Vector2(
                    Mathf.Max(MinWidth, _startValue.x + delta.x * 2f),
                    Mathf.Max(MinHeight, _startValue.y - delta.y * 2f));
            }
            else
            {
                _panel.anchoredPosition = _startValue + delta;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            try
            {
                if (Resize) { if (_size != null) _size.Value = _panel.sizeDelta; }
                else if (_position != null) _position.Value = _panel.anchoredPosition;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[FiresRPGClasses] Could not persist the window layout: " + ex.Message);
            }
        }
    }
}
