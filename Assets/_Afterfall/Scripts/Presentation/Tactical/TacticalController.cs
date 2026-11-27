using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.World;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Strategic-mode interaction: selecting structures, issuing orders (recipes, power,
    /// switches), electrical network overlay, fog of war and recently seen threats. The view
    /// only shows what the survivor knows: unexplored terrain is fogged and infected are only
    /// marked while in the survivor's line of sight.
    /// </summary>
    public sealed class TacticalController : MonoBehaviour
    {
        private GameController _gc;
        private readonly List<LineRenderer> _cables = new List<LineRenderer>();
        private Transform _cableRoot;
        private GameObject _selectionRing;
        private GameObject _fog;
        private Texture2D _fogTex;
        private int _fogVersion = -1;
        private int _cableVersion = -1;
        private float _cableRefresh;
        private readonly Dictionary<EntityId, float> _seenZombies = new Dictionary<EntityId, float>();

        public EntityId Selected { get; private set; }

        /// <summary>When set, the next selected structure is connected to this one.</summary>
        public EntityId LinkSource { get; private set; }

        /// <summary>The next left click on explored ground marks a gathering area.</summary>
        public bool PlacingZone { get; private set; }

        private Transform _zoneRoot;
        private GameObject _zonePreview;
        private readonly List<GameObject> _zoneDiscs = new List<GameObject>();
        private Material _zoneMat;

        public IReadOnlyDictionary<EntityId, float> SeenZombies => _seenZombies;

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _cableRoot = new GameObject("PowerOverlay").transform;
            _cableRoot.SetParent(transform, false);
            _selectionRing = Shapes.Part(transform, "SelectionRing", MeshFactory.Cylinder(24), MaterialLibrary.UnlitTransparent(new Color(1f, 0.85f, 0.3f, 0.45f)), Vector3.zero, new Vector3(3, 0.05f, 3), null, false);
            _selectionRing.SetActive(false);
            _zoneRoot = new GameObject("WorkZones").transform;
            _zoneRoot.SetParent(transform, false);
            _zoneMat = MaterialLibrary.UnlitTransparent(new Color(0.45f, 0.9f, 0.4f, 0.16f));
            _zonePreview = Shapes.Part(_zoneRoot, "ZonePreview", MeshFactory.Cylinder(48), MaterialLibrary.UnlitTransparent(new Color(0.95f, 0.9f, 0.4f, 0.22f)), Vector3.zero, Vector3.one, null, false);
            _zonePreview.SetActive(false);
            BuildFog();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            _cableRoot.gameObject.SetActive(visible);
            _zoneRoot.gameObject.SetActive(visible);
            if (_fog != null) _fog.SetActive(visible);
            if (!visible)
            {
                _selectionRing.SetActive(false);
                LinkSource = EntityId.None;
                PlacingZone = false;
            }
        }

        public void Select(EntityId id)
        {
            Selected = id;
            LinkSource = EntityId.None;
        }

        /// <summary>Starts marking a gathering area: the next left click on explored ground places it (right click cancels).</summary>
        public void BeginZone()
        {
            PlacingZone = true;
            LinkSource = EntityId.None;
            _gc.Toast("Click on explored ground to mark a gathering area (right click cancels).");
        }

        public void BeginLink(EntityId from)
        {
            LinkSource = from;
            _gc.Toast("Select another electrical structure to run a cable (RMB to cancel).");
        }

        private void Update()
        {
            if (_gc?.Session == null) return;
            TrackVisibleZombies();
            if (!_gc.CameraRig.TacticalActive) return;

            if (_gc.TacticalInputActive && !_gc.Build.IsPlacing && !_gc.Ui.PointerOverUi)
            {
                if (_gc.Input.TacSelect.WasPressedThisFrame())
                {
                    if (PlacingZone) PlaceZone();
                    else Pick();
                }
                if (_gc.Input.TacCommand.WasPressedThisFrame())
                {
                    if (PlacingZone) PlacingZone = false;
                    else if (LinkSource.IsValid) LinkSource = EntityId.None;
                    else Selected = EntityId.None;
                }
            }

            UpdateSelectionRing();
            UpdateZones();
            _cableRefresh -= Time.unscaledDeltaTime;
            if (_cableRefresh <= 0f || _cableVersion != _gc.Session.World.PowerTopologyVersion) RefreshCables();
            if (_fogVersion != _gc.Session.World.Exploration.Version) RefreshFog();
        }

        private void Pick()
        {
            Ray ray = _gc.CameraRig.AimRay(true);
            if (!Physics.Raycast(ray, out RaycastHit hit, 800f, Layers.InteractMask, QueryTriggerInteraction.Collide)) return;
            StructureView view = hit.collider.GetComponentInParent<StructureView>();
            if (view == null)
            {
                Selected = EntityId.None;
                return;
            }
            if (!_gc.Session.World.Exploration.IsRevealed(view.transform.position.x, view.transform.position.z)) return;
            if (LinkSource.IsValid && view.Id != LinkSource)
            {
                OpResult r = _gc.Session.ConnectPower(LinkSource, view.Id);
                _gc.Toast(r.Message, r.Ok ? Severity.Success : Severity.Warning);
                LinkSource = EntityId.None;
                return;
            }
            Selected = view.Id;
        }

        private bool GroundUnderCursor(out Vector3 point)
        {
            point = default;
            Ray ray = _gc.CameraRig.AimRay(true);
            if (!Physics.Raycast(ray, out RaycastHit hit, 800f, Layers.Mask(Layers.Terrain), QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        private void PlaceZone()
        {
            if (!GroundUnderCursor(out Vector3 p)) return;
            OpResult r = _gc.Session.AddWorkZone(p.ToSim(), WorkZone.DefaultRadius, out _);
            _gc.Toast(r.Message, r.Ok ? Severity.Success : Severity.Warning);
            if (r.Ok) PlacingZone = false;
        }

        /// <summary>A translucent disc per gathering area, plus a preview under the cursor while marking one.</summary>
        private void UpdateZones()
        {
            int i = 0;
            foreach (WorkZone z in _gc.Session.World.WorkZones.Values)
            {
                if (i == _zoneDiscs.Count) _zoneDiscs.Add(Shapes.Part(_zoneRoot, "Zone", MeshFactory.Cylinder(48), _zoneMat, Vector3.zero, Vector3.one, null, false));
                GameObject disc = _zoneDiscs[i++];
                disc.SetActive(true);
                Vector3 c = z.Center.ToUnity();
                c.y = _gc.GroundHeight(c);
                disc.transform.position = c;
                disc.transform.localScale = new Vector3(z.Radius * 2f, 1.2f, z.Radius * 2f);
            }
            for (; i < _zoneDiscs.Count; i++) _zoneDiscs[i].SetActive(false);
            bool preview = PlacingZone && GroundUnderCursor(out Vector3 at);
            _zonePreview.SetActive(preview);
            if (!preview) return;
            GroundUnderCursor(out Vector3 p);
            _zonePreview.transform.position = p;
            _zonePreview.transform.localScale = new Vector3(WorkZone.DefaultRadius * 2f, 1.2f, WorkZone.DefaultRadius * 2f);
        }

        private void UpdateSelectionRing()
        {
            if (!Selected.IsValid || !_gc.Session.World.Structures.TryGetValue(Selected, out Structure s))
            {
                _selectionRing.SetActive(false);
                return;
            }
            BuildingDefinition d = _gc.Session.Content.Building(s.DefId);
            float size = Mathf.Max(d.SizeX, d.SizeZ) + 1f;
            _selectionRing.SetActive(true);
            _selectionRing.transform.position = s.Position.ToUnity() + Vector3.up * 0.05f;
            _selectionRing.transform.localScale = new Vector3(size, 0.05f, size) * (1f + Mathf.PingPong(Time.unscaledTime * 0.3f, 0.08f));
        }

        private void RefreshCables()
        {
            _cableRefresh = 0.5f;
            _cableVersion = _gc.Session.World.PowerTopologyVersion;
            List<PowerLink> links = _gc.Session.World.PowerLinks;
            while (_cables.Count < links.Count)
            {
                var go = new GameObject("Cable");
                go.transform.SetParent(_cableRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.widthMultiplier = 0.25f;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cables.Add(lr);
            }
            for (int i = 0; i < _cables.Count; i++)
            {
                bool active = i < links.Count;
                _cables[i].gameObject.SetActive(active);
                if (!active) continue;
                PowerLink l = links[i];
                if (!_gc.Session.World.Structures.TryGetValue(l.A, out Structure a) || !_gc.Session.World.Structures.TryGetValue(l.B, out Structure b)) continue;
                bool live = (a.Generator != null && a.Generator.Running) || (b.Generator != null && b.Generator.Running) || (a.Power != null && a.Power.Powered) || (b.Power != null && b.Power.Powered) || HasLiveNetwork(a);
                Color c = live ? new Color(1f, 0.85f, 0.2f, 0.9f) : new Color(0.55f, 0.55f, 0.6f, 0.8f);
                _cables[i].sharedMaterial = MaterialLibrary.UnlitTransparent(c);
                _cables[i].SetPosition(0, a.Position.ToUnity() + Vector3.up * 2.5f);
                _cables[i].SetPosition(1, b.Position.ToUnity() + Vector3.up * 2.5f);
            }
        }

        private bool HasLiveNetwork(Structure s)
        {
            Domain.Power.PowerNetworkInfo net = _gc.Session.Power.NetworkOf(s.Id);
            return net != null && net.SuppliedWatts > 0f;
        }

        private void TrackVisibleZombies()
        {
            float now = Time.time;
            foreach (KeyValuePair<EntityId, ZombieView> kv in _gc.Sync.Zombies)
            {
                if (kv.Value == null) continue;
                Vector3 eye = _gc.Player.transform.position + Vector3.up * 1.6f;
                Vector3 target = kv.Value.transform.position + Vector3.up * 1.2f;
                if (Vector3.Distance(eye, target) > 70f) continue;
                if (Physics.Linecast(eye, target, Layers.SolidMask, QueryTriggerInteraction.Ignore)) continue;
                _seenZombies[kv.Key] = now;
            }
            var stale = new List<EntityId>();
            foreach (KeyValuePair<EntityId, float> kv in _seenZombies)
                if (now - kv.Value > 20f || !_gc.Session.World.Zombies.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (EntityId id in stale) _seenZombies.Remove(id);
        }

        private void BuildFog()
        {
            ExplorationState e = _gc.Session.World.Exploration;
            int n = e.CellsPerSide;
            _fogTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "FogOfWar" };
            // A terrain-following grid so the fog sits just above the ground everywhere.
            WorldLayout l = _gc.Session.World.Layout;
            int verts = n + 1;
            var mesh = new Mesh { name = "FogGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var v = new Vector3[verts * verts];
            var uv = new Vector2[verts * verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                {
                    float wx = Mathf.Min(x * ExplorationState.CellSize, l.Size);
                    float wz = Mathf.Min(z * ExplorationState.CellSize, l.Size);
                    float h = Mathf.Max(l.HeightAt(wx, wz), l.WaterLevel) + 9f;
                    v[z * verts + x] = new Vector3(wx, h, wz);
                    uv[z * verts + x] = new Vector2(wx / (n * ExplorationState.CellSize), wz / (n * ExplorationState.CellSize));
                }
            var tris = new int[n * n * 6];
            int t = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * verts + x;
                    tris[t++] = i;
                    tris[t++] = i + verts;
                    tris[t++] = i + 1;
                    tris[t++] = i + 1;
                    tris[t++] = i + verts;
                    tris[t++] = i + verts + 1;
                }
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _fog = new GameObject("FogOfWar");
            _fog.transform.SetParent(transform, false);
            _fog.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = _fog.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.UnlitTransparentTextured(_fogTex);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            RefreshFog();
        }

        private void RefreshFog()
        {
            ExplorationState e = _gc.Session.World.Exploration;
            _fogVersion = e.Version;
            int n = e.CellsPerSide;
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = e.Revealed[i] ? new Color32(0, 0, 0, 0) : new Color32(12, 14, 18, 235);
            _fogTex.SetPixels32(px);
            _fogTex.Apply(false);
        }

        /// <summary>Short status line for a structure (used by overlay labels and the selection panel).</summary>
        public static string StatusLine(GameController gc, Structure s)
        {
            BuildingDefinition d = gc.Session.Content.Building(s.DefId);
            if (!s.IsComplete) return $"Blueprint — {gc.Session.Construction.WorkProgress01(s) * 100:0}%";
            if (d.IsBroken(s.Health) && s.PlayerBuilt) return $"Broken ({s.Health:0}/{d.MaxHealth:0}) — repair it with a hammer";
            if (s.Machine != null) return UI.UiKit.StatusText(s.Machine);
            if (s.Generator != null) return s.Generator.Status;
            if (s.Heater != null) return s.Heater.Status;
            if (s.Battery != null) return $"Stored {s.Battery.StoredJoules / 1000:0}/{d.Battery.CapacityKJ:0} kJ ({s.Battery.FlowWatts:+0;-0;0} W)";
            if (d.Light != null) return s.Power?.Reason ?? "";
            if (s.Storage != null) return $"{CountUsed(s)} / {s.Storage.SlotCount} slots used";
            return d.Description;
        }

        private static int CountUsed(Structure s)
        {
            int n = 0;
            for (int i = 0; i < s.Storage.SlotCount; i++)
                if (!s.Storage.GetSlot(i).IsEmpty) n++;
            return n;
        }

        public static bool IsBlocked(Structure s) => s.Machine != null && s.Machine.Status != MachineStatus.Working && s.Machine.Status != MachineStatus.Idle;
    }
}
