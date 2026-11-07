using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class WorldGeneratorTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalLayout()
        {
            WorldLayout a = WorldGenerator.Generate(77);
            WorldLayout b = WorldGenerator.Generate(77);
            Assert.AreEqual(a.Nodes.Count, b.Nodes.Count);
            CollectionAssert.AreEqual(a.Height.Heights, b.Height.Heights);
            for (int i = 0; i < a.Nodes.Count; i++)
            {
                Assert.AreEqual(a.Nodes[i].DefId, b.Nodes[i].DefId);
                Assert.AreEqual(a.Nodes[i].Position, b.Nodes[i].Position);
            }
        }

        [Test]
        public void DifferentSeeds_VaryVegetationButKeepLandmarks()
        {
            WorldLayout a = WorldGenerator.Generate(1);
            WorldLayout b = WorldGenerator.Generate(2);
            Assert.AreNotEqual(a.Nodes.Count(n => n.DefId.StartsWith("tree")), b.Nodes.Count(n => n.DefId.StartsWith("tree")));
            CollectionAssert.AreEqual(a.Pois.Select(p => p.Id), b.Pois.Select(p => p.Id));
        }

        [Test]
        public void Layout_HasPrototypeContent()
        {
            WorldLayout l = WorldGenerator.Generate(12345);
            Assert.GreaterOrEqual(l.Pois.Count(p => p.Kind != "camp"), 6, "4-6 points of interest required");
            Assert.Greater(l.Nodes.Count(n => n.DefId.StartsWith("tree")), 300, "a real forest");
            Assert.Greater(l.Nodes.Count(n => n.DefId == "ore_iron"), 10);
            Assert.Greater(l.Containers.Count, 20);
            Assert.Greater(l.ZombieSpawnPoints.Count, 15);
            Assert.IsFalse(l.IsWater(l.PlayerSpawn.X, l.PlayerSpawn.Z), "spawn on dry land");
            Assert.IsTrue(l.IsWater(l.LakeCenter.X, l.LakeCenter.Z), "the lake holds water");
            foreach (NodeSeed n in l.Nodes) Assert.IsTrue(l.IsInsideMap(n.Position.X, n.Position.Z));
            Assert.IsTrue(l.Containers.Any(c => c.LootTable == "schematic"), "the generator schematic exists somewhere");
        }

        [Test]
        public void Terrain_IsFlattenedUnderPointsOfInterest()
        {
            WorldLayout l = WorldGenerator.Generate(5);
            foreach (PoiInfo p in l.Pois)
            {
                float c = l.HeightAt(p.Center.X, p.Center.Z);
                float edge = l.HeightAt(p.Center.X + p.Radius * 0.6f, p.Center.Z);
                Assert.Less(System.Math.Abs(c - edge), 0.75f, p.Id);
            }
        }

        [Test]
        public void Exploration_RevealsCellsAroundPosition()
        {
            var e = new ExplorationState(512);
            Assert.IsFalse(e.IsRevealed(100, 100));
            Assert.IsTrue(e.Reveal(new Vec3(100, 0, 100), 30));
            Assert.IsTrue(e.IsRevealed(100, 100));
            Assert.IsFalse(e.IsRevealed(300, 300));
            Assert.IsFalse(e.Reveal(new Vec3(100, 0, 100), 30), "nothing new the second time");
        }
    }

    public class ZombieIntegrationTests
    {
        [Test]
        public void DeathByDamage_IsHandledOnce()
        {
            GameSession s = TestSupport.NewSession(4242);
            s.Threats.SpawningEnabled = false;
            s.Player.Inventory.TryAdd("stone", 3);
            int died = 0, messages = 0;
            s.Events.Subscribe<PlayerDied>(e => died++);
            s.Events.Subscribe<Notification>(n => { if (n.Message.StartsWith("You died")) messages++; });
            s.DamagePlayer(1000f, "a test");
            TestSupport.Run(s, 2);
            Assert.AreEqual(1, died, "one death event");
            Assert.AreEqual(1, messages, "one death message");
            Assert.AreEqual(1, s.Player.Deaths);
            Assert.AreEqual(1, s.World.Drops.Values.Count(d => d.IsBelongings));
            Assert.IsTrue(s.Respawn().Ok);
            s.DamagePlayer(1000f, "a second test");
            TestSupport.Run(s, 1);
            Assert.AreEqual(2, died, "the next death is handled again");
        }

        [Test]
        public void Zombie_DetectsChasesAndDamagesPlayer_ThenDiesToMelee()
        {
            GameSession s = TestSupport.NewSession(4242);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            Vec3 p = s.Player.Position;
            ZombieState z = s.Zombies.Spawn(new Vec3(p.X + 10, p.Y, p.Z), "wanderer", new DeterministicRandom(1));
            z.Yaw = -90; // facing the player (-X)
            int hits = 0;
            s.Events.Subscribe<PlayerDamaged>(e => hits++);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.2); // midday
            TestSupport.Run(s, 8);
            Assert.Greater(hits, 0, "the zombie reached and attacked the player");
            Assert.Less(s.Player.Vitals.Health, 100);

            s.Player.Inventory.TryAdd("machete", 1);
            s.SelectHotbar(0);
            for (int i = 0; i < 6 && !z.IsDead; i++)
            {
                s.Player.NextActionTime = 0;
                s.Player.Vitals.Stamina = 100;
                s.MeleeAttack(new[] { z.Id });
            }
            Assert.IsTrue(z.IsDead);
            Assert.AreEqual(1, s.World.Metrics.ZombiesKilled);
        }

        [Test]
        public void CrouchingPlayerBehindZombie_IsNotNoticed()
        {
            GameSession s = TestSupport.NewSession(4243);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            Vec3 p = s.Player.Position;
            ZombieState z = s.Zombies.Spawn(new Vec3(p.X + 8, p.Y, p.Z), "wanderer", new DeterministicRandom(1));
            z.Yaw = 90; // facing away (+X)
            z.Mode = ZombieMode.Idle;
            z.IdleTimer = 100;
            s.ReportPlayerTransform(p, 0, new PlayerActivity { Crouching = true });
            TestSupport.Run(s, 3);
            Assert.AreNotEqual(ZombieMode.Chase, z.Mode);
            Assert.AreNotEqual(ZombieMode.Attack, z.Mode);
        }
    }

    public class ThreatDirectorTests
    {
        [Test]
        public void Spawns_AreNeverNearPlayerOrInsideBase()
        {
            GameSession s = TestSupport.NewSession(9);
            Structure crate = TestSupport.Build(s.World, "storage_crate", s.World.Layout.ZombieSpawnPoints[0]);
            foreach (Vec3 p in s.World.Layout.ZombieSpawnPoints)
            {
                bool valid = s.Threats.IsValidSpawn(p, ThreatDirector_MinDistance);
                if (Vec3.DistanceXZ(p, crate.Position) < Afterfall.Domain.Threats.ThreatDirector.BaseExclusionRadius) Assert.IsFalse(valid);
                if (Vec3.Distance(p, s.Player.Position) < ThreatDirector_MinDistance) Assert.IsFalse(valid);
            }

            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.6); // night
            TestSupport.Run(s, 60, 0.1);
            Assert.Greater(s.World.Threat.SpawnedTotal, 0, "the director replenishes the population at night");
            foreach (ZombieState z in s.World.Zombies.Values)
            {
                Assert.GreaterOrEqual(Vec3.DistanceXZ(z.Home, crate.Position), Afterfall.Domain.Threats.ThreatDirector.BaseExclusionRadius - 4f, "never spawned inside the base");
                Assert.GreaterOrEqual(Vec3.Distance(z.Home, s.Player.Position), ThreatDirector_MinDistance - 4f, "never spawned next to the player");
            }
        }

        private const float ThreatDirector_MinDistance = Afterfall.Domain.Threats.ThreatDirector.MinSpawnDistance;

        [Test]
        public void NoisyBase_GetsAnnouncedRaidBeforeArrival()
        {
            GameSession s = TestSupport.NewSession(10);
            var warnings = new System.Collections.Generic.List<string>();
            s.Events.Subscribe<ThreatWarningIssued>(e => warnings.Add(e.Message));
            Vec3 basePos = s.Player.Position;
            TestSupport.Build(s.World, "storage_crate", basePos);
            s.World.Threat.Heat = 60f;
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.6);
            int zombiesBefore = s.World.Zombies.Count;
            TestSupport.Run(s, 4, 0.1);
            Assert.IsTrue(warnings.Any(w => w.Contains("approaches")), "a warning is issued first");
            Assert.Greater(s.World.Threat.PendingRaidAt, s.Now, "the group has not arrived yet");
            TestSupport.Run(s, Afterfall.Domain.Threats.ThreatDirector.RaidWarningLead + 3, 0.1);
            Assert.Less(s.World.Threat.PendingRaidAt, 0, "the announced group arrived");
            Assert.GreaterOrEqual(s.World.Zombies.Values.Count(z => z.Mode == ZombieMode.Investigate || z.Mode == ZombieMode.Chase || z.Mode == ZombieMode.Search), 1);
        }
    }
}

