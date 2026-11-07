using System;

namespace Afterfall.Core
{
    /// <summary>
    /// Authoritative simulation time. Elapsed time is measured in simulation seconds
    /// (1:1 with real time at normal speed); a configurable day length maps it to the
    /// in-game day/night cycle. Default: 48 real minutes per game day.
    /// </summary>
    public sealed class SimulationClock
    {
        public const double DefaultDayLengthSeconds = 48 * 60;

        /// <summary>Time of day at which a new game starts (0.30 = 07:12).</summary>
        public const double DefaultStartTimeOfDay = 0.30;

        public SimulationClock(double dayLengthSeconds = DefaultDayLengthSeconds, double elapsedSeconds = 0, double startTimeOfDay = DefaultStartTimeOfDay)
        {
            DayLengthSeconds = dayLengthSeconds <= 0 ? DefaultDayLengthSeconds : dayLengthSeconds;
            ElapsedSeconds = Math.Max(0, elapsedSeconds);
            StartTimeOfDay = startTimeOfDay;
        }

        public double DayLengthSeconds { get; }
        public double StartTimeOfDay { get; }
        public double ElapsedSeconds { get; private set; }

        private double TotalDays => StartTimeOfDay + ElapsedSeconds / DayLengthSeconds;

        /// <summary>1-based day counter.</summary>
        public int Day => (int)Math.Floor(TotalDays) + 1;

        /// <summary>Fraction of the current day in [0,1): 0 = midnight, 0.5 = noon.</summary>
        public double TimeOfDay01 => TotalDays - Math.Floor(TotalDays);

        public double Hours24 => TimeOfDay01 * 24.0;

        /// <summary>Night spans 20:00–05:30.</summary>
        public bool IsNight => Hours24 >= 20.0 || Hours24 < 5.5;

        /// <summary>Approximate ambient daylight in [0,1] (used by AI perception and lighting).</summary>
        public float Daylight01
        {
            get
            {
                double h = Hours24;
                if (h >= 7 && h <= 18.5) return 1f;
                if (h >= 20 || h <= 5) return 0.15f;
                if (h < 7) return (float)(0.15 + 0.85 * (h - 5) / 2.0);
                return (float)(0.15 + 0.85 * (20 - h) / 1.5);
            }
        }

        /// <summary>Simulation seconds that correspond to one in-game hour.</summary>
        public double SecondsPerGameHour => DayLengthSeconds / 24.0;

        public string FormatTime()
        {
            int totalMinutes = (int)(TimeOfDay01 * 24 * 60);
            return $"Day {Day}  {totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }

        public void Advance(double seconds)
        {
            if (seconds > 0) ElapsedSeconds += seconds;
        }
    }
}
