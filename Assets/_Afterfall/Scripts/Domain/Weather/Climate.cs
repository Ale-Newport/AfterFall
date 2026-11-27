using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Weather
{
    /// <summary>
    /// The weather at one moment, derived from the saved <see cref="WeatherState"/> and the clock
    /// (nothing here is stored, so it can never disagree with the save).
    /// </summary>
    public struct WeatherReadout
    {
        public WeatherKind Kind;

        /// <summary>Cloud cover 0–1, precipitation 0–1 and storm strength (wind, lightning) 0–1, blended across fronts.</summary>
        public float Cloud, Rain, Storm;

        /// <summary>How far a cold snap has set in, 0–1.</summary>
        public float ColdSnap;

        /// <summary>Open-air temperature in °C, including the cold snap and the chill of rain and wind.</summary>
        public float OutdoorC;

        public bool Raining => Rain > 0.05f;

        public static WeatherReadout Of(WorldState w)
        {
            WeatherState s = w.Weather;
            double now = w.Clock.ElapsedSeconds;
            double hour = w.Clock.SecondsPerGameHour;
            var r = new WeatherReadout { Kind = s.Kind };
            float blend = s.Initialized ? Clamp01((float)((now - s.StartedAt) / (WeatherTuning.FrontBlendHours * hour))) : 1f;
            r.Cloud = Lerp(WeatherTuning.Cloud(s.PreviousKind), WeatherTuning.Cloud(s.Kind), blend);
            r.Rain = Lerp(WeatherTuning.Rain(s.PreviousKind), WeatherTuning.Rain(s.Kind), blend);
            r.Storm = Lerp(WeatherTuning.Storm(s.PreviousKind), WeatherTuning.Storm(s.Kind), blend);
            if (s.ColdSnapActive(now))
            {
                double ramp = WeatherTuning.ColdSnapRampHours * hour;
                r.ColdSnap = Clamp01((float)Math.Min((now - s.ColdSnapStartAt) / ramp, (s.ColdSnapEndAt - now) / ramp));
            }
            float severity = Math.Max(0.4f, Math.Min(1.6f, w.Settings.WeatherSeverity));
            r.OutdoorC = WeatherTuning.BaseTemperatureC(w.Clock.Hours24) - 1.5f * r.Cloud - 2.5f * r.Rain - 2f * r.Storm
                         - WeatherTuning.ColdSnapDropC * severity * r.ColdSnap;
            return r;
        }

        /// <summary>Multiplier on how far the infected see (rain and storms hide you).</summary>
        public float SightFactor => 1f - 0.25f * Rain - 0.2f * Storm;

        /// <summary>Multiplier on how far the infected hear (rain drowns out footsteps).</summary>
        public float HearingFactor => 1f - 0.2f * Rain - 0.15f * Storm;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>What a person feels at a spot: temperature, shelter and the nearest heat.</summary>
    public struct ClimateReading
    {
        public float TemperatureC;
        public bool Sheltered;

        /// <summary>The heat source warming this spot most (EntityId.None if none).</summary>
        public EntityId HeatSource;

        public float HeatC;
    }

    /// <summary>Fuel burning in a campfire or stove.</summary>
    public sealed class HeaterState
    {
        public Inventory Fuel { get; set; }

        /// <summary>Seconds left on the fuel unit currently burning.</summary>
        public float BurnRemaining { get; set; }

        public bool Lit { get; set; }
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// Shelter and heat. A spot is sheltered under a finished roof or inside one of the valley's
    /// buildings; lit heaters and working furnaces warm everything near them.
    /// </summary>
    public static class Climate
    {
        /// <summary>World buildings the player can walk into.</summary>
        private static readonly HashSet<string> IndoorKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "house", "clinic", "shop", "garage", "warehouse", "barn", "apartment"
        };

        /// <summary>Heat (°C) and reach of a furnace while it burns fuel.</summary>
        public const float FurnaceHeatC = 12f;

        public const float FurnaceHeatRadius = 3.5f;

        public static ClimateReading At(WorldState w, ContentDatabase content, Vec3 p, in WeatherReadout weather)
        {
            var r = new ClimateReading { Sheltered = IsSheltered(w, content, p) };
            float t = weather.OutdoorC;
            // Shelter keeps off the rain and wind and holds some warmth.
            if (r.Sheltered) t += WeatherTuning.ShelterBonusC + 2.5f * weather.Rain + 2f * weather.Storm;
            float best = 0f;
            foreach (EntityId id in w.SortedStructureIds())
            {
                Structure s = w.Structures[id];
                if (!s.IsComplete) continue;
                float heat = HeatAt(s, content.Building(s.DefId), p);
                if (heat <= best) continue;
                best = heat;
                r.HeatSource = s.Id;
            }
            r.HeatC = best;
            r.TemperatureC = t + best;
            return r;
        }

        /// <summary>Warmth a structure gives at a point: full within half its radius, fading to nothing at its edge.</summary>
        public static float HeatAt(Structure s, BuildingDefinition def, Vec3 p)
        {
            float warmth, radius;
            if (s.Heater != null && s.Heater.Lit && def?.Heater != null)
            {
                warmth = def.Heater.WarmthC;
                radius = def.Heater.Radius;
            }
            else if (s.Machine != null && s.Machine.Fuel != null && s.Machine.Status == MachineStatus.Working)
            {
                warmth = FurnaceHeatC;
                radius = FurnaceHeatRadius;
            }
            else return 0f;
            float d = Vec3.DistanceXZ(s.Position, p);
            if (d >= radius || Math.Abs(p.Y - s.Position.Y) > 3f) return 0f;
            return d <= radius * 0.5f ? warmth : warmth * (radius - d) / (radius * 0.5f);
        }

        public static bool IsSheltered(WorldState w, ContentDatabase content, Vec3 p)
        {
            foreach (EntityId id in w.SortedStructureIds())
            {
                Structure s = w.Structures[id];
                if (!s.IsComplete) continue;
                BuildingDefinition def = content.Building(s.DefId);
                if (def == null || def.Snap != SnapMode.Roof) continue;
                float above = s.Position.Y - p.Y;
                if (above < 0.8f || above > 5.5f) continue;
                if (Inside(p, s.Position, s.Yaw, def.SizeX * 0.5f + 0.2f, def.SizeZ * 0.5f + 0.2f)) return true;
            }
            if (w.Layout == null) return false;
            foreach (PropPlacement prop in w.Layout.Props)
            {
                if (!IndoorKinds.Contains(prop.Kind)) continue;
                if (p.Y < prop.Position.Y - 1f || p.Y > prop.Position.Y + prop.Size.Y) continue;
                if (Inside(p, prop.Position, prop.Yaw, prop.Size.X * 0.5f - 0.25f, prop.Size.Z * 0.5f - 0.25f)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a structure stands in the open (nothing roofed over it). Exposed conductors draw
        /// lightning; exposed fires burn faster in the rain; exposed planter beds catch the rain.
        /// </summary>
        public static bool IsExposed(WorldState w, ContentDatabase content, Structure s)
        {
            return !IsSheltered(w, content, s.Position + new Vec3(0f, 0.1f, 0f));
        }

        /// <summary>Whether a point lies in a rectangle of half-extents (hx, hz) centred on <paramref name="center"/> and turned by <paramref name="yaw"/>.</summary>
        public static bool Inside(Vec3 p, Vec3 center, float yaw, float hx, float hz)
        {
            double r = -yaw * Math.PI / 180.0;
            float c = (float)Math.Cos(r), sn = (float)Math.Sin(r);
            float dx = p.X - center.X, dz = p.Z - center.Z;
            float lx = dx * c + dz * sn;
            float lz = -dx * sn + dz * c;
            return Math.Abs(lx) <= hx && Math.Abs(lz) <= hz;
        }
    }
}
