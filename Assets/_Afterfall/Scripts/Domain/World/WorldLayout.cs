using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>Engine-independent terrain heights. The presentation builds a Unity Terrain from it.</summary>
    public sealed class HeightField
    {
        public HeightField(int resolution, float size, float maxHeight)
        {
            Resolution = resolution;
            Size = size;
            MaxHeight = maxHeight;
            Heights = new float[resolution * resolution];
        }

        public int Resolution { get; }
        public float Size { get; }
        public float MaxHeight { get; }

        /// <summary>Row-major heights in metres: index = z * Resolution + x.</summary>
        public float[] Heights { get; }

        public float CellSize => Size / (Resolution - 1);

        public float Get(int x, int z)
        {
            x = x < 0 ? 0 : x >= Resolution ? Resolution - 1 : x;
            z = z < 0 ? 0 : z >= Resolution ? Resolution - 1 : z;
            return Heights[z * Resolution + x];
        }

        public void Set(int x, int z, float h) => Heights[z * Resolution + x] = h < 0 ? 0 : h > MaxHeight ? MaxHeight : h;

        /// <summary>Bilinear height at a world position (clamped to the map).</summary>
        public float Sample(float wx, float wz)
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
    }

    public sealed class RoadPath
    {
        public List<Vec3> Points { get; } = new List<Vec3>();
        public float Width { get; set; } = 7f;
        public bool Paved { get; set; } = true;
    }

    public sealed class NodeSeed
    {
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public float Scale { get; set; } = 1f;
    }

    public sealed class ContainerSeed
    {
        public string DefId { get; set; } = "";
        public string LootTable { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public string PoiId { get; set; } = "";
    }

    /// <summary>Generated static description of the playable region.</summary>
    public sealed class WorldLayout
    {
        public const float WorldSize = 512f;
        public const int HeightResolution = 513;
        public const float MaxTerrainHeight = 90f;

        public HeightField Height { get; set; }
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

        public float HeightAt(float x, float z) => Height.Sample(x, z);
        public Vec3 Ground(float x, float z) => new Vec3(x, Height.Sample(x, z), z);
        public bool IsWater(float x, float z) => Height.Sample(x, z) < WaterLevel + 0.15f;

        public bool IsInsideMap(float x, float z, float margin = 0f) =>
            x >= margin && z >= margin && x <= WorldSize - margin && z <= WorldSize - margin;

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
