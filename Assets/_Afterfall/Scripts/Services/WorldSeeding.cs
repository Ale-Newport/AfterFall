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
    /// Creates the layout's keyed fixtures (doors, furniture and containers of planned buildings)
    /// that the world does not have yet. A new game gets all of them; a save made before a building
    /// existed gets that building's fixtures once, on load, so new content appears in old worlds
    /// without touching anything the player already has.
    /// </summary>
    public static class WorldSeeding
    {
        /// <summary>Returns how many fixtures were created.</summary>
        public static int EnsureFixtures(WorldState world, ContentDatabase content) =>
            SeedFixtures(world, content, world.Layout.Fixtures, world.Layout.Vehicles);

        /// <summary>Creates the given keyed fixtures and vehicles that the world does not have yet; returns how many.</summary>
        public static int SeedFixtures(WorldState world, ContentDatabase content, List<FixtureSeed> fixtures, List<VehicleSeed> vehicleSeeds)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (Structure s in world.Structures.Values)
                if (!string.IsNullOrEmpty(s.SeedKey)) present.Add(s.SeedKey);
            int created = 0;
            foreach (FixtureSeed f in fixtures)
            {
                if (present.Contains(f.Key)) continue;
                BuildingDefinition def = content.Building(f.DefId);
                if (def == null) continue;
                Structure s = StructureFactory.Create(world, def, f.Position, f.Yaw, StructureState.Complete);
                s.PlayerBuilt = false;
                s.PoiId = f.PoiId;
                s.SeedKey = f.Key;
                s.LockId = f.LockId ?? "";
                s.DoorLocked = def.IsDoor && f.Locked;
                StructureFactory.InitializeComponents(world, content, s);
                if (s.Storage != null)
                {
                    if (!string.IsNullOrEmpty(f.LootTable) && content.LootTables.TryGetValue(f.LootTable, out LootTable table))
                    {
                        // Each fixture rolls from its own stream, so adding fixtures never changes others' loot.
                        var rng = DeterministicRandom.Derive(world.Seed, "fixture:" + f.Key);
                        foreach (ItemAmount a in table.Roll(rng, world.Settings.LootAbundance)) s.Storage.AddUpTo(a.ItemId, a.Quantity);
                    }
                    foreach (string lockId in f.KeysInside)
                        if (content.Items.Contains(DoorRules.KeyItem)) s.Storage.TryAddStack(new ItemStack(DoorRules.KeyItem, 1, 0, lockId));
                    foreach (string item in f.ItemsInside)
                        if (content.Items.Contains(item)) s.Storage.AddUpTo(item, 1);
                }
                s.Touched = false;
                world.AddStructure(s);
                created++;
            }

            var vehicles = new HashSet<string>(StringComparer.Ordinal);
            foreach (Afterfall.Domain.Vehicles.VehicleState v in world.Vehicles.Values)
                if (!string.IsNullOrEmpty(v.SeedKey)) vehicles.Add(v.SeedKey);
            foreach (VehicleSeed seed in vehicleSeeds)
            {
                if (vehicles.Contains(seed.Key)) continue;
                Afterfall.Domain.Vehicles.VehicleDefinition def = content.Vehicle(seed.DefId);
                if (def == null) continue;
                Afterfall.Domain.Vehicles.VehicleState v = VehicleService.Create(world, content, def, seed.Position, seed.Yaw);
                v.SeedKey = seed.Key;
                v.Paint = seed.Paint;
                v.Fuel = Math.Min(def.FuelCapacity, seed.Fuel);
                v.Engine = seed.Engine;
                v.Body = seed.Body;
                v.Tyres = seed.Tyres;
                v.Battery = seed.Battery;
                v.Locked = seed.Locked;
                v.LockId = seed.LockId ?? "";
                v.KeyInIgnition = seed.KeyInIgnition;
                foreach (string item in seed.TrunkItems)
                    if (content.Items.Contains(item)) v.Storage.AddUpTo(item, 1);
                world.AddVehicle(v);
                created++;
            }
            return created;
        }
    }
}
