using System;
using Afterfall.Core;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Colony
{
    /// <summary>What colonists may gather in a work zone.</summary>
    [Flags]
    public enum GatherKinds
    {
        None = 0,

        /// <summary>Trees and branch piles (logs).</summary>
        Wood = 1,

        /// <summary>Rocks and ore seams.</summary>
        Stone = 2,

        /// <summary>Fiber plants.</summary>
        Fiber = 4,

        /// <summary>Berry bushes and other food.</summary>
        Food = 8,

        All = Wood | Stone | Fiber | Food
    }

    /// <summary>
    /// A circle on the map, marked from the strategic view, where colonists with Gather work go to
    /// chop, mine and pick resources and carry them back to storage. The zone only says where and
    /// what; every log still comes from a real node, cut by a colonist with a real tool.
    /// </summary>
    public sealed class WorkZone
    {
        public const float MinRadius = 6f;
        public const float MaxRadius = 30f;
        public const float DefaultRadius = 14f;

        public EntityId Id { get; set; }
        public Vec3 Center { get; set; }
        public float Radius { get; set; } = DefaultRadius;
        public GatherKinds Gather { get; set; } = GatherKinds.All;

        /// <summary>What the planner found on its last pass (not saved): how many nodes, and what is missing.</summary>
        public string Status { get; set; } = "";

        public bool Contains(Vec3 p) => Vec3.DistanceXZ(Center, p) <= Radius;
    }

    public static class GatherRules
    {
        /// <summary>The kinds a resource node counts as, from what it yields. Salvage (scrap heaps, wrecks) is never gathered by colonists.</summary>
        public static GatherKinds KindsOf(ResourceNodeDefinition def, ItemCatalog items)
        {
            GatherKinds k = GatherKinds.None;
            foreach (ResourceYield y in def.Yields)
            {
                switch (y.ItemId)
                {
                    case "wood": k |= GatherKinds.Wood; break;
                    case "stone":
                    case "iron_ore": k |= GatherKinds.Stone; break;
                    case "fiber": k |= GatherKinds.Fiber; break;
                    default:
                        if (!items.TryGet(y.ItemId, out ItemDefinition d)) break;
                        if (d.Category == ItemCategory.Salvaged) return GatherKinds.None;
                        if (d.Consumable != null) k |= GatherKinds.Food;
                        break;
                }
            }
            return k;
        }

        public static string Describe(GatherKinds k)
        {
            if (k == GatherKinds.None) return "nothing";
            var parts = new System.Collections.Generic.List<string>();
            if ((k & GatherKinds.Wood) != 0) parts.Add("wood");
            if ((k & GatherKinds.Stone) != 0) parts.Add("stone");
            if ((k & GatherKinds.Fiber) != 0) parts.Add("fiber");
            if ((k & GatherKinds.Food) != 0) parts.Add("food");
            return string.Join(", ", parts);
        }
    }
}
