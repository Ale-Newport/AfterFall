using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// The waypoint on screen: a marker with its name and distance over the waypoint when it is
    /// in view, or an arrow pinned to the screen edge pointing toward it when it is not.
    /// </summary>
    public sealed class WaypointIndicator
    {
        private const float Margin = 46f;

        private readonly GameController _gc;
        private readonly VisualElement _icon, _arrow;
        private readonly Label _label;

        public VisualElement Root { get; }

        public WaypointIndicator(GameController gc)
        {
            _gc = gc;
            Root = UiKit.Div("waypoint");
            Root.pickingMode = PickingMode.Ignore;
            _icon = Root.Put(UiKit.Div("waypoint-icon"));
            _icon.style.backgroundImage = new StyleBackground(ItemIcons.Glyph("map_waypoint"));
            _arrow = Root.Put(UiKit.Div("waypoint-arrow"));
            _arrow.style.backgroundImage = new StyleBackground(ItemIcons.Glyph("map_player"));
            _label = Root.Put(UiKit.Text("", "waypoint-label"));
            foreach (VisualElement e in new[] { _icon, _arrow, _label }) e.pickingMode = PickingMode.Ignore;
        }

        /// <summary>True when the marker sits over the waypoint itself (it is on screen).</summary>
        public bool OnScreen { get; private set; }

        public void Refresh(bool visible)
        {
            MapNavigation nav = _gc.Session.World.Navigation;
            IPanel panel = Root.panel;
            if (!visible || !nav.HasWaypoint || panel == null)
            {
                UiKit.Show(Root, false);
                return;
            }
            UiKit.Show(Root, true);
            Camera cam = _gc.CameraRig.Camera;
            Vector3 target = nav.Waypoint.ToUnity() + Vector3.up * 2f;
            Vector3 sp = cam.WorldToScreenPoint(target);
            bool behind = sp.z < 0f;
            Vector2 panelSize = panel.visualTree.layout.size;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(behind ? Screen.width - sp.x : sp.x, Screen.height - (behind ? Screen.height - sp.y : sp.y)));
            Vector2 centre = panelSize * 0.5f;
            Vector2 min = new Vector2(Margin, Margin), max = panelSize - new Vector2(Margin, Margin + 60f);
            OnScreen = !behind && p.x >= min.x && p.y >= min.y && p.x <= max.x && p.y <= max.y;
            if (!OnScreen)
            {
                // Pin to the screen edge along the direction from the centre.
                // (A point behind the camera was mirrored above, so it pins to the side it lies on.)
                Vector2 d = p - centre;
                if (d.sqrMagnitude < 1f) d = Vector2.down;
                float sx = d.x != 0f ? ((d.x > 0 ? max.x : min.x) - centre.x) / d.x : float.MaxValue;
                float sy = d.y != 0f ? ((d.y > 0 ? max.y : min.y) - centre.y) / d.y : float.MaxValue;
                float k = Mathf.Min(Mathf.Abs(sx), Mathf.Abs(sy));
                p = centre + d * k;
                float angle = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
                _arrow.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            }
            UiKit.Show(_arrow, !OnScreen);
            UiKit.Show(_icon, OnScreen);
            Root.style.left = p.x;
            Root.style.top = p.y;
            // Keep the name on screen when the marker is pinned to a side edge.
            float shift = p.x < 220f ? 0f : p.x > panelSize.x - 220f ? -100f : -50f;
            _label.style.translate = new Translate(Length.Percent(shift), 0f);
            float metres = Vec3.DistanceXZ(_gc.Session.Player.Position, nav.Waypoint);
            _label.text = $"{nav.WaypointLabel} · {MapGeometry.Distance(metres)}";
        }
    }
}
