using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;
using Afterfall.Presentation.Art;
using UnityEngine;
using UnityEngine.Rendering;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Ground detail around the camera: grass tufts, wildflowers, ferns, reeds, pebbles and leaf
    /// litter, drawn with GPU instancing (no GameObjects, no colliders). The ground is split into
    /// 10 m cells; a cell's instances are generated deterministically from its coordinates, the
    /// terrain's splat weights and the layout (nothing on roads, rock, water, steep slopes or under
    /// buildings), so the same spot always grows the same plants. Cells are generated within a small
    /// time budget as the camera moves and released behind it. Instances thin out with distance in
    /// the shader, so nothing pops; from the high strategic camera everything is out of range and
    /// nothing is drawn.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class GroundCover : MonoBehaviour
    {
        private const float CellSize = 10f;
        private const float BuildBudgetMs = 1.2f;
        private const int MaxPerDraw = 1023;
        private const float MaxCameraHeight = 70f;

        private static readonly int PusherId = Shader.PropertyToID("_AfterfallGrassPusher");

        private enum Kind
        {
            GrassShort, GrassMedium, GrassTall, GrassDry,
            Daisy, Buttercup, Cornflower, Poppy,
            FernA, FernB,
            PebblesA, PebblesB, PebblesC,
            LitterA, LitterB,
            ReedsA, ReedsB
        }

        private sealed class Variant
        {
            public Mesh Mesh;
            public Material Material;
            public float FadeNear, FadeFar;
            public Matrix4x4[] Draw = new Matrix4x4[256];
            public int Count;
        }

        /// <summary>One variant's instances in a cell, sorted so the ones that survive farthest come first.</summary>
        private sealed class Bucket
        {
            public Matrix4x4[] Items = new Matrix4x4[32];
            /// <summary>1 − the shader's per-instance rank, ascending after <see cref="Sort"/>.</summary>
            public float[] Keys = new float[32];
            public int Count;

            public void Add(Matrix4x4 m, float rank)
            {
                if (Count == Items.Length)
                {
                    Array.Resize(ref Items, Count * 2);
                    Array.Resize(ref Keys, Count * 2);
                }
                Items[Count] = m;
                Keys[Count] = 1f - rank;
                Count++;
            }

            public void Sort()
            {
                if (Count > 1) Array.Sort(Keys, Items, 0, Count);
            }

            /// <summary>How many instances are still drawn at a rank threshold (rank above it).</summary>
            public int CountAbove(float rank)
            {
                if (rank <= 0f) return Count;
                float key = 1f - rank;
                int lo = 0, hi = Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (Keys[mid] < key) lo = mid + 1;
                    else hi = mid;
                }
                return lo;
            }
        }

        private sealed class Cell
        {
            public int X, Z;
            public Bounds Bounds;
            public Bucket[] Buckets;
            public bool Built, Dirty;
        }

        /// <summary>An obstacle the cover must not grow through: an oriented box or, for other colliders, its bounds.</summary>
        private struct Blocker
        {
            public bool IsBox;
            public Matrix4x4 WorldToLocal;
            public Vector3 Center, Half;
            public Bounds Bounds;
        }

        private GameController _gc;
        private WorldLayout _layout;
        private GroundWeights _weights;
        private Variant[] _variants;
        private float _maxFade;
        private int _seed;
        private readonly Dictionary<long, Cell> _cells = new Dictionary<long, Cell>();
        private readonly Stack<Cell> _pool = new Stack<Cell>();
        private readonly List<Cell> _queue = new List<Cell>();
        private readonly List<long> _remove = new List<long>();
        private readonly Collider[] _overlap = new Collider[128];
        private readonly List<Blocker> _blockers = new List<Blocker>();
        private readonly Plane[][] _cameraPlanes = { new Plane[6], new Plane[6], new Plane[6], new Plane[6] };
        private readonly Vector3[] _cameraPositions = new Vector3[4];
        private Camera[] _cameras = new Camera[8];
        private readonly float[] _w = new float[TerrainSurfaces.Count];
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly Dictionary<EntityId, Vector3> _structurePositions = new Dictionary<EntityId, Vector3>();
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _frameClock = new System.Diagnostics.Stopwatch();
        private float _rebuildAt = -1f;
        private Placer _flower, _fern, _pebbles, _litter, _reeds;

        /// <summary>Instances submitted last frame (for the debug overlay and performance checks).</summary>
        public int DrawnInstances { get; private set; }
        /// <summary>Main-thread time this component took last frame, in milliseconds.</summary>
        public float LastCpuMs { get; private set; }
        public int CellCount => _cells.Count;
        /// <summary>Time spent generating cells last frame, in milliseconds.</summary>
        public float LastBuildMs { get; private set; }
        private int _blockerMask;

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _layout = gc.Session.World.Layout;
            _seed = gc.Session.World.Seed;
            _weights = WorldBuilder.Weights;
            Material template = Resources.Load<Material>("Materials/AfterfallGroundCover");
            if (_weights == null || template == null || !template.shader.isSupported || !SystemInfo.supportsInstancing)
            {
                Debug.LogWarning("[Afterfall] Ground cover disabled (missing terrain, material or instancing support).");
                enabled = false;
                return;
            }
            _blockerMask = Layers.Mask(Layers.Structure, Layers.Prop, Layers.Resource);
            _flower = PlaceFlower;
            _fern = PlaceFern;
            _pebbles = PlacePebbles;
            _litter = PlaceLitter;
            _reeds = PlaceReeds;
            CreateVariants(template);
            Shader.SetGlobalVector(PusherId, Vector4.zero);

            EventBus bus = gc.Session.Events;
            _subscriptions.Add(bus.Subscribe<BuildingPlaced>(e => StructureChanged(e.StructureId)));
            _subscriptions.Add(bus.Subscribe<BuildingCompleted>(e => StructureChanged(e.StructureId)));
            _subscriptions.Add(bus.Subscribe<BuildingRemoved>(e => StructureChanged(e.StructureId)));

            // Fill the view at once so the first frame is not bare.
            Physics.SyncTransforms();
            Vector3 start = gc.Player != null ? gc.Player.transform.position : gc.CameraRig.Camera.transform.position;
            UpdateCells(start, float.MaxValue);
        }

        private void OnDestroy()
        {
            foreach (IDisposable s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
        }

        // ------------------------------------------------------------------ variants

        private void CreateVariants(Material template)
        {
            // Own colours are sRGB (as Material.SetColor expects); tinted parts lean toward the colour of
            // the ground beneath. Grass keeps some of its own green so it never turns brown on bare earth.
            Material grass = Make(template, "grass", new Color(0.4f, 0.47f, 0.22f), 0.6f, 1.15f, 1f, 0.7f, 14f, 46f, 0.5f, 0.12f);
            Material grassTall = Make(template, "grass tall", new Color(0.42f, 0.47f, 0.23f), 0.55f, 1.12f, 1f, 0.55f, 18f, 50f, 0.5f, 0.12f);
            Material dry = Make(template, "dry grass", new Color(0.6f, 0.53f, 0.36f), 0.4f, 1.25f, 0.85f, 0.6f, 14f, 46f, 0.45f, 0.1f);
            Material flowers = Make(template, "flowers", new Color(0.75f, 0.75f, 0.75f), 0.6f, 1.15f, 0.8f, 0.6f, 10f, 40f, 0.4f, 0.15f);
            Material fern = Make(template, "fern", new Color(0.36f, 0.45f, 0.2f), 0.15f, 1f, 0.45f, 0.35f, 16f, 44f, 0.45f, 0.14f);
            Material pebbles = Make(template, "pebbles", new Color(0.5f, 0.48f, 0.45f), 0.3f, 1.6f, 0f, 0f, 5f, 22f, 0f, 0.18f);
            Material litter = Make(template, "litter", new Color(0.36f, 0.36f, 0.36f), 0.2f, 1.5f, 0f, 0f, 5f, 20f, 0.2f, 0.12f);
            Material reeds = Make(template, "reeds", new Color(0.45f, 0.47f, 0.28f), 0.25f, 1f, 0.7f, 0.12f, 22f, 50f, 0.45f, 0.12f);
            _variants = new Variant[Enum.GetValues(typeof(Kind)).Length];
            _variants[(int)Kind.GrassShort] = V(MeshFactory.GrassTuft(MeshFactory.GrassShort), grass);
            _variants[(int)Kind.GrassMedium] = V(MeshFactory.GrassTuft(MeshFactory.GrassMedium), grass);
            _variants[(int)Kind.GrassTall] = V(MeshFactory.GrassTuft(MeshFactory.GrassTall), grassTall);
            _variants[(int)Kind.GrassDry] = V(MeshFactory.GrassTuft(MeshFactory.GrassDry), dry);
            _variants[(int)Kind.Daisy] = V(MeshFactory.FlowerCluster(MeshFactory.FlowerDaisy), flowers);
            _variants[(int)Kind.Buttercup] = V(MeshFactory.FlowerCluster(MeshFactory.FlowerButtercup), flowers);
            _variants[(int)Kind.Cornflower] = V(MeshFactory.FlowerCluster(MeshFactory.FlowerCornflower), flowers);
            _variants[(int)Kind.Poppy] = V(MeshFactory.FlowerCluster(MeshFactory.FlowerPoppy), flowers);
            _variants[(int)Kind.FernA] = V(MeshFactory.Fern(0), fern);
            _variants[(int)Kind.FernB] = V(MeshFactory.Fern(1), fern);
            _variants[(int)Kind.PebblesA] = V(MeshFactory.Pebbles(0), pebbles);
            _variants[(int)Kind.PebblesB] = V(MeshFactory.Pebbles(1), pebbles);
            _variants[(int)Kind.PebblesC] = V(MeshFactory.Pebbles(2), pebbles);
            _variants[(int)Kind.LitterA] = V(MeshFactory.Litter(0), litter);
            _variants[(int)Kind.LitterB] = V(MeshFactory.Litter(1), litter);
            _variants[(int)Kind.ReedsA] = V(MeshFactory.Reeds(0), reeds);
            _variants[(int)Kind.ReedsB] = V(MeshFactory.Reeds(1), reeds);
            _maxFade = 0f;
            foreach (Variant v in _variants) _maxFade = Mathf.Max(_maxFade, v.FadeFar);
        }

        private static Variant V(Mesh mesh, Material mat) =>
            new Variant { Mesh = mesh, Material = mat, FadeNear = mat.GetFloat("_FadeNear"), FadeFar = mat.GetFloat("_FadeFar") };

        private static Material Make(Material template, string name, Color own, float tint, float gain, float wind, float bend,
            float fadeNear, float fadeFar, float translucency, float smoothness)
        {
            var m = new Material(template) { name = "ground cover " + name, enableInstancing = true };
            m.SetColor("_BaseColor", own);
            m.SetFloat("_TintStrength", tint);
            m.SetFloat("_GroundGain", gain);
            m.SetFloat("_Wind", wind);
            m.SetFloat("_Bend", bend);
            m.SetFloat("_FadeNear", fadeNear);
            m.SetFloat("_FadeFar", fadeFar);
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (_gc == null || _gc.CameraRig == null || _gc.CameraRig.Camera == null) return;
            _frameClock.Restart();
            DrawnInstances = 0;
            LastBuildMs = 0f;
            Vector3 cam = _gc.CameraRig.Camera.transform.position;
            if (_gc.Player != null)
            {
                Vector3 p = _gc.Player.transform.position;
                Shader.SetGlobalVector(PusherId, new Vector4(p.x, p.y, p.z, 0.85f));
            }
            // The strategic camera is far above everything this draws.
            if (cam.y - _layout.HeightAt(cam.x, cam.z) <= MaxCameraHeight)
            {
                UpdateCells(cam, BuildBudgetMs);
                Draw();
            }
            LastCpuMs = (float)_frameClock.Elapsed.TotalMilliseconds;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Keeps the cells within reach of <paramref name="center"/>, generating missing ones nearest first.</summary>
        private void UpdateCells(Vector3 center, float budgetMs)
        {
            float reach = _maxFade + CellSize * 0.75f;
            int cx0 = Mathf.FloorToInt((center.x - reach) / CellSize), cx1 = Mathf.FloorToInt((center.x + reach) / CellSize);
            int cz0 = Mathf.FloorToInt((center.z - reach) / CellSize), cz1 = Mathf.FloorToInt((center.z + reach) / CellSize);
            int maxCell = Mathf.CeilToInt(WorldLayout.WorldSize / CellSize) - 1;

            _remove.Clear();
            foreach (KeyValuePair<long, Cell> kv in _cells)
                if (DistanceXZ(kv.Value.Bounds, center) > reach + CellSize) _remove.Add(kv.Key);
            foreach (long k in _remove)
            {
                Cell c = _cells[k];
                _cells.Remove(k);
                foreach (Bucket b in c.Buckets) b.Count = 0;
                c.Built = false;
                _pool.Push(c);
            }

            bool rebuild = _rebuildAt >= 0f && Time.time >= _rebuildAt;
            if (rebuild) _rebuildAt = -1f;
            _queue.Clear();
            for (int z = Mathf.Max(0, cz0); z <= Mathf.Min(maxCell, cz1); z++)
            {
                for (int x = Mathf.Max(0, cx0); x <= Mathf.Min(maxCell, cx1); x++)
                {
                    long k = Key(x, z);
                    if (!_cells.TryGetValue(k, out Cell c))
                    {
                        var b = new Bounds(new Vector3((x + 0.5f) * CellSize, 0f, (z + 0.5f) * CellSize), new Vector3(CellSize, 0f, CellSize));
                        if (DistanceXZ(b, center) > reach) continue;
                        c = _pool.Count > 0 ? _pool.Pop() : NewCell();
                        c.X = x;
                        c.Z = z;
                        c.Built = false;
                        c.Dirty = false;
                        c.Bounds = CellBounds(x, z);
                        _cells[k] = c;
                    }
                    if (!c.Built || (rebuild && c.Dirty)) _queue.Add(c);
                }
            }
            if (_queue.Count == 0) return;
            _queue.Sort((a, b) => DistanceXZ(a.Bounds, center).CompareTo(DistanceXZ(b.Bounds, center)));
            _clock.Restart();
            foreach (Cell c in _queue)
            {
                BuildCell(c);
                if (_clock.Elapsed.TotalMilliseconds > budgetMs) break;
            }
            LastBuildMs = (float)_clock.Elapsed.TotalMilliseconds;
        }

        private Cell NewCell()
        {
            var c = new Cell { Buckets = new Bucket[_variants.Length] };
            for (int i = 0; i < c.Buckets.Length; i++) c.Buckets[i] = new Bucket();
            return c;
        }

        private Bounds CellBounds(int x, int z)
        {
            float x0 = x * CellSize, z0 = z * CellSize;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i <= 2; i++)
            {
                for (int j = 0; j <= 2; j++)
                {
                    float h = _layout.HeightAt(x0 + i * CellSize * 0.5f, z0 + j * CellSize * 0.5f);
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                }
            }
            // Margin for terrain between the samples, the tallest reeds and the wind.
            lo -= 1f;
            hi += 2.5f;
            return new Bounds(new Vector3(x0 + CellSize * 0.5f, (lo + hi) * 0.5f, z0 + CellSize * 0.5f), new Vector3(CellSize + 2f, hi - lo, CellSize + 2f));
        }

        private static float DistanceXZ(Bounds b, Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void Draw()
        {
            // Cull against every camera that renders the world this frame (the gameplay camera and,
            // for instance, the model gallery's camera).
            if (Camera.allCamerasCount > _cameras.Length) _cameras = new Camera[Camera.allCamerasCount * 2];
            int n = Camera.GetAllCameras(_cameras);
            int used = 0;
            for (int i = 0; i < n && used < _cameraPlanes.Length; i++)
            {
                Camera c = _cameras[i];
                if (c == null || !c.isActiveAndEnabled || c.targetTexture != null || (c.cullingMask & 1) == 0) continue;
                GeometryUtility.CalculateFrustumPlanes(c, _cameraPlanes[used]);
                _cameraPositions[used] = c.transform.position;
                used++;
            }
            if (used == 0) return;

            foreach (Variant v in _variants) v.Count = 0;
            foreach (Cell cell in _cells.Values)
            {
                if (!cell.Built) continue;
                float nearest = float.MaxValue;
                bool seen = false;
                for (int i = 0; i < used; i++)
                {
                    float d = Mathf.Sqrt(cell.Bounds.SqrDistance(_cameraPositions[i]));
                    if (d > _maxFade) continue;
                    if (!GeometryUtility.TestPlanesAABB(_cameraPlanes[i], cell.Bounds)) continue;
                    seen = true;
                    nearest = Mathf.Min(nearest, d);
                }
                if (!seen) continue;
                for (int k = 0; k < _variants.Length; k++)
                {
                    Bucket bucket = cell.Buckets[k];
                    if (bucket.Count == 0) continue;
                    Variant v = _variants[k];
                    if (nearest > v.FadeFar) continue;
                    // Only the instances whose own cut-off distance (as the shader computes it) reaches
                    // the nearest point of the cell, with a little margin.
                    float t = Mathf.Clamp01((nearest - 2f - v.FadeNear) / (v.FadeFar - v.FadeNear));
                    int count = bucket.CountAbove(t * t);
                    if (count == 0) continue;
                    if (v.Count + count > v.Draw.Length) Array.Resize(ref v.Draw, Mathf.NextPowerOfTwo(v.Count + count));
                    Array.Copy(bucket.Items, 0, v.Draw, v.Count, count);
                    v.Count += count;
                }
            }

            Vector3 eye = _gc.CameraRig.Camera.transform.position;
            var bounds = new Bounds(eye, new Vector3(_maxFade * 2f + CellSize * 2f, 400f, _maxFade * 2f + CellSize * 2f));
            foreach (Variant v in _variants)
            {
                if (v.Count == 0) continue;
                DrawnInstances += v.Count;
                var rp = new RenderParams(v.Material)
                {
                    layer = 0,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    motionVectorMode = MotionVectorGenerationMode.Camera,
                    worldBounds = bounds
                };
                for (int start = 0; start < v.Count; start += MaxPerDraw)
                    Graphics.RenderMeshInstanced(rp, v.Mesh, 0, v.Draw, Mathf.Min(MaxPerDraw, v.Count - start), start);
            }
        }

        // ------------------------------------------------------------------ structures

        private void StructureChanged(EntityId id)
        {
            Vector3 pos;
            if (_gc.Session.World.Structures.TryGetValue(id, out Structure s))
            {
                pos = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
                _structurePositions[id] = pos;
            }
            else if (!_structurePositions.TryGetValue(id, out pos))
            {
                pos = _gc.Player != null ? _gc.Player.transform.position : Vector3.zero;
            }
            foreach (Cell c in _cells.Values)
                if (DistanceXZ(c.Bounds, pos) < 14f) c.Dirty = true;
            // Give the structure's view a moment to appear (or disappear) before looking again.
            _rebuildAt = Time.time + 0.35f;
        }

        // ------------------------------------------------------------------ generation

        private void BuildCell(Cell c)
        {
            foreach (Bucket b in c.Buckets) b.Count = 0;
            c.Built = true;
            c.Dirty = false;
            GatherBlockers(c);
            Grass(c);
            Scatter(c, 8, 61, _flower);
            Scatter(c, 7, 71, _fern);
            Scatter(c, 11, 81, _pebbles);
            Scatter(c, 10, 91, _litter);
            Scatter(c, 13, 101, _reeds);
            foreach (Bucket b in c.Buckets) b.Sort();
        }

        private delegate void Placer(Cell c, float x, float z, float[] w, float h0, float h1, float h2);

        /// <summary>Visits a jittered grid of <paramref name="perSide"/>² candidate points in the cell, with stable hashes per point.</summary>
        private void Scatter(Cell c, int perSide, int seed, Placer place)
        {
            float step = CellSize / perSide;
            for (int j = 0; j < perSide; j++)
            {
                for (int i = 0; i < perSide; i++)
                {
                    int gx = c.X * perSide + i, gz = c.Z * perSide + j;
                    float h0 = TextureSynth.Hash01(gx, gz, _seed + seed);
                    float x = c.X * CellSize + (i + TextureSynth.Hash01(gx, gz, _seed + seed + 1)) * step;
                    float z = c.Z * CellSize + (j + TextureSynth.Hash01(gx, gz, _seed + seed + 2)) * step;
                    _weights.Sample(x, z, _w);
                    place(c, x, z, _w, h0, TextureSynth.Hash01(gx, gz, _seed + seed + 3), TextureSynth.Hash01(gx, gz, _seed + seed + 4));
                }
            }
        }

        private void Grass(Cell c)
        {
            const int perSide = 32;
            float step = CellSize / perSide;
            for (int j = 0; j < perSide; j++)
            {
                for (int i = 0; i < perSide; i++)
                {
                    int gx = c.X * perSide + i, gz = c.Z * perSide + j;
                    float h0 = TextureSynth.Hash01(gx, gz, _seed + 31);
                    float x = c.X * CellSize + (i + TextureSynth.Hash01(gx, gz, _seed + 32)) * step;
                    float z = c.Z * CellSize + (j + TextureSynth.Hash01(gx, gz, _seed + 33)) * step;
                    _weights.Sample(x, z, _w);
                    float meadow = _w[TerrainSurfaces.Meadow], dry = _w[TerrainSurfaces.DryGrass];
                    float score = meadow + dry * 0.9f + _w[TerrainSurfaces.ForestFloor] * 0.12f + _w[TerrainSurfaces.Dirt] * 0.07f
                        + _w[TerrainSurfaces.Gravel] * 0.05f + _w[TerrainSurfaces.Shore] * 0.1f;
                    score *= 1f - Mathf.Clamp01(_w[TerrainSurfaces.Asphalt] * 3f) - Mathf.Clamp01(_w[TerrainSurfaces.Rock] * 1.5f);
                    if (score <= 0.02f) continue;
                    // Clumps and thin spots rather than an even lawn.
                    float clump = 0.68f + 0.5f * Noise.Value(_seed + 41, x * 0.32f, z * 0.32f);
                    if (h0 > score * clump) continue;
                    if (!Ground(x, z, 0.1f, out Vector3 pos, out Vector3 normal)) continue;
                    if (Blocked(pos)) continue;

                    float h1 = TextureSynth.Hash01(gx, gz, _seed + 34), h2 = TextureSynth.Hash01(gx, gz, _seed + 35);
                    Kind kind;
                    if (h1 < dry / Mathf.Max(0.05f, meadow + dry) * 0.9f) kind = Kind.GrassDry;
                    else
                    {
                        float tall = Noise.Value(_seed + 43, x * 0.11f, z * 0.11f) + _w[TerrainSurfaces.DryGrass] * 0.3f;
                        kind = tall > 0.3f && h2 < 0.55f ? Kind.GrassTall : h2 < 0.5f ? Kind.GrassMedium : Kind.GrassShort;
                    }
                    Add(c, kind, pos, normal, TextureSynth.Hash01(gx, gz, _seed + 36) * 360f, Mathf.Lerp(0.75f, 1.25f, TextureSynth.Hash01(gx, gz, _seed + 37)), 0.5f);
                }
            }
        }

        private void PlaceFlower(Cell c, float x, float z, float[] w, float h0, float h1, float h2)
        {
            float patch = Smooth(0.1f, 0.55f, Noise.Value(_seed + 51, x * 0.07f, z * 0.07f));
            float density = w[TerrainSurfaces.Meadow] * (0.05f + patch * 0.8f) + w[TerrainSurfaces.DryGrass] * patch * 0.25f;
            if (h0 > density) return;
            if (!Ground(x, z, 0.2f, out Vector3 pos, out Vector3 normal) || Blocked(pos)) return;
            // Species grow in patches of their own, with a few strays.
            float species = (Noise.Value(_seed + 52, x * 0.025f, z * 0.025f) * 0.5f + 0.5f) * 4f + (h1 - 0.5f) * 1.4f;
            int s = Mathf.Clamp((int)species, 0, 3);
            if (s == 3 && h2 < 0.6f) s = 0; // poppies stay rare
            Add(c, Kind.Daisy + s, pos, normal, h2 * 360f, Mathf.Lerp(0.8f, 1.2f, h1), 0.3f);
        }

        private void PlaceFern(Cell c, float x, float z, float[] w, float h0, float h1, float h2)
        {
            float patch = Smooth(-0.25f, 0.35f, Noise.Value(_seed + 61, x * 0.09f, z * 0.09f));
            if (h0 > w[TerrainSurfaces.ForestFloor] * patch * 0.8f) return;
            if (!Ground(x, z, 0.3f, out Vector3 pos, out Vector3 normal) || Blocked(pos)) return;
            Add(c, h1 < 0.5f ? Kind.FernA : Kind.FernB, pos, normal, h2 * 360f, Mathf.Lerp(0.75f, 1.3f, h1), 0.6f);
        }

        private void PlacePebbles(Cell c, float x, float z, float[] w, float h0, float h1, float h2)
        {
            float density = w[TerrainSurfaces.Dirt] * 0.45f + w[TerrainSurfaces.Gravel] * 0.7f + w[TerrainSurfaces.Shore] * 0.5f
                + w[TerrainSurfaces.Rock] * 0.4f + w[TerrainSurfaces.ForestFloor] * 0.08f + w[TerrainSurfaces.Meadow] * 0.03f;
            if (h0 > density) return;
            if (!Ground(x, z, -1f, out Vector3 pos, out Vector3 normal) || Blocked(pos)) return;
            Add(c, Kind.PebblesA + (int)(h1 * 2.999f), pos, normal, h2 * 360f, Mathf.Lerp(0.8f, 1.4f, h1), 0.9f);
        }

        private void PlaceLitter(Cell c, float x, float z, float[] w, float h0, float h1, float h2)
        {
            if (h0 > w[TerrainSurfaces.ForestFloor] * 0.75f + w[TerrainSurfaces.DryGrass] * 0.04f) return;
            if (!Ground(x, z, 0f, out Vector3 pos, out Vector3 normal) || Blocked(pos)) return;
            Add(c, h1 < 0.5f ? Kind.LitterA : Kind.LitterB, pos, normal, h2 * 360f, Mathf.Lerp(0.8f, 1.3f, h1), 1f);
        }

        private void PlaceReeds(Cell c, float x, float z, float[] w, float h0, float h1, float h2)
        {
            float water = _layout.WaterLevel;
            float h = _layout.HeightAt(x, z);
            if (h < water - 0.35f || h > water + 0.5f) return;
            float patch = Smooth(-0.3f, 0.3f, Noise.Value(_seed + 71, x * 0.08f, z * 0.08f));
            if (h0 > (0.2f + w[TerrainSurfaces.Shore] * 0.6f) * patch) return;
            var pos = new Vector3(x, h, z);
            if (Blocked(pos)) return;
            Add(c, h1 < 0.5f ? Kind.ReedsA : Kind.ReedsB, pos, Vector3.up, h2 * 360f, Mathf.Lerp(0.75f, 1.2f, h1), 0f);
        }

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Ground position and normal at (x, z). Fails in the lake (unless <paramref name="minAboveWater"/>
        /// is negative) and on slopes too steep for plants.
        /// </summary>
        private bool Ground(float x, float z, float minAboveWater, out Vector3 pos, out Vector3 normal)
        {
            float h = _layout.HeightAt(x, z);
            pos = new Vector3(x, h, z);
            normal = Vector3.up;
            if (minAboveWater >= 0f && h < _layout.WaterLevel + minAboveWater) return false;
            const float d = 0.5f;
            float hx = _layout.HeightAt(x + d, z) - _layout.HeightAt(x - d, z);
            float hz = _layout.HeightAt(x, z + d) - _layout.HeightAt(x, z - d);
            normal = new Vector3(-hx, 2f * d, -hz).normalized;
            return normal.y > 0.74f;
        }

        private void Add(Cell c, Kind kind, Vector3 pos, Vector3 normal, float yaw, float scale, float follow)
        {
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, follow));
            c.Buckets[(int)kind].Add(Matrix4x4.TRS(pos, tilt * Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale), Rank(pos.x, pos.z));
        }

        /// <summary>The shader's per-instance rank (AfterfallHash12 of the pivot), which sets how far away it is still drawn.</summary>
        private static float Rank(float x, float z)
        {
            float a = Frac(x * 0.1031f), b = Frac(z * 0.1031f), c = a;
            float d = a * (b + 33.33f) + b * (c + 33.33f) + c * (a + 33.33f);
            a += d;
            b += d;
            c += d;
            return Frac((a + b) * c);
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        // ------------------------------------------------------------------ obstacles

        private void GatherBlockers(Cell c)
        {
            _blockers.Clear();
            Vector3 half = new Vector3(CellSize * 0.5f + 0.5f, c.Bounds.extents.y + 1f, CellSize * 0.5f + 0.5f);
            int n = Physics.OverlapBoxNonAlloc(c.Bounds.center, half, _overlap, Quaternion.identity, _blockerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider col = _overlap[i];
                if (col == null || !col.enabled) continue;
                if (col is BoxCollider box)
                {
                    Vector3 s = box.transform.lossyScale;
                    // A quarter metre of clearance around walls, floors and crates.
                    var margin = new Vector3(0.25f / Mathf.Max(0.01f, Mathf.Abs(s.x)), 0.3f / Mathf.Max(0.01f, Mathf.Abs(s.y)), 0.25f / Mathf.Max(0.01f, Mathf.Abs(s.z)));
                    _blockers.Add(new Blocker { IsBox = true, WorldToLocal = box.transform.worldToLocalMatrix, Center = box.center, Half = box.size * 0.5f + margin });
                }
                else
                {
                    Bounds b = col.bounds;
                    b.Expand(new Vector3(0.3f, 0.4f, 0.3f));
                    _blockers.Add(new Blocker { Bounds = b });
                }
            }
        }

        private bool Blocked(Vector3 ground)
        {
            Vector3 p = ground + new Vector3(0f, 0.15f, 0f);
            for (int i = 0; i < _blockers.Count; i++)
            {
                Blocker b = _blockers[i];
                if (b.IsBox)
                {
                    Vector3 l = b.WorldToLocal.MultiplyPoint3x4(p) - b.Center;
                    if (Mathf.Abs(l.x) < b.Half.x && Mathf.Abs(l.y) < b.Half.y && Mathf.Abs(l.z) < b.Half.z) return true;
                }
                else if (b.Bounds.Contains(p)) return true;
            }
            return false;
        }
    }
}
