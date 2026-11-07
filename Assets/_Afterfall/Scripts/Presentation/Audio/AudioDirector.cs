using System.Collections.Generic;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.World;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Plays the procedural soundscape: footsteps, impacts, combat, infected, machine loops,
    /// ambience and UI cues, plus text captions for important sounds (accessibility).
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private GameController _gc;
        private AudioSource _ui, _wind, _night;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<EntityId, AudioSource> _loops = new Dictionary<EntityId, AudioSource>();
        private readonly List<System.IDisposable> _subs = new List<System.IDisposable>();
        private float _stepDistance;
        private Vector3 _lastPlayerPos;
        private float _loopScan;
        private float _groanScan;
        private int _poolIndex;
        private int _stepVariant;

        public string Caption { get; private set; } = "";
        public float CaptionUntil { get; private set; }

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _ui = Source2D("UI");
            _wind = Source2D("Wind");
            _wind.clip = ProceduralAudio.Get("loop_wind");
            _wind.loop = true;
            _wind.volume = 0.25f;
            _wind.Play();
            _night = Source2D("Night");
            _night.clip = ProceduralAudio.Get("loop_night");
            _night.loop = true;
            _night.volume = 0f;
            _night.Play();
            for (int i = 0; i < 20; i++)
            {
                var go = new GameObject("Sfx");
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                Configure3D(s, 40f);
                _pool.Add(s);
            }
            _lastPlayerPos = gc.Player.transform.position;

            var bus = gc.Session.Events;
            _subs.Add(bus.Subscribe<ItemsGathered>(OnGathered));
            _subs.Add(bus.Subscribe<PlayerDamaged>(_ =>
            {
                PlayAt("hit_flesh", _gc.Player.transform.position, 0.9f);
                PlayAt("player_hurt", _gc.Player.transform.position, 0.7f);
            }));
            _subs.Add(bus.Subscribe<ZombieKilled>(e => PlayAt("zombie_death", new Vector3(e.Position.X, e.Position.Y, e.Position.Z), 0.9f)));
            _subs.Add(bus.Subscribe<BuildingCompleted>(e =>
            {
                if (_gc.Session.World.Structures.TryGetValue(e.StructureId, out Structure s)) PlayAt("complete", s.Position.ToUnity(), 0.8f);
            }));
            _subs.Add(bus.Subscribe<ThreatWarningIssued>(e =>
            {
                Play2D("warning", 0.8f);
                SetCaption("[Ominous rumble — " + e.Message + "]");
            }));
            _subs.Add(bus.Subscribe<Notification>(n =>
            {
                if (n.Severity == Severity.Danger) Play2D("alert", 0.5f);
                else if (n.Severity == Severity.Success) Play2D("pickup", 0.35f);
            }));
            _subs.Add(bus.Subscribe<BuildingRemoved>(e =>
            {
                if (e.Reason.StartsWith("destroyed")) SetCaption("[Something breaks with a loud crack]");
            }));
        }

        private void OnDestroy()
        {
            foreach (System.IDisposable s in _subs) s.Dispose();
        }

        private AudioSource Source2D(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            return s;
        }

        private static void Configure3D(AudioSource s, float maxDistance)
        {
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f;
            s.maxDistance = maxDistance;
            s.dopplerLevel = 0f;
            s.playOnAwake = false;
        }

        public void PlayAt(string clip, Vector3 position, float volume = 1f, float pitchJitter = 0.08f)
        {
            AudioSource s = _pool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % _pool.Count;
            s.transform.position = position;
            s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            s.PlayOneShot(ProceduralAudio.Get(clip), volume);
        }

        public void Play2D(string clip, float volume = 1f) => _ui.PlayOneShot(ProceduralAudio.Get(clip), volume);

        private void OnGathered(ItemsGathered e)
        {
            if (!_gc.Session.World.ResourceNodes.TryGetValue(e.NodeId, out ResourceNodeState n)) return;
            ResourceNodeDefinition def = _gc.Session.Content.Node(n.DefId);
            string clip = def.Visual.StartsWith("tree") || def.Visual == "branches" ? "impact_wood"
                : def.Visual == "rock" || def.Visual == "ore" ? "impact_stone"
                : def.Visual == "scrap" || def.Visual == "car" || def.Visual == "transformer" || def.Visual == "pump_gas" ? "impact_metal"
                : "impact_soft";
            PlayAt(clip, n.Position.ToUnity() + Vector3.up, 0.8f);
        }

        public void SetCaption(string text)
        {
            if (!_gc.Settings.Captions) return;
            Caption = text;
            CaptionUntil = Time.unscaledTime + 3f;
        }

        private void Update()
        {
            if (_gc?.Session == null || _gc.Player == null) return;
            float dt = Time.deltaTime;
            float daylight = _gc.Session.World.Clock.Daylight01;
            _wind.volume = Mathf.Lerp(_wind.volume, Mathf.Lerp(0.12f, 0.3f, daylight), dt);
            _night.volume = Mathf.Lerp(_night.volume, (1f - daylight) * 0.35f, dt);
            bool paused = _gc.Session.Paused;
            _wind.pitch = paused ? 0.6f : 1f;

            // Footsteps from distance actually travelled on the ground.
            Vector3 p = _gc.Player.transform.position;
            Vector3 flat = new Vector3(p.x - _lastPlayerPos.x, 0f, p.z - _lastPlayerPos.z);
            _lastPlayerPos = p;
            if (!_gc.IsDead && flat.magnitude < 2f)
            {
                _stepDistance += flat.magnitude;
                float stride = _gc.Player.IsSprinting ? 1.6f : _gc.Player.IsCrouching ? 0.9f : 1.25f;
                if (_stepDistance >= stride)
                {
                    _stepDistance = 0f;
                    _stepVariant = (_stepVariant + 1) % 3;
                    PlayAt("step" + _stepVariant, p, _gc.Player.IsCrouching ? 0.25f : _gc.Player.IsSprinting ? 0.8f : 0.5f, 0.12f);
                }
            }

            _loopScan -= dt;
            if (_loopScan <= 0f)
            {
                _loopScan = 0.25f;
                UpdateMachineLoops();
            }
            _groanScan -= dt;
            if (_groanScan <= 0f)
            {
                _groanScan = 1f;
                UpdateGroans();
            }
        }

        private void UpdateMachineLoops()
        {
            var alive = new HashSet<EntityId>();
            foreach (KeyValuePair<EntityId, StructureView> kv in _gc.Sync.Structures)
            {
                if (!_gc.Session.World.Structures.TryGetValue(kv.Key, out Structure s) || !s.IsComplete || kv.Value == null) continue;
                string clip = null;
                if (s.Generator != null && s.Generator.Running && s.Generator.OutputWatts > 1f) clip = "loop_generator";
                else if (s.Machine != null && s.Machine.Status == MachineStatus.Working) clip = s.Machine.Fuel != null ? "loop_fire" : "loop_workshop";
                if (clip == null) continue;
                alive.Add(kv.Key);
                if (!_loops.TryGetValue(kv.Key, out AudioSource src) || src == null)
                {
                    src = kv.Value.gameObject.AddComponent<AudioSource>();
                    Configure3D(src, clip == "loop_generator" ? 50f : 25f);
                    src.loop = true;
                    src.volume = 0f;
                    _loops[kv.Key] = src;
                    if (clip == "loop_generator") SetCaption("[Generator running]");
                }
                if (src.clip == null || src.clip.name != clip)
                {
                    src.clip = ProceduralAudio.Get(clip);
                    src.Play();
                }
                src.volume = Mathf.MoveTowards(src.volume, clip == "loop_generator" ? 0.7f : 0.45f, 0.25f);
            }
            var stop = new List<EntityId>();
            foreach (KeyValuePair<EntityId, AudioSource> kv in _loops)
            {
                if (alive.Contains(kv.Key)) continue;
                if (kv.Value == null)
                {
                    stop.Add(kv.Key);
                    continue;
                }
                kv.Value.volume = Mathf.MoveTowards(kv.Value.volume, 0f, 0.3f);
                if (kv.Value.volume <= 0.01f)
                {
                    kv.Value.Stop();
                    kv.Value.clip = null;
                }
            }
            foreach (EntityId id in stop) _loops.Remove(id);
        }

        private void UpdateGroans()
        {
            Vector3 p = _gc.Player.transform.position;
            foreach (KeyValuePair<EntityId, ZombieView> kv in _gc.Sync.Zombies)
            {
                ZombieView z = kv.Value;
                if (z == null) continue;
                float d = Vector3.Distance(z.transform.position, p);
                if (d > 35f || Random.value > 0.12f) continue;
                if (_gc.Session.World.Zombies.TryGetValue(kv.Key, out var state) && state.IsDead) continue;
                PlayAt("groan" + Random.Range(0, 3), z.transform.position + Vector3.up * 1.6f, 0.75f, 0.12f);
                if (d < 20f) SetCaption($"[Infected groaning {Direction(z.transform.position)}]");
            }
        }

        public void ZombieAttack(Vector3 position)
        {
            PlayAt("zombie_attack", position + Vector3.up * 1.5f, 0.9f);
            SetCaption($"[Infected lunges {Direction(position)}]");
        }

        private string Direction(Vector3 source)
        {
            Transform cam = _gc.CameraRig.ViewTransform;
            Vector3 to = source - cam.position;
            to.y = 0f;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            float angle = Vector3.SignedAngle(fwd, to, Vector3.up);
            if (Mathf.Abs(angle) < 45f) return "ahead";
            if (Mathf.Abs(angle) > 135f) return "behind you";
            return angle > 0 ? "to your right" : "to your left";
        }
    }
}
