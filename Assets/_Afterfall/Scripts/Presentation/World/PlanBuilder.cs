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
            List<PropKit.Piece> pieces = PropKit.Cached("plan:" + plan.Id, kit => Visuals(kit, plan, -1));
            PropKit.Spawn(t, pieces);
            Colliders(t, plan, -1);
        }

        /// <summary>
        /// One floor of a building (a city building is built a floor or three at a time around the
        /// survivor): its slab, walls, rooms, stair up, rails and furniture, with colliders. The meshes
        /// are not cached: destroy them with the floor.
        /// </summary>
        public static void BuildFloor(Transform t, BuildingPlan plan, int floor)
        {
            List<PropKit.Piece> pieces = PropKit.Fresh($"{plan.Name} floor {floor}", kit => Visuals(kit, plan, floor));
            PropKit.Spawn(t, pieces);
            Colliders(t, plan, floor);
        }

        /// <summary>True for a city building, whose outer walls wear its facade texture (painted windows fall in the real openings).</summary>
        private static bool FacadeWalls(BuildingPlan p) => CitySurfaces.FacadeOf(p.Facade) != null && p.Id.StartsWith(BuildingPlans.CityBlockKind + "|", StringComparison.Ordinal);

        public static Material FacadeMaterial(BuildingPlan p) => MaterialLibrary.Facade(CitySurfaces.FacadeOf(p.Facade), Color.white);

        /// <summary>The facade's colour (a vertex colour) for a building's finish and tint, shared with the far views.</summary>
        public static Color FacadeTint(string facade, int tint)
        {
            switch (facade)
            {
                case "brick": return new[] { new Color(0.78f, 0.42f, 0.32f), new Color(0.86f, 0.72f, 0.52f), new Color(0.62f, 0.44f, 0.34f), new Color(0.74f, 0.48f, 0.36f), new Color(0.92f, 0.88f, 0.8f), new Color(0.66f, 0.34f, 0.26f), new Color(0.8f, 0.62f, 0.46f), new Color(0.88f, 0.82f, 0.72f) }[tint & 7];
                case "render": return new[] { new Color(0.95f, 0.93f, 0.86f), new Color(0.9f, 0.86f, 0.72f), new Color(0.82f, 0.88f, 0.9f), new Color(0.94f, 0.84f, 0.8f), new Color(0.86f, 0.9f, 0.82f), new Color(0.98f, 0.96f, 0.92f), new Color(0.9f, 0.8f, 0.68f), new Color(0.84f, 0.84f, 0.86f) }[tint & 7];
                case "concrete": return new[] { new Color(0.86f, 0.86f, 0.84f), new Color(0.78f, 0.79f, 0.8f), new Color(0.9f, 0.88f, 0.82f), new Color(0.72f, 0.74f, 0.76f) }[tint & 3];
                case "stone": return new[] { new Color(0.92f, 0.88f, 0.78f), new Color(0.84f, 0.82f, 0.78f), new Color(0.88f, 0.84f, 0.72f), new Color(0.8f, 0.8f, 0.8f) }[tint & 3];
                default: return new Color(0.7f, 0.72f, 0.7f);
            }
        }

        /// <summary>
        /// A city building's outside where its floors are not built: facades (with the texture's
        /// windows) below <paramref name="from"/> and above <paramref name="to"/>, the roof and its
        /// plant, plaster inside, and caps closing the built floors off from the empty ones.
        /// </summary>
        public static void BuildShell(Transform t, BuildingPlan p, int from, int to)
        {
            List<PropKit.Piece> pieces = PropKit.Fresh($"{p.Name} shell", kit =>
            {
                float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
                Material face = FacadeMaterial(p);
                Color tint = FacadeTint(p.Facade, p.FacadeTint);
                var spans = new List<(float Y0, float Y1)>();
                if (from > 0) spans.Add((-0.75f, p.FloorY(from) - p.SlabThickness));
                if (to < p.Floors - 1) spans.Add((p.FloorY(to + 1) - p.SlabThickness, p.TotalHeight));
                foreach ((float y0, float y1) in spans)
                {
                    MeshBuilder f = kit[face];
                    f.Color = tint;
                    float ow = BuildingPlans.BayOffset(p.Width), od = BuildingPlans.BayOffset(p.Depth);
                    f.FacadeQuad(new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), y0, y1, Vector3.forward, -ow);
                    f.FacadeQuad(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), y0, y1, Vector3.back, -ow);
                    f.FacadeQuad(new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), y0, y1, Vector3.left, -od);
                    f.FacadeQuad(new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), y0, y1, Vector3.right, -od);
                    // Plaster behind, so the empty floors read as walls from inside too.
                    MeshBuilder pl = kit[Plaster];
                    pl.Color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
                    float h = y1 - y0, c = (y0 + y1) * 0.5f;
                    pl.Box(new Vector3(0f, c, hz - Ext), new Vector3(p.Width, h, 0.1f));
                    pl.Box(new Vector3(0f, c, -hz + Ext), new Vector3(p.Width, h, 0.1f));
                    pl.Box(new Vector3(-hx + Ext, c, 0f), new Vector3(0.1f, h, p.Depth));
                    pl.Box(new Vector3(hx - Ext, c, 0f), new Vector3(0.1f, h, p.Depth));
                }
                // Caps above and below the built floors.
                MeshBuilder slab = kit[Concrete];
                slab.Color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
                if (to < p.Floors - 1) slab.Box(new Vector3(0f, p.FloorY(to + 1) + 0.12f, 0f), new Vector3(p.Width - 0.1f, 0.24f, p.Depth - 0.1f));
                if (from > 0) slab.Box(new Vector3(0f, p.FloorY(from) - p.SlabThickness - 0.12f, 0f), new Vector3(p.Width - 0.1f, 0.24f, p.Depth - 0.1f));
                Roof(kit, p);
            });
            PropKit.Spawn(t, pieces);
            var root = new GameObject("Colliders").transform;
            root.SetParent(t, false);
            float hx2 = p.Width * 0.5f, hz2 = p.Depth * 0.5f;
            void Span(float y0, float y1)
            {
                float h = y1 - y0, c = (y0 + y1) * 0.5f;
                Shapes.NotWalkable(Box(root, "Facade", new Vector3(0f, c, hz2 - Ext * 0.5f), new Vector3(p.Width, h, Ext)));
                Shapes.NotWalkable(Box(root, "Facade", new Vector3(0f, c, -hz2 + Ext * 0.5f), new Vector3(p.Width, h, Ext)));
                Shapes.NotWalkable(Box(root, "Facade", new Vector3(-hx2 + Ext * 0.5f, c, 0f), new Vector3(Ext, h, p.Depth)));
                Shapes.NotWalkable(Box(root, "Facade", new Vector3(hx2 - Ext * 0.5f, c, 0f), new Vector3(Ext, h, p.Depth)));
            }
            if (from > 0) Span(-0.75f, p.FloorY(from) - p.SlabThickness);
            if (to < p.Floors - 1)
            {
                Span(p.FloorY(to + 1) - p.SlabThickness, p.TotalHeight);
                Box(root, "Cap", new Vector3(0f, p.FloorY(to + 1) + 0.12f, 0f), new Vector3(p.Width, 0.24f, p.Depth));
                Shapes.NotWalkable(Box(root, "Roof", new Vector3(0f, p.RoofY - p.SlabThickness * 0.5f, 0f), new Vector3(p.Width, p.SlabThickness, p.Depth)));
            }
            if (from > 0) Box(root, "Cap", new Vector3(0f, p.FloorY(from) - p.SlabThickness - 0.12f, 0f), new Vector3(p.Width, 0.24f, p.Depth));
        }

        private const float Ext = 0.3f;

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

        private static void Visuals(PropKit kit, BuildingPlan p, int only)
        {
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            bool facade = FacadeWalls(p);
            // Slabs: a foundation, one per upper floor (with stair holes) and the roof. A single floor
            // builds its own slab, and the roof when it is the top floor.
            for (int f = 0; f <= p.Floors; f++)
            {
                if (only >= 0 && f != only && !(f == p.Floors && only == p.Floors - 1)) continue;
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
                if (only >= 0 && room.Floor != only) continue;
                Rect area = Rect.MinMaxRect(Mathf.Min(room.X0, room.X1), Mathf.Min(room.Z0, room.Z1), Mathf.Max(room.X0, room.X1), Mathf.Max(room.Z0, room.Z1));
                Material finish = room.Finish == "tiles" ? Tiles : room.Finish == "concrete" ? Concrete : room.Finish == "carpet" ? Carpet(room.Floor + (int)(room.X0 * 7f)) : room.Finish == "lino" ? Lino : Planks;
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
                if (only >= 0 && w.Floor != only) continue;
                foreach (Piece pc in Pieces(p, w))
                {
                    if (w.Exterior)
                    {
                        float o = Outward(w);
                        WallBox(w, pc, o * (w.Thickness * 0.5f - 0.11f), 0.22f, out Vector3 c, out Vector3 s);
                        MeshBuilder brick = kit[facade ? Concrete : WallMaterial(p)];
                        brick.Color = new Color(1f, 1f, 1f, 1f);
                        brick.Box(c, s, null, Bevel);
                        if (facade) FacadeFace(kit, p, w, pc);
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

            foreach (PlanStair s in p.Stairs)
                if (only < 0 || s.FromFloor == only) Stair(kit, p, s);
            foreach (PlanRail r in p.Rails)
                if (only < 0 || r.Floor == only) Rail(kit, p.FloorY(r.Floor), new Vector3(r.X0, 0f, r.Z0), new Vector3(r.X1, 0f, r.Z1));
            if (only < 0) Roof(kit, p);
            if (only <= 0) Entrance(kit, p);
            foreach (PlanDecor d in p.Decor)
                if (only < 0 || d.Floor == only) Decor(kit, p, d);
        }

        /// <summary>The outer face of a city building's wall piece in its facade texture (UVs from the building's corner, up from its base).</summary>
        private static void FacadeFace(PropKit kit, BuildingPlan p, PlanWall w, Piece pc)
        {
            MeshBuilder f = kit[FacadeMaterial(p)];
            f.Color = FacadeTint(p.Facade, p.FacadeTint);
            float o = Outward(w), face = w.Thickness * 0.5f + 0.004f;
            if (w.AlongX)
            {
                float z = w.Z0 + o * face, x0 = Mathf.Min(w.X0, w.X1);
                float u0 = (x0 + p.Width * 0.5f) - BuildingPlans.BayOffset(p.Width);
                f.FacadeQuad(new Vector3(x0 + pc.U0, 0f, z), new Vector3(x0 + pc.U1, 0f, z), pc.V0, pc.V1, new Vector3(0f, 0f, o), u0 + pc.U0);
            }
            else
            {
                float x = w.X0 + o * face, z0 = Mathf.Min(w.Z0, w.Z1);
                float u0 = (z0 + p.Depth * 0.5f) - BuildingPlans.BayOffset(p.Depth);
                f.FacadeQuad(new Vector3(x, 0f, z0 + pc.U0), new Vector3(x, 0f, z0 + pc.U1), pc.V0, pc.V1, new Vector3(o, 0f, 0f), u0 + pc.U0);
            }
        }

        private static Material WallMaterial(BuildingPlan p)
        {
            switch (p.Facade)
            {
                case "stone": return Mat(Surface.Stone, new Color(0.78f, 0.75f, 0.68f));
                case "corrugated": return Mat(PropSurfaces.CorrugatedHorizontal, new Color(0.62f, 0.64f, 0.62f), 0.35f, 0.5f);
                case "render": return Mat(Surface.Plaster, new Color(0.86f, 0.84f, 0.78f));
                case "concrete": return Concrete;
                default: return Brick;
            }
        }

        private static Material Lino => Mat(CitySurfaces.Lino, new Color(0.66f, 0.68f, 0.62f), 0.5f);

        private static readonly Color[] Fabrics =
        {
            new Color(0.36f, 0.42f, 0.5f), new Color(0.55f, 0.32f, 0.28f), new Color(0.42f, 0.48f, 0.34f), new Color(0.6f, 0.52f, 0.4f), new Color(0.3f, 0.3f, 0.34f), new Color(0.58f, 0.44f, 0.52f)
        };

        private static Material Carpet(int variant) => Mat(CitySurfaces.Carpet, Color.Lerp(Fabrics[((variant % Fabrics.Length) + Fabrics.Length) % Fabrics.Length], Color.white, 0.25f));

        private static Material FabricOf(int variant) => Mat(Surface.Fabric, Fabrics[((variant % Fabrics.Length) + Fabrics.Length) % Fabrics.Length]);

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
                {
                    Material cloth = d.Variant == 0 ? Fabric : FabricOf(d.Variant);
                    Box(cloth, new Vector3(0f, 0.22f, 0f), new Vector3(2.0f, 0.44f, 0.85f), 0.06f);
                    Box(cloth, new Vector3(0f, 0.6f, -0.33f), new Vector3(2.0f, 0.5f, 0.2f), 0.06f, 0.9f);
                    Box(cloth, new Vector3(-0.93f, 0.55f, 0f), new Vector3(0.2f, 0.3f, 0.85f), 0.05f, 0.92f);
                    Box(cloth, new Vector3(0.93f, 0.55f, 0f), new Vector3(0.2f, 0.3f, 0.85f), 0.05f, 0.92f);
                    break;
                }
                case "armchair":
                {
                    Material cloth = FabricOf(d.Variant + 2);
                    Box(cloth, new Vector3(0f, 0.22f, 0f), new Vector3(0.85f, 0.44f, 0.8f), 0.06f);
                    Box(cloth, new Vector3(0f, 0.62f, -0.3f), new Vector3(0.85f, 0.55f, 0.2f), 0.06f, 0.9f);
                    Box(cloth, new Vector3(-0.36f, 0.55f, 0f), new Vector3(0.14f, 0.28f, 0.8f), 0.04f, 0.92f);
                    Box(cloth, new Vector3(0.36f, 0.55f, 0f), new Vector3(0.14f, 0.28f, 0.8f), 0.04f, 0.92f);
                    break;
                }
                case "tv":
                    Box(Wood, new Vector3(0f, 0.25f, 0f), new Vector3(1.4f, 0.5f, 0.4f), 0.01f, 0.8f);
                    Box(Steel, new Vector3(0f, 0.85f, 0f), new Vector3(1.1f, 0.65f, 0.06f), 0.01f, 0.25f);
                    break;
                case "plant":
                {
                    Box(Porcelain, new Vector3(0f, 0.2f, 0f), new Vector3(0.4f, 0.4f, 0.4f), 0.04f, 0.6f + 0.06f * (d.Variant % 4));
                    MeshBuilder leaves = kit[Mat(Surface.Foliage, new Color(0.3f, 0.45f, 0.22f))];
                    leaves.Color = new Color(0.8f + 0.04f * (d.Variant % 5), 0.9f, 0.8f, 0.95f);
                    leaves.Box(o + r * new Vector3(0f, 0.75f, 0f), new Vector3(0.55f, 0.7f, 0.55f), r, 0.2f);
                    break;
                }
                case "kitchen":
                    Box(Wood, new Vector3(0f, 0.43f, 0f), new Vector3(1.8f, 0.86f, 0.6f), 0.01f, 0.95f);
                    Box(Porcelain, new Vector3(0f, 0.89f, 0f), new Vector3(1.84f, 0.04f, 0.64f), 0.005f, 0.85f);
                    Box(Steel, new Vector3(0.4f, 0.92f, 0f), new Vector3(0.5f, 0.02f, 0.4f), 0f, 0.9f);
                    Box(Wood, new Vector3(0f, 1.75f, -0.12f), new Vector3(1.8f, 0.7f, 0.35f), 0.01f, 0.9f);
                    break;
                case "desk":
                    Box(Wood, new Vector3(0f, 0.73f, 0f), new Vector3(1.4f, 0.04f, 0.7f), 0.005f, 0.85f + 0.03f * (d.Variant % 4));
                    Box(Wood, new Vector3(-0.62f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.66f), 0.005f, 0.8f);
                    Box(Wood, new Vector3(0.62f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.66f), 0.005f, 0.8f);
                    Box(Steel, new Vector3(0f, 0.97f, 0.15f), new Vector3(0.55f, 0.38f, 0.04f), 0.005f, 0.3f);
                    break;
                case "whiteboard":
                    Box(Porcelain, new Vector3(0f, 1.5f, 0f), new Vector3(2.0f, 1.1f, 0.04f), 0.005f, 1.05f);
                    Box(Steel, new Vector3(0f, 0.93f, 0.05f), new Vector3(2.0f, 0.04f, 0.08f), 0f, 0.8f);
                    break;
                case "bathtub":
                    Box(Porcelain, new Vector3(0f, 0.3f, 0f), new Vector3(1.7f, 0.6f, 0.75f), 0.08f);
                    Box(Steel, new Vector3(0f, 0.58f, 0f), new Vector3(1.5f, 0.03f, 0.55f), 0f, 0.55f);
                    break;
                case "pew":
                    Box(Wood, new Vector3(0f, 0.45f, 0f), new Vector3(3.4f, 0.06f, 0.45f), 0.01f, 0.8f);
                    Box(Wood, new Vector3(0f, 0.75f, -0.2f), new Vector3(3.4f, 0.55f, 0.05f), 0.01f, 0.75f);
                    Box(Wood, new Vector3(-1.68f, 0.45f, 0f), new Vector3(0.06f, 0.9f, 0.5f), 0.01f, 0.7f);
                    Box(Wood, new Vector3(1.68f, 0.45f, 0f), new Vector3(0.06f, 0.9f, 0.5f), 0.01f, 0.7f);
                    break;
                case "altar":
                    Box(Mat(Surface.Stone, new Color(0.8f, 0.78f, 0.72f)), new Vector3(0f, 0.5f, 0f), new Vector3(2.2f, 1.0f, 0.9f), 0.03f);
                    Box(Mat(Surface.Fabric, new Color(0.85f, 0.82f, 0.74f)), new Vector3(0f, 1.01f, 0f), new Vector3(2.3f, 0.02f, 1.0f), 0f);
                    break;
                case "pallet":
                    Box(Wood, new Vector3(0f, 0.07f, 0f), new Vector3(1.2f, 0.14f, 1.0f), 0.01f, 0.8f);
                    Box(Mat(Surface.Fabric, new Color(0.72f, 0.66f, 0.54f)), new Vector3(0f, 0.6f, 0f), new Vector3(1.1f, 0.92f, 0.95f), 0.05f, 0.85f + 0.05f * (d.Variant % 3));
                    break;
                case "lift":
                    // Lift doors and a call panel on the wall (the lift itself is a fast way between floors).
                    Box(Steel, new Vector3(0f, 1.1f, 0.03f), new Vector3(1.3f, 2.2f, 0.05f), 0.005f, 0.95f);
                    Box(Steel, new Vector3(0f, 1.1f, 0.06f), new Vector3(0.02f, 2.15f, 0.02f), 0f, 0.6f);
                    Box(Trim, new Vector3(0f, 2.3f, 0.03f), new Vector3(1.5f, 0.14f, 0.06f), 0f, 0.9f);
                    Box(Steel, new Vector3(0.85f, 1.2f, 0.03f), new Vector3(0.12f, 0.25f, 0.03f), 0f, 0.7f);
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
                    Box(d.Variant == 0 ? Fabric : FabricOf(d.Variant + 1), new Vector3(0f, 0.008f, 0f), new Vector3(2.2f, 0.012f, 1.5f), 0f, 0.7f);
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
                case "coffee_table":
                    Box(Wood, new Vector3(0f, 0.42f, 0f), new Vector3(1.0f, 0.05f, 0.55f), 0.01f, 0.9f);
                    Box(Wood, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.03f, 0.45f), 0.005f, 0.8f);
                    foreach (float sx in new[] { -0.44f, 0.44f })
                        foreach (float sz in new[] { -0.22f, 0.22f })
                            Box(Wood, new Vector3(sx, 0.2f, sz), new Vector3(0.05f, 0.4f, 0.05f), 0.005f, 0.85f);
                    Box(Porcelain, new Vector3(0.22f, 0.5f, 0.05f), new Vector3(0.12f, 0.1f, 0.12f), 0.02f, 0.75f);
                    break;
                case "sideboard":
                    Box(Wood, new Vector3(0f, 0.4f, 0f), new Vector3(1.6f, 0.8f, 0.45f), 0.01f, 0.82f);
                    foreach (float sx in new[] { -0.4f, 0.4f })
                        Box(Trim, new Vector3(sx, 0.42f, 0.23f), new Vector3(0.74f, 0.6f, 0.01f), 0f, 0.7f);
                    Box(Porcelain, new Vector3(-0.5f, 0.92f, 0f), new Vector3(0.16f, 0.24f, 0.16f), 0.04f, 0.6f + 0.05f * (d.Variant % 4));
                    Box(Wood, new Vector3(0.35f, 1.02f, -0.12f), new Vector3(0.34f, 0.42f, 0.04f), 0.005f, 0.5f);
                    break;
                case "picture":
                {
                    // A framed landscape on the wall: sky over fields, a darker band of hills between.
                    Box(Wood, new Vector3(0f, 1.55f, 0.015f), new Vector3(0.92f, 0.7f, 0.03f), 0.005f, 0.55f + 0.1f * (d.Variant % 3));
                    MeshBuilder sky = kit[FabricOf(d.Variant + 3)];
                    sky.Color = new Color(1f, 1f, 1f, 0.95f);
                    sky.Box(o + r * new Vector3(0f, 1.69f, 0.032f), new Vector3(0.78f, 0.28f, 0.006f), r, 0f);
                    Box(FabricOf(d.Variant + 7), new Vector3(0f, 1.5f, 0.033f), new Vector3(0.78f, 0.1f, 0.006f), 0f, 0.55f);
                    Box(FabricOf(d.Variant), new Vector3(0f, 1.395f, 0.032f), new Vector3(0.78f, 0.17f, 0.006f), 0f, 0.85f);
                    break;
                }
                case "lamp":
                {
                    Box(Steel, new Vector3(0f, 0.015f, 0f), new Vector3(0.3f, 0.03f, 0.3f), 0.01f, 0.4f);
                    Box(Steel, new Vector3(0f, 0.78f, 0f), new Vector3(0.03f, 1.5f, 0.03f), 0f, 0.5f);
                    MeshBuilder shade = kit[Mat(Surface.Fabric, new Color(0.93f, 0.86f, 0.7f))];
                    shade.Color = new Color(0.9f + 0.03f * (d.Variant % 4), 0.9f, 0.85f, 0.95f);
                    shade.Box(o + r * new Vector3(0f, 1.55f, 0f), new Vector3(0.42f, 0.3f, 0.42f), r, 0.06f);
                    break;
                }
                case "coat_rack":
                    Box(Wood, new Vector3(0f, 1.72f, 0.02f), new Vector3(0.9f, 0.12f, 0.03f), 0.005f, 0.8f);
                    for (int i = 0; i < 4; i++) Box(Steel, new Vector3(-0.33f + i * 0.22f, 1.68f, 0.07f), new Vector3(0.03f, 0.03f, 0.08f), 0f, 0.5f);
                    Box(FabricOf(d.Variant), new Vector3(-0.22f, 1.25f, 0.1f), new Vector3(0.36f, 0.85f, 0.14f), 0.05f, 0.8f);
                    Box(FabricOf(d.Variant + 4), new Vector3(0.2f, 1.32f, 0.1f), new Vector3(0.32f, 0.7f, 0.13f), 0.05f, 0.75f);
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

        private static void Colliders(Transform t, BuildingPlan p, int only)
        {
            var root = new GameObject("Colliders").transform;
            root.SetParent(t, false);
            float hx = p.Width * 0.5f, hz = p.Depth * 0.5f;
            for (int f = 0; f <= p.Floors; f++)
            {
                if (only >= 0 && f != only && !(f == p.Floors && only == p.Floors - 1)) continue;
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
                if (only >= 0 && w.Floor != only) continue;
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
                if (only >= 0 && s.FromFloor != only) continue;
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
                if (only >= 0 && r.Floor != only) continue;
                Vector3 a = new Vector3(r.X0, p.FloorY(r.Floor) + 0.5f, r.Z0), b = new Vector3(r.X1, p.FloorY(r.Floor) + 0.5f, r.Z1);
                Vector3 d = b - a;
                Vector3 size = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Abs(d.x), 1f, 0.06f) : new Vector3(0.06f, 1f, Mathf.Abs(d.z));
                Box(root, "Rail", (a + b) * 0.5f, size);
            }
            foreach (PlanDecor d in p.Decor)
            {
                if (only >= 0 && d.Floor != only) continue;
                Vector3 size;
                switch (d.Kind)
                {
                    case "armchair": size = new Vector3(0.9f, 0.85f, 0.85f); break;
                    case "kitchen": size = new Vector3(1.8f, 0.9f, 0.6f); break;
                    case "desk": size = new Vector3(1.4f, 0.75f, 0.7f); break;
                    case "bathtub": size = new Vector3(1.7f, 0.6f, 0.75f); break;
                    case "pew": size = new Vector3(3.4f, 0.9f, 0.55f); break;
                    case "altar": size = new Vector3(2.2f, 1.0f, 0.9f); break;
                    case "pallet": size = new Vector3(1.2f, 1.1f, 1.0f); break;
                    case "plant": size = new Vector3(0.5f, 1.2f, 0.5f); break;
                    case "sofa": size = new Vector3(2.0f, 0.85f, 0.85f); break;
                    case "table": size = new Vector3(1.4f, 0.75f, 0.8f); break;
                    case "dining": size = new Vector3(1.4f, 0.75f, 1.9f); break;
                    case "washer": size = new Vector3(0.6f, 0.86f, 0.6f); break;
                    case "shop_rack": size = new Vector3(2.4f, 1.5f, 0.7f); break;
                    case "bench": size = new Vector3(1.6f, 0.47f, 0.42f); break;
                    case "mailboxes": size = new Vector3(1.2f, 1.6f, 0.3f); break;
                    case "toilet": size = new Vector3(0.42f, 0.72f, 0.66f); break;
                    case "nightstand": size = new Vector3(0.45f, 0.54f, 0.4f); break;
                    case "tv": size = new Vector3(1.4f, 0.5f, 0.4f); break;
                    case "coffee_table": size = new Vector3(1.0f, 0.45f, 0.55f); break;
                    case "sideboard": size = new Vector3(1.6f, 0.8f, 0.45f); break;
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
