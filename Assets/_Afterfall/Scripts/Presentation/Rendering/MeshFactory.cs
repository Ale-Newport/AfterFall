using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedural meshes for the stylised art direction. Meshes are cached and shared between
    /// renderers. Every mesh carries vertex colours (rgb multiplies the material, alpha is baked
    /// ambient occlusion; the shader uses them as linear values, unlike material tints, which are
    /// sRGB colours that Unity converts) and needs no UVs: the Afterfall/Surface shader maps its textures
    /// triplanarly. Feature areas add their own shapes in partial files (MeshFactory.&lt;Area&gt;.cs)
    /// built with <see cref="MeshBuilder"/> and cached with <see cref="Cached"/>.
    /// </summary>
    public static partial class MeshFactory
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Unit cube centred on the origin (same extents as Unity's built-in cube).</summary>
        public static Mesh Cube => Cached("cube", () =>
        {
            var b = new MeshBuilder();
            b.Box(Vector3.zero, Vector3.one);
            return b.Build("cube");
        });

        /// <summary>Unit quad in the XY plane facing −Z (same as Unity's built-in quad).</summary>
        public static Mesh Quad => Cached("quad", () =>
        {
            var b = new MeshBuilder();
            b.Quad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f));
            return b.Build("quad");
        });

        /// <summary>A box of the given size (centred), optionally with chamfered edges that catch the light. Use with scale 1.</summary>
        public static Mesh Box(Vector3 size, float bevel = 0f) =>
            Cached($"box{size.x:0.###}x{size.y:0.###}x{size.z:0.###}b{bevel:0.###}", () =>
            {
                var b = new MeshBuilder();
                b.Box(Vector3.zero, size, null, bevel);
                return b.Build("box");
            });

        /// <summary>Unit cylinder: radius 0.5, from y=0 to y=1.</summary>
        public static Mesh Cylinder(int sides = 8, bool smooth = false) => Cached($"cyl{sides}{(smooth ? "s" : "")}", () =>
        {
            var b = new MeshBuilder();
            b.Cylinder(Vector3.zero, Vector3.up, 0.5f, 0.5f, sides, smooth);
            return b.Build("cylinder");
        });

        /// <summary>Unit frustum: bottom radius 0.5 at y=0, top radius 0.5·<paramref name="topRatio"/> at y=1.</summary>
        public static Mesh Frustum(float topRatio, int sides = 10, bool smooth = true) => Cached($"frustum{topRatio:0.###}:{sides}{(smooth ? "s" : "")}", () =>
        {
            var b = new MeshBuilder();
            b.Cylinder(Vector3.zero, Vector3.up, 0.5f, 0.5f * topRatio, sides, smooth);
            return b.Build("frustum");
        });

        /// <summary>Unit cone: base radius 0.5 at y=0, apex at y=1.</summary>
        public static Mesh Cone(int sides = 8) => Cached($"cone{sides}", () =>
        {
            var b = new MeshBuilder();
            b.Cylinder(Vector3.zero, Vector3.up, 0.5f, 0f, sides, false, true, false);
            return b.Build("cone");
        });

        /// <summary>Sphere of radius 0.5 (icosphere).</summary>
        public static Mesh Sphere(int subdivisions = 2, bool smooth = true) => Cached($"sphere{subdivisions}{(smooth ? "s" : "")}", () =>
        {
            var b = new MeshBuilder();
            b.Sphere(Vector3.zero, Vector3.one * 0.5f, subdivisions, smooth);
            return b.Build("sphere");
        });

        /// <summary>Capsule along Y of the given radius and total height, base at y=0. Use with scale 1.</summary>
        public static Mesh Capsule(float radius, float height, int sides = 12) => Cached($"capsule{radius:0.###}:{height:0.###}:{sides}", () =>
        {
            var b = new MeshBuilder();
            b.Capsule(new Vector3(0f, radius, 0f), new Vector3(0f, Mathf.Max(radius, height - radius), 0f), radius, sides);
            return b.Build("capsule");
        });

        /// <summary>Triangular prism roof: 1×1×1, ridge along X at y=1, eaves at z=±0.5.</summary>
        public static Mesh GableRoof() => Cached("gable", () =>
        {
            var b = new MeshBuilder();
            Vector3 a0 = new Vector3(-0.5f, 0, -0.5f), a1 = new Vector3(-0.5f, 0, 0.5f), a2 = new Vector3(-0.5f, 1, 0);
            Vector3 b0 = new Vector3(0.5f, 0, -0.5f), b1 = new Vector3(0.5f, 0, 0.5f), b2 = new Vector3(0.5f, 1, 0);
            Vector3 inside = new Vector3(0f, 0.3f, 0f);
            b.Face(inside, a0, a1, a2);
            b.Face(inside, b0, b1, b2);
            b.Face(inside, a2, b2, b0, a0);
            b.Face(inside, b2, a2, a1, b1);
            b.Face(inside, b0, b1, a1, a0);
            return b.Build("gable");
        });

        /// <summary>Jittered icosphere (rocks, ore, bushes). Radius ≈0.5.</summary>
        public static Mesh Rock(int variant, float jitter = 0.18f, float squash = 0.75f) => Cached($"rock{variant}:{jitter}:{squash}", () =>
        {
            var b = new MeshBuilder();
            b.Sphere(Vector3.zero, new Vector3(0.5f, 0.5f * squash, 0.5f), 1, false, d => 1f + (Hash(variant, d) * 2f - 1f) * jitter);
            return b.Build("rock");
        });

        /// <summary>Pine crown: three stacked cones in a single mesh, height 1, base radius 0.5.</summary>
        public static Mesh PineCrown(int variant) => Cached($"pine{variant}", () =>
        {
            var b = new MeshBuilder();
            float y = 0f;
            for (int i = 0; i < 3; i++)
            {
                float r = 0.5f - i * 0.12f + Hash(variant * 31 + i, Vector3.one) * 0.04f;
                float h = 0.45f - i * 0.05f;
                b.Matrix = Matrix4x4.TRS(new Vector3(0, y, 0), Quaternion.Euler(0, Hash(variant * 7 + i, Vector3.up) * 50f, 0), Vector3.one);
                b.Cylinder(Vector3.zero, new Vector3(0, h, 0), r, 0f, 7, false, true, false);
                y += h * 0.62f;
            }
            return b.Build("pine");
        });

        public static Mesh Blob(int variant) => Rock(100 + variant, 0.22f, 0.9f);

        /// <summary>Surface of revolution around Y from (radius, height) points, bottom to top.</summary>
        public static Mesh Lathe(string key, Vector2[] profile, int sides, bool smooth = true) => Cached("lathe:" + key + ":" + sides, () =>
        {
            var b = new MeshBuilder();
            b.Lathe(profile, sides, smooth);
            return b.Build("lathe " + key);
        });

        /// <summary>Returns the cached mesh for <paramref name="key"/>, building it on first use.</summary>
        public static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out Mesh m) && m != null) return m;
            m = build();
            Cache[key] = m;
            return m;
        }

        /// <summary>Deterministic 0..1 hash of a seed and a direction (stable jitter for procedural shapes).</summary>
        public static float Hash(int seed, Vector3 v)
        {
            unchecked
            {
                int h = seed * 73856093 ^ Mathf.RoundToInt(v.x * 1000f) * 19349663 ^ Mathf.RoundToInt(v.y * 1000f) * 83492791 ^ Mathf.RoundToInt(v.z * 1000f) * 2654435;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xffffff) / 16777216f;
            }
        }
    }

    /// <summary>
    /// Accumulates geometry for one mesh. Flat faces (<see cref="Tri"/>, <see cref="Quad"/>,
    /// <see cref="Face"/>) get their own vertices; smooth primitives share vertices so normals
    /// interpolate. <see cref="Matrix"/> places each primitive; <see cref="Color"/> is written to the
    /// vertices (rgb multiplies the material colour, alpha is ambient occlusion: 1 open, lower in
    /// crevices). Front faces wind clockwise; <see cref="Face"/> orients itself away from a point
    /// inside the solid so callers never have to think about winding.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int> _t = new List<int>();

        public Color Color = Color.white;
        public Matrix4x4 Matrix = Matrix4x4.identity;

        public int VertexCount => _v.Count;

        /// <summary>Adds a vertex (local to <see cref="Matrix"/>) and returns its index.</summary>
        public int Vertex(Vector3 p, Vector3 n) => Vertex(p, n, Color);

        public int Vertex(Vector3 p, Vector3 n, Color c)
        {
            _v.Add(Matrix.MultiplyPoint3x4(p));
            _n.Add(Unit(Matrix.MultiplyVector(n), Vector3.up));
            _c.Add(c);
            return _v.Count - 1;
        }

        /// <summary>
        /// Normalises without Unity's cut-off: Vector3.normalized returns zero below a length of 1e-5,
        /// which millimetre faces (bevel corners) reach, and a zero normal becomes NaN in the shader.
        /// </summary>
        public static Vector3 Unit(Vector3 v, Vector3 fallback)
        {
            float m = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return m > 1e-20f ? v / m : fallback;
        }

        public void Triangle(int a, int b, int c)
        {
            _t.Add(a);
            _t.Add(b);
            _t.Add(c);
        }

        /// <summary>Flat triangle; the front face is clockwise (normal = (b−a)×(c−a)).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-24f) return;
            n = Unit(n, Vector3.up);
            int i = Vertex(a, n);
            Vertex(b, n);
            Vertex(c, n);
            Triangle(i, i + 1, i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>A flat convex polygon facing away from <paramref name="inside"/> (fan triangulated).</summary>
        public void Face(Vector3 inside, params Vector3[] poly)
        {
            if (poly.Length < 3) return;
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 p in poly) centroid += p;
            centroid /= poly.Length;
            Vector3 n = Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]);
            bool flip = Vector3.Dot(n, centroid - inside) < 0f;
            for (int i = 1; i + 1 < poly.Length; i++)
            {
                if (flip) Tri(poly[0], poly[i + 1], poly[i]);
                else Tri(poly[0], poly[i], poly[i + 1]);
            }
        }

        /// <summary>A box centred on <paramref name="center"/>; <paramref name="bevel"/> chamfers every edge (metres).</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion? rotation = null, float bevel = 0f)
        {
            Matrix4x4 saved = Matrix;
            Matrix = saved * Matrix4x4.TRS(center, rotation ?? Quaternion.identity, Vector3.one);
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f);
            Vector3 o = Vector3.zero;
            if (b <= 0f)
            {
                Vector3 p(int x, int y, int z) => new Vector3(x * h.x, y * h.y, z * h.z);
                Face(o, p(1, -1, -1), p(1, 1, -1), p(1, 1, 1), p(1, -1, 1));
                Face(o, p(-1, -1, -1), p(-1, 1, -1), p(-1, 1, 1), p(-1, -1, 1));
                Face(o, p(-1, 1, -1), p(1, 1, -1), p(1, 1, 1), p(-1, 1, 1));
                Face(o, p(-1, -1, -1), p(1, -1, -1), p(1, -1, 1), p(-1, -1, 1));
                Face(o, p(-1, -1, 1), p(1, -1, 1), p(1, 1, 1), p(-1, 1, 1));
                Face(o, p(-1, -1, -1), p(1, -1, -1), p(1, 1, -1), p(-1, 1, -1));
            }
            else
            {
                Vector3 i = h - Vector3.one * b;
                // Main faces, inset by the bevel.
                for (int axis = 0; axis < 3; axis++)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var poly = new Vector3[4];
                        for (int k = 0; k < 4; k++)
                        {
                            float u = (k == 0 || k == 3) ? -1f : 1f, w = k < 2 ? -1f : 1f;
                            poly[k] = Axis(axis, s * h[axis], u * i[(axis + 1) % 3], w * i[(axis + 2) % 3]);
                        }
                        Face(o, poly);
                    }
                }
                // Edge chamfers between two faces, and corner triangles.
                for (int axis = 0; axis < 3; axis++)
                {
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    for (int s1 = -1; s1 <= 1; s1 += 2)
                    {
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            // Edge along `axis`, at the corner of faces a1 (sign s1) and a2 (sign s2).
                            Vector3 e(float along, bool onA1)
                            {
                                var v = new Vector3();
                                v[axis] = along;
                                v[a1] = s1 * (onA1 ? h[a1] : i[a1]);
                                v[a2] = s2 * (onA1 ? i[a2] : h[a2]);
                                return v;
                            }
                            Face(o, e(-i[axis], true), e(i[axis], true), e(i[axis], false), e(-i[axis], false));
                        }
                    }
                }
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                            Face(o, new Vector3(sx * h.x, sy * i.y, sz * i.z), new Vector3(sx * i.x, sy * h.y, sz * i.z), new Vector3(sx * i.x, sy * i.y, sz * h.z));
            }
            Matrix = saved;
        }

        private static Vector3 Axis(int axis, float a, float b, float c)
        {
            var v = new Vector3();
            v[axis] = a;
            v[(axis + 1) % 3] = b;
            v[(axis + 2) % 3] = c;
            return v;
        }

        /// <summary>
        /// A (possibly tapered) cylinder from <paramref name="bottom"/> to <paramref name="top"/>.
        /// Smooth sides share vertices; caps are always flat. A zero top radius makes a cone.
        /// </summary>
        public void Cylinder(Vector3 bottom, Vector3 top, float rBottom, float rTop, int sides, bool smooth = true, bool capBottom = true, bool capTop = true)
        {
            Vector3 axis = top - bottom;
            float len = axis.magnitude;
            if (len < 1e-6f) return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, axis / len);
            Matrix4x4 saved = Matrix;
            Matrix = saved * Matrix4x4.TRS(bottom, rot, Vector3.one);
            float slope = (rBottom - rTop) / len;
            if (smooth)
            {
                int start = VertexCount;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 n = new Vector3(dir.x, slope, dir.z).normalized;
                    Vertex(dir * rBottom, n);
                    Vertex(dir * rTop + Vector3.up * len, n);
                }
                for (int s = 0; s < sides; s++)
                {
                    int i0 = start + s * 2;
                    // Outward-facing, clockwise from outside.
                    Triangle(i0, i0 + 1, i0 + 3);
                    Triangle(i0, i0 + 3, i0 + 2);
                }
            }
            else
            {
                Vector3 inside = new Vector3(0f, len * 0.5f, 0f);
                for (int s = 0; s < sides; s++)
                {
                    float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    if (rTop < 1e-5f) Face(inside, d0 * rBottom, d1 * rBottom, Vector3.up * len);
                    else Face(inside, d0 * rBottom, d1 * rBottom, d1 * rTop + Vector3.up * len, d0 * rTop + Vector3.up * len);
                }
            }
            if (capBottom && rBottom > 1e-5f) Disc(Vector3.zero, rBottom, sides, Vector3.down);
            if (capTop && rTop > 1e-5f) Disc(Vector3.up * len, rTop, sides, Vector3.up);
            Matrix = saved;
        }

        private void Disc(Vector3 center, float r, int sides, Vector3 normal)
        {
            var poly = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                poly[s] = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            }
            Face(center - normal, poly);
        }

        /// <summary>
        /// An ellipsoid (icosphere). <paramref name="displace"/> receives the unit direction of each
        /// vertex and returns a radius multiplier (rocks, crowns). Flat faces when not smooth.
        /// </summary>
        public void Sphere(Vector3 center, Vector3 radii, int subdivisions = 1, bool smooth = true, System.Func<Vector3, float> displace = null)
        {
            Icosphere(subdivisions, out List<Vector3> dirs, out List<int> tris);
            var pts = new Vector3[dirs.Count];
            for (int k = 0; k < dirs.Count; k++)
            {
                float m = displace != null ? displace(dirs[k]) : 1f;
                pts[k] = center + Vector3.Scale(dirs[k], radii) * m;
            }
            if (smooth)
            {
                // Normals from the displaced surface: average of adjacent face normals.
                var normals = new Vector3[pts.Length];
                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 fn = Vector3.Cross(pts[tris[t + 1]] - pts[tris[t]], pts[tris[t + 2]] - pts[tris[t]]);
                    normals[tris[t]] += fn;
                    normals[tris[t + 1]] += fn;
                    normals[tris[t + 2]] += fn;
                }
                int start = VertexCount;
                for (int k = 0; k < pts.Length; k++) Vertex(pts[k], Unit(normals[k], dirs[k]));
                for (int t = 0; t < tris.Count; t += 3) Triangle(start + tris[t], start + tris[t + 1], start + tris[t + 2]);
            }
            else
            {
                for (int t = 0; t < tris.Count; t += 3) Face(center, pts[tris[t]], pts[tris[t + 1]], pts[tris[t + 2]]);
            }
        }

        /// <summary>A smooth capsule between two sphere centres.</summary>
        public void Capsule(Vector3 a, Vector3 b, float radius, int sides = 12)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            Quaternion rot = len > 1e-6f ? Quaternion.FromToRotation(Vector3.up, axis / len) : Quaternion.identity;
            int rings = Mathf.Max(2, sides / 4);
            var profile = new List<Vector2>();
            for (int r = 0; r <= rings; r++)
            {
                float t = -Mathf.PI * 0.5f + r * Mathf.PI * 0.5f / rings;
                profile.Add(new Vector2(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius));
            }
            for (int r = 0; r <= rings; r++)
            {
                float t = r * Mathf.PI * 0.5f / rings;
                profile.Add(new Vector2(Mathf.Cos(t) * radius, len + Mathf.Sin(t) * radius));
            }
            Matrix4x4 saved = Matrix;
            Matrix = saved * Matrix4x4.TRS(a, rot, Vector3.one);
            Lathe(profile.ToArray(), sides, true);
            Matrix = saved;
        }

        /// <summary>
        /// Surface of revolution around local Y. <paramref name="profile"/> holds (radius, height)
        /// from bottom to top; radius 0 at either end closes the shape. Open ends are capped flat.
        /// </summary>
        public void Lathe(Vector2[] profile, int sides, bool smooth = true)
        {
            int n = profile.Length;
            if (n < 2) return;
            if (smooth)
            {
                int start = VertexCount;
                for (int p = 0; p < n; p++)
                {
                    // Profile normal from neighbouring segments (outward in the radius/height plane).
                    Vector2 prev = profile[Mathf.Max(0, p - 1)], next = profile[Mathf.Min(n - 1, p + 1)];
                    Vector2 d = next - prev;
                    float dl = Mathf.Sqrt(d.x * d.x + d.y * d.y);
                    Vector2 tangent = dl > 1e-20f ? d / dl : Vector2.up;
                    Vector2 pn = new Vector2(tangent.y, -tangent.x);
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s * Mathf.PI * 2f / sides;
                        float c = Mathf.Cos(a), si = Mathf.Sin(a);
                        Vertex(new Vector3(c * profile[p].x, profile[p].y, si * profile[p].x), new Vector3(c * pn.x, pn.y, si * pn.x));
                    }
                }
                int row = sides + 1;
                for (int p = 0; p + 1 < n; p++)
                    for (int s = 0; s < sides; s++)
                    {
                        int i0 = start + p * row + s, i1 = i0 + 1, j0 = i0 + row, j1 = j0 + 1;
                        Triangle(i0, j0, j1);
                        Triangle(i0, j1, i1);
                    }
            }
            else
            {
                for (int p = 0; p + 1 < n; p++)
                {
                    Vector3 inside = new Vector3(0f, (profile[p].y + profile[p + 1].y) * 0.5f, 0f);
                    for (int s = 0; s < sides; s++)
                    {
                        float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                        Vector3 P(Vector2 pr, float a) => new Vector3(Mathf.Cos(a) * pr.x, pr.y, Mathf.Sin(a) * pr.x);
                        if (profile[p].x < 1e-5f) Face(inside, P(profile[p], a0), P(profile[p + 1], a0), P(profile[p + 1], a1));
                        else if (profile[p + 1].x < 1e-5f) Face(inside, P(profile[p], a0), P(profile[p + 1], a0), P(profile[p], a1));
                        else Face(inside, P(profile[p], a0), P(profile[p + 1], a0), P(profile[p + 1], a1), P(profile[p], a1));
                    }
                }
            }
            if (profile[0].x > 1e-5f) Disc(new Vector3(0f, profile[0].y, 0f), profile[0].x, sides, Vector3.down);
            if (profile[n - 1].x > 1e-5f) Disc(new Vector3(0f, profile[n - 1].y, 0f), profile[n - 1].x, sides, Vector3.up);
        }

        /// <summary>Copies another mesh in, transformed, with an optional colour override.</summary>
        public void Append(Mesh mesh, Matrix4x4 transform, Color? color = null)
        {
            Matrix4x4 saved = Matrix;
            Matrix = saved * transform;
            var verts = mesh.vertices;
            var normals = mesh.normals;
            var colors = mesh.colors;
            int start = VertexCount;
            for (int k = 0; k < verts.Length; k++)
                Vertex(verts[k], k < normals.Length ? normals[k] : Vector3.up, color ?? (k < colors.Length ? colors[k] : Color));
            var tris = mesh.triangles;
            bool mirrored = transform.determinant < 0f;
            for (int t = 0; t < tris.Length; t += 3)
            {
                if (mirrored) Triangle(start + tris[t], start + tris[t + 2], start + tris[t + 1]);
                else Triangle(start + tris[t], start + tris[t + 1], start + tris[t + 2]);
            }
            Matrix = saved;
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }

        private static void Icosphere(int subdivisions, out List<Vector3> verts, out List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                var next = new List<int>();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b, verts, mid), bc = Mid(b, c, verts, mid), ca = Mid(c, a, verts, mid);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            // The table winds so that (b−a)×(c−a) points outward: front faces as Unity expects.
        }

        private static int Mid(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }
    }

    /// <summary>Helpers to assemble composite props from meshes and colliders.</summary>
    public static class Shapes
    {
        public static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 localPos, Vector3 localScale, Quaternion? localRot = null, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A part built at its real size (scale 1), e.g. from <see cref="MeshBuilder"/>.</summary>
        public static GameObject Mesh(Transform parent, string name, Mesh mesh, Material material, Vector3 localPos, Quaternion? localRot = null, bool shadows = true) =>
            Part(parent, name, mesh, material, localPos, Vector3.one, localRot, shadows);

        /// <summary>
        /// Keeps a roof, ceiling or canopy out of the walkable NavMesh. It still blocks agents, but
        /// nobody is placed on it (a survivor indoors used to snap onto the nearest roof) or routed over it.
        /// </summary>
        public static GameObject NotWalkable(GameObject go)
        {
            var m = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            m.overrideArea = true;
            m.area = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
            return go;
        }

        /// <summary>A box part whose centre is at <paramref name="center"/>, with an optional box collider.</summary>
        public static GameObject Box(Transform parent, string name, Material material, Vector3 center, Vector3 size, bool collider = true, Quaternion? rot = null)
        {
            GameObject go = Part(parent, name, MeshFactory.Cube, material, center, size, rot);
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>A box with chamfered edges (built at size, scale 1), with an optional box collider of the full size.</summary>
        public static GameObject BevelBox(Transform parent, string name, Material material, Vector3 center, Vector3 size, float bevel = 0.025f, bool collider = true, Quaternion? rot = null)
        {
            GameObject go = Part(parent, name, MeshFactory.Box(size, bevel), material, center, Vector3.one, rot);
            if (collider) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>A wall segment with an optional rectangular opening (door/window), built from boxes.</summary>
        public static void WallWithOpening(Transform parent, Material mat, Vector3 center, float length, float height, float thickness, bool alongX,
            float openingCenter, float openingWidth, float openingBottom, float openingTop)
        {
            if (openingWidth <= 0f)
            {
                Box(parent, "Wall", mat, center, alongX ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length));
                return;
            }
            float half = length * 0.5f;
            float leftLen = openingCenter - openingWidth * 0.5f + half;
            float rightLen = half - (openingCenter + openingWidth * 0.5f);
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 size(float len, float h) => alongX ? new Vector3(len, h, thickness) : new Vector3(thickness, h, len);
            if (leftLen > 0.01f) Box(parent, "WallL", mat, center + axis * (-half + leftLen * 0.5f), size(leftLen, height));
            if (rightLen > 0.01f) Box(parent, "WallR", mat, center + axis * (half - rightLen * 0.5f), size(rightLen, height));
            float bottomY = center.y - height * 0.5f;
            if (openingBottom > 0.01f)
                Box(parent, "WallB", mat, new Vector3(0, bottomY + openingBottom * 0.5f, 0) + Flat(center) + axis * openingCenter, size(openingWidth, openingBottom));
            float topH = height - openingTop;
            if (topH > 0.01f)
                Box(parent, "WallT", mat, new Vector3(0, bottomY + openingTop + topH * 0.5f, 0) + Flat(center) + axis * openingCenter, size(openingWidth, topH));
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        /// <summary>Capsule collider matching a unit cylinder mesh (radius 0.5, y 0..1) on this object.</summary>
        public static CapsuleCollider AddCylinderCollider(this GameObject go)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.center = new Vector3(0f, 0.5f, 0f);
            c.height = 1f;
            c.radius = 0.5f;
            return c;
        }

        /// <summary>Sets the layer on a hierarchy, leaving "Hitbox" trigger children on the Interactable layer.</summary>
        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            if (go.name == "Hitbox") return;
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }
}
