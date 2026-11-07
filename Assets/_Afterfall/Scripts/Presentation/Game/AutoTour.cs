using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Scripted smoke run for builds and the editor: <c>-afterfall-tour &lt;dir&gt;</c> starts a new game,
    /// visits the main screens, captures screenshots, measures frame times, writes
    /// <c>tour-report.txt</c> (including every error logged) and quits.
    /// </summary>
    public sealed class AutoTour : MonoBehaviour
    {
        public const string Flag = "-afterfall-tour";
        private GameBootstrap _boot;
        private string _dir;
        private readonly List<string> _errors = new List<string>();
        private readonly StringBuilder _report = new StringBuilder();

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

            gc.Ui.OpenInventory(true);
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
            gc.SetTactical(false);

            SimulationClock clock = gc.Session.World.Clock;
            double toNight = ((22.0 / 24.0) - clock.TimeOfDay01 + 1.0) % 1.0 * clock.DayLengthSeconds;
            clock.Advance(toNight);
            yield return Seconds(2f);
            yield return Capture("08_night");

            gc.Save("tour_test");
            _boot.LoadGame("tour_test");
            float loadTimeout = Time.realtimeSinceStartup + 120f;
            while ((_boot.Game == null || _boot.Game == gc || !_boot.Game.Running) && Time.realtimeSinceStartup < loadTimeout) yield return null;
            Line(_boot.Game != null && _boot.Game != gc && _boot.Game.Running ? "Save and reload: OK" : "FAIL: reload did not complete");
            _boot.Saves.Store.Delete("tour_test");
            yield return Seconds(1.5f);
            yield return Capture("09_after_reload");
            Finish();
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
