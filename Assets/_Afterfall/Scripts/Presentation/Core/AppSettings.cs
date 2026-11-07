using UnityEngine;

namespace Afterfall.Presentation
{
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

        public static AppSettings Load()
        {
            var s = new AppSettings
            {
                MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "sensitivity", 1f),
                InvertY = PlayerPrefs.GetInt(Prefix + "invertY", 0) == 1,
                UiScale = PlayerPrefs.GetFloat(Prefix + "uiScale", 1f),
                FieldOfView = PlayerPrefs.GetFloat(Prefix + "fov", 55f),
                ShowFps = PlayerPrefs.GetInt(Prefix + "fps", 0) == 1,
                CameraShake = PlayerPrefs.GetInt(Prefix + "shake", 1) == 1
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
            PlayerPrefs.Save();
        }

        private void Clamp()
        {
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.1f, 4f);
            UiScale = Mathf.Clamp(UiScale, 0.7f, 1.6f);
            FieldOfView = Mathf.Clamp(FieldOfView, 40f, 80f);
        }
    }
}
