using System.IO;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;
using Afterfall.Persistence;
using Afterfall.Presentation.UI;
using Afterfall.Services;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Scene entry point (the only component in Main.unity). Loads content, settings and input,
    /// shows the main menu and starts/loads/ends game sessions. Everything else is created in code.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private GameController _game;

        public AppSettings Settings { get; private set; }
        public GameInput Input { get; private set; }
        public ContentDatabase Content { get; private set; }
        public SaveService Saves { get; private set; }
        public UiManager Ui { get; private set; }
        public string ContentError { get; private set; }

        public static string SaveDirectory => Path.Combine(Application.persistentDataPath, "Saves");

        private void Awake()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            Settings = AppSettings.Load();
            AudioListener.volume = Settings.MasterVolume;
            Input = new GameInput();
            Saves = new SaveService(new SaveFileStore(SaveDirectory));
            Content = LoadContent(out string error);
            ContentError = error;
            Ui = UiManager.Create(this);
            Ui.ShowMainMenu();
            if (!string.IsNullOrEmpty(error)) Ui.Toast("Content failed to load: " + error, Severity.Danger);
            AutoTour.TryAttach(this);
        }

        public static ContentDatabase LoadContent(out string error)
        {
            error = null;
            try
            {
                ContentDatabase db = ContentJson.Load(ContentJson.FromLookup(name =>
                {
                    var asset = Resources.Load<TextAsset>("Content/" + name);
                    return asset != null ? asset.text : null;
                }));
                var problems = ContentValidator.Validate(db);
                if (problems.Count > 0)
                {
                    error = $"{problems.Count} content problem(s): {problems[0]}";
                    foreach (string p in problems) Debug.LogError("[Afterfall content] " + p);
                }
                return db;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                Debug.LogException(ex);
                return new ContentDatabase();
            }
        }

        public void StartNewGame(DifficultyPreset preset, int seed)
        {
            EndGame();
            Ui.HideMainMenu();
            Ui.ShowLoading("Generating world…");
            StartCoroutine(NewGameRoutine(preset, seed));
        }

        private System.Collections.IEnumerator NewGameRoutine(DifficultyPreset preset, int seed)
        {
            yield return null; // let the loading screen render before the synchronous generation step
            GameSession session = NewGameFactory.Create(Content, seed, GameSettings.FromPreset(preset));
            _game = GameController.Create(this, session, "save1");
        }

        public void LoadGame(string slot)
        {
            if (!Saves.Store.Exists(slot))
            {
                Ui.Toast($"No save named '{slot}'.", Severity.Warning);
                return;
            }
            EndGame();
            Ui.HideMainMenu();
            Ui.ShowLoading("Loading " + slot + "…");
            StartCoroutine(LoadRoutine(slot));
        }

        private System.Collections.IEnumerator LoadRoutine(string slot)
        {
            yield return null;
            LoadResult result = Saves.Load(slot, Content);
            if (!result.Ok)
            {
                Ui.HideLoading();
                Ui.ShowMainMenu();
                Ui.Toast("Could not load: " + result.Error, Severity.Danger);
                yield break;
            }
            _game = GameController.Create(this, result.Session, slot == SaveService.AutoSlot || slot == SaveService.QuickSlot ? "save1" : slot);
            foreach (string w in result.Warnings) Ui.Toast(w, Severity.Warning);
        }

        public void ReturnToMainMenu()
        {
            EndGame();
            Ui.ShowMainMenu();
        }

        private void EndGame()
        {
            if (_game == null) return;
            Ui.DetachGame();
            Destroy(_game.gameObject);
            _game = null;
            // Terrain data, generated textures, meshes and NavMesh data of the old session.
            Resources.UnloadUnusedAssets();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void ApplySettings()
        {
            Ui.ApplyScale(Settings.UiScale);
            AudioListener.volume = Settings.MasterVolume;
            if (_game != null && _game.CameraRig != null) _game.CameraRig.ApplySettings(Settings);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (_game != null) Destroy(_game.gameObject);
            if (Ui != null) Destroy(Ui.gameObject);
            Input?.Dispose();
        }

        /// <summary>The running game, if any (used by tools and PlayMode tests).</summary>
        public GameController Game => _game;
    }
}
