using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedural, intentionally stylised blockout of points-of-interest scenery. Buildings are
    /// walk-in: walls are separate colliders with real door and window openings.
    /// Convention: local origin at ground centre, entrance on the +Z face.
    /// </summary>
    public static class PropBuilder
    {
        private static readonly Color[] HousePaint =
        {
            new Color(0.62f, 0.57f, 0.48f), new Color(0.52f, 0.6f, 0.55f), new Color(0.64f, 0.56f, 0.53f), new Color(0.5f, 0.55f, 0.62f)
        };

        public static GameObject Build(PropPlacement p)
        {
            var go = new GameObject("Prop_" + p.Kind);
            go.transform.position = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            go.transform.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
            Transform t = go.transform;
            Vector3 s = new Vector3(p.Size.X, p.Size.Y, p.Size.Z);
            switch (p.Kind)
            {
                case "house": Building(t, s, HousePaint[p.Variant % HousePaint.Length], new Color(0.35f, 0.24f, 0.21f), true, 1.4f); break;
                case "clinic": Building(t, s, new Color(0.78f, 0.79f, 0.76f), new Color(0.4f, 0.42f, 0.43f), false, 0f); Cross(t, s); break;
                case "shop": Building(t, s, new Color(0.7f, 0.66f, 0.52f), new Color(0.3f, 0.3f, 0.32f), false, 0f); SignBoard(t, s, new Color(0.75f, 0.2f, 0.15f)); break;
                case "garage": Building(t, s, new Color(0.42f, 0.48f, 0.52f), new Color(0.3f, 0.33f, 0.35f), false, 0f, 5f, 4f); break;
                case "warehouse": Building(t, s, new Color(0.48f, 0.43f, 0.36f), new Color(0.33f, 0.31f, 0.29f), false, 0f, 4f, 4f); break;
                case "shack": Building(t, s, new Color(0.45f, 0.4f, 0.33f), new Color(0.3f, 0.28f, 0.26f), false, 0f); break;
                case "barn": Building(t, s, new Color(0.56f, 0.23f, 0.18f), new Color(0.29f, 0.24f, 0.21f), true, 0f, 4f, 4.5f); break;
                case "tent": Tent(t, s); break;
                case "firepit": Firepit(t); break;
                case "log_seat": Shapes.Part(t, "Log", MeshFactory.Cylinder(7), MaterialLibrary.Lit(new Color(0.4f, 0.28f, 0.18f)), new Vector3(-0.9f, 0.2f, 0), new Vector3(0.4f, 1.8f, 0.4f), Quaternion.Euler(0, 0, -90)).AddComponent<BoxCollider>(); break;
                case "fence": Fence(t, s); break;
                case "sign": Sign(t, s); break;
                case "canopy": Canopy(t, s); break;
                case "transformer": Transformer(t, s); break;
                case "pylon": Pylon(t, s); break;
                case "cliff": Cliff(t, s, p.Variant); break;
                case "silo": Silo(t, s); break;
                case "power_pole_old": OldPole(t, s); break;
                default:
                    Shapes.Box(t, "Block", MaterialLibrary.Lit(Color.gray), new Vector3(0, s.y * 0.5f, 0), s);
                    break;
            }
            return go;
        }

        private static void Building(Transform t, Vector3 s, Color wall, Color roof, bool gable, float doorOffset, float doorWidth = 1.2f, float doorHeight = 2.2f)
        {
            Material wm = MaterialLibrary.Lit(wall);
            Material trim = MaterialLibrary.Lit(wall * 0.75f);
            Material rm = MaterialLibrary.Lit(roof);
            Material floor = MaterialLibrary.Lit(new Color(0.33f, 0.3f, 0.27f));
            const float th = 0.22f;
            float h = s.y;
            Shapes.Box(t, "Floor", floor, new Vector3(0, 0.05f, 0), new Vector3(s.x, 0.1f, s.z));
            Shapes.Box(t, "Foundation", trim, new Vector3(0, -0.4f, 0), new Vector3(s.x + 0.2f, 0.8f, s.z + 0.2f));
            float halfX = s.x * 0.5f, halfZ = s.z * 0.5f;
            // Front (+Z) with door
            Shapes.WallWithOpening(t, wm, new Vector3(0, h * 0.5f, halfZ - th * 0.5f), s.x, h, th, true, doorOffset, doorWidth, 0f, doorHeight);
            // Back (-Z) with window
            Shapes.WallWithOpening(t, wm, new Vector3(0, h * 0.5f, -halfZ + th * 0.5f), s.x, h, th, true, -s.x * 0.2f, 1.1f, 1.0f, 2.0f);
            // Sides with windows
            Shapes.WallWithOpening(t, wm, new Vector3(-halfX + th * 0.5f, h * 0.5f, 0), s.z - th * 2, h, th, false, 0f, 1.1f, 1.0f, 2.0f);
            Shapes.WallWithOpening(t, wm, new Vector3(halfX - th * 0.5f, h * 0.5f, 0), s.z - th * 2, h, th, false, s.z * 0.15f, 1.1f, 1.0f, 2.0f);
            if (gable)
            {
                GameObject r = Shapes.Part(t, "Roof", MeshFactory.GableRoof(), rm, new Vector3(0, h, 0), new Vector3(s.x + 0.6f, Mathf.Min(2.2f, s.z * 0.35f), s.z + 0.8f));
                r.AddComponent<MeshCollider>().sharedMesh = MeshFactory.GableRoof();
                Shapes.Box(t, "Ceiling", wm, new Vector3(0, h - 0.05f, 0), new Vector3(s.x, 0.1f, s.z));
            }
            else
            {
                Shapes.Box(t, "Roof", rm, new Vector3(0, h + 0.1f, 0), new Vector3(s.x + 0.4f, 0.2f, s.z + 0.4f));
                Shapes.Box(t, "Parapet", trim, new Vector3(0, h + 0.35f, halfZ + 0.1f), new Vector3(s.x + 0.4f, 0.3f, 0.2f));
            }
            // Debris inside for atmosphere
            Shapes.Box(t, "Debris", MaterialLibrary.Lit(new Color(0.3f, 0.27f, 0.22f)), new Vector3(halfX * 0.4f, 0.2f, -halfZ * 0.3f), new Vector3(0.8f, 0.3f, 0.5f), true, Quaternion.Euler(0, 25, 8));
        }

        private static void Cross(Transform t, Vector3 s)
        {
            Material red = MaterialLibrary.Lit(new Color(0.75f, 0.12f, 0.1f));
            Vector3 c = new Vector3(0, s.y + 0.9f, s.z * 0.5f + 0.15f);
            Shapes.Box(t, "CrossV", red, c, new Vector3(0.35f, 1.1f, 0.1f), false);
            Shapes.Box(t, "CrossH", red, c, new Vector3(1.1f, 0.35f, 0.1f), false);
        }

        private static void SignBoard(Transform t, Vector3 s, Color c)
        {
            Shapes.Box(t, "Sign", MaterialLibrary.Lit(c), new Vector3(0, s.y + 0.7f, s.z * 0.5f + 0.2f), new Vector3(s.x * 0.7f, 0.8f, 0.12f), false);
        }

        private static void Tent(Transform t, Vector3 s)
        {
            GameObject r = Shapes.Part(t, "Canvas", MeshFactory.GableRoof(), MaterialLibrary.Lit(new Color(0.42f, 0.47f, 0.33f)), Vector3.zero, new Vector3(s.z, s.y, s.x), Quaternion.Euler(0, 90, 0));
            r.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.8f);
        }

        private static void Firepit(Transform t)
        {
            Material stone = MaterialLibrary.Lit(new Color(0.42f, 0.41f, 0.4f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Shapes.Part(t, "Stone", MeshFactory.Rock(i), stone, new Vector3(Mathf.Cos(a) * 0.55f, 0.05f, Mathf.Sin(a) * 0.55f), Vector3.one * 0.3f);
            }
            Shapes.Part(t, "Ash", MeshFactory.Cylinder(8), MaterialLibrary.Lit(new Color(0.12f, 0.11f, 0.1f)), new Vector3(0, 0, 0), new Vector3(0.9f, 0.05f, 0.9f));
        }

        private static void Fence(Transform t, Vector3 s)
        {
            bool alongX = s.x >= s.z;
            float len = Mathf.Max(s.x, s.z);
            Material wood = MaterialLibrary.Lit(new Color(0.42f, 0.33f, 0.24f));
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2.5f) + 1);
            for (int i = 0; i < posts; i++)
            {
                float o = -len * 0.5f + i * len / (posts - 1);
                Vector3 p = alongX ? new Vector3(o, s.y * 0.5f, 0) : new Vector3(0, s.y * 0.5f, o);
                Shapes.Box(t, "Post", wood, p, new Vector3(0.12f, s.y, 0.12f), false);
            }
            Vector3 railSize = alongX ? new Vector3(len, 0.08f, 0.06f) : new Vector3(0.06f, 0.08f, len);
            Shapes.Box(t, "Rail1", wood, new Vector3(0, s.y * 0.45f, 0), railSize, false);
            Shapes.Box(t, "Rail2", wood, new Vector3(0, s.y * 0.85f, 0), railSize, false);
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, s.y * 0.5f, 0);
            col.size = alongX ? new Vector3(len, s.y, 0.2f) : new Vector3(0.2f, s.y, len);
        }

        private static void Sign(Transform t, Vector3 s)
        {
            Material wood = MaterialLibrary.Lit(new Color(0.35f, 0.3f, 0.25f));
            Shapes.Box(t, "Pole", wood, new Vector3(0, s.y * 0.5f, 0), new Vector3(0.12f, s.y, 0.12f));
            Shapes.Box(t, "Board", MaterialLibrary.Lit(new Color(0.15f, 0.35f, 0.2f)), new Vector3(0, s.y - 0.3f, 0.08f), new Vector3(s.x, 0.6f, 0.06f), false);
        }

        private static void Canopy(Transform t, Vector3 s)
        {
            Material steel = MaterialLibrary.Lit(new Color(0.6f, 0.6f, 0.58f), 0.3f, 0.4f);
            float hx = s.x * 0.4f, hz = s.z * 0.35f;
            foreach (Vector3 c in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz) })
                Shapes.Box(t, "Pillar", steel, c + new Vector3(0, s.y * 0.5f, 0), new Vector3(0.3f, s.y, 0.3f));
            Shapes.Box(t, "Roof", MaterialLibrary.Lit(new Color(0.78f, 0.75f, 0.68f)), new Vector3(0, s.y, 0), new Vector3(s.x, 0.4f, s.z));
            Shapes.Box(t, "Stripe", MaterialLibrary.Lit(new Color(0.7f, 0.2f, 0.15f)), new Vector3(0, s.y, s.z * 0.5f + 0.02f), new Vector3(s.x, 0.25f, 0.05f), false);
        }

        private static void Transformer(Transform t, Vector3 s)
        {
            Material body = MaterialLibrary.Lit(new Color(0.36f, 0.42f, 0.38f), 0.2f, 0.3f);
            Shapes.Box(t, "Body", body, new Vector3(0, s.y * 0.4f, 0), new Vector3(s.x, s.y * 0.8f, s.z));
            Material ins = MaterialLibrary.Lit(new Color(0.55f, 0.32f, 0.2f));
            for (int i = -1; i <= 1; i++)
                Shapes.Part(t, "Insulator", MeshFactory.Cylinder(6), ins, new Vector3(i * s.x * 0.3f, s.y * 0.8f, 0), new Vector3(0.25f, s.y * 0.35f, 0.25f));
        }

        private static void Pylon(Transform t, Vector3 s)
        {
            Material steel = MaterialLibrary.Lit(new Color(0.5f, 0.52f, 0.54f), 0.3f, 0.5f);
            float b = 1.6f;
            foreach (Vector3 c in new[] { new Vector3(-b, 0, -b), new Vector3(b, 0, -b), new Vector3(-b, 0, b), new Vector3(b, 0, b) })
            {
                Vector3 top = new Vector3(c.x * 0.25f, s.y, c.z * 0.25f);
                Vector3 mid = (c + top) * 0.5f;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, (top - c).normalized);
                Shapes.Box(t, "Leg", steel, mid, new Vector3(0.18f, (top - c).magnitude, 0.18f), true, rot);
            }
            Shapes.Box(t, "Arm", steel, new Vector3(0, s.y * 0.85f, 0), new Vector3(5f, 0.2f, 0.25f), false);
            Shapes.Box(t, "Arm2", steel, new Vector3(0, s.y * 0.65f, 0), new Vector3(3.6f, 0.2f, 0.25f), false);
        }

        private static void Cliff(Transform t, Vector3 s, int variant)
        {
            Material rock = MaterialLibrary.Lit(new Color(0.46f, 0.43f, 0.4f));
            int pieces = Mathf.Max(3, Mathf.RoundToInt(s.x / 5f));
            for (int i = 0; i < pieces; i++)
            {
                float x = -s.x * 0.5f + (i + 0.5f) * s.x / pieces;
                float h = s.y * (0.7f + 0.3f * Mathf.PerlinNoise(i * 0.7f, variant));
                GameObject r = Shapes.Part(t, "Rock", MeshFactory.Rock(i + variant * 10, 0.2f, 1f), rock, new Vector3(x, h * 0.3f, 0), new Vector3(s.x / pieces * 1.6f, h, s.z * 1.4f), Quaternion.Euler(0, i * 37f, 0));
                r.AddComponent<MeshCollider>().sharedMesh = MeshFactory.Rock(i + variant * 10, 0.2f, 1f);
            }
        }

        private static void Silo(Transform t, Vector3 s)
        {
            Material m = MaterialLibrary.Lit(new Color(0.62f, 0.62f, 0.6f), 0.35f, 0.4f);
            GameObject body = Shapes.Part(t, "Body", MeshFactory.Cylinder(12), m, Vector3.zero, new Vector3(s.x, s.y, s.z));
            body.AddCylinderCollider();
            Shapes.Part(t, "Cap", MeshFactory.Cone(12), MaterialLibrary.Lit(new Color(0.45f, 0.45f, 0.45f)), new Vector3(0, s.y, 0), new Vector3(s.x * 1.05f, 1.6f, s.z * 1.05f));
        }

        private static void OldPole(Transform t, Vector3 s)
        {
            Material wood = MaterialLibrary.Lit(new Color(0.33f, 0.26f, 0.2f));
            Shapes.Part(t, "Pole", MeshFactory.Cylinder(6), wood, Vector3.zero, new Vector3(0.3f, s.y, 0.3f)).AddCylinderCollider();
            Shapes.Box(t, "Cross", wood, new Vector3(0, s.y - 0.6f, 0), new Vector3(1.8f, 0.12f, 0.12f), false);
        }
    }
}
