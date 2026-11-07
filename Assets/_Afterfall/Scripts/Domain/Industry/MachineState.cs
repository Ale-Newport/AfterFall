using Afterfall.Domain.Items;

namespace Afterfall.Domain.Industry
{
    /// <summary>Explicit machine states. Every non-working state has a player-facing reason.</summary>
    public enum MachineStatus
    {
        Idle,
        Working,
        WaitingForInput,
        OutputFull,
        MissingPower,
        MissingFuel,
        Damaged,
        RecipeNotConfigured,
        Disabled,
        Unbuilt
    }

    public sealed class MachineState
    {
        public string RecipeId { get; set; } = "";
        public Inventory Input { get; set; }
        public Inventory Output { get; set; }

        /// <summary>Burnable fuel inventory for combustion machines; null otherwise.</summary>
        public Inventory Fuel { get; set; }

        /// <summary>Seconds of progress in the current cycle.</summary>
        public float Progress { get; set; }

        /// <summary>Inputs for the current cycle have been consumed and outputs are pending.</summary>
        public bool CycleActive { get; set; }

        /// <summary>Remaining burn time of the fuel item currently burning.</summary>
        public float FuelBurnRemaining { get; set; }

        public MachineStatus Status { get; set; } = MachineStatus.Idle;
        public string StatusDetail { get; set; } = "";

        /// <summary>The machine wants electricity next tick (set by MachineSystem, read by PowerSystem).</summary>
        public bool RequestingPower { get; set; }

        public int CompletedCycles { get; set; }

        /// <summary>Simulation time at which the last cycle completed (for production-rate display).</summary>
        public double LastCycleCompletedAt { get; set; } = -1;
    }
}
