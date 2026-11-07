using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class PersistenceTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "afterfall_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static Dictionary<string, int> AllItems(WorldState w)
        {
            var totals = new Dictionary<string, int>();
            foreach (Inventory inv in w.AllInventories)
                foreach (var kv in inv.Totals())
                {
                    totals.TryGetValue(kv.Key, out int c);
                    totals[kv.Key] = c + kv.Value;
                }
            return totals;
        }

        private static GameSession BuildRichSession()
        {
            GameSession s = TestSupport.NewSession(2024);
            WorldState w = s.World;
            w.Tech.Unlocked.Add("metalworking");
            w.Tech.Unlocked.Add("electricity");
            s.Player.Inventory.TryAdd("planks", 13);
            s.Player.Inventory.TryAdd("stone_axe", 1);
            s.Player.Inventory.Wear(s.Player.Inventory.FirstEmptySlot() - 1, 17);
            Vec3 b = s.Player.Position;
            Structure furnace = TestSupport.Build(w, "furnace", new Vec3(b.X + 4, b.Y, b.Z));
            s.Machines.SetRecipe(furnace, "smelt_iron");
            furnace.Machine.Input.TryAdd("iron_ore", 6);
            furnace.Machine.Fuel.TryAdd("wood", 3);
            Structure gen = TestSupport.Build(w, "generator", new Vec3(b.X - 4, b.Y, b.Z));
            gen.Generator.Fuel.TryAdd("fuel_can", 2);
            Structure ws = TestSupport.Build(w, "workshop", new Vec3(b.X - 6, b.Y, b.Z + 2));
            s.Machines.SetRecipe(ws, "gears");
            ws.Machine.Input.TryAdd("iron_ingot", 5);
            Structure crate = TestSupport.Build(w, "storage_crate", new Vec3(b.X, b.Y, b.Z + 4));
            crate.Storage.TryAdd("scrap_metal", 33);
            crate.Storage.TryReserveItems("scrap_metal", 5, out _);
            s.PlaceBlueprint("wall", new Vec3(b.X + 8, b.Y, b.Z), 90, new PlacementProbe { OnSupport = true }, out Structure wall);
            wall.Delivered["planks"] = 2;
            s.Threats.SpawningEnabled = false;
            TestSupport.Run(s, 5); // machines mid-cycle, generator burning
            return s;
        }

        [Test]
        public void RoundTrip_PreservesItemsIdsMachinesAndPower()
        {
            GameSession s = BuildRichSession();
            WorldState w = s.World;
            string text = SaveSerializer.Serialize(w, "test", 123);
            var warnings = new List<string>();
            WorldState l = SaveSerializer.Deserialize(text, TestSupport.Content, warnings);

            Assert.IsEmpty(warnings, string.Join("\n", warnings));
            CollectionAssert.AreEquivalent(AllItems(w), AllItems(l), "no items created or destroyed");
            CollectionAssert.AreEquivalent(w.Structures.Keys, l.Structures.Keys);
            CollectionAssert.AreEquivalent(w.ResourceNodes.Keys, l.ResourceNodes.Keys);
            CollectionAssert.AreEquivalent(w.Zombies.Keys, l.Zombies.Keys);
            Assert.AreEqual(w.Ids.NextValue, l.Ids.NextValue);
            Assert.AreEqual(w.Rng.State, l.Rng.State);
            Assert.AreEqual(w.Clock.ElapsedSeconds, l.Clock.ElapsedSeconds, 1e-9);
            Assert.AreEqual(w.PowerLinks.Count, l.PowerLinks.Count);

            foreach (Structure a in w.Structures.Values.Where(x => x.Machine != null))
            {
                Structure b = l.Structures[a.Id];
                Assert.AreEqual(a.Machine.RecipeId, b.Machine.RecipeId);
                Assert.AreEqual(a.Machine.Progress, b.Machine.Progress, 1e-4);
                Assert.AreEqual(a.Machine.CycleActive, b.Machine.CycleActive);
                Assert.AreEqual(a.Machine.FuelBurnRemaining, b.Machine.FuelBurnRemaining, 1e-4);
                Assert.AreEqual(a.Machine.Input.Id, b.Machine.Input.Id, "inventory ids are stable");
                Assert.AreSame(b.Machine.Input, l.FindInventory(b.Machine.Input.Id));
            }
            Structure gen = w.Structures.Values.First(x => x.Generator != null);
            Assert.AreEqual(gen.Generator.BufferJoules, l.Structures[gen.Id].Generator.BufferJoules, 1e-6);
            Structure crate = w.Structures.Values.First(x => x.PlayerBuilt && x.Storage != null && x.Storage.Count("scrap_metal") > 0);
            Assert.AreEqual(28, l.Structures[crate.Id].Storage.Available("scrap_metal"), "reservations survive");
            Structure wall = w.Structures.Values.First(x => x.DefId == "wall");
            Assert.AreEqual(2, l.Structures[wall.Id].Delivered["planks"]);
            Assert.AreEqual(w.Player.Inventory.GetSlot(w.Player.Inventory.FirstEmptySlot() - 1).Durability,
                l.Player.Inventory.GetSlot(l.Player.Inventory.FirstEmptySlot() - 1).Durability);
            Assert.AreEqual(w.Exploration.RevealedFraction, l.Exploration.RevealedFraction, 1e-6);
            CollectionAssert.AreEquivalent(w.Tech.Unlocked, l.Tech.Unlocked);
            Assert.IsEmpty(SaveValidator.Validate(l));
        }

        [Test]
        public void ProductionContinues_AfterLoad()
        {
            GameSession s = BuildRichSession();
            string text = SaveSerializer.Serialize(s.World, "test", 0);
            var loaded = new GameSession(TestSupport.Content, SaveSerializer.Deserialize(text, TestSupport.Content, null));
            loaded.Threats.SpawningEnabled = false;
            Structure furnace = loaded.World.Structures.Values.First(x => x.DefId == "furnace");
            Structure ws = loaded.World.Structures.Values.First(x => x.DefId == "workshop");
            TestSupport.Run(loaded, 30);
            Assert.AreEqual(3, furnace.Machine.Output.Count("iron_ingot"));
            Assert.GreaterOrEqual(ws.Machine.Output.Count("gear"), 6, "powered production resumes immediately after load");
        }

        [Test]
        public void SaveTwice_ProducesIdenticalPayload()
        {
            GameSession s = BuildRichSession();
            string a = SaveSerializer.Serialize(s.World, "x", 0);
            WorldState l = SaveSerializer.Deserialize(a, TestSupport.Content, null);
            string b = SaveSerializer.Serialize(l, "x", 0);
            Assert.AreEqual(SaveSerializer.ParseObject(a)["payload"]["World"].ToString(), SaveSerializer.ParseObject(b)["payload"]["World"].ToString());
        }

        [Test]
        public void Checksum_DoesNotDependOnNumberFormatting()
        {
            // Unity's Mono prints some re-read doubles with 17 digits (0.874098659 → 0.87409865899999994).
            GameSession s = BuildRichSession();
            JObject env = SaveSerializer.ParseObject(SaveSerializer.Serialize(s.World, "x", 0));
            var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new SeventeenDigitWriter(text)) env.WriteTo(writer);
            StringAssert.Contains("0.", text.ToString());
            WorldState loaded = SaveSerializer.Deserialize(text.ToString(), TestSupport.Content, null);
            Assert.AreEqual(s.World.Structures.Count, loaded.Structures.Count);
        }

        private sealed class SeventeenDigitWriter : JsonTextWriter
        {
            public SeventeenDigitWriter(TextWriter w) : base(w)
            {
            }

            public override void WriteValue(double value) => WriteRawValue(value.ToString("G17", CultureInfo.InvariantCulture));
        }

        [Test]
        public void TamperedSave_IsRejected()
        {
            GameSession s = TestSupport.NewSession(3);
            string text = SaveSerializer.Serialize(s.World, "t", 0);
            JObject env = SaveSerializer.ParseObject(text);
            env["payload"]["World"]["Seed"] = 999;
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(env.ToString(), TestSupport.Content, null));
        }

        [Test]
        public void TruncatedSave_IsRejected()
        {
            GameSession s = TestSupport.NewSession(3);
            string text = SaveSerializer.Serialize(s.World, "t", 0);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(text.Substring(0, text.Length / 2), TestSupport.Content, null));
        }

        [Test]
        public void NewerSchema_IsRejected()
        {
            GameSession s = TestSupport.NewSession(3);
            JObject env = SaveSerializer.ParseObject(SaveSerializer.Serialize(s.World, "t", 0));
            env["schema"] = SaveSerializer.CurrentSchemaVersion + 1;
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(env.ToString(), TestSupport.Content, null));
        }

        [Test]
        public void OlderSchema_IsMigrated()
        {
            GameSession s = TestSupport.NewSession(3);
            JObject env = SaveSerializer.ParseObject(SaveSerializer.Serialize(s.World, "t", 0));
            // Simulate a hypothetical schema 0 file that stored the seed under a different name.
            var payload = (JObject)env["payload"];
            payload["World"]["LegacySeed"] = payload["World"]["Seed"];
            ((JObject)payload["World"]).Remove("Seed");
            env["schema"] = 0;
            env["checksum"] = SaveSerializer.Checksum(payload);
            SaveMigrations.Register(0, p =>
            {
                p["World"]["Seed"] = p["World"]["LegacySeed"];
                ((JObject)p["World"]).Remove("LegacySeed");
                return p;
            });
            try
            {
                var log = new List<string>();
                WorldState w = SaveSerializer.Deserialize(env.ToString(), TestSupport.Content, log);
                Assert.AreEqual(3, w.Seed);
                Assert.IsTrue(log.Any(x => x.Contains("Migrated")));
            }
            finally
            {
                SaveMigrations.Unregister(0);
            }
        }

        [Test]
        public void FileStore_WritesAtomically_AndRecoversFromBackup()
        {
            var store = new SaveFileStore(_dir);
            var service = new SaveService(store);
            GameSession s = TestSupport.NewSession(11);
            s.Player.Inventory.TryAdd("wood", 7);
            Assert.IsTrue(service.Save(s, "slot1", 10, out _));
            s.Player.Inventory.TryAdd("wood", 3);
            Assert.IsTrue(service.Save(s, "slot1", 20, out _));
            Assert.IsTrue(File.Exists(store.BackupPathFor("slot1")));
            Assert.IsFalse(File.Exists(store.PathFor("slot1") + ".tmp"));

            LoadResult ok = service.Load("slot1", TestSupport.Content);
            Assert.IsTrue(ok.Ok, ok.Error);
            Assert.AreEqual(10, ok.Session.Player.Inventory.Count("wood"));

            // Damage the main file: the previous save is recovered instead of losing everything.
            File.WriteAllText(store.PathFor("slot1"), "{ \"format\": \"afterfall-save\", \"payload\": {");
            LoadResult recovered = service.Load("slot1", TestSupport.Content);
            Assert.IsTrue(recovered.Ok, recovered.Error);
            Assert.IsTrue(recovered.RecoveredFromBackup);
            Assert.AreEqual(7, recovered.Session.Player.Inventory.Count("wood"));
        }

        [Test]
        public void FileStore_ListsSlotsWithHeaders()
        {
            var store = new SaveFileStore(_dir);
            var service = new SaveService(store);
            service.Save(TestSupport.NewSession(1), "alpha", 5, out _);
            service.Save(TestSupport.NewSession(2), "beta slot!", 6, out _);
            List<SaveSlotInfo> slots = store.List();
            Assert.AreEqual(2, slots.Count);
            Assert.IsTrue(slots.All(x => x.Readable));
            Assert.IsTrue(slots.Any(x => x.Slot == "beta_slot_"));
        }
    }
}
