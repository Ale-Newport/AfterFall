using System.Globalization;
using System.Text;
using Afterfall.Core;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using UnityEngine;
using UnityEngine.Profiling;

namespace Afterfall.Presentation
{
    /// <summary>
    /// F3 developer diagnostics overlay. Each section (performance, simulation, entities,
    /// position, navigation, power, economy, saves) can be switched on its own from the
    /// creative panel's Debug tab; the choice is kept in <see cref="AppSettings"/>.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private GameController _gc;
        private float _avgMs = 16f;
        private float _worstMs;
        private float _resetWorst;
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private GUIStyle _style;
        private float _slowRefresh;
        private int _rigidbodies, _awakeBodies, _navAgents;
        private double _cpuMs = -1, _gpuMs = -1;
        private string _lastSave = "none this session";
        private System.IDisposable _saveSub;

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _saveSub = gc.Session.Events.Subscribe<SaveCompleted>(s => _lastSave = $"{(s.Success ? "OK" : "FAILED")} at {System.DateTime.Now:HH:mm:ss}: {s.Message}");
        }

        private void OnDestroy() => _saveSub?.Dispose();

        private bool On(DiagnosticSection s) => (_gc.Settings.DiagnosticSections & (int)s) != 0;

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _avgMs = Mathf.Lerp(_avgMs, ms, 0.05f);
            _worstMs = Mathf.Max(_worstMs, ms);
            _resetWorst -= Time.unscaledDeltaTime;
            if (_resetWorst <= 0f)
            {
                _resetWorst = 5f;
                _worstMs = ms;
            }
            if (_gc == null || !_gc.Settings.ShowFps) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                _cpuMs = _timings[0].cpuFrameTime;
                _gpuMs = _timings[0].gpuFrameTime;
            }
            _slowRefresh -= Time.unscaledDeltaTime;
            if (_slowRefresh <= 0f)
            {
                // Scene-wide counts are not free: once a second is enough.
                _slowRefresh = 1f;
                Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
                _rigidbodies = bodies.Length;
                _awakeBodies = 0;
                foreach (Rigidbody b in bodies)
                    if (!b.isKinematic && !b.IsSleeping()) _awakeBodies++;
                _navAgents = FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None).Length;
            }
        }

        private void OnGUI()
        {
            if (_gc == null || !_gc.Settings.ShowFps || _gc.Session == null) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            var w = _gc.Session.World;
            CultureInfo ci = CultureInfo.InvariantCulture;
            _sb.Clear();
            if (On(DiagnosticSection.Performance))
            {
                _sb.AppendLine(string.Format(ci, "{0:0} FPS  ({1:0.0} ms avg, {2:0.0} ms worst 5s) · CPU {3} · GPU {4}", 1000f / _avgMs, _avgMs, _worstMs,
                    _cpuMs > 0 ? _cpuMs.ToString("0.0", ci) + " ms" : "n/a", _gpuMs > 0 ? _gpuMs.ToString("0.0", ci) + " ms" : "n/a"));
                _sb.AppendLine(string.Format(ci, "Memory: {0:0} MB allocated, {1:0} MB reserved, managed heap {2:0} MB · rigidbodies {3} ({4} awake)",
                    Profiler.GetTotalAllocatedMemoryLong() / 1048576.0, Profiler.GetTotalReservedMemoryLong() / 1048576.0, System.GC.GetTotalMemory(false) / 1048576.0, _rigidbodies, _awakeBodies));
            }
            if (On(DiagnosticSection.Simulation))
            {
                foreach (TickScheduler.Entry e in _gc.Session.Scheduler.Entries)
                    _sb.AppendLine(string.Format(ci, "{0,-18} {1:0.#} Hz  avg {2:0.000} ms  max {3:0.00} ms", e.System.Name, 1.0 / e.Interval, e.AverageTickMs, e.MaxTickMs));
                _sb.AppendLine(string.Format(ci, "Events {0} · dropped sim time {1:0.0}s · paused {2}", _gc.Session.Events.PublishedCount, _gc.Session.Scheduler.DroppedSeconds, _gc.Session.Paused));
            }
            if (On(DiagnosticSection.Entities))
            {
                int colonists = 0;
                foreach (var c in w.Colonists.Values)
                    if (c.Recruited) colonists++;
                _sb.AppendLine($"Structures {w.Structures.Count} · nodes {w.ResourceNodes.Count} · drops {w.Drops.Count} · infected {_gc.Session.Zombies.AliveCount} (views {_gc.Sync.Zombies.Count}) · survivors {w.Colonists.Count} ({colonists} recruited) · jobs {w.Jobs.Count}");
                _sb.AppendLine(string.Format(ci, "Threat heat {0:0.0} · target population {1}", w.Threat.Heat, _gc.Session.Threats.TargetPopulation));
            }
            if (On(DiagnosticSection.Position))
            {
                Vector3 p = _gc.Player.transform.position;
                _sb.AppendLine(string.Format(ci, "Position X {0:0.0} Y {1:0.0} Z {2:0.0} · heading {3:0}° · ground {4:0.0} · map {5:0} m · explored {6:0}%{7}",
                    p.x, p.y, p.z, _gc.CameraRig.ViewTransform.eulerAngles.y, _gc.GroundHeight(p), w.Layout.Size, w.Exploration.RevealedFraction * 100f,
                    _gc.Player.Flight.Active ? string.Format(ci, " · FLYING {0:0} m/s", _gc.Player.Flight.CurrentSpeed) : ""));
                var poi = w.Layout.PoiAt(p.x, p.z);
                _sb.AppendLine($"Mode {w.Settings.ModeLabel()} · area {(poi != null ? poi.Name : "wilderness")}");
            }
            if (On(DiagnosticSection.Navigation))
                _sb.AppendLine($"NavMesh {(_gc.NavReady ? "ready" : "baking")} · agents {_navAgents} · infected views {_gc.Sync.Zombies.Count}");
            if (On(DiagnosticSection.Power))
            {
                _sb.AppendLine($"Power networks {_gc.Session.Power.Networks.Count} · topology rebuilds {_gc.Session.Power.TopologyRebuilds}");
                foreach (var net in _gc.Session.Power.Networks)
                    _sb.AppendLine(string.Format(ci, "  grid {0}: capacity {1:0} W, demand {2:0} W, supplied {3:0} W, stored {4:0} kJ, generators {5}/{6} running, {7} unpowered · {8}", net.Id, net.CapacityWatts, net.DemandWatts, net.SuppliedWatts, net.StoredJoules / 1000.0, net.RunningGenerators, net.Generators, net.UnpoweredConsumers, net.Status));
            }
            if (On(DiagnosticSection.Economy))
            {
                int inventories = 0, reservations = 0;
                foreach (Inventory inv in w.AllInventories)
                {
                    inventories++;
                    foreach (ReservationRecord _ in inv.Reservations) reservations++;
                }
                int failures = 0;
                foreach (var c in w.Colonists.Values) failures += c.RecentFailures.Count;
                _sb.AppendLine($"Inventories {inventories} · open reservations {reservations} · colonist failures recorded {failures}");
            }
            if (On(DiagnosticSection.Saves))
                _sb.AppendLine($"Slot {_gc.CurrentSlot} · last save {_lastSave} · saves this world {w.Metrics.Saves}");

            string text = _sb.ToString();
            float height = _style.CalcHeight(new GUIContent(text), 700f) + 10f;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(8, 120, 712, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14, 124, 700, height), text, _style);
        }
    }
}
