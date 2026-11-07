using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.AI
{
    /// <summary>A sound in the world that the infected can hear (footsteps, combat, machines).</summary>
    public struct NoiseEvent
    {
        public Vec3 Position;
        public float Radius;
        public double Time;
        public string Source;
    }

    /// <summary>Short-lived buffer of recent noises. Not persisted.</summary>
    public sealed class NoiseBoard
    {
        public const double Lifetime = 1.5;
        private readonly List<NoiseEvent> _events = new List<NoiseEvent>();

        public IReadOnlyList<NoiseEvent> Events => _events;

        public void Emit(Vec3 position, float radius, double now, string source)
        {
            if (radius <= 0f) return;
            _events.Add(new NoiseEvent { Position = position, Radius = radius, Time = now, Source = source });
        }

        public void Expire(double now) => _events.RemoveAll(e => now - e.Time > Lifetime);

        /// <summary>Returns the closest audible noise for a listener, if any.</summary>
        public bool TryHear(Vec3 listener, float hearingMultiplier, out NoiseEvent heard)
        {
            heard = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (NoiseEvent e in _events)
            {
                float d = Vec3.DistanceXZ(listener, e.Position);
                if (d > e.Radius * hearingMultiplier || d >= best) continue;
                best = d;
                heard = e;
                found = true;
            }
            return found;
        }
    }

    public static class PerceptionRules
    {
        public static bool InViewCone(Vec3 from, float yawDegrees, Vec3 to, float fovDegrees)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            if (dx * dx + dz * dz < 1e-4f) return true;
            double angleTo = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            double diff = Math.Abs(NormalizeAngle(angleTo - yawDegrees));
            return diff <= fovDegrees * 0.5;
        }

        /// <summary>Sight range after darkness and stealth modifiers.</summary>
        public static float EffectiveSightRange(ZombieArchetype a, float daylight01, bool targetCrouching, bool targetSprinting)
        {
            float light = 0.45f + 0.55f * Math.Max(0f, Math.Min(1f, daylight01));
            float stance = targetCrouching ? 0.6f : targetSprinting ? 1.15f : 1f;
            return a.SightRange * light * stance;
        }

        /// <summary>
        /// Vision check. Line of sight must be supplied by the caller (physics raycast in the
        /// game, assumed clear in pure simulations). Very close targets are sensed regardless
        /// of facing — but never through walls.
        /// </summary>
        public static bool CanSee(ZombieState z, ZombieArchetype a, Vec3 target, float daylight01, bool crouching, bool sprinting, bool lineOfSightClear)
        {
            if (!lineOfSightClear) return false;
            float d = Vec3.Distance(z.Position, target);
            if (d <= a.ProximitySense) return true;
            if (d > EffectiveSightRange(a, daylight01, crouching, sprinting)) return false;
            return InViewCone(z.Position, z.Yaw, target, a.SightFovDegrees);
        }

        public static double NormalizeAngle(double deg)
        {
            deg %= 360.0;
            if (deg > 180) deg -= 360;
            if (deg < -180) deg += 360;
            return deg;
        }
    }

    /// <summary>
    /// Pure state machine for the infected:
    /// Idle/Wander → Investigate stimulus → Chase → Attack → lose target → Search → resume.
    /// No omniscience: it only reacts to what <see cref="ZombiePerception"/> reports.
    /// </summary>
    public static class ZombieBrain
    {
        public static ZombieDecision Think(ZombieState z, ZombieArchetype a, ZombiePerception p, DeterministicRandom rng, float dt)
        {
            var d = new ZombieDecision();
            if (z.IsDead) return d;

            z.ModeTime += dt;
            z.AttackCooldown = Math.Max(0f, z.AttackCooldown - dt);

            bool seesTarget = p.TargetVisible && p.TargetAlive;
            if (seesTarget)
            {
                z.LastKnownTarget = p.TargetPosition;
                z.TimeSinceSeen = 0f;
                if (p.TargetDistance <= a.AttackRange) Enter(z, ZombieMode.Attack);
                else if (z.Mode != ZombieMode.Chase && z.Mode != ZombieMode.Attack) Enter(z, ZombieMode.Chase);
            }
            else
            {
                z.TimeSinceSeen += dt;
            }

            switch (z.Mode)
            {
                case ZombieMode.Idle:
                    if (TryInvestigate(z, p)) goto case ZombieMode.Investigate;
                    z.IdleTimer -= dt;
                    if (z.IdleTimer <= 0f)
                    {
                        Enter(z, ZombieMode.Wander);
                        z.MoveTarget = RandomPointNear(z.Home, a.WanderRadius, rng);
                        goto case ZombieMode.Wander;
                    }
                    break;

                case ZombieMode.Wander:
                    if (TryInvestigate(z, p)) goto case ZombieMode.Investigate;
                    if (p.ReachedDestination || p.PathFailed || z.ModeTime > 25f)
                    {
                        Enter(z, ZombieMode.Idle);
                        z.IdleTimer = rng.Range(2f, 7f);
                        break;
                    }
                    Move(ref d, z.MoveTarget, a.WalkSpeed);
                    break;

                case ZombieMode.Investigate:
                    if (p.HeardNoise) z.MoveTarget = p.NoisePosition;
                    if (p.ReachedDestination || p.PathFailed || z.ModeTime > 20f)
                    {
                        Enter(z, ZombieMode.Search);
                        z.LastKnownTarget = z.MoveTarget;
                        break;
                    }
                    Move(ref d, z.MoveTarget, a.InvestigateSpeed);
                    break;

                case ZombieMode.Chase:
                    if (!p.TargetAlive)
                    {
                        Enter(z, ZombieMode.Search);
                        break;
                    }
                    if (!seesTarget && (z.TimeSinceSeen > a.LoseTargetSeconds || p.PathFailed))
                    {
                        Enter(z, ZombieMode.Search);
                        break;
                    }
                    Move(ref d, z.LastKnownTarget, a.ChaseSpeed);
                    break;

                case ZombieMode.Attack:
                    if (!p.TargetAlive || !seesTarget)
                    {
                        Enter(z, p.TargetAlive ? ZombieMode.Chase : ZombieMode.Search);
                        if (z.Mode == ZombieMode.Chase) Move(ref d, z.LastKnownTarget, a.ChaseSpeed);
                        break;
                    }
                    if (p.TargetDistance > a.AttackRange * 1.2f)
                    {
                        Enter(z, ZombieMode.Chase);
                        Move(ref d, z.LastKnownTarget, a.ChaseSpeed);
                        break;
                    }
                    d.FaceTarget = true;
                    d.FacePosition = p.TargetPosition;
                    if (z.AttackCooldown <= 0f)
                    {
                        d.Attack = true;
                        z.AttackCooldown = a.AttackCooldown;
                    }
                    break;

                case ZombieMode.Search:
                    if (TryInvestigate(z, p)) goto case ZombieMode.Investigate;
                    if (z.ModeTime > a.SearchSeconds)
                    {
                        Enter(z, ZombieMode.Idle);
                        z.IdleTimer = rng.Range(1f, 4f);
                        break;
                    }
                    if (p.ReachedDestination || p.PathFailed || Vec3.DistanceXZ(z.Position, z.MoveTarget) < 1f || z.MoveTarget.Equals(Vec3.Zero))
                        z.MoveTarget = RandomPointNear(z.LastKnownTarget, 6f, rng);
                    Move(ref d, z.MoveTarget, a.InvestigateSpeed);
                    break;
            }
            return d;
        }

        private static bool TryInvestigate(ZombieState z, ZombiePerception p)
        {
            if (!p.HeardNoise) return false;
            Enter(z, ZombieMode.Investigate);
            z.MoveTarget = p.NoisePosition;
            return true;
        }

        private static void Enter(ZombieState z, ZombieMode mode)
        {
            if (z.Mode == mode) return;
            z.Mode = mode;
            z.ModeTime = 0f;
        }

        private static void Move(ref ZombieDecision d, Vec3 target, float speed)
        {
            d.Move = true;
            d.Destination = target;
            d.Speed = speed;
        }

        private static Vec3 RandomPointNear(Vec3 center, float radius, DeterministicRandom rng)
        {
            float ang = rng.Range(0f, (float)(Math.PI * 2));
            float r = rng.Range(radius * 0.3f, radius);
            return new Vec3(center.X + (float)Math.Cos(ang) * r, center.Y, center.Z + (float)Math.Sin(ang) * r);
        }
    }
}
