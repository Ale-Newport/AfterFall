using System;

namespace Afterfall.Presentation.Art
{
    // Recipes for the point-of-interest buildings (see PropSurfaces).
    public static partial class TextureSynth
    {
        /// <summary>Roll-up door curtain: interlocking horizontal steel slats with dents, scuffs and grime.</summary>
        public static SurfaceMaps RollUpSlats(int n)
        {
            const int slats = 16;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float fv = v * slats;
                int row = (int)MathF.Floor(fv);
                float ly = fv - row;
                float bulge = MathF.Sin(ly * 3.14159f);
                float joint = 1f - SmoothStep(0f, 0.12f, MathF.Min(ly, 1f - ly));
                float dent = Fbm(u, v, 3, 6, 3, 301 + row % 3);
                float grime = MathF.Max(0f, Fbm(u, v, 22, 1, 2, 302)) * SmoothStep(-0.2f, 0.4f, Fbm(u, v, 2, 2, 303));
                float scratch = Scratches(u, v, 304, 12);
                float lum = (0.96f + dent * 0.04f + bulge * 0.05f - grime * 0.22f) * (1f - joint * 0.35f) * (1f - scratch * 0.2f);
                c.Tint(i, lum, lum * 0.99f, lum * (0.97f - grime * 0.05f));
                c.H[i] = 0.35f + bulge * 0.4f - joint * 0.3f + dent * 0.04f;
                c.Crevice[i] = 1f - joint * 0.45f;
                c.Smooth[i] = 0.85f - grime * 0.4f - scratch * 0.2f;
            });
            return c.Finish(1.2f, 1.6f, 1);
        }

        /// <summary>Checkered vinyl floor tiles (30 cm) with grout, scuffs, grime and a few cracked tiles.</summary>
        public static SurfaceMaps FloorTiles(int n)
        {
            const int tiles = 8;
            var c = new Canvas(n);
            float grout = 1.2f / n * tiles;
            c.Each((i, u, v) =>
            {
                float fu = u * tiles, fv = v * tiles;
                int col = (int)MathF.Floor(fu), row = (int)MathF.Floor(fv);
                float lx = fu - col, ly = fv - row;
                float edge = MathF.Min(MathF.Min(lx, 1f - lx), MathF.Min(ly, 1f - ly));
                float tile = SmoothStep(grout * 0.4f, grout * 1.4f, edge);
                int id = row * 13 + col;
                float tone = ((row + col) & 1) == 0 ? 1.0f : 0.76f;
                tone *= 0.95f + Hash01(id, 311) * 0.08f;
                float scuff = SmoothStep(0.1f, 0.6f, Fbm(u, v, 4, 4, 312));
                float dirt = Fbm(u, v, 3, 3, 313);
                bool cracked = Hash01(id, 314) < 0.05f;
                float crack = cracked ? (1f - SmoothStep(0.004f, 0.012f, MathF.Abs((lx - 0.5f) * 0.8f - (ly - 0.5f) + Fbm(u, v, 16, 2, 315) * 0.05f))) * SmoothStep(0.05f, 0.2f, edge) : 0f;
                float lum = tile * tone * (1f - scuff * 0.1f + dirt * 0.06f) * (1f - crack * 0.5f) + (1f - tile) * 0.5f;
                c.Tint(i, lum, lum * 0.985f, lum * 0.95f);
                c.H[i] = tile * (0.6f + dirt * 0.02f) - crack * 0.2f;
                c.Crevice[i] = Lerp(0.6f, 1f, tile) * (1f - crack * 0.4f);
                c.Smooth[i] = tile * (0.9f - scuff * 0.5f) + 0.1f;
            });
            return c.Finish(0.8f, 1.5f, 1);
        }

        /// <summary>Suspended ceiling: 60 cm fissured tiles in a light T-bar grid, with faint water stains.</summary>
        public static SurfaceMaps CeilingTiles(int n)
        {
            const int tiles = 4;
            var c = new Canvas(n);
            float bar = 2.2f / n * tiles;
            c.Each((i, u, v) =>
            {
                float fu = u * tiles, fv = v * tiles;
                int col = (int)MathF.Floor(fu), row = (int)MathF.Floor(fv);
                float lx = fu - col, ly = fv - row;
                float edge = MathF.Min(MathF.Min(lx, 1f - lx), MathF.Min(ly, 1f - ly));
                float grid = 1f - SmoothStep(bar * 0.5f, bar * 1.1f, edge);
                int id = row * tiles + col;
                float fissure = Hash01((int)(u * n), (int)(v * n), 321) < 0.08f ? 1f : 0f;
                float stain = SmoothStep(0.25f, 0.6f, Fbm(u, v, 3, 3, 322));
                float tone = 0.96f + Hash01(id, 323) * 0.06f;
                float lum = tone * (1f - fissure * 0.1f) * (1f - stain * 0.1f);
                c.R[i] = Lerp(lum, 0.95f, grid);
                c.G[i] = Lerp(lum * (1f - stain * 0.03f), 0.95f, grid);
                c.B[i] = Lerp(lum * (1f - stain * 0.08f), 0.93f, grid);
                c.H[i] = 0.5f - fissure * 0.05f + grid * 0.25f;
                c.Smooth[i] = 0.4f + grid * 0.5f;
            });
            return c.Finish(0.6f, 1.2f, 1);
        }

        /// <summary>Quarried sedimentary rock: horizontal beds of varied tone, fine laminae, vertical joints and drill scars.</summary>
        public static SurfaceMaps QuarryRock(int n)
        {
            const int beds = 5, joints = 3;
            var c = new Canvas(n);
            c.Each((i, u, v) =>
            {
                float warp = Fbm(u, v, 2, 2, 3, 331) * 0.07f;
                float bed = (v + warp) * beds;
                int layer = (int)MathF.Floor(bed);
                float lb = bed - layer;
                int lid = ((layer % beds) + beds) % beds;
                float tone = 0.84f + Hash01(lid, 332) * 0.26f;
                float warm = Hash01(lid, 333) * 0.6f;
                float parting = (1f - SmoothStep(0f, 0.06f, MathF.Min(lb, 1f - lb))) * (0.55f + 0.45f * SmoothStep(-0.3f, 0.3f, Fbm(u, v, 6, 2, 2, 341)));
                float lamina = MathF.Sin((v + warp) * 70f) * 0.5f + 0.5f;
                // Vertical joints crossing a whole bed, offset from bed to bed.
                float ju = u * joints + Hash01(lid, 334) + Fbm(u, v, 2, 10, 3, 335) * 0.3f;
                int seg = (int)MathF.Floor(ju);
                float jd = MathF.Min(Frac(ju), 1f - Frac(ju)) / joints;
                float joint = Hash01(lid * 31 + ((seg % joints) + joints) % joints, 336) < 0.4f ? (1f - SmoothStep(0.001f, 0.004f, jd)) * SmoothStep(0.05f, 0.3f, Fbm(u, v, 2, 6, 2, 340) + 0.2f) : 0f;
                float rough = Fbm(u, v, 8, 8, 3, 337);
                float blotch = Fbm(u, v, 3, 3, 338);
                float drill = 1f - SmoothStep(0.002f, 0.006f, MathF.Abs(Frac(u * 3f + 0.25f) - 0.5f) / 3f);
                drill *= SmoothStep(0.15f, 0.45f, Fbm(u, v, 3, 1, 339)) * (lb > 0.15f && lb < 0.85f ? 1f : 0f);
                float lum = tone * (0.92f + rough * 0.1f + blotch * 0.08f + lamina * 0.04f) * (1f - parting * 0.38f) * (1f - joint * 0.4f) * (1f - drill * 0.22f);
                c.Tint(i, lum * (1f + warm * 0.12f), lum * (1f + warm * 0.03f), lum * (1f - warm * 0.16f));
                c.H[i] = 0.55f + rough * 0.3f + lamina * 0.04f - parting * 0.35f - joint * 0.4f - drill * 0.15f;
                c.Crevice[i] = (1f - parting * 0.45f) * (1f - joint * 0.5f);
                c.Smooth[i] = 0.7f + rough * 0.2f;
            });
            return c.Finish(1.4f, 2.4f, 3);
        }
    }
}
