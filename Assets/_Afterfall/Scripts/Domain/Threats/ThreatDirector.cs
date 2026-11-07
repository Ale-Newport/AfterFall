using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Threats
{
    /// <summary>
    /// Controls infected population and announced threats. It considers time of day,
    /// difficulty and the "heat" produced by noise, light and industry. Fairness rules:
    /// spawns happen out of sight range, never inside or next to the player's base, and
    /// larger groups are always announced before they arrive.
    /// </summary>
    public sealed class ThreatDirector : ISimulationSystem
    {
        public const float MinSpawnDistance = 50f;
        public const float MaxSpawnDistance = 150f;
        public const float BaseExclusionRadius = 30f;
        public const float DespawnDistance = 175f;
        public const float RaidHeatThreshold = 35f;
        public const double RaidWarningLead = 30.0;
        public const double RaidCooldown = 900.0;
        public const double SpawnCooldown = 4.0;

        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly ZombieSystem _zombies;
        private readonly DeterministicRandom _rng;

        public ThreatDirector(WorldState world, ContentDatabase content, EventBus bus, ZombieSystem zombies)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _zombies = zombies;
            _rng = new DeterministicRandom((ulong)(uint)world.Seed * 31UL + 7UL);
        }

        public string Name => "Threat director";

        /// <summary>Set false to disable spawning (tests, tutorials).</summary>
        public bool SpawningEnabled { get; set; } = true;

        public int TargetPopulation { get; private set; }

        public void Tick(double dt)
        {
            ThreatState t = _world.Threat;
            double now = _world.Clock.ElapsedSeconds;
            UpdateHeat(t, dt);
            IssueDuskWarning(t);
            if (_world.Player == null || _world.Layout == null || !SpawningEnabled) return;

            Despawn();
            bool night = _world.Clock.IsNight;
            TargetPopulation = (int)Math.Round((night ? 14 : 7) * _world.Settings.EnemyDensity + t.Heat / 12f);
            int alive = _zombies.AliveCount;
            if (alive < TargetPopulation && now - t.LastSpawnAt >= SpawnCooldown)
            {
                if (TryPickSpawn(MinSpawnDistance, out Vec3 point))
                {
                    _zombies.Spawn(Jitter(point, 3f), "wanderer", _rng);
                    t.SpawnedTotal++;
                    t.LastSpawnAt = now;
                }
            }

            HandleRaids(t, now, night);
        }

        private void UpdateHeat(ThreatState t, double dt)
        {
            float gain = 0f;
            bool dark = _world.Clock.Daylight01 < 0.55f;
            foreach (Structure s in _world.Structures.Values)
            {
                if (!s.PlayerBuilt || !s.IsComplete) continue;
                if (s.Generator != null && s.Generator.Running) gain += 0.08f;
                if (s.Machine != null && s.Machine.Status == MachineStatus.Working) gain += 0.04f;
                if (dark && s.Power != null && s.Power.Powered && _content.Building(s.DefId)?.Light != null) gain += 0.02f;
            }
            t.Heat = (float)(t.Heat * Math.Exp(-dt / 260.0) + gain * dt);
            t.Heat = Math.Max(0f, Math.Min(100f, t.Heat));
        }

        private void IssueDuskWarning(ThreatState t)
        {
            double h = _world.Clock.Hours24;
            int day = _world.Clock.Day;
            if (h >= 19.0 && h < 20.0 && t.LastNightWarnedDay != day)
            {
                t.LastNightWarnedDay = day;
                const string msg = "Dusk is falling. The infected grow restless at night — find shelter or light.";
                _bus?.Publish(new ThreatWarningIssued(msg));
                _bus?.Publish(new Notification(msg, Severity.Warning));
            }
        }

        private void HandleRaids(ThreatState t, double now, bool night)
        {
            if (t.PendingRaidAt >= 0)
            {
                if (now < t.PendingRaidAt) return;
                Vec3 target = BaseCenter(out bool hasBase) ;
                int count = 3 + (int)Math.Min(3, t.Heat / 25f);
                for (int i = 0; i < count; i++)
                {
                    ZombieState z = _zombies.Spawn(Jitter(t.PendingRaidOrigin, 4f), "wanderer", _rng);
                    if (!hasBase) continue;
                    z.Mode = ZombieMode.Investigate;
                    z.ModeTime = 0f;
                    z.MoveTarget = target;
                }
                t.PendingRaidAt = -1;
                t.LastRaidAt = now;
                return;
            }

            if (!night || t.Heat < RaidHeatThreshold || now - t.LastRaidAt < RaidCooldown) return;
            Vec3 baseCenter = BaseCenter(out bool any);
            if (!any) return;
            if (!TryPickSpawnAwayFrom(baseCenter, 70f, out Vec3 origin)) return;
            t.PendingRaidAt = now + RaidWarningLead;
            t.PendingRaidOrigin = origin;
            string dir = Compass(origin - baseCenter);
            string msg = $"The noise from your base is drawing the infected. A group approaches from the {dir}.";
            _bus?.Publish(new ThreatWarningIssued(msg));
            _bus?.Publish(new Notification(msg, Severity.Danger));
        }

        private void Despawn()
        {
            var remove = new List<EntityId>();
            foreach (ZombieState z in _world.Zombies.Values)
            {
                if (z.IsDead) continue;
                if (z.Mode != ZombieMode.Idle && z.Mode != ZombieMode.Wander) continue;
                if (Vec3.Distance(z.Position, _world.Player.Position) > DespawnDistance) remove.Add(z.Id);
            }
            foreach (EntityId id in remove) _world.Zombies.Remove(id);
            if (remove.Count > 0) _world.MarkChanged();
        }

        /// <summary>True if a position is a fair spawn: away from the player and outside any base.</summary>
        public bool IsValidSpawn(Vec3 p, float minPlayerDistance)
        {
            float dp = Vec3.Distance(p, _world.Player.Position);
            if (dp < minPlayerDistance || dp > MaxSpawnDistance) return false;
            if (_world.Layout.IsWater(p.X, p.Z)) return false;
            foreach (Structure s in _world.Structures.Values)
                if (s.PlayerBuilt && s.IsComplete && Vec3.DistanceXZ(s.Position, p) < BaseExclusionRadius) return false;
            return true;
        }

        private bool TryPickSpawn(float minPlayerDistance, out Vec3 point)
        {
            List<Vec3> pts = _world.Layout.ZombieSpawnPoints;
            point = default;
            if (pts.Count == 0) return false;
            int start = _rng.Range(0, pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Vec3 p = pts[(start + i) % pts.Count];
                if (!IsValidSpawn(p, minPlayerDistance)) continue;
                point = p;
                return true;
            }
            return false;
        }

        private bool TryPickSpawnAwayFrom(Vec3 center, float minDistance, out Vec3 point)
        {
            point = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (Vec3 p in _world.Layout.ZombieSpawnPoints)
            {
                float d = Vec3.DistanceXZ(p, center);
                if (d < minDistance || d >= best || !IsValidSpawn(p, MinSpawnDistance)) continue;
                best = d;
                point = p;
                found = true;
            }
            return found;
        }

        private Vec3 BaseCenter(out bool any)
        {
            float x = 0, y = 0, z = 0;
            int n = 0;
            foreach (Structure s in _world.Structures.Values)
            {
                if (!s.PlayerBuilt || !s.IsComplete) continue;
                x += s.Position.X;
                y += s.Position.Y;
                z += s.Position.Z;
                n++;
            }
            any = n > 0;
            return n > 0 ? new Vec3(x / n, y / n, z / n) : Vec3.Zero;
        }

        private Vec3 Jitter(Vec3 p, float r)
        {
            float x = p.X + _rng.Range(-r, r);
            float z = p.Z + _rng.Range(-r, r);
            return _world.Layout != null ? _world.Layout.Ground(x, z) : new Vec3(x, p.Y, z);
        }

        public static string Compass(Vec3 dir)
        {
            double angle = Math.Atan2(dir.X, dir.Z) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[(int)Math.Round(angle / 45.0) % 8];
        }
    }
}
