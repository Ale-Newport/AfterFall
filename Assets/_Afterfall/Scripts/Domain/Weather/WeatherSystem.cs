using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Weather
{
    /// <summary>The front over the valley changed.</summary>
    public sealed class WeatherChanged : IGameEvent
    {
        public WeatherChanged(WeatherKind from, WeatherKind to)
        {
            From = from;
            To = to;
        }

        public WeatherKind From { get; }
        public WeatherKind To { get; }
        public override string ToString() => $"WeatherChanged {From}->{To}";
    }

    /// <summary>A storm or cold snap was foreseen (signs in the sky, a turn in the wind).</summary>
    public sealed class WeatherWarningIssued : IGameEvent
    {
        public WeatherWarningIssued(string message)
        {
            Message = message;
        }

        public string Message { get; }
        public override string ToString() => $"WeatherWarning {Message}";
    }

    /// <summary>Lightning hit the ground, a structure, or a lightning rod that took it harmlessly.</summary>
    public sealed class LightningStruck : IGameEvent
    {
        public LightningStruck(Vec3 position, EntityId structureId, bool absorbedByRod, float damage)
        {
            Position = position;
            StructureId = structureId;
            AbsorbedByRod = absorbedByRod;
            Damage = damage;
        }

        public Vec3 Position { get; }

        /// <summary>The structure hit (the rod when one absorbed it); EntityId.None for open ground.</summary>
        public EntityId StructureId { get; }

        public bool AbsorbedByRod { get; }
        public float Damage { get; }
        public override string ToString() => $"LightningStruck {Position} {StructureId} rod={AbsorbedByRod} dmg={Damage:0}";
    }

    /// <summary>
    /// Weather at 1 Hz: rolls fronts and cold snaps ahead of time, foretells storms and cold snaps
    /// before they arrive, throws lightning at exposed conductors during storms (lightning rods
    /// take it instead) and lets the rain water planter beds in the open.
    /// </summary>
    public sealed class WeatherSystem : ISimulationSystem
    {
        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly DeterministicRandom _rng;

        public WeatherSystem(WorldState world, ContentDatabase content, EventBus bus)
        {
            _world = world;
            _content = content;
            _bus = bus;
            _rng = world.WeatherRng;
        }

        public string Name => "Weather";

        /// <summary>Thunder at a strike (set by the session to the infected's noise field).</summary>
        public Action<Vec3, float, string> EmitNoise { get; set; }

        /// <summary>Set false to freeze the weather (tests that need fixed conditions).</summary>
        public bool Enabled { get; set; } = true;

        public WeatherReadout Current => WeatherReadout.Of(_world);

        private double Hour => _world.Clock.SecondsPerGameHour;
        private double Now => _world.Clock.ElapsedSeconds;

        public void Tick(double dt)
        {
            WeatherState s = _world.Weather;
            if (!s.Initialized) Initialize(s);
            if (!Enabled) return;
            double now = Now;
            while (now >= s.EndsAt) BeginFront(s, s.NextKind, s.EndsAt);
            ForetellStorm(s, now);
            ColdSnaps(s, now);
            WeatherReadout r = Current;
            if (r.Storm > 0.5f) Lightning(s, now, r);
            else s.NextStrikeAt = -1;
            if (r.Rain > 0.05f) WaterCrops(r, dt);
        }

        // ------------------------------------------------------------------ fronts

        /// <summary>A new game (or a save from before weather) starts clear, with a cold snap two evenings later.</summary>
        private void Initialize(WeatherState s)
        {
            double now = Now;
            s.Kind = WeatherKind.Clear;
            s.PreviousKind = WeatherKind.Clear;
            s.StartedAt = now - WeatherTuning.FrontBlendHours * Hour;
            s.EndsAt = now + WeatherTuning.RollDurationHours(WeatherKind.Clear, _rng) * Hour;
            s.NextKind = WeatherKind.Overcast;
            if (s.ColdSnapStartAt < 0) ScheduleColdSnap(s, now, 1);
        }

        private void BeginFront(WeatherState s, WeatherKind kind, double at)
        {
            WeatherKind old = s.Kind;
            s.PreviousKind = old;
            s.Kind = kind;
            s.StartedAt = at;
            s.EndsAt = at + Math.Max(0.1, WeatherTuning.RollDurationHours(kind, _rng)) * Hour;
            bool calm = s.EndsAt < WeatherTuning.CalmStartHours * Hour;
            s.NextKind = WeatherTuning.RollNext(kind, _world.Settings.WeatherSeverity, calm, _rng);
            if (old == kind) return;
            _bus?.Publish(new WeatherChanged(old, kind));
            string msg = Announce(old, kind);
            if (msg != null) _bus?.Publish(new Notification(msg, kind == WeatherKind.Storm ? Severity.Warning : Severity.Info));
        }

        private static string Announce(WeatherKind from, WeatherKind to)
        {
            switch (to)
            {
                case WeatherKind.Storm: return "The storm breaks. Stay clear of poles and lamps in the open.";
                case WeatherKind.Rain: return from == WeatherKind.Storm ? "The storm eases into steady rain." : "It starts to rain. Planter beds in the open are watered.";
                case WeatherKind.Clear: return from == WeatherKind.Overcast ? null : "The rain stops and the sky clears.";
                default: return from == WeatherKind.Clear ? null : "The rain stops.";
            }
        }

        /// <summary>Storms are seen coming: a warning about one and a half game hours before one breaks.</summary>
        private void ForetellStorm(WeatherState s, double now)
        {
            if (s.NextKind != WeatherKind.Storm || s.Kind == WeatherKind.Storm || s.StormWarnedFor == s.EndsAt) return;
            if (s.EndsAt - now > WeatherTuning.StormWarningHours * Hour) return;
            s.StormWarnedFor = s.EndsAt;
            string dir = Threats.ThreatDirector.Compass(new Vec3(_rng.Range(-1f, 1f), 0f, _rng.Range(-1f, 1f)));
            Warn($"Thunder rumbles to the {dir}. A storm will break within two hours: lightning seeks out poles, lamps and generators in the open.");
        }

        public WeatherForecast Forecast()
        {
            WeatherState s = _world.Weather;
            double now = Now;
            var f = new WeatherForecast { Now = s.Kind, ColdSnapActive = s.ColdSnapActive(now), StormInHours = -1, ColdSnapInHours = -1 };
            if (s.StormWarnedFor == s.EndsAt && s.NextKind == WeatherKind.Storm) f.StormInHours = Math.Max(0, (s.EndsAt - now) / Hour);
            if (!f.ColdSnapActive && s.ColdSnapWarnedFor == s.ColdSnapStartAt && s.ColdSnapStartAt > now) f.ColdSnapInHours = (s.ColdSnapStartAt - now) / Hour;
            return f;
        }

        // ------------------------------------------------------------------ cold snaps

        /// <summary>Plans the next cold snap for an evening <paramref name="daysAhead"/> days after the current one.</summary>
        private void ScheduleColdSnap(WeatherState s, double now, int daysAhead)
        {
            SimulationClock clock = _world.Clock;
            double dayStart = now - clock.TimeOfDay01 * clock.DayLengthSeconds;
            double startHour = 16.0 + 4.0 * _rng.NextFloat();
            s.ColdSnapStartAt = dayStart + daysAhead * clock.DayLengthSeconds + startHour * Hour;
            s.ColdSnapEndAt = s.ColdSnapStartAt + (10.0 + 6.0 * _rng.NextFloat()) * Hour;
            s.ColdSnapAnnounced = false;
        }

        private void ColdSnaps(WeatherState s, double now)
        {
            if (s.ColdSnapStartAt < 0)
            {
                ScheduleColdSnap(s, now, 2 + _rng.Range(0, 2));
                return;
            }
            if (now >= s.ColdSnapEndAt)
            {
                _bus?.Publish(new Notification("The cold snap is over. The air softens.", Severity.Info));
                ScheduleColdSnap(s, now, 2 + _rng.Range(0, 2));
                return;
            }
            if (now >= s.ColdSnapStartAt)
            {
                if (s.ColdSnapAnnounced) return;
                s.ColdSnapAnnounced = true;
                const string arrived = "The cold snap has arrived. Stay near a fire or under a roof, and eat well.";
                _bus?.Publish(new WeatherWarningIssued(arrived));
                _bus?.Publish(new Notification(arrived, Severity.Warning));
                return;
            }
            if (s.ColdSnapWarnedFor == s.ColdSnapStartAt || s.ColdSnapStartAt - now > WeatherTuning.ColdSnapWarningHours * Hour) return;
            s.ColdSnapWarnedFor = s.ColdSnapStartAt;
            Warn("The wind turns bitter. A cold snap will set in this evening: gather fuel, build a fire and get under a roof.");
        }

        /// <summary>Starts a cold snap now (tour and tests). It sets in over the normal ramp and lasts <paramref name="hours"/> game hours.</summary>
        public void ForceColdSnap(double hours)
        {
            WeatherState s = _world.Weather;
            if (!s.Initialized) Initialize(s);
            s.ColdSnapStartAt = Now;
            s.ColdSnapEndAt = Now + hours * Hour;
            s.ColdSnapWarnedFor = s.ColdSnapStartAt;
            s.ColdSnapAnnounced = false;
        }

        /// <summary>Replaces the current front (tour and tests); <paramref name="instant"/> skips the blend.</summary>
        public void ForceFront(WeatherKind kind, double hours, bool instant)
        {
            WeatherState s = _world.Weather;
            if (!s.Initialized) Initialize(s);
            BeginFront(s, kind, instant ? Now - WeatherTuning.FrontBlendHours * Hour : Now);
            s.EndsAt = Now + hours * Hour;
            if (instant) s.PreviousKind = kind;
        }

        /// <summary>
        /// Freezes the sky on one front with no cold snap planned (the scripted tour's other scenes).
        /// Setting <see cref="Enabled"/> again resumes the normal cycle.
        /// </summary>
        public void Hold(WeatherKind kind)
        {
            ForceFront(kind, 24, true);
            WeatherState s = _world.Weather;
            s.ColdSnapStartAt = -1;
            s.ColdSnapEndAt = -1;
            s.ColdSnapAnnounced = false;
            s.NextStrikeAt = -1;
            Enabled = false;
        }

        private void Warn(string message)
        {
            _bus?.Publish(new WeatherWarningIssued(message));
            _bus?.Publish(new Notification(message, Severity.Warning));
        }

        // ------------------------------------------------------------------ lightning

        private void Lightning(WeatherState s, double now, in WeatherReadout r)
        {
            if (s.NextStrikeAt < 0)
            {
                s.NextStrikeAt = now + _rng.Range(WeatherTuning.StrikeIntervalMin, WeatherTuning.StrikeIntervalMax);
                return;
            }
            if (now < s.NextStrikeAt) return;
            s.NextStrikeAt = now + _rng.Range(WeatherTuning.StrikeIntervalMin, WeatherTuning.StrikeIntervalMax) / Math.Max(0.5f, r.Storm);
            s.Strikes++;
            Strike();
        }

        /// <summary>
        /// One strike. It finds the settlement's exposed conductors with a chance that grows with how
        /// many there are, preferring tall ones; otherwise it hits open ground somewhere in the valley.
        /// </summary>
        public LightningStruck Strike()
        {
            var candidates = new List<Structure>();
            float totalHeight = 0f;
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure st = _world.Structures[id];
                if (!st.PlayerBuilt || !st.IsComplete) continue;
                BuildingDefinition def = _content.Building(st.DefId);
                if (def == null || !def.AttractsLightning || !Climate.IsExposed(_world, _content, st)) continue;
                candidates.Add(st);
                totalHeight += def.SizeY;
            }
            float hitChance = Math.Min(WeatherTuning.StrikeChanceMax, WeatherTuning.StrikeChanceBase + WeatherTuning.StrikeChancePerConductor * candidates.Count);
            LightningStruck e;
            if (candidates.Count > 0 && _rng.Chance(hitChance)) e = StrikeSettlement(candidates, totalHeight);
            else e = new LightningStruck(OpenGroundPoint(), EntityId.None, false, 0f);
            EmitNoise?.Invoke(e.Position, WeatherTuning.ThunderNoiseRadius, "thunder");
            _bus?.Publish(e);
            return e;
        }

        private LightningStruck StrikeSettlement(List<Structure> candidates, float totalHeight)
        {
            float roll = _rng.NextFloat() * totalHeight;
            Structure target = candidates[candidates.Count - 1];
            foreach (Structure c in candidates)
            {
                roll -= _content.Building(c.DefId).SizeY;
                if (roll > 0f) continue;
                target = c;
                break;
            }
            Structure rod = target;
            if (_content.Building(target.DefId).LightningRod == null) rod = NearestRod(target.Position);
            if (rod != null)
            {
                _bus?.Publish(new Notification("Lightning struck your lightning rod. The charge ran to ground harmlessly.", Severity.Info));
                return new LightningStruck(rod.Position, rod.Id, true, 0f);
            }
            BuildingDefinition def = _content.Building(target.DefId);
            float before = target.Health;
            target.Health = Math.Max(1f, target.Health - def.MaxHealth * WeatherTuning.StrikeDamageFraction);
            bool broken = def.IsBroken(target.Health);
            if (broken && target.Power != null) _world.MarkPowerTopologyChanged();
            _bus?.Publish(new Notification(
                broken ? $"Lightning struck your {def.Name}! It is broken until you repair it with a hammer." : $"Lightning struck your {def.Name} and damaged it.",
                Severity.Danger));
            return new LightningStruck(target.Position, target.Id, false, before - target.Health);
        }

        private Structure NearestRod(Vec3 p)
        {
            Structure best = null;
            float bestDist = float.MaxValue;
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure st = _world.Structures[id];
                if (!st.IsComplete) continue;
                LightningRodSpec spec = _content.Building(st.DefId)?.LightningRod;
                if (spec == null) continue;
                float d = Vec3.DistanceXZ(st.Position, p);
                if (d > spec.Radius || d >= bestDist) continue;
                best = st;
                bestDist = d;
            }
            return best;
        }

        private Vec3 OpenGroundPoint()
        {
            float size = _world.Layout?.Size ?? WorldLayout.WorldSize;
            Vec3 center = _world.Player?.Position ?? new Vec3(size * 0.5f, 0f, size * 0.5f);
            float a = _rng.Range(0f, (float)(Math.PI * 2.0));
            float d = _rng.Range(40f, 160f);
            float x = Math.Max(2f, Math.Min(size - 2f, center.X + (float)Math.Cos(a) * d));
            float z = Math.Max(2f, Math.Min(size - 2f, center.Z + (float)Math.Sin(a) * d));
            return _world.Layout != null ? _world.Layout.Ground(x, z) : new Vec3(x, center.Y, z);
        }

        // ------------------------------------------------------------------ rain

        /// <summary>Soil water (0–100) that full rain adds per second to a planter bed in the open.</summary>
        public const float RainWaterPerSecond = 1.2f;

        private void WaterCrops(in WeatherReadout r, double dt)
        {
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure st = _world.Structures[id];
                if (st.Crop == null || !st.IsComplete || !Climate.IsExposed(_world, _content, st)) continue;
                st.Crop.Water = Math.Min(100f, st.Crop.Water + RainWaterPerSecond * r.Rain * (float)dt);
            }
        }
    }

    /// <summary>What the survivor can tell about the weather: the sky now and the warnings already given.</summary>
    public struct WeatherForecast
    {
        public WeatherKind Now;
        public bool ColdSnapActive;

        /// <summary>Game hours until a foretold storm breaks; negative when none has been foretold.</summary>
        public double StormInHours;

        /// <summary>Game hours until a foretold cold snap sets in; negative when none has been foretold.</summary>
        public double ColdSnapInHours;
    }

    /// <summary>
    /// Burns fuel in heaters (campfires) at 2 Hz. A fire in the open burns faster in the rain; a
    /// heater that is switched off keeps its partly burnt log for later.
    /// </summary>
    public sealed class HeaterSystem : ISimulationSystem
    {
        private readonly WorldState _world;
        private readonly ContentDatabase _content;

        public HeaterSystem(WorldState world, ContentDatabase content)
        {
            _world = world;
            _content = content;
        }

        public string Name => "Heaters";

        public void Tick(double dt)
        {
            WeatherReadout weather = WeatherReadout.Of(_world);
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (s.Heater == null || !s.IsComplete) continue;
                HeaterSpec spec = _content.Building(s.DefId)?.Heater;
                if (spec == null) continue;
                HeaterState h = s.Heater;
                if (!s.Enabled)
                {
                    h.Lit = false;
                    h.Status = FuelSecondsLeft(h, spec) > 0f ? "Put out — light it to burn" : "Put out — add logs or charcoal, then light it";
                    continue;
                }
                float rate = 1f;
                if (weather.Rain > 0.05f && Climate.IsExposed(_world, _content, s)) rate += weather.Rain;
                Burn(h, spec, (float)dt * rate);
                h.Lit = h.BurnRemaining > 0f;
                float left = FuelSecondsLeft(h, spec);
                h.Status = h.Lit
                    ? $"Burning — about {left / _world.Clock.SecondsPerGameHour:0.#} h of fuel{(rate > 1.05f ? " (the rain makes it burn faster)" : "")}"
                    : "Out of fuel — add logs or charcoal";
            }
        }

        private void Burn(HeaterState h, HeaterSpec spec, float seconds)
        {
            while (seconds > 0f)
            {
                if (h.BurnRemaining >= seconds)
                {
                    h.BurnRemaining -= seconds;
                    return;
                }
                seconds -= h.BurnRemaining;
                h.BurnRemaining = 0f;
                if (!TryLoad(h, spec)) return;
            }
        }

        /// <summary>Takes one fuel item from the heater's fuel slots and starts burning it.</summary>
        private bool TryLoad(HeaterState h, HeaterSpec spec)
        {
            for (int i = 0; i < h.Fuel.SlotCount; i++)
            {
                ItemStack st = h.Fuel.GetSlot(i);
                if (st.IsEmpty) continue;
                float burn = _content.Items.Get(st.ItemId).BurnSeconds;
                if (burn <= 0f || h.Fuel.TakeFromSlot(i, 1).IsEmpty) continue;
                h.BurnRemaining += burn * spec.BurnTimeMultiplier;
                return true;
            }
            return false;
        }

        /// <summary>Seconds of fire left from the burning item plus the fuel slots.</summary>
        public float FuelSecondsLeft(HeaterState h, HeaterSpec spec)
        {
            float total = h.BurnRemaining;
            for (int i = 0; i < h.Fuel.SlotCount; i++)
            {
                ItemStack st = h.Fuel.GetSlot(i);
                if (!st.IsEmpty) total += st.Quantity * _content.Items.Get(st.ItemId).BurnSeconds * spec.BurnTimeMultiplier;
            }
            return total;
        }
    }
}
