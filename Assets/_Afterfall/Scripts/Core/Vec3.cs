using System;

namespace Afterfall.Core
{
    /// <summary>
    /// Engine-independent 3D vector used by the domain model and persistence.
    /// The presentation layer converts to/from UnityEngine.Vector3 at the boundary.
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public float SqrMagnitude => X * X + Y * Y + Z * Z;

        public Vec3 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec3(X / m, Y / m, Z / m) : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        /// <summary>Horizontal (XZ-plane) distance; most gameplay ranges ignore height.</summary>
        public static float DistanceXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();
        public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
    }
}
