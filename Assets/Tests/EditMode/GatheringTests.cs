using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.Weather;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class GatheringTests
    {
        private static GameSession Camp(out ColonistState c, out Structure crate, int seed)
        {
            GameSession s = TestSupport.NewSession(seed);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            s.Weather.Hold(WeatherKind.Clear);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.1); // morning
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            crate = TestSupport.Build(s.World, "storage_crate", new Vec3(p.X + 3, p.Y, p.Z));
            c = s.World.Colonists.Values.First();
            c.Position = new Vec3(p.X + 1, p.Y, p.Z + 1);
            Assert.IsTrue(s.Recruit(c.Id).Ok);
            return s;
        }

        private static ResourceNodeState Nearest(GameSession s, params string[] defIds)
        {
            ResourceNodeState n = s.World.ResourceNodes.Values
                .Where(x => defIds.Contains(x.DefId) && !x.Depleted)
                .OrderBy(x => Vec3.DistanceXZ(x.Position, s.Player.Position)).FirstOrDefault();
            Assert.IsNotNull(n, "a " + string.Join("/", defIds) + " near the camp");
            Assert.IsTrue(s.World.Exploration.IsRevealed(n.Position.X, n.Position.Z), "the node is in explored land");
            return n;
        }

        private static int Everywhere(GameSession s, string item) => s.World.AllInventories.Sum(inv => inv.Count(item));

        [Test]
        public void Zone_PlansWork_OnlyForAllowedKinds_WithTheToolInStorage()
        {
            GameSession s = Camp(out ColonistState c, out Structure crate, 7070);
            ResourceNodeState tree = Nearest(s, "tree_pine", "tree_birch");
            Assert.IsTrue(s.AddWorkZone(tree.Position, 7f, out WorkZone zone).Ok);
            Assert.IsTrue(s.SetZoneGather(zone.Id, GatherKinds.All, false).Ok);
            Assert.IsTrue(s.SetZoneGather(zone.Id, GatherKinds.Wood, true).Ok);
            TestSupport.Run(s, 3, 0.25);
            Assert.IsFalse(s.World.Jobs.Values.Any(j => j.Type == JobType.Gather && s.World.ResourceNodes[j.TargetNode].DefId.StartsWith("tree")),
                "no trees are cut without an axe");
            StringAssert.Contains("need an axe in storage", zone.Status);

            Assert.IsTrue(crate.Storage.TryAdd("stone_axe", 1));
            TestSupport.Run(s, 3, 0.25);
            List<Job> gather = s.World.Jobs.Values.Where(j => j.Type == JobType.Gather && j.IsActive).ToList();
            Assert.AreEqual(1, gather.Count, "one gatherer, one job at a time");
            ResourceNodeState target = s.World.ResourceNodes[gather[0].TargetNode];
            Assert.IsTrue(zone.Contains(target.Position));
            Assert.AreNotEqual(GatherKinds.None, GatherRules.KindsOf(TestSupport.Content.Node(target.DefId), TestSupport.Content.Items) & GatherKinds.Wood);
            if (target.DefId.StartsWith("tree")) Assert.AreEqual("stone_axe", gather[0].ItemId, "the axe is fetched from the crate");

            Assert.IsTrue(s.SetWorkPriority(c.Id, JobType.Gather, 0).Ok);
            foreach (Job j in gather) if (j.AssigneeId.IsValid) s.Colony.Fail(c, j, "test");
            TestSupport.Run(s, 3, 0.25);
            Assert.IsFalse(s.World.Jobs.Values.Any(j => j.Type == JobType.Gather && j.IsActive), "nobody gathers once Gather work is off");
            StringAssert.Contains("no colonist has Gather work", zone.Status);
        }

        [Test]
        public void Colonist_FetchesTheAxe_ChopsATree_AndBringsLogsAndAxeHome()
        {
            GameSession s = Camp(out ColonistState c, out Structure crate, 7071);
            ResourceNodeState tree = Nearest(s, "tree_pine", "tree_birch");
            Assert.IsTrue(s.AddWorkZone(tree.Position, 3f, out WorkZone zone).Ok);
            Assert.IsTrue(s.SetZoneGather(zone.Id, GatherKinds.Fiber | GatherKinds.Food | GatherKinds.Stone, false).Ok);
            Assert.IsTrue(crate.Storage.TryAdd("stone_axe", 1));
            int durability = crate.Storage.GetSlot(0).Durability;
            int harvestsBefore = tree.RemainingHarvests;
            bool chopping = false;
            for (int i = 0; i < 4800 && !(crate.Storage.Count("wood") > 0 && crate.Storage.Count("stone_axe") == 1 && !c.CurrentJob.IsValid); i++)
            {
                s.Update(0.25);
                chopping |= c.Status.StartsWith("Chop") && c.Carry.Count("stone_axe") == 1;
                Assert.AreEqual(1, Everywhere(s, "stone_axe"), "the axe is never duplicated or lost");
            }
            Assert.IsTrue(chopping, "the colonist went out with the axe");
            Assert.Greater(crate.Storage.Count("wood"), 0, "logs reach the crate");
            Assert.AreEqual(1, crate.Storage.Count("stone_axe"), "the axe comes back to the crate");
            Assert.Less(tree.RemainingHarvests, harvestsBefore, "the logs came from the tree");
            int slot = Enumerable.Range(0, crate.Storage.SlotCount).First(i => crate.Storage.GetSlot(i).ItemId == "stone_axe");
            Assert.Less(crate.Storage.GetSlot(slot).Durability, durability, "the axe wore down");
            Assert.AreEqual(0, c.Carry.Count("wood"), "the load was put away");
        }

        [Test]
        public void BareHanded_GatherersPickFiberAndBerries()
        {
            GameSession s = Camp(out ColonistState c, out Structure crate, 7072);
            ResourceNodeState plant = Nearest(s, "plant_fiber", "bush_berry");
            Assert.IsTrue(s.AddWorkZone(plant.Position, 6f, out WorkZone zone).Ok);
            Assert.IsTrue(s.SetZoneGather(zone.Id, GatherKinds.Wood | GatherKinds.Stone, false).Ok);
            for (int i = 0; i < 4800 && crate.Storage.Count("fiber") + crate.Storage.Count("berries") == 0; i++) s.Update(0.25);
            Assert.Greater(crate.Storage.Count("fiber") + crate.Storage.Count("berries"), 0, "no tool needed for plants");
        }

        [Test]
        public void Zones_SurviveSaves_AndRemovingOneCancelsItsWork()
        {
            GameSession s = Camp(out ColonistState c, out Structure crate, 7073);
            ResourceNodeState plant = Nearest(s, "plant_fiber", "bush_berry", "branch_pile");
            Assert.IsTrue(s.AddWorkZone(plant.Position, 9f, out WorkZone zone).Ok);
            Assert.IsTrue(s.SetZoneGather(zone.Id, GatherKinds.Stone, false).Ok);
            TestSupport.Run(s, 6, 0.25);

            var warnings = new List<string>();
            WorldState loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(s.World, "zones", 0), TestSupport.Content, warnings);
            Assert.IsEmpty(warnings, string.Join("\n", warnings));
            WorkZone lz = loaded.WorkZones[zone.Id];
            Assert.AreEqual(zone.Center, lz.Center);
            Assert.AreEqual(zone.Radius, lz.Radius, 1e-4);
            Assert.AreEqual(GatherKinds.Wood | GatherKinds.Fiber | GatherKinds.Food, lz.Gather);
            foreach (Job j in s.World.Jobs.Values.Where(x => x.Type == JobType.Gather))
            {
                Job lj = loaded.Jobs[j.Id];
                Assert.AreEqual(j.TargetNode, lj.TargetNode);
                Assert.AreEqual(j.Progress, lj.Progress, 1e-4);
                Assert.AreEqual(j.Harvests, lj.Harvests);
            }

            c.Work.Priorities[JobType.Gather] = 0;
            foreach (Job j in s.World.Jobs.Values.Where(x => x.Type == JobType.Gather && x.AssigneeId.IsValid).ToList()) s.Colony.Fail(c, j, "test");
            c.Work.Priorities[JobType.Gather] = 3;
            Assert.IsTrue(s.RemoveWorkZone(zone.Id).Ok);
            Assert.IsFalse(s.World.Jobs.Values.Any(j => j.Type == JobType.Gather && j.State == JobState.Open), "open work in the zone is cancelled");
            TestSupport.Run(s, 6, 0.25);
            Assert.IsFalse(s.World.Jobs.Values.Any(j => j.Type == JobType.Gather && j.IsActive), "no new work without a zone");
        }

        [Test]
        public void AddWorkZone_NeedsExploredLand_AndHasALimit()
        {
            GameSession s = TestSupport.NewSession(7074);
            Vec3 far = new Vec3(WorldLayout.WorldSize - 20f, 0f, 20f);
            Assert.IsFalse(s.World.Exploration.IsRevealed(far.X, far.Z));
            Assert.AreEqual("unexplored", s.AddWorkZone(far, 10f, out _).Code);
            for (int i = 0; i < GameSession.MaxWorkZones; i++) Assert.IsTrue(s.AddWorkZone(s.Player.Position, 50f, out WorkZone z).Ok && z.Radius == WorkZone.MaxRadius);
            Assert.AreEqual("too_many", s.AddWorkZone(s.Player.Position, 10f, out _).Code);
        }

        [Test]
        public void GatherKinds_FollowWhatANodeYields()
        {
            var c = TestSupport.Content;
            Assert.AreEqual(GatherKinds.Wood, GatherRules.KindsOf(c.Node("tree_pine"), c.Items));
            Assert.AreEqual(GatherKinds.Wood | GatherKinds.Fiber, GatherRules.KindsOf(c.Node("tree_birch"), c.Items));
            Assert.AreEqual(GatherKinds.Stone, GatherRules.KindsOf(c.Node("ore_iron"), c.Items));
            Assert.AreEqual(GatherKinds.Food, GatherRules.KindsOf(c.Node("bush_berry"), c.Items));
            Assert.AreEqual(GatherKinds.None, GatherRules.KindsOf(c.Node("scrap_pile"), c.Items), "salvage is the survivor's job");
            Assert.AreEqual(GatherKinds.None, GatherRules.KindsOf(c.Node("fuel_pump"), c.Items));
        }
    }
}
