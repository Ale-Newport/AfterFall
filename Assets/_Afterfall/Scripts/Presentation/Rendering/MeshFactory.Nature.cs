using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Resource-node models: conifers and birches (with stumps), boulders and ore seams, fibre
    /// tufts, berry bushes, branch piles, scrap heaps, car wrecks, fuel pumps, burnt transformers
    /// and items lying in the world. Each model is built once per variant in a single pass, one
    /// mesh per material, and cached; all randomness is hashed from the variant so it is stable.
    /// </summary>
    public static partial class MeshFactory
    {
        private static readonly Dictionary<string, Mesh[]> NatureSets = new Dictionary<string, Mesh[]>();
        private static readonly Dictionary<int, Vector3[]> IcoDirCache = new Dictionary<int, Vector3[]>();
        private static readonly Dictionary<int, int[]> IcoTriCache = new Dictionary<int, int[]>();

        /// <summary>
        /// Builds a mesh, converting its vertex colours from the sRGB values they are authored in to
        /// the linear values the shader multiplies with (the project renders in linear space; the
        /// material tint is converted by Unity, vertex colours are not). Alpha (ambient occlusion) is kept.
        /// </summary>
        private static Mesh NatureBuild(MeshBuilder b, string name)
        {
            Mesh m = b.Build(name);
            Color[] cols = m.colors;
            for (int i = 0; i < cols.Length; i++)
            {
                Color c = cols[i];
                cols[i] = new Color(Mathf.GammaToLinearSpace(Mathf.Max(0f, c.r)), Mathf.GammaToLinearSpace(Mathf.Max(0f, c.g)), Mathf.GammaToLinearSpace(Mathf.Max(0f, c.b)), c.a);
            }
            m.colors = cols;
            return m;
        }

        /// <summary>Builds the meshes of one model (one per material) in a single pass and caches them together.</summary>
        private static Mesh[] NatureSet(string key, System.Func<Mesh[]> build)
        {
            if (NatureSets.TryGetValue(key, out Mesh[] set) && set != null && set[0] != null) return set;
            set = build();
            NatureSets[key] = set;
            return set;
        }

        // ================================================================ trees

        /// <summary>Pine variant: [0] bark trunk with root flare, [1] needle crown. Both pivot at the tree base.</summary>
        public static Mesh[] NaturePine(int variant) => NatureSet("nat:pine:" + (variant & 3), () =>
        {
            int v = variant & 3;
            PineShape(v, out float height, out float crownBase, out float rMax, out float shape, out Vector3 lean, out float orange);
            int seed = 1100 + v * 37;
            var bark = new MeshBuilder();
            PineTrunk(bark, seed, height - 0.7f, crownBase, lean, orange, -1f);
            var crown = new MeshBuilder();
            PineCrownTiers(crown, seed, height, crownBase, rMax, shape, lean, PineTone(v));
            return new[] { NatureBuild(bark, "pine trunk"), NatureBuild(crown, "pine crown") };
        });

        /// <summary>Felled pine or birch: [0] bark stump with roots, [1] cut end grain, splinters and chips (object-space material).</summary>
        public static Mesh[] NatureStump(int variant, bool birch) => NatureSet("nat:stump:" + (variant & 3) + (birch ? "b" : "p"), () =>
        {
            int v = variant & 3;
            int seed = (birch ? 2300 : 1300) + v * 41;
            float cut = birch ? 0.42f + Rnd(seed, 1) * 0.12f : 0.5f + Rnd(seed, 1) * 0.14f;
            var bark = new MeshBuilder();
            var wood = new MeshBuilder();
            float radius;
            if (birch)
            {
                BirchTrunk(bark, seed, 6.5f, Vector3.zero, cut, out radius);
            }
            else
            {
                PineShape(v, out float height, out float crownBase, out _, out _, out _, out float orange);
                PineTrunk(bark, seed, height - 0.7f, crownBase, Vector3.zero, orange, cut);
                radius = PineRadius(cut, height - 0.7f);
            }
            CutTop(wood, seed, cut, radius * 0.93f);
            return new[] { NatureBuild(bark, "stump bark"), NatureBuild(wood, "stump cut") };
        });

        /// <summary>Birch variant: [0] white trunk and limbs, [1] leaf clusters. Both pivot at the tree base.</summary>
        public static Mesh[] NatureBirch(int variant) => NatureSet("nat:birch:" + (variant & 3), () =>
        {
            int v = variant & 3;
            int seed = 2100 + v * 53;
            float height = 6.6f + Rnd(seed, 2) * 0.9f;
            float leanYaw = Rnd(seed, 3) * Mathf.PI * 2f;
            float leanAmount = 0.025f + Rnd(seed, 4) * 0.045f;
            var lean = new Vector3(Mathf.Cos(leanYaw), 0f, Mathf.Sin(leanYaw)) * leanAmount;
            var bark = new MeshBuilder();
            BirchTrunk(bark, seed, height, lean, -1f, out _);
            var leaves = new MeshBuilder();
            BirchCrown(bark, leaves, seed, height, lean, BirchTone(v));
            return new[] { NatureBuild(bark, "birch trunk"), NatureBuild(leaves, "birch crown") };
        });

        private static void PineShape(int v, out float height, out float crownBase, out float rMax, out float shape, out Vector3 lean, out float orange)
        {
            switch (v & 3)
            {
                case 0: height = 8.5f; crownBase = 1.4f; rMax = 1.9f; shape = 0.95f; lean = new Vector3(0.008f, 0f, 0.005f); orange = 0.2f; break;
                case 1: height = 9.3f; crownBase = 2.2f; rMax = 1.75f; shape = 0.9f; lean = new Vector3(-0.006f, 0f, 0.01f); orange = 0.35f; break;
                case 2: height = 8.9f; crownBase = 3.5f; rMax = 1.8f; shape = 0.6f; lean = new Vector3(0.012f, 0f, -0.008f); orange = 1f; break;
                default: height = 7.9f; crownBase = 1.7f; rMax = 2.0f; shape = 1.05f; lean = new Vector3(0.035f, 0f, -0.02f); orange = 0.25f; break;
            }
        }

        private static Color PineTone(int v)
        {
            switch (v & 3)
            {
                case 0: return new Color(0.95f, 1f, 0.97f);
                case 1: return new Color(1f, 1.01f, 0.9f);
                case 2: return new Color(1.05f, 1.03f, 0.86f);
                default: return new Color(0.9f, 0.97f, 1.03f);
            }
        }

        private static Color BirchTone(int v)
        {
            switch (v & 3)
            {
                case 0: return new Color(1f, 1f, 1f);
                case 1: return new Color(1.06f, 1.02f, 0.84f);
                case 2: return new Color(0.94f, 1.02f, 1.02f);
                default: return new Color(1.1f, 0.98f, 0.78f);
            }
        }

        /// <summary>Pine trunk radius at height y (root flare near the ground, tapering to the leader).</summary>
        private static float PineRadius(float y, float top)
        {
            float flare = 1f + 0.42f * Mathf.Exp(-Mathf.Max(0f, y + 0.2f) * 3f);
            return Mathf.Max(0.03f, 0.37f * flare * (1f - 0.9f * Mathf.Clamp01(y / top)));
        }

        /// <summary>Trunk with root flares and dead branch stubs. A positive <paramref name="cutAt"/> builds a stump of that height instead.</summary>
        private static void PineTrunk(MeshBuilder b, int seed, float top, float crownBase, Vector3 lean, float orange, float cutAt)
        {
            bool stump = cutAt > 0f;
            float end = stump ? cutAt : top;
            Vector3 mossDir = Flat(Rnd(seed, 7) * Mathf.PI * 2f);
            Color Shade(Vector3 p, Vector3 n)
            {
                float ao = Mathf.Lerp(0.5f, 1f, Sstep(-0.3f, 1.1f, p.y));
                float inCrown = stump ? 0f : Sstep(crownBase - 0.4f, crownBase + 1.2f, p.y);
                float moss = Mathf.Clamp01((1.2f - p.y) / 1.2f) * Mathf.Clamp01(Vector3.Dot(n, mossDir) * 1.2f + 0.15f + Fbm3(p * 3f, seed) * 0.8f);
                Color c = Color.Lerp(Color.white, new Color(1.32f, 0.98f, 0.72f), Sstep(2.2f, 5f, p.y) * orange);
                c = Color.Lerp(c, new Color(0.72f, 0.92f, 0.5f), moss * 0.75f);
                float k = (1f - inCrown * 0.35f) * (0.9f + Fbm3(p * 1.3f, seed + 5) * 0.12f) * Mathf.Lerp(0.75f, 1f, ao);
                return new Color(c.r * k, c.g * k, c.b * k, ao * (1f - inCrown * 0.35f));
            }

            // Main stem: denser rings near the ground for the flare.
            var heights = new List<float> { -0.5f, -0.2f, 0.05f, 0.3f, 0.65f, 1.2f };
            for (float y = 2f; y < end - 0.3f; y += 1.3f) heights.Add(y);
            if (stump) heights.RemoveAll(y => y >= end - 0.05f);
            heights.Add(end);
            var path = new Vector3[heights.Count + (stump ? 0 : 1)];
            var radii = new float[path.Length];
            for (int i = 0; i < heights.Count; i++)
            {
                float y = heights[i];
                float wob = stump ? 0f : 0.04f;
                path[i] = Axis(lean, y) + new Vector3(Mathf.Sin(y * 1.7f + seed) * wob, 0f, Mathf.Cos(y * 1.3f + seed) * wob);
                radii[i] = PineRadius(y, top) * (1f + (Rnd(seed, i, 9) - 0.5f) * 0.06f);
            }
            if (!stump)
            {
                path[path.Length - 1] = Axis(lean, top + 0.4f);
                radii[radii.Length - 1] = 0f;
            }
            Tube(b, path, radii, 10, (i, p, n) => Shade(p, n), false, stump);

            // Root flares radiating into the ground.
            int roots = 5 + (seed & 1);
            for (int r = 0; r < roots; r++)
            {
                float yaw = (r + Rnd(seed, r, 20) * 0.6f) / roots * Mathf.PI * 2f;
                Vector3 d = Flat(yaw);
                float reach = 0.55f + Rnd(seed, r, 21) * 0.25f;
                var rp = new[] { d * 0.1f + Vector3.up * 0.45f, d * 0.32f + Vector3.up * 0.13f, d * reach * 0.78f + Vector3.down * 0.03f, d * reach + Vector3.down * 0.2f };
                var rr = new[] { 0.18f, 0.14f, 0.08f, 0.03f };
                Tube(b, rp, rr, 6, (i, p, n) => Shade(p, n));
            }

            if (stump) return;
            // Dead branch stubs below the crown, as on real forest pines.
            int stubs = 2 + Mathf.RoundToInt(crownBase * 1.2f);
            for (int s = 0; s < stubs; s++)
            {
                float y = Mathf.Lerp(1.3f, crownBase + 0.5f, (s + Rnd(seed, s, 30)) / stubs);
                Vector3 d = Flat(Rnd(seed, s, 31) * Mathf.PI * 2f);
                float len = 0.3f + Rnd(seed, s, 32) * 0.5f;
                Vector3 a = Axis(lean, y);
                var sp = new[] { a, a + d * (0.2f + len * 0.4f) + Vector3.down * 0.03f, a + d * (0.25f + len) + Vector3.down * (0.08f + len * 0.15f) };
                Tube(b, sp, new[] { 0.045f, 0.025f, 0f }, 4, (i, p, n) => Shade(p, n) * new Color(1.1f, 1.08f, 1.05f, 1f));
            }
        }

        private static Vector3 Axis(Vector3 lean, float y) => new Vector3(lean.x * y, y, lean.z * y);

        /// <summary>Overlapping, drooping branch tiers (jagged needle skirts) up to a thin leader.</summary>
        private static void PineCrownTiers(MeshBuilder b, int seed, float height, float crownBase, float rMax, float shape, Vector3 lean, Color tone)
        {
            float top = height;
            float span = top - 0.9f - crownBase;
            int tiers = Mathf.Max(5, Mathf.RoundToInt(span / 0.62f) + 1);
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float y = crownBase + span * Mathf.Pow(t, 0.92f) + (Rnd(seed, i, 1) - 0.5f) * 0.14f;
                float radius = (rMax * Mathf.Pow(1f - t, shape) + 0.28f) * Rnd(seed, i, 2, 0.88f, 1.1f);
                int points = Mathf.Clamp(Mathf.RoundToInt(6f + 6f * radius / rMax), 6, 12);
                float rise = 0.42f + radius * 0.32f;
                float droop = (0.2f + radius * 0.24f) * Mathf.Lerp(1.15f, 0.7f, t);
                float shade = Mathf.Lerp(0.8f, 1.04f, t);
                PineSkirt(b, seed * 31 + i, Axis(lean, y), radius, rise, droop, points, tone * shade);
            }
            // Leader: a last small whorl and a thin spike.
            PineSkirt(b, seed * 31 + 99, Axis(lean, top - 0.75f), 0.3f, 0.45f, 0.12f, 5, tone * 1.05f);
            Tube(b, new[] { Axis(lean, top - 0.5f), Axis(lean, top - 0.1f), Axis(lean, top + 0.3f) }, new[] { 0.07f, 0.045f, 0f }, 5, (k, p, n) => new Color(tone.r, tone.g, tone.b, 0.9f));
        }

        /// <summary>
        /// One tier of a conifer: a pleated cone of needles from the trunk out to a jagged rim of
        /// drooping branch tips, with a darker underside back to the trunk. Normals lean toward the
        /// crown's outward direction so the crown lights as one soft volume.
        /// </summary>
        private static void PineSkirt(MeshBuilder b, int seed, Vector3 axis, float radius, float rise, float droop, int points, Color col)
        {
            int n = points * 2;
            var rim = new Vector3[n];
            var mid = new Vector3[n];
            var under = new Vector3[n];
            var lum = new float[n];
            Vector3 apex = axis + Vector3.up * rise;
            float offset = Rnd(seed, 0) * Mathf.PI * 2f;
            float tiltDir = Rnd(seed, 1) * Mathf.PI * 2f, tilt = Rnd(seed, 2) * 0.12f;
            for (int k = 0; k < n; k++)
            {
                bool tip = (k & 1) == 0 && Rnd(seed, k, 3) > 0.08f;
                float a = offset + k * Mathf.PI / points + (Rnd(seed, k, 4) - 0.5f) * 0.4f * Mathf.PI / points;
                Vector3 d = Flat(a);
                float r = tip ? radius * Rnd(seed, k, 5, 0.86f, 1.12f) : radius * Rnd(seed, k, 5, 0.6f, 0.74f);
                float y = tip ? -droop * Rnd(seed, k, 6, 0.8f, 1.25f) : -droop * 0.2f + 0.04f;
                y += Mathf.Cos(a - tiltDir) * tilt * r;
                rim[k] = axis + d * r + Vector3.up * y;
                // The upper surface bulges a little between the trunk and the rim.
                float ym = Mathf.Lerp(rise, y, 0.52f) + radius * 0.06f;
                mid[k] = axis + d * (r * 0.5f) + Vector3.up * ym;
                under[k] = axis + d * (r * 0.32f) + Vector3.up * (y * 0.75f + rise * 0.22f);
                lum[k] = tip ? Rnd(seed, k, 7, 0.95f, 1.12f) : Rnd(seed, k, 7, 0.78f, 0.9f);
            }
            Vector3 Bend(Vector3 p, Vector3 nn)
            {
                Vector3 radial = p - new Vector3(axis.x, p.y, axis.z);
                Vector3 crownN = (radial.normalized + Vector3.up * 0.75f).normalized;
                return nn.y < -0.2f ? (nn * 0.8f + crownN * 0.2f).normalized : (nn * 0.5f + crownN * 0.5f).normalized;
            }
            Color Top(float l, float ao, bool tipColor)
            {
                Color c = col * l;
                if (tipColor) c = new Color(c.r * 1.06f, c.g * 1.08f, c.b * 0.9f);
                return new Color(c.r, c.g, c.b, ao);
            }
            var underCol = new Color(col.r * 0.6f, col.g * 0.56f, col.b * 0.48f, 0.45f);
            Color apexC = Top(0.62f, 0.55f, false);
            Vector3 hub = axis + Vector3.up * (rise * 0.1f - droop * 0.2f);
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                Color m0 = Top(0.84f * lum[k], 0.75f, false), m1 = Top(0.84f * lum[k1], 0.75f, false);
                Color r0 = Top(lum[k], (k & 1) == 0 ? 1f : 0.8f, (k & 1) == 0), r1 = Top(lum[k1], (k1 & 1) == 0 ? 1f : 0.8f, (k1 & 1) == 0);
                Vector3 outTop = (rim[k] + rim[k1]) * 0.5f - axis + Vector3.up * radius;
                FlatTri(b, apex, mid[k], mid[k1], outTop, apexC, m0, m1, Bend);
                FlatTri(b, mid[k], rim[k], rim[k1], outTop, m0, r0, r1, Bend);
                FlatTri(b, mid[k], rim[k1], mid[k1], outTop, m0, r1, m1, Bend);
                Vector3 outUnder = (rim[k] + rim[k1]) * 0.5f - axis - Vector3.up * radius * 1.5f;
                FlatTri(b, rim[k], under[k], under[k1], outUnder, underCol * 1.3f, underCol, underCol, Bend);
                FlatTri(b, rim[k], under[k1], rim[k1], outUnder, underCol * 1.3f, underCol, underCol * 1.3f, Bend);
                FlatTri(b, under[k], hub, under[k1], -Vector3.up, underCol, underCol * 0.8f, underCol, Bend);
            }
        }

        private static float BirchRadius(float y, float height)
        {
            float flare = 1f + 0.6f * Mathf.Exp(-Mathf.Max(0f, y + 0.3f) * 2.4f);
            return Mathf.Max(0.025f, 0.165f * flare * (1f - 0.82f * Mathf.Clamp01(y / height)));
        }

        private static Vector3 BirchAxis(int seed, Vector3 lean, float y)
        {
            float phase = seed * 0.37f;
            return new Vector3(lean.x * y + Mathf.Sin(y * 0.8f + phase) * 0.09f * Mathf.Clamp01(y / 2f), y, lean.z * y + Mathf.Cos(y * 0.7f + phase) * 0.09f * Mathf.Clamp01(y / 2f));
        }

        /// <summary>White trunk with dark rough bark at the foot. A positive <paramref name="cutAt"/> builds a stump.</summary>
        private static void BirchTrunk(MeshBuilder b, int seed, float height, Vector3 lean, float cutAt, out float radiusAtEnd)
        {
            bool stump = cutAt > 0f;
            float end = stump ? cutAt : height - 0.3f;
            var hs = new List<float> { -0.4f, -0.1f, 0.25f, 0.7f, 1.4f };
            for (float y = 2.4f; y < end - 0.4f; y += 1.2f) hs.Add(y);
            if (stump) hs.RemoveAll(y => y >= end - 0.05f);
            hs.Add(end);
            var path = new Vector3[hs.Count + (stump ? 0 : 1)];
            var radii = new float[path.Length];
            for (int i = 0; i < hs.Count; i++)
            {
                path[i] = stump ? new Vector3(0f, hs[i], 0f) : BirchAxis(seed, lean, hs[i]);
                radii[i] = BirchRadius(hs[i], height);
            }
            if (!stump)
            {
                path[path.Length - 1] = BirchAxis(seed, lean, height + 0.2f);
                radii[radii.Length - 1] = 0f;
            }
            radiusAtEnd = BirchRadius(end, height);
            Tube(b, path, radii, 9, (i, p, n) => BirchBarkShade(seed, p, n), false, stump);

            // Low root swellings.
            for (int r = 0; r < 4; r++)
            {
                Vector3 d = Flat((r + Rnd(seed, r, 60) * 0.7f) * Mathf.PI * 0.5f);
                var rp = new[] { d * 0.06f + Vector3.up * 0.28f, d * 0.24f + Vector3.up * 0.06f, d * 0.55f + Vector3.down * 0.1f };
                Tube(b, rp, new[] { 0.09f, 0.06f, 0.015f }, 5, (i, p, n) => BirchBarkShade(seed, p, n));
            }
        }

        private static Color BirchBarkShade(int seed, Vector3 p, Vector3 n)
        {
            // Birches turn dark, rough and fissured near the ground.
            float foot = 1f - Sstep(0.15f, 0.9f + Fbm3(p * 4f, seed) * 0.35f, p.y);
            float ao = Mathf.Lerp(0.55f, 1f, Sstep(-0.3f, 0.8f, p.y));
            Color c = Color.Lerp(new Color(0.98f, 0.97f, 0.94f), new Color(0.3f, 0.28f, 0.26f), foot);
            float k = 0.94f + Fbm3(p * 2f, seed + 3) * 0.08f;
            return new Color(c.r * k, c.g * k, c.b * k, ao);
        }

        /// <summary>
        /// Limbs (into <paramref name="bark"/>) and an airy, oval crown of overlapping leaf clusters
        /// (into <paramref name="leaves"/>), each cluster fed by a limb from the trunk.
        /// </summary>
        private static void BirchCrown(MeshBuilder bark, MeshBuilder leaves, int seed, float height, Vector3 lean, Color tone)
        {
            float crownLow = 2.4f + Rnd(seed, 80) * 0.5f, crownTop = height + 0.25f;
            float midY = (crownLow + crownTop) * 0.5f;
            var env = new Vector3(1.3f + Rnd(seed, 81) * 0.3f, (crownTop - crownLow) * 0.5f, 1.25f + Rnd(seed, 82) * 0.3f);
            Vector3 center = BirchAxis(seed, lean, midY);
            // Layered leaf masses on alternating sides up the trunk, wide below and narrow on top,
            // overlapping into one oval crown; each mass has a smaller lump beside it.
            var clusters = new List<Vector4>(); // xyz centre, w radius (fed by a limb)
            var lumps = new List<Vector4>();
            int count = 7 + (seed & 1);
            float spin = Rnd(seed, 83) * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float y = Mathf.Lerp(crownLow + 0.55f, crownTop - 1.0f, t) + (Rnd(seed, i, 84) - 0.5f) * 0.3f;
                Vector3 d = Flat(spin + i * 2.4f + (Rnd(seed, i, 85) - 0.5f) * 0.6f);
                float dist = Mathf.Lerp(0.85f, 0.3f, t) * Rnd(seed, i, 86, 0.8f, 1.1f);
                float r = Mathf.Lerp(0.92f, 0.62f, t) * Rnd(seed, i, 87, 0.9f, 1.1f);
                Vector3 c = BirchAxis(seed, lean, y) + d * dist;
                clusters.Add(new Vector4(c.x, c.y, c.z, r));
                lumps.Add(new Vector4(c.x, c.y, c.z, r));
                Vector3 side = Flat(spin + i * 2.4f + (Rnd(seed, i, 88) < 0.5f ? 1.1f : -1.1f));
                Vector3 sat = c + side * (r * 0.75f) + Vector3.up * (r * Rnd(seed, i, 89, -0.1f, 0.35f));
                lumps.Add(new Vector4(sat.x, sat.y, sat.z, r * Rnd(seed, i, 90, 0.55f, 0.72f)));
            }
            Vector3 topC = BirchAxis(seed, lean, crownTop - 0.6f);
            clusters.Add(new Vector4(topC.x, topC.y, topC.z, 0.6f));
            lumps.Add(new Vector4(topC.x, topC.y, topC.z, 0.6f));

            // A limb from the trunk to every cluster away from it, rising and curving outward.
            for (int ci = 0; ci < clusters.Count; ci++)
            {
                var c = new Vector3(clusters[ci].x, clusters[ci].y, clusters[ci].z);
                Vector3 axisAtC = BirchAxis(seed, lean, c.y);
                Vector3 off = c - axisAtC;
                off.y = 0f;
                float dist = off.magnitude;
                if (dist < 0.35f) continue;
                float y0 = Mathf.Clamp(c.y - dist * Rnd(seed, ci, 86, 0.55f, 0.9f), 2.2f, height - 0.9f);
                Vector3 a = BirchAxis(seed, lean, y0);
                Vector3 m = Vector3.Lerp(a, c, 0.5f) + Vector3.down * (dist * 0.12f);
                var path = new[] { a, Vector3.Lerp(a, m, 0.5f), m, Vector3.Lerp(m, c, 0.6f), c };
                float r0 = Mathf.Lerp(0.06f, 0.03f, Mathf.InverseLerp(2.2f, height - 0.9f, y0));
                Tube(bark, path, new[] { r0, r0 * 0.8f, r0 * 0.62f, r0 * 0.45f, r0 * 0.25f }, 4, (i, p, n) =>
                {
                    // Limbs darken toward the twigs.
                    Color w = BirchBarkShade(seed, p, n);
                    float dark = i / 4f;
                    return new Color(w.r * Mathf.Lerp(1f, 0.4f, dark), w.g * Mathf.Lerp(1f, 0.36f, dark), w.b * Mathf.Lerp(1f, 0.34f, dark), w.a * 0.85f);
                }, false, true);
            }

            Vector3 crownCenter = center + Vector3.up * 0.3f;
            for (int ci = 0; ci < lumps.Count; ci++)
            {
                Vector4 cl = lumps[ci];
                var cc = new Vector3(cl.x, cl.y, cl.z);
                float r = cl.w;
                int cs = seed * 7 + ci * 13;
                Color ctone = tone * Rnd(cs, 1, 0, 0.9f, 1.06f);
                // Clusters hang a little below their limb, like the birch's weeping twigs.
                Vector3[] pts = Ellipsoid(1, cc, new Vector3(r, r * 0.84f, r), d =>
                {
                    float m = 1f + Fbm3(d * 2.6f, cs) * 0.36f;
                    return d.y < -0.4f ? m * 0.9f : m;
                }, out int[] tris);
                EmitSoftFaceted(leaves, pts, tris, 0.3f, (p, n) =>
                {
                    Vector3 crownN = (p - crownCenter).normalized;
                    return (n * 0.65f + crownN * 0.35f).normalized;
                }, (p, n) =>
                {
                    Vector3 local = (p - cc) / r;
                    Vector3 fromCenter = p - crownCenter;
                    float up = Mathf.Clamp01(local.y * 0.5f + 0.5f);
                    float outer = Mathf.Clamp01(new Vector3(fromCenter.x / env.x, fromCenter.y / env.y, fromCenter.z / env.z).magnitude * 0.85f);
                    float lit = up * 0.55f + outer * 0.45f;
                    Color c = ctone * Mathf.Lerp(0.6f, 1.1f, lit);
                    if (lit > 0.62f) c = new Color(c.r * 1.05f, c.g * 1.04f, c.b * 0.9f);
                    return new Color(c.r, c.g, c.b, Mathf.Lerp(0.42f, 1f, lit));
                });
            }
        }

        /// <summary>Cut face (growth rings, object space), a splintered hinge and wood chips on the ground.</summary>
        private static void CutTop(MeshBuilder b, int seed, float y, float radius)
        {
            var fresh = new Color(1f, 1f, 1f, 1f);
            int sides = 12;
            var rim = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                float r = radius * (0.96f + Rnd(seed, s, 80) * 0.06f);
                rim[s] = new Vector3(Mathf.Cos(a) * r, y + 0.008f + Rnd(seed, s, 81) * 0.012f, Mathf.Sin(a) * r);
            }
            var c = new Vector3(0f, y + 0.02f, 0f);
            for (int s = 0; s < sides; s++)
                FlatTri(b, c, rim[s], rim[(s + 1) % sides], Vector3.up, fresh, fresh * 0.94f, fresh * 0.94f);

            // Hinge: splinters left standing across the cut where the trunk tore off.
            float hingeYaw = Rnd(seed, 82) * Mathf.PI;
            Vector3 along = Flat(hingeYaw), across = Flat(hingeYaw + Mathf.PI * 0.5f);
            int splinters = 6;
            for (int i = 0; i < splinters; i++)
            {
                float u = ((i + 0.5f) / splinters - 0.5f) * radius * 1.6f;
                float h = 0.06f + Rnd(seed, i, 83) * 0.18f * (1f - Mathf.Abs(u) / radius);
                float w = 0.025f + Rnd(seed, i, 84) * 0.03f;
                Vector3 bpos = new Vector3(0f, y, 0f) + along * u + across * ((Rnd(seed, i, 85) - 0.5f) * 0.06f);
                Vector3 tip = bpos + Vector3.up * h + across * ((Rnd(seed, i, 86) - 0.5f) * 0.05f);
                Vector3 l = bpos - along * w, r = bpos + along * w, f = bpos + across * 0.02f, k = bpos - across * 0.02f;
                Color cc = new Color(0.97f, 0.93f, 0.86f, 1f);
                FlatTri(b, l, f, tip, f - bpos + (l - bpos) * 0.5f, cc, cc, cc);
                FlatTri(b, f, r, tip, f - bpos + (r - bpos) * 0.5f, cc, cc, cc);
                FlatTri(b, r, k, tip, k - bpos + (r - bpos) * 0.5f, cc, cc, cc);
                FlatTri(b, k, l, tip, k - bpos + (l - bpos) * 0.5f, cc, cc, cc);
            }

            // Chips scattered around.
            for (int i = 0; i < 12; i++)
            {
                float a = Rnd(seed, i, 87) * Mathf.PI * 2f;
                float d = radius + 0.2f + Rnd(seed, i, 88) * 0.9f;
                var pos = new Vector3(Mathf.Cos(a) * d, 0.012f, Mathf.Sin(a) * d);
                b.Color = new Color(0.95f, 0.9f, 0.82f, 0.8f) * Rnd(seed, i, 89, 0.85f, 1.05f);
                b.Box(pos, new Vector3(0.05f + Rnd(seed, i, 90) * 0.06f, 0.015f, 0.025f + Rnd(seed, i, 91) * 0.03f), Quaternion.Euler(Rnd(seed, i, 92) * 20f - 10f, Rnd(seed, i, 93) * 360f, Rnd(seed, i, 94) * 20f - 10f), 0.004f);
            }
            b.Color = Color.white;
        }

        // ================================================================ rocks and ore

        /// <summary>
        /// Faceted boulder sunk into the ground with moss on top (variants: rounded, slab, tall and
        /// split in two); depleted = a broken stub with fresh faces and rubble.
        /// </summary>
        public static Mesh NatureBoulder(int variant, bool depleted) => Cached("nat:boulder:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int v = variant & 3;
            int seed = 3100 + v * 29;
            var b = new MeshBuilder();
            float topCut = depleted ? 0.4f : 9f;
            switch (v)
            {
                case 0: Boulder(b, seed, new Vector3(0f, 0.2f, 0f), new Vector3(0.74f, 0.55f, 0.66f), 4, -0.22f, topCut, true, 1f, null); break;
                case 1: Boulder(b, seed, new Vector3(0f, 0.12f, 0f), new Vector3(0.82f, 0.42f, 0.7f), 6, -0.2f, topCut, true, 1f, null); break;
                case 2: Boulder(b, seed, new Vector3(0f, 0.28f, 0f), new Vector3(0.62f, 0.62f, 0.56f), 7, -0.25f, topCut, true, 1f, null); break;
                default:
                    Boulder(b, seed, new Vector3(-0.3f, 0.18f, 0.02f), new Vector3(0.48f, 0.55f, 0.56f), 6, -0.22f, topCut, true, 1f, null);
                    Boulder(b, seed + 3, new Vector3(0.36f, 0.15f, -0.05f), new Vector3(0.44f, 0.5f, 0.52f), 6, -0.22f, topCut, true, 1.04f, null);
                    break;
            }
            int pebbles = depleted ? 8 : 3;
            for (int i = 0; i < pebbles; i++)
            {
                float a = Rnd(seed, i, 10) * Mathf.PI * 2f;
                float d = 0.72f + Rnd(seed, i, 11) * 0.45f;
                float s = depleted ? 0.1f + Rnd(seed, i, 12) * 0.14f : 0.07f + Rnd(seed, i, 12) * 0.1f;
                Boulder(b, seed + 50 + i, new Vector3(Mathf.Cos(a) * d, s * 0.25f, Mathf.Sin(a) * d), new Vector3(s, s * 0.7f, s * 0.9f), 3, -0.02f, 9f, false, depleted ? 1.1f : 1f, null);
            }
            return NatureBuild(b, "boulder");
        });

        /// <summary>
        /// Iron ore outcrop: [0] angular dark rock (iron ore surface) stained rust-red along a seam,
        /// [1] ore lenses and nodules lying in the seam (null when depleted, which also lowers the rock).
        /// </summary>
        public static Mesh[] NatureOre(int variant, bool depleted) => NatureSet("nat:ore:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int v = variant & 3;
            int seed = 4100 + v * 31;
            var rock = new MeshBuilder();
            var veins = new MeshBuilder();
            var mainC = new Vector3(0f, 0.38f, 0f);
            var mainR = new Vector3(0.98f, 0.74f, 0.82f);
            const int cuts = 9;
            Vector3 seamN = new Vector3(0.3f + Rnd(seed, 30) * 0.3f, 1f, -0.3f + Rnd(seed, 31) * 0.6f).normalized;
            float seamD = 0.05f + Rnd(seed, 32) * 0.1f;
            // Rust bleeding out of the seam and running down the faces.
            Color Stain(Vector3 p, Vector3 n, Color c)
            {
                float d = Vector3.Dot(p - mainC, seamN) - seamD;
                float s = Mathf.Exp(-d * d / 0.008f) + (d < 0f ? Mathf.Exp(d * 5f) * 0.3f * Sstep(0.25f, 0.6f, Fbm3(p * 6f, seed + 7)) : 0f);
                s = Mathf.Clamp01(s) * (depleted ? 0.55f : 0.8f);
                return Color.Lerp(c, new Color(1.3f, 0.82f, 0.56f, c.a), s);
            }
            float cut = depleted ? 0.66f : 9f;
            Boulder(rock, seed, mainC, mainR, cuts, -0.3f, cut, false, 1f, Stain);
            Boulder(rock, seed + 7, new Vector3(0.74f, 0.2f, -0.44f), new Vector3(0.52f, 0.46f, 0.48f), 7, -0.25f, depleted ? 0.42f : 9f, false, 0.95f, Stain);
            Boulder(rock, seed + 9, new Vector3(-0.7f, 0.12f, 0.46f), new Vector3(0.4f, 0.32f, 0.36f), 6, -0.2f, 9f, false, 1.02f, Stain);
            int rubble = depleted ? 10 : 4;
            for (int i = 0; i < rubble; i++)
            {
                float a = Rnd(seed, i, 20) * Mathf.PI * 2f;
                float d = 1.0f + Rnd(seed, i, 21) * 0.5f;
                float s = 0.08f + Rnd(seed, i, 22) * (depleted ? 0.16f : 0.1f);
                Boulder(rock, seed + 60 + i, new Vector3(Mathf.Cos(a) * d, s * 0.2f, Mathf.Sin(a) * d * 0.85f), new Vector3(s, s * 0.7f, s * 0.85f), 4, -0.02f, 9f, false, 1.05f, depleted ? Stain : (System.Func<Vector3, Vector3, Color, Color>)null);
            }
            if (depleted) return new[] { NatureBuild(rock, "ore rock"), null };

            // Ore crust: the outcrop's own facets inside the seam band, lifted off the rock, with nuggets.
            Vector3[] mp = BoulderPoints(seed, mainC, mainR, cuts, -0.3f, 9f, out int[] mtris);
            var lifted = new Vector3[mp.Length];
            for (int k = 0; k < mp.Length; k++) lifted[k] = mp[k] + (mp[k] - mainC).normalized * (0.02f + Rnd(seed, k, 40) * 0.015f);
            var band = new List<int>();
            for (int t = 0; t < mtris.Length; t += 3)
            {
                Vector3 c = (mp[mtris[t]] + mp[mtris[t + 1]] + mp[mtris[t + 2]]) / 3f;
                float d = Vector3.Dot(c - mainC, seamN) - seamD;
                float width = 0.08f + Fbm3(c * 4f, seed + 12) * 0.06f;
                if (Mathf.Abs(d) < width && c.y > 0.06f) band.AddRange(new[] { mtris[t], mtris[t + 1], mtris[t + 2] });
            }
            EmitFaceted(veins, lifted, band.ToArray(), (p, n) =>
            {
                float kind = Fbm3(p * 5f, seed + 13);
                Color c = kind > 0.15f ? new Color(1.02f, 0.9f, 0.78f) : kind > -0.2f ? new Color(0.78f, 0.6f, 0.52f) : new Color(0.62f, 0.64f, 0.72f);
                return new Color(c.r, c.g, c.b, 0.85f);
            });
            for (int i = 0; i < 10 && band.Count > 0; i++)
            {
                int t = Mathf.FloorToInt(Rnd(seed, i, 41) * (band.Count / 3 - 1)) * 3;
                Vector3 a = lifted[band[t]], b2 = lifted[band[t + 1]], c2 = lifted[band[t + 2]];
                Vector3 at = (a + b2 + c2) / 3f;
                Vector3 nrm = Vector3.Cross(b2 - a, c2 - a).normalized;
                if (Vector3.Dot(nrm, at - mainC) < 0f) nrm = -nrm;
                float sz = 0.04f + Rnd(seed, i, 42) * 0.05f;
                Quaternion rot = Quaternion.LookRotation(Vector3.Cross(nrm, seamN).sqrMagnitude > 0.01f ? Vector3.Cross(nrm, seamN).normalized : Vector3.forward, nrm);
                Color tint = Rnd(seed, i, 43) < 0.6f ? new Color(1.05f, 0.9f, 0.76f) : new Color(0.62f, 0.64f, 0.72f);
                OrientedLump(veins, seed * 7 + i, at, rot, new Vector3(sz, sz * 0.7f, sz * 1.4f), new Color(tint.r, tint.g, tint.b, 0.9f));
            }
            return new[] { NatureBuild(rock, "ore rock"), NatureBuild(veins, "ore veins") };
        });

        /// <summary>A small faceted lump with its own orientation (ore lenses, nuggets).</summary>
        private static void OrientedLump(MeshBuilder b, int seed, Vector3 center, Quaternion rot, Vector3 radii, Color col)
        {
            Ico(1, out Vector3[] dirs, out int[] tris);
            var pts = new Vector3[dirs.Length];
            for (int k = 0; k < dirs.Length; k++)
            {
                float m = 0.82f + Rnd(seed, k, 1) * 0.3f;
                pts[k] = center + rot * Vector3.Scale(dirs[k], radii) * m;
            }
            EmitFaceted(b, pts, tris, (p, n) => col * (0.9f + Fbm3(p * 20f, seed) * 0.15f));
        }

        /// <summary>Displaced, fractured ellipsoid points of a boulder, flattened into the ground and optionally cut off on top.</summary>
        private static Vector3[] BoulderPoints(int seed, Vector3 center, Vector3 radii, int cuts, float bury, float topCut, out int[] tris)
        {
            int subdiv = radii.x > 0.3f ? 2 : 1;
            Vector3[] pts = Ellipsoid(subdiv, center, radii, d => BoulderShape(seed, cuts, d), out tris);
            for (int k = 0; k < pts.Length; k++)
            {
                if (pts[k].y < bury) pts[k].y = bury + (pts[k].y - bury) * 0.15f;
                if (pts[k].y > topCut) pts[k].y = topCut + (pts[k].y - topCut) * 0.08f;
            }
            return pts;
        }

        /// <summary>Radius multiplier of a boulder in a unit direction: low-frequency lumps plus planar fractures.</summary>
        private static float BoulderShape(int seed, int cuts, Vector3 dir)
        {
            float m = 1f + Fbm3(dir * 1.6f, seed) * 0.22f + Fbm3(dir * 4f, seed + 11) * 0.05f;
            Vector3 p = dir * m;
            for (int k = 0; k < cuts; k++)
            {
                Vector3 n = new Vector3(Rnd(seed, k, 1) * 2f - 1f, Rnd(seed, k, 2) * 1.4f - 0.3f, Rnd(seed, k, 3) * 2f - 1f).normalized;
                float d = 0.7f + Rnd(seed, k, 4) * 0.22f;
                float over = Vector3.Dot(p, n) - d;
                if (over > 0f) p -= n * over;
            }
            return p.magnitude;
        }

        /// <summary>
        /// Emits a faceted boulder. Points below <paramref name="bury"/> (relative to the ground) are
        /// flattened into the ground, points above <paramref name="topCut"/> are sliced off as a fresh
        /// face. Colours: moss and lichen on top, grime at the ground line, an optional extra stain.
        /// </summary>
        private static void Boulder(MeshBuilder b, int seed, Vector3 center, Vector3 radii, int cuts, float bury, float topCut, bool moss, float brightness,
            System.Func<Vector3, Vector3, Color, Color> stain)
        {
            Vector3[] pts = BoulderPoints(seed, center, radii, cuts, bury, topCut, out int[] tris);
            Vector3 mossDir = new Vector3(Rnd(seed, 90) - 0.5f, 1.6f, Rnd(seed, 91) - 0.5f).normalized;
            EmitFaceted(b, pts, tris, (p, n) =>
            {
                float grime = 1f - Sstep(-0.05f, 0.25f, p.y);
                float var = 1f + Fbm3(p * 2.5f, seed + 3) * 0.12f;
                Color c = new Color(var, var * 0.99f, var * 0.97f) * brightness;
                bool fresh = topCut < 5f && p.y > topCut - 0.03f;
                if (fresh) c = new Color(c.r * 1.14f, c.g * 1.12f, c.b * 1.08f);
                if (moss && !fresh)
                {
                    float m = Sstep(0.28f, 0.62f, Vector3.Dot(n, mossDir) + Fbm3(p * 3f, seed + 5) * 0.5f) * Sstep(0.05f, 0.3f, p.y);
                    c = Color.Lerp(c, new Color(0.64f, 0.8f, 0.42f) * var, m * 0.75f);
                    float lichen = Sstep(0.22f, 0.36f, Fbm3(p * 7f, seed + 9)) * (1f - m);
                    c = Color.Lerp(c, new Color(1.12f, 1.1f, 0.82f), lichen * 0.65f);
                }
                c = Color.Lerp(c, new Color(0.62f, 0.56f, 0.48f), grime * 0.7f);
                c.a = Mathf.Lerp(1f, 0.55f, grime);
                return stain != null ? stain(p, n, c) : c;
            });
        }

        // ================================================================ plants

        /// <summary>Tuft of long grass and reed blades with seed heads; depleted = cut stubble and a few fallen stems. Use double-sided.</summary>
        public static Mesh NatureFiber(int variant, bool depleted) => Cached("nat:fiber:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int seed = 5100 + (variant & 3) * 23;
            var b = new MeshBuilder();
            int blades = depleted ? 24 : 42;
            for (int i = 0; i < blades; i++)
            {
                float a = Rnd(seed, i, 1) * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Rnd(seed, i, 2)) * 0.28f;
                var basePos = new Vector3(Mathf.Cos(a) * r, -0.04f, Mathf.Sin(a) * r);
                float yaw = a + (Rnd(seed, i, 3) - 0.5f) * 1.2f;
                float lean = (0.1f + Rnd(seed, i, 4) * 0.5f) * (0.4f + r * 2f);
                float len = depleted ? 0.08f + Rnd(seed, i, 5) * 0.14f : 0.6f + Rnd(seed, i, 5) * 0.75f;
                float width = 0.028f + Rnd(seed, i, 6) * 0.026f;
                Blade(b, basePos, yaw, lean, len, width, depleted ? 0f : 0.25f + Rnd(seed, i, 7) * 0.35f, depleted);
            }
            if (!depleted)
            {
                // Seed stems standing above the leaves.
                for (int i = 0; i < 8; i++)
                {
                    float a = Rnd(seed, i, 20) * Mathf.PI * 2f;
                    float r = Rnd(seed, i, 21) * 0.18f;
                    var basePos = new Vector3(Mathf.Cos(a) * r, -0.04f, Mathf.Sin(a) * r);
                    float yaw = a;
                    float lean = 0.08f + Rnd(seed, i, 22) * 0.22f;
                    float len = 1.05f + Rnd(seed, i, 23) * 0.4f;
                    Vector3 tip = Blade(b, basePos, yaw, lean, len, 0.013f, 0.05f, false, true);
                    var dir = (tip - (basePos + Vector3.up * len * 0.7f)).normalized;
                    var head = new[] { tip - dir * 0.03f, tip + dir * 0.04f, tip + dir * 0.14f, tip + dir * 0.22f, tip + dir * 0.27f };
                    var col = new Color(1.05f, 0.72f, 0.55f, 1f);
                    Tube(b, head, new[] { 0.012f, 0.032f, 0.036f, 0.026f, 0f }, 6, (k, p, n) => col * (k == 0 ? 0.8f : 1f));
                }
            }
            else
            {
                // A few cut stems lying on the ground.
                for (int i = 0; i < 5; i++)
                {
                    float yaw = Rnd(seed, i, 30) * Mathf.PI * 2f;
                    var p0 = new Vector3(Rnd(seed, i, 31) - 0.5f, 0.01f, Rnd(seed, i, 32) - 0.5f) * 0.6f;
                    Vector3 d = Flat(yaw);
                    Vector3 side = Flat(yaw + Mathf.PI * 0.5f) * 0.012f;
                    float len = 0.4f + Rnd(seed, i, 33) * 0.4f;
                    var c0 = new Color(0.78f, 0.76f, 0.56f, 0.8f);
                    FlatTri(b, p0 - side, p0 + side, p0 + d * len + Vector3.up * 0.01f, Vector3.up, c0, c0, c0 * 1.1f);
                }
            }
            return NatureBuild(b, "fiber");
        });

        /// <summary>A curved, tapering blade (double-sided material). Returns the tip position.</summary>
        private static Vector3 Blade(MeshBuilder b, Vector3 basePos, float yaw, float lean, float len, float width, float droop, bool cut, bool stem = false)
        {
            Vector3 fwd = Flat(yaw), side = Flat(yaw + Mathf.PI * 0.5f);
            const int segs = 3;
            var l = new Vector3[segs + 1];
            var r = new Vector3[segs + 1];
            var cols = new Color[segs + 1];
            Vector3 tip = basePos;
            for (int j = 0; j <= segs; j++)
            {
                float s = j / (float)segs;
                Vector3 c = basePos + Vector3.up * (len * s) + fwd * (len * (lean * s + droop * s * s));
                c.y -= droop * len * s * s * 0.5f;
                float w = width * (cut || stem ? 1f - s * 0.3f : 1f - s * 0.85f);
                l[j] = c - side * w;
                r[j] = c + side * w;
                tip = c;
                Color dark = new Color(0.45f, 0.46f, 0.32f, 0.5f), mid = new Color(0.95f, 1f, 0.9f, 1f), dry = new Color(1.12f, 1.04f, 0.72f, 1f);
                if (stem) dry = new Color(1.0f, 0.85f, 0.62f, 1f);
                cols[j] = cut ? Color.Lerp(dark, new Color(0.8f, 0.82f, 0.6f, 0.9f), s) : (s < 0.5f ? Color.Lerp(dark, mid, s * 2f) : Color.Lerp(mid, dry, (s - 0.5f) * 2f));
            }
            Vector3 Bend(Vector3 p, Vector3 n) => (Vector3.up * 0.7f + (p - basePos).normalized * 0.2f + n * 0.25f).normalized;
            for (int j = 0; j < segs; j++)
            {
                Vector3 outward = Vector3.Cross(l[j + 1] - l[j], r[j] - l[j]);
                bool tipSeg = j == segs - 1 && !cut && !stem;
                if (tipSeg)
                {
                    FlatTri(b, l[j], r[j], (l[j + 1] + r[j + 1]) * 0.5f, outward, cols[j], cols[j], cols[j + 1], Bend);
                }
                else
                {
                    FlatTri(b, l[j], r[j], r[j + 1], outward, cols[j], cols[j], cols[j + 1], Bend);
                    FlatTri(b, l[j], r[j + 1], l[j + 1], outward, cols[j], cols[j + 1], cols[j + 1], Bend);
                }
            }
            return tip;
        }

        /// <summary>Berry bush: [0] dense leafy dome, [1] red berry clusters on its surface.</summary>
        public static Mesh[] NatureBush(int variant) => NatureSet("nat:bush:" + (variant & 3), () =>
        {
            int seed = 6100 + (variant & 3) * 19;
            var leaves = new MeshBuilder();
            var berries = new MeshBuilder();
            var lumps = new List<Vector4>
            {
                new Vector4(0f, 0.5f, 0f, 0.55f),
                new Vector4(0.05f, 0.85f, -0.05f, 0.38f)
            };
            int ring = 6;
            for (int i = 0; i < ring; i++)
            {
                float a = (i + Rnd(seed, i, 1) * 0.5f) / ring * Mathf.PI * 2f;
                float d = 0.42f + Rnd(seed, i, 2) * 0.12f;
                lumps.Add(new Vector4(Mathf.Cos(a) * d, 0.32f + Rnd(seed, i, 3) * 0.2f, Mathf.Sin(a) * d, 0.34f + Rnd(seed, i, 4) * 0.1f));
            }
            for (int i = 0; i < 2; i++)
            {
                float a = Rnd(seed, i, 5) * Mathf.PI * 2f;
                lumps.Add(new Vector4(Mathf.Cos(a) * 0.25f, 0.78f, Mathf.Sin(a) * 0.25f, 0.32f));
            }
            var bushCenter = new Vector3(0f, 0.45f, 0f);
            for (int li = 0; li < lumps.Count; li++)
            {
                Vector4 lp = lumps[li];
                var c = new Vector3(lp.x, lp.y, lp.z);
                float r = lp.w;
                int ls = seed + li * 17;
                float tone = Rnd(ls, 1, 0, 0.9f, 1.06f);
                Vector3[] pts = Ellipsoid(1, c, new Vector3(r, r * 0.85f, r), d => 1f + Fbm3(d * 2.5f, ls) * 0.3f, out int[] tris);
                for (int k = 0; k < pts.Length; k++) if (pts[k].y < -0.02f) pts[k].y = -0.02f;
                EmitSmooth(leaves, pts, tris, (p, n) => (n * 0.4f + (p - bushCenter + Vector3.up * 0.2f).normalized * 0.6f).normalized, (p, n) =>
                {
                    float up = Mathf.Clamp01((p.y - 0.05f) / 0.95f);
                    float outer = Mathf.Clamp01((p - bushCenter).magnitude / 0.8f);
                    float lit = up * 0.55f + outer * 0.45f;
                    float lum = Mathf.Lerp(0.5f, 1.08f, lit) * tone;
                    return new Color(lum, lum * 1.01f, lum * 0.95f, Mathf.Lerp(0.45f, 1f, lit));
                });
            }
            // Berry clusters sit on the outer surface of the lumps, mostly on the upper half.
            int clusters = 14;
            for (int i = 0; i < clusters; i++)
            {
                Vector4 lp = lumps[1 + (i % (lumps.Count - 1))];
                var c = new Vector3(lp.x, lp.y, lp.z);
                Vector3 outDir = (c - bushCenter);
                outDir.y = 0f;
                outDir = (outDir.sqrMagnitude < 0.01f ? Flat(i * 2.3f) : outDir.normalized);
                Vector3 dir = (outDir + Vector3.up * (Rnd(seed, i, 40) * 0.9f - 0.1f) + Flat(Rnd(seed, i, 41) * 6.28f) * 0.4f).normalized;
                Vector3 at = c + dir * lp.w * 0.98f;
                int n = 5 + Mathf.RoundToInt(Rnd(seed, i, 42) * 4f);
                for (int k = 0; k < n; k++)
                {
                    Vector3 off = new Vector3(Rnd(seed, i * 8 + k, 43) - 0.5f, Rnd(seed, i * 8 + k, 44) - 0.5f, Rnd(seed, i * 8 + k, 45) - 0.5f) * 0.13f;
                    float s = 0.03f + Rnd(seed, i * 8 + k, 46) * 0.012f;
                    float ripe = Rnd(seed, i * 8 + k, 47);
                    Color bc = ripe < 0.6f ? new Color(1f, 1f, 1f, 1f) * Rnd(seed, i * 8 + k, 48, 0.85f, 1.1f) : ripe < 0.85f ? new Color(0.55f, 0.5f, 0.85f, 1f) : new Color(1.35f, 0.9f, 0.6f, 1f);
                    Vector3[] pts = Ellipsoid(1, at + off + dir * 0.03f, Vector3.one * s, null, out int[] tris);
                    EmitSmooth(berries, pts, tris, null, (p, nn) => new Color(bc.r, bc.g, bc.b, 1f));
                }
            }
            return new[] { NatureBuild(leaves, "bush leaves"), NatureBuild(berries, "bush berries") };
        });

        /// <summary>A pile of fallen, forked branches and twigs; depleted = a few twigs.</summary>
        public static Mesh NatureBranches(int variant, bool depleted) => Cached("nat:branches:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int seed = 7100 + (variant & 3) * 11;
            var b = new MeshBuilder();
            int count = depleted ? 3 : 7;
            for (int i = 0; i < count; i++)
            {
                float yaw = (i * 0.9f + Rnd(seed, i, 1) * 0.8f) * Mathf.PI / Mathf.Max(1, count) * 2f;
                Vector3 d = Flat(yaw);
                float len = depleted ? 0.4f + Rnd(seed, i, 2) * 0.3f : 1.1f + Rnd(seed, i, 2) * 0.7f;
                float r0 = depleted ? 0.018f : 0.028f + Rnd(seed, i, 3) * 0.028f;
                float lift = depleted ? 0f : (i % 3) * 0.06f + 0.02f;
                Vector3 off = new Vector3(Rnd(seed, i, 4) - 0.5f, 0f, Rnd(seed, i, 5) - 0.5f) * 0.5f;
                Vector3 a = off - d * len * 0.5f + Vector3.up * (r0 + lift * 0.3f);
                Vector3 e = off + d * len * 0.5f + Vector3.up * (r0 * 0.5f + lift);
                Vector3 side = Flat(yaw + Mathf.PI * 0.5f);
                Vector3 m = Vector3.Lerp(a, e, 0.5f) + side * ((Rnd(seed, i, 6) - 0.5f) * 0.18f) + Vector3.up * lift * 0.6f;
                var path = new[] { a, Vector3.Lerp(a, m, 0.5f), m, Vector3.Lerp(m, e, 0.5f), e };
                Tube(b, path, new[] { r0, r0 * 0.9f, r0 * 0.75f, r0 * 0.55f, r0 * 0.2f }, 6, (k, p, n) => BranchShade(seed, p, n), true, true);
                // Forks and twigs off the main branch.
                int forks = depleted ? 1 : 2 + (i % 2);
                for (int f = 0; f < forks; f++)
                {
                    float t = 0.3f + 0.5f * (f + Rnd(seed, i * 4 + f, 7)) / forks;
                    Vector3 start = Vector3.Lerp(a, e, t) + Vector3.up * 0.01f;
                    float sgn = (f % 2 == 0) ? 1f : -1f;
                    Vector3 fd = (d * 0.8f + side * sgn * (0.5f + Rnd(seed, i * 4 + f, 8) * 0.5f) + Vector3.up * (Rnd(seed, i * 4 + f, 9) * 0.35f)).normalized;
                    float fl = len * (0.25f + Rnd(seed, i * 4 + f, 10) * 0.25f);
                    Vector3 fm = start + fd * fl * 0.5f;
                    Vector3 fe = start + fd * fl + Vector3.down * Mathf.Min(start.y, fd.y * fl * 0.5f);
                    float fr = r0 * 0.55f;
                    Vector3 fq = Vector3.Lerp(fm, fe, 0.5f) + side * sgn * fl * 0.08f;
                    Tube(b, new[] { start, fm, fq, fe }, new[] { fr, fr * 0.75f, fr * 0.55f, fr * 0.35f }, 4, (k, p, n) => BranchShade(seed, p, n), false, true);
                    if (!depleted)
                    {
                        Vector3 td = (fd + side * sgn * 0.8f + Vector3.up * 0.3f).normalized;
                        Vector3 t1 = fm + td * fl * 0.25f, t2 = t1 + (td + side * sgn * 0.5f).normalized * fl * 0.2f;
                        Tube(b, new[] { fm, t1, t2 }, new[] { fr * 0.5f, fr * 0.38f, fr * 0.28f }, 3, (k, p, n) => BranchShade(seed, p, n), false, true);
                    }
                }
            }
            return NatureBuild(b, "branches");
        });

        private static Color BranchShade(int seed, Vector3 p, Vector3 n)
        {
            float moss = Sstep(0.2f, 0.5f, Fbm3(p * 4f, seed) + n.y * 0.3f);
            float ao = Mathf.Lerp(0.55f, 1f, Sstep(0f, 0.15f, p.y + n.y * 0.03f));
            float weathered = Sstep(-0.1f, 0.4f, Fbm3(p * 1.7f, seed + 2));
            Color c = Color.Lerp(new Color(0.95f, 0.9f, 0.86f), new Color(1.3f, 1.25f, 1.18f), weathered);
            c = Color.Lerp(c, new Color(0.75f, 0.9f, 0.5f), moss * 0.55f) * (0.9f + Fbm3(p * 9f, seed + 1) * 0.12f);
            return new Color(c.r, c.g, c.b, ao);
        }

        // ================================================================ scrap

        /// <summary>
        /// Scrap heap: [0] corroded metal (sheets, pipes, beam, drum, door, box; double-sided),
        /// [1] rubber (tyre), [2] the low dirt mound it has settled into. Depleted: a few flat remnants.
        /// </summary>
        public static Mesh[] NatureScrap(int variant, bool depleted) => NatureSet("nat:scrap:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int seed = 8100 + (variant & 3) * 13;
            var metal = new MeshBuilder();
            var rubber = new MeshBuilder();
            var dirt = new MeshBuilder();
            float J(int k) => (Rnd(seed, k, 77) - 0.5f) * 0.12f;
            Color paintA = PaintRemnant(seed, 0), paintB = PaintRemnant(seed, 1);

            float mh = depleted ? 0.06f : 0.2f;
            Vector3[] mound = Ellipsoid(2, new Vector3(0f, -0.06f, 0f), new Vector3(1.0f, mh + 0.06f, 0.82f), d => 1f + Fbm3(d * 3f, seed) * 0.16f, out int[] mt);
            for (int k = 0; k < mound.Length; k++) if (mound[k].y < -0.05f) mound[k].y = -0.05f;
            EmitSmooth(dirt, mound, mt, null, (p, n) =>
            {
                float rustStain = Sstep(0.1f, 0.45f, Fbm3(p * 4f, seed + 4));
                float k = 0.9f + Fbm3(p * 9f, seed + 5) * 0.15f;
                Color c = Color.Lerp(new Color(k, k * 0.97f, k * 0.93f), new Color(1.15f, 0.82f, 0.62f), rustStain * 0.6f);
                return new Color(c.r, c.g, c.b, Mathf.Lerp(0.6f, 1f, Sstep(-0.05f, 0.12f, p.y)));
            });

            IBeam(metal, new Vector3(0.05f + J(1), depleted ? 0.07f : 0.16f, -0.3f), Quaternion.Euler(0f, 14f + J(2) * 100f, 3f), 1.75f, new Color(0.95f, 0.9f, 0.85f, 0.85f));
            Tyre(rubber, Matrix4x4.TRS(new Vector3(0.6f + J(5), depleted ? 0.08f : 0.14f, 0.45f), Quaternion.Euler(6f, 0f, 4f), Vector3.one), 0.31f, 0.19f, 0.2f, 16, false);
            if (depleted)
            {
                CorrugatedSheet(metal, seed + 1, new Vector3(-0.25f, 0.08f, 0.2f), Quaternion.Euler(2f, 35f + J(4) * 80f, 1f), 1.1f, 0.7f, 0.02f, new Color(1f, 0.96f, 0.92f, 0.85f));
                for (int i = 0; i < 4; i++)
                {
                    metal.Color = new Color(0.95f, 0.92f, 0.9f, 0.85f);
                    metal.Box(new Vector3(Rnd(seed, i, 60) - 0.5f, 0.03f, Rnd(seed, i, 61) - 0.5f) * 1.3f, new Vector3(0.18f, 0.012f, 0.1f), Quaternion.Euler(0f, Rnd(seed, i, 62) * 360f, 0f), 0.004f);
                }
                metal.Color = Color.white;
                return new[] { NatureBuild(metal, "scrap metal"), NatureBuild(rubber, "scrap rubber"), NatureBuild(dirt, "scrap dirt") };
            }

            // Oil drum on its side, the heap's anchor.
            metal.Color = Color.Lerp(paintA, new Color(1f, 0.85f, 0.7f, 1f), 0.3f);
            metal.Matrix = Matrix4x4.TRS(new Vector3(-0.45f + J(6), 0.45f, -0.02f), Quaternion.Euler(0f, 62f + J(7) * 100f, 84f), Vector3.one);
            metal.Lathe(new[]
            {
                new Vector2(0.27f, -0.44f), new Vector2(0.3f, -0.43f), new Vector2(0.29f, -0.4f), new Vector2(0.29f, -0.16f), new Vector2(0.305f, -0.15f), new Vector2(0.29f, -0.13f),
                new Vector2(0.29f, 0.13f), new Vector2(0.305f, 0.15f), new Vector2(0.29f, 0.16f), new Vector2(0.29f, 0.4f), new Vector2(0.3f, 0.43f), new Vector2(0.27f, 0.44f)
            }, 16, true);
            metal.Matrix = Matrix4x4.identity;
            // A corrugated sheet leaning on the drum, another lying across the front.
            CorrugatedSheet(metal, seed + 1, new Vector3(0.12f, 0.44f, 0.08f), Quaternion.Euler(0f, -90f + J(4) * 60f, 0f) * Quaternion.Euler(-30f, 0f, 0f), 1.2f, 0.78f, 0.06f, new Color(1f, 0.96f, 0.92f, 0.9f));
            CorrugatedSheet(metal, seed + 2, new Vector3(0.12f, 0.24f, 0.44f), Quaternion.Euler(5f, 18f + J(8) * 60f, 4f), 1.0f, 0.62f, 0.07f, new Color(0.92f, 0.86f, 0.8f, 1f));
            // An old car door propped against the back of the drum.
            PaintedPanel(metal, seed + 3, new Vector3(-0.25f, 0.4f, -0.5f), Quaternion.Euler(-58f, 172f + J(10) * 60f, 5f), new Vector2(0.95f, 0.6f), paintB);
            // A steel wheel leaning on the tyre, a battered metal box, pipes and rebar lying across.
            Tyre(metal, Matrix4x4.TRS(new Vector3(0.82f, 0.26f, -0.12f), Quaternion.Euler(0f, 30f, 68f), Vector3.one), 0.22f, 0.15f, 0.16f, 14, false, new Color(0.95f, 0.98f, 1.02f, 0.9f));
            metal.Color = new Color(0.95f, 0.98f, 1.02f, 0.85f);
            metal.Matrix = Matrix4x4.TRS(new Vector3(0.82f, 0.26f, -0.12f), Quaternion.Euler(0f, 30f, 68f), Vector3.one);
            metal.Cylinder(new Vector3(0f, -0.02f, 0f), new Vector3(0f, 0.02f, 0f), 0.16f, 0.16f, 14, true);
            metal.Matrix = Matrix4x4.identity;
            Color box = Color.Lerp(paintA, Color.white, 0.4f);
            ShadedBox(metal, new Vector3(0.42f, 0.3f, -0.38f), new Vector3(0.42f, 0.24f, 0.3f), Quaternion.Euler(6f, 25f + J(11) * 60f, 12f), 0.02f, (p, n) => new Color(box.r, box.g, box.b, Mathf.Lerp(0.7f, 1f, Sstep(0.1f, 0.4f, p.y))));
            Pipe(metal, new Vector3(-0.72f, 0.2f, 0.5f), new Vector3(0.72f, 0.33f, 0.22f), 0.05f, new Color(0.95f, 0.98f, 1.02f, 0.9f));
            Pipe(metal, new Vector3(0.86f, 0.08f, -0.56f), new Vector3(-0.3f, 0.27f, -0.62f), 0.035f, new Color(1.05f, 0.9f, 0.8f, 0.85f));
            for (int i = 0; i < 2; i++)
            {
                var a = new Vector3(-0.65f + i * 0.2f, 0.14f, -0.15f + i * 0.5f);
                var m = new Vector3(-0.1f + i * 0.25f, 0.27f + i * 0.04f, -0.05f + i * 0.3f);
                var e = new Vector3(0.35f + i * 0.2f, 0.2f, 0.1f + i * 0.25f);
                Tube(metal, Spline(new[] { a, m, e }, 3), Fill(7, 0.01f), 4, (k, p, n) => new Color(0.9f, 0.72f, 0.58f, 0.9f), true, true);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = Rnd(seed, i, 50) * Mathf.PI * 2f, d = 0.35f + Rnd(seed, i, 51) * 0.5f;
                var at = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d * 0.8f);
                at.y = Mathf.Max(0.02f, mh * Mathf.Sqrt(Mathf.Max(0f, 1f - d * d)) + 0.01f);
                metal.Color = Color.Lerp(new Color(0.95f, 0.95f, 0.98f, 0.9f), new Color(1.05f, 0.85f, 0.7f, 0.9f), Rnd(seed, i, 52));
                metal.Box(at, new Vector3(0.12f + Rnd(seed, i, 53) * 0.12f, 0.012f, 0.07f + Rnd(seed, i, 54) * 0.08f), Quaternion.Euler(Rnd(seed, i, 55) * 16f - 8f, Rnd(seed, i, 56) * 360f, Rnd(seed, i, 57) * 16f - 8f), 0.004f);
            }
            metal.Color = Color.white;
            return new[] { NatureBuild(metal, "scrap metal"), NatureBuild(rubber, "scrap rubber"), NatureBuild(dirt, "scrap dirt") };
        });

        private static Color PaintRemnant(int seed, int k)
        {
            Color[] paints = { new Color(0.62f, 0.78f, 1.1f, 1f), new Color(0.75f, 1f, 0.72f, 1f), new Color(1.25f, 0.7f, 0.6f, 1f), new Color(1.2f, 1.12f, 0.9f, 1f) };
            return paints[Mathf.FloorToInt(Rnd(seed, k, 99) * 3.99f)];
        }

        private static void IBeam(MeshBuilder b, Vector3 c, Quaternion rot, float length, Color col)
        {
            b.Color = col;
            Matrix4x4 saved = b.Matrix;
            b.Matrix = saved * Matrix4x4.TRS(c, rot, Vector3.one);
            b.Box(new Vector3(0f, -0.065f, 0f), new Vector3(length, 0.018f, 0.14f), null, 0.004f);
            b.Box(new Vector3(0f, 0.065f, 0f), new Vector3(length, 0.018f, 0.14f), null, 0.004f);
            b.Box(Vector3.zero, new Vector3(length, 0.12f, 0.014f), null, 0f);
            b.Matrix = saved;
        }

        /// <summary>A bent corrugated sheet (ridges across its width), rendered double-sided.</summary>
        private static void CorrugatedSheet(MeshBuilder b, int seed, Vector3 c, Quaternion rot, float length, float width, float bend, Color col)
        {
            const int waves = 7, perWave = 4, rows = 5;
            int cols = waves * perWave;
            var grid = new Vector3[rows + 1, cols + 1];
            Matrix4x4 m = Matrix4x4.TRS(c, rot, Vector3.one);
            for (int r = 0; r <= rows; r++)
            {
                float z = (r / (float)rows - 0.5f) * length;
                for (int k = 0; k <= cols; k++)
                {
                    float x = (k / (float)cols - 0.5f) * width;
                    float y = Mathf.Sin(k / (float)perWave * Mathf.PI * 2f) * 0.018f;
                    float zz = z / (length * 0.5f);
                    y -= bend * zz * zz;
                    y += Fbm3(new Vector3(x * 3f, 0f, z * 3f), seed) * 0.03f;
                    grid[r, k] = m.MultiplyPoint3x4(new Vector3(x, y, z));
                }
            }
            Vector3 up = rot * Vector3.up;
            for (int r = 0; r < rows; r++)
                for (int k = 0; k < cols; k++)
                {
                    float rustK = Rnd(seed, r * 64 + k / perWave, 3);
                    Color cc = Color.Lerp(col, new Color(1.08f, 0.82f, 0.62f, col.a), rustK * 0.6f);
                    FlatTri(b, grid[r, k], grid[r, k + 1], grid[r + 1, k + 1], up, cc, cc, cc);
                    FlatTri(b, grid[r, k], grid[r + 1, k + 1], grid[r + 1, k], up, cc, cc, cc);
                }
        }

        private static void PaintedPanel(MeshBuilder b, int seed, Vector3 c, Quaternion rot, Vector2 size, Color paint)
        {
            Matrix4x4 saved = b.Matrix;
            b.Matrix = saved * Matrix4x4.TRS(c, rot, Vector3.one);
            b.Color = paint;
            b.Box(new Vector3(0f, 0f, -size.y * 0.2f), new Vector3(size.x, 0.04f, size.y * 0.6f), null, 0.015f);
            // Window frame of the door.
            b.Color = Color.Lerp(paint, new Color(1f, 0.8f, 0.65f, 1f), 0.4f);
            b.Box(new Vector3(-size.x * 0.47f, 0f, size.y * 0.3f), new Vector3(0.04f, 0.03f, size.y * 0.42f), null, 0.008f);
            b.Box(new Vector3(size.x * 0.47f, 0f, size.y * 0.3f), new Vector3(0.04f, 0.03f, size.y * 0.42f), null, 0.008f);
            b.Box(new Vector3(0f, 0f, size.y * 0.5f), new Vector3(size.x, 0.03f, 0.04f), null, 0.008f);
            b.Color = new Color(1.1f, 1.12f, 1.15f, 1f);
            b.Box(new Vector3(size.x * 0.3f, 0.03f, 0f), new Vector3(0.12f, 0.025f, 0.03f), null, 0.006f);
            b.Matrix = saved;
            b.Color = Color.white;
        }

        private static void Pipe(MeshBuilder b, Vector3 a, Vector3 e, float r, Color col)
        {
            Vector3 m = Vector3.Lerp(a, e, 0.5f);
            Tube(b, new[] { a, m, e }, new[] { r, r, r }, 8, (k, p, n) => col);
        }

        /// <summary>A tyre around the local Y axis (outer radius, inner radius, width); <paramref name="flat"/> squashes the bottom.</summary>
        private static void Tyre(MeshBuilder b, Matrix4x4 m, float outer, float inner, float width, int sides, bool flat, Color? color = null)
        {
            const int around = 10;
            float rc = (outer + inner) * 0.5f, hr = (outer - inner) * 0.5f, hw = width * 0.5f;
            var pts = new Vector3[sides * around];
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < around; k++)
                {
                    float phi = k * Mathf.PI * 2f / around;
                    float cx = Mathf.Cos(phi), sy = Mathf.Sin(phi);
                    float ex = Mathf.Sign(cx) * Mathf.Pow(Mathf.Abs(cx), 0.55f), ey = Mathf.Sign(sy) * Mathf.Pow(Mathf.Abs(sy), 0.55f);
                    float rad = rc + ex * hr;
                    Vector3 p = dir * rad + Vector3.up * (ey * hw);
                    pts[s * around + k] = p;
                }
            }
            var tris = new List<int>();
            for (int s = 0; s < sides; s++)
                for (int k = 0; k < around; k++)
                {
                    int s1 = (s + 1) % sides, k1 = (k + 1) % around;
                    int i00 = s * around + k, i10 = s1 * around + k, i01 = s * around + k1, i11 = s1 * around + k1;
                    tris.Add(i00); tris.Add(i01); tris.Add(i11);
                    tris.Add(i00); tris.Add(i11); tris.Add(i10);
                }
            // Transform and optionally flatten the contact patch (in the transformed frame, world-down).
            for (int k = 0; k < pts.Length; k++)
            {
                Vector3 p = m.MultiplyPoint3x4(pts[k]);
                if (flat)
                {
                    float axleY = m.MultiplyPoint3x4(Vector3.zero).y;
                    float floor = axleY - outer * 0.74f;
                    if (p.y < floor)
                    {
                        float over = floor - p.y;
                        Vector3 axleDir = m.MultiplyVector(Vector3.up).normalized;
                        float side = Vector3.Dot(p - m.MultiplyPoint3x4(Vector3.zero), axleDir);
                        p.y = floor - over * 0.15f;
                        p += axleDir * Mathf.Sign(side) * over * 0.35f;
                    }
                }
                pts[k] = p;
            }
            int[] tri = tris.ToArray();
            // Make sure triangles face outward from the tyre's tube centre line.
            Vector3 c0 = m.MultiplyPoint3x4(Vector3.zero);
            for (int t = 0; t < tri.Length; t += 3)
            {
                Vector3 p0 = pts[tri[t]], p1 = pts[tri[t + 1]], p2 = pts[tri[t + 2]];
                Vector3 mid = (p0 + p1 + p2) / 3f;
                Vector3 axleDir = m.MultiplyVector(Vector3.up).normalized;
                Vector3 radial = mid - c0 - axleDir * Vector3.Dot(mid - c0, axleDir);
                Vector3 tubeC = c0 + radial.normalized * rc;
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), mid - tubeC) < 0f)
                {
                    int tmp = tri[t + 1];
                    tri[t + 1] = tri[t + 2];
                    tri[t + 2] = tmp;
                }
            }
            Color col = color ?? new Color(1f, 1f, 1f, 1f);
            EmitSmooth(b, pts, tri, null, (p, n) => new Color(col.r, col.g, col.b, Mathf.Lerp(0.6f, 1f, Sstep(0f, 0.3f, p.y))));
        }

        // ================================================================ car wreck

        private const int CarPaint = 0, CarRocker = 1, CarGlass = 2, CarUnder = 3, CarSkip = 4;

        private static readonly Color CarRust = new Color(0.66f, 0.38f, 0.24f, 1f);

        /// <summary>
        /// Old car wreck: a sedan (even variants) or a pickup (odd). [0] body (painted; rusted,
        /// door- and glassless and double-sided when stripped), [1] trim and running gear (rusty
        /// metal), [2] glass (null when stripped), [3] rubber (tyres, wipers, seats).
        /// </summary>
        public static Mesh[] NatureCar(int variant, bool stripped) => NatureSet("nat:car:" + (variant & 3) + (stripped ? "s" : ""), () =>
        {
            int v = variant & 3;
            bool pickup = (v & 1) == 1;
            int seed = 9100 + v * 17;
            Color[] paints = { new Color(0.66f, 0.2f, 0.15f), new Color(0.28f, 0.42f, 0.6f), new Color(0.86f, 0.78f, 0.58f), new Color(0.36f, 0.48f, 0.31f) };
            Color paint = paints[v];
            var body = new MeshBuilder();
            var trim = new MeshBuilder();
            var glass = stripped ? null : new MeshBuilder();
            var rubber = new MeshBuilder();
            float wheelF = pickup ? 1.42f : 1.35f, wheelR = pickup ? -1.5f : -1.35f;
            float zFront = 2.25f, zBack = pickup ? -0.6f : -2.25f, zTail = pickup ? -2.3f : -2.25f;

            Color Shade(int tag, Vector3 p, Vector3 n)
            {
                if (tag == CarUnder) return new Color(0.45f, 0.38f, 0.33f, 0.45f);
                if (tag == CarGlass)
                {
                    float dust = Sstep(1.15f, 0.95f, p.y);
                    return new Color(1f + dust * 0.35f, 1f + dust * 0.32f, 1f + dust * 0.22f, 1f);
                }
                float ao = Mathf.Lerp(0.6f, 1f, Sstep(0.25f, 0.65f, p.y));
                if (stripped)
                {
                    float remnant = Sstep(0.05f, 0.35f, Fbm3(p * 1.8f, seed + 4)) * Sstep(0.55f, 0.85f, p.y) * 0.75f;
                    Color pr = new Color(paint.r * 1.3f, paint.g * 1.5f, paint.b * 1.7f);
                    Color c0 = Color.Lerp(Color.white, pr, remnant) * (0.9f + Fbm3(p * 3f, seed + 5) * 0.15f);
                    return new Color(c0.r, c0.g, c0.b, ao);
                }
                float rust = Sstep(0.62f, 0.3f, p.y) * 0.5f + Sstep(0.38f, 0.7f, Fbm3(p * 2.2f, seed)) * 0.5f;
                if (n.y > 0.6f) rust += Sstep(0.5f, 0.75f, Fbm3(p * 1.4f, seed + 2)) * 0.3f;
                if (tag == CarRocker) rust += 0.35f;
                // Rust eats the wheel-arch lips first.
                foreach (float wz in new[] { wheelF, wheelR })
                {
                    float dz = p.z - wz, dy = p.y - 0.27f;
                    float ad = Mathf.Sqrt(dz * dz + dy * dy) - 0.47f;
                    if (Mathf.Abs(p.x) > 0.7f) rust += Mathf.Exp(-ad * ad / 0.004f) * 0.55f;
                }
                rust = Mathf.Clamp01(rust);
                Color c = paint * (0.92f + Fbm3(p * 4f, seed + 1) * 0.08f);
                if (n.y > 0.5f) c = Color.Lerp(c, new Color(0.86f, 0.84f, 0.8f), 0.12f * n.y);
                c = Color.Lerp(c, CarRust, rust);
                return new Color(c.r, c.g, c.b, ao);
            }

            // Stations along the length: regular steps plus every window, door and arch boundary.
            var zs = new List<float>();
            for (float z = zBack; z < zFront; z += 0.15f) zs.Add(z);
            zs.Add(zFront);
            foreach (float wz in new[] { wheelF, wheelR })
                for (int k = 0; k <= 8; k++) zs.Add(wz + 0.43f * Mathf.Cos(k * Mathf.PI / 8f));
            CarWindows(pickup, out float wsTop, out float rearTop, out float rearBase);
            zs.AddRange(new[] { 0.95f, wsTop, rearTop, rearBase, -0.2f, 0.86f, -1.06f, -0.27f, -0.13f, -1.0f, -0.52f, -0.42f, 1.95f, -1.95f });
            zs = CleanStations(zs, zBack, zFront);

            bool InRange(float z, float a, float b2) => z > a && z < b2;
            bool SideWindow(float z) => pickup ? InRange(z, -0.42f, 0.95f) : InRange(z, -1.0f, -0.27f) || InRange(z, -0.13f, 0.95f);
            bool DoorGone(float z, bool right)
            {
                if (!stripped) return false;
                if (pickup) return !right && InRange(z, -0.52f, 0.86f);
                bool frontDoor = InRange(z, -0.2f, 0.86f), rearDoor = InRange(z, -1.06f, -0.2f);
                return v == 0 ? (!right && frontDoor) || (right && rearDoor) : (!right && rearDoor) || (right && frontDoor);
            }
            int Tag(int j, float z)
            {
                if (j == 17) return CarUnder;
                if (j == 8)
                {
                    bool win = InRange(z, wsTop + 0.02f, 0.93f) || InRange(z, rearBase + 0.02f, rearTop - 0.02f);
                    return win ? (stripped ? CarSkip : CarGlass) : CarPaint;
                }
                bool right = j > 8;
                int k = right ? 16 - j : j;
                bool gone = DoorGone(z, right);
                if (k == 0) return CarRocker;
                if (k <= 4) return gone ? CarSkip : CarPaint;
                if (k == 5) return SideWindow(z) ? (stripped ? CarSkip : CarGlass) : (gone ? CarSkip : CarPaint);
                return CarPaint;
            }
            MeshBuilder Target(int tag) => tag == CarGlass ? glass : tag == CarUnder ? trim : tag == CarSkip ? null : body;

            CarLoft(zs, 18, (z, pts) => CarSection(z, pickup, wheelF, wheelR, pts), Tag, Target, Shade, true, true);

            if (pickup)
            {
                // Open bed: two side walls with the rear arch, a floor and a tailgate.
                var bedZ = new List<float>();
                for (float z = zTail; z < -0.6f; z += 0.15f) bedZ.Add(z);
                bedZ.Add(-0.6f);
                for (int k = 0; k <= 8; k++) bedZ.Add(wheelR + 0.43f * Mathf.Cos(k * Mathf.PI / 8f));
                bedZ = CleanStations(bedZ, zTail, -0.6f);
                foreach (bool right in new[] { false, true })
                {
                    CarLoft(bedZ, 4, (z, pts) =>
                    {
                        float wB = 0.9f - (z < -2.15f ? 0.03f : 0f);
                        float yb = CarBottom(z, wheelF, wheelR);
                        var l = new[] { new Vector2(-wB, yb), new Vector2(-wB, 1.0f), new Vector2(-(wB - 0.07f), 1.0f), new Vector2(-(wB - 0.07f), Mathf.Max(yb, 0.6f)) };
                        for (int k = 0; k < 4; k++) pts[k] = right ? new Vector2(-l[3 - k].x, l[3 - k].y) : l[k];
                    }, (j, z) => j == 3 ? CarUnder : CarPaint, Target, Shade, true, true);
                }
                ShadedBox(body, new Vector3(0f, 0.6f, -1.45f), new Vector3(1.68f, 0.05f, 1.72f), Quaternion.identity, 0.01f, (p, n) => Shade(CarPaint, p, n) * new Color(0.7f, 0.68f, 0.66f, 0.8f));
                if (!stripped) ShadedBox(body, new Vector3(0f, 0.8f, -2.27f), new Vector3(1.68f, 0.42f, 0.05f), Quaternion.identity, 0.012f, (p, n) => Shade(CarPaint, p, n));
                foreach (float x in new[] { -0.45f, 0.45f })
                    ShadedBox(trim, new Vector3(x, 0.42f, -1.4f), new Vector3(0.08f, 0.14f, 1.9f), Quaternion.identity, 0.01f, (p, n) => new Color(0.5f, 0.42f, 0.36f, 0.5f));
            }

            CarDetails(body, trim, glass, rubber, seed, pickup, stripped, zFront, zTail, wheelF, wheelR);

            var meshes = new Mesh[4];
            meshes[0] = NatureBuild(body, "car body");
            meshes[1] = NatureBuild(trim, "car trim");
            meshes[2] = glass != null ? NatureBuild(glass, "car glass") : null;
            meshes[3] = NatureBuild(rubber, "car rubber");
            return meshes;
        });

        private static void CarWindows(bool pickup, out float wsTop, out float rearTop, out float rearBase)
        {
            wsTop = pickup ? 0.42f : 0.3f;
            rearTop = pickup ? -0.42f : -0.75f;
            rearBase = pickup ? -0.56f : -1.18f;
        }

        private static List<float> CleanStations(List<float> zs, float min, float max)
        {
            zs.Sort();
            var clean = new List<float>();
            foreach (float z in zs)
            {
                if (z < min - 1e-4f || z > max + 1e-4f) continue;
                float zc = Mathf.Clamp(z, min, max);
                if (clean.Count == 0 || zc - clean[clean.Count - 1] > 0.025f) clean.Add(zc);
                else if (Mathf.Abs(zc - max) < 1e-4f) clean[clean.Count - 1] = zc;
            }
            return clean;
        }

        private static float CarBelt(float z, bool pickup)
        {
            if (z > 2.1f) return Mathf.Lerp(0.9f, 0.8f, (z - 2.1f) / 0.15f);
            if (z > 0.95f) return Mathf.Lerp(0.97f, 0.9f, (z - 0.95f) / 1.15f);
            if (pickup) return 1.0f;
            if (z > -1.15f) return 0.97f;
            if (z > -2.12f) return 0.99f;
            return Mathf.Lerp(0.86f, 0.99f, (z + 2.25f) / 0.13f);
        }

        private static float CarRoof(float z, bool pickup)
        {
            float belt = CarBelt(z, pickup);
            float roofY = pickup ? 1.5f : 1.41f;
            CarWindows(pickup, out float wsTop, out float rearTop, out float rearBase);
            const float wsBase = 0.95f;
            if (z >= wsBase) return belt;
            if (z > wsTop) return Mathf.Lerp(roofY, belt, (z - wsTop) / (wsBase - wsTop));
            if (z > rearTop) return roofY + 0.015f * Mathf.Sin((z - rearTop) / (wsTop - rearTop) * Mathf.PI);
            if (z > rearBase) return Mathf.Lerp(belt, roofY, (z - rearBase) / (rearTop - rearBase));
            return belt;
        }

        private static float CarBottom(float z, float wheelF, float wheelR)
        {
            float az = Mathf.Abs(z);
            float y = az > 2.0f ? Mathf.Lerp(0.3f, 0.4f, (az - 2.0f) / 0.3f) : 0.3f;
            foreach (float wz in new[] { wheelF, wheelR })
            {
                float dz = z - wz;
                if (Mathf.Abs(dz) < 0.43f) y = Mathf.Max(y, 0.27f + Mathf.Sqrt(0.43f * 0.43f - dz * dz));
            }
            return y;
        }

        /// <summary>
        /// Body cross-section at <paramref name="z"/>: 18 points from the bottom-left up the left side,
        /// over the roof and down the right side (rocker, crease, shoulder, window line, roof).
        /// </summary>
        private static void CarSection(float z, bool pickup, float wheelF, float wheelR, Vector2[] pts)
        {
            float az = Mathf.Abs(z);
            float wB = 0.9f;
            if (az > 1.95f) wB -= 0.11f * ((az - 1.95f) / 0.3f) * ((az - 1.95f) / 0.3f);
            float yb = CarBottom(z, wheelF, wheelR);
            float yt = CarBelt(z, pickup);
            float yr = Mathf.Max(yt, CarRoof(z, pickup));
            float g = yr - yt;
            const float c = 0.06f;
            float wR = Mathf.Min(0.74f, wB - c - 0.02f);
            float y1 = yb + 0.09f;
            float y3 = yt - c;
            float y2 = Mathf.Max(y1 + 0.02f, Mathf.Min(0.66f, y3 - 0.02f));
            float xIn = wB - c;
            pts[0] = new Vector2(-wB, yb);
            pts[1] = new Vector2(-wB - 0.004f, y1);
            pts[2] = new Vector2(-wB - 0.018f, y2);
            pts[3] = new Vector2(-wB, y3);
            pts[4] = new Vector2(-xIn, yt);
            pts[5] = new Vector2(-Mathf.Lerp(xIn, wR, 0.1f), yt + g * 0.08f);
            pts[6] = new Vector2(-Mathf.Lerp(xIn, wR, 0.88f), yt + g * 0.9f);
            pts[7] = new Vector2(-wR, yr);
            pts[8] = new Vector2(-(wR - 0.13f), yr + 0.012f);
            for (int k = 0; k <= 8; k++) pts[17 - k] = new Vector2(-pts[k].x, pts[k].y);
        }

        /// <summary>
        /// Lofts cross-sections along z. Each strip segment goes to the builder its tag selects (null
        /// skips it, leaving an opening); faces are oriented by the section's traversal direction.
        /// </summary>
        private static void CarLoft(List<float> zs, int count, System.Action<float, Vector2[]> section, System.Func<int, float, int> tagOf,
            System.Func<int, MeshBuilder> target, System.Func<int, Vector3, Vector3, Color> shade, bool capFront, bool capBack)
        {
            var secs = new Vector2[zs.Count][];
            for (int i = 0; i < zs.Count; i++)
            {
                secs[i] = new Vector2[count];
                section(zs[i], secs[i]);
            }
            Color Col(int tag, Vector3 p, Vector3 o) => shade(tag, p, o.normalized);
            for (int i = 0; i + 1 < zs.Count; i++)
            {
                float zm = (zs[i] + zs[i + 1]) * 0.5f;
                for (int j = 0; j < count; j++)
                {
                    int j1 = (j + 1) % count;
                    int tag = tagOf(j, zm);
                    MeshBuilder b = target(tag);
                    if (b == null) continue;
                    Vector2 dir = secs[i][j1] - secs[i][j] + secs[i + 1][j1] - secs[i + 1][j];
                    var outward = new Vector3(-dir.y, dir.x, 0f);
                    if (outward.sqrMagnitude < 1e-10f) continue;
                    var a = new Vector3(secs[i][j].x, secs[i][j].y, zs[i]);
                    var bb = new Vector3(secs[i][j1].x, secs[i][j1].y, zs[i]);
                    var c = new Vector3(secs[i + 1][j1].x, secs[i + 1][j1].y, zs[i + 1]);
                    var d = new Vector3(secs[i + 1][j].x, secs[i + 1][j].y, zs[i + 1]);
                    FlatTri(b, a, bb, c, outward, Col(tag, a, outward), Col(tag, bb, outward), Col(tag, c, outward));
                    FlatTri(b, a, c, d, outward, Col(tag, a, outward), Col(tag, c, outward), Col(tag, d, outward));
                }
            }
            void Cap(int i, float facing)
            {
                MeshBuilder b = target(CarPaint);
                Vector2 cen = Vector2.zero;
                foreach (Vector2 p in secs[i]) cen += p;
                cen /= count;
                var cp = new Vector3(cen.x, cen.y, zs[i]);
                var o = new Vector3(0f, 0f, facing);
                for (int j = 0; j < count; j++)
                {
                    int j1 = (j + 1) % count;
                    var a = new Vector3(secs[i][j].x, secs[i][j].y, zs[i]);
                    var bb = new Vector3(secs[i][j1].x, secs[i][j1].y, zs[i]);
                    FlatTri(b, cp, a, bb, o, Col(CarPaint, cp, o), Col(CarPaint, a, o), Col(CarPaint, bb, o));
                }
            }
            if (capFront) Cap(zs.Count - 1, 1f);
            if (capBack) Cap(0, -1f);
        }

        private static void CarDetails(MeshBuilder body, MeshBuilder trim, MeshBuilder glass, MeshBuilder rubber, int seed, bool pickup, bool stripped,
            float zFront, float zTail, float wheelF, float wheelR)
        {
            var chrome = new Color(1.05f, 1.08f, 1.12f, 1f);
            var rusty = new Color(1f, 0.72f, 0.52f, 0.9f);
            var dark = new Color(0.32f, 0.3f, 0.29f, 0.6f);
            Color Metal(Vector3 p, float rustBias) => Color.Lerp(chrome, rusty, Mathf.Clamp01(rustBias + Sstep(0.1f, 0.5f, Fbm3(p * 5f, seed + 8))));

            // Bumpers (the front one is gone on a stripped wreck).
            if (!stripped)
            {
                ShadedBox(trim, new Vector3(0f, 0.45f, zFront + 0.06f), new Vector3(1.78f, 0.13f, 0.11f), Quaternion.identity, 0.03f, (p, n) => Metal(p, 0.1f));
                foreach (float s in new[] { -1f, 1f })
                    ShadedBox(trim, new Vector3(s * 0.84f, 0.45f, zFront - 0.06f), new Vector3(0.1f, 0.13f, 0.26f), Quaternion.Euler(0f, s * 12f, 0f), 0.03f, (p, n) => Metal(p, 0.1f));
            }
            ShadedBox(trim, new Vector3(0f, 0.46f, zTail - 0.06f), new Vector3(1.8f, 0.13f, 0.11f), Quaternion.identity, 0.03f, (p, n) => Metal(p, stripped ? 0.6f : 0.15f));

            // Grille and headlights.
            ShadedBox(trim, new Vector3(0f, 0.62f, zFront + 0.005f), new Vector3(0.92f, 0.2f, 0.03f), Quaternion.identity, 0.006f, (p, n) => dark);
            for (int k = 0; k < 4; k++)
                ShadedBox(trim, new Vector3(0f, 0.55f + k * 0.045f, zFront + 0.025f), new Vector3(0.9f, 0.014f, 0.016f), Quaternion.identity, 0.003f, (p, n) => Metal(p, stripped ? 0.5f : 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                var hl = new Vector3(s * 0.64f, 0.63f, zFront);
                trim.Color = Metal(hl, stripped ? 0.5f : 0f);
                trim.Cylinder(hl - Vector3.forward * 0.01f, hl + Vector3.forward * 0.035f, 0.1f, 0.095f, 12, true);
                if (glass != null)
                {
                    glass.Color = new Color(2.2f, 2.2f, 2.1f, 1f);
                    glass.Cylinder(hl + Vector3.forward * 0.03f, hl + Vector3.forward * 0.045f, 0.082f, 0.07f, 12, true);
                }
                else
                {
                    rubber.Color = new Color(0.25f, 0.24f, 0.23f, 0.4f);
                    rubber.Cylinder(hl + Vector3.forward * 0.02f, hl + Vector3.forward * 0.036f, 0.082f, 0.082f, 12, true);
                }
                // Tail lights.
                ShadedBox(body, new Vector3(s * 0.62f, pickup ? 0.86f : 0.79f, zTail - 0.008f), new Vector3(0.24f, 0.13f, 0.03f), Quaternion.identity, 0.01f,
                    (p, n) => stripped ? new Color(0.5f, 0.4f, 0.35f, 1f) : new Color(0.72f, 0.1f, 0.08f, 1f));
            }
            var plate = new Color(0.95f, 0.9f, 0.62f, 1f);
            ShadedBox(body, new Vector3(0f, 0.6f, zTail - 0.012f), new Vector3(0.32f, 0.16f, 0.012f), Quaternion.identity, 0.004f, (p, n) => plate);
            if (!stripped) ShadedBox(body, new Vector3(0f, 0.45f, zFront + 0.12f), new Vector3(0.32f, 0.12f, 0.01f), Quaternion.identity, 0.004f, (p, n) => plate);

            // Door seams follow the side surface; handles sit at the doors' rear edges.
            var doorEdges = pickup ? new[] { 0.86f, -0.52f } : new[] { 0.86f, -0.2f, -1.06f };
            var handles = pickup ? new[] { -0.4f } : new[] { -0.08f, -0.94f };
            var sec = new Vector2[18];
            var seamCol = stripped ? new Color(0.4f, 0.3f, 0.25f, 0.6f) : new Color(0.25f, 0.22f, 0.2f, 0.6f);
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (float z in doorEdges)
                {
                    CarSection(z, pickup, wheelF, wheelR, sec);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector2 a = sec[k], b2 = sec[k + 1];
                        Vector2 dd = b2 - a;
                        Vector3 o = new Vector3(-dd.y, dd.x, 0f).normalized * 0.003f;
                        var outward = new Vector3(s * o.x, o.y, 0f);
                        var pa = new Vector3(s * a.x, a.y, z) + outward;
                        var pb = new Vector3(s * b2.x, b2.y, z) + outward;
                        var dz = new Vector3(0f, 0f, 0.005f);
                        FlatTri(body, pa - dz, pb - dz, pb + dz, outward, seamCol, seamCol, seamCol);
                        FlatTri(body, pa - dz, pb + dz, pa + dz, outward, seamCol, seamCol, seamCol);
                    }
                }
                foreach (float z in handles)
                    ShadedBox(trim, new Vector3(s * 0.912f, 0.86f, z), new Vector3(0.022f, 0.024f, 0.11f), Quaternion.identity, 0.006f, (p, n) => Metal(p, 0.1f));
                if (!stripped || s > 0f)
                {
                    ShadedBox(trim, new Vector3(s * 0.94f, 1.0f, 0.74f), new Vector3(0.06f, 0.03f, 0.03f), Quaternion.identity, 0.005f, (p, n) => Metal(p, 0.2f));
                    ShadedBox(trim, new Vector3(s * 0.99f, 1.05f, 0.74f), new Vector3(0.05f, 0.08f, 0.12f), Quaternion.Euler(0f, s * 8f, 0f), 0.015f, (p, n) => Metal(p, 0.15f));
                }
            }

            if (!stripped)
            {
                // Wipers resting on the windshield base, an antenna on the front fender.
                foreach (float x in new[] { -0.32f, 0.22f })
                    ShadedBox(rubber, new Vector3(x, 1.0f, 0.9f), new Vector3(0.46f, 0.014f, 0.022f), Quaternion.Euler(-35f, 0f, 4f), 0.004f, (p, n) => new Color(0.6f, 0.6f, 0.6f, 1f));
                Tube(trim, new[] { new Vector3(0.78f, 0.95f, 1.15f), new Vector3(0.79f, 1.3f, 1.08f), new Vector3(0.8f, 1.65f, 1.0f) }, new[] { 0.006f, 0.005f, 0.004f }, 4, (k, p, n) => chrome);
            }
            Tube(trim, new[] { new Vector3(0.45f, 0.33f, -1.2f), new Vector3(0.5f, 0.3f, -1.9f), new Vector3(0.52f, 0.31f, zTail - 0.02f) }, new[] { 0.028f, 0.028f, 0.028f }, 6, (k, p, n) => new Color(0.75f, 0.55f, 0.45f, 0.7f), false, true);

            // Wheels: flat tyres on rusty rims, or bare rims sunk into the dirt on a stripped wreck.
            foreach (float wz in new[] { wheelF, wheelR })
                foreach (float s in new[] { -1f, 1f })
                {
                    var hub = new Vector3(s * 0.78f, stripped ? 0.25f : 0.27f, wz);
                    Matrix4x4 m = Matrix4x4.TRS(hub, Quaternion.Euler(0f, 0f, 90f), Vector3.one);
                    if (!stripped) Tyre(rubber, m, 0.32f, 0.19f, 0.21f, 16, true, new Color(1f, 1f, 1f, 1f));
                    if (stripped)
                    {
                        // Bare steel wheel: rim barrel, dished centre and hub.
                        var steel = new Color(1f, 0.8f, 0.62f, 0.8f);
                        Tyre(trim, m, 0.25f, 0.18f, 0.18f, 16, false, steel);
                        trim.Color = steel;
                        trim.Cylinder(hub - Vector3.right * 0.02f, hub + Vector3.right * 0.02f, 0.19f, 0.19f, 14, true);
                        trim.Cylinder(hub - Vector3.right * (s * 0.02f), hub + Vector3.right * (s * 0.07f), 0.07f, 0.05f, 10, true);
                    }
                    else
                    {
                        trim.Color = Metal(hub, 0.25f);
                        trim.Cylinder(hub - Vector3.right * 0.08f, hub + Vector3.right * 0.08f, 0.2f, 0.2f, 14, true);
                        trim.Color = Metal(hub, 0.05f);
                        trim.Sphere(hub + Vector3.right * (s * 0.085f), new Vector3(0.035f, 0.13f, 0.13f), 1, true);
                    }
                }
            trim.Color = Color.white;

            if (stripped)
            {
                // Gutted interior seen through the empty openings: seat frames and a dashboard.
                var vinyl = new Color(3.0f, 2.25f, 1.8f, 0.7f);
                float[] seats = pickup ? new[] { 0.05f } : new[] { 0.1f, -0.85f };
                foreach (float z in seats)
                {
                    ShadedBox(rubber, new Vector3(0f, 0.52f, z), new Vector3(1.4f, 0.14f, 0.5f), Quaternion.identity, 0.04f, (p, n) => vinyl);
                    ShadedBox(rubber, new Vector3(0f, 0.82f, z - 0.27f), new Vector3(1.4f, 0.48f, 0.12f), Quaternion.Euler(-12f, 0f, 0f), 0.04f, (p, n) => vinyl);
                }
                ShadedBox(trim, new Vector3(0f, 0.86f, 0.8f), new Vector3(1.55f, 0.18f, 0.3f), Quaternion.identity, 0.03f, (p, n) => dark);
                Tyre(rubber, Matrix4x4.TRS(new Vector3(-0.38f, 1.0f, 0.55f), Quaternion.Euler(60f, 0f, 0f), Vector3.one), 0.19f, 0.16f, 0.03f, 12, false, new Color(1.5f, 1.4f, 1.3f, 1f));
            }
        }

        // ================================================================ fuel pump

        /// <summary>
        /// 1970s fuel pump on a small island: [0] painted body, [1] metal trim and nozzle, [2] rubber
        /// (hose, display slots), [3] concrete island. Depleted: hose cut, nozzle gone, panel open.
        /// </summary>
        public static Mesh[] NaturePump(int variant, bool depleted) => NatureSet("nat:pump:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int v = variant & 3;
            int seed = 9500 + v * 7;
            var paintB = new MeshBuilder();
            var trim = new MeshBuilder();
            var rubber = new MeshBuilder();
            var concrete = new MeshBuilder();
            Color main = (v & 1) == 0 ? new Color(0.82f, 0.2f, 0.15f) : new Color(0.3f, 0.5f, 0.38f);
            var cream = new Color(0.95f, 0.91f, 0.78f);
            Color Paint(Color c, Vector3 p)
            {
                float rust = Sstep(0.55f, 0.24f, p.y) * 0.7f + Sstep(0.35f, 0.6f, Fbm3(p * 4f, seed)) * 0.5f;
                Color r = Color.Lerp(c * (0.92f + Fbm3(p * 6f, seed + 1) * 0.08f), CarRust, Mathf.Clamp01(rust));
                return new Color(r.r, r.g, r.b, Mathf.Lerp(0.7f, 1f, Sstep(0.2f, 0.6f, p.y)));
            }
            var metal = new Color(0.95f, 0.9f, 0.85f, 0.9f);

            ShadedBox(concrete, new Vector3(0f, 0.07f, 0f), new Vector3(1.2f, 0.14f, 0.8f), Quaternion.identity, 0.03f, (p, n) =>
            {
                float stain = Sstep(0.1f, 0.4f, Fbm3(p * 3f, seed + 2)) * (n.y > 0.5f ? 1f : 0.4f);
                float k = Mathf.Lerp(1f, 0.55f, stain);
                return new Color(k, k * 0.98f, k * 0.95f, n.y > 0.5f ? 1f : 0.8f);
            });
            ShadedBox(trim, new Vector3(0f, 0.19f, 0f), new Vector3(0.74f, 0.1f, 0.54f), Quaternion.identity, 0.015f, (p, n) => new Color(0.7f, 0.55f, 0.45f, 0.7f));
            ShadedBox(paintB, new Vector3(0f, 0.8f, 0f), new Vector3(0.66f, 1.12f, 0.46f), Quaternion.identity, 0.035f, (p, n) => Paint(main, p));
            ShadedBox(paintB, new Vector3(0f, 0.5f, 0f), new Vector3(0.672f, 0.08f, 0.472f), Quaternion.identity, 0.012f, (p, n) => Paint(cream, p));
            ShadedBox(paintB, new Vector3(0f, 1.54f, 0f), new Vector3(0.7f, 0.36f, 0.5f), Quaternion.identity, 0.05f, (p, n) => Paint(cream, p));
            ShadedBox(paintB, new Vector3(0f, 1.55f, 0.248f), new Vector3(0.56f, 0.22f, 0.012f), Quaternion.identity, 0.006f, (p, n) => Paint(main, p));
            paintB.Color = cream;
            paintB.Cylinder(new Vector3(0f, 1.55f, 0.25f), new Vector3(0f, 1.55f, 0.262f), 0.075f, 0.075f, 16, true);
            paintB.Color = Paint(main, new Vector3(0f, 1.55f, 0f));
            paintB.Cylinder(new Vector3(0f, 1.55f, 0.262f), new Vector3(0f, 1.55f, 0.266f), 0.045f, 0.045f, 16, true);

            // Mechanical display: chrome bezel, cream face, two rows of number wheels.
            ShadedBox(trim, new Vector3(0f, 1.12f, 0.232f), new Vector3(0.5f, 0.34f, 0.025f), Quaternion.identity, 0.01f, (p, n) => metal);
            ShadedBox(paintB, new Vector3(0f, 1.12f, 0.246f), new Vector3(0.44f, 0.28f, 0.01f), Quaternion.identity, 0.003f, (p, n) => depleted ? new Color(0.3f, 0.28f, 0.26f, 0.7f) : cream);
            if (!depleted)
            {
                foreach (float y in new[] { 1.19f, 1.06f })
                    for (int k = 0; k < 5; k++)
                    {
                        var at = new Vector3(-0.13f + k * 0.065f, y, 0.252f);
                        ShadedBox(rubber, at, new Vector3(0.05f, 0.065f, 0.006f), Quaternion.identity, 0f, (p, n) => new Color(0.4f, 0.4f, 0.4f, 1f));
                        ShadedBox(paintB, at + new Vector3(0f, 0f, 0.004f), new Vector3(0.03f, 0.042f, 0.003f), Quaternion.identity, 0f, (p, n) => new Color(0.95f, 0.95f, 0.92f, 1f));
                    }
            }
            ShadedBox(rubber, new Vector3(0f, 0.95f, 0.235f), new Vector3(0.24f, 0.06f, 0.008f), Quaternion.identity, 0f, (p, n) => new Color(0.5f, 0.5f, 0.5f, 1f));

            // Front access panel: closed with a seam, or hanging open over a dark cavity.
            if (!depleted)
            {
                foreach (Vector4 seg in new[] { new Vector4(0f, 0.86f, 0.5f, 0.008f), new Vector4(0f, 0.3f, 0.5f, 0.008f), new Vector4(-0.25f, 0.58f, 0.008f, 0.56f), new Vector4(0.25f, 0.58f, 0.008f, 0.56f) })
                    ShadedBox(rubber, new Vector3(seg.x, seg.y, 0.231f), new Vector3(seg.z, seg.w, 0.004f), Quaternion.identity, 0f, (p, n) => new Color(0.35f, 0.33f, 0.3f, 0.8f));
            }
            else
            {
                ShadedBox(rubber, new Vector3(0f, 0.58f, 0.229f), new Vector3(0.5f, 0.56f, 0.006f), Quaternion.identity, 0f, (p, n) => new Color(0.25f, 0.24f, 0.23f, 0.35f));
                Quaternion open = Quaternion.Euler(0f, -70f, 0f);
                var hinge = new Vector3(-0.25f, 0.58f, 0.235f);
                ShadedBox(paintB, hinge + open * new Vector3(0.25f, 0f, 0.01f), new Vector3(0.5f, 0.55f, 0.018f), open, 0.006f, (p, n) => Paint(main, p));
            }

            // Side: nozzle holster, crank lever, hose outlet.
            ShadedBox(trim, new Vector3(0.35f, 1.0f, 0.05f), new Vector3(0.06f, 0.2f, 0.14f), Quaternion.identity, 0.01f, (p, n) => metal);
            ShadedBox(trim, new Vector3(0.345f, 1.24f, 0.12f), new Vector3(0.03f, 0.03f, 0.12f), Quaternion.identity, 0.005f, (p, n) => metal);
            trim.Color = metal;
            trim.Cylinder(new Vector3(0.33f, 1.3f, -0.12f), new Vector3(0.39f, 1.3f, -0.12f), 0.03f, 0.026f, 8, true);
            if (!depleted)
            {
                ShadedBox(trim, new Vector3(0.405f, 1.02f, 0.03f), new Vector3(0.04f, 0.16f, 0.05f), Quaternion.Euler(15f, 0f, 0f), 0.012f, (p, n) => metal);
                Tube(trim, new[] { new Vector3(0.405f, 1.08f, 0.06f), new Vector3(0.405f, 1.1f, 0.13f), new Vector3(0.405f, 1.05f, 0.19f) }, new[] { 0.013f, 0.012f, 0.01f }, 6, (k, p, n) => metal, false, true);
                var hose = new[]
                {
                    new Vector3(0.39f, 1.3f, -0.12f), new Vector3(0.45f, 1.24f, -0.13f), new Vector3(0.52f, 1.02f, -0.11f), new Vector3(0.56f, 0.7f, -0.06f),
                    new Vector3(0.55f, 0.42f, 0f), new Vector3(0.5f, 0.3f, 0.05f), new Vector3(0.45f, 0.42f, 0.07f), new Vector3(0.42f, 0.7f, 0.06f), new Vector3(0.405f, 0.9f, 0.03f), new Vector3(0.405f, 0.96f, 0.02f)
                };
                Vector3[] smooth = Spline(hose, 4);
                Tube(rubber, smooth, Fill(smooth.Length, 0.021f), 7, (k, p, n) => new Color(0.8f, 0.8f, 0.8f, 1f));
            }
            else
            {
                var stub = new[] { new Vector3(0.39f, 1.3f, -0.12f), new Vector3(0.44f, 1.22f, -0.13f), new Vector3(0.47f, 1.02f, -0.11f) };
                Tube(rubber, stub, Fill(stub.Length, 0.021f), 7, (k, p, n) => new Color(0.8f, 0.8f, 0.8f, 1f), false, true);
            }
            trim.Color = Color.white;
            paintB.Color = Color.white;
            return new[] { NatureBuild(paintB, "pump paint"), NatureBuild(trim, "pump trim"), NatureBuild(rubber, "pump rubber"), NatureBuild(concrete, "pump island") };
        });

        private static float[] Fill(int n, float value)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = value;
            return a;
        }

        // ================================================================ transformer

        /// <summary>
        /// Burnt-out pad transformer: [0] painted tank, radiators and conservator with soot, [1] glazed
        /// porcelain bushings and oil stain, [2] cables and the dark rupture, [3] concrete pad.
        /// Depleted: one radiator bank torn off and lying on the pad, bushings broken, cables gone.
        /// </summary>
        public static Mesh[] NatureTransformer(int variant, bool depleted) => NatureSet("nat:transformer:" + (variant & 3) + (depleted ? "d" : ""), () =>
        {
            int seed = 9700 + (variant & 3) * 5;
            var paintB = new MeshBuilder();
            var porcelain = new MeshBuilder();
            var cable = new MeshBuilder();
            var pad = new MeshBuilder();
            var burst = new Vector3(0.15f, 0.82f, 0.38f);
            Color Paint(Vector3 p)
            {
                float d = (p - burst).magnitude;
                float soot = Mathf.Exp(-d * d / 0.16f) + Mathf.Exp(-Mathf.Pow((p.x - burst.x) * 2.2f, 2f)) * Sstep(burst.y, 1.5f, p.y) * Sstep(0f, 0.3f, p.z) * 0.8f;
                soot = Mathf.Clamp01(soot * (0.8f + Fbm3(p * 5f, seed) * 0.4f));
                float rust = Sstep(0.45f, 0.18f, p.y) * 0.6f + Sstep(0.35f, 0.65f, Fbm3(p * 3f, seed + 1)) * 0.4f;
                Color c = Color.Lerp(new Color(0.95f, 1f, 0.96f) * (0.92f + Fbm3(p * 6f, seed + 2) * 0.08f), new Color(0.82f, 0.52f, 0.36f), Mathf.Clamp01(rust));
                c = Color.Lerp(c, new Color(0.09f, 0.085f, 0.08f), soot);
                return new Color(c.r, c.g, c.b, Mathf.Lerp(0.65f, 1f, Sstep(0.15f, 0.5f, p.y)));
            }

            ShadedBox(pad, new Vector3(0f, 0.075f, 0f), new Vector3(1.75f, 0.15f, 1.3f), Quaternion.identity, 0.03f, (p, n) =>
            {
                float k = 0.9f + Fbm3(p * 2f, seed + 3) * 0.1f;
                return new Color(k, k * 0.98f, k * 0.95f, n.y > 0.5f ? 1f : 0.8f);
            });
            // Oil puddle on the pad, glossy black.
            var puddle = new List<Vector3>();
            for (int k = 0; k < 14; k++)
            {
                float a = k * Mathf.PI * 2f / 14f;
                float r = 0.32f + Fbm3(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2f, seed + 4) * 0.18f;
                puddle.Add(new Vector3(0.2f + Mathf.Cos(a) * r * 1.3f, 0.153f, Mathf.Min(0.62f, 0.48f + Mathf.Sin(a) * r * 0.45f)));
            }
            var pc = new Vector3(0.2f, 0.153f, 0.48f);
            for (int k = 0; k < puddle.Count; k++)
                FlatTri(cable, pc, puddle[k], puddle[(k + 1) % puddle.Count], Vector3.up, new Color(0.45f, 0.42f, 0.4f, 1f), new Color(0.6f, 0.56f, 0.5f, 1f), new Color(0.6f, 0.56f, 0.5f, 1f));

            // Skids, tank and lid.
            foreach (float z in new[] { -0.28f, 0.28f })
                ShadedBox(paintB, new Vector3(0f, 0.2f, z), new Vector3(1.2f, 0.1f, 0.1f), Quaternion.identity, 0.01f, (p, n) => Paint(p) * new Color(0.7f, 0.7f, 0.7f, 0.8f));
            ShadedBox(paintB, new Vector3(0f, 0.775f, 0f), new Vector3(1.0f, 1.05f, 0.75f), Quaternion.identity, 0.03f, (p, n) => Paint(p));
            ShadedBox(paintB, new Vector3(0f, 1.33f, 0f), new Vector3(1.08f, 0.06f, 0.83f), Quaternion.identity, 0.015f, (p, n) => Paint(p));
            // Ruptured seam: torn petals around a black hole.
            var hole = new List<Vector3>();
            for (int k = 0; k < 9; k++)
            {
                float a = k * Mathf.PI * 2f / 9f;
                float r = 0.07f + Rnd(seed, k, 5) * 0.06f;
                hole.Add(burst + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 1.3f, 0.002f));
            }
            for (int k = 0; k < hole.Count; k++)
            {
                FlatTri(cable, burst + Vector3.forward * 0.001f, hole[k], hole[(k + 1) % hole.Count], Vector3.forward, new Color(0.05f, 0.05f, 0.05f, 0.3f), new Color(0.1f, 0.1f, 0.1f, 0.5f), new Color(0.1f, 0.1f, 0.1f, 0.5f));
                if (k % 2 == 0)
                {
                    Vector3 a = hole[k], b2 = hole[(k + 1) % hole.Count];
                    Vector3 mid = (a + b2) * 0.5f;
                    Vector3 tipP = mid + (mid - burst).normalized * 0.06f + Vector3.forward * 0.07f;
                    var sc = new Color(0.12f, 0.11f, 0.1f, 0.7f);
                    Vector3 outN = Vector3.Cross(b2 - a, tipP - a);
                    FlatTri(paintB, a, b2, tipP, outN, sc, sc, sc);
                    FlatTri(paintB, a, tipP, b2, -outN, sc, sc, sc);
                }
            }
            // Warning plate.
            ShadedBox(paintB, new Vector3(-0.28f, 1.05f, 0.378f), new Vector3(0.2f, 0.14f, 0.006f), Quaternion.identity, 0f, (p, n) => new Color(2.0f, 1.55f, 0.3f, 1f));
            ShadedBox(paintB, new Vector3(-0.28f, 1.05f, 0.382f), new Vector3(0.07f, 0.07f, 0.003f), Quaternion.Euler(0f, 0f, 45f), 0f, (p, n) => new Color(0.1f, 0.1f, 0.1f, 1f));

            // Radiator banks on both sides, with header pipes.
            foreach (float s in new[] { -1f, 1f })
            {
                if (depleted && s > 0f)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        float y = k == 0 ? 0.42f : 1.18f;
                        paintB.Color = Paint(new Vector3(s * 0.55f, y, 0f));
                        paintB.Cylinder(new Vector3(s * 0.49f, y, -0.15f), new Vector3(s * 0.58f, y, -0.15f), 0.035f, 0.035f, 8, true);
                    }
                    continue;
                }
                for (int f = 0; f < 7; f++)
                {
                    float z = -0.27f + f * 0.09f;
                    ShadedBox(paintB, new Vector3(s * 0.62f, 0.8f, z), new Vector3(0.24f, 0.82f, 0.022f), Quaternion.identity, 0.006f, (p, n) => Paint(p));
                }
                foreach (float y in new[] { 0.42f, 1.18f })
                {
                    paintB.Color = Paint(new Vector3(s * 0.55f, y, 0f));
                    paintB.Cylinder(new Vector3(s * 0.62f, y, -0.32f), new Vector3(s * 0.62f, y, 0.32f), 0.035f, 0.035f, 8, true);
                    paintB.Cylinder(new Vector3(s * 0.49f, y, -0.15f), new Vector3(s * 0.62f, y, -0.15f), 0.03f, 0.03f, 8, true);
                }
            }
            if (depleted)
            {
                // The torn-off bank lies on its side on the ground next to the pad.
                Quaternion lie = Quaternion.Euler(0f, 12f, 0f);
                var at = new Vector3(1.22f, 0f, 0.05f);
                for (int f = 0; f < 7; f++)
                    ShadedBox(paintB, at + lie * new Vector3(0f, 0.12f, -0.27f + f * 0.09f), new Vector3(0.82f, 0.24f, 0.022f), lie, 0.006f, (p, n) => Paint(p + Vector3.up * 0.5f));
                foreach (float y in new[] { 0.035f, 0.205f })
                {
                    paintB.Color = Paint(at + Vector3.up * 0.6f);
                    paintB.Cylinder(at + lie * new Vector3(-0.38f, y, -0.32f), at + lie * new Vector3(-0.38f, y, 0.32f), 0.035f, 0.035f, 8, true);
                }
            }

            // Conservator tank on brackets.
            paintB.Color = Paint(new Vector3(0f, 1.6f, -0.25f));
            paintB.Cylinder(new Vector3(-0.38f, 1.6f, -0.25f), new Vector3(0.38f, 1.6f, -0.25f), 0.15f, 0.15f, 14, true);
            foreach (float x in new[] { -0.25f, 0.25f })
                ShadedBox(paintB, new Vector3(x, 1.45f, -0.25f), new Vector3(0.05f, 0.18f, 0.12f), Quaternion.identity, 0.01f, (p, n) => Paint(p));

            // Porcelain bushings: three tall ones on the lid, three short ones on the front.
            var tall = new[]
            {
                new Vector2(0.065f, 0f), new Vector2(0.065f, 0.04f), new Vector2(0.11f, 0.06f), new Vector2(0.05f, 0.09f), new Vector2(0.1f, 0.12f), new Vector2(0.05f, 0.15f),
                new Vector2(0.09f, 0.18f), new Vector2(0.05f, 0.21f), new Vector2(0.08f, 0.24f), new Vector2(0.04f, 0.27f), new Vector2(0.04f, 0.31f)
            };
            var broken = new[] { new Vector2(0.065f, 0f), new Vector2(0.065f, 0.04f), new Vector2(0.11f, 0.06f), new Vector2(0.05f, 0.09f), new Vector2(0.075f, 0.1f) };
            for (int k = 0; k < 3; k++)
            {
                float x = -0.3f + k * 0.3f;
                bool isBroken = depleted || k == 1;
                porcelain.Color = Color.white;
                porcelain.Matrix = Matrix4x4.Translate(new Vector3(x, 1.36f, 0.1f));
                porcelain.Lathe(isBroken ? broken : tall, 10, true);
                porcelain.Matrix = Matrix4x4.identity;
                if (!isBroken)
                {
                    paintB.Color = new Color(0.75f, 0.7f, 0.65f, 1f);
                    paintB.Cylinder(new Vector3(x, 1.67f, 0.1f), new Vector3(x, 1.72f, 0.1f), 0.03f, 0.025f, 8, true);
                    // Snapped cable hanging from the terminal down to the pad.
                    // Snapped cable: up off the terminal, sagging back over the conservator to the ground.
                    float side = x < 0f ? -1f : 1f;
                    var path = Spline(new[]
                    {
                        new Vector3(x, 1.72f, 0.1f), new Vector3(x + side * 0.05f, 1.86f, 0.0f), new Vector3(x + side * 0.15f, 1.84f, -0.35f), new Vector3(x + side * 0.3f, 1.4f, -0.62f),
                        new Vector3(x + side * 0.42f, 0.6f, -0.72f), new Vector3(x + side * 0.5f, 0.17f, -0.62f), new Vector3(x + side * 0.75f, 0.16f, -0.45f)
                    }, 3);
                    Tube(cable, path, Fill(path.Length, 0.022f), 6, (i, p, n) => new Color(0.85f, 0.85f, 0.85f, 1f), false, true);
                }
            }
            for (int k = 0; k < 3; k++)
            {
                porcelain.Matrix = Matrix4x4.TRS(new Vector3(0.12f + k * 0.13f, 1.12f, 0.375f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
                porcelain.Lathe(new[] { new Vector2(0.045f, 0f), new Vector2(0.06f, 0.03f), new Vector2(0.035f, 0.05f), new Vector2(0.05f, 0.07f), new Vector2(0.03f, 0.09f), new Vector2(0.02f, 0.11f) }, 8, true);
            }
            porcelain.Matrix = Matrix4x4.identity;
            paintB.Color = Color.white;
            porcelain.Color = Color.white;
            return new[] { NatureBuild(paintB, "transformer body"), NatureBuild(porcelain, "transformer porcelain"), NatureBuild(cable, "transformer cables"), NatureBuild(pad, "transformer pad") };
        });

        // ================================================================ dropped items

        /// <summary>
        /// Items lying in the world, one fabric mesh: a tied burlap sack with a canvas bundle, or the
        /// player's lost backpack with a bedroll.
        /// </summary>
        public static Mesh NatureDrop(bool belongings) => Cached("nat:drop:" + (belongings ? "pack" : "sack"), () =>
        {
            var b = new MeshBuilder();
            int seed = belongings ? 9900 : 9950;
            if (!belongings)
            {
                Sack(b, seed);
                // A small canvas bundle tied with rope beside the sack.
                Matrix4x4 m = Matrix4x4.TRS(new Vector3(0.36f, 0.09f, 0.12f), Quaternion.Euler(0f, 30f, 90f), Vector3.one);
                b.Matrix = m;
                b.Color = new Color(1.08f, 1.04f, 0.9f, 1f);
                b.Lathe(new[] { new Vector2(0.05f, -0.2f), new Vector2(0.085f, -0.18f), new Vector2(0.095f, -0.1f), new Vector2(0.095f, 0.1f), new Vector2(0.085f, 0.18f), new Vector2(0.05f, 0.2f) }, 10, true);
                b.Matrix = Matrix4x4.identity;
                foreach (float y in new[] { -0.09f, 0.09f })
                    Tyre(b, m * Matrix4x4.Translate(new Vector3(0f, y, 0f)), 0.1f, 0.085f, 0.02f, 10, false, new Color(0.62f, 0.52f, 0.38f, 1f));
            }
            else
            {
                Backpack(b, seed);
            }
            b.Color = Color.white;
            return NatureBuild(b, belongings ? "backpack" : "sack");
        });

        private static void Sack(MeshBuilder b, int seed)
        {
            var profile = new[]
            {
                new Vector2(0.0f, 0.0f), new Vector2(0.17f, 0.01f), new Vector2(0.25f, 0.06f), new Vector2(0.28f, 0.15f), new Vector2(0.26f, 0.26f),
                new Vector2(0.2f, 0.35f), new Vector2(0.1f, 0.42f), new Vector2(0.055f, 0.46f), new Vector2(0.055f, 0.49f), new Vector2(0.09f, 0.53f),
                new Vector2(0.075f, 0.57f), new Vector2(0.0f, 0.56f)
            };
            const int sides = 14;
            int rings = profile.Length;
            var pts = new Vector3[rings * sides];
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float y = profile[r].y;
                    float rad = profile[r].x;
                    // Slumped body with folds; the gathered neck ruffles.
                    rad *= 1f + Fbm3(dir * 2f + Vector3.up * y * 4f, seed) * 0.12f + (y < 0.42f ? Mathf.Sin(a * 7f + y * 9f) * 0.03f : Mathf.Sin(a * 9f) * 0.15f);
                    Vector3 p = dir * rad + Vector3.up * y;
                    p.x += y * y * 0.35f;
                    p.z *= 0.9f;
                    pts[r * sides + s] = p;
                }
            var tris = new List<int>();
            for (int r = 0; r + 1 < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    int a = r * sides + s, c = r * sides + s1, d = (r + 1) * sides + s1, e = (r + 1) * sides + s;
                    tris.Add(a); tris.Add(e); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            int[] tri = tris.ToArray();
            // Orient outward from the sack's (sheared) axis.
            for (int t = 0; t < tri.Length; t += 3)
            {
                Vector3 p0 = pts[tri[t]], p1 = pts[tri[t + 1]], p2 = pts[tri[t + 2]];
                Vector3 mid = (p0 + p1 + p2) / 3f;
                var axisP = new Vector3(mid.y * mid.y * 0.35f, mid.y, 0f);
                Vector3 outward = mid - axisP;
                if (mid.y < 0.012f) outward = Vector3.down;
                else if (mid.y > 0.55f) outward = Vector3.up + (mid - axisP);
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) < 0f)
                {
                    int tmp = tri[t + 1];
                    tri[t + 1] = tri[t + 2];
                    tri[t + 2] = tmp;
                }
            }
            var burlap = new Color(0.9f, 0.78f, 0.6f);
            EmitSmooth(b, pts, tri, null, (p, n) =>
            {
                bool rope = p.y > 0.445f && p.y < 0.505f;
                Color c = rope ? new Color(0.6f, 0.5f, 0.36f) : burlap * (0.92f + Fbm3(p * 8f, seed + 1) * 0.1f);
                return new Color(c.r, c.g, c.b, Mathf.Lerp(0.6f, 1f, Sstep(0f, 0.12f, p.y)));
            });
            Tyre(b, Matrix4x4.Translate(new Vector3(0.475f * 0.475f * 0.35f, 0.475f, 0f)), 0.075f, 0.05f, 0.035f, 10, false, new Color(0.62f, 0.52f, 0.38f, 1f));
            Tube(b, new[] { new Vector3(0.12f, 0.47f, 0.05f), new Vector3(0.18f, 0.38f, 0.1f), new Vector3(0.2f, 0.28f, 0.12f) }, new[] { 0.012f, 0.012f, 0.008f }, 5, (k, p, n) => new Color(0.6f, 0.5f, 0.36f, 1f), false, true);
        }

        private static void Backpack(MeshBuilder b, int seed)
        {
            var canvas = new Color(0.36f, 0.52f, 0.9f);
            var strap = new Color(0.42f, 0.36f, 0.3f);
            var roll = new Color(0.62f, 0.68f, 0.45f);
            Quaternion tilt = Quaternion.Euler(-14f, 20f, 0f);
            var c = new Vector3(0f, 0.25f, 0f);
            Color Fab(Color col, Vector3 p)
            {
                Color k = col * (0.92f + Fbm3(p * 9f, seed) * 0.08f);
                return new Color(k.r, k.g, k.b, Mathf.Lerp(0.6f, 1f, Sstep(0f, 0.15f, p.y)));
            }
            Vector3 L(Vector3 local) => c + tilt * local;
            ShadedBox(b, L(Vector3.zero), new Vector3(0.36f, 0.48f, 0.22f), tilt, 0.07f, (p, n) => Fab(canvas, p));
            ShadedBox(b, L(new Vector3(0f, -0.08f, 0.13f)), new Vector3(0.28f, 0.22f, 0.08f), tilt, 0.035f, (p, n) => Fab(canvas * 0.92f, p));
            ShadedBox(b, L(new Vector3(0f, 0.22f, 0.02f)), new Vector3(0.37f, 0.07f, 0.25f), tilt * Quaternion.Euler(8f, 0f, 0f), 0.03f, (p, n) => Fab(canvas * 0.85f, p));
            foreach (float s in new[] { -1f, 1f })
            {
                ShadedBox(b, L(new Vector3(s * 0.2f, -0.06f, 0f)), new Vector3(0.06f, 0.2f, 0.15f), tilt, 0.025f, (p, n) => Fab(canvas * 0.9f, p));
                // Buckle straps down the flap, shoulder straps on the back.
                ShadedBox(b, L(new Vector3(s * 0.08f, 0.1f, 0.12f)), new Vector3(0.035f, 0.26f, 0.012f), tilt, 0.004f, (p, n) => Fab(strap, p));
                ShadedBox(b, L(new Vector3(s * 0.08f, 0.0f, 0.18f)), new Vector3(0.045f, 0.035f, 0.01f), tilt, 0.003f, (p, n) => new Color(0.9f, 0.85f, 0.7f, 1f));
                var sp = new[] { L(new Vector3(s * 0.09f, 0.2f, -0.11f)), L(new Vector3(s * 0.11f, 0.08f, -0.17f)), L(new Vector3(s * 0.12f, -0.1f, -0.16f)), L(new Vector3(s * 0.12f, -0.22f, -0.11f)) };
                Tube(b, sp, Fill(sp.Length, 0.018f), 5, (k, p, n) => Fab(strap, p));
            }
            // Bedroll strapped on top.
            Matrix4x4 rm = Matrix4x4.TRS(L(new Vector3(0f, 0.32f, 0f)), tilt * Quaternion.Euler(0f, 0f, 90f), Vector3.one);
            b.Matrix = rm;
            b.Color = Fab(roll, L(new Vector3(0f, 0.32f, 0f)));
            b.Lathe(new[] { new Vector2(0.04f, -0.24f), new Vector2(0.075f, -0.22f), new Vector2(0.08f, -0.15f), new Vector2(0.08f, 0.15f), new Vector2(0.075f, 0.22f), new Vector2(0.04f, 0.24f) }, 10, true);
            b.Matrix = Matrix4x4.identity;
            foreach (float y in new[] { -0.12f, 0.12f })
                Tyre(b, rm * Matrix4x4.Translate(new Vector3(0f, y, 0f)), 0.088f, 0.075f, 0.025f, 10, false, strap);
        }

        // ================================================================ shared helpers

        private static float Rnd(int seed, int a, int b = 0) => Art.TextureSynth.Hash01(a, b, seed);

        private static float Rnd(int seed, int a, int b, float min, float max) => min + (max - min) * Art.TextureSynth.Hash01(a, b, seed);

        private static float Sstep(float e0, float e1, float x) => Art.TextureSynth.SmoothStep(e0, e1, x);

        private static Vector3 Flat(float yaw) => new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));

        /// <summary>Smooth 3D value noise, about −1..1.</summary>
        private static float ValueNoise(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float C(int x, int y, int z) => Art.TextureSynth.Hash01(x + z * 7919, y, seed);
            float a = Mathf.Lerp(Mathf.Lerp(C(x0, y0, z0), C(x0 + 1, y0, z0), fx), Mathf.Lerp(C(x0, y0 + 1, z0), C(x0 + 1, y0 + 1, z0), fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(C(x0, y0, z0 + 1), C(x0 + 1, y0, z0 + 1), fx), Mathf.Lerp(C(x0, y0 + 1, z0 + 1), C(x0 + 1, y0 + 1, z0 + 1), fx), fy);
            return Mathf.Lerp(a, b, fz) * 2f - 1f;
        }

        private static float Fbm3(Vector3 p, int seed, int octaves = 3)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(p, seed + o * 131) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Unit icosphere directions and outward-wound triangles, cached per subdivision level.</summary>
        private static void Ico(int subdivisions, out Vector3[] dirs, out int[] tris)
        {
            if (IcoDirCache.TryGetValue(subdivisions, out dirs) && IcoTriCache.TryGetValue(subdivisions, out tris)) return;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var list = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                var next = new List<int>();
                for (int i = 0; i < list.Count; i += 3)
                {
                    int a = list[i], b = list[i + 1], c = list[i + 2];
                    int ab = IcoMid(a, b, verts, mid), bc = IcoMid(b, c, verts, mid), ca = IcoMid(c, a, verts, mid);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                list = next;
            }
            dirs = verts.ToArray();
            tris = list.ToArray();
            IcoDirCache[subdivisions] = dirs;
            IcoTriCache[subdivisions] = tris;
        }

        private static int IcoMid(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }

        /// <summary>Icosphere points scaled to an ellipsoid and displaced by a radius multiplier per direction.</summary>
        private static Vector3[] Ellipsoid(int subdivisions, Vector3 center, Vector3 radii, System.Func<Vector3, float> displace, out int[] tris)
        {
            Ico(subdivisions, out Vector3[] dirs, out tris);
            var pts = new Vector3[dirs.Length];
            for (int k = 0; k < dirs.Length; k++)
                pts[k] = center + Vector3.Scale(dirs[k], radii) * (displace != null ? displace(dirs[k]) : 1f);
            return pts;
        }

        private static Vector3[] AreaNormals(Vector3[] pts, int[] tris)
        {
            var normals = new Vector3[pts.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 fn = Vector3.Cross(pts[tris[t + 1]] - pts[tris[t]], pts[tris[t + 2]] - pts[tris[t]]);
                normals[tris[t]] += fn;
                normals[tris[t + 1]] += fn;
                normals[tris[t + 2]] += fn;
            }
            for (int k = 0; k < normals.Length; k++) normals[k] = normals[k].sqrMagnitude > 1e-12f ? normals[k].normalized : Vector3.up;
            return normals;
        }

        /// <summary>Shared-vertex surface with smooth normals (optionally bent) and a colour per vertex.</summary>
        private static void EmitSmooth(MeshBuilder b, Vector3[] pts, int[] tris, System.Func<Vector3, Vector3, Vector3> bend, System.Func<Vector3, Vector3, Color> shade)
        {
            Vector3[] normals = AreaNormals(pts, tris);
            int start = b.VertexCount;
            for (int k = 0; k < pts.Length; k++)
            {
                Vector3 n = bend != null ? bend(pts[k], normals[k]) : normals[k];
                b.Vertex(pts[k], n, shade(pts[k], normals[k]));
            }
            for (int t = 0; t < tris.Length; t += 3) b.Triangle(start + tris[t], start + tris[t + 1], start + tris[t + 2]);
        }

        /// <summary>
        /// Facets with softened normals: each corner blends its face normal with the smooth normal
        /// (<paramref name="facet"/> = weight of the face normal), then the optional bend.
        /// </summary>
        private static void EmitSoftFaceted(MeshBuilder b, Vector3[] pts, int[] tris, float facet, System.Func<Vector3, Vector3, Vector3> bend, System.Func<Vector3, Vector3, Color> shade)
        {
            Vector3[] normals = AreaNormals(pts, tris);
            var cols = new Color[pts.Length];
            for (int k = 0; k < pts.Length; k++) cols[k] = shade(pts[k], normals[k]);
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 p0 = pts[tris[t]], p1 = pts[tris[t + 1]], p2 = pts[tris[t + 2]];
                Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                if (fn.sqrMagnitude < 1e-12f) continue;
                fn.Normalize();
                int i = b.VertexCount;
                for (int c = 0; c < 3; c++)
                {
                    int idx = tris[t + c];
                    Vector3 n = (fn * facet + normals[idx] * (1f - facet)).normalized;
                    b.Vertex(pts[idx], bend != null ? bend(pts[idx], n) : n, cols[idx]);
                }
                b.Triangle(i, i + 1, i + 2);
            }
        }

        /// <summary>Flat-shaded facets; colours come from the smooth normal so they blend across facets.</summary>
        private static void EmitFaceted(MeshBuilder b, Vector3[] pts, int[] tris, System.Func<Vector3, Vector3, Color> shade)
        {
            Vector3[] normals = AreaNormals(pts, tris);
            var cols = new Color[pts.Length];
            for (int k = 0; k < pts.Length; k++) cols[k] = shade(pts[k], normals[k]);
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 p0 = pts[tris[t]], p1 = pts[tris[t + 1]], p2 = pts[tris[t + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                int i = b.Vertex(p0, n, cols[tris[t]]);
                b.Vertex(p1, n, cols[tris[t + 1]]);
                b.Vertex(p2, n, cols[tris[t + 2]]);
                b.Triangle(i, i + 1, i + 2);
            }
        }

        /// <summary>One flat triangle facing <paramref name="outward"/>, with a colour per corner and optionally bent normals.</summary>
        private static void FlatTri(MeshBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward, Color c0, Color c1, Color c2, System.Func<Vector3, Vector3, Vector3> bend = null)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0f)
            {
                Vector3 tp = p1; p1 = p2; p2 = tp;
                Color tc = c1; c1 = c2; c2 = tc;
                n = -n;
            }
            n.Normalize();
            int i = b.Vertex(p0, bend != null ? bend(p0, n) : n, c0);
            b.Vertex(p1, bend != null ? bend(p1, n) : n, c1);
            b.Vertex(p2, bend != null ? bend(p2, n) : n, c2);
            b.Triangle(i, i + 1, i + 2);
        }

        /// <summary>Catmull-Rom curve through the points, <paramref name="perSegment"/> samples per segment (hoses, cables, wire).</summary>
        private static Vector3[] Spline(Vector3[] pts, int perSegment)
        {
            var result = new List<Vector3>();
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Length - 1, i + 2)];
                for (int k = 0; k < perSegment; k++)
                {
                    float t = k / (float)perSegment, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(pts[pts.Length - 1]);
            return result.ToArray();
        }

        /// <summary>
        /// A smooth tube along a path with a radius per point (branches, roots, hoses, cables). A zero
        /// last radius closes it in a point; <paramref name="shade"/> colours each vertex from its path
        /// index, position and normal.
        /// </summary>
        private static void Tube(MeshBuilder b, Vector3[] path, float[] radii, int sides, System.Func<int, Vector3, Vector3, Color> shade, bool capStart = false, bool capEnd = false)
        {
            int n = path.Length;
            if (n < 2) return;
            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)];
                tangents[i] = d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.up;
            }
            Vector3 normal = Vector3.Cross(tangents[0], Mathf.Abs(tangents[0].y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            int start = b.VertexCount;
            var pos = new List<Vector3>();
            var ring = new int[n];
            int tip = -1;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = tangents[i];
                normal = (normal - Vector3.Dot(normal, t) * t).normalized;
                Vector3 binormal = Vector3.Cross(t, normal);
                if (i == n - 1 && radii[i] < 1e-4f)
                {
                    tip = b.Vertex(path[i], t, shade(i, path[i], t)) - start;
                    pos.Add(path[i]);
                    ring[i] = tip;
                    break;
                }
                ring[i] = pos.Count;
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    Vector3 dir = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    Vector3 p = path[i] + dir * radii[i];
                    b.Vertex(p, dir, shade(i, p, dir));
                    pos.Add(p);
                }
            }
            for (int i = 0; i + 1 < n; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    int a = ring[i] + s, c = ring[i] + s1;
                    if (i + 1 == n - 1 && tip >= 0)
                    {
                        TriOut(b, start, a, c, tip, pos, (pos[a] + pos[c]) * 0.5f - path[i]);
                        continue;
                    }
                    int d = ring[i + 1] + s1, e = ring[i + 1] + s;
                    Vector3 outward = (pos[a] + pos[c]) * 0.5f - path[i];
                    TriOut(b, start, a, c, d, pos, outward);
                    TriOut(b, start, a, d, e, pos, outward);
                }
            }
            if (capStart && radii[0] > 1e-4f)
            {
                int ci = b.Vertex(path[0], -tangents[0], shade(0, path[0], -tangents[0])) - start;
                pos.Add(path[0]);
                for (int s = 0; s < sides; s++) TriOut(b, start, ci, ring[0] + s, ring[0] + (s + 1) % sides, pos, -tangents[0]);
            }
            if (capEnd && tip < 0)
            {
                int last = n - 1;
                int ci = b.Vertex(path[last], tangents[last], shade(last, path[last], tangents[last])) - start;
                pos.Add(path[last]);
                for (int s = 0; s < sides; s++) TriOut(b, start, ci, ring[last] + s, ring[last] + (s + 1) % sides, pos, tangents[last]);
            }
        }

        private static void TriOut(MeshBuilder b, int start, int i0, int i1, int i2, List<Vector3> pos, Vector3 outward)
        {
            Vector3 nn = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
            if (nn.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(nn, outward) >= 0f) b.Triangle(start + i0, start + i1, start + i2);
            else b.Triangle(start + i0, start + i2, start + i1);
        }

        /// <summary>
        /// A box (optionally chamfered) emitted with a colour per vertex from <paramref name="shade"/>
        /// (position and face normal in mesh space): rust creeping up from the ground, soot, grime.
        /// </summary>
        private static void ShadedBox(MeshBuilder b, Vector3 center, Vector3 size, Quaternion rot, float bevel, System.Func<Vector3, Vector3, Color> shade)
        {
            Matrix4x4 m = Matrix4x4.TRS(center, rot, Vector3.one);
            Vector3 h = size * 0.5f;
            float bv = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f);
            Vector3 i = h - Vector3.one * bv;
            void Poly(params Vector3[] local)
            {
                var w = new Vector3[local.Length];
                Vector3 cen = Vector3.zero;
                for (int k = 0; k < local.Length; k++)
                {
                    w[k] = m.MultiplyPoint3x4(local[k]);
                    cen += local[k];
                }
                cen /= local.Length;
                Vector3 outward = m.MultiplyVector(cen);
                for (int k = 1; k + 1 < w.Length; k++)
                {
                    Vector3 n = Vector3.Cross(w[k] - w[0], w[k + 1] - w[0]);
                    if (n.sqrMagnitude < 1e-14f) continue;
                    Vector3 fn = (Vector3.Dot(n, outward) < 0f ? -n : n).normalized;
                    FlatTri(b, w[0], w[k], w[k + 1], outward, shade(w[0], fn), shade(w[k], fn), shade(w[k + 1], fn));
                }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var poly = new Vector3[4];
                    for (int k = 0; k < 4; k++)
                    {
                        float u = (k == 0 || k == 3) ? -1f : 1f, w2 = k < 2 ? -1f : 1f;
                        var v = new Vector3();
                        v[axis] = s * h[axis];
                        v[(axis + 1) % 3] = u * i[(axis + 1) % 3];
                        v[(axis + 2) % 3] = w2 * i[(axis + 2) % 3];
                        poly[k] = v;
                    }
                    Poly(poly);
                }
            }
            if (bv <= 0f) return;
            for (int axis = 0; axis < 3; axis++)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                for (int s1 = -1; s1 <= 1; s1 += 2)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        Vector3 E(float along, bool onA1)
                        {
                            var v = new Vector3();
                            v[axis] = along;
                            v[a1] = s1 * (onA1 ? h[a1] : i[a1]);
                            v[a2] = s2 * (onA1 ? i[a2] : h[a2]);
                            return v;
                        }
                        Poly(E(-i[axis], true), E(i[axis], true), E(i[axis], false), E(-i[axis], false));
                    }
            }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Poly(new Vector3(sx * h.x, sy * i.y, sz * i.z), new Vector3(sx * i.x, sy * h.y, sz * i.z), new Vector3(sx * i.x, sy * i.y, sz * h.z));
        }
    }
}
