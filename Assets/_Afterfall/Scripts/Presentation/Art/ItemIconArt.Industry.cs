using System;
using System.Collections.Generic;

namespace Afterfall.Presentation.Art
{
    /// <summary>Icons for processed materials, construction supplies, electrical and mechanical parts.</summary>
    public static partial class ItemIconArt
    {
        // ------------------------------------------------------------ processed

        /// <summary>Three sawn boards stacked askew, grain along them and end grain on the near ends.</summary>
        private static void Planks(IconCanvas c)
        {
            var iso = new Iso(25f, 85f, 0.76f, -0.43f, -0.44f, -0.36f);
            Rgba wood = new Rgba(0.78f, 0.60f, 0.40f);
            float[][] boards = { new[] { 0f, 0f, 0f }, new[] { 5f, 8f, -2.5f }, new[] { -3f, 16f, 2.5f } };
            const float len = 74f, wid = 22f, th = 8f;
            for (int i = 0; i < boards.Length; i++)
            {
                float x = boards[i][0], y = boards[i][1], z = boards[i][2];
                Rgba col = i == 1 ? wood.Darker(0.06f) : i == 2 ? wood.Lighter(0.05f) : wood;
                int seed = 300 + i;
                Part(c, l =>
                {
                    float[] top = iso.P(x, y + th, z, x + len, y + th, z, x + len, y + th, z + wid, x, y + th, z + wid);
                    float[] front = iso.P(x, y, z, x + len, y, z, x + len, y + th, z, x, y + th, z);
                    float[] end = iso.P(x, y, z, x, y, z + wid, x, y + th, z + wid, x, y + th, z);
                    l.Polygon(Paint.Shaded(col.Lighter(0.18f), top[6], top[7], top[2], top[3], 0.12f), top);
                    l.Polygon(Paint.Shaded(col.Darker(0.22f), front[6], front[7], front[2], front[3], 0.1f), front);
                    l.Grain(seed, 12f, 1.1f, 0.22f, -29.4f);
                    Inside(l, d =>
                    {
                        for (int k = 1; k < 5; k++)
                        {
                            float zz = z + wid * k / 5f + (IconCanvas.Hash01(seed, k, 0) - 0.5f) * 2f;
                            float[] g = iso.P(x + 3f, y + th, zz, x + len * 0.5f, y + th, zz + 1.2f, x + len - 3f, y + th, zz - 0.6f);
                            d.Polyline(col.Darker(0.4f).WithAlpha(0.45f), 0.6f, g);
                        }
                        float kx = iso.SX(x + 24f + i * 16f, y + th, z + wid * 0.55f), ky = iso.SY(x + 24f + i * 16f, y + th, z + wid * 0.55f);
                        d.Ellipse(kx, ky, 2.6f, 1.4f, -29.4f, col.Darker(0.45f));
                        d.EllipseRing(kx, ky, 4.2f, 2.3f, -29.4f, 0.6f, col.Darker(0.3f).WithAlpha(0.6f));
                    });
                    // End grain: lighter sawn face with arcs of the growth rings.
                    var endLayer = l.Layer();
                    endLayer.Polygon(Paint.Shaded(new Rgba(0.88f, 0.72f, 0.50f), end[0], end[1], end[4], end[5], 0.12f), end);
                    Inside(endLayer, d =>
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            float yy = y + th * (0.25f + k * 0.25f);
                            float[] a = iso.P(x, yy, z + 1f, x, yy + 1.6f - k * 0.6f, z + wid * 0.5f, x, yy, z + wid - 1f);
                            d.Curve(a[0], a[1], a[2], a[3], a[4], a[5], 0.6f, col.Darker(0.38f).WithAlpha(0.6f));
                        }
                    });
                    l.Draw(endLayer);
                }, 1.4f, 0.5f);
            }
        }

        /// <summary>A flat coil of twisted rope with a frayed loose end.</summary>
        private static void RopeCoil(IconCanvas c)
        {
            Rgba rope = new Rgba(0.72f, 0.60f, 0.40f);
            const float cx = 50f, cy = 54f;
            float[] radii = { 36f, 27.5f, 19f, 10.5f };
            const float squash = 0.6f, thick = 7.4f;
            // Far halves from the outside in, near halves from the inside out, so loops overlap correctly.
            for (int i = 0; i < radii.Length; i++) RopeArc(c, cx, cy - i * 1.2f, radii[i], radii[i] * squash, 180f, 180f, thick, rope, 400 + i);
            Part(c, l => l.Ellipse(cx, cy - 4f, 6.5f, 3.6f, 0f, new Rgba(0.16f, 0.12f, 0.08f)), 0f, 0f);
            for (int i = radii.Length - 1; i >= 0; i--) RopeArc(c, cx, cy - i * 1.2f, radii[i], radii[i] * squash, 0f, 180f, thick, rope, 410 + i);
            // Loose end leaving the outer loop, with a frayed tip.
            Part(c, l =>
            {
                float[] tail = IconCanvas.CurvePoints(cx - 34f, cy + 9f, 18f, 80f, 30f, 88f, 10);
                l.Polyline(Ink.WithAlpha(0.75f), thick + 1.4f, tail);
                l.Polyline(Paint.Shaded(rope, 10f, 60f, 40f, 90f, 0.25f), thick, tail);
                Inside(l, d =>
                {
                    for (int i = 0; i < 9; i++)
                    {
                        float x = tail[i * 2], y = tail[i * 2 + 1];
                        d.Line(x - 3f, y - 2f, x + 3f, y + 2.5f, 1f, rope.Darker(0.45f).WithAlpha(0.7f));
                    }
                });
                for (int i = 0; i < 6; i++)
                {
                    float a = (-40f + i * 16f) * MathF.PI / 180f;
                    l.Line(30f, 88f, 30f + MathF.Cos(a) * 6.5f, 88f + MathF.Sin(a) * 4f, 0.9f, rope.Lighter(0.1f));
                }
            }, 1.2f, 0.45f);
        }

        private static void RopeArc(IconCanvas c, float cx, float cy, float rx, float ry, float start, float sweep, float width, Rgba col, int seed)
        {
            Part(c, l =>
            {
                l.EllipseArc(cx, cy, rx, ry, 0f, width + 1.4f, start, sweep, Ink.WithAlpha(0.75f));
                l.EllipseArc(cx, cy, rx, ry, 0f, width, start, sweep, Paint.Shaded(start >= 180f ? col.Darker(0.12f) : col, cx - rx, cy - ry, cx + rx, cy + ry, 0.25f));
                Inside(l, d =>
                {
                    // Twisted strands: short oblique strokes along the loop.
                    int n = (int)(rx * 0.55f);
                    for (int i = 0; i <= n; i++)
                    {
                        float a = (start + sweep * i / n) * MathF.PI / 180f;
                        float px = cx + MathF.Cos(a) * rx, py = cy + MathF.Sin(a) * ry;
                        float tx = -MathF.Sin(a) * rx, ty = MathF.Cos(a) * ry;
                        float tl = MathF.Max(1e-3f, MathF.Sqrt(tx * tx + ty * ty));
                        tx /= tl;
                        ty /= tl;
                        float nx = -ty, ny = tx;
                        float ux = tx * 0.6f + nx * 0.8f, uy = ty * 0.6f + ny * 0.8f;
                        d.Line(px - ux * width * 0.6f, py - uy * width * 0.6f, px + ux * width * 0.6f, py + uy * width * 0.6f, 1.1f, col.Darker(0.5f).WithAlpha(0.7f));
                        d.Line(px - ux * width * 0.6f + tx * 1.4f, py - uy * width * 0.6f + ty * 1.4f, px + ux * width * 0.6f + tx * 1.4f, py + uy * width * 0.6f + ty * 1.4f, 0.8f, col.Lighter(0.35f).WithAlpha(0.5f));
                    }
                });
            }, 1f, 0.4f);
        }

        /// <summary>Three steel nails, two side by side and one across them.</summary>
        private static void Nails(IconCanvas c)
        {
            Nail(c, 34f, 13f, 80f, 70f);
            Nail(c, 17f, 28f, 62f, 87f);
            Nail(c, 14f, 72f, 89f, 43f);
        }

        private static void Nail(IconCanvas c, float hx, float hy, float tx, float ty)
        {
            Rgba steel = new Rgba(0.66f, 0.68f, 0.71f);
            float dx = tx - hx, dy = ty - hy, len = MathF.Sqrt(dx * dx + dy * dy);
            float ux = dx / len, uy = dy / len;
            float deg = MathF.Atan2(uy, ux) * 180f / MathF.PI;
            const float w = 7f;
            Part(c, l =>
            {
                float sx = tx - ux * 12f, sy = ty - uy * 12f;
                Rod(l, hx + ux * 2f, hy + uy * 2f, sx, sy, w, steel, 0.6f, false);
                float nx = -uy * w * 0.5f, ny = ux * w * 0.5f;
                l.Polygon(Across(steel, sx, sy, tx, ty, w, 0.6f), sx + nx, sy + ny, tx, ty, sx - nx, sy - ny);
                l.Ellipse(hx + ux * 2.2f, hy + uy * 2.2f, 2.6f, 9.4f, deg, Across(steel.Darker(0.1f), hx, hy, tx, ty, 18f, 0.5f));
                l.Ellipse(hx, hy, 2.2f, 9.2f, deg, Paint.Shaded(steel.Lighter(0.25f), hx - 8f, hy - 8f, hx + 8f, hy + 8f, 0.25f));
                Inside(l, d =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float px = hx + ux * (5f + i * 1.6f), py = hy + uy * (5f + i * 1.6f);
                        d.Line(px + nx, py + ny, px - nx, py - ny, 0.6f, Ink.WithAlpha(0.35f));
                    }
                });
            }, 1f, 0.5f);
            Glint(c, hx + ux * len * 0.35f - uy * 1.2f, hy + uy * len * 0.35f + ux * 1.2f, 1.8f, 0.7f);
        }

        /// <summary>Three cast iron ingots: two side by side and one on top, bright and freshly poured.</summary>
        private static void IronIngots(IconCanvas c)
        {
            var iso = new Iso(13f, 72f, 0.86f, 0.22f, 0.42f, -0.36f);
            Ingot(c, iso, 0f, 0f, 25f, 501);
            Ingot(c, iso, 0f, 0f, 0f, 502);
            Ingot(c, iso, 4f, 14f, 12.5f, 503);
        }

        private static void Ingot(IconCanvas c, Iso iso, float x, float y, float z, int seed)
        {
            Rgba iron = new Rgba(0.60f, 0.64f, 0.70f);
            const float L = 58f, W = 23f, H = 14f, k = 5f;
            Part(c, l =>
            {
                float[] top = iso.P(x + k, y + H, z + k, x + L - k, y + H, z + k, x + L - k, y + H, z + W - k, x + k, y + H, z + W - k);
                float[] front = iso.P(x, y, z, x + L, y, z, x + L - k, y + H, z + k, x + k, y + H, z + k);
                float[] side = iso.P(x + L, y, z, x + L, y, z + W, x + L - k, y + H, z + W - k, x + L - k, y + H, z + k);
                l.Polygon(Paint.Stops(front[6], front[7], front[2], front[3], new[] { 0f, 0.35f, 1f }, iron.Darker(0.08f), iron.Lighter(0.25f), iron.Darker(0.42f)), front);
                l.Polygon(Paint.Shaded(iron.Darker(0.48f), side[6], side[7], side[2], side[3], 0.15f), side);
                l.Polygon(Paint.Stops(top[0], top[1], top[4], top[5], new[] { 0f, 0.4f, 1f }, iron.Lighter(0.25f), iron.Lighter(0.5f), iron.Lighter(0.05f)), top);
                l.Grain(seed, 2.5f, 2.5f, 0.07f);
                Inside(l, d =>
                {
                    // A cast mark pressed into the top.
                    float mx = iso.SX(x + L * 0.5f, y + H, z + W * 0.5f), my = iso.SY(x + L * 0.5f, y + H, z + W * 0.5f);
                    d.Polygon(iron.Darker(0.15f), iso.P(x + L * 0.5f - 7f, y + H, z + W * 0.5f - 3f, x + L * 0.5f + 7f, y + H, z + W * 0.5f - 3f,
                        x + L * 0.5f + 7f, y + H, z + W * 0.5f + 3f, x + L * 0.5f - 7f, y + H, z + W * 0.5f + 3f));
                    d.Line(mx - 4f, my + 0.4f, mx + 4f, my - 0.6f, 0.7f, White.WithAlpha(0.4f));
                    Speckle(d, seed, 10, top[6] - 4f, top[5] - 2f, side[2], side[3], 0.4f, 0.9f, iron.Darker(0.5f).WithAlpha(0.5f));
                });
            }, 1.4f, 0.65f, 0.45f, 0.4f);
            Glint(c, iso.SX(x + k + 2f, y + H, z + k + 1f), iso.SY(x + k + 2f, y + H, z + k + 1f), 2.4f, 0.75f);
        }

        /// <summary>A red jerry can: X-embossed side, triple handle and a spout cap.</summary>
        private static void FuelCan(IconCanvas c)
        {
            Rgba red = new Rgba(0.70f, 0.18f, 0.13f);
            var iso = new Iso(21f, 82f, 0.92f, 0.1f, 0.5f, -0.4f);
            const float W = 42f, H = 54f, D = 16f;
            Part(c, l =>
            {
                iso.Box(l, 0f, 0f, 0f, W, H, D, red, 0.22f, 0.38f);
                l.Grain(601, 3f, 3f, 0.1f);
                Inside(l, d =>
                {
                    // Raised rim around the side panel and the embossed X.
                    d.Polyline(Ink.WithAlpha(0.35f), 1.4f, iso.P(4f, 4f, 0f, W - 4f, 4f, 0f, W - 4f, H - 6f, 0f, 4f, H - 6f, 0f, 4f, 4f, 0f));
                    d.Polyline(White.WithAlpha(0.22f), 0.9f, iso.P(3f, 3f, 0f, 3f, H - 5f, 0f, W - 3f, H - 5f, 0f));
                    foreach (float[] s in new[] { new[] { 7f, 8f, W - 7f, H - 10f }, new[] { W - 7f, 8f, 7f, H - 10f } })
                    {
                        float[] a = iso.P(s[0], s[1], 0f, s[2], s[3], 0f);
                        d.Line(a[0] + 0.8f, a[1] + 0.8f, a[2] + 0.8f, a[3] + 0.8f, 3f, Ink.WithAlpha(0.4f));
                        d.Line(a[0] - 0.6f, a[1] - 0.6f, a[2] - 0.6f, a[3] - 0.6f, 2f, red.Lighter(0.3f));
                    }
                    // Scuffs and grime toward the bottom.
                    for (int i = 0; i < 5; i++)
                    {
                        float x0 = 6f + IconCanvas.Hash01(602, i, 0) * (W - 12f), y0 = 6f + IconCanvas.Hash01(602, i, 1) * (H - 14f);
                        float[] a = iso.P(x0, y0, 0f, x0 + 6f, y0 + 1.5f, 0f);
                        d.Line(a[0], a[1], a[2], a[3], 0.6f, White.WithAlpha(0.35f));
                    }
                    float[] lowFront = iso.P(0f, 0f, 0f, W, 0f, 0f, W, 14f, 0f, 0f, 14f, 0f);
                    d.Polygon(Paint.Linear(new Rgba(0.18f, 0.12f, 0.08f, 0.5f), new Rgba(0.18f, 0.12f, 0.08f, 0f), 0f, lowFront[1], 0f, lowFront[7]), lowFront);
                    d.Polygon(new Rgba(0.18f, 0.12f, 0.08f, 0.3f), iso.P(W, 0f, 0f, W, 0f, D, W, 10f, D, W, 10f, 0f));
                });
            }, 2.2f, 0f);
            // Triple handle along the top, cast with the can.
            Part(c, l =>
            {
                float[] bar = iso.P(11f, H - 1f, D * 0.5f, W - 3f, H - 1f, D * 0.5f, W - 3f, H + 11f, D * 0.5f, 11f, H + 11f, D * 0.5f);
                l.Polygon(Paint.Shaded(red.Lighter(0.08f), bar[6], bar[7], bar[2], bar[3], 0.28f), bar);
                l.Cut(k =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float hx = iso.SX(15.5f + i * 9f, H + 4.5f, D * 0.5f), hy = iso.SY(15.5f + i * 9f, H + 4.5f, D * 0.5f);
                        k.RoundRect(hx, hy, 6.4f, 6f, 2.6f, 6f, Ink);
                    }
                });
            }, 1.4f, 0.45f);
            // Spout with its clamp lever.
            Part(c, l =>
            {
                float bx = iso.SX(5f, H, D * 0.45f), by = iso.SY(5f, H, D * 0.45f);
                Rgba cap = new Rgba(0.30f, 0.31f, 0.32f);
                Rod(l, bx, by + 2f, bx - 3f, by - 10f, 8f, cap, 0.45f, false);
                l.Ellipse(bx - 3f, by - 10f, 4.1f, 1.9f, -14f, Paint.Shaded(cap.Lighter(0.3f), bx - 7f, by - 12f, bx + 1f, by - 8f, 0.2f));
                l.Rect(bx - 6.6f, by - 6f, bx + 1f, by - 4.6f, cap.Darker(0.3f));
                l.Line(bx + 2f, by - 7f, bx + 10f, by - 3f, 2.2f, Steel.Darker(0.1f));
            }, 1.2f, 0.45f);
        }

        // ------------------------------------------------------------ electrical

        /// <summary>A coil of bare copper wire held by two ties, with a loose end.</summary>
        private static void CopperWire(IconCanvas c)
        {
            Rgba copper = new Rgba(0.82f, 0.46f, 0.22f);
            Part(c, l =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float cx = 44f + i * 1.7f, cy = 44f + i * 1.5f;
                    float rot = -12f + i * 4.5f;
                    float rx = 30f - (i % 2) * 1.6f, ry = 25f - (i % 3) * 1.2f;
                    l.EllipseRing(cx, cy, rx, ry, rot, 5.8f, Ink.WithAlpha(0.8f));
                    l.EllipseRing(cx, cy, rx, ry, rot, 4.2f, Paint.Shaded(copper, cx - rx, cy - ry, cx + rx, cy + ry, 0.32f));
                }
            }, 1.2f, 0f, 0.45f, 0.35f);
            // Loose end with a kink and a bright cut tip.
            Part(c, l =>
            {
                float[] tail = IconCanvas.CurvePoints(70f, 64f, 84f, 66f, 86f, 86f, 10);
                l.Polyline(Ink.WithAlpha(0.8f), 5.6f, tail);
                l.Polyline(Paint.Shaded(copper, 70f, 64f, 88f, 86f, 0.3f), 4f, tail);
                l.Circle(86f, 86f, 1.6f, new Rgba(1f, 0.82f, 0.58f));
            }, 1f, 0.35f);
            // Black cable ties holding the coil.
            Part(c, l =>
            {
                foreach (float[] t in new[] { new[] { 18f, 44f, 70f }, new[] { 78f, 56f, 64f } })
                {
                    l.RoundRect(t[0], t[1], 5f, 14f, 1.5f, t[2], Paint.Shaded(new Rgba(0.16f, 0.16f, 0.17f), t[0] - 4f, t[1] - 7f, t[0] + 4f, t[1] + 7f, 0.4f));
                }
            }, 0.8f, 0.4f);
            Glint(c, 30f, 26f, 2.8f, 0.85f);
            Glint(c, 72f, 36f, 2f, 0.6f);
        }

        /// <summary>A lead-acid car battery: dark case, grey lid, red and black terminals, warning label.</summary>
        private static void CarBattery(IconCanvas c)
        {
            Rgba casing = new Rgba(0.22f, 0.25f, 0.29f);
            var iso = new Iso(13f, 84f, 0.96f, 0.08f, 0.46f, -0.38f);
            const float W = 56f, H = 36f, D = 26f;
            Part(c, l =>
            {
                iso.Box(l, 0f, 0f, 0f, W, H - 6f, D, casing, 0.2f, 0.3f);
                iso.Box(l, -0.5f, H - 6f, -0.5f, W + 1f, 6f, D + 1f, new Rgba(0.40f, 0.42f, 0.45f), 0.2f, 0.32f);
                Inside(l, d =>
                {
                    // Label band with polarity marks and a lightning symbol.
                    float[] band = iso.P(4f, 8f, 0f, W - 4f, 8f, 0f, W - 4f, 22f, 0f, 4f, 22f, 0f);
                    d.Polygon(Paint.Shaded(new Rgba(0.92f, 0.70f, 0.18f), band[6], band[7], band[2], band[3], 0.15f), band);
                    float[] stripe = iso.P(4f, 8f, 0f, W - 4f, 8f, 0f, W - 4f, 11f, 0f, 4f, 11f, 0f);
                    d.Polygon(new Rgba(0.15f, 0.15f, 0.16f), stripe);
                    float bx = iso.SX(15f, 16.5f, 0f), by = iso.SY(15f, 16.5f, 0f);
                    d.Polygon(new Rgba(0.15f, 0.15f, 0.16f), bx + 1f, by - 5f, bx - 3f, by + 0.5f, bx, by + 0.5f, bx - 1.5f, by + 5f, bx + 3.5f, by - 1f, bx + 0.5f, by - 1f, bx + 2.5f, by - 5f);
                    for (int i = 0; i < 3; i++)
                    {
                        float[] t = iso.P(24f, 19f - i * 3.2f, 0f, 46f - i * 5f, 19f - i * 3.2f, 0f);
                        d.Line(t[0], t[1], t[2], t[3], 1.4f, new Rgba(0.15f, 0.15f, 0.16f, 0.85f));
                    }
                    // Vent caps along the lid.
                    for (int i = 0; i < 5; i++)
                    {
                        float vx = iso.SX(16f + i * 6.5f, H, D * 0.5f), vy = iso.SY(16f + i * 6.5f, H, D * 0.5f);
                        d.Ellipse(vx, vy, 2.4f, 1.3f, -18f, new Rgba(0.22f, 0.23f, 0.25f));
                        d.Line(vx - 1.3f, vy + 0.3f, vx + 1.3f, vy - 0.4f, 0.5f, White.WithAlpha(0.3f));
                    }
                    // Plus and minus moulded into the lid.
                    float px = iso.SX(6f, H, 5f), py = iso.SY(6f, H, 5f);
                    d.Line(px - 2.4f, py, px + 2.4f, py, 1.1f, White.WithAlpha(0.75f));
                    d.Line(px, py - 2f, px, py + 2f, 1.1f, White.WithAlpha(0.75f));
                    float mx = iso.SX(50f, H, 5f), my = iso.SY(50f, H, 5f);
                    d.Line(mx - 2.4f, my, mx + 2.4f, my, 1.1f, White.WithAlpha(0.75f));
                });
            }, 2f, 0f);
            Terminal(c, iso.SX(8f, H, D * 0.5f), iso.SY(8f, H, D * 0.5f), new Rgba(0.82f, 0.16f, 0.12f));
            Terminal(c, iso.SX(48f, H, D * 0.5f), iso.SY(48f, H, D * 0.5f), new Rgba(0.20f, 0.20f, 0.22f));
        }

        private static void Terminal(IconCanvas c, float x, float y, Rgba cap)
        {
            Rgba lead = new Rgba(0.62f, 0.63f, 0.64f);
            Part(c, l =>
            {
                l.Ellipse(x, y, 6f, 3f, -18f, cap.Darker(0.2f));
                Rod(l, x, y - 1f, x, y - 7.5f, 9f, cap, 0.45f, false);
                l.Ellipse(x, y - 7.5f, 4.6f, 2.3f, -18f, cap.Lighter(0.25f));
                Rod(l, x, y - 8f, x, y - 12.5f, 5f, lead, 0.5f, false);
                l.Ellipse(x, y - 12.5f, 2.5f, 1.2f, -18f, lead.Lighter(0.3f));
            }, 0.8f, 0.45f);
        }

        /// <summary>A green circuit board: chips, gold traces, a capacitor and an edge connector.</summary>
        private static void CircuitBoard(IconCanvas c)
        {
            float[] q = { 13f, 40f, 63f, 20f, 88f, 57f, 37f, 80f };
            Func<float, float, float[]> map = (u, v) => new[]
            {
                (1 - u) * (1 - v) * q[0] + u * (1 - v) * q[2] + u * v * q[4] + (1 - u) * v * q[6],
                (1 - u) * (1 - v) * q[1] + u * (1 - v) * q[3] + u * v * q[5] + (1 - u) * v * q[7],
            };
            Func<float[], float[]> mapAll = uv =>
            {
                var w = new float[uv.Length];
                for (int i = 0; i + 1 < uv.Length; i += 2)
                {
                    float[] p = map(uv[i], uv[i + 1]);
                    w[i] = p[0];
                    w[i + 1] = p[1];
                }
                return w;
            };
            Rgba pcb = new Rgba(0.16f, 0.46f, 0.28f);
            Rgba gold = new Rgba(0.88f, 0.72f, 0.34f);
            Part(c, l =>
            {
                // Board edge (thickness) under the face.
                l.Polygon(pcb.Darker(0.55f), q[6], q[7], q[4], q[5], q[4], q[5] + 4f, q[6], q[7] + 4f);
                l.Polygon(pcb.Darker(0.45f), q[0], q[1], q[6], q[7], q[6], q[7] + 4f, q[0], q[1] + 4f);
                l.Polygon(Paint.Shaded(pcb, 13f, 20f, 88f, 80f, 0.22f), q);
                Inside(l, d =>
                {
                    // Traces with right-angle bends and solder pads.
                    float[][] traces =
                    {
                        new[] { 0.1f, 0.2f, 0.3f, 0.2f, 0.38f, 0.3f }, new[] { 0.1f, 0.3f, 0.28f, 0.3f, 0.36f, 0.4f },
                        new[] { 0.6f, 0.12f, 0.6f, 0.3f, 0.7f, 0.4f, 0.9f, 0.4f }, new[] { 0.68f, 0.12f, 0.68f, 0.26f, 0.9f, 0.26f },
                        new[] { 0.15f, 0.6f, 0.15f, 0.8f, 0.45f, 0.8f }, new[] { 0.22f, 0.6f, 0.22f, 0.72f, 0.5f, 0.72f, 0.58f, 0.62f },
                        new[] { 0.55f, 0.9f, 0.55f, 0.75f, 0.8f, 0.75f }, new[] { 0.62f, 0.9f, 0.62f, 0.82f, 0.88f, 0.82f },
                    };
                    foreach (float[] t in traces)
                    {
                        float[] w = mapAll(t);
                        d.Polyline(gold.Darker(0.25f).WithAlpha(0.9f), 1.3f, w);
                        d.Circle(w[0], w[1], 1.5f, gold);
                        d.Circle(w[w.Length - 2], w[w.Length - 1], 1.5f, gold);
                    }
                    // Mounting holes and silkscreen marks.
                    foreach (float[] h in new[] { new[] { 0.07f, 0.08f }, new[] { 0.93f, 0.08f }, new[] { 0.93f, 0.92f }, new[] { 0.07f, 0.92f } })
                    {
                        float[] p = map(h[0], h[1]);
                        d.Circle(p[0], p[1], 2.2f, gold.Lighter(0.1f));
                        d.Circle(p[0], p[1], 1.1f, new Rgba(0.1f, 0.12f, 0.1f));
                    }
                    d.Polyline(White.WithAlpha(0.55f), 0.6f, mapAll(new[] { 0.36f, 0.48f, 0.64f, 0.48f, 0.64f, 0.62f, 0.36f, 0.62f, 0.36f, 0.48f }));
                    // Gold edge connector along the near edge.
                    for (int i = 0; i < 8; i++)
                    {
                        float u0 = 0.18f + i * 0.08f;
                        d.Polygon(Paint.Shaded(gold, 0f, 60f, 100f, 90f, 0.2f), mapAll(new[] { u0, 0.9f, u0 + 0.05f, 0.9f, u0 + 0.05f, 1f, u0, 1f }));
                    }
                });
            }, 1.6f, 0f);
            // Chips with pins.
            Part(c, l =>
            {
                Chip(l, mapAll, 0.38f, 0.18f, 0.58f, 0.44f);
                Chip(l, mapAll, 0.66f, 0.46f, 0.84f, 0.66f);
            }, 1f, 0.5f);
            // An electrolytic capacitor standing on the board.
            Part(c, l =>
            {
                float[] p = map(0.22f, 0.42f);
                Rgba cap = new Rgba(0.18f, 0.30f, 0.56f);
                Rod(l, p[0], p[1], p[0], p[1] - 9f, 9f, cap, 0.5f, false);
                l.Ellipse(p[0], p[1], 4.5f, 2f, 0f, cap.Darker(0.2f));
                l.Ellipse(p[0], p[1] - 9f, 4.5f, 2f, 0f, Paint.Shaded(Steel, p[0] - 4f, p[1] - 11f, p[0] + 4f, p[1] - 7f, 0.2f));
                l.Line(p[0] - 1.6f, p[1] - 9f, p[0] + 1.6f, p[1] - 9f, 0.6f, Ink.WithAlpha(0.5f));
                l.Rect(p[0] + 2f, p[1] - 8f, p[0] + 3.4f, p[1] - 0.5f, White.WithAlpha(0.6f));
            }, 0.8f, 0.45f);
        }

        private static void Chip(IconCanvas l, Func<float[], float[]> mapAll, float u0, float v0, float u1, float v1)
        {
            Rgba pin = new Rgba(0.78f, 0.80f, 0.82f);
            int n = 5;
            for (int i = 0; i < n; i++)
            {
                float u = u0 + (u1 - u0) * (i + 0.5f) / n;
                l.Polygon(pin, mapAll(new[] { u - 0.015f, v0 - 0.05f, u + 0.015f, v0 - 0.05f, u + 0.015f, v0 + 0.02f, u - 0.015f, v0 + 0.02f }));
                l.Polygon(pin.Darker(0.25f), mapAll(new[] { u - 0.015f, v1 - 0.02f, u + 0.015f, v1 - 0.02f, u + 0.015f, v1 + 0.05f, u - 0.015f, v1 + 0.05f }));
            }
            float[] body = mapAll(new[] { u0, v0, u1, v0, u1, v1, u0, v1 });
            l.Polygon(Paint.Shaded(new Rgba(0.13f, 0.13f, 0.14f), body[0], body[1], body[4], body[5], 0.2f), body);
            float[] lid = mapAll(new[] { u0 + 0.02f, v0 + 0.03f, u1 - 0.02f, v0 + 0.03f, u1 - 0.02f, v1 - 0.03f, u0 + 0.02f, v1 - 0.03f });
            l.Polygon(new Rgba(0.2f, 0.2f, 0.21f), lid);
            float[] dot = mapAll(new[] { u0 + 0.05f, v0 + 0.07f });
            l.Circle(dot[0], dot[1], 1f, new Rgba(0.45f, 0.45f, 0.46f));
        }

        /// <summary>A blueprint, rolled at the top, with a generator drawn in white and a gold power mark.</summary>
        private static void GeneratorSchematic(IconCanvas c)
        {
            var f = new Frame(50f, 54f, -7f);
            Rgba blue = new Rgba(0.17f, 0.36f, 0.62f);
            Rgba paperBack = new Rgba(0.90f, 0.87f, 0.78f);
            Rgba line = new Rgba(0.92f, 0.96f, 1f, 0.92f);
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(blue, 18f, 20f, 82f, 90f, 0.2f), f.P(-31f, -30f, 31f, -30f, 31f, 26f, 22f, 35f, -31f, 35f));
                Inside(l, d =>
                {
                    for (int i = -5; i <= 5; i++)
                    {
                        d.Line(f.X(i * 6f, -32f), f.Y(i * 6f, -32f), f.X(i * 6f, 36f), f.Y(i * 6f, 36f), 0.45f, line.WithAlpha(0.18f));
                        d.Line(f.X(-32f, i * 6f), f.Y(-32f, i * 6f), f.X(32f, i * 6f), f.Y(32f, i * 6f), 0.45f, line.WithAlpha(0.18f));
                    }
                    // Generator: engine block, flywheel, base rails, exhaust.
                    d.Polyline(line, 1.2f, f.P(-22f, -2f, -22f, -14f, 2f, -14f, 2f, 10f, -22f, 10f, -22f, -2f));
                    d.Polyline(line, 1f, f.P(-18f, -14f, -18f, -20f, -10f, -20f, -10f, -14f));
                    d.Ring(f.X(13f, -2f), f.Y(13f, -2f), 8.5f, 1.2f, line);
                    d.Ring(f.X(13f, -2f), f.Y(13f, -2f), 2.6f, 1f, line);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * MathF.PI / 3f;
                        d.Line(f.X(13f + MathF.Cos(a) * 3.2f, -2f + MathF.Sin(a) * 3.2f), f.Y(13f + MathF.Cos(a) * 3.2f, -2f + MathF.Sin(a) * 3.2f),
                            f.X(13f + MathF.Cos(a) * 7.6f, -2f + MathF.Sin(a) * 7.6f), f.Y(13f + MathF.Cos(a) * 7.6f, -2f + MathF.Sin(a) * 7.6f), 0.7f, line.WithAlpha(0.8f));
                    }
                    d.Line(f.X(-25f, 13f), f.Y(-25f, 13f), f.X(24f, 13f), f.Y(24f, 13f), 1.2f, line);
                    for (int i = 0; i < 4; i++) d.Line(f.X(-18f + i * 6f, -10f), f.Y(-18f + i * 6f, -10f), f.X(-18f + i * 6f, 6f), f.Y(-18f + i * 6f, 6f), 0.6f, line.WithAlpha(0.6f));
                    // Dimension line with arrow heads, and a title block.
                    d.Line(f.X(-22f, 19f), f.Y(-22f, 19f), f.X(21f, 19f), f.Y(21f, 19f), 0.7f, line.WithAlpha(0.8f));
                    d.Polygon(line, f.P(-22f, 19f, -18f, 17.4f, -18f, 20.6f));
                    d.Polygon(line, f.P(21f, 19f, 17f, 17.4f, 17f, 20.6f));
                    d.Polyline(line.WithAlpha(0.85f), 0.8f, f.P(-28f, 24f, 4f, 24f, 4f, 33f));
                    for (int i = 0; i < 2; i++) d.Line(f.X(-25f, 27f + i * 3f), f.Y(-25f, 27f + i * 3f), f.X(-4f - i * 8f, 27f + i * 3f), f.Y(-4f - i * 8f, 27f + i * 3f), 1f, line.WithAlpha(0.6f));
                    // Faint fold creases.
                    d.Line(f.X(0f, -30f), f.Y(0f, -30f), f.X(0f, 35f), f.Y(0f, 35f), 1f, Ink.WithAlpha(0.15f));
                });
                // Curled-up corner showing the paper's back.
                l.Polygon(Paint.Shaded(paperBack, f.X(22f, 26f), f.Y(22f, 26f), f.X(31f, 35f), f.Y(31f, 35f), 0.2f), f.P(31f, 26f, 22f, 35f, 20f, 24f));
            }, 1.6f, 0f);
            // Gold power mark.
            Part(c, l =>
            {
                float bx = f.X(-2f, -23f), by = f.Y(-2f, -23f);
                Rgba gold = new Rgba(0.92f, 0.76f, 0.30f);
                l.Polygon(Paint.Shaded(gold, bx - 5f, by - 6f, bx + 5f, by + 6f, 0.25f), bx + 2f, by - 7f, bx - 5f, by + 1f, bx - 0.5f, by + 1f, bx - 2.5f, by + 7.5f, bx + 5f, by - 1.5f, bx + 0.5f, by - 1.5f, bx + 3.5f, by - 7f);
            }, 0.8f, 0.4f);
            // The roll along the top edge.
            Part(c, l =>
            {
                Rod(l, f.X(-34f, -31f), f.Y(-34f, -31f), f.X(34f, -31f), f.Y(34f, -31f), 10f, paperBack, 0.45f, false);
                foreach (float x in new[] { -34f, 34f })
                {
                    l.Ellipse(f.X(x, -31f), f.Y(x, -31f), 2.2f, 5f, f.Deg, paperBack.Darker(0.12f));
                    l.EllipseRing(f.X(x, -31f), f.Y(x, -31f), 1.2f, 2.8f, f.Deg, 0.7f, blue);
                }
                Inside(l, d => d.Line(f.X(-30f, -27.5f), f.Y(-30f, -27.5f), f.X(30f, -27.5f), f.Y(30f, -27.5f), 1f, blue.WithAlpha(0.5f)));
            }, 1f, 0.45f);
        }

        // ------------------------------------------------------------ mechanical

        /// <summary>Gear outline: <paramref name="teeth"/> trapezoidal teeth between the root and tip radii.</summary>
        private static float[] GearOutline(float cx, float cy, float rRoot, float rTip, int teeth, float rotDeg, float squash = 1f)
        {
            var pts = new List<float>();
            float pitch = MathF.PI * 2f / teeth, rot = rotDeg * MathF.PI / 180f;
            for (int i = 0; i < teeth; i++)
            {
                float a = rot + i * pitch;
                foreach (float[] s in new[] { new[] { -0.5f, rRoot }, new[] { -0.28f, rRoot }, new[] { -0.17f, rTip }, new[] { 0.17f, rTip }, new[] { 0.28f, rRoot } })
                {
                    float t = a + s[0] * pitch;
                    pts.Add(cx + MathF.Cos(t) * s[1]);
                    pts.Add(cy + MathF.Sin(t) * s[1] * squash);
                }
            }
            return pts.ToArray();
        }

        /// <summary>A machined spur gear with lightening holes, shown with its thickness.</summary>
        private static void Gear(IconCanvas c)
        {
            Rgba metal = new Rgba(0.62f, 0.65f, 0.68f);
            DrawGear(c, 47f, 47f, 31f, 38f, 12, 4f, metal, 5, 22f, 7f, 701);
        }

        private static void DrawGear(IconCanvas c, float cx, float cy, float rRoot, float rTip, int teeth, float depth, Rgba metal, int holes, float holeRadius, float holeWidth, int seed)
        {
            float dx = depth * 0.7f, dy = depth;
            Action<IconCanvas, float, float> cut = (k, ox, oy) =>
            {
                for (int i = 0; i < holes; i++)
                    k.Arc(cx + ox, cy + oy, holeRadius, holeWidth, i * 360f / holes + 18f, 360f / holes - 26f, Ink);
                k.Circle(cx + ox, cy + oy, rRoot * 0.16f, Ink);
            };
            // Back face and rim give the thickness; the holes show their inner walls.
            Part(c, l =>
            {
                l.Polygon(metal.Darker(0.5f), GearOutline(cx + dx, cy + dy, rRoot, rTip, teeth, 4f));
                for (int s = 1; s < 4; s++) l.Polygon(metal.Darker(0.42f), GearOutline(cx + dx * s / 4f, cy + dy * s / 4f, rRoot, rTip, teeth, 4f));
                if (holes > 0) l.Cut(k => cut(k, dx, dy));
            }, 0.8f, 0f);
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(metal, cx - rTip, cy - rTip, cx + rTip, cy + rTip, 0.3f), GearOutline(cx, cy, rRoot, rTip, teeth, 4f));
                l.Grain(seed, 2f, 2f, 0.07f);
                Inside(l, d =>
                {
                    // Turned rings on the face, a raised hub and a keyway.
                    d.Ring(cx, cy, rRoot * 0.86f, 1.2f, metal.Darker(0.3f).WithAlpha(0.7f));
                    d.Ring(cx, cy, rRoot * 0.86f + 1.2f, 0.7f, White.WithAlpha(0.35f));
                    d.Circle(cx, cy, rRoot * 0.36f, Paint.Shaded(metal.Lighter(0.18f), cx - 10f, cy - 10f, cx + 10f, cy + 10f, 0.3f));
                    d.Ring(cx, cy, rRoot * 0.36f, 0.9f, metal.Darker(0.35f));
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (100f + i * 9f) * MathF.PI / 180f;
                        d.Arc(cx, cy, rRoot * (0.55f + i * 0.08f), 0.6f, -150f + i * 20f, 70f, White.WithAlpha(0.3f));
                    }
                });
                if (holes > 0) l.Cut(k => cut(k, 0f, 0f));
                l.Cut(k => k.Rect(cx - 1.6f, cy - rRoot * 0.16f - 2.2f, cx + 1.6f, cy - rRoot * 0.1f, Ink));
            }, 1.8f, 0.4f, 0.45f, 0.4f);
            Glint(c, cx - rTip * 0.62f, cy - rTip * 0.55f, 2.6f, 0.8f);
        }

        /// <summary>An assortment: small gear, spring, hex nut and a threaded bolt.</summary>
        private static void MechanicalParts(IconCanvas c)
        {
            Rgba steel = new Rgba(0.62f, 0.64f, 0.66f);
            Rgba brass = new Rgba(0.80f, 0.63f, 0.30f);
            DrawGear(c, 32f, 33f, 15f, 20f, 9, 3f, steel.Darker(0.05f), 0, 0f, 0f, 801);
            // Coil spring along a diagonal.
            Part(c, l =>
            {
                var f = new Frame(62f, 20f, 50f);
                for (int i = 0; i < 7; i++)
                {
                    float x = i * 4.4f;
                    l.EllipseArc(f.X(x, 0f), f.Y(x, 0f), 3.4f, 9f, f.Deg, 3.4f, 270f, 180f, Ink.WithAlpha(0.8f));
                    l.EllipseArc(f.X(x, 0f), f.Y(x, 0f), 3.4f, 9f, f.Deg, 2.2f, 270f, 180f, steel.Darker(0.25f));
                }
                for (int i = 0; i < 7; i++)
                {
                    float x = i * 4.4f + 2.2f;
                    l.EllipseArc(f.X(x, 0f), f.Y(x, 0f), 3.4f, 9f, f.Deg, 3.4f, 90f, 180f, Ink.WithAlpha(0.8f));
                    l.EllipseArc(f.X(x, 0f), f.Y(x, 0f), 3.4f, 9f, f.Deg, 2.2f, 90f, 180f, Paint.Shaded(steel.Lighter(0.15f), 56f, 12f, 92f, 50f, 0.3f));
                }
            }, 0.8f, 0.4f);
            // Hex nut in brass.
            Part(c, l =>
            {
                const float nx = 27f, ny = 73f, r = 13f;
                var hex = new float[12];
                var hexLow = new float[12];
                for (int i = 0; i < 6; i++)
                {
                    float a = (i * 60f + 30f) * MathF.PI / 180f;
                    hex[i * 2] = nx + MathF.Cos(a) * r;
                    hex[i * 2 + 1] = ny + MathF.Sin(a) * r * 0.62f;
                    hexLow[i * 2] = hex[i * 2];
                    hexLow[i * 2 + 1] = hex[i * 2 + 1] + 6f;
                }
                l.Polygon(Paint.Linear(brass.Darker(0.15f), brass.Darker(0.45f), nx - r, 0f, nx + r, 0f), hexLow);
                l.Polygon(brass.Darker(0.25f), hex[0], hex[1], hexLow[0], hexLow[1], hexLow[2], hexLow[3], hexLow[4], hexLow[5], hex[4], hex[5], hex[2], hex[3]);
                l.Polygon(Paint.Shaded(brass.Lighter(0.15f), nx - r, ny - r, nx + r, ny + r, 0.25f), hex);
                l.Ellipse(nx, ny, 6f, 3.8f, 0f, new Rgba(0.22f, 0.17f, 0.10f));
                l.EllipseArc(nx, ny, 6f, 3.8f, 0f, 1.2f, 0f, 180f, brass.Lighter(0.3f));
            }, 1.2f, 0.45f);
            // Hex bolt with a threaded shank.
            Part(c, l =>
            {
                float hx = 50f, hy = 60f, tx = 86f, ty = 86f;
                Rod(l, hx, hy, tx, ty, 8f, steel, 0.55f, false);
                Inside(l, d =>
                {
                    for (int i = 0; i < 9; i++)
                    {
                        float t = 0.3f + i * 0.08f;
                        float px = hx + (tx - hx) * t, py = hy + (ty - hy) * t;
                        d.Line(px - 2.4f, py + 3.6f, px + 3f, py - 3f, 0.9f, Ink.WithAlpha(0.45f));
                    }
                });
                var f = new Frame(hx, hy, 36f);
                l.Polygon(Paint.Shaded(steel.Darker(0.1f), hx - 9f, hy - 9f, hx + 9f, hy + 9f, 0.25f), f.P(-3f, -10f, 3f, -10f, 4f, -5f, 4f, 5f, 3f, 10f, -3f, 10f, -4f, 5f, -4f, -5f));
                l.Polygon(Paint.Shaded(steel.Lighter(0.2f), hx - 9f, hy - 9f, hx + 9f, hy + 9f, 0.25f), f.P(-4f, -5f, 4f, -5f, 4f, 5f, -4f, 5f));
            }, 1.2f, 0.45f);
            Glint(c, 22f, 24f, 2.2f, 0.75f);
        }

        /// <summary>A straight steel pipe with a threaded end and a coupling showing its open bore.</summary>
        private static void MetalPipe(IconCanvas c)
        {
            var f = new Frame(48f, 54f, -26f);
            Rgba steel = new Rgba(0.58f, 0.62f, 0.66f);
            Part(c, l =>
            {
                Rod(l, f.X(-40f, 0f), f.Y(-40f, 0f), f.X(30f, 0f), f.Y(30f, 0f), 17f, steel, 0.6f, false);
                l.Ellipse(f.X(-40f, 0f), f.Y(-40f, 0f), 3.4f, 8.5f, f.Deg, steel.Darker(0.25f));
                Inside(l, d =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float x = -38f + i * 2f;
                        d.Line(f.X(x, -9f), f.Y(x, -9f), f.X(x + 1f, 9f), f.Y(x + 1f, 9f), 0.7f, Ink.WithAlpha(0.4f));
                    }
                    d.Line(f.X(-28f, -5f), f.Y(-28f, -5f), f.X(26f, -5f), f.Y(26f, -5f), 1.2f, White.WithAlpha(0.45f));
                    Speckle(d, 901, 8, 20f, 30f, 80f, 80f, 0.8f, 1.8f, RustBrown.WithAlpha(0.35f));
                });
                l.Grain(902, 6f, 1.5f, 0.06f, f.Deg);
            }, 1.6f, 0f);
            Part(c, l =>
            {
                Rod(l, f.X(26f, 0f), f.Y(26f, 0f), f.X(40f, 0f), f.Y(40f, 0f), 22f, steel.Darker(0.08f), 0.55f, false);
                Inside(l, d =>
                {
                    d.Line(f.X(29f, -11f), f.Y(29f, -11f), f.X(29f, 11f), f.Y(29f, 11f), 1f, Ink.WithAlpha(0.3f));
                    d.Line(f.X(37f, -11f), f.Y(37f, -11f), f.X(37f, 11f), f.Y(37f, 11f), 1f, Ink.WithAlpha(0.3f));
                });
                l.Ellipse(f.X(40f, 0f), f.Y(40f, 0f), 4.6f, 11f, f.Deg, Paint.Shaded(steel.Lighter(0.15f), f.X(40f, -11f), f.Y(40f, -11f), f.X(40f, 11f), f.Y(40f, 11f), 0.25f));
                l.Ellipse(f.X(40.5f, 0.3f), f.Y(40.5f, 0.3f), 3.2f, 7.8f, f.Deg, Paint.Linear(new Rgba(0.08f, 0.08f, 0.09f), new Rgba(0.30f, 0.31f, 0.33f), f.X(40f, -8f), f.Y(40f, -8f), f.X(40f, 8f), f.Y(40f, 8f)));
            }, 1.4f, 0.45f);
            Glint(c, f.X(10f, -5f), f.Y(10f, -5f), 2.6f, 0.75f);
        }

        /// <summary>An industrial electric motor: finned blue housing, end bell, shaft, feet and terminal box.</summary>
        private static void ElectricMotor(IconCanvas c)
        {
            Rgba paint = new Rgba(0.16f, 0.38f, 0.56f);
            Rgba grey = new Rgba(0.60f, 0.62f, 0.64f);
            // Feet and base plate.
            Part(c, l =>
            {
                var iso = new Iso(18f, 84f, 1f, 0f, 0.5f, -0.35f);
                iso.Box(l, 0f, 0f, 0f, 48f, 5f, 16f, new Rgba(0.30f, 0.32f, 0.34f), 0.2f, 0.3f);
                l.Polygon(paint.Darker(0.25f), 24f, 79f, 32f, 66f, 58f, 66f, 64f, 79f);
                foreach (float x in new[] { 22f, 58f }) l.Circle(x + 4f, 78.5f, 1.5f, Steel.Lighter(0.2f));
            }, 1.2f, 0f);
            // Housing with cooling fins.
            Part(c, l =>
            {
                l.Ellipse(19f, 50f, 6f, 25f, 0f, paint.Darker(0.25f));
                l.Rect(19f, 25f, 63f, 75f, Paint.Cylinder(paint, 40f, 25f, 40f, 75f, 0.5f));
                Inside(l, d =>
                {
                    for (int i = 0; i < 9; i++)
                    {
                        float y = 28f + i * 5.5f;
                        d.Line(14f, y, 62f, y, 1.6f, Ink.WithAlpha(0.35f));
                        d.Line(14f, y + 1.6f, 62f, y + 1.6f, 0.9f, White.WithAlpha(0.22f));
                    }
                    // Name plate.
                    d.RoundRect(38f, 55f, 14f, 9f, 1f, 0f, Paint.Shaded(Steel.Lighter(0.15f), 31f, 50f, 45f, 60f, 0.2f));
                    for (int i = 0; i < 3; i++) d.Line(33f, 52.5f + i * 2.4f, 42f - i * 2f, 52.5f + i * 2.4f, 0.7f, Ink.WithAlpha(0.5f));
                    Speckle(d, 951, 10, 18f, 26f, 62f, 74f, 0.6f, 1.6f, Steel.WithAlpha(0.5f));
                });
                l.Grain(952, 3f, 3f, 0.08f);
            }, 1.8f, 0.45f);
            // Terminal box with a cable gland.
            Part(c, l =>
            {
                var iso = new Iso(30f, 27f, 1f, 0f, 0.5f, -0.35f);
                iso.Box(l, 0f, 0f, 0f, 16f, 7f, 10f, paint.Lighter(0.05f), 0.25f, 0.3f);
                Rod(l, 30f, 23f, 23f, 23f, 4.5f, new Rgba(0.18f, 0.18f, 0.19f), 0.4f, false);
                l.Curve(23f, 23f, 14f, 22f, 12f, 30f, 2.6f, new Rgba(0.15f, 0.15f, 0.16f));
            }, 1f, 0.45f);
            // End bell, bolts and shaft.
            Part(c, l =>
            {
                l.Ellipse(63f, 50f, 8f, 25f, 0f, Paint.Shaded(grey, 55f, 25f, 71f, 75f, 0.3f));
                l.Ellipse(64f, 50f, 5f, 14f, 0f, Paint.Shaded(grey.Lighter(0.12f), 59f, 36f, 69f, 64f, 0.3f));
                foreach (float a in new[] { -70f, -20f, 30f, 80f })
                {
                    float r = a * MathF.PI / 180f;
                    l.Circle(63f + MathF.Cos(r) * 5.4f, 50f + MathF.Sin(r) * 20f, 1.4f, grey.Darker(0.4f));
                }
            }, 1.2f, 0.4f);
            Part(c, l =>
            {
                Rod(l, 66f, 50f, 88f, 50f, 7f, Steel, 0.6f, false);
                l.Ellipse(88f, 50f, 1.8f, 3.5f, 0f, Steel.Lighter(0.2f));
                l.Rect(76f, 46.5f, 86f, 47.6f, Ink.WithAlpha(0.5f));
            }, 0.8f, 0.45f);
            Glint(c, 30f, 31f, 2.4f, 0.6f);
        }
    }
}
