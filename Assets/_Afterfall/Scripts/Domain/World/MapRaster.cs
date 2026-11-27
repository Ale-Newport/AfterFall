using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// A view of the map on screen: which world point is at the centre, how many metres one
    /// pixel covers, and the map's rotation (0 = north up; a heading-up minimap passes the
    /// player's heading). Screen pixels grow right and down, world +Z is north.
    /// </summary>
    public struct MapView
    {
        public float CenterX;
        public float CenterZ;
        public float MetersPerPixel;
        public float RotationDegrees;
        public float Width;
        public float Height;

        public MapView(float centerX, float centerZ, float metersPerPixel, float width, float height, float rotationDegrees = 0f)
        {
            CenterX = centerX;
            CenterZ = centerZ;
            MetersPerPixel = metersPerPixel;
            Width = width;
            Height = height;
            RotationDegrees = rotationDegrees;
        }

        /// <summary>Screen pixel of a world point.</summary>
        public void WorldToScreen(float x, float z, out float px, out float py)
        {
            double r = RotationDegrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            float dx = x - CenterX, dz = z - CenterZ;
            // Rotating by the heading (counter-clockwise) brings the direction faced to the top.
            float rx = dx * c - dz * s;
            float rz = dx * s + dz * c;
            px = Width * 0.5f + rx / MetersPerPixel;
            py = Height * 0.5f - rz / MetersPerPixel;
        }

        /// <summary>World point under a screen pixel.</summary>
        public void ScreenToWorld(float px, float py, out float x, out float z)
        {
            double r = RotationDegrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            float rx = (px - Width * 0.5f) * MetersPerPixel;
            float rz = (Height * 0.5f - py) * MetersPerPixel;
            x = CenterX + rx * c + rz * s;
            z = CenterZ - rx * s + rz * c;
        }

        /// <summary>True if a screen point lies inside the view (with an optional inset).</summary>
        public bool Contains(float px, float py, float inset = 0f) => px >= inset && py >= inset && px <= Width - inset && py <= Height - inset;
    }

    /// <summary>
    /// Map features precomputed from the layout once per world: road segments, building
    /// footprints and a forest-density grid. Rendering any region reads only this and the height field.
    /// </summary>
    public sealed class MapFeatures
    {
        public const float ForestCell = 8f;

        internal struct Segment
        {
            public float Ax, Az, Bx, Bz, HalfWidth;
            public bool Paved;
        }

        internal struct Footprint
        {
            public float Cx, Cz, Hx, Hz, Cos, Sin;
            public byte Kind;
        }

        internal readonly List<Segment> Roads = new List<Segment>();
        internal readonly List<Footprint> Buildings = new List<Footprint>();
        internal float[] Forest;
        internal int ForestCells;

        /// <summary>Prop kinds drawn as building footprints.</summary>
        public static readonly HashSet<string> BuildingKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "house", "clinic", "shop", "garage", "warehouse", "barn", "shack", "silo", "canopy", "transformer", "tent", "apartment", "townhouse", "store", "office", "church", "school", "hall"
        };

        public int RoadSegmentCount => Roads.Count;
        public int BuildingCount => Buildings.Count;

        public static MapFeatures Build(WorldLayout layout)
        {
            var f = new MapFeatures();
            foreach (RoadPath r in layout.Roads)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                    f.Roads.Add(new Segment { Ax = r.Points[i].X, Az = r.Points[i].Z, Bx = r.Points[i + 1].X, Bz = r.Points[i + 1].Z, HalfWidth = r.Width * 0.5f, Paved = r.Paved });
            foreach (PropPlacement p in layout.Props)
            {
                if (!BuildingKinds.Contains(p.Kind)) continue;
                double a = p.Yaw * Math.PI / 180.0;
                f.Buildings.Add(new Footprint
                {
                    Cx = p.Position.X, Cz = p.Position.Z, Hx = p.Size.X * 0.5f, Hz = p.Size.Z * 0.5f,
                    Cos = (float)Math.Cos(a), Sin = (float)Math.Sin(a), Kind = (byte)(p.Kind == "canopy" || p.Kind == "tent" ? 1 : 0)
                });
            }
            int cells = Math.Max(1, (int)Math.Ceiling(layout.Size / ForestCell));
            f.ForestCells = cells;
            var counts = new float[cells * cells];
            foreach (NodeSeed n in layout.Nodes)
            {
                if (!n.DefId.StartsWith("tree", StringComparison.Ordinal)) continue;
                int cx = (int)(n.Position.X / ForestCell), cz = (int)(n.Position.Z / ForestCell);
                if (cx < 0 || cz < 0 || cx >= cells || cz >= cells) continue;
                counts[cz * cells + cx] += n.Scale;
            }
            // A light blur turns scattered trees into woodland patches, as on a printed map.
            f.Forest = new float[cells * cells];
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    float sum = 0f, wsum = 0f;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, zz = z + dz;
                            if (xx < 0 || zz < 0 || xx >= cells || zz >= cells) continue;
                            float w = dx == 0 && dz == 0 ? 2f : 1f;
                            sum += counts[zz * cells + xx] * w;
                            wsum += w;
                        }
                    }
                    f.Forest[z * cells + x] = Math.Min(1f, sum / wsum / 0.9f);
                }
            }
            return f;
        }

        /// <summary>Forest density 0..1 at a world point (bilinear between cell centres).</summary>
        public float ForestAt(float x, float z)
        {
            float fx = x / ForestCell - 0.5f, fz = z / ForestCell - 0.5f;
            int x0 = (int)Math.Floor(fx), z0 = (int)Math.Floor(fz);
            float tx = fx - x0, tz = fz - z0;
            float a = Cell(x0, z0), b = Cell(x0 + 1, z0), c = Cell(x0, z0 + 1), d = Cell(x0 + 1, z0 + 1);
            float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
            return ab + (cd - ab) * tz;
        }

        private float Cell(int x, int z)
        {
            x = x < 0 ? 0 : x >= ForestCells ? ForestCells - 1 : x;
            z = z < 0 ? 0 : z >= ForestCells ? ForestCells - 1 : z;
            return Forest[z * ForestCells + x];
        }
    }

    /// <summary>
    /// Renders a cartographic map of any rectangle of the world from authoritative layout data
    /// (heights, water, forests, roads, buildings) into RGBA bytes: hypsometric tints with hill
    /// shading and contour lines, woodland, water with shorelines, cased roads and building
    /// footprints. Pure C#, deterministic and thread-safe, so map tiles are rendered on worker
    /// threads and the result is the same in the game, in tests and in the headless preview.
    /// Row 0 is the southern edge (texture order).
    /// </summary>
    public static class MapRasterizer
    {
        public const float ContourInterval = 5f;

        private struct Rgb
        {
            public float R, G, B;

            public Rgb(float r, float g, float b)
            {
                R = r;
                G = g;
                B = b;
            }

            public static Rgb Lerp(Rgb a, Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        private static readonly Rgb Lowland = new Rgb(214, 222, 182);
        private static readonly Rgb Upland = new Rgb(222, 208, 164);
        private static readonly Rgb Highland = new Rgb(196, 176, 146);
        private static readonly Rgb Summit = new Rgb(226, 222, 214);
        private static readonly Rgb Woods = new Rgb(126, 166, 112);
        private static readonly Rgb Contour = new Rgb(150, 116, 82);
        private static readonly Rgb Water = new Rgb(142, 186, 222);
        private static readonly Rgb DeepWater = new Rgb(104, 152, 204);
        private static readonly Rgb Shore = new Rgb(70, 112, 160);
        private static readonly Rgb RoadCasing = new Rgb(92, 84, 74);
        private static readonly Rgb PavedFill = new Rgb(250, 238, 196);
        private static readonly Rgb DirtFill = new Rgb(204, 170, 120);
        private static readonly Rgb BuildingFill = new Rgb(146, 120, 108);
        private static readonly Rgb BuildingEdge = new Rgb(84, 64, 56);
        private static readonly Rgb CanopyFill = new Rgb(186, 176, 164);

        /// <summary>
        /// Renders the world rectangle [x0,x1]×[z0,z1] into a <paramref name="width"/>×<paramref name="height"/> RGBA image.
        /// With <paramref name="parallel"/> off it uses only the calling thread (the game renders on one
        /// low-priority thread so it never competes with the frame).
        /// </summary>
        public static byte[] Render(WorldLayout layout, MapFeatures features, float x0, float z0, float x1, float z1, int width, int height, bool parallel = true)
        {
            var px = new byte[width * height * 4];
            float sx = (x1 - x0) / width, sz = (z1 - z0) / height;
            float water = layout.WaterLevel;
            float maxH = layout.MaxHeight;

            void Row(int row)
            {
                float wz = z0 + (row + 0.5f) * sz;
                for (int col = 0; col < width; col++)
                {
                    float wx = x0 + (col + 0.5f) * sx;
                    Rgb c = Ground(layout, features, wx, wz, sx, sz, water, maxH);
                    int o = (row * width + col) * 4;
                    px[o] = Byte(c.R);
                    px[o + 1] = Byte(c.G);
                    px[o + 2] = Byte(c.B);
                    px[o + 3] = 255;
                }
            }
            if (parallel) Parallel.For(0, height, Row);
            else
                for (int row = 0; row < height; row++) Row(row);

            float pixel = Math.Max(sx, sz);
            foreach (MapFeatures.Footprint b in features.Buildings) DrawFootprint(px, width, height, x0, z0, sx, sz, b, pixel);
            // Casings first, then fills, so junctions read as one network.
            foreach (MapFeatures.Segment s in features.Roads)
                if (s.Paved) DrawSegment(px, width, height, x0, z0, sx, sz, s, s.HalfWidth + Math.Max(1.2f, pixel * 1.2f), RoadCasing);
            foreach (MapFeatures.Segment s in features.Roads)
                DrawSegment(px, width, height, x0, z0, sx, sz, s, Math.Max(s.HalfWidth * (s.Paved ? 0.8f : 0.55f), pixel * (s.Paved ? 0.9f : 0.6f)), s.Paved ? PavedFill : DirtFill);
            return px;
        }

        private static Rgb Ground(WorldLayout layout, MapFeatures f, float x, float z, float sx, float sz, float water, float maxH)
        {
            float h = layout.HeightAt(x, z);
            if (h < water)
            {
                float depth = Math.Min(1f, (water - h) / 6f);
                Rgb w = Rgb.Lerp(Water, DeepWater, depth);
                return Rgb.Lerp(Shore, w, Math.Min(1f, (water - h) / 0.5f + 0.15f));
            }

            // Hypsometric tint by height above the water.
            float t = Math.Max(0f, Math.Min(1f, (h - water) / Math.Max(1f, maxH - water)));
            Rgb c = t < 0.35f ? Rgb.Lerp(Lowland, Upland, t / 0.35f)
                : t < 0.75f ? Rgb.Lerp(Upland, Highland, (t - 0.35f) / 0.4f)
                : Rgb.Lerp(Highland, Summit, (t - 0.75f) / 0.25f);

            float forest = f.ForestAt(x, z);
            if (forest > 0.05f)
            {
                // Woodland with a faint stipple, like a printed map's tree pattern.
                float stipple = Noise.Hash01(911, (int)Math.Floor(x / 3f), (int)Math.Floor(z / 3f)) * 0.12f;
                c = Rgb.Lerp(c, Woods, Math.Min(0.85f, forest * 0.95f) * (0.94f + stipple));
            }

            // Hill shading lit from the north-west.
            float d = Math.Max(1f, Math.Max(sx, sz));
            float dx = (layout.HeightAt(x + d, z) - layout.HeightAt(x - d, z)) / (2f * d);
            float dz = (layout.HeightAt(x, z + d) - layout.HeightAt(x, z - d)) / (2f * d);
            float nx = -dx, ny = 1f, nz = -dz;
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            const float lx = -0.5f, ly = 0.75f, lz = 0.43f;
            float shade = (nx * lx + ny * ly + nz * lz) / len;
            float k = 0.78f + shade * 0.3f;
            c = new Rgb(c.R * k, c.G * k, c.B * k);

            // Contours: thin lines every 5 m, stronger every 25 m, sized to the pixel.
            float slope = (float)Math.Sqrt(dx * dx + dz * dz);
            float perPixel = slope * Math.Max(sx, sz);
            if (perPixel > 1e-4f && perPixel < ContourInterval * 0.5f)
            {
                float level = h / ContourInterval;
                float frac = Math.Abs(level - (float)Math.Round(level));
                float widthLevels = perPixel / ContourInterval * 0.6f;
                float line = 1f - Smooth(widthLevels * 0.5f, widthLevels * 1.5f, frac);
                bool major = Math.Abs(Math.Round(level) % 5) < 0.5;
                c = Rgb.Lerp(c, Contour, line * (major ? 0.55f : 0.28f));
            }

            // A thin dark shoreline just above the water.
            if (h < water + 0.35f) c = Rgb.Lerp(c, Shore, 0.6f);
            return c;
        }

        private static void DrawSegment(byte[] px, int w, int h, float x0, float z0, float sx, float sz, MapFeatures.Segment s, float half, Rgb color)
        {
            float pad = half + Math.Max(sx, sz);
            int c0 = Clamp((int)Math.Floor((Math.Min(s.Ax, s.Bx) - pad - x0) / sx), w), c1 = Clamp((int)Math.Ceiling((Math.Max(s.Ax, s.Bx) + pad - x0) / sx), w);
            int r0 = Clamp((int)Math.Floor((Math.Min(s.Az, s.Bz) - pad - z0) / sz), h), r1 = Clamp((int)Math.Ceiling((Math.Max(s.Az, s.Bz) + pad - z0) / sz), h);
            float aa = Math.Max(sx, sz) * 0.75f;
            for (int row = r0; row < r1; row++)
            {
                float wz = z0 + (row + 0.5f) * sz;
                for (int col = c0; col < c1; col++)
                {
                    float wx = x0 + (col + 0.5f) * sx;
                    float dist = WorldLayout.DistanceToSegmentXZ(wx, wz, new Vec3(s.Ax, 0, s.Az), new Vec3(s.Bx, 0, s.Bz), out _);
                    float cover = 1f - Smooth(half - aa, half + aa, dist);
                    if (cover > 0f) Blend(px, (row * w + col) * 4, color, cover);
                }
            }
        }

        private static void DrawFootprint(byte[] px, int w, int h, float x0, float z0, float sx, float sz, MapFeatures.Footprint b, float pixel)
        {
            float reach = (float)Math.Sqrt(b.Hx * b.Hx + b.Hz * b.Hz) + pixel;
            int c0 = Clamp((int)Math.Floor((b.Cx - reach - x0) / sx), w), c1 = Clamp((int)Math.Ceiling((b.Cx + reach - x0) / sx), w);
            int r0 = Clamp((int)Math.Floor((b.Cz - reach - z0) / sz), h), r1 = Clamp((int)Math.Ceiling((b.Cz + reach - z0) / sz), h);
            // Tiny buildings still show as at least a pixel or so.
            float hx = Math.Max(b.Hx, pixel * 0.7f), hz = Math.Max(b.Hz, pixel * 0.7f);
            float edge = Math.Max(0.35f, pixel * 0.9f);
            float aa = pixel * 0.6f;
            for (int row = r0; row < r1; row++)
            {
                float wz = z0 + (row + 0.5f) * sz - b.Cz;
                for (int col = c0; col < c1; col++)
                {
                    float wx = x0 + (col + 0.5f) * sx - b.Cx;
                    // Into the footprint's frame (inverse of the yaw rotation).
                    float lx = Math.Abs(wx * b.Cos - wz * b.Sin), lz = Math.Abs(wx * b.Sin + wz * b.Cos);
                    float outside = Math.Max(lx - hx, lz - hz);
                    float cover = 1f - Smooth(-aa, aa, outside);
                    if (cover <= 0f) continue;
                    bool rim = outside > -edge;
                    Blend(px, (row * w + col) * 4, b.Kind == 1 ? CanopyFill : rim ? BuildingEdge : BuildingFill, cover);
                }
            }
        }

        private static void Blend(byte[] px, int o, Rgb c, float a)
        {
            px[o] = Byte(px[o] + (c.R - px[o]) * a);
            px[o + 1] = Byte(px[o + 1] + (c.G - px[o + 1]) * a);
            px[o + 2] = Byte(px[o + 2] + (c.B - px[o + 2]) * a);
        }

        private static int Clamp(int v, int max) => v < 0 ? 0 : v > max ? max : v;
        private static byte Byte(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f));

        private static float Smooth(float e0, float e1, float x)
        {
            float t = (x - e0) / (e1 - e0);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3f - 2f * t);
        }
    }
}
