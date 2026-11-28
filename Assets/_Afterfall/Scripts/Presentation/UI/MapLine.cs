using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// A polyline over a map (the route to the waypoint): a dark casing under a bright line, drawn with
    /// the vector painter. Points are in the element's own pixels; a NaN point breaks the line, so only
    /// the stretches near the view need be given.
    /// </summary>
    public sealed class MapLine : VisualElement
    {
        private readonly List<Vector2> _points = new List<Vector2>();

        public Color LineColor { get; set; } = new Color(1f, 0.8f, 0.22f, 0.95f);
        public Color CasingColor { get; set; } = new Color(0.12f, 0.1f, 0.06f, 0.8f);
        public float LineWidth { get; set; } = 4f;

        /// <summary>Points drawn last time (tests and the tour).</summary>
        public int PointCount { get; private set; }

        public MapLine()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += Draw;
        }

        /// <summary>Starts a new set of points.</summary>
        public void Begin() => _points.Clear();

        public void Add(Vector2 p) => _points.Add(p);

        public void Break()
        {
            if (_points.Count > 0 && !float.IsNaN(_points[_points.Count - 1].x)) _points.Add(new Vector2(float.NaN, float.NaN));
        }

        /// <summary>Finishes the set and redraws.</summary>
        public void End()
        {
            PointCount = 0;
            foreach (Vector2 p in _points)
                if (!float.IsNaN(p.x)) PointCount++;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            if (_points.Count < 2) return;
            Painter2D p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Stroke(p, CasingColor, LineWidth + 3f);
            Stroke(p, LineColor, LineWidth);
        }

        private void Stroke(Painter2D p, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            bool open = false;
            foreach (Vector2 pt in _points)
            {
                if (float.IsNaN(pt.x))
                {
                    open = false;
                    continue;
                }
                if (open) p.LineTo(pt);
                else p.MoveTo(pt);
                open = true;
            }
            p.Stroke();
        }

        /// <summary>
        /// Adds a route's stretches that come near the rectangle (0, 0)–(<paramref name="w"/>, <paramref name="h"/>)
        /// of <paramref name="view"/>, skipping points closer than 2 px (no allocation: the minimap calls it every frame).
        /// </summary>
        public void AddRoute(IReadOnlyList<Afterfall.Core.Vec3> route, Afterfall.Domain.World.MapView view, float w, float h, float margin = 30f)
        {
            Vector2 prev = default;
            bool havePrev = false, drawn = false;
            for (int i = 0; i < route.Count; i++)
            {
                view.WorldToScreen(route[i].X, route[i].Z, out float sx, out float sy);
                var s = new Vector2(sx, sy);
                if (havePrev)
                {
                    bool near = Mathf.Max(prev.x, s.x) >= -margin && Mathf.Min(prev.x, s.x) <= w + margin
                        && Mathf.Max(prev.y, s.y) >= -margin && Mathf.Min(prev.y, s.y) <= h + margin;
                    if (near)
                    {
                        if (!drawn) Add(prev);
                        if ((s - _points[_points.Count - 1]).sqrMagnitude >= 4f || i == route.Count - 1) Add(s);
                        drawn = true;
                    }
                    else if (drawn)
                    {
                        Break();
                        drawn = false;
                    }
                }
                prev = s;
                havePrev = true;
            }
        }
    }
}
