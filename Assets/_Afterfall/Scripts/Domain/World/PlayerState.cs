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

        /// <summary>Simulation time before which the player cannot attack/harvest again.</summary>
        public double NextActionTime { get; set; }

        public ItemStack EquippedStack => Inventory.GetSlot(SelectedHotbar);
    }
}
