using System;
using System.Collections.Generic;
using Afterfall.Domain.World;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedural point-of-interest architecture and world props. Buildings are walk-in: walls are
    /// separate box colliders with real door and window openings, roofs and ceilings are excluded
    /// from the walkable NavMesh, and all decoration (trims, glass, porches, furniture) is render-only
    /// so it never changes pathing. Visuals are merged into a few meshes per material and cached per
    /// kind, size and variant. Convention: local origin at ground centre, entrance on the +Z face,
    /// floor top at y = 0.1.
    /// </summary>
    public static class PropBuilder
    {
        public static GameObject Build(PropPlacement p)
        {
            var go = new GameObject("Prop_" + p.Kind);
            go.transform.position = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            go.transform.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
            Transform t = go.transform;
            Vector3 s = new Vector3(p.Size.X, p.Size.Y, p.Size.Z);
            // Props without a variant (fences, cliffs, poles) vary with their place in the world.
            int place = Mathf.Abs(Mathf.RoundToInt(p.Position.X * 7.31f) * 31 + Mathf.RoundToInt(p.Position.Z * 3.17f));
            switch (p.Kind)
            {
                case "house": House(t, s, p.Variant); break;
                case "clinic": Clinic(t, s); break;
                case "shop": Shop(t, s); break;
                case "garage": Garage(t, s); break;
                case "warehouse": Warehouse(t, s); break;
                case "shack": Shack(t, s, p.Variant); break;
                case "barn": Barn(t, s); break;
                case "tent": Tent(t, s); break;
                case "firepit": Firepit(t); break;
                case "log_seat": LogSeat(t); break;
                case "fence": Fence(t, s, place % 97); break;
                case "sign": Sign(t, s); break;
                case "canopy": Canopy(t, s); break;
                case "transformer": Transformer(t, s); break;
                case "pylon": Pylon(t, s); break;
                case "cliff": Cliff(t, s, p.Variant * 13 + place % 7); break;
                case "silo": Silo(t, s); break;
                case "power_pole_old": OldPole(t, s, place % 4); break;
                default:
                    Shapes.BevelBox(t, "Block", Mat(Surface.Concrete, new Color(0.55f, 0.54f, 0.52f)), new Vector3(0, s.y * 0.5f, 0), s, 0.04f);
                    break;
            }
            return go;
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Prop materials map their texture in the prop's own frame, so a building looks the same at any yaw.</summary>
        private static Material Mat(Surface surface, Color tint, float? smoothness = null, float? metallic = null, bool doubleSided = false) =>
            MaterialLibrary.Surface(surface, tint, smoothness, metallic, true, 1f, 0f, null, doubleSided);

        private static Material Glass => Mat(Surface.Glass, new Color(0.17f, 0.21f, 0.23f), 0.9f);
        private static Material Steel => Mat(Surface.Metal, new Color(0.56f, 0.58f, 0.58f), 0.35f, 0.65f);
        private static Material DarkMetal => Mat(Surface.Metal, new Color(0.14f, 0.14f, 0.15f), 0.3f, 0.5f);
        private static Material Concrete => Mat(Surface.Concrete, new Color(0.55f, 0.54f, 0.51f));
        private static Material OldWood => Mat(Surface.Wood, new Color(0.47f, 0.42f, 0.35f));
        private static Material Rubber => Mat(Surface.Rubber, new Color(0.1f, 0.1f, 0.1f));
        private static Material WhitePaint => Mat(Surface.PaintedMetal, new Color(0.8f, 0.79f, 0.75f));
        private static Material RedPaint => Mat(Surface.PaintedMetal, new Color(0.62f, 0.12f, 0.09f));
        private static Material YellowPaint => Mat(Surface.PaintedMetal, new Color(0.78f, 0.6f, 0.12f));

        private static Color Tone(float k, float ao = 1f) => new Color(k, k, k, ao);

        /// <summary>A small deterministic per-part brightness variation around 1.</summary>
        private static Color Vary(int seed, int i, float amount = 0.12f, float ao = 1f) => Tone(1f - amount + PropGeo.Hash(seed, i) * amount, ao);

        // ------------------------------------------------------------------ colliders

        private static GameObject ColliderBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static void CapsuleCollider(Transform parent, string name, Vector3 baseCenter, float height, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = baseCenter;
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.center = new Vector3(0f, height * 0.5f, 0f);
            c.height = height;
            c.radius = radius;
        }

        // ================================================================== buildings

        private enum Fill { Glass, Broken, Boarded, Empty }
        private enum DoorKind { French, Glass, Plank, RollUp, Barn }
        private enum WindowKind { Residential, Storefront, Industrial, Rustic }
        private enum RoofKind { Flat, Gable, Gambrel, Mono }

        /// <summary>A door or window opening in one wall: 0 front (+Z), 1 back, 2 left (−X), 3 right (+X).</summary>
        private sealed class Opening
        {
            public readonly int Wall;
            public readonly float Center, Width, Bottom, Top;
            public readonly bool IsDoor;
            public Fill Fill;

            public Opening(int wall, float center, float width, float bottom, float top, bool door, Fill fill)
            {
                Wall = wall;
                Center = center;
                Width = width;
                Bottom = bottom;
                Top = top;
                IsDoor = door;
                Fill = fill;
            }

            public Vector4 Hole => new Vector4(Center - Width * 0.5f, Center + Width * 0.5f, Bottom, Top);
        }

        /// <summary>Everything that defines a building's shell; the same spec drives visuals and colliders.</summary>
        private sealed class Building
        {
            public readonly string Key;
            public readonly Vector3 Size;
            public readonly int Seed;
            public float Th = 0.22f, BaseY = 0.2f, FloorY = 0.1f, BandTop;
            public Material Wall, Band, Inner, Trim, Foundation, Floor, Ceiling, Roof, Sash, Leaf, Metal;
            public RoofKind RoofType = RoofKind.Flat;
            public float Pitch, Pitch2, Break = 0.6f, Eave = 0.4f, Rake = 0.3f, RoofThick = 0.16f, Moss, Parapet;
            /// <summary>Rust blooms on metal cladding, rust on a metal roof, missing shingles.</summary>
            public float Rust, RoofRust, RoofDamage;
            public bool RidgeAlongZ, Trusses, Gutters, CornerBoards = true;
            public WindowKind Windows;
            public DoorKind DoorType;
            public float Casing = 0.1f;
            public readonly List<Opening> Openings = new List<Opening>();

            public Building(string kind, Vector3 size, int variant, int seed)
            {
                Key = $"prop:{kind}:{size.x:0.##}x{size.y:0.##}x{size.z:0.##}:{variant}";
                Size = size;
                Seed = seed;
            }

            public float HalfX => Size.x * 0.5f;
            public float HalfZ => Size.z * 0.5f;
            public float H => Size.y;
            /// <summary>Distance from the ridge to an eave wall.</summary>
            public float SpanHalf => RidgeAlongZ ? HalfX : HalfZ;
            /// <summary>Half length of the ridge, to the gable walls.</summary>
            public float LenHalf => RidgeAlongZ ? HalfZ : HalfX;
            public float ExtTop => RoofType == RoofKind.Flat ? H + 0.2f + Parapet : H;

            public Opening Door(float center, float width, float top)
            {
                var o = new Opening(0, center, width, 0f, top, true, Fill.Empty);
                Openings.Add(o);
                return o;
            }

            public Opening Window(int wall, float center, float width, float bottom, float top, Fill fill)
            {
                var o = new Opening(wall, center, width, bottom, top, false, fill);
                Openings.Add(o);
                return o;
            }

            public Opening MainDoor => Openings.Find(o => o.IsDoor);

            /// <summary>Underside of a gable or gambrel roof as (distance from the ridge, height), from the eave edge to the ridge.</summary>
            public Vector2[] Profile()
            {
                float w = SpanHalf, e = Eave;
                float t1 = Mathf.Tan(Pitch * Mathf.Deg2Rad);
                if (RoofType == RoofKind.Gable) return new[] { new Vector2(w + e, H - e * t1), new Vector2(0f, H + w * t1) };
                if (RoofType == RoofKind.Gambrel)
                {
                    float t2 = Mathf.Tan(Pitch2 * Mathf.Deg2Rad);
                    float wb = w * Break, yb = H + (w - wb) * t1;
                    return new[] { new Vector2(w + e, H - e * t1), new Vector2(w, H), new Vector2(wb, yb), new Vector2(0f, yb + wb * t2) };
                }
                return null;
            }

            public float UnderY(float d)
            {
                Vector2[] p = Profile();
                if (p == null) return H;
                if (d >= p[0].x) return p[0].y;
                for (int i = 0; i + 1 < p.Length; i++)
                    if (d <= p[i].x && d >= p[i + 1].x)
                        return Mathf.Lerp(p[i + 1].y, p[i].y, (d - p[i + 1].x) / Mathf.Max(1e-4f, p[i].x - p[i + 1].x));
                return p[p.Length - 1].y;
            }

            /// <summary>Underside of a lean-to roof rising from the back wall to the front.</summary>
            public float MonoUnderY(float z) => H + (z + HalfZ) * Mathf.Tan(Pitch * Mathf.Deg2Rad);

            public float RidgeTop => RoofType == RoofKind.Mono ? MonoUnderY(HalfZ + Eave) + RoofThick : UnderY(0f) + RoofThick;
        }

        /// <summary>A wall's frame: u runs along <see cref="Right"/> (the axis openings are measured on), outward along <see cref="Out"/>.</summary>
        private struct Frame
        {
            public Vector3 Origin, Right, Out;
            public float Half, InHalf;
            public Quaternion Rot;

            public Vector3 P(float u, float y, float outward = 0f) => Origin + Right * u + Vector3.up * y + Out * outward;
        }

        private static Frame WallFrame(Building b, int wall)
        {
            float hx = b.HalfX, hz = b.HalfZ, th = b.Th;
            Frame f;
            switch (wall)
            {
                case 0: f = new Frame { Origin = new Vector3(0f, 0f, hz), Right = Vector3.right, Out = Vector3.forward, Half = hx, InHalf = hx - th }; break;
                case 1: f = new Frame { Origin = new Vector3(0f, 0f, -hz), Right = Vector3.right, Out = Vector3.back, Half = hx, InHalf = hx - th }; break;
                case 2: f = new Frame { Origin = new Vector3(-hx, 0f, 0f), Right = Vector3.forward, Out = Vector3.left, Half = hz, InHalf = hz - th }; break;
                default: f = new Frame { Origin = new Vector3(hx, 0f, 0f), Right = Vector3.forward, Out = Vector3.right, Half = hz, InHalf = hz - th }; break;
            }
            f.Rot = Quaternion.LookRotation(f.Out, Vector3.up);
            return f;
        }

        private static List<Vector4> Holes(Building b, int wall)
        {
            var list = new List<Vector4>();
            foreach (Opening o in b.Openings)
                if (o.Wall == wall) list.Add(o.Hole);
            return list;
        }

        private static Fill PickFill(int seed, int index, float broken, float boarded)
        {
            float h = PropGeo.Hash(seed, index);
            return h < boarded ? Fill.Boarded : h < boarded + broken ? Fill.Broken : Fill.Glass;
        }

        /// <summary>Spawns the cached visuals of a building plus its colliders.</summary>
        private static void Spawn(Transform t, Building b, Action<PropKit> extras)
        {
            List<PropKit.Piece> pieces = PropKit.Cached(b.Key, kit =>
            {
                Shell(kit, b);
                Roof(kit, b);
                foreach (Opening o in b.Openings)
                {
                    Frame f = WallFrame(b, o.Wall);
                    if (o.IsDoor) Door(kit, b, f, o);
                    else Window(kit, b, f, o, b.Openings.IndexOf(o));
                }
                extras?.Invoke(kit);
            });
            PropKit.Spawn(t, pieces);
            Colliders(t, b);
        }

        // ------------------------------------------------------------------ shell

        private static void Shell(PropKit kit, Building b)
        {
            PropShade ext = PropShades.Weathered(b.Seed, 0f, b.ExtTop, 1f, 0.15f, 1f, b.Rust);
            PropShade inner = PropShades.Interior(b.Seed + 1, b.FloorY, b.H);
            float[] grimeRows = { b.BaseY + 0.25f, b.BaseY + 0.55f, b.BaseY + 0.9f, b.BaseY + 1.3f };
            float top = b.ExtTop;
            for (int w = 0; w < 4; w++)
            {
                Frame f = WallFrame(b, w);
                List<Vector4> holes = Holes(b, w);
                MeshBuilder wall = kit[b.Wall];
                if (b.Band != null && b.BandTop > b.BaseY)
                {
                    PropGeo.Panel(kit[b.Band], f.Origin + f.Out * 0.015f, f.Right, Vector3.up, f.Out, -f.Half - 0.015f, f.Half + 0.015f, b.BaseY, b.BandTop, holes, 0.8f, 0.5f, ext, grimeRows);
                    PropGeo.Panel(wall, f.Origin, f.Right, Vector3.up, f.Out, -f.Half, f.Half, b.BandTop, top, holes, 0.8f, 0.6f, ext, grimeRows);
                    kit[b.Band].Color = Tone(0.95f);
                    float capHalf = w < 2 ? f.Half + 0.045f : f.Half - 0.006f;
                    Strip(kit[b.Band], f, -capHalf, capHalf, b.BandTop + 0.02f, 0.05f, 0.05f, 0.02f, holes);
                }
                else
                {
                    PropGeo.Panel(wall, f.Origin, f.Right, Vector3.up, f.Out, -f.Half, f.Half, b.BaseY, top, holes, 0.8f, 0.6f, ext, grimeRows);
                }
                PropGeo.Panel(kit[b.Inner], f.Origin - f.Out * b.Th, f.Right, Vector3.up, -f.Out, -f.InHalf, f.InHalf, b.FloorY, b.H, holes, 1f, 0.8f, inner,
                    new[] { b.FloorY + 0.3f, b.H - 0.3f });
                foreach (Vector4 h in holes) Reveal(kit[b.Trim], f, h, b);
                GableFill(kit, b, f, w, ext, inner);
                if (b.RoofType == RoofKind.Flat)
                {
                    // Parapet: inner face above the roof deck and a metal coping on top.
                    PropGeo.Panel(wall, f.Origin - f.Out * b.Th, f.Right, Vector3.up, -f.Out, -f.InHalf, f.InHalf, b.H + 0.2f, top, null, 1f, 1f, ext);
                    float len = w < 2 ? f.Half + 0.04f : f.InHalf - 0.04f;
                    kit[b.Metal].Color = Tone(0.9f);
                    kit[b.Metal].Box(f.P(0f, top + 0.03f, -b.Th * 0.5f), new Vector3(len * 2f, 0.06f, b.Th + 0.08f), f.Rot);
                }
                else if (FillTop(b, w) == null)
                {
                    Color cap = Tone(0.8f, 0.7f);
                    PropGeo.Quad(wall, f.P(-f.Half, b.H, 0f), f.P(f.Half, b.H, 0f), f.P(f.Half, b.H, -b.Th), f.P(-f.Half, b.H, -b.Th), Vector3.up, cap, cap, cap, cap);
                }
            }

            // Corner boards hide the panel seams.
            if (b.CornerBoards)
            {
                MeshBuilder trim = kit[b.Trim];
                trim.Color = Tone(0.92f);
                float y0 = b.Band != null ? b.BandTop + 0.045f : b.BaseY;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        trim.Box(new Vector3(sx * (b.HalfX - 0.05f), (y0 + top) * 0.5f, sz * (b.HalfZ - 0.05f)), new Vector3(0.2f, top - y0, 0.2f), null, 0.012f);
            }

            // Foundation band around the base, open at the doors, with a step or threshold under each door.
            MeshBuilder fnd = kit[b.Foundation];
            const float footing = -0.9f;
            for (int w = 0; w < 4; w++)
            {
                Frame f = WallFrame(b, w);
                float len = w < 2 ? f.Half + 0.04f : f.Half - 0.26f;
                fnd.Color = Tone(0.88f, 0.9f);
                Strip(fnd, f, -len, len, (b.BaseY + footing) * 0.5f, b.BaseY - footing, 0.3f, -0.11f, Holes(b, w));
            }
            MeshBuilder floor = kit[b.Floor];
            PropGeo.Panel(floor, new Vector3(0f, b.FloorY, 0f), Vector3.right, Vector3.forward, Vector3.up, -b.HalfX + b.Th, b.HalfX - b.Th, -b.HalfZ + b.Th, b.HalfZ - b.Th,
                null, 1.2f, 1.2f, PropShades.Floor(b.Seed + 2, b.HalfX - b.Th, b.HalfZ - b.Th));
            foreach (Opening o in b.Openings)
            {
                if (!o.IsDoor) continue;
                Frame f = WallFrame(b, o.Wall);
                Color c = Tone(0.75f, 0.85f);
                PropGeo.Quad(floor, f.P(o.Center - o.Width * 0.5f, b.FloorY, 0.02f), f.P(o.Center + o.Width * 0.5f, b.FloorY, 0.02f),
                    f.P(o.Center + o.Width * 0.5f, b.FloorY, -b.Th), f.P(o.Center - o.Width * 0.5f, b.FloorY, -b.Th), Vector3.up, c, c, c, c);
            }
            if (b.Ceiling != null)
                PropGeo.Panel(kit[b.Ceiling], new Vector3(0f, b.H - 0.002f, 0f), Vector3.right, Vector3.forward, Vector3.down, -b.HalfX + b.Th, b.HalfX - b.Th, -b.HalfZ + b.Th, b.HalfZ - b.Th,
                    null, 1.5f, 1.5f, PropShades.Interior(b.Seed + 3, b.H - 3f, b.H + 0.6f));
        }

        /// <summary>Lines one opening's cut through the wall.</summary>
        private static void Reveal(MeshBuilder m, Frame f, Vector4 h, Building b)
        {
            float y0 = Mathf.Max(h.z, b.FloorY), y1 = h.w, th = b.Th;
            Color c = Tone(0.85f, 0.8f);
            PropGeo.Quad(m, f.P(h.x, y0, 0f), f.P(h.x, y1, 0f), f.P(h.x, y1, -th), f.P(h.x, y0, -th), f.Right, c, c, c, c);
            PropGeo.Quad(m, f.P(h.y, y0, 0f), f.P(h.y, y1, 0f), f.P(h.y, y1, -th), f.P(h.y, y0, -th), -f.Right, c, c, c, c);
            PropGeo.Quad(m, f.P(h.x, y1, 0f), f.P(h.y, y1, 0f), f.P(h.y, y1, -th), f.P(h.x, y1, -th), Vector3.down, c, c, c, c);
            if (h.z > b.FloorY + 0.01f)
                PropGeo.Quad(m, f.P(h.x, y0, 0f), f.P(h.y, y0, 0f), f.P(h.y, y0, -th), f.P(h.x, y0, -th), Vector3.up, c, c, c, c);
        }

        /// <summary>Boxes along a wall at height <paramref name="yc"/>, interrupted where an opening crosses them.</summary>
        private static void Strip(MeshBuilder m, Frame f, float u0, float u1, float yc, float height, float depth, float outward, List<Vector4> holes, float bevel = 0f)
        {
            var cuts = new List<Vector2>();
            if (holes != null)
                foreach (Vector4 h in holes)
                    if (h.z < yc + height * 0.5f && h.w > yc - height * 0.5f) cuts.Add(new Vector2(h.x, h.y));
            cuts.Sort((a, c) => a.x.CompareTo(c.x));
            float start = u0;
            foreach (Vector2 c in cuts)
            {
                float end = Mathf.Min(c.x, u1);
                if (end > start + 0.01f) m.Box(f.P((start + end) * 0.5f, yc, outward), new Vector3(end - start, height, depth), f.Rot, bevel);
                start = Mathf.Max(start, c.y);
            }
            if (u1 > start + 0.01f) m.Box(f.P((start + u1) * 0.5f, yc, outward), new Vector3(u1 - start, height, depth), f.Rot, bevel);
        }

        /// <summary>The wall height above the eave line at u, for gable ends and lean-to sides (null: nothing above).</summary>
        private static Func<float, float> FillTop(Building b, int wall)
        {
            switch (b.RoofType)
            {
                case RoofKind.Gable:
                case RoofKind.Gambrel:
                    bool gableEnd = b.RidgeAlongZ ? wall < 2 : wall >= 2;
                    if (!gableEnd) return null;
                    return u => b.UnderY(Mathf.Abs(u));
                case RoofKind.Mono:
                    if (wall >= 2) return u => b.MonoUnderY(u);
                    if (wall == 0) return u => b.MonoUnderY(b.HalfZ);
                    return null;
                default:
                    return null;
            }
        }

        private static void GableFill(PropKit kit, Building b, Frame f, int wall, PropShade ext, PropShade inner)
        {
            Func<float, float> top = FillTop(b, wall);
            if (top == null) return;
            var breaks = new List<float> { -f.Half, f.Half };
            Vector2[] prof = b.Profile();
            if (prof != null)
                foreach (Vector2 p in prof)
                    if (p.x < f.Half - 1e-3f)
                    {
                        breaks.Add(p.x);
                        breaks.Add(-p.x);
                    }
            breaks.Sort();
            var us = new List<float> { breaks[0] };
            for (int i = 1; i < breaks.Count; i++)
            {
                float a = us[us.Count - 1], c = breaks[i];
                if (c - a < 1e-3f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt((c - a) / 0.9f));
                for (int k = 1; k <= n; k++) us.Add(a + (c - a) * k / n);
            }
            Columns(kit[b.Wall], f, 0f, f.Out, us, b.H, top, ext);
            if (b.Ceiling == null)
            {
                var ins = new List<float>();
                foreach (float u in us) ins.Add(Mathf.Clamp(u, -f.InHalf, f.InHalf));
                Columns(kit[b.Inner], f, -b.Th, -f.Out, ins, b.H, u => top(u) - 0.02f, inner);
            }
        }

        private static void Columns(MeshBuilder m, Frame f, float outward, Vector3 normal, List<float> us, float y0, Func<float, float> top, PropShade shade)
        {
            for (int i = 0; i + 1 < us.Count; i++)
            {
                float ua = us[i], ub = us[i + 1];
                if (ub - ua < 1e-3f) continue;
                float ta = top(ua), tb = top(ub);
                if (ta <= y0 + 1e-3f && tb <= y0 + 1e-3f) continue;
                int rows = Mathf.Max(1, Mathf.CeilToInt((Mathf.Max(ta, tb) - y0) / 0.7f));
                for (int r = 0; r < rows; r++)
                {
                    float k0 = (float)r / rows, k1 = (float)(r + 1) / rows;
                    Vector3 a = f.P(ua, Mathf.Lerp(y0, ta, k0), outward), c = f.P(ub, Mathf.Lerp(y0, tb, k0), outward);
                    Vector3 d = f.P(ub, Mathf.Lerp(y0, tb, k1), outward), e = f.P(ua, Mathf.Lerp(y0, ta, k1), outward);
                    PropGeo.Quad(m, a, c, d, e, normal, shade(a, normal), shade(c, normal), shade(d, normal), shade(e, normal));
                }
            }
        }

        // ------------------------------------------------------------------ roofs

        /// <summary>Rotation from the canonical roof frame (ridge along X) to the building.</summary>
        private static Quaternion RoofFrame(Building b) => b.RidgeAlongZ ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        private static void Roof(PropKit kit, Building b)
        {
            PropShade top = PropShades.Roof(b.Seed + 5, b.H - 0.3f, b.RidgeTop, b.Moss, b.RoofRust, b.RoofDamage);
            PropShade under = PropShades.Solid(new Color(0.72f, 0.7f, 0.66f, 0.65f));
            if (b.RoofType == RoofKind.Flat)
            {
                PropGeo.Panel(kit[b.Roof], new Vector3(0f, b.H + 0.2f, 0f), Vector3.right, Vector3.forward, Vector3.up, -b.HalfX + b.Th, b.HalfX - b.Th, -b.HalfZ + b.Th, b.HalfZ - b.Th,
                    null, 1.2f, 1.2f, top);
                return;
            }
            float len = b.LenHalf + b.Rake;
            Quaternion frame = RoofFrame(b);
            Vector3[] underLine;
            if (b.RoofType == RoofKind.Mono)
            {
                float zb = -(b.HalfZ + b.Eave), zf = b.HalfZ + b.Eave;
                underLine = new[] { new Vector3(0f, b.MonoUnderY(zb), zb), new Vector3(0f, b.MonoUnderY(zf), zf) };
                Slab(kit.In(b.Roof, "RoofA", Vector3.zero, frame), underLine, len, b.RoofThick, top, under, true, true);
            }
            else
            {
                // Two halves, the front one turned 180° so object-space shingles run the same way on both slopes.
                Vector2[] prof = b.Profile();
                underLine = new Vector3[prof.Length];
                for (int i = 0; i < prof.Length; i++) underLine[i] = new Vector3(0f, prof[i].y, -prof[i].x);
                Slab(kit.In(b.Roof, "RoofA", Vector3.zero, frame), underLine, len, b.RoofThick, top, under, true, false);
                Slab(kit.In(b.Roof, "RoofB", Vector3.zero, frame * Quaternion.Euler(0f, 180f, 0f)), underLine, len, b.RoofThick, top, under, true, false);
            }
            RoofTrim(kit, b, frame, len);
        }

        /// <summary>A roof slab along X following an underside polyline in the YZ plane, with top, underside, end and edge faces.</summary>
        private static void Slab(MeshBuilder m, Vector3[] under, float halfLen, float thick, PropShade top, PropShade below, bool capStart, bool capEnd)
        {
            Vector3 up = Vector3.up * thick;
            for (int k = 0; k + 1 < under.Length; k++)
            {
                Vector3 u0 = under[k], u1 = under[k + 1];
                Vector3 dir = (u1 - u0).normalized;
                float segLen = (u1 - u0).magnitude;
                Vector3 n = Vector3.Cross(dir, Vector3.right);
                if (n.y < 0f) n = -n;
                PropGeo.Panel(m, u0 + up + Vector3.left * halfLen, Vector3.right, dir, n, 0f, halfLen * 2f, 0f, segLen, null, 1.1f, 0.8f, top);
                PropGeo.Panel(m, u0 + Vector3.left * halfLen, Vector3.right, dir, -n, 0f, halfLen * 2f, 0f, segLen, null, 2.5f, 2.5f, below);
                Vector3 mid = (u0 + u1) * 0.5f + up * 0.5f;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Vector3 x = Vector3.right * (sx * halfLen);
                    PropGeo.Poly(m, mid, below, u0 + x, u1 + x, u1 + up + x, u0 + up + x);
                }
            }
            if (capStart) EdgeCap(m, under[0], under[1], up, halfLen, below);
            if (capEnd) EdgeCap(m, under[under.Length - 1], under[under.Length - 2], up, halfLen, below);
        }

        private static void EdgeCap(MeshBuilder m, Vector3 edge, Vector3 toward, Vector3 up, float halfLen, PropShade shade)
        {
            Vector3 x = Vector3.right * halfLen;
            PropGeo.Poly(m, toward, shade, edge - x, edge + x, edge + up + x, edge - x + up);
        }

        /// <summary>Fascia, rake boards, ridge cap, gutters and (open interiors) trusses, all in the canonical roof frame.</summary>
        private static void RoofTrim(PropKit kit, Building b, Quaternion frame, float len)
        {
            MeshBuilder trim = kit[b.Trim];
            MeshBuilder roof = kit[b.Roof];
            MeshBuilder metal = kit[b.Metal];
            Matrix4x4 m = Matrix4x4.Rotate(frame);
            trim.Matrix = m;
            roof.Matrix = m;
            metal.Matrix = m;
            trim.Color = Tone(0.92f);
            float t = b.RoofThick;
            if (b.RoofType == RoofKind.Mono)
            {
                float zb = -(b.HalfZ + b.Eave), zf = b.HalfZ + b.Eave;
                float yb = b.MonoUnderY(zb), yf = b.MonoUnderY(zf);
                trim.Box(new Vector3(0f, yb + t * 0.5f - 0.03f, zb - 0.013f), new Vector3(len * 2f + 0.05f, t + 0.1f, 0.026f));
                trim.Box(new Vector3(0f, yf + t * 0.5f - 0.03f, zf + 0.013f), new Vector3(len * 2f + 0.05f, t + 0.1f, 0.026f));
                for (int sx = -1; sx <= 1; sx += 2)
                    PropGeo.Beam(trim, new Vector3(sx * (len + 0.013f), yb + t * 0.5f - 0.03f, zb), new Vector3(sx * (len + 0.013f), yf + t * 0.5f - 0.03f, zf), 0.026f, t + 0.1f, Vector3.up);
                // Purlins under the sheets.
                MeshBuilder wood = kit[b.Trim];
                for (int i = 0; i < 4; i++)
                {
                    float z = Mathf.Lerp(-b.HalfZ + 0.4f, b.HalfZ - 0.4f, i / 3f);
                    wood.Box(new Vector3(0f, b.MonoUnderY(z) - 0.05f, z), new Vector3(b.LenHalf * 2f - 0.1f, 0.1f, 0.08f));
                }
            }
            else
            {
                Vector2[] prof = b.Profile();
                Vector2 e = prof[0];
                for (int s = -1; s <= 1; s += 2)
                {
                    trim.Box(new Vector3(0f, e.y + t * 0.5f - 0.03f, s * (e.x + 0.013f)), new Vector3(len * 2f + 0.05f, t + 0.1f, 0.026f));
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int k = 0; k + 1 < prof.Length; k++)
                        {
                            Vector3 a = new Vector3(sx * (len + 0.013f), prof[k].y + t * 0.5f - 0.02f, s * prof[k].x);
                            Vector3 c = new Vector3(sx * (len + 0.013f), prof[k + 1].y + t * 0.5f - 0.02f, s * prof[k + 1].x);
                            PropGeo.Beam(trim, a, c, 0.026f, t + 0.1f, Vector3.up);
                        }
                    }
                }
                // Ridge cap.
                roof.Color = Tone(0.85f);
                float ridge = prof[prof.Length - 1].y + t;
                roof.Box(new Vector3(0f, ridge + 0.02f, 0f), new Vector3(len * 2f + 0.03f, 0.07f, 0.34f), null, 0.025f);
                if (b.Gutters)
                {
                    metal.Color = Tone(0.9f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float gz = s * (e.x + 0.085f), gy = e.y - 0.02f;
                        metal.Box(new Vector3(0f, gy, gz), new Vector3(b.LenHalf * 2f + 0.1f, 0.11f, 0.13f), null, 0.02f);
                        // Downspouts at both ends of the front, one at the back.
                        for (int sx = -1; sx <= 1; sx += 2)
                        {
                            if (s < 0 && sx < 0) continue;
                            float x = sx * (b.LenHalf - 0.16f);
                            float wz = s * (b.SpanHalf + 0.06f);
                            PropGeo.Beam(metal, new Vector3(x, gy - 0.03f, gz), new Vector3(x, gy - 0.4f, wz), 0.07f, 0.07f, Vector3.right);
                            PropGeo.Beam(metal, new Vector3(x, gy - 0.4f, wz), new Vector3(x, 0.35f, wz), 0.07f, 0.07f, Vector3.right);
                            PropGeo.Beam(metal, new Vector3(x, 0.35f, wz), new Vector3(x, 0.08f, wz + s * 0.3f), 0.07f, 0.07f, Vector3.right);
                        }
                    }
                }
                if (b.Trusses)
                {
                    float w = b.SpanHalf - b.Th;
                    int n = Mathf.Max(2, Mathf.RoundToInt(b.LenHalf * 2f / 3.5f));
                    for (int i = 0; i < n; i++)
                    {
                        float x = Mathf.Lerp(-b.LenHalf + 1.6f, b.LenHalf - 1.6f, n == 1 ? 0.5f : (float)i / (n - 1));
                        float yr = b.UnderY(0f) - 0.12f;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            PropGeo.Beam(metal, new Vector3(x, b.UnderY(w) - 0.12f, s * w), new Vector3(x, yr, 0f), 0.1f, 0.22f, Vector3.up);
                            PropGeo.Beam(metal, new Vector3(x, b.H - 0.2f, s * w * 0.5f), new Vector3(x, b.UnderY(w * 0.25f) - 0.15f, s * w * 0.25f), 0.06f, 0.08f, Vector3.right);
                        }
                        PropGeo.Beam(metal, new Vector3(x, b.H - 0.2f, -w), new Vector3(x, b.H - 0.2f, w), 0.1f, 0.16f, Vector3.right);
                        PropGeo.Beam(metal, new Vector3(x, b.H - 0.2f, 0f), new Vector3(x, yr, 0f), 0.08f, 0.08f, Vector3.right);
                    }
                    for (int j = 0; j < 3; j++)
                    {
                        float d = w * (0.15f + j * 0.33f);
                        for (int s = -1; s <= 1; s += 2)
                            metal.Box(new Vector3(0f, b.UnderY(d) - 0.07f, s * d), new Vector3(b.LenHalf * 2f - b.Th * 2f, 0.12f, 0.08f));
                    }
                }
            }
            trim.Matrix = Matrix4x4.identity;
            roof.Matrix = Matrix4x4.identity;
            metal.Matrix = Matrix4x4.identity;
        }

        // ------------------------------------------------------------------ windows and doors

        private static void Casing(MeshBuilder m, Frame f, float u0, float u1, float y0, float y1, float cw, float depth, bool sill, bool dripCap)
        {
            float o = depth * 0.5f;
            m.Box(f.P(u0 - cw * 0.5f, (y0 + y1 + cw) * 0.5f, o), new Vector3(cw, y1 - y0 + cw, depth), f.Rot);
            m.Box(f.P(u1 + cw * 0.5f, (y0 + y1 + cw) * 0.5f, o), new Vector3(cw, y1 - y0 + cw, depth), f.Rot);
            m.Box(f.P((u0 + u1) * 0.5f, y1 + cw * 0.5f, o), new Vector3(u1 - u0, cw, depth), f.Rot);
            if (dripCap) m.Box(f.P((u0 + u1) * 0.5f, y1 + cw + 0.02f, o + 0.012f), new Vector3(u1 - u0 + cw * 2f + 0.06f, 0.04f, depth + 0.03f), f.Rot, 0.008f);
            if (sill)
            {
                m.Box(f.P((u0 + u1) * 0.5f, y0 - 0.025f, 0.035f), new Vector3(u1 - u0 + cw * 2f + 0.1f, 0.05f, 0.14f), f.Rot, 0.01f);
                m.Box(f.P((u0 + u1) * 0.5f, y0 - 0.11f, o), new Vector3(u1 - u0 + cw * 2f - 0.04f, 0.12f, depth), f.Rot);
            }
        }

        /// <summary>A pane of glass (outward-facing only, so the room looks out through it) or what is left of it.</summary>
        private static void Pane(MeshBuilder glass, Frame f, float ua, float ub, float ya, float yb, float outward, Fill fill, int seed)
        {
            if (fill == Fill.Empty) return;
            float k = 0.8f + PropGeo.Hash(seed, 1) * 0.35f;
            Color c = new Color(k, k, k * 1.02f, 1f);
            if (fill == Fill.Glass)
            {
                PropGeo.Quad(glass, f.P(ua, ya, outward), f.P(ub, ya, outward), f.P(ub, yb, outward), f.P(ua, yb, outward), f.Out, c, c, c, c);
                return;
            }
            // Broken: jagged shards left in the corners.
            float w = ub - ua, h = yb - ya;
            float a = 0.25f + PropGeo.Hash(seed, 2) * 0.4f, bb = 0.3f + PropGeo.Hash(seed, 3) * 0.5f;
            PropGeo.Poly(glass, f.P((ua + ub) * 0.5f, (ya + yb) * 0.5f, outward - 1f), null, f.P(ua, yb, outward), f.P(ua + w * a, yb, outward), f.P(ua + w * a * 0.3f, yb - h * 0.25f, outward), f.P(ua, yb - h * bb, outward));
            if (PropGeo.Hash(seed, 4) < 0.7f)
                PropGeo.Poly(glass, f.P((ua + ub) * 0.5f, (ya + yb) * 0.5f, outward - 1f), null, f.P(ub, ya, outward), f.P(ub, ya + h * (0.2f + a * 0.4f), outward), f.P(ub - w * 0.2f, ya + h * 0.12f, outward), f.P(ub - w * (0.3f + bb * 0.3f), ya, outward));
        }

        private static void Window(PropKit kit, Building b, Frame f, Opening o, int index)
        {
            int seed = b.Seed * 31 + index * 7;
            float u0 = o.Center - o.Width * 0.5f, u1 = o.Center + o.Width * 0.5f, y0 = o.Bottom, y1 = o.Top;
            MeshBuilder trim = kit[b.Trim], sash = kit[b.Sash];
            MeshBuilder glass = kit.In(Glass, "Glass", null, null, false);
            trim.Color = Tone(0.93f);
            sash.Color = Tone(0.9f);
            int cols, rows;
            float bar, rec;
            switch (b.Windows)
            {
                case WindowKind.Residential:
                    Casing(trim, f, u0, u1, y0, y1, b.Casing, 0.035f, true, true);
                    cols = 2;
                    rows = 2;
                    bar = 0.045f;
                    rec = -0.07f;
                    break;
                case WindowKind.Storefront:
                    Casing(trim, f, u0, u1, y0, y1, b.Casing, 0.05f, false, false);
                    trim.Box(f.P((u0 + u1) * 0.5f, y0 - 0.02f, 0.04f), new Vector3(u1 - u0 + b.Casing * 2f + 0.04f, 0.04f, 0.12f), f.Rot);
                    cols = Mathf.Max(1, Mathf.CeilToInt((u1 - u0) / 1.4f));
                    rows = y1 - y0 > 1.4f ? 2 : 1;
                    bar = 0.06f;
                    rec = -0.05f;
                    break;
                case WindowKind.Industrial:
                    Casing(trim, f, u0, u1, y0, y1, b.Casing, 0.05f, false, false);
                    kit[b.Foundation].Color = Tone(0.9f);
                    kit[b.Foundation].Box(f.P((u0 + u1) * 0.5f, y0 - 0.04f, 0.04f), new Vector3(u1 - u0 + b.Casing * 2f + 0.08f, 0.08f, 0.16f), f.Rot, 0.01f);
                    cols = Mathf.Max(2, Mathf.RoundToInt((u1 - u0) / 0.55f));
                    rows = Mathf.Max(2, Mathf.RoundToInt((y1 - y0) / 0.45f));
                    bar = 0.035f;
                    rec = -0.08f;
                    break;
                default:
                    Casing(trim, f, u0, u1, y0, y1, b.Casing, 0.03f, true, false);
                    cols = 2;
                    rows = 2;
                    bar = 0.04f;
                    rec = -0.06f;
                    break;
            }
            if (o.Fill == Fill.Empty) return;

            // Sash frame and muntins.
            float ia = u0 + bar, ib = u1 - bar, ja = y0 + bar, jb = y1 - bar;
            float sd = 0.05f;
            sash.Box(f.P(u0 + bar * 0.5f, (y0 + y1) * 0.5f, rec), new Vector3(bar, y1 - y0, sd), f.Rot);
            sash.Box(f.P(u1 - bar * 0.5f, (y0 + y1) * 0.5f, rec), new Vector3(bar, y1 - y0, sd), f.Rot);
            sash.Box(f.P((u0 + u1) * 0.5f, y0 + bar * 0.5f, rec), new Vector3(ib - ia, bar, sd), f.Rot);
            sash.Box(f.P((u0 + u1) * 0.5f, y1 - bar * 0.5f, rec), new Vector3(ib - ia, bar, sd), f.Rot);
            float pw = (ib - ia - (cols - 1) * bar) / cols, ph = (jb - ja - (rows - 1) * bar) / rows;
            for (int c = 1; c < cols; c++) sash.Box(f.P(ia + c * (pw + bar) - bar * 0.5f, (ja + jb) * 0.5f, rec), new Vector3(bar, jb - ja, sd * 0.8f), f.Rot);
            for (int r = 1; r < rows; r++) sash.Box(f.P((ia + ib) * 0.5f, ja + r * (ph + bar) - bar * 0.5f, rec), new Vector3(ib - ia, bar * (b.Windows == WindowKind.Residential ? 1.3f : 1f), sd * 0.8f), f.Rot);
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    Fill fill = o.Fill == Fill.Boarded ? Fill.Broken : o.Fill;
                    if (b.Windows == WindowKind.Industrial && fill == Fill.Glass)
                    {
                        float h = PropGeo.Hash(seed + c * 5 + r * 11, 9);
                        fill = h < 0.18f ? Fill.Broken : h < 0.28f ? Fill.Empty : Fill.Glass;
                    }
                    float pa = ia + c * (pw + bar), pb = ja + r * (ph + bar);
                    Pane(glass, f, pa, pa + pw, pb, pb + ph, rec - 0.01f, fill, seed + c * 3 + r * 13);
                }
            }
            if (o.Fill == Fill.Boarded) Boards(kit, f, u0 - b.Casing, u1 + b.Casing, y0, y1, seed);
            if (b.Windows == WindowKind.Residential && b.Leaf != null && o.Wall != 1) Shutters(kit, b, f, u0 - b.Casing, u1 + b.Casing, y0, y1, seed);
        }

        private static void Boards(PropKit kit, Frame f, float u0, float u1, float y0, float y1, int seed)
        {
            MeshBuilder wood = kit[OldWood];
            int n = 3;
            for (int i = 0; i < n; i++)
            {
                float y = Mathf.Lerp(y0 + 0.18f, y1 - 0.15f, (float)i / (n - 1)) + (PropGeo.Hash(seed, 20 + i) - 0.5f) * 0.1f;
                float tilt = (PropGeo.Hash(seed, 30 + i) - 0.5f) * 14f;
                wood.Color = Vary(seed, 40 + i, 0.25f);
                wood.Box(f.P((u0 + u1) * 0.5f + (PropGeo.Hash(seed, 50 + i) - 0.5f) * 0.1f, y, 0.06f + i * 0.004f), new Vector3(u1 - u0 + 0.14f, 0.17f, 0.024f),
                    f.Rot * Quaternion.Euler(0f, 0f, tilt), 0.006f);
            }
        }

        private static void Shutters(PropKit kit, Building b, Frame f, float u0, float u1, float y0, float y1, int seed)
        {
            MeshBuilder m = kit[b.Leaf];
            float w = Mathf.Min(0.45f, (u1 - u0) * 0.4f), h = y1 - y0 + 0.12f;
            for (int s = 0; s < 2; s++)
            {
                if (PropGeo.Hash(seed, 60 + s) < 0.15f) continue; // a missing shutter
                float u = s == 0 ? u0 - w * 0.5f - 0.02f : u1 + w * 0.5f + 0.02f;
                bool askew = PropGeo.Hash(seed, 70 + s) < 0.2f;
                Quaternion rot = f.Rot * Quaternion.Euler(0f, 0f, askew ? (s == 0 ? -9f : 9f) : 0f);
                float yc = (y0 + y1) * 0.5f - (askew ? 0.12f : 0f);
                Vector3 c = f.P(u, yc, 0.03f);
                m.Color = Vary(seed, 80 + s, 0.15f);
                m.Box(c, new Vector3(w, h, 0.03f), rot, 0.008f);
                // Louvres.
                Vector3 up = rot * Vector3.up, outv = rot * Vector3.forward;
                int slats = Mathf.Max(4, Mathf.RoundToInt(h / 0.11f));
                for (int i = 0; i < slats; i++)
                {
                    float y = -h * 0.5f + 0.1f + i * (h - 0.2f) / (slats - 1);
                    m.Box(c + up * y + outv * 0.02f, new Vector3(w - 0.08f, 0.03f, 0.015f), rot * Quaternion.Euler(-25f, 0f, 0f));
                }
            }
        }

        private static void Door(PropKit kit, Building b, Frame f, Opening o)
        {
            int seed = b.Seed * 17 + 5;
            float u0 = o.Center - o.Width * 0.5f, u1 = o.Center + o.Width * 0.5f;
            switch (b.DoorType)
            {
                case DoorKind.RollUp: RollUpDoor(kit, b, f, o, seed); return;
                case DoorKind.Barn: BarnDoors(kit, b, f, o); return;
            }
            MeshBuilder trim = kit[b.Trim];
            trim.Color = Tone(0.93f);
            Casing(trim, f, u0, u1, b.FloorY, o.Top, b.Casing, 0.04f, false, b.Windows == WindowKind.Residential);
            float lw = o.Width * 0.5f - 0.015f, lh = o.Top - b.FloorY - 0.02f;
            float angL = 96f + PropGeo.Hash(seed, 1) * 18f, angR = 104f + PropGeo.Hash(seed, 2) * 22f;
            Vector3 dirL = f.Right * Mathf.Cos(angL * Mathf.Deg2Rad) - f.Out * Mathf.Sin(angL * Mathf.Deg2Rad);
            Vector3 dirR = -f.Right * Mathf.Cos(angR * Mathf.Deg2Rad) - f.Out * Mathf.Sin(angR * Mathf.Deg2Rad);
            Vector3 hingeL = f.P(u0 + 0.03f, b.FloorY + 0.012f, -b.Th - 0.03f), hingeR = f.P(u1 - 0.03f, b.FloorY + 0.012f, -b.Th - 0.03f);
            Leaf(kit, b, hingeL, dirL, lw, lh);
            if (b.DoorType == DoorKind.Plank)
            {
                // The other leaf came off its hinges and leans against the front wall.
                MeshBuilder m = kit[b.Leaf];
                m.Color = Tone(0.85f);
                Quaternion lean = f.Rot * Quaternion.Euler(-9f, 0f, 3f);
                Vector3 c = f.P(u1 + 0.35f + lw * 0.5f, lh * 0.5f + 0.02f, 0.05f + lh * 0.5f * Mathf.Sin(9f * Mathf.Deg2Rad) + 0.03f);
                m.Box(c, new Vector3(lw, lh, 0.045f), lean, 0.008f);
                Vector3 outv = lean * Vector3.forward, up = lean * Vector3.up;
                m.Box(c + up * (lh * 0.3f) + outv * 0.035f, new Vector3(lw - 0.06f, 0.12f, 0.025f), lean);
                m.Box(c - up * (lh * 0.3f) + outv * 0.035f, new Vector3(lw - 0.06f, 0.12f, 0.025f), lean);
            }
            else
            {
                Leaf(kit, b, hingeR, dirR, lw, lh);
            }
        }

        /// <summary>One door leaf hinged at <paramref name="hinge"/> and swung open along <paramref name="dir"/>.</summary>
        private static void Leaf(PropKit kit, Building b, Vector3 hinge, Vector3 dir, float lw, float lh)
        {
            Vector3 nrm = Vector3.Cross(Vector3.up, dir);
            Quaternion rot = Quaternion.LookRotation(nrm, Vector3.up);
            Vector3 P(float a, float y, float n) => hinge + dir * a + Vector3.up * y + nrm * n;
            MeshBuilder body = kit[b.Leaf];
            MeshBuilder hw = kit[DarkMetal];
            MeshBuilder glass = kit.In(Glass, "Glass", null, null, false);
            body.Color = Tone(0.92f);
            hw.Color = Tone(1f);
            switch (b.DoorType)
            {
                case DoorKind.French:
                    body.Box(P(0.05f, lh * 0.5f, 0f), new Vector3(0.1f, lh, 0.045f), rot);
                    body.Box(P(lw - 0.05f, lh * 0.5f, 0f), new Vector3(0.1f, lh, 0.045f), rot);
                    body.Box(P(lw * 0.5f, 0.13f, 0f), new Vector3(lw - 0.2f, 0.26f, 0.045f), rot);
                    body.Box(P(lw * 0.5f, 0.95f, 0f), new Vector3(lw - 0.2f, 0.12f, 0.045f), rot);
                    body.Box(P(lw * 0.5f, lh - 0.06f, 0f), new Vector3(lw - 0.2f, 0.12f, 0.045f), rot);
                    body.Box(P(lw * 0.5f, 0.6f, 0f), new Vector3(lw - 0.2f, 0.64f, 0.026f), rot);
                    body.Box(P(lw * 0.5f, (1.01f + lh - 0.12f) * 0.5f, 0f), new Vector3(0.03f, lh - 1.13f, 0.035f), rot);
                    glass.Box(P(lw * 0.5f, (1.01f + lh - 0.12f) * 0.5f, 0f), new Vector3(lw - 0.2f, lh - 1.13f, 0.01f), rot);
                    hw.Sphere(P(lw - 0.09f, 1.0f, 0.04f), Vector3.one * 0.03f, 1);
                    hw.Sphere(P(lw - 0.09f, 1.0f, -0.04f), Vector3.one * 0.03f, 1);
                    break;
                case DoorKind.Glass:
                    MeshBuilder al = kit[b.Sash];
                    al.Color = Tone(0.95f);
                    al.Box(P(0.045f, lh * 0.5f, 0f), new Vector3(0.09f, lh, 0.045f), rot);
                    al.Box(P(lw - 0.045f, lh * 0.5f, 0f), new Vector3(0.09f, lh, 0.045f), rot);
                    al.Box(P(lw * 0.5f, 0.12f, 0f), new Vector3(lw - 0.18f, 0.24f, 0.045f), rot);
                    al.Box(P(lw * 0.5f, lh - 0.05f, 0f), new Vector3(lw - 0.18f, 0.1f, 0.045f), rot);
                    glass.Box(P(lw * 0.5f, (0.24f + lh - 0.1f) * 0.5f, 0f), new Vector3(lw - 0.18f, lh - 0.34f, 0.01f), rot);
                    al.Box(P(lw * 0.5f, 1.0f, 0.06f), new Vector3(lw - 0.2f, 0.035f, 0.035f), rot);
                    al.Box(P(lw * 0.5f, 1.0f, -0.06f), new Vector3(lw - 0.2f, 0.035f, 0.035f), rot);
                    break;
                default:
                    body.Box(P(lw * 0.5f, lh * 0.5f, 0f), new Vector3(lw, lh, 0.045f), rot, 0.006f);
                    for (int i = 0; i < 2; i++) body.Box(P(lw * 0.5f, i == 0 ? lh * 0.2f : lh * 0.8f, -0.035f), new Vector3(lw - 0.06f, 0.12f, 0.025f), rot);
                    PropGeo.Beam(body, P(0.08f, lh * 0.25f, -0.035f), P(lw - 0.08f, lh * 0.75f, -0.035f), 0.1f, 0.025f, nrm);
                    hw.Box(P(lw - 0.1f, 1.0f, 0.035f), new Vector3(0.03f, 0.18f, 0.03f), rot);
                    break;
            }
        }

        private static void RollUpDoor(PropKit kit, Building b, Frame f, Opening o, int seed)
        {
            float u0 = o.Center - o.Width * 0.5f, u1 = o.Center + o.Width * 0.5f, top = o.Top;
            MeshBuilder frame = kit[b.Metal];
            frame.Color = Tone(0.85f);
            Casing(frame, f, u0, u1, 0f, top, 0.16f, 0.07f, false, false);
            // The curtain hangs part-way down, leaving at least 2.6 m clear.
            float lowered = Mathf.Min(top - 2.6f, 0.7f + PropGeo.Hash(seed, 3) * 0.6f);
            MeshBuilder slats = kit[Mat(PropSurfaces.RollUpDoor, new Color(0.7f, 0.66f, 0.56f))];
            slats.Color = Tone(0.95f);
            slats.Box(f.P(o.Center, top - lowered * 0.5f, -0.09f), new Vector3(o.Width + 0.06f, lowered, 0.035f), f.Rot);
            frame.Box(f.P(o.Center, top - lowered - 0.03f, -0.09f), new Vector3(o.Width + 0.02f, 0.07f, 0.07f), f.Rot);
            kit[DarkMetal].Box(f.P(o.Center - o.Width * 0.3f, top - lowered + 0.12f, -0.05f), new Vector3(0.18f, 0.04f, 0.04f), f.Rot);
            kit[DarkMetal].Box(f.P(o.Center + o.Width * 0.3f, top - lowered + 0.12f, -0.05f), new Vector3(0.18f, 0.04f, 0.04f), f.Rot);
            // Coil hood inside, above the opening.
            frame.Box(f.P(o.Center, top + 0.32f, -b.Th - 0.3f), new Vector3(o.Width + 0.3f, 0.55f, 0.55f), f.Rot, 0.03f);
        }

        private static void BarnDoors(PropKit kit, Building b, Frame f, Opening o)
        {
            float u0 = o.Center - o.Width * 0.5f, u1 = o.Center + o.Width * 0.5f, top = o.Top;
            MeshBuilder trim = kit[b.Trim];
            trim.Color = Tone(0.95f);
            Casing(trim, f, u0, u1, b.FloorY, top, 0.18f, 0.04f, false, false);
            float pw = o.Width * 0.5f + 0.14f, ph = top + 0.1f;
            // Track and hangers.
            kit[DarkMetal].Box(f.P(o.Center, top + 0.36f, 0.13f), new Vector3(o.Width * 2f + 0.8f, 0.07f, 0.05f), f.Rot);
            MeshBuilder panel = kit[b.Wall];
            for (int s = -1; s <= 1; s += 2)
            {
                float uc = s < 0 ? u0 - pw * 0.5f + 0.16f : u1 + pw * 0.5f - 0.16f;
                if (s > 0) uc -= 0.6f; // one door slid only part of the way
                Vector3 c = f.P(uc, 0.06f + ph * 0.5f, 0.1f);
                panel.Color = Tone(0.9f);
                panel.Box(c, new Vector3(pw, ph, 0.06f), f.Rot, 0.01f);
                // White frame and X brace.
                float fo = 0.1f + 0.035f;
                trim.Box(f.P(uc - pw * 0.5f + 0.08f, 0.06f + ph * 0.5f, fo), new Vector3(0.16f, ph, 0.02f), f.Rot);
                trim.Box(f.P(uc + pw * 0.5f - 0.08f, 0.06f + ph * 0.5f, fo), new Vector3(0.16f, ph, 0.02f), f.Rot);
                trim.Box(f.P(uc, 0.06f + 0.08f, fo), new Vector3(pw - 0.32f, 0.16f, 0.02f), f.Rot);
                trim.Box(f.P(uc, 0.06f + ph - 0.08f, fo), new Vector3(pw - 0.32f, 0.16f, 0.02f), f.Rot);
                trim.Box(f.P(uc, 0.06f + ph * 0.5f, fo), new Vector3(pw - 0.32f, 0.14f, 0.02f), f.Rot);
                for (int k = 0; k < 2; k++)
                {
                    float ya = 0.06f + 0.16f + k * (ph * 0.5f - 0.08f), yb = ya + ph * 0.5f - 0.24f;
                    PropGeo.Beam(trim, f.P(uc - pw * 0.5f + 0.16f, ya, fo), f.P(uc + pw * 0.5f - 0.16f, yb, fo), 0.13f, 0.02f, f.Out);
                    PropGeo.Beam(trim, f.P(uc + pw * 0.5f - 0.16f, ya, fo), f.P(uc - pw * 0.5f + 0.16f, yb, fo), 0.13f, 0.02f, f.Out);
                }
                kit[DarkMetal].Box(f.P(uc - pw * 0.3f, top + 0.25f, 0.13f), new Vector3(0.08f, 0.25f, 0.03f), f.Rot);
                kit[DarkMetal].Box(f.P(uc + pw * 0.3f, top + 0.25f, 0.13f), new Vector3(0.08f, 0.25f, 0.03f), f.Rot);
            }
        }

        // ------------------------------------------------------------------ building colliders

        private static void Colliders(Transform t, Building b)
        {
            ColliderBox(t, "Floor", new Vector3(0f, b.FloorY * 0.5f, 0f), new Vector3(b.Size.x, b.FloorY, b.Size.z));
            ColliderBox(t, "Foundation", new Vector3(0f, -0.4f, 0f), new Vector3(b.Size.x + 0.2f, 0.8f, b.Size.z + 0.2f));
            for (int w = 0; w < 4; w++) ColliderWall(t, b, w);
            if (b.RoofType == RoofKind.Flat)
            {
                Shapes.NotWalkable(ColliderBox(t, "Roof", new Vector3(0f, b.H + (0.2f + b.Parapet) * 0.5f, 0f), new Vector3(b.Size.x, 0.2f + b.Parapet, b.Size.z)));
                return;
            }
            var roof = new GameObject("Roof");
            roof.transform.SetParent(t, false);
            roof.AddComponent<MeshCollider>().sharedMesh = RoofCollider(b);
            Shapes.NotWalkable(roof);
            Shapes.NotWalkable(ColliderBox(t, "Ceiling", new Vector3(0f, b.H - 0.05f, 0f), new Vector3(b.Size.x, 0.1f, b.Size.z)));
        }

        /// <summary>Wall boxes around every opening of one wall (the walls stay H tall; gables and parapets are covered by the roof colliders).</summary>
        private static void ColliderWall(Transform t, Building b, int wall)
        {
            Frame f = WallFrame(b, wall);
            float len = wall < 2 ? b.Size.x : b.Size.z - 2f * b.Th;
            Vector3 c = f.Origin - f.Out * (b.Th * 0.5f);
            float h = b.H;
            Vector3 Size(float l, float hh) => wall < 2 ? new Vector3(l, hh, b.Th) : new Vector3(b.Th, hh, l);
            var ops = b.Openings.FindAll(o => o.Wall == wall);
            ops.Sort((x, y) => x.Center.CompareTo(y.Center));
            float start = -len * 0.5f;
            foreach (Opening o in ops)
            {
                float a = o.Center - o.Width * 0.5f, e = o.Center + o.Width * 0.5f;
                if (a > start + 0.01f) ColliderBox(t, "Wall", c + f.Right * ((start + a) * 0.5f) + Vector3.up * (h * 0.5f), Size(a - start, h));
                if (o.Bottom > 0.01f) ColliderBox(t, "WallB", c + f.Right * o.Center + Vector3.up * (o.Bottom * 0.5f), Size(o.Width, o.Bottom));
                if (h - o.Top > 0.01f) ColliderBox(t, "WallT", c + f.Right * o.Center + Vector3.up * ((o.Top + h) * 0.5f), Size(o.Width, h - o.Top));
                start = e;
            }
            if (len * 0.5f > start + 0.01f) ColliderBox(t, "Wall", c + f.Right * ((start + len * 0.5f) * 0.5f) + Vector3.up * (h * 0.5f), Size(len * 0.5f - start, h));
        }

        /// <summary>A closed prism around the roof's top surface (with overhangs), for physics only.</summary>
        private static Mesh RoofCollider(Building b) => MeshFactory.Cached("propcol:" + b.Key, () =>
        {
            var pts = new List<Vector2>();
            float t = b.RoofThick;
            if (b.RoofType == RoofKind.Mono)
            {
                float zb = -(b.HalfZ + b.Eave), zf = b.HalfZ + b.Eave;
                pts.Add(new Vector2(zb, b.MonoUnderY(zb)));
                pts.Add(new Vector2(zb, b.MonoUnderY(zb) + t));
                pts.Add(new Vector2(zf, b.MonoUnderY(zf) + t));
                pts.Add(new Vector2(zf, b.MonoUnderY(zf)));
            }
            else
            {
                Vector2[] prof = b.Profile();
                pts.Add(new Vector2(-prof[0].x, prof[0].y));
                for (int i = 0; i < prof.Length; i++) pts.Add(new Vector2(-prof[i].x, prof[i].y + t));
                for (int i = prof.Length - 2; i >= 0; i--) pts.Add(new Vector2(prof[i].x, prof[i].y + t));
                pts.Add(new Vector2(prof[0].x, prof[0].y));
            }
            var mb = new MeshBuilder { Matrix = Matrix4x4.Rotate(RoofFrame(b)) };
            float x = b.LenHalf + b.Rake;
            Vector2 centroid = Vector2.zero;
            foreach (Vector2 p in pts) centroid += p;
            centroid /= pts.Count;
            Vector3 inside = new Vector3(0f, centroid.y, centroid.x);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i], c = pts[(i + 1) % pts.Count];
                if ((a - c).sqrMagnitude < 1e-6f) continue;
                mb.Face(inside, new Vector3(-x, a.y, a.x), new Vector3(x, a.y, a.x), new Vector3(x, c.y, c.x), new Vector3(-x, c.y, c.x));
            }
            var left = new Vector3[pts.Count];
            var right = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                left[i] = new Vector3(-x, pts[i].y, pts[i].x);
                right[i] = new Vector3(x, pts[i].y, pts[i].x);
            }
            mb.Face(inside, left);
            mb.Face(inside, right);
            return mb.Build("roof collider");
        });

        // ================================================================== building kinds

        private static readonly Color[] HouseSiding =
        {
            new Color(0.78f, 0.73f, 0.6f), new Color(0.55f, 0.62f, 0.54f), new Color(0.68f, 0.49f, 0.43f), new Color(0.5f, 0.57f, 0.64f)
        };
        private static readonly Color[] HouseShingles =
        {
            new Color(0.4f, 0.32f, 0.27f), new Color(0.3f, 0.3f, 0.31f), new Color(0.42f, 0.37f, 0.33f), new Color(0.32f, 0.35f, 0.33f)
        };
        private static readonly Color[] HouseAccent =
        {
            new Color(0.25f, 0.36f, 0.29f), new Color(0.42f, 0.24f, 0.18f), new Color(0.24f, 0.28f, 0.34f), new Color(0.2f, 0.21f, 0.22f)
        };
        private static readonly Color[] Wallpaper =
        {
            new Color(0.74f, 0.68f, 0.56f), new Color(0.62f, 0.66f, 0.6f), new Color(0.72f, 0.62f, 0.58f), new Color(0.64f, 0.66f, 0.7f)
        };

        private static void House(Transform t, Vector3 s, int variant)
        {
            int v = ((variant % 4) + 4) % 4;
            var b = new Building("house", s, v, 101 + v * 17)
            {
                Wall = Mat(Surface.Siding, HouseSiding[v]),
                Inner = Mat(Surface.Plaster, Wallpaper[v]),
                Trim = Mat(Surface.Wood, new Color(0.82f, 0.8f, 0.74f)),
                Foundation = Mat(Surface.Concrete, new Color(0.52f, 0.51f, 0.49f)),
                Floor = Mat(Surface.Planks, new Color(0.47f, 0.37f, 0.28f)),
                Ceiling = Mat(Surface.Plaster, new Color(0.76f, 0.74f, 0.69f)),
                Roof = Mat(Surface.Shingles, HouseShingles[v]),
                Leaf = Mat(Surface.Wood, HouseAccent[v]),
                Metal = Mat(Surface.PaintedMetal, new Color(0.7f, 0.69f, 0.65f)),
                RoofType = RoofKind.Gable,
                Pitch = 30f,
                Eave = 0.45f,
                Rake = 0.32f,
                Moss = 1f,
                RoofDamage = 0.35f + v * 0.15f,
                Gutters = true,
                Windows = WindowKind.Residential,
                DoorType = DoorKind.French
            };
            b.Sash = b.Trim;
            b.Door(1.4f, 1.6f, 2.2f);
            b.Window(0, -2.0f, 1.3f, 0.9f, 2.2f, PickFill(b.Seed, 1, 0.25f, 0.2f));
            b.Window(1, -2.4f, 1.0f, 1.0f, 2.0f, PickFill(b.Seed, 2, 0.3f, 0.2f));
            b.Window(1, 1.9f, 1.0f, 1.0f, 2.0f, PickFill(b.Seed, 3, 0.3f, 0.2f));
            b.Window(2, 0.3f, 1.1f, 0.95f, 2.1f, PickFill(b.Seed, 4, 0.3f, 0.25f));
            b.Window(3, 0.3f, 1.0f, 1.0f, 2.0f, PickFill(b.Seed, 5, 0.3f, 0.25f));
            Spawn(t, b, kit => HouseExtras(kit, b, v));
        }

        private static void HouseExtras(PropKit kit, Building b, int v)
        {
            Frame front = WallFrame(b, 0);
            Opening door = b.MainDoor;
            MeshBuilder trim = kit[b.Trim];
            if (v != 1)
            {
                // Porch: plank deck, skirt, two posts, side railings and a lean-to metal roof.
                float u0 = door.Center - 1.75f, u1 = door.Center + 1.75f, depth = 1.8f, mid = (u0 + u1) * 0.5f;
                MeshBuilder deck = kit[Mat(Surface.Planks, new Color(0.5f, 0.43f, 0.35f))];
                deck.Color = Tone(0.92f);
                deck.Box(front.P(mid, 0.045f, depth * 0.5f + 0.02f), new Vector3(u1 - u0, 0.07f, depth), front.Rot, 0.01f);
                deck.Color = Tone(0.6f, 0.75f);
                deck.Box(front.P(mid, -0.28f, depth * 0.5f), new Vector3(u1 - u0 - 0.1f, 0.6f, depth - 0.1f), front.Rot);
                trim.Color = Tone(0.9f);
                float postZ = depth - 0.1f, roofLow = 2.36f, roofHigh = 2.68f;
                for (int i = 0; i < 2; i++)
                {
                    float u = i == 0 ? u0 + 0.1f : u1 - 0.1f;
                    trim.Box(front.P(u, (0.08f + roofLow) * 0.5f, postZ), new Vector3(0.13f, roofLow - 0.08f, 0.13f), front.Rot, 0.015f);
                    // Side railing back to the wall.
                    trim.Box(front.P(u, 0.92f, postZ * 0.5f), new Vector3(0.06f, 0.06f, postZ), front.Rot);
                    trim.Box(front.P(u, 0.22f, postZ * 0.5f), new Vector3(0.06f, 0.05f, postZ), front.Rot);
                    for (int k = 1; k < 12; k++) trim.Box(front.P(u, 0.57f, postZ * k / 12f), new Vector3(0.035f, 0.65f, 0.035f), front.Rot);
                }
                trim.Box(front.P(mid, roofLow - 0.07f, postZ), new Vector3(u1 - u0 + 0.1f, 0.14f, 0.12f), front.Rot);
                MeshBuilder tin = kit[Mat(Surface.Corrugated, new Color(0.5f, 0.48f, 0.45f))];
                tin.Color = Tone(0.95f);
                PropGeo.Beam(tin, front.P(mid, roofHigh + 0.03f, 0.02f), front.P(mid, roofLow + 0.03f, depth + 0.15f), u1 - u0 + 0.35f, 0.05f, Vector3.up);
                trim.Box(front.P(mid, roofHigh - 0.05f, 0.04f), new Vector3(u1 - u0 + 0.3f, 0.1f, 0.06f), front.Rot);
            }
            else
            {
                // Concrete stoop and a small hood on brackets.
                kit[b.Foundation].Color = Tone(0.9f);
                kit[b.Foundation].Box(front.P(door.Center, -0.36f, 0.5f), new Vector3(door.Width + 0.7f, 0.88f, 1.0f), front.Rot, 0.02f);
                MeshBuilder tin = kit[Mat(Surface.Corrugated, new Color(0.45f, 0.44f, 0.42f))];
                tin.Color = Tone(0.95f);
                PropGeo.Beam(tin, front.P(door.Center, 2.78f, 0.02f), front.P(door.Center, 2.6f, 0.85f), door.Width + 0.55f, 0.05f, Vector3.up);
                trim.Color = Tone(0.9f);
                for (int sx = -1; sx <= 1; sx += 2)
                    PropGeo.Beam(trim, front.P(door.Center + sx * (door.Width * 0.5f + 0.15f), 2.3f, 0.03f), front.P(door.Center + sx * (door.Width * 0.5f + 0.15f), 2.62f, 0.75f), 0.06f, 0.06f, Vector3.up);
            }

            // Brick chimney through the back slope.
            Material brick = Mat(Surface.Brick, new Color(0.9f, 0.86f, 0.84f));
            float cx = -b.HalfX + 1.3f + (v % 2) * 0.5f, cz = -0.95f;
            float chTop = b.RidgeTop + 0.75f;
            kit[brick].Color = Tone(0.95f);
            kit[brick].Box(new Vector3(cx, (b.H + chTop) * 0.5f, cz), new Vector3(0.68f, chTop - b.H, 0.58f));
            kit[b.Foundation].Box(new Vector3(cx, chTop + 0.045f, cz), new Vector3(0.8f, 0.09f, 0.7f), null, 0.015f);
            kit[DarkMetal].Color = Tone(0.9f);
            PropGeo.Rod(kit[DarkMetal], new Vector3(cx + 0.1f, chTop + 0.08f, cz), new Vector3(cx + 0.1f, chTop + 0.32f, cz), 0.08f, 10);

            // Louvred attic vents in both gables.
            for (int w = 2; w <= 3; w++)
            {
                Frame f = WallFrame(b, w);
                float y = b.H + (b.UnderY(0f) - b.H) * 0.45f;
                trim.Color = Tone(0.92f);
                trim.Box(f.P(0f, y, 0.02f), new Vector3(0.72f, 0.5f, 0.04f), f.Rot, 0.01f);
                kit[DarkMetal].Color = Tone(0.6f, 0.6f);
                kit[DarkMetal].Box(f.P(0f, y, 0.035f), new Vector3(0.56f, 0.36f, 0.02f), f.Rot);
                for (int i = 0; i < 4; i++) trim.Box(f.P(0f, y - 0.135f + i * 0.09f, 0.055f), new Vector3(0.56f, 0.035f, 0.03f), f.Rot * Quaternion.Euler(-30f, 0f, 0f));
            }

            // AC unit, meter and mailbox outside; furniture inside.
            Frame right = WallFrame(b, 3);
            AcUnit(kit, right.P(-1.7f, 0f, 0.6f), right.Rot);
            kit[b.Metal].Color = Tone(0.85f);
            kit[b.Metal].Box(right.P(1.9f, 1.45f, 0.08f), new Vector3(0.3f, 0.42f, 0.16f), right.Rot, 0.02f);
            kit[DarkMetal].Color = Tone(0.9f);
            kit[DarkMetal].Box(right.P(1.9f, 0.75f, 0.05f), new Vector3(0.05f, 1.0f, 0.05f), right.Rot);
            MeshBuilder post = kit[OldWood];
            post.Color = Tone(0.85f);
            Vector3 mail = front.P(door.Center + 2.4f, 0f, 3.6f);
            post.Box(mail + Vector3.up * 0.55f, new Vector3(0.09f, 1.1f, 0.09f), front.Rot);
            MeshBuilder box = kit[b.Leaf];
            box.Color = Tone(0.85f);
            box.Box(mail + Vector3.up * 1.18f + front.Out * 0.05f, new Vector3(0.2f, 0.2f, 0.46f), front.Rot * Quaternion.Euler(0f, 0f, 4f), 0.04f);

            HouseInterior(kit, b, v);
        }

        private static void HouseInterior(PropKit kit, Building b, int v)
        {
            float fy = b.FloorY;
            MeshBuilder rug = kit[Mat(Surface.Fabric, new Color(0.45f, 0.22f, 0.18f))];
            rug.Color = Tone(0.9f);
            rug.Box(new Vector3(-1.0f, fy + 0.006f, 0.7f), new Vector3(2.2f, 0.012f, 1.5f), Quaternion.Euler(0f, 7f + v * 3f, 0f));
            MeshBuilder sofa = kit[Mat(Surface.Fabric, new Color(0.42f, 0.4f, 0.33f))];
            float sx = -b.HalfX + b.Th + 0.45f;
            sofa.Color = Tone(0.9f);
            sofa.Box(new Vector3(sx, fy + 0.22f, 0.9f), new Vector3(0.85f, 0.44f, 2.0f), null, 0.05f);
            sofa.Box(new Vector3(sx - 0.33f, fy + 0.55f, 0.9f), new Vector3(0.2f, 0.5f, 2.0f), null, 0.05f);
            for (int i = 0; i < 2; i++) sofa.Box(new Vector3(sx, fy + 0.52f, i == 0 ? -0.02f : 1.82f), new Vector3(0.85f, 0.2f, 0.18f), null, 0.05f);
            for (int i = 0; i < 2; i++) sofa.Box(new Vector3(sx + 0.05f, fy + 0.5f, 0.42f + i * 0.96f), new Vector3(0.62f, 0.12f, 0.9f), null, 0.04f);
            MeshBuilder wood = kit[Mat(Surface.Wood, new Color(0.42f, 0.3f, 0.21f))];
            wood.Color = Tone(0.9f);
            wood.Box(new Vector3(-2.2f, fy + 0.45f, 0.9f), new Vector3(0.55f, 0.04f, 1.0f), null, 0.01f);
            for (int i = 0; i < 4; i++) wood.Box(new Vector3(-2.2f + (i % 2 - 0.5f) * 0.45f, fy + 0.22f, 0.9f + (i / 2 - 0.5f) * 0.9f), new Vector3(0.04f, 0.44f, 0.04f));
            // A picture on the back wall and one fallen against it.
            float wz = -b.HalfZ + b.Th;
            wood.Box(new Vector3(0.35f, 1.65f, wz + 0.02f), new Vector3(0.55f, 0.42f, 0.03f), null, 0.005f);
            Color art = HouseAccent[(v + 1) % 4];
            kit[Mat(Surface.Plaster, new Color(art.r * 1.6f, art.g * 1.6f, art.b * 1.6f))].Box(new Vector3(0.35f, 1.65f, wz + 0.036f), new Vector3(0.45f, 0.32f, 0.004f));
            wood.Box(new Vector3(-0.5f, fy + 0.2f, wz + 0.12f), new Vector3(0.5f, 0.4f, 0.03f), Quaternion.Euler(-18f, 0f, 0f), 0.005f);
            // Ceiling lamp and scattered paper.
            kit[b.Metal].Box(new Vector3(0f, b.H - 0.03f, 0f), new Vector3(0.3f, 0.05f, 0.3f), null, 0.02f);
            kit[Mat(Surface.Glass, new Color(0.8f, 0.78f, 0.7f), 0.6f)].Sphere(new Vector3(0f, b.H - 0.1f, 0f), new Vector3(0.14f, 0.08f, 0.14f), 1);
            MeshBuilder paper = kit[Mat(Surface.Plaster, new Color(0.82f, 0.8f, 0.74f))];
            for (int i = 0; i < 7; i++)
            {
                float x = -2.5f + PropGeo.Hash(b.Seed, 90 + i) * 4.5f, z = -1.5f + PropGeo.Hash(b.Seed, 100 + i) * 3f;
                paper.Color = Vary(b.Seed, 110 + i, 0.2f);
                paper.Box(new Vector3(x, fy + 0.004f + i * 0.0005f, z), new Vector3(0.21f, 0.003f, 0.29f), Quaternion.Euler(0f, PropGeo.Hash(b.Seed, 120 + i) * 180f, 0f));
            }
        }

        private static void AcUnit(PropKit kit, Vector3 pos, Quaternion rot)
        {
            MeshBuilder body = kit[Mat(Surface.PaintedMetal, new Color(0.66f, 0.64f, 0.58f))];
            MeshBuilder dark = kit[DarkMetal];
            MeshBuilder pad = kit[Concrete];
            Vector3 up = Vector3.up;
            pad.Color = Tone(0.85f);
            pad.Box(pos + up * 0.03f, new Vector3(0.95f, 0.12f, 0.95f), rot, 0.02f);
            body.Color = Tone(0.9f);
            body.Box(pos + up * 0.45f, new Vector3(0.75f, 0.72f, 0.75f), rot, 0.03f);
            dark.Color = Tone(0.8f);
            dark.Cylinder(pos + up * 0.81f, pos + up * 0.83f, 0.27f, 0.27f, 16);
            body.Cylinder(pos + up * 0.83f, pos + up * 0.86f, 0.07f, 0.07f, 8);
            for (int i = 0; i < 4; i++)
            {
                Quaternion r = rot * Quaternion.Euler(0f, i * 90f, 0f);
                dark.Box(pos + up * 0.42f + r * (Vector3.forward * 0.38f), new Vector3(0.56f, 0.46f, 0.012f), r);
            }
            Vector3 back = rot * Vector3.back;
            PropGeo.Rod(dark, pos + up * 0.6f + back * 0.37f, pos + up * 0.6f + back * 0.62f, 0.02f);
            PropGeo.Rod(dark, pos + up * 0.5f + back * 0.37f, pos + up * 0.5f + back * 0.62f, 0.015f);
        }

        private static void Clinic(Transform t, Vector3 s)
        {
            var b = new Building("clinic", s, 0, 211)
            {
                Wall = Mat(Surface.Plaster, new Color(0.8f, 0.79f, 0.74f)),
                Band = Mat(Surface.Concrete, new Color(0.5f, 0.51f, 0.5f)),
                BandTop = 0.8f,
                Inner = Mat(Surface.Plaster, new Color(0.7f, 0.76f, 0.73f)),
                Trim = Mat(Surface.Metal, new Color(0.6f, 0.61f, 0.62f), 0.4f, 0.6f),
                Foundation = Mat(Surface.Concrete, new Color(0.5f, 0.5f, 0.48f)),
                Floor = Mat(PropSurfaces.FloorTiles, new Color(0.52f, 0.54f, 0.52f)),
                Ceiling = Mat(PropSurfaces.CeilingTiles, new Color(0.78f, 0.78f, 0.75f)),
                Roof = Mat(Surface.Gravel, new Color(0.45f, 0.44f, 0.42f)),
                Metal = Mat(Surface.PaintedMetal, new Color(0.62f, 0.64f, 0.65f)),
                Parapet = 0.6f,
                Moss = 0.6f,
                Windows = WindowKind.Storefront,
                DoorType = DoorKind.Glass,
                Casing = 0.07f
            };
            b.Sash = b.Trim;
            b.Leaf = b.Trim;
            b.Door(0f, 1.6f, 2.3f);
            b.Window(0, -3.2f, 2.0f, 0.9f, 2.3f, Fill.Glass);
            b.Window(0, 3.2f, 2.0f, 0.9f, 2.3f, Fill.Broken);
            b.Window(1, 1.3f, 1.4f, 1.1f, 2.2f, Fill.Boarded);
            b.Window(2, 0.8f, 1.6f, 0.95f, 2.2f, Fill.Glass);
            b.Window(3, 0.8f, 1.6f, 0.95f, 2.2f, Fill.Broken);
            Spawn(t, b, kit =>
            {
                Frame front = WallFrame(b, 0);
                MeshBuilder white = kit[WhitePaint];
                // Entrance canopy on tie rods.
                white.Color = Tone(0.95f);
                white.Box(front.P(0f, 2.82f, 0.7f), new Vector3(3.2f, 0.14f, 1.4f), front.Rot, 0.02f);
                kit[RedPaint].Color = Tone(1f);
                kit[RedPaint].Box(front.P(0f, 2.82f, 1.405f), new Vector3(3.2f, 0.08f, 0.012f), front.Rot);
                kit[DarkMetal].Color = Tone(0.9f);
                for (int sx = -1; sx <= 1; sx += 2) PropGeo.Rod(kit[DarkMetal], front.P(sx * 1.4f, 2.88f, 1.3f), front.P(sx * 1.4f, 3.5f, 0.02f), 0.015f);
                // Red cross light box on the parapet.
                float cy = b.ExtTop - 0.15f;
                white.Box(front.P(0f, cy, 0.1f), new Vector3(1.3f, 1.3f, 0.2f), front.Rot, 0.04f);
                kit[RedPaint].Box(front.P(0f, cy, 0.205f), new Vector3(0.95f, 0.3f, 0.02f), front.Rot);
                kit[RedPaint].Box(front.P(0f, cy, 0.205f), new Vector3(0.3f, 0.95f, 0.02f), front.Rot);
                // Name plate beside the door.
                MeshBuilder plate = kit[Mat(Surface.PaintedMetal, new Color(0.2f, 0.32f, 0.5f))];
                plate.Color = Tone(1f);
                plate.Box(front.P(-1.45f, 1.7f, 0.02f), new Vector3(0.6f, 0.4f, 0.025f), front.Rot, 0.008f);
                Lettering(white, front.P(-1.45f, 1.76f, 0.035f), Vector3.up, front.Out, 0.07f, 0.46f, 221);
                Lettering(white, front.P(-1.45f, 1.62f, 0.035f), Vector3.up, front.Out, 0.045f, 0.4f, 222);
                Bench(kit, front.P(-3.2f, 0f, 0.65f), front.Rot);
                // Painted accent band, rear door, rain-water pipes and a dumpster out back.
                MeshBuilder teal = kit[Mat(Surface.PaintedMetal, new Color(0.24f, 0.42f, 0.44f))];
                teal.Color = Tone(0.95f);
                for (int w = 0; w < 4; w++)
                {
                    Frame f = WallFrame(b, w);
                    float half = w < 2 ? f.Half + 0.02f : f.Half - 0.01f;
                    Strip(teal, f, -half, half, 2.62f, 0.14f, 0.02f, 0.01f, Holes(b, w));
                }
                Frame back = WallFrame(b, 1);
                PersonnelDoor(kit, back, -3.0f, b);
                Dumpster(kit, back.P(-0.9f, 0f, 1.1f), back.Rot);
                Downspout(kit, b, back, -b.HalfX + 0.35f);
                Downspout(kit, b, back, b.HalfX - 0.35f);
                // Rooftop plant.
                RoofUnit(kit, new Vector3(2.4f, b.H + 0.2f, -1.6f), Quaternion.identity);
                for (int i = 0; i < 3; i++) PropGeo.Rod(kit[DarkMetal], new Vector3(-2.5f + i * 0.7f, b.H + 0.2f, -2.2f), new Vector3(-2.5f + i * 0.7f, b.H + 0.75f + i * 0.1f, -2.2f), 0.05f, 8);
                kit[b.Metal].Color = Tone(0.9f);
                kit[b.Metal].Box(new Vector3(-1.5f, b.H + 0.35f, 1.4f), new Vector3(0.8f, 0.3f, 0.8f), null, 0.02f);
                // Interior: reception counter, gurney, scattered supplies.
                MeshBuilder counter = kit[Mat(Surface.Wood, new Color(0.55f, 0.47f, 0.38f))];
                counter.Color = Tone(0.9f);
                counter.Box(new Vector3(3.3f, b.FloorY + 0.52f, 1.6f), new Vector3(2.2f, 1.04f, 0.6f), null, 0.01f);
                white.Box(new Vector3(3.3f, b.FloorY + 1.06f, 1.55f), new Vector3(2.3f, 0.04f, 0.72f), null, 0.01f);
                Gurney(kit, new Vector3(-b.HalfX + b.Th + 0.45f, b.FloorY, 1.3f));
                MeshBuilder boxes = kit[Mat(Surface.PaintedMetal, new Color(0.82f, 0.8f, 0.76f))];
                for (int i = 0; i < 6; i++)
                {
                    boxes.Color = Vary(211, 140 + i, 0.25f);
                    boxes.Box(new Vector3(-3.2f + PropGeo.Hash(211, 150 + i) * 2.4f, b.FloorY + 0.04f, -1.9f + PropGeo.Hash(211, 160 + i) * 0.8f), new Vector3(0.18f, 0.08f, 0.12f),
                        Quaternion.Euler(0f, PropGeo.Hash(211, 170 + i) * 180f, 0f), 0.01f);
                }
                kit[Steel].Color = Tone(0.9f);
                PropGeo.Rod(kit[Steel], new Vector3(-2.2f, b.FloorY + 0.03f, 0.4f), new Vector3(-1.0f, b.FloorY + 0.03f, -0.4f), 0.015f);
            });
        }

        private static void Shop(Transform t, Vector3 s)
        {
            var b = new Building("shop", s, 0, 307)
            {
                Wall = Mat(Surface.Brick, new Color(0.9f, 0.86f, 0.82f)),
                Band = Mat(Surface.Concrete, new Color(0.5f, 0.5f, 0.48f)),
                BandTop = 0.55f,
                Inner = Mat(Surface.Plaster, new Color(0.78f, 0.74f, 0.64f)),
                Trim = Mat(Surface.Metal, new Color(0.58f, 0.59f, 0.6f), 0.4f, 0.6f),
                Foundation = Mat(Surface.Concrete, new Color(0.5f, 0.5f, 0.48f)),
                Floor = Mat(PropSurfaces.FloorTiles, new Color(0.54f, 0.52f, 0.49f)),
                Ceiling = Mat(PropSurfaces.CeilingTiles, new Color(0.78f, 0.77f, 0.73f)),
                Roof = Mat(Surface.Gravel, new Color(0.44f, 0.43f, 0.41f)),
                Metal = Mat(Surface.PaintedMetal, new Color(0.55f, 0.56f, 0.56f)),
                Parapet = 0.9f,
                Moss = 0.6f,
                Windows = WindowKind.Storefront,
                DoorType = DoorKind.Glass,
                Casing = 0.07f,
                CornerBoards = false
            };
            b.Sash = b.Trim;
            b.Leaf = b.Trim;
            b.Door(0f, 1.6f, 2.3f);
            b.Window(0, -3.0f, 2.7f, 0.9f, 2.45f, Fill.Glass);
            b.Window(0, 3.0f, 2.7f, 0.9f, 2.45f, Fill.Broken);
            b.Window(1, 2.6f, 0.8f, 1.5f, 2.2f, Fill.Glass);
            b.Window(2, 0.5f, 1.4f, 1.0f, 2.2f, Fill.Boarded);
            b.Window(3, -1.5f, 1.0f, 1.1f, 2.1f, Fill.Broken);
            Spawn(t, b, kit =>
            {
                Frame front = WallFrame(b, 0);
                MeshBuilder white = kit[WhitePaint];
                // Fascia sign across the parapet.
                float sy = b.H + 0.55f;
                MeshBuilder red = kit[RedPaint];
                red.Color = Tone(1f);
                white.Color = Tone(1f);
                red.Box(front.P(0f, sy, 0.07f), new Vector3(s.x - 0.8f, 0.9f, 0.12f), front.Rot, 0.02f);
                white.Box(front.P(0f, sy - 0.36f, 0.135f), new Vector3(s.x - 0.8f, 0.07f, 0.012f), front.Rot);
                white.Box(front.P(0f, sy + 0.36f, 0.135f), new Vector3(s.x - 0.8f, 0.03f, 0.012f), front.Rot);
                Lettering(white, front.P(-0.45f, sy + 0.02f, 0.14f), Vector3.up, front.Out, 0.38f, 5.4f, 307);
                kit[YellowPaint].Color = Tone(1f);
                kit[YellowPaint].Cylinder(front.P(3.6f, sy, 0.13f), front.P(3.6f, sy, 0.15f), 0.3f, 0.3f, 20);
                red.Cylinder(front.P(3.6f, sy, 0.15f), front.P(3.6f, sy, 0.16f), 0.18f, 0.18f, 20);
                // Ice chest and propane cage beside the entrance.
                white.Color = Tone(0.88f);
                white.Box(front.P(-1.45f, 0.5f, 0.42f), new Vector3(1.15f, 0.95f, 0.7f), front.Rot, 0.03f);
                MeshBuilder blue = kit[Mat(Surface.PaintedMetal, new Color(0.2f, 0.36f, 0.6f))];
                blue.Color = Tone(1f);
                blue.Box(front.P(-1.45f, 0.72f, 0.775f), new Vector3(0.9f, 0.22f, 0.01f), front.Rot);
                Lettering(white, front.P(-1.45f, 0.72f, 0.785f), Vector3.up, front.Out, 0.12f, 0.6f, 309);
                Cage(kit, front.P(1.45f, 0f, 0.4f), front.Rot);
                Frame back = WallFrame(b, 1), side = WallFrame(b, 3);
                PersonnelDoor(kit, back, -1.6f, b);
                Dumpster(kit, back.P(-3.4f, 0f, 1.1f), back.Rot);
                Downspout(kit, b, back, -b.HalfX + 0.35f);
                Downspout(kit, b, back, b.HalfX - 0.35f);
                AcUnit(kit, side.P(1.4f, 0f, 0.6f), side.Rot);
                // Posters in the windows.
                MeshBuilder posters = kit[Mat(Surface.Plaster, new Color(0.85f, 0.8f, 0.62f))];
                for (int i = 0; i < 3; i++)
                {
                    float u = i == 2 ? 2.2f : -3.9f + i * 1.3f;
                    posters.Color = Vary(307, 180 + i, 0.3f);
                    posters.Box(front.P(u, 1.65f + (i % 2) * 0.2f, -0.045f), new Vector3(0.45f, 0.6f, 0.004f), front.Rot * Quaternion.Euler(0f, 0f, (i - 1) * 4f));
                }
                // Interior: checkout counter with a till, cans and boxes on the floor.
                MeshBuilder counter = kit[Mat(Surface.Wood, new Color(0.5f, 0.42f, 0.33f))];
                counter.Color = Tone(0.9f);
                counter.Box(new Vector3(-3.0f, b.FloorY + 0.5f, 1.75f), new Vector3(2.0f, 1.0f, 0.65f), null, 0.01f);
                kit[DarkMetal].Color = Tone(0.9f);
                kit[DarkMetal].Box(new Vector3(-3.4f, b.FloorY + 1.12f, 1.7f), new Vector3(0.4f, 0.22f, 0.35f), Quaternion.Euler(0f, 15f, 0f), 0.02f);
                MeshBuilder goods = kit[Mat(Surface.PaintedMetal, new Color(0.6f, 0.5f, 0.35f))];
                for (int i = 0; i < 10; i++)
                {
                    goods.Color = new Color(0.6f + PropGeo.Hash(307, 200 + i) * 0.5f, 0.6f + PropGeo.Hash(307, 210 + i) * 0.4f, 0.5f + PropGeo.Hash(307, 220 + i) * 0.5f, 1f);
                    Vector3 p = new Vector3(-2.5f + PropGeo.Hash(307, 230 + i) * 5f, b.FloorY, -1.4f + PropGeo.Hash(307, 240 + i) * 2.2f);
                    if (i % 2 == 0) goods.Cylinder(p + Vector3.up * 0.04f, p + new Vector3(0.12f, 0.04f, 0.02f), 0.04f, 0.04f, 8);
                    else goods.Box(p + Vector3.up * 0.06f, new Vector3(0.22f, 0.12f, 0.16f), Quaternion.Euler(0f, i * 37f, 0f), 0.01f);
                }
            });
        }

        private static void Garage(Transform t, Vector3 s)
        {
            var b = new Building("garage", s, 0, 401)
            {
                Wall = Mat(Surface.Corrugated, new Color(0.46f, 0.52f, 0.56f)),
                Band = Mat(Surface.Concrete, new Color(0.54f, 0.53f, 0.5f)),
                BandTop = 1.2f,
                Inner = Mat(Surface.Corrugated, new Color(0.42f, 0.44f, 0.45f)),
                Trim = Mat(Surface.PaintedMetal, new Color(0.34f, 0.37f, 0.39f)),
                Foundation = Mat(Surface.Concrete, new Color(0.5f, 0.49f, 0.47f)),
                Floor = Mat(Surface.Concrete, new Color(0.46f, 0.45f, 0.43f)),
                Roof = Mat(Surface.Corrugated, new Color(0.52f, 0.47f, 0.42f)),
                Metal = Mat(Surface.PaintedMetal, new Color(0.4f, 0.42f, 0.43f)),
                RoofType = RoofKind.Gable,
                Pitch = 12f,
                Eave = 0.35f,
                Rake = 0.25f,
                RoofThick = 0.1f,
                Moss = 0.3f,
                Rust = 0.55f,
                RoofRust = 0.8f,
                Gutters = true,
                Trusses = true,
                Windows = WindowKind.Industrial,
                DoorType = DoorKind.RollUp,
                Casing = 0.08f
            };
            b.Sash = DarkMetal;
            b.Leaf = b.Trim;
            b.Door(0f, 5f, 4f);
            b.Window(0, -4.9f, 2.2f, 2.6f, 3.6f, Fill.Glass);
            b.Window(1, 1.0f, 2.4f, 2.6f, 3.6f, Fill.Glass);
            b.Window(2, 0f, 2.4f, 2.4f, 3.6f, Fill.Glass);
            b.Window(3, 0.5f, 2.4f, 2.4f, 3.6f, Fill.Boarded);
            Spawn(t, b, kit =>
            {
                Frame front = WallFrame(b, 0), right = WallFrame(b, 3), left = WallFrame(b, 2);
                // Shop sign above the door.
                MeshBuilder sign = kit[Mat(Surface.PaintedMetal, new Color(0.78f, 0.74f, 0.62f))];
                sign.Color = Tone(0.95f);
                sign.Box(front.P(0f, 4.48f, 0.05f), new Vector3(5.4f, 0.72f, 0.06f), front.Rot, 0.015f);
                MeshBuilder blue = kit[Mat(Surface.PaintedMetal, new Color(0.18f, 0.27f, 0.42f))];
                blue.Color = Tone(1f);
                blue.Box(front.P(0f, 4.2f, 0.085f), new Vector3(5.3f, 0.08f, 0.01f), front.Rot);
                Lettering(blue, front.P(0f, 4.53f, 0.085f), Vector3.up, front.Out, 0.32f, 4.6f, 403);
                PersonnelDoor(kit, front, 4.9f, b);
                PersonnelDoor(kit, WallFrame(b, 1), -4.2f, b);
                WallLamp(kit, front.P(-3.1f, 4.15f, 0f), front.Rot);
                WallLamp(kit, front.P(3.1f, 4.15f, 0f), front.Rot);
                // Drums and tyres outside.
                Drums(kit, right.P(-2.6f, 0f, 0.55f), right.Rot, 405, 3);
                Tyres(kit, left.P(2.8f, 0f, 0.6f), 4);
                Tyres(kit, left.P(1.9f, 0f, 0.75f), 2);
                // Interior: workbench and pegboard, engine stand, lift posts, oil stains, chain hoist.
                float wz = -b.HalfZ + b.Th;
                Workbench(kit, new Vector3(0.9f, b.FloorY, wz + 0.4f));
                MeshBuilder engine = kit[DarkMetal];
                engine.Color = Tone(0.9f);
                engine.Box(new Vector3(3.2f, b.FloorY + 0.85f, -1.0f), new Vector3(0.62f, 0.5f, 0.75f), Quaternion.Euler(0f, 20f, 0f), 0.04f);
                MeshBuilder red = kit[RedPaint];
                red.Color = Tone(0.8f);
                PropGeo.Rod(red, new Vector3(3.2f, b.FloorY, -1.0f), new Vector3(3.2f, b.FloorY + 0.6f, -1.0f), 0.04f);
                PropGeo.Beam(red, new Vector3(2.7f, b.FloorY + 0.04f, -1.0f), new Vector3(3.7f, b.FloorY + 0.04f, -1.0f), 0.06f, 0.06f, Vector3.up);
                MeshBuilder lift = kit[YellowPaint];
                lift.Color = Tone(0.9f);
                for (int i = 0; i < 2; i++) lift.Box(new Vector3(5.0f, b.FloorY + 1.4f, i == 0 ? -1.9f : 1.6f), new Vector3(0.32f, 2.8f, 0.32f), null, 0.02f);
                lift.Box(new Vector3(5.0f, b.FloorY + 2.85f, -0.15f), new Vector3(0.2f, 0.12f, 3.5f));
                Stains(kit, new Vector3(-0.8f, b.FloorY, 0.6f), 403);
                PropGeo.Rope(kit[DarkMetal], new Vector3(-2.2f, b.H - 0.2f, 0.4f), new Vector3(-2.2f, 1.6f, 0.4f), 0.012f, 0f, 2);
                kit[DarkMetal].Box(new Vector3(-2.2f, 1.5f, 0.4f), new Vector3(0.2f, 0.25f, 0.15f), null, 0.02f);
            });
        }

        private static void Warehouse(Transform t, Vector3 s)
        {
            var b = new Building("warehouse", s, 0, 433)
            {
                Wall = Mat(Surface.Corrugated, new Color(0.64f, 0.56f, 0.45f)),
                Band = Mat(Surface.Concrete, new Color(0.54f, 0.53f, 0.5f)),
                BandTop = 1.0f,
                Inner = Mat(Surface.Corrugated, new Color(0.5f, 0.46f, 0.4f)),
                Trim = Mat(Surface.PaintedMetal, new Color(0.42f, 0.39f, 0.35f)),
                Foundation = Mat(Surface.Concrete, new Color(0.5f, 0.49f, 0.47f)),
                Floor = Mat(Surface.Concrete, new Color(0.48f, 0.47f, 0.45f)),
                Roof = Mat(Surface.Corrugated, new Color(0.5f, 0.5f, 0.49f)),
                Metal = Mat(Surface.PaintedMetal, new Color(0.38f, 0.38f, 0.37f)),
                RoofType = RoofKind.Gable,
                RidgeAlongZ = true,
                Pitch = 11f,
                Eave = 0.3f,
                Rake = 0.3f,
                RoofThick = 0.1f,
                Moss = 0.3f,
                Rust = 0.65f,
                RoofRust = 0.9f,
                Gutters = true,
                Trusses = true,
                Windows = WindowKind.Industrial,
                DoorType = DoorKind.RollUp,
                Casing = 0.08f
            };
            b.Sash = DarkMetal;
            b.Leaf = b.Trim;
            b.Door(0f, 4f, 4f);
            b.Window(0, -5.6f, 2.6f, 4.2f, 5.2f, Fill.Glass);
            b.Window(0, 5.6f, 2.6f, 4.2f, 5.2f, Fill.Glass);
            b.Window(2, -2.8f, 2.4f, 3.8f, 4.8f, Fill.Glass);
            b.Window(2, 2.8f, 2.4f, 3.8f, 4.8f, Fill.Glass);
            b.Window(3, -2.8f, 2.4f, 3.8f, 4.8f, Fill.Glass);
            b.Window(3, 2.8f, 2.4f, 3.8f, 4.8f, Fill.Boarded);
            Spawn(t, b, kit =>
            {
                Frame front = WallFrame(b, 0), right = WallFrame(b, 3);
                PersonnelDoor(kit, front, -4.6f, b);
                PersonnelDoor(kit, WallFrame(b, 1), 4.5f, b);
                WallLamp(kit, front.P(0f, 4.55f, 0f), front.Rot);
                WallLamp(kit, front.P(-4.6f, 2.75f, 0f), front.Rot);
                MeshBuilder yellow = kit[YellowPaint];
                yellow.Color = Tone(0.9f);
                kit[Concrete].Color = Tone(0.85f);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Vector3 p = front.P(sx * 2.4f, 0f, 0.45f);
                    yellow.Cylinder(p + Vector3.down * 0.2f, p + Vector3.up * 1.1f, 0.1f, 0.1f, 12);
                    kit[Concrete].Cylinder(p + Vector3.down * 0.2f, p + Vector3.up * 0.06f, 0.17f, 0.17f, 12);
                }
                // Big painted unit number on the front gable.
                kit[WhitePaint].Color = Tone(0.85f);
                Lettering(kit[WhitePaint], front.P(0f, 6.3f, 0.02f), Vector3.up, front.Out, 0.7f, 1.1f, 434);
                Pallets(kit, right.P(2.6f, 0f, 1.2f), right.Rot, 437, true);
                Drums(kit, right.P(-1.0f, 0f, 0.55f), right.Rot, 439, 2);
                // Interior: a pallet rack along the left wall and loose pallets.
                Rack(kit, new Vector3(-b.HalfX + b.Th + 0.55f, b.FloorY, 1.6f), 6f, b.Seed);
                Pallets(kit, new Vector3(4.6f, b.FloorY, 3.0f), Quaternion.Euler(0f, 12f, 0f), 441, false);
                Stains(kit, new Vector3(1.4f, b.FloorY, 0.5f), 443);
            });
        }

        private static void Shack(Transform t, Vector3 s, int variant)
        {
            int v = ((variant % 2) + 2) % 2;
            var b = new Building("shack", s, v, 503 + v * 11)
            {
                Wall = v == 0 ? Mat(Surface.PlanksVertical, new Color(0.54f, 0.48f, 0.4f)) : Mat(Surface.Corrugated, new Color(0.56f, 0.46f, 0.38f)),
                Inner = Mat(Surface.PlanksVertical, new Color(0.46f, 0.4f, 0.33f)),
                Trim = Mat(Surface.Wood, new Color(0.46f, 0.41f, 0.34f)),
                Foundation = Mat(Surface.Concrete, new Color(0.5f, 0.49f, 0.46f)),
                Floor = Mat(Surface.Planks, new Color(0.42f, 0.35f, 0.28f)),
                Roof = Mat(Surface.Corrugated, new Color(0.54f, 0.44f, 0.37f)),
                Metal = Mat(Surface.PaintedMetal, new Color(0.45f, 0.45f, 0.43f)),
                RoofType = RoofKind.Mono,
                Pitch = 9f,
                Eave = 0.35f,
                Rake = 0.25f,
                RoofThick = 0.08f,
                Moss = 0.4f,
                Rust = v == 1 ? 0.8f : 0f,
                RoofRust = 1f,
                Windows = WindowKind.Rustic,
                DoorType = DoorKind.Plank,
                Casing = 0.1f,
                BaseY = 0.15f
            };
            b.Sash = b.Trim;
            b.Leaf = Mat(Surface.PlanksVertical, new Color(0.5f, 0.44f, 0.36f));
            b.Door(0f, 1.6f, 2.2f);
            b.Window(0, -(s.x * 0.5f - 0.95f), 0.7f, 1.1f, 1.8f, v == 0 ? Fill.Broken : Fill.Boarded);
            b.Window(2, 0.2f, 0.8f, 1.1f, 1.9f, Fill.Glass);
            b.Window(3, -0.3f, 0.8f, 1.1f, 1.9f, v == 0 ? Fill.Boarded : Fill.Broken);
            Spawn(t, b, kit =>
            {
                Frame front = WallFrame(b, 0), right = WallFrame(b, 3), left = WallFrame(b, 2);
                // Stovepipe through the roof from a small stove in the corner.
                Vector3 pipe = new Vector3(b.HalfX - 0.7f, 0f, -0.5f);
                float roofY = b.MonoUnderY(pipe.z);
                MeshBuilder iron = kit[DarkMetal];
                iron.Color = Tone(0.9f);
                PropGeo.Rod(iron, pipe + Vector3.up * 0.9f, pipe + Vector3.up * (roofY + 0.9f), 0.07f, 10);
                iron.Cylinder(pipe + Vector3.up * (roofY + 0.9f), pipe + Vector3.up * (roofY + 1.1f), 0.16f, 0.02f, 10, false);
                iron.Box(pipe + new Vector3(0f, b.FloorY + 0.35f, 0f), new Vector3(0.5f, 0.6f, 0.45f), null, 0.03f);
                Drums(kit, left.P(-b.HalfZ + 0.6f, 0f, 0.5f), left.Rot, b.Seed, 1);
                if (v == 0)
                {
                    // Electrical panel and conduit on the substation hut, a warning sign and a table inside.
                    kit[b.Metal].Color = Tone(0.85f);
                    kit[b.Metal].Box(right.P(1.0f, 1.5f, 0.1f), new Vector3(0.55f, 0.7f, 0.2f), right.Rot, 0.02f);
                    PropGeo.Rod(kit[b.Metal], right.P(1.0f, 1.85f, 0.08f), right.P(1.0f, b.H + 0.2f, 0.08f), 0.03f);
                    Warning(kit, front.P(1.05f, 1.75f, 0.045f), front.Rot, 0.28f);
                    MeshBuilder wood = kit[b.Trim];
                    wood.Color = Tone(0.9f);
                    wood.Box(new Vector3(1.2f, b.FloorY + 0.74f, 0.4f), new Vector3(1.0f, 0.05f, 0.65f), null, 0.01f);
                    for (int i = 0; i < 4; i++) wood.Box(new Vector3(1.2f + (i % 2 - 0.5f) * 0.9f, b.FloorY + 0.36f, 0.4f + (i / 2 - 0.5f) * 0.55f), new Vector3(0.05f, 0.72f, 0.05f));
                    wood.Box(new Vector3(0.45f, b.FloorY + 0.22f, 0.9f), new Vector3(0.42f, 0.04f, 0.42f), Quaternion.Euler(80f, 30f, 0f), 0.01f);
                }
            });
        }

        private static void Barn(Transform t, Vector3 s)
        {
            var b = new Building("barn", s, 0, 601)
            {
                Wall = Mat(Surface.PlanksVertical, new Color(0.54f, 0.2f, 0.15f)),
                Band = Mat(Surface.Stone, new Color(0.62f, 0.6f, 0.56f)),
                BandTop = 0.75f,
                Inner = Mat(Surface.PlanksVertical, new Color(0.44f, 0.34f, 0.26f)),
                Trim = Mat(Surface.Wood, new Color(0.8f, 0.78f, 0.72f)),
                Foundation = Mat(Surface.Concrete, new Color(0.5f, 0.49f, 0.46f)),
                Floor = Mat(Surface.Planks, new Color(0.4f, 0.33f, 0.26f)),
                Ceiling = Mat(Surface.Planks, new Color(0.42f, 0.34f, 0.26f)),
                Roof = Mat(Surface.Corrugated, new Color(0.5f, 0.5f, 0.49f)),
                Metal = Mat(Surface.PaintedMetal, new Color(0.4f, 0.4f, 0.39f)),
                RoofType = RoofKind.Gambrel,
                RidgeAlongZ = true,
                Pitch = 58f,
                Pitch2 = 24f,
                Break = 0.72f,
                Eave = 0.4f,
                Rake = 0.35f,
                RoofThick = 0.12f,
                Moss = 0.5f,
                RoofRust = 0.6f,
                Windows = WindowKind.Rustic,
                DoorType = DoorKind.Barn,
                Casing = 0.12f
            };
            b.Sash = b.Trim;
            b.Leaf = b.Wall;
            b.Door(0f, 4f, 4.5f);
            b.Window(2, -2.0f, 0.9f, 1.8f, 2.7f, Fill.Glass);
            b.Window(2, 2.0f, 0.9f, 1.8f, 2.7f, Fill.Broken);
            b.Window(3, -2.0f, 0.9f, 1.8f, 2.7f, Fill.Broken);
            b.Window(3, 2.0f, 0.9f, 1.8f, 2.7f, Fill.Glass);
            b.Window(1, 0f, 0.9f, 1.8f, 2.7f, Fill.Boarded);
            Spawn(t, b, kit =>
            {
                Frame front = WallFrame(b, 0);
                MeshBuilder trim = kit[b.Trim];
                // Hayloft door in the front gable, with the hoist beam above.
                float hy0 = b.H + 0.5f, hy1 = b.H + 2.4f;
                trim.Color = Tone(0.95f);
                Casing(trim, front, -0.85f, 0.85f, hy0, hy1, 0.14f, 0.04f, true, false);
                MeshBuilder panel = kit[b.Wall];
                panel.Color = Tone(0.8f);
                panel.Box(front.P(0f, (hy0 + hy1) * 0.5f, 0.02f), new Vector3(1.7f, hy1 - hy0, 0.04f), front.Rot);
                PropGeo.Beam(trim, front.P(-0.75f, hy0 + 0.1f, 0.05f), front.P(0.75f, hy1 - 0.1f, 0.05f), 0.12f, 0.02f, front.Out);
                PropGeo.Beam(trim, front.P(0.75f, hy0 + 0.1f, 0.05f), front.P(-0.75f, hy1 - 0.1f, 0.05f), 0.12f, 0.02f, front.Out);
                trim.Box(front.P(0f, (hy0 + hy1) * 0.5f, 0.05f), new Vector3(0.1f, hy1 - hy0, 0.02f), front.Rot);
                float peak = b.UnderY(0f);
                MeshBuilder beamWood = kit[OldWood];
                beamWood.Color = Tone(0.85f);
                beamWood.Box(front.P(0f, peak - 0.25f, 0.5f), new Vector3(0.18f, 0.2f, 1.2f), front.Rot);
                kit[DarkMetal].Color = Tone(0.9f);
                kit[DarkMetal].Cylinder(front.P(0f, peak - 0.45f, 0.95f), front.P(0f, peak - 0.3f, 0.95f), 0.08f, 0.08f, 10);
                PropGeo.Rope(kit[Mat(Surface.Fabric, new Color(0.55f, 0.5f, 0.4f))], front.P(0f, peak - 0.45f, 0.95f), front.P(0.1f, hy1 + 0.4f, 0.9f), 0.012f, 0.05f, 3);
                // Cupola on the ridge.
                float ridge = b.RidgeTop;
                MeshBuilder cup = kit[b.Wall];
                cup.Color = Tone(0.9f);
                cup.Box(new Vector3(0f, ridge + 0.1f, 0f), new Vector3(1.0f, 1.0f, 1.0f));
                trim.Box(new Vector3(0f, ridge + 0.45f, 0f), new Vector3(1.04f, 0.5f, 1.04f));
                kit[DarkMetal].Color = Tone(0.5f, 0.6f);
                for (int i = 0; i < 4; i++)
                {
                    Quaternion r = Quaternion.Euler(0f, i * 90f, 0f);
                    kit[DarkMetal].Box(r * new Vector3(0f, 0f, 0.525f) + Vector3.up * (ridge + 0.45f), new Vector3(0.7f, 0.36f, 0.01f), r);
                }
                kit[b.Roof].Color = Tone(0.9f);
                kit[b.Roof].Box(new Vector3(0f, ridge + 0.72f, 0f), new Vector3(1.25f, 0.06f, 1.25f), null, 0.02f);
                kit[b.Roof].Matrix = Matrix4x4.Rotate(Quaternion.Euler(0f, 45f, 0f));
                kit[b.Roof].Cylinder(new Vector3(0f, ridge + 0.75f, 0f), new Vector3(0f, ridge + 1.25f, 0f), 0.88f, 0f, 4, false);
                kit[b.Roof].Matrix = Matrix4x4.identity;
                kit[DarkMetal].Color = Tone(0.9f);
                PropGeo.Rod(kit[DarkMetal], new Vector3(0f, ridge + 1.2f, 0f), new Vector3(0f, ridge + 1.9f, 0f), 0.015f);
                kit[DarkMetal].Box(new Vector3(0.12f, ridge + 1.82f, 0f), new Vector3(0.4f, 0.12f, 0.01f));
                WallLamp(kit, front.P(0f, 4.95f, 0f), front.Rot);
                // Interior: straw bales and loose straw.
                MeshBuilder straw = kit[Mat(Surface.Needles, new Color(0.72f, 0.62f, 0.38f))];
                float bx = -b.HalfX + b.Th + 0.5f;
                for (int i = 0; i < 7; i++)
                {
                    int layer = i < 4 ? 0 : 1;
                    float z = layer == 0 ? 0.5f + i * 0.95f : 0.95f + (i - 4) * 0.95f;
                    straw.Color = Vary(601, 300 + i, 0.15f);
                    straw.Box(new Vector3(bx + layer * 0.05f, b.FloorY + 0.23f + layer * 0.46f, z), new Vector3(0.48f, 0.46f, 0.92f), Quaternion.Euler(0f, (PropGeo.Hash(601, 310 + i) - 0.5f) * 10f, 0f), 0.05f);
                }
                straw.Color = Tone(0.8f);
                straw.Box(new Vector3(bx + 1.0f, b.FloorY + 0.03f, 1.0f), new Vector3(1.4f, 0.06f, 1.8f), Quaternion.Euler(0f, 20f, 0f), 0.03f);
                straw.Box(new Vector3(1.5f, b.FloorY + 0.02f, -1.5f), new Vector3(2.0f, 0.04f, 1.2f), Quaternion.Euler(0f, -15f, 0f), 0.02f);
            });
        }

        // ------------------------------------------------------------------ building details

        /// <summary>
        /// Stylised lettering: blocky glyphs grouped in words, so signs read as text from a distance
        /// without rendering any. The line is centred on <paramref name="center"/> and runs left to right
        /// for someone looking at it against <paramref name="outward"/>.
        /// </summary>
        private static void Lettering(MeshBuilder m, Vector3 center, Vector3 up, Vector3 outward, float cap, float width, int seed)
        {
            Vector3 right = Vector3.Cross(outward, up).normalized;
            Quaternion rot = Quaternion.LookRotation(outward, up);
            float stroke = Mathf.Max(0.008f, cap * 0.17f), space = cap * 0.2f;
            // Glyph widths first (negative = word gap), so the line can be centred.
            var glyphs = new List<float>();
            float used = 0f;
            for (int i = 0; i < 200; i++)
            {
                float h = PropGeo.Hash(seed, i);
                bool gap = i > 2 && h < 0.13f && glyphs.Count > 0 && glyphs[glyphs.Count - 1] > 0f;
                float w = gap ? -cap * 0.45f : h < 0.25f ? stroke : cap * (0.5f + PropGeo.Hash(seed, i + 300) * 0.25f);
                float advance = Mathf.Abs(w) + (gap ? 0f : space);
                if (used + advance > width) break;
                glyphs.Add(w);
                used += advance;
            }
            while (glyphs.Count > 0 && glyphs[glyphs.Count - 1] < 0f)
            {
                used += glyphs[glyphs.Count - 1];
                glyphs.RemoveAt(glyphs.Count - 1);
            }
            float x = -(used - space) * 0.5f;
            for (int i = 0; i < glyphs.Count; i++)
            {
                float w = glyphs[i];
                if (w < 0f)
                {
                    x -= w;
                    continue;
                }
                Vector3 c = center + right * (x + w * 0.5f);
                if (w <= stroke) m.Box(c, new Vector3(stroke, cap, 0.006f), rot);
                else
                {
                    int shape = (int)(PropGeo.Hash(seed, i + 500) * 4f);
                    m.Box(c - right * (w * 0.5f - stroke * 0.5f), new Vector3(stroke, cap, 0.006f), rot);
                    if (shape != 1) m.Box(c + right * (w * 0.5f - stroke * 0.5f), new Vector3(stroke, cap, 0.006f), rot);
                    if (shape != 2) m.Box(c + up * (cap * 0.5f - stroke * 0.5f), new Vector3(w, stroke, 0.006f), rot);
                    if (shape != 3) m.Box(c - up * (cap * 0.5f - stroke * 0.5f), new Vector3(w, stroke, 0.006f), rot);
                    if (shape >= 2) m.Box(c, new Vector3(w * 0.8f, stroke, 0.006f), rot);
                }
                x += w + space;
            }
        }

        private static void PersonnelDoor(PropKit kit, Frame f, float u, Building b)
        {
            MeshBuilder frame = kit[b.Trim];
            frame.Color = Tone(0.9f);
            Casing(frame, f, u - 0.48f, u + 0.48f, 0.1f, 2.15f, 0.08f, 0.05f, false, false);
            MeshBuilder door = kit[Mat(Surface.PaintedMetal, new Color(0.36f, 0.4f, 0.36f))];
            door.Color = Tone(0.9f);
            door.Box(f.P(u, 1.125f, 0.025f), new Vector3(0.96f, 2.05f, 0.04f), f.Rot, 0.008f);
            kit[DarkMetal].Color = Tone(0.9f);
            kit[DarkMetal].Box(f.P(u + 0.36f, 1.05f, 0.06f), new Vector3(0.04f, 0.2f, 0.04f), f.Rot);
            kit[Concrete].Color = Tone(0.85f);
            kit[Concrete].Box(f.P(u, 0.0f, 0.4f), new Vector3(1.3f, 0.2f, 0.8f), f.Rot, 0.02f);
            Warning(kit, f.P(u, 1.6f, 0.05f), f.Rot, 0.25f);
        }

        /// <summary>A yellow warning triangle with a lightning bolt, facing along <paramref name="rot"/>.</summary>
        private static void Warning(PropKit kit, Vector3 c, Quaternion rot, float size)
        {
            Vector3 r = rot * Vector3.right, up = rot * Vector3.up, o = rot * Vector3.forward;
            MeshBuilder y = kit[YellowPaint];
            y.Color = Tone(1f);
            PropGeo.Poly(y, c - o, null, c - r * size * 0.5f - up * size * 0.4f, c + r * size * 0.5f - up * size * 0.4f, c + up * size * 0.48f);
            MeshBuilder k = kit[DarkMetal];
            k.Color = Tone(0.6f);
            Vector3 z = o * 0.003f;
            PropGeo.Poly(k, c - o, null, c + z - r * size * 0.05f + up * size * 0.24f, c + z + r * size * 0.08f + up * size * 0.24f, c + z - r * size * 0.04f - up * size * 0.04f);
            PropGeo.Poly(k, c - o, null, c + z - r * size * 0.07f, c + z + r * size * 0.06f, c + z - up * size * 0.3f);
        }

        private static void WallLamp(PropKit kit, Vector3 p, Quaternion rot)
        {
            Vector3 o = rot * Vector3.forward;
            MeshBuilder m = kit[DarkMetal];
            m.Color = Tone(0.9f);
            PropGeo.Rod(m, p, p + o * 0.35f + Vector3.up * 0.12f, 0.02f);
            m.Cylinder(p + o * 0.4f + Vector3.up * 0.02f, p + o * 0.4f + Vector3.up * 0.16f, 0.2f, 0.04f, 12, false);
            kit[Mat(Surface.Glass, new Color(0.7f, 0.68f, 0.6f), 0.6f)].Sphere(p + o * 0.4f + Vector3.up * 0.02f, new Vector3(0.07f, 0.05f, 0.07f), 1);
        }

        private static void Bench(PropKit kit, Vector3 p, Quaternion rot)
        {
            MeshBuilder wood = kit[Mat(Surface.Wood, new Color(0.48f, 0.38f, 0.28f))];
            MeshBuilder iron = kit[DarkMetal];
            Vector3 r = rot * Vector3.right, o = rot * Vector3.forward;
            wood.Color = Tone(0.9f);
            for (int i = 0; i < 3; i++) wood.Box(p + Vector3.up * 0.45f + o * (0.12f - i * 0.12f), new Vector3(1.5f, 0.035f, 0.1f), rot, 0.008f);
            for (int i = 0; i < 2; i++) wood.Box(p + Vector3.up * (0.62f + i * 0.14f) - o * 0.2f, new Vector3(1.5f, 0.1f, 0.03f), rot * Quaternion.Euler(-10f, 0f, 0f), 0.008f);
            iron.Color = Tone(0.9f);
            for (int s = -1; s <= 1; s += 2)
            {
                iron.Box(p + r * (s * 0.62f) + Vector3.up * 0.22f, new Vector3(0.05f, 0.44f, 0.4f), rot);
                iron.Box(p + r * (s * 0.62f) + Vector3.up * 0.65f - o * 0.22f, new Vector3(0.05f, 0.42f, 0.05f), rot);
            }
        }

        /// <summary>A rain-water collector head through the parapet and a pipe down the wall.</summary>
        private static void Downspout(PropKit kit, Building b, Frame f, float u)
        {
            MeshBuilder m = kit[b.Metal];
            m.Color = Tone(0.85f);
            float top = b.H + 0.35f;
            m.Box(f.P(u, top, 0.12f), new Vector3(0.3f, 0.22f, 0.24f), f.Rot, 0.01f);
            PropGeo.Beam(m, f.P(u, top - 0.1f, 0.12f), f.P(u, 0.35f, 0.12f), 0.09f, 0.09f, f.Out);
            PropGeo.Beam(m, f.P(u, 0.35f, 0.12f), f.P(u, 0.06f, 0.42f), 0.09f, 0.09f, f.Out);
        }

        private static void Dumpster(PropKit kit, Vector3 p, Quaternion rot)
        {
            MeshBuilder body = kit[Mat(Surface.PaintedMetal, new Color(0.22f, 0.34f, 0.26f))];
            MeshBuilder lid = kit[Mat(Surface.Rubber, new Color(0.13f, 0.13f, 0.13f))];
            body.Color = Tone(0.9f);
            body.Box(p + Vector3.up * 0.62f, new Vector3(1.8f, 1.05f, 1.05f), rot, 0.03f);
            body.Box(p + Vector3.up * 1.13f, new Vector3(1.86f, 0.06f, 1.11f), rot, 0.02f);
            for (int s = -1; s <= 1; s += 2) body.Box(p + rot * new Vector3(s * 0.93f, 0.75f, 0f), new Vector3(0.04f, 0.08f, 0.9f), rot);
            lid.Color = Tone(1f);
            lid.Box(p + rot * new Vector3(-0.45f, 1.18f, 0f), new Vector3(0.88f, 0.04f, 1.1f), rot * Quaternion.Euler(-3f, 0f, 0f), 0.01f);
            Quaternion open = rot * Quaternion.Euler(-100f, 0f, 0f);
            lid.Box(p + rot * new Vector3(0.45f, 1.17f, -0.55f) + open * new Vector3(0f, 0f, 0.55f), new Vector3(0.88f, 0.04f, 1.1f), open, 0.01f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 w = p + rot * new Vector3((i % 2 - 0.5f) * 1.5f, 0.05f, (i / 2 - 0.5f) * 0.8f);
                lid.Cylinder(w - rot * Vector3.right * 0.025f, w + rot * Vector3.right * 0.025f, 0.05f, 0.05f, 8);
            }
        }

        private static void Gurney(PropKit kit, Vector3 p)
        {
            MeshBuilder steel = kit[Steel];
            steel.Color = Tone(0.9f);
            steel.Box(p + Vector3.up * 0.7f, new Vector3(0.62f, 0.05f, 1.9f), null, 0.01f);
            kit[Rubber].Color = Tone(1f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 leg = p + new Vector3((i % 2 - 0.5f) * 0.5f, 0f, (i / 2 - 0.5f) * 1.7f);
                PropGeo.Rod(steel, leg + Vector3.up * 0.08f, leg + Vector3.up * 0.7f, 0.018f);
                kit[Rubber].Cylinder(leg + new Vector3(-0.02f, 0.05f, 0f), leg + new Vector3(0.02f, 0.05f, 0f), 0.05f, 0.05f, 8);
            }
            MeshBuilder pad = kit[Mat(Surface.Fabric, new Color(0.5f, 0.6f, 0.66f))];
            pad.Color = Tone(0.9f);
            pad.Box(p + Vector3.up * 0.79f + Vector3.back * 0.05f, new Vector3(0.58f, 0.12f, 1.75f), Quaternion.Euler(0f, 0f, 2f), 0.04f);
        }

        private static void RoofUnit(PropKit kit, Vector3 p, Quaternion rot)
        {
            MeshBuilder body = kit[Mat(Surface.PaintedMetal, new Color(0.62f, 0.62f, 0.58f))];
            body.Color = Tone(0.9f);
            body.Box(p + Vector3.up * 0.55f, new Vector3(1.6f, 0.9f, 1.1f), rot, 0.03f);
            kit[Concrete].Color = Tone(0.85f);
            kit[Concrete].Box(p + Vector3.up * 0.05f, new Vector3(1.8f, 0.1f, 1.3f), rot);
            kit[DarkMetal].Color = Tone(0.8f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 c = p + rot * new Vector3(-0.4f + i * 0.8f, 1.0f, 0f);
                kit[DarkMetal].Cylinder(c, c + Vector3.up * 0.02f, 0.33f, 0.33f, 16);
            }
        }

        private static void Cage(PropKit kit, Vector3 p, Quaternion rot)
        {
            MeshBuilder bars = kit[Steel];
            Vector3 r = rot * Vector3.right, o = rot * Vector3.forward;
            bars.Color = Tone(0.85f);
            const float w = 0.9f, d = 0.6f, h = 1.3f;
            for (int i = 0; i <= 6; i++)
            {
                float u = -w * 0.5f + i * w / 6f;
                PropGeo.Rod(bars, p + r * u + o * d, p + r * u + o * d + Vector3.up * h, 0.012f, 4);
            }
            for (int k = 0; k < 3; k++) bars.Box(p + Vector3.up * (0.05f + k * (h - 0.05f) / 2f) + o * d, new Vector3(w, 0.03f, 0.03f), rot);
            for (int sx = -1; sx <= 1; sx += 2) bars.Box(p + r * (sx * w * 0.5f) + Vector3.up * (h * 0.5f) + o * (d * 0.5f), new Vector3(0.03f, h, d), rot);
            bars.Box(p + Vector3.up * h + o * (d * 0.5f), new Vector3(w, 0.03f, d), rot);
            MeshBuilder tanks = kit[Mat(Surface.PaintedMetal, new Color(0.66f, 0.68f, 0.7f))];
            for (int i = 0; i < 3; i++)
            {
                tanks.Color = Vary(77, i, 0.2f);
                tanks.Matrix = Matrix4x4.Translate(p + r * (-0.28f + i * 0.28f) + o * (d * 0.5f));
                tanks.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.14f, 0.02f), new Vector2(0.14f, 0.42f), new Vector2(0.1f, 0.5f), new Vector2(0.03f, 0.52f), new Vector2(0f, 0.53f) }, 10);
                tanks.Matrix = Matrix4x4.identity;
            }
        }

        private static void Drums(PropKit kit, Vector3 p, Quaternion rot, int seed, int count)
        {
            Material[] mats = { Mat(Surface.Rust, new Color(0.5f, 0.42f, 0.36f)), Mat(Surface.PaintedMetal, new Color(0.22f, 0.32f, 0.46f)), Mat(Surface.PaintedMetal, new Color(0.55f, 0.18f, 0.12f)) };
            Vector3 r = rot * Vector3.right;
            for (int i = 0; i < count; i++)
            {
                MeshBuilder m = kit[mats[(int)(PropGeo.Hash(seed, i) * 3f) % 3]];
                m.Color = Vary(seed, i + 10, 0.15f);
                bool tipped = count > 2 && i == count - 1;
                Vector3 c = p + r * (i * 0.66f - (count - 1) * 0.33f);
                Quaternion q = tipped ? rot * Quaternion.Euler(0f, 30f, 90f) : Quaternion.Euler(0f, PropGeo.Hash(seed, i + 20) * 360f, 0f);
                if (tipped) c += Vector3.up * 0.29f + rot * Vector3.forward * 0.5f + r * 0.4f;
                m.Append(MeshFactory.PropDrum(), Matrix4x4.TRS(c + (tipped ? q * Vector3.down * 0.44f : Vector3.zero), q, Vector3.one), m.Color);
            }
        }

        private static void Tyres(PropKit kit, Vector3 p, int count)
        {
            MeshBuilder m = kit[Rubber];
            m.Color = Tone(1f);
            var ring = new List<Vector2>();
            for (int k = 0; k <= 8; k++)
            {
                float a = -Mathf.PI * 0.5f + k * Mathf.PI / 8f;
                ring.Add(new Vector2(0.3f + Mathf.Cos(a) * 0.1f, 0.1f + Mathf.Sin(a) * 0.1f));
            }
            var inner = new List<Vector2>(ring);
            for (int k = 7; k >= 1; k--)
            {
                float a = Mathf.PI * 0.5f + (8 - k) * Mathf.PI / 8f;
                inner.Add(new Vector2(0.3f + Mathf.Cos(a) * 0.1f, 0.1f + Mathf.Sin(a) * 0.1f));
            }
            for (int i = 0; i < count; i++)
            {
                m.Matrix = Matrix4x4.TRS(p + new Vector3((i % 2) * 0.05f, i * 0.2f, 0f), Quaternion.Euler(0f, i * 23f, (i % 2) * 2f), Vector3.one);
                m.Lathe(inner.ToArray(), 14, true);
            }
            m.Matrix = Matrix4x4.identity;
        }

        private static void Workbench(PropKit kit, Vector3 p)
        {
            MeshBuilder top = kit[Mat(Surface.Wood, new Color(0.46f, 0.36f, 0.26f))];
            MeshBuilder frame = kit[Steel];
            top.Color = Tone(0.85f);
            top.Box(p + Vector3.up * 0.88f, new Vector3(3.0f, 0.06f, 0.8f), null, 0.01f);
            top.Box(p + Vector3.up * 0.25f, new Vector3(2.9f, 0.03f, 0.7f));
            frame.Color = Tone(0.8f);
            for (int i = 0; i < 4; i++) frame.Box(p + new Vector3((i % 2 - 0.5f) * 2.85f, 0.43f, (i / 2 - 0.5f) * 0.7f), new Vector3(0.05f, 0.86f, 0.05f));
            MeshBuilder board = kit[Mat(Surface.Plaster, new Color(0.6f, 0.52f, 0.4f))];
            board.Color = Tone(0.9f);
            board.Box(p + new Vector3(0f, 1.65f, -0.37f), new Vector3(2.8f, 1.1f, 0.03f));
            MeshBuilder tools = kit[DarkMetal];
            tools.Color = Tone(0.9f);
            for (int i = 0; i < 9; i++)
            {
                float x = -1.2f + i * 0.3f;
                float len = 0.2f + PropGeo.Hash(91, i) * 0.3f;
                tools.Box(p + new Vector3(x, 1.85f - len * 0.5f, -0.34f), new Vector3(0.04f + (i % 3) * 0.02f, len, 0.025f), Quaternion.Euler(0f, 0f, (PropGeo.Hash(92, i) - 0.5f) * 20f));
            }
            tools.Box(p + new Vector3(1.2f, 0.98f, 0.1f), new Vector3(0.2f, 0.14f, 0.16f), null, 0.02f);
            kit[RedPaint].Color = Tone(0.9f);
            kit[RedPaint].Box(p + new Vector3(-0.9f, 1.0f, 0.05f), new Vector3(0.5f, 0.2f, 0.24f), null, 0.02f);
        }

        private static void Stains(PropKit kit, Vector3 p, int seed)
        {
            MeshBuilder m = kit[Mat(Surface.Asphalt, new Color(0.16f, 0.15f, 0.14f), 0.55f)];
            for (int i = 0; i < 3; i++)
            {
                m.Color = Tone(0.8f + PropGeo.Hash(seed, i) * 0.3f);
                Vector3 c = p + new Vector3((PropGeo.Hash(seed, i + 5) - 0.5f) * 2.4f, 0.003f + i * 0.0015f, (PropGeo.Hash(seed, i + 9) - 0.5f) * 2.4f);
                float r = 0.3f + PropGeo.Hash(seed, i + 13) * 0.45f;
                m.Cylinder(c, c + Vector3.up * 0.002f, r, r, 12, true, false, true);
            }
        }

        private static void Pallets(PropKit kit, Vector3 p, Quaternion rot, int seed, bool stack)
        {
            MeshBuilder wood = kit[Mat(Surface.Planks, new Color(0.56f, 0.46f, 0.34f))];
            MeshBuilder card = kit[Mat(Surface.Fabric, new Color(0.62f, 0.5f, 0.34f))];
            int count = stack ? 3 : 2;
            for (int i = 0; i < count; i++)
            {
                Vector3 c = p + rot * new Vector3((PropGeo.Hash(seed, i) - 0.5f) * 0.1f, i * 0.15f, (i % 2) * 0.05f);
                Quaternion q = rot * Quaternion.Euler(0f, (PropGeo.Hash(seed, i + 3) - 0.5f) * 8f, 0f);
                wood.Color = Vary(seed, i + 6, 0.2f);
                for (int k = 0; k < 5; k++) wood.Box(c + q * new Vector3(-0.5f + k * 0.25f, 0.13f, 0f), new Vector3(0.1f, 0.022f, 1.2f), q);
                for (int k = 0; k < 3; k++) wood.Box(c + q * new Vector3(-0.45f + k * 0.45f, 0.06f, 0f), new Vector3(0.1f, 0.1f, 1.2f), q);
            }
            Vector3 topP = p + Vector3.up * (count * 0.15f);
            for (int k = 0; k < (stack ? 4 : 2); k++)
            {
                card.Color = Vary(seed, k + 20, 0.2f);
                card.Box(topP + rot * new Vector3((k % 2 - 0.5f) * 0.5f, 0.22f + (k / 2) * 0.42f, (PropGeo.Hash(seed, k + 30) - 0.5f) * 0.4f), new Vector3(0.46f, 0.4f, 0.55f), rot * Quaternion.Euler(0f, (PropGeo.Hash(seed, k + 40) - 0.5f) * 14f, 0f), 0.02f);
            }
        }

        private static void Rack(PropKit kit, Vector3 p, float length, int seed)
        {
            MeshBuilder blue = kit[Mat(Surface.PaintedMetal, new Color(0.2f, 0.3f, 0.48f))];
            MeshBuilder orange = kit[Mat(Surface.PaintedMetal, new Color(0.72f, 0.36f, 0.12f))];
            blue.Color = Tone(0.9f);
            orange.Color = Tone(0.9f);
            int bays = Mathf.Max(1, Mathf.RoundToInt(length / 2.7f));
            float bay = length / bays;
            for (int i = 0; i <= bays; i++)
            {
                float z = p.z - length * 0.5f + i * bay;
                for (int s = -1; s <= 1; s += 2) blue.Box(new Vector3(p.x + s * 0.45f, p.y + 1.8f, z), new Vector3(0.08f, 3.6f, 0.08f));
                for (int k = 0; k < 6; k++) PropGeo.Beam(blue, new Vector3(p.x - 0.45f, p.y + 0.3f + k * 0.55f, z), new Vector3(p.x + 0.45f, p.y + 0.55f + k * 0.55f, z), 0.03f, 0.03f, Vector3.forward);
            }
            for (int l = 0; l < 3; l++)
            {
                float y = p.y + 0.15f + l * 1.5f;
                for (int s = -1; s <= 1; s += 2) orange.Box(new Vector3(p.x + s * 0.45f, y, p.z), new Vector3(0.06f, 0.12f, length));
                if (l == 2) continue;
                for (int i = 0; i < bays; i++)
                {
                    if (PropGeo.Hash(seed, l * 7 + i) < 0.25f) continue;
                    Vector3 c = new Vector3(p.x, y + 0.08f, p.z - length * 0.5f + (i + 0.5f) * bay);
                    Pallets(kit, c, Quaternion.Euler(0f, 90f, 0f), seed + l * 13 + i, false);
                }
            }
        }

        // ================================================================== camp

        private static void Tent(Transform t, Vector3 s)
        {
            PropKit.Spawn(t, PropKit.Cached($"prop:tent:{s.x:0.##}x{s.y:0.##}x{s.z:0.##}", kit => TentKit(kit, s)));
            Shapes.NotWalkable(ColliderBox(t, "Canvas", new Vector3(0f, s.y * 0.5f, 0f), new Vector3(s.x * 0.8f, s.y, s.z)));
        }

        private static void TentKit(PropKit kit, Vector3 s)
        {
            float w = s.x * 0.5f, h = s.y, l = s.z * 0.5f;
            Material canvasMat = Mat(Surface.Tarp, new Color(0.46f, 0.47f, 0.34f), null, null, true);
            MeshBuilder canvas = kit[canvasMat];
            const int nz = 8, nj = 5;
            // Sagging side walls: a grid from the ridge down to the hem.
            for (int side = -1; side <= 1; side += 2)
            {
                var grid = new Vector3[nz + 1, nj + 1];
                for (int i = 0; i <= nz; i++)
                {
                    float z = Mathf.Lerp(-l, l, (float)i / nz);
                    float along = Mathf.Sin(Mathf.PI * i / nz);
                    for (int j = 0; j <= nj; j++)
                    {
                        float k = (float)j / nj;
                        Vector3 ridge = new Vector3(0f, h - 0.05f * along, z);
                        Vector3 hem = new Vector3(side * w, 0.03f, z + (i == 0 ? -0.04f : i == nz ? 0.04f : 0f));
                        Vector3 p = Vector3.Lerp(ridge, hem, k);
                        p += new Vector3(-side * 0.6f, -1f, 0f).normalized * (0.08f * Mathf.Sin(Mathf.PI * k) * (0.4f + 0.6f * along));
                        grid[i, j] = p;
                    }
                }
                Sheet(canvas, grid, new Vector3(side, 0.5f, 0f), PropShades.Weathered(41 + side, 0f, h, 0.7f, 0.15f, 0f));
            }
            // Closed back, open front with the flaps tied aside.
            PropShade flat = PropShades.Weathered(43, 0f, h, 0.7f, 0.15f, 0f);
            PropGeo.Poly(canvas, new Vector3(0f, h * 0.3f, 0f), flat, new Vector3(-w, 0.03f, -l - 0.04f), new Vector3(w, 0.03f, -l - 0.04f), new Vector3(0f, h, -l));
            for (int side = -1; side <= 1; side += 2)
            {
                PropGeo.Poly(canvas, new Vector3(0f, h * 0.3f, 0f), flat, new Vector3(side * 0.08f, h - 0.05f, l + 0.01f), new Vector3(side * w, 0.03f, l + 0.04f), new Vector3(side * w * 0.62f, 0.03f, l + 0.12f));
                Vector3 roll0 = new Vector3(side * 0.16f, h - 0.22f, l + 0.05f), roll1 = new Vector3(side * w * 0.6f, 0.2f, l + 0.12f);
                canvas.Capsule(roll0, roll1, 0.03f, 6);
            }
            // A tear in one side, with a flap hanging out of it.
            MeshBuilder dark = kit[Mat(Surface.Tarp, new Color(0.08f, 0.08f, 0.07f))];
            dark.Color = Tone(1f);
            Vector3 n = new Vector3(w, h * 0.55f, 0f).normalized;
            Vector3 tc = new Vector3(w * 0.5f, h * 0.48f, -l * 0.45f) + n * 0.035f;
            Vector3 down = Vector3.Cross(n, Vector3.forward).normalized;
            if (down.y > 0f) down = -down;
            Vector3 fw = Vector3.forward;
            PropGeo.Poly(dark, tc - n, null, tc + fw * 0.3f, tc - down * 0.07f - fw * 0.02f, tc - fw * 0.24f + down * 0.02f);
            PropGeo.Poly(dark, tc - n, null, tc + fw * 0.06f + down * 0.01f, tc - down * 0.16f + fw * 0.02f, tc + down * 0.11f - fw * 0.06f);
            PropGeo.Poly(canvas, tc - n, flat, tc + Vector3.forward * 0.26f + n * 0.01f, tc - Vector3.forward * 0.2f + n * 0.01f, tc + down * 0.3f + n * 0.12f + Vector3.forward * 0.05f);
            // Poles, guy ropes and pegs.
            MeshBuilder pole = kit[Mat(Surface.Wood, new Color(0.45f, 0.36f, 0.26f))];
            MeshBuilder rope = kit[Mat(Surface.Fabric, new Color(0.62f, 0.56f, 0.44f))];
            pole.Color = Tone(0.9f);
            rope.Color = Tone(0.95f);
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 top = new Vector3(0f, h + 0.12f, e * (l - 0.02f));
                PropGeo.Rod(pole, new Vector3(0f, 0f, e * (l - 0.02f)), top, 0.022f, 6);
                Vector3 peg = new Vector3(0f, 0.02f, e * (l + 1.1f));
                PropGeo.Rope(rope, top + Vector3.down * 0.04f, peg, 0.006f, 0.04f, 4);
                Peg(pole, peg);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a = new Vector3(side * w * 0.5f, h * 0.52f, e * l * 0.55f);
                    Vector3 sp = new Vector3(side * (w + 0.85f), 0.02f, e * l * 0.6f);
                    PropGeo.Rope(rope, a, sp, 0.006f, 0.03f, 4);
                    Peg(pole, sp);
                }
            }
            // Ground sheet and a sleeping bag inside.
            MeshBuilder floor = kit[Mat(Surface.Tarp, new Color(0.18f, 0.2f, 0.22f))];
            floor.Color = Tone(0.9f, 0.8f);
            floor.Box(new Vector3(0f, 0.012f, 0.05f), new Vector3(w * 1.8f, 0.012f, l * 2f + 0.2f));
            MeshBuilder bag = kit[Mat(Surface.Fabric, new Color(0.24f, 0.3f, 0.42f))];
            bag.Color = Tone(0.9f);
            bag.Capsule(new Vector3(-w * 0.4f, 0.1f, -l + 0.4f), new Vector3(-w * 0.35f, 0.1f, l - 0.6f), 0.11f, 10);
        }

        private static void Peg(MeshBuilder m, Vector3 p)
        {
            m.Box(p + Vector3.up * 0.04f, new Vector3(0.03f, 0.12f, 0.03f), Quaternion.Euler(12f, 0f, 0f));
        }

        /// <summary>A smooth two-sided sheet over a grid (normals from neighbouring points, front toward <paramref name="outside"/>).</summary>
        private static void Sheet(MeshBuilder m, Vector3[,] grid, Vector3 outside, PropShade shade)
        {
            int ni = grid.GetLength(0), nj = grid.GetLength(1);
            int start = m.VertexCount;
            for (int i = 0; i < ni; i++)
            {
                for (int j = 0; j < nj; j++)
                {
                    Vector3 du = grid[Mathf.Min(ni - 1, i + 1), j] - grid[Mathf.Max(0, i - 1), j];
                    Vector3 dv = grid[i, Mathf.Min(nj - 1, j + 1)] - grid[i, Mathf.Max(0, j - 1)];
                    Vector3 n = Vector3.Cross(du, dv).normalized;
                    if (Vector3.Dot(n, outside) < 0f) n = -n;
                    m.Vertex(grid[i, j], n, shade(grid[i, j], n));
                }
            }
            Vector3 a0 = grid[0, 0], a1 = grid[1, 0], a2 = grid[1, 1];
            bool flip = Vector3.Dot(Vector3.Cross(a1 - a0, a2 - a0), outside) < 0f;
            for (int i = 0; i + 1 < ni; i++)
            {
                for (int j = 0; j + 1 < nj; j++)
                {
                    int a = start + i * nj + j, b = a + nj, c = b + 1, d = a + 1;
                    if (flip)
                    {
                        m.Triangle(a, c, b);
                        m.Triangle(a, d, c);
                    }
                    else
                    {
                        m.Triangle(a, b, c);
                        m.Triangle(a, c, d);
                    }
                }
            }
        }

        private static void Firepit(Transform t)
        {
            PropKit.Spawn(t, PropKit.Cached("prop:firepit", kit =>
            {
                MeshBuilder stone = kit[Mat(Surface.Rock, new Color(0.52f, 0.5f, 0.47f))];
                for (int i = 0; i < 11; i++)
                {
                    float a = i * Mathf.PI * 2f / 11f + PropGeo.Hash(7, i) * 0.2f;
                    float r = 0.56f + PropGeo.Hash(8, i) * 0.06f;
                    var size = new Vector3(0.26f + PropGeo.Hash(9, i) * 0.1f, 0.24f + PropGeo.Hash(10, i) * 0.1f, 0.22f + PropGeo.Hash(11, i) * 0.08f);
                    // Stones darken toward the fire where soot built up.
                    float soot = 0.55f + PropGeo.Hash(12, i) * 0.25f;
                    stone.Append(MeshFactory.PropStone(i), Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, size.y * 0.3f, Mathf.Sin(a) * r), Quaternion.Euler(PropGeo.Hash(13, i) * 25f, -a * Mathf.Rad2Deg, PropGeo.Hash(14, i) * 20f), size),
                        new Color(soot, soot * 0.98f, soot * 0.95f, 0.9f));
                }
                MeshBuilder ash = kit[Mat(Surface.Soil, new Color(0.3f, 0.29f, 0.28f))];
                ash.Color = Tone(0.55f, 0.8f);
                ash.Cylinder(new Vector3(0f, -0.02f, 0f), new Vector3(0f, 0.025f, 0f), 0.62f, 0.55f, 16);
                ash.Color = Tone(1.2f, 0.9f);
                ash.Lathe(new[] { new Vector2(0.48f, 0.02f), new Vector2(0.35f, 0.05f), new Vector2(0.15f, 0.075f), new Vector2(0f, 0.08f) }, 14);
                MeshBuilder charred = kit[Mat(Surface.Bark, new Color(0.11f, 0.1f, 0.09f))];
                charred.Color = Tone(1f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 1.37f + 0.4f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 outer = dir * (0.42f + PropGeo.Hash(15, i) * 0.1f) + Vector3.up * 0.07f;
                    Vector3 inner = -dir * 0.05f + Vector3.up * (0.14f + (i % 2) * 0.06f);
                    charred.Cylinder(outer, inner, 0.055f, 0.035f, 7, false);
                }
                MeshBuilder ends = kit[Mat(Surface.Soil, new Color(0.56f, 0.54f, 0.5f))];
                ends.Color = Tone(1f);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f;
                    ends.Sphere(new Vector3(Mathf.Cos(a) * 0.12f, 0.12f, Mathf.Sin(a) * 0.12f), new Vector3(0.05f, 0.03f, 0.05f), 0, false);
                }
                // Cooking tripod with a blackened pot.
                MeshBuilder sticks = kit[Mat(Surface.Wood, new Color(0.44f, 0.35f, 0.25f))];
                sticks.Color = Tone(0.9f);
                Vector3 apex = new Vector3(0f, 1.25f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2f / 3f + 0.5f;
                    Vector3 foot = new Vector3(Mathf.Cos(a) * 0.82f, 0f, Mathf.Sin(a) * 0.82f);
                    PropGeo.Rod(sticks, foot, apex + (apex - foot).normalized * 0.12f, 0.022f, 6);
                }
                MeshBuilder iron = kit[DarkMetal];
                iron.Color = Tone(1f);
                PropGeo.Rod(iron, apex, new Vector3(0f, 0.62f, 0f), 0.006f, 4);
                iron.Matrix = Matrix4x4.Translate(new Vector3(0f, 0.36f, 0f));
                iron.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.1f, 0.01f), new Vector2(0.15f, 0.08f), new Vector2(0.15f, 0.2f), new Vector2(0.165f, 0.22f), new Vector2(0.14f, 0.22f), new Vector2(0.13f, 0.06f), new Vector2(0f, 0.05f) }, 14);
                iron.Matrix = Matrix4x4.identity;
                PropGeo.Rope(iron, new Vector3(-0.15f, 0.58f, 0f), new Vector3(0.15f, 0.58f, 0f), 0.005f, -0.08f, 5);
            }));
        }

        private static void LogSeat(Transform t)
        {
            PropKit.Spawn(t, PropKit.Cached("prop:log_seat", kit =>
            {
                // Built standing along local Y and laid on its side, so object-space bark furrows run along the log.
                Quaternion lay = Quaternion.Euler(0f, 0f, 90f);
                Vector3 at = new Vector3(0f, 0.2f, 0f);
                MeshBuilder bark = kit.In(Mat(Surface.Bark, new Color(0.4f, 0.33f, 0.25f)), "Log", at, lay);
                bark.Color = Tone(0.95f);
                bark.Cylinder(new Vector3(0f, -0.9f, 0f), new Vector3(-0.01f, 0.9f, 0.02f), 0.205f, 0.185f, 11, true, false, false);
                bark.Color = Tone(0.9f);
                bark.Cylinder(new Vector3(0.12f, -0.3f, 0.12f), new Vector3(0.3f, -0.26f, 0.3f), 0.045f, 0.03f, 6, false);
                bark.Cylinder(new Vector3(-0.1f, 0.45f, -0.15f), new Vector3(-0.15f, 0.5f, -0.32f), 0.04f, 0.028f, 6, false);
                MeshBuilder wood = kit.In(Mat(Surface.Wood, new Color(0.64f, 0.52f, 0.38f)), "Log", at, lay);
                wood.Color = Tone(1f);
                wood.Cylinder(new Vector3(0f, -0.915f, 0f), new Vector3(0f, -0.895f, 0f), 0.19f, 0.19f, 11);
                wood.Cylinder(new Vector3(-0.01f, 0.895f, 0.02f), new Vector3(-0.01f, 0.915f, 0.02f), 0.172f, 0.172f, 11);
                wood.Color = Tone(0.75f);
                wood.Cylinder(new Vector3(0f, -0.92f, 0f), new Vector3(0f, -0.914f, 0f), 0.06f, 0.06f, 8);
                wood.Cylinder(new Vector3(-0.01f, 0.914f, 0.02f), new Vector3(-0.01f, 0.92f, 0.02f), 0.05f, 0.05f, 8);
            }));
            ColliderBox(t, "Log", new Vector3(0f, 0.2f, 0f), new Vector3(1.8f, 0.4f, 0.4f));
        }

        // ================================================================== fences and signs

        private static void Fence(Transform t, Vector3 s, int seed)
        {
            bool alongX = s.x >= s.z;
            float len = Mathf.Max(s.x, s.z), h = s.y;
            string style = h >= 2f ? "palisade" : h >= 1.3f ? "sheet" : "rail";
            List<PropKit.Piece> pieces = PropKit.Cached($"prop:fence:{style}:{len:0.##}:{h:0.##}:{seed}", kit =>
            {
                if (style == "palisade") Palisade(kit, len, h, seed);
                else if (style == "sheet") SheetFence(kit, len, h, seed);
                else RailFence(kit, len, h, seed);
            });
            var holder = new GameObject("Fence").transform;
            holder.SetParent(t, false);
            holder.localRotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            PropKit.Spawn(holder, pieces);
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, s.y * 0.5f, 0);
            col.size = alongX ? new Vector3(len, s.y, 0.2f) : new Vector3(0.2f, s.y, len);
        }

        private static void RailFence(PropKit kit, float len, float h, int seed)
        {
            MeshBuilder wood = kit[Mat(Surface.Wood, new Color(0.47f, 0.42f, 0.35f))];
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2.4f) + 1);
            float step = len / (posts - 1);
            var tops = new Vector3[posts];
            for (int i = 0; i < posts; i++)
            {
                float x = -len * 0.5f + i * step;
                float ph = h + 0.05f + (PropGeo.Hash(seed, i) - 0.5f) * 0.12f;
                Quaternion lean = Quaternion.Euler((PropGeo.Hash(seed, i + 40) - 0.5f) * 6f, 0f, (PropGeo.Hash(seed, i + 80) - 0.5f) * 5f);
                wood.Color = Vary(seed, i + 120, 0.2f, 0.95f);
                wood.Box(new Vector3(x, ph * 0.5f - 0.1f, 0f) + lean * Vector3.up * 0.0f, new Vector3(0.12f, ph + 0.2f, 0.12f), lean, 0.015f);
                tops[i] = new Vector3(x, 0f, 0f);
            }
            float[] rails = { 0.3f, 0.62f, 0.93f };
            for (int r = 0; r < rails.Length; r++)
            {
                for (int i = 0; i + 1 < posts; i++)
                {
                    float roll = PropGeo.Hash(seed * 3 + r, i);
                    if (roll < 0.06f) continue;
                    Vector3 a = new Vector3(tops[i].x - 0.05f, rails[r] * h, 0.08f), c = new Vector3(tops[i + 1].x + 0.05f, rails[r] * h, 0.08f);
                    if (roll < 0.14f) c = new Vector3(c.x - 0.2f, 0.06f, 0.2f);
                    wood.Color = Vary(seed * 7 + r, i, 0.22f);
                    PropGeo.Beam(wood, a, c, 0.045f, 0.11f, Vector3.up, 0.01f);
                }
            }
        }

        private static void SheetFence(PropKit kit, float len, float h, int seed)
        {
            MeshBuilder post = kit[OldWood];
            Material rust = Mat(Surface.Rust, new Color(0.52f, 0.44f, 0.38f), null, null, false);
            Material galv = Mat(Surface.Metal, new Color(0.5f, 0.52f, 0.52f), 0.3f, 0.6f);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2.6f) + 1);
            float step = len / (posts - 1);
            post.Color = Tone(0.85f);
            for (int i = 0; i < posts; i++)
            {
                float x = -len * 0.5f + i * step;
                post.Box(new Vector3(x, h * 0.5f, -0.07f), new Vector3(0.1f, h + 0.1f, 0.1f), Quaternion.Euler(0f, 0f, (PropGeo.Hash(seed, i) - 0.5f) * 4f), 0.012f);
            }
            for (int r = 0; r < 2; r++) post.Box(new Vector3(0f, r == 0 ? 0.35f : h - 0.3f, -0.07f), new Vector3(len, 0.09f, 0.045f));
            const float sheetW = 0.86f, pitch = 0.77f;
            int sheets = Mathf.CeilToInt(len / pitch);
            for (int i = 0; i < sheets; i++)
            {
                float roll = PropGeo.Hash(seed + 5, i);
                if (roll < 0.05f && i > 0 && i < sheets - 1) continue;
                float x0 = -len * 0.5f + i * pitch;
                float sh = h + (PropGeo.Hash(seed + 6, i) - 0.5f) * 0.18f;
                float y0 = 0.03f + PropGeo.Hash(seed + 7, i) * 0.08f;
                float tilt = (PropGeo.Hash(seed + 8, i) - 0.5f) * 3f;
                MeshBuilder m = kit[roll < 0.55f ? rust : galv];
                Matrix4x4 place = Matrix4x4.TRS(new Vector3(x0 + sheetW * 0.5f, y0, (i % 2) * 0.012f), Quaternion.Euler(0f, 0f, tilt), Vector3.one);
                m.Matrix = place;
                Corrugation(m, sheetW, sh, PropShades.Rusty(seed + i, -y0, 0.6f));
                m.Matrix = Matrix4x4.identity;
            }
        }

        /// <summary>A two-sided corrugated sheet centred on x = 0, from y = 0 to <paramref name="height"/>, facing ±Z.</summary>
        private static void Corrugation(MeshBuilder m, float width, float height, PropShade shade)
        {
            const int waves = 11, perWave = 4;
            int n = waves * perWave;
            for (int side = 0; side < 2; side++)
            {
                float zOff = side == 0 ? 0.004f : -0.004f;
                int start = m.VertexCount;
                for (int k = 0; k <= n; k++)
                {
                    float x = -width * 0.5f + width * k / n;
                    float ph = (float)k / perWave * Mathf.PI * 2f;
                    float z = Mathf.Sin(ph) * 0.012f + zOff;
                    float dz = Mathf.Cos(ph) * 0.012f * Mathf.PI * 2f * waves / width;
                    Vector3 nrm = new Vector3(-dz, 0f, 1f).normalized * (side == 0 ? 1f : -1f);
                    Vector3 p0 = new Vector3(x, 0f, z), p1 = new Vector3(x, height, z);
                    m.Vertex(p0, nrm, shade(p0, nrm));
                    m.Vertex(p1, nrm, shade(p1, nrm));
                }
                for (int k = 0; k < n; k++)
                {
                    int a = start + k * 2;
                    if (side == 0)
                    {
                        m.Triangle(a, a + 3, a + 1);
                        m.Triangle(a, a + 2, a + 3);
                    }
                    else
                    {
                        m.Triangle(a, a + 1, a + 3);
                        m.Triangle(a, a + 3, a + 2);
                    }
                }
            }
        }

        private static void Palisade(PropKit kit, float len, float h, int seed)
        {
            MeshBuilder steel = kit[Mat(Surface.Metal, new Color(0.5f, 0.53f, 0.52f), 0.3f, 0.6f)];
            PropShade rust = PropShades.Rusty(seed, 0f, 0.45f);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2.75f) + 1);
            float step = len / (posts - 1);
            for (int i = 0; i < posts; i++)
            {
                float x = -len * 0.5f + i * step;
                steel.Color = Tone(0.85f);
                steel.Box(new Vector3(x, (h + 0.1f) * 0.5f - 0.05f, -0.06f), new Vector3(0.1f, h + 0.15f, 0.1f), null, 0.01f);
                steel.Box(new Vector3(x, h + 0.12f, 0.05f), new Vector3(0.04f, 0.3f, 0.04f), Quaternion.Euler(30f, 0f, 0f));
            }
            for (int r = 0; r < 2; r++)
            {
                steel.Color = Tone(0.82f);
                steel.Box(new Vector3(0f, r == 0 ? 0.35f : h - 0.35f, -0.035f), new Vector3(len, 0.06f, 0.05f));
            }
            // Barbed strand on the angled arms.
            steel.Color = Tone(0.7f);
            PropGeo.Rod(steel, new Vector3(-len * 0.5f, h + 0.24f, 0.12f), new Vector3(len * 0.5f, h + 0.24f, 0.12f), 0.006f, 4, false);
            int pales = Mathf.FloorToInt(len / 0.15f);
            for (int i = 0; i <= pales; i++)
            {
                // Slightly irregular spacing breaks up moiré when the fence is seen from afar.
                float x = -len * 0.5f + i * len / pales + (i > 0 && i < pales ? (PropGeo.Hash(seed + 2, i) - 0.5f) * 0.04f : 0f);
                Vector3 c = new Vector3(x, 0f, 0f);
                Color col = rust(c + Vector3.up * 0.3f, Vector3.forward);
                float k = 0.9f + PropGeo.Hash(seed, i) * 0.14f;
                steel.Color = new Color(col.r * k, col.g * k, col.b * k, 1f);
                float ph = h - 0.08f + (PropGeo.Hash(seed + 1, i) - 0.5f) * 0.04f;
                steel.Box(new Vector3(x, ph * 0.5f + 0.01f, 0.01f), new Vector3(0.07f, ph, 0.016f));
                // Triple-pointed head.
                for (int tip = -1; tip <= 1; tip++)
                {
                    float tx = x + tip * 0.0235f, ty = ph + 0.01f;
                    for (int f = -1; f <= 1; f += 2)
                        PropGeo.Poly(steel, new Vector3(tx, ty, 0.01f - f), null, new Vector3(tx - 0.0117f, ty, 0.01f + f * 0.008f), new Vector3(tx + 0.0117f, ty, 0.01f + f * 0.008f),
                            new Vector3(tx, ty + 0.06f + (tip == 0 ? 0.015f : 0f), 0.01f + f * 0.008f));
                }
            }
        }

        private static void Sign(Transform t, Vector3 s)
        {
            bool price = s.y >= 2.6f;
            PropKit.Spawn(t, PropKit.Cached($"prop:sign:{s.x:0.##}x{s.y:0.##}", kit =>
            {
                if (price) PriceSign(kit, s);
                else TownSign(kit, s);
            }));
            float px = price ? 0.55f : 0.62f;
            for (int sx = -1; sx <= 1; sx += 2) ColliderBox(t, "Post", new Vector3(sx * px, s.y * 0.5f, 0f), new Vector3(0.12f, s.y, 0.12f));
        }

        private static void TownSign(PropKit kit, Vector3 s)
        {
            MeshBuilder wood = kit[OldWood];
            wood.Color = Tone(0.85f);
            for (int sx = -1; sx <= 1; sx += 2) wood.Box(new Vector3(sx * 0.62f, s.y * 0.5f - 0.15f, 0f), new Vector3(0.1f, s.y + 0.3f, 0.1f), Quaternion.Euler(0f, 0f, sx * 0.8f), 0.012f);
            float by = s.y - 0.5f;
            Quaternion lean = Quaternion.Euler(0f, 0f, -2.5f);
            MeshBuilder green = kit[Mat(Surface.PaintedMetal, new Color(0.17f, 0.34f, 0.23f))];
            green.Matrix = Matrix4x4.TRS(new Vector3(0f, by, 0.075f), lean, Vector3.one);
            green.Color = Tone(0.95f);
            green.Box(Vector3.zero, new Vector3(s.x, 0.95f, 0.03f), null, 0.01f);
            green.Matrix = Matrix4x4.identity;
            MeshBuilder white = kit[WhitePaint];
            white.Matrix = Matrix4x4.TRS(new Vector3(0f, by, 0.075f), lean, Vector3.one);
            white.Color = Tone(1f);
            float bw = s.x - 0.08f, bh = 0.87f;
            white.Box(new Vector3(0f, bh * 0.5f, 0.017f), new Vector3(bw, 0.025f, 0.004f));
            white.Box(new Vector3(0f, -bh * 0.5f, 0.017f), new Vector3(bw, 0.025f, 0.004f));
            white.Box(new Vector3(-bw * 0.5f, 0f, 0.017f), new Vector3(0.025f, bh, 0.004f));
            white.Box(new Vector3(bw * 0.5f, 0f, 0.017f), new Vector3(0.025f, bh, 0.004f));
            Lettering(white, new Vector3(0f, 0.1f, 0.018f), Vector3.up, Vector3.forward, 0.2f, 1.26f, 707);
            Lettering(white, new Vector3(0f, -0.22f, 0.018f), Vector3.up, Vector3.forward, 0.09f, 0.72f, 708);
            white.Matrix = Matrix4x4.identity;
            // Bullet holes.
            MeshBuilder dark = kit[DarkMetal];
            dark.Matrix = Matrix4x4.TRS(new Vector3(0f, by, 0.075f), lean, Vector3.one);
            dark.Color = Tone(0.5f);
            for (int i = 0; i < 4; i++)
                dark.Cylinder(new Vector3(-0.5f + PropGeo.Hash(709, i) * 1.0f, -0.35f + PropGeo.Hash(710, i) * 0.6f, 0.016f), new Vector3(-0.5f + PropGeo.Hash(709, i) * 1.0f, -0.35f + PropGeo.Hash(710, i) * 0.6f, 0.019f) + Vector3.forward * 0.001f, 0.012f, 0.012f, 6);
            dark.Matrix = Matrix4x4.identity;
        }

        private static void PriceSign(PropKit kit, Vector3 s)
        {
            MeshBuilder steel = kit[Mat(Surface.PaintedMetal, new Color(0.58f, 0.58f, 0.56f))];
            steel.Color = Tone(0.9f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                steel.Box(new Vector3(sx * 0.55f, s.y * 0.5f - 0.1f, 0f), new Vector3(0.12f, s.y + 0.2f, 0.12f), null, 0.01f);
                kit[Concrete].Color = Tone(0.85f);
                kit[Concrete].Box(new Vector3(sx * 0.55f, 0.1f, 0f), new Vector3(0.36f, 0.4f, 0.36f), null, 0.02f);
            }
            // Logo light box.
            float ly = s.y - 0.4f;
            MeshBuilder white = kit[WhitePaint];
            white.Color = Tone(0.95f);
            white.Box(new Vector3(0f, ly, 0f), new Vector3(s.x, 0.85f, 0.22f), null, 0.03f);
            MeshBuilder red = kit[RedPaint];
            red.Color = Tone(1f);
            for (int f = -1; f <= 1; f += 2)
            {
                red.Box(new Vector3(0f, ly - 0.32f, f * 0.112f), new Vector3(s.x - 0.04f, 0.14f, 0.004f));
                red.Cylinder(new Vector3(0f, ly + 0.06f, f * 0.11f), new Vector3(0f, ly + 0.06f, f * 0.116f), 0.27f, 0.27f, 20);
                white.Matrix = Matrix4x4.identity;
                PropGeo.Poly(white, new Vector3(0f, ly, 0f), null, new Vector3(-0.1f, ly - 0.05f, f * 0.118f), new Vector3(0.1f, ly - 0.05f, f * 0.118f), new Vector3(0f, ly + 0.2f, f * 0.118f));
                white.Sphere(new Vector3(0f, ly - 0.03f, f * 0.117f), new Vector3(0.1f, 0.1f, 0.002f), 1);
            }
            // Price board with digit blocks; one row lost its numbers.
            float py = s.y - 1.3f;
            MeshBuilder board = kit[Mat(Surface.PaintedMetal, new Color(0.14f, 0.15f, 0.16f))];
            board.Color = Tone(1f);
            board.Box(new Vector3(0f, py, 0f), new Vector3(s.x - 0.1f, 0.9f, 0.14f), null, 0.02f);
            MeshBuilder amber = kit[Mat(Surface.PaintedMetal, new Color(0.85f, 0.62f, 0.2f))];
            amber.Color = Tone(1f);
            for (int f = -1; f <= 1; f += 2)
            {
                Vector3 o = Vector3.forward * (f * 0.072f);
                Vector3 viewerRight = Vector3.Cross(o, Vector3.up).normalized;
                for (int r = 0; r < 3; r++)
                {
                    float y = py + 0.27f - r * 0.27f;
                    white.Box(new Vector3(0f, y, 0f) - viewerRight * 0.4f + o, new Vector3(0.5f, 0.17f, 0.004f));
                    Lettering(kit[DarkMetal], new Vector3(0f, y, 0f) - viewerRight * 0.4f + o * 1.06f, Vector3.up, o, 0.08f, 0.4f, 730 + r);
                    if (r == 1 && f > 0) continue;
                    Lettering(amber, new Vector3(0f, y, 0f) + viewerRight * 0.3f + o * 1.02f, Vector3.up, o, 0.16f, 0.55f, 720 + r);
                }
            }
        }

        // ================================================================== fuel station canopy

        private static void Canopy(Transform t, Vector3 s)
        {
            float hx = s.x * 0.4f, hz = s.z * 0.35f;
            PropKit.Spawn(t, PropKit.Cached($"prop:canopy:{s.x:0.##}x{s.y:0.##}x{s.z:0.##}", kit => CanopyKit(kit, s, hx, hz)));
            foreach (Vector3 c in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz) })
                ColliderBox(t, "Pillar", c + new Vector3(0, s.y * 0.5f, 0), new Vector3(0.3f, s.y, 0.3f));
            Shapes.NotWalkable(ColliderBox(t, "Roof", new Vector3(0, s.y, 0), new Vector3(s.x, 0.4f, s.z)));
        }

        private static void CanopyKit(PropKit kit, Vector3 s, float hx, float hz)
        {
            float h = s.y, under = h - 0.22f, top = h + 0.25f;
            // Forecourt slab, pump island and bollards.
            MeshBuilder slab = kit[Mat(Surface.Concrete, new Color(0.54f, 0.53f, 0.5f))];
            float sx = s.x * 0.5f + 1.6f, sz = s.z * 0.5f + 1.2f;
            PropGeo.Panel(slab, new Vector3(0f, 0.04f, 0f), Vector3.right, Vector3.forward, Vector3.up, -sx, sx, -sz, sz, null, 1.2f, 1.2f, (p, n) =>
            {
                float oil = PropGeo.Smooth(0.62f, 0.8f, PropGeo.Fbm(p * 0.45f, 811));
                float k = (0.85f + 0.15f * PropGeo.Fbm(p * 0.9f, 812)) * (1f - oil * 0.45f);
                return new Color(k, k, k * 0.98f, 1f);
            });
            slab.Color = Tone(0.8f, 0.85f);
            slab.Box(new Vector3(0f, -0.23f, sz), new Vector3(sx * 2f, 0.54f, 0.01f));
            slab.Box(new Vector3(0f, -0.23f, -sz), new Vector3(sx * 2f, 0.54f, 0.01f));
            slab.Box(new Vector3(sx, -0.23f, 0f), new Vector3(0.01f, 0.54f, sz * 2f));
            slab.Box(new Vector3(-sx, -0.23f, 0f), new Vector3(0.01f, 0.54f, sz * 2f));
            slab.Color = Tone(0.92f);
            slab.Box(new Vector3(0f, 0.09f, 0f), new Vector3(9.2f, 0.12f, 1.3f), null, 0.03f);
            // Worn lane markings either side of the island.
            MeshBuilder paint = kit[Mat(Surface.Concrete, new Color(0.78f, 0.66f, 0.3f))];
            for (int lane = -1; lane <= 1; lane += 2)
            {
                for (int i = 0; i < 6; i++)
                {
                    paint.Color = Tone(0.75f + PropGeo.Hash(817, i + lane * 10) * 0.3f);
                    paint.Box(new Vector3(-4.4f + i * 1.76f, 0.043f, lane * 2.1f), new Vector3(1.1f, 0.004f, 0.12f));
                }
            }
            MeshBuilder yellow = kit[YellowPaint];
            yellow.Color = Tone(0.9f);
            for (int sxi = -1; sxi <= 1; sxi += 2)
            {
                yellow.Cylinder(new Vector3(sxi * 4.35f, 0.15f, 0f), new Vector3(sxi * 4.35f, 1.1f, 0f), 0.09f, 0.09f, 12);
                kit[DarkMetal].Color = Tone(0.8f);
                kit[DarkMetal].Cylinder(new Vector3(sxi * 4.35f, 0.75f, 0f), new Vector3(sxi * 4.35f, 0.88f, 0f), 0.095f, 0.095f, 12);
            }
            MeshBuilder bin = kit[Mat(Surface.PaintedMetal, new Color(0.22f, 0.34f, 0.26f))];
            bin.Color = Tone(0.9f);
            bin.Cylinder(new Vector3(0f, 0.15f, 0.3f), new Vector3(0f, 0.9f, 0.3f), 0.22f, 0.24f, 14);
            kit[DarkMetal].Cylinder(new Vector3(0f, 0.9f, 0.3f), new Vector3(0f, 0.95f, 0.3f), 0.25f, 0.2f, 14);
            // Columns on concrete pedestals.
            MeshBuilder white = kit[WhitePaint];
            MeshBuilder red = kit[RedPaint];
            foreach (Vector3 c in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz) })
            {
                slab.Color = Tone(0.88f);
                slab.Box(c + Vector3.up * 0.3f, new Vector3(0.55f, 0.6f, 0.55f), null, 0.03f);
                white.Color = Tone(0.92f);
                white.Box(c + Vector3.up * (0.6f + under) * 0.5f, new Vector3(0.3f, under - 0.6f, 0.3f), null, 0.02f);
                red.Color = Tone(0.95f);
                red.Box(c + Vector3.up * 1.0f, new Vector3(0.31f, 0.5f, 0.31f), null, 0.02f);
            }
            // Fascia: white band with a red stripe, logo discs front and back.
            float fx = s.x * 0.5f, fz = s.z * 0.5f, fh = top - under + 0.15f, fy = (top + under) * 0.5f + 0.05f;
            white.Color = Tone(0.95f);
            white.Box(new Vector3(0f, fy, fz), new Vector3(s.x + 0.1f, fh, 0.1f), null, 0.02f);
            white.Box(new Vector3(0f, fy, -fz), new Vector3(s.x + 0.1f, fh, 0.1f), null, 0.02f);
            white.Box(new Vector3(fx, fy, 0f), new Vector3(0.1f, fh, s.z - 0.1f), null, 0.02f);
            white.Box(new Vector3(-fx, fy, 0f), new Vector3(0.1f, fh, s.z - 0.1f), null, 0.02f);
            red.Color = Tone(1f);
            red.Box(new Vector3(0f, fy - 0.12f, fz + 0.055f), new Vector3(s.x + 0.1f, 0.22f, 0.01f));
            red.Box(new Vector3(0f, fy - 0.12f, -fz - 0.055f), new Vector3(s.x + 0.1f, 0.22f, 0.01f));
            red.Box(new Vector3(fx + 0.055f, fy - 0.12f, 0f), new Vector3(0.01f, 0.22f, s.z + 0.1f));
            red.Box(new Vector3(-fx - 0.055f, fy - 0.12f, 0f), new Vector3(0.01f, 0.22f, s.z + 0.1f));
            kit[Mat(Surface.PaintedMetal, new Color(0.2f, 0.3f, 0.5f))].Box(new Vector3(0f, fy + 0.18f, fz + 0.055f), new Vector3(s.x + 0.1f, 0.05f, 0.008f));
            for (int f = -1; f <= 1; f += 2)
            {
                red.Cylinder(new Vector3(-3.6f, fy + 0.02f, f * (fz + 0.06f)), new Vector3(-3.6f, fy + 0.02f, f * (fz + 0.075f)), 0.42f, 0.42f, 24);
                PropGeo.Poly(white, new Vector3(-3.6f, fy, 0f), null, new Vector3(-3.75f, fy - 0.1f, f * (fz + 0.077f)), new Vector3(-3.45f, fy - 0.1f, f * (fz + 0.077f)), new Vector3(-3.6f, fy + 0.25f, f * (fz + 0.077f)));
                white.Sphere(new Vector3(-3.6f, fy - 0.06f, f * (fz + 0.076f)), new Vector3(0.15f, 0.15f, 0.002f), 1);
            }
            // Deck: soffit with light panels underneath, membrane on top inside the fascia.
            MeshBuilder soffit = kit[Mat(Surface.PaintedMetal, new Color(0.72f, 0.72f, 0.69f))];
            PropGeo.Panel(soffit, new Vector3(0f, under, 0f), Vector3.right, Vector3.forward, Vector3.down, -fx + 0.05f, fx - 0.05f, -fz + 0.05f, fz - 0.05f, null, 1.5f, 1.5f,
                PropShades.Solid(new Color(0.85f, 0.85f, 0.85f, 0.85f)));
            PropGeo.Panel(kit[Mat(Surface.Asphalt, new Color(0.4f, 0.4f, 0.39f))], new Vector3(0f, top - 0.1f, 0f), Vector3.right, Vector3.forward, Vector3.up, -fx + 0.05f, fx - 0.05f, -fz + 0.05f, fz - 0.05f,
                null, 1.5f, 1.5f, PropShades.Roof(813, top, top + 0.1f, 0.5f));
            MeshBuilder lights = kit[Mat(Surface.Plain, new Color(0.86f, 0.86f, 0.82f), 0.6f)];
            for (int i = 0; i < 6; i++)
            {
                float lx = (i % 3 - 1) * 3.6f, lz = (i / 3 - 0.5f) * 3.2f;
                bool broken = i == 4;
                lights.Color = broken ? Tone(0.3f) : Tone(1f);
                lights.Box(new Vector3(lx, under - 0.04f, lz), new Vector3(0.9f, 0.06f, 0.9f), broken ? Quaternion.Euler(18f, 0f, 6f) : (Quaternion?)null, 0.01f);
            }
        }

        // ================================================================== substation

        private static void Transformer(Transform t, Vector3 s)
        {
            PropKit.Spawn(t, PropKit.Cached($"prop:transformer:{s.x:0.##}x{s.y:0.##}x{s.z:0.##}", kit => TransformerKit(kit, s)));
            ColliderBox(t, "Body", new Vector3(0, s.y * 0.4f, 0), new Vector3(s.x, s.y * 0.8f, s.z));
        }

        private static void TransformerKit(PropKit kit, Vector3 s)
        {
            MeshBuilder plinth = kit[Concrete];
            plinth.Color = Tone(0.88f);
            plinth.Box(new Vector3(0f, 0.05f, 0f), new Vector3(s.x * 0.98f, 0.5f, s.z * 0.98f), null, 0.03f);
            MeshBuilder tank = kit[Mat(Surface.PaintedMetal, new Color(0.38f, 0.44f, 0.4f))];
            PropShade rusty = PropShades.Rusty(901, 0.3f, 0.5f);
            float tw = s.x * 0.58f, td = s.z * 0.6f, tb = 0.3f, th = s.y * 0.56f;
            PropGeo.Panel(tank, new Vector3(-tw * 0.5f, tb, td * 0.5f), Vector3.right, Vector3.up, Vector3.forward, 0f, tw, 0f, th, null, 0.4f, 0.4f, rusty);
            PropGeo.Panel(tank, new Vector3(-tw * 0.5f, tb, -td * 0.5f), Vector3.right, Vector3.up, Vector3.back, 0f, tw, 0f, th, null, 0.4f, 0.4f, rusty);
            PropGeo.Panel(tank, new Vector3(-tw * 0.5f, tb, -td * 0.5f), Vector3.forward, Vector3.up, Vector3.left, 0f, td, 0f, th, null, 0.4f, 0.4f, rusty);
            PropGeo.Panel(tank, new Vector3(tw * 0.5f, tb, -td * 0.5f), Vector3.forward, Vector3.up, Vector3.right, 0f, td, 0f, th, null, 0.4f, 0.4f, rusty);
            float top = tb + th;
            tank.Color = Tone(0.92f);
            tank.Box(new Vector3(0f, top + 0.03f, 0f), new Vector3(tw + 0.1f, 0.06f, td + 0.1f), null, 0.015f);
            tank.Box(new Vector3(0f, tb + 0.04f, 0f), new Vector3(tw + 0.06f, 0.08f, td + 0.06f), null, 0.015f);
            // Radiator banks on both sides.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float x0 = sx * tw * 0.5f;
                int fins = Mathf.Max(5, Mathf.RoundToInt(td / 0.11f));
                for (int i = 0; i < fins; i++)
                {
                    float z = -td * 0.5f + 0.08f + i * (td - 0.16f) / (fins - 1);
                    tank.Color = Tone(0.85f + (i % 2) * 0.05f);
                    tank.Box(new Vector3(x0 + sx * 0.24f, tb + th * 0.5f, z), new Vector3(0.42f, th * 0.82f, 0.03f));
                }
                for (int k = 0; k < 2; k++)
                {
                    float y = tb + th * (k == 0 ? 0.12f : 0.88f);
                    tank.Cylinder(new Vector3(x0 + sx * 0.24f, y, -td * 0.5f), new Vector3(x0 + sx * 0.24f, y, td * 0.5f), 0.05f, 0.05f, 8);
                }
            }
            // Conservator tank on brackets.
            float cy = top + 0.45f;
            tank.Color = Tone(0.9f);
            tank.Capsule(new Vector3(-tw * 0.32f, cy, -td * 0.25f), new Vector3(tw * 0.32f, cy, -td * 0.25f), 0.2f, 12);
            for (int sx = -1; sx <= 1; sx += 2) tank.Box(new Vector3(sx * tw * 0.25f, top + 0.2f, -td * 0.25f), new Vector3(0.06f, 0.4f, 0.12f));
            // Porcelain bushings with terminals.
            MeshBuilder porcelain = kit[Mat(Surface.Plain, new Color(0.45f, 0.25f, 0.16f), 0.7f)];
            porcelain.Color = Tone(1f);
            Matrix4x4 flip = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
            for (int i = -1; i <= 1; i++)
            {
                Vector3 b = new Vector3(i * tw * 0.3f, top + 0.06f, td * 0.15f);
                porcelain.Append(MeshFactory.PropInsulatorString(0.75f, 0.1f), Matrix4x4.Translate(b) * flip);
                kit[Steel].Color = Tone(0.9f);
                kit[Steel].Cylinder(b + Vector3.up * 0.75f, b + Vector3.up * 0.86f, 0.035f, 0.035f, 8);
                Vector3 lv = new Vector3(i * tw * 0.22f, top + 0.06f, td * 0.42f);
                porcelain.Append(MeshFactory.PropInsulatorString(0.3f, 0.06f), Matrix4x4.Translate(lv) * flip);
            }
            // Warning sign and rating plate, a drain valve.
            Warning(kit, new Vector3(0f, tb + th * 0.6f, td * 0.5f + 0.005f), Quaternion.identity, 0.32f);
            kit[Steel].Box(new Vector3(tw * 0.3f, tb + th * 0.35f, td * 0.5f + 0.005f), new Vector3(0.22f, 0.14f, 0.008f));
            kit[DarkMetal].Color = Tone(0.9f);
            kit[DarkMetal].Cylinder(new Vector3(-tw * 0.3f, tb + 0.2f, td * 0.5f), new Vector3(-tw * 0.3f, tb + 0.2f, td * 0.5f + 0.12f), 0.04f, 0.04f, 8);
            kit[DarkMetal].Cylinder(new Vector3(-tw * 0.3f, tb + 0.2f, td * 0.5f + 0.12f), new Vector3(-tw * 0.3f, tb + 0.2f, td * 0.5f + 0.15f), 0.07f, 0.07f, 8);
        }

        private static void Pylon(Transform t, Vector3 s)
        {
            const float baseHalf = 1.6f, topHalf = 0.4f;
            PropKit.Spawn(t, PropKit.Cached($"prop:pylon:{s.y:0.##}", kit => PylonKit(kit, s.y, baseHalf, topHalf)));
            foreach (Vector3 c in new[] { new Vector3(-baseHalf, 0, -baseHalf), new Vector3(baseHalf, 0, -baseHalf), new Vector3(-baseHalf, 0, baseHalf), new Vector3(baseHalf, 0, baseHalf) })
            {
                Vector3 top = new Vector3(c.x * 0.25f, s.y, c.z * 0.25f);
                ColliderBox(t, "Leg", (c + top) * 0.5f, new Vector3(0.18f, (top - c).magnitude, 0.18f), Quaternion.FromToRotation(Vector3.up, (top - c).normalized));
            }
        }

        private static void PylonKit(PropKit kit, float h, float b0, float bt)
        {
            MeshBuilder steel = kit[Mat(Surface.Metal, new Color(0.54f, 0.56f, 0.57f), 0.32f, 0.65f)];
            Vector3 Leg(int sx, int sz, float y)
            {
                float half = Mathf.Lerp(b0, bt, y / h);
                return new Vector3(sx * half, y, sz * half);
            }
            int[,] corners = { { -1, -1 }, { 1, -1 }, { 1, 1 }, { -1, 1 } };
            steel.Color = Tone(0.9f);
            for (int c = 0; c < 4; c++)
            {
                PropGeo.Beam(steel, Leg(corners[c, 0], corners[c, 1], -0.3f), Leg(corners[c, 0], corners[c, 1], h), 0.15f, 0.15f, new Vector3(corners[c, 0], 0f, corners[c, 1]));
                kit[Concrete].Color = Tone(0.85f);
                kit[Concrete].Box(Leg(corners[c, 0], corners[c, 1], 0f) + Vector3.up * 0.1f, new Vector3(0.55f, 0.45f, 0.55f), null, 0.03f);
            }
            const int levels = 8;
            var ys = new float[levels + 1];
            for (int i = 0; i <= levels; i++) ys[i] = h * (1f - Mathf.Pow(1f - (float)i / levels, 1.25f));
            steel.Color = Tone(0.85f);
            for (int i = 0; i <= levels; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int n = (c + 1) % 4;
                    Vector3 a = Leg(corners[c, 0], corners[c, 1], ys[i]), bb = Leg(corners[n, 0], corners[n, 1], ys[i]);
                    if (i > 0) PropGeo.Beam(steel, a, bb, 0.06f, 0.06f, Vector3.up);
                    if (i == levels) continue;
                    Vector3 a2 = Leg(corners[c, 0], corners[c, 1], ys[i + 1]), b2 = Leg(corners[n, 0], corners[n, 1], ys[i + 1]);
                    Vector3 face = ((a + bb) * 0.5f - new Vector3(0f, ys[i], 0f)).normalized;
                    PropGeo.Beam(steel, a, b2, 0.05f, 0.05f, face);
                    PropGeo.Beam(steel, bb, a2, 0.05f, 0.05f, face);
                }
            }
            // Peak for the earth wire.
            for (int c = 0; c < 4; c++) PropGeo.Beam(steel, Leg(corners[c, 0], corners[c, 1], h), new Vector3(0f, h + 1.3f, 0f), 0.07f, 0.07f, Vector3.up);
            // Two crossarms, insulator strings at the tips.
            MeshBuilder glass = kit[Mat(Surface.Glass, new Color(0.32f, 0.42f, 0.4f), 0.8f)];
            glass.Color = Tone(1f);
            float[] armY = { h * 0.84f, h * 0.64f };
            float[] armLen = { 2.6f, 1.95f };
            for (int a = 0; a < 2; a++)
            {
                float y = armY[a];
                float half = Mathf.Lerp(b0, bt, y / h);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Vector3 tip = new Vector3(sx * (half + armLen[a]), y, 0f);
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        PropGeo.Beam(steel, new Vector3(sx * half, y, sz * half), tip, 0.06f, 0.06f, Vector3.up);
                        PropGeo.Beam(steel, new Vector3(sx * half, y + 0.7f, sz * half * 0.9f), tip + Vector3.up * 0.06f, 0.05f, 0.05f, Vector3.up);
                        int braces = 3;
                        for (int k = 1; k < braces; k++)
                        {
                            float f = (float)k / braces;
                            Vector3 low = Vector3.Lerp(new Vector3(sx * half, y, sz * half), tip, f);
                            Vector3 high = Vector3.Lerp(new Vector3(sx * half, y + 0.7f, sz * half * 0.9f), tip, f + 0.5f / braces);
                            PropGeo.Beam(steel, low, high, 0.04f, 0.04f, Vector3.up);
                        }
                    }
                    glass.Append(MeshFactory.PropInsulatorString(1.15f, 0.11f), Matrix4x4.Translate(tip + Vector3.down * 0.04f));
                    steel.Box(tip + Vector3.down * 1.25f, new Vector3(0.1f, 0.12f, 0.3f), null, 0.02f);
                }
            }
            // Anti-climb guard and a danger plate.
            for (int c = 0; c < 4; c++)
            {
                int n = (c + 1) % 4;
                PropGeo.Beam(steel, Leg(corners[c, 0], corners[c, 1], 3.2f) + Vector3.up * 0.2f, Leg(corners[n, 0], corners[n, 1], 3.2f) + Vector3.up * 0.2f, 0.03f, 0.2f, Vector3.up);
            }
            Vector3 plate = Leg(1, 1, 2.2f);
            kit[WhitePaint].Color = Tone(0.95f);
            kit[WhitePaint].Box(plate + new Vector3(-0.2f, 0f, 0.1f), new Vector3(0.3f, 0.4f, 0.01f));
            Warning(kit, plate + new Vector3(-0.2f, 0.05f, 0.107f), Quaternion.identity, 0.2f);
        }

        // ================================================================== quarry, farm and roadside

        private static void Cliff(Transform t, Vector3 s, int seed)
        {
            List<PropKit.Piece> pieces = PropKit.Cached($"prop:cliff:{s.x:0.##}x{s.y:0.##}x{s.z:0.##}:{seed}", kit => CliffKit(kit, s, seed));
            PropKit.Spawn(t, pieces);
            PropKit.Piece body = pieces.Find(p => p.Name == "Rock");
            if (body == null) return;
            var col = new GameObject("Rock");
            col.transform.SetParent(t, false);
            col.AddComponent<MeshCollider>().sharedMesh = body.Mesh;
        }

        /// <summary>
        /// A two-sided ridge of quarried rock: thick horizontal beds broken into blocks along their
        /// length, each bed stepping back a little above the one below, a rough mossy crest, talus and a
        /// few cut blocks at the foot.
        /// </summary>
        private static void CliffKit(PropKit kit, Vector3 s, int seed)
        {
            Material rockMat = Mat(PropSurfaces.QuarryRock, new Color(0.66f, 0.62f, 0.56f));
            MeshBuilder rock = kit.In(rockMat, "Rock");
            float halfL = s.x * 0.5f, halfD = s.z * 0.5f, height = s.y;
            const float bottom = -1.6f;
            // Bed boundaries are shared along the whole ridge, so layers read as continuous strata.
            var beds = new List<float> { bottom };
            for (int i = 0; beds[beds.Count - 1] < height * 1.3f + 1f; i++) beds.Add(beds[beds.Count - 1] + 1.0f + PropGeo.Hash(seed, i) * 1.3f);
            int bedCount = beds.Count - 1;
            var bedTone = new float[bedCount];
            var bedWarm = new float[bedCount];
            var bedStep = new float[bedCount];
            var blockLen = new float[bedCount];
            for (int i = 0; i < bedCount; i++)
            {
                bedTone[i] = 0.8f + PropGeo.Hash(seed + 1, i) * 0.26f;
                bedWarm[i] = PropGeo.Hash(seed + 2, i);
                bedStep[i] = 0.06f + PropGeo.Hash(seed + 4, i) * 0.22f;
                blockLen[i] = 2.2f + PropGeo.Hash(seed + 5, i) * 3.5f;
            }
            int sections = Mathf.Max(8, Mathf.RoundToInt(s.x / 0.9f));
            var profiles = new List<Vector3[]>();
            var colours = new List<Color[]>();
            for (int k = 0; k <= sections; k++)
            {
                float x = -halfL + s.x * k / sections;
                float taper = PropGeo.Smooth(0f, 5f, Mathf.Min(x + halfL, halfL - x));
                float topY = height * (0.82f + 0.22f * PropGeo.Fbm(new Vector3(x * 0.08f, 0f, 3.1f), seed)) * Mathf.Lerp(0.2f, 1f, taper);
                float depth = halfD * Mathf.Lerp(0.45f, 1f, taper);
                var pts = new List<Vector3>();
                for (int side = 1; side >= -1; side -= 2)
                {
                    // Every section has the same number of points: beds above the crest collapse onto it.
                    var face = new List<Vector3>();
                    float d = depth;
                    Vector3 last = new Vector3(x, bottom, side * depth);
                    for (int i = 0; i < bedCount; i++)
                    {
                        float y0 = beds[i], y1 = Mathf.Min(beds[i + 1], topY);
                        if (y0 >= topY - 0.1f)
                        {
                            face.Add(last);
                            face.Add(last);
                            face.Add(last);
                            continue;
                        }
                        int block = Mathf.FloorToInt((x + halfL + PropGeo.Hash(seed + 6, i) * blockLen[i]) / blockLen[i]);
                        float roll = PropGeo.Hash(seed + 7 + side, i * 131 + block);
                        float blockOff = (roll - 0.5f) * 0.6f - (roll < 0.12f ? 0.45f : 0f);
                        if (i > 0) d -= bedStep[i];
                        float face0 = Mathf.Max(0.35f, d + blockOff + (PropGeo.Noise(new Vector3(x * 0.3f, i * 2.7f, side * 4f), seed + 3) - 0.5f) * 0.2f);
                        float lean = 0.04f + PropGeo.Hash(seed + 8, i) * 0.1f;
                        face.Add(new Vector3(x, y0 + 0.04f, side * (face0 - 0.14f)));
                        face.Add(new Vector3(x, y0 + 0.16f, side * face0));
                        last = new Vector3(x, y1 - 0.02f, side * Mathf.Max(0.3f, face0 - lean));
                        face.Add(last);
                    }
                    if (side > 0)
                    {
                        pts.AddRange(face);
                        // Rounded, uneven crest across to the back face.
                        for (int j = 1; j <= 3; j++)
                        {
                            float z = Mathf.Lerp(last.z, -last.z, j / 4f);
                            float bump = (PropGeo.Noise(new Vector3(x * 0.4f, j * 2.3f, 0f), seed + 9) - 0.4f) * 0.5f;
                            pts.Add(new Vector3(x, topY + 0.25f + bump, z * 0.9f));
                        }
                    }
                    else
                    {
                        face.Reverse();
                        pts.AddRange(face);
                    }
                }
                var cols = new Color[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 p = pts[i];
                    int bed = 0;
                    while (bed + 1 < bedCount && p.y >= beds[bed + 1]) bed++;
                    float tone = bedTone[bed] * (0.9f + 0.16f * PropGeo.Noise(p * 0.6f, seed + 10));
                    float warm = bedWarm[bed];
                    float ao = Mathf.Lerp(0.5f, 1f, PropGeo.Smooth(-0.6f, 1.4f, p.y));
                    Color c = new Color(tone * (1f + warm * 0.1f), tone * (1f + warm * 0.02f), tone * (1f - warm * 0.1f), ao);
                    if (p.y > topY - 0.05f) c = new Color(c.r * 0.72f, c.g * 0.86f, c.b * 0.55f, c.a);
                    cols[i] = c;
                }
                profiles.Add(pts.ToArray());
                colours.Add(cols);
            }
            // Skin consecutive sections with flat facets. Each section runs counter-clockwise in (z, y),
            // so an edge (dz, dy) faces (dy, -dz).
            for (int k = 0; k < sections; k++)
            {
                Vector3[] a = profiles[k], c = profiles[k + 1];
                Color[] ca = colours[k], cc = colours[k + 1];
                int n = a.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 edge = (a[j] - a[i]) + (c[j] - c[i]);
                    Vector3 facing = new Vector3(0f, -edge.z, edge.y);
                    if (facing.sqrMagnitude < 1e-8f) continue;
                    PropGeo.Quad(rock, a[i], c[i], c[j], a[j], facing, ca[i], cc[i], cc[j], ca[j]);
                }
            }
            for (int e = 0; e <= sections; e += sections)
            {
                Vector3[] p = profiles[e];
                Vector3 centre = Vector3.zero;
                foreach (Vector3 q in p) centre += q;
                centre /= p.Length;
                Vector3 outward = e == 0 ? Vector3.left : Vector3.right;
                Color cm = colours[e][p.Length / 2];
                for (int i = 0; i < p.Length; i++)
                {
                    int j = (i + 1) % p.Length;
                    if ((p[j] - p[i]).sqrMagnitude < 1e-8f) continue;
                    PropGeo.Quad(rock, centre, p[i], p[j], centre, outward, cm, colours[e][i], colours[e][j], cm);
                }
            }
            // Talus and fallen blocks along both feet (render only).
            MeshBuilder talus = kit.In(rockMat, "Talus");
            int count = Mathf.RoundToInt(s.x * 1.1f);
            for (int i = 0; i < count; i++)
            {
                float x = -halfL + 1f + PropGeo.Hash(seed + 11, i) * (s.x - 2f);
                int side = PropGeo.Hash(seed + 12, i) < 0.5f ? 1 : -1;
                float size = 0.35f + Mathf.Pow(PropGeo.Hash(seed + 13, i), 2.2f) * 1.6f;
                float z = side * (halfD * 0.9f + PropGeo.Hash(seed + 14, i) * 2.2f);
                float tone = 0.72f + PropGeo.Hash(seed + 15, i) * 0.3f;
                talus.Append(MeshFactory.PropStone(i % 12), Matrix4x4.TRS(new Vector3(x, size * 0.12f, z), Quaternion.Euler(PropGeo.Hash(seed + 16, i) * 40f, PropGeo.Hash(seed + 17, i) * 360f, 0f),
                    new Vector3(size, size * 0.75f, size * 0.9f)), new Color(tone, tone * 0.97f, tone * 0.93f, 0.85f));
            }
            for (int i = 0; i < 4; i++)
            {
                float x = -halfL + 3f + PropGeo.Hash(seed + 18, i) * (s.x - 6f);
                int side = i % 2 == 0 ? 1 : -1;
                var size = new Vector3(1.2f + PropGeo.Hash(seed + 19, i) * 0.8f, 0.7f + PropGeo.Hash(seed + 20, i) * 0.4f, 0.9f + PropGeo.Hash(seed + 21, i) * 0.5f);
                talus.Color = Tone(0.85f + PropGeo.Hash(seed + 22, i) * 0.2f, 0.9f);
                talus.Box(new Vector3(x, size.y * 0.45f, side * (halfD + 1.4f + PropGeo.Hash(seed + 23, i))), size, Quaternion.Euler(0f, PropGeo.Hash(seed + 24, i) * 50f - 25f, PropGeo.Hash(seed + 25, i) * 6f), 0.04f);
            }
        }

        private static void Silo(Transform t, Vector3 s)
        {
            PropKit.Spawn(t, PropKit.Cached($"prop:silo:{s.x:0.##}x{s.y:0.##}", kit => SiloKit(kit, s.x * 0.5f, s.y)));
            CapsuleCollider(t, "Body", Vector3.zero, s.y, s.x * 0.5f);
        }

        private static void SiloKit(PropKit kit, float r, float h)
        {
            Material galv = Mat(Surface.Metal, new Color(0.6f, 0.61f, 0.6f), 0.36f, 0.65f);
            MeshBuilder body = kit[galv];
            PropShade weather = PropShades.Rusty(951, 0.3f, 0.75f);
            // Ribbed shell: alternating radii with smooth normals, a small lip at each ring seam.
            const int ribs = 40, perRib = 4;
            int seg = ribs * perRib;
            var ys = new List<float>();
            float ring = (h - 0.3f) / 8f;
            for (int i = 0; i <= 8; i++)
            {
                float y = 0.3f + i * ring;
                if (i > 0) ys.Add(y - 0.04f);
                ys.Add(y);
                if (i < 8) ys.Add(y + 0.04f);
            }
            int start = body.VertexCount;
            for (int j = 0; j < ys.Count; j++)
            {
                float y = ys[j];
                bool seam = Mathf.Abs((y - 0.3f) / ring - Mathf.Round((y - 0.3f) / ring)) < 0.01f;
                for (int k = 0; k <= seg; k++)
                {
                    float a = k * Mathf.PI * 2f / seg;
                    float phase = (float)k / perRib * Mathf.PI * 2f;
                    float rr = r + Mathf.Cos(phase) * 0.025f + (seam ? 0.02f : 0f);
                    float drda = -Mathf.Sin(phase) * 0.025f * ribs;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), tan = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    Vector3 n = (dir * rr - tan * drda).normalized;
                    Vector3 p = dir * rr + Vector3.up * y;
                    body.Vertex(p, n, weather(p, n));
                }
            }
            int row = seg + 1;
            for (int j = 0; j + 1 < ys.Count; j++)
                for (int k = 0; k < seg; k++)
                {
                    int a = start + j * row + k, b = a + row;
                    body.Triangle(a, b, b + 1);
                    body.Triangle(a, b + 1, a + 1);
                }
            // Footing, eave ring, conical roof with radial ribs, vent cap.
            MeshBuilder concrete = kit[Concrete];
            concrete.Color = Tone(0.85f);
            concrete.Cylinder(new Vector3(0f, -0.4f, 0f), new Vector3(0f, 0.32f, 0f), r + 0.3f, r + 0.25f, 28);
            body.Color = Tone(0.85f);
            body.Cylinder(new Vector3(0f, h - 0.04f, 0f), new Vector3(0f, h + 0.04f, 0f), r + 0.06f, r + 0.06f, 32);
            MeshBuilder roof = kit[Mat(Surface.Metal, new Color(0.56f, 0.57f, 0.56f), 0.3f, 0.6f)];
            roof.Color = Tone(0.92f);
            float capH = r * 0.75f, vent = 0.42f;
            roof.Lathe(new[] { new Vector2(r + 0.18f, h - 0.06f), new Vector2(r + 0.16f, h + 0.02f), new Vector2(vent, h + capH), new Vector2(vent, h + capH + 0.25f), new Vector2(0f, h + capH + 0.25f) }, 32, false);
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                PropGeo.Beam(roof, d * (r + 0.12f) + Vector3.up * (h + 0.05f), d * (vent + 0.02f) + Vector3.up * (h + capH + 0.01f), 0.04f, 0.035f, d + Vector3.up);
            }
            roof.Cylinder(new Vector3(0f, h + capH + 0.25f, 0f), new Vector3(0f, h + capH + 0.32f, 0f), 0.5f, 0.5f, 16);
            roof.Cylinder(new Vector3(0f, h + capH + 0.32f, 0f), new Vector3(0f, h + capH + 0.6f, 0f), 0.52f, 0.0f, 16, false);
            // Caged ladder up the side and onto the roof.
            MeshBuilder steel = kit[Steel];
            steel.Color = Tone(0.85f);
            float la = 50f * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Sin(la), 0f, Mathf.Cos(la)), side = Vector3.Cross(Vector3.up, radial).normalized;
            Vector3 baseP = radial * (r + 0.2f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                PropGeo.Rod(steel, baseP + side * (sx * 0.22f) + Vector3.up * 0.3f, baseP + side * (sx * 0.22f) + Vector3.up * (h + 0.6f), 0.02f, 6);
                for (int k = 0; k < 6; k++)
                {
                    float y = 0.8f + k * (h - 0.8f) / 5f;
                    PropGeo.Rod(steel, radial * (r + 0.03f) + side * (sx * 0.22f) + Vector3.up * y, baseP + side * (sx * 0.22f) + Vector3.up * y, 0.012f, 4);
                }
            }
            for (float y = 0.6f; y < h + 0.5f; y += 0.3f) PropGeo.Rod(steel, baseP - side * 0.22f + Vector3.up * y, baseP + side * 0.22f + Vector3.up * y, 0.012f, 4);
            // Safety cage: half hoops around the ladder and vertical bars.
            for (float y = 2.4f; y < h + 0.4f; y += 0.9f)
            {
                Vector3 prev = baseP + side * 0.38f + Vector3.up * y;
                for (int k = 1; k <= 8; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 p = baseP + (side * Mathf.Cos(a) + radial * Mathf.Sin(a)) * 0.38f + Vector3.up * y;
                    PropGeo.Rod(steel, prev, p, 0.012f, 4, false);
                    prev = p;
                }
            }
            for (int k = 1; k <= 3; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 off = (side * Mathf.Cos(a) + radial * Mathf.Sin(a)) * 0.38f;
                PropGeo.Rod(steel, baseP + off + Vector3.up * 2.4f, baseP + off + Vector3.up * (h + 0.3f), 0.012f, 4);
            }
            // Roof ladder up the cone.
            for (int sx = -1; sx <= 1; sx += 2)
                PropGeo.Rod(steel, radial * (r + 0.1f) + side * (sx * 0.2f) + Vector3.up * (h + 0.12f), radial * (vent + 0.08f) + side * (sx * 0.2f) + Vector3.up * (h + capH + 0.05f), 0.015f, 4);
            // Inspection hatch at the bottom.
            Vector3 hatchDir = new Vector3(Mathf.Sin(-0.5f), 0f, Mathf.Cos(-0.5f));
            body.Color = Tone(0.8f);
            body.Box(hatchDir * (r + 0.03f) + Vector3.up * 0.85f, new Vector3(0.7f, 0.8f, 0.05f), Quaternion.LookRotation(hatchDir), 0.015f);
            kit[DarkMetal].Color = Tone(0.9f);
            kit[DarkMetal].Box(hatchDir * (r + 0.07f) + Vector3.up * 0.85f, new Vector3(0.08f, 0.15f, 0.04f), Quaternion.LookRotation(hatchDir));
        }

        private static void OldPole(Transform t, Vector3 s, int variant)
        {
            PropKit.Spawn(t, PropKit.Cached($"prop:pole:{s.y:0.##}:{variant}", kit => PoleKit(kit, s.y, variant)));
            CapsuleCollider(t, "Pole", Vector3.zero, s.y, 0.15f);
        }

        private static void PoleKit(PropKit kit, float h, int v)
        {
            MeshBuilder wood = kit[Mat(Surface.Wood, new Color(0.36f, 0.3f, 0.23f))];
            Vector3 lean = new Vector3((PropGeo.Hash(1201, v) - 0.5f) * 0.35f, 0f, (PropGeo.Hash(1202, v) - 0.5f) * 0.3f);
            Vector3 top = Vector3.up * h + lean;
            Vector3 At(float y) => Vector3.up * y + lean * (y / h);
            wood.Color = Tone(0.9f);
            wood.Cylinder(Vector3.down * 0.4f, At(0.6f), 0.165f, 0.16f, 9, true, false, false);
            wood.Color = Tone(1f);
            wood.Cylinder(At(0.6f), top, 0.16f, 0.12f, 9, true, false, true);
            kit[DarkMetal].Color = Tone(0.9f);
            kit[DarkMetal].Cylinder(top, top + Vector3.up * 0.04f, 0.13f, 0.1f, 9);
            // Crossarm with braces and pin insulators (one variant has a broken, hanging arm).
            float ay = h - 0.6f;
            Vector3 arm = At(ay) + new Vector3(0f, 0f, 0.15f);
            Quaternion armRot = v == 2 ? Quaternion.Euler(0f, 0f, 28f) : Quaternion.identity;
            wood.Color = Tone(0.85f);
            wood.Box(arm, new Vector3(1.9f, 0.1f, 0.1f), armRot, 0.01f);
            MeshBuilder iron = kit[DarkMetal];
            for (int sx = -1; sx <= 1; sx += 2)
                PropGeo.Beam(iron, arm + armRot * new Vector3(sx * 0.55f, -0.05f, 0f), At(ay - 0.6f) + new Vector3(0f, 0f, 0.15f), 0.04f, 0.01f, Vector3.forward);
            MeshBuilder glass = kit[Mat(Surface.Glass, new Color(0.3f, 0.48f, 0.42f), 0.8f)];
            glass.Color = Tone(1f);
            float[] pins = { -0.8f, -0.45f, 0.62f };
            for (int i = 0; i < pins.Length; i++)
            {
                Vector3 p = arm + armRot * new Vector3(pins[i], 0.05f, 0f);
                iron.Cylinder(p, p + Vector3.up * 0.08f, 0.015f, 0.015f, 6);
                glass.Matrix = Matrix4x4.TRS(p + Vector3.up * 0.06f, Quaternion.identity, Vector3.one);
                glass.Lathe(new[] { new Vector2(0.075f, 0f), new Vector2(0.08f, 0.04f), new Vector2(0.05f, 0.08f), new Vector2(0.055f, 0.12f), new Vector2(0.03f, 0.15f), new Vector2(0f, 0.16f) }, 10);
                glass.Matrix = Matrix4x4.identity;
            }
            if (v == 1)
            {
                // Pole-top transformer can.
                Vector3 c = At(h - 1.8f) + new Vector3(0f, 0f, -0.42f);
                MeshBuilder can = kit[Mat(Surface.PaintedMetal, new Color(0.5f, 0.52f, 0.5f))];
                can.Color = Tone(0.9f);
                can.Cylinder(c, c + Vector3.up * 0.7f, 0.24f, 0.24f, 14);
                can.Cylinder(c + Vector3.up * 0.7f, c + Vector3.up * 0.76f, 0.26f, 0.2f, 14);
                iron.Box(c + new Vector3(0f, 0.35f, 0.24f), new Vector3(0.12f, 0.5f, 0.12f));
                kit[Mat(Surface.Plain, new Color(0.45f, 0.25f, 0.16f), 0.7f)].Append(MeshFactory.PropInsulatorString(0.18f, 0.05f), Matrix4x4.Translate(c + Vector3.up * 0.76f) * Matrix4x4.Scale(new Vector3(1f, -1f, 1f)));
            }
            if (v == 3)
            {
                // A snapped line hanging down the pole.
                PropGeo.Rope(iron, arm + new Vector3(0.62f, 0.12f, 0f), At(2.2f) + new Vector3(0.5f, 0f, 0.6f), 0.008f, 0.6f, 6);
            }
            // Step bolts.
            for (int i = 0; i < 6; i++)
            {
                float y = 2.4f + i * 0.45f;
                Vector3 d = Quaternion.Euler(0f, i % 2 == 0 ? 60f : 120f, 0f) * Vector3.right;
                PropGeo.Rod(iron, At(y) + d * 0.12f, At(y) + d * 0.3f, 0.012f, 4);
            }
        }
    }
}
