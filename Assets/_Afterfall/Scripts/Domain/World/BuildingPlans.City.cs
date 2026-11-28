using System;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Kesselmouth's enterable buildings: blocks of flats three to six storeys high, supermarkets and
    /// railway stations. Their ids carry everything needed to rebuild them
    /// (<c>kind|uid|name|floors</c>), so a chunk's plan and the view built from it always agree, and
    /// fixture keys (prefixed with the id) are unique to each building.
    /// </summary>
    public static partial class BuildingPlans
    {
        public const string FlatsKind = "flats", SupermarketKind = "supermarket", StationKind = "station";

        /// <summary>The id of a city building: its kind, a unique tag, its name and (flats) its storeys.</summary>
        public static string CityPlanId(string kind, string uid, string name, int floors) => $"{kind}|{uid}|{name}|{floors}";

        private static BuildingPlan CityPlan(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string[] parts = id.Split('|');
            if (parts[0] == CityBlockKind) return CityBlockPlan(id, parts);
            if (parts.Length < 4) return null;
            int.TryParse(parts[3], out int floors);
            switch (parts[0])
            {
                case FlatsKind: return Flats(id, parts[2], Math.Max(3, Math.Min(6, floors)));
                case SupermarketKind: return Supermarket(id, parts[2]);
                case StationKind: return Station(id, parts[2]);
                default: return null;
            }
        }

        /// <summary>
        /// A block of flats, 16 × 12 m and <paramref name="floors"/> storeys: the same stair hall as the
        /// Millbrook block, its two flights alternating up the building; a corner shop and a storage
        /// room on the ground floor and a flat on each floor above. Some flats are locked; each key is
        /// somewhere already reachable (the shop counter or a lower flat), so every door can be opened.
        /// </summary>
        public static BuildingPlan Flats(string id, string name, int floors)
        {
            const float hx = 8f, hz = 6f, ext = 0.3f;
            const float inX = hx - ext, inZ = hz - ext;
            var rng = DeterministicRandom.Derive(7177, id);
            var p = new BuildingPlan { Id = id, Name = name, Width = hx * 2f, Depth = hz * 2f, Floors = floors };

            // Stair lanes: flights from even floors rise north in the west lane, from odd floors south in the east lane.
            const float laneW0 = -inX, laneMid = -6.15f, laneE1 = -4.6f, stairS = -4.1f, stairN = 1.3f;
            for (int f = 0; f + 1 < floors; f++)
            {
                if (f % 2 == 0) p.Stairs.Add(new PlanStair { FromFloor = f, X0 = laneW0, X1 = laneMid, ZStart = stairS, ZEnd = stairN });
                else p.Stairs.Add(new PlanStair { FromFloor = f, X0 = laneMid, X1 = laneE1, ZStart = stairN, ZEnd = stairS });
            }
            for (int k = 1; k < floors; k++)
            {
                bool viaWest = (k - 1) % 2 == 0, departs = k + 1 < floors;
                if (viaWest)
                {
                    p.Holes.Add(new PlanHole { Floor = k, X0 = laneW0, Z0 = stairS, X1 = laneMid, Z1 = stairN });
                    p.Rails.Add(new PlanRail { Floor = k, X0 = laneMid, Z0 = stairS, X1 = laneMid, Z1 = stairN });
                    p.Rails.Add(new PlanRail { Floor = k, X0 = laneW0, Z0 = stairS, X1 = laneMid, Z1 = stairS });
                    // The next flight rises south beside the hole: fence its high end off from the hall.
                    if (departs) p.Rails.Add(new PlanRail { Floor = k, X0 = laneE1, Z0 = -2.6f, X1 = laneE1, Z1 = stairN - 0.2f });
                }
                else
                {
                    p.Holes.Add(new PlanHole { Floor = k, X0 = laneMid, Z0 = stairS, X1 = laneE1, Z1 = stairN });
                    p.Rails.Add(new PlanRail { Floor = k, X0 = laneMid, Z0 = stairS, X1 = laneMid, Z1 = stairN });
                    p.Rails.Add(new PlanRail { Floor = k, X0 = laneE1, Z0 = stairS, X1 = laneE1, Z1 = stairN });
                    p.Rails.Add(new PlanRail { Floor = k, X0 = laneMid, Z0 = stairN, X1 = laneE1, Z1 = stairN });
                }
            }

            for (int f = 0; f < floors; f++)
            {
                PlanWall front = Wall(p, f, -hx, hz - ext * 0.5f, hx, hz - ext * 0.5f, ext, true);
                PlanWall back = Wall(p, f, -hx, -hz + ext * 0.5f, hx, -hz + ext * 0.5f, ext, true);
                PlanWall west = Wall(p, f, -hx + ext * 0.5f, -hz + ext, -hx + ext * 0.5f, hz - ext, ext, true);
                PlanWall east = Wall(p, f, hx - ext * 0.5f, -hz + ext, hx - ext * 0.5f, hz - ext, ext, true);
                if (f == 0) Door(front, 5f, 1.6f, 2.3f);
                else Window(front, 5f, 1.2f);
                Window(front, 1.8f, 1.2f);
                Window(front, 9.2f, 1.4f);
                Window(front, 12.6f, 1.4f);
                Window(back, 1.8f, 1.2f);
                Window(back, 9.2f, 1.4f);
                Window(back, 14.6f, 0.8f);
                Window(east, 2.9f, 1.4f);
                Window(east, 8.5f, 1.4f);
                Window(west, 3.0f, 1.0f);
                PlanWall corridor = Wall(p, f, -1.4f, -inZ, -1.4f, inZ, 0.15f, false);
                PlanWall middle = Wall(p, f, -1.4f, 0f, inX, 0f, 0.15f, false);
                Door(corridor, 5.7f + 2.6f, 1.6f, 2.3f);
                if (f == 0) Door(corridor, 5.7f - 2.6f, 1.6f, 2.3f);
                else
                {
                    Door(middle, 2.0f + 1.4f, 1.6f, 2.3f);
                    PlanWall bath = Wall(p, f, 4.5f, -inZ, 4.5f, 0f, 0.15f, false);
                    Door(bath, 5.7f - 1.4f, 1.6f, 2.3f);
                }
            }

            // Ground floor: the corner shop (its counter is the first hiding place for keys) and the storage room.
            string storage = name + " · Storage";
            Room(p, 0, "Stair hall", -inX, -inZ, -1.4f, inZ, "tiles");
            Room(p, 0, "Corner shop", -1.4f, 0f, inX, inZ, "tiles");
            Room(p, 0, "Storage room", -1.4f, -inZ, inX, 0f, "concrete");
            Fixture(p, "door:entrance", 0, "door_entrance", -3f, hz - ext * 0.5f, 0f);
            Fixture(p, "door:shop", 0, "door_interior", -1.4f, 2.6f, 90f);
            PlanFixture storageDoor = Fixture(p, "door:storage", 0, "door_interior", -1.4f, -2.6f, 90f);
            storageDoor.LockId = storage;
            storageDoor.Locked = true;
            Container(p, "shop:shelf1", 0, "loot_shelf", "store", 2.9f, 4.95f, 180f);
            Container(p, "shop:shelf2", 0, "loot_shelf", "store", 6.6f, 4.95f, 180f);
            Container(p, "shop:fridge", 0, "loot_fridge", "fridge", 7.1f, 2.2f, -90f);
            PlanFixture counter = Container(p, "shop:counter", 0, "loot_counter", "counter", 1.6f, 1.4f, 0f);
            Container(p, "storage:locker", 0, "loot_locker", "electrical_minor", 7.2f, -4.4f, -90f);
            Container(p, "storage:toolbox", 0, "loot_toolbox", "toolbox", 3.2f, -5.2f, 0f);
            Container(p, "storage:crate", 0, "loot_crate", "general", 6.8f, -1.0f, -90f);
            Fixture(p, "storage:workbench", 0, "workbench", 0.8f, -4.8f, 0f);
            Decor(p, 0, "mailboxes", -7.5f, 3.6f, 90f);
            Decor(p, 0, "bench", -2.9f, -4.4f, 0f);
            Decor(p, 0, "shop_rack", 4.0f, 2.6f, 0f);

            // The flats. Keys go into containers that are already reachable, so nothing is sealed for good.
            var reachable = new System.Collections.Generic.List<PlanFixture> { counter };
            bool storageKeyPlaced = false;
            for (int f = 1; f < floors; f++)
            {
                int n = f + 1;
                string flat = $"Flat {n}";
                bool study = rng.NextFloat() < 0.35f;
                PlanFixture door = Fixture(p, $"door:flat{n}", f, "door_interior", -1.4f, 2.6f, 90f);
                if (rng.NextFloat() < 0.45f)
                {
                    door.LockId = $"{name} · {flat}";
                    door.Locked = true;
                    reachable[rng.Range(0, reachable.Count)].KeysInside.Add(door.LockId);
                }
                Fixture(p, $"door:flat{n}_bedroom", f, "door_interior", 2.0f, 0f, 0f);
                Fixture(p, $"door:flat{n}_bath", f, "door_interior", 4.5f, -1.4f, 90f);
                Room(p, f, "Stair hall", -inX, -inZ, -1.4f, inZ, "tiles");
                Room(p, f, study ? $"{flat} · study" : $"{flat} · living room", -1.4f, 0f, inX, inZ, "planks");
                Room(p, f, $"{flat} · bedroom", -1.4f, -inZ, 4.5f, 0f, "planks");
                Room(p, f, $"{flat} · bathroom", 4.5f, -inZ, inX, 0f, "tiles");
                var added = new System.Collections.Generic.List<PlanFixture>();
                if (study)
                {
                    added.Add(Container(p, $"flat{n}:desk1", f, "loot_desk", "office", 1.0f, 4.9f, 180f));
                    added.Add(Container(p, $"flat{n}:desk2", f, "loot_desk", "office", 4.2f, 4.9f, 180f));
                    added.Add(Container(p, $"flat{n}:bookshelf", f, "loot_bookshelf", "study", 7.25f, 4.6f, -90f));
                    added.Add(Container(p, $"flat{n}:locker", f, "loot_locker", "electrical", -0.7f, 4.5f, 90f));
                    Decor(p, f, "chair", 1.0f, 4.1f, 0f);
                    Decor(p, f, "chair", 4.2f, 4.1f, 0f);
                    Decor(p, f, "table", 3.0f, 1.6f, 0f);
                }
                else
                {
                    added.Add(Container(p, $"flat{n}:fridge", f, "loot_fridge", "fridge", 7.2f, 4.6f, -90f));
                    added.Add(Container(p, $"flat{n}:kitchen", f, "loot_cabinet", "kitchen", 7.2f, 3.4f, -90f));
                    Fixture(p, $"flat{n}:stove", f, "stove", 7.2f, 2.3f, -90f);
                    added.Add(Container(p, $"flat{n}:bookshelf", f, "loot_bookshelf", "study", -0.55f, 5.25f, 180f));
                    Decor(p, f, "sofa", 3.0f, 0.65f, 0f);
                    Decor(p, f, "table", 3.0f, 2.6f, 0f);
                    Decor(p, f, "dining", 5.6f, 4.0f, 0f);
                    Decor(p, f, "rug", 3.0f, 2.2f, 0f);
                }
                added.Add(Container(p, $"flat{n}:wardrobe", f, "loot_wardrobe", "wardrobe", -0.7f, -4.8f, 90f));
                PlanFixture drawers = Container(p, $"flat{n}:drawers", f, "loot_drawers", "wardrobe", 3.6f, -0.6f, 180f);
                added.Add(drawers);
                Fixture(p, $"flat{n}:bed", f, "bed_double", 1.8f, -4.4f, 0f);
                added.Add(Container(p, $"flat{n}:medcab", f, "loot_medcab", "medical", 7.3f, -3.2f, -90f));
                added.Add(Container(p, $"flat{n}:sink", f, "loot_sink", "bathroom", 6.0f, -5.25f, 0f));
                Decor(p, f, "toilet", 7.2f, -1.0f, -90f);
                Decor(p, f, "nightstand", 3.2f, -5.3f, 0f);
                if (!storageKeyPlaced)
                {
                    drawers.KeysInside.Add(storage);
                    storageKeyPlaced = true;
                }
                reachable.AddRange(added);
            }
            return p;
        }

        /// <summary>
        /// A supermarket, 32 × 22 m on one high floor: two entrances, checkouts, three aisles of shelves
        /// and a wall of fridges on the sales floor; a storeroom with a loading door behind it, and a
        /// locked manager's office whose key was left in a till.
        /// </summary>
        public static BuildingPlan Supermarket(string id, string name)
        {
            const float hx = 16f, hz = 11f, ext = 0.3f;
            const float inX = hx - ext, inZ = hz - ext;
            var p = new BuildingPlan { Id = id, Name = name, Width = hx * 2f, Depth = hz * 2f, Floors = 1, FloorHeight = 4.6f };
            string office = name + " · Office";

            // Along the front and back u runs from x = -16; along the sides from z = -10.7.
            PlanWall front = Wall(p, 0, -hx, hz - ext * 0.5f, hx, hz - ext * 0.5f, ext, true);
            PlanWall back = Wall(p, 0, -hx, -hz + ext * 0.5f, hx, -hz + ext * 0.5f, ext, true);
            PlanWall west = Wall(p, 0, -hx + ext * 0.5f, -hz + ext, -hx + ext * 0.5f, hz - ext, ext, true);
            Wall(p, 0, hx - ext * 0.5f, -hz + ext, hx - ext * 0.5f, hz - ext, ext, true);
            Door(front, 10f, 1.6f, 2.3f);  // x = -6
            Door(front, 22f, 1.6f, 2.3f);  // x = +6
            ShopWindow(front, 4f, 5f);
            ShopWindow(front, 16f, 6f);
            ShopWindow(front, 28f, 5f);
            Door(back, 28f, 1.6f, 2.3f);   // x = +12: the loading door
            Window(west, 16f, 1.4f);
            Window(west, 3f, 1.0f);
            PlanWall store = Wall(p, 0, -inX, -5f, inX, -5f, 0.15f, false);
            Door(store, 12.7f, 1.6f, 2.3f); // x = -3
            Door(store, 25.7f, 1.6f, 2.3f); // x = +10
            PlanWall officeWall = Wall(p, 0, -8f, -inZ, -8f, -5f, 0.15f, false);
            Door(officeWall, 3.2f, 1.6f, 2.3f); // z = -7.5

            Room(p, 0, "Sales floor", -inX, -5f, inX, inZ, "tiles");
            Room(p, 0, "Storeroom", -8f, -inZ, inX, -5f, "concrete");
            Room(p, 0, "Manager's office", -inX, -inZ, -8f, -5f, "planks");

            Fixture(p, "door:entrance_w", 0, "door_entrance", -6f, hz - ext * 0.5f, 0f);
            Fixture(p, "door:entrance_e", 0, "door_entrance", 6f, hz - ext * 0.5f, 0f);
            Fixture(p, "door:loading", 0, "door_entrance", 12f, -hz + ext * 0.5f, 180f);
            Fixture(p, "door:store_w", 0, "door_interior", -3f, -5f, 0f);
            Fixture(p, "door:store_e", 0, "door_interior", 10f, -5f, 0f);
            PlanFixture officeDoor = Fixture(p, "door:office", 0, "door_interior", -8f, -7.5f, 90f);
            officeDoor.LockId = office;
            officeDoor.Locked = true;

            // Checkouts inside the doors; one till still holds the office key.
            Container(p, "till1", 0, "loot_counter", "counter", -3.5f, 7.4f, 0f);
            Container(p, "till2", 0, "loot_counter", "counter", 0f, 7.4f, 0f).KeysInside.Add(office);
            Container(p, "till3", 0, "loot_counter", "counter", 3.5f, 7.4f, 0f);

            // Three aisles of shelves in three runs, with cross aisles between the runs.
            float[] rows = { 4.3f, 1.4f, -1.5f };
            float[] xs = { -12.6f, -10.8f, -9.0f, -7.2f, -1.8f, 0f, 1.8f, 7.8f, 9.6f, 11.4f };
            string[] tables = { "store", "store", "kitchen", "store", "bathroom", "store", "medical", "general", "store", "kitchen" };
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < xs.Length; i++)
                    Container(p, $"shelf{r}_{i}", 0, "loot_shelf", tables[(i + r * 3) % tables.Length], xs[i], rows[r], r % 2 == 0 ? 180f : 0f);
            for (int i = 0; i < 6; i++)
                Container(p, $"fridge{i}", 0, "loot_fridge", "fridge", 15.15f, 0.5f + i * 0.9f, -90f);

            // Storeroom and office.
            Container(p, "store:crate1", 0, "loot_crate", "general", -5.5f, -9.6f, 0f);
            Container(p, "store:crate2", 0, "loot_crate", "store", -3.8f, -9.6f, 0f);
            Container(p, "store:crate3", 0, "loot_crate", "store", 0.5f, -9.6f, 0f);
            Container(p, "store:crate4", 0, "loot_crate", "general", 2.3f, -9.6f, 0f);
            Container(p, "store:shelf", 0, "loot_shelf", "store", 6.5f, -10.2f, 0f);
            Container(p, "store:locker", 0, "loot_locker", "electrical_minor", 15.1f, -6.5f, -90f);
            Container(p, "store:toolbox", 0, "loot_toolbox", "toolbox", 9.0f, -10.3f, 0f);
            Container(p, "office:desk", 0, "loot_desk", "office", -12f, -9.9f, 0f);
            Container(p, "office:locker", 0, "loot_locker", "electrical", -15.2f, -6.0f, 90f);
            Container(p, "office:drawers", 0, "loot_drawers", "office", -9.2f, -10.2f, 0f);
            Decor(p, 0, "chair", -12f, -9.1f, 180f);
            Decor(p, 0, "bench", -11f, 9.9f, 180f);
            Decor(p, 0, "bench", 11f, 9.9f, 180f);
            return p;
        }

        /// <summary>
        /// A railway station, 28 × 12 m on one high floor, its back to the track and the platform: a
        /// ticket hall with doors to the street and to the platform, a kiosk, and a locked ticket office
        /// whose key is behind the kiosk counter.
        /// </summary>
        public static BuildingPlan Station(string id, string name)
        {
            const float hx = 14f, hz = 6f, ext = 0.3f;
            const float inX = hx - ext, inZ = hz - ext;
            var p = new BuildingPlan { Id = id, Name = name, Width = hx * 2f, Depth = hz * 2f, Floors = 1, FloorHeight = 5.0f };
            string ticketOffice = name + " · Ticket office";

            PlanWall front = Wall(p, 0, -hx, hz - ext * 0.5f, hx, hz - ext * 0.5f, ext, true);
            PlanWall back = Wall(p, 0, -hx, -hz + ext * 0.5f, hx, -hz + ext * 0.5f, ext, true);
            PlanWall west = Wall(p, 0, -hx + ext * 0.5f, -hz + ext, -hx + ext * 0.5f, hz - ext, ext, true);
            PlanWall east = Wall(p, 0, hx - ext * 0.5f, -hz + ext, hx - ext * 0.5f, hz - ext, ext, true);
            Door(front, 11f, 1.6f, 2.3f);  // x = -3
            Door(front, 17f, 1.6f, 2.3f);  // x = +3
            TallWindow(front, 4f, 1.6f);
            TallWindow(front, 7.5f, 1.6f);
            TallWindow(front, 20.5f, 1.6f);
            TallWindow(front, 24f, 1.6f);
            Door(back, 8f, 1.6f, 2.3f);    // x = -6: to the platform
            Door(back, 20f, 1.6f, 2.3f);   // x = +6
            TallWindow(back, 3f, 1.6f);
            TallWindow(back, 14f, 2.4f);
            TallWindow(back, 25f, 1.6f);
            Window(west, 5.7f, 1.4f);
            Window(east, 5.7f, 1.4f);
            PlanWall officeWall = Wall(p, 0, 8f, -inZ, 8f, inZ, 0.15f, false);
            Door(officeWall, 2.7f, 1.6f, 2.3f);  // z = -3
            PlanWall kioskWall = Wall(p, 0, -8f, -inZ, -8f, inZ, 0.15f, false);
            Door(kioskWall, 8.7f, 1.6f, 2.3f);   // z = +3

            Room(p, 0, "Ticket hall", -8f, -inZ, 8f, inZ, "tiles");
            Room(p, 0, "Ticket office", 8f, -inZ, inX, inZ, "planks");
            Room(p, 0, "Kiosk", -inX, -inZ, -8f, inZ, "tiles");

            Fixture(p, "door:street_w", 0, "door_entrance", -3f, hz - ext * 0.5f, 0f);
            Fixture(p, "door:street_e", 0, "door_entrance", 3f, hz - ext * 0.5f, 0f);
            Fixture(p, "door:platform_w", 0, "door_entrance", -6f, -hz + ext * 0.5f, 180f);
            Fixture(p, "door:platform_e", 0, "door_entrance", 6f, -hz + ext * 0.5f, 180f);
            Fixture(p, "door:kiosk", 0, "door_interior", -8f, 3f, 90f);
            PlanFixture officeDoor = Fixture(p, "door:office", 0, "door_interior", 8f, -3f, 90f);
            officeDoor.LockId = ticketOffice;
            officeDoor.Locked = true;

            Container(p, "office:counter", 0, "loot_counter", "counter", 10.5f, 4.6f, 180f);
            Container(p, "office:desk", 0, "loot_desk", "office", 12.4f, -4.7f, 0f);
            Container(p, "office:locker", 0, "loot_locker", "electrical_minor", 13.1f, 0.5f, -90f);
            Container(p, "office:medcab", 0, "loot_medcab", "medical", 13.4f, 3.0f, -90f);
            Container(p, "kiosk:shelf1", 0, "loot_shelf", "store", -11f, 5.2f, 180f);
            Container(p, "kiosk:shelf2", 0, "loot_shelf", "store", -11f, -5.2f, 0f);
            Container(p, "kiosk:fridge", 0, "loot_fridge", "fridge", -13.2f, 0f, 90f);
            Container(p, "kiosk:counter", 0, "loot_counter", "counter", -9.3f, -1.0f, 90f).KeysInside.Add(ticketOffice);
            Container(p, "hall:locker1", 0, "loot_locker", "general", -5.6f, 5.3f, 180f);
            Container(p, "hall:locker2", 0, "loot_locker", "general", 5.6f, 5.3f, 180f);
            Decor(p, 0, "bench", -2f, 0.4f, 0f);
            Decor(p, 0, "bench", 2f, 0.4f, 0f);
            Decor(p, 0, "bench", -2f, -0.4f, 180f);
            Decor(p, 0, "bench", 2f, -0.4f, 180f);
            Decor(p, 0, "chair", 12.4f, -3.9f, 180f);
            return p;
        }

        private static void ShopWindow(PlanWall w, float center, float width) =>
            w.Openings.Add(new PlanOpening { Kind = PlanOpeningKind.Window, Center = center, Width = width, Bottom = 0.5f, Top = 3.4f });

        private static void TallWindow(PlanWall w, float center, float width) =>
            w.Openings.Add(new PlanOpening { Kind = PlanOpeningKind.Window, Center = center, Width = width, Bottom = 0.9f, Top = 3.8f });
    }
}
