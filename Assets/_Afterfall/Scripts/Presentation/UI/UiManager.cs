using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>A modal window shown over gameplay.</summary>
    public interface IWindow
    {
        VisualElement Root { get; }
        void Refresh();
        void OnClose();
    }

    /// <summary>
    /// Owns the single UI Toolkit document and its layers (HUD, windows, menus, toasts) and
    /// decides when gameplay input is captured by UI. All screens are built in code.
    /// </summary>
    public sealed class UiManager : MonoBehaviour
    {
        private UIDocument _doc;
        private PanelSettings _panel;
        private VisualElement _root, _hudLayer, _overlayLayer, _windowLayer, _menuLayer, _toastLayer;
        private readonly List<(VisualElement Element, float Until)> _toasts = new List<(VisualElement, float)>();
        private IWindow _window;
        private HudView _hud;
        private TacticalPanel _tactical;
        private VisualElement _death, _loading, _mainMenu;
        private Label _loadingText;
        private float _refreshTimer;

        public GameBootstrap App { get; private set; }
        public GameController Game { get; private set; }
        public VisualElement OverlayLayer => _overlayLayer;
        public bool WindowOpen => _window != null;
        public bool MenuOpen => (_mainMenu != null && _mainMenu.style.display != DisplayStyle.None) || (_loading != null && _loading.style.display != DisplayStyle.None);
        public bool AnyWindowOpen => WindowOpen || MenuOpen;
        public IWindow CurrentWindow => _window;

        public static UiManager Create(GameBootstrap app)
        {
            // Configure the document while inactive so UIDocument.OnEnable sees its PanelSettings.
            var go = new GameObject("UI");
            go.SetActive(false);
            var ui = go.AddComponent<UiManager>();
            ui.App = app;
            ui._panel = ScriptableObject.CreateInstance<PanelSettings>();
            ui._panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/AfterfallTheme");
            ui._panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ui._panel.referenceResolution = new Vector2Int(1920, 1080);
            ui._panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ui._panel.match = 0.5f;
            ui._panel.scale = app.Settings.UiScale;
            ui._doc = go.AddComponent<UIDocument>();
            ui._doc.panelSettings = ui._panel;
            go.SetActive(true);
            ui.BuildLayers();
            return ui;
        }

        private void BuildLayers()
        {
            _root = _doc.rootVisualElement;
            StyleSheet sheet = Resources.Load<StyleSheet>("UI/Afterfall");
            if (sheet != null) _root.styleSheets.Add(sheet);
            else Debug.LogWarning("[Afterfall] UI stylesheet Resources/UI/Afterfall.uss not found.");
            _root.AddToClassList("root");
            _hudLayer = Layer("hud", PickingMode.Ignore);
            _overlayLayer = Layer("overlay", PickingMode.Ignore);
            _windowLayer = Layer("windows", PickingMode.Ignore);
            _menuLayer = Layer("menus", PickingMode.Ignore);
            _toastLayer = Layer("toasts", PickingMode.Ignore);
            var toasts = UiKit.Div("toasts");
            toasts.pickingMode = PickingMode.Ignore;
            _toastLayer.Add(toasts);
            _toastLayer.userData = toasts;

            _loading = UiKit.Div("layer", "loading");
            _loadingText = _loading.Put(UiKit.Text("Loading…", "title"));
            _loading.Add(UiKit.Text("Preparing the world", "muted"));
            UiKit.Show(_loading, false);
            _menuLayer.Add(_loading);
        }

        private VisualElement Layer(string name, PickingMode picking)
        {
            var e = UiKit.Div("layer");
            e.name = name;
            e.pickingMode = picking;
            _root.Add(e);
            return e;
        }

        public void ApplyScale(float scale)
        {
            if (_panel != null) _panel.scale = scale;
        }

        /// <summary>True when the mouse is over an interactive UI element (blocks world clicks).</summary>
        public bool PointerOverUi
        {
            get
            {
                if (Mouse.current == null || _doc?.rootVisualElement?.panel == null) return false;
                Vector2 screen = Mouse.current.position.ReadValue();
                Vector2 p = RuntimePanelUtils.ScreenToPanel(_doc.rootVisualElement.panel, new Vector2(screen.x, Screen.height - screen.y));
                VisualElement picked = _doc.rootVisualElement.panel.Pick(p);
                return picked != null && picked != _root;
            }
        }

        // ------------------------------------------------------------------ toasts

        public void Toast(string message, Severity severity = Severity.Info)
        {
            if (string.IsNullOrEmpty(message)) return;
            var list = (VisualElement)_toastLayer.userData;
            var t = UiKit.Text(message, "toast", "toast--" + severity);
            t.pickingMode = PickingMode.Ignore;
            list.Add(t);
            _toasts.Add((t, Time.unscaledTime + (severity == Severity.Danger ? 7f : 4.5f)));
            while (_toasts.Count > 6)
            {
                _toasts[0].Element.RemoveFromHierarchy();
                _toasts.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------ menus

        public void ShowLoading(string text)
        {
            _loadingText.text = text;
            UiKit.Show(_loading, true);
        }

        public void HideLoading() => UiKit.Show(_loading, false);

        public void ShowMainMenu()
        {
            CloseWindow();
            if (_mainMenu != null) _mainMenu.RemoveFromHierarchy();
            _mainMenu = MenuScreens.MainMenu(this);
            _menuLayer.Insert(0, _mainMenu);
        }

        public void HideMainMenu()
        {
            _mainMenu?.RemoveFromHierarchy();
            _mainMenu = null;
        }

        public void ReplaceMainMenuContent(VisualElement content)
        {
            if (_mainMenu == null) return;
            _mainMenu.Clear();
            _mainMenu.Add(content);
        }

        // ------------------------------------------------------------------ game attachment

        public void AttachGame(GameController gc)
        {
            Game = gc;
            _hud = new HudView(this, gc);
            _hudLayer.Add(_hud.Root);
            _tactical = new TacticalPanel(this, gc);
            _hudLayer.Add(_tactical.Root);
            _death = MenuScreens.DeathScreen(this);
            UiKit.Show(_death, false);
            _menuLayer.Add(_death);
        }

        public void DetachGame()
        {
            CloseWindow();
            _hud?.Root.RemoveFromHierarchy();
            _tactical?.Root.RemoveFromHierarchy();
            _death?.RemoveFromHierarchy();
            _overlayLayer.Clear();
            _hud = null;
            _tactical = null;
            _death = null;
            Game = null;
        }

        // ------------------------------------------------------------------ windows

        public void OpenWindow(IWindow window)
        {
            CloseWindow();
            _window = window;
            _windowLayer.Add(window.Root);
            window.Refresh();
        }

        public void CloseWindow()
        {
            if (_window == null) return;
            _window.OnClose();
            _window.Root.RemoveFromHierarchy();
            _window = null;
        }

        public void ToggleInventory()
        {
            if (_window is InventoryWindow) CloseWindow();
            else OpenInventory(false);
        }

        public void OpenInventory(bool craftingFocus) => OpenWindow(new InventoryWindow(this, Game, craftingFocus));

        public void OpenContainer(EntityId inventoryId, string title)
        {
            if (!Game.Session.CanAccess(inventoryId, out string reason))
            {
                Toast(reason, Severity.Warning);
                return;
            }
            Game.Session.MarkSearched(inventoryId);
            OpenWindow(new ContainerWindow(this, Game, inventoryId, title));
        }

        public void OpenMachine(EntityId structureId) => OpenWindow(new MachineWindow(this, Game, structureId));

        public void ToggleBuildMenu()
        {
            if (_window is BuildMenuWindow) CloseWindow();
            else OpenWindow(new BuildMenuWindow(this, Game));
        }

        public void OpenPause() => OpenWindow(new PauseWindow(this, Game));
        public void OpenSettings() => OpenWindow(new SettingsWindow(this));
        public void OpenHelp() => OpenWindow(new HelpWindow(this));

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                if (_toasts[i].Until > now) continue;
                _toasts[i].Element.RemoveFromHierarchy();
                _toasts.RemoveAt(i);
            }
            if (Game == null || Game.Session == null) return;
            _hud?.Refresh();
            _tactical?.Refresh();
            if (_death != null) UiKit.Show(_death, Game.Session.Player.Vitals.IsDead);
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_window != null && _refreshTimer <= 0f)
            {
                _refreshTimer = 0.1f;
                _window.Refresh();
            }
        }
    }
}
