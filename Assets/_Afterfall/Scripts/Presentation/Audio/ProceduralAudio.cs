using System;
using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Synthesises every sound effect at startup (noise bursts, decaying tones, engine loops),
    /// so the game ships without third-party audio. Clips are cached by name.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int Rate = 44100;
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static AudioClip Get(string name)
        {
            if (Cache.TryGetValue(name, out AudioClip c) && c != null) return c;
            c = Build(name);
            Cache[name] = c;
            return c;
        }

        private static AudioClip Build(string name)
        {
            var rnd = new System.Random(name.GetHashCode());
            switch (name)
            {
                case "step0": case "step1": case "step2": return Clip(name, 0.13f, (t, i) => Lowpass(Noise(rnd), 0.35f, i) * Env(t, 0.004f, 0.11f) * 0.55f);
                case "impact_wood": return Clip(name, 0.32f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 110 * t) * 0.7f + Noise(rnd) * 0.35f * Env(t, 0.001f, 0.03f)) * Env(t, 0.002f, 0.28f));
                case "impact_stone": return Clip(name, 0.25f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 1250 * t) * 0.25f * Env(t, 0.001f, 0.12f) + Noise(rnd) * 0.6f * Env(t, 0.001f, 0.06f)) * 0.8f);
                case "impact_metal": return Clip(name, 0.45f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 820 * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 1310 * t) * 0.2f) * Env(t, 0.001f, 0.4f) + Noise(rnd) * 0.3f * Env(t, 0.001f, 0.04f));
                case "impact_soft": return Clip(name, 0.18f, (t, i) => Lowpass(Noise(rnd), 0.2f, i) * Env(t, 0.003f, 0.15f) * 0.6f);
                case "swing": return Clip(name, 0.28f, (t, i) => Bandpass(Noise(rnd), i) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.28f)) * 0.5f);
                case "hit_flesh": return Clip(name, 0.22f, (t, i) => (Lowpass(Noise(rnd), 0.12f, i) * 1.2f + Mathf.Sin(2 * Mathf.PI * 70 * t) * 0.5f) * Env(t, 0.002f, 0.18f));
                case "player_hurt": return Clip(name, 0.35f, (t, i) => (Saw(160 - 60 * t, t) * 0.25f + Noise(rnd) * 0.1f) * Env(t, 0.01f, 0.3f));
                case "groan0": case "groan1": case "groan2": return Groan(name, rnd, 1.1f + (float)rnd.NextDouble() * 0.6f);
                case "zombie_attack": return Clip(name, 0.5f, (t, i) => (Saw(180 + 220 * t, t) * 0.35f + Noise(rnd) * 0.25f) * Env(t, 0.02f, 0.45f));
                case "zombie_death": return Groan(name, rnd, 1.6f, 0.6f);
                case "build": return Clip(name, 0.16f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 300 * t) * 0.5f + Noise(rnd) * 0.4f * Env(t, 0.001f, 0.02f)) * Env(t, 0.001f, 0.14f));
                case "complete": return Clip(name, 0.6f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 523 * t) * Step(t, 0f, 0.3f) + Mathf.Sin(2 * Mathf.PI * 784 * t) * Step(t, 0.15f, 0.6f)) * 0.25f * Env(t, 0.01f, 0.55f));
                case "pickup": return Clip(name, 0.12f, (t, i) => Mathf.Sin(2 * Mathf.PI * (600 + 900 * t) * t) * 0.3f * Env(t, 0.005f, 0.1f));
                case "click": return Clip(name, 0.04f, (t, i) => Mathf.Sin(2 * Mathf.PI * 1800 * t) * 0.25f * Env(t, 0.001f, 0.035f));
                case "alert": return Clip(name, 0.5f, (t, i) => Mathf.Sin(2 * Mathf.PI * (t < 0.25f ? 660 : 440) * t) * 0.3f * Env(t, 0.01f, 0.48f));
                case "warning": return Clip(name, 1.6f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 55 * t) + Mathf.Sin(2 * Mathf.PI * 82 * t) * 0.6f) * 0.3f * Mathf.Sin(Mathf.PI * t / 1.6f));
                case "loop_generator": return Loop(name, 1f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 50 * t) * 0.4f + Saw(100, t) * 0.15f + Lowpass(Noise(rnd), 0.1f, i) * 0.2f) * (0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * 12 * t)));
                case "loop_workshop": return Loop(name, 2f, (t, i) => Mathf.Sin(2 * Mathf.PI * 180 * t) * 0.18f + Saw(90, t) * 0.06f + (Mathf.Repeat(t, 1f) < 0.05f ? Noise(rnd) * 0.4f : 0f));
                case "loop_fire": return Loop(name, 2f, (t, i) => Lowpass(Noise(rnd), 0.25f, i) * 0.25f + (rnd.NextDouble() < 0.0006 ? 0.8f : 0f));
                case "loop_wind": return Loop(name, 6f, (t, i) => Lowpass(Noise(rnd), 0.03f, i) * 2.2f * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * t / 6f)));
                case "loop_night": return Loop(name, 3f, (t, i) => Mathf.Sin(2 * Mathf.PI * 4200 * t) * 0.05f * (Mathf.Repeat(t * 7f, 1f) < 0.12f && Mathf.Repeat(t, 1.5f) < 0.6f ? 1f : 0f) + Lowpass(Noise(rnd), 0.03f, i) * 0.8f);
                default: return Clip(name, 0.1f, (t, i) => 0f);
            }
        }

        private static AudioClip Groan(string name, System.Random rnd, float length, float pitch = 1f)
        {
            float f0 = (85f + (float)rnd.NextDouble() * 40f) * pitch;
            return Clip(name, length, (t, i) =>
            {
                float vib = Mathf.Sin(2 * Mathf.PI * 5.5f * t) * 6f;
                float f = f0 + vib - 25f * t / length;
                float v = Saw(f, t) * 0.35f + Lowpass(Noise(rnd), 0.15f, i) * 0.25f;
                return v * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / length)) * 0.8f;
            });
        }

        // ------------------------------------------------------------------ synthesis helpers

        private static float _lp, _bpLow, _bpBand;

        private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        private static float Lowpass(float x, float a, int i)
        {
            if (i == 0) _lp = 0f;
            _lp += a * (x - _lp);
            return _lp;
        }

        private static float Bandpass(float x, int i)
        {
            if (i == 0) _bpLow = _bpBand = 0f;
            _bpLow += 0.3f * _bpBand;
            float high = x - _bpLow - 0.5f * _bpBand;
            _bpBand += 0.3f * high;
            return _bpBand;
        }

        private static float Saw(float freq, float t) => 2f * Mathf.Repeat(freq * t, 1f) - 1f;

        /// <summary>Attack/decay envelope.</summary>
        private static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / Mathf.Max(0.001f, decay) * 4f);

        private static float Step(float t, float from, float to) => t >= from && t < to ? 1f : 0f;

        private static AudioClip Clip(string name, float seconds, Func<float, int, float> sample)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate, i), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Loopable clip: the last 10% is cross-faded into the start to avoid clicks.</summary>
        private static AudioClip Loop(string name, float seconds, Func<float, int, float> sample)
        {
            int n = (int)(seconds * Rate);
            int fade = n / 10;
            var raw = new float[n + fade];
            for (int i = 0; i < raw.Length; i++) raw[i] = sample(i / (float)Rate, i);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = raw[i];
                if (i < fade)
                {
                    float k = i / (float)fade;
                    v = raw[i] * k + raw[n + i] * (1f - k);
                }
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
