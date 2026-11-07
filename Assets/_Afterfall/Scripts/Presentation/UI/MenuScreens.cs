using System;
using System.Collections.Generic;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>Main menu, new game, load game, death screen.</summary>
    public static class MenuScreens
    {
        public static VisualElement MainMenu(UiManager ui)
        {
            var root = UiKit.Div("layer", "menu-bg");
            root.Add(Home(ui));
            return root;
        }

        private static VisualElement Home(UiManager ui)
        {
            var col = UiKit.Div("col");
            col.style.alignItems = Align.Center;
            col.Add(UiKit.Text("AFTERFALL", "logo"));
            col.Add(UiKit.Text("From Survival to Civilization", "tagline"));
            List<SaveSlotInfo> saves = ui.App.Saves.Store.List();
            if (saves.Count > 0 && saves[0].Readable)
            {
                SaveSlotInfo latest = saves[0];
                col.Add(UiKit.Btn($"Continue  ({latest.Header.Slot}, day {latest.Header.Day})", () => ui.App.LoadGame(latest.Slot), "menu-btn", "btn--primary"));
            }
            col.Add(UiKit.Btn("New Game", () => ui.ReplaceMainMenuContent(NewGame(ui)), "menu-btn"));
            col.Add(UiKit.Btn("Load Game", () => ui.ReplaceMainMenuContent(LoadGame(ui)), "menu-btn"));
            col.Add(UiKit.Btn("Settings", () => ui.OpenSettings(), "menu-btn"));
            col.Add(UiKit.Btn("Controls", () => ui.OpenHelp(), "menu-btn"));
            col.Add(UiKit.Btn("Quit", () => ui.App.Quit(), "menu-btn"));
            var footer = col.Put(UiKit.Text($"Prototype {Afterfall.Persistence.SaveSerializer.GameVersion} · Unity {Application.unityVersion}", "muted"));
            footer.style.marginTop = 30;
            return col;
        }

        private static VisualElement NewGame(UiManager ui)
        {
            var col = UiKit.Div("panel", "col");
            col.style.width = 760;
            col.Add(UiKit.Text("New Game", "title"));
            col.Add(UiKit.Text("Choose how harsh the world is. Every value can be tuned independently later.", "muted"));
            DifficultyPreset chosen = DifficultyPreset.Survival;
            var buttons = new Dictionary<DifficultyPreset, Button>();
            var desc = UiKit.Text("", "wrap");
            void Select(DifficultyPreset p)
            {
                chosen = p;
                foreach (var kv in buttons) UiKit.SetClass(kv.Value, "btn--selected", kv.Key == p);
                GameSettings s = GameSettings.FromPreset(p);
                desc.text = p switch
                {
                    DifficultyPreset.Exploration => "Exploration — fewer infected, generous loot, slow hunger and thirst. Tactical pause allowed.",
                    DifficultyPreset.Hardcore => "Hardcore — more and deadlier infected, scarce loot, fast needs. Death ends the run.",
                    _ => "Survival — the intended experience. Death drops your carried items where you fell; your base persists."
                } + $"\nInfected density ×{s.EnemyDensity:0.#} · loot ×{s.LootAbundance:0.#} · needs ×{s.SurvivalDrain:0.#} · damage ×{s.EnemyDamage:0.#}";
            }
            var row = col.Put(UiKit.Div("row"));
            foreach (DifficultyPreset p in new[] { DifficultyPreset.Exploration, DifficultyPreset.Survival, DifficultyPreset.Hardcore })
                buttons[p] = row.Put(UiKit.Btn(p.ToString(), () => Select(p)));
            col.Add(desc);
            var seedField = col.Put(new TextField("World seed") { value = UnityEngine.Random.Range(1, 99999).ToString() });
            Select(DifficultyPreset.Survival);
            var actions = col.Put(UiKit.Div("row"));
            actions.Add(UiKit.Btn("Back", () => ui.ShowMainMenu()));
            actions.Add(UiKit.Btn("Start", () =>
            {
                int seed = int.TryParse(seedField.value, out int v) ? v : seedField.value.GetHashCode();
                ui.App.StartNewGame(chosen, seed);
            }, "btn--primary"));
            var wrap = UiKit.Div("col");
            wrap.style.alignItems = Align.Center;
            wrap.Add(col);
            return wrap;
        }

        public static VisualElement LoadGame(UiManager ui)
        {
            var col = UiKit.Div("panel", "col");
            col.style.width = 760;
            col.Add(UiKit.Text("Load Game", "title"));
            var list = col.Put(new ScrollView());
            list.style.maxHeight = 520;
            List<SaveSlotInfo> saves = ui.App.Saves.Store.List();
            if (saves.Count == 0) list.Add(UiKit.Text("No saves yet.", "muted"));
            foreach (SaveSlotInfo s in saves)
            {
                SaveSlotInfo slot = s;
                var row = list.Put(UiKit.Div("list-item"));
                string info = s.Readable ? $"{s.Header.Slot} — day {s.Header.Day} ({s.Header.TimeOfDay}) · {s.Header.Difficulty} · seed {s.Header.Seed} · {s.LastWriteUtc.ToLocalTime():g}" : $"{s.Slot} — unreadable header (a backup may still load)";
                row.Add(UiKit.Text(info, "grow", "wrap"));
                row.Add(UiKit.Btn("Load", () => ui.App.LoadGame(slot.Slot), "btn--small", "btn--primary"));
                row.Add(UiKit.Btn("Delete", () =>
                {
                    ui.App.Saves.Store.Delete(slot.Slot);
                    ui.ReplaceMainMenuContent(LoadGame(ui));
                }, "btn--small", "btn--danger"));
            }
            col.Add(UiKit.Btn("Back", () => ui.ShowMainMenu()));
            var wrap = UiKit.Div("col");
            wrap.style.alignItems = Align.Center;
            wrap.Add(col);
            return wrap;
        }

        public static VisualElement DeathScreen(UiManager ui)
        {
            var root = UiKit.Div("layer", "death");
            root.Add(UiKit.Text("YOU DIED", "death-title"));
            Label info = root.Put(UiKit.Text("", "wrap"));
            info.style.maxWidth = 700;
            info.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.RegisterCallback<AttachToPanelEvent>(_ => { });
            var row = root.Put(UiKit.Div("row"));
            Button respawn = row.Put(UiKit.Btn("Wake up", () =>
            {
                var r = ui.Game.Session.Respawn();
                ui.Toast(r.Message, r.Ok ? Severity.Info : Severity.Danger);
                if (r.Ok) ui.Game.OnRespawned();
            }, "menu-btn", "btn--primary"));
            row.Add(UiKit.Btn("Main Menu", () => ui.App.ReturnToMainMenu(), "menu-btn"));
            root.schedule.Execute(() =>
            {
                if (ui.Game == null) return;
                bool over = ui.Game.Session.GameOver;
                info.text = over
                    ? "Hardcore: this run is over. Your settlement will be remembered by no one."
                    : "Your carried belongings lie where you fell, marked by a blue beacon. You will wake at your bedroll if you built one, otherwise where you first awoke.";
                UiKit.Show(respawn, !over);
            }).Every(500);
            return root;
        }
    }

    public sealed class PauseWindow : IWindow
    {
        private readonly UiManager _ui;
        private readonly TextField _slot;

        public PauseWindow(UiManager ui, GameController gc)
        {
            _ui = ui;
            Root = UiKit.Div("window");
            Root.style.left = new Length(35, LengthUnit.Percent);
            Root.style.right = new Length(35, LengthUnit.Percent);
            Root.style.top = new Length(18, LengthUnit.Percent);
            Root.Add(UiKit.Text("Paused", "title"));
            Root.Add(UiKit.Btn("Resume", () => ui.CloseWindow(), "btn--primary"));
            _slot = Root.Put(new TextField("Save name") { value = gc.CurrentSlot });
            Root.Add(UiKit.Btn("Save game", () =>
            {
                gc.Save(string.IsNullOrWhiteSpace(_slot.value) ? "save" : _slot.value);
            }));
            Root.Add(UiKit.Btn("Load game…", () => ui.OpenWindow(new LoadWindow(ui))));
            Root.Add(UiKit.Btn("Settings", () => ui.OpenSettings()));
            Root.Add(UiKit.Btn("Controls & goals", () => ui.OpenHelp()));
            Root.Add(UiKit.Btn("Save & return to main menu", () =>
            {
                gc.Save(gc.CurrentSlot);
                ui.App.ReturnToMainMenu();
            }));
            Root.Add(UiKit.Btn("Quit to desktop (saves first)", () =>
            {
                gc.Save(gc.CurrentSlot);
                ui.App.Quit();
            }, "btn--danger"));
            Root.Add(UiKit.Text("The world keeps simulating only while this menu is closed.", "muted"));
        }

        public VisualElement Root { get; }
        public void Refresh() { }
        public void OnClose() { }
    }

    public sealed class LoadWindow : IWindow
    {
        public LoadWindow(UiManager ui)
        {
            Root = UiKit.Div("window", "window-center");
            Root.Add(MenuScreens.LoadGame(ui));
            Root.Add(UiKit.Btn("Close", () => ui.CloseWindow()));
        }

        public VisualElement Root { get; }
        public void Refresh() { }
        public void OnClose() { }
    }

    public sealed class SettingsWindow : IWindow
    {
        private readonly UiManager _ui;
        private readonly VisualElement _bindings;

        public SettingsWindow(UiManager ui)
        {
            _ui = ui;
            AppSettings s = ui.App.Settings;
            Root = UiKit.Div("window", "window-center");
            Root.Add(UiKit.Text("Settings", "title"));
            var sens = Root.Put(new Slider("Mouse sensitivity", 0.1f, 4f) { value = s.MouseSensitivity });
            sens.RegisterValueChangedCallback(e => { s.MouseSensitivity = e.newValue; Apply(); });
            var fov = Root.Put(new Slider("Field of view", 40f, 80f) { value = s.FieldOfView });
            fov.RegisterValueChangedCallback(e => { s.FieldOfView = e.newValue; Apply(); });
            var scale = Root.Put(new Slider("Interface scale", 0.7f, 1.6f) { value = s.UiScale });
            scale.RegisterValueChangedCallback(e => { s.UiScale = e.newValue; Apply(); });
            var invert = Root.Put(new Toggle("Invert vertical look") { value = s.InvertY });
            invert.RegisterValueChangedCallback(e => { s.InvertY = e.newValue; Apply(); });
            var shake = Root.Put(new Toggle("Camera shake") { value = s.CameraShake });
            shake.RegisterValueChangedCallback(e => { s.CameraShake = e.newValue; Apply(); });
            var fps = Root.Put(new Toggle("Show performance overlay (F3)") { value = s.ShowFps });
            fps.RegisterValueChangedCallback(e => { s.ShowFps = e.newValue; Apply(); });
            var head = Root.Put(UiKit.Div("row"));
            head.Add(UiKit.Text("Key bindings", "subtitle"));
            head.Add(UiKit.Btn("Reset to defaults", () => { ui.App.Input.ResetBindings(); BuildBindings(); }, "btn--small"));
            var scroll = Root.Put(new ScrollView());
            scroll.style.flexGrow = 1;
            _bindings = scroll;
            BuildBindings();
            Root.Add(UiKit.Btn("Back", () =>
            {
                ui.CloseWindow();
                if (ui.Game != null) ui.OpenPause();
            }, "btn--primary"));
        }

        private void Apply()
        {
            _ui.App.Settings.Save();
            _ui.App.ApplySettings();
        }

        private void BuildBindings()
        {
            _bindings.Clear();
            foreach ((string label, InputAction action, int index) in _ui.App.Input.Rebindables())
            {
                var row = _bindings.Put(UiKit.Div("list-item"));
                row.Add(UiKit.Text(label, "grow"));
                Label key = row.Put(UiKit.Text(GameInput.Display(action, index)));
                key.style.width = 160;
                InputAction a = action;
                int i = index;
                row.Add(UiKit.Btn("Rebind", () =>
                {
                    key.text = "press a key…";
                    _ui.App.Input.StartRebind(a, i, () => key.text = GameInput.Display(a, i));
                }, "btn--small"));
            }
        }

        public VisualElement Root { get; }
        public void Refresh() { }
        public void OnClose() => _ui.App.Settings.Save();
    }

    public sealed class HelpWindow : IWindow
    {
        public HelpWindow(UiManager ui)
        {
            Root = UiKit.Div("window", "window-center");
            Root.Add(UiKit.Text("Controls", "title"));
            var cols = Root.Put(UiKit.Div("row"));
            cols.style.alignItems = Align.FlexStart;
            cols.Add(UiKit.Text(
                "WASD move · Shift sprint · C/Ctrl crouch · Space jump\n" +
                "Mouse look · wheel zoom\n" +
                "E interact / hold E to build · LMB attack or harvest\n" +
                "1–8 hotbar · Q drop held item\n" +
                "I backpack & crafting · B construction\n" +
                "R rotate blueprint · RMB cancel placement\n" +
                "Tab strategic view (WASD pan, Q/E rotate, wheel zoom, LMB select)\n" +
                "F5 quick save · F9 quick load · F3 performance · Esc menu", "wrap", "grow"));
            cols.Add(UiKit.Text(
                "Getting started\n" +
                "1. Loot the supply crate at your camp. Gather fiber, branches and stones by hand.\n" +
                "2. Craft rope and a stone axe (I). Equip it and chop trees.\n" +
                "3. Build a workbench (B), saw planks, then foundations, walls and a crate.\n" +
                "4. Find iron ore at the Old Quarry (north) — needs a pickaxe — to unlock furnaces.\n" +
                "5. Smelt ingots. Recover the generator schematic at the Ridge Substation (dangerous).\n" +
                "6. Build a generator and a Machine Workshop next to it; fuel with cans from the gas station or charcoal.\n" +
                "7. Machine gears to unlock the electric water pump — clean water forever.\n" +
                "Noise and light attract the infected. Night is dangerous. Machines explain why they stop.", "wrap", "grow"));
            Root.Add(UiKit.Btn("Close", () => ui.CloseWindow(), "btn--primary"));
        }

        public VisualElement Root { get; }
        public void Refresh() { }
        public void OnClose() { }
    }
}
