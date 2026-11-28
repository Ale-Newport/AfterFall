using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// The face of a skinned character: blinking every few seconds and a few expressions mixed from
    /// the model's blend shapes (made from MakeHuman's expression units by the character build):
    /// <c>blink</c>, <c>smile</c>, <c>pain</c>, <c>snarl</c> and <c>mouth_open</c>. Survivors wince
    /// when hit and smile now and then; the infected snarl, and gape when they lunge; the dead close
    /// their eyes. Only faces on screen are updated.
    /// </summary>
    public sealed class CharacterFace
    {
        private struct Target
        {
            public SkinnedMeshRenderer Renderer;
            public int Blink, Smile, Pain, Snarl, Open;
        }

        private readonly List<Target> _targets = new List<Target>();
        private readonly bool _infected;
        private uint _rng;
        private float _nextBlink, _blinkT = -1f, _nextSmile, _smileHold, _pain;
        private float _blink, _smile, _painW, _snarl, _open;
        private float _time;

        private CharacterFace(bool infected, int seed)
        {
            _infected = infected;
            _rng = (uint)(seed * 747796405 + 2891336453u) | 1u;
            _nextBlink = 1f + Random01() * 3f;
            _nextSmile = 8f + Random01() * 20f;
        }

        /// <summary>False (or <c>-afterfall-no-faces</c>) leaves every face still.</summary>
        public static bool Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-afterfall-no-faces") < 0;

        /// <summary>The face of a model instance, or null when it has no expression blend shapes (older builds).</summary>
        public static CharacterFace Bind(GameObject model, bool infected, int seed)
        {
            if (!Enabled) return null;
            var face = new CharacterFace(infected, seed);
            foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh m = r.sharedMesh;
                if (m == null || m.blendShapeCount == 0) continue;
                var t = new Target
                {
                    Renderer = r, Blink = m.GetBlendShapeIndex("blink"), Smile = m.GetBlendShapeIndex("smile"), Pain = m.GetBlendShapeIndex("pain"),
                    Snarl = m.GetBlendShapeIndex("snarl"), Open = m.GetBlendShapeIndex("mouth_open")
                };
                if (t.Blink < 0 && t.Smile < 0 && t.Pain < 0 && t.Snarl < 0 && t.Open < 0) continue;
                face._targets.Add(t);
            }
            return face._targets.Count > 0 ? face : null;
        }

        private float Random01()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>Hit: a wince that fades over about a second.</summary>
        public void Hurt() => _pain = 1.2f;

        public void Update(float dt, bool dead, bool attacking)
        {
            _time += dt;
            bool visible = false;
            foreach (Target t in _targets)
                if (t.Renderer != null && t.Renderer.isVisible) visible = true;

            // Blinks: a quick close and a slower open every 2–6 s (more often when hurt).
            float blink = 0f;
            if (dead) blink = 1f;
            else
            {
                _nextBlink -= dt;
                if (_blinkT < 0f && _nextBlink <= 0f)
                {
                    _blinkT = 0f;
                    _nextBlink = (_pain > 0f ? 1f : 2f) + Random01() * 4f;
                }
                if (_blinkT >= 0f)
                {
                    _blinkT += dt;
                    blink = _blinkT < 0.07f ? _blinkT / 0.07f : _blinkT < 0.11f ? 1f : Mathf.Max(0f, 1f - (_blinkT - 0.11f) / 0.13f);
                    if (_blinkT > 0.24f) _blinkT = -1f;
                }
            }

            float smile = 0f, pain = 0f, snarl = 0f, open = 0f;
            _pain = Mathf.Max(0f, _pain - dt);
            if (_infected)
            {
                // A standing snarl, a slow groan, and the jaw wide when lunging.
                snarl = dead ? 0f : 0.45f + 0.15f * Mathf.Sin(_time * 0.9f);
                open = dead ? 0.3f : Mathf.Max(0f, Mathf.Sin(_time * 0.6f)) * 0.35f + (attacking ? 0.65f : 0f);
            }
            else if (dead) open = 0.25f;
            else
            {
                pain = Mathf.Clamp01(_pain);
                _nextSmile -= dt;
                if (_nextSmile <= 0f)
                {
                    _smileHold = 2f + Random01() * 3f;
                    _nextSmile = 15f + Random01() * 30f;
                }
                if (_smileHold > 0f)
                {
                    _smileHold -= dt;
                    smile = pain > 0f ? 0f : 0.65f;
                }
                if (attacking) pain = Mathf.Max(pain, 0.35f); // effort
            }

            float k = 1f - Mathf.Exp(-dt * 10f);
            _blink = blink; // blinks are fast: no smoothing
            _smile = Mathf.Lerp(_smile, smile, k * 0.5f);
            _painW = Mathf.Lerp(_painW, pain, k);
            _snarl = Mathf.Lerp(_snarl, snarl, k * 0.6f);
            _open = Mathf.Lerp(_open, open, k);
            if (!visible) return;
            foreach (Target t in _targets)
            {
                if (t.Renderer == null) continue;
                Set(t.Renderer, t.Blink, _blink);
                Set(t.Renderer, t.Smile, _smile);
                Set(t.Renderer, t.Pain, _painW);
                Set(t.Renderer, t.Snarl, _snarl);
                Set(t.Renderer, t.Open, _open);
            }
        }

        private static void Set(SkinnedMeshRenderer r, int index, float w)
        {
            if (index >= 0) r.SetBlendShapeWeight(index, w * 100f);
        }
    }
}
