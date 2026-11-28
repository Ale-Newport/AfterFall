using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Engine-independent terrain heights on a square grid starting at (0, 0): a whole array
    /// (<see cref="HeightField"/>) or tiles made on demand (<see cref="TiledHeightField"/>).
    /// The presentation builds Unity Terrain tiles from it.
    /// </summary>
    public abstract class HeightSource
    {
        /// <summary>Samples along one side (grid points, not cells).</summary>
        public abstract int Resolution { get; }
        public abstract float Size { get; }
        public abstract float MaxHeight { get; }
        public float CellSize => Size / (Resolution - 1);

        /// <summary>Height at a grid point (clamped to the grid).</summary>
        public abstract float Get(int x, int z);

        /// <summary>Bilinear height at a world position (clamped to the map).</summary>
        public virtual float Sample(float wx, float wz)
        {
            float fx = wx / CellSize;
            float fz = wz / CellSize;
            int x0 = (int)Math.Floor(fx);
            int z0 = (int)Math.Floor(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            float h00 = Get(x0, z0);
            float h10 = Get(x0 + 1, z0);
            float h01 = Get(x0, z0 + 1);
            float h11 = Get(x0 + 1, z0 + 1);
            float a = h00 + (h10 - h00) * tx;
            float b = h01 + (h11 - h01) * tx;
            return a + (b - a) * tz;
        }

        public float SlopeDegrees(float wx, float wz)
        {
            float d = CellSize;
            float dx = (Sample(wx + d, wz) - Sample(wx - d, wz)) / (2 * d);
            float dz = (Sample(wx, wz + d) - Sample(wx, wz - d)) / (2 * d);
            return (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180.0 / Math.PI);
        }
    }

    /// <summary>Heights held in one array (the 512 m valley and the 2 km region).</summary>
    public sealed class HeightField : HeightSource
    {
        public HeightField(int resolution, float size, float maxHeight)
        {
            Resolution = resolution;
            Size = size;
            MaxHeight = maxHeight;
            Heights = new float[resolution * resolution];
        }

        public override int Resolution { get; }
        public override float Size { get; }
        public override float MaxHeight { get; }

        /// <summary>Row-major heights in metres: index = z * Resolution + x.</summary>
        public float[] Heights { get; }

        public new float CellSize => Size / (Resolution - 1);

        public override float Get(int x, int z)
        {
            x = x < 0 ? 0 : x >= Resolution ? Resolution - 1 : x;
            z = z < 0 ? 0 : z >= Resolution ? Resolution - 1 : z;
            return Heights[z * Resolution + x];
        }

        public void Set(int x, int z, float h) => Heights[z * Resolution + x] = h < 0 ? 0 : h > MaxHeight ? MaxHeight : h;

        /// <summary>Bilinear height (inlined for the whole-array case, which the generators use heavily).</summary>
        public override float Sample(float wx, float wz)
        {
            float cell = Size / (Resolution - 1);
            float fx = wx / cell;
            float fz = wz / cell;
            int x0 = (int)Math.Floor(fx);
            int z0 = (int)Math.Floor(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            float h00 = Get(x0, z0);
            float h10 = Get(x0 + 1, z0);
            float h01 = Get(x0, z0 + 1);
            float h11 = Get(x0 + 1, z0 + 1);
            float a = h00 + (h10 - h00) * tx;
            float b = h01 + (h11 - h01) * tx;
            return a + (b - a) * tz;
        }
    }

    /// <summary>Deterministic value noise (no dependency on engine noise functions).</summary>
    public static class Noise
    {
        public static float Hash01(int seed, int x, int z)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)z * 0xC2B2AE3Du;
                h *= 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x165667B1u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Smooth value noise in [-1, 1].</summary>
        public static float Value(int seed, float x, float z)
        {
            int x0 = (int)Math.Floor(x);
            int z0 = (int)Math.Floor(z);
            float tx = Smooth(x - x0);
            float tz = Smooth(z - z0);
            float a = Hash01(seed, x0, z0);
            float b = Hash01(seed, x0 + 1, z0);
            float c = Hash01(seed, x0, z0 + 1);
            float d = Hash01(seed, x0 + 1, z0 + 1);
            float ab = a + (b - a) * tx;
            float cd = c + (d - c) * tx;
            return (ab + (cd - ab) * tz) * 2f - 1f;
        }

        /// <summary>Fractal Brownian motion, normalised to roughly [-1, 1].</summary>
        public static float Fbm(int seed, float x, float z, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(seed + i * 101, x * freq, z * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }

    public sealed class PoiInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>residential, fuel, workshop, substation, quarry, farm, camp.</summary>
        public string Kind { get; set; } = "";

        public Vec3 Center { get; set; }
        public float Radius { get; set; }

        /// <summary>Low, Medium, High — shown to the player once discovered.</summary>
        public string Risk { get; set; } = "Low";

        public string Description { get; set; } = "";
    }

    /// <summary>Static scenery (houses, wrecks, fences). Regenerated from the seed; not persisted.</summary>
    public sealed class PropPlacement
    {
        public string Kind { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public Vec3 Size { get; set; }
        public string PoiId { get; set; } = "";
        public int Variant { get; set; }

        /// <summary>For planned multi-storey buildings: the <see cref="BuildingPlan"/> id the shell is built from.</summary>
        public string PlanId { get; set; } = "";

        /// <summary>Tilt in degrees, positive when the +Z end is higher (bridges between banks of different height).</summary>
        public float Pitch { get; set; }
    }

    public sealed class RoadPath
    {
        public List<Vec3> Points { get; } = new List<Vec3>();
        public float Width { get; set; } = 7f;
        public bool Paved { get; set; } = true;
        /// <summary>A railway line (ballast and track) rather than a road.</summary>
        public bool Rail { get; set; }
    }

    /// <summary>A river: a centre line whose points carry the water surface height (falling downstream) and a width.</summary>
    public sealed class RiverPath
    {
        public string Name { get; set; } = "";
        public List<Vec3> Points { get; } = new List<Vec3>();
        public float Width { get; set; } = 14f;
    }

    /// <summary>A lake or reservoir: a round body of water with a level.</summary>
    public sealed class LakeInfo
    {
        public string Name { get; set; } = "";
        public Vec3 Center { get; set; }
        public float Radius { get; set; }
        public float Level { get; set; }
    }

    public sealed class NodeSeed
    {
        /// <summary>Stable key in chunked worlds ("" in prebuilt ones), so a node is made once and found again.</summary>
        public string Key { get; set; } = "";
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public float Scale { get; set; } = 1f;
    }

    public sealed class ContainerSeed
    {
        /// <summary>Stable key in chunked worlds ("" in prebuilt ones).</summary>
        public string Key { get; set; } = "";
        public string DefId { get; set; } = "";
        public string LootTable { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public string PoiId { get; set; } = "";
    }

    /// <summary>
    /// A structure the layout places by a stable key (doors, furniture and containers of planned
    /// buildings). Created in new games and, once, in older saves that do not have it yet.
    /// </summary>
    public sealed class FixtureSeed
    {
        public string Key { get; set; } = "";
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public string PoiId { get; set; } = "";
        public string LootTable { get; set; } = "";
        public string LockId { get; set; } = "";
        public bool Locked { get; set; }
        public List<string> KeysInside { get; } = new List<string>();
        public List<string> ItemsInside { get; } = new List<string>();
    }

    /// <summary>An abandoned vehicle the layout places by a stable key (created once, like fixtures).</summary>
    public sealed class VehicleSeed
    {
        public string Key { get; set; } = "";
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public int Paint { get; set; }
        public float Fuel { get; set; }
        public float Engine { get; set; } = 100f;
        public float Body { get; set; } = 100f;
        public float Tyres { get; set; } = 100f;
        public float Battery { get; set; } = 100f;
        public bool Locked { get; set; }
        public string LockId { get; set; } = "";
        public bool KeyInIgnition { get; set; }
        public List<string> TrunkItems { get; } = new List<string>();
    }

    /// <summary>A multi-storey building placed from a <see cref="BuildingPlan"/> (origin at its ground, facing by yaw).</summary>
    public sealed class PlacedPlan
    {
        public BuildingPlan Plan { get; set; }
        public Vec3 Origin { get; set; }
        public float Yaw { get; set; }
        public string PoiId { get; set; } = "";

        /// <summary>World position of a point in the plan's frame on a floor.</summary>
        public Vec3 ToWorld(float x, float z, int floor)
        {
            Vec3 o = WorldGenerator.Rotate(new Vec3(x, 0f, z), Yaw);
            return new Vec3(Origin.X + o.X, Origin.Y + Plan.FloorY(floor), Origin.Z + o.Z);
        }

        /// <summary>Plan-frame position and floor of a world point, or false when it is outside the building.</summary>
        public bool ToPlan(Vec3 p, out float x, out float z, out int floor)
        {
            Vec3 local = WorldGenerator.Rotate(new Vec3(p.X - Origin.X, 0f, p.Z - Origin.Z), -Yaw);
            x = local.X;
            z = local.Z;
            floor = (int)Math.Floor((p.Y - Origin.Y - Plan.GroundFloorY + 0.6f) / Plan.FloorHeight);
            return Math.Abs(x) <= Plan.Width * 0.5f && Math.Abs(z) <= Plan.Depth * 0.5f && floor >= 0 && floor < Plan.Floors;
        }
    }

    /// <summary>A survivor who can be recruited, placed by the generator.</summary>
    public sealed class SurvivorSeed
    {
        public string Name { get; set; } = "";
        public Vec3 Position { get; set; }
        public string PoiId { get; set; } = "";

        /// <summary>Job type name → skill multiplier (e.g. Build 1.4).</summary>
        public Dictionary<string, float> Skills { get; } = new Dictionary<string, float>();
    }

    /// <summary>Generated static description of the playable region.</summary>
    public sealed class WorldLayout
    {
        public const float WorldSize = 512f;
        public const int HeightResolution = 513;
        public const float MaxTerrainHeight = 90f;

        public HeightSource Height { get; set; }
        public float WaterLevel { get; set; }
        public Vec3 LakeCenter { get; set; }
        public float LakeRadius { get; set; }
        public List<PoiInfo> Pois { get; } = new List<PoiInfo>();
        public List<PropPlacement> Props { get; } = new List<PropPlacement>();
        public List<RoadPath> Roads { get; } = new List<RoadPath>();
        public Vec3 PlayerSpawn { get; set; }
        public List<Vec3> ZombieSpawnPoints { get; } = new List<Vec3>();
        public List<NodeSeed> Nodes { get; } = new List<NodeSeed>();
        public List<ContainerSeed> Containers { get; } = new List<ContainerSeed>();
        public List<SurvivorSeed> Survivors { get; } = new List<SurvivorSeed>();
        public List<FixtureSeed> Fixtures { get; } = new List<FixtureSeed>();
        public List<PlacedPlan> Plans { get; } = new List<PlacedPlan>();
        public List<VehicleSeed> Vehicles { get; } = new List<VehicleSeed>();
        public List<RiverPath> Rivers { get; } = new List<RiverPath>();
        public List<LakeInfo> Lakes { get; } = new List<LakeInfo>();

        // ------------------------------------------------------------------ chunked worlds

        /// <summary>Side of a chunk (and of a terrain tile) in metres.</summary>
        public const float ChunkSize = 512f;

        /// <summary>The generator of a chunked world (version 3); null when the whole layout is built up front.</summary>
        public IChunkSource Chunks { get; set; }
        public bool IsChunked => Chunks != null;
        public int ChunksAcross => (int)Math.Ceiling(Size / ChunkSize - 0.001f);

        /// <summary>Sea level of a chunked world (land below it near the coast is sea); the valley's water line otherwise.</summary>
        public float SeaLevel { get; set; }

        private readonly Dictionary<long, ChunkContent> _chunks = new Dictionary<long, ChunkContent>();

        /// <summary>Counts chunks generated, so views know to look for new content.</summary>
        public int ChunkVersion { get; private set; }

        private int _fixedRoads = -1;

        /// <summary>How many of <see cref="Roads"/> are the world's fixed roads; the streets of generated chunks follow them.</summary>
        public int FixedRoadCount => _fixedRoads < 0 ? Roads.Count : _fixedRoads;

        public IEnumerable<ChunkContent> GeneratedChunks => _chunks.Values;
        public int GeneratedChunkCount => _chunks.Count;

        public bool HasChunk(int x, int z) => _chunks.ContainsKey(ChunkContent.ChunkKey(x, z));

        public ChunkContent GetChunk(int x, int z) => _chunks.TryGetValue(ChunkContent.ChunkKey(x, z), out ChunkContent c) ? c : null;

        public static int ChunkOf(float coordinate) => (int)Math.Floor(coordinate / ChunkSize);

        /// <summary>
        /// The chunk's content, generated now if needed and appended to the layout's lists (props,
        /// nodes, containers, fixtures, vehicles, spawn points, streets). Main thread only.
        /// Null outside the map or in a prebuilt layout.
        /// </summary>
        public ChunkContent EnsureChunk(int x, int z)
        {
            if (Chunks == null || x < 0 || z < 0 || x >= ChunksAcross || z >= ChunksAcross) return null;
            long key = ChunkContent.ChunkKey(x, z);
            if (_chunks.TryGetValue(key, out ChunkContent c)) return c;
            if (_pending.TryGetValue(key, out System.Threading.Tasks.Task<ChunkContent> task))
            {
                _pending.Remove(key);
                c = task.Result;
            }
            else
            {
                c = new ChunkContent(x, z);
                Chunks.Populate(this, c);
            }
            return Integrate(c);
        }

        private readonly Dictionary<long, System.Threading.Tasks.Task<ChunkContent>> _pending = new Dictionary<long, System.Threading.Tasks.Task<ChunkContent>>();

        /// <summary>Chunks generated on workers at once at most, so the nearest requests are never stuck behind far ones.</summary>
        public const int MaxPendingChunks = 6;

        /// <summary>
        /// Starts generating a chunk on a worker thread (main thread only); <see cref="IntegrateReady"/>
        /// adds it when done. False when too many are in flight already (ask again later).
        /// </summary>
        public bool RequestChunk(int x, int z)
        {
            if (Chunks == null || x < 0 || z < 0 || x >= ChunksAcross || z >= ChunksAcross) return false;
            long key = ChunkContent.ChunkKey(x, z);
            if (_chunks.ContainsKey(key) || _pending.ContainsKey(key)) return true;
            if (_pending.Count >= MaxPendingChunks) return false;
            IChunkSource source = Chunks;
            _pending[key] = System.Threading.Tasks.Task.Run(() =>
            {
                var c = new ChunkContent(x, z);
                source.Populate(this, c);
                return c;
            });
            return true;
        }

        public bool IsChunkPending(int x, int z) => _pending.ContainsKey(ChunkContent.ChunkKey(x, z));
        public int PendingChunks => _pending.Count;

        /// <summary>Adds up to <paramref name="max"/> chunks whose generation finished (main thread); returns them.</summary>
        public List<ChunkContent> IntegrateReady(int max)
        {
            var done = new List<ChunkContent>();
            if (_pending.Count == 0) return done;
            var keys = new List<long>();
            foreach (var kv in _pending)
                if (kv.Value.IsCompleted && keys.Count < max) keys.Add(kv.Key);
            foreach (long k in keys)
            {
                System.Threading.Tasks.Task<ChunkContent> t = _pending[k];
                _pending.Remove(k);
                if (t.IsFaulted) throw t.Exception;
                done.Add(Integrate(t.Result));
            }
            return done;
        }

        private ChunkContent Integrate(ChunkContent c)
        {
            long key = c.Key;
            if (_chunks.TryGetValue(key, out ChunkContent existing)) return existing;
            _chunks[key] = c;
            Props.AddRange(c.Props);
            Nodes.AddRange(c.Nodes);
            Containers.AddRange(c.Containers);
            Fixtures.AddRange(c.Fixtures);
            Vehicles.AddRange(c.Vehicles);
            ZombieSpawnPoints.AddRange(c.ZombieSpawns);
            if (_fixedRoads < 0) _fixedRoads = Roads.Count;
            Roads.AddRange(c.Streets);
            Plans.AddRange(c.Plans);
            ChunkVersion++;
            return c;
        }

        /// <summary>Height for maps: a cached coarse grid in a chunked world, the real height otherwise.</summary>
        public float MapHeightAt(float x, float z) => Chunks != null ? Chunks.MapHeight(x, z) : HeightAt(x, z);

        /// <summary>Height without local detail (cheap anywhere): the generator's coarse height in a chunked world.</summary>
        public float CoarseHeightAt(float x, float z) => Chunks != null ? Chunks.CoarseHeight(x, z) : HeightAt(x, z);

        /// <summary>
        /// Water at coarse detail, without making terrain tiles (maps): rivers within their banks,
        /// lakes, and sea where the coarse ground is below sea level.
        /// </summary>
        public bool CoarseWaterAt(float x, float z, out float level)
        {
            if (Chunks == null) return WaterAt(x, z, out level);
            level = SeaLevel;
            if (Chunks.MapHeight(x, z) < SeaLevel) return true;
            foreach (LakeInfo lake in Lakes)
            {
                float dx = x - lake.Center.X, dz = z - lake.Center.Z;
                if (dx * dx + dz * dz > lake.Radius * lake.Radius) continue;
                level = lake.Level;
                return true;
            }
            if (_riverIndex == null) return false;
            long k = ((long)(int)Math.Floor(x / RiverCell) << 32) ^ (uint)(int)Math.Floor(z / RiverCell);
            if (!_riverIndex.TryGetValue(k, out List<int> segs)) return false;
            foreach (int id in segs)
            {
                (int r, int i) = _riverSegs[id];
                RiverPath river = Rivers[r];
                float d = DistanceToSegmentXZ(x, z, river.Points[i], river.Points[i + 1], out float t);
                if (d > river.Width * 0.5f) continue;
                level = river.Points[i].Y + (river.Points[i + 1].Y - river.Points[i].Y) * t;
                return true;
            }
            return false;
        }

        /// <summary>Generator version that built this layout (1 = the 512 m valley, 2 = the 2 km region, 3 = the 64 km basin).</summary>
        public int Version { get; set; } = 1;

        /// <summary>Display name of the region.</summary>
        public string RegionName { get; set; } = "Millbrook Valley";

        private Dictionary<long, List<int>> _riverIndex;
        private List<(int River, int Seg)> _riverSegs;
        private const float RiverCell = 64f;

        /// <summary>Indexes the rivers' segments by 64 m cells (call after rivers are final).</summary>
        public void IndexWater()
        {
            _riverIndex = new Dictionary<long, List<int>>();
            _riverSegs = new List<(int, int)>();
            for (int r = 0; r < Rivers.Count; r++)
            {
                RiverPath river = Rivers[r];
                for (int i = 0; i + 1 < river.Points.Count; i++)
                {
                    int id = _riverSegs.Count;
                    _riverSegs.Add((r, i));
                    Vec3 a = river.Points[i], b = river.Points[i + 1];
                    float m = river.Width;
                    int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - m) / RiverCell), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + m) / RiverCell);
                    int z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - m) / RiverCell), z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + m) / RiverCell);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            long k = ((long)x << 32) ^ (uint)z;
                            if (!_riverIndex.TryGetValue(k, out List<int> list)) _riverIndex[k] = list = new List<int>();
                            list.Add(id);
                        }
                }
            }
        }

        /// <summary>
        /// The water surface over a point, if any: the valley lake (below <see cref="WaterLevel"/>),
        /// a river within its banks, or a lake. Thread-safe once <see cref="IndexWater"/> has run.
        /// </summary>
        public bool WaterAt(float x, float z, out float level)
        {
            level = WaterLevel;
            float h = Height.Sample(x, z);
            if (h < WaterLevel + 0.15f) return true;
            foreach (LakeInfo lake in Lakes)
            {
                float dx = x - lake.Center.X, dz = z - lake.Center.Z;
                if (dx * dx + dz * dz <= lake.Radius * lake.Radius && h < lake.Level + 0.15f)
                {
                    level = lake.Level;
                    return true;
                }
            }
            if (_riverIndex == null || _riverIndex.Count == 0) return false;
            long k = ((long)(int)Math.Floor(x / RiverCell) << 32) ^ (uint)(int)Math.Floor(z / RiverCell);
            if (!_riverIndex.TryGetValue(k, out List<int> segs)) return false;
            foreach (int id in segs)
            {
                (int r, int i) = _riverSegs[id];
                RiverPath river = Rivers[r];
                float d = DistanceToSegmentXZ(x, z, river.Points[i], river.Points[i + 1], out float t);
                if (d > river.Width * 0.5f) continue;
                float lvl = river.Points[i].Y + (river.Points[i + 1].Y - river.Points[i].Y) * t;
                if (h > lvl + 0.15f) continue;
                level = lvl;
                return true;
            }
            return false;
        }


        /// <summary>The planned building and room at a world point (for room names and indoor checks), if any.</summary>
        public PlanRoom RoomAt(Vec3 p, out PlacedPlan building)
        {
            foreach (PlacedPlan b in Plans)
            {
                if (!b.ToPlan(p, out float x, out float z, out int floor)) continue;
                building = b;
                return b.Plan.RoomAt(floor, x, z);
            }
            building = null;
            return null;
        }

        /// <summary>Side length of this layout's square map in metres.</summary>
        public float Size => Height != null ? Height.Size : WorldSize;

        /// <summary>Highest terrain height the height field can hold.</summary>
        public float MaxHeight => Height != null ? Height.MaxHeight : MaxTerrainHeight;

        public float HeightAt(float x, float z) => Height.Sample(x, z);
        public Vec3 Ground(float x, float z) => new Vec3(x, Height.Sample(x, z), z);
        public bool IsWater(float x, float z) => WaterAt(x, z, out _);

        /// <summary>
        /// The level of the water nearest a point within <paramref name="reach"/> metres of a river
        /// bank or lake shore (for shore painting); the valley's water line otherwise.
        /// </summary>
        public bool WaterNear(float x, float z, float reach, out float level)
        {
            level = WaterLevel;
            foreach (LakeInfo lake in Lakes)
            {
                float dx = x - lake.Center.X, dz = z - lake.Center.Z, r = lake.Radius + reach;
                if (dx * dx + dz * dz > r * r) continue;
                level = lake.Level;
                return true;
            }
            if (_riverIndex == null || _riverIndex.Count == 0) return false;
            long k = ((long)(int)Math.Floor(x / RiverCell) << 32) ^ (uint)(int)Math.Floor(z / RiverCell);
            if (!_riverIndex.TryGetValue(k, out List<int> segs)) return false;
            float best = float.MaxValue;
            foreach (int id in segs)
            {
                (int r, int i) = _riverSegs[id];
                RiverPath river = Rivers[r];
                float d = DistanceToSegmentXZ(x, z, river.Points[i], river.Points[i + 1], out float t);
                if (d > river.Width * 0.5f + reach || d >= best) continue;
                best = d;
                level = river.Points[i].Y + (river.Points[i + 1].Y - river.Points[i].Y) * t;
            }
            return best < float.MaxValue;
        }

        public bool IsInsideMap(float x, float z, float margin = 0f) =>
            x >= margin && z >= margin && x <= Size - margin && z <= Size - margin;

        public float DistanceToRoad(float x, float z)
        {
            float best = float.MaxValue;
            foreach (RoadPath r in Roads)
            {
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    float d = DistanceToSegmentXZ(x, z, r.Points[i], r.Points[i + 1], out _);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        public PoiInfo PoiAt(float x, float z)
        {
            foreach (PoiInfo p in Pois)
                if (Vec3.DistanceXZ(p.Center, new Vec3(x, 0, z)) <= p.Radius) return p;
            return null;
        }

        public PoiInfo Poi(string id)
        {
            foreach (PoiInfo p in Pois)
                if (p.Id == id) return p;
            return null;
        }

        public static float DistanceToSegmentXZ(float px, float pz, Vec3 a, Vec3 b, out float t)
        {
            float abx = b.X - a.X, abz = b.Z - a.Z;
            float len2 = abx * abx + abz * abz;
            t = len2 < 1e-6f ? 0f : ((px - a.X) * abx + (pz - a.Z) * abz) / len2;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            float cx = a.X + abx * t - px;
            float cz = a.Z + abz * t - pz;
            return (float)Math.Sqrt(cx * cx + cz * cz);
        }
    }
}
