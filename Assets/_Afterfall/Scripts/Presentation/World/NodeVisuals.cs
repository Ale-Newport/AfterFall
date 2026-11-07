using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Procedural visuals for harvestable resource nodes.</summary>
    public static class NodeVisuals
    {
        public static void Build(Transform root, ResourceNodeDefinition def, ResourceNodeState state)
        {
            int v = (int)(state.Id.Value % 4);
            bool depleted = state.Depleted;
            switch (def.Visual)
            {
                case "tree_pine":
                    Trunk(root, depleted, 0.38f, new Color(0.32f, 0.22f, 0.15f));
                    if (!depleted) Shapes.Part(root, "Crown", MeshFactory.PineCrown(v), MaterialLibrary.Lit(new Color(0.16f, 0.29f, 0.17f) * (0.9f + v * 0.05f)), new Vector3(0, 2.0f, 0), new Vector3(3.2f, 6.5f, 3.2f));
                    break;
                case "tree_birch":
                    Trunk(root, depleted, 0.3f, new Color(0.82f, 0.8f, 0.74f));
                    if (!depleted) Shapes.Part(root, "Crown", MeshFactory.Blob(v), MaterialLibrary.Lit(new Color(0.42f, 0.5f, 0.24f) * (0.9f + v * 0.05f)), new Vector3(0, 4.8f, 0), new Vector3(3.4f, 3.6f, 3.4f));
                    break;
                case "branches":
                    for (int i = 0; i < 4; i++)
                        Shapes.Part(root, "Branch", MeshFactory.Cylinder(5), MaterialLibrary.Lit(new Color(0.38f, 0.27f, 0.18f)), new Vector3(0, 0.12f + i * 0.08f, 0), new Vector3(0.12f, 1.6f - i * 0.2f, 0.12f), Quaternion.Euler(0, i * 47f, 88f));
                    Collider(root, new Vector3(0, 0.3f, 0), new Vector3(1.6f, 0.6f, 1.6f), true);
                    break;
                case "rock":
                    Shapes.Part(root, "Rock", MeshFactory.Rock(v), MaterialLibrary.Lit(new Color(0.47f, 0.46f, 0.44f)), new Vector3(0, 0.25f, 0), new Vector3(1.4f, 1.1f, 1.3f));
                    Collider(root, new Vector3(0, 0.35f, 0), new Vector3(1.2f, 0.8f, 1.1f), false);
                    break;
                case "ore":
                    Shapes.Part(root, "Rock", MeshFactory.Rock(v + 4), MaterialLibrary.Lit(new Color(0.38f, 0.33f, 0.31f)), new Vector3(0, 0.4f, 0), new Vector3(2.0f, 1.6f, 1.8f));
                    for (int i = 0; i < 4; i++)
                        Shapes.Part(root, "Vein", MeshFactory.Rock(i + 20), MaterialLibrary.Lit(new Color(0.62f, 0.3f, 0.16f), 0.4f, 0.3f), new Vector3((i - 1.5f) * 0.35f, 0.55f + (i % 2) * 0.25f, 0.55f), Vector3.one * 0.35f);
                    Collider(root, new Vector3(0, 0.6f, 0), new Vector3(1.8f, 1.2f, 1.6f), false);
                    break;
                case "fiber":
                    for (int i = 0; i < 6; i++)
                        Shapes.Part(root, "Blade", MeshFactory.Cone(4), MaterialLibrary.Lit(new Color(0.48f, 0.58f, 0.27f)), new Vector3(Mathf.Cos(i) * 0.25f, 0, Mathf.Sin(i * 1.7f) * 0.25f), new Vector3(0.18f, 0.9f + (i % 3) * 0.2f, 0.18f), Quaternion.Euler(i * 6f - 15f, i * 60f, 0), false);
                    Collider(root, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.8f, 0.9f), true);
                    break;
                case "bush":
                    Shapes.Part(root, "Bush", MeshFactory.Blob(v + 8), MaterialLibrary.Lit(new Color(0.24f, 0.36f, 0.19f)), new Vector3(0, 0.1f, 0), new Vector3(1.5f, 1.2f, 1.5f));
                    if (!depleted)
                        for (int i = 0; i < 7; i++)
                            Shapes.Part(root, "Berry", MeshFactory.Rock(i + 40, 0.05f, 1f), MaterialLibrary.Lit(new Color(0.55f, 0.1f, 0.3f), 0.5f), new Vector3(Mathf.Cos(i * 0.9f) * 0.6f, 0.5f + (i % 3) * 0.15f, Mathf.Sin(i * 0.9f) * 0.6f), Vector3.one * 0.12f, null, false);
                    Collider(root, new Vector3(0, 0.5f, 0), new Vector3(1.4f, 1f, 1.4f), true);
                    break;
                case "scrap":
                    Material rust = MaterialLibrary.Lit(new Color(0.42f, 0.3f, 0.22f), 0.3f, 0.4f);
                    Material steel = MaterialLibrary.Lit(new Color(0.5f, 0.52f, 0.55f), 0.35f, 0.6f);
                    for (int i = 0; i < 6; i++)
                        Shapes.Part(root, "Junk", MeshFactory.Cube, i % 2 == 0 ? rust : steel, new Vector3((i % 3 - 1) * 0.45f, 0.18f + (i / 3) * 0.3f, ((i * 7) % 3 - 1) * 0.35f), new Vector3(0.7f, 0.3f, 0.45f), Quaternion.Euler(i * 11f, i * 53f, i * 7f));
                    Collider(root, new Vector3(0, 0.4f, 0), new Vector3(1.6f, 0.8f, 1.3f), false);
                    break;
                case "car":
                    Car(root, v, depleted);
                    break;
                case "pump_gas":
                    Material red = MaterialLibrary.Lit(new Color(0.72f, 0.18f, 0.14f));
                    Shapes.Box(root, "Pump", red, new Vector3(0, 0.85f, 0), new Vector3(0.7f, 1.7f, 0.5f));
                    Shapes.Box(root, "Display", MaterialLibrary.Lit(new Color(0.15f, 0.18f, 0.2f)), new Vector3(0, 1.4f, 0.26f), new Vector3(0.5f, 0.3f, 0.02f), false);
                    Shapes.Box(root, "Hose", MaterialLibrary.Lit(new Color(0.1f, 0.1f, 0.1f)), new Vector3(0.38f, 0.9f, 0), new Vector3(0.06f, 0.9f, 0.06f), false);
                    break;
                case "transformer":
                    Shapes.Box(root, "Body", MaterialLibrary.Lit(new Color(0.25f, 0.27f, 0.25f)), new Vector3(0, 0.7f, 0), new Vector3(1.6f, 1.4f, 1.2f));
                    Shapes.Box(root, "Burn", MaterialLibrary.Lit(new Color(0.08f, 0.07f, 0.06f)), new Vector3(0, 1.0f, 0.61f), new Vector3(1.0f, 0.6f, 0.02f), false);
                    break;
                default:
                    Shapes.Box(root, "Node", MaterialLibrary.Lit(Color.magenta), new Vector3(0, 0.5f, 0), Vector3.one);
                    break;
            }
        }

        private static void Trunk(Transform root, bool depleted, float radius, Color c)
        {
            float h = depleted ? 0.6f : 2.6f;
            GameObject trunk = Shapes.Part(root, "Trunk", MeshFactory.Cylinder(7), MaterialLibrary.Lit(c), Vector3.zero, new Vector3(radius * 2f, h, radius * 2f));
            trunk.AddCylinderCollider();
            if (!depleted) Collider(root, new Vector3(0, 2.5f, 0), new Vector3(1.2f, 4f, 1.2f), true);
        }

        private static void Car(Transform root, int v, bool stripped)
        {
            Color[] paint = { new Color(0.45f, 0.18f, 0.15f), new Color(0.2f, 0.3f, 0.42f), new Color(0.55f, 0.53f, 0.45f), new Color(0.25f, 0.33f, 0.26f) };
            Material body = MaterialLibrary.Lit(stripped ? new Color(0.36f, 0.27f, 0.2f) : paint[v], 0.25f, 0.3f);
            Material glass = MaterialLibrary.Lit(new Color(0.12f, 0.15f, 0.17f), 0.6f);
            Material tire = MaterialLibrary.Lit(new Color(0.08f, 0.08f, 0.08f));
            Shapes.Box(root, "Body", body, new Vector3(0, 0.65f, 0), new Vector3(1.8f, 0.7f, 4.2f));
            Shapes.Box(root, "Cabin", body, new Vector3(0, 1.25f, -0.2f), new Vector3(1.6f, 0.55f, 2.2f));
            Shapes.Box(root, "Windows", glass, new Vector3(0, 1.27f, -0.2f), new Vector3(1.62f, 0.4f, 2.0f), false);
            foreach (Vector3 w in new[] { new Vector3(-0.85f, 0.32f, 1.3f), new Vector3(0.85f, 0.32f, 1.3f), new Vector3(-0.85f, 0.32f, -1.3f), new Vector3(0.85f, 0.32f, -1.3f) })
                Shapes.Part(root, "Tire", MeshFactory.Cylinder(8), tire, w, new Vector3(0.62f, 0.25f, 0.62f), Quaternion.Euler(0, 0, 90));
        }

        /// <summary>
        /// Solid colliders go on the node itself; trigger hitboxes (for aiming at foliage) go on a
        /// child in the Interactable layer so they never block movement or navigation.
        /// </summary>
        public static void Collider(Transform root, Vector3 center, Vector3 size, bool trigger)
        {
            Transform host = root;
            if (trigger)
            {
                var child = new GameObject("Hitbox");
                child.transform.SetParent(root, false);
                child.layer = Layers.Interactable;
                host = child.transform;
            }
            var c = host.gameObject.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            c.isTrigger = trigger;
        }
    }
}
