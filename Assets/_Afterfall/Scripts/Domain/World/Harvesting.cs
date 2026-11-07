using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Content;
using Afterfall.Domain.Items;

namespace Afterfall.Domain.World
{
    public static class HarvestRules
    {
        public static float ActionSeconds(ResourceNodeDefinition def, ItemDefinition tool)
        {
            bool good = tool?.Tool != null && tool.Tool.Type == def.PreferredTool && def.PreferredTool != ToolType.None;
            return def.ActionSeconds * (good ? 0.55f : 1f);
        }

        /// <summary>
        /// One harvest action. Atomic: if the yield does not fit in the inventory nothing is
        /// harvested and the node is unchanged.
        /// </summary>
        public static OpResult Harvest(WorldState world, ContentDatabase content, ResourceNodeState node, ItemDefinition tool, Inventory inventory, DeterministicRandom rng, List<ItemAmount> gained)
        {
            ResourceNodeDefinition def = content.Node(node.DefId);
            if (def == null) return OpResult.Fail("unknown", "Unknown resource.");
            if (node.Depleted)
            {
                if (def.RespawnSeconds <= 0) return OpResult.Fail("depleted", $"{def.Name} is exhausted.");
                double minutes = Math.Max(1, Math.Ceiling((node.RespawnAt - world.Clock.ElapsedSeconds) / 60.0));
                return OpResult.Fail("depleted", $"{def.Name} is exhausted (regrows in ~{minutes:0} min).");
            }
            ToolType held = tool?.Tool?.Type ?? ToolType.None;
            if (def.RequiredTool != ToolType.None && held != def.RequiredTool)
                return OpResult.Fail("tool_required", $"{def.Verb} {def.Name} requires a {ToolName(def.RequiredTool)} in hand.");

            float mult = held == def.PreferredTool && def.PreferredTool != ToolType.None ? Math.Max(1f, tool.Tool.GatherPower) : 1f;
            var yield = new List<ItemAmount>();
            foreach (ResourceYield y in def.Yields)
            {
                if (!rng.Chance(y.Chance)) continue;
                int q = (int)Math.Round(rng.Range(y.Min, y.Max + 1) * mult);
                if (q > 0) yield.Add(new ItemAmount(y.ItemId, q));
            }
            if (yield.Count > 0 && !inventory.TryAddAll(yield))
                return OpResult.Fail("inventory_full", "Inventory full — drop or store something first.");

            node.RemainingHarvests--;
            if (node.RemainingHarvests <= 0)
            {
                node.Depleted = true;
                node.RespawnAt = world.Clock.ElapsedSeconds + def.RespawnSeconds;
                world.MarkChanged();
            }
            gained?.AddRange(yield);
            return OpResult.Success();
        }

        public static string ToolName(ToolType t)
        {
            switch (t)
            {
                case ToolType.Axe: return "axe";
                case ToolType.Pickaxe: return "pickaxe";
                case ToolType.Hammer: return "hammer";
                case ToolType.Knife: return "knife";
                default: return t.ToString().ToLowerInvariant();
            }
        }
    }

    /// <summary>Regrows depleted renewable nodes (trees, bushes) out of the player's immediate view.</summary>
    public sealed class ResourceRespawnSystem : ISimulationSystem
    {
        private readonly WorldState _world;
        private readonly ContentDatabase _content;

        public ResourceRespawnSystem(WorldState world, ContentDatabase content)
        {
            _world = world;
            _content = content;
        }

        public string Name => "Resource regrowth";

        public void Tick(double dt)
        {
            double now = _world.Clock.ElapsedSeconds;
            bool changed = false;
            foreach (ResourceNodeState n in _world.ResourceNodes.Values)
            {
                if (!n.Depleted || n.RespawnAt > now) continue;
                ResourceNodeDefinition def = _content.Node(n.DefId);
                if (def == null || def.RespawnSeconds <= 0) continue;
                if (_world.Player != null && Vec3.Distance(_world.Player.Position, n.Position) < 20f) continue;
                n.Depleted = false;
                n.RemainingHarvests = def.Harvests;
                changed = true;
            }
            if (changed) _world.MarkChanged();
        }
    }
}
