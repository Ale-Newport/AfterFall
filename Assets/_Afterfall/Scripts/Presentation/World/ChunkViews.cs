using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Views of a chunked world (version 3) around the camera: each chunk's props (bridges, rail,
    /// enterable buildings, village houses) within <see cref="PropRadius"/>; Kesselmouth's merged
    /// buildings with colliders within <see cref="CityDetailRadius"/>; its skyline of boxes out to
    /// <see cref="SkylineRadius"/>; and a coarse far-terrain mesh (12 km, coloured by biome) under the
    /// detailed terrain tiles so the land and the city reach the horizon. Geometry is prepared on worker
    /// threads; at most one chunk is turned into GameObjects per frame.
    /// </summary>
    public sealed class ChunkViews : MonoBehaviour
    {
        public const float PropRadius = 900f, PropDrop = 1150f;
        public const float CityDetailRadius = 760f, CityDetailDrop = 980f;
        public const float SkylineRadius = 2600f, SkylineDrop = 3000f;
        private const float FarSize = 12000f, FarStep = 96f, FarRecenter = 1500f;

        private sealed class View
        {
            public GameObject Props, Detail, Skyline;
            public Task<CityMesher.Prepared> Pending;
            public bool PendingDetail;
        }

        private GameController _gc;
        private WorldLayout _layout;
        private BasinWorld _basin;
        private Transform _root;
        private readonly Dictionary<long, View> _views = new Dictionary<long, View>();
        private readonly Queue<(long Key, PropPlacement Prop)> _propQueue = new Queue<(long, PropPlacement)>();
        private float _scan;
        private GameObject _far;
        private Vector3 _farCenter = new Vector3(float.NaN, 0, 0);
        private Task<(Vector3[] V, Color[] C, int[] T)> _farTask;
        private Vector3 _farTaskCenter;

        public int DetailedChunks { get; private set; }
        public int SkylineChunks { get; private set; }
        public int PropChunks { get; private set; }

        public void Initialize(GameController gc, WorldLayout layout, Transform root)
        {
            _gc = gc;
            _layout = layout;
            _basin = layout.Chunks as BasinWorld;
            _root = root;
            CityMesher.Prepare();
        }

        private Vector3 Focus()
        {
            if (_gc.CameraRig != null && _gc.CameraRig.Camera != null) return _gc.CameraRig.Camera.transform.position.ToWorld();
            return _gc.Player != null ? _gc.Player.transform.position.ToWorld() : Vector3.zero;
        }

        private View Get(long key)
        {
            if (!_views.TryGetValue(key, out View v)) _views[key] = v = new View();
            return v;
        }

        private void Update()
        {
            if (_gc == null || _layout == null) return;
            Vector3 f = Focus();
            UpdateFar(f);

            // Finish at most one prepared city chunk, then a few props, per frame.
            foreach (var kv in _views)
            {
                View v = kv.Value;
                if (v.Pending == null || !v.Pending.IsCompleted) continue;
                CityMesher.Prepared prep = v.Pending.Result;
                v.Pending = null;
                if (v.PendingDetail)
                {
                    if (v.Detail == null)
                    {
                        v.Detail = CityMesher.Finish(prep, _root);
                        MarkNavDirty(kv.Key);
                    }
                    if (v.Skyline != null) Destroy(v.Skyline);
                    v.Skyline = null;
                }
                else if (v.Detail == null && v.Skyline == null) v.Skyline = CityMesher.Finish(prep, _root);
                break;
            }
            // A chunk's buildings are obstacles (and floors) for the NavMesh once they all stand.
            if (_propQueue.Count == 0 && _navProps.Count > 0)
            {
                foreach (long k in _navProps) MarkNavDirty(k);
                _navProps.Clear();
            }
            for (int i = 0; i < 6 && _propQueue.Count > 0; i++)
            {
                (long key, PropPlacement p) = _propQueue.Dequeue();
                if (!_views.TryGetValue(key, out View v) || v.Props == null) continue;
                GameObject go = PropBuilder.Build(p);
                if (go == null) continue;
                go.transform.SetParent(v.Props.transform, false); // world coordinates as local ones under the anchor
                Shapes.SetLayerRecursive(go, Layers.Prop);
            }

            _scan -= Time.unscaledDeltaTime;
            if (_scan > 0f) return;
            _scan = 0.4f;
            Scan(f);
        }

        private void Scan(Vector3 f)
        {
            float size = WorldLayout.ChunkSize;
            // Distant city chunks are made (on workers) just for the skyline.
            if (_basin != null)
                ForChunks(f, SkylineRadius, (x, z, d) =>
                {
                    if (_layout.HasChunk(x, z) || _layout.IsChunkPending(x, z) || _layout.PendingChunks >= 2) return; // near chunks first
                    if (_basin.CityDensity((x + 0.5f) * size, (z + 0.5f) * size) > 0.08f) _layout.RequestChunk(x, z);
                });
            _layout.IntegrateReady(6);

            int detailed = 0, sky = 0, props = 0;
            ForChunks(f, SkylineDrop, (x, z, d) =>
            {
                ChunkContent c = _layout.GetChunk(x, z);
                if (c == null) return;
                View v = Get(c.Key);
                if (d < PropRadius && v.Props == null && c.Props.Count > 0)
                {
                    v.Props = new GameObject($"Props {x},{z}");
                    v.Props.transform.SetParent(_root, false);
                    foreach (PropPlacement p in c.Props) _propQueue.Enqueue((c.Key, p));
                    _navProps.Add(c.Key);
                }
                if (c.Blocks.Count > 0 && v.Pending == null)
                {
                    if (d < CityDetailRadius && v.Detail == null)
                    {
                        v.PendingDetail = true;
                        v.Pending = Task.Run(() => CityMesher.PrepareDetailed(c));
                    }
                    else if (d >= CityDetailRadius && d < SkylineRadius && v.Detail == null && v.Skyline == null)
                    {
                        v.PendingDetail = false;
                        v.Pending = Task.Run(() => CityMesher.PrepareSkyline(c));
                    }
                }
            });

            var drop = new List<long>();
            foreach (var kv in _views)
            {
                ChunkContent.FromKey(kv.Key, out int x, out int z);
                float d = Distance(f, x, z);
                View v = kv.Value;
                if (v.Props != null && d > PropDrop)
                {
                    Destroy(v.Props);
                    v.Props = null;
                }
                if (v.Detail != null && d > CityDetailDrop)
                {
                    Destroy(v.Detail);
                    v.Detail = null;
                }
                if (v.Skyline != null && (d > SkylineDrop || v.Detail != null))
                {
                    Destroy(v.Skyline);
                    v.Skyline = null;
                }
                // Leaving detail range: the skyline takes over (next scan prepares it).
                if (v.Props == null && v.Detail == null && v.Skyline == null && v.Pending == null && d > SkylineDrop) drop.Add(kv.Key);
                if (v.Detail != null) detailed++;
                if (v.Skyline != null) sky++;
                if (v.Props != null) props++;
            }
            foreach (long k in drop) _views.Remove(k);
            DetailedChunks = detailed;
            SkylineChunks = sky;
            PropChunks = props;
        }

        private readonly HashSet<long> _navProps = new HashSet<long>();

        private void MarkNavDirty(long key)
        {
            ChunkContent.FromKey(key, out int x, out int z);
            float size = WorldLayout.ChunkSize;
            _gc.MarkNavDirty(new Rect(x * size, z * size, size, size));
        }

        private static float Distance(Vector3 p, int x, int z)
        {
            float size = WorldLayout.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(x * size - p.x, p.x - (x + 1) * size));
            float dz = Mathf.Max(0f, Mathf.Max(z * size - p.z, p.z - (z + 1) * size));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void ForChunks(Vector3 p, float radius, System.Action<int, int, float> each)
        {
            int n = _layout.ChunksAcross;
            int x0 = Mathf.Max(0, WorldLayout.ChunkOf(p.x - radius)), x1 = Mathf.Min(n - 1, WorldLayout.ChunkOf(p.x + radius));
            int z0 = Mathf.Max(0, WorldLayout.ChunkOf(p.z - radius)), z1 = Mathf.Min(n - 1, WorldLayout.ChunkOf(p.z + radius));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Distance(p, x, z);
                    if (d <= radius) each(x, z, d);
                }
        }

        // ------------------------------------------------------------------ far terrain

        private void UpdateFar(Vector3 f)
        {
            if (_farTask != null && _farTask.IsCompleted)
            {
                (Vector3[] v, Color[] c, int[] t) = _farTask.Result;
                _farTask = null;
                if (_far != null) Destroy(_far.GetComponent<MeshFilter>().sharedMesh);
                else
                {
                    _far = new GameObject("Far terrain");
                    _far.transform.SetParent(_root, false);
                    _far.AddComponent<MeshFilter>();
                    var r = _far.AddComponent<MeshRenderer>();
                    r.sharedMaterial = MaterialLibrary.Surface(Art.Surface.Plain, Color.white, 0.05f);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var mesh = new Mesh { name = "Far terrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = v;
                mesh.colors = c;
                mesh.triangles = t;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                _far.GetComponent<MeshFilter>().sharedMesh = mesh;
                _farCenter = _farTaskCenter;
            }
            if (_farTask != null) return;
            if (!float.IsNaN(_farCenter.x) && Mathf.Abs(f.x - _farCenter.x) < FarRecenter && Mathf.Abs(f.z - _farCenter.z) < FarRecenter) return;
            Vector3 centre = new Vector3(Mathf.Round(f.x / FarStep) * FarStep, 0f, Mathf.Round(f.z / FarStep) * FarStep);
            _farTaskCenter = centre;
            WorldLayout layout = _layout;
            BasinWorld basin = _basin;
            _farTask = Task.Run(() => BuildFar(layout, basin, centre));
        }

        /// <summary>The far-terrain grid: map heights lowered under the detailed tiles, vertex colours by biome.</summary>
        private static (Vector3[], Color[], int[]) BuildFar(WorldLayout layout, BasinWorld basin, Vector3 centre)
        {
            int n = Mathf.RoundToInt(FarSize / FarStep);
            int verts = n + 1;
            var v = new Vector3[verts * verts];
            var c = new Color[verts * verts];
            float x0 = centre.x - FarSize * 0.5f, z0 = centre.z - FarSize * 0.5f;
            for (int j = 0; j < verts; j++)
                for (int i = 0; i < verts; i++)
                {
                    float x = Mathf.Clamp(x0 + i * FarStep, 0f, layout.Size), z = Mathf.Clamp(z0 + j * FarStep, 0f, layout.Size);
                    float h = layout.MapHeightAt(x, z);
                    // Below the detailed tiles everywhere, even in the hollows of rough mountain ground.
                    float drop = 3f + (basin != null ? basin.Roughness(x, z) * 4.5f : 0f);
                    v[j * verts + i] = new Vector3(x, h - drop, z);
                    Color col = new Color(0.33f, 0.38f, 0.22f);
                    if (basin != null)
                    {
                        BiomeSample b = basin.Biome(x, z);
                        col = Color.Lerp(col, new Color(0.18f, 0.26f, 0.14f), Mathf.Clamp01(b.Forest));
                        col = Color.Lerp(col, new Color(0.5f, 0.46f, 0.32f), b.Farm * 0.6f + b.Moor * 0.7f);
                        col = Color.Lerp(col, new Color(0.62f, 0.42f, 0.3f), b.Badlands);
                        col = Color.Lerp(col, new Color(0.45f, 0.44f, 0.43f), b.City * 0.85f + b.Mountain * Mathf.Clamp01((h - 300f) / 200f) * 0.7f);
                        col = Color.Lerp(col, new Color(0.88f, 0.9f, 0.93f), b.Snow);
                        col = Color.Lerp(col, new Color(0.74f, 0.68f, 0.52f), b.Beach);
                    }
                    c[j * verts + i] = col;
                }
            var t = new int[n * n * 6];
            int k = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * verts + i;
                    t[k++] = a;
                    t[k++] = a + verts;
                    t[k++] = a + 1;
                    t[k++] = a + 1;
                    t[k++] = a + verts;
                    t[k++] = a + verts + 1;
                }
            return (v, c, t);
        }
    }
}
