using System;
using System.Collections.Generic;

namespace Afterfall.Presentation.Art
{
    /// <summary>Icons for food, water and medicine.</summary>
    public static partial class ItemIconArt
    {
        /// <summary>A cluster of glossy dark berries under two leaves.</summary>
        private static void Berries(IconCanvas c)
        {
            Rgba leaf = new Rgba(0.36f, 0.54f, 0.24f);
            Part(c, l =>
            {
                Leaf(l, 46f, 30f, 22f, 10f, -150f, leaf);
                Leaf(l, 54f, 28f, 22f, 9f, -30f, leaf.Lighter(0.1f));
                l.Curve(50f, 30f, 52f, 20f, 58f, 13f, 2.2f, new Rgba(0.42f, 0.32f, 0.2f));
            }, 1.2f, 0f);
            Rgba berry = new Rgba(0.50f, 0.12f, 0.30f);
            float[][] cluster =
            {
                new[] { 60f, 42f, 10f }, new[] { 38f, 44f, 10.5f }, new[] { 74f, 58f, 10f }, new[] { 26f, 62f, 10f },
                new[] { 50f, 56f, 12f }, new[] { 64f, 74f, 11f }, new[] { 38f, 76f, 11.5f },
            };
            for (int i = 0; i < cluster.Length; i++)
            {
                float x = cluster[i][0], y = cluster[i][1], r = cluster[i][2];
                Rgba b = i % 3 == 1 ? berry.Darker(0.12f) : berry;
                Part(c, l =>
                {
                    l.Circle(x, y, r, Paint.Radial(b.Lighter(0.42f), b.Darker(0.55f), x - r * 0.35f, y - r * 0.4f, r * 1.25f));
                    l.Ellipse(x - r * 0.38f, y - r * 0.42f, r * 0.26f, r * 0.17f, -35f, White.WithAlpha(0.85f));
                    l.Circle(x + r * 0.3f, y + r * 0.3f, r * 0.12f, b.Lighter(0.3f).WithAlpha(0.5f));
                    // The little crown on top.
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * 72f * MathF.PI / 180f;
                        l.Line(x + r * 0.15f, y - r * 0.05f, x + r * 0.15f + MathF.Cos(a) * r * 0.2f, y - r * 0.05f + MathF.Sin(a) * r * 0.2f, 0.8f, b.Darker(0.6f));
                    }
                }, 1.4f, 0.5f);
            }
        }

        private static void Leaf(IconCanvas c, float bx, float by, float len, float wid, float dirDeg, Rgba col)
        {
            var f = new Frame(bx, by, dirDeg);
            float[] top = IconCanvas.CurvePoints(0f, 0f, len * 0.5f, -wid, len, 0f, 8);
            float[] bot = IconCanvas.CurvePoints(len, 0f, len * 0.5f, wid, 0f, 0f, 8);
            var pts = new float[top.Length + bot.Length];
            Array.Copy(top, pts, top.Length);
            Array.Copy(bot, 0, pts, top.Length, bot.Length);
            float tipX = f.X(len, 0f), tipY = f.Y(len, 0f);
            c.Polygon(Paint.Shaded(col, MathF.Min(bx, tipX) - 4f, MathF.Min(by, tipY) - 4f, MathF.Max(bx, tipX) + 4f, MathF.Max(by, tipY) + 4f, 0.25f), f.P(pts));
            c.Line(bx, by, f.X(len * 0.85f, 0f), f.Y(len * 0.85f, 0f), 0.9f, col.Lighter(0.35f));
            for (int i = 1; i <= 3; i++)
            {
                float t = len * i / 4.5f;
                c.Line(f.X(t, 0f), f.Y(t, 0f), f.X(t + len * 0.14f, -wid * 0.5f), f.Y(t + len * 0.14f, -wid * 0.5f), 0.6f, col.Lighter(0.25f).WithAlpha(0.7f));
                c.Line(f.X(t, 0f), f.Y(t, 0f), f.X(t + len * 0.14f, wid * 0.5f), f.Y(t + len * 0.14f, wid * 0.5f), 0.6f, col.Lighter(0.25f).WithAlpha(0.7f));
            }
        }

