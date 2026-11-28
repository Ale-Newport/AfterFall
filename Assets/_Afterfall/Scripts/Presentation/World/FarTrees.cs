using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Distant trees in a chunked world. Real tree views exist only within
    /// <see cref="WorldSync.NodeViewRadius"/> of the survivor; beyond it each generated chunk's trees
    /// are drawn as simple low-poly stand-ins (the same positions, kinds and sizes), merged into one
    /// mesh per 128 m cell, out to <see cref="Radius"/>. Cells near the survivor are hidden so the
    /// stand-ins never show through the real trees. A felled tree is left out when its chunk is rebuilt.
    /// </summary>
    public sealed class FarTrees : MonoBehaviour
    {
        public const float Radius = 1250f, Drop = 1450f, NearHide = 230f;
        private const int Cells = 4; // per chunk side: 128 m cells

        private sealed class View
        {
            public GameObject Root;
            public Renderer[] Parts = new Renderer[Cells * Cells];
            public Task<MeshData[]> Pending;
        }

        private sealed class MeshData
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
        }

        private GameController _gc;
        private WorldLayout _layout;
        private Transform _root;
        private Material _material;
        private readonly Dictionary<long, View> _views = new Dictionary<long, View>();
        private float _scan;
        private int _depleted = -1;

        public int ChunkCount => _views.Count;
        public int VisibleCells { get; private set; }

        public void Initialize(GameController gc, WorldLayout layout, Transform root)
        {
            _gc = gc;
            _layout = layout;
            _root = root;
            _material = MaterialLibrary.Surface(Art.Surface.Plain, Color.white, 0.04f);
        }

        private Vector3 Focus()
        {
            if (_gc.CameraRig != null && _gc.CameraRig.Camera != null) return _gc.CameraRig.Camera.transform.position.ToWorld();
            return _gc.Player != null ? _gc.Player.transform.position.ToWorld() : Vector3.zero;
        }

        private void Update()
        {
            if (_gc == null || _layout == null || !_gc.Running) return;
            // Finish one chunk per frame.
            foreach (KeyValuePair<long, View> kv in _views)
            {
                View v = kv.Value;
                if (v.Pending == null || !v.Pending.IsCompleted) continue;
                Finish(kv.Key, v, v.Pending.Result);
                v.Pending = null;
                break;
            }
            _scan -= Time.unscaledDeltaTime;
            if (_scan > 0f) return;
            _scan = 0.5f;
            Scan(Focus());
        }

        private void Scan(Vector3 f)
        {
            // A tree felled since the last look: rebuild the chunks near the survivor (they hold it).
            int depleted = 0;
            foreach (ResourceNodeState n in _gc.Session.World.ResourceNodes.Values)
                if (n.Depleted) depleted++;
            if (depleted != _depleted)
            {
                if (_depleted >= 0)
                    foreach (KeyValuePair<long, View> kv in _views)
                        if (ChunkDistance(f, kv.Key) < WorldSync.NodeViewRadius + 200f) Clear(kv.Value);
                _depleted = depleted;
            }

            float size = WorldLayout.ChunkSize;
            int n0 = _layout.ChunksAcross;
            int x0 = Mathf.Max(0, WorldLayout.ChunkOf(f.x - Radius)), x1 = Mathf.Min(n0 - 1, WorldLayout.ChunkOf(f.x + Radius));
            int z0 = Mathf.Max(0, WorldLayout.ChunkOf(f.z - Radius)), z1 = Mathf.Min(n0 - 1, WorldLayout.ChunkOf(f.z + Radius));
            int started = 0;
            for (int z = z0; z <= z1 && started < 2; z++)
                for (int x = x0; x <= x1 && started < 2; x++)
                {
                    long key = ChunkContent.ChunkKey(x, z);
                    if (ChunkDistance(f, key) > Radius) continue;
                    if (_views.TryGetValue(key, out View v) && (v.Root != null || v.Pending != null)) continue;
                    ChunkContent c = _layout.GetChunk(x, z);
                    if (c == null) continue;
                    if (v == null) _views[key] = v = new View();
                    List<(Vector3 P, float Scale, float Yaw, bool Birch)> trees = Collect(c);
                    Vector3 origin = new Vector3(c.MinX, 0f, c.MinZ);
                    v.Pending = Task.Run(() => Build(trees, origin));
                    started++;
                }

            var drop = new List<long>();
            int visible = 0;
            foreach (KeyValuePair<long, View> kv in _views)
            {
                if (ChunkDistance(f, kv.Key) > Drop)
                {
                    drop.Add(kv.Key);
                    continue;
                }
                View v = kv.Value;
                if (v.Root == null) continue;
                ChunkContent.FromKey(kv.Key, out int cx, out int cz);
                float cell = size / Cells;
                for (int j = 0; j < Cells; j++)
                    for (int i = 0; i < Cells; i++)
                    {
                        Renderer r = v.Parts[j * Cells + i];
                        if (r == null) continue;
                        float ax = cx * size + i * cell, az = cz * size + j * cell;
                        float dx = Mathf.Max(0f, Mathf.Max(ax - f.x, f.x - (ax + cell))), dz = Mathf.Max(0f, Mathf.Max(az - f.z, f.z - (az + cell)));
                        bool show = dx * dx + dz * dz > NearHide * NearHide;
                        if (r.enabled != show) r.enabled = show;
                        if (show) visible++;
                    }
            }
            VisibleCells = visible;
            foreach (long k in drop)
            {
                Clear(_views[k]);
                _views.Remove(k);
            }
        }

        private static float ChunkDistance(Vector3 p, long key)
        {
            ChunkContent.FromKey(key, out int x, out int z);
            float size = WorldLayout.ChunkSize;
            float dx = Mathf.Max(0f, Mathf.Max(x * size - p.x, p.x - (x + 1) * size));
            float dz = Mathf.Max(0f, Mathf.Max(z * size - p.z, p.z - (z + 1) * size));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void Clear(View v)
        {
            if (v.Root != null)
            {
                foreach (MeshFilter mf in v.Root.GetComponentsInChildren<MeshFilter>()) Destroy(mf.sharedMesh);
                Destroy(v.Root);
            }
            v.Root = null;
            v.Parts = new Renderer[Cells * Cells];
            v.Pending = null;
        }

        /// <summary>The chunk's standing trees (main thread: felled ones are looked up in the world).</summary>
        private List<(Vector3, float, float, bool)> Collect(ChunkContent c)
        {
            WorldState w = _gc.Session.World;
            var list = new List<(Vector3, float, float, bool)>();
            foreach (NodeSeed n in c.Nodes)
            {
                bool pine = n.DefId == "tree_pine", birch = n.DefId == "tree_birch";
                if (!pine && !birch) continue;
                if (!string.IsNullOrEmpty(n.Key) && w.TryGetNodeByKey(n.Key, out ResourceNodeState live) && live.Depleted) continue;
                list.Add((new Vector3(n.Position.X, n.Position.Y, n.Position.Z), n.Scale, n.Yaw, birch));
            }
            return list;
        }

        /// <summary>Low-poly trees in the chunk's 4 × 4 cells, positions relative to the chunk's corner (worker thread).</summary>
        private static MeshData[] Build(List<(Vector3 P, float Scale, float Yaw, bool Birch)> trees, Vector3 origin)
        {
            var cells = new MeshData[Cells * Cells];
            float cell = WorldLayout.ChunkSize / Cells;
            foreach ((Vector3 p, float scale, float yaw, bool birch) in trees)
            {
                Vector3 local = p - origin;
                int i = Mathf.Clamp(Mathf.FloorToInt(local.x / cell), 0, Cells - 1), j = Mathf.Clamp(Mathf.FloorToInt(local.z / cell), 0, Cells - 1);
                MeshData m = cells[j * Cells + i] ?? (cells[j * Cells + i] = new MeshData());
                int h = Mathf.Abs(Mathf.RoundToInt(p.x * 13.7f) * 31 + Mathf.RoundToInt(p.z * 7.1f));
                float tone = 0.86f + (h % 17) / 17f * 0.28f;
                if (birch) Broadleaf(m, local, scale, yaw, tone);
                else Conifer(m, local, scale, yaw, tone);
            }
            return cells;
        }

        private static readonly Color Bark = new Color(0.2f, 0.15f, 0.11f), BirchBark = new Color(0.62f, 0.6f, 0.56f);
        private static readonly Color Needles = new Color(0.07f, 0.13f, 0.07f), Leaves = new Color(0.16f, 0.24f, 0.08f);

        private static void Conifer(MeshData m, Vector3 p, float s, float yaw, float tone)
        {
            float h = 11.5f * s;
            Prism(m, p, 0.22f * s, 0.18f * s, 0f, h * 0.3f, 4, yaw, Bark);
            Color c = Needles * tone;
            c.a = 1f;
            Cone(m, p, 2.6f * s, h * 0.18f, h * 0.62f, 6, yaw, c);
            Cone(m, p, 1.9f * s, h * 0.45f, h, 6, yaw + 30f, c * 1.08f);
        }

        private static void Broadleaf(MeshData m, Vector3 p, float s, float yaw, float tone)
        {
            float h = 9.5f * s;
            Prism(m, p, 0.16f * s, 0.12f * s, 0f, h * 0.55f, 4, yaw, BirchBark);
            Color c = Leaves * tone;
            c.a = 1f;
            // A crown of two stacked eight-sided bulbs.
            Bulb(m, p, 2.2f * s, h * 0.38f, h * 0.82f, yaw, c);
            Bulb(m, p, 1.5f * s, h * 0.68f, h, yaw + 22f, c * 1.1f);
        }

        private static void Prism(MeshData m, Vector3 p, float r0, float r1, float y0, float y1, int sides, float yaw, Color c)
        {
            for (int k = 0; k < sides; k++)
            {
                float a0 = (yaw + k * 360f / sides) * Mathf.Deg2Rad, a1 = (yaw + (k + 1) * 360f / sides) * Mathf.Deg2Rad;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 a = p + d0 * r0 + Vector3.up * y0, b = p + d1 * r0 + Vector3.up * y0, cc = p + d1 * r1 + Vector3.up * y1, d = p + d0 * r1 + Vector3.up * y1;
                Vector3 outward = d0 + d1;
                Tri(m, a, b, cc, c, outward);
                Tri(m, a, cc, d, c, outward);
            }
        }

        private static void Cone(MeshData m, Vector3 p, float r, float y0, float y1, int sides, float yaw, Color c)
        {
            Vector3 top = p + Vector3.up * y1, centre = p + Vector3.up * y0;
            for (int k = 0; k < sides; k++)
            {
                float a0 = (yaw + k * 360f / sides) * Mathf.Deg2Rad, a1 = (yaw + (k + 1) * 360f / sides) * Mathf.Deg2Rad;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = centre + d0 * r, b1 = centre + d1 * r;
                Tri(m, b0, top, b1, c, d0 + d1 + Vector3.up * 0.5f);
                Tri(m, b0, b1, centre, c * 0.7f, Vector3.down);
            }
        }

        private static void Bulb(MeshData m, Vector3 p, float r, float y0, float y1, float yaw, Color c)
        {
            const int sides = 8;
            float mid = y0 + (y1 - y0) * 0.45f;
            Vector3 top = p + Vector3.up * y1, bottom = p + Vector3.up * y0;
            for (int k = 0; k < sides; k++)
            {
                float a0 = (yaw + k * 360f / sides) * Mathf.Deg2Rad, a1 = (yaw + (k + 1) * 360f / sides) * Mathf.Deg2Rad;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 e0 = p + d0 * r + Vector3.up * mid, e1 = p + d1 * r + Vector3.up * mid;
                Tri(m, e0, top, e1, c, d0 + d1 + Vector3.up * 0.6f);
                Tri(m, e0, e1, bottom, c * 0.75f, d0 + d1 - Vector3.up * 0.6f);
            }
        }

        /// <summary>A flat-shaded triangle wound so its front faces <paramref name="outward"/>.</summary>
        private static void Tri(MeshData m, Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0f)
            {
                Vector3 t = b;
                b = c;
                c = t;
                n = -n;
            }
            n.Normalize();
            col.a = 1f;
            int i = m.V.Count;
            m.V.Add(a);
            m.V.Add(b);
            m.V.Add(c);
            for (int k = 0; k < 3; k++)
            {
                m.N.Add(n);
                m.C.Add(col);
            }
            m.T.Add(i);
            m.T.Add(i + 1);
            m.T.Add(i + 2);
        }

        private void Finish(long key, View v, MeshData[] cells)
        {
            ChunkContent.FromKey(key, out int x, out int z);
            v.Root = new GameObject($"Far trees {x},{z}");
            v.Root.transform.SetParent(_root, false);
            v.Root.transform.localPosition = new Vector3(x * WorldLayout.ChunkSize, 0f, z * WorldLayout.ChunkSize);
            for (int k = 0; k < cells.Length; k++)
            {
                MeshData d = cells[k];
                if (d == null || d.V.Count == 0) continue;
                var mesh = new Mesh { name = "Far trees", indexFormat = d.V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(d.V);
                mesh.SetNormals(d.N);
                mesh.SetColors(d.C);
                mesh.SetTriangles(d.T, 0);
                mesh.RecalculateBounds();
                var go = new GameObject("Cell");
                go.transform.SetParent(v.Root.transform, false);
                go.layer = Layers.Prop;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false; // the next scan shows it if it is far enough
                v.Parts[k] = r;
            }
        }
    }
}
