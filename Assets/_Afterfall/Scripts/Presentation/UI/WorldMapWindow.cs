using System.Collections.Generic;
using System.Globalization;
using Afterfall.Core;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// The world map (M): the cartographic overview with fog over unexplored land, labels for
    /// discovered places, the player, settlement, survivors, markers and the waypoint. Drag to
    /// pan, wheel to zoom around the cursor; left-click sets the waypoint, right-click places a
    /// named marker. The game keeps running underneath.
    /// </summary>
    public sealed class WorldMapWindow : IWindow
    {
        private const float DragThreshold = 5f;

        private readonly UiManager _ui;
        private readonly GameController _gc;
        private readonly VisualElement _viewport, _image, _detail, _fog, _symbols;
        private readonly Label _cursorInfo, _waypointInfo, _exploreInfo, _zoomInfo;
        private readonly TextField _markerName;
        private readonly VisualElement _markerList;
        private readonly Toggle _reveal, _rotate;
        private readonly List<MapSymbol> _found = new List<MapSymbol>();
        private float _cx, _cz, _mpp;
        private bool _dragging, _pressed;
        private int _button;
        private Vector2 _pressAt, _lastPointer;
        private int _markersVersion = -1;

        public WorldMapWindow(UiManager ui, GameController gc)
        {
            _ui = ui;
            _gc = gc;
            Root = UiKit.Div("window", "map-window");
            var body = Root.Put(UiKit.Div("row", "grow"));
            body.style.alignItems = Align.Stretch;

            // The side panel is on the left, clear of the toasts in the top-right corner.
            var side = body.Put(UiKit.Div("col", "map-side"));
            _viewport = body.Put(UiKit.Div("map-viewport", "grow"));
            _image = _viewport.Put(UiKit.Div("map-layer"));
            _detail = _viewport.Put(UiKit.Div("map-layer"));
            _fog = _viewport.Put(UiKit.Div("map-layer"));
            _symbols = _viewport.Put(UiKit.Div("map-layer"));
            foreach (VisualElement e in new[] { _image, _detail, _fog, _symbols }) e.pickingMode = PickingMode.Ignore;
            _viewport.RegisterCallback<PointerDownEvent>(OnDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnUp);
            _viewport.RegisterCallback<WheelEvent>(OnWheel);
            _viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout());

            var head = side.Put(UiKit.Div("row"));
            head.Add(UiKit.Text("Map", "title", "grow"));
            head.Add(UiKit.Btn("Close (M)", () => _ui.CloseWindow(), "btn--small"));
            var zoomRow = side.Put(UiKit.Div("row"));
            zoomRow.Add(UiKit.Btn("+", () => ZoomAround(0.66f, null), "btn--small"));
            zoomRow.Add(UiKit.Btn("−", () => ZoomAround(1.5f, null), "btn--small"));
            zoomRow.Add(UiKit.Btn("Centre on me", CenterOnPlayer, "btn--small"));
            zoomRow.Add(UiKit.Btn("Whole map", FitAll, "btn--small"));
            _zoomInfo = side.Put(UiKit.Text("", "muted"));
            _cursorInfo = side.Put(UiKit.Text("", "muted"));

            side.Add(UiKit.Text("Waypoint", "subtitle"));
            _waypointInfo = side.Put(UiKit.Text("", "wrap"));
            var wpRow = side.Put(UiKit.Div("row"));
            wpRow.Add(UiKit.Btn("Clear waypoint", () => Report(_gc.Session.ClearWaypoint()), "btn--small"));
            side.Add(UiKit.Text("Left-click the map to set the waypoint. Right-click to place a marker with the name below.", "muted"));
            _markerName = side.Put(new TextField { value = "" });

            side.Add(UiKit.Text("Your markers", "subtitle"));
            var scroll = side.Put(new ScrollView());
            scroll.AddToClassList("map-markers");
            _markerList = scroll.contentContainer;

            side.Add(UiKit.Text("Exploration", "subtitle"));
            _exploreInfo = side.Put(UiKit.Text("", "wrap"));
            _rotate = side.Put(new Toggle("Minimap turns with you") { value = _gc.Settings.MinimapRotate });
            _rotate.RegisterValueChangedCallback(e =>
            {
                _gc.Settings.MinimapRotate = e.newValue;
                _gc.Settings.Save();
            });
            _reveal = side.Put(new Toggle("Developer: show the whole map (view only)") { value = _gc.Maps.RevealAll });
            _reveal.RegisterValueChangedCallback(e => _gc.Maps.RevealAll = e.newValue && _gc.Session.Creative.Enabled);
            side.Add(Legend());

            Vector3 p = _gc.Player.transform.position.ToWorld();
            _cx = p.x;
            _cz = p.z;
            // The basin opens wider (a city block is ~100 m, the basin 64 km).
            _mpp = _gc.Session.World.Layout.IsChunked ? 2f : 0.6f;
        }

        public VisualElement Root { get; }

        public void OnClose() { }

        /// <summary>Current view (centre and zoom) for tests and the tour.</summary>
        public MapView View => new MapView(_cx, _cz, _mpp, _viewport.resolvedStyle.width, _viewport.resolvedStyle.height);

        public void Refresh()
        {
            WorldState w = _gc.Session.World;
            UiKit.Show(_reveal, _gc.Session.Creative.Enabled);
            if (!_gc.Session.Creative.Enabled && _gc.Maps.RevealAll) _gc.Maps.RevealAll = false;
            MapNavigation nav = w.Navigation;
            if (nav.HasWaypoint)
            {
                Vec3 me = _gc.Session.Player.Position;
                float d = Vec3.DistanceXZ(me, nav.Waypoint);
                float b = MapGeometry.Bearing(me, nav.Waypoint);
                _waypointInfo.text = $"{nav.WaypointLabel}: {MapGeometry.Distance(d)} {MapGeometry.Cardinal(b)} ({b:0}°)";
            }
            else _waypointInfo.text = "None set.";
            int known = 0;
            foreach (PoiInfo poi in w.Layout.Pois)
                if (w.Exploration.DiscoveredPois.Contains(poi.Id)) known++;
            _exploreInfo.text = $"{w.Exploration.RevealedFraction * 100f:0}% of the land explored · {known} of {w.Layout.Pois.Count} places discovered";
            if (_markersVersion != nav.Version) RebuildMarkerList();
            Layout();
        }

        // ------------------------------------------------------------ layout

        private void Layout()
        {
            float vw = _viewport.resolvedStyle.width, vh = _viewport.resolvedStyle.height;
            if (float.IsNaN(vw) || vw < 10f) return;
            WorldLayout layout = _gc.Session.World.Layout;
            Clamp(vw, vh);
            var view = new MapView(_cx, _cz, _mpp, vw, vh);
            view.WorldToScreen(0f, layout.Size, out float left, out float top);
            float size = layout.Size / _mpp;
            foreach (VisualElement e in new[] { _image, _fog })
            {
                e.style.left = left;
                e.style.top = top;
                e.style.width = size;
                e.style.height = size;
            }
            if (_gc.Maps.Overview != null) _image.style.backgroundImage = new StyleBackground(_gc.Maps.Overview);
            if (_gc.Maps.Fog != null) _fog.style.backgroundImage = new StyleBackground(_gc.Maps.Fog);
            // Zoomed in on a chunked world, the local detail map (streets, buildings) lies over the overview.
            bool detail = _gc.Maps.Detail != null && _mpp < 6f;
            UiKit.Show(_detail, detail);
            if (detail)
            {
                view.WorldToScreen(_gc.Maps.DetailOrigin.x, _gc.Maps.DetailOrigin.y + MapTextures.DetailSize, out float dl, out float dt);
                _detail.style.left = dl;
                _detail.style.top = dt;
                _detail.style.width = MapTextures.DetailSize / _mpp;
                _detail.style.height = MapTextures.DetailSize / _mpp;
                _detail.style.backgroundImage = new StyleBackground(_gc.Maps.Detail);
            }
            // Textures have row 0 at the south edge; UI images draw row 0 at the bottom too, so no flip is needed.
            _zoomInfo.text = string.Format(CultureInfo.InvariantCulture, "Scale: 100 px = {0}", MapGeometry.Distance(100f * _mpp));

            _symbols.Clear();
            MapSymbols.Collect(_gc, _gc.Maps.RevealAll, _found);
            bool labels = _mpp < 2.5f;
            foreach (MapSymbol s in _found)
            {
                view.WorldToScreen(s.X, s.Z, out float px, out float py);
                if (!view.Contains(px, py, -40f)) continue;
                float iconSize = s.Kind == MapSymbolKind.Player ? 30f : s.Kind == MapSymbolKind.Poi ? 18f : s.Kind == MapSymbolKind.Survivor ? 12f : 24f;
                bool showLabel = s.Kind != MapSymbolKind.Player && (labels || s.Kind == MapSymbolKind.Poi || s.Kind == MapSymbolKind.Waypoint);
                VisualElement e = MapSymbols.Make(s, iconSize, showLabel);
                e.style.left = px;
                e.style.top = py;
                if (s.Kind == MapSymbolKind.Player) e[0].style.rotate = new Rotate(new Angle(s.Heading, AngleUnit.Degree));
                _symbols.Add(e);
            }
        }

        private void Clamp(float vw, float vh)
        {
            float size = _gc.Session.World.Layout.Size;
            float fit = size / Mathf.Max(64f, Mathf.Min(vw, vh)) * 1.05f;
            _mpp = Mathf.Clamp(_mpp, 0.12f, Mathf.Max(0.2f, fit));
            _cx = Mathf.Clamp(_cx, 0f, size);
            _cz = Mathf.Clamp(_cz, 0f, size);
        }

        private void CenterOnPlayer()
        {
            Vector3 p = _gc.Player.transform.position.ToWorld();
            _cx = p.x;
            _cz = p.z;
            Layout();
        }

        private void FitAll()
        {
            float size = _gc.Session.World.Layout.Size;
            _cx = size * 0.5f;
            _cz = size * 0.5f;
            _mpp = 1e6f;
            Layout();
        }

        /// <summary>Zooms by <paramref name="factor"/> (below 1 zooms in) keeping the world point under <paramref name="pointer"/> fixed.</summary>
        private void ZoomAround(float factor, Vector2? pointer)
        {
            float vw = _viewport.resolvedStyle.width, vh = _viewport.resolvedStyle.height;
            Vector2 at = pointer ?? new Vector2(vw * 0.5f, vh * 0.5f);
            var before = new MapView(_cx, _cz, _mpp, vw, vh);
            before.ScreenToWorld(at.x, at.y, out float wx, out float wz);
            _mpp *= factor;
            Clamp(vw, vh);
            var after = new MapView(_cx, _cz, _mpp, vw, vh);
            after.ScreenToWorld(at.x, at.y, out float nx, out float nz);
            _cx += wx - nx;
            _cz += wz - nz;
            Layout();
        }

        /// <summary>Scripted control (the tour): centre on a world point at a zoom.</summary>
        public void Focus(float x, float z, float metersPerPixel)
        {
            _cx = x;
            _cz = z;
            _mpp = metersPerPixel;
            Layout();
        }

        // ------------------------------------------------------------ input

        private void OnDown(PointerDownEvent e)
        {
            _pressed = true;
            _dragging = false;
            _button = e.button;
            _pressAt = e.localPosition;
            _lastPointer = e.localPosition;
            _viewport.CapturePointer(e.pointerId);
        }

        private void OnMove(PointerMoveEvent e)
        {
            Vector2 local = e.localPosition;
            float vw = _viewport.resolvedStyle.width, vh = _viewport.resolvedStyle.height;
            new MapView(_cx, _cz, _mpp, vw, vh).ScreenToWorld(local.x, local.y, out float wx, out float wz);
            _cursorInfo.text = string.Format(CultureInfo.InvariantCulture, "Cursor: X {0:0}, Z {1:0} · {2} from you", wx, wz,
                MapGeometry.Distance(Vec3.DistanceXZ(_gc.Session.Player.Position, new Vec3(wx, 0f, wz))));
            if (!_pressed) return;
            if (!_dragging && (local - _pressAt).magnitude > DragThreshold) _dragging = true;
            if (_dragging)
            {
                Vector2 d = local - _lastPointer;
                _cx -= d.x * _mpp;
                _cz += d.y * _mpp;
                Layout();
            }
            _lastPointer = local;
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_viewport.HasPointerCapture(e.pointerId)) _viewport.ReleasePointer(e.pointerId);
            bool click = _pressed && !_dragging;
            _pressed = false;
            _dragging = false;
            if (!click) return;
            float vw = _viewport.resolvedStyle.width, vh = _viewport.resolvedStyle.height;
            new MapView(_cx, _cz, _mpp, vw, vh).ScreenToWorld(e.localPosition.x, e.localPosition.y, out float wx, out float wz);
            if (_button == 1) PlaceMarker(wx, wz);
            else SetWaypointAt(wx, wz);
        }

        private void OnWheel(WheelEvent e)
        {
            ZoomAround(e.delta.y > 0f ? 1.25f : 0.8f, e.localMousePosition);
            e.StopPropagation();
        }

        /// <summary>Sets the waypoint at a world point, named after a known place there if any.</summary>
        public OpResult SetWaypointAt(float x, float z)
        {
            WorldState w = _gc.Session.World;
            PoiInfo poi = w.Layout.PoiAt(x, z);
            string label = poi != null && (w.Exploration.DiscoveredPois.Contains(poi.Id) || _gc.Maps.RevealAll) ? poi.Name
                : string.Format(CultureInfo.InvariantCulture, "Waypoint ({0:0}, {1:0})", x, z);
            OpResult r = _gc.Session.SetWaypoint(new Vec3(x, 0f, z), label);
            Report(r);
            return r;
        }

        public void PlaceMarker(float x, float z)
        {
            string name = string.IsNullOrWhiteSpace(_markerName.value) ? $"Marker {_gc.Session.World.Navigation.Markers.Count + 1}" : _markerName.value;
            Report(_gc.Session.AddMapMarker(name, new Vec3(x, 0f, z)));
            _markerName.value = "";
        }

        private void RebuildMarkerList()
        {
            MapNavigation nav = _gc.Session.World.Navigation;
            _markersVersion = nav.Version;
            _markerList.Clear();
            if (nav.Markers.Count == 0) _markerList.Add(UiKit.Text("None yet.", "muted"));
            foreach (MapMarker m in nav.Markers)
            {
                MapMarker marker = m;
                var row = _markerList.Put(UiKit.Div("list-item"));
                row.Add(UiKit.Text((m.Kind == MarkerKind.Bookmark ? "★ " : "● ") + m.Name, "grow"));
                row.Add(UiKit.Btn("Show", () => Focus(marker.Position.X, marker.Position.Z, Mathf.Min(_mpp, 0.8f)), "btn--small"));
                row.Add(UiKit.Btn("Go here", () => Report(_gc.Session.SetWaypoint(marker.Position, marker.Name)), "btn--small"));
                row.Add(UiKit.Btn("×", () => Report(_gc.Session.RemoveMapMarker(marker.Id)), "btn--small", "btn--danger"));
            }
        }

        private static VisualElement Legend()
        {
            var box = UiKit.Div("col", "map-legend");
            box.Add(UiKit.Text("Legend", "subtitle"));
            box.Add(UiKit.Text("Pale lines: paved roads · brown: tracks · dark blocks: buildings · green: woodland · blue: water · thin lines: 5 m contours · grey wash: unexplored", "muted"));
            return box;
        }

        private void Report(OpResult r)
        {
            if (!string.IsNullOrEmpty(r.Message)) _gc.Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
            Refresh();
        }
    }
}
