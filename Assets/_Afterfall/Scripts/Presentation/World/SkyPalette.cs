using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Everything the sky, the lights, the fog and the colour grade need at one moment of the day.
    /// Colours are authored in sRGB (as picked in a colour picker); intensities are linear.
    /// </summary>
    public struct SkyLook
    {
        public Color Zenith, Horizon, HorizonGlow, SunHalo, CloudLit, CloudShade;
        public Color SunLight, AmbientSky, AmbientEquator, AmbientGround;
        public float SunIntensity, MoonIntensity, ShadowStrength, FogDensity, Stars;
        /// <summary>Colour grade: post exposure (EV), white balance temperature, saturation and contrast.</summary>
        public float Exposure, Temperature, Saturation, Contrast;

        public static SkyLook Lerp(SkyLook a, SkyLook b, float t)
        {
            return new SkyLook
            {
                Zenith = Color.Lerp(a.Zenith, b.Zenith, t),
                Horizon = Color.Lerp(a.Horizon, b.Horizon, t),
                HorizonGlow = Color.Lerp(a.HorizonGlow, b.HorizonGlow, t),
                SunHalo = Color.Lerp(a.SunHalo, b.SunHalo, t),
                CloudLit = Color.Lerp(a.CloudLit, b.CloudLit, t),
                CloudShade = Color.Lerp(a.CloudShade, b.CloudShade, t),
                SunLight = Color.Lerp(a.SunLight, b.SunLight, t),
                AmbientSky = Color.Lerp(a.AmbientSky, b.AmbientSky, t),
                AmbientEquator = Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
                AmbientGround = Color.Lerp(a.AmbientGround, b.AmbientGround, t),
                SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
                MoonIntensity = Mathf.Lerp(a.MoonIntensity, b.MoonIntensity, t),
                ShadowStrength = Mathf.Lerp(a.ShadowStrength, b.ShadowStrength, t),
                FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),
                Stars = Mathf.Lerp(a.Stars, b.Stars, t),
                Exposure = Mathf.Lerp(a.Exposure, b.Exposure, t),
                Temperature = Mathf.Lerp(a.Temperature, b.Temperature, t),
                Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
                Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
            };
        }
    }

    /// <summary>
    /// The time-of-day model: where the sun and moon are, and a keyed palette over the sun's
    /// elevation (night, blue hour, twilight, golden hour, day) for a slightly warm, hazy,
    /// post-collapse mood. Pure function of the time of day, so saves and the gallery look the same.
    /// </summary>
    public static class SkyPalette
    {
        // A summer day at 40°N: sunrise about 05:45, solar noon 12:30, sunset about 19:15 (the clock's
        // daylight ramps over 05:00–07:00 and 18:30–20:00). The noon sun stays 63° high, so shadows always
        // have a direction. The yaw keeps the compass of the original sun path (sunrise towards +X).
        private const float Latitude = 40f, Declination = 13f, SolarNoonHours = 12.5f, CompassYaw = -35f;

        /// <summary>Unit vector from the ground towards the sun at a time of day (0..1, 0.5 = noon).</summary>
        public static Vector3 SunDirection(float timeOfDay01) => Celestial(timeOfDay01, Declination);

        /// <summary>Unit vector towards the moon: roughly opposite the sun, on a slightly lower arc.</summary>
        public static Vector3 MoonDirection(float timeOfDay01) => Celestial(timeOfDay01 + 0.5f, -Declination * 0.6f);

        private static Vector3 Celestial(float timeOfDay01, float declination)
        {
            float hourAngle = (timeOfDay01 * 24f - SolarNoonHours) * 15f * Mathf.Deg2Rad;
            float lat = Latitude * Mathf.Deg2Rad, dec = declination * Mathf.Deg2Rad;
            // East = +X, north = +Z. The noon direction is south, raised by (90° - latitude).
            var east = new Vector3(1f, 0f, 0f);
            var noon = new Vector3(0f, Mathf.Cos(lat), -Mathf.Sin(lat));
            var pole = new Vector3(0f, Mathf.Sin(lat), Mathf.Cos(lat));
            Vector3 dir = east * (-Mathf.Sin(hourAngle) * Mathf.Cos(dec)) + noon * (Mathf.Cos(hourAngle) * Mathf.Cos(dec)) + pole * Mathf.Sin(dec);
            return (Quaternion.Euler(0f, CompassYaw, 0f) * dir).normalized;
        }

        /// <summary>The palette at a sun elevation in degrees (negative = below the horizon).</summary>
        public static SkyLook Evaluate(float sunElevationDegrees)
        {
            if (sunElevationDegrees <= Keys[0].Elevation) return Keys[0].Look;
            for (int i = 1; i < Keys.Length; i++)
            {
                if (sunElevationDegrees > Keys[i].Elevation) continue;
                float t = Mathf.InverseLerp(Keys[i - 1].Elevation, Keys[i].Elevation, sunElevationDegrees);
                return SkyLook.Lerp(Keys[i - 1].Look, Keys[i].Look, Mathf.SmoothStep(0f, 1f, t));
            }
            return Keys[Keys.Length - 1].Look;
        }

        private struct Key
        {
            public float Elevation;
            public SkyLook Look;
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        private static readonly Key[] Keys =
        {
            // Deep night: moonlit blue, readable silhouettes against a lighter horizon.
            new Key { Elevation = -16f, Look = new SkyLook
            {
                Zenith = C(0.035f, 0.05f, 0.1f), Horizon = C(0.12f, 0.15f, 0.21f), HorizonGlow = C(0f, 0f, 0f), SunHalo = C(0f, 0f, 0f),
                CloudLit = C(0.15f, 0.17f, 0.23f), CloudShade = C(0.07f, 0.08f, 0.12f),
                SunLight = C(1f, 0.4f, 0.2f), SunIntensity = 0f, MoonIntensity = 0.5f, ShadowStrength = 0.6f,
                AmbientSky = C(0.22f, 0.27f, 0.4f), AmbientEquator = C(0.15f, 0.17f, 0.24f), AmbientGround = C(0.08f, 0.08f, 0.09f),
                FogDensity = 0.006f, Stars = 1f, Exposure = 1.1f, Temperature = -16f, Saturation = -28f, Contrast = 4f,
            } },
            // Blue hour.
            new Key { Elevation = -7f, Look = new SkyLook
            {
                Zenith = C(0.08f, 0.12f, 0.24f), Horizon = C(0.27f, 0.26f, 0.32f), HorizonGlow = C(0.2f, 0.1f, 0.07f), SunHalo = C(0.08f, 0.04f, 0.03f),
                CloudLit = C(0.3f, 0.22f, 0.25f), CloudShade = C(0.11f, 0.11f, 0.16f),
                SunLight = C(1f, 0.4f, 0.2f), SunIntensity = 0f, MoonIntensity = 0.3f, ShadowStrength = 0.6f,
                AmbientSky = C(0.28f, 0.32f, 0.46f), AmbientEquator = C(0.25f, 0.23f, 0.29f), AmbientGround = C(0.09f, 0.09f, 0.1f),
                FogDensity = 0.005f, Stars = 0.45f, Exposure = 1f, Temperature = -9f, Saturation = -16f, Contrast = 4f,
            } },
            // Civil twilight, the sun just under the horizon.
            new Key { Elevation = -1.5f, Look = new SkyLook
            {
                Zenith = C(0.17f, 0.25f, 0.43f), Horizon = C(0.58f, 0.45f, 0.42f), HorizonGlow = C(0.55f, 0.24f, 0.1f), SunHalo = C(0.5f, 0.24f, 0.12f),
                CloudLit = C(0.86f, 0.48f, 0.36f), CloudShade = C(0.28f, 0.25f, 0.32f),
                SunLight = C(1f, 0.42f, 0.2f), SunIntensity = 0f, MoonIntensity = 0f, ShadowStrength = 0.75f,
                AmbientSky = C(0.46f, 0.5f, 0.66f), AmbientEquator = C(0.56f, 0.45f, 0.43f), AmbientGround = C(0.16f, 0.13f, 0.12f),
                FogDensity = 0.0042f, Stars = 0.04f, Exposure = 0.95f, Temperature = 0f, Saturation = -6f, Contrast = 5f,
            } },
            // Golden hour, low sun.
            new Key { Elevation = 4f, Look = new SkyLook
            {
                Zenith = C(0.27f, 0.41f, 0.62f), Horizon = C(0.86f, 0.7f, 0.54f), HorizonGlow = C(0.6f, 0.32f, 0.14f), SunHalo = C(1f, 0.6f, 0.32f),
                CloudLit = C(1f, 0.74f, 0.54f), CloudShade = C(0.46f, 0.42f, 0.48f),
                SunLight = C(1f, 0.58f, 0.32f), SunIntensity = 1.5f, MoonIntensity = 0f, ShadowStrength = 0.82f,
                AmbientSky = C(0.66f, 0.7f, 0.84f), AmbientEquator = C(0.8f, 0.67f, 0.56f), AmbientGround = C(0.3f, 0.24f, 0.18f),
                FogDensity = 0.0034f, Stars = 0f, Exposure = 0.85f, Temperature = 7f, Saturation = 0f, Contrast = 6f,
            } },
            // Morning / late afternoon.
            new Key { Elevation = 16f, Look = new SkyLook
            {
                Zenith = C(0.3f, 0.47f, 0.7f), Horizon = C(0.79f, 0.77f, 0.7f), HorizonGlow = C(0.16f, 0.1f, 0.05f), SunHalo = C(0.9f, 0.76f, 0.56f),
                CloudLit = C(1f, 0.94f, 0.86f), CloudShade = C(0.56f, 0.57f, 0.63f),
                SunLight = C(1f, 0.82f, 0.62f), SunIntensity = 2.05f, MoonIntensity = 0f, ShadowStrength = 0.86f,
                AmbientSky = C(0.67f, 0.75f, 0.87f), AmbientEquator = C(0.75f, 0.71f, 0.63f), AmbientGround = C(0.32f, 0.28f, 0.21f),
                FogDensity = 0.0027f, Stars = 0f, Exposure = 0.6f, Temperature = 5f, Saturation = -3f, Contrast = 7f,
            } },
            // High sun: dusty pale-blue sky, warm haze at the horizon.
            new Key { Elevation = 45f, Look = new SkyLook
            {
                Zenith = C(0.29f, 0.47f, 0.72f), Horizon = C(0.74f, 0.78f, 0.78f), HorizonGlow = C(0f, 0f, 0f), SunHalo = C(0.85f, 0.8f, 0.7f),
                CloudLit = C(1f, 0.99f, 0.96f), CloudShade = C(0.6f, 0.63f, 0.69f),
                SunLight = C(1f, 0.94f, 0.84f), SunIntensity = 2.3f, MoonIntensity = 0f, ShadowStrength = 0.88f,
                AmbientSky = C(0.56f, 0.65f, 0.78f), AmbientEquator = C(0.64f, 0.67f, 0.64f), AmbientGround = C(0.3f, 0.27f, 0.21f),
                FogDensity = 0.0024f, Stars = 0f, Exposure = 0.35f, Temperature = 4f, Saturation = -5f, Contrast = 7f,
            } },
        };
    }
}
