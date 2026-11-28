using System;
using System.Collections.Generic;
using System.Globalization;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Every building of Kesselmouth as a real building: towers, mansion blocks, terraces, semis,
    /// sheds and churches each get a <see cref="BuildingPlan"/> with every floor, made from the city
    /// block that stands there (deterministic, rebuilt from the plan id). Floors are 3.6 m and the
    /// facades a grid of 3.6 m bays with a window in each, so the textured facade seen from afar
    /// lines up with the real windows. A straight stair flight rises in the core on every floor
    /// (flights stack, all rising the same way), tall buildings have a lift, and every floor is laid
    /// out and furnished for what it is: a lobby, offices, flats, hotel rooms, a shop, a house, a
    /// warehouse, a nave. Rooms, colours, furniture and locked doors vary per building and floor.
    /// </summary>
    public static partial class BuildingPlans
    {
        public const string CityBlockKind = "cb";
        public const float CityFloorHeight = 3.6f, CityBay = 3.6f;
        private const float Ext = 0.3f, Part = 0.15f, StairWidth = 1.55f, StairRun = 5.4f, StairMargin = 1.2f;

        /// <summary>The outside of a city building: what the far views draw and the plan matches.</summary>
        public struct CityShape
        {
            public string Kind;
            public float Width, Depth, FloorHeight;
            public int Floors;

            /// <summary>"brick", "render", "concrete", "stone" or "corrugated" (no windows).</summary>
            public string Facade;

            /// <summary>Colour variant of the facade (0..7).</summary>
            public int Tint;

            public bool Lift;

            /// <summary>Height of the roof's top (the parapet's top is <see cref="ParapetHeight"/> above).</summary>
            public float RoofY => 0.15f + Floors * FloorHeight;
            public const float ParapetHeight = 0.9f;
            public float TotalHeight => RoofY + ParapetHeight;
        }

        public static bool IsCityBlock(string kind) =>
            kind == "city_tower" || kind == "city_midrise" || kind == "city_terrace" || kind == "city_semi" || kind == "city_shed" || kind == "city_church";

        private static float SnapBays(float length, float min) => Math.Max(min, (float)Math.Floor(length / 1.8f + 1e-3f) * 1.8f);

        /// <summary>The real building's outside for a city block.</summary>
        public static CityShape ShapeOf(string kind, float sizeX, float sizeY, float sizeZ, int variant)
        {
            var s = new CityShape { Kind = kind, FloorHeight = CityFloorHeight, Tint = variant & 7 };
            switch (kind)
            {
                case "city_tower":
                    s.Width = SnapBays(sizeX, 9f);
                    s.Depth = SnapBays(sizeZ, 9f);
                    s.Floors = Clamp((int)Math.Round((sizeY - 1.05f) / CityFloorHeight), 4, 72);
                    s.Facade = (variant & 3) == 0 ? "concrete" : (variant & 3) == 1 ? "stone" : (variant & 3) == 2 ? "brick" : "render";
                    s.Lift = true;
                    break;
                case "city_midrise":
                    s.Width = SnapBays(sizeX, 9f);
                    s.Depth = SnapBays(sizeZ, 9f);
                    s.Floors = Clamp((int)Math.Round((sizeY - 1.05f) / CityFloorHeight), 3, 10);
                    s.Facade = (variant & 1) == 0 ? "brick" : "stone";
                    s.Lift = s.Floors >= 5;
                    break;
                case "city_terrace":
                    s.Width = SnapBays(sizeX, 9f);
                    s.Depth = SnapBays(sizeZ, 9f);
                    s.Floors = Clamp((int)Math.Round((sizeY - 0.6f) / CityFloorHeight), 2, 4);
                    s.Facade = (variant & 7) == 4 || (variant & 7) == 7 ? "render" : "brick";
                    break;
                case "city_semi":
                    s.Width = SnapBays(sizeX, 9f);
                    s.Depth = SnapBays(sizeZ, 9f);
                    s.Floors = 2;
                    s.Facade = (variant & 1) == 0 ? "render" : "brick";
                    break;
                case "city_shed":
                    s.Width = SnapBays(sizeX, 10.8f);
                    s.Depth = SnapBays(sizeZ, 9f);
                    s.Floors = 1;
                    s.FloorHeight = Math.Max(4.5f, Math.Min(9f, sizeY - 0.8f));
                    s.Facade = "corrugated";
                    break;
                default: // church
                    s.Width = SnapBays(sizeX, 10.8f);
                    s.Depth = SnapBays(sizeZ, 18f);
                    s.Floors = 1;
                    s.FloorHeight = 9f;
                    s.Facade = "stone";
                    break;
            }
            return s;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        public static CityShape ShapeOf(PropPlacement b) => ShapeOf(b.Kind, b.Size.X, b.Size.Y, b.Size.Z, b.Variant);

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>A unit's corner as a key part, the same in every culture.</summary>
        private static string U(float x, float z) =>
            Math.Round(x, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "_" + Math.Round(z, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        /// <summary>The plan id of a city block's building: everything needed to make it again.</summary>
        public static string CityBlockPlanId(PropPlacement b) =>
            $"{CityBlockKind}|{b.Kind}|{F(b.Position.X)}_{F(b.Position.Z)}|{F(b.Size.X)}|{F(b.Size.Y)}|{F(b.Size.Z)}|{b.Variant}";

        /// <summary>The building of a city block, placed: centred on the block, its front on the block's front line.</summary>
        public static PlacedPlan PlaceCityBlock(PropPlacement b)
        {
            BuildingPlan plan = Get(CityBlockPlanId(b));
            return new PlacedPlan { Plan = plan, Origin = CityOrigin(b, ShapeOf(b)), Yaw = b.Yaw, PoiId = b.PoiId ?? "" };
        }

        /// <summary>Where a city block's building stands (its plan origin), without making its plan.</summary>
        public static Vec3 CityOrigin(PropPlacement b, CityShape s)
        {
            double r = b.Yaw * Math.PI / 180.0;
            var front = new Vec3((float)Math.Sin(r), 0, (float)Math.Cos(r));
            Vec3 o = b.Position + front * ((b.Size.Z - s.Depth) * 0.5f);
            return new Vec3(o.X, b.Position.Y, o.Z);
        }

        /// <summary>Where on a facade of <paramref name="length"/> the bay grid starts (bays are centred on the wall).</summary>
        public static float BayOffset(float length) => (float)Math.Round((length % CityBay) * 0.5f, 3);

        private static readonly Dictionary<string, BuildingPlan> CityCache = new Dictionary<string, BuildingPlan>();
        private static readonly LinkedList<string> CityCacheOrder = new LinkedList<string>();

        private static BuildingPlan CityBlockPlan(string id, string[] parts)
        {
            lock (CityCache)
            {
                if (CityCache.TryGetValue(id, out BuildingPlan cached)) return cached;
            }
            if (parts.Length < 7) return null;
            float P(string v) => float.Parse(v, CultureInfo.InvariantCulture);
            BuildingPlan plan = MakeCityBuilding(id, parts[1], P(parts[3]), P(parts[4]), P(parts[5]), int.Parse(parts[6], CultureInfo.InvariantCulture));
            lock (CityCache)
            {
                CityCache[id] = plan;
                CityCacheOrder.AddLast(id);
                while (CityCacheOrder.Count > 48)
                {
                    CityCache.Remove(CityCacheOrder.First.Value);
                    CityCacheOrder.RemoveFirst();
                }
            }
            return plan;
        }

        // ------------------------------------------------------------------ generation

        private enum FloorUse { Lobby, Offices, Flats, Hotel, Shop, Cafe, HouseGround, HouseUpper, Warehouse, Nave }

        private static readonly string[] Streets = { "Albany", "Bishop's", "Carver", "Dalton", "Elm", "Foundry", "Granary", "Harbour", "Iron", "King's", "Lantern", "Mercer", "North", "Orchard", "Pilgrim", "Queen's", "Rope", "Salt", "Tanner", "Union", "Vine", "Weaver" };

        private static BuildingPlan MakeCityBuilding(string id, string kind, float sizeX, float sizeY, float sizeZ, int variant)
        {
            CityShape s = ShapeOf(kind, sizeX, sizeY, sizeZ, variant);
            var rng = DeterministicRandom.Derive(9091, id);
            string street = Streets[rng.Range(0, Streets.Length)];
            string name = kind switch
            {
                "city_tower" => $"{street} Tower",
                "city_midrise" => $"{street} Mansions",
                "city_terrace" => $"{1 + rng.Range(0, 180)} {street} Row",
                "city_semi" => $"{1 + rng.Range(0, 220)} {street} Avenue",
                "city_shed" => $"{street} Wharf Store",
                _ => $"St {street}'s Church"
            };
            var p = new BuildingPlan
            {
                Id = id, Name = name, Width = s.Width, Depth = s.Depth, Floors = s.Floors, FloorHeight = s.FloorHeight,
                Facade = s.Facade, FacadeTint = s.Tint, Lift = s.Lift
            };
            bool shops = (variant & 8) != 0;
            // What each floor is.
            var uses = new FloorUse[s.Floors];
            int towerKind = rng.Range(0, 3); // offices, flats or a hotel above the lobby
            for (int f = 0; f < s.Floors; f++)
            {
                switch (kind)
                {
                    case "city_tower":
                        if (f == 0) uses[f] = FloorUse.Lobby;
                        else if (towerKind == 0) uses[f] = f % 7 == 3 ? FloorUse.Flats : FloorUse.Offices; // offices, with the odd residential floor
                        else if (towerKind == 1) uses[f] = f <= 2 ? FloorUse.Offices : FloorUse.Flats;
                        else uses[f] = f == 1 ? FloorUse.Cafe : FloorUse.Hotel;
                        break;
                    case "city_midrise":
                        uses[f] = f == 0 ? (shops || rng.NextFloat() < 0.4f ? (rng.NextFloat() < 0.35f ? FloorUse.Cafe : FloorUse.Shop) : FloorUse.Flats) : FloorUse.Flats;
                        break;
                    case "city_terrace":
                    case "city_semi":
                        uses[f] = f == 0 ? (shops ? FloorUse.Shop : FloorUse.HouseGround) : FloorUse.HouseUpper;
                        break;
                    case "city_shed":
                        uses[f] = FloorUse.Warehouse;
                        break;
                    default:
                        uses[f] = FloorUse.Nave;
                        break;
                }
            }

            if (kind == "city_church") Church(p, rng);
            else if (kind == "city_shed") Warehouse(p, rng);
            else if (kind == "city_terrace" || kind == "city_semi")
            {
                List<float> houses = HouseEdges(s.Width, kind == "city_semi");
                int number = int.Parse(name.Substring(0, name.IndexOf(' ')), CultureInfo.InvariantCulture);
                if (houses.Count > 2) p.Name = $"{number}–{number + 2 * (houses.Count - 2)} {name.Substring(name.IndexOf(' ') + 1)}";
                Storeys(p, uses, rng, houses, number);
            }
            else Storeys(p, uses, rng, null, 0);
            return p;
        }

        /// <summary>Bay-centred window positions along a wall of <paramref name="length"/> (u from its start).</summary>
        private static List<float> BayCentres(float length)
        {
            var list = new List<float>();
            float off = BayOffset(length);
            for (float u = off + CityBay * 0.5f; u + 0.8f <= length - 0.35f; u += CityBay)
                if (u - 0.8f >= 0.35f) list.Add(u);
            return list;
        }

        /// <summary>Windows in every bay of a floor's outer walls, except where an inner wall, a door or the lift meets the wall.</summary>
        private static void Windows(BuildingPlan p, int floor, float bottom = 0.9f, float top = 2.3f)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            var blockers = new List<(bool AlongX, float Wall, float At)>();
            foreach (PlanWall w in p.Walls)
            {
                if (w.Floor != floor || w.Exterior) continue;
                foreach ((float x, float z) in new[] { (w.X0, w.Z0), (w.X1, w.Z1) })
                {
                    if (Math.Abs(Math.Abs(z) - (hz - Ext)) < 0.2f) blockers.Add((true, Math.Sign(z), x));
                    if (Math.Abs(Math.Abs(x) - (hx - Ext)) < 0.2f) blockers.Add((false, Math.Sign(x), z));
                }
            }
            foreach (PlanDecor d in p.Decor)
                if (d.Floor == floor && d.Kind == "lift") blockers.Add((false, Math.Sign(d.X), d.Z));
            foreach (PlanWall w in p.Walls)
            {
                if (w.Floor != floor || !w.Exterior) continue;
                bool alongX = w.AlongX;
                float side = alongX ? Math.Sign(w.Z0) : Math.Sign(w.X0);
                float start = alongX ? Math.Min(w.X0, w.X1) : Math.Min(w.Z0, w.Z1);
                // The bay grid runs from the building's corner (side walls start inside the front and back walls).
                float corner = alongX ? -hx : -hz;
                float length = alongX ? p.Width : p.Depth;
                foreach (float c in BayCentres(length))
                {
                    float at = corner + c;
                    bool blocked = false;
                    foreach ((bool bx, float bw, float bat) in blockers)
                        if (bx == alongX && bw == side && Math.Abs(bat - at) < 1.25f) blocked = true;
                    foreach (PlanOpening o in w.Openings)
                        if (Math.Abs(start + o.Center - at) < o.Width * 0.5f + 1.0f) blocked = true;
                    if (blocked) continue;
                    w.Openings.Add(new PlanOpening { Kind = PlanOpeningKind.Window, Center = at - start, Width = 1.6f, Bottom = bottom, Top = top });
                }
            }
        }

        private static (PlanWall Front, PlanWall Back, PlanWall West, PlanWall East) Shell(BuildingPlan p, int f)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            return (Wall(p, f, -hx, hz - Ext * 0.5f, hx, hz - Ext * 0.5f, Ext, true),
                    Wall(p, f, -hx, -hz + Ext * 0.5f, hx, -hz + Ext * 0.5f, Ext, true),
                    Wall(p, f, -hx + Ext * 0.5f, -hz + Ext, -hx + Ext * 0.5f, hz - Ext, Ext, true),
                    Wall(p, f, hx - Ext * 0.5f, -hz + Ext, hx - Ext * 0.5f, hz - Ext, Ext, true));
        }

        /// <summary>
        /// Where a row of houses is divided: the building's sides and party walls on bay lines, two bays
        /// apart (the end house takes what is left); a semi is split once, near its middle.
        /// </summary>
        private static List<float> HouseEdges(float width, bool semi)
        {
            float hx = width * 0.5f, offX = BayOffset(width);
            var edges = new List<float> { -hx };
            if (semi)
            {
                if (width >= CityBay * 4f - 0.05f) edges.Add(-hx + offX + CityBay * (float)Math.Round((hx - offX) / CityBay));
            }
            else
            {
                for (int k = 2; ; k += 2)
                {
                    float line = -hx + offX + CityBay * k;
                    if (hx - line < CityBay * 2f - 0.05f) break;
                    edges.Add(line);
                }
                // An end house of three bays or more gives one to its neighbour.
                if (edges.Count > 1 && hx - edges[edges.Count - 1] >= CityBay * 3f - 0.05f) edges[edges.Count - 1] += CityBay;
            }
            edges.Add(hx);
            return edges;
        }

        /// <summary>
        /// Multi-storey buildings. Each section (the whole building, or each house of a terrace or a pair of
        /// semis, between party walls) has a core strip along its west side holding the stair (one flight
        /// per floor, all rising north, stacked) and a walkway beside it; the rest of each floor is laid
        /// out for its use.
        /// </summary>
        private static void Storeys(BuildingPlan p, FloorUse[] uses, DeterministicRandom rng, List<float> houses, int number)
        {
            float hz = p.Depth * 0.5f, inX = p.Width * 0.5f - Ext, inZ = hz - Ext;
            var fronts = new PlanWall[p.Floors];
            for (int f = 0; f < p.Floors; f++) fronts[f] = Shell(p, f).Front;
            List<float> edges = houses ?? new List<float> { -p.Width * 0.5f, p.Width * 0.5f };
            int count = edges.Count - 1;
            for (int i = 0; i < count; i++)
            {
                float xa = i == 0 ? -inX : edges[i] + Part * 0.5f, xb = i == count - 1 ? inX : edges[i + 1] - Part * 0.5f;
                if (i > 0)
                    for (int f = 0; f < p.Floors; f++) Wall(p, f, edges[i], -inZ, edges[i], inZ, Part, false);
                var own = (FloorUse[])uses.Clone();
                if (i > 0 && own[0] == FloorUse.Shop) own[0] = FloorUse.HouseGround; // the corner shop is at the end of the row
                string house = houses != null && count > 1 ? $"No. {number + 2 * i}" : "";
                Section(p, own, rng, fronts, xa, xb, count > 1 ? $"h{i}:" : "", house, houses != null);
            }
            for (int f = 0; f < p.Floors; f++)
            {
                bool glazed = uses[f] == FloorUse.Shop || uses[f] == FloorUse.Cafe || uses[f] == FloorUse.Lobby;
                Windows(p, f, glazed ? 0.6f : 0.9f, glazed ? 2.9f : 2.3f);
            }
        }

        /// <summary>One section of a building from <paramref name="xa"/> to <paramref name="xb"/> (inner faces), every floor.</summary>
        private static void Section(BuildingPlan p, FloorUse[] uses, DeterministicRandom rng, PlanWall[] fronts, float xa, float xb, string tag, string house, bool home)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f, inZ = hz - Ext;
            float offX = BayOffset(p.Width), offZ = BayOffset(p.Depth);
            // The core's east wall on a bay line, leaving a walkway of at least 1.7 m beside the stair.
            float xCore = -hx + offX;
            while (xCore < xa + StairWidth + 1.7f) xCore += CityBay;
            float laneE = xa + StairWidth;
            float stairS = -inZ + StairMargin, stairN = stairS + StairRun;
            float walkX = (laneE + xCore) * 0.5f;
            int n = p.Floors;

            for (int f = 0; f + 1 < n; f++)
                p.Stairs.Add(new PlanStair { FromFloor = f, X0 = xa, X1 = laneE, ZStart = stairS, ZEnd = stairN });
            for (int k = 1; k < n; k++)
            {
                p.Holes.Add(new PlanHole { Floor = k, X0 = xa, Z0 = stairS, X1 = laneE, Z1 = stairN });
                p.Rails.Add(new PlanRail { Floor = k, X0 = laneE, Z0 = stairS, X1 = laneE, Z1 = stairN });
                if (k == n - 1) p.Rails.Add(new PlanRail { Floor = k, X0 = xa, Z0 = stairS, X1 = laneE, Z1 = stairS }); // nothing climbs on from the top floor
            }

            string[] palette = home ? new[] { "planks", "carpet", "carpet", "planks", "lino" } : new[] { "planks", "carpet", "tiles", "planks", "carpet", "lino" };
            string prefix = house.Length > 0 ? house + ", " : "";
            for (int f = 0; f < n; f++)
            {
                if (f == 0)
                {
                    Door(fronts[0], walkX + hx, 1.6f, 2.3f);
                    Fixture(p, $"f0:{tag}door:entrance", 0, "door_entrance", walkX, hz - Ext * 0.5f, 0f);
                    if (!home) Decor(p, 0, "mailboxes", xa + 0.2f, inZ - 0.8f, 90f);
                }
                PlanWall core = Wall(p, f, xCore, -inZ, xCore, inZ, Part, false);
                string hall = home ? (f == 0 ? "hall" : "landing") : f == 0 ? "Entrance hall" : $"Floor {f} · stair hall";
                Room(p, f, prefix + hall, xa, -inZ, xCore, inZ, f == 0 ? (home ? "planks" : "tiles") : home ? "carpet" : "lino");
                if (p.Lift && stairN + 1.7f <= inZ) Decor(p, f, "lift", xa + 0.12f, stairN + 1.0f, 90f);
                float ex0 = xCore + Part * 0.5f;
                string label = prefix + (home ? (f == 0 ? "ground floor" : f == 1 ? "first floor" : f == 2 ? "second floor" : "top floor") : f == 0 ? "Ground floor" : $"Floor {f}");
                FloorUse use = uses[f];
                string finish = palette[rng.Range(0, palette.Length)];
                switch (use)
                {
                    case FloorUse.Lobby: Lobby(p, f, core, ex0, xb, inZ, rng); break;
                    case FloorUse.Shop: ShopFloor(p, f, core, ex0, xb, inZ, rng, false); break;
                    case FloorUse.Cafe: ShopFloor(p, f, core, ex0, xb, inZ, rng, true); break;
                    case FloorUse.Warehouse: break;
                    default:
                        Units(p, f, use, label, core, ex0, xb, inZ, offX, offZ, finish, rng, tag);
                        break;
                }
                if (home)
                {
                    // The hall: a runner by the front door and coats on the hooks; pictures up the stairs.
                    float wall = xCore - Part * 0.5f;
                    if (f == 0)
                    {
                        Decor(p, 0, "rug", walkX, inZ - 2.0f, 90f, rng.Range(0, 6));
                        if (SpotFree(p, 0, wall, inZ - 2.6f, 1.3f)) Decor(p, 0, "coat_rack", wall, inZ - 2.6f, 270f, rng.Range(0, 6));
                    }
                    Pictures(p, f, xa, -inZ, wall, inZ, f == 0 ? 1 : 2, rng);
                }
                else if (use == FloorUse.Flats || use == FloorUse.Hotel || use == FloorUse.Offices) Pictures(p, f, xa, -inZ, xCore - Part * 0.5f, inZ, 1, rng);
            }
        }

        /// <summary>No door within <paramref name="reach"/> of a spot on a floor.</summary>
        private static bool SpotFree(BuildingPlan p, int f, float x, float z, float reach)
        {
            foreach (PlanFixture d in p.Fixtures)
                if (d.Floor == f && d.DefId.StartsWith("door", StringComparison.Ordinal) && Math.Abs(d.X - x) < reach && Math.Abs(d.Z - z) < reach) return false;
            return true;
        }

        /// <summary>
        /// A picture on the wall at (<paramref name="x"/>, <paramref name="z"/>), facing <paramref name="yaw"/> (0 north,
        /// 90 east, 180 south, 270 west), if that wall is an inner one (outer walls get windows), no door is
        /// beside it and nothing tall stands against it.
        /// </summary>
        private static bool Picture(BuildingPlan p, int f, float x, float z, float yaw, DeterministicRandom rng)
        {
            float inX = p.Width * 0.5f - Ext, inZ = p.Depth * 0.5f - Ext;
            bool facesZ = Math.Abs(Math.Sin(yaw * Math.PI / 180.0)) < 0.5;
            if (facesZ ? Math.Abs(Math.Abs(z) - inZ) < 0.4f : Math.Abs(Math.Abs(x) - inX) < 0.4f) return false;
            if (!SpotFree(p, f, x, z, 1.2f)) return false;
            // Not beside a stair: the flights are stacked in the same lane on every floor.
            foreach (PlanStair st in p.Stairs)
                if ((st.FromFloor == f || st.FromFloor == f - 1) && x > Math.Min(st.X0, st.X1) - 0.1f && x < Math.Max(st.X0, st.X1) + 0.1f
                    && z > Math.Min(st.ZStart, st.ZEnd) - 0.3f && z < Math.Max(st.ZStart, st.ZEnd) + 0.3f) return false;
            foreach (PlanFixture o in p.Fixtures)
                if (o.Floor == f && o.DefId != "bed_double" && Math.Abs(o.X - x) < 0.8f && Math.Abs(o.Z - z) < 0.8f) return false;
            foreach (PlanDecor o in p.Decor)
                if (o.Floor == f && (o.Kind == "kitchen" || o.Kind == "picture" || o.Kind == "lift" || o.Kind == "whiteboard" || o.Kind == "mailboxes" || o.Kind == "coat_rack" || o.Kind == "shop_rack")
                    && Math.Abs(o.X - x) < 1.1f && Math.Abs(o.Z - z) < 1.1f) return false;
            Decor(p, f, "picture", x, z, yaw, rng.Range(0, 12));
            return true;
        }

        /// <summary>Up to <paramref name="count"/> pictures on the inner walls of a room (inner faces given).</summary>
        private static void Pictures(BuildingPlan p, int f, float x0, float z0, float x1, float z1, int count, DeterministicRandom rng)
        {
            var spots = new List<(float X, float Z, float Yaw)>();
            foreach (float a in new[] { 0.3f, 0.5f, 0.7f })
            {
                spots.Add((x0 + 0.02f, z0 + (z1 - z0) * a, 90f));
                spots.Add((x1 - 0.02f, z0 + (z1 - z0) * a, 270f));
                spots.Add((x0 + (x1 - x0) * a, z0 + 0.02f, 0f));
                spots.Add((x0 + (x1 - x0) * a, z1 - 0.02f, 180f));
            }
            int start = rng.Range(0, spots.Count);
            for (int i = 0; i < spots.Count && count > 0; i++)
            {
                (float x, float z, float yaw) = spots[(start + i * 5) % spots.Count];
                if (Picture(p, f, x, z, yaw, rng)) count--;
            }
        }

        /// <summary>A floor's rooms east of the core: a spine corridor with units on both sides when there is room, else rooms off the walkway.</summary>
        private static void Units(BuildingPlan p, int f, FloorUse use, string label, PlanWall core, float ex0, float inX, float inZ, float offX, float offZ,
            string finish, DeterministicRandom rng, string tag)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            bool home = use == FloorUse.HouseGround || use == FloorUse.HouseUpper;
            float ew = inX - ex0, ed = 2f * inZ;
            var units = new List<(float X0, float Z0, float X1, float Z1, float DoorX, float DoorZ, float DoorYaw, bool Corridor)>();
            if (!home && ew >= 10.5f && ed >= 12f)
            {
                // A spine corridor across the floor on the bay line nearest the middle.
                float zc = -hz + offZ + CityBay * (float)Math.Round((hz - offZ) / CityBay);
                if (zc - 0.9f - (-inZ) < 4f) zc += CityBay;
                if (inZ - (zc + 0.9f) < 4f) zc -= CityBay;
                PlanWall north = Wall(p, f, ex0, zc + 0.9f, inX, zc + 0.9f, Part, false);
                PlanWall south = Wall(p, f, ex0, zc - 0.9f, inX, zc - 0.9f, Part, false);
                Door(core, zc + inZ, 1.6f, 2.3f); // open doorway from the stair hall
                Room(p, f, $"{label} · corridor", ex0, zc - 0.9f, inX, zc + 0.9f, "lino");
                foreach (bool side in new[] { true, false })
                {
                    PlanWall wall = side ? north : south;
                    float z0 = side ? zc + 0.9f : -inZ, z1 = side ? inZ : zc - 0.9f;
                    // Units across the strip, on bay lines.
                    int bays = use == FloorUse.Hotel ? 1 : use == FloorUse.Offices ? (side ? 99 : 2) : 2 + rng.Range(0, 2);
                    float x = ex0;
                    while (x < inX - 1f)
                    {
                        float target = x + bays * CityBay - 0.2f;
                        float next = -hx + offX + CityBay * (float)Math.Ceiling((target - (-hx + offX)) / CityBay - 1e-3);
                        if (inX - next < CityBay * 0.9f) next = inX; // no sliver at the end
                        next = Math.Min(next, inX);
                        if (next < inX) Wall(p, f, next, side ? zc + 0.9f : -inZ, next, side ? inZ : zc - 0.9f, Part, false);
                        float dx = (x + next) * 0.5f + (next - x > 4.5f ? -(next - x) * 0.25f : 0f);
                        Door(wall, dx - ex0, 1.6f, 2.3f);
                        units.Add((x, z0, next, z1, dx, side ? zc + 0.9f : zc - 0.9f, 0f, true));
                        x = next;
                    }
                }
            }
            else
            {
                // Rooms in a row along the walkway, each with a door in the core wall (in a house, near the
                // room's front end, leaving its middle free for furniture).
                int bays = use == FloorUse.Hotel || home ? 1 : 2;
                float z = -inZ;
                while (z < inZ - 1f)
                {
                    float target = z + bays * CityBay - 0.2f;
                    float next = -hz + offZ + CityBay * (float)Math.Ceiling((target - (-hz + offZ)) / CityBay - 1e-3);
                    if (inZ - next < CityBay * 0.9f) next = inZ;
                    next = Math.Min(next, inZ);
                    if (next < inZ) Wall(p, f, ex0, next, inX, next, Part, false);
                    float dz = home ? next - 1.0f : (z + next) * 0.5f;
                    Door(core, dz + inZ, 1.6f, 2.3f);
                    units.Add((ex0, z, inX, next, ex0 - Part * 0.5f, dz, 90f, false));
                    z = next;
                }
            }

            // Furnish each unit; some doors are locked and their keys left elsewhere on the floor.
            var keyHolders = new List<PlanFixture>();
            var locked = new List<PlanFixture>();
            int i = 0;
            foreach (var u in units)
            {
                string unit = $"{f}{(char)('A' + i)}";
                string room;
                bool canLock = use == FloorUse.Flats || use == FloorUse.Hotel;
                PlanFixture door = Fixture(p, $"f{f}:{tag}door:{i}", f, "door_interior", u.DoorX, u.DoorZ, u.DoorYaw);
                if (canLock && rng.NextFloat() < 0.3f)
                {
                    door.LockId = $"{p.Name} · {(use == FloorUse.Hotel ? "Room" : "Flat")} {unit}";
                    door.Locked = true;
                    locked.Add(door);
                }
                var before = new List<PlanFixture>(p.Fixtures);
                int decorBefore = p.Decor.Count;
                switch (use)
                {
                    case FloorUse.Offices: room = OfficeUnit(p, f, u.X0, u.Z0, u.X1, u.Z1, i, rng); break;
                    case FloorUse.Hotel: room = HotelRoom(p, f, u.X0, u.Z0, u.X1, u.Z1, u.Corridor, rng); break;
                    case FloorUse.HouseGround: room = HouseGroundRoom(p, f, u.X0, u.Z0, u.X1, u.Z1, i, rng); break;
                    case FloorUse.HouseUpper: room = HouseUpperRoom(p, f, u.X0, u.Z0, u.X1, u.Z1, i, rng); break;
                    default: room = Flat(p, f, u.X0, u.Z0, u.X1, u.Z1, u.Corridor, rng); break;
                }
                ClearDoorways(p, f, before.Count, decorBefore);
                string title = use == FloorUse.Flats ? $"Flat {unit} · {room}" : use == FloorUse.Hotel ? $"Room {unit}" : room;
                Room(p, f, $"{label} · {title}", u.X0, u.Z0, u.X1, u.Z1, room == "kitchen-diner" || room == "bathroom" ? "tiles" : finish);
                if (!door.Locked)
                    foreach (PlanFixture fx in p.Fixtures)
                        if (!before.Contains(fx) && !string.IsNullOrEmpty(fx.LootTable)) keyHolders.Add(fx);
                i++;
            }
            // Each key lies in an open unit on the same floor; with no open unit to hold them, nothing is locked.
            foreach (PlanFixture door in locked)
            {
                if (keyHolders.Count > 0) keyHolders[rng.Range(0, keyHolders.Count)].KeysInside.Add(door.LockId);
                else
                {
                    door.Locked = false;
                    door.LockId = "";
                }
            }
        }

        /// <summary>Removes furniture added since the given counts that would stand in a doorway on the floor.</summary>
        private static void ClearDoorways(BuildingPlan p, int floor, int fixturesFrom, int decorFrom)
        {
            var doors = p.Fixtures.FindAll(x => x.Floor == floor && x.DefId.StartsWith("door", StringComparison.Ordinal));
            bool Near(float x, float z)
            {
                foreach (PlanFixture d in doors)
                    if ((d.X - x) * (d.X - x) + (d.Z - z) * (d.Z - z) < 0.95f * 0.95f) return true;
                return false;
            }
            for (int i = p.Fixtures.Count - 1; i >= fixturesFrom; i--)
            {
                PlanFixture f = p.Fixtures[i];
                if (f.Floor == floor && !f.DefId.StartsWith("door", StringComparison.Ordinal) && Near(f.X, f.Z) && f.KeysInside.Count == 0) p.Fixtures.RemoveAt(i);
            }
            for (int i = p.Decor.Count - 1; i >= decorFrom; i--)
            {
                PlanDecor d = p.Decor[i];
                if (d.Floor == floor && d.Kind != "rug" && Near(d.X, d.Z)) p.Decor.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ rooms

        /// <summary>A point in a unit: <paramref name="a"/> across (0 west .. 1 east), <paramref name="b"/> deep (0 south .. 1 north).</summary>
        private static (float X, float Z) At(float x0, float z0, float x1, float z1, float a, float b) => (x0 + (x1 - x0) * a, z0 + (z1 - z0) * b);

        private static string Flat(BuildingPlan p, int f, float x0, float z0, float x1, float z1, bool corridor, DeterministicRandom rng)
        {
            float w = x1 - x0, d = z1 - z0;
            // The window side is the side away from the corridor (the unit's outer edge).
            bool northOuter = Math.Abs(z1 - (p.Depth * 0.5f - Ext)) < 0.3f;
            float outer = northOuter ? z1 - 0.45f : z0 + 0.45f, inner = northOuter ? z0 + 0.45f : z1 - 0.45f;
            float faceIn = northOuter ? 180f : 0f; // furniture against the outer wall faces inward
            if (w >= 6.5f)
            {
                // Bedroom in the west part, living room and kitchen in the east part.
                float split = x0 + Math.Max(3.2f, w * 0.42f);
                PlanWall part = Wall(p, f, split, z0, split, z1, Part, false);
                Door(part, (inner - z0) + (northOuter ? 1.2f : -1.2f), 1.6f, 2.3f);
                Fixture(p, $"f{f}:{U(x0, z0)}:bedroom_door", f, "door_interior", split, inner + (northOuter ? 1.2f : -1.2f), 90f);
                Fixture(p, $"f{f}:{U(x0, z0)}:bed", f, "bed_double", (x0 + split) * 0.5f, outer + (northOuter ? -1.3f : 1.3f), faceIn);
                Container(p, $"f{f}:{U(x0, z0)}:wardrobe", f, "loot_wardrobe", "wardrobe", x0 + 0.4f, (outer + inner) * 0.5f, 90f);
                Decor(p, f, "nightstand", (x0 + split) * 0.5f + 1.05f, outer, faceIn);
                Decor(p, f, "rug", (x0 + split) * 0.5f, (outer + inner) * 0.5f, 0f, rng.Range(0, 6));
                // Bathroom corner by the door side.
                Decor(p, f, "toilet", split - 0.4f, inner, 270f);
                Container(p, $"f{f}:{U(x0, z0)}:medcab", f, "loot_medcab", "medical", split - 0.22f, inner + (northOuter ? 2.3f : -2.3f), 270f);
                Pictures(p, f, x0 + Part * 0.5f, z0 + Part * 0.5f, split - Part * 0.5f, z1 - Part * 0.5f, 1, rng);
                x0 = split;
            }
            // Living room and kitchen.
            Decor(p, f, "sofa", x0 + (x1 - x0) * 0.5f, outer + (northOuter ? -1.6f : 1.6f), faceIn + 180f, rng.Range(0, 6));
            Decor(p, f, "tv", x0 + (x1 - x0) * 0.5f, outer + (northOuter ? -0.25f : 0.25f), faceIn);
            Decor(p, f, "rug", x0 + (x1 - x0) * 0.5f, outer + (northOuter ? -1.0f : 1.0f), 0f, rng.Range(0, 6));
            Decor(p, f, rng.NextFloat() < 0.5f ? "plant" : "armchair", x1 - 0.6f, outer + (northOuter ? -0.6f : 0.6f), faceIn, rng.Range(0, 6));
            Container(p, $"f{f}:{U(x0, z0)}:kitchen", f, "loot_cabinet", "kitchen", x1 - 0.3f, (outer + inner) * 0.5f, 270f);
            Container(p, $"f{f}:{U(x0, z0)}:fridge", f, "loot_fridge", "fridge", x1 - 0.4f, (outer + inner) * 0.5f + (northOuter ? -1.1f : 1.1f), 270f);
            Decor(p, f, "kitchen", x1 - 0.3f, (outer + inner) * 0.5f + (northOuter ? 1.1f : -1.1f), 270f);
            if (d > 5.5f) Decor(p, f, "dining", x0 + (x1 - x0) * 0.45f, (outer + inner) * 0.5f, 90f);
            if (rng.NextFloat() < 0.5f) Container(p, $"f{f}:{U(x0, z0)}:shelf", f, "loot_bookshelf", "study", x0 + 0.25f, outer + (northOuter ? -1.0f : 1.0f), 90f);
            Decor(p, f, "coffee_table", x0 + (x1 - x0) * 0.5f, outer + (northOuter ? -1.0f : 1.0f), 0f);
            Pictures(p, f, x0 + Part * 0.5f, z0 + Part * 0.5f, x1 - Part * 0.5f, z1 - Part * 0.5f, 2, rng);
            return w >= 6.5f ? "flat" : "studio";
        }

        private static string HotelRoom(BuildingPlan p, int f, float x0, float z0, float x1, float z1, bool corridor, DeterministicRandom rng)
        {
            bool northOuter = Math.Abs(z1 - (p.Depth * 0.5f - Ext)) < 0.3f;
            float outer = northOuter ? z1 - 0.45f : z0 + 0.45f, inner = northOuter ? z0 + 0.45f : z1 - 0.45f;
            float faceIn = northOuter ? 180f : 0f;
            float cx = (x0 + x1) * 0.5f;
            Fixture(p, $"f{f}:{U(x0, z0)}:bed", f, "bed_double", cx, (outer + inner) * 0.5f + (northOuter ? 0.6f : -0.6f), faceIn);
            Decor(p, f, "nightstand", cx + 1.1f, outer + (northOuter ? -0.3f : 0.3f), faceIn);
            Container(p, $"f{f}:{U(x0, z0)}:wardrobe", f, "loot_wardrobe", "wardrobe", x0 + 0.4f, inner + (northOuter ? 1.9f : -1.9f), 90f);
            Decor(p, f, "desk", x1 - 0.45f, (outer + inner) * 0.5f, 270f, rng.Range(0, 6));
            Decor(p, f, "tv", x1 - 0.2f, (outer + inner) * 0.5f + (northOuter ? 1.2f : -1.2f), 270f);
            Decor(p, f, "toilet", x0 + 0.35f, inner, 90f);
            Container(p, $"f{f}:{U(x0, z0)}:sink", f, "loot_sink", "bathroom", x0 + 0.25f, inner + (northOuter ? 0.9f : -0.9f), 90f);
            Decor(p, f, "rug", cx, (outer + inner) * 0.5f, 0f, rng.Range(0, 6));
            Decor(p, f, "lamp", x1 - 0.35f, outer + (northOuter ? -0.35f : 0.35f), 0f, rng.Range(0, 6));
            Pictures(p, f, x0 + Part * 0.5f, z0 + Part * 0.5f, x1 - Part * 0.5f, z1 - Part * 0.5f, 1, rng);
            return "hotel room";
        }

        private static string OfficeUnit(BuildingPlan p, int f, float x0, float z0, float x1, float z1, int index, DeterministicRandom rng)
        {
            float w = x1 - x0, d = z1 - z0;
            if (w > 8f && d > 5f)
            {
                // Open plan: rows of desks with chairs, a plant or two, filing lockers.
                int rows = Math.Max(1, (int)((d - 2.4f) / 2.6f)), cols = Math.Max(1, (int)((w - 2f) / 2.2f));
                int k = 0;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float x = x0 + 1.3f + c * 2.2f, z = z0 + 1.6f + r * 2.6f;
                        if (k % 4 == 0) Container(p, $"f{f}:{U(x0, z0)}:desk{k}", f, "loot_desk", "office", x, z, 0f);
                        else Decor(p, f, "desk", x, z, 0f, rng.Range(0, 6));
                        Decor(p, f, "chair", x, z - 0.7f, 0f);
                        k++;
                    }
                Container(p, $"f{f}:{U(x0, z0)}:files", f, "loot_locker", "office", x1 - 0.3f, z1 - 0.6f, 270f);
                Decor(p, f, "plant", x0 + 0.5f, z1 - 0.5f, 0f, rng.Range(0, 6));
                Decor(p, f, "plant", x1 - 0.5f, z0 + 0.5f, 0f, rng.Range(0, 6));
                Pictures(p, f, x0 + Part * 0.5f, z0 + Part * 0.5f, x1 - Part * 0.5f, z1 - Part * 0.5f, 2, rng);
                return "open-plan office";
            }
            switch ((index + rng.Range(0, 3)) % 3)
            {
                case 0:
                    Decor(p, f, "dining", (x0 + x1) * 0.5f, (z0 + z1) * 0.5f, 90f);
                    Decor(p, f, "whiteboard", x0 + 0.1f, (z0 + z1) * 0.5f, 90f);
                    Decor(p, f, "plant", x1 - 0.5f, z1 - 0.5f, 0f, rng.Range(0, 6));
                    return "meeting room";
                case 1:
                    Container(p, $"f{f}:{U(x0, z0)}:kitchen", f, "loot_cabinet", "kitchen", x1 - 0.3f, (z0 + z1) * 0.5f, 270f);
                    Container(p, $"f{f}:{U(x0, z0)}:fridge", f, "loot_fridge", "fridge", x1 - 0.4f, (z0 + z1) * 0.5f + 1.2f, 270f);
                    Decor(p, f, "kitchen", x1 - 0.3f, (z0 + z1) * 0.5f - 1.1f, 270f);
                    Decor(p, f, "table", (x0 + x1) * 0.5f - 0.5f, (z0 + z1) * 0.5f, 0f);
                    Decor(p, f, "chair", (x0 + x1) * 0.5f - 0.5f, (z0 + z1) * 0.5f - 0.7f, 0f);
                    return "staff kitchen";
                default:
                    Container(p, $"f{f}:{U(x0, z0)}:desk", f, "loot_desk", "office", (x0 + x1) * 0.5f, (z0 + z1) * 0.5f, 0f);
                    Decor(p, f, "chair", (x0 + x1) * 0.5f, (z0 + z1) * 0.5f - 0.7f, 0f);
                    Container(p, $"f{f}:{U(x0, z0)}:shelf", f, "loot_bookshelf", "study", x0 + 0.25f, (z0 + z1) * 0.5f, 90f);
                    Decor(p, f, "armchair", x1 - 0.6f, z1 - 0.6f, 225f, rng.Range(0, 6));
                    return "office";
            }
        }

        /// <summary>Which end of a house a room is at: 1 the street front, -1 the back, 0 in between.</summary>
        private static int HouseEnd(BuildingPlan p, float z0, float z1)
        {
            float inZ = p.Depth * 0.5f - Ext;
            return z1 > inZ - 0.3f ? 1 : z0 < -inZ + 0.3f ? -1 : 0;
        }

        // A house's rooms lie east of its hall, each with its door in the hall wall (x0) near its north end,
        // so the hall wall south of the door and the other three walls are free for furniture.

        private static string HouseGroundRoom(BuildingPlan p, int f, float x0, float z0, float x1, float z1, int index, DeterministicRandom rng)
        {
            string k = U(x0, z0);
            float w = x1 - x0, d = z1 - z0, ex = x1 - Part * 0.5f, sz = z0 + Part * 0.5f;
            int end = HouseEnd(p, z0, z1);
            if (end == -1)
            {
                // Kitchen-diner at the back: fridge, cupboards, cooker and worktop along the east wall,
                // the washing machine beside them, a table with four chairs, a picture over it.
                Container(p, $"f{f}:{k}:fridge", f, "loot_fridge", "fridge", ex - 0.4f, sz + 0.42f, 270f);
                Container(p, $"f{f}:{k}:cabinet", f, "loot_cabinet", "kitchen", ex - 0.3f, sz + 1.35f, 270f);
                Fixture(p, $"f{f}:{k}:stove", f, "stove", ex - 0.3f, sz + 2.2f, 270f);
                if (d > 4.45f)
                {
                    Decor(p, f, "kitchen", ex - 0.3f, sz + 3.45f, 270f);
                    if (w > 4.2f) Decor(p, f, "washer", ex - 1.3f, sz + 0.35f, 0f);
                }
                else Decor(p, f, "washer", ex - 0.35f, sz + 2.85f, 270f);
                float tx = x0 + Math.Max(1.15f, (w - 0.7f) * 0.45f);
                Decor(p, f, "dining", tx, sz + 0.8f, 90f);
                Picture(p, f, x0 + 0.02f, sz + 0.8f, 90f, rng);
                if (w > 5.5f) Decor(p, f, "sideboard", x0 + 0.25f, sz + 0.9f, 90f);
                if (d > 4.6f) Decor(p, f, "plant", ex - 0.35f, z1 - 0.45f, 0f, rng.Range(0, 6));
                return "kitchen-diner";
            }
            if (end == 1 || index % 2 == 0)
            {
                // The front room: television on the hall wall, sofa facing it from the far wall, a coffee table
                // on a rug between, an armchair by the window, books, a lamp, pictures.
                float tvZ = sz + Math.Min(0.95f, d - 2.6f);
                float sofaX = Math.Min(ex - 0.5f, x0 + 3.1f);
                Decor(p, f, "tv", x0 + 0.3f, tvZ, 90f);
                Picture(p, f, x0 + 0.02f, tvZ, 90f, rng);
                Decor(p, f, "sofa", sofaX, tvZ + 0.25f, 270f, rng.Range(0, 6));
                Decor(p, f, "rug", (x0 + sofaX) * 0.5f, tvZ + 0.2f, 90f, rng.Range(0, 6));
                Decor(p, f, "coffee_table", (x0 + 0.5f + sofaX - 0.45f) * 0.5f, tvZ + 0.2f, 90f);
                if (d > 3.7f) Decor(p, f, "armchair", x0 + w * 0.55f, z1 - 0.65f, 180f + rng.Range(-20, 21), rng.Range(0, 6));
                if (w > 4.5f) Decor(p, f, "lamp", ex - 0.3f, sz + 0.25f, 0f, rng.Range(0, 6));
                Container(p, $"f{f}:{k}:shelf", f, "loot_bookshelf", "study", ex - 0.2f, z1 - 0.75f, 270f);
                if (w > 5.5f && d > 4.2f) Decor(p, f, "sideboard", ex - 0.25f, sz + 1.5f, 270f);
                Pictures(p, f, x0, sz, ex, z1 - Part * 0.5f, 1, rng);
                return end == 1 ? "front room" : "sitting room";
            }
            // A room in the middle: a dining room or a study.
            if (rng.NextFloat() < 0.5f)
            {
                Decor(p, f, "dining", x0 + w * 0.5f, sz + d * 0.42f, 90f);
                Decor(p, f, "sideboard", ex - 0.25f, sz + d * 0.42f, 270f);
                Decor(p, f, "plant", ex - 0.35f, sz + 0.35f, 0f, rng.Range(0, 6));
                Pictures(p, f, x0, sz, ex, z1, 2, rng);
                return "dining room";
            }
            Container(p, $"f{f}:{k}:desk", f, "loot_desk", "office", ex - 0.4f, sz + 1.0f, 270f);
            Decor(p, f, "chair", ex - 1.05f, sz + 1.0f, 90f);
            Container(p, $"f{f}:{k}:shelf", f, "loot_bookshelf", "study", x0 + 0.25f, sz + 0.75f, 90f);
            Decor(p, f, "armchair", x0 + w * 0.5f, sz + 0.55f, rng.Range(-20, 21), rng.Range(0, 6));
            Decor(p, f, "lamp", ex - 0.3f, z1 - 0.35f, 0f, rng.Range(0, 6));
            Pictures(p, f, x0, sz, ex, z1, 1, rng);
            return "study";
        }

        private static string HouseUpperRoom(BuildingPlan p, int f, float x0, float z0, float x1, float z1, int index, DeterministicRandom rng)
        {
            string k = U(x0, z0);
            float w = x1 - x0, d = z1 - z0, ex = x1 - Part * 0.5f, sz = z0 + Part * 0.5f;
            int end = HouseEnd(p, z0, z1);
            if (end == -1 && f == 1)
            {
                // The bathroom over the kitchen: a bath along the back wall, toilet and basin on the side wall.
                Decor(p, f, "bathtub", x0 + 0.95f, sz + 0.42f, 0f);
                Decor(p, f, "toilet", ex - 0.35f, sz + 0.55f, 270f);
                Container(p, $"f{f}:{k}:sink", f, "loot_sink", "bathroom", ex - 0.25f, sz + 1.45f, 270f);
                Container(p, $"f{f}:{k}:medcab", f, "loot_medcab", "medical", ex - 0.17f, sz + 2.3f, 270f);
                if (d > 3.9f) Decor(p, f, "washer", ex - 0.35f, sz + 3.15f, 270f);
                if (w > 4.5f) Decor(p, f, "plant", ex - 0.35f, z1 - 0.45f, 0f, rng.Range(0, 6));
                return "bathroom";
            }
            bool main = end == 1 && f == 1;
            if (!main && index % 2 == 1 && rng.NextFloat() < 0.4f)
            {
                // A box room: a desk under the picture, shelves, a chest of drawers.
                Container(p, $"f{f}:{k}:desk", f, "loot_desk", "office", ex - 0.4f, sz + 1.0f, 270f);
                Decor(p, f, "chair", ex - 1.05f, sz + 1.0f, 90f);
                Container(p, $"f{f}:{k}:shelf", f, "loot_bookshelf", "study", x0 + 0.25f, sz + 0.75f, 90f);
                Container(p, $"f{f}:{k}:drawers", f, "loot_drawers", "wardrobe", ex - 0.3f, z1 - 0.6f, 270f);
                Decor(p, f, "rug", x0 + w * 0.5f, sz + d * 0.45f, 0f, rng.Range(0, 6));
                Pictures(p, f, x0, sz, ex, z1, 1, rng);
                return "box room";
            }
            // A bedroom: the bed's head against the east wall between two bedside tables, the wardrobe
            // on the hall wall, drawers at the foot, a rug, a lamp and pictures.
            float bz = sz + Math.Min(1.35f, d * 0.4f);
            Fixture(p, $"f{f}:{k}:bed", f, "bed_double", ex - 1.1f, bz, 270f);
            Picture(p, f, ex - 0.02f, bz, 270f, rng);
            Decor(p, f, "nightstand", ex - 0.25f, bz - 1.05f, 270f);
            if (bz + 1.3f < z1) Decor(p, f, "nightstand", ex - 0.25f, bz + 1.05f, 270f);
            Container(p, $"f{f}:{k}:wardrobe", f, "loot_wardrobe", "wardrobe", x0 + 0.32f, sz + 0.7f, 90f);
            Container(p, $"f{f}:{k}:drawers", f, "loot_drawers", "wardrobe", ex - 1.6f, sz + 0.25f, 0f);
            Decor(p, f, "rug", x0 + Math.Max(1.0f, w - 2.6f), bz, 90f, rng.Range(0, 6));
            if (w > 5.5f)
            {
                Decor(p, f, "armchair", x0 + 1.6f, sz + 0.6f, 20f, rng.Range(0, 6));
                Decor(p, f, "lamp", x0 + 0.85f, sz + 0.3f, 0f, rng.Range(0, 6));
            }
            Pictures(p, f, x0, sz, ex, z1, 1, rng);
            return main ? "main bedroom" : "bedroom";
        }

        private static void Lobby(BuildingPlan p, int f, PlanWall core, float ex0, float inX, float inZ, DeterministicRandom rng)
        {
            Door(core, inZ + inZ - 1.6f, 1.6f, 2.3f); // from the entrance hall into the lobby, near the front
            Room(p, f, "Lobby", ex0, -inZ, inX, inZ, "tiles");
            float cx = (ex0 + inX) * 0.5f;
            Container(p, $"f{f}:reception", f, "loot_counter", "counter", cx, inZ - 3.2f, 180f);
            Decor(p, f, "sofa", cx - 2.2f, 0f, 90f, rng.Range(0, 6));
            Decor(p, f, "sofa", cx + 2.2f, 0f, 270f, rng.Range(0, 6));
            Decor(p, f, "table", cx, 0f, 0f);
            Decor(p, f, "rug", cx, 0f, 0f, rng.Range(0, 6));
            for (int i = 0; i < 4; i++) Decor(p, f, "plant", i % 2 == 0 ? ex0 + 0.6f : inX - 0.6f, i < 2 ? -inZ + 0.6f : inZ - 0.6f, 0f, rng.Range(0, 6));
            Container(p, $"f{f}:security", f, "loot_locker", "electrical_minor", inX - 0.3f, -inZ + 2.2f, 270f);
        }

        private static void ShopFloor(BuildingPlan p, int f, PlanWall core, float ex0, float inX, float inZ, DeterministicRandom rng, bool cafe)
        {
            Door(core, inZ + inZ - 1.6f, 1.6f, 2.3f);
            Room(p, f, cafe ? "Café" : "Shop", ex0, -inZ, inX, inZ, cafe ? "planks" : "tiles");
            float cx = (ex0 + inX) * 0.5f;
            Container(p, $"f{f}:till", f, "loot_counter", "counter", cx, inZ - 2.4f, 180f);
            if (cafe)
            {
                Container(p, $"f{f}:fridge", f, "loot_fridge", "fridge", inX - 0.4f, inZ - 1.2f, 270f);
                int k = 0;
                for (float z = -inZ + 1.6f; z < inZ - 4.4f; z += 2.6f)
                    for (float x = ex0 + 1.4f; x < inX - 1.2f; x += 2.8f)
                        Decor(p, f, k++ % 3 == 0 ? "dining" : "table", x, z, 0f);
                Decor(p, f, "plant", ex0 + 0.5f, inZ - 0.5f, 0f, rng.Range(0, 6));
            }
            else
            {
                int k = 0;
                for (float z = -inZ + 1.4f; z < inZ - 4.2f; z += 2.6f)
                    for (float x = ex0 + 1.4f; x + 0.9f < inX - 0.8f; x += 2.2f)
                        Container(p, $"f{f}:shelf{k++}", f, "loot_shelf", rng.NextFloat() < 0.3f ? "kitchen" : "store", x, z, k % 2 == 0 ? 0f : 180f);
                Container(p, $"f{f}:fridge", f, "loot_fridge", "fridge", inX - 0.4f, inZ - 1.2f, 270f);
            }
        }

        /// <summary>A shed: one tall open floor of pallets and racking, a loading door and a small office.</summary>
        private static void Warehouse(BuildingPlan p, DeterministicRandom rng)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f, inX = hx - Ext, inZ = hz - Ext;
            var (front, _, _, _) = Shell(p, 0);
            Door(front, hx - 2f, 1.6f, 2.3f);
            Fixture(p, "f0:door:entrance", 0, "door_entrance", -2f, hz - Ext * 0.5f, 0f);
            Room(p, 0, "Warehouse floor", -inX, -inZ, inX, inZ, "concrete");
            int k = 0;
            for (float z = -inZ + 1.6f; z < inZ - 3f; z += 3f)
                for (float x = -inX + 1.6f; x < inX - 1.6f; x += 3.2f)
                {
                    if (k % 3 == 0) Container(p, $"f0:crate{k}", 0, "loot_crate", rng.NextFloat() < 0.5f ? "general" : "workshop", x, z, 0f);
                    else Decor(p, 0, rng.NextFloat() < 0.5f ? "pallet" : "shop_rack", x, z, 0f, rng.Range(0, 6));
                    k++;
                }
            Container(p, "f0:locker", 0, "loot_locker", "electrical_minor", inX - 0.3f, inZ - 1f, 270f);
            Container(p, "f0:toolbox", 0, "loot_toolbox", "toolbox", inX - 0.5f, inZ - 2.2f, 270f);
            Windows(p, 0, 3.2f, 4.2f);
        }

        /// <summary>A church: one tall nave with pews, an altar and a vestry cupboard.</summary>
        private static void Church(BuildingPlan p, DeterministicRandom rng)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f, inX = hx - Ext, inZ = hz - Ext;
            var (front, _, _, _) = Shell(p, 0);
            Door(front, hx, 1.6f, 2.3f);
            Fixture(p, "f0:door:entrance", 0, "door_entrance", 0f, hz - Ext * 0.5f, 0f);
            Room(p, 0, "Nave", -inX, -inZ, inX, inZ, "tiles");
            for (float z = inZ - 3f; z > -inZ + 5f; z -= 1.6f)
            {
                Decor(p, 0, "pew", -inX * 0.5f - 0.3f, z, 0f);
                Decor(p, 0, "pew", inX * 0.5f + 0.3f, z, 0f);
            }
            Decor(p, 0, "altar", 0f, -inZ + 2.2f, 0f);
            Decor(p, 0, "rug", 0f, 0f, 0f, 2);
            Container(p, "f0:vestry", 0, "loot_wardrobe", "general", inX - 0.4f, -inZ + 1f, 270f);
            Container(p, "f0:candles", 0, "loot_drawers", "general", -inX + 0.3f, -inZ + 1f, 90f);
            Windows(p, 0, 2.2f, 6.5f);
        }
    }
}
