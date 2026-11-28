using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Core;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>
    /// The way by road to the player's waypoint, for the map, the minimap and the HUD. The road
    /// network is built in the background from the roads known (the world's roads and the streets
    /// of the chunks made so far) and rebuilt now and then as more streets are generated; a route is
    /// planned when the waypoint changes and again when the survivor strays from it. Nothing here
    /// is saved: it follows from the waypoint and the survivor's position.
    /// </summary>
    public sealed class RoutePlanner
    {
        /// <summary>Further than this from the route for <see cref="StraySeconds"/>, it is planned again.</summary>
        public const float StrayDistance = 35f;

        public const float StraySeconds = 2f;

        /// <summary>Within this distance of the destination the route is done.</summary>
        public const float ArriveDistance = 25f;

        private const float RebuildSeconds = 20f;

        private readonly GameSession _session;
        private Task<RoadNetwork> _building;
        private int _buildingVersion = -1, _networkVersion = -1;
        private Task<RoadRoute> _routing;
        private int _waypointVersion = -1, _hint;
        private float _sinceBuild, _strayFor;
        private bool _replan;

        public RoutePlanner(GameSession session)
        {
            _session = session;
        }

        /// <summary>The network routes are planned on, once built.</summary>
        public RoadNetwork Network { get; private set; }

        /// <summary>The current way to the waypoint, or null (no waypoint, or still planning).</summary>
        public RoadRoute Route { get; private set; }

        /// <summary>How far along <see cref="Route"/> the survivor is, and how far off it.</summary>
        public float Along { get; private set; }

        public float OffRoute { get; private set; }

        /// <summary>Metres still to go along the route.</summary>
        public float Remaining => Route == null ? 0f : Math.Max(0f, Route.Length - Along);

        /// <summary>Changes whenever a new route is adopted (the map redraws it).</summary>
        public int Version { get; private set; }

        /// <summary>Routes planned so far (tests and the tour).</summary>
        public int Planned { get; private set; }

        public bool Planning => _routing != null || (_building != null && Network == null);

        /// <summary>Called every frame with the survivor's position and the real time step.</summary>
        public void Update(Vec3 survivor, float dt)
        {
            MapNavigation nav = _session.World.Navigation;
            WorldLayout layout = _session.World.Layout;
            _sinceBuild += dt;
            // The network is built from the start (on its own thread, not queued behind chunk work), so a
            // waypoint set later is routed at once; it is rebuilt for new streets only while a waypoint is set.
            if (_building == null && (Network == null || (nav.HasWaypoint && layout.ChunkVersion != _networkVersion && _sinceBuild > RebuildSeconds)))
            {
                // The list is copied here, on the thread that adds chunk streets to it.
                var roads = new List<RoadPath>(layout.Roads);
                int major = layout.FixedRoadCount;
                _buildingVersion = layout.ChunkVersion;
                _sinceBuild = 0f;
                _building = Task.Factory.StartNew(() => RoadNetwork.Build(roads, major), TaskCreationOptions.LongRunning);
            }
            if (!nav.HasWaypoint)
            {
                if (Route != null)
                {
                    Route = null;
                    Version++;
                }
                _waypointVersion = nav.Version;
                if (_building != null && _building.IsCompleted) TakeNetwork();
                return;
            }
            if (_building != null && _building.IsCompleted)
            {
                // A network with more streets may know a better way from here.
                TakeNetwork();
                _replan = true;
            }
            if (Network == null) return;

            if (nav.Version != _waypointVersion)
            {
                _waypointVersion = nav.Version;
                _replan = true;
            }
            if (Route != null)
            {
                Along = Route.Locate(survivor, ref _hint, out float off);
                OffRoute = off;
                _strayFor = off > StrayDistance && Remaining > ArriveDistance ? _strayFor + dt : 0f;
                if (_strayFor > StraySeconds) _replan = true;
            }
            if (_replan && _routing == null)
            {
                _replan = false;
                _strayFor = 0f;
                RoadNetwork network = Network;
                Vec3 to = nav.Waypoint;
                _routing = Task.Factory.StartNew(() => network.Route(survivor, to), TaskCreationOptions.LongRunning);
            }
            if (_routing != null && _routing.IsCompleted)
            {
                if (_routing.IsFaulted) throw _routing.Exception;
                Adopt(_routing.Result, survivor);
                _routing = null;
            }
        }

        private void TakeNetwork()
        {
            if (_building.IsFaulted) throw _building.Exception;
            Network = _building.Result;
            _networkVersion = _buildingVersion;
            _building = null;
        }

        /// <summary>Builds the network if needed and plans the route at once (tests and the tour).</summary>
        public RoadRoute PlanNow(Vec3 survivor)
        {
            MapNavigation nav = _session.World.Navigation;
            if (!nav.HasWaypoint) return null;
            if (Network == null)
            {
                Network = RoadNetwork.Build(new List<RoadPath>(_session.World.Layout.Roads), _session.World.Layout.FixedRoadCount);
                _networkVersion = _session.World.Layout.ChunkVersion;
            }
            _waypointVersion = nav.Version;
            Adopt(Network.Route(survivor, nav.Waypoint), survivor);
            return Route;
        }

        private void Adopt(RoadRoute route, Vec3 survivor)
        {
            Route = route;
            _hint = 0;
            Along = route.Locate(survivor, ref _hint, out float off);
            OffRoute = off;
            Planned++;
            Version++;
        }

        /// <summary>A short line for the HUD: the next turn and the distance left, or null with no route.</summary>
        public string Guidance()
        {
            if (Route == null) return null;
            if (Remaining <= ArriveDistance) return "Arrived";
            string left = MapGeometry.Distance(Remaining);
            if (!Route.NextTurn(Along, out RouteTurn next) || next.Kind == TurnKind.Arrive)
                return Route.Direct ? $"{left} across country" : $"{left} to go";
            return $"{RouteTurn.Describe(next.Kind)} in {MapGeometry.Distance(Math.Max(0f, next.Along - Along))} · {left} to go";
        }
    }
}
