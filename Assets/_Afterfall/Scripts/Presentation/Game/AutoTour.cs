using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Afterfall.Core;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Services;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Scripted smoke run for builds and the editor: <c>-afterfall-tour &lt;dir&gt;</c> starts a new game,
    /// visits the main screens, builds a powered settlement with a recruited colonist and crops,
    /// saves and reloads it, captures screenshots, measures frame times, writes
    /// <c>tour-report.txt</c> (including every error logged) and quits. Lines starting with
    /// "FAIL" mark checks that did not hold.
    /// <para>Building materials are granted rather than gathered (counted in the report); placement,
    /// construction, machines, power, colonists and farming run through the normal game commands.</para>
    /// </summary>
    public sealed class AutoTour : MonoBehaviour
    {
        public const string Flag = "-afterfall-tour";
        private GameBootstrap _boot;
        private string _dir;
        private readonly List<string> _errors = new List<string>();
        private readonly StringBuilder _report = new StringBuilder();
        private readonly SortedDictionary<string, int> _granted = new SortedDictionary<string, int>();

        /// <summary>What the reload check looks for.</summary>
        private sealed class Settlement
        {
            public Vector3 Site;
            public EntityId Colonist, Plot, Workshop, Generator;
        }

        public static void TryAttach(GameBootstrap boot)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, Flag);
            if (i < 0) return;
            string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "Tour");
            var tour = boot.gameObject.AddComponent<AutoTour>();
            tour._boot = boot;
            tour._dir = Path.GetFullPath(dir);
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _errors.Add($"{type}: {message}\n{stack}");
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_dir);
            Line($"AFTERFALL tour {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {Screen.width}x{Screen.height}");
            yield return Seconds(2f);
            yield return Capture("01_main_menu");

            float t0 = Time.realtimeSinceStartup;
            _boot.StartNewGame(DifficultyPreset.Survival, 20261107);
            float timeout = t0 + 180f;
            while ((_boot.Game == null || !_boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            GameController gc = _boot.Game;
            if (gc == null || !gc.Running)
            {
                Line("FAIL: the new game did not finish loading within 180 s");
                Finish();
                yield break;
            }
            Line($"New game ready in {Time.realtimeSinceStartup - t0:0.0} s");
            gc.Session.Threats.SpawningEnabled = false;
            float navTimeout = Time.realtimeSinceStartup + 60f;
            while (!gc.NavReady && Time.realtimeSinceStartup < navTimeout) yield return null;
            Line($"NavMesh ready: {gc.NavReady} after {Time.realtimeSinceStartup - t0:0.0} s");
            yield return Seconds(2f);
            yield return Capture("02_start_camp");
            yield return MeasureFrames("start camp", 5f);
            LogRendering();
            SkipTo(gc, 12.5);
            yield return Seconds(1.5f);
            yield return Capture("02b_midday");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-afterfall-tour-diag") >= 0)
            {
                RenderSettings.sun.shadows = LightShadows.None;
                yield return Seconds(0.5f);
                yield return Capture("02c_midday_no_shadows");
                RenderSettings.sun.shadows = LightShadows.Soft;
                SkipTo(gc, 15.5);
                yield return Seconds(1f);
                yield return Capture("02d_afternoon");
                foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off || !r.enabled) continue;
                    Vector3 size = r.bounds.size;
                    if (size.x > 60f || size.z > 60f) Line($"Large shadow caster: {r.name} ({r.GetType().Name}) size {size} layer {LayerMask.LayerToName(r.gameObject.layer)} material {r.sharedMaterial?.name}");
                }
            }

            ResourceNodeView node = FindObjectsByType<ResourceNodeView>(FindObjectsSortMode.None)
                .Where(v => v.Def.Id == "branch_pile")
                .OrderBy(v => Vector3.Distance(v.transform.position, gc.Player.transform.position))
                .FirstOrDefault();
            if (node != null)
            {
                gc.Player.Teleport(node.transform.position + new Vector3(1.4f, 0.2f, 0f));
                yield return Seconds(0.5f);
                int before = gc.Session.Player.Inventory.Count("wood");
                gc.HarvestTarget(node);
                Line($"Harvest branch pile: wood {before} -> {gc.Session.Player.Inventory.Count("wood")}");
            }
            else Line("FAIL: no branch pile found");

            gc.Ui.OpenInventory(false);
            yield return Seconds(0.5f);
            yield return Capture("03_inventory_crafting");
            gc.Ui.CloseWindow();

            gc.Ui.ToggleBuildMenu();
            yield return Seconds(0.5f);
            yield return Capture("04_build_menu");
            gc.Ui.CloseWindow();

            BuildingDefinition def = gc.Session.Content.Building("foundation") ?? gc.Session.Content.Buildings.Values.FirstOrDefault(b => b.Buildable);
            if (def != null)
            {
                gc.Build.Begin(def);
                yield return Seconds(1f);
                Line($"Placement preview for {def.Id}: has position {gc.Build.HasPosition}, valid {gc.Build.LastResult?.Valid.ToString() ?? "none"} {string.Join("; ", gc.Build.LastResult?.Reasons ?? new List<string>())}");
                yield return Capture("05_placement_ghost");
                gc.Build.Cancel();
            }

            ZombieSpawnNear(gc, 9f, "wanderer");
            ZombieSpawnNear(gc, 12f, "runner");
            yield return Seconds(2.5f);
            yield return Capture("06_infected");
            foreach (var z in gc.Session.World.Zombies.Values.ToList()) gc.Session.World.Zombies.Remove(z.Id);
            gc.Session.World.MarkChanged();

            gc.SetTactical(true);
            yield return Seconds(2f);
            yield return Capture("07_strategic_view");
            yield return MeasureFrames("strategic view", 3f);
            Vector3 far = gc.Player.transform.position + new Vector3(140f, 0f, 60f);
            gc.CameraRig.Tactical.Focus(far, 0f);
            yield return Seconds(1f);
            yield return Capture("07b_strategic_unexplored");
            Transform fog = gc.Tactical.transform.Find("FogOfWar");
            Renderer fogRenderer = fog != null ? fog.GetComponent<Renderer>() : null;
            Line(fogRenderer == null ? "FAIL: no fog-of-war renderer" :
                $"Fog of war: active {fog.gameObject.activeInHierarchy}, visible {fogRenderer.isVisible}, shader {fogRenderer.sharedMaterial.shader.name}, queue {fogRenderer.sharedMaterial.renderQueue}, layer {LayerMask.LayerToName(fog.gameObject.layer)}");
            gc.SetTactical(false);

            SkipTo(gc, 9.0);
            Settlement town = null;
            yield return BuildSettlement(gc, t => town = t);
            if (town == null)
            {
                Finish();
                yield break;
            }

            SkipTo(gc, 22.0);
            StandAndLook(gc, town.Site + new Vector3(-9f, 0f, -7f), town.Site);
            yield return Seconds(2f);
            yield return Capture("16_night_at_base");

            int structures = gc.Session.World.Structures.Count;
            gc.Save("tour_test");
            _boot.LoadGame("tour_test");
            float loadTimeout = Time.realtimeSinceStartup + 120f;
            while ((_boot.Game == null || _boot.Game == gc || !_boot.Game.Running) && Time.realtimeSinceStartup < loadTimeout) yield return null;
            GameController loaded = _boot.Game;
            if (loaded == null || loaded == gc || !loaded.Running) Line("FAIL: reload did not complete");
            else
            {
                WorldState w = loaded.Session.World;
                bool sameStructures = w.Structures.Count == structures;
                bool recruited = w.Colonists.TryGetValue(town.Colonist, out ColonistState c) && c.Recruited;
                bool crops = w.Structures.TryGetValue(town.Plot, out Structure plot) && plot.Crop != null && plot.Crop.Planted;
                bool recipe = w.Structures.TryGetValue(town.Workshop, out Structure shop) && shop.Machine.RecipeId == "gears";
                bool linked = w.HasPowerLink(town.Generator, town.Workshop);
                Line($"{(sameStructures && recruited && crops && recipe && linked ? "Save and reload: OK" : "FAIL: save and reload")} " +
                     $"(structures {w.Structures.Count}/{structures}, colonist recruited {recruited}, crop planted {crops}, workshop recipe kept {recipe}, power link kept {linked})");
            }
            _boot.Saves.Store.Delete("tour_test");
            yield return Seconds(1.5f);
            yield return Capture("17_after_reload");
            Finish();
        }

        // ================================================================== settlement

        private IEnumerator BuildSettlement(GameController gc, Action<Settlement> done)
        {
            GameSession s = gc.Session;
            Inventory bag = s.Player.Inventory;
            float maxWeight = bag.MaxWeight;
            bag.MaxWeight = 2000f; // granted materials would otherwise exceed the carry limit
            ColonistState survivor = s.World.Colonists.Values.Where(c => !c.Recruited).OrderBy(c => c.Id).FirstOrDefault();
            if (survivor == null)
            {
                Line("FAIL: no survivor to recruit");
                yield break;
            }
            var town = new Settlement { Site = Ground(gc, survivor.Position.ToUnity() + new Vector3(-16f, 0f, 4f)), Colonist = survivor.Id };
            Line($"Settlement site near {survivor.Name} at {Fmt(town.Site)}");

            Structure bed = null, crate = null, furnace = null, gen = null, shop = null, plotA = null, plotB = null;
            yield return Construct(gc, "bed", town.Site, x => bed = x);
            yield return Construct(gc, "storage_crate", town.Site + new Vector3(4f, 0f, 0f), x => crate = x);
            yield return Construct(gc, "workbench", town.Site + new Vector3(0f, 0f, 4f), null);
            Grant(bag, "iron_ore", 6);
            yield return Seconds(1f);
            Check(s.World.Tech.IsUnlocked("metalworking"), "Iron ore at a workbench unlocks Metalworking");
            yield return Construct(gc, "furnace", town.Site + new Vector3(5f, 0f, 5f), x => furnace = x);
            if (furnace != null)
            {
                yield return Use(gc, furnace, () => s.SetMachineRecipe(furnace.Id, "smelt_iron"), "set the furnace recipe");
                yield return Deposit(gc, furnace, "iron_ore", 6, furnace.Machine.Input);
                Grant(bag, "wood", 3);
                yield return Deposit(gc, furnace, "wood", 3, furnace.Machine.Fuel);
                Time.timeScale = 4f;
                float smelt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - smelt < 20f && furnace.Machine.Output.Count("iron_ingot") < 2) yield return null;
                Time.timeScale = 1f;
                Check(furnace.Machine.Output.Count("iron_ingot") >= 2, $"Furnace: {furnace.Machine.Status}, {furnace.Machine.Output.Count("iron_ingot")} ingots smelted ({Time.realtimeSinceStartup - smelt:0.0} s at 4x)");
                gc.Ui.OpenMachine(furnace.Id);
                yield return Seconds(0.5f);
                yield return Capture("09_furnace_window");
                gc.Ui.CloseWindow();
            }
            Grant(bag, "schematic_generator", 1);
            yield return Seconds(1f);
            Check(s.World.Tech.IsUnlocked("electricity"), "A generator schematic plus smelted ingots unlock Electricity");
            yield return Construct(gc, "generator", town.Site + new Vector3(-6f, 0f, 2f), x => gen = x);
            if (gen != null) yield return Construct(gc, "workshop", gen.Position.ToUnity(), x => shop = x, 3.5f);
            if (shop != null) yield return Construct(gc, "lamp", shop.Position.ToUnity(), null, 2.5f);
            Grant(bag, "seed_potato", 1);
            yield return Seconds(1f);
            Check(s.World.Tech.IsUnlocked("agriculture"), "Seeds unlock Agriculture");
            yield return Construct(gc, "planter_bed", town.Site + new Vector3(2f, 0f, -5f), x => plotA = x);
            yield return Construct(gc, "planter_bed", town.Site + new Vector3(5f, 0f, -5f), x => plotB = x);
            if (bed == null || crate == null || furnace == null || gen == null || shop == null || plotA == null || plotB == null)
            {
                Line("FAIL: the settlement could not be completed");
                bag.MaxWeight = maxWeight;
                yield break;
            }
            town.Generator = gen.Id;
            town.Workshop = shop.Id;
            town.Plot = plotB.Id;
            Check(s.World.HasPowerLink(gen.Id, shop.Id), "The workshop connected itself to the generator");
            StandAndLook(gc, town.Site + new Vector3(-10f, 0f, -9f), town.Site + new Vector3(0f, 0f, 1f));
            yield return Seconds(1f);
            yield return Capture("08_base_built");

            // Electricity.
            yield return Use(gc, shop, () => s.SetMachineRecipe(shop.Id, "gears"), "set the workshop recipe");
            Grant(bag, "iron_ingot", 20);
            yield return Deposit(gc, shop, "iron_ingot", 20, shop.Machine.Input);
            yield return Seconds(1f);
            Check(shop.Machine.Status == MachineStatus.MissingPower, $"Workshop without generator fuel: {shop.Machine.Status} ({shop.Machine.StatusDetail})");
            Grant(bag, "charcoal", 1);
            yield return Deposit(gc, gen, "charcoal", 1, gen.Generator.Fuel);
            Time.timeScale = 4f;
            yield return Seconds(6f);
            Time.timeScale = 1f;
            Check(shop.Machine.Output.Count("gear") > 0, $"Powered workshop: {shop.Machine.Status}, {shop.Machine.Output.Count("gear")} gears machined; generator: {gen.Generator.Status}");
            gc.Ui.OpenMachine(shop.Id);
            yield return Seconds(0.5f);
            yield return Capture("10_workshop_window");
            gc.Ui.CloseWindow();

            gc.SetTactical(true);
            gc.CameraRig.Tactical.Focus(town.Site, 20f);
            gc.Tactical.Select(shop.Id);
            yield return Seconds(1.5f);
            yield return Capture("11_settlement_strategic");
            s.ToggleEnabled(gen.Id);
            yield return Seconds(1.5f);
            Check(shop.Machine.Status == MachineStatus.MissingPower, $"Generator switched off: workshop {shop.Machine.Status} ({shop.Machine.StatusDetail})");
            yield return Capture("12_power_outage");
            s.ToggleEnabled(gen.Id);
            yield return Seconds(1.5f);
            Check(shop.Machine.Status == MachineStatus.Working, $"Generator back on: workshop {shop.Machine.Status}");
            gc.Tactical.Select(EntityId.None);
            gc.SetTactical(false);

            // A colonist: recruited, then hauling, building and farming from the crate on their own.
            LogDoorway(gc, "farm", "house", 1.4f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-afterfall-tour-diag") >= 0) LogAllDoorways(gc);
            Vector3 hideout = survivor.Position.ToUnity();
            StandAndLook(gc, FreeSpotNear(gc, hideout, 1.6f), hideout);
            yield return Seconds(0.3f);
            OpResult joined = s.Recruit(survivor.Id);
            Check(joined.Ok, $"Recruit {survivor.Name}: {joined.Message} (survivor {Vector3.Distance(gc.Player.transform.position, hideout):0.0} m away)");
            Grant(bag, "planks", 6);
            Grant(bag, "nails", 6);
            Grant(bag, "seed_potato", 1);
            Grant(bag, "water_bottle", 3);
            yield return Deposit(gc, crate, "planks", 6, crate.Storage);
            yield return Deposit(gc, crate, "nails", 6, crate.Storage);
            yield return Deposit(gc, crate, "seed_potato", 1, crate.Storage);
            yield return Deposit(gc, crate, "water_bottle", 2, crate.Storage);
            Structure extra = PlanNear(gc, "storage_crate", town.Site + new Vector3(-3f, 0f, -3f), 0f, out string reason);
            Check(extra != null, "Planned a second crate for the colonist to build" + (extra == null ? ": " + reason : ""));
            yield return Use(gc, plotA, () => s.PlantCrop(plotA.Id), "plant by hand");
            yield return Use(gc, plotA, () => s.WaterCrop(plotA.Id), "water by hand");

            Time.timeScale = 4f;
            float start = Time.realtimeSinceStartup;
            float nextLog = start;
            bool captured = false;
            while (Time.realtimeSinceStartup - start < 45f && !(extra != null && extra.IsComplete && plotB.Crop.Planted && plotB.Crop.Water > 0f))
            {
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    nextLog += 7.5f;
                    LogColonist(gc, survivor);
                }
                if (!captured && survivor.Activity == ColonistActivity.Working)
                {
                    captured = true;
                    Time.timeScale = 1f;
                    Vector3 at = survivor.Position.ToUnity();
                    StandAndLook(gc, at + (town.Site - at).normalized * -5f + new Vector3(3f, 0f, 0f), at);
                    yield return Seconds(0.6f);
                    yield return Capture("13_colonist_at_work");
                    Time.timeScale = 4f;
                }
                yield return Seconds(0.5f);
            }
            Time.timeScale = 1f;
            Check(extra != null && extra.IsComplete, $"The colonist hauled the planks and nails and built the crate ({Time.realtimeSinceStartup - start:0} s at 4x)");
            Check(plotB.Crop.Planted && plotB.Crop.Water > 0f, $"The colonist planted and watered the second bed (water {plotB.Crop.Water:0})");
            Line($"{survivor.Name}: {survivor.Activity}, \"{survivor.Status}\"{(survivor.RecentFailures.Count > 0 ? "; recent problems: " + string.Join(" | ", survivor.RecentFailures) : "")}");

            plotB.Crop.Growth = 1f; // tour shortcut: ripen the crop so the harvest happens now
            Time.timeScale = 4f;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 30f && crate.Storage.Count("potato") == 0) yield return Seconds(0.5f);
            Time.timeScale = 1f;
            Check(crate.Storage.Count("potato") > 0, $"The colonist harvested the ripe bed into the crate ({crate.Storage.Count("potato")} potatoes)");

            gc.SetTactical(true);
            gc.CameraRig.Tactical.Focus(town.Site, 20f);
            yield return Seconds(1.5f);
            yield return Capture("14_colony_panel");
            gc.SetTactical(false);
            StandAndLook(gc, plotA.Position.ToUnity() + new Vector3(-3f, 0f, -4f), (plotA.Position.ToUnity() + plotB.Position.ToUnity()) * 0.5f);
            yield return Seconds(1f);
            yield return Capture("15_crops");

            bag.MaxWeight = maxWeight;
            Line("Granted for the settlement: " + string.Join(", ", _granted.Select(kv => $"{kv.Value} {kv.Key}")));
            done(town);
        }

        private IEnumerator Construct(GameController gc, string defId, Vector3 near, Action<Structure> done, float minRadius = 0f)
        {
            Structure st = PlanNear(gc, defId, near, minRadius, out string reason);
            if (st == null)
            {
                Line($"FAIL: could not plan {defId} near {Fmt(near)}: {reason}");
                yield break;
            }
            StandAndLook(gc, StandSpot(gc, st), st.Position.ToUnity());
            yield return null;
            yield return null;
            foreach (ItemAmount a in gc.Session.Construction.RemainingCost(st)) Grant(gc.Session.Player.Inventory, a.ItemId, a.Quantity);
            int steps = 0;
            while (!st.IsComplete && steps++ < 400)
            {
                OpResult r = gc.Session.BuildStep(st.Id, 0.5f);
                if (!r.Ok)
                {
                    Line($"FAIL: building {defId}: {r.Message}");
                    yield break;
                }
                yield return null;
            }
            if (!st.IsComplete)
            {
                Line($"FAIL: {defId} still unfinished after {steps} work steps");
                yield break;
            }
            Line($"Built {gc.Session.Content.Building(defId).Name} at {Fmt(st.Position.ToUnity())} ({steps} work steps)");
            yield return null;
            done?.Invoke(st);
        }

        /// <summary>Plans a blueprint at the first valid spot on rings around <paramref name="near"/>.</summary>
        /// <summary>Diagnostics for a POI building's front door: NavMesh on both sides, a path through, colliders in the opening.</summary>
        private void LogDoorway(GameController gc, string poi, string kind, float doorOffset)
        {
            PropPlacement prop = gc.Session.World.Layout.Props.FirstOrDefault(p => p.PoiId == poi && p.Kind == kind);
            if (prop == null) return;
            Quaternion rot = Quaternion.Euler(0f, prop.Yaw, 0f);
            Vector3 origin = prop.Position.ToUnity();
            float halfZ = prop.Size.Z * 0.5f;
            Vector3 W(float x, float y, float z) => origin + rot * new Vector3(x, y, z);
            Vector3 inside = W(doorOffset, 0.2f, halfZ - 1.0f), outside = W(doorOffset, 0.2f, halfZ + 1.5f);
            bool inOk = UnityEngine.AI.NavMesh.SamplePosition(inside, out UnityEngine.AI.NavMeshHit inHit, 1f, UnityEngine.AI.NavMesh.AllAreas);
            bool outOk = UnityEngine.AI.NavMesh.SamplePosition(outside, out UnityEngine.AI.NavMeshHit outHit, 1f, UnityEngine.AI.NavMesh.AllAreas);
            string path = "n/a";
            bool connected = false;
            if (inOk && outOk)
            {
                var p = new UnityEngine.AI.NavMeshPath();
                UnityEngine.AI.NavMesh.CalculatePath(inHit.position, outHit.position, UnityEngine.AI.NavMesh.AllAreas, p);
                connected = p.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                path = $"{p.status} via " + string.Join(" → ", p.corners.Select(c => $"({c.x:0.0}, {c.y:0.00}, {c.z:0.0})"));
                bool blocked = UnityEngine.AI.NavMesh.Raycast(inHit.position, outHit.position, out UnityEngine.AI.NavMeshHit edge, UnityEngine.AI.NavMesh.AllAreas);
                path += blocked ? $"; NavMesh edge at ({edge.position.x:0.0}, {edge.position.y:0.00}, {edge.position.z:0.0})" : "; straight line clear";
            }
            Vector3 door = W(doorOffset, 0.2f, halfZ - 0.11f);
            bool doorOk = UnityEngine.AI.NavMesh.SamplePosition(door, out UnityEngine.AI.NavMeshHit doorHit, 0.5f, UnityEngine.AI.NavMesh.AllAreas);
            path += $"; inside ({inHit.position.x:0.0}, {inHit.position.z:0.0}), outside ({outHit.position.x:0.0}, {outHit.position.z:0.0}), door ({door.x:0.0}, {door.z:0.0}) NavMesh {doorOk}";
            var hits = Physics.OverlapBox(W(doorOffset, 1.0f, halfZ - 0.11f), new Vector3(0.55f, 0.85f, 0.4f), rot, ~0, QueryTriggerInteraction.Ignore);
            Line($"{(connected ? "" : "FAIL: ")}{poi} {kind} door: floor y {origin.y:0.00}, ground outside {gc.GroundHeight(outside):0.00}; NavMesh inside {inOk} (y {inHit.position.y:0.00}), outside {outOk} (y {outHit.position.y:0.00}); path {path}; in the opening: " +
                 string.Join(", ", hits.Select(h => $"{h.name}[{LayerMask.LayerToName(h.gameObject.layer)}]")));
        }

        private void LogAllDoorways(GameController gc)
        {
            var doors = new Dictionary<string, (float Offset, float Width)>
            {
                ["house"] = (1.4f, 1.6f), ["clinic"] = (0f, 1.6f), ["shop"] = (0f, 1.6f), ["shack"] = (0f, 1.6f),
                ["garage"] = (0f, 5f), ["warehouse"] = (0f, 4f), ["barn"] = (0f, 4f)
            };
            foreach (PropPlacement prop in gc.Session.World.Layout.Props)
            {
                if (!doors.TryGetValue(prop.Kind, out var d)) continue;
                Quaternion rot = Quaternion.Euler(0f, prop.Yaw, 0f);
                Vector3 W(float x, float z) => prop.Position.ToUnity() + rot * new Vector3(x, 0.2f, z);
                float halfZ = prop.Size.Z * 0.5f;
                bool a = UnityEngine.AI.NavMesh.SamplePosition(W(d.Offset, halfZ - 1.0f), out UnityEngine.AI.NavMeshHit ih, 1f, UnityEngine.AI.NavMesh.AllAreas);
                bool b = UnityEngine.AI.NavMesh.SamplePosition(W(d.Offset, halfZ + 1.5f), out UnityEngine.AI.NavMeshHit oh, 1f, UnityEngine.AI.NavMesh.AllAreas);
                var path = new UnityEngine.AI.NavMeshPath();
                if (a && b) UnityEngine.AI.NavMesh.CalculatePath(ih.position, oh.position, UnityEngine.AI.NavMesh.AllAreas, path);
                Line($"  door of {prop.PoiId} {prop.Kind} ({d.Width} m wide): {(a && b ? path.status.ToString() : $"no NavMesh inside {a} / outside {b}")}");
            }
        }

        private void LogColonist(GameController gc, ColonistState c)
        {
            ColonistView v = gc.Sync.ColonistViewOf(c.Id);
            string agent = v == null ? "no view" : !v.OnNavMesh ? $"off NavMesh (agent enabled {v.Agent.enabled})" :
                $"path {v.Agent.pathStatus}, pending {v.Agent.pathPending}, remaining {v.Agent.remainingDistance:0.0} m, speed {v.Agent.velocity.magnitude:0.0}, stopped {v.Agent.isStopped}";
            Line($"  {c.Name}: {c.Activity} \"{c.Status}\" at {Fmt(c.Position.ToUnity())} y {c.Position.Y:0.0} (ground {gc.GroundHeight(c.Position.ToUnity()):0.0}), target {(c.HasMoveTarget ? Fmt(c.MoveTarget.ToUnity()) : "none")}; {agent}");
        }

        private static Structure PlanNear(GameController gc, string defId, Vector3 near, float minRadius, out string reason)
        {
            reason = "unknown building";
            BuildingDefinition def = gc.Session.Content.Building(defId);
            if (def == null) return null;
            for (float r = minRadius; r <= 26f; r += 2f)
            {
                int steps = r < 0.5f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 2f);
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    OpResult res = gc.Build.PlanAt(def, near + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), 0f, out Structure st);
                    if (res.Ok) return st;
                    reason = res.Message;
                }
            }
            return null;
        }

        private IEnumerator Use(GameController gc, Structure st, Func<OpResult> action, string label)
        {
            StandAndLook(gc, StandSpot(gc, st), st.Position.ToUnity());
            yield return null;
            yield return null;
            OpResult r = action();
            if (!r.Ok) Line($"FAIL: {label}: {r.Message}");
        }

        private IEnumerator Deposit(GameController gc, Structure st, string itemId, int quantity, Inventory target)
        {
            yield return Use(gc, st, () => Transfer(gc.Session, itemId, quantity, target), $"put {quantity} {itemId} into the {st.DefId}");
        }

        private static OpResult Transfer(GameSession s, string itemId, int quantity, Inventory target)
        {
            Inventory bag = s.Player.Inventory;
            while (quantity > 0)
            {
                int slot = -1;
                for (int i = 0; i < bag.SlotCount && slot < 0; i++)
                    if (bag.GetSlot(i).ItemId == itemId) slot = i;
                if (slot < 0) return OpResult.Fail("missing", $"No {itemId} in the backpack.");
                int amount = Math.Min(quantity, bag.GetSlot(slot).Quantity);
                OpResult r = s.Transfer(bag.Id, slot, target.Id, amount);
                if (!r.Ok) return r;
                quantity -= amount;
            }
            return OpResult.Success();
        }

        private void Grant(Inventory bag, string itemId, int quantity)
        {
            if (!bag.TryAdd(itemId, quantity))
            {
                Line($"FAIL: could not grant {quantity} {itemId}");
                return;
            }
            _granted.TryGetValue(itemId, out int n);
            _granted[itemId] = n + quantity;
        }

        /// <summary>A free spot just outside a structure's footprint, within interaction range.</summary>
        private static Vector3 StandSpot(GameController gc, Structure st)
        {
            BuildingDefinition def = gc.Session.Content.Building(st.DefId);
            return FreeSpotNear(gc, st.Position.ToUnity(), 0.5f * Mathf.Max(def.SizeX, def.SizeZ) + 1.2f);
        }

        /// <summary>A spot about <paramref name="distance"/> from <paramref name="center"/> where a person fits, at the centre's floor height.</summary>
        private static Vector3 FreeSpotNear(GameController gc, Vector3 center, float distance)
        {
            int solid = Layers.Mask(Layers.Structure, Layers.Prop, Layers.Resource);
            for (float d = distance; d < distance + 3f; d += 0.5f)
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f;
                    Vector3 p = center + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    // Indoors the floor can sit above the terrain: stand on whatever is below head height.
                    p.y = Physics.Raycast(new Vector3(p.x, center.y + 1.6f, p.z), Vector3.down, out RaycastHit hit, 4f, Layers.SolidMask, QueryTriggerInteraction.Ignore) ? hit.point.y + 0.05f : gc.GroundHeight(p) + 0.05f;
                    if (!Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.6f, 0.35f, solid, QueryTriggerInteraction.Ignore)) return p;
                }
            }
            return Ground(gc, center + new Vector3(distance, 0f, 0f));
        }

        private static void StandAndLook(GameController gc, Vector3 position, Vector3 target)
        {
            Vector3 p = position;
            p.y = Mathf.Max(position.y, gc.GroundHeight(position) + 0.05f);
            gc.Player.Teleport(p);
            Vector3 d = target - p;
            gc.CameraRig.Look(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 14f);
        }

        private static Vector3 Ground(GameController gc, Vector3 p)
        {
            p.y = gc.GroundHeight(p) + 0.05f;
            return p;
        }

        private static string Fmt(Vector3 v) => $"({v.x:0}, {v.z:0})";

        private void Check(bool ok, string text) => Line(ok ? text : "FAIL: " + text);

        // ================================================================== helpers

        private static void SkipTo(GameController gc, double hour)
        {
            SimulationClock clock = gc.Session.World.Clock;
            clock.Advance((hour / 24.0 - clock.TimeOfDay01 + 1.0) % 1.0 * clock.DayLengthSeconds);
        }

        private void LogRendering()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            object Prop(string name) => rp?.GetType().GetProperty(name)?.GetValue(rp);
            Light sun = RenderSettings.sun;
            Line($"Quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', pipeline '{rp?.name}', main light shadows {Prop("supportsMainLightShadows")}, " +
                 $"shadow distance {Prop("shadowDistance")}, cascades {Prop("shadowCascadeCount")}");
            Line(sun == null ? "No RenderSettings.sun" : $"Sun: enabled {sun.enabled}, intensity {sun.intensity:0.00}, shadows {sun.shadows}, rotation {sun.transform.eulerAngles}");
        }

        private void ZombieSpawnNear(GameController gc, float distance, string archetype)
        {
            Vector3 p = gc.Player.transform.position + gc.Player.transform.forward * distance;
            p.y = gc.GroundHeight(p);
            gc.Session.Zombies.Spawn(p.ToSim(), archetype, gc.Session.Rng);
        }

        private IEnumerator MeasureFrames(string label, float seconds)
        {
            var ms = new List<float>();
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                ms.Add(Time.unscaledDeltaTime * 1000f);
            }
            ms.Sort();
            if (ms.Count == 0) yield break;
            Line($"Frame time at {label}: avg {ms.Average():0.0} ms, p95 {ms[(int)(ms.Count * 0.95f)]:0.0} ms, worst {ms[ms.Count - 1]:0.0} ms over {ms.Count} frames");
        }

        private static IEnumerator Seconds(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private IEnumerator Capture(string name)
        {
            string path = Path.Combine(_dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            Line("Captured " + name);
        }

        private void Line(string text)
        {
            Debug.Log("[Tour] " + text);
            _report.AppendLine(text);
        }

        private void Finish()
        {
            Line($"Errors logged: {_errors.Count}");
            foreach (string e in _errors) _report.AppendLine(e);
            File.WriteAllText(Path.Combine(_dir, "tour-report.txt"), _report.ToString());
            _boot.Quit();
        }
    }
}
