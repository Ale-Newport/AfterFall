using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;

namespace Afterfall.Domain.AI
{
    /// <summary>Navigation feedback for a zombie's current destination.</summary>
    public struct ZombieNavStatus
    {
        public bool ReachedDestination;
        public bool PathFailed;
    }

    /// <summary>
    /// Bridges the pure AI to a physical world. The game implements it with raycasts and
    /// NavMeshAgents; tests use <see cref="DirectZombieAdapter"/>.
    /// </summary>
    public interface IZombieWorldAdapter
    {
        bool HasLineOfSight(ZombieState zombie, Vec3 target);
        ZombieNavStatus GetNavStatus(ZombieState zombie);
        void Apply(ZombieState zombie, ZombieArchetype archetype, ZombieDecision decision, float dt);
    }

    /// <summary>Straight-line movement with unobstructed sight. Used headless and in tests.</summary>
    public sealed class DirectZombieAdapter : IZombieWorldAdapter
    {
        private readonly Dictionary<EntityId, Vec3> _destinations = new Dictionary<EntityId, Vec3>();

        public bool HasLineOfSight(ZombieState zombie, Vec3 target) => true;

        public ZombieNavStatus GetNavStatus(ZombieState zombie)
        {
            if (!_destinations.TryGetValue(zombie.Id, out Vec3 dest)) return new ZombieNavStatus { ReachedDestination = true };
            return new ZombieNavStatus { ReachedDestination = Vec3.DistanceXZ(zombie.Position, dest) < 0.8f };
        }

        public void Apply(ZombieState zombie, ZombieArchetype archetype, ZombieDecision decision, float dt)
        {
            if (decision.Move)
            {
                _destinations[zombie.Id] = decision.Destination;
                Vec3 delta = decision.Destination - zombie.Position;
                delta.Y = 0;
                float dist = delta.Magnitude;
                if (dist > 0.05f)
                {
                    float step = Math.Min(dist, decision.Speed * dt);
                    zombie.Position = zombie.Position + delta.Normalized * step;
                    zombie.Yaw = (float)(Math.Atan2(delta.X, delta.Z) * 180.0 / Math.PI);
                }
            }
            if (decision.FaceTarget)
            {
                Vec3 delta = decision.FacePosition - zombie.Position;
                zombie.Yaw = (float)(Math.Atan2(delta.X, delta.Z) * 180.0 / Math.PI);
            }
        }
    }

    /// <summary>Runs perception and decision making for every infected at a fixed rate (10 Hz).</summary>
    public sealed class ZombieSystem : ISimulationSystem
    {
        public const float CorpseSeconds = 45f;

        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly DeterministicRandom _rng;

        public ZombieSystem(WorldState world, ContentDatabase content, EventBus bus)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _rng = new DeterministicRandom((ulong)(uint)world.Seed ^ 0xA5A5A5A5UL);
        }

        public string Name => "Infected AI";
        public NoiseBoard Noise { get; } = new NoiseBoard();
        public IZombieWorldAdapter Adapter { get; set; } = new DirectZombieAdapter();

        /// <summary>Invoked when a zombie lands an attack on the player (damage already scaled by difficulty).</summary>
        public event Action<ZombieState, float> PlayerHit;

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (ZombieState z in _world.Zombies.Values)
                    if (!z.IsDead) n++;
                return n;
            }
        }

        public void Tick(double dt)
        {
            float fdt = (float)dt;
            double now = _world.Clock.ElapsedSeconds;
            Noise.Expire(now);
            PlayerState player = _world.Player;
            bool alive = player != null && !player.Vitals.IsDead;
            float daylight = _world.Clock.Daylight01;
            var toRemove = new List<EntityId>();

            var ids = new List<EntityId>(_world.Zombies.Keys);
            ids.Sort();
            foreach (EntityId id in ids)
            {
                ZombieState z = _world.Zombies[id];
                if (z.IsDead)
                {
                    z.DeadTime += fdt;
                    if (z.DeadTime > CorpseSeconds) toRemove.Add(id);
                    continue;
                }
                ZombieArchetype a = _content.Archetype(z.Archetype);
                var p = new ZombiePerception { TargetAlive = alive };
                if (player != null)
                {
                    p.TargetPosition = player.Position;
                    p.TargetDistance = Vec3.Distance(z.Position, player.Position);
                    if (alive && p.TargetDistance <= a.SightRange * 1.2f)
                    {
                        bool los = Adapter.HasLineOfSight(z, player.Position);
                        p.TargetVisible = PerceptionRules.CanSee(z, a, player.Position, daylight, player.Activity.Crouching, player.Activity.Sprinting, los);
                    }
                }
                if (Noise.TryHear(z.Position, a.HearingMultiplier, out NoiseEvent heard))
                {
                    p.HeardNoise = true;
                    p.NoisePosition = heard.Position;
                }
                ZombieNavStatus nav = Adapter.GetNavStatus(z);
                p.ReachedDestination = nav.ReachedDestination;
                p.PathFailed = nav.PathFailed;

                ZombieDecision d = ZombieBrain.Think(z, a, p, _rng, fdt);
                Adapter.Apply(z, a, d, fdt);

                if (d.Attack && alive && Vec3.Distance(z.Position, player.Position) <= a.AttackRange * 1.3f)
                    PlayerHit?.Invoke(z, a.AttackDamage * _world.Settings.EnemyDamage);
            }
            foreach (EntityId id in toRemove)
            {
                _world.Zombies.Remove(id);
                _world.MarkChanged();
            }
        }

        public ZombieState Spawn(Vec3 position, string archetype, DeterministicRandom rng)
        {
            ZombieArchetype a = _content.Archetype(archetype);
            var z = new ZombieState
            {
                Id = _world.Ids.Allocate(),
                Archetype = a.Id,
                Position = position,
                Home = position,
                Health = a.MaxHealth,
                Yaw = rng.Range(0f, 360f),
                Mode = ZombieMode.Idle,
                IdleTimer = rng.Range(0.5f, 4f)
            };
            _world.Zombies[z.Id] = z;
            _world.MarkChanged();
            _bus?.Publish(new ZombieSpawned(z.Id));
            return z;
        }

        /// <summary>Damages a zombie. Being hit reveals the attacker's position. Returns true if it died.</summary>
        public bool Damage(EntityId id, float amount, Vec3 attackerPosition)
        {
            if (!_world.Zombies.TryGetValue(id, out ZombieState z) || z.IsDead || amount <= 0f) return false;
            z.Health -= amount;
            if (z.Health <= 0f)
            {
                z.Health = 0f;
                z.Mode = ZombieMode.Dead;
                z.ModeTime = 0f;
                _world.Metrics.ZombiesKilled++;
                _bus?.Publish(new ZombieKilled(z.Id, z.Position));
                return true;
            }
            z.LastKnownTarget = attackerPosition;
            z.TimeSinceSeen = 0f;
            if (z.Mode != ZombieMode.Attack) z.Mode = ZombieMode.Chase;
            return false;
        }
    }
}
