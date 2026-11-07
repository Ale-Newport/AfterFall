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
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.85f;
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(transform, false);
            _moon = moonGo.AddComponent<Light>();
            _moon.type = LightType.Directional;
            _moon.shadows = LightShadows.None;
            _moon.color = new Color(0.55f, 0.65f, 0.9f);
            RenderSettings.sun = _sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        }

        private void Update()
        {
            if (_gc?.Session == null) return;
            SimulationClock clock = _gc.Session.World.Clock;
            float t = (float)clock.TimeOfDay01;
            float daylight = clock.Daylight01;
            // Sun rises at ~06:00 in the east (+X) and sets at ~18:00.
            float sunAngle = (t - 0.25f) * 360f;
            _sun.transform.rotation = Quaternion.Euler(sunAngle, -35f, 0f);
            float elevation = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f);
            _sun.intensity = Mathf.Clamp01(elevation * 2.2f) * 1.25f;
            _sun.enabled = _sun.intensity > 0.01f;
            Color warm = new Color(1f, 0.62f, 0.38f);
            Color noon = new Color(1f, 0.96f, 0.88f);
            _sun.color = Color.Lerp(warm, noon, Mathf.Clamp01(elevation * 2.5f));
            _moon.transform.rotation = Quaternion.Euler(sunAngle + 180f, -35f, 0f);
            _moon.intensity = Mathf.Clamp01(-elevation * 2f) * 0.22f;
            _moon.enabled = _moon.intensity > 0.01f;

            Color skyDay = new Color(0.55f, 0.62f, 0.68f), skyNight = new Color(0.06f, 0.08f, 0.13f);
            RenderSettings.ambientSkyColor = Color.Lerp(skyNight, skyDay, daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.05f, 0.06f, 0.08f), new Color(0.42f, 0.44f, 0.4f), daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.03f, 0.03f, 0.04f), new Color(0.2f, 0.18f, 0.14f), daylight);
            RenderSettings.fogColor = Color.Lerp(new Color(0.05f, 0.06f, 0.09f), new Color(0.62f, 0.66f, 0.66f), daylight);
            RenderSettings.fogStartDistance = Mathf.Lerp(15f, 70f, daylight);
            RenderSettings.fogEndDistance = Mathf.Lerp(120f, 420f, daylight);
            Camera cam = _gc.CameraRig != null ? _gc.CameraRig.Camera : null;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
            }
        }
    }
}