        /// <summary>Two plain potatoes, freshly dug.</summary>
        private static void Potatoes(IconCanvas c)
        {
            Rgba skin = new Rgba(0.74f, 0.57f, 0.35f);
            Part(c, l => PotatoBody(l, 61f, 38f, 24f, 16f, -20f, skin.Darker(0.08f), 91), 2.2f, 0f);
            Part(c, l => PotatoBody(l, 42f, 62f, 29f, 20f, 12f, skin, 92), 2.4f, 0.5f);
        }

        /// <summary>A lumpy potato with eyes and earthy specks (no effects; callers wrap it in a part).</summary>
        private static void PotatoBody(IconCanvas c, float cx, float cy, float rx, float ry, float rot, Rgba skin, int seed)
        {
            var f = new Frame(cx, cy, rot);
            Paint p = Paint.Radial(skin.Lighter(0.28f), skin.Darker(0.32f), cx - rx * 0.35f, cy - ry * 0.5f, rx * 1.35f);
            c.Ellipse(cx, cy, rx, ry, rot, p);
            c.Ellipse(f.X(rx * 0.42f, -ry * 0.12f), f.Y(rx * 0.42f, -ry * 0.12f), rx * 0.62f, ry * 0.92f, rot + 8f, p);
            c.Ellipse(f.X(-rx * 0.45f, ry * 0.08f), f.Y(-rx * 0.45f, ry * 0.08f), rx * 0.58f, ry * 0.88f, rot - 6f, p);
            Inside(c, d =>
            {
                Speckle(d, seed, (int)(rx * 1.4f), cx - rx, cy - ry, cx + rx, cy + ry, 0.35f, 0.8f, skin.Darker(0.45f).WithAlpha(0.55f));
                Blotches(d, seed + 3, rx * 0.35f, 0.66f, cx - rx * 1.6f, cy - ry * 1.6f, cx + rx * 1.6f, cy + ry * 1.6f, new Rgba(0.40f, 0.30f, 0.18f, 0.28f));
                // Eyes: a few shallow dimples spread over the tuber (single strokes, never in pairs).
                float[][] eyes = { new[] { -0.55f, -0.25f }, new[] { 0.15f, 0.35f }, new[] { 0.62f, -0.2f } };
                for (int i = 0; i < eyes.Length; i++)
                {
                    float lx = eyes[i][0] * rx, ly = eyes[i][1] * ry;
                    float ex = f.X(lx, ly), ey = f.Y(lx, ly);
                    float a = rot + (IconCanvas.Hash01(seed, i, 9) - 0.5f) * 60f;
                    d.Ellipse(ex, ey, rx * 0.075f, rx * 0.03f, a, skin.Darker(0.5f).WithAlpha(0.85f));
                    d.Ellipse(ex - 0.5f, ey - 0.9f, rx * 0.06f, rx * 0.02f, a, skin.Lighter(0.35f).WithAlpha(0.6f));
                }
            });
        }

