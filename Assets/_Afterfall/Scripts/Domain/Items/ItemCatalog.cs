using System;
using System.Collections.Generic;

namespace Afterfall.Domain.Items
{
    /// <summary>Lookup of item definitions by stable id.</summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> _items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

        public ItemCatalog()
        {
        }

        public ItemCatalog(IEnumerable<ItemDefinition> items)
        {
            foreach (ItemDefinition item in items) Add(item);
        }

        public IEnumerable<ItemDefinition> All => _items.Values;
        public int Count => _items.Count;

        public void Add(ItemDefinition item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.Id)) throw new ArgumentException("Item id must not be empty.");
            if (_items.ContainsKey(item.Id)) throw new ArgumentException($"Duplicate item id '{item.Id}'.");
            _items.Add(item.Id, item);
        }

        public bool Contains(string id) => id != null && _items.ContainsKey(id);

        public bool TryGet(string id, out ItemDefinition item)
        {
            if (id == null)
            {
                item = null;
                return false;
            }
            return _items.TryGetValue(id, out item);
        }

        public ItemDefinition Get(string id)
        {
            if (id != null && _items.TryGetValue(id, out ItemDefinition item)) return item;
            throw new KeyNotFoundException($"Unknown item id '{id}'.");
        }

        public string NameOf(string id) => TryGet(id, out ItemDefinition d) ? d.Name : id;
    }
}
