using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.Power;
using Afterfall.Domain.World;

namespace Afterfall.Persistence
{
    /// <summary>Explicit, field-by-field mapping between the runtime world and the save schema.</summary>
    public static class WorldStateMapper
    {
        // ================================================================== to DTO

        public static WorldDto ToDto(WorldState w)
        {
            var dto = new WorldDto
            {
                Seed = w.Seed,
                Settings = w.Settings,
                ElapsedSeconds = w.Clock.ElapsedSeconds,
                DayLengthSeconds = w.Clock.DayLengthSeconds,
                StartTimeOfDay = w.Clock.StartTimeOfDay,
                NextId = w.Ids.NextValue,
                RngState = w.Rng.State,
                Player = ToDto(w.Player),
                Threat = w.Threat,
                Metrics = w.Metrics,
                ExploredCells = PackBits(w.Exploration.Revealed)
            };
            foreach (EntityId id in w.SortedStructureIds()) dto.Structures.Add(ToDto(w.Structures[id]));
            foreach (PowerLink l in w.PowerLinks) dto.PowerLinks.Add(new[] { l.A.Value, l.B.Value });
            foreach (ResourceNodeState n in Sorted(w.ResourceNodes))
                dto.Nodes.Add(new NodeDto { Id = n.Id.Value, DefId = n.DefId, Position = n.Position, Yaw = n.Yaw, Scale = n.Scale, Remaining = n.RemainingHarvests, Depleted = n.Depleted, RespawnAt = n.RespawnAt });
            foreach (WorldDrop d in Sorted(w.Drops))
                dto.Drops.Add(new DropDto { Id = d.Id.Value, Position = d.Position, Contents = ToDto(d.Contents), Label = d.Label, IsBelongings = d.IsBelongings });
            foreach (ZombieState z in Sorted(w.Zombies))
            {
                dto.Zombies.Add(new ZombieDto
                {
                    Id = z.Id.Value, Archetype = z.Archetype, Position = z.Position, Yaw = z.Yaw, Health = z.Health, Mode = z.Mode,
                    Home = z.Home, MoveTarget = z.MoveTarget, LastKnownTarget = z.LastKnownTarget, TimeSinceSeen = z.TimeSinceSeen, DeadTime = z.DeadTime
                });
            }
            foreach (ColonistState c in Sorted(w.Colonists))
            {
                var cd = new ColonistDto
                {
                    Id = c.Id.Value, Name = c.Name, Recruited = c.Recruited, Position = c.Position, Yaw = c.Yaw, Vitals = c.Vitals, Carry = ToDto(c.Carry),
                    CurrentJob = c.CurrentJob.Value, Activity = c.Activity.ToString(), Status = c.Status, HomeBed = c.HomeBed.Value,
                    MoveTarget = c.MoveTarget, HasMoveTarget = c.HasMoveTarget, Morale = c.Morale, RecentFailures = new List<string>(c.RecentFailures)
                };
                foreach (KeyValuePair<JobType, float> kv in c.Skills) cd.Skills[kv.Key.ToString()] = kv.Value;
                foreach (KeyValuePair<JobType, int> kv in c.Work.Priorities) cd.Work[kv.Key.ToString()] = kv.Value;
                dto.Colonists.Add(cd);
            }
            foreach (Job j in Sorted(w.Jobs))
            {
                dto.Jobs.Add(new JobDto
                {
                    Id = j.Id.Value, Type = j.Type.ToString(), Priority = j.Priority, State = j.State.ToString(), Assignee = j.AssigneeId.Value,
                    Description = j.Description, FailureReason = j.FailureReason, CreatedAt = j.CreatedAt, TargetStructure = j.TargetStructure.Value,
                    SourceInventory = j.SourceInventory.Value, DestinationInventory = j.DestinationInventory.Value, ItemId = j.ItemId, Quantity = j.Quantity,
                    Stage = j.Stage.ToString(), SourceToken = j.SourceToken.Id, DestinationToken = j.DestinationToken.Id, DeliverToBlueprint = j.DeliverToBlueprint
                });
            }
            dto.UnlockedTechs.AddRange(w.Tech.Unlocked);
            dto.UnlockedTechs.Sort(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> kv in w.Tech.Progress) dto.TechProgress[kv.Key] = kv.Value;
            dto.DiscoveredPois.AddRange(w.Exploration.DiscoveredPois);
            dto.DiscoveredPois.Sort(StringComparer.Ordinal);
            return dto;
        }

