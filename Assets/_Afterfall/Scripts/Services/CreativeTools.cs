using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>Where the creative item browser puts spawned items.</summary>
    public enum SpawnTarget
    {
        Backpack,
        World,
        Container
    }

    /// <summary>A place the creative panel can teleport to.</summary>
    public sealed class TeleportDestination
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public Vec3 Position { get; set; }

        /// <summary>The map marker or bookmark this destination comes from (None for points of interest).</summary>
        public EntityId MarkerId { get; set; }
    }

    /// <summary>
    /// Creative and developer commands: switching modes, the item browser's search and spawning,
    /// teleport bookkeeping and bookmarks. Every command refuses unless the game is in Creative
    /// mode or developer tools were enabled, and enabling them marks the save for good
    /// (<see cref="GameSettings.DeveloperTools"/>), so survival saves stay distinguishable.
    /// </summary>
    public sealed class CreativeTools
    {
        public const int MaxSpawnQuantity = 999;
        private const string Locked = "Developer tools are off. Enable them in the creative panel (F6) first.";

        private readonly GameSession _session;
        private readonly List<Vec3> _history = new List<Vec3>();

        public CreativeTools(GameSession session)
        {
            _session = session;
        }

        private WorldState World => _session.World;
        public bool Enabled => World.Settings.ToolsEnabled();
        public bool IsCreative => World.Settings.IsCreative();

        /// <summary>Turns developer tools on in this game (Survival saves are marked as such for good).</summary>
        public OpResult EnableDeveloperTools()
        {
            if (Enabled) return OpResult.Success("Developer tools are already on.");
            World.Settings.DeveloperTools = true;
            _session.Events.Publish(new Notification("Developer tools enabled. This save is now marked as using them.", Severity.Warning));
            return OpResult.Success("Developer tools enabled.");
        }

        /// <summary>Switches between Survival and Creative. Leaving Survival marks the save as using developer tools.</summary>
        public OpResult SetMode(GameMode mode)
        {
            if (World.Settings.Mode == mode) return OpResult.Success($"Already in {mode} mode.");
            if (mode == GameMode.Creative) World.Settings.DeveloperTools = true;
            World.Settings.Mode = mode;
            _session.Events.Publish(new Notification(mode == GameMode.Creative
                ? "Creative mode: free and instant building, every blueprint, no harm."
                : "Survival mode: every rule applies again.", Severity.Info));
            return OpResult.Success($"{mode} mode.");
        }

        // ------------------------------------------------------------------ item browser

        /// <summary>
        /// Items from the real catalogue whose name, id or tags contain <paramref name="query"/>
        /// (case-insensitive), optionally in one category, ordered by category then name. New
        /// content appears here automatically; nothing is listed by hand.
        /// </summary>
        public List<ItemDefinition> SearchItems(string query, ItemCategory? category = null)
        {
            string q = (query ?? "").Trim();
            var result = new List<ItemDefinition>();
            foreach (ItemDefinition d in _session.Content.Items.All)
            {
                if (category.HasValue && d.Category != category.Value) continue;
                if (q.Length > 0 && !Matches(d, q)) continue;
                result.Add(d);
            }
            result.Sort((a, b) =>
            {
                int c = a.Category.CompareTo(b.Category);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static bool Matches(ItemDefinition d, string q)
        {
            if (d.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (d.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (d.Tags != null)
                foreach (string t in d.Tags)
                    if (t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Creates <paramref name="quantity"/> new items through the normal inventory rules: into the
        /// backpack (anything that does not fit is dropped at the survivor's feet), as a ground
        /// drop at <paramref name="worldPosition"/>, or into a container's inventory.
        /// </summary>
        public OpResult SpawnItem(string itemId, int quantity, SpawnTarget target, Vec3 worldPosition = default, EntityId container = default)
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            if (!_session.Content.Items.TryGet(itemId, out ItemDefinition def)) return OpResult.Fail("unknown", $"Unknown item '{itemId}'.");
            if (quantity <= 0) return OpResult.Fail("quantity", "Choose a quantity of at least 1.");
            quantity = Math.Min(quantity, MaxSpawnQuantity);
            List<ItemStack> stacks = Stacks(def, quantity);

            switch (target)
            {
                case SpawnTarget.Backpack:
                {
                    if (_session.Player.Vitals.IsDead) return OpResult.Fail("dead", "You are dead.");
                    var overflow = new List<ItemStack>();
                    foreach (ItemStack st in stacks)
                        if (!_session.Player.Inventory.TryAddStack(st)) overflow.Add(st);
                    if (overflow.Count > 0)
                    {
                        _session.Construction.CreateDrop(overflow, _session.Player.Position, def.Name, false);
                        return OpResult.Success($"Spawned {quantity} {def.Name}; what did not fit is at your feet.");
                    }
                    return OpResult.Success($"Spawned {quantity} {def.Name} into the backpack.");
                }
                case SpawnTarget.World:
                    _session.Construction.CreateDrop(stacks, worldPosition, def.Name, false);
                    return OpResult.Success($"Spawned {quantity} {def.Name} on the ground.");
                default:
                {
                    Inventory inv = World.FindInventory(container);
                    if (inv == null) return OpResult.Fail("no_container", "Look at a container (or open one) to spawn into it.");
                    int added = 0;
                    foreach (ItemStack st in stacks)
                        if (inv.TryAddStack(st)) added += st.Quantity;
                    if (added == 0) return OpResult.Fail("full", $"{inv.Label} has no room for {def.Name}.");
                    return OpResult.Success(added < quantity ? $"Spawned {added} of {quantity} {def.Name} into {inv.Label} (full)." : $"Spawned {quantity} {def.Name} into {inv.Label}.");
                }
            }
        }

        /// <summary>Splits a quantity into stacks the item allows; durable items arrive in full condition.</summary>
        private static List<ItemStack> Stacks(ItemDefinition def, int quantity)
        {
            var list = new List<ItemStack>();
            int per = def.HasDurability ? 1 : Math.Max(1, def.MaxStack);
            for (int left = quantity; left > 0; left -= per)
                list.Add(new ItemStack(def.Id, Math.Min(per, left), def.HasDurability ? def.MaxDurability : 0));
            return list;
        }

        // ------------------------------------------------------------------ teleporting

        /// <summary>Every point of interest (developer tools know the whole map) plus the player's bookmarks.</summary>
        public List<TeleportDestination> Destinations()
        {
            var list = new List<TeleportDestination>();
            foreach (PoiInfo p in World.Layout.Pois)
                list.Add(new TeleportDestination { Name = p.Name, Kind = p.Kind, Position = p.Center });
            foreach (MapMarker m in World.Navigation.Markers)
                list.Add(new TeleportDestination { Name = m.Name, Kind = m.Kind == MarkerKind.Bookmark ? "bookmark" : "marker", Position = m.Position, MarkerId = m.Id });
            return list;
        }

        /// <summary>
        /// Checks a teleport target and records where the survivor was, so "Back" can return
        /// there. The presentation layer then finds safe ground at the target and moves the body.
        /// </summary>
        public OpResult BeginTeleport(Vec3 target)
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            if (_session.Player.Vitals.IsDead) return OpResult.Fail("dead", "You are dead.");
            if (!World.Layout.IsInsideMap(target.X, target.Z, 4f)) return OpResult.Fail("outside", $"({target.X:0}, {target.Z:0}) is outside the map.");
            _history.Add(_session.Player.Position);
            if (_history.Count > 16) _history.RemoveAt(0);
            return OpResult.Success();
        }

        public bool HasPrevious => _history.Count > 0;

        /// <summary>The last place the survivor teleported from (removed from the history).</summary>
        public bool TryPopPrevious(out Vec3 position)
        {
            position = default;
            if (_history.Count == 0) return false;
            position = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            return true;
        }

        // ------------------------------------------------------------------ world controls

        /// <summary>Jumps the clock forward to the next <paramref name="hour"/> (0–24) of the day.</summary>
        public OpResult SkipToHour(double hour)
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            SimulationClock clock = World.Clock;
            double delta = (hour / 24.0 - clock.TimeOfDay01 + 1.0) % 1.0 * clock.DayLengthSeconds;
            clock.Advance(delta);
            return OpResult.Success($"Time is now {clock.FormatTime()}.");
        }

        /// <summary>Holds the sky on one kind of weather until <see cref="ResumeWeather"/>.</summary>
        public OpResult HoldWeather(Afterfall.Domain.Weather.WeatherKind kind)
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            _session.Weather.Hold(kind);
            return OpResult.Success($"Weather held: {kind}.");
        }

        public OpResult ResumeWeather()
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            _session.Weather.Enabled = true;
            return OpResult.Success("The weather changes on its own again.");
        }

        /// <summary>Spawns one infected (for testing detection and combat).</summary>
        public OpResult SpawnInfected(Vec3 position, string archetype)
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            if (archetype == null || !_session.Content.Archetypes.ContainsKey(archetype)) return OpResult.Fail("unknown", $"Unknown infected type '{archetype}'.");
            _session.Zombies.Spawn(World.Layout.Ground(position.X, position.Z), archetype, World.ZombieRng);
            return OpResult.Success($"Spawned a {archetype}.");
        }

        /// <summary>Removes every infected from the world (testing).</summary>
        public OpResult RemoveAllInfected()
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            int n = World.Zombies.Count;
            World.Zombies.Clear();
            World.MarkChanged();
            return OpResult.Success($"Removed {n} infected.");
        }

        public OpResult AddBookmark(string name, Vec3 position)
        {
            if (!Enabled) return OpResult.Fail("locked", Locked);
            return World.Navigation.AddMarker(World.Ids.Allocate(), name, position, MarkerKind.Bookmark, out _);
        }
    }
}
