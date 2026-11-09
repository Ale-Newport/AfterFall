using System;
using System.Collections.Generic;
using Afterfall.Domain.Items;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Material slots of character and held-item meshes; <see cref="HumanoidRig"/> maps them to shared materials.</summary>
    public enum CharacterSurface
    {
        Skin,
        Cloth,
        Knit,
        Leather,
        Canvas,
        Hair,
        Gloss,
        Glow,
        Wood,
        Steel,
        Stone,
        Twine
    }

    /// <summary>
    /// Bind-pose joint layout shared by the character meshes and the rig animation, in metres. The rig
    /// origin is at the feet, the character faces +Z and its right side is +X. Every bone's mesh is
    /// modelled in that bone's space, so animation only rotates rigid parts.
    /// </summary>
    public static class CharacterLayout
    {
        public const float PelvisY = 0.935f;
        public const float HipY = -0.025f;
        public const float Thigh = 0.43f;
        public const float Shin = 0.40f;
        public const float AnkleY = 0.08f;
        public const float WaistY = 0.10f;
        public const float ShoulderY = 0.378f;
        public const float ShoulderZ = -0.005f;
        public const float UpperArm = 0.28f;
        public const float Forearm = 0.25f;
        public static readonly Vector3 Neck = new Vector3(0f, 0.448f, 0.01f);

        /// <summary>Centre of the right fist in forearm space: held items are gripped here, handle along the hand's +Z.</summary>
        public static readonly Vector3 Grip = new Vector3(-0.03f, -0.36f, 0f);

        public static float HipX(HumanoidRig.Palette p) => p.Female ? 0.095f : 0.092f;

        public static float ShoulderX(HumanoidRig.Palette p, bool infected) => 0.178f * (p.Female ? 0.93f : 1f) * (infected ? 0.95f : 1f);
    }

    /// <summary>The meshes of one character look: per bone, one mesh per material slot. Shared by every rig with that look.</summary>
    public sealed class CharacterModel
    {
        private static readonly List<KeyValuePair<CharacterSurface, Mesh>> None = new List<KeyValuePair<CharacterSurface, Mesh>>();
        public readonly Dictionary<string, List<KeyValuePair<CharacterSurface, Mesh>>> Bones = new Dictionary<string, List<KeyValuePair<CharacterSurface, Mesh>>>();

        public List<KeyValuePair<CharacterSurface, Mesh>> PartsOf(string bone) => Bones.TryGetValue(bone, out var parts) ? parts : None;

        public bool Alive
        {
            get
            {
                foreach (var bone in Bones.Values)
                    foreach (var part in bone)
                        if (part.Value == null) return false;
                return true;
            }
        }
    }

    public static partial class MeshFactory
    {
        private static readonly Dictionary<string, CharacterModel> CharacterCache = new Dictionary<string, CharacterModel>();
        private static readonly Dictionary<string, List<KeyValuePair<CharacterSurface, Mesh>>> HeldCache = new Dictionary<string, List<KeyValuePair<CharacterSurface, Mesh>>>();

        /// <summary>The sculpted body (clothes, face, hair, gear) for a palette, cached per look.</summary>
        public static CharacterModel Character(HumanoidRig.Palette p, bool infected)
        {
            string key = string.Join(":", "chr", infected ? "inf" : "liv", Hex(p.Skin), Hex(p.Shirt), Hex(p.Pants), Hex(p.Shoes), Hex(p.Accent), Hex(p.Hair), Hex(p.Under), Hex(p.Eyes),
                (int)p.HairStyle, (int)p.Outfit, (int)p.Headwear, (int)p.Legwear, (int)p.Footwear, p.Female, p.Beard, p.Backpack, p.Satchel, p.Bandana, p.Variant);
            if (CharacterCache.TryGetValue(key, out CharacterModel m) && m.Alive) return m;
            m = new CharacterSculptor(p, infected).Build(key);
            CharacterCache[key] = m;
            return m;
        }

        /// <summary>Model of a tool, weapon or carried item in the fist: grip at the origin, handle along +X, edge towards −Y.</summary>
        public static List<KeyValuePair<CharacterSurface, Mesh>> HeldItem(ItemDefinition item)
        {
            string key = item.Id + ":" + (item.Tool?.Type ?? ToolType.None) + ":" + item.Color;
            if (HeldCache.TryGetValue(key, out var parts) && parts.TrueForAll(x => x.Value != null)) return parts;
            parts = HeldItemSculptor.Build(item);
            HeldCache[key] = parts;
            return parts;
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGBA(c);
    }

    /// <summary>One cross-section of a loft: centre (X, Z) at height Y, half width, front (+Z) and back depth, superellipse exponent, baked occlusion.</summary>
    internal struct Ring
    {
        public float Y, X, Z, Rx, Rf, Rb, Sq, Ao, Jag;

        public Ring(float y, float rx, float rf, float rb, float sq = 2.2f, float ao = 1f)
        {
            Y = y;
            X = 0f;
            Z = 0f;
            Rx = rx;
            Rf = rf;
            Rb = rb;
            Sq = sq;
            Ao = ao;
            Jag = 0f;
        }

        public Ring At(float x, float z)
        {
            Ring r = this;
            r.X = x;
            r.Z = z;
            return r;
        }

        public Ring Ragged(float amount)
        {
            Ring r = this;
            r.Jag = amount;
            return r;
        }

        public static Ring Lerp(Ring a, Ring b, float t) => new Ring
        {
            Y = Mathf.Lerp(a.Y, b.Y, t),
            X = Mathf.Lerp(a.X, b.X, t),
            Z = Mathf.Lerp(a.Z, b.Z, t),
            Rx = Mathf.Lerp(a.Rx, b.Rx, t),
            Rf = Mathf.Lerp(a.Rf, b.Rf, t),
            Rb = Mathf.Lerp(a.Rb, b.Rb, t),
            Sq = Mathf.Lerp(a.Sq, b.Sq, t),
            Ao = Mathf.Lerp(a.Ao, b.Ao, t)
        };
    }

    /// <summary>
    /// Organic modelling helpers on top of <see cref="MeshBuilder"/>: lofts through cross-sections, tubes
    /// along paths, shaped icospheres, slabs and blade plates, all with per-vertex colour (linear rgb,
    /// alpha = occlusion). Triangles are oriented from the normals, so mirrored coordinates are safe.
    /// </summary>
    internal static class Sculpt
    {
        private sealed class Geo
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public int Add(Vector3 p, Vector3 n, Color c)
            {
                P.Add(p);
                N.Add(n);
                C.Add(c);
                return P.Count - 1;
            }

            public void Tri(int a, int b, int c)
            {
                T.Add(a);
                T.Add(b);
                T.Add(c);
            }

            public void Quad(int a, int b, int c, int d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            public void Emit(MeshBuilder b)
            {
                int start = b.VertexCount;
                for (int i = 0; i < P.Count; i++) b.Vertex(P[i], N[i], C[i]);
                for (int t = 0; t < T.Count; t += 3)
                {
                    int i0 = T[t], i1 = T[t + 1], i2 = T[t + 2];
                    Vector3 n = Vector3.Cross(P[i1] - P[i0], P[i2] - P[i0]);
                    if (n.sqrMagnitude < 1e-30f) continue;
                    if (Vector3.Dot(n, N[i0] + N[i1] + N[i2]) < 0f)
                    {
                        int s = i1;
                        i1 = i2;
                        i2 = s;
                    }
                    b.Triangle(start + i0, start + i1, start + i2);
                }
            }
        }

        /// <summary>
        /// Normalises without Unity's 1e-5 cut-off (Vector3.normalized returns zero below it, which gives
        /// millimetre-sized details zero normals and NaN pixels in the shader).
        /// </summary>
        public static Vector3 Norm(Vector3 v, Vector3 fallback)
        {
            float m = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return m > 1e-20f ? v / m : fallback;
        }

        // ---------------------------------------------------------------- colour and noise

        public static Color Lin(Color srgb, float ao = 1f)
        {
            Color l = srgb.linear;
            return new Color(l.r, l.g, l.b, ao);
        }

        public static Color Lin(float r, float g, float b) => Lin(new Color(r, g, b));

        public static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static Color Mul(Color c, float r, float g, float b) => new Color(c.r * r, c.g * g, c.b * b, c.a);

        /// <summary>Lerps rgb and keeps the occlusion of <paramref name="a"/>.</summary>
        public static Color Mix(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a);
        }

        public static Color WithAo(Color c, float ao) => new Color(c.r, c.g, c.b, ao);

        public static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        public static float Step(float e0, float e1, float x) => Smooth01((x - e0) / (e1 - e0));

        /// <summary>Gaussian falloff between two directions (1 when equal).</summary>
        public static float Gauss(Vector3 d, Vector3 centre, float width) => Mathf.Exp(-(d - centre.normalized).sqrMagnitude / (width * width));

        /// <summary>Smooth deterministic 3D value noise in 0..1.</summary>
        public static float Noise3(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = Smooth01(p.x - x0), fy = Smooth01(p.y - y0), fz = Smooth01(p.z - z0);
            float V(int dx, int dy, int dz) => MeshFactory.Hash(seed, new Vector3(x0 + dx, y0 + dy, z0 + dz));
            float a = Mathf.Lerp(Mathf.Lerp(V(0, 0, 0), V(1, 0, 0), fx), Mathf.Lerp(V(0, 1, 0), V(1, 1, 0), fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(V(0, 0, 1), V(1, 0, 1), fx), Mathf.Lerp(V(0, 1, 1), V(1, 1, 1), fx), fy);
            return Mathf.Lerp(a, b, fz);
        }

        // ---------------------------------------------------------------- lofts

        public static Vector3 RingPoint(Ring r, float angle)
        {
            float e = 2f / Mathf.Max(1f, r.Sq);
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            float x = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), e) * r.Rx;
            float z = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), e) * (sa >= 0f ? r.Rf : r.Rb);
            return new Vector3(r.X + x, r.Y, r.Z + z);
        }

        public static Ring RingAt(IList<Ring> rings, float y)
        {
            int n = rings.Count;
            bool ascending = rings[n - 1].Y >= rings[0].Y;
            Ring Get(int i) => rings[ascending ? i : n - 1 - i];
            if (y <= Get(0).Y) return Get(0);
            for (int k = 0; k + 1 < n; k++)
                if (y <= Get(k + 1).Y) return Ring.Lerp(Get(k), Get(k + 1), Mathf.InverseLerp(Get(k).Y, Get(k + 1).Y, y));
            return Get(n - 1);
        }

        /// <summary>
        /// A point on a loft's surface at height <paramref name="y"/> and angle (degrees: 0 = +X, 90 = front),
        /// lifted along the outward normal. Used to place pockets, straps and patches on curved clothes.
        /// </summary>
        public static Vector3 Surf(IList<Ring> rings, float y, float angleDeg, float lift, out Vector3 normal)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            Ring r = RingAt(rings, y);
            Vector3 p = RingPoint(r, a);
            Vector3 around = RingPoint(r, a + 0.02f) - RingPoint(r, a - 0.02f);
            Vector3 along = RingPoint(RingAt(rings, y + 0.01f), a) - RingPoint(RingAt(rings, y - 0.01f), a);
            normal = Vector3.Cross(along, around);
            Vector3 outward = p - new Vector3(r.X, p.y, r.Z);
            if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
            normal = Norm(normal, Norm(outward, Vector3.forward));
            return p + normal * lift;
        }

        public static Vector3 Surf(IList<Ring> rings, float y, float angleDeg, float lift = 0f) => Surf(rings, y, angleDeg, lift, out _);

        /// <summary>
        /// A smooth tube through superellipse cross-sections stacked along +Y (bottom to top). The paint
        /// callback gets each vertex position (after <see cref="MeshBuilder.Matrix"/>); its alpha is multiplied by the ring's occlusion.
        /// </summary>
        public static void Loft(MeshBuilder b, IList<Ring> rings, int sides, Func<Vector3, Color> paint, bool capBottom = false, bool capTop = false, int seed = 0)
        {
            int nr = rings.Count;
            var grid = new Vector3[nr * sides];
            for (int k = 0; k < nr; k++)
            {
                Ring r = rings[k];
                for (int s = 0; s < sides; s++)
                {
                    Vector3 p = RingPoint(r, s * Mathf.PI * 2f / sides);
                    if (r.Jag > 0f)
                    {
                        float h = MeshFactory.Hash(seed, new Vector3(s, 1f, 0f));
                        p.y -= r.Jag * h * h * (s % 3 == 0 ? 1f : 0.4f);
                    }
                    grid[k * sides + s] = p;
                }
            }
            var g = new Geo();
            for (int k = 0; k < nr; k++)
            {
                for (int s = 0; s < sides; s++)
                {
                    Vector3 p = grid[k * sides + s];
                    Vector3 along = grid[Mathf.Min(k + 1, nr - 1) * sides + s] - grid[Mathf.Max(k - 1, 0) * sides + s];
                    Vector3 around = grid[k * sides + (s + 1) % sides] - grid[k * sides + (s + sides - 1) % sides];
                    Vector3 outward = p - new Vector3(rings[k].X, p.y, rings[k].Z);
                    Vector3 n = Vector3.Cross(along, around);
                    if (n.sqrMagnitude < 1e-30f) n = outward;
                    if (Vector3.Dot(n, outward) < 0f) n = -n;
                    Color c = paint != null ? paint(b.Matrix.MultiplyPoint3x4(p)) : b.Color;
                    c.a *= rings[k].Ao;
                    g.Add(p, Norm(n, Vector3.up), c);
                }
            }
            for (int k = 0; k + 1 < nr; k++)
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    g.Quad(k * sides + s, (k + 1) * sides + s, (k + 1) * sides + s1, k * sides + s1);
                }
            if (capBottom) Cap(g, grid, 0, sides, CentreOf(rings[0]) - CentreOf(rings[Mathf.Min(1, nr - 1)]), paint, b);
            if (capTop) Cap(g, grid, nr - 1, sides, CentreOf(rings[nr - 1]) - CentreOf(rings[Mathf.Max(0, nr - 2)]), paint, b);
            g.Emit(b);
        }

        private static Vector3 CentreOf(Ring r) => new Vector3(r.X, r.Y, r.Z);

        private static void Cap(Geo g, Vector3[] grid, int k, int sides, Vector3 normal, Func<Vector3, Color> paint, MeshBuilder b)
        {
            normal = Norm(normal, Vector3.up);
            Vector3 centre = Vector3.zero;
            for (int s = 0; s < sides; s++) centre += grid[k * sides + s];
            centre /= sides;
            Color c = paint != null ? paint(b.Matrix.MultiplyPoint3x4(centre)) : b.Color;
            int ci = g.Add(centre, normal, c);
            int first = g.P.Count;
            for (int s = 0; s < sides; s++) g.Add(grid[k * sides + s], normal, c);
            for (int s = 0; s < sides; s++) g.Tri(ci, first + s, first + (s + 1) % sides);
        }

        // ---------------------------------------------------------------- tubes

        private static Vector3 Tangent(IList<Vector3> path, int i, bool closed)
        {
            int n = path.Count;
            Vector3 t;
            if (closed) t = path[(i + 1) % n] - path[(i + n - 1) % n];
            else if (i == 0) t = path[1] - path[0];
            else if (i == n - 1) t = path[n - 1] - path[n - 2];
            else t = path[i + 1] - path[i - 1];
            return Norm(t, Vector3.up);
        }

        /// <summary>
        /// A smooth tube along a path with an elliptical section: <paramref name="rx"/> along the side axis
        /// (tangent × up), <paramref name="ry"/> along <paramref name="up"/>, which is carried along the path
        /// by parallel transport. Flat straps are wide tubes with a tiny thickness.
        /// </summary>
        public static void Tube(MeshBuilder b, IList<Vector3> path, IList<float> rx, IList<float> ry, Vector3 up, int sides, Func<Vector3, Color> paint,
            bool caps = true, bool closed = false)
        {
            int n = path.Count;
            if (n < 2) return;
            var g = new Geo();
            Vector3 u = up;
            Vector3 t0 = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = Tangent(path, i, closed);
                u -= t * Vector3.Dot(u, t);
                if (u.sqrMagnitude < 1e-8f) u = Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right);
                u = Norm(u, Vector3.up);
                if (i == 0) t0 = t;
                Vector3 side = Norm(Vector3.Cross(u, t), Vector3.right);
                float ax = Mathf.Max(rx[i], 1e-4f), ay = Mathf.Max(ry[i], 1e-4f);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides + Mathf.PI / sides;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 p = path[i] + side * (ca * ax) + u * (sa * ay);
                    Vector3 nrm = Norm(side * (ca / ax) + u * (sa / ay), side);
                    g.Add(p, nrm, paint != null ? paint(b.Matrix.MultiplyPoint3x4(p)) : b.Color);
                }
            }
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int i1 = (i + 1) % n;
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    g.Quad(i * sides + s, i1 * sides + s, i1 * sides + s1, i * sides + s1);
                }
            }
            if (caps && !closed)
            {
                var ends = new Vector3[sides * n];
                for (int i = 0; i < g.P.Count; i++) ends[i] = g.P[i];
                Cap(g, ends, 0, sides, -t0, paint, b);
                Cap(g, ends, n - 1, sides, Tangent(path, n - 1, false), paint, b);
            }
            g.Emit(b);
        }

        /// <summary>A closed ring of tube around a centre, in the plane spanned by <paramref name="axisA"/> and <paramref name="axisB"/>.</summary>
        public static void Loop(MeshBuilder b, Vector3 centre, Vector3 axisA, Vector3 axisB, float ra, float rb, float thickness, float width, int segments, Color color)
        {
            var path = new Vector3[segments];
            var rx = new float[segments];
            var ry = new float[segments];
            Vector3 normal = Vector3.Cross(axisA, axisB).normalized;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                path[i] = centre + axisA * (Mathf.Cos(a) * ra) + axisB * (Mathf.Sin(a) * rb);
                rx[i] = thickness;
                ry[i] = width;
            }
            Tube(b, path, rx, ry, normal, 6, _ => color, false, true);
        }

        // ---------------------------------------------------------------- shaped spheres

        private static readonly Dictionary<int, KeyValuePair<List<Vector3>, List<int>>> Icos = new Dictionary<int, KeyValuePair<List<Vector3>, List<int>>>();

        private static void Icosphere(int subdivisions, out List<Vector3> verts, out List<int> tris)
        {
            if (Icos.TryGetValue(subdivisions, out var cached))
            {
                verts = cached.Key;
                tris = cached.Value;
                return;
            }
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
            Icos[subdivisions] = new KeyValuePair<List<Vector3>, List<int>>(verts, tris);
        }

        private static int Mid(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }

        /// <summary>
        /// A shaped icosphere: <paramref name="shape"/> maps each unit direction to an offset from the centre
        /// (default: the ellipsoid radii); <paramref name="paint"/> gets the placed position and the direction.
        /// </summary>
        public static void Ellipsoid(MeshBuilder b, Vector3 center, Vector3 radii, int subdiv, Func<Vector3, Vector3> shape, Func<Vector3, Vector3, Color> paint,
            bool smooth = true, Quaternion? rotation = null)
        {
            Icosphere(subdiv, out List<Vector3> dirs, out List<int> tris);
            Quaternion q = rotation ?? Quaternion.identity;
            var pts = new Vector3[dirs.Count];
            for (int k = 0; k < dirs.Count; k++) pts[k] = center + q * (shape != null ? shape(dirs[k]) : Vector3.Scale(dirs[k], radii));
            var g = new Geo();
            Color Paint(Vector3 p, Vector3 d) => paint != null ? paint(b.Matrix.MultiplyPoint3x4(p), d) : b.Color;
            if (smooth)
            {
                var nrm = new Vector3[pts.Length];
                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 a = pts[tris[t]], bb = pts[tris[t + 1]], c = pts[tris[t + 2]];
                    Vector3 fn = Vector3.Cross(bb - a, c - a);
                    if (Vector3.Dot(fn, (a + bb + c) / 3f - center) < 0f) fn = -fn;
                    nrm[tris[t]] += fn;
                    nrm[tris[t + 1]] += fn;
                    nrm[tris[t + 2]] += fn;
                }
                for (int k = 0; k < pts.Length; k++) g.Add(pts[k], Norm(nrm[k], q * dirs[k]), Paint(pts[k], dirs[k]));
                for (int t = 0; t < tris.Count; t += 3) g.Tri(tris[t], tris[t + 1], tris[t + 2]);
            }
            else
            {
                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 a = pts[tris[t]], bb = pts[tris[t + 1]], c = pts[tris[t + 2]];
                    Vector3 fn = Vector3.Cross(bb - a, c - a);
                    if (fn.sqrMagnitude < 1e-30f) continue;
                    if (Vector3.Dot(fn, (a + bb + c) / 3f - center) < 0f) fn = -fn;
                    fn = Norm(fn, Vector3.up);
                    Color col = Paint((a + bb + c) / 3f, (dirs[tris[t]] + dirs[tris[t + 1]] + dirs[tris[t + 2]]).normalized);
                    int i = g.Add(a, fn, col);
                    g.Add(bb, fn, col);
                    g.Add(c, fn, col);
                    g.Tri(i, i + 1, i + 2);
                }
            }
            g.Emit(b);
        }

        public static void Ball(MeshBuilder b, Vector3 center, Vector3 radii, Color color, int subdiv = 1, Quaternion? rotation = null) =>
            Ellipsoid(b, center, radii, subdiv, null, (p, d) => color, true, rotation);

        // ---------------------------------------------------------------- flat solids

        /// <summary>A flat convex polygon facing away from <paramref name="inside"/>, in <see cref="MeshBuilder.Color"/> (robust for tiny faces).</summary>
        public static void Face(MeshBuilder b, Vector3 inside, params Vector3[] poly)
        {
            if (poly.Length < 3) return;
            Vector3 centroid = Vector3.zero, n = Vector3.zero;
            for (int i = 0; i < poly.Length; i++)
            {
                centroid += poly[i];
                Vector3 p0 = poly[i], p1 = poly[(i + 1) % poly.Length];
                n += new Vector3((p0.y - p1.y) * (p0.z + p1.z), (p0.z - p1.z) * (p0.x + p1.x), (p0.x - p1.x) * (p0.y + p1.y));
            }
            centroid /= poly.Length;
            if (n.sqrMagnitude < 1e-30f) return;
            if (Vector3.Dot(n, centroid - inside) < 0f) n = -n;
            n = Norm(n, Vector3.up);
            var g = new Geo();
            foreach (Vector3 p in poly) g.Add(p, n, b.Color);
            for (int i = 1; i + 1 < poly.Length; i++) g.Tri(0, i, i + 1);
            g.Emit(b);
        }

        /// <summary>A box with chamfered edges (the shape of <see cref="MeshBuilder.Box"/>, robust for millimetre details), in <see cref="MeshBuilder.Color"/>.</summary>
        public static void Box(MeshBuilder b, Vector3 center, Vector3 size, Quaternion? rotation = null, float bevel = 0f)
        {
            Quaternion q = rotation ?? Quaternion.identity;
            Vector3 h = size * 0.5f;
            float bv = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            Vector3 i = h - Vector3.one * bv;
            Vector3 W(Vector3 v) => center + q * v;
            for (int axis = 0; axis < 3; axis++)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                for (int s = -1; s <= 1; s += 2)
                {
                    var poly = new Vector3[4];
                    for (int k = 0; k < 4; k++)
                    {
                        float u = (k == 0 || k == 3) ? -1f : 1f, w = k < 2 ? -1f : 1f;
                        var v = new Vector3();
                        v[axis] = s * h[axis];
                        v[a1] = u * i[a1];
                        v[a2] = w * i[a2];
                        poly[k] = W(v);
                    }
                    Face(b, center, poly);
                }
                if (bv <= 0f) continue;
                for (int s1 = -1; s1 <= 1; s1 += 2)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        Vector3 E(float along, bool onA1)
                        {
                            var v = new Vector3();
                            v[axis] = along;
                            v[a1] = s1 * (onA1 ? h[a1] : i[a1]);
                            v[a2] = s2 * (onA1 ? i[a2] : h[a2]);
                            return W(v);
                        }
                        Face(b, center, E(-i[axis], true), E(i[axis], true), E(i[axis], false), E(-i[axis], false));
                    }
            }
            if (bv <= 0f) return;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Face(b, center, W(new Vector3(sx * h.x, sy * i.y, sz * i.z)), W(new Vector3(sx * i.x, sy * h.y, sz * i.z)), W(new Vector3(sx * i.x, sy * i.y, sz * h.z)));
        }

        /// <summary>A capped cylinder between two points, in <see cref="MeshBuilder.Color"/>.</summary>
        public static void Cylinder(MeshBuilder b, Vector3 bottom, Vector3 top, float radius, int sides)
        {
            Vector3 axis = Norm(top - bottom, Vector3.up);
            Color c = b.Color;
            Tube(b, new[] { bottom, top }, new[] { radius, radius }, new[] { radius, radius }, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right, sides, _ => c);
        }

        /// <summary>A convex outline in the XZ plane extruded from <paramref name="y0"/> to <paramref name="y1"/> (soles, brims).</summary>
        public static void Slab(MeshBuilder b, IList<Vector2> outline, float y0, float y1, Color color)
        {
            int n = outline.Count;
            var top = new Vector3[n];
            var bottom = new Vector3[n];
            Vector3 inside = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                top[i] = new Vector3(outline[i].x, y1, outline[i].y);
                bottom[i] = new Vector3(outline[i].x, y0, outline[i].y);
                inside += top[i] + bottom[i];
            }
            inside /= 2 * n;
            Color saved = b.Color;
            b.Color = color;
            Face(b, inside, top);
            Face(b, inside, bottom);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Face(b, inside, bottom[i], bottom[j], top[j], top[i]);
            }
            b.Color = saved;
        }

        /// <summary>
        /// A blade or axe head: a star-shaped outline in the XY plane, thick along Z. Inner rings (fractions of
        /// the outline towards <paramref name="center"/>) let the edge be ground thinner than the body.
        /// </summary>
        public static void Plate(MeshBuilder b, Vector2 center, IList<Vector2> outline, float[] fractions, Func<Vector2, int, float> halfThickness, Func<Vector2, int, Color> paint)
        {
            int n = outline.Count, nr = fractions.Length;
            Vector2 R(int k, int i) => center + (outline[i % n] - center) * fractions[k];
            Vector3 P(int k, int i, float side)
            {
                Vector2 q = R(k, i);
                return new Vector3(q.x, q.y, side * halfThickness(q, k));
            }
            var inside = new Vector3(center.x, center.y, 0f);
            Color saved = b.Color;
            float tc = halfThickness(center, nr);
            foreach (float side in new[] { 1f, -1f })
            {
                for (int k = 0; k + 1 < nr; k++)
                    for (int i = 0; i < n; i++)
                    {
                        b.Color = paint((R(k, i) + R(k + 1, i)) * 0.5f, k);
                        Face(b, inside, P(k, i, side), P(k, i + 1, side), P(k + 1, i + 1, side), P(k + 1, i, side));
                    }
                for (int i = 0; i < n; i++)
                {
                    b.Color = paint(R(nr - 1, i), nr - 1);
                    Face(b, inside, P(nr - 1, i, side), P(nr - 1, i + 1, side), new Vector3(center.x, center.y, side * tc));
                }
            }
            for (int i = 0; i < n; i++)
            {
                b.Color = paint(R(0, i), 0);
                Face(b, inside, P(0, i, 1f), P(0, i + 1, 1f), P(0, i + 1, -1f), P(0, i, -1f));
            }
            b.Color = saved;
        }
    }

    /// <summary>
    /// Sculpts one character look from its palette: layered clothes on a tapered body, a face with
    /// jaw, nose, ears, lids and brows, hair and headwear, boots, gear, and the infected's wounds.
    /// Everything is modelled in bone space at the bind pose of <see cref="CharacterLayout"/>.
    /// </summary>
    internal sealed class CharacterSculptor
    {
        private readonly HumanoidRig.Palette _p;
        private readonly bool _inf;
        private readonly int _seed;
        private readonly float _girth, _armGirth, _hipX, _shoulderX;
        private readonly Vector3 _skullC, _skullR;
        private readonly Color _skin, _jacket, _sleeve, _pants, _boots, _accent, _hair, _under, _iris, _metal, _blood;
        private readonly Dictionary<string, Dictionary<CharacterSurface, MeshBuilder>> _bones = new Dictionary<string, Dictionary<CharacterSurface, MeshBuilder>>();
        private readonly Dictionary<string, Vector3> _origins = new Dictionary<string, Vector3>();
        private Ring[] _torso, _pelvis;
        private float _hem;

        public CharacterSculptor(HumanoidRig.Palette p, bool infected)
        {
            _p = p;
            _inf = infected;
            _seed = 7919 * (p.Variant + 3) + (infected ? 104729 : 0) + (int)p.Outfit * 131 + (int)p.HairStyle * 17;
            _girth = (infected ? 0.9f : 1f) * (p.Female ? 0.95f : 1f);
            _armGirth = infected ? 0.86f : (p.Female ? 0.96f : 1.08f);
            _hipX = CharacterLayout.HipX(p);
            _shoulderX = CharacterLayout.ShoulderX(p, infected);
            float hs = p.Female ? 0.965f : 1f;
            _skullC = new Vector3(0f, 0.158f, 0.01f);
            _skullR = infected ? new Vector3(0.086f, 0.112f, 0.099f) * hs : new Vector3(0.09f, 0.109f, 0.1f) * hs;

            _skin = Sculpt.Lin(p.Skin);
            _jacket = Sculpt.Lin(p.Shirt);
            _under = Sculpt.Lin(p.Under.a > 0f ? p.Under : new Color(0.55f, 0.54f, 0.5f));
            _sleeve = p.Outfit == HumanoidRig.Outfit.Vest ? _under : _jacket;
            _pants = Sculpt.Lin(p.Pants);
            _boots = Sculpt.Lin(p.Shoes);
            _accent = Sculpt.Lin(p.Accent);
            _hair = Sculpt.Lin(p.Hair.a > 0f ? p.Hair : new Color(0.2f, 0.14f, 0.1f));
            _iris = Sculpt.Lin(p.Eyes.a > 0f ? p.Eyes : new Color(0.3f, 0.2f, 0.12f));
            _metal = Sculpt.Lin(0.62f, 0.6f, 0.55f);
            _blood = Sculpt.Lin(0.28f, 0.05f, 0.04f);

            var L = new Vector3(0f, CharacterLayout.PelvisY, 0f);
            Vector3 chest = L + new Vector3(0f, CharacterLayout.WaistY, 0f);
            _origins["Pelvis"] = L;
            _origins["Chest"] = chest;
            _origins["Head"] = chest + CharacterLayout.Neck;
            foreach (int sx in new[] { -1, 1 })
            {
                string s = Side(sx);
                Vector3 shoulder = chest + new Vector3(sx * _shoulderX, CharacterLayout.ShoulderY, CharacterLayout.ShoulderZ);
                _origins["UpperArm" + s] = shoulder;
                _origins["Forearm" + s] = shoulder + Vector3.down * CharacterLayout.UpperArm;
                Vector3 hip = L + new Vector3(sx * _hipX, CharacterLayout.HipY, 0f);
                _origins["Thigh" + s] = hip;
                _origins["Shin" + s] = hip + Vector3.down * CharacterLayout.Thigh;
                _origins["Foot" + s] = hip + Vector3.down * (CharacterLayout.Thigh + CharacterLayout.Shin);
            }
        }

        private static string Side(int sx) => sx > 0 ? "R" : "L";

        private bool Is(HumanoidRig.Outfit o) => _p.Outfit == o;

        private MeshBuilder B(string bone, CharacterSurface s)
        {
            if (!_bones.TryGetValue(bone, out var bySurface)) _bones[bone] = bySurface = new Dictionary<CharacterSurface, MeshBuilder>();
            if (!bySurface.TryGetValue(s, out MeshBuilder b)) bySurface[s] = b = new MeshBuilder();
            return b;
        }

        public CharacterModel Build(string key)
        {
            Torso();
            Pelvis();
            foreach (int sx in new[] { -1, 1 })
            {
                Thigh(sx);
                Shin(sx);
                Foot(sx);
                UpperArm(sx);
                Forearm(sx);
            }
            Head();
            if (_p.Backpack) Backpack();
            if (_p.Satchel) Satchel();
            if (_p.Bandana) Bandana();

            var model = new CharacterModel();
            foreach (var bone in _bones)
            {
                var list = new List<KeyValuePair<CharacterSurface, Mesh>>();
                foreach (var part in bone.Value)
                {
                    if (part.Value.VertexCount == 0) continue;
                    Mesh mesh = part.Value.Build($"{key}:{bone.Key}:{part.Key}");
                    Weather(mesh, _origins[bone.Key], part.Key);
                    list.Add(new KeyValuePair<CharacterSurface, Mesh>(part.Key, mesh));
                }
                model.Bones[bone.Key] = list;
            }
            return model;
        }

        // ---------------------------------------------------------------- weathering

        /// <summary>Grime towards the ground, fabric wear, and for the infected torn holes, blood and bruises; occlusion near the ground.</summary>
        private void Weather(Mesh mesh, Vector3 origin, CharacterSurface s)
        {
            if (s == CharacterSurface.Glow || s == CharacterSurface.Gloss) return;
            Vector3[] verts = mesh.vertices;
            Color[] cols = mesh.colors;
            Color mud = Sculpt.Lin(0.24f, 0.2f, 0.15f);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 q = origin + verts[i];
                Color c = cols[i];
                float n1 = Sculpt.Noise3(q * 9f, _seed);
                float n2 = Sculpt.Noise3(q * 3.3f, _seed + 7);
                float low = 1f - Sculpt.Step(0.04f, 0.6f, q.y);
                bool cloth = s == CharacterSurface.Cloth || s == CharacterSurface.Canvas || s == CharacterSurface.Knit || s == CharacterSurface.Leather;
                if (cloth)
                {
                    c = Sculpt.Shade(c, 0.93f + n1 * 0.12f);
                    float dirt = low * (0.35f + n2 * 0.4f) + (_inf ? 0.15f + n2 * 0.25f : 0.04f * n2);
                    c = Sculpt.Mix(c, Sculpt.Shade(mud, 0.5f + (c.r + c.g + c.b) * 0.5f), dirt);
                    if (_inf)
                    {
                        float grey = (c.r + c.g + c.b) / 3f;
                        c = Sculpt.Mix(c, new Color(grey, grey, grey), 0.3f);
                        // Tears on the body of a top and the lower trouser legs, not on seats or hoods.
                        if (s == CharacterSurface.Cloth && ((q.y > 1.08f && q.y < 1.42f) || q.y < 0.6f))
                        {
                            float tear = Sculpt.Noise3(q * 6.5f, _seed + 3);
                            float hole = Sculpt.Step(0.67f, 0.7f, tear);
                            float rim = Sculpt.Step(0.61f, 0.67f, tear) * (1f - hole);
                            c = Sculpt.Shade(c, 1f - rim * 0.55f);
                            c = Sculpt.Mix(c, Sculpt.Mul(_skin, 0.85f, 0.8f, 0.8f), hole);
                        }
                        float splat = Sculpt.Noise3(q * 4.2f, _seed + 11) * 0.8f + Sculpt.Noise3(q * 15f, _seed + 12) * 0.2f;
                        splat += q.y > 1.2f && q.z > 0.04f ? 0.12f : 0f;
                        c = Sculpt.Mix(c, _blood, Sculpt.Step(0.68f, 0.74f, splat) * 0.7f);
                    }
                }
                else if (s == CharacterSurface.Skin && _inf)
                {
                    float bruise = Sculpt.Noise3(q * 7f, _seed + 5);
                    c = Sculpt.Mix(c, Sculpt.Lin(0.32f, 0.27f, 0.3f), Sculpt.Step(0.6f, 0.75f, bruise) * 0.6f);
                    c = Sculpt.Mix(c, _blood, Sculpt.Step(0.74f, 0.8f, Sculpt.Noise3(q * 5f, _seed + 13)) * 0.9f);
                }
                else if (s == CharacterSurface.Skin)
                {
                    c = Sculpt.Shade(c, 0.97f + n1 * 0.06f);
                }
                c.a *= Mathf.Lerp(0.72f, 1f, Sculpt.Step(0f, 0.55f, q.y));
                cols[i] = c;
            }
            mesh.colors = cols;
        }

        // ---------------------------------------------------------------- torso

        private float VStart
        {
            get
            {
                switch (_p.Outfit)
                {
                    case HumanoidRig.Outfit.FieldJacket: return 0.33f;
                    case HumanoidRig.Outfit.WorkJacket: return 0.3f;
                    case HumanoidRig.Outfit.Trucker: return 0.31f;
                    case HumanoidRig.Outfit.Vest: return 0.24f;
                    case HumanoidRig.Outfit.DressShirt: return 0.37f;
                    default: return 2f;
                }
            }
        }

        private void Torso()
        {
            float g = _girth, f = _p.Female ? 1f : 0f, sh = _shoulderX / 0.178f;
            _hem = Is(HumanoidRig.Outfit.Hoodie) ? -0.085f : Is(HumanoidRig.Outfit.DressShirt) ? -0.03f : -0.04f;
            float ragged = _inf ? 0.03f : 0f;
            _torso = new[]
            {
                new Ring(_hem, 0.163f * g, 0.104f * g, 0.106f * g, 2.6f, 0.85f).Ragged(ragged * 1.4f),
                new Ring(_hem + 0.035f, 0.158f * g, 0.102f * g, 0.104f * g, 2.6f),
                new Ring(0.1f, (0.152f - 0.014f * f) * g, 0.104f * g, 0.1f * g, 2.6f),
                new Ring(0.2f, (0.165f - 0.008f * f) * g * sh, (0.116f + 0.014f * f) * g, 0.1f * g, 2.6f),
                new Ring(0.28f, 0.173f * g * sh, (0.118f + 0.01f * f) * g, 0.1f * g, 2.6f),
                new Ring(0.34f, 0.175f * g * sh, 0.108f * g, 0.097f * g, 2.6f),
                new Ring(0.38f, 0.176f * g * sh, 0.093f * g, 0.089f * g, 2.5f),
                new Ring(0.415f, 0.138f * sh, 0.077f, 0.08f, 2.4f),
                new Ring(0.443f, 0.085f, 0.06f, 0.063f, 2.2f),
                new Ring(0.458f, 0.058f, 0.05f, 0.054f, 2.1f, 0.7f)
            };
            MeshBuilder cloth = B("Chest", CharacterSurface.Cloth);
            float vs = VStart;
            Color hemBand = Sculpt.Shade(_jacket, 0.8f);
            Sculpt.Loft(cloth, _torso, 26, q =>
            {
                Color c = _jacket;
                if (q.y < _hem + 0.035f && !Is(HumanoidRig.Outfit.DressShirt)) c = hemBand;
                if (q.z < -0.02f && q.y > 0.3f) c = Sculpt.Shade(c, 0.9f);
                if (q.z > 0.02f && q.y > vs && Mathf.Abs(q.x) < (q.y - vs) * 0.6f) c = Is(HumanoidRig.Outfit.DressShirt) ? _skin : _under;
                if (Is(HumanoidRig.Outfit.Vest) && q.y < 0.37f && Mathf.Repeat(q.y, 0.06f) < 0.008f) c = Sculpt.Shade(c, 0.78f);
                return c;
            }, false, true, _seed);

            // Lapels define the V of an open collar crisply.
            if (vs < 1f && !Is(HumanoidRig.Outfit.Hoodie))
            {
                Color lapel = Is(HumanoidRig.Outfit.WorkJacket) ? Sculpt.Shade(_accent, 0.9f) : Sculpt.Shade(_jacket, 0.88f);
                foreach (int sx in new[] { -1, 1 })
                {
                    var path = new List<Vector3>();
                    for (int i = 0; i <= 4; i++)
                    {
                        float y = Mathf.Lerp(vs, 0.43f, i / 4f);
                        float x = sx * (y - vs) * 0.6f;
                        path.Add(OnTorsoFront(x, y, 0.004f));
                    }
                    Sculpt.Tube(cloth, path, Same(5, 0.011f), Same(5, 0.003f), Vector3.forward, 6, _ => lapel);
                }
            }

            Collar(cloth);
            Closure(cloth);
            Pockets(cloth);
            if (Is(HumanoidRig.Outfit.DressShirt)) Tie(cloth);
            if (Is(HumanoidRig.Outfit.Hoodie)) Hood(cloth);
            if (Is(HumanoidRig.Outfit.FieldJacket))
            {
                // Shoulder epaulettes.
                foreach (int sx in new[] { -1, 1 })
                {
                    var path = new[] { new Vector3(sx * 0.07f, 0.452f, -0.006f), new Vector3(sx * 0.12f, 0.432f, -0.006f), new Vector3(sx * 0.165f, 0.405f, -0.006f) };
                    Sculpt.Tube(cloth, path, Same(3, 0.018f), Same(3, 0.004f), Vector3.up, 6, _ => Sculpt.Shade(_jacket, 0.85f));
                }
            }
        }

        private static float[] Same(int n, float v)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        private static float[] Ramp(int n, float from, float to)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = Mathf.Lerp(from, to, n > 1 ? i / (float)(n - 1) : 0f);
            return a;
        }

        /// <summary>A point on the front of the torso at (x, y), lifted along the surface normal.</summary>
        private Vector3 OnTorsoFront(float x, float y, float lift)
        {
            Ring r = Sculpt.RingAt(_torso, y);
            float t = Mathf.Clamp(x / Mathf.Max(0.01f, r.Rx), -0.98f, 0.98f);
            // Superellipse: find the angle whose x matches.
            float e = 2f / r.Sq;
            float ca = Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), 1f / e);
            float angle = Mathf.Acos(Mathf.Clamp(ca, -1f, 1f)) * Mathf.Rad2Deg;
            return Sculpt.Surf(_torso, y, angle, lift);
        }

        private void Collar(MeshBuilder cloth)
        {
            Color col = Is(HumanoidRig.Outfit.Vest) || Is(HumanoidRig.Outfit.DressShirt) ? _under : Sculpt.Shade(_jacket, 0.9f);
            if (Is(HumanoidRig.Outfit.DressShirt)) col = Sculpt.Shade(_jacket, 0.95f);
            if (Is(HumanoidRig.Outfit.WorkJacket)) col = Sculpt.Shade(_accent, 0.85f);
            if (Is(HumanoidRig.Outfit.Hoodie)) return;
            bool stand = Is(HumanoidRig.Outfit.FieldJacket) || Is(HumanoidRig.Outfit.Vest);
            float open = stand ? 34f : 46f;
            var path = new List<Vector3>();
            for (int i = 0; i <= 14; i++)
            {
                float a = (90f + open + i * (360f - 2f * open) / 14f) * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(a) * 0.071f, stand ? 0.462f : 0.452f, -0.004f + Mathf.Sin(a) * 0.066f));
            }
            Sculpt.Tube(cloth, path, Same(path.Count, stand ? 0.008f : 0.011f), Same(path.Count, stand ? 0.03f : 0.022f), Vector3.up, 8, q => Sculpt.WithAo(col, q.z > 0.03f ? 1f : 0.85f));
            if (!stand)
            {
                // Collar points folded down onto the chest.
                foreach (int sx in new[] { -1, 1 })
                {
                    Vector3 a = OnTorsoFront(sx * 0.05f, 0.44f, 0.006f);
                    Vector3 b = OnTorsoFront(sx * 0.075f, 0.385f, 0.006f);
                    Vector3 c = OnTorsoFront(sx * 0.03f, 0.4f, 0.006f);
                    Sculpt.Tube(cloth, new[] { a, Vector3.Lerp(a, b, 0.5f) * 0.6f + c * 0.4f, b }, new[] { 0.02f, 0.016f, 0.005f }, Same(3, 0.004f), Vector3.forward, 6, _ => col);
                }
            }
        }

        private void Closure(MeshBuilder cloth)
        {
            float top = Mathf.Min(VStart, 0.42f);
            if (Is(HumanoidRig.Outfit.FieldJacket) || Is(HumanoidRig.Outfit.Hoodie) || Is(HumanoidRig.Outfit.Vest))
            {
                // Zipper: dark tape with a pull tab.
                if (Is(HumanoidRig.Outfit.Hoodie)) top = 0.4f;
                var path = new List<Vector3>();
                for (int i = 0; i <= 8; i++) path.Add(OnTorsoFront(0f, Mathf.Lerp(_hem + 0.005f, top, i / 8f), 0.002f));
                Color tape = Sculpt.Lin(0.1f, 0.1f, 0.09f);
                Sculpt.Tube(cloth, path, Same(path.Count, 0.007f), Same(path.Count, 0.0025f), Vector3.forward, 4, _ => tape);
                Vector3 pull = OnTorsoFront(0f, top - 0.025f, 0.006f);
                cloth.Color = _metal;
                Sculpt.Box(cloth, pull, new Vector3(0.008f, 0.024f, 0.004f), null, 0.0015f);
            }
            else
            {
                // Buttons down the placket.
                Color button = Is(HumanoidRig.Outfit.DressShirt) ? Sculpt.Lin(0.8f, 0.78f, 0.72f) : Sculpt.Lin(0.35f, 0.3f, 0.24f);
                if (Is(HumanoidRig.Outfit.Trucker)) button = _metal;
                for (float y = _hem + 0.06f; y < top - 0.01f; y += 0.075f)
                {
                    Vector3 p = OnTorsoFront(0f, y, 0.002f);
                    Sculpt.Ball(cloth, p, new Vector3(0.008f, 0.008f, 0.003f), button);
                }
                var path = new List<Vector3>();
                for (int i = 0; i <= 6; i++) path.Add(OnTorsoFront(-0.004f, Mathf.Lerp(_hem + 0.005f, top, i / 6f), 0.0015f));
                Sculpt.Tube(cloth, path, Same(path.Count, 0.014f), Same(path.Count, 0.002f), Vector3.forward, 4, _ => Sculpt.Shade(_jacket, 0.88f));
            }
        }

        /// <summary>A patch pocket with a flap, sitting on the torso surface.</summary>
        private void Pocket(MeshBuilder b, float x, float y, float w, float h, Color body, bool flap, bool pointedFlap = false)
        {
            Vector3 p = OnTorsoFront(x, y, 0f);
            Ring r = Sculpt.RingAt(_torso, y);
            float t = Mathf.Clamp(x / r.Rx, -0.95f, 0.95f);
            float e = 2f / r.Sq;
            float angle = Mathf.Acos(Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), 1f / e)) * Mathf.Rad2Deg;
            Sculpt.Surf(_torso, y, angle, 0f, out Vector3 n);
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            b.Color = body;
            Sculpt.Box(b, p + n * 0.002f, new Vector3(w, h, 0.012f), rot, 0.004f);
            if (!flap) return;
            b.Color = Sculpt.Shade(body, 0.86f);
            Vector3 fp = OnTorsoFront(x, y + h * 0.5f - 0.008f, 0.009f);
            Sculpt.Box(b, fp, new Vector3(w + 0.006f, pointedFlap ? 0.03f : 0.026f, 0.008f), rot, 0.003f);
            b.Color = Is(HumanoidRig.Outfit.Trucker) ? _metal : Sculpt.Shade(body, 0.55f);
            Sculpt.Box(b, fp + n * 0.004f + Vector3.down * 0.006f, new Vector3(0.009f, 0.009f, 0.004f), rot, 0.002f);
        }

        private void Pockets(MeshBuilder cloth)
        {
            Color body = Sculpt.Shade(_jacket, 0.95f);
            switch (_p.Outfit)
            {
                case HumanoidRig.Outfit.FieldJacket:
                    Pocket(cloth, -0.075f, 0.22f, 0.07f, 0.085f, body, true);
                    Pocket(cloth, 0.075f, 0.22f, 0.07f, 0.085f, body, true);
                    Pocket(cloth, -0.085f, 0.05f, 0.08f, 0.08f, body, true);
                    Pocket(cloth, 0.085f, 0.05f, 0.08f, 0.08f, body, true);
                    break;
                case HumanoidRig.Outfit.Trucker:
                    Pocket(cloth, -0.075f, 0.23f, 0.065f, 0.07f, body, true, true);
                    Pocket(cloth, 0.075f, 0.23f, 0.065f, 0.07f, body, true, true);
                    break;
                case HumanoidRig.Outfit.WorkJacket:
                    Pocket(cloth, -0.085f, 0.05f, 0.085f, 0.095f, body, false);
                    Pocket(cloth, 0.085f, 0.05f, 0.085f, 0.095f, body, false);
                    Pocket(cloth, -0.075f, 0.24f, 0.06f, 0.07f, body, false);
                    break;
                case HumanoidRig.Outfit.Vest:
                    Pocket(cloth, -0.07f, 0.17f, 0.06f, 0.07f, body, true);
                    Pocket(cloth, 0.07f, 0.17f, 0.06f, 0.07f, body, true);
                    Pocket(cloth, -0.085f, 0.03f, 0.075f, 0.075f, body, true);
                    Pocket(cloth, 0.085f, 0.03f, 0.075f, 0.075f, body, true);
                    break;
                case HumanoidRig.Outfit.DressShirt:
                    Pocket(cloth, -0.07f, 0.24f, 0.055f, 0.06f, Sculpt.Shade(_jacket, 0.97f), false);
                    break;
                case HumanoidRig.Outfit.Hoodie:
                    Pocket(cloth, 0f, 0.0f, 0.2f, 0.1f, Sculpt.Shade(_jacket, 0.93f), false);
                    break;
            }
        }

        private void Tie(MeshBuilder cloth)
        {
            Color tie = Sculpt.Lin(0.33f, 0.08f, 0.08f);
            var path = new List<Vector3>();
            float[] ys = { 0.425f, 0.38f, 0.31f, 0.24f, 0.17f };
            for (int i = 0; i < ys.Length; i++) path.Add(OnTorsoFront(0.012f + i * 0.007f, ys[i], 0.006f + i * 0.002f));
            Sculpt.Tube(cloth, path, new[] { 0.009f, 0.012f, 0.018f, 0.024f, 0.026f }, Same(5, 0.003f), Vector3.forward, 6, _ => tie);
            Sculpt.Ball(cloth, OnTorsoFront(0.01f, 0.432f, 0.008f), new Vector3(0.013f, 0.011f, 0.008f), Sculpt.Shade(tie, 0.85f));
        }

        private void Hood(MeshBuilder cloth)
        {
            var path = new List<Vector3> { new Vector3(0.07f, 0.395f, 0.07f), new Vector3(0.1f, 0.425f, 0.01f) };
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(-20f, -160f, i / 8f) * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(a) * 0.1f, 0.44f, -0.03f + Mathf.Sin(a) * 0.085f));
            }
            path.Add(new Vector3(-0.1f, 0.425f, 0.01f));
            path.Add(new Vector3(-0.07f, 0.395f, 0.07f));
            int n = path.Count;
            var rx = new float[n];
            var ry = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Sin(i / (float)(n - 1) * Mathf.PI);
                rx[i] = 0.012f + 0.022f * t;
                ry[i] = 0.016f + 0.03f * t;
            }
            Color hood = Sculpt.Shade(_jacket, 0.92f);
            Sculpt.Tube(cloth, path, rx, ry, Vector3.up, 10, q => Sculpt.WithAo(hood, q.y < 0.43f ? 0.8f : 1f));
            Color cord = Sculpt.Lin(0.75f, 0.73f, 0.68f);
            foreach (int sx in new[] { -1, 1 })
            {
                var c = new[] { OnTorsoFront(sx * 0.035f, 0.415f, 0.008f), OnTorsoFront(sx * 0.04f, 0.35f, 0.006f), OnTorsoFront(sx * 0.042f, 0.29f, 0.006f) };
                Sculpt.Tube(cloth, c, Same(3, 0.003f), Same(3, 0.003f), Vector3.forward, 4, _ => cord);
            }
        }

        // ---------------------------------------------------------------- pelvis and legs

        private void Pelvis()
        {
            float g = _girth, hw = _p.Female ? 1.06f : 1f;
            _pelvis = new[]
            {
                new Ring(-0.15f, 0.04f * g, 0.03f, 0.035f, 2f, 0.5f),
                new Ring(-0.13f, 0.085f * hw * g, 0.066f * g, 0.07f * g, 2.6f, 0.7f),
                new Ring(-0.09f, 0.146f * hw * g, 0.086f * g, 0.084f * g, 2.6f),
                new Ring(-0.035f, 0.165f * hw * g, 0.094f * g, 0.092f * g, 2.6f),
                new Ring(0.03f, 0.156f * g, 0.094f * g, 0.097f * g, 2.6f),
                new Ring(0.09f, 0.148f * g, 0.092f * g, 0.094f * g, 2.6f),
                new Ring(0.11f, 0.1f * g, 0.06f, 0.06f, 2.2f)
            };
            MeshBuilder cloth = B("Pelvis", CharacterSurface.Cloth);
            Sculpt.Loft(cloth, _pelvis, 24, q => q.z > 0.04f && Mathf.Abs(q.x) < 0.006f && q.y < 0.0f ? Sculpt.Shade(_pants, 0.75f) : _pants);
            // Back pockets.
            foreach (int sx in new[] { -1, 1 })
            {
                Vector3 p = Sculpt.Surf(_pelvis, -0.035f, 270f + sx * 34f, 0.001f, out Vector3 n);
                cloth.Color = Sculpt.Shade(_pants, 0.88f);
                Sculpt.Box(cloth, p, new Vector3(0.064f, 0.07f, 0.005f), Quaternion.LookRotation(n, Vector3.up), 0.002f);
            }

            if (Is(HumanoidRig.Outfit.Hoodie)) return;
            MeshBuilder leather = B("Pelvis", CharacterSurface.Leather);
            Color belt = _inf ? Sculpt.Lin(0.13f, 0.1f, 0.08f) : Sculpt.Lin(0.27f, 0.18f, 0.11f);
            var beltRings = new[]
            {
                new Ring(0.022f, 0.163f * g, 0.1f * g, 0.104f * g, 2.6f, 0.8f),
                new Ring(0.066f, 0.157f * g, 0.098f * g, 0.1f * g, 2.6f, 0.9f)
            };
            Sculpt.Loft(leather, beltRings, 24, _ => belt, true, true);
            Vector3 buckle = Sculpt.Surf(beltRings, 0.044f, 90f, 0.005f);
            leather.Color = _metal;
            Sculpt.Box(leather, buckle, new Vector3(0.052f, 0.04f, 0.008f), null, 0.004f);
            leather.Color = Sculpt.Shade(belt, 0.7f);
            Sculpt.Box(leather, buckle + new Vector3(0f, 0f, 0.002f), new Vector3(0.032f, 0.022f, 0.008f), null, 0.002f);
            // Belt loops in the trouser colour.
            foreach (float a in new[] { 60f, 120f, 10f, 170f, 230f, 310f })
            {
                Vector3 p = Sculpt.Surf(beltRings, 0.044f, a, 0.002f, out Vector3 n);
                cloth.Color = Sculpt.Shade(_pants, 0.9f);
                Sculpt.Box(cloth, p, new Vector3(0.012f, 0.05f, 0.006f), Quaternion.LookRotation(n, Vector3.up), 0.002f);
            }
            if (_p.Backpack)
            {
                // A leather pouch on the right hip and a sheathed knife on the left.
                Vector3 p = Sculpt.Surf(_pelvis, -0.0f, -20f, 0.026f, out Vector3 n);
                Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
                leather.Color = Sculpt.Shade(_accent, 0.95f);
                Sculpt.Box(leather, p + Vector3.down * 0.02f, new Vector3(0.1f, 0.095f, 0.05f), rot, 0.014f);
                leather.Color = Sculpt.Shade(_accent, 0.78f);
                Sculpt.Box(leather, p + Vector3.up * 0.022f + n * 0.004f, new Vector3(0.104f, 0.035f, 0.054f), rot, 0.008f);
                Vector3 k = Sculpt.Surf(_pelvis, -0.06f, 200f, 0.012f, out Vector3 kn);
                Quaternion krot = Quaternion.LookRotation(kn, Vector3.up) * Quaternion.Euler(0f, 0f, -12f);
                leather.Color = Sculpt.Shade(_accent, 0.6f);
                Sculpt.Box(leather, k, new Vector3(0.035f, 0.16f, 0.014f), krot, 0.006f);
                leather.Color = Sculpt.Lin(0.12f, 0.1f, 0.09f);
                Sculpt.Box(leather, k + krot * new Vector3(0f, 0.11f, 0f), new Vector3(0.024f, 0.07f, 0.022f), krot, 0.007f);
            }
        }

        private void Thigh(int sx)
        {
            float g = Mathf.Sqrt(_girth) * 1.12f;
            var rings = new List<Ring>();
            float[,] t =
            {
                { 0.09f, 0.05f, 0.05f, 0.05f, 0.6f },
                { 0.04f, 0.074f, 0.074f, 0.08f, 0.8f },
                { 0.0f, 0.08f, 0.082f, 0.088f, 0.9f },
                { -0.09f, 0.078f, 0.082f, 0.084f, 1f },
                { -0.21f, 0.069f, 0.071f, 0.069f, 1f },
                { -0.33f, 0.06f, 0.062f, 0.059f, 1f },
                { -0.415f, 0.058f, 0.06f, 0.058f, 1f },
                { -0.455f, 0.055f, 0.058f, 0.055f, 1f },
                { -0.476f, 0.04f, 0.042f, 0.04f, 1f },
                { -0.486f, 0.016f, 0.016f, 0.016f, 1f }
            };
            for (int i = 0; i < t.GetLength(0); i++)
            {
                float y = t[i, 0];
                float inward = Mathf.Clamp01(-y / 0.45f);
                rings.Add(new Ring(y, t[i, 1] * g, t[i, 2] * g, t[i, 3] * g, 2.2f, t[i, 4]).At(sx * (0.004f - 0.014f * inward), 0f));
            }
            string bone = "Thigh" + Side(sx);
            MeshBuilder cloth = B(bone, CharacterSurface.Cloth);
            Sculpt.Loft(cloth, rings, 18, _ => _pants);
            if (_p.Legwear == HumanoidRig.Legwear.Cargo)
            {
                // Cargo pocket on the outer thigh and a reinforced knee.
                float outer = sx > 0 ? 0f : 180f;
                Vector3 p = Sculpt.Surf(rings, -0.22f, outer + sx * 12f, 0.001f, out Vector3 n);
                Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
                cloth.Color = Sculpt.Shade(_pants, 0.97f);
                Sculpt.Box(cloth, p, new Vector3(0.1f, 0.11f, 0.014f), rot, 0.006f);
                cloth.Color = Sculpt.Shade(_pants, 0.88f);
                Sculpt.Box(cloth, Sculpt.Surf(rings, -0.168f, outer + sx * 12f, 0.007f), new Vector3(0.104f, 0.032f, 0.012f), rot, 0.004f);
                Vector3 kp = Sculpt.Surf(rings, -0.41f, 90f, -0.004f, out Vector3 kn);
                Sculpt.Ellipsoid(cloth, kp, new Vector3(0.046f, 0.052f, 0.01f), 1, null, (q, d) => Sculpt.Shade(_pants, 0.9f), true, Quaternion.LookRotation(kn, Vector3.up));
            }
            else if (_p.Legwear == HumanoidRig.Legwear.Work)
            {
                // Hammer loop and side pocket seam.
                float outer = sx > 0 ? 0f : 180f;
                if (sx < 0)
                {
                    Vector3 p = Sculpt.Surf(rings, -0.12f, outer, 0.004f, out Vector3 n);
                    cloth.Color = Sculpt.Shade(_pants, 0.85f);
                    Sculpt.Box(cloth, p, new Vector3(0.02f, 0.09f, 0.008f), Quaternion.LookRotation(n, Vector3.up), 0.003f);
                }
                Vector3 kp = Sculpt.Surf(rings, -0.4f, 90f, -0.004f, out Vector3 kn);
                Sculpt.Ellipsoid(cloth, kp, new Vector3(0.048f, 0.058f, 0.008f), 1, null, (q, d) => Sculpt.Shade(_pants, 0.94f), true, Quaternion.LookRotation(kn, Vector3.up));
            }
        }

        private bool Boots => _p.Footwear == HumanoidRig.Footwear.Boots;

        private void Shin(int sx)
        {
            float g = Mathf.Sqrt(_girth) * 1.1f;
            string bone = "Shin" + Side(sx);
            MeshBuilder cloth = B(bone, CharacterSurface.Cloth);
            var rings = new List<Ring>
            {
                new Ring(0.058f, 0.02f * g, 0.02f * g, 0.02f * g),
                new Ring(0.035f, 0.044f * g, 0.046f * g, 0.046f * g),
                new Ring(0f, 0.052f * g, 0.054f * g, 0.057f * g),
                new Ring(-0.09f, 0.055f * g, 0.055f * g, 0.064f * g),
                new Ring(-0.19f, 0.051f * g, 0.051f * g, 0.057f * g),
                new Ring(-0.28f, 0.048f * g, 0.049f * g, 0.05f * g)
            };
            bool tucked = Boots && _p.Legwear == HumanoidRig.Legwear.Cargo;
            if (tucked)
            {
                rings.Add(new Ring(-0.31f, 0.044f * g, 0.045f * g, 0.046f * g, 2.2f, 0.6f));
            }
            else
            {
                float jag = _inf ? 0.02f : 0f;
                rings.Add(new Ring(-0.34f, 0.056f * g, 0.058f * g, 0.058f * g));
                rings.Add(new Ring(-0.375f, 0.064f * g, 0.068f * g, 0.066f * g, 2.2f, 0.9f).Ragged(jag * 2f));
                rings.Add(new Ring(-0.377f, 0.045f, 0.045f, 0.045f, 2.2f, 0.4f).Ragged(jag * 2f));
            }
            for (int i = 0; i < rings.Count; i++) rings[i] = rings[i].At(sx * -0.004f, 0f);
            bool jeans = _p.Legwear == HumanoidRig.Legwear.Jeans;
            Sculpt.Loft(cloth, rings, 18, q => jeans && q.y < -0.35f ? Sculpt.Shade(_pants, 1.15f) : _pants, false, false, _seed + sx);

            if (!Boots) return;
            MeshBuilder leather = B(bone, CharacterSurface.Leather);
            var shaft = new[]
            {
                new Ring(-0.43f, 0.05f, 0.064f, 0.058f, 2.3f, 0.75f),
                new Ring(-0.385f, 0.051f, 0.058f, 0.058f, 2.3f),
                new Ring(-0.32f, 0.054f, 0.057f, 0.059f, 2.3f),
                new Ring(-0.275f, 0.058f, 0.061f, 0.063f, 2.3f),
                new Ring(-0.262f, 0.06f, 0.063f, 0.065f, 2.3f),
                new Ring(-0.256f, 0.05f, 0.052f, 0.055f, 2.3f, 0.5f)
            };
            if (!tucked)
            {
                // Under straight trousers only the lower boot shows.
                for (int i = 0; i < shaft.Length; i++) shaft[i].Y = Mathf.Lerp(-0.43f, -0.34f, (shaft[i].Y + 0.43f) / 0.174f);
            }
            for (int i = 0; i < shaft.Length; i++) shaft[i] = shaft[i].At(sx * -0.004f, 0f);
            Color collar = Sculpt.Shade(_boots, 0.7f);
            float top = shaft[shaft.Length - 1].Y;
            Sculpt.Loft(leather, shaft, 18, q => q.y > top - 0.016f ? collar : _boots, false, true);
            Color lace = Sculpt.Lin(0.42f, 0.33f, 0.22f);
            for (float y = -0.405f; y < top - 0.02f; y += 0.022f)
            {
                Vector3 p = Sculpt.Surf(shaft, y, 90f, 0.002f);
                leather.Color = lace;
                Sculpt.Box(leather, p, new Vector3(0.034f, 0.005f, 0.006f), Quaternion.Euler(0f, 0f, ((int)(y * 100f) & 1) == 0 ? 12f : -12f), 0.001f);
            }
        }

        private void Foot(int sx)
        {
            string bone = "Foot" + Side(sx);
            MeshBuilder leather = B(bone, CharacterSurface.Leather);
            bool boots = Boots;
            float[,] t = boots
                ? new float[,]
                {
                    { -0.086f, 0.026f, 0.0f }, { -0.076f, 0.04f, 0.03f }, { -0.045f, 0.047f, 0.045f }, { 0.0f, 0.05f, 0.04f }, { 0.045f, 0.052f, 0.012f },
                    { 0.095f, 0.054f, -0.016f }, { 0.145f, 0.051f, -0.026f }, { 0.18f, 0.043f, -0.032f }, { 0.2f, 0.03f, -0.04f }, { 0.212f, 0.012f, -0.048f }
                }
                : new float[,]
                {
                    { -0.082f, 0.024f, -0.01f }, { -0.072f, 0.038f, 0.012f }, { -0.04f, 0.043f, 0.016f }, { 0.0f, 0.045f, 0.012f }, { 0.045f, 0.047f, 0.0f },
                    { 0.095f, 0.049f, -0.022f }, { 0.145f, 0.047f, -0.03f }, { 0.18f, 0.04f, -0.036f }, { 0.2f, 0.028f, -0.042f }, { 0.21f, 0.01f, -0.048f }
                };
            var rings = new List<Ring>();
            for (int i = 0; i < t.GetLength(0); i++)
            {
                float top = t[i, 2], bottom = -0.06f;
                float half = (top - bottom) * 0.5f;
                // Loft axis runs along +Z (toe); the loft's +Z (front depth) maps to −Y.
                rings.Add(new Ring(t[i, 0], t[i, 1], half, half, 2.6f).At(sx * 0.006f * Mathf.Clamp01(t[i, 0] / 0.2f), -(top + bottom) * 0.5f));
            }
            Matrix4x4 saved = leather.Matrix;
            leather.Matrix = saved * Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f));
            Color toe = Sculpt.Shade(_boots, 0.82f);
            Sculpt.Loft(leather, rings, 16, q => q.z > 0.13f || q.z < -0.06f ? toe : _boots, true, true);
            leather.Matrix = saved;

            var sole = new List<Vector2>
            {
                new Vector2(0f, -0.093f), new Vector2(0.036f, -0.083f), new Vector2(0.048f, -0.05f), new Vector2(0.053f, 0f), new Vector2(0.059f, 0.06f),
                new Vector2(0.06f, 0.12f), new Vector2(0.053f, 0.17f), new Vector2(0.037f, 0.206f), new Vector2(0.012f, 0.223f), new Vector2(-0.012f, 0.223f),
                new Vector2(-0.034f, 0.206f), new Vector2(-0.048f, 0.17f), new Vector2(-0.054f, 0.12f), new Vector2(-0.053f, 0.06f), new Vector2(-0.047f, 0f),
                new Vector2(-0.043f, -0.05f), new Vector2(-0.033f, -0.083f)
            };
            if (sx < 0)
            {
                // Mirror and keep the winding order.
                sole.Reverse();
                for (int i = 0; i < sole.Count; i++) sole[i] = new Vector2(-sole[i].x, sole[i].y);
            }
            bool sneaker = !boots && _inf && _p.Outfit == HumanoidRig.Outfit.Hoodie;
            Color soleCol = sneaker ? Sculpt.Lin(0.62f, 0.6f, 0.55f) : Sculpt.Lin(0.08f, 0.075f, 0.07f);
            Sculpt.Slab(leather, sole, -0.08f, boots ? -0.056f : -0.062f, Sculpt.WithAo(soleCol, 0.8f));
            var welt = new List<Vector2>();
            foreach (Vector2 v in sole) welt.Add(new Vector2(v.x * 0.97f, v.y * 0.985f + 0.001f));
            Sculpt.Slab(leather, welt, boots ? -0.058f : -0.064f, boots ? -0.05f : -0.058f, sneaker ? soleCol : Sculpt.Shade(_boots, 0.55f));
            if (boots)
            {
                Color lace = Sculpt.Lin(0.42f, 0.33f, 0.22f);
                leather.Color = lace;
                Sculpt.Box(leather, new Vector3(0f, 0.034f, 0.02f), new Vector3(0.036f, 0.005f, 0.006f), Quaternion.Euler(-35f, 0f, 10f), 0.001f);
                Sculpt.Box(leather, new Vector3(0f, 0.018f, 0.045f), new Vector3(0.034f, 0.005f, 0.006f), Quaternion.Euler(-30f, 0f, -10f), 0.001f);
            }
        }

        // ---------------------------------------------------------------- arms and hands

        /// <summary>Where the sleeve ends on the forearm (forearm space; above 0 = bare forearm).</summary>
        private float SleeveEnd(int sx)
        {
            if (_inf)
            {
                switch (_p.Variant % 3)
                {
                    case 0: return sx > 0 ? 0.02f : -0.236f;
                    case 1: return sx < 0 ? -0.1f : -0.236f;
                    default: return -0.236f;
                }
            }
            if (Is(HumanoidRig.Outfit.Vest)) return -0.11f;
            return -0.236f;
        }

        private void UpperArm(int sx)
        {
            string bone = "UpperArm" + Side(sx);
            float g = _armGirth;
            var rings = new List<Ring>
            {
                new Ring(0.026f, 0.016f * g, 0.016f * g, 0.016f * g, 2f),
                new Ring(0.014f, 0.038f * g, 0.038f * g, 0.038f * g, 2f, 0.9f),
                new Ring(-0.014f, 0.054f * g, 0.056f * g, 0.054f * g, 2.1f),
                new Ring(-0.06f, 0.056f * g, 0.057f * g, 0.055f * g, 2.1f),
                new Ring(-0.14f, 0.051f * g, 0.053f * g, 0.051f * g, 2.1f, 0.9f),
                new Ring(-0.22f, 0.047f * g, 0.048f * g, 0.047f * g, 2.1f),
                new Ring(-0.27f, 0.046f * g, 0.046f * g, 0.046f * g, 2.1f),
                new Ring(-0.295f, 0.036f * g, 0.036f * g, 0.036f * g, 2f),
                new Ring(-0.31f, 0.014f, 0.014f, 0.014f, 2f)
            };
            MeshBuilder cloth = B(bone, CharacterSurface.Cloth);
            Sculpt.Loft(cloth, rings, 16, _ => _sleeve);
        }

        private void Forearm(int sx)
        {
            string bone = "Forearm" + Side(sx);
            float g = _armGirth;
            float end = SleeveEnd(sx);
            MeshBuilder cloth = B(bone, CharacterSurface.Cloth);
            MeshBuilder skin = B(bone, CharacterSurface.Skin);
            bool rolled = end > -0.2f;
            if (end < 0.0f)
            {
                var rings = new List<Ring>
                {
                    new Ring(0.052f, 0.018f * g, 0.018f * g, 0.018f * g, 2f),
                    new Ring(0.035f, 0.042f * g, 0.042f * g, 0.042f * g, 2f),
                    new Ring(0.0f, 0.047f * g, 0.047f * g, 0.05f * g, 2.1f),
                    new Ring(-0.07f, 0.046f * g, 0.048f * g, 0.046f * g, 2.1f)
                };
                if (rolled)
                {
                    rings.Add(new Ring(end + 0.02f, 0.046f * g, 0.047f * g, 0.046f * g, 2.1f));
                    rings.Add(new Ring(end, 0.05f * g, 0.05f * g, 0.05f * g, 2.1f).Ragged(_inf ? 0.04f : 0f));
                    rings.Add(new Ring(end - 0.002f, 0.032f * g, 0.032f * g, 0.032f * g, 2f, 0.5f).Ragged(_inf ? 0.04f : 0f));
                }
                else
                {
                    rings.Add(new Ring(-0.165f, 0.041f * g, 0.04f * g, 0.04f * g, 2.1f));
                    rings.Add(new Ring(-0.205f, 0.042f * g, 0.042f * g, 0.042f * g, 2.1f));
                    rings.Add(new Ring(-0.232f, 0.044f * g, 0.044f * g, 0.044f * g, 2.1f).Ragged(_inf ? 0.035f : 0f));
                    rings.Add(new Ring(-0.236f, 0.03f * g, 0.03f * g, 0.03f * g, 2f, 0.5f).Ragged(_inf ? 0.035f : 0f));
                }
                Color cuff = Sculpt.Shade(_sleeve, 0.82f);
                Sculpt.Loft(cloth, rings, 16, q => q.y < -0.203f || (rolled && q.y < end + 0.025f) ? cuff : _sleeve, false, false, _seed + 5 * sx);
            }
            else
            {
                // Sleeve rolled above the elbow: a thick rolled cuff on the upper arm's end.
                Sculpt.Loop(cloth, new Vector3(0f, 0.012f, 0f), Vector3.right, Vector3.forward, 0.05f * g, 0.05f * g, 0.012f, 0.014f, 14, Sculpt.Shade(_sleeve, 0.85f));
            }

            // Bare forearm where the sleeve does not reach.
            if (end > -0.2f)
            {
                var arm = new[]
                {
                    new Ring(0.05f, 0.016f * g, 0.016f * g, 0.016f * g, 2f),
                    new Ring(0.03f, 0.038f * g, 0.038f * g, 0.038f * g, 2f),
                    new Ring(-0.02f, 0.041f * g, 0.043f * g, 0.042f * g, 2f),
                    new Ring(-0.1f, 0.037f * g, 0.039f * g, 0.037f * g, 2f),
                    new Ring(-0.19f, 0.027f * g, 0.032f * g, 0.031f * g, 2f),
                    new Ring(-0.235f, 0.023f, 0.03f, 0.029f, 2f)
                };
                Sculpt.Loft(skin, arm, 14, _ => _skin);
            }

            Hand(skin, sx);
        }

        private void Hand(MeshBuilder skin, int sx)
        {
            float k = _inf ? 0.92f : (_p.Female ? 0.92f : 1f);
            var wrist = new[]
            {
                new Ring(-0.2f, 0.024f * k, 0.03f * k, 0.03f * k, 2f),
                new Ring(-0.25f, 0.021f * k, 0.031f * k, 0.031f * k, 2.2f),
                new Ring(-0.27f, 0.017f * k, 0.04f * k, 0.036f * k, 2.6f),
                new Ring(-0.31f, 0.016f * k, 0.04f * k, 0.038f * k, 2.6f),
                new Ring(-0.335f, 0.015f * k, 0.037f * k, 0.035f * k, 2.6f),
                new Ring(-0.346f, 0.009f * k, 0.028f * k, 0.028f * k, 2.4f)
            };
            for (int i = 0; i < wrist.Length; i++) wrist[i] = wrist[i].At(sx * -0.004f, 0f);
            Color knuckles = Sculpt.Mul(_skin, 0.95f, 0.88f, 0.86f);
            Sculpt.Loft(skin, wrist, 12, q => q.y < -0.32f ? knuckles : _skin);
            float[] zs = { 0.026f, 0.009f, -0.008f, -0.025f };
            float[] rs = { 0.0088f, 0.0092f, 0.0086f, 0.0076f };
            float[] len = { 0.97f, 1.05f, 1f, 0.85f };
            for (int f = 0; f < 4; f++)
            {
                float z = zs[f] * k, r = rs[f] * k, l = len[f] * k;
                Vector3[] path = _inf
                    ? new[] { new Vector3(-0.002f, -0.33f, z), new Vector3(0.004f, -0.33f - 0.042f * l, z), new Vector3(-0.006f, -0.33f - 0.078f * l, z), new Vector3(-0.022f, -0.33f - 0.092f * l, z) }
                    : new[] { new Vector3(-0.002f, -0.33f, z), new Vector3(-0.005f, -0.33f - 0.042f * l, z), new Vector3(-0.03f, -0.33f - 0.062f * l, z), new Vector3(-0.05f, -0.33f - 0.04f * l, z) };
                for (int i = 0; i < path.Length; i++) path[i].x *= sx;
                Sculpt.Tube(skin, path, new[] { r, r * 0.95f, r * 0.85f, r * 0.7f }, new[] { r, r * 0.95f, r * 0.85f, r * 0.7f }, Vector3.forward, 6, _ => knuckles);
            }
            var thumb = new[] { new Vector3(-0.01f, -0.272f, 0.034f), new Vector3(-0.022f, -0.302f, 0.046f), new Vector3(-0.036f, -0.326f, 0.04f), new Vector3(-0.046f, -0.342f, 0.026f) };
            for (int i = 0; i < thumb.Length; i++) thumb[i] = new Vector3(thumb[i].x * sx, thumb[i].y, thumb[i].z * k);
            Sculpt.Tube(skin, thumb, new[] { 0.012f * k, 0.011f * k, 0.0095f * k, 0.008f * k }, new[] { 0.012f * k, 0.011f * k, 0.0095f * k, 0.008f * k }, Vector3.right, 6, _ => _skin);
        }

        // ---------------------------------------------------------------- head

        private Vector3 SkullOffset(Vector3 d)
        {
            float m = 1f;
            float lower = Sculpt.Smooth01((-d.y - 0.05f) / 0.8f);
            m -= (_inf ? 0.17f : 0.13f) * lower * Mathf.Abs(d.x);
            m -= 0.14f * lower * Mathf.Max(0f, -d.z);
            m -= 0.05f * Mathf.Max(0f, d.z - 0.55f);
            m += 0.03f * Sculpt.Smooth01((-d.y - 0.55f) / 0.35f) * Mathf.Max(0f, d.z);
            m += 0.03f * Sculpt.Gauss(d, new Vector3(0f, 0.32f, 0.95f), 0.3f);
            m -= (_inf ? 0.075f : 0.04f) * Sockets(d);
            m += 0.025f * (Sculpt.Gauss(d, new Vector3(0.62f, -0.1f, 0.78f), 0.25f) + Sculpt.Gauss(d, new Vector3(-0.62f, -0.1f, 0.78f), 0.25f));
            if (_inf) m -= 0.06f * (Sculpt.Gauss(d, new Vector3(0.6f, -0.42f, 0.68f), 0.22f) + Sculpt.Gauss(d, new Vector3(-0.6f, -0.42f, 0.68f), 0.22f));
            return Vector3.Scale(d, _skullR) * m;
        }

        private static float Sockets(Vector3 d) =>
            Sculpt.Gauss(d, new Vector3(0.36f, 0.06f, 0.93f), 0.17f) + Sculpt.Gauss(d, new Vector3(-0.36f, 0.06f, 0.93f), 0.17f);

        /// <summary>The point on the face at head-space (x, y).</summary>
        private Vector3 FacePoint(float x, float y)
        {
            var d = new Vector3((x - _skullC.x) / _skullR.x, (y - _skullC.y) / _skullR.y, 0f);
            d.z = Mathf.Sqrt(Mathf.Max(0.02f, 1f - d.x * d.x - d.y * d.y));
            d.Normalize();
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = SkullOffset(d);
                var want = new Vector3(x - _skullC.x, y - _skullC.y, p.z);
                d = new Vector3(want.x / _skullR.x, want.y / _skullR.y, want.z / _skullR.z).normalized;
            }
            return _skullC + SkullOffset(d);
        }

        private void Head()
        {
            MeshBuilder skin = B("Head", CharacterSurface.Skin);
            float ng = _inf ? 0.95f : (_p.Female ? 1f : 1.12f);
            Sculpt.Loft(skin, new[]
            {
                new Ring(-0.06f, 0.052f * ng, 0.05f * ng, 0.054f * ng, 2f, 0.55f),
                new Ring(0.04f, 0.05f * ng, 0.047f * ng, 0.052f * ng, 2f, 0.8f).At(0f, 0.004f),
                new Ring(0.1f, 0.046f * ng, 0.044f * ng, 0.05f * ng, 2f).At(0f, 0.012f)
            }, 14, _ => _skin);
            if (_inf)
            {
                // A bite wound on the side of the neck.
                Sculpt.Ellipsoid(skin, new Vector3(-0.04f, 0.035f, 0.02f), new Vector3(0.014f, 0.02f, 0.012f), 1, null, (q, d) => Sculpt.Lin(0.25f, 0.04f, 0.04f), true);
            }

            bool male = !_p.Female;
            Sculpt.Ellipsoid(skin, _skullC, _skullR, 3, SkullOffset, (q, d) => FaceColour(d, male), true);
            Nose(skin);
            Ears(skin);
            Mouth(skin);
            Eyes(skin);
            Brows();
            HairCut();
            if (_p.Beard) Beard();
            Headwear();
        }

        private Color FaceColour(Vector3 d, bool male)
        {
            Color c = _skin;
            float ao = 1f;
            float lip = Sculpt.Gauss(d, new Vector3(0f, -0.54f, 0.84f), 0.13f);
            c = Sculpt.Mix(c, Sculpt.Mul(_skin, 0.78f, 0.55f, 0.52f), lip * 0.5f);
            float sockets = Sockets(d);
            ao -= 0.28f * sockets;
            ao *= Mathf.Lerp(0.62f, 1f, Sculpt.Smooth01((d.y + 0.95f) / 0.35f));
            if (_inf)
            {
                c = Sculpt.Mix(c, Sculpt.Lin(0.27f, 0.2f, 0.25f), Mathf.Clamp01(sockets * 0.9f));
                float veins = Sculpt.Step(0.62f, 0.7f, Sculpt.Noise3(d * 9f, _seed + 21)) * Sculpt.Smooth01(Mathf.Abs(d.x) * 1.5f - 0.5f);
                c = Sculpt.Mix(c, Sculpt.Lin(0.22f, 0.26f, 0.24f), veins * 0.6f);
                float blood = Sculpt.Smooth01((Sculpt.Gauss(d, new Vector3(0f, -0.66f, 0.75f), 0.35f) - 0.25f) / 0.4f) * (0.5f + 0.5f * Sculpt.Noise3(d * 8f, _seed));
                c = Sculpt.Mix(c, _blood, blood * 0.9f);
                return Sculpt.WithAo(c, ao);
            }
            c = Sculpt.Mix(c, Sculpt.Mul(_skin, 1.02f, 0.84f, 0.8f), 0.3f * (Sculpt.Gauss(d, new Vector3(0.6f, -0.18f, 0.78f), 0.26f) + Sculpt.Gauss(d, new Vector3(-0.6f, -0.18f, 0.78f), 0.26f)));
            if (male && !_p.Beard)
            {
                float stubble = Sculpt.Smooth01((-d.y - 0.28f) / 0.2f) * Sculpt.Smooth01((d.z + 0.1f) / 0.3f) * (1f - lip);
                c = Sculpt.Mix(c, Sculpt.Shade(_hair, 0.9f), stubble * 0.28f);
            }
            return Sculpt.WithAo(c, ao);
        }

        private void Nose(MeshBuilder skin)
        {
            Vector3 bridge = FacePoint(0f, 0.178f) + new Vector3(0f, 0f, -0.004f);
            Vector3 tip = FacePoint(0f, 0.132f) + new Vector3(0f, 0f, _inf ? 0.017f : 0.021f);
            Vector3 baseC = FacePoint(0f, 0.12f) + new Vector3(0f, 0f, 0.007f);
            Vector3 wl = FacePoint(-0.017f, 0.124f) + new Vector3(0f, 0f, -0.001f);
            Vector3 wr = FacePoint(0.017f, 0.124f) + new Vector3(0f, 0f, -0.001f);
            Vector3 inside = _skullC + new Vector3(0f, -0.02f, 0.02f);
            skin.Color = Sculpt.WithAo(_inf ? Sculpt.Shade(_skin, 0.95f) : Sculpt.Mul(_skin, 1f, 0.95f, 0.93f), 1f);
            Sculpt.Face(skin, inside, bridge, tip, wr);
            Sculpt.Face(skin, inside, bridge, wl, tip);
            skin.Color = Sculpt.WithAo(skin.Color, 0.75f);
            Sculpt.Face(skin, inside, tip, wl, baseC);
            Sculpt.Face(skin, inside, tip, baseC, wr);
            Sculpt.Ball(skin, tip + new Vector3(0f, -0.003f, -0.006f), new Vector3(0.011f, 0.009f, 0.009f), Sculpt.Mul(_skin, 1f, 0.94f, 0.92f));
        }

        private void Ears(MeshBuilder skin)
        {
            foreach (int sx in new[] { -1, 1 })
            {
                var c = new Vector3(sx * 0.084f * _skullR.x / 0.087f, 0.151f, -0.004f);
                Sculpt.Ellipsoid(skin, c, new Vector3(0.01f, 0.028f, 0.019f), 2, null,
                    (q, d) => Sculpt.WithAo(Sculpt.Mul(_skin, 1f, 0.93f, 0.9f), 1f - 0.4f * Sculpt.Gauss(d, new Vector3(sx, 0f, 0.2f), 0.6f)), true,
                    Quaternion.Euler(-8f, sx * -18f, 0f));
            }
        }

        private void Mouth(MeshBuilder skin)
        {
            if (_inf)
            {
                Vector3 m = FacePoint(0f, 0.093f);
                Sculpt.Ball(skin, m + new Vector3(0f, 0f, -0.004f), new Vector3(0.019f, 0.011f, 0.008f), Sculpt.Lin(0.08f, 0.02f, 0.02f));
                skin.Color = Sculpt.Lin(0.6f, 0.56f, 0.42f);
                Sculpt.Box(skin, FacePoint(0f, 0.0995f) + new Vector3(0f, 0f, -0.0015f), new Vector3(0.026f, 0.005f, 0.006f), null, 0.0015f);
                Sculpt.Box(skin, FacePoint(0f, 0.0845f) + new Vector3(0f, 0f, -0.0025f), new Vector3(0.022f, 0.004f, 0.006f), null, 0.0015f);
                return;
            }
            skin.Color = Sculpt.WithAo(Sculpt.Mul(_skin, 0.45f, 0.28f, 0.27f), 0.8f);
            Sculpt.Box(skin, FacePoint(0f, 0.097f) + new Vector3(0f, 0f, -0.0018f), new Vector3(0.034f, 0.0035f, 0.006f), null, 0.0012f);
            Sculpt.Ball(skin, FacePoint(0f, 0.0905f) + new Vector3(0f, 0f, -0.002f), new Vector3(0.016f, 0.005f, 0.006f), Sculpt.Mul(_skin, 0.88f, 0.66f, 0.64f));
        }

        private void Eyes(MeshBuilder skin)
        {
            MeshBuilder eyes = B("Head", _inf ? CharacterSurface.Glow : CharacterSurface.Gloss);
            float r = _inf ? 0.0135f : 0.0125f;
            Color sclera = Sculpt.Lin(0.8f, 0.78f, 0.74f), pupil = Sculpt.Lin(0.03f, 0.025f, 0.02f);
            foreach (int sx in new[] { -1, 1 })
            {
                Vector3 c = FacePoint(sx * 0.033f, 0.165f) + new Vector3(0f, 0f, _inf ? -0.007f : -0.009f);
                Sculpt.Ellipsoid(eyes, c, Vector3.one * r, 2, null, (q, d) => d.z > 0.93f ? pupil : d.z > 0.76f ? _iris : sclera, true);
                if (_inf) continue;
                // Upper and lower lids keep the eyes from staring.
                Sculpt.Ellipsoid(skin, c + new Vector3(0f, 0.0072f, 0.0015f), new Vector3(0.0158f, 0.0072f, 0.0132f), 1, null, (q, d) => Sculpt.WithAo(Sculpt.Shade(_skin, 0.92f), 0.85f), true);
                Sculpt.Ellipsoid(skin, c + new Vector3(0f, -0.0095f, 0.0005f), new Vector3(0.0145f, 0.005f, 0.0122f), 1, null, (q, d) => _skin, true);
            }
        }

        private void Brows()
        {
            MeshBuilder hair = B("Head", CharacterSurface.Hair);
            Color brow = Sculpt.Shade(_hair, 0.8f);
            foreach (int sx in new[] { -1, 1 })
            {
                float inner = _inf ? 0.181f : 0.187f;
                var path = new[]
                {
                    FacePoint(sx * 0.012f, inner) + new Vector3(0f, 0f, 0.002f),
                    FacePoint(sx * 0.032f, 0.191f) + new Vector3(0f, 0f, 0.002f),
                    FacePoint(sx * 0.052f, 0.185f) + new Vector3(0f, 0f, 0.0015f)
                };
                Sculpt.Tube(hair, path, new[] { 0.0055f, 0.0055f, 0.0035f }, new[] { 0.004f, 0.004f, 0.003f }, Vector3.forward, 6, _ => brow);
            }
        }

        private float HairMask(Vector3 d)
        {
            HumanoidRig.HairStyle s = _p.HairStyle;
            bool longHair = s == HumanoidRig.HairStyle.Bob || s == HumanoidRig.HairStyle.Long;
            float front = longHair ? 0.42f : 0.46f;
            float side = longHair ? -0.72f : 0.1f;
            float back = longHair ? -0.85f : -0.55f;
            float h = d.z >= 0f ? Mathf.Lerp(side, front, Mathf.Pow(d.z, 1.4f)) : Mathf.Lerp(side, back, Mathf.Pow(-d.z, 0.8f));
            if (s == HumanoidRig.HairStyle.Messy) h -= 0.08f * Mathf.Max(0f, d.z) * Sculpt.Noise3(d * 6f, _seed + 2);
            float mask = Sculpt.Step(h - 0.02f, h + 0.07f, d.y);
            if (longHair)
            {
                float face = Sculpt.Step(0.25f, 0.42f, d.z) * (1f - Sculpt.Step(0.3f, 0.42f, d.y));
                mask *= 1f - face;
            }
            else
            {
                mask *= 1f - Sculpt.Gauss(d, new Vector3(0.98f, -0.06f, -0.12f), 0.3f) - Sculpt.Gauss(d, new Vector3(-0.98f, -0.06f, -0.12f), 0.3f);
                if (!_p.Female)
                {
                    // Sideburns in front of the ears.
                    float burns = Sculpt.Step(0.82f, 0.9f, Mathf.Abs(d.x)) * Sculpt.Step(-0.05f, 0.08f, d.z) * (1f - Sculpt.Step(0.28f, 0.36f, d.z)) * Sculpt.Step(-0.32f, -0.22f, d.y);
                    mask = Mathf.Max(mask, burns);
                }
            }
            if (s == HumanoidRig.HairStyle.Balding) mask *= 1f - Sculpt.Step(0.48f, 0.7f, d.y) * Sculpt.Step(-0.45f, 0.1f, d.z);
            if (_inf && (s == HumanoidRig.HairStyle.Balding || s == HumanoidRig.HairStyle.Crop)) mask *= Sculpt.Step(0.2f, 0.3f, Sculpt.Noise3(d * 5f, _seed + 9) + 0.12f);
            return Mathf.Clamp01(mask);
        }

        private Vector3 HairShape(Vector3 d)
        {
            HumanoidRig.HairStyle s = _p.HairStyle;
            float mask = HairMask(d);
            float n = Sculpt.Noise3(d * 6f, _seed + 4) * 2f - 1f;
            float thick;
            switch (s)
            {
                case HumanoidRig.HairStyle.Messy:
                    float clumps = Sculpt.Noise3(d * 9f, _seed + 6);
                    thick = 0.075f + 0.045f * clumps * clumps + 0.025f * n + 0.03f * Mathf.Max(0f, d.y) + 0.03f * Mathf.Max(0f, d.z) * Mathf.Max(0f, d.y);
                    break;
                case HumanoidRig.HairStyle.Ponytail:
                    thick = 0.045f + 0.008f * n;
                    break;
                case HumanoidRig.HairStyle.Bob:
                    thick = 0.06f + 0.012f * n + 0.07f * Sculpt.Smooth01((0.1f - d.y) / 0.7f);
                    break;
                case HumanoidRig.HairStyle.Long:
                    thick = 0.05f + 0.04f * n + 0.05f * Sculpt.Smooth01((0.1f - d.y) / 0.7f);
                    break;
                case HumanoidRig.HairStyle.Balding:
                    thick = 0.035f + 0.01f * n;
                    break;
                case HumanoidRig.HairStyle.Buzz:
                    thick = 0.022f;
                    break;
                default:
                    thick = 0.06f + 0.015f * n + 0.02f * Mathf.Max(0f, d.y);
                    break;
            }
            Vector3 p = SkullOffset(d) * Mathf.Lerp(0.9f, 1f + thick, mask);
            if ((s == HumanoidRig.HairStyle.Bob || s == HumanoidRig.HairStyle.Long) && d.y < 0.15f)
            {
                // Long hair hangs instead of following the jaw inwards.
                float hang = Sculpt.Smooth01((0.15f - d.y) / 0.8f) * mask;
                p.x *= 1f + 0.3f * hang;
                if (p.z < 0f) p.z *= 1f + 0.2f * hang;
                if (s == HumanoidRig.HairStyle.Long) p.y -= 0.05f * hang * (1f + n);
            }
            return p;
        }

        private void HairCut()
        {
            MeshBuilder hair = B("Head", CharacterSurface.Hair);
            Color roots = Sculpt.Shade(_hair, 0.6f);
            Sculpt.Ellipsoid(hair, _skullC, _skullR, 3, HairShape, (q, d) =>
            {
                float m = HairMask(d);
                Color c = Sculpt.Mix(roots, _hair, Sculpt.Smooth01(m * 1.6f));
                c = Sculpt.Shade(c, 1f + 0.15f * Mathf.Max(0f, d.y));
                return Sculpt.WithAo(c, Mathf.Lerp(0.65f, 1f, m));
            }, true);
            if (_p.HairStyle == HumanoidRig.HairStyle.Ponytail)
            {
                Vector3 o = _skullC;
                var path = new[]
                {
                    o + new Vector3(0f, 0.04f, -0.098f), o + new Vector3(0f, 0.012f, -0.128f), o + new Vector3(0f, -0.045f, -0.14f),
                    o + new Vector3(0f, -0.1f, -0.135f), o + new Vector3(0f, -0.15f, -0.118f)
                };
                Sculpt.Tube(hair, path, new[] { 0.026f, 0.03f, 0.026f, 0.018f, 0.005f }, new[] { 0.022f, 0.026f, 0.022f, 0.015f, 0.005f }, Vector3.right, 10, _ => _hair);
                MeshBuilder cloth = B("Head", CharacterSurface.Cloth);
                Sculpt.Loop(cloth, o + new Vector3(0f, 0.02f, -0.122f), Vector3.right, new Vector3(0f, 0.73f, -0.68f), 0.024f, 0.022f, 0.004f, 0.006f, 12, _accent);
            }
        }

        private void Beard()
        {
            MeshBuilder hair = B("Head", CharacterSurface.Hair);
            float Mask(Vector3 d)
            {
                float lower = Sculpt.Smooth01((-d.y - 0.1f - 0.25f * Mathf.Max(0f, d.z - 0.55f)) / 0.12f);
                float front = Sculpt.Smooth01((d.z + 0.1f) / 0.2f);
                float lips = Sculpt.Step(0.3f, 0.2f, Mathf.Abs(d.x)) * Sculpt.Step(-0.66f, -0.6f, d.y) * (1f - Sculpt.Step(-0.48f, -0.44f, d.y));
                float stache = Sculpt.Step(0.34f, 0.26f, Mathf.Abs(d.x)) * Sculpt.Step(-0.48f, -0.44f, d.y) * (1f - Sculpt.Step(-0.38f, -0.33f, d.y)) * Sculpt.Step(0.6f, 0.7f, d.z);
                return Mathf.Clamp01(Mathf.Max(lower * front * (1f - lips), stache));
            }
            Sculpt.Ellipsoid(hair, _skullC, _skullR, 3, d =>
            {
                float n = Sculpt.Noise3(d * 8f, _seed + 8) * 2f - 1f;
                float chin = Sculpt.Smooth01((-d.y - 0.7f) / 0.25f) * Mathf.Max(0f, d.z);
                return SkullOffset(d) * Mathf.Lerp(0.9f, 1.05f + 0.06f * chin + 0.008f * n, Mask(d));
            }, (q, d) => Sculpt.WithAo(Sculpt.Shade(_hair, 0.9f), Mathf.Lerp(0.7f, 1f, Mask(d))), true);
        }

        private void Headwear()
        {
            if (_p.Headwear == HumanoidRig.Headwear.Cap)
            {
                MeshBuilder cloth = B("Head", CharacterSurface.Cloth);
                float Mask(Vector3 d) => Sculpt.Step(-0.015f, 0.015f, d.y - (0.2f + 0.2f * d.z));
                Sculpt.Ellipsoid(cloth, _skullC, _skullR, 3, d => SkullOffset(d) * Mathf.Lerp(0.88f, 1.105f + 0.02f * Mathf.Max(0f, d.y), Mask(d)),
                    (q, d) => Sculpt.WithAo(Mathf.Repeat(Mathf.Atan2(d.x, d.z) * 3f / Mathf.PI, 1f) < 0.04f && d.y > 0.4f ? Sculpt.Shade(_accent, 0.8f) : _accent, Mathf.Lerp(0.7f, 1f, Mask(d))), true);
                Vector3 edge = _skullC + SkullOffset(new Vector3(0f, 0.4f, 0.92f).normalized) * 1.1f;
                var brim = new List<Vector2>();
                for (int i = 0; i <= 10; i++)
                {
                    float a = Mathf.Lerp(-100f, 100f, i / 10f) * Mathf.Deg2Rad;
                    brim.Add(new Vector2(Mathf.Sin(a) * 0.09f, Mathf.Max(-0.025f, Mathf.Cos(a) * 0.082f)));
                }
                brim.Reverse();
                Matrix4x4 saved = cloth.Matrix;
                cloth.Matrix = saved * Matrix4x4.TRS(edge + new Vector3(0f, -0.006f, -0.012f), Quaternion.Euler(12f, 0f, 0f), Vector3.one);
                Sculpt.Slab(cloth, brim, -0.004f, 0.004f, Sculpt.Shade(_accent, 0.85f));
                cloth.Matrix = saved;
                Sculpt.Ball(cloth, _skullC + SkullOffset(Vector3.up) * 1.12f, new Vector3(0.008f, 0.004f, 0.008f), Sculpt.Shade(_accent, 0.8f));
            }
            else if (_p.Headwear == HumanoidRig.Headwear.Beanie)
            {
                MeshBuilder knit = B("Head", CharacterSurface.Knit);
                float H(Vector3 d) => d.z >= 0f ? Mathf.Lerp(-0.02f, 0.36f, d.z) : Mathf.Lerp(-0.02f, -0.28f, -d.z);
                float Mask(Vector3 d) => Sculpt.Step(-0.02f, 0.02f, d.y - H(d));
                Sculpt.Ellipsoid(knit, _skullC, _skullR, 3, d => SkullOffset(d) * Mathf.Lerp(0.88f, 1.13f + 0.07f * Sculpt.Smooth01((d.y - 0.6f) / 0.4f), Mask(d)),
                    (q, d) => Sculpt.WithAo(_accent, Mathf.Lerp(0.7f, 1f, Mask(d))), true);
                var path = new List<Vector3>();
                for (int i = 0; i < 28; i++)
                {
                    float a = i * Mathf.PI * 2f / 28f;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    for (int it = 0; it < 3; it++)
                    {
                        float h = H(d) + 0.03f;
                        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - h * h));
                        d = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
                    }
                    path.Add(_skullC + SkullOffset(d) * 1.14f);
                }
                Sculpt.Tube(knit, path, Same(28, 0.011f), Same(28, 0.02f), Vector3.up, 8, _ => Sculpt.Shade(_accent, 0.9f), false, true);
            }
        }

        // ---------------------------------------------------------------- gear

        private void Backpack()
        {
            MeshBuilder canvas = B("Chest", CharacterSurface.Canvas);
            MeshBuilder leather = B("Chest", CharacterSurface.Leather);
            Color pack = _accent;
            const float cz = -0.185f;
            var body = new[]
            {
                new Ring(0.035f, 0.12f, 0.06f, 0.065f, 3f, 0.7f),
                new Ring(0.055f, 0.146f, 0.078f, 0.086f, 3f, 0.85f),
                new Ring(0.2f, 0.152f, 0.08f, 0.096f, 3f),
                new Ring(0.36f, 0.148f, 0.08f, 0.09f, 3f),
                new Ring(0.43f, 0.14f, 0.075f, 0.08f, 3f),
                new Ring(0.445f, 0.11f, 0.06f, 0.06f, 3f)
            };
            for (int i = 0; i < body.Length; i++) body[i] = body[i].At(0f, cz);
            Sculpt.Loft(canvas, body, 20, q => Sculpt.WithAo(pack, q.z > -0.12f ? 0.7f : 1f), true, true);
            canvas.Color = Sculpt.Shade(pack, 0.82f);
            Sculpt.Box(canvas, new Vector3(0f, 0.448f, cz - 0.005f), new Vector3(0.3f, 0.055f, 0.19f), null, 0.022f);
            Sculpt.Box(canvas, new Vector3(0f, 0.398f, -0.284f), new Vector3(0.24f, 0.09f, 0.02f), null, 0.008f);
            canvas.Color = Sculpt.Shade(pack, 0.9f);
            Sculpt.Box(canvas, new Vector3(0f, 0.15f, -0.292f), new Vector3(0.21f, 0.15f, 0.055f), null, 0.02f);
            foreach (int sx in new[] { -1, 1 })
            {
                Sculpt.Loft(canvas, new[]
                {
                    new Ring(0.06f, 0.036f, 0.04f, 0.04f, 2.4f, 0.8f).At(sx * 0.158f, cz),
                    new Ring(0.07f, 0.042f, 0.046f, 0.046f, 2.4f).At(sx * 0.16f, cz),
                    new Ring(0.19f, 0.044f, 0.048f, 0.048f, 2.4f).At(sx * 0.162f, cz),
                    new Ring(0.195f, 0.036f, 0.04f, 0.04f, 2.4f, 0.6f).At(sx * 0.162f, cz)
                }, 12, _ => Sculpt.Shade(pack, 0.88f), true, true);
            }

            // Compression straps on the back, and buckles.
            Color strap = Sculpt.Lin(0.14f, 0.12f, 0.1f);
            foreach (int sx in new[] { -1, 1 })
            {
                var path = new List<Vector3>();
                foreach (float y in new[] { 0.06f, 0.14f, 0.24f, 0.34f, 0.42f })
                {
                    Ring r = Sculpt.RingAt(body, y);
                    path.Add(new Vector3(sx * 0.075f, y, cz - r.Rb - 0.003f - (y < 0.23f ? 0.058f : 0f)));
                }
                Sculpt.Tube(leather, path, Same(path.Count, 0.012f), Same(path.Count, 0.0025f), Vector3.back, 4, _ => strap);
                leather.Color = _metal;
                Sculpt.Box(leather, new Vector3(sx * 0.075f, 0.3f, cz - 0.096f), new Vector3(0.03f, 0.022f, 0.006f), null, 0.002f);
            }

            // Shoulder straps over the jacket, joined by a sternum strap.
            foreach (int sx in new[] { -1, 1 })
            {
                float a(float deg) => sx > 0 ? deg : 180f - deg;
                // From the top of the pack, over the shoulder and down the chest (the carried "up" stays the surface normal).
                var path = new List<Vector3>
                {
                    new Vector3(sx * 0.092f, 0.4f, -0.11f),
                    new Vector3(sx * 0.098f, 0.435f, -0.07f),
                    new Vector3(sx * 0.1f, 0.448f, -0.01f),
                    new Vector3(sx * 0.1f, 0.425f, 0.05f),
                    Sculpt.Surf(_torso, 0.35f, a(62f), 0.009f),
                    Sculpt.Surf(_torso, 0.26f, a(58f), 0.008f),
                    Sculpt.Surf(_torso, 0.16f, a(48f), 0.008f),
                    Sculpt.Surf(_torso, 0.07f, a(28f), 0.008f),
                    new Vector3(sx * 0.15f, 0.04f, -0.06f)
                };
                Sculpt.Tube(leather, path, Same(path.Count, 0.022f), Same(path.Count, 0.006f), Vector3.back, 6, _ => strap);
                leather.Color = _metal;
                Vector3 b = Sculpt.Surf(_torso, 0.12f, a(38f), 0.014f, out Vector3 bn);
                Sculpt.Box(leather, b, new Vector3(0.03f, 0.02f, 0.006f), Quaternion.LookRotation(bn, Vector3.up), 0.002f);
            }
            var sternum = new List<Vector3>();
            for (int i = 0; i <= 6; i++) sternum.Add(Sculpt.Surf(_torso, 0.27f, Mathf.Lerp(120f, 60f, i / 6f), 0.014f));
            Sculpt.Tube(leather, sternum, Same(sternum.Count, 0.008f), Same(sternum.Count, 0.0025f), Vector3.forward, 4, _ => strap);
            leather.Color = Sculpt.Lin(0.1f, 0.1f, 0.1f);
            Sculpt.Box(leather, Sculpt.Surf(_torso, 0.27f, 90f, 0.018f), new Vector3(0.03f, 0.02f, 0.008f), null, 0.003f);

            // Rolled bedroll on top, strapped down.
            MeshBuilder cloth = B("Chest", CharacterSurface.Cloth);
            Color roll = Sculpt.Lin(0.33f, 0.37f, 0.4f);
            var axis = new List<Vector3>();
            for (int i = 0; i <= 6; i++) axis.Add(new Vector3(Mathf.Lerp(-0.2f, 0.2f, i / 6f), 0.522f, cz - 0.005f));
            var rr = new float[axis.Count];
            for (int i = 0; i < rr.Length; i++) rr[i] = 0.064f + 0.003f * Mathf.Sin(i * 2.1f);
            Sculpt.Tube(cloth, axis, rr, rr, Vector3.up, 16, q => Mathf.Abs(q.x) > 0.195f ? Sculpt.Shade(roll, 0.7f) : roll);
            foreach (float x in new[] { -0.11f, 0.11f })
                Sculpt.Loop(leather, new Vector3(x, 0.522f, cz - 0.005f), Vector3.up, Vector3.forward, 0.068f, 0.068f, 0.003f, 0.011f, 16, strap);
        }

        private void Satchel()
        {
            MeshBuilder canvas = B("Chest", CharacterSurface.Canvas);
            MeshBuilder leather = B("Chest", CharacterSurface.Leather);
            Color strap = Sculpt.Shade(_accent, 0.6f);
            var front = new List<Vector3> { new Vector3(0.1f, 0.452f, -0.04f), new Vector3(0.1f, 0.44f, 0.04f) };
            float[] ys = { 0.37f, 0.27f, 0.17f, 0.08f, 0.0f, -0.05f };
            float[] angs = { 72f, 82f, 98f, 118f, 142f, 160f };
            for (int i = 0; i < ys.Length; i++) front.Add(Sculpt.Surf(_torso, Mathf.Max(ys[i], _hem + 0.01f), angs[i], 0.01f));
            Sculpt.Tube(leather, front, Same(front.Count, 0.017f), Same(front.Count, 0.004f), Vector3.up, 4, _ => strap);
            var back = new List<Vector3> { new Vector3(0.1f, 0.452f, -0.04f) };
            float[] bys = { 0.38f, 0.27f, 0.16f, 0.06f, -0.02f };
            float[] bangs = { 300f, 268f, 238f, 212f, 196f };
            for (int i = 0; i < bys.Length; i++) back.Add(Sculpt.Surf(_torso, Mathf.Max(bys[i], _hem + 0.01f), bangs[i], 0.01f));
            Sculpt.Tube(leather, back, Same(back.Count, 0.017f), Same(back.Count, 0.004f), Vector3.up, 4, _ => strap);
            Quaternion rot = Quaternion.Euler(0f, -60f, 0f);
            var bag = new Vector3(-0.15f, -0.09f, 0.075f);
            canvas.Color = _accent;
            Sculpt.Box(canvas, bag, new Vector3(0.21f, 0.17f, 0.065f), rot, 0.022f);
            canvas.Color = Sculpt.Shade(_accent, 0.8f);
            Sculpt.Box(canvas, bag + rot * new Vector3(0f, 0.035f, 0.03f), new Vector3(0.215f, 0.11f, 0.012f), rot, 0.005f);
            leather.Color = _metal;
            Sculpt.Box(leather, bag + rot * new Vector3(0f, -0.01f, 0.038f), new Vector3(0.025f, 0.018f, 0.006f), rot, 0.002f);
        }

        private void Bandana()
        {
            MeshBuilder cloth = B("Chest", CharacterSurface.Cloth);
            Color band = Sculpt.Lin(0.52f, 0.17f, 0.13f);
            var path = new List<Vector3>();
            for (int i = 0; i < 18; i++)
            {
                float a = i * Mathf.PI * 2f / 18f;
                path.Add(new Vector3(Mathf.Cos(a) * 0.066f, 0.462f - 0.012f * Mathf.Max(0f, Mathf.Sin(a)), 0.002f + Mathf.Sin(a) * 0.062f));
            }
            Sculpt.Tube(cloth, path, Same(18, 0.013f), Same(18, 0.016f), Vector3.up, 8, _ => band, false, true);
            cloth.Color = Sculpt.Shade(band, 0.9f);
            Sculpt.Box(cloth, new Vector3(0f, 0.405f, 0.083f), new Vector3(0.05f, 0.05f, 0.008f), Quaternion.Euler(-12f, 0f, 45f), 0.004f);
        }
    }

    /// <summary>Tools, weapons and carried items modelled for the fist (see <see cref="MeshFactory.HeldItem"/>).</summary>
    internal static class HeldItemSculptor
    {
        private static readonly Color Haft = Sculpt.Lin(0.47f, 0.34f, 0.21f);
        private static readonly Color Branch = Sculpt.Lin(0.37f, 0.28f, 0.19f);
        private static readonly Color Iron = Sculpt.Lin(0.27f, 0.27f, 0.28f);
        private static readonly Color Bright = Sculpt.Lin(0.8f, 0.8f, 0.8f);
        private static readonly Color Cord = Sculpt.Lin(0.7f, 0.6f, 0.42f);

        public static List<KeyValuePair<CharacterSurface, Mesh>> Build(ItemDefinition item)
        {
            var parts = new Dictionary<CharacterSurface, MeshBuilder>();
            MeshBuilder B(CharacterSurface s)
            {
                if (!parts.TryGetValue(s, out MeshBuilder b)) parts[s] = b = new MeshBuilder();
                return b;
            }
            string id = item.Id ?? "";
            int seed = id.GetHashCode() & 0xffff;
            ToolType type = item.Tool?.Type ?? ToolType.None;
            bool stone = id.StartsWith("stone");
            switch (type)
            {
                case ToolType.Axe:
                    if (stone) StoneAxe(B, seed);
                    else IronAxe(B);
                    break;
                case ToolType.Pickaxe:
                    if (stone) StonePick(B, seed);
                    else IronPick(B);
                    break;
                case ToolType.Hammer:
                    Hammer(B);
                    break;
                case ToolType.Knife:
                case ToolType.Weapon:
                    if (id.Contains("pipe")) Pipe(B);
                    else Machete(B, type == ToolType.Knife ? 0.6f : 1f);
                    break;
                default:
                    Carried(B, item, seed);
                    break;
            }
            var list = new List<KeyValuePair<CharacterSurface, Mesh>>();
            foreach (var p in parts) list.Add(new KeyValuePair<CharacterSurface, Mesh>(p.Key, p.Value.Build("held " + id + " " + p.Key)));
            return list;
        }

        private static float[] Profile(int n, Func<float, float> f)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = f(i / (float)(n - 1));
            return a;
        }

        /// <summary>A straight, slightly oval turned handle along +X.</summary>
        private static void Handle(MeshBuilder wood, float x0, float x1, float r, bool knob)
        {
            const int n = 8;
            var path = new Vector3[n];
            var ry = new float[n];
            var rz = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                // A swelled knob at the butt (curving slightly down on axes and picks), tapering towards the head.
                path[i] = new Vector3(Mathf.Lerp(x0, x1, t), knob ? -0.012f * (1f - Sculpt.Smooth01(t * 4f)) : 0f, 0f);
                ry[i] = r * (knob && t < 0.1f ? 1.25f : 1f) * Mathf.Lerp(1.08f, 0.92f, t);
                rz[i] = ry[i] * 0.8f;
            }
            Sculpt.Tube(wood, path, rz, ry, Vector3.up, 10, q => q.x < x0 + 0.02f ? Sculpt.Shade(Haft, 0.75f) : Haft);
        }

        /// <summary>A rough branch haft with knots.</summary>
        private static void Stick(MeshBuilder wood, float x0, float x1, float r, int seed)
        {
            const int n = 9;
            var path = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                path[i] = new Vector3(Mathf.Lerp(x0, x1, t), (MeshFactory.Hash(seed, new Vector3(i, 0f, 0f)) - 0.5f) * 0.006f, (MeshFactory.Hash(seed, new Vector3(i, 1f, 0f)) - 0.5f) * 0.006f);
            }
            float[] rr = Profile(n, t => r * Mathf.Lerp(1.1f, 0.9f, t) * (0.94f + 0.12f * MeshFactory.Hash(seed + 3, new Vector3(t, 0f, 0f))));
            Sculpt.Tube(wood, path, rr, rr, Vector3.up, 8, q => Sculpt.Shade(Branch, 0.9f + 0.2f * Sculpt.Noise3(q * 60f, seed)));
            Sculpt.Ball(wood, new Vector3(Mathf.Lerp(x0, x1, 0.45f), r * 0.8f, 0.004f), new Vector3(0.008f, 0.006f, 0.007f), Sculpt.Shade(Branch, 0.8f));
        }

        /// <summary>Twine wrapped around a haft at <paramref name="x"/>, holding a head.</summary>
        private static void Lashing(MeshBuilder twine, float x, float yLow, float yHigh, float halfDepth)
        {
            float cy = (yLow + yHigh) * 0.5f, ry = (yHigh - yLow) * 0.5f;
            for (int i = 0; i < 4; i++)
                Sculpt.Loop(twine, new Vector3(x - 0.022f + i * 0.0145f, cy, 0f), Vector3.up, Vector3.forward, ry, halfDepth, 0.0028f, 0.0028f, 12, Sculpt.Shade(Cord, 0.9f + 0.05f * i));
            foreach (float tilt in new[] { -32f, 32f })
            {
                Quaternion q = Quaternion.Euler(0f, 0f, tilt);
                Sculpt.Loop(twine, new Vector3(x, cy, 0f), q * Vector3.up, Vector3.forward, ry * 1.12f, halfDepth * 1.06f, 0.0026f, 0.0026f, 12, Cord);
            }
        }

        private static void StoneAxe(Func<CharacterSurface, MeshBuilder> B, int seed)
        {
            Stick(B(CharacterSurface.Wood), -0.09f, 0.49f, 0.0165f, seed);
            Sculpt.Ellipsoid(B(CharacterSurface.Stone), new Vector3(0.425f, -0.04f, 0f), Vector3.one, 1, d =>
            {
                float j = 1f + (MeshFactory.Hash(seed, d) - 0.5f) * 0.18f;
                float edge = d.y < 0f ? Mathf.Lerp(1f, 0.14f, -d.y) : 1f;
                return new Vector3(d.x * 0.046f * (1f + 0.3f * Mathf.Max(0f, -d.y)), d.y * 0.088f, d.z * 0.027f * edge) * j;
            }, (q, d) => Sculpt.Lin(0.46f + 0.06f * d.x, 0.44f + 0.05f * d.x, 0.4f), false);
            Lashing(B(CharacterSurface.Twine), 0.425f, -0.03f, 0.034f, 0.03f);
        }

        private static void StonePick(Func<CharacterSurface, MeshBuilder> B, int seed)
        {
            Stick(B(CharacterSurface.Wood), -0.09f, 0.55f, 0.0165f, seed + 1);
            Sculpt.Ellipsoid(B(CharacterSurface.Stone), new Vector3(0.515f, -0.005f, 0f), Vector3.one, 2, d =>
            {
                float j = 1f + (MeshFactory.Hash(seed, d) - 0.5f) * 0.14f;
                float taper = Mathf.Pow(1f - Mathf.Abs(d.y), 0.8f);
                return new Vector3(d.x * 0.026f * taper - 0.04f * d.y * d.y, d.y * 0.15f, d.z * 0.021f * taper) * j;
            }, (q, d) => Sculpt.Lin(0.4f, 0.39f, 0.37f), false);
            Lashing(B(CharacterSurface.Twine), 0.515f, -0.035f, 0.03f, 0.026f);
        }

        private static void IronAxe(Func<CharacterSurface, MeshBuilder> B)
        {
            Handle(B(CharacterSurface.Wood), -0.085f, 0.482f, 0.0145f, true);
            var outline = new[]
            {
                new Vector2(0.418f, 0.036f), new Vector2(0.474f, 0.036f), new Vector2(0.478f, -0.02f), new Vector2(0.506f, -0.096f), new Vector2(0.502f, -0.12f),
                new Vector2(0.456f, -0.134f), new Vector2(0.4f, -0.124f), new Vector2(0.384f, -0.1f), new Vector2(0.412f, -0.02f)
            };
            Sculpt.Plate(B(CharacterSurface.Steel), new Vector2(0.445f, -0.03f), outline, new[] { 1f, 0.8f },
                (p, ring) => ring == 0 && p.y < -0.06f ? 0.0012f : Mathf.Lerp(0.0045f, 0.0165f, Sculpt.Step(-0.115f, -0.015f, p.y)),
                (p, ring) => ring == 0 && p.y < -0.07f ? Bright : Iron);
            B(CharacterSurface.Wood).Color = Sculpt.Shade(Haft, 0.6f);
            Sculpt.Box(B(CharacterSurface.Wood), new Vector3(0.484f, 0f, 0f), new Vector3(0.006f, 0.026f, 0.012f), null, 0.002f);
        }

        private static void IronPick(Func<CharacterSurface, MeshBuilder> B)
        {
            Handle(B(CharacterSurface.Wood), -0.085f, 0.56f, 0.0145f, true);
            MeshBuilder steel = B(CharacterSurface.Steel);
            steel.Color = Iron;
            Sculpt.Box(steel, new Vector3(0.535f, 0f, 0f), new Vector3(0.05f, 0.046f, 0.034f), null, 0.005f);
            var down = new[] { new Vector3(0.535f, -0.015f, 0f), new Vector3(0.53f, -0.07f, 0f), new Vector3(0.515f, -0.12f, 0f), new Vector3(0.49f, -0.162f, 0f), new Vector3(0.462f, -0.192f, 0f) };
            Sculpt.Tube(steel, down, new[] { 0.016f, 0.013f, 0.01f, 0.006f, 0.0012f }, new[] { 0.014f, 0.012f, 0.009f, 0.006f, 0.0012f }, Vector3.forward, 4,
                q => q.y < -0.16f ? Bright : Iron);
            var up = new[] { new Vector3(0.535f, 0.015f, 0f), new Vector3(0.53f, 0.07f, 0f), new Vector3(0.515f, 0.12f, 0f), new Vector3(0.493f, 0.158f, 0f) };
            Sculpt.Tube(steel, up, new[] { 0.016f, 0.012f, 0.008f, 0.0025f }, new[] { 0.014f, 0.014f, 0.015f, 0.016f }, Vector3.forward, 4,
                q => q.y > 0.14f ? Bright : Iron);
        }

        private static void Hammer(Func<CharacterSurface, MeshBuilder> B)
        {
            Handle(B(CharacterSurface.Wood), -0.07f, 0.29f, 0.0125f, false);
            MeshBuilder grip = B(CharacterSurface.Leather);
            var path = new[] { new Vector3(-0.075f, 0f, 0f), new Vector3(-0.03f, 0f, 0f), new Vector3(0.02f, 0f, 0f), new Vector3(0.07f, 0f, 0f) };
            Sculpt.Tube(grip, path, new[] { 0.0145f, 0.0135f, 0.014f, 0.0125f }, new[] { 0.0175f, 0.0165f, 0.017f, 0.015f }, Vector3.up, 10,
                q => Mathf.Repeat(q.x * 40f, 1f) < 0.2f ? Sculpt.Lin(0.12f, 0.04f, 0.03f) : Sculpt.Lin(0.42f, 0.09f, 0.06f));
            MeshBuilder steel = B(CharacterSurface.Steel);
            steel.Color = Iron;
            Sculpt.Box(steel, new Vector3(0.28f, 0.004f, 0f), new Vector3(0.036f, 0.044f, 0.026f), null, 0.004f);
            Sculpt.Loft(steel, new[]
            {
                new Ring(-0.068f, 0.014f, 0.014f, 0.014f, 2f, 1f).At(0.28f, 0f),
                new Ring(-0.064f, 0.0168f, 0.0168f, 0.0168f).At(0.28f, 0f),
                new Ring(-0.05f, 0.0168f, 0.0168f, 0.0168f).At(0.28f, 0f),
                new Ring(-0.035f, 0.013f, 0.013f, 0.013f).At(0.28f, 0f),
                new Ring(-0.015f, 0.014f, 0.013f, 0.013f).At(0.28f, 0f)
            }, 12, q => q.y < -0.062f ? Bright : Iron, true, false);
            foreach (float z in new[] { -0.0065f, 0.0065f })
            {
                var claw = new[] { new Vector3(0.28f, 0.02f, z), new Vector3(0.279f, 0.05f, z * 1.05f), new Vector3(0.268f, 0.075f, z * 1.15f), new Vector3(0.25f, 0.088f, z * 1.25f) };
                Sculpt.Tube(steel, claw, new[] { 0.013f, 0.011f, 0.007f, 0.0025f }, new[] { 0.0055f, 0.005f, 0.004f, 0.0025f }, Vector3.forward, 6, _ => Iron);
            }
        }

        private static void Machete(Func<CharacterSurface, MeshBuilder> B, float length)
        {
            MeshBuilder grip = B(CharacterSurface.Leather);
            var path = new[] { new Vector3(-0.078f, -0.004f, 0f), new Vector3(-0.04f, 0f, 0f), new Vector3(0.01f, 0f, 0f), new Vector3(0.06f, 0.002f, 0f), new Vector3(0.085f, 0.002f, 0f) };
            Sculpt.Tube(grip, path, new[] { 0.012f, 0.011f, 0.0115f, 0.011f, 0.0105f }, new[] { 0.019f, 0.016f, 0.017f, 0.016f, 0.015f }, Vector3.up, 10, _ => Sculpt.Lin(0.1f, 0.09f, 0.08f));
            MeshBuilder steel = B(CharacterSurface.Steel);
            steel.Color = Iron;
            Sculpt.Box(steel, new Vector3(0.09f, -0.004f, 0f), new Vector3(0.012f, 0.05f, 0.026f), null, 0.003f);
            foreach (float x in new[] { -0.045f, 0.03f })
                Sculpt.Cylinder(steel, new Vector3(x, 0f, -0.0125f), new Vector3(x, 0f, 0.0125f), 0.0045f, 8);
            float L(float x) => 0.09f + (x - 0.09f) * length;
            var outline = new[]
            {
                new Vector2(L(0.092f), 0.012f), new Vector2(L(0.3f), 0.016f), new Vector2(L(0.47f), 0.019f), new Vector2(L(0.535f), 0.012f), new Vector2(L(0.565f), -0.008f),
                new Vector2(L(0.555f), -0.03f), new Vector2(L(0.52f), -0.046f), new Vector2(L(0.4f), -0.044f), new Vector2(L(0.2f), -0.034f), new Vector2(L(0.092f), -0.024f)
            };
            Sculpt.Plate(steel, new Vector2(L(0.3f), -0.012f), outline, new[] { 1f, 0.72f },
                (p, ring) => ring == 0 ? (p.y < -0.01f ? 0.0006f : 0.0022f) : 0.0026f,
                (p, ring) => ring == 0 && p.y < -0.015f ? Bright : Sculpt.Mix(Sculpt.Lin(0.52f, 0.53f, 0.54f), Sculpt.Lin(0.4f, 0.25f, 0.15f), p.y > 0.008f ? Sculpt.Noise3(new Vector3(p.x, p.y, 0f) * 40f, 77) * 0.8f : 0f));
        }

        private static void Pipe(Func<CharacterSurface, MeshBuilder> B)
        {
            MeshBuilder steel = B(CharacterSurface.Steel);
            Color lead = Sculpt.Lin(0.38f, 0.39f, 0.4f);
            var path = new[] { new Vector3(-0.1f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0.36f, 0f, 0f) };
            Sculpt.Tube(steel, path, Same3(0.0165f), Same3(0.0165f), Vector3.up, 12, q => Sculpt.Shade(lead, 0.85f + 0.3f * Sculpt.Noise3(q * 30f, 5)));
            Color fitting = Sculpt.Lin(0.25f, 0.24f, 0.22f);
            Sculpt.Tube(steel, new[] { new Vector3(0.34f, 0f, 0f), new Vector3(0.375f, 0f, 0f), new Vector3(0.392f, -0.006f, 0f), new Vector3(0.4f, -0.03f, 0f), new Vector3(0.401f, -0.05f, 0f) },
                new[] { 0.023f, 0.023f, 0.023f, 0.022f, 0.022f }, new[] { 0.023f, 0.023f, 0.023f, 0.022f, 0.022f }, Vector3.forward, 12, _ => fitting);
            steel.Color = fitting;
            Sculpt.Cylinder(steel, new Vector3(0.401f, -0.05f, 0f), new Vector3(0.401f, -0.057f, 0f), 0.026f, 8);
            MeshBuilder tape = B(CharacterSurface.Twine);
            Sculpt.Tube(tape, new[] { new Vector3(-0.09f, 0f, 0f), new Vector3(-0.01f, 0f, 0f), new Vector3(0.07f, 0f, 0f) }, Same3(0.0188f), Same3(0.0188f), Vector3.up, 12,
                q => Mathf.Repeat(q.x * 45f + q.y * 8f, 1f) < 0.18f ? Sculpt.Lin(0.1f, 0.1f, 0.1f) : Sculpt.Lin(0.2f, 0.2f, 0.19f));
        }

        private static float[] Same3(float v) => new[] { v, v, v };

        /// <summary>Resources and supplies: a log, a rock, a fibre sheaf, a bottle, a can, or a tied cloth bundle in the item's colour.</summary>
        private static void Carried(Func<CharacterSurface, MeshBuilder> B, ItemDefinition item, int seed)
        {
            string id = item.Id ?? "";
            Color tint = Sculpt.Lin(Conversions.ParseColor(item.Color, Color.gray));
            if (id == "wood" || id == "planks")
            {
                MeshBuilder wood = B(CharacterSurface.Wood);
                if (id == "planks")
                {
                    wood.Color = Sculpt.Lin(0.6f, 0.46f, 0.3f);
                    Sculpt.Box(wood, new Vector3(0.12f, 0f, 0f), new Vector3(0.4f, 0.09f, 0.022f), null, 0.004f);
                    return;
                }
                var path = new[] { new Vector3(-0.1f, -0.03f, 0f), new Vector3(0.05f, -0.032f, 0f), new Vector3(0.2f, -0.03f, 0f) };
                Sculpt.Tube(wood, path, Same3(0.045f), Same3(0.042f), Vector3.up, 10, q => Mathf.Abs(q.x - 0.05f) > 0.148f ? Sculpt.Lin(0.62f, 0.48f, 0.32f) : Sculpt.Lin(0.3f, 0.22f, 0.15f));
                return;
            }
            if (id == "stone" || id.EndsWith("_ore") || id == "scrap_metal")
            {
                CharacterSurface s = id == "scrap_metal" ? CharacterSurface.Steel : CharacterSurface.Stone;
                Sculpt.Ellipsoid(B(s), new Vector3(0.02f, -0.03f, 0f), Vector3.one, 1, d => Vector3.Scale(d, new Vector3(0.06f, 0.045f, 0.05f)) * (1f + (MeshFactory.Hash(seed, d) - 0.5f) * 0.3f),
                    (q, d) => Sculpt.Mix(tint, Sculpt.Lin(0.4f, 0.39f, 0.37f), 0.5f + 0.3f * d.y), false);
                return;
            }
            if (id == "fiber")
            {
                MeshBuilder twine = B(CharacterSurface.Twine);
                for (int i = 0; i < 9; i++)
                {
                    float a = i * 2.4f, r = 0.012f * Mathf.Sqrt(i / 9f);
                    var off = new Vector3(0f, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                    var stalk = new[] { new Vector3(-0.09f, 0f, 0f) + off * 0.6f, new Vector3(0.02f, 0f, 0f) + off, new Vector3(0.16f + MeshFactory.Hash(seed, new Vector3(i, 0, 0)) * 0.05f, 0f, 0f) + off * 2.5f };
                    Sculpt.Tube(twine, stalk, Same3(0.0035f), Same3(0.0035f), Vector3.up, 4, q => Sculpt.Mix(tint, Sculpt.Lin(0.55f, 0.5f, 0.3f), q.x > 0.12f ? 0.5f : 0.1f));
                }
                Sculpt.Loop(twine, new Vector3(0.035f, 0f, 0f), Vector3.up, Vector3.forward, 0.018f, 0.018f, 0.003f, 0.004f, 10, Cord);
                return;
            }
            if (id.Contains("bottle"))
            {
                Sculpt.Loft(B(CharacterSurface.Gloss), new[]
                {
                    new Ring(-0.1f, 0.03f, 0.03f, 0.03f, 2f).At(0f, 0f), new Ring(0.04f, 0.031f, 0.031f, 0.031f, 2f), new Ring(0.07f, 0.024f, 0.024f, 0.024f, 2f),
                    new Ring(0.09f, 0.012f, 0.012f, 0.012f, 2f)
                }, 14, q => Sculpt.Mix(tint, Color.white, 0.25f), true, false);
                Sculpt.Loft(B(CharacterSurface.Leather), new[] { new Ring(0.088f, 0.013f, 0.013f, 0.013f, 2f), new Ring(0.108f, 0.013f, 0.013f, 0.013f, 2f) }, 10,
                    _ => Sculpt.Lin(0.15f, 0.3f, 0.6f), false, true);
                return;
            }
            if (id.StartsWith("canned"))
            {
                Sculpt.Loft(B(CharacterSurface.Steel), new[]
                {
                    new Ring(-0.055f, 0.034f, 0.034f, 0.034f, 2f), new Ring(-0.05f, 0.036f, 0.036f, 0.036f, 2f), new Ring(0.05f, 0.036f, 0.036f, 0.036f, 2f),
                    new Ring(0.055f, 0.034f, 0.034f, 0.034f, 2f)
                }, 16, q => Mathf.Abs(q.y) < 0.042f ? tint : Bright, true, true);
                return;
            }
            // A cloth bundle tied at the neck, in the item's colour.
            MeshBuilder cloth = B(CharacterSurface.Canvas);
            Color sack = Sculpt.Mix(tint, Sculpt.Lin(0.55f, 0.48f, 0.36f), 0.45f);
            Sculpt.Ellipsoid(cloth, new Vector3(0f, -0.05f, 0f), Vector3.one, 2, d => Vector3.Scale(d, new Vector3(0.06f, 0.05f, 0.055f)) * (1f + (Sculpt.Noise3(d * 3f, seed) - 0.5f) * 0.25f),
                (q, d) => Sculpt.WithAo(sack, Mathf.Lerp(0.7f, 1f, (d.y + 1f) * 0.5f)), true);
            Sculpt.Loft(cloth, new[] { new Ring(-0.01f, 0.018f, 0.018f, 0.018f, 2f), new Ring(0.012f, 0.012f, 0.012f, 0.012f, 2f), new Ring(0.03f, 0.022f, 0.02f, 0.02f, 2f) }, 8,
                _ => Sculpt.Shade(sack, 0.9f), false, true);
            Sculpt.Loop(B(CharacterSurface.Twine), new Vector3(0f, 0.008f, 0f), Vector3.right, Vector3.forward, 0.016f, 0.016f, 0.003f, 0.004f, 10, Cord);
        }
    }
}
