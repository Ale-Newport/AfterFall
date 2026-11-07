using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedural low-poly, flat-shaded meshes for the stylised art direction (trees, rocks,
    /// roofs...). Meshes are cached and shared between renderers.
    /// </summary>
    public static class MeshFactory
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();
        private static Mesh _cube, _quad;

        public static Mesh Cube
        {
            get
            {
                if (_cube == null) _cube = Builtin(PrimitiveType.Cube);
                return _cube;
            }
        }

        public static Mesh Quad
        {
            get
            {
                if (_quad == null) _quad = Builtin(PrimitiveType.Quad);
                return _quad;
            }
        }

        private static Mesh Builtin(PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        /// <summary>Unit cylinder: radius 0.5, from y=0 to y=1.</summary>
        public static Mesh Cylinder(int sides = 8) => Cached($"cyl{sides}", () => Lathe(new[] { new Vector2(0.5f, 0f), new Vector2(0.5f, 1f) }, sides, true, true));

        /// <summary>Unit cone: base radius 0.5 at y=0, apex at y=1.</summary>
        public static Mesh Cone(int sides = 8) => Cached($"cone{sides}", () => Lathe(new[] { new Vector2(0.5f, 0f), new Vector2(0f, 1f) }, sides, true, false));

        /// <summary>Triangular prism roof: 1×1×1, ridge along X at y=1, eaves at z=±0.5.</summary>
        public static Mesh GableRoof() => Cached("gable", () =>
        {
            var b = new FlatBuilder();
            Vector3 a0 = new Vector3(-0.5f, 0, -0.5f), a1 = new Vector3(-0.5f, 0, 0.5f), a2 = new Vector3(-0.5f, 1, 0);
            Vector3 b0 = new Vector3(0.5f, 0, -0.5f), b1 = new Vector3(0.5f, 0, 0.5f), b2 = new Vector3(0.5f, 1, 0);
            // Clockwise (Unity front-face) winding: every normal points outward.
            b.Tri(a0, a1, a2);
            b.Tri(b0, b2, b1);
            b.Quad(a2, b2, b0, a0);
            b.Quad(b2, a2, a1, b1);
            b.Quad(b0, b1, a1, a0);
            return b.Build("gable");
        });

        /// <summary>Jittered icosphere (rocks, ore, bushes). Radius ≈0.5.</summary>
        public static Mesh Rock(int variant, float jitter = 0.18f, float squash = 0.75f) => Cached($"rock{variant}:{jitter}:{squash}", () =>
        {
            var rnd = new System.Random(1234 + variant * 977);
            List<Vector3> verts;
            List<int> tris;
            Icosphere(1, out verts, out tris);
            for (int i = 0; i < verts.Count; i++)
            {
                float j = 1f + ((float)rnd.NextDouble() * 2f - 1f) * jitter;
                Vector3 v = verts[i] * 0.5f * j;
                v.y *= squash;
                verts[i] = v;
            }
            var b = new FlatBuilder();
            for (int t = 0; t < tris.Count; t += 3) b.Tri(verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]]);
            Mesh m = b.Build("rock");
            return m;
        });

        /// <summary>Pine crown: three stacked cones in a single mesh, height 1, base radius 0.5.</summary>
        public static Mesh PineCrown(int variant) => Cached($"pine{variant}", () =>
        {
            var rnd = new System.Random(77 + variant * 31);
            var b = new FlatBuilder();
            float y = 0f;
            for (int i = 0; i < 3; i++)
            {
                float r = 0.5f - i * 0.12f + (float)rnd.NextDouble() * 0.04f;
                float h = 0.45f - i * 0.05f;
                AddCone(b, new Vector3(0, y, 0), r, h, 7, (float)rnd.NextDouble() * 50f);
                y += h * 0.62f;
            }
            return b.Build("pine");
        });

        public static Mesh Blob(int variant) => Rock(100 + variant, 0.22f, 0.9f);

        private static void AddCone(FlatBuilder b, Vector3 baseCenter, float radius, float height, int sides, float rotDeg)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (rotDeg + i * 360f / sides) * Mathf.Deg2Rad;
                float a1 = (rotDeg + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                Vector3 p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                b.Tri(p0, apex, p1);
                b.Tri(p1, baseCenter, p0);
            }
        }

        private static Mesh Lathe(Vector2[] profile, int sides, bool capBottom, bool capTop)
        {
            var b = new FlatBuilder();
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                for (int p = 0; p + 1 < profile.Length; p++)
                {
                    Vector3 v00 = new Vector3(Mathf.Cos(a0) * profile[p].x, profile[p].y, Mathf.Sin(a0) * profile[p].x);
                    Vector3 v01 = new Vector3(Mathf.Cos(a1) * profile[p].x, profile[p].y, Mathf.Sin(a1) * profile[p].x);
                    Vector3 v10 = new Vector3(Mathf.Cos(a0) * profile[p + 1].x, profile[p + 1].y, Mathf.Sin(a0) * profile[p + 1].x);
                    Vector3 v11 = new Vector3(Mathf.Cos(a1) * profile[p + 1].x, profile[p + 1].y, Mathf.Sin(a1) * profile[p + 1].x);
                    if (profile[p + 1].x < 1e-4f) b.Tri(v00, v10, v01);
                    else b.Quad(v00, v10, v11, v01);
                }
                if (capBottom && profile[0].x > 0)
                {
                    Vector3 c = new Vector3(0, profile[0].y, 0);
                    b.Tri(c, new Vector3(Mathf.Cos(a0) * profile[0].x, profile[0].y, Mathf.Sin(a0) * profile[0].x), new Vector3(Mathf.Cos(a1) * profile[0].x, profile[0].y, Mathf.Sin(a1) * profile[0].x));
                }
                int last = profile.Length - 1;
                if (capTop && profile[last].x > 0)
                {
                    Vector3 c = new Vector3(0, profile[last].y, 0);
                    b.Tri(c, new Vector3(Mathf.Cos(a1) * profile[last].x, profile[last].y, Mathf.Sin(a1) * profile[last].x), new Vector3(Mathf.Cos(a0) * profile[last].x, profile[last].y, Mathf.Sin(a0) * profile[last].x));
                }
            }
            return b.Build("lathe");
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
        }

        private static int Mid(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }

        private static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out Mesh m) && m != null) return m;
            m = build();
            Cache[key] = m;
            return m;
        }

        /// <summary>Accumulates flat-shaded triangles (unique vertices per face).</summary>
        private sealed class FlatBuilder
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<int> _t = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                _v.Add(a);
                _v.Add(b);
                _v.Add(c);
                _n.Add(n);
                _n.Add(n);
                _n.Add(n);
                _t.Add(i);
                _t.Add(i + 1);
                _t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
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

        /// <summary>A box part whose centre is at <paramref name="center"/>, with an optional box collider.</summary>
        public static GameObject Box(Transform parent, string name, Material material, Vector3 center, Vector3 size, bool collider = true, Quaternion? rot = null)
        {
            GameObject go = Part(parent, name, MeshFactory.Cube, material, center, size, rot);
            if (collider) go.AddComponent<BoxCollider>();
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
