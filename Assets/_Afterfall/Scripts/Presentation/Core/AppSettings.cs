using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>Sections of the F3 developer diagnostics overlay, each switchable on its own.</summary>
    [System.Flags]
    public enum DiagnosticSection
    {
        None = 0,
        Performance = 1,
        Simulation = 2,
        Entities = 4,
        Position = 8,
        Navigation = 16,
        Power = 32,
        Economy = 64,
        Saves = 128,
        Default = Performance | Simulation | Entities | Position | Navigation
    }

    /// <summary>Per-user preferences (not part of save games). Stored in PlayerPrefs.</summary>
    public sealed class AppSettings
    {
        private const string Prefix = "afterfall.settings.";

        public float MouseSensitivity = 1f;
        public bool InvertY;
        public float UiScale = 1f;
        public float FieldOfView = 55f;
        public bool ShowFps;
        public bool CameraShake = true;
        public float MasterVolume = 0.8f;
        public bool Captions = true;

        /// <summary>Creative item browser: favourite and recently spawned item ids (comma-separated).</summary>
        public string CreativeFavorites = "";
        public string CreativeRecent = "";

        /// <summary>Which sections the F3 diagnostics overlay shows (<see cref="DiagnosticSection"/> flags).</summary>
        public int DiagnosticSections = (int)DiagnosticSection.Default;

        public static AppSettings Load()
        {
            var s = new AppSettings
            {
                MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "sensitivity", 1f),
                InvertY = PlayerPrefs.GetInt(Prefix + "invertY", 0) == 1,
                UiScale = PlayerPrefs.GetFloat(Prefix + "uiScale", 1f),
                FieldOfView = PlayerPrefs.GetFloat(Prefix + "fov", 55f),
                ShowFps = PlayerPrefs.GetInt(Prefix + "fps", 0) == 1,
                CameraShake = PlayerPrefs.GetInt(Prefix + "shake", 1) == 1,
                MasterVolume = PlayerPrefs.GetFloat(Prefix + "volume", 0.8f),
                Captions = PlayerPrefs.GetInt(Prefix + "captions", 1) == 1,
                CreativeFavorites = PlayerPrefs.GetString(Prefix + "creativeFavorites", ""),
                CreativeRecent = PlayerPrefs.GetString(Prefix + "creativeRecent", ""),
                DiagnosticSections = PlayerPrefs.GetInt(Prefix + "diagnostics", (int)DiagnosticSection.Default)
            };
            s.Clamp();
            return s;
        }

        public void Save()
        {
            Clamp();
            PlayerPrefs.SetFloat(Prefix + "sensitivity", MouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "invertY", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "uiScale", UiScale);
            PlayerPrefs.SetFloat(Prefix + "fov", FieldOfView);
            PlayerPrefs.SetInt(Prefix + "fps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "shake", CameraShake ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "volume", MasterVolume);
            PlayerPrefs.SetInt(Prefix + "captions", Captions ? 1 : 0);
            PlayerPrefs.SetString(Prefix + "creativeFavorites", CreativeFavorites ?? "");
            PlayerPrefs.SetString(Prefix + "creativeRecent", CreativeRecent ?? "");
            PlayerPrefs.SetInt(Prefix + "diagnostics", DiagnosticSections);
            PlayerPrefs.Save();
        }

        private void Clamp()
        {
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.1f, 4f);
            UiScale = Mathf.Clamp(UiScale, 0.7f, 1.6f);
            FieldOfView = Mathf.Clamp(FieldOfView, 40f, 80f);
            MasterVolume = Mathf.Clamp01(MasterVolume);
        }
    }
}