        private static PlayerDto ToDto(PlayerState p)
        {
            if (p == null) return null;
            return new PlayerDto
            {
                Id = p.Id.Value,
                Position = p.Position,
                Yaw = p.Yaw,
                Vitals = p.Vitals,
                Inventory = ToDto(p.Inventory),
                Crafting = new List<Domain.Crafting.CraftJob>(p.Crafting.Jobs),
                SelectedHotbar = p.SelectedHotbar,
                SpawnStructureId = p.SpawnStructureId.Value,
                WorldSpawn = p.WorldSpawn,
                Deaths = p.Deaths,
                NextActionTime = p.NextActionTime
            };
        }

        private static StructureDto ToDto(Structure s)
        {
            var dto = new StructureDto
            {
                Id = s.Id.Value,
                DefId = s.DefId,
                Position = s.Position,
                Yaw = s.Yaw,
                State = s.State.ToString(),
                WorkDone = s.WorkDone,
                Health = s.Health,
                PlayerBuilt = s.PlayerBuilt,
                PoiId = s.PoiId,
                Searched = s.Searched,
                DoorOpen = s.DoorOpen,
                Enabled = s.Enabled,
                Storage = ToDto(s.Storage)
            };
            foreach (KeyValuePair<string, int> kv in s.Delivered) dto.Delivered[kv.Key] = kv.Value;
            if (s.Machine != null)
            {
                MachineState m = s.Machine;
                dto.Machine = new MachineDto
                {
                    RecipeId = m.RecipeId, Input = ToDto(m.Input), Output = ToDto(m.Output), Fuel = ToDto(m.Fuel), Progress = m.Progress,
                    CycleActive = m.CycleActive, FuelBurnRemaining = m.FuelBurnRemaining, Status = m.Status.ToString(), StatusDetail = m.StatusDetail,
                    RequestingPower = m.RequestingPower, CompletedCycles = m.CompletedCycles, LastCycleCompletedAt = m.LastCycleCompletedAt
                };
            }
            if (s.Generator != null)
            {
                dto.Generator = new GeneratorDto
                {
                    Fuel = ToDto(s.Generator.Fuel), BufferJoules = s.Generator.BufferJoules, Running = s.Generator.Running,
                    OutputWatts = s.Generator.OutputWatts, Status = s.Generator.Status
                };
            }
            if (s.Battery != null) dto.BatteryStoredJoules = s.Battery.StoredJoules;
            if (s.Crop != null) dto.Crop = new Afterfall.Domain.Farming.CropState { SeedId = s.Crop.SeedId, Growth = s.Crop.Growth, Water = s.Crop.Water, Irrigated = s.Crop.Irrigated };
            if (s.Power != null) dto.Power = new PowerDto { Powered = s.Power.Powered, Reason = s.Power.Reason, DemandWatts = s.Power.DemandWatts };
            return dto;
        }

        public static InventoryDto ToDto(Inventory inv)
        {
            if (inv == null) return null;
            var dto = new InventoryDto { Id = inv.Id.Value, Label = inv.Label, Slots = inv.SlotCount, MaxWeight = inv.MaxWeight };
            for (int i = 0; i < inv.SlotCount; i++)
            {
                ItemStack st = inv.GetSlot(i);
                if (!st.IsEmpty) dto.Items.Add(new SlotDto { Index = i, ItemId = st.ItemId, Quantity = st.Quantity, Durability = st.Durability });
            }
            foreach (ReservationRecord r in inv.Reservations)
                dto.Reservations.Add(new ReservationRecord { Id = r.Id, Kind = r.Kind, ItemId = r.ItemId, Quantity = r.Quantity });
            dto.Reservations.Sort((a, b) => a.Id.CompareTo(b.Id));
            return dto;
        }

