using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.Power;

namespace Afterfall.Domain.Construction
{
    public enum StructureState
    {
        /// <summary>Planned: waiting for materials and/or construction work.</summary>
        Blueprint,

        Complete
    }

    /// <summary>
    /// Runtime state of a placed structure. The authoritative record lives in
    /// <see cref="World.WorldState"/>; GameObjects only visualise it.
    /// </summary>
    public sealed class Structure
    {
        public EntityId Id { get; set; }
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public StructureState State { get; set; }

        /// <summary>Materials already delivered to a blueprint (item id → quantity).</summary>
        public Dictionary<string, int> Delivered { get; } = new Dictionary<string, int>();

        public float WorkDone { get; set; }
        public float Health { get; set; }

        /// <summary>True for structures created by the player; false for world-generated props and loot.</summary>
        public bool PlayerBuilt { get; set; } = true;

        /// <summary>Point of interest this structure belongs to (world props).</summary>
        public string PoiId { get; set; } = "";

        /// <summary>Has the player opened/looted this container at least once.</summary>
        public bool Searched { get; set; }

        public bool DoorOpen { get; set; }

        /// <summary>Player toggle for lights and machines.</summary>
        public bool Enabled { get; set; } = true;

        // Functional components (null when the definition does not provide them).
        public Inventory Storage { get; set; }
        public MachineState Machine { get; set; }
        public GeneratorState Generator { get; set; }
        public BatteryState Battery { get; set; }

        /// <summary>Electrical state for any power-participating structure.</summary>
        public PowerState Power { get; set; }

        public bool IsComplete => State == StructureState.Complete;

        public override string ToString() => $"{DefId}{Id}";
    }
}
