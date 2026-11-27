using UnityEngine;
using S = Afterfall.Presentation.StructureSlot;

namespace Afterfall.Presentation
{
    /// <summary>Doors and the apartment furniture: drawers, desks, bookshelves, a shop counter, a sink, a stove and a double bed.</summary>
    internal static partial class StructureModels
    {
        /// <summary>Hinge offset from the opening's edge and leaf thickness, shared with <see cref="StructureView"/>'s door collider.</summary>
        public const float DoorHingeInset = 0.07f;
        public const float DoorLeafThickness = 0.045f;

        /// <summary>
        /// A panelled door in a slim frame. The leaf is the "Leaf" part, pivoting on its hinge side
        /// (−X), which <see cref="StructureView"/> swings open and closed.
        /// </summary>
        public static void Door(StructureKit k, float sx, float sy, float sz, Color color, bool entrance)
        {
            float hx = sx * 0.5f;
            Color frame = entrance ? Paint(0.32f, 0.36f, 0.37f) : Paint(0.78f, 0.76f, 0.7f);
            // Jambs and head inside the opening.
            k.Span(entrance ? S.Paint : S.Paint, new Vector3(-hx, 0f, -sz * 0.5f), new Vector3(-hx + 0.05f, sy, sz * 0.5f), frame, 0.006f);
            k.Span(S.Paint, new Vector3(hx - 0.05f, 0f, -sz * 0.5f), new Vector3(hx, sy, sz * 0.5f), frame, 0.006f);
            k.Span(S.Paint, new Vector3(-hx, sy - 0.05f, -sz * 0.5f), new Vector3(hx, sy, sz * 0.5f), frame, 0.006f);

            float x0 = -hx + DoorHingeInset, x1 = hx - 0.055f, w = x1 - x0, h = sy - 0.07f, t = DoorLeafThickness;
            k.Group("Leaf", new Vector3(x0, 0f, 0f));
            if (entrance)
            {
                Color steel = Paint(new Color(0.34f, 0.4f, 0.42f));
                k.Span(S.Paint, new Vector3(x0, 0.02f, -t * 0.5f), new Vector3(x1, h, t * 0.5f), steel, 0.008f);
                // Wired-glass panel and a push bar.
                k.Span(S.Glass, new Vector3(x0 + w * 0.25f, 1.15f, -t * 0.5f - 0.003f), new Vector3(x1 - w * 0.25f, h - 0.25f, t * 0.5f + 0.003f), T(0.9f));
                k.Span(S.Metal, new Vector3(x0 + 0.12f, 0.98f, t * 0.5f), new Vector3(x1 - 0.1f, 1.04f, t * 0.5f + 0.06f), T(0.8f), 0.01f);
                k.Span(S.Metal, new Vector3(x0 + 0.12f, 0.98f, -t * 0.5f - 0.06f), new Vector3(x1 - 0.1f, 1.04f, -t * 0.5f), T(0.8f), 0.01f);
            }
            else
            {
                Color wood = WoodTone(color);
                k.Span(S.TimberV, new Vector3(x0, 0.02f, -t * 0.5f), new Vector3(x1, h, t * 0.5f), wood, 0.008f);
                // Four raised panels on each face.
                foreach (float face in new[] { -1f, 1f })
                {
                    float z = face * (t * 0.5f + 0.004f);
                    for (int i = 0; i < 2; i++)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            float px0 = x0 + 0.1f + i * (w - 0.2f) * 0.5f + (i == 1 ? 0.04f : 0f), px1 = px0 + (w - 0.24f) * 0.5f;
                            float py0 = j == 0 ? 0.18f : h * 0.5f + 0.08f, py1 = j == 0 ? h * 0.5f - 0.04f : h - 0.16f;
                            k.Span(S.TimberV, new Vector3(px0, py0, Mathf.Min(z, z - face * 0.006f)), new Vector3(px1, py1, Mathf.Max(z, z - face * 0.006f)), Scale(wood, 0.9f), 0.004f);
                        }
                    }
                }
                Color brass = Rel(new Color(0.75f, 0.6f, 0.3f), StructurePalette.Metal);
                foreach (float face in new[] { -1f, 1f })
                    k.Sphere(S.Metal, new Vector3(x1 - 0.08f, 1.0f, face * (t * 0.5f + 0.035f)), Vector3.one * 0.03f, brass, 1);
                k.Span(S.Metal, new Vector3(x1 - 0.1f, 0.88f, -t * 0.5f - 0.002f), new Vector3(x1 - 0.06f, 0.94f, t * 0.5f + 0.002f), brass);
            }
            k.EndGroup();
        }

        /// <summary>A shop counter: a wooden cabinet with a laminate top and a till.</summary>
        public static void Counter(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = WoodTone(color);
            k.Span(S.TimberV, new Vector3(-hx + 0.02f, 0f, -hz + 0.02f), new Vector3(hx - 0.02f, sy - 0.05f, hz - 0.02f), wood, 0.01f);
            k.Span(S.Paint, new Vector3(-hx, sy - 0.05f, -hz), new Vector3(hx, sy, hz + 0.03f), Paint(0.74f, 0.72f, 0.66f), 0.01f);
            k.Span(S.Paint, new Vector3(0.2f, sy, -0.15f), new Vector3(0.6f, sy + 0.18f, 0.15f), Paint(0.2f, 0.2f, 0.22f), 0.02f);
            k.Span(S.Paint, new Vector3(0.25f, sy + 0.18f, -0.05f), new Vector3(0.55f, sy + 0.32f, 0.02f), Paint(0.25f, 0.27f, 0.3f), 0.01f);
            for (int i = 0; i < 3; i++)
                k.Span(S.TimberV, new Vector3(-hx + 0.1f + i * (sx - 0.2f) / 3f, 0.1f, -hz - 0.004f), new Vector3(-hx + 0.06f + (i + 1) * (sx - 0.2f) / 3f, sy - 0.15f, -hz + 0.02f), Board(k, 40 + i, Scale(wood, 0.9f)), 0.006f);
        }

        /// <summary>A chest of drawers with four drawers and knobs.</summary>
        public static void Drawers(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = WoodTone(color);
            k.Span(S.TimberV, new Vector3(-hx, 0.06f, -hz), new Vector3(hx, sy, hz), wood, 0.012f);
            k.Span(S.TimberV, new Vector3(-hx + 0.03f, 0f, -hz + 0.03f), new Vector3(hx - 0.03f, 0.06f, hz - 0.03f), Scale(wood, 0.6f));
            for (int i = 0; i < 4; i++)
            {
                float y0 = 0.1f + i * (sy - 0.14f) / 4f, y1 = y0 + (sy - 0.14f) / 4f - 0.025f;
                k.Span(S.TimberV, new Vector3(-hx + 0.03f, y0, hz - 0.004f), new Vector3(hx - 0.03f, y1, hz + 0.012f), Board(k, 60 + i, wood), 0.006f);
                foreach (float x in new[] { -hx * 0.5f, hx * 0.5f })
                    k.Sphere(S.Metal, new Vector3(x, (y0 + y1) * 0.5f, hz + 0.025f), Vector3.one * 0.018f, Rel(new Color(0.7f, 0.56f, 0.3f), StructurePalette.Metal), 1);
            }
        }

        /// <summary>A desk with a drawer pedestal, a lamp and a stack of papers.</summary>
        public static void Desk(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = WoodTone(color);
            k.Span(S.TimberV, new Vector3(-hx, sy - 0.04f, -hz), new Vector3(hx, sy, hz), wood, 0.008f);
            k.Span(S.TimberV, new Vector3(hx - 0.45f, 0f, -hz + 0.03f), new Vector3(hx - 0.03f, sy - 0.04f, hz - 0.03f), Scale(wood, 0.9f), 0.006f);
            for (int i = 0; i < 3; i++)
                k.Span(S.TimberV, new Vector3(hx - 0.43f, 0.06f + i * 0.22f, hz - 0.03f), new Vector3(hx - 0.05f, 0.25f + i * 0.22f, hz - 0.015f), Board(k, 70 + i, wood), 0.004f);
            k.Span(S.TimberV, new Vector3(-hx + 0.03f, 0f, -hz + 0.03f), new Vector3(-hx + 0.07f, sy - 0.04f, hz - 0.03f), Scale(wood, 0.8f));
            k.Span(S.Plain, new Vector3(-0.3f, sy, -0.1f), new Vector3(0.0f, sy + 0.05f, 0.12f), Rel(new Color(0.92f, 0.9f, 0.84f), StructurePalette.Plain));
            if (!k.Detail) return;
            k.Cylinder(S.Metal, new Vector3(-hx + 0.25f, sy, -0.2f), new Vector3(-hx + 0.25f, sy + 0.35f, -0.2f), 0.012f, 0.012f, 8, T(0.4f));
            k.Cylinder(S.Paint, new Vector3(-hx + 0.25f, sy + 0.32f, -0.2f), new Vector3(-hx + 0.25f, sy + 0.42f, -0.12f), 0.06f, 0.03f, 10, Paint(0.2f, 0.4f, 0.3f));
        }

        /// <summary>A tall bookshelf with rows of book spines.</summary>
        public static void Bookshelf(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = WoodTone(color);
            k.Span(S.TimberV, new Vector3(-hx, 0f, -hz), new Vector3(-hx + 0.03f, sy, hz), wood);
            k.Span(S.TimberV, new Vector3(hx - 0.03f, 0f, -hz), new Vector3(hx, sy, hz), wood);
            k.Span(S.TimberV, new Vector3(-hx, 0f, -hz), new Vector3(hx, sy, -hz + 0.02f), Scale(wood, 0.7f));
            for (int s = 0; s < 5; s++)
            {
                float y = s * (sy - 0.03f) / 4f;
                k.Span(S.TimberV, new Vector3(-hx + 0.03f, y, -hz + 0.02f), new Vector3(hx - 0.03f, y + 0.03f, hz), Scale(wood, 0.95f));
                if (s == 4 || !k.Detail) continue;
                float x = -hx + 0.05f;
                int b = 0;
                while (x < hx - 0.08f)
                {
                    float bw = 0.025f + k.Rand(200 + s * 40 + b) * 0.035f, bh = 0.24f + k.Rand(300 + s * 40 + b) * 0.14f;
                    Color spine = Paint(Color.HSVToRGB(k.Rand(400 + s * 40 + b), 0.35f + k.Rand(500 + b) * 0.3f, 0.3f + k.Rand(600 + b) * 0.35f));
                    if (k.Rand(700 + s * 40 + b) > 0.12f) k.Span(S.Paint, new Vector3(x, y + 0.03f, -hz + 0.05f), new Vector3(x + bw, y + 0.03f + bh, hz - 0.03f), spine, 0.003f);
                    x += bw + 0.004f;
                    b++;
                }
            }
        }

        /// <summary>A pedestal bathroom sink with a cupboard underneath and a mirror above.</summary>
        public static void Sink(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color white = Paint(new Color(0.88f, 0.87f, 0.83f));
            k.Span(S.Paint, new Vector3(-hx, 0f, -hz), new Vector3(hx, sy - 0.12f, hz - 0.04f), Paint(0.75f, 0.72f, 0.66f), 0.01f);
            k.Span(S.Paint, new Vector3(-hx - 0.02f, sy - 0.12f, -hz), new Vector3(hx + 0.02f, sy, hz), white, 0.03f);
            k.Span(S.Paint, new Vector3(-hx + 0.1f, sy - 0.04f, -hz + 0.1f), new Vector3(hx - 0.1f, sy + 0.002f, hz - 0.08f), Paint(0.6f, 0.62f, 0.62f));
            k.Cylinder(S.Metal, new Vector3(0f, sy, -hz + 0.06f), new Vector3(0f, sy + 0.2f, -hz + 0.06f), 0.015f, 0.015f, 8, T(0.8f));
            k.Span(S.Glass, new Vector3(-hx + 0.05f, sy + 0.35f, -hz - 0.01f), new Vector3(hx - 0.05f, sy + 0.95f, -hz + 0.01f), T(0.95f));
        }

        /// <summary>A freestanding gas stove: four burners, an oven door and knobs.</summary>
        public static void Stove(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color enamel = Paint(color);
            k.Span(S.Paint, new Vector3(-hx, 0.04f, -hz), new Vector3(hx, sy, hz), enamel, 0.02f);
            k.Span(S.Paint, new Vector3(-hx + 0.04f, 0.1f, hz - 0.004f), new Vector3(hx - 0.04f, sy - 0.18f, hz + 0.01f), Scale(enamel, 0.92f), 0.01f);
            k.Span(S.Glass, new Vector3(-hx + 0.12f, 0.3f, hz + 0.008f), new Vector3(hx - 0.12f, sy - 0.32f, hz + 0.014f), T(0.4f));
            k.Span(S.Metal, new Vector3(-hx + 0.1f, sy - 0.24f, hz + 0.01f), new Vector3(hx - 0.1f, sy - 0.21f, hz + 0.05f), T(0.8f), 0.008f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * hx * 0.45f, z = (i < 2 ? -1f : 1f) * hz * 0.45f;
                k.Cylinder(S.Metal, new Vector3(x, sy, z), new Vector3(x, sy + 0.02f, z), 0.09f, 0.08f, 12, T(0.25f));
                k.Cylinder(S.Metal, new Vector3(-hx + 0.1f + i * (sx - 0.2f) / 3f, sy - 0.1f, hz), new Vector3(-hx + 0.1f + i * (sx - 0.2f) / 3f, sy - 0.1f, hz + 0.03f), 0.022f, 0.02f, 8, T(0.3f));
            }
        }

        /// <summary>A double bed: wooden frame and headboard, a mattress, pillows and a rumpled blanket.</summary>
        public static void DoubleBed(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = WoodTone(new Color(0.45f, 0.33f, 0.22f));
            k.Span(S.TimberV, new Vector3(-hx, 0.1f, -hz), new Vector3(hx, 0.32f, hz), wood, 0.01f);
            k.Span(S.TimberV, new Vector3(-hx, 0f, -hz), new Vector3(hx, 1.0f, -hz + 0.07f), Scale(wood, 0.9f), 0.015f);
            k.Span(S.TimberV, new Vector3(-hx, 0f, hz - 0.06f), new Vector3(hx, 0.5f, hz), Scale(wood, 0.9f), 0.015f);
            foreach (float x in new[] { -hx + 0.03f, hx - 0.03f })
                foreach (float z in new[] { -hz + 0.05f, hz - 0.05f })
                    k.Span(S.TimberV, new Vector3(x - 0.03f, 0f, z - 0.03f), new Vector3(x + 0.03f, 0.12f, z + 0.03f), Scale(wood, 0.7f));
            Color sheet = Rel(new Color(0.82f, 0.8f, 0.74f), StructurePalette.Fabric);
            k.Span(S.Fabric, new Vector3(-hx + 0.05f, 0.32f, -hz + 0.08f), new Vector3(hx - 0.05f, 0.5f, hz - 0.08f), sheet, 0.05f);
            foreach (float x in new[] { -hx * 0.48f, hx * 0.48f })
                k.Box(S.Fabric, new Vector3(x, 0.56f, -hz + 0.3f), new Vector3(sx * 0.4f, 0.12f, 0.35f), Rel(new Color(0.9f, 0.88f, 0.82f), StructurePalette.Fabric), 0.05f);
            k.Span(S.Fabric, new Vector3(-hx + 0.02f, 0.5f, -hz + 0.55f), new Vector3(hx - 0.02f, 0.56f, hz - 0.1f), Rel(color, StructurePalette.Fabric), 0.03f);
        }
    }
}
