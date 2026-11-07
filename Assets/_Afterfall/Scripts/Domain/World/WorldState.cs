using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;

namespace Afterfall.Domain.World
{
    /// <summary>An electrical cable between two structures.</summary>
    public struct PowerLink
    {
        public EntityId A;
        public EntityId B;

        public PowerLink(EntityId a, EntityId b)
        {
            // Normalised order so (a,b) and (b,a) are the same link.
            if (a.Value <= b.Value)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Involves(EntityId id) => A == id || B == id;
        public EntityId Other(EntityId id) => A == id ? B : A;
    }

    public sealed class ThreatState
    {
        /// <summary>Accumulated attention from noise, light and industry (decays over time).</summary>
        public float Heat { get; set; }

        public int LastNightWarnedDay { get; set; }
        public double LastHeatWarningAt { get; set; } = -1e9;
        public int SpawnedTotal { get; set; }

        /// <summary>Simulation time at which an announced group arrives (negative = none pending).</summary>
        public double PendingRaidAt { get; set; } = -1;

        public Vec3 PendingRaidOrigin { get; set; }
        public double LastRaidAt { get; set; } = -1e9;
        public double LastSpawnAt { get; set; } = -1e9;
    }

    /// <summary>Playtest metrics tracked in the save (time to first construction, etc.).</summary>
    public sealed class PlaythroughMetrics
    {
        public double FirstConstructionAt { get; set; } = -1;
        public double FirstMachineOutputAt { get; set; } = -1;
        public double FirstPoweredOutputAt { get; set; } = -1;
        public int ItemsCrafted { get; set; }
        public int StructuresBuilt { get; set; }
        public int ZombiesKilled { get; set; }
        public int Deaths { get; set; }
        public int Saves { get; set; }
    }

    /// <summary>
    /// The complete authoritative state of a running game. Everything that must survive
    /// save/load lives here (or is regenerated deterministically from <see cref="Seed"/>).
    /// </summary>
    public sealed class WorldState
    {
        private readonly Dictionary<EntityId, Inventory> _inventories = new Dictionary<EntityId, Inventory>();
        private readonly Dictionary<EntityId, EntityId> _inventoryOwner = new Dictionary<EntityId, EntityId>();

        public WorldState(int seed, GameSettings settings, SimulationClock clock, IdAllocator ids)
        {
            Seed = seed;
            Settings = settings;
            Clock = clock;
            Ids = ids;
            Rng = new DeterministicRandom((ulong)(uint)seed * 2654435761UL + 1);
            Exploration = new ExplorationState(WorldLayout.WorldSize);
        }

        public int Seed { get; }
        public GameSettings Settings { get; }
        public SimulationClock Clock { get; }
        public IdAllocator Ids { get; }
        public DeterministicRandom Rng { get; }

        public PlayerState Player { get; set; }
        public Dictionary<EntityId, Structure> Structures { get; } = new Dictionary<EntityId, Structure>();
        public List<PowerLink> PowerLinks { get; } = new List<PowerLink>();
        public Dictionary<EntityId, ResourceNodeState> ResourceNodes { get; } = new Dictionary<EntityId, ResourceNodeState>();
        public Dictionary<EntityId, WorldDrop> Drops { get; } = new Dictionary<EntityId, WorldDrop>();
        public Dictionary<EntityId, ZombieState> Zombies { get; } = new Dictionary<EntityId, ZombieState>();
        public Dictionary<EntityId, ColonistState> Colonists { get; } = new Dictionary<EntityId, ColonistState>();
        public Dictionary<EntityId, Job> Jobs { get; } = new Dictionary<EntityId, Job>();
        public TechnologyState Tech { get; } = new TechnologyState();
        public ExplorationState Exploration { get; }
        public ThreatState Threat { get; } = new ThreatState();
        public PlaythroughMetrics Metrics { get; } = new PlaythroughMetrics();

        /// <summary>Static world layout regenerated from the seed (not persisted).</summary>
        public WorldLayout Layout { get; set; }

        /// <summary>Incremented whenever structures, drops or nodes are added/removed (view resync).</summary>
        public int Version { get; private set; }

        /// <summary>Incremented whenever power topology may have changed.</summary>
        public int PowerTopologyVersion { get; private set; }

        public void MarkChanged() => Version++;
        public void MarkPowerTopologyChanged() => PowerTopologyVersion++;

