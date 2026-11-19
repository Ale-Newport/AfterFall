using System.Collections.Generic;
using System.Linq;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>
    /// Progression-blocker checks on real generated worlds: starting from what a new game actually
    /// contains (the survivor's kit, rolled loot, resource nodes, points of interest), works out by
    /// fixpoint everything the player can reach (items, tools, technologies, buildings, stations,
    /// recipes, crops) and fails if any content can never be obtained, built or crafted.
    /// </summary>
    public class ProgressionTests
    {
        /// <summary>World seeds checked; loot is rolled per seed, so a chance-based key item can go missing.</summary>
        private static readonly int[] Seeds = { 1, 7, 42, 1337, 12345, 2024, 31337, 99991 };

        private sealed class Reach
        {
            public readonly HashSet<string> Items = new HashSet<string>();
            public readonly HashSet<string> Crafted = new HashSet<string>();
            public readonly HashSet<string> Techs = new HashSet<string>();
            public readonly HashSet<string> Built = new HashSet<string>();
            public readonly HashSet<string> Stations = new HashSet<string> { "" };
            public readonly HashSet<string> Recipes = new HashSet<string>();
        }

        private static Reach Explore(GameSession s)
        {
            ContentDatabase c = s.Content;
            WorldState w = s.World;
            var r = new Reach();

            // What the world hands the player: the starting kit and everything lying in containers and drops.
            foreach (Inventory inv in w.AllInventories)
                for (int i = 0; i < inv.SlotCount; i++)
                    if (!inv.GetSlot(i).IsEmpty) r.Items.Add(inv.GetSlot(i).ItemId);
            var nodes = new HashSet<string>(w.ResourceNodes.Values.Select(n => n.DefId));
            var pois = new HashSet<string>(w.Layout.Pois.Select(p => p.Id));

            bool changed = true;
            while (changed)
            {
                int before = r.Items.Count + r.Crafted.Count + r.Techs.Count + r.Built.Count + r.Recipes.Count;
                var tools = new HashSet<ToolType>(r.Items.Select(id => c.Items.Get(id)?.Tool?.Type ?? ToolType.None));
                tools.Add(ToolType.None);

                foreach (string nodeId in nodes)
                {
                    ResourceNodeDefinition n = c.ResourceNodes[nodeId];
                    if (!tools.Contains(n.RequiredTool)) continue;
                    foreach (ResourceYield y in n.Yields)
                        if (y.Chance > 0f && y.Max > 0) r.Items.Add(y.ItemId);
                }

                foreach (TechDefinition t in c.Techs.Values)
                {
                    if (r.Techs.Contains(t.Id) || !t.Prerequisites.All(r.Techs.Contains)) continue;
                    if (t.Conditions.All(cond => Satisfied(cond, r, pois))) r.Techs.Add(t.Id);
                }

                foreach (BuildingDefinition b in c.Buildings.Values)
                {
                    if (!b.Buildable || r.Built.Contains(b.Id)) continue;
                    if (!Unlocked(b.RequiredTech, r) || !b.Cost.All(a => r.Items.Contains(a.ItemId))) continue;
                    r.Built.Add(b.Id);
                    if (!string.IsNullOrEmpty(b.CraftingStation)) r.Stations.Add(b.CraftingStation);
                    if (b.Machine != null) r.Stations.Add(b.Machine.Station);
                }

                foreach (RecipeDefinition rec in c.Recipes.Values)
                {
                    if (r.Recipes.Contains(rec.Id) || !Unlocked(rec.RequiredTech, r) || !r.Stations.Contains(rec.Station)) continue;
                    if (!rec.Inputs.All(a => r.Items.Contains(a.ItemId))) continue;
                    r.Recipes.Add(rec.Id);
                    foreach (ItemAmount o in rec.Outputs)
                    {
                        r.Items.Add(o.ItemId);
                        r.Crafted.Add(o.ItemId);
                    }
                }

                foreach (string id in r.Built)
                {
                    FarmSpec f = c.Building(id).Farm;
                    if (f == null || !r.Items.Contains(f.SeedItem)) continue;
                    r.Items.Add(f.CropItem);
                    r.Crafted.Add(f.CropItem);
                }

                changed = r.Items.Count + r.Crafted.Count + r.Techs.Count + r.Built.Count + r.Recipes.Count != before;
            }
            return r;
        }

        private static bool Unlocked(string tech, Reach r) => string.IsNullOrEmpty(tech) || r.Techs.Contains(tech);

        private static bool Satisfied(UnlockCondition cond, Reach r, HashSet<string> pois)
        {
            switch (cond.Trigger)
            {
                case UnlockTrigger.Start: return true;
                case UnlockTrigger.AcquireItem: return r.Items.Contains(cond.Target);
                case UnlockTrigger.CraftItem: return r.Crafted.Contains(cond.Target);
                case UnlockTrigger.BuildStructure: return r.Built.Contains(cond.Target);
                case UnlockTrigger.DiscoverPoi: return pois.Contains(cond.Target);
                default: return false;
            }
        }

        [Test]
        public void EveryTechnologyBuildingAndRecipe_IsReachable_OnEverySeed([ValueSource(nameof(Seeds))] int seed)
        {
            GameSession s = TestSupport.NewSession(seed);
            ContentDatabase c = s.Content;
            Reach r = Explore(s);

            CollectionAssert.IsEmpty(c.Techs.Keys.Where(t => !r.Techs.Contains(t)).ToList(), "technologies that can never unlock");
            CollectionAssert.IsEmpty(c.Buildings.Values.Where(b => b.Buildable && !r.Built.Contains(b.Id)).Select(b => b.Id).ToList(), "buildings that can never be built");
            CollectionAssert.IsEmpty(c.Recipes.Keys.Where(id => !r.Recipes.Contains(id)).ToList(), "recipes that can never be crafted");
        }

        [Test]
        public void EveryItemTheGameAsksFor_CanBeObtained_OnEverySeed([ValueSource(nameof(Seeds))] int seed)
        {
            GameSession s = TestSupport.NewSession(seed);
            ContentDatabase c = s.Content;
            Reach r = Explore(s);

            var needed = new SortedSet<string>();
            foreach (RecipeDefinition rec in c.Recipes.Values) foreach (ItemAmount a in rec.Inputs) needed.Add(a.ItemId);
            foreach (BuildingDefinition b in c.Buildings.Values.Where(b => b.Buildable)) foreach (ItemAmount a in b.Cost) needed.Add(a.ItemId);
            foreach (BuildingDefinition b in c.Buildings.Values.Where(b => b.Farm != null)) needed.Add(b.Farm.SeedItem);

            CollectionAssert.IsEmpty(needed.Where(id => !r.Items.Contains(id)).ToList(), "items required by recipes or buildings that the world never provides");
        }

        [Test]
        public void AllContentReferences_PointAtExistingDefinitions()
        {
            ContentDatabase c = TestSupport.Content;
            var missing = new List<string>();
            void Item(string where, string id)
            {
                if (!string.IsNullOrEmpty(id) && !c.Items.TryGet(id, out _)) missing.Add($"{where}: item '{id}'");
            }
            void Tech(string where, string id)
            {
                if (!string.IsNullOrEmpty(id) && !c.Techs.ContainsKey(id)) missing.Add($"{where}: technology '{id}'");
            }
            foreach (RecipeDefinition rec in c.Recipes.Values)
            {
                foreach (ItemAmount a in rec.Inputs.Concat(rec.Outputs)) Item("recipe " + rec.Id, a.ItemId);
                Tech("recipe " + rec.Id, rec.RequiredTech);
            }
            foreach (BuildingDefinition b in c.Buildings.Values)
            {
                foreach (ItemAmount a in b.Cost) Item("building " + b.Id, a.ItemId);
                Tech("building " + b.Id, b.RequiredTech);
                if (b.Farm != null)
                {
                    Item("building " + b.Id, b.Farm.SeedItem);
                    Item("building " + b.Id, b.Farm.CropItem);
                }
            }
            foreach (ResourceNodeDefinition n in c.ResourceNodes.Values)
                foreach (ResourceYield y in n.Yields) Item("node " + n.Id, y.ItemId);
            foreach (LootTable t in c.LootTables.Values)
                foreach (LootEntry e in t.Entries) Item("loot table " + t.Id, e.ItemId);
            foreach (TechDefinition t in c.Techs.Values)
            {
                foreach (string p in t.Prerequisites) Tech("technology " + t.Id, p);
                foreach (UnlockCondition cond in t.Conditions)
                {
                    if (cond.Trigger == UnlockTrigger.AcquireItem || cond.Trigger == UnlockTrigger.CraftItem) Item("technology " + t.Id, cond.Target);
                    if (cond.Trigger == UnlockTrigger.BuildStructure && c.Building(cond.Target) == null) missing.Add($"technology {t.Id}: building '{cond.Target}'");
                }
            }
            CollectionAssert.IsEmpty(missing);
        }
    }
}
