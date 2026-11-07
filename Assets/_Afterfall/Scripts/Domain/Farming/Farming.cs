using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Farming
{
    public sealed class CropState
    {
        /// <summary>Seed item planted ("" = empty plot).</summary>
        public string SeedId { get; set; } = "";

        /// <summary>Growth 0–1; harvestable at 1.</summary>
        public float Growth { get; set; }

        /// <summary>Soil water 0–100. Crops only grow while above 0.</summary>
        public float Water { get; set; }

        public bool Irrigated { get; set; }

        public bool Planted => !string.IsNullOrEmpty(SeedId);
        public bool Ready => Planted && Growth >= 1f;
    }

    /// <summary>
    /// Crop growth (1 Hz) plus player/colonist farming actions. Crops grow only while watered;
    /// an enabled electric water pump on a grid with available power irrigates every plot in range.
    /// </summary>
    public sealed class FarmingSystem : ISimulationSystem
    {
        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly TechnologyService _tech;
        private readonly DeterministicRandom _rng;

        public FarmingSystem(WorldState world, ContentDatabase content, EventBus bus, TechnologyService tech)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _tech = tech;
            _rng = new DeterministicRandom((ulong)(uint)world.Seed * 977UL + 13UL);
        }

        public string Name => "Farming";

        /// <summary>Whether a pump's grid can power it (set by the session from the power system).</summary>
        public Func<Structure, bool> PumpHasPower { get; set; } = s => s.Power != null && s.Power.Powered;

        public void Tick(double dt)
        {
            List<Structure> pumps = null;
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (s.Crop == null || !s.IsComplete) continue;
                FarmSpec spec = _content.Building(s.DefId)?.Farm;
                if (spec == null) continue;
                if (pumps == null) pumps = RunningPumps();
                s.Crop.Irrigated = IsIrrigated(s, spec, pumps);
                if (s.Crop.Irrigated) s.Crop.Water = 100f;
                if (!s.Crop.Planted || s.Crop.Ready) continue;
                if (s.Crop.Water <= 0f) continue;
                s.Crop.Water = Math.Max(0f, s.Crop.Water - spec.WaterPerSecond * (float)dt);
                bool wasReady = s.Crop.Ready;
                s.Crop.Growth = Math.Min(1f, s.Crop.Growth + (float)(dt / spec.GrowSeconds));
                if (!wasReady && s.Crop.Ready) _bus?.Publish(new Notification($"{_content.Items.NameOf(spec.CropItem)} ready to harvest.", Severity.Success));
            }
        }

        private List<Structure> RunningPumps()
        {
            var list = new List<Structure>();
            foreach (Structure s in _world.Structures.Values)
                if (s.Machine != null && s.IsComplete && s.Enabled && s.Health > 0f && _content.Building(s.DefId)?.Machine?.Station == "water_pump" && PumpHasPower(s))
                    list.Add(s);
            return list;
        }

        private static bool IsIrrigated(Structure plot, FarmSpec spec, List<Structure> pumps)
        {
            foreach (Structure p in pumps)
                if (Vec3.DistanceXZ(p.Position, plot.Position) <= spec.IrrigationRange) return true;
            return false;
        }

        public OpResult Plant(Structure plot, Inventory from)
        {
            FarmSpec spec = Spec(plot, out OpResult error);
            if (spec == null) return error;
            if (plot.Crop.Planted) return OpResult.Fail("occupied", "Something is already growing here.");
            if (!from.TryRemove(spec.SeedItem, 1)) return OpResult.Fail("no_seed", $"You need {_content.Items.NameOf(spec.SeedItem)} to plant.");
            plot.Crop.SeedId = spec.SeedItem;
            plot.Crop.Growth = 0f;
            return OpResult.Success($"Planted {_content.Items.NameOf(spec.SeedItem)}." + (plot.Crop.Water <= 0f && !plot.Crop.Irrigated ? " It needs water to grow." : ""));
        }

        /// <summary>Waters a plot with a bottle of water (the empty bottle is returned).</summary>
        public OpResult Water(Structure plot, Inventory from)
        {
            FarmSpec spec = Spec(plot, out OpResult error);
            if (spec == null) return error;
            if (plot.Crop.Water >= 90f) return OpResult.Fail("wet", "The soil is already wet.");
            if (from.Available("water_bottle") <= 0) return OpResult.Fail("no_water", "You need a water bottle (fill bottles at a water pump).");
            if (!from.TryRemove("water_bottle", 1)) return OpResult.Fail("no_water", "Water bottle unavailable.");
            from.AddUpTo("empty_bottle", 1);
            plot.Crop.Water = 100f;
            return OpResult.Success("Watered the planter bed.");
        }

        /// <summary>Harvests a ripe plot atomically: crop plus some seed back, or nothing if it does not fit.</summary>
        public OpResult Harvest(Structure plot, Inventory into)
        {
            FarmSpec spec = Spec(plot, out OpResult error);
            if (spec == null) return error;
            if (!plot.Crop.Planted) return OpResult.Fail("empty", "Nothing is planted.");
            if (!plot.Crop.Ready) return OpResult.Fail("not_ready", $"Still growing ({plot.Crop.Growth * 100:0}%).");
            var yield = new List<ItemAmount> { new ItemAmount(spec.CropItem, _rng.Range(spec.YieldMin, spec.YieldMax + 1)) };
            int seeds = _rng.Range(spec.SeedReturnMin, spec.SeedReturnMax + 1);
            if (seeds > 0) yield.Add(new ItemAmount(spec.SeedItem, seeds));
            if (!into.TryAddAll(yield)) return OpResult.Fail("inventory_full", "Not enough room for the harvest.");
            plot.Crop.SeedId = "";
            plot.Crop.Growth = 0f;
            foreach (ItemAmount a in yield) _tech?.Record(UnlockTrigger.CraftItem, a.ItemId, a.Quantity);
            return OpResult.Success($"Harvested {yield[0].Quantity} {_content.Items.NameOf(spec.CropItem)}" + (seeds > 0 ? $" and {seeds} seed(s)." : "."));
        }

        private FarmSpec Spec(Structure plot, out OpResult error)
        {
            error = default;
            FarmSpec spec = plot != null && plot.IsComplete ? _content.Building(plot.DefId)?.Farm : null;
            if (spec == null || plot.Crop == null)
            {
                error = OpResult.Fail("not_farm", "This is not a finished planter bed.");
                return null;
            }
            return spec;
        }
    }
}
