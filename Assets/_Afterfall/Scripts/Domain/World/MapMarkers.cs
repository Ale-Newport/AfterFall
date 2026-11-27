using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    public enum MarkerKind
    {
        /// <summary>A place the player marked on the world map.</summary>
        Custom,

        /// <summary>A developer bookmark saved from the creative panel.</summary>
        Bookmark
    }

    /// <summary>A named point the player placed on the map (saved).</summary>
    public sealed class MapMarker
    {
        public EntityId Id { get; set; }
        public string Name { get; set; } = "";
        public Vec3 Position { get; set; }
        public MarkerKind Kind { get; set; }
    }

    /// <summary>
    /// The player's own map annotations: custom markers, developer bookmarks and the single
    /// active waypoint the minimap and HUD point to. Everything here is saved.
    /// </summary>
    public sealed class MapNavigation
    {
        public const int MaxMarkers = 64;
        public const int MaxNameLength = 40;

        public List<MapMarker> Markers { get; } = new List<MapMarker>();
        public bool HasWaypoint { get; private set; }
        public Vec3 Waypoint { get; private set; }
        public string WaypointLabel { get; private set; } = "";

        /// <summary>Changes whenever markers or the waypoint change (the UI redraws them).</summary>
        public int Version { get; private set; }

        public void SetWaypoint(Vec3 position, string label)
        {
            HasWaypoint = true;
            Waypoint = position;
            WaypointLabel = Clean(label, "Waypoint");
            Version++;
        }

        public void ClearWaypoint()
        {
            if (!HasWaypoint) return;
            HasWaypoint = false;
            WaypointLabel = "";
            Version++;
        }

        public OpResult AddMarker(EntityId id, string name, Vec3 position, MarkerKind kind, out MapMarker marker)
        {
            marker = null;
            if (Markers.Count >= MaxMarkers) return OpResult.Fail("too_many", $"You already have {MaxMarkers} markers. Remove one first.");
            marker = new MapMarker { Id = id, Name = Clean(name, kind == MarkerKind.Bookmark ? "Bookmark" : "Marker"), Position = position, Kind = kind };
            Markers.Add(marker);
            Version++;
            return OpResult.Success($"Marked {marker.Name}.");
        }

        public bool RemoveMarker(EntityId id)
        {
            int removed = Markers.RemoveAll(m => m.Id == id);
            if (removed > 0) Version++;
            return removed > 0;
        }

        public MapMarker Find(EntityId id)
        {
            foreach (MapMarker m in Markers)
                if (m.Id == id) return m;
            return null;
        }

        /// <summary>Restores saved state (used by the save mapper).</summary>
        public void Restore(IEnumerable<MapMarker> markers, bool hasWaypoint, Vec3 waypoint, string label)
        {
            Markers.Clear();
            if (markers != null) Markers.AddRange(markers);
            HasWaypoint = hasWaypoint;
            Waypoint = waypoint;
            WaypointLabel = hasWaypoint ? Clean(label, "Waypoint") : "";
            Version++;
        }

        private static string Clean(string name, string fallback)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) n = fallback;
            return n.Length > MaxNameLength ? n.Substring(0, MaxNameLength) : n;
        }
    }

    /// <summary>Compass helpers shared by the HUD, minimap and world map.</summary>
    public static class MapGeometry
    {
        /// <summary>Bearing in degrees (0 = north = +Z, 90 = east = +X) from one point to another.</summary>
        public static float Bearing(Vec3 from, Vec3 to)
        {
            double deg = Math.Atan2(to.X - from.X, to.Z - from.Z) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            return (float)deg;
        }

        public static string Cardinal(float bearing)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int i = (int)Math.Floor(((bearing % 360f) + 360f + 22.5f) % 360f / 45f);
            return names[i];
        }

        /// <summary>"340 m" or "2.4 km".</summary>
        public static string Distance(float metres) => metres >= 1000f
            ? (metres / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km"
            : metres.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
    }
}
