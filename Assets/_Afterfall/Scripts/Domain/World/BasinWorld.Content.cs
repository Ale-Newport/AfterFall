using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>What a point of the basin is like, for vegetation and ground painting (each 0..1).</summary>
    public struct BiomeSample
    {
        public float Mountain, Snow, Moor, Badlands, Marsh, Beach, City, Farm, Forest, Lakes;
    }

    public sealed partial class BasinWorld
    {
        public static readonly Vec3 SizeHouse = new Vec3(8, 3.2f, 6), SizeShop = new Vec3(10, 3.4f, 7), SizeClinic = new Vec3(10, 3.4f, 8),
            SizeGarage = new Vec3(14, 5, 10), SizeWarehouse = new Vec3(16, 6, 12), SizeShack = new Vec3(6, 3, 5), SizeBarn = new Vec3(12, 6, 9);

        private Dictionary<long, ChunkContent> _regionChunks;

        /// <summary>The biome mix at a point (also used to paint the ground).</summary>
        public BiomeSample Biome(float x, float z)
        {
            var b = new BiomeSample();
            if (x < BlockSize && z < BlockSize) return b;
            float wx = x + Noise.Fbm(_seed + 101, x * 0.00005f, z * 0.00005f, 3) * 3000f;
            float wz = z + Noise.Fbm(_seed + 102, x * 0.00005f + 7.3f, z * 0.00005f, 3) * 3000f;
            float h = Terrain(x, z);
            b.Mountain = S(17000f, 7000f, wx) * S(7000f, 15000f, wz);
            b.Snow = S(520f, 620f, h + Noise.Fbm(_seed + 301, x * 0.003f, z * 0.003f, 3) * 40f);
            b.Moor = S(46000f, 54000f, wz) * S(12000f, 19000f, wx) * S(53000f, 45000f, wx);
            b.Lakes = S(43000f, 49000f, wz) * S(43000f, 48000f, wx) * S(60000f, 56000f, wx);
            b.Badlands = Badlands(x, z);
            b.City = CityMask(x, z);
            b.Marsh = S(4500f, 2000f, Vec3.DistanceXZ(new Vec3(wx, 0, wz), new Vec3(53500, 0, 20000)));
            float shore = x - CoastX(z);
            b.Beach = S(-260f, -60f, shore) * S(34000f, 30000f, z) + S(-60f, -10f, shore) * S(30000f, 34000f, z) * 0.6f;
            b.Farm = S(14000f, 4000f, wz) * (1f - b.Badlands) * (1f - b.City) * S(4000f, 9000f, wx) + S(22000f, 26000f, wx) * S(16000f, 12000f, wz) * 0.6f;
            b.Farm = Math.Min(1f, b.Farm) * (1f - b.Marsh) * (1f - b.Beach);
            float thorn = S(9000f, 5000f, Vec3.DistanceXZ(new Vec3(wx, 0, wz), new Vec3(19000, 0, 44000)));
            float density = Noise.Fbm(_seed + 77, x * 0.0016f, z * 0.0016f, 3);
            b.Forest = Math.Max(0f, Math.Min(1f, density * 1.6f + thorn * 0.9f + b.Mountain * 0.35f + b.Lakes * 0.3f - b.Moor * 0.6f - b.Farm * 0.55f - b.Badlands - b.Marsh - b.Beach - b.Snow - b.City + 0.05f));
            return b;
        }

        private BiomeSample[] _mapBiomes;
        private float[] _mapDensity;
        private const int BiomeGridRes = 257;
        private readonly object _biomeLock = new object();

        /// <summary>The biome mix from a cached 256 m grid (world maps; ~65 000 samples made once).</summary>
        public BiomeSample MapBiome(float x, float z)
        {
            EnsureBiomeGrid();
            Grid(x, z, out int o, out float tx, out float tz);
            BiomeSample a = _mapBiomes[o], b = _mapBiomes[o + 1], c = _mapBiomes[o + BiomeGridRes], d = _mapBiomes[o + BiomeGridRes + 1];
            float L(float p, float q, float r, float s) => (p + (q - p) * tx) + ((r + (s - r) * tx) - (p + (q - p) * tx)) * tz;
            return new BiomeSample
            {
                Mountain = L(a.Mountain, b.Mountain, c.Mountain, d.Mountain), Snow = L(a.Snow, b.Snow, c.Snow, d.Snow), Moor = L(a.Moor, b.Moor, c.Moor, d.Moor),
                Badlands = L(a.Badlands, b.Badlands, c.Badlands, d.Badlands), Marsh = L(a.Marsh, b.Marsh, c.Marsh, d.Marsh), Beach = L(a.Beach, b.Beach, c.Beach, d.Beach),
                City = L(a.City, b.City, c.City, d.City), Farm = L(a.Farm, b.Farm, c.Farm, d.Farm), Forest = L(a.Forest, b.Forest, c.Forest, d.Forest), Lakes = L(a.Lakes, b.Lakes, c.Lakes, d.Lakes)
            };
        }

        /// <summary>How built-up the city is, from the cached grid (world maps).</summary>
        public float MapDensity(float x, float z)
        {
            EnsureBiomeGrid();
            Grid(x, z, out int o, out float tx, out float tz);
            float a = _mapDensity[o] + (_mapDensity[o + 1] - _mapDensity[o]) * tx;
            float b = _mapDensity[o + BiomeGridRes] + (_mapDensity[o + BiomeGridRes + 1] - _mapDensity[o + BiomeGridRes]) * tx;
            return a + (b - a) * tz;
        }

        private static void Grid(float x, float z, out int o, out float tx, out float tz)
        {
            float cell = WorldSize / (BiomeGridRes - 1);
            float fx = Math.Max(0f, Math.Min(BiomeGridRes - 1.001f, x / cell)), fz = Math.Max(0f, Math.Min(BiomeGridRes - 1.001f, z / cell));
            int ix = (int)fx, iz = (int)fz;
            tx = fx - ix;
            tz = fz - iz;
            o = iz * BiomeGridRes + ix;
        }

        private void EnsureBiomeGrid()
        {
            if (_mapDensity != null) return;
            lock (_biomeLock)
            {
                if (_mapDensity != null) return;
                var biomes = new BiomeSample[BiomeGridRes * BiomeGridRes];
                var density = new float[BiomeGridRes * BiomeGridRes];
                float cell = WorldSize / (BiomeGridRes - 1);
                System.Threading.Tasks.Parallel.For(0, BiomeGridRes, j =>
                {
                    for (int i = 0; i < BiomeGridRes; i++)
                    {
                        biomes[j * BiomeGridRes + i] = Biome(i * cell, j * cell);
                        density[j * BiomeGridRes + i] = CityDensity(i * cell, j * cell);
                    }
                });
                _mapBiomes = biomes;
                System.Threading.Volatile.Write(ref _mapDensity, density);
            }
        }

        /// <summary>Generates one chunk: Millbrook's own content inside its block, bridges, rails, villages, the city and nature.</summary>
        public void Populate(WorldLayout layout, ChunkContent c)
        {
            var rng = DeterministicRandom.Derive(_seed, $"chunk:{c.X}:{c.Z}");
            int nodeIndex = 0, containerIndex = 0;
            string Prefix() => $"c{c.X}_{c.Z}";
            void Node(string def, float x, float z, float yaw, float scale) =>
                c.Nodes.Add(new NodeSeed { Key = $"{Prefix()}:n{nodeIndex++}", DefId = def, Position = layout.Ground(x, z), Yaw = yaw, Scale = scale });

            if (c.MinX < BlockSize && c.MinZ < BlockSize) CopyRegion(c);
            float x0 = c.MinX, z0 = c.MinZ, x1 = x0 + WorldLayout.ChunkSize, z1 = z0 + WorldLayout.ChunkSize;
            bool Inside(Vec3 p) => p.X >= x0 && p.X < x1 && p.Z >= z0 && p.Z < z1;

            foreach (PropPlacement b in _bridges)
                if (Inside(b.Position)) c.Props.Add(b);
            foreach (Seg s in _segs)
            {
                if (!s.Rail) continue;
                Vec3 mid = Vec3.Lerp(s.A, s.B, 0.5f);
                if (!Inside(mid) || OverWater(mid.X, mid.Z)) continue;
                float len = Vec3.DistanceXZ(s.A, s.B);
                if (len < 1f) continue;
                c.Props.Add(new PropPlacement { Kind = "rail", Position = new Vec3(mid.X, (s.HA + s.HB) * 0.5f, mid.Z), Yaw = (float)(Math.Atan2(s.B.X - s.A.X, s.B.Z - s.A.Z) * 180.0 / Math.PI), Size = new Vec3(3.2f, 0.4f, len), Variant = (int)(mid.X + mid.Z) & 3 });
            }

            foreach (Village v in _villages)
                if (Inside(v.Center)) PopulateVillage(layout, c, v, rng, ref containerIndex);

            if (c.MinX >= BlockSize || c.MinZ >= BlockSize)
            {
                PopulateCity(layout, c, rng, ref nodeIndex, ref containerIndex);
                PopulateNature(layout, c, rng, Node);
                PopulateInteriors(layout, c);
            }
        }

        // ------------------------------------------------------------------ Greater Millbrook

        private void CopyRegion(ChunkContent c)
        {
            if (_regionChunks == null)
            {
                _regionChunks = new Dictionary<long, ChunkContent>();
                ChunkContent Bucket(Vec3 p)
                {
                    int x = WorldLayout.ChunkOf(Math.Min(p.X, BlockSize - 1f)), z = WorldLayout.ChunkOf(Math.Min(p.Z, BlockSize - 1f));
                    long k = ChunkContent.ChunkKey(x, z);
                    if (!_regionChunks.TryGetValue(k, out ChunkContent b)) _regionChunks[k] = b = new ChunkContent(x, z);
                    return b;
                }
                foreach (PropPlacement p in _region.Props) Bucket(p.Position).Props.Add(p);
                for (int i = 0; i < _region.Nodes.Count; i++)
                {
                    NodeSeed n = _region.Nodes[i];
                    n.Key = "r:n" + i;
                    Bucket(n.Position).Nodes.Add(n);
                }
                for (int i = 0; i < _region.Containers.Count; i++)
                {
                    ContainerSeed s = _region.Containers[i];
                    s.Key = "r:c" + i;
                    Bucket(s.Position).Containers.Add(s);
                }
                foreach (FixtureSeed f in _region.Fixtures) Bucket(f.Position).Fixtures.Add(f);
                foreach (VehicleSeed v in _region.Vehicles) Bucket(v.Position).Vehicles.Add(v);
                foreach (Vec3 p in _region.ZombieSpawnPoints) Bucket(p).ZombieSpawns.Add(p);
                foreach (PlacedPlan p in _region.Plans) Bucket(p.Origin).Plans.Add(p);
            }
            if (!_regionChunks.TryGetValue(c.Key, out ChunkContent src)) return;
            c.Props.AddRange(src.Props);
            c.Nodes.AddRange(src.Nodes);
            c.Containers.AddRange(src.Containers);
            c.Fixtures.AddRange(src.Fixtures);
            c.Vehicles.AddRange(src.Vehicles);
            c.ZombieSpawns.AddRange(src.ZombieSpawns);
            c.Plans.AddRange(src.Plans);
        }

        // ------------------------------------------------------------------ villages

        private void PopulateVillage(WorldLayout layout, ChunkContent c, Village v, DeterministicRandom rng, ref int containerIndex)
        {
            int count = v.Kind == "town" ? 26 : v.Kind == "village" ? 14 : v.Kind == "outpost" ? 7 : 6;
            var placed = new List<(Vec3 P, float R)>();
            int ci = containerIndex;
            for (int attempt = 0; attempt < count * 8 && placed.Count < count; attempt++)
            {
                double a = rng.NextFloat() * Math.PI * 2;
                float d = 14f + rng.NextFloat() * v.Radius;
                float x = v.Center.X + (float)Math.Cos(a) * d, z = v.Center.Z + (float)Math.Sin(a) * d;
                string kind;
                Vec3 size;
                float roll = rng.NextFloat();
                if (v.Id == "redrock") { kind = roll < 0.5f ? "shack" : roll < 0.8f ? "garage" : "warehouse"; }
                else if (v.Kind == "hamlet") { kind = roll < 0.6f ? "house" : roll < 0.85f ? "barn" : "shack"; }
                else if (v.Kind == "town") { kind = roll < 0.55f ? "house" : roll < 0.75f ? "shop" : roll < 0.83f ? "clinic" : roll < 0.93f ? "garage" : "warehouse"; }
                else { kind = roll < 0.65f ? "house" : roll < 0.8f ? "shop" : roll < 0.9f ? "barn" : "garage"; }
                size = SizeOf(kind);
                float r = Math.Max(size.X, size.Z) * 0.5f + 2f;
                bool clash = false;
                foreach ((Vec3 p, float pr) in placed)
                    if (Vec3.DistanceXZ(p, new Vec3(x, 0, z)) < r + pr + 3f) clash = true;
                if (clash || NearRoad(x, z, r + 2f) || OverWater(x, z)) continue;
                // Face the village centre (and so its square).
                float yaw = (float)(Math.Atan2(v.Center.X - x, v.Center.Z - z) * 180.0 / Math.PI);
                placed.Add((new Vec3(x, 0, z), r));
                c.Props.Add(new PropPlacement { Kind = kind, Position = layout.Ground(x, z), Yaw = yaw, Size = size, PoiId = v.Id, Variant = rng.Range(0, 4) });
                foreach (WorldGenerator.ContainerSpec spec in WorldGenerator.ContainersFor(kind, rng))
                {
                    Vec3 off = WorldGenerator.Rotate(new Vec3(spec.X, 0, spec.Z), yaw);
                    c.Containers.Add(new ContainerSeed { Key = $"c{c.X}_{c.Z}:k{ci++}", DefId = spec.Def, LootTable = spec.Table, Position = layout.Ground(x + off.X, z + off.Z), Yaw = yaw + spec.Yaw, PoiId = v.Id });
                }
                if (rng.NextFloat() < 0.35f) c.ZombieSpawns.Add(layout.Ground(x + rng.Range(-8f, 8f), z + rng.Range(-8f, 8f)));
            }
            containerIndex = ci;
            if (v.Kind == "town" || v.Kind == "village")
            {
                float vx = v.Center.X + 6f, vz = v.Center.Z - 6f;
                string def = rng.NextFloat() < 0.5f ? "sedan" : "pickup";
                c.Vehicles.Add(new VehicleSeed
                {
                    Key = $"vehicle:{v.Id}", DefId = def, Position = layout.Ground(vx, vz), Yaw = rng.Range(0f, 360f), Paint = rng.Range(0, 4), Fuel = rng.Range(4f, 30f),
                    Engine = rng.Range(45f, 90f), Body = rng.Range(40f, 85f), Tyres = rng.Range(50f, 95f), Battery = rng.Range(30f, 90f), LockId = $"{v.Name} {(def == "sedan" ? "sedan" : "pickup")}",
                    KeyInIgnition = rng.NextFloat() < 0.4f
                });
            }
        }

        internal static Vec3 SizeOf(string kind)
        {
            switch (kind)
            {
                case "shop": return SizeShop;
                case "clinic": return SizeClinic;
                case "garage": return SizeGarage;
                case "warehouse": return SizeWarehouse;
                case "shack": return SizeShack;
                case "barn": return SizeBarn;
                default: return SizeHouse;
            }
        }

        internal bool NearRoad(float x, float z, float clearance)
        {
            long k = ((long)(int)Math.Floor(x / SegCell) << 32) ^ (uint)(int)Math.Floor(z / SegCell);
            if (!_segIndex.TryGetValue(k, out List<int> list)) return false;
            foreach (int id in list)
            {
                Seg s = _segs[id];
                if (WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out _) < s.Half + clearance) return true;
            }
            return false;
        }

        /// <summary>Water at coarse detail: sea, lakes, rivers within their banks (plus a margin).</summary>
        internal bool OverWater(float x, float z, float margin = 4f)
        {
            if (x > CoastX(z) - 40f) return true;
            foreach (LakeInfo lake in _layout.Lakes)
                if (Vec3.DistanceXZ(new Vec3(x, 0, z), lake.Center) < lake.Radius + margin) return true;
            List<int> segs = Lookup(_valleyIndex, ValleyCell, x, z);
            if (segs == null) return false;
            foreach (int id in segs)
            {
                RiverSeg s = _riverSegs[id];
                if (WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out _) < s.Half + margin) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ nature

        private void PopulateNature(WorldLayout layout, ChunkContent c, DeterministicRandom rng, Action<string, float, float, float, float> node)
        {
            const float spacing = 11f;
            for (float z = c.MinZ + 4f; z < c.MinZ + WorldLayout.ChunkSize - 4f; z += spacing)
                for (float x = c.MinX + 4f; x < c.MinX + WorldLayout.ChunkSize - 4f; x += spacing)
                {
                    float px = x + rng.Range(-4.5f, 4.5f), pz = z + rng.Range(-4.5f, 4.5f);
                    float roll = rng.NextFloat(), roll2 = rng.NextFloat();
                    BiomeSample b = Biome(px, pz);
                    if (b.City > 0.25f) continue; // the city plants its own parks
                    float h = layout.HeightAt(px, pz);
                    if (h < Sea + 0.6f || OverWater(px, pz, 3f) || layout.Height.SlopeDegrees(px, pz) > (b.Mountain > 0.5f ? 40f : 32f) || NearRoad(px, pz, 3f) || NearVillage(px, pz)) continue;
                    float treeLine = S(560f, 480f, h);
                    float forest = b.Forest * treeLine;
                    // Hedgerows: lines of trees along the field boundaries of the farmland.
                    if (b.Farm > 0.4f)
                    {
                        float fx = Math.Abs((px + Noise.Fbm(_seed + 401, px * 0.004f, pz * 0.004f, 2) * 40f) % 260f), fz = Math.Abs((pz + Noise.Fbm(_seed + 402, px * 0.004f, pz * 0.004f, 2) * 40f) % 210f);
                        if ((fx < 6f || fz < 6f) && roll < 0.5f) forest = 0.9f;
                    }
                    if (roll < forest * 0.62f)
                    {
                        string type = h > 300f || b.Mountain > 0.5f || roll2 < 0.6f ? "tree_pine" : "tree_birch";
                        node(type, px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.35f));
                    }
                    else if (b.Badlands > 0.5f)
                    {
                        if (roll < 0.05f) node(roll2 < 0.3f ? "ore_iron" : "rock_small", px, pz, rng.Range(0f, 360f), rng.Range(1f, 1.8f));
                    }
                    else if (b.Snow > 0.5f || h > 480f)
                    {
                        // Few, large rocks on the snowfields (and none on steep snow, where they would sink to flat stains).
                        if (roll < 0.02f && layout.Height.SlopeDegrees(px, pz) < 24f) node(roll2 < 0.35f ? "ore_iron" : "rock_small", px, pz, rng.Range(0f, 360f), rng.Range(1.2f, 2f));
                    }
                    else if (b.Moor > 0.5f)
                    {
                        if (roll < 0.03f) node("rock_small", px, pz, rng.Range(0f, 360f), rng.Range(1f, 2f));
                        else if (roll < 0.07f) node("plant_fiber", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                        else if (roll < 0.08f) node("bush_berry", px, pz, rng.Range(0f, 360f), 1f);
                    }
                    else if (b.Marsh > 0.5f)
                    {
                        if (roll < 0.07f) node("plant_fiber", px, pz, rng.Range(0f, 360f), rng.Range(0.9f, 1.3f));
                    }
                    else if (roll < 0.03f) node("plant_fiber", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                    else if (roll < 0.045f) node("bush_berry", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                    else if (roll < 0.06f) node(b.Mountain > 0.3f && roll2 < 0.3f ? "ore_iron" : "rock_small", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.5f));
                    else if (roll < 0.064f) node("branch_pile", px, pz, rng.Range(0f, 360f), 1f);
                }
            // Abandoned cars on the roads, and a few infected wandering the countryside.
            foreach (Seg s in SegsInChunk(c))
            {
                if (s.Rail || rng.NextFloat() > 0.04f) continue;
                Vec3 p = Vec3.Lerp(s.A, s.B, rng.NextFloat());
                if (OverWater(p.X, p.Z, 20f)) continue;
                Vec3 d = (s.B - s.A).Normalized;
                var side = new Vec3(-d.Z, 0, d.X) * (rng.NextFloat() < 0.5f ? -1f : 1f) * (s.Half - 1.5f);
                node("wreck_car", p.X + side.X, p.Z + side.Z, (float)(Math.Atan2(d.X, d.Z) * 180.0 / Math.PI) + rng.Range(-25f, 25f), 1f);
            }
            int spawns = rng.Range(0, 3);
            for (int i = 0; i < spawns; i++)
            {
                float x = c.MinX + rng.Range(30f, 480f), z = c.MinZ + rng.Range(30f, 480f);
                if (!OverWater(x, z, 10f) && layout.HeightAt(x, z) > Sea + 1f) c.ZombieSpawns.Add(layout.Ground(x, z));
            }
        }

        private bool NearVillage(float x, float z)
        {
            foreach (Village v in _villages)
                if (Vec3.DistanceXZ(new Vec3(x, 0, z), v.Center) < v.Radius + 20f) return true;
            return false;
        }

        private IEnumerable<Seg> SegsInChunk(ChunkContent c)
        {
            var seen = new HashSet<int>();
            for (float z = c.MinZ; z < c.MinZ + WorldLayout.ChunkSize; z += SegCell)
                for (float x = c.MinX; x < c.MinX + WorldLayout.ChunkSize; x += SegCell)
                {
                    long k = ((long)(int)Math.Floor(x / SegCell) << 32) ^ (uint)(int)Math.Floor(z / SegCell);
                    if (!_segIndex.TryGetValue(k, out List<int> list)) continue;
                    foreach (int id in list)
                    {
                        if (!seen.Add(id)) continue;
                        Seg s = _segs[id];
                        Vec3 mid = Vec3.Lerp(s.A, s.B, 0.5f);
                        if (mid.X >= c.MinX && mid.X < c.MinX + WorldLayout.ChunkSize && mid.Z >= c.MinZ && mid.Z < c.MinZ + WorldLayout.ChunkSize) yield return s;
                    }
                }
        }
    }
}
