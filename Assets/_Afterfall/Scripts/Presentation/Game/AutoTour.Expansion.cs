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
            yield return CreativeScene(gc);
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
            frames.Sort();
            Check(flown > 200f, $"Flew {flown:0} m toward Hartley's Workshop in {seconds:0.0} s (top speed {maxSpeed:0} m/s); frames median {(frames.Count > 0 ? frames[frames.Count / 2] : 0):0.0} ms, worst {(frames.Count > 0 ? frames[frames.Count - 1] : 0):0.0} ms");
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
