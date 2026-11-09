using System;
using System.Reflection;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// URP per-camera settings (post-processing, anti-aliasing). Cameras created in code start with
    /// post-processing off. The URP runtime assembly is reached by reflection so Presentation does
    /// not depend on it (the headless compile check does not build URP).
    /// </summary>
    public static class CameraSetup
    {
        private static readonly Type DataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");

        /// <summary>Turns on post-processing (the scene volume's tonemapping, bloom, grading...) and SMAA.</summary>
        public static void EnablePostProcessing(Camera cam, bool antialiasing = true)
        {
            if (cam == null || DataType == null) return;
            Component data = cam.GetComponent(DataType) ?? cam.gameObject.AddComponent(DataType);
            Set(data, "renderPostProcessing", true);
            if (antialiasing)
            {
                Set(data, "antialiasing", "SubpixelMorphologicalAntiAliasing");
                Set(data, "antialiasingQuality", "High");
            }
            Set(data, "dithering", true);
            // A single NaN pixel (degenerate normal, zero-length vector) would otherwise bloom into a black square.
            Set(data, "stopNaN", true);
        }

        /// <summary>Sets a property by name; enum values may be given as their names.</summary>
        public static void Set(Component data, string property, object value)
        {
            PropertyInfo p = data != null ? data.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance) : null;
            if (p == null || !p.CanWrite)
            {
                Debug.LogWarning($"[Afterfall] Camera setting '{property}' is not available.");
                return;
            }
            if (value is string name && p.PropertyType.IsEnum) value = Enum.Parse(p.PropertyType, name);
            p.SetValue(data, value);
        }
    }
}
