using System;
using System.Collections.Generic;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Illustrated item icons, drawn with <see cref="IconCanvas"/>. Each item id maps to a drawing;
    /// items without one get their category's symbol in the item colour. The house style: 3/4 or front
    /// views lit from the top left, each part on its own layer with an embossed edge and a contact shadow
    /// on what lies below it (<see cref="Part"/>), material grain, a dark outline and a soft drop shadow
    /// added by <see cref="Render"/>. The subject fills roughly the 10..90 box.
    /// Drawings live in the partial files ItemIconArt.Resources/Supplies/Tools/Industry.
    /// </summary>
    public static partial class ItemIconArt
    {
        public const int DefaultSize = 128;

        /// <summary>Outline colour shared by every icon.</summary>
        public static readonly Rgba Ink = new Rgba(0.08f, 0.07f, 0.06f, 1f);

        private static readonly Dictionary<string, Action<IconCanvas>> Drawings = new Dictionary<string, Action<IconCanvas>>
        {
            // Raw resources and salvage (ItemIconArt.Resources.cs)
            ["wood"] = Log,
            ["stone"] = Stone,
            ["fiber"] = Fiber,
            ["iron_ore"] = IronOre,
            ["seed_potato"] = SeedPotatoes,
            ["scrap_metal"] = ScrapMetal,
            ["cloth"] = ClothRags,
            ["charcoal"] = Charcoal,
            // Food and medicine (ItemIconArt.Supplies.cs)
            ["berries"] = Berries,
            ["potato"] = Potatoes,
            ["canned_food"] = CannedBeans,
            ["snack_bar"] = SnackBar,
            ["water_bottle"] = WaterBottle,
            ["empty_bottle"] = EmptyBottle,
            ["bandage"] = Bandage,
            ["medkit"] = Medkit,
            // Tools and weapons (ItemIconArt.Tools.cs)
            ["stone_axe"] = StoneAxe,
            ["stone_pickaxe"] = StonePickaxe,
            ["hammer"] = Hammer,
            ["metal_axe"] = IronAxe,
            ["metal_pickaxe"] = IronPickaxe,
            ["machete"] = Machete,
            ["lead_pipe"] = LeadPipe,
            // Processed, electrical and mechanical (ItemIconArt.Industry.cs)
            ["planks"] = Planks,
            ["rope"] = RopeCoil,
            ["nails"] = Nails,
            ["iron_ingot"] = IronIngots,
            ["fuel_can"] = FuelCan,
            ["copper_wire"] = CopperWire,
            ["battery"] = CarBattery,
            ["circuit_board"] = CircuitBoard,
            ["schematic_generator"] = GeneratorSchematic,
            ["gear"] = Gear,
            ["mechanical_parts"] = MechanicalParts,
            ["metal_pipe"] = MetalPipe,
            ["electric_motor"] = ElectricMotor,
        };

        public static bool HasDrawing(string itemId) => itemId != null && Drawings.ContainsKey(itemId);

        /// <summary>Renders the icon for an item (its drawing, or the category symbol tinted with <paramref name="colorHex"/>).</summary>
        public static IconCanvas Render(string itemId, string category, string colorHex, int size = DefaultSize)
        {
            var art = new IconCanvas(size);
            Rgba tint = Rgba.Hex(colorHex, new Rgba(0.6f, 0.6f, 0.6f));
            if (itemId != null && Drawings.TryGetValue(itemId, out Action<IconCanvas> draw)) draw(art);
            else CategorySymbol(art, category, tint);
            art.Outline(2.2f, Ink.WithAlpha(0.92f));
            art.Shadow(1.2f, 2.6f, 4.5f, new Rgba(0f, 0f, 0f, 0.5f));
            return art;
        }

        // ------------------------------------------------------------ shared palette

        private static readonly Rgba WoodLight = new Rgba(0.80f, 0.62f, 0.40f);
        private static readonly Rgba WoodHandle = new Rgba(0.60f, 0.41f, 0.24f);
        private static readonly Rgba Bark = new Rgba(0.38f, 0.26f, 0.16f);
        private static readonly Rgba Steel = new Rgba(0.68f, 0.71f, 0.74f);
        private static readonly Rgba ForgedIron = new Rgba(0.34f, 0.35f, 0.37f);
        private static readonly Rgba RockGrey = new Rgba(0.56f, 0.55f, 0.52f);
        private static readonly Rgba Twine = new Rgba(0.76f, 0.63f, 0.40f);
        private static readonly Rgba RustBrown = new Rgba(0.58f, 0.30f, 0.14f);
        private static readonly Rgba White = new Rgba(1f, 0.98f, 0.94f);

        // ------------------------------------------------------------ drawing helpers

        /// <summary>
        /// Draws one part on its own layer, embosses its edges and composites it with a soft contact
        /// shadow on whatever is already drawn below. Returns the layer (for masks of later details).
        /// </summary>
        private static IconCanvas Part(IconCanvas c, Action<IconCanvas> draw, float bevel = 2f, float shadow = 0.35f,
            float light = 0.32f, float shade = 0.38f)
        {
            IconCanvas layer = c.Layer();
            draw(layer);
            if (bevel > 0f) layer.Bevel(bevel, light, shade);
            if (shadow > 0f) c.ShadeUnder(layer, 0.8f, 1.8f, 3.2f, shadow);
            c.Draw(layer);
            return layer;
        }

        /// <summary>Details clipped to the paint already on <paramref name="c"/> (grain lines, labels, scratches).</summary>
        private static void Inside(IconCanvas c, Action<IconCanvas> draw, float opacity = 1f)
        {
            IconCanvas d = c.Layer();
            draw(d);
            c.DrawInside(d, opacity);
        }

        /// <summary>A round bar from (x0, y0) to (x1, y1), shaded across its width toward the top-left light.</summary>
        private static void Rod(IconCanvas c, float x0, float y0, float x1, float y1, float width, Rgba col, float gloss = 0.35f, bool roundEnds = true)
        {
            float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float nx = -dy / len, ny = dx / len;
            if (nx + ny > 0f)
            {
                nx = -nx;
                ny = -ny;
            }
            float mx = (x0 + x1) * 0.5f, my = (y0 + y1) * 0.5f, h = width * 0.5f;
            Paint p = Paint.Cylinder(col, mx + nx * h, my + ny * h, mx - nx * h, my - ny * h, gloss);
            if (roundEnds) c.Line(x0, y0, x1, y1, width, p);
            else c.Polygon(p, x0 + nx * h, y0 + ny * h, x1 + nx * h, y1 + ny * h, x1 - nx * h, y1 - ny * h, x0 - nx * h, y0 - ny * h);
        }

        /// <summary>The paint for a round bar's cross-section between two points (used where the bar is a polygon).</summary>
        private static Paint Across(Rgba col, float x0, float y0, float x1, float y1, float width, float gloss = 0.35f)
        {
            float dx = x1 - x0, dy = y1 - y0, len = MathF.Max(1e-4f, MathF.Sqrt(dx * dx + dy * dy));
            float nx = -dy / len, ny = dx / len;
            if (nx + ny > 0f)
            {
                nx = -nx;
                ny = -ny;
            }
            float mx = (x0 + x1) * 0.5f, my = (y0 + y1) * 0.5f, h = width * 0.5f;
            return Paint.Cylinder(col, mx + nx * h, my + ny * h, mx - nx * h, my - ny * h, gloss);
        }

        /// <summary>A four-pointed specular glint.</summary>
        private static void Glint(IconCanvas c, float x, float y, float size, float alpha = 0.9f)
        {
            Rgba w = White.WithAlpha(alpha);
            float k = size * 0.2f;
            c.Polygon(w, x - size, y, x - k, y - k, x, y - size, x + k, y - k, x + size, y, x + k, y + k, x, y + size, x - k, y + k);
            c.Circle(x, y, size * 0.28f, w);
        }

        /// <summary>Scattered dots in a box (pores, rust pits, specks), placed by a stable hash.</summary>
        private static void Speckle(IconCanvas c, int seed, int count, float l, float t, float r, float b, float rMin, float rMax, Rgba col)
        {
            for (int i = 0; i < count; i++)
            {
                float x = l + (r - l) * IconCanvas.Hash01(seed, i, 0);
                float y = t + (b - t) * IconCanvas.Hash01(seed, i, 1);
                float rad = rMin + (rMax - rMin) * IconCanvas.Hash01(seed, i, 2);
                c.Circle(x, y, rad, col);
            }
        }

        /// <summary>Organic patches (rust, grime, moss) wherever smooth noise rises above <paramref name="threshold"/> (0..1).</summary>
        private static void Blotches(IconCanvas c, int seed, float cell, float threshold, float l, float t, float r, float b, Paint p)
        {
            c.Fill(l, t, r, b, (x, y) =>
            {
                float n = IconCanvas.ValueNoise(seed, x / cell, y / cell) * 0.65f + IconCanvas.ValueNoise(seed + 5, x / cell * 2.7f, y / cell * 2.7f) * 0.35f;
                return (threshold - n) * cell * 1.2f;
            }, p);
        }

        /// <summary>
        /// A tapered strip along a spine (blades of grass, sprouts, fringes): half-width <paramref name="w0"/>
        /// at the first spine point narrowing to <paramref name="w1"/> at the last.
        /// </summary>
        private static void Strip(IconCanvas c, float[] spine, float w0, float w1, Paint p)
        {
            int n = spine.Length / 2;
            var poly = new float[n * 4];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                float tx = spine[b * 2] - spine[a * 2], ty = spine[b * 2 + 1] - spine[a * 2 + 1];
                float len = MathF.Max(1e-4f, MathF.Sqrt(tx * tx + ty * ty));
                float w = w0 + (w1 - w0) * i / Math.Max(1f, n - 1f);
                float nx = -ty / len * w, ny = tx / len * w;
                poly[i * 2] = spine[i * 2] + nx;
                poly[i * 2 + 1] = spine[i * 2 + 1] + ny;
                poly[(2 * n - 1 - i) * 2] = spine[i * 2] - nx;
                poly[(2 * n - 1 - i) * 2 + 1] = spine[i * 2 + 1] - ny;
            }
            c.Polygon(p, poly);
        }

        /// <summary>A rotated local frame: local +x along (cos, sin) of <see cref="Deg"/>, local +y 90 degrees clockwise from it.</summary>
        private readonly struct Frame
        {
            public readonly float Ox, Oy, Deg, C, S;

            public Frame(float ox, float oy, float deg)
            {
                Ox = ox;
                Oy = oy;
                Deg = deg;
                C = MathF.Cos(deg * MathF.PI / 180f);
                S = MathF.Sin(deg * MathF.PI / 180f);
            }

            public float X(float lx, float ly) => Ox + lx * C - ly * S;
            public float Y(float lx, float ly) => Oy + lx * S + ly * C;

            /// <summary>Transforms local x, y pairs to canvas coordinates.</summary>
            public float[] P(params float[] local)
            {
                var w = new float[local.Length];
                for (int i = 0; i + 1 < local.Length; i += 2)
                {
                    w[i] = X(local[i], local[i + 1]);
                    w[i + 1] = Y(local[i], local[i + 1]);
                }
                return w;
            }
        }

        /// <summary>
        /// A simple 3/4 projection for boxy things: x to the right and slightly down, z (depth) up and to the
        /// right, y straight up. Visible faces of a box are its top (+y), front (-z) and right (+x).
        /// </summary>
        private readonly struct Iso
        {
            public readonly float Ox, Oy, Xx, Xy, Zx, Zy;

            public Iso(float ox, float oy, float xx, float xy, float zx, float zy)
            {
                Ox = ox;
                Oy = oy;
                Xx = xx;
                Xy = xy;
                Zx = zx;
                Zy = zy;
            }

            public float SX(float x, float y, float z) => Ox + x * Xx + z * Zx;
            public float SY(float x, float y, float z) => Oy + x * Xy + z * Zy - y;

            /// <summary>Projects x, y, z triples to canvas x, y pairs.</summary>
            public float[] P(params float[] xyz)
            {
                var w = new float[xyz.Length / 3 * 2];
                for (int i = 0, j = 0; i + 2 < xyz.Length; i += 3, j += 2)
                {
                    w[j] = SX(xyz[i], xyz[i + 1], xyz[i + 2]);
                    w[j + 1] = SY(xyz[i], xyz[i + 1], xyz[i + 2]);
                }
                return w;
            }

            /// <summary>
            /// Draws the visible faces of an axis-aligned box from (x, y, z) with size (w, h, d):
            /// the top lightest, the front in the base colour, the right side darkest.
            /// </summary>
            public void Box(IconCanvas c, float x, float y, float z, float w, float h, float d, Rgba col, float top = 0.28f, float side = 0.32f)
            {
                float[] front = P(x, y, z, x + w, y, z, x + w, y + h, z, x, y + h, z);
                float[] right = P(x + w, y, z, x + w, y, z + d, x + w, y + h, z + d, x + w, y + h, z);
                float[] lid = P(x, y + h, z, x + w, y + h, z, x + w, y + h, z + d, x, y + h, z + d);
                c.Polygon(Paint.Shaded(col, front[6], front[7], front[2], front[3], 0.12f), front);
                c.Polygon(Paint.Shaded(col.Darker(side), right[6], right[7], right[2], right[3], 0.1f), right);
                c.Polygon(Paint.Shaded(col.Lighter(top), lid[6], lid[7], lid[2], lid[3], 0.08f), lid);
            }
        }

        // ------------------------------------------------------------ category symbols

        /// <summary>Fallback symbol for an item category (names of <c>ItemCategory</c>), used by items without a drawing.</summary>
        public static void CategorySymbol(IconCanvas c, string category, Rgba tint)
        {
            Paint body = Paint.Shaded(tint, 15, 15, 85, 85);
            Part(c, l =>
            {
                switch (category)
                {
                    case "Natural": // leaf
                        l.Ellipse(50, 50, 20, 34, 40, body);
                        l.Line(30, 74, 66, 30, 2.2f, tint.Darker(0.45f));
                        break;
                    case "FoodMedicine": // tin can
                        l.RoundRect(50, 54, 44, 52, 6, 0, body);
                        l.Ellipse(50, 29, 22, 7, 0, tint.Lighter(0.35f));
                        l.Rect(28, 46, 72, 62, new Rgba(0.92f, 0.88f, 0.78f));
                        break;
                    case "Electrical": // lightning bolt
                        l.Polygon(body, 56, 12, 26, 56, 46, 56, 38, 88, 74, 40, 54, 40, 64, 12);
                        break;
                    case "Mechanical": // cog
                        Cog(l, 50, 50, 30, 10, body, tint.Darker(0.4f));
                        break;
                    case "Tool": // wrench
                        l.Line(30, 72, 62, 40, 11, body);
                        l.Circle(66, 34, 15, body);
                        l.Cut(k => k.RoundRect(72, 28, 10, 18, 2, 45, Ink));
                        break;
                    case "Construction": // nail
                        l.RoundRect(50, 22, 30, 8, 3, 0, body);
                        l.Polygon(body, 45, 25, 55, 25, 53, 74, 50, 86, 47, 74);
                        break;
                    case "Processed": // crate
                        l.RoundRect(50, 52, 56, 52, 4, 0, body);
                        l.Line(28, 30, 72, 74, 4, tint.Darker(0.35f));
                        l.Line(72, 30, 28, 74, 4, tint.Darker(0.35f));
                        break;
                    default: // salvage: bent plate with rivets
                        l.Polygon(body, 20, 38, 64, 22, 82, 52, 38, 78);
                        l.Circle(36, 44, 3, tint.Darker(0.5f));
                        l.Circle(64, 34, 3, tint.Darker(0.5f));
                        l.Circle(56, 62, 3, tint.Darker(0.5f));
                        break;
                }
            }, 2.2f, 0f);
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
