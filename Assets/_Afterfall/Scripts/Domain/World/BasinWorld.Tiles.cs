using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    public sealed partial class BasinWorld
    {
        private const int CoarseStep = 4;

        /// <summary>
        /// Full-detail heights of one 512 m tile: the coarse ground on a 4 m grid, upsampled, with fine
        /// detail by biome (rocky in the mountains, none in the city), then village pads, roads, river
        /// channels and lake bowls pressed in. Inside Greater Millbrook its own heights are used as they are.
        /// </summary>
        public void BuildTile(int tx, int tz, int res, float cell, float[] heights)
        {
            float ox = tx * (res - 1) * cell, oz = tz * (res - 1) * cell;
            float size = (res - 1) * cell;
            int cres = (int)(size / CoarseStep) + 1;
            var coarse = new float[cres * cres];
            var rough = new float[cres * cres];
            for (int j = 0; j < cres; j++)
                for (int i = 0; i < cres; i++)
                {
                    float x = ox + i * CoarseStep, z = oz + j * CoarseStep;
                    coarse[j * cres + i] = CoarseHeight(x, z);
                    rough[j * cres + i] = Roughness(x, z);
                }

            for (int zi = 0; zi < res; zi++)
            {
                float z = oz + zi * cell;
                float fz = zi * cell / CoarseStep;
                int cz = Math.Min(cres - 2, (int)fz);
                float tzf = fz - cz;
                for (int xi = 0; xi < res; xi++)
                {
                    float x = ox + xi * cell;
                    if (x <= BlockSize && z <= BlockSize)
                    {
                        heights[zi * res + xi] = _region.Height.Sample(x, z);
                        continue;
                    }
                    float fx = xi * cell / CoarseStep;
                    int cx = Math.Min(cres - 2, (int)fx);
                    float txf = fx - cx;
                    int o = cz * cres + cx;
                    float h0 = coarse[o] + (coarse[o + 1] - coarse[o]) * txf, h1 = coarse[o + cres] + (coarse[o + cres + 1] - coarse[o + cres]) * txf;
                    float r0 = rough[o] + (rough[o + 1] - rough[o]) * txf, r1 = rough[o + cres] + (rough[o + cres + 1] - rough[o + cres]) * txf;
                    float h = h0 + (h1 - h0) * tzf, r = r0 + (r1 - r0) * tzf;
                    if (r > 0.01f) h += (Noise.Fbm(_seed + 201, x * 0.045f, z * 0.045f, 2) * 1.2f + Noise.Fbm(_seed + 202, x * 0.011f, z * 0.011f, 2) * 2.5f) * r;
                    heights[zi * res + xi] = h;
                }
            }

            StampPads(ox, oz, res, cell, heights);
            float[] roadDone = StampRoads(ox, oz, res, cell, heights);
            StampRivers(ox, oz, res, cell, heights, roadDone);
            StampLakes(ox, oz, res, cell, heights, roadDone);
            for (int i = 0; i < heights.Length; i++) heights[i] = Math.Max(0f, Math.Min(MaxHeight - 1f, heights[i]));
        }

        /// <summary>How rough the ground's fine detail is: rocky mountains and badlands, smooth city, gentle elsewhere.</summary>
        public float Roughness(float x, float z)
        {
            if (x < BlockSize + BlendWidth && z < BlockSize + BlendWidth) return 0.2f * S(BlockSize, BlockSize + BlendWidth, Math.Max(x, z));
            float city = CityMask(x, z);
            float shore = x - CoastX(z);
            float r = 0.35f + 2.2f * S(80f, 260f, Terrain(x, z)) + 1.2f * Badlands(x, z);
            r *= 1f - city;
            r *= S(0f, -600f, shore);
            return r;
        }

        private delegate void CellAction(int index, float x, float z);

        private static void ForCells(float ox, float oz, int res, float cell, float x0, float z0, float x1, float z1, CellAction each)
        {
            int ax = Math.Max(0, (int)Math.Floor((x0 - ox) / cell)), bx = Math.Min(res - 1, (int)Math.Ceiling((x1 - ox) / cell));
            int az = Math.Max(0, (int)Math.Floor((z0 - oz) / cell)), bz = Math.Min(res - 1, (int)Math.Ceiling((z1 - oz) / cell));
            for (int zi = az; zi <= bz; zi++)
                for (int xi = ax; xi <= bx; xi++)
                    each(zi * res + xi, ox + xi * cell, oz + zi * cell);
        }

        private static bool InBlock(float x, float z) => x <= BlockSize && z <= BlockSize;

        private void StampPads(float ox, float oz, int res, float cell, float[] h)
        {
            float size = (res - 1) * cell;
            foreach (Village v in _villages)
            {
                float reach = v.Pad * 1.6f;
                if (v.Center.X + reach < ox || v.Center.X - reach > ox + size || v.Center.Z + reach < oz || v.Center.Z - reach > oz + size) continue;
                float target = PadHeight(v);
                ForCells(ox, oz, res, cell, v.Center.X - reach, v.Center.Z - reach, v.Center.X + reach, v.Center.Z + reach, (i, x, z) =>
                {
                    float w = S(reach, v.Pad * 0.85f, Vec3.DistanceXZ(new Vec3(x, 0, z), v.Center));
                    if (w > 0f) h[i] += (target - h[i]) * w;
                });
            }
        }

        /// <summary>Presses every road near the tile into the ground (heights averaged where roads meet); returns how firmly roads hold each cell.</summary>
        private float[] StampRoads(float ox, float oz, int res, float cell, float[] h)
        {
            float size = (res - 1) * cell;
            var done = new float[res * res];
            var sumW = new float[res * res];
            var sumWH = new float[res * res];
            var seen = new HashSet<int>();
            var own = new Dictionary<int, (float W, float H)>();
            var byRoad = new Dictionary<int, List<int>>();
            int x0 = (int)Math.Floor((ox - 30f) / SegCell), x1 = (int)Math.Floor((ox + size + 30f) / SegCell);
            int z0 = (int)Math.Floor((oz - 30f) / SegCell), z1 = (int)Math.Floor((oz + size + 30f) / SegCell);
            var segs = new List<int>();
            for (int zc = z0; zc <= z1; zc++)
                for (int xc = x0; xc <= x1; xc++)
                    if (_segIndex.TryGetValue(((long)xc << 32) ^ (uint)zc, out List<int> list))
                        foreach (int id in list)
                            if (seen.Add(id)) segs.Add(id);
            if (segs.Count == 0) return done;
            // Each road's own surface (nearest length wins), then all roads averaged by weight⁴.
            foreach (int id in segs)
            {
                Seg s = _segs[id];
                float reach = s.Half + 9f;
                ForCells(ox, oz, res, cell, Math.Min(s.A.X, s.B.X) - reach, Math.Min(s.A.Z, s.B.Z) - reach, Math.Max(s.A.X, s.B.X) + reach, Math.Max(s.A.Z, s.B.Z) + reach, (i, x, z) =>
                {
                    if (InBlock(x, z)) return;
                    float d = WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out float t);
                    float w = S(s.Half + 9f, s.Half + 1f, d);
                    if (w <= 0f) return;
                    float k = w * w;
                    k *= k;
                    // One contribution per cell per segment; overlapping lengths of one road add consistent heights.
                    sumW[i] += k;
                    sumWH[i] += k * (s.HA + (s.HB - s.HA) * t);
                    if (w > done[i]) done[i] = w;
                });
            }
            for (int i = 0; i < h.Length; i++)
                if (done[i] > 0f) h[i] += (sumWH[i] / sumW[i] - h[i]) * done[i];
            return done;
        }

        private void StampRivers(float ox, float oz, int res, float cell, float[] h, float[] roadDone)
        {
            float size = (res - 1) * cell;
            var seen = new HashSet<int>();
            for (float zc = oz; zc <= oz + size + ValleyCell; zc += ValleyCell)
                for (float xc = ox; xc <= ox + size + ValleyCell; xc += ValleyCell)
                {
                    List<int> segs = Lookup(_valleyIndex, ValleyCell, Math.Min(xc, ox + size), Math.Min(zc, oz + size));
                    if (segs == null) continue;
                    foreach (int id in segs)
                    {
                        if (!seen.Add(id)) continue;
                        RiverSeg s = _riverSegs[id];
                        float half = s.Half, reach = half + 40f;
                        float depth = 2.6f + Math.Min(6f, s.Half * 0.04f);
                        ForCells(ox, oz, res, cell, Math.Min(s.A.X, s.B.X) - reach, Math.Min(s.A.Z, s.B.Z) - reach, Math.Max(s.A.X, s.B.X) + reach, Math.Max(s.A.Z, s.B.Z) + reach, (i, x, z) =>
                        {
                            if (InBlock(x, z)) return;
                            float d = WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out float t);
                            if (d > reach || (d > half + 13f && roadDone[i] > 0.5f)) return;
                            float level = s.A.Y + (s.B.Y - s.A.Y) * t;
                            float bed = level - depth * S(half, half * 0.3f, d) - 0.4f;
                            float beyond = Math.Max(0f, d - half - 6f);
                            float bank = level + 0.6f + (d - half) * 0.35f + beyond * beyond * 0.05f;
                            float target = d < half ? bed : bank;
                            if (target < h[i]) h[i] = target;
                        });
                    }
                }
        }

        private void StampLakes(float ox, float oz, int res, float cell, float[] h, float[] roadDone)
        {
            float size = (res - 1) * cell;
            foreach (LakeInfo lake in _layout.Lakes)
            {
                if (lake.Center.X < BlockSize && lake.Center.Z < BlockSize) continue; // the valley's lake is in Millbrook's own heights
                float reach = lake.Radius + 120f;
                if (lake.Center.X + reach < ox || lake.Center.X - reach > ox + size || lake.Center.Z + reach < oz || lake.Center.Z - reach > oz + size) continue;
                ForCells(ox, oz, res, cell, lake.Center.X - reach, lake.Center.Z - reach, lake.Center.X + reach, lake.Center.Z + reach, (i, x, z) =>
                {
                    float dist = Vec3.DistanceXZ(new Vec3(x, 0, z), lake.Center);
                    if (dist > reach || (dist > lake.Radius + 8f && roadDone[i] > 0.5f)) return;
                    float d = dist / lake.Radius;
                    float beyond = Math.Max(0f, dist - lake.Radius - 30f);
                    float target = d < 1f ? lake.Level - 0.6f - Math.Min(9f, lake.Radius * 0.012f) * (1f - d * d) : lake.Level + 0.5f + (dist - lake.Radius) * 0.25f + beyond * beyond * 0.02f;
                    if (target < h[i]) h[i] = target;
                });
            }
        }
    }
}
