using Afterfall.Domain.Construction;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Procedural visuals for constructible structures and world containers.</summary>
    public static class StructureVisuals
    {
        public static readonly Color GhostValid = new Color(0.35f, 0.85f, 0.45f, 0.42f);
        public static readonly Color GhostInvalid = new Color(0.95f, 0.3f, 0.25f, 0.42f);
        public static readonly Color BlueprintTint = new Color(0.35f, 0.65f, 1f, 0.38f);

        /// <summary>
        /// Builds visuals under <paramref name="root"/>. When <paramref name="ghost"/> is not null,
        /// every renderer uses that material and no solid colliders are created.
        /// </summary>
        public static void Build(Transform root, BuildingDefinition def, Material ghost, bool solidColliders)
        {
            Color baseColor = Conversions.ParseColor(def.Color, new Color(0.6f, 0.55f, 0.48f));
            Material M(Color c, float smooth = 0.12f, float metal = 0f) => ghost ?? MaterialLibrary.Lit(c, smooth, metal);
            Material main = M(baseColor);
            Material dark = M(baseColor * 0.6f);
            Material steel = M(new Color(0.5f, 0.52f, 0.55f), 0.35f, 0.6f);
            Material wood = M(new Color(0.5f, 0.37f, 0.24f));
            bool col = solidColliders && ghost == null;
            float sx = def.SizeX, sy = def.SizeY, sz = def.SizeZ;

            switch (def.Visual)
            {
                case "foundation":
                    Box(root, "Platform", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col);
                    for (int i = 0; i < 4; i++)
                        Box(root, "Plank", dark, new Vector3(-sx * 0.375f + i * sx * 0.25f, sy + 0.005f, 0), new Vector3(0.04f, 0.01f, sz * 0.98f), false);
                    Box(root, "Footing", dark, new Vector3(0, -0.6f, 0), new Vector3(sx * 0.95f, 1.2f, sz * 0.95f), col);
                    break;
                case "wall":
                    Wall(root, main, dark, sx, sy, sz, col, 0f, 0f, 0f, 0f);
                    break;
                case "doorway":
                    Wall(root, main, dark, sx, sy, sz, col, 0f, 1.6f, 0f, 2.3f); // wide enough for NavMesh agents
                    break;
                case "window_wall":
                    Wall(root, main, dark, sx, sy, sz, col, 0f, 1.2f, 1.0f, 2.1f);
                    if (ghost == null) Box(root, "Shutter", dark, new Vector3(0.75f, 1.55f, sz * 0.5f + 0.03f), new Vector3(0.3f, 1.1f, 0.04f), false);
                    break;
                case "roof":
                    Shapes.NotWalkable(Box(root, "Roof", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col));
                    Box(root, "Eave", dark, new Vector3(0, sy * 0.5f, sz * 0.5f), new Vector3(sx, sy * 1.2f, 0.12f), false);
                    break;
                case "crate":
                case "loot_crate":
                    Box(root, "Crate", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col);
                    Box(root, "Band", dark, new Vector3(0, sy * 0.5f, 0), new Vector3(sx + 0.02f, 0.1f, sz + 0.02f), false);
                    Box(root, "Lid", dark, new Vector3(0, sy + 0.02f, 0), new Vector3(sx * 0.95f, 0.04f, sz * 0.95f), false);
                    break;
                case "workbench":
                    Box(root, "Top", main, new Vector3(0, 0.9f, 0), new Vector3(sx, 0.12f, sz), col);
                    foreach (Vector3 leg in Corners(sx * 0.42f, sz * 0.38f)) Box(root, "Leg", dark, leg + new Vector3(0, 0.42f, 0), new Vector3(0.1f, 0.84f, 0.1f), false);
                    Box(root, "Vice", steel, new Vector3(sx * 0.35f, 1.05f, sz * 0.3f), new Vector3(0.2f, 0.18f, 0.18f), false);
                    Box(root, "Saw", steel, new Vector3(-sx * 0.2f, 0.98f, 0), new Vector3(0.6f, 0.02f, 0.14f), false);
                    if (col) Collider(root, new Vector3(0, 0.5f, 0), new Vector3(sx, 1f, sz));
                    break;
                case "bed":
                    Box(root, "Mat", main, new Vector3(0, 0.12f, 0), new Vector3(sx, 0.24f, sz), false);
                    Box(root, "Pillow", M(new Color(0.75f, 0.72f, 0.65f)), new Vector3(sx * 0.38f, 0.3f, 0), new Vector3(0.4f, 0.14f, sz * 0.8f), false);
                    Box(root, "Blanket", M(new Color(0.3f, 0.35f, 0.42f)), new Vector3(-sx * 0.12f, 0.26f, 0), new Vector3(sx * 0.7f, 0.06f, sz * 1.02f), false);
                    break;
                case "barricade":
                    for (int i = 0; i < 3; i++)
                        Shapes.Part(root, "Log", MeshFactory.Cylinder(7), wood, new Vector3(-sx * 0.5f, 0.3f + i * 0.45f, 0), new Vector3(0.45f, sx, 0.45f), Quaternion.Euler(0, 0, -90));
                    foreach (float x in new[] { -sx * 0.4f, sx * 0.4f })
                        Shapes.Part(root, "Stake", MeshFactory.Cone(5), wood, new Vector3(x, 0, 0.1f), new Vector3(0.3f, sy, 0.3f), Quaternion.Euler(-20, 0, 0));
                    if (col) Collider(root, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz));
                    break;
                case "furnace":
                    Box(root, "Body", main, new Vector3(0, sy * 0.4f, 0), new Vector3(sx, sy * 0.8f, sz), col);
                    Shapes.Part(root, "Chimney", MeshFactory.Cylinder(8), dark, new Vector3(0, sy * 0.8f, -sz * 0.2f), new Vector3(0.5f, sy * 0.7f, 0.5f));
                    Box(root, "Mouth", M(new Color(0.1f, 0.08f, 0.07f)), new Vector3(0, sy * 0.3f, sz * 0.5f), new Vector3(sx * 0.45f, sy * 0.3f, 0.05f), false).name = "Fire";
                    break;
                case "workshop":
                    Box(root, "Bench", main, new Vector3(0, 0.85f, 0), new Vector3(sx, 0.15f, sz), false);
                    Box(root, "Base", dark, new Vector3(0, 0.4f, 0), new Vector3(sx * 0.95f, 0.8f, sz * 0.9f), false);
                    Shapes.Part(root, "Lathe", MeshFactory.Cylinder(10), steel, new Vector3(-sx * 0.2f, 1.05f, 0), new Vector3(0.35f, sx * 0.5f, 0.35f), Quaternion.Euler(0, 0, 90));
                    Box(root, "Press", steel, new Vector3(sx * 0.32f, 1.35f, 0), new Vector3(0.4f, 0.9f, 0.4f), false);
                    Box(root, "Lamp", ghost ?? MaterialLibrary.Emissive(new Color(0.9f, 0.8f, 0.4f), new Color(0.6f, 0.5f, 0.2f)), new Vector3(sx * 0.32f, 1.85f, 0), new Vector3(0.2f, 0.1f, 0.2f), false).name = "Indicator";
                    if (col) Collider(root, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz));
                    break;
                case "pump":
                    Shapes.Part(root, "Tank", MeshFactory.Cylinder(10), main, Vector3.zero, new Vector3(sx * 0.8f, sy * 0.7f, sz * 0.8f));
                    Box(root, "Motor", steel, new Vector3(0, sy * 0.8f, 0), new Vector3(0.6f, 0.4f, 0.6f), false);
                    Shapes.Part(root, "Pipe", MeshFactory.Cylinder(6), steel, new Vector3(0, 0.3f, sz * 0.4f), new Vector3(0.18f, 2.2f, 0.18f), Quaternion.Euler(90, 0, 0));
                    if (col) Collider(root, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz));
                    break;
                case "generator":
                    Box(root, "Frame", dark, new Vector3(0, 0.08f, 0), new Vector3(sx, 0.16f, sz), false);
                    Box(root, "Engine", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx * 0.9f, sy * 0.75f, sz * 0.85f), false).name = "Engine";
                    Shapes.Part(root, "Exhaust", MeshFactory.Cylinder(6), steel, new Vector3(-sx * 0.35f, sy * 0.85f, 0), new Vector3(0.12f, 0.5f, 0.12f));
                    Box(root, "Panel", M(new Color(0.15f, 0.15f, 0.15f)), new Vector3(sx * 0.3f, sy * 0.6f, sz * 0.43f), new Vector3(0.4f, 0.3f, 0.02f), false);
                    if (col) Collider(root, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz));
                    break;
                case "pole":
                    GameObject pole = Shapes.Part(root, "Pole", MeshFactory.Cylinder(6), wood, Vector3.zero, new Vector3(0.25f, sy, 0.25f));
                    if (col) pole.AddCylinderCollider();
                    Box(root, "Cross", wood, new Vector3(0, sy - 0.4f, 0), new Vector3(1.2f, 0.1f, 0.1f), false);
                    foreach (float x in new[] { -0.5f, 0.5f })
                        Shapes.Part(root, "Insulator", MeshFactory.Cylinder(6), M(new Color(0.75f, 0.85f, 0.9f)), new Vector3(x, sy - 0.35f, 0), new Vector3(0.08f, 0.18f, 0.08f));
                    break;
                case "lamp":
                    GameObject lp = Shapes.Part(root, "Post", MeshFactory.Cylinder(6), steel, Vector3.zero, new Vector3(0.12f, sy, 0.12f));
                    if (col) lp.AddCylinderCollider();
                    Box(root, "Arm", steel, new Vector3(0, sy, 0.25f), new Vector3(0.08f, 0.08f, 0.6f), false);
                    Box(root, "Head", ghost ?? MaterialLibrary.Lit(new Color(0.85f, 0.82f, 0.7f)), new Vector3(0, sy - 0.06f, 0.5f), new Vector3(0.3f, 0.12f, 0.3f), false).name = "LampHead";
                    break;
                case "battery":
                    for (int i = 0; i < 2; i++)
                        Box(root, "Cell", main, new Vector3((i - 0.5f) * sx * 0.5f, sy * 0.45f, 0), new Vector3(sx * 0.45f, sy * 0.9f, sz), false);
                    Box(root, "Terminals", M(new Color(0.8f, 0.2f, 0.15f)), new Vector3(0, sy * 0.92f, 0), new Vector3(sx * 0.9f, 0.08f, 0.15f), false);
                    if (col) Collider(root, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz));
                    break;
                case "planter":
                    Box(root, "Frame", dark, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), false);
                    Box(root, "Soil", M(new Color(0.24f, 0.17f, 0.11f)), new Vector3(0, sy + 0.01f, 0), new Vector3(sx * 0.9f, 0.04f, sz * 0.85f), false).name = "Soil";
                    if (ghost == null)
                    {
                        var crops = new GameObject("Crops").transform;
                        crops.SetParent(root, false);
                        crops.localPosition = new Vector3(0, sy, 0);
                        for (int i = 0; i < 6; i++)
                            Shapes.Part(crops, "Plant", MeshFactory.Blob(i), MaterialLibrary.Lit(new Color(0.3f, 0.5f, 0.22f)), new Vector3((i % 3 - 1) * sx * 0.3f, 0f, (i / 3 - 0.5f) * sz * 0.45f), new Vector3(0.35f, 0.4f, 0.35f));
                    }
                    break;
                case "cabinet":
                case "fridge":
                case "wardrobe":
                case "medcab":
                case "locker":
                case "shelf":
                    Box(root, "Body", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col);
                    Box(root, "Door", dark, new Vector3(0, sy * 0.5f, sz * 0.5f + 0.01f), new Vector3(sx * 0.92f, sy * 0.92f, 0.02f), false);
                    Box(root, "Handle", steel, new Vector3(sx * 0.35f, sy * 0.55f, sz * 0.5f + 0.03f), new Vector3(0.04f, 0.18f, 0.04f), false);
                    break;
                case "toolbox":
                    Box(root, "Box", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col);
                    Box(root, "Handle", dark, new Vector3(0, sy + 0.06f, 0), new Vector3(sx * 0.6f, 0.06f, 0.06f), false);
                    break;
                default:
                    Box(root, "Block", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col);
                    break;
            }

            // Every structure gets a trigger hitbox so it can be aimed at even without solid colliders.
            var hit = new GameObject("Hitbox");
            hit.transform.SetParent(root, false);
            hit.layer = Layers.Interactable;
            var bc = hit.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0, Mathf.Max(0.3f, sy * 0.5f), 0);
            bc.size = new Vector3(Mathf.Max(0.6f, sx), Mathf.Max(0.6f, sy), Mathf.Max(0.6f, sz));
        }

        private static void Wall(Transform root, Material main, Material dark, float sx, float sy, float sz, bool col, float openingCenter, float openingWidth, float openingBottom, float openingTop)
        {
            if (openingWidth <= 0f)
            {
                Box(root, "Wall", main, new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz), col);
            }
            else
            {
                float half = sx * 0.5f;
                float left = openingCenter - openingWidth * 0.5f + half;
                float right = half - (openingCenter + openingWidth * 0.5f);
                Box(root, "WallL", main, new Vector3(-half + left * 0.5f, sy * 0.5f, 0), new Vector3(left, sy, sz), col);
                Box(root, "WallR", main, new Vector3(half - right * 0.5f, sy * 0.5f, 0), new Vector3(right, sy, sz), col);
                if (openingBottom > 0f) Box(root, "WallB", main, new Vector3(openingCenter, openingBottom * 0.5f, 0), new Vector3(openingWidth, openingBottom, sz), col);
                Box(root, "WallT", main, new Vector3(openingCenter, openingTop + (sy - openingTop) * 0.5f, 0), new Vector3(openingWidth, sy - openingTop, sz), col);
            }
            Box(root, "Beam", dark, new Vector3(0, sy - 0.08f, 0), new Vector3(sx, 0.16f, sz + 0.04f), false);
        }

        private static GameObject Box(Transform root, string name, Material m, Vector3 c, Vector3 s, bool collider)
        {
            return Shapes.Box(root, name, m, c, s, collider);
        }

        private static void Collider(Transform root, Vector3 center, Vector3 size)
        {
            var c = root.gameObject.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
        }

        private static Vector3[] Corners(float x, float z) => new[] { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z) };
    }
}
