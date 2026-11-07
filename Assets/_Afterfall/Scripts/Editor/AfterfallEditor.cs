using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.World;
using Afterfall.Presentation;
using Afterfall.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Afterfall.EditorTools
{
    /// <summary>
    /// Makes the project usable immediately after opening: registers and opens the main scene
    /// and validates content once per editor session.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectBootstrap
    {
        public const string MainScenePath = "Assets/_Afterfall/Scenes/Main.unity";
        private const string SessionKey = "Afterfall.ProjectBootstrap.Done";

        static ProjectBootstrap()
        {
            if (Application.isBatchMode || SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            SessionState.SetBool(SessionKey, true);
            EnsureSceneInBuildSettings();
            Scene active = SceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(active.path) && !active.isDirty;
            if (untitled && File.Exists(MainScenePath) && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorSceneManager.OpenScene(MainScenePath);
            AfterfallMenu.ValidateContent(false);
        }

        public static void EnsureSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == MainScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    public static class AfterfallMenu
    {
        [MenuItem("AFTERFALL/Play Main Scene %#p", priority = 0)]
        public static void PlayMain()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ProjectBootstrap.MainScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("AFTERFALL/Development/Validate Content", priority = 20)]
        public static void ValidateContentMenu() => ValidateContent(true);

        public static bool ValidateContent(bool verbose)
        {
            ContentDatabase db = GameBootstrap.LoadContent(out string error);
            List<string> problems = ContentValidator.Validate(db);
            if (error != null && problems.Count == 0) problems.Add(error);
            if (problems.Count == 0)
            {
                if (verbose) Debug.Log($"[Afterfall] Content OK: {db.Items.Count} items, {db.Recipes.Count} recipes, {db.Buildings.Count} buildings, {db.Techs.Count} technologies, {db.ResourceNodes.Count} resource nodes.");
                return true;
            }
            foreach (string p in problems) Debug.LogError("[Afterfall content] " + p);
            return false;
        }

        [MenuItem("AFTERFALL/Development/Regenerate Main Scene", priority = 21)]
        public static void RegenerateMainScene()
        {
            if (File.Exists(ProjectBootstrap.MainScenePath) &&
                !EditorUtility.DisplayDialog("Regenerate Main Scene", "This replaces Assets/_Afterfall/Scenes/Main.unity with a fresh scene containing only the bootstrap and post-processing volume. Continue?", "Replace", "Cancel"))
                return;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("AFTERFALL Bootstrap").AddComponent<GameBootstrap>();
            // The Volume type lives in the URP/Core package; resolve it by name to keep this assembly package-agnostic.
            System.Type volumeType = System.Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            if (volumeType != null)
            {
                Component volume = new GameObject("Global Volume").AddComponent(volumeType);
                var so = new SerializedObject(volume);
                so.FindProperty("m_IsGlobal").boolValue = true;
                so.FindProperty("sharedProfile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Afterfall/Settings/MainVolumeProfile.asset");
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.SaveScene(scene, ProjectBootstrap.MainScenePath);
            ProjectBootstrap.EnsureSceneInBuildSettings();
            Debug.Log("[Afterfall] Main scene regenerated.");
        }

        [MenuItem("AFTERFALL/Development/Run Simulation Benchmark", priority = 40)]
        public static void Benchmark()
        {
            string report = SimulationBenchmark.Run();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Simulation benchmark", report, "OK");
        }

        [MenuItem("AFTERFALL/Development/Open Save Folder", priority = 60)]
        public static void OpenSaves()
        {
            Directory.CreateDirectory(GameBootstrap.SaveDirectory);
            EditorUtility.RevealInFinder(GameBootstrap.SaveDirectory);
        }

        [MenuItem("AFTERFALL/Development/Delete All Saves", priority = 61)]
        public static void DeleteSaves()
        {
            if (!Directory.Exists(GameBootstrap.SaveDirectory)) return;
            if (!EditorUtility.DisplayDialog("Delete all saves", "Permanently delete every AFTERFALL save in " + GameBootstrap.SaveDirectory + "?", "Delete", "Cancel")) return;
            Directory.Delete(GameBootstrap.SaveDirectory, true);
            Debug.Log("[Afterfall] Saves deleted.");
        }
    }

    /// <summary>Headless simulation load test (also runnable from the command line).</summary>
    public static class SimulationBenchmark
    {
        public static string Run(int machines = 40, double seconds = 300)
        {
            ContentDatabase content = GameBootstrap.LoadContent(out _);
            GameSession s = NewGameFactory.Create(content, 4242, GameSettings.FromPreset(DifficultyPreset.Survival));
            s.World.Tech.Unlocked.Add("metalworking");
            s.World.Tech.Unlocked.Add("electricity");
            Vec3 c = s.Player.Position;
            for (int i = 0; i < machines; i++)
            {
                var pos = new Vec3(c.X + (i % 10) * 3f, c.Y, c.Z + (i / 10) * 3f);
                bool electric = i % 2 == 0;
                BuildingDefinition def = content.Building(electric ? "workshop" : "furnace");
                Structure st = StructureFactory.Create(s.World, def, pos, 0, StructureState.Complete);
                StructureFactory.InitializeComponents(s.World, content, st);
                s.World.AddStructure(st);
                PowerLinker.AutoConnect(s.World, content, st);
                st.Machine.RecipeId = electric ? "gears" : "smelt_scrap";
                st.Machine.Input.AddUpTo(electric ? "iron_ingot" : "scrap_metal", 90);
                st.Machine.Fuel?.AddUpTo("charcoal", 30);
                if (i % 8 == 0)
                {
                    Structure g = StructureFactory.Create(s.World, content.Building("generator"), new Vec3(pos.X, pos.Y, pos.Z - 1.5f), 0, StructureState.Complete);
                    StructureFactory.InitializeComponents(s.World, content, g);
                    s.World.AddStructure(g);
                    PowerLinker.AutoConnect(s.World, content, g);
                    g.Generator.Fuel.AddUpTo("fuel_can", 10);
                }
            }
            var sw = Stopwatch.StartNew();
            int frames = 0;
            for (double t = 0; t < seconds; t += 1.0 / 60.0)
            {
                s.Update(1.0 / 60.0);
                frames++;
            }
            sw.Stop();
            var lines = new List<string>
            {
                $"Simulated {seconds:0}s ({frames} frames at 60 Hz) with {machines} machines, {s.World.Structures.Count} structures, {s.Zombies.AliveCount} infected.",
                $"Total {sw.Elapsed.TotalMilliseconds:0} ms -> {sw.Elapsed.TotalMilliseconds / frames:0.000} ms per frame on this machine."
            };
            foreach (TickScheduler.Entry e in s.Scheduler.Entries)
                lines.Add($"  {e.System.Name}: {e.TickCount} ticks, avg {e.AverageTickMs:0.000} ms, max {e.MaxTickMs:0.00} ms");
            return string.Join("\n", lines);
        }
    }

    /// <summary>Command-line build entry points: Unity -batchmode -quit -executeMethod Afterfall.EditorTools.BuildCommands.BuildMacOS</summary>
    public static class BuildCommands
    {
        public static void BuildMacOS() => Build(BuildTarget.StandaloneOSX, "Builds/macOS/AFTERFALL.app");
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/AFTERFALL.exe");

        [MenuItem("AFTERFALL/Build/macOS", priority = 80)]
        public static void BuildMacMenu() => BuildMacOS();

        [MenuItem("AFTERFALL/Build/Windows (64-bit)", priority = 81)]
        public static void BuildWindowsMenu() => BuildWindows();

        private static void Build(BuildTarget target, string path)
        {
            ProjectBootstrap.EnsureSceneInBuildSettings();
            if (!AfterfallMenu.ValidateContent(true))
            {
                Fail("Content validation failed; build aborted.");
                return;
            }
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = target,
                options = BuildOptions.None
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Fail($"Build failed: {report.summary.result}, {report.summary.totalErrors} error(s).");
                return;
            }
            Debug.Log($"[Afterfall] Build succeeded: {path} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:0}s).");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[Afterfall] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
