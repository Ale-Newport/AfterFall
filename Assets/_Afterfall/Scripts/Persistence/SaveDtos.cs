using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.World;

namespace Afterfall.Persistence
{
    // Plain data transfer objects describing the on-disk save schema. The domain model is
    // mapped to/from these explicitly (WorldStateMapper) so the file format can evolve with
    // versioned migrations independently of runtime classes.

    public sealed class SaveHeader
    {
        public string Slot { get; set; } = "";
        public string SavedAtUtc { get; set; } = "";
        public int Day { get; set; }
        public string TimeOfDay { get; set; } = "";
        public double PlaytimeSeconds { get; set; }
        public string Difficulty { get; set; } = "";
        public int Seed { get; set; }
        public string GameVersion { get; set; } = "";
    }

    public sealed class SlotDto
    {
        public int Index { get; set; }
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }
        public int Durability { get; set; }
    }

    public sealed class InventoryDto
    {
        public long Id { get; set; }
        public string Label { get; set; } = "";
        public int Slots { get; set; }
        public float MaxWeight { get; set; }
        public List<SlotDto> Items { get; set; } = new List<SlotDto>();
        public List<ReservationRecord> Reservations { get; set; } = new List<ReservationRecord>();
    }

    public sealed class MachineDto
    {
        public string RecipeId { get; set; } = "";
        public InventoryDto Input { get; set; }
        public InventoryDto Output { get; set; }
        public InventoryDto Fuel { get; set; }
        public float Progress { get; set; }
        public bool CycleActive { get; set; }
        public float FuelBurnRemaining { get; set; }
        public string Status { get; set; } = "";
        public string StatusDetail { get; set; } = "";
        public bool RequestingPower { get; set; }
        public int CompletedCycles { get; set; }
        public double LastCycleCompletedAt { get; set; }
    }

    public sealed class GeneratorDto
    {
        public InventoryDto Fuel { get; set; }
        public double BufferJoules { get; set; }
        public bool Running { get; set; }
        public float OutputWatts { get; set; }
        public string Status { get; set; } = "";
    }

    public sealed class PowerDto
    {
        public bool Powered { get; set; }
        public string Reason { get; set; } = "";
        public float DemandWatts { get; set; }
    }

    public sealed class StructureDto
    {
        public long Id { get; set; }
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public string State { get; set; } = "";
        public Dictionary<string, int> Delivered { get; set; } = new Dictionary<string, int>();
        public float WorkDone { get; set; }
        public float Health { get; set; }
        public bool PlayerBuilt { get; set; }
        public string PoiId { get; set; } = "";
        public bool Searched { get; set; }
        public bool DoorOpen { get; set; }
        public bool Enabled { get; set; } = true;
        public InventoryDto Storage { get; set; }
        public MachineDto Machine { get; set; }
        public GeneratorDto Generator { get; set; }
        public double? BatteryStoredJoules { get; set; }
        public PowerDto Power { get; set; }
    }

    public sealed class PlayerDto
    {
        public long Id { get; set; }
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public SurvivalState Vitals { get; set; }
        public InventoryDto Inventory { get; set; }
        public List<CraftJob> Crafting { get; set; } = new List<CraftJob>();
        public int SelectedHotbar { get; set; }
        public long SpawnStructureId { get; set; }
        public Vec3 WorldSpawn { get; set; }
        public int Deaths { get; set; }
        public double NextActionTime { get; set; }
    }

    public sealed class NodeDto
    {
        public long Id { get; set; }
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public float Scale { get; set; }
        public int Remaining { get; set; }
        public bool Depleted { get; set; }
        public double RespawnAt { get; set; }
    }

    public sealed class DropDto
    {
        public long Id { get; set; }
        public Vec3 Position { get; set; }
        public InventoryDto Contents { get; set; }
        public string Label { get; set; } = "";
        public bool IsBelongings { get; set; }
    }

    public sealed class ZombieDto
    {
        public long Id { get; set; }
        public string Archetype { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public float Health { get; set; }
        public ZombieMode Mode { get; set; }
        public Vec3 Home { get; set; }
        public Vec3 MoveTarget { get; set; }
        public Vec3 LastKnownTarget { get; set; }
        public float TimeSinceSeen { get; set; }
        public float DeadTime { get; set; }
    }

    public sealed class ColonistDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public bool Recruited { get; set; }
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public SurvivalState Vitals { get; set; }
        public InventoryDto Carry { get; set; }
        public Dictionary<string, float> Skills { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, int> Work { get; set; } = new Dictionary<string, int>();
        public long CurrentJob { get; set; }
        public string Activity { get; set; } = "";
        public string Status { get; set; } = "";
        public long HomeBed { get; set; }
        public Vec3 MoveTarget { get; set; }
        public bool HasMoveTarget { get; set; }
        public float Morale { get; set; }
        public List<string> RecentFailures { get; set; } = new List<string>();
    }

    public sealed class JobDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = "";
        public int Priority { get; set; }
        public string State { get; set; } = "";
        public long Assignee { get; set; }
        public string Description { get; set; } = "";
        public string FailureReason { get; set; } = "";
        public double CreatedAt { get; set; }
        public long TargetStructure { get; set; }
        public long SourceInventory { get; set; }
        public long DestinationInventory { get; set; }
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }
        public string Stage { get; set; } = "";
        public long SourceToken { get; set; }
        public long DestinationToken { get; set; }
        public bool DeliverToBlueprint { get; set; }
    }

    public sealed class WorldDto
    {
        public int Seed { get; set; }
        public GameSettings Settings { get; set; }
        public double ElapsedSeconds { get; set; }
        public double DayLengthSeconds { get; set; }
        public double StartTimeOfDay { get; set; }
        public long NextId { get; set; }
        public ulong RngState { get; set; }
        public PlayerDto Player { get; set; }
        public List<StructureDto> Structures { get; set; } = new List<StructureDto>();
        public List<long[]> PowerLinks { get; set; } = new List<long[]>();
        public List<NodeDto> Nodes { get; set; } = new List<NodeDto>();
        public List<DropDto> Drops { get; set; } = new List<DropDto>();
        public List<ZombieDto> Zombies { get; set; } = new List<ZombieDto>();
        public List<ColonistDto> Colonists { get; set; } = new List<ColonistDto>();
        public List<JobDto> Jobs { get; set; } = new List<JobDto>();
        public List<string> UnlockedTechs { get; set; } = new List<string>();
        public Dictionary<string, int> TechProgress { get; set; } = new Dictionary<string, int>();

        /// <summary>Explored cells as a base64 bitset.</summary>
        public string ExploredCells { get; set; } = "";

        public List<string> DiscoveredPois { get; set; } = new List<string>();
        public ThreatState Threat { get; set; }
        public PlaythroughMetrics Metrics { get; set; }
    }

    public sealed class SaveFile
    {
        public int SchemaVersion { get; set; }
        public SaveHeader Header { get; set; }
        public WorldDto World { get; set; }
    }
}
