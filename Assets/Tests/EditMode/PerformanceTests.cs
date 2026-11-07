using System;
using System.Diagnostics;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.World;
using Afterfall.Services;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>
    /// Coarse simulation budget check: a busy settlement (40 machines, generators, infected)
    /// must simulate far faster than real time. Thresholds are generous so the test is stable
    /// on slow CI machines; the measured numbers are printed for the progress log.
    /// </summary>
    public class PerformanceTests
    {
        [Test]
        public void BusySettlement_SimulatesWellWithinFrameBudget()
        {
            GameSession s = TestSupport.NewSession(4242);
            s.World.Tech.Unlocked.Add("metalworking");
            s.World.Tech.Unlocked.Add("electricity");
            Vec3 c = s.Player.Position;
            for (int i = 0; i < 40; i++)
            {
                var pos = new Vec3(c.X + (i % 10) * 3f, c.Y, c.Z + (i / 10) * 3f + 6f);
                bool electric = i % 2 == 0;
                Structure st = TestSupport.Build(s.World, electric ? "workshop" : "furnace", pos);
                st.Machine.RecipeId = electric ? "gears" : "smelt_scrap";
                st.Machine.Input.AddUpTo(electric ? "iron_ingot" : "scrap_metal", 90);
                st.Machine.Fuel?.AddUpTo("charcoal", 30);
                if (i % 8 == 0)
                {
                    Structure g = TestSupport.Build(s.World, "generator", new Vec3(pos.X, pos.Y, pos.Z - 1.5f));
                    g.Generator.Fuel.AddUpTo("fuel_can", 5);
                }
            }
            s.World.Clock.Advance(s.World.Clock.DayLengthSeconds * 0.6); // night: larger infected population

            const double seconds = 120;
            const double frame = 1.0 / 60.0;
            var sw = Stopwatch.StartNew();
            int frames = 0;
            for (double t = 0; t < seconds; t += frame)
            {
                s.Update(frame);
                frames++;
            }
            sw.Stop();
            double perFrame = sw.Elapsed.TotalMilliseconds / frames;
            string report = $"{frames} frames, {perFrame:0.0000} ms/frame, structures={s.World.Structures.Count}, infected={s.Zombies.AliveCount}";
            foreach (TickScheduler.Entry e in s.Scheduler.Entries) report += $"\n  {e.System.Name}: avg {e.AverageTickMs:0.0000} ms, max {e.MaxTickMs:0.000} ms";
            TestContext.WriteLine(report);
            Console.WriteLine(report);
            Assert.Less(perFrame, 2.0, "simulation must stay far below a 16.7 ms frame");
        }
    }
}
