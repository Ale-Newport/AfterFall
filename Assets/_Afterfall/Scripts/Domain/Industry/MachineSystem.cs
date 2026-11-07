using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Industry
{
    /// <summary>
    /// Advances every machine in the world, rendered or not. Machines consume real inputs
    /// from their input inventory, burn real fuel, require real electricity and deliver real
    /// outputs; whenever a machine is not working its status explains why.
    /// </summary>
    public sealed class MachineSystem : ISimulationSystem
    {
        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly TechnologyService _tech;

        public MachineSystem(WorldState world, ContentDatabase content, EventBus bus, TechnologyService tech)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _tech = tech;
        }

        public string Name => "Machines";

        public void Tick(double dt)
        {
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (s.Machine == null) continue;
                BuildingDefinition def = _content.Building(s.DefId);
                if (def?.Machine == null) continue;
                MachineStatus old = s.Machine.Status;
                Step(s, def, (float)dt);
                if (old != s.Machine.Status)
                    _bus?.Publish(new MachineStatusChanged(s.Id, old.ToString(), s.Machine.Status.ToString(), s.Machine.StatusDetail));
            }
        }

        public OpResult SetRecipe(Structure s, string recipeId)
        {
            BuildingDefinition def = _content.Building(s.DefId);
            if (s.Machine == null || def?.Machine == null) return OpResult.Fail("not_machine", "This is not a machine.");
            if (string.IsNullOrEmpty(recipeId))
            {
                s.Machine.RecipeId = "";
                return OpResult.Success("Recipe cleared.");
            }
            RecipeDefinition r = _content.Recipe(recipeId);
            if (r == null || r.Station != def.Machine.Station) return OpResult.Fail("wrong_station", $"{def.Name} cannot run that recipe.");
            if (!_world.Tech.IsUnlocked(r.RequiredTech)) return OpResult.Fail("locked", _tech != null ? _tech.DescribeRequirements(r.RequiredTech) : "Recipe is locked.");
            if (s.Machine.CycleActive && s.Machine.RecipeId != recipeId)
                return OpResult.Fail("busy", "Wait for the current cycle to finish before changing the recipe.");
            s.Machine.RecipeId = recipeId;
            return OpResult.Success($"{def.Name} set to {r.Name}.");
        }

        private void Step(Structure s, BuildingDefinition def, float dt)
        {
            MachineState m = s.Machine;
            MachineSpec spec = def.Machine;
            bool electric = spec.PowerWatts > 0f;
            m.RequestingPower = false;

            if (!s.IsComplete)
            {
                SetStatus(m, MachineStatus.Unbuilt, "Under construction");
                return;
            }
            if (s.Health <= 0f)
            {
                SetStatus(m, MachineStatus.Damaged, "Damaged — repair it with a hammer");
                return;
            }
            if (!s.Enabled)
            {
                SetStatus(m, MachineStatus.Disabled, "Switched off");
                return;
            }
            RecipeDefinition recipe = _content.Recipe(m.RecipeId);
            if (recipe == null)
            {
                SetStatus(m, MachineStatus.RecipeNotConfigured, "Choose a recipe");
                return;
            }
            if (!_world.Tech.IsUnlocked(recipe.RequiredTech))
            {
                SetStatus(m, MachineStatus.RecipeNotConfigured, "Recipe locked: " + recipe.RequiredTech);
                return;
            }

            if (spec.AutoTransferRange > 0f) AutoTransfer(s, def, recipe);

            if (!m.CycleActive)
            {
                if (!m.Output.CanAcceptAll(recipe.Outputs))
                {
                    SetStatus(m, MachineStatus.OutputFull, "Output full — collect the products");
                    return;
                }
                List<ItemAmount> missing = m.Input.Missing(recipe.Inputs);
                if (missing.Count > 0)
                {
                    SetStatus(m, MachineStatus.WaitingForInput, "Needs " + Describe(missing));
                    return;
                }
                if (spec.FuelSlots > 0 && m.FuelBurnRemaining <= 0f && !HasBurnable(m.Fuel))
                {
                    SetStatus(m, MachineStatus.MissingFuel, "Needs fuel (wood or charcoal)");
                    return;
                }
                if (electric)
                {
                    m.RequestingPower = true;
                    if (!s.Power.Powered)
                    {
                        // The power system allocates on its next tick; a freshly ready machine is "starting".
                        if (IsUnallocated(s.Power.Reason)) SetStatus(m, MachineStatus.Idle, "Starting…");
                        else SetStatus(m, MachineStatus.MissingPower, s.Power.Reason);
                        return;
                    }
                }
                if (!m.Input.TryConsume(recipe.Inputs))
                {
                    SetStatus(m, MachineStatus.WaitingForInput, "Inputs unavailable");
                    return;
                }
                m.CycleActive = true;
                m.Progress = 0f;
            }

            // Cycle in progress: inputs already consumed and held "inside" the machine.
            if (electric)
            {
                m.RequestingPower = true;
                if (!s.Power.Powered)
                {
                    SetStatus(m, MachineStatus.MissingPower, IsUnallocated(s.Power.Reason) ? "Waiting for power" : s.Power.Reason);
                    return;
                }
            }
            if (spec.FuelSlots > 0)
            {
                if (m.FuelBurnRemaining <= 0f && !TryIgnite(m))
                {
                    SetStatus(m, MachineStatus.MissingFuel, "Out of fuel — production paused");
                    return;
                }
                m.FuelBurnRemaining = Math.Max(0f, m.FuelBurnRemaining - dt);
            }

            m.Progress += dt;
            SetStatus(m, MachineStatus.Working, $"{recipe.Name} {Math.Min(100, (int)(m.Progress / recipe.Duration * 100))}%");
            if (m.Progress < recipe.Duration) return;

            if (!m.Output.TryAddAll(recipe.Outputs))
            {
                m.Progress = recipe.Duration;
                SetStatus(m, MachineStatus.OutputFull, "Output full — collect the products");
                return;
            }
            m.CycleActive = false;
            m.Progress = 0f;
            m.CompletedCycles++;
            m.LastCycleCompletedAt = _world.Clock.ElapsedSeconds;
            if (_world.Metrics.FirstMachineOutputAt < 0) _world.Metrics.FirstMachineOutputAt = _world.Clock.ElapsedSeconds;
            if (electric && _world.Metrics.FirstPoweredOutputAt < 0) _world.Metrics.FirstPoweredOutputAt = _world.Clock.ElapsedSeconds;
            _bus?.Publish(new RecipeCompleted(s.Id, recipe.Id));
            foreach (ItemAmount o in recipe.Outputs) _tech?.Record(UnlockTrigger.CraftItem, o.ItemId, o.Quantity);
        }

        /// <summary>
        /// Level-2 automation: pulls inputs/fuel from adjacent player storage and pushes
        /// finished products into it. Items physically move between real inventories.
        /// </summary>
        private void AutoTransfer(Structure s, BuildingDefinition def, RecipeDefinition recipe)
        {
            List<Structure> storages = AdjacentStorages(s, def.Machine.AutoTransferRange);
            if (storages.Count == 0) return;
            MachineState m = s.Machine;

            for (int i = 0; i < m.Output.SlotCount; i++)
            {
                ItemStack st = m.Output.GetSlot(i);
                if (st.IsEmpty) continue;
                foreach (Structure target in storages)
                    if (InventoryTransfers.TryTransferSlot(m.Output, i, target.Storage, st.Quantity).Ok && m.Output.GetSlot(i).IsEmpty) break;
            }

            foreach (ItemAmount need in recipe.Inputs)
            {
                int want = need.Quantity * 2 - m.Input.Count(need.ItemId);
                foreach (Structure source in storages)
                {
                    if (want <= 0) break;
                    int amount = Math.Min(want, Math.Min(source.Storage.Available(need.ItemId), m.Input.SpaceFor(need.ItemId)));
                    if (amount > 0 && InventoryTransfers.TryTransfer(source.Storage, m.Input, need.ItemId, amount).Ok) want -= amount;
                }
            }

            if (m.Fuel != null && m.Fuel.IsEmpty)
            {
                foreach (Structure source in storages)
                {
                    string fuelId = BestFuel(source.Storage);
                    if (fuelId == null) continue;
                    int amount = Math.Min(5, Math.Min(source.Storage.Available(fuelId), m.Fuel.SpaceFor(fuelId)));
                    if (amount > 0 && InventoryTransfers.TryTransfer(source.Storage, m.Fuel, fuelId, amount).Ok) break;
                }
            }
        }

        private List<Structure> AdjacentStorages(Structure s, float range)
        {
            var list = new List<Structure>();
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure o = _world.Structures[id];
                if (o.Storage == null || !o.IsComplete || !o.PlayerBuilt || o.Id == s.Id) continue;
                if (Vec3.Distance(o.Position, s.Position) <= range) list.Add(o);
            }
            list.Sort((a, b) => Vec3.Distance(a.Position, s.Position).CompareTo(Vec3.Distance(b.Position, s.Position)));
            return list;
        }

        private string BestFuel(Inventory inv)
        {
            string best = null;
            float bestBurn = 0f;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                ItemStack st = inv.GetSlot(i);
                if (st.IsEmpty) continue;
                ItemDefinition d = _content.Items.Get(st.ItemId);
                if (d.BurnSeconds > bestBurn && inv.Available(d.Id) > 0)
                {
                    bestBurn = d.BurnSeconds;
                    best = d.Id;
                }
            }
            return best;
        }

        private bool HasBurnable(Inventory fuel)
        {
            if (fuel == null) return false;
            for (int i = 0; i < fuel.SlotCount; i++)
            {
                ItemStack st = fuel.GetSlot(i);
                if (!st.IsEmpty && _content.Items.Get(st.ItemId).BurnSeconds > 0f) return true;
            }
            return false;
        }

        private bool TryIgnite(MachineState m)
        {
            if (m.Fuel == null) return false;
            for (int i = 0; i < m.Fuel.SlotCount; i++)
            {
                ItemStack st = m.Fuel.GetSlot(i);
                if (st.IsEmpty) continue;
                ItemDefinition d = _content.Items.Get(st.ItemId);
                if (d.BurnSeconds <= 0f) continue;
                if (m.Fuel.TakeFromSlot(i, 1).IsEmpty) continue;
                m.FuelBurnRemaining += d.BurnSeconds;
                return true;
            }
            return false;
        }

        private string Describe(List<ItemAmount> items)
        {
            var parts = new List<string>();
            foreach (ItemAmount a in items) parts.Add($"{a.Quantity} {_content.Items.NameOf(a.ItemId)}");
            return string.Join(", ", parts);
        }

        private static bool IsUnallocated(string reason) => string.IsNullOrEmpty(reason) || reason == "Not requested";

        private static void SetStatus(MachineState m, MachineStatus status, string detail)
        {
            m.Status = status;
            m.StatusDetail = detail;
        }
    }
}
