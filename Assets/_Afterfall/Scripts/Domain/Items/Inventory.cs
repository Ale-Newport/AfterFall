using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.Items
{
    public enum ReservationKind
    {
        /// <summary>Items set aside for removal (e.g. a hauler is coming to pick them up).</summary>
        Outgoing,

        /// <summary>Capacity set aside for items that are on their way.</summary>
        Incoming
    }

    /// <summary>Handle to a reservation held by an inventory. Tokens are single-use.</summary>
    [Serializable]
    public readonly struct ReservationToken : IEquatable<ReservationToken>
    {
        public readonly EntityId InventoryId;
        public readonly long Id;

        public ReservationToken(EntityId inventoryId, long id)
        {
            InventoryId = inventoryId;
            Id = id;
        }

        public bool IsValid => Id > 0;
        public bool Equals(ReservationToken other) => InventoryId == other.InventoryId && Id == other.Id;
        public override bool Equals(object obj) => obj is ReservationToken t && Equals(t);
        public override int GetHashCode() => InventoryId.GetHashCode() * 31 + Id.GetHashCode();
        public override string ToString() => $"res{InventoryId}:{Id}";
    }

    /// <summary>Persistable view of an active reservation.</summary>
    public sealed class ReservationRecord
    {
        public long Id { get; set; }
        public ReservationKind Kind { get; set; }
        public string ItemId { get; set; }
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Slot-based container with weight limits, item filters and a reservation system.
    /// This is the single source of truth for item quantities: UI, world objects and NPC
    /// jobs all read and mutate items exclusively through these operations, and every
    /// mutating operation is atomic (it either fully succeeds or changes nothing).
    /// </summary>
    public sealed class Inventory
    {
        private readonly ItemCatalog _catalog;
        private readonly ItemStack[] _slots;
        private readonly Dictionary<long, ReservationRecord> _reservations = new Dictionary<long, ReservationRecord>();
        private long _nextReservationId = 1;

        public Inventory(EntityId id, ItemCatalog catalog, int slotCount, float maxWeight = 0f, InventoryFilter filter = null, string label = "")
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            Id = id;
            _catalog = catalog;
            _slots = new ItemStack[slotCount];
            MaxWeight = maxWeight;
            Filter = filter;
            Label = label;
        }

        public EntityId Id { get; }
        public string Label { get; set; }
        public int SlotCount => _slots.Length;

        /// <summary>Hard weight cap in kg; 0 means unlimited.</summary>
        public float MaxWeight { get; set; }

        public InventoryFilter Filter { get; set; }

        /// <summary>Incremented on every change; cheap for UI to poll.</summary>
        public int Version { get; private set; }

        public event Action<Inventory> Changed;

        public ItemCatalog Catalog => _catalog;
        public IEnumerable<ReservationRecord> Reservations => _reservations.Values;
        public long NextReservationId => _nextReservationId;

        // ------------------------------------------------------------------ queries

        public ItemStack GetSlot(int index) => _slots[index];

        public bool IsEmpty
        {
            get
            {
                foreach (ItemStack s in _slots)
                    if (!s.IsEmpty) return false;
                return true;
            }
        }

        public int Count(string itemId)
        {
            int total = 0;
            foreach (ItemStack s in _slots)
                if (!s.IsEmpty && s.ItemId == itemId) total += s.Quantity;
            return total;
        }

        public int ReservedOutgoing(string itemId) => SumReservations(ReservationKind.Outgoing, itemId);
        public int ReservedIncoming(string itemId) => SumReservations(ReservationKind.Incoming, itemId);

        /// <summary>Quantity that may be removed right now (total minus outgoing reservations).</summary>
        public int Available(string itemId) => Count(itemId) - ReservedOutgoing(itemId);

        public float TotalWeight
        {
            get
            {
                float w = 0f;
                foreach (ItemStack s in _slots)
                    if (!s.IsEmpty && _catalog.TryGet(s.ItemId, out ItemDefinition d)) w += d.Weight * s.Quantity;
                return w;
            }
        }

        public Dictionary<string, int> Totals()
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ItemStack s in _slots)
            {
                if (s.IsEmpty) continue;
                totals.TryGetValue(s.ItemId, out int c);
                totals[s.ItemId] = c + s.Quantity;
            }
            return totals;
        }

        public int FirstEmptySlot()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].IsEmpty) return i;
            return -1;
        }

        public bool Accepts(string itemId)
        {
            if (!_catalog.TryGet(itemId, out ItemDefinition def)) return false;
            return Filter == null || Filter.Accepts(def);
        }

        /// <summary>
        /// How many units of <paramref name="itemId"/> could be added now, honouring slot
        /// capacity, weight, filter and all incoming reservations held by others.
        /// </summary>
        public int SpaceFor(string itemId)
        {
            if (!_catalog.TryGet(itemId, out ItemDefinition def)) return 0;
            if (Filter != null && !Filter.Accepts(def)) return 0;

            // Simulate the slot layout after every pending incoming reservation lands.
            var ids = new string[_slots.Length];
            var qty = new int[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsEmpty) continue;
                ids[i] = _slots[i].ItemId;
                qty[i] = _slots[i].Quantity;
            }
            float pendingWeight = 0f;
            foreach (ReservationRecord r in _reservations.Values)
            {
                if (r.Kind != ReservationKind.Incoming) continue;
                if (!_catalog.TryGet(r.ItemId, out ItemDefinition rd)) continue;
                pendingWeight += rd.Weight * r.Quantity;
                SimulatePlace(ids, qty, rd, r.Quantity);
            }

            int maxStack = Math.Max(1, def.HasDurability ? 1 : def.MaxStack);
            long slotSpace = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == null) slotSpace += maxStack;
                else if (ids[i] == def.Id && def.IsStackable) slotSpace += Math.Max(0, maxStack - qty[i]);
            }

            long space = slotSpace;
            if (MaxWeight > 0f && def.Weight > 0f)
            {
                float freeWeight = MaxWeight - TotalWeight - pendingWeight;
                long byWeight = freeWeight <= 0 ? 0 : (long)Math.Floor(freeWeight / def.Weight + 1e-4);
                space = Math.Min(space, byWeight);
            }
            return (int)Math.Min(int.MaxValue, Math.Max(0, space));
        }

        public bool CanAccept(string itemId, int quantity) => quantity > 0 && SpaceFor(itemId) >= quantity;

        /// <summary>True if every requirement is available (unreserved) in this inventory.</summary>
        public bool HasAll(IEnumerable<ItemAmount> requirements)
        {
            foreach (KeyValuePair<string, int> kv in ItemAmount.Aggregate(requirements))
                if (Available(kv.Key) < kv.Value) return false;
            return true;
        }

        /// <summary>Lists the shortfall for each requirement that is not satisfied.</summary>
        public List<ItemAmount> Missing(IEnumerable<ItemAmount> requirements)
        {
            var missing = new List<ItemAmount>();
            foreach (KeyValuePair<string, int> kv in ItemAmount.Aggregate(requirements))
            {
                int have = Available(kv.Key);
                if (have < kv.Value) missing.Add(new ItemAmount(kv.Key, kv.Value - have));
            }
            return missing;
        }

        // ------------------------------------------------------------------ add / remove

        /// <summary>Adds the full quantity or nothing.</summary>
        public bool TryAdd(string itemId, int quantity, int durability = -1)
        {
            if (!CanAccept(itemId, quantity)) return false;
            PlaceUnchecked(itemId, quantity, durability);
            NotifyChanged();
            return true;
        }

        /// <summary>Adds as much as fits and returns the amount actually added.</summary>
        public int AddUpTo(string itemId, int quantity, int durability = -1)
        {
            if (quantity <= 0) return 0;
            int toAdd = Math.Min(quantity, SpaceFor(itemId));
            if (toAdd <= 0) return 0;
            PlaceUnchecked(itemId, toAdd, durability);
            NotifyChanged();
            return toAdd;
        }

        /// <summary>Adds an exact stack (preserving durability) or nothing.</summary>
        public bool TryAddStack(ItemStack stack)
        {
            if (stack.IsEmpty) return true;
            return TryAdd(stack.ItemId, stack.Quantity, stack.Durability > 0 ? stack.Durability : -1);
        }

        /// <summary>
        /// Adds several item types atomically (all or nothing) by reserving capacity for
        /// each before committing any of them.
        /// </summary>
        public bool TryAddAll(IEnumerable<ItemAmount> items)
        {
            var tokens = new List<KeyValuePair<ReservationToken, ItemAmount>>();
            foreach (KeyValuePair<string, int> kv in ItemAmount.Aggregate(items))
            {
                if (!TryReserveSpace(kv.Key, kv.Value, out ReservationToken token))
                {
                    foreach (KeyValuePair<ReservationToken, ItemAmount> t in tokens) ReleaseReservation(t.Key);
                    return false;
                }
                tokens.Add(new KeyValuePair<ReservationToken, ItemAmount>(token, new ItemAmount(kv.Key, kv.Value)));
            }
            foreach (KeyValuePair<ReservationToken, ItemAmount> t in tokens)
                CommitIncoming(t.Key, new[] { new ItemStack(t.Value.ItemId, t.Value.Quantity) });
            return true;
        }

        /// <summary>True if all of the given items would fit together.</summary>
        public bool CanAcceptAll(IEnumerable<ItemAmount> items)
        {
            var tokens = new List<ReservationToken>();
            bool ok = true;
            foreach (KeyValuePair<string, int> kv in ItemAmount.Aggregate(items))
            {
                if (!TryReserveSpace(kv.Key, kv.Value, out ReservationToken token))
                {
                    ok = false;
                    break;
                }
                tokens.Add(token);
            }
            foreach (ReservationToken t in tokens) ReleaseReservation(t);
            return ok;
        }

        /// <summary>Removes the full quantity of unreserved items or nothing.</summary>
        public bool TryRemove(string itemId, int quantity)
        {
            return TryTake(itemId, quantity, null);
        }

        /// <summary>
        /// Removes the full quantity of unreserved items (or nothing) and reports the exact
        /// stacks removed so durability can be preserved on transfer.
        /// </summary>
        public bool TryTake(string itemId, int quantity, List<ItemStack> taken)
        {
            if (quantity <= 0 || Available(itemId) < quantity) return false;
            TakeUnchecked(itemId, quantity, taken);
            NotifyChanged();
            return true;
        }

        /// <summary>Removes all requirements atomically. Fails without side effects if any is short.</summary>
        public bool TryConsume(IEnumerable<ItemAmount> requirements)
        {
            Dictionary<string, int> totals = ItemAmount.Aggregate(requirements);
            foreach (KeyValuePair<string, int> kv in totals)
                if (Available(kv.Key) < kv.Value) return false;
            foreach (KeyValuePair<string, int> kv in totals)
                TakeUnchecked(kv.Key, kv.Value, null);
            if (totals.Count > 0) NotifyChanged();
            return true;
        }

        /// <summary>
        /// Removes up to <paramref name="quantity"/> from a specific slot, never touching
        /// quantities reserved for others. Returns the removed stack (may be empty).
        /// </summary>
        public ItemStack TakeFromSlot(int index, int quantity)
        {
            if (index < 0 || index >= _slots.Length) return ItemStack.Empty;
            ItemStack s = _slots[index];
            if (s.IsEmpty || quantity <= 0) return ItemStack.Empty;
            int allowed = Math.Min(s.Quantity, Math.Min(quantity, Available(s.ItemId)));
            if (allowed <= 0) return ItemStack.Empty;
            var taken = new ItemStack(s.ItemId, allowed, s.Durability);
            s.Quantity -= allowed;
            _slots[index] = s.Quantity > 0 ? s : ItemStack.Empty;
            NotifyChanged();
            return taken;
        }

        /// <summary>Reduces durability of the item in a slot. Returns true if the item broke and was removed.</summary>
        public bool Wear(int index, int amount)
        {
            if (index < 0 || index >= _slots.Length || amount <= 0) return false;
            ItemStack s = _slots[index];
            if (s.IsEmpty || !_catalog.TryGet(s.ItemId, out ItemDefinition def) || !def.HasDurability) return false;
            s.Durability -= amount;
            bool broke = s.Durability <= 0;
            _slots[index] = broke ? ItemStack.Empty : s;
            NotifyChanged();
            return broke;
        }

        // ------------------------------------------------------------------ slot manipulation (UI)

        /// <summary>Moves a stack between slots of this inventory: merge if compatible, otherwise swap.</summary>
        public bool MoveSlot(int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= _slots.Length || to >= _slots.Length) return false;
            ItemStack a = _slots[from];
            ItemStack b = _slots[to];
            if (a.IsEmpty) return false;
            if (!b.IsEmpty && b.ItemId == a.ItemId && _catalog.TryGet(a.ItemId, out ItemDefinition def) && def.IsStackable)
            {
                int room = def.MaxStack - b.Quantity;
                if (room <= 0)
                {
                    _slots[from] = b;
                    _slots[to] = a;
                }
                else
                {
                    int moved = Math.Min(room, a.Quantity);
                    b.Quantity += moved;
                    a.Quantity -= moved;
                    _slots[to] = b;
                    _slots[from] = a.Quantity > 0 ? a : ItemStack.Empty;
                }
            }
            else
            {
                _slots[from] = b;
                _slots[to] = a;
            }
            NotifyChanged();
            return true;
        }

        /// <summary>Splits <paramref name="amount"/> off a stack into the first empty slot.</summary>
        public bool SplitSlot(int index, int amount)
        {
            if (index < 0 || index >= _slots.Length) return false;
            ItemStack s = _slots[index];
            if (s.IsEmpty || amount <= 0 || amount >= s.Quantity) return false;
            int empty = FirstEmptySlot();
            if (empty < 0) return false;
            s.Quantity -= amount;
            _slots[index] = s;
            _slots[empty] = new ItemStack(s.ItemId, amount, s.Durability);
            NotifyChanged();
            return true;
        }

        /// <summary>Merges partial stacks and moves items to the front. Totals are unchanged.</summary>
        public void Compact()
        {
            Dictionary<string, int> totals = Totals();
            var durables = new List<ItemStack>();
            foreach (ItemStack s in _slots)
                if (!s.IsEmpty && _catalog.TryGet(s.ItemId, out ItemDefinition d) && d.HasDurability) durables.Add(s);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = ItemStack.Empty;
            foreach (ItemStack s in durables)
            {
                totals.Remove(s.ItemId);
                _slots[FirstEmptySlot()] = s;
            }
            foreach (KeyValuePair<string, int> kv in totals)
                PlaceUnchecked(kv.Key, kv.Value, -1);
            NotifyChanged();
        }

        // ------------------------------------------------------------------ reservations

        /// <summary>Reserves unreserved items so nobody else can take them until committed or released.</summary>
        public bool TryReserveItems(string itemId, int quantity, out ReservationToken token)
        {
            token = default;
            if (quantity <= 0 || Available(itemId) < quantity) return false;
            token = AddReservation(ReservationKind.Outgoing, itemId, quantity);
            return true;
        }

        /// <summary>Reserves capacity for items that will arrive later.</summary>
        public bool TryReserveSpace(string itemId, int quantity, out ReservationToken token)
        {
            token = default;
            if (!CanAccept(itemId, quantity)) return false;
            token = AddReservation(ReservationKind.Incoming, itemId, quantity);
            return true;
        }

        public bool HasReservation(ReservationToken token) => token.InventoryId == Id && _reservations.ContainsKey(token.Id);

        public ReservationRecord GetReservation(ReservationToken token)
        {
            if (token.InventoryId != Id) return null;
            _reservations.TryGetValue(token.Id, out ReservationRecord r);
            return r;
        }

        /// <summary>Cancels a reservation without moving items. Returns false for unknown/used tokens.</summary>
        public bool ReleaseReservation(ReservationToken token)
        {
            if (token.InventoryId != Id || !_reservations.Remove(token.Id)) return false;
            NotifyChanged();
            return true;
        }

        /// <summary>Removes the reserved items and consumes the token. Fails if the token is unknown or already used.</summary>
        public bool CommitOutgoing(ReservationToken token, List<ItemStack> taken)
        {
            ReservationRecord r = GetReservation(token);
            if (r == null || r.Kind != ReservationKind.Outgoing) return false;
            if (Count(r.ItemId) < r.Quantity) return false; // should be impossible; defensive
            _reservations.Remove(token.Id);
            TakeUnchecked(r.ItemId, r.Quantity, taken);
            NotifyChanged();
            return true;
        }

        /// <summary>Delivers items into capacity reserved earlier and consumes the token.</summary>
        public bool CommitIncoming(ReservationToken token, IList<ItemStack> stacks)
        {
            ReservationRecord r = GetReservation(token);
            if (r == null || r.Kind != ReservationKind.Incoming) return false;
            int total = 0;
            foreach (ItemStack s in stacks)
            {
                if (s.IsEmpty) continue;
                if (s.ItemId != r.ItemId) return false;
                total += s.Quantity;
            }
            if (total != r.Quantity) return false;
            _reservations.Remove(token.Id);
            foreach (ItemStack s in stacks)
                if (!s.IsEmpty) PlaceUnchecked(s.ItemId, s.Quantity, s.Durability > 0 ? s.Durability : -1);
            NotifyChanged();
            return true;
        }

        // ------------------------------------------------------------------ persistence support

        /// <summary>Overwrites a slot directly. Only for loading saved state and test setup.</summary>
        public void SetSlotRaw(int index, ItemStack stack)
        {
            _slots[index] = stack.IsEmpty ? ItemStack.Empty : stack;
            NotifyChanged();
        }

        /// <summary>Restores a reservation from a save file.</summary>
        public void RestoreReservation(ReservationRecord record)
        {
            _reservations[record.Id] = record;
            if (record.Id >= _nextReservationId) _nextReservationId = record.Id + 1;
        }

        public void ClearAll()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i] = ItemStack.Empty;
            _reservations.Clear();
            NotifyChanged();
        }

        // ------------------------------------------------------------------ internals

        private ReservationToken AddReservation(ReservationKind kind, string itemId, int quantity)
        {
            long id = _nextReservationId++;
            _reservations[id] = new ReservationRecord { Id = id, Kind = kind, ItemId = itemId, Quantity = quantity };
            NotifyChanged();
            return new ReservationToken(Id, id);
        }

        private int SumReservations(ReservationKind kind, string itemId)
        {
            int total = 0;
            foreach (ReservationRecord r in _reservations.Values)
                if (r.Kind == kind && r.ItemId == itemId) total += r.Quantity;
            return total;
        }

        private void PlaceUnchecked(string itemId, int quantity, int durability)
        {
            ItemDefinition def = _catalog.Get(itemId);
            int remaining = quantity;
            if (def.HasDurability)
            {
                int dur = durability > 0 ? Math.Min(durability, def.MaxDurability) : def.MaxDurability;
                while (remaining > 0)
                {
                    int slot = FirstEmptySlot();
                    if (slot < 0) throw new InvalidOperationException($"Inventory {Id} overflow placing {itemId} (capacity was not checked).");
                    _slots[slot] = new ItemStack(itemId, 1, dur);
                    remaining--;
                }
                return;
            }

            int maxStack = Math.Max(1, def.MaxStack);
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                ItemStack s = _slots[i];
                if (s.IsEmpty || s.ItemId != itemId || s.Quantity >= maxStack) continue;
                int add = Math.Min(maxStack - s.Quantity, remaining);
                s.Quantity += add;
                _slots[i] = s;
                remaining -= add;
            }
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                int add = Math.Min(maxStack, remaining);
                _slots[i] = new ItemStack(itemId, add);
                remaining -= add;
            }
            if (remaining > 0) throw new InvalidOperationException($"Inventory {Id} overflow placing {itemId} (capacity was not checked).");
        }

        private void TakeUnchecked(string itemId, int quantity, List<ItemStack> taken)
        {
            int remaining = quantity;
            for (int i = _slots.Length - 1; i >= 0 && remaining > 0; i--)
            {
                ItemStack s = _slots[i];
                if (s.IsEmpty || s.ItemId != itemId) continue;
                int take = Math.Min(s.Quantity, remaining);
                taken?.Add(new ItemStack(itemId, take, s.Durability));
                s.Quantity -= take;
                _slots[i] = s.Quantity > 0 ? s : ItemStack.Empty;
                remaining -= take;
            }
        }

        private static void SimulatePlace(string[] ids, int[] qty, ItemDefinition def, int quantity)
        {
            int remaining = quantity;
            int maxStack = def.HasDurability ? 1 : Math.Max(1, def.MaxStack);
            if (def.IsStackable)
            {
                for (int i = 0; i < ids.Length && remaining > 0; i++)
                {
                    if (ids[i] != def.Id || qty[i] >= maxStack) continue;
                    int add = Math.Min(maxStack - qty[i], remaining);
                    qty[i] += add;
                    remaining -= add;
                }
            }
            for (int i = 0; i < ids.Length && remaining > 0; i++)
            {
                if (ids[i] != null) continue;
                int add = Math.Min(maxStack, remaining);
                ids[i] = def.Id;
                qty[i] = add;
                remaining -= add;
            }
        }

        private void NotifyChanged()
        {
            Version++;
            Changed?.Invoke(this);
        }

        public override string ToString() => $"Inventory{Id} '{Label}' ({SlotCount} slots)";
    }
}
