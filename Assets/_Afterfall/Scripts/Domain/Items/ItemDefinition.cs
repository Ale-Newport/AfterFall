using System;

namespace Afterfall.Domain.Items
{
    /// <summary>The eight resource categories of the AFTERFALL economy.</summary>
    public enum ItemCategory
    {
        Natural,
        Salvaged,
        Processed,
        FoodMedicine,
        Tool,
        Construction,
        Mechanical,
        Electrical
    }

    public enum ToolType
    {
        None,
        Axe,
        Pickaxe,
        Hammer,
        Knife,
        Weapon
    }

    /// <summary>Effects applied when an item is consumed (eaten, drunk, applied).</summary>
    public sealed class ConsumableEffect
    {
        public float Hunger { get; set; }
        public float Thirst { get; set; }
        public float Health { get; set; }
        public float Stamina { get; set; }

        /// <summary>Item returned after consumption (e.g. an empty bottle), "" for none.</summary>
        public string Returns { get; set; } = "";
    }

    /// <summary>Stats for items usable as tools and/or melee weapons.</summary>
    public sealed class ToolStats
    {
        public ToolType Type { get; set; }

        /// <summary>Multiplier on yield/speed when gathering with a matching node type.</summary>
        public float GatherPower { get; set; } = 1f;

        public float Damage { get; set; } = 8f;
        public float StaminaCost { get; set; } = 8f;
        public float Cooldown { get; set; } = 0.8f;
        public float Range { get; set; } = 1.8f;

        /// <summary>Durability lost per use (gather hit or attack).</summary>
        public int WearPerUse { get; set; } = 1;
    }

    /// <summary>
    /// Static, data-driven description of an item type. Loaded from content JSON and
    /// treated as immutable at runtime. Runtime quantities live in <see cref="Inventory"/>.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ItemCategory Category { get; set; }
        public int MaxStack { get; set; } = 1;

        /// <summary>Kilograms per unit.</summary>
        public float Weight { get; set; }

        /// <summary>Litres per unit (informational for now; future container volume limits).</summary>
        public float Volume { get; set; }

        /// <summary>0 = item has no durability.</summary>
        public int MaxDurability { get; set; }

        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>Short label used by the procedural icon renderer.</summary>
        public string Icon { get; set; } = "";

        /// <summary>Hex colour for icons and world drops.</summary>
        public string Color { get; set; } = "#8A8A8A";

        public ConsumableEffect Consumable { get; set; }
        public ToolStats Tool { get; set; }

        /// <summary>Seconds of combustion when used as fuel in a fire-based station (furnace, campfire).</summary>
        public float BurnSeconds { get; set; }

        /// <summary>Kilojoules released when burned in a generator.</summary>
        public float EnergyKJ { get; set; }

        public bool HasDurability => MaxDurability > 0;
        public bool IsStackable => MaxStack > 1 && !HasDurability;
        public bool IsConsumable => Consumable != null;
        public bool IsTool => Tool != null;

        public bool HasTag(string tag)
        {
            if (Tags == null) return false;
            foreach (string t in Tags)
                if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public override string ToString() => Id;
    }
}
