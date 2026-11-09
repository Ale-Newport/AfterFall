using System.Collections.Generic;
using Afterfall.Domain.Construction;
using Afterfall.Presentation.Art;
using UnityEngine;
using static Afterfall.Presentation.StructureModels;
using S = Afterfall.Presentation.StructureSlot;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedural visuals for constructible structures and world containers. Each model is built once
    /// per definition and instance variant (<see cref="MeshFactory.StructureModel"/>) as one mesh per
    /// material slot, so a structure is a handful of renderers whatever its detail. Colliders are plain
    /// boxes kept exactly as the navigation and placement code expects; decoration never collides.
    /// </summary>
    public static class StructureVisuals
    {
        public static readonly Color GhostValid = new Color(0.35f, 0.85f, 0.45f, 0.42f);
        public static readonly Color GhostInvalid = new Color(0.95f, 0.3f, 0.25f, 0.42f);
        public static readonly Color BlueprintTint = new Color(0.35f, 0.65f, 1f, 0.38f);

        private static readonly Material[] SlotMaterials = new Material[(int)S.Glow + 1];

        /// <summary>
        /// Builds visuals under <paramref name="root"/>. When <paramref name="ghost"/> is not null,
        /// every renderer uses that material and no solid colliders are created.
        /// </summary>
        public static void Build(Transform root, BuildingDefinition def, Material ghost, bool solidColliders)
        {
            Color color = Conversions.ParseColor(def.Color, new Color(0.6f, 0.55f, 0.48f));
            bool col = solidColliders && ghost == null;
            int variant = ghost != null ? 0 : VariantAt(root.position);
            StructurePart[] parts = MeshFactory.StructureModel("structure:" + def.Id, ghost != null, variant, k => Model(k, def, color));
            Instantiate(root, parts, ghost);
            if (col) Colliders(root, def);

            // Every structure gets a trigger hitbox so it can be aimed at even without solid colliders.
            float sx = def.SizeX, sy = def.SizeY, sz = def.SizeZ;
            var hit = new GameObject("Hitbox");
            hit.transform.SetParent(root, false);
            hit.layer = Layers.Interactable;
            var bc = hit.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0, Mathf.Max(0.3f, sy * 0.5f), 0);
            bc.size = new Vector3(Mathf.Max(0.6f, sx), Mathf.Max(0.6f, sy), Mathf.Max(0.6f, sz));
        }

        /// <summary>A stable variant (0..3) from the placement, so neighbouring copies differ a little.</summary>
        private static int VariantAt(Vector3 p) =>
            Mathf.Min(3, (int)(MeshFactory.Hash(4211, new Vector3(Mathf.Round(p.x * 2f), Mathf.Round(p.y * 2f), Mathf.Round(p.z * 2f))) * 4f));

        private static void Model(StructureKit k, BuildingDefinition def, Color color)
        {
            float sx = def.SizeX, sy = def.SizeY, sz = def.SizeZ;
            Color wood = WoodTone(color);
            switch (def.Visual)
            {
                case "foundation":
                    Foundation(k, sx, sy, sz, wood);
                    break;
                case "wall":
                    Wall(k, sx, sy, sz, wood, 0f, 0f, 0f);
                    break;
                case "doorway":
                    Wall(k, sx, sy, sz, wood, 1.6f, 0f, 2.3f); // wide enough for NavMesh agents
                    break;
                case "window_wall":
                    Wall(k, sx, sy, sz, wood, 1.2f, 1.0f, 2.1f);
                    break;
                case "roof":
                    Roof(k, sx, sy, sz, wood);
                    break;
                case "crate":
                    Crate(k, sx, sy, sz, wood, def.Category == BuildingCategory.WorldProp);
                    break;
                case "workbench":
                    Workbench(k, sx, sy, sz, wood);
                    break;
                case "bed":
                    Bed(k, sx, sy, sz, color);
                    break;
                case "barricade":
                    Barricade(k, sx, sy, sz, wood);
                    break;
                case "planter":
                    const float soil = 0.315f;
                    Planter(k, sx, sy, sz, wood);
                    k.Group("Soil", Vector3.zero);
                    PlanterSoil(k, sx, sz, soil);
                    k.EndGroup();
                    if (k.Detail)
                    {
                        var crops = new Vector3(0f, soil + 0.04f, 0f);
                        k.Group("Crops", crops);
                        Crops(k, sx, crops);
                        k.EndGroup();
                    }
                    break;
                case "furnace":
                    Furnace(k, sx, sy, sz);
                    break;
                case "workshop":
                    Workshop(k, sx, sy, sz, color);
                    break;
                case "pump":
                    Pump(k, sx, sy, sz, color);
                    break;
                case "generator":
                    Generator(k, sx, sy, sz, color);
                    break;
                case "pole":
                    Pole(k, sy, wood);
                    break;
                case "lamp":
                    Lamp(k, sy);
                    break;
                case "battery":
                    Battery(k, sx, sy, sz, color);
                    break;
                case "cabinet":
                    Cabinet(k, sx, sy, sz, color);
                    break;
                case "fridge":
                    Fridge(k, sx, sy, sz, color);
                    break;
                case "wardrobe":
                    Wardrobe(k, sx, sy, sz, color);
                    break;
                case "medcab":
                    MedicineCabinet(k, sx, sy, sz, color);
                    break;
                case "locker":
                    Locker(k, sx, sy, sz, color);
                    break;
                case "shelf":
                    StoreShelf(k, sx, sy, sz, color);
                    break;
                case "toolbox":
                    Toolbox(k, sx, sy, sz, color);
                    break;
                default:
                    k.Span(S.Paint, new Vector3(-sx * 0.5f, 0f, -sz * 0.5f), new Vector3(sx * 0.5f, sy, sz * 0.5f), Paint(color), 0.03f);
                    break;
            }
        }

        // ------------------------------------------------------------ assembly

        /// <summary>One renderer per material slot; named parts become their own objects at their pivots.</summary>
        private static void Instantiate(Transform root, StructurePart[] parts, Material ghost)
        {
            foreach (StructurePart p in parts)
            {
                int used = 0, only = -1;
                for (int i = 0; i < p.Layers.Length; i++)
                {
                    if (p.Layers[i] == null) continue;
                    used++;
                    only = i;
                }
                if (p.Name.Length > 0 && used == 1)
                {
                    // Animated parts the game looks up by name (Fire, LampHead, Indicator, Soil) are single renderers.
                    Shapes.Mesh(root, p.Name, p.Layers[only], ghost ?? SlotMaterial((S)only), p.Origin);
                    continue;
                }
                Transform parent = root;
                if (p.Name.Length > 0)
                {
                    parent = new GameObject(p.Name).transform;
                    parent.SetParent(root, false);
                    parent.localPosition = p.Origin;
                }
                for (int i = 0; i < p.Layers.Length; i++)
                    if (p.Layers[i] != null) Shapes.Mesh(parent, ((S)i).ToString(), p.Layers[i], ghost ?? SlotMaterial((S)i), Vector3.zero);
            }
        }

        /// <summary>The shared material of a slot. Structures map their textures in object space so the pattern turns with them.</summary>
        private static Material SlotMaterial(S slot)
        {
            Material m = SlotMaterials[(int)slot];
            if (m != null) return m;
            switch (slot)
            {
                case S.Timber: m = MaterialLibrary.Surface(StructureSurfaces.Timber, StructurePalette.Timber, objectSpace: true); break;
                case S.TimberV: m = MaterialLibrary.Surface(StructureSurfaces.TimberVertical, StructurePalette.Timber, objectSpace: true); break;
                case S.PaintedWood: m = MaterialLibrary.Surface(Surface.Wood, StructurePalette.PaintedWood, 0.32f, objectSpace: true); break;
                case S.Bark: m = MaterialLibrary.Surface(Surface.Bark, StructurePalette.Bark, objectSpace: true); break;
                case S.BarkH: m = MaterialLibrary.Surface(StructureSurfaces.BarkHorizontal, StructurePalette.Bark, objectSpace: true); break;
                case S.Stone: m = MaterialLibrary.Surface(Surface.Stone, StructurePalette.Stone, objectSpace: true, tileScale: 0.8f); break;
                case S.Brick: m = MaterialLibrary.Surface(Surface.Brick, StructurePalette.Brick, objectSpace: true); break;
                case S.Concrete: m = MaterialLibrary.Surface(Surface.Concrete, StructurePalette.Concrete, objectSpace: true); break;
                case S.Metal: m = MaterialLibrary.Surface(Surface.Metal, StructurePalette.Metal, objectSpace: true, tileScale: 0.6f); break;
                case S.Paint: m = MaterialLibrary.Surface(Surface.PaintedMetal, StructurePalette.Paint, objectSpace: true, tileScale: 0.7f); break;
                case S.Rust: m = MaterialLibrary.Surface(Surface.Rust, StructurePalette.Rust, objectSpace: true, tileScale: 0.6f); break;
                case S.Corrugated: m = MaterialLibrary.Surface(Surface.Corrugated, StructurePalette.Corrugated, objectSpace: true); break;
                case S.Fabric: m = MaterialLibrary.Surface(Surface.Fabric, StructurePalette.Fabric, objectSpace: true); break;
                case S.Rubber: m = MaterialLibrary.Surface(Surface.Rubber, StructurePalette.Rubber, objectSpace: true); break;
                case S.Soil: m = MaterialLibrary.Surface(Surface.Soil, StructurePalette.Soil, objectSpace: true, tileScale: 0.35f); break;
                case S.Foliage: m = MaterialLibrary.Surface(Surface.Foliage, StructurePalette.Foliage, objectSpace: true, tileScale: 0.25f, wind: 0.6f); break;
                case S.Glass: m = MaterialLibrary.Surface(Surface.Glass, StructurePalette.Glass, 0.85f, objectSpace: true); break;
                case S.Plain: m = MaterialLibrary.Surface(Surface.Plain, StructurePalette.Plain, objectSpace: true); break;
                default: m = MaterialLibrary.Emissive(new Color(1f, 0.72f, 0.32f), new Color(1f, 0.55f, 0.15f) * 1.6f); break;
            }
            return SlotMaterials[(int)slot] = m;
        }

        // ------------------------------------------------------------ colliders

        /// <summary>Solid colliders exactly as before the art pass: navigation carves one obstacle per box.</summary>
        private static void Colliders(Transform root, BuildingDefinition def)
        {
            float sx = def.SizeX, sy = def.SizeY, sz = def.SizeZ;
            Vector3 whole = new Vector3(sx, sy, sz), mid = new Vector3(0f, sy * 0.5f, 0f);
            switch (def.Visual)
            {
                case "foundation":
                    ColliderBox(root, "Platform", mid, whole);
                    ColliderBox(root, "Footing", new Vector3(0f, -0.6f, 0f), new Vector3(sx * 0.95f, 1.2f, sz * 0.95f));
                    break;
                case "wall":
                    WallColliders(root, sx, sy, sz, 0f, 0f, 0f);
                    break;
                case "doorway":
                    WallColliders(root, sx, sy, sz, 1.6f, 0f, 2.3f);
                    break;
                case "window_wall":
                    WallColliders(root, sx, sy, sz, 1.2f, 1.0f, 2.1f);
                    break;
                case "roof":
                    Shapes.NotWalkable(ColliderBox(root, "Roof", mid, whole));
                    break;
                case "crate":
                    ColliderBox(root, "Crate", mid, whole);
                    break;
                case "workbench":
                    RootCollider(root, new Vector3(0f, 0.5f, 0f), new Vector3(sx, 1f, sz));
                    break;
                case "bed":
                case "planter":
                    break;
                case "furnace":
                    ColliderBox(root, "Body", new Vector3(0f, sy * 0.4f, 0f), new Vector3(sx, sy * 0.8f, sz));
                    break;
                case "barricade":
                case "workshop":
                case "pump":
                case "generator":
                case "battery":
                    RootCollider(root, mid, whole);
                    break;
                case "pole":
                    ColliderCylinder(root, "Pole", new Vector3(0.25f, sy, 0.25f));
                    break;
                case "lamp":
                    ColliderCylinder(root, "Post", new Vector3(0.12f, sy, 0.12f));
                    break;
                case "cabinet":
                case "fridge":
                case "wardrobe":
                case "medcab":
                case "locker":
                case "shelf":
                    ColliderBox(root, "Body", mid, whole);
                    break;
                case "toolbox":
                    ColliderBox(root, "Box", mid, whole);
                    break;
                default:
                    ColliderBox(root, "Block", mid, whole);
                    break;
            }
        }

        /// <summary>Wall boxes around the opening (centred, <paramref name="openW"/> wide, from <paramref name="openBottom"/> to <paramref name="openTop"/>).</summary>
        private static void WallColliders(Transform root, float sx, float sy, float sz, float openW, float openBottom, float openTop)
        {
            if (openW <= 0f)
            {
                ColliderBox(root, "Wall", new Vector3(0, sy * 0.5f, 0), new Vector3(sx, sy, sz));
                return;
            }
            float half = sx * 0.5f;
            float left = -openW * 0.5f + half;
            float right = half - openW * 0.5f;
            ColliderBox(root, "WallL", new Vector3(-half + left * 0.5f, sy * 0.5f, 0), new Vector3(left, sy, sz));
            ColliderBox(root, "WallR", new Vector3(half - right * 0.5f, sy * 0.5f, 0), new Vector3(right, sy, sz));
            if (openBottom > 0f) ColliderBox(root, "WallB", new Vector3(0f, openBottom * 0.5f, 0), new Vector3(openW, openBottom, sz));
            ColliderBox(root, "WallT", new Vector3(0f, openTop + (sy - openTop) * 0.5f, 0), new Vector3(openW, sy - openTop, sz));
        }

        private static GameObject ColliderBox(Transform root, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static void ColliderCylinder(Transform root, string name, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localScale = scale;
            go.AddCylinderCollider();
        }

        private static void RootCollider(Transform root, Vector3 center, Vector3 size)
        {
            var c = root.gameObject.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
        }
    }

    internal static partial class StructureModels
    {
        // ------------------------------------------------------------ furnace

        /// <summary>
        /// Brick smelting furnace on a fieldstone plinth: a sooty firebox mouth with an iron door swung
        /// open, steel bands and corner irons, tapered shoulders and a short stack. The coal bed and the
        /// glowing back wall are the "Fire" part.
        /// </summary>
        public static void Furnace(StructureKit k, float sx, float sy, float sz)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            const float bx = 0.7f, y0 = 0.2f, y1 = 1.12f, mw = 0.26f, my0 = 0.36f, my1 = 0.76f, depth = 0.34f;
            float zf = bx, zb = zf - depth;
            k.Span(S.Stone, new Vector3(-hx, -0.15f, -hz), new Vector3(hx, y0, hz), T(0.9f), 0.04f);

            // Weathered, sooty brick rather than fresh red.
            Color brick = new Color(0.72f, 0.66f, 0.62f);
            k.Span(S.Brick, new Vector3(-bx, y0, -bx), new Vector3(bx, y1, zb), brick);
            k.Span(S.Brick, new Vector3(-bx, y0, zb), new Vector3(-mw, y1, zf), brick);
            k.Span(S.Brick, new Vector3(mw, y0, zb), new Vector3(bx, y1, zf), brick);
            k.Span(S.Brick, new Vector3(-mw, y0, zb), new Vector3(mw, my0, zf), brick);
            k.Span(S.Brick, new Vector3(-mw, my1, zb), new Vector3(mw, y1, zf), brick);
            // Soot-blackened lining of the firebox.
            Color soot = T(0.16f, 0.45f);
            k.Span(S.Brick, new Vector3(-mw, my0, zb), new Vector3(-mw + 0.01f, my1, zf - 0.004f), soot);
            k.Span(S.Brick, new Vector3(mw - 0.01f, my0, zb), new Vector3(mw, my1, zf - 0.004f), soot);
            k.Span(S.Brick, new Vector3(-mw, my1 - 0.01f, zb), new Vector3(mw, my1, zf - 0.004f), soot);
            k.Span(S.Brick, new Vector3(-mw, my0, zb), new Vector3(mw, my0 + 0.01f, zf - 0.004f), soot);
            k.Span(S.Brick, new Vector3(-mw, my0, zb), new Vector3(mw, my1, zb + 0.01f), soot);

            // The fire: StructureView swaps in an emissive material and animates it.
            k.Group("Fire", new Vector3(0f, my0, zb + 0.15f));
            k.Sphere(S.Glow, new Vector3(0f, my0 + 0.005f, zb + 0.15f), new Vector3(0.21f, 0.05f, 0.13f), T(1f), 1, false, d => 1f + (MeshFactory.Hash(77, d) - 0.5f) * 0.4f);
            k.Span(S.Glow, new Vector3(-mw + 0.012f, my0 + 0.01f, zb + 0.011f), new Vector3(mw - 0.012f, my0 + 0.11f, zb + 0.014f), T(1f));
            k.EndGroup();
            // Charcoal lumps on the glowing bed: dark shapes against the glow, lit by the firelight.
            for (int i = 0; i < 9; i++)
            {
                int seed = i;
                Vector3 p = new Vector3(-0.17f + (i % 5) * 0.085f + Fixed(i + 430) * 0.03f, my0 + 0.045f + (i / 5) * 0.02f, zb + 0.08f + (i / 5) * 0.12f + Fixed(i + 440) * 0.06f);
                k.Sphere(S.Rubber, p, new Vector3(0.04f, 0.03f, 0.035f) * (0.8f + Fixed(i + 450) * 0.5f), T(0.06f), 0, false, d => 1f + (MeshFactory.Hash(seed + 460, d) - 0.5f) * 0.5f);
            }

            // Iron: lintel, corner angles and two tie bands.
            Color iron = T(0.75f);
            k.Span(S.Rust, new Vector3(-mw - 0.09f, my1, zf - 0.01f), new Vector3(mw + 0.09f, my1 + 0.055f, zf + 0.014f), iron, 0.004f);
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                {
                    k.Span(S.Rust, new Vector3(xs * (bx - 0.045f), y0, zs * (bx + 0.008f)), new Vector3(xs * (bx + 0.008f), y1, zs * bx), iron, 0.002f);
                    k.Span(S.Rust, new Vector3(xs * (bx + 0.008f), y0, zs * (bx - 0.045f)), new Vector3(xs * bx, y1, zs * bx), iron, 0.002f);
                }
            foreach (float y in new[] { 0.29f, 0.98f })
                k.Span(S.Rust, new Vector3(-bx - 0.012f, y, -bx - 0.012f), new Vector3(bx + 0.012f, y + 0.045f, bx + 0.012f), Scale(iron, 0.9f), 0.003f);

            // Shoulders and stack.
            Matrix4x4 saved = k.Matrix;
            k.Matrix = saved * Matrix4x4.TRS(new Vector3(0f, y1, 0f), Quaternion.Euler(0f, 45f, 0f), Vector3.one);
            k.Cylinder(S.Brick, Vector3.zero, new Vector3(0f, 0.27f, 0f), bx * 1.4142f, 0.31f * 1.4142f, 4, Scale(brick, 0.85f), false, false, true);
            k.Matrix = saved;
            k.Span(S.Brick, new Vector3(-0.26f, y1 + 0.2f, -0.26f), new Vector3(0.26f, sy - 0.16f, 0.26f), Scale(brick, 0.8f));
            k.Span(S.Brick, new Vector3(-0.261f, sy - 0.16f, -0.261f), new Vector3(0.261f, sy - 0.07f, 0.261f), Scale(brick, 0.35f));
            k.Span(S.Stone, new Vector3(-0.32f, sy - 0.07f, -0.32f), new Vector3(0.32f, sy - 0.025f, 0.32f), T(0.55f), 0.012f);
            k.Cylinder(S.Rust, new Vector3(0f, sy - 0.03f, 0f), new Vector3(0f, sy, 0f), 0.11f, 0.105f, 14, T(0.5f), true, false, false);
            k.Disc(S.Metal, new Vector3(0f, sy - 0.006f, 0f), Vector3.up, 0.1f, 14, T(0.03f, 0.3f));

            // Iron door swung open to the right of the mouth, and the ash-pit door below.
            k.Matrix = saved * Matrix4x4.TRS(new Vector3(mw + 0.035f, 0f, zf + 0.016f), Quaternion.Euler(0f, 160f, 0f), Vector3.one);
            k.Span(S.Rust, new Vector3(-0.56f, my0 - 0.035f, 0f), new Vector3(-0.01f, my1 + 0.035f, 0.022f), Scale(iron, 0.85f), 0.006f);
            k.Span(S.Rust, new Vector3(-0.52f, my0, 0.022f), new Vector3(-0.05f, my1, 0.03f), Scale(iron, 0.7f), 0.004f);
            k.Cylinder(S.Metal, new Vector3(-0.47f, (my0 + my1) * 0.5f, 0.03f), new Vector3(-0.47f, (my0 + my1) * 0.5f, 0.09f), 0.009f, 0.009f, 6, T(0.4f));
            k.Cylinder(S.Metal, new Vector3(-0.47f, (my0 + my1) * 0.5f - 0.06f, 0.09f), new Vector3(-0.47f, (my0 + my1) * 0.5f + 0.06f, 0.09f), 0.011f, 0.011f, 6, T(0.4f));
            k.Matrix = saved;
            foreach (float y in new[] { my0 + 0.06f, my1 - 0.06f })
                k.Cylinder(S.Rust, new Vector3(mw + 0.035f, y - 0.04f, zf + 0.016f), new Vector3(mw + 0.035f, y + 0.04f, zf + 0.016f), 0.014f, 0.014f, 8, iron);
            k.Span(S.Rust, new Vector3(-0.16f, y0 + 0.015f, zf), new Vector3(0.16f, 0.285f, zf + 0.014f), Scale(iron, 0.85f), 0.004f);
            k.Cylinder(S.Metal, new Vector3(-0.05f, 0.25f, zf + 0.014f), new Vector3(0.05f, 0.25f, zf + 0.014f), 0.008f, 0.008f, 6, T(0.4f));
            if (!k.Detail) return;
            foreach (float y in new[] { 0.29f, 0.98f })
                foreach (float xs in new[] { -1f, 1f })
                    Bolt(k, new Vector3(xs * 0.45f, y + 0.0225f, bx + 0.012f), Vector3.forward, 0.01f);
            // Poker and shovel leaning on the right side.
            k.Cylinder(S.Metal, new Vector3(bx + 0.05f, y0, 0.32f), new Vector3(bx + 0.016f, y1 - 0.08f, 0.42f), 0.009f, 0.009f, 6, T(0.35f));
            k.Cylinder(S.TimberV, new Vector3(bx + 0.06f, y0, -0.1f), new Vector3(bx + 0.02f, y1 - 0.05f, -0.3f), 0.016f, 0.015f, 8, Board(k, 5, T(1.1f)));
            k.Box(S.Metal, new Vector3(bx + 0.065f, y0 + 0.11f, -0.07f), new Vector3(0.02f, 0.2f, 0.17f), T(0.45f), 0.005f, Quaternion.Euler(-12f, 0f, -3f));
            // A heap of coal on the plinth's front ledge.
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = new Vector3(-0.5f + Fixed(i + 400) * 0.3f, y0 + 0.02f, zf + 0.04f + Fixed(i + 410) * 0.04f);
                int seed = i;
                k.Sphere(S.Rubber, p, Vector3.one * (0.025f + Fixed(i + 420) * 0.02f), T(0.08f), 0, false, d => 1f + (MeshFactory.Hash(seed + 900, d) - 0.5f) * 0.5f);
            }
        }

        // ------------------------------------------------------------ workshop

        /// <summary>
        /// Steel machine bench with a metal lathe, a drill press, drawers and a pegboard of tools. The amber
        /// beacon on the pegboard is the "Indicator" that pulses while the workshop runs.
        /// </summary>
        public static void Workshop(StructureKit k, float sx, float sy, float sz, Color bodyColor)
        {
            float hx = sx * 0.5f;
            const float benchY = 0.86f, z0 = -0.5f, z1 = 0.4f;
            float top = benchY + 0.05f;
            Color frame = Paint(0.22f, 0.27f, 0.25f);
            Color body = Paint(bodyColor);
            Color machine = Paint(0.33f, 0.42f, 0.4f);
            foreach (float x in new[] { -hx + 0.05f, hx - 0.05f })
                foreach (float z in new[] { z0 + 0.05f, z1 - 0.05f })
                    k.Span(S.Paint, new Vector3(x - 0.03f, 0f, z - 0.03f), new Vector3(x + 0.03f, benchY, z + 0.03f), frame, 0.005f);
            foreach (float z in new[] { z0 + 0.05f, z1 - 0.05f })
            {
                k.Span(S.Paint, new Vector3(-hx + 0.05f, 0.1f, z - 0.02f), new Vector3(hx - 0.05f, 0.15f, z + 0.02f), frame, 0.004f);
                k.Span(S.Paint, new Vector3(-hx + 0.05f, benchY - 0.08f, z - 0.02f), new Vector3(hx - 0.05f, benchY, z + 0.02f), frame, 0.004f);
            }
            k.Span(S.Metal, new Vector3(-hx + 0.08f, 0.15f, z0 + 0.03f), new Vector3(hx - 0.08f, 0.17f, z1 - 0.03f), T(0.45f));
            k.Span(S.Metal, new Vector3(-hx, benchY, z0), new Vector3(hx, top, z1), T(0.6f), 0.008f);
            // Drawer cabinet under the right half.
            k.Span(S.Paint, new Vector3(0.3f, 0.17f, z0 + 0.06f), new Vector3(1.08f, benchY - 0.02f, z1 - 0.06f), body, 0.01f);
            float dh = (benchY - 0.2f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float y = 0.18f + i * dh;
                k.Span(S.Paint, new Vector3(0.32f, y + 0.008f, z1 - 0.06f), new Vector3(1.06f, y + dh - 0.008f, z1 - 0.045f), Scale(body, 1.08f), 0.006f);
                k.Span(S.Metal, new Vector3(0.62f, y + dh * 0.55f, z1 - 0.045f), new Vector3(0.76f, y + dh * 0.55f + 0.02f, z1 - 0.025f), T(0.75f), 0.004f);
            }

            // Pegboard on two posts behind the bench.
            Color board = Rel(new Color(0.52f, 0.4f, 0.28f), StructurePalette.PaintedWood);
            foreach (float x in new[] { -hx + 0.06f, hx - 0.06f })
                k.Span(S.Paint, new Vector3(x - 0.025f, 0f, -0.7f), new Vector3(x + 0.025f, sy - 0.04f, -0.66f), frame, 0.004f);
            k.Span(S.PaintedWood, new Vector3(-hx + 0.09f, top, -0.69f), new Vector3(hx - 0.09f, sy - 0.07f, -0.665f), board, 0.004f);

            // Metal lathe on the left: bed, headstock, chuck, carriage, tailstock and motor.
            float ly = top, lz = -0.12f;
            k.Span(S.Paint, new Vector3(-1.1f, ly, lz - 0.13f), new Vector3(-0.14f, ly + 0.09f, lz + 0.12f), machine, 0.01f);
            k.Span(S.Metal, new Vector3(-1.0f, ly + 0.09f, lz - 0.1f), new Vector3(-0.16f, ly + 0.11f, lz - 0.06f), T(0.85f));
            k.Span(S.Metal, new Vector3(-1.0f, ly + 0.09f, lz + 0.05f), new Vector3(-0.16f, ly + 0.11f, lz + 0.09f), T(0.85f));
            k.Span(S.Paint, new Vector3(-1.13f, ly, lz - 0.15f), new Vector3(-0.86f, ly + 0.32f, lz + 0.14f), machine, 0.025f);
            float cy = ly + 0.22f;
            k.Cylinder(S.Metal, new Vector3(-0.86f, cy, lz), new Vector3(-0.79f, cy, lz), 0.08f, 0.08f, 16, T(0.75f));
            k.Cylinder(S.Metal, new Vector3(-0.79f, cy, lz), new Vector3(-0.42f, cy, lz), 0.022f, 0.022f, 10, T(0.9f));
            k.Span(S.Paint, new Vector3(-0.62f, ly + 0.11f, lz - 0.14f), new Vector3(-0.46f, ly + 0.17f, lz + 0.15f), machine, 0.008f);
            k.Span(S.Metal, new Vector3(-0.57f, ly + 0.17f, lz - 0.05f), new Vector3(-0.5f, ly + 0.21f, lz + 0.02f), T(0.6f), 0.004f);
            k.Span(S.Paint, new Vector3(-0.34f, ly + 0.11f, lz - 0.1f), new Vector3(-0.2f, ly + 0.28f, lz + 0.1f), machine, 0.015f);
            k.Cylinder(S.Metal, new Vector3(-0.34f, cy, lz), new Vector3(-0.42f, cy, lz), 0.02f, 0.012f, 8, T(0.85f));
            k.Ring(S.Metal, new Vector3(-0.17f, cy, lz), Vector3.right, 0.05f, 0.007f, 14, 5, T(0.7f));
            k.Ring(S.Metal, new Vector3(-0.54f, ly + 0.12f, lz + 0.19f), Vector3.forward, 0.05f, 0.007f, 14, 5, T(0.7f));
            k.Cylinder(S.Metal, new Vector3(-0.54f, ly + 0.12f, lz + 0.15f), new Vector3(-0.54f, ly + 0.12f, lz + 0.19f), 0.01f, 0.01f, 6, T(0.7f));
            k.Cylinder(S.Paint, new Vector3(-1.08f, ly + 0.11f, -0.4f), new Vector3(-0.8f, ly + 0.11f, -0.4f), 0.09f, 0.09f, 14, Paint(0.2f, 0.22f, 0.22f));
            k.Span(S.Paint, new Vector3(-1.12f, ly + 0.02f, -0.36f), new Vector3(-1.06f, ly + 0.3f, -0.18f), machine, 0.01f);

            // Drill press on the right.
            float dx = 0.75f, dz = -0.3f;
            k.Span(S.Paint, new Vector3(dx - 0.2f, top, dz - 0.2f), new Vector3(dx + 0.2f, top + 0.04f, dz + 0.18f), machine, 0.01f);
            k.Cylinder(S.Metal, new Vector3(dx, top + 0.04f, dz - 0.13f), new Vector3(dx, sy - 0.12f, dz - 0.13f), 0.035f, 0.035f, 12, T(0.85f));
            k.Cylinder(S.Paint, new Vector3(dx, top + 0.3f, dz + 0.02f), new Vector3(dx, top + 0.33f, dz + 0.02f), 0.13f, 0.13f, 18, machine);
            k.Span(S.Paint, new Vector3(dx - 0.05f, top + 0.29f, dz - 0.15f), new Vector3(dx + 0.05f, top + 0.33f, dz - 0.05f), machine);
            k.Span(S.Paint, new Vector3(dx - 0.13f, sy - 0.42f, dz - 0.2f), new Vector3(dx + 0.13f, sy - 0.2f, dz + 0.13f), machine, 0.03f);
            k.Span(S.Paint, new Vector3(dx - 0.15f, sy - 0.2f, dz - 0.3f), new Vector3(dx + 0.15f, sy - 0.12f, dz + 0.14f), Scale(machine, 0.85f), 0.03f);
            k.Cylinder(S.Paint, new Vector3(dx, sy - 0.33f, dz - 0.2f), new Vector3(dx, sy - 0.33f, dz - 0.33f), 0.085f, 0.085f, 14, Paint(0.2f, 0.22f, 0.22f));
            k.Cylinder(S.Metal, new Vector3(dx, sy - 0.42f, dz + 0.04f), new Vector3(dx, sy - 0.52f, dz + 0.04f), 0.03f, 0.026f, 10, T(0.85f));
            k.Cylinder(S.Metal, new Vector3(dx, sy - 0.52f, dz + 0.04f), new Vector3(dx, sy - 0.58f, dz + 0.04f), 0.026f, 0.012f, 10, T(0.6f));
            k.Cylinder(S.Metal, new Vector3(dx, sy - 0.58f, dz + 0.04f), new Vector3(dx, sy - 0.66f, dz + 0.04f), 0.005f, 0.004f, 6, T(0.9f));
            Vector3 hub = new Vector3(dx + 0.14f, sy - 0.32f, dz);
            k.Cylinder(S.Metal, hub - Vector3.right * 0.01f, hub + Vector3.right * 0.03f, 0.025f, 0.025f, 10, T(0.7f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.4f;
                Vector3 end = hub + new Vector3(0.03f, Mathf.Cos(a) * 0.14f, Mathf.Sin(a) * 0.14f);
                k.Cylinder(S.Metal, hub + Vector3.right * 0.02f, end, 0.006f, 0.006f, 6, T(0.75f));
                k.Sphere(S.Rubber, end, Vector3.one * 0.016f, T(0.05f));
            }

            // Status beacon on the pegboard (StructureView pulses it while working).
            Vector3 beacon = new Vector3(hx - 0.22f, sy - 0.07f, -0.64f);
            k.Cylinder(S.Paint, beacon - Vector3.up * 0.02f, beacon + Vector3.up * 0.012f, 0.04f, 0.04f, 12, Paint(0.15f, 0.15f, 0.15f));
            k.Group("Indicator", beacon + Vector3.up * 0.012f);
            k.Sphere(S.Glow, beacon + Vector3.up * 0.03f, new Vector3(0.032f, 0.042f, 0.032f), T(1f), 2);
            k.EndGroup();
            if (!k.Detail) return;

            // Pegboard holes.
            Color hole = Scale(board, 0.25f);
            for (float y = top + 0.06f; y < sy - 0.1f; y += 0.05f)
                for (float x = -hx + 0.14f; x < hx - 0.12f; x += 0.05f)
                    k.Face(S.PaintedWood, hole, new Vector3(x, y, -0.7f), new Vector3(x - 0.005f, y - 0.005f, -0.6648f), new Vector3(x + 0.005f, y - 0.005f, -0.6648f), new Vector3(x + 0.005f, y + 0.005f, -0.6648f), new Vector3(x - 0.005f, y + 0.005f, -0.6648f));
            // Lower shelf: a toolbox, bar stock and a bucket of offcuts.
            k.Span(S.Paint, new Vector3(-0.95f, 0.17f, -0.25f), new Vector3(-0.5f, 0.38f, 0.0f), Paint(0.62f, 0.14f, 0.1f), 0.012f);
            k.Cylinder(S.Rubber, new Vector3(-0.85f, 0.41f, -0.125f), new Vector3(-0.6f, 0.41f, -0.125f), 0.014f, 0.014f, 8, T(0.05f));
            foreach (float x in new[] { -0.85f, -0.6f })
                k.Span(S.Metal, new Vector3(x - 0.01f, 0.38f, -0.135f), new Vector3(x + 0.01f, 0.41f, -0.115f), T(0.6f));
            for (int i = 0; i < 5; i++)
                k.Cylinder(S.Metal, new Vector3(-0.35f, 0.185f + (i / 3) * 0.03f, -0.3f + (i % 3) * 0.032f + (i / 3) * 0.016f), new Vector3(0.25f, 0.185f + (i / 3) * 0.03f, -0.3f + (i % 3) * 0.032f + (i / 3) * 0.016f), 0.015f, 0.015f, 8, T(0.55f + i * 0.06f));

            // Tools on the pegboard.
            for (int i = 0; i < 6; i++)
            {
                float x = -0.95f + i * 0.07f, len = 0.14f + i * 0.025f;
                k.Span(S.Metal, new Vector3(x - 0.011f, sy - 0.2f - len, -0.665f), new Vector3(x + 0.011f, sy - 0.2f, -0.655f), T(0.75f));
                k.Ring(S.Metal, new Vector3(x, sy - 0.2f + 0.012f, -0.66f), Vector3.forward, 0.016f, 0.006f, 10, 4, T(0.75f));
            }
            k.Span(S.TimberV, new Vector3(-0.42f, top + 0.18f, -0.665f), new Vector3(-0.395f, top + 0.5f, -0.64f), Board(k, 3, T(1.2f)));
            k.Span(S.Metal, new Vector3(-0.47f, top + 0.5f, -0.67f), new Vector3(-0.345f, top + 0.54f, -0.635f), T(0.45f), 0.004f);
            k.Ring(S.Rubber, new Vector3(-0.15f, sy - 0.3f, -0.64f), Vector3.forward, 0.09f, 0.012f, 18, 6, T(0.04f));
            k.Ring(S.Rubber, new Vector3(-0.15f, sy - 0.3f, -0.635f), Vector3.forward, 0.07f, 0.012f, 18, 6, T(0.04f));
            // Bench vice, an oil can and offcuts of bar stock.
            k.Span(S.Paint, new Vector3(0.05f, top, 0.18f), new Vector3(0.25f, top + 0.1f, 0.3f), Paint(0.2f, 0.3f, 0.45f), 0.01f);
            k.Span(S.Paint, new Vector3(0.05f, top + 0.01f, 0.33f), new Vector3(0.25f, top + 0.1f, 0.38f), Paint(0.2f, 0.3f, 0.45f), 0.01f);
            k.Cylinder(S.Metal, new Vector3(0.15f, top + 0.05f, 0.25f), new Vector3(0.15f, top + 0.05f, 0.42f), 0.011f, 0.011f, 8, T(0.8f));
            k.Lathe(S.Paint, new Vector3(-0.02f, top, -0.3f), new[] { new Vector2(0.05f, 0f), new Vector2(0.05f, 0.08f), new Vector2(0.03f, 0.11f), new Vector2(0.008f, 0.17f) }, 12, Paint(0.75f, 0.55f, 0.15f));
            for (int i = 0; i < 4; i++)
                k.Cylinder(S.Metal, new Vector3(-0.05f + i * 0.04f, top + 0.012f, 0.05f), new Vector3(0.12f + i * 0.04f, top + 0.012f, 0.12f), 0.012f, 0.012f, 8, T(0.7f + i * 0.05f));
            // Power cable to the floor.
            var cable = new[] { new Vector3(hx - 0.12f, top - 0.1f, z0 + 0.1f), new Vector3(hx - 0.04f, top - 0.2f, z0 + 0.1f), new Vector3(hx - 0.02f, 0.02f, z0 + 0.12f), new Vector3(hx - 0.2f, 0.015f, z0 + 0.3f) };
            k.Tube(S.Rubber, MeshFactory.RoundCorners(cable, 0.12f, 4), 0.012f, 6, T(0.04f));
        }

        // ------------------------------------------------------------ water pump

        /// <summary>
        /// Electric water pump on a concrete pad: a riveted pressure tank, a motor-driven centrifugal pump
        /// with its suction from below, a gate valve and check valve, a junction box on a post and a
        /// gooseneck spout over a bucket.
        /// </summary>
        public static void Pump(StructureKit k, float sx, float sy, float sz, Color tankColor)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            k.Span(S.Concrete, new Vector3(-hx, -0.12f, -hz), new Vector3(hx, 0.14f, hz), T(0.95f), 0.03f);
            const float pad = 0.14f;
            Color tank = Paint(tankColor);
            Color pipe = T(0.75f);
            Color dark = Paint(0.18f, 0.19f, 0.19f);

            // Pressure tank on three legs.
            Vector3 tc = new Vector3(-0.3f, 0f, -0.22f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.5f;
                Vector3 foot = tc + new Vector3(Mathf.Cos(a) * 0.27f, pad, Mathf.Sin(a) * 0.27f);
                k.Cylinder(S.Paint, foot, foot + new Vector3(-Mathf.Cos(a) * 0.04f, 0.26f, -Mathf.Sin(a) * 0.04f), 0.02f, 0.02f, 6, dark);
                k.Cylinder(S.Paint, foot, foot + Vector3.up * 0.01f, 0.04f, 0.04f, 8, dark);
            }
            k.Lathe(S.Paint, tc, new[]
            {
                new Vector2(0f, 0.3f), new Vector2(0.15f, 0.32f), new Vector2(0.25f, 0.37f), new Vector2(0.295f, 0.44f), new Vector2(0.3f, 0.5f),
                new Vector2(0.3f, 1.48f), new Vector2(0.295f, 1.54f), new Vector2(0.25f, 1.61f), new Vector2(0.15f, 1.66f), new Vector2(0f, 1.68f)
            }, 22, tank);
            foreach (float y in new[] { 0.5f, 1.0f, 1.48f })
                k.Ring(S.Paint, tc + Vector3.up * y, Vector3.up, 0.301f, 0.006f, 28, 4, Scale(tank, 0.8f));

            // Pump and motor on a base frame, suction from below.
            float py = 0.36f, pz = 0.33f;
            k.Span(S.Paint, new Vector3(-0.08f, pad, pz - 0.18f), new Vector3(0.66f, pad + 0.06f, pz + 0.18f), dark, 0.005f);
            Color motor = Paint(0.24f, 0.38f, 0.3f);
            k.Cylinder(S.Paint, new Vector3(0.26f, py, pz), new Vector3(0.6f, py, pz), 0.135f, 0.135f, 18, motor);
            k.Lathe(S.Paint, new Vector3(0.6f, py, pz), new[] { new Vector2(0.13f, 0f), new Vector2(0.12f, 0.04f), new Vector2(0.06f, 0.07f), new Vector2(0f, 0.075f) }, 18, Scale(motor, 0.8f), true, Quaternion.Euler(0f, 0f, -90f));
            k.Span(S.Paint, new Vector3(0.3f, pad + 0.06f, pz - 0.12f), new Vector3(0.56f, py - 0.08f, pz + 0.12f), motor, 0.01f);
            k.Span(S.Paint, new Vector3(0.36f, py + 0.12f, pz - 0.07f), new Vector3(0.5f, py + 0.2f, pz + 0.07f), motor, 0.01f);
            k.Cylinder(S.Metal, new Vector3(0.18f, py, pz), new Vector3(0.26f, py, pz), 0.055f, 0.055f, 12, T(0.6f));
            k.Cylinder(S.Paint, new Vector3(0.0f, py, pz), new Vector3(0.18f, py, pz), 0.15f, 0.15f, 18, tank);
            k.Cylinder(S.Paint, new Vector3(-0.04f, py, pz), new Vector3(0.0f, py, pz), 0.07f, 0.07f, 14, Scale(tank, 0.85f));
            k.Span(S.Paint, new Vector3(0.0f, pad + 0.06f, pz - 0.1f), new Vector3(0.18f, py - 0.1f, pz + 0.1f), tank, 0.01f);
            // Suction: out of the pump's eye and down into the pad.
            k.Tube(S.Metal, MeshFactory.RoundCorners(new[] { new Vector3(-0.04f, py, pz), new Vector3(-0.15f, py, pz), new Vector3(-0.15f, pad - 0.05f, pz) }, 0.07f, 5), 0.04f, 12, pipe);
            k.Cylinder(S.Metal, new Vector3(-0.15f, pad, pz), new Vector3(-0.15f, pad + 0.02f, pz), 0.075f, 0.075f, 14, Scale(pipe, 0.8f));
            // Discharge: up from the volute, gate valve, check valve, into the tank.
            float ry = 0.86f;
            var discharge = new[] { new Vector3(0.09f, py + 0.13f, pz), new Vector3(0.09f, ry, pz), new Vector3(tc.x, ry, pz), new Vector3(tc.x, ry, tc.z + 0.28f) };
            k.Tube(S.Metal, MeshFactory.RoundCorners(discharge, 0.08f, 5), 0.032f, 12, pipe);
            k.Cylinder(S.Metal, new Vector3(0.09f, 0.6f, pz), new Vector3(0.09f, 0.7f, pz), 0.05f, 0.05f, 12, Scale(pipe, 0.7f));
            k.Cylinder(S.Metal, new Vector3(0.09f, 0.7f, pz), new Vector3(0.09f, 0.78f, pz), 0.012f, 0.012f, 6, pipe);
            k.Ring(S.Paint, new Vector3(0.09f, 0.78f, pz), Vector3.up, 0.06f, 0.008f, 14, 5, Paint(0.7f, 0.12f, 0.1f));
            k.Cylinder(S.Metal, new Vector3(-0.12f, ry, pz), new Vector3(-0.02f, ry, pz), 0.048f, 0.048f, 12, Scale(pipe, 0.7f));
            // Riser from the tank top to a gooseneck spout over a bucket, with a gauge.
            var riser = new[] { new Vector3(tc.x, 1.66f, tc.z), new Vector3(tc.x, sy - 0.07f, tc.z), new Vector3(tc.x, sy - 0.07f, 0.45f), new Vector3(tc.x, 1.66f, 0.45f) };
            k.Tube(S.Metal, MeshFactory.RoundCorners(riser, 0.1f, 6), 0.028f, 12, pipe);
            k.Cylinder(S.Metal, new Vector3(tc.x, 1.72f, 0.45f), new Vector3(tc.x, 1.6f, 0.45f), 0.035f, 0.03f, 10, Scale(pipe, 0.8f));
            k.Cylinder(S.Paint, new Vector3(tc.x - 0.03f, 1.76f, 0.45f), new Vector3(tc.x - 0.1f, 1.76f, 0.45f), 0.01f, 0.01f, 6, Paint(0.7f, 0.12f, 0.1f));
            Vector3 gauge = new Vector3(tc.x + 0.12f, 1.7f, tc.z + 0.1f);
            k.Cylinder(S.Metal, gauge - Vector3.up * 0.06f, gauge, 0.008f, 0.008f, 6, pipe);
            k.Cylinder(S.Metal, gauge + new Vector3(0f, 0.04f, -0.015f), gauge + new Vector3(0f, 0.04f, 0.015f), 0.045f, 0.045f, 16, T(0.6f));
            k.Disc(S.Plain, gauge + new Vector3(0f, 0.04f, 0.0155f), Vector3.forward, 0.038f, 16, T(1f));
            k.Lathe(S.Metal, new Vector3(tc.x, pad, 0.45f), new[] { new Vector2(0.1f, 0f), new Vector2(0.125f, 0.22f), new Vector2(0.118f, 0.22f), new Vector2(0.095f, 0.02f) }, 16, T(0.7f));
            k.Disc(S.Glass, new Vector3(tc.x, pad + 0.16f, 0.45f), Vector3.up, 0.11f, 16, Rel(new Color(0.25f, 0.32f, 0.3f), StructurePalette.Glass));
            // Wet concrete round the bucket.
            k.Disc(S.Concrete, new Vector3(tc.x + 0.04f, pad + 0.002f, 0.42f), Vector3.up, 0.22f, 18, T(0.55f));

            // Junction box on a post, conduit to the motor.
            k.Cylinder(S.Metal, new Vector3(0.58f, pad, -0.52f), new Vector3(0.58f, 1.32f, -0.52f), 0.025f, 0.025f, 8, Scale(pipe, 0.8f));
            k.Span(S.Paint, new Vector3(0.47f, 1.0f, -0.495f), new Vector3(0.69f, 1.3f, -0.4f), Paint(0.62f, 0.63f, 0.6f), 0.012f);
            k.Span(S.Paint, new Vector3(0.53f, 1.06f, -0.4f), new Vector3(0.63f, 1.12f, -0.385f), Paint(0.15f, 0.15f, 0.15f), 0.004f);
            k.Sphere(S.Glow, new Vector3(0.58f, 1.2f, -0.39f), Vector3.one * 0.016f, T(1f), 1);
            var conduit = new[] { new Vector3(0.55f, 1.0f, -0.45f), new Vector3(0.55f, pad + 0.02f, -0.45f), new Vector3(0.43f, pad + 0.02f, -0.45f), new Vector3(0.43f, pad + 0.02f, pz - 0.2f), new Vector3(0.43f, py + 0.12f, pz - 0.05f) };
            k.Tube(S.Rubber, MeshFactory.RoundCorners(conduit, 0.06f, 4), 0.014f, 6, T(0.05f));
            if (!k.Detail) return;
            for (int i = 0; i < 7; i++)
                k.Ring(S.Paint, new Vector3(0.3f + i * 0.045f, py, pz), Vector3.right, 0.138f, 0.007f, 18, 4, Scale(motor, 0.9f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                Bolt(k, new Vector3(-0.15f + Mathf.Cos(a) * 0.06f, pad + 0.02f, pz + Mathf.Sin(a) * 0.06f), Vector3.up, 0.008f);
            }
            k.Span(S.Plain, new Vector3(tc.x - 0.08f, 0.9f, tc.z + 0.295f), new Vector3(tc.x + 0.08f, 1.0f, tc.z + 0.305f), Rel(new Color(0.8f, 0.78f, 0.7f), StructurePalette.Plain));
        }

        // ------------------------------------------------------------ generator

        /// <summary>
        /// Diesel generating set on its base fuel tank: engine block, radiator, alternator, air filter,
        /// muffler and exhaust stack, with the control panel on the alternator. Everything on the rubber
        /// mounts is the "Engine" part, which StructureView shakes while it runs.
        /// </summary>
        public static void Generator(StructureKit k, float sx, float sy, float sz, Color paint)
        {
            float hx = sx * 0.5f;
            Color red = Paint(paint);
            Color dark = Paint(0.16f, 0.17f, 0.17f);
            Color black = T(0.04f);
            // Skid and base tank.
            foreach (float z in new[] { -0.41f, 0.41f })
                k.Span(S.Paint, new Vector3(-hx, 0f, z - 0.045f), new Vector3(hx, 0.09f, z + 0.045f), dark, 0.006f);
            k.Span(S.Paint, new Vector3(-hx + 0.05f, 0.06f, -0.45f), new Vector3(hx - 0.05f, 0.24f, 0.45f), Scale(dark, 1.3f), 0.02f);
            k.Cylinder(S.Paint, new Vector3(0.66f, 0.24f, 0.3f), new Vector3(0.66f, 0.29f, 0.3f), 0.045f, 0.045f, 12, Paint(0.8f, 0.62f, 0.1f));
            k.Disc(S.Plain, new Vector3(0.4f, 0.16f, 0.451f), Vector3.forward, 0.035f, 14, T(1f));
            foreach (float x in new[] { -0.5f, 0.45f })
                foreach (float z in new[] { -0.25f, 0.25f })
                    k.Cylinder(S.Rubber, new Vector3(x, 0.24f, z), new Vector3(x, 0.29f, z), 0.04f, 0.035f, 10, black);

            k.Group("Engine", Vector3.zero);
            k.Span(S.Paint, new Vector3(-0.55f, 0.27f, -0.27f), new Vector3(0.12f, 0.74f, 0.27f), red, 0.03f);
            k.Span(S.Paint, new Vector3(-0.52f, 0.74f, -0.22f), new Vector3(0.08f, 0.84f, 0.22f), Scale(red, 0.85f), 0.02f);
            k.Span(S.Paint, new Vector3(-0.5f, 0.84f, -0.17f), new Vector3(0.06f, 0.91f, 0.17f), dark, 0.025f);
            // Radiator and fan shroud at the cold end.
            k.Span(S.Paint, new Vector3(-0.86f, 0.29f, -0.38f), new Vector3(-0.66f, 0.98f, 0.38f), dark, 0.015f);
            k.Cylinder(S.Paint, new Vector3(-0.66f, 0.6f, 0f), new Vector3(-0.55f, 0.6f, 0f), 0.27f, 0.22f, 18, dark);
            k.Cylinder(S.Metal, new Vector3(-0.72f, 0.98f, 0.2f), new Vector3(-0.72f, 1.02f, 0.2f), 0.035f, 0.035f, 10, T(0.7f));
            // Alternator with its end bell.
            k.Cylinder(S.Paint, new Vector3(0.12f, 0.52f, 0f), new Vector3(0.62f, 0.52f, 0f), 0.25f, 0.25f, 22, red);
            k.Lathe(S.Paint, new Vector3(0.62f, 0.52f, 0f), new[] { new Vector2(0.25f, 0f), new Vector2(0.24f, 0.04f), new Vector2(0.18f, 0.08f), new Vector2(0f, 0.1f) }, 22, Scale(red, 0.85f), true, Quaternion.Euler(0f, 0f, -90f));
            k.Span(S.Paint, new Vector3(0.2f, 0.72f, -0.18f), new Vector3(0.56f, 0.84f, 0.13f), dark, 0.01f);
            // Control panel facing the front.
            k.Span(S.Paint, new Vector3(0.18f, 0.84f, -0.03f), new Vector3(0.6f, sy - 0.08f, 0.1f), dark, 0.012f);
            float panelY = (0.84f + sy - 0.08f) * 0.5f;
            foreach (float x in new[] { 0.27f, 0.39f })
            {
                k.Cylinder(S.Metal, new Vector3(x, panelY + 0.03f, 0.1f), new Vector3(x, panelY + 0.03f, 0.112f), 0.04f, 0.04f, 16, T(0.6f));
                k.Disc(S.Plain, new Vector3(x, panelY + 0.03f, 0.1125f), Vector3.forward, 0.034f, 16, T(1f));
                k.Span(S.Paint, new Vector3(x - 0.002f, panelY + 0.03f, 0.113f), new Vector3(x + 0.022f, panelY + 0.034f, 0.115f), Paint(0.7f, 0.1f, 0.08f));
            }
            k.Span(S.Rubber, new Vector3(0.47f, panelY - 0.02f, 0.1f), new Vector3(0.55f, panelY + 0.08f, 0.13f), black, 0.006f);
            k.Sphere(S.Glow, new Vector3(0.27f, panelY - 0.07f, 0.105f), Vector3.one * 0.014f, T(1f), 1);
            // Air filter on the back, muffler on top and the exhaust stack.
            k.Cylinder(S.Paint, new Vector3(-0.42f, 0.86f, -0.24f), new Vector3(-0.12f, 0.86f, -0.24f), 0.075f, 0.075f, 14, black);
            Color rust = T(0.85f);
            k.Tube(S.Rust, MeshFactory.RoundCorners(new[] { new Vector3(-0.38f, 0.76f, 0.27f), new Vector3(-0.38f, 0.76f, 0.33f), new Vector3(-0.38f, 0.97f, 0.33f), new Vector3(-0.38f, 0.97f, 0.12f) }, 0.05f, 4), 0.03f, 10, rust);
            k.Cylinder(S.Rust, new Vector3(-0.46f, 0.99f, 0.1f), new Vector3(0.02f, 0.99f, 0.1f), 0.085f, 0.085f, 16, rust);
            Vector3 stackTop = new Vector3(0.04f, sy - 0.03f, 0.1f);
            k.Cylinder(S.Rust, new Vector3(0.0f, 0.99f, 0.1f), stackTop, 0.032f, 0.03f, 10, Scale(rust, 0.7f));
            k.Disc(S.Rust, stackTop, Vector3.up, 0.03f, 10, T(0.05f));
            if (k.Detail)
            {
                // Rain flap propped half open on the stack, a data plate and a warning sticker.
                k.Box(S.Rust, stackTop + new Vector3(-0.012f, 0.02f, 0f), new Vector3(0.07f, 0.004f, 0.07f), Scale(rust, 0.8f), 0f, Quaternion.Euler(0f, 0f, 35f));
                k.Span(S.Metal, new Vector3(-0.35f, 0.5f, 0.27f), new Vector3(-0.2f, 0.58f, 0.275f), T(0.8f));
                k.Span(S.Paint, new Vector3(0.24f, sy - 0.15f, 0.1f), new Vector3(0.34f, sy - 0.1f, 0.103f), Paint(0.9f, 0.72f, 0.12f));
                k.Span(S.Paint, new Vector3(0.27f, sy - 0.14f, 0.103f), new Vector3(0.31f, sy - 0.11f, 0.104f), Paint(0.1f, 0.1f, 0.1f));
                // Starter battery on the base frame.
                k.Span(S.Paint, new Vector3(0.66f, 0.24f, -0.38f), new Vector3(0.84f, 0.42f, -0.24f), Paint(0.12f, 0.12f, 0.13f), 0.01f);
                k.Cylinder(S.Rubber, new Vector3(0.7f, 0.42f, -0.31f), new Vector3(0.7f, 0.445f, -0.31f), 0.014f, 0.012f, 8, Rel(new Color(0.6f, 0.08f, 0.06f), StructurePalette.Rubber));
                k.Tube(S.Rubber, MeshFactory.RoundCorners(new[] { new Vector3(0.7f, 0.445f, -0.31f), new Vector3(0.7f, 0.5f, -0.31f), new Vector3(0.5f, 0.5f, -0.28f) }, 0.04f, 3), 0.009f, 6, Rel(new Color(0.6f, 0.08f, 0.06f), StructurePalette.Rubber));
                for (int i = 0; i < 3; i++)
                    k.Span(S.Paint, new Vector3(-0.47f + i * 0.2f, 0.91f, -0.14f), new Vector3(-0.35f + i * 0.2f, 0.93f, 0.14f), dark, 0.006f);
                for (int i = 0; i < 16; i++)
                    k.Span(S.Metal, new Vector3(-0.875f, 0.33f + i * 0.039f, -0.33f), new Vector3(-0.86f, 0.345f + i * 0.039f, 0.33f), T(0.35f));
                for (int i = 0; i < 4; i++)
                    k.Ring(S.Paint, new Vector3(0.18f + i * 0.12f, 0.52f, 0f), Vector3.right, 0.252f, 0.006f, 24, 4, dark);
                k.Cylinder(S.Paint, new Vector3(-0.3f, 0.45f, 0.27f), new Vector3(-0.3f, 0.45f, 0.36f), 0.045f, 0.045f, 12, Paint(0.85f, 0.85f, 0.82f));
                k.Tube(S.Rubber, MeshFactory.RoundCorners(new[] { new Vector3(0.55f, 0.24f, 0.36f), new Vector3(0.55f, 0.3f, 0.36f), new Vector3(0.05f, 0.4f, 0.32f), new Vector3(-0.1f, 0.5f, 0.27f) }, 0.08f, 4), 0.009f, 6, black);
            }
            k.EndGroup();
        }

        // ------------------------------------------------------------ power pole

        /// <summary>Tapered wooden pole with a creosoted butt, a braced crossarm, glass insulators, a ground wire and steps.</summary>
        public static void Pole(StructureKit k, float sy, Color tone)
        {
            Color wood = Tint(tone, 0.82f, 0.82f, 0.86f);
            const float r0 = 0.13f, r1 = 0.1f;
            float R(float y) => Mathf.Lerp(r0, r1, (y + 0.4f) / (sy + 0.4f));
            k.Cylinder(S.TimberV, new Vector3(0f, 0.55f, 0f), new Vector3(0f, sy - 0.03f, 0f), R(0.55f), r1, 14, wood, true, false, true);
            k.Cylinder(S.TimberV, new Vector3(0f, -0.4f, 0f), new Vector3(0f, 0.55f, 0f), r0 + 0.002f, R(0.55f) + 0.002f, 14, Tint(wood, 0.36f, 0.32f, 0.3f), true, false, false);
            k.Cylinder(S.Metal, new Vector3(0f, sy - 0.03f, 0f), new Vector3(0f, sy, 0f), r1 + 0.008f, r1 * 0.5f, 14, T(0.5f), true, false, true);
            float arm = sy - 0.37f;
            k.Span(S.Timber, new Vector3(-0.62f, arm - 0.05f, R(arm) - 0.005f), new Vector3(0.62f, arm + 0.05f, R(arm) + 0.095f), Board(k, 1, Scale(wood, 1.1f)), 0.01f);
            foreach (float xs in new[] { -1f, 1f })
                k.Beam(S.Metal, new Vector3(xs * 0.4f, arm - 0.05f, R(arm) + 0.045f), new Vector3(0f, arm - 0.5f, R(arm - 0.5f) + 0.004f), Vector3.forward, 0.035f, 0.007f, T(0.45f));
            Bolt(k, new Vector3(0f, arm, R(arm) + 0.095f), Vector3.forward, 0.012f);
            Bolt(k, new Vector3(0f, arm - 0.5f, R(arm - 0.5f) + 0.008f), Vector3.forward, 0.01f);
            // Pin-type glass insulators.
            var profile = new[]
            {
                new Vector2(0.056f, 0f), new Vector2(0.052f, 0.018f), new Vector2(0.036f, 0.028f), new Vector2(0.042f, 0.055f),
                new Vector2(0.046f, 0.066f), new Vector2(0.03f, 0.088f), new Vector2(0.036f, 0.1f), new Vector2(0.028f, 0.12f), new Vector2(0f, 0.132f)
            };
            float az = R(arm) + 0.045f;
            foreach (float x in new[] { -0.5f, -0.22f, 0.5f })
            {
                k.Cylinder(S.Metal, new Vector3(x, arm + 0.05f, az), new Vector3(x, arm + 0.085f, az), 0.014f, 0.012f, 8, T(0.5f));
                k.Lathe(S.Glass, new Vector3(x, arm + 0.075f, az), profile, 14, T(1f));
            }
            if (!k.Detail) return;
            // Ground wire stapled down the back.
            var wire = new List<Vector3>();
            for (float y = arm - 0.1f; y > -0.05f; y -= 0.5f) wire.Add(new Vector3(0.02f, y, -R(y) - 0.006f));
            wire.Add(new Vector3(0.02f, -0.05f, -R(-0.05f) - 0.006f));
            k.Tube(S.Metal, wire, 0.005f, 5, Rel(new Color(0.7f, 0.42f, 0.28f), StructurePalette.Metal));
            // Steps and the pole tag.
            for (int i = 0; i < 6; i++)
            {
                float y = 1.9f + i * 0.4f, xs = i % 2 == 0 ? 1f : -1f;
                k.Cylinder(S.Metal, new Vector3(xs * (R(y) - 0.02f), y, 0f), new Vector3(xs * (R(y) + 0.17f), y + 0.01f, 0f), 0.011f, 0.011f, 6, T(0.45f));
                k.Cylinder(S.Metal, new Vector3(xs * (R(y) + 0.16f), y, 0f), new Vector3(xs * (R(y) + 0.16f), y + 0.05f, 0f), 0.01f, 0.01f, 6, T(0.45f));
            }
            k.Span(S.Paint, new Vector3(-0.045f, 1.6f, R(1.6f) - 0.003f), new Vector3(0.045f, 1.72f, R(1.6f) + 0.003f), Paint(0.85f, 0.7f, 0.15f));
            k.Span(S.Paint, new Vector3(-0.035f, 1.64f, R(1.6f) + 0.003f), new Vector3(0.035f, 1.68f, R(1.6f) + 0.004f), Paint(0.1f, 0.1f, 0.1f));
            // Tamped earth and stones at the foot.
            k.Shell(S.Soil, Vector3.zero, new[] { new Vector2(0.34f, -0.03f), new Vector2(0.25f, 0.03f), new Vector2(0.16f, 0.06f), new Vector2(r0, 0.065f) }, 16, T(0.9f));
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f + 0.4f, d = 0.2f + Fixed(i + 500) * 0.1f;
                int seed = i;
                k.Sphere(S.Stone, new Vector3(Mathf.Cos(a) * d, 0.02f, Mathf.Sin(a) * d), new Vector3(0.05f, 0.035f, 0.045f), T(0.85f), 0, false, v => 1f + (MeshFactory.Hash(seed + 520, v) - 0.5f) * 0.4f);
            }
        }

        // ------------------------------------------------------------ lamp

        /// <summary>Steel post on a concrete footing with a gooseneck arm and an enamel shade; the bulb is the "LampHead".</summary>
        public static void Lamp(StructureKit k, float sy)
        {
            Color post = Paint(0.16f, 0.24f, 0.2f);
            k.Span(S.Concrete, new Vector3(-0.2f, -0.15f, -0.2f), new Vector3(0.2f, 0.12f, 0.2f), T(0.95f), 0.025f);
            k.Span(S.Metal, new Vector3(-0.13f, 0.12f, -0.13f), new Vector3(0.13f, 0.135f, 0.13f), T(0.45f), 0.004f);
            k.Lathe(S.Paint, new Vector3(0f, 0.135f, 0f), new[] { new Vector2(0.075f, 0f), new Vector2(0.075f, 0.05f), new Vector2(0.06f, 0.09f), new Vector2(0.05f, 0.17f) }, 14, post);
            float topY = sy - 0.22f;
            k.Cylinder(S.Paint, new Vector3(0f, 0.3f, 0f), new Vector3(0f, topY, 0f), 0.048f, 0.04f, 14, post, true, false, false);
            k.Span(S.Paint, new Vector3(-0.06f, 0.95f, 0.035f), new Vector3(0.06f, 1.12f, 0.1f), Paint(0.5f, 0.52f, 0.5f), 0.008f);
            k.Span(S.Rubber, new Vector3(-0.012f, 1.02f, 0.1f), new Vector3(0.012f, 1.06f, 0.125f), T(0.05f), 0.003f);
            Vector3 head = new Vector3(0f, sy - 0.27f, 0.44f);
            var arm = new[] { new Vector3(0f, topY - 0.02f, 0f), new Vector3(0f, sy - 0.04f, 0f), new Vector3(0f, sy - 0.03f, 0.3f), head + Vector3.up * 0.13f };
            k.Tube(S.Paint, MeshFactory.RoundCorners(arm, 0.12f, 6), 0.024f, 10, post);
            // Enamel shade: dark green outside, white inside, rolled rim.
            k.Shell(S.Paint, head, new[] { new Vector2(0.19f, 0f), new Vector2(0.175f, 0.02f), new Vector2(0.1f, 0.095f), new Vector2(0.05f, 0.13f), new Vector2(0.035f, 0.15f) }, 22, post);
            k.Disc(S.Paint, head + Vector3.up * 0.15f, Vector3.up, 0.035f, 22, post);
            k.Shell(S.Paint, head, new[] { new Vector2(0.183f, 0.004f), new Vector2(0.168f, 0.022f), new Vector2(0.095f, 0.095f), new Vector2(0.046f, 0.126f) }, 22, Paint(0.86f, 0.85f, 0.8f), true);
            k.Disc(S.Paint, head + Vector3.up * 0.126f, Vector3.down, 0.046f, 22, Paint(0.86f, 0.85f, 0.8f));
            k.Ring(S.Paint, head + Vector3.up * 0.002f, Vector3.up, 0.187f, 0.006f, 26, 5, post);
            k.Cylinder(S.Metal, head + Vector3.up * 0.06f, head + Vector3.up * 0.126f, 0.028f, 0.028f, 10, T(0.6f));
            k.Group("LampHead", head + Vector3.up * 0.01f);
            k.Sphere(S.Plain, head + Vector3.up * 0.01f, new Vector3(0.05f, 0.065f, 0.05f), Rel(new Color(0.9f, 0.86f, 0.72f), StructurePalette.Plain), 2);
            k.EndGroup();
            if (!k.Detail) return;
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                    Bolt(k, new Vector3(xs * 0.1f, 0.135f, zs * 0.1f), Vector3.up, 0.011f);
            k.Tube(S.Rubber, new[] { new Vector3(0.025f, 1.12f, 0.06f), new Vector3(0.03f, 1.6f, 0.05f), new Vector3(0.03f, topY - 0.05f, 0.042f) }, 0.007f, 5, T(0.05f));
        }

        // ------------------------------------------------------------ battery bank

        /// <summary>Angle-iron rack of deep-cycle cells with interconnects and a charge controller on top.</summary>
        public static void Battery(StructureKit k, float sx, float sy, float sz, Color paint)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color rack = Paint(paint);
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                {
                    k.Span(S.Paint, new Vector3(xs * hx, 0f, zs * hz), new Vector3(xs * (hx - 0.045f), sy, zs * (hz - 0.006f)), rack, 0.002f);
                    k.Span(S.Paint, new Vector3(xs * hx, 0f, zs * (hz - 0.006f)), new Vector3(xs * (hx - 0.006f), sy, zs * (hz - 0.045f)), rack, 0.002f);
                }
            float[] shelves = { 0.04f, 0.48f, 0.9f };
            foreach (float y in shelves)
            {
                k.Span(S.Paint, new Vector3(-hx + 0.006f, y, -hz + 0.006f), new Vector3(hx - 0.006f, y + 0.025f, hz - 0.006f), Scale(rack, 0.9f), 0.003f);
                k.Span(S.Paint, new Vector3(-hx + 0.006f, y - 0.03f, hz - 0.03f), new Vector3(hx - 0.006f, y + 0.025f, hz - 0.006f), rack, 0.002f);
            }
            Color caseColor = Paint(0.13f, 0.13f, 0.14f), lid = Paint(0.55f, 0.57f, 0.6f);
            Color red = Rel(new Color(0.6f, 0.08f, 0.06f), StructurePalette.Rubber), black = T(0.04f);
            for (int level = 0; level < 2; level++)
            {
                float y0 = shelves[level] + 0.025f;
                for (int i = 0; i < 3; i++)
                {
                    float cx = (i - 1) * 0.37f;
                    k.Span(S.Paint, new Vector3(cx - 0.165f, y0, -0.29f), new Vector3(cx + 0.165f, y0 + 0.31f, 0.29f), caseColor, 0.012f);
                    k.Span(S.Paint, new Vector3(cx - 0.17f, y0 + 0.31f, -0.295f), new Vector3(cx + 0.17f, y0 + 0.345f, 0.295f), lid, 0.01f);
                    k.Span(S.Paint, new Vector3(cx - 0.12f, y0 + 0.1f, 0.29f), new Vector3(cx + 0.12f, y0 + 0.22f, 0.293f), Paint(0.85f, 0.7f, 0.18f));
                    k.Span(S.Paint, new Vector3(cx - 0.12f, y0 + 0.15f, 0.293f), new Vector3(cx + 0.12f, y0 + 0.17f, 0.295f), Paint(0.1f, 0.1f, 0.1f));
                    float ty = y0 + 0.345f;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 t = new Vector3(cx + side * 0.11f, ty, 0.2f);
                        k.Cylinder(S.Metal, t, t + Vector3.up * 0.03f, 0.016f, 0.014f, 8, T(0.55f));
                        k.Ring(S.Rubber, t + Vector3.up * 0.008f, Vector3.up, 0.022f, 0.008f, 10, 4, side > 0f ? red : black);
                    }
                    if (!k.Detail) continue;
                    for (int v = 0; v < 3; v++)
                        k.Cylinder(S.Rubber, new Vector3(cx + (v - 1) * 0.06f, ty, -0.05f), new Vector3(cx + (v - 1) * 0.06f, ty + 0.015f, -0.05f), 0.016f, 0.016f, 8, Scale(black, 3f));
                    if (i < 2)
                    {
                        Vector3 a = new Vector3(cx + 0.11f, ty + 0.03f, 0.2f), b = new Vector3(cx + 0.37f - 0.11f, ty + 0.03f, 0.2f);
                        k.Tube(S.Rubber, new[] { a, (a + b) * 0.5f + Vector3.up * 0.05f, b }, 0.011f, 6, i == 0 ? red : black);
                    }
                }
            }
            // Charge controller with a display, a lamp and the disconnect switch.
            float by = shelves[2] + 0.025f;
            k.Span(S.Paint, new Vector3(-0.38f, by, -0.28f), new Vector3(0.38f, sy - 0.02f, 0.1f), Paint(0.7f, 0.71f, 0.69f), 0.012f);
            k.Span(S.Glass, new Vector3(-0.3f, by + 0.13f, 0.1f), new Vector3(-0.08f, by + 0.21f, 0.104f), Rel(new Color(0.2f, 0.35f, 0.3f), StructurePalette.Glass));
            k.Sphere(S.Glow, new Vector3(0.0f, by + 0.17f, 0.104f), Vector3.one * 0.013f, T(1f), 1);
            k.Cylinder(S.Paint, new Vector3(0.2f, by + 0.14f, 0.1f), new Vector3(0.2f, by + 0.14f, 0.13f), 0.045f, 0.045f, 16, Paint(0.85f, 0.7f, 0.15f));
            k.Span(S.Paint, new Vector3(0.185f, by + 0.09f, 0.13f), new Vector3(0.215f, by + 0.19f, 0.15f), Paint(0.7f, 0.1f, 0.08f), 0.006f);
            if (!k.Detail) return;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side > 0f ? 0.37f + 0.11f : -0.37f - 0.11f;
                var path = new[] { new Vector3(x, shelves[1] + 0.025f + 0.375f, 0.2f), new Vector3(x, by - 0.02f, 0.25f), new Vector3(side * 0.25f, by + 0.04f, 0.12f) };
                k.Tube(S.Rubber, MeshFactory.RoundCorners(path, 0.08f, 4), 0.012f, 6, side > 0f ? red : black);
            }
        }

        // ------------------------------------------------------------ world containers

        /// <summary>Kitchen base cabinet: painted doors with raised panels (one ajar), drawers, a butcher-block top and a kettle.</summary>
        public static void Cabinet(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color body = Rel(color, StructurePalette.PaintedWood);
            k.Span(S.PaintedWood, new Vector3(-hx + 0.02f, 0f, -hz + 0.02f), new Vector3(hx - 0.02f, 0.09f, hz - 0.07f), Scale(body, 0.35f));
            k.Span(S.PaintedWood, new Vector3(-hx, 0.09f, -hz), new Vector3(hx, sy - 0.04f, hz - 0.022f), body, 0.005f);
            k.Span(S.Timber, new Vector3(-hx, sy - 0.04f, -hz), new Vector3(hx, sy, hz), Board(k, 1, WoodTone(new Color(0.62f, 0.46f, 0.3f))), 0.008f);
            float dy0 = sy - 0.2f, dy1 = sy - 0.055f;
            foreach (float side in new[] { -1f, 1f })
            {
                k.Span(S.PaintedWood, new Vector3(side * 0.005f, dy0, hz - 0.022f), new Vector3(side * (hx - 0.012f), dy1, hz - 0.002f), body, 0.006f);
                k.Span(S.Metal, new Vector3(side * hx * 0.5f - 0.05f, (dy0 + dy1) * 0.5f - 0.008f, hz - 0.002f), new Vector3(side * hx * 0.5f + 0.05f, (dy0 + dy1) * 0.5f + 0.008f, hz + 0.014f), T(0.7f), 0.003f);
            }
            float y0 = 0.11f, y1 = dy0 - 0.015f;
            // Dark interior behind the open right door.
            k.Span(S.PaintedWood, new Vector3(0.02f, y0 + 0.01f, hz - 0.03f), new Vector3(hx - 0.025f, y1 - 0.01f, hz - 0.021f), T(0.04f, 0.4f));
            // Hinged at the outer edges; the right one hangs open on a looted, dark interior.
            CabinetDoor(k, new Vector3(-hx + 0.012f, 0f, hz - 0.022f), 1f, hx - 0.017f, y0, y1, 0f, body);
            CabinetDoor(k, new Vector3(hx - 0.012f, 0f, hz - 0.022f), -1f, hx - 0.017f, y0, y1, k.Detail ? 30f + k.Rand(3) * 40f : 0f, body);
            if (!k.Detail) return;
            Color enamel = Paint(0.62f, 0.2f, 0.15f);
            k.Lathe(S.Paint, new Vector3(-0.22f, sy, -0.05f), new[] { new Vector2(0.08f, 0f), new Vector2(0.09f, 0.06f), new Vector2(0.085f, 0.12f), new Vector2(0.05f, 0.16f), new Vector2(0.02f, 0.18f), new Vector2(0f, 0.185f) }, 16, enamel);
            k.Tube(S.Paint, MeshFactory.RoundCorners(new[] { new Vector3(-0.29f, sy + 0.04f, -0.05f), new Vector3(-0.35f, sy + 0.1f, -0.05f), new Vector3(-0.37f, sy + 0.14f, -0.05f) }, 0.04f, 3), 0.012f, 6, enamel);
            k.Tube(S.Rubber, MeshFactory.RoundCorners(new[] { new Vector3(-0.27f, sy + 0.14f, -0.05f), new Vector3(-0.22f, sy + 0.24f, -0.05f), new Vector3(-0.17f, sy + 0.14f, -0.05f) }, 0.05f, 4), 0.01f, 6, T(0.05f));
        }

        /// <summary>A frame-and-panel door hinged at <paramref name="hinge"/>, extending toward <paramref name="dir"/>, swung by <paramref name="open"/> degrees.</summary>
        private static void CabinetDoor(StructureKit k, Vector3 hinge, float dir, float width, float y0, float y1, float open, Color body)
        {
            Matrix4x4 saved = k.Matrix;
            k.Matrix = saved * Matrix4x4.TRS(hinge, Quaternion.Euler(0f, -dir * open, 0f), Vector3.one);
            k.Span(S.PaintedWood, new Vector3(0f, y0, 0f), new Vector3(dir * width, y1, 0.02f), body, 0.006f);
            k.Span(S.PaintedWood, new Vector3(dir * 0.06f, y0 + 0.07f, 0.02f), new Vector3(dir * (width - 0.06f), y1 - 0.07f, 0.03f), Scale(body, 0.93f), 0.012f);
            k.Sphere(S.Metal, new Vector3(dir * (width - 0.04f), y1 - 0.08f, 0.04f), Vector3.one * 0.016f, T(0.7f), 1);
            k.Matrix = saved;
        }

        /// <summary>Old enamelled fridge-freezer: rounded body, two doors with long handles, kick grille, rust at the edges.</summary>
        public static void Fridge(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color enamel = Paint(color);
            k.Span(S.Paint, new Vector3(-hx, 0.06f, -hz), new Vector3(hx, sy, hz - 0.045f), Scale(enamel, 0.95f), 0.045f);
            k.Span(S.Paint, new Vector3(-hx + 0.04f, 0f, -hz + 0.05f), new Vector3(hx - 0.04f, 0.08f, hz - 0.08f), Paint(0.12f, 0.12f, 0.12f), 0.005f);
            float split = sy - 0.5f;
            k.Span(S.Paint, new Vector3(-hx + 0.006f, split + 0.012f, hz - 0.05f), new Vector3(hx - 0.006f, sy - 0.012f, hz), enamel, 0.035f);
            k.Span(S.Paint, new Vector3(-hx + 0.006f, 0.1f, hz - 0.05f), new Vector3(hx - 0.006f, split - 0.012f, hz), enamel, 0.035f);
            k.Span(S.Rubber, new Vector3(-hx + 0.03f, split - 0.014f, hz - 0.055f), new Vector3(hx - 0.03f, split + 0.014f, hz - 0.045f), T(0.04f));
            Color chrome = T(0.75f);
            foreach (var (a, b) in new[] { (split + 0.06f, sy - 0.12f), (split - 0.42f, split - 0.06f) })
            {
                float x = -hx + 0.07f;
                var pts = new[] { new Vector3(x, a, hz - 0.002f), new Vector3(x, a, hz + 0.04f), new Vector3(x, b, hz + 0.04f), new Vector3(x, b, hz - 0.002f) };
                k.Tube(S.Metal, MeshFactory.RoundCorners(pts, 0.025f, 3), 0.012f, 8, chrome);
            }
            foreach (float y in new[] { 0.14f, split - 0.03f, split + 0.04f, sy - 0.05f })
                k.Span(S.Metal, new Vector3(hx - 0.04f, y - 0.025f, hz - 0.05f), new Vector3(hx + 0.004f, y + 0.025f, hz - 0.03f), T(0.55f), 0.004f);
            k.Span(S.Metal, new Vector3(-0.1f, sy - 0.1f, hz - 0.001f), new Vector3(0.1f, sy - 0.07f, hz + 0.002f), T(0.8f), 0.003f);
            if (!k.Detail) return;
            for (int i = 0; i < 5; i++)
                k.Span(S.Paint, new Vector3(-hx + 0.06f, 0.012f + i * 0.013f, hz - 0.081f), new Vector3(hx - 0.06f, 0.018f + i * 0.013f, hz - 0.075f), Paint(0.3f, 0.3f, 0.3f));
            // Rust creeping up from the bottom edges and a note under a magnet.
            for (int i = 0; i < 4; i++)
            {
                float x = -hx + 0.08f + k.Rand(10 + i) * (sx - 0.2f), w = 0.04f + k.Rand(20 + i) * 0.06f, h = 0.03f + k.Rand(30 + i) * 0.08f;
                k.Span(S.Rust, new Vector3(x, 0.1f, hz - 0.001f), new Vector3(x + w, 0.1f + h, hz + 0.0015f), T(0.85f));
            }
            k.Span(S.Plain, new Vector3(0.05f, split + 0.18f, hz - 0.001f), new Vector3(0.19f, split + 0.33f, hz + 0.001f), Rel(new Color(0.9f, 0.86f, 0.62f), StructurePalette.Plain));
            k.Cylinder(S.Paint, new Vector3(0.12f, split + 0.31f, hz + 0.001f), new Vector3(0.12f, split + 0.31f, hz + 0.012f), 0.014f, 0.014f, 10, Paint(0.75f, 0.15f, 0.12f));
        }

        /// <summary>Tall panelled wardrobe with a plinth, stepped cornice, knobs and a keyhole.</summary>
        public static void Wardrobe(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = WoodTone(color);
            k.Span(S.TimberV, new Vector3(-hx + 0.02f, 0f, -hz + 0.02f), new Vector3(hx - 0.02f, 0.1f, hz - 0.01f), Scale(wood, 0.7f), 0.006f);
            k.Span(S.TimberV, new Vector3(-hx + 0.03f, 0.1f, -hz + 0.03f), new Vector3(hx - 0.03f, sy - 0.12f, hz - 0.03f), wood, 0.006f);
            k.Span(S.TimberV, new Vector3(-hx + 0.012f, sy - 0.12f, -hz + 0.012f), new Vector3(hx - 0.012f, sy - 0.055f, hz - 0.012f), Scale(wood, 0.85f), 0.008f);
            k.Span(S.TimberV, new Vector3(-hx, sy - 0.055f, -hz), new Vector3(hx, sy, hz), Scale(wood, 0.8f), 0.014f);
            float y0 = 0.13f, y1 = sy - 0.15f;
            k.Span(S.TimberV, new Vector3(-0.005f, y0, hz - 0.032f), new Vector3(0.005f, y1, hz - 0.028f), T(0.05f, 0.4f));
            foreach (float side in new[] { -1f, 1f })
            {
                float xi = side * 0.005f, xo = side * (hx - 0.045f);
                k.Span(S.TimberV, new Vector3(xi, y0, hz - 0.03f), new Vector3(xo, y1, hz - 0.006f), Board(k, side > 0f ? 1 : 2, wood), 0.006f);
                float pi = side * 0.07f, po = side * (hx - 0.11f);
                k.Span(S.TimberV, new Vector3(pi, 0.95f, hz - 0.006f), new Vector3(po, y1 - 0.09f, hz + 0.004f), Board(k, side > 0f ? 3 : 4, Scale(wood, 0.92f)), 0.014f);
                k.Span(S.TimberV, new Vector3(pi, y0 + 0.09f, hz - 0.006f), new Vector3(po, 0.83f, hz + 0.004f), Board(k, side > 0f ? 5 : 6, Scale(wood, 0.92f)), 0.014f);
                k.Sphere(S.Metal, new Vector3(side * 0.045f, 1.0f, hz + 0.02f), Vector3.one * 0.018f, Rel(new Color(0.72f, 0.56f, 0.3f), StructurePalette.Metal), 1);
            }
            k.Span(S.Metal, new Vector3(0.012f, 0.93f, hz - 0.006f), new Vector3(0.03f, 0.97f, hz - 0.002f), Rel(new Color(0.72f, 0.56f, 0.3f), StructurePalette.Metal));
        }

        /// <summary>Standing white metal medicine cabinet with a red cross, a lever handle and vent slots.</summary>
        public static void MedicineCabinet(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color white = Paint(color);
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                    k.Cylinder(S.Metal, new Vector3(xs * (hx - 0.05f), 0f, zs * (hz - 0.05f)), new Vector3(xs * (hx - 0.05f), 0.09f, zs * (hz - 0.05f)), 0.015f, 0.018f, 8, T(0.55f));
            k.Span(S.Paint, new Vector3(-hx, 0.08f, -hz), new Vector3(hx, sy, hz - 0.025f), Scale(white, 0.95f), 0.015f);
            k.Span(S.Paint, new Vector3(-hx + 0.012f, 0.1f, hz - 0.026f), new Vector3(hx - 0.012f, sy - 0.015f, hz - 0.004f), white, 0.008f);
            Color red = Paint(0.72f, 0.1f, 0.08f);
            float cy = sy * 0.66f;
            k.Span(S.Paint, new Vector3(-0.04f, cy - 0.13f, hz - 0.004f), new Vector3(0.04f, cy + 0.13f, hz), red);
            k.Span(S.Paint, new Vector3(-0.13f, cy - 0.04f, hz - 0.004f), new Vector3(0.13f, cy + 0.04f, hz), red);
            k.Span(S.Metal, new Vector3(hx - 0.07f, cy - 0.12f, hz - 0.004f), new Vector3(hx - 0.045f, cy - 0.02f, hz + 0.012f), T(0.7f), 0.004f);
            k.Cylinder(S.Metal, new Vector3(hx - 0.0575f, cy + 0.03f, hz - 0.004f), new Vector3(hx - 0.0575f, cy + 0.03f, hz + 0.002f), 0.012f, 0.012f, 10, T(0.7f));
            for (int i = 0; i < 4; i++)
                k.Span(S.Paint, new Vector3(-0.15f, 0.18f + i * 0.03f, hz - 0.0045f), new Vector3(0.15f, 0.192f + i * 0.03f, hz - 0.002f), Paint(0.15f, 0.15f, 0.15f));
            if (!k.Detail) return;
            Color brown = Rel(new Color(0.45f, 0.22f, 0.08f), StructurePalette.Glass);
            for (int i = 0; i < 3; i++)
            {
                float x = -0.25f + i * 0.09f, h = 0.08f + k.Rand(40 + i) * 0.05f;
                k.Lathe(S.Glass, new Vector3(x, sy, 0f), new[] { new Vector2(0.025f, 0f), new Vector2(0.025f, h), new Vector2(0.012f, h + 0.02f), new Vector2(0.012f, h + 0.03f), new Vector2(0f, h + 0.031f) }, 10, i == 1 ? T(0.9f) : brown);
            }
            k.Span(S.Paint, new Vector3(0.05f, sy, -0.08f), new Vector3(0.25f, sy + 0.07f, 0.06f), Paint(0.85f, 0.85f, 0.82f), 0.004f);
            k.Span(S.Paint, new Vector3(0.12f, sy + 0.02f, 0.06f), new Vector3(0.18f, sy + 0.05f, 0.062f), red);
        }

        /// <summary>Steel locker pair: two doors with louvred vents, lift handles, number plates and a padlock.</summary>
        public static void Locker(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color paint = Paint(color);
            k.Span(S.Paint, new Vector3(-hx + 0.01f, 0f, -hz + 0.02f), new Vector3(hx - 0.01f, 0.08f, hz - 0.05f), Paint(0.12f, 0.12f, 0.12f), 0.004f);
            k.Span(S.Paint, new Vector3(-hx, 0.08f, -hz), new Vector3(hx, sy, hz - 0.02f), Scale(paint, 0.9f), 0.01f);
            Color slot = Paint(0.05f, 0.05f, 0.05f);
            foreach (float side in new[] { -1f, 1f })
            {
                float xi = side * 0.004f, xo = side * (hx - 0.012f), xc = (xi + xo) * 0.5f;
                k.Span(S.Paint, new Vector3(xi, 0.1f, hz - 0.021f), new Vector3(xo, sy - 0.025f, hz - 0.003f), paint, 0.005f);
                foreach (float vy in new[] { sy - 0.3f, 0.17f })
                    for (int i = 0; i < 6; i++)
                    {
                        float y = vy + i * 0.022f;
                        k.Span(S.Paint, new Vector3(xc - 0.11f, y, hz - 0.0035f), new Vector3(xc + 0.11f, y + 0.01f, hz - 0.002f), slot);
                        k.Span(S.Paint, new Vector3(xc - 0.112f, y + 0.01f, hz - 0.003f), new Vector3(xc + 0.112f, y + 0.016f, hz + 0.006f), paint, 0.002f);
                    }
                float hxp = side * 0.055f;
                k.Span(S.Metal, new Vector3(hxp - 0.014f, sy * 0.5f - 0.07f, hz - 0.003f), new Vector3(hxp + 0.014f, sy * 0.5f + 0.07f, hz + 0.016f), T(0.65f), 0.004f);
                k.Span(S.Metal, new Vector3(xc - 0.04f, sy - 0.09f, hz - 0.003f), new Vector3(xc + 0.04f, sy - 0.06f, hz), T(0.8f));
                k.Span(S.Paint, new Vector3(xc - 0.02f, sy - 0.082f, hz), new Vector3(xc + 0.02f, sy - 0.068f, hz + 0.001f), slot);
            }
            if (!k.Detail) return;
            Color brass = Rel(new Color(0.72f, 0.58f, 0.28f), StructurePalette.Metal);
            Vector3 lockP = new Vector3(-0.055f, sy * 0.5f - 0.13f, hz + 0.018f);
            k.Span(S.Metal, lockP - new Vector3(0.022f, 0.025f, 0.008f), lockP + new Vector3(0.022f, 0.02f, 0.008f), brass, 0.005f);
            k.Tube(S.Metal, new[] { lockP + new Vector3(-0.013f, 0.02f, 0f), lockP + new Vector3(-0.013f, 0.045f, 0f), lockP + new Vector3(0f, 0.058f, 0f), lockP + new Vector3(0.013f, 0.045f, 0f), lockP + new Vector3(0.013f, 0.02f, 0f) }, 0.004f, 5, T(0.8f));
            for (int i = 0; i < 3; i++)
            {
                float x = -hx + 0.05f + k.Rand(50 + i) * (sx - 0.15f), w = 0.03f + k.Rand(60 + i) * 0.05f;
                k.Span(S.Rust, new Vector3(x, 0.1f, hz - 0.002f), new Vector3(x + w, 0.12f + k.Rand(70 + i) * 0.12f, hz + 0.0005f), T(0.85f));
            }
        }

        /// <summary>Gondola store shelving with price strips and the leftovers of its stock (stable per variant).</summary>
        public static void StoreShelf(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color paint = Paint(color);
            k.Span(S.Paint, new Vector3(-hx, 0f, -hz + 0.02f), new Vector3(hx, 0.12f, hz - 0.02f), Scale(paint, 0.55f), 0.005f);
            k.Span(S.Paint, new Vector3(-hx + 0.02f, 0.12f, -hz), new Vector3(hx - 0.02f, sy - 0.03f, -hz + 0.025f), Scale(paint, 0.9f), 0.004f);
            foreach (float xs in new[] { -1f, 1f })
                k.Span(S.Paint, new Vector3(xs * (hx - 0.02f), 0f, -hz), new Vector3(xs * hx, sy, hz), paint, 0.005f);
            k.Span(S.Paint, new Vector3(-hx, sy - 0.03f, -hz), new Vector3(hx, sy, hz), paint, 0.006f);
            float[] levels = { 0.12f, 0.52f, 0.92f, 1.32f };
            Color strip = Paint(0.85f, 0.83f, 0.74f);
            foreach (float y in levels)
            {
                k.Span(S.Paint, new Vector3(-hx + 0.02f, y - 0.02f, -hz + 0.025f), new Vector3(hx - 0.02f, y, hz - 0.03f), paint, 0.003f);
                k.Span(S.Paint, new Vector3(-hx + 0.02f, y - 0.05f, hz - 0.035f), new Vector3(hx - 0.02f, y + 0.008f, hz - 0.02f), strip, 0.003f);
            }
            if (!k.Detail) return;
            Color[] goods =
            {
                Paint(0.62f, 0.16f, 0.12f), Paint(0.8f, 0.64f, 0.2f), Paint(0.2f, 0.32f, 0.55f), Paint(0.26f, 0.45f, 0.26f),
                Paint(0.85f, 0.84f, 0.8f), Paint(0.8f, 0.42f, 0.15f), Paint(0.45f, 0.2f, 0.35f)
            };
            int salt = 100;
            for (int level = 0; level < levels.Length; level++)
            {
                float y = levels[level];
                float x = -hx + 0.05f;
                while (x < hx - 0.08f)
                {
                    float r = k.Rand(salt++);
                    Color c = goods[(int)(k.Rand(salt++) * goods.Length) % goods.Length];
                    float z = hz - 0.13f - k.Rand(salt++) * 0.05f;
                    if (r < 0.28f)
                    {
                        x += 0.1f + k.Rand(salt++) * 0.15f; // looted gap
                        continue;
                    }
                    if (r < 0.6f)
                    {
                        // A row of cans.
                        int cans = 1 + (int)(k.Rand(salt++) * 3f);
                        for (int i = 0; i < cans && x < hx - 0.08f; i++)
                        {
                            bool fallen = k.Rand(salt++) < 0.12f;
                            if (fallen) k.Cylinder(S.Paint, new Vector3(x, y + 0.035f, z - 0.05f), new Vector3(x, y + 0.035f, z + 0.06f), 0.035f, 0.035f, 10, c);
                            else
                            {
                                k.Cylinder(S.Metal, new Vector3(x + 0.036f, y, z), new Vector3(x + 0.036f, y + 0.11f, z), 0.035f, 0.035f, 10, T(0.7f));
                                k.Cylinder(S.Paint, new Vector3(x + 0.036f, y + 0.015f, z), new Vector3(x + 0.036f, y + 0.095f, z), 0.0365f, 0.0365f, 10, c, true, false, false);
                            }
                            x += 0.078f;
                        }
                    }
                    else if (r < 0.85f)
                    {
                        // A cereal box or carton.
                        float w = 0.07f + k.Rand(salt++) * 0.1f, h = 0.16f + k.Rand(salt++) * 0.12f, d = 0.06f + k.Rand(salt++) * 0.06f;
                        float tilt = k.Rand(salt++) < 0.15f ? 12f : 0f;
                        k.Box(S.Paint, new Vector3(x + w * 0.5f, y + h * 0.5f, z), new Vector3(w, h, d), c, 0.004f, Quaternion.Euler(0f, (k.Rand(salt++) - 0.5f) * 10f, tilt));
                        k.Span(S.Paint, new Vector3(x + w * 0.15f, y + h * 0.55f, z + d * 0.5f), new Vector3(x + w * 0.85f, y + h * 0.8f, z + d * 0.5f + 0.002f), Paint(0.88f, 0.86f, 0.8f));
                        x += w + 0.01f;
                    }
                    else
                    {
                        // Bottles.
                        Color glass = k.Rand(salt++) < 0.5f ? Rel(new Color(0.25f, 0.4f, 0.22f), StructurePalette.Glass) : Rel(new Color(0.42f, 0.24f, 0.1f), StructurePalette.Glass);
                        for (int i = 0; i < 2 && x < hx - 0.08f; i++)
                        {
                            k.Lathe(S.Glass, new Vector3(x + 0.035f, y, z), new[] { new Vector2(0.032f, 0f), new Vector2(0.032f, 0.16f), new Vector2(0.014f, 0.21f), new Vector2(0.013f, 0.25f), new Vector2(0f, 0.252f) }, 10, glass);
                            x += 0.075f;
                        }
                    }
                }
            }
        }

        /// <summary>Red steel toolbox with an overhanging lid, latches and a rubber-gripped handle.</summary>
        public static void Toolbox(StructureKit k, float sx, float sy, float sz, Color color)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color red = Paint(color);
            k.Span(S.Paint, new Vector3(-hx + 0.012f, 0.008f, -hz + 0.03f), new Vector3(hx - 0.012f, 0.27f, hz - 0.03f), Scale(red, 0.92f), 0.01f);
            k.Span(S.Paint, new Vector3(-hx, 0.27f, -hz + 0.018f), new Vector3(hx, 0.335f, hz - 0.018f), red, 0.018f);
            foreach (float x in new[] { -hx + 0.05f, hx - 0.05f })
                k.Span(S.Rubber, new Vector3(x - 0.03f, 0f, -hz + 0.05f), new Vector3(x + 0.03f, 0.01f, hz - 0.05f), T(0.04f));
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                k.Span(S.Metal, new Vector3(x - 0.03f, 0.21f, hz - 0.032f), new Vector3(x + 0.03f, 0.3f, hz - 0.016f), T(0.75f), 0.004f);
                k.Span(S.Paint, new Vector3(x - 0.015f, 0.335f, -0.015f), new Vector3(x + 0.015f, 0.37f, 0.015f), Paint(0.15f, 0.15f, 0.15f), 0.004f);
            }
            k.Cylinder(S.Metal, new Vector3(-hx + 0.02f, 0.272f, -hz + 0.02f), new Vector3(hx - 0.02f, 0.272f, -hz + 0.02f), 0.008f, 0.008f, 6, T(0.6f));
            k.Cylinder(S.Rubber, new Vector3(-0.2f, sy - 0.018f, 0f), new Vector3(0.2f, sy - 0.018f, 0f), 0.016f, 0.016f, 10, T(0.05f));
            if (!k.Detail) return;
            k.Span(S.Plain, new Vector3(-0.08f, 0.1f, hz - 0.031f), new Vector3(0.08f, 0.16f, hz - 0.028f), Rel(new Color(0.85f, 0.82f, 0.7f), StructurePalette.Plain));
            for (int i = 0; i < 2; i++)
            {
                float x = -hx + 0.1f + k.Rand(10 + i) * (sx - 0.25f);
                k.Span(S.Rust, new Vector3(x, 0.01f, hz - 0.031f), new Vector3(x + 0.05f, 0.04f + k.Rand(20 + i) * 0.05f, hz - 0.029f), T(0.85f));
            }
        }
    }
}
