using System;
using Afterfall.Core;

namespace Afterfall.Domain.Weather
{
    /// <summary>The kind of weather front over the valley.</summary>
    public enum WeatherKind
    {
        Clear,
        Overcast,
        Rain,
        Storm
    }

    /// <summary>
    /// Saved weather: the current front (and the one before it, so changes blend), the scheduled
    /// cold snap, and the warnings already issued. Fronts are rolled from <c>WorldState.WeatherRng</c>,
    /// so the same seed and save always produce the same weather.
    /// </summary>
    public sealed class WeatherState
    {
        public WeatherKind Kind { get; set; } = WeatherKind.Clear;
        public WeatherKind PreviousKind { get; set; } = WeatherKind.Clear;

        /// <summary>Simulation time at which the current front started.</summary>
        public double StartedAt { get; set; }

        /// <summary>Simulation time at which the current front ends (0 = not rolled yet).</summary>
        public double EndsAt { get; set; }

        /// <summary>The front that follows the current one (rolled in advance so it can be foreseen).</summary>
        public WeatherKind NextKind { get; set; } = WeatherKind.Overcast;

        /// <summary>Start and end of the next (or current) cold snap; negative = none scheduled.</summary>
        public double ColdSnapStartAt { get; set; } = -1;

        public double ColdSnapEndAt { get; set; } = -1;

        /// <summary>Start time of the storm front that has already been announced (avoids repeats).</summary>
        public double StormWarnedFor { get; set; } = -1;

        public double ColdSnapWarnedFor { get; set; } = -1;
        public bool ColdSnapAnnounced { get; set; }

        /// <summary>Next lightning strike while a storm rages.</summary>
        public double NextStrikeAt { get; set; } = -1;

        public int Strikes { get; set; }

        public bool Initialized => EndsAt > 0;

        public bool ColdSnapActive(double now) => ColdSnapStartAt >= 0 && now >= ColdSnapStartAt && now < ColdSnapEndAt;
    }

    /// <summary>Balance values for weather and temperature (game hours scale with the day length).</summary>
    public static class WeatherTuning
    {
        /// <summary>Game hours over which one front blends into the next.</summary>
        public const double FrontBlendHours = 0.5;

        /// <summary>Game hours over which a cold snap sets in and lifts.</summary>
        public const double ColdSnapRampHours = 1.5;

        public const double StormWarningHours = 1.5;
        public const double ColdSnapWarningHours = 3.0;

        /// <summary>No storm before this many game hours into a new game.</summary>
        public const double CalmStartHours = 10.0;

        /// <summary>Temperature drop of a full cold snap at weather severity 1.</summary>
        public const float ColdSnapDropC = 18f;

        /// <summary>Shelter (a roof or a building) adds this much and keeps rain and wind off.</summary>
        public const float ShelterBonusC = 10f;

        /// <summary>Real seconds between lightning strikes in a full storm.</summary>
        public const float StrikeIntervalMin = 12f;

        public const float StrikeIntervalMax = 40f;

        /// <summary>
        /// Health a strike takes from an unprotected structure, as a fraction of its maximum: the first
        /// strike damages it, a second breaks it. Lightning never destroys anything.
        /// </summary>
        public const float StrikeDamageFraction = 0.5f;

        /// <summary>Chance that a strike finds the settlement: this base plus <see cref="StrikeChancePerConductor"/> per exposed conductor, capped.</summary>
        public const float StrikeChanceBase = 0.1f;

        public const float StrikeChancePerConductor = 0.04f;
        public const float StrikeChanceMax = 0.4f;

        /// <summary>Hearing radius of thunder at the point of a strike (draws the infected).</summary>
        public const float ThunderNoiseRadius = 35f;

        /// <summary>Minimum duration of each front in game hours (index = WeatherKind).</summary>
        private static readonly double[] MinHours = { 5, 2, 2, 1 };

        private static readonly double[] MaxHours = { 12, 6, 5, 2.5 };

        public static double RollDurationHours(WeatherKind kind, DeterministicRandom rng)
        {
            int i = (int)kind;
            return MinHours[i] + (MaxHours[i] - MinHours[i]) * rng.NextFloat();
        }

        /// <summary>
        /// Markov chain of fronts. Clear skies always cloud over first, and storms only grow out of
        /// clouds or rain. <paramref name="severity"/> scales the chance of a storm.
        /// </summary>
        public static WeatherKind RollNext(WeatherKind current, float severity, bool calm, DeterministicRandom rng)
        {
            float storm, rain, clear, overcast;
            switch (current)
            {
                case WeatherKind.Clear:
                    return WeatherKind.Overcast;
                case WeatherKind.Overcast:
                    rain = 0.45f;
                    clear = 0.45f;
                    storm = 0.10f;
                    overcast = 0f;
                    break;
                case WeatherKind.Rain:
                    overcast = 0.5f;
                    clear = 0.25f;
                    storm = 0.25f;
                    rain = 0f;
                    break;
                default:
                    rain = 0.75f;
                    overcast = 0.25f;
                    clear = 0f;
                    storm = 0f;
                    break;
            }
            storm *= Math.Max(0f, severity);
            if (calm) storm = 0f;
            float total = storm + rain + clear + overcast;
            float roll = rng.NextFloat() * total;
            if ((roll -= storm) < 0f) return WeatherKind.Storm;
            if ((roll -= rain) < 0f) return WeatherKind.Rain;
            if ((roll -= clear) < 0f) return WeatherKind.Clear;
            return WeatherKind.Overcast;
        }

        /// <summary>Cloud cover 0–1 of a fully developed front.</summary>
        public static float Cloud(WeatherKind k) => k == WeatherKind.Clear ? 0.15f : k == WeatherKind.Overcast ? 0.75f : k == WeatherKind.Rain ? 0.88f : 1f;

        /// <summary>Precipitation 0–1 of a fully developed front.</summary>
        public static float Rain(WeatherKind k) => k == WeatherKind.Rain ? 0.6f : k == WeatherKind.Storm ? 1f : 0f;

        /// <summary>Wind and lightning 0–1 of a fully developed front.</summary>
        public static float Storm(WeatherKind k) => k == WeatherKind.Storm ? 1f : 0f;

        public static string Describe(WeatherKind k) => k == WeatherKind.Clear ? "Clear" : k == WeatherKind.Overcast ? "Overcast" : k == WeatherKind.Rain ? "Rain" : "Thunderstorm";

        /// <summary>Outdoor temperature (°C) of a normal day: about 9 °C before dawn, 20 °C mid-afternoon.</summary>
        public static float BaseTemperatureC(double hours24) => 14.5f + 5.5f * (float)Math.Cos(2.0 * Math.PI * (hours24 - 15.0) / 24.0);
    }
}
