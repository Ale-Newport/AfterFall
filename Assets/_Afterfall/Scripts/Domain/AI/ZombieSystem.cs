using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
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

        /// <summary>Optional collision test: returns true if a body cannot stand at the position.</summary>
        public Func<Vec3, bool> IsBlocked { get; set; }

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
                    Vec3 next = zombie.Position + delta.Normalized * step;
                    if (IsBlocked == null || !IsBlocked(next)) zombie.Position = next;
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

        public const float BreachReach = 2.4f;
        public const float StallSeconds = 2.5f;
        public const float StructureDamageMultiplier = 1.5f;

        private readonly Dictionary<EntityId, double> _breachWarnings = new Dictionary<EntityId, double>();

        public string Name => "Infected AI";

        /// <summary>Used to damage player structures that block the infected (set by the session).</summary>
        public ConstructionService Construction { get; set; }
        public NoiseBoard Noise { get; } = new NoiseBoard();
        public IZombieWorldAdapter Adapter { get; set; } = new DirectZombieAdapter();

        /// <summary>Invoked when a zombie lands an attack on the player (damage already scaled by difficulty).</summary>
        public event Action<ZombieState, float> PlayerHit;

        /// <summary>Invoked when a zombie lands an attack on a colonist (colonist id, damage).</summary>
        public event Action<EntityId, float> ColonistHit;

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
                EntityId colonistTarget = EntityId.None;
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
                // Colonists are prey too: the nearest visible living one wins if closer than a visible player.
                foreach (Colony.ColonistState c in _world.Colonists.Values)
                {
                    if (!c.Recruited || c.Activity == Colony.ColonistActivity.Down) continue;
                    float cd = Vec3.Distance(z.Position, c.Position);
                    if (cd > a.SightRange * 1.2f || (p.TargetVisible && cd >= p.TargetDistance)) continue;
                    if (!PerceptionRules.CanSee(z, a, c.Position, daylight, false, false, Adapter.HasLineOfSight(z, c.Position))) continue;
                    p.TargetVisible = true;
                    p.TargetAlive = true;
                    p.TargetPosition = c.Position;
                    p.TargetDistance = cd;
                    colonistTarget = c.Id;
                }
                if (Noise.TryHear(z.Position, a.HearingMultiplier, out NoiseEvent heard))
                {
                    p.HeardNoise = true;
                    p.NoisePosition = heard.Position;
                }
                ZombieNavStatus nav = Adapter.GetNavStatus(z);
                p.ReachedDestination = nav.ReachedDestination;
                p.PathFailed = nav.PathFailed;

                if (TryBreach(z, a, p, fdt, now)) continue;

                ZombieDecision d = ZombieBrain.Think(z, a, p, _rng, fdt);
                Adapter.Apply(z, a, d, fdt);
                TrackProgress(z, d, fdt);

                if (d.Attack && colonistTarget.IsValid && Vec3.Distance(z.Position, p.TargetPosition) <= a.AttackRange * 1.3f)
                    ColonistHit?.Invoke(colonistTarget, a.AttackDamage * _world.Settings.EnemyDamage);
                else if (d.Attack && alive && Vec3.Distance(z.Position, player.Position) <= a.AttackRange * 1.3f)
                    PlayerHit?.Invoke(z, a.AttackDamage * _world.Settings.EnemyDamage);
            }
            foreach (EntityId id in toRemove)
            {
                _world.Zombies.Remove(id);
                _world.MarkChanged();
            }
        }

        /// <summary>
        /// Infected that stop making progress toward their goal next to a player-built blocking
        /// structure start battering it until it breaks or the survivor comes within reach.
        /// </summary>
        private bool TryBreach(ZombieState z, ZombieArchetype a, ZombiePerception p, float dt, double now)
        {
            if (Construction == null) return false;
            if (p.TargetVisible && p.TargetAlive && p.TargetDistance <= a.AttackRange)
            {
                z.BreachTarget = EntityId.None;
                return false;
            }
            if (!z.BreachTarget.IsValid)
            {
                if (z.StallTime < StallSeconds) return false;
                Structure blocker = NearestBlocker(z.Position, BreachReach);
                if (blocker == null) return false;
                z.BreachTarget = blocker.Id;
                z.StallTime = 0f;
            }
            if (!_world.Structures.TryGetValue(z.BreachTarget, out Structure target) || Vec3.DistanceXZ(target.Position, z.Position) > BreachReach + FootprintRadius(target))
            {
                z.BreachTarget = EntityId.None;
                return false;
            }

            z.AttackCooldown = Math.Max(0f, z.AttackCooldown - dt);
            var d = new ZombieDecision { FaceTarget = true, FacePosition = target.Position };
            if (z.AttackCooldown <= 0f)
            {
                d.Attack = true;
                z.AttackCooldown = a.AttackCooldown;
                Noise.Emit(target.Position, 14f, now, "breach");
                BuildingDefinition def = _content.Building(target.DefId);
                if (!_breachWarnings.TryGetValue(target.Id, out double last) || now - last > 20.0)
                {
                    _breachWarnings[target.Id] = now;
                    _bus?.Publish(new Notification($"The infected are breaking through your {def?.Name ?? "structure"}!", Severity.Danger));
                }
                if (Construction.Damage(target, a.AttackDamage * StructureDamageMultiplier * _world.Settings.EnemyDamage, "the infected"))
                    z.BreachTarget = EntityId.None;
            }
            Adapter.Apply(z, a, d, dt);
            return true;
        }

        private void TrackProgress(ZombieState z, ZombieDecision d, float dt)
        {
            bool pursuing = d.Move && (z.Mode == ZombieMode.Chase || z.Mode == ZombieMode.Investigate);
            if (!pursuing)
            {
                z.StallTime = 0f;
                z.LastGoalDistance = -1f;
                return;
            }
            float dist = Vec3.DistanceXZ(z.Position, d.Destination);
            if (z.LastGoalDistance < 0f || dist < z.LastGoalDistance - 0.1f)
            {
                z.LastGoalDistance = dist;
                z.StallTime = 0f;
            }
            else if (dist > 1.5f)
            {
                z.StallTime += dt;
            }
        }

        /// <summary>Nearest complete, player-built defensive structure that blocks movement within reach.</summary>
        public Structure NearestBlocker(Vec3 position, float reach)
        {
            Structure best = null;
            float bestD = float.MaxValue;
            foreach (Structure s in _world.Structures.Values)
            {
                if (!s.PlayerBuilt || !s.IsComplete) continue;
                BuildingDefinition def = _content.Building(s.DefId);
                // Only barriers are battered (walls, doorways, barricades), not crates or machines.
                if (def == null || !def.BlocksMovement || !def.IsDefensive) continue;
                float d = Vec3.DistanceXZ(s.Position, position) - FootprintRadius(s);
                if (d <= reach && d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>True if a point lies inside the footprint of a complete blocking player structure.</summary>
        public bool IsInsideBlocker(Vec3 p, float margin)
        {
            foreach (Structure s in _world.Structures.Values)
            {
                if (!s.PlayerBuilt || !s.IsComplete) continue;
                BuildingDefinition def = _content.Building(s.DefId);
                if (def == null || !def.BlocksMovement || def.Snap == SnapMode.Foundation || def.Snap == SnapMode.Roof) continue;
                double r = -s.Yaw * Math.PI / 180.0;
                float dx = p.X - s.Position.X, dz = p.Z - s.Position.Z;
                float lx = (float)(dx * Math.Cos(r) + dz * Math.Sin(r));
                float lz = (float)(-dx * Math.Sin(r) + dz * Math.Cos(r));
                if (Math.Abs(lx) <= def.SizeX * 0.5f + margin && Math.Abs(lz) <= def.SizeZ * 0.5f + margin) return true;
            }
            return false;
        }

        private float FootprintRadius(Structure s)
        {
            BuildingDefinition def = _content.Building(s.DefId);
            return def == null ? 0.5f : 0.5f * Math.Max(def.SizeX, def.SizeZ);
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
