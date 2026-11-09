using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Afterfall.Presentation
{
    /// <summary>
    /// The runtime half of the atmosphere, created next to the sun by <see cref="DayNightCycle.CreateLights"/>:
    /// the Afterfall/Sky skybox material, a realtime reflection probe that captures only the sky (so
    /// metal and water reflect the current sky instead of a fixed default), and a global volume that
    /// carries the time-of-day colour grade (exposure, white balance, saturation, contrast) on top
    /// of the scene's base grade. URP and Core RP types are reached by reflection so Presentation
    /// does not depend on the render pipeline assemblies (see <see cref="CameraSetup"/>).
    /// </summary>
    public sealed class SkyRig : MonoBehaviour
    {
        private const string VolumeTypeName = "UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime";
        private const string ProfileTypeName = "UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime";
        private const string ColorAdjustmentsTypeName = "UnityEngine.Rendering.Universal.ColorAdjustments, Unity.RenderPipelines.Universal.Runtime";
        private const string WhiteBalanceTypeName = "UnityEngine.Rendering.Universal.WhiteBalance, Unity.RenderPipelines.Universal.Runtime";

        private static Material _sky;
        private static bool _skyLoaded;
        private static readonly int ZenithId = Shader.PropertyToID("_ZenithColor");
        private static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
        private static readonly int HorizonGlowId = Shader.PropertyToID("_HorizonGlowColor");
        private static readonly int SunHaloId = Shader.PropertyToID("_SunHaloColor");
        private static readonly int SunDiscId = Shader.PropertyToID("_SunDiscColor");
        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int MoonColorId = Shader.PropertyToID("_MoonColor");
        private static readonly int MoonDirectionId = Shader.PropertyToID("_MoonDirection");
        private static readonly int StarsId = Shader.PropertyToID("_StarIntensity");
        private static readonly int CloudLitId = Shader.PropertyToID("_CloudLitColor");
        private static readonly int CloudShadeId = Shader.PropertyToID("_CloudShadeColor");

        private ReflectionProbe _probe;
        private float _probeElevation = float.NaN;
        private ScriptableObject _gradeProfile;
        private object _exposure, _contrast, _saturation, _temperature, _tint;

        /// <summary>The shared skybox material, or null when the sky shader is unavailable (then cameras clear to the fog colour).</summary>
        public static Material SkyMaterial
        {
            get
            {
                if (_skyLoaded) return _sky;
                _skyLoaded = true;
                Material asset = Resources.Load<Material>("Materials/AfterfallSky");
                if (asset != null && asset.shader != null && asset.shader.isSupported) _sky = new Material(asset) { name = "AfterfallSky (runtime)" };
                else Debug.LogWarning("[Afterfall] Sky material Resources/Materials/AfterfallSky is missing or unsupported; using a solid background.");
                return _sky;
            }
        }

        /// <summary>Adds the sky rig to the sun and creates its probe and grade volume under <paramref name="parent"/>.</summary>
        public static SkyRig Create(Transform parent, Light sun)
        {
            SkyRig rig = sun.gameObject.AddComponent<SkyRig>();
            rig.CreateProbe(parent);
            rig.CreateGradeVolume(parent);
            return rig;
        }

        private void CreateProbe(Transform parent)
        {
            var go = new GameObject("SkyReflection");
            go.transform.SetParent(parent, false);
            _probe = go.AddComponent<ReflectionProbe>();
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            _probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            _probe.cullingMask = 0;
            _probe.resolution = 128;
            _probe.hdr = true;
            _probe.boxProjection = false;
            _probe.importance = 0;
            _probe.blendDistance = 0f;
            _probe.size = new Vector3(8000f, 8000f, 8000f);
        }

        private void CreateGradeVolume(Transform parent)
        {
            Type volumeType = Type.GetType(VolumeTypeName), profileType = Type.GetType(ProfileTypeName);
            Type adjustType = Type.GetType(ColorAdjustmentsTypeName), balanceType = Type.GetType(WhiteBalanceTypeName);
            MethodInfo add = profileType?.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            if (volumeType == null || add == null || adjustType == null || balanceType == null)
            {
                Debug.LogWarning("[Afterfall] Volume types not found; the time-of-day grade is off (the base grade still applies).");
                return;
            }
            _gradeProfile = ScriptableObject.CreateInstance(profileType);
            _gradeProfile.name = "TimeOfDayGrade (runtime)";
            object adjust = add.Invoke(_gradeProfile, new object[] { adjustType, false });
            object balance = add.Invoke(_gradeProfile, new object[] { balanceType, false });
            _exposure = Parameter(adjust, "postExposure");
            _contrast = Parameter(adjust, "contrast");
            _saturation = Parameter(adjust, "saturation");
            _temperature = Parameter(balance, "temperature");
            _tint = Parameter(balance, "tint");

            // Configured while inactive so the volume registers with its final priority and profile.
            var go = new GameObject("TimeOfDayGrade");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            Component volume = go.AddComponent(volumeType);
            SetMember(volume, "isGlobal", true);
            SetMember(volume, "priority", 10f);
            SetMember(volume, "weight", 1f);
            SetMember(volume, "sharedProfile", _gradeProfile);
            go.SetActive(true);
        }

        /// <summary>Pushes one moment of the day to the sky material, the grade and (when the sky changed enough) the reflection probe.</summary>
        public void Apply(in SkyLook look, Vector3 toSun, Vector3 toMoon, float sunElevation)
        {
            Material sky = SkyMaterial;
            if (sky != null)
            {
                float sunUp = Mathf.Clamp01((toSun.y + 0.03f) / 0.06f);
                float moonUp = Mathf.Clamp01((toMoon.y + 0.03f) / 0.06f) * Mathf.Clamp01(-sunElevation / 6f);
                sky.SetVector(ZenithId, Linear(look.Zenith));
                sky.SetVector(HorizonId, Linear(look.Horizon));
                sky.SetVector(HorizonGlowId, Linear(look.HorizonGlow));
                sky.SetVector(SunHaloId, Linear(look.SunHalo));
                sky.SetVector(SunDiscId, Linear(look.SunLight) * (28f * sunUp));
                sky.SetVector(SunDirectionId, toSun);
                sky.SetVector(MoonColorId, Linear(new Color(0.86f, 0.89f, 0.97f)) * (1.6f * moonUp));
                sky.SetVector(MoonDirectionId, toMoon);
                sky.SetFloat(StarsId, look.Stars);
                sky.SetVector(CloudLitId, Linear(look.CloudLit));
                sky.SetVector(CloudShadeId, Linear(look.CloudShade));
                if (RenderSettings.skybox != sky) RenderSettings.skybox = sky;
            }

            if (_gradeProfile != null)
            {
                SetParameter(_exposure, look.Exposure);
                SetParameter(_contrast, look.Contrast);
                SetParameter(_saturation, look.Saturation);
                SetParameter(_temperature, look.Temperature);
                SetParameter(_tint, 2f);
            }

            // The sky changes slowly: re-capture it every degree of sun travel.
            if (_probe != null && _probe.isActiveAndEnabled && sky != null && !(Mathf.Abs(sunElevation - _probeElevation) < 1f))
            {
                _probeElevation = sunElevation;
                _probe.RenderProbe();
            }
        }

        /// <summary>Fog colour in sRGB: the sky's horizon including the faint part of the twilight band.</summary>
        public static Color FogColor(in SkyLook look) => (Linear(look.Horizon) + Linear(look.HorizonGlow) * 0.15f).gamma;

        private static Color Linear(Color c) => new Color(c.r, c.g, c.b, 1f).linear;

        private static object Parameter(object component, string field)
        {
            FieldInfo f = component?.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            object p = f?.GetValue(component);
            if (p == null) Debug.LogWarning($"[Afterfall] Grade parameter '{field}' not found.");
            return p;
        }

        private static void SetParameter(object parameter, float value)
        {
            if (parameter == null) return;
            Type t = parameter.GetType();
            try
            {
                t.GetProperty("overrideState", BindingFlags.Public | BindingFlags.Instance)?.SetValue(parameter, true);
                t.GetProperty("value", BindingFlags.Public | BindingFlags.Instance)?.SetValue(parameter, value);
            }
            catch (AmbiguousMatchException)
            {
                // Fall back to the serialized fields the parameter properties wrap.
                t.GetField("m_OverrideState", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)?.SetValue(parameter, true);
                FieldInfo v = null;
                for (Type b = t; b != null && v == null; b = b.BaseType) v = b.GetField("m_Value", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                v?.SetValue(parameter, value);
            }
        }

        private static void SetMember(object target, string name, object value)
        {
            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                p.SetValue(target, value);
                return;
            }
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
            else Debug.LogWarning($"[Afterfall] Volume member '{name}' not found.");
        }

        private void OnDestroy()
        {
            if (_gradeProfile != null) Destroy(_gradeProfile);
        }
    }
}
