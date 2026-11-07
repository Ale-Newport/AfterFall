using Afterfall.Core;

namespace Afterfall.Domain.Events
{
    public enum Severity
    {
        Info,
        Success,
        Warning,
        Danger
    }

    /// <summary>Player-facing message (toast). Domain systems use this to explain outcomes.</summary>
    public sealed class Notification : IGameEvent
    {
        public Notification(string message, Severity severity = Severity.Info)
        {
            Message = message;
            Severity = severity;
        }

        public string Message { get; }
        public Severity Severity { get; }
        public override string ToString() => $"Notification[{Severity}] {Message}";
    }

    public sealed class ItemsGathered : IGameEvent
    {
        public ItemsGathered(EntityId nodeId, string itemId, int quantity)
        {
            NodeId = nodeId;
            ItemId = itemId;
            Quantity = quantity;
        }

        public EntityId NodeId { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public override string ToString() => $"ItemsGathered {Quantity}x{ItemId} from {NodeId}";
    }

    public sealed class ItemTransferred : IGameEvent
    {
        public ItemTransferred(EntityId from, EntityId to, string itemId, int quantity)
        {
            From = from;
            To = to;
            ItemId = itemId;
            Quantity = quantity;
        }

        public EntityId From { get; }
        public EntityId To { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public override string ToString() => $"ItemTransferred {Quantity}x{ItemId} {From}->{To}";
    }

    public sealed class RecipeCompleted : IGameEvent
    {
        public RecipeCompleted(EntityId producer, string recipeId)
        {
            Producer = producer;
            RecipeId = recipeId;
        }

        /// <summary>Machine structure id, or EntityId.None for the player's hand crafting.</summary>
        public EntityId Producer { get; }
        public string RecipeId { get; }
        public override string ToString() => $"RecipeCompleted {RecipeId} by {Producer}";
    }

    public sealed class BuildingPlaced : IGameEvent
    {
        public BuildingPlaced(EntityId structureId, string defId)
        {
            StructureId = structureId;
            DefId = defId;
        }

        public EntityId StructureId { get; }
        public string DefId { get; }
        public override string ToString() => $"BuildingPlaced {DefId} {StructureId}";
    }

    public sealed class BuildingCompleted : IGameEvent
    {
        public BuildingCompleted(EntityId structureId, string defId)
        {
            StructureId = structureId;
            DefId = defId;
        }

        public EntityId StructureId { get; }
        public string DefId { get; }
        public override string ToString() => $"BuildingCompleted {DefId} {StructureId}";
    }

    public sealed class BuildingRemoved : IGameEvent
    {
        public BuildingRemoved(EntityId structureId, string defId, string reason)
        {
            StructureId = structureId;
            DefId = defId;
            Reason = reason;
        }

        public EntityId StructureId { get; }
        public string DefId { get; }
        public string Reason { get; }
        public override string ToString() => $"BuildingRemoved {DefId} {StructureId} ({Reason})";
    }

    public sealed class PowerNetworkChanged : IGameEvent
    {
        public PowerNetworkChanged(int networkCount)
        {
            NetworkCount = networkCount;
        }

        public int NetworkCount { get; }
        public override string ToString() => $"PowerNetworkChanged networks={NetworkCount}";
    }

    public sealed class MachineStatusChanged : IGameEvent
    {
        public MachineStatusChanged(EntityId structureId, string oldStatus, string newStatus, string detail)
        {
            StructureId = structureId;
            OldStatus = oldStatus;
            NewStatus = newStatus;
            Detail = detail;
        }

        public EntityId StructureId { get; }
        public string OldStatus { get; }
        public string NewStatus { get; }
        public string Detail { get; }
        public override string ToString() => $"MachineStatusChanged {StructureId} {OldStatus}->{NewStatus} ({Detail})";
    }

    public sealed class TechnologyUnlocked : IGameEvent
    {
        public TechnologyUnlocked(string techId, string name)
        {
            TechId = techId;
            Name = name;
        }

        public string TechId { get; }
        public string Name { get; }
        public override string ToString() => $"TechnologyUnlocked {TechId}";
    }

    public sealed class PlayerDamaged : IGameEvent
    {
        public PlayerDamaged(float amount, string source)
        {
            Amount = amount;
            Source = source;
        }

        public float Amount { get; }
        public string Source { get; }
        public override string ToString() => $"PlayerDamaged {Amount:0.#} by {Source}";
    }

    public sealed class PlayerDied : IGameEvent
    {
        public PlayerDied(Vec3 position, string cause, EntityId belongingsDropId)
        {
            Position = position;
            Cause = cause;
            BelongingsDropId = belongingsDropId;
        }

        public Vec3 Position { get; }
        public string Cause { get; }
        public EntityId BelongingsDropId { get; }
        public override string ToString() => $"PlayerDied at {Position} ({Cause})";
    }

    public sealed class PlayerRespawned : IGameEvent
    {
        public PlayerRespawned(Vec3 position)
        {
            Position = position;
        }

        public Vec3 Position { get; }
        public override string ToString() => $"PlayerRespawned at {Position}";
    }

    public sealed class ZombieSpawned : IGameEvent
    {
        public ZombieSpawned(EntityId zombieId)
        {
            ZombieId = zombieId;
        }

        public EntityId ZombieId { get; }
        public override string ToString() => $"ZombieSpawned {ZombieId}";
    }

    public sealed class ZombieKilled : IGameEvent
    {
        public ZombieKilled(EntityId zombieId, Vec3 position)
        {
            ZombieId = zombieId;
            Position = position;
        }

        public EntityId ZombieId { get; }
        public Vec3 Position { get; }
        public override string ToString() => $"ZombieKilled {ZombieId}";
    }

    public sealed class ThreatWarningIssued : IGameEvent
    {
        public ThreatWarningIssued(string message)
        {
            Message = message;
        }

        public string Message { get; }
        public override string ToString() => $"ThreatWarning {Message}";
    }

    public sealed class PoiDiscovered : IGameEvent
    {
        public PoiDiscovered(string poiId, string name)
        {
            PoiId = poiId;
            Name = name;
        }

        public string PoiId { get; }
        public string Name { get; }
        public override string ToString() => $"PoiDiscovered {PoiId}";
    }

    public sealed class SaveCompleted : IGameEvent
    {
        public SaveCompleted(string slot, bool success, string message)
        {
            Slot = slot;
            Success = success;
            Message = message;
        }

        public string Slot { get; }
        public bool Success { get; }
        public string Message { get; }
        public override string ToString() => $"SaveCompleted {Slot} ok={Success} {Message}";
    }
}