        // ------------------------------------------------------------------ inventory registry

        public void RegisterInventory(Inventory inventory, EntityId owner)
        {
            if (inventory == null) return;
            _inventories[inventory.Id] = inventory;
            _inventoryOwner[inventory.Id] = owner;
            Ids.EnsureAbove(inventory.Id);
        }

        public void UnregisterInventory(Inventory inventory)
        {
            if (inventory == null) return;
            _inventories.Remove(inventory.Id);
            _inventoryOwner.Remove(inventory.Id);
        }

        public Inventory FindInventory(EntityId id) => _inventories.TryGetValue(id, out Inventory inv) ? inv : null;

        /// <summary>Owner entity (structure, drop or player marker) of an inventory.</summary>
        public EntityId OwnerOf(EntityId inventoryId) => _inventoryOwner.TryGetValue(inventoryId, out EntityId o) ? o : EntityId.None;

        public IEnumerable<Inventory> AllInventories => _inventories.Values;

        // ------------------------------------------------------------------ entity management

        public void AddStructure(Structure s)
        {
            Structures[s.Id] = s;
            _sortedStructureIds = null;
            Ids.EnsureAbove(s.Id);
            RegisterStructureInventories(s);
            if (s.Power != null) MarkPowerTopologyChanged();
            MarkChanged();
        }

        public void RegisterStructureInventories(Structure s)
        {
            RegisterInventory(s.Storage, s.Id);
            if (s.Machine != null)
            {
                RegisterInventory(s.Machine.Input, s.Id);
                RegisterInventory(s.Machine.Output, s.Id);
                RegisterInventory(s.Machine.Fuel, s.Id);
            }
            if (s.Generator != null) RegisterInventory(s.Generator.Fuel, s.Id);
        }

        public void RemoveStructure(EntityId id)
        {
            if (!Structures.TryGetValue(id, out Structure s)) return;
            UnregisterInventory(s.Storage);
            if (s.Machine != null)
            {
                UnregisterInventory(s.Machine.Input);
                UnregisterInventory(s.Machine.Output);
                UnregisterInventory(s.Machine.Fuel);
            }
            if (s.Generator != null) UnregisterInventory(s.Generator.Fuel);
            Structures.Remove(id);
            _sortedStructureIds = null;
            int removed = PowerLinks.RemoveAll(l => l.Involves(id));
            if (s.Power != null || removed > 0) MarkPowerTopologyChanged();
            if (Player != null && Player.SpawnStructureId == id) Player.SpawnStructureId = EntityId.None;
            MarkChanged();
        }

        public void AddDrop(WorldDrop drop)
        {
            Drops[drop.Id] = drop;
            Ids.EnsureAbove(drop.Id);
            RegisterInventory(drop.Contents, drop.Id);
            MarkChanged();
        }

        public void RemoveDrop(EntityId id)
        {
            if (!Drops.TryGetValue(id, out WorldDrop d)) return;
            UnregisterInventory(d.Contents);
            Drops.Remove(id);
            MarkChanged();
        }

        public void AddColonist(ColonistState c)
        {
            Colonists[c.Id] = c;
            Ids.EnsureAbove(c.Id);
            RegisterInventory(c.Carry, c.Id);
            MarkChanged();
        }

        public void AddNode(ResourceNodeState node)
        {
            ResourceNodes[node.Id] = node;
            Ids.EnsureAbove(node.Id);
            MarkChanged();
        }

        public bool HasPowerLink(EntityId a, EntityId b)
        {
            var key = new PowerLink(a, b);
            foreach (PowerLink l in PowerLinks)
                if (l.A == key.A && l.B == key.B) return true;
            return false;
        }

        private List<EntityId> _sortedStructureIds;

        /// <summary>
        /// Sorted structure ids for deterministic iteration. Cached and replaced (never mutated)
        /// when structures are added or removed, so callers may safely add/remove while iterating;
        /// they must look ids up with TryGetValue if they remove structures mid-loop.
        /// </summary>
        public IReadOnlyList<EntityId> SortedStructureIds()
        {
            if (_sortedStructureIds == null)
            {
                var ids = new List<EntityId>(Structures.Keys);
                ids.Sort();
                _sortedStructureIds = ids;
            }
            return _sortedStructureIds;
        }
    }
}
