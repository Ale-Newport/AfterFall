using Afterfall.Core;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Boundary conversions between engine-independent domain types and Unity types. In the 64 km
    /// basin Unity's coordinates are the world's minus a floating <see cref="Origin"/> (moved in 512 m
    /// steps to stay near the survivor, so positions keep millimetre precision); elsewhere it is zero.
    /// </summary>
    public static class Conversions
    {
        /// <summary>The world point at Unity's origin (x and z only).</summary>
        public static Vector3 Origin { get; set; }

        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X - Origin.x, v.Y, v.Z - Origin.z);
        public static Vec3 ToSim(this Vector3 v) => new Vec3(v.x + Origin.x, v.y, v.z + Origin.z);

        /// <summary>A Unity position as a world position (still a Vector3).</summary>
        public static Vector3 ToWorld(this Vector3 v) => new Vector3(v.x + Origin.x, v.y, v.z + Origin.z);

        /// <summary>A world position as a Unity position.</summary>
        public static Vector3 FromWorld(this Vector3 w) => new Vector3(w.x - Origin.x, w.y, w.z - Origin.z);

        public static Color ParseColor(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }
    }
}
