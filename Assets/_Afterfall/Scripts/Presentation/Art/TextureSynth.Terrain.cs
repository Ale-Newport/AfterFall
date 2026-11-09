using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Ground recipes for the Unity terrain layers (<see cref="TerrainSurfaces"/>) and the lake's
    /// ripple normals. Recipes paint absolute linear colours into a <see cref="Canvas"/>, and
    /// <see cref="FinishTerrain"/> derives the normal map and bakes crevice shading into the colour.
    /// Every feature is periodic in the tile (wrapped noise, wrapped scatter cells), so the maps tile.
    /// </summary>
    public static partial class TextureSynth
    {
        // ---------------------------------------------------------------- helpers

        /// <summary>A linear RGB colour (recipes keep palettes in sRGB 0..255 for readability).</summary>
        private readonly struct Col
        {
            public readonly float R, G, B;

            public Col(float r, float g, float b)
            {
                R = r;
                G = g;
                B = b;
            }

            public static Col S(float r, float g, float b) => new Col(SrgbToLinear(r / 255f), SrgbToLinear(g / 255f), SrgbToLinear(b / 255f));
            public static Col operator *(Col c, float k) => new Col(c.R * k, c.G * k, c.B * k);
            public static Col Mix(Col a, Col b, float t) => new Col(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

        private static void Put(Canvas c, int i, Col col)
        {
            c.R[i] = col.R;
            c.G[i] = col.G;
            c.B[i] = col.B;
        }

        /// <summary>Periodic Worley noise that also returns the offset from the nearest feature point (cell units).</summary>
        private static void VoronoiLocal(float u, float v, int cells, int seed, float jitter, out float f1, out float f2, out int id, out float ox, out float oy)
        {
            float x = u * cells, y = v * cells;
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            f1 = 9f;
            f2 = 9f;
            id = 0;
            ox = 0f;
            oy = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy;
                    int wx = Wrap(cx, cells), wy = Wrap(cy, cells);
                    float px = cx + 0.5f + (Hash01(wx, wy, seed) - 0.5f) * jitter;
                    float py = cy + 0.5f + (Hash01(wx, wy, seed + 17) - 0.5f) * jitter;
                    float rx = x - px, ry = y - py;
                    float d = MathF.Sqrt(rx * rx + ry * ry);
                    if (d < f1)
                    {
                        f2 = f1;
                        f1 = d;
                        id = wy * cells + wx;
                        ox = rx;
                        oy = ry;
                    }
                    else if (d < f2) f2 = d;
                }
            }
        }

        /// <summary>
        /// Scattered strokes (blades, needles, twigs): at most one per cell of a periodic grid, with a
        /// random angle around <paramref name="angle"/>, length and draw order. Lengths and widths are in
        /// cell units; <paramref name="reach"/> is how many neighbour cells a stroke may cross. Returns the
        /// anti-aliased coverage of the topmost stroke, its hash (tone) and the position along it (−1..1).
        /// </summary>
        private static float Strokes(float u, float v, int cells, int seed, float density, float minHalf, float maxHalf, float halfWidth,
            float texel, int reach, float angle, float spread, out float tone, out float along01)
        {
            float x = u * cells, y = v * cells;
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float best = 0f, bestScore = -1f;
            tone = 0f;
            along01 = 0f;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int cx = xi + dx, cy = yi + dy;
                    int wx = Wrap(cx, cells), wy = Wrap(cy, cells);
                    if (Hash01(wx, wy, seed) > density) continue;
                    float px = cx + Hash01(wx, wy, seed + 1), py = cy + Hash01(wx, wy, seed + 2);
                    float rx = x - px, ry = y - py;
                    float half = Lerp(minHalf, maxHalf, Hash01(wx, wy, seed + 4));
                    if (rx * rx + ry * ry > (half + halfWidth + texel) * (half + halfWidth + texel)) continue;
                    float a = angle + (Hash01(wx, wy, seed + 3) - 0.5f) * spread;
                    float ca = MathF.Cos(a), sa = MathF.Sin(a);
                    float al = rx * ca + ry * sa;
                    float ac = MathF.Abs(-rx * sa + ry * ca);
                    float s = al / half;
                    float w = halfWidth * MathF.Max(0.35f, 1f - s * s * 0.6f);
                    float cov = (1f - SmoothStep(w - texel * 0.5f, w + texel * 0.5f, ac)) * (1f - SmoothStep(half - texel, half + texel, MathF.Abs(al)));
                    if (cov <= 0.01f) continue;
                    float key = Hash01(wx, wy, seed + 5);
                    float score = cov >= 0.5f ? 1f + key : cov;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = cov;
                    tone = key;
                    along01 = Clamp01(s * 0.5f + 0.5f);
                }
            }
            return best;
        }

        /// <summary>
        /// Scattered ellipses (leaves, pebbles, lichen): at most one per cell, random size, aspect and
        /// angle. Returns the anti-aliased coverage of the topmost one, its hash and the local position
        /// inside it (lx along the long axis, ly across, both −1..1).
        /// </summary>
        private static float Blobs(float u, float v, int cells, int seed, float density, float minR, float maxR, float minAspect,
            float texel, out float tone, out float lx, out float ly)
        {
            float x = u * cells, y = v * cells;
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float best = 0f, bestScore = -1f;
            tone = 0f;
            lx = 0f;
            ly = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy;
                    int wx = Wrap(cx, cells), wy = Wrap(cy, cells);
                    if (Hash01(wx, wy, seed) > density) continue;
                    float px = cx + Hash01(wx, wy, seed + 1), py = cy + Hash01(wx, wy, seed + 2);
                    float rx = x - px, ry = y - py;
                    float r = Lerp(minR, maxR, Hash01(wx, wy, seed + 4));
                    if (rx * rx + ry * ry > (r + texel) * (r + texel)) continue;
                    float aspect = Lerp(minAspect, 1f, Hash01(wx, wy, seed + 6));
                    float a = Hash01(wx, wy, seed + 3) * 3.14159f;
                    float ca = MathF.Cos(a), sa = MathF.Sin(a);
                    float al = (rx * ca + ry * sa) / r;
                    float ac = (-rx * sa + ry * ca) / (r * aspect);
                    float d = MathF.Sqrt(al * al + ac * ac);
                    float edge = texel / (r * aspect);
                    float cov = 1f - SmoothStep(1f - edge, 1f + edge, d);
                    if (cov <= 0.01f) continue;
                    float key = Hash01(wx, wy, seed + 5);
                    float score = cov >= 0.5f ? 1f + key : cov;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = cov;
                    tone = key;
                    lx = al;
                    ly = ac;
                }
            }
            return best;
        }

        /// <summary>
        /// A continuous, jagged crack network: the zero contour of periodic noise (with a finer noise
        /// roughening it). Returns anti-aliased coverage; <paramref name="width"/> is in noise units.
        /// </summary>
        private static float CrackLine(float u, float v, int period, int seed, float width, float texel)
        {
            float n = Perlin(u * period, v * period, period, period, seed) + Perlin(u * period * 6f, v * period * 6f, period * 6, period * 6, seed + 1) * 0.14f
                + Perlin(u * period * 24f, v * period * 24f, period * 24, period * 24, seed + 2) * 0.03f;
            float aa = texel * period * 1.2f;
            return 1f - SmoothStep(width, width + aa, MathF.Abs(n));
        }

        /// <summary>
        /// Builds full-colour terrain maps from a canvas whose R/G/B hold absolute linear colours and
        /// whose Smooth holds absolute smoothness. Crevices darken the colour (URP terrain has no cavity input).
        /// </summary>
        private static TerrainMaps FinishTerrain(Canvas c, float bump, float cavityDepth = 2.5f, int cavityRadius = 3, float cavityStrength = 0.8f)
        {
            int n = c.N;
            var maps = new TerrainMaps(n);
            float[] blur = BoxBlur(c.H, n, cavityRadius);
            float k = n * 0.02f * bump;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float dx = (c.H[y * n + (x + 1) % n] - c.H[y * n + (x + n - 1) % n]) * 0.5f * k;
                    float dy = (c.H[((y + 1) % n) * n + x] - c.H[((y + n - 1) % n) * n + x]) * 0.5f * k;
                    float inv = 1f / MathF.Sqrt(dx * dx + dy * dy + 1f);
                    float cav = Clamp01(1f - MathF.Max(0f, blur[i] - c.H[i]) * cavityDepth);
                    cav = MathF.Max(0.3f, cav * c.Crevice[i]);
                    float shade = 1f + (cav - 1f) * cavityStrength;
                    int o = i * 4;
                    maps.Color[o] = Clamp01(c.R[i] * shade);
                    maps.Color[o + 1] = Clamp01(c.G[i] * shade);
                    maps.Color[o + 2] = Clamp01(c.B[i] * shade);
                    maps.Color[o + 3] = Clamp01(c.Smooth[i]);
                    maps.Normal[o] = -dx * inv;
                    maps.Normal[o + 1] = -dy * inv;
                    maps.Normal[o + 2] = inv;
                    maps.Normal[o + 3] = Clamp01(c.H[i]);
                }
            }
            return maps;
        }

        // ---------------------------------------------------------------- grass

        /// <summary>
        /// A grass sward seen from above: five layers of blade strokes that lean along a slowly turning
        /// flow field (lower layers darker, tips lighter), soil showing where the sward thins, lush and
        /// tired patches and optional clover. Shared by the meadow and the dry grass layers.
        /// </summary>
        private static TerrainMaps GrassSward(int n, int seed, Col deep, Col mid, Col tip, Col straw, Col soil,
            float strawShare, float soilShare, float clover, int cells, float bladeHalf)
        {
            var c = new Canvas(n);
            Col cloverCol = Col.S(64, 92, 52), cloverLight = Col.S(88, 116, 68);
            float texel = 1f / n;
            int reach = bladeHalf > 0.95f ? 2 : 1;
            c.Each((i, u, v) =>
            {
                float patch = Fbm(u, v, 3, 3, seed);
                float patch2 = Fbm(u, v, 9, 2, seed + 1);
                float flow = Fbm(u, v, 2, 2, seed + 2) * 4f;
                float bare = soilShare * SmoothStep(-0.05f, 0.5f, Fbm(u, v, 7, 3, seed + 3) + patch * 0.3f);
                Col col = Col.Mix(deep * 0.7f, deep, 0.5f + patch2 * 0.5f);
                col = Col.Mix(col, soil, Clamp01(bare * 1.3f));
                float h = 0.1f;
                float tired = SmoothStep(-0.1f, 0.5f, -patch);
                for (int layer = 0; layer < 5; layer++)
                {
                    float depth = layer * 0.25f;
                    float density = Lerp(0.95f, 0.6f, depth) * (1f - bare * 0.8f);
                    float cov = Strokes(u, v, cells, seed + 20 + layer * 13, density, bladeHalf * 0.55f, bladeHalf, 0.07f, texel * cells, reach,
                        flow + layer * 0.4f, 1.6f, out float tone, out float al);
                    if (cov <= 0f) continue;
                    // Lower layers sit in the shade of the upper ones; tips catch the light and yellow.
                    Col bc = Col.Mix(deep, mid, Clamp01(0.42f + depth * 0.38f + tone * 0.22f));
                    bc = Col.Mix(bc, tip, Clamp01((al - 0.5f) * 1.8f) * (0.25f + tone * 0.5f));
                    if (Hash01((int)(tone * 65536f), seed + 4) < strawShare * (1f + tired * 1.5f))
                        bc = Col.Mix(bc, straw, 0.55f + tone * 0.4f) * (0.8f + depth * 0.3f);
                    col = Col.Mix(col, bc, cov);
                    h = Lerp(h, 0.3f + depth * 0.5f + al * 0.1f, cov);
                }
                // Lush (darker, bluer) and tired (yellower) patches across the tile.
                float lush = patch * 0.6f + patch2 * 0.4f;
                col = lush > 0f ? Col.Mix(col, col * 0.88f, lush) : Col.Mix(col, Col.Mix(col, tip, 0.35f), -lush * 0.8f);

                float cl = 0f;
                if (clover > 0f)
                {
                    VoronoiLocal(u, v, 9, seed + 10, 0.9f, out float p1, out _, out int pid, out _, out _);
                    float inPatch = Hash01(pid, seed + 11) < clover ? 1f - SmoothStep(0.3f, 0.55f, p1 + Fbm(u, v, 16, 2, seed + 12) * 0.15f) : 0f;
                    if (inPatch > 0f)
                    {
                        float leaf = Blobs(u, v, 110, seed + 13, 0.8f * inPatch, 0.3f, 0.48f, 0.75f, texel * 110f, out float lt, out float lx, out float ly);
                        cl = leaf;
                        Col lc = Col.Mix(cloverCol, cloverLight, Clamp01(0.3f + lt * 0.5f - (lx * lx + ly * ly) * 0.3f));
                        col = Col.Mix(col, lc, cl);
                        h = Lerp(h, 0.75f + (1f - lx * lx - ly * ly) * 0.1f, cl);
                    }
                }

                Put(c, i, col);
                c.H[i] = h;
                c.Crevice[i] = Lerp(0.75f, 1f, SmoothStep(0.1f, 0.4f, h));
                c.Smooth[i] = 0.08f + SmoothStep(0.3f, 0.8f, h) * 0.1f + cl * 0.08f;
            });
            return FinishTerrain(c, 0.7f, 1.6f, 2, 0.5f);
        }

        /// <summary>Meadow: green sward with clover, yellowed tips and a little soil.</summary>
        public static TerrainMaps TerrainMeadow(int n) =>
            GrassSward(n, 4100, Col.S(56, 72, 34), Col.S(104, 124, 58), Col.S(150, 150, 82), Col.S(156, 142, 96), Col.S(76, 64, 46),
                0.05f, 0.25f, 0.3f, 56, 0.85f);

        /// <summary>Dry grass: straw-coloured, longer lodged blades with green regrowth and more bare soil.</summary>
        public static TerrainMaps TerrainDryGrass(int n) =>
            GrassSward(n, 4200, Col.S(70, 70, 44), Col.S(112, 108, 66), Col.S(158, 144, 96), Col.S(170, 152, 108), Col.S(92, 78, 58),
                0.55f, 0.45f, 0f, 44, 0.95f);

        // ---------------------------------------------------------------- forest floor

        /// <summary>Forest floor: dark humus under pine needles, fallen leaves, twigs, cones and moss.</summary>
        public static TerrainMaps TerrainForestFloor(int n)
        {
            var c = new Canvas(n);
            Col humus = Col.S(44, 35, 27), humusLight = Col.S(70, 56, 40), moss = Col.S(58, 72, 36), mossLight = Col.S(84, 98, 50);
            Col needleA = Col.S(112, 80, 52), needleB = Col.S(90, 72, 54), needleC = Col.S(136, 106, 72);
            Col leafA = Col.S(138, 110, 70), leafB = Col.S(114, 74, 46), leafC = Col.S(88, 66, 46), leafD = Col.S(146, 124, 78);
            Col twig = Col.S(66, 50, 36), twigLight = Col.S(104, 86, 64), cone = Col.S(92, 62, 38);
            float texel = 1f / n;
            c.Each((i, u, v) =>
            {
                float blot = Fbm(u, v, 4, 4, 4301);
                float fine = Fbm(u, v, 40, 2, 4302);
                Col col = Col.Mix(humus, humusLight, Clamp01(0.45f + blot * 0.5f + fine * 0.2f));
                float h = 0.15f + blot * 0.08f + fine * 0.04f;
                float smooth = 0.06f;
                float crev = 1f;

                float mossMask = SmoothStep(0.28f, 0.5f, Fbm(u, v, 4, 4, 4303) + fine * 0.2f);
                if (mossMask > 0f)
                {
                    float fuzz = Fbm(u, v, 64, 2, 4304);
                    col = Col.Mix(col, Col.Mix(moss, mossLight, Clamp01(0.5f + fuzz * 0.8f)), mossMask);
                    h += mossMask * (0.12f + fuzz * 0.05f);
                }

                // Three layers of needles, denser where there is no moss.
                float needleDensity = 0.95f - mossMask * 0.6f;
                for (int layer = 0; layer < 3; layer++)
                {
                    float cov = Strokes(u, v, 64, 4310 + layer * 11, needleDensity, 0.55f, 0.95f, 0.045f, texel * 64f, 1, layer * 1.1f, 6.28f, out float tone, out float al);
                    if (cov <= 0f) continue;
                    Col nc = tone < 0.4f ? Col.Mix(needleA, needleB, tone * 2.5f) : Col.Mix(needleB, needleC, (tone - 0.4f) * 1.6f);
                    nc = nc * (0.85f + 0.3f * (1f - MathF.Abs(al - 0.5f)));
                    col = Col.Mix(col, nc, cov);
                    h = MathF.Max(h, 0.3f + layer * 0.04f) * cov + h * (1f - cov);
                }

                float leafCov = Blobs(u, v, 16, 4320, 0.45f, 0.16f, 0.3f, 0.4f, texel * 16f, out float lt, out float lx, out float ly);
                // Pointed (almond) leaves rather than ellipses.
                leafCov *= 1f - SmoothStep(0.85f, 1.05f, MathF.Abs(ly) / (1.02f - lx * lx));
                if (leafCov > 0f)
                {
                    Col lc = lt < 0.3f ? leafA : lt < 0.55f ? leafB : lt < 0.8f ? leafC : leafD;
                    float vein = 1f - SmoothStep(0.02f, 0.08f, MathF.Abs(ly));
                    float rim = SmoothStep(0.65f, 1f, MathF.Sqrt(lx * lx + ly * ly));
                    lc = lc * (1f - vein * 0.2f - rim * 0.25f) * (0.9f + Fbm(u, v, 48, 2, 4321) * 0.15f);
                    col = Col.Mix(col, lc, leafCov);
                    h = Lerp(h, 0.45f + (1f - lx * lx) * 0.05f + rim * 0.05f, leafCov);
                    smooth = Lerp(smooth, 0.14f, leafCov);
                }

                float twigCov = Strokes(u, v, 5, 4330, 0.7f, 0.28f, 0.6f, 0.009f, texel * 5f, 2, 0f, 6.28f, out float tt, out float ta);
                if (twigCov > 0f)
                {
                    Col tc = Col.Mix(twig, twigLight, Clamp01(tt * 0.6f + Fbm(u, v, 30, 2, 4331) * 0.4f));
                    col = Col.Mix(col, tc, twigCov);
                    h = Lerp(h, 0.65f, twigCov);
                }

                float coneCov = Blobs(u, v, 6, 4340, 0.18f, 0.035f, 0.05f, 0.6f, texel * 6f, out _, out float cx, out float cy);
                if (coneCov > 0f)
                {
                    float scales = 0.75f + 0.25f * MathF.Sin(cx * 14f) * MathF.Sin(cy * 9f);
                    col = Col.Mix(col, cone * scales, coneCov);
                    h = Lerp(h, 0.75f, coneCov);
                }

                // Shadowed gaps between litter pieces.
                crev = Lerp(0.7f, 1f, SmoothStep(0.12f, 0.32f, h));
                Put(c, i, col);
                c.H[i] = h;
                c.Crevice[i] = crev;
                c.Smooth[i] = smooth;
            });
            return FinishTerrain(c, 1.2f, 2.5f, 2, 0.8f);
        }

        // ---------------------------------------------------------------- dirt, gravel, shore

        /// <summary>Compacted dirt: blotches, fine grain, embedded pebbles and stones, dried-mud cracks and damp hollows.</summary>
        public static TerrainMaps TerrainDirt(int n)
        {
            var c = new Canvas(n);
            Col light = Col.S(112, 96, 76), dark = Col.S(78, 64, 50), wet = Col.S(62, 52, 40);
            Col stoneA = Col.S(118, 112, 104), stoneB = Col.S(128, 116, 98), stoneC = Col.S(88, 84, 78), straw = Col.S(138, 122, 88);
            float texel = 1f / n;
            c.Each((i, u, v) =>
            {
                float b1 = Fbm(u, v, 3, 4, 4401);
                float b2 = Fbm(u, v, 10, 3, 4402);
                float grain = Hash01((int)(u * n), (int)(v * n), 4403) - 0.5f;
                Col col = Col.Mix(dark, light, Clamp01(0.55f + b1 * 0.35f + b2 * 0.2f));
                col = col * (1f + grain * 0.08f);
                float h = 0.45f + b1 * 0.1f + b2 * 0.05f + grain * 0.03f;
                float smooth = 0.08f;
                float crev = 1f;

                float dryMask = SmoothStep(0.05f, 0.35f, Fbm(u, v, 2, 3, 4404));
                if (dryMask > 0f)
                {
                    VoronoiLocal(u, v, 13, 4405, 0.85f, out float f1, out float f2, out _, out _, out _);
                    float crack = (1f - SmoothStep(0.01f, 0.05f, f2 - f1)) * dryMask;
                    col = Col.Mix(col, col * 0.55f, crack);
                    col = Col.Mix(col, Col.Mix(col, light, 0.5f), dryMask * (1f - crack) * 0.4f);
                    h -= crack * 0.3f;
                    crev -= crack * 0.35f;
                }

                float damp = SmoothStep(0.15f, 0.5f, -b1 + b2 * 0.3f);
                col = Col.Mix(col, wet, damp * 0.7f);
                smooth += damp * 0.22f;

                float pc = Blobs(u, v, 48, 4410, 0.3f, 0.14f, 0.32f, 0.55f, texel * 48f, out float pt, out float px, out float py);
                if (pc > 0f)
                {
                    float d2 = px * px + py * py;
                    Col sc = pt < 0.4f ? stoneA : pt < 0.7f ? stoneB : stoneC;
                    sc = Col.Mix(sc, col, 0.25f) * (1f - d2 * 0.3f);
                    col = Col.Mix(col, sc, pc);
                    h = Lerp(h, 0.6f + (1f - d2) * 0.2f, pc);
                    smooth = Lerp(smooth, 0.2f, pc);
                }
                float big = Blobs(u, v, 10, 4420, 0.16f, 0.14f, 0.26f, 0.65f, texel * 10f, out float bt, out float bx, out float by);
                if (big > 0f)
                {
                    float d2 = bx * bx + by * by;
                    float buried = SmoothStep(0.55f, 0.95f, d2 + Fbm(u, v, 32, 2, 4421) * 0.2f);
                    Col sc = Col.Mix(Col.Mix(stoneC, stoneA, bt), col, 0.2f) * (0.95f - d2 * 0.25f);
                    col = Col.Mix(col, sc, big * (1f - buried));
                    h = Lerp(h, 0.6f + (1f - d2) * 0.15f, big);
                }
                float bits = Strokes(u, v, 30, 4430, 0.18f, 0.2f, 0.45f, 0.03f, texel * 30f, 1, 0f, 6.28f, out float st, out _);
                if (bits > 0f) col = Col.Mix(col, straw * (0.7f + st * 0.4f), bits * 0.8f);

                Put(c, i, col);
                c.H[i] = h;
                c.Crevice[i] = Clamp01(crev);
                c.Smooth[i] = smooth;
            });
            return FinishTerrain(c, 1f, 2.5f, 2, 0.75f);
        }

        /// <summary>Loose gravel: angular stones of mixed rock types with dust settling between them.</summary>
        public static TerrainMaps TerrainGravel(int n)
        {
            var c = new Canvas(n);
            var palette = new[]
            {
                Col.S(138, 134, 126), Col.S(118, 112, 104), Col.S(156, 146, 128), Col.S(100, 98, 96), Col.S(128, 116, 100), Col.S(146, 140, 134)
            };
            Col dust = Col.S(132, 118, 96), gap = Col.S(58, 52, 44);
            c.Each((i, u, v) =>
            {
                float dustMask = SmoothStep(-0.1f, 0.5f, Fbm(u, v, 3, 3, 4501));
                VoronoiLocal(u, v, 56, 4502, 0.95f, out float f1, out float f2, out int id, out _, out _);
                float edge = f2 - f1;
                float stone = SmoothStep(0.03f, 0.13f, edge);
                float dome = MathF.Sqrt(Clamp01(edge * 2.4f));
                Col sc = palette[(int)(Hash01(id, 4503) * palette.Length) % palette.Length] * (0.85f + Hash01(id, 4504) * 0.3f);
                float facet = Fbm(u, v, 96, 2, 4505);
                sc = sc * (1f + facet * 0.08f + (dome - 0.5f) * 0.12f);
                // Smaller stones fill the gaps.
                VoronoiLocal(u, v, 150, 4506, 0.95f, out float g1, out float g2, out int gid, out _, out _);
                float small = SmoothStep(0.04f, 0.16f, g2 - g1);
                Col fill = Col.Mix(gap, palette[(int)(Hash01(gid, 4507) * palette.Length) % palette.Length] * 0.8f, small);
                Col col = Col.Mix(fill, sc, stone);
                float dusty = dustMask * (0.35f + (1f - stone) * 0.5f);
                col = Col.Mix(col, dust, Clamp01(dusty));
                Put(c, i, col);
                c.H[i] = stone * (0.5f + dome * 0.4f) + (1f - stone) * (0.15f + small * 0.15f) + dustMask * 0.05f;
                c.Crevice[i] = Lerp(0.45f, 1f, Clamp01(stone + small * 0.4f + dustMask * 0.3f));
                c.Smooth[i] = 0.08f + stone * 0.12f;
            });
            return FinishTerrain(c, 1.3f, 2.5f, 2, 0.8f);
        }

        /// <summary>Lake shore: damp silt with ripple marks, pebbles, algae stains and bits of debris.</summary>
        public static TerrainMaps TerrainShore(int n)
        {
            var c = new Canvas(n);
            Col silt = Col.S(124, 112, 90), wet = Col.S(80, 72, 58), algae = Col.S(80, 86, 52), pebbleA = Col.S(118, 112, 104), pebbleB = Col.S(94, 88, 80), debris = Col.S(56, 46, 36);
            float texel = 1f / n;
            c.Each((i, u, v) =>
            {
                float warp = Fbm(u, v, 3, 3, 4601);
                float ripple = 0.5f + 0.5f * MathF.Sin((v * 18f + warp * 1.6f + Fbm(u, v, 9, 2, 4602) * 0.3f) * 6.2831853f);
                float wetMask = SmoothStep(-0.25f, 0.35f, Fbm(u, v, 2, 3, 4603));
                float grain = Hash01((int)(u * n), (int)(v * n), 4604) - 0.5f;
                Col col = Col.Mix(silt, wet, wetMask * 0.85f) * (0.92f + ripple * 0.12f + grain * 0.06f);
                float alg = SmoothStep(0.2f, 0.5f, Fbm(u, v, 5, 3, 4605)) * wetMask;
                col = Col.Mix(col, algae, alg * 0.6f);
                float h = 0.4f + ripple * 0.18f + grain * 0.02f;
                float smooth = 0.12f + wetMask * 0.33f;

                float pc = Blobs(u, v, 26, 4610, 0.3f, 0.16f, 0.36f, 0.6f, texel * 26f, out float pt, out float px, out float py);
                if (pc > 0f)
                {
                    float d2 = px * px + py * py;
                    col = Col.Mix(col, Col.Mix(pebbleB, pebbleA, pt) * (1f - d2 * 0.3f) * (1f - wetMask * 0.3f), pc);
                    h = Lerp(h, 0.7f + (1f - d2) * 0.2f, pc);
                    smooth = Lerp(smooth, 0.25f + wetMask * 0.3f, pc);
                }
                float bits = Strokes(u, v, 12, 4620, 0.12f, 0.1f, 0.25f, 0.035f, texel * 12f, 1, 0f, 6.28f, out _, out _);
                if (bits > 0f)
                {
                    col = Col.Mix(col, debris, bits);
                    h = Lerp(h, 0.65f, bits);
                }
                Put(c, i, col);
                c.H[i] = h;
                c.Crevice[i] = 1f;
                c.Smooth[i] = smooth;
            });
            return FinishTerrain(c, 0.9f, 2f, 2, 0.7f);
        }

        // ---------------------------------------------------------------- rock and asphalt

        /// <summary>Weathered bedrock: fractal relief, strata, a few deep fractures, mineral grain, lichen rosettes and moss in the hollows.</summary>
        public static TerrainMaps TerrainRock(int n)
        {
            var c = new Canvas(n);
            Col grey = Col.S(112, 110, 104), warm = Col.S(118, 108, 94), dark = Col.S(56, 55, 52);
            Col lichenY = Col.S(140, 136, 84), lichenP = Col.S(150, 154, 142), moss = Col.S(62, 76, 40), soil = Col.S(72, 60, 46);
            float texel = 1f / n;
            c.Each((i, u, v) =>
            {
                // Domain-warped fractures so the blocks read as broken rock, not paving.
                float wu = u + Fbm(u, v, 3, 3, 4720) * 0.06f, wv = v + Fbm(u, v, 3, 3, 4721) * 0.06f;
                VoronoiLocal(Frac(wu), Frac(wv), 3, 4701, 0.9f, out float f1, out float f2, out int id, out _, out _);
                float crackMask = SmoothStep(0.05f, 0.35f, Fbm(u, v, 3, 2, 4722));
                float crack = (1f - SmoothStep(0.004f, 0.022f, f2 - f1 + Fbm(u, v, 24, 2, 4702) * 0.01f)) * crackMask;
                float slabTone = Hash01(id, 4703);
                float relief = Fbm(u, v, 5, 5, 4704);
                float fine = Fbm(u, v, 36, 2, 4705);
                float strata = 0.5f + 0.5f * MathF.Sin((v * 11f + relief * 1.4f + Fbm(u, v, 2, 2, 4706) * 0.8f) * 6.2831853f);
                float fract = SmoothStep(0.88f, 0.96f, Ridged(u, v, 6, 6, 3, 4707)) * SmoothStep(0f, 0.3f, Fbm(u, v, 4, 2, 4723));
                float speck = Hash01((int)(u * n * 0.5f), (int)(v * n * 0.5f), 4708);

                Col col = Col.Mix(grey, warm, Clamp01(slabTone * 0.4f + relief * 0.4f + 0.25f));
                float weather = Fbm(u, v, 3, 3, 4724);
                col = col * (0.8f + relief * 0.24f + strata * 0.08f + fine * 0.08f + (speck - 0.5f) * 0.1f);
                col = Col.Mix(col, dark, SmoothStep(0.05f, 0.5f, weather) * 0.35f);
                col = Col.Mix(col, dark, Clamp01(crack * 0.55f + fract * 0.35f));
                float h = 0.5f + relief * 0.35f + strata * 0.06f + fine * 0.04f - crack * 0.4f - fract * 0.2f;

                // Moss and soil collect in fractures and hollows.
                float hollow = Clamp01(crack * 0.7f + fract * 0.4f + SmoothStep(-0.05f, -0.45f, relief) * 0.7f);
                float green = SmoothStep(-0.1f, 0.35f, Fbm(u, v, 4, 3, 4709));
                col = Col.Mix(col, Col.Mix(soil, moss, green), hollow * 0.6f);

                // Crusty lichen patches on the exposed tops.
                float lichen = SmoothStep(0.22f, 0.38f, Fbm(u, v, 6, 3, 4710) + Fbm(u, v, 48, 2, 4711) * 0.12f) * SmoothStep(-0.25f, 0.15f, relief);
                if (lichen > 0f)
                {
                    float kind = SmoothStep(-0.2f, 0.2f, Fbm(u, v, 3, 2, 4712));
                    float crust = 0.85f + Fbm(u, v, 96, 2, 4713) * 0.3f;
                    col = Col.Mix(col, Col.Mix(lichenY, lichenP, kind) * crust, lichen * (1f - crack) * 0.7f);
                    h += lichen * 0.03f;
                }
                Put(c, i, col);
                c.H[i] = h;
                c.Crevice[i] = 1f - crack * 0.5f;
                c.Smooth[i] = 0.1f + speck * 0.12f + (1f - hollow) * 0.04f;
            });
            return FinishTerrain(c, 1.3f, 2.5f, 3, 0.85f);
        }

        /// <summary>
        /// Old road asphalt: aggregate, worn and fresh patches, long cracks and crocodile cracking,
        /// tar-sealed cracks, oil stains and moss creeping out of the cracks.
        /// </summary>
        public static TerrainMaps TerrainAsphalt(int n)
        {
            var c = new Canvas(n);
            Col fresh = Col.S(54, 54, 53), worn = Col.S(98, 96, 92), stone = Col.S(138, 134, 128), tar = Col.S(26, 26, 27);
            Col weed = Col.S(66, 80, 42), dirt = Col.S(76, 66, 52);
            c.Each((i, u, v) =>
            {
                float wear = Fbm(u, v, 2, 4, 4801);
                float wear2 = Fbm(u, v, 7, 3, 4802);
                float agg = Hash01((int)(u * n), (int)(v * n), 4803) - 0.5f;
                float agg2 = Hash01((int)(u * n * 0.5f), (int)(v * n * 0.5f), 4804);
                float wornness = Clamp01(0.45f + wear * 0.7f + wear2 * 0.25f);
                Col col = Col.Mix(fresh, worn, wornness);
                col = col * (1f + agg * (0.12f + wornness * 0.12f));
                // Exposed aggregate shows where the binder has worn away.
                if (agg2 > 0.96f - wornness * 0.05f) col = Col.Mix(col, stone * (0.7f + (agg2 - 0.9f) * 2f), 0.25f + wornness * 0.25f);
                float h = 0.55f + agg * 0.08f;
                float smooth = 0.12f + (1f - wornness) * 0.12f;

                float longCrack = CrackLine(u, v, 3, 4805, 0.008f, 1f / n) * SmoothStep(-0.35f, 0.05f, Fbm(u, v, 3, 2, 4806));
                float branch = CrackLine(u, v, 7, 4814, 0.006f, 1f / n) * SmoothStep(0.05f, 0.35f, Fbm(u, v, 4, 2, 4815));
                VoronoiLocal(u, v, 34, 4807, 0.9f, out float f1, out float f2, out int id, out _, out _);
                float gator = SmoothStep(0.15f, 0.35f, Fbm(u, v, 5, 3, 4808));
                float plate = (1f - SmoothStep(0.015f, 0.05f, f2 - f1)) * gator;
                float crack = Clamp01(MathF.Max(longCrack, MathF.Max(plate, branch * 0.8f)));
                col = Col.Mix(col, col * (0.85f + Hash01(id, 4809) * 0.3f), gator * 0.8f);
                col = Col.Mix(col, Col.Mix(tar, dirt, 0.3f), crack * 0.9f);
                h -= crack * 0.5f;

                float sealedCrack = SmoothStep(0.93f, 0.975f, Ridged(u, v, 3, 3, 3, 4810)) * SmoothStep(0.15f, 0.45f, Fbm(u, v, 2, 2, 4811));
                col = Col.Mix(col, tar, sealedCrack * 0.85f);
                h += sealedCrack * 0.04f;
                smooth = Lerp(smooth, 0.4f, sealedCrack);

                float weeds = crack * SmoothStep(0f, 0.35f, Fbm(u, v, 4, 3, 4812));
                col = Col.Mix(col, weed, weeds * 0.85f);

                float oil = SmoothStep(0.35f, 0.6f, Fbm(u, v, 3, 3, 4813)) * (1f - crack);
                col = Col.Mix(col, tar, oil * 0.4f);
                smooth = Lerp(smooth, 0.45f, oil * 0.8f);

                Put(c, i, col);
                c.H[i] = h;
                c.Crevice[i] = 1f - crack * 0.45f;
                c.Smooth[i] = smooth;
            });
            return FinishTerrain(c, 0.9f, 2f, 1, 0.7f);
        }

        // ---------------------------------------------------------------- water

        /// <summary>Tileable small wind ripples for the lake surface (normals only; the colour is unused).</summary>
        public static TerrainMaps TerrainWaterRipples(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float big = Fbm(u, v, 3, 4, 4901);
                float chop = Ridged(u, v, 7, 5, 3, 4902);
                float fine = Fbm(u, v, 16, 2, 4903);
                c.H[i] = 0.5f + big * 0.35f + chop * 0.18f + fine * 0.05f;
                c.R[i] = c.G[i] = c.B[i] = 0.5f;
                c.Smooth[i] = 1f;
            });
            return FinishTerrain(c, 0.55f, 0f, 1, 0f);
        }
    }
}
