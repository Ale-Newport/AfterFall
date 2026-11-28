using Afterfall.Domain.Vehicles;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// A vehicle's headlights and engine. The lights come on with the engine at dusk, at night and in
    /// rain or storms (the L key switches them to always on or off) (two spotlights and red tail lights; the quad bike has one lamp); the engine is a
    /// procedural loop whose pitch and volume follow the revs, with a starter sound when it catches.
    /// </summary>
    public sealed partial class VehicleView
    {
        /// <summary>The headlight switch: automatic (on with the engine in the dark), always on, or off.</summary>
        public enum LightMode
        {
            Auto,
            On,
            Off
        }

        public LightMode Lights { get; set; } = LightMode.Auto;

        private Light[] _headlights;
        private Light _taillights;
        private AudioSource _engine;
        private bool _wasRunning;
        private float _revs;

        private void BuildLightsAndEngine()
        {
            bool atv = Def.Category == VehicleCategory.Atv;
            float front = Def.SizeZ * 0.5f - 0.05f, y = atv ? 0.78f : 0.74f;
            float[] xs = atv ? new[] { 0f } : new[] { -Def.SizeX * 0.34f, Def.SizeX * 0.34f };
            _headlights = new Light[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                var go = new GameObject("Headlight");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(xs[i], y, front);
                go.transform.localRotation = Quaternion.Euler(6f, 0f, 0f); // dipped a little
                Light l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 45f;
                l.spotAngle = 62f;
                l.innerSpotAngle = 30f;
                l.color = new Color(1f, 0.95f, 0.84f);
                l.intensity = atv ? 9f : 7f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                _headlights[i] = l;
            }
            var tail = new GameObject("Taillights");
            tail.transform.SetParent(transform, false);
            tail.transform.localPosition = new Vector3(0f, y - 0.05f, -Def.SizeZ * 0.5f - 0.3f);
            _taillights = tail.AddComponent<Light>();
            _taillights.type = LightType.Point;
            _taillights.range = 4f;
            _taillights.color = new Color(1f, 0.1f, 0.06f);
            _taillights.intensity = 1.2f;
            _taillights.shadows = LightShadows.None;
            _taillights.enabled = false;

            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = ProceduralAudio.Get("loop_engine");
            _engine.loop = true;
            _engine.playOnAwake = false;
            _engine.spatialBlend = 1f;
            _engine.rolloffMode = AudioRolloffMode.Linear;
            _engine.minDistance = 3f;
            _engine.maxDistance = 60f;
            _engine.dopplerLevel = 0.3f;
            _engine.volume = 0f;
        }

        private void UpdateLightsAndEngine(VehicleState v, float dt)
        {
            if (_engine == null) BuildLightsAndEngine();
            bool running = v.EngineOn && v.Fuel > 0f;
            var weather = _gc.Session.WeatherNow;
            bool dark = _gc.Session.World.Clock.Daylight01 < 0.35f || weather.Rain > 0.4f || weather.Storm > 0.2f;
            bool lit = running && (Lights == LightMode.On || (Lights == LightMode.Auto && dark));
            foreach (Light l in _headlights)
                if (l.enabled != lit) l.enabled = lit;
            if (_taillights.enabled != lit) _taillights.enabled = lit;
            if (lit && Driven) _taillights.intensity = Throttle < -0.05f || Mathf.Abs(ForwardSpeed) < 0.3f ? 2.6f : 1.2f; // brighter braking or stopped

            if (running && !_wasRunning) _gc.Audio?.PlayAt("engine_start", transform.position + Vector3.up * 0.6f, 0.8f, 0f);
            _wasRunning = running;
            // Revs: from idle to full with speed (relative to the top speed) and the throttle.
            float target = running ? Mathf.Clamp01(Mathf.Abs(ForwardSpeed) * 3.6f / Mathf.Max(30f, Def.MaxSpeedKmh) * 0.75f + (Driven ? Mathf.Abs(Throttle) * 0.35f : 0f)) : 0f;
            _revs = Mathf.MoveTowards(_revs, target, dt * 1.8f);
            if (running)
            {
                if (!_engine.isPlaying) _engine.Play();
                bool atv = Def.Category == VehicleCategory.Atv;
                _engine.pitch = (atv ? 1.35f : 1f) * (0.75f + _revs * 1.6f);
                _engine.volume = Mathf.MoveTowards(_engine.volume, 0.35f + _revs * 0.45f, dt * 2f);
            }
            else if (_engine.isPlaying)
            {
                _engine.volume = Mathf.MoveTowards(_engine.volume, 0f, dt * 3f);
                if (_engine.volume <= 0.01f) _engine.Stop();
            }
        }

        /// <summary>Whether the headlights are on now (the tour checks them at night).</summary>
        public bool HeadlightsOn => _headlights != null && _headlights.Length > 0 && _headlights[0].enabled;

        /// <summary>Whether the engine sound is playing now.</summary>
        public bool EngineAudible => _engine != null && _engine.isPlaying;
    }
}
