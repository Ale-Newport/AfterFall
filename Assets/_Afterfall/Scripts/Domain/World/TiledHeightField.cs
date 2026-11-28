using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// What a chunked world generator provides: a cheap coarse height for any point (maps, far
    /// queries), full-detail heights for one terrain tile, the content of one chunk, and water
    /// at coarse detail. Every answer depends only on the seed and the position, never on which
    /// tiles or chunks were made before, so a world rebuilt from its seed is the same world.
    /// </summary>
    public interface IChunkSource
    {
        /// <summary>Height without local detail (no roads pressed in, no small noise): for maps and far away.</summary>
        float CoarseHeight(float x, float z);

        /// <summary>The coarse height from a cached grid (tens of metres per cell): world maps and overviews.</summary>
        float MapHeight(float x, float z);

        /// <summary>Fills <paramref name="heights"/> (row-major, <paramref name="res"/>²) for tile (tx, tz) at full detail.</summary>
        void BuildTile(int tx, int tz, int res, float cell, float[] heights);

        /// <summary>Generates the content of one chunk into <paramref name="chunk"/>; the layout's heights may be sampled.</summary>
        void Populate(WorldLayout layout, ChunkContent chunk);
    }

    /// <summary>One generated chunk of a large world: its slice of the layout's content.</summary>
    public sealed class ChunkContent
    {
        public ChunkContent(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }
        public long Key => ChunkKey(X, Z);
        public float MinX => X * WorldLayout.ChunkSize;
        public float MinZ => Z * WorldLayout.ChunkSize;

        public List<PropPlacement> Props { get; } = new List<PropPlacement>();
        public List<NodeSeed> Nodes { get; } = new List<NodeSeed>();
        public List<ContainerSeed> Containers { get; } = new List<ContainerSeed>();
        public List<FixtureSeed> Fixtures { get; } = new List<FixtureSeed>();
        public List<VehicleSeed> Vehicles { get; } = new List<VehicleSeed>();
        public List<Vec3> ZombieSpawns { get; } = new List<Vec3>();
        public List<RoadPath> Streets { get; } = new List<RoadPath>();
        public List<PlacedPlan> Plans { get; } = new List<PlacedPlan>();

        /// <summary>Simple city buildings (blocks, terraces, towers) drawn merged per chunk, never entered.</summary>
        public List<PropPlacement> Blocks { get; } = new List<PropPlacement>();

        public static long ChunkKey(int x, int z) => ((long)x << 32) ^ (uint)z;
        public static void FromKey(long key, out int x, out int z)
        {
            x = (int)(key >> 32);
            z = (int)(uint)key;
        }
    }

    /// <summary>
    /// Heights in 512 m tiles at 1 m spacing, made on demand by an <see cref="IChunkSource"/> and
    /// kept in a bounded cache (least recently used tiles are dropped and remade when needed).
    /// Thread-safe: the presentation paints tiles on worker threads.
    /// </summary>
    public sealed class TiledHeightField : HeightSource
    {
        public const int TileCells = 512;
        private const int TileRes = TileCells + 1;

        private readonly IChunkSource _source;
        private readonly int _tiles;
        private readonly int _capacity;
        private readonly Dictionary<long, float[]> _cache = new Dictionary<long, float[]>();
        private readonly LinkedList<long> _order = new LinkedList<long>();
        private readonly Dictionary<long, LinkedListNode<long>> _nodes = new Dictionary<long, LinkedListNode<long>>();
        private readonly object _lock = new object();

        [ThreadStatic] private static float[] _lastTile;
        [ThreadStatic] private static long _lastKey;
        [ThreadStatic] private static TiledHeightField _lastOwner;

        public TiledHeightField(IChunkSource source, int tilesAcross, float maxHeight, int capacity = 96)
        {
            _source = source;
            _tiles = tilesAcross;
            MaxHeight = maxHeight;
            _capacity = Math.Max(16, capacity);
            Resolution = tilesAcross * TileCells + 1;
            Size = tilesAcross * TileCells;
        }

        public override int Resolution { get; }
        public override float Size { get; }
        public override float MaxHeight { get; }

        /// <summary>Tiles made so far (each one is made again if it was dropped).</summary>
        public int TilesBuilt { get; private set; }

        public int CachedTiles
        {
            get
            {
                lock (_lock) return _cache.Count;
            }
        }

        public override float Get(int x, int z)
        {
            x = x < 0 ? 0 : x >= Resolution ? Resolution - 1 : x;
            z = z < 0 ? 0 : z >= Resolution ? Resolution - 1 : z;
            int tx = Math.Min(_tiles - 1, x / TileCells), tz = Math.Min(_tiles - 1, z / TileCells);
            float[] tile = Tile(tx, tz);
            return tile[(z - tz * TileCells) * TileRes + (x - tx * TileCells)];
        }

        /// <summary>The heights of one tile (made now if needed).</summary>
        public float[] Tile(int tx, int tz)
        {
            long key = ((long)tx << 32) ^ (uint)tz;
            if (_lastOwner == this && _lastKey == key && _lastTile != null) return _lastTile;
            float[] tile;
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out tile))
                {
                    LinkedListNode<long> n = _nodes[key];
                    _order.Remove(n);
                    _order.AddFirst(n);
                }
            }
            if (tile == null)
            {
                tile = new float[TileRes * TileRes];
                _source.BuildTile(tx, tz, TileRes, 1f, tile);
                lock (_lock)
                {
                    if (_cache.TryGetValue(key, out float[] other)) tile = other; // another thread made it meanwhile
                    else
                    {
                        _cache[key] = tile;
                        _nodes[key] = _order.AddFirst(key);
                        TilesBuilt++;
                        while (_cache.Count > _capacity)
                        {
                            long old = _order.Last.Value;
                            _order.RemoveLast();
                            _nodes.Remove(old);
                            _cache.Remove(old);
                        }
                    }
                }
            }
            _lastOwner = this;
            _lastKey = key;
            _lastTile = tile;
            return tile;
        }

        /// <summary>Whether a tile is in the cache now (no work).</summary>
        public bool IsCached(int tx, int tz)
        {
            lock (_lock) return _cache.ContainsKey(((long)tx << 32) ^ (uint)tz);
        }
    }
}
