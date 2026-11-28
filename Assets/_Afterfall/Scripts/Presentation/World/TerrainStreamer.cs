using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// The ground as 512 m terrain tiles built around the survivor: tiles near the start are built
    /// before play begins, others are painted on worker threads and uploaded a tile at a time as
    /// the survivor approaches, and far ones are released. Neighbouring tiles are stitched so LOD
    /// seams never open. A 512 m map is a single tile, built at load as before.
    /// </summary>
    public sealed class TerrainStreamer : MonoBehaviour
    {
        /// <summary>Tiles whose nearest edge is within this distance of the survivor are kept.</summary>
        public const float LoadRadius = 700f;
        public const float UnloadRadius = 1100f;

        private sealed class Tile
        {
            public int X, Z;
            public Terrain Terrain;
            public GroundWeights Weights;
            public Task<GroundWeights> Painting;
        }

        private GameController _gc;
        private WorldLayout _layout;
        private int _seed;
        private Transform _root;
        private int _across;
        private readonly Dictionary<int, Tile> _tiles = new Dictionary<int, Tile>();
        private readonly List<int> _drop = new List<int>();
        private Texture2D _tint;
        private float _scan;
        private Vector3 _hold;
        private float _holdUntil = -1f;

        /// <summary>Splat weights for ground cover across every loaded tile.</summary>
        public WorldWeights Weights { get; private set; }

        public int LoadedTiles
        {
            get
            {
                int n = 0;
                foreach (Tile t in _tiles.Values)
                    if (t.Terrain != null) n++;
                return n;
            }
        }

        public int TotalTiles => _across * _across;

        /// <summary>Counts tiles built, so the NavMesh window knows to take in new ground.</summary>
        public int Version { get; private set; }
        public int PaintingTiles { get; private set; }

        public void Initialize(GameController gc, WorldLayout layout, int seed, Transform root)
        {
            _gc = gc;
            _layout = layout;
            _seed = seed;
            _root = root;
            _across = WorldBuilder.TilesAcross(layout);
            Weights = new WorldWeights(this);
            WorldBuilder.PrepareLayers();
            _tint = WorldBuilder.CreateGroundTint(layout);
        }

        private int Key(int x, int z) => z * _across + x;

        /// <summary>Builds every tile within <paramref name="radius"/> of a point now (load screens, teleports).</summary>
        public void BuildAround(Vector3 p, float radius)
        {
            p = p.ToWorld();
            var pending = new List<Tile>();
            ForTilesNear(p, radius, (x, z) =>
            {
                Tile t = Get(x, z);
                if (t.Terrain == null && t.Painting == null) t.Painting = Paint(x, z, true);
                if (t.Terrain == null) pending.Add(t);
            });
            foreach (Tile t in pending) Finish(t);
            Stitch();
        }

        /// <summary>True once the tile under a point is built (a teleport waits for this).</summary>
        public bool IsReadyAt(Vector3 p)
        {
            p = p.ToWorld();
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x / WorldBuilder.TileSize), 0, _across - 1), z = Mathf.Clamp(Mathf.FloorToInt(p.z / WorldBuilder.TileSize), 0, _across - 1);
            return _tiles.TryGetValue(Key(x, z), out Tile t) && t.Terrain != null;
        }

        /// <summary>Starts painting the tiles around a point now (a teleport is about to go there).</summary>
        public void Prefetch(Vector3 p)
        {
            p = p.ToWorld();
            // Keep the destination's tiles while the camera is still far away (a teleport is waiting for them).
            _hold = p;
            _holdUntil = Time.unscaledTime + 6f;
            ForTilesNear(p, WorldBuilder.TileSize * 0.6f, (x, z) =>
            {
                Tile t = Get(x, z);
                if (t.Terrain == null && t.Painting == null) t.Painting = Paint(x, z);
            });
        }

        private Tile Get(int x, int z)
        {
            int k = Key(x, z);
            if (!_tiles.TryGetValue(k, out Tile t)) _tiles[k] = t = new Tile { X = x, Z = z };
            return t;
        }

        /// <summary>
        /// Starts painting a tile on a worker. In a chunked world the tile's chunk must exist first (its
        /// streets and buildings are painted): returns null and asks for it unless <paramref name="now"/>.
        /// </summary>
        private Task<GroundWeights> Paint(int x, int z, bool now = false)
        {
            WorldLayout layout = _layout;
            if (layout.IsChunked && !layout.HasChunk(x, z))
            {
                if (now) layout.EnsureChunk(x, z);
                else
                {
                    layout.RequestChunk(x, z);
                    layout.IntegrateReady(4);
                    if (!layout.HasChunk(x, z)) return null;
                }
            }
            int seed = _seed;
            float ox = x * WorldBuilder.TileSize, oz = z * WorldBuilder.TileSize;
            WorldBuilder.TileFeatures features = WorldBuilder.CollectFeatures(layout, ox, oz, WorldBuilder.TileSize);
            return Task.Run(() => WorldBuilder.PaintSplat(layout, seed, ox, oz, WorldBuilder.TileSize, features));
        }

        private void Finish(Tile t)
        {
            GroundWeights w = t.Painting.Result;
            t.Painting = null;
            t.Weights = w;
            t.Terrain = WorldBuilder.BuildTile(_layout, t.X, t.Z, w, _root);
            Version++;
            _gc.MarkNavDirty(new Rect(t.X * WorldBuilder.TileSize, t.Z * WorldBuilder.TileSize, WorldBuilder.TileSize, WorldBuilder.TileSize));
            WorldBuilder.PublishGroundTint(w, _tint, _layout.Size);
        }

        private void ForTilesNear(Vector3 p, float radius, System.Action<int, int> each)
        {
            float ts = WorldBuilder.TileSize;
            int xa = Mathf.Max(0, Mathf.FloorToInt((p.x - radius) / ts)), xb = Mathf.Min(_across - 1, Mathf.FloorToInt((p.x + radius) / ts));
            int za = Mathf.Max(0, Mathf.FloorToInt((p.z - radius) / ts)), zb = Mathf.Min(_across - 1, Mathf.FloorToInt((p.z + radius) / ts));
            for (int z = za; z <= zb; z++)
                for (int x = xa; x <= xb; x++)
                    if (Near(p, x * ts, z * ts, radius)) each(x, z);
        }

        private void Update()
        {
            if (_gc == null || _gc.Player == null) return;
            // Tiles stream around the camera and the survivor: they part in flight and strategic view,
            // and for a frame or two after a teleport, and the ground under the survivor must stay.
            Vector3 survivor = _gc.Player.transform.position.ToWorld();
            Vector3 p = _gc.CameraRig != null && _gc.CameraRig.Camera != null ? _gc.CameraRig.Camera.transform.position.ToWorld() : survivor;
            // Upload at most one finished tile per frame; uploading is the costly main-thread part.
            Tile ready = null;
            int painting = 0;
            foreach (Tile t in _tiles.Values)
            {
                if (t.Painting == null) continue;
                painting++;
                if (ready == null && t.Painting.IsCompleted) ready = t;
            }
            PaintingTiles = painting;
            if (ready != null)
            {
                Finish(ready);
                Stitch();
            }
            _scan -= Time.unscaledDeltaTime;
            if (_scan > 0f) return;
            _scan = 0.5f;
            System.Action<int, int> load = (x, z) =>
            {
                Tile t = Get(x, z);
                if (t.Terrain == null && t.Painting == null) t.Painting = Paint(x, z);
            };
            ForTilesNear(survivor, LoadRadius, load);
            if (Flat(p - survivor) > 50f) ForTilesNear(p, LoadRadius, load);
            _drop.Clear();
            foreach (KeyValuePair<int, Tile> kv in _tiles)
            {
                Tile t = kv.Value;
                if (t.Terrain == null) continue;
                float x0 = t.X * WorldBuilder.TileSize, z0 = t.Z * WorldBuilder.TileSize;
                if (Near(p, x0, z0, UnloadRadius) || Near(survivor, x0, z0, UnloadRadius)) continue;
                if (Time.unscaledTime < _holdUntil)
                {
                    float hx = Mathf.Max(0f, Mathf.Max(x0 - _hold.x, _hold.x - (x0 + WorldBuilder.TileSize)));
                    float hz = Mathf.Max(0f, Mathf.Max(z0 - _hold.z, _hold.z - (z0 + WorldBuilder.TileSize)));
                    if (hx * hx + hz * hz <= LoadRadius * LoadRadius) continue;
                }
                _drop.Add(kv.Key);
            }
            foreach (int k in _drop)
            {
                Tile t = _tiles[k];
                TerrainData data = t.Terrain.terrainData;
                Destroy(t.Terrain.gameObject);
                Destroy(data);
                _tiles.Remove(k);
            }
            if (_drop.Count > 0) Stitch();
        }

        private static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        private static bool Near(Vector3 p, float x0, float z0, float radius)
        {
            float dx = Mathf.Max(0f, Mathf.Max(x0 - p.x, p.x - (x0 + WorldBuilder.TileSize)));
            float dz = Mathf.Max(0f, Mathf.Max(z0 - p.z, p.z - (z0 + WorldBuilder.TileSize)));
            return dx * dx + dz * dz <= radius * radius;
        }

        private void Stitch()
        {
            foreach (Tile t in _tiles.Values)
            {
                if (t.Terrain == null) continue;
                t.Terrain.SetNeighbors(TerrainAt(t.X - 1, t.Z), TerrainAt(t.X, t.Z + 1), TerrainAt(t.X + 1, t.Z), TerrainAt(t.X, t.Z - 1));
            }
        }

        private Terrain TerrainAt(int x, int z) => x >= 0 && z >= 0 && x < _across && z < _across && _tiles.TryGetValue(Key(x, z), out Tile t) ? t.Terrain : null;

        /// <summary>The splat weights of the tile under a point, or null when it is not loaded.</summary>
        public GroundWeights WeightsAt(float x, float z)
        {
            int tx = Mathf.Clamp(Mathf.FloorToInt(x / WorldBuilder.TileSize), 0, _across - 1), tz = Mathf.Clamp(Mathf.FloorToInt(z / WorldBuilder.TileSize), 0, _across - 1);
            return _tiles.TryGetValue(Key(tx, tz), out Tile t) ? t.Weights : null;
        }
    }

    /// <summary>Ground-cover weights across all loaded tiles (bare meadow where no tile is loaded).</summary>
    public sealed class WorldWeights
    {
        private readonly TerrainStreamer _tiles;

        public WorldWeights(TerrainStreamer tiles)
        {
            _tiles = tiles;
        }

        public void Sample(float x, float z, float[] w)
        {
            GroundWeights g = _tiles.WeightsAt(x, z);
            if (g != null)
            {
                g.Sample(x, z, w);
                return;
            }
            System.Array.Clear(w, 0, w.Length);
            w[0] = 1f;
        }
    }
}
