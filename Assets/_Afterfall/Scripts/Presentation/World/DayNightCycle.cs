using Afterfall.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Drives the atmosphere from the simulation clock: sun and moon (direction, colour, intensity,
    /// shadows), the sky (<see cref="SkyRig"/>), ambient light, fog and the time-of-day colour grade,
    /// all from one keyed palette (<see cref="SkyPalette"/>) so they always agree.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        private GameController _gc;
        private Light _sun, _moon;

        public void Initialize(GameController gc)
        {
            _gc = gc;
            CreateLights(transform, out _sun, out _moon);
        }

        /// <summary>Creates the sun and moon lights, the sky rig and the static render settings (shared with the model gallery).</summary>
        public static void CreateLights(Transform parent, out Light sun, out Light moon)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.88f;
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(parent, false);
            moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            // The sun and moon never shine together, so whichever is up is URP's main light and casts shadows.
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.6f;
            moon.color = new Color(0.62f, 0.72f, 0.98f);
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 1f;
            SkyRig.Create(parent, sun);
        }

        private void Update()
        {
            if (_gc?.Session == null) return;
            SimulationClock clock = _gc.Session.World.Clock;
            Camera cam = _gc.CameraRig != null ? _gc.CameraRig.Camera : null;
            Apply((float)clock.TimeOfDay01, clock.Daylight01, _sun, _moon, cam);
        }

        /// <summary>
        /// Sets sun, moon, sky, ambient light, fog and the colour grade for a time of day (0..1,
        /// 0.5 = noon), and makes <paramref name="cam"/> clear to the sky. Everything visual follows
        /// the sun's elevation at that time so sky, light and shadows agree; <paramref name="daylight"/>
        /// (the clock's gameplay daylight) is accepted for compatibility. Shared with the model gallery.
        /// </summary>
        public static void Apply(float timeOfDay01, float daylight, Light sun, Light moon, Camera cam)
        {
            Vector3 toSun = SkyPalette.SunDirection(timeOfDay01);
            Vector3 toMoon = SkyPalette.MoonDirection(timeOfDay01);
            float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            SkyLook look = SkyPalette.Evaluate(elevation);

            sun.transform.rotation = Quaternion.LookRotation(-AboveHorizon(toSun));
            sun.color = look.SunLight;
            sun.intensity = look.SunIntensity;
            sun.shadowStrength = look.ShadowStrength;
            sun.enabled = sun.intensity > 0.005f;

            // Moonlight fades in after the sun has set and out as the moon nears the horizon.
            moon.transform.rotation = Quaternion.LookRotation(-AboveHorizon(toMoon));
            moon.intensity = look.MoonIntensity * Mathf.Clamp01(toMoon.y * 6f);
            moon.shadowStrength = look.ShadowStrength;
            moon.enabled = !sun.enabled && moon.intensity > 0.005f;

            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;
            Color fog = SkyRig.FogColor(look);
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = look.FogDensity;

            SkyRig rig = sun.GetComponent<SkyRig>();
            if (rig != null) rig.Apply(look, toSun, toMoon, elevation);
            if (cam != null)
            {
                bool sky = SkyRig.SkyMaterial != null;
                cam.clearFlags = sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                cam.backgroundColor = fog;
            }
        }

        /// <summary>Light never comes from under the ground: a body near the horizon shines at least 1.5° from above.</summary>
        private static Vector3 AboveHorizon(Vector3 dir)
        {
            dir.y = Mathf.Max(dir.y, 0.026f);
            return dir.normalized;
        }
    }
}
