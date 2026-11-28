using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// The real buildings of Kesselmouth around the survivor. A city block within
    /// <see cref="BuildRadius"/> is built from its plan (made on a worker): its outside in full and the
    /// floors around the survivor's floor with their walls, stairs and furniture (one floor per frame),
    /// while its merged stand-in is left out of the chunk's city mesh. As the survivor climbs, floors
    /// are built above and dropped below; their doors and furniture are seeded in the simulation as
    /// they come into range. Beyond <see cref="DropRadius"/> the stand-in comes back first, then the
    /// building goes. Tall buildings have a lift on every floor.
    /// </summary>
    public sealed class CityBuildingViews : MonoBehaviour
    {
        public const float BuildRadius = 45f, DropRadius = 75f;

        private sealed class Entry
        {
            public string Id;
            public long Chunk;
            public PropPlacement Block;
            public Task<PlacedPlan> Planning;
            public PlacedPlan Placed;
            public GameObject Root, Shell;
            public readonly Dictionary<int, GameObject> Floors = new Dictionary<int, GameObject>();
            public int From = -1, To = -2, WantFrom = -1, WantTo = -2;
            public bool Leaving;
            public int LeaveVersion;

            /// <summary>The floor a lift ride is taking the survivor to (its floors are kept while it gets ready), or -1.</summary>
            public int Ride = -1;
        }

        private GameController _gc;
        private WorldLayout _layout;
        private Transform _root;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private float _scan;

        public int Count => _entries.Count;
        public int BuiltFloors
        {
            get
            {
                int n = 0;
                foreach (Entry e in _entries.Values) n += e.Floors.Count;
                return n;
            }
        }

        public void Initialize(GameController gc, WorldLayout layout, Transform root)
        {
            _gc = gc;
            _layout = layout;
            _root = root;
            if (gc.Chunks != null) gc.Chunks.Excluded = ExcludedIn;
        }

        private HashSet<string> ExcludedIn(long chunk)
        {
            var set = new HashSet<string>();
            foreach (Entry e in _entries.Values)
                if (e.Chunk == chunk && e.Root != null && !e.Leaving) set.Add(e.Id);
            return set;
        }

        /// <summary>The real building the survivor is in (or next to), if any.</summary>
        public PlacedPlan BuildingAt(Vector3 unityPosition, out int floor)
        {
            floor = -1;
            Vec3 p = unityPosition.ToSim();
            foreach (Entry e in _entries.Values)
            {
                if (e.Placed == null) continue;
                if (e.Placed.ToPlan(p, out float x, out float z, out int f))
                {
                    floor = f;
                    return e.Placed;
                }
            }
            return null;
        }

        private void Update()
        {
            if (_gc == null || !_gc.Running || _gc.Player == null) return;
            Vector3 me = _gc.Player.transform.position.ToWorld();

            // Plans made on workers become buildings; one floor (or a shell) is built per frame.
            bool built = false;
            foreach (Entry e in _entries.Values)
            {
                if (e.Planning != null && e.Planning.IsCompleted)
                {
                    e.Placed = e.Planning.Result;
                    e.Planning = null;
                    Create(e);
                }
                if (built || e.Root == null || e.Leaving) continue;
                built = Step(e);
            }

            _scan -= Time.unscaledDeltaTime;
            if (_scan > 0f) return;
            _scan = 0.25f;
            Scan(me);
        }

        private void Scan(Vector3 me)
        {
            // Blocks near the survivor, in the chunks around them.
            int cx = WorldLayout.ChunkOf(me.x), cz = WorldLayout.ChunkOf(me.z);
            var near = new HashSet<string>();
            for (int z = cz - 1; z <= cz + 1; z++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    ChunkContent c = _layout.GetChunk(x, z);
                    if (c == null) continue;
                    foreach (PropPlacement b in c.Blocks)
                    {
                        if (!BuildingPlans.IsCityBlock(b.Kind)) continue;
                        float d = Distance(me, b);
                        if (d > DropRadius) continue;
                        string id = BuildingPlans.CityBlockPlanId(b);
                        near.Add(id);
                        if (d < BuildRadius && !_entries.ContainsKey(id))
                        {
                            PropPlacement block = b;
                            _entries[id] = new Entry { Id = id, Chunk = c.Key, Block = b, Planning = Task.Run(() => BuildingPlans.PlaceCityBlock(block)) };
                        }
                    }
                }

            var gone = new List<string>();
            foreach (Entry e in _entries.Values)
            {
                if (!near.Contains(e.Id) && !e.Leaving)
                {
                    // Out of range: bring the stand-in back, then remove the building once it is drawn.
                    e.Leaving = true;
                    if (e.Root == null)
                    {
                        gone.Add(e.Id);
                        continue;
                    }
                    e.LeaveVersion = _gc.Chunks.DetailVersion(e.Chunk);
                    _gc.Chunks.Rebuild(e.Chunk);
                    continue;
                }
                if (e.Leaving)
                {
                    int v = _gc.Chunks.DetailVersion(e.Chunk);
                    if (v < 0 || v > e.LeaveVersion) gone.Add(e.Id);
                    continue;
                }
                if (e.Placed == null) continue;
                // Which floors to build: around the survivor's floor inside, the ground floor and the next from outside.
                Vec3 p = me.FromWorld().ToSim();
                int floor;
                bool inside = e.Placed.ToPlan(p, out _, out _, out floor);
                int n = e.Placed.Plan.Floors;
                if (e.Ride >= 0)
                {
                    e.WantFrom = Mathf.Clamp(e.Ride - 1, 0, n - 1);
                    e.WantTo = Mathf.Clamp(e.Ride + 1, 0, n - 1);
                }
                else if (inside)
                {
                    e.WantFrom = Mathf.Clamp(floor - 1, 0, n - 1);
                    e.WantTo = Mathf.Clamp(floor + 1, 0, n - 1);
                }
                else if (Distance(me, e.Block) < 22f)
                {
                    e.WantFrom = 0;
                    e.WantTo = Mathf.Min(1, n - 1);
                }
                else
                {
                    e.WantFrom = 0;
                    e.WantTo = -1; // just the outside
                }
            }
            foreach (string id in gone) Remove(id);
        }

        /// <summary>Distance from a point (world) to a block's footprint.</summary>
        private static float Distance(Vector3 me, PropPlacement b)
        {
            Vec3 local = WorldGenerator.Rotate(new Vec3(me.x - b.Position.X, 0f, me.z - b.Position.Z), -b.Yaw);
            float dx = Mathf.Max(0f, Mathf.Abs(local.X) - b.Size.X * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(local.Z) - b.Size.Z * 0.5f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void Create(Entry e)
        {
            if (e.Leaving || e.Placed == null) return;
            e.Root = new GameObject("Building " + e.Placed.Plan.Name);
            e.Root.transform.SetParent(_root, false);
            Vec3 o = e.Placed.Origin;
            e.Root.transform.localPosition = new Vector3(o.X, o.Y, o.Z); // world coordinates under the anchor
            e.Root.transform.localRotation = Quaternion.Euler(0f, e.Placed.Yaw, 0f);
            Shapes.SetLayerRecursive(e.Root, Layers.Prop);
            _gc.Session.City.Activate(e.Placed);
            e.WantFrom = 0;
            e.WantTo = -1;
            Shell(e, 0, -1);
            // The stand-in leaves the chunk mesh now the building stands.
            _gc.Chunks.Rebuild(e.Chunk);
        }

        /// <summary>The slowest floor build since <see cref="ResetTimings"/>, in ms, and what it spent (performance checks).</summary>
        public float SlowestFloorMs { get; private set; }

        public string SlowestFloor { get; private set; } = "";

        public void ResetTimings()
        {
            SlowestFloorMs = 0f;
            SlowestFloor = "";
        }

        /// <summary>Builds or drops one floor towards the wanted range; returns true if it did any work.</summary>
        private bool Step(Entry e)
        {
            BuildingPlan plan = e.Placed.Plan;
            // Drop floors out of range first (cheap), then build the nearest missing floor. During a lift
            // ride nothing is dropped: the survivor still stands on the floor they are leaving.
            foreach (KeyValuePair<int, GameObject> kv in e.Floors)
                if (e.Ride < 0 && (kv.Key < e.WantFrom || kv.Key > e.WantTo))
                {
                    DestroyBuilt(kv.Value);
                    e.Floors.Remove(kv.Key);
                    Reshell(e);
                    return true;
                }
            // The floor in the middle of the range (the survivor's, or a lift's destination) first.
            int mid = e.Ride >= 0 ? e.Ride : (e.WantFrom + e.WantTo) / 2;
            for (int k = 0; k <= 2 * (e.WantTo - e.WantFrom + 1); k++)
            {
                int f = mid + (k % 2 == 0 ? k / 2 : -(k + 1) / 2);
                if (f < e.WantFrom || f > e.WantTo || e.Floors.ContainsKey(f)) continue;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var go = new GameObject($"Floor {f}");
                go.transform.SetParent(e.Root.transform, false);
                PlanBuilder.BuildFloor(go.transform, plan, f);
                AddLifts(go.transform, e, f);
                Shapes.SetLayerRecursive(go, Layers.Prop);
                e.Floors[f] = go;
                float build = (float)sw.Elapsed.TotalMilliseconds;
                int seeded = _gc.Session.City.SeedFloors(e.Placed, f, f);
                float seed = (float)sw.Elapsed.TotalMilliseconds - build;
                Reshell(e);
                float total = (float)sw.Elapsed.TotalMilliseconds;
                if (total > SlowestFloorMs)
                {
                    SlowestFloorMs = total;
                    SlowestFloor = $"{plan.Name} floor {f}: {total:0.0} ms (meshes {build:0.0}, {seeded} fittings {seed:0.0}, shell {total - build - seed:0.0})";
                }
                Vec3 o = e.Placed.Origin;
                float r = Mathf.Max(plan.Width, plan.Depth);
                _gc.MarkNavDirty(new Rect(o.X - r, o.Z - r, 2f * r, 2f * r));
                return true;
            }
            return false;
        }

        /// <summary>The outside around the built floors (they are always a run of consecutive floors).</summary>
        private void Reshell(Entry e)
        {
            int from = int.MaxValue, to = int.MinValue;
            foreach (int f in e.Floors.Keys)
            {
                from = Mathf.Min(from, f);
                to = Mathf.Max(to, f);
            }
            if (e.Floors.Count == 0)
            {
                from = 0;
                to = -1;
            }
            if (from == e.From && to == e.To && e.Shell != null) return;
            Shell(e, from, to);
        }

        private void Shell(Entry e, int from, int to)
        {
            if (e.Shell != null) DestroyBuilt(e.Shell);
            e.Shell = new GameObject("Shell");
            e.Shell.transform.SetParent(e.Root.transform, false);
            PlanBuilder.BuildShell(e.Shell.transform, e.Placed.Plan, from, to);
            Shapes.SetLayerRecursive(e.Shell, Layers.Prop);
            e.From = from;
            e.To = to;
        }

        private void AddLifts(Transform floor, Entry e, int f)
        {
            BuildingPlan plan = e.Placed.Plan;
            if (!plan.Lift) return;
            foreach (PlanDecor d in plan.Decor)
            {
                if (d.Floor != f || d.Kind != "lift") continue;
                var go = new GameObject("Lift");
                go.transform.SetParent(floor, false);
                go.transform.localPosition = new Vector3(d.X, plan.FloorY(f) + 1.1f, d.Z);
                go.transform.localRotation = Quaternion.Euler(0f, d.Yaw, 0f);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(1.5f, 2.2f, 0.5f);
                box.center = new Vector3(0f, 0f, 0.25f);
                box.isTrigger = true;
                go.layer = Layers.Interactable;
                LiftPanel panel = go.AddComponent<LiftPanel>();
                panel.Views = this;
                panel.BuildingId = e.Id;
                panel.Floor = f;
            }
        }

        private static void DestroyBuilt(GameObject go)
        {
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>()) Destroy(mf.sharedMesh);
            Destroy(go);
        }

        private void Remove(string id)
        {
            if (!_entries.TryGetValue(id, out Entry e)) return;
            if (e.Root != null) DestroyBuilt(e.Root);
            if (e.Placed != null) _gc.Session.City.Deactivate(e.Placed.Plan.Id);
            _entries.Remove(id);
        }

        /// <summary>The floors and name of a building by id, for the lift window.</summary>
        public PlacedPlan Building(string id) => _entries.TryGetValue(id, out Entry e) ? e.Placed : null;

        /// <summary>
        /// Takes the lift: the survivor steps out on <paramref name="floor"/> in front of the lift
        /// doors, once that floor is built (its neighbours follow).
        /// </summary>
        public void TakeLift(string id, int floor)
        {
            if (!_entries.TryGetValue(id, out Entry e) || e.Placed == null) return;
            BuildingPlan plan = e.Placed.Plan;
            floor = Mathf.Clamp(floor, 0, plan.Floors - 1);
            StartCoroutine(Ride(e, floor));
        }

        private System.Collections.IEnumerator Ride(Entry e, int floor)
        {
            BuildingPlan plan = e.Placed.Plan;
            e.Ride = floor;
            e.WantFrom = Mathf.Max(0, floor - 1);
            e.WantTo = Mathf.Min(plan.Floors - 1, floor + 1);
            float until = Time.realtimeSinceStartup + 10f;
            while (!e.Floors.ContainsKey(floor) && Time.realtimeSinceStartup < until && e.Root != null)
            {
                Step(e);
                yield return null;
            }
            PlanDecor lift = plan.Decor.Find(d => d.Floor == floor && d.Kind == "lift");
            if (lift == null || !e.Floors.ContainsKey(floor))
            {
                e.Ride = -1;
                _gc.Toast("The lift doors will not open. Try again.", Afterfall.Domain.Events.Severity.Warning);
                yield break;
            }
            // A step out of the doors, facing away from them.
            Vec3 at = e.Placed.ToWorld(lift.X + 0.9f, lift.Z, floor);
            Vector3 target = new Vector3(at.X, at.Y + 0.05f, at.Z).FromWorld();
            Physics.SyncTransforms();
            _gc.Player.Teleport(target);
            _gc.Session.ReportPlayerTransform(target.ToSim(), _gc.Session.Player.Yaw, new PlayerActivity());
            _gc.Audio?.Play2D("click", 0.6f);
            _gc.Toast(floor == 0 ? $"{plan.Name}: ground floor." : $"{plan.Name}: floor {floor}.", Afterfall.Domain.Events.Severity.Info);
            // From here the survivor's own floor decides what is built.
            yield return null;
            e.Ride = -1;
        }
    }

    /// <summary>A lift's call panel: opens the floor list.</summary>
    public sealed class LiftPanel : MonoBehaviour, IInteractable
    {
        public CityBuildingViews Views;
        public string BuildingId;
        public int Floor;

        public string Prompt(GameController gc) => $"[E] Take the lift (you are on {(Floor == 0 ? "the ground floor" : "floor " + Floor)})";

        public void Interact(GameController gc)
        {
            PlacedPlan building = Views.Building(BuildingId);
            if (building != null) gc.Ui.OpenWindow(new UI.LiftWindow(gc.Ui, gc, Views, BuildingId, building, Floor));
        }

        public bool HoldToInteract => false;
        public void InteractHeld(GameController gc, float dt) { }
        public bool PrimaryHarvests => false;
        public Vector3 FocusPoint => transform.position;
    }
}
