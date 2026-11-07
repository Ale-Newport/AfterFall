using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>
    /// Headless versions of the prototype acceptance scenario, driven exclusively through
    /// <see cref="GameSession"/> player commands (the same API the Unity presentation uses).
    /// Walking is simulated by moving the player next to targets.
    /// </summary>
    public class AcceptanceLoopTests
    {
        private static void MoveTo(GameSession s, Vec3 target, float offset = 1.2f)
        {
            s.ReportPlayerTransform(new Vec3(target.X + offset, target.Y, target.Z), 0f, new PlayerActivity());
        }

        private static void HarvestUntil(GameSession s, string itemId, int quantity, params string[] nodeDefs)
        {
            var candidates = s.World.ResourceNodes.Values
                .Where(n => nodeDefs.Contains(n.DefId) && !n.Depleted)
                .OrderBy(n => Vec3.DistanceXZ(n.Position, s.Player.Position))
                .ToList();
            foreach (ResourceNodeState node in candidates)
            {
                MoveTo(s, node.Position);
                int guard = 0;
                while (!node.Depleted && s.Player.Inventory.Count(itemId) < quantity && guard++ < 20)
                {
                    OpResult r = s.Harvest(node.Id);
                    Assert.IsTrue(r.Ok || r.Code == "busy" || r.Code == "exhausted", r.ToString());
                    TestSupport.Run(s, 1.6, 0.1);
                }
                if (s.Player.Inventory.Count(itemId) >= quantity) return;
            }
            Assert.Fail($"Could not gather {quantity} {itemId}; have {s.Player.Inventory.Count(itemId)}.");
        }

        private static void Craft(GameSession s, string recipe, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                OpResult r = s.Craft(recipe);
                Assert.IsTrue(r.Ok, $"{recipe}: {r}");
                TestSupport.Run(s, s.Content.Recipe(recipe).Duration + 0.3, 0.1);
            }
        }

        private static Structure Construct(GameSession s, string defId, Vec3 at, PlacementProbe probe = null)
        {
            MoveTo(s, at, 2f);
            OpResult placed = s.PlaceBlueprint(defId, at, 0f, probe ?? new PlacementProbe { Distance = 2f }, out Structure st);
            Assert.IsTrue(placed.Ok, placed.ToString());
            for (int i = 0; i < 200 && !st.IsComplete; i++)
            {
                OpResult r = s.BuildStep(st.Id, 0.5f);
                Assert.IsTrue(r.Ok, $"{defId}: {r}");
                TestSupport.Run(s, 0.5, 0.1);
            }
            Assert.IsTrue(st.IsComplete);
            return st;
        }

        private static int SlotOf(Inventory inv, string itemId)
        {
            for (int i = 0; i < inv.SlotCount; i++)
                if (inv.GetSlot(i).ItemId == itemId) return i;
            return -1;
        }

        private static void Deposit(GameSession s, string itemId, int qty, Inventory target)
        {
            while (qty > 0)
            {
                int slot = SlotOf(s.Player.Inventory, itemId);
                Assert.GreaterOrEqual(slot, 0, $"player has no {itemId}");
                int amount = Math.Min(qty, s.Player.Inventory.GetSlot(slot).Quantity);
                OpResult r = s.Transfer(s.Player.Inventory.Id, slot, target.Id, amount);
                Assert.IsTrue(r.Ok, r.ToString());
                qty -= amount;
            }
        }

        private static GameSession QuietSession(int seed)
        {
            GameSession s = TestSupport.NewSession(seed);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            return s;
        }

        [Test]
        public void EarlyGame_FromBareHandsToShelter_Organic()
        {
            GameSession s = QuietSession(777);
            Vec3 camp = s.Player.Position;

            // Loot the starting cache.
            Structure cache = s.World.Structures.Values.First(x => x.PoiId == "camp" && x.Storage != null);
            MoveTo(s, cache.Position);
            Assert.IsTrue(s.TakeAll(cache.Storage.Id).Ok);
            Assert.AreEqual(2, s.Player.Inventory.Count("water_bottle"));

            // Bare hands: fiber, branches, stones.
            HarvestUntil(s, "fiber", 8, "plant_fiber");
            HarvestUntil(s, "wood", 4, "branch_pile");
            HarvestUntil(s, "stone", 7, "rock_small");
            Craft(s, "rope", 2);
            Craft(s, "stone_axe");
            Assert.AreEqual(1, s.Player.Inventory.Count("stone_axe"));

            // Equip the axe and chop trees.
            Assert.IsTrue(s.UseItem(SlotOf(s.Player.Inventory, "stone_axe")).Ok);
            Assert.AreEqual("stone_axe", s.EquippedItem.Id);
            HarvestUntil(s, "wood", 24, "tree_pine", "tree_birch");
            HarvestUntil(s, "stone", 4, "rock_small");

            // Workbench → planks → foundation.
            Vec3 basePos = s.World.Layout.Ground(camp.X + 6, camp.Z + 6);
            Construct(s, "workbench", basePos);
            Assert.Greater(s.World.Metrics.FirstConstructionAt, 0);
            MoveTo(s, basePos);
            Craft(s, "saw_planks", 3);
            Assert.GreaterOrEqual(s.Player.Inventory.Count("planks"), 18);
            Structure foundation = Construct(s, "foundation", s.World.Layout.Ground(basePos.X + 4, basePos.Z));
            Assert.AreEqual(StructureState.Complete, foundation.State);

            // Drink the found water: the bottle comes back.
            int slot = SlotOf(s.Player.Inventory, "water_bottle");
            float thirst = s.Player.Vitals.Thirst;
            Assert.IsTrue(s.UseItem(slot).Ok);
            Assert.Greater(s.Player.Vitals.Thirst, thirst);
            Assert.AreEqual(1, s.Player.Inventory.Count("empty_bottle"));

            // Save, load, and keep playing.
            string dir = Path.Combine(Path.GetTempPath(), "afterfall_accept_" + Guid.NewGuid().ToString("N"));
            try
            {
                var saves = new SaveService(new SaveFileStore(dir));
                Assert.IsTrue(saves.Save(s, "accept", s.Now, out string msg), msg);
                LoadResult loaded = saves.Load("accept", TestSupport.Content);
                Assert.IsTrue(loaded.Ok, loaded.Error);
                GameSession l = loaded.Session;
                Assert.AreEqual(s.Player.Inventory.Count("planks"), l.Player.Inventory.Count("planks"));
                Assert.AreEqual("stone_axe", l.EquippedItem.Id);
                Assert.IsTrue(l.World.Structures[foundation.Id].IsComplete);
                Assert.IsTrue(l.World.Tech.IsUnlocked("survival"));
                l.Threats.SpawningEnabled = false;
                MoveTo(l, basePos);
                Assert.IsTrue(l.Craft("saw_planks").Ok, "crafting at the reloaded workbench works");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void IndustrialChain_SmeltPowerMachineAndRecover()
        {
            GameSession s = QuietSession(778);
            Inventory bag = s.Player.Inventory;
            bag.MaxWeight = 200f; // fixture: salvage gathered on earlier trips
            bag.TryAdd("wood", 20);
            bag.TryAdd("stone", 18);
            bag.TryAdd("iron_ore", 10);
            bag.TryAdd("scrap_metal", 12);
            bag.TryAdd("mechanical_parts", 7);
            bag.TryAdd("copper_wire", 8);
            bag.TryAdd("circuit_board", 1);
            bag.TryAdd("planks", 8);
            bag.TryAdd("charcoal", 3);
            Vec3 c = s.Player.Position;

            Construct(s, "workbench", s.World.Layout.Ground(c.X + 5, c.Z));
            TestSupport.Run(s, 0.5);
            Assert.IsTrue(s.World.Tech.IsUnlocked("metalworking"), "iron ore + workbench unlock metalworking");

            Structure furnace = Construct(s, "furnace", s.World.Layout.Ground(c.X + 5, c.Z + 4));
            Assert.IsTrue(s.SetMachineRecipe(furnace.Id, "smelt_iron").Ok);
            Deposit(s, "iron_ore", 10, furnace.Machine.Input);
            Deposit(s, "wood", 3, furnace.Machine.Fuel);
            TestSupport.Run(s, 42, 0.1);
            Assert.AreEqual(5, furnace.Machine.Output.Count("iron_ingot"));
            Assert.AreEqual("output_only", s.Transfer(bag.Id, SlotOf(bag, "wood"), furnace.Machine.Output.Id, 1).Code);
            Assert.IsTrue(s.TakeAll(furnace.Machine.Output.Id).Ok);
            Assert.AreEqual(5, bag.Count("iron_ingot"));

            Assert.IsFalse(s.World.Tech.IsUnlocked("electricity"));
            bag.TryAdd("schematic_generator", 1); // looted at the Ridge Substation
            TestSupport.Run(s, 0.5);
            Assert.IsTrue(s.World.Tech.IsUnlocked("electricity"));

            Structure gen = Construct(s, "generator", s.World.Layout.Ground(c.X - 3, c.Z + 8));
            bag.TryAdd("iron_ingot", 6);
            Structure shop = Construct(s, "workshop", s.World.Layout.Ground(c.X - 3, c.Z + 11));
            Assert.IsTrue(s.World.HasPowerLink(gen.Id, shop.Id));
            Assert.IsTrue(s.SetMachineRecipe(shop.Id, "gears").Ok);
            shop.Machine.Input.TryAdd("iron_ingot", 60); // fixture: a stockpile of ingots
            TestSupport.Run(s, 2);
            Assert.AreEqual(MachineStatus.MissingPower, shop.Machine.Status, "no fuel yet");

            Deposit(s, "charcoal", 1, gen.Generator.Fuel);
            TestSupport.Run(s, 20, 0.1);
            Assert.AreEqual(MachineStatus.Working, shop.Machine.Status);
            Assert.GreaterOrEqual(shop.Machine.Output.Count("gear"), 4);
            Assert.IsTrue(s.World.Tech.IsUnlocked("water_works"), "machining 4 gears unlocks Water Works");

            TestSupport.Run(s, 300, 0.1); // 150 kJ at 500 W lasts 300 s
            Assert.AreEqual(MachineStatus.MissingPower, shop.Machine.Status);
            StringAssert.Contains("fuel", shop.Machine.StatusDetail);
            int gears = shop.Machine.Output.Count("gear");
            TestSupport.Run(s, 15, 0.1);
            Assert.AreEqual(gears, shop.Machine.Output.Count("gear"));

            Deposit(s, "charcoal", 1, gen.Generator.Fuel);
            TestSupport.Run(s, 15, 0.1);
            Assert.AreEqual(MachineStatus.Working, shop.Machine.Status, "restoring fuel resumes production");
            Assert.Greater(shop.Machine.Output.Count("gear"), gears);
            Assert.Greater(s.World.Metrics.FirstPoweredOutputAt, 0);
        }
    }
}