        // ================================================================== from DTO

        public static WorldState FromDto(WorldDto dto, ContentDatabase content, List<string> warnings)
        {
            var clock = new SimulationClock(dto.DayLengthSeconds, dto.ElapsedSeconds, dto.StartTimeOfDay);
            var w = new WorldState(dto.Seed, dto.Settings ?? GameSettings.FromPreset(DifficultyPreset.Survival), clock, new IdAllocator(dto.NextId));
            w.Rng.State = dto.RngState;
            w.Layout = WorldGenerator.Generate(dto.Seed);

            if (dto.Player != null)
            {
                PlayerDto pd = dto.Player;
                var p = new PlayerState
                {
                    Id = new EntityId(pd.Id),
                    Position = pd.Position,
                    Yaw = pd.Yaw,
                    Vitals = pd.Vitals ?? new Domain.Survival.SurvivalState(),
                    SelectedHotbar = pd.SelectedHotbar,
                    SpawnStructureId = new EntityId(pd.SpawnStructureId),
                    WorldSpawn = pd.WorldSpawn,
                    Deaths = pd.Deaths,
                    NextActionTime = pd.NextActionTime
                };
                p.Inventory = FromDto(pd.Inventory, content, null, warnings) ?? new Inventory(w.Ids.Allocate(), content.Items, PlayerState.BackpackSlots, PlayerState.CarryMaxKg, null, "Backpack");
                foreach (var job in pd.Crafting ?? new List<Domain.Crafting.CraftJob>())
                {
                    if (content.Recipe(job.RecipeId) != null) p.Crafting.Jobs.Add(job);
                    else warnings?.Add($"Dropped crafting job for unknown recipe '{job.RecipeId}'.");
                }
                w.Player = p;
                w.Ids.EnsureAbove(p.Id);
                w.RegisterInventory(p.Inventory, p.Id);
            }

            foreach (StructureDto sd in dto.Structures)
            {
                BuildingDefinition def = content.Building(sd.DefId);
                if (def == null)
                {
                    warnings?.Add($"Skipped structure {sd.Id}: unknown building '{sd.DefId}'.");
                    continue;
                }
                var s = new Structure
                {
                    Id = new EntityId(sd.Id),
                    DefId = sd.DefId,
                    Position = sd.Position,
                    Yaw = sd.Yaw,
                    State = Enum.TryParse(sd.State, out StructureState st) ? st : StructureState.Blueprint,
                    WorkDone = sd.WorkDone,
                    Health = sd.Health,
                    PlayerBuilt = sd.PlayerBuilt,
                    PoiId = sd.PoiId ?? "",
                    Searched = sd.Searched,
                    DoorOpen = sd.DoorOpen,
                    Enabled = sd.Enabled
                };
                foreach (KeyValuePair<string, int> kv in sd.Delivered ?? new Dictionary<string, int>())
                    if (content.Items.Contains(kv.Key) && kv.Value > 0) s.Delivered[kv.Key] = kv.Value;

                if (s.IsComplete)
                {
                    var ids = new Dictionary<string, long>
                    {
                        ["storage"] = sd.Storage?.Id ?? 0,
                        ["input"] = sd.Machine?.Input?.Id ?? 0,
                        ["output"] = sd.Machine?.Output?.Id ?? 0,
                        ["fuel"] = sd.Machine?.Fuel?.Id ?? 0,
                        ["generator_fuel"] = sd.Generator?.Fuel?.Id ?? 0
                    };
                    StructureFactory.InitializeComponents(content, s, role => ids.TryGetValue(role, out long v) && v > 0 ? new EntityId(v) : w.Ids.Allocate());
                    Fill(s.Storage, sd.Storage, content, warnings);
                    if (s.Machine != null && sd.Machine != null)
                    {
                        MachineDto md = sd.Machine;
                        Fill(s.Machine.Input, md.Input, content, warnings);
                        Fill(s.Machine.Output, md.Output, content, warnings);
                        Fill(s.Machine.Fuel, md.Fuel, content, warnings);
                        s.Machine.RecipeId = content.Recipe(md.RecipeId) != null ? md.RecipeId : "";
                        s.Machine.Progress = md.Progress;
                        s.Machine.CycleActive = md.CycleActive && s.Machine.RecipeId.Length > 0;
                        s.Machine.FuelBurnRemaining = md.FuelBurnRemaining;
                        s.Machine.Status = Enum.TryParse(md.Status, out MachineStatus ms) ? ms : MachineStatus.Idle;
                        s.Machine.StatusDetail = md.StatusDetail ?? "";
                        s.Machine.RequestingPower = md.RequestingPower;
                        s.Machine.CompletedCycles = md.CompletedCycles;
                        s.Machine.LastCycleCompletedAt = md.LastCycleCompletedAt;
                    }
                    if (s.Generator != null && sd.Generator != null)
                    {
                        Fill(s.Generator.Fuel, sd.Generator.Fuel, content, warnings);
                        s.Generator.BufferJoules = sd.Generator.BufferJoules;
                        s.Generator.Running = sd.Generator.Running;
                        s.Generator.OutputWatts = sd.Generator.OutputWatts;
                        s.Generator.Status = sd.Generator.Status ?? "";
                    }
                    if (s.Battery != null && sd.BatteryStoredJoules.HasValue) s.Battery.StoredJoules = sd.BatteryStoredJoules.Value;
                    if (s.Crop != null && sd.Crop != null)
                    {
                        s.Crop.SeedId = content.Items.Contains(sd.Crop.SeedId) ? sd.Crop.SeedId : "";
                        s.Crop.Growth = sd.Crop.Growth;
                        s.Crop.Water = sd.Crop.Water;
                        s.Crop.Irrigated = sd.Crop.Irrigated;
                    }
                    if (s.Power != null && sd.Power != null)
                    {
                        s.Power.Powered = sd.Power.Powered;
                        s.Power.Reason = sd.Power.Reason ?? "";
                        s.Power.DemandWatts = sd.Power.DemandWatts;
                    }
                }
                w.AddStructure(s);
            }

            foreach (long[] link in dto.PowerLinks)
            {
                if (link == null || link.Length != 2) continue;
                var a = new EntityId(link[0]);
                var b = new EntityId(link[1]);
                if (w.Structures.ContainsKey(a) && w.Structures.ContainsKey(b) && !w.HasPowerLink(a, b)) w.PowerLinks.Add(new PowerLink(a, b));
            }
            w.MarkPowerTopologyChanged();

            foreach (NodeDto nd in dto.Nodes)
            {
                if (content.Node(nd.DefId) == null)
                {
                    warnings?.Add($"Skipped node {nd.Id}: unknown definition '{nd.DefId}'.");
                    continue;
                }
                w.AddNode(new ResourceNodeState { Id = new EntityId(nd.Id), DefId = nd.DefId, Position = nd.Position, Yaw = nd.Yaw, Scale = nd.Scale, RemainingHarvests = nd.Remaining, Depleted = nd.Depleted, RespawnAt = nd.RespawnAt });
            }

            foreach (DropDto dd in dto.Drops)
            {
                Inventory inv = FromDto(dd.Contents, content, null, warnings);
                if (inv == null) continue;
                w.AddDrop(new WorldDrop { Id = new EntityId(dd.Id), Position = dd.Position, Contents = inv, Label = dd.Label ?? "", IsBelongings = dd.IsBelongings });
            }

            foreach (ZombieDto zd in dto.Zombies)
            {
                var z = new ZombieState
                {
                    Id = new EntityId(zd.Id), Archetype = zd.Archetype, Position = zd.Position, Yaw = zd.Yaw, Health = zd.Health, Mode = zd.Mode,
                    Home = zd.Home, MoveTarget = zd.MoveTarget, LastKnownTarget = zd.LastKnownTarget, TimeSinceSeen = zd.TimeSinceSeen, DeadTime = zd.DeadTime
                };
                w.Zombies[z.Id] = z;
                w.Ids.EnsureAbove(z.Id);
            }

            foreach (ColonistDto cd in dto.Colonists ?? new List<ColonistDto>())
            {
                var c = new ColonistState
                {
                    Id = new EntityId(cd.Id), Name = cd.Name ?? "", Recruited = cd.Recruited, Position = cd.Position, Yaw = cd.Yaw,
                    Vitals = cd.Vitals ?? new Domain.Survival.SurvivalState(), CurrentJob = new EntityId(cd.CurrentJob),
                    Activity = Enum.TryParse(cd.Activity, out ColonistActivity act) ? act : ColonistActivity.Idle, Status = cd.Status ?? "",
                    HomeBed = new EntityId(cd.HomeBed), MoveTarget = cd.MoveTarget, HasMoveTarget = cd.HasMoveTarget, Morale = cd.Morale
                };
                c.Carry = FromDto(cd.Carry, content, null, warnings) ?? new Inventory(w.Ids.Allocate(), content.Items, ColonySystem.CarrySlots, ColonySystem.CarryKg, null, c.Name + "'s pack");
                foreach (KeyValuePair<string, float> kv in cd.Skills ?? new Dictionary<string, float>())
                    if (Enum.TryParse(kv.Key, out JobType jt)) c.Skills[jt] = kv.Value;
                foreach (KeyValuePair<string, int> kv in cd.Work ?? new Dictionary<string, int>())
                    if (Enum.TryParse(kv.Key, out JobType jt)) c.Work.Priorities[jt] = kv.Value;
                c.RecentFailures.AddRange(cd.RecentFailures ?? new List<string>());
                w.AddColonist(c);
            }
            foreach (JobDto jd in dto.Jobs ?? new List<JobDto>())
            {
                if (!Enum.TryParse(jd.Type, out JobType type)) continue;
                var j = new Job
                {
                    Id = new EntityId(jd.Id), Type = type, Priority = jd.Priority, State = Enum.TryParse(jd.State, out JobState js) ? js : JobState.Failed,
                    AssigneeId = new EntityId(jd.Assignee), Description = jd.Description ?? "", FailureReason = jd.FailureReason ?? "", CreatedAt = jd.CreatedAt,
                    TargetStructure = new EntityId(jd.TargetStructure), SourceInventory = new EntityId(jd.SourceInventory), DestinationInventory = new EntityId(jd.DestinationInventory),
                    ItemId = jd.ItemId ?? "", Quantity = jd.Quantity, Stage = Enum.TryParse(jd.Stage, out HaulStage hs) ? hs : HaulStage.ToSource,
                    SourceToken = jd.SourceToken > 0 ? new ReservationToken(new EntityId(jd.SourceInventory), jd.SourceToken) : default,
                    DestinationToken = jd.DestinationToken > 0 ? new ReservationToken(new EntityId(jd.DestinationInventory), jd.DestinationToken) : default,
                    DeliverToBlueprint = jd.DeliverToBlueprint
                };
                w.Jobs[j.Id] = j;
                w.Ids.EnsureAbove(j.Id);
            }

            foreach (string t in dto.UnlockedTechs) w.Tech.Unlocked.Add(t);
            foreach (KeyValuePair<string, int> kv in dto.TechProgress) w.Tech.Progress[kv.Key] = kv.Value;
            UnpackBits(dto.ExploredCells, w.Exploration.Revealed);
            foreach (string poi in dto.DiscoveredPois) w.Exploration.DiscoveredPois.Add(poi);
            if (dto.Threat != null) CopyThreat(dto.Threat, w.Threat);
            if (dto.Metrics != null) CopyMetrics(dto.Metrics, w.Metrics);
            return w;
        }

