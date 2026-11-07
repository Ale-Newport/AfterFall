using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Items;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Economic invariants: reservations and two-phase transfers never duplicate or destroy items.</summary>
    public class ReservationTests
    {
        [Test]
        public void ReservedItems_CannotBeTakenByOthers()
        {
            Inventory inv = TestSupport.NewInventory(4);
            inv.TryAdd("wood", 10);
            Assert.IsTrue(inv.TryReserveItems("wood", 7, out ReservationToken t));
            Assert.AreEqual(3, inv.Available("wood"));
            Assert.IsFalse(inv.TryRemove("wood", 4));
            Assert.IsTrue(inv.TryRemove("wood", 3));
            Assert.IsTrue(inv.ReleaseReservation(t));
            Assert.AreEqual(7, inv.Available("wood"));
        }

        [Test]
        public void TakeFromSlot_RespectsReservations()
        {
            Inventory inv = TestSupport.NewInventory(4);
            inv.TryAdd("wood", 10);
            inv.TryReserveItems("wood", 8, out _);
            ItemStack taken = inv.TakeFromSlot(0, 10);
            Assert.AreEqual(2, taken.Quantity);
            Assert.AreEqual(8, inv.Count("wood"));
        }

        [Test]
        public void CannotReserveMoreThanAvailable()
        {
            Inventory inv = TestSupport.NewInventory(4);
            inv.TryAdd("stone", 5);
            Assert.IsTrue(inv.TryReserveItems("stone", 3, out _));
            Assert.IsFalse(inv.TryReserveItems("stone", 3, out _));
        }

        [Test]
        public void SpaceReservation_ReducesCapacityForOthers()
        {
            Inventory inv = TestSupport.NewInventory(2);
            Assert.IsTrue(inv.TryReserveSpace("wood", 30, out ReservationToken t));
            Assert.AreEqual(30, inv.SpaceFor("wood"));
            Assert.IsFalse(inv.TryAdd("stone", 31));
            Assert.IsTrue(inv.TryAdd("stone", 30));
            Assert.AreEqual(0, inv.SpaceFor("stone"));
            Assert.IsTrue(inv.CommitIncoming(t, new[] { new ItemStack("wood", 30) }), "reserved capacity must still be available");
            Assert.AreEqual(30, inv.Count("wood"));
        }

        [Test]
        public void ReservationTokens_AreSingleUse()
        {
            Inventory inv = TestSupport.NewInventory(4);
            inv.TryAdd("wood", 10);
            inv.TryReserveItems("wood", 4, out ReservationToken t);
            var taken = new List<ItemStack>();
            Assert.IsTrue(inv.CommitOutgoing(t, taken));
            Assert.IsFalse(inv.CommitOutgoing(t, taken), "second commit must fail");
            Assert.IsFalse(inv.ReleaseReservation(t), "used token cannot be released");
            Assert.AreEqual(6, inv.Count("wood"));
        }

        [Test]
        public void TokenFromAnotherInventory_IsRejected()
        {
            Inventory a = TestSupport.NewInventory(4, id: 1);
            Inventory b = TestSupport.NewInventory(4, id: 2);
            a.TryAdd("wood", 5);
            b.TryAdd("wood", 5);
            a.TryReserveItems("wood", 2, out ReservationToken t);
            Assert.IsFalse(b.CommitOutgoing(t, new List<ItemStack>()));
            Assert.AreEqual(5, b.Count("wood"));
        }

        [Test]
        public void TicketCommit_MovesItemsExactlyOnce()
        {
            Inventory src = TestSupport.NewInventory(4, id: 1);
            Inventory dst = TestSupport.NewInventory(4, id: 2);
            src.TryAdd("iron_ore", 12);
            Assert.IsTrue(ReservedTransfers.TryBegin(src, dst, "iron_ore", 8, out TransferTicket ticket).Ok);
            Assert.AreEqual(4, src.Available("iron_ore"));
            Assert.IsTrue(ReservedTransfers.Commit(ticket, src, dst).Ok);
            Assert.AreEqual("already_committed", ReservedTransfers.Commit(ticket, src, dst).Code);
            Assert.AreEqual(4, src.Count("iron_ore"));
            Assert.AreEqual(8, dst.Count("iron_ore"));
        }

        [Test]
        public void TicketAbort_ReleasesBothSidesAndConservesItems()
        {
            Inventory src = TestSupport.NewInventory(4, id: 1);
            Inventory dst = TestSupport.NewInventory(1, id: 2);
            src.TryAdd("wood", 20);
            Assert.IsTrue(ReservedTransfers.TryBegin(src, dst, "wood", 20, out TransferTicket ticket).Ok);
            Assert.AreEqual(10, dst.SpaceFor("wood"));
            ReservedTransfers.Abort(ticket, src, dst, "hauler died");
            Assert.AreEqual(TransferTicketState.Aborted, ticket.State);
            Assert.AreEqual(20, src.Available("wood"));
            Assert.AreEqual(30, dst.SpaceFor("wood"));
            Assert.AreEqual("aborted", ReservedTransfers.Commit(ticket, src, dst).Code);
            Assert.AreEqual(20, src.Count("wood"));
            Assert.AreEqual(0, dst.Count("wood"));
        }

        [Test]
        public void TicketBegin_FailsCleanlyWhenDestinationHasNoRoom()
        {
            Inventory src = TestSupport.NewInventory(4, id: 1);
            Inventory dst = TestSupport.NewInventory(1, id: 2);
            src.TryAdd("wood", 40);
            Assert.IsFalse(ReservedTransfers.TryBegin(src, dst, "wood", 31, out _).Ok);
            Assert.AreEqual(40, src.Available("wood"), "source reservation must be released on failure");
        }

        [Test]
        public void RandomizedOperations_ConserveTotalItems()
        {
            var rng = new DeterministicRandom(42);
            var invs = new[]
            {
                TestSupport.NewInventory(6, id: 1), TestSupport.NewInventory(4, id: 2), TestSupport.NewInventory(3, maxWeight: 20f, id: 3)
            };
            invs[0].TryAdd("wood", 60);
            invs[0].TryAdd("stone", 50);
            invs[1].TryAdd("scrap_metal", 40);
            invs[1].TryAdd("fiber", 30);
            int total = TestSupport.TotalUnits(invs);
            string[] ids = { "wood", "stone", "scrap_metal", "fiber" };
            var tickets = new List<TransferTicket>();
            for (int step = 0; step < 3000; step++)
            {
                Inventory a = invs[rng.Range(0, invs.Length)];
                Inventory b = invs[rng.Range(0, invs.Length)];
                string id = ids[rng.Range(0, ids.Length)];
                int q = rng.Range(1, 25);
                switch (rng.Range(0, 6))
                {
                    case 0: InventoryTransfers.TryTransfer(a, b, id, q); break;
                    case 1: InventoryTransfers.TryTransferSlot(a, rng.Range(0, a.SlotCount), b, q); break;
                    case 2:
                        if (ReservedTransfers.TryBegin(a, b, id, q, out TransferTicket t).Ok) tickets.Add(t);
                        break;
                    case 3:
                        if (tickets.Count > 0)
                        {
                            TransferTicket t2 = tickets[rng.Range(0, tickets.Count)];
                            ReservedTransfers.Commit(t2, Find(invs, t2.SourceInventory), Find(invs, t2.DestinationInventory));
                        }
                        break;
                    case 4:
                        if (tickets.Count > 0)
                        {
                            TransferTicket t3 = tickets[rng.Range(0, tickets.Count)];
                            ReservedTransfers.Abort(t3, Find(invs, t3.SourceInventory), Find(invs, t3.DestinationInventory), "random");
                        }
                        break;
                    case 5: a.MoveSlot(rng.Range(0, a.SlotCount), rng.Range(0, a.SlotCount)); break;
                }
                Assert.AreEqual(total, TestSupport.TotalUnits(invs), $"conservation broken at step {step}");
                foreach (Inventory inv in invs)
                {
                    foreach (string itemId in ids) Assert.GreaterOrEqual(inv.Available(itemId), 0, "reserved more than held");
                    if (inv.MaxWeight > 0) Assert.LessOrEqual(inv.TotalWeight, inv.MaxWeight + 1e-3f);
                }
            }
        }

        private static Inventory Find(Inventory[] invs, EntityId id)
        {
            foreach (Inventory i in invs)
                if (i.Id == id) return i;
            return null;
        }
    }
}
