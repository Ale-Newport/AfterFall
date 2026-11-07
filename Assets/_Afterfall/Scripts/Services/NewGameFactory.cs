using System;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>Creates a fresh world: terrain layout, resource nodes, looted containers, player and initial infected.</summary>
    public static class NewGameFactory
    {
        public static GameSession Create(ContentDatabase content, int seed, GameSettings settings)
        {
            settings = settings ?? GameSettings.FromPreset(DifficultyPreset.Survival);
            var world = new WorldState(seed, settings, new SimulationClock(settings.DayLengthMinutes * 60.0), new IdAllocator());
            world.Layout = WorldGenerator.Generate(seed);

            var player = new PlayerState
            {
                Id = world.Ids.Allocate(),
                Position = world.Layout.PlayerSpawn,
                WorldSpawn = world.Layout.PlayerSpawn
            };
            player.Inventory = new Inventory(world.Ids.Allocate(), content.Items, PlayerState.BackpackSlots, PlayerState.CarryMaxKg, null, "Backpack");
            world.Player = player;
            world.RegisterInventory(player.Inventory, player.Id);

            foreach (NodeSeed n in world.Layout.Nodes)
            {
                ResourceNodeDefinition def = content.Node(n.DefId);
                if (def == null) continue;
                world.AddNode(new ResourceNodeState
                {
                    Id = world.Ids.Allocate(),
                    DefId = n.DefId,
                    Position = n.Position,
                    Yaw = n.Yaw,
                    Scale = n.Scale,
                    RemainingHarvests = def.Harvests
                });
            }

            int index = 0;
            foreach (ContainerSeed c in world.Layout.Containers)
            {
                BuildingDefinition def = content.Building(c.DefId);
                if (def == null) continue;
                Structure s = StructureFactory.Create(world, def, c.Position, c.Yaw, StructureState.Complete);
                s.PlayerBuilt = false;
                s.PoiId = c.PoiId;
                StructureFactory.InitializeComponents(world, content, s);
                if (s.Storage != null && content.LootTables.TryGetValue(c.LootTable, out LootTable table))
                {
                    var rng = DeterministicRandom.Derive(seed, "loot:" + index);
                    foreach (ItemAmount a in table.Roll(rng, settings.LootAbundance)) s.Storage.AddUpTo(a.ItemId, a.Quantity);
                }
                world.AddStructure(s);
                index++;
            }

            var session = new GameSession(content, world);
            session.Tech.Evaluate();

            foreach (SurvivorSeed sv in world.Layout.Survivors)
            {
                var skills = new System.Collections.Generic.Dictionary<Afterfall.Domain.Colony.JobType, float>();
                foreach (var kv in sv.Skills)
                    if (Enum.TryParse(kv.Key, out Afterfall.Domain.Colony.JobType jt)) skills[jt] = kv.Value;
                session.Colony.CreateSurvivor(sv.Name, sv.Position, skills);
            }

            // Initial infected population around points of interest, never near the start.
            var zr = DeterministicRandom.Derive(seed, "initial_zombies");
            foreach (Vec3 p in world.Layout.ZombieSpawnPoints)
            {
                if (Vec3.DistanceXZ(p, player.Position) < 70f) continue;
                if (!zr.Chance(Math.Min(1f, 0.55f * settings.EnemyDensity))) continue;
                session.Zombies.Spawn(p, "wanderer", zr);
            }
            world.Exploration.Reveal(player.Position, GameSession.RevealRadius);
            return session;
        }
    }
}
