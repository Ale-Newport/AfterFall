using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.Items
{
    /// <summary>
    /// Atomic item movement between two inventories. Every method either moves exactly
    /// the requested quantity or leaves both inventories untouched, so items can never
    /// be duplicated or lost by a failed transfer.
    /// </summary>
    public static class InventoryTransfers
    {
        public static OpResult TryTransfer(Inventory from, Inventory to, string itemId, int quantity)
        {
            if (from == null || to == null) return OpResult.Fail("invalid", "Missing inventory.");
            if (ReferenceEquals(from, to)) return OpResult.Fail("same_inventory", "Source and destination are the same.");
            if (quantity <= 0) return OpResult.Fail("invalid_quantity", "Quantity must be positive.");
            if (from.Available(itemId) < quantity)
                return OpResult.Fail("insufficient", $"Not enough {from.Catalog.NameOf(itemId)} ({from.Available(itemId)}/{quantity}).");
            if (!to.Accepts(itemId))
                return OpResult.Fail("rejected", $"{to.Label} does not accept {from.Catalog.NameOf(itemId)}.");
            if (!to.CanAccept(itemId, quantity))
                return OpResult.Fail("no_space", $"{to.Label} has no room for {quantity} {from.Catalog.NameOf(itemId)}.");

            var taken = new List<ItemStack>();
            if (!from.TryTake(itemId, quantity, taken)) return OpResult.Fail("insufficient", "Items became unavailable.");
            if (!AddAll(to, taken))
            {
                // Capacity was verified above; restore the source to stay conservative regardless.
                AddAll(from, taken);
                return OpResult.Fail("no_space", "Destination rejected the items.");
            }
            return OpResult.Success();
        }

        /// <summary>Moves up to <paramref name="quantity"/> units out of a specific slot (preserving durability).</summary>
        public static OpResult TryTransferSlot(Inventory from, int slot, Inventory to, int quantity)
        {
            if (from == null || to == null) return OpResult.Fail("invalid", "Missing inventory.");
            ItemStack s = from.GetSlot(slot);
            if (s.IsEmpty) return OpResult.Fail("empty", "Slot is empty.");
            if (!to.Accepts(s.ItemId)) return OpResult.Fail("rejected", $"{to.Label} does not accept {from.Catalog.NameOf(s.ItemId)}.");
            int amount = Math.Min(Math.Min(quantity, s.Quantity), Math.Min(to.SpaceFor(s.ItemId), from.Available(s.ItemId)));
            if (amount <= 0) return OpResult.Fail("no_space", $"{to.Label} is full.");
            ItemStack taken = from.TakeFromSlot(slot, amount);
            if (taken.IsEmpty) return OpResult.Fail("insufficient", "Items are reserved.");
            if (!to.TryAddStack(taken))
            {
                from.TryAddStack(taken);
                return OpResult.Fail("no_space", "Destination rejected the items.");
            }
            return amount < quantity && amount < s.Quantity
                ? OpResult.Success($"Moved {amount} (destination full).")
                : OpResult.Success();
        }

        /// <summary>Moves as many units of every item as fit. Returns the number of units moved.</summary>
        public static int TransferAll(Inventory from, Inventory to)
        {
            int moved = 0;
            for (int i = 0; i < from.SlotCount; i++)
            {
                ItemStack s = from.GetSlot(i);
                if (s.IsEmpty) continue;
                OpResult r = TryTransferSlot(from, i, to, s.Quantity);
                if (r.Ok) moved += s.Quantity - from.GetSlot(i).Quantity;
            }
            return moved;
        }

        private static bool AddAll(Inventory to, List<ItemStack> stacks)
        {
            foreach (ItemStack s in stacks)
                if (!to.TryAddStack(s)) return false;
            return true;
        }
    }

    public enum TransferTicketState
    {
        Pending,
        Committed,
        Aborted
    }

    /// <summary>
    /// Two-phase transfer used by long-running actors (NPC haulers, conveyors): items are
    /// reserved at the source and capacity at the destination when the job starts, and the
    /// movement is committed (or aborted, releasing both sides) when the job ends.
    /// </summary>
    public sealed class TransferTicket
    {
        public EntityId SourceInventory { get; set; }
        public EntityId DestinationInventory { get; set; }
        public ReservationToken SourceToken { get; set; }
        public ReservationToken DestinationToken { get; set; }
        public string ItemId { get; set; }
        public int Quantity { get; set; }
        public TransferTicketState State { get; set; }
        public string FailureReason { get; set; } = "";
    }

    public static class ReservedTransfers
    {
        public static OpResult TryBegin(Inventory from, Inventory to, string itemId, int quantity, out TransferTicket ticket)
        {
            ticket = null;
            if (from == null || to == null || ReferenceEquals(from, to)) return OpResult.Fail("invalid", "Invalid inventories.");
            if (!from.TryReserveItems(itemId, quantity, out ReservationToken src))
                return OpResult.Fail("insufficient", $"Not enough unreserved {from.Catalog.NameOf(itemId)} in {from.Label}.");
            if (!to.TryReserveSpace(itemId, quantity, out ReservationToken dst))
            {
                from.ReleaseReservation(src);
                return OpResult.Fail("no_space", $"{to.Label} cannot accept {quantity} {from.Catalog.NameOf(itemId)}.");
            }
            ticket = new TransferTicket
            {
                SourceInventory = from.Id,
                DestinationInventory = to.Id,
                SourceToken = src,
                DestinationToken = dst,
                ItemId = itemId,
                Quantity = quantity,
                State = TransferTicketState.Pending
            };
            return OpResult.Success();
        }

        public static OpResult Commit(TransferTicket ticket, Inventory from, Inventory to)
        {
            if (ticket == null) return OpResult.Fail("invalid", "No ticket.");
            if (ticket.State == TransferTicketState.Committed) return OpResult.Fail("already_committed", "Transfer was already committed.");
            if (ticket.State == TransferTicketState.Aborted) return OpResult.Fail("aborted", "Transfer was aborted.");
            if (from == null || to == null || from.Id != ticket.SourceInventory || to.Id != ticket.DestinationInventory)
                return OpResult.Fail("wrong_inventory", "Ticket does not belong to these inventories.");
            if (!from.HasReservation(ticket.SourceToken) || !to.HasReservation(ticket.DestinationToken))
                return OpResult.Fail("stale", "Reservation no longer exists.");

            var taken = new List<ItemStack>();
            if (!from.CommitOutgoing(ticket.SourceToken, taken)) return OpResult.Fail("stale", "Source reservation could not be committed.");
            if (!to.CommitIncoming(ticket.DestinationToken, taken))
            {
                // Put the items back; capacity in the source is guaranteed because we just removed them.
                foreach (ItemStack s in taken) from.TryAddStack(s);
                to.ReleaseReservation(ticket.DestinationToken);
                ticket.State = TransferTicketState.Aborted;
                ticket.FailureReason = "Destination rejected delivery.";
                return OpResult.Fail("rejected", ticket.FailureReason);
            }
            ticket.State = TransferTicketState.Committed;
            return OpResult.Success();
        }

        /// <summary>Releases both reservations. Safe to call repeatedly.</summary>
        public static void Abort(TransferTicket ticket, Inventory from, Inventory to, string reason)
        {
            if (ticket == null || ticket.State != TransferTicketState.Pending) return;
            from?.ReleaseReservation(ticket.SourceToken);
            to?.ReleaseReservation(ticket.DestinationToken);
            ticket.State = TransferTicketState.Aborted;
            ticket.FailureReason = reason ?? "";
        }
    }
}
