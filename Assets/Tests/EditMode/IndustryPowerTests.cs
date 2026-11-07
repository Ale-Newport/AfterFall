using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Power;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class MachineTests
    {
        private WorldState _w;
        private MachineSystem _machines;
        private PowerSystem _power;

        [SetUp]
        public void SetUp()
        {
            _w = TestSupport.BareWorld();
            var tech = new TechnologyService(TestSupport.Content.Techs, _w.Tech, null);
            tech.Evaluate();
            _w.Tech.Unlocked.Add("metalworking");
            _w.Tech.Unlocked.Add("electricity");
            _machines = new MachineSystem(_w, TestSupport.Content, null, tech);
            _power = new PowerSystem(_w, TestSupport.Content, null);
        }

        private void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += 0.25)
            {
                _power.Tick(0.25);
                _machines.Tick(0.25);
                _w.Clock.Advance(0.25);
            }
        }

        [Test]
        public void Furnace_ReportsEveryBlockingReason()
        {
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(0, 0, 0));
            Run(0.5);
            Assert.AreEqual(MachineStatus.RecipeNotConfigured, f.Machine.Status);
            Assert.IsTrue(_machines.SetRecipe(f, "smelt_iron").Ok);
            Run(0.5);
            Assert.AreEqual(MachineStatus.WaitingForInput, f.Machine.Status);
            StringAssert.Contains("2 Iron Ore", f.Machine.StatusDetail);
            f.Machine.Input.TryAdd("iron_ore", 4);
            Run(0.5);
            Assert.AreEqual(MachineStatus.MissingFuel, f.Machine.Status);
            f.Machine.Fuel.TryAdd("wood", 1);
            Run(0.5);
            Assert.AreEqual(MachineStatus.Working, f.Machine.Status);
        }

        [Test]
        public void Furnace_ConsumesInputsAndFuel_ProducesOutputs()
        {
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(0, 0, 0));
            _machines.SetRecipe(f, "smelt_iron");
            f.Machine.Input.TryAdd("iron_ore", 4);
            f.Machine.Fuel.TryAdd("wood", 2); // 30 s of burn
            Run(17); // two 8 s cycles
            Assert.AreEqual(2, f.Machine.Output.Count("iron_ingot"));
            Assert.AreEqual(0, f.Machine.Input.Count("iron_ore"));
            Assert.AreEqual(0, f.Machine.Fuel.Count("wood"), "second log ignited during the second cycle");
            Assert.AreEqual(MachineStatus.WaitingForInput, f.Machine.Status);
        }

        [Test]
        public void Furnace_PausesWhenFuelRunsOut_AndResumesWhenRefuelled()
        {
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(0, 0, 0));
            _machines.SetRecipe(f, "charcoal"); // 10 s cycle
            f.Machine.Input.TryAdd("wood", 3);
            f.Machine.Fuel.TryAdd("fiber", 1); // 2 s of burn only
            Run(4);
            Assert.AreEqual(MachineStatus.MissingFuel, f.Machine.Status);
            float progress = f.Machine.Progress;
            Assert.Greater(progress, 1.5f);
            Run(5);
            Assert.AreEqual(progress, f.Machine.Progress, 1e-3, "no progress without fuel");
            f.Machine.Fuel.TryAdd("charcoal", 1);
            Run(10);
            Assert.AreEqual(2, f.Machine.Output.Count("charcoal"));
        }

        [Test]
        public void Machine_StopsWhenOutputFull()
        {
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(0, 0, 0));
            _machines.SetRecipe(f, "smelt_scrap");
            for (int i = 0; i < f.Machine.Output.SlotCount; i++) f.Machine.Output.SetSlotRaw(i, new Domain.Items.ItemStack("iron_ingot", 30));
            f.Machine.Input.TryAdd("scrap_metal", 8);
            f.Machine.Fuel.TryAdd("charcoal", 2);
            Run(2);
            Assert.AreEqual(MachineStatus.OutputFull, f.Machine.Status);
            Assert.AreEqual(8, f.Machine.Input.Count("scrap_metal"), "inputs are not consumed when output cannot be stored");
        }

        [Test]
        public void SemiAutomation_PullsFromAndPushesToAdjacentCrate()
        {
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(0, 0, 0));
            Structure crate = TestSupport.Build(_w, "storage_crate", new Vec3(2, 0, 0));
            Structure far = TestSupport.Build(_w, "storage_crate", new Vec3(20, 0, 0));
            _machines.SetRecipe(f, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 6);
            crate.Storage.TryAdd("charcoal", 3);
            far.Storage.TryAdd("iron_ore", 10);
            Run(30);
            Assert.AreEqual(3, crate.Storage.Count("iron_ingot") + f.Machine.Output.Count("iron_ingot"));
            Assert.GreaterOrEqual(crate.Storage.Count("iron_ingot"), 2, "outputs pushed into the adjacent crate");
            Assert.AreEqual(10, far.Storage.Count("iron_ore"), "distant storage is untouched");
        }

        [Test]
        public void ChangingRecipeMidCycle_IsRefused()
        {
            Structure f = TestSupport.Build(_w, "furnace", new Vec3(0, 0, 0));
            _machines.SetRecipe(f, "smelt_iron");
            f.Machine.Input.TryAdd("iron_ore", 2);
            f.Machine.Fuel.TryAdd("wood", 1);
            Run(1);
            Assert.IsTrue(f.Machine.CycleActive);
            Assert.AreEqual("busy", _machines.SetRecipe(f, "charcoal").Code);
        }
    }

    public class PowerTests
    {
        private WorldState _w;
        private MachineSystem _machines;
        private PowerSystem _power;

        [SetUp]
        public void SetUp()
        {
            _w = TestSupport.BareWorld();
            var tech = new TechnologyService(TestSupport.Content.Techs, _w.Tech, null);
            tech.Evaluate();
            _w.Tech.Unlocked.Add("metalworking");
            _w.Tech.Unlocked.Add("electricity");
            _machines = new MachineSystem(_w, TestSupport.Content, null, tech);
            _power = new PowerSystem(_w, TestSupport.Content, null);
        }

        private void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += 0.25)
            {
                _power.Tick(0.25);
                _machines.Tick(0.25);
                _w.Clock.Advance(0.25);
            }
        }

        private Structure Workshop(Vec3 p)
        {
            Structure ws = TestSupport.Build(_w, "workshop", p);
            ws.Machine.RecipeId = "gears";
            ws.Machine.Input.TryAdd("iron_ingot", 60);
            return ws;
        }

        [Test]
        public void Workshop_WithoutGenerator_ReportsNoPowerSource()
        {
            Structure ws = Workshop(new Vec3(0, 0, 0));
            Run(1);
            Assert.AreEqual(MachineStatus.MissingPower, ws.Machine.Status);
            StringAssert.Contains("No power source", ws.Machine.StatusDetail);
        }

        [Test]
        public void Generator_PowersWorkshop_StopsWhenFuelRunsOut_ResumesWhenRefuelled()
        {
            Structure gen = TestSupport.Build(_w, "generator", new Vec3(0, 0, 0));
            Structure ws = Workshop(new Vec3(3, 0, 0));
            Assert.IsTrue(_w.HasPowerLink(gen.Id, ws.Id), "auto-connected within range");
            gen.Generator.Fuel.TryAdd("charcoal", 1); // 150 kJ = 300 s at 500 W
            Run(13);
            Assert.AreEqual(MachineStatus.Working, ws.Machine.Status);
            Assert.GreaterOrEqual(ws.Machine.Output.Count("gear"), 2);
            Run(300);
            Assert.AreEqual(MachineStatus.MissingPower, ws.Machine.Status);
            StringAssert.Contains("out of fuel", ws.Machine.StatusDetail);
            int gears = ws.Machine.Output.Count("gear");
            Run(20);
            Assert.AreEqual(gears, ws.Machine.Output.Count("gear"), "no production without power");
            gen.Generator.Fuel.TryAdd("fuel_can", 1);
            Run(13);
            Assert.AreEqual(MachineStatus.Working, ws.Machine.Status);
            Assert.Greater(ws.Machine.Output.Count("gear"), gears);
        }

        [Test]
        public void FuelIsConsumedProportionallyToLoad()
        {
            Structure gen = TestSupport.Build(_w, "generator", new Vec3(0, 0, 0));
            gen.Generator.Fuel.TryAdd("fuel_can", 2);
            Run(60); // no load connected
            Assert.AreEqual(2, gen.Generator.Fuel.Count("fuel_can"), "an idle generator burns nothing");
            Workshop(new Vec3(3, 0, 0));
            Run(60);
            Assert.AreEqual(1, gen.Generator.Fuel.Count("fuel_can"), "one can loaded into the burner");
            Assert.AreEqual(600_000 - 500 * 60, gen.Generator.BufferJoules, 600);
        }

        [Test]
        public void Shortage_ShedsLowestPriorityFirst()
        {
            Structure gen = TestSupport.Build(_w, "generator", new Vec3(0, 0, 0));
            gen.Generator.Fuel.TryAdd("fuel_can", 5);
            // 4 workshops (priority 2, 500 W each) + a lamp at night (priority 4, 60 W) = 2060 W > 2000 W
            var shops = new[] { Workshop(new Vec3(2, 0, 0)), Workshop(new Vec3(-2, 0, 0)), Workshop(new Vec3(0, 0, 2)), Workshop(new Vec3(0, 0, -2)) };
            Structure lamp = TestSupport.Build(_w, "lamp", new Vec3(1, 0, 1));
            _w.Clock.Advance(_w.Clock.DayLengthSeconds * 0.6); // into the night
            Run(2);
            foreach (Structure s in shops) Assert.IsTrue(s.Power.Powered, "high-priority machines keep power");
            Assert.IsFalse(lamp.Power.Powered);
            StringAssert.Contains("shed by priority", lamp.Power.Reason);
        }

        [Test]
        public void Battery_ChargesFromSurplus_AndCoversGeneratorOutage()
        {
            Structure gen = TestSupport.Build(_w, "generator", new Vec3(0, 0, 0));
            Structure bat = TestSupport.Build(_w, "battery_bank", new Vec3(2, 0, 0));
            gen.Generator.Fuel.TryAdd("fuel_can", 1);
            Run(10);
            Assert.Greater(bat.Battery.StoredJoules, 10_000, "surplus charges the battery");
            double stored = bat.Battery.StoredJoules;
            gen.Enabled = false;
            Structure ws = Workshop(new Vec3(-2, 0, 0));
            Run(6);
            Assert.AreEqual(MachineStatus.Working, ws.Machine.Status, "battery carries the load");
            Assert.Less(bat.Battery.StoredJoules, stored);
        }

        [Test]
        public void Topology_RebuildsOnlyWhenChanged()
        {
            Structure gen = TestSupport.Build(_w, "generator", new Vec3(0, 0, 0));
            Run(5);
            int rebuilds = _power.TopologyRebuilds;
            Run(5);
            Assert.AreEqual(rebuilds, _power.TopologyRebuilds);
            TestSupport.Build(_w, "lamp", new Vec3(2, 0, 0));
            Run(0.5);
            Assert.AreEqual(rebuilds + 1, _power.TopologyRebuilds);
            Assert.AreEqual(1, _power.Networks.Count);
        }

        [Test]
        public void Cables_RespectRange_AndPolesRelay()
        {
            Structure gen = TestSupport.Build(_w, "generator", new Vec3(0, 0, 0));
            Structure far = Workshop(new Vec3(20, 0, 0));
            Assert.IsFalse(_w.HasPowerLink(gen.Id, far.Id));
            Assert.IsFalse(PowerLinker.CanLink(_w, TestSupport.Content, gen, far, out string reason));
            StringAssert.Contains("power pole", reason);
            TestSupport.Build(_w, "power_pole", new Vec3(10, 0, 0));
            gen.Generator.Fuel.TryAdd("fuel_can", 1);
            Run(1);
            Assert.AreEqual(1, _power.Networks.Count, "pole joins generator and workshop into one network");
            Assert.IsTrue(far.Power.Powered);
        }
    }
}
