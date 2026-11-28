using System;
using System.Collections;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Presentation.UI;
using Afterfall.Services;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>The world-expansion scenes of the scripted tour: creative/debug mode, flight and teleports.</summary>
    public sealed partial class AutoTour
    {
        /// <summary><c>-afterfall-tour-expansion</c>: run only the expansion scenes after the new game loads.</summary>
        public const string OnlyExpansionFlag = "-afterfall-tour-expansion";

        private IEnumerator ExpansionScenes(GameController gc)
        {
            gc.Session.Threats.SpawningEnabled = false;
            gc.Session.Weather.Hold(Afterfall.Domain.Weather.WeatherKind.Clear);
            SkipTo(gc, 11.0);
            yield return MapScene(gc);
            yield return ApartmentScene(gc);
            yield return VehicleScene(gc);
            // The vehicle scene reloads the game: carry on in the reloaded one.
            if (_boot.Game != null && _boot.Game.Running) gc = _boot.Game;
            gc.Session.Threats.SpawningEnabled = false;
            yield return CreativeScene(gc);
            yield return RegionScene(gc);
        }

        private IEnumerator MapScene(GameController gc)
        {
            GameSession s = gc.Session;
            WorldState w = s.World;
            Line("— World map and minimap —");
            float wait = Time.realtimeSinceStartup + P(20f);
            while (!gc.Maps.Ready && Time.realtimeSinceStartup < wait) yield return null;
            Check(gc.Maps.Ready, $"The map was drawn from the world layout ({gc.Maps.OverviewSize}×{gc.Maps.OverviewSize} px for {w.Layout.Size:0} m)");
            gc.Ui.ToggleWorldMap();
            yield return Seconds(0.5f);
            var map = gc.Ui.CurrentWindow as WorldMapWindow;
            if (map == null)
            {
                Line("FAIL: M did not open the world map");
                yield break;
            }
            map.Focus(w.Layout.Size * 0.5f, w.Layout.Size * 0.5f, 1e6f);
            yield return Seconds(0.5f);
            var symbols = new System.Collections.Generic.List<MapSymbol>();
            MapSymbols.Collect(gc, false, symbols);
            int pois = symbols.Count(x => x.Kind == MapSymbolKind.Poi);
            int discovered = w.Layout.Pois.Count(p => w.Exploration.DiscoveredPois.Contains(p.Id));
            Check(pois == discovered && pois < w.Layout.Pois.Count, $"Survival shows only discovered places on the map ({pois} of {w.Layout.Pois.Count}); unexplored land is under fog ({w.Exploration.RevealedFraction * 100f:0}% explored)");
            yield return Capture("40_world_map");

            PoiInfo target = w.Layout.Poi("workshop");
            OpResult set = map.SetWaypointAt(target.Center.X, target.Center.Z);
            Check(set.Ok && w.Navigation.HasWaypoint && Vector2.Distance(new Vector2(w.Navigation.Waypoint.X, w.Navigation.Waypoint.Z), new Vector2(target.Center.X, target.Center.Z)) < 0.5f,
                $"Clicked the map to set a waypoint at the distant workshop: {set.Message}");
            map.PlaceMarker(260f, 300f);
            Check(w.Navigation.Markers.Any(m => m.Kind == MarkerKind.Custom), "Right-click placed a custom marker");
            Vector3 me = gc.Player.transform.position;
            map.Focus(me.x, me.z, 0.35f);
            yield return Seconds(0.5f);
            map.View.WorldToScreen(me.x, me.z, out float sx, out float sy);
            Check(Mathf.Abs(sx - map.View.Width * 0.5f) < 1f && Mathf.Abs(sy - map.View.Height * 0.5f) < 1f, $"The player marker sits at the survivor's position on the map ({sx:0}, {sy:0} in a {map.View.Width:0}×{map.View.Height:0} view)");
            yield return Capture("41_world_map_zoomed");
            gc.Ui.CloseWindow();

            // Face the waypoint and walk toward it: the minimap, its distance and the on-screen marker follow.
            Vector3 to = w.Navigation.Waypoint.ToUnity() - gc.Player.transform.position;
            gc.CameraRig.Look(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 6f);
            yield return Seconds(0.6f);
            Check(gc.Ui.Hud != null && gc.Ui.Hud.Waypoint.OnScreen, "Facing the waypoint, its marker is drawn over it on screen");
            float before = Vec3.DistanceXZ(s.Player.Position, w.Navigation.Waypoint);
            gc.Player.SetScriptedMove(Vector2.up, false);
            yield return Seconds(5f);
            gc.Player.SetScriptedMove(Vector2.zero, false);
            float after = Vec3.DistanceXZ(s.Player.Position, w.Navigation.Waypoint);
            Check(after < before - 12f, $"Walking toward the waypoint: {before:0} m → {after:0} m");
            yield return Capture("42_minimap_and_waypoint");
            gc.CameraRig.Look(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 150f, 6f);
            yield return Seconds(0.6f);
            Check(!gc.Ui.Hud.Waypoint.OnScreen, "Facing away, the waypoint marker is pinned to the screen edge");
            gc.Settings.MinimapRotate = true;
            yield return Seconds(0.5f);
            yield return Capture("43_minimap_rotating");
            gc.Settings.MinimapRotate = false;
        }

        /// <summary>
        /// <c>-afterfall-tour-perf</c>: frame time at the start camp with parts of the expansion switched
        /// off one at a time, to attribute a frame-time change.
        /// </summary>
        private IEnumerator PerfAttribution(GameController gc)
        {
            yield return Seconds(3f);
            int skinned = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Length;
            Line($"Perf: {gc.Sync.Zombies.Count} infected views, {gc.Sync.Colonists.Count} survivor views, {skinned} skinned renderers");
            yield return MeasureFrames("start camp (all on)", 4f);
            var zombies = new System.Collections.Generic.List<GameObject>();
            foreach (var z in gc.Sync.Zombies.Values)
                if (z != null && z.gameObject.activeSelf)
                {
                    z.gameObject.SetActive(false);
                    zombies.Add(z.gameObject);
                }
            yield return MeasureFrames($"start camp ({zombies.Count} infected views off)", 4f);
            foreach (GameObject z in zombies) z.SetActive(true);
            foreach (SkinnedMeshRenderer r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            yield return MeasureFrames("start camp (skinned shadows off)", 4f);
            foreach (SkinnedMeshRenderer r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            gc.Ui.Hud.Minimap.Suspended = true;
            yield return MeasureFrames("start camp (minimap off)", 4f);
            GameObject apt = GameObject.Find("Prop_apartment");
            if (apt != null) apt.SetActive(false);
            yield return MeasureFrames("start camp (apartment shell off)", 4f);
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (StructureView v in FindObjectsByType<StructureView>(FindObjectsSortMode.None))
            {
                if (!gc.Session.World.Structures.TryGetValue(v.Id, out Structure st) || string.IsNullOrEmpty(st.SeedKey)) continue;
                v.gameObject.SetActive(false);
                hidden.Add(v.gameObject);
            }
            yield return MeasureFrames($"start camp (also {hidden.Count} apartment fixtures off)", 4f);
            foreach (GameObject h in hidden) h.SetActive(true);
            if (apt != null) apt.SetActive(true);
            gc.Ui.Hud.Minimap.Suspended = false;
            yield return MeasureFrames("start camp (all on again)", 4f);
        }

        // ================================================================== vehicles

        private IEnumerator VehicleScene(GameController gc)
        {
            GameSession s = gc.Session;
            WorldState w = s.World;
            Line("— Vehicles —");
            Afterfall.Domain.Vehicles.VehicleState sedan = w.Vehicles.Values.FirstOrDefault(v => v.SeedKey == "vehicle:millbrook_sedan");
            if (sedan == null)
            {
                Line("FAIL: the abandoned sedan is missing");
                yield break;
            }
            VehicleView view = gc.Sync.VehicleViewOf(sedan.Id);
            Check(view != null, $"The sedan stands at {Fmt(sedan.Position.ToUnity())}: {Afterfall.Domain.Vehicles.VehicleRules.Summary(sedan, view?.Def)}");
            Vector3 side = view.transform.position - view.transform.right * 2.6f;
            StandAndLook(gc, Ground(gc, side), view.transform.position + Vector3.up * 0.8f);
            yield return Seconds(1f);
            yield return Capture("60_sedan_locked");
            OpResult locked = s.Vehicles.Enter(sedan.Id);
            Check(!locked.Ok && !s.Player.InVehicle, "The sedan is locked without its key: " + locked.Message);

            // Its key hangs in flat 2's kitchen.
            Structure kitchen = w.Structures.Values.FirstOrDefault(x => x.SeedKey == BuildingPlans.MillbrookApartmentsId + ":flat2:kitchen");
            StandAndLook(gc, StandSpot(gc, kitchen), kitchen.Position.ToUnity());
            yield return null;
            OpResult took = s.TakeAll(kitchen.Storage.Id);
            Check(took.Ok && s.Player.Inventory.HasTagged(Afterfall.Domain.Vehicles.VehicleRules.KeyItem, sedan.LockId), $"Found the key to {sedan.LockId} in flat 2's kitchen");

            StandAndLook(gc, Ground(gc, side), view.transform.position + Vector3.up * 0.8f);
            yield return null;
            gc.EnterVehicle(view);
            yield return Seconds(1.2f);
            Check(s.Player.InVehicle && gc.Player.Vehicle == view && gc.Player.Rig.Seated, "Unlocked the sedan with its key and got in: the survivor sits at the wheel");
            yield return Capture("61_in_the_sedan");
            OpResult start = s.Vehicles.ToggleEngine();
            Check(start.Ok && sedan.EngineOn, "Started the engine: " + start.Message);

            float fuel0 = sedan.Fuel;
            Vector3 from = view.transform.position;
            var route = new[] { new Vector3(172f, 0f, 286f), new Vector3(186f, 0f, 262f), new Vector3(205f, 0f, 263f), new Vector3(262f, 0f, 257f), new Vector3(279f, 0f, 252f) };
            yield return DriveRoute(gc, view, route, 38f, P(60f), "62_driving_on_the_road");
            float driven = Vector3.Distance(from, view.transform.position);
            Check(driven > 100f && Vector3.Distance(Flat(view.transform.position), Flat(route[route.Length - 1])) < 14f,
                $"Drove {driven:0} m (odometer {sedan.OdometerMetres:0} m) along the road toward the gas station; fuel {fuel0:0.00} → {sedan.Fuel:0.00} L; upright {Vector3.Dot(view.transform.up, Vector3.up):0.00}");
            Check(sedan.Fuel < fuel0 && sedan.OdometerMetres > 90.0, "Driving burned fuel and ran up the odometer");
            Check(Vector3.Distance(s.Player.Position.ToUnity(), view.transform.position) < 3f, "The survivor rode along with the car");
            Check(view.EngineAudible && !view.HeadlightsOn, $"The engine is heard while it runs; by day the headlights stay off");
            SkipTo(gc, 22.5);
            yield return Seconds(1f);
            Check(view.HeadlightsOn, "At night the running car's headlights and tail lights are on");
            view.Lights = VehicleView.LightMode.Off;
            yield return null;
            yield return null;
            Check(!view.HeadlightsOn, "The light switch turns them off");
            view.Lights = VehicleView.LightMode.Auto;
            yield return null;
            yield return Capture("62b_headlights_at_night");
            SkipTo(gc, 11.0);
            yield return Seconds(0.5f);

            gc.ExitVehicle();
            yield return Seconds(1f);
            Vector3 feet = gc.Player.transform.position;
            Check(!s.Player.InVehicle && gc.Player.Vehicle == null && !gc.Player.Rig.Seated && SafeGround.Fits(feet + Vector3.up * 0.02f) && Vector3.Distance(feet, view.transform.position) < 5f,
                $"Parked and got out beside the car at {Fmt(feet)}; the engine is {(sedan.EngineOn ? "still on" : "off")}");
            Check(s.Vehicles.SetLocked(sedan.Id, true).Ok && sedan.Locked, "Locked the sedan with its key");
            yield return Seconds(0.5f);
            yield return Capture("63_parked_at_the_gas_station");

            // Save, reload, and find it where it was left.
            Vector3 parked = view.transform.position;
            float parkedFuel = sedan.Fuel;
            gc.Save("tour_vehicle");
            _boot.LoadGame("tour_vehicle");
            float loadTimeout = Time.realtimeSinceStartup + P(120f);
            while ((_boot.Game == null || _boot.Game == gc || !_boot.Game.Running) && Time.realtimeSinceStartup < loadTimeout) yield return null;
            _boot.Saves.Store.Delete("tour_vehicle");
            GameController g2 = _boot.Game;
            if (g2 == null || g2 == gc || !g2.Running)
            {
                Line("FAIL: the reload after parking did not complete");
                yield break;
            }
            g2.Session.Threats.SpawningEnabled = false;
            g2.Session.Weather.Hold(Afterfall.Domain.Weather.WeatherKind.Clear);
            yield return Seconds(1.5f);
            Afterfall.Domain.Vehicles.VehicleState again = g2.Session.World.Vehicles[sedan.Id];
            VehicleView view2 = g2.Sync.VehicleViewOf(sedan.Id);
            Check(view2 != null && Vector3.Distance(Flat(view2.transform.position), Flat(parked)) < 1.0f && Mathf.Abs(again.Fuel - parkedFuel) < 0.01f && again.Locked
                  && g2.Session.Player.Inventory.HasTagged(Afterfall.Domain.Vehicles.VehicleRules.KeyItem, again.LockId),
                $"After a reload the sedan is where it was parked ({Fmt(view2 != null ? view2.transform.position : Vector3.zero)}), with {again.Fuel:0.00} L, locked, and the key in the backpack");

            // The pickup at the workshop has no key: hotwire it.
            gc = g2;
            s = gc.Session;
            w = s.World;
            Afterfall.Domain.Vehicles.VehicleState pickup = w.Vehicles.Values.First(v => v.SeedKey == "vehicle:hartley_pickup");
            VehicleView pv = gc.Sync.VehicleViewOf(pickup.Id);
            Vector3 pside = pv.transform.position - pv.transform.right * 2.8f;
            gc.TeleportTo(pside, "the workshop pickup", false);
            yield return Seconds(1f);
            StandAndLook(gc, Ground(gc, pside), pv.transform.position + Vector3.up);
            yield return null;
            gc.EnterVehicle(pv);
            yield return Seconds(0.6f);
            OpResult noKey = s.Vehicles.ToggleEngine();
            Check(s.Player.InVehicle && !noKey.Ok, "Got into the unlocked pickup; without a key it will not start: " + noKey.Message);
            Grant(s.Player.Inventory, "screwdriver", 1);
            Grant(s.Player.Inventory, "copper_wire", 12);
            int tries = 0;
            OpResult wired = OpResult.Fail("", "");
            while (!pickup.Hotwired && tries < 12)
            {
                tries++;
                wired = s.Vehicles.Hotwire();
            }
            Check(pickup.Hotwired, $"Hotwired the pickup after {tries} attempt(s): {wired.Message}");
            Check(s.Vehicles.ToggleEngine().Ok && pickup.EngineOn, "The hotwired pickup starts without a key");
            yield return Capture("64_hotwired_pickup");
            // Run down an infected standing in the yard.
            Vector3 ahead = pv.transform.position + pv.transform.forward * 22f;
            ahead.y = gc.GroundHeight(ahead);
            var zombie = s.Zombies.Spawn(ahead.ToSim(), "wanderer", s.Rng);
            yield return null;
            Vector3 p0 = pv.transform.position;
            yield return DriveRoute(gc, pv, new[] { ahead + pv.transform.forward * 12f }, 45f, P(12f), null);
            Check(zombie.IsDead, $"Drove the pickup into an infected and ran it down (pickup body {pickup.Body:0}%, drove {Vector3.Distance(p0, pv.transform.position):0} m)");
            if (!zombie.IsDead) LogZombie(gc, zombie);
            foreach (var x in w.Zombies.Values.ToList()) w.Zombies.Remove(x.Id);
            w.MarkChanged();
            gc.ExitVehicle(true);
            yield return Seconds(0.5f);

            // The farm's quad bike still has its key in the ignition, and almost no fuel.
            Afterfall.Domain.Vehicles.VehicleState atv = w.Vehicles.Values.First(v => v.SeedKey == "vehicle:dawson_atv");
            VehicleView av = gc.Sync.VehicleViewOf(atv.Id);
            Vector3 aside = av.transform.position - av.transform.right * 2.2f;
            gc.TeleportTo(aside, "the farm quad bike", false);
            yield return Seconds(1f);
            StandAndLook(gc, Ground(gc, aside), av.transform.position + Vector3.up);
            yield return null;
            gc.EnterVehicle(av);
            yield return Seconds(0.5f);
            Check(s.Player.InVehicle && s.Vehicles.ToggleEngine().Ok && atv.EngineOn, "The quad bike starts with the key left in its ignition");
            Vector3 a0 = av.transform.position;
            yield return DriveRoute(gc, av, new[] { av.transform.position + av.transform.forward * 25f }, 25f, P(10f), "65_quad_bike");
            Check(Vector3.Distance(a0, av.transform.position) > 12f, $"Rode the quad bike {Vector3.Distance(a0, av.transform.position):0} m (fuel {atv.Fuel:0.00} L left)");
            gc.ExitVehicle(true);
            yield return Seconds(0.5f);
        }

        /// <summary>Drives a vehicle through <paramref name="points"/> (steering toward each, holding a speed), then brakes to a stop.</summary>
        private IEnumerator DriveRoute(GameController gc, VehicleView v, Vector3[] points, float maxKmh, float timeout, string capture)
        {
            int idx = 0;
            float end = Time.realtimeSinceStartup + timeout;
            bool captured = capture == null;
            float start = Time.realtimeSinceStartup;
            while (idx < points.Length && Time.realtimeSinceStartup < end && gc.Player.Vehicle == v)
            {
                Vector3 to = points[idx] - v.transform.position;
                to.y = 0f;
                if (to.magnitude < 7f)
                {
                    idx++;
                    continue;
                }
                Vector3 fwd = v.transform.forward;
                fwd.y = 0f;
                float angle = Vector3.SignedAngle(fwd, to, Vector3.up);
                float kmh = v.Speed * 3.6f;
                float throttle = kmh < maxKmh ? 0.75f : 0f;
                if (Mathf.Abs(angle) > 45f && kmh > 18f) throttle = -0.4f;
                gc.ScriptedDriving = new Vector3(Mathf.Clamp(angle / 28f, -1f, 1f), throttle, 0f);
                if (!captured && Time.realtimeSinceStartup - start > 1.5f)
                {
                    captured = true;
                    yield return Capture(capture);
                }
                yield return null;
            }
            float stop = Time.realtimeSinceStartup + P(8f);
            while (v.Speed > 0.4f && Time.realtimeSinceStartup < stop)
            {
                gc.ScriptedDriving = new Vector3(0f, -1f, 1f);
                yield return null;
            }
            gc.ScriptedDriving = null;
        }

        // ================================================================== multi-storey building

        private IEnumerator ApartmentScene(GameController gc)
        {
            GameSession s = gc.Session;
            WorldState w = s.World;
            Line("— Millbrook Apartments (three floors, doors, keys) —");
            PlacedPlan apt = w.Layout.Plans.FirstOrDefault();
            if (apt == null)
            {
                Line("FAIL: no planned building in the layout");
                yield break;
            }
            BuildingPlan plan = apt.Plan;
            Structure Fixture(string key) => w.Structures.Values.FirstOrDefault(x => x.SeedKey == plan.Id + ":" + key);
            Vector3 At(float x, float z, int floor) => apt.ToWorld(x, z, floor).ToUnity();
            float Facing(float dx, float dz) => apt.Yaw + Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            Check(w.Structures.Values.Count(x => x.SeedKey.StartsWith(plan.Id, StringComparison.Ordinal)) == plan.Fixtures.Count, $"The building's {plan.Fixtures.Count} doors and furniture exist as world structures");

            // Outside, in front of the entrance.
            Vector3 front = At(-3f, plan.Depth * 0.5f + 4f, 0);
            front.y = gc.GroundHeight(front) + 0.05f;
            StandAndLook(gc, front, At(0f, 0f, 1) + Vector3.up * 2f);
            gc.CameraRig.Look(Facing(0f, -1f), -8f);
            yield return Seconds(1.5f);
            yield return Capture("50_apartments_exterior");

            Structure entrance = Fixture("door:entrance");
            StructureView entranceView = gc.Sync.StructureViewOf(entrance.Id);
            Check(entranceView != null && entranceView.DoorBlocksNavigation, "The closed entrance door blocks the NavMesh");
            Vector3 inside0 = At(-3f, 3f, 0), upstairs2 = At(-3f, 3f, 2);
            Check(PathStatus(front + Vector3.up * 0.3f, inside0) != UnityEngine.AI.NavMeshPathStatus.PathComplete, "Agents cannot walk in through the closed entrance");
            StandAndLook(gc, At(-3f, plan.Depth * 0.5f + 1.2f, 0), entrance.Position.ToUnity());
            yield return null;
            OpResult open = s.ToggleDoor(entrance.Id);
            yield return Seconds(1f);
            Check(open.Ok && entrance.DoorOpen && !entranceView.DoorBlocksNavigation, "Opened the entrance: " + open.Message);
            UnityEngine.AI.NavMeshPathStatus toSecond = PathStatus(front + Vector3.up * 0.3f, upstairs2);
            Check(toSecond == UnityEngine.AI.NavMeshPathStatus.PathComplete, $"With the entrance open, agents can walk from the street up both flights to the second floor ({toSecond})");
            if (toSecond != UnityEngine.AI.NavMeshPathStatus.PathComplete)
            {
                // Where does the walkable network stop?
                var probes = new (string, Vector3)[]
                {
                    ("hall, floor 0", At(-3f, 3f, 0)), ("foot of flight 0", At(-6.9f, -4.9f, 0)), ("middle of flight 0", At(-6.9f, -1.4f, 0) + Vector3.up * 1.6f),
                    ("landing, floor 1", At(-6.9f, 2.2f, 1)), ("corridor, floor 1", At(-3f, 3f, 1)), ("foot of flight 1", At(-5.4f, 2.1f, 1)),
                    ("middle of flight 1", At(-5.4f, -1.4f, 1) + Vector3.up * 1.6f), ("landing, floor 2", At(-5.4f, -4.9f, 2)), ("corridor, floor 2", At(-3f, 3f, 2))
                };
                // A reachability map of the ground floor's hall (x across, z down the page), and what stands there.
                for (float zz = 5.2f; zz >= -5.4f; zz -= 0.8f)
                {
                    var row = new System.Text.StringBuilder($"  z {zz,5:0.0} ");
                    for (float xx = -7.4f; xx <= -1.6f; xx += 0.6f)
                    {
                        Vector3 q = At(xx, zz, 0) + Vector3.up * 0.2f;
                        bool onMesh = UnityEngine.AI.NavMesh.SamplePosition(q, out UnityEngine.AI.NavMeshHit qh, 0.3f, UnityEngine.AI.NavMesh.AllAreas) && Mathf.Abs(qh.position.y - q.y) < 0.5f;
                        row.Append(!onMesh ? '#' : PathStatus(front + Vector3.up * 0.3f, q) == UnityEngine.AI.NavMeshPathStatus.PathComplete ? '.' : 'x');
                    }
                    Line(row.ToString());
                }
                foreach (Collider col in Physics.OverlapBox(At(-4.5f, 0f, 0) + Vector3.up * 1.2f, new Vector3(3.2f, 0.9f, 5.6f), Quaternion.Euler(0f, apt.Yaw, 0f), ~0, QueryTriggerInteraction.Ignore))
                    Line($"  in the hall: {col.name} [{LayerMask.LayerToName(col.gameObject.layer)}] at {Fmt(col.bounds.center)} y {col.bounds.center.y - apt.Origin.Y:0.0}, size {col.bounds.size}");
                foreach (var (name, p) in probes)
                {
                    bool on = UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 0.6f, UnityEngine.AI.NavMesh.AllAreas);
                    Line($"  {name}: NavMesh {(on ? $"at y {hit.position.y - apt.Origin.Y:0.00}" : "none within 0.6 m")}, path from the street {PathStatus(front + Vector3.up * 0.3f, p)}");
                }
            }

            // Climb the stairs on foot.
            StandAndLook(gc, At(-6.9f, -4.8f, 0), At(-6.9f, 2f, 1));
            gc.CameraRig.Look(Facing(0f, 1f), 10f);
            yield return Seconds(0.4f);
            yield return Capture("51_apartment_stair_hall");
            gc.Player.SetScriptedMove(Vector2.up, false);
            float climb = Time.realtimeSinceStartup + P(5f);
            while (Time.realtimeSinceStartup < climb && gc.Player.transform.position.y < apt.Origin.Y + plan.FloorY(1) - 0.05f) yield return null;
            yield return Seconds(0.25f);
            gc.Player.SetScriptedMove(Vector2.zero, false);
            float y1 = gc.Player.transform.position.y - apt.Origin.Y;
            Check(Mathf.Abs(y1 - plan.FloorY(1)) < 0.3f, $"Walked up the first flight to the first floor (height {y1:0.00} m, floor at {plan.FloorY(1):0.00} m)");
            StandAndLook(gc, At(-5.4f, 2.1f, 1), At(-5.4f, -4f, 2));
            gc.CameraRig.Look(Facing(0f, -1f), 10f);
            yield return Seconds(0.3f);
            gc.Player.SetScriptedMove(Vector2.up, false);
            climb = Time.realtimeSinceStartup + P(5f);
            while (Time.realtimeSinceStartup < climb && gc.Player.transform.position.y < apt.Origin.Y + plan.FloorY(2) - 0.05f) yield return null;
            yield return Seconds(0.25f);
            gc.Player.SetScriptedMove(Vector2.zero, false);
            float y2 = gc.Player.transform.position.y - apt.Origin.Y;
            Check(Mathf.Abs(y2 - plan.FloorY(2)) < 0.3f, $"Walked up the second flight to the second floor (height {y2:0.00} m)");
            PlanRoom room = w.Layout.RoomAt(gc.Player.transform.position.ToSim(), out _);
            Check(room != null && room.Floor == 2, $"The survivor is in the {room?.Name} on floor {room?.Floor}");

            // Flat 3 is locked; its key is behind the shop counter downstairs.
            Structure flat3 = Fixture("door:flat3");
            StandAndLook(gc, At(-2.6f, 2.6f, 2), flat3.Position.ToUnity() + Vector3.up);
            yield return Seconds(0.3f);
            OpResult refused = s.ToggleDoor(flat3.Id);
            Check(!refused.Ok && flat3.DoorLocked, "Flat 3 is locked: " + refused.Message);
            Structure counter = Fixture("shop:counter");
            Structure shopDoor = Fixture("door:shop");
            StandAndLook(gc, At(-2.6f, 2.6f, 0), shopDoor.Position.ToUnity() + Vector3.up);
            yield return null;
            Check(s.ToggleDoor(shopDoor.Id).Ok && shopDoor.DoorOpen, "Opened the corner shop's door");
            StandAndLook(gc, StandSpot(gc, counter), counter.Position.ToUnity());
            yield return null;
            gc.Ui.OpenContainer(counter.Storage.Id, "Shop Counter");
            yield return Seconds(0.5f);
            yield return Capture("52_shop_counter_key");
            OpResult took = s.TakeAll(counter.Storage.Id);
            gc.Ui.CloseWindow();
            bool hasKey = s.Player.Inventory.HasTagged(DoorRules.KeyItem, flat3.LockId);
            Check(took.Ok && hasKey, $"Found the key to {flat3.LockId} behind the shop counter");
            StandAndLook(gc, At(-2.6f, 2.6f, 2), flat3.Position.ToUnity() + Vector3.up);
            yield return null;
            OpResult unlocked = s.ToggleDoor(flat3.Id);
            yield return Seconds(1f);
            Check(unlocked.Ok && flat3.DoorOpen && !flat3.DoorLocked, "Unlocked and opened flat 3: " + unlocked.Message);
            StandAndLook(gc, At(0.4f, 1.8f, 2), At(5f, 4.5f, 2) + Vector3.up * 0.8f);
            yield return Seconds(1.2f);
            yield return Capture("53_flat3_study");
            Structure desk = Fixture("flat3:desk1");
            Check(desk != null && s.CanAccess(desk.Storage.Id, out _) , "The study's desk can be searched from inside the flat");

            // Close and lock it again with the key; the lock survives a save.
            StandAndLook(gc, At(-2.6f, 2.6f, 2), flat3.Position.ToUnity() + Vector3.up);
            yield return null;
            s.ToggleDoor(flat3.Id);
            Check(s.SetDoorLocked(flat3.Id, true).Ok && flat3.DoorLocked, "Locked flat 3 again with its key");

            // An infected sees the survivor through an open door, the door is shut in its face, and it
            // batters it until it breaks, then walks through.
            Structure bedroom = Fixture("door:flat2_bedroom");
            BuildingDefinition doorDef = s.Content.Building(bedroom.DefId);
            bedroom.Health = doorDef.MaxHealth * 0.45f;
            StandAndLook(gc, At(1.2f, -2.2f, 1), bedroom.Position.ToUnity() + Vector3.up);
            yield return null;
            if (!bedroom.DoorOpen) s.ToggleDoor(bedroom.Id);
            yield return Seconds(0.6f);
            var z = s.Zombies.Spawn(At(2.4f, 4.4f, 1).ToSim(), "runner", s.Rng);
            float seen = Time.realtimeSinceStartup + P(8f);
            while (Time.realtimeSinceStartup < seen && z.Mode != Afterfall.Domain.AI.ZombieMode.Chase) yield return null;
            Line($"  the infected noticed the survivor: {z.Mode}");
            s.ToggleDoor(bedroom.Id);
            float batter = Time.realtimeSinceStartup + P(25f), shout = 0f;
            while (Time.realtimeSinceStartup < batter && !DoorRules.IsBroken(bedroom, doorDef) && !z.IsDead)
            {
                // The survivor keeps making noise behind the door.
                shout -= Time.deltaTime;
                if (shout <= 0f)
                {
                    shout = 1f;
                    s.EmitNoise(s.Player.Position, 14f, "tour");
                }
                yield return null;
            }
            if (!DoorRules.IsBroken(bedroom, doorDef)) LogZombie(gc, z);
            yield return Seconds(1f);
            Check(DoorRules.IsBroken(bedroom, doorDef), $"An infected battered the closed bedroom door until it broke ({bedroom.Health:0}/{doorDef.MaxHealth:0})");
            StructureView bedroomView = gc.Sync.StructureViewOf(bedroom.Id);
            Check(bedroomView != null && !bedroomView.DoorBlocksNavigation, "The broken door no longer blocks the way");
            yield return Capture("54_broken_door");
            foreach (var x in w.Zombies.Values.ToList()) w.Zombies.Remove(x.Id);
            w.MarkChanged();
            s.Player.Vitals.Health = s.Player.Vitals.MaxHealth;
            StandAndLook(gc, front, At(0f, 0f, 1) + Vector3.up * 2f);
            yield return null;
        }

        private IEnumerator CreativeScene(GameController gc)
        {
            GameSession s = gc.Session;
            Line("— Creative and developer mode —");
            if (gc.Player.Flight.Active) gc.Player.Flight.ForceLand();
            if (s.World.Settings.ToolsEnabled())
            {
                Line("Developer tools were already on; skipping the locked-state checks");
            }
            else
            {
                bool flew = gc.Player.Flight.SetActive(true, out string refused);
                Check(!flew && !gc.Player.Flight.Active, $"Survival refuses free flight without developer tools ({refused})");
                Check(!s.Creative.SpawnItem("planks", 5, SpawnTarget.Backpack).Ok, "Survival refuses item spawning without developer tools");
            }

            // Open a container first, so the panel has a container to spawn into.
            Structure crate = s.World.Structures.Values.Where(x => !x.PlayerBuilt && x.Storage != null)
                .OrderBy(x => Vector3.Distance(x.Position.ToUnity(), gc.Player.transform.position)).FirstOrDefault();
            if (crate != null)
            {
                StandAndLook(gc, StandSpot(gc, crate), crate.Position.ToUnity());
                yield return null;
                gc.Ui.OpenContainer(crate.Storage.Id, "Supply crate");
                yield return Seconds(0.3f);
                gc.Ui.CloseWindow();
            }

            gc.Ui.ToggleCreativePanel();
            yield return Seconds(0.5f);
            var panel = gc.Ui.CurrentWindow as CreativePanel;
            if (panel == null)
            {
                Line("FAIL: F6 did not open the creative panel");
                yield break;
            }
            yield return Capture("30_creative_locked");
            Check(s.Creative.EnableDeveloperTools().Ok && s.Creative.Enabled, $"Developer tools enabled; the save is labelled \"{s.World.Settings.ModeLabel()}\"");
            panel.ShowItems("");
            yield return Seconds(0.3f);
            Check(panel.ShownCount == s.Content.Items.Count, $"The item browser lists every registered item ({panel.ShownCount} of {s.Content.Items.Count})");
            panel.ShowItems("axe");
            yield return Seconds(0.4f);
            Check(panel.ShownCount >= 3, $"Searching \"axe\" finds {panel.ShownCount} items");
            Check(panel.SelectById("metal_axe"), "Selected the metal axe in the browser");
            yield return Seconds(0.3f);
            yield return Capture("31_creative_item_browser");
            int axes = s.Player.Inventory.Count("metal_axe");
            panel.SetQuantity(1);
            OpResult spawned = panel.SpawnSelected(SpawnTarget.Backpack);
            Check(spawned.Ok && s.Player.Inventory.Count("metal_axe") == axes + 1, "Spawned a metal axe into the backpack: " + spawned.Message);
            panel.ShowItems("planks");
            panel.SelectById("planks");
            panel.SetQuantity(40);
            int drops = s.World.Drops.Count;
            OpResult dropped = panel.SpawnSelected(SpawnTarget.World);
            Check(dropped.Ok && s.World.Drops.Count == drops + 1, "Spawned 40 planks on the ground: " + dropped.Message);
            if (crate != null)
            {
                panel.ShowItems("nails");
                panel.SelectById("nails");
                panel.SetQuantity(25);
                int nails = crate.Storage.Count("nails");
                OpResult into = panel.SpawnSelected(SpawnTarget.Container);
                Check(into.Ok && crate.Storage.Count("nails") == nails + 25, "Spawned 25 nails into the supply crate: " + into.Message);
            }
            panel.ShowWorld();
            yield return Seconds(0.4f);
            yield return Capture("32_creative_world_tab");
            gc.Ui.CloseWindow();

            // ------------------------------------------------------------ free flight
            PoiInfo workshop = s.World.Layout.Poi("workshop");
            Vector3 start = gc.Player.transform.position;
            bool workshopKnown = s.World.Exploration.IsRevealed(workshop.Center.X, workshop.Center.Z);
            Check(gc.Player.Flight.SetActive(true, out string takeoff) && gc.Player.Flight.Active, "Free flight on: " + takeoff);
            Check(!gc.Player.Rig.gameObject.activeSelf, "The survivor's body is hidden in flight, so no walking or running animation plays");
            Vector3 to = workshop.Center.ToUnity() - start;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            gc.CameraRig.Look(yaw, 8f); // positive pitch looks down over the valley
            gc.Player.Flight.SetSpeed(40f);
            gc.Player.Flight.SetScriptedInput(new Vector3(0f, 1f, 0f), false);
            yield return Seconds(1.2f);
            gc.Player.Flight.SetScriptedInput(new Vector3(0f, 0f, 1f), true);
            float flightStart = Time.realtimeSinceStartup;
            bool captured = false;
            float maxSpeed = 0f;
            var frames = new System.Collections.Generic.List<float>();
            while (Time.realtimeSinceStartup - flightStart < P(20f))
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                maxSpeed = Mathf.Max(maxSpeed, gc.Player.Flight.CurrentSpeed);
                Vector3 here = gc.Player.transform.position;
                float left = Vector2.Distance(new Vector2(here.x, here.z), new Vector2(workshop.Center.X, workshop.Center.Z));
                if (!captured && left < 150f)
                {
                    captured = true;
                    yield return Capture("33_free_flight");
                }
                // Ease off near the target, as a player would.
                if (left < 140f) gc.Player.Flight.SetScriptedInput(new Vector3(0f, 0f, 1f), false);
                if (left < 35f) break;
            }
            gc.Player.Flight.SetScriptedInput(Vector3.zero, false);
            float flown = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(gc.Player.transform.position.x, gc.Player.transform.position.z));
            float seconds = Time.realtimeSinceStartup - flightStart;
            float remaining = Vector2.Distance(new Vector2(gc.Player.transform.position.x, gc.Player.transform.position.z), new Vector2(workshop.Center.X, workshop.Center.Z));
            frames.Sort();
            Check(remaining < 40f && flown > 150f, $"Flew {flown:0} m to within {remaining:0} m of Hartley's Workshop in {seconds:0.0} s (top speed {maxSpeed:0} m/s); frames median {(frames.Count > 0 ? frames[frames.Count / 2] : 0):0.0} ms, worst {(frames.Count > 0 ? frames[frames.Count - 1] : 0):0.0} ms");
            Check(s.Player.Activity.Flying, "The simulation knows the survivor is flying");
            Check(workshopKnown || !s.World.Exploration.IsRevealed(workshop.Center.X, workshop.Center.Z), "Flying over unexplored land does not reveal it");
            Check(!gc.Player.Rig.gameObject.activeSelf, "Still no body animation in flight");

            Check(gc.Player.Flight.SetActive(false, out string landed) && !gc.Player.Flight.Active, "Landed: " + landed);
            yield return Seconds(0.6f);
            Vector3 feet = gc.Player.transform.position;
            Check(SafeGround.Fits(feet + Vector3.up * 0.02f) && feet.y >= gc.GroundHeight(feet) - 0.3f && gc.Player.Rig.gameObject.activeSelf,
                $"Back on safe ground at {Fmt(feet)} (y {feet.y:0.0}, terrain {gc.GroundHeight(feet):0.0}), body visible, nothing overlapping");
            yield return Capture("34_landed_at_workshop");

            // ------------------------------------------------------------ teleports
            PoiInfo quarry = s.World.Layout.Poi("quarry");
            Vector3 beforeTeleport = gc.Player.transform.position;
            gc.TeleportTo(quarry.Center.ToUnity(), quarry.Name);
            yield return null;
            float tpWait = Time.realtimeSinceStartup + P(10f);
            while (gc.Teleporting && Time.realtimeSinceStartup < tpWait) yield return null;
            yield return Seconds(0.5f);
            Vector3 atQuarry = gc.Player.transform.position;
            Check(Vector2.Distance(new Vector2(atQuarry.x, atQuarry.z), new Vector2(quarry.Center.X, quarry.Center.Z)) < 45f && SafeGround.Fits(atQuarry + Vector3.up * 0.02f),
                $"Teleported to {quarry.Name}: {Fmt(atQuarry)}, standing clear");
            yield return Capture("35_teleport_quarry");
            gc.TeleportBack();
            yield return null;
            tpWait = Time.realtimeSinceStartup + P(10f);
            while (gc.Teleporting && Time.realtimeSinceStartup < tpWait) yield return null;
            yield return Seconds(0.3f);
            Check(Vector3.Distance(gc.Player.transform.position, beforeTeleport) < 6f, $"\"Back\" returned to {Fmt(gc.Player.transform.position)} (was {Fmt(beforeTeleport)})");
            Check(s.Creative.AddBookmark("Workshop yard", s.Player.Position).Ok && s.World.Navigation.Markers.Any(m => m.Kind == MarkerKind.Bookmark), "Bookmarked the workshop yard");

            // ------------------------------------------------------------ creative construction
            Check(s.Creative.SetMode(GameMode.Creative).Ok && s.World.Settings.IsCreative(), "Switched to Creative mode");
            float weight = s.Player.Inventory.TotalWeight;
            Structure gen = PlanNear(gc, "generator", gc.Player.transform.position + gc.Player.transform.forward * 6f, 3f, out string reason, 24f);
            Check(gen != null && gen.IsComplete && gen.Generator != null, gen != null ? $"Creative: a generator (still locked by technology) was built instantly at {Fmt(gen.Position.ToUnity())}" : "FAIL: creative generator: " + reason);
            Check(Mathf.Abs(s.Player.Inventory.TotalWeight - weight) < 0.01f, "Creative building used no materials");
            Structure wall = PlanNear(gc, "barricade", gc.Player.transform.position + gc.Player.transform.forward * 4f, 2f, out reason, 20f);
            Check(wall != null && wall.IsComplete, "Creative: a barricade built instantly");
            if (gen != null)
            {
                Vector3 g = gen.Position.ToUnity();
                StandAndLook(gc, StandSpot(gc, gen), g);
                yield return Seconds(1f);
                yield return Capture("36_creative_build");
            }
            s.DamagePlayer(80f, "tour");
            Check(s.Player.Vitals.Health >= s.Player.Vitals.MaxHealth - 0.01f, "Creative mode keeps the survivor unharmed");

            // Spawn a vehicle from the panel's vehicle browser.
            gc.Ui.ToggleCreativePanel();
            yield return Seconds(0.4f);
            if (gc.Ui.CurrentWindow is CreativePanel vehiclesPanel)
            {
                vehiclesPanel.ShowVehicles();
                yield return Seconds(0.4f);
                yield return Capture("38_creative_vehicles");
                int before = s.World.Vehicles.Count;
                OpResult made = vehiclesPanel.SpawnVehicle("pickup");
                gc.Ui.CloseWindow();
                yield return Seconds(1.5f);
                var spawnedCar = s.World.Vehicles.Values.FirstOrDefault(v => v.DebugSpawned);
                VehicleView sv = spawnedCar != null ? gc.Sync.VehicleViewOf(spawnedCar.Id) : null;
                Check(made.Ok && s.World.Vehicles.Count == before + 1 && sv != null && Vector3.Dot(sv.transform.up, Vector3.up) > 0.95f
                      && s.Player.Inventory.HasTagged(Afterfall.Domain.Vehicles.VehicleRules.KeyItem, spawnedCar.LockId),
                    $"Spawned a pickup from the creative panel on clear ground {(sv != null ? Vector3.Distance(sv.transform.position, gc.Player.transform.position) : 0f):0} m ahead, upright, with its key: {made.Message}");
                if (sv != null)
                {
                    StandAndLook(gc, Ground(gc, sv.transform.position - sv.transform.right * 3f), sv.transform.position + Vector3.up);
                    yield return Seconds(0.8f);
                    yield return Capture("39_spawned_pickup");
                }
            }

            bool fps = gc.Settings.ShowFps;
            int sections = gc.Settings.DiagnosticSections;
            gc.Settings.ShowFps = true;
            gc.Settings.DiagnosticSections = 255;
            yield return Seconds(1.2f);
            yield return Capture("37_diagnostics");
            gc.Settings.ShowFps = fps;
            gc.Settings.DiagnosticSections = sections;

            gc.Save("tour_creative");
            string text = _boot.Saves.Store.Exists("tour_creative") ? _boot.Saves.Store.ReadMain("tour_creative") : null;
            SaveHeader header = text != null ? SaveSerializer.ReadHeader(text) : null;
            Check(header != null && header.Mode == "Creative", $"The save is labelled \"{header?.Mode}\" in the load list");
            _boot.Saves.Store.Delete("tour_creative");
            Check(s.Creative.SetMode(GameMode.Survival).Ok && !s.World.Settings.IsCreative() && s.World.Settings.DeveloperTools, $"Back to Survival; the save stays marked \"{s.World.Settings.ModeLabel()}\"");
        }
    }
}
