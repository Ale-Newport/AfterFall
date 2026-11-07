using System;
using System.Collections.Generic;

namespace Afterfall.Domain.Items
{
    /// <summary>
    /// Restricts which items an inventory accepts (e.g. a furnace fuel slot only accepts
    /// items with BurnSeconds &gt; 0, a generator only liquid fuel). A null filter accepts all.
    /// </summary>
    public sealed class InventoryFilter
    {
        public HashSet<string> AllowedItems { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<ItemCategory> AllowedCategories { get; } = new HashSet<ItemCategory>();
        public HashSet<string> RequiredTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool RequireBurnable { get; set; }
        public bool RequireEnergy { get; set; }

        /// <summary>Human-readable description shown when an item is rejected.</summary>
        public string Description { get; set; } = "";

        public bool Accepts(ItemDefinition item)
        {
            if (item == null) return false;
            if (AllowedItems.Contains(item.Id)) return true;
            bool hasRules = AllowedCategories.Count > 0 || RequiredTags.Count > 0 || RequireBurnable || RequireEnergy;
            // An explicit item whitelist without other rules rejects everything not listed.
            if (!hasRules) return AllowedItems.Count == 0;
            if (AllowedCategories.Count > 0 && !AllowedCategories.Contains(item.Category)) return false;
            foreach (string tag in RequiredTags)
                if (!item.HasTag(tag)) return false;
            if (RequireBurnable && item.BurnSeconds <= 0) return false;
            if (RequireEnergy && item.EnergyKJ <= 0) return false;
            return true;
        }

        public static InventoryFilter Burnable() => new InventoryFilter { RequireBurnable = true, Description = "Only burnable fuel" };
        public static InventoryFilter Energy() => new InventoryFilter { RequireEnergy = true, Description = "Only generator fuel" };

        public static InventoryFilter Only(IEnumerable<string> itemIds, string description)
        {
            var f = new InventoryFilter { Description = description };
            foreach (string id in itemIds) f.AllowedItems.Add(id);
            return f;
        }
    }
}
