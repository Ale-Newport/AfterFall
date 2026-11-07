using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.Power;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Construction
{
    /// <summary>Geometric facts about a candidate placement, measured by the presentation layer (physics).</summary>
    public sealed class PlacementProbe
    {
        public bool InsideMap { get; set; } = true;
        public bool HasGround { get; set; } = true;
        public float SlopeDegrees { get; set; }
        public bool InWater { get; set; }

        /// <summary>Open water within a few metres (required by pumps).</summary>
        public bool WaterNearby { get; set; }

        /// <summary>The piece rests on a foundation/floor (or snapped to a socket of one).</summary>
        public bool OnSupport { get; set; }

        public bool Blocked { get; set; }
        public string BlockedBy { get; set; } = "";
        public float Distance { get; set; }
        public float MaxDistance { get; set; } = 12f;
    }

    public sealed class PlacementResult
    {
        public bool Valid => Reasons.Count == 0;
        public List<string> Reasons { get; } = new List<string>();
        public List<ItemAmount> MissingMaterials { get; } = new List<ItemAmount>();
    }

    public static class PlacementRules
    {
        /// <summary>
        /// Evaluates every rule and reports all reasons a placement is invalid. Missing
        /// materials do not block placing a blueprint (materials are delivered later) but
        /// are reported so the UI can show them.
        /// </summary>
        public static PlacementResult Evaluate(BuildingDefinition def, PlacementProbe probe, TechnologyState tech, TechnologyService techService, Inventory builder)
        {
            var r = new PlacementResult();
            if (!def.Buildable) r.Reasons.Add($"{def.Name} cannot be built.");
            if (!tech.IsUnlocked(def.RequiredTech))
                r.Reasons.Add(techService != null ? techService.DescribeRequirements(def.RequiredTech) : $"Requires technology {def.RequiredTech}.");
            if (!probe.InsideMap) r.Reasons.Add("Outside the playable area.");
            if (probe.Distance > probe.MaxDistance) r.Reasons.Add($"Too far away ({probe.Distance:0} m > {probe.MaxDistance:0} m).");
            if (probe.InWater) r.Reasons.Add("Cannot build in water.");
            if (def.RequiresWater && !probe.WaterNearby) r.Reasons.Add("Must be placed at the edge of open water.");
            if (def.RequiresSupport && !probe.OnSupport) r.Reasons.Add("Must be placed on a foundation or floor.");
            if (!probe.OnSupport)
            {
                if (!probe.HasGround) r.Reasons.Add("Needs solid ground.");
                else if (probe.SlopeDegrees > def.MaxSlopeDegrees) r.Reasons.Add($"Ground too steep ({probe.SlopeDegrees:0}° > {def.MaxSlopeDegrees:0}°).");
            }
            if (probe.Blocked) r.Reasons.Add(string.IsNullOrEmpty(probe.BlockedBy) ? "Blocked by an obstacle." : $"Blocked by {probe.BlockedBy}.");
            if (builder != null) r.MissingMaterials.AddRange(builder.Missing(def.Cost));
            return r;
        }
    }

    /// <summary>Creates structures and their functional components.</summary>
    public static class StructureFactory
    {
        public static Structure Create(WorldState world, BuildingDefinition def, Vec3 position, float yaw, StructureState state)
        {
            var s = new Structure
            {
                Id = world.Ids.Allocate(),
                DefId = def.Id,
                Position = position,
                Yaw = yaw,
                State = state,
                Health = state == StructureState.Complete ? def.MaxHealth : def.MaxHealth * 0.25f
            };
            return s;
        }

        /// <summary>Instantiates inventories and power/machine state for a completed structure.</summary>
        public static void InitializeComponents(WorldState world, ContentDatabase content, Structure s)
        {
            InitializeComponents(content, s, role => world.Ids.Allocate());
        }

        /// <summary>
        /// Instantiates components using ids from <paramref name="idForRole"/> (roles: storage,
        /// input, output, fuel, generator_fuel). Loading passes the saved ids so references survive.
        /// </summary>
        public static void InitializeComponents(ContentDatabase content, Structure s, Func<string, EntityId> idForRole)
        {
            BuildingDefinition def = content.Building(s.DefId);
            if (def == null) return;
            ItemCatalog catalog = content.Items;

            if (def.Storage != null && s.Storage == null)
                s.Storage = new Inventory(idForRole("storage"), catalog, def.Storage.Slots, def.Storage.MaxWeight, null, def.Name);

            if (def.Machine != null && s.Machine == null)
            {
                var inputIds = new HashSet<string>();
                var outputIds = new HashSet<string>();
                List<Crafting.RecipeDefinition> recipes = content.RecipesForStation(def.Machine.Station);
                foreach (var r in recipes)
                {
                    foreach (ItemAmount a in r.Inputs) inputIds.Add(a.ItemId);
                    foreach (ItemAmount a in r.Outputs) outputIds.Add(a.ItemId);
                }
                s.Machine = new MachineState
                {
                    Input = new Inventory(idForRole("input"), catalog, def.Machine.InputSlots, 0f, InventoryFilter.Only(inputIds, $"{def.Name} inputs"), $"{def.Name} input"),
                    Output = new Inventory(idForRole("output"), catalog, def.Machine.OutputSlots, 0f, InventoryFilter.Only(outputIds, $"{def.Name} outputs"), $"{def.Name} output"),
                    Fuel = def.Machine.FuelSlots > 0
                        ? new Inventory(idForRole("fuel"), catalog, def.Machine.FuelSlots, 0f, InventoryFilter.Burnable(), $"{def.Name} fuel")
                        : null
                };
                if (recipes.Count == 1) s.Machine.RecipeId = recipes[0].Id;
            }

            if (def.Generator != null && s.Generator == null)
                s.Generator = new GeneratorState { Fuel = new Inventory(idForRole("generator_fuel"), catalog, def.Generator.FuelSlots, 0f, InventoryFilter.Energy(), $"{def.Name} fuel") };

            if (def.Battery != null && s.Battery == null) s.Battery = new BatteryState();
            if (def.IsPowerComponent && s.Power == null) s.Power = new PowerState();
        }
    }

    /// <summary>Blueprint → materials delivered → construction work → completed structure.</summary>
    public sealed class ConstructionService
    {
        public const float DemolishRefundFraction = 0.5f;
        public const float RepairCostFraction = 0.5f;

        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly TechnologyService _tech;

        public ConstructionService(WorldState world, ContentDatabase content, EventBus bus, TechnologyService tech)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _tech = tech;
        }

        public OpResult PlaceBlueprint(string defId, Vec3 position, float yaw, PlacementProbe probe, Inventory builder, out Structure structure)
        {
            structure = null;
            BuildingDefinition def = _content.Building(defId);
            if (def == null) return OpResult.Fail("unknown", $"Unknown building '{defId}'.");
            PlacementResult check = PlacementRules.Evaluate(def, probe ?? new PlacementProbe(), _world.Tech, _tech, builder);
            if (!check.Valid) return OpResult.Fail("invalid_placement", string.Join(" ", check.Reasons));

            structure = StructureFactory.Create(_world, def, position, yaw, StructureState.Blueprint);
            _world.AddStructure(structure);
            _bus?.Publish(new BuildingPlaced(structure.Id, def.Id));
            return OpResult.Success($"{def.Name} blueprint placed.");
        }

        public List<ItemAmount> RemainingCost(Structure s)
        {
            var remaining = new List<ItemAmount>();
            BuildingDefinition def = _content.Building(s.DefId);
            if (def == null || s.IsComplete) return remaining;
            foreach (KeyValuePair<string, int> kv in ItemAmount.Aggregate(def.Cost))
            {
                s.Delivered.TryGetValue(kv.Key, out int have);
                if (have < kv.Value) remaining.Add(new ItemAmount(kv.Key, kv.Value - have));
            }
            return remaining;
        }

        public bool AllMaterialsDelivered(Structure s) => RemainingCost(s).Count == 0;

        /// <summary>Moves still-needed materials from an inventory into the blueprint. Returns units delivered.</summary>
        public int DeliverMaterials(Structure s, Inventory from)
        {
            if (s.IsComplete || from == null) return 0;
            int delivered = 0;
            foreach (ItemAmount need in RemainingCost(s))
            {
                int amount = Math.Min(need.Quantity, from.Available(need.ItemId));
                if (amount <= 0 || !from.TryRemove(need.ItemId, amount)) continue;
                s.Delivered.TryGetValue(need.ItemId, out int have);
                s.Delivered[need.ItemId] = have + amount;
                delivered += amount;
            }
            return delivered;
        }

        /// <summary>
        /// Performs construction work: first delivers any missing materials the worker carries,
        /// then advances work if everything has been delivered. Completes the structure when done.
        /// </summary>
        public OpResult Work(Structure s, float seconds, Inventory workerInventory)
        {
            if (s == null) return OpResult.Fail("invalid", "Nothing to build.");
            if (s.IsComplete) return OpResult.Fail("complete", "Already complete.");
            BuildingDefinition def = _content.Building(s.DefId);
            DeliverMaterials(s, workerInventory);
            List<ItemAmount> remaining = RemainingCost(s);
            if (remaining.Count > 0)
            {
                var parts = new List<string>();
                foreach (ItemAmount a in remaining) parts.Add($"{a.Quantity} {_content.Items.NameOf(a.ItemId)}");
                return OpResult.Fail("missing_materials", "Needs " + string.Join(", ", parts) + ".");
            }
            s.WorkDone += Math.Max(0f, seconds);
            s.Health = Math.Max(s.Health, def.MaxHealth * (0.25f + 0.75f * Math.Min(1f, s.WorkDone / def.BuildSeconds)));
            if (s.WorkDone >= def.BuildSeconds) Complete(s);
            return OpResult.Success();
        }

        public float WorkProgress01(Structure s)
        {
            BuildingDefinition def = _content.Building(s.DefId);
            if (def == null) return 0f;
            return s.IsComplete ? 1f : Math.Min(1f, s.WorkDone / def.BuildSeconds);
        }

        public void Complete(Structure s)
        {
            BuildingDefinition def = _content.Building(s.DefId);
            s.State = StructureState.Complete;
            s.WorkDone = def.BuildSeconds;
            s.Health = def.MaxHealth;
            s.Delivered.Clear();
            StructureFactory.InitializeComponents(_world, _content, s);
            _world.RegisterStructureInventories(s);
            if (s.Power != null)
            {
                PowerLinker.AutoConnect(_world, _content, s);
                _world.MarkPowerTopologyChanged();
            }
            _world.MarkChanged();
            if (s.PlayerBuilt)
            {
                _world.Metrics.StructuresBuilt++;
                if (_world.Metrics.FirstConstructionAt < 0) _world.Metrics.FirstConstructionAt = _world.Clock.ElapsedSeconds;
            }
            _bus?.Publish(new BuildingCompleted(s.Id, s.DefId));
            _bus?.Publish(new Notification($"{def.Name} completed.", Severity.Success));
            _tech?.Record(UnlockTrigger.BuildStructure, s.DefId);
        }

        /// <summary>Cancels a blueprint, returning delivered materials (overflow is dropped on the ground, never lost).</summary>
        public OpResult CancelBlueprint(Structure s, Inventory refundTo)
        {
            if (s == null || s.IsComplete) return OpResult.Fail("invalid", "Only blueprints can be cancelled.");
            var refund = new List<ItemAmount>();
            foreach (KeyValuePair<string, int> kv in s.Delivered) refund.Add(new ItemAmount(kv.Key, kv.Value));
            GiveOrDrop(refund, refundTo, s.Position, "Returned materials");
            _world.RemoveStructure(s.Id);
            _bus?.Publish(new BuildingRemoved(s.Id, s.DefId, "cancelled"));
            return OpResult.Success("Blueprint cancelled.");
        }

        /// <summary>Removes a completed structure, refunding part of its cost and spilling its contents.</summary>
        public OpResult Demolish(Structure s, Inventory refundTo)
        {
            if (s == null) return OpResult.Fail("invalid", "Nothing to demolish.");
            if (!s.IsComplete) return CancelBlueprint(s, refundTo);
            if (!s.PlayerBuilt) return OpResult.Fail("not_owned", "You can only demolish your own structures.");
            BuildingDefinition def = _content.Building(s.DefId);
            var refund = new List<ItemAmount>();
            foreach (ItemAmount a in def.Cost)
            {
                int q = (int)Math.Floor(a.Quantity * DemolishRefundFraction);
                if (q > 0) refund.Add(new ItemAmount(a.ItemId, q));
            }
            SpillContents(s);
            GiveOrDrop(refund, refundTo, s.Position, "Salvaged materials");
            _world.RemoveStructure(s.Id);
            _bus?.Publish(new BuildingRemoved(s.Id, s.DefId, "demolished"));
            return OpResult.Success($"{def.Name} demolished.");
        }

        public List<ItemAmount> RepairCost(Structure s)
        {
            var cost = new List<ItemAmount>();
            BuildingDefinition def = _content.Building(s.DefId);
            if (def == null || !s.IsComplete || s.Health >= def.MaxHealth) return cost;
            float missing = 1f - s.Health / def.MaxHealth;
            foreach (ItemAmount a in def.Cost)
            {
                int q = (int)Math.Ceiling(a.Quantity * missing * RepairCostFraction);
                if (q > 0) cost.Add(new ItemAmount(a.ItemId, q));
            }
            return cost;
        }

        public OpResult Repair(Structure s, Inventory from)
        {
            BuildingDefinition def = _content.Building(s.DefId);
            if (def == null || !s.IsComplete) return OpResult.Fail("invalid", "Cannot repair this.");
            if (s.Health >= def.MaxHealth) return OpResult.Fail("full_health", $"{def.Name} is not damaged.");
            List<ItemAmount> cost = RepairCost(s);
            if (!from.TryConsume(cost))
            {
                var parts = new List<string>();
                foreach (ItemAmount m in from.Missing(cost)) parts.Add($"{m.Quantity} {_content.Items.NameOf(m.ItemId)}");
                return OpResult.Fail("missing_materials", "Repair needs " + string.Join(", ", parts) + ".");
            }
            s.Health = def.MaxHealth;
            return OpResult.Success($"{def.Name} repaired.");
        }

        /// <summary>Applies damage; destroyed structures spill their contents and are removed.</summary>
        public bool Damage(Structure s, float amount, string source)
        {
            if (s == null || amount <= 0f) return false;
            s.Health -= amount;
            if (s.Health > 0f) return false;
            BuildingDefinition def = _content.Building(s.DefId);
            SpillContents(s);
            _world.RemoveStructure(s.Id);
            _bus?.Publish(new BuildingRemoved(s.Id, s.DefId, "destroyed by " + source));
            _bus?.Publish(new Notification($"{def?.Name ?? s.DefId} was destroyed by {source}.", Severity.Danger));
            return true;
        }

        /// <summary>Moves every item held by a structure into a ground drop so nothing is lost.</summary>
        public void SpillContents(Structure s)
        {
            var stacks = new List<ItemStack>();
            CollectStacks(s.Storage, stacks);
            if (s.Machine != null)
            {
                CollectStacks(s.Machine.Input, stacks);
                CollectStacks(s.Machine.Output, stacks);
                CollectStacks(s.Machine.Fuel, stacks);
            }
            if (s.Generator != null) CollectStacks(s.Generator.Fuel, stacks);
            foreach (KeyValuePair<string, int> kv in s.Delivered)
                stacks.Add(new ItemStack(kv.Key, kv.Value));
            if (stacks.Count > 0) CreateDrop(stacks, s.Position, "Contents of " + (_content.Building(s.DefId)?.Name ?? s.DefId), false);
        }

        public WorldDrop CreateDrop(List<ItemStack> stacks, Vec3 position, string label, bool belongings)
        {
            int slots = Math.Max(4, stacks.Count + 2);
            var inv = new Inventory(_world.Ids.Allocate(), _content.Items, slots, 0f, null, label);
            foreach (ItemStack st in stacks)
            {
                if (!inv.TryAddStack(st))
                {
                    // Cannot happen with one slot per stack + spare; guard against silent loss anyway.
                    throw new InvalidOperationException("Drop overflow while spilling contents.");
                }
            }
            var drop = new WorldDrop { Id = _world.Ids.Allocate(), Position = position, Contents = inv, Label = label, IsBelongings = belongings };
            _world.AddDrop(drop);
            return drop;
        }

        private void GiveOrDrop(List<ItemAmount> items, Inventory to, Vec3 position, string label)
        {
            var overflow = new List<ItemStack>();
            foreach (ItemAmount a in items)
            {
                int added = to != null ? to.AddUpTo(a.ItemId, a.Quantity) : 0;
                if (added < a.Quantity) overflow.Add(new ItemStack(a.ItemId, a.Quantity - added));
            }
            if (overflow.Count > 0) CreateDrop(SplitToStacks(overflow), position, label, false);
        }

        private List<ItemStack> SplitToStacks(List<ItemStack> raw)
        {
            var result = new List<ItemStack>();
            foreach (ItemStack s in raw)
            {
                ItemDefinition def = _content.Items.Get(s.ItemId);
                int max = def.HasDurability ? 1 : Math.Max(1, def.MaxStack);
                int remaining = s.Quantity;
                while (remaining > 0)
                {
                    int q = Math.Min(max, remaining);
                    result.Add(new ItemStack(s.ItemId, q, s.Durability));
                    remaining -= q;
                }
            }
            return result;
        }

        private static void CollectStacks(Inventory inv, List<ItemStack> into)
        {
            if (inv == null) return;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                ItemStack st = inv.GetSlot(i);
                if (!st.IsEmpty) into.Add(st);
            }
            inv.ClearAll();
        }
    }

    /// <summary>Rules for creating cables between power structures.</summary>
    public static class PowerLinker
    {
        public const int MaxLinksPerStructure = 8;
        public const float DefaultRange = 5f;

        public static float RangeOf(BuildingDefinition def) => def?.PowerNode?.Range ?? DefaultRange;

        public static bool CanLink(WorldState world, ContentDatabase content, Structure a, Structure b, out string reason)
        {
            reason = "";
            if (a == null || b == null || a.Id == b.Id) { reason = "Select two different structures."; return false; }
            if (a.Power == null || b.Power == null) { reason = "Both structures must be electrical."; return false; }
            if (!a.IsComplete || !b.IsComplete) { reason = "Finish construction first."; return false; }
            if (world.HasPowerLink(a.Id, b.Id)) { reason = "Already connected."; return false; }
            float range = Math.Max(RangeOf(content.Building(a.DefId)), RangeOf(content.Building(b.DefId)));
            float d = Vec3.Distance(a.Position, b.Position);
            if (d > range) { reason = $"Too far for a cable ({d:0.0} m > {range:0} m). Place a power pole in between."; return false; }
            if (LinkCount(world, a.Id) >= MaxLinksPerStructure || LinkCount(world, b.Id) >= MaxLinksPerStructure) { reason = "Too many cables on one structure."; return false; }
            return true;
        }

        public static OpResult Link(WorldState world, ContentDatabase content, Structure a, Structure b)
        {
            if (!CanLink(world, content, a, b, out string reason)) return OpResult.Fail("cannot_link", reason);
            world.PowerLinks.Add(new PowerLink(a.Id, b.Id));
            world.MarkPowerTopologyChanged();
            return OpResult.Success("Cable connected.");
        }

        public static OpResult Unlink(WorldState world, EntityId a, EntityId b)
        {
            var key = new PowerLink(a, b);
            int removed = world.PowerLinks.RemoveAll(l => l.A == key.A && l.B == key.B);
            if (removed == 0) return OpResult.Fail("no_link", "Those structures are not connected.");
            world.MarkPowerTopologyChanged();
            return OpResult.Success("Cable removed.");
        }

        /// <summary>
        /// Relays (poles) connect to every electrical structure in range; other structures
        /// connect to the nearest electrical structure in range. Returns cables added.
        /// </summary>
        public static int AutoConnect(WorldState world, ContentDatabase content, Structure s)
        {
            if (s.Power == null) return 0;
            BuildingDefinition def = content.Building(s.DefId);
            bool relay = def?.PowerNode != null && def.PowerNode.IsRelay;
            int added = 0;
            Structure nearest = null;
            float nearestDist = float.MaxValue;
            foreach (EntityId id in world.SortedStructureIds())
            {
                Structure other = world.Structures[id];
                if (other.Id == s.Id || other.Power == null || !other.IsComplete) continue;
                if (!CanLink(world, content, s, other, out _)) continue;
                if (relay)
                {
                    world.PowerLinks.Add(new PowerLink(s.Id, other.Id));
                    added++;
                    continue;
                }
                float d = Vec3.Distance(s.Position, other.Position);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = other;
                }
            }
            if (!relay && nearest != null)
            {
                world.PowerLinks.Add(new PowerLink(s.Id, nearest.Id));
                added++;
            }
            if (added > 0) world.MarkPowerTopologyChanged();
            return added;
        }

        public static int LinkCount(WorldState world, EntityId id)
        {
            int n = 0;
            foreach (PowerLink l in world.PowerLinks)
                if (l.Involves(id)) n++;
            return n;
        }
    }
}
