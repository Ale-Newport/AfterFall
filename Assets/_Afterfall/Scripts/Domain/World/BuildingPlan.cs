using System;
using System.Collections.Generic;

namespace Afterfall.Domain.World
{
    public enum PlanOpeningKind
    {
        Door,
        Window
    }

    /// <summary>An opening in a plan wall: centre along the wall from its start, width, bottom and top above the floor.</summary>
    public sealed class PlanOpening
    {
        public PlanOpeningKind Kind;
        public float Center;
        public float Width;
        public float Bottom;
        public float Top;
    }

    /// <summary>
    /// An axis-aligned wall on one floor, from (X0, Z0) to (X1, Z1) in the building's frame (X right,
    /// Z toward the front), standing on the floor and reaching the slab above.
    /// </summary>
    public sealed class PlanWall
    {
        public int Floor;
        public float X0, Z0, X1, Z1;
        public float Thickness = 0.15f;
        public bool Exterior;
        public readonly List<PlanOpening> Openings = new List<PlanOpening>();

        public float Length => Math.Abs(X1 - X0) + Math.Abs(Z1 - Z0);
        public bool AlongX => Math.Abs(X1 - X0) >= Math.Abs(Z1 - Z0);
    }

    /// <summary>A straight flight of stairs from one floor to the next along Z, in the lane X0..X1.</summary>
    public sealed class PlanStair
    {
        public int FromFloor;
        public float X0, X1;
        public float ZStart, ZEnd;
    }

    /// <summary>A rectangle of the floor slab left open (where a flight of stairs comes up through it).</summary>
    public sealed class PlanHole
    {
        public int Floor;
        public float X0, Z0, X1, Z1;
    }

    /// <summary>A guard rail on one floor from (X0, Z0) to (X1, Z1).</summary>
    public sealed class PlanRail
    {
        public int Floor;
        public float X0, Z0, X1, Z1;
    }

    /// <summary>A room (for floor finishes and the player-facing room name).</summary>
    public sealed class PlanRoom
    {
        public int Floor;
        public string Name = "";
        public float X0, Z0, X1, Z1;

        /// <summary>"tiles", "planks" or "concrete".</summary>
        public string Finish = "planks";
    }

    /// <summary>
    /// An interactive thing the plan places in the world as a structure (door, furniture, container):
    /// its content definition, position and facing in the building's frame, and for containers
    /// the loot table and any keys inside. <see cref="Key"/> is stable across versions.
    /// </summary>
    public sealed class PlanFixture
    {
        public string Key = "";
        public int Floor;
        public string DefId = "";
        public float X, Z, Yaw;
        public string LootTable = "";

        /// <summary>Doors: the lock's name and whether it starts locked.</summary>
        public string LockId = "";
        public bool Locked;

        /// <summary>Containers: names of the locks whose keys lie inside.</summary>
        public readonly List<string> KeysInside = new List<string>();

        /// <summary>Containers: items always found inside (besides the loot roll).</summary>
        public readonly List<string> ItemsInside = new List<string>();
    }

    /// <summary>Decoration drawn with the building (sofas, tables, toilets): no interaction, its own kind name.</summary>
    public sealed class PlanDecor
    {
        public int Floor;
        public string Kind = "";
        public float X, Z, Yaw;

        /// <summary>Colour variant (fabrics, plants, wood) so the same furniture differs from room to room.</summary>
        public int Variant;
    }

    /// <summary>
    /// A complete multi-storey building in its own frame: floors, walls with door and window
    /// openings, stairs and the holes they rise through, rails, rooms, and the interactive doors
    /// and furniture. The world generator turns fixtures into structures; the presentation builds
    /// the walls, slabs and stairs from the same plan, so geometry and gameplay always agree.
    /// </summary>
    public sealed class BuildingPlan
    {
        public string Id = "";
        public string Name = "";
        public float Width, Depth;
        public int Floors;
        public float FloorHeight = 3.2f;

        /// <summary>The outer walls' finish: "brick" (the default), "render", "concrete", "stone" or "corrugated"; and its colour variant.</summary>
        public string Facade = "brick";
        public int FacadeTint;

        /// <summary>A lift serves every floor (city buildings of five floors or more).</summary>
        public bool Lift;
        public float SlabThickness = 0.2f;

