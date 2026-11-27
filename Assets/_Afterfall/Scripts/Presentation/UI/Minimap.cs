using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// The HUD minimap (bottom right): the same cartographic map and fog as the world map,
    /// centred on the survivor, north up or turning with them, with nearby known places, markers,
    /// settlement and survivors, and the waypoint (pinned to the rim when it is out of range).
    /// Zoom with = and -. Drawn from the shared textures; nothing renders the world for it.
    /// </summary>
    public sealed class Minimap
    {
        public const float Size = 220f;
        private static readonly float[] Zooms = { 0.35f, 0.6f, 1f, 1.8f, 3f };

        private readonly GameController _gc;
        private readonly VisualElement _disc, _image, _fog, _symbols, _north;
        private readonly Label _info;
        private readonly List<MapSymbol> _found = new List<MapSymbol>();
        private int _zoom = 2;

        public VisualElement Root { get; }

        public Minimap(GameController gc)
        {
            _gc = gc;
            Root = UiKit.Div("minimap");
            Root.pickingMode = PickingMode.Ignore;
            _disc = Root.Put(UiKit.Div("minimap-disc"));
            _image = _disc.Put(UiKit.Div("map-layer"));
            _fog = _disc.Put(UiKit.Div("map-layer"));
            _symbols = _disc.Put(UiKit.Div("map-layer"));
            _north = Root.Put(UiKit.Text("N", "minimap-north"));
            _info = Root.Put(UiKit.Text("", "minimap-info"));
            foreach (VisualElement e in new[] { _disc, _image, _fog, _symbols, _north, _info }) e.pickingMode = PickingMode.Ignore;
        }

        private readonly List<VisualElement> _pool = new List<VisualElement>();
        private readonly List<MapSymbolKind> _poolKinds = new List<MapSymbolKind>();
        private readonly List<Color> _poolTints = new List<Color>();

        public float MetersPerPixel => Zooms[_zoom];

        /// <summary>The pooled symbol element at <paramref name="index"/>, restyled for <paramref name="s"/> only when it changes.</summary>
        private VisualElement Slot(int index, MapSymbol s)
        {
            Color tint = MapSymbols.Tint(s);
            float size = s.Kind == MapSymbolKind.Player ? 22f : s.Kind == MapSymbolKind.Survivor ? 9f : s.Kind == MapSymbolKind.Poi ? 13f : 17f;
            if (index >= _pool.Count)
            {
                VisualElement fresh = MapSymbols.Make(s, size, false);
                _symbols.Add(fresh);
                _pool.Add(fresh);
                _poolKinds.Add(s.Kind);
                _poolTints.Add(tint);
                return fresh;
            }
            VisualElement e = _pool[index];
            UiKit.Show(e, true);
            if (_poolKinds[index] != s.Kind || _poolTints[index] != tint)
            {
                VisualElement icon = e[0];
                icon.style.backgroundImage = new StyleBackground(ItemIcons.Glyph(MapSymbols.Glyph(s.Kind)));
                icon.style.unityBackgroundImageTintColor = tint;
                icon.style.width = size;
                icon.style.height = size;
                icon.style.left = -size * 0.5f;
                icon.style.top = -size * 0.5f;
                _poolKinds[index] = s.Kind;
                _poolTints[index] = tint;
            }
            return e;
        }

        public void Zoom(int steps) => _zoom = Mathf.Clamp(_zoom + steps, 0, Zooms.Length - 1);

        public void Refresh(bool visible)
        {
            UiKit.Show(Root, visible);
            if (!visible) return;
            MapTextures maps = _gc.Maps;
            WorldState w = _gc.Session.World;
            float mpp = Zooms[_zoom];
            Vector3 me = _gc.Player.transform.position;
            float heading = _gc.CameraRig.ViewTransform.eulerAngles.y;
            bool rotate = _gc.Settings.MinimapRotate;
            float rot = rotate ? heading : 0f;
            var view = new MapView(me.x, me.z, mpp, Size, Size, rot);

            // The map image is placed so the survivor's point is at the disc centre, then turned about that point.
            float mapPx = w.Layout.Size / mpp;
            float left = Size * 0.5f - me.x / mpp;
            float top = Size * 0.5f - (w.Layout.Size - me.z) / mpp;
            foreach (VisualElement e in new[] { _image, _fog })
            {
                e.style.left = left;
                e.style.top = top;
                e.style.width = mapPx;
                e.style.height = mapPx;
                e.style.transformOrigin = new TransformOrigin(Length.Pixels(me.x / mpp), Length.Pixels((w.Layout.Size - me.z) / mpp));
                e.style.rotate = new Rotate(new Angle(-rot, AngleUnit.Degree));
            }
            if (maps.Overview != null) _image.style.backgroundImage = new StyleBackground(maps.Overview);
            if (maps.Fog != null) _fog.style.backgroundImage = new StyleBackground(maps.Fog);

            // North marker on the rim.
            view.WorldToScreen(me.x, me.z + 1000f, out float nx, out float ny);
            Vector2 dir = new Vector2(nx - Size * 0.5f, ny - Size * 0.5f).normalized;
            _north.style.left = Size * 0.5f + dir.x * (Size * 0.5f - 2f) - 8f;
            _north.style.top = Size * 0.5f + dir.y * (Size * 0.5f - 2f) - 10f;

            MapSymbols.Collect(_gc, maps.RevealAll, _found);
            float radius = Size * 0.5f - 9f;
            int used = 0;
            foreach (MapSymbol s in _found)
            {
                view.WorldToScreen(s.X, s.Z, out float px, out float py);
                var off = new Vector2(px - Size * 0.5f, py - Size * 0.5f);
                bool outside = off.magnitude > radius;
                if (outside && s.Kind != MapSymbolKind.Waypoint) continue;
                if (outside) off = off.normalized * radius;
                // Elements are pooled: the minimap updates every frame, so it must not allocate every frame.
                VisualElement e = Slot(used++, s);
                e.style.left = Size * 0.5f + off.x;
                e.style.top = Size * 0.5f + off.y;
                e[0].style.rotate = new Rotate(new Angle(s.Kind == MapSymbolKind.Player ? s.Heading - rot : 0f, AngleUnit.Degree));
            }
            for (int i = used; i < _pool.Count; i++) UiKit.Show(_pool[i], false);

            MapNavigation nav = w.Navigation;
            string place = w.Layout.PoiAt(me.x, me.z) is PoiInfo poi && w.Exploration.DiscoveredPois.Contains(poi.Id) ? poi.Name : "";
            string waypoint = nav.HasWaypoint
                ? $"{nav.WaypointLabel} · {MapGeometry.Distance(Vec3.DistanceXZ(new Vec3(me.x, 0f, me.z), nav.Waypoint))}\u00A0{MapGeometry.Cardinal(MapGeometry.Bearing(new Vec3(me.x, 0f, me.z), nav.Waypoint))}"
                : "";
            _info.text = string.Join("\n", new[] { place, waypoint }).Trim('\n');
            UiKit.Show(_info, _info.text.Length > 0);
        }
    }
}
