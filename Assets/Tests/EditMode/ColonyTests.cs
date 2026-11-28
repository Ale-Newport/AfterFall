using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class ColonyTests
    {
        private static GameSession Base(out ColonistState c, out Structure crate, int seed = 6060)
        {
            GameSession s = TestSupport.NewSession(seed);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.1); // morning, not night
            s.World.Tech.Unlocked.Add("metalworking");
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            crate = TestSupport.Build(s.World, "storage_crate", new Vec3(p.X + 3, p.Y, p.Z));
            c = s.World.Colonists.Values.First();
            c.Position = new Vec3(p.X + 1, p.Y, p.Z + 1);
            Assert.IsTrue(s.Recruit(c.Id).Ok);
            return s;
        }

        private static int Total(GameSession s, string item)
        {
            int n = 0;
            foreach (Inventory inv in s.World.AllInventories) n += inv.Count(item);
            foreach (Structure st in s.World.Structures.Values)
                if (st.Delivered.TryGetValue(item, out int d)) n += d;
            return n;
        }

        [Test]
        public void Mechanic_RepairsDamagedBuildings_WithMaterialsFromStorage()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            Structure wall = TestSupport.Build(s.World, "wall", new Vec3(p.X + 6, p.Y, p.Z - 6));
            float max = s.Content.Building("wall").MaxHealth;
            wall.Health = max * 0.3f;
            List<ItemAmount> cost = s.Construction.RepairCost(wall);
            foreach (ItemAmount a in cost) crate.Storage.TryAdd(a.ItemId, a.Quantity + 2);
            TestSupport.Run(s, 20, 0.1);
            Assert.Less(wall.Health, max, "without a mechanic nobody repairs");
            Assert.IsTrue(s.SetWorkPriority(c.Id, JobType.Mechanic, 1).Ok);
            TestSupport.Run(s, 20, 0.1);
            Assert.Less(wall.Health, max, "no hammer in storage, no repairs");
            crate.Storage.TryAdd("hammer", 1);
            TestSupport.Run(s, 60, 0.1);
            Assert.AreEqual(max, wall.Health, 0.01f, "the mechanic fetched the materials and repaired the wall");
            foreach (ItemAmount a in cost) Assert.AreEqual(2, crate.Storage.Count(a.ItemId) + c.Carry.Count(a.ItemId), "the repair used exactly its cost");
        }

        [Test]
        public void Medic_TreatsWoundedColonists_AndTheSurvivorNearby()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            ColonistState other = s.World.Colonists.Values.First(x => x.Id != c.Id);
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z + 3));
            other.Position = new Vec3(p.X + 2, p.Y, p.Z - 2);
            Assert.IsTrue(s.Recruit(other.Id).Ok);
            crate.Storage.TryAdd("bandage", 3);
            other.Vitals.Health = other.Vitals.MaxHealth * 0.4f;
            s.Player.Vitals.Health = s.Player.Vitals.MaxHealth * 0.3f;
            Assert.IsTrue(s.SetWorkPriority(c.Id, JobType.Medic, 1).Ok);
            float otherBefore = other.Vitals.Health, playerBefore = s.Player.Vitals.Health;
            TestSupport.Run(s, 60, 0.1);
            Assert.Greater(other.Vitals.Health, otherBefore + 15f, "the medic bandaged the wounded colonist");
            Assert.Greater(s.Player.Vitals.Health, playerBefore + 15f, "and the survivor, who is near the settlement");
            Assert.AreEqual(1, crate.Storage.Count("bandage") + c.Carry.Count("bandage"), "one bandage each");
        }

        [Test]
        public void Guard_ArmsWithTheBestWeaponInStorage()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            crate.Storage.TryAdd("machete", 1);
            crate.Storage.TryAdd("hammer", 1);
            Assert.IsTrue(s.SetWorkPriority(c.Id, JobType.Guard, 1).Ok);
            TestSupport.Run(s, 20, 0.1);
            Assert.AreEqual(1, c.Carry.Count("machete"), "the guard took the machete (the best weapon) and keeps it");
            Assert.AreEqual(0, crate.Storage.Count("machete"));
            Assert.AreEqual(1, crate.Storage.Count("hammer"));
        }

        [Test]
        public void StorageDesignation_FillsDesignatedCratesFirst_AndPutsAwayWhatBelongsThere()
        {
            GameSession s = Base(out ColonistState c, out Structure general);
            Vec3 p = s.Player.Position;
            Structure pantry = TestSupport.Build(s.World, "storage_crate", new Vec3(p.X - 8, p.Y, p.Z + 6));
            Assert.AreEqual("not_storage", s.SetStorageAccepts(EntityId.None, ItemCategory.Natural, false).Code);
            // Designate the pantry: food and medicine only.
            foreach (ItemCategory cat in (ItemCategory[])System.Enum.GetValues(typeof(ItemCategory)))
                if (cat != ItemCategory.FoodMedicine) Assert.IsTrue(s.SetStorageAccepts(pantry.Id, cat, false).Ok);
            Assert.AreNotEqual(0, pantry.StorageFilter);
            Assert.IsTrue(pantry.Accepts(ItemCategory.FoodMedicine) && !pantry.Accepts(ItemCategory.Natural));
            Assert.AreEqual("empty", s.SetStorageAccepts(pantry.Id, ItemCategory.FoodMedicine, false).Code, "a storage takes at least one kind");

            // Food in the general crate is carried to the pantry; wood stays where it is.
            general.Storage.TryAdd("canned_food", 3);
            general.Storage.TryAdd("wood", 6);
            TestSupport.Run(s, 120, 0.1);
            Assert.AreEqual(3, pantry.Storage.Count("canned_food"), "the colonist put the food away in the pantry");
            Assert.AreEqual(0, pantry.Storage.Count("wood"), "nothing the pantry does not take goes there");
            Assert.AreEqual(6, general.Storage.Count("wood"));

            // Saved and loaded.
            string text = SaveSerializer.Serialize(s.World, "t", 1);
            WorldState loaded = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            Assert.AreEqual(pantry.StorageFilter, loaded.Structures[pantry.Id].StorageFilter);

            // Taking everything again makes it general storage.
            foreach (ItemCategory cat in (ItemCategory[])System.Enum.GetValues(typeof(ItemCategory))) s.SetStorageAccepts(pantry.Id, cat, true);
            Assert.AreEqual(0, pantry.StorageFilter);
        }

        [Test]
        public void Guard_FightsInfectedNearTheSettlement()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            s.Player.Position = new Vec3(p.X - 60, p.Y, p.Z - 60); // the survivor is away
            var rng = DeterministicRandom.Derive(1, "guard");
            ZombieState z = s.Zombies.Spawn(new Vec3(c.Position.X + 6, c.Position.Y, c.Position.Z), "wanderer", rng);
            z.Mode = ZombieMode.Chase;
            Assert.AreEqual(0, c.Work.PriorityFor(JobType.Guard), "guarding is off until set");
            Assert.IsTrue(s.SetWorkPriority(c.Id, JobType.Guard, 1).Ok);
            TestSupport.Run(s, 0.5, 0.05);
            StringAssert.StartsWith("Guarding", c.Status, "a guard goes for an infected near the settlement instead of fleeing");
            for (int i = 0; i < 400 && !z.IsDead && c.Activity != ColonistActivity.Down; i++) TestSupport.Run(s, 0.25, 0.05);
            Assert.IsTrue(z.IsDead, $"the guard put the infected down ({c.Status}, guard health {c.Vitals.Health:0})");
            Assert.AreNotEqual(ColonistActivity.Down, c.Activity);
            Assert.Greater(c.Skill(JobType.Guard), 1f, "fighting trains the guard");
        }

        [Test]
        public void NewGame_HasRecruitableSurvivors()
        {
            GameSession s = TestSupport.NewSession(1);
            Assert.AreEqual(2, s.World.Colonists.Count);
            Assert.IsTrue(s.World.Colonists.Values.All(c => !c.Recruited));
        }

        [Test]
        public void Recruiting_RequiresProximityAndAFreeBed()
        {
            GameSession s = TestSupport.NewSession(2);
            ColonistState c = s.World.Colonists.Values.First();
            Assert.AreEqual("too_far", s.Recruit(c.Id).Code);
            c.Position = s.Player.Position;
            OpResult r = s.Recruit(c.Id);
            Assert.AreEqual("no_bed", r.Code);
            StringAssert.Contains("bedroll", r.Message);
            TestSupport.Build(s.World, "bed", new Vec3(s.Player.Position.X + 2, s.Player.Position.Y, s.Player.Position.Z));
            Assert.IsTrue(s.Recruit(c.Id).Ok);
            Assert.IsTrue(c.Recruited);
            Assert.AreEqual(1, s.RecruitedCount);
        }

        [Test]
        public void Hauler_SuppliesFurnace_AndStoresOutputs_ConservingItems()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(p.X + 12, p.Y, p.Z + 6));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 8);
            crate.Storage.TryAdd("charcoal", 4);
            int ore = Total(s, "iron_ore");
            TestSupport.Run(s, 90, 0.1);
            Assert.AreEqual(ore - Total(s, "iron_ore"), 2 * Total(s, "iron_ingot"), "every ingot consumed exactly two ore");
            Assert.Greater(Total(s, "iron_ingot"), 1, "the colonist kept the furnace supplied");
            Assert.Greater(crate.Storage.Count("iron_ingot"), 0, "outputs were carried back to storage");
            Assert.IsTrue(s.World.Jobs.Values.All(j => j.State != JobState.Claimed || j.AssigneeId == c.Id));
        }

        [Test]
        public void Builder_DeliversMaterialsAndBuildsBlueprint()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            s.ReportPlayerTransform(new Vec3(p.X + 40, p.Y, p.Z), 0, new PlayerActivity()); // the player is elsewhere
            Assert.IsTrue(s.Construction.PlaceBlueprint("workbench", new Vec3(p.X + 8, p.Y, p.Z - 5), 0, new PlacementProbe(), null, out Structure bp).Ok);
            crate.Storage.TryAdd("wood", 6);
            crate.Storage.TryAdd("stone", 4);
            TestSupport.Run(s, 60, 0.1);
            Assert.IsTrue(bp.IsComplete, "the colonist hauled the materials and did the construction work");
            Assert.AreEqual(0, crate.Storage.Count("wood"));
        }

        [Test]
        public void NewRecruit_WithNoWork_WalksToTheSettlement()
        {
            GameSession s = TestSupport.NewSession(6161);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.1);
            Vec3 p = s.Player.Position;
            Structure bed = TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            ColonistState c = s.World.Colonists.Values.First();
            c.Position = s.World.Layout.Ground(p.X + 60, p.Z + 20);
            s.Player.Position = c.Position; // talk to them where they hide
            Assert.IsTrue(s.Recruit(c.Id).Ok);
            s.Player.Position = p;
            TestSupport.Run(s, 3, 0.1);
            Assert.AreEqual("Heading to the settlement", c.Status);
            TestSupport.Run(s, 40, 0.1);
            Assert.LessOrEqual(Vec3.DistanceXZ(c.Position, bed.Position), ColonySystem.HomeRadius, "the recruit walked home");
            Assert.AreEqual(ColonistActivity.Idle, c.Activity);
        }

        [Test]
        public void Colonists_RefuelOnlyWithDedicatedFuel_NotBuildingMaterials()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(p.X + 12, p.Y, p.Z + 6));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 4);
            crate.Storage.TryAdd("planks", 10);
            TestSupport.Run(s, 40, 0.1);
            Assert.AreEqual(10, crate.Storage.Count("planks"), "planks stay in storage");
            Assert.AreEqual(0, furnace.Machine.Fuel.Count("planks"));

            crate.Storage.TryAdd("wood", 4);
            TestSupport.Run(s, 40, 0.1);
            Assert.Greater(furnace.Machine.Fuel.Count("wood") + Total(s, "iron_ingot"), 0, "wood is used as fuel");
            Assert.AreEqual(10, crate.Storage.Count("planks"));
        }

        [Test]
        public void UnreachableTarget_FailsJobWithReason_AndReleasesReservations()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(p.X + 12, p.Y, p.Z + 8));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 6);
            int ore = Total(s, "iron_ore");
            var walls = new BlockedMovement();
            s.Colony.Movement = walls;
            TestSupport.Run(s, 10, 0.1);
            Assert.IsTrue(c.RecentFailures.Any(f => f.Contains("can't find a way there")), "the colonist gives up and says why");
            Assert.AreEqual(ore, Total(s, "iron_ore"), "no ore was lost");
            Assert.IsTrue(s.World.AllInventories.All(inv => inv.Reservations.All(r => s.World.Jobs.Values.Any(j => j.IsActive && (j.SourceToken.Id == r.Id || j.DestinationToken.Id == r.Id)))), "no orphaned reservations");

            s.Colony.Movement = new DirectColonistMovement { GroundHeight = q => s.World.Layout.HeightAt(q.X, q.Z) };
            TestSupport.Run(s, 60, 0.1);
            Assert.Greater(furnace.Machine.Input.Count("iron_ore") + furnace.Machine.Output.Count("iron_ingot"), 0, "once a route exists the furnace is supplied");
        }

        /// <summary>Movement that never gets anywhere and reports that the target cannot be reached.</summary>
        private sealed class BlockedMovement : IColonistMovement
        {
            public bool Step(ColonistState c, float speed, float arriveDistance, float dt) => false;
            public bool CannotReach(ColonistState c) => true;
        }

        [Test]
        public void InterruptedHaul_ReleasesReservations_AndKeepsItems()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(p.X + 30, p.Y, p.Z + 20));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 6);
            int ore = Total(s, "iron_ore");
            // Run until the colonist is carrying ore toward the furnace.
            for (int i = 0; i < 600 && c.Carry.Count("iron_ore") == 0; i++) s.Update(0.1);
            Assert.Greater(c.Carry.Count("iron_ore"), 0);
            Assert.IsTrue(s.Construction.Demolish(furnace, s.Player.Inventory).Ok, "destination destroyed mid-haul");
            TestSupport.Run(s, 40, 0.1);
            Assert.AreEqual(ore, Total(s, "iron_ore"), "no ore was lost or duplicated");
            Assert.AreEqual(0, c.Carry.Count("iron_ore"), "carried ore was put back into storage");
            Assert.IsTrue(c.RecentFailures.Any(f => f.Contains("destination is gone")), "the failure reason is recorded");
            Assert.IsTrue(s.World.AllInventories.All(inv => inv.Reservations.All(r => s.World.Jobs.Values.Any(j => j.IsActive && (j.SourceToken.Id == r.Id || j.DestinationToken.Id == r.Id)))), "no orphaned reservations");
        }

        [Test]
        public void TwoColonists_NeverShareAJob()
        {
            GameSession s = Base(out ColonistState a, out Structure crate);
            ColonistState b = s.World.Colonists.Values.First(x => x.Id != a.Id);
            TestSupport.Build(s.World, "bed", new Vec3(s.Player.Position.X - 6, s.Player.Position.Y, s.Player.Position.Z));
            b.Position = s.Player.Position;
            Assert.IsTrue(s.Recruit(b.Id).Ok);
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(s.Player.Position.X + 10, s.Player.Position.Y, s.Player.Position.Z));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 30);
            for (int i = 0; i < 300; i++)
            {
                s.Update(0.1);
                var claimed = s.World.Jobs.Values.Where(j => j.State == JobState.Claimed).ToList();
                Assert.AreEqual(claimed.Count, claimed.Select(j => j.Id).Distinct().Count());
                Assert.IsFalse(a.CurrentJob.IsValid && a.CurrentJob == b.CurrentJob, "duplicate job ownership");
            }
        }

        [Test]
        public void HungryColonist_EatsFromStorage()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            crate.Storage.TryAdd("canned_food", 2);
            c.Vitals.Hunger = 10f;
            TestSupport.Run(s, 20, 0.1);
            Assert.Greater(c.Vitals.Hunger, 30f);
            Assert.AreEqual(1, crate.Storage.Count("canned_food"));
        }

        [Test]
        public void WorkPriorityZero_DisablesJobType()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Assert.IsTrue(s.SetWorkPriority(c.Id, JobType.Haul, 0).Ok);
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(s.Player.Position.X + 8, s.Player.Position.Y, s.Player.Position.Z));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 8);
            TestSupport.Run(s, 30, 0.1);
            Assert.AreEqual(8, crate.Storage.Count("iron_ore"), "hauling disabled");
        }

        [Test]
        public void SaveLoad_MidHaul_ConservesItemsAndContinues()
        {
            GameSession s = Base(out ColonistState c, out Structure crate);
            Vec3 p = s.Player.Position;
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(p.X + 25, p.Y, p.Z + 10));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            crate.Storage.TryAdd("iron_ore", 8);
            crate.Storage.TryAdd("charcoal", 3);
            for (int i = 0; i < 600 && c.Carry.Count("iron_ore") == 0; i++) s.Update(0.1);
            int ore = Total(s, "iron_ore");
            string text = SaveSerializer.Serialize(s.World, "colony", 0);
            var warnings = new List<string>();
            var loaded = new GameSession(TestSupport.Content, SaveSerializer.Deserialize(text, TestSupport.Content, warnings));
            Assert.IsEmpty(warnings);
            loaded.Threats.SpawningEnabled = false;
            ColonistState lc = loaded.World.Colonists[c.Id];
            Assert.AreEqual(c.Carry.Count("iron_ore"), lc.Carry.Count("iron_ore"));
            Assert.AreEqual(c.CurrentJob, lc.CurrentJob);
            Assert.AreEqual(ore, Total(loaded, "iron_ore"));
            TestSupport.Run(loaded, 60, 0.1);
            Assert.Greater(Total(loaded, "iron_ingot"), 0, "the job resumed after loading");
            Assert.AreEqual(ore - Total(loaded, "iron_ore"), 2 * Total(loaded, "iron_ingot"));
        }
    }
}