        /// <summary>A tin can with a wrap-around label showing a bowl of beans.</summary>
        private static void CannedBeans(IconCanvas c)
        {
            Rgba tin = new Rgba(0.70f, 0.72f, 0.74f);
            Rgba label = new Rgba(0.84f, 0.62f, 0.12f);
            const float l0 = 25f, r0 = 75f, top = 27f, bottom = 84f, ry = 7f;
            Part(c, l =>
            {
                l.Rect(l0, top, r0, bottom, Paint.Cylinder(tin, l0, 50f, r0, 50f, 0.7f));
                l.Ellipse(50f, bottom, 25f, ry, 0f, Paint.Cylinder(tin, l0, 50f, r0, 50f, 0.7f));
                // Label band following the curve of the can.
                Inside(l, d =>
                {
                    float[] upper = Half(50f, 35f, 25f, ry, false);
                    float[] lower = Half(50f, 77f, 25f, ry, true);
                    var band = new float[upper.Length + lower.Length];
                    Array.Copy(upper, band, upper.Length);
                    Array.Copy(lower, 0, band, upper.Length, lower.Length);
                    d.Polygon(Paint.Cylinder(label, l0, 50f, r0, 50f, 0.35f), band);
                    d.EllipseArc(50f, 42.5f, 25f, ry, 0f, 4.5f, 0f, 180f, Paint.Cylinder(new Rgba(0.68f, 0.18f, 0.12f), l0, 50f, r0, 50f, 0.3f));
                    // Bowl of beans on a cream oval.
                    d.Ellipse(50f, 62f, 13f, 9.5f, 0f, new Rgba(0.96f, 0.92f, 0.80f));
                    d.Ellipse(50f, 66f, 10f, 4.5f, 0f, new Rgba(0.42f, 0.30f, 0.22f));
                    Rgba bean = new Rgba(0.62f, 0.24f, 0.14f);
                    float[][] beans = { new[] { 44f, 62f, 20f }, new[] { 50f, 60.5f, -10f }, new[] { 56f, 62f, 25f }, new[] { 47f, 64.5f, -30f }, new[] { 53.5f, 64.5f, 10f } };
                    foreach (float[] b in beans)
                    {
                        d.Ellipse(b[0], b[1], 2.6f, 1.6f, b[2], Paint.Radial(bean.Lighter(0.35f), bean.Darker(0.2f), b[0] - 0.8f, b[1] - 0.6f, 2.8f));
                    }
                    // Rolled ribs near the ends.
                    foreach (float y in new[] { 31f, 80f })
                    {
                        d.EllipseArc(50f, y, 25f, ry, 0f, 1.1f, 0f, 180f, Ink.WithAlpha(0.35f));
                        d.EllipseArc(50f, y + 1.2f, 25f, ry, 0f, 0.8f, 0f, 180f, White.WithAlpha(0.35f));
                    }
                });
            }, 2f, 0f);
            Part(c, l =>
            {
                l.Ellipse(50f, top, 25f, ry, 0f, Paint.Shaded(tin.Lighter(0.2f), 25f, 20f, 75f, 34f, 0.2f));
                l.EllipseRing(50f, top, 22f, ry * 0.82f, 0f, 1.2f, tin.Darker(0.3f));
                l.EllipseRing(50f, top, 16f, ry * 0.6f, 0f, 0.8f, tin.Darker(0.2f).WithAlpha(0.6f));
                l.EllipseRing(61f, top - 0.6f, 4.2f, 1.9f, 0f, 1.4f, tin.Darker(0.35f));
            }, 1f, 0.25f);
        }

        /// <summary>The near (lower) or far half of an ellipse as a polyline, left to right or right to left.</summary>
        private static float[] Half(float cx, float cy, float rx, float ry, bool reverse)
        {
            const int n = 16;
            var pts = new float[(n + 1) * 2];
            for (int i = 0; i <= n; i++)
            {
                float a = MathF.PI * (reverse ? i / (float)n : 1f - i / (float)n);
                pts[i * 2] = cx + MathF.Cos(a) * rx;
                pts[i * 2 + 1] = cy + MathF.Sin(a) * ry;
            }
            return pts;
        }

