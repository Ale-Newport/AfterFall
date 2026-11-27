using System.Collections;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Presentation.UI;
using Afterfall.Services;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Runs one game session in the scene: builds the world view, owns the player, cameras,
    /// controllers and UI attachment, advances the simulation, and routes input by mode
    /// (third person, strategic view, building, windows, death).
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public const float AutosaveInterval = 300f;

        private readonly List<System.IDisposable> _subscriptions = new List<System.IDisposable>();
        private readonly Dictionary<string, float> _throttle = new Dictionary<string, float>();
        private IInteractable _target;
        private float _holdTick;
        private float _demolishHold;
        private float _autosave = AutosaveInterval;
        private Transform _worldRoot;
        private NavMeshSurface _surface;
        private AsyncOperation _navOp;

        public GameBootstrap App { get; private set; }
        public GameSession Session { get; private set; }
        public GameInput Input => App.Input;
        public AppSettings Settings => App.Settings;
        public UiManager Ui => App.Ui;
        public PlayerCharacter Player { get; private set; }
        public CameraRig CameraRig { get; private set; }
        public WorldSync Sync { get; private set; }
        public BuildController Build { get; private set; }
        public TacticalController Tactical { get; private set; }
        public Terrain Terrain { get; private set; }
        public AudioDirector Audio { get; private set; }

        /// <summary>Doors colonists open on their way and close behind them.</summary>
        public DoorTraffic Doors { get; private set; }

        /// <summary>Map textures shared by the world map and the minimap.</summary>
        public Afterfall.Presentation.UI.MapTextures Maps { get; private set; }
        public bool Running { get; private set; }
        public bool NavReady { get; private set; }
        public string CurrentSlot { get; set; } = "save1";
        public double Playtime { get; private set; }
        public string CurrentPrompt { get; private set; }

        public bool IsDead => Session.Player.Vitals.IsDead;
        public bool GameplayInputActive => Running && !Ui.AnyWindowOpen && !CameraRig.TacticalActive && !IsDead;
        public bool LookInputActive => GameplayInputActive;
        public bool TacticalInputActive => Running && CameraRig.TacticalActive && !Ui.AnyWindowOpen;
        public bool PlacementInputActive => Running && Build.IsPlacing && !Ui.AnyWindowOpen && !IsDead;

        public static GameController Create(GameBootstrap app, GameSession session, string slot)
        {
            var go = new GameObject("Game");
            var gc = go.AddComponent<GameController>();
            gc.App = app;
            gc.Session = session;
            gc.CurrentSlot = slot;
            gc.StartCoroutine(gc.BuildWorld());
            return gc;
        }

        private IEnumerator BuildWorld()
        {
            Ui.ShowLoading("Shaping the land…");
            yield return null;
            ProceduralTextures.PrewarmCatalogue();
            _worldRoot = new GameObject("World").transform;
            _worldRoot.SetParent(transform, false);
            Terrain = WorldBuilder.BuildTerrain(Session.World.Layout, Session.World.Seed, _worldRoot);
            WorldBuilder.BuildWater(Session.World.Layout, _worldRoot);
            WorldBuilder.BuildBoundary(_worldRoot);
            yield return null;
            Ui.ShowLoading("Raising ruins…");
            yield return null;
            WorldBuilder.BuildProps(Session.World.Layout, _worldRoot);
            yield return null;
            Ui.ShowLoading("Growing the forest…");
            yield return null;

            Maps = new Afterfall.Presentation.UI.MapTextures(Session.World);
            Sync = new GameObject("Entities").AddComponent<WorldSync>();
            Sync.transform.SetParent(_worldRoot, false);
            Sync.Initialize(this);
            Doors = new DoorTraffic(this);
            Session.Colony.Movement = new NavColonistMovement(this);
            yield return null;

            Vector3 spawn = Session.Player.Position.ToUnity();
            spawn.y = Mathf.Max(spawn.y, GroundHeight(spawn)) + 0.1f;
            Player = PlayerCharacter.Create(this, spawn, Session.Player.Yaw);
            Player.transform.SetParent(transform, true);
            CameraRig = CameraRig.Create(this, Player.transform, Settings);
            CameraRig.transform.SetParent(transform, true);
            gameObject.AddComponent<DayNightCycle>().Initialize(this);
            gameObject.AddComponent<WeatherView>().Initialize(this);
            gameObject.AddComponent<GroundCover>().Initialize(this);
            Build = gameObject.AddComponent<BuildController>();
            Build.Initialize(this);
            Tactical = new GameObject("Tactical").AddComponent<TacticalController>();
            Tactical.transform.SetParent(transform, false);
            Tactical.Initialize(this);
            gameObject.AddComponent<DebugOverlay>().Initialize(this);
            Audio = new GameObject("Audio").AddComponent<AudioDirector>();
            Audio.transform.SetParent(transform, false);
            Audio.Initialize(this);
            Session.Zombies.Adapter = new NavZombieAdapter(this);
            Subscribe();
            Ui.AttachGame(this);
            Ui.HideLoading();
            Running = true;
            Ui.Toast(Session.World.Metrics.Saves == 0 && Session.Now < 5
                ? "You wake up alone at a cold campfire. Search the crate, gather what you can, and survive. (F1 for controls)"
                : $"Welcome back. {Session.World.Clock.FormatTime()}.", Severity.Info);
            StartCoroutine(BakeNavMesh());
        }

        private IEnumerator BakeNavMesh()
        {
            yield return null;
            _surface = _worldRoot.gameObject.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.Children;
            _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            _surface.layerMask = Layers.Mask(Layers.Terrain, Layers.Prop, Layers.Resource, Layers.Structure) | 1;
            _surface.overrideVoxelSize = true;
            _surface.voxelSize = 0.22f;
            _surface.overrideTileSize = true;
            _surface.tileSize = 128;
            _surface.navMeshData = new NavMeshData(_surface.agentTypeID);
            _surface.AddData();
            _navOp = _surface.UpdateNavMesh(_surface.navMeshData);
            while (_navOp != null && !_navOp.isDone) yield return null;
            NavReady = true;
        }

        private void Subscribe()
        {
            EventBus bus = Session.Events;
            _subscriptions.Add(bus.Subscribe<Notification>(n => Toast(n.Message, n.Severity)));
            _subscriptions.Add(bus.Subscribe<PlayerDamaged>(d =>
            {
                CameraRig.Shake(Mathf.Clamp01(d.Amount / 20f));
                Player.Rig.Flash();
            }));
            _subscriptions.Add(bus.Subscribe<ZombieKilled>(_ => Toast("Infected killed.", Severity.Success)));
            _subscriptions.Add(bus.Subscribe<SaveCompleted>(s => Toast(s.Message, s.Success ? Severity.Success : Severity.Danger)));
            bus.HandlerFailed += (e, ex) => Debug.LogException(ex);
        }

        private void OnDestroy()
        {
            foreach (System.IDisposable s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
            Maps?.Dispose();
            if (_surface != null) _surface.RemoveData();
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (!Running) return;
            HandleGlobalInput();
            UpdateInputMaps();
            Session.Paused = IsPausingWindow(Ui.CurrentWindow) || Ui.MenuOpen;
            if (!Session.Paused)
            {
                Session.Update(Time.deltaTime);
                Playtime += Time.deltaTime;
            }
            UpdateCursor();
            UpdateInteraction();
            Maps?.Update();
            Doors?.Update();

            if (!Session.Paused && !IsDead)
            {
                _autosave -= Time.deltaTime;
                if (_autosave <= 0f)
                {
                    _autosave = AutosaveInterval;
                    App.Saves.Save(Session, SaveService.AutoSlot, Playtime, out _);
                }
            }
        }

        private void HandleGlobalInput()
        {
            GameInput i = Input;
            if (i.Pause.WasPressedThisFrame())
            {
                if (Build.IsPlacing) Build.Cancel();
                else if (Ui.WindowOpen) Ui.CloseWindow();
                else if (CameraRig.TacticalActive) SetTactical(false);
                else if (!IsDead) Ui.OpenPause();
            }
            if (IsDead) return;
            bool menuWindow = IsPausingWindow(Ui.CurrentWindow);
            if (i.Inventory.WasPressedThisFrame() && !menuWindow) Ui.ToggleInventory();
            if (i.Build.WasPressedThisFrame() && !menuWindow)
            {
                Build.Cancel();
                Ui.ToggleBuildMenu();
            }
            if (i.ToggleTactical.WasPressedThisFrame() && !Ui.WindowOpen)
            {
                if (Player.Flight.Active) ToastThrottled("Land first (F7) to use the strategic view.", Severity.Warning);
                else SetTactical(!CameraRig.TacticalActive);
            }
            if (i.QuickSave.WasPressedThisFrame()) Save(SaveService.QuickSlot);
            if (i.QuickLoad.WasPressedThisFrame()) App.LoadGame(SaveService.QuickSlot);
            if (i.DebugOverlay.WasPressedThisFrame())
            {
                Settings.ShowFps = !Settings.ShowFps;
                Settings.Save();
            }
            if (i.Help.WasPressedThisFrame() && !menuWindow) Ui.OpenHelp();
            if (i.CreativePanel.WasPressedThisFrame() && !menuWindow) Ui.ToggleCreativePanel();
            if (i.FreeFlight.WasPressedThisFrame() && !menuWindow && !Ui.WindowOpen && !CameraRig.TacticalActive) ToggleFlight();
            if (i.WorldMap.WasPressedThisFrame() && !menuWindow) Ui.ToggleWorldMap();
            if (i.MinimapZoomIn.WasPressedThisFrame()) Ui.ZoomMinimap(-1);
            if (i.MinimapZoomOut.WasPressedThisFrame()) Ui.ZoomMinimap(1);

            if (!GameplayInputActive) return;
            for (int h = 0; h < i.Hotbar.Length; h++)
                if (i.Hotbar[h].WasPressedThisFrame()) Session.SelectHotbar(h);
            if (i.Drop.WasPressedThisFrame())
            {
                OpResult r = Session.DropFromSlot(Session.Player.SelectedHotbar, 1);
                if (r.Ok) Toast(r.Message);
            }
        }

        private void UpdateInputMaps()
        {
            Input.SetMode(GameplayInputActive || PlacementInputActive, TacticalInputActive);
        }

        private void UpdateCursor()
        {
            bool free = Ui.AnyWindowOpen || CameraRig.TacticalActive || IsDead;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        private static bool IsPausingWindow(IWindow w) => w is PauseWindow || w is SettingsWindow || w is HelpWindow || w is LoadWindow;

        public void SetTactical(bool on)
        {
            if (on == CameraRig.TacticalActive) return;
            Build.Cancel();
            CameraRig.SetTactical(on, Player.transform.position);
            Tactical.SetVisible(on);
        }

        // ------------------------------------------------------------------ interaction

        private void UpdateInteraction()
        {
            CurrentPrompt = null;
            _target = null;
            if (!GameplayInputActive || Build.IsPlacing) return;
            _target = FindTarget();
            if (_target != null) CurrentPrompt = _target.Prompt(this);
            else if (NearWater()) CurrentPrompt = "[E] Drink from the lake";

            GameInput i = Input;
            if (i.Interact.WasPressedThisFrame())
            {
                _holdTick = 0f;
                if (_target != null) _target.Interact(this);
                else if (NearWater())
                {
                    OpResult r = Session.DrinkFromWater();
                    Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
                }
            }
            else if (i.Interact.IsPressed() && _target != null && _target.HoldToInteract)
            {
                _holdTick += Time.deltaTime;
                if (_holdTick >= 0.25f)
                {
                    _target.InteractHeld(this, _holdTick);
                    _holdTick = 0f;
                }
            }

            if (_target is StructureView door && door.Def.IsDoor && i.Rotate.WasPressedThisFrame() && Session.World.Structures.TryGetValue(door.Id, out Afterfall.Domain.Construction.Structure ds))
            {
                OpResult locked = Session.SetDoorLocked(door.Id, !ds.DoorLocked);
                Toast(locked.Message, locked.Ok ? Severity.Info : Severity.Warning);
            }

            if (_target is StructureView sv && i.Deconstruct.IsPressed())
            {
                _demolishHold += Time.deltaTime;
                CurrentPrompt = $"Demolishing {sv.Def.Name}… {Mathf.Clamp01(_demolishHold / 1.2f) * 100:0}%";
                if (_demolishHold >= 1.2f)
                {
                    _demolishHold = 0f;
                    OpResult r = Session.Demolish(sv.Id);
                    Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
                }
            }
            else _demolishHold = 0f;

            if (i.Primary.WasPressedThisFrame() || (i.Primary.IsPressed() && Session.Now >= Session.Player.NextActionTime && _target is ResourceNodeView))
            {
                if (_target is ResourceNodeView node) HarvestTarget(node);
                else MeleeAttack();
            }
        }

        private IInteractable FindTarget()
        {
            Ray ray = CameraRig.AimRay(false);
            float camToPlayer = Vector3.Distance(ray.origin, Player.transform.position + Vector3.up * 1.4f);
            if (Physics.Raycast(ray, out RaycastHit hit, camToPlayer + 3.5f, Layers.InteractMask, QueryTriggerInteraction.Collide))
            {
                IInteractable it = hit.collider.GetComponentInParent<IInteractable>();
                if (it != null && Vector3.Distance(Player.transform.position, hit.point) <= GameSession.InteractRange + 1.2f) return it;
            }
            // Fallback: nearest interactable in front of the player (helps at close range).
            Collider[] near = Physics.OverlapSphere(Player.transform.position + Player.transform.forward * 1.2f + Vector3.up, 1.6f, Layers.Mask(Layers.Interactable, Layers.Resource, Layers.Structure), QueryTriggerInteraction.Collide);
            IInteractable best = null;
            float bestD = float.MaxValue;
            foreach (Collider c in near)
            {
                IInteractable it = c.GetComponentInParent<IInteractable>();
                if (it == null) continue;
                float d = Vector3.Distance(Player.transform.position, it.FocusPoint);
                if (d < bestD)
                {
                    bestD = d;
                    best = it;
                }
            }
            return best;
        }

        private bool NearWater()
        {
            Vector3 p = Player.transform.position;
            Afterfall.Domain.World.WorldLayout l = Session.World.Layout;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                if (l.IsWater(p.x + Mathf.Cos(a) * 2.5f, p.z + Mathf.Sin(a) * 2.5f)) return true;
            }
            return false;
        }

        public void HarvestTarget(ResourceNodeView node)
        {
            if (Session.Now < Session.Player.NextActionTime) return;
            var gained = new List<ItemAmount>();
            OpResult r = Session.Harvest(node.Id, gained);
            if (r.Code == "busy") return;
            Player.FaceAim();
            Player.Rig.PlaySwing();
            Audio.PlayAt("swing", Player.transform.position + Vector3.up, 0.4f);
            if (r.Ok)
            {
                CameraRig.Shake(0.15f);
                Toast(r.Message, Severity.Info);
            }
            else ToastThrottled(r.Message, Severity.Warning);
        }

        private void MeleeAttack()
        {
            if (Session.Now < Session.Player.NextActionTime) return;
            Player.FaceAim(0.5f);
            float range = Session.AttackRange;
            Vector3 center = Player.transform.position + Vector3.up * 1.0f + CameraForwardFlat() * (range * 0.6f);
            Collider[] hits = Physics.OverlapSphere(center, range * 0.7f, Layers.Mask(Layers.Zombie), QueryTriggerInteraction.Ignore);
            var ids = new List<EntityId>();
            foreach (Collider c in hits)
            {
                ZombieView z = c.GetComponentInParent<ZombieView>();
                if (z != null && !ids.Contains(z.Id)) ids.Add(z.Id);
            }
            OpResult r = Session.MeleeAttack(ids);
            if (r.Code == "busy") return;
            Player.Rig.PlaySwing();
            Audio.PlayAt("swing", Player.transform.position + Vector3.up, 0.6f);
            if (!r.Ok)
            {
                ToastThrottled(r.Message, Severity.Warning);
                return;
            }
            foreach (EntityId id in ids)
            {
                ZombieView v = Sync.ZombieViewOf(id);
                if (v == null) continue;
                v.Rig.Flash();
                Audio.PlayAt("hit_flesh", v.transform.position + Vector3.up * 1.2f, 0.9f);
            }
            if (ids.Count > 0) CameraRig.Shake(0.25f);
        }

        private Vector3 CameraForwardFlat()
        {
            Vector3 f = Vector3.ProjectOnPlane(CameraRig.ViewTransform.forward, Vector3.up);
            return f.sqrMagnitude > 0.01f ? f.normalized : Player.transform.forward;
        }

        // ------------------------------------------------------------------ helpers

        public float GroundHeight(Vector3 p) => Session.World.Layout.HeightAt(p.x, p.z);

        public void Toast(string message, Severity severity = Severity.Info) => Ui.Toast(message, severity);

        /// <summary>Shows a message at most once every 2 seconds (for repeated failure reasons).</summary>
        public void ToastThrottled(string message, Severity severity)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_throttle.TryGetValue(message, out float until) && Time.unscaledTime < until) return;
            _throttle[message] = Time.unscaledTime + 2f;
            Toast(message, severity);
        }

        public void Save(string slot)
        {
            CurrentSlot = slot;
            App.Saves.Save(Session, slot, Playtime, out _);
        }

        // ------------------------------------------------------------------ creative: flight and teleport

        public void ToggleFlight()
        {
            Player.Flight.Toggle(out string message);
            if (!string.IsNullOrEmpty(message)) Toast(message, Player.Flight.Active || message == "Landed." ? Severity.Info : Severity.Warning);
        }

        /// <summary>The inventory of the last container the player opened (target for spawning into a container).</summary>
        public EntityId LastContainer { get; set; }

        /// <summary>
        /// True when the world around a point is ready to stand in. The whole map is built at
        /// load today; sector streaming will answer this per sector, and teleports already wait on it.
        /// </summary>
        public bool IsWorldReadyAt(Vector3 position) => Running;

        /// <summary>
        /// Teleports the survivor (developer tools): waits until the destination is ready, then lands
        /// on safe ground near it (a building's floor, never its roof). While flying, the flight
        /// camera moves there instead, 25 m above the ground.
        /// </summary>
        public void TeleportTo(Vector3 target, string label, bool recordHistory = true)
        {
            StartCoroutine(TeleportRoutine(target, label, recordHistory));
        }

        /// <summary>Set while a teleport waits for its destination (the tour waits on it).</summary>
        public bool Teleporting { get; private set; }

        private IEnumerator TeleportRoutine(Vector3 target, string label, bool recordHistory)
        {
            if (recordHistory)
            {
                OpResult ok = Session.Creative.BeginTeleport(target.ToSim());
                if (!ok.Ok)
                {
                    Toast(ok.Message, Severity.Warning);
                    yield break;
                }
            }
            Teleporting = true;
            float timeout = Time.realtimeSinceStartup + 30f;
            while (!IsWorldReadyAt(target) && Time.realtimeSinceStartup < timeout) yield return null;
            if (!IsWorldReadyAt(target))
            {
                Teleporting = false;
                Toast("The destination did not finish loading; teleport cancelled.", Severity.Warning);
                yield break;
            }
            float ground = GroundHeight(target);
            if (Player.Flight.Active)
            {
                Player.Flight.MoveTo(new Vector3(target.x, Mathf.Max(target.y, ground) + 25f, target.z));
                Teleporting = false;
                Toast($"Flew to {label}.", Severity.Info);
                yield break;
            }
            if (!SafeGround.TryFind(this, target, ground + 300f, 40f, out Vector3 spot))
            {
                Teleporting = false;
                Toast($"No safe ground near {label}.", Severity.Warning);
                yield break;
            }
            Player.Teleport(spot);
            Session.ReportPlayerTransform(spot.ToSim(), Session.Player.Yaw, new Afterfall.Domain.World.PlayerActivity());
            Teleporting = false;
            Toast($"Teleported to {label}.", Severity.Info);
        }

        /// <summary>Returns to where the last teleport started.</summary>
        public void TeleportBack()
        {
            if (!Session.Creative.TryPopPrevious(out Vec3 back))
            {
                Toast("No earlier location to return to.", Severity.Warning);
                return;
            }
            TeleportTo(back.ToUnity(), "your previous location", false);
        }

        public void OnRespawned()
        {
            Player.Teleport(Session.Player.Position.ToUnity() + Vector3.up * 0.2f);
        }
    }
}
