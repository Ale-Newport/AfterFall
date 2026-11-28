using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Kesselmouth's interiors: railway stations along the lines out of the city, and blocks of flats
    /// and supermarkets that replace some of the city's plain facades. All of it is laid after a
    /// chunk's other content, from its own random stream, so nothing generated before moves.
    /// </summary>
    public sealed partial class BasinWorld
    {
        private sealed class StationSite
        {
            public string Name = "";
            public string Uid = "";
            public Vec3 Track;   // on the track's centre line, at the middle of the platform
            public Vec3 Along;   // unit direction of the track
            public Vec3 Side;    // unit direction from the track to the building
            public Vec3 Centre => Track + Side * StationOffset;
            public Vec3 PlatformCentre => Track + Side * PlatformOffset;
        }

        /// <summary>Distance between stations along a line, and of the platform and building from the track's centre.</summary>
        public const float StationSpacing = 2400f;
        private const float PlatformOffset = 4.6f, PlatformWidth = 5f, PlatformLength = 72f, StationOffset = 13.1f;
        private const float StationHalfWidth = 14f, StationHalfDepth = 6f;

        private readonly List<StationSite> _stations = new List<StationSite>();

        /// <summary>Stations along the rail lines (for tests and the tour): building centre and name.</summary>
        public IEnumerable<(Vec3 Centre, string Name)> Stations
        {
            get
            {
                foreach (StationSite s in _stations) yield return (s.Centre, s.Name);
            }
        }

        /// <summary>Walks each rail line and places a station every <see cref="StationSpacing"/> where it runs through the city or a town.</summary>
        private void DefineStations()
        {
            var lines = new List<List<Seg>>();
            foreach (Seg s in _segs)
            {
                if (!s.Rail) continue;
                List<Seg> line = lines.Count > 0 ? lines[lines.Count - 1] : null;
                if (line == null || Vec3.DistanceXZ(line[line.Count - 1].B, s.A) > 0.5f) lines.Add(line = new List<Seg>());
                line.Add(s);
            }
            int n = 0;
            for (int li = 0; li < lines.Count; li++)
            {
                List<Seg> line = lines[li];
                float total = 0f;
                foreach (Seg s in line) total += Vec3.DistanceXZ(s.A, s.B);
                var wanted = new List<float>();
                // The central station where the lines meet (once), then every few kilometres, and the line's end.
                if (li == 0) wanted.Add(220f);
                for (float d = StationSpacing; d < total - 600f; d += StationSpacing) wanted.Add(d);
                wanted.Add(total - 160f);
                foreach (float d0 in wanted)
                {
                    StationSite site = null;
                    // Slide along the line a little to find a spot clear of the city's streets and other roads.
                    bool end = d0 > total - 200f;
                    for (int k = 0; k < (end ? 31 : 13) && site == null; k++)
                    {
                        float d = d0 + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 18f;
                        if (d < 60f || d > total - 40f) continue;
                        Vec3 p = PointAlong(line, d), ahead = PointAlong(line, d + 36f), behind = PointAlong(line, d - 36f);
                        Vec3 along = (ahead - behind).Normalized;
                        if (!InTownOrCity(p)) break;
                        foreach (int side in new[] { 1, -1 })
                        {
                            var sideDir = new Vec3(along.Z * side, 0, -along.X * side);
                            var candidate = new StationSite { Track = p, Along = along, Side = sideDir };
                            if (StationClear(candidate))
                            {
                                site = candidate;
                                break;
                            }
                        }
                    }
                    if (site == null) continue;
                    bool near = false;
                    foreach (StationSite other in _stations)
                        if (Vec3.DistanceXZ(other.Track, site.Track) < 1200f) near = true;
                    if (near) continue;
                    site.Uid = $"st{n++}";
                    site.Name = StationName(site.Centre);
                    _stations.Add(site);
                }
            }
        }

        private static Vec3 PointAlong(List<Seg> line, float d)
        {
            foreach (Seg s in line)
            {
                float len = Vec3.DistanceXZ(s.A, s.B);
                if (d <= len) return Vec3.Lerp(s.A, s.B, len > 0f ? Math.Max(0f, d) / len : 0f);
                d -= len;
            }
            return line[line.Count - 1].B;
        }

        private bool InTownOrCity(Vec3 p)
        {
            if (CityDensity(p.X, p.Z) > 0.15f) return true;
            foreach (Village v in _villages)
                if (v.Kind == "town" && Vec3.DistanceXZ(v.Center, p) < v.Radius + 700f) return true; // the line ends at the town's edge
            return false;
        }

        /// <summary>Clear of water, roads (other than the rail), and the city's streets.</summary>
        private bool StationClear(StationSite s)
        {
            Vec3 c = s.Centre, pc = s.PlatformCentre;
            var points = new List<Vec3>
            {
                c, c + s.Along * StationHalfWidth, c - s.Along * StationHalfWidth,
                c + s.Along * StationHalfWidth + s.Side * StationHalfDepth, c - s.Along * StationHalfWidth + s.Side * StationHalfDepth,
                pc + s.Along * (PlatformLength * 0.5f), pc - s.Along * (PlatformLength * 0.5f)
            };
            foreach (Vec3 p in points)
            {
                if (OverWater(p.X, p.Z, 4f) || NearOtherRoad(p.X, p.Z, 3f)) return false;
                if (CityDensity(p.X, p.Z) > 0.06f && NearCityStreet(p.X, p.Z, 8f)) return false;
            }
            return true;
        }

        /// <summary>Near a planned road that is not a railway.</summary>
        private bool NearOtherRoad(float x, float z, float clearance)
        {
            long k = ((long)(int)Math.Floor(x / SegCell) << 32) ^ (uint)(int)Math.Floor(z / SegCell);
            if (!_segIndex.TryGetValue(k, out List<int> list)) return false;
            foreach (int id in list)
            {
                Seg s = _segs[id];
                if (s.Rail) continue;
                if (WorldLayout.DistanceToSegmentXZ(x, z, s.A, s.B, out _) < s.Half + clearance) return true;
            }
            return false;
        }

        /// <summary>Within about <paramref name="metres"/> of one of the city's streets (where U or V is a whole number).</summary>
        private bool NearCityStreet(float x, float z, float metres)
        {
            Fields(x, z, out float u, out float v);
            float du = Math.Abs(u - (float)Math.Round(u)), dv = Math.Abs(v - (float)Math.Round(v));
            return Math.Min(du, dv) * GridSpacing < metres;
        }

        private static readonly string[] StationSuffixes = { "Road", "Park", "Junction", "Hill", "Cross", "Green", "Lane", "Fields" };
        private readonly Dictionary<string, int> _stationNames = new Dictionary<string, int>();

        /// <summary>A town's station, the central one, or one named after its district (the second and later with a suffix).</summary>
        private string StationName(Vec3 p)
        {
            foreach (Village v in _villages)
                if (v.Kind == "town" && Vec3.DistanceXZ(v.Center, p) < v.Radius + 1200f) return v.Name + " Station";
            if (Vec3.DistanceXZ(p, CityCenter) < 900f) return "Kesselmouth Central";
            string best = "Kesselmouth";
            float bestD = float.MaxValue;
            foreach ((string id, string name, float dx, float dz, string desc) in CityDistricts)
            {
                float d = Vec3.DistanceXZ(p, new Vec3(dx, 0, dz));
                if (d < bestD)
                {
                    bestD = d;
                    best = name;
                }
            }
            _stationNames.TryGetValue(best, out int k);
            _stationNames[best] = k + 1;
            return k == 0 ? best + " Station" : $"{best} {StationSuffixes[(k - 1) % StationSuffixes.Length]} Station";
        }

        // ------------------------------------------------------------------ per chunk

        private static readonly string[] FlatNames =
        {
            "Albion", "Beaumont", "Carlisle", "Dunmore", "Elmwood", "Fairfax", "Garnet", "Hollis", "Ingram", "Juniper", "Kestrel", "Linden",
            "Merton", "Norwood", "Oakley", "Prospect", "Quarry", "Rowan", "Sefton", "Thornbury", "Upland", "Vale", "Wexford", "Yardley"
        };
        private static readonly string[] FlatSuffixes = { "House", "Court", "Mansions", "Buildings", "Point" };
        private static readonly string[] MarketNames = { "Aldwater Foods", "Marlow's Market", "Penny Basket", "Greystone Grocers", "Kessel Co-operative", "Harbour Fresh" };

        /// <summary>
        /// Adds this chunk's stations, and turns a few of its plain city buildings into blocks of flats
        /// and supermarkets that can be entered. Runs after everything else in the chunk.
        /// </summary>
        private void PopulateInteriors(WorldLayout layout, ChunkContent c)
        {
            var rng = DeterministicRandom.Derive(_seed, $"interiors:{c.X}:{c.Z}");
            float x0 = c.MinX, z0 = c.MinZ, x1 = x0 + WorldLayout.ChunkSize, z1 = z0 + WorldLayout.ChunkSize;
            foreach (StationSite s in _stations)
            {
                Vec3 sc = s.Centre;
                if (sc.X < x0 - 160f || sc.X > x1 + 160f || sc.Z < z0 - 160f || sc.Z > z1 + 160f) continue;
                ClearForStation(c, s);
                if (sc.X >= x0 && sc.X < x1 && sc.Z >= z0 && sc.Z < z1) AddStation(layout, c, s);
            }

            int flats = 0, markets = 0;
            for (int i = 0; i < c.Blocks.Count; i++)
            {
                PropPlacement b = c.Blocks[i];
                string uid = $"{(int)Math.Round(b.Position.X)}_{(int)Math.Round(b.Position.Z)}";
                BuildingPlan plan = null;
                if (b.Kind == "city_midrise" && b.Size.X >= 16.5f && b.Size.Z >= 12f && flats < 1 && rng.NextFloat() < 0.03f)
                {
                    int floors = Math.Max(3, Math.Min(6, (int)Math.Round((b.Size.Y - 1f) / 3.2f)));
                    string name = FlatNames[rng.Range(0, FlatNames.Length)] + " " + FlatSuffixes[rng.Range(0, FlatSuffixes.Length)];
                    plan = BuildingPlans.Get(BuildingPlans.CityPlanId(BuildingPlans.FlatsKind, uid, name, floors));
                    flats++;
                }
                else if ((b.Kind == "city_shed" || b.Kind == "city_tower") && b.Size.X >= 32.5f && b.Size.Z >= 22.5f && markets < 1 && rng.NextFloat() < 0.2f)
                {
                    string name = MarketNames[rng.Range(0, MarketNames.Length)];
                    plan = BuildingPlans.Get(BuildingPlans.CityPlanId(BuildingPlans.SupermarketKind, uid, name, 1));
                    markets++;
                }
                if (plan == null) continue;
                // The building keeps the facade's front line; it is no deeper than the facade it replaces.
                double r = b.Yaw * Math.PI / 180.0;
                var front = new Vec3((float)Math.Sin(r), 0, (float)Math.Cos(r));
                Vec3 at = b.Position + front * ((b.Size.Z - plan.Depth) * 0.5f);
                AddPlan(c, plan, new Vec3(at.X, b.Position.Y, at.Z), b.Yaw, "kesselmouth");
                c.Blocks.RemoveAt(i);
                i--;
            }
        }

        private void AddStation(WorldLayout layout, ChunkContent c, StationSite s)
        {
            Vec3 sc = s.Centre;
            float ground = CityDensity(sc.X, sc.Z) > 0.06f ? CoarseHeight(sc.X, sc.Z) : layout.HeightAt(sc.X, sc.Z);
            float yaw = (float)(Math.Atan2(s.Side.X, s.Side.Z) * 180.0 / Math.PI); // the front faces away from the track
            BuildingPlan plan = BuildingPlans.Get(BuildingPlans.CityPlanId(BuildingPlans.StationKind, s.Uid, s.Name, 1));
            AddPlan(c, plan, new Vec3(sc.X, ground, sc.Z), yaw, "kesselmouth");
            // The platform: its top level with the station's floor, along the track.
            Vec3 pc = s.PlatformCentre;
            float trackYaw = (float)(Math.Atan2(s.Along.X, s.Along.Z) * 180.0 / Math.PI);
            c.Props.Add(new PropPlacement
            {
                Kind = "platform", Position = new Vec3(pc.X, ground + plan.GroundFloorY, pc.Z), Yaw = trackYaw,
                Size = new Vec3(PlatformWidth, 1.2f, PlatformLength), Variant = s.Side.X * s.Along.Z - s.Side.Z * s.Along.X > 0f ? 1 : 0, // 1: the building on the platform's right (+X), the track on its left
                PoiId = "kesselmouth"
            });
        }

        /// <summary>A planned building in a chunk: its shell as a prop, its doors and furniture as keyed fixtures.</summary>
        private static void AddPlan(ChunkContent c, BuildingPlan plan, Vec3 origin, float yaw, string poi)
        {
            var placed = new PlacedPlan { Plan = plan, Origin = origin, Yaw = yaw, PoiId = poi };
            c.Plans.Add(placed);
            c.Props.Add(new PropPlacement { Kind = "apartment", Position = origin, Yaw = yaw, Size = new Vec3(plan.Width, plan.TotalHeight, plan.Depth), PoiId = poi, PlanId = plan.Id });
            foreach (PlanFixture f in plan.Fixtures)
            {
                var seed = new FixtureSeed
                {
                    Key = f.Key, DefId = f.DefId, Position = placed.ToWorld(f.X, f.Z, f.Floor), Yaw = yaw + f.Yaw, PoiId = poi,
                    LootTable = f.LootTable, LockId = f.LockId, Locked = f.Locked
                };
                seed.KeysInside.AddRange(f.KeysInside);
                seed.ItemsInside.AddRange(f.ItemsInside);
                c.Fixtures.Add(seed);
            }
        }

        /// <summary>Removes this chunk's buildings, props, containers and nodes that stand on a station or its platform.</summary>
        private static void ClearForStation(ChunkContent c, StationSite s)
        {
            Vec3 c0 = s.Centre - s.Along * StationHalfWidth, c1 = s.Centre + s.Along * StationHalfWidth;
            Vec3 p0 = s.PlatformCentre - s.Along * (PlatformLength * 0.5f), p1 = s.PlatformCentre + s.Along * (PlatformLength * 0.5f);
            bool Hits(Vec3 p, float radius) =>
                WorldLayout.DistanceToSegmentXZ(p.X, p.Z, c0, c1, out _) < StationHalfDepth + 2f + radius ||
                WorldLayout.DistanceToSegmentXZ(p.X, p.Z, p0, p1, out _) < PlatformWidth * 0.5f + 1f + radius;
            float Radius(PropPlacement b) => Math.Max(b.Size.X, b.Size.Z) * 0.5f;

            c.Blocks.RemoveAll(b => Hits(b.Position, Radius(b)));
            var cleared = new List<(Vec3 P, float R)>();
            c.Props.RemoveAll(p =>
            {
                if (p.Kind == "rail" || p.Kind == "bridge" || p.Kind == "platform" || p.Kind == "apartment") return false;
                if (!Hits(p.Position, Radius(p))) return false;
                cleared.Add((p.Position, Radius(p) + 1f));
                return true;
            });
            c.Containers.RemoveAll(k =>
            {
                if (Hits(k.Position, 0.8f)) return true;
                foreach ((Vec3 p, float r) in cleared)
                    if (Vec3.DistanceXZ(p, k.Position) < r) return true;
                return false;
            });
            c.Nodes.RemoveAll(n => Hits(n.Position, 2f));
        }
    }
}