        /// <summary>A wrapped snack bar with crimped ends and a chocolate-coloured label.</summary>
        private static void SnackBar(IconCanvas c)
        {
            Rgba wrap = new Rgba(0.90f, 0.48f, 0.14f);
            var f = new Frame(50f, 51f, -32f);
            Part(c, l =>
            {
                float[] body = f.P(-30f, -13f, 30f, -13f, 32f, -9f, 32f, 9f, 30f, 13f, -30f, 13f, -32f, 9f, -32f, -9f);
                l.Polygon(Paint.Cylinder(wrap, f.X(0f, -13f), f.Y(0f, -13f), f.X(0f, 13f), f.Y(0f, 13f), 0.45f), body);
                foreach (int side in new[] { -1, 1 })
                {
                    var crimp = new List<float>();
                    float x0 = side * 31f, x1 = side * 40f;
                    crimp.AddRange(new[] { x0, -11f });
                    for (int i = 0; i <= 8; i++) crimp.AddRange(new[] { i % 2 == 0 ? x1 : x1 - side * 2.2f, -11f + i * 22f / 8f });
                    crimp.AddRange(new[] { x0, 11f });
                    float[] pts = f.P(crimp.ToArray());
                    l.Polygon(Paint.Cylinder(wrap.Lighter(0.08f), f.X(0f, -11f), f.Y(0f, -11f), f.X(0f, 11f), f.Y(0f, 11f), 0.5f), pts);
                }
                Inside(l, d =>
                {
                    foreach (int side in new[] { -1, 1 })
                        for (int i = 0; i < 5; i++)
                        {
                            float x = side * (32.5f + i * 1.6f);
                            d.Line(f.X(x, -11f), f.Y(x, -11f), f.X(x, 11f), f.Y(x, 11f), 0.6f, Ink.WithAlpha(0.3f));
                        }
                    // Label: chocolate band, cream stripe and a wavy swoosh.
                    d.Polygon(Paint.Cylinder(new Rgba(0.36f, 0.18f, 0.10f), f.X(0f, -13f), f.Y(0f, -13f), f.X(0f, 13f), f.Y(0f, 13f), 0.35f),
                        f.P(-15f, -14f, 15f, -14f, 15f, 14f, -15f, 14f));
                    d.Polygon(new Rgba(0.96f, 0.86f, 0.62f), f.P(-15f, 4f, 15f, 4f, 15f, 7f, -15f, 7f));
                    d.Polyline(new Rgba(0.98f, 0.80f, 0.30f), 2.2f, f.P(-11f, -2f, -5f, -7f, 2f, -2f, 9f, -7f));
                    d.Line(f.X(-28f, -8.5f), f.Y(-28f, -8.5f), f.X(28f, -8.5f), f.Y(28f, -8.5f), 2.4f, White.WithAlpha(0.35f));
                });
            }, 2f, 0f);
        }

        // ------------------------------------------------------------ bottles

        /// <summary>The outline of a plastic bottle (neck at y 20, body 30..70, base at y 88), cap excluded.</summary>
        private static float[] BottleOutline()
        {
            var pts = new List<float> { 44f, 20f, 56f, 20f };
            pts.AddRange(IconCanvas.CurvePoints(56f, 24f, 57f, 31f, 70f, 38f, 6));
            pts.AddRange(new[] { 70f, 46f, 68f, 50f, 70f, 54f, 70f, 82f });
            pts.AddRange(IconCanvas.CurvePoints(70f, 82f, 70f, 88f, 64f, 88f, 4));
            pts.AddRange(IconCanvas.CurvePoints(36f, 88f, 30f, 88f, 30f, 82f, 4));
            pts.AddRange(new[] { 30f, 54f, 32f, 50f, 30f, 46f, 30f, 38f });
            pts.AddRange(IconCanvas.CurvePoints(30f, 38f, 43f, 31f, 44f, 24f, 6));
            return pts.ToArray();
        }

        /// <summary>A plastic bottle full of clear blue water, capped and labelled.</summary>
        private static void WaterBottle(IconCanvas c)
        {
            Rgba plastic = new Rgba(0.78f, 0.87f, 0.93f);
            Rgba water = new Rgba(0.15f, 0.50f, 0.86f);
            Part(c, l =>
            {
                l.Polygon(Paint.Cylinder(plastic, 30f, 50f, 70f, 50f, 0.6f), BottleOutline());
                Inside(l, d =>
                {
                    d.Rect(0f, 33f, 100f, 100f, Paint.Cylinder(water, 30f, 50f, 70f, 50f, 0.55f));
                    d.Ellipse(50f, 33f, 17.5f, 2.4f, 0f, water.Lighter(0.45f));
                    d.Rect(0f, 54f, 100f, 70f, Paint.Cylinder(new Rgba(0.94f, 0.96f, 0.97f), 30f, 50f, 70f, 50f, 0.3f));
                    d.Rect(0f, 54f, 100f, 56f, new Rgba(0.13f, 0.38f, 0.70f));
                    d.Rect(0f, 68f, 100f, 70f, new Rgba(0.13f, 0.38f, 0.70f));
                    Drop(d, 50f, 62.5f, 4.6f, new Rgba(0.16f, 0.52f, 0.88f));
                    foreach (float y in new[] { 44f, 76f, 81f })
                    {
                        d.Line(30f, y, 70f, y, 1f, White.WithAlpha(0.35f));
                        d.Line(30f, y + 1.2f, 70f, y + 1.2f, 0.8f, Ink.WithAlpha(0.2f));
                    }
                    d.RoundRect(36f, 62f, 3.4f, 44f, 1.7f, 0f, White.WithAlpha(0.55f));
                    d.Curve(39f, 30f, 42f, 27f, 46f, 24f, 2f, White.WithAlpha(0.6f));
                });
            }, 1.8f, 0f, 0.4f, 0.32f);
            BottleCap(c, new Rgba(0.13f, 0.40f, 0.78f));
        }

