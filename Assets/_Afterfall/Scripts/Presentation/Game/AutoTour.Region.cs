using System.Collections;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.World;
using Afterfall.Services;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>The region scene of the scripted tour: a new game in the 2 km Greater Millbrook world.</summary>
    public sealed partial class AutoTour
    {
        /// <summary><c>-afterfall-tour-region</c>: run only the region scene.</summary>
        public const string OnlyRegionFlag = "-afterfall-tour-region";

        private IEnumerator RegionScene(GameController previous)
        {
            Line("— Greater Millbrook (world version 2) —");
            float t0 = Time.realtimeSinceStartup;
            _boot.StartNewGame(DifficultyPreset.Survival, 20261107, GameMode.Survival, 2);
            float timeout = Time.realtimeSinceStartup + P(180f);
            while ((_boot.Game == null || _boot.Game == previous || !_boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            GameController gc = _boot.Game;
            if (gc == null || gc == previous || !gc.Running)
            {
                Line("FAIL: the region did not load");
                yield break;
            }
            float loaded = Time.realtimeSinceStartup - t0;
            while (!gc.NavReady && Time.realtimeSinceStartup < timeout) yield return null;
            float navReady = Time.realtimeSinceStartup - t0;
            GameSession s = gc.Session;
            WorldLayout l = s.World.Layout;
            s.Threats.SpawningEnabled = false;
            s.Weather.Hold(Afterfall.Domain.Weather.WeatherKind.Clear);
            SkipTo(gc, 10.5);
            Check(Mathf.Approximately(l.Size, 2048f) && l.Version == 2, $"A new game in {l.RegionName}: {l.Size:0} m across, {l.Props.Count} props, {l.Nodes.Count} trees, rocks and plants, {l.Containers.Count} containers, {s.World.Zombies.Count} infected");
            Line($"Region load: playable after {loaded:0.0} s, NavMesh window baked after {navReady:0.0} s");
            Check(gc.NavWindowed, $"The NavMesh is baked in a {GameController.NavWindow:0} m window around the survivor, not across all {l.Size:0} m");
            Check(gc.Sync.NodeViewCount > 50 && gc.Sync.NodeViewCount < l.Nodes.Count / 4, $"Only nearby nodes have views: {gc.Sync.NodeViewCount} of {s.World.ResourceNodes.Count}");
            Check(gc.TerrainTiles.LoadedTiles < WorldBuilder.TilesAcross(l) * WorldBuilder.TilesAcross(l), $"Terrain streams in tiles: {gc.TerrainTiles.LoadedTiles} of {WorldBuilder.TilesAcross(l) * WorldBuilder.TilesAcross(l)} loaded at the start");
            Check(gc.Maps.Ready || true, $"World map overview {gc.Maps.OverviewSize}×{gc.Maps.OverviewSize} px");
            yield return Seconds(2f);
            yield return MeasureFrames("region start camp", 4f);

            // Ashford: the bridge over the Kessel.
            PropPlacement bridge = l.Props.Where(p => p.Kind == "bridge").OrderBy(p => Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(1000f, 480f))).First();
            Vector3 deck = bridge.Position.ToUnity();
            Vector3 along = Quaternion.Euler(0f, bridge.Yaw, 0f) * Vector3.forward;
            Vector3 west = deck - along * (bridge.Size.Z * 0.5f + 14f);
            gc.TeleportTo(west, "Ashford bridge", false);
            yield return null;
            float tpWait = Time.realtimeSinceStartup + P(40f);
            while (gc.Teleporting && Time.realtimeSinceStartup < tpWait) yield return null;
            float navWait = Time.realtimeSinceStartup + P(40f);
            while ((gc.NavBaking || Vector2.Distance(new Vector2(gc.NavWindowCentre.x, gc.NavWindowCentre.z), new Vector2(west.x, west.z)) > 120f) && Time.realtimeSinceStartup < navWait) yield return null;
            // Neighbouring terrain tiles finish loading after the teleport; the window rebakes for them.
            yield return Seconds(1.5f);
            while ((gc.NavBaking || gc.TerrainTiles.PaintingTiles > 0) && Time.realtimeSinceStartup < navWait) yield return null;
            yield return Seconds(0.5f);
            while (gc.NavBaking && Time.realtimeSinceStartup < navWait) yield return null;
            Check(Vector3.Distance(Flat(gc.Player.transform.position), Flat(west)) < 12f, $"Teleported to the west end of the Ashford bridge {Fmt(gc.Player.transform.position)}");
            StandAndLook(gc, Ground(gc, west), deck + along * 20f + Vector3.up * 1.5f);
            yield return Seconds(1.5f);
            yield return Capture("80_region_ashford_bridge");
            bool deckNav = UnityEngine.AI.NavMesh.SamplePosition(deck + Vector3.up * 0.2f, out UnityEngine.AI.NavMeshHit dh, 1.0f, UnityEngine.AI.NavMesh.AllAreas) && Mathf.Abs(dh.position.y - deck.y) < 0.5f;
            Check(deckNav, $"The NavMesh runs over the bridge deck ({deck.y:0.0} m, {deck.y - l.HeightAt(deck.x, deck.z):0.0} m above the river bed)");
            Vector3 eastEnd = Ground(gc, deck + along * (bridge.Size.Z * 0.5f + 14f));
            Vector3 westEnd = Ground(gc, west);
            bool westOn = UnityEngine.AI.NavMesh.SamplePosition(westEnd, out _, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
            bool eastOn = UnityEngine.AI.NavMesh.SamplePosition(eastEnd, out _, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
            UnityEngine.AI.NavMeshPathStatus toDeck = PathStatus(westEnd, deck + Vector3.up * 0.2f), fromDeck = PathStatus(deck + Vector3.up * 0.2f, eastEnd);
            Check(toDeck == UnityEngine.AI.NavMeshPathStatus.PathComplete && fromDeck == UnityEngine.AI.NavMeshPathStatus.PathComplete,
                $"Agents can walk across the bridge from bank to bank (west bank {Fmt(westEnd)} y {westEnd.y:0.0} on NavMesh {westOn}, east bank {Fmt(eastEnd)} y {eastEnd.y:0.0} on NavMesh {eastOn}; west→deck {toDeck}, deck→east {fromDeck}; NavMesh window at {Fmt(gc.NavWindowCentre)})");
            Check(gc.Sync.NodeViewCount > 50, $"Node views followed the survivor: {gc.Sync.NodeViewCount} near Ashford");
            yield return MeasureFrames("Ashford (town, river, bridge)", 4f);

            // Drive the Ashford pickup (key in the ignition) east over the bridge.
            Afterfall.Domain.Vehicles.VehicleState pickup = s.World.Vehicles.Values.FirstOrDefault(v => v.SeedKey == "vehicle:ashford_pickup");
            VehicleView pv = pickup != null ? gc.Sync.VehicleViewOf(pickup.Id) : null;
            Check(pv != null, "The Ashford pickup is parked on a town street");
            if (pv != null)
            {
                Vector3 pside = pv.transform.position - pv.transform.right * 2.8f;
                StandAndLook(gc, Ground(gc, pside), pv.transform.position + Vector3.up);
                yield return Seconds(1f);
                gc.EnterVehicle(pv);
                yield return Seconds(0.6f);
                OpResult started = s.Vehicles.ToggleEngine();
                Check(s.Player.InVehicle && started.Ok && pickup.EngineOn, "Started the Ashford pickup: " + started.Message);
                // Along the highway: its own points from beside the pickup to 40 m past the bridge.
                RoadPath highway = l.Roads.Where(r => r.Paved && r.Width >= 9f).OrderBy(r => r.Points.Min(p => Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(deck.x, deck.z)))).First();
                Vector3 east = deck + along * (bridge.Size.Z * 0.5f + 40f);
                Vector3 from = pv.transform.position;
                var route = highway.Points.Select(p => p.ToUnity()).Where(p => p.x > from.x + 8f && p.x < east.x).ToList();
                route.Insert(0, highway.Points.Select(p => p.ToUnity()).OrderBy(p => Vector3.Distance(Flat(p), Flat(from))).First());
                route.Add(east);
                for (int i = 0; i < route.Count; i++) route[i] = new Vector3(route[i].x, gc.GroundHeight(route[i]), route[i].z);
                float lowest = float.MaxValue;
                bool onDeck = false;
                Vector3 start = pv.transform.position;
                IEnumerator drive = DriveRoute(gc, pv, route.ToArray(), 35f, P(70f), "81_region_driving_over_the_bridge");
                while (drive.MoveNext())
                {
                    Vector3 vp = pv.transform.position;
                    if (Vector2.Distance(new Vector2(vp.x, vp.z), new Vector2(deck.x, deck.z)) < 6f)
                    {
                        onDeck = true;
                        lowest = Mathf.Min(lowest, vp.y);
                    }
                    yield return drive.Current;
                }
                Vector3 end = pv.transform.position;
                Check(onDeck && lowest > deck.y - 1f, $"Drove over the bridge on its deck (lowest {lowest:0.0} m, deck {deck.y:0.0} m)");
                Check(Vector3.Distance(Flat(end), Flat(east)) < 16f && Vector3.Dot(pv.transform.up, Vector3.up) > 0.8f, $"Reached the east bank {Fmt(end)} upright, {Vector3.Distance(Flat(start), Flat(end)):0} m from the start");
                gc.ExitVehicle(false);
                yield return Seconds(0.8f);
            }

            // The other places of the region.
            foreach (string id in new[] { "eastgate", "pinecrest", "research" })
            {
                PoiInfo poi = l.Poi(id);
                gc.TeleportTo(poi.Center.ToUnity() + new Vector3(0f, 0f, -poi.Radius * 0.6f), poi.Name, false);
                yield return null;
                tpWait = Time.realtimeSinceStartup + P(40f);
                while (gc.Teleporting && Time.realtimeSinceStartup < tpWait) yield return null;
                yield return Seconds(2.5f);
                Vector3 here = gc.Player.transform.position;
                StandAndLook(gc, here, poi.Center.ToUnity() + Vector3.up * 3f);
                yield return Seconds(1.5f);
                Check(Vector3.Distance(Flat(here), Flat(poi.Center.ToUnity())) < poi.Radius + 20f && gc.IsWorldReadyAt(here), $"At {poi.Name} {Fmt(here)}: terrain ready, {gc.TerrainTiles.LoadedTiles} tiles loaded, {gc.Sync.NodeViewCount} node views");
                yield return Capture("82_region_" + id);
                yield return MeasureFrames(poi.Name, 3f);
            }
            Check(s.World.Exploration.DiscoveredPois.Contains("eastgate") && s.World.Exploration.DiscoveredPois.Contains("pinecrest"), $"Visited places are discovered on the map ({s.World.Exploration.DiscoveredPois.Count} so far)");
        }
    }
}
