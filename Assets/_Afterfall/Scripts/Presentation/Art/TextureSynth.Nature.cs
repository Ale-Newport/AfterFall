using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>Texture recipes for resource nodes: birch bark, cut end grain and iron ore.</summary>
    public static partial class TextureSynth
    {
        /// <summary>
        /// Birch bark: chalky white with dark horizontal lenticels, a few dark scars and warm peeling
        /// patches. Lenticels run along u (horizontal on a standing trunk).
        /// </summary>
        public static SurfaceMaps BirchBark(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                // Lenticels: short horizontal dashes of varied length, denser in bands.
                float dashN = Fbm(u, v, 6, 64, 3, 301, 0.45f);
                float gate = SmoothStep(-0.25f, 0.25f, Fbm(u, v, 2, 7, 2, 302));
                float lenticel = SmoothStep(0.16f, 0.3f, dashN) * gate;
                float fine = SmoothStep(0.3f, 0.42f, Fbm(u, v, 14, 110, 2, 307)) * 0.6f;
                lenticel = MathF.Max(lenticel, fine);
                // Larger dark scars where branches fell off: wide, flat ellipses.
                Voronoi(u * 2f, v * 5f, 2, 5, 303, out float s1, out _, out int sid);
                float scar = Hash01(sid, 304) < 0.35f ? 1f - SmoothStep(0.1f, 0.2f, s1) : 0f;
                // Peeling outer layer: small warm patches with a thin dark rim.
                float peelN = Fbm(u, v, 5, 14, 3, 305);
                float peel = SmoothStep(0.4f, 0.44f, peelN);
                float rim = SmoothStep(0.36f, 0.4f, peelN) * (1f - peel);
                float chalk = Fbm(u, v, 12, 24, 3, 306);
                float lum = (0.97f + chalk * 0.06f) * (1f - lenticel * 0.78f) * (1f - scar * 0.8f) * (1f - rim * 0.25f);
                c.Tint(i, lum * (1f + peel * 0.04f), lum * (1f - peel * 0.02f), lum * (1f - peel * 0.08f - chalk * 0.02f));
                c.H[i] = 0.55f + chalk * 0.06f - lenticel * 0.25f - scar * 0.3f + peel * 0.06f;
                c.Crevice[i] = 1f - lenticel * 0.35f - scar * 0.3f;
                c.Smooth[i] = 0.8f - lenticel * 0.4f + peel * 0.2f;
            });
            return c.Finish(0.9f, 2f, 2);
        }

        /// <summary>
        /// Sawn or chopped end grain: growth rings centred on the tile corner (map it in object space
        /// with the trunk axis at the origin), darker heartwood, radial drying checks and tool marks.
        /// </summary>
        public static SurfaceMaps EndGrain(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float du = MathF.Min(u, 1f - u), dv = MathF.Min(v, 1f - v);
                float r = MathF.Sqrt(du * du + dv * dv);
                float warp = Fbm(u, v, 5, 5, 3, 311) * 0.02f;
                float rings = Frac((r + warp) * 64f);
                float late = SmoothStep(0.62f, 0.9f, rings) * (1f - SmoothStep(0.93f, 1f, rings));
                float heart = 1f - SmoothStep(0.12f, 0.2f, r + warp * 2f);
                // Radial checks: a few cracks from the pith outward.
                float ang = MathF.Atan2(u > 0.5f ? u - 1f : u, v > 0.5f ? v - 1f : v);
                float crack = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float a0 = Hash01(k, 312) * 6.2831853f - 3.1415926f;
                    float da = MathF.Abs(ang - a0);
                    da = MathF.Min(da, 6.2831853f - da);
                    float width = 0.006f / MathF.Max(0.02f, r);
                    float len = 0.12f + Hash01(k, 313) * 0.16f;
                    crack = MathF.Max(crack, (1f - SmoothStep(0f, width, da)) * (1f - SmoothStep(len * 0.7f, len, r)) * SmoothStep(0.01f, 0.03f, r));
                }
                float marks = Fbm(u, v, 2, 40, 2, 314) * 0.5f + 0.5f;
                float fibre = Fbm(u, v, 48, 48, 2, 315);
                float lum = (0.96f + fibre * 0.06f + (marks - 0.5f) * 0.08f) * (1f - late * 0.4f) * (1f - crack * 0.75f);
                float warm = heart * 0.14f;
                c.Tint(i, lum * (1f - heart * 0.08f + warm * 0.4f), lum * (1f - heart * 0.14f), lum * (1f - heart * 0.24f));
                c.H[i] = 0.55f + late * 0.06f + fibre * 0.05f + (marks - 0.5f) * 0.08f - crack * 0.4f;
                c.Crevice[i] = 1f - crack * 0.5f;
                c.Smooth[i] = 0.7f + late * 0.2f;
            });
            return c.Finish(0.8f, 2f, 2);
        }

        /// <summary>
        /// Weathered, corroded sheet steel (wrecks, scrap): mottled orange rust over dark mill scale,
        /// streaks running down, flaking patches with lifted rims. Unlike <see cref="Rust"/> it has no
        /// pitting, so large panels do not look perforated. The hue lives in the map; tint grey-brown.
        /// </summary>
        public static SurfaceMaps Corrosion(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float big = Fbm(u, v, 3, 3, 4, 401);
                float rust = SmoothStep(-0.35f, 0.35f, big + Fbm(u, v, 12, 12, 3, 402) * 0.4f);
                float streak = MathF.Max(0f, Fbm(u, v, 22, 2, 3, 403)) * SmoothStep(-0.1f, 0.4f, Fbm(u, v, 3, 3, 2, 404));
                float flakeN = Fbm(u, v, 7, 7, 3, 405);
                float flake = SmoothStep(0.3f, 0.36f, flakeN);
                float rim = SmoothStep(0.25f, 0.3f, flakeN) * (1f - flake);
                float grain = Fbm(u, v, 48, 48, 2, 406);
                float lum = (0.82f + rust * 0.22f + grain * 0.07f * (0.5f + rust)) * (1f - streak * 0.3f) * (1f + flake * 0.08f) * (1f - rim * 0.2f);
                c.Tint(i, lum * (1f + rust * 0.32f + flake * 0.05f), lum * (1f - rust * 0.04f), lum * (1f - rust * 0.32f - flake * 0.05f));
                c.H[i] = 0.5f + big * 0.08f + grain * 0.1f * rust + flake * 0.06f + rim * 0.04f;
                c.Crevice[i] = 1f - rim * 0.3f - streak * 0.15f;
                c.Smooth[i] = 0.75f - rust * 0.55f + flake * 0.1f;
            });
            return c.Finish(1f, 2f, 2);
        }

        /// <summary>
        /// Iron ore: dark, dense rock with rust-orange limonite stains, ochre veins and a scatter of
        /// glinting metallic grains. The hue lives in the map; tint it a dark grey-brown.
        /// </summary>
        public static SurfaceMaps IronOre(int n)
        {
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float big = Fbm(u, v, 3, 4, 321);
                float veinN = Ridged(u + Fbm(u, v, 2, 3, 322) * 0.15f, v, 3, 3, 3, 323);
                float vein = SmoothStep(0.72f, 0.9f, veinN);
                float stain = SmoothStep(0.0f, 0.4f, Fbm(u, v, 4, 4, 324)) * 0.8f + vein * 0.5f;
                stain = Clamp01(stain);
                Voronoi(u, v, 40, 325, out float g1, out _, out int gid);
                float grain = Hash01(gid, 326) < 0.1f ? 1f - SmoothStep(0.05f, 0.25f, g1) : 0f;
                float crack = SmoothStep(0.88f, 0.97f, Ridged(u, v, 5, 5, 2, 327));
                float lum = (0.8f + big * 0.16f) * (1f - crack * 0.4f) * (1f + grain * 0.25f) * (1f + stain * 0.1f);
                c.Tint(i, lum * (1f + stain * 0.75f), lum * (1f - stain * 0.02f), lum * (1f - stain * 0.42f + grain * 0.1f));
                c.H[i] = 0.5f + big * 0.4f - crack * 0.4f + vein * 0.08f + grain * 0.05f;
                c.Crevice[i] = 1f - crack * 0.5f;
                c.Smooth[i] = 0.45f + grain * 0.55f - stain * 0.2f;
            });
            return c.Finish(1.3f, 2.5f, 3);
        }
    }
}