        /// <summary>The same bottle, empty, capless and scuffed: salvage waiting to be filled.</summary>
        private static void EmptyBottle(IconCanvas c)
        {
            Rgba plastic = new Rgba(0.74f, 0.83f, 0.86f);
            Part(c, l =>
            {
                l.Polygon(Paint.Cylinder(plastic, 30f, 50f, 70f, 50f, 0.75f), BottleOutline());
                Inside(l, d =>
                {
                    // See-through: the far wall is a little darker in the middle.
                    d.Rect(36f, 34f, 64f, 86f, Paint.Stops(36f, 0f, 64f, 0f, new[] { 0f, 0.5f, 1f }, Ink.WithAlpha(0f), Ink.WithAlpha(0.16f), Ink.WithAlpha(0f)));
                    // Dirt settled at the bottom and a smeared, torn label.
                    d.Rect(0f, 80f, 100f, 100f, new Rgba(0.45f, 0.36f, 0.24f, 0.45f));
                    Speckle(d, 101, 14, 32f, 70f, 68f, 86f, 0.5f, 1.4f, new Rgba(0.40f, 0.30f, 0.20f, 0.6f));
                    d.Polygon(new Rgba(0.86f, 0.84f, 0.76f, 0.8f), 30f, 56f, 52f, 56f, 48f, 60f, 54f, 63f, 47f, 67f, 30f, 67f);
                    // A crumple dent.
                    d.Curve(58f, 38f, 64f, 46f, 60f, 54f, 1.4f, Ink.WithAlpha(0.22f));
                    d.Curve(59.2f, 38f, 65.2f, 46f, 61.2f, 54f, 0.8f, White.WithAlpha(0.5f));
                    foreach (float y in new[] { 44f, 76f })
                        d.Line(30f, y, 70f, y, 1f, White.WithAlpha(0.4f));
                    d.RoundRect(36f, 60f, 3.4f, 46f, 1.7f, 0f, White.WithAlpha(0.7f));
                    d.RoundRect(66f, 62f, 1.6f, 36f, 0.8f, 0f, White.WithAlpha(0.35f));
                    d.Curve(39f, 30f, 42f, 27f, 46f, 24f, 2f, White.WithAlpha(0.7f));
                });
            }, 1.8f, 0f, 0.4f, 0.32f);
            // Bare threaded neck, no cap.
            Part(c, l =>
            {
                l.RoundRect(50f, 17.5f, 13f, 9f, 1.4f, 0f, Paint.Cylinder(plastic.Darker(0.05f), 43f, 17f, 57f, 17f, 0.6f));
                l.Ellipse(50f, 13.2f, 6.5f, 1.6f, 0f, new Rgba(0.30f, 0.34f, 0.36f));
                for (int i = 0; i < 3; i++) l.Line(43.5f, 15.5f + i * 2.4f, 56.5f, 14.8f + i * 2.4f, 0.8f, Ink.WithAlpha(0.35f));
                l.Rect(41f, 20f, 59f, 23f, Paint.Cylinder(plastic.Darker(0.1f), 41f, 21f, 59f, 21f, 0.5f));
            }, 0.8f, 0.3f);
        }

        private static void BottleCap(IconCanvas c, Rgba col)
        {
            Part(c, l =>
            {
                l.Rect(41f, 20f, 59f, 23f, Paint.Cylinder(col.Lighter(0.1f), 41f, 21f, 59f, 21f, 0.5f));
                l.RoundRect(50f, 15f, 16f, 10f, 2f, 0f, Paint.Cylinder(col, 42f, 15f, 58f, 15f, 0.6f));
                for (int i = 0; i < 6; i++) l.Line(44f + i * 2.4f, 11f, 44f + i * 2.4f, 19f, 0.6f, Ink.WithAlpha(0.3f));
            }, 1f, 0.35f);
        }

