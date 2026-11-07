using System.Collections.Generic;
using Afterfall.Domain.Items;

namespace Afterfall.Domain.Crafting
{
    /// <summary>
    /// Data-driven transformation of input items into output items. The same definition
    /// serves hand crafting (Station empty), station crafting (e.g. "workbench") and
    /// automated machine processing (e.g. "furnace", "workshop").
    /// </summary>
    public sealed class RecipeDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<ItemAmount> Inputs { get; set; } = new List<ItemAmount>();
        public List<ItemAmount> Outputs { get; set; } = new List<ItemAmount>();

        /// <summary>Station id required ("" = can be crafted by hand anywhere).</summary>
        public string Station { get; set; } = "";

        /// <summary>Technology that must be unlocked ("" = always known).</summary>
        public string RequiredTech { get; set; } = "";

        /// <summary>Seconds per crafting cycle.</summary>
        public float Duration { get; set; } = 2f;

        public string Category { get; set; } = "General";

        public bool IsHandRecipe => string.IsNullOrEmpty(Station);

        public override string ToString() => Id;
    }
}
