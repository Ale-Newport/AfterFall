using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.Threats;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Regression tests for defects found in the v0.1.0 code review.</summary>
    public class RegressionTests
    {
        private static GameSession RoundTrip(GameSession s)
        {
            string text = SaveSerializer.Serialize(s.World, "regression", 0);
            WorldState w = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            return new GameSession(TestSupport.Content, w);
        }

        [Test]
        public void EverySimulationRandomStream_ResumesAfterSaveAndLoad()
        {
            GameSession s = TestSupport.NewSession(77);
            WorldState w = s.World;
            DeterministicRandom[] streams = { w.SessionRng, w.ZombieRng, w.ThreatRng, w.FarmRng };
            for (int i = 0; i < streams.Length; i++)
                for (int k = 0; k <= i * 3 + 5; k++) streams[i].NextULong();
            Assert.AreSame(w.SessionRng, s.Rng, "harvest and loot rolls use the saved session stream");

            GameSession loaded = RoundTrip(s);
            WorldState l = loaded.World;
            Assert.AreEqual(w.SessionRng.State, l.SessionRng.State);
            Assert.AreEqual(w.ZombieRng.State, l.ZombieRng.State);
            Assert.AreEqual(w.ThreatRng.State, l.ThreatRng.State);
            Assert.AreEqual(w.FarmRng.State, l.FarmRng.State);
            Assert.AreEqual(w.SessionRng.NextULong(), l.SessionRng.NextULong(), "the next roll matches the unsaved game");
        }

        [Test]
        public void ZombieTimers_SurviveSaveAndLoad()
        {
            GameSession s = TestSupport.NewSession(78);
            s.Threats.SpawningEnabled = false;
            ZombieState z = s.Zombies.Spawn(new Vec3(s.Player.Position.X + 60f, s.Player.Position.Y, s.Player.Position.Z), "wanderer", s.World.ZombieRng);
            z.AttackCooldown = 1.25f;
            z.ModeTime = 7.5f;
            z.IdleTimer = 3.5f;

            ZombieState l = RoundTrip(s).World.Zombies[z.Id];
            Assert.AreEqual(1.25f, l.AttackCooldown, 1e-5f);
            Assert.AreEqual(7.5f, l.ModeTime, 1e-5f);
            Assert.AreEqual(3.5f, l.IdleTimer, 1e-5f);
        }

        [Test]
        public void TakingTheLastStackOutOfADrop_RemovesTheDrop()
        {
            GameSession s = TestSupport.NewSession(79);
            WorldDrop d = s.Construction.CreateDrop(new List<ItemStack> { new ItemStack("stone", 3) }, s.Player.Position, "Pile", false);
            int slot = Enumerable.Range(0, d.Contents.SlotCount).First(i => !d.Contents.GetSlot(i).IsEmpty);

            Assert.IsTrue(s.Transfer(d.Contents.Id, slot, s.Player.Inventory.Id, 3).Ok);
            Assert.IsFalse(s.World.Drops.ContainsKey(d.Id), "an emptied drop leaves the world");
            Assert.IsFalse(RoundTrip(s).World.Drops.ContainsKey(d.Id), "and is not saved");
        }

        [Test]
        public void ADeadColonist_FreesTheirBed()
        {
            GameSession s = TestSupport.NewSession(80);
            s.Threats.SpawningEnabled = false;
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            ColonistState a = s.World.Colonists.Values.First();
            ColonistState b = s.World.Colonists.Values.Skip(1).First();
            a.Position = p;
            Assert.IsTrue(s.Recruit(a.Id).Ok);

            s.Colony.DamageColonist(a.Id, 1000f, "test");
            Assert.AreEqual(ColonistActivity.Down, a.Activity);

            b.Position = p;
            OpResult r = s.Recruit(b.Id);
            Assert.IsTrue(r.Ok, r.Message);
        }

        [Test]
        public void WateringWithAFullBackpack_KeepsEveryBottle()
        {
            GameSession s = TestSupport.NewSession(81);
            s.World.Tech.Unlocked.Add("agriculture");
            Structure plot = TestSupport.Build(s.World, "planter_bed", new Vec3(s.Player.Position.X + 2, s.Player.Position.Y, s.Player.Position.Z));
            Inventory inv = s.Player.Inventory;
            inv.ClearAll();
            inv.SetSlotRaw(0, new ItemStack("water_bottle", 3));
            for (int i = 1; i < inv.SlotCount; i++) inv.SetSlotRaw(i, new ItemStack("fiber", 1));
            int before = TestSupport.TotalUnits(inv);

            OpResult r = s.WaterCrop(plot.Id);

            Assert.IsFalse(r.Ok);
            Assert.AreEqual("inventory_full", r.Code);
            Assert.AreEqual(before, TestSupport.TotalUnits(inv), "no bottle is destroyed");
            Assert.AreEqual(3, inv.Count("water_bottle"));
        }

        [Test]
        public void ClearingARecipeMidCycle_WaitsForTheCycle()
        {
            GameSession s = TestSupport.NewSession(82);
            s.World.Tech.Unlocked.Add("metalworking");
            Structure f = TestSupport.Build(s.World, "furnace", new Vec3(s.Player.Position.X + 3, s.Player.Position.Y, s.Player.Position.Z));
            Assert.IsTrue(s.SetMachineRecipe(f.Id, "smelt_iron").Ok);
            f.Machine.Input.TryAdd("iron_ore", 2);
            f.Machine.Fuel.TryAdd("wood", 2);
            TestSupport.Run(s, 1.0);
            Assert.IsTrue(f.Machine.CycleActive);

            Assert.AreEqual("busy", s.SetMachineRecipe(f.Id, "").Code);
            Assert.AreEqual("smelt_iron", f.Machine.RecipeId);
            Assert.IsTrue(s.SetMachineRecipe(f.Id, "smelt_iron").Ok, "the machine is not locked");
        }

        [Test]
        public void ARaidWhoseOriginBecameUnfair_DoesNotSpawnThere()
        {
            GameSession s = TestSupport.NewSession(83);
            Vec3 basePos = s.Player.Position;
            TestSupport.Build(s.World, "bed", basePos);
            ThreatState t = s.World.Threat;
            var origin = new Vec3(basePos.X + 90f, basePos.Y, basePos.Z);
            t.PendingRaidOrigin = origin;
            t.PendingRaidAt = s.World.Clock.ElapsedSeconds;
            // The player walks to where the group was announced.
            s.Player.Position = origin;
            var existing = new HashSet<EntityId>(s.World.Zombies.Keys);

            s.Threats.Tick(0.1);

            foreach (ZombieState z in s.World.Zombies.Values)
            {
                if (existing.Contains(z.Id)) continue;
                Assert.GreaterOrEqual(Vec3.Distance(z.Position, s.Player.Position), ThreatDirector.MinSpawnDistance - 5f, "no infected appear next to the player");
            }
            Assert.IsTrue(t.PendingRaidAt < 0 || t.PendingRaidAt > s.World.Clock.ElapsedSeconds, "the raid is rerouted with a new warning or called off");
            if (t.PendingRaidAt >= 0)
                Assert.IsTrue(s.Threats.IsValidSpawn(t.PendingRaidOrigin, ThreatDirector.MinSpawnDistance));
        }
    }
}
