using Afterfall.Core;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Boundary conversions between engine-independent domain types and Unity types.</summary>
    public static class Conversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToSim(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Color ParseColor(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }
    }
}