        /// <summary>Height of the ground floor's surface above the building origin.</summary>
        public float GroundFloorY = 0.15f;

        public float ParapetHeight = 0.9f;
        public readonly List<PlanWall> Walls = new List<PlanWall>();
        public readonly List<PlanStair> Stairs = new List<PlanStair>();
        public readonly List<PlanHole> Holes = new List<PlanHole>();
        public readonly List<PlanRail> Rails = new List<PlanRail>();
        public readonly List<PlanRoom> Rooms = new List<PlanRoom>();
        public readonly List<PlanFixture> Fixtures = new List<PlanFixture>();
        public readonly List<PlanDecor> Decor = new List<PlanDecor>();

        /// <summary>Height of a floor's walking surface above the building origin.</summary>
        public float FloorY(int floor) => GroundFloorY + floor * FloorHeight;

        /// <summary>Top of the roof slab.</summary>
        public float RoofY => FloorY(Floors);

        public float TotalHeight => RoofY + ParapetHeight;

        public PlanRoom RoomAt(int floor, float x, float z)
        {
            foreach (PlanRoom r in Rooms)
                if (r.Floor == floor && x >= Math.Min(r.X0, r.X1) && x <= Math.Max(r.X0, r.X1) && z >= Math.Min(r.Z0, r.Z1) && z <= Math.Max(r.Z0, r.Z1)) return r;
            return null;
        }
    }

    /// <summary>The hand-designed building plans the generator can place.</summary>
    public static partial class BuildingPlans
    {
        public const string MillbrookApartmentsId = "millbrook_apartments";

        /// <summary>The sedan parked beside the apartments; its key hangs in flat 2's kitchen.</summary>
        public const string SedanLock = "Blue sedan · MB-2187";

        public const string AshfordApartmentsId = "ashford_apartments";
        public const string EastgateApartmentsId = "eastgate_apartments";

        public static BuildingPlan Get(string id)
        {
            switch (id)
            {
                case MillbrookApartmentsId: return MillbrookApartments();
                case AshfordApartmentsId: return Apartments(AshfordApartmentsId, "Ashford Apartments", "");
                case EastgateApartmentsId: return Apartments(EastgateApartmentsId, "Eastgate Apartments", EastgateCarLock);
                default: return CityPlan(id);
            }
        }

        /// <summary>The sedan parked at the Eastgate apartments; its key is in that building's flat 2 kitchen.</summary>
        public const string EastgateCarLock = "Grey sedan · EG-7730";

        /// <summary>
        /// Millbrook Apartments: a three-storey brick block, 16 × 12 m. A stair hall on the west
        /// side; the ground floor holds a corner shop and a storage room with a workbench; the first
        /// floor is flat 2 (living room and kitchen, bedroom, bathroom); the second is flat 3, locked,
        /// with a study. The key to flat 3 is behind the shop counter; the storage key is in flat 2.
        /// </summary>
        public static BuildingPlan MillbrookApartments() => Apartments(MillbrookApartmentsId, "Millbrook Apartments", SedanLock);

