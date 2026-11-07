using System.Collections.Generic;
using Afterfall.Domain.Items;

namespace Afterfall.Domain.Construction
{
    public enum BuildingCategory
    {
        Structure,
        Storage,
        Crafting,
        Industry,
        Power,
        Defense,
        Furniture,
        Lighting,
        Farming,

        /// <summary>World-generated props (loot containers, wrecks). Not player-buildable.</summary>
        WorldProp
    }

    public enum SnapMode
    {
        /// <summary>Placed freely on terrain or floors, aligned to a 0.5 m grid.</summary>
        Free,

        /// <summary>Foundations: 3 m grid, snap edge-to-edge with other foundations.</summary>
        Foundation,

        /// <summary>Walls, doors, windows: snap to foundation/floor edges.</summary>
        Wall,

        /// <summary>Roofs and upper floors: snap on top of walls.</summary>
        Roof
    }

    public sealed class StorageSpec
    {
        public int Slots { get; set; } = 16;
        public float MaxWeight { get; set; }
    }

    public sealed class MachineSpec
    {
        /// <summary>Station id; recipes whose Station matches can be configured on this machine.</summary>
        public string Station { get; set; } = "";
        public int InputSlots { get; set; } = 4;
        public int OutputSlots { get; set; } = 4;

        /// <summary>Burnable fuel slots (furnace). 0 = machine does not burn fuel.</summary>
        public int FuelSlots { get; set; }

        /// <summary>Electrical demand while working. 0 = not electrical.</summary>
        public float PowerWatts { get; set; }

        /// <summary>Load-shedding priority: lower numbers keep power longest (1 critical … 5 optional).</summary>
        public int PowerPriority { get; set; } = 3;

        /// <summary>Noise radius in metres while working (attracts the infected).</summary>
        public float NoiseRadius { get; set; } = 8f;

        /// <summary>Level-2 automation: pull inputs from / push outputs to adjacent storage.</summary>
        public float AutoTransferRange { get; set; }
    }

    public sealed class GeneratorSpec
    {
        public float CapacityWatts { get; set; } = 2000f;
        public int FuelSlots { get; set; } = 2;
        public float NoiseRadius { get; set; } = 25f;
    }

    public sealed class BatterySpec
    {
        public float CapacityKJ { get; set; } = 3600f;
        public float MaxRateWatts { get; set; } = 1500f;
    }

    public sealed class PowerNodeSpec
    {
        /// <summary>Maximum cable length (metres) for automatic and manual connections.</summary>
        public float Range { get; set; } = 6f;

        /// <summary>Poles relay power and auto-connect to every pole in range.</summary>
        public bool IsRelay { get; set; }
    }

    public sealed class LightSpec
    {
        public float PowerWatts { get; set; } = 60f;
        public float Range { get; set; } = 14f;
        public int PowerPriority { get; set; } = 4;
    }

    /// <summary>A crop plot: plant a seed item, keep it watered, harvest the crop item.</summary>
    public sealed class FarmSpec
    {
        public string SeedItem { get; set; } = "";
        public string CropItem { get; set; } = "";
        public int YieldMin { get; set; } = 2;
        public int YieldMax { get; set; } = 4;
        public int SeedReturnMin { get; set; }
        public int SeedReturnMax { get; set; } = 1;

        /// <summary>Seconds of watered growth from planting to harvest.</summary>
        public float GrowSeconds { get; set; } = 900f;

        /// <summary>Soil water (0–100) used per second while growing.</summary>
        public float WaterPerSecond { get; set; } = 0.08f;

        /// <summary>A running electric water pump within this range keeps the plot watered.</summary>
        public float IrrigationRange { get; set; } = 10f;
    }

    /// <summary>
    /// Static description of a constructible (or world-generated) structure: cost, build
    /// effort, footprint, placement rules and the functional components it provides.
    /// </summary>
    public sealed class BuildingDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public BuildingCategory Category { get; set; }
        public List<ItemAmount> Cost { get; set; } = new List<ItemAmount>();

        /// <summary>Seconds of construction work after all materials are delivered.</summary>
        public float BuildSeconds { get; set; } = 3f;

        public float MaxHealth { get; set; } = 200f;

        /// <summary>Footprint in metres (X width, Y height, Z depth).</summary>
        public float SizeX { get; set; } = 1f;
        public float SizeY { get; set; } = 1f;
        public float SizeZ { get; set; } = 1f;

        public SnapMode Snap { get; set; }

        /// <summary>Must rest on a foundation or floor (walls, roofs, most machines inside bases are optional).</summary>
        public bool RequiresSupport { get; set; }

        /// <summary>Other pieces may rest on this one (foundations, floors).</summary>
        public bool ProvidesSupport { get; set; }

        public float MaxSlopeDegrees { get; set; } = 25f;
        public string RequiredTech { get; set; } = "";
        public bool Buildable { get; set; } = true;

        public StorageSpec Storage { get; set; }
        public string CraftingStation { get; set; } = "";
        public MachineSpec Machine { get; set; }
        public GeneratorSpec Generator { get; set; }
        public BatterySpec Battery { get; set; }
        public PowerNodeSpec PowerNode { get; set; }
        public LightSpec Light { get; set; }
        public FarmSpec Farm { get; set; }

        public bool IsDoor { get; set; }

        /// <summary>Sleeping spot: sets the respawn point and allows sleeping.</summary>
        public bool IsBed { get; set; }

        /// <summary>Counts as defensive perimeter (walls, barricades) for the threat director.</summary>
        public bool IsDefensive { get; set; }

        /// <summary>Must be placed within a few metres of open water (pumps).</summary>
        public bool RequiresWater { get; set; }

        /// <summary>Whether the finished structure blocks movement (carves navigation).</summary>
        public bool BlocksMovement { get; set; } = true;

        /// <summary>Presentation key that selects the procedural model builder.</summary>
        public string Visual { get; set; } = "box";
        public string Color { get; set; } = "#9A8F7A";

        public bool IsPowerComponent => Generator != null || Battery != null || PowerNode != null || Light != null || (Machine != null && Machine.PowerWatts > 0);

        public override string ToString() => Id;
    }
}
