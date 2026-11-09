using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>Small UI symbols drawn in the same style as the item icons (recipe arrows, locks, bare hands, time).</summary>
    public static partial class ItemIconArt
    {
        /// <summary>Renders a UI glyph: "arrow", "lock", "hand" or "clock" (anything else draws a dot).</summary>
        public static IconCanvas RenderGlyph(string name, int size)
        {
            var art = new IconCanvas(size);
            Rgba bone = new Rgba(0.88f, 0.84f, 0.74f);
            switch (name)
            {
                case "arrow":
                    Part(art, l =>
                    {
                        l.RoundRect(40f, 50f, 50f, 16f, 4f, 0f, Paint.Shaded(bone, 15f, 42f, 65f, 58f, 0.2f));
                        l.Polygon(Paint.Shaded(bone, 55f, 22f, 90f, 78f, 0.2f), 56f, 22f, 90f, 50f, 56f, 78f);
                    }, 3f, 0f);
                    break;
                case "lock":
                    Part(art, l =>
                    {
                        l.Arc(50f, 42f, 17f, 8f, 180f, 180f, Paint.Shaded(Steel, 30f, 20f, 70f, 50f, 0.3f));
                        l.Line(33f, 42f, 33f, 50f, 8f, Steel.Darker(0.1f));
                        l.Line(67f, 42f, 67f, 50f, 8f, Steel.Darker(0.2f));
                    }, 2f, 0f);
                    Part(art, l =>
                    {
                        Rgba brass = new Rgba(0.84f, 0.66f, 0.28f);
                        l.RoundRect(50f, 66f, 52f, 40f, 6f, 0f, Paint.Shaded(brass, 24f, 46f, 76f, 86f, 0.28f));
                        l.Circle(50f, 62f, 6f, new Rgba(0.2f, 0.15f, 0.08f));
                        l.Polygon(new Rgba(0.2f, 0.15f, 0.08f), 46f, 64f, 54f, 64f, 52f, 76f, 48f, 76f);
                    }, 2.4f, 0.4f);
                    break;
                case "hand":
                    Part(art, l =>
                    {
                        Rgba skin = new Rgba(0.86f, 0.66f, 0.50f);
                        Paint p = Paint.Shaded(skin, 20f, 10f, 80f, 90f, 0.25f);
                        l.RoundRect(50f, 62f, 44f, 42f, 14f, 0f, p);
                        float[] fingers = { 33f, 45f, 56f, 67f };
                        float[] tops = { 28f, 18f, 20f, 30f };
                        for (int i = 0; i < 4; i++) l.Line(fingers[i], 56f, fingers[i], tops[i], 10f, p);
                        l.Line(28f, 70f, 14f, 50f, 11f, p);
                        Inside(l, d =>
                        {
                            for (int i = 1; i < 4; i++) d.Line(fingers[i] - 5.5f, 56f, fingers[i] - 5.5f, tops[i] + 8f, 1.2f, skin.Darker(0.4f).WithAlpha(0.6f));
                        });
                    }, 2.4f, 0f);
                    break;
                case "clock":
                    Part(art, l =>
                    {
                        l.Circle(50f, 50f, 36f, Paint.Shaded(new Rgba(0.82f, 0.66f, 0.30f), 14f, 14f, 86f, 86f, 0.25f));
                        l.Circle(50f, 50f, 29f, Paint.Shaded(bone.Lighter(0.3f), 21f, 21f, 79f, 79f, 0.12f));
                        for (int i = 0; i < 12; i++)
                        {
                            float a = i * MathF.PI / 6f;
                            l.Line(50f + MathF.Cos(a) * 23f, 50f + MathF.Sin(a) * 23f, 50f + MathF.Cos(a) * 26f, 50f + MathF.Sin(a) * 26f, i % 3 == 0 ? 3f : 1.6f, Ink.WithAlpha(0.7f));
                        }
                        l.Line(50f, 50f, 50f, 30f, 4f, Ink);
                        l.Line(50f, 50f, 64f, 56f, 4f, Ink);
                        l.Circle(50f, 50f, 3.5f, new Rgba(0.75f, 0.2f, 0.15f));
                    }, 2.4f, 0f);
                    break;
                default:
                    Part(art, l => l.Circle(50f, 50f, 20f, Paint.Shaded(bone, 30f, 30f, 70f, 70f, 0.25f)), 2f, 0f);
                    break;
            }
            art.Outline(3f, Ink.WithAlpha(0.92f));
            art.Shadow(1.5f, 3f, 5f, new Rgba(0f, 0f, 0f, 0.45f));
            return art;
        }
    }
}
