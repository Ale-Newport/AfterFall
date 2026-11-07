using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Colony
{
    public sealed class ColonistRecruited : IGameEvent
    {
        public ColonistRecruited(EntityId id, string name)
        {
            ColonistId = id;
            Name = name;
        }

        public EntityId ColonistId { get; }
        public string Name { get; }
        public override string ToString() => $"ColonistRecruited {Name} {ColonistId}";
    }

    public sealed class ColonistJobFailed : IGameEvent
    {
        public ColonistJobFailed(EntityId colonist, EntityId job, string reason)
        {
            ColonistId = colonist;
            JobId = job;
            Reason = reason;
        }

        public EntityId ColonistId { get; }
        public EntityId JobId { get; }
        public string Reason { get; }
        public override string ToString() => $"ColonistJobFailed {ColonistId} job {JobId}: {Reason}";
    }

    /// <summary>
    /// Colony simulation: plans logistics and construction jobs from the state of the base,
    /// assigns them to recruited colonists (priority → personal preference → skill → distance),
    /// and executes them physically (walk, pick up, carry, deliver, build, eat, rest).
    /// Every haul is backed by inventory reservations; failures release them, keep the items,
    /// and record a reason the player can review.
    /// </summary>
    public sealed class ColonySystem : ISimulationSystem
    {
        public const float WalkSpeed = 3.2f;
        public const float ArriveDistance = 1.8f;
        public const int CarrySlots = 6;
        public const float CarryKg = 30f;
        public const int MaxHaulQuantity = 20;
        public const double PlanInterval = 2.0;
        public const float NeedThreshold = 35f;
        public const float FleeRadius = 9f;

        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly ConstructionService _construction;
        private Farming.FarmingSystem _farming;
        private readonly Dictionary<EntityId, double> _nextAssign = new Dictionary<EntityId, double>();
        private double _nextPlan;

        public ColonySystem(WorldState world, ContentDatabase content, EventBus bus, ConstructionService construction)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _construction = construction;
        }

        public string Name => "Colony";

        /// <summary>Enables Farm jobs (planting, watering, harvesting planter beds).</summary>
        public Farming.FarmingSystem Farming
        {
            get => _farming;
            set => _farming = value;
        }
        public IColonistMovement Movement { get; set; } = new DirectColonistMovement();

        // ================================================================== recruitment

        public ColonistState CreateSurvivor(string name, Vec3 position, Dictionary<JobType, float> skills)
        {
            var c = new ColonistState { Id = _world.Ids.Allocate(), Name = name, Position = position, Status = "Waiting to be found" };
            c.Carry = new Inventory(_world.Ids.Allocate(), _content.Items, CarrySlots, CarryKg, null, name + "'s pack");
            if (skills != null)
                foreach (KeyValuePair<JobType, float> kv in skills) c.Skills[kv.Key] = kv.Value;
            _world.AddColonist(c);
            return c;
        }

        public Structure FreeBed()
        {
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (!s.IsComplete || !s.PlayerBuilt || _content.Building(s.DefId)?.IsBed != true) continue;
                bool taken = false;
                foreach (ColonistState c in _world.Colonists.Values)
                    if (c.Recruited && c.HomeBed == s.Id) taken = true;
                if (!taken) return s;
            }
            return null;
        }

        public OpResult Recruit(EntityId colonistId, Vec3 recruiterPosition)
        {
            if (!_world.Colonists.TryGetValue(colonistId, out ColonistState c)) return OpResult.Fail("missing", "There is nobody here.");
            if (c.Recruited) return OpResult.Fail("already", $"{c.Name} already lives in your settlement.");
            if (Vec3.Distance(c.Position, recruiterPosition) > 4f) return OpResult.Fail("too_far", "Move closer to talk.");
            Structure bed = FreeBed();
            if (bed == null) return OpResult.Fail("no_bed", $"{c.Name}: \"I'll come if you have a bed for me.\" Build a bedroll first.");
            c.Recruited = true;
            c.HomeBed = bed.Id;
            c.Status = "Joined the settlement";
            _bus?.Publish(new ColonistRecruited(c.Id, c.Name));
            _bus?.Publish(new Notification($"{c.Name} joined your settlement. They will haul and build on their own — manage them in the strategic view.", Severity.Success));
            return OpResult.Success($"{c.Name} joined you.");
        }

        // ================================================================== simulation

        public void Tick(double dt)
        {
            double now = _world.Clock.ElapsedSeconds;
            if (now >= _nextPlan)
            {
                _nextPlan = now + PlanInterval;
                PlanJobs(now);
            }
            foreach (EntityId id in SortedColonists())
            {
                ColonistState c = _world.Colonists[id];
                if (!c.Recruited || c.Activity == ColonistActivity.Down) continue;
                Needs(c, dt);
                if (c.Activity == ColonistActivity.Down) continue;
                if (Flee(c, (float)dt)) continue;
                if ((!c.CurrentJob.IsValid || !_world.Jobs.ContainsKey(c.CurrentJob)) && (!_nextAssign.TryGetValue(c.Id, out double next) || now >= next))
                {
                    _nextAssign[c.Id] = now + 1.0;
                    Assign(c, now);
                }
                if (c.CurrentJob.IsValid && c.HasMoveTarget && Movement.CannotReach(c) && _world.Jobs.TryGetValue(c.CurrentJob, out Job stuck))
                {
                    Fail(c, stuck, "can't find a way there");
                    c.HasMoveTarget = false;
                }
                else if (c.CurrentJob.IsValid) Execute(c, (float)dt);
                else
                {
                    c.Activity = ColonistActivity.Idle;
                    c.HasMoveTarget = false;
                    if (c.Status.Length == 0 || c.Activity == ColonistActivity.Idle) c.Status = "Idle — no work available";
                }
            }
            PruneFinishedJobs(now);
        }

        private void Needs(ColonistState c, double dt)
        {
            var activity = new PlayerActivity { Moving = c.Activity == ColonistActivity.Moving };
            SurvivalRules.Tick(c.Vitals, _content.Survival, activity, dt, _world.Clock.SecondsPerGameHour, _world.Settings.SurvivalDrain * 0.75f);
            if (c.Activity == ColonistActivity.Resting)
            {
                c.Vitals.Stamina = Math.Min(c.Vitals.MaxStamina, c.Vitals.Stamina + 5f * (float)dt);
                c.Vitals.Health = Math.Min(c.Vitals.MaxHealth, c.Vitals.Health + 0.05f * (float)dt);
            }
            if (c.Vitals.IsDead) Down(c, c.Vitals.Hunger <= 0 ? "starvation" : c.Vitals.Thirst <= 0 ? "dehydration" : "injuries");
        }

        /// <summary>Colonist killed: their job fails cleanly and carried items drop where they fell.</summary>
        public void Down(ColonistState c, string cause)
        {
            if (c.CurrentJob.IsValid && _world.Jobs.TryGetValue(c.CurrentJob, out Job j)) Fail(c, j, $"{c.Name} died ({cause})");
            c.Activity = ColonistActivity.Down;
            c.Status = $"Dead ({cause})";
            var stacks = new List<ItemStack>();
            for (int i = 0; i < c.Carry.SlotCount; i++)
                if (!c.Carry.GetSlot(i).IsEmpty) stacks.Add(c.Carry.GetSlot(i));
            c.Carry.ClearAll();
            if (stacks.Count > 0) _construction.CreateDrop(stacks, c.Position, $"{c.Name}'s belongings", false);
            _bus?.Publish(new Notification($"{c.Name} has died ({cause}).", Severity.Danger));
        }

        /// <summary>
        /// Colonists drop what they are doing and run from infected that are hunting nearby
        /// (their job fails cleanly, so reserved items are released). Returns true while fleeing.
        /// </summary>
        private bool Flee(ColonistState c, float dt)
        {
            AI.ZombieState threat = null;
            float best = FleeRadius;
            foreach (AI.ZombieState z in _world.Zombies.Values)
            {
                if (z.IsDead || (z.Mode != AI.ZombieMode.Chase && z.Mode != AI.ZombieMode.Attack && z.Mode != AI.ZombieMode.Investigate)) continue;
                float d = Vec3.DistanceXZ(z.Position, c.Position);
                if (d < best)
                {
                    best = d;
                    threat = z;
                }
            }
            if (threat == null)
            {
                if (c.Status == "Fleeing from the infected!") c.Status = "Safe again";
                return false;
            }
            if (c.CurrentJob.IsValid && _world.Jobs.TryGetValue(c.CurrentJob, out Job j)) Fail(c, j, "fled from the infected");
            Vec3 away = (c.Position - threat.Position).Normalized;
            if (away.SqrMagnitude < 0.01f) away = new Vec3(1, 0, 0);
            Vec3 goal = c.Position + away * 12f;
            if (c.HomeBed.IsValid && _world.Structures.TryGetValue(c.HomeBed, out Structure bed) && Vec3.DistanceXZ(bed.Position, threat.Position) > Vec3.DistanceXZ(c.Position, threat.Position))
                goal = bed.Position;
            c.MoveTarget = goal;
            c.HasMoveTarget = true;
            Movement.Step(c, WalkSpeed * 1.5f, 0.5f, dt);
            c.Activity = ColonistActivity.Moving;
            c.Status = "Fleeing from the infected!";
            return true;
        }

        public void DamageColonist(EntityId id, float amount, string source)
        {
            if (!_world.Colonists.TryGetValue(id, out ColonistState c) || c.Activity == ColonistActivity.Down) return;
            if (SurvivalRules.Damage(c.Vitals, amount)) Down(c, source);
        }

        // ================================================================== planning

        private void PlanJobs(double now)
        {
            List<Structure> storages = PlayerStorages();
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (!s.PlayerBuilt) continue;
                if (!s.IsComplete)
                {
                    PlanBlueprint(s, storages, now);
                    continue;
                }
                if (s.Machine != null) PlanMachine(s, storages, now);
                if (s.Crop != null && _farming != null) PlanFarm(s, storages, now);
                if (s.Generator != null) PlanFuel(s.Id, s.Generator.Fuel, storages, now, energy: true);
            }
        }

        private void PlanBlueprint(Structure s, List<Structure> storages, double now)
        {
            List<ItemAmount> remaining = _construction.RemainingCost(s);
            if (remaining.Count == 0)
            {
                if (!HasActiveJob(JobType.Build, s.Id, null)) AddJob(new Job { Type = JobType.Build, TargetStructure = s.Id, Description = $"Build {_content.Building(s.DefId)?.Name}", CreatedAt = now });
                return;
            }
            foreach (ItemAmount need in remaining)
            {
                int missing = need.Quantity - PendingQuantity(s.Id, need.ItemId);
                foreach (Structure st in storages)
                {
                    if (missing <= 0) break;
                    int available = st.Storage.Available(need.ItemId) - PendingOutgoing(st.Storage.Id, need.ItemId);
                    int qty = Math.Min(Math.Min(missing, available), MaxHaulQuantity);
                    if (qty <= 0) continue;
                    AddJob(new Job
                    {
                        Type = JobType.Haul, TargetStructure = s.Id, SourceInventory = st.Storage.Id, ItemId = need.ItemId, Quantity = qty,
                        DeliverToBlueprint = true, CreatedAt = now,
                        Description = $"Deliver {qty} {_content.Items.NameOf(need.ItemId)} to {_content.Building(s.DefId)?.Name} blueprint"
                    });
                    missing -= qty;
                }
            }
        }

        private void PlanMachine(Structure s, List<Structure> storages, double now)
        {
            RecipeDefinition r = _content.Recipe(s.Machine.RecipeId);
            if (r != null && s.Enabled)
            {
                foreach (ItemAmount input in r.Inputs)
                {
                    int target = input.Quantity * 3;
                    int missing = target - s.Machine.Input.Count(input.ItemId) - PendingQuantity(s.Id, input.ItemId);
                    if (missing < input.Quantity) continue;
                    PlanSupply(s, s.Machine.Input, input.ItemId, missing, storages, now);
                }
            }
            if (s.Machine.Fuel != null && s.Enabled) PlanFuel(s.Id, s.Machine.Fuel, storages, now, energy: false);

            // Collect finished products into storage.
            for (int i = 0; i < s.Machine.Output.SlotCount; i++)
            {
                ItemStack st = s.Machine.Output.GetSlot(i);
                if (st.IsEmpty) continue;
                int pending = PendingOutgoing(s.Machine.Output.Id, st.ItemId);
                int qty = Math.Min(s.Machine.Output.Available(st.ItemId) - pending, MaxHaulQuantity);
                if (qty <= 0) continue;
                Structure dest = BestStorageFor(st.ItemId, qty, storages, s.Position);
                if (dest == null) continue;
                AddJob(new Job
                {
                    Type = JobType.Haul, Priority = JobPriority.Productive, TargetStructure = dest.Id, SourceInventory = s.Machine.Output.Id,
                    DestinationInventory = dest.Storage.Id, ItemId = st.ItemId, Quantity = Math.Min(qty, dest.Storage.SpaceFor(st.ItemId)), CreatedAt = now,
                    Description = $"Store {_content.Items.NameOf(st.ItemId)} from {_content.Building(s.DefId)?.Name}"
                });
            }
        }

        private void PlanFarm(Structure plot, List<Structure> storages, double now)
        {
            if (HasActiveJob(JobType.Farm, plot.Id, null)) return;
            BuildingDefinition def = _content.Building(plot.DefId);
            Farming.CropState crop = plot.Crop;
            string name = def?.Name ?? "planter bed";
            if (crop.Ready)
            {
                AddJob(new Job { Type = JobType.Farm, Farm = FarmAction.Harvest, TargetStructure = plot.Id, Description = $"Harvest {name}", CreatedAt = now });
                return;
            }
            FarmAction action;
            string item;
            if (!crop.Planted)
            {
                action = FarmAction.Plant;
                item = def.Farm.SeedItem;
            }
            else if (crop.Water < 30f && !crop.Irrigated)
            {
                action = FarmAction.Water;
                item = "water_bottle";
            }
            else return;
            foreach (Structure st in storages)
            {
                if (st.Storage.Available(item) - PendingOutgoing(st.Storage.Id, item) <= 0) continue;
                AddJob(new Job
                {
                    Type = JobType.Farm, Farm = action, TargetStructure = plot.Id, SourceInventory = st.Storage.Id, ItemId = item, Quantity = 1, CreatedAt = now,
                    Description = $"{(action == FarmAction.Plant ? "Plant" : "Water")} {name}"
                });
                return;
            }
        }

        /// <summary>Content tag of items colonists may burn on their own (wood, charcoal, fuel cans).</summary>
        public const string FuelTag = "fuel";

        private void PlanFuel(EntityId machineId, Inventory fuel, List<Structure> storages, double now, bool energy)
        {
            int have = 0;
            for (int i = 0; i < fuel.SlotCount; i++) have += fuel.GetSlot(i).Quantity;
            if (have + PendingQuantity(machineId, null) >= 2) return;
            string best = null;
            float bestValue = 0f;
            foreach (Structure st in storages)
            {
                foreach (KeyValuePair<string, int> kv in st.Storage.Totals())
                {
                    ItemDefinition d = _content.Items.Get(kv.Key);
                    float value = energy ? d.EnergyKJ : d.BurnSeconds;
                    // Only dedicated fuels: colonists must not burn building materials such as planks.
                    if (value <= bestValue || !d.HasTag(FuelTag) || !fuel.Accepts(d.Id) || st.Storage.Available(d.Id) - PendingOutgoing(st.Storage.Id, d.Id) <= 0) continue;
                    best = d.Id;
                    bestValue = value;
                }
            }
            if (best == null) return;
            Structure target = _world.Structures[machineId];
            PlanSupply(target, fuel, best, energy ? 2 : 5, storages, now);
        }

        private void PlanSupply(Structure target, Inventory destination, string itemId, int quantity, List<Structure> storages, double now)
        {
            foreach (Structure st in storages)
            {
                if (quantity <= 0) return;
                int available = st.Storage.Available(itemId) - PendingOutgoing(st.Storage.Id, itemId);
                int qty = Math.Min(Math.Min(quantity, available), MaxHaulQuantity);
                if (qty <= 0) continue;
                AddJob(new Job
                {
                    Type = JobType.Haul, TargetStructure = target.Id, SourceInventory = st.Storage.Id, DestinationInventory = destination.Id,
                    ItemId = itemId, Quantity = qty, CreatedAt = now,
                    Description = $"Supply {qty} {_content.Items.NameOf(itemId)} to {_content.Building(target.DefId)?.Name}"
                });
                quantity -= qty;
            }
        }

        // ================================================================== assignment

        private void Assign(ColonistState c, double now)
        {
            c.CurrentJob = EntityId.None;
            if (TryPersonalJob(c, now)) return;

            Job best = null;
            int bestPersonal = int.MaxValue;
            float bestScore = float.MaxValue;
            foreach (Job j in SortedJobs())
            {
                if (j.State != JobState.Open || j.AssigneeId.IsValid) continue;
                int personal = c.Work.PriorityFor(j.Type);
                if (personal <= 0) continue;
                if (j.Type == JobType.Build && _world.Structures.TryGetValue(j.TargetStructure, out Structure bp) && _construction.RemainingCost(bp).Count > 0) continue;
                // Order: job urgency bucket, then the colonist's preference, then skill-weighted distance.
                float score = Vec3.Distance(c.Position, JobPosition(j)) / Math.Max(0.25f, c.Skill(j.Type));
                bool better = best == null || j.Priority < best.Priority || (j.Priority == best.Priority && (personal < bestPersonal || (personal == bestPersonal && score < bestScore)));
                if (!better) continue;
                best = j;
                bestPersonal = personal;
                bestScore = score;
            }
            if (best != null && !Claim(c, best)) _world.Jobs.Remove(best.Id); // stale plan; the planner recreates it
        }

        private bool TryPersonalJob(ColonistState c, double now)
        {
            // Stash anything left in the pack first.
            if (!c.Carry.IsEmpty)
            {
                for (int i = 0; i < c.Carry.SlotCount; i++)
                {
                    ItemStack st = c.Carry.GetSlot(i);
                    if (st.IsEmpty) continue;
                    Structure dest = BestStorageFor(st.ItemId, 1, PlayerStorages(), c.Position);
                    if (dest == null) break;
                    var stash = new Job
                    {
                        Type = JobType.Haul, Priority = JobPriority.Productive, TargetStructure = dest.Id, SourceInventory = c.Carry.Id,
                        DestinationInventory = dest.Storage.Id, ItemId = st.ItemId, Quantity = Math.Min(c.Carry.Available(st.ItemId), dest.Storage.SpaceFor(st.ItemId)),
                        Description = $"Put away {_content.Items.NameOf(st.ItemId)}", CreatedAt = now
                    };
                    if (stash.Quantity <= 0) continue;
                    AddJob(stash);
                    if (Claim(c, stash)) return true;
                    _world.Jobs.Remove(stash.Id);
                }
            }
            if (c.Vitals.Hunger < NeedThreshold || c.Vitals.Thirst < NeedThreshold)
            {
                bool thirst = c.Vitals.Thirst < c.Vitals.Hunger;
                foreach (Structure st in PlayerStorages())
                {
                    string food = BestConsumable(st.Storage, thirst);
                    if (food == null) continue;
                    var eat = new Job
                    {
                        Type = JobType.Eat, Priority = JobPriority.Survival, SourceInventory = st.Storage.Id, TargetStructure = st.Id, ItemId = food, Quantity = 1,
                        Description = $"{(thirst ? "Drink" : "Eat")} {_content.Items.NameOf(food)}", CreatedAt = now
                    };
                    AddJob(eat);
                    if (Claim(c, eat)) return true;
                    _world.Jobs.Remove(eat.Id);
                }
            }
            if (_world.Clock.IsNight && c.HomeBed.IsValid && _world.Structures.ContainsKey(c.HomeBed))
            {
                var rest = new Job { Type = JobType.Rest, Priority = JobPriority.Survival, TargetStructure = c.HomeBed, Description = "Sleep until morning", CreatedAt = now };
                AddJob(rest);
                return Claim(c, rest);
            }
            return false;
        }

        /// <summary>Claims a job for a colonist, acquiring every reservation it needs (all or nothing).</summary>
        public bool Claim(ColonistState c, Job j)
        {
            if (j.State != JobState.Open || j.AssigneeId.IsValid) return false;
            if (j.Type == JobType.Haul || j.Type == JobType.Eat || (j.Type == JobType.Farm && j.SourceInventory.IsValid))
            {
                Inventory src = _world.FindInventory(j.SourceInventory);
                if (src == null || !src.TryReserveItems(j.ItemId, j.Quantity, out ReservationToken st))
                {
                    j.State = JobState.Failed;
                    j.FailureReason = $"{_content.Items.NameOf(j.ItemId)} no longer available";
                    return false;
                }
                j.SourceToken = st;
                if (j.Type == JobType.Haul && j.DestinationInventory.IsValid)
                {
                    Inventory dst = _world.FindInventory(j.DestinationInventory);
                    if (dst == null || !dst.TryReserveSpace(j.ItemId, j.Quantity, out ReservationToken dt))
                    {
                        src.ReleaseReservation(st);
                        j.State = JobState.Failed;
                        j.FailureReason = "destination is full";
                        return false;
                    }
                    j.DestinationToken = dt;
                }
            }
            j.AssigneeId = c.Id;
            j.State = JobState.Claimed;
            j.Stage = HaulStage.ToSource;
            c.CurrentJob = j.Id;
            c.Status = j.Description;
            return true;
        }

        // ================================================================== execution

        private void Execute(ColonistState c, float dt)
        {
            Job j = _world.Jobs[c.CurrentJob];
            if (j.State != JobState.Claimed || j.AssigneeId != c.Id)
            {
                c.CurrentJob = EntityId.None;
                return;
            }
            switch (j.Type)
            {
                case JobType.Haul:
                    ExecuteHaul(c, j, dt);
                    break;
                case JobType.Build:
                    if (!_world.Structures.TryGetValue(j.TargetStructure, out Structure bp) || bp.IsComplete)
                    {
                        Complete(c, j);
                        return;
                    }
                    if (!MoveTo(c, bp.Position, dt, 2.5f + FootprintRadius(bp))) return;
                    c.Activity = ColonistActivity.Working;
                    OpResult r = _construction.Work(bp, dt * c.Skill(JobType.Build), c.Carry);
                    if (!r.Ok)
                    {
                        Fail(c, j, r.Message);
                        return;
                    }
                    if (bp.IsComplete) Complete(c, j);
                    break;
                case JobType.Eat:
                    ExecuteEat(c, j, dt);
                    break;
                case JobType.Farm:
                    ExecuteFarm(c, j, dt);
                    break;
                case JobType.Rest:
                    if (!_world.Structures.TryGetValue(j.TargetStructure, out Structure bed))
                    {
                        Fail(c, j, "their bed was destroyed");
                        return;
                    }
                    if (!MoveTo(c, bed.Position, dt, 1.2f)) return;
                    c.Activity = ColonistActivity.Resting;
                    c.Status = "Sleeping";
                    if (!_world.Clock.IsNight) Complete(c, j);
                    break;
            }
        }

        private void ExecuteHaul(ColonistState c, Job j, float dt)
        {
            if (j.Stage == HaulStage.ToSource)
            {
                Inventory src = _world.FindInventory(j.SourceInventory);
                if (src == null || !src.HasReservation(j.SourceToken))
                {
                    Fail(c, j, "the source container is gone");
                    return;
                }
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, ArriveDistance)) return;
                if (src == c.Carry)
                {
                    // Putting away own items: nothing to pick up.
                    j.Stage = HaulStage.ToDestination;
                    return;
                }
                if (!c.Carry.TryReserveSpace(j.ItemId, j.Quantity, out ReservationToken carryToken))
                {
                    Fail(c, j, "their pack is full");
                    return;
                }
                var taken = new List<ItemStack>();
                if (!src.CommitOutgoing(j.SourceToken, taken))
                {
                    c.Carry.ReleaseReservation(carryToken);
                    Fail(c, j, "items were not there");
                    return;
                }
                if (!c.Carry.CommitIncoming(carryToken, taken))
                {
                    foreach (ItemStack s in taken) src.TryAddStack(s);
                    c.Carry.ReleaseReservation(carryToken);
                    Fail(c, j, "could not carry the items");
                    return;
                }
                j.SourceToken = default;
                j.Stage = HaulStage.ToDestination;
                return;
            }

            // Carrying to the destination.
            if (!_world.Structures.TryGetValue(j.TargetStructure, out Structure target))
            {
                Fail(c, j, "the destination is gone");
                return;
            }
            if (!MoveTo(c, target.Position, dt, ArriveDistance + FootprintRadius(target))) return;
            c.Activity = ColonistActivity.Working;
            if (j.DeliverToBlueprint)
            {
                if (target.IsComplete)
                {
                    Complete(c, j);
                    return;
                }
                _construction.DeliverMaterials(target, c.Carry);
                Complete(c, j);
                return;
            }
            Inventory dst = _world.FindInventory(j.DestinationInventory);
            if (dst == null || !dst.HasReservation(j.DestinationToken))
            {
                Fail(c, j, "the destination is gone");
                return;
            }
            var stacks = new List<ItemStack>();
            int qty = Math.Min(j.Quantity, c.Carry.Available(j.ItemId));
            if (qty < j.Quantity && c.Carry == _world.FindInventory(j.SourceInventory) && c.Carry.HasReservation(j.SourceToken))
            {
                // Stash jobs reserve their own pack; release it before taking.
                c.Carry.ReleaseReservation(j.SourceToken);
                qty = Math.Min(j.Quantity, c.Carry.Available(j.ItemId));
            }
            if (qty < j.Quantity || !c.Carry.TryTake(j.ItemId, j.Quantity, stacks))
            {
                Fail(c, j, "items went missing on the way");
                return;
            }
            if (!dst.CommitIncoming(j.DestinationToken, stacks))
            {
                foreach (ItemStack s in stacks) c.Carry.TryAddStack(s);
                Fail(c, j, "the destination refused the delivery");
                return;
            }
            j.DestinationToken = default;
            Complete(c, j);
        }

        private void ExecuteFarm(ColonistState c, Job j, float dt)
        {
            if (_farming == null || !_world.Structures.TryGetValue(j.TargetStructure, out Structure plot) || plot.Crop == null)
            {
                Fail(c, j, "the planter bed is gone");
                return;
            }
            if (j.SourceInventory.IsValid && j.Stage == HaulStage.ToSource)
            {
                Inventory src = _world.FindInventory(j.SourceInventory);
                if (src == null || !src.HasReservation(j.SourceToken))
                {
                    Fail(c, j, $"no {_content.Items.NameOf(j.ItemId)} left in storage");
                    return;
                }
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, ArriveDistance)) return;
                var taken = new List<ItemStack>();
                if (!c.Carry.CanAccept(j.ItemId, 1) || !src.CommitOutgoing(j.SourceToken, taken))
                {
                    Fail(c, j, "could not pick up the supplies");
                    return;
                }
                foreach (ItemStack st in taken) c.Carry.TryAddStack(st);
                j.SourceToken = default;
                j.Stage = HaulStage.ToDestination;
                return;
            }
            if (!MoveTo(c, plot.Position, dt, ArriveDistance + FootprintRadius(plot))) return;
            c.Activity = ColonistActivity.Working;
            OpResult r;
            switch (j.Farm)
            {
                case FarmAction.Plant: r = plot.Crop.Planted ? OpResult.Success() : _farming.Plant(plot, c.Carry); break;
                case FarmAction.Water: r = plot.Crop.Water >= 90f ? OpResult.Success() : _farming.Water(plot, c.Carry); break;
                default: r = plot.Crop.Ready ? _farming.Harvest(plot, c.Carry) : OpResult.Success(); break;
            }
            if (!r.Ok)
            {
                Fail(c, j, r.Message);
                return;
            }
            Complete(c, j);
        }

        private void ExecuteEat(ColonistState c, Job j, float dt)
        {
            Inventory src = _world.FindInventory(j.SourceInventory);
            if (src == null || !src.HasReservation(j.SourceToken))
            {
                Fail(c, j, "the food is gone");
                return;
            }
            if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, ArriveDistance)) return;
            var taken = new List<ItemStack>();
            if (!src.CommitOutgoing(j.SourceToken, taken))
            {
                Fail(c, j, "the food is gone");
                return;
            }
            j.SourceToken = default;
            c.Activity = ColonistActivity.Eating;
            ItemDefinition food = _content.Items.Get(j.ItemId);
            SurvivalRules.Apply(c.Vitals, food.Consumable);
            if (!string.IsNullOrEmpty(food.Consumable?.Returns) && !c.Carry.TryAdd(food.Consumable.Returns, 1))
                _construction.CreateDrop(new List<ItemStack> { new ItemStack(food.Consumable.Returns, 1) }, c.Position, "Empty bottle", false);
            Complete(c, j);
        }

        private bool MoveTo(ColonistState c, Vec3 target, float dt, float arrive)
        {
            c.MoveTarget = target;
            c.HasMoveTarget = true;
            if (Movement.Step(c, WalkSpeed, arrive, dt))
            {
                c.HasMoveTarget = false;
                return true;
            }
            c.Activity = ColonistActivity.Moving;
            return false;
        }

        private void Complete(ColonistState c, Job j)
        {
            j.State = JobState.Completed;
            c.CurrentJob = EntityId.None;
            c.Activity = ColonistActivity.Idle;
            c.HasMoveTarget = false;
            c.Status = "Done: " + j.Description;
        }

        /// <summary>Fails a job: releases every reservation it still holds and records the reason.</summary>
        public void Fail(ColonistState c, Job j, string reason)
        {
            if (j.SourceToken.IsValid) _world.FindInventory(j.SourceInventory)?.ReleaseReservation(j.SourceToken);
            if (j.DestinationToken.IsValid) _world.FindInventory(j.DestinationInventory)?.ReleaseReservation(j.DestinationToken);
            j.SourceToken = default;
            j.DestinationToken = default;
            j.State = JobState.Failed;
            j.FailureReason = reason;
            if (c != null)
            {
                c.CurrentJob = EntityId.None;
                c.Activity = ColonistActivity.Idle;
                c.HasMoveTarget = false;
                c.Status = $"Gave up: {j.Description} — {reason}";
                c.RecordFailure($"{j.Description}: {reason}");
                _bus?.Publish(new ColonistJobFailed(c.Id, j.Id, reason));
            }
        }

        // ================================================================== helpers

        private void AddJob(Job j)
        {
            j.Id = _world.Ids.Allocate();
            _world.Jobs[j.Id] = j;
        }

        private void PruneFinishedJobs(double now)
        {
            List<EntityId> remove = null;
            foreach (Job j in _world.Jobs.Values)
            {
                if (j.IsActive || now - j.CreatedAt < 60.0) continue;
                (remove ?? (remove = new List<EntityId>())).Add(j.Id);
            }
            if (remove == null) return;
            foreach (EntityId id in remove) _world.Jobs.Remove(id);
        }

        private bool HasActiveJob(JobType type, EntityId target, string item)
        {
            foreach (Job j in _world.Jobs.Values)
                if (j.IsActive && j.Type == type && j.TargetStructure == target && (item == null || j.ItemId == item)) return true;
            return false;
        }

        /// <summary>Quantity already on its way to a structure (optionally of one item).</summary>
        private int PendingQuantity(EntityId target, string itemId)
        {
            int n = 0;
            foreach (Job j in _world.Jobs.Values)
                if (j.IsActive && j.Type == JobType.Haul && j.TargetStructure == target && (itemId == null || j.ItemId == itemId)) n += j.Quantity;
            return n;
        }

        /// <summary>Quantity that open (not yet claimed) jobs intend to take from an inventory.</summary>
        private int PendingOutgoing(EntityId inventory, string itemId)
        {
            int n = 0;
            foreach (Job j in _world.Jobs.Values)
                if (j.State == JobState.Open && j.SourceInventory == inventory && j.ItemId == itemId) n += j.Quantity;
            return n;
        }

        private List<Structure> PlayerStorages()
        {
            var list = new List<Structure>();
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (s.PlayerBuilt && s.IsComplete && s.Storage != null) list.Add(s);
            }
            return list;
        }

        private Structure BestStorageFor(string itemId, int quantity, List<Structure> storages, Vec3 near)
        {
            Structure best = null;
            float bestD = float.MaxValue;
            foreach (Structure s in storages)
            {
                if (s.Storage.SpaceFor(itemId) < Math.Min(quantity, 1)) continue;
                float d = Vec3.Distance(s.Position, near);
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        private string BestConsumable(Inventory inv, bool thirst)
        {
            string best = null;
            float bestValue = 0f;
            foreach (KeyValuePair<string, int> kv in inv.Totals())
            {
                ItemDefinition d = _content.Items.Get(kv.Key);
                if (d.Consumable == null || inv.Available(d.Id) <= 0) continue;
                float value = thirst ? d.Consumable.Thirst : d.Consumable.Hunger;
                if (value <= bestValue) continue;
                bestValue = value;
                best = d.Id;
            }
            return best;
        }

        private Vec3 JobPosition(Job j)
        {
            if ((j.Type == JobType.Haul || j.Type == JobType.Eat) && j.SourceInventory.IsValid) return InventoryPosition(j.SourceInventory, null);
            return _world.Structures.TryGetValue(j.TargetStructure, out Structure s) ? s.Position : Vec3.Zero;
        }

        private Vec3 InventoryPosition(EntityId inventory, ColonistState fallback)
        {
            EntityId owner = _world.OwnerOf(inventory);
            if (_world.Structures.TryGetValue(owner, out Structure s)) return s.Position;
            if (_world.Colonists.TryGetValue(owner, out ColonistState c)) return c.Position;
            if (_world.Drops.TryGetValue(owner, out WorldDrop d)) return d.Position;
            return fallback?.Position ?? Vec3.Zero;
        }

        private float FootprintRadius(Structure s)
        {
            BuildingDefinition def = _content.Building(s.DefId);
            return def == null ? 0.5f : 0.5f * Math.Max(def.SizeX, def.SizeZ);
        }

        private List<EntityId> SortedColonists()
        {
            var ids = new List<EntityId>(_world.Colonists.Keys);
            ids.Sort();
            return ids;
        }

        private List<Job> SortedJobs()
        {
            var list = new List<Job>(_world.Jobs.Values);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }
    }
}
