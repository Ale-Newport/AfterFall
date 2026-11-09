using Afterfall.Core;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Drives the sun, moon, ambient light and fog from the simulation clock.</summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        private GameController _gc;
        private Light _sun, _moon;

        public void Initialize(GameController gc)
        {
            _gc = gc;
            CreateLights(transform, out _sun, out _moon);
        }

        /// <summary>Creates the sun and moon lights and the static render settings (shared with the model gallery).</summary>
        public static void CreateLights(Transform parent, out Light sun, out Light moon)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(parent, false);
            moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.shadows = LightShadows.None;
            moon.color = new Color(0.55f, 0.65f, 0.9f);
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        }

        private void Update()
        {
            if (_gc?.Session == null) return;
            SimulationClock clock = _gc.Session.World.Clock;
            Camera cam = _gc.CameraRig != null ? _gc.CameraRig.Camera : null;
            Apply((float)clock.TimeOfDay01, clock.Daylight01, _sun, _moon, cam);
        }

        /// <summary>
        /// Sets sun, moon, ambient light, fog and the camera background for a time of day (0..1,
        /// 0.5 = noon) and a daylight factor (0 night .. 1 day). Shared with the model gallery.
        /// </summary>
        public static void Apply(float timeOfDay01, float daylight, Light sun, Light moon, Camera cam)
        {
            // Sun rises at ~06:00 in the east (+X) and sets at ~18:00.
            float sunAngle = (timeOfDay01 - 0.25f) * 360f;
            sun.transform.rotation = Quaternion.Euler(sunAngle, -35f, 0f);
            float elevation = Mathf.Sin((timeOfDay01 - 0.25f) * Mathf.PI * 2f);
            sun.intensity = Mathf.Clamp01(elevation * 2.2f) * 1.25f;
            sun.enabled = sun.intensity > 0.01f;
            Color warm = new Color(1f, 0.62f, 0.38f);
            Color noon = new Color(1f, 0.96f, 0.88f);
            sun.color = Color.Lerp(warm, noon, Mathf.Clamp01(elevation * 2.5f));
            moon.transform.rotation = Quaternion.Euler(sunAngle + 180f, -35f, 0f);
            moon.intensity = Mathf.Clamp01(-elevation * 2f) * 0.22f;
            moon.enabled = moon.intensity > 0.01f;

            Color skyDay = new Color(0.55f, 0.62f, 0.68f), skyNight = new Color(0.06f, 0.08f, 0.13f);
            RenderSettings.ambientSkyColor = Color.Lerp(skyNight, skyDay, daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.05f, 0.06f, 0.08f), new Color(0.42f, 0.44f, 0.4f), daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.03f, 0.03f, 0.04f), new Color(0.2f, 0.18f, 0.14f), daylight);
            RenderSettings.fogColor = Color.Lerp(new Color(0.05f, 0.06f, 0.09f), new Color(0.62f, 0.66f, 0.66f), daylight);
            RenderSettings.fogStartDistance = Mathf.Lerp(15f, 70f, daylight);
            RenderSettings.fogEndDistance = Mathf.Lerp(120f, 420f, daylight);
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
            }
        }
    }
}
