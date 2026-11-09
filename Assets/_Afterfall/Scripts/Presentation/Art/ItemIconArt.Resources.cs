using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>Icons for raw resources and salvage: logs, rocks, ore, fibre, seeds, scrap, rags, charcoal.</summary>
    public static partial class ItemIconArt
    {
        /// <summary>A sawn log lying diagonally, bark furrows along it and growth rings on the cut face.</summary>
        private static void Log(IconCanvas c)
        {
            const float ax = 0.866f, ay = -0.5f;   // along the log, toward the cut face
            const float nx = -0.5f, ny = -0.866f;  // across it, toward the light
            const float r = 18.5f, minor = 9.8f;
            float bx = 18f, by = 74f, fx = 73f, fy = 42.2f;
            float mx = (bx + fx) * 0.5f, my = (by + fy) * 0.5f;
            Paint bark = Paint.Cylinder(Bark, mx + nx * r, my + ny * r, mx - nx * r, my - ny * r, 0.25f);
            Part(c, l =>
            {
                l.Polygon(bark, bx + nx * r, by + ny * r, fx + nx * r, fy + ny * r, fx - nx * r, fy - ny * r, bx - nx * r, by - ny * r);
                l.Ellipse(bx, by, minor, r, -30f, bark);
                l.Grain(11, 14f, 2.2f, 0.3f, -30f);
                Inside(l, d =>
                {
                    for (int k = -4; k <= 4; k++)
                    {
                        float o = k * 3.7f + (IconCanvas.Hash01(3, k, 0) - 0.5f) * 1.4f;
                        var pts = new float[16];
                        for (int i = 0; i < 8; i++)
                        {
                            float t = -14f + i * 10f;
                            float j = (IconCanvas.Hash01(5, k, i) - 0.5f) * 1.6f;
                            pts[i * 2] = bx + ax * t + nx * (o + j);
                            pts[i * 2 + 1] = by + ay * t + ny * (o + j);
                        }
                        d.Polyline(Bark.Darker(0.55f).WithAlpha(0.85f), 1.3f, pts);
                        for (int i = 0; i < 8; i++)
                        {
                            pts[i * 2] += nx * 1.5f;
                            pts[i * 2 + 1] += ny * 1.5f;
                        }
                        d.Polyline(Bark.Lighter(0.3f).WithAlpha(0.35f), 0.8f, pts);
                    }
                });
            }, 2.4f, 0f);

            // A sawn-off branch stub on the upper side.
            float sx = mx - ax * 9f + nx * (r - 3f), sy = my - ay * 9f + ny * (r - 3f);
            Part(c, l =>
            {
                Rod(l, sx, sy, sx - 3.5f, sy - 6.5f, 8f, Bark, 0.2f, false);
                l.Ellipse(sx - 3.5f, sy - 6.5f, 4f, 2.6f, -25f, Bark.Darker(0.2f));
                l.Ellipse(sx - 3.6f, sy - 6.7f, 3f, 1.8f, -25f, WoodLight);
            }, 1.2f, 0.3f);

            // The cut face: bark rim, sapwood, heartwood, rings, pith and drying cracks.
            Rgba face = new Rgba(0.88f, 0.71f, 0.47f);
            Part(c, l =>
            {
                l.Ellipse(fx, fy, minor, r, -30f, Bark.Darker(0.15f));
                l.Ellipse(fx + 0.5f, fy + 0.3f, minor * 0.84f, r * 0.87f, -30f,
                    Paint.Radial(face.Lighter(0.18f), face.Darker(0.16f), fx - 2f, fy - 3f, r));
                l.Ellipse(fx + 0.5f, fy + 0.3f, minor * 0.4f, r * 0.42f, -30f, face.Darker(0.14f).WithAlpha(0.6f));
                foreach (float s in new[] { 0.24f, 0.46f, 0.66f })
                    l.EllipseRing(fx + 0.5f, fy + 0.3f, minor * 0.84f * s * 1.2f, r * 0.87f * s * 1.2f, -30f, 0.9f, face.Darker(0.4f).WithAlpha(0.55f));
                l.Circle(fx + 0.5f, fy + 0.3f, 1.3f, face.Darker(0.55f));
                foreach (float phi in new[] { 200f, 330f })
                {
                    float p = phi * MathF.PI / 180f;
                    float lx = MathF.Cos(p) * minor * 0.8f, ly = MathF.Sin(p) * r * 0.8f;
                    float ex = fx + lx * ax - ly * ay, ey = fy + lx * ay + ly * ax;
                    l.Line(fx + 0.5f, fy + 0.3f, ex, ey, 0.9f, face.Darker(0.55f).WithAlpha(0.8f));
                }
            }, 1.4f, 0.2f, 0.4f, 0.3f);
        }

        /// <summary>A faceted grey boulder with lit top planes, pores and a crack.</summary>
        private static void Stone(IconCanvas c)
        {
            Rgba rock = RockGrey;
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(rock, 15f, 20f, 85f, 85f, 0.18f), 13, 64, 19, 40, 35, 24, 60, 18, 80, 28, 89, 54, 81, 76, 55, 86, 27, 82);
                l.Polygon(Paint.Shaded(rock.Lighter(0.34f), 20f, 18f, 70f, 46f, 0.12f), 19, 40, 35, 24, 60, 18, 80, 28, 62, 42, 33, 47);
                l.Polygon(Paint.Shaded(rock.Lighter(0.12f), 13f, 40f, 33f, 70f, 0.1f), 13, 64, 19, 40, 33, 47, 29, 68);
                l.Polygon(Paint.Shaded(rock.Darker(0.32f), 62f, 30f, 89f, 80f, 0.12f), 62, 42, 80, 28, 89, 54, 81, 76, 64, 64);
                l.Polygon(rock.Darker(0.14f), 29, 68, 33, 47, 62, 42, 64, 64, 55, 86, 27, 82);
                l.Grain(5, 6f, 6f, 0.17f);
                Inside(l, d =>
                {
                    Speckle(d, 21, 40, 14f, 20f, 88f, 86f, 0.5f, 1.2f, rock.Darker(0.5f).WithAlpha(0.6f));
                    Speckle(d, 22, 26, 14f, 20f, 88f, 86f, 0.4f, 0.9f, rock.Lighter(0.5f).WithAlpha(0.5f));
                    d.Polyline(rock.Darker(0.6f), 1.2f, 31, 60, 36, 62.5f, 40, 61.5f, 45, 66, 48, 66.5f, 52, 72, 51, 77);
                    d.Polyline(rock.Lighter(0.35f).WithAlpha(0.55f), 0.7f, 31, 58.8f, 36, 61.3f, 40, 60.3f, 45, 64.8f, 48.6f, 65.3f, 52.8f, 70.8f);
                    d.Polyline(rock.Darker(0.5f).WithAlpha(0.8f), 0.8f, 45, 66, 43, 71, 44, 75);
                    // A little lichen on the top.
                    Speckle(d, 23, 9, 30f, 24f, 52f, 34f, 1f, 2.2f, new Rgba(0.62f, 0.66f, 0.42f, 0.55f));
                });
            }, 2.6f, 0f, 0.36f, 0.42f);
        }

        /// <summary>A rust-red ore chunk with orange oxide patches and glinting metallic crystals.</summary>
        private static void IronOre(IconCanvas c)
        {
            Rgba ore = new Rgba(0.45f, 0.28f, 0.21f);
            Rgba rust = new Rgba(0.78f, 0.42f, 0.17f);
            Rgba metal = new Rgba(0.56f, 0.58f, 0.64f);
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(ore, 12f, 16f, 88f, 86f, 0.2f), 11, 60, 21, 34, 39, 24, 52, 30, 66, 15, 87, 31, 89, 60, 73, 83, 43, 87, 19, 80);
                l.Polygon(Paint.Shaded(ore.Lighter(0.3f), 20f, 15f, 70f, 45f, 0.1f), 21, 34, 39, 24, 52, 30, 66, 15, 87, 31, 68, 42, 46, 44, 30, 46);
                l.Polygon(Paint.Shaded(ore.Darker(0.3f), 68f, 31f, 89f, 83f, 0.1f), 68, 42, 87, 31, 89, 60, 73, 83, 66, 62);
                l.Polygon(ore.Lighter(0.1f), 11, 60, 21, 34, 30, 46, 27, 64);
                l.Grain(31, 5f, 5f, 0.2f);
                Inside(l, d =>
                {
                    Speckle(d, 32, 16, 14f, 22f, 86f, 84f, 2.5f, 6.5f, rust.WithAlpha(0.42f));
                    Speckle(d, 33, 30, 14f, 22f, 86f, 84f, 0.6f, 1.6f, rust.Lighter(0.2f).WithAlpha(0.6f));
                    Speckle(d, 34, 30, 14f, 22f, 86f, 84f, 0.5f, 1.2f, ore.Darker(0.55f).WithAlpha(0.7f));
                });
            }, 2.6f, 0f, 0.34f, 0.42f);
            // Metallic crystals (little cubes) breaking the surface.
            float[][] cubes = { new[] { 38f, 54f, 6f }, new[] { 58f, 40f, 5f }, new[] { 66f, 62f, 6.5f }, new[] { 30f, 36f, 4f }, new[] { 48f, 72f, 4.5f } };
            Part(c, l =>
            {
                foreach (float[] q in cubes)
                {
                    float x = q[0], y = q[1], s = q[2];
                    l.Polygon(metal.Lighter(0.35f), x - s, y - s * 0.3f, x, y - s * 0.75f, x + s, y - s * 0.3f, x, y + s * 0.15f);
                    l.Polygon(metal, x - s, y - s * 0.3f, x, y + s * 0.15f, x, y + s, x - s, y + s * 0.5f);
                    l.Polygon(metal.Darker(0.4f), x, y + s * 0.15f, x + s, y - s * 0.3f, x + s, y + s * 0.5f, x, y + s);
                }
            }, 0.8f, 0.4f, 0.4f, 0.3f);
            Glint(c, 57f, 36.5f, 3.2f);
            Glint(c, 37f, 49.5f, 2.6f, 0.8f);
            Glint(c, 65.5f, 57.5f, 2.4f, 0.7f);
        }

        /// <summary>A bundle of grass-like fibre blades tied with twine.</summary>
        private static void Fiber(IconCanvas c)
        {
            Rgba[] greens =
            {
                new Rgba(0.40f, 0.55f, 0.24f), new Rgba(0.52f, 0.65f, 0.29f), new Rgba(0.64f, 0.70f, 0.36f),
                new Rgba(0.73f, 0.69f, 0.40f), new Rgba(0.47f, 0.62f, 0.27f)
            };
            // base x, tie x, control x, control y, tip x, tip y (back blades first)
            float[][] blades =
            {
                new[] { 42f, 46f, 30f, 40f, 15f, 24f },
                new[] { 58f, 54f, 70f, 40f, 86f, 26f },
                new[] { 44f, 47f, 38f, 34f, 29f, 11f },
                new[] { 56f, 53f, 62f, 34f, 72f, 12f },
                new[] { 47f, 48.5f, 46f, 32f, 44f, 8f },
                new[] { 53f, 51.5f, 55f, 32f, 58f, 8f },
                new[] { 49f, 49.5f, 41f, 46f, 33f, 32f },
                new[] { 51f, 50.5f, 60f, 46f, 67f, 34f },
                new[] { 50f, 50f, 51f, 40f, 51f, 18f },
            };
            for (int i = 0; i < blades.Length; i++)
            {
                float[] b = blades[i];
                Rgba col = greens[i % greens.Length];
                if (i < 2) col = col.Darker(0.15f);
                float[] upper = IconCanvas.CurvePoints(b[1], 62f, b[2], b[3], b[4], b[5], 10);
                var spine = new float[upper.Length + 2];
                spine[0] = b[0];
                spine[1] = 88f;
                Array.Copy(upper, 0, spine, 2, upper.Length);
                Part(c, l =>
                {
                    Strip(l, spine, 2.6f, 0.2f, Paint.Shaded(col, 20f, 10f, 80f, 90f, 0.22f));
                    Inside(l, d => d.Polyline(col.Lighter(0.4f).WithAlpha(0.45f), 0.7f, spine));
                }, 1f, 0.3f, 0.3f, 0.3f);
            }
            // Cut ends at the bottom.
            Part(c, l =>
            {
                for (int i = 0; i < blades.Length; i++)
                    l.Ellipse(blades[i][0], 88f, 2.4f, 1.1f, 0f, new Rgba(0.80f, 0.80f, 0.52f));
            }, 0f, 0f);
            // Twine wraps and a knot with loose ends.
            Part(c, l =>
            {
                for (int k = 0; k < 3; k++)
                {
                    float y = 59f + k * 3.2f;
                    l.Curve(41.5f, y, 50f, y + 2.6f, 58.5f, y, 3.2f, Ink.WithAlpha(0.8f));
                    l.Curve(41.5f, y, 50f, y + 2.6f, 58.5f, y, 2.2f, Paint.Shaded(Twine, 40f, 58f, 60f, 70f, 0.25f));
                }
                l.Circle(59f, 63f, 3f, Paint.Shaded(Twine, 56f, 60f, 62f, 66f, 0.25f));
                l.Curve(60f, 64f, 64f, 68f, 63f, 76f, 1.8f, Twine.Darker(0.1f));
                l.Curve(61f, 63f, 67f, 65f, 70f, 72f, 1.8f, Twine);
            }, 1f, 0.45f);
        }

        /// <summary>An open burlap sack of sprouting seed potatoes.</summary>
        private static void SeedPotatoes(IconCanvas c)
        {
            Rgba burlap = new Rgba(0.68f, 0.55f, 0.35f);
            float[] right = IconCanvas.CurvePoints(79f, 42f, 93f, 66f, 74f, 89f, 8);
            float[] bottom = IconCanvas.CurvePoints(74f, 89f, 50f, 94f, 26f, 89f, 6);
            float[] left = IconCanvas.CurvePoints(26f, 89f, 7f, 66f, 21f, 42f, 8);
            var sack = new float[right.Length + bottom.Length + left.Length];
            Array.Copy(right, 0, sack, 0, right.Length);
            Array.Copy(bottom, 0, sack, right.Length, bottom.Length);
            Array.Copy(left, 0, sack, right.Length + bottom.Length, left.Length);
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(burlap, 14f, 40f, 86f, 92f, 0.26f), sack);
                l.Grain(41, 1.6f, 1.6f, 0.18f, 45f);
                Inside(l, d =>
                {
                    for (int i = -10; i <= 10; i++)
                    {
                        d.Line(10f + i * 4f, 40f, 10f + i * 4f + 60f, 100f, 0.5f, burlap.Darker(0.35f).WithAlpha(0.3f));
                        d.Line(90f + i * 4f, 40f, 90f + i * 4f - 60f, 100f, 0.5f, burlap.Darker(0.35f).WithAlpha(0.22f));
                    }
                    // Stencilled sprout mark.
                    Rgba ink = new Rgba(0.20f, 0.32f, 0.16f, 0.7f);
                    d.Line(50f, 82f, 50f, 64f, 2f, ink);
                    d.Ellipse(44.5f, 66f, 5.5f, 2.6f, 30f, ink);
                    d.Ellipse(55.5f, 63f, 5.5f, 2.6f, -30f, ink);
                    // Side seam stitches.
                    for (int i = 0; i < 6; i++) d.Line(80f - i * 0.6f, 52f + i * 6f, 83f - i * 0.6f, 54f + i * 6f, 1f, burlap.Darker(0.5f).WithAlpha(0.7f));
                });
            }, 2.4f, 0f);
            Part(c, l => l.Ellipse(50f, 42f, 29f, 8.5f, 0f, Paint.Linear(new Rgba(0.16f, 0.11f, 0.07f), new Rgba(0.30f, 0.22f, 0.14f), 50f, 34f, 50f, 50f)), 0f, 0f);
            Rgba skin = new Rgba(0.76f, 0.58f, 0.36f);
            Part(c, l =>
            {
                PotatoBody(l, 38f, 37f, 11.5f, 8f, -12f, skin, 51);
                PotatoBody(l, 62f, 36f, 12f, 8.5f, 14f, skin.Darker(0.06f), 52);
                PotatoBody(l, 50f, 32f, 10.5f, 7.5f, 4f, skin.Lighter(0.05f), 53);
            }, 1.6f, 0.45f);
            // Sprouts: pale stems turning green, each with a pair of tiny leaves.
            Rgba stem = new Rgba(0.86f, 0.78f, 0.80f);
            Rgba leaf = new Rgba(0.42f, 0.64f, 0.26f);
            Part(c, l =>
            {
                float[][] sprouts = { new[] { 47f, 27f, 44f, 18f, 40f, 12f }, new[] { 64f, 30f, 68f, 22f, 66f, 15f }, new[] { 34f, 32f, 30f, 26f, 25f, 22f } };
                foreach (float[] s in sprouts)
                {
                    l.Curve(s[0], s[1], s[2], s[3], s[4], s[5], 2.6f, Paint.Linear(stem, leaf, s[0], s[1], s[4], s[5]));
                    l.Ellipse(s[4] - 2.6f, s[5] + 0.5f, 3.6f, 1.9f, -35f, leaf);
                    l.Ellipse(s[4] + 2.6f, s[5] - 0.5f, 3.6f, 1.9f, 35f, leaf.Lighter(0.15f));
                }
            }, 0.8f, 0.3f);
            // The rolled lip of the sack in front of the potatoes.
            Part(c, l =>
            {
                l.EllipseArc(50f, 42f, 29f, 8.5f, 0f, 7f, -8f, 196f, Paint.Shaded(burlap.Lighter(0.08f), 20f, 40f, 80f, 56f, 0.25f));
                l.Grain(42, 1.4f, 1.4f, 0.18f, -45f);
            }, 1.6f, 0.4f);
        }

        /// <summary>A torn, corrugated sheet of rusting metal in front of a bent angle bar.</summary>
        private static void ScrapMetal(IconCanvas c)
        {
            Rgba sheet = new Rgba(0.57f, 0.60f, 0.64f);
            Rgba rust = RustBrown;
            Part(c, l =>
            {
                l.Polyline(Paint.Shaded(rust.Lighter(0.1f), 14f, 14f, 90f, 60f, 0.25f), 8f, 16f, 30f, 44f, 17f, 84f, 40f);
                l.Polyline(rust.Darker(0.35f), 2.4f, 16f, 34f, 44f, 21f, 84f, 44f);
                l.Grain(61, 3f, 3f, 0.3f);
                Inside(l, d => Speckle(d, 62, 20, 14f, 12f, 88f, 50f, 0.6f, 1.6f, rust.Lighter(0.35f).WithAlpha(0.7f)));
            }, 1.6f, 0f);
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(sheet, 12f, 28f, 88f, 90f, 0.24f),
                    12, 58, 24, 38, 46, 30, 54, 34, 60, 30, 72, 32, 86, 45, 81, 52, 88, 60, 82, 66, 84, 72, 70, 84, 58, 80, 50, 88, 40, 84, 30, 88, 20, 78);
                Inside(l, d =>
                {
                    // Corrugations: alternating lit and shaded ridges.
                    float px = 0.89f, py = -0.45f, dx = 0.45f, dy = 0.89f;
                    for (int i = -7; i <= 7; i++)
                    {
                        float cx = 50f + px * i * 7f, cy = 59f + py * i * 7f;
                        d.Line(cx - dx * 50f - px * 1.6f, cy - dy * 50f - py * 1.6f, cx + dx * 50f - px * 1.6f, cy + dy * 50f - py * 1.6f, 2.2f, White.WithAlpha(0.28f));
                        d.Line(cx - dx * 50f + px * 1.6f, cy - dy * 50f + py * 1.6f, cx + dx * 50f + px * 1.6f, cy + dy * 50f + py * 1.6f, 2.2f, Ink.WithAlpha(0.28f));
                    }
                    // The bent half is in shade.
                    d.Polygon(Ink.WithAlpha(0.22f), 52, 20, 100, 20, 100, 100, 72, 100, 60, 64);
                    d.Line(52f, 20f, 60f, 64f, 1.4f, White.WithAlpha(0.45f));
                    d.Line(60f, 64f, 72f, 100f, 1.4f, White.WithAlpha(0.45f));
                    Blotches(d, 63, 7f, 0.6f, 10f, 26f, 90f, 90f, rust.WithAlpha(0.6f));
                    Blotches(d, 64, 4f, 0.66f, 10f, 26f, 90f, 90f, rust.Darker(0.25f).WithAlpha(0.6f));
                    Speckle(d, 65, 30, 14f, 30f, 88f, 88f, 0.4f, 1.1f, rust.Darker(0.3f).WithAlpha(0.7f));
                });
                l.Grain(66, 4f, 4f, 0.12f);
                l.Cut(k =>
                {
                    k.Circle(30f, 52f, 2.6f, Ink);
                    k.Circle(70f, 44f, 2.4f, Ink);
                    k.Circle(44f, 74f, 2.6f, Ink);
                });
                foreach (float[] h in new[] { new[] { 30f, 52f }, new[] { 70f, 44f }, new[] { 44f, 74f } })
                    l.Ring(h[0], h[1], 3.6f, 1.6f, sheet.Darker(0.35f));
            }, 2.2f, 0.45f);
        }

        /// <summary>A stack of three folded rags: faded denim, red flannel and beige cotton with a frayed edge.</summary>
        private static void ClothRags(IconCanvas c)
        {
            FoldedCloth(c, 51f, 73f, 70f, 17f, -3f, new Rgba(0.38f, 0.47f, 0.58f), 71, false);
            FoldedCloth(c, 47f, 58f, 64f, 17f, 4f, new Rgba(0.66f, 0.27f, 0.21f), 72, true);
            FoldedCloth(c, 52f, 42f, 58f, 17f, -4f, new Rgba(0.82f, 0.74f, 0.58f), 73, false);
            // A frayed corner hanging off the top rag.
            Rgba beige = new Rgba(0.80f, 0.72f, 0.56f);
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(beige, 70f, 40f, 86f, 66f, 0.2f), 74, 40, 82, 42, 86, 60, 80, 62, 76, 56);
                for (int i = 0; i < 4; i++) l.Line(80f + i * 1.6f, 61f - i * 0.4f, 79.5f + i * 2f, 66f + (i % 2) * 1.5f, 0.9f, beige.Darker(0.15f));
                Inside(l, d => d.Line(77f, 44f, 81f, 58f, 0.8f, beige.Darker(0.35f).WithAlpha(0.6f)));
            }, 1.2f, 0.35f);
        }

        private static void FoldedCloth(IconCanvas c, float cx, float cy, float w, float h, float rot, Rgba col, int seed, bool plaid)
        {
            var f = new Frame(cx, cy, rot);
            Part(c, l =>
            {
                // A soft slab: wavy top surface over a rounded front fold.
                float hw = w * 0.5f, hh = h * 0.5f;
                l.RoundRect(cx, cy, w, h, 6f, rot, Paint.Shaded(col, cx - hw, cy - hh, cx + hw, cy + hh, 0.18f));
                for (int i = 0; i < 4; i++)
                {
                    float lx = -hw + 9f + i * (w - 18f) / 3f + (IconCanvas.Hash01(seed, i, 1) - 0.5f) * 4f;
                    l.Ellipse(f.X(lx, -hh + 1.5f), f.Y(lx, -hh + 1.5f), 5f + IconCanvas.Hash01(seed, i, 0) * 3f, 1.8f, rot, col.Lighter(0.06f));
                }
                l.Grain(seed, 1.2f, 1.2f, 0.12f, rot);
                Inside(l, d =>
                {
                    if (plaid)
                    {
                        for (int i = -6; i <= 6; i++)
                        {
                            d.Line(f.X(i * 5.5f, -20f), f.Y(i * 5.5f, -20f), f.X(i * 5.5f, 20f), f.Y(i * 5.5f, 20f), 1.6f, Ink.WithAlpha(0.22f));
                            d.Line(f.X(i * 5.5f + 2f, -20f), f.Y(i * 5.5f + 2f, -20f), f.X(i * 5.5f + 2f, 20f), f.Y(i * 5.5f + 2f, 20f), 0.6f, White.WithAlpha(0.18f));
                        }
                        d.Line(f.X(-40f, -1f), f.Y(-40f, -1f), f.X(40f, -1f), f.Y(40f, -1f), 2f, Ink.WithAlpha(0.22f));
                    }
                    // The front fold: a darker band with a soft crease highlight.
                    d.Polygon(Ink.WithAlpha(0.2f), f.P(-hw - 2f, hh * 0.15f, hw + 2f, hh * 0.15f, hw + 2f, hh + 2f, -hw - 2f, hh + 2f));
                    d.Line(f.X(-hw + 3f, hh * 0.1f), f.Y(-hw + 3f, hh * 0.1f), f.X(hw - 3f, hh * 0.1f), f.Y(hw - 3f, hh * 0.1f), 1.3f, White.WithAlpha(0.3f));
                    // Running stitches along the hem.
                    for (int i = 0; i < 9; i++)
                    {
                        float lx = -hw + 6f + i * (w - 12f) / 8.5f;
                        d.Line(f.X(lx, hh * 0.62f), f.Y(lx, hh * 0.62f), f.X(lx + 2.6f, hh * 0.62f), f.Y(lx + 2.6f, hh * 0.62f), 0.8f, col.Lighter(0.45f).WithAlpha(0.8f));
                    }
                });
                // Loose threads at the ends.
                for (int i = 0; i < 3; i++)
                {
                    float ly = -hh * 0.5f + i * hh * 0.5f;
                    l.Line(f.X(-hw + 0.5f, ly), f.Y(-hw + 0.5f, ly), f.X(-hw - 2.6f, ly + 1f), f.Y(-hw - 2.6f, ly + 1f), 0.8f, col.Darker(0.1f));
                }
            }, 1.8f, 0.45f);
        }

        /// <summary>Three matte black lumps of charcoal with a silvery sheen, growth rings and cracks.</summary>
        private static void Charcoal(IconCanvas c)
        {
            CharcoalLump(c, new[] { 50f, 26f, 58f, 19f, 72f, 16f, 82f, 22f, 89f, 33f, 87f, 47f, 82f, 58f, 66f, 62f, 55f, 52f, 51f, 40f },
                new[] { 50f, 26f, 58f, 19f, 72f, 16f, 82f, 22f, 89f, 33f, 74f, 36f, 60f, 34f }, 81);
            CharcoalLump(c, new[] { 11f, 47f, 15f, 36f, 24f, 28f, 36f, 27f, 46f, 31f, 51f, 42f, 46f, 55f, 34f, 62f, 19f, 61f },
                new[] { 11f, 47f, 15f, 36f, 24f, 28f, 36f, 27f, 46f, 31f, 38f, 40f, 24f, 43f }, 82);
            CharcoalLump(c, new[] { 18f, 70f, 24f, 55f, 34f, 47f, 50f, 43f, 66f, 45f, 79f, 53f, 84f, 66f, 78f, 80f, 62f, 88f, 40f, 88f, 25f, 82f },
                new[] { 18f, 70f, 24f, 55f, 34f, 47f, 50f, 43f, 66f, 45f, 79f, 53f, 64f, 58f, 44f, 60f, 28f, 66f }, 83);
        }

        private static void CharcoalLump(IconCanvas c, float[] outline, float[] topFacet, int seed)
        {
            Rgba coal = new Rgba(0.09f, 0.088f, 0.095f);
            float minX = 100f, minY = 100f, maxX = 0f, maxY = 0f;
            for (int i = 0; i < outline.Length; i += 2)
            {
                minX = MathF.Min(minX, outline[i]);
                maxX = MathF.Max(maxX, outline[i]);
                minY = MathF.Min(minY, outline[i + 1]);
                maxY = MathF.Max(maxY, outline[i + 1]);
            }
            Part(c, l =>
            {
                l.Polygon(Paint.Shaded(coal, minX, minY, maxX, maxY, 0.25f), outline);
                l.Polygon(Paint.Shaded(new Rgba(0.19f, 0.2f, 0.24f), minX, minY, maxX, maxY, 0.35f), topFacet);
                l.Grain(seed, 2.5f, 2.5f, 0.2f);
                Inside(l, d =>
                {
                    // Faint growth rings left from the wood, and jagged cracks across them.
                    for (int i = 0; i < 4; i++)
                    {
                        float y = minY + (maxY - minY) * (0.25f + i * 0.18f);
                        d.Curve(minX - 4f, y + 4f, (minX + maxX) * 0.5f, y - 3f, maxX + 4f, y + 3f, 0.6f, new Rgba(0.55f, 0.56f, 0.6f, 0.22f));
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float x = minX + (maxX - minX) * (0.22f + 0.28f * i + (IconCanvas.Hash01(seed, i, 0) - 0.5f) * 0.12f);
                        var crack = new float[10];
                        for (int k = 0; k < 5; k++)
                        {
                            crack[k * 2] = x + (IconCanvas.Hash01(seed, i, k + 1) - 0.5f) * 5f;
                            crack[k * 2 + 1] = minY + (maxY - minY) * k / 4f;
                        }
                        d.Polyline(Ink.WithAlpha(0.95f), 1f, crack);
                        for (int k = 0; k < 5; k++) crack[k * 2] -= 1f;
                        d.Polyline(new Rgba(0.5f, 0.52f, 0.56f, 0.35f), 0.6f, crack);
                    }
                    // A soft blue-grey sheen along the upper edge.
                    d.Polyline(new Rgba(0.62f, 0.66f, 0.74f, 0.4f), 1.6f, topFacet);
                    Speckle(d, seed, 14, minX, minY, maxX, maxY, 0.4f, 0.9f, new Rgba(0.7f, 0.72f, 0.76f, 0.35f));
                });
            }, 2.2f, 0.5f, 0.3f, 0.3f);
        }
    }
}
