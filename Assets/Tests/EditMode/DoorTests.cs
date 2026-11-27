using System;
using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Doors, locks and keys, the multi-storey building plan, and seeding it into new and old worlds.</summary>
    public sealed class DoorTests
    {
        private const string Flat3 = "Millbrook Apartments · Flat 3";
        private const string Storage = "Millbrook Apartments · Storage";

        private static Structure Door(GameSession s, string key) => s.World.Structures.Values.Single(x => x.SeedKey == BuildingPlans.MillbrookApartmentsId + ":" + key);

        private static void StandAt(GameSession s, Structure st) => s.Player.Position = st.Position + new Vec3(0.8f, 0f, 0.8f);

        [Test]
        public void TaggedKeys_KeepTheirLock_ThroughTransfersCompactingAndSaves()
        {
            GameSession s = TestSupport.NewSession();
            Inventory bag = s.Player.Inventory;
            Assert.IsTrue(bag.TryAddStack(new ItemStack(DoorRules.KeyItem, 1, 0, Flat3)));
            Assert.IsTrue(bag.TryAddStack(new ItemStack(DoorRules.KeyItem, 1, 0, Storage)));
            Assert.IsTrue(bag.TryAdd("wood", 5));
            Assert.IsTrue(bag.HasTagged(DoorRules.KeyItem, Flat3) && bag.HasTagged(DoorRules.KeyItem, Storage));
            Assert.AreEqual(2, bag.Count(DoorRules.KeyItem), "keys never merge");
            Assert.IsFalse(bag.TryAddStack(new ItemStack(DoorRules.KeyItem, 2, 0, "x")), "a tagged stack is one item");

            Inventory box = TestSupport.NewInventory(s.World, 6);
            int slot = Enumerable.Range(0, bag.SlotCount).First(i => bag.GetSlot(i).Tag == Flat3);
            Assert.IsTrue(InventoryTransfers.TryTransferSlot(bag, slot, box, 1).Ok);
            Assert.IsTrue(box.HasTagged(DoorRules.KeyItem, Flat3));
            Assert.IsFalse(bag.HasTagged(DoorRules.KeyItem, Flat3));
            Assert.IsTrue(InventoryTransfers.TryTransfer(box, bag, DoorRules.KeyItem, 1).Ok, "moving by item id keeps the tag");
            Assert.IsTrue(bag.HasTagged(DoorRules.KeyItem, Flat3));
            bag.Compact();
            Assert.IsTrue(bag.HasTagged(DoorRules.KeyItem, Flat3) && bag.HasTagged(DoorRules.KeyItem, Storage));
            Assert.AreEqual(5, bag.Count("wood"));

            string text = SaveSerializer.Serialize(s.World, "t", 1);
            WorldState l = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            Assert.IsTrue(l.Player.Inventory.HasTagged(DoorRules.KeyItem, Flat3));
            Assert.IsTrue(l.Player.Inventory.HasTagged(DoorRules.KeyItem, Storage));

            // Dying drops them with their labels; picking the belongings up brings them back.
            s.DamagePlayer(1000f, "test");
            TestSupport.Run(s, 0.3);
            var drop = s.World.Drops.Values.Single(d => d.IsBelongings);
            Assert.IsTrue(drop.Contents.HasTagged(DoorRules.KeyItem, Flat3));
        }

        [Test]
        public void NewGame_PlacesTheApartmentBuilding_WithLockedDoorsAndTheirKeys()
        {
            GameSession s = TestSupport.NewSession();
            WorldLayout l = s.World.Layout;
            PlacedPlan apt = l.Plans.Single();
            BuildingPlan plan = apt.Plan;
            Assert.AreEqual(3, plan.Floors);
            Assert.AreEqual(plan.Fixtures.Count, s.World.Structures.Values.Count(x => x.SeedKey.StartsWith(plan.Id, StringComparison.Ordinal)), "every door and piece of furniture is a structure");
            Assert.IsTrue(l.Props.Any(p => p.Kind == "apartment" && p.PlanId == plan.Id));

            Structure flat3 = Door(s, "door:flat3");
            Assert.IsTrue(flat3.DoorLocked && flat3.LockId == Flat3 && !flat3.DoorOpen);
            Assert.AreEqual(apt.Origin.Y + plan.FloorY(2), flat3.Position.Y, 1e-3, "flat 3 is on the second floor");
            Assert.IsTrue(Door(s, "door:storage").DoorLocked);
            Assert.IsFalse(Door(s, "door:entrance").DoorLocked);

            Structure counter = Door(s, "shop:counter");
            Assert.IsTrue(counter.Storage.HasTagged(DoorRules.KeyItem, Flat3), "the key to flat 3 is behind the shop counter");
            Structure drawers = Door(s, "flat2:drawers");
            Assert.IsTrue(drawers.Storage.HasTagged(DoorRules.KeyItem, Storage), "the storage key is in flat 2");
            Assert.AreEqual(0, WorldSeeding.EnsureFixtures(s.World, TestSupport.Content), "nothing is created twice");
        }

        [Test]
        public void Doors_OpenCloseLockAndUnlock_OnlyWithTheirKey()
        {
            GameSession s = TestSupport.NewSession();
            Structure shop = Door(s, "door:shop");
            StandAt(s, shop);
            Assert.IsTrue(s.ToggleDoor(shop.Id).Ok);
            Assert.IsTrue(shop.DoorOpen);
            Assert.IsTrue(s.ToggleDoor(shop.Id).Ok);
            Assert.IsFalse(shop.DoorOpen);
            Assert.IsFalse(s.SetDoorLocked(shop.Id, true).Ok, "an interior door without a lock cannot be locked");

            Structure flat3 = Door(s, "door:flat3");
            StandAt(s, flat3);
            OpResult refused = s.ToggleDoor(flat3.Id);
            Assert.IsFalse(refused.Ok);
            StringAssert.Contains("Flat 3", refused.Message);
            Assert.IsFalse(flat3.DoorOpen);

            s.Player.Inventory.TryAddStack(new ItemStack(DoorRules.KeyItem, 1, 0, Storage));
            Assert.IsFalse(s.ToggleDoor(flat3.Id).Ok, "the wrong key does not fit");
            s.Player.Inventory.TryAddStack(new ItemStack(DoorRules.KeyItem, 1, 0, Flat3));
            OpResult opened = s.ToggleDoor(flat3.Id);
            Assert.IsTrue(opened.Ok, opened.Message);
            Assert.IsTrue(flat3.DoorOpen && !flat3.DoorLocked);
            Assert.IsFalse(s.SetDoorLocked(flat3.Id, true).Ok, "close it before locking");
            s.ToggleDoor(flat3.Id);
            Assert.IsTrue(s.SetDoorLocked(flat3.Id, true).Ok);
            Assert.IsTrue(flat3.DoorLocked);

            s.Player.Position = flat3.Position + new Vec3(30f, 0f, 0f);
            Assert.IsFalse(s.ToggleDoor(flat3.Id).Ok, "out of reach");

            // A door survives a save with its state.
            string text = SaveSerializer.Serialize(s.World, "t", 1);
            WorldState l = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            Structure loaded = l.Structures[flat3.Id];
            Assert.IsTrue(loaded.DoorLocked && !loaded.DoorOpen && loaded.LockId == Flat3 && loaded.SeedKey == flat3.SeedKey);
        }

        [Test]
        public void BrokenDoors_StayOpen_UntilRepaired()
        {
            GameSession s = TestSupport.NewSession();
            Structure door = Door(s, "door:flat2");
            BuildingDefinition def = TestSupport.Content.Building(door.DefId);
            s.Construction.Damage(door, def.MaxHealth * 0.8f, "test");
            Assert.IsTrue(DoorRules.IsBroken(door, def) && DoorRules.IsPassable(door, def));
            StandAt(s, door);
            Assert.IsFalse(s.ToggleDoor(door.Id).Ok, "a broken door cannot be closed");
            s.Player.Inventory.TryAdd("hammer", 1);
            s.Player.Inventory.TryAdd("planks", 10);
            s.Player.Inventory.TryAdd("nails", 10);
            OpResult fixedIt = s.Repair(door.Id);
            Assert.IsTrue(fixedIt.Ok, fixedIt.Message);
            Assert.IsFalse(DoorRules.IsBroken(door, def));
            Assert.IsTrue(s.ToggleDoor(door.Id).Ok && door.DoorOpen);
        }

        [Test]
        public void TheInfected_BatterClosedDoors_AndWalkThroughOpenOnes()
        {
            GameSession s = TestSupport.NewSession();
            Structure door = Door(s, "door:shop");
            Assert.IsFalse(door.DoorOpen);
            Assert.AreSame(door, s.Zombies.NearestBlocker(door.Position, 1.5f), "a closed world door is a barrier the infected batter");
            Assert.IsTrue(s.Zombies.IsInsideBlocker(door.Position, 0.1f));
            door.DoorOpen = true;
            Assert.IsNull(s.Zombies.NearestBlocker(door.Position, 1.5f), "an open door is not battered");
            Assert.IsFalse(s.Zombies.IsInsideBlocker(door.Position, 0.1f));

            // Colonists open unlocked doors on their way, never locked ones.
            door.DoorOpen = false;
            Assert.IsTrue(DoorRules.OpenForColonist(door, TestSupport.Content.Building(door.DefId)).Ok && door.DoorOpen);
            Structure flat3 = Door(s, "door:flat3");
            Assert.IsFalse(DoorRules.OpenForColonist(flat3, TestSupport.Content.Building(flat3.DefId)).Ok);
            s.CloseDoorBehind(door.Id);
            Assert.IsFalse(door.DoorOpen);
        }

        [Test]
        public void AnOlderSave_GetsTheNewBuildingsFixtures_Once()
        {
            GameSession s = TestSupport.NewSession();
            var keys = s.World.Structures.Values.Where(x => x.SeedKey != "").ToDictionary(x => x.SeedKey, x => x.Storage?.Totals());
            Assert.Greater(keys.Count, 30);
            // Simulate a save from before the building: remove its fixtures, then load.
            foreach (Structure st in s.World.Structures.Values.Where(x => x.SeedKey != "").ToList()) s.World.RemoveStructure(st.Id);
            string text = SaveSerializer.Serialize(s.World, "old", 1);
            var store = new SaveFileStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "afterfall-doors-" + Guid.NewGuid().ToString("N")));
            var saves = new SaveService(store);
            store.Write("old", text);
            LoadResult loaded = saves.Load("old", TestSupport.Content);
            Assert.IsTrue(loaded.Ok, loaded.Error);
            WorldState w = loaded.Session.World;
            var again = w.Structures.Values.Where(x => x.SeedKey != "").ToDictionary(x => x.SeedKey, x => x.Storage?.Totals());
            CollectionAssert.AreEquivalent(keys.Keys, again.Keys);
            foreach (var kv in keys)
                if (kv.Value != null) CollectionAssert.AreEquivalent(kv.Value, again[kv.Key], "the same loot rolls in an old world as in a new one: " + kv.Key);
            Assert.AreEqual(0, WorldSeeding.EnsureFixtures(w, TestSupport.Content));
            System.IO.Directory.Delete(store.Directory, true);
        }

        [Test]
        public void ThePlan_PutsEveryDoorInADoorway_AndEveryStairUnderAHole()
        {
            BuildingPlan p = BuildingPlans.MillbrookApartments();
            foreach (PlanFixture f in p.Fixtures.Where(x => x.DefId.StartsWith("door", StringComparison.Ordinal)))
            {
                bool inOpening = p.Walls.Any(w => w.Floor == f.Floor && w.Openings.Any(o => o.Kind == PlanOpeningKind.Door && OpeningContains(w, o, f.X, f.Z)));
                Assert.IsTrue(inOpening, $"{f.Key} at ({f.X}, {f.Z}) on floor {f.Floor} stands in a door opening");
            }
            foreach (PlanFixture f in p.Fixtures.Where(x => !x.DefId.StartsWith("door", StringComparison.Ordinal)))
            {
                Assert.IsNotNull(p.RoomAt(f.Floor, f.X, f.Z), $"{f.Key} is inside a room");
                Assert.IsTrue(Math.Abs(f.X) < p.Width * 0.5f - 0.2f && Math.Abs(f.Z) < p.Depth * 0.5f - 0.2f, $"{f.Key} is inside the walls");
            }
            foreach (PlanStair st in p.Stairs)
            {
                PlanHole hole = p.Holes.SingleOrDefault(h => h.Floor == st.FromFloor + 1);
                Assert.IsNotNull(hole, $"the flight from floor {st.FromFloor} rises through a hole in floor {st.FromFloor + 1}");
                Assert.AreEqual(Math.Min(st.X0, st.X1), Math.Min(hole.X0, hole.X1), 1e-3);
                Assert.AreEqual(Math.Max(st.X0, st.X1), Math.Max(hole.X0, hole.X1), 1e-3);
                float run = Math.Abs(st.ZEnd - st.ZStart);
                double slope = Math.Atan2(p.FloorHeight, run) * 180.0 / Math.PI;
                Assert.Less(slope, 35.0, "stairs are walkable for people and NavMesh agents");
                Assert.GreaterOrEqual(Math.Abs(st.X1 - st.X0), 1.4f, "flights are wide enough for agents to pass");
            }
            foreach (PlanWall w in p.Walls)
                foreach (PlanOpening o in w.Openings.Where(x => x.Kind == PlanOpeningKind.Door))
                    Assert.GreaterOrEqual(o.Width, 1.6f, "doorways are at least 1.6 m wide for NavMesh agents");
            Assert.AreEqual("Flat 3 · study", p.RoomAt(2, 3f, 3f).Name);
        }

        private static bool OpeningContains(PlanWall w, PlanOpening o, float x, float z)
        {
            float u = w.AlongX ? x - Math.Min(w.X0, w.X1) : z - Math.Min(w.Z0, w.Z1);
            float off = w.AlongX ? Math.Abs(z - w.Z0) : Math.Abs(x - w.X0);
            return off < 0.2f && Math.Abs(u - o.Center) < 0.05f;
        }
    }
}
