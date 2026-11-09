using System;

namespace Afterfall.Presentation.Art
{
    public static partial class TextureSynth
    {
        /// <summary>
        /// Weathered sawn timber with the grain along u: eroded earlywood leaves the latewood lines
        /// raised and dark, long drying checks follow the grain, the odd knot, and grey sun-bleached
        /// patches. Used for every board, beam and post a survivor saws (geometry gives the board edges).
        /// </summary>
        public static SurfaceMaps Timber(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float warp = Fbm(u, v, 1, 2, 3, 301) * 1.3f + Fbm(u, v, 3, 3, 2, 302) * 0.12f;
                float rings = Frac(v * 16f + warp * 2.2f);
                float contrast = 0.6f + 0.4f * Fbm(u, v, 2, 3, 2, 308);
                float late = SmoothStep(0.6f, 0.8f, rings) * (1f - SmoothStep(0.86f, 0.98f, rings)) * contrast;
                float fibre = Fbm(u, v, 3, 96, 2, 303);
                float streak = Fbm(u, v, 2, 24, 3, 304);
                float check = SmoothStep(0.95f, 0.99f, Ridged(u, v + warp * 0.01f, 1, 16, 2, 305)) * SmoothStep(0.0f, 0.3f, Fbm(u, v, 3, 2, 306));
                float bleach = SmoothStep(0.1f, 0.6f, Fbm(u, v, 2, 3, 3, 307));
                float knotD = TimberKnot(u, v);
                float knot = 1f - SmoothStep(0.008f, 0.03f, knotD);
                float knotRing = SmoothStep(0.02f, 0.035f, knotD) * (1f - SmoothStep(0.035f, 0.07f, knotD));
                float lum = (0.9f + streak * 0.12f + fibre * 0.06f) * (1f - late * 0.3f) * (1f - check * 0.7f) * (1f - knot * 0.55f) * (1f - knotRing * 0.2f);
                // Bleached wood goes silver-grey: less red, a touch cooler.
                c.Tint(i, lum * (1f - bleach * 0.06f), lum * (1f - bleach * 0.02f), lum * (1f + bleach * 0.02f));
                c.H[i] = 0.55f + late * 0.2f + fibre * 0.07f + streak * 0.05f - check * 0.5f - knot * 0.05f + knotRing * 0.06f;
                c.Crevice[i] = 1f - check * 0.6f;
                c.Smooth[i] = 0.7f + late * 0.2f - bleach * 0.15f;
            });
            return c.Finish(1.1f, 2.2f, 2);
        }

        /// <summary>Distance to the nearest of three knots per tile (tile units, stretched along the grain).</summary>
        private static float TimberKnot(float u, float v)
        {
            float best = 9f;
            for (int k = 0; k < 3; k++)
            {
                float ku = Hash01(k, 311), kv = Hash01(k, 312);
                float du = Frac(u - ku + 0.5f) - 0.5f, dv = Frac(v - kv + 0.5f) - 0.5f;
                best = MathF.Min(best, MathF.Sqrt(du * du * 0.35f + dv * dv));
            }
            return best;
        }
    }
}
