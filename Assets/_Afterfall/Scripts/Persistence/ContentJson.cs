using System;
using System.Collections.Generic;
using Afterfall.Domain.AI;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;
using Newtonsoft.Json;

namespace Afterfall.Persistence
{
    /// <summary>Raw JSON text of each content file (items.json, recipes.json, ...).</summary>
    public sealed class ContentSources
    {
        public string Items { get; set; }
        public string Recipes { get; set; }
        public string Buildings { get; set; }
        public string Technologies { get; set; }
        public string ResourceNodes { get; set; }
        public string LootTables { get; set; }
        public string Archetypes { get; set; }
    }

    /// <summary>
    /// Loads content definitions from JSON. Unknown fields are errors so typos in data files
    /// are caught immediately rather than silently ignored.
    /// </summary>
    public static class ContentJson
    {
        private static readonly JsonSerializerSettings Settings = JsonDefaults.Create(true);

        public static ContentDatabase Load(ContentSources src)
        {
            var db = new ContentDatabase();
            foreach (ItemDefinition i in Parse<ItemDefinition>(src.Items, "items")) db.Items.Add(i);
            foreach (RecipeDefinition r in Parse<RecipeDefinition>(src.Recipes, "recipes")) db.AddRecipe(r);
            foreach (BuildingDefinition b in Parse<BuildingDefinition>(src.Buildings, "buildings")) db.AddBuilding(b);
            foreach (TechDefinition t in Parse<TechDefinition>(src.Technologies, "technologies")) db.AddTech(t);
            foreach (ResourceNodeDefinition n in Parse<ResourceNodeDefinition>(src.ResourceNodes, "resource_nodes")) db.AddNode(n);
            foreach (LootTable l in Parse<LootTable>(src.LootTables, "loot_tables")) db.AddLootTable(l);
            foreach (ZombieArchetype a in Parse<ZombieArchetype>(src.Archetypes, "archetypes")) db.Archetypes[a.Id] = a;
            return db;
        }

        private static List<T> Parse<T>(string json, string file)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<T>();
            try
            {
                return JsonConvert.DeserializeObject<List<T>>(json, Settings) ?? new List<T>();
            }
            catch (JsonException ex)
            {
                throw new FormatException($"Content file '{file}.json' is invalid: {ex.Message}", ex);
            }
        }

        public static readonly string[] FileNames =
        {
            "items", "recipes", "buildings", "technologies", "resource_nodes", "loot_tables", "archetypes"
        };

        /// <summary>Builds sources from a lookup function (file name without extension → text).</summary>
        public static ContentSources FromLookup(Func<string, string> read)
        {
            return new ContentSources
            {
                Items = read("items"),
                Recipes = read("recipes"),
                Buildings = read("buildings"),
                Technologies = read("technologies"),
                ResourceNodes = read("resource_nodes"),
                LootTables = read("loot_tables"),
                Archetypes = read("archetypes")
            };
        }
    }
}
