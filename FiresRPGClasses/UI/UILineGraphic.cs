using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Every connector of a talent tree in ONE UI mesh (doc 12 section 10: "connection lines drawn as a single UI mesh
    /// rather than one Image per segment"). A segment joins the centres of two RectTransforms - the node rings - so
    /// the lines follow the layout when the window is resized; positions are re-read whenever the mesh rebuilds.
    /// Segments can be dashed (the boards' "locked, new" and level-cap lines). Core has no line graphic; this one
    /// is small enough to live here.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILineGraphic : MaskableGraphic
    {
        private struct Segment
        {
            public RectTransform From;
            public RectTransform To;
            public Vector2 FromOffset;
            public Vector2 ToOffset;
            public float Width;
            public Color Color;
            public bool Dashed;
        }

        private readonly List<Segment> _segments = new List<Segment>();

        private const float DashOn = 6f;
        private const float DashOff = 5f;

        public int Count => _segments.Count;

        public void ClearSegments()
        {
            _segments.Clear();
            SetVerticesDirty();
        }

        /// <summary>A line between the centres of two rects (plus optional local offsets, e.g. "below the ring").</summary>
        public void Add(RectTransform from, RectTransform to, float width, Color color, bool dashed = false,
            Vector2 fromOffset = default, Vector2 toOffset = default)
        {
            if (from == null || to == null) return;
            _segments.Add(new Segment
            {
                From = from, To = to, FromOffset = fromOffset, ToOffset = toOffset,
                Width = width, Color = color, Dashed = dashed
            });
            SetVerticesDirty();
        }

        /// <summary>The rects moved (layout, resize): redraw from their new positions.</summary>
        public void Relayout() => SetVerticesDirty();

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (Segment segment in _segments)
            {
                if (segment.From == null || segment.To == null) continue;
                Vector2 a = Local(segment.From) + segment.FromOffset;
                Vector2 b = Local(segment.To) + segment.ToOffset;
                if (!segment.Dashed)
                {
                    Quad(vh, a, b, segment.Width, segment.Color);
                    continue;
                }
                float length = Vector2.Distance(a, b);
                if (length < 0.01f) continue;
                Vector2 step = (b - a) / length;
                for (float at = 0f; at < length; at += DashOn + DashOff)
                    Quad(vh, a + step * at, a + step * Mathf.Min(at + DashOn, length), segment.Width, segment.Color);
            }
        }

        /// <summary>The centre of a rect in this graphic's local space.</summary>
        private Vector2 Local(RectTransform target)
        {
            Vector3 world = target.TransformPoint(target.rect.center);
            return rectTransform.InverseTransformPoint(world);
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < 0.0001f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
            int start = vh.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = a - normal; vh.AddVert(vertex);
            vertex.position = a + normal; vh.AddVert(vertex);
            vertex.position = b + normal; vh.AddVert(vertex);
            vertex.position = b - normal; vh.AddVert(vertex);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
