using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>A straight-alpha colour (0..1 per channel, sRGB as authored).</summary>
    public struct Rgba
    {
        public float R, G, B, A;

        public Rgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Parses "#RRGGBB" or "#RRGGBBAA"; returns <paramref name="fallback"/> when malformed.</summary>
        public static Rgba Hex(string hex, Rgba fallback = default)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string h = hex.TrimStart('#');
            if (h.Length != 6 && h.Length != 8) return fallback;
            try
            {
                float c(int i) => Convert.ToInt32(h.Substring(i, 2), 16) / 255f;
                return new Rgba(c(0), c(2), c(4), h.Length == 8 ? c(6) : 1f);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        public Rgba Lighter(float k) => new Rgba(R + (1f - R) * k, G + (1f - G) * k, B + (1f - B) * k, A);
        public Rgba Darker(float k) => new Rgba(R * (1f - k), G * (1f - k), B * (1f - k), A);
        public Rgba WithAlpha(float a) => new Rgba(R, G, B, a);

        public static Rgba Lerp(Rgba a, Rgba b, float t) =>
            new Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }

    /// <summary>How a shape is filled: a solid colour or a linear/radial gradient (logical coordinates).</summary>
    public struct Paint
    {
        private enum Kind { Solid, Linear, Radial }

        private Kind _kind;
        private Rgba _a, _b;
        private float _x0, _y0, _x1, _y1;

        public static Paint Solid(Rgba c) => new Paint { _kind = Kind.Solid, _a = c, _b = c };

        /// <summary>Colour <paramref name="a"/> at (x0, y0) blending to <paramref name="b"/> at (x1, y1).</summary>
        public static Paint Linear(Rgba a, Rgba b, float x0, float y0, float x1, float y1) =>
            new Paint { _kind = Kind.Linear, _a = a, _b = b, _x0 = x0, _y0 = y0, _x1 = x1, _y1 = y1 };

        /// <summary>Colour <paramref name="inner"/> at the centre blending to <paramref name="outer"/> at <paramref name="radius"/>.</summary>
        public static Paint Radial(Rgba inner, Rgba outer, float cx, float cy, float radius) =>
            new Paint { _kind = Kind.Radial, _a = inner, _b = outer, _x0 = cx, _y0 = cy, _x1 = radius };

        /// <summary>The house style for volumes: lit from the top left, darker toward the bottom right of the given box.</summary>
        public static Paint Shaded(Rgba c, float left, float top, float right, float bottom, float strength = 0.28f) =>
            Linear(c.Lighter(strength), c.Darker(strength), left, top, right, bottom);

        public static implicit operator Paint(Rgba c) => Solid(c);

        public Rgba Sample(float x, float y)
        {
            switch (_kind)
            {
                case Kind.Linear:
                {
                    float dx = _x1 - _x0, dy = _y1 - _y0;
                    float len2 = dx * dx + dy * dy;
                    float t = len2 > 1e-6f ? ((x - _x0) * dx + (y - _y0) * dy) / len2 : 0f;
                    return Rgba.Lerp(_a, _b, Math.Max(0f, Math.Min(1f, t)));
                }
                case Kind.Radial:
                {
                    float d = MathF.Sqrt((x - _x0) * (x - _x0) + (y - _y0) * (y - _y0));
                    return Rgba.Lerp(_a, _b, Math.Max(0f, Math.Min(1f, d / Math.Max(1e-4f, _x1))));
                }
                default:
                    return _a;
            }
        }
    }

    /// <summary>
    /// A small anti-aliased vector rasteriser for item icons. Shapes are drawn in a logical
    /// 0..100 square (y pointing down, like SVG) at any pixel size, using signed distance
    /// functions for clean edges. Pixels are premultiplied RGBA, row 0 at the top.
    /// Plain C# (no UnityEngine) so icons can be previewed headlessly (tools/dotnet/ArtPreview).
    /// </summary>
    public sealed class IconCanvas
    {
        public const float Units = 100f;

        public readonly int Size;
        public readonly float[] Px;
        private readonly float _scale;
        private bool _erase;

        public IconCanvas(int size)
        {
            Size = size;
            Px = new float[size * size * 4];
            _scale = size / Units;
        }

        /// <summary>A blank canvas of the same size, for drawing a group that gets its own effects.</summary>
        public IconCanvas Layer() => new IconCanvas(Size);

        // ------------------------------------------------------------ shapes

        public void Circle(float cx, float cy, float r, Paint p) =>
            Fill(cx - r, cy - r, cx + r, cy + r, (x, y) => Len(x - cx, y - cy) - r, p);

        public void Ellipse(float cx, float cy, float rx, float ry, float rotDeg, Paint p)
        {
            float c = MathF.Cos(rotDeg * Deg), s = MathF.Sin(rotDeg * Deg);
            float ext = Math.Max(rx, ry);
            Fill(cx - ext, cy - ext, cx + ext, cy + ext, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                float lx = dx * c + dy * s, ly = -dx * s + dy * c;
                float k0 = Len(lx / rx, ly / ry);
                float k1 = Len(lx / (rx * rx), ly / (ry * ry));
                return k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -Math.Min(rx, ry);
            }, p);
        }

        /// <summary>A rectangle centred on (cx, cy), rotated clockwise by <paramref name="rotDeg"/>, with corner radius <paramref name="r"/>.</summary>
        public void RoundRect(float cx, float cy, float w, float h, float r, float rotDeg, Paint p)
        {
            float c = MathF.Cos(rotDeg * Deg), s = MathF.Sin(rotDeg * Deg);
            float hw = w * 0.5f, hh = h * 0.5f;
            r = Math.Min(r, Math.Min(hw, hh));
            float ext = Len(hw, hh);
            Fill(cx - ext, cy - ext, cx + ext, cy + ext, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                float lx = Math.Abs(dx * c + dy * s), ly = Math.Abs(-dx * s + dy * c);
                float qx = lx - (hw - r), qy = ly - (hh - r);
                return Len(Math.Max(qx, 0f), Math.Max(qy, 0f)) + Math.Min(Math.Max(qx, qy), 0f) - r;
            }, p);
        }

        public void Rect(float left, float top, float right, float bottom, Paint p) =>
            RoundRect((left + right) * 0.5f, (top + bottom) * 0.5f, right - left, bottom - top, 0f, 0f, p);

        /// <summary>A filled polygon from x0, y0, x1, y1, ... (any winding, even-odd rule).</summary>
        public void Polygon(Paint p, params float[] xy)
        {
            int n = xy.Length / 2;
            if (n < 3) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, xy[i * 2]);
                maxX = Math.Max(maxX, xy[i * 2]);
                minY = Math.Min(minY, xy[i * 2 + 1]);
                maxY = Math.Max(maxY, xy[i * 2 + 1]);
            }
            Fill(minX, minY, maxX, maxY, (x, y) => PolygonDistance(xy, n, x, y), p);
        }

        /// <summary>A stroke with round caps.</summary>
        public void Line(float x0, float y0, float x1, float y1, float width, Paint p)
        {
            float r = width * 0.5f;
            Fill(Math.Min(x0, x1) - r, Math.Min(y0, y1) - r, Math.Max(x0, x1) + r, Math.Max(y0, y1) + r,
                (x, y) => SegmentDistance(x, y, x0, y0, x1, y1) - r, p);
        }

        /// <summary>Connected round-capped strokes through x0, y0, x1, y1, ...</summary>
        public void Polyline(Paint p, float width, params float[] xy)
        {
            int n = xy.Length / 2;
            if (n < 2) return;
            float r = width * 0.5f;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, xy[i * 2]);
                maxX = Math.Max(maxX, xy[i * 2]);
                minY = Math.Min(minY, xy[i * 2 + 1]);
                maxY = Math.Max(maxY, xy[i * 2 + 1]);
            }
            Fill(minX - r, minY - r, maxX + r, maxY + r, (x, y) =>
            {
                float best = float.MaxValue;
                for (int i = 0; i + 1 < n; i++)
                    best = Math.Min(best, SegmentDistance(x, y, xy[i * 2], xy[i * 2 + 1], xy[i * 2 + 2], xy[i * 2 + 3]));
                return best - r;
            }, p);
        }

        public void Ring(float cx, float cy, float r, float width, Paint p)
        {
            float o = r + width * 0.5f;
            Fill(cx - o, cy - o, cx + o, cy + o, (x, y) => Math.Abs(Len(x - cx, y - cy) - r) - width * 0.5f, p);
        }

        /// <summary>A round-capped circular arc. Angles in degrees, 0 = +x (right), increasing clockwise (y down).</summary>
        public void Arc(float cx, float cy, float r, float width, float startDeg, float sweepDeg, Paint p)
        {
            float o = r + width * 0.5f;
            float a0 = startDeg * Deg, a1 = (startDeg + sweepDeg) * Deg;
            float ex0 = cx + MathF.Cos(a0) * r, ey0 = cy + MathF.Sin(a0) * r;
            float ex1 = cx + MathF.Cos(a1) * r, ey1 = cy + MathF.Sin(a1) * r;
            Fill(cx - o, cy - o, cx + o, cy + o, (x, y) =>
            {
                float a = MathF.Atan2(y - cy, x - cx);
                float rel = (a - a0) % (MathF.PI * 2f);
                if (rel < 0) rel += MathF.PI * 2f;
                if (sweepDeg < 0) rel = MathF.PI * 2f - rel;
                float d = rel <= Math.Abs(sweepDeg) * Deg
                    ? Math.Abs(Len(x - cx, y - cy) - r)
                    : Math.Min(Len(x - ex0, y - ey0), Len(x - ex1, y - ey1));
                return d - width * 0.5f;
            }, p);
        }

        /// <summary>
        /// Fills any shape given its signed distance (logical units, negative inside) within a
        /// logical bounding box. Every other primitive goes through here.
        /// </summary>
        public void Fill(float left, float top, float right, float bottom, Func<float, float, float> sdf, Paint p)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(left * _scale) - 2), y0 = Math.Max(0, (int)MathF.Floor(top * _scale) - 2);
            int x1 = Math.Min(Size - 1, (int)MathF.Ceiling(right * _scale) + 2), y1 = Math.Min(Size - 1, (int)MathF.Ceiling(bottom * _scale) + 2);
            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float x = (px + 0.5f) / _scale, y = (py + 0.5f) / _scale;
                    float cover = 0.5f - sdf(x, y) * _scale;
                    if (cover <= 0f) continue;
                    if (cover > 1f) cover = 1f;
                    Blend(px, py, p.Sample(x, y), cover);
                }
            }
        }

        /// <summary>Shapes drawn inside <paramref name="shapes"/> erase instead of paint (holes, notches, gaps).</summary>
        public void Cut(Action<IconCanvas> shapes)
        {
            _erase = true;
            try
            {
                shapes(this);
            }
            finally
            {
                _erase = false;
            }
        }

        private void Blend(int px, int py, Rgba c, float cover)
        {
            int i = (py * Size + px) * 4;
            if (_erase)
            {
                float keep = 1f - cover;
                Px[i] *= keep;
                Px[i + 1] *= keep;
                Px[i + 2] *= keep;
                Px[i + 3] *= keep;
                return;
            }
            float a = c.A * cover;
            float inv = 1f - a;
            Px[i] = c.R * a + Px[i] * inv;
            Px[i + 1] = c.G * a + Px[i + 1] * inv;
            Px[i + 2] = c.B * a + Px[i + 2] * inv;
            Px[i + 3] = a + Px[i + 3] * inv;
        }

        // ------------------------------------------------------------ layers and effects

        /// <summary>Composites <paramref name="layer"/> over this canvas.</summary>
        public void Draw(IconCanvas layer, float opacity = 1f)
        {
            for (int i = 0; i < Px.Length; i += 4)
            {
                float a = layer.Px[i + 3] * opacity;
                if (a <= 0f) continue;
                float inv = 1f - a;
                Px[i] = layer.Px[i] * opacity + Px[i] * inv;
                Px[i + 1] = layer.Px[i + 1] * opacity + Px[i + 1] * inv;
                Px[i + 2] = layer.Px[i + 2] * opacity + Px[i + 2] * inv;
                Px[i + 3] = a + Px[i + 3] * inv;
            }
        }

        /// <summary>Draws <paramref name="layer"/> only where this canvas already has paint (details that stay inside a shape).</summary>
        public void DrawInside(IconCanvas layer, float opacity = 1f)
        {
            for (int i = 0; i < Px.Length; i += 4)
            {
                float mask = Px[i + 3];
                float a = layer.Px[i + 3] * opacity * mask;
                if (a <= 0f) continue;
                float k = layer.Px[i + 3] > 0f ? a / layer.Px[i + 3] : 0f;
                float inv = 1f - a;
                Px[i] = layer.Px[i] * k + Px[i] * inv;
                Px[i + 1] = layer.Px[i + 1] * k + Px[i + 1] * inv;
                Px[i + 2] = layer.Px[i + 2] * k + Px[i + 2] * inv;
            }
        }

        /// <summary>Adds an outline of <paramref name="width"/> logical units behind the current artwork.</summary>
        public void Outline(float width, Rgba color)
        {
            float r = width * _scale;
            int ri = (int)MathF.Ceiling(r) + 1;
            var dilated = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float best = 0f;
                    for (int oy = -ri; oy <= ri && best < 1f; oy++)
                    {
                        int sy = y + oy;
                        if (sy < 0 || sy >= Size) continue;
                        for (int ox = -ri; ox <= ri; ox++)
                        {
                            int sx = x + ox;
                            if (sx < 0 || sx >= Size) continue;
                            float a = Px[(sy * Size + sx) * 4 + 3];
                            if (a <= best) continue;
                            float w = Math.Max(0f, Math.Min(1f, r + 0.5f - MathF.Sqrt(ox * ox + oy * oy)));
                            best = Math.Max(best, a * w);
                        }
                    }
                    dilated[y * Size + x] = best;
                }
            }
            Under(dilated, color);
        }

        /// <summary>Adds a soft drop shadow behind the current artwork (offset and blur in logical units).</summary>
        public void Shadow(float dx, float dy, float blur, Rgba color)
        {
            var a = new float[Size * Size];
            int ox = (int)MathF.Round(dx * _scale), oy = (int)MathF.Round(dy * _scale);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int sx = x - ox, sy = y - oy;
                    if (sx < 0 || sy < 0 || sx >= Size || sy >= Size) continue;
                    a[y * Size + x] = Px[(sy * Size + sx) * 4 + 3];
                }
            }
            int r = Math.Max(1, (int)MathF.Round(blur * _scale * 0.5f));
            a = BoxBlur(BoxBlur(a, r), r);
            Under(a, color);
        }

        private void Under(float[] alpha, Rgba color)
        {
            for (int i = 0; i < alpha.Length; i++)
            {
                int o = i * 4;
                float under = alpha[i] * color.A * (1f - Px[o + 3]);
                Px[o] += color.R * under;
                Px[o + 1] += color.G * under;
                Px[o + 2] += color.B * under;
                Px[o + 3] += under;
            }
        }

        private float[] BoxBlur(float[] src, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float norm = 1f / (2 * r + 1);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++)
                    {
                        int sx = x + k;
                        if (sx >= 0 && sx < Size) s += src[y * Size + sx];
                    }
                    tmp[y * Size + x] = s * norm;
                }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++)
                    {
                        int sy = y + k;
                        if (sy >= 0 && sy < Size) s += tmp[sy * Size + x];
                    }
                    dst[y * Size + x] = s * norm;
                }
            return dst;
        }

        /// <summary>Straight-alpha 8-bit RGBA, row 0 at the bottom when <paramref name="bottomUp"/> (Unity textures).</summary>
        public byte[] ToRgba32(bool bottomUp)
        {
            var bytes = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            {
                int dy = bottomUp ? Size - 1 - y : y;
                for (int x = 0; x < Size; x++)
                {
                    int s = (y * Size + x) * 4, d = (dy * Size + x) * 4;
                    float a = Px[s + 3];
                    float inv = a > 1e-5f ? 1f / a : 0f;
                    bytes[d] = ToByte(Px[s] * inv);
                    bytes[d + 1] = ToByte(Px[s + 1] * inv);
                    bytes[d + 2] = ToByte(Px[s + 2] * inv);
                    bytes[d + 3] = ToByte(a);
                }
            }
            return bytes;
        }

        private static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)MathF.Round(v * 255f)));

        // ------------------------------------------------------------ geometry

        private const float Deg = MathF.PI / 180f;

        private static float Len(float x, float y) => MathF.Sqrt(x * x + y * y);

        public static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float len2 = bax * bax + bay * bay;
            float h = len2 > 1e-8f ? Math.Max(0f, Math.Min(1f, (pax * bax + pay * bay) / len2)) : 0f;
            return Len(pax - bax * h, pay - bay * h);
        }

        private static float PolygonDistance(float[] v, int n, float x, float y)
        {
            float d = (x - v[0]) * (x - v[0]) + (y - v[1]) * (y - v[1]);
            float s = 1f;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                float vix = v[i * 2], viy = v[i * 2 + 1], vjx = v[j * 2], vjy = v[j * 2 + 1];
                float ex = vjx - vix, ey = vjy - viy;
                float wx = x - vix, wy = y - viy;
                float e2 = ex * ex + ey * ey;
                float h = e2 > 1e-8f ? Math.Max(0f, Math.Min(1f, (wx * ex + wy * ey) / e2)) : 0f;
                float bx = wx - ex * h, by = wy - ey * h;
                d = Math.Min(d, bx * bx + by * by);
                bool c1 = y >= viy, c2 = y < vjy, c3 = ex * wy > ey * wx;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * MathF.Sqrt(d);
        }
    }
}
