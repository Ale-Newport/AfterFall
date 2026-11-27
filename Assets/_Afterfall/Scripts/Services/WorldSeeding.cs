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
        public static int EnsureFixtures(WorldState world, ContentDatabase content)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (Structure s in world.Structures.Values)
                if (!string.IsNullOrEmpty(s.SeedKey)) present.Add(s.SeedKey);
            int created = 0;
            foreach (FixtureSeed f in world.Layout.Fixtures)
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
                }
                world.AddStructure(s);
                created++;
            }
            return created;
        }
    }
}
