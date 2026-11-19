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
    /// <summary>
    /// Long unattended sessions: a working settlement with colonists, machines, crops and threats
    /// runs for in-game days while saves are made and reloaded, and every invariant a player relies
    /// on must hold throughout.
    /// </summary>
    public class SoakTests
    {
        private static GameSession Settlement(int seed)
        {
            GameSession s = TestSupport.NewSession(seed);
            s.World.Tech.Unlocked.Add("metalworking");
            s.World.Tech.Unlocked.Add("agriculture");
            Vec3 p = s.Player.Position;
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z));
            TestSupport.Build(s.World, "bed", new Vec3(p.X - 3, p.Y, p.Z + 3));
            Structure crate = TestSupport.Build(s.World, "storage_crate", new Vec3(p.X + 3, p.Y, p.Z));
            Structure furnace = TestSupport.Build(s.World, "furnace", new Vec3(p.X + 12, p.Y, p.Z + 6));
            Assert.IsTrue(s.Machines.SetRecipe(furnace, "smelt_iron").Ok);
            TestSupport.Build(s.World, "planter_bed", new Vec3(p.X + 6, p.Y, p.Z - 4));
            crate.Storage.TryAdd("iron_ore", 20);
            crate.Storage.TryAdd("wood", 20);
            crate.Storage.TryAdd("seed_potato", 4);
            crate.Storage.TryAdd("water_bottle", 6);
            crate.Storage.TryAdd("canned_food", 6);
            foreach (ColonistState c in s.World.Colonists.Values.Take(2).ToList())
            {
                c.Position = new Vec3(p.X + 1, p.Y, p.Z + 1);
                Assert.IsTrue(s.Recruit(c.Id).Ok);
            }
            return s;
        }

        private static GameSession Reload(GameSession s)
        {
            var warnings = new List<string>();
            WorldState w = SaveSerializer.Deserialize(SaveSerializer.Serialize(s.World, "soak", 0), TestSupport.Content, warnings);
            return new GameSession(TestSupport.Content, w);
        }

        private static void AssertInvariants(GameSession s, string when)
        {
            CollectionAssert.IsEmpty(SaveValidator.Validate(s.World), "world validation " + when);
            foreach (Inventory inv in s.World.AllInventories)
            {
                foreach (string item in inv.Totals().Keys)
                    Assert.LessOrEqual(inv.ReservedOutgoing(item), inv.Count(item), $"{inv.Label} over-reserves {item} {when}");
                Assert.IsTrue(inv.MaxWeight <= 0f || inv.TotalWeight <= inv.MaxWeight + 0.01f, $"{inv.Label} is overweight {when}");
            }
            foreach (ZombieState z in s.World.Zombies.Values)
                Assert.IsFalse(float.IsNaN(z.Position.X) || float.IsNaN(z.Position.Z), "infected position is NaN " + when);
            foreach (ColonistState c in s.World.Colonists.Values)
                Assert.IsFalse(float.IsNaN(c.Position.X) || float.IsNaN(c.Position.Z), $"{c.Name}'s position is NaN {when}");
        }

        [Test]
        public void ASettlementRunsForTwoDays_WithSavesAndReloads_WithoutBreakingInvariants()
        {
            GameSession s = Settlement(4242);
            double day = s.World.Clock.DayLengthSeconds;
            for (int leg = 0; leg < 8; leg++)
            {
                TestSupport.Run(s, day / 4, 0.1);
                AssertInvariants(s, $"after {leg + 1} quarter-days");
                s = Reload(s);
                AssertInvariants(s, $"after reload {leg + 1}");
            }
            Assert.Greater(s.Now, day * 1.99, "two full days elapsed");
        }

        [Test]
        public void ALoadedGame_PlaysOnExactlyLikeTheUnsavedGame()
        {
            // Loading must not change the future: the same world, saved and reloaded, evolves
            // identically to the copy that was never saved.
            GameSession a = Settlement(5151);
            TestSupport.Run(a, 120, 0.1);
            GameSession b = Reload(a);

            for (int i = 0; i < 10; i++)
            {
                TestSupport.Run(a, 60, 0.1);
                TestSupport.Run(b, 60, 0.1);
                string sa = SaveSerializer.Serialize(a.World, "soak", 0);
                string sb = SaveSerializer.Serialize(b.World, "soak", 0);
                Assert.AreEqual(Strip(sa), Strip(sb), $"the two copies diverged after {(i + 1) * 60} s");
            }
        }

        /// <summary>Drops the envelope (timestamps, checksum) so only the world payload is compared.</summary>
        private static string Strip(string save) => Newtonsoft.Json.Linq.JObject.Parse(save)["payload"]["World"].ToString();
    }
}
