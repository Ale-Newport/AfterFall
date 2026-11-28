using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.Weather;
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

        /// <summary>Below this warmth a colonist leaves their work to warm up; they go back at <see cref="WarmEnough"/>.</summary>
        public const float WarmUpThreshold = 35f;

        public const float WarmEnough = 90f;

        /// <summary>How far a cold colonist will walk to a fire.</summary>
        public const float WarmUpSearchRadius = 90f;
        public const float FleeRadius = 9f;

        /// <summary>Guards defend this far around the settlement (their bed), strike from this reach, this often, this hard.</summary>
        public const float GuardRadius = 30f, GuardReach = 1.9f, GuardHitEvery = 1.2f, GuardDamage = 14f;

        /// <summary>Idle colonists further than this from their bed walk back to the settlement.</summary>
        public const float HomeRadius = 18f;

        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly ConstructionService _construction;
        private Farming.FarmingSystem _farming;

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

        /// <summary>Damages an infected (the session wires it to the infected AI); returns true if it died.</summary>
        public Func<EntityId, float, Vec3, bool> DamageZombie { get; set; }

        /// <summary>Noise of colonists' work (set by the session to the infected's noise field).</summary>
        public Action<Vec3, float, string> EmitNoise { get; set; }

        /// <summary>Harvests a gatherer makes before carrying the load home.</summary>
        public const int GatherHarvestsPerTrip = 6;

        /// <summary>Colonists take this much longer per harvest than the player.</summary>
        public const float GatherSlowdown = 1.3f;

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
                    if (c.Recruited && c.Activity != ColonistActivity.Down && c.HomeBed == s.Id) taken = true;
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
            if (now >= _world.ColonyNextPlanAt)
            {
                _world.ColonyNextPlanAt = now + PlanInterval;
                PlanJobs(now);
            }
            foreach (EntityId id in SortedColonists())
            {
                ColonistState c = _world.Colonists[id];
                if (!c.Recruited || c.Activity == ColonistActivity.Down) continue;
                Needs(c, dt);
                if (c.Activity == ColonistActivity.Down) continue;
                if (Guard(c, (float)dt)) continue;
                if (Flee(c, (float)dt)) continue;
                if ((!c.CurrentJob.IsValid || !_world.Jobs.ContainsKey(c.CurrentJob)) && now >= c.NextAssignAt)
                {
                    c.NextAssignAt = now + 1.0;
                    Assign(c, now);
                }
                if (c.CurrentJob.IsValid && c.HasMoveTarget && Movement.CannotReach(c) && _world.Jobs.TryGetValue(c.CurrentJob, out Job stuck))
                {
                    Fail(c, stuck, "can't find a way there");
                    c.HasMoveTarget = false;
                }
                else if (c.CurrentJob.IsValid) Execute(c, (float)dt);
                else if (FarFromHome(c, out Structure home))
                {
                    // A new recruit (or a colonist who fled far) walks back to the settlement when there is no work.
                    if (c.HasMoveTarget && Movement.CannotReach(c))
                    {
                        c.Activity = ColonistActivity.Idle;
                        c.Status = "Can't find a way to the settlement";
                    }
                    else
                    {
                        if (MoveTo(c, home.Position, (float)dt, HomeRadius * 0.5f)) c.Activity = ColonistActivity.Idle;
                        c.Status = "Heading to the settlement";
                    }
                }
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
            ClimateReading climate = Climate.At(_world, _content, c.Position, WeatherReadout.Of(_world));
            c.FeltTemperatureC = climate.TemperatureC;
            SurvivalRules.TickWarmth(c.Vitals, _content.Survival, climate.TemperatureC, dt, _world.Clock.SecondsPerGameHour);
            if (c.Activity == ColonistActivity.Resting)
            {
                c.Vitals.Stamina = Math.Min(c.Vitals.MaxStamina, c.Vitals.Stamina + 5f * (float)dt);
                c.Vitals.Health = Math.Min(c.Vitals.MaxHealth, c.Vitals.Health + 0.05f * (float)dt);
            }
            if (c.Vitals.IsDead) Down(c, c.Vitals.Hunger <= 0 ? "starvation" : c.Vitals.Thirst <= 0 ? "dehydration" : c.Vitals.Warmth <= 0 ? "the cold" : "injuries");
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

        /// <summary>The best weapon (highest damage) a colonist carries, or null; its slot.</summary>
        private ToolStats GuardWeapon(Inventory carry, out int slot)
        {
            slot = -1;
            ToolStats best = null;
            for (int i = 0; i < carry.SlotCount; i++)
            {
                ItemStack st = carry.GetSlot(i);
                if (st.IsEmpty) continue;
                ToolStats t = _content.Items.Get(st.ItemId).Tool;
                if (t == null || t.Damage <= GuardDamage || (best != null && t.Damage <= best.Damage)) continue;
                best = t;
                slot = i;
            }
            return best;
        }

        /// <summary>
        /// A guard with nothing better than bare hands fetches the best weapon from storage near the
        /// settlement (a machete, a pipe, an axe…). Returns true while walking to get it.
        /// </summary>
        private bool ArmGuard(ColonistState c, Vec3 post, float dt)
        {
            ToolStats have = GuardWeapon(c.Carry, out _);
            Structure from = null;
            string item = null;
            float bestDamage = have?.Damage ?? GuardDamage;
            foreach (Structure st in PlayerStorages())
            {
                if (Vec3.DistanceXZ(st.Position, post) > GuardRadius) continue;
                foreach (KeyValuePair<string, int> kv in st.Storage.Totals())
                {
                    ToolStats t = _content.Items.Get(kv.Key).Tool;
                    if (t == null || t.Damage <= bestDamage || st.Storage.Available(kv.Key) - PendingOutgoing(st.Storage.Id, kv.Key) <= 0) continue;
                    bestDamage = t.Damage;
                    from = st;
                    item = kv.Key;
                }
            }
            if (from == null) return false;
            if (!MoveTo(c, from.Position, dt, ArriveDistance + FootprintRadius(from)))
            {
                c.Status = $"Guarding: fetching the {_content.Items.NameOf(item)}";
                return true;
            }
            var taken = new List<ItemStack>();
            if (from.Storage.TryRemove(item, 1) && c.Carry.TryAdd(item, 1)) c.Status = $"On guard with the {_content.Items.NameOf(item)}";
            c.Activity = ColonistActivity.Idle;
            return false;
        }

        // ------------------------------------------------------------------ mechanic and medic

        /// <summary>Repairs start below this share of a building's health.</summary>
        public const float RepairBelow = 0.75f;
        public const float RepairSeconds = 3f;

        /// <summary>A repair job for each damaged building whose repair materials one storage holds (and a hammer is in storage).</summary>
        private void PlanRepairs(List<Structure> storages, double now)
        {
            bool anyMechanic = false;
            foreach (ColonistState c in _world.Colonists.Values)
                if (c.Recruited && c.Activity != ColonistActivity.Down && c.Work.PriorityFor(JobType.Mechanic) > 0) anyMechanic = true;
            if (!anyMechanic) return;
            bool hammer = false;
            foreach (Structure st in storages)
                foreach (KeyValuePair<string, int> kv in st.Storage.Totals())
                    if (_content.Items.Get(kv.Key).Tool?.Type == ToolType.Hammer) hammer = true;
            if (!hammer) return;
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                BuildingDefinition def = _content.Building(s.DefId);
                if (!s.PlayerBuilt || !s.IsComplete || def == null || s.Health >= def.MaxHealth * RepairBelow || HasActiveJob(JobType.Mechanic, s.Id, null)) continue;
                List<ItemAmount> cost = _construction.RepairCost(s);
                Structure source = null;
                foreach (Structure st in storages)
                {
                    bool all = true;
                    foreach (ItemAmount a in cost)
                        if (st.Storage.Available(a.ItemId) - PendingOutgoing(st.Storage.Id, a.ItemId) < a.Quantity) all = false;
                    if (all)
                    {
                        source = st;
                        break;
                    }
                }
                if (source == null) continue;
                AddJob(new Job
                {
                    Type = JobType.Mechanic, Priority = JobPriority.Safety, TargetStructure = s.Id, SourceInventory = source.Storage.Id, CreatedAt = now,
                    Description = $"Repair {def.Name}"
                });
            }
        }

        private void ExecuteRepair(ColonistState c, Job j, float dt)
        {
            if (!_world.Structures.TryGetValue(j.TargetStructure, out Structure s) || !s.IsComplete)
            {
                Fail(c, j, "the building is gone");
                return;
            }
            BuildingDefinition def = _content.Building(s.DefId);
            if (s.Health >= def.MaxHealth)
            {
                Complete(c, j);
                return;
            }
            List<ItemAmount> cost = _construction.RepairCost(s);
            if (j.Stage == HaulStage.ToSource)
            {
                Inventory src = _world.FindInventory(j.SourceInventory);
                if (src == null)
                {
                    Fail(c, j, "the storage is gone");
                    return;
                }
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, SourceArrive(j.SourceInventory))) return;
                if (!src.TryConsume(cost))
                {
                    Fail(c, j, "the repair materials are gone");
                    return;
                }
                foreach (ItemAmount a in cost) c.Carry.AddUpTo(a.ItemId, a.Quantity);
                j.Stage = HaulStage.ToDestination;
                c.Status = j.Description;
            }
            if (!MoveTo(c, s.Position, dt, 2.5f + FootprintRadius(s))) return;
            c.Activity = ColonistActivity.Working;
            j.Progress += dt * c.Skill(JobType.Mechanic);
            if (j.Progress < RepairSeconds) return;
            OpResult r = _construction.Repair(s, c.Carry);
            if (!r.Ok)
            {
                Fail(c, j, r.Message);
                return;
            }
            c.Skills[JobType.Mechanic] = Math.Min(2f, c.Skill(JobType.Mechanic) + 0.02f);
            Complete(c, j);
        }

        /// <summary>Colonists treat below this share of their health, the survivor below <see cref="TreatSurvivorBelow"/> and within <see cref="TreatSurvivorRadius"/> of a medic's bed.</summary>
        public const float TreatBelow = 0.6f, TreatSurvivorBelow = 0.5f, TreatSurvivorRadius = 40f;

        private void PlanTreatment(List<Structure> storages, double now)
        {
            var medics = new List<ColonistState>();
            foreach (ColonistState c in _world.Colonists.Values)
                if (c.Recruited && c.Activity != ColonistActivity.Down && c.Work.PriorityFor(JobType.Medic) > 0) medics.Add(c);
            if (medics.Count == 0) return;
            var patients = new List<(EntityId Id, float Missing, Vec3 At)>();
            foreach (ColonistState c in _world.Colonists.Values)
                if (c.Recruited && c.Activity != ColonistActivity.Down && c.Vitals.Health < c.Vitals.MaxHealth * TreatBelow)
                    patients.Add((c.Id, c.Vitals.MaxHealth - c.Vitals.Health, c.Position));
            PlayerState p = _world.Player;
            if (p != null && !p.Vitals.IsDead && p.Vitals.Health < p.Vitals.MaxHealth * TreatSurvivorBelow && !p.InVehicle)
                foreach (ColonistState m in medics)
                    if (m.HomeBed.IsValid && _world.Structures.TryGetValue(m.HomeBed, out Structure bed) && Vec3.DistanceXZ(bed.Position, p.Position) < TreatSurvivorRadius)
                    {
                        patients.Add((EntityId.None, p.Vitals.MaxHealth - p.Vitals.Health, p.Position));
                        break;
                    }
            foreach ((EntityId id, float missing, Vec3 at) in patients)
            {
                bool treating = false;
                foreach (Job j in _world.Jobs.Values)
                    if (j.IsActive && j.Type == JobType.Medic && j.Patient == id) treating = true;
                if (treating) continue;
                // A medkit for bad wounds, a bandage otherwise (whichever storage has one).
                foreach (string want in missing > 40f ? new[] { "medkit", "bandage" } : new[] { "bandage", "medkit" })
                {
                    Structure source = null;
                    foreach (Structure st in storages)
                        if (st.Storage.Available(want) - PendingOutgoing(st.Storage.Id, want) > 0)
                        {
                            source = st;
                            break;
                        }
                    if (source == null || !_content.Items.Contains(want)) continue;
                    string who = id.IsValid && _world.Colonists.TryGetValue(id, out ColonistState pc) ? pc.Name : "the survivor";
                    AddJob(new Job
                    {
                        Type = JobType.Medic, Priority = JobPriority.Survival, SourceInventory = source.Storage.Id, ItemId = want, Quantity = 1, Patient = id,
                        TargetStructure = source.Id, CreatedAt = now, Description = $"Treat {who} with a {_content.Items.NameOf(want).ToLowerInvariant()}"
                    });
                    break;
                }
            }
        }

        private void ExecuteTreat(ColonistState c, Job j, float dt)
        {
            SurvivalState patient;
            Vec3 at;
            if (j.Patient.IsValid)
            {
                if (!_world.Colonists.TryGetValue(j.Patient, out ColonistState pc) || pc.Activity == ColonistActivity.Down)
                {
                    Fail(c, j, "the patient is gone");
                    return;
                }
                patient = pc.Vitals;
                at = pc.Position;
            }
            else
            {
                if (_world.Player == null || _world.Player.Vitals.IsDead)
                {
                    Fail(c, j, "the survivor is gone");
                    return;
                }
                patient = _world.Player.Vitals;
                at = _world.Player.Position;
            }
            if (j.Stage == HaulStage.ToSource)
            {
                Inventory src = _world.FindInventory(j.SourceInventory);
                if (src == null || !src.HasReservation(j.SourceToken))
                {
                    Fail(c, j, "the medicine is gone");
                    return;
                }
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, SourceArrive(j.SourceInventory))) return;
                var taken = new List<ItemStack>();
                if (!src.CommitOutgoing(j.SourceToken, taken))
                {
                    Fail(c, j, "the medicine is gone");
                    return;
                }
                j.SourceToken = default;
                j.Stage = HaulStage.ToDestination;
            }
            if (!MoveTo(c, at, dt, ArriveDistance)) return;
            c.Activity = ColonistActivity.Working;
            j.Progress += dt;
            if (j.Progress < 2f) return;
            ItemDefinition item = _content.Items.Get(j.ItemId);
            float before = patient.Health;
            SurvivalRules.Apply(patient, item.Consumable);
            // Skill makes the same dressing go further.
            patient.Health = Math.Min(patient.MaxHealth, patient.Health + (patient.Health - before) * Math.Max(0f, c.Skill(JobType.Medic) - 1f));
            c.Skills[JobType.Medic] = Math.Min(2f, c.Skill(JobType.Medic) + 0.02f);
            string who = j.Patient.IsValid && _world.Colonists.TryGetValue(j.Patient, out ColonistState p2) ? p2.Name : "you";
            _bus?.Publish(new Notification($"{c.Name} treated {who} ({item.Name}: health {before:0} → {patient.Health:0}).", Severity.Success));
            Complete(c, j);
        }

        /// <summary>
        /// A guard (Guard work on) fights infected that come within <see cref="GuardRadius"/> of the
        /// settlement: drops the current job cleanly, walks up and strikes every
        /// <see cref="GuardHitEvery"/> seconds. Returns true while fighting. Below a third of their
        /// health a guard leaves the fight (and flees like anyone else).
        /// </summary>
        private bool Guard(ColonistState c, float dt)
        {
            if (c.Work.PriorityFor(JobType.Guard) <= 0 || DamageZombie == null || c.Vitals.Health < c.Vitals.MaxHealth / 3f) return false;
            Vec3 post = c.HomeBed.IsValid && _world.Structures.TryGetValue(c.HomeBed, out Structure bed) ? bed.Position : c.Position;
            AI.ZombieState target = null;
            float best = float.MaxValue;
            foreach (AI.ZombieState z in _world.Zombies.Values)
            {
                if (z.IsDead || Vec3.DistanceXZ(z.Position, post) > GuardRadius) continue;
                float d = Vec3.DistanceXZ(z.Position, c.Position);
                if (d < best)
                {
                    best = d;
                    target = z;
                }
            }
            if (target == null)
            {
                if (c.Status.StartsWith("Guarding", StringComparison.Ordinal)) c.Status = "On guard — all quiet";
                return !c.CurrentJob.IsValid && ArmGuard(c, post, dt);
            }
            if (c.CurrentJob.IsValid && _world.Jobs.TryGetValue(c.CurrentJob, out Job j)) Fail(c, j, "called away to fight the infected");
            if (best > GuardReach)
            {
                c.Activity = ColonistActivity.Moving;
                c.Status = "Guarding: going for the infected";
                MoveTo(c, target.Position, dt, GuardReach * 0.8f);
                return true;
            }
            c.HasMoveTarget = false;
            c.Activity = ColonistActivity.Working;
            c.GuardCooldown -= dt;
            if (c.GuardCooldown > 0f) return true;
            c.GuardCooldown = GuardHitEvery;
            float skill = c.Skill(JobType.Guard); // 1 to start, better with practice (like every skill)
            ToolStats weapon = GuardWeapon(c.Carry, out _);
            if (weapon != null) c.GuardCooldown = Math.Max(0.6f, weapon.Cooldown * 1.3f);
            bool killed = DamageZombie(target.Id, Math.Max(GuardDamage, weapon?.Damage ?? 0f) * skill, c.Position);
            c.Skills[JobType.Guard] = Math.Min(2f, skill + 0.02f);
            c.Status = killed ? "Guarding: put down an infected" : "Guarding: fighting the infected";
            if (killed) _bus?.Publish(new Notification($"{c.Name} put down an infected near the settlement.", Severity.Success));
            return true;
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
                if (s.Heater != null && s.Enabled) PlanFuel(s.Id, s.Heater.Fuel, storages, now, energy: false);
            }
            PlanGathering(storages, now);
            PlanTidying(storages, now);
            PlanRepairs(storages, now);
            PlanTreatment(storages, now);
        }

        /// <summary>Designated storage is preferred over general storage up to this many metres further away.</summary>
        public const float DesignatedPreference = 500f;
        private const int MaxTidyJobs = 4;

        /// <summary>
        /// Storage designation, low priority: items in storage that no longer takes them, and items in
        /// general storage that a designated crate takes, are carried to where they belong.
        /// </summary>
        private void PlanTidying(List<Structure> storages, double now)
        {
            bool designated = false;
            foreach (Structure s in storages)
                if (s.StorageFilter != 0) designated = true;
            if (!designated) return;
            int open = 0;
            foreach (Job j in _world.Jobs.Values)
                if (j.IsActive && j.Type == JobType.Haul && j.Priority == JobPriority.Optional) open++;
            foreach (Structure from in storages)
            {
                foreach (KeyValuePair<string, int> kv in from.Storage.Totals())
                {
                    if (open >= MaxTidyJobs) return;
                    ItemCategory category = _content.Items.Get(kv.Key).Category;
                    bool wrongPlace = !from.Accepts(category);
                    // From general storage only into a crate designated for this kind of item.
                    if (!wrongPlace && from.StorageFilter != 0) continue;
                    int qty = Math.Min(from.Storage.Available(kv.Key) - PendingOutgoing(from.Storage.Id, kv.Key), MaxHaulQuantity);
                    if (qty <= 0) continue;
                    Structure dest = BestStorageFor(kv.Key, qty, storages, from.Position, from);
                    if (dest == null || (!wrongPlace && dest.StorageFilter == 0)) continue;
                    AddJob(new Job
                    {
                        Type = JobType.Haul, Priority = JobPriority.Optional, TargetStructure = dest.Id, SourceInventory = from.Storage.Id,
                        DestinationInventory = dest.Storage.Id, ItemId = kv.Key, Quantity = Math.Min(qty, dest.Storage.SpaceFor(kv.Key)), CreatedAt = now,
                        Description = $"Put away {_content.Items.NameOf(kv.Key)} in {_content.Building(dest.DefId)?.Name}"
                    });
                    open++;
                }
            }
        }

        // ------------------------------------------------------------------ gathering

        /// <summary>
        /// One gather job per node, up to one per colonist who gathers. A node is worked only if it lies in
        /// a zone that allows its kind, its yield has somewhere to be stored, and any tool it needs is in storage.
        /// </summary>
        private void PlanGathering(List<Structure> storages, double now)
        {
            if (_world.WorkZones.Count == 0) return;
            int gatherers = 0;
            foreach (ColonistState c in _world.Colonists.Values)
                if (c.Recruited && c.Activity != ColonistActivity.Down && c.Work.PriorityFor(JobType.Gather) > 0) gatherers++;
            var busy = new HashSet<EntityId>();
            int active = 0;
            foreach (Job j in _world.Jobs.Values)
            {
                if (j.Type != JobType.Gather || !j.IsActive) continue;
                busy.Add(j.TargetNode);
                active++;
            }
            var zoneIds = new List<EntityId>(_world.WorkZones.Keys);
            zoneIds.Sort();
            var nodeIds = new List<EntityId>(_world.ResourceNodes.Keys);
            nodeIds.Sort();
            foreach (EntityId zid in zoneIds)
            {
                WorkZone zone = _world.WorkZones[zid];
                int found = 0;
                string missing = null;
                foreach (EntityId nid in nodeIds)
                {
                    ResourceNodeState node = _world.ResourceNodes[nid];
                    if (node.Depleted || !zone.Contains(node.Position)) continue;
                    ResourceNodeDefinition def = _content.Node(node.DefId);
                    if (def == null || (GatherRules.KindsOf(def, _content.Items) & zone.Gather) == 0) continue;
                    found++;
                    if (busy.Contains(nid) || active >= gatherers) continue;
                    if (BestStorageFor(def.Yields[0].ItemId, 1, storages, node.Position) == null)
                    {
                        missing = "have no storage space for what they yield";
                        continue;
                    }
                    Structure toolStore = null;
                    string toolId = null;
                    if (def.RequiredTool != ToolType.None && !FindTool(def.RequiredTool, storages, out toolStore, out toolId))
                    {
                        missing = $"need {WithArticle(HarvestRules.ToolName(def.RequiredTool))} in storage";
                        continue;
                    }
                    var job = new Job
                    {
                        Type = JobType.Gather, Priority = JobPriority.Productive, TargetNode = nid, CreatedAt = now,
                        Description = $"{def.Verb} {def.Name}"
                    };
                    if (toolStore != null)
                    {
                        job.SourceInventory = toolStore.Storage.Id;
                        job.ItemId = toolId;
                        job.Quantity = 1;
                    }
                    AddJob(job);
                    busy.Add(nid);
                    active++;
                }
                zone.Status = found == 0 ? $"Nothing to gather ({GatherRules.Describe(zone.Gather)})"
                    : gatherers == 0 ? $"{found} to gather — no colonist has Gather work"
                    : missing != null ? $"{found} to gather — some {missing}" : $"{found} to gather";
            }
        }

        private static string WithArticle(string noun) => ("aeiou".IndexOf(char.ToLowerInvariant(noun[0])) >= 0 ? "an " : "a ") + noun;

        /// <summary>The best tool of a type that storage holds and nobody has claimed.</summary>
        private bool FindTool(ToolType type, List<Structure> storages, out Structure store, out string itemId)
        {
            store = null;
            itemId = null;
            float bestPower = 0f;
            foreach (Structure st in storages)
            {
                foreach (KeyValuePair<string, int> kv in st.Storage.Totals())
                {
                    ItemDefinition d = _content.Items.Get(kv.Key);
                    if (d.Tool == null || d.Tool.Type != type || d.Tool.GatherPower <= bestPower) continue;
                    if (st.Storage.Available(d.Id) - PendingOutgoing(st.Storage.Id, d.Id) <= 0) continue;
                    store = st;
                    itemId = d.Id;
                    bestPower = d.Tool.GatherPower;
                }
            }
            return store != null;
        }

        private void ExecuteGather(ColonistState c, Job j, float dt)
        {
            if (!_world.ResourceNodes.TryGetValue(j.TargetNode, out ResourceNodeState node))
            {
                Fail(c, j, "the resource is gone");
                return;
            }
            if (j.SourceInventory.IsValid && j.Stage == HaulStage.ToSource)
            {
                // Fetch the tool from storage first.
                Inventory src = _world.FindInventory(j.SourceInventory);
                if (src == null || !src.HasReservation(j.SourceToken))
                {
                    Fail(c, j, $"the {_content.Items.NameOf(j.ItemId)} is no longer in storage");
                    return;
                }
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, SourceArrive(j.SourceInventory))) return;
                var taken = new List<ItemStack>();
                if (!c.Carry.CanAccept(j.ItemId, 1) || !src.CommitOutgoing(j.SourceToken, taken))
                {
                    Fail(c, j, "could not take the tool");
                    return;
                }
                foreach (ItemStack st in taken) c.Carry.TryAddStack(st);
                j.SourceToken = default;
                j.Stage = HaulStage.ToDestination;
                return;
            }
            ResourceNodeDefinition def = _content.Node(node.DefId);
            if (node.Depleted)
            {
                if (j.Harvests > 0) Complete(c, j);
                else Fail(c, j, "nothing left to gather there");
                return;
            }
            if (!MoveTo(c, node.Position, dt, 1.2f + 0.6f * node.Scale)) return;
            c.Activity = ColonistActivity.Working;
            int toolSlot = ToolSlot(c.Carry, def);
            ItemDefinition tool = toolSlot >= 0 ? _content.Items.Get(c.Carry.GetSlot(toolSlot).ItemId) : null;
            if (def.RequiredTool != ToolType.None && tool == null)
            {
                Fail(c, j, $"no {HarvestRules.ToolName(def.RequiredTool)} to work with");
                return;
            }
            j.Progress += dt * c.Skill(JobType.Gather);
            float action = HarvestRules.ActionSeconds(def, tool) * GatherSlowdown;
            if (j.Progress < action) return;
            j.Progress -= action;
            var gained = new List<ItemAmount>();
            OpResult r = HarvestRules.Harvest(_world, _content, node, tool, c.Carry, _world.SessionRng, gained);
            if (!r.Ok)
            {
                if (r.Code == "inventory_full" && j.Harvests > 0) Complete(c, j);
                else Fail(c, j, r.Message);
                return;
            }
            j.Harvests++;
            if (tool?.Tool != null && c.Carry.Wear(toolSlot, tool.Tool.WearPerUse))
                _bus?.Publish(new Notification($"{c.Name}'s {tool.Name} broke.", Severity.Warning));
            EmitNoise?.Invoke(node.Position, def.NoiseRadius, "harvest");
            if (node.Depleted || j.Harvests >= GatherHarvestsPerTrip || c.Carry.TotalWeight > CarryKg * 0.8f) Complete(c, j);
        }

        /// <summary>The carried tool that suits a node best (its preferred tool, else its required one), or -1.</summary>
        private int ToolSlot(Inventory carry, ResourceNodeDefinition def)
        {
            int best = -1;
            float bestPower = 0f;
            for (int i = 0; i < carry.SlotCount; i++)
            {
                ItemStack st = carry.GetSlot(i);
                if (st.IsEmpty) continue;
                ToolStats t = _content.Items.Get(st.ItemId).Tool;
                if (t == null || (t.Type != def.PreferredTool && t.Type != def.RequiredTool) || t.Type == ToolType.None) continue;
                if (t.GatherPower <= bestPower) continue;
                best = i;
                bestPower = t.GatherPower;
            }
            return best;
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
                    int missing = target - s.Machine.Input.Count(input.ItemId) - PendingQuantity(s.Id, input.ItemId, s.Machine.Input.Id);
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
            if (def?.Farm == null) return;
            string name = def.Name;
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
            if (have + PendingQuantity(machineId, null, fuel.Id) >= 2) return;
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
                    if (c.Work.PriorityFor(JobType.Guard) > 0 && GuardWeapon(c.Carry, out int ws) != null && ws == i) continue; // a guard keeps their weapon
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
            if (c.Vitals.Warmth < WarmUpThreshold && c.FeltTemperatureC < _content.Survival.ComfortTemperatureC && TryWarmUp(c, now)) return true;
            if (_world.Clock.IsNight && c.HomeBed.IsValid && _world.Structures.ContainsKey(c.HomeBed))
            {
                var rest = new Job { Type = JobType.Rest, Priority = JobPriority.Survival, TargetStructure = c.HomeBed, Description = "Sleep until morning", CreatedAt = now };
                AddJob(rest);
                return Claim(c, rest);
            }
            return false;
        }

        /// <summary>
        /// A cold colonist heads for the nearest burning fire or working furnace, or failing that a
        /// bed under a roof. With neither, they keep working and say why they are freezing.
        /// </summary>
        private bool TryWarmUp(ColonistState c, double now)
        {
            Structure best = FindWarmSpot(c);
            if (best == null)
            {
                const string reason = "Freezing: no fire burning and no bed under a roof";
                c.Status = reason;
                if (c.RecentFailures.Count == 0 || c.RecentFailures[0] != reason) c.RecordFailure(reason);
                return false;
            }
            var job = new Job
            {
                Type = JobType.WarmUp, Priority = JobPriority.Survival, TargetStructure = best.Id, CreatedAt = now,
                Description = $"Warm up at the {_content.Building(best.DefId)?.Name ?? "fire"}"
            };
            AddJob(job);
            if (Claim(c, job)) return true;
            _world.Jobs.Remove(job.Id);
            return false;
        }

        /// <summary>The nearest burning fire or working furnace in reach, else the colonist's bed if it is under a roof.</summary>
        private Structure FindWarmSpot(ColonistState c)
        {
            Structure best = null;
            float bestDist = WarmUpSearchRadius;
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (!s.PlayerBuilt || !s.IsComplete) continue;
                if (Climate.HeatAt(s, _content.Building(s.DefId), s.Position) <= 0f) continue;
                float d = Vec3.DistanceXZ(s.Position, c.Position);
                if (d >= bestDist) continue;
                best = s;
                bestDist = d;
            }
            if (best == null && c.HomeBed.IsValid && _world.Structures.TryGetValue(c.HomeBed, out Structure bed) && Climate.IsSheltered(_world, _content, bed.Position + new Vec3(0f, 0.1f, 0f)))
                best = bed;
            return best;
        }

        private void ExecuteWarmUp(ColonistState c, Job j, float dt)
        {
            if (!_world.Structures.TryGetValue(j.TargetStructure, out Structure spot))
            {
                Fail(c, j, "the fire is gone");
                return;
            }
            BuildingDefinition def = _content.Building(spot.DefId);
            bool bed = def != null && def.IsBed;
            if (!bed && Climate.HeatAt(spot, def, spot.Position) <= 0f)
            {
                Fail(c, j, "the fire went out");
                return;
            }
            float arrive = bed ? 1.2f : Math.Max(1.2f, (def?.Heater?.Radius ?? Climate.FurnaceHeatRadius) * 0.35f);
            if (!MoveTo(c, spot.Position, dt, arrive)) return;
            c.Activity = ColonistActivity.Idle;
            c.Status = $"Warming up at the {def?.Name ?? "fire"}";
            if (c.Vitals.Warmth >= WarmEnough) Complete(c, j);
        }

        /// <summary>Claims a job for a colonist, acquiring every reservation it needs (all or nothing).</summary>
        public bool Claim(ColonistState c, Job j)
        {
            if (j.State != JobState.Open || j.AssigneeId.IsValid) return false;
            if (j.Type == JobType.Haul || j.Type == JobType.Eat || j.Type == JobType.Medic || ((j.Type == JobType.Farm || j.Type == JobType.Gather) && j.SourceInventory.IsValid))
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
                case JobType.Mechanic:
                    ExecuteRepair(c, j, dt);
                    break;
                case JobType.Medic:
                    ExecuteTreat(c, j, dt);
                    break;
                case JobType.Eat:
                    ExecuteEat(c, j, dt);
                    break;
                case JobType.Farm:
                    ExecuteFarm(c, j, dt);
                    break;
                case JobType.WarmUp:
                    ExecuteWarmUp(c, j, dt);
                    break;
                case JobType.Gather:
                    ExecuteGather(c, j, dt);
                    break;
                case JobType.Rest:
                    if (!_world.Structures.TryGetValue(j.TargetStructure, out Structure bed))
                    {
                        Fail(c, j, "their bed was destroyed");
                        return;
                    }
                    if (c.Vitals.Warmth < WarmUpThreshold && c.FeltTemperatureC < _content.Survival.ComfortTemperatureC && FindWarmSpot(c) is Structure warm && warm.Id != bed.Id)
                    {
                        Fail(c, j, "too cold to sleep there");
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
                    Fail(c, j, src == null ? "the source container is gone" : "the reserved items are no longer there");
                    return;
                }
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, SourceArrive(j.SourceInventory))) return;
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
                if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, SourceArrive(j.SourceInventory))) return;
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
            if (!MoveTo(c, InventoryPosition(j.SourceInventory, c), dt, SourceArrive(j.SourceInventory))) return;
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

        private bool FarFromHome(ColonistState c, out Structure home)
        {
            home = null;
            return c.HomeBed.IsValid && _world.Structures.TryGetValue(c.HomeBed, out home) && Vec3.DistanceXZ(c.Position, home.Position) > HomeRadius;
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
        /// <summary>
        /// Quantity that active hauls are bringing to a structure. Pass the destination inventory to
        /// count only hauls into it: a machine's fuel and its ingredients are planned separately, and
        /// wood hauled as fuel must not count toward a recipe that also takes wood.
        /// </summary>
        private int PendingQuantity(EntityId target, string itemId, EntityId destination = default)
        {
            int n = 0;
            foreach (Job j in _world.Jobs.Values)
                if (j.IsActive && j.Type == JobType.Haul && j.TargetStructure == target && (itemId == null || j.ItemId == itemId)
                    && (!destination.IsValid || j.DestinationInventory == destination)) n += j.Quantity;
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

        /// <summary>
        /// The storage to put an item in: one that takes its category and has room, designated storage
        /// (a crate set to take this kind of item) before general storage, then the nearest.
        /// </summary>
        private Structure BestStorageFor(string itemId, int quantity, List<Structure> storages, Vec3 near, Structure except = null)
        {
            Structure best = null;
            float bestD = float.MaxValue;
            ItemCategory category = _content.Items.Get(itemId).Category;
            foreach (Structure s in storages)
            {
                if (s == except || !s.Accepts(category) || s.Storage.SpaceFor(itemId) < Math.Min(quantity, 1)) continue;
                float d = Vec3.Distance(s.Position, near) + (s.StorageFilter != 0 ? 0f : DesignatedPreference);
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
            if ((j.Type == JobType.Haul || j.Type == JobType.Eat || j.Type == JobType.Gather) && j.SourceInventory.IsValid) return InventoryPosition(j.SourceInventory, null);
            if (j.Type == JobType.Gather && _world.ResourceNodes.TryGetValue(j.TargetNode, out ResourceNodeState n)) return n.Position;
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

        /// <summary>
        /// How close a colonist must get to a container to use it: to the edge of a structure's footprint,
        /// as for deliveries, so a machine boxed in by its neighbours can still be emptied from any side.
        /// </summary>
        private float SourceArrive(EntityId inventory)
        {
            return _world.Structures.TryGetValue(_world.OwnerOf(inventory), out Structure s) ? ArriveDistance + FootprintRadius(s) : ArriveDistance;
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