        /// <summary>
        /// The three-storey apartment block, named <paramref name="name"/> (lock names follow it).
        /// <paramref name="carKey"/>, if set, is a vehicle key left in flat 2's kitchen.
        /// </summary>
        public static BuildingPlan Apartments(string id, string name, string carKey)
        {
            const float hx = 8f, hz = 6f, ext = 0.3f;
            const float inX = hx - ext, inZ = hz - ext; // 7.7, 5.7
            var p = new BuildingPlan { Id = id, Name = name, Width = hx * 2f, Depth = hz * 2f, Floors = 3 };
            string flat3 = name + " · Flat 3";
            string storage = name + " · Storage";

            // Stair lanes: west lane rises north from floor 0, east lane rises south from floor 1.
            const float laneW0 = -inX, laneMid = -6.15f, laneE1 = -4.6f, stairS = -4.1f, stairN = 1.3f;
            p.Stairs.Add(new PlanStair { FromFloor = 0, X0 = laneW0, X1 = laneMid, ZStart = stairS, ZEnd = stairN });
            p.Stairs.Add(new PlanStair { FromFloor = 1, X0 = laneMid, X1 = laneE1, ZStart = stairN, ZEnd = stairS });
            p.Holes.Add(new PlanHole { Floor = 1, X0 = laneW0, Z0 = stairS, X1 = laneMid, Z1 = stairN });
            p.Holes.Add(new PlanHole { Floor = 2, X0 = laneMid, Z0 = stairS, X1 = laneE1, Z1 = stairN });
            // Guards around the openings the flights come up through (open only where you step off).
            p.Rails.Add(new PlanRail { Floor = 1, X0 = laneMid, Z0 = stairS, X1 = laneMid, Z1 = stairN });
            p.Rails.Add(new PlanRail { Floor = 1, X0 = laneW0, Z0 = stairS, X1 = laneMid, Z1 = stairS });
            p.Rails.Add(new PlanRail { Floor = 2, X0 = laneMid, Z0 = stairS, X1 = laneMid, Z1 = stairN });
            p.Rails.Add(new PlanRail { Floor = 2, X0 = laneE1, Z0 = stairS, X1 = laneE1, Z1 = stairN });
            p.Rails.Add(new PlanRail { Floor = 2, X0 = laneMid, Z0 = stairN, X1 = laneE1, Z1 = stairN });
            // On the first floor the low end under the rising east flight is fenced off (under 2 m of headroom).
            p.Rails.Add(new PlanRail { Floor = 1, X0 = laneE1, Z0 = -2.6f, X1 = laneE1, Z1 = stairN - 0.2f });

            for (int f = 0; f < p.Floors; f++)
            {
                // Exterior walls with windows; the ground floor's front has the entrance.
                PlanWall front = Wall(p, f, -hx, hz - ext * 0.5f, hx, hz - ext * 0.5f, ext, true);
                PlanWall back = Wall(p, f, -hx, -hz + ext * 0.5f, hx, -hz + ext * 0.5f, ext, true);
                PlanWall west = Wall(p, f, -hx + ext * 0.5f, -hz + ext, -hx + ext * 0.5f, hz - ext, ext, true);
                PlanWall east = Wall(p, f, hx - ext * 0.5f, -hz + ext, hx - ext * 0.5f, hz - ext, ext, true);
                // Along the front and back walls, u runs from x = -8 (u = 0) to x = +8 (u = 16).
                if (f == 0) Door(front, 5f, 1.6f, 2.3f); // x = -3: the entrance
                else Window(front, 5f, 1.2f);
                Window(front, 1.8f, 1.2f);             // x = -6.2 stair hall
                Window(front, 9.2f, 1.4f);             // x = 1.2
                Window(front, 12.6f, 1.4f);            // x = 4.6
                Window(back, 1.8f, 1.2f);
                Window(back, 9.2f, 1.4f);
                Window(back, 14.6f, 0.8f);             // x = 6.6 bathroom
                // Side walls: u runs from z = -5.7 (u = 0) to z = +5.7.
                Window(east, 2.9f, 1.4f);
                Window(east, 8.5f, 1.4f);
                Window(west, 3.0f, 1.0f);

                // Corridor wall between the hall and the east rooms (x = -1.4), u from z = -5.7.
                PlanWall corridor = Wall(p, f, -1.4f, -inZ, -1.4f, inZ, 0.15f, false);
                // Wall between the front and back rooms (z = 0), u from x = -1.4.
                PlanWall middle = Wall(p, f, -1.4f, 0f, inX, 0f, 0.15f, false);
                if (f == 0)
                {
                    Door(corridor, 5.7f + 2.6f, 1.6f, 2.3f);  // shop (z = +2.6)
                    Door(corridor, 5.7f - 2.6f, 1.6f, 2.3f);  // storage (z = -2.6)
                }
                else
                {
                    Door(corridor, 5.7f + 2.6f, 1.6f, 2.3f);  // flat door into the living room
                    Door(middle, 2.0f + 1.4f, 1.6f, 2.3f);    // living room → bedroom (x = 2.0)
                    PlanWall bath = Wall(p, f, 4.5f, -inZ, 4.5f, 0f, 0.15f, false);
                    Door(bath, 5.7f - 1.4f, 1.6f, 2.3f);      // bedroom → bathroom (z = -1.4)
                }
            }

            // Rooms (floor finishes and names).
            Room(p, 0, "Stair hall", -inX, -inZ, -1.4f, inZ, "tiles");
            Room(p, 0, "Corner shop", -1.4f, 0f, inX, inZ, "tiles");
            Room(p, 0, "Storage room", -1.4f, -inZ, inX, 0f, "concrete");
            for (int f = 1; f < p.Floors; f++)
            {
                Room(p, f, "Stair hall", -inX, -inZ, -1.4f, inZ, "tiles");
                Room(p, f, f == 1 ? "Flat 2 · living room" : "Flat 3 · study", -1.4f, 0f, inX, inZ, "planks");
                Room(p, f, $"Flat {f + 1} · bedroom", -1.4f, -inZ, 4.5f, 0f, "planks");
                Room(p, f, $"Flat {f + 1} · bathroom", 4.5f, -inZ, inX, 0f, "tiles");
            }

            // Doors (fixtures). Door yaw: 0 faces the front (+Z); 90 faces east (+X).
            Fixture(p, "door:entrance", 0, "door_entrance", -3f, hz - ext * 0.5f, 0f);
            Fixture(p, "door:shop", 0, "door_interior", -1.4f, 2.6f, 90f);
            var storageDoor = Fixture(p, "door:storage", 0, "door_interior", -1.4f, -2.6f, 90f);
            storageDoor.LockId = storage;
            storageDoor.Locked = true;
            Fixture(p, "door:flat2", 1, "door_interior", -1.4f, 2.6f, 90f);
            Fixture(p, "door:flat2_bedroom", 1, "door_interior", 2.0f, 0f, 0f);
            Fixture(p, "door:flat2_bath", 1, "door_interior", 4.5f, -1.4f, 90f);
            var flat3Door = Fixture(p, "door:flat3", 2, "door_interior", -1.4f, 2.6f, 90f);
            flat3Door.LockId = flat3;
            flat3Door.Locked = true;
            Fixture(p, "door:flat3_bedroom", 2, "door_interior", 2.0f, 0f, 0f);
            Fixture(p, "door:flat3_bath", 2, "door_interior", 4.5f, -1.4f, 90f);

            // Ground floor: corner shop and storage room.
            Container(p, "shop:shelf1", 0, "loot_shelf", "store", 2.9f, 4.95f, 180f);
            Container(p, "shop:shelf2", 0, "loot_shelf", "store", 6.6f, 4.95f, 180f);
            Container(p, "shop:fridge", 0, "loot_fridge", "fridge", 7.1f, 2.2f, -90f);
            Container(p, "shop:counter", 0, "loot_counter", "counter", 1.6f, 1.4f, 0f).KeysInside.Add(flat3);
            Container(p, "storage:locker", 0, "loot_locker", "electrical_minor", 7.2f, -4.4f, -90f);
            Container(p, "storage:toolbox", 0, "loot_toolbox", "toolbox", 3.2f, -5.2f, 0f).ItemsInside.Add("screwdriver");
            Container(p, "storage:crate", 0, "loot_crate", "general", 6.8f, -1.0f, -90f);
            Fixture(p, "storage:workbench", 0, "workbench", 0.8f, -4.8f, 0f);

            // First floor: flat 2.
            Container(p, "flat2:fridge", 1, "loot_fridge", "fridge", 7.2f, 4.6f, -90f);
            PlanFixture kitchen = Container(p, "flat2:kitchen", 1, "loot_cabinet", "kitchen", 7.2f, 3.4f, -90f);
            if (!string.IsNullOrEmpty(carKey)) kitchen.KeysInside.Add(carKey);
            Fixture(p, "flat2:stove", 1, "stove", 7.2f, 2.3f, -90f);
            Container(p, "flat2:bookshelf", 1, "loot_bookshelf", "study", -0.55f, 5.25f, 180f);
            Container(p, "flat2:wardrobe", 1, "loot_wardrobe", "wardrobe", -0.7f, -4.8f, 90f);
            Container(p, "flat2:drawers", 1, "loot_drawers", "wardrobe", 3.6f, -0.6f, 180f).KeysInside.Add(storage);
            Fixture(p, "flat2:bed", 1, "bed_double", 1.8f, -4.4f, 0f);
            Container(p, "flat2:medcab", 1, "loot_medcab", "medical", 7.3f, -3.2f, -90f);
            Container(p, "flat2:sink", 1, "loot_sink", "bathroom", 6.0f, -5.25f, 0f);

            // Second floor: flat 3 (locked): a study full of paperwork and electronics.
            Container(p, "flat3:desk1", 2, "loot_desk", "office", 1.0f, 4.9f, 180f);
            Container(p, "flat3:desk2", 2, "loot_desk", "office", 4.2f, 4.9f, 180f);
            Container(p, "flat3:bookshelf1", 2, "loot_bookshelf", "study", 7.25f, 4.6f, -90f);
            Container(p, "flat3:bookshelf2", 2, "loot_bookshelf", "study", 7.25f, 1.2f, -90f);
            Container(p, "flat3:locker", 2, "loot_locker", "electrical", -0.7f, 4.5f, 90f);
            Container(p, "flat3:wardrobe", 2, "loot_wardrobe", "wardrobe", -0.7f, -4.8f, 90f);
            Fixture(p, "flat3:bed", 2, "bed_double", 1.8f, -4.4f, 0f);
            Container(p, "flat3:medcab", 2, "loot_medcab", "medical", 7.3f, -3.2f, -90f);
            Container(p, "flat3:sink", 2, "loot_sink", "bathroom", 6.0f, -5.25f, 0f);

            // Decoration: furniture to look at and walk around.
            Decor(p, 0, "mailboxes", -7.5f, 3.6f, 90f);
            Decor(p, 0, "bench", -2.9f, -4.4f, 0f);
            Decor(p, 0, "shop_rack", 4.0f, 2.6f, 0f);
            Decor(p, 0, "washer", 5.2f, -5.25f, 0f);
            Decor(p, 0, "washer", 6.2f, -5.25f, 0f);
            Decor(p, 1, "sofa", 3.0f, 0.65f, 0f);
            Decor(p, 1, "table", 3.0f, 2.6f, 0f);
            Decor(p, 1, "dining", 5.6f, 4.0f, 0f);
            Decor(p, 1, "toilet", 7.2f, -1.0f, -90f);
            Decor(p, 1, "nightstand", 3.2f, -5.3f, 0f);
            Decor(p, 2, "chair", 1.0f, 4.1f, 0f);
            Decor(p, 2, "chair", 4.2f, 4.1f, 0f);
            Decor(p, 2, "table", 3.0f, 1.6f, 0f);
            Decor(p, 2, "toilet", 7.2f, -1.0f, -90f);
            Decor(p, 2, "nightstand", 3.2f, -5.3f, 0f);
            Decor(p, 1, "rug", 3.0f, 2.2f, 0f);
            Decor(p, 2, "rug", 2.6f, -2.6f, 0f);
            return p;
        }

