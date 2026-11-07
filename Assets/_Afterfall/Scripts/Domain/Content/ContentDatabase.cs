using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Content
{
    public sealed class LootEntry
    {
        public string ItemId { get; set; } = "";
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 1;
        public float Chance { get; set; } = 1f;
    }

    /// <summary>Independent-roll loot table used to fill world containers at world creation.</summary>
    public sealed class LootTable
    {
        public string Id { get; set; } = "";
        public List<LootEntry> Entries { get; set; } = new List<LootEntry>();

        public List<ItemAmount> Roll(DeterministicRandom rng, float abundance)
        {
            var result = new List<ItemAmount>();
            foreach (LootEntry e in Entries)
            {
                // Guaranteed entries (chance >= 1) are never removed by low abundance.
                float chance = e.Chance >= 1f ? 1f : Math.Min(1f, e.Chance * abundance);
                if (!rng.Chance(chance)) continue;
                int qty = rng.Range(e.Min, e.Max + 1);
                if (e.Chance < 1f || abundance > 1f) qty = Math.Max(1, (int)Math.Round(qty * abundance));
                if (qty > 0) result.Add(new ItemAmount(e.ItemId, qty));
            }
            return result;
        }
    }

    /// <summary>All static game content, loaded from data files. Immutable after loading.</summary>
    public sealed class ContentDatabase
    {
        public ItemCatalog Items { get; } = new ItemCatalog();
        public Dictionary<string, RecipeDefinition> Recipes { get; } = new Dictionary<string, RecipeDefinition>(StringComparer.Ordinal);
        public Dictionary<string, BuildingDefinition> Buildings { get; } = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
        public Dictionary<string, TechDefinition> Techs { get; } = new Dictionary<string, TechDefinition>(StringComparer.Ordinal);
        public Dictionary<string, ResourceNodeDefinition> ResourceNodes { get; } = new Dictionary<string, ResourceNodeDefinition>(StringComparer.Ordinal);
        public Dictionary<string, LootTable> LootTables { get; } = new Dictionary<string, LootTable>(StringComparer.Ordinal);
        public Dictionary<string, ZombieArchetype> Archetypes { get; } = new Dictionary<string, ZombieArchetype>(StringComparer.Ordinal);
        public SurvivalConfig Survival { get; set; } = new SurvivalConfig();

        public RecipeDefinition Recipe(string id) => id != null && Recipes.TryGetValue(id, out RecipeDefinition r) ? r : null;
        public BuildingDefinition Building(string id) => id != null && Buildings.TryGetValue(id, out BuildingDefinition b) ? b : null;
        public ResourceNodeDefinition Node(string id) => id != null && ResourceNodes.TryGetValue(id, out ResourceNodeDefinition n) ? n : null;

        public ZombieArchetype Archetype(string id)
        {
            if (id != null && Archetypes.TryGetValue(id, out ZombieArchetype a)) return a;
            if (!Archetypes.TryGetValue("wanderer", out a))
            {
                a = ZombieArchetype.Wanderer();
                Archetypes["wanderer"] = a;
            }
            return a;
        }

        public List<RecipeDefinition> RecipesForStation(string station)
        {
            var list = new List<RecipeDefinition>();
            foreach (RecipeDefinition r in Recipes.Values)
                if (string.Equals(r.Station ?? "", station ?? "", StringComparison.Ordinal)) list.Add(r);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public void AddRecipe(RecipeDefinition r) => AddUnique(Recipes, r.Id, r, "recipe");
        public void AddBuilding(BuildingDefinition b) => AddUnique(Buildings, b.Id, b, "building");
        public void AddTech(TechDefinition t) => AddUnique(Techs, t.Id, t, "technology");
        public void AddNode(ResourceNodeDefinition n) => AddUnique(ResourceNodes, n.Id, n, "resource node");
        public void AddLootTable(LootTable l) => AddUnique(LootTables, l.Id, l, "loot table");

        private static void AddUnique<T>(Dictionary<string, T> dict, string id, T value, string kind)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException($"A {kind} has an empty id.");
            if (dict.ContainsKey(id)) throw new ArgumentException($"Duplicate {kind} id '{id}'.");
            dict.Add(id, value);
        }
    }

    /// <summary>Static checks that every content reference resolves; run by tests and the editor validator.</summary>
    public static class ContentValidator
    {
        public static List<string> Validate(ContentDatabase db)
        {
            var errors = new List<string>();
            var stations = new HashSet<string>(StringComparer.Ordinal);
            foreach (BuildingDefinition b in db.Buildings.Values)
            {
                if (!string.IsNullOrEmpty(b.CraftingStation)) stations.Add(b.CraftingStation);
                if (b.Machine != null && !string.IsNullOrEmpty(b.Machine.Station)) stations.Add(b.Machine.Station);
            }

            foreach (ItemDefinition i in db.Items.All)
            {
                if (i.MaxStack < 1) errors.Add($"Item {i.Id}: MaxStack must be >= 1.");
                if (i.Weight < 0) errors.Add($"Item {i.Id}: negative weight.");
                if (i.HasDurability && i.MaxStack != 1) errors.Add($"Item {i.Id}: durable items must have MaxStack 1.");
                if (i.Consumable != null && !string.IsNullOrEmpty(i.Consumable.Returns) && !db.Items.Contains(i.Consumable.Returns))
                    errors.Add($"Item {i.Id}: returns unknown item '{i.Consumable.Returns}'.");
            }

            foreach (RecipeDefinition r in db.Recipes.Values)
            {
                if (r.Outputs.Count == 0) errors.Add($"Recipe {r.Id}: has no outputs.");
                if (r.Duration <= 0) errors.Add($"Recipe {r.Id}: duration must be positive.");
                CheckAmounts(db, errors, $"Recipe {r.Id} input", r.Inputs);
                CheckAmounts(db, errors, $"Recipe {r.Id} output", r.Outputs);
                if (!string.IsNullOrEmpty(r.Station) && !stations.Contains(r.Station)) errors.Add($"Recipe {r.Id}: no building provides station '{r.Station}'.");
                CheckTech(db, errors, $"Recipe {r.Id}", r.RequiredTech);
            }

            foreach (BuildingDefinition b in db.Buildings.Values)
            {
                if (b.Buildable && b.Cost.Count == 0) errors.Add($"Building {b.Id}: buildable but has no cost.");
                if (b.BuildSeconds <= 0) errors.Add($"Building {b.Id}: BuildSeconds must be positive.");
                if (b.MaxHealth <= 0) errors.Add($"Building {b.Id}: MaxHealth must be positive.");
                CheckAmounts(db, errors, $"Building {b.Id} cost", b.Cost);
                CheckTech(db, errors, $"Building {b.Id}", b.RequiredTech);
                if (b.Farm != null && (!db.Items.Contains(b.Farm.SeedItem) || !db.Items.Contains(b.Farm.CropItem)))
                    errors.Add($"Building {b.Id}: farm references unknown seed or crop item.");
                if (b.Machine != null && db.RecipesForStation(b.Machine.Station).Count == 0)
                    errors.Add($"Building {b.Id}: machine station '{b.Machine.Station}' has no recipes.");
            }

            foreach (TechDefinition t in db.Techs.Values)
            {
                foreach (string pre in t.Prerequisites ?? Array.Empty<string>())
                    if (!db.Techs.ContainsKey(pre)) errors.Add($"Tech {t.Id}: unknown prerequisite '{pre}'.");
                foreach (UnlockCondition c in t.Conditions)
                {
                    if (c.Trigger == UnlockTrigger.AcquireItem || c.Trigger == UnlockTrigger.CraftItem)
                    {
                        if (!db.Items.Contains(c.Target)) errors.Add($"Tech {t.Id}: condition references unknown item '{c.Target}'.");
                    }
                    else if (c.Trigger == UnlockTrigger.BuildStructure && !db.Buildings.ContainsKey(c.Target))
                    {
                        errors.Add($"Tech {t.Id}: condition references unknown building '{c.Target}'.");
                    }
                }
            }

            foreach (ResourceNodeDefinition n in db.ResourceNodes.Values)
            {
                if (n.Harvests <= 0) errors.Add($"Node {n.Id}: Harvests must be positive.");
                foreach (ResourceYield y in n.Yields)
                {
                    if (!db.Items.Contains(y.ItemId)) errors.Add($"Node {n.Id}: yields unknown item '{y.ItemId}'.");
                    if (y.Min > y.Max) errors.Add($"Node {n.Id}: yield {y.ItemId} has Min > Max.");
                }
            }

            foreach (LootTable l in db.LootTables.Values)
                foreach (LootEntry e in l.Entries)
                    if (!db.Items.Contains(e.ItemId)) errors.Add($"Loot table {l.Id}: unknown item '{e.ItemId}'.");

            return errors;
        }

        private static void CheckAmounts(ContentDatabase db, List<string> errors, string context, List<ItemAmount> amounts)
        {
            foreach (ItemAmount a in amounts)
            {
                if (!db.Items.Contains(a.ItemId)) errors.Add($"{context}: unknown item '{a.ItemId}'.");
                if (a.Quantity <= 0) errors.Add($"{context}: non-positive quantity for '{a.ItemId}'.");
            }
        }

        private static void CheckTech(ContentDatabase db, List<string> errors, string context, string tech)
        {
            if (!string.IsNullOrEmpty(tech) && !db.Techs.ContainsKey(tech)) errors.Add($"{context}: unknown technology '{tech}'.");
        }
    }
}
