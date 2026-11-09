using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Presentation.UI;
using Afterfall.Services;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Visual review mode for builds: <c>-afterfall-gallery &lt;dir&gt;</c> photographs every model in a
    /// lit studio (structures, resource nodes, world props, characters and held items), renders the
    /// item icon sheet, then starts a game and photographs the camp, the backpack and every point of
    /// interest at several times of day. Screenshots land in &lt;dir&gt;/&lt;section&gt;/, a report with
    /// timings and every error logged in &lt;dir&gt;/gallery-report.txt; then the game quits.
    /// <c>-afterfall-gallery-sections a,b</c> limits it to some of: structures, nodes, props,
    /// characters, icons, world.
    /// </summary>
    public sealed class ModelGallery : MonoBehaviour
    {
        public const string Flag = "-afterfall-gallery";
        public const string SectionsFlag = "-afterfall-gallery-sections";
        private static readonly string[] AllSections = { "structures", "nodes", "props", "characters", "icons", "world" };

        private GameBootstrap _boot;
        private string _dir;
        private HashSet<string> _sections;
        private Camera _cam;
        private Light _sun, _moon;
        private Transform _studio;
        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<string> _errors = new List<string>();
        private int _shots;

        public static string RequestedDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, Flag);
            if (i < 0) return null;
            string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "Gallery");
            return Path.GetFullPath(dir);
        }

        public static void TryAttach(GameBootstrap boot)
        {
            string dir = RequestedDirectory();
            if (dir == null) return;
            // Several galleries may run side by side; an unfocused player must keep running.
            Application.runInBackground = true;
            var g = boot.gameObject.AddComponent<ModelGallery>();
            g._boot = boot;
            g._dir = dir;
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, SectionsFlag);
            g._sections = new HashSet<string>(i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',').Select(s => s.Trim()) : AllSections);
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
            Line($"AFTERFALL gallery {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}, sections {string.Join(",", _sections)}");
            _boot.Ui.HideMainMenu();
            BuildStudio();
            float t0 = Time.realtimeSinceStartup;
            ProceduralTextures.PrewarmCatalogue();
            Line($"Prewarmed the surface catalogue in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            yield return null;

            if (_sections.Contains("structures")) yield return Structures();
            if (_sections.Contains("nodes")) yield return Nodes();
            if (_sections.Contains("props")) yield return Props();
            if (_sections.Contains("characters")) yield return Characters();
            if (_sections.Contains("icons")) IconSheet();
            if (_sections.Contains("world")) yield return World();
            Finish();
        }

        // ------------------------------------------------------------ studio

        private void BuildStudio()
        {
            _studio = new GameObject("GalleryStudio").transform;
            DayNightCycle.CreateLights(_studio, out _sun, out _moon);
            // Not under the studio: the camera keeps working for world shots after the studio is hidden.
            var camGo = new GameObject("GalleryCamera");
            _cam = camGo.AddComponent<Camera>();
            _cam.depth = 50;
            _cam.fieldOfView = 30f;
            CameraSetup.EnablePostProcessing(_cam);
            Shapes.Part(_studio, "Ground", MeshFactory.Box(new Vector3(400f, 0.2f, 400f)), MaterialLibrary.Surface(Art.Surface.Grass, new Color(0.38f, 0.45f, 0.26f)),
                new Vector3(0f, -0.1f, 0f), Vector3.one);
            Daylight(0.42f);
        }

        private void Daylight(float timeOfDay)
        {
            float elevation = Mathf.Sin((timeOfDay - 0.25f) * Mathf.PI * 2f);
            DayNightCycle.Apply(timeOfDay, Mathf.Clamp01(elevation * 3f + 0.2f), _sun, _moon, _cam);
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 900f;
        }

        private GameObject Subject(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_studio, false);
            return go;
        }

        /// <summary>Frames everything rendered under <paramref name="subject"/> from the front right and saves a screenshot.</summary>
        private IEnumerator Shoot(string name, GameObject subject, float yaw = 35f, float pitch = 20f, float zoom = 1f)
        {
            Bounds b = RenderBounds(subject);
            float radius = Mathf.Max(0.3f, b.extents.magnitude);
            float dist = radius / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f / zoom;
            Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            _cam.transform.position = b.center + dir * dist;
            _cam.transform.rotation = Quaternion.LookRotation(-dir);
            _cam.nearClipPlane = Mathf.Max(0.05f, dist - radius * 3f);
            _cam.farClipPlane = dist + radius * 4f + 400f;
            yield return Capture(name);
        }

        private IEnumerator Capture(string name)
        {
            string path = Path.Combine(_dir, name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            yield return null;
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            _shots++;
        }

        private static Bounds RenderBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ sections

        private IEnumerator Structures()
        {
            int n = 0;
            foreach (BuildingDefinition def in _boot.Content.Buildings.Values.OrderBy(d => d.Id))
            {
                GameObject go = Subject(def.Id);
                StructureVisuals.Build(go.transform, def, null, true);
                Illuminate(go, def);
                yield return Shoot("structures/" + def.Id, go);
                Destroy(go);
                n++;
            }
            // Placement ghosts and a blueprint, side by side.
            BuildingDefinition[] sample = _boot.Content.Buildings.Values.Where(d => d.Buildable).OrderBy(d => d.Id).Take(3).ToArray();
            GameObject row = Subject("ghosts");
            Material[] mats = { MaterialLibrary.Ghost(StructureVisuals.GhostValid), MaterialLibrary.Ghost(StructureVisuals.GhostInvalid), MaterialLibrary.Ghost(StructureVisuals.BlueprintTint) };
            for (int i = 0; i < sample.Length; i++)
            {
                GameObject g = new GameObject(sample[i].Id);
                g.transform.SetParent(row.transform, false);
                g.transform.localPosition = new Vector3(i * 4f, 0f, 0f);
                StructureVisuals.Build(g.transform, sample[i], mats[i], false);
            }
            yield return Shoot("structures/_ghosts", row, 0f);
            Destroy(row);
            Line($"Structures: {n} models");
        }

        /// <summary>Shows machines working and lamps lit, the way StructureView does in the game.</summary>
        private static void Illuminate(GameObject go, BuildingDefinition def)
        {
            var view = go.AddComponent<StructureView>();
            view.Def = def;
            view.BuiltComplete = true;
            view.CacheParts();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Fire") MaterialLibrary.SetEmission(r.sharedMaterial, new Color(1f, 0.45f, 0.1f) * 2.8f);
                if (r.name == "LampHead") MaterialLibrary.SetEmission(r.sharedMaterial, new Color(1f, 0.85f, 0.55f) * 2.5f);
            }
            foreach (Light l in go.GetComponentsInChildren<Light>()) l.intensity = 2.2f;
        }

        private IEnumerator Nodes()
        {
            int n = 0;
            foreach (ResourceNodeDefinition def in _boot.Content.ResourceNodes.Values.OrderBy(d => d.Id))
            {
                foreach (bool depleted in new[] { false, true })
                {
                    var state = new ResourceNodeState { Id = new EntityId(1000 + n), DefId = def.Id, Depleted = depleted, RemainingHarvests = depleted ? 0 : def.Harvests };
                    GameObject go = Subject(def.Id);
                    NodeVisuals.Build(go.transform, def, state);
                    yield return Shoot("nodes/" + def.Id + (depleted ? "_depleted" : ""), go);
                    Destroy(go);
                    n++;
                }
            }
            // Variants side by side (the visual variant comes from the entity id).
            foreach (string visual in new[] { "tree_pine", "tree_birch", "rock", "bush" })
            {
                ResourceNodeDefinition def = _boot.Content.ResourceNodes.Values.FirstOrDefault(d => d.Visual == visual);
                if (def == null) continue;
                GameObject row = Subject(visual + "_variants");
                for (int v = 0; v < 4; v++)
                {
                    var g = new GameObject("v" + v);
                    g.transform.SetParent(row.transform, false);
                    g.transform.localPosition = new Vector3(v * (visual.StartsWith("tree") ? 4.5f : 2.2f), 0f, 0f);
                    NodeVisuals.Build(g.transform, def, new ResourceNodeState { Id = new EntityId(v), DefId = def.Id, RemainingHarvests = def.Harvests });
                }
                yield return Shoot("nodes/_" + visual + "_variants", row, 15f, 12f);
                Destroy(row);
            }
            Line($"Resource nodes: {n} shots");
        }

        private IEnumerator Props()
        {
            WorldLayout layout = WorldGenerator.Generate(20261107);
            var seen = new Dictionary<string, int>();
            int n = 0;
            foreach (PropPlacement p in layout.Props.OrderBy(p => p.Kind).ThenBy(p => p.Variant))
            {
                seen.TryGetValue(p.Kind, out int count);
                if (count >= 2) continue;
                seen[p.Kind] = count + 1;
                var copy = new PropPlacement { Kind = p.Kind, Size = p.Size, Variant = p.Variant, PoiId = p.PoiId, Yaw = 0f };
                GameObject go = PropBuilder.Build(copy);
                if (go == null) continue;
                go.transform.SetParent(_studio, true);
                yield return Shoot($"props/{p.Kind}_{count}", go);
                Destroy(go);
                n++;
            }
            Line($"Props: {n} shots of {seen.Count} kinds");
        }

        private IEnumerator Characters()
        {
            var rigs = new List<(string name, HumanoidRig rig)>();
            GameObject lineup = Subject("lineup");
            void Add(string name, HumanoidRig.Palette palette, bool infected, float x)
            {
                var holder = new GameObject(name).transform;
                holder.SetParent(lineup.transform, false);
                holder.localPosition = new Vector3(x, 0f, 0f);
                rigs.Add((name, HumanoidRig.Create(holder, palette, infected)));
            }
            Add("survivor", PlayerCharacter.SurvivorPalette, false, 0f);
            for (int v = 0; v < 3; v++) Add("colonist_" + v, ColonistView.PaletteFor(v), false, 1.2f + v * 1.2f);
            for (int v = 0; v < 3; v++) Add("infected_" + v, ZombieView.PaletteFor(v), true, 4.8f + v * 1.2f);
            foreach (var (_, rig) in rigs) rig.Animate(0f, false, true, 0.02f);
            yield return Shoot("characters/_lineup", lineup, 20f, 8f);
            foreach ((string name, HumanoidRig rig) in rigs)
            {
                yield return Shoot("characters/" + name, rig.gameObject, 25f, 6f);
                yield return Shoot("characters/" + name + "_back", rig.gameObject, 205f, 10f);
            }
            // Walking and running poses.
            HumanoidRig survivor = rigs[0].rig, infected = rigs[4].rig;
            for (int f = 0; f < 6; f++)
            {
                survivor.Animate(3.5f, false, true, 0.05f);
                infected.Animate(1.2f, false, true, 0.05f);
            }
            yield return Shoot("characters/_walking", lineup, 70f, 8f);
            Destroy(lineup);

            // Every tool and weapon in hand.
            ItemDefinition[] tools = _boot.Content.Items.All.Where(i => i.Tool != null && i.Tool.Type != ToolType.None).OrderBy(i => i.Id).ToArray();
            GameObject held = Subject("held");
            for (int i = 0; i < tools.Length; i++)
            {
                var holder = new GameObject(tools[i].Id).transform;
                holder.SetParent(held.transform, false);
                holder.localPosition = new Vector3(i * 1.1f, 0f, 0f);
                HumanoidRig rig = HumanoidRig.Create(holder, PlayerCharacter.SurvivorPalette, false);
                rig.Animate(0f, false, true, 0.02f);
                rig.SetHeldItem(tools[i]);
            }
            yield return Shoot("characters/_held_items", held, 30f, 10f, 1.3f);
            Destroy(held);
            Line($"Characters: {rigs.Count} rigs, {tools.Length} held items");
        }

        /// <summary>Every item icon on slot-coloured tiles, as the backpack shows them (plus a half-size row).</summary>
        private void IconSheet()
        {
            ItemDefinition[] items = _boot.Content.Items.All.OrderBy(i => i.Category).ThenBy(i => i.Id).ToArray();
            const int cols = 9, tile = 136;
            int rows = (items.Length + cols - 1) / cols;
            var sheet = new Texture2D(cols * tile, rows * tile, TextureFormat.RGBA32, false);
            var bg = new Color32(40, 42, 38, 255);
            var px = Enumerable.Repeat(bg, sheet.width * sheet.height).ToArray();
            for (int k = 0; k < items.Length; k++)
            {
                Texture2D icon = ItemIcons.Get(items[k]);
                if (icon == null) continue;
                var tmp = RenderTexture.GetTemporary(icon.width, icon.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(icon, tmp);
                var prev = RenderTexture.active;
                RenderTexture.active = tmp;
                var readable = new Texture2D(icon.width, icon.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, icon.width, icon.height), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tmp);
                Color32[] ip = readable.GetPixels32();
                int ox = (k % cols) * tile + 4, oy = (rows - 1 - k / cols) * tile + 4;
                for (int y = 0; y < icon.height; y++)
                    for (int x = 0; x < icon.width; x++)
                    {
                        Color32 c = ip[y * icon.width + x];
                        int d = (oy + y) * sheet.width + ox + x;
                        float a = c.a / 255f;
                        px[d] = new Color32((byte)(c.r * a + px[d].r * (1 - a)), (byte)(c.g * a + px[d].g * (1 - a)), (byte)(c.b * a + px[d].b * (1 - a)), 255);
                    }
                Destroy(readable);
            }
            sheet.SetPixels32(px);
            sheet.Apply();
            Directory.CreateDirectory(Path.Combine(_dir, "icons"));
            File.WriteAllBytes(Path.Combine(_dir, "icons", "sheet.png"), sheet.EncodeToPNG());
            Destroy(sheet);
            Line($"Icons: {items.Length} items ({items.Count(i => Art.ItemIconArt.HasDrawing(i.Id))} with their own drawing)");
        }

        private IEnumerator World()
        {
            _cam.enabled = false;
            _studio.gameObject.SetActive(false);
            float t0 = Time.realtimeSinceStartup;
            _boot.StartNewGame(DifficultyPreset.Survival, 20261107);
            float timeout = t0 + 180f;
            while ((_boot.Game == null || !_boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            GameController gc = _boot.Game;
            if (gc == null || !gc.Running)
            {
                Line("FAIL: the new game did not finish loading within 180 s");
                yield break;
            }
            Line($"New game ready in {Time.realtimeSinceStartup - t0:0.0} s");
            gc.Session.Threats.SpawningEnabled = false;
            while (!gc.NavReady && Time.realtimeSinceStartup < timeout) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            yield return Capture("world/01_start_camp");

            Inventory bag = gc.Session.Player.Inventory;
            foreach (ItemDefinition def in _boot.Content.Items.All.OrderBy(i => i.Category).ThenBy(i => i.Id)) bag.TryAdd(def.Id, def.MaxStack > 1 ? 3 : 1);
            yield return new WaitForSecondsRealtime(0.5f);
            gc.Ui.OpenInventory(false);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Capture("world/02_backpack");
            gc.Ui.CloseWindow();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("world/03_hotbar");

            foreach ((double hour, string name) in new[] { (12.5, "04_midday"), (18.4, "05_dusk"), (22.0, "06_night"), (6.4, "07_dawn") })
            {
                SkipTo(gc, hour);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Capture("world/" + name);
            }

            // Every point of interest from a raised three-quarter view, in mid-morning light.
            SkipTo(gc, 10.0);
            _cam.enabled = true;
            _cam.fieldOfView = 50f;
            _cam.farClipPlane = 650f;
            WorldLayout layout = gc.Session.World.Layout;
            Vector3 home = gc.Player.transform.position;
            foreach (PoiInfo poi in layout.Pois)
            {
                Vector3 c = new Vector3(poi.Center.X, layout.HeightAt(poi.Center.X, poi.Center.Z), poi.Center.Z);
                float dist = poi.Radius * 1.25f + 10f;
                Vector3 dir = Quaternion.Euler(0f, 150f, 0f) * Vector3.forward;
                Vector3 eye = c + dir * dist;
                eye.y = layout.HeightAt(eye.x, eye.z) + 8f + poi.Radius * 0.35f;
                gc.Player.Teleport(new Vector3(eye.x, layout.HeightAt(eye.x, eye.z) + 0.2f, eye.z));
                _cam.transform.position = eye;
                _cam.transform.rotation = Quaternion.LookRotation(c + Vector3.up * 2f - eye);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Capture($"world/poi_{poi.Kind}_{poi.Id}");
            }
            // Eye level in the forest and at the lake shore.
            gc.Player.Teleport(home);
            Vector3 forward = gc.Player.transform.forward;
            _cam.transform.position = home + Vector3.up * 1.7f - forward * 2f;
            _cam.transform.rotation = Quaternion.LookRotation(forward + Vector3.down * 0.05f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture("world/08_eye_level");
            _cam.enabled = false;
            Line($"World: {layout.Pois.Count} points of interest");
        }

        private static void SkipTo(GameController gc, double hour)
        {
            var clock = gc.Session.World.Clock;
            clock.Advance((hour / 24.0 - clock.TimeOfDay01 + 1.0) % 1.0 * clock.DayLengthSeconds);
        }

        private void Line(string text)
        {
            Debug.Log("[Gallery] " + text);
            _report.AppendLine(text);
        }

        private void Finish()
        {
            Line($"Screenshots: {_shots}");
            Line($"Errors logged: {_errors.Count}");
            foreach (string e in _errors) _report.AppendLine(e);
            File.WriteAllText(Path.Combine(_dir, "gallery-report.txt"), _report.ToString());
            _boot.Quit();
        }
    }
}