        public static Inventory FromDto(InventoryDto dto, ContentDatabase content, InventoryFilter filter, List<string> warnings)
        {
            if (dto == null || dto.Slots <= 0) return null;
            var inv = new Inventory(new EntityId(dto.Id), content.Items, dto.Slots, dto.MaxWeight, filter, dto.Label ?? "");
            Fill(inv, dto, content, warnings);
            return inv;
        }

        private static void Fill(Inventory inv, InventoryDto dto, ContentDatabase content, List<string> warnings)
        {
            if (inv == null || dto == null) return;
            foreach (SlotDto s in dto.Items)
            {
                if (s.Index < 0 || s.Index >= inv.SlotCount)
                {
                    warnings?.Add($"Inventory {dto.Id}: slot {s.Index} out of range; item {s.ItemId} x{s.Quantity} moved to a free slot.");
                    int free = inv.FirstEmptySlot();
                    if (free < 0) continue;
                    s.Index = free;
                }
                if (!content.Items.TryGet(s.ItemId, out ItemDefinition def))
                {
                    warnings?.Add($"Inventory {dto.Id}: unknown item '{s.ItemId}' removed.");
                    continue;
                }
                int qty = Math.Max(0, Math.Min(s.Quantity, def.HasDurability ? 1 : def.MaxStack));
                if (qty != s.Quantity) warnings?.Add($"Inventory {dto.Id}: clamped {s.ItemId} from {s.Quantity} to {qty}.");
                if (qty > 0) inv.SetSlotRaw(s.Index, new ItemStack(s.ItemId, qty, s.Durability));
            }
            foreach (ReservationRecord r in dto.Reservations ?? new List<ReservationRecord>())
                inv.RestoreReservation(new ReservationRecord { Id = r.Id, Kind = r.Kind, ItemId = r.ItemId, Quantity = r.Quantity });
        }

