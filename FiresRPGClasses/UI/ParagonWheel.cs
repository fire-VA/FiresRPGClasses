using UnityEngine;
using UnityEngine.EventSystems;

namespace FiresRPGClasses.UI
{
    /// <summary>0.1.152 [classes-ui]: the mouse wheel over the Paragon map steps the one-figure view (Fire 10-16).</summary>
    internal sealed class ParagonWheel : MonoBehaviour, IScrollHandler
    {
        internal ParagonView View;
        private float _next;

        public void OnScroll(PointerEventData data)
        {
            if (View == null || !ParagonFigures.On || Time.unscaledTime < _next || Mathf.Abs(data.scrollDelta.y) < 0.01f) return;
            _next = Time.unscaledTime + 0.18f;   // one step per notch burst
            View.StepFigure(data.scrollDelta.y > 0f ? -1 : 1);
        }
    }
}