namespace Afterfall.Tests
{
    public class ColonySafetyTests
    {
        [Test]
        public void Infected_HuntColonists_WhoFleeAndCanDie_WithoutLosingItems()
        {
            GameSession s = TestSupport.NewSession(7070);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.1);
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            ColonistState c = s.World.Colonists.Values.First();
            c.Position = new Vec3(p.X + 1, p.Y, p.Z);
            Assert.IsTrue(s.Recruit(c.Id).Ok);
            c.Carry.TryAdd("wood", 5);
            s.ReportPlayerTransform(new Vec3(p.X - 60, p.Y, p.Z), 0, new PlayerActivity()); // the player is far away

            ZombieState z = s.Zombies.Spawn(new Vec3(c.Position.X + 6, c.Position.Y, c.Position.Z), "wanderer", new DeterministicRandom(4));
            z.Yaw = -90f;
            float health = c.Vitals.Health;
            Vec3 start = c.Position;
            TestSupport.Run(s, 2, 0.1);
            Assert.AreEqual("Fleeing from the infected!", c.Status, "the colonist notices the hunter and runs");
            Assert.Greater(Vec3.DistanceXZ(c.Position, z.Position), 1f);

            c.Vitals.Health = 5f; // gravely wounded: the next hit is fatal
            for (int i = 0; i < 600 && c.Activity != ColonistActivity.Down; i++)
            {
                c.Position = z.Position + new Vec3(1f, 0, 0); // pinned against the infected
                s.Update(0.1);
            }
            Assert.AreEqual(ColonistActivity.Down, c.Activity, "infected can kill colonists");
            Assert.AreEqual(5, s.World.Drops.Values.Sum(d => d.Contents.Count("wood")), "their belongings are dropped, not lost");
            Assert.Less(c.Vitals.Health, health);
            Assert.IsNotNull(start.ToString());
        }
    }
}
