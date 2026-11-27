using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.Vehicles;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Vehicles: placement, keys and locks, hotwiring, driving and fuel, repairs, the infected, saves and the creative spawner.</summary>
    public sealed class VehicleTests
    {
        private static VehicleState Seeded(GameSession s, string key) => s.World.Vehicles.Values.Single(v => v.SeedKey == key);

        private static void StandBy(GameSession s, VehicleState v) => s.Player.Position = v.Position + new Vec3(1.8f, 0f, 0f);

        [Test]
        public void NewGame_PlacesThreeAbandonedVehicles_EachReachedADifferentWay()
        {
            GameSession s = TestSupport.NewSession();
            Assert.AreEqual(3, s.World.Vehicles.Count);
            VehicleState sedan = Seeded(s, "vehicle:millbrook_sedan");
            Assert.IsTrue(sedan.Locked);
            Structure kitchen = s.World.Structures.Values.Single(x => x.SeedKey == BuildingPlans.MillbrookApartmentsId + ":flat2:kitchen");
            Assert.IsTrue(kitchen.Storage.HasTagged(VehicleRules.KeyItem, sedan.LockId), "the sedan's key hangs in flat 2's kitchen");
            VehicleState pickup = Seeded(s, "vehicle:hartley_pickup");
            Assert.IsFalse(pickup.Locked);
            Assert.IsFalse(s.World.AllInventories.Any(inv => inv.HasTagged(VehicleRules.KeyItem, pickup.LockId)), "the pickup's key is lost: it must be hotwired");
            VehicleState atv = Seeded(s, "vehicle:dawson_atv");
            Assert.IsTrue(atv.KeyInIgnition && atv.Fuel < 3f);
            Structure toolbox = s.World.Structures.Values.Single(x => x.SeedKey == BuildingPlans.MillbrookApartmentsId + ":storage:toolbox");
            Assert.Greater(toolbox.Storage.Count(VehicleRules.HotwireTool), 0, "a screwdriver is always in the apartments' storage room");
            foreach (VehicleState v in s.World.Vehicles.Values)
                Assert.IsTrue(s.World.Layout.IsInsideMap(v.Position.X, v.Position.Z, 10f) && !s.World.Layout.IsWater(v.Position.X, v.Position.Z));
        }

        [Test]
        public void LockedCar_NeedsItsKey_OrASmashedWindow()
        {
            GameSession s = TestSupport.NewSession();
            VehicleState sedan = Seeded(s, "vehicle:millbrook_sedan");
            StandBy(s, sedan);
            OpResult refused = s.Vehicles.Enter(sedan.Id);
            Assert.IsFalse(refused.Ok);
            StringAssert.Contains("key", refused.Message);
            Assert.IsFalse(s.Player.InVehicle);

            s.Player.Inventory.TryAddStack(new ItemStack(VehicleRules.KeyItem, 1, 0, "Some other car"));
            Assert.IsFalse(s.Vehicles.Enter(sedan.Id).Ok, "the wrong key does not fit");
            s.Player.Inventory.TryAddStack(new ItemStack(VehicleRules.KeyItem, 1, 0, sedan.LockId));
            OpResult entered = s.Vehicles.Enter(sedan.Id);
            Assert.IsTrue(entered.Ok, entered.Message);
            Assert.IsTrue(s.Player.InVehicle && !sedan.Locked && sedan.Known);
            Assert.IsTrue(s.Vehicles.ToggleEngine().Ok && sedan.EngineOn, "its key starts it");
            Assert.IsTrue(s.Vehicles.Exit().Ok);
            Assert.IsFalse(sedan.EngineOn, "getting out switches the engine off");
            Assert.IsTrue(s.Vehicles.SetLocked(sedan.Id, true).Ok && sedan.Locked);

            GameSession t = TestSupport.NewSession();
            VehicleState other = Seeded(t, "vehicle:millbrook_sedan");
            StandBy(t, other);
            Assert.IsTrue(t.Vehicles.SmashWindow(other.Id).Ok);
            Assert.IsTrue(!other.Locked && other.WindowBroken && other.Body < 71f);
            Assert.IsTrue(t.Vehicles.Enter(other.Id).Ok);
            OpResult noKey = t.Vehicles.ToggleEngine();
            Assert.IsFalse(noKey.Ok);
            StringAssert.Contains("hotwire", noKey.Message);
        }

        [Test]
        public void Hotwiring_NeedsAScrewdriverAndWire_IsAChance_AndHasConsequences()
        {
            GameSession s = TestSupport.NewSession();
            VehicleState pickup = Seeded(s, "vehicle:hartley_pickup");
            StandBy(s, pickup);
            Assert.IsTrue(s.Vehicles.Enter(pickup.Id).Ok);
            Assert.IsFalse(s.Vehicles.ToggleEngine().Ok, "no key");
            Assert.IsFalse(s.Vehicles.Hotwire().Ok, "no screwdriver");
            s.Player.Inventory.TryAdd(VehicleRules.HotwireTool, 1);
            Assert.IsFalse(s.Vehicles.Hotwire().Ok, "no wire");
            s.Player.Inventory.TryAdd(VehicleRules.HotwireWire, 30);
            Assert.IsTrue(s.Vehicles.CanTryHotwire(out string reason, out float chance) && reason == "");
            Assert.That(chance, Is.InRange(0.1f, 0.95f));

            int attempts = 0;
            float engineBefore = pickup.Engine;
            int failures = 0;
            while (!pickup.Hotwired && attempts < 25)
            {
                int wire = s.Player.Inventory.Count(VehicleRules.HotwireWire);
                OpResult r = s.Vehicles.Hotwire();
                attempts++;
                Assert.AreEqual(wire - 1, s.Player.Inventory.Count(VehicleRules.HotwireWire), "each attempt uses a length of wire");
                if (!r.Ok) failures++;
            }
            Assert.IsTrue(pickup.Hotwired, $"hotwired within {attempts} attempts");
            Assert.AreEqual(engineBefore - 4f * failures, pickup.Engine, 1e-3, "each failure chips the ignition");
            OpResult start = s.Vehicles.ToggleEngine();
            Assert.IsTrue(start.Ok && pickup.EngineOn, start.Message);
            Assert.IsFalse(s.Vehicles.Hotwire().Ok, "no need once it starts");

            // The same attempts with the same saved random stream give the same outcome.
            GameSession again = TestSupport.NewSession();
            VehicleState p2 = Seeded(again, "vehicle:hartley_pickup");
            StandBy(again, p2);
            again.Vehicles.Enter(p2.Id);
            again.Player.Inventory.TryAdd(VehicleRules.HotwireTool, 1);
            again.Player.Inventory.TryAdd(VehicleRules.HotwireWire, 30);
            int attempts2 = 0;
            while (!p2.Hotwired && attempts2 < 25)
            {
                again.Vehicles.Hotwire();
                attempts2++;
            }
            Assert.AreEqual(attempts, attempts2, "deterministic");
        }

        [Test]
        public void Driving_BurnsFuel_StallsWhenEmpty_AndRefuelling()
        {
            GameSession s = TestSupport.NewSession();
            VehicleState atv = Seeded(s, "vehicle:dawson_atv");
            VehicleDefinition def = TestSupport.Content.Vehicle("atv");
            StandBy(s, atv);
            Assert.IsTrue(s.Vehicles.Enter(atv.Id).Ok);
            Assert.IsTrue(s.Vehicles.ToggleEngine().Ok, "the key is in the ignition");
            float fuel = atv.Fuel;
            Vec3 p = atv.Position;
            s.Vehicles.ReportDriving(atv.Id, p + new Vec3(0f, 0f, 1000f), 0f, 1000f, 1f, 60.0, 16f);
            float wear = 1f + (100f - atv.Engine) / 200f; // a worn engine drinks more
            Assert.AreEqual(fuel - def.FuelPerKm * wear - def.IdleFuelPerHour / 60f, atv.Fuel, 1e-3f);
            Assert.AreEqual(1000.0, atv.OdometerMetres, 1e-6);
            Assert.AreEqual(p + new Vec3(0f, 0f, 1000f), s.Player.Position, "the survivor rides along");
            Assert.IsFalse(s.Vehicles.Exit().Ok, "not while moving fast");

            s.Vehicles.ReportDriving(atv.Id, p, 0f, 200000f, 1f, 10.0, 16f);
            Assert.AreEqual(0f, atv.Fuel);
            Assert.IsFalse(atv.EngineOn, "it stalls when the tank runs dry");
            Assert.IsFalse(s.Vehicles.ToggleEngine().Ok);
            s.Vehicles.ReportDriving(atv.Id, p, 0f, 0f, 0f, 0.1, 0f);
            Assert.IsTrue(s.Vehicles.Exit().Ok);
            Assert.IsFalse(s.Vehicles.Refuel(atv.Id).Ok, "no fuel can");
            s.Player.Inventory.TryAdd(VehicleRules.FuelItem, 1);
            OpResult refuel = s.Vehicles.Refuel(atv.Id);
            Assert.IsTrue(refuel.Ok, refuel.Message);
            Assert.AreEqual(def.FuelCapacity, atv.Fuel, 1e-3, "a can fills the small tank");
            Assert.AreEqual(0, s.Player.Inventory.Count(VehicleRules.FuelItem));
        }

        [Test]
        public void Crashes_Repairs_AndTheInfected()
        {
            GameSession s = TestSupport.NewSession();
            s.Threats.SpawningEnabled = false;
            VehicleState sedan = Seeded(s, "vehicle:millbrook_sedan");
            sedan.Locked = false;
            StandBy(s, sedan);
            Assert.IsTrue(s.Vehicles.Enter(sedan.Id).Ok);
            float body = sedan.Body;
            s.Vehicles.ReportCrash(sedan.Id, 20f);
            Assert.Less(sedan.Body, body);
            Assert.Less(s.Player.Vitals.Health, 100f, "a hard crash hurts the driver");

            // An infected attacking the driver tears at the car instead.
            float health = s.Player.Vitals.Health;
            ZombieState z = s.Zombies.Spawn(sedan.Position + new Vec3(1.5f, 0f, 0f), "runner", DeterministicRandom.Derive(3, "z"));
            float before = sedan.Body;
            Assert.IsTrue(s.Vehicles.AbsorbAttack(10f));
            Assert.Less(sedan.Body, before);
            Assert.AreEqual(health, s.Player.Vitals.Health, 1e-3);

            // Running one down at speed kills it.
            OpResult hit = s.Vehicles.HitInfected(sedan.Id, z.Id, 14f);
            Assert.IsTrue(hit.Ok && z.IsDead, hit.Message);

            s.Vehicles.ReportDriving(sedan.Id, sedan.Position, sedan.Yaw, 0f, 0f, 0.1, 0f);
            s.Vehicles.Exit();
            StandBy(s, sedan);
            Assert.IsFalse(s.Vehicles.Repair(sedan.Id, VehicleComponent.Body).Ok, "needs a hammer and scrap");
            s.Player.Inventory.TryAdd("hammer", 1);
            s.Player.Inventory.TryAdd("scrap_metal", 8);
            float b0 = sedan.Body;
            OpResult fixedBody = s.Vehicles.Repair(sedan.Id, VehicleComponent.Body);
            Assert.IsTrue(fixedBody.Ok, fixedBody.Message);
            Assert.AreEqual(System.Math.Min(100f, b0 + 35f), sedan.Body, 1e-3);
            Assert.AreEqual(4, s.Player.Inventory.Count("scrap_metal"));
        }

        [Test]
        public void Vehicles_SurviveSaveAndLoad_WithKeysTrunkAndDriver()
        {
            GameSession s = TestSupport.NewSession();
            VehicleState sedan = Seeded(s, "vehicle:millbrook_sedan");
            s.Player.Inventory.TryAddStack(new ItemStack(VehicleRules.KeyItem, 1, 0, sedan.LockId));
            StandBy(s, sedan);
            s.Vehicles.Enter(sedan.Id);
            s.Vehicles.ToggleEngine();
            s.Vehicles.ReportDriving(sedan.Id, sedan.Position + new Vec3(30f, 0f, 5f), 75f, 30.4f, 0.6f, 4.0, 2f);
            sedan.Storage.TryAdd("planks", 12);
            sedan.Claimed = true;

            string text = SaveSerializer.Serialize(s.World, "t", 1);
            WorldState l = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            VehicleState back = l.Vehicles[sedan.Id];
            Assert.AreEqual(sedan.Position, back.Position);
            Assert.AreEqual(75f, back.Yaw, 1e-3);
            Assert.AreEqual(sedan.Fuel, back.Fuel, 1e-4);
            Assert.AreEqual(sedan.LockId, back.LockId);
            Assert.AreEqual(sedan.Body, back.Body, 1e-4);
            Assert.AreEqual(12, back.Storage.Count("planks"));
            Assert.AreSame(back.Storage, l.FindInventory(back.Storage.Id), "the trunk is a registered inventory");
            Assert.IsTrue(back.Claimed && back.Known && back.SeedKey == sedan.SeedKey);
            Assert.AreEqual(sedan.Id, l.Player.VehicleId, "the survivor is still at the wheel");
            Assert.IsTrue(l.Player.Inventory.HasTagged(VehicleRules.KeyItem, sedan.LockId));
            Assert.AreEqual(30.4, back.OdometerMetres, 1e-3);
            Assert.AreEqual(0, WorldSeeding.EnsureFixtures(l, TestSupport.Content), "seeded vehicles are not created twice");
        }

        [Test]
        public void Creative_SpawnsVehicles_WithKeys_AndOnlyDeletesItsOwn()
        {
            GameSession s = TestSupport.NewSession();
            Assert.IsFalse(s.Creative.SpawnVehicle("sedan", s.Player.Position + new Vec3(6, 0, 0), 0f, new VehicleSpawnOptions(), out _).Ok, "locked in survival");
            s.Creative.EnableDeveloperTools();
            CollectionAssert.AreEquivalent(new[] { "atv", "pickup", "sedan" }, s.Creative.VehicleTypes().Select(d => d.Id).ToArray());
            Assert.AreEqual(1, s.Creative.VehicleTypes("quad").Count);
            OpResult made = s.Creative.SpawnVehicle("pickup", s.Player.Position + new Vec3(6, 0, 0), 30f, new VehicleSpawnOptions { FuelFraction = 0.5f, Condition = 60f, Locked = true, Paint = 2 }, out VehicleState v);
            Assert.IsTrue(made.Ok, made.Message);
            Assert.AreEqual(35f, v.Fuel, 1e-3);
            Assert.AreEqual(60f, v.Engine, 1e-3);
            Assert.IsTrue(v.Locked && v.DebugSpawned && v.Paint == 2);
            Assert.IsTrue(s.Player.Inventory.HasTagged(VehicleRules.KeyItem, v.LockId));
            Assert.IsTrue(s.Creative.ResetVehicle(v.Id).Ok && v.Fuel == 70f && v.Body == 100f);
            Assert.IsFalse(s.Creative.DeleteVehicle(Seeded(s, "vehicle:dawson_atv").Id).Ok, "world vehicles stay");
            v.Storage.TryAdd("planks", 5);
            Assert.IsTrue(s.Creative.DeleteVehicle(v.Id).Ok);
            Assert.IsFalse(s.World.Vehicles.ContainsKey(v.Id));
            Assert.IsTrue(s.World.Drops.Values.Any(d => d.Contents.Count("planks") == 5), "its trunk spills, nothing is lost");
        }
    }
}