namespace Afterfall.Tests
{
    public class BreachTests
    {
        [Test]
        public void Zombie_BattersBarricadeInItsPath_ThenReachesPlayer()
        {
            GameSession s = TestSupport.NewSession(5150);
            s.Threats.SpawningEnabled = false;
            foreach (EntityId id in s.World.Zombies.Keys.ToList()) s.World.Zombies.Remove(id);
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.2); // midday
            Vec3 p = s.Player.Position;
            Structure barricade = TestSupport.Build(s.World, "barricade", new Vec3(p.X + 4f, p.Y, p.Z));
            barricade.Yaw = 90f; // spans the zombie's straight path
            ZombieState z = s.Zombies.Spawn(new Vec3(p.X + 8f, p.Y, p.Z), "wanderer", new DeterministicRandom(3));
            z.Yaw = -90f;
            var warnings = new System.Collections.Generic.List<string>();
            s.Events.Subscribe<Notification>(n => warnings.Add(n.Message));
            int hits = 0;
            s.Events.Subscribe<PlayerDamaged>(e => hits++);

            TestSupport.Run(s, 6, 0.1);
            Assert.Greater(z.Position.X, p.X + 4.3f, "the barricade stops the zombie");
            Assert.AreEqual(0, hits);
            Assert.IsTrue(z.BreachTarget.IsValid, "a stalled zombie starts battering the barrier");
            Assert.Less(barricade.Health, 500f);
            Assert.IsTrue(warnings.Any(w => w.Contains("breaking through")), "the player is warned");

