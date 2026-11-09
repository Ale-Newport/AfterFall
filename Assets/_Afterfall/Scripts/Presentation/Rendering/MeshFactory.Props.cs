using System;
using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Small meshes shared by the world props (insulators, drums, rubble).</summary>
    public static partial class MeshFactory
    {
        /// <summary>Glazed porcelain insulator string hanging down from y=0: a stack of ribbed sheds.</summary>
        public static Mesh PropInsulatorString(float length, float radius) => Cached($"prop_insulator:{length:0.###}:{radius:0.###}", () =>
        {
            var b = new MeshBuilder();
            int discs = Mathf.Max(3, Mathf.RoundToInt(length / 0.13f));
            float step = length / discs;
            var profile = new List<Vector2>();
            for (int i = discs - 1; i >= 0; i--)
            {
                float y = -length + i * step;
                profile.Add(new Vector2(radius * 0.32f, y));
                profile.Add(new Vector2(radius, y + step * 0.3f));
                profile.Add(new Vector2(radius * 0.92f, y + step * 0.45f));
                profile.Add(new Vector2(radius * 0.32f, y + step * 0.75f));
            }
            profile.Add(new Vector2(radius * 0.2f, 0f));
            profile.Add(new Vector2(0f, 0.02f));
            b.Lathe(profile.ToArray(), 10, true);
            return b.Build("insulator string");
        });

        /// <summary>A 200-litre drum standing on y=0 (radius 0.29, height 0.88) with rolling hoops and a rim.</summary>
        public static Mesh PropDrum() => Cached("prop_drum", () =>
        {
            var b = new MeshBuilder();
            const float r = 0.29f, h = 0.88f;
            b.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(r - 0.01f, 0f), new Vector2(r, 0.02f), new Vector2(r, 0.28f), new Vector2(r + 0.012f, 0.29f),
                new Vector2(r + 0.012f, 0.31f), new Vector2(r, 0.32f), new Vector2(r, 0.56f), new Vector2(r + 0.012f, 0.57f), new Vector2(r + 0.012f, 0.59f),
                new Vector2(r, 0.6f), new Vector2(r, h - 0.02f), new Vector2(r + 0.006f, h), new Vector2(r - 0.02f, h), new Vector2(r - 0.025f, h - 0.012f),
                new Vector2(0f, h - 0.012f)
            }, 16, true);
            return b.Build("drum");
        });

        /// <summary>A low, flat-faced chunk of broken stone (rubble, talus, fire ring), roughly 1 unit wide.</summary>
        public static Mesh PropStone(int variant) => Rock(300 + variant, 0.24f, 0.62f);
    }

    /// <summary>Vertex colour (rgb multiplies the material, alpha is ambient occlusion) of a point on a prop surface.</summary>
    internal delegate Color PropShade(Vector3 position, Vector3 normal);

    /// <summary>
    /// Geometry helpers for world props: subdivided wall panels with openings (so vertex colours can
    /// carry grime and ambient occlusion), oriented polygons, beams, rods and sagging ropes. They add
    /// to a <see cref="MeshBuilder"/> in its current <see cref="MeshBuilder.Matrix"/>.
    /// </summary>
    internal static class PropGeo
    {
        /// <summary>
        /// A flat rectangle origin + right·u + up·v for u in [u0, u1], v in [v0, v1], facing
        /// <paramref name="normal"/>, minus rectangular holes (x..y along u, z..w along v). The grid is
        /// subdivided to at most <paramref name="cellU"/> × <paramref name="cellV"/> so the shade can vary.
        /// </summary>
        public static void Panel(MeshBuilder b, Vector3 origin, Vector3 right, Vector3 up, Vector3 normal, float u0, float u1, float v0, float v1,
            IList<Vector4> holes, float cellU, float cellV, PropShade shade, float[] extraV = null)
        {
            if (u1 - u0 < 1e-3f || v1 - v0 < 1e-3f) return;
            List<float> us = Breaks(u0, u1, cellU, holes, true, null);
            List<float> vs = Breaks(v0, v1, cellV, holes, false, extraV);
            int nu = us.Count, nv = vs.Count;
            int start = b.VertexCount;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    Vector3 p = origin + right * us[i] + up * vs[j];
                    b.Vertex(p, normal, shade != null ? shade(p, normal) : b.Color);
                }
            }
            bool flip = Vector3.Dot(Vector3.Cross(right, up), normal) < 0f;
            for (int j = 0; j + 1 < nv; j++)
            {
                for (int i = 0; i + 1 < nu; i++)
                {
                    if (InHole((us[i] + us[i + 1]) * 0.5f, (vs[j] + vs[j + 1]) * 0.5f, holes)) continue;
                    int a = start + j * nu + i, c = a + nu + 1;
                    if (flip)
                    {
                        b.Triangle(a, c, a + 1);
                        b.Triangle(a, a + nu, c);
                    }
                    else
                    {
                        b.Triangle(a, a + 1, c);
                        b.Triangle(a, c, a + nu);
                    }
                }
            }
        }

        private static bool InHole(float u, float v, IList<Vector4> holes)
        {
            if (holes == null) return false;
            foreach (Vector4 h in holes)
                if (u > h.x && u < h.y && v > h.z && v < h.w) return true;
            return false;
        }

        private static List<float> Breaks(float lo, float hi, float cell, IList<Vector4> holes, bool alongU, float[] extra)
        {
            var raw = new List<float> { lo, hi };
            if (holes != null)
            {
                foreach (Vector4 h in holes)
                {
                    float a = alongU ? h.x : h.z, c = alongU ? h.y : h.w;
                    if (a > lo && a < hi) raw.Add(a);
                    if (c > lo && c < hi) raw.Add(c);
                }
            }
            if (extra != null)
                foreach (float e in extra)
                    if (e > lo && e < hi) raw.Add(e);
            raw.Sort();
            var result = new List<float> { raw[0] };
            for (int i = 1; i < raw.Count; i++)
            {
                float a = result[result.Count - 1], c = raw[i];
                if (c - a < 1e-3f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt((c - a) / Mathf.Max(0.05f, cell) - 1e-3f));
                for (int k = 1; k <= n; k++) result.Add(a + (c - a) * k / n);
            }
            return result;
        }

        /// <summary>A flat convex polygon facing away from <paramref name="inside"/>, shaded per vertex.</summary>
        public static void Poly(MeshBuilder b, Vector3 inside, PropShade shade, params Vector3[] poly)
        {
            if (poly.Length < 3) return;
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 p in poly) centroid += p;
            centroid /= poly.Length;
            Vector3 n = Vector3.zero;
            for (int i = 1; i + 1 < poly.Length && n.sqrMagnitude < 1e-10f; i++) n = Vector3.Cross(poly[i] - poly[0], poly[i + 1] - poly[0]);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            bool flip = Vector3.Dot(n, centroid - inside) < 0f;
            if (flip) n = -n;
            int start = b.VertexCount;
            foreach (Vector3 p in poly) b.Vertex(p, n, shade != null ? shade(p, n) : b.Color);
            for (int i = 1; i + 1 < poly.Length; i++)
            {
                if (flip) b.Triangle(start, start + i + 1, start + i);
                else b.Triangle(start, start + i, start + i + 1);
            }
        }

        /// <summary>A flat quad a-b-c-d (convex, in order) facing <paramref name="facing"/>, with per-corner colours.</summary>
        public static void Quad(MeshBuilder b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, Vector3 facing, Color ca, Color cb, Color cc, Color cd)
        {
            Vector3 n = Vector3.Cross(c1 - a, c2 - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c2 - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            bool flip = Vector3.Dot(n, facing) < 0f;
            if (flip) n = -n;
            int s = b.VertexCount;
            b.Vertex(a, n, ca);
            b.Vertex(c1, n, cb);
            b.Vertex(c2, n, cc);
            b.Vertex(d, n, cd);
            if (flip)
            {
                b.Triangle(s, s + 2, s + 1);
                b.Triangle(s, s + 3, s + 2);
            }
            else
            {
                b.Triangle(s, s + 1, s + 2);
                b.Triangle(s, s + 2, s + 3);
            }
        }

        /// <summary>A rectangular beam from <paramref name="from"/> to <paramref name="to"/> (width along the side axis, depth along <paramref name="upHint"/>).</summary>
        public static void Beam(MeshBuilder b, Vector3 from, Vector3 to, float width, float depth, Vector3 upHint, float bevel = 0f)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = d / len;
            Vector3 up = Vector3.ProjectOnPlane(upHint, dir);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.forward, dir);
            b.Box((from + to) * 0.5f, new Vector3(width, depth, len), Quaternion.LookRotation(dir, up.normalized), bevel);
        }

        /// <summary>A round rod between two points.</summary>
        public static void Rod(MeshBuilder b, Vector3 from, Vector3 to, float radius, int sides = 6, bool caps = true)
        {
            b.Cylinder(from, to, radius, radius, sides, true, caps, caps);
        }

        /// <summary>A rope or cable sagging between two points.</summary>
        public static void Rope(MeshBuilder b, Vector3 from, Vector3 to, float radius, float sag, int segments = 6)
        {
            Vector3 prev = from;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.down * (sag * 4f * t * (1f - t));
                b.Cylinder(prev, p, radius, radius, 4, true, false, false);
                prev = p;
            }
        }

        /// <summary>Smooth deterministic 3D value noise in 0..1.</summary>
        public static float Noise(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = Fade(p.x - x0), fy = Fade(p.y - y0), fz = Fade(p.z - z0);
            float L(int dx, int dy, int dz) => MeshFactory.Hash(seed, new Vector3(x0 + dx, y0 + dy, z0 + dz));
            float a = Mathf.Lerp(Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx), Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx), fy);
            float c = Mathf.Lerp(Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx), Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx), fy);
            return Mathf.Lerp(a, c, fz);
        }

        /// <summary>Two octaves of <see cref="Noise"/>.</summary>
        public static float Fbm(Vector3 p, int seed) => Noise(p, seed) * 0.65f + Noise(p * 2.3f + new Vector3(17.1f, 3.3f, 9.7f), seed + 1) * 0.35f;

        private static float Fade(float t) => t * t * (3f - 2f * t);

        public static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Deterministic 0..1 value for an integer seed and index.</summary>
        public static float Hash(int seed, int i) => MeshFactory.Hash(seed, new Vector3(i * 1.37f, i * 0.71f + 3f, 5f));
    }

    /// <summary>Weathering shades shared by the props (all deterministic).</summary>
    internal static class PropShades
    {
        /// <summary>A single colour and occlusion.</summary>
        public static PropShade Solid(Color c) => (p, n) => c;

        /// <summary>
        /// Exterior weathering: dirt splash and darker occlusion near <paramref name="groundY"/>, broad
        /// faded patches, rain streaks running down from <paramref name="topY"/> and, for metal
        /// cladding, rust blooms (<paramref name="rust"/>).
        /// </summary>
        public static PropShade Weathered(int seed, float groundY, float topY, float grime = 1f, float patch = 0.15f, float streaks = 1f, float rust = 0f)
        {
            return (p, n) =>
            {
                float hgt = p.y - groundY;
                float dirt = (1f - PropGeo.Smooth(0f, 1.25f, hgt)) * grime;
                float big = PropGeo.Fbm(p * 0.33f, seed);
                float k = 1f - patch * (1f - big);
                float column = PropGeo.Noise(new Vector3((p.x + p.z) * 1.9f, p.y * 0.12f, 0.5f), seed + 7);
                float streak = PropGeo.Smooth(0.55f, 0.85f, column) * PropGeo.Smooth(topY - 2.8f, topY - 0.2f, p.y) * 0.26f * streaks;
                float r = Mathf.Clamp01(PropGeo.Smooth(0.52f, 0.8f, PropGeo.Fbm(new Vector3(p.x + p.z, p.y * 0.6f, p.x - p.z) * 0.45f, seed + 9)) + dirt * 0.5f) * rust;
                float ao = Mathf.Lerp(0.62f, 1f, PropGeo.Smooth(0f, 0.55f, hgt));
                return new Color(k * (1f - dirt * 0.36f) * (1f - streak), k * (1f - dirt * 0.4f) * (1f - streak * 1.05f) * (1f - r * 0.22f),
                    k * (1f - dirt * 0.48f) * (1f - streak * 1.15f) * (1f - r * 0.42f), ao);
            };
        }

        /// <summary>Interior walls: dimmer, with occlusion along the floor and the ceiling.</summary>
        public static PropShade Interior(int seed, float floorY, float ceilingY)
        {
            return (p, n) =>
            {
                // Unlit rooms read darker than the facade; corners and the floor line darker still.
                float k = 0.66f + 0.1f * PropGeo.Fbm(p * 0.5f, seed);
                float ao = Mathf.Lerp(0.55f, 1f, PropGeo.Smooth(0f, 0.45f, p.y - floorY)) * Mathf.Lerp(0.65f, 1f, PropGeo.Smooth(0f, 0.5f, ceilingY - p.y));
                return new Color(k, k * 0.98f, k * 0.95f, ao);
            };
        }

        /// <summary>Floors: worn traffic paths, dust in the corners.</summary>
        public static PropShade Floor(int seed, float halfX, float halfZ)
        {
            return (p, n) =>
            {
                float edge = Mathf.Min(halfX - Mathf.Abs(p.x), halfZ - Mathf.Abs(p.z));
                float dust = 1f - PropGeo.Smooth(0f, 0.7f, edge);
                float k = 0.68f + 0.14f * PropGeo.Fbm(p * 0.6f, seed);
                return new Color(k * (1f - dust * 0.2f), k * (1f - dust * 0.22f), k * (1f - dust * 0.26f), Mathf.Lerp(0.6f, 1f, PropGeo.Smooth(0f, 0.35f, edge)));
            };
        }

        /// <summary>
        /// Roofs: moss and lichen on upward faces (more toward the eaves), sun-faded patches, rust
        /// blooms on metal (<paramref name="rust"/>) and dark patches of missing shingles (<paramref name="damage"/>).
        /// </summary>
        public static PropShade Roof(int seed, float eaveY, float ridgeY, float moss, float rust = 0f, float damage = 0f)
        {
            return (p, n) =>
            {
                float up = PropGeo.Smooth(0.25f, 0.6f, n.y);
                float low = 1f - PropGeo.Smooth(eaveY, ridgeY + 0.01f, p.y) * 0.6f;
                float m = PropGeo.Smooth(0.5f, 0.78f, PropGeo.Fbm(p * 0.55f, seed) * low + 0.12f) * moss * up;
                float k = 0.88f + 0.14f * PropGeo.Fbm(p * 1.1f, seed + 3);
                float r = PropGeo.Smooth(0.5f, 0.78f, PropGeo.Fbm(p * 0.6f + new Vector3(31f, 7f, 13f), seed + 11)) * rust * up;
                float d = PropGeo.Smooth(0.74f, 0.8f, PropGeo.Noise(p * 1.4f, seed + 13)) * damage * up;
                float dk = 1f - d * 0.55f;
                return new Color(k * dk * Mathf.Lerp(1f, 0.82f, m), k * dk * Mathf.Lerp(1f, 1.18f, m) * (1f - r * 0.24f), k * dk * Mathf.Lerp(1f, 0.6f, m) * (1f - r * 0.45f), 1f);
            };
        }

        /// <summary>Metal that rusts from the bottom up and in streaks.</summary>
        public static PropShade Rusty(int seed, float groundY, float amount)
        {
            return (p, n) =>
            {
                float hgt = p.y - groundY;
                float r = Mathf.Clamp01((1f - PropGeo.Smooth(0f, 1.6f, hgt)) * 0.7f + PropGeo.Smooth(0.55f, 0.8f, PropGeo.Fbm(p * 0.9f, seed)) * 0.6f) * amount;
                float k = 0.9f + 0.1f * PropGeo.Noise(p * 1.7f, seed + 2);
                return new Color(k * Mathf.Lerp(1f, 0.92f, r), k * Mathf.Lerp(1f, 0.7f, r), k * Mathf.Lerp(1f, 0.52f, r), Mathf.Lerp(0.7f, 1f, PropGeo.Smooth(0f, 0.4f, hgt)));
            };
        }
    }

    /// <summary>
    /// Collects a prop's geometry in one <see cref="MeshBuilder"/> per material and frame, then bakes
    /// them into a few shared meshes (one renderer each). Baked kits are cached by a key holding every
    /// parameter, so identical props share meshes.
    /// </summary>
    internal sealed class PropKit
    {
        /// <summary>One baked renderer: mesh, material and placement under the prop root.</summary>
        public sealed class Piece
        {
            public string Name;
            public Material Material;
            public Mesh Mesh;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Shadows;
        }

        private sealed class Group
        {
            public string Frame;
            public Material Material;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Shadows;
            public readonly MeshBuilder Builder = new MeshBuilder();
        }

        private static readonly Dictionary<string, List<Piece>> Cache = new Dictionary<string, List<Piece>>();
        private readonly List<Group> _groups = new List<Group>();

        /// <summary>The builder for <paramref name="material"/> in the prop's own frame.</summary>
        public MeshBuilder this[Material material] => In(material);

        /// <summary>
        /// The builder for <paramref name="material"/> in a named frame placed at
        /// <paramref name="position"/>/<paramref name="rotation"/> (object-space materials map their
        /// texture in that frame, e.g. a roof half turned so its shingles run the right way).
        /// </summary>
        public MeshBuilder In(Material material, string frame = "", Vector3? position = null, Quaternion? rotation = null, bool shadows = true)
        {
            foreach (Group g in _groups)
                if (g.Material == material && g.Frame == frame && g.Shadows == shadows) return g.Builder;
            var group = new Group
            {
                Frame = frame,
                Material = material,
                Position = position ?? Vector3.zero,
                Rotation = rotation ?? Quaternion.identity,
                Shadows = shadows
            };
            _groups.Add(group);
            return group.Builder;
        }

        private List<Piece> Bake(string name)
        {
            var pieces = new List<Piece>();
            foreach (Group g in _groups)
            {
                if (g.Builder.VertexCount == 0) continue;
                pieces.Add(new Piece
                {
                    Name = string.IsNullOrEmpty(g.Frame) ? "Mesh" : g.Frame,
                    Material = g.Material,
                    Mesh = g.Builder.Build(name),
                    Position = g.Position,
                    Rotation = g.Rotation,
                    Shadows = g.Shadows
                });
            }
            return pieces;
        }

        /// <summary>Returns the cached pieces for <paramref name="key"/>, filling a new kit on first use.</summary>
        public static List<Piece> Cached(string key, Action<PropKit> fill)
        {
            if (Cache.TryGetValue(key, out List<Piece> pieces) && pieces.TrueForAll(p => p.Mesh != null && p.Material != null)) return pieces;
            var kit = new PropKit();
            fill(kit);
            pieces = kit.Bake(key);
            Cache[key] = pieces;
            return pieces;
        }

        /// <summary>Instantiates the pieces under <paramref name="parent"/> (renderers only, no colliders).</summary>
        public static void Spawn(Transform parent, List<Piece> pieces)
        {
            foreach (Piece p in pieces) Shapes.Mesh(parent, p.Name, p.Mesh, p.Material, p.Position, p.Rotation, p.Shadows);
        }
    }
}
