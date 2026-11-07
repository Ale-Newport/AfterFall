using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Items;

namespace Afterfall.Domain.World
{
    public sealed class ResourceYield
    {
        public string ItemId { get; set; } = "";
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 1;

        /// <summary>Probability per harvest action that this yield drops at all.</summary>
        public float Chance { get; set; } = 1f;
    }

    /// <summary>A harvestable world object type: tree, rock, ore deposit, scrap pile, bush...</summary>
    public sealed class ResourceNodeDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<ResourceYield> Yields { get; set; } = new List<ResourceYield>();

        /// <summary>Harvest actions available before the node is depleted.</summary>
        public int Harvests { get; set; } = 3;

        /// <summary>Tool that improves yield (GatherPower multiplier).</summary>
        public ToolType PreferredTool { get; set; }

        /// <summary>Tool without which harvesting is impossible (None = bare hands work).</summary>
        public ToolType RequiredTool { get; set; }

        /// <summary>Seconds until a depleted node regrows; 0 = never.</summary>
        public float RespawnSeconds { get; set; }

        /// <summary>Seconds one harvest action takes by hand (tools halve it).</summary>
        public float ActionSeconds { get; set; } = 1.2f;

        /// <summary>Noise radius of one harvest action.</summary>
        public float NoiseRadius { get; set; } = 6f;

        public string Verb { get; set; } = "Gather";
        public string Visual { get; set; } = "rock";
    }

    public sealed class ResourceNodeState
    {
        public EntityId Id { get; set; }
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public float Scale { get; set; } = 1f;
        public int RemainingHarvests { get; set; }
        public bool Depleted { get; set; }

        /// <summary>Simulation time at which a depleted node regrows (if its definition allows).</summary>
        public double RespawnAt { get; set; }
    }

    /// <summary>Items lying in the world: dropped stacks, the player's lost belongings, debris.</summary>
    public sealed class WorldDrop
    {
        public EntityId Id { get; set; }
        public Vec3 Position { get; set; }
        public Inventory Contents { get; set; }
        public string Label { get; set; } = "Dropped items";

        /// <summary>The player's belongings after death (highlighted, never despawns).</summary>
        public bool IsBelongings { get; set; }
    }
}
