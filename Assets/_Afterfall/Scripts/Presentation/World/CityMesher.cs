using System.Collections.Generic;
using Afterfall.Domain.World;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Kesselmouth's buildings, merged per chunk: one mesh per material for every building of a chunk
    /// (terraces of brick houses with sash windows, chimneys and slate roofs; mansion blocks; glass
    /// towers; rendered semi-detached houses; corrugated dock sheds; churches with spires), plus one box
    /// collider per building under a single Not Walkable modifier (no NavMesh on roofs). Far away a
    /// chunk is drawn as a skyline of coloured boxes in one mesh. Colours vary by vertex colour, never by material.
    /// </summary>
    public static class CityMesher
    {
        private static Material Brick, Render, Stone, Slate, Tiles, Glass, Metal, Wood, Skyline;

        /// <summary>Makes the shared materials (main thread, before any preparation on workers).</summary>
        public static void Prepare()
        {
            if (Skyline != null) return;
            Brick = MaterialLibrary.Surface(Surface.Brick, Color.white);
            Render = MaterialLibrary.Surface(Surface.Plaster, Color.white);
            Stone = MaterialLibrary.Surface(Surface.Concrete, Color.white);
            Slate = MaterialLibrary.Surface(Surface.Shingles, new Color(0.42f, 0.44f, 0.48f));
            Tiles = MaterialLibrary.Surface(Surface.Shingles, new Color(0.55f, 0.32f, 0.24f));
            Glass = MaterialLibrary.Surface(Surface.Glass, new Color(0.2f, 0.24f, 0.27f), 0.9f);
            Metal = MaterialLibrary.Surface(Surface.Corrugated, Color.white);
            Wood = MaterialLibrary.Surface(Surface.Plain, Color.white, 0.2f);
            Skyline = MaterialLibrary.Surface(Surface.Plain, Color.white, 0.18f);
        }

        /// <summary>A chunk's buildings computed off the main thread, waiting to become meshes and colliders.</summary>
        public sealed class Prepared
        {
            internal Kit Kit;
            internal MeshBuilder Sky;
            internal readonly List<(Vector3 Pos, Quaternion Rot, Vector3 Size)> Colliders = new List<(Vector3, Quaternion, Vector3)>();
            internal Vector3 Origin;
            internal string Name;
        }

        internal sealed class Kit
        {
            private readonly Dictionary<Material, MeshBuilder> _parts = new Dictionary<Material, MeshBuilder>();
            public Matrix4x4 Matrix = Matrix4x4.identity;

            public MeshBuilder this[Material m]
            {
                get
                {
                    if (!_parts.TryGetValue(m, out MeshBuilder b)) _parts[m] = b = new MeshBuilder();
                    b.Matrix = Matrix;
                    return b;
                }
            }

            public void Spawn(Transform parent, string name, bool shadows)
            {
                foreach (var kv in _parts)
                    if (kv.Value.VertexCount > 0) Shapes.Mesh(parent, name + " " + kv.Key.name, kv.Value.Build(name), kv.Key, Vector3.zero, null, shadows);
            }
        }

        private static Color Tone(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
        private static float Hash(int seed, int i) => PropGeo.Hash(seed, i);

        private static readonly Color[] Bricks =
        {
            new Color(0.56f, 0.28f, 0.2f), new Color(0.72f, 0.6f, 0.42f), new Color(0.46f, 0.33f, 0.25f), new Color(0.62f, 0.38f, 0.28f),
            new Color(0.84f, 0.8f, 0.72f), new Color(0.5f, 0.24f, 0.18f), new Color(0.68f, 0.52f, 0.38f), new Color(0.78f, 0.74f, 0.66f)
        };

        /// <summary>The detailed buildings of a chunk (with colliders): geometry only, safe on a worker thread.</summary>
        public static Prepared PrepareDetailed(ChunkContent c)
        {
            var prep = new Prepared { Kit = new Kit(), Origin = new Vector3(c.MinX, 0f, c.MinZ), Name = $"City {c.X},{c.Z}" };
            Kit kit = prep.Kit;
            foreach (PropPlacement p in c.Blocks)
            {
                var pos = new Vector3(p.Position.X, p.Position.Y, p.Position.Z) - prep.Origin;
                Quaternion rot = Quaternion.Euler(0f, p.Yaw, 0f);
                kit.Matrix = Matrix4x4.TRS(pos, rot, Vector3.one);
                float w = p.Size.X, h = p.Size.Y, d = p.Size.Z;
                int seed = Mathf.Abs(Mathf.RoundToInt(p.Position.X * 3.1f) * 31 + Mathf.RoundToInt(p.Position.Z * 1.7f));
                switch (p.Kind)
                {
                    case "city_terrace": Terrace(kit, w, h, d, p.Variant, seed); break;
                    case "city_midrise": Midrise(kit, w, h, d, p.Variant, seed); break;
                    case "city_tower": Tower(kit, w, h, d, p.Variant, seed); break;
                    case "city_semi": Semi(kit, w, h, d, p.Variant, seed); break;
                    case "city_shed": Shed(kit, w, h, d, p.Variant, seed); break;
                    case "city_church": Church(kit, w, h, d, p.Variant, seed); break;
                    default: continue;
                }
                prep.Colliders.Add((pos + Vector3.up * (h * 0.5f - 0.75f), rot, new Vector3(w, h + 1.5f, d)));
            }
            return prep;
        }

        /// <summary>The chunk's buildings as plain boxes in one mesh, for the skyline far away (worker thread).</summary>
        public static Prepared PrepareSkyline(ChunkContent c)
        {
            var prep = new Prepared { Sky = new MeshBuilder(), Origin = new Vector3(c.MinX, 0f, c.MinZ), Name = $"Skyline {c.X},{c.Z}" };
            MeshBuilder mb = prep.Sky;
            foreach (PropPlacement p in c.Blocks)
            {
                var pos = new Vector3(p.Position.X, p.Position.Y, p.Position.Z) - prep.Origin;
                mb.Matrix = Matrix4x4.TRS(pos, Quaternion.Euler(0f, p.Yaw, 0f), Vector3.one);
                float h = p.Size.Y + (p.Kind == "city_terrace" || p.Kind == "city_semi" ? 1.6f : 0f);
                mb.Color = p.Kind == "city_tower" ? new Color(0.36f, 0.42f, 0.48f) : p.Kind == "city_shed" ? new Color(0.5f, 0.52f, 0.5f) : Tone(Bricks[p.Variant & 7], 0.85f);
                mb.Box(new Vector3(0f, h * 0.5f - 1f, 0f), new Vector3(p.Size.X, h + 2f, p.Size.Z));
            }
            return prep;
        }

        /// <summary>Turns prepared geometry into meshes, renderers and colliders under <paramref name="parent"/> (main thread).</summary>
        public static GameObject Finish(Prepared prep, Transform parent)
        {
            var root = new GameObject(prep.Name);
            root.transform.SetParent(parent, false);
            root.transform.position = prep.Origin;
            if (prep.Kit != null)
            {
                prep.Kit.Spawn(root.transform, "City", true);
                if (prep.Colliders.Count > 0)
                {
                    var colliders = new GameObject("Colliders").transform;
                    colliders.SetParent(root.transform, false);
                    Shapes.NotWalkable(colliders.gameObject);
                    foreach ((Vector3 pos, Quaternion rot, Vector3 size) in prep.Colliders)
                    {
                        var col = new GameObject("B");
                        col.transform.SetParent(colliders, false);
                        col.transform.localPosition = pos;
                        col.transform.localRotation = rot;
                        col.AddComponent<BoxCollider>().size = size;
                    }
                }
            }
            if (prep.Sky != null && prep.Sky.VertexCount > 0) Shapes.Mesh(root.transform, "Skyline", prep.Sky.Build("skyline"), Skyline, Vector3.zero, null, false);
            Shapes.SetLayerRecursive(root, Layers.Prop);
            return root;
        }

        // ------------------------------------------------------------------ building kinds (local frame: front is +Z, origin at ground centre)

        private static void Windows(Kit kit, float x0, float x1, float y, float zFace, float pitch, float ww, float wh, bool back = false)
        {
            MeshBuilder g = kit[Glass];
            g.Color = Color.white;
            int n = Mathf.Max(1, Mathf.FloorToInt((x1 - x0) / pitch));
            float step = (x1 - x0) / n;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + step * (i + 0.5f);
                g.Box(new Vector3(x, y + wh * 0.5f, zFace + (back ? -0.03f : 0.03f)), new Vector3(ww, wh, 0.08f));
            }
        }

        private static void GableRoof(Kit kit, Material m, Color tint, float w, float d, float y, float rise, float overhang)
        {
            MeshBuilder r = kit[m];
            r.Color = tint;
            float hw = w * 0.5f + overhang, hd = d * 0.5f + overhang;
            var inside = new Vector3(0f, y, 0f);
            r.Face(inside, new Vector3(-hw, y, hd), new Vector3(hw, y, hd), new Vector3(hw, y + rise, 0f), new Vector3(-hw, y + rise, 0f));
            r.Face(inside, new Vector3(-hw, y, -hd), new Vector3(hw, y, -hd), new Vector3(hw, y + rise, 0f), new Vector3(-hw, y + rise, 0f));
            r.Face(inside + new Vector3(0, 0, 0), new Vector3(-hw, y, hd), new Vector3(-hw, y + rise, 0f), new Vector3(-hw, y, -hd));
            r.Face(inside, new Vector3(hw, y, hd), new Vector3(hw, y + rise, 0f), new Vector3(hw, y, -hd));
        }

        private static void Terrace(Kit kit, float w, float h, float d, int variant, int seed)
        {
            int houses = Mathf.Max(1, Mathf.RoundToInt(w / 5.4f));
            float hw = w / houses;
            Color brick = Bricks[variant & 7];
            MeshBuilder b = kit[(variant & 7) == 4 || (variant & 7) == 7 ? Render : Brick];
            for (int i = 0; i < houses; i++)
            {
                b.Color = Tone(brick, 0.88f + Hash(seed, i) * 0.22f);
                float cx = -w * 0.5f + hw * (i + 0.5f);
                b.Box(new Vector3(cx, h * 0.5f - 0.75f, 0f), new Vector3(hw + 0.01f, h + 1.5f, d));
            }
            GableRoof(kit, Slate, Color.white, w, d, h, 2.1f, 0.25f);
            MeshBuilder chim = kit[Brick];
            chim.Color = Tone(brick, 0.8f);
            for (int i = 0; i <= houses; i++)
                if (i % 2 == 0) chim.Box(new Vector3(-w * 0.5f + hw * i, h + 2.3f, 0f), new Vector3(0.9f, 1.6f, 0.7f));
            int floors = Mathf.Max(1, Mathf.RoundToInt((h - 0.6f) / 3.1f));
            bool shops = (variant & 8) != 0;
            MeshBuilder wood = kit[Wood];
            for (int i = 0; i < houses; i++)
            {
                float cx = -w * 0.5f + hw * (i + 0.5f);
                for (int f = 0; f < floors; f++)
                {
                    float y = 0.9f + f * 3.1f;
                    if (f == 0 && shops) continue;
                    if (f == 0)
                    {
                        Windows(kit, cx - hw * 0.05f, cx + hw * 0.45f, y, d * 0.5f, 2.4f, 1.1f, 1.7f);
                        wood.Color = Tone(new Color(0.18f, 0.2f, 0.26f), 0.7f + Hash(seed + 5, i) * 0.8f);
                        wood.Box(new Vector3(cx - hw * 0.28f, 1.15f, d * 0.5f + 0.04f), new Vector3(1.0f, 2.2f, 0.08f));
                    }
                    else Windows(kit, cx - hw * 0.45f, cx + hw * 0.45f, y, d * 0.5f, 2.4f, 1.1f, 1.8f);
                    Windows(kit, cx - hw * 0.3f, cx + hw * 0.3f, y, -d * 0.5f, 2.8f, 1.0f, 1.5f, true);
                }
                if (shops)
                {
                    MeshBuilder g = kit[Glass];
                    g.Color = Color.white;
                    g.Box(new Vector3(cx, 1.5f, d * 0.5f + 0.03f), new Vector3(hw - 0.6f, 2.5f, 0.1f));
                    wood.Color = Color.HSVToRGB(Hash(seed + 9, i), 0.5f, 0.45f);
                    wood.Box(new Vector3(cx, 3.05f, d * 0.5f + 0.12f), new Vector3(hw - 0.2f, 0.55f, 0.2f));
                }
            }
        }

        private static void Midrise(Kit kit, float w, float h, float d, int variant, int seed)
        {
            Color c = Bricks[(variant + 3) & 7];
            MeshBuilder b = kit[(variant & 1) == 0 ? Brick : Stone];
            b.Color = Tone(c, 0.92f + Hash(seed, 1) * 0.12f);
            b.Box(new Vector3(0f, h * 0.5f - 0.75f, 0f), new Vector3(w, h + 1.5f, d));
            MeshBuilder s = kit[Stone];
            s.Color = new Color(0.72f, 0.7f, 0.66f);
            s.Box(new Vector3(0f, h + 0.45f, 0f), new Vector3(w + 0.3f, 0.9f, d + 0.3f));
            s.Box(new Vector3(0f, 3.4f, d * 0.5f + 0.05f), new Vector3(w, 0.3f, 0.2f));
            int floors = Mathf.Max(1, Mathf.FloorToInt(h / 3.2f));
            bool shops = (variant & 8) != 0;
            for (int f = 0; f < floors; f++)
            {
                float y = 1f + f * 3.2f;
                if (f == 0 && shops)
                {
                    MeshBuilder g = kit[Glass];
                    g.Color = Color.white;
                    g.Box(new Vector3(0f, 1.6f, d * 0.5f + 0.03f), new Vector3(w - 1.2f, 2.6f, 0.1f));
                    continue;
                }
                Windows(kit, -w * 0.5f + 0.8f, w * 0.5f - 0.8f, y, d * 0.5f, 3.4f, 1.4f, 1.9f);
                Windows(kit, -w * 0.5f + 0.8f, w * 0.5f - 0.8f, y, -d * 0.5f, 3.4f, 1.4f, 1.9f, true);
            }
        }

        private static void Tower(Kit kit, float w, float h, float d, int variant, int seed)
        {
            MeshBuilder g = kit[Glass];
            g.Color = Color.HSVToRGB(0.55f + Hash(seed, 2) * 0.1f, 0.15f + Hash(seed, 3) * 0.2f, 0.75f + Hash(seed, 4) * 0.25f);
            g.Box(new Vector3(0f, h * 0.5f - 0.75f, 0f), new Vector3(w, h + 1.5f, d));
            MeshBuilder s = kit[Stone];
            s.Color = Tone(new Color(0.7f, 0.7f, 0.72f), 0.8f + Hash(seed, 5) * 0.3f);
            float band = (variant & 2) == 0 ? 3.6f : 7.2f;
            for (float y = 4f; y < h - 1f; y += band) s.Box(new Vector3(0f, y, 0f), new Vector3(w + 0.12f, 0.35f, d + 0.12f));
            int fins = Mathf.Max(2, Mathf.RoundToInt(w / 6f));
            if ((variant & 4) != 0)
                for (int i = 0; i <= fins; i++)
                {
                    float x = -w * 0.5f + w * i / fins;
                    s.Box(new Vector3(x, h * 0.5f, d * 0.5f + 0.08f), new Vector3(0.3f, h, 0.2f));
                    s.Box(new Vector3(x, h * 0.5f, -d * 0.5f - 0.08f), new Vector3(0.3f, h, 0.2f));
                }
            s.Box(new Vector3(0f, h + 0.6f, 0f), new Vector3(w + 0.4f, 1.2f, d + 0.4f));
            s.Box(new Vector3(0f, h + 3.2f, 0f), new Vector3(w * 0.55f, 4f, d * 0.55f));
            MeshBuilder dark = kit[Wood];
            dark.Color = new Color(0.12f, 0.13f, 0.15f);
            dark.Box(new Vector3(0f, 2f, d * 0.5f + 0.06f), new Vector3(w * 0.5f, 4f, 0.1f));
        }

        private static void Semi(Kit kit, float w, float h, float d, int variant, int seed)
        {
            Color wall = (variant & 1) == 0 ? new Color(0.86f, 0.82f, 0.74f) : Bricks[variant & 7];
            MeshBuilder b = kit[(variant & 1) == 0 ? Render : Brick];
            b.Color = Tone(wall, 0.9f + Hash(seed, 1) * 0.15f);
            b.Box(new Vector3(0f, h * 0.5f - 0.75f, 0f), new Vector3(w, h + 1.5f, d));
            MeshBuilder r = kit[Tiles];
            r.Color = Tone(Color.white, 0.8f + Hash(seed, 2) * 0.4f);
            float hw = w * 0.5f + 0.35f, hd = d * 0.5f + 0.35f, rise = 2.6f, inset = hd;
            var inside = new Vector3(0f, h, 0f);
            r.Face(inside, new Vector3(-hw, h, hd), new Vector3(hw, h, hd), new Vector3(hw - inset, h + rise, 0f), new Vector3(-hw + inset, h + rise, 0f));
            r.Face(inside, new Vector3(-hw, h, -hd), new Vector3(hw, h, -hd), new Vector3(hw - inset, h + rise, 0f), new Vector3(-hw + inset, h + rise, 0f));
            r.Face(inside, new Vector3(-hw, h, hd), new Vector3(-hw + inset, h + rise, 0f), new Vector3(-hw, h, -hd));
            r.Face(inside, new Vector3(hw, h, hd), new Vector3(hw - inset, h + rise, 0f), new Vector3(hw, h, -hd));
            MeshBuilder chim = kit[Brick];
            chim.Color = Bricks[2];
            chim.Box(new Vector3(0f, h + 2.4f, -0.6f), new Vector3(0.8f, 1.8f, 1f));
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = side * w * 0.25f;
                Windows(kit, cx - w * 0.2f, cx + w * 0.2f, 3.9f, d * 0.5f, 2.2f, 1.2f, 1.4f);
                Windows(kit, cx + side * w * 0.02f, cx + side * w * 0.22f, 0.9f, d * 0.5f, 2.2f, 1.6f, 1.6f);
                MeshBuilder wood = kit[Wood];
                wood.Color = Color.HSVToRGB(Hash(seed + 7, side + 1), 0.45f, 0.4f);
                wood.Box(new Vector3(cx - side * w * 0.12f, 1.1f, d * 0.5f + 0.04f), new Vector3(1f, 2.1f, 0.08f));
                Windows(kit, cx - w * 0.15f, cx + w * 0.15f, 3.9f, -d * 0.5f, 2.2f, 1.1f, 1.3f, true);
            }
        }

        private static void Shed(Kit kit, float w, float h, float d, int variant, int seed)
        {
            MeshBuilder m = kit[Metal];
            Color[] cols = { new Color(0.55f, 0.58f, 0.6f), new Color(0.34f, 0.45f, 0.55f), new Color(0.4f, 0.5f, 0.38f), new Color(0.62f, 0.55f, 0.45f) };
            m.Color = Tone(cols[variant & 3], 0.9f + Hash(seed, 1) * 0.2f);
            m.Box(new Vector3(0f, h * 0.5f - 0.75f, 0f), new Vector3(w, h + 1.5f, d));
            GableRoof(kit, Metal, Tone(cols[(variant + 1) & 3], 0.8f), w, d, h, 1.6f, 0.4f);
            MeshBuilder dark = kit[Wood];
            dark.Color = new Color(0.3f, 0.31f, 0.33f);
            int doors = Mathf.Max(1, Mathf.FloorToInt(w / 12f));
            for (int i = 0; i < doors; i++)
            {
                float x = -w * 0.5f + w * (i + 0.5f) / doors;
                dark.Box(new Vector3(x, 2.4f, d * 0.5f + 0.05f), new Vector3(4.6f, 4.8f, 0.1f));
            }
            Windows(kit, -w * 0.5f + 1f, w * 0.5f - 1f, h - 2.2f, d * 0.5f, 4f, 2f, 0.9f);
        }

        private static void Church(Kit kit, float w, float h, float d, int variant, int seed)
        {
            MeshBuilder s = kit[(variant & 1) == 0 ? Stone : Brick];
            s.Color = (variant & 1) == 0 ? new Color(0.7f, 0.68f, 0.62f) : Bricks[0];
            float nave = h * 0.55f;
            s.Box(new Vector3(0f, nave * 0.5f - 0.75f, -2.5f), new Vector3(w, nave + 1.5f, d - 5f));
            GableRoof(kit, Slate, Color.white, w, d - 5f, nave, w * 0.45f, 0.3f);
            float tw = 5.5f, th = h + 6f;
            s.Box(new Vector3(0f, th * 0.5f - 0.75f, d * 0.5f - 3f), new Vector3(tw, th + 1.5f, tw));
            MeshBuilder r = kit[Slate];
            r.Color = Color.white;
            var apex = new Vector3(0f, th + 13f, d * 0.5f - 3f);
            float e = tw * 0.5f;
            var c0 = new Vector3(-e, th, d * 0.5f - 3f - e);
            var c1 = new Vector3(e, th, d * 0.5f - 3f - e);
            var c2 = new Vector3(e, th, d * 0.5f - 3f + e);
            var c3 = new Vector3(-e, th, d * 0.5f - 3f + e);
            var inside = new Vector3(0f, th + 2f, d * 0.5f - 3f);
            r.Face(inside, c0, c1, apex);
            r.Face(inside, c1, c2, apex);
            r.Face(inside, c2, c3, apex);
            r.Face(inside, c3, c0, apex);
            MeshBuilder g = kit[Glass];
            g.Color = new Color(0.5f, 0.42f, 0.6f);
            for (float z = -d * 0.5f + 3f; z < d * 0.5f - 6f; z += 4f)
            {
                g.Box(new Vector3(w * 0.5f + 0.03f, nave * 0.45f, z), new Vector3(0.08f, nave * 0.5f, 1.4f));
                g.Box(new Vector3(-w * 0.5f - 0.03f, nave * 0.45f, z), new Vector3(0.08f, nave * 0.5f, 1.4f));
            }
        }
    }
}
