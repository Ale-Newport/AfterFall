using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Kesselmouth. Its street plan is two smoothly warped fields U and V: streets are where U or V
    /// is a whole number, so blocks are the cells between them. The warp bends streets like an old
    /// city's, and block size grows from ~75 m in the centre to ~125 m in the suburbs. Because the
    /// fields are defined everywhere, every chunk builds the blocks whose centres it holds and they
    /// always meet their neighbours. Each block is towers, mansion blocks, terraces, semi-detached
    /// houses, docks or a park, by density and district; a few buildings per block can be entered.
    /// </summary>
    public sealed partial class BasinWorld
    {
        private const float CityAngle = 0.21f; // radians: the grid's general bearing

        /// <summary>How built-up the city is at a point (0 countryside .. 1 centre).</summary>
        public float CityDensity(float x, float z)
        {
            float mask = CityMask(x, z);
            if (mask <= 0f) return 0f;
            float d = Vec3.DistanceXZ(new Vec3(x, 0, z), CityCenter) / CityRadius;
            float core = 1f - S(0f, 1f, d);
            return mask * Math.Max(0f, Math.Min(1f, 0.12f + core * 0.95f + Noise.Fbm(_seed + 501, x * 0.0004f, z * 0.0004f, 3) * 0.25f));
        }

        /// <summary>
        /// The street grid's spacing. It is constant: a spacing that varied across the city would be
        /// multiplied by the distance from the origin and squeeze the blocks; dense blocks are split instead.
        /// </summary>
        private const float GridSpacing = 104f;

        private void Fields(float x, float z, out float u, out float v)
        {
            float c = (float)Math.Cos(CityAngle), s = (float)Math.Sin(CityAngle);
            float rx = x * c - z * s, rz = x * s + z * c;
            float wu = Noise.Fbm(_seed + 511, x * 0.0007f, z * 0.0007f, 3) * 140f;
            float wv = Noise.Fbm(_seed + 512, x * 0.0007f + 5.2f, z * 0.0007f, 3) * 140f;
            u = (rx + wu) / GridSpacing;
            v = (rz + wv) / GridSpacing;
        }

        /// <summary>The world point where the fields take (u, v): a linear estimate from <paramref name="guess"/>, then Newton steps.</summary>
        private Vec3 Solve(float u, float v, Vec3 guess) => Solve(u, v, guess, out _);

        private Vec3 Solve(float u, float v, Vec3 guess, out bool ok)
        {
            float x = guess.X, z = guess.Z;
            ok = false;
            for (int it = 0; it < 10; it++)
            {
                Fields(x, z, out float fu, out float fv);
                float eu = fu - u, ev = fv - v;
                if (Math.Abs(eu) < 2e-4f && Math.Abs(ev) < 2e-4f)
                {
                    ok = true;
                    break;
                }
                Fields(x + 1f, z, out float ux, out float vx);
                Fields(x, z + 1f, out float uz, out float vz);
                float a = ux - fu, b = uz - fu, c = vx - fv, d = vz - fv;
                float det = a * d - b * c;
                if (Math.Abs(det) < 1e-9f) break;
                x -= (d * eu - b * ev) / det;
                z -= (-c * eu + a * ev) / det;
            }
            return new Vec3(x, 0, z);
        }

        private void PopulateCity(WorldLayout layout, ChunkContent c, DeterministicRandom rng, ref int nodeIndex, ref int containerIndex)
        {
            float x0 = c.MinX, z0 = c.MinZ, size = WorldLayout.ChunkSize;
            // Is any of the chunk city at all?
            float best = 0f;
            for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 4; i++)
                    best = Math.Max(best, CityDensity(x0 + i * size / 4f, z0 + j * size / 4f));
            if (best < 0.06f) return;

            // Block indices that can have their centre in this chunk.
            float umin = float.MaxValue, umax = float.MinValue, vmin = float.MaxValue, vmax = float.MinValue;
            for (int j = -1; j <= 5; j++)
                for (int i = -1; i <= 5; i++)
                {
                    Fields(x0 + i * size / 4f, z0 + j * size / 4f, out float u, out float v);
                    umin = Math.Min(umin, u); umax = Math.Max(umax, u); vmin = Math.Min(vmin, v); vmax = Math.Max(vmax, v);
                }
            int ni = nodeIndex, ci = containerIndex;
            for (int bj = (int)Math.Floor(vmin) - 1; bj <= (int)Math.Ceiling(vmax); bj++)
                for (int bi = (int)Math.Floor(umin) - 1; bi <= (int)Math.Ceiling(umax); bi++)
                {
                    Vec3 guess = new Vec3(x0 + size * 0.5f, 0, z0 + size * 0.5f);
                    Vec3 centre = Solve(bi + 0.5f, bj + 0.5f, guess, out bool solved);
                    if (!solved || centre.X < x0 || centre.X >= x0 + size || centre.Z < z0 || centre.Z >= z0 + size) continue;
                    Block(layout, c, bi, bj, centre, ref ni, ref ci);
                }
            nodeIndex = ni;
            containerIndex = ci;
        }

        private void Block(WorldLayout layout, ChunkContent c, int bi, int bj, Vec3 centre, ref int ni, ref int ci)
        {
            float density = CityDensity(centre.X, centre.Z);
            if (density < 0.08f) return;
            var rng = DeterministicRandom.Derive(_seed, $"block:{bi}:{bj}");
            Vec3 p00 = Solve(bi, bj, centre), p10 = Solve(bi + 1, bj, centre), p11 = Solve(bi + 1, bj + 1, centre), p01 = Solve(bi, bj + 1, centre);
            bool arterialU = Mod(bi, 6) == 0, arterialV = Mod(bj, 6) == 0;

            // Streets: each block lays its south and west edges (the neighbours lay the others).
            Street(layout, c, p00, p10, bi, bj, true, arterialV);
            Street(layout, c, p00, p01, bi, bj, false, arterialU);

            bool wet = OverWater(centre.X, centre.Z, 30f) || OverWater(p00.X, p00.Z, 6f) || OverWater(p10.X, p10.Z, 6f) || OverWater(p11.X, p11.Z, 6f) || OverWater(p01.X, p01.Z, 6f);
            if (wet) return;
            if (NearRoadExcept(centre.X, centre.Z, 18f)) return; // a ring road or avenue cuts through: leave it open

            // Dense blocks are halved by a lane (alternating direction), like the small blocks of an old centre.
            if (density > 0.6f)
            {
                bool splitU = Mod(bi + bj, 2) == 0;
                Vec3 m0 = splitU ? Solve(bi + 0.5f, bj, centre) : Solve(bi, bj + 0.5f, centre);
                Vec3 m1 = splitU ? Solve(bi + 0.5f, bj + 1, centre) : Solve(bi + 1, bj + 0.5f, centre);
                var lane = new RoadPath { Width = 7f, Paved = true };
                lane.Points.Add(G(m0.X, m0.Z));
                lane.Points.Add(G(centre.X, centre.Z));
                lane.Points.Add(G(m1.X, m1.Z));
                c.Streets.Add(lane);
                Vec3[] a = splitU ? new[] { p00, m0, m1, p01 } : new[] { p00, p10, m1, m0 };
                Vec3[] b = splitU ? new[] { m0, p10, p11, m1 } : new[] { m0, m1, p11, p01 };
                Fill(layout, c, rng, a, density, arterialU, arterialV, ref ni, ref ci);
                Fill(layout, c, rng, b, density, arterialU, arterialV, ref ni, ref ci);
            }
            else Fill(layout, c, rng, new[] { p00, p10, p11, p01 }, density, arterialU, arterialV, ref ni, ref ci);

            if (rng.NextFloat() < 0.025f)
            {
                Vec3 p = Vec3.Lerp(p00, p01, 0.5f) + (p10 - p00).Normalized * 2.5f;
                string def = rng.NextFloat() < 0.6f ? "sedan" : "pickup";
                c.Vehicles.Add(new VehicleSeed
                {
                    Key = $"vehicle:city:{bi}:{bj}", DefId = def, Position = G(p.X, p.Z), Yaw = EdgeYaw(p00, p01), Paint = rng.Range(0, 4), Fuel = rng.Range(2f, 25f),
                    Engine = rng.Range(40f, 90f), Body = rng.Range(35f, 80f), Tyres = rng.Range(40f, 90f), Battery = rng.Range(20f, 80f), Locked = rng.NextFloat() < 0.5f, LockId = $"Kesselmouth {def} {bi}-{bj}"
                });
            }
        }

        /// <summary>Fills one block quad (corners in order) with buildings, a park, cars and infected.</summary>
        private void Fill(WorldLayout layout, ChunkContent c, DeterministicRandom rng, Vec3[] q, float density, bool arterialU, bool arterialV, ref int ni, ref int ci)
        {
            Vec3 p00 = q[0], p10 = q[1], p01 = q[3];
            Vec3 centre = Inside(q, 0.5f, 0.5f);
            float dCbd = Vec3.DistanceXZ(centre, new Vec3(40000, 0, 30600));
            float heath = S(2600f, 1800f, Vec3.DistanceXZ(centre, new Vec3(44500, 0, 41000)));
            float parkNoise = Noise.Fbm(_seed + 520, centre.X * 0.0011f, centre.Z * 0.0011f, 3);
            bool park = heath > 0.5f || parkNoise > 0.42f || (density < 0.5f && rng.NextFloat() < 0.04f);
            bool docks = centre.X > 48500f && NearRiver(centre.X, centre.Z, 1400f);
            string kind = park ? "park" : dCbd < 2600f && density > 0.6f ? "towers" : docks ? "docks" : density > 0.66f ? "mansions" : density > 0.36f ? "terraces" : "semis";

            float streetHalf = (arterialU || arterialV ? 6f : 4f) + 2.2f; // the street and its pavement
            if (kind == "park")
            {
                // Grass, paths and trees (some of them felled for wood), and the odd wanderer.
                int trees = rng.Range(10, 26);
                for (int i = 0; i < trees; i++)
                {
                    Vec3 p = Inside(q, rng.NextFloat() * 0.8f + 0.1f, rng.NextFloat() * 0.8f + 0.1f);
                    c.Nodes.Add(new NodeSeed { Key = $"c{c.X}_{c.Z}:n{ni++}", DefId = rng.NextFloat() < 0.55f ? "tree_birch" : "tree_pine", Position = G(p.X, p.Z), Yaw = rng.Range(0f, 360f), Scale = rng.Range(0.9f, 1.4f) });
                }
                if (rng.NextFloat() < 0.5f) c.Nodes.Add(new NodeSeed { Key = $"c{c.X}_{c.Z}:n{ni++}", DefId = "bush_berry", Position = G(centre.X + 4f, centre.Z), Yaw = 0f, Scale = 1f });
                c.Props.Add(new PropPlacement { Kind = "city_park", Position = G(centre.X, centre.Z), Size = new Vec3(Vec3.DistanceXZ(p00, p10), 0f, Vec3.DistanceXZ(p00, p01)), Yaw = EdgeYaw(p00, p10) });
                if (rng.NextFloat() < 0.25f) c.ZombieSpawns.Add(G(centre.X, centre.Z));
                return;
            }

            int enterable = 0, maxEnterable = kind == "towers" ? 1 : 2;
            // How deep a row can be: rows on opposite sides must not meet in the middle.
            float across = Math.Min(Vec3.DistanceXZ(Vec3.Lerp(q[0], q[1], 0.5f), Vec3.Lerp(q[3], q[2], 0.5f)), Vec3.DistanceXZ(Vec3.Lerp(q[0], q[3], 0.5f), Vec3.Lerp(q[1], q[2], 0.5f)));
            float maxDepth = (across - 2f * streetHalf) * 0.5f - 1.5f;
            for (int e = 0; e < 4; e++)
            {
                Vec3 a = q[e], b = q[(e + 1) % 4];
                float len = Vec3.DistanceXZ(a, b);
                Vec3 dir = (b - a).Normalized;
                // Inward normal (towards the block's centre).
                var nrm = new Vec3(-dir.Z, 0, dir.X);
                if ((centre.X - a.X) * nrm.X + (centre.Z - a.Z) * nrm.Z < 0f) nrm = nrm * -1f;
                float faceYaw = (float)(Math.Atan2(-nrm.X, -nrm.Z) * 180.0 / Math.PI); // fronts face the street
                bool shops = (e == 0 && arterialV) || (e == 3 && arterialU) || (kind == "mansions" && rng.NextFloat() < 0.4f);
                float depth, unit, height;
                string blockKind;
                switch (kind)
                {
                    case "towers":
                        blockKind = "city_tower"; depth = rng.Range(22f, 34f); unit = rng.Range(24f, 38f); height = rng.Range(40f, 70f) + (1f - S(0f, 2600f, dCbd)) * rng.Range(40f, 150f); break;
                    case "docks":
                        blockKind = "city_shed"; depth = rng.Range(18f, 28f); unit = rng.Range(26f, 48f); height = rng.Range(8f, 13f); break;
                    case "mansions":
                        blockKind = "city_midrise"; depth = rng.Range(12f, 16f); unit = rng.Range(18f, 34f); height = 3.2f * rng.Range(4, 8) + 1f; break;
                    case "terraces":
                        blockKind = "city_terrace"; depth = rng.Range(8.5f, 10.5f); unit = 5.4f * rng.Range(4, 9); height = 3.1f * rng.Range(2, 4) + 0.6f; break;
                    default:
                        blockKind = "city_semi"; depth = 8.5f; unit = 11f; height = 6.6f; break;
                }
                float setback = streetHalf + (kind == "semis" ? 4.5f : 0.4f);
                depth = Math.Min(depth, maxDepth - (setback - streetHalf));
                if (depth < 6f) continue;
                // Rows start and end clear of the rows along the neighbouring edges (the corners stay open).
                float start = setback + depth + 1.5f, end = len - setback - depth - 1.5f;
                if (kind == "towers") { start += 4f; end -= 4f; }
                float gap = kind == "semis" ? 7f : kind == "towers" ? 10f : kind == "docks" ? 6f : 0.4f;
                for (float s = start; s + unit * 0.5f <= end + 0.01f; s += unit + gap)
                {
                    float w = Math.Min(unit, end - s + unit * 0.5f);
                    if (w < 5f) break;
                    float along = s + w * 0.5f - unit * 0.5f;
                    Vec3 at = a + dir * along + nrm * (setback + depth * 0.5f);
                    // Now and then a building that can be entered (and searched) instead of a facade.
                    if (enterable < maxEnterable && w >= 8f && rng.NextFloat() < (kind == "docks" ? 0.12f : kind == "towers" ? 0.25f : 0.06f))
                    {
                        string k = kind == "docks" ? (rng.NextFloat() < 0.6f ? "warehouse" : "garage") : shops || kind == "towers" ? (rng.NextFloat() < 0.8f ? "shop" : "clinic") : "house";
                        Vec3 sz = SizeOf(k);
                        Vec3 pos = a + dir * along + nrm * (setback + sz.Z * 0.5f);
                        if (!NearRoad(pos.X, pos.Z, Math.Max(sz.X, sz.Z) * 0.4f))
                        {
                            c.Props.Add(new PropPlacement { Kind = k, Position = G(pos.X, pos.Z), Yaw = faceYaw, Size = sz, PoiId = "kesselmouth", Variant = rng.Range(0, 4) });
                            foreach (WorldGenerator.ContainerSpec spec in WorldGenerator.ContainersFor(k, rng))
                            {
                                Vec3 off = WorldGenerator.Rotate(new Vec3(spec.X, 0, spec.Z), faceYaw);
                                c.Containers.Add(new ContainerSeed { Key = $"c{c.X}_{c.Z}:k{ci++}", DefId = spec.Def, LootTable = spec.Table, Position = G(pos.X + off.X, pos.Z + off.Z), Yaw = faceYaw + spec.Yaw, PoiId = "kesselmouth" });
                            }
                            enterable++;
                            continue;
                        }
                    }
                    if (NearRoad(at.X, at.Z, Math.Max(w, depth) * 0.45f)) continue;
                    int variant = rng.Range(0, 8) | (shops ? 8 : 0);
                    float h = kind == "terraces" || kind == "semis" ? height : height * rng.Range(0.85f, 1.15f);
                    c.Blocks.Add(new PropPlacement { Kind = blockKind, Position = G(at.X, at.Z), Yaw = faceYaw, Size = new Vec3(w, h, depth), Variant = variant, PoiId = "kesselmouth" });
                }
            }
            // Landmarks: a church in some residential blocks.
            if ((kind == "terraces" || kind == "mansions") && rng.NextFloat() < 0.05f)
                c.Blocks.Add(new PropPlacement { Kind = "city_church", Position = G(centre.X, centre.Z), Yaw = EdgeYaw(p00, p10), Size = new Vec3(14f, 13f, 30f), Variant = rng.Range(0, 4), PoiId = "kesselmouth" });
            if (rng.NextFloat() < (kind == "towers" ? 0.5f : 0.3f)) c.ZombieSpawns.Add(G(centre.X, centre.Z));
            if (rng.NextFloat() < 0.5f)
            {
                Vec3 p = Vec3.Lerp(p00, p10, rng.Range(0.2f, 0.8f));
                c.Nodes.Add(new NodeSeed { Key = $"c{c.X}_{c.Z}:n{ni++}", DefId = rng.NextFloat() < 0.85f ? "wreck_car" : "scrap_pile", Position = G(p.X, p.Z), Yaw = EdgeYaw(p00, p10) + rng.Range(-20f, 20f), Scale = 1f });
            }
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>
        /// Ground in the city: its terrain has no fine detail, so the coarse height is the ground (within
        /// centimetres) and distant chunks (the skyline) are made without full-detail height tiles.
        /// </summary>
        private Vec3 G(float x, float z) => new Vec3(x, CoarseHeight(x, z), z);

        private static float EdgeYaw(Vec3 a, Vec3 b) => (float)(Math.Atan2(b.X - a.X, b.Z - a.Z) * 180.0 / Math.PI);

        /// <summary>A point inside the block's quad at (s, t) in 0..1.</summary>
        private static Vec3 Inside(Vec3[] q, float s, float t) => Vec3.Lerp(Vec3.Lerp(q[0], q[1], s), Vec3.Lerp(q[3], q[2], s), t);

        private void Street(WorldLayout layout, ChunkContent c, Vec3 a, Vec3 b, int bi, int bj, bool alongU, bool arterial)
        {
            Vec3 mid = Vec3.Lerp(a, b, 0.5f);
            if (CityDensity(mid.X, mid.Z) < 0.1f) return;
            if (OverWater(a.X, a.Z, 2f) || OverWater(b.X, b.Z, 2f) || OverWater(mid.X, mid.Z, 2f)) return;
            var r = new RoadPath { Width = arterial ? 12f : 8f, Paved = true };
            // Three points along the curved edge (the middle one solved on the fields).
            Vec3 m = alongU ? Solve(bi + 0.5f, bj, mid) : Solve(bi, bj + 0.5f, mid);
            r.Points.Add(G(a.X, a.Z));
            r.Points.Add(G(m.X, m.Z));
            r.Points.Add(G(b.X, b.Z));
            c.Streets.Add(r);
        }

        private bool NearRiver(float x, float z, float within)
        {
            List<int> segs = Lookup(_valleyIndex, ValleyCell, x, z);
            if (segs == null) return false;
            foreach (int id in segs)
                if (WorldLayout.DistanceToSegmentXZ(x, z, _riverSegs[id].A, _riverSegs[id].B, out _) < within) return true;
            return false;
        }

        /// <summary>Near a planned road (ring roads, avenues, motorways), not counting the block's own streets.</summary>
        private bool NearRoadExcept(float x, float z, float clearance) => NearRoad(x, z, clearance);
    }
}
