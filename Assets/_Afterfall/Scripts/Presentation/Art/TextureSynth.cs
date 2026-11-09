using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Deterministic, tileable procedural texture recipes (wood, planks, bark, foliage, stone, metal...).
    /// Every recipe paints a <see cref="Canvas"/> (height, albedo multiplier, smoothness, crevice
    /// darkening) and <see cref="Canvas.Finish"/> derives the normal map and cavity term.
    /// Coordinates: u runs along texture x (horizontal on walls), v along texture y (up on walls);
    /// both are in tile units 0..1 and every noise wraps at the tile edge.
    /// </summary>
    public static partial class TextureSynth
    {
        // ---------------------------------------------------------------- noise primitives

        /// <summary>Integer hash to 0..1.</summary>
        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xffffff) / 16777216f;
            }
        }

        public static float Hash01(int x, int seed) => Hash01(x, 0x51ed27, seed);

        private static int Wrap(int i, int period)
        {
            int m = i % period;
            return m < 0 ? m + period : m;
        }

        private static float Grad(int ix, int iy, int seed, float dx, float dy)
        {
            float a = Hash01(ix, iy, seed) * 6.2831853f;
            return MathF.Cos(a) * dx + MathF.Sin(a) * dy;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Periodic gradient noise, roughly −1..1. x and y are in lattice cells; it repeats every period cells.</summary>
        public static float Perlin(float x, float y, int periodX, int periodY, int seed)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float xf = x - xi, yf = y - yi;
            int x0 = Wrap(xi, periodX), x1 = Wrap(xi + 1, periodX);
            int y0 = Wrap(yi, periodY), y1 = Wrap(yi + 1, periodY);
            float n00 = Grad(x0, y0, seed, xf, yf);
            float n10 = Grad(x1, y0, seed, xf - 1f, yf);
            float n01 = Grad(x0, y1, seed, xf, yf - 1f);
            float n11 = Grad(x1, y1, seed, xf - 1f, yf - 1f);
            float u = Fade(xf), v = Fade(yf);
            return Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), v) * 1.42f;
        }

        /// <summary>Tileable fractal noise, roughly −1..1. u, v in tile units; <paramref name="periodX"/>/<paramref name="periodY"/> cells per tile at the first octave.</summary>
        public static float Fbm(float u, float v, int periodX, int periodY, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int px = Math.Max(1, periodX), py = Math.Max(1, periodY);
            for (int o = 0; o < octaves; o++)
            {
                sum += Perlin(u * px, v * py, px, py, seed + o * 1013) * amp;
                norm += amp;
                amp *= gain;
                px *= 2;
                py *= 2;
            }
            return sum / norm;
        }

        public static float Fbm(float u, float v, int period, int octaves, int seed, float gain = 0.5f) => Fbm(u, v, period, period, octaves, seed, gain);

        /// <summary>Ridged fractal noise 0..1 (1 on the ridges): cracks, bark furrows, veins.</summary>
        public static float Ridged(float u, float v, int periodX, int periodY, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int px = Math.Max(1, periodX), py = Math.Max(1, periodY);
            for (int o = 0; o < octaves; o++)
            {
                float n = 1f - MathF.Abs(Perlin(u * px, v * py, px, py, seed + o * 733));
                sum += n * n * amp;
                norm += amp;
                amp *= 0.5f;
                px *= 2;
                py *= 2;
            }
            return sum / norm;
        }

        /// <summary>Periodic Worley noise: distances to the nearest and second-nearest feature point (in cell units) and the nearest cell's id.</summary>
        public static void Voronoi(float u, float v, int cellsX, int cellsY, int seed, out float f1, out float f2, out int cellId, float jitter = 0.9f)
        {
            float x = u * cellsX, y = v * cellsY;
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            f1 = 9f;
            f2 = 9f;
            cellId = 0;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = xi + ox, cy = yi + oy;
                    int wx = Wrap(cx, cellsX), wy = Wrap(cy, cellsY);
                    float px = cx + 0.5f + (Hash01(wx, wy, seed) - 0.5f) * jitter;
                    float py = cy + 0.5f + (Hash01(wx, wy, seed + 17) - 0.5f) * jitter;
                    float dx = px - x, dy = py - y;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d < f1)
                    {
                        f2 = f1;
                        f1 = d;
                        cellId = wy * cellsX + wx;
                    }
                    else if (d < f2) f2 = d;
                }
            }
        }

        public static void Voronoi(float u, float v, int cells, int seed, out float f1, out float f2, out int cellId) => Voronoi(u, v, cells, cells, seed, out f1, out f2, out cellId);

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static float Frac(float x) => x - MathF.Floor(x);

        // ---------------------------------------------------------------- canvas

        /// <summary>Working layers of a recipe. Albedo channels are multipliers around 1.</summary>
        public sealed class Canvas
        {
            public readonly int N;
            public readonly float[] H, R, G, B, Smooth, Crevice;

            public Canvas(int n)
            {
                N = n;
                int c = n * n;
                H = new float[c];
                R = new float[c];
                G = new float[c];
                B = new float[c];
                Smooth = new float[c];
                Crevice = new float[c];
                for (int i = 0; i < c; i++)
                {
                    R[i] = G[i] = B[i] = 1f;
                    Smooth[i] = 1f;
                    Crevice[i] = 1f;
                }
            }

            /// <summary>Calls <paramref name="paint"/> for every texel with its index and tile coordinates (texel centres).</summary>
            public void Each(Action<int, float, float> paint)
            {
                float inv = 1f / N;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                        paint(y * N + x, (x + 0.5f) * inv, (y + 0.5f) * inv);
            }

            public void Tint(int i, float r, float g, float b)
            {
                R[i] *= r;
                G[i] *= g;
                B[i] *= b;
            }

            public void Shade(int i, float k)
            {
                R[i] *= k;
                G[i] *= k;
                B[i] *= k;
            }

            /// <summary>
            /// Builds the final maps. <paramref name="bump"/> scales slopes derived from the height field;
            /// <paramref name="cavityDepth"/> darkens texels below their neighbourhood.
            /// </summary>
            public SurfaceMaps Finish(float bump, float cavityDepth = 2.5f, int cavityRadius = 3)
            {
                var maps = new SurfaceMaps(N);
                float[] blur = BoxBlur(H, N, cavityRadius);
                float k = N * 0.02f * bump;
                double lum = 0;
                for (int i = 0; i < N * N; i++) lum += 0.2126 * R[i] + 0.7152 * G[i] + 0.0722 * B[i];
                float scale = (float)(SurfaceMaps.AlbedoMean / Math.Max(1e-4, lum / (N * N)));
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        float dx = (H[y * N + (x + 1) % N] - H[y * N + (x + N - 1) % N]) * 0.5f * k;
                        float dy = (H[((y + 1) % N) * N + x] - H[((y + N - 1) % N) * N + x]) * 0.5f * k;
                        float inv = 1f / MathF.Sqrt(dx * dx + dy * dy + 1f);
                        float nx = -dx * inv, ny = -dy * inv;
                        float cav = Clamp01(1f - MathF.Max(0f, blur[i] - H[i]) * cavityDepth);
                        cav = MathF.Max(0.25f, cav * Crevice[i]);
                        int o = i * 4;
                        maps.Albedo[o] = Clamp01(R[i] * scale);
                        maps.Albedo[o + 1] = Clamp01(G[i] * scale);
                        maps.Albedo[o + 2] = Clamp01(B[i] * scale);
                        maps.Albedo[o + 3] = Clamp01(Smooth[i]);
                        maps.Detail[o] = nx * 0.5f + 0.5f;
                        maps.Detail[o + 1] = ny * 0.5f + 0.5f;
                        maps.Detail[o + 2] = cav;
                        maps.Detail[o + 3] = Clamp01(H[i]);
                    }
                }
                return maps;
            }
        }

        private static float[] BoxBlur(float[] src, int n, int r)
        {
            var tmp = new float[n * n];
            var dst = new float[n * n];
            float norm = 1f / (2 * r + 1);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += src[y * n + (x + k + n) % n];
                    tmp[y * n + x] = s * norm;
                }
            }
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += tmp[((y + k + n) % n) * n + x];
                    dst[y * n + x] = s * norm;
                }
            }
            return dst;
        }

        /// <summary>Swaps the u and v axes (horizontal boards become vertical).</summary>
        public static SurfaceMaps Transpose(SurfaceMaps m)
        {
            var t = new SurfaceMaps(m.Size);
            int n = m.Size;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int s = (x * n + y) * 4, d = (y * n + x) * 4;
                    for (int c = 0; c < 4; c++) t.Albedo[d + c] = m.Albedo[s + c];
                    t.Detail[d] = m.Detail[s + 1];
                    t.Detail[d + 1] = m.Detail[s];
                    t.Detail[d + 2] = m.Detail[s + 2];
                    t.Detail[d + 3] = m.Detail[s + 3];
                }
            }
            return t;
        }

        // ---------------------------------------------------------------- recipes

        public static SurfaceMaps Plain(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float low = Fbm(u, v, 3, 4, 11);
                float fine = Fbm(u, v, 24, 2, 12);
                c.Shade(i, 1f + low * 0.07f + fine * 0.03f);
                c.H[i] = 0.5f + fine * 0.25f + low * 0.1f;
            });
            return c.Finish(0.6f, 1f);
        }

        /// <summary>Long wood grain running along u.</summary>
        public static SurfaceMaps Wood(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float warp = Fbm(u, v, 2, 6, 3, 21) * 0.9f;
                float rings = Frac(v * 9f + warp * 1.6f);
                float band = SmoothStep(0.0f, 0.35f, rings) * (1f - SmoothStep(0.55f, 1f, rings));
                float streak = Fbm(u, v, 3, 48, 2, 22);
                float tone = 0.86f + band * 0.16f + streak * 0.08f;
                c.Tint(i, tone, tone * (0.97f + band * 0.03f), tone * (0.93f + band * 0.06f));
                c.H[i] = 0.5f + streak * 0.3f + band * 0.15f;
                c.Smooth[i] = 0.85f + band * 0.15f;
            });
            return c.Finish(0.7f, 1.5f);
        }

        /// <summary>Floor/wall boards running along u, with staggered joints, gaps, grain, knots and nails.</summary>
        public static SurfaceMaps Planks(int n)
        {
            const int boards = 8;
            var c = new Canvas(n);
            float gap = 1.4f / n * boards;
            c.Each((i, u, v) =>
            {
                float fv = v * boards;
                int row = (int)MathF.Floor(fv);
                float ly = fv - row;
                int segs = Hash01(row, 5) < 0.5f ? 1 : 2;
                float su = Frac(u + Hash01(row, 6)) * segs;
                int seg = (int)MathF.Floor(su);
                float lx = su - seg;
                float edgeY = MathF.Min(ly, 1f - ly);
                float edgeX = MathF.Min(lx, 1f - lx) / segs * boards;
                float edge = MathF.Min(edgeY, edgeX);
                float plank = SmoothStep(gap * 0.5f, gap * 2.2f, edge);
                float tone = 0.78f + Hash01(row * 7 + seg, 7) * 0.26f;
                float warp = Fbm(u, v, 3, 12, 3, 31 + row) * 0.6f;
                float grain = 0.5f + 0.5f * MathF.Sin((ly * 7f + warp * 2.2f + Hash01(row, 8) * 6f) * 6.2831853f);
                float streak = Fbm(u, v, 4, 64, 2, 33);
                float knotD = Knot(u, v, row, seg, segs, boards);
                float knot = 1f - SmoothStep(0.0f, 0.045f, knotD);
                float lum = tone * (0.9f + grain * 0.1f + streak * 0.06f) * (1f - knot * 0.45f);
                c.Tint(i, lum, lum * (0.965f + grain * 0.02f), lum * (0.92f + grain * 0.04f));
                float nail = NailDot(lx, ly, segs, boards, n);
                c.Shade(i, 1f - nail * 0.55f);
                c.H[i] = plank * (0.75f + streak * 0.08f + grain * 0.04f) - knot * 0.05f + nail * 0.05f;
                c.Crevice[i] = Lerp(0.45f, 1f, plank);
                c.Smooth[i] = 0.75f + plank * 0.2f;
                if (plank < 0.5f) c.Shade(i, Lerp(0.45f, 1f, plank * 2f));
            });
            return c.Finish(1.2f, 2f, 2);
        }

        private static float Knot(float u, float v, int row, int seg, int segs, int boards)
        {
            if (Hash01(row * 13 + seg, 9) > 0.35f) return 1f;
            float ku = (seg + 0.2f + Hash01(row * 13 + seg, 10) * 0.6f) / segs - Hash01(row, 6);
            float kv = (row + 0.3f + Hash01(row * 13 + seg, 11) * 0.4f) / boards;
            float du = Frac(u - ku + 0.5f) - 0.5f, dv = v - kv;
            return MathF.Sqrt(du * du * 0.25f + dv * dv);
        }

        private static float NailDot(float lx, float ly, int segs, int boards, int n)
        {
            float px = lx * n / segs, py = ly * n / boards;
            float w = (float)n / segs, h = (float)n / boards;
            float best = 9f;
            foreach (float ex in new[] { 3.5f, w - 3.5f })
                foreach (float ey in new[] { h * 0.3f, h * 0.7f })
                    best = MathF.Min(best, MathF.Sqrt((px - ex) * (px - ex) + (py - ey) * (py - ey)));
            return 1f - SmoothStep(0.6f, 1.4f, best);
        }

        /// <summary>Painted clapboard siding: overlapping horizontal boards with shadow lines and weathering.</summary>
        public static SurfaceMaps Siding(int n)
        {
            const int boards = 10;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float fv = v * boards;
                int row = (int)MathF.Floor(fv);
                float ly = fv - row;
                float weather = Fbm(u, v, 8, 1, 3, 41);
                float drip = MathF.Max(0f, Fbm(u, v, 24, 2, 2, 42));
                float grain = Fbm(u, v, 2, 40, 2, 43);
                float peel = SmoothStep(0.42f, 0.5f, Fbm(u, v, 6, 4, 0 + 4, 44));
                float tone = 0.95f + weather * 0.06f - drip * 0.12f + grain * 0.03f;
                c.Shade(i, tone);
                if (peel > 0f) c.Tint(i, 1f - peel * 0.32f, 1f - peel * 0.34f, 1f - peel * 0.38f);
                float lip = 1f - SmoothStep(0f, 0.09f, ly);
                c.H[i] = 0.35f + (1f - ly) * 0.55f - lip * 0.35f + grain * 0.02f - peel * 0.04f;
                c.Crevice[i] = 1f - lip * 0.45f;
                c.Shade(i, 1f - lip * 0.25f);
                c.Smooth[i] = 0.9f - peel * 0.5f - drip * 0.2f;
            });
            return c.Finish(1.1f, 1.5f, 2);
        }

        /// <summary>Vertical bark furrows.</summary>
        public static SurfaceMaps Bark(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float warp = Fbm(u, v, 4, 2, 3, 51) * 0.08f;
                float ridge = Ridged(u + warp, v, 10, 2, 4, 52);
                float plates = Fbm(u, v, 6, 3, 3, 53);
                float h = MathF.Pow(ridge, 1.6f) * 0.8f + plates * 0.15f;
                float lichen = SmoothStep(0.25f, 0.5f, Fbm(u, v, 3, 3, 3, 54));
                float lum = 0.62f + h * 0.55f;
                c.Tint(i, lum * (1f - lichen * 0.15f), lum * (1f + lichen * 0.12f), lum * (1f - lichen * 0.2f));
                c.H[i] = h;
                c.Crevice[i] = Lerp(0.5f, 1f, SmoothStep(0.05f, 0.45f, h));
                c.Smooth[i] = 0.6f + h * 0.4f;
            });
            return c.Finish(1.6f, 2.5f, 3);
        }

        /// <summary>Clumps of broad leaves with hue variation and dark gaps (deciduous crowns, bushes, crops).</summary>
        public static SurfaceMaps Foliage(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                Voronoi(u, v, 9, 61, out float f1, out float f2, out int id);
                float clump = SmoothStep(0f, 0.35f, f2 - f1);
                float dome = 1f - MathF.Min(1f, f1 * 1.15f);
                Voronoi(u, v, 46, 62, out float l1, out float l2, out int leaf);
                float leafEdge = SmoothStep(0f, 0.18f, l2 - l1);
                float leafDome = 1f - MathF.Min(1f, l1 * 1.3f);
                float hue = Hash01(id, 63) - 0.5f;
                float leafTone = 0.85f + Hash01(leaf, 64) * 0.3f;
                float lum = leafTone * (0.72f + dome * 0.38f) * Lerp(0.55f, 1f, leafEdge);
                c.Tint(i, lum * (1f + hue * 0.22f), lum * (1f + hue * 0.05f), lum * (1f - hue * 0.25f));
                c.H[i] = dome * 0.55f + leafDome * 0.3f * leafEdge + clump * 0.1f;
                c.Crevice[i] = Lerp(0.45f, 1f, clump * leafEdge);
                c.Smooth[i] = 0.7f + leafDome * 0.3f;
            });
            return c.Finish(1.3f, 2.2f, 2);
        }

        /// <summary>Tufts of pine needles: oriented fine strokes inside clumps.</summary>
        public static SurfaceMaps Needles(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                Voronoi(u, v, 7, 71, out float f1, out float f2, out int id);
                float a = Hash01(id, 72) * 1.2f - 0.6f;
                float s = MathF.Sin(a), co = MathF.Cos(a);
                float along = u * s + v * co;
                float across = u * co - v * s;
                float strands = 0.5f + 0.5f * MathF.Sin(across * 6.2831853f * 70f + Fbm(u, v, 16, 2, 73) * 4f);
                float breakup = Fbm(along * 3f, across, 12, 3, 74);
                float dome = 1f - MathF.Min(1f, f1 * 1.1f);
                float gap = SmoothStep(0f, 0.3f, f2 - f1);
                float lum = (0.7f + strands * 0.35f + breakup * 0.12f) * (0.7f + dome * 0.4f) * Lerp(0.5f, 1f, gap);
                float hue = Hash01(id, 75) - 0.5f;
                c.Tint(i, lum * (1f + hue * 0.15f), lum, lum * (1f - hue * 0.12f));
                c.H[i] = dome * 0.5f + strands * 0.25f + breakup * 0.1f;
                c.Crevice[i] = Lerp(0.5f, 1f, gap);
                c.Smooth[i] = 0.6f + strands * 0.4f;
            });
            return c.Finish(1.2f, 2f, 2);
        }

        /// <summary>Natural rock: layered fractal relief, cracks, speckles and lichen.</summary>
        public static SurfaceMaps Rock(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float big = Fbm(u, v, 3, 5, 81);
                float strata = MathF.Sin((v * 7f + Fbm(u, v, 2, 3, 82) * 0.8f) * 6.2831853f) * 0.5f + 0.5f;
                float crack = SmoothStep(0.86f, 0.97f, Ridged(u, v, 4, 4, 3, 83));
                float speck = Hash01((int)(u * n * 0.5f), (int)(v * n * 0.5f), 84);
                float lichen = SmoothStep(0.3f, 0.55f, Fbm(u, v, 4, 3, 85)) * 0.8f;
                float lum = 0.86f + big * 0.18f + strata * 0.05f + (speck - 0.5f) * 0.1f - crack * 0.35f;
                c.Tint(i, lum * (1f + lichen * 0.08f), lum * (1f + lichen * 0.16f), lum * (1f - lichen * 0.12f));
                c.H[i] = 0.5f + big * 0.45f + strata * 0.06f - crack * 0.4f + (speck - 0.5f) * 0.03f;
                c.Crevice[i] = 1f - crack * 0.5f;
                c.Smooth[i] = 0.7f + speck * 0.3f;
            });
            return c.Finish(1.4f, 2.5f, 3);
        }

        /// <summary>Fieldstone masonry: rounded stones of varied tone set in mortar.</summary>
        public static SurfaceMaps StoneBlocks(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                Voronoi(u, v, 8, 5, 91, out float f1, out float f2, out int id, 0.75f);
                float edge = f2 - f1;
                float stone = SmoothStep(0.05f, 0.16f, edge);
                float dome = MathF.Sqrt(Clamp01(edge * 2.2f));
                float surf = Fbm(u, v, 12, 3, 92);
                float tone = 0.78f + Hash01(id, 93) * 0.3f;
                float warm = Hash01(id, 94) - 0.5f;
                float lum = stone > 0.5f ? tone * (0.95f + surf * 0.1f) : 1.05f + surf * 0.06f;
                c.Tint(i, lum * (1f + warm * 0.06f), lum, lum * (1f - warm * 0.07f));
                c.H[i] = stone * (0.55f + dome * 0.35f + surf * 0.08f) + (1f - stone) * (0.15f + surf * 0.05f);
                c.Crevice[i] = Lerp(0.55f, 1f, stone);
                c.Smooth[i] = 0.6f + stone * 0.4f;
            });
            return c.Finish(1.3f, 2.5f, 3);
        }

        /// <summary>Cast concrete: blotches, fine aggregate, pits, hairline cracks and rain streaks.</summary>
        public static SurfaceMaps Concrete(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float blotch = Fbm(u, v, 3, 4, 101);
                float speck = Hash01((int)(u * n), (int)(v * n), 102) - 0.5f;
                Voronoi(u, v, 40, 103, out float p1, out _, out int pid);
                float pit = Hash01(pid, 104) < 0.12f ? 1f - SmoothStep(0.08f, 0.2f, p1) : 0f;
                float crack = SmoothStep(0.93f, 0.985f, Ridged(u, v, 3, 3, 3, 105)) * SmoothStep(0.1f, 0.3f, Fbm(u, v, 2, 2, 106));
                float streak = MathF.Max(0f, Fbm(u, v, 18, 1, 2, 107)) * SmoothStep(-0.1f, 0.4f, Fbm(u, v, 2, 2, 108));
                float lum = 0.93f + blotch * 0.09f + speck * 0.06f - pit * 0.2f - crack * 0.3f - streak * 0.16f;
                c.Shade(i, lum);
                c.H[i] = 0.6f + blotch * 0.05f + speck * 0.04f - pit * 0.35f - crack * 0.3f;
                c.Crevice[i] = 1f - crack * 0.4f - pit * 0.3f;
                c.Smooth[i] = 0.85f - streak * 0.3f + speck * 0.1f;
            });
            return c.Finish(0.9f, 2f, 2);
        }

        /// <summary>Stucco/plaster: soft bumps, stains and a few chips.</summary>
        public static SurfaceMaps Plaster(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float bump = Fbm(u, v, 14, 3, 111);
                float stain = Fbm(u, v, 3, 4, 112);
                float drip = MathF.Max(0f, Fbm(u, v, 14, 1, 2, 113)) * SmoothStep(0f, 0.5f, Fbm(u, v, 2, 2, 114));
                float chip = SmoothStep(0.55f, 0.62f, Fbm(u, v, 5, 4, 115));
                float lum = 0.96f + stain * 0.07f + bump * 0.03f - drip * 0.14f - chip * 0.18f;
                c.Tint(i, lum, lum * (1f - drip * 0.02f), lum * (1f - drip * 0.05f));
                c.H[i] = 0.55f + bump * 0.2f - chip * 0.25f;
                c.Crevice[i] = 1f - chip * 0.3f;
                c.Smooth[i] = 0.8f - drip * 0.3f;
            });
            return c.Finish(0.8f, 1.5f, 2);
        }

        /// <summary>Running-bond brick, pre-coloured (red-brown bricks, grey mortar): use a near-white tint.</summary>
        public static SurfaceMaps Brick(int n)
        {
            const int rows = 24, perRow = 8;
            var c = new Canvas(n);
            float mortar = 1.1f / n * rows;
            c.Each((i, u, v) =>
            {
                float fv = v * rows;
                int row = (int)MathF.Floor(fv);
                float ly = fv - row;
                float fu = u * perRow + (row % 2 == 0 ? 0f : 0.5f);
                int col = (int)MathF.Floor(fu);
                float lx = fu - col;
                int id = row * 31 + Wrap(col, perRow);
                float chip = Fbm(u, v, 32, 2, 121) * 0.04f;
                float edge = MathF.Min(MathF.Min(ly, 1f - ly), MathF.Min(lx, 1f - lx) * rows / perRow) + chip;
                float brick = SmoothStep(mortar * 0.5f, mortar * 1.3f, edge);
                float tone = 0.75f + Hash01(id, 122) * 0.35f;
                float burnt = Hash01(id, 123) < 0.12f ? 0.7f : 1f;
                float surf = Fbm(u, v, 24, 3, 124);
                float br = 0.62f * tone * burnt * (1f + surf * 0.1f), bg = 0.3f * tone * burnt * (1f + surf * 0.08f), bb = 0.22f * tone * burnt;
                float mr = 0.62f + surf * 0.05f, mg = 0.6f + surf * 0.05f, mb = 0.56f + surf * 0.05f;
                c.R[i] = Lerp(mr, br, brick);
                c.G[i] = Lerp(mg, bg, brick);
                c.B[i] = Lerp(mb, bb, brick);
                c.H[i] = brick * (0.7f + surf * 0.1f) + (1f - brick) * 0.25f;
                c.Crevice[i] = Lerp(0.6f, 1f, brick);
                c.Smooth[i] = 0.6f + brick * 0.3f;
            });
            return c.Finish(1.1f, 2f, 2);
        }

        /// <summary>Bare brushed steel: fine streaks along u and scattered scratches.</summary>
        public static SurfaceMaps Metal(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float brush = Fbm(u, v, 2, 96, 2, 131);
                float blotch = Fbm(u, v, 3, 3, 132);
                float scratch = Scratches(u, v, 133, 14);
                float lum = 0.95f + brush * 0.05f + blotch * 0.05f + scratch * 0.08f;
                c.Shade(i, lum);
                c.H[i] = 0.5f + brush * 0.08f - scratch * 0.1f;
                c.Smooth[i] = 0.82f + brush * 0.1f + blotch * 0.08f - scratch * 0.2f;
            });
            return c.Finish(0.4f, 1f, 1);
        }

        /// <summary>Thin, randomly oriented scratch lines (0..1 coverage).</summary>
        public static float Scratches(float u, float v, int seed, int count)
        {
            float best = 0f;
            for (int s = 0; s < count; s++)
            {
                float cx = Hash01(s, seed), cy = Hash01(s, seed + 1);
                float a = Hash01(s, seed + 2) * 3.14159f;
                float len = 0.05f + Hash01(s, seed + 3) * 0.2f;
                float dx = Frac(u - cx + 0.5f) - 0.5f, dy = Frac(v - cy + 0.5f) - 0.5f;
                float along = dx * MathF.Cos(a) + dy * MathF.Sin(a);
                float across = -dx * MathF.Sin(a) + dy * MathF.Cos(a);
                if (MathF.Abs(along) > len) continue;
                float w = 1f - SmoothStep(0.0005f, 0.003f, MathF.Abs(across));
                best = MathF.Max(best, w * (1f - MathF.Abs(along) / len));
            }
            return best;
        }

        /// <summary>Painted sheet metal with orange peel, chipped edges showing dark metal, grime streaks.</summary>
        public static SurfaceMaps PaintedMetal(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float peel = Fbm(u, v, 40, 2, 141);
                // Domain-warped, rough noise gives jagged flakes instead of round blobs.
                float wu = u + Fbm(u, v, 7, 3, 146) * 0.035f, wv = v + Fbm(u, v, 7, 3, 147) * 0.035f;
                float chipN = Fbm(wu, wv, 8, 5, 142, 0.62f);
                float chip = SmoothStep(0.3f, 0.33f, chipN);
                float rim = SmoothStep(0.25f, 0.3f, chipN) * (1f - chip);
                float grime = MathF.Max(0f, Fbm(u, v, 16, 1, 3, 143)) * SmoothStep(-0.2f, 0.4f, Fbm(u, v, 2, 2, 144));
                float scratch = Scratches(u, v, 145, 10);
                float lum = (0.97f + peel * 0.03f - grime * 0.22f) * (1f - chip * 0.6f) * (1f + rim * 0.08f) * (1f - scratch * 0.25f);
                c.Tint(i, lum * (1f + chip * 0.05f), lum, lum * (1f - chip * 0.08f));
                c.H[i] = 0.6f + peel * 0.03f - chip * 0.15f + rim * 0.03f;
                c.Smooth[i] = (0.95f - grime * 0.4f) * (1f - chip * 0.5f);
            });
            return c.Finish(0.6f, 1.5f, 2);
        }

        /// <summary>Corroded steel: orange-brown blotches and pits (hue lives in the map; tint grey-brown).</summary>
        public static SurfaceMaps Rust(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float rust = SmoothStep(-0.15f, 0.35f, Fbm(u, v, 4, 5, 151));
                float flake = Fbm(u, v, 30, 3, 152);
                Voronoi(u, v, 30, 153, out float p1, out _, out int pid);
                float pit = Hash01(pid, 154) < 0.25f ? 1f - SmoothStep(0.05f, 0.25f, p1) : 0f;
                float lum = 0.85f + flake * 0.12f - pit * 0.25f;
                c.Tint(i, lum * (1f + rust * 0.28f), lum * (1f - rust * 0.05f), lum * (1f - rust * 0.32f));
                c.H[i] = 0.5f + flake * 0.2f * rust - pit * 0.3f;
                c.Crevice[i] = 1f - pit * 0.35f;
                c.Smooth[i] = 1f - rust * 0.7f;
            });
            return c.Finish(1f, 2f, 2);
        }

        /// <summary>Corrugated sheet: vertical ridges with rust drips.</summary>
        public static SurfaceMaps Corrugated(int n)
        {
            const int ridges = 12;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float wave = 0.5f + 0.5f * MathF.Sin(u * ridges * 6.2831853f);
                float drip = MathF.Max(0f, Fbm(u, v, 20, 1, 3, 161)) * SmoothStep(-0.1f, 0.35f, Fbm(u, v, 2, 2, 162));
                float blotch = Fbm(u, v, 4, 4, 163);
                float lum = 0.95f + blotch * 0.07f - drip * 0.25f + (wave - 0.5f) * 0.05f;
                c.Tint(i, lum * (1f + drip * 0.18f), lum * (1f - drip * 0.02f), lum * (1f - drip * 0.18f));
                c.H[i] = wave * 0.9f + blotch * 0.03f;
                c.Smooth[i] = 0.9f - drip * 0.6f;
            });
            return c.Finish(1.1f, 0.8f, 2);
        }

        /// <summary>Asphalt roof shingles: staggered tabs in rows along u with granules and shadowed lower edges.</summary>
        public static SurfaceMaps Shingles(int n)
        {
            const int rows = 10, tabs = 6;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float fv = v * rows;
                int row = (int)MathF.Floor(fv);
                float ly = fv - row;
                float fu = u * tabs + (row % 2 == 0 ? 0f : 0.5f) + (Hash01(row, 171) - 0.5f) * 0.1f;
                int col = (int)MathF.Floor(fu);
                float lx = fu - col;
                int id = row * 17 + Wrap(col, tabs);
                float slot = 1f - SmoothStep(0.012f, 0.03f, MathF.Min(lx, 1f - lx));
                float granule = Hash01((int)(u * n), (int)(v * n), 172) - 0.5f;
                float tone = 0.8f + Hash01(id, 173) * 0.28f;
                float lip = 1f - SmoothStep(0f, 0.12f, ly);
                float lum = tone * (1f + granule * 0.18f) * (1f - lip * 0.3f) * (1f - slot * 0.5f);
                c.Shade(i, lum);
                c.H[i] = 0.3f + (1f - ly) * 0.55f + granule * 0.04f - slot * 0.3f;
                c.Crevice[i] = (1f - lip * 0.35f) * (1f - slot * 0.4f);
                c.Smooth[i] = 0.7f + granule * 0.3f;
            });
            return c.Finish(1.2f, 1.8f, 2);
        }

        /// <summary>Plain-weave cloth.</summary>
        public static SurfaceMaps Fabric(int n)
        {
            const int threads = 32;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float wu = MathF.Sin(u * threads * 6.2831853f), wv = MathF.Sin(v * threads * 6.2831853f);
                float over = (wu * wv) > 0 ? MathF.Abs(wv) : MathF.Abs(wu);
                float fuzz = Fbm(u, v, 24, 2, 181);
                float stain = Fbm(u, v, 2, 3, 182);
                float lum = 0.88f + over * 0.12f + fuzz * 0.04f + stain * 0.06f;
                c.Shade(i, lum);
                c.H[i] = over * 0.6f + fuzz * 0.1f;
                c.Smooth[i] = 0.7f + over * 0.3f;
            });
            return c.Finish(0.9f, 1f, 1);
        }

        /// <summary>Heavy canvas/tarp: coarse weave, wrinkles and stains.</summary>
        public static SurfaceMaps Tarp(int n)
        {
            const int threads = 48;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float wu = MathF.Sin(u * threads * 6.2831853f), wv = MathF.Sin(v * threads * 6.2831853f);
                float weave = (wu * wv) > 0 ? MathF.Abs(wv) : MathF.Abs(wu);
                float wrinkle = Ridged(u, v, 3, 2, 3, 191);
                float stain = SmoothStep(0.1f, 0.5f, Fbm(u, v, 3, 4, 192));
                float lum = (0.9f + weave * 0.08f + wrinkle * 0.08f) * (1f - stain * 0.18f);
                c.Tint(i, lum, lum * (1f - stain * 0.02f), lum * (1f - stain * 0.06f));
                c.H[i] = weave * 0.25f + wrinkle * 0.5f;
                c.Smooth[i] = 0.8f + weave * 0.2f;
            });
            return c.Finish(1f, 1.2f, 2);
        }

        /// <summary>Pebbled leather with creases.</summary>
        public static SurfaceMaps Leather(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                Voronoi(u, v, 36, 201, out float f1, out float f2, out int id);
                float pebble = SmoothStep(0f, 0.25f, f2 - f1);
                float crease = SmoothStep(0.85f, 0.97f, Ridged(u, v, 3, 3, 2, 202));
                float wear = Fbm(u, v, 3, 3, 203);
                float lum = (0.9f + pebble * 0.1f + wear * 0.08f) * (1f - crease * 0.2f);
                c.Shade(i, lum);
                c.H[i] = pebble * 0.4f - crease * 0.3f;
                c.Smooth[i] = 0.7f + pebble * 0.2f + wear * 0.1f;
            });
            return c.Finish(0.9f, 1.5f, 1);
        }

        /// <summary>Very subtle mottling and pores.</summary>
        public static SurfaceMaps Skin(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float mottle = Fbm(u, v, 3, 3, 211);
                float pore = Fbm(u, v, 48, 1, 212);
                float lum = 0.97f + mottle * 0.05f + pore * 0.015f;
                c.Tint(i, lum * (1f + mottle * 0.02f), lum, lum * (1f - mottle * 0.02f));
                c.H[i] = 0.5f + pore * 0.12f + mottle * 0.05f;
                c.Smooth[i] = 0.8f + mottle * 0.15f;
            });
            return c.Finish(0.4f, 0.6f, 1);
        }

        /// <summary>Tilled soil: clods, small stones, dark crevices.</summary>
        public static SurfaceMaps Soil(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                Voronoi(u, v, 14, 221, out float f1, out float f2, out int id);
                float clod = SmoothStep(0f, 0.3f, f2 - f1);
                float dome = 1f - MathF.Min(1f, f1);
                float grit = Fbm(u, v, 32, 2, 222);
                Voronoi(u, v, 40, 223, out float s1, out _, out int sid);
                float stone = Hash01(sid, 224) < 0.08f ? 1f - SmoothStep(0.15f, 0.3f, s1) : 0f;
                float tone = 0.88f + Hash01(id, 225) * 0.18f;
                float lum = tone * (0.78f + dome * 0.3f) * Lerp(0.6f, 1f, clod) + stone * 0.35f + grit * 0.05f;
                c.Tint(i, lum, lum * (1f - stone * 0.05f), lum * (1f + stone * 0.08f));
                c.H[i] = dome * 0.5f * clod + grit * 0.08f + stone * 0.3f;
                c.Crevice[i] = Lerp(0.45f, 1f, clod);
                c.Smooth[i] = 0.5f + stone * 0.5f;
            });
            return c.Finish(1.3f, 2.5f, 2);
        }

        /// <summary>Road asphalt: aggregate speckle, wear blotches, cracks.</summary>
        public static SurfaceMaps Asphalt(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float agg = Hash01((int)(u * n), (int)(v * n), 231);
                float agg2 = Hash01((int)(u * n * 0.5f), (int)(v * n * 0.5f), 232);
                float wear = Fbm(u, v, 3, 4, 233);
                float crack = SmoothStep(0.92f, 0.98f, Ridged(u, v, 3, 3, 3, 234)) * SmoothStep(0f, 0.3f, Fbm(u, v, 2, 2, 235));
                float lum = 0.88f + (agg - 0.5f) * 0.18f + (agg2 > 0.93f ? 0.25f : 0f) + wear * 0.1f - crack * 0.35f;
                c.Shade(i, lum);
                c.H[i] = 0.5f + (agg - 0.5f) * 0.12f - crack * 0.4f;
                c.Crevice[i] = 1f - crack * 0.4f;
                c.Smooth[i] = 0.7f + wear * 0.2f;
            });
            return c.Finish(0.9f, 2f, 1);
        }

        /// <summary>Loose gravel: packed pebbles of varied tone.</summary>
        public static SurfaceMaps Gravel(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                Voronoi(u, v, 22, 241, out float f1, out float f2, out int id);
                float edge = f2 - f1;
                float pebble = SmoothStep(0.02f, 0.2f, edge);
                float dome = MathF.Sqrt(Clamp01(edge * 1.8f));
                float tone = 0.72f + Hash01(id, 242) * 0.42f;
                float warm = Hash01(id, 243) - 0.5f;
                float lum = tone * Lerp(0.45f, 1f, pebble);
                c.Tint(i, lum * (1f + warm * 0.1f), lum, lum * (1f - warm * 0.1f));
                c.H[i] = dome * pebble;
                c.Crevice[i] = Lerp(0.4f, 1f, pebble);
                c.Smooth[i] = 0.5f + pebble * 0.5f;
            });
            return c.Finish(1.4f, 2.5f, 2);
        }

        /// <summary>Tyre rubber: fine noise.</summary>
        public static SurfaceMaps Rubber(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float fine = Fbm(u, v, 32, 2, 251);
                float wear = Fbm(u, v, 3, 3, 252);
                c.Shade(i, 0.95f + fine * 0.05f + wear * 0.06f);
                c.H[i] = 0.5f + fine * 0.15f;
                c.Smooth[i] = 0.7f + wear * 0.3f;
            });
            return c.Finish(0.5f, 1f, 1);
        }

        /// <summary>Window glass: flat with faint smudges (smoothness only).</summary>
        public static SurfaceMaps Glass(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float smudge = SmoothStep(0f, 0.5f, Fbm(u, v, 3, 4, 261));
                float dirt = MathF.Max(0f, Fbm(u, v, 12, 1, 2, 262));
                c.Shade(i, 1f - dirt * 0.1f);
                c.H[i] = 0.5f;
                c.Smooth[i] = 1f - smudge * 0.25f - dirt * 0.3f;
            });
            return c.Finish(0.1f, 0.5f, 1);
        }

        /// <summary>Short grass blades with yellow and blue-green variation (hue lives in the map).</summary>
        public static SurfaceMaps GrassBlades(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float patch = Fbm(u, v, 3, 4, 271);
                float blades = 0.5f + 0.5f * MathF.Sin((u * 90f + Fbm(u, v, 10, 3, 2, 272) * 3f) * 6.2831853f);
                float clump = Fbm(u, v, 20, 6, 2, 273);
                float dry = SmoothStep(0.15f, 0.55f, Fbm(u, v, 2, 3, 274));
                float lum = 0.75f + blades * 0.25f + clump * 0.12f + patch * 0.08f;
                c.Tint(i, lum * (1f + dry * 0.35f), lum * (1f + dry * 0.08f), lum * (1f - dry * 0.15f - patch * 0.05f));
                c.H[i] = blades * 0.4f + clump * 0.3f;
                c.Crevice[i] = Lerp(0.65f, 1f, blades);
                c.Smooth[i] = 0.6f + blades * 0.4f;
            });
            return c.Finish(1f, 1.5f, 1);
        }
    }
}
