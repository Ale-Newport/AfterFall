using System.Collections;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.World;
using Afterfall.Services;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>The basin scene of the scripted tour: a new game in the 64 km Kessel Basin and a visit to its biomes and its city.</summary>
    public sealed partial class AutoTour
    {
        /// <summary><c>-afterfall-tour-basin</c>: run only the basin scene.</summary>
        public const string OnlyBasinFlag = "-afterfall-tour-basin";

        private IEnumerator BasinScene(GameController previous)
        {
            Line("— The Kessel Basin (world version 3) —");
            float t0 = Time.realtimeSinceStartup;
            _boot.StartNewGame(DifficultyPreset.Survival, 20261107, GameMode.Survival, 3);
            float timeout = Time.realtimeSinceStartup + P(240f);
            while ((_boot.Game == null || _boot.Game == previous || !_boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            GameController gc = _boot.Game;
            if (gc == null || gc == previous || !gc.Running)
            {
                Line("FAIL: the basin did not load");
                yield break;
            }
            float loaded = Time.realtimeSinceStartup - t0;
            while (!gc.NavReady && Time.realtimeSinceStartup < timeout) yield return null;
            GameSession s = gc.Session;
            WorldLayout l = s.World.Layout;
            s.Threats.SpawningEnabled = false;
            s.Weather.Hold(Afterfall.Domain.Weather.WeatherKind.Clear);
            SkipTo(gc, 15.5);
            Check(l.IsChunked && Mathf.Approximately(l.Size, 65536f), $"A new game in {l.RegionName}: {l.Size / 1000f:0} km across, {l.GeneratedChunkCount} chunks generated, {s.World.SeededChunks.Count} seeded, {s.World.ResourceNodes.Count} nodes, {s.World.Structures.Count} structures, {s.World.Zombies.Count} infected");
            Line($"Basin load: playable after {loaded:0.0} s");
            yield return Seconds(3f);
            yield return Capture("90_basin_start");
            yield return MeasureFrames("basin start (Millbrook)", 4f);

            var stops = new (string Name, float X, float Z, float Yaw, float Pitch)[]
            {
                ("Kesselmouth, Old Quarter", 39980f, 30250f, 40f, 4f),
                ("Kesselmouth, terraced streets", 33300f, 33900f, 120f, 6f),
                ("Kesselmouth, Docklands", 51800f, 29800f, 200f, 6f),
                ("the Aldwater in the city", 44500f, 31200f, 250f, 10f),
                ("the Greyspine (snow)", 6300f, 30400f, 90f, 2f),
                ("the High Moor", 30200f, 54200f, 0f, 8f),
                ("the Lake District", 50600f, 54200f, 30f, 8f),
                ("the Scar", 10600f, 6200f, 60f, 8f),
                ("the southern coast", 57600f, 9400f, 90f, 10f),
                ("Harrowgate", 14050f, 8880f, 20f, 8f)
            };
            int shot = 0;
            foreach ((string name, float x, float z, float yaw, float pitch) in stops)
            {
                gc.TeleportTo(new Vec3(x, l.HeightAt(x, z), z).ToUnity(), name, false);
                yield return null;
                float tp = Time.realtimeSinceStartup + P(60f);
                while (gc.Teleporting && Time.realtimeSinceStartup < tp) yield return null;
                // Let streaming catch up: terrain, chunks, the city's meshes and the NavMesh.
                float settle = Time.realtimeSinceStartup + P(25f);
                yield return Seconds(2f);
                while ((gc.TerrainTiles.PaintingTiles > 0 || l.PendingChunks > 0 || gc.NavBaking) && Time.realtimeSinceStartup < settle) yield return null;
                yield return Seconds(2.5f);
                Vector3 here = gc.Player.transform.position.ToWorld();
                gc.CameraRig.Look(yaw, pitch);
                yield return Seconds(1f);
                ChunkViews v = gc.Chunks;
                Vector3 local = gc.Player.transform.position;
                bool onMesh = UnityEngine.AI.NavMesh.SamplePosition(local, out UnityEngine.AI.NavMeshHit nh, 3f, UnityEngine.AI.NavMesh.AllAreas);
                Check(Mathf.Abs(local.x) < 2600f && Mathf.Abs(local.z) < 2600f && onMesh,
                    $"Floating origin at {Fmt(Conversions.Origin)} after {gc.OriginShifts} shifts: the survivor is at Unity {Fmt(local)} (world {Fmt(here)}), on the NavMesh {onMesh} (window at {Fmt(gc.NavWindowCentre)}, {UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length / 3} triangles)");
                Check(Vector3.Distance(Flat(here), new Vector3(x, 0, z)) < 60f && gc.IsWorldReadyAt(here.FromWorld()),
                    $"At {name} {Fmt(here)}, {here.y:0} m: {l.GeneratedChunkCount} chunks made, city {v.DetailedChunks} detailed + {v.SkylineChunks} skyline, {gc.Sync.NodeViewCount} node views, {gc.TerrainTiles.LoadedTiles} tiles ({gc.TerrainTiles.PaintingTiles} painting, camera at {Fmt(gc.CameraRig.Camera.transform.position.ToWorld())}, ground {l.HeightAt(here.x, here.z):0} m)");
                if (name == "the Lake District")
                    Check(gc.FarTrees != null && gc.FarTrees.VisibleCells > 20, $"Distant trees stand in for the real ones beyond {WorldSync.NodeViewRadius:0} m: {gc.FarTrees?.VisibleCells} cells in {gc.FarTrees?.ChunkCount} chunks");
                if (shot == 0)
                {
                    float mw = Time.realtimeSinceStartup + P(20f);
                    while (gc.Maps.Detail == null && Time.realtimeSinceStartup < mw) yield return null;
                    yield return Seconds(1f);
                    Check(gc.Maps.Detail != null, $"The minimap draws the city's streets and buildings from a local map around the survivor (from {gc.Maps.DetailOrigin})");
                }
                yield return Capture($"9{(shot < 9 ? "" : "9")}{shot + 1}_basin_{name.Replace(" ", "_").Replace(",", "").Replace("(", "").Replace(")", "")}");
                yield return MeasureFrames(name, 3f);
                shot++;
            }
            yield return BasinInteriors(gc, l);
            yield return CityTowers(gc, l);
            yield return RoadRoute(gc, l);
            float wait = Time.realtimeSinceStartup + P(30f);
            while (!gc.Maps.Ready && Time.realtimeSinceStartup < wait) yield return null;
            Check(gc.Maps.Ready, $"The basin's world map was drawn ({gc.Maps.OverviewSize}×{gc.Maps.OverviewSize} px for {l.Size / 1000f:0} km)");
            gc.Maps.RevealAll = true;
            gc.Ui.ToggleWorldMap();
            yield return Seconds(1.5f);
            yield return Capture("99z_basin_world_map");
            gc.Ui.CloseWindow();
            gc.Maps.RevealAll = false;
            var t = (TiledHeightField)l.Height;
            Line($"Basin streaming: {t.TilesBuilt} height tiles made ({t.CachedTiles} cached), {l.GeneratedChunkCount} chunks generated, {s.World.SeededChunks.Count} seeded, {s.World.ResourceNodes.Count} nodes and {s.World.Structures.Count} structures in the world, {s.World.Zombies.Count} infected");
            string save = Afterfall.Persistence.SaveSerializer.Serialize(s.World, "tour", 1);
            Check(save.Length < 20_000_000, $"A save after crossing the basin is {save.Length / 1024} KB");
        }

        /// <summary>Inside the city's buildings: the central station, a block of flats and a supermarket.</summary>
        private IEnumerator BasinInteriors(GameController gc, WorldLayout l)
        {
            var basin = (BasinWorld)l.Chunks;
            (Vec3 Centre, string Name) central = basin.Stations.FirstOrDefault(st => st.Name == "Kesselmouth Central");
            Check(central.Name != null, $"{basin.Stations.Count()} railway stations along the lines: {string.Join(", ", basin.Stations.Take(6).Select(st => st.Name))}…");
            if (central.Name == null) yield break;
            l.EnsureChunk(WorldLayout.ChunkOf(central.Centre.X), WorldLayout.ChunkOf(central.Centre.Z));
            var visits = new System.Collections.Generic.List<(string Label, PlacedPlan Plan, float X, float Z, int Floor, float Yaw)>();
            PlacedPlan station = l.Plans.FirstOrDefault(pp => pp.Plan.Name == central.Name);
            if (station != null) visits.Add(("the station's ticket hall", station, 0f, 2.5f, 0, 180f));
            // The nearest block of flats and supermarket among the chunks made so far.
            PlacedPlan Nearest(string kind) => l.Plans.Where(pp => pp.Plan.Id.StartsWith(kind + "|", System.StringComparison.Ordinal))
                .OrderBy(pp => Vec3.DistanceXZ(pp.Origin, central.Centre)).FirstOrDefault();
            PlacedPlan flats = Nearest(BuildingPlans.FlatsKind), market = Nearest(BuildingPlans.SupermarketKind);
            if (flats != null) visits.Add(($"the stair hall of {flats.Plan.Name} ({flats.Plan.Floors} storeys)", flats, -3f, 2.5f, 0, 180f));
            if (market != null) visits.Add(($"the sales floor of {market.Plan.Name}", market, 0f, 9f, 0, 180f));
            Check(flats != null && market != null, $"The city's chunks hold blocks of flats ({l.Plans.Count(pp => pp.Plan.Id.StartsWith("flats|", System.StringComparison.Ordinal))}) and supermarkets ({l.Plans.Count(pp => pp.Plan.Id.StartsWith("supermarket|", System.StringComparison.Ordinal))})");
            int n = 0;
            foreach ((string label, PlacedPlan plan, float px, float pz, int floor, float yaw) in visits)
            {
                Vec3 target = plan.ToWorld(px, pz, floor);
                gc.TeleportTo(target.ToUnity(), label, false);
                yield return null;
                float tp = Time.realtimeSinceStartup + P(60f);
                while (gc.Teleporting && Time.realtimeSinceStartup < tp) yield return null;
                float settle = Time.realtimeSinceStartup + P(20f);
                yield return Seconds(2f);
                while ((gc.TerrainTiles.PaintingTiles > 0 || l.PendingChunks > 0 || gc.NavBaking) && Time.realtimeSinceStartup < settle) yield return null;
                yield return Seconds(2f);
                gc.CameraRig.Look(plan.Yaw + yaw, 8f);
                yield return Seconds(1f);
                Vec3 at = gc.Player.transform.position.ToSim();
                PlanRoom room = l.RoomAt(at, out PlacedPlan inside);
                bool onMesh = UnityEngine.AI.NavMesh.SamplePosition(gc.Player.transform.position, out UnityEngine.AI.NavMeshHit hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
                int fixtures = gc.Session.World.Structures.Values.Count(st => Vec3.DistanceXZ(st.Position, plan.Origin) < Mathf.Max(plan.Plan.Width, plan.Plan.Depth) * 0.6f);
                Check(inside == plan && room != null && onMesh && fixtures >= 8,
                    $"Inside {label}: room {(room != null ? room.Name : "none")}, {fixtures} doors and fittings, on the NavMesh {onMesh}");
                yield return Capture($"99i{n++}_basin_{plan.Plan.Id.Substring(0, plan.Plan.Id.IndexOf('|'))}");
            }

            // Nothing is searched through a wall: outside the locked ticket office the desk inside is not offered,
            // while in the ticket hall the locker in front of the survivor is.
            if (station != null)
            {
                string Prompt() => gc.CurrentPrompt ?? "none";
                IEnumerator Stand(float px, float pz, float localYaw)
                {
                    Vec3 at = station.ToWorld(px, pz, 0);
                    at = new Vec3(at.X, Mathf.Max(at.Y, l.HeightAt(at.X, at.Z)) + 0.05f, at.Z); // outside, the ground may be above the floor
                    gc.Player.Teleport(at.ToUnity());
                    float yaw = station.Yaw + localYaw;
                    gc.Player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    gc.CameraRig.Look(yaw, 10f);
                    yield return Seconds(0.8f);
                }
                // Back to the station first, waiting for its ground and buildings to load.
                gc.TeleportTo(station.ToWorld(0f, 2.5f, 0).ToUnity(), "the station", false);
                yield return null;
                float back = Time.realtimeSinceStartup + P(60f);
                while (gc.Teleporting && Time.realtimeSinceStartup < back) yield return null;
                yield return Seconds(3f);
                yield return Stand(14.45f, -4.7f, -90f); // against the east wall outside, the office desk 1.4 m away behind it
                yield return Seconds(1f);
                string outside = Prompt();
                Line($"  outside the office: survivor at {Fmt(gc.Player.transform.position.ToWorld())}; {gc.DescribeNearbyTargets()}");
                yield return Capture("99i8_basin_outside_office");
                yield return Stand(5.6f, 3.9f, 0f);     // in the hall, facing a locker
                float until = Time.realtimeSinceStartup + P(3f);
                while (!Prompt().Contains("Locker") && Time.realtimeSinceStartup < until) yield return null;
                string inside = Prompt();
                Line($"  in the hall: survivor at {Fmt(gc.Player.transform.position.ToWorld())}; {gc.DescribeNearbyTargets()}");
                yield return Capture("99i9_basin_hall_locker");
                Check(!outside.Contains("Desk") && inside.Contains("Locker"),
                    $"Interaction needs a clear line of sight: outside the office wall the prompt is \"{outside}\"; facing the hall locker it is \"{inside}\"");
            }
        }

        /// <summary>The heading (degrees) with the most room in front of the survivor at chest height, for a look round a room.</summary>
        private static float OpenestView(GameController gc)
        {
            Vector3 eye = gc.Player.transform.position + Vector3.up * 1.3f;
            float best = 0f, heading = 0f;
            for (int a = 0; a < 24; a++)
            {
                float yaw = a * 15f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float d = Physics.Raycast(eye, dir, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 30f;
                if (d > best)
                {
                    best = d;
                    heading = yaw;
                }
            }
            return heading;
        }

        /// <summary>
        /// A waypoint at a distant town: the way there by road is planned in the background, drawn on the
        /// world map and the minimap, and the HUD names the next turn.
        /// </summary>
        private IEnumerator RoadRoute(GameController gc, WorldLayout l)
        {
            Vec3 me = gc.Player.transform.position.ToSim();
            PoiInfo town = l.Pois.Where(p => p.Kind == "town" || p.Kind == "village")
                .Where(p => Vec3.DistanceXZ(p.Center, me) > 8000f && Vec3.DistanceXZ(p.Center, me) < 30000f)
                .OrderBy(p => Vec3.DistanceXZ(p.Center, me)).FirstOrDefault();
            if (town == null)
            {
                Line("FAIL: no town 8–30 km from the survivor for a road route");
                yield break;
            }
            float started = Time.realtimeSinceStartup;
            Check(gc.Session.SetWaypoint(town.Center, town.Name).Ok, $"Waypoint set at {town.Name}, {Vec3.DistanceXZ(town.Center, me) / 1000f:0.0} km away");
            float wait = Time.realtimeSinceStartup + P(30f);
            while (gc.Session.Routes.Route == null && Time.realtimeSinceStartup < wait) yield return null;
            Afterfall.Domain.World.RoadRoute route = gc.Session.Routes.Route;
            float straight = Vec3.DistanceXZ(town.Center, me);
            Check(route != null && !route.Direct && route.Length >= straight && route.Length < straight * 2.2f && route.OffRoad < 3000f,
                route == null ? "FAIL: no route was planned" :
                $"The way to {town.Name} by road was planned in {Time.realtimeSinceStartup - started:0.0} s on a network of {gc.Session.Routes.Network.NodeCount} junctions and bends: {route.Length / 1000f:0.0} km (straight {straight / 1000f:0.0} km), {route.OffRoad:0} m off the road, {route.Turns.Count} turns; the HUD says \"{gc.Session.Routes.Guidance()}\"");
            if (route == null) yield break;

            gc.Ui.ToggleWorldMap();
            yield return Seconds(0.5f);
            if (gc.Ui.CurrentWindow is Afterfall.Presentation.UI.WorldMapWindow map)
            {
                gc.Maps.RevealAll = true;
                map.Focus((me.X + town.Center.X) * 0.5f, (me.Z + town.Center.Z) * 0.5f, straight / 500f);
                yield return Seconds(1f);
                Check(map.RoutePoints > 10, $"The world map draws the way ({map.RoutePoints} points)");
                yield return Capture("99r0_route_on_the_map");
                gc.Maps.RevealAll = false;
                gc.Ui.CloseWindow();
            }
            yield return Seconds(1f);
            yield return Capture("99r1_route_on_the_minimap");
            Check(gc.Session.ClearWaypoint().Ok, "Waypoint cleared");
            yield return null;
            yield return null;
            Check(gc.Session.Routes.Route == null, "With no waypoint the route is gone");
        }

        /// <summary>
        /// The city's real buildings: walk up to a tower in the Old Quarter, go in through its door, take
        /// the lift to a middle floor and to the top, and look round; then a house in the terraces.
        /// </summary>
        private IEnumerator CityTowers(GameController gc, WorldLayout l)
        {
            var blocks = new System.Collections.Generic.List<PropPlacement>();
            foreach ((int x, int z) in new[] { (78, 59), (77, 59), (78, 58) })
            {
                ChunkContent c = l.EnsureChunk(x, z);
                if (c != null) blocks.AddRange(c.Blocks);
            }
            PropPlacement towerBlock = blocks.Where(b => b.Kind == "city_tower").OrderByDescending(b => b.Size.Y).FirstOrDefault();
            if (towerBlock == null)
            {
                Line("FAIL: no tower in the Old Quarter");
                yield break;
            }
            PlacedPlan tower = BuildingPlans.PlaceCityBlock(towerBlock);
            BuildingPlan plan = tower.Plan;
            PlanFixture door = plan.Fixtures.First(f => f.Key.EndsWith("door:entrance", System.StringComparison.Ordinal));
            // Outside the front door, then through it.
            Vec3 outside = tower.ToWorld(door.X, door.Z + 3f, 0);
            gc.TeleportTo(new Vec3(outside.X, l.HeightAt(outside.X, outside.Z), outside.Z).ToUnity(), plan.Name, false);
            yield return null;
            float wait = Time.realtimeSinceStartup + P(60f);
            while (gc.Teleporting && Time.realtimeSinceStartup < wait) yield return null;
            wait = Time.realtimeSinceStartup + P(20f);
            while ((gc.CityBuildings.BuildingAt(tower.ToWorld(0f, 0f, 0).ToUnity() + Vector3.up * 0.3f, out _) == null || gc.CityBuildings.BuiltFloors < 1 || gc.NavBaking) && Time.realtimeSinceStartup < wait) yield return null;
            yield return Seconds(2f);
            gc.CameraRig.Look(tower.Yaw + 180f, 8f);
            yield return Seconds(1f);
            yield return Capture("99t0_city_tower_street");
            Check(gc.CityBuildings.Count >= 1 && gc.CityBuildings.BuiltFloors >= 1,
                $"{plan.Name}: {plan.Floors} floors, built for real near the survivor ({gc.CityBuildings.Count} buildings standing, {gc.CityBuildings.BuiltFloors} floors built)");

            // Open the door and walk in.
            Afterfall.Domain.Construction.Structure frontDoor = gc.Session.World.Structures.Values.FirstOrDefault(st => st.SeedKey == door.Key);
            Check(frontDoor != null && gc.Session.ToggleDoor(frontDoor.Id).Ok && frontDoor.DoorOpen, $"Opened the front door of {plan.Name}");
            yield return Seconds(1f);
            Vec3 hall = tower.ToWorld(door.X, door.Z - 2.5f, 0);
            gc.Player.SetScriptedMove(Vector2.up, false);
            gc.CameraRig.Look(tower.Yaw + 180f, 5f);
            float walk = Time.realtimeSinceStartup + P(8f);
            while (Time.realtimeSinceStartup < walk && Vector2.Distance(new Vector2(gc.Player.transform.position.ToWorld().x, gc.Player.transform.position.ToWorld().z), new Vector2(hall.X, hall.Z)) > 1.2f)
            {
                Vector3 to = new Vector3(hall.X, 0f, hall.Z).FromWorld() - gc.Player.transform.position;
                gc.CameraRig.Look(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 5f);
                yield return null;
            }
            gc.Player.SetScriptedMove(Vector2.zero, false);
            yield return Seconds(1.5f);
            PlacedPlan inBuilding = gc.CityBuildings.BuildingAt(gc.Player.transform.position + Vector3.up * 0.2f, out int floorNow);
            PlanRoom room = l.RoomAt(gc.Player.transform.position.ToSim() + new Vec3(0, 0.2f, 0), out _);
            bool onMesh = UnityEngine.AI.NavMesh.SamplePosition(gc.Player.transform.position, out UnityEngine.AI.NavMeshHit _, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
            Check(inBuilding != null && inBuilding.Plan.Id == plan.Id && floorNow == 0 && room != null,
                $"Walked in through the front door of {plan.Name}: on floor {floorNow}, in the {(room != null ? room.Name : "open")}; on the NavMesh {onMesh}");
            yield return Capture("99t1_city_tower_entrance");

            // The lift: a middle floor, then the top.
            int[] floors = { plan.Floors / 2, plan.Floors - 1 };
            int shot = 2;
            foreach (int target in floors)
            {
                gc.CityBuildings.TakeLift(plan.Id, target);
                wait = Time.realtimeSinceStartup + P(20f);
                int at = -1;
                while (Time.realtimeSinceStartup < wait)
                {
                    if (gc.CityBuildings.BuildingAt(gc.Player.transform.position + Vector3.up * 0.2f, out at) != null && at == target) break;
                    yield return null;
                }
                yield return Seconds(2.5f);
                Vector3 pos = gc.Player.transform.position;
                gc.CityBuildings.BuildingAt(pos + Vector3.up * 0.2f, out at);
                string prefix = plan.Id + ":f" + target + ":";
                int things = gc.Session.World.Structures.Values.Count(st => st.SeedKey.StartsWith(prefix, System.StringComparison.Ordinal));
                int planned = plan.Fixtures.Count(f => f.Floor == target);
                bool standing = UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit liftHit, 1.5f, UnityEngine.AI.NavMesh.AllAreas) || gc.Player.IsGrounded;
                // From the lift, a path along the floor to its corridor (or its first room).
                PlanRoom hallway = plan.Rooms.Where(x => x.Floor == target && x.Name.Contains("corridor")).FirstOrDefault()
                    ?? plan.Rooms.Where(x => x.Floor == target && !x.Name.Contains("stair hall")).FirstOrDefault();
                string path = "no room";
                if (hallway != null)
                {
                    Vec3 hw = tower.ToWorld((hallway.X0 + hallway.X1) * 0.5f, (hallway.Z0 + hallway.Z1) * 0.5f, target);
                    var navPath = new UnityEngine.AI.NavMeshPath();
                    bool end = UnityEngine.AI.NavMesh.SamplePosition(hw.ToUnity(), out UnityEngine.AI.NavMeshHit endHit, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
                    UnityEngine.AI.NavMesh.CalculatePath(liftHit.position, endHit.position, UnityEngine.AI.NavMesh.AllAreas, navPath);
                    path = $"{navPath.status} to the {hallway.Name.Substring(hallway.Name.IndexOf('·') + 1).Trim()}" + (end ? "" : " (not on the NavMesh)");
                }
                Check(at == target && things == planned && things > 0 && standing && path.StartsWith("PathComplete", System.StringComparison.Ordinal),
                    $"Took the lift to floor {target} of {plan.Floors - 1}: on floor {at} ({pos.ToSim().Y - tower.Origin.Y:0.0} m up), standing there {standing}, {things} of {planned} doors and furnishings in the world, path {path}; the floor holds {string.Join(", ", plan.Rooms.Where(x => x.Floor == target).Select(x => x.Name.Substring(x.Name.IndexOf('·') + 1).Trim()).Distinct().Take(5))}");
                // Into the biggest room of the floor to look round.
                PlanRoom big = plan.Rooms.Where(x => x.Floor == target && !x.Name.Contains("stair hall") && !x.Name.Contains("corridor"))
                    .OrderByDescending(x => System.Math.Abs((x.X1 - x.X0) * (x.Z1 - x.Z0))).FirstOrDefault();
                if (big != null)
                {
                    float bx = big.X0 + (big.X1 - big.X0) * 0.3f, bz = big.Z0 + (big.Z1 - big.Z0) * 0.3f;
                    Vec3 bw = tower.ToWorld(bx, bz, target);
                    gc.Player.Teleport(new Vector3(bw.X, bw.Y + 0.05f, bw.Z).FromWorld());
                    yield return Seconds(1f);
                    gc.CameraRig.Look(OpenestView(gc), 14f);
                    yield return Seconds(1.5f);
                }
                yield return Capture($"99t{shot++}_city_tower_floor_{target}");
            }
            gc.CityBuildings.ResetTimings();
            gc.SlowestNavStartMs = 0f;
            // Down to the middle again and measure while the floors there are built.
            gc.CityBuildings.TakeLift(plan.Id, plan.Floors / 3);
            yield return MeasureFrames("inside a tower (lift down and its floors built)", 3f);
            Line($"  slowest floor build: {gc.CityBuildings.SlowestFloor}; slowest NavMesh rebake start {gc.SlowestNavStartMs:0.0} ms");
            yield return MeasureFrames("inside a tower (settled)", 3f);

            // A house in the terraces west of the centre.
            PropPlacement house = null;
            for (int z = 50; z <= 70 && house == null; z++)
                for (int x = 50; x <= 80 && house == null; x++)
                    house = l.GetChunk(x, z)?.Blocks.FirstOrDefault(b => b.Kind == "city_terrace" || b.Kind == "city_semi");
            Check(house != null, "Found a terrace or a semi among the city's chunks");
            if (house != null)
            {
                PlacedPlan home = BuildingPlans.PlaceCityBlock(house);
                Vec3 inside = home.ToWorld(home.Plan.Fixtures.First(f => f.Key.EndsWith("door:entrance", System.StringComparison.Ordinal)).X, home.Plan.Depth * 0.5f - 2.2f, 0);
                gc.TeleportTo(inside.ToUnity(), home.Plan.Name, false);
                yield return null;
                wait = Time.realtimeSinceStartup + P(60f);
                while (gc.Teleporting && Time.realtimeSinceStartup < wait) yield return null;
                wait = Time.realtimeSinceStartup + P(20f);
                while (gc.CityBuildings.BuildingAt(gc.Player.transform.position + Vector3.up * 0.2f, out _) == null && Time.realtimeSinceStartup < wait) yield return null;
                yield return Seconds(3f);
                PlacedPlan inHome = gc.CityBuildings.BuildingAt(gc.Player.transform.position + Vector3.up * 0.2f, out int hf);
                PlanRoom living = home.Plan.Rooms.Where(x => x.Floor == 0 && !x.Name.Contains("hall")).OrderByDescending(x => System.Math.Abs((x.X1 - x.X0) * (x.Z1 - x.Z0))).FirstOrDefault();
                if (living != null)
                {
                    Vec3 lw = home.ToWorld(living.X0 + (living.X1 - living.X0) * 0.25f, (living.Z0 + living.Z1) * 0.5f, 0);
                    gc.Player.Teleport(new Vector3(lw.X, lw.Y + 0.05f, lw.Z).FromWorld());
                    yield return Seconds(1f);
                    gc.CameraRig.Look(OpenestView(gc), 14f);
                }
                yield return Seconds(1.5f);
                Check(inHome != null && inHome.Plan.Id == home.Plan.Id,
                    $"Inside {home.Plan.Name} ({home.Plan.Floors} floors: {string.Join(", ", home.Plan.Rooms.Select(x => x.Name.Substring(x.Name.IndexOf('·') + 1).Trim()).Distinct().Take(6))})");
                yield return Capture("99t9_city_house");
            }
        }
    }
}
