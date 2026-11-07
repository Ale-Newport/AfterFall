using System;
using System.Collections.Generic;

namespace Afterfall.Domain.Items
{
    /// <summary>A quantity of one item type occupying an inventory slot.</summary>
    [Serializable]
    public struct ItemStack
    {
        public string ItemId;
        public int Quantity;

        /// <summary>Remaining durability for durable items (MaxStack 1); 0 for others.</summary>
        public int Durability;

        public ItemStack(string itemId, int quantity, int durability = 0)
        {
            ItemId = itemId;
            Quantity = quantity;
            Durability = durability;
        }

        public static readonly ItemStack Empty = new ItemStack(null, 0);

        public bool IsEmpty => Quantity <= 0 || string.IsNullOrEmpty(ItemId);

        public override string ToString() => IsEmpty ? "(empty)" : $"{ItemId} x{Quantity}" + (Durability > 0 ? $" [{Durability}]" : "");
    }

    /// <summary>An item id with a quantity, used by recipes, costs and loot.</summary>
    [Serializable]
    public struct ItemAmount
    {
        public string ItemId;
        public int Quantity;

        public ItemAmount(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public override string ToString() => $"{Quantity}x {ItemId}";

        /// <summary>Merges duplicate ids so requirement checks are correct for lists like [wood 2, wood 3].</summary>
        public static Dictionary<string, int> Aggregate(IEnumerable<ItemAmount> amounts)
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            if (amounts == null) return totals;
            foreach (ItemAmount a in amounts)
            {
                if (a.Quantity <= 0 || string.IsNullOrEmpty(a.ItemId)) continue;
                totals.TryGetValue(a.ItemId, out int current);
                totals[a.ItemId] = current + a.Quantity;
            }
            return totals;
        }
    }
}
