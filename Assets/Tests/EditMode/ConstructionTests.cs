using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class ConstructionTests
    {
        private WorldState _w;
        private ConstructionService _svc;
        private TechnologyService _tech;

        [SetUp]
        public void SetUp()
        {
            _w = TestSupport.BareWorld();
            _tech = new TechnologyService(TestSupport.Content.Techs, _w.Tech, null);
            _tech.Evaluate();
            _svc = new ConstructionService(_w, TestSupport.Content, null, _tech);
        }

        private Structure Place(string def, Vec3 pos, PlacementProbe probe = null)
        {
            Assert.IsTrue(_svc.PlaceBlueprint(def, pos, 0, probe ?? new PlacementProbe(), _w.Player.Inventory, out Structure s).Ok);
            return s;
        }

        [Test]
        public void Placement_ExplainsEveryProblem()
        {
            BuildingDefinition wall = TestSupport.Content.Building("wall");
            var probe = new PlacementProbe { OnSupport = false, SlopeDegrees = 40, Blocked = true, BlockedBy = "Pine Tree", Distance = 20, MaxDistance = 12 };
            PlacementResult r = PlacementRules.Evaluate(wall, probe, _w.Tech, _tech, _w.Player.Inventory);
            Assert.IsFalse(r.Valid);
            Assert.IsTrue(r.Reasons.Any(x => x.Contains("foundation")));
            Assert.IsTrue(r.Reasons.Any(x => x.Contains("too steep") || x.Contains("steep")));
            Assert.IsTrue(r.Reasons.Any(x => x.Contains("Pine Tree")));
            Assert.IsTrue(r.Reasons.Any(x => x.Contains("Too far")));
            Assert.IsTrue(r.MissingMaterials.Count > 0, "missing materials are reported for information");
        }

        [Test]
        public void Placement_LockedBuildingIsInvalid()
        {
            PlacementResult r = PlacementRules.Evaluate(TestSupport.Content.Building("generator"), new PlacementProbe(), _w.Tech, _tech, null);
            Assert.IsFalse(r.Valid);
            Assert.IsTrue(r.Reasons[0].Contains("Electrification"));
        }

        [Test]
        public void Placement_PumpNeedsWater()
        {
            _w.Tech.Unlocked.Add("water_works");
            PlacementResult r = PlacementRules.Evaluate(TestSupport.Content.Building("water_pump"), new PlacementProbe { WaterNearby = false }, _w.Tech, _tech, null);
            Assert.IsTrue(r.Reasons.Any(x => x.Contains("water")));
        }

        [Test]
        public void Blueprint_NeedsMaterialsBeforeWork()
        {
            Structure s = Place("workbench", new Vec3(5, 0, 5));
            Assert.AreEqual(StructureState.Blueprint, s.State);
            OpResult r = _svc.Work(s, 10, _w.Player.Inventory);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("6 Log", r.Message);
        }

        [Test]
        public void Blueprint_DeliversWorksAndCompletes()
        {
            Structure s = Place("storage_crate", new Vec3(5, 0, 5));
            _w.Player.Inventory.TryAdd("planks", 10);
            _w.Player.Inventory.TryAdd("nails", 10);
            Assert.IsTrue(_svc.Work(s, 1, _w.Player.Inventory).Ok);
            Assert.AreEqual(4, _w.Player.Inventory.Count("planks"));
            Assert.AreEqual(4, _w.Player.Inventory.Count("nails"));
            Assert.AreEqual(StructureState.Blueprint, s.State);
            _svc.Work(s, 5, _w.Player.Inventory);
            Assert.AreEqual(StructureState.Complete, s.State);
            Assert.IsNotNull(s.Storage);
            Assert.AreEqual(20, s.Storage.SlotCount);
            Assert.AreSame(s.Storage, _w.FindInventory(s.Storage.Id));
            Assert.AreEqual(1, _w.Metrics.StructuresBuilt);
        }

        [Test]
        public void PartialDelivery_IsRememberedAcrossVisits()
        {
            Structure s = Place("workbench", new Vec3(5, 0, 5));
            _w.Player.Inventory.TryAdd("wood", 4);
            _svc.Work(s, 1, _w.Player.Inventory);
            Assert.AreEqual(4, s.Delivered["wood"]);
            _w.Player.Inventory.TryAdd("wood", 5);
            _w.Player.Inventory.TryAdd("stone", 4);
            _svc.Work(s, 10, _w.Player.Inventory);
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(3, _w.Player.Inventory.Count("wood"), "only the 2 missing logs were taken");
        }

        [Test]
        public void CancelBlueprint_RefundsDeliveredMaterials()
        {
            Structure s = Place("workbench", new Vec3(5, 0, 5));
            _w.Player.Inventory.TryAdd("wood", 6);
            _svc.DeliverMaterials(s, _w.Player.Inventory);
            Assert.AreEqual(0, _w.Player.Inventory.Count("wood"));
            Assert.IsTrue(_svc.CancelBlueprint(s, _w.Player.Inventory).Ok);
            Assert.AreEqual(6, _w.Player.Inventory.Count("wood"));
            Assert.IsFalse(_w.Structures.ContainsKey(s.Id));
        }

        [Test]
        public void Refund_OverflowGoesToGroundInsteadOfBeingLost()
        {
            Structure s = Place("workbench", new Vec3(5, 0, 5));
            _w.Player.Inventory.TryAdd("wood", 6);
            _svc.DeliverMaterials(s, _w.Player.Inventory);
            for (int i = 0; i < _w.Player.Inventory.SlotCount; i++) _w.Player.Inventory.SetSlotRaw(i, new ItemStack("nails", 1));
            _svc.CancelBlueprint(s, _w.Player.Inventory);
            Assert.AreEqual(1, _w.Drops.Count);
            Assert.AreEqual(6, _w.Drops.Values.First().Contents.Count("wood"));
        }

        [Test]
        public void Demolish_RefundsHalfAndSpillsStorage()
        {
            Structure crate = TestSupport.Build(_w, "storage_crate", new Vec3(5, 0, 5));
            crate.Storage.TryAdd("stone", 25);
            Assert.IsTrue(_svc.Demolish(crate, _w.Player.Inventory).Ok);
            Assert.AreEqual(3, _w.Player.Inventory.Count("planks"));
            Assert.AreEqual(3, _w.Player.Inventory.Count("nails"));
            Assert.AreEqual(25, _w.Drops.Values.Sum(d => d.Contents.Count("stone")), "contents are dropped, not destroyed");
        }

        [Test]
        public void WorldPropsCannotBeDemolished()
        {
            Structure loot = TestSupport.Build(_w, "loot_crate", new Vec3(5, 0, 5));
            loot.PlayerBuilt = false;
            Assert.IsFalse(_svc.Demolish(loot, _w.Player.Inventory).Ok);
        }

        [Test]
        public void Repair_CostsProportionalMaterials()
        {
            Structure crate = TestSupport.Build(_w, "storage_crate", new Vec3(5, 0, 5));
            crate.Health = 75; // half of 150
            var cost = _svc.RepairCost(crate);
            Assert.AreEqual(2, cost.First(c => c.ItemId == "planks").Quantity); // ceil(6 * 0.5 * 0.5)
            _w.Player.Inventory.TryAdd("planks", 2);
            _w.Player.Inventory.TryAdd("nails", 2);
            Assert.IsTrue(_svc.Repair(crate, _w.Player.Inventory).Ok);
            Assert.AreEqual(150, crate.Health);
        }

        [Test]
        public void Destruction_SpillsMachineContents()
        {
            _w.Tech.Unlocked.Add("metalworking");
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(5, 0, 5));
            f.Machine.Input.TryAdd("iron_ore", 4);
            f.Machine.Fuel.TryAdd("wood", 3);
            Assert.IsTrue(_svc.Damage(f, 10000, "fire"));
            Assert.AreEqual(4, _w.Drops.Values.Sum(d => d.Contents.Count("iron_ore")));
            Assert.AreEqual(3, _w.Drops.Values.Sum(d => d.Contents.Count("wood")));
        }
    }
}
