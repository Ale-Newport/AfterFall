using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// World version 2: Greater Millbrook, a 2 km × 2 km region. The original valley keeps its
    /// place in the south-west corner (its landmarks, roads and loot are placed exactly as in
    /// version 1) and opens east onto the Kessel river: the riverside town of Ashford with its
    /// bridge, the Eastgate industrial district, two farms on the plains, Pinecrest village up a
    /// mountain road, a forest lodge, the Kessel reservoir and the remote Northgate research
    /// station. Towns are street grids with parcels; terrain, roads, rivers and lakes are stamped
    /// into one height field with bounded loops so generation stays fast. Fully deterministic.
    /// </summary>
    public static partial class WorldGenerator
    {
        public const float RegionSize = 2048f;
        public const float RegionMaxHeight = 220f;

        /// <summary>The layout for a world version (1 = valley, 2 = region).</summary>
        public static WorldLayout Generate(int seed, int version) => version >= 2 ? GenerateRegion(seed) : Generate(seed);

        /// <summary>Where the region's apartment blocks stand (Y holds the yaw), each inside a town block.</summary>
        private static readonly Vec3 AshfordApartments = new Vec3(935f, 0f, 540f);
        private static readonly Vec3 EastgateApartments = new Vec3(1724f, 180f, 650f);

        private static Parcel Reserved(float x, float z, float w, float d) =>
            new Parcel { Poi = "", Kind = "reserved", X = x, Z = z, Size = new Vec3(w, 0f, d) };

        private struct Parcel
        {
            public string Poi, Kind;
            public float X, Z, Yaw;
            public Vec3 Size;
            public int Variant;
        }

        public static WorldLayout GenerateRegion(int seed)
        {
            var l = new WorldLayout
            {
                WaterLevel = 15.5f,
                LakeCenter = new Vec3(105f, 0f, 95f),
                LakeRadius = 50f,
                Version = 2,
                RegionName = "Greater Millbrook"
            };
            DefinePois(l);
            DefineRegionPois(l);
            DefineRoads(l);
            DefineRegionRoads(l);
            DefineRivers(l);
            DefineRail(l);
            // Lots kept clear in the towns: the apartment blocks and where vehicles are parked.
            var parcels = new List<Parcel>
            {
                Reserved(AshfordApartments.X, AshfordApartments.Z, 24f, 20f),
                Reserved(EastgateApartments.X, EastgateApartments.Z, 24f, 20f),
                Reserved(910f, 452f, 8f, 8f),
                Reserved(1712f, 628f, 8f, 8f)
            };
            var rng = DeterministicRandom.Derive(seed, "region:towns");
            PlanTown(l, rng, parcels, "ashford", 860f, 400f, 6, 5, 48f, 40f, "residential");
            PlanTown(l, rng, parcels, "eastgate", 1500f, 470f, 5, 4, 64f, 52f, "industrial");
            AddBridges(l);
            l.Height = BuildRegionTerrain(seed, l);
            ProjectRoadsToGround(l);
            SetBridgeDecks(l);
            SetRiverLevels(l);
            l.IndexWater();

            // The valley, exactly as in version 1.
            PlaceStartCamp(l);
            PlaceSettlement(l);
            PlaceGasStation(l);
            PlaceWorkshop(l);
            PlaceSubstation(l);
            PlaceQuarry(l, seed);
            PlaceFarm(l, seed);
            PlaceRoadside(l, seed);
            PlaceSurvivors(l);

            // The region.
            PlaceParcels(l, parcels, seed);
            PlacePinecrest(l, seed);
            PlaceRegionFarms(l, seed);
            PlaceResearch(l, seed);
            PlaceLodge(l);
            PlaceRail(l);
            PlaceRegionVegetation(l, seed);
            PlaceRegionSpawns(l, seed);

            PlacePlan(l, BuildingPlans.MillbrookApartments(), "millbrook", 206f, 322f, 180f);
            PlacePlan(l, BuildingPlans.Get(BuildingPlans.AshfordApartmentsId), "ashford", AshfordApartments.X, AshfordApartments.Z, AshfordApartments.Y);
            PlacePlan(l, BuildingPlans.Get(BuildingPlans.EastgateApartmentsId), "eastgate", EastgateApartments.X, EastgateApartments.Z, EastgateApartments.Y);
            PlaceVehicles(l);
            PlaceRegionVehicles(l);
            return l;
        }

        // ------------------------------------------------------------------ landmarks and roads

        private static void DefineRegionPois(WorldLayout l)
        {
            l.Pois.Add(new PoiInfo { Id = "ashford", Name = "Ashford", Kind = "town", Center = new Vec3(1000, 0, 500), Radius = 160, Risk = "Medium", Description = "A riverside town: homes, shops, a clinic, apartments and the bridge over the Kessel." });
            l.Pois.Add(new PoiInfo { Id = "eastgate", Name = "Eastgate District", Kind = "industrial", Center = new Vec3(1660, 0, 570), Radius = 175, Risk = "High", Description = "Warehouses, garages and workshops along the old freight line. Parts, tools — and many infected." });
            l.Pois.Add(new PoiInfo { Id = "pinecrest", Name = "Pinecrest", Kind = "village", Center = new Vec3(520, 0, 1620), Radius = 70, Risk = "Medium", Description = "A mountain village at the end of a long gravel road. Cabins, a barn and a store." });
            l.Pois.Add(new PoiInfo { Id = "reservoir", Name = "Kessel Reservoir", Kind = "lake", Center = new Vec3(1230, 0, 1420), Radius = 125, Risk = "Low", Description = "A reservoir in the hills that feeds the Kessel." });
            l.Pois.Add(new PoiInfo { Id = "research", Name = "Northgate Research Station", Kind = "research", Center = new Vec3(1820, 0, 1800), Radius = 70, Risk = "High", Description = "A fenced station in the far north-east. Advanced equipment and danger." });
            l.Pois.Add(new PoiInfo { Id = "millerfarm", Name = "Miller Farm", Kind = "farm", Center = new Vec3(1450, 0, 200), Radius = 50, Risk = "Low", Description = "A farmstead on the southern plains." });
            l.Pois.Add(new PoiInfo { Id = "coldwater", Name = "Coldwater Farm", Kind = "farm", Center = new Vec3(1780, 0, 270), Radius = 50, Risk = "Low", Description = "A dairy farm east of Eastgate." });
            l.Pois.Add(new PoiInfo { Id = "lodge", Name = "Thornwood Lodge", Kind = "lodge", Center = new Vec3(705, 0, 895), Radius = 30, Risk = "Low", Description = "A hunting lodge deep in the Thornwood." });
        }

        private static void DefineRegionRoads(WorldLayout l)
        {
            // The valley's main road leaves east as the highway: Ashford, Eastgate and on.
            var highway = new RoadPath { Width = 9f, Paved = true };
            AddPoints(highway, 512, 352, 600, 368, 700, 392, 800, 420, 900, 446, 960, 462, 1050, 478, 1140, 486, 1260, 505, 1400, 528, 1540, 548, 1660, 560, 1800, 566, 1920, 570, 2047, 572);
            l.Roads.Add(highway);
            var mountain = new RoadPath { Width = 6f, Paved = false };
            AddPoints(mountain, 318, 388, 360, 470, 430, 560, 470, 680, 440, 820, 500, 960, 470, 1100, 540, 1240, 500, 1380, 560, 1500, 520, 1620);
            l.Roads.Add(mountain);
            var lodge = new RoadPath { Width = 5f, Paved = false };
            AddPoints(lodge, 440, 820, 560, 860, 690, 893);
            l.Roads.Add(lodge);
            var river = new RoadPath { Width = 6f, Paved = false };
            AddPoints(river, 960, 560, 1000, 720, 1030, 900, 1060, 1080, 1100, 1250, 1130, 1360);
            l.Roads.Add(river);
            var research = new RoadPath { Width = 6f, Paved = false };
            AddPoints(research, 1130, 1360, 1080, 1430, 1090, 1520, 1200, 1600, 1400, 1650, 1600, 1720, 1820, 1790);
            l.Roads.Add(research);
            var miller = new RoadPath { Width = 5f, Paved = false };
            AddPoints(miller, 1400, 528, 1430, 360, 1450, 225);
            l.Roads.Add(miller);
            var cold = new RoadPath { Width = 5f, Paved = false };
            AddPoints(cold, 1870, 568, 1850, 420, 1790, 300);
            l.Roads.Add(cold);
        }

        private static void DefineRivers(WorldLayout l)
        {
            l.Lakes.Add(new LakeInfo { Name = "Kessel Reservoir", Center = new Vec3(1230, 0, 1420), Radius = 105f });
            var kessel = new RiverPath { Name = "Kessel", Width = 16f };
            float[] pts = { 1290, 2047, 1260, 1850, 1250, 1640, 1235, 1500, 1230, 1420, 1210, 1330, 1170, 1250, 1120, 1080, 1080, 900, 1050, 720, 1020, 560, 1005, 470, 980, 330, 1000, 180, 970, 0 };
            // The course is subdivided every ~30 m and pushed sideways by smooth noise so it meanders.
            for (int i = 0; i + 3 < pts.Length; i += 2)
            {
                var a = new Vec3(pts[i], 0, pts[i + 1]);
                var b = new Vec3(pts[i + 2], 0, pts[i + 3]);
                int steps = Math.Max(1, (int)(Vec3.DistanceXZ(a, b) / 30f));
                Vec3 dir = (b - a).Normalized;
                var side = new Vec3(-dir.Z, 0, dir.X);
                for (int s = 0; s < steps; s++)
                {
                    Vec3 p = Vec3.Lerp(a, b, s / (float)steps);
                    bool fixedPoint = s == 0 && (i == 0 || NearLake(l, p, 10f));
                    float offset = fixedPoint || NearLake(l, p, 0f) ? 0f : Noise.Fbm(9001, p.X * 0.004f, p.Z * 0.004f, 2) * 38f;
                    kessel.Points.Add(p + side * offset);
                }
            }
            kessel.Points.Add(new Vec3(pts[pts.Length - 2], 0, pts[pts.Length - 1]));
            l.Rivers.Add(kessel);
        }

        private static bool NearLake(WorldLayout l, Vec3 p, float margin)
        {
            foreach (LakeInfo lake in l.Lakes)
                if (Vec3.DistanceXZ(p, lake.Center) < lake.Radius + margin) return true;
            return false;
        }

        /// <summary>A bridge wherever a road crosses a river (as a prop spanning the channel).</summary>
        private static void AddBridges(WorldLayout l)
        {
            foreach (RoadPath road in l.Roads)
            {
                for (int i = 0; i + 1 < road.Points.Count; i++)
                {
                    Vec3 a = road.Points[i], b = road.Points[i + 1];
                    foreach (RiverPath river in l.Rivers)
                    {
                        for (int j = 0; j + 1 < river.Points.Count; j++)
                        {
                            if (!Intersect(a, b, river.Points[j], river.Points[j + 1], out float t)) continue;
                            Vec3 p = Vec3.Lerp(a, b, t);
                            float yaw = (float)(Math.Atan2(b.X - a.X, b.Z - a.Z) * 180.0 / Math.PI);
                            // Long enough to clear the cut banks (BridgeClear metres either side of the
                            // river's middle), measured along a road that may cross at an angle.
                            Vec3 rd = (b - a).Normalized, wd = (river.Points[j + 1] - river.Points[j]).Normalized;
                            float sin = Math.Max(0.5f, Math.Abs(rd.X * wd.Z - rd.Z * wd.X));
                            float length = (river.Width * 0.5f + BridgeClear) * 2f / sin + 2f;
                            l.Props.Add(new PropPlacement { Kind = "bridge", Position = p, Yaw = yaw, Size = new Vec3(road.Width + 2f, 1.2f, length), PoiId = "" });
                        }
                    }
                }
            }
        }

        /// <summary>How far beyond a river's edge its banks are cut away under a bridge.</summary>
        private const float BridgeClear = 13f;

        private static bool Intersect(Vec3 a, Vec3 b, Vec3 c, Vec3 d, out float t)
        {
            t = 0f;
            float r1x = b.X - a.X, r1z = b.Z - a.Z, r2x = d.X - c.X, r2z = d.Z - c.Z;
            float den = r1x * r2z - r1z * r2x;
            if (Math.Abs(den) < 1e-6f) return false;
            float u = ((c.X - a.X) * r2z - (c.Z - a.Z) * r2x) / den;
            float v = ((c.X - a.X) * r1z - (c.Z - a.Z) * r1x) / den;
            t = u;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>The region's ground before stamping: the valley's own formula in the south-west, rolling plains east, hills and mountains north.</summary>
        private static float RegionBase(int seed, float x, float z)
        {
            float valley = 22f + Noise.Fbm(seed, x * 0.006f, z * 0.006f, 4) * 6f + Noise.Fbm(seed + 17, x * 0.03f, z * 0.03f, 2) * 0.8f
                           + SmoothStep(400f, 505f, z) * (26f + Noise.Fbm(seed + 31, x * 0.02f, z * 0.02f, 3) * 14f)
                           + SmoothStep(40f, 0f, x) * 6f + SmoothStep(30f, 0f, z) * 6f;
            float plains = 28f + Noise.Fbm(seed + 5, x * 0.004f, z * 0.004f, 4) * 10f + Noise.Fbm(seed + 6, x * 0.02f, z * 0.02f, 2) * 1.5f;
            float ridged = 1f - Math.Abs(Noise.Fbm(seed + 7, x * 0.0032f, z * 0.0032f, 5));
            float mountains = SmoothStep(1050f, 1650f, z) * (55f + ridged * ridged * 95f);
            float hills = SmoothStep(380f, 520f, z) * SmoothStep(1150f, 700f, z) * (18f + Noise.Fbm(seed + 8, x * 0.01f, z * 0.01f, 3) * 10f);
            float edges = SmoothStep(40f, 0f, x) * 10f + SmoothStep(30f, 0f, z) * 8f + SmoothStep(1990f, 2048f, x) * 25f + SmoothStep(1990f, 2048f, z) * 25f;
            float outer = plains + mountains + hills + edges;
            float wv = (1f - SmoothStep(470f, 620f, x)) * (1f - SmoothStep(520f, 660f, z));
            return outer + (valley - outer) * wv;
        }

        private static HeightField BuildRegionTerrain(int seed, WorldLayout l)
        {
            int res = (int)RegionSize + 1;
            var hf = new HeightField(res, RegionSize, RegionMaxHeight);
            float cell = hf.CellSize;
            for (int zi = 0; zi < res; zi++)
                for (int xi = 0; xi < res; xi++)
                {
                    float x = xi * cell, z = zi * cell;
                    float h = RegionBase(seed, x, z);
                    float dl = Vec3.DistanceXZ(new Vec3(x, 0, z), l.LakeCenter) / l.LakeRadius;
                    if (dl < 1.25f) h -= 11f * SmoothStep(1.25f, 0.2f, dl);
                    hf.Heights[zi * res + xi] = h;
                }

            // Points of interest are levelled to their centre's height (the quarry a little lower).
            var poiHeights = new float[l.Pois.Count];
            for (int i = 0; i < l.Pois.Count; i++)
            {
                PoiInfo p = l.Pois[i];
                float target = RegionBase(seed, p.Center.X, p.Center.Z);
                if (p.Kind == "quarry") target -= 4f;
                poiHeights[i] = Math.Max(target, l.WaterLevel + 2.5f);
                if (p.Kind == "lake") continue;
                float reach = p.Radius * 1.6f;
                ForCells(hf, p.Center.X - reach, p.Center.Z - reach, p.Center.X + reach, p.Center.Z + reach, (idx, x, z) =>
                {
                    float d = Vec3.DistanceXZ(new Vec3(x, 0, z), p.Center);
                    float w = SmoothStep(p.Radius * 1.6f, p.Radius * 0.85f, d);
                    if (w > 0f) hf.Heights[idx] += (poiHeights[i] - hf.Heights[idx]) * w;
                });
            }

            // Water levels first (from the levelled ground, before roads touch it): a river's level
            // only falls downstream; a lake takes the level of the river where it passes through.
            foreach (RiverPath river in l.Rivers)
            {
                float last = float.MaxValue;
                for (int i = 0; i < river.Points.Count; i++)
                {
                    Vec3 p = river.Points[i];
                    float bank = SampleRaw(hf, p.X, p.Z) - 1.8f;
                    foreach (LakeInfo lake in l.Lakes)
                        if (Vec3.DistanceXZ(p, lake.Center) < lake.Radius) bank = Math.Min(bank, SampleRaw(hf, lake.Center.X, lake.Center.Z) - 2.5f);
                    last = Math.Min(last, bank);
                    river.Points[i] = new Vec3(p.X, last, p.Z);
                }
                foreach (LakeInfo lake in l.Lakes)
                    for (int i = 0; i < river.Points.Count; i++)
                        if (Vec3.DistanceXZ(river.Points[i], lake.Center) < lake.Radius) lake.Level = river.Points[i].Y;
            }

            // Roads (the valley's and the highway first): each is cut into short lengths whose heights
            // come from the ground, except where it meets a road graded before it or crosses a river
            // (those points are pinned), as are points on a town's levelled pad. Grades are limited so
            // vehicles can climb them. Then the ground within a few metres of every road is pulled to
            // the roads' surfaces at once: where roads meet, their heights are averaged (the nearer a
            // road's middle, the more it counts), so junctions are smooth whatever order roads come in.
            var roadDone = new float[res * res];   // strongest road weight per cell
            var sumW = new float[res * res];
            var sumWH = new float[res * res];
            var cells = new Dictionary<int, (float W, float H)>();
            var graded = new List<(RoadPath Road, float[] Heights)>();
            foreach (RoadPath r in l.Roads)
            {
                Subdivide(r, 24f);
                List<float> crossing = AddCrossings(r, graded, l, hf);
                var anchors = new float[r.Points.Count];
                var pinned = new bool[r.Points.Count];
                for (int i = 0; i < r.Points.Count; i++)
                {
                    Vec3 pt = r.Points[i];
                    if (!float.IsNaN(crossing[i]))
                    {
                        anchors[i] = crossing[i];
                        pinned[i] = true;
                        continue;
                    }
                    anchors[i] = Math.Max(SampleRaw(hf, pt.X, pt.Z), l.WaterLevel + 2f);
                    for (int k = 0; k < l.Pois.Count; k++)
                        if (l.Pois[k].Kind != "lake" && Vec3.DistanceXZ(pt, l.Pois[k].Center) < l.Pois[k].Radius * 0.8f) pinned[i] = true;
                }
                LimitGrade(r, anchors, pinned, 0.1f);
                graded.Add((r, anchors));
                // This road's own surface: the nearest of its lengths decides (bends overlap).
                cells.Clear();
                for (int s = 0; s + 1 < r.Points.Count; s++)
                {
                    Vec3 a = r.Points[s], b = r.Points[s + 1];
                    float ha = anchors[s], hb = anchors[s + 1];
                    float reach = r.Width * 0.5f + 9f;
                    ForCells(hf, Math.Min(a.X, b.X) - reach, Math.Min(a.Z, b.Z) - reach, Math.Max(a.X, b.X) + reach, Math.Max(a.Z, b.Z) + reach, (idx, x, z) =>
                    {
                        float d = WorldLayout.DistanceToSegmentXZ(x, z, a, b, out float t);
                        float w = SmoothStep(r.Width * 0.5f + 9f, r.Width * 0.5f + 1f, d);
                        if (w <= 0f || (cells.TryGetValue(idx, out var cur) && cur.W >= w)) return;
                        cells[idx] = (w, ha + (hb - ha) * t);
                    });
                }
                foreach (KeyValuePair<int, (float W, float H)> c in cells)
                {
                    float k = c.Value.W * c.Value.W;
                    k *= k;
                    sumW[c.Key] += k;
                    sumWH[c.Key] += k * c.Value.H;
                    if (c.Value.W > roadDone[c.Key]) roadDone[c.Key] = c.Value.W;
                }
            }
            for (int i = 0; i < roadDone.Length; i++)
                if (roadDone[i] > 0f) hf.Heights[i] += (sumWH[i] / sumW[i] - hf.Heights[i]) * roadDone[i];

            // Rivers: a channel cut below the water level (set before the roads); lakes are bowls.
            foreach (RiverPath river in l.Rivers)
            {
                for (int s = 0; s + 1 < river.Points.Count; s++)
                {
                    Vec3 a = river.Points[s], b = river.Points[s + 1];
                    float half = river.Width * 0.5f, reach = half + 40f;
                    ForCells(hf, Math.Min(a.X, b.X) - reach, Math.Min(a.Z, b.Z) - reach, Math.Max(a.X, b.X) + reach, Math.Max(a.Z, b.Z) + reach, (idx, x, z) =>
                    {
                        float d = WorldLayout.DistanceToSegmentXZ(x, z, a, b, out float t);
                        // Roads keep their level beyond a bridge's span.
                        if (d > reach || (d > half + BridgeClear && roadDone[idx] > 0.5f)) return;
                        float level = a.Y + (b.Y - a.Y) * t;
                        float bed = level - 2.6f * SmoothStep(half, 0f, d) - 0.4f;
                        // The bank rises gently, then ever more steeply, so it always meets the ground.
                        float beyond = Math.Max(0f, d - half - 6f);
                        float bank = level + 0.6f + (d - half) * 0.35f + beyond * beyond * 0.05f;
                        float target = d < half ? bed : Math.Min(hf.Heights[idx], bank);
                        if (target < hf.Heights[idx]) hf.Heights[idx] = target;
                    });
                }
            }
            foreach (LakeInfo lake in l.Lakes)
            {
                float reach = lake.Radius + 90f;
                ForCells(hf, lake.Center.X - reach, lake.Center.Z - reach, lake.Center.X + reach, lake.Center.Z + reach, (idx, x, z) =>
                {
                    float dist = Vec3.DistanceXZ(new Vec3(x, 0, z), lake.Center);
                    if (dist > reach || (dist > lake.Radius + 8f && roadDone[idx] > 0.5f)) return;
                    float d = dist / lake.Radius;
                    float beyond = Math.Max(0f, dist - lake.Radius - 30f);
                    float target = d < 1f ? lake.Level - 0.5f - 5f * (1f - d * d) : lake.Level + 0.5f + (dist - lake.Radius) * 0.3f + beyond * beyond * 0.03f;
                    if (target < hf.Heights[idx]) hf.Heights[idx] = target;
                });
            }
            for (int i = 0; i < hf.Heights.Length; i++) hf.Heights[i] = Math.Max(0f, Math.Min(RegionMaxHeight, hf.Heights[i]));
            return hf;
        }

        private static void SetRiverLevels(WorldLayout l)
        {
            // The road projection does not touch rivers; their levels were set while carving.
        }

        /// <summary>
        /// Limits a road's grade (rise per metre). Pinned points (crossings, a town's levelled pad)
        /// keep their heights; every other point is kept within reach of all of them and relaxed
        /// towards its neighbours. If the pinned points are too far apart in height for
        /// <paramref name="maxGrade"/>, the whole road uses the gentlest grade that joins them (at most 16 %).
        /// </summary>
        private const float MaxRoadGrade = 0.16f;

        private static void LimitGrade(RoadPath r, float[] h, bool[] pinned, float maxGrade)
        {
            int n = h.Length;
            var at = new float[n];
            for (int i = 1; i < n; i++) at[i] = at[i - 1] + Vec3.DistanceXZ(r.Points[i - 1], r.Points[i]);
            float g = maxGrade;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    if (pinned[p] && pinned[q] && at[q] - at[p] > 1f) g = Math.Max(g, Math.Abs(h[q] - h[p]) / (at[q] - at[p]) * 1.02f);
            // Pinned points right next to each other (a crossing at a pad's edge) must not free the whole road.
            g = Math.Min(g, MaxRoadGrade);
            var lo = new float[n];
            var hi = new float[n];
            for (int i = 0; i < n; i++)
            {
                lo[i] = float.MinValue;
                hi[i] = float.MaxValue;
                if (pinned[i]) continue;
                for (int p = 0; p < n; p++)
                {
                    if (!pinned[p]) continue;
                    float reach = Math.Abs(at[i] - at[p]) * g;
                    lo[i] = Math.Max(lo[i], h[p] - reach);
                    hi[i] = Math.Min(hi[i], h[p] + reach);
                }
                h[i] = Math.Max(lo[i], Math.Min(hi[i], h[i]));
            }
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < n; i++)
                {
                    if (pinned[i]) continue;
                    float max = (at[i] - at[i - 1]) * g;
                    h[i] = Math.Max(lo[i], Math.Min(hi[i], Math.Max(h[i - 1] - max, Math.Min(h[i - 1] + max, h[i]))));
                }
                for (int i = n - 2; i >= 0; i--)
                {
                    if (pinned[i]) continue;
                    float max = (at[i + 1] - at[i]) * g;
                    h[i] = Math.Max(lo[i], Math.Min(hi[i], Math.Max(h[i + 1] - max, Math.Min(h[i + 1] + max, h[i]))));
                }
            }
        }

        /// <summary>
        /// Inserts a point wherever <paramref name="r"/> crosses or meets a road already graded, or a
        /// river, and returns per point the height it must have there (NaN for ordinary points).
        /// </summary>
        private static List<float> AddCrossings(RoadPath r, List<(RoadPath Road, float[] Heights)> graded, WorldLayout l, HeightField hf)
        {
            var pts = new List<Vec3>(r.Points);
            var outPts = new List<Vec3>();
            var heights = new List<float>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var hits = new List<(float T, float H)>();
                foreach ((RoadPath road, float[] h) in graded)
                    for (int j = 0; j + 1 < road.Points.Count; j++)
                        if (IntersectTU(pts[i], pts[i + 1], road.Points[j], road.Points[j + 1], out float t, out float u))
                            hits.Add((t, h[j] + (h[j + 1] - h[j]) * u));
                // Over a river the road rises to bridge height: 4 m above the water.
                foreach (RiverPath river in l.Rivers)
                    for (int j = 0; j + 1 < river.Points.Count; j++)
                        if (IntersectTU(pts[i], pts[i + 1], river.Points[j], river.Points[j + 1], out float t, out float u))
                        {
                            float level = river.Points[j].Y + (river.Points[j + 1].Y - river.Points[j].Y) * u;
                            hits.Add((t, level + 4f));
                        }
                hits.Sort((a, b) => a.T.CompareTo(b.T));
                outPts.Add(pts[i]);
                heights.Add(hits.Count > 0 && hits[0].T < 1e-3f ? hits[0].H : float.NaN);
                foreach ((float t, float h) in hits)
                {
                    if (t < 1e-3f || t > 1f - 1e-3f) continue;
                    outPts.Add(Vec3.Lerp(pts[i], pts[i + 1], t));
                    heights.Add(h);
                }
                if (i + 2 == pts.Count)
                {
                    outPts.Add(pts[i + 1]);
                    heights.Add(hits.Count > 0 && hits[hits.Count - 1].T > 1f - 1e-3f ? hits[hits.Count - 1].H : float.NaN);
                }
            }
            r.Points.Clear();
            r.Points.AddRange(outPts);
            return heights;
        }

        private static bool IntersectTU(Vec3 a, Vec3 b, Vec3 c, Vec3 d, out float t, out float u)
        {
            t = u = 0f;
            float r1x = b.X - a.X, r1z = b.Z - a.Z, r2x = d.X - c.X, r2z = d.Z - c.Z;
            float den = r1x * r2z - r1z * r2x;
            if (Math.Abs(den) < 1e-6f) return false;
            t = ((c.X - a.X) * r2z - (c.Z - a.Z) * r2x) / den;
            u = ((c.X - a.X) * r1z - (c.Z - a.Z) * r1x) / den;
            const float eps = 1e-4f;
            return t >= -eps && t <= 1f + eps && u >= -eps && u <= 1f + eps;
        }

        /// <summary>Splits a path's segments into lengths of at most <paramref name="step"/> metres.</summary>
        private static void Subdivide(RoadPath r, float step)
        {
            var pts = new List<Vec3>(r.Points);
            r.Points.Clear();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                int n = Math.Max(1, (int)Math.Ceiling(Vec3.DistanceXZ(pts[i], pts[i + 1]) / step));
                for (int k = 0; k < n; k++) r.Points.Add(Vec3.Lerp(pts[i], pts[i + 1], k / (float)n));
            }
            r.Points.Add(pts[pts.Count - 1]);
        }

        private static float SampleRaw(HeightField hf, float x, float z) => hf.Sample(x, z);

        private static void ForCells(HeightField hf, float x0, float z0, float x1, float z1, Action<int, float, float> each)
        {
            float cell = hf.CellSize;
            int ax = Math.Max(0, (int)Math.Floor(x0 / cell)), bx = Math.Min(hf.Resolution - 1, (int)Math.Ceiling(x1 / cell));
            int az = Math.Max(0, (int)Math.Floor(z0 / cell)), bz = Math.Min(hf.Resolution - 1, (int)Math.Ceiling(z1 / cell));
            for (int zi = az; zi <= bz; zi++)
                for (int xi = ax; xi <= bx; xi++)
                    each(zi * hf.Resolution + xi, xi * cell, zi * cell);
        }

        // ------------------------------------------------------------------ towns

        /// <summary>
        /// A grid town: streets every block, and parcels along both sides of each street facing it,
        /// leaving out anything in the river, on another road or too close to a planned building.
        /// </summary>
        private static void PlanTown(WorldLayout l, DeterministicRandom rng, List<Parcel> parcels, string poi, float x0, float z0, int blocksX, int blocksZ, float bw, float bd, string district)
        {
            float x1 = x0 + blocksX * bw, z1 = z0 + blocksZ * bd;
            var streets = new List<RoadPath>();
            for (int i = 0; i <= blocksX; i++) streets.AddRange(StreetPieces(l, new Vec3(x0 + i * bw, 0, z0), new Vec3(x0 + i * bw, 0, z1)));
            for (int j = 0; j <= blocksZ; j++) streets.AddRange(StreetPieces(l, new Vec3(x0, 0, z0 + j * bd), new Vec3(x1, 0, z0 + j * bd)));
            l.Roads.AddRange(streets);
            int placed = 0;
            foreach (RoadPath street in streets)
            {
                Vec3 a = street.Points[0], b = street.Points[street.Points.Count - 1];
                float len = Vec3.DistanceXZ(a, b);
                Vec3 dir = (b - a).Normalized;
                var side = new Vec3(-dir.Z, 0, dir.X);
                for (float t = 12f; t < len - 12f; t += 19f)
                {
                    foreach (float s in new[] { -1f, 1f })
                    {
                        string kind;
                        Vec3 size;
                        int variant = rng.Range(0, 4);
                        float roll = rng.NextFloat();
                        if (district == "industrial")
                        {
                            if (roll < 0.45f) { kind = "warehouse"; size = new Vec3(16, 6, 12); }
                            else if (roll < 0.75f) { kind = "garage"; size = new Vec3(14, 5, 10); }
                            else if (roll < 0.88f) { kind = "shop"; size = new Vec3(10, 3.4f, 7); }
                            else { kind = "shack"; size = new Vec3(6, 3, 5); }
                        }
                        else
                        {
                            if (roll < 0.7f) { kind = "house"; size = new Vec3(8, 3.2f, 6); }
                            else if (roll < 0.86f) { kind = "shop"; size = new Vec3(10, 3.4f, 7); }
                            else if (roll < 0.93f) { kind = "clinic"; size = new Vec3(10, 3.4f, 8); }
                            else { kind = "garage"; size = new Vec3(14, 5, 10); }
                        }
                        if (rng.NextFloat() < 0.15f) continue; // an empty lot
                        float setback = street.Width * 0.5f + 3f + size.Z * 0.5f;
                        Vec3 c = a + dir * t + side * (s * setback);
                        // Buildings face the street: their front (+Z) points back across it.
                        float yaw = (float)(Math.Atan2(-side.X * s, -side.Z * s) * 180.0 / Math.PI);
                        float clearance = Math.Max(size.X, size.Z) * 0.5f + 1f;
                        if (NearWater(l, c.X, c.Z, clearance + 20f) || NearOtherRoad(l, c.X, c.Z, clearance + 1f, street)) continue;
                        bool overlaps = false;
                        foreach (Parcel p in parcels)
                            if (Vec3.DistanceXZ(new Vec3(p.X, 0, p.Z), c) < clearance + Math.Max(p.Size.X, p.Size.Z) * 0.5f + 1.5f) overlaps = true;
                        if (overlaps || c.X < 20f || c.Z < 20f || c.X > RegionSize - 20f || c.Z > RegionSize - 20f) continue;
                        parcels.Add(new Parcel { Poi = poi, Kind = kind, X = c.X, Z = c.Z, Yaw = yaw, Size = size, Variant = variant });
                        placed++;
                    }
                }
            }
        }

        /// <summary>A street from a to b, split where it would run through a river (only planned bridges cross water).</summary>
        private static List<RoadPath> StreetPieces(WorldLayout l, Vec3 a, Vec3 b)
        {
            var pieces = new List<RoadPath>();
            float len = Vec3.DistanceXZ(a, b);
            Vec3 start = a;
            bool inside = false;
            int steps = Math.Max(2, (int)(len / 4f));
            for (int i = 0; i <= steps; i++)
            {
                Vec3 p = Vec3.Lerp(a, b, i / (float)steps);
                bool wet = NearWater(l, p.X, p.Z, 24f);
                if (wet && !inside)
                {
                    if (Vec3.DistanceXZ(start, p) > 20f) pieces.Add(Street(start, p));
                    inside = true;
                }
                else if (!wet && inside)
                {
                    start = p;
                    inside = false;
                }
            }
            if (!inside && Vec3.DistanceXZ(start, b) > 20f) pieces.Add(Street(start, b));
            return pieces;
        }

        private static RoadPath Street(Vec3 a, Vec3 b)
        {
            var r = new RoadPath { Width = 7f, Paved = true };
            r.Points.Add(a);
            r.Points.Add(b);
            return r;
        }

        private static bool NearWater(WorldLayout l, float x, float z, float margin)
        {
            foreach (RiverPath r in l.Rivers)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                    if (WorldLayout.DistanceToSegmentXZ(x, z, r.Points[i], r.Points[i + 1], out _) < r.Width * 0.5f + margin) return true;
            foreach (LakeInfo lake in l.Lakes)
                if (Vec3.DistanceXZ(new Vec3(x, 0, z), lake.Center) < lake.Radius + margin) return true;
            return false;
        }

        private static bool NearOtherRoad(WorldLayout l, float x, float z, float clearance, RoadPath except)
        {
            foreach (RoadPath r in l.Roads)
            {
                if (r == except) continue;
                for (int i = 0; i + 1 < r.Points.Count; i++)
                    if (WorldLayout.DistanceToSegmentXZ(x, z, r.Points[i], r.Points[i + 1], out _) < r.Width * 0.5f + clearance) return true;
            }
            return false;
        }

        /// <summary>Every town parcel becomes a building with the furniture its kind has, and some infected.</summary>
        private static void PlaceParcels(WorldLayout l, List<Parcel> parcels, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "region:parcels");
            foreach (Parcel p in parcels)
            {
                if (p.Kind == "reserved") continue;
                PlaceBuilding(l, p.Poi, p.Kind, p.X, p.Z, p.Yaw, p.Size, p.Variant, ContainersFor(p.Kind, rng));
                if (rng.NextFloat() < 0.35f) AddSpawns(l, p.X + rng.Range(-8f, 8f), p.Z + rng.Range(-8f, 8f));
                if (rng.NextFloat() < 0.12f) AddNode(l, rng.NextFloat() < 0.5f ? "wreck_car" : "scrap_pile", p.X + rng.Range(-9f, 9f), p.Z + rng.Range(-9f, 9f), rng.Range(0f, 360f), 1f);
            }
        }

        private static ContainerSpec[] ContainersFor(string kind, DeterministicRandom rng)
        {
            switch (kind)
            {
                case "house":
                    return rng.NextFloat() < 0.5f
                        ? new[] { new ContainerSpec("loot_cabinet", "kitchen", -2.9f, -2.1f, 0), new ContainerSpec("loot_fridge", "fridge", -1.6f, -2.3f, 0), new ContainerSpec("loot_wardrobe", "wardrobe", 3.1f, -1.2f, -90) }
                        : new[] { new ContainerSpec("loot_cabinet", "kitchen", -2.9f, -2.1f, 0), new ContainerSpec("loot_toolbox", "toolbox", 3.0f, 1.8f, -90) };
                case "shop":
                    return new[] { new ContainerSpec("loot_shelf", "store", -3.5f, -2.4f, 0), new ContainerSpec("loot_shelf", "store", 0.0f, -2.4f, 0), new ContainerSpec("loot_counter", "counter", 3.6f, 1.0f, -90) };
                case "clinic":
                    return new[] { new ContainerSpec("loot_medcab", "medical", -4.0f, -3.0f, 0), new ContainerSpec("loot_medcab", "medical", -1.5f, -3.2f, 0), new ContainerSpec("loot_crate", "general", 3.8f, -2.6f, -90) };
                case "garage":
                    return new[] { new ContainerSpec("loot_toolbox", "toolbox", -5.8f, -3.8f, 0), new ContainerSpec("loot_toolbox", "workshop", -3.4f, -4.0f, 0), new ContainerSpec("loot_crate", "workshop", 5.6f, -3.6f, -90) };
                case "warehouse":
                    return new[] { new ContainerSpec("loot_crate", "general", -6.5f, -4.5f, 0), new ContainerSpec("loot_crate", "workshop", -4.0f, -4.6f, 0), new ContainerSpec("loot_locker", rng.NextFloat() < 0.3f ? "electrical" : "electrical_minor", 6.6f, -3.5f, -90) };
                default:
                    return new[] { new ContainerSpec("loot_crate", "general", 0f, -1.2f, 0) };
            }
        }

        // ------------------------------------------------------------------ remote places

        private static void PlacePinecrest(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "region:pinecrest");
            PoiInfo poi = l.Poi("pinecrest");
            float cx = poi.Center.X, cz = poi.Center.Z;
            // A loop road round the village green, cabins facing it.
            var loop = new RoadPath { Width = 5f, Paved = false };
            for (int i = 0; i <= 12; i++)
            {
                double a = i * Math.PI * 2 / 12;
                loop.Points.Add(l.Ground(cx + (float)Math.Cos(a) * 34f, cz + (float)Math.Sin(a) * 34f));
            }
            l.Roads.Add(loop);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI * 2 / 8 + 0.2;
                float x = cx + (float)Math.Cos(a) * 50f, z = cz + (float)Math.Sin(a) * 50f;
                float yaw = (float)(Math.Atan2(cx - x, cz - z) * 180.0 / Math.PI);
                bool cabin = i % 3 != 0;
                PlaceBuilding(l, "pinecrest", cabin ? "shack" : "house", x, z, yaw, cabin ? new Vec3(6, 3, 5) : new Vec3(8, 3.2f, 6), rng.Range(0, 4), ContainersFor(cabin ? "shack" : "house", rng));
            }
            PlaceBuilding(l, "pinecrest", "barn", cx - 6, cz + 4, 30, new Vec3(12, 6, 9), 0, new[] { new ContainerSpec("loot_crate", "barn", -4.5f, -3.4f, 0) });
            PlaceBuilding(l, "pinecrest", "shop", cx + 10, cz - 8, 200, new Vec3(10, 3.4f, 7), 0, ContainersFor("shop", rng));
            AddSpawns(l, cx, cz + 20, cx - 25, cz - 10, cx + 30, cz + 15);
        }

        private static void PlaceRegionFarms(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "region:farms");
            foreach (string id in new[] { "millerfarm", "coldwater" })
            {
                PoiInfo p = l.Poi(id);
                float cx = p.Center.X, cz = p.Center.Z;
                PlaceBuilding(l, id, "barn", cx + 14, cz - 6, 90, new Vec3(12, 6, 9), 0, new[] { new ContainerSpec("loot_crate", "barn", -4.5f, -3.4f, 0), new ContainerSpec("loot_toolbox", "toolbox", 4.5f, -3.4f, 0) });
                PlaceBuilding(l, id, "house", cx - 12, cz + 10, 160, new Vec3(8, 3.2f, 6), rng.Range(0, 4), ContainersFor("house", rng));
                AddProp(l, id, "silo", cx + 26, cz + 10, 0, new Vec3(4, 9, 4));
                for (int i = 0; i < 16; i++) AddNode(l, "plant_fiber", cx + rng.Range(-40f, -18f), cz + rng.Range(-38f, -16f), rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                for (int i = 0; i < 5; i++) AddNode(l, "bush_berry", cx + rng.Range(20f, 34f), cz + rng.Range(18f, 30f), rng.Range(0f, 360f), 1f);
                AddProp(l, id, "fence", cx - 29, cz - 40, 0, new Vec3(24, 1.1f, 0.1f));
                AddSpawns(l, cx + 6, cz - 20);
            }
        }

        private static void PlaceResearch(WorldLayout l, int seed)
        {
            PoiInfo p = l.Poi("research");
            float cx = p.Center.X, cz = p.Center.Z;
            const float h = 30f;
            AddProp(l, "research", "fence", cx - h, cz, 90, new Vec3(0.1f, 2.6f, h * 2));
            AddProp(l, "research", "fence", cx + h, cz, 90, new Vec3(0.1f, 2.6f, h * 2));
            AddProp(l, "research", "fence", cx, cz + h, 0, new Vec3(h * 2, 2.6f, 0.1f));
            AddProp(l, "research", "fence", cx - 19, cz - h, 0, new Vec3(22, 2.6f, 0.1f));
            AddProp(l, "research", "fence", cx + 19, cz - h, 0, new Vec3(22, 2.6f, 0.1f));
            PlaceBuilding(l, "research", "warehouse", cx, cz + 10, 180, new Vec3(16, 6, 12), 0, new[]
            {
                new ContainerSpec("loot_locker", "electrical", -6.5f, -4.5f, 0),
                new ContainerSpec("loot_locker", "schematic", -4.0f, -4.6f, 0),
                new ContainerSpec("loot_desk", "office", 5.5f, -3.5f, -90),
                new ContainerSpec("loot_medcab", "medical", 6.6f, 1.5f, -90)
            });
            PlaceBuilding(l, "research", "shack", cx - 18, cz - 12, 90, new Vec3(6, 3, 5), 0, new[] { new ContainerSpec("loot_locker", "electrical", -2.1f, -1.8f, 0) });
            PlaceBuilding(l, "research", "shack", cx + 18, cz - 12, 270, new Vec3(6, 3, 5), 1, new[] { new ContainerSpec("loot_desk", "office", 0f, -1.6f, 0) });
            AddProp(l, "research", "transformer", cx + 20, cz + 18, 0, new Vec3(3, 3, 2.4f));
            AddProp(l, "research", "pylon", cx - 24, cz + 24, 0, new Vec3(1.5f, 12, 1.5f));
            AddSpawns(l, cx, cz - 6, cx - 10, cz + 2, cx + 12, cz + 4, cx - 22, cz - 20, cx + 22, cz - 22, cx, cz + 22);
        }

        private static void PlaceLodge(WorldLayout l)
        {
            PoiInfo p = l.Poi("lodge");
            PlaceBuilding(l, "lodge", "house", p.Center.X, p.Center.Z, 250, new Vec3(8, 3.2f, 6), 2, new[]
            {
                new ContainerSpec("loot_cabinet", "kitchen", -2.9f, -2.1f, 0),
                new ContainerSpec("loot_wardrobe", "wardrobe", 3.1f, -1.2f, -90)
            });
            PlaceBuilding(l, "lodge", "shack", p.Center.X + 12, p.Center.Z + 6, 200, new Vec3(5, 2.8f, 4), 1, new[] { new ContainerSpec("loot_toolbox", "toolbox", 0f, -1.2f, 0) });
            AddProp(l, "lodge", "firepit", p.Center.X - 6, p.Center.Z - 7, 0, new Vec3(1.2f, 0.3f, 1.2f));
        }

        /// <summary>The disused freight line along the north edge of Eastgate: a graded path, track laid in 12 m lengths.</summary>
        private static void DefineRail(WorldLayout l)
        {
            var rail = new RoadPath { Width = 4f, Paved = false, Rail = true };
            for (float x = 1320f; x <= 1980f; x += RailLength) rail.Points.Add(new Vec3(x, 0, 715f));
            l.Roads.Add(rail);
        }

        private const float RailLength = 12f;

        private static void PlaceRail(WorldLayout l)
        {
            foreach (RoadPath r in l.Roads)
            {
                if (!r.Rail) continue;
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    Vec3 a = r.Points[i], b = r.Points[i + 1];
                    Vec3 mid = Vec3.Lerp(a, b, 0.5f);
                    float yaw = (float)(Math.Atan2(b.X - a.X, b.Z - a.Z) * 180.0 / Math.PI);
                    l.Props.Add(new PropPlacement { Kind = "rail", Position = new Vec3(mid.X, (a.Y + b.Y) * 0.5f, mid.Z), Yaw = yaw, Size = new Vec3(3.2f, 0.4f, Vec3.DistanceXZ(a, b)), PoiId = "eastgate", Variant = i % 4 });
                }
            }
        }

        /// <summary>Lays each bridge's deck from bank to bank: its ends sit on the road beyond the carved channel.</summary>
        private static void SetBridgeDecks(WorldLayout l)
        {
            foreach (PropPlacement p in l.Props)
            {
                if (p.Kind != "bridge") continue;
                double a = p.Yaw * Math.PI / 180.0;
                float dx = (float)Math.Sin(a) * p.Size.Z * 0.5f, dz = (float)Math.Cos(a) * p.Size.Z * 0.5f;
                // The road surface just beyond each end (the banks under the deck are cut away).
                float k = (p.Size.Z * 0.5f + 2f) / (p.Size.Z * 0.5f);
                float front = l.HeightAt(p.Position.X + dx * k, p.Position.Z + dz * k), back = l.HeightAt(p.Position.X - dx * k, p.Position.Z - dz * k);
                p.Position = new Vec3(p.Position.X, (front + back) * 0.5f, p.Position.Z);
                p.Pitch = (float)(Math.Atan2(front - back, p.Size.Z) * 180.0 / Math.PI);
            }
        }

        // ------------------------------------------------------------------ nature and infected

        private static void PlaceRegionVegetation(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "region:vegetation");
            var roads = new RoadIndex(l);
            // Building footprints (and the planned blocks, placed later) are kept clear of trees.
            foreach (PropPlacement p in l.Props)
                if (p.Kind != "rail" && p.Kind != "bridge") roads.AddDisc(p.Position.X, p.Position.Z, Math.Max(p.Size.X, p.Size.Z) * 0.5f + 1.5f);
            roads.AddDisc(AshfordApartments.X, AshfordApartments.Z, 14f);
            roads.AddDisc(EastgateApartments.X, EastgateApartments.Z, 14f);
            const float spacing = 10f;
            for (float z = 8f; z < RegionSize - 8f; z += spacing)
            {
                for (float x = 8f; x < RegionSize - 8f; x += spacing)
                {
                    float px = x + rng.Range(-4f, 4f), pz = z + rng.Range(-4f, 4f);
                    float forest = RegionForest(seed, px, pz);
                    if (!FreeForRegionVegetation(l, roads, px, pz, 2.5f)) continue;
                    float hgt = l.HeightAt(px, pz);
                    if (forest > 0f && rng.Chance(0.6f))
                    {
                        string type = hgt > 120f || rng.Chance(0.7f) ? "tree_pine" : "tree_birch";
                        AddNode(l, type, px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.35f));
                    }
                    else if (forest < -0.25f && rng.Chance(0.06f)) AddNode(l, "plant_fiber", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                    else if (rng.Chance(0.02f)) AddNode(l, "bush_berry", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                    else if (rng.Chance(hgt > 90f ? 0.05f : 0.012f)) AddNode(l, hgt > 100f && rng.Chance(0.25f) ? "ore_iron" : "rock_small", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.5f));
                    else if (rng.Chance(0.006f)) AddNode(l, "branch_pile", px, pz, rng.Range(0f, 360f), 1f);
                }
            }
        }

        /// <summary>
        /// Woodland density in the region (above zero is forest): thick in the west and the northern
        /// hills, thin on the eastern plains and the high peaks. Ground painting uses it too.
        /// </summary>
        public static float RegionForest(int seed, float x, float z)
        {
            float density = Noise.Fbm(seed + 77, x * 0.009f, z * 0.009f, 3);
            return density + (700f - x) / 1400f * 0.35f + SmoothStep(500f, 900f, z) * 0.25f - SmoothStep(1300f, 1700f, z) * SmoothStep(1100f, 1600f, x) * 0.1f
                   - SmoothStep(1250f, 1500f, x) * SmoothStep(700f, 300f, z) * 0.45f - SmoothStep(1800f, 2000f, z) * 0.3f;
        }

        private static bool FreeForRegionVegetation(WorldLayout l, RoadIndex roads, float x, float z, float clearance)
        {
            if (!l.IsInsideMap(x, z, 6f)) return false;
            if (l.IsWater(x, z) || NearWaterFast(l, x, z, 3f)) return false;
            if (l.Height.SlopeDegrees(x, z) > 32f) return false;
            if (roads.Near(x, z, clearance + 1.5f)) return false;
            foreach (PoiInfo p in l.Pois)
                if (p.Kind != "lake" && Vec3.DistanceXZ(p.Center, new Vec3(x, 0, z)) < p.Radius + clearance) return false;
            return true;
        }

        private static bool NearWaterFast(WorldLayout l, float x, float z, float margin) =>
            l.IsWater(x + margin, z) || l.IsWater(x - margin, z) || l.IsWater(x, z + margin) || l.IsWater(x, z - margin);

        private static void PlaceRegionSpawns(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "region:spawns");
            int added = 0;
            for (int attempt = 0; attempt < 600 && added < 40; attempt++)
            {
                float x = rng.Range(40f, RegionSize - 40f), z = rng.Range(40f, RegionSize - 40f);
                if (x < 520f && z < 520f) continue; // the valley keeps its own
                if (l.IsWater(x, z) || l.Height.SlopeDegrees(x, z) > 25f) continue;
                l.ZombieSpawnPoints.Add(l.Ground(x, z));
                added++;
            }
        }

        private static void PlaceRegionVehicles(WorldLayout l)
        {
            l.Vehicles.Add(new VehicleSeed
            {
                Key = "vehicle:eastgate_sedan", DefId = "sedan", Position = l.Ground(1712f, 628f), Yaw = 90f, Paint = 3, Fuel = 30f,
                Engine = 74f, Body = 66f, Tyres = 80f, Battery = 70f, Locked = true, LockId = BuildingPlans.EastgateCarLock
            });
            l.Vehicles.Add(new VehicleSeed
            {
                Key = "vehicle:ashford_pickup", DefId = "pickup", Position = l.Ground(910f, 452f), Yaw = 0f, Paint = 2, Fuel = 24f,
                Engine = 60f, Body = 55f, Tyres = 70f, Battery = 60f, LockId = "Ashford pickup · AF-3318", KeyInIgnition = true
            });
            l.Vehicles.Add(new VehicleSeed
            {
                Key = "vehicle:pinecrest_atv", DefId = "atv", Position = l.Ground(548f, 1640f), Yaw = 200f, Paint = 0, Fuel = 6f,
                Engine = 70f, Body = 75f, Tyres = 65f, Battery = 55f, LockId = "Pinecrest quad bike"
            });
        }

        /// <summary>Road segments by 64 m cell, for fast "is this near a road" questions.</summary>
        private sealed class RoadIndex
        {
            private readonly Dictionary<long, List<(Vec3 A, Vec3 B, float Half)>> _cells = new Dictionary<long, List<(Vec3, Vec3, float)>>();
            private const float Cell = 64f;

            public RoadIndex(WorldLayout l)
            {
                foreach (RoadPath r in l.Roads)
                    for (int i = 0; i + 1 < r.Points.Count; i++)
                    {
                        Vec3 a = r.Points[i], b = r.Points[i + 1];
                        float m = r.Width * 0.5f + 8f;
                        int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - m) / Cell), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + m) / Cell);
                        int z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - m) / Cell), z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + m) / Cell);
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                long k = ((long)x << 32) ^ (uint)z;
                                if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<(Vec3, Vec3, float)>();
                                list.Add((a, b, r.Width * 0.5f));
                            }
                    }
            }

            /// <summary>Adds a round obstacle (a zero-length segment) of radius <paramref name="r"/>.</summary>
            public void AddDisc(float x, float z, float r)
            {
                var c = new Vec3(x, 0, z);
                int x0 = (int)Math.Floor((x - r - 8f) / Cell), x1 = (int)Math.Floor((x + r + 8f) / Cell);
                int z0 = (int)Math.Floor((z - r - 8f) / Cell), z1 = (int)Math.Floor((z + r + 8f) / Cell);
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        long k = ((long)cx << 32) ^ (uint)cz;
                        if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<(Vec3, Vec3, float)>();
                        list.Add((c, c, r));
                    }
            }

            public bool Near(float x, float z, float clearance)
            {
                long k = ((long)(int)Math.Floor(x / Cell) << 32) ^ (uint)(int)Math.Floor(z / Cell);
                if (!_cells.TryGetValue(k, out var list)) return false;
                foreach (var s in list)
                    if (WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out _) < s.Half + clearance) return true;
                return false;
            }
        }
    }
}
