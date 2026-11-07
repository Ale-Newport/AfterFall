using Afterfall.Domain.Items;

namespace Afterfall.Domain.Power
{
    public sealed class GeneratorState
    {
        public Inventory Fuel { get; set; }

        /// <summary>Joules remaining from the fuel unit currently burning.</summary>
        public double BufferJoules { get; set; }

        public bool Running { get; set; }

        /// <summary>Electrical output during the last tick, in watts.</summary>
        public float OutputWatts { get; set; }

        public string Status { get; set; } = "";
    }

    public sealed class BatteryState
    {
        public double StoredJoules { get; set; }

        /// <summary>Positive = charging, negative = discharging (watts, last tick).</summary>
        public float FlowWatts { get; set; }
    }

    /// <summary>Electrical state of a structure that participates in a power network.</summary>
    public sealed class PowerState
    {
        /// <summary>Network identifier assigned on the last topology rebuild (0 = isolated).</summary>
        public int NetworkId { get; set; }

        /// <summary>Watts requested this tick (consumers only).</summary>
        public float DemandWatts { get; set; }

        public bool Powered { get; set; }
        public string Reason { get; set; } = "";
    }
}
