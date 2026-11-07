using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class ContentTests
    {
        [Test]
        public void ShippedContent_HasNoValidationErrors()
        {
            List<string> errors = ContentValidator.Validate(TestSupport.Content);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void ShippedContent_MeetsPrototypeScope()
        {
            ContentDatabase c = TestSupport.Content;
            Assert.GreaterOrEqual(c.Items.Count, 15);
            Assert.GreaterOrEqual(c.Recipes.Count, 8);
            Assert.GreaterOrEqual(c.Buildings.Values.Count(b => b.Buildable), 8);
            Assert.GreaterOrEqual(c.Buildings.Values.Count(b => b.Machine != null || !string.IsNullOrEmpty(b.CraftingStation)), 2);
        }

        [Test]
        public void IndustrialChain_IsReachableFromRawResources()
        {
            // Iron ore -> ingot (furnace) -> gear (workshop) -> water pump
            ContentDatabase c = TestSupport.Content;
            Assert.IsTrue(c.Recipes.Values.Any(r => r.Station == "furnace" && r.Outputs.Any(o => o.ItemId == "iron_ingot") && r.Inputs.Any(i => i.ItemId == "iron_ore")));
            Assert.IsTrue(c.Recipes.Values.Any(r => r.Station == "workshop" && r.Outputs.Any(o => o.ItemId == "gear")));
            Assert.IsTrue(c.Building("water_pump").Cost.Any(a => a.ItemId == "gear"));
        }

        [Test]
        public void LootTable_RollIsDeterministic()
        {
            LootTable t = TestSupport.Content.LootTables["workshop"];
            List<ItemAmount> a = t.Roll(new DeterministicRandom(7), 1f);
            List<ItemAmount> b = t.Roll(new DeterministicRandom(7), 1f);
            CollectionAssert.AreEqual(a.Select(x => x.ToString()), b.Select(x => x.ToString()));
        }

        [Test]
        public void GuaranteedLoot_SurvivesLowAbundance()
        {
            List<ItemAmount> loot = TestSupport.Content.LootTables["schematic"].Roll(new DeterministicRandom(3), 0.1f);
            Assert.IsTrue(loot.Any(a => a.ItemId == "schematic_generator"));
        }
    }

    public class CraftingTests
    {
        private static TechnologyState StartTech()
        {
            var state = new TechnologyState();
            new TechnologyService(TestSupport.Content.Techs, state, null).Evaluate();
            return state;
        }

        [Test]
        public void Check_ReportsMissingIngredients()
        {
            Inventory inv = TestSupport.NewInventory(10);
            inv.TryAdd("fiber", 2);
            CraftCheck c = CraftingRules.Check(TestSupport.Content.Recipe("rope"), inv, StartTech(), new HashSet<string>());
            Assert.IsFalse(c.CanCraft);
            Assert.AreEqual(1, c.Missing.Count);
            Assert.AreEqual(2, c.Missing[0].Quantity);
            StringAssert.Contains("Plant Fiber", c.Reason);
        }

        [Test]
        public void Check_RequiresStationNearby()
        {
            Inventory inv = TestSupport.NewInventory(10);
            inv.TryAdd("wood", 10);
            CraftCheck c = CraftingRules.Check(TestSupport.Content.Recipe("saw_planks"), inv, StartTech(), new HashSet<string>());
            Assert.IsTrue(c.StationMissing);
            StringAssert.Contains("Workbench", c.Reason);
            Assert.IsTrue(CraftingRules.Check(TestSupport.Content.Recipe("saw_planks"), inv, StartTech(), new HashSet<string> { "workbench" }).CanCraft);
        }

        [Test]
        public void Check_LockedRecipeExplainsTechnology()
        {
            Inventory inv = TestSupport.NewInventory(10);
            inv.TryAdd("iron_ingot", 5);
            inv.TryAdd("wood", 5);
            inv.TryAdd("rope", 2);
            var state = StartTech();
            var svc = new TechnologyService(TestSupport.Content.Techs, state, null);
            CraftCheck c = CraftingRules.Check(TestSupport.Content.Recipe("metal_axe"), inv, state, new HashSet<string> { "workbench" }, svc);
            Assert.IsTrue(c.Locked);
            StringAssert.Contains("Metalworking", c.Reason);
        }

        [Test]
        public void Queue_ConsumesInputsOnEnqueue_AndDeliversOnCompletion()
        {
            Inventory inv = TestSupport.NewInventory(10);
            inv.TryAdd("fiber", 8);
            var q = new CraftingQueue();
            RecipeDefinition rope = TestSupport.Content.Recipe("rope");
            Assert.IsTrue(q.Enqueue(rope, inv, StartTech(), null).Ok);
            Assert.AreEqual(4, inv.Count("fiber"));
            Assert.IsNull(q.Tick(1.0, inv, TestSupport.Content.Recipes));
            Assert.AreEqual(rope, q.Tick(1.1, inv, TestSupport.Content.Recipes));
            Assert.AreEqual(1, inv.Count("rope"));
        }

        [Test]
        public void Queue_WaitsForSpace_WhenOutputDoesNotFit()
        {
            Inventory inv = TestSupport.NewInventory(1);
            inv.TryAdd("fiber", 4);
            var q = new CraftingQueue();
            q.Enqueue(TestSupport.Content.Recipe("rope"), inv, StartTech(), null);
            inv.TryAdd("stone", 30); // fill the only slot
            Assert.IsNull(q.Tick(5, inv, TestSupport.Content.Recipes));
            Assert.IsTrue(q.Jobs[0].AwaitingSpace);
            inv.TryRemove("stone", 30);
            Assert.IsNotNull(q.Tick(0.1, inv, TestSupport.Content.Recipes));
            Assert.AreEqual(1, inv.Count("rope"));
        }

        [Test]
        public void Cancel_RefundsInputs()
        {
            Inventory inv = TestSupport.NewInventory(10);
            inv.TryAdd("wood", 2);
            inv.TryAdd("stone", 3);
            inv.TryAdd("rope", 1);
            var q = new CraftingQueue();
            Assert.IsTrue(q.Enqueue(TestSupport.Content.Recipe("stone_axe"), inv, StartTech(), null).Ok);
            Assert.IsTrue(inv.IsEmpty);
            Assert.IsTrue(q.Cancel(0, inv, TestSupport.Content.Recipes).Ok);
            Assert.AreEqual(2, inv.Count("wood"));
            Assert.AreEqual(3, inv.Count("stone"));
            Assert.AreEqual(1, inv.Count("rope"));
        }
    }

    public class TechnologyTests
    {
        [Test]
        public void Start_UnlocksSurvivalOnly()
        {
            var state = new TechnologyState();
            new TechnologyService(TestSupport.Content.Techs, state, null).Evaluate();
            Assert.IsTrue(state.IsUnlocked("survival"));
            Assert.IsFalse(state.IsUnlocked("metalworking"));
        }

        [Test]
        public void Metalworking_UnlocksAfterWorkbenchAndIronOre()
        {
            var state = new TechnologyState();
            var bus = new EventBus();
            var unlocked = new List<string>();
            bus.Subscribe<Afterfall.Domain.Events.TechnologyUnlocked>(e => unlocked.Add(e.TechId));
            var svc = new TechnologyService(TestSupport.Content.Techs, state, bus);
            svc.Evaluate();
            svc.Record(UnlockTrigger.AcquireItem, "iron_ore");
            Assert.IsFalse(state.IsUnlocked("metalworking"));
            StringAssert.Contains("Build a Workbench (0/1)", svc.DescribeRequirements("metalworking"));
            svc.Record(UnlockTrigger.BuildStructure, "workbench");
            Assert.IsTrue(state.IsUnlocked("metalworking"));
            CollectionAssert.Contains(unlocked, "metalworking");
        }

        [Test]
        public void Electricity_RequiresPrerequisiteEvenIfConditionsMet()
        {
            var state = new TechnologyState();
            var svc = new TechnologyService(TestSupport.Content.Techs, state, null);
            svc.Evaluate();
            svc.Record(UnlockTrigger.AcquireItem, "schematic_generator");
            svc.Record(UnlockTrigger.CraftItem, "iron_ingot", 2);
            Assert.IsFalse(state.IsUnlocked("electricity"));
            svc.Record(UnlockTrigger.BuildStructure, "workbench");
            svc.Record(UnlockTrigger.AcquireItem, "iron_ore");
            Assert.IsTrue(state.IsUnlocked("metalworking"));
            Assert.IsTrue(state.IsUnlocked("electricity"), "cascading unlock once the prerequisite is met");
        }
    }

    public class SchedulerTests
    {
        private sealed class Counter : ISimulationSystem
        {
            public int Ticks;
            public double Total;
            public string Name => "counter";

            public void Tick(double dt)
            {
                Ticks++;
                Total += dt;
            }
        }

        [Test]
        public void SystemsTickAtTheirOwnFrequency()
        {
            var s = new TickScheduler();
            var fast = new Counter();
            var slow = new Counter();
            s.Register(fast, 10);
            s.Register(slow, 0.5);
            for (int i = 0; i < 320; i++) s.Advance(1.0 / 32); // 10 seconds at 32 fps (exact binary fractions)
            Assert.AreEqual(100, fast.Ticks, 1);
            Assert.AreEqual(5, slow.Ticks);
            Assert.AreEqual(10.0, fast.Total, 0.11);
        }

        [Test]
        public void LargeHitch_IsCappedInsteadOfSpiralling()
        {
            var s = new TickScheduler();
            var c = new Counter();
            s.Register(c, 10);
            s.Advance(60);
            Assert.AreEqual(TickScheduler.MaxStepsPerAdvance, c.Ticks);
            Assert.Greater(s.DroppedSeconds, 50);
        }

        [Test]
        public void Clock_DayLengthDefaultsTo48Minutes()
        {
            var clock = new SimulationClock();
            double start = clock.TimeOfDay01;
            clock.Advance(48 * 60);
            Assert.AreEqual(start, clock.TimeOfDay01, 1e-9);
            Assert.AreEqual(2, clock.Day);
        }
    }
}