        /// <summary>A water drop pointing up, centred on its round part.</summary>
        private static void Drop(IconCanvas c, float x, float y, float r, Paint p)
        {
            c.Circle(x, y, r, p);
            c.Polygon(p, x - r * 0.86f, y - r * 0.5f, x, y - r * 2.1f, x + r * 0.86f, y - r * 0.5f);
        }

        // ------------------------------------------------------------ medicine

        /// <summary>A roll of gauze with its tail unwound and held by a clip.</summary>
        private static void Bandage(IconCanvas c)
        {
            Rgba gauze = new Rgba(0.93f, 0.92f, 0.88f);
            // The unrolled tail.
            Part(c, l =>
            {
                float[] top = IconCanvas.CurvePoints(36f, 58f, 64f, 70f, 88f, 60f, 10);
                float[] bot = IconCanvas.CurvePoints(90f, 74f, 64f, 84f, 38f, 74f, 10);
                var tail = new float[top.Length + bot.Length];
                Array.Copy(top, tail, top.Length);
                Array.Copy(bot, 0, tail, top.Length, bot.Length);
                l.Polygon(Paint.Shaded(gauze.Darker(0.05f), 36f, 58f, 90f, 84f, 0.18f), tail);
                Inside(l, d =>
                {
                    for (int i = -12; i <= 12; i++)
                    {
                        d.Line(40f + i * 3f, 50f, 60f + i * 3f, 90f, 0.5f, Ink.WithAlpha(0.12f));
                        d.Line(60f + i * 3f, 50f, 40f + i * 3f, 90f, 0.5f, Ink.WithAlpha(0.1f));
                    }
                    d.Curve(38f, 66f, 64f, 77f, 89f, 67f, 1.1f, new Rgba(0.45f, 0.62f, 0.85f, 0.7f));
                });
                // Metal clip near the end.
                l.Polygon(Paint.Shaded(Steel, 76f, 60f, 86f, 78f, 0.3f), 77f, 63f, 84f, 61.5f, 86f, 75.5f, 79f, 77f);
                for (int i = 0; i < 3; i++) l.Line(78.5f + i * 2.2f, 63f, 80f + i * 2.2f, 75.5f, 0.6f, Ink.WithAlpha(0.45f));
            }, 1.6f, 0f);
            // The roll: body seen at three-quarters with a spiral face.
            const float fx = 38f, fy = 44f, rr = 23f;
            float bx = fx + 16f, by = fy - 7f;
            Part(c, l =>
            {
                float dx = bx - fx, dy = by - fy, len = MathF.Sqrt(dx * dx + dy * dy);
                float nx = -dy / len * rr, ny = dx / len * rr;
                l.Ellipse(bx, by, rr * 0.82f, rr, -20f, gauze.Darker(0.1f));
                l.Polygon(Paint.Shaded(gauze, fx - rr, fy - rr, bx + rr, by + rr, 0.2f), fx + nx, fy + ny, bx + nx, by + ny, bx - nx, by - ny, fx - nx, fy - ny);
                Inside(l, d =>
                {
                    for (int i = 0; i < 4; i++) d.Line(fx + 4f + i * 4f, fy - rr, bx + 4f + i * 4f, by + rr, 0.5f, Ink.WithAlpha(0.08f));
                });
            }, 1.8f, 0.4f);
            Part(c, l =>
            {
                l.Ellipse(fx, fy, rr * 0.82f, rr, -20f, Paint.Radial(gauze.Lighter(0.3f), gauze.Darker(0.12f), fx - 6f, fy - 8f, rr * 1.2f));
                var spiral = new float[2 * 60];
                for (int i = 0; i < 60; i++)
                {
                    float t = i / 59f;
                    float a = t * MathF.PI * 2f * 3.2f;
                    float rad = 5.5f + t * (rr - 7.5f);
                    float lx = MathF.Cos(a) * rad * 0.82f, ly = MathF.Sin(a) * rad;
                    spiral[i * 2] = fx + lx * 0.94f + ly * 0.34f;
                    spiral[i * 2 + 1] = fy - lx * 0.34f + ly * 0.94f;
                }
                l.Polyline(gauze.Darker(0.3f).WithAlpha(0.8f), 0.8f, spiral);
                l.Ellipse(fx, fy, 4.4f, 5.2f, -20f, new Rgba(0.55f, 0.52f, 0.46f));
                l.Ellipse(fx + 0.6f, fy + 0.6f, 3f, 3.8f, -20f, new Rgba(0.28f, 0.26f, 0.22f));
            }, 1.4f, 0.2f, 0.4f, 0.3f);
        }

