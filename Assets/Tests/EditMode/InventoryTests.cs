using System.Collections.Generic;
using Afterfall.Domain.Items;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class InventoryTests
    {
        [Test]
        public void Add_StacksUpToMaxStackThenUsesNewSlots()
        {
            Inventory inv = TestSupport.NewInventory(3);
            Assert.IsTrue(inv.TryAdd("wood", 45)); // max stack 30
            Assert.AreEqual(30, inv.GetSlot(0).Quantity);
            Assert.AreEqual(15, inv.GetSlot(1).Quantity);
            Assert.AreEqual(45, inv.Count("wood"));
        }

        [Test]
        public void TryAdd_IsAtomicWhenCapacityInsufficient()
        {
            Inventory inv = TestSupport.NewInventory(2);
            Assert.IsFalse(inv.TryAdd("wood", 61));
            Assert.AreEqual(0, inv.Count("wood"));
            Assert.AreEqual(60, inv.SpaceFor("wood"));
        }

        [Test]
        public void AddUpTo_AddsOnlyWhatFits()
        {
            Inventory inv = TestSupport.NewInventory(1);
            Assert.AreEqual(30, inv.AddUpTo("stone", 50));
            Assert.AreEqual(30, inv.Count("stone"));
        }

        [Test]
        public void WeightLimit_RestrictsCapacity()
        {
            Inventory inv = TestSupport.NewInventory(10, maxWeight: 10f);
            Assert.AreEqual(6, inv.SpaceFor("wood")); // 1.5 kg each
            Assert.IsFalse(inv.TryAdd("wood", 7));
            Assert.IsTrue(inv.TryAdd("wood", 6));
            Assert.AreEqual(9f, inv.TotalWeight, 1e-3f);
        }

        [Test]
        public void Filter_RejectsDisallowedItems()
        {
            Inventory fuel = TestSupport.NewInventory(2, filter: InventoryFilter.Burnable());
            Assert.IsTrue(fuel.CanAccept("wood", 1));
            Assert.IsFalse(fuel.CanAccept("stone", 1));
            Assert.AreEqual(0, fuel.SpaceFor("iron_ore"));
        }

        [Test]
        public void DurableItems_NeverStack_AndKeepDurability()
        {
            Inventory inv = TestSupport.NewInventory(5);
            Assert.IsTrue(inv.TryAdd("stone_axe", 2));
            Assert.AreEqual(1, inv.GetSlot(0).Quantity);
            Assert.AreEqual(1, inv.GetSlot(1).Quantity);
            Assert.AreEqual(60, inv.GetSlot(0).Durability);
        }

        [Test]
        public void Wear_BreaksAndRemovesItemAtZero()
        {
            Inventory inv = TestSupport.NewInventory(2);
            inv.TryAdd("stone_axe", 1);
            Assert.IsFalse(inv.Wear(0, 59));
            Assert.AreEqual(1, inv.GetSlot(0).Durability);
            Assert.IsTrue(inv.Wear(0, 1));
            Assert.IsTrue(inv.GetSlot(0).IsEmpty);
        }

        [Test]
        public void TryConsume_FailsWithoutSideEffectsWhenAnyRequirementMissing()
        {
            Inventory inv = TestSupport.NewInventory(5);
            inv.TryAdd("wood", 5);
            inv.TryAdd("stone", 1);
            var req = new List<ItemAmount> { new ItemAmount("wood", 2), new ItemAmount("stone", 3) };
            Assert.IsFalse(inv.TryConsume(req));
            Assert.AreEqual(5, inv.Count("wood"));
            Assert.AreEqual(1, inv.Count("stone"));
        }

        [Test]
        public void TryConsume_AggregatesDuplicateRequirements()
        {
            Inventory inv = TestSupport.NewInventory(5);
            inv.TryAdd("wood", 4);
            var req = new List<ItemAmount> { new ItemAmount("wood", 3), new ItemAmount("wood", 2) };
            Assert.IsFalse(inv.TryConsume(req), "5 wood requested in total, only 4 held");
            Assert.AreEqual(4, inv.Count("wood"));
        }

        [Test]
        public void TryAddAll_IsAllOrNothing()
        {
            Inventory inv = TestSupport.NewInventory(1);
            var items = new List<ItemAmount> { new ItemAmount("wood", 10), new ItemAmount("stone", 1) };
            Assert.IsFalse(inv.TryAddAll(items));
            Assert.IsTrue(inv.IsEmpty);
            Assert.IsTrue(inv.TryAddAll(new List<ItemAmount> { new ItemAmount("wood", 10) }));
        }

        [Test]
        public void MoveSlot_MergesCompatibleStacksAndSwapsOthers()
        {
            Inventory inv = TestSupport.NewInventory(3);
            inv.SetSlotRaw(0, new ItemStack("wood", 20));
            inv.SetSlotRaw(1, new ItemStack("wood", 20));
            inv.SetSlotRaw(2, new ItemStack("stone", 5));
            Assert.IsTrue(inv.MoveSlot(0, 1));
            Assert.AreEqual(30, inv.GetSlot(1).Quantity);
            Assert.AreEqual(10, inv.GetSlot(0).Quantity);
            Assert.IsTrue(inv.MoveSlot(2, 0));
            Assert.AreEqual("stone", inv.GetSlot(0).ItemId);
            Assert.AreEqual("wood", inv.GetSlot(2).ItemId);
            Assert.AreEqual(40, inv.Count("wood"));
        }

        [Test]
        public void SplitSlot_CreatesNewStackPreservingTotal()
        {
            Inventory inv = TestSupport.NewInventory(3);
            inv.TryAdd("fiber", 10);
            Assert.IsTrue(inv.SplitSlot(0, 4));
            Assert.AreEqual(6, inv.GetSlot(0).Quantity);
            Assert.AreEqual(4, inv.GetSlot(1).Quantity);
            Assert.AreEqual(10, inv.Count("fiber"));
            Assert.IsFalse(inv.SplitSlot(0, 6), "cannot split off an entire stack");
        }

        [Test]
        public void Compact_PreservesTotals()
        {
            Inventory inv = TestSupport.NewInventory(6);
            inv.SetSlotRaw(1, new ItemStack("wood", 5));
            inv.SetSlotRaw(3, new ItemStack("wood", 7));
            inv.SetSlotRaw(5, new ItemStack("stone_axe", 1, 30));
            inv.Compact();
            Assert.AreEqual(12, inv.Count("wood"));
            Assert.AreEqual(1, inv.Count("stone_axe"));
            Assert.AreEqual(30, inv.GetSlot(0).Durability);
        }

        [Test]
        public void Transfer_MovesExactQuantityOrNothing()
        {
            Inventory a = TestSupport.NewInventory(4, id: 1);
            Inventory b = TestSupport.NewInventory(1, id: 2);
            a.TryAdd("stone", 40);
            Assert.IsFalse(InventoryTransfers.TryTransfer(a, b, "stone", 31).Ok);
            Assert.AreEqual(40, a.Count("stone"));
            Assert.AreEqual(0, b.Count("stone"));
            Assert.IsTrue(InventoryTransfers.TryTransfer(a, b, "stone", 30).Ok);
            Assert.AreEqual(10, a.Count("stone"));
            Assert.AreEqual(30, b.Count("stone"));
        }

        [Test]
        public void TransferSlot_PreservesDurability()
        {
            Inventory a = TestSupport.NewInventory(2, id: 1);
            Inventory b = TestSupport.NewInventory(2, id: 2);
            a.SetSlotRaw(0, new ItemStack("hammer", 1, 42));
            Assert.IsTrue(InventoryTransfers.TryTransferSlot(a, 0, b, 1).Ok);
            Assert.AreEqual(42, b.GetSlot(0).Durability);
        }

        [Test]
        public void Transfer_RejectedByFilterLeavesSourceIntact()
        {
            Inventory a = TestSupport.NewInventory(2, id: 1);
            Inventory fuel = TestSupport.NewInventory(2, filter: InventoryFilter.Burnable(), id: 2);
            a.TryAdd("stone", 5);
            Assert.AreEqual("rejected", InventoryTransfers.TryTransfer(a, fuel, "stone", 5).Code);
            Assert.AreEqual(5, a.Count("stone"));
        }
    }
}
