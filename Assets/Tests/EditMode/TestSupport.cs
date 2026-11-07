using System;
using System.IO;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;

namespace Afterfall.Tests
{
    /// <summary>Shared fixtures. Loads the real shipped content so tests exercise production data.</summary>
    public static class TestSupport
    {
        private static ContentDatabase _content;
        private static readonly object Lock = new object();

        public static ContentDatabase Content
        {
            get
            {
                lock (Lock)
                {
                    if (_content == null) _content = ContentJson.Load(ContentJson.FromLookup(name => File.ReadAllText(Path.Combine(ContentDirectory(), name + ".json"))));
                    return _content;
                }
            }
        }

        public static string ContentDirectory()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "Content"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "_Afterfall", "Resources", "Content")
            };
            foreach (string c in candidates)
                if (File.Exists(Path.Combine(c, "items.json"))) return c;
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string probe = Path.Combine(dir, "Assets", "_Afterfall", "Resources", "Content");
                if (File.Exists(Path.Combine(probe, "items.json"))) return probe;
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException("Could not locate Assets/_Afterfall/Resources/Content.");
        }

        /// <summary>A bare world (no generated terrain) for fast unit tests of systems.</summary>
        public static WorldState BareWorld(int seed = 1)
        {
            var w = new WorldState(seed, GameSettings.FromPreset(DifficultyPreset.Survival), new SimulationClock(), new IdAllocator());
            var p = new PlayerState { Id = w.Ids.Allocate(), Position = new Vec3(100, 20, 100) };
            p.Inventory = new Inventory(w.Ids.Allocate(), Content.Items, PlayerState.BackpackSlots, PlayerState.CarryMaxKg, null, "Backpack");
            w.Player = p;
            w.RegisterInventory(p.Inventory, p.Id);
            return w;
        }

        public static Inventory NewInventory(WorldState w, int slots = 10, float maxWeight = 0f, InventoryFilter filter = null)
        {
            var inv = new Inventory(w.Ids.Allocate(), Content.Items, slots, maxWeight, filter, "Test");
            return inv;
        }

        public static Inventory NewInventory(int slots = 10, float maxWeight = 0f, InventoryFilter filter = null, long id = 1000)
        {
            return new Inventory(new EntityId(id), Content.Items, slots, maxWeight, filter, "Test" + id);
        }

        /// <summary>Places and instantly completes a structure (bypassing build work) for system tests.</summary>
        public static Structure Build(WorldState w, string defId, Vec3 position)
        {
            BuildingDefinition def = Content.Building(defId);
            Structure s = StructureFactory.Create(w, def, position, 0f, StructureState.Complete);
            StructureFactory.InitializeComponents(w, Content, s);
            w.AddStructure(s);
            if (s.Power != null) PowerLinker.AutoConnect(w, Content, s);
            return s;
        }

        public static void Run(GameSession session, double seconds, double step = 0.05)
        {
            for (double t = 0; t < seconds; t += step) session.Update(step);
        }

        public static GameSession NewSession(int seed = 12345, DifficultyPreset preset = DifficultyPreset.Survival)
        {
            return NewGameFactory.Create(Content, seed, GameSettings.FromPreset(preset));
        }

        public static int TotalUnits(params Inventory[] inventories)
        {
            int n = 0;
            foreach (Inventory inv in inventories)
                for (int i = 0; i < inv.SlotCount; i++) n += inv.GetSlot(i).Quantity;
            return n;
        }
    }
}
