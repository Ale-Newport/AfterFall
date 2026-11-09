using System;

namespace Afterfall.Presentation.Art
{
    public static partial class TextureSynth
    {
        /// <summary>Hair: fine strands along v gathered into clumps, darker between clumps.</summary>
        public static SurfaceMaps HairStrands(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float warp = Fbm(u, v, 3, 2, 2, 301) * 0.06f;
                float clump = Fbm(u + warp, v, 12, 1, 2, 302);
                float strands = Fbm(u + warp * 1.5f, v, 72, 2, 2, 303);
                float fly = Scratches(u, v, 304, 6);
                float lum = 0.84f + clump * 0.2f + strands * 0.12f + fly * 0.08f;
                c.Tint(i, lum * (1f + clump * 0.03f), lum, lum * (1f - clump * 0.03f));
                c.H[i] = 0.5f + clump * 0.35f + strands * 0.18f;
                c.Crevice[i] = 0.85f + clump * 0.15f;
                c.Smooth[i] = 0.75f + strands * 0.25f;
            });
            return c.Finish(1.1f, 1.4f, 1);
        }

        /// <summary>Knitted wool (beanies, scarves): rows of interlocking stitches along u.</summary>
        public static SurfaceMaps Knit(int n)
        {
            const int cols = 14, rows = 18;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float fu = u * cols, fv = v * rows;
                float lx = fu - MathF.Floor(fu), ly = fv - MathF.Floor(fv);
                // Each stitch is a pair of slanted loops forming a V.
                float slant = lx < 0.5f ? (lx * 2f - ly) : ((1f - lx) * 2f - ly);
                float loop = 1f - MathF.Min(1f, MathF.Abs(slant) * 1.6f);
                float gap = SmoothStep(0.0f, 0.12f, MathF.Min(lx, 1f - lx));
                float fuzz = Fbm(u, v, 32, 2, 305);
                float lum = 0.82f + loop * 0.2f * gap + fuzz * 0.05f;
                c.Shade(i, lum);
                c.H[i] = loop * 0.6f * gap + fuzz * 0.08f;
                c.Crevice[i] = Lerp(0.7f, 1f, gap);
                c.Smooth[i] = 0.6f + loop * 0.3f;
            });
            return c.Finish(1f, 1.5f, 1);
        }
    }
}
