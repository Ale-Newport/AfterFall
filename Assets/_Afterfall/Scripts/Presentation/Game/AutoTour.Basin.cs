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
    }
}
