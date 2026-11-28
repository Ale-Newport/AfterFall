using System;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Motion-captured gait cycles (a walk and a run from the CMU Graphics Lab Motion Capture
    /// Database, converted by tools/mocap/convert_cmu.py) in the rig's own pose parameters: pelvis
    /// offset and swing, chest and head swing, upper arms and elbows, ankle targets and foot pitch.
    /// One cycle covers <see cref="Clip.stride"/> metres, so a clip played by distance travelled keeps
    /// the planted foot still at any speed. The <c>-afterfall-procedural-gait</c> flag turns them off.
    /// </summary>
    public static class GaitClips
    {
        [Serializable]
        public sealed class Frame
        {
            public float[] pelvis = new float[3], pelvisRot = new float[3], chestRot = new float[3], headRot = new float[3];
            public float[] armL = new float[3], armR = new float[3], footL = new float[3], footR = new float[3];
            public float foreL, foreR, pitchL, pitchR;
        }

        [Serializable]
        public sealed class Clip
        {
            public string name = "", source = "";
            public float stride, duration, speed;
            public Frame[] frames = Array.Empty<Frame>();
        }

        /// <summary>A pose sampled from the clips (rig parameters, before the rig's own posture is added).</summary>
        public struct Sample
        {
            public Vector3 Pelvis, PelvisRot, ChestRot, HeadRot, ArmL, ArmR, FootL, FootR;
            public float ForeL, ForeR, PitchL, PitchR;
        }

        public static bool Enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-afterfall-procedural-gait") < 0;

        private static bool _loaded;
        private static Clip _walk, _run;

        public static Clip Walk
        {
            get
            {
                Load();
                return _walk;
            }
        }

        public static Clip Run
        {
            get
            {
                Load();
                return _run;
            }
        }

        public static bool Available => Enabled && Walk != null && Run != null;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _walk = Read("walk");
            _run = Read("run");
        }

        private static Clip Read(string name)
        {
            var text = Resources.Load<TextAsset>("Characters/Motion/" + name);
            if (text == null) return null;
            Clip c = JsonUtility.FromJson<Clip>(text.text);
            return c != null && c.frames.Length > 1 && c.stride > 0.1f ? c : null;
        }

        /// <summary>How much of the run is mixed in at a speed (m/s): all walk below 2 m/s, all run above 3.6.</summary>
        public static float RunWeight(float speed) => Mathf.Clamp01((speed - 2f) / 1.6f);

        /// <summary>Metres per cycle at a speed (the walk's and run's strides mixed).</summary>
        public static float Stride(float speed) => Mathf.Lerp(Walk.stride, Run.stride, RunWeight(speed));

        /// <summary>The walk and run mixed at <paramref name="phase"/> (0..1, from the left heel strike).</summary>
        public static Sample At(float phase, float runWeight)
        {
            Sample a = At(Walk, phase), b = At(Run, phase);
            if (runWeight <= 0f) return a;
            if (runWeight >= 1f) return b;
            return new Sample
            {
                Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, runWeight),
                PelvisRot = Vector3.Lerp(a.PelvisRot, b.PelvisRot, runWeight),
                ChestRot = Vector3.Lerp(a.ChestRot, b.ChestRot, runWeight),
                HeadRot = Vector3.Lerp(a.HeadRot, b.HeadRot, runWeight),
                ArmL = Vector3.Lerp(a.ArmL, b.ArmL, runWeight),
                ArmR = Vector3.Lerp(a.ArmR, b.ArmR, runWeight),
                FootL = Vector3.Lerp(a.FootL, b.FootL, runWeight),
                FootR = Vector3.Lerp(a.FootR, b.FootR, runWeight),
                ForeL = Mathf.Lerp(a.ForeL, b.ForeL, runWeight),
                ForeR = Mathf.Lerp(a.ForeR, b.ForeR, runWeight),
                PitchL = Mathf.Lerp(a.PitchL, b.PitchL, runWeight),
                PitchR = Mathf.Lerp(a.PitchR, b.PitchR, runWeight)
            };
        }

        private static Sample At(Clip c, float phase)
        {
            int n = c.frames.Length;
            float f = Mathf.Repeat(phase, 1f) * n;
            int i0 = Mathf.FloorToInt(f) % n, i1 = (i0 + 1) % n;
            float t = f - Mathf.Floor(f);
            Frame a = c.frames[i0], b = c.frames[i1];
            return new Sample
            {
                Pelvis = V(a.pelvis, b.pelvis, t),
                PelvisRot = V(a.pelvisRot, b.pelvisRot, t),
                ChestRot = V(a.chestRot, b.chestRot, t),
                HeadRot = V(a.headRot, b.headRot, t),
                ArmL = V(a.armL, b.armL, t),
                ArmR = V(a.armR, b.armR, t),
                FootL = V(a.footL, b.footL, t),
                FootR = V(a.footR, b.footR, t),
                ForeL = Mathf.Lerp(a.foreL, b.foreL, t),
                ForeR = Mathf.Lerp(a.foreR, b.foreR, t),
                PitchL = Mathf.Lerp(a.pitchL, b.pitchL, t),
                PitchR = Mathf.Lerp(a.pitchR, b.pitchR, t)
            };
        }

        private static Vector3 V(float[] a, float[] b, float t) =>
            a.Length < 3 || b.Length < 3 ? Vector3.zero : Vector3.Lerp(new Vector3(a[0], a[1], a[2]), new Vector3(b[0], b[1], b[2]), t);
    }
}
