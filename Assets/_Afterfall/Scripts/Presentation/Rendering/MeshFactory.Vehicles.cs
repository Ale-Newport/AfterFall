using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>The meshes of one driveable vehicle model: body parts, hinged doors and a wheel.</summary>
    public sealed class VehicleMeshes
    {
        /// <summary>[0] painted body, [1] trim and running gear, [2] glass, [3] rubber and interior.</summary>
        public Mesh[] Body;

        /// <summary>Doors, front-left first; each is pivoted on its hinge.</summary>
        public readonly List<DoorMesh> Doors = new List<DoorMesh>();

        /// <summary>One wheel (tyre and rim) centred on its hub, axle along X.</summary>
        public Mesh WheelRubber, WheelRim;

        /// <summary>Where the driver sits (feet origin) in the vehicle's frame.</summary>
        public Vector3 Seat;
    }

    public sealed class DoorMesh
    {
        public Mesh Paint, Glass;
        public Vector3 Hinge;

        /// <summary>-1 left, +1 right: the side it swings out to.</summary>
        public float Side;
    }

    public static partial class MeshFactory
    {
        private static readonly Dictionary<string, VehicleMeshes> VehicleCache = new Dictionary<string, VehicleMeshes>();

        /// <summary>
        /// A driveable vehicle model: "sedan" and "pickup" reuse the wreck's lofted body (in good
        /// paint, with a little rust) with the door openings left dark and the doors built as
        /// separate hinged panels carrying their windows; "atv" is a quad bike built from parts.
        /// Wheels are separate so they can turn and steer.
        /// </summary>
        public static VehicleMeshes Vehicle(string visual, Color paint, float wheelRadius)
        {
            string key = $"veh:{visual}:{ColorUtility.ToHtmlStringRGB(paint)}:{wheelRadius:0.00}";
            if (VehicleCache.TryGetValue(key, out VehicleMeshes cached) && cached.Body[0] != null) return cached;
            VehicleMeshes m = visual == "atv" ? Atv(paint) : Car(visual == "pickup", paint);
            Wheel(m, wheelRadius, visual == "atv");
            VehicleCache[key] = m;
            return m;
        }

        // ------------------------------------------------------------ cars

        private static VehicleMeshes Car(bool pickup, Color paint)
        {
            int seed = pickup ? 9300 : 9200;
            var body = new MeshBuilder();
            var trim = new MeshBuilder();
            var glass = new MeshBuilder();
            var rubber = new MeshBuilder();
            float wheelF = pickup ? 1.42f : 1.35f, wheelR = pickup ? -1.5f : -1.35f;
            float zFront = 2.25f, zBack = pickup ? -0.6f : -2.25f, zTail = pickup ? -2.3f : -2.25f;
            // Door spans along the body (front to back) and the hinge at each door's front edge.
            float[][] doors = pickup ? new[] { new[] { 0.86f, -0.52f } } : new[] { new[] { 0.86f, -0.2f }, new[] { -0.2f, -1.06f } };

            Color Shade(int tag, Vector3 p, Vector3 n)
            {
                if (tag == CarUnder) return new Color(0.22f, 0.2f, 0.19f, 0.5f);
                if (tag == CarGlass) return new Color(1.1f, 1.1f, 1.08f, 1f);
                float ao = Mathf.Lerp(0.62f, 1f, Sstep(0.25f, 0.65f, p.y));
                float rust = Sstep(0.45f, 0.3f, p.y) * 0.35f * Sstep(0.55f, 0.8f, Fbm3(p * 2.2f, seed));
                Color c = Color.Lerp(paint, CarRust, rust) * (0.94f + Fbm3(p * 3f, seed + 5) * 0.08f);
                return new Color(c.r, c.g, c.b, ao);
            }

            var zs = new List<float>();
            for (float z = zBack; z < zFront; z += 0.15f) zs.Add(z);
            zs.Add(zFront);
            foreach (float wz in new[] { wheelF, wheelR })
                for (int k = 0; k <= 8; k++) zs.Add(wz + 0.43f * Mathf.Cos(k * Mathf.PI / 8f));
            CarWindows(pickup, out float wsTop, out float rearTop, out float rearBase);
            zs.AddRange(new[] { 0.95f, wsTop, rearTop, rearBase, -0.2f, 0.86f, -1.06f, -0.27f, -0.13f, -1.0f, -0.52f, -0.42f, 1.95f, -1.95f });
            zs = CleanStations(zs, zBack, zFront);

            bool InRange(float z, float a, float b2) => z > a && z < b2;
            bool SideWindow(float z) => pickup ? InRange(z, -0.42f, 0.95f) : InRange(z, -1.0f, -0.27f) || InRange(z, -0.13f, 0.95f);
            bool InDoor(float z)
            {
                foreach (float[] d in doors)
                    if (InRange(z, d[1], d[0])) return true;
                return false;
            }
            int Tag(int j, float z)
            {
                if (j == 17) return CarUnder;
                if (j == 8) return InRange(z, wsTop + 0.02f, 0.93f) || InRange(z, rearBase + 0.02f, rearTop - 0.02f) ? CarGlass : CarPaint;
                bool right = j > 8;
                int k = right ? 16 - j : j;
                if (k == 0) return CarRocker;
                // The door openings: dark inside, and the doors (built below) carry the paint and windows.
                if (InDoor(z) && k <= 5) return k == 5 ? CarSkip : CarUnder;
                if (k == 5) return SideWindow(z) ? CarGlass : CarPaint;
                return CarPaint;
            }
            MeshBuilder Target(int tag) => tag == CarGlass ? glass : tag == CarUnder ? trim : tag == CarSkip ? null : body;
            CarLoft(zs, 18, (z, pts) => CarSection(z, pickup, wheelF, wheelR, pts), Tag, Target, Shade, true, true);

            if (pickup)
            {
                var bedZ = new List<float>();
                for (float z = zTail; z < -0.6f; z += 0.15f) bedZ.Add(z);
                bedZ.Add(-0.6f);
                for (int k = 0; k <= 8; k++) bedZ.Add(wheelR + 0.43f * Mathf.Cos(k * Mathf.PI / 8f));
                bedZ = CleanStations(bedZ, zTail, -0.6f);
                foreach (bool right in new[] { false, true })
                {
                    CarLoft(bedZ, 4, (z, pts) =>
                    {
                        float wB = 0.9f - (z < -2.15f ? 0.03f : 0f);
                        float yb = CarBottom(z, wheelF, wheelR);
                        var l = new[] { new Vector2(-wB, yb), new Vector2(-wB, 1.0f), new Vector2(-(wB - 0.07f), 1.0f), new Vector2(-(wB - 0.07f), Mathf.Max(yb, 0.6f)) };
                        for (int k = 0; k < 4; k++) pts[k] = right ? new Vector2(-l[3 - k].x, l[3 - k].y) : l[k];
                    }, (j, z) => j == 3 ? CarUnder : CarPaint, Target, Shade, true, true);
                }
                ShadedBox(body, new Vector3(0f, 0.6f, -1.45f), new Vector3(1.68f, 0.05f, 1.72f), Quaternion.identity, 0.01f, (p, n) => Shade(CarPaint, p, n) * new Color(0.7f, 0.68f, 0.66f, 0.8f));
                ShadedBox(body, new Vector3(0f, 0.8f, -2.27f), new Vector3(1.68f, 0.42f, 0.05f), Quaternion.identity, 0.012f, (p, n) => Shade(CarPaint, p, n));
            }

            CarDetails(body, trim, glass, rubber, seed, pickup, false, zFront, zTail, wheelF, wheelR, false);

            // Interior seen through the windows and open doors: seats, a dashboard and a steering wheel.
            var vinyl = new Color(0.36f, 0.3f, 0.26f, 0.8f);
            float[] seats = pickup ? new[] { -0.05f } : new[] { 0.0f, -0.9f };
            foreach (float z in seats)
            {
                ShadedBox(rubber, new Vector3(0f, 0.45f, z), new Vector3(1.4f, 0.14f, 0.5f), Quaternion.identity, 0.04f, (p, n) => vinyl);
                ShadedBox(rubber, new Vector3(0f, 0.78f, z - 0.27f), new Vector3(1.4f, 0.56f, 0.12f), Quaternion.Euler(-16f, 0f, 0f), 0.04f, (p, n) => vinyl);
            }
            ShadedBox(trim, new Vector3(0f, 0.9f, 0.82f), new Vector3(1.55f, 0.2f, 0.32f), Quaternion.identity, 0.03f, (p, n) => new Color(0.3f, 0.28f, 0.27f, 0.7f));
            Tyre(rubber, Matrix4x4.TRS(new Vector3(-0.38f, 1.02f, 0.58f), Quaternion.Euler(60f, 0f, 0f), Vector3.one), 0.19f, 0.16f, 0.03f, 12, false, new Color(0.25f, 0.24f, 0.23f, 1f));

            var m = new VehicleMeshes
            {
                Body = new[] { NatureBuild(body, "vehicle body"), NatureBuild(trim, "vehicle trim"), NatureBuild(glass, "vehicle glass"), NatureBuild(rubber, "vehicle interior") },
                // The driver's floor point: hips land on the cushion and the head stays under the roof.
                Seat = new Vector3(-0.38f, 0.2f, -0.02f)
            };

            // Doors: the side panel (rocker crease to window frame) and the window, lofted over the door's span.
            var sec = new Vector2[18];
            foreach (float side in new[] { -1f, 1f })
            {
                foreach (float[] d in doors)
                {
                    var dz = new List<float>();
                    for (float z = d[1]; z < d[0]; z += 0.12f) dz.Add(z);
                    dz.Add(d[0]);
                    dz = CleanStations(dz, d[1], d[0]);
                    var dp = new MeshBuilder();
                    var dg = new MeshBuilder();
                    CarLoft(dz, 6, (z, pts) =>
                    {
                        CarSection(z, pickup, wheelF, wheelR, sec);
                        for (int k = 0; k < 6; k++)
                        {
                            Vector2 p = sec[k + 1];
                            // Mirror for the right side, and stand a hair proud of the body.
                            pts[side < 0 ? k : 5 - k] = new Vector2(side < 0 ? p.x - 0.006f : -p.x + 0.006f, p.y);
                        }
                    }, (j, z) =>
                    {
                        int k = side < 0 ? j : 4 - j;
                        if (j == 5) return CarSkip;
                        return k == 4 ? (SideWindow(z) ? CarGlass : CarPaint) : CarPaint;
                    }, tag => tag == CarGlass ? dg : tag == CarSkip ? null : dp, Shade, false, false);
                    var hinge = new Vector3(side * 0.9f, 0.6f, d[0]);
                    m.Doors.Add(new DoorMesh { Paint = Offset(NatureBuild(dp, "door"), hinge), Glass = Offset(NatureBuild(dg, "door glass"), hinge), Hinge = hinge, Side = side });
                }
            }
            // Order: front-left, front-right, then rear-left, rear-right.
            m.Doors.Sort((a, b) => a.Hinge.z != b.Hinge.z ? b.Hinge.z.CompareTo(a.Hinge.z) : a.Side.CompareTo(b.Side));
            return m;
        }

        /// <summary>Moves a mesh so <paramref name="pivot"/> becomes its origin (door hinges).</summary>
        private static Mesh Offset(Mesh mesh, Vector3 pivot)
        {
            Vector3[] v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] -= pivot;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------ quad bike

        private static VehicleMeshes Atv(Color paint)
        {
            var body = new MeshBuilder();
            var trim = new MeshBuilder();
            var glass = new MeshBuilder();
            var rubber = new MeshBuilder();
            Color Paint(Vector3 p, Vector3 n) => new Color(paint.r, paint.g, paint.b, Mathf.Lerp(0.7f, 1f, Sstep(0.3f, 0.9f, p.y)));
            Color Steel(Vector3 p, Vector3 n) => new Color(0.55f, 0.55f, 0.56f, 0.8f);
            Color Dark(Vector3 p, Vector3 n) => new Color(0.18f, 0.17f, 0.17f, 0.85f);
            // Frame rails and the engine block between the wheels.
            ShadedBox(trim, new Vector3(-0.22f, 0.36f, 0f), new Vector3(0.06f, 0.06f, 1.5f), Quaternion.identity, 0.01f, Steel);
            ShadedBox(trim, new Vector3(0.22f, 0.36f, 0f), new Vector3(0.06f, 0.06f, 1.5f), Quaternion.identity, 0.01f, Steel);
            ShadedBox(trim, new Vector3(0f, 0.45f, 0.05f), new Vector3(0.42f, 0.32f, 0.55f), Quaternion.identity, 0.03f, Dark);
            // Fenders over each axle, the fuel tank and the seat.
            ShadedBox(body, new Vector3(0f, 0.72f, 0.66f), new Vector3(1.12f, 0.12f, 0.72f), Quaternion.Euler(-6f, 0f, 0f), 0.05f, Paint);
            ShadedBox(body, new Vector3(0f, 0.72f, -0.64f), new Vector3(1.12f, 0.12f, 0.74f), Quaternion.Euler(5f, 0f, 0f), 0.05f, Paint);
            ShadedBox(body, new Vector3(0f, 0.74f, 0.22f), new Vector3(0.42f, 0.2f, 0.42f), Quaternion.identity, 0.06f, Paint);
            ShadedBox(rubber, new Vector3(0f, 0.86f, -0.22f), new Vector3(0.36f, 0.1f, 0.68f), Quaternion.identity, 0.04f, (p, n) => new Color(0.16f, 0.15f, 0.15f, 1f));
            ShadedBox(body, new Vector3(0f, 0.62f, -0.2f), new Vector3(0.4f, 0.2f, 0.6f), Quaternion.identity, 0.03f, Paint);
            // Racks front and back.
            foreach (float z in new[] { 0.82f, -0.86f })
            {
                ShadedBox(trim, new Vector3(0f, 0.84f, z), new Vector3(0.9f, 0.03f, 0.42f), Quaternion.identity, 0.005f, Steel);
                foreach (float x in new[] { -0.44f, 0.44f })
                    ShadedBox(trim, new Vector3(x, 0.9f, z), new Vector3(0.03f, 0.1f, 0.42f), Quaternion.identity, 0.005f, Steel);
            }
            // Steering column, handlebar and grips, headlights.
            trim.Color = new Color(0.5f, 0.5f, 0.52f, 1f);
            trim.Cylinder(new Vector3(0f, 0.7f, 0.42f), new Vector3(0f, 1.02f, 0.3f), 0.025f, 0.025f, 8, true);
            trim.Cylinder(new Vector3(-0.36f, 1.04f, 0.3f), new Vector3(0.36f, 1.04f, 0.3f), 0.018f, 0.018f, 8, true);
            rubber.Color = new Color(0.12f, 0.12f, 0.12f, 1f);
            rubber.Cylinder(new Vector3(-0.38f, 1.04f, 0.3f), new Vector3(-0.28f, 1.04f, 0.3f), 0.026f, 0.026f, 8, true);
            rubber.Cylinder(new Vector3(0.28f, 1.04f, 0.3f), new Vector3(0.38f, 1.04f, 0.3f), 0.026f, 0.026f, 8, true);
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                glass.Color = new Color(2f, 2f, 1.9f, 1f);
                glass.Cylinder(new Vector3(x, 0.72f, 1.0f), new Vector3(x, 0.72f, 1.03f), 0.06f, 0.055f, 10, true);
            }
            trim.Color = Color.white;
            return new VehicleMeshes
            {
                Body = new[] { NatureBuild(body, "atv body"), NatureBuild(trim, "atv frame"), NatureBuild(glass, "atv lights"), NatureBuild(rubber, "atv seat") },
                Seat = new Vector3(0f, 0.55f, -0.25f)
            };
        }

        // ------------------------------------------------------------ wheels

        private static void Wheel(VehicleMeshes m, float radius, bool knobbly)
        {
            var rubber = new MeshBuilder();
            var rim = new MeshBuilder();
            float width = knobbly ? 0.26f : 0.21f;
            Matrix4x4 axle = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 90f), Vector3.one);
            Tyre(rubber, axle, radius, radius * 0.62f, width, 18, false, new Color(1f, 1f, 1f, 1f));
            if (knobbly)
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.PI * 2f / 14f;
                    ShadedBox(rubber, new Vector3(0f, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius), new Vector3(width * 0.9f, 0.04f, 0.06f), Quaternion.Euler(-a * Mathf.Rad2Deg, 0f, 0f), 0.005f, (p, n) => Color.white);
                }
            rim.Color = new Color(0.75f, 0.76f, 0.78f, 1f);
            rim.Cylinder(new Vector3(-width * 0.42f, 0f, 0f), new Vector3(width * 0.42f, 0f, 0f), radius * 0.62f, radius * 0.62f, 16, true);
            rim.Color = new Color(0.55f, 0.56f, 0.58f, 1f);
            rim.Cylinder(new Vector3(width * 0.42f, 0f, 0f), new Vector3(width * 0.5f, 0f, 0f), radius * 0.22f, radius * 0.18f, 10, true);
            rim.Cylinder(new Vector3(-width * 0.5f, 0f, 0f), new Vector3(-width * 0.42f, 0f, 0f), radius * 0.18f, radius * 0.22f, 10, true);
            rim.Color = Color.white;
            m.WheelRubber = NatureBuild(rubber, "wheel tyre");
            m.WheelRim = NatureBuild(rim, "wheel rim");
        }
    }
}
