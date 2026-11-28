using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>World version 3: the 64 km Kessel Basin, generated in chunks around the survivor.</summary>
    public sealed class BasinTests
    {
        private const int Seed = 20261107;

        private static GameSession NewBasinGame()
        {
            var settings = GameSettings.FromPreset(DifficultyPreset.Survival);
            settings.WorldVersion = 3;
            return NewGameFactory.Create(TestSupport.Content, Seed, settings);
        }

        private static void TravelTo(GameSession s, float x, float z, int seconds = 3)
        {
            s.World.Player.Position = s.World.Layout.Ground(x, z);
            s.Streaming.Warm(s.World.Player.Position);
            for (int i = 0; i < seconds * 4; i++) s.Update(0.25);
        }

        [Test]
        public void Basin_IsSixtyFourKilometres_AndBuiltOnlyWhereTheSurvivorGoes()
        {
            var sw = Stopwatch.StartNew();
            WorldLayout l = WorldGenerator.Generate(Seed, 3);
            Assert.Less(sw.ElapsedMilliseconds, 20000, "planning the basin stays a loading-screen job");
            Assert.AreEqual(65536f, l.Size, 0.1f);
            Assert.IsTrue(l.IsChunked);
            Assert.AreEqual(0, l.GeneratedChunkCount, "nothing is generated until asked");
            foreach (string id in new[] { "kesselmouth", "harrowgate", "cragholm", "redrock", "kingsmoor", "dunmere", "gullhaven", "saltcombe", "fenwick", "greyspine", "highmoor", "scar", "millbrook", "ashford" })
                Assert.IsNotNull(l.Poi(id), id);
            PoiInfo city = l.Poi("kesselmouth");
            Assert.Greater(Math.PI * city.Radius * city.Radius / 1e6, 1300.0, "Kesselmouth covers about the area of Greater London (~1,570 km²)");

            // The same seed is the same world, chunk by chunk, whatever order they are made in.
            WorldLayout m = WorldGenerator.Generate(Seed, 3);
            ChunkContent a1 = l.EnsureChunk(78, 59), a2 = l.EnsureChunk(30, 70);
            ChunkContent b2 = m.EnsureChunk(30, 70), b1 = m.EnsureChunk(78, 59);
            Assert.AreEqual(a1.Blocks.Count, b1.Blocks.Count);
            Assert.AreEqual(a1.Nodes.Count, b1.Nodes.Count);
            Assert.AreEqual(a2.Nodes.Count, b2.Nodes.Count);
            for (int i = 0; i < a1.Blocks.Count; i += 7) Assert.AreEqual(a1.Blocks[i].Position.X, b1.Blocks[i].Position.X, 1e-3f);
            for (int i = 0; i < 50; i++)
            {
                float x = 39000f + i * 37.3f, z = 29000f + i * 21.7f;
                Assert.AreEqual(l.HeightAt(x, z), m.HeightAt(x, z), 1e-3f);
            }
        }

        [Test]
        public void Basin_HasItsBiomes()
        {
            WorldLayout l = WorldGenerator.Generate(Seed, 3);
            var b = (BasinWorld)l.Chunks;
            Assert.Greater(l.HeightAt(6000f, 30000f), 480f, "the Greyspine is high");
            Assert.Greater(b.Biome(6000f, 30000f).Snow, 0.5f, "with snow");
            Assert.Greater(b.Biome(30000f, 55000f).Moor, 0.7f);
            Assert.Greater(b.Biome(11000f, 6500f).Badlands, 0.7f);
            Assert.Greater(b.Biome(40000f, 30000f).City, 0.9f);
            Assert.Greater(b.Biome(30000f, 5000f).Farm, 0.5f);
            Assert.Greater(b.Biome(19000f, 44000f).Forest, 0.5f);
            Assert.IsTrue(l.CoarseWaterAt(63000f, 20000f, out float sea) && Math.Abs(sea - BasinWorld.Sea) < 0.01f, "the sea in the east");
            Assert.IsTrue(l.WaterAt(51200f, 55700f, out _), "a lake in the lake district");
            // The Aldwater flows downhill to the sea through the city.
            foreach (RiverPath r in l.Rivers.Where(r => r.Name.StartsWith("Aldwater", StringComparison.Ordinal)))
                for (int i = 1; i < r.Points.Count; i++) Assert.LessOrEqual(r.Points[i].Y, r.Points[i - 1].Y + 1e-3f);
            Vec3 river = l.Rivers.First(r => r.Name == "Aldwater" && r.Width > 80f).Points[400];
            Assert.IsTrue(l.WaterAt(river.X, river.Z, out _), $"the river holds water in the city at {river}");
        }

        [Test]
        public void City_HasStreetsBuildingsLootAndParks_WithoutOverlaps()
        {
            WorldLayout l = WorldGenerator.Generate(Seed, 3);
            int blocks = 0, enterable = 0, containers = 0, streets = 0, parks = 0;
            var kinds = new HashSet<string>();
            for (int z = 56; z <= 62; z += 2)
                for (int x = 60; x <= 104; x += 4)
                {
                    ChunkContent c = l.EnsureChunk(x, z);
                    blocks += c.Blocks.Count;
                    enterable += c.Props.Count(p => p.PoiId == "kesselmouth");
                    containers += c.Containers.Count;
                    streets += c.Streets.Count;
                    parks += c.Props.Count(p => p.Kind == "city_park");
                    foreach (PropPlacement p in c.Blocks) kinds.Add(p.Kind);
                    // Buildings in a chunk never overlap each other or stand in water.
                    var all = c.Blocks.Concat(c.Props.Where(p => p.PoiId == "kesselmouth")).ToList();
                    for (int i = 0; i < all.Count; i++)
                    {
                        Assert.IsFalse(l.WaterAt(all[i].Position.X, all[i].Position.Z, out _), $"{all[i].Kind} in water at {all[i].Position}");
                        for (int j = i + 1; j < all.Count; j++)
                            Assert.Greater(Vec3.DistanceXZ(all[i].Position, all[j].Position), Math.Min(Math.Min(all[i].Size.X, all[i].Size.Z), Math.Min(all[j].Size.X, all[j].Size.Z)) * 0.45f,
                                $"{all[i].Kind} and {all[j].Kind} overlap at {all[i].Position}");
                    }
                }
            Assert.Greater(blocks, 5000, "a dense city along the Aldwater");
            Assert.Greater(enterable, 100, "buildings to enter and search");
            Assert.Greater(containers, 200);
            Assert.Greater(streets, 1000);
            Assert.Greater(parks, 5);
            foreach (string k in new[] { "city_tower", "city_midrise", "city_terrace", "city_semi" }) Assert.IsTrue(kinds.Contains(k), k);
            Assert.Greater(l.Props.Count(p => p.Kind == "bridge"), 0, "the Aldwater is bridged");
        }

        [Test]
        public void NewBasinGame_SeedsAroundTheSurvivor_AndForgetsUntouchedTreesFarAway()
        {
            GameSession s = NewBasinGame();
            WorldState w = s.World;
            Assert.Greater(w.SeededChunks.Count, 0);
            Assert.Greater(w.ResourceNodes.Count, 500, "trees and rocks around the camp");
            Assert.Greater(w.Structures.Values.Count(x => !x.PlayerBuilt), 20, "containers and fixtures of Millbrook");
            Assert.Greater(w.Vehicles.Count, 0);
            int nearStart = w.ResourceNodes.Count;

            // Fell a tree at the camp, then go to the city.
            ResourceNodeState tree = w.ResourceNodes.Values.Where(n => n.DefId == "tree_pine").OrderBy(n => Vec3.DistanceXZ(n.Position, w.Player.Position)).First();
            tree.RemainingHarvests -= 1;
            tree.Touched = true;
            string treeKey = tree.SeedKey;
            Assert.IsNotEmpty(treeKey);

            TravelTo(s, 40500f, 31500f);
            Assert.IsTrue(w.SeededChunks.Contains(ChunkContent.ChunkKey(WorldLayout.ChunkOf(40500f), WorldLayout.ChunkOf(31500f))));
            Assert.IsTrue(w.ResourceNodes.Values.Any(n => n.SeedKey == treeKey), "the felled tree is kept");
            Assert.Less(w.ResourceNodes.Values.Count(n => n.Position.X < 2048f && n.Position.Z < 2048f), nearStart / 4, "untouched trees far away are let go");
            Assert.Greater(w.Structures.Values.Count(x => x.PoiId == "kesselmouth"), 20, "city loot near the survivor");

            // Save in the city, load, and the world is the same.
            string text = SaveSerializer.Serialize(w, "t", 1);
            WorldState loaded = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            var s2 = new GameSession(TestSupport.Content, loaded);
            Assert.AreEqual(w.SeededChunks.Count, loaded.SeededChunks.Count);
            Assert.AreEqual(w.Structures.Count, loaded.Structures.Count);
            Assert.AreEqual(w.Vehicles.Count, loaded.Vehicles.Count);
            Assert.IsTrue(loaded.ResourceNodes.Values.Any(n => n.SeedKey == treeKey && n.RemainingHarvests == tree.RemainingHarvests));
            Assert.AreEqual(w.ResourceNodes.Values.Count(n => n.Position.X > 30000f), loaded.ResourceNodes.Values.Count(n => n.Position.X > 30000f), "the trees around the survivor are back");
            Assert.Less(text.Length, 6_000_000, "untouched trees are not saved");

            // Coming back seeds nothing twice.
            int structures = loaded.Structures.Count;
            TravelTo(s2, 300f, 300f);
            Assert.AreEqual(structures, loaded.Structures.Count, "Millbrook was seeded already");
            Assert.AreEqual(1, loaded.ResourceNodes.Values.Count(n => n.SeedKey == treeKey), "the felled tree is not made again");
        }

        [Test]
        public void Basin_CityHasStationsFlatsAndSupermarkets_WithOpenableDoors()
        {
            WorldLayout l = WorldGenerator.Generate(Seed, 3);
            var b = (BasinWorld)l.Chunks;
            var stations = b.Stations.ToList();
            Assert.GreaterOrEqual(stations.Count, 6, "stations along the three lines: " + string.Join(", ", stations.Select(s => s.Name)));
            Assert.IsTrue(stations.Any(s => s.Name == "Kesselmouth Central"), "a central station where the lines meet");
            Assert.IsTrue(stations.Any(s => s.Name == "Harrowgate Station"), "and one at the end of the western line");

            // Every station is built, with its platform, in the chunk that holds it.
            var plans = new List<PlacedPlan>();
            foreach ((Vec3 centre, string name) in stations)
            {
                ChunkContent c = l.EnsureChunk(WorldLayout.ChunkOf(centre.X), WorldLayout.ChunkOf(centre.Z));
                PlacedPlan st = c.Plans.FirstOrDefault(p => p.Plan.Id.StartsWith(BuildingPlans.StationKind + "|", StringComparison.Ordinal));
                Assert.IsNotNull(st, name);
                Assert.IsTrue(c.Props.Any(p => p.Kind == "platform"), name + " has a platform");
                foreach (PropPlacement blk in c.Blocks)
                    Assert.Greater(Vec3.DistanceXZ(blk.Position, centre), Math.Max(blk.Size.X, blk.Size.Z) * 0.5f, $"no city building stands in {name}");
                plans.Add(st);
            }

            // A stretch of the city has blocks of flats and a supermarket.
            for (int z = 56; z <= 62; z++)
                for (int x = 70; x <= 104; x++)
                    plans.AddRange(l.EnsureChunk(x, z).Plans.Where(p => p.Plan.Id.Contains("|")));
            plans = plans.GroupBy(p => p.Plan.Id).Select(g => g.First()).ToList(); // the stretch holds some of the stations again
            var flats = plans.Where(p => p.Plan.Id.StartsWith(BuildingPlans.FlatsKind + "|", StringComparison.Ordinal)).ToList();
            Assert.Greater(flats.Count, 10, "blocks of flats");
            Assert.IsTrue(flats.Any(f => f.Plan.Floors >= 5), "some of them five storeys or more");
            Assert.IsTrue(plans.Any(p => p.Plan.Id.StartsWith(BuildingPlans.SupermarketKind + "|", StringComparison.Ordinal)), "a supermarket");

            var keys = new HashSet<string>();
            foreach (PlacedPlan pp in plans)
            {
                BuildingPlan plan = pp.Plan;
                Assert.AreEqual(Math.Max(0, plan.Floors - 1), plan.Stairs.Count, plan.Name + ": a flight up from every floor but the top");
                Assert.AreEqual(plan.Id, BuildingPlans.Get(plan.Id).Id, "the view rebuilds the same plan from its id");
                foreach (PlanFixture f in plan.Fixtures) Assert.IsTrue(keys.Add(f.Key), "fixture keys are unique across buildings: " + f.Key);
                // Every locked door's key is inside the building, in a container that is not behind that door.
                foreach (PlanFixture door in plan.Fixtures.Where(f => f.Locked))
                    Assert.IsTrue(plan.Fixtures.Any(f => f.KeysInside.Contains(door.LockId) && !f.Key.Contains(door.Key.Substring(door.Key.LastIndexOf(':') + 1).Replace("door:", ""))), $"{plan.Name}: the key to {door.LockId} is in the building");
            }
        }

        [Test]
        public void Basin_UntouchedFittingsFarAway_AreDroppedFromSaves_AndComeBackTheSame()
        {
            GameSession s = NewBasinGame();
            WorldState w = s.World;
            TravelTo(s, 39980f, 30250f);
            var city = w.Structures.Values.Where(st => st.SeedKey.StartsWith("c", StringComparison.Ordinal) && st.Storage != null && !st.Storage.IsEmpty).ToList();
            Assert.Greater(city.Count, 20, "the city's containers are seeded around the survivor");
            // Loot one container: its chunk is the player's now and keeps everything.
            Structure looted = city[0];
            ItemStack first = Enumerable.Range(0, looted.Storage.SlotCount).Select(looted.Storage.GetSlot).First(st => !st.IsEmpty);
            Assert.IsTrue(looted.Storage.TryRemove(first.ItemId, 1));
            Assert.IsTrue(looted.Touched, "taking something marks a seeded container touched");
            // A seed key names the chunk that generated it ("c{x}_{z}:…"), which can be a neighbour of where it stands.
            long ChunkOfKey(string k)
            {
                int us = k.IndexOf('_'), colon = k.IndexOf(':');
                return ChunkContent.ChunkKey(int.Parse(k.Substring(1, us - 1)), int.Parse(k.Substring(us + 1, colon - us - 1)));
            }
            long lootedChunk = ChunkOfKey(looted.SeedKey);
            // Remember an untouched container elsewhere and its loot.
            Structure kept = city.First(st => !st.Touched && ChunkOfKey(st.SeedKey) != lootedChunk);
            string keptKey = kept.SeedKey;
            string Loot(Structure st) => string.Join(",", Enumerable.Range(0, st.Storage.SlotCount).Select(st.Storage.GetSlot).Where(x => !x.IsEmpty).Select(x => $"{x.ItemId}x{x.Quantity}{x.Tag}").OrderBy(x => x, StringComparer.Ordinal));
            string keptLoot = Loot(kept);
            int before = w.Structures.Count;
            int sizeBefore = SaveSerializer.Serialize(w, "t", 1).Length;

            TravelTo(s, 20000f, 30250f, 12); // 20 km away: past the keep radius, and a prune pass or two
            Assert.Greater(w.DormantChunks.Count, 0, "far chunks' untouched fittings are dropped");
            Assert.IsFalse(w.Structures.Values.Any(st => st.SeedKey == keptKey), "the untouched container is gone from the world");
            Assert.IsTrue(w.Structures.ContainsKey(looted.Id), "the looted container's chunk keeps its fittings");
            Assert.IsFalse(w.DormantChunks.Contains(lootedChunk));
            Assert.Less(w.Structures.Count, before);

            // The save remembers which chunks are dormant, and is smaller for it.
            string text = SaveSerializer.Serialize(w, "t", 1);
            WorldState loaded = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            CollectionAssert.AreEquivalent(w.DormantChunks, loaded.DormantChunks);
            Assert.IsTrue(loaded.Structures[looted.Id].Touched, "touched is saved");
            TestContext.WriteLine($"Save {sizeBefore / 1024} KB in the city, {text.Length / 1024} KB after leaving it ({w.DormantChunks.Count} dormant chunks)");

            // Back in the city everything is there again, the untouched container with the same loot.
            TravelTo(s, 39980f, 30250f);
            Structure again = w.Structures.Values.FirstOrDefault(st => st.SeedKey == keptKey);
            Assert.IsNotNull(again, "the dormant chunk woke when the survivor came back");
            Assert.AreEqual(keptLoot, Loot(again), "remade exactly as it was generated");
            Assert.IsFalse(again.Touched);
            Assert.IsFalse(w.DormantChunks.Contains(ChunkOfKey(again.SeedKey)));
        }
    }
}
