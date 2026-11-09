using System;
using System.Collections.Generic;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Illustrated item icons, drawn with <see cref="IconCanvas"/>. Each item id maps to a drawing;
    /// items without one get their category's symbol in the item colour. The house style: shapes lit
    /// from the top left (<see cref="Paint.Shaded"/>), a dark outline and a soft drop shadow added by
    /// <see cref="Render"/>; the subject fills roughly the 10..90 box.
    /// </summary>
    public static partial class ItemIconArt
    {
        public const int DefaultSize = 128;

        /// <summary>Outline colour shared by every icon.</summary>
        public static readonly Rgba Ink = new Rgba(0.08f, 0.07f, 0.06f, 1f);

        private static readonly Dictionary<string, Action<IconCanvas>> Drawings = new Dictionary<string, Action<IconCanvas>>
        {
            ["wood"] = Log,
            ["stone"] = Stone,
        };

        public static bool HasDrawing(string itemId) => itemId != null && Drawings.ContainsKey(itemId);

        /// <summary>Renders the icon for an item (its drawing, or the category symbol tinted with <paramref name="colorHex"/>).</summary>
        public static IconCanvas Render(string itemId, string category, string colorHex, int size = DefaultSize)
        {
            var art = new IconCanvas(size);
            Rgba tint = Rgba.Hex(colorHex, new Rgba(0.6f, 0.6f, 0.6f));
            if (itemId != null && Drawings.TryGetValue(itemId, out Action<IconCanvas> draw)) draw(art);
            else CategorySymbol(art, category, tint);
            art.Outline(2.4f, Ink.WithAlpha(0.92f));
            art.Shadow(1.2f, 2.4f, 4f, new Rgba(0f, 0f, 0f, 0.45f));
            return art;
        }

        // ------------------------------------------------------------ drawings

        private static void Log(IconCanvas c)
        {
            Rgba bark = new Rgba(0.42f, 0.27f, 0.15f), wood = new Rgba(0.86f, 0.68f, 0.44f);
            c.RoundRect(46, 54, 66, 30, 8, -28, Paint.Shaded(bark, 20, 30, 70, 80));
            var grooves = c.Layer();
            for (int i = 0; i < 4; i++)
                grooves.Line(22 + i * 12, 64 - i * 6, 48 + i * 12, 50 - i * 6, 1.6f, bark.Darker(0.45f));
            c.DrawInside(grooves, 0.8f);
            c.Ellipse(74, 39, 11, 16, -28, Paint.Radial(wood.Lighter(0.15f), wood.Darker(0.15f), 74, 39, 14));
            c.Ring(74, 39, 5f, 1.1f, wood.Darker(0.35f));
            c.Ring(74, 39, 9f, 1.1f, wood.Darker(0.3f));
            c.Circle(74, 39, 1.6f, wood.Darker(0.5f));
        }

        private static void Stone(IconCanvas c)
        {
            Rgba rock = new Rgba(0.58f, 0.57f, 0.55f);
            c.Polygon(Paint.Shaded(rock, 15, 25, 85, 85), 18, 62, 26, 34, 50, 22, 74, 30, 86, 56, 76, 78, 40, 82);
            c.Polygon(rock.Lighter(0.3f), 26, 34, 50, 22, 74, 30, 56, 44, 34, 46);
            c.Polygon(rock.Darker(0.3f), 56, 44, 74, 30, 86, 56, 76, 78, 62, 62);
            c.Line(40, 60, 52, 66, 1.4f, rock.Darker(0.45f));
        }

        // ------------------------------------------------------------ category symbols

        /// <summary>Fallback symbol for an item category (names of <c>ItemCategory</c>).</summary>
        public static void CategorySymbol(IconCanvas c, string category, Rgba tint)
        {
            Paint body = Paint.Shaded(tint, 15, 15, 85, 85);
            switch (category)
            {
                case "Natural": // leaf
                    c.Ellipse(50, 50, 20, 34, 40, body);
                    c.Line(30, 74, 66, 30, 2.2f, tint.Darker(0.45f));
                    break;
                case "FoodMedicine": // tin can
                    c.RoundRect(50, 54, 44, 52, 6, 0, body);
                    c.Ellipse(50, 29, 22, 7, 0, tint.Lighter(0.35f));
                    c.Rect(28, 46, 72, 62, new Rgba(0.92f, 0.88f, 0.78f));
                    break;
                case "Electrical": // lightning bolt
                    c.Polygon(body, 56, 12, 26, 56, 46, 56, 38, 88, 74, 40, 54, 40, 64, 12);
                    break;
                case "Mechanical": // cog
                    Cog(c, 50, 50, 30, 10, body, tint.Darker(0.4f));
                    break;
                case "Tool": // wrench
                    c.Line(30, 72, 62, 40, 11, body);
                    c.Circle(66, 34, 15, body);
                    c.Cut(k => k.RoundRect(72, 28, 10, 18, 2, 45, Ink));
                    break;
                case "Construction": // nail
                    c.RoundRect(50, 22, 30, 8, 3, 0, body);
                    c.Polygon(body, 45, 25, 55, 25, 53, 74, 50, 86, 47, 74);
                    break;
                case "Processed": // crate
                    c.RoundRect(50, 52, 56, 52, 4, 0, body);
                    c.Line(28, 30, 72, 74, 4, tint.Darker(0.35f));
                    c.Line(72, 30, 28, 74, 4, tint.Darker(0.35f));
                    break;
                default: // salvage: bent plate with rivets
                    c.Polygon(body, 20, 38, 64, 22, 82, 52, 38, 78);
                    c.Circle(36, 44, 3, tint.Darker(0.5f));
                    c.Circle(64, 34, 3, tint.Darker(0.5f));
                    c.Circle(56, 62, 3, tint.Darker(0.5f));
                    break;
            }
        }

        /// <summary>A gear with <paramref name="teeth"/> teeth and a hole.</summary>
        public static void Cog(IconCanvas c, float cx, float cy, float r, int teeth, Paint body, Rgba hole)
        {
            for (int i = 0; i < teeth; i++)
            {
                float a = i * 360f / teeth;
                float rad = a * MathF.PI / 180f;
                c.RoundRect(cx + MathF.Cos(rad) * r * 0.92f, cy + MathF.Sin(rad) * r * 0.92f, r * 0.34f, r * 0.3f, 1.5f, a, body);
            }
            c.Circle(cx, cy, r * 0.82f, body);
            c.Ring(cx, cy, r * 0.3f, r * 0.12f, hole);
            c.Cut(k => k.Circle(cx, cy, r * 0.24f, Ink));
        }
    }
}
