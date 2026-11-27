using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Game modes, the creative item browser, creative construction, flight rules and map markers.</summary>
    public sealed class CreativeTests
    {
        private static GameSession Creative()
        {
            GameSettings settings = GameSettings.FromPreset(DifficultyPreset.Survival);
            settings.Mode = GameMode.Creative;
            GameSession s = NewGameFactory.Create(TestSupport.Content, 4242, settings);
            s.Threats.SpawningEnabled = false;
            return s;
        }

        [Test]
        public void ItemBrowser_ListsTheWholeCatalogue_AndFilters()
        {
            GameSession s = TestSupport.NewSession();
            List<ItemDefinition> all = s.Creative.SearchItems("");
            Assert.AreEqual(TestSupport.Content.Items.Count, all.Count, "every registered item is listed, none by hand");
            CollectionAssert.AreEquivalent(TestSupport.Content.Items.All.Select(d => d.Id), all.Select(d => d.Id));

            List<ItemDefinition> axes = s.Creative.SearchItems("AXE");
            CollectionAssert.Contains(axes.Select(d => d.Id).ToList(), "stone_axe");
            CollectionAssert.Contains(axes.Select(d => d.Id).ToList(), "stone_pickaxe");
            Assert.IsTrue(axes.All(d => d.Name.ToLowerInvariant().Contains("axe") || d.Id.Contains("axe")));

            List<ItemDefinition> fuel = s.Creative.SearchItems("fuel");
            CollectionAssert.Contains(fuel.Select(d => d.Id).ToList(), "wood", "tags match too");

            List<ItemDefinition> tools = s.Creative.SearchItems("", ItemCategory.Tool);
            Assert.IsTrue(tools.Count > 0 && tools.All(d => d.Category == ItemCategory.Tool));
        }

        [Test]
        public void Survival_RefusesSpawning_UntilDeveloperToolsAreEnabled_AndMarksTheSave()
        {
            GameSession s = TestSupport.NewSession();
            Assert.IsFalse(s.Creative.Enabled);
            OpResult refused = s.Creative.SpawnItem("planks", 10, SpawnTarget.Backpack);
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual(0, s.Player.Inventory.Count("planks"));
            Assert.AreEqual("Survival", s.World.Settings.ModeLabel());

            Assert.IsTrue(s.Creative.EnableDeveloperTools().Ok);
            Assert.IsTrue(s.Creative.Enabled);
            Assert.IsFalse(s.World.Settings.IsCreative(), "developer tools do not switch the rules to Creative");
            Assert.AreEqual("Survival · developer tools", s.World.Settings.ModeLabel());

            Assert.IsTrue(s.Creative.SpawnItem("planks", 10, SpawnTarget.Backpack).Ok);
            Assert.AreEqual(10, s.Player.Inventory.Count("planks"));
        }

        [Test]
        public void SpawnItem_Backpack_World_Container_AndOverflowIsNeverLost()
        {
            GameSession s = Creative();
            int drops = s.World.Drops.Count;

            Assert.IsTrue(s.Creative.SpawnItem("hammer", 2, SpawnTarget.Backpack).Ok);
            ItemDefinition hammer = TestSupport.Content.Items.Get("hammer");
            int hammers = 0;
            for (int i = 0; i < s.Player.Inventory.SlotCount; i++)
            {
                ItemStack st = s.Player.Inventory.GetSlot(i);
                if (st.ItemId != "hammer") continue;
                hammers += st.Quantity;
                Assert.AreEqual(hammer.MaxDurability, st.Durability, "durable items arrive in full condition");
            }
            Assert.AreEqual(2, hammers);

            // Far more than a backpack holds: the rest lands at the survivor's feet.
            OpResult lots = s.Creative.SpawnItem("stone", 999, SpawnTarget.Backpack);
            Assert.IsTrue(lots.Ok, lots.Message);
            int inBag = s.Player.Inventory.Count("stone");
            int onGround = s.World.Drops.Values.Sum(d => d.Contents.Count("stone"));
            Assert.AreEqual(999, inBag + onGround, "nothing created beyond the request, nothing lost");
            Assert.Greater(onGround, 0);

            Vec3 spot = s.Player.Position + new Vec3(3, 0, 0);
            Assert.IsTrue(s.Creative.SpawnItem("canned_food", 4, SpawnTarget.World, spot).Ok);
            Assert.IsTrue(s.World.Drops.Values.Any(d => d.Contents.Count("canned_food") == 4 && Vec3.DistanceXZ(d.Position, spot) < 0.01f));
            Assert.Greater(s.World.Drops.Count, drops);

            Structure crate = s.World.Structures.Values.First(x => x.Storage != null && !x.PlayerBuilt);
            int before = crate.Storage.Count("rope");
            Assert.IsTrue(s.Creative.SpawnItem("rope", 3, SpawnTarget.Container, default, crate.Storage.Id).Ok);
            Assert.AreEqual(before + 3, crate.Storage.Count("rope"));

            Assert.IsFalse(s.Creative.SpawnItem("no_such_item", 1, SpawnTarget.Backpack).Ok);
            Assert.IsFalse(s.Creative.SpawnItem("rope", 1, SpawnTarget.Container, default, new EntityId(999999)).Ok);
        }

        [Test]
        public void CreativeConstruction_IsFreeInstantAndIgnoresTechnology_ButKeepsPhysicalRules()
        {
            GameSession s = Creative();
            Vec3 p = s.Player.Position;
            Assert.IsFalse(s.World.Tech.IsUnlocked(TestSupport.Content.Building("generator").RequiredTech), "precondition: generator still locked");

            PlacementResult check = s.EvaluatePlacement("generator", new PlacementProbe { Distance = 3f });
            Assert.IsTrue(check.Valid, string.Join(" ", check.Reasons));
            Assert.IsEmpty(check.MissingMaterials);

            OpResult placed = s.PlaceBlueprint("generator", new Vec3(p.X + 5, p.Y, p.Z), 0f, new PlacementProbe { Distance = 3f }, out Structure gen);
            Assert.IsTrue(placed.Ok, placed.Message);
            Assert.IsTrue(gen.IsComplete, "built instantly");
            Assert.IsNotNull(gen.Generator, "a creative build is a real, working structure");
            Assert.AreEqual(0, s.Player.Inventory.TotalWeight, 1e-4, "nothing was consumed or required");

            PlacementResult wet = s.EvaluatePlacement("generator", new PlacementProbe { InWater = true });
            Assert.IsFalse(wet.Valid, "placement rules still apply in creative");
            PlacementResult steep = s.EvaluatePlacement("generator", new PlacementProbe { SlopeDegrees = 80f });
            Assert.IsFalse(steep.Valid);

            // Removal refunds nothing, and world containers can be removed too.
            Structure cabinet = s.World.Structures.Values.First(x => !x.PlayerBuilt && x.IsComplete);
            s.Player.Position = cabinet.Position;
            Assert.IsTrue(s.Demolish(cabinet.Id).Ok);
            Assert.IsFalse(s.World.Structures.ContainsKey(cabinet.Id));
            s.Player.Position = gen.Position;
            Assert.IsTrue(s.Demolish(gen.Id).Ok);
            Assert.AreEqual(0, s.Player.Inventory.TotalWeight, 1e-4, "no refund for something free");
        }

        [Test]
        public void SurvivalConstruction_IsUnchanged()
        {
            GameSession s = TestSupport.NewSession();
            PlacementResult check = s.EvaluatePlacement("generator", new PlacementProbe { Distance = 3f });
            Assert.IsFalse(check.Valid, "technology still required in survival");
            OpResult placed = s.PlaceBlueprint("storage_crate", s.Player.Position + new Vec3(3, 0, 0), 0f, new PlacementProbe { Distance = 3f }, out Structure crate);
            Assert.IsTrue(placed.Ok, placed.Message);
            Assert.IsFalse(crate.IsComplete, "survival places a blueprint");
            Structure cabinet = s.World.Structures.Values.First(x => !x.PlayerBuilt && x.IsComplete);
            s.Player.Position = cabinet.Position;
            Assert.IsFalse(s.Demolish(cabinet.Id).Ok, "world containers stay in survival");
        }

        [Test]
        public void Creative_KeepsTheSurvivorUnharmed_SurvivalDoesNot()
        {
            GameSession c = Creative();
            c.Player.Vitals.Hunger = 10f;
            c.DamagePlayer(500f, "test");
            TestSupport.Run(c, 2);
            Assert.IsFalse(c.Player.Vitals.IsDead);
            Assert.AreEqual(100f, c.Player.Vitals.Hunger, 1e-3);

            GameSession s = TestSupport.NewSession();
            s.DamagePlayer(30f, "test");
            Assert.Less(s.Player.Vitals.Health, 100f);
        }

        [Test]
        public void Flying_RevealsNothing_AndTheInfectedIgnoreTheSurvivor()
        {
            GameSession s = TestSupport.NewSession();
            s.Threats.SpawningEnabled = false;
            Vec3 far = new Vec3(420, 60, 420);
            Assert.IsFalse(s.World.Exploration.IsRevealed(far.X, far.Z));
            s.ReportPlayerTransform(far, 0f, new PlayerActivity { Flying = true });
            TestSupport.Run(s, 2);
            Assert.IsFalse(s.World.Exploration.IsRevealed(far.X, far.Z), "flying over land does not discover it");

            Vec3 ground = s.World.Layout.Ground(far.X, far.Z);
            var z = s.Zombies.Spawn(ground + new Vec3(4, 0, 0), "runner", DeterministicRandom.Derive(1, "t"));
            s.ReportPlayerTransform(ground + new Vec3(0, 1, 0), 0f, new PlayerActivity { Flying = true });
            TestSupport.Run(s, 3);
            Assert.AreNotEqual(Afterfall.Domain.AI.ZombieMode.Chase, z.Mode, "a flying survivor is not perceived");
            Assert.AreEqual(100f, s.Player.Vitals.Health, 1e-3);

            s.ReportPlayerTransform(ground, 0f, new PlayerActivity());
            TestSupport.Run(s, 1);
            Assert.IsTrue(s.World.Exploration.IsRevealed(far.X, far.Z), "landing reveals the surroundings");
        }

        [Test]
        public void Teleport_RecordsWhereYouCameFrom_AndRefusesOutsideTheMap()
        {
            GameSession s = Creative();
            Vec3 start = s.Player.Position;
            List<TeleportDestination> dest = s.Creative.Destinations();
            Assert.AreEqual(s.World.Layout.Pois.Count, dest.Count);
            Assert.IsTrue(s.Creative.BeginTeleport(dest[1].Position).Ok);
            Assert.IsFalse(s.Creative.BeginTeleport(new Vec3(-50, 0, 10)).Ok);
            Assert.IsTrue(s.Creative.TryPopPrevious(out Vec3 back));
            Assert.AreEqual(start, back);
            Assert.IsFalse(s.Creative.HasPrevious);

            Assert.IsTrue(s.Creative.AddBookmark("Ridge view", new Vec3(300, 40, 300)).Ok);
            Assert.AreEqual(s.World.Layout.Pois.Count + 1, s.Creative.Destinations().Count);
        }

        [Test]
        public void ModeMarkersAndWaypoint_SurviveSaveAndLoad()
        {
            GameSession s = TestSupport.NewSession();
            s.Creative.EnableDeveloperTools();
            Assert.IsTrue(s.SetWaypoint(new Vec3(400, 0, 300), "Hartley's").Ok);
            Assert.IsTrue(s.AddMapMarker("Berries", new Vec3(280, 0, 140)).Ok);
            s.Creative.AddBookmark("Lookout", new Vec3(250, 0, 450));

            string text = SaveSerializer.Serialize(s.World, "t", 1);
            WorldState l = SaveSerializer.Deserialize(text, TestSupport.Content, new List<string>());
            Assert.IsTrue(l.Settings.DeveloperTools);
            Assert.AreEqual(GameMode.Survival, l.Settings.Mode);
            Assert.IsTrue(l.Navigation.HasWaypoint);
            Assert.AreEqual("Hartley's", l.Navigation.WaypointLabel);
            Assert.AreEqual(s.World.Navigation.Waypoint, l.Navigation.Waypoint);
            Assert.AreEqual(2, l.Navigation.Markers.Count);
            Assert.AreEqual(MarkerKind.Bookmark, l.Navigation.Markers.Single(m => m.Name == "Lookout").Kind);
            Assert.IsTrue(l.Ids.NextValue > l.Navigation.Markers.Max(m => m.Id.Value), "marker ids are never reused");

            // A save written before modes and markers existed loads as plain Survival.
            var json = Newtonsoft.Json.Linq.JObject.Parse(text);
            var payload = (Newtonsoft.Json.Linq.JObject)json["payload"];
            var world = (Newtonsoft.Json.Linq.JObject)payload["World"];
            world.Remove("Navigation");
            var settings = (Newtonsoft.Json.Linq.JObject)world["Settings"];
            settings.Remove("Mode");
            settings.Remove("DeveloperTools");
            settings.Remove("WorldVersion");
            WorldState old = SaveSerializer.Deserialize(Resign(json), TestSupport.Content, new List<string>());
            Assert.AreEqual(GameMode.Survival, old.Settings.Mode);
            Assert.IsFalse(old.Settings.DeveloperTools);
            Assert.AreEqual(1, old.Settings.WorldVersion);
            Assert.IsFalse(old.Navigation.HasWaypoint);
            Assert.IsEmpty(old.Navigation.Markers);
        }

        [Test]
        public void Waypoint_SetAndClear()
        {
            GameSession s = TestSupport.NewSession();
            Assert.IsFalse(s.ClearWaypoint().Ok);
            Assert.IsFalse(s.SetWaypoint(new Vec3(-5, 0, 5), "x").Ok);
            Assert.IsTrue(s.SetWaypoint(new Vec3(300, 0, 230), "").Ok);
            Assert.AreEqual("Waypoint", s.World.Navigation.WaypointLabel);
            Assert.AreEqual(s.World.Layout.HeightAt(300, 230), s.World.Navigation.Waypoint.Y, 1e-3, "waypoints sit on the ground");
            Assert.IsTrue(s.ClearWaypoint().Ok);
            Assert.IsFalse(s.World.Navigation.HasWaypoint);
            Assert.AreEqual(0f, MapGeometry.Bearing(new Vec3(0, 0, 0), new Vec3(0, 0, 10)), 1e-3);
            Assert.AreEqual(90f, MapGeometry.Bearing(new Vec3(0, 0, 0), new Vec3(10, 0, 0)), 1e-3);
            Assert.AreEqual("SW", MapGeometry.Cardinal(225f));
            Assert.AreEqual("2.4 km", MapGeometry.Distance(2400f));
        }

        /// <summary>Recomputes the checksum of an edited save envelope.</summary>
        private static string Resign(Newtonsoft.Json.Linq.JObject envelope)
        {
            var payload = (Newtonsoft.Json.Linq.JObject)envelope["payload"];
            envelope["checksum"] = SaveSerializer.Checksum(SaveSerializer.ParseObject(payload.ToString(Newtonsoft.Json.Formatting.None)));
            return envelope.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
