using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.World;
using NUnit.Framework;

namespace Afterfall.Tests
{
    public class SurvivalTests
    {
        private static readonly SurvivalConfig Config = new SurvivalConfig();
        private const double SecondsPerHour = 48 * 60 / 24.0;

        [Test]
        public void Needs_DrainOverAGameDay()
        {
            var s = new SurvivalState { Hunger = 100, Thirst = 100 };
            for (int i = 0; i < 48 * 60; i++) SurvivalRules.Tick(s, Config, new PlayerActivity(), 1.0, SecondsPerHour, 1f);
            Assert.AreEqual(100 - 100 / Config.HungerDaysToEmpty, s.Hunger, 0.5);
            Assert.AreEqual(100 - 100 / Config.ThirstDaysToEmpty, s.Thirst, 0.5);
        }

        [Test]
        public void Starvation_CausesDamage_ButFedPlayerHeals()
        {
            var s = new SurvivalState { Hunger = 0, Thirst = 50, Health = 50 };
            SurvivalRules.Tick(s, Config, new PlayerActivity(), SecondsPerHour, SecondsPerHour, 1f);
            Assert.AreEqual(50 - Config.StarvingDamagePerHour, s.Health, 0.1);
            var fed = new SurvivalState { Hunger = 90, Thirst = 90, Health = 50 };
            SurvivalRules.Tick(fed, Config, new PlayerActivity(), SecondsPerHour, SecondsPerHour, 1f);
            Assert.Greater(fed.Health, 50);
        }

        [Test]
        public void Sprinting_DrainsStamina_RestingRegenerates()
        {
            var s = new SurvivalState();
            var sprint = new PlayerActivity { Moving = true, Sprinting = true };
            for (int i = 0; i < 30; i++) SurvivalRules.Tick(s, Config, sprint, 0.1, SecondsPerHour, 1f);
            Assert.AreEqual(100 - Config.SprintStaminaPerSecond * 3, s.Stamina, 0.5);
            float tired = s.Stamina;
            for (int i = 0; i < 30; i++) SurvivalRules.Tick(s, Config, new PlayerActivity(), 0.1, SecondsPerHour, 1f);
            Assert.Greater(s.Stamina, tired);
        }

        [Test]
        public void Consumables_ApplyEffectsWithinBounds()
        {
            var s = new SurvivalState { Hunger = 90, Health = 95 };
            SurvivalRules.Apply(s, new ConsumableEffect { Hunger = 35, Health = 20 });
            Assert.AreEqual(100, s.Hunger);
            Assert.AreEqual(100, s.Health);
        }

        [Test]
        public void Encumbrance_SlowsOnlyAboveComfortLimit()
        {
            Assert.AreEqual(1f, SurvivalRules.EncumbranceSpeedFactor(30, 35, 55));
            Assert.AreEqual(0.55f, SurvivalRules.EncumbranceSpeedFactor(55, 35, 55), 1e-4);
        }
    }

    public class ZombieBrainTests
    {
        private static readonly ZombieArchetype A = ZombieArchetype.Wanderer();
        private readonly DeterministicRandom _rng = new DeterministicRandom(9);

        private static ZombieState Zombie() => new ZombieState { Id = new EntityId(1), Position = new Vec3(0, 0, 0), Home = new Vec3(0, 0, 0), Health = 60, IdleTimer = 0.05f };

        [Test]
        public void Idle_EventuallyWanders()
        {
            ZombieState z = Zombie();
            ZombieDecision d = ZombieBrain.Think(z, A, new ZombiePerception { TargetAlive = true }, _rng, 0.1f);
            Assert.AreEqual(ZombieMode.Wander, z.Mode);
            Assert.IsTrue(d.Move);
            Assert.AreEqual(A.WalkSpeed, d.Speed);
        }

