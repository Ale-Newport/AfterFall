using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>
    /// Brings a chunked world (version 3) to life around the survivor. Chunks ahead are generated on
    /// worker threads; chunks within <see cref="SeedRadius"/> are seeded once (their containers with
    /// loot, fixtures, vehicles, campfires and infected become world entities, remembered in
    /// <see cref="WorldState.SeededChunks"/>) and get their trees, rocks and plants as resource nodes.
    /// Nodes nobody has touched are dropped again far from the survivor and every colonist, and are
    /// remade from the layout when someone comes back, so the world can be any size.
    /// </summary>
    public sealed class ChunkStreaming
    {
        public const float SeedRadius = 640f;
        public const float PrefetchRadius = 1150f;
        public const float NodeKeepRadius = 1000f;

        /// <summary>Seeded chunks this far from the survivor and every colonist may have their untouched fittings dropped.</summary>
        public const float FittingKeepRadius = 1200f;
        private const float Interval = 0.5f, PruneInterval = 5f;

        private readonly GameSession _session;
        private readonly HashSet<long> _nodesActive = new HashSet<long>();
        private float _timer, _pruneTimer = PruneInterval;

        public ChunkStreaming(GameSession session)
        {
            _session = session;
        }

        private WorldState World => _session.World;
        private WorldLayout Layout => _session.World.Layout;

        /// <summary>Chunks whose resource nodes are in the world now.</summary>
        public int ActiveChunks => _nodesActive.Count;

        public void Update(double dt)
        {
            if (Layout == null || !Layout.IsChunked) return;
            _timer -= (float)dt;
            if (_timer > 0f) return;
            _timer = Interval;
            Vec3 p = World.Player != null ? World.Player.Position : Layout.PlayerSpawn;
            Step(p, false);
            _pruneTimer -= Interval;
            if (_pruneTimer <= 0f)
            {
                _pruneTimer = PruneInterval;
                PruneFittings(p);
                _session.City.Prune(p);
            }
        }

        /// <summary>Makes everything around a point ready now, without waiting for workers (new game, load, teleport).</summary>
        public void Warm(Vec3 around) => Step(around, true);

        private void Step(Vec3 p, bool now)
        {
            WorldLayout l = Layout;
            l.IntegrateReady(16);
            if (!now) ForChunks(p, PrefetchRadius, (x, z) => l.RequestChunk(x, z));
            ForChunks(p, SeedRadius, (x, z) =>
            {
                if (!now && !l.HasChunk(x, z)) return; // its worker will finish soon (never generate during play)
                ChunkContent c = l.EnsureChunk(x, z);
                if (c == null) return;
                if (!World.SeededChunks.Contains(c.Key)) Seed(c);
                else if (World.DormantChunks.Contains(c.Key)) Wake(c);
                ActivateNodes(c);
            });
            DropFarNodes(p);
        }

        private void ForChunks(Vec3 p, float radius, Action<int, int> each)
        {
            int x0 = WorldLayout.ChunkOf(p.X - radius), x1 = WorldLayout.ChunkOf(p.X + radius);
            int z0 = WorldLayout.ChunkOf(p.Z - radius), z1 = WorldLayout.ChunkOf(p.Z + radius);
            float size = WorldLayout.ChunkSize;
            // Nearest chunks first.
            var list = new List<(float D, int X, int Z)>();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = Math.Max(0f, Math.Max(x * size - p.X, p.X - (x + 1) * size));
                    float dz = Math.Max(0f, Math.Max(z * size - p.Z, p.Z - (z + 1) * size));
                    float d = dx * dx + dz * dz;
                    if (d <= radius * radius) list.Add((d, x, z));
                }
            list.Sort((a, b) => a.D.CompareTo(b.D));
            foreach ((_, int x, int z) in list) each(x, z);
        }

        /// <summary>Creates the chunk's containers, fixtures, vehicles, campfires and infected (once per world).</summary>
        private void Seed(ChunkContent c)
        {
            WorldState w = World;
            ContentDatabase content = _session.Content;
            foreach (ContainerSeed cs in c.Containers) CreateContainer(cs);
            BuildingDefinition campfire = content.Building("campfire");
            foreach (PropPlacement prop in c.Props)
            {
                if (prop.Kind != "firepit" || campfire == null) continue;
                Structure fire = StructureFactory.Create(w, campfire, prop.Position, prop.Yaw, StructureState.Complete);
                fire.Enabled = false;
                StructureFactory.InitializeComponents(w, content, fire);
                w.AddStructure(fire);
            }
            WorldSeeding.SeedFixtures(w, content, c.Fixtures, c.Vehicles);
            var zr = DeterministicRandom.Derive(w.Seed, "zombies:" + c.Key);
            Vec3 player = w.Player != null ? w.Player.Position : Layout.PlayerSpawn;
            foreach (Vec3 sp in c.ZombieSpawns)
            {
                if (Vec3.DistanceXZ(sp, player) < 70f) continue;
                if (!zr.Chance(Math.Min(1f, 0.5f * w.Settings.EnemyDensity))) continue;
                _session.Zombies.Spawn(sp, "wanderer", zr);
            }
            w.SeededChunks.Add(c.Key);
            w.MarkChanged();
        }

        /// <summary>A seeded container with its loot (each rolls from its own key, so it is the same every time it is made).</summary>
        private void CreateContainer(ContainerSeed cs)
        {
            WorldState w = World;
            ContentDatabase content = _session.Content;
            BuildingDefinition def = content.Building(cs.DefId);
            if (def == null) return;
            Structure s = StructureFactory.Create(w, def, cs.Position, cs.Yaw, StructureState.Complete);
            s.PlayerBuilt = false;
            s.PoiId = cs.PoiId;
            s.SeedKey = cs.Key;
            StructureFactory.InitializeComponents(w, content, s);
            if (s.Storage != null && content.LootTables.TryGetValue(cs.LootTable, out LootTable table))
            {
                var rng = DeterministicRandom.Derive(w.Seed, "loot:" + cs.Key);
                foreach (ItemAmount a in table.Roll(rng, w.Settings.LootAbundance)) s.Storage.AddUpTo(a.ItemId, a.Quantity);
            }
            s.Touched = false;
            w.AddStructure(s);
        }

        /// <summary>Makes a dormant chunk's containers and fittings again, exactly as they were generated.</summary>
        private void Wake(ChunkContent c)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (Structure s in World.Structures.Values)
                if (!string.IsNullOrEmpty(s.SeedKey)) present.Add(s.SeedKey);
            foreach (ContainerSeed cs in c.Containers)
                if (!present.Contains(cs.Key)) CreateContainer(cs);
            WorldSeeding.SeedFixtures(World, _session.Content, c.Fixtures, new List<VehicleSeed>());
            World.DormantChunks.Remove(c.Key);
            World.MarkChanged();
        }

        /// <summary>
        /// Drops the containers and fittings of seeded chunks far from the survivor and every colonist
        /// when none of them has been touched (contents, searched, door, damage): the save then holds
        /// only the chunk's key. A chunk with anything touched or destroyed keeps all of its fittings.
        /// </summary>
        private void PruneFittings(Vec3 p)
        {
            WorldState w = World;
            if (w.SeededChunks.Count == 0) return;
            var keep = new List<Vec3> { p };
            foreach (var col in w.Colonists.Values)
                if (col.Recruited) keep.Add(col.Position);
            List<long> far = null;
            foreach (long key in w.SeededChunks)
            {
                if (w.DormantChunks.Contains(key)) continue;
                ChunkContent.FromKey(key, out int x, out int z);
                float cx = (x + 0.5f) * WorldLayout.ChunkSize, cz = (z + 0.5f) * WorldLayout.ChunkSize;
                bool near = false;
                foreach (Vec3 k in keep)
                    if (Vec3.DistanceXZ(k, new Vec3(cx, 0, cz)) < FittingKeepRadius + WorldLayout.ChunkSize * 0.71f) near = true;
                if (!near) (far ?? (far = new List<long>())).Add(key);
            }
            if (far == null) return;
            var byKey = new Dictionary<string, Structure>(StringComparer.Ordinal);
            foreach (Structure s in w.Structures.Values)
                if (!string.IsNullOrEmpty(s.SeedKey)) byKey[s.SeedKey] = s;
            ContentDatabase content = _session.Content;
            foreach (long key in far)
            {
                ChunkContent.FromKey(key, out int x, out int z);
                ChunkContent c = Layout.GetChunk(x, z);
                if (c == null || (c.Containers.Count == 0 && c.Fixtures.Count == 0)) continue;
                var drop = new List<EntityId>();
                bool untouched = true;
                foreach (ContainerSeed cs in c.Containers)
                {
                    if (!byKey.TryGetValue(cs.Key, out Structure s) || !Untouched(s, content, false)) { untouched = false; break; }
                    drop.Add(s.Id);
                }
                if (untouched)
                    foreach (FixtureSeed f in c.Fixtures)
                    {
                        if (!byKey.TryGetValue(f.Key, out Structure s) || !Untouched(s, content, f.Locked)) { untouched = false; break; }
                        drop.Add(s.Id);
                    }
                if (!untouched) continue;
                foreach (EntityId id in drop) w.RemoveStructure(id);
                w.DormantChunks.Add(key);
            }
        }

        private static bool Untouched(Structure s, ContentDatabase content, bool seededLocked)
        {
            if (s.Touched || s.Searched || s.DoorOpen || !s.IsComplete) return false;
            BuildingDefinition def = content.Building(s.DefId);
            if (def == null || s.Health < def.MaxHealth) return false;
            return !def.IsDoor || s.DoorLocked == seededLocked;
        }

        private void ActivateNodes(ChunkContent c)
        {
            if (_nodesActive.Contains(c.Key)) return;
            _nodesActive.Add(c.Key);
            WorldState w = World;
            foreach (NodeSeed n in c.Nodes)
            {
                if (string.IsNullOrEmpty(n.Key) || w.HasNodeKey(n.Key)) continue;
                ResourceNodeDefinition def = _session.Content.Node(n.DefId);
                if (def == null) continue;
                w.AddNode(new ResourceNodeState
                {
                    Id = w.Ids.Allocate(), DefId = n.DefId, Position = n.Position, Yaw = n.Yaw, Scale = n.Scale,
                    RemainingHarvests = def.Harvests, SeedKey = n.Key
                });
            }
        }

        /// <summary>The chunk a keyed node belongs to: "c{x}_{z}:…" keys name it; Millbrook's ("r:…") go by position.</summary>
        private static long ChunkOfKey(ResourceNodeState n)
        {
            string k = n.SeedKey;
            if (k.Length > 1 && k[0] == 'c')
            {
                int us = k.IndexOf('_'), colon = k.IndexOf(':');
                if (us > 1 && colon > us && int.TryParse(k.Substring(1, us - 1), out int x) && int.TryParse(k.Substring(us + 1, colon - us - 1), out int z))
                    return ChunkContent.ChunkKey(x, z);
            }
            float max = Domain.World.BasinWorld.BlockSize - 1f;
            return ChunkContent.ChunkKey(WorldLayout.ChunkOf(Math.Min(n.Position.X, max)), WorldLayout.ChunkOf(Math.Min(n.Position.Z, max)));
        }

        /// <summary>Drops untouched nodes of chunks far from the survivor and from every colonist.</summary>
        private void DropFarNodes(Vec3 p)
        {
            if (_nodesActive.Count == 0) return;
            var keep = new List<Vec3> { p };
            foreach (var c in World.Colonists.Values)
                if (c.Recruited) keep.Add(c.Position);
            var drop = new List<long>();
            foreach (long key in _nodesActive)
            {
                ChunkContent.FromKey(key, out int x, out int z);
                float cx = (x + 0.5f) * WorldLayout.ChunkSize, cz = (z + 0.5f) * WorldLayout.ChunkSize;
                bool near = false;
                foreach (Vec3 k in keep)
                    if (Vec3.DistanceXZ(k, new Vec3(cx, 0, cz)) < NodeKeepRadius + WorldLayout.ChunkSize * 0.71f) near = true;
                if (!near) drop.Add(key);
            }
            if (drop.Count == 0) return;
            var dropSet = new HashSet<long>(drop);
            var remove = new List<EntityId>();
            foreach (ResourceNodeState n in World.ResourceNodes.Values)
            {
                if (string.IsNullOrEmpty(n.SeedKey) || n.Touched || n.Depleted) continue;
                if (dropSet.Contains(ChunkOfKey(n))) remove.Add(n.Id);
            }
            foreach (EntityId id in remove) World.RemoveNode(id);
            foreach (long k in drop) _nodesActive.Remove(k);
        }
    }
}
