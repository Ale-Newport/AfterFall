using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Ground cover meshes for the Afterfall/GroundCover shader (grass tufts, wildflowers, ferns,
    /// reeds, pebbles, leaf litter). Conventions differ from the rest of the factory: vertex colour
    /// rgb is a multiplier around 0.5 (the shader doubles it), alpha is ambient occlusion, and uv0.x
    /// says how strongly the colour of the ground beneath tints the vertex (1 for blades and stems,
    /// 0 for petals). The pivot is at ground level; blades start a little below it so they never
    /// float on uneven terrain. Shapes vary per variant from a fixed seed, so they are stable.
    /// </summary>
    public static partial class MeshFactory
    {
        public const int GrassShort = 0, GrassMedium = 1, GrassTall = 2, GrassDry = 3;

        /// <summary>A tuft of grass blades: <see cref="GrassShort"/>, <see cref="GrassMedium"/>, <see cref="GrassTall"/> or <see cref="GrassDry"/>.</summary>
        public static Mesh GrassTuft(int kind) => Cached("gc_grass" + kind, () =>
        {
            var b = new CoverBuilder();
            int seed = 7100 + kind * 37;
            int blades = kind == GrassShort ? 22 : kind == GrassMedium ? 20 : kind == GrassTall ? 15 : 17;
            float hMin = kind == GrassShort ? 0.16f : kind == GrassMedium ? 0.28f : kind == GrassTall ? 0.42f : 0.26f;
            float hMax = kind == GrassShort ? 0.32f : kind == GrassMedium ? 0.5f : kind == GrassTall ? 0.78f : 0.52f;
            float leanMax = kind == GrassDry ? 1.25f : 0.85f;
            for (int i = 0; i < blades; i++)
            {
                float a = Rand(seed, i, 0) * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Rand(seed, i, 1)) * (kind == GrassTall ? 0.15f : 0.13f);
                var root = new Vector3(Mathf.Cos(a) * r, -0.04f, Mathf.Sin(a) * r);
                float yaw = a + (Rand(seed, i, 2) - 0.5f) * 1.6f;
                float h = Mathf.Lerp(hMin, hMax, Rand(seed, i, 3));
                float lean = Mathf.Lerp(0.15f, leanMax, Rand(seed, i, 4) * Rand(seed, i, 5) + 0.25f * Rand(seed, i, 6));
                float w = (kind == GrassTall ? 0.013f : 0.016f) * Mathf.Lerp(0.8f, 1.3f, Rand(seed, i, 7));
                float tone = Rand(seed, i, 8);
                Color tip = kind == GrassDry ? new Color(0.62f, 0.6f, 0.5f, 1f) : Color.Lerp(new Color(0.6f, 0.62f, 0.46f, 1f), new Color(0.72f, 0.68f, 0.44f, 1f), tone * tone);
                b.Blade(root, yaw, lean, h, w, new Color(0.27f, 0.28f, 0.27f, 0.4f), tip);
            }
            if (kind == GrassTall || kind == GrassDry)
            {
                // A few seed stalks above the leaves.
                for (int i = 0; i < 3; i++)
                {
                    float a = Rand(seed, 40 + i, 0) * Mathf.PI * 2f;
                    var root = new Vector3(Mathf.Cos(a) * 0.04f, -0.04f, Mathf.Sin(a) * 0.04f);
                    float h = hMax * Mathf.Lerp(1f, 1.25f, Rand(seed, 40 + i, 1));
                    Vector3 top = b.Blade(root, a, 0.25f, h, 0.005f, new Color(0.3f, 0.3f, 0.28f, 0.45f), new Color(0.6f, 0.56f, 0.44f, 1f));
                    b.Spike(top, a, 0.07f, 0.012f, new Color(0.62f, 0.56f, 0.4f, 1f));
                }
            }
            return b.Build("grass tuft " + kind);
        });

        public const int FlowerDaisy = 0, FlowerButtercup = 1, FlowerCornflower = 2, FlowerPoppy = 3;

        /// <summary>A small cluster of wildflowers with a few blades at the base.</summary>
        public static Mesh FlowerCluster(int kind) => Cached("gc_flower" + kind, () =>
        {
            var b = new CoverBuilder();
            int seed = 7300 + kind * 53;
            Color petal, centre;
            int petals;
            float size;
            switch (kind)
            {
                case FlowerButtercup: petal = new Color(0.42f, 0.33f, 0.04f, 1f); centre = new Color(0.36f, 0.28f, 0.05f, 1f); petals = 5; size = 0.028f; break;
                case FlowerCornflower: petal = new Color(0.15f, 0.16f, 0.36f, 1f); centre = new Color(0.12f, 0.1f, 0.22f, 1f); petals = 7; size = 0.033f; break;
                case FlowerPoppy: petal = new Color(0.4f, 0.07f, 0.03f, 1f); centre = new Color(0.06f, 0.05f, 0.04f, 1f); petals = 4; size = 0.042f; break;
                default: petal = new Color(0.42f, 0.42f, 0.39f, 1f); centre = new Color(0.4f, 0.3f, 0.05f, 1f); petals = 9; size = 0.03f; break;
            }
            int count = 3 + (int)(Rand(seed, 0, 9) * 4f);
            for (int i = 0; i < count; i++)
            {
                float a = Rand(seed, i, 0) * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Rand(seed, i, 1)) * 0.14f;
                var root = new Vector3(Mathf.Cos(a) * r, -0.03f, Mathf.Sin(a) * r);
                float h = Mathf.Lerp(0.2f, 0.42f, Rand(seed, i, 2));
                Vector3 top = b.Blade(root, a, Mathf.Lerp(0.05f, 0.35f, Rand(seed, i, 3)), h, 0.0045f, new Color(0.28f, 0.3f, 0.26f, 0.45f), new Color(0.42f, 0.46f, 0.36f, 1f));
                b.Tint = 0f;
                b.Blossom(top, petals, size * Mathf.Lerp(0.85f, 1.2f, Rand(seed, i, 4)), Rand(seed, i, 5) * 6.28f, Rand(seed, i, 6) * 0.5f, petal, centre);
                b.Tint = 1f;
            }
            for (int i = 0; i < 5; i++)
            {
                float a = Rand(seed, 20 + i, 0) * Mathf.PI * 2f;
                var root = new Vector3(Mathf.Cos(a) * 0.05f, -0.04f, Mathf.Sin(a) * 0.05f);
                b.Blade(root, a, 0.5f, Mathf.Lerp(0.12f, 0.22f, Rand(seed, 20 + i, 1)), 0.012f, new Color(0.27f, 0.28f, 0.27f, 0.4f), new Color(0.6f, 0.62f, 0.48f, 1f));
            }
            return b.Build("flowers " + kind);
        });

        /// <summary>A fern: arching fronds with alternating leaflets.</summary>
        public static Mesh Fern(int variant) => Cached("gc_fern" + variant, () =>
        {
            var b = new CoverBuilder { Tint = 0.25f };
            int seed = 7500 + variant * 29;
            int fronds = 7 + variant % 3;
            for (int f = 0; f < fronds; f++)
            {
                float yaw = (f + Rand(seed, f, 0) * 0.6f) / fronds * Mathf.PI * 2f;
                float len = Mathf.Lerp(0.42f, 0.72f, Rand(seed, f, 1));
                float rise = Mathf.Lerp(1.0f, 1.6f, Rand(seed, f, 2));
                b.Frond(yaw, len, rise, 14, new Color(0.34f, 0.36f, 0.3f, 0.5f), new Color(0.56f, 0.6f, 0.44f, 1f));
            }
            return b.Build("fern " + variant);
        });

        /// <summary>Reeds and cattails for the waterline.</summary>
        public static Mesh Reeds(int variant) => Cached("gc_reeds" + variant, () =>
        {
            var b = new CoverBuilder { Tint = 0.25f };
            int seed = 7700 + variant * 31;
            for (int i = 0; i < 12; i++)
            {
                float a = Rand(seed, i, 0) * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Rand(seed, i, 1)) * 0.22f;
                var root = new Vector3(Mathf.Cos(a) * r, -0.08f, Mathf.Sin(a) * r);
                float h = Mathf.Lerp(0.8f, 1.45f, Rand(seed, i, 2));
                b.Blade(root, a + (Rand(seed, i, 3) - 0.5f) * 1.2f, Mathf.Lerp(0.05f, 0.4f, Rand(seed, i, 4)), h, 0.011f,
                    new Color(0.3f, 0.31f, 0.27f, 0.45f), Color.Lerp(new Color(0.56f, 0.58f, 0.42f, 1f), new Color(0.66f, 0.6f, 0.42f, 1f), Rand(seed, i, 5)));
            }
            for (int i = 0; i < 2 + variant % 2; i++)
            {
                float a = Rand(seed, 30 + i, 0) * Mathf.PI * 2f;
                var root = new Vector3(Mathf.Cos(a) * 0.08f, -0.08f, Mathf.Sin(a) * 0.08f);
                float h = Mathf.Lerp(1.2f, 1.55f, Rand(seed, 30 + i, 1));
                Vector3 top = b.Blade(root, a, 0.08f, h, 0.004f, new Color(0.3f, 0.31f, 0.27f, 0.45f), new Color(0.5f, 0.5f, 0.38f, 1f));
                b.Tint = 0f;
                b.Head(top - new Vector3(0f, 0.16f, 0f), 0.13f, 0.014f, new Color(0.2f, 0.12f, 0.07f, 1f));
                b.Tint = 0.25f;
            }
            return b.Build("reeds " + variant);
        });

        /// <summary>A few small stones half sunk into the ground.</summary>
        public static Mesh Pebbles(int variant) => Cached("gc_pebbles" + variant, () =>
        {
            var b = new CoverBuilder { Tint = 0.35f };
            int seed = 7900 + variant * 41;
            int count = 3 + (int)(Rand(seed, 0, 9) * 5f);
            for (int i = 0; i < count; i++)
            {
                float a = Rand(seed, i, 0) * Mathf.PI * 2f;
                float d = Mathf.Sqrt(Rand(seed, i, 1)) * 0.3f;
                float s = Mathf.Lerp(0.022f, 0.075f, Rand(seed, i, 2) * Rand(seed, i, 3));
                float tone = Mathf.Lerp(0.38f, 0.6f, Rand(seed, i, 4));
                float warm = (Rand(seed, i, 5) - 0.5f) * 0.06f;
                b.Pebble(new Vector3(Mathf.Cos(a) * d, s * 0.22f, Mathf.Sin(a) * d), new Vector3(s, s * Mathf.Lerp(0.45f, 0.75f, Rand(seed, i, 6)), s * Mathf.Lerp(0.7f, 1f, Rand(seed, i, 7))),
                    a * 57f, new Color(tone + warm, tone, tone - warm, 1f), seed + i * 13);
            }
            return b.Build("pebbles " + variant);
        });

        /// <summary>Fallen leaves, needles and a twig or two lying flat on the ground.</summary>
        public static Mesh Litter(int variant) => Cached("gc_litter" + variant, () =>
        {
            var b = new CoverBuilder { Tint = 0.2f };
            int seed = 8100 + variant * 43;
            var palette = new[]
            {
                new Color(0.36f, 0.25f, 0.12f, 1f), new Color(0.3f, 0.16f, 0.08f, 1f), new Color(0.24f, 0.17f, 0.1f, 1f), new Color(0.4f, 0.32f, 0.15f, 1f)
            };
            for (int i = 0; i < 9; i++)
            {
                float a = Rand(seed, i, 0) * Mathf.PI * 2f;
                float d = Mathf.Sqrt(Rand(seed, i, 1)) * 0.35f;
                float len = Mathf.Lerp(0.05f, 0.09f, Rand(seed, i, 2));
                b.Leaf(new Vector3(Mathf.Cos(a) * d, 0.012f + i * 0.0015f, Mathf.Sin(a) * d), Rand(seed, i, 3) * 360f, len, len * 0.45f,
                    (Rand(seed, i, 4) - 0.5f) * 25f, palette[(int)(Rand(seed, i, 5) * palette.Length) % palette.Length]);
            }
            for (int i = 0; i < 2; i++)
            {
                float a = Rand(seed, 20 + i, 0) * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(a) * 0.12f, 0.012f, Mathf.Sin(a) * 0.12f);
                float yaw = Rand(seed, 20 + i, 1) * Mathf.PI;
                float len = Mathf.Lerp(0.18f, 0.38f, Rand(seed, 20 + i, 2));
                Vector3 dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * (len * 0.5f);
                b.B.Color = new Color(0.2f, 0.15f, 0.1f, 0.8f);
                b.B.Cylinder(c - dir, c + dir + new Vector3(0f, 0.015f, 0f), 0.007f, 0.004f, 4, false, false, false);
            }
            return b.Build("litter " + variant);
        });

        private static float Rand(int seed, int i, int k) => Art.TextureSynth.Hash01(i, k, seed);

        /// <summary>A <see cref="MeshBuilder"/> that also records the ground-tint weight (uv0.x) of every vertex.</summary>
        private sealed class CoverBuilder
        {
            public readonly MeshBuilder B = new MeshBuilder();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private float _tint = 1f;

            /// <summary>Ground-tint weight for the vertices added from now on.</summary>
            public float Tint
            {
                get => _tint;
                set
                {
                    Sync();
                    _tint = value;
                }
            }

            private void Sync()
            {
                while (_uv.Count < B.VertexCount) _uv.Add(new Vector2(_tint, 0f));
            }

            public Mesh Build(string name)
            {
                Sync();
                Mesh m = B.Build(name);
                m.SetUVs(0, _uv);
                m.RecalculateBounds();
                return m;
            }

            /// <summary>A curved, tapering blade leaning along <paramref name="yaw"/>; returns the tip position.</summary>
            public Vector3 Blade(Vector3 root, float yaw, float lean, float height, float width, Color rootColor, Color tipColor)
            {
                const int segments = 3;
                var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var side = new Vector3(-dir.z, 0f, dir.x);
                float twist = (Mathf.Repeat(yaw * 7.13f, 1f) - 0.5f) * 0.8f;
                Vector3 p = root;
                float ds = height / segments;
                int prevL = -1, prevR = -1;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    float angle = lean * t * t + lean * 0.25f * t;
                    Vector3 tangent = dir * Mathf.Sin(angle) + Vector3.up * Mathf.Cos(angle);
                    Vector3 sd = Quaternion.AngleAxis(twist * t * Mathf.Rad2Deg, tangent) * side;
                    Vector3 normal = Vector3.Cross(sd, tangent).normalized;
                    Color c = Color.Lerp(rootColor, tipColor, t);
                    if (s == segments)
                    {
                        int tip = B.Vertex(p, normal, c);
                        B.Triangle(prevL, prevR, tip);
                        return p;
                    }
                    float w = width * Mathf.Pow(1f - t, 0.6f);
                    int l = B.Vertex(p - sd * w, normal, c);
                    int r = B.Vertex(p + sd * w, normal, c);
                    if (prevL >= 0)
                    {
                        B.Triangle(prevL, prevR, r);
                        B.Triangle(prevL, r, l);
                    }
                    prevL = l;
                    prevR = r;
                    p += tangent * ds;
                }
                return p;
            }

            /// <summary>A seed head: a narrow spindle continuing the stalk.</summary>
            public void Spike(Vector3 basePoint, float yaw, float length, float width, Color color)
            {
                var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                Vector3 up = (Vector3.up + dir * 0.25f).normalized;
                for (int k = 0; k < 2; k++)
                {
                    Vector3 side = k == 0 ? new Vector3(-dir.z, 0f, dir.x) : dir;
                    Vector3 n = Vector3.Cross(side, up).normalized;
                    int a = B.Vertex(basePoint, n, color * 0.8f);
                    int l = B.Vertex(basePoint + up * (length * 0.4f) - side * width, n, color);
                    int r = B.Vertex(basePoint + up * (length * 0.4f) + side * width, n, color);
                    int t = B.Vertex(basePoint + up * length, n, color);
                    B.Triangle(a, r, l);
                    B.Triangle(l, r, t);
                }
            }

            /// <summary>A flower head: a fan of petals around a small domed centre, tilted a little.</summary>
            public void Blossom(Vector3 centre, int petals, float radius, float spin, float tilt, Color petal, Color heart)
            {
                Quaternion q = Quaternion.AngleAxis(tilt * Mathf.Rad2Deg, new Vector3(Mathf.Cos(spin), 0f, Mathf.Sin(spin)));
                Vector3 up = q * Vector3.up;
                for (int i = 0; i < petals; i++)
                {
                    float a = spin + i * Mathf.PI * 2f / petals;
                    Vector3 d = q * new Vector3(Mathf.Cos(a), 0.18f, Mathf.Sin(a));
                    Vector3 s = q * new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    float half = Mathf.PI / petals * 0.85f;
                    int c0 = B.Vertex(centre, up, petal * 0.85f);
                    int l = B.Vertex(centre + d * (radius * 0.6f) - s * (radius * Mathf.Sin(half) * 0.9f), up, petal);
                    int t = B.Vertex(centre + d * radius, up, petal);
                    int r = B.Vertex(centre + d * (radius * 0.6f) + s * (radius * Mathf.Sin(half) * 0.9f), up, petal);
                    B.Triangle(c0, l, t);
                    B.Triangle(c0, t, r);
                }
                Color saved = B.Color;
                B.Color = heart;
                B.Sphere(centre + up * (radius * 0.12f), new Vector3(radius * 0.32f, radius * 0.2f, radius * 0.32f), 0, false);
                B.Color = saved;
            }

            /// <summary>An arching fern frond with alternating, shrinking leaflets.</summary>
            public void Frond(float yaw, float length, float rise, int pairs, Color rootColor, Color tipColor)
            {
                var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var side = new Vector3(-dir.z, 0f, dir.x);
                Vector3 Point(float t) => dir * (length * t) + Vector3.up * (length * rise * (t * (1.15f - t)) * 1.3f) + Vector3.down * 0.02f;
                Vector3 prev = Point(0f);
                for (int i = 1; i <= pairs; i++)
                {
                    float t = (float)i / pairs;
                    Vector3 p = Point(t);
                    Vector3 tangent = (p - prev).normalized;
                    Vector3 n = Vector3.Cross(side, tangent).normalized;
                    Color c = Color.Lerp(rootColor, tipColor, t);
                    // Stem segment.
                    int a = B.Vertex(prev - side * 0.003f, n, c);
                    int b = B.Vertex(prev + side * 0.003f, n, c);
                    int e = B.Vertex(p, n, c);
                    B.Triangle(a, b, e);
                    // Leaflets on both sides, longest at the middle of the frond.
                    float leaf = length * 0.24f * Mathf.Sin(Mathf.Clamp01(t * 1.1f) * Mathf.PI) + 0.01f;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector3 basePoint = Vector3.Lerp(prev, p, k < 0 ? 0.3f : 0.7f);
                        Vector3 outDir = (side * k + tangent * 0.45f + Vector3.down * 0.15f).normalized;
                        int l0 = B.Vertex(basePoint - tangent * (leaf * 0.12f), n, c);
                        int l1 = B.Vertex(basePoint + outDir * leaf, n, c * 1.08f);
                        int l2 = B.Vertex(basePoint + tangent * (leaf * 0.22f), n, c);
                        B.Triangle(l0, l1, l2);
                    }
                    prev = p;
                }
            }

            /// <summary>A cattail head: a short dark cylinder.</summary>
            public void Head(Vector3 bottom, float length, float radius, Color color)
            {
                Color saved = B.Color;
                B.Color = color;
                B.Cylinder(bottom, bottom + Vector3.up * length, radius, radius * 0.8f, 6, true, true, true);
                B.Color = saved;
            }

            /// <summary>A low, irregular stone.</summary>
            public void Pebble(Vector3 centre, Vector3 radii, float yawDeg, Color color, int seed)
            {
                Matrix4x4 saved = B.Matrix;
                Color savedColor = B.Color;
                B.Matrix = saved * Matrix4x4.TRS(centre, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
                B.Color = color;
                B.Sphere(Vector3.zero, radii, 1, false, d => 0.85f + Hash(seed, d) * 0.3f);
                B.Matrix = saved;
                B.Color = savedColor;
            }

            /// <summary>A flat pointed leaf lying on the ground, slightly cupped.</summary>
            public void Leaf(Vector3 centre, float yawDeg, float length, float width, float rollDeg, Color color)
            {
                Quaternion q = Quaternion.Euler(rollDeg * 0.3f, yawDeg, rollDeg);
                Vector3 f = q * Vector3.forward, s = q * Vector3.right, up = q * Vector3.up;
                int a = B.Vertex(centre - f * (length * 0.5f), up, color * 0.9f);
                int l = B.Vertex(centre - s * (width * 0.5f) + up * 0.004f, up, color);
                int t = B.Vertex(centre + f * (length * 0.5f), up, color * 1.05f);
                int r = B.Vertex(centre + s * (width * 0.5f) + up * 0.004f, up, color);
                B.Triangle(a, l, t);
                B.Triangle(a, t, r);
            }
        }
    }
}
