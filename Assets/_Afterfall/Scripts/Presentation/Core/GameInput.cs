using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Afterfall.Presentation
{
    /// <summary>
    /// All input actions, defined in code so the game needs no generated assets. Gameplay
    /// scripts read actions from here instead of polling devices, and every binding can be
    /// rebound at runtime (overrides persist in PlayerPrefs).
    /// </summary>
    public sealed class GameInput : IDisposable
    {
        private const string OverridesKey = "afterfall.bindings";

        public readonly InputActionAsset Asset;
        public readonly InputActionMap Global;
        public readonly InputActionMap Player;
        public readonly InputActionMap Tactical;

        // Global
        public readonly InputAction Pause, Inventory, ToggleTactical, QuickSave, QuickLoad, DebugOverlay, Help, Build;

        // Player (third person)
        public readonly InputAction Move, Look, Zoom, Jump, Sprint, Crouch, Interact, Primary, Secondary, Rotate, Drop;
        public readonly InputAction[] Hotbar = new InputAction[8];

        // Tactical camera
        public readonly InputAction TacPan, TacRotate, TacZoom, TacSelect, TacCommand, TacDrag, TacLook, TacFast, TacRotateBuild;

        public GameInput()
        {
            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Asset.name = "AfterfallInput";
            Global = Asset.AddActionMap("Global");
            Player = Asset.AddActionMap("Player");
            Tactical = Asset.AddActionMap("Tactical");

            Pause = Button(Global, "Pause", "<Keyboard>/escape");
            Inventory = Button(Global, "Inventory", "<Keyboard>/i");
            ToggleTactical = Button(Global, "Strategic Camera", "<Keyboard>/tab");
            Build = Button(Global, "Build Menu", "<Keyboard>/b");
            QuickSave = Button(Global, "Quick Save", "<Keyboard>/f5");
            QuickLoad = Button(Global, "Quick Load", "<Keyboard>/f9");
            DebugOverlay = Button(Global, "Debug Overlay", "<Keyboard>/f3");
            Help = Button(Global, "Help", "<Keyboard>/f1");

            Move = Player.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");
            Look = Player.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            Zoom = Player.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            Jump = Button(Player, "Jump", "<Keyboard>/space");
            Sprint = Button(Player, "Sprint", "<Keyboard>/leftShift");
            Crouch = Button(Player, "Crouch", "<Keyboard>/c");
            Crouch.AddBinding("<Keyboard>/leftCtrl");
            Interact = Button(Player, "Interact", "<Keyboard>/e");
            Primary = Button(Player, "Primary Action", "<Mouse>/leftButton");
            Secondary = Button(Player, "Secondary Action", "<Mouse>/rightButton");
            Rotate = Button(Player, "Rotate Blueprint", "<Keyboard>/r");
            Drop = Button(Player, "Drop Item", "<Keyboard>/q");
            for (int i = 0; i < Hotbar.Length; i++) Hotbar[i] = Button(Player, $"Hotbar {i + 1}", $"<Keyboard>/{i + 1}");

            TacPan = Tactical.AddAction("Pan", InputActionType.Value);
            TacPan.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            TacRotate = Tactical.AddAction("Rotate View", InputActionType.Value);
            TacRotate.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
            TacZoom = Tactical.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            TacSelect = Button(Tactical, "Select", "<Mouse>/leftButton");
            TacCommand = Button(Tactical, "Command", "<Mouse>/rightButton");
            TacDrag = Button(Tactical, "Drag Pan", "<Mouse>/middleButton");
            TacLook = Tactical.AddAction("Pointer Delta", InputActionType.Value, "<Mouse>/delta");
            TacFast = Button(Tactical, "Fast Pan", "<Keyboard>/leftShift");
            TacRotateBuild = Button(Tactical, "Rotate Blueprint", "<Keyboard>/r");

            LoadOverrides();
            Global.Enable();
        }

        private static InputAction Button(InputActionMap map, string name, string binding)
        {
            return map.AddAction(name, InputActionType.Button, binding);
        }

        public void SetMode(bool player, bool tactical)
        {
            if (player) Player.Enable();
            else Player.Disable();
            if (tactical) Tactical.Enable();
            else Tactical.Disable();
        }

        /// <summary>Rebindable entries for the settings screen (composite parts are listed individually).</summary>
        public List<(string Label, InputAction Action, int BindingIndex)> Rebindables()
        {
            var list = new List<(string, InputAction, int)>();
            foreach (InputActionMap map in new[] { Global, Player, Tactical })
            {
                foreach (InputAction a in map.actions)
                {
                    if (a.name == "Look" || a.name == "Pointer Delta" || a.name == "Zoom") continue;
                    for (int i = 0; i < a.bindings.Count; i++)
                    {
                        InputBinding b = a.bindings[i];
                        if (b.isComposite) continue;
                        if (b.path.StartsWith("<Gamepad>", StringComparison.Ordinal) || b.path.Contains("Arrow")) continue;
                        string label = b.isPartOfComposite ? $"{map.name}: {a.name} {b.name}" : $"{map.name}: {a.name}";
                        if (!b.isPartOfComposite && i > 0) label += " (alt)";
                        list.Add((label, a, i));
                    }
                }
            }
            return list;
        }

        public static string Display(InputAction action, int bindingIndex)
        {
            return action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        public void StartRebind(InputAction action, int bindingIndex, Action onDone)
        {
            bool wasEnabled = action.enabled;
            action.Disable();
            action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op =>
                {
                    op.Dispose();
                    if (wasEnabled) action.Enable();
                    SaveOverrides();
                    onDone?.Invoke();
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    if (wasEnabled) action.Enable();
                    onDone?.Invoke();
                })
                .Start();
        }

        public void ResetBindings()
        {
            foreach (InputActionMap map in Asset.actionMaps) map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(OverridesKey);
        }

        public void SaveOverrides()
        {
            PlayerPrefs.SetString(OverridesKey, Asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        private void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(OverridesKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                Asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Afterfall] Ignoring invalid saved key bindings: " + ex.Message);
            }
        }

        public void Dispose()
        {
            Asset.Disable();
            UnityEngine.Object.Destroy(Asset);
        }
    }
}
