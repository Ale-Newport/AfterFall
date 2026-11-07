using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Items;
using Afterfall.Domain.Technology;

namespace Afterfall.Domain.Crafting
{
    /// <summary>Why a recipe can or cannot be crafted right now (drives UI explanations).</summary>
    public sealed class CraftCheck
    {
        public bool CanCraft { get; set; }
        public bool Locked { get; set; }
        public bool StationMissing { get; set; }
        public List<ItemAmount> Missing { get; } = new List<ItemAmount>();
        public string Reason { get; set; } = "";
    }

    public static class CraftingRules
    {
        public static CraftCheck Check(RecipeDefinition recipe, Inventory source, TechnologyState tech, ICollection<string> stationsInRange, TechnologyService techService = null)
        {
            var check = new CraftCheck();
            if (!tech.IsUnlocked(recipe.RequiredTech))
            {
                check.Locked = true;
                check.Reason = techService != null ? techService.DescribeRequirements(recipe.RequiredTech) : $"Requires technology {recipe.RequiredTech}";
                return check;
            }
            if (!recipe.IsHandRecipe && (stationsInRange == null || !stationsInRange.Contains(recipe.Station)))
            {
                check.StationMissing = true;
                check.Reason = $"Requires a nearby {StationName(recipe.Station)}";
            }
            check.Missing.AddRange(source.Missing(recipe.Inputs));
            if (check.Missing.Count > 0 && check.Reason.Length == 0)
            {
                var parts = new List<string>();
                foreach (ItemAmount m in check.Missing) parts.Add($"{m.Quantity} {source.Catalog.NameOf(m.ItemId)}");
                check.Reason = "Missing " + string.Join(", ", parts);
            }
            check.CanCraft = !check.StationMissing && check.Missing.Count == 0;
            return check;
        }

        public static string StationName(string station)
        {
            switch (station)
            {
                case "workbench": return "Workbench";
                case "furnace": return "Furnace";
                case "workshop": return "Machine Workshop";
                case "campfire": return "Campfire";
                default: return string.IsNullOrEmpty(station) ? "hands" : station;
            }
        }
    }

    public sealed class CraftJob
    {
        public string RecipeId { get; set; } = "";
        public float Progress { get; set; }
        public float Duration { get; set; }

        /// <summary>Finished but the outputs did not fit into the inventory yet.</summary>
        public bool AwaitingSpace { get; set; }
    }

    /// <summary>
    /// The player's personal crafting queue. Inputs are consumed when a job is queued (so
    /// they cannot be spent twice) and refunded on cancellation; outputs are delivered
    /// atomically when the job completes, or held until there is room.
    /// </summary>
    public sealed class CraftingQueue
    {
        public const int MaxJobs = 6;

        public List<CraftJob> Jobs { get; } = new List<CraftJob>();

        public OpResult Enqueue(RecipeDefinition recipe, Inventory inventory, TechnologyState tech, ICollection<string> stations, TechnologyService techService = null)
        {
            if (Jobs.Count >= MaxJobs) return OpResult.Fail("queue_full", "Crafting queue is full.");
            CraftCheck check = CraftingRules.Check(recipe, inventory, tech, stations, techService);
            if (!check.CanCraft) return OpResult.Fail(check.Locked ? "locked" : check.StationMissing ? "no_station" : "missing_items", check.Reason);
            if (!inventory.TryConsume(recipe.Inputs)) return OpResult.Fail("missing_items", "Ingredients are no longer available.");
            Jobs.Add(new CraftJob { RecipeId = recipe.Id, Duration = recipe.Duration });
            return OpResult.Success($"Crafting {recipe.Name}");
        }

        /// <summary>Advances the head job. Returns the recipe completed this tick, if any.</summary>
        public RecipeDefinition Tick(double dt, Inventory inventory, IReadOnlyDictionary<string, RecipeDefinition> recipes)
        {
            if (Jobs.Count == 0) return null;
            CraftJob job = Jobs[0];
            if (!recipes.TryGetValue(job.RecipeId, out RecipeDefinition recipe))
            {
                Jobs.RemoveAt(0);
                return null;
            }
            job.Progress += (float)dt;
            if (job.Progress < job.Duration) return null;
            job.Progress = job.Duration;
            if (!inventory.TryAddAll(recipe.Outputs))
            {
                job.AwaitingSpace = true;
                return null;
            }
            Jobs.RemoveAt(0);
            return recipe;
        }

        /// <summary>Cancels a queued job and refunds its inputs (fails without changes if they do not fit).</summary>
        public OpResult Cancel(int index, Inventory inventory, IReadOnlyDictionary<string, RecipeDefinition> recipes)
        {
            if (index < 0 || index >= Jobs.Count) return OpResult.Fail("invalid", "No such job.");
            if (!recipes.TryGetValue(Jobs[index].RecipeId, out RecipeDefinition recipe))
            {
                Jobs.RemoveAt(index);
                return OpResult.Success();
            }
            if (!inventory.TryAddAll(recipe.Inputs)) return OpResult.Fail("no_space", "Not enough room to refund the ingredients.");
            Jobs.RemoveAt(index);
            return OpResult.Success($"Cancelled {recipe.Name}");
        }
    }
}
