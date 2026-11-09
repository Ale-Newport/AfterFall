using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Icons for tools and weapons. Hafted tools share one pose: the handle runs from the bottom left up to
    /// the head at the top right, drawn in a <see cref="Frame"/> whose local +y runs down the handle and
    /// local +x points to the blade side. Primitive tools have knapped stone heads lashed on with twine;
    /// forged ones have smooth iron heads with a bright ground edge.
    /// </summary>
    public static partial class ItemIconArt
    {
        /// <summary>Axes and the hammer: the head sits left of centre so the blade or claw fits on the right.</summary>
        private static readonly Frame AxePose = new Frame(45f, 33f, 34f);

        /// <summary>Pickaxes: the head is centred because it reaches out on both sides.</summary>
        private static readonly Frame PickPose = new Frame(52f, 31f, 34f);

        /// <summary>A wooden handle along local y from <paramref name="top"/> to <paramref name="bottom"/> with grain and a worn grip.</summary>
        private static void Handle(IconCanvas c, Frame f, float top, float bottom, float width, Rgba wood, int seed)
        {
            Part(c, l =>
            {
                Rod(l, f.X(0f, top), f.Y(0f, top), f.X(0f, bottom), f.Y(0f, bottom), width, wood, 0.3f);
                l.Ellipse(f.X(0f, bottom - 1.5f), f.Y(0f, bottom - 1.5f), width * 0.62f, width * 0.36f, f.Deg, wood.Darker(0.1f));
                l.Grain(seed, 10f, 1.2f, 0.22f, f.Deg + 90f);
                Inside(l, d =>
                {
                    d.Line(f.X(-width * 0.18f, top + 4f), f.Y(-width * 0.18f, top + 4f), f.X(-width * 0.12f, bottom - 6f), f.Y(-width * 0.12f, bottom - 6f), 0.6f, wood.Darker(0.45f).WithAlpha(0.5f));
                    d.Line(f.X(width * 0.2f, top + 10f), f.Y(width * 0.2f, top + 10f), f.X(width * 0.16f, bottom - 14f), f.Y(width * 0.16f, bottom - 14f), 0.6f, wood.Darker(0.45f).WithAlpha(0.4f));
                    // Darker, polished grip where the hands go.
                    d.Line(f.X(0f, bottom - 22f), f.Y(0f, bottom - 22f), f.X(0f, bottom), f.Y(0f, bottom), width + 2f, Ink.WithAlpha(0.18f));
                });
            }, 1.4f, 0f);
        }

        /// <summary>Twine wrapped round the handle between local y0 and y1, plus an X over the head.</summary>
        private static void Lashing(IconCanvas c, Frame f, float y0, float y1, float halfWidth, bool cross)
        {
            Part(c, l =>
            {
                if (cross)
                {
                    l.Line(f.X(-halfWidth - 2f, y0 - 1f), f.Y(-halfWidth - 2f, y0 - 1f), f.X(halfWidth + 2f, y1 + 1f), f.Y(halfWidth + 2f, y1 + 1f), 3.6f, Ink.WithAlpha(0.7f));
                    l.Line(f.X(halfWidth + 2f, y0 - 1f), f.Y(halfWidth + 2f, y0 - 1f), f.X(-halfWidth - 2f, y1 + 1f), f.Y(-halfWidth - 2f, y1 + 1f), 3.6f, Ink.WithAlpha(0.7f));
                    l.Line(f.X(-halfWidth - 2f, y0 - 1f), f.Y(-halfWidth - 2f, y0 - 1f), f.X(halfWidth + 2f, y1 + 1f), f.Y(halfWidth + 2f, y1 + 1f), 2.4f, Twine.Darker(0.1f));
                    l.Line(f.X(halfWidth + 2f, y0 - 1f), f.Y(halfWidth + 2f, y0 - 1f), f.X(-halfWidth - 2f, y1 + 1f), f.Y(-halfWidth - 2f, y1 + 1f), 2.4f, Twine);
                }
                for (float y = y0; y <= y1 + 0.01f; y += 3.3f)
                {
                    float ax = f.X(-halfWidth, y), ay = f.Y(-halfWidth, y), bx = f.X(halfWidth, y + 1.2f), by = f.Y(halfWidth, y + 1.2f);
                    l.Line(ax, ay, bx, by, 3.6f, Ink.WithAlpha(0.75f));
                    Rod(l, ax, ay, bx, by, 2.5f, Twine, 0.35f);
                }
            }, 0.8f, 0.45f);
        }

        /// <summary>A knapped stone head: base polygon plus lighter chipped facets along the edge.</summary>
        private static void KnappedStone(IconCanvas l, Frame f, float[] outline, float[][] facets, int seed)
        {
            Rgba rock = new Rgba(0.56f, 0.54f, 0.50f);
            float[] w = f.P(outline);
            float minX = 100f, minY = 100f, maxX = 0f, maxY = 0f;
            for (int i = 0; i < w.Length; i += 2)
            {
                minX = MathF.Min(minX, w[i]);
                maxX = MathF.Max(maxX, w[i]);
                minY = MathF.Min(minY, w[i + 1]);
                maxY = MathF.Max(maxY, w[i + 1]);
            }
            l.Polygon(Paint.Shaded(rock, minX, minY, maxX, maxY, 0.25f), w);
            Inside(l, d =>
            {
                for (int i = 0; i < facets.Length; i++)
                {
                    Rgba fc = i % 2 == 0 ? rock.Lighter(0.3f) : rock.Darker(0.2f);
                    d.Polygon(fc.WithAlpha(0.8f), f.P(facets[i]));
                }
                Speckle(d, seed, 30, minX, minY, maxX, maxY, 0.4f, 1f, rock.Darker(0.5f).WithAlpha(0.6f));
                Speckle(d, seed + 1, 16, minX, minY, maxX, maxY, 0.4f, 0.8f, rock.Lighter(0.5f).WithAlpha(0.5f));
            });
            l.Grain(seed, 3.5f, 3.5f, 0.2f);
        }

        /// <summary>A forged iron head: dark scaled body with a bright ground bevel along <paramref name="edge"/>.</summary>
        private static void ForgedHead(IconCanvas l, Frame f, float[] outline, float[] edge, int seed)
        {
            float[] w = f.P(outline);
            float minX = 100f, minY = 100f, maxX = 0f, maxY = 0f;
            for (int i = 0; i < w.Length; i += 2)
            {
                minX = MathF.Min(minX, w[i]);
                maxX = MathF.Max(maxX, w[i]);
                minY = MathF.Min(minY, w[i + 1]);
                maxY = MathF.Max(maxY, w[i + 1]);
            }
            l.Polygon(Paint.Shaded(ForgedIron, minX, minY, maxX, maxY, 0.3f), w);
            l.Grain(seed, 3f, 3f, 0.12f);
            Inside(l, d =>
            {
                float[] e = f.P(edge);
                d.Polygon(Paint.Shaded(Steel.Lighter(0.1f), minX, minY, maxX, maxY, 0.3f), e);
                Speckle(d, seed, 12, minX, minY, maxX, maxY, 0.4f, 1f, new Rgba(0.16f, 0.16f, 0.17f, 0.7f));
            });
        }

        /// <summary>Stone axe: a knapped wedge lashed to a wooden haft.</summary>
        private static void StoneAxe(IconCanvas c)
        {
            Frame f = AxePose;
            Handle(c, f, -15f, 58f, 7.5f, WoodHandle, 201);
            Part(c, l => KnappedStone(l, f,
                new[] { -9f, -12f, 6f, -14f, 14f, -17f, 24f, -21f, 33f, -23f, 39f, -14f, 42f, -4f, 41f, 7f, 37f, 17f, 33f, 22f, 25f, 16f, 16f, 11f, -9f, 10f },
                new[]
                {
                    new[] { 33f, -23f, 39f, -14f, 31f, -11f, 27f, -18f },
                    new[] { 39f, -14f, 42f, -4f, 34f, -2f, 31f, -11f },
                    new[] { 42f, -4f, 41f, 7f, 33f, 6f, 34f, -2f },
                    new[] { 41f, 7f, 37f, 17f, 30f, 12f, 33f, 6f },
                    new[] { 14f, -17f, 24f, -21f, 22f, -8f, 12f, -6f },
                    new[] { 16f, 11f, 25f, 16f, 27f, 5f, 18f, 2f },
                }, 202), 2.2f, 0.45f);
            Glint(c, f.X(39f, -6f), f.Y(39f, -6f), 2.6f, 0.75f);
            Lashing(c, f, -11f, 9f, 5.5f, true);
        }

        /// <summary>Stone pickaxe: a chunky double-pointed stone lashed across the haft.</summary>
        private static void StonePickaxe(IconCanvas c)
        {
            Frame f = PickPose;
            Handle(c, f, -13f, 62f, 7.5f, WoodHandle, 211);
            Part(c, l => KnappedStone(l, f,
                new[]
                {
                    -38f, 15f, -35f, 7f, -29f, 1f, -21f, -5f, -11f, -11f, 0f, -12f, 11f, -11f, 21f, -5f, 29f, 1f, 35f, 7f, 38f, 15f,
                    31f, 12f, 24f, 9f, 14f, 4f, 0f, 3f, -14f, 4f, -24f, 9f, -31f, 12f,
                },
                new[]
                {
                    new[] { -38f, 15f, -35f, 7f, -29f, 1f, -26f, 8f },
                    new[] { -21f, -5f, -11f, -11f, -8f, -3f, -18f, 2f },
                    new[] { 0f, -12f, 11f, -11f, 9f, -4f, -1f, -4f },
                    new[] { 21f, -5f, 29f, 1f, 24f, 6f, 16f, 0f },
                    new[] { 35f, 7f, 38f, 15f, 31f, 12f, 30f, 6f },
                }, 212), 2.2f, 0.45f);
            Lashing(c, f, -10f, 7f, 5.5f, true);
        }

        /// <summary>Iron axe: a forged bearded head with a bright edge, hafted through its eye.</summary>
        private static void IronAxe(IconCanvas c)
        {
            Frame f = AxePose;
            Handle(c, f, -14f, 58f, 7.5f, WoodHandle.Lighter(0.06f), 221);
            Part(c, l =>
            {
                ForgedHead(l, f,
                    new[] { -15f, -10f, -8f, -12.5f, 8f, -12.5f, 16f, -8f, 32f, -22f, 39f, -14f, 42f, 0f, 40f, 13f, 34f, 25f, 26f, 15f, 16f, 8f, 8f, 11f, -8f, 11f, -15f, 9f },
                    new[] { 30f, -20f, 39f, -14f, 42f, 0f, 40f, 13f, 34f, 25f, 30f, 17f, 33f, 1f, 32f, -13f }, 222);
                Inside(l, d =>
                {
                    d.Polyline(White.WithAlpha(0.75f), 1f, f.P(35f, -17f, 39.5f, -7f, 40.5f, 4f, 37.5f, 15f));
                    d.Line(f.X(-14f, -9f), f.Y(-14f, -9f), f.X(-14f, 8f), f.Y(-14f, 8f), 1.4f, White.WithAlpha(0.3f));
                    d.Line(f.X(17f, -6f), f.Y(17f, -6f), f.X(30f, -17f), f.Y(30f, -17f), 0.9f, White.WithAlpha(0.3f));
                });
                // The haft end and wedge showing through the eye.
                l.RoundRect(f.X(0f, -13f), f.Y(0f, -13f), 6.6f, 2.8f, 1f, f.Deg, WoodHandle.Lighter(0.15f));
                l.Line(f.X(-2.4f, -13f), f.Y(-2.4f, -13f), f.X(2.4f, -13f), f.Y(2.4f, -13f), 0.9f, ForgedIron.Darker(0.3f));
            }, 2f, 0.45f, 0.4f, 0.4f);
            Glint(c, f.X(40f, -5f), f.Y(40f, -5f), 3.2f);
        }

        /// <summary>Iron pickaxe: a forged pick-mattock with a long spike, a flat chisel end and a socket collar.</summary>
        private static void IronPickaxe(IconCanvas c)
        {
            Frame f = PickPose;
            Handle(c, f, -13f, 62f, 7.5f, WoodHandle.Lighter(0.06f), 231);
            Part(c, l =>
            {
                ForgedHead(l, f,
                    new[]
                    {
                        -42f, 17f, -27f, -1f, -9f, -9f, -8f, -13f, 8f, -13f, 9f, -9f, 24f, -4f, 36f, 8f, 39f, 12f, 33f, 17f,
                        22f, 6f, 9f, 3f, 8f, 9f, -8f, 9f, -9f, 3f, -25f, 6f,
                    },
                    new[] { -42f, 17f, -35f, 6f, -32f, 9f, 36f, 8f, 39f, 12f, 33f, 17f, 31f, 13f, 34f, 9f }, 232);
                Inside(l, d =>
                {
                    d.Curve(f.X(-36f, 8f), f.Y(-36f, 8f), f.X(-22f, -5f), f.Y(-22f, -5f), f.X(-9f, -8f), f.Y(-9f, -8f), 1f, White.WithAlpha(0.45f));
                    d.Curve(f.X(9f, -8f), f.Y(9f, -8f), f.X(22f, -4f), f.Y(22f, -4f), f.X(33f, 7f), f.Y(33f, 7f), 1f, White.WithAlpha(0.35f));
                    d.Line(f.X(-8f, -12f), f.Y(-8f, -12f), f.X(-8f, 8f), f.Y(-8f, 8f), 1.1f, Ink.WithAlpha(0.4f));
                    d.Line(f.X(8f, -12f), f.Y(8f, -12f), f.X(8f, 8f), f.Y(8f, 8f), 1.1f, Ink.WithAlpha(0.4f));
                });
                l.RoundRect(f.X(0f, -13f), f.Y(0f, -13f), 6.6f, 2.8f, 1f, f.Deg, WoodHandle.Lighter(0.15f));
            }, 2f, 0.45f, 0.4f, 0.4f);
            Glint(c, f.X(-39f, 13f), f.Y(-39f, 13f), 2.6f, 0.8f);
            Glint(c, f.X(36f, 13f), f.Y(36f, 13f), 2.2f, 0.7f);
        }

        /// <summary>Claw hammer: a steel head with a round striking face and a split, curved claw; rope-wrapped grip.</summary>
        private static void Hammer(IconCanvas c)
        {
            Frame f = AxePose;
            Handle(c, f, -11f, 58f, 7.5f, WoodHandle, 241);
            Part(c, l =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float y = 36f + i * 3.6f;
                    float ax = f.X(-4.8f, y), ay = f.Y(-4.8f, y), bx = f.X(4.8f, y + 1.4f), by = f.Y(4.8f, y + 1.4f);
                    l.Line(ax, ay, bx, by, 3.8f, Ink.WithAlpha(0.7f));
                    Rod(l, ax, ay, bx, by, 2.8f, Twine.Darker(0.08f), 0.35f);
                }
            }, 0.8f, 0.4f);
            Rgba head = new Rgba(0.52f, 0.55f, 0.58f);
            Part(c, l =>
            {
                // Poll and striking face on the left, the eye block in the middle.
                float[] poll = f.P(-7f, -8.5f, -13f, -6.5f, -21f, -7f, -25f, -9f, -28f, -9f, -28f, 5f, -25f, 5f, -21f, 3f, -13f, 2.5f, -7f, 4.5f);
                l.Polygon(Across(head, f.X(-28f, -2f), f.Y(-28f, -2f), f.X(-7f, -2f), f.Y(-7f, -2f), 14f, 0.55f), poll);
                l.Polygon(Paint.Shaded(head, f.X(-8f, -11f), f.Y(-8f, -11f), f.X(8f, 8f), f.Y(8f, 8f), 0.3f), f.P(-8f, -11f, 8f, -11f, 8f, 8f, -8f, 8f));
                // The claw: a tapered curve bending back toward the handle.
                float[] spine = f.P(IconCanvas.CurvePoints(6f, -3f, 26f, -5f, 37f, 13f, 12));
                Strip(l, spine, 6.4f, 1.2f, Paint.Shaded(head, f.X(6f, -10f), f.Y(6f, -10f), f.X(37f, 14f), f.Y(37f, 14f), 0.3f));
                l.Grain(242, 3f, 3f, 0.08f);
                Inside(l, d =>
                {
                    d.Polygon(Paint.Shaded(Steel.Lighter(0.3f), f.X(-29f, -10f), f.Y(-29f, -10f), f.X(-25f, 6f), f.Y(-25f, 6f), 0.15f),
                        f.P(-29f, -10f, -25.5f, -10f, -25.5f, 6f, -29f, 6f));
                    float[] slit = f.P(IconCanvas.CurvePoints(16f, -4f, 28f, -4.5f, 37f, 13f, 8));
                    d.Polyline(Ink.WithAlpha(0.85f), 1.6f, slit);
                    d.Line(f.X(-24f, -6.5f), f.Y(-24f, -6.5f), f.X(-10f, -5.5f), f.Y(-10f, -5.5f), 1f, White.WithAlpha(0.5f));
                    d.Line(f.X(-7f, -10f), f.Y(-7f, -10f), f.X(-7f, 7f), f.Y(-7f, 7f), 1f, Ink.WithAlpha(0.35f));
                    d.Line(f.X(7f, -10f), f.Y(7f, -10f), f.X(7f, 7f), f.Y(7f, 7f), 1f, Ink.WithAlpha(0.35f));
                });
            }, 2f, 0.45f, 0.4f, 0.4f);
            Glint(c, f.X(-27f, -6f), f.Y(-27f, -6f), 2.6f, 0.85f);
        }

        /// <summary>Machete: a long, slightly rusty blade with a cloth-wrapped, riveted grip.</summary>
        private static void Machete(IconCanvas c)
        {
            var f = new Frame(32f, 68f, -45f);
            Rgba blade = new Rgba(0.70f, 0.72f, 0.74f);
            Part(c, l =>
            {
                float[] w = f.P(0f, -5f, 44f, -6f, 60f, -8f, 66f, -9f, 62f, 0f, 52f, 8f, 36f, 10.5f, 12f, 8f, 0f, 7f);
                l.Polygon(Paint.Shaded(blade, f.X(0f, -8f), f.Y(0f, -8f), f.X(60f, 10f), f.Y(60f, 10f), 0.3f), w);
                l.Grain(251, 6f, 1.5f, 0.08f, f.Deg);
                Inside(l, d =>
                {
                    // Darker spine, bright ground edge, a fuller line and rust freckles.
                    d.Polygon(blade.Darker(0.3f).WithAlpha(0.8f), f.P(0f, -6f, 66f, -10f, 64f, -5f, 0f, -2.5f));
                    d.Polygon(Paint.Shaded(Steel.Lighter(0.35f), f.X(0f, 4f), f.Y(0f, 4f), f.X(60f, 10f), f.Y(60f, 10f), 0.15f),
                        f.P(0f, 5f, 12f, 5.5f, 36f, 7.5f, 52f, 5f, 61f, -2f, 64f, 0f, 52f, 9f, 36f, 11f, 12f, 9f, 0f, 8f));
                    d.Line(f.X(4f, 1.5f), f.Y(4f, 1.5f), f.X(46f, 1f), f.Y(46f, 1f), 0.9f, Ink.WithAlpha(0.3f));
                    Speckle(d, 252, 14, f.X(20f, 0f) - 12f, f.Y(20f, 0f) - 12f, f.X(50f, 0f) + 8f, f.Y(50f, 0f) + 8f, 0.6f, 1.8f, RustBrown.WithAlpha(0.55f));
                });
            }, 1.6f, 0f, 0.4f, 0.4f);
            Glint(c, f.X(58f, -2f), f.Y(58f, -2f), 3f);
            // Guard and grip.
            Part(c, l =>
            {
                l.RoundRect(f.X(-1.5f, 1f), f.Y(-1.5f, 1f), 4f, 20f, 1.5f, f.Deg, Paint.Shaded(new Rgba(0.22f, 0.2f, 0.18f), f.X(-3f, -9f), f.Y(-3f, -9f), f.X(0f, 11f), f.Y(0f, 11f), 0.3f));
                Rod(l, f.X(-3f, 1f), f.Y(-3f, 1f), f.X(-26f, 1f), f.Y(-26f, 1f), 10f, new Rgba(0.30f, 0.22f, 0.16f), 0.3f);
                Inside(l, d =>
                {
                    Rgba cloth = new Rgba(0.62f, 0.56f, 0.44f);
                    for (int i = 0; i < 6; i++)
                    {
                        float x = -6f - i * 3.6f;
                        d.Line(f.X(x, -6f), f.Y(x, -6f), f.X(x - 2.4f, 7f), f.Y(x - 2.4f, 7f), 2.6f, Ink.WithAlpha(0.6f));
                        Rod(d, f.X(x, -6f), f.Y(x, -6f), f.X(x - 2.4f, 7f), f.Y(x - 2.4f, 7f), 2f, cloth, 0.3f);
                    }
                });
                l.Circle(f.X(-24f, 1f), f.Y(-24f, 1f), 1.6f, new Rgba(0.82f, 0.66f, 0.32f));
            }, 1f, 0.45f);
        }

        /// <summary>Lead pipe: a heavy dull pipe with a T-fitting at the business end and a taped grip.</summary>
        private static void LeadPipe(IconCanvas c)
        {
            var f = new Frame(34f, 66f, -45f);
            Rgba lead = new Rgba(0.46f, 0.49f, 0.51f);
            Part(c, l =>
            {
                Rod(l, f.X(-30f, 0f), f.Y(-30f, 0f), f.X(46f, 0f), f.Y(46f, 0f), 10f, lead, 0.25f, false);
                l.Grain(261, 5f, 2f, 0.12f, f.Deg);
                Inside(l, d =>
                {
                    Speckle(d, 262, 7, f.X(0f, 0f) - 8f, f.Y(30f, 0f) - 8f, f.X(30f, 0f) + 8f, f.Y(0f, 0f) + 8f, 1f, 2.2f, Ink.WithAlpha(0.25f));
                    d.Line(f.X(4f, -2.5f), f.Y(4f, -2.5f), f.X(18f, -2.8f), f.Y(18f, -2.8f), 0.8f, White.WithAlpha(0.4f));
                    d.Line(f.X(24f, -2.2f), f.Y(24f, -2.2f), f.X(30f, -2.6f), f.Y(30f, -2.6f), 0.8f, White.WithAlpha(0.35f));
                });
            }, 1.6f, 0f);
            // T-fitting with a side outlet and a thick collar.
            Part(c, l =>
            {
                Rod(l, f.X(47f, -6f), f.Y(47f, -6f), f.X(47f, -21f), f.Y(47f, -21f), 11f, lead.Darker(0.05f), 0.3f, false);
                Rod(l, f.X(38f, 0f), f.Y(38f, 0f), f.X(57f, 0f), f.Y(57f, 0f), 15f, lead.Darker(0.05f), 0.3f, false);
                l.Ellipse(f.X(47f, -21f), f.Y(47f, -21f), 5.5f, 2.2f, f.Deg, lead.Darker(0.35f));
                l.Ellipse(f.X(47f, -21f), f.Y(47f, -21f), 3.6f, 1.3f, f.Deg, Ink.WithAlpha(0.85f));
                Inside(l, d =>
                {
                    foreach (float x in new[] { 40f, 55f })
                        d.Line(f.X(x, -9f), f.Y(x, -9f), f.X(x, 9f), f.Y(x, 9f), 1f, Ink.WithAlpha(0.35f));
                    d.Line(f.X(42f, 4f), f.Y(42f, 4f), f.X(52f, -4f), f.Y(52f, -4f), 1.2f, White.WithAlpha(0.25f));
                });
                l.Grain(263, 4f, 4f, 0.12f);
            }, 1.8f, 0.45f);
            // Black tape spiralled around the grip.
            Part(c, l =>
            {
                Rod(l, f.X(-31f, 0f), f.Y(-31f, 0f), f.X(-6f, 0f), f.Y(-6f, 0f), 12f, new Rgba(0.20f, 0.20f, 0.21f), 0.35f, false);
                Inside(l, d =>
                {
                    for (int i = 0; i < 7; i++)
                    {
                        float x = -29f + i * 3.6f;
                        d.Line(f.X(x, -6.5f), f.Y(x, -6.5f), f.X(x + 3f, 6.5f), f.Y(x + 3f, 6.5f), 0.7f, White.WithAlpha(0.18f));
                    }
                });
                l.Polygon(new Rgba(0.18f, 0.18f, 0.19f), f.P(-6f, 5f, -2f, 7f, -1f, 9f, -5f, 6.5f));
            }, 1.2f, 0.45f);
        }
    }
}
