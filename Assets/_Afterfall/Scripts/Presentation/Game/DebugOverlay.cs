using System.Text;
using Afterfall.Core;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>F3 performance/diagnostics overlay: frame time, simulation tick cost, entity counts.</summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private GameController _gc;
        private float _avgMs = 16f;
        private float _worstMs;
        private float _resetWorst;
        private readonly StringBuilder _sb = new StringBuilder();
        private GUIStyle _style;

        public void Initialize(GameController gc) => _gc = gc;

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
        }

        private void OnGUI()
        {
            if (_gc == null || !_gc.Settings.ShowFps || _gc.Session == null) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            _sb.Clear();
            _sb.AppendLine($"{1000f / _avgMs:0} FPS  ({_avgMs:0.0} ms avg, {_worstMs:0.0} ms worst 5s)");
            foreach (TickScheduler.Entry e in _gc.Session.Scheduler.Entries)
                _sb.AppendLine($"{e.System.Name,-18} {1.0 / e.Interval:0.#} Hz  avg {e.AverageTickMs:0.000} ms  max {e.MaxTickMs:0.00} ms");
            var w = _gc.Session.World;
            _sb.AppendLine($"Structures {w.Structures.Count} · nodes {w.ResourceNodes.Count} · drops {w.Drops.Count} · infected {_gc.Session.Zombies.AliveCount} (views {_gc.Sync.Zombies.Count})");
            _sb.AppendLine($"Power networks {_gc.Session.Power.Networks.Count} · rebuilds {_gc.Session.Power.TopologyRebuilds} · heat {w.Threat.Heat:0.0} · target pop {_gc.Session.Threats.TargetPopulation}");
            _sb.AppendLine($"NavMesh {(_gc.NavReady ? "ready" : "baking")} · events {_gc.Session.Events.PublishedCount} · dropped sim time {_gc.Session.Scheduler.DroppedSeconds:0.0}s");
            _sb.AppendLine($"Player {w.Player.Position} · explored {w.Exploration.RevealedFraction * 100:0}%");
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(8, 120, 560, 140), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14, 124, 550, 140), _sb.ToString(), _style);
        }
    }
}