        [Test]
        public void Noise_TriggersInvestigation()
        {
            ZombieState z = Zombie();
            var p = new ZombiePerception { TargetAlive = true, HeardNoise = true, NoisePosition = new Vec3(10, 0, 0) };
            ZombieDecision d = ZombieBrain.Think(z, A, p, _rng, 0.1f);
            Assert.AreEqual(ZombieMode.Investigate, z.Mode);
            Assert.AreEqual(10f, d.Destination.X);
        }

        [Test]
        public void SeeingTarget_Chases_ThenAttacksInRangeWithCooldown()
        {
            ZombieState z = Zombie();
            var p = new ZombiePerception { TargetAlive = true, TargetVisible = true, TargetPosition = new Vec3(8, 0, 0), TargetDistance = 8 };
            ZombieDecision d = ZombieBrain.Think(z, A, p, _rng, 0.1f);
            Assert.AreEqual(ZombieMode.Chase, z.Mode);
            Assert.AreEqual(A.ChaseSpeed, d.Speed);

            p.TargetDistance = 1.2f;
            d = ZombieBrain.Think(z, A, p, _rng, 0.1f);
            Assert.AreEqual(ZombieMode.Attack, z.Mode);
            Assert.IsTrue(d.Attack);
            d = ZombieBrain.Think(z, A, p, _rng, 0.1f);
            Assert.IsFalse(d.Attack, "cooldown prevents an immediate second hit");
            for (int i = 0; i < 20; i++) d = ZombieBrain.Think(z, A, p, _rng, 0.1f);
            Assert.IsTrue(z.AttackCooldown < A.AttackCooldown);
        }

        [Test]
        public void LosingTarget_LeadsToSearch_ThenIdle()
        {
            ZombieState z = Zombie();
            var seen = new ZombiePerception { TargetAlive = true, TargetVisible = true, TargetPosition = new Vec3(8, 0, 0), TargetDistance = 8 };
            ZombieBrain.Think(z, A, seen, _rng, 0.1f);
            var hidden = new ZombiePerception { TargetAlive = true };
            for (int i = 0; i < (int)(A.LoseTargetSeconds / 0.1f) + 2; i++) ZombieBrain.Think(z, A, hidden, _rng, 0.1f);
            Assert.AreEqual(ZombieMode.Search, z.Mode);
            Assert.AreEqual(8f, z.LastKnownTarget.X, "searches the last known position, not the real one");
            for (int i = 0; i < (int)(A.SearchSeconds / 0.1f) + 2; i++) ZombieBrain.Think(z, A, hidden, _rng, 0.1f);
            Assert.AreEqual(ZombieMode.Idle, z.Mode);
        }

        [Test]
        public void DeadZombie_DoesNothing()
        {
            ZombieState z = Zombie();
            z.Mode = ZombieMode.Dead;
            ZombieDecision d = ZombieBrain.Think(z, A, new ZombiePerception { TargetAlive = true, TargetVisible = true, TargetDistance = 1 }, _rng, 0.1f);
            Assert.IsFalse(d.Move);
            Assert.IsFalse(d.Attack);
        }

        [Test]
        public void Perception_RespectsFovDarknessCrouchAndWalls()
        {
            ZombieState z = Zombie();
            z.Yaw = 0; // facing +Z
            Assert.IsTrue(PerceptionRules.CanSee(z, A, new Vec3(0, 0, 15), 1f, false, false, true));
            Assert.IsFalse(PerceptionRules.CanSee(z, A, new Vec3(0, 0, -15), 1f, false, false, true), "behind it");
            Assert.IsFalse(PerceptionRules.CanSee(z, A, new Vec3(0, 0, 15), 0f, false, false, true), "too dark at 15 m");
            Assert.IsFalse(PerceptionRules.CanSee(z, A, new Vec3(0, 0, 15), 1f, true, false, true), "crouching reduces range");
            Assert.IsFalse(PerceptionRules.CanSee(z, A, new Vec3(0, 0, 5), 1f, false, false, false), "walls block sight");
            Assert.IsTrue(PerceptionRules.CanSee(z, A, new Vec3(0, 0, -2), 1f, false, false, true), "sensed when very close");
        }
    }
}
