using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>World version 2: the 2 km Greater Millbrook region.</summary>
    public sealed class RegionTests
    {
        private const int Seed = 20261107;
        private static WorldLayout _region, _valley;
        private static long _generationMs;

        private static WorldLayout Region
        {
            get
            {
                if (_region != null) return _region;
                var sw = Stopwatch.StartNew();
                _region = WorldGenerator.Generate(Seed, 2);
                _generationMs = sw.ElapsedMilliseconds;
                return _region;
            }
        }

        private static WorldLayout Valley => _valley ?? (_valley = WorldGenerator.Generate(Seed, 1));

        private static bool IsBuilding(PropPlacement p) => MapFeatures.BuildingKinds.Contains(p.Kind) && p.Kind != "canopy" && p.Kind != "silo" && p.Kind != "transformer";

        [Test]
        public void Region_IsTwoKilometresAcross_Deterministic_AndQuickToGenerate()
        {
            WorldLayout a = Region, b = WorldGenerator.Generate(Seed, 2);
            Assert.AreEqual(2048f, a.Size, 0.01f);
            Assert.AreEqual(2, a.Version);
            Assert.AreEqual("Greater Millbrook", a.RegionName);
            Assert.AreEqual(a.Props.Count, b.Props.Count);
            Assert.AreEqual(a.Nodes.Count, b.Nodes.Count);
            Assert.AreEqual(a.Containers.Count, b.Containers.Count);
            for (int i = 0; i < 200; i++)
            {
                float x = i * 10.13f, z = 2047f - i * 9.7f;
                Assert.AreEqual(a.HeightAt(x, z), b.HeightAt(x, z), 1e-4f);
            }
            for (int i = 0; i < a.Nodes.Count; i += 97) Assert.AreEqual(a.Nodes[i].Position.X, b.Nodes[i].Position.X, 1e-4f);
            // Under a second in a release build; the headless tests run a debug build.
            Assert.Less(_generationMs, 15000, "region generation must stay fast enough for a loading screen");
            // The world version is not the default: old saves and the valley stay version 1.
            Assert.AreEqual(512f, Valley.Size, 0.01f);
            Assert.AreEqual(1, new GameSettings().WorldVersion);
        }

        [Test]
        public void Region_KeepsTheValley_AndAddsTownsVillagesAndRemotePlaces()
        {
            // The valley's landmarks stand where they stood.
            foreach (PoiInfo p in Valley.Pois)
            {
                PoiInfo q = Region.Poi(p.Id);
                Assert.IsNotNull(q, p.Id);
                Assert.AreEqual(p.Center.X, q.Center.X, 0.01f, p.Id);
                Assert.AreEqual(p.Center.Z, q.Center.Z, 0.01f, p.Id);
            }
            foreach (PropPlacement p in Valley.Props.Where(IsBuilding))
                Assert.IsTrue(Region.Props.Any(q => q.Kind == p.Kind && Math.Abs(q.Position.X - p.Position.X) < 0.01f && Math.Abs(q.Position.Z - p.Position.Z) < 0.01f), $"{p.Kind} at {p.Position}");
            Assert.AreEqual(Valley.PlayerSpawn.X, Region.PlayerSpawn.X, 0.01f);

            int Buildings(string poi) => Region.Props.Count(p => p.PoiId == poi && IsBuilding(p));
            Assert.GreaterOrEqual(Buildings("ashford"), 60, "Ashford is a town");
            Assert.GreaterOrEqual(Buildings("eastgate"), 60, "Eastgate is an industrial district");
            Assert.GreaterOrEqual(Buildings("pinecrest"), 8, "Pinecrest is a village");
            foreach (string id in new[] { "millerfarm", "coldwater", "research", "lodge", "reservoir" }) Assert.IsNotNull(Region.Poi(id), id);
            Assert.GreaterOrEqual(Region.Props.Count(p => p.PoiId == "research" && IsBuilding(p)), 3);
            Assert.AreEqual(3, Region.Plans.Count, "apartment blocks in Millbrook, Ashford and Eastgate");
            Assert.AreEqual(3, Region.Plans.Select(p => p.Plan.Id).Distinct().Count());
            // Each block's fixtures have their own keys, so seeding never confuses them.
            Assert.AreEqual(Region.Fixtures.Count, Region.Fixtures.Select(f => f.Key).Distinct().Count());
            Assert.GreaterOrEqual(Region.Containers.Count, 400, "the towns are worth searching");
            Assert.AreEqual(6, Region.Vehicles.Count);
            Assert.AreEqual(Region.Vehicles.Count, Region.Vehicles.Select(v => v.Key).Distinct().Count());
        }

        [Test]
        public void Region_BuildingsStandOnDryLand_OffTheRoads_AndApart()
        {
            var buildings = Region.Props.Where(p => IsBuilding(p) && p.Kind != "apartment").ToList();
            foreach (PropPlacement p in buildings)
            {
                Assert.IsFalse(Region.IsWater(p.Position.X, p.Position.Z), $"{p.Kind} at {p.Position} is in water");
                Assert.IsTrue(Region.IsInsideMap(p.Position.X, p.Position.Z, 10f), $"{p.Kind} at {p.Position}");
            }
            var town = buildings.Where(p => p.PoiId == "ashford" || p.PoiId == "eastgate").ToList();
            foreach (PropPlacement p in town)
            {
                float r = Math.Max(p.Size.X, p.Size.Z) * 0.5f;
                foreach (RoadPath road in Region.Roads)
                    for (int i = 0; i + 1 < road.Points.Count; i++)
                        Assert.Greater(WorldLayout.DistanceToSegmentXZ(p.Position.X, p.Position.Z, road.Points[i], road.Points[i + 1], out _), road.Width * 0.5f + r * 0.5f, $"{p.Kind} at {p.Position} sits on a road");
                foreach (PlacedPlan plan in Region.Plans)
                    Assert.Greater(Vec3.DistanceXZ(p.Position, plan.Origin), r + 8f, $"{p.Kind} at {p.Position} overlaps {plan.Plan.Name}");
            }
            for (int i = 0; i < town.Count; i++)
                for (int j = i + 1; j < town.Count; j++)
                    Assert.Greater(Vec3.DistanceXZ(town[i].Position, town[j].Position), 4f, $"{town[i].Kind} and {town[j].Kind} overlap at {town[i].Position}");
            // No tree grows inside a building.
            foreach (NodeSeed n in Region.Nodes.Where(n => n.DefId.StartsWith("tree_", StringComparison.Ordinal)).Take(20000))
                foreach (PropPlacement p in town)
                    Assert.Greater(Vec3.DistanceXZ(n.Position, p.Position), Math.Min(p.Size.X, p.Size.Z) * 0.5f, $"tree at {n.Position} in a {p.Kind}");
        }

        [Test]
        public void River_FlowsDownhill_HoldsWater_AndRoadsCrossItOnBridges()
        {
            RiverPath kessel = Region.Rivers.Single();
            for (int i = 1; i < kessel.Points.Count; i++) Assert.LessOrEqual(kessel.Points[i].Y, kessel.Points[i - 1].Y + 1e-3f, $"point {i}");
            int wet = 0;
            for (int i = 0; i + 1 < kessel.Points.Count; i++)
            {
                Vec3 m = Vec3.Lerp(kessel.Points[i], kessel.Points[i + 1], 0.5f);
                if (Region.WaterAt(m.X, m.Z, out float level))
                {
                    wet++;
                    Assert.Greater(level - Region.HeightAt(m.X, m.Z), 0.5f, $"the river is too shallow at {m}");
                }
            }
            Assert.Greater(wet, kessel.Points.Count * 0.9f, "the channel holds water along its course");

            LakeInfo reservoir = Region.Lakes.Single();
            Assert.IsTrue(Region.WaterAt(reservoir.Center.X, reservoir.Center.Z, out float lakeLevel));
            Assert.AreEqual(reservoir.Level, lakeLevel, 0.01f);
            Assert.Greater(lakeLevel - Region.HeightAt(reservoir.Center.X, reservoir.Center.Z), 3f, "the reservoir is deep");

            var bridges = Region.Props.Where(p => p.Kind == "bridge").ToList();
            Assert.GreaterOrEqual(bridges.Count, 2, "the highway and the research road cross the Kessel");
            foreach (PropPlacement b in bridges)
            {
                Assert.Greater(b.Position.Y, Region.HeightAt(b.Position.X, b.Position.Z) + 1f, "the deck spans the channel");
                Assert.Less(Math.Abs(b.Pitch), 12f, "a bridge is no ramp");
            }
            // Away from bridges no road runs through water.
            foreach (RoadPath road in Region.Roads)
                for (int i = 0; i + 1 < road.Points.Count; i++)
                    for (float t = 0f; t <= 1f; t += 0.05f)
                    {
                        Vec3 p = Vec3.Lerp(road.Points[i], road.Points[i + 1], t);
                        if (!Region.IsWater(p.X, p.Z)) continue;
                        Assert.IsTrue(bridges.Any(b => Vec3.DistanceXZ(b.Position, p) < b.Size.Z * 0.5f), $"road through water at {p}");
                    }
        }

        [Test]
        public void Roads_AreDrivable_AndLeadEverywhere()
        {
            // Grades along every road stay within what the vehicles climb (away from bridge ramps).
            var bridges = Region.Props.Where(p => p.Kind == "bridge").ToList();
            float worst = 0f;
            Vec3 where = default;
            foreach (RoadPath road in Region.Roads)
                for (int i = 0; i + 1 < road.Points.Count; i++)
                {
                    Vec3 a = road.Points[i], b = road.Points[i + 1];
                    float len = Vec3.DistanceXZ(a, b);
                    for (float d = 0f; d + 4f <= len; d += 4f)
                    {
                        Vec3 p = Vec3.Lerp(a, b, d / len), q = Vec3.Lerp(a, b, (d + 4f) / len);
                        if (bridges.Any(br => Vec3.DistanceXZ(br.Position, p) < br.Size.Z * 0.5f + 6f)) continue;
                        float g = Math.Abs(Region.HeightAt(q.X, q.Z) - Region.HeightAt(p.X, p.Z)) / 4f;
                        if (g <= worst) continue;
                        worst = g;
                        where = p;
                    }
                }
            // The mountain roads to Pinecrest and Northgate climb at up to about 14 %.
            Assert.Less(worst, 0.35f, $"steepest road grade over 4 m, at {where}");
            // Every inhabited place is on the road network.
            foreach (string id in new[] { "ashford", "eastgate", "pinecrest", "research", "millerfarm", "coldwater", "lodge", "millbrook", "workshop" })
            {
                PoiInfo poi = Region.Poi(id);
                float nearest = Region.Roads.Where(r => !r.Rail).SelectMany(r => r.Points.Zip(r.Points.Skip(1), (a, b) => WorldLayout.DistanceToSegmentXZ(poi.Center.X, poi.Center.Z, a, b, out _))).Min();
                Assert.Less(nearest, poi.Radius + 20f, id);
            }
            Assert.IsTrue(Region.Roads.Any(r => r.Rail), "the freight line");
            Assert.Greater(Region.Props.Count(p => p.Kind == "rail"), 40);
        }

        [Test]
        public void Region_SpawnsAndVehicles_AreOnUsableGround()
        {
            foreach (Vec3 p in Region.ZombieSpawnPoints) Assert.IsFalse(Region.IsWater(p.X, p.Z), $"spawn in water at {p}");
            Assert.Greater(Region.ZombieSpawnPoints.Count(p => p.X > 520f || p.Z > 520f), 60, "the region has its own infected");
            foreach (VehicleSeed v in Region.Vehicles)
            {
                Assert.IsFalse(Region.IsWater(v.Position.X, v.Position.Z), v.Key);
                Assert.Less(Region.Height.SlopeDegrees(v.Position.X, v.Position.Z), 12f, v.Key);
            }
            // The Eastgate sedan's key is in the Eastgate apartments.
            VehicleSeed sedan = Region.Vehicles.Single(v => v.Key == "vehicle:eastgate_sedan");
            Assert.IsTrue(Region.Fixtures.Any(f => f.Key.StartsWith(BuildingPlans.EastgateApartmentsId, StringComparison.Ordinal) && f.KeysInside.Contains(sedan.LockId)));
            Assert.Greater(Region.Nodes.Count, 6000, "forests across the region");
            Assert.Less(Region.Nodes.Count, 30000, "but not more than the views can stream");
        }

        [Test]
        public void NewRegionGame_SavesAndLoads_AsTheRegion()
        {
            var settings = GameSettings.FromPreset(DifficultyPreset.Survival);
            settings.WorldVersion = 2;
            GameSession s = NewGameFactory.Create(TestSupport.Content, Seed, settings);
            Assert.AreEqual(2048f, s.World.Layout.Size, 0.01f);
            Assert.AreEqual(128, s.World.Exploration.CellsPerSide, "fog of war covers the whole region");
            Assert.AreEqual(6, s.World.Vehicles.Count);
            Assert.Greater(s.World.Zombies.Count, 40);
            s.World.Player.Position = new Vec3(1000f, s.World.Layout.HeightAt(1000f, 500f), 500f);
            s.World.Exploration.Reveal(s.World.Player.Position, 60f);
            for (int i = 0; i < 30; i++) s.Update(1f);

            string text = SaveSerializer.Serialize(s.World, "t", 1);
            WorldState w = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            Assert.AreEqual(2, w.Settings.WorldVersion);
            Assert.AreEqual(2048f, w.Layout.Size, 0.01f);
            Assert.AreEqual(s.World.Layout.Props.Count, w.Layout.Props.Count);
            Assert.IsTrue(w.Exploration.IsRevealed(1000f, 500f));
            Assert.AreEqual(s.World.Structures.Count, w.Structures.Count);
            Assert.AreEqual(s.World.Vehicles.Count, w.Vehicles.Count);
            Assert.AreEqual(1000f, w.Player.Position.X, 0.01f);

            // A version 1 save is still the valley.
            GameSession v1 = TestSupport.NewSession(Seed);
            WorldState w1 = SaveSerializer.Deserialize(SaveSerializer.Serialize(v1.World, "t", 1), TestSupport.Content, new List<string>());
            Assert.AreEqual(512f, w1.Layout.Size, 0.01f);
        }
    }
}
