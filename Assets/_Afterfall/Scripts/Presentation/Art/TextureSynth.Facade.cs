using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// City facades: one tile is one bay of a city building, 3.6 m wide and one 3.6 m floor high,
    /// with the window where the real building has it (1.6 m wide, 0.9–2.3 m above the floor, which
    /// is 0.15 m above the building's base), so a facade drawn with these from afar shows the same
    /// windows the building has when you walk up to it. The wall is pale and neutral: the building's
    /// colour comes from its tint.
    /// </summary>
    public static partial class TextureSynth
    {
        private const float Bay = 3.6f, WinL = 1.0f / Bay, WinR = 2.6f / Bay, WinB = 1.05f / Bay, WinT = 2.45f / Bay, Slab = 0.15f / Bay;

        public static SurfaceMaps FacadeBrick(int n) => Facade(n, 0);
        public static SurfaceMaps FacadeRender(int n) => Facade(n, 1);
        public static SurfaceMaps FacadeConcrete(int n) => Facade(n, 2);
        public static SurfaceMaps FacadeStone(int n) => Facade(n, 3);

        private static SurfaceMaps Facade(int n, int style)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                // The wall.
                float wall, h = 0.6f, crev = 1f;
                float grime = Fbm(u, v, 4, 3, 300 + style) * 0.08f + (1f - v) * 0.03f;
                switch (style)
                {
                    case 0: // brick: 48 courses and 16 bricks a course per bay
                    {
                        float fv = v * 48f;
                        int row = (int)MathF.Floor(fv);
                        float ly = fv - row;
                        float fu = u * 16f + (row % 2 == 0 ? 0f : 0.5f);
                        int col = (int)MathF.Floor(fu);
                        float lx = fu - col;
                        float edge = MathF.Min(MathF.Min(ly, 1f - ly), MathF.Min(lx, 1f - lx) * 0.33f);
                        float brick = SmoothStep(0.06f, 0.16f, edge);
                        float tone = 0.86f + Hash01(row * 31 + Wrap(col, 16), 301) * 0.2f;
                        wall = Lerp(0.66f, 0.82f * tone, brick);
                        h = brick * 0.7f + 0.2f;
                        crev = Lerp(0.65f, 1f, brick);
                        break;
                    }
                    case 1: // render
                        wall = 0.86f + Fbm(u, v, 8, 4, 302) * 0.06f;
                        h = 0.5f + Fbm(u, v, 16, 3, 303) * 0.1f;
                        break;
                    case 2: // concrete panels: joints at the bay's middle and at each floor
                    {
                        float joint = MathF.Min(MathF.Abs(u - 0.5f), MathF.Min(u, 1f - u));
                        float panel = SmoothStep(0.004f, 0.01f, joint) * SmoothStep(0.004f, 0.01f, MathF.Min(MathF.Abs(v - Slab), MathF.Abs(v - 0.88f)));
                        wall = (0.78f + Fbm(u, v, 6, 4, 304) * 0.07f) * Lerp(0.7f, 1f, panel);
                        h = 0.4f + panel * 0.2f;
                        crev = Lerp(0.6f, 1f, panel);
                        break;
                    }
                    default: // ashlar stone: blocks 0.6 × 0.4 m
                    {
                        float fv = v * 9f;
                        int row = (int)MathF.Floor(fv);
                        float ly = fv - row;
                        float fu = u * 6f + (row % 2 == 0 ? 0f : 0.5f);
                        int col = (int)MathF.Floor(fu);
                        float lx = fu - col;
                        float edge = MathF.Min(MathF.Min(ly, 1f - ly), MathF.Min(lx, 1f - lx) * 0.66f);
                        float block = SmoothStep(0.02f, 0.06f, edge);
                        wall = Lerp(0.7f, 0.86f + Hash01(row * 17 + Wrap(col, 6), 305) * 0.08f, block);
                        h = block * 0.6f + 0.2f;
                        crev = Lerp(0.6f, 1f, block);
                        break;
                    }
                }
                wall -= grime;
                float r = wall, g = wall * 0.98f, b = wall * 0.95f;
                float smooth = 0.3f;

                // A floor band at the slab (not on brick, which runs on).
                if (style != 0 && MathF.Abs(v - Slab) < 0.012f)
                {
                    r = g = b = wall * 0.88f;
                    h = 0.75f;
                }

                // Lintel and sill around the window.
                bool inX = u > WinL - 0.02f && u < WinR + 0.02f;
                if (inX && v > WinT && v < WinT + 0.045f)
                {
                    r = g = b = 0.8f;
                    h = 0.8f;
                }
                if (u > WinL - 0.035f && u < WinR + 0.035f && v < WinB && v > WinB - 0.03f)
                {
                    r = g = b = 0.84f;
                    h = 0.9f;
                }
                // The window: a light frame, a mullion and transom, dark glass with a sky sheen.
                if (u > WinL && u < WinR && v > WinB && v < WinT)
                {
                    float lu = (u - WinL) / (WinR - WinL), lv = (v - WinB) / (WinT - WinB);
                    bool frame = lu < 0.035f || lu > 0.965f || lv < 0.035f || lv > 0.965f || MathF.Abs(lu - 0.5f) < 0.016f || MathF.Abs(lv - 0.7f) < 0.014f;
                    if (frame)
                    {
                        r = g = b = 0.82f;
                        h = 0.45f;
                    }
                    else
                    {
                        // (No curtains: a tiled texture would hang the same ones in every window.)
                        float sheen = 0.12f + lv * 0.1f + (1f - lu) * 0.03f + Fbm(u, v, 3, 2, 306) * 0.05f;
                        r = sheen * 0.8f;
                        g = sheen * 0.9f;
                        b = sheen * 1.05f;
                        h = 0.1f;
                        smooth = 0.95f;
                        crev = 0.9f;
                    }
                }
                c.R[i] = r;
                c.G[i] = g;
                c.B[i] = b;
                c.H[i] = h;
                c.Crevice[i] = crev;
                c.Smooth[i] = smooth;
            });
            return c.Finish(0.9f, 1.5f, 2);
        }

        /// <summary>Short-pile carpet: a soft weave with a faint pattern.</summary>
        public static SurfaceMaps Carpet(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float pile = Fbm(u, v, 64, 2, 310) * 0.12f + Fbm(u, v, 8, 3, 311) * 0.06f;
                float pattern = (MathF.Sin(u * MathF.PI * 16f) * MathF.Sin(v * MathF.PI * 16f)) * 0.03f;
                c.Shade(i, 0.82f + pile + pattern);
                c.H[i] = 0.5f + pile;
                c.Smooth[i] = 0.05f;
            });
            return c.Finish(0.3f, 1f, 1);
        }

        /// <summary>Sheet vinyl: smooth with faint speckles.</summary>
        public static SurfaceMaps Lino(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float speck = Hash01((int)(u * n), (int)(v * n), 312) < 0.04f ? -0.08f : 0f;
                c.Shade(i, 0.88f + Fbm(u, v, 4, 3, 313) * 0.04f + speck);
                c.H[i] = 0.5f;
                c.Smooth[i] = 0.6f;
            });
            return c.Finish(0.2f, 1f, 1);
        }
    }
}
