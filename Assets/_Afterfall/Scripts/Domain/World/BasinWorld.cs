using System;
using System.Collections.Generic;
using System.Threading;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// World version 3: the Kessel Basin, 64 km × 64 km, generated in 512 m chunks as the survivor
    /// travels. Greater Millbrook (version 2) is its south-west corner, unchanged. Around it: the
    /// Greyspine mountains (snow above ~550 m) in the west, foothills and the Thornwood, the High
    /// Moor in the north, a lake district in the north-east, the red badlands of the Scar in the
    /// south-west, farmland in the south, salt marshes on the estuary, a coast of beaches and
    /// cliffs in the east, a dozen towns and villages, and the metropolis of Kesselmouth (about the
    /// area of Greater London) on the Aldwater river, all joined by a graded road and rail network.
    /// Only global vector data (rivers, lakes, roads, places, the city's plan) is made up front;
    /// terrain tiles and chunk content are made from it on demand and depend only on the seed.
    /// </summary>
    public sealed partial class BasinWorld : IChunkSource
    {
        public const float WorldSize = 65536f;
        public const float MaxHeight = 900f;
        public const float Sea = 12f;
        public const float BlockSize = 2048f;
        /// <summary>Width of the band where Greater Millbrook's ground blends into the basin's.</summary>
        public const float BlendWidth = 1200f;
        public static readonly Vec3 CityCenter = new Vec3(40000f, 0f, 30000f);
        public const float CityRadius = 21000f;

        private readonly int _seed;
        private readonly WorldLayout _layout;
        private readonly WorldLayout _region;

        private struct Village
        {
            public string Id, Name, Kind;
            public Vec3 Center;
            public float Radius, Pad;
        }

        private readonly List<Village> _villages = new List<Village>();

        /// <summary>A graded road length: ends, their surface heights, half its width.</summary>
        private struct Seg
        {
            public Vec3 A, B;
            public float HA, HB, Half;
            public bool Rail, Paved;
        }

        private readonly List<Seg> _segs = new List<Seg>();
        private readonly Dictionary<long, List<int>> _segIndex = new Dictionary<long, List<int>>();
        private const float SegCell = 256f;

        private struct RiverSeg
        {
            public Vec3 A, B;
            public float Half;
        }

        private readonly List<RiverSeg> _riverSegs = new List<RiverSeg>();
        private readonly Dictionary<long, List<int>> _valleyIndex = new Dictionary<long, List<int>>();
        private const float ValleyCell = 512f, ValleyReach = 700f;
        private readonly List<PropPlacement> _bridges = new List<PropPlacement>();

        public BasinWorld(int seed, WorldLayout layout, WorldLayout region)
        {
            _seed = seed;
            _layout = layout;
            _region = region;
        }

        // ================================================================== the plan

        /// <summary>Builds the global data: places, rivers and lakes, roads and rails, bridges.</summary>
        public void Plan()
        {
            DefinePlaces();
            DefineRivers();
            DefineLakes();
            IndexRiversForValleys();
            DefineRoads();
            _layout.IndexWater();
            CopyRegion(new ChunkContent(0, 0)); // builds the per-chunk buckets now, so worker threads only read them
        }

        private void DefinePlaces()
        {
            void V(string id, string name, string kind, float x, float z, float radius, string desc, string risk = "Medium")
            {
                _villages.Add(new Village { Id = id, Name = name, Kind = kind, Center = new Vec3(x, 0, z), Radius = radius, Pad = radius });
                _layout.Pois.Add(new PoiInfo { Id = id, Name = name, Kind = kind, Center = new Vec3(x, 0, z), Radius = radius, Risk = risk, Description = desc });
            }
            V("harrowgate", "Harrowgate", "town", 14000, 9000, 150, "A market town on the motorway: shops, a garage, a clinic and rows of houses.");
            V("oxley", "Oxley", "village", 25000, 7000, 90, "A farming village among wheat fields.", "Low");
            V("stonebridge", "Stonebridge", "village", 19000, 22000, 100, "A village at the crossroads below the foothills.");
            V("ashbycross", "Ashby Cross", "village", 27000, 18000, 80, "A crossroads hamlet with a pub and a chapel.", "Low");
            V("cragholm", "Cragholm", "village", 6500, 22000, 90, "A stone village in a high mountain valley.");
            V("redrock", "Redrock", "outpost", 12000, 5500, 70, "A dusty outpost in the red badlands of the Scar.", "High");
            V("hightor", "High Tor", "hamlet", 31000, 55000, 60, "A few farmhouses on the bare High Moor.", "Low");
            V("kingsmoor", "Kingsmoor", "town", 40000, 50000, 140, "A moorland town where the north road meets the lakes road.");
            V("dunmere", "Dunmere", "village", 50500, 51500, 100, "A lakeside village in the lake district.", "Low");
            V("gullhaven", "Gullhaven", "village", 57000, 44000, 90, "A fishing village above the northern cliffs.");
            V("saltcombe", "Saltcombe", "village", 56000, 9000, 100, "A harbour village behind the southern dunes.");
            V("fenwick", "Fenwick", "hamlet", 53000, 19000, 70, "Stilted houses in the salt marsh of the estuary.", "High");
            _layout.Pois.Add(new PoiInfo
            {
                Id = "kesselmouth", Name = "Kesselmouth", Kind = "city", Center = CityCenter, Radius = CityRadius, Risk = "High",
                Description = "A metropolis on the Aldwater, about the size of a capital city: towers, terraces, docks, parks and millions of empty windows."
            });
            foreach ((string id, string name, float x, float z, string desc) in CityDistricts)
                _layout.Pois.Add(new PoiInfo { Id = id, Name = name, Kind = "district", Center = new Vec3(x, 0, z), Radius = 2200, Risk = "High", Description = desc });
            _layout.Pois.Add(new PoiInfo { Id = "greyspine", Name = "The Greyspine", Kind = "mountains", Center = new Vec3(6000, 0, 36000), Radius = 9000, Risk = "Medium", Description = "Snow-capped mountains along the west of the basin." });
            _layout.Pois.Add(new PoiInfo { Id = "highmoor", Name = "The High Moor", Kind = "moor", Center = new Vec3(30000, 0, 56000), Radius = 9000, Risk = "Low", Description = "Open heather moorland, wind and stone." });
            _layout.Pois.Add(new PoiInfo { Id = "lakes", Name = "The Lake District", Kind = "lakes", Center = new Vec3(51000, 0, 55000), Radius = 7000, Risk = "Low", Description = "Lakes and wooded hills in the north-east." });
            _layout.Pois.Add(new PoiInfo { Id = "scar", Name = "The Scar", Kind = "badlands", Center = new Vec3(11000, 0, 6500), Radius = 5000, Risk = "Medium", Description = "Red badlands of terraced mesas and dry gullies." });
            _layout.Pois.Add(new PoiInfo { Id = "thornwood", Name = "The Thornwood", Kind = "forest", Center = new Vec3(19000, 0, 44000), Radius = 7000, Risk = "Medium", Description = "Old forest between the mountains and the moor." });
            _layout.Pois.Add(new PoiInfo { Id = "marsh", Name = "Estuary Marshes", Kind = "marsh", Center = new Vec3(54000, 0, 20000), Radius = 5000, Risk = "Medium", Description = "Reeds, creeks and mud flats on the Aldwater estuary." });
        }

        /// <summary>The city's districts (map labels and their character).</summary>
        internal static readonly (string Id, string Name, float X, float Z, string Desc)[] CityDistricts =
        {
            ("oldquarter", "Old Quarter", 40000, 30600, "The heart of Kesselmouth: towers of glass and stone around the old bridges."),
            ("docklands", "Docklands", 52500, 28800, "Warehouses, cranes and basins where the Aldwater widens towards the sea."),
            ("westford", "Westford", 30500, 30500, "Long terraces of brick houses and busy high streets."),
            ("northfield", "Northfield", 39500, 38500, "Mansion blocks and parks on the hill north of the river."),
            ("southbank", "Southbank", 40500, 24000, "Riverside flats, markets and railway arches south of the river."),
            ("eastholm", "Eastholm", 47000, 34500, "Terraced streets and factories between the city and the marshes."),
            ("brookmere", "Brookmere", 34500, 37500, "Leafy streets along the Brack."),
            ("cedarrise", "Cedar Rise", 27500, 22500, "Suburban avenues of semi-detached houses."),
            ("hollinheath", "Hollin Heath", 44500, 41000, "A great heath of grass and woods inside the city.")
        };

        private void DefineRivers()
        {
            // The Aldwater: from the Greyspine through Kesselmouth to the sea, widening in stages.
            float[] ald = { 5200, 45500, 9000, 41000, 14000, 36500, 19500, 33500, 24000, 31500, 28500, 33000, 33000, 31000, 36500, 28000,
                            40000, 29500, 43000, 32500, 46500, 31000, 49000, 27500, 52500, 26500, 56000, 27500, 59500, 27000, 64500, 26500 };
            var stages = new[] { (0, 3, 28f, "Aldwater"), (3, 11, 90f, "Aldwater"), (11, 13, 160f, "Aldwater"), (13, 15, 300f, "Aldwater Estuary") };
            float level = float.MaxValue;
            foreach ((int from, int to, float width, string name) in stages)
            {
                var r = new RiverPath { Name = name, Width = width };
                Meander(r, ald, from, to, width < 100f ? 60f : 25f);
                level = SetLevels(r, level);
                _layout.Rivers.Add(r);
            }
            var brack = new RiverPath { Name = "Brack", Width = 26f };
            Meander(brack, new float[] { 24000, 52000, 27000, 46000, 30500, 40000, 33000, 35500, 34800, 31600 }, 0, 4, 45f);
            SetLevels(brack, float.MaxValue);
            _layout.Rivers.Add(brack);
            var fennel = new RiverPath { Name = "Fennel", Width = 24f };
            Meander(fennel, new float[] { 52200, 50500, 55000, 45500, 58000, 41000, 61000, 40200, 64500, 39800 }, 0, 4, 40f);
            SetLevels(fennel, float.MaxValue);
            _layout.Rivers.Add(fennel);
        }

        /// <summary>Control points [from..to] subdivided every ~40 m and pushed sideways by smooth noise.</summary>
        private void Meander(RiverPath r, float[] pts, int from, int to, float amplitude)
        {
            for (int i = from; i < to; i++)
            {
                var a = new Vec3(pts[i * 2], 0, pts[i * 2 + 1]);
                var b = new Vec3(pts[i * 2 + 2], 0, pts[i * 2 + 3]);
                int steps = Math.Max(1, (int)(Vec3.DistanceXZ(a, b) / 40f));
                Vec3 dir = (b - a).Normalized;
                var side = new Vec3(-dir.Z, 0, dir.X);
                for (int s = 0; s < steps; s++)
                {
                    Vec3 p = Vec3.Lerp(a, b, s / (float)steps);
                    // Ends of a stage stay put so stages join.
                    float fade = Math.Min(1f, Math.Min(s, steps - s) / 8f);
                    float off = (Noise.Fbm(_seed + 9001, p.X * 0.0008f, p.Z * 0.0008f, 3) * 2.2f) * amplitude * fade;
                    r.Points.Add(p + side * off);
                }
            }
            r.Points.Add(new Vec3(pts[to * 2], 0, pts[to * 2 + 1]));
        }

        /// <summary>Water levels that only fall downstream, never below the sea; returns the last level.</summary>
        private float SetLevels(RiverPath r, float start)
        {
            float last = start;
            for (int i = 0; i < r.Points.Count; i++)
            {
                Vec3 p = r.Points[i];
                float bank = Terrain(p.X, p.Z) - 2.2f;
                last = Math.Max(Sea, Math.Min(last, bank));
                r.Points[i] = new Vec3(p.X, last, p.Z);
            }
            return last;
        }

        private void DefineLakes()
        {
            void L(string name, float x, float z, float r)
            {
                float level = Math.Max(Sea + 1f, Terrain(x, z) - 1.5f);
                _layout.Lakes.Add(new LakeInfo { Name = name, Center = new Vec3(x, 0, z), Radius = r, Level = level });
            }
            L("Dun Water", 47000, 53200, 650);
            L("Mere of Ashes", 51200, 55700, 900);
            L("Greywater", 55600, 52600, 520);
            L("Long Mere", 49200, 59200, 720);
            L("Tarn Howe", 54200, 60200, 460);
            L("Moor Tarn", 30200, 57200, 300);
            L("Crag Lake", 7000, 30200, 380);
            // The valley's lake in Greater Millbrook (its level from version 1).
            _layout.Lakes.Add(new LakeInfo { Name = "Millbrook Lake", Center = _region.LakeCenter, Radius = _region.LakeRadius * 1.25f, Level = _region.WaterLevel });
        }

        private void IndexRiversForValleys()
        {
            foreach (RiverPath r in _layout.Rivers)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    int id = _riverSegs.Count;
                    _riverSegs.Add(new RiverSeg { A = r.Points[i], B = r.Points[i + 1], Half = r.Width * 0.5f });
                    AddToIndex(_valleyIndex, ValleyCell, r.Points[i], r.Points[i + 1], ValleyReach + r.Width, id);
                }
        }

        private static void AddToIndex(Dictionary<long, List<int>> index, float cell, Vec3 a, Vec3 b, float margin, int id)
        {
            int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - margin) / cell), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + margin) / cell);
            int z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - margin) / cell), z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + margin) / cell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long k = ((long)x << 32) ^ (uint)z;
                    if (!index.TryGetValue(k, out List<int> list)) index[k] = list = new List<int>();
                    list.Add(id);
                }
        }

        private static List<int> Lookup(Dictionary<long, List<int>> index, float cell, float x, float z)
        {
            long k = ((long)(int)Math.Floor(x / cell) << 32) ^ (uint)(int)Math.Floor(z / cell);
            return index.TryGetValue(k, out List<int> list) ? list : null;
        }

        // ================================================================== heights

        private static float S(float e0, float e1, float x) => WorldGenerator.SmoothStep(e0, e1, x);

        /// <summary>The basin's ground without rivers, roads or local detail.</summary>
        internal float Terrain(float x, float z)
        {
            float wx = x + Noise.Fbm(_seed + 101, x * 0.00005f, z * 0.00005f, 3) * 3000f;
            float wz = z + Noise.Fbm(_seed + 102, x * 0.00005f + 7.3f, z * 0.00005f, 3) * 3000f;
            float h = 26f + Noise.Fbm(_seed + 1, x * 0.00011f, z * 0.00011f, 4) * 16f + Noise.Fbm(_seed + 2, x * 0.0009f, z * 0.0009f, 3) * 4f;

            float mtn = S(17000f, 7000f, wx) * S(7000f, 15000f, wz);
            if (mtn > 0f)
            {
                float r = 1f - Math.Abs(Noise.Fbm(_seed + 3, x * 0.00028f, z * 0.00028f, 5));
                h += mtn * (90f + r * r * 680f + Noise.Fbm(_seed + 4, x * 0.0011f, z * 0.0011f, 3) * 40f);
            }
            float hills = S(26000f, 14000f, wx) * S(3000f, 11000f, wz) * (1f - mtn);
            if (hills > 0f) h += hills * (28f + Noise.Fbm(_seed + 10, x * 0.0005f, z * 0.0005f, 4) * 26f);
            float moor = S(46000f, 54000f, wz) * S(12000f, 19000f, wx) * S(53000f, 45000f, wx);
            if (moor > 0f) h += moor * (120f + Noise.Fbm(_seed + 5, x * 0.0004f, z * 0.0004f, 4) * 70f);
            float lakes = S(43000f, 49000f, wz) * S(43000f, 48000f, wx) * S(60000f, 56000f, wx);
            if (lakes > 0f) h += lakes * (70f + Noise.Fbm(_seed + 11, x * 0.0006f, z * 0.0006f, 4) * 55f);

            float bad = Badlands(x, z);
            if (bad > 0f)
            {
                float t = 46f + Noise.Fbm(_seed + 6, x * 0.0006f, z * 0.0006f, 4) * 80f;
                float step = 14f;
                float k = t / step;
                float terraced = (float)Math.Floor(k) * step + S(0.82f, 1f, k - (float)Math.Floor(k)) * step;
                h += (terraced - h) * bad;
            }

            // Highlands north of Greater Millbrook, rising to meet its mountains.
            float highland = S(5200f, 1800f, Vec3.DistanceXZ(new Vec3(x, 0, z), new Vec3(1100, 0, 2600))) * S(1400f, 2600f, z);
            if (highland > 0f) h += highland * (120f + Noise.Fbm(_seed + 13, x * 0.0012f, z * 0.0012f, 3) * 30f);

            float city = CityMask(x, z);
            if (city > 0f) h += (16f + Noise.Fbm(_seed + 7, x * 0.00008f, z * 0.00008f, 3) * 9f - h) * city;

            // Salt marsh on the estuary's south bank: barely above the sea, with pools.
            float marsh = S(4500f, 2000f, Vec3.DistanceXZ(new Vec3(wx, 0, wz), new Vec3(53500, 0, 20000)));
            if (marsh > 0f) h += (Sea + 0.9f + Noise.Fbm(_seed + 12, x * 0.004f, z * 0.004f, 3) * 1.6f - h) * marsh;

            // The coast: land slopes down to the shore (gently to the south, steeply under the northern cliffs), then the seabed.
            float shore = x - CoastX(z);
            float ramp = 2600f + (400f - 2600f) * S(34000f, 40000f, z);
            if (shore > -ramp)
            {
                float beach = Sea + 1.2f;
                h += (beach - h) * S(-ramp, 0f, shore);
                if (shore > 0f) h = Sea - 1f - Math.Min(shore * 0.03f, Sea - 1f);
            }
            else h = Math.Max(h, Sea + 2.5f);
            return Math.Max(0f, Math.Min(MaxHeight - 1f, h));
        }

        public float CoastX(float z) => 58500f + Noise.Fbm(_seed + 8, 0.37f, z * 0.00007f, 3) * 2600f;

        public float Badlands(float x, float z)
        {
            float wx = x + Noise.Fbm(_seed + 103, x * 0.0002f, z * 0.0002f, 3) * 1500f;
            return S(6200f, 3600f, Vec3.DistanceXZ(new Vec3(wx, 0, z), new Vec3(11000, 0, 6500)));
        }

        /// <summary>How much a point is city (1 within most of Kesselmouth, fading at its edge; 0 in the sea).</summary>
        public float CityMask(float x, float z)
        {
            float wx = x + Noise.Fbm(_seed + 104, x * 0.00009f, z * 0.00009f, 3) * 2500f;
            float wz = z + Noise.Fbm(_seed + 105, x * 0.00009f + 3.1f, z * 0.00009f, 3) * 2500f;
            float d = Vec3.DistanceXZ(new Vec3(wx, 0, wz), CityCenter) / CityRadius;
            return S(1.05f, 0.82f, d) * S(-200f, -900f, x - CoastX(z));
        }

        /// <summary>Terrain with the rivers' valleys (no channels, roads or detail): the coarse ground.</summary>
        public float CoarseHeight(float x, float z)
        {
            if (x < BlockSize + BlendWidth && z < BlockSize + BlendWidth)
            {
                float w = S(BlockSize + BlendWidth - 20f, BlockSize - 20f, Math.Max(x, z));
                if (w >= 1f) return _region.Height.Sample(x, z);
                float outer = Valley(x, z, Terrain(x, z));
                return outer + (_region.Height.Sample(Math.Min(x, BlockSize), Math.Min(z, BlockSize)) - outer) * w;
            }
            return Valley(x, z, Terrain(x, z));
        }

        /// <summary>Lowers ground near rivers into gentle valleys down to their banks.</summary>
        private float Valley(float x, float z, float h)
        {
            List<int> segs = Lookup(_valleyIndex, ValleyCell, x, z);
            if (segs == null) return h;
            foreach (int id in segs)
            {
                RiverSeg s = _riverSegs[id];
                float d = WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out float t);
                if (d > ValleyReach + s.Half) continue;
                float level = s.A.Y + (s.B.Y - s.A.Y) * t;
                float bank = level + 1.2f + Math.Max(0f, d - s.Half) * 0.06f + Math.Max(0f, d - s.Half - 150f) * Math.Max(0f, d - s.Half - 150f) * 0.0004f;
                if (bank < h) h = bank;
            }
            return h;
        }

        // ================================================================== roads

        private readonly List<(RoadPath Road, float[] Heights)> _graded = new List<(RoadPath, float[])>();

        private void DefineRoads()
        {
            RoadPath Road(float width, bool paved, params float[] pts)
            {
                var r = new RoadPath { Width = width, Paved = paved };
                WorldGenerator.AddPoints(r, pts);
                return r;
            }
            var roads = new List<RoadPath>
            {
                Road(14f, true, 2047, 572, 5000, 1500, 9000, 3800, 14000, 9000, 18000, 16000, 19000, 22000, 21500, 27000, 25000, 29000, 30000, 29200, 35000, 29600, 40000, 30000, 45000, 29000, 50000, 28000, 55500, 28500),
                Road(9f, true, 14000, 9000, 25000, 7000, 35000, 8500, 45000, 10500, 53000, 12000, 56000, 9000),
                Road(9f, true, 40000, 30000, 40500, 38000, 40000, 44000, 40000, 50000, 36000, 54000, 31000, 55000),
                Road(8f, true, 40000, 50000, 45000, 51000, 50500, 51500, 55000, 50000, 57000, 44000, 55000, 36000, 50000, 28000),
                Road(7f, false, 520, 1620, 1500, 4500, 3500, 9000, 5000, 15000, 6500, 22000, 8000, 28000, 10500, 34000, 14500, 37000),
                Road(8f, true, 6500, 22000, 12000, 21000, 19000, 22000),
                Road(6f, false, 1450, 225, 5000, 2600, 9000, 4500, 12000, 5500),
                Road(8f, true, 45000, 10500, 50000, 15000, 53000, 19000, 52000, 24000, 50000, 28000),
                Road(8f, true, 21500, 27000, 27000, 18000, 35000, 8500)
            };
            // Kesselmouth: two ring roads and radial avenues.
            roads.Add(Ring(4500f, 24, 12f));
            roads.Add(Ring(12000f, 48, 12f));
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4 + 0.2;
                var r = new RoadPath { Width = 12f, Paved = true };
                for (float d = 600f; d <= 19500f; d += 900f)
                {
                    float x = CityCenter.X + (float)Math.Cos(a) * d, z = CityCenter.Z + (float)Math.Sin(a) * d;
                    if (x > CoastX(z) - 700f) break;
                    r.Points.Add(new Vec3(x, 0, z));
                }
                if (r.Points.Count > 1) roads.Add(r);
            }
            // Rail: west to Harrowgate, north to Kingsmoor, east to the docks.
            RoadPath Rail(params float[] pts)
            {
                var r = Road(4f, false, pts);
                r.Rail = true;
                return r;
            }
            roads.Add(Rail(40000, 30300, 33000, 30800, 26000, 28500, 21000, 24000, 18500, 17000, 14200, 9300));
            roads.Add(Rail(40000, 30300, 41500, 38500, 41000, 45000, 40400, 50000));
            roads.Add(Rail(40000, 30300, 46000, 29600, 51000, 28900, 55000, 29000));
            foreach (RoadPath r in roads) Grade(r);
        }

        private RoadPath Ring(float radius, int n, float width)
        {
            var r = new RoadPath { Width = width, Paved = true };
            for (int i = 0; i <= n; i++)
            {
                double a = i * Math.PI * 2 / n;
                float x = CityCenter.X + (float)Math.Cos(a) * radius, z = CityCenter.Z + (float)Math.Sin(a) * radius;
                r.Points.Add(new Vec3(Math.Min(x, CoastX(z) - 800f), 0, z));
            }
            return r;
        }

        /// <summary>
        /// Grades a road as in the region generator: short lengths, pinned at crossings with earlier
        /// roads (their height) and rivers (5 m above the water, with a bridge), and on village pads;
        /// grades limited; heights recorded for the terrain tiles and the bridges.
        /// </summary>
        private void Grade(RoadPath r)
        {
            WorldGenerator.Subdivide(r, 24f);
            var pts = new List<Vec3>();
            var pinnedHeights = new List<float>();
            var bridgesHere = new List<(Vec3 P, float Yaw, float Length)>();
            for (int i = 0; i + 1 < r.Points.Count; i++)
            {
                Vec3 a = r.Points[i], b = r.Points[i + 1];
                var hits = new List<(float T, float H)>();
                var seen = new HashSet<int>();
                foreach (int id in SegsNear(a, b, seen))
                {
                    Seg s = _segs[id];
                    if (WorldGenerator.IntersectTU(a, b, s.A, s.B, out float t, out float u)) hits.Add((t, s.HA + (s.HB - s.HA) * u));
                }
                foreach (RiverPath river in _layout.Rivers)
                    for (int j = 0; j + 1 < river.Points.Count; j++)
                    {
                        Vec3 c = river.Points[j], d = river.Points[j + 1];
                        if (Math.Max(c.X, d.X) < Math.Min(a.X, b.X) || Math.Min(c.X, d.X) > Math.Max(a.X, b.X) || Math.Max(c.Z, d.Z) < Math.Min(a.Z, b.Z) || Math.Min(c.Z, d.Z) > Math.Max(a.Z, b.Z)) continue;
                        if (!WorldGenerator.IntersectTU(a, b, c, d, out float t, out float u)) continue;
                        float level = c.Y + (d.Y - c.Y) * u;
                        hits.Add((t, level + 5f));
                        Vec3 rd = (b - a).Normalized, wd = (d - c).Normalized;
                        float sin = Math.Max(0.5f, Math.Abs(rd.X * wd.Z - rd.Z * wd.X));
                        float length = (river.Width * 0.5f + 13f) * 2f / sin + 2f;
                        bridgesHere.Add((Vec3.Lerp(a, b, t), (float)(Math.Atan2(b.X - a.X, b.Z - a.Z) * 180.0 / Math.PI), length));
                    }
                hits.Sort((p, q) => p.T.CompareTo(q.T));
                pts.Add(a);
                pinnedHeights.Add(hits.Count > 0 && hits[0].T < 1e-3f ? hits[0].H : float.NaN);
                foreach ((float t, float h) in hits)
                {
                    if (t < 1e-3f || t > 1f - 1e-3f) continue;
                    pts.Add(Vec3.Lerp(a, b, t));
                    pinnedHeights.Add(h);
                }
                if (i + 2 == r.Points.Count)
                {
                    pts.Add(b);
                    pinnedHeights.Add(hits.Count > 0 && hits[hits.Count - 1].T > 1f - 1e-3f ? hits[hits.Count - 1].H : float.NaN);
                }
            }
            r.Points.Clear();
            r.Points.AddRange(pts);
            var anchors = new float[pts.Count];
            var pinned = new bool[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                if (!float.IsNaN(pinnedHeights[i]))
                {
                    anchors[i] = pinnedHeights[i];
                    pinned[i] = true;
                    continue;
                }
                anchors[i] = Math.Max(CoarseHeight(pts[i].X, pts[i].Z), Sea + 2f);
                foreach (Village v in _villages)
                    if (Vec3.DistanceXZ(pts[i], v.Center) < v.Pad * 0.8f)
                    {
                        anchors[i] = PadHeight(v);
                        pinned[i] = true;
                    }
                if (pts[i].X <= BlockSize && pts[i].Z <= BlockSize)
                {
                    anchors[i] = _region.Height.Sample(pts[i].X, pts[i].Z);
                    pinned[i] = true;
                }
            }
            WorldGenerator.LimitGrade(r, anchors, pinned, r.Rail ? 0.04f : 0.09f);
            for (int i = 0; i < pts.Count; i++) r.Points[i] = new Vec3(pts[i].X, anchors[i], pts[i].Z);
            _graded.Add((r, anchors));
            _layout.Roads.Add(r);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                int id = _segs.Count;
                _segs.Add(new Seg { A = r.Points[i], B = r.Points[i + 1], HA = anchors[i], HB = anchors[i + 1], Half = r.Width * 0.5f, Rail = r.Rail, Paved = r.Paved });
                AddToIndex(_segIndex, SegCell, r.Points[i], r.Points[i + 1], r.Width * 0.5f + 12f, id);
            }
            foreach ((Vec3 p, float yaw, float length) in bridgesHere)
            {
                double ang = yaw * Math.PI / 180.0;
                float dx = (float)Math.Sin(ang), dz = (float)Math.Cos(ang);
                float k = length * 0.5f + 2f;
                float front = RoadHeightAt(r, anchors, p.X + dx * k, p.Z + dz * k), back = RoadHeightAt(r, anchors, p.X - dx * k, p.Z - dz * k);
                _bridges.Add(new PropPlacement
                {
                    Kind = "bridge", Position = new Vec3(p.X, (front + back) * 0.5f, p.Z), Yaw = yaw, Size = new Vec3(r.Width + 2f, 1.2f, length),
                    Pitch = (float)(Math.Atan2(front - back, length) * 180.0 / Math.PI), Variant = r.Rail ? 1 : 0
                });
            }
        }

        private IEnumerable<int> SegsNear(Vec3 a, Vec3 b, HashSet<int> seen)
        {
            int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / SegCell), x1 = (int)Math.Floor(Math.Max(a.X, b.X) / SegCell);
            int z0 = (int)Math.Floor(Math.Min(a.Z, b.Z) / SegCell), z1 = (int)Math.Floor(Math.Max(a.Z, b.Z) / SegCell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!_segIndex.TryGetValue(((long)x << 32) ^ (uint)z, out List<int> list)) continue;
                    foreach (int id in list)
                        if (seen.Add(id)) yield return id;
                }
        }

        private static float RoadHeightAt(RoadPath r, float[] h, float x, float z)
        {
            float best = float.MaxValue, height = 0f;
            for (int i = 0; i + 1 < r.Points.Count; i++)
            {
                float d = WorldLayout.DistanceToSegmentXZ(x, z, r.Points[i], r.Points[i + 1], out float t);
                if (d >= best) continue;
                best = d;
                height = h[i] + (h[i + 1] - h[i]) * t;
            }
            return height;
        }

        private float PadHeight(Village v) => Math.Max(Sea + 2f, Valley(v.Center.X, v.Center.Z, Terrain(v.Center.X, v.Center.Z)));

        // ================================================================== map heights

        private float[] _mapGrid;
        private const int MapGridRes = 1025;
        private readonly object _mapLock = new object();

        /// <summary>Coarse heights on a 64 m grid for the world map (made once, on first use, ~1 s on a worker).</summary>
        public float MapHeight(float x, float z)
        {
            float[] g = _mapGrid;
            if (g == null)
            {
                lock (_mapLock)
                {
                    if (_mapGrid == null)
                    {
                        var grid = new float[MapGridRes * MapGridRes];
                        float cell = WorldSize / (MapGridRes - 1);
                        System.Threading.Tasks.Parallel.For(0, MapGridRes, zi =>
                        {
                            for (int xi = 0; xi < MapGridRes; xi++) grid[zi * MapGridRes + xi] = CoarseHeight(xi * cell, zi * cell);
                        });
                        Volatile.Write(ref _mapGrid, grid);
                    }
                    g = _mapGrid;
                }
            }
            float c = WorldSize / (MapGridRes - 1);
            float fx = Math.Max(0f, Math.Min(MapGridRes - 1.001f, x / c)), fz = Math.Max(0f, Math.Min(MapGridRes - 1.001f, z / c));
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float a = g[iz * MapGridRes + ix] + (g[iz * MapGridRes + ix + 1] - g[iz * MapGridRes + ix]) * tx;
            float b = g[(iz + 1) * MapGridRes + ix] + (g[(iz + 1) * MapGridRes + ix + 1] - g[(iz + 1) * MapGridRes + ix]) * tx;
            return a + (b - a) * tz;
        }
    }
}
