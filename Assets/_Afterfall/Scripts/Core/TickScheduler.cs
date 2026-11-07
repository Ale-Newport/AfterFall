using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Afterfall.Core
{
    /// <summary>A simulation system advanced with a fixed timestep by <see cref="TickScheduler"/>.</summary>
    public interface ISimulationSystem
    {
        string Name { get; }
        void Tick(double dt);
    }

    /// <summary>
    /// Runs simulation systems at independent fixed frequencies (e.g. survival at 10 Hz,
    /// machines at 4 Hz, the threat director at 0.5 Hz) using time accumulation, so results
    /// do not depend on rendering frame rate and nothing requires an active GameObject.
    /// </summary>
    public sealed class TickScheduler
    {
        /// <summary>Upper bound on catch-up steps per Advance to avoid a spiral of death after a hitch.</summary>
        public const int MaxStepsPerAdvance = 8;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Stopwatch _stopwatch = new Stopwatch();

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Simulated time dropped because a system could not catch up (diagnostics).</summary>
        public double DroppedSeconds { get; private set; }

        public void Register(ISimulationSystem system, double hz)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (hz <= 0) throw new ArgumentOutOfRangeException(nameof(hz));
            _entries.Add(new Entry(system, 1.0 / hz));
        }

        public void Advance(double dt)
        {
            if (dt <= 0) return;
            foreach (Entry e in _entries)
            {
                e.Accumulator += dt;
                int steps = 0;
                while (e.Accumulator >= e.Interval && steps < MaxStepsPerAdvance)
                {
                    _stopwatch.Restart();
                    e.System.Tick(e.Interval);
                    _stopwatch.Stop();
                    e.RecordTiming(_stopwatch.Elapsed.TotalMilliseconds);
                    e.Accumulator -= e.Interval;
                    steps++;
                }
                if (e.Accumulator >= e.Interval)
                {
                    double drop = e.Accumulator - (e.Accumulator % e.Interval);
                    DroppedSeconds += drop;
                    e.Accumulator -= drop;
                }
            }
        }

        public sealed class Entry
        {
            internal Entry(ISimulationSystem system, double interval)
            {
                System = system;
                Interval = interval;
            }

            public ISimulationSystem System { get; }
            public double Interval { get; }
            internal double Accumulator;
            public long TickCount { get; private set; }
            public double LastTickMs { get; private set; }
            public double AverageTickMs { get; private set; }
            public double MaxTickMs { get; private set; }

            internal void RecordTiming(double ms)
            {
                TickCount++;
                LastTickMs = ms;
                AverageTickMs += (ms - AverageTickMs) / Math.Min(TickCount, 100);
                if (ms > MaxTickMs) MaxTickMs = ms;
            }
        }
    }
}
