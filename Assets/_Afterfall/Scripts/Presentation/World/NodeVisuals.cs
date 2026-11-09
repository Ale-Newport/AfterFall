using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedural visuals for harvestable resource nodes and items lying in the world. The models
    /// come from MeshFactory.Nature (one cached mesh per material and variant, variant = id % 4);
    /// this class picks the materials and adds the colliders gameplay and pathing rely on:
    /// solid boxes or a trunk capsule on the node, trigger "Hitbox" children for foliage.
    /// </summary>
    public static class NodeVisuals
    {
        // Trees sway as a whole from the ground: trunk and crown share the pivot and the strength,
        // so the crown never slides off its trunk.
        private const float PineWind = 0.22f, BirchWind = 0.32f;

        private static readonly Color BarkTint = new Color(0.36f, 0.29f, 0.23f);
        private static readonly Color BirchTint = new Color(0.86f, 0.85f, 0.8f);

        public static void Build(Transform root, ResourceNodeDefinition def, ResourceNodeState state)
        {
            int v = (int)(state.Id.Value % 4);
            bool depleted = state.Depleted;
            switch (def.Visual)
            {
                case "tree_pine":
                    Tree(root, v, depleted, false);
                    break;
                case "tree_birch":
                    Tree(root, v, depleted, true);
                    break;
                case "branches":
                    Shapes.Mesh(root, "Branches", MeshFactory.NatureBranches(v, depleted), MaterialLibrary.Surface(Art.Surface.Bark, BarkTint), Vector3.zero);
                    Collider(root, new Vector3(0, 0.3f, 0), new Vector3(1.6f, 0.6f, 1.6f), true);
                    break;
                case "rock":
                    Shapes.Mesh(root, "Rock", MeshFactory.NatureBoulder(v, depleted), MaterialLibrary.Surface(Art.Surface.Rock, new Color(0.55f, 0.54f, 0.51f)), Vector3.zero);
                    // A mined-out rock keeps its footprint but only its broken stub stands.
                    if (depleted) Collider(root, new Vector3(0, 0.22f, 0), new Vector3(1.2f, 0.5f, 1.1f), false);
                    else Collider(root, new Vector3(0, 0.35f, 0), new Vector3(1.2f, 0.8f, 1.1f), false);
                    break;
                case "ore":
                    Mesh[] ore = MeshFactory.NatureOre(v, depleted);
                    Shapes.Mesh(root, "Rock", ore[0], MaterialLibrary.Surface(Art.NatureSurfaces.IronOre, new Color(0.48f, 0.46f, 0.45f), 0.16f, 0f), Vector3.zero);
                    if (ore[1] != null) Shapes.Mesh(root, "Vein", ore[1], MaterialLibrary.Surface(Art.NatureSurfaces.IronOre, new Color(0.56f, 0.36f, 0.24f), 0.35f, 0.35f), Vector3.zero);
                    if (depleted) Collider(root, new Vector3(0, 0.36f, 0), new Vector3(1.8f, 0.72f, 1.6f), false);
                    else Collider(root, new Vector3(0, 0.6f, 0), new Vector3(1.8f, 1.2f, 1.6f), false);
                    break;
                case "fiber":
                    Shapes.Mesh(root, "Fiber", MeshFactory.NatureFiber(v, depleted), MaterialLibrary.Surface(Art.Surface.Grass, new Color(0.44f, 0.5f, 0.27f), wind: 1.6f, doubleSided: true), Vector3.zero);
                    Collider(root, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.8f, 0.9f), true);
                    break;
                case "bush":
                    Mesh[] bush = MeshFactory.NatureBush(v);
                    Shapes.Mesh(root, "Bush", bush[0], MaterialLibrary.Surface(Art.Surface.Foliage, new Color(0.27f, 0.39f, 0.2f), tileScale: 0.35f, wind: 0.8f), Vector3.zero);
                    if (!depleted)
                        Shapes.Mesh(root, "Berries", bush[1], MaterialLibrary.Surface(Art.Surface.Plain, new Color(0.62f, 0.06f, 0.1f), 0.65f, wind: 0.8f), Vector3.zero, null, false);
                    Collider(root, new Vector3(0, 0.5f, 0), new Vector3(1.4f, 1f, 1.4f), true);
                    break;
                case "scrap":
                    Mesh[] scrap = MeshFactory.NatureScrap(v, depleted);
                    Shapes.Mesh(root, "Junk", scrap[0], MaterialLibrary.Surface(Art.NatureSurfaces.Corrosion, new Color(0.5f, 0.4f, 0.32f), doubleSided: true), Vector3.zero);
                    Shapes.Mesh(root, "Tyre", scrap[1], Rubber, Vector3.zero);
                    Shapes.Mesh(root, "Dirt", scrap[2], MaterialLibrary.Surface(Art.Surface.Soil, new Color(0.38f, 0.32f, 0.26f)), Vector3.zero);
                    // Picked clean, only flat remnants are left to step over.
                    if (depleted) Collider(root, new Vector3(0, 0.1f, 0), new Vector3(1.6f, 0.2f, 1.3f), false);
                    else Collider(root, new Vector3(0, 0.4f, 0), new Vector3(1.6f, 0.8f, 1.3f), false);
                    break;
                case "car":
                    Car(root, v, depleted);
                    break;
                case "pump_gas":
                    Mesh[] pump = MeshFactory.NaturePump(v, depleted);
                    Shapes.Mesh(root, "Pump", pump[0], Paint, Vector3.zero);
                    Shapes.Mesh(root, "Trim", pump[1], Trim, Vector3.zero);
                    Shapes.Mesh(root, "Hose", pump[2], Rubber, Vector3.zero);
                    Shapes.Mesh(root, "Island", pump[3], Concrete, Vector3.zero);
                    Collider(root, new Vector3(0, 0.85f, 0), new Vector3(0.7f, 1.7f, 0.5f), false);
                    break;
                case "transformer":
                    Mesh[] tr = MeshFactory.NatureTransformer(v, depleted);
                    Shapes.Mesh(root, "Body", tr[0], MaterialLibrary.Surface(Art.Surface.PaintedMetal, new Color(0.42f, 0.47f, 0.43f), 0.3f), Vector3.zero);
                    Shapes.Mesh(root, "Insulators", tr[1], MaterialLibrary.Surface(Art.Surface.Plain, new Color(0.45f, 0.26f, 0.15f), 0.75f), Vector3.zero);
                    Shapes.Mesh(root, "Cables", tr[2], Rubber, Vector3.zero);
                    Shapes.Mesh(root, "Pad", tr[3], Concrete, Vector3.zero);
                    Collider(root, new Vector3(0, 0.7f, 0), new Vector3(1.6f, 1.4f, 1.2f), false);
                    break;
                default:
                    Shapes.Box(root, "Node", MaterialLibrary.Lit(Color.magenta), new Vector3(0, 0.5f, 0), Vector3.one);
                    break;
            }
        }

        /// <summary>
        /// Items lying in the world: a sack, or the player's backpack plus a tall light beacon (child
        /// "Beacon") marking the lost belongings. Includes the interaction hitbox.
        /// </summary>
        public static void BuildDrop(Transform root, bool belongings)
        {
            Shapes.Mesh(root, belongings ? "Backpack" : "Sack", MeshFactory.NatureDrop(belongings), MaterialLibrary.Surface(Art.Surface.Fabric, new Color(0.78f, 0.74f, 0.66f)), Vector3.zero);
            if (belongings)
                Shapes.Part(root, "Beacon", MeshFactory.Cylinder(6), MaterialLibrary.Emissive(new Color(0.4f, 0.7f, 1f), new Color(0.3f, 0.6f, 1f) * 2f), Vector3.zero, new Vector3(0.12f, 30f, 0.12f), null, false);
            Collider(root, new Vector3(0, 0.4f, 0), new Vector3(1f, 0.8f, 1f), true);
        }

        // Old, oxidised paint: low gloss and no metallic sheen, so faded colours keep their hue.
        private static Material Paint => MaterialLibrary.Surface(Art.Surface.PaintedMetal, new Color(0.8f, 0.8f, 0.8f), 0.3f, 0.05f);
        private static Material Trim => MaterialLibrary.Surface(Art.NatureSurfaces.Corrosion, new Color(0.55f, 0.5f, 0.46f), 0.35f, 0.5f);
        private static Material Rubber => MaterialLibrary.Surface(Art.Surface.Rubber, new Color(0.13f, 0.12f, 0.12f));
        private static Material Concrete => MaterialLibrary.Surface(Art.Surface.Concrete, new Color(0.56f, 0.55f, 0.52f));

        /// <summary>A standing tree (trunk capsule + foliage hitbox) or its stump with a fresh cut face.</summary>
        private static void Tree(Transform root, int v, bool depleted, bool birch)
        {
            float radius = birch ? 0.26f : 0.38f;
            GameObject trunk;
            if (depleted)
            {
                Mesh[] stump = MeshFactory.NatureStump(v, birch);
                trunk = Shapes.Mesh(root, "Trunk", stump[0], birch ? MaterialLibrary.Surface(Art.NatureSurfaces.BirchBark, BirchTint) : MaterialLibrary.Surface(Art.Surface.Bark, BarkTint), Vector3.zero);
                Color wood = birch ? new Color(0.8f, 0.7f, 0.56f) : new Color(0.74f, 0.58f, 0.42f);
                Shapes.Mesh(root, "Cut", stump[1], MaterialLibrary.Surface(Art.NatureSurfaces.EndGrain, wood, objectSpace: true), Vector3.zero);
                TrunkCollider(trunk, radius, 0.6f);
                return;
            }
            Mesh[] tree = birch ? MeshFactory.NatureBirch(v) : MeshFactory.NaturePine(v);
            Material bark = birch
                ? MaterialLibrary.Surface(Art.NatureSurfaces.BirchBark, BirchTint, wind: BirchWind)
                : MaterialLibrary.Surface(Art.Surface.Bark, BarkTint, wind: PineWind);
            Material crown = birch
                ? MaterialLibrary.Surface(Art.Surface.Foliage, new Color(0.44f, 0.52f, 0.26f), tileScale: 0.4f, wind: BirchWind)
                : MaterialLibrary.Surface(Art.Surface.Needles, new Color(0.25f, 0.34f, 0.21f), tileScale: 0.5f, wind: PineWind);
            trunk = Shapes.Mesh(root, "Trunk", tree[0], bark, Vector3.zero);
            Shapes.Mesh(root, "Crown", tree[1], crown, Vector3.zero);
            TrunkCollider(trunk, radius, 2.6f);
            Collider(root, new Vector3(0, 2.5f, 0), new Vector3(1.2f, 4f, 1.2f), true);
        }

        private static void TrunkCollider(GameObject trunk, float radius, float height)
        {
            var c = trunk.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = radius;
            c.height = height;
            c.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private static void Car(Transform root, int v, bool stripped)
        {
            Mesh[] car = MeshFactory.NatureCar(v, stripped);
            Material body = stripped ? MaterialLibrary.Surface(Art.NatureSurfaces.Corrosion, new Color(0.52f, 0.4f, 0.31f), doubleSided: true) : Paint;
            Shapes.Mesh(root, "Body", car[0], body, Vector3.zero);
            Shapes.Mesh(root, "Trim", car[1], Trim, Vector3.zero);
            if (car[2] != null) Shapes.Mesh(root, "Windows", car[2], MaterialLibrary.Surface(Art.Surface.Glass, new Color(0.16f, 0.18f, 0.19f)), Vector3.zero);
            Shapes.Mesh(root, "Rubber", car[3], Rubber, Vector3.zero);
            // Body and cabin blockers as before (odd variants are pickups with a shorter cab).
            Collider(root, new Vector3(0, 0.65f, 0), new Vector3(1.8f, 0.7f, 4.2f), false);
            if ((v & 1) == 1) Collider(root, new Vector3(0, 1.25f, 0.15f), new Vector3(1.6f, 0.55f, 1.5f), false);
            else Collider(root, new Vector3(0, 1.25f, -0.2f), new Vector3(1.6f, 0.55f, 2.2f), false);
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
