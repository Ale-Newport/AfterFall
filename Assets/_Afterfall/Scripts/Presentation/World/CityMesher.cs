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
        private static Material Brick, Render, Stone, Slate, Tiles, Glass, Metal, Wood, Skyline, Paint;
        private static Material FacadeBrick, FacadeRender, FacadeConcrete, FacadeStone, PlainStone, Roofing;

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
            Paint = MaterialLibrary.Surface(Surface.PaintedMetal, Color.white, 0.35f, 0.3f);
            FacadeBrick = MaterialLibrary.Facade(CitySurfaces.FacadeBrick, Color.white);
            FacadeRender = MaterialLibrary.Facade(CitySurfaces.FacadeRender, Color.white);
            FacadeConcrete = MaterialLibrary.Facade(CitySurfaces.FacadeConcrete, Color.white);
            FacadeStone = MaterialLibrary.Facade(CitySurfaces.FacadeStone, Color.white);
            PlainStone = MaterialLibrary.Surface(Surface.Stone, new Color(0.78f, 0.75f, 0.68f));
            Roofing = MaterialLibrary.Surface(Surface.Concrete, new Color(0.5f, 0.5f, 0.5f));
        }

        private static Material FacadeFor(string facade)
        {
            switch (facade)
            {
                case "brick": return FacadeBrick;
                case "render": return FacadeRender;
                case "concrete": return FacadeConcrete;
                case "stone": return FacadeStone;
                default: return null;
            }
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

        /// <summary>
        /// The detailed buildings of a chunk (with colliders): every block drawn as the outside of its
        /// real building (BuildingPlans.ShapeOf: the same size, floors and facade, the facade texture's
        /// windows on the real windows' bay grid, a flat roof with a parapet and plant). Buildings in
        /// <paramref name="exclude"/> (plan ids) are left out: they stand there for real, built with their
        /// insides. Geometry only, safe on a worker thread.
        /// </summary>
        public static Prepared PrepareDetailed(ChunkContent c, HashSet<string> exclude = null)
        {
            var prep = new Prepared { Kit = new Kit(), Origin = new Vector3(c.MinX, 0f, c.MinZ), Name = $"City {c.X},{c.Z}" };
            Kit kit = prep.Kit;
            foreach (PropPlacement p in c.Blocks)
            {
                if (!BuildingPlans.IsCityBlock(p.Kind)) continue;
                if (exclude != null && exclude.Count > 0 && exclude.Contains(BuildingPlans.CityBlockPlanId(p))) continue;
                BuildingPlans.CityShape shape = BuildingPlans.ShapeOf(p);
                Afterfall.Core.Vec3 o = BuildingPlans.CityOrigin(p, shape);
                var pos = new Vector3(o.X, o.Y, o.Z) - prep.Origin;
                Quaternion rot = Quaternion.Euler(0f, p.Yaw, 0f);
                kit.Matrix = Matrix4x4.TRS(pos, rot, Vector3.one);
                Exterior(kit, shape, p.Position.X * 7.1f + p.Position.Z * 3.3f);
                prep.Colliders.Add((pos + rot * new Vector3(0f, shape.TotalHeight * 0.5f - 0.75f, 0f), rot, new Vector3(shape.Width, shape.TotalHeight + 1.5f, shape.Depth)));
            }
            foreach (RoadPath r in c.Streets) StreetFurniture(kit, r, prep.Origin);
            return prep;
        }

        /// <summary>A city building's outside in its own frame (origin at the middle of its base, front +Z).</summary>
        private static void Exterior(Kit kit, BuildingPlans.CityShape s, float seedF)
        {
            int seed = Mathf.Abs((int)seedF);
            float hx = s.Width * 0.5f, hz = s.Depth * 0.5f, top = s.TotalHeight;
            Material face = FacadeFor(s.Facade);
            if (face != null)
            {
                MeshBuilder f = kit[face];
                f.Color = PlanBuilder.FacadeTint(s.Facade, s.Tint);
                float ow = BuildingPlans.BayOffset(s.Width), od = BuildingPlans.BayOffset(s.Depth);
                f.FacadeQuad(new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), -0.75f, top, Vector3.forward, -ow);
                f.FacadeQuad(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), -0.75f, top, Vector3.back, -ow);
                f.FacadeQuad(new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), -0.75f, top, Vector3.left, -od);
                f.FacadeQuad(new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), -0.75f, top, Vector3.right, -od);
            }
            else
            {
                // A shed's corrugated cladding, a church's stone.
                MeshBuilder w = kit[s.Facade == "corrugated" ? Metal : PlainStone];
                w.Color = s.Facade == "corrugated" ? Tone(new Color(0.75f, 0.78f, 0.76f), 0.9f + Hash(seed, 1) * 0.15f) : Color.white;
                w.Box(new Vector3(0f, top * 0.5f - 0.375f, 0f), new Vector3(s.Width, top + 0.75f, s.Depth));
                if (s.Facade != "corrugated")
                {
                    // Tall church windows.
                    MeshBuilder g = kit[Glass];
                    g.Color = Color.white;
                    for (float z = -hz + 2.5f; z < hz - 2f; z += 3.6f)
                    {
                        g.Box(new Vector3(hx + 0.01f, 4.5f, z), new Vector3(0.04f, 4.2f, 1.4f));
                        g.Box(new Vector3(-hx - 0.01f, 4.5f, z), new Vector3(0.04f, 4.2f, 1.4f));
                    }
                }
            }
            // The roof: membrane, coping on the parapet, and some plant.
            MeshBuilder roof = kit[Roofing];
            roof.Color = Color.white;
            roof.Box(new Vector3(0f, s.RoofY + 0.02f, 0f), new Vector3(s.Width - 0.4f, 0.06f, s.Depth - 0.4f));
            MeshBuilder coping = kit[Stone];
            coping.Color = Tone(Color.white, 0.92f);
            coping.Box(new Vector3(0f, top + 0.03f, hz - 0.15f), new Vector3(s.Width + 0.04f, 0.06f, 0.36f));
            coping.Box(new Vector3(0f, top + 0.03f, -hz + 0.15f), new Vector3(s.Width + 0.04f, 0.06f, 0.36f));
            coping.Box(new Vector3(hx - 0.15f, top + 0.03f, 0f), new Vector3(0.36f, 0.06f, s.Depth));
            coping.Box(new Vector3(-hx + 0.15f, top + 0.03f, 0f), new Vector3(0.36f, 0.06f, s.Depth));
            if (s.Floors >= 3)
            {
                MeshBuilder m = kit[Paint];
                m.Color = Tone(new Color(0.7f, 0.72f, 0.72f), 0.9f);
                m.Box(new Vector3(hx * 0.4f, s.RoofY + 0.65f, -hz * 0.3f), new Vector3(2.2f, 1.3f, 1.5f));
                m.Box(new Vector3(-hx * 0.35f, s.RoofY + 1.2f, hz * 0.25f), new Vector3(2.4f, 2.4f, 2.4f));
                if (s.Floors >= 12) m.Box(new Vector3(0f, s.RoofY + 1.6f, 0f), new Vector3(Mathf.Min(6f, s.Width * 0.3f), 3.2f, Mathf.Min(6f, s.Depth * 0.3f)));
            }
        }

        /// <summary>
        /// Lamp posts along both pavements of a street, with bins, benches, and on the main roads bus
        /// shelters and telephone boxes. Render-only (people walk round them; nothing paths through them).
        /// </summary>
        private static void StreetFurniture(Kit kit, RoadPath r, Vector3 origin)
        {
            bool main = r.Width >= 11f;
            int n = 0;
            for (int s = 0; s + 1 < r.Points.Count; s++)
            {
                Vector3 a = new Vector3(r.Points[s].X, r.Points[s].Y, r.Points[s].Z) - origin, b = new Vector3(r.Points[s + 1].X, r.Points[s + 1].Y, r.Points[s + 1].Z) - origin;
                Vector3 dir = b - a;
                dir.y = 0f;
                float len = dir.magnitude;
                if (len < 24f) continue;
                dir /= len;
                Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                int seed = Mathf.Abs(Mathf.RoundToInt(a.x * 5.3f + origin.x) * 17 + Mathf.RoundToInt(a.z * 2.9f + origin.z));
                // Clear of the junctions at both ends.
                for (float t = 12f; t < len - 12f; t += main ? 24f : 28f, n++)
                {
                    float side = (n & 1) == 0 ? 1f : -1f;
                    Vector3 kerb = Vector3.Lerp(a, b, t / len) + right * side * (r.Width * 0.5f + 0.5f);
                    float yaw = Mathf.Atan2(-right.x * side, -right.z * side) * Mathf.Rad2Deg; // facing the road
                    kit.Matrix = Matrix4x4.TRS(kerb, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                    Lamp(kit);
                    float roll = Hash(seed, n);
                    Vector3 along = dir * (roll < 0.5f ? 3f : -3f);
                    if (main && n % 9 == 4)
                    {
                        kit.Matrix = Matrix4x4.TRS(kerb + right * side * 1.3f + dir * 6f, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                        Shelter(kit);
                    }
                    else if (main && n % 13 == 7)
                    {
                        kit.Matrix = Matrix4x4.TRS(kerb + right * side * 1.4f + along, Quaternion.Euler(0f, yaw + 180f, 0f), Vector3.one);
                        PhoneBox(kit);
                    }
                    else if (n % 4 == 2)
                    {
                        kit.Matrix = Matrix4x4.TRS(kerb + right * side * 1.1f + along, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                        Bench(kit, roll);
                    }
                    else if (n % 3 == 1)
                    {
                        kit.Matrix = Matrix4x4.TRS(kerb + right * side * 0.2f + along * 0.6f, Quaternion.Euler(0f, yaw + roll * 90f, 0f), Vector3.one);
                        Bin(kit, roll);
                    }
                }
            }
            kit.Matrix = Matrix4x4.identity;
        }

        private static readonly Color LampGreen = new Color(0.03f, 0.06f, 0.045f), BinGreen = new Color(0.025f, 0.07f, 0.04f), Grey = new Color(0.22f, 0.23f, 0.23f);

        /// <summary>A street lamp, local +Z towards the road: a tapered post and an arm with its lantern over the kerb.</summary>
        private static void Lamp(Kit kit)
        {
            MeshBuilder p = kit[Paint];
            p.Color = LampGreen;
            p.Cylinder(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.5f, 0f), 0.16f, 0.12f, 8);
            p.Cylinder(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 6.2f, 0f), 0.08f, 0.055f, 6);
            p.Cylinder(new Vector3(0f, 6.1f, 0f), new Vector3(0f, 6.25f, 1.1f), 0.04f, 0.035f, 5);
            p.Box(new Vector3(0f, 6.18f, 1.25f), new Vector3(0.36f, 0.12f, 0.55f));
            MeshBuilder g = kit[Glass];
            g.Color = new Color(0.8f, 0.78f, 0.66f);
            g.Box(new Vector3(0f, 6.08f, 1.25f), new Vector3(0.28f, 0.08f, 0.45f));
        }

        private static void Bin(Kit kit, float roll)
        {
            MeshBuilder p = kit[Paint];
            p.Color = Tone(BinGreen, 0.85f + roll * 0.3f);
            p.Cylinder(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.85f, 0f), 0.27f, 0.3f, 10);
            p.Color = Grey;
            p.Cylinder(new Vector3(0f, 0.85f, 0f), new Vector3(0f, 0.95f, 0f), 0.32f, 0.26f, 10);
        }

        /// <summary>A park bench facing the road (+Z): two cast frames and timber slats.</summary>
        private static void Bench(Kit kit, float roll)
        {
            MeshBuilder p = kit[Paint];
            p.Color = LampGreen;
            for (int s = -1; s <= 1; s += 2)
            {
                p.Box(new Vector3(s * 0.72f, 0.22f, 0.05f), new Vector3(0.06f, 0.44f, 0.5f));
                p.Box(new Vector3(s * 0.72f, 0.62f, -0.2f), new Vector3(0.06f, 0.5f, 0.06f), Quaternion.Euler(-12f, 0f, 0f));
            }
            MeshBuilder w = kit[Wood];
            w.Color = Tone(new Color(0.26f, 0.16f, 0.09f), 0.85f + roll * 0.3f);
            for (int i = 0; i < 3; i++) w.Box(new Vector3(0f, 0.45f, -0.12f + i * 0.14f), new Vector3(1.6f, 0.04f, 0.11f));
            for (int i = 0; i < 2; i++) w.Box(new Vector3(0f, 0.62f + i * 0.17f, -0.24f - i * 0.035f), new Vector3(1.6f, 0.11f, 0.035f), Quaternion.Euler(-12f, 0f, 0f));
        }

        /// <summary>A bus shelter, open towards the road (+Z): a steel frame, glass back and sides, a flat roof and a seat.</summary>
        private static void Shelter(Kit kit)
        {
            MeshBuilder p = kit[Paint];
            p.Color = Grey;
            foreach (float x in new[] { -1.8f, 1.8f })
                foreach (float z in new[] { -0.7f, 0.7f })
                    p.Box(new Vector3(x, 1.25f, z), new Vector3(0.08f, 2.5f, 0.08f));
            p.Box(new Vector3(0f, 2.55f, 0f), new Vector3(3.9f, 0.1f, 1.7f));
            p.Box(new Vector3(0f, 0.5f, -0.5f), new Vector3(2.8f, 0.05f, 0.35f));
            MeshBuilder g = kit[Glass];
            g.Color = new Color(0.55f, 0.6f, 0.62f);
            g.Box(new Vector3(0f, 1.3f, -0.7f), new Vector3(3.5f, 2.2f, 0.03f));
            g.Box(new Vector3(-1.8f, 1.3f, 0f), new Vector3(0.03f, 2.2f, 1.3f));
            g.Box(new Vector3(1.8f, 1.3f, 0f), new Vector3(0.03f, 2.2f, 1.3f));
        }

        private static void PhoneBox(Kit kit)
        {
            MeshBuilder p = kit[Paint];
            p.Color = new Color(0.42f, 0.02f, 0.015f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    p.Box(new Vector3(sx * 0.45f, 1.25f, sz * 0.45f), new Vector3(0.1f, 2.5f, 0.1f));
            p.Box(new Vector3(0f, 2.55f, 0f), new Vector3(1.05f, 0.2f, 1.05f));
            p.Box(new Vector3(0f, 2.72f, 0f), new Vector3(0.9f, 0.14f, 0.9f));
            p.Box(new Vector3(0f, 0.05f, 0f), new Vector3(1.05f, 0.1f, 1.05f));
            MeshBuilder g = kit[Glass];
            g.Color = new Color(0.5f, 0.52f, 0.5f);
            g.Box(new Vector3(0f, 1.3f, 0f), new Vector3(0.84f, 2.2f, 0.84f));
        }

        /// <summary>The chunk's buildings as plain boxes in one mesh, for the skyline far away (worker thread): the real buildings' sizes.</summary>
        public static Prepared PrepareSkyline(ChunkContent c)
        {
            var prep = new Prepared { Sky = new MeshBuilder(), Origin = new Vector3(c.MinX, 0f, c.MinZ), Name = $"Skyline {c.X},{c.Z}" };
            MeshBuilder mb = prep.Sky;
            foreach (PropPlacement p in c.Blocks)
            {
                if (!BuildingPlans.IsCityBlock(p.Kind)) continue;
                BuildingPlans.CityShape shape = BuildingPlans.ShapeOf(p);
                Afterfall.Core.Vec3 o = BuildingPlans.CityOrigin(p, shape);
                var pos = new Vector3(o.X, o.Y, o.Z) - prep.Origin;
                mb.Matrix = Matrix4x4.TRS(pos, Quaternion.Euler(0f, p.Yaw, 0f), Vector3.one);
                mb.Color = Tone(PlanBuilder.FacadeTint(shape.Facade, shape.Tint), 0.72f);
                float h = shape.TotalHeight;
                mb.Box(new Vector3(0f, h * 0.5f - 1f, 0f), new Vector3(shape.Width, h + 2f, shape.Depth));
            }
            return prep;
        }

        /// <summary>Turns prepared geometry into meshes, renderers and colliders under <paramref name="parent"/> (main thread).</summary>
        public static GameObject Finish(Prepared prep, Transform parent)
        {
            var root = new GameObject(prep.Name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = prep.Origin; // under the world anchor
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

    }
}