        private static PlanWall Wall(BuildingPlan p, int floor, float x0, float z0, float x1, float z1, float th, bool exterior)
        {
            var w = new PlanWall { Floor = floor, X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Thickness = th, Exterior = exterior };
            p.Walls.Add(w);
            return w;
        }

        private static void Door(PlanWall w, float center, float width, float top) =>
            w.Openings.Add(new PlanOpening { Kind = PlanOpeningKind.Door, Center = center, Width = width, Bottom = 0f, Top = top });

        private static void Window(PlanWall w, float center, float width) =>
            w.Openings.Add(new PlanOpening { Kind = PlanOpeningKind.Window, Center = center, Width = width, Bottom = 0.9f, Top = 2.3f });

        private static void Room(BuildingPlan p, int floor, string name, float x0, float z0, float x1, float z1, string finish) =>
            p.Rooms.Add(new PlanRoom { Floor = floor, Name = name, X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Finish = finish });

        private static PlanFixture Fixture(BuildingPlan p, string key, int floor, string def, float x, float z, float yaw)
        {
            var f = new PlanFixture { Key = p.Id + ":" + key, Floor = floor, DefId = def, X = x, Z = z, Yaw = yaw };
            p.Fixtures.Add(f);
            return f;
        }

        private static PlanFixture Container(BuildingPlan p, string key, int floor, string def, string loot, float x, float z, float yaw)
        {
            PlanFixture f = Fixture(p, key, floor, def, x, z, yaw);
            f.LootTable = loot;
            return f;
        }

        private static void Decor(BuildingPlan p, int floor, string kind, float x, float z, float yaw, int variant = 0) =>
            p.Decor.Add(new PlanDecor { Floor = floor, Kind = kind, X = x, Z = z, Yaw = yaw, Variant = variant });
    }
}
