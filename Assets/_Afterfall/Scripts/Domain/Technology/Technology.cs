using System;
using System.Collections.Generic;
using System.Text;
using Afterfall.Core;
using Afterfall.Domain.Events;

namespace Afterfall.Domain.Technology
{
    public enum UnlockTrigger
    {
        /// <summary>Known from the start of the game.</summary>
        Start,

        /// <summary>Obtain an item (by gathering, looting, crafting or picking it up).</summary>
        AcquireItem,

        /// <summary>Complete construction of a building.</summary>
        BuildStructure,

        /// <summary>Produce an item through a recipe (hand, station or machine).</summary>
        CraftItem,

        /// <summary>Discover a point of interest.</summary>
        DiscoverPoi
    }

    public sealed class UnlockCondition
    {
        public UnlockTrigger Trigger { get; set; }
        public string Target { get; set; } = "";
        public int Count { get; set; } = 1;
        public string Description { get; set; } = "";

        public string Key => TechnologyState.Key(Trigger, Target);
    }

    /// <summary>
    /// A technology is unlocked by doing things in the world (finding schematics, building
    /// infrastructure, operating production chains) rather than by grinding experience.
    /// Unlocking only grants knowledge: the real material prerequisites still apply.
    /// </summary>
    public sealed class TechDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int Era { get; set; } = 1;
        public string[] Prerequisites { get; set; } = Array.Empty<string>();
        public List<UnlockCondition> Conditions { get; set; } = new List<UnlockCondition>();
    }

    /// <summary>Runtime, persisted technology progress.</summary>
    public sealed class TechnologyState
    {
        public HashSet<string> Unlocked { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, int> Progress { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public static string Key(UnlockTrigger trigger, string target) => trigger + ":" + target;

        /// <summary>Empty requirement means "no technology needed".</summary>
        public bool IsUnlocked(string techId) => string.IsNullOrEmpty(techId) || Unlocked.Contains(techId);

        public int GetProgress(UnlockTrigger trigger, string target)
        {
            Progress.TryGetValue(Key(trigger, target), out int v);
            return v;
        }
    }

    public sealed class TechnologyService
    {
        private readonly IReadOnlyDictionary<string, TechDefinition> _techs;
        private readonly EventBus _bus;

        public TechnologyService(IReadOnlyDictionary<string, TechDefinition> techs, TechnologyState state, EventBus bus)
        {
            _techs = techs;
            State = state;
            _bus = bus;
        }

        public TechnologyState State { get; }

        /// <summary>Records world activity and unlocks every technology whose conditions are now met.</summary>
        public void Record(UnlockTrigger trigger, string target, int count = 1)
        {
            if (string.IsNullOrEmpty(target) || count <= 0) return;
            string key = TechnologyState.Key(trigger, target);
            State.Progress.TryGetValue(key, out int current);
            State.Progress[key] = current + count;
            Evaluate();
        }

        /// <summary>Unlocks everything currently satisfiable (also used at new game for Start techs).</summary>
        public void Evaluate()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (TechDefinition tech in SortedTechs())
                {
                    if (State.Unlocked.Contains(tech.Id) || !IsSatisfied(tech)) continue;
                    State.Unlocked.Add(tech.Id);
                    changed = true;
                    _bus?.Publish(new TechnologyUnlocked(tech.Id, tech.Name));
                    _bus?.Publish(new Notification($"New technology: {tech.Name}", Severity.Success));
                }
            }
        }

        public bool IsSatisfied(TechDefinition tech)
        {
            foreach (string pre in tech.Prerequisites ?? Array.Empty<string>())
                if (!State.Unlocked.Contains(pre)) return false;
            foreach (UnlockCondition c in tech.Conditions)
            {
                if (c.Trigger == UnlockTrigger.Start) continue;
                if (State.GetProgress(c.Trigger, c.Target) < c.Count) return false;
            }
            return true;
        }

        /// <summary>Explains what is still needed, e.g. "Requires Metalworking; Build Furnace (0/1)".</summary>
        public string DescribeRequirements(string techId)
        {
            if (string.IsNullOrEmpty(techId) || !_techs.TryGetValue(techId, out TechDefinition tech)) return "";
            if (State.Unlocked.Contains(techId)) return $"{tech.Name} (known)";
            var sb = new StringBuilder();
            sb.Append("Locked: ").Append(tech.Name).Append(" — ");
            bool first = true;
            foreach (string pre in tech.Prerequisites ?? Array.Empty<string>())
            {
                if (State.Unlocked.Contains(pre)) continue;
                if (!first) sb.Append("; ");
                sb.Append("requires ").Append(_techs.TryGetValue(pre, out TechDefinition p) ? p.Name : pre);
                first = false;
            }
            foreach (UnlockCondition c in tech.Conditions)
            {
                if (c.Trigger == UnlockTrigger.Start) continue;
                int have = Math.Min(c.Count, State.GetProgress(c.Trigger, c.Target));
                if (!first) sb.Append("; ");
                sb.Append(string.IsNullOrEmpty(c.Description) ? c.Key : c.Description).Append($" ({have}/{c.Count})");
                first = false;
            }
            return sb.ToString();
        }

        private List<TechDefinition> SortedTechs()
        {
            var list = new List<TechDefinition>(_techs.Values);
            list.Sort((a, b) => a.Era != b.Era ? a.Era.CompareTo(b.Era) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }
    }
}