            TestSupport.Run(s, 60, 0.1);
            Assert.IsFalse(s.World.Structures.ContainsKey(barricade.Id), "the barricade eventually breaks");
            Assert.Greater(hits, 0, "then the zombie reaches the player");
        }

        [Test]
        public void Zombie_DoesNotBatterCratesOrMachines()
        {
            GameSession s = TestSupport.NewSession(5151);
            Structure crate = TestSupport.Build(s.World, "storage_crate", s.Player.Position);
            Assert.IsNull(s.Zombies.NearestBlocker(s.Player.Position, 3f));
            Structure wall = TestSupport.Build(s.World, "barricade", new Vec3(s.Player.Position.X + 1f, s.Player.Position.Y, s.Player.Position.Z));
            Assert.AreEqual(wall.Id, s.Zombies.NearestBlocker(s.Player.Position, 3f).Id);
            Assert.IsNotNull(crate);
        }
    }
}

namespace Afterfall.Tests
{
    public class ArchetypeTests
    {
        [Test]
        public void Runner_IsFasterThanRunningSurvivorButFragile()
        {
            var runner = TestSupport.Content.Archetype("runner");
            var wanderer = TestSupport.Content.Archetype("wanderer");
            Assert.AreEqual("runner", runner.Id);
            Assert.Greater(runner.ChaseSpeed, 4.6f, "outruns a running survivor — sprint to escape");
            Assert.Less(runner.ChaseSpeed, 7.2f, "a sprinting survivor can still escape");
            Assert.Less(runner.MaxHealth, wanderer.MaxHealth);
        }

        [Test]
        public void NightSpawns_IncludeRunners()
        {
            GameSession s = TestSupport.NewSession(31337);
            int runners = 0;
            for (int i = 0; i < 400; i++) if (s.Threats.PickArchetype(true) == "runner") runners++;
            Assert.Greater(runners, 60);
            Assert.Less(runners, 140);
        }
    }
}
