using Afterfall.Core;

namespace Afterfall.Domain.AI
{
    public enum ZombieMode
    {
        Idle,
        Wander,
        Investigate,
        Chase,
        Attack,
        Search,
        Dead
    }

    /// <summary>Tunable behaviour profile. Only the Wanderer is used in the prototype.</summary>
    public sealed class ZombieArchetype
    {
        public string Id { get; set; } = "wanderer";
        public string Name { get; set; } = "Wanderer";
        public float MaxHealth { get; set; } = 60f;
        public float WalkSpeed { get; set; } = 0.9f;
        public float InvestigateSpeed { get; set; } = 1.4f;
        public float ChaseSpeed { get; set; } = 3.1f;
        public float SightRange { get; set; } = 22f;
        public float SightFovDegrees { get; set; } = 130f;

        /// <summary>Within this distance the target is sensed regardless of facing.</summary>
        public float ProximitySense { get; set; } = 2.5f;

        public float HearingMultiplier { get; set; } = 1f;
        public float AttackRange { get; set; } = 1.7f;
        public float AttackDamage { get; set; } = 12f;
        public float AttackCooldown { get; set; } = 1.6f;
        public float LoseTargetSeconds { get; set; } = 5f;
        public float SearchSeconds { get; set; } = 12f;
        public float WanderRadius { get; set; } = 14f;

        public static ZombieArchetype Wanderer() => new ZombieArchetype();
    }

    public sealed class ZombieState
    {
        public EntityId Id { get; set; }
        public string Archetype { get; set; } = "wanderer";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public float Health { get; set; }
        public ZombieMode Mode { get; set; } = ZombieMode.Idle;
        public Vec3 Home { get; set; }
        public Vec3 MoveTarget { get; set; }
        public Vec3 LastKnownTarget { get; set; }
        public float ModeTime { get; set; }
        public float TimeSinceSeen { get; set; } = 999f;
        public float AttackCooldown { get; set; }
        public float IdleTimer { get; set; }

        /// <summary>Seconds since death (corpse cleanup).</summary>
        public float DeadTime { get; set; }

        /// <summary>Player structure this zombie is currently battering (transient).</summary>
        public EntityId BreachTarget { get; set; }

        /// <summary>Seconds without meaningful progress toward its goal (transient).</summary>
        public float StallTime { get; set; }

        public float LastGoalDistance { get; set; } = -1f;

        public bool IsDead => Mode == ZombieMode.Dead;
    }

    /// <summary>What a zombie perceived this tick (computed by the perception provider).</summary>
    public struct ZombiePerception
    {
        public bool TargetVisible;
        public bool TargetAlive;
        public Vec3 TargetPosition;
        public float TargetDistance;
        public bool HeardNoise;
        public Vec3 NoisePosition;

        /// <summary>The navigation layer could not find a path to the current destination.</summary>
        public bool PathFailed;

        /// <summary>The zombie is within arrival distance of its current destination.</summary>
        public bool ReachedDestination;
    }

    /// <summary>Brain output consumed by the movement layer.</summary>
    public struct ZombieDecision
    {
        public bool Move;
        public Vec3 Destination;
        public float Speed;
        public bool Attack;
        public bool FaceTarget;
        public Vec3 FacePosition;
    }
}
