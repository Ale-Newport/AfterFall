using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Farming;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class FarmingTests
    {
        private static GameSession Farm(out Structure plot)
        {
            GameSession s = TestSupport.NewSession(8080);
            s.Threats.SpawningEnabled = false;
            s.World.Tech.Unlocked.Add("agriculture");
            plot = TestSupport.Build(s.World, "planter_bed", new Vec3(s.Player.Position.X + 2, s.Player.Position.Y, s.Player.Position.Z));
            return s;
        }

        [Test]
        public void SeedsUnlockAgriculture()
        {
            GameSession s = TestSupport.NewSession(8081);
            Assert.IsFalse(s.World.Tech.IsUnlocked("agriculture"));
            s.Player.Inventory.TryAdd("seed_potato", 1);
            TestSupport.Run(s, 0.5);
            Assert.IsTrue(s.World.Tech.IsUnlocked("agriculture"));
        }

        [Test]
        public void Crop_GrowsOnlyWhileWatered_ThenHarvests()
        {
            GameSession s = Farm(out Structure plot);
            s.Player.Inventory.TryAdd("seed_potato", 2);
            s.Player.Inventory.TryAdd("water_bottle", 1);
            Assert.IsTrue(s.PlantCrop(plot.Id).Ok);
            Assert.AreEqual("occupied", s.PlantCrop(plot.Id).Code);
            TestSupport.Run(s, 60, 0.25);
            Assert.AreEqual(0f, plot.Crop.Growth, 1e-5, "dry soil: no growth");
            Assert.IsTrue(s.WaterCrop(plot.Id).Ok);
            Assert.AreEqual(1, s.Player.Inventory.Count("empty_bottle"), "the bottle comes back");
            TestSupport.Run(s, 120, 0.25);
            Assert.Greater(plot.Crop.Growth, 0.1f);
            Assert.AreEqual("not_ready", s.HarvestCrop(plot.Id).Code);
            plot.Crop.Growth = 1f;
            Assert.IsTrue(s.HarvestCrop(plot.Id).Ok);
            Assert.GreaterOrEqual(s.Player.Inventory.Count("potato"), 3);
            Assert.IsFalse(plot.Crop.Planted);
        }

        [Test]
        public void PoweredPump_IrrigatesNearbyPlots()
        {
            GameSession s = Farm(out Structure plot);
            s.World.Tech.Unlocked.Add("electricity");
            s.World.Tech.Unlocked.Add("water_works");
            Vec3 p = plot.Position;
            Structure gen = TestSupport.Build(s.World, "generator", new Vec3(p.X + 4, p.Y, p.Z));
            Structure pump = TestSupport.Build(s.World, "water_pump", new Vec3(p.X + 6, p.Y, p.Z));
            gen.Generator.Fuel.TryAdd("fuel_can", 1);
            pump.Machine.Input.TryAdd("empty_bottle", 5);
            s.Player.Inventory.TryAdd("seed_potato", 1);
            Assert.IsTrue(s.PlantCrop(plot.Id).Ok);
            TestSupport.Run(s, 30, 0.25);
            Assert.IsTrue(plot.Crop.Irrigated);
            Assert.Greater(plot.Crop.Growth, 0f);
        }

        [Test]
        public void CropState_SurvivesSaveLoad()
        {
            GameSession s = Farm(out Structure plot);
            s.Player.Inventory.TryAdd("seed_potato", 1);
            s.PlantCrop(plot.Id);
            plot.Crop.Water = 55f;
            plot.Crop.Growth = 0.42f;
            WorldState l = SaveSerializer.Deserialize(SaveSerializer.Serialize(s.World, "f", 0), TestSupport.Content, null);
            CropState c = l.Structures[plot.Id].Crop;
            Assert.AreEqual("seed_potato", c.SeedId);
            Assert.AreEqual(0.42f, c.Growth, 1e-5);
            Assert.AreEqual(55f, c.Water, 1e-5);
        }
    }
}
