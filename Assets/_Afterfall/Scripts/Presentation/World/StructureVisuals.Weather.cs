using System.Collections.Generic;
using UnityEngine;
using S = Afterfall.Presentation.StructureSlot;

namespace Afterfall.Presentation
{
    internal static partial class StructureModels
    {
        // ------------------------------------------------------------ campfire

        /// <summary>
        /// A ring of fieldstones around a bed of ash with split logs leaned into a tepee. The flames are
        /// the "Fire" part, which <see cref="StructureView"/> lights, flickers and dances while fuel burns.
        /// </summary>
        public static void Campfire(StructureKit k, float sx, float sz)
        {
            float ring = Mathf.Min(sx, sz) * 0.4f;
            // Ash and charred earth inside the ring.
            k.Disc(S.Soil, new Vector3(0f, 0.012f, 0f), Vector3.up, ring * 0.92f, 20, T(0.28f, 0.8f));
            k.Disc(S.Soil, new Vector3(0f, 0.016f, 0f), Vector3.up, ring * 0.55f, 16, Rel(new Color(0.42f, 0.4f, 0.38f), StructurePalette.Soil, 0.9f));
            const int stones = 11;
            for (int i = 0; i < stones; i++)
            {
                float a = i * Mathf.PI * 2f / stones + Fixed(i + 700) * 0.2f;
                float r = ring + Fixed(i + 710) * 0.05f;
                var size = new Vector3(0.13f + Fixed(i + 720) * 0.05f, 0.09f + Fixed(i + 730) * 0.05f, 0.1f + Fixed(i + 740) * 0.04f);
                int seed = i;
                // Inner faces blackened by the fire.
                Color tone = Color.Lerp(T(0.85f), T(0.45f), Fixed(i + 750) * 0.6f);
                k.Sphere(S.Stone, new Vector3(Mathf.Cos(a) * r, size.y * 0.55f, Mathf.Sin(a) * r), size, tone, 1, false,
                    d => 1f + (MeshFactory.Hash(seed + 760, d) - 0.5f) * 0.45f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
            }

            // Split logs leaned into a tepee, their feet charred.
            const int logs = 5;
            for (int i = 0; i < logs; i++)
            {
                float a = i * Mathf.PI * 2f / logs + 0.3f;
                var foot = new Vector3(Mathf.Cos(a) * ring * 0.78f, 0.03f, Mathf.Sin(a) * ring * 0.78f);
                var head = new Vector3(Mathf.Cos(a) * 0.05f, 0.5f + Fixed(i + 770) * 0.08f, Mathf.Sin(a) * 0.05f);
                k.Cylinder(S.Bark, foot, Vector3.Lerp(foot, head, 0.45f), 0.055f, 0.05f, 8, T(0.25f, 0.8f), true, true, false);
                k.Cylinder(S.Bark, Vector3.Lerp(foot, head, 0.45f), head, 0.05f, 0.04f, 8, T(0.95f), true, false, true);
            }
            // Two logs lying across the embers.
            k.Cylinder(S.Bark, new Vector3(-0.3f, 0.06f, -0.08f), new Vector3(0.28f, 0.07f, 0.1f), 0.06f, 0.055f, 8, T(0.3f, 0.8f));
            k.Cylinder(S.Bark, new Vector3(-0.12f, 0.06f, 0.26f), new Vector3(0.1f, 0.065f, -0.28f), 0.055f, 0.05f, 8, T(0.35f, 0.8f));

            // The flames: tongues around the logs and glowing embers at their feet.
            k.Group("Fire", new Vector3(0f, 0.02f, 0f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + 0.2f;
                float r = 0.06f + Fixed(i + 780) * 0.1f;
                float h = 0.22f + Fixed(i + 790) * 0.18f;
                int seed = i;
                k.Sphere(S.Glow, new Vector3(Mathf.Cos(a) * r, 0.04f + h * 0.5f, Mathf.Sin(a) * r), new Vector3(0.07f, h * 0.5f, 0.07f), T(1f), 1, true,
                    d => d.y > 0f ? 1f - d.y * 0.55f + (MeshFactory.Hash(seed + 800, d) - 0.5f) * 0.2f : 1f);
            }
            k.Sphere(S.Glow, new Vector3(0f, 0.04f, 0f), new Vector3(0.26f, 0.035f, 0.26f), T(1f), 1, false, d => 1f + (MeshFactory.Hash(811, d) - 0.5f) * 0.5f);
            k.EndGroup();

            if (!k.Detail) return;
            // A spare log and a blackened kettle waiting beside the ring.
            k.Cylinder(S.Bark, new Vector3(ring + 0.25f, 0.07f, -0.35f), new Vector3(ring + 0.2f, 0.07f, 0.25f), 0.07f, 0.065f, 9, T(0.95f));
            k.Cylinder(S.Metal, new Vector3(-ring - 0.22f, 0.0f, 0.2f), new Vector3(-ring - 0.22f, 0.15f, 0.2f), 0.09f, 0.075f, 12, T(0.12f, 0.7f));
            k.Disc(S.Metal, new Vector3(-ring - 0.22f, 0.152f, 0.2f), Vector3.up, 0.075f, 12, T(0.15f));
            k.Tube(S.Metal, new[] { new Vector3(-ring - 0.3f, 0.15f, 0.2f), new Vector3(-ring - 0.22f, 0.26f, 0.2f), new Vector3(-ring - 0.14f, 0.15f, 0.2f) }, 0.006f, 5, T(0.2f));
        }

        // ------------------------------------------------------------ lightning rod

        /// <summary>
        /// A galvanised steel mast on a concrete footing, held by three guy wires, with a copper air
        /// terminal at the top and a copper down-conductor to a ground rod beside the footing.
        /// </summary>
        public static void LightningRod(StructureKit k, float sy)
        {
            Color steel = T(0.95f);
            Color copper = Rel(new Color(0.72f, 0.44f, 0.26f), StructurePalette.Metal);
            k.Span(S.Concrete, new Vector3(-0.28f, -0.2f, -0.28f), new Vector3(0.28f, 0.14f, 0.28f), T(0.92f), 0.03f);
            k.Span(S.Metal, new Vector3(-0.16f, 0.14f, -0.16f), new Vector3(0.16f, 0.16f, 0.16f), T(0.5f), 0.004f);
            float top = sy - 0.9f;
            k.Cylinder(S.Metal, new Vector3(0f, 0.16f, 0f), new Vector3(0f, top, 0f), 0.065f, 0.04f, 12, steel, true, false, true);
            // Sleeve joints every two metres.
            for (float y = 2f; y < top; y += 2f)
                k.Cylinder(S.Metal, new Vector3(0f, y - 0.06f, 0f), new Vector3(0f, y + 0.06f, 0f), 0.062f - y * 0.003f, 0.06f - y * 0.003f, 12, T(0.8f));
            // Copper air terminal: a spike rising from a crown of points.
            k.Cylinder(S.Metal, new Vector3(0f, top, 0f), new Vector3(0f, sy - 0.05f, 0f), 0.018f, 0.006f, 10, copper);
            k.Sphere(S.Metal, new Vector3(0f, top + 0.05f, 0f), new Vector3(0.045f, 0.03f, 0.045f), copper, 1);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                var tip = new Vector3(Mathf.Cos(a) * 0.12f, top + 0.32f, Mathf.Sin(a) * 0.12f);
                k.Cylinder(S.Metal, new Vector3(0f, top + 0.06f, 0f), tip, 0.008f, 0.003f, 6, copper);
            }
            // Guy wires from two thirds of the way up to stakes in the ground.
            float guyY = top * 0.66f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.5f;
                var stake = new Vector3(Mathf.Cos(a) * 2.2f, 0.05f, Mathf.Sin(a) * 2.2f);
                k.Cylinder(S.Metal, new Vector3(0f, guyY, 0f), stake, 0.007f, 0.007f, 5, T(0.7f));
                k.Cylinder(S.Metal, stake + new Vector3(0f, -0.1f, 0f), stake + new Vector3(0f, 0.09f, 0f), 0.02f, 0.016f, 6, T(0.4f));
            }
            k.Ring(S.Metal, new Vector3(0f, guyY, 0f), Vector3.up, 0.055f, 0.012f, 12, 5, T(0.6f));
            // Copper down-conductor clipped to the mast, running to a ground rod.
            var cable = new List<Vector3>();
            for (float y = top; y > 0.3f; y -= 0.6f) cable.Add(new Vector3(0.06f, y, 0.02f));
            cable.Add(new Vector3(0.06f, 0.2f, 0.02f));
            cable.Add(new Vector3(0.33f, 0.02f, 0.05f));
            cable.Add(new Vector3(0.48f, 0.02f, 0.05f));
            k.Tube(S.Metal, cable, 0.009f, 5, copper);
            k.Cylinder(S.Metal, new Vector3(0.5f, -0.2f, 0.05f), new Vector3(0.5f, 0.07f, 0.05f), 0.016f, 0.016f, 8, copper);
            if (!k.Detail) return;
            k.Span(S.Paint, new Vector3(-0.06f, 1.4f, 0.058f), new Vector3(0.06f, 1.55f, 0.064f), Paint(0.85f, 0.72f, 0.12f));
            k.Span(S.Paint, new Vector3(-0.045f, 1.44f, 0.064f), new Vector3(0.045f, 1.51f, 0.066f), Paint(0.1f, 0.1f, 0.1f));
        }
    }
}
