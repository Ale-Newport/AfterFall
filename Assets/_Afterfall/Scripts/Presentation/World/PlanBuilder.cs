using System;
using System.Collections.Generic;
using Afterfall.Domain.World;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Builds a multi-storey building from its <see cref="BuildingPlan"/>: brick exterior walls with
    /// plaster inside, window glazing, interior partitions with door openings, a concrete slab per
    /// floor with holes where the stairs rise, walkable stair flights (stepped visuals over an
    /// invisible ramp the NavMesh and the character controller use), guard rails, a flat roof with a
    /// parapet, floor finishes per room and the furniture decoration. Doors, containers and
    /// furniture with a use are structures placed from the same plan, so walls and doorways agree.
    /// </summary>
    internal static class PlanBuilder
    {
        private const float Bevel = 0.01f;

        private static Material Mat(Surface s, Color c, float? smooth = null, float? metal = null) =>
            MaterialLibrary.Surface(s, c, smooth, metal, true, 1f, 0f, null, false);

        private static Material Brick => Mat(Surface.Brick, new Color(0.6f, 0.36f, 0.28f));
        private static Material Plaster => Mat(Surface.Plaster, new Color(0.8f, 0.77f, 0.7f));
        private static Material Concrete => Mat(Surface.Concrete, new Color(0.56f, 0.55f, 0.52f));
        private static Material Tiles => Mat(PropSurfaces.FloorTiles, new Color(0.74f, 0.72f, 0.67f), 0.35f);
        private static Material Planks => Mat(Surface.Planks, new Color(0.56f, 0.41f, 0.27f));
        private static Material Ceiling => Mat(PropSurfaces.CeilingTiles, new Color(0.84f, 0.82f, 0.77f));
        private static Material Glass => Mat(Surface.Glass, new Color(0.17f, 0.21f, 0.23f), 0.9f);
        private static Material Steel => Mat(Surface.Metal, new Color(0.5f, 0.52f, 0.52f), 0.35f, 0.65f);
        private static Material Trim => Mat(Surface.PaintedMetal, new Color(0.72f, 0.7f, 0.64f));
        private static Material Wood => Mat(Surface.Wood, new Color(0.46f, 0.34f, 0.23f));
        private static Material Fabric => Mat(Surface.Fabric, new Color(0.36f, 0.42f, 0.5f));
        private static Material Porcelain => Mat(Surface.PaintedMetal, new Color(0.88f, 0.87f, 0.83f), 0.6f);

        public static void Build(Transform t, BuildingPlan plan)
        {
            if (plan == null) return;
            List<PropKit.Piece> pieces = PropKit.Cached("plan:" + plan.Id, kit => Visuals(kit, plan));
            PropKit.Spawn(t, pieces);
            Colliders(t, plan);
        }

        // ------------------------------------------------------------ geometry shared by visuals and colliders

        /// <summary>One solid rectangle of a wall: u along the wall from its start, v height above the origin.</summary>
        private struct Piece
        {
            public float U0, U1, V0, V1;
        }

        private static float WallBottom(BuildingPlan p, PlanWall w) => w.Floor == 0 ? (w.Exterior ? -0.75f : p.FloorY(0)) : p.FloorY(w.Floor) - (w.Exterior ? p.SlabThickness : 0f);

        private static float WallTop(BuildingPlan p, PlanWall w) => w.Exterior && w.Floor == p.Floors - 1 ? p.RoofY + p.ParapetHeight : p.FloorY(w.Floor + 1) - p.SlabThickness;

        private static List<Piece> Pieces(BuildingPlan p, PlanWall w)
        {
            float v0 = WallBottom(p, w), v1 = WallTop(p, w), floor = p.FloorY(w.Floor);
            var holes = new List<PlanOpening>(w.Openings);
            holes.Sort((a, b) => a.Center.CompareTo(b.Center));
            var list = new List<Piece>();
            float u = 0f;
            foreach (PlanOpening o in holes)
            {
                float a = o.Center - o.Width * 0.5f, b = o.Center + o.Width * 0.5f;
                if (a > u) list.Add(new Piece { U0 = u, U1 = a, V0 = v0, V1 = v1 });
                if (floor + o.Bottom > v0 + 0.01f) list.Add(new Piece { U0 = a, U1 = b, V0 = v0, V1 = floor + o.Bottom });
                if (floor + o.Top < v1 - 0.01f) list.Add(new Piece { U0 = a, U1 = b, V0 = floor + o.Top, V1 = v1 });
                u = b;
            }
            if (u < w.Length) list.Add(new Piece { U0 = u, U1 = w.Length, V0 = v0, V1 = v1 });
            return list;
        }

        /// <summary>Box centre and size for a wall piece, offset across the wall by <paramref name="offset"/> with thickness <paramref name="th"/>.</summary>
        private static void WallBox(PlanWall w, Piece pc, float offset, float th, out Vector3 center, out Vector3 size)
        {
            float uc = (pc.U0 + pc.U1) * 0.5f, len = pc.U1 - pc.U0, vc = (pc.V0 + pc.V1) * 0.5f, h = pc.V1 - pc.V0;
            if (w.AlongX)
            {
                center = new Vector3(Mathf.Min(w.X0, w.X1) + uc, vc, w.Z0 + offset);
                size = new Vector3(len, h, th);
            }
            else
            {
                center = new Vector3(w.X0 + offset, vc, Mathf.Min(w.Z0, w.Z1) + uc);
                size = new Vector3(th, h, len);
            }
        }

        /// <summary>Outward direction across an exterior wall (+1 or -1 along the wall's normal axis).</summary>
        private static float Outward(PlanWall w) => w.AlongX ? Mathf.Sign(w.Z0) : Mathf.Sign(w.X0);

        /// <summary>The rectangle minus holes, as boxes (strips between the holes' X edges).</summary>
        private static List<Rect> Subtract(Rect area, List<PlanHole> holes)
        {
            var xs = new List<float> { area.xMin, area.xMax };
            foreach (PlanHole h in holes)
            {
                xs.Add(Mathf.Clamp(Mathf.Min(h.X0, h.X1), area.xMin, area.xMax));
                xs.Add(Mathf.Clamp(Mathf.Max(h.X0, h.X1), area.xMin, area.xMax));
            }
            xs.Sort();
            var result = new List<Rect>();
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                float xa = xs[i], xb = xs[i + 1];
                if (xb - xa < 0.01f) continue;
                var cuts = new List<Vector2>();
                foreach (PlanHole h in holes)
                    if (Mathf.Min(h.X0, h.X1) <= xa + 0.001f && Mathf.Max(h.X0, h.X1) >= xb - 0.001f) cuts.Add(new Vector2(Mathf.Min(h.Z0, h.Z1), Mathf.Max(h.Z0, h.Z1)));
                cuts.Sort((a, b) => a.x.CompareTo(b.x));
                float z = area.yMin;
                foreach (Vector2 c in cuts)
                {
                    if (c.x > z + 0.01f) result.Add(Rect.MinMaxRect(xa, z, xb, Mathf.Min(c.x, area.yMax)));
                    z = Mathf.Max(z, c.y);
                }
                if (area.yMax > z + 0.01f) result.Add(Rect.MinMaxRect(xa, z, xb, area.yMax));
            }
            return result;
        }

        private static List<PlanHole> HolesOn(BuildingPlan p, int floor) => p.Holes.FindAll(h => h.Floor == floor);

        // ------------------------------------------------------------ visuals

        private static void Visuals(PropKit kit, BuildingPlan p)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            // Slabs: a foundation, one per upper floor (with stair holes) and the roof.
            for (int f = 0; f <= p.Floors; f++)
            {
                float top = p.FloorY(f), bottom = f == 0 ? -0.75f : top - p.SlabThickness;
                MeshBuilder slab = kit[Concrete];
                slab.Color = new Color(0.92f, 0.92f, 0.92f, 0.95f);
                foreach (Rect r in Subtract(Rect.MinMaxRect(-hx, -hz, hx, hz), HolesOn(p, f)))
                    slab.Box(new Vector3(r.center.x, (top + bottom) * 0.5f, r.center.y), new Vector3(r.width, top - bottom, r.height), null, Bevel);
                if (f > 0 && f < p.Floors)
                {
                    // A concrete band marks each floor line outside.
                    MeshBuilder band = kit[Concrete];
                    band.Color = new Color(0.8f, 0.8f, 0.78f, 1f);
                    band.Box(new Vector3(0f, top - p.SlabThickness * 0.5f, hz + 0.03f), new Vector3(p.Width + 0.08f, p.SlabThickness + 0.06f, 0.06f));
                    band.Box(new Vector3(0f, top - p.SlabThickness * 0.5f, -hz - 0.03f), new Vector3(p.Width + 0.08f, p.SlabThickness + 0.06f, 0.06f));
                    band.Box(new Vector3(hx + 0.03f, top - p.SlabThickness * 0.5f, 0f), new Vector3(0.06f, p.SlabThickness + 0.06f, p.Depth + 0.08f));
                    band.Box(new Vector3(-hx - 0.03f, top - p.SlabThickness * 0.5f, 0f), new Vector3(0.06f, p.SlabThickness + 0.06f, p.Depth + 0.08f));
                }
            }

            // Floor finishes and ceilings per room.
            foreach (PlanRoom room in p.Rooms)
            {
                Rect area = Rect.MinMaxRect(Mathf.Min(room.X0, room.X1), Mathf.Min(room.Z0, room.Z1), Mathf.Max(room.X0, room.X1), Mathf.Max(room.Z0, room.Z1));
                Material finish = room.Finish == "tiles" ? Tiles : room.Finish == "concrete" ? Concrete : Planks;
                MeshBuilder fb = kit[finish];
                fb.Color = new Color(1f, 1f, 1f, 0.9f);
                float y = p.FloorY(room.Floor);
                foreach (Rect r in Subtract(area, HolesOn(p, room.Floor)))
                    fb.Box(new Vector3(r.center.x, y + 0.006f, r.center.y), new Vector3(r.width, 0.012f, r.height));
                MeshBuilder cb = kit[Ceiling];
                cb.Color = new Color(1f, 1f, 1f, 0.85f);
                float cy = p.FloorY(room.Floor + 1) - p.SlabThickness - 0.006f;
                foreach (Rect r in Subtract(area, HolesOn(p, room.Floor + 1)))
                    cb.Box(new Vector3(r.center.x, cy, r.center.y), new Vector3(r.width, 0.012f, r.height));
            }

            // Walls: brick outside and plaster inside for the shell, plaster both sides inside.
            foreach (PlanWall w in p.Walls)
            {
                foreach (Piece pc in Pieces(p, w))
                {
                    if (w.Exterior)
                    {
                        float o = Outward(w);
                        WallBox(w, pc, o * (w.Thickness * 0.5f - 0.11f), 0.22f, out Vector3 c, out Vector3 s);
                        MeshBuilder brick = kit[Brick];
                        brick.Color = new Color(1f, 1f, 1f, 1f);
                        brick.Box(c, s, null, Bevel);
                        // The inner plaster stops at the slab above (the brick carries on to the parapet).
                        Piece inner = pc;
                        inner.V0 = Mathf.Max(pc.V0, p.FloorY(w.Floor));
                        inner.V1 = Mathf.Min(pc.V1, p.FloorY(w.Floor + 1) - p.SlabThickness);
                        if (inner.V1 > inner.V0 + 0.01f)
                        {
                            WallBox(w, inner, -o * (w.Thickness * 0.5f - 0.04f), 0.08f, out c, out s);
                            MeshBuilder pl = kit[Plaster];
                            pl.Color = new Color(1f, 1f, 1f, 0.9f);
                            pl.Box(c, s);
                        }
                    }
                    else
                    {
                        WallBox(w, pc, 0f, w.Thickness, out Vector3 c, out Vector3 s);
                        MeshBuilder pl = kit[Plaster];
                        pl.Color = new Color(0.97f, 0.97f, 0.97f, 0.9f);
                        pl.Box(c, s, null, Bevel);
                    }
                }
                foreach (PlanOpening o in w.Openings) OpeningTrim(kit, p, w, o);
            }

            foreach (PlanStair s in p.Stairs) Stair(kit, p, s);
            foreach (PlanRail r in p.Rails) Rail(kit, p.FloorY(r.Floor), new Vector3(r.X0, 0f, r.Z0), new Vector3(r.X1, 0f, r.Z1));
            Roof(kit, p);
            Entrance(kit, p);
            foreach (PlanDecor d in p.Decor) Decor(kit, p, d);
        }

        private static void OpeningTrim(PropKit kit, BuildingPlan p, PlanWall w, PlanOpening o)
        {
            float floor = p.FloorY(w.Floor);
            var pc = new Piece { U0 = o.Center - o.Width * 0.5f, U1 = o.Center + o.Width * 0.5f, V0 = floor + o.Bottom, V1 = floor + o.Top };
            MeshBuilder trim = kit[Trim];
            trim.Color = new Color(0.95f, 0.95f, 0.95f, 1f);
            if (o.Kind == PlanOpeningKind.Window)
            {
                // Glazing in the middle of the wall, a sill outside and a frame.
                WallBox(w, pc, 0f, 0.03f, out Vector3 c, out Vector3 s);
                kit[Glass].Color = new Color(1f, 1f, 1f, 1f);
                kit[Glass].Box(c, s);
                var sill = new Piece { U0 = pc.U0 - 0.06f, U1 = pc.U1 + 0.06f, V0 = pc.V0 - 0.06f, V1 = pc.V0 };
                WallBox(w, sill, w.Exterior ? Outward(w) * (w.Thickness * 0.5f) : 0f, w.Thickness + 0.1f, out c, out s);
                trim.Box(c, s, null, 0.008f);
                var mullion = new Piece { U0 = o.Center - 0.025f, U1 = o.Center + 0.025f, V0 = pc.V0, V1 = pc.V1 };
                WallBox(w, mullion, 0f, 0.06f, out c, out s);
                trim.Box(c, s);
                var transom = new Piece { U0 = pc.U0, U1 = pc.U1, V0 = pc.V1 - 0.45f, V1 = pc.V1 - 0.4f };
                WallBox(w, transom, 0f, 0.06f, out c, out s);
                trim.Box(c, s);
            }
            else
            {
                // A door casing around the opening on both faces.
                foreach (float side in new[] { -1f, 1f })
                {
                    float off = side * (w.Thickness * 0.5f + 0.01f);
                    WallBox(w, new Piece { U0 = pc.U0 - 0.07f, U1 = pc.U0, V0 = pc.V0, V1 = pc.V1 + 0.07f }, off, 0.03f, out Vector3 c, out Vector3 s);
                    trim.Box(c, s);
                    WallBox(w, new Piece { U0 = pc.U1, U1 = pc.U1 + 0.07f, V0 = pc.V0, V1 = pc.V1 + 0.07f }, off, 0.03f, out c, out s);
                    trim.Box(c, s);
                    WallBox(w, new Piece { U0 = pc.U0, U1 = pc.U1, V0 = pc.V1, V1 = pc.V1 + 0.07f }, off, 0.03f, out c, out s);
                    trim.Box(c, s);
                }
            }
        }

        private static void Stair(PropKit kit, BuildingPlan p, PlanStair s)
        {
            int steps = Mathf.Max(8, Mathf.RoundToInt(p.FloorHeight / 0.19f));
            float y0 = p.FloorY(s.FromFloor), rise = p.FloorHeight / steps;
            float dir = Mathf.Sign(s.ZEnd - s.ZStart), run = Mathf.Abs(s.ZEnd - s.ZStart), tread = run / steps;
            float xa = Mathf.Min(s.X0, s.X1), xb = Mathf.Max(s.X0, s.X1), xc = (xa + xb) * 0.5f, width = xb - xa - 0.04f;
            MeshBuilder concrete = kit[Concrete];
            MeshBuilder nosing = kit[Tiles];
            for (int i = 0; i < steps; i++)
            {
                float za = s.ZStart + dir * i * tread, zb = s.ZStart + dir * (i + 1) * tread;
                float top = y0 + (i + 1) * rise;
                concrete.Color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
                concrete.Box(new Vector3(xc, top - rise * 0.5f - 0.1f, (za + zb) * 0.5f), new Vector3(width, rise + 0.2f, tread), null, 0.004f);
                nosing.Color = new Color(0.85f, 0.85f, 0.85f, 1f);
                nosing.Box(new Vector3(xc, top + 0.005f, (za + zb) * 0.5f), new Vector3(width, 0.012f, tread + 0.01f));
            }
            // The sloped soffit under the flight.
            float len = Mathf.Sqrt(run * run + p.FloorHeight * p.FloorHeight);
            float angle = Mathf.Atan2(p.FloorHeight, run) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(dir > 0 ? -angle : angle, 0f, 0f);
            Vector3 mid = new Vector3(xc, y0 + p.FloorHeight * 0.5f - 0.32f, (s.ZStart + s.ZEnd) * 0.5f);
            concrete.Color = new Color(0.85f, 0.85f, 0.85f, 0.85f);
            concrete.Box(mid, new Vector3(width, 0.18f, len), rot);
            // Handrails on the sides that are not against an outer wall.
            float inner = p.Width * 0.5f - 0.35f;
            foreach (float x in new[] { xa, xb })
            {
                if (Mathf.Abs(x) > inner) continue;
                Vector3 a = new Vector3(x, y0, s.ZStart), b = new Vector3(x, y0 + p.FloorHeight, s.ZEnd);
                SlopedRail(kit, a, b);
            }
        }

        private static void SlopedRail(PropKit kit, Vector3 a, Vector3 b)
        {
            MeshBuilder steel = kit[Steel];
            steel.Color = new Color(0.9f, 0.9f, 0.9f, 1f);
            Vector3 top0 = a + Vector3.up * 0.95f, top1 = b + Vector3.up * 0.95f;
            Vector3 d = top1 - top0;
            steel.Box((top0 + top1) * 0.5f, new Vector3(0.05f, 0.05f, d.magnitude), Quaternion.LookRotation(d.normalized, Vector3.up));
            int posts = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / 1.1f));
            for (int i = 0; i <= posts; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, i / (float)posts);
                steel.Box(q + Vector3.up * 0.475f, new Vector3(0.035f, 0.95f, 0.035f));
            }
        }

        private static void Rail(PropKit kit, float y, Vector3 a, Vector3 b)
        {
            a.y = y;
            b.y = y;
            SlopedRail(kit, a, b);
            MeshBuilder steel = kit[Steel];
            Vector3 d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            // A mid rail as well on level guards.
            steel.Box((a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.03f, 0.03f, d.magnitude), Quaternion.LookRotation(d.normalized, Vector3.up));
        }

        private static void Roof(PropKit kit, BuildingPlan p)
        {
            float y = p.RoofY, hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            MeshBuilder tar = kit[Concrete];
            tar.Color = new Color(0.55f, 0.55f, 0.55f, 0.9f);
            tar.Box(new Vector3(0f, y + 0.02f, 0f), new Vector3(p.Width - 0.5f, 0.04f, p.Depth - 0.5f));
            MeshBuilder coping = kit[Trim];
            coping.Color = new Color(0.8f, 0.8f, 0.8f, 1f);
            float cy = p.TotalHeight + 0.03f;
            coping.Box(new Vector3(0f, cy, hz - 0.15f), new Vector3(p.Width + 0.04f, 0.06f, 0.38f));
            coping.Box(new Vector3(0f, cy, -hz + 0.15f), new Vector3(p.Width + 0.04f, 0.06f, 0.38f));
            coping.Box(new Vector3(hx - 0.15f, cy, 0f), new Vector3(0.38f, 0.06f, p.Depth));
            coping.Box(new Vector3(-hx + 0.15f, cy, 0f), new Vector3(0.38f, 0.06f, p.Depth));
            // Rooftop plant: an air handler, a water tank and a stair-hatch box.
            MeshBuilder steel = kit[Steel];
            steel.Color = new Color(0.85f, 0.85f, 0.85f, 1f);
            steel.Box(new Vector3(3.5f, y + 0.6f, -2f), new Vector3(2.2f, 1.2f, 1.4f), null, 0.03f);
            steel.Box(new Vector3(3.5f, y + 1.25f, -2f), new Vector3(1.6f, 0.1f, 1f));
            kit[Wood].Color = new Color(0.8f, 0.8f, 0.8f, 1f);
            kit[Wood].Box(new Vector3(-2.5f, y + 1.3f, 2.5f), new Vector3(1.8f, 2.4f, 1.8f), null, 0.04f);
            kit[Brick].Box(new Vector3(-6.2f, y + 1.1f, -1.5f), new Vector3(3.0f, 2.2f, 3.0f), null, Bevel);
        }

        private static void Entrance(PropKit kit, BuildingPlan p)
        {
            // A small concrete canopy and step over the front door(s) on the ground floor.
            foreach (PlanWall w in p.Walls)
            {
                if (!w.Exterior || w.Floor != 0) continue;
                foreach (PlanOpening o in w.Openings)
                {
                    if (o.Kind != PlanOpeningKind.Door) continue;
                    float outward = Outward(w);
                    var canopy = new Piece { U0 = o.Center - o.Width * 0.5f - 0.5f, U1 = o.Center + o.Width * 0.5f + 0.5f, V0 = p.FloorY(0) + o.Top + 0.3f, V1 = p.FloorY(0) + o.Top + 0.45f };
                    WallBox(w, canopy, outward * (w.Thickness * 0.5f + 0.6f), 1.2f, out Vector3 c, out Vector3 s);
                    kit[Concrete].Color = new Color(0.9f, 0.9f, 0.9f, 1f);
                    kit[Concrete].Box(c, s, null, 0.02f);
                    var step = new Piece { U0 = o.Center - o.Width * 0.5f - 0.3f, U1 = o.Center + o.Width * 0.5f + 0.3f, V0 = -0.3f, V1 = p.FloorY(0) - 0.005f };
                    WallBox(w, step, outward * (w.Thickness * 0.5f + 0.45f), 0.9f, out c, out s);
                    kit[Concrete].Box(c, s, null, 0.02f);
                }
            }
        }

        // ------------------------------------------------------------ decoration

        private static void Decor(PropKit kit, BuildingPlan p, PlanDecor d)
        {
            float y = p.FloorY(d.Floor);
            Quaternion r = Quaternion.Euler(0f, d.Yaw, 0f);
            Vector3 o = new Vector3(d.X, y, d.Z);
            void Box(Material m, Vector3 local, Vector3 size, float bevel = 0.01f, float shade = 1f)
            {
                MeshBuilder b = kit[m];
                b.Color = new Color(shade, shade, shade, 0.95f);
                b.Box(o + r * local, size, r, bevel);
            }
            switch (d.Kind)
            {
                case "sofa":
                    Box(Fabric, new Vector3(0f, 0.22f, 0f), new Vector3(2.0f, 0.44f, 0.85f), 0.06f);
                    Box(Fabric, new Vector3(0f, 0.6f, -0.33f), new Vector3(2.0f, 0.5f, 0.2f), 0.06f, 0.9f);
                    Box(Fabric, new Vector3(-0.93f, 0.55f, 0f), new Vector3(0.2f, 0.3f, 0.85f), 0.05f, 0.92f);
                    Box(Fabric, new Vector3(0.93f, 0.55f, 0f), new Vector3(0.2f, 0.3f, 0.85f), 0.05f, 0.92f);
                    break;
                case "table":
                case "dining":
                    Box(Wood, new Vector3(0f, 0.73f, 0f), new Vector3(1.4f, 0.05f, 0.8f), 0.01f);
                    foreach (float sx in new[] { -0.62f, 0.62f })
                        foreach (float sz in new[] { -0.33f, 0.33f })
                            Box(Wood, new Vector3(sx, 0.36f, sz), new Vector3(0.06f, 0.72f, 0.06f), 0.005f, 0.85f);
                    if (d.Kind == "dining")
                        foreach (float sz in new[] { -0.65f, 0.65f })
                            foreach (float sx in new[] { -0.35f, 0.35f })
                                Chair(Box, new Vector3(sx, 0f, sz), sz > 0f ? 180f : 0f);
                    break;
                case "chair":
                    Chair(Box, Vector3.zero, 0f);
                    break;
                case "toilet":
                    Box(Porcelain, new Vector3(0f, 0.2f, 0.05f), new Vector3(0.38f, 0.4f, 0.5f), 0.08f);
                    Box(Porcelain, new Vector3(0f, 0.55f, -0.22f), new Vector3(0.42f, 0.35f, 0.16f), 0.03f);
                    break;
                case "nightstand":
                    Box(Wood, new Vector3(0f, 0.27f, 0f), new Vector3(0.45f, 0.54f, 0.4f), 0.01f);
                    break;
                case "rug":
                    Box(Fabric, new Vector3(0f, 0.008f, 0f), new Vector3(2.2f, 0.012f, 1.5f), 0f, 0.7f);
                    break;
                case "mailboxes":
                    Box(Steel, new Vector3(0f, 1.2f, 0f), new Vector3(1.2f, 0.8f, 0.3f), 0.01f);
                    for (int i = 0; i < 6; i++)
                        Box(Trim, new Vector3(-0.4f + (i % 3) * 0.4f, 1.0f + (i / 3) * 0.4f, 0.16f), new Vector3(0.34f, 0.32f, 0.02f), 0f, 0.85f);
                    break;
                case "bench":
                    Box(Wood, new Vector3(0f, 0.44f, 0f), new Vector3(1.6f, 0.06f, 0.42f));
                    Box(Steel, new Vector3(-0.7f, 0.22f, 0f), new Vector3(0.05f, 0.44f, 0.38f));
                    Box(Steel, new Vector3(0.7f, 0.22f, 0f), new Vector3(0.05f, 0.44f, 0.38f));
                    break;
                case "washer":
                    Box(Porcelain, new Vector3(0f, 0.43f, 0f), new Vector3(0.6f, 0.86f, 0.6f), 0.03f);
                    Box(Steel, new Vector3(0f, 0.5f, 0.3f), new Vector3(0.36f, 0.36f, 0.02f), 0.01f, 0.6f);
                    break;
                case "shop_rack":
                    for (int i = 0; i < 3; i++) Box(Steel, new Vector3(0f, 0.25f + i * 0.5f, 0f), new Vector3(2.4f, 0.04f, 0.7f));
                    foreach (float sx in new[] { -1.15f, 1.15f })
                        Box(Steel, new Vector3(sx, 0.75f, 0f), new Vector3(0.05f, 1.5f, 0.7f), 0f, 0.8f);
                    break;
            }
        }

        private static void Chair(Action<Material, Vector3, Vector3, float, float> box, Vector3 at, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            box(Wood, at + r * new Vector3(0f, 0.45f, 0f), new Vector3(0.44f, 0.05f, 0.44f), 0.01f, 1f);
            box(Wood, at + r * new Vector3(0f, 0.72f, -0.2f), new Vector3(0.44f, 0.5f, 0.04f), 0.01f, 0.9f);
            foreach (float sx in new[] { -0.19f, 0.19f })
                foreach (float sz in new[] { -0.19f, 0.19f })
                    box(Wood, at + r * new Vector3(sx, 0.22f, sz), new Vector3(0.04f, 0.45f, 0.04f), 0.005f, 0.85f);
        }

        // ------------------------------------------------------------ colliders

        private static void Colliders(Transform t, BuildingPlan p)
        {
            var root = new GameObject("Colliders").transform;
            root.SetParent(t, false);
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            for (int f = 0; f <= p.Floors; f++)
            {
                float top = p.FloorY(f), bottom = f == 0 ? -0.75f : top - p.SlabThickness;
                int i = 0;
                foreach (Rect r in Subtract(Rect.MinMaxRect(-hx, -hz, hx, hz), HolesOn(p, f)))
                {
                    GameObject slab = Box(root, $"Slab{f}_{i++}", new Vector3(r.center.x, (top + bottom) * 0.5f, r.center.y), new Vector3(r.width, top - bottom, r.height));
                    // Nobody is placed on, or routed over, the roof.
                    if (f == p.Floors) Shapes.NotWalkable(slab);
                }
            }
            int n = 0;
            foreach (PlanWall w in p.Walls)
            {
                foreach (Piece pc in Pieces(p, w))
                {
                    WallBox(w, pc, 0f, w.Thickness, out Vector3 c, out Vector3 s);
                    GameObject wall = Box(root, "Wall" + n++, c, s);
                    if (pc.V1 >= p.RoofY + 0.1f) Shapes.NotWalkable(wall); // parapet tops
                }
                foreach (PlanOpening o in w.Openings)
                {
                    if (o.Kind != PlanOpeningKind.Window) continue;
                    float floor = p.FloorY(w.Floor);
                    WallBox(w, new Piece { U0 = o.Center - o.Width * 0.5f, U1 = o.Center + o.Width * 0.5f, V0 = floor + o.Bottom, V1 = floor + o.Top }, 0f, 0.1f, out Vector3 c, out Vector3 s);
                    Box(root, "Glass" + n++, c, s);
                }
            }
            foreach (PlanStair s in p.Stairs)
            {
                float run = Mathf.Abs(s.ZEnd - s.ZStart), dir = Mathf.Sign(s.ZEnd - s.ZStart);
                float len = Mathf.Sqrt(run * run + p.FloorHeight * p.FloorHeight) + 0.3f;
                float angle = Mathf.Atan2(p.FloorHeight, run) * Mathf.Rad2Deg;
                float xa = Mathf.Min(s.X0, s.X1), xb = Mathf.Max(s.X0, s.X1);
                float y0 = p.FloorY(s.FromFloor);
                // The ramp's top surface runs through the step nosings, from floor to floor.
                Quaternion rot = Quaternion.Euler(dir > 0 ? -angle : angle, 0f, 0f);
                Vector3 normal = rot * Vector3.up;
                Vector3 mid = new Vector3((xa + xb) * 0.5f, y0 + p.FloorHeight * 0.5f, (s.ZStart + s.ZEnd) * 0.5f) - normal * 0.1f;
                GameObject ramp = Box(root, "Stair" + s.FromFloor, mid, new Vector3(xb - xa, 0.2f, len));
                ramp.transform.localRotation = rot;
                float inner = p.Width * 0.5f - 0.35f;
                foreach (float x in new[] { xa, xb })
                {
                    if (Mathf.Abs(x) > inner) continue;
                    // Rails along the open sides of the flight: upright segments stepping up with it. (One
                    // tilted box would lean past the ends and squeeze the landings shut for the NavMesh.)
                    const float margin = 0.25f, seg = 0.6f;
                    int count = Mathf.Max(1, Mathf.CeilToInt((run - 2f * margin) / seg));
                    float step = (run - 2f * margin) / count;
                    for (int k = 0; k < count; k++)
                    {
                        float d0 = margin + k * step, d1 = d0 + step;
                        float h0 = p.FloorHeight * d0 / run, h1 = p.FloorHeight * d1 / run;
                        float bottom = y0 + h0 - 0.05f, top = y0 + h1 + 1.0f;
                        float zc = s.ZStart + dir * (d0 + d1) * 0.5f;
                        Box(root, "StairRail", new Vector3(x, (bottom + top) * 0.5f, zc), new Vector3(0.06f, top - bottom, step));
                    }
                }
            }
            foreach (PlanRail r in p.Rails)
            {
                Vector3 a = new Vector3(r.X0, p.FloorY(r.Floor) + 0.5f, r.Z0), b = new Vector3(r.X1, p.FloorY(r.Floor) + 0.5f, r.Z1);
                Vector3 d = b - a;
                Vector3 size = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Abs(d.x), 1f, 0.06f) : new Vector3(0.06f, 1f, Mathf.Abs(d.z));
                Box(root, "Rail", (a + b) * 0.5f, size);
            }
            foreach (PlanDecor d in p.Decor)
            {
                Vector3 size;
                switch (d.Kind)
                {
                    case "sofa": size = new Vector3(2.0f, 0.85f, 0.85f); break;
                    case "table": size = new Vector3(1.4f, 0.75f, 0.8f); break;
                    case "dining": size = new Vector3(1.4f, 0.75f, 1.9f); break;
                    case "washer": size = new Vector3(0.6f, 0.86f, 0.6f); break;
                    case "shop_rack": size = new Vector3(2.4f, 1.5f, 0.7f); break;
                    case "bench": size = new Vector3(1.6f, 0.47f, 0.42f); break;
                    case "mailboxes": size = new Vector3(1.2f, 1.6f, 0.3f); break;
                    case "toilet": size = new Vector3(0.42f, 0.72f, 0.66f); break;
                    case "nightstand": size = new Vector3(0.45f, 0.54f, 0.4f); break;
                    default: continue;
                }
                GameObject c = Box(root, "Decor_" + d.Kind, new Vector3(d.X, p.FloorY(d.Floor) + size.y * 0.5f, d.Z), size);
                c.transform.localRotation = Quaternion.Euler(0f, d.Yaw, 0f);
            }
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }
    }
}
