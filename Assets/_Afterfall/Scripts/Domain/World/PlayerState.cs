using Afterfall.Core;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;

namespace Afterfall.Domain.World
{
    /// <summary>Transient per-frame activity reported by the presentation layer.</summary>
    public struct PlayerActivity
    {
        public bool Moving;
        public bool Sprinting;
        public bool Crouching;

        /// <summary>Driving: no footsteps (the engine makes its own noise).</summary>
        public bool Driving;

        /// <summary>Free flight (creative/debug camera): no footstep noise, no map discovery, and the infected cannot perceive the survivor.</summary>
        public bool Flying;
    }

    public sealed class PlayerState
    {
        public const int HotbarSize = 8;
        public const int BackpackSlots = 24;
        public const float CarryComfortKg = 35f;
        public const float CarryMaxKg = 55f;

        /// <summary>Stable id of the player entity (owner of the backpack inventory).</summary>
        public EntityId Id { get; set; }

        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public SurvivalState Vitals { get; set; } = new SurvivalState();
        public Inventory Inventory { get; set; }
        public CraftingQueue Crafting { get; set; } = new CraftingQueue();

        /// <summary>Selected hotbar slot (0-7); hotbar slots are the first slots of the inventory.</summary>
        public int SelectedHotbar { get; set; }

        /// <summary>Bed (or other shelter) used as the respawn point; None = world spawn.</summary>
        public EntityId SpawnStructureId { get; set; }

        public Vec3 WorldSpawn { get; set; }
        public int Deaths { get; set; }
        public PlayerActivity Activity;

        /// <summary>The vehicle the survivor is driving (None on foot).</summary>
        public EntityId VehicleId { get; set; }

        public bool InVehicle => VehicleId != EntityId.None;

        /// <summary>Simulation time before which the player cannot attack/harvest again.</summary>
        public double NextActionTime { get; set; }

        /// <summary>Temperature the survivor feels where they stand (derived each tick; not saved).</summary>
        public float FeltTemperatureC { get; set; } = 15f;

        /// <summary>Under a roof or inside a building (derived each tick; not saved).</summary>
        public bool Sheltered { get; set; }

        public ItemStack EquippedStack => Inventory.GetSlot(SelectedHotbar);
    }
}
