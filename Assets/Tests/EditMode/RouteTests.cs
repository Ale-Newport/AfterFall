using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Afterfall.Core;
using Afterfall.Domain.World;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Road-aware routes to the waypoint: the road network, its junctions and the route planner.</summary>
    public sealed class RouteTests
    {
        private static RoadPath Road(float width, bool paved, params float[] xz)
        {
            var r = new RoadPath { Width = width, Paved = paved };
            for (int i = 0; i + 1 < xz.Length; i += 2)
            {
                // Short lengths, as the generators make them.
                if (r.Points.Count > 0)
                {
                    Vec3 last = r.Points[r.Points.Count - 1], next = new Vec3(xz[i], 0f, xz[i + 1]);
                    int steps = (int)System.Math.Ceiling(Vec3.DistanceXZ(last, next) / 40f);
                    for (int k = 1; k < steps; k++) r.Points.Add(Vec3.Lerp(last, next, k / (float)steps));
                }
                r.Points.Add(new Vec3(xz[i], 0f, xz[i + 1]));
            }
            return r;
        }

        private static float DistanceToRoads(IEnumerable<RoadPath> roads, Vec3 p)
        {
            float best = float.MaxValue;
            foreach (RoadPath r in roads)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                    best = System.Math.Min(best, WorldLayout.DistanceToSegmentXZ(p.X, p.Z, r.Points[i], r.Points[i + 1], out _));
            return best;
        }

        [Test]
        public void Route_FollowsTheRoads_AndTurnsAtTheCrossing()
        {
            // An east–west road crossed by a north–south one (the crossing is not a point of either).
            var roads = new List<RoadPath> { Road(8f, true, 0, 0, 1000, 0), Road(8f, true, 515, -500, 515, 500) };
            RoadNetwork net = RoadNetwork.Build(roads);
            Assert.AreEqual(4, net.DegreeAt(new Vec3(515f, 0f, 0f)), "the roads meet where they cross");

            RoadRoute route = net.Route(new Vec3(10f, 0f, 20f), new Vec3(515f, 0f, 470f));
            Assert.IsFalse(route.Direct);
            Assert.AreEqual(20f + 505f + 470f, route.Length, 2f, "onto the road, east to the crossing and north");
            Assert.AreEqual(20f, route.OffRoad, 0.5f);
            for (int i = 1; i < route.Points.Count - 1; i++)
                Assert.Less(DistanceToRoads(roads, route.Points[i]), 0.5f, "every point between the ends is on a road");
            RouteTurn turn = route.Turns.First();
            Assert.AreEqual(TurnKind.Left, turn.Kind, "heading east, north is to the left");
            Assert.AreEqual(515f, turn.Position.X, 0.5f);
            Assert.AreEqual(0f, turn.Position.Z, 0.5f);
            Assert.AreEqual(TurnKind.Arrive, route.Turns.Last().Kind);

            // Going the other way it is a right turn, south then west.
            RoadRoute back = net.Route(new Vec3(515f, 0f, 470f), new Vec3(10f, 0f, 20f));
            Assert.AreEqual(TurnKind.Right, back.Turns.First().Kind);
            Assert.AreEqual(route.Length, back.Length, 0.5f);
        }

        [Test]
        public void Route_JoinsRoadsThatEndAgainstAnother_AndIgnoresRailways()
        {
            var roads = new List<RoadPath>
            {
                Road(8f, true, 0, 0, 1000, 0),
                // A side road stopping a metre short of the main road's edge.
                Road(6f, true, 300, 5, 300, 600),
                // A railway from the side road's top to far east: never driven along.
                Road(4f, false, 300, 600, 2000, 600)
            };
            roads[2].Rail = true;
            RoadNetwork net = RoadNetwork.Build(roads);
            Assert.AreEqual(3, net.DegreeAt(new Vec3(300f, 0f, 0f)), "the side road is joined to the main road");

            RoadRoute up = net.Route(new Vec3(990f, 0f, -5f), new Vec3(300f, 0f, 590f));
            Assert.IsFalse(up.Direct);
            Assert.AreEqual(690f + 590f, up.Length, 25f, "west along the main road, then up the side road");
            Assert.AreEqual(TurnKind.Right, up.Turns.First().Kind, "heading west, north is to the right");

            // Along the railway there is no road: straight across country is quicker than the detour.
            RoadRoute rail = net.Route(new Vec3(400f, 0f, 610f), new Vec3(1900f, 0f, 610f));
            Assert.IsTrue(rail.Direct);
            Assert.AreEqual(1500f, rail.Length, 0.5f);
        }

        [Test]
        public void Route_GoesAcrossCountry_OnlyWhenThatIsQuicker()
        {
            var roads = new List<RoadPath> { Road(8f, true, 0, 0, 1000, 0), Road(6f, false, 0, 200, 1000, 200) };
            RoadNetwork net = RoadNetwork.Build(roads);
            RoadRoute far = net.Route(new Vec3(100f, 0f, 30f), new Vec3(900f, 0f, 30f));
            Assert.IsFalse(far.Direct, "800 m apart beside a road: take the road");
            Assert.AreEqual(60f, far.OffRoad, 15f, "a short way across to the road at each end");
            RoadRoute near = net.Route(new Vec3(100f, 0f, 30f), new Vec3(130f, 0f, 60f));
            Assert.IsTrue(near.Direct, "a few dozen metres: just walk there");
            // Between the two roads, the paved one is preferred over the track at the same distance.
            RoadRoute mid = net.Route(new Vec3(100f, 0f, 100f), new Vec3(900f, 0f, 100f));
            Assert.Less(mid.Points.Skip(1).Take(mid.Points.Count - 2).Max(p => p.Z), 1f, "along the paved road, not the track");
        }

        [Test]
        public void Route_CrossesCountryToTheMainRoads_WhenLocalStreetsLeadNowhere()
        {
            var roads = new List<RoadPath>
            {
                // The main road (a fixed road of the world) …
                Road(9f, true, 0, 0, 20000, 0),
                // … and a block of streets 600 m north of it, not joined to it (the chunks between are not made yet).
                Road(8f, true, 900, 600, 1300, 600, 1300, 900, 900, 900, 900, 600)
            };
            RoadNetwork net = RoadNetwork.Build(roads, 1);
            RoadRoute route = net.Route(new Vec3(1100f, 0f, 610f), new Vec3(19000f, 0f, 10f));
            Assert.IsFalse(route.Direct);
            Assert.AreEqual(640f, route.OffRoad, 40f, "across country to the main road (a little slantwise saves road), then along it");
            Assert.Less(route.Length, 18700f);
        }

        [Test]
        public void Basin_RouteBetweenTowns_FollowsTheRoads()
        {
            WorldLayout l = WorldGenerator.Generate(20261107, 3);
            var sw = Stopwatch.StartNew();
            RoadNetwork net = RoadNetwork.Build(l.Roads);
            long build = sw.ElapsedMilliseconds;
            Assert.Less(build, 4000, "the network is built in the background in a few seconds at most");

            Vec3 from = l.PlayerSpawn;
            PoiInfo kingsmoor = l.Pois.First(p => p.Id == "kingsmoor");
            sw.Restart();
            RoadRoute route = net.Route(from, kingsmoor.Center);
            long plan = sw.ElapsedMilliseconds;
            float straight = Vec3.DistanceXZ(from, kingsmoor.Center);
            TestContext.WriteLine($"Network: {net.NodeCount} nodes, {net.EdgeCount} edges in {build} ms; route {route.Length / 1000f:0.0} km (straight {straight / 1000f:0.0} km), {route.OffRoad:0} m off-road, {route.Turns.Count} turns, planned in {plan} ms");
            Assert.Less(plan, 1500);
            Assert.IsFalse(route.Direct);
            Assert.Less(route.OffRoad, 1500f, "both ends are near roads");
            Assert.GreaterOrEqual(route.Length, straight);
            Assert.Less(route.Length, straight * 1.6f, "no wild detours");
            Assert.GreaterOrEqual(route.Turns.Count(t => t.Kind != TurnKind.Arrive && t.Kind != TurnKind.LeaveRoad), 1, "it turns at junctions on the way");
            var drivable = l.Roads.Where(r => !r.Rail).ToList();
            for (int i = 2; i < route.Points.Count - 2; i += 25)
                Assert.Less(DistanceToRoads(drivable, route.Points[i]), 3f, "the way runs along the roads");
        }

        [Test]
        public void Planner_FollowsTheSurvivor_AndPlansAgainWhenTheyStray()
        {
            GameSession s = TestSupport.NewSession();
            WorldLayout l = s.World.Layout;
            Assert.IsNull(s.Routes.PlanNow(s.Player.Position), "no waypoint, no route");
            PoiInfo target = l.Pois.OrderByDescending(p => Vec3.DistanceXZ(p.Center, s.Player.Position)).First();
            Assert.IsTrue(s.SetWaypoint(target.Center, target.Name).Ok);
            RoadRoute route = s.Routes.PlanNow(s.Player.Position);
            Assert.NotNull(route);
            Assert.That(s.Routes.Guidance(), Does.Contain("to go").Or.Contain("across country"));
            int planned = s.Routes.Planned;

            // Halfway along the route, the planner follows progress without planning again.
            float half = route.Length * 0.5f;
            int idx = route.Along.FindIndex(a => a >= half);
            Vec3 onRoute = route.Points[idx];
            for (int i = 0; i < 10; i++) s.Routes.Update(onRoute, 0.25f);
            Assert.AreEqual(planned, s.Routes.Planned);
            Assert.AreEqual(route.Along[idx], s.Routes.Along, 1f);

            // Well off the route for a few seconds: a new route from where the survivor is.
            // A point 120 m away that is well clear of every part of the route.
            Vec3 astray = onRoute;
            float clear = 0f;
            for (int a = 0; a < 16; a++)
            {
                double ang = a * System.Math.PI / 8;
                Vec3 c = onRoute + new Vec3((float)System.Math.Cos(ang) * 120f, 0f, (float)System.Math.Sin(ang) * 120f);
                if (!l.IsInsideMap(c.X, c.Z, 5f)) continue;
                int h = 0;
                route.Locate(c, ref h, out float off);
                if (off > clear)
                {
                    clear = off;
                    astray = c;
                }
            }
            Assert.Greater(clear, RoutePlanner.StrayDistance + 5f);
            var sw = Stopwatch.StartNew();
            while (s.Routes.Planned == planned && sw.ElapsedMilliseconds < 5000)
            {
                s.Routes.Update(astray, 0.5f);
                Thread.Sleep(2);
            }
            Assert.Greater(s.Routes.Planned, planned, "planned again after straying");
            Assert.Less(Vec3.DistanceXZ(s.Routes.Route.Points[0], astray), 0.5f, "the new route starts where the survivor is");

            Assert.IsTrue(s.ClearWaypoint().Ok);
            s.Routes.Update(astray, 0.1f);
            Assert.IsNull(s.Routes.Route);
            Assert.IsNull(s.Routes.Guidance());
        }
    }
}