        // ================================================================== helpers

        private static List<T> Sorted<T>(Dictionary<EntityId, T> dict)
        {
            var keys = new List<EntityId>(dict.Keys);
            keys.Sort();
            var list = new List<T>(keys.Count);
            foreach (EntityId k in keys) list.Add(dict[k]);
            return list;
        }

        public static string PackBits(bool[] bits)
        {
            var bytes = new byte[(bits.Length + 7) / 8];
            for (int i = 0; i < bits.Length; i++)
                if (bits[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
            return Convert.ToBase64String(bytes);
        }

        public static void UnpackBits(string packed, bool[] into)
        {
            if (string.IsNullOrEmpty(packed)) return;
            byte[] bytes = Convert.FromBase64String(packed);
            for (int i = 0; i < into.Length && (i >> 3) < bytes.Length; i++)
                into[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
        }

        private static void CopyThreat(ThreatState from, ThreatState to)
        {
            to.Heat = from.Heat;
            to.LastNightWarnedDay = from.LastNightWarnedDay;
            to.LastHeatWarningAt = from.LastHeatWarningAt;
            to.SpawnedTotal = from.SpawnedTotal;
            to.PendingRaidAt = from.PendingRaidAt;
            to.PendingRaidOrigin = from.PendingRaidOrigin;
            to.LastRaidAt = from.LastRaidAt;
            to.LastSpawnAt = from.LastSpawnAt;
        }

        private static void CopyMetrics(PlaythroughMetrics from, PlaythroughMetrics to)
        {
            to.FirstConstructionAt = from.FirstConstructionAt;
            to.FirstMachineOutputAt = from.FirstMachineOutputAt;
            to.FirstPoweredOutputAt = from.FirstPoweredOutputAt;
            to.ItemsCrafted = from.ItemsCrafted;
            to.StructuresBuilt = from.StructuresBuilt;
            to.ZombiesKilled = from.ZombiesKilled;
            to.Deaths = from.Deaths;
            to.Saves = from.Saves;
        }
    }
}
