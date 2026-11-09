using System;
using System.Collections.Generic;
using UnityEngine;
using S = Afterfall.Presentation.StructureSlot;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Material slots of structure models. <see cref="StructureVisuals"/> maps every slot to one shared
    /// material; per-part colour lives in the vertex colours, so a whole settlement shares a dozen materials.
    /// </summary>
    public enum StructureSlot
    {
        Timber,
        TimberV,
        PaintedWood,
        Bark,
        BarkH,
        Stone,
        Brick,
        Concrete,
        Metal,
        Paint,
        Rust,
        Corrugated,
        Fabric,
        Rubber,
        Soil,
        Foliage,
        Glass,
        Plain,
        Glow
    }

    /// <summary>
    /// Base tints (sRGB) of the structure materials. Vertex colours are linear multipliers of these:
    /// <see cref="Rel"/> turns a wanted colour into the multiplier for a slot.
    /// </summary>
    public static class StructurePalette
    {
        public static readonly Color Timber = new Color(0.55f, 0.43f, 0.31f);
        public static readonly Color Bark = new Color(0.42f, 0.35f, 0.28f);
        public static readonly Color PaintedWood = new Color(0.88f, 0.87f, 0.84f);
        public static readonly Color Stone = new Color(0.6f, 0.58f, 0.54f);
        public static readonly Color Brick = new Color(0.84f, 0.76f, 0.7f);
        public static readonly Color Concrete = new Color(0.62f, 0.61f, 0.58f);
        public static readonly Color Metal = new Color(0.56f, 0.57f, 0.58f);
        public static readonly Color Paint = new Color(0.92f, 0.92f, 0.92f);
        public static readonly Color Rust = new Color(0.5f, 0.38f, 0.29f);
        public static readonly Color Corrugated = new Color(0.64f, 0.65f, 0.64f);
        public static readonly Color Fabric = new Color(0.88f, 0.87f, 0.85f);
        public static readonly Color Rubber = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color Soil = new Color(0.33f, 0.25f, 0.18f);
        public static readonly Color Foliage = new Color(0.36f, 0.5f, 0.24f);
        public static readonly Color Glass = new Color(0.55f, 0.7f, 0.66f);
        public static readonly Color Plain = new Color(0.9f, 0.9f, 0.9f);

        /// <summary>The vertex colour that turns a material of tint <paramref name="tint"/> into <paramref name="srgb"/>.</summary>
        public static Color Rel(Color srgb, Color tint, float ao = 1f)
        {
            Color a = srgb.linear, b = tint.linear;
            return new Color(a.r / Mathf.Max(0.01f, b.r), a.g / Mathf.Max(0.01f, b.g), a.b / Mathf.Max(0.01f, b.b), ao);
        }
    }

    /// <summary>One named part of a structure model: its pivot and one mesh per material slot (null when unused).</summary>
    public sealed class StructurePart
    {
        public readonly string Name;
        public readonly Vector3 Origin;
        public readonly Mesh[] Layers;

        public StructurePart(string name, Vector3 origin, Mesh[] layers)
        {
            Name = name;
            Origin = origin;
            Layers = layers;
        }
    }

    /// <summary>
    /// Assembles a structure model from many primitives into one mesh per material slot. Geometry is
    /// given in structure-local metres (y = 0 on the ground or the supporting piece, +Z the front).
    /// Named groups become separate parts the game animates (Engine, Fire, LampHead...). A ghost kit
    /// (placement preview, blueprint) merges everything into a single mesh and skips small detail.
    /// </summary>
    public sealed class StructureKit
    {
        private const int SlotCount = (int)StructureSlot.Glow + 1;

        private sealed class GroupData
        {
            public string Name;
            public Vector3 Origin;
            public readonly MeshBuilder[] Layers = new MeshBuilder[SlotCount];
        }

        private readonly List<GroupData> _groups = new List<GroupData>();
        private GroupData _current;

        /// <summary>Simplified single-mesh model for transparent ghosts: no small detail, no animated parts.</summary>
        public readonly bool Ghost;
        /// <summary>Instance variant (0..3) for stable colour and wear differences between copies.</summary>
        public readonly int Variant;
        /// <summary>Placement applied to the following primitives (structure-local space).</summary>
        public Matrix4x4 Matrix = Matrix4x4.identity;
        /// <summary>Darkens and dirties vertices near local y = 0 (things standing on the ground).</summary>
        public bool GroundShade = true;

        public bool Detail => !Ghost;

        public StructureKit(bool ghost, int variant)
        {
            Ghost = ghost;
            Variant = variant;
            _current = new GroupData { Name = "", Origin = Vector3.zero };
            _groups.Add(_current);
        }

        /// <summary>Directs the following primitives into a named part whose pivot is <paramref name="origin"/>. Ghosts stay in one mesh.</summary>
        public void Group(string name, Vector3 origin)
        {
            if (Ghost) return;
            _current = _groups.Find(g => g.Name == name);
            if (_current != null) return;
            _current = new GroupData { Name = name, Origin = origin };
            _groups.Add(_current);
        }

        public void EndGroup() => _current = _groups[0];

        /// <summary>Stable 0..1 random number for this variant.</summary>
        public float Rand(int salt) => MeshFactory.Hash(Variant * 92821 + salt * 131 + 7, new Vector3(salt * 0.731f, Variant * 0.37f + 0.11f, 0.5f));

        /// <summary>Stable 0..1 random number that does not change between variants (shared shapes).</summary>
        public static float Fixed(int salt) => MeshFactory.Hash(salt * 7919 + 3, new Vector3(salt * 0.173f, 0.29f, 0.71f));

        /// <summary>The builder of a slot with the kit's placement and the given vertex colour.</summary>
        public MeshBuilder Layer(StructureSlot slot, Color color)
        {
            int i = Ghost ? 0 : (int)slot;
            MeshBuilder b = _current.Layers[i] ?? (_current.Layers[i] = new MeshBuilder());
            b.Matrix = Matrix4x4.Translate(-_current.Origin) * Matrix;
            b.Color = color;
            return b;
        }

        // ------------------------------------------------------------ primitives

        public void Box(StructureSlot s, Vector3 center, Vector3 size, Color c, float bevel = 0f, Quaternion? rot = null) =>
            Layer(s, c).Box(center, Abs(size), rot, Chamfer(bevel));

        /// <summary>Axis-aligned box between two corners.</summary>
        public void Span(StructureSlot s, Vector3 a, Vector3 b, Color c, float bevel = 0f) =>
            Layer(s, c).Box((a + b) * 0.5f, Abs(b - a), null, Chamfer(bevel));

        /// <summary>Chamfers under 4 mm are invisible and only make degenerate corner faces.</summary>
        private static float Chamfer(float bevel) => bevel < 0.004f ? 0f : bevel;

        /// <summary>
        /// A board, rail or strut between two points: <paramref name="thickness"/> runs along
        /// <paramref name="up"/> (made perpendicular to the beam), <paramref name="width"/> across both.
        /// </summary>
        public void Beam(StructureSlot s, Vector3 a, Vector3 b, Vector3 up, float width, float thickness, Color c, float bevel = 0f)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-8f) return;
            Vector3 u = Vector3.ProjectOnPlane(up, d.normalized);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.forward : Vector3.up, d.normalized);
            Quaternion rot = Quaternion.LookRotation(d.normalized, u.normalized);
            Layer(s, c).Box((a + b) * 0.5f, new Vector3(width, thickness, d.magnitude), rot, bevel);
        }

        public void Cylinder(StructureSlot s, Vector3 a, Vector3 b, float ra, float rb, int sides, Color c, bool smooth = true, bool capA = true, bool capB = true) =>
            Layer(s, c).Cylinder(a, b, ra, rb, sides, smooth, capA, capB);

        public void Sphere(StructureSlot s, Vector3 center, Vector3 radii, Color c, int subdiv = 1, bool smooth = true, Func<Vector3, float> displace = null, Quaternion? rot = null)
        {
            MeshBuilder b = Layer(s, c);
            if (rot.HasValue)
            {
                b.Matrix = b.Matrix * Matrix4x4.TRS(center, rot.Value, Vector3.one);
                b.Sphere(Vector3.zero, radii, subdiv, smooth, displace);
            }
            else b.Sphere(center, radii, subdiv, smooth, displace);
        }

        /// <summary>Surface of revolution around the local Y axis of <paramref name="rot"/>, placed at <paramref name="origin"/>.</summary>
        public void Lathe(StructureSlot s, Vector3 origin, Vector2[] profile, int sides, Color c, bool smooth = true, Quaternion? rot = null)
        {
            MeshBuilder b = Layer(s, c);
            b.Matrix = b.Matrix * Matrix4x4.TRS(origin, rot ?? Quaternion.identity, Vector3.one);
            b.Lathe(profile, sides, smooth);
        }

        /// <summary>
        /// An open surface of revolution without caps (lamp shades, buckets, tank shells): profile
        /// (radius, height) from bottom to top; <paramref name="inward"/> faces it toward the axis.
        /// </summary>
        public void Shell(StructureSlot s, Vector3 origin, Vector2[] profile, int sides, Color c, bool inward = false, Quaternion? rot = null)
        {
            MeshBuilder b = Layer(s, c);
            b.Matrix = b.Matrix * Matrix4x4.TRS(origin, rot ?? Quaternion.identity, Vector3.one);
            int n = profile.Length, row = sides + 1;
            if (n < 2) return;
            var pos = new Vector3[n * row];
            var nrm = new Vector3[n * row];
            for (int p = 0; p < n; p++)
            {
                Vector2 tangent = (profile[Mathf.Min(n - 1, p + 1)] - profile[Mathf.Max(0, p - 1)]).normalized;
                Vector2 pn = new Vector2(tangent.y, -tangent.x) * (inward ? -1f : 1f);
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    float co = Mathf.Cos(a), si = Mathf.Sin(a);
                    pos[p * row + i] = new Vector3(co * profile[p].x, profile[p].y, si * profile[p].x);
                    nrm[p * row + i] = new Vector3(co * pn.x, pn.y, si * pn.x).normalized;
                }
            }
            int q = profile[0].x > 1e-5f ? 0 : row;
            bool flip = Vector3.Dot(Vector3.Cross(pos[q + row] - pos[q], pos[q + row + 1] - pos[q]), nrm[q]) < 0f;
            int start = b.VertexCount;
            for (int k = 0; k < pos.Length; k++) b.Vertex(pos[k], nrm[k]);
            for (int p = 0; p + 1 < n; p++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int a = start + p * row + i, bb = a + 1, cc = a + row, d = cc + 1;
                    if (!flip)
                    {
                        b.Triangle(a, cc, d);
                        b.Triangle(a, d, bb);
                    }
                    else
                    {
                        b.Triangle(a, d, cc);
                        b.Triangle(a, bb, d);
                    }
                }
            }
        }

        public void Face(StructureSlot s, Color c, Vector3 inside, params Vector3[] poly) => Layer(s, c).Face(inside, poly);

        /// <summary>A flat disc facing along <paramref name="normal"/>.</summary>
        public void Disc(StructureSlot s, Vector3 center, Vector3 normal, float radius, int sides, Color c)
        {
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal.normalized);
            var poly = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                poly[i] = center + rot * new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            Layer(s, c).Face(center - normal, poly);
        }

        /// <summary>A circle swept along a path: pipes, cables, rope, handles.</summary>
        public void Tube(StructureSlot s, IList<Vector3> path, float radius, int sides, Color c, bool closed = false, bool caps = true) =>
            MeshFactory.Tube(Layer(s, c), path, radius, sides, closed, caps);

        /// <summary>A torus: rope lashings, hose loops, valve wheels, collars.</summary>
        public void Ring(StructureSlot s, Vector3 center, Vector3 axis, float radius, float thickness, int segments, int sides, Color c)
        {
            Vector3 n = axis.normalized;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u);
            var path = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                path[i] = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            }
            Tube(s, path, thickness, sides, c, true, false);
        }

        /// <summary>
        /// A height field over a rectangle (soil, mounds) with smooth normals; <paramref name="color"/>
        /// gives each vertex its colour (alpha = occlusion).
        /// </summary>
        public void Heightfield(StructureSlot s, float x0, float x1, float z0, float z1, int nx, int nz, Func<float, float, float> height, Func<float, float, float, Color> color)
        {
            MeshBuilder b = Layer(s, Color.white);
            int start = b.VertexCount;
            float dx = (x1 - x0) / nx, dz = (z1 - z0) / nz;
            for (int j = 0; j <= nz; j++)
            {
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * dx, z = z0 + j * dz;
                    float y = height(x, z);
                    float hx = height(x + dx * 0.5f, z) - height(x - dx * 0.5f, z);
                    float hz = height(x, z + dz * 0.5f) - height(x, z - dz * 0.5f);
                    Vector3 n = new Vector3(-hx / dx, 1f, -hz / dz).normalized;
                    b.Vertex(new Vector3(x, y, z), n, color(x, y, z));
                }
            }
            int row = nx + 1;
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    int a = start + j * row + i, c = a + row;
                    // Clockwise seen from above: the normal points up.
                    b.Triangle(a, c, c + 1);
                    b.Triangle(a, c + 1, a + 1);
                }
            }
        }

        /// <summary>
        /// A corrugated sheet lying in the XZ plane with its ridges along Z (crest height <paramref name="y"/> ± amplitude).
        /// The wave phase matches the Corrugated texture in object space (pitch 0.1 m), so relief and normal map agree.
        /// </summary>
        public void CorrugatedSheet(StructureSlot s, float x0, float x1, float z0, float z1, float y, float amplitude, float pitch, float thickness, Color c)
        {
            MeshBuilder b = Layer(s, c);
            int steps = Mathf.Max(2, Mathf.CeilToInt((x1 - x0) / pitch * 8f));
            var top = new Vector3[steps + 1];
            var nrm = new Vector3[steps + 1];
            float k = Mathf.PI * 2f / pitch;
            for (int i = 0; i <= steps; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)steps);
                top[i] = new Vector3(x, y + amplitude * Mathf.Sin(k * x), 0f);
                nrm[i] = new Vector3(-amplitude * k * Mathf.Cos(k * x), 1f, 0f).normalized;
            }
            Vector3 down = Vector3.down * thickness;
            int start = b.VertexCount;
            for (int i = 0; i <= steps; i++)
            {
                b.Vertex(top[i] + Vector3.forward * z0, nrm[i]);
                b.Vertex(top[i] + Vector3.forward * z1, nrm[i]);
                b.Vertex(top[i] + down + Vector3.forward * z0, -nrm[i]);
                b.Vertex(top[i] + down + Vector3.forward * z1, -nrm[i]);
            }
            for (int i = 0; i < steps; i++)
            {
                int a = start + i * 4, n = a + 4;
                // Top: clockwise from above (x right, z up the screen).
                b.Triangle(a, a + 1, n + 1);
                b.Triangle(a, n + 1, n);
                // Underside: the reverse.
                b.Triangle(a + 2, n + 3, a + 3);
                b.Triangle(a + 2, n + 2, n + 3);
            }
            // Wavy end edges and the two straight side edges.
            Vector3 inside = new Vector3((x0 + x1) * 0.5f, y - thickness * 0.5f, (z0 + z1) * 0.5f);
            for (int i = 0; i < steps; i++)
            {
                foreach (float z in new[] { z0, z1 })
                {
                    Vector3 o = Vector3.forward * z;
                    b.Face(inside, top[i] + o, top[i + 1] + o, top[i + 1] + down + o, top[i] + down + o);
                }
            }
            foreach (int i in new[] { 0, steps })
                b.Face(inside, top[i] + Vector3.forward * z0, top[i] + Vector3.forward * z1, top[i] + down + Vector3.forward * z1, top[i] + down + Vector3.forward * z0);
        }

        // ------------------------------------------------------------ build

        internal StructurePart[] Build(string name)
        {
            var parts = new List<StructurePart>();
            foreach (GroupData g in _groups)
            {
                var layers = new Mesh[SlotCount];
                bool any = false;
                for (int i = 0; i < SlotCount; i++)
                {
                    MeshBuilder b = g.Layers[i];
                    if (b == null || b.VertexCount == 0) continue;
                    Mesh m = b.Build($"{name}/{g.Name}/{(StructureSlot)i}");
                    SanitizeNormals(m);
                    if (GroundShade) ShadeGround(m, g.Origin.y, (StructureSlot)i);
                    layers[i] = m;
                    any = true;
                }
                if (any) parts.Add(new StructurePart(g.Name, g.Origin, layers));
            }
            return parts.ToArray();
        }

        /// <summary>
        /// Millimetre-sized faces (bevel corners of thin trims, bolt flanks) can come out of the builder
        /// with a zero normal, which the lighting turns into NaN and bloom into a white blob.
        /// </summary>
        private static void SanitizeNormals(Mesh m)
        {
            Vector3[] n = m.normals;
            bool bad = false;
            for (int i = 0; i < n.Length; i++)
            {
                if (n[i].sqrMagnitude > 0.25f) continue;
                n[i] = Vector3.up;
                bad = true;
            }
            if (bad) m.normals = n;
        }

        /// <summary>Contact occlusion and grime near the ground: mossy on wood and stone, rust-brown on metal.</summary>
        private static void ShadeGround(Mesh m, float originY, StructureSlot slot)
        {
            if (slot == StructureSlot.Glow || slot == StructureSlot.Glass || slot == StructureSlot.Foliage) return;
            Vector3[] v = m.vertices;
            Color[] c = m.colors;
            bool organic = slot == StructureSlot.Timber || slot == StructureSlot.TimberV || slot == StructureSlot.Bark || slot == StructureSlot.BarkH || slot == StructureSlot.Stone || slot == StructureSlot.Brick || slot == StructureSlot.Concrete;
            Color grime = organic ? new Color(0.6f, 0.64f, 0.46f) : new Color(0.62f, 0.5f, 0.38f);
            for (int i = 0; i < v.Length; i++)
            {
                float y = v[i].y + originY;
                float ao = Mathf.Lerp(0.58f, 1f, Smooth01(y / 0.55f));
                float g = Smooth01(y / 0.32f);
                c[i] = new Color(c[i].r * Mathf.Lerp(grime.r, 1f, g), c[i].g * Mathf.Lerp(grime.g, 1f, g), c[i].b * Mathf.Lerp(grime.b, 1f, g), c[i].a * ao);
            }
            m.colors = c;
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    public static partial class MeshFactory
    {
        private static readonly Dictionary<string, StructurePart[]> StructureCache = new Dictionary<string, StructurePart[]>();

        /// <summary>
        /// The parts of a structure model, built once per key, variant and ghost flag. <paramref name="key"/>
        /// must name everything the model depends on (the building definition id does).
        /// </summary>
        public static StructurePart[] StructureModel(string key, bool ghost, int variant, Action<StructureKit> build)
        {
            string full = $"{key}:{variant}:{(ghost ? "ghost" : "full")}";
            if (StructureCache.TryGetValue(full, out StructurePart[] parts) && parts.Length > 0 && FirstMesh(parts[0]) != null) return parts;
            var kit = new StructureKit(ghost, variant);
            build(kit);
            parts = kit.Build(full);
            StructureCache[full] = parts;
            return parts;
        }

        private static Mesh FirstMesh(StructurePart p)
        {
            foreach (Mesh m in p.Layers)
                if (m != null) return m;
            return null;
        }

        /// <summary>
        /// Sweeps a circle of <paramref name="radius"/> along <paramref name="path"/> with smooth normals
        /// (parallel-transported frames, so bends do not twist). Open ends are capped flat.
        /// </summary>
        public static void Tube(MeshBuilder b, IList<Vector3> path, float radius, int sides, bool closed = false, bool caps = true)
        {
            int n = path.Count;
            if (n < 2) return;
            var tangent = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = closed ? path[(i - 1 + n) % n] : path[Mathf.Max(0, i - 1)];
                Vector3 next = closed ? path[(i + 1) % n] : path[Mathf.Min(n - 1, i + 1)];
                tangent[i] = (next - prev).normalized;
            }
            Vector3 normal = Vector3.Cross(tangent[0], Mathf.Abs(tangent[0].y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            int row = sides + 1;
            var pos = new Vector3[n * row];
            var nrm = new Vector3[n * row];
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    normal = Quaternion.FromToRotation(tangent[i - 1], tangent[i]) * normal;
                    normal = Vector3.ProjectOnPlane(normal, tangent[i]).normalized;
                }
                Vector3 binormal = Vector3.Cross(tangent[i], normal);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    Vector3 d = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    pos[i * row + s] = path[i] + d * radius;
                    nrm[i * row + s] = d;
                }
            }
            // The first quad decides the winding for all of them (front faces clockwise from outside).
            bool flip = Vector3.Dot(Vector3.Cross(pos[row] - pos[0], pos[row + 1] - pos[0]), nrm[0]) < 0f;
            int start = b.VertexCount;
            for (int k = 0; k < pos.Length; k++) b.Vertex(pos[k], nrm[k]);
            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                int i1 = (i + 1) % n;
                for (int s = 0; s < sides; s++)
                {
                    int a = start + i * row + s, bb = a + 1, c = start + i1 * row + s, d = c + 1;
                    if (!flip)
                    {
                        b.Triangle(a, c, d);
                        b.Triangle(a, d, bb);
                    }
                    else
                    {
                        b.Triangle(a, d, c);
                        b.Triangle(a, bb, d);
                    }
                }
            }
            if (!caps || closed) return;
            var ring = new Vector3[sides];
            for (int s = 0; s < sides; s++) ring[s] = pos[s];
            b.Face(path[0] + tangent[0], ring);
            for (int s = 0; s < sides; s++) ring[s] = pos[(n - 1) * row + s];
            b.Face(path[n - 1] - tangent[n - 1], ring);
        }

        /// <summary>Replaces every inner corner of a polyline with a smooth bend of the given radius (pipes, cables).</summary>
        public static List<Vector3> RoundCorners(IList<Vector3> points, float radius, int segments = 5)
        {
            var result = new List<Vector3> { points[0] };
            for (int i = 1; i + 1 < points.Count; i++)
            {
                Vector3 p = points[i], a = points[i - 1], c = points[i + 1];
                float r = Mathf.Min(radius, Vector3.Distance(a, p) * 0.5f, Vector3.Distance(c, p) * 0.5f);
                Vector3 p0 = p + (a - p).normalized * r, p2 = p + (c - p).normalized * r;
                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    result.Add((1 - t) * (1 - t) * p0 + 2f * (1 - t) * t * p + t * t * p2);
                }
            }
            result.Add(points[points.Count - 1]);
            return result;
        }
    }

    /// <summary>
    /// Geometry of player-built pieces and world containers, in structure-local metres (y = 0 at the
    /// base, +Z the front, footprint centred on the origin). Machines and furniture continue in
    /// StructureVisuals.cs. Colours are linear multipliers of the slot tints (<see cref="StructurePalette"/>).
    /// </summary>
    internal static partial class StructureModels
    {
        // ------------------------------------------------------------ colour helpers

        /// <summary>A grey multiplier (linear) with optional occlusion.</summary>
        internal static Color T(float k, float ao = 1f) => new Color(k, k, k, ao);

        internal static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        internal static Color Tint(Color c, float r, float g, float b) => new Color(c.r * r, c.g * g, c.b * b, c.a);

        /// <summary>The vertex colour that paints the Paint slot in an sRGB colour.</summary>
        internal static Color Paint(float r, float g, float b, float ao = 1f) => StructurePalette.Rel(new Color(r, g, b), StructurePalette.Paint, ao);

        internal static Color Paint(Color srgb, float ao = 1f) => StructurePalette.Rel(srgb, StructurePalette.Paint, ao);

        internal static Color Rel(Color srgb, Color tint, float ao = 1f) => StructurePalette.Rel(srgb, tint, ao);

        /// <summary>Timber multiplier for a definition colour, kept within a believable range of wood tones.</summary>
        internal static Color WoodTone(Color srgb)
        {
            Color c = StructurePalette.Rel(srgb, StructurePalette.Timber);
            return new Color(Mathf.Clamp(c.r, 0.5f, 1.5f), Mathf.Clamp(c.g, 0.5f, 1.5f), Mathf.Clamp(c.b, 0.5f, 1.5f), 1f);
        }

        /// <summary>One board's tone: a brightness spread, some boards sun-bleached grey, a few dark and oily.</summary>
        internal static Color Board(StructureKit k, int salt, Color tone)
        {
            Color c = Scale(tone, 0.7f + 0.42f * k.Rand(salt));
            float w = k.Rand(salt + 5003);
            if (w < 0.25f) c = Tint(c, 0.86f, 0.95f, 1.1f);
            else if (w > 0.9f) c = Tint(c, 0.78f, 0.7f, 0.6f);
            c.a = 1f;
            return c;
        }

        /// <summary>Variant-independent 0..1 noise (shapes that must not change between copies).</summary>
        internal static float Fixed(int salt) => StructureKit.Fixed(salt);

        /// <summary>A small round nail or screw head on a surface facing <paramref name="normal"/>.</summary>
        internal static void Nail(StructureKit k, Vector3 p, Vector3 normal, float radius = 0.007f)
        {
            k.Cylinder(S.Metal, p - normal * 0.004f, p + normal * 0.0025f, radius, radius * 0.9f, 6, T(0.32f), false, false, true);
        }

        /// <summary>A hex bolt head on a washer.</summary>
        internal static void Bolt(StructureKit k, Vector3 p, Vector3 normal, float radius = 0.012f)
        {
            k.Cylinder(S.Metal, p - normal * 0.002f, p + normal * 0.002f, radius * 1.35f, radius * 1.35f, 10, T(0.55f), true, false, true);
            k.Cylinder(S.Metal, p, p + normal * radius * 0.7f, radius, radius, 6, T(0.42f), false, false, true);
        }

        // ------------------------------------------------------------ foundation

        /// <summary>Plank deck on timber sills, on a fieldstone plinth that reaches below grade for slopes.</summary>
        public static void Foundation(StructureKit k, float sx, float sy, float sz, Color tone)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            const float stoneTop = 0.12f, sw = 0.22f;
            float deckBottom = sy - 0.08f;
            k.Span(S.Stone, new Vector3(-hx + 0.05f, -1.2f, -hz + 0.05f), new Vector3(hx - 0.05f, stoneTop, hz - 0.05f), T(0.92f), 0.04f);
            if (k.Detail)
            {
                // Big corner quoins break the plinth's straight line.
                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0 ? -1f : 1f) * (hx - 0.22f), z = (i < 2 ? -1f : 1f) * (hz - 0.22f);
                    k.Box(S.Stone, new Vector3(x, -0.04f, z), new Vector3(0.44f, 0.33f, 0.44f), T(0.75f + 0.25f * k.Rand(i + 50)), 0.05f, Quaternion.Euler(0f, (k.Rand(i + 60) - 0.5f) * 8f, 0f));
                }
            }

            Color sill = Scale(tone, 0.72f);
            k.Span(S.Timber, new Vector3(-hx, stoneTop, hz - sw), new Vector3(hx, deckBottom, hz), Board(k, 1, sill), 0.015f);
            k.Span(S.Timber, new Vector3(-hx, stoneTop, -hz), new Vector3(hx, deckBottom, -hz + sw), Board(k, 2, sill), 0.015f);
            k.Span(S.Timber, new Vector3(hx - sw, stoneTop, -hz + sw), new Vector3(hx, deckBottom, hz - sw), Board(k, 3, sill), 0.015f);
            k.Span(S.Timber, new Vector3(-hx, stoneTop, -hz + sw), new Vector3(-hx + sw, deckBottom, hz - sw), Board(k, 4, sill), 0.015f);
            if (k.Detail)
                foreach (float x in new[] { -0.75f, 0f, 0.75f })
                    k.Span(S.Timber, new Vector3(x - 0.06f, stoneTop, -hz + sw), new Vector3(x + 0.06f, deckBottom, hz - sw), Scale(sill, 0.6f));

            const int boards = 14;
            float pitch = sz / boards;
            for (int i = 0; i < boards; i++)
            {
                float z = -hz + pitch * (i + 0.5f);
                float trim = k.Rand(i * 7 + 100) * 0.03f;
                bool left = k.Rand(i * 7 + 101) > 0.5f;
                float top = sy - k.Rand(i * 7 + 102) * 0.005f;
                k.Span(S.Timber, new Vector3(-hx + (left ? trim : 0f), deckBottom, z - pitch * 0.5f + 0.006f), new Vector3(hx - (left ? 0f : trim), top, z + pitch * 0.5f - 0.006f), Board(k, i + 10, tone), 0.012f);
                if (!k.Detail) continue;
                int line = 0;
                foreach (float x in new[] { -hx + sw * 0.5f, 0f, hx - sw * 0.5f })
                {
                    foreach (float dz in new[] { -0.05f, 0.05f })
                        Nail(k, new Vector3(x + (k.Rand(i * 13 + line + 300) - 0.5f) * 0.03f, top, z + dz), Vector3.up);
                    line++;
                }
            }
        }

        // ------------------------------------------------------------ walls

        private const float Liner = 0.025f;

        /// <summary>
        /// Timber-framed wall clad in lapped boards outside, with exposed studs inside. An opening of
        /// <paramref name="openW"/> × (<paramref name="openBottom"/>..<paramref name="openTop"/>) makes a
        /// doorway (bottom 0) or a window with casings, sill, glazing bars and shutters.
        /// </summary>
        public static void Wall(StructureKit k, float sx, float sy, float sz, Color tone, float openW, float openBottom, float openTop)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            bool opening = openW > 0f, window = opening && openBottom > 0f;
            float ow = openW * 0.5f, inner = hx - hz, jx = ow + Liner;
            const float studZ0 = -0.105f, studZ1 = 0.09f, jamb = 0.12f;

            // Corner posts: the same shape and colour in every wall, so the post two walls share at a
            // corner draws identically from both.
            foreach (float x in new[] { -hx, hx })
                k.Box(S.TimberV, new Vector3(x, sy * 0.5f, 0f), new Vector3(sz, sy, sz), Scale(tone, 0.78f), 0.025f);

            Color frame = Scale(tone, 0.86f);
            if (opening && !window)
            {
                k.Span(S.Timber, new Vector3(-inner, 0f, studZ0), new Vector3(-jx, 0.1f, studZ1), Board(k, 1, frame), 0.01f);
                k.Span(S.Timber, new Vector3(jx, 0f, studZ0), new Vector3(inner, 0.1f, studZ1), Board(k, 1, frame), 0.01f);
            }
            else k.Span(S.Timber, new Vector3(-inner, 0f, studZ0), new Vector3(inner, 0.1f, studZ1), Board(k, 1, frame), 0.01f);
            k.Span(S.Timber, new Vector3(-inner, sy - 0.16f, studZ0 - 0.01f), new Vector3(inner, sy, studZ1), Board(k, 2, frame), 0.012f);

            float[] studs = !opening ? new[] { -0.75f, 0f, 0.75f } : window ? new[] { -1.06f, 1.06f } : new[] { -1.17f, 1.17f };
            for (int i = 0; i < studs.Length; i++)
                k.Span(S.TimberV, new Vector3(studs[i] - 0.045f, 0.1f, studZ0), new Vector3(studs[i] + 0.045f, sy - 0.16f, studZ1), Board(k, 10 + i, frame), 0.008f);

            if (!opening)
            {
                // Let-in diagonal brace, proud of the studs on the inside.
                k.Beam(S.Timber, new Vector3(-1.3f, 0.14f, -0.116f), new Vector3(-0.12f, sy - 0.2f, -0.116f), Vector3.forward, 0.1f, 0.022f, Board(k, 40, frame), 0.006f);
            }
            else
            {
                foreach (float side in new[] { -1f, 1f })
                    k.Span(S.TimberV, new Vector3(side * jx, window ? 0.1f : 0f, studZ0), new Vector3(side * (jx + jamb), sy - 0.16f, studZ1), Board(k, side > 0f ? 20 : 21, frame), 0.008f);
                k.Span(S.Timber, new Vector3(-jx - jamb, openTop + Liner, studZ0), new Vector3(jx + jamb, openTop + Liner + 0.16f, studZ1), Board(k, 22, frame), 0.01f);
                k.Span(S.TimberV, new Vector3(-0.045f, openTop + Liner + 0.16f, studZ0), new Vector3(0.045f, sy - 0.16f, studZ1), Board(k, 23, frame), 0.008f);
                if (window)
                {
                    k.Span(S.Timber, new Vector3(-jx - jamb, openBottom - Liner - 0.1f, studZ0), new Vector3(jx + jamb, openBottom - Liner, studZ1), Board(k, 24, frame), 0.01f);
                    k.Span(S.TimberV, new Vector3(-0.045f, 0.1f, studZ0), new Vector3(0.045f, openBottom - Liner - 0.1f, studZ1), Board(k, 25, frame), 0.008f);
                }
                // Full-thickness liners frame the opening.
                float yb = window ? openBottom : 0f;
                Color lining = Scale(tone, 0.95f);
                foreach (float side in new[] { -1f, 1f })
                    k.Span(S.TimberV, new Vector3(side * ow, yb, -hz), new Vector3(side * jx, openTop, hz), Board(k, side > 0f ? 26 : 27, lining), 0.004f);
                k.Span(S.Timber, new Vector3(-jx, openTop, -hz), new Vector3(jx, openTop + Liner, hz), Board(k, 28, lining), 0.004f);
                if (window) k.Span(S.Timber, new Vector3(-jx, openBottom - Liner, -hz), new Vector3(jx, openBottom, hz), Board(k, 29, lining), 0.004f);
            }

            Siding(k, hz, inner, sy, tone, opening, window ? openBottom : 0f, openTop, jx, studs);

            // Vertical sheathing behind the siding: the inside face between the studs, and a second
            // layer so low sun cannot leak through the laps in the shadow map.
            float sy0 = 0.1f, sy1 = sy - 0.16f;
            if (k.Detail) Sheathing(k, -inner, opening ? -jx : inner, sy0, sy1, tone, 300);
            if (opening && k.Detail)
            {
                Sheathing(k, jx, inner, sy0, sy1, tone, 330);
                Sheathing(k, -jx, jx, openTop + Liner, sy1, tone, 360);
                if (window) Sheathing(k, -jx, jx, sy0, openBottom - Liner, tone, 390);
            }

            if (!opening) return;
            Trim(k, hz, ow, openTop, window ? openBottom : 0f, tone, 1f);
            Trim(k, hz, ow, openTop, window ? openBottom : 0f, tone, -1f);
            if (window)
            {
                float mid = (openBottom + openTop) * 0.5f;
                Color trim = Scale(tone, 0.7f);
                k.Span(S.Timber, new Vector3(-jx - 0.16f, openBottom - 0.045f, hz - 0.03f), new Vector3(jx + 0.16f, openBottom + 0.012f, hz + 0.07f), Board(k, 50, trim), 0.008f);
                k.Span(S.Timber, new Vector3(-jx - 0.11f, openBottom - 0.2f, hz - 0.004f), new Vector3(jx + 0.11f, openBottom - 0.045f, hz + 0.022f), Board(k, 51, trim), 0.006f);
                k.Span(S.Timber, new Vector3(-jx - 0.06f, openBottom - 0.03f, -hz - 0.06f), new Vector3(jx + 0.06f, openBottom + 0.012f, -hz + 0.03f), Board(k, 52, trim), 0.008f);
                // Glazing bars (the glass is long gone).
                k.Span(S.TimberV, new Vector3(-0.024f, openBottom, -0.03f), new Vector3(0.024f, openTop, 0.03f), Board(k, 53, trim), 0.006f);
                k.Span(S.Timber, new Vector3(-ow, mid - 0.022f, -0.027f), new Vector3(ow, mid + 0.022f, 0.027f), Board(k, 54, trim), 0.006f);
                if (k.Detail)
                {
                    float hinge = jx + 0.11f, h = openTop - openBottom + 0.02f;
                    Shutter(k, new Vector3(-hinge, openBottom + 0.002f, hz + 0.012f), -1f, ow + 0.02f, h, k.Rand(55) * 6f, 60, tone);
                    Shutter(k, new Vector3(hinge, openBottom + 0.002f, hz + 0.012f), 1f, ow + 0.02f, h, 14f + k.Rand(56) * 12f, 70, tone);
                }
            }
            else if (k.Detail)
            {
                // The door is gone; its strap hinges are still on the jamb.
                foreach (float y in new[] { 0.32f, 1.95f })
                {
                    k.Span(S.Rust, new Vector3(-ow - 0.01f, y - 0.03f, -hz - 0.006f), new Vector3(-ow + 0.012f, y + 0.03f, -hz + 0.004f), T(0.8f), 0.002f);
                    k.Cylinder(S.Rust, new Vector3(-ow + 0.008f, y - 0.045f, -hz - 0.004f), new Vector3(-ow + 0.008f, y + 0.045f, -hz - 0.004f), 0.011f, 0.011f, 8, T(0.7f));
                }
            }
        }

        /// <summary>Lapped clapboards between the corner posts, cut around the opening.</summary>
        private static void Siding(StructureKit k, float hz, float inner, float sy, Color tone, bool opening, float openBottom, float openTop, float jx, float[] studs)
        {
            const int boards = 13;
            // Thick enough that each board's back meets the one below: no light between the laps.
            const float bh = 0.25f, lean = 0.028f, t = 0.028f;
            float pitch = (sy - 0.04f - bh) / (boards - 1);
            Quaternion rot = Quaternion.Euler(-Mathf.Atan2(lean, bh) * Mathf.Rad2Deg, 0f, 0f);
            Vector3 n = rot * Vector3.forward;
            for (int i = 0; i < boards; i++)
            {
                float yb = 0.02f + i * pitch, yt = yb + bh;
                Color c = Board(k, 100 + i, tone);
                if (i == 0) c = Tint(c, 0.82f, 0.88f, 0.72f);
                bool cut = opening && yb < openTop && yt > openBottom;
                if (!cut) SidingBoard(k, -inner, inner, yb, yt, yb, hz, rot, n, t, lean, bh, c);
                else
                {
                    SidingBoard(k, -inner, -jx, yb, yt, yb, hz, rot, n, t, lean, bh, c);
                    SidingBoard(k, jx, inner, yb, yt, yb, hz, rot, n, t, lean, bh, Board(k, 140 + i, tone));
                    // Short pieces above and below the opening.
                    if (yt > openTop + Liner) SidingBoard(k, -jx, jx, Mathf.Max(yb, openTop + Liner), yt, yb, hz, rot, n, t, lean, bh, c);
                    if (openBottom > 0f && yb < openBottom - Liner) SidingBoard(k, -jx, jx, yb, Mathf.Min(yt, openBottom - Liner), yb, hz, rot, n, t, lean, bh, c);
                }
                if (!k.Detail) continue;
                foreach (float x in studs)
                    if (!cut || Mathf.Abs(x) > jx + 0.05f) Nail(k, new Vector3(x, yb + 0.04f, hz - lean * 0.16f + 0.001f), n, 0.006f);
            }
        }

        /// <summary>Vertical boards about 20 cm wide filling x0..x1, y0..y1 just behind the siding.</summary>
        private static void Sheathing(StructureKit k, float x0, float x1, float y0, float y1, Color tone, int salt)
        {
            if (x1 - x0 < 0.02f || y1 - y0 < 0.02f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 0.2f));
            float w = (x1 - x0) / n;
            for (int i = 0; i < n; i++)
                k.Span(S.TimberV, new Vector3(x0 + i * w + 0.003f, y0, 0.048f), new Vector3(x0 + (i + 1) * w - 0.003f, y1, 0.07f), Board(k, salt + i, Scale(tone, 0.9f)), 0.004f);
        }

        /// <summary>
        /// One clapboard from x0 to x1 covering heights y0..y1 of a board whose full height starts at
        /// <paramref name="boardBottom"/> (its bottom edge stands out at z = hz, the top leans in).
        /// </summary>
        private static void SidingBoard(StructureKit k, float x0, float x1, float y0, float y1, float boardBottom, float hz, Quaternion rot, Vector3 n, float t, float lean, float bh, Color c)
        {
            if (x1 - x0 < 0.01f || y1 - y0 < 0.01f) return;
            float ym = (y0 + y1) * 0.5f;
            float zFace = hz - lean * (ym - boardBottom) / bh;
            Vector3 center = new Vector3((x0 + x1) * 0.5f, ym, zFace) - n * (t * 0.5f);
            k.Box(S.Timber, center, new Vector3(x1 - x0, y1 - y0, t), c, 0.005f, rot);
        }

        /// <summary>Casings round the opening on one face (+1 outside, −1 inside).</summary>
        private static void Trim(StructureKit k, float hz, float ow, float openTop, float openBottom, Color tone, float face)
        {
            const float cw = 0.1f, ct = 0.022f, reveal = 0.008f;
            Color trim = Scale(tone, face > 0f ? 0.7f : 0.8f);
            float z0 = face * (hz - 0.004f), z1 = face * (hz + ct);
            float yb = openBottom > 0f ? openBottom + 0.012f : 0f;
            float xi = ow + reveal, xo = ow + Liner + cw;
            foreach (float side in new[] { -1f, 1f })
                k.Span(S.TimberV, new Vector3(side * xi, yb, z0), new Vector3(side * xo, openTop + reveal, z1), Board(k, side > 0f ? 80 : 81, trim), 0.006f);
            k.Span(S.Timber, new Vector3(-xo - 0.02f, openTop + reveal, z0), new Vector3(xo + 0.02f, openTop + reveal + 0.13f, z1), Board(k, 82, trim), 0.006f);
            if (face > 0f)
                k.Span(S.Timber, new Vector3(-xo - 0.04f, openTop + reveal + 0.13f, z0), new Vector3(xo + 0.04f, openTop + reveal + 0.155f, hz + ct + 0.03f), Board(k, 83, Scale(trim, 0.85f)), 0.006f);
        }

        /// <summary>
        /// A board shutter hinged at <paramref name="hinge"/>, folded back flat against the wall toward
        /// <paramref name="dir"/> (±X) and swung <paramref name="ajar"/> degrees away from it.
        /// </summary>
        private static void Shutter(StructureKit k, Vector3 hinge, float dir, float width, float height, float ajar, int salt, Color tone)
        {
            Matrix4x4 saved = k.Matrix;
            k.Matrix = saved * Matrix4x4.TRS(hinge, Quaternion.Euler(0f, -dir * ajar, 0f), Vector3.one);
            const int boards = 3;
            float bw = width / boards;
            for (int b = 0; b < boards; b++)
                k.Span(S.TimberV, new Vector3(dir * (b * bw + 0.004f), 0f, 0f), new Vector3(dir * ((b + 1) * bw - 0.004f), height, 0.022f), Board(k, salt + b, tone), 0.004f);
            Color batten = Scale(tone, 0.8f);
            k.Span(S.Timber, new Vector3(dir * 0.03f, 0.12f, 0.022f), new Vector3(dir * (width - 0.03f), 0.22f, 0.042f), Board(k, salt + 5, batten), 0.005f);
            k.Span(S.Timber, new Vector3(dir * 0.03f, height - 0.22f, 0.022f), new Vector3(dir * (width - 0.03f), height - 0.12f, 0.042f), Board(k, salt + 6, batten), 0.005f);
            k.Beam(S.Timber, new Vector3(dir * 0.08f, 0.22f, 0.032f), new Vector3(dir * (width - 0.08f), height - 0.22f, 0.032f), Vector3.forward, 0.08f, 0.018f, Board(k, salt + 7, batten), 0.004f);
            foreach (float y in new[] { 0.17f, height - 0.17f })
            {
                k.Span(S.Rust, new Vector3(-dir * 0.015f, y - 0.018f, 0.042f), new Vector3(dir * 0.26f, y + 0.018f, 0.048f), T(0.75f), 0.002f);
                k.Cylinder(S.Rust, new Vector3(-dir * 0.015f, y - 0.04f, 0.03f), new Vector3(-dir * 0.015f, y + 0.04f, 0.03f), 0.01f, 0.01f, 8, T(0.7f));
            }
            k.Matrix = saved;
        }

        // ------------------------------------------------------------ roof

        /// <summary>Corrugated sheets on purlins over a boarded ceiling with exposed beams, framed by fascia boards.</summary>
        public static void Roof(StructureKit k, float sx, float sy, float sz, Color tone)
        {
            k.GroundShade = false;
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color beam = Scale(tone, 1.25f);
            float[] beams = { -hz + 0.22f, -0.42f, 0.42f, hz - 0.22f };
            for (int i = 0; i < beams.Length; i++)
                k.Span(S.Timber, new Vector3(-hx + 0.03f, 0f, beams[i] - 0.05f), new Vector3(hx - 0.03f, 0.12f, beams[i] + 0.05f), Board(k, i, beam), 0.012f);
            const int boards = 12;
            float pitch = sx / boards;
            for (int i = 0; i < boards; i++)
            {
                float x = -hx + pitch * (i + 0.5f);
                k.Span(S.TimberV, new Vector3(x - pitch * 0.5f + 0.005f, 0.12f, -hz + 0.03f), new Vector3(x + pitch * 0.5f - 0.005f, 0.145f, hz - 0.03f), Board(k, 10 + i, Scale(tone, 1.4f)), 0.004f);
            }
            int purlin = 0;
            foreach (float z in new[] { -1.2f, 0f, 1.2f })
                k.Span(S.Timber, new Vector3(-hx + 0.03f, 0.145f, z - 0.035f), new Vector3(hx - 0.03f, 0.19f, z + 0.035f), Board(k, 30 + purlin++, beam), 0.008f);
            Color fascia = Scale(tone, 1.1f);
            k.Span(S.Timber, new Vector3(-hx, 0f, hz - 0.028f), new Vector3(hx, 0.19f, hz), Board(k, 40, fascia), 0.006f);
            k.Span(S.Timber, new Vector3(-hx, 0f, -hz), new Vector3(hx, 0.19f, -hz + 0.028f), Board(k, 41, fascia), 0.006f);
            k.Span(S.Timber, new Vector3(hx - 0.028f, 0f, -hz + 0.028f), new Vector3(hx, 0.19f, hz - 0.028f), Board(k, 42, fascia), 0.006f);
            k.Span(S.Timber, new Vector3(-hx, 0f, -hz + 0.028f), new Vector3(-hx + 0.028f, 0.19f, hz - 0.028f), Board(k, 43, fascia), 0.006f);

            // Three overlapping sheets, the middle one lapped over its neighbours; weathered unevenly.
            const float amp = 0.011f, wave = 0.1f, overlap = 0.06f;
            float w = (sx + 2f * overlap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x0 = -hx + i * (w - overlap), x1 = x0 + w;
                float y = 0.205f + (i == 1 ? 0.004f : 0f);
                // Galvanised, weathered or a salvaged sheet gone rust-brown.
                float r = k.Rand(200 + i);
                Color c = r < 0.35f ? T(1f) : r < 0.72f ? new Color(0.92f, 0.8f, 0.68f) : new Color(0.78f, 0.48f, 0.3f);
                k.CorrugatedSheet(S.Corrugated, x0, x1, -hz, hz, y, amp, wave, 0.003f, c);
                if (!k.Detail) continue;
                // Screws on every other crest along the purlins.
                for (float x = Mathf.Ceil((x0 + 0.03f - 0.025f) / wave) * wave + 0.025f; x < x1 - 0.03f; x += wave * 2f)
                    foreach (float z in new[] { -1.2f, 0f, 1.2f })
                        k.Cylinder(S.Metal, new Vector3(x, y + amp - 0.001f, z), new Vector3(x, y + amp + 0.006f, z), 0.009f, 0.008f, 6, T(0.45f), false, false, true);
            }
        }

        // ------------------------------------------------------------ crate

        /// <summary>
        /// Board crate with corner battens, rims, diagonal braces and a cleated lid. Storage crates carry
        /// rope handles; supply crates steel straps and a stencilled panel.
        /// </summary>
        public static void Crate(StructureKit k, float sx, float sy, float sz, Color tone, bool supply)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            const float bt = 0.025f, bw = 0.07f, t = 0.02f, lid = 0.045f;
            float top = sy - lid;
            const int rows = 3;
            float rh = top / rows;
            for (int r = 0; r < rows; r++)
            {
                float y0 = r * rh + 0.003f, y1 = (r + 1) * rh - 0.003f;
                foreach (float zs in new[] { -1f, 1f })
                    k.Span(S.Timber, new Vector3(-hx + bt, y0, zs * (hz - bt - t)), new Vector3(hx - bt, y1, zs * (hz - bt)), Board(k, r * 4 + (zs > 0f ? 0 : 1), tone), 0.004f);
                foreach (float xs in new[] { -1f, 1f })
                    k.Span(S.Timber, new Vector3(xs * (hx - bt - t), y0, -hz + bt + t), new Vector3(xs * (hx - bt), y1, hz - bt - t), Board(k, r * 4 + (xs > 0f ? 2 : 3), tone), 0.004f);
            }
            k.Span(S.Timber, new Vector3(-hx + bt, 0f, -hz + bt), new Vector3(hx - bt, 0.02f, hz - bt), Scale(tone, 0.6f));

            Color frame = Scale(tone, 0.82f);
            int salt = 20;
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                {
                    k.Span(S.TimberV, new Vector3(xs * (hx - bw), 0f, zs * (hz - bt)), new Vector3(xs * hx, top, zs * hz), Board(k, salt++, frame), 0.006f);
                    k.Span(S.TimberV, new Vector3(xs * (hx - bt), 0f, zs * (hz - bw)), new Vector3(xs * hx, top, zs * (hz - bt)), Board(k, salt++, frame), 0.006f);
                }
            foreach (float y in new[] { 0f, top - bw })
            {
                foreach (float zs in new[] { -1f, 1f })
                    k.Span(S.Timber, new Vector3(-hx + bw, y, zs * (hz - bt)), new Vector3(hx - bw, y + bw, zs * hz), Board(k, salt++, frame), 0.006f);
                foreach (float xs in new[] { -1f, 1f })
                    k.Span(S.Timber, new Vector3(xs * (hx - bt), y, -hz + bw), new Vector3(xs * hx, y + bw, hz - bw), Board(k, salt++, frame), 0.006f);
            }
            foreach (float zs in new[] { -1f, 1f })
            {
                float z = zs * (hz - bt * 0.5f - 0.003f);
                k.Beam(S.Timber, new Vector3(-hx + bw * 0.6f, bw * 0.6f, z), new Vector3(hx - bw * 0.6f, top - bw * 0.6f, z), Vector3.forward, 0.065f, bt - 0.006f, Board(k, salt++, frame), 0.005f);
            }

            const int lidBoards = 4;
            float lp = sz / lidBoards;
            for (int i = 0; i < lidBoards; i++)
                k.Span(S.Timber, new Vector3(-hx, top, -hz + i * lp + 0.003f), new Vector3(hx, top + 0.025f, -hz + (i + 1) * lp - 0.003f), Board(k, 60 + i, tone), 0.005f);
            foreach (float xs in new[] { -1f, 1f })
                k.Span(S.TimberV, new Vector3(xs * (hx - 0.18f), top + 0.025f, -hz + 0.03f), new Vector3(xs * (hx - 0.1f), sy, hz - 0.03f), Board(k, xs > 0f ? 66 : 67, frame), 0.005f);

            if (!k.Detail) return;
            if (supply)
            {
                Color steel = T(0.5f);
                foreach (float x in new[] { -hx * 0.45f, hx * 0.45f })
                {
                    k.Span(S.Metal, new Vector3(x - 0.02f, top + 0.025f, -hz - 0.004f), new Vector3(x + 0.02f, top + 0.029f, hz + 0.004f), steel);
                    foreach (float zs in new[] { -1f, 1f })
                        k.Span(S.Metal, new Vector3(x - 0.02f, 0.004f, zs * hz), new Vector3(x + 0.02f, top + 0.029f, zs * (hz + 0.004f)), steel);
                }
                // Faded stencil panel on the front.
                k.Span(S.Paint, new Vector3(-0.17f, top * 0.42f, hz - bt), new Vector3(0.17f, top * 0.62f, hz - bt + 0.002f), Paint(0.78f, 0.74f, 0.6f));
                k.Span(S.Paint, new Vector3(-0.12f, top * 0.49f, hz - bt + 0.002f), new Vector3(0.12f, top * 0.55f, hz - bt + 0.003f), Paint(0.2f, 0.22f, 0.18f));
            }
            else
            {
                Color rope = Rel(new Color(0.66f, 0.56f, 0.4f), StructurePalette.Fabric);
                float ym = top - 0.12f;
                foreach (float xs in new[] { -1f, 1f })
                {
                    var pts = new[]
                    {
                        new Vector3(xs * (hx - bt - 0.005f), ym, -0.11f), new Vector3(xs * (hx + 0.012f), ym - 0.04f, -0.1f),
                        new Vector3(xs * (hx + 0.022f), ym - 0.09f, 0f), new Vector3(xs * (hx + 0.012f), ym - 0.04f, 0.1f),
                        new Vector3(xs * (hx - bt - 0.005f), ym, 0.11f)
                    };
                    k.Tube(S.Fabric, MeshFactory.RoundCorners(pts, 0.05f, 3), 0.012f, 6, rope);
                }
                foreach (float xs in new[] { -1f, 1f })
                    foreach (float y in new[] { bw * 0.5f, top - bw * 0.5f })
                        Nail(k, new Vector3(xs * (hx - bw * 0.5f), y, hz), Vector3.forward);
            }
        }

        // ------------------------------------------------------------ workbench

        /// <summary>Heavy plank bench with aprons, a stocked lower shelf, a bench vice and tools on top.</summary>
        public static void Workbench(StructureKit k, float sx, float sy, float sz, Color tone)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            const float topY = 0.8f, topT = 0.07f, leg = 0.1f;
            float lx = hx - 0.12f, lz = hz - 0.1f;
            Color frame = Scale(tone, 0.9f);
            int salt = 0;
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                    k.Span(S.TimberV, new Vector3(xs * lx - leg * 0.5f, 0f, zs * lz - leg * 0.5f), new Vector3(xs * lx + leg * 0.5f, topY, zs * lz + leg * 0.5f), Board(k, salt++, frame), 0.01f);
            foreach (float zs in new[] { -1f, 1f })
            {
                float zo = zs * (lz + leg * 0.5f), zi = zs * (lz + leg * 0.5f - 0.035f);
                k.Span(S.Timber, new Vector3(-lx + leg * 0.5f, topY - 0.13f, zi), new Vector3(lx - leg * 0.5f, topY, zo), Board(k, salt++, frame), 0.008f);
                k.Span(S.Timber, new Vector3(-lx + leg * 0.5f, 0.13f, zi), new Vector3(lx - leg * 0.5f, 0.22f, zo), Board(k, salt++, frame), 0.008f);
            }
            foreach (float xs in new[] { -1f, 1f })
            {
                float xo = xs * (lx + leg * 0.5f), xi = xs * (lx + leg * 0.5f - 0.035f);
                k.Span(S.Timber, new Vector3(xi, topY - 0.13f, -lz + leg * 0.5f), new Vector3(xo, topY, lz - leg * 0.5f), Board(k, salt++, frame), 0.008f);
                k.Span(S.Timber, new Vector3(xi, 0.13f, -lz + leg * 0.5f), new Vector3(xo, 0.22f, lz - leg * 0.5f), Board(k, salt++, frame), 0.008f);
            }
            // Lower shelf boards.
            float shelfW = (2f * lz - leg) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float z0 = -lz + leg * 0.5f + i * shelfW;
                k.Span(S.Timber, new Vector3(-lx + leg * 0.5f, 0.22f, z0 + 0.004f), new Vector3(lx - leg * 0.5f, 0.245f, z0 + shelfW - 0.004f), Board(k, salt++, tone), 0.004f);
            }
            // Thick laminated top, slightly uneven.
            const int planks = 5;
            float pz = sz / planks;
            for (int i = 0; i < planks; i++)
            {
                float y1 = topY + topT - k.Rand(40 + i) * 0.006f;
                k.Span(S.Timber, new Vector3(-hx, topY, -hz + i * pz + 0.003f), new Vector3(hx, y1, -hz + (i + 1) * pz - 0.003f), Board(k, 30 + i, Scale(tone, 1.08f)), 0.01f);
            }
            float top = topY + topT;
            k.Span(S.Timber, new Vector3(-hx, top, -hz), new Vector3(hx, Mathf.Min(sy, top + 0.12f), -hz + 0.035f), Board(k, 50, frame), 0.008f);

            // Bench vice on the front right corner.
            float vx = hx - 0.28f, vz = hz - 0.1f;
            Color iron = Paint(0.26f, 0.32f, 0.36f);
            k.Span(S.Paint, new Vector3(vx - 0.1f, top, vz - 0.17f), new Vector3(vx + 0.1f, top + 0.09f, vz - 0.06f), iron, 0.01f);
            k.Span(S.Paint, new Vector3(vx - 0.1f, top + 0.005f, vz - 0.035f), new Vector3(vx + 0.1f, top + 0.09f, vz + 0.02f), iron, 0.01f);
            k.Span(S.Metal, new Vector3(vx - 0.095f, top + 0.05f, vz - 0.06f), new Vector3(vx + 0.095f, top + 0.088f, vz - 0.05f), T(0.7f));
            k.Span(S.Metal, new Vector3(vx - 0.095f, top + 0.05f, vz - 0.045f), new Vector3(vx + 0.095f, top + 0.088f, vz - 0.035f), T(0.7f));
            k.Cylinder(S.Metal, new Vector3(vx, top + 0.045f, vz - 0.12f), new Vector3(vx, top + 0.045f, vz + 0.07f), 0.012f, 0.012f, 8, T(0.8f));
            k.Cylinder(S.Metal, new Vector3(vx - 0.1f, top + 0.045f, vz + 0.06f), new Vector3(vx + 0.1f, top + 0.045f, vz + 0.06f), 0.007f, 0.007f, 6, T(0.8f));
            if (!k.Detail) return;
            foreach (float x in new[] { vx - 0.105f, vx + 0.105f })
                k.Sphere(S.Metal, new Vector3(x, top + 0.045f, vz + 0.06f), Vector3.one * 0.013f, T(0.8f));

            // Hand saw, hammer, square, a plane, a nail tin and offcuts.
            Matrix4x4 saved = k.Matrix;
            k.Matrix = saved * Matrix4x4.TRS(new Vector3(-0.35f, top, 0.1f), Quaternion.Euler(0f, 18f, 0f), Vector3.one);
            k.Span(S.Metal, new Vector3(-0.28f, 0f, -0.05f), new Vector3(0.2f, 0.003f, 0.05f), T(0.75f));
            k.Span(S.Timber, new Vector3(0.2f, 0f, -0.065f), new Vector3(0.33f, 0.024f, 0.065f), Board(k, 70, Scale(tone, 0.6f)), 0.008f);
            k.Matrix = saved * Matrix4x4.TRS(new Vector3(0.18f, top, -0.18f), Quaternion.Euler(0f, -25f, 0f), Vector3.one);
            k.Cylinder(S.TimberV, new Vector3(-0.15f, 0.018f, 0f), new Vector3(0.14f, 0.018f, 0f), 0.016f, 0.014f, 8, Board(k, 71, Scale(tone, 1.3f)));
            k.Span(S.Metal, new Vector3(0.12f, 0.002f, -0.055f), new Vector3(0.16f, 0.04f, 0.055f), T(0.45f), 0.004f);
            k.Matrix = saved * Matrix4x4.TRS(new Vector3(0.62f, top, 0.18f), Quaternion.Euler(0f, 40f, 0f), Vector3.one);
            k.Span(S.Metal, new Vector3(-0.15f, 0f, -0.012f), new Vector3(0.15f, 0.003f, 0.012f), T(0.8f));
            k.Span(S.Metal, new Vector3(-0.15f, 0f, 0.012f), new Vector3(-0.126f, 0.003f, 0.2f), T(0.8f));
            k.Matrix = saved * Matrix4x4.TRS(new Vector3(-0.02f, top, -0.26f), Quaternion.Euler(0f, 8f, 0f), Vector3.one);
            k.Span(S.Paint, new Vector3(-0.11f, 0f, -0.03f), new Vector3(0.11f, 0.045f, 0.03f), Paint(0.2f, 0.32f, 0.5f), 0.008f);
            k.Span(S.Timber, new Vector3(0f, 0.045f, -0.02f), new Vector3(0.07f, 0.09f, 0.02f), Board(k, 72, Scale(tone, 1.2f)), 0.01f);
            k.Matrix = saved;
            k.Cylinder(S.Rust, new Vector3(0.45f, top, -0.3f), new Vector3(0.45f, top + 0.11f, -0.3f), 0.05f, 0.05f, 12, T(0.9f), true, false, false);
            k.Disc(S.Metal, new Vector3(0.45f, top + 0.09f, -0.3f), Vector3.up, 0.048f, 12, T(0.35f));
            for (int i = 0; i < 3; i++)
            {
                float len = 0.25f + k.Rand(80 + i) * 0.2f;
                k.Box(S.Timber, new Vector3(-0.72f + i * 0.02f, top + 0.025f + i * 0.045f, -0.25f + i * 0.03f), new Vector3(len, 0.045f, 0.1f), Board(k, 81 + i, Scale(tone, 1.25f)), 0.006f, Quaternion.Euler(0f, (k.Rand(85 + i) - 0.5f) * 30f, 0f));
            }
            // Lower shelf: a stack of planks and a bucket.
            for (int i = 0; i < 3; i++)
                k.Span(S.Timber, new Vector3(-0.15f + i * 0.01f, 0.245f + i * 0.03f, -0.25f + i * 0.02f), new Vector3(0.6f - i * 0.03f, 0.275f + i * 0.03f, -0.05f + i * 0.02f), Board(k, 90 + i, tone), 0.005f);
            k.Lathe(S.Paint, new Vector3(-0.55f, 0.245f, 0.05f), new[] { new Vector2(0.11f, 0f), new Vector2(0.135f, 0.24f), new Vector2(0.125f, 0.24f), new Vector2(0.1f, 0.03f) }, 14, Paint(0.5f, 0.52f, 0.5f));
        }

        // ------------------------------------------------------------ bed

        /// <summary>Low plank bed with a ticking mattress, pillow and a wool blanket turned down at the head.</summary>
        public static void Bed(StructureKit k, float sx, float sy, float sz, Color blanket)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color wood = Scale(WoodTone(new Color(0.55f, 0.42f, 0.3f)), 0.9f);
            const float leg = 0.075f;
            int salt = 0;
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                    k.Span(S.TimberV, new Vector3(xs * hx, 0f, zs * hz), new Vector3(xs * (hx - leg), xs > 0f ? sy : 0.22f, zs * (hz - leg)), Board(k, salt++, wood), 0.008f);
            foreach (float zs in new[] { -1f, 1f })
                k.Span(S.Timber, new Vector3(-hx + leg, 0.07f, zs * hz), new Vector3(hx - leg, 0.2f, zs * (hz - 0.05f)), Board(k, salt++, wood), 0.008f);
            k.Span(S.Timber, new Vector3(-hx + 0.005f, 0.07f, -hz + leg), new Vector3(-hx + 0.055f, 0.2f, hz - leg), Board(k, salt++, wood), 0.008f);
            // Headboard: two boards between the tall head posts.
            k.Span(S.Timber, new Vector3(hx - 0.06f, 0.07f, -hz + leg), new Vector3(hx - 0.01f, 0.22f, hz - leg), Board(k, salt++, wood), 0.008f);
            k.Span(S.Timber, new Vector3(hx - 0.06f, 0.24f, -hz + leg), new Vector3(hx - 0.01f, sy - 0.03f, hz - leg), Board(k, salt++, wood), 0.008f);

            Color ticking = Rel(new Color(0.76f, 0.73f, 0.64f), StructurePalette.Fabric);
            k.Span(S.Fabric, new Vector3(-hx + 0.04f, 0.16f, -hz + 0.045f), new Vector3(hx - 0.07f, 0.3f, hz - 0.045f), ticking, 0.045f);
            Color sheet = Rel(new Color(0.84f, 0.82f, 0.76f), StructurePalette.Fabric);
            k.Sphere(S.Fabric, new Vector3(hx - 0.26f, 0.335f, 0f), new Vector3(0.16f, 0.06f, 0.31f), sheet, 2, true, d => 1f + 0.06f * Mathf.Sin(d.z * 9f) * (1f - Mathf.Abs(d.y)));

            // Blanket over the lower two thirds, hanging over the sides and the foot.
            Color wool = Rel(blanket, StructurePalette.Fabric);
            float bx1 = hx - 0.48f;
            k.Span(S.Fabric, new Vector3(-hx + 0.02f, 0.29f, -hz + 0.025f), new Vector3(bx1, 0.325f, hz - 0.025f), wool, 0.012f);
            foreach (float zs in new[] { -1f, 1f })
                k.Span(S.Fabric, new Vector3(-hx + 0.02f, 0.17f, zs * (hz - 0.045f)), new Vector3(bx1, 0.32f, zs * (hz - 0.022f)), Scale(wool, 0.85f), 0.01f);
            k.Span(S.Fabric, new Vector3(-hx + 0.012f, 0.17f, -hz + 0.025f), new Vector3(-hx + 0.04f, 0.32f, hz - 0.025f), Scale(wool, 0.85f), 0.01f);
            // Turned-down edge with the sheet showing.
            k.Cylinder(S.Fabric, new Vector3(bx1, 0.33f, -hz + 0.03f), new Vector3(bx1, 0.33f, hz - 0.03f), 0.028f, 0.028f, 10, wool);
            k.Span(S.Fabric, new Vector3(bx1, 0.3f, -hz + 0.035f), new Vector3(bx1 + 0.12f, 0.312f, hz - 0.035f), sheet, 0.005f);
            if (!k.Detail) return;
            // Soft folds in the wool.
            for (int i = 0; i < 3; i++)
                k.Sphere(S.Fabric, new Vector3(-0.55f + i * 0.4f + k.Rand(10 + i) * 0.1f, 0.322f, (k.Rand(20 + i) - 0.5f) * 0.4f), new Vector3(0.16f, 0.018f, 0.06f), wool, 1, true, null, Quaternion.Euler(0f, 20f + k.Rand(30 + i) * 60f, 0f));
        }

        // ------------------------------------------------------------ barricade

        /// <summary>Palisade of sharpened logs lashed to two rails, propped from behind, with stones at the foot.</summary>
        public static void Barricade(StructureKit k, float sx, float sy, float sz, Color tone)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            Color bark = Rel(new Color(0.42f, 0.35f, 0.28f), StructurePalette.Bark);
            Color cut = WoodTone(new Color(0.78f, 0.66f, 0.48f));
            const int logs = 13;
            float spacing = sx / logs;
            for (int i = 0; i < logs; i++)
            {
                float x = -hx + spacing * (i + 0.5f) + (Fixed(i) - 0.5f) * 0.02f;
                float r = 0.095f + Fixed(i + 20) * 0.016f;
                float h = sy - 0.42f + Fixed(i + 40) * 0.14f;
                Vector3 baseP = new Vector3(x, -0.2f, (Fixed(i + 60) - 0.5f) * 0.03f);
                Vector3 topP = new Vector3(x + (Fixed(i + 80) - 0.5f) * 0.05f, h, baseP.z + (Fixed(i + 100) - 0.5f) * 0.04f);
                k.Cylinder(S.Bark, baseP, topP, r, r * 0.93f, 9, Scale(bark, 0.85f + Fixed(i + 120) * 0.3f), true, false, false);
                Vector3 axis = (topP - baseP).normalized;
                k.Cylinder(S.TimberV, topP, topP + axis * (0.24f + Fixed(i + 140) * 0.08f), r * 0.93f, 0f, 6, Scale(cut, 0.9f + Fixed(i + 160) * 0.2f), false, false, false);
            }
            // Rails across the front, end grain showing.
            const float zr = 0.17f;
            Color rope = Rel(new Color(0.6f, 0.5f, 0.34f), StructurePalette.Fabric);
            foreach (float y in new[] { 0.42f, 1.02f })
            {
                k.Cylinder(S.BarkH, new Vector3(-hx + 0.02f, y, zr), new Vector3(hx - 0.02f, y + 0.02f, zr), 0.075f, 0.068f, 9, bark, true, false, false);
                k.Disc(S.Timber, new Vector3(-hx + 0.02f, y, zr), Vector3.left, 0.075f, 9, cut);
                k.Disc(S.Timber, new Vector3(hx - 0.02f, y + 0.02f, zr), Vector3.right, 0.068f, 9, cut);
                if (!k.Detail) continue;
                for (int i = 0; i < logs; i++)
                {
                    float x = -hx + spacing * (i + 0.5f);
                    float yy = y + 0.02f * (x + hx) / sx;
                    k.Ring(S.Fabric, new Vector3(x - 0.025f, yy, zr), Vector3.right, 0.083f, 0.011f, 12, 5, rope);
                    k.Ring(S.Fabric, new Vector3(x + 0.025f, yy, zr), Vector3.right, 0.083f, 0.011f, 12, 5, rope);
                }
            }
            // Props behind.
            foreach (float x in new[] { -0.95f, 0.95f })
                k.Cylinder(S.Bark, new Vector3(x, -0.15f, -hz + 0.02f), new Vector3(x + 0.02f, 1.15f, -0.07f), 0.07f, 0.06f, 8, Scale(bark, 0.9f), true, false, true);
            if (!k.Detail) return;
            for (int i = 0; i < 7; i++)
            {
                float x = -hx + 0.2f + i * (sx - 0.4f) / 6f + (Fixed(i + 200) - 0.5f) * 0.2f;
                float z = (i % 2 == 0 ? 1f : -1f) * (0.12f + Fixed(i + 210) * 0.1f);
                float r = 0.08f + Fixed(i + 220) * 0.07f;
                int seed = i;
                k.Sphere(S.Stone, new Vector3(x, r * 0.25f, z), new Vector3(r, r * 0.7f, r * 0.9f), T(0.8f + Fixed(i + 230) * 0.25f), 1, false, d => 1f + (MeshFactory.Hash(seed + 700, d) - 0.5f) * 0.35f);
            }
        }

        // ------------------------------------------------------------ planter

        /// <summary>Raised plank bed on corner posts with a capping rail.</summary>
        public static void Planter(StructureKit k, float sx, float sy, float sz, Color tone)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            const float t = 0.035f, post = 0.08f, cap = 0.03f;
            float wallTop = sy - cap;
            int salt = 0;
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                    k.Span(S.TimberV, new Vector3(xs * (hx - t), 0f, zs * (hz - t)), new Vector3(xs * (hx - t - post), wallTop, zs * (hz - t - post)), Board(k, salt++, Scale(tone, 0.8f)), 0.006f);
            foreach (float y0 in new[] { 0.01f, wallTop * 0.5f + 0.004f })
            {
                float y1 = y0 + wallTop * 0.5f - 0.012f;
                foreach (float zs in new[] { -1f, 1f })
                    k.Span(S.Timber, new Vector3(-hx, y0, zs * (hz - t)), new Vector3(hx, y1, zs * hz), Board(k, salt++, tone), 0.006f);
                foreach (float xs in new[] { -1f, 1f })
                    k.Span(S.Timber, new Vector3(xs * (hx - t), y0, -hz + t), new Vector3(xs * hx, y1, hz - t), Board(k, salt++, tone), 0.006f);
            }
            Color capTone = Scale(tone, 0.9f);
            foreach (float zs in new[] { -1f, 1f })
                k.Span(S.Timber, new Vector3(-hx - 0.01f, wallTop, zs * (hz - 0.09f)), new Vector3(hx + 0.01f, sy, zs * (hz + 0.01f)), Board(k, salt++, capTone), 0.008f);
            foreach (float xs in new[] { -1f, 1f })
                k.Span(S.Timber, new Vector3(xs * (hx - 0.09f), wallTop, -hz + 0.09f), new Vector3(xs * (hx + 0.01f), sy, hz - 0.09f), Board(k, salt++, capTone), 0.008f);
            if (!k.Detail) return;
            foreach (float xs in new[] { -1f, 1f })
                foreach (float zs in new[] { -1f, 1f })
                    foreach (float y in new[] { wallTop * 0.25f, wallTop * 0.75f })
                        Nail(k, new Vector3(xs * (hx - t - post * 0.5f), y, zs * hz), Vector3.forward * zs);
        }

        /// <summary>Tilled soil inside the planter: two mounded rows with clods.</summary>
        public static void PlanterSoil(StructureKit k, float sx, float sz, float level)
        {
            float hx = sx * 0.5f - 0.036f, hz = sz * 0.5f - 0.036f;
            float Row(float z) => Mathf.Exp(-Mathf.Pow((z - 0.25f) / 0.13f, 2f)) + Mathf.Exp(-Mathf.Pow((z + 0.25f) / 0.13f, 2f));
            float Height(float x, float z)
            {
                float edge = Mathf.Min(hx - Mathf.Abs(x), hz - Mathf.Abs(z));
                float clod = (MeshFactory.Hash(91, new Vector3(Mathf.Round(x * 18f), 0f, Mathf.Round(z * 18f))) - 0.5f) * 0.012f;
                return level + Row(z) * 0.045f + clod - (1f - Mathf.Clamp01(edge / 0.06f)) * 0.012f;
            }
            k.Heightfield(S.Soil, -hx, hx, -hz, hz, 30, 18, Height, (x, y, z) =>
            {
                float furrow = 1f - Mathf.Clamp01(Row(z));
                return new Color(1f - furrow * 0.15f, 1f - furrow * 0.15f, 1f - furrow * 0.12f, 1f - furrow * 0.35f);
            });
        }

        /// <summary>
        /// Potato plants in the crop rows around the Crops pivot (on the soil): stems and clusters of
        /// oval leaves, sized so the game's growth scale (up to 1.6 high) ends near half a metre.
        /// </summary>
        public static void Crops(StructureKit k, float sx, Vector3 origin)
        {
            Color stem = new Color(0.75f, 0.85f, 0.6f, 0.8f);
            int plant = 0;
            foreach (float z in new[] { -0.25f, 0.25f })
            {
                foreach (float x in new[] { -sx * 0.31f, 0f, sx * 0.31f })
                {
                    Vector3 p = origin + new Vector3(x + (Fixed(plant + 300) - 0.5f) * 0.08f, 0f, z + (Fixed(plant + 310) - 0.5f) * 0.05f);
                    const int stems = 5;
                    var tips = new Vector3[stems];
                    for (int s = 0; s < stems; s++)
                    {
                        float a = (s / (float)stems + Fixed(plant * 7 + s + 320) * 0.15f) * Mathf.PI * 2f;
                        Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        float h = 0.16f + Fixed(plant * 7 + s + 330) * 0.12f;
                        Vector3 tip = p + dir * (0.07f + Fixed(plant * 7 + s + 340) * 0.05f) + Vector3.up * h;
                        tips[s] = tip;
                        k.Cylinder(S.Foliage, p + Vector3.up * 0.005f, tip, 0.008f, 0.005f, 5, stem, true, false, false);
                        // Leaflets along and at the end of the stem, angled up and out.
                        for (int l = 0; l < 3; l++)
                        {
                            float t = 0.45f + l * 0.27f;
                            Vector3 lp = Vector3.Lerp(p, tip, t) + dir * 0.025f;
                            float yaw = -a * Mathf.Rad2Deg + 90f + (l - 1) * 35f;
                            float shade = 0.75f + t * 0.3f;
                            k.Sphere(S.Foliage, lp, new Vector3(0.042f, 0.009f, 0.065f), new Color(shade, shade, shade, 0.65f + t * 0.35f), 1, true, null,
                                Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-25f - l * 8f, 0f, 0f));
                        }
                    }
                    // A few flowers at the stem tips.
                    if (Fixed(plant + 350) > 0.35f)
                    {
                        for (int f = 0; f < 2; f++)
                        {
                            Vector3 fp = tips[(plant + f * 2) % stems] + Vector3.up * 0.012f;
                            k.Sphere(S.Plain, fp, new Vector3(0.016f, 0.01f, 0.016f), Rel(new Color(0.86f, 0.82f, 0.92f), StructurePalette.Plain), 1);
                            k.Sphere(S.Plain, fp + Vector3.up * 0.008f, Vector3.one * 0.006f, Rel(new Color(0.95f, 0.8f, 0.2f), StructurePalette.Plain), 0);
                        }
                    }
                    plant++;
                }
            }
        }
    }
}