        /// <summary>A red first-aid case with a white cross, lid seam, latches and a carry handle.</summary>
        private static void Medkit(IconCanvas c)
        {
            Rgba red = new Rgba(0.80f, 0.20f, 0.16f);
            var iso = new Iso(16f, 86f, 1f, 0f, 0.55f, -0.42f);
            const float w = 56f, h = 40f, d = 22f;
            Part(c, l =>
            {
                iso.Box(l, 0f, 0f, 0f, w, h, d, red, 0.22f, 0.36f);
                l.Grain(111, 3f, 3f, 0.08f);
                Inside(l, k =>
                {
                    // Lid seam around the box.
                    k.Polyline(Ink.WithAlpha(0.45f), 1.3f, iso.P(0f, 30f, 0f, w, 30f, 0f, w, 30f, d));
                    k.Polyline(White.WithAlpha(0.25f), 0.8f, iso.P(0f, 29f, 0f, w, 29f, 0f, w, 29f, d));
                    // Rubber corner guards at the bottom.
                    k.Polygon(new Rgba(0.22f, 0.2f, 0.2f), iso.P(0f, 0f, 0f, 7f, 0f, 0f, 7f, 5f, 0f, 0f, 5f, 0f));
                    k.Polygon(new Rgba(0.22f, 0.2f, 0.2f), iso.P(w - 7f, 0f, 0f, w, 0f, 0f, w, 5f, 0f, w - 7f, 5f, 0f));
                    k.Polygon(new Rgba(0.16f, 0.14f, 0.14f), iso.P(w, 0f, 0f, w, 0f, 7f, w, 5f, 7f, w, 5f, 0f));
                });
            }, 2.2f, 0f);
            // White cross on the front.
            Part(c, l =>
            {
                float cx = iso.SX(w * 0.5f, 15f, 0f), cy = iso.SY(w * 0.5f, 15f, 0f);
                Paint white = Paint.Shaded(new Rgba(0.97f, 0.96f, 0.93f), cx - 12f, cy - 12f, cx + 12f, cy + 12f, 0.12f);
                l.RoundRect(cx, cy, 22f, 7.5f, 1.2f, 0f, white);
                l.RoundRect(cx, cy, 7.5f, 22f, 1.2f, 0f, white);
            }, 1f, 0.35f);
            // Latches over the seam and the carry handle on the lid.
            Part(c, l =>
            {
                foreach (float x in new[] { 12f, 44f })
                {
                    float lx = iso.SX(x, 30f, 0f), ly = iso.SY(x, 30f, 0f);
                    l.RoundRect(lx, ly, 6f, 8f, 1f, 0f, Paint.Shaded(Steel, lx - 3f, ly - 4f, lx + 3f, ly + 4f, 0.3f));
                }
                float hx0 = iso.SX(18f, h, d * 0.5f), hy0 = iso.SY(18f, h, d * 0.5f);
                float hx1 = iso.SX(38f, h, d * 0.5f), hy1 = iso.SY(38f, h, d * 0.5f);
                l.Curve(hx0, hy0, (hx0 + hx1) * 0.5f, hy0 - 20f, hx1, hy1, 5f, Paint.Shaded(new Rgba(0.24f, 0.22f, 0.22f), hx0, hy0 - 12f, hx1, hy1, 0.3f));
                l.Rect(hx0 - 3f, hy0 - 2f, hx0 + 3f, hy0 + 1.5f, new Rgba(0.18f, 0.16f, 0.16f));
                l.Rect(hx1 - 3f, hy1 - 2f, hx1 + 3f, hy1 + 1.5f, new Rgba(0.18f, 0.16f, 0.16f));
            }, 1.2f, 0.35f);
        }
    }
}
