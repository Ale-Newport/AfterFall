using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Presentation.UI;
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
            public Structure Gen, Shop, Lamp, Crate;
            public ColonistState Mara;
            public readonly List<EntityId> Expected = new List<EntityId>();
        }

        /// <summary>The tour's output folder when <c>-afterfall-tour</c> is on the command line, otherwise null.</summary>
        public static string RequestedDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, Flag);
            if (i < 0) return null;
            string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "Tour");
            return Path.GetFullPath(dir);
        }

        public static void TryAttach(GameBootstrap boot)
        {
            string dir = RequestedDirectory();
            if (dir == null) return;
            Application.runInBackground = true; // an unfocused player would pause the tour
            var tour = boot.gameObject.AddComponent<AutoTour>();
            tour._boot = boot;
            tour._dir = dir;
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
            string playerSaves = SaveFolderSnapshot();
            Line($"AFTERFALL tour {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {Screen.width}x{Screen.height}");
            yield return Seconds(2f);
            yield return Capture("01_main_menu");
            yield return MainMenuScreens();

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

            yield return Defence(gc, town);
            yield return ColonyLife(gc, town);
            yield return PumpOutpost(gc, town);
            yield return PowerGrid(gc, town);

            SkipTo(gc, 22.0);
            Time.timeScale = 4f;
            float sleepWait = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - sleepWait < 20f && town.Mara.Activity != ColonistActivity.Resting) yield return null;
            Time.timeScale = 1f;
            Check(town.Mara.Activity == ColonistActivity.Resting, $"At night {town.Mara.Name} sleeps in her bed ({town.Mara.Activity}: {town.Mara.Status})");
            StandAndLook(gc, town.Site + new Vector3(-9f, 0f, -7f), town.Site);
            yield return Seconds(2f);
            yield return Capture("21_night_at_base");
            yield return MeasureFrames("settlement at night", 3f);

            yield return DeathAndRespawn(gc);
            gc.Ui.OpenPause();
            yield return Seconds(0.5f);
            Check(gc.Session.Paused, "The pause menu pauses the simulation");
            yield return Capture("24_pause_menu");
            gc.Ui.CloseWindow();
            yield return Seconds(0.3f);
            Check(!gc.Session.Paused, "Closing the pause menu resumes the simulation");
            bool fps = gc.Settings.ShowFps;
            gc.Settings.ShowFps = true;
            yield return Seconds(1f);
            yield return Capture("25_f3_overlay");
            gc.Settings.ShowFps = fps;

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
                    int colonists = w.Colonists.Values.Count(x => x.Recruited);
                bool batteries = w.Structures.Values.Any(x => x.Battery != null && x.Battery.StoredJoules > 0);
                Line($"{(sameStructures && recruited && crops && recipe && linked && colonists == 2 && batteries ? "Save and reload: OK" : "FAIL: save and reload")} " +
                     $"(structures {w.Structures.Count}/{structures}, colonists recruited {colonists}, crop planted {crops}, workshop recipe kept {recipe}, power link kept {linked}, battery charge kept {batteries})");
            }
            _boot.Saves.Store.Delete("tour_test");
            yield return Seconds(1.5f);
            yield return Capture("26_after_reload");
            Check(SaveFolderSnapshot() == playerSaves, $"The tour left the player's save folder untouched ({GameBootstrap.SaveDirectory})");
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
            Structure lamp = null;
            if (shop != null) yield return Construct(gc, "lamp", shop.Position.ToUnity(), x => lamp = x, 2.5f);
            Grant(bag, "seed_potato", 1);
            yield return Seconds(1f);
            Check(s.World.Tech.IsUnlocked("agriculture"), "Seeds unlock Agriculture");
            yield return Construct(gc, "planter_bed", town.Site + new Vector3(2f, 0f, -5f), x => plotA = x);
            yield return Construct(gc, "planter_bed", town.Site + new Vector3(5f, 0f, -5f), x => plotB = x);
            if (bed == null || crate == null || furnace == null || gen == null || shop == null || lamp == null || plotA == null || plotB == null)
            {
                Line("FAIL: the settlement could not be completed");
                bag.MaxWeight = maxWeight;
                yield break;
            }
            town.Generator = gen.Id;
            town.Workshop = shop.Id;
            town.Plot = plotB.Id;
            town.Gen = gen;
            town.Shop = shop;
            town.Lamp = lamp;
            town.Crate = crate;
            town.Mara = survivor;
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

        private IEnumerator Construct(GameController gc, string defId, Vector3 near, Action<Structure> done, float minRadius = 0f, float maxRadius = 26f)
        {
            Structure st = PlanNear(gc, defId, near, minRadius, out string reason, maxRadius);
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

        private void LogZombie(GameController gc, ZombieState z)
        {
            ZombieView v = gc.Sync.ZombieViewOf(z.Id);
            string agent = v == null ? "no view" : !v.OnNavMesh ? "off NavMesh" : $"path {v.Agent.pathStatus}, remaining {v.Agent.remainingDistance:0.0} m, speed {v.Agent.velocity.magnitude:0.0}";
            float toPlayer = Vector3.Distance(z.Position.ToUnity(), gc.Player.transform.position);
            Line($"  infected: {z.Mode} at {Fmt(z.Position.ToUnity())}, {toPlayer:0.0} m from the survivor, health {z.Health:0}, stall {z.StallTime:0.0} s, breaching {(z.BreachTarget.IsValid ? gc.Session.World.Structures.TryGetValue(z.BreachTarget, out Structure t) ? t.DefId : "?" : "nothing")}; {agent}");
        }

        private void LogColonist(GameController gc, ColonistState c)
        {
            ColonistView v = gc.Sync.ColonistViewOf(c.Id);
            string agent = v == null ? "no view" : !v.OnNavMesh ? $"off NavMesh (agent enabled {v.Agent.enabled})" :
                $"path {v.Agent.pathStatus}, pending {v.Agent.pathPending}, remaining {v.Agent.remainingDistance:0.0} m, speed {v.Agent.velocity.magnitude:0.0}, stopped {v.Agent.isStopped}";
            Line($"  {c.Name}: {c.Activity} \"{c.Status}\" at {Fmt(c.Position.ToUnity())} y {c.Position.Y:0.0} (ground {gc.GroundHeight(c.Position.ToUnity()):0.0}), target {(c.HasMoveTarget ? Fmt(c.MoveTarget.ToUnity()) : "none")}; {agent}");
        }

        private static Structure PlanNear(GameController gc, string defId, Vector3 near, float minRadius, out string reason, float maxRadius = 26f)
        {
            reason = "unknown building";
            BuildingDefinition def = gc.Session.Content.Building(defId);
            if (def == null) return null;
            float stepSize = maxRadius < 2f ? 0.25f : 2f;
            for (float r = minRadius; r <= maxRadius; r += stepSize)
            {
                int steps = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / stepSize);
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

        // ================================================================== more chapters

        private IEnumerator MainMenuScreens()
        {
            UiManager ui = _boot.Ui;
            ui.ReplaceMainMenuContent(MenuScreens.NewGame(ui));
            yield return Seconds(0.4f);
            yield return Capture("01b_new_game");
            ui.ReplaceMainMenuContent(MenuScreens.LoadGame(ui));
            yield return Seconds(0.4f);
            yield return Capture("01c_load_game");
            ui.ShowMainMenu();
            ui.OpenSettings();
            yield return Seconds(0.4f);
            yield return Capture("01d_settings");
            ui.CloseWindow();
            ui.OpenHelp();
            yield return Seconds(0.4f);
            yield return Capture("01e_controls");
            ui.CloseWindow();
            yield return Seconds(0.3f);
        }

        /// <summary>A shelter snapped onto a foundation, a barricade the infected batter, repair, demolish and melee.</summary>
        private IEnumerator Defence(GameController gc, Settlement town)
        {
            GameSession s = gc.Session;
            Inventory bag = s.Player.Inventory;
            Structure foundation = null, doorway = null, roof = null, barricade = null;
            // Far enough from the base that the infected comes for the survivor, not the colonists.
            yield return Construct(gc, "foundation", town.Site + new Vector3(-6f, 0f, -45f), x => foundation = x);
            if (foundation == null) yield break;
            Vector3 c = foundation.Position.ToUnity();
            var walls = new List<Structure>();
            // The doorway faces away from the base (-Z), so infected arrive from the far side.
            foreach (Vector3 edge in new[] { new Vector3(1.4f, 0f, 0f), new Vector3(-1.4f, 0f, 0f), new Vector3(0f, 0f, 1.4f) })
                yield return Construct(gc, "wall", c + edge, x => walls.Add(x), 0f, 0.5f);
            yield return Construct(gc, "doorway", c + new Vector3(0f, 0f, -1.4f), x => doorway = x, 0f, 0.5f);
            yield return Construct(gc, "roof", c, x => roof = x, 0f, 0.5f);
            Check(walls.Count == 3 && doorway != null && roof != null, $"Shelter: {walls.Count} walls and {(doorway != null ? "a" : "no")} doorway snapped to the foundation, {(roof != null ? "a" : "no")} roof on top");
            if (doorway == null) yield break;
            Vector3 door = doorway.Position.ToUnity();
            Vector3 outward = door - c;
            outward.y = 0f;
            outward.Normalize();
            yield return Seconds(0.5f); // let the new parts carve the NavMesh
            Check(PathStatus(c + Vector3.up * 0.5f, door + outward * 3f) == UnityEngine.AI.NavMeshPathStatus.PathComplete, "Agents can walk into the shelter through the doorway");
            StandAndLook(gc, door + outward * 6f + new Vector3(-3f, 0f, 0f), c);
            yield return Seconds(0.8f);
            yield return Capture("16_shelter");

            yield return Construct(gc, "barricade", door + outward * 0.45f, x => barricade = x, 0f, 1.2f);
            if (barricade == null) yield break;
            yield return Seconds(0.5f);
            Check(PathStatus(c + Vector3.up * 0.5f, door + outward * 3f) != UnityEngine.AI.NavMeshPathStatus.PathComplete, "The barricade seals the doorway");

            // Hide inside, make noise and let an infected find the barricade.
            gc.Player.Teleport(c + Vector3.up * 0.45f);
            gc.CameraRig.Look(Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg, 8f);
            ZombieState z = s.Zombies.Spawn((door + outward * 12f).ToSim(), "wanderer", s.Rng);
            float maxHealth = s.Content.Building("barricade").MaxHealth;
            Time.timeScale = 2f;
            float start = Time.realtimeSinceStartup, nextNoise = 0f;
            var shelter = new List<Structure>(walls) { doorway, barricade };
            float nextLog = start;
            while (Time.realtimeSinceStartup - start < 40f && shelter.All(x => x.Health >= s.Content.Building(x.DefId).MaxHealth) && !z.IsDead)
            {
                if (Time.realtimeSinceStartup >= nextNoise)
                {
                    nextNoise = Time.realtimeSinceStartup + 2f;
                    s.EmitNoise(s.Player.Position, 30f, "tour");
                }
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    nextLog += 4f;
                    LogZombie(gc, z);
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Structure hit = shelter.FirstOrDefault(x => x.Health < s.Content.Building(x.DefId).MaxHealth);
            Check(hit != null, $"The infected battered the shelter ({(hit != null ? $"{s.Content.Building(hit.DefId).Name} {hit.Health:0}/{s.Content.Building(hit.DefId).MaxHealth:0}" : "nothing damaged")}, infected {z.Mode})");
            yield return Capture("17_breach");

            Grant(bag, "hammer", 1);
            Structure damaged = hit ?? barricade;
            foreach (ItemAmount a in s.Construction.RepairCost(damaged)) Grant(bag, a.ItemId, a.Quantity);
            OpResult repaired = s.Repair(damaged.Id);
            Check(repaired.Ok && damaged.Health >= s.Content.Building(damaged.DefId).MaxHealth, "Repair with a hammer: " + repaired.Message);
            int wood = bag.Count("wood");
            OpResult demolished = s.Demolish(barricade.Id);
            Check(demolished.Ok && !s.World.Structures.ContainsKey(barricade.Id), $"Demolish the barricade: {demolished.Message} (wood {wood} -> {bag.Count("wood")})");

            Grant(bag, "stone_axe", 1);
            for (int i = 0; i < PlayerState.HotbarSize; i++)
                if (bag.GetSlot(i).ItemId == "stone_axe") s.SelectHotbar(i);
            int swings = 0;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 30f && !z.IsDead)
            {
                if (Vector3.Distance(z.Position.ToUnity(), gc.Player.transform.position) < 2.6f && s.MeleeAttack(new[] { z.Id }).Code == "ok") swings++;
                else if (Time.realtimeSinceStartup >= nextNoise)
                {
                    nextNoise = Time.realtimeSinceStartup + 2f;
                    s.EmitNoise(s.Player.Position, 30f, "tour");
                }
                yield return null;
            }
            Check(z.IsDead, $"Killed the infected with a stone axe after it came through the doorway ({swings} swings, survivor health {s.Player.Vitals.Health:0})");
            RemoveZombies(gc);
            s.Player.Vitals.Health = s.Player.Vitals.MaxHealth;
        }

        /// <summary>Fleeing, eating, a second survivor walking across the valley.</summary>
        private IEnumerator ColonyLife(GameController gc, Settlement town)
        {
            GameSession s = gc.Session;
            ColonistState mara = town.Mara;
            Vector3 at = mara.Position.ToUnity();
            Vector3 away = at - town.Site;
            away.y = 0f;
            Vector3 spawn = at + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right) * 7f;
            ZombieState z = s.Zombies.Spawn(Ground(gc, spawn).ToSim(), "wanderer", s.Rng);
            z.Yaw = Mathf.Atan2(at.x - spawn.x, at.z - spawn.z) * Mathf.Rad2Deg; // looking her way, so it sees her
            float start = Time.realtimeSinceStartup, nextLog = start;
            bool fled = false;
            while (Time.realtimeSinceStartup - start < 15f && !fled && mara.Vitals.Health > 50f)
            {
                fled = mara.Status.StartsWith("Fleeing");
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    nextLog += 1f;
                    LogZombie(gc, z);
                    LogColonist(gc, mara);
                }
                yield return null;
            }
            Check(fled, $"{mara.Name} flees from an infected that comes for her (infected {z.Mode}, her health {mara.Vitals.Health:0})");
            RemoveZombies(gc);
            yield return Seconds(1f);

            int potatoes = town.Crate.Storage.Count("potato");
            mara.Vitals.Hunger = 20f; // tour shortcut: a hungry colonist
            Time.timeScale = 4f;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 20f && mara.Vitals.Hunger < 30f) yield return null;
            Time.timeScale = 1f;
            Check(mara.Vitals.Hunger >= 30f, $"{mara.Name} ate from the crate when hungry (hunger 20 -> {mara.Vitals.Hunger:0}, potatoes {potatoes} -> {town.Crate.Storage.Count("potato")})");

            Structure bed2 = null;
            yield return Construct(gc, "bed", town.Site + new Vector3(-3f, 0f, 2f), x => bed2 = x);
            ColonistState eli = s.World.Colonists.Values.FirstOrDefault(c => !c.Recruited);
            if (eli == null || bed2 == null)
            {
                Line("FAIL: no second survivor or bed");
                yield break;
            }
            Vector3 hideout = eli.Position.ToUnity();
            StandAndLook(gc, FreeSpotNear(gc, hideout, 1.6f), hideout);
            yield return Seconds(0.3f);
            OpResult joined = s.Recruit(eli.Id);
            Check(joined.Ok, $"Recruit {eli.Name}: {joined.Message}");
            float distance = Vector3.Distance(Flat(hideout), Flat(town.Site));
            Time.timeScale = 4f;
            start = Time.realtimeSinceStartup;
            nextLog = start;
            while (Time.realtimeSinceStartup - start < 60f && Vector3.Distance(Flat(eli.Position.ToUnity()), Flat(town.Site)) > 25f)
            {
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    nextLog += 15f;
                    LogColonist(gc, eli);
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Check(Vector3.Distance(Flat(eli.Position.ToUnity()), Flat(town.Site)) <= 25f, $"{eli.Name} walked {distance:0} m across the valley to the settlement ({Time.realtimeSinceStartup - start:0} s at 4x)");
            gc.SetTactical(true);
            gc.CameraRig.Tactical.Focus(town.Site, 20f);
            yield return Seconds(1.5f);
            yield return Capture("18_two_colonists");
            gc.SetTactical(false);
        }

        /// <summary>A generator and an electric pump on the lake shore filling bottles and irrigating a bed.</summary>
        private IEnumerator PumpOutpost(GameController gc, Settlement town)
        {
            GameSession s = gc.Session;
            Inventory bag = s.Player.Inventory;
            WorldLayout layout = s.World.Layout;
            Vector3 lake = layout.LakeCenter.ToUnity();
            Vector3 toTown = Flat(town.Site - lake).normalized;
            Vector3 shore = Ground(gc, lake + toTown * (layout.LakeRadius + 2f));
            Check(s.World.Tech.IsUnlocked("water_works"), "Machining gears unlocked Water Works");
            Structure pump = null, gen = null, plot = null;
            yield return Construct(gc, "water_pump", shore, x => pump = x);
            if (pump == null) yield break;
            yield return Construct(gc, "generator", pump.Position.ToUnity(), x => gen = x, 3f, 6f);
            yield return Construct(gc, "planter_bed", pump.Position.ToUnity(), x => plot = x, 4f, 9f);
            if (gen == null || plot == null) yield break;
            Check(s.World.HasPowerLink(gen.Id, pump.Id), "The pump connected itself to its generator");
            yield return Use(gc, pump, () => s.SetMachineRecipe(pump.Id, "pump_water"), "set the pump recipe");
            Grant(bag, "empty_bottle", 4);
            yield return Deposit(gc, pump, "empty_bottle", 4, pump.Machine.Input);
            Grant(bag, "fuel_can", 1);
            yield return Deposit(gc, gen, "fuel_can", 1, gen.Generator.Fuel);
            Grant(bag, "seed_potato", 1);
            yield return Use(gc, plot, () => s.PlantCrop(plot.Id), "plant at the pump");
            Time.timeScale = 4f;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 15f && (pump.Machine.Output.Count("water_bottle") == 0 || !plot.Crop.Irrigated)) yield return null;
            Time.timeScale = 1f;
            Check(pump.Machine.Output.Count("water_bottle") > 0, $"The powered pump filled {pump.Machine.Output.Count("water_bottle")} bottles ({UiKit.StatusText(pump.Machine)})");
            Check(plot.Crop.Irrigated && plot.Crop.Water >= 99f, $"The pump irrigates the planter bed {Vector3.Distance(Flat(pump.Position.ToUnity()), Flat(plot.Position.ToUnity())):0.0} m away (water {plot.Crop.Water:0})");
            StandAndLook(gc, pump.Position.ToUnity() + Flat(pump.Position.ToUnity() - lake).normalized * 8f + new Vector3(2f, 0f, 2f), pump.Position.ToUnity());
            yield return Seconds(1f);
            yield return Capture("19_pump_outpost");
        }

        /// <summary>Poles and cables, a battery bank, three workshops and a lamp shed at night on battery power.</summary>
        private IEnumerator PowerGrid(GameController gc, Settlement town)
        {
            GameSession s = gc.Session;
            Inventory bag = s.Player.Inventory;
            Structure gen = town.Gen;
            Vector3 g = gen.Position.ToUnity();
            Structure pole = null, farLamp = null, battery = null;
            yield return Construct(gc, "power_pole", g + new Vector3(-9f, 0f, 0f), x => pole = x, 0f, 4f);
            if (pole != null) yield return Construct(gc, "lamp", pole.Position.ToUnity() + new Vector3(-9f, 0f, 0f), x => farLamp = x, 0f, 4f);
            if (pole == null || farLamp == null) yield break;
            yield return Seconds(0.5f);
            Check(SameGrid(s, gen, farLamp), $"A power pole carries the generator's grid to a lamp {Vector3.Distance(g, farLamp.Position.ToUnity()):0} m away");
            OpResult cut = s.DisconnectPower(pole.Id, farLamp.Id);
            yield return Seconds(0.5f);
            Check(cut.Ok && !SameGrid(s, gen, farLamp), "Cutting the cable isolates the lamp: " + cut.Message);
            OpResult linked = s.ConnectPower(pole.Id, farLamp.Id);
            yield return Seconds(0.5f);
            Check(linked.Ok && SameGrid(s, gen, farLamp), "Reconnecting the cable: " + linked.Message);

            yield return Construct(gc, "battery_bank", g, x => battery = x, 2.5f, 7f);
            var shops = new List<Structure> { town.Shop };
            for (int i = 0; i < 2; i++) yield return Construct(gc, "workshop", g, x => shops.Add(x), 3.5f, 8f);
            if (battery == null || shops.Count < 3) yield break;
            yield return Seconds(0.5f); // the grid is recomputed on the next power tick
            Check(SameGrid(s, gen, battery) && shops.All(x => SameGrid(s, gen, x)), "The battery bank and three workshops share the generator's grid");
            foreach (Structure shop in shops)
            {
                yield return Use(gc, shop, () => s.SetMachineRecipe(shop.Id, "gears"), "set a workshop recipe");
                Grant(bag, "iron_ingot", 20);
                yield return Deposit(gc, shop, "iron_ingot", 20, shop.Machine.Input);
            }
            Grant(bag, "fuel_can", 2);
            yield return Deposit(gc, gen, "fuel_can", 2, gen.Generator.Fuel);
            Time.timeScale = 4f;
            yield return Seconds(12f);
            Time.timeScale = 1f;
            Check(battery.Battery.StoredJoules > 1000.0, $"Surplus generator power charged the battery ({battery.Battery.StoredJoules / 1000.0:0} kJ)");

            SkipTo(gc, 21.0);
            yield return Seconds(1f);
            s.ToggleEnabled(gen.Id);
            yield return Seconds(1.5f);
            bool shopsRun = shops.All(x => x.Power.Powered);
            bool lampShed = town.Lamp.Power.Reason.StartsWith("Insufficient power");
            Check(shopsRun && lampShed, $"Generator off at night: the battery runs the three workshops ({shops.Count(x => x.Power.Powered)}/3 powered) and the lamp is shed first ({town.Lamp.Power.Reason})");
            gc.SetTactical(true);
            gc.CameraRig.Tactical.Focus(g, 20f);
            gc.Tactical.Select(town.Lamp.Id);
            yield return Seconds(1.5f);
            yield return Capture("20_power_shedding");
            s.ToggleEnabled(gen.Id);
            yield return Seconds(1.5f);
            Check(town.Lamp.Power.Powered && shops.All(x => x.Power.Powered), $"Generator back on: everything powered (lamp: {town.Lamp.Power.Reason}; generator enabled {gen.Enabled}, {gen.Generator.Status}, fuel {Units(gen.Generator.Fuel)} items, buffer {gen.Generator.BufferJoules / 1000:0} kJ)");
            gc.Tactical.Select(EntityId.None);
            gc.SetTactical(false);
        }

        private IEnumerator DeathAndRespawn(GameController gc)
        {
            GameSession s = gc.Session;
            Inventory bag = s.Player.Inventory;
            int carried = Units(bag);
            s.DamagePlayer(1000f, "the tour");
            yield return Seconds(1.5f);
            Check(gc.IsDead, "The survivor died");
            yield return Capture("22_death_screen");
            WorldDrop drop = s.World.Drops.Values.FirstOrDefault(d => d.IsBelongings);
            Check(drop != null && Units(drop.Contents) == carried && Units(bag) == 0, $"All {carried} carried items were dropped where the survivor fell");
            OpResult respawned = s.Respawn();
            if (respawned.Ok) gc.OnRespawned();
            Check(respawned.Ok, "Respawn: " + respawned.Message);
            yield return Seconds(1.5f);
            if (drop == null) yield break;
            Vector3 dropAt = drop.Position.ToUnity();
            Vector3 p = gc.Player.transform.position;
            gc.CameraRig.Look(Mathf.Atan2(dropAt.x - p.x, dropAt.z - p.z) * Mathf.Rad2Deg, 4f);
            yield return Seconds(1f);
            yield return Capture("23_belongings_beacon");
            Line($"Respawned {Vector3.Distance(Flat(p), Flat(dropAt)):0} m from the belongings");
            StandAndLook(gc, FreeSpotNear(gc, dropAt, 1.2f), dropAt);
            yield return Seconds(0.3f);
            OpResult picked = s.PickUpDrop(drop.Id);
            Check(picked.Ok && Units(bag) == carried, $"Recovered the belongings: {picked.Message}");
        }

        private static UnityEngine.AI.NavMeshPathStatus PathStatus(Vector3 from, Vector3 to)
        {
            if (!UnityEngine.AI.NavMesh.SamplePosition(from, out UnityEngine.AI.NavMeshHit a, 1.5f, UnityEngine.AI.NavMesh.AllAreas) ||
                !UnityEngine.AI.NavMesh.SamplePosition(to, out UnityEngine.AI.NavMeshHit b, 1.5f, UnityEngine.AI.NavMesh.AllAreas))
                return UnityEngine.AI.NavMeshPathStatus.PathInvalid;
            var path = new UnityEngine.AI.NavMeshPath();
            UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, path);
            return path.status;
        }

        private static bool SameGrid(GameSession s, Structure a, Structure b)
        {
            var net = s.Power.NetworkOf(a.Id);
            return net != null && net.Members.Contains(b.Id);
        }

        private static void RemoveZombies(GameController gc)
        {
            foreach (EntityId id in gc.Session.World.Zombies.Keys.ToList()) gc.Session.World.Zombies.Remove(id);
            gc.Session.World.MarkChanged();
        }

        private static int Units(Inventory inv)
        {
            int n = 0;
            for (int i = 0; i < inv.SlotCount; i++) n += inv.GetSlot(i).Quantity;
            return n;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static string SaveFolderSnapshot()
        {
            if (!Directory.Exists(GameBootstrap.SaveDirectory)) return "";
            return string.Join("|", Directory.GetFiles(GameBootstrap.SaveDirectory).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => $"{Path.GetFileName(f)}@{File.GetLastWriteTimeUtc(f).Ticks}"));
        }

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
