using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    public enum MapSymbolKind
    {
        Player,
        Waypoint,
        Marker,
        Bookmark,
        Poi,
        Settlement,
        Survivor,
        Vehicle
    }

    /// <summary>One thing drawn on the map or minimap at a world position.</summary>
    public struct MapSymbol
    {
        public MapSymbolKind Kind;
        public float X;
        public float Z;
        public string Label;
        public string PoiKind;
        public float Heading;
        public EntityId Ref;
    }

    /// <summary>
    /// What the player knows and may see on the map, gathered from the world state: their own
    /// position and heading, the waypoint, their markers and bookmarks, discovered points of
    /// interest, their settlement and recruited survivors. Undiscovered places are left out
    /// unless the developer "reveal all" view is on; hidden enemies are never shown.
    /// </summary>
    public static class MapSymbols
    {
        public static void Collect(GameController gc, bool revealAll, List<MapSymbol> into)
        {
            into.Clear();
            WorldState w = gc.Session.World;
            foreach (PoiInfo p in w.Layout.Pois)
            {
                if (!revealAll && !w.Exploration.DiscoveredPois.Contains(p.Id)) continue;
                into.Add(new MapSymbol { Kind = MapSymbolKind.Poi, X = p.Center.X, Z = p.Center.Z, Label = p.Name, PoiKind = p.Kind });
            }
            if (TrySettlement(w, gc.Session.Content, out Vec3 home))
                into.Add(new MapSymbol { Kind = MapSymbolKind.Settlement, X = home.X, Z = home.Z, Label = "Your settlement" });
            foreach (ColonistState c in w.Colonists.Values)
                if (c.Recruited && !c.Vitals.IsDead) into.Add(new MapSymbol { Kind = MapSymbolKind.Survivor, X = c.Position.X, Z = c.Position.Z, Label = c.Name, Ref = c.Id });
            foreach (Afterfall.Domain.Vehicles.VehicleState v in w.Vehicles.Values)
            {
                // Only vehicles the survivor knows about: ones they have been in, inspected or claimed.
                if (!v.Known && !v.Claimed && !revealAll) continue;
                var vdef = gc.Session.Content.Vehicle(v.DefId);
                into.Add(new MapSymbol { Kind = MapSymbolKind.Vehicle, X = v.Position.X, Z = v.Position.Z, Label = (vdef?.Name ?? "Vehicle") + (v.Claimed ? " (yours)" : ""), Ref = v.Id });
            }
            foreach (MapMarker m in w.Navigation.Markers)
                into.Add(new MapSymbol { Kind = m.Kind == MarkerKind.Bookmark ? MapSymbolKind.Bookmark : MapSymbolKind.Marker, X = m.Position.X, Z = m.Position.Z, Label = m.Name, Ref = m.Id });
            if (w.Navigation.HasWaypoint)
                into.Add(new MapSymbol { Kind = MapSymbolKind.Waypoint, X = w.Navigation.Waypoint.X, Z = w.Navigation.Waypoint.Z, Label = w.Navigation.WaypointLabel });
            Vector3 p0 = gc.Player.transform.position.ToWorld();
            float heading = gc.Player.Flight.Active ? gc.CameraRig.ViewTransform.eulerAngles.y : gc.Player.transform.eulerAngles.y;
            into.Add(new MapSymbol { Kind = MapSymbolKind.Player, X = p0.x, Z = p0.z, Label = "You", Heading = heading });
        }

        /// <summary>The respawn bed if one is set, otherwise the centre of the player's finished buildings (three or more).</summary>
        public static bool TrySettlement(WorldState w, Afterfall.Domain.Content.ContentDatabase content, out Vec3 at)
        {
            at = default;
            if (w.Player.SpawnStructureId != EntityId.None && w.Structures.TryGetValue(w.Player.SpawnStructureId, out Structure bed))
            {
                at = bed.Position;
                return true;
            }
            float x = 0f, z = 0f;
            int n = 0;
            foreach (Structure s in w.Structures.Values)
            {
                if (!s.PlayerBuilt || !s.IsComplete) continue;
                x += s.Position.X;
                z += s.Position.Z;
                n++;
            }
            if (n < 3) return false;
            at = new Vec3(x / n, 0f, z / n);
            return true;
        }

        public static string Glyph(MapSymbolKind kind)
        {
            switch (kind)
            {
                case MapSymbolKind.Player: return "map_player";
                case MapSymbolKind.Waypoint: return "map_waypoint";
                case MapSymbolKind.Marker: return "map_marker";
                case MapSymbolKind.Bookmark: return "map_bookmark";
                case MapSymbolKind.Settlement: return "map_settlement";
                case MapSymbolKind.Vehicle: return "map_vehicle";
                default: return "map_dot";
            }
        }

        /// <summary>Tint for POI and survivor dots (by point-of-interest kind).</summary>
        public static Color Tint(MapSymbol s)
        {
            if (s.Kind == MapSymbolKind.Survivor) return new Color(0.45f, 0.9f, 0.45f);
            if (s.Kind != MapSymbolKind.Poi) return Color.white;
            switch (s.PoiKind)
            {
                case "residential": return new Color(0.92f, 0.78f, 0.5f);
                case "fuel": return new Color(0.95f, 0.55f, 0.3f);
                case "workshop": return new Color(0.65f, 0.7f, 0.78f);
                case "substation": return new Color(0.98f, 0.88f, 0.3f);
                case "quarry": return new Color(0.72f, 0.66f, 0.6f);
                case "farm": return new Color(0.7f, 0.86f, 0.4f);
                case "camp": return new Color(0.95f, 0.62f, 0.4f);
                default: return new Color(0.9f, 0.9f, 0.85f);
            }
        }

        /// <summary>A symbol element: a glyph icon, optionally with a text label underneath.</summary>
        public static VisualElement Make(MapSymbol s, float size, bool label)
        {
            var root = new VisualElement();
            root.AddToClassList("map-sym");
            root.pickingMode = PickingMode.Ignore;
            var icon = new VisualElement();
            icon.AddToClassList("map-sym-icon");
            icon.pickingMode = PickingMode.Ignore;
            icon.style.width = size;
            icon.style.height = size;
            icon.style.left = -size * 0.5f;
            icon.style.top = -size * 0.5f;
            icon.style.backgroundImage = new StyleBackground(ItemIcons.Glyph(Glyph(s.Kind)));
            icon.style.unityBackgroundImageTintColor = Tint(s);
            root.Add(icon);
            if (label && !string.IsNullOrEmpty(s.Label))
            {
                var l = new Label(s.Label);
                l.AddToClassList("map-sym-label");
                if (s.Kind == MapSymbolKind.Poi && s.PoiKind == "residential") l.AddToClassList("map-sym-label--town");
                l.pickingMode = PickingMode.Ignore;
                l.style.top = size * 0.5f;
                root.Add(l);
            }
            return root;
        }
    }
}
