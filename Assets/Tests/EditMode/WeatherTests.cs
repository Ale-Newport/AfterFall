using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Weather;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class WeatherTests
    {
        private static GameSession Calm(int seed)
        {
            GameSession s = TestSupport.NewSession(seed);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            return s;
        }

        private static Vec3 Near(GameSession s, float dx, float dz)
        {
            Vec3 p = s.Player.Position;
            return new Vec3(p.X + dx, p.Y, p.Z + dz);
        }

        /// <summary>Runs only the weather on a bare world, one simulated second at a time.</summary>
        private static List<(double At, WeatherKind Kind)> Fronts(int seed, double days, List<(double At, string Message)> warnings = null)
        {
            WorldState w = TestSupport.BareWorld(seed);
            var bus = new EventBus();
            var weather = new WeatherSystem(w, TestSupport.Content, bus);
            var fronts = new List<(double, WeatherKind)>();
            bus.Subscribe<WeatherChanged>(e => fronts.Add((w.Clock.ElapsedSeconds, e.To)));
            if (warnings != null) bus.Subscribe<WeatherWarningIssued>(e => warnings.Add((w.Clock.ElapsedSeconds, e.Message)));
            double end = days * w.Clock.DayLengthSeconds;
            while (w.Clock.ElapsedSeconds < end)
            {
                w.Clock.Advance(1.0);
                weather.Tick(1.0);
            }
            return fronts;
        }

        [Test]
        public void Fronts_AreDeterministic_AndEveryStormIsForetold()
        {
            int storms = 0;
            foreach (int seed in new[] { 3, 17, 4242 })
            {
                var warnings = new List<(double At, string Message)>();
                List<(double At, WeatherKind Kind)> a = Fronts(seed, 10, warnings);
                List<(double At, WeatherKind Kind)> b = Fronts(seed, 10);
                CollectionAssert.AreEqual(a, b, "the same seed gives the same weather");
                Assert.Greater(a.Count, 10, "fronts change over ten days");
                double hour = SimulationClock.DefaultDayLengthSeconds / 24.0;
                foreach ((double at, WeatherKind kind) in a.Where(f => f.Kind == WeatherKind.Storm))
                {
                    storms++;
                    Assert.GreaterOrEqual(at, WeatherTuning.CalmStartHours * hour, "no storm in the first hours of a game");
                    bool foretold = warnings.Any(x => x.Message.Contains("storm") && at - x.At >= 0.9 * WeatherTuning.StormWarningHours * hour && at - x.At <= WeatherTuning.StormWarningHours * hour + 1);
                    Assert.IsTrue(foretold, $"seed {seed}: storm at {at:0} s was announced about {WeatherTuning.StormWarningHours} game hours ahead");
                }
            }
            Assert.Greater(storms, 0, "storms happen");
        }

        [Test]
        public void ColdSnaps_AreScheduledForEvenings_AndForetold()
        {
            WorldState w = TestSupport.BareWorld(9);
            var bus = new EventBus();
            var weather = new WeatherSystem(w, TestSupport.Content, bus);
            var warnings = new List<double>();
            var started = new List<double>();
            bus.Subscribe<WeatherWarningIssued>(e =>
            {
                if (e.Message.Contains("cold snap will set in")) warnings.Add(w.Clock.ElapsedSeconds);
                if (e.Message.Contains("cold snap has arrived")) started.Add(w.Clock.ElapsedSeconds);
            });
            double hour = w.Clock.SecondsPerGameHour;
            double minCold = double.MaxValue;
            while (w.Clock.ElapsedSeconds < 8 * w.Clock.DayLengthSeconds)
            {
                w.Clock.Advance(1.0);
                weather.Tick(1.0);
                minCold = System.Math.Min(minCold, WeatherReadout.Of(w).OutdoorC);
            }
            Assert.GreaterOrEqual(started.Count, 2, "a cold snap every two or three days");
            Assert.GreaterOrEqual(started[0], w.Clock.DayLengthSeconds, "the first night is spared");
            for (int i = 0; i < started.Count; i++)
            {
                Assert.IsTrue(warnings.Any(x => started[i] - x >= WeatherTuning.ColdSnapWarningHours * hour - 2 && started[i] - x <= WeatherTuning.ColdSnapWarningHours * hour + 2),
                    $"cold snap {i} was foretold three game hours ahead");
            }
            Assert.Less(minCold, -3f, "a cold snap brings frost");
        }

        [Test]
        public void ColdSnap_ChillsTheSurvivorInTheOpen_AndACampfireWarmsThem()
        {
            GameSession s = Calm(5150);
            s.Weather.ForceFront(WeatherKind.Clear, 30, true);
            s.Weather.ForceColdSnap(20);
            TestSupport.Run(s, 200, 0.25); // the snap sets in over 1.5 game hours
            Assert.Less(s.Player.FeltTemperatureC, 0f, "below freezing in the open");
            Assert.IsFalse(s.Player.Sheltered);

            s.Player.Vitals.Warmth = 20f;
            s.Player.Vitals.Health = 100f;
            TestSupport.Run(s, 240, 0.25);
            Assert.AreEqual(0f, s.Player.Vitals.Warmth, 1e-3, "freezing");
            Assert.Less(s.Player.Vitals.Health, 98f, "freezing costs health");
            Assert.IsFalse(s.Player.Vitals.IsDead);

            Structure fire = TestSupport.Build(s.World, "campfire", Near(s, 1.5f, 0f));
            Assert.IsTrue(fire.Heater.Fuel.TryAdd("wood", 4));
            TestSupport.Run(s, 2, 0.25);
            Assert.IsTrue(fire.Heater.Lit, fire.Heater.Status);
            Assert.Greater(s.Player.FeltTemperatureC, 10f, "the fire warms the survivor");
            float health = s.Player.Vitals.Health;
            TestSupport.Run(s, 240, 0.25);
            Assert.Greater(s.Player.Vitals.Warmth, 30f, "warm again by the fire");
            Assert.GreaterOrEqual(s.Player.Vitals.Health, health, "no more freezing damage");
        }

        [Test]
        public void Campfire_BurnsFuelOnlyWhileLit_AndCooksOnlyWhenBurning()
        {
            GameSession s = Calm(5151);
            s.Weather.ForceFront(WeatherKind.Clear, 30, true);
            Structure fire = TestSupport.Build(s.World, "campfire", Near(s, 2f, 0f));
            s.Player.Inventory.TryAdd("potato", 2);
            Assert.IsFalse(s.CheckRecipe("bake_potato").CanCraft, "an unlit fire cannot cook");
            Assert.IsFalse(fire.Heater.Fuel.TryAdd("stone", 1), "only burnable fuel goes in");
            Assert.IsTrue(fire.Heater.Fuel.TryAdd("wood", 4));
            TestSupport.Run(s, 250, 0.25);
            // A log burns 15 s in a furnace and 8x longer in a campfire: three logs lit by 250 s, one left.
            Assert.AreEqual(1, fire.Heater.Fuel.Count("wood"));
            Assert.AreEqual(110f, fire.Heater.BurnRemaining, 2f);
            Assert.IsTrue(fire.Heater.Lit);
            Assert.IsTrue(s.CheckRecipe("bake_potato").CanCraft, s.CheckRecipe("bake_potato").Reason);

            fire.Enabled = false;
            float left = fire.Heater.BurnRemaining;
            TestSupport.Run(s, 100, 0.25);
            Assert.IsFalse(fire.Heater.Lit);
            Assert.AreEqual(left, fire.Heater.BurnRemaining, 1e-3, "a put-out fire keeps its log");
            Assert.AreEqual(1, fire.Heater.Fuel.Count("wood"));
            Assert.AreEqual(0f, Climate.HeatAt(fire, TestSupport.Content.Building("campfire"), fire.Position));

            fire.Enabled = true;
            TestSupport.Run(s, 1, 0.25);
            Assert.IsTrue(s.Craft("bake_potato").Ok);
            s.Player.Vitals.Warmth = 50f;
            TestSupport.Run(s, 8, 0.25);
            int slot = Enumerable.Range(0, s.Player.Inventory.SlotCount).First(i => s.Player.Inventory.GetSlot(i).ItemId == "baked_potato");
            Assert.IsTrue(s.UseItem(slot).Ok);
            Assert.Greater(s.Player.Vitals.Warmth, 65f, "hot food warms");
        }

        [Test]
        public void Campfire_InTheRain_BurnsFaster()
        {
            GameSession s = Calm(5152);
            s.Weather.ForceFront(WeatherKind.Rain, 30, true);
            Structure fire = TestSupport.Build(s.World, "campfire", Near(s, 2f, 0f));
            Assert.IsTrue(fire.Heater.Fuel.TryAdd("wood", 4));
            TestSupport.Run(s, 100, 0.25);
            // Rain at 0.6 makes the fire burn 1.6x: 160 s of a 120 s log, so the second log is 40 s in.
            Assert.AreEqual(2, fire.Heater.Fuel.Count("wood"));
            Assert.AreEqual(80f, fire.Heater.BurnRemaining, 2f);
            StringAssert.Contains("rain", fire.Heater.Status);
        }

        [Test]
        public void Shelter_UnderARoofOrInsideABuilding()
        {
            GameSession s = Calm(5153);
            Vec3 spot = Near(s, 20f, 0f);
            Assert.IsFalse(Climate.IsSheltered(s.World, TestSupport.Content, spot));
            TestSupport.Build(s.World, "roof", spot + new Vec3(0.5f, 3.2f, -0.5f));
            Assert.IsTrue(Climate.IsSheltered(s.World, TestSupport.Content, spot));
            Assert.IsFalse(Climate.IsSheltered(s.World, TestSupport.Content, spot + new Vec3(3f, 0f, 0f)), "beside the roof is outside");

            PropPlacement house = s.World.Layout.Props.First(p => p.Kind == "house");
            Assert.IsTrue(Climate.IsSheltered(s.World, TestSupport.Content, house.Position + new Vec3(0f, 0.2f, 0f)), "inside a house");
            Vec3 front = house.Position + WorldGenerator.Rotate(new Vec3(0f, 0f, house.Size.Z * 0.5f + 2f), house.Yaw);
            Assert.IsFalse(Climate.IsSheltered(s.World, TestSupport.Content, front), "in front of a house");

            s.Weather.ForceFront(WeatherKind.Storm, 3, true);
            s.Weather.ForceColdSnap(10);
            s.World.Clock.Advance(s.World.Clock.SecondsPerGameHour * 2);
            WeatherReadout r = s.WeatherNow;
            ClimateReading inside = Climate.At(s.World, TestSupport.Content, spot, r);
            ClimateReading outside = Climate.At(s.World, TestSupport.Content, spot + new Vec3(6f, 0f, 0f), r);
            Assert.AreEqual(WeatherTuning.ShelterBonusC + 2.5f * r.Rain + 2f * r.Storm, inside.TemperatureC - outside.TemperatureC, 1e-3);
        }

        [Test]
        public void Lightning_BreaksExposedConductors_ButNeverDestroysThem()
        {
            GameSession s = Calm(5154);
            s.Weather.ForceFront(WeatherKind.Storm, 3, true);
            Structure gen = TestSupport.Build(s.World, "generator", Near(s, 8f, 0f));
            Structure pole = TestSupport.Build(s.World, "power_pole", Near(s, 12f, 2f));
            Structure lamp = TestSupport.Build(s.World, "lamp", Near(s, 15f, 4f));
            gen.Generator.Fuel.TryAdd("fuel_can", 2);
            int count = s.World.Structures.Count;
            var hits = new List<LightningStruck>();
            Structure struck = null;
            for (int i = 0; i < 600 && struck == null; i++)
            {
                LightningStruck e = s.Weather.Strike();
                if (!e.StructureId.IsValid) continue;
                Structure target = s.World.Structures[e.StructureId];
                BuildingDefinition d = TestSupport.Content.Building(target.DefId);
                // The first strike on a structure damages it; the second breaks it.
                if (hits.All(h => h.StructureId != e.StructureId))
                {
                    Assert.AreEqual(d.MaxHealth * (1f - WeatherTuning.StrikeDamageFraction), target.Health, 1e-3, "a first strike takes half the health");
                    Assert.IsFalse(d.IsBroken(target.Health), "a damaged structure still works");
                }
                else struck = target;
                hits.Add(e);
            }
            Assert.IsNotNull(struck, "exposed conductors draw strikes until one is hit twice");
            Assert.AreEqual(count, s.World.Structures.Count, "lightning never destroys");
            Assert.IsTrue(hits.All(h => !h.AbsorbedByRod));
            BuildingDefinition def = TestSupport.Content.Building(struck.DefId);
            Assert.IsTrue(def.IsBroken(struck.Health), "a second strike breaks it");
            Assert.GreaterOrEqual(struck.Health, 1f);
            TestSupport.Run(s, 1, 0.25);
            StringAssert.Contains("Broken", struck.Power.Reason);
            Assert.IsNull(s.Power.NetworkOf(struck.Id), "a broken component is off the grid");

            var materials = TestSupport.NewInventory(s.World, 20);
            foreach (ItemAmount a in s.Construction.RepairCost(struck)) materials.TryAdd(a.ItemId, a.Quantity);
            Assert.IsTrue(s.Construction.Repair(struck, materials).Ok);
            TestSupport.Run(s, 1, 0.25);
            Assert.IsNotNull(s.Power.NetworkOf(struck.Id), "repaired, it rejoins the grid");
            StringAssert.DoesNotContain("Broken", struck.Power.Reason);
        }

        [Test]
        public void LightningRod_TakesTheStrikes_ForEverythingInRange()
        {
            GameSession s = Calm(5155);
            s.Weather.ForceFront(WeatherKind.Storm, 3, true);
            Structure gen = TestSupport.Build(s.World, "generator", Near(s, 8f, 0f));
            Structure pole = TestSupport.Build(s.World, "power_pole", Near(s, 12f, 2f));
            Structure lamp = TestSupport.Build(s.World, "lamp", Near(s, 15f, 4f));
            TestSupport.Build(s.World, "lightning_rod", Near(s, 11f, 1f));
            int hits = 0;
            for (int i = 0; i < 300; i++)
            {
                LightningStruck e = s.Weather.Strike();
                if (!e.StructureId.IsValid) continue;
                hits++;
                Assert.IsTrue(e.AbsorbedByRod, "the rod takes every strike in its range");
                Assert.AreEqual(0f, e.Damage);
            }
            Assert.Greater(hits, 20);
            foreach (Structure st in new[] { gen, pole, lamp }) Assert.AreEqual(TestSupport.Content.Building(st.DefId).MaxHealth, st.Health);
        }

        [Test]
        public void Storm_EmitsThunderThatTheInfectedHear_AndRainHidesTheSurvivor()
        {
            GameSession s = Calm(5156);
            s.Weather.ForceFront(WeatherKind.Storm, 3, true);
            LightningStruck e = s.Weather.Strike();
            Assert.IsTrue(s.Zombies.Noise.TryHear(e.Position + new Vec3(10f, 0f, 0f), 1f, out NoiseEvent heard));
            Assert.AreEqual("thunder", heard.Source);

            ZombieArchetype a = TestSupport.Content.Archetype("wanderer");
            var z = new ZombieState { Position = new Vec3(0, 0, 0), Yaw = 0f };
            var target = new Vec3(0, 0, a.SightRange * 0.85f);
            Assert.IsTrue(PerceptionRules.CanSee(z, a, target, 1f, false, false, true, 1f), "seen on a clear day");
            Assert.IsFalse(PerceptionRules.CanSee(z, a, target, 1f, false, false, true, s.WeatherNow.SightFactor), "hidden by the storm");
        }

        [Test]
        public void Rain_WatersPlanterBedsInTheOpen_NotUnderARoof()
        {
            GameSession s = Calm(5157);
            s.World.Tech.Unlocked.Add("agriculture");
            Structure open = TestSupport.Build(s.World, "planter_bed", Near(s, 6f, 0f));
            Structure covered = TestSupport.Build(s.World, "planter_bed", Near(s, -6f, 0f));
            TestSupport.Build(s.World, "roof", covered.Position + new Vec3(0f, 3.2f, 0f));
            s.Weather.ForceFront(WeatherKind.Rain, 5, true);
            TestSupport.Run(s, 60, 0.25);
            Assert.Greater(open.Crop.Water, 35f);
            Assert.AreEqual(0f, covered.Crop.Water, 1e-3);
        }

        [Test]
        public void Colonist_InAColdSnap_WarmsUpAtTheCampfire_AndKeepsItFed()
        {
            GameSession s = Calm(5158);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.1);
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            Structure crate = TestSupport.Build(s.World, "storage_crate", new Vec3(p.X + 3, p.Y, p.Z));
            ColonistState c = s.World.Colonists.Values.First();
            c.Position = new Vec3(p.X + 1, p.Y, p.Z + 1);
            Assert.IsTrue(s.Recruit(c.Id).Ok);
            Structure fire = TestSupport.Build(s.World, "campfire", new Vec3(p.X + 10, p.Y, p.Z + 6));
            crate.Storage.TryAdd("wood", 10);

            s.Weather.ForceFront(WeatherKind.Clear, 30, true);
            TestSupport.Run(s, 30, 0.25);
            Assert.Greater(fire.Heater.Fuel.Count("wood") + (fire.Heater.Lit ? 1 : 0), 0, "a colonist brings logs to the fire");
            TestSupport.Run(s, 10, 0.25);
            Assert.IsTrue(fire.Heater.Lit, fire.Heater.Status);

            s.Weather.ForceColdSnap(20);
            TestSupport.Run(s, 190, 0.25);
            // Away from the fire (where the haul left them), cold, with nothing else to do.
            c.Position = new Vec3(p.X - 2, p.Y, p.Z - 2);
            c.Vitals.Warmth = 30f;
            bool warming = false;
            for (int i = 0; i < 2400 && c.Vitals.Warmth < ColonySystem.WarmEnough; i++)
            {
                s.Update(0.25);
                if (c.Status.StartsWith("Warming up at the Campfire")) warming = true;
            }
            Assert.IsTrue(warming, $"the colonist went to the fire ({c.Status})");
            Assert.GreaterOrEqual(c.Vitals.Warmth, ColonySystem.WarmEnough);
            Assert.Less(Vec3.DistanceXZ(c.Position, fire.Position), 3f);
            Assert.AreNotEqual(ColonistActivity.Down, c.Activity);
        }

        [Test]
        public void WeatherAndFires_SurviveSaveAndLoad_AndContinueIdentically()
        {
            GameSession s = Calm(5159);
            Structure fire = TestSupport.Build(s.World, "campfire", Near(s, 2f, 0f));
            fire.Heater.Fuel.TryAdd("charcoal", 3);
            TestSupport.Run(s, 20, 0.25);
            s.Weather.ForceFront(WeatherKind.Rain, 2, false);
            s.Weather.ForceColdSnap(6);
            TestSupport.Run(s, 30, 0.25);

            string text = SaveSerializer.Serialize(s.World, "weather", 0);
            var warnings = new List<string>();
            WorldState loaded = SaveSerializer.Deserialize(text, TestSupport.Content, warnings);
            Assert.IsEmpty(warnings, string.Join("\n", warnings));
            var l = new GameSession(TestSupport.Content, loaded);
            l.Threats.SpawningEnabled = false;

            Structure lf = loaded.Structures[fire.Id];
            Assert.AreEqual(fire.Heater.Fuel.Id, lf.Heater.Fuel.Id);
            Assert.AreSame(lf.Heater.Fuel, loaded.FindInventory(fire.Heater.Fuel.Id));
            Assert.AreEqual(fire.Heater.Fuel.Count("charcoal"), lf.Heater.Fuel.Count("charcoal"));
            Assert.AreEqual(fire.Heater.BurnRemaining, lf.Heater.BurnRemaining, 1e-4);
            Assert.AreEqual(s.World.Weather.Kind, loaded.Weather.Kind);
            Assert.AreEqual(s.World.Weather.ColdSnapEndAt, loaded.Weather.ColdSnapEndAt, 1e-9);
            Assert.AreEqual(s.World.WeatherRng.State, loaded.WeatherRng.State);
            Assert.AreEqual(s.WeatherNow.OutdoorC, l.WeatherNow.OutdoorC, 1e-4);

            var a = new List<WeatherKind>();
            var b = new List<WeatherKind>();
            s.Events.Subscribe<WeatherChanged>(e => a.Add(e.To));
            l.Events.Subscribe<WeatherChanged>(e => b.Add(e.To));
            TestSupport.Run(s, 1200, 0.25);
            TestSupport.Run(l, 1200, 0.25);
            CollectionAssert.AreEqual(a, b, "the loaded game has the same weather ahead");
            Assert.AreEqual(s.World.Weather.EndsAt, loaded.Weather.EndsAt, 1e-6);
            Assert.AreEqual(fire.Heater.BurnRemaining, lf.Heater.BurnRemaining, 1e-3);
            Assert.AreEqual(s.Player.Vitals.Warmth, l.Player.Vitals.Warmth, 1e-3);
        }
    }
}
