using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Road and rail infrastructure of the region: river bridges and the freight line's track.</summary>
    public static partial class PropBuilder
    {
        /// <summary>
        /// A concrete road bridge. The local origin is the middle of the deck's driving surface, the
        /// length runs along +Z from bank to bank. The deck and parapets are solid (people, vehicles and
        /// the NavMesh use the deck); girders, piers and abutments are render-only and reach down into the banks.
        /// </summary>
        private static void Bridge(Transform t, Vector3 s)
        {
            float w = s.x, len = s.z;
            PropKit.Spawn(t, PropKit.Cached($"prop:bridge:{w:0.##}x{len:0.##}", kit => BridgeKit(kit, w, len)));
            ColliderBox(t, "Deck", new Vector3(0f, -0.44f, 0f), new Vector3(w, 0.92f, len));
            for (int side = -1; side <= 1; side += 2)
                ColliderBox(t, "Parapet", new Vector3(side * (w * 0.5f - 0.15f), 0.55f, 0f), new Vector3(0.3f, 1.1f, len));
        }

        private static void BridgeKit(PropKit kit, float w, float len)
        {
            MeshBuilder concrete = kit[Mat(Surface.Concrete, new Color(0.58f, 0.57f, 0.54f))];
            MeshBuilder asphalt = kit[Mat(Surface.Asphalt, new Color(0.3f, 0.3f, 0.3f))];
            MeshBuilder steel = kit[Mat(Surface.PaintedMetal, new Color(0.36f, 0.4f, 0.38f), 0.3f, 0.4f)];
            MeshBuilder paint = kit[WhitePaint];

            // Deck slab, asphalt and kerbs.
            concrete.Color = Tone(0.95f);
            concrete.Box(new Vector3(0f, -0.48f, 0f), new Vector3(w, 0.86f, len), null, 0.03f);
            asphalt.Color = Tone(1f);
            asphalt.Box(new Vector3(0f, -0.02f, 0f), new Vector3(w - 1.4f, 0.06f, len - 0.02f));
            for (int side = -1; side <= 1; side += 2)
            {
                concrete.Color = Tone(1f);
                concrete.Box(new Vector3(side * (w * 0.5f - 0.35f), 0.07f, 0f), new Vector3(0.7f, 0.24f, len), null, 0.02f);
                // Faded edge line.
                paint.Color = Tone(0.75f);
                paint.Box(new Vector3(side * (w * 0.5f - 0.9f), 0.012f, 0f), new Vector3(0.12f, 0.01f, len - 1f));
            }

            // Parapet: steel posts and two rails on each side.
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2f) + 1);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (w * 0.5f - 0.15f);
                for (int i = 0; i < posts; i++)
                {
                    float z = -len * 0.5f + 0.2f + i * (len - 0.4f) / (posts - 1);
                    steel.Color = Vary(1301 + side, i, 0.15f);
                    steel.Box(new Vector3(x, 0.6f, z), new Vector3(0.1f, 1.0f, 0.1f));
                }
                steel.Color = Tone(1f);
                steel.Box(new Vector3(x, 1.05f, 0f), new Vector3(0.14f, 0.1f, len));
                steel.Box(new Vector3(x, 0.6f, 0f), new Vector3(0.08f, 0.12f, len));
            }

            // Girders under the deck, piers on each bank and abutments at the ends.
            concrete.Color = Tone(0.85f);
            for (int side = -1; side <= 1; side += 2)
                concrete.Box(new Vector3(side * w * 0.25f, -1.5f, 0f), new Vector3(0.7f, 1.3f, len - 0.4f), null, 0.03f);
            float pier = len * 0.5f - 12f;
            for (int end = -1; end <= 1; end += 2)
            {
                concrete.Color = Tone(0.8f);
                concrete.Box(new Vector3(0f, -2.3f, end * pier), new Vector3(w * 0.75f, 0.6f, 1.6f), null, 0.03f);
                for (int c = -1; c <= 1; c += 2)
                    concrete.Cylinder(new Vector3(c * w * 0.22f, -12f, end * pier), new Vector3(c * w * 0.22f, -2.4f, end * pier), 0.55f, 0.5f, 14);
                concrete.Color = Tone(0.78f);
                concrete.Box(new Vector3(0f, -3.2f, end * (len * 0.5f - 0.6f)), new Vector3(w + 0.6f, 5.6f, 1.2f), null, 0.03f);
            }
        }

        /// <summary>
        /// A length of the old freight line along +Z: ballast, timber sleepers and two rusted rails at
        /// standard gauge. Render-only: people and vehicles cross it on the ground.
        /// </summary>
        private static void Rail(Transform t, Vector3 s, int variant)
        {
            float len = s.z;
            PropKit.Spawn(t, PropKit.Cached($"prop:rail:{len:0.##}:{variant}", kit => RailKit(kit, len, variant)));
        }

        private static void RailKit(PropKit kit, float len, int variant)
        {
            MeshBuilder ballast = kit[Mat(Surface.Gravel, new Color(0.5f, 0.48f, 0.45f))];
            MeshBuilder wood = kit[Mat(Surface.Wood, new Color(0.3f, 0.26f, 0.21f))];
            MeshBuilder rail = kit[Mat(Surface.Rust, new Color(0.45f, 0.33f, 0.25f), 0.25f, 0.5f)];
            ballast.Color = Tone(0.95f);
            ballast.Box(new Vector3(0f, -0.06f, 0f), new Vector3(3.2f, 0.2f, len));
            ballast.Color = Tone(1f);
            ballast.Box(new Vector3(0f, 0.1f, 0f), new Vector3(2.6f, 0.14f, len));

            int seed = 1401 + variant * 37;
            int sleepers = Mathf.RoundToInt(len / 0.62f);
            for (int i = 0; i < sleepers; i++)
            {
                if (PropGeo.Hash(seed, i) < 0.04f) continue; // a rotted sleeper has gone
                float z = -len * 0.5f + (i + 0.5f) * len / sleepers;
                float skew = (PropGeo.Hash(seed + 1, i) - 0.5f) * 5f;
                wood.Color = Vary(seed + 2, i, 0.25f);
                wood.Box(new Vector3((PropGeo.Hash(seed + 3, i) - 0.5f) * 0.08f, 0.22f, z), new Vector3(2.5f, 0.13f, 0.24f), Quaternion.Euler(0f, skew, 0f), 0.01f);
            }
            const float gauge = 1.435f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (gauge * 0.5f + 0.036f);
                rail.Color = Vary(seed + 4, side + 1, 0.1f);
                rail.Box(new Vector3(x, 0.3f, 0f), new Vector3(0.13f, 0.03f, len));   // foot
                rail.Box(new Vector3(x, 0.37f, 0f), new Vector3(0.025f, 0.11f, len)); // web
                rail.Box(new Vector3(x, 0.44f, 0f), new Vector3(0.07f, 0.04f, len));  // head
            }
        }

        /// <summary>
        /// A station platform along +Z, its top at the local origin (level with the station's floor):
        /// a solid concrete slab reaching into the ground, a yellow safety line on the track's side
        /// (-X for variant 1, +X otherwise), and a canopy on posts over its middle (render-only).
        /// </summary>
        private static void Platform(Transform t, Vector3 s, int variant)
        {
            float w = s.x, len = s.z;
            PropKit.Spawn(t, PropKit.Cached($"prop:platform:{w:0.##}x{len:0.##}:{variant}", kit => PlatformKit(kit, w, len, variant == 1 ? -1f : 1f)));
            ColliderBox(t, "Platform", new Vector3(0f, -1.5f, 0f), new Vector3(w, 3f, len));
        }

        private static void PlatformKit(PropKit kit, float w, float len, float trackSide)
        {
            MeshBuilder concrete = kit[Mat(Surface.Concrete, new Color(0.6f, 0.59f, 0.56f))];
            MeshBuilder paving = kit[Mat(PropSurfaces.FloorTiles, new Color(0.52f, 0.5f, 0.47f))];
            MeshBuilder paint = kit[YellowPaint];
            MeshBuilder steel = kit[Mat(Surface.PaintedMetal, new Color(0.22f, 0.3f, 0.27f), 0.3f, 0.4f)];
            MeshBuilder roof = kit[Mat(Surface.Metal, new Color(0.5f, 0.52f, 0.52f), 0.3f, 0.6f)];
            concrete.Color = Tone(0.9f);
            concrete.Box(new Vector3(0f, -1.5f, 0f), new Vector3(w, 3f, len), null, 0.03f);
            paving.Color = Tone(1f);
            paving.Box(new Vector3(0f, 0.005f, 0f), new Vector3(w - 0.1f, 0.02f, len - 0.1f));
            // Edge coping and the safety line on the track side.
            concrete.Color = Tone(1.05f);
            concrete.Box(new Vector3(trackSide * (w * 0.5f - 0.25f), 0.01f, 0f), new Vector3(0.5f, 0.04f, len), null, 0.01f);
            paint.Color = Tone(0.95f);
            paint.Box(new Vector3(trackSide * (w * 0.5f - 0.75f), 0.022f, 0f), new Vector3(0.12f, 0.01f, len - 0.4f));

            // The canopy over the middle half, its posts along the far side, midway between the
            // station's doors (which are 6 m either side of its middle) so none stands in a doorway.
            float cl = len * 0.5f, back = -trackSide * (w * 0.5f - 0.6f);
            int posts = Mathf.Max(2, Mathf.RoundToInt(cl / 6f));
            for (int i = 0; i < posts; i++)
            {
                float z = -cl * 0.5f + (i + 0.5f) * cl / posts;
                steel.Color = Vary(1701, i, 0.1f);
                steel.Box(new Vector3(back, 1.65f, z), new Vector3(0.16f, 3.3f, 0.16f));
                steel.Box(new Vector3(back * 0.35f, 3.25f, z), new Vector3(w * 0.68f, 0.14f, 0.12f), Quaternion.Euler(0f, 0f, trackSide * -4f));
            }
            roof.Color = Tone(0.95f);
            roof.Box(new Vector3(back * 0.3f, 3.42f, 0f), new Vector3(w * 0.85f, 0.08f, cl + 0.6f), Quaternion.Euler(0f, 0f, trackSide * -4f), 0.01f);
            // Benches between the posts and a sign board at each end of the canopy.
            for (int i = 1; i < posts; i += 2)
            {
                float z = -cl * 0.5f + i * cl / posts;
                steel.Color = Tone(0.8f);
                steel.Box(new Vector3(back + trackSide * 0.55f, 0.45f, z), new Vector3(0.45f, 0.06f, 1.6f));
                steel.Box(new Vector3(back + trackSide * 0.35f, 0.75f, z), new Vector3(0.05f, 0.5f, 1.6f));
                steel.Box(new Vector3(back + trackSide * 0.55f, 0.22f, z - 0.7f), new Vector3(0.4f, 0.44f, 0.06f));
                steel.Box(new Vector3(back + trackSide * 0.55f, 0.22f, z + 0.7f), new Vector3(0.4f, 0.44f, 0.06f));
            }
            for (int end = -1; end <= 1; end += 2)
            {
                steel.Color = Tone(0.7f);
                steel.Box(new Vector3(back * 0.2f, 2.7f, end * (cl * 0.5f - 0.3f)), new Vector3(1.8f, 0.45f, 0.06f));
                paint.Color = Tone(0.9f);
                paint.Box(new Vector3(back * 0.2f, 2.7f, end * (cl * 0.5f - 0.27f)), new Vector3(1.6f, 0.3f, 0.02f));
            }
        }
    }
}
