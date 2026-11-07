using System;
using System.Collections.Generic;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.World;
using Afterfall.Persistence;

namespace Afterfall.Services
{
    public sealed class LoadResult
    {
        public GameSession Session { get; set; }
        public bool RecoveredFromBackup { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public string Error { get; set; } = "";
        public bool Ok => Session != null;
    }

    /// <summary>Save/load orchestration with backup recovery.</summary>
    public sealed class SaveService
    {
        public const string QuickSlot = "quicksave";
        public const string AutoSlot = "autosave";

        private readonly SaveFileStore _store;

        public SaveService(SaveFileStore store)
        {
            _store = store;
        }

        public SaveFileStore Store => _store;

        public bool Save(GameSession session, string slot, double playtimeSeconds, out string message)
        {
            try
            {
                session.World.Metrics.Saves++;
                string text = SaveSerializer.Serialize(session.World, slot, playtimeSeconds);
                _store.Write(slot, text);
                message = $"Game saved ({slot}).";
                session.Events.Publish(new SaveCompleted(slot, true, message));
                return true;
            }
            catch (Exception ex)
            {
                message = "Save failed: " + ex.Message;
                session.Events.Publish(new SaveCompleted(slot, false, message));
                session.Events.Publish(new Notification(message, Severity.Danger));
                return false;
            }
        }

        public LoadResult Load(string slot, ContentDatabase content)
        {
            var result = new LoadResult();
            if (!_store.Exists(slot))
            {
                result.Error = $"No save named '{slot}'.";
                return result;
            }
            try
            {
                WorldState world = SaveSerializer.Deserialize(_store.ReadMain(slot), content, result.Warnings);
                result.Session = new GameSession(content, world);
                return result;
            }
            catch (Exception ex) when (ex is SaveCorruptException || ex is FormatException || ex is System.IO.IOException)
            {
                string backup = _store.ReadBackup(slot);
                if (backup == null)
                {
                    result.Error = ex.Message;
                    return result;
                }
                try
                {
                    WorldState world = SaveSerializer.Deserialize(backup, content, result.Warnings);
                    result.Session = new GameSession(content, world);
                    result.RecoveredFromBackup = true;
                    result.Warnings.Add("Main save was damaged (" + ex.Message + "); loaded the previous backup.");
                    return result;
                }
                catch (Exception ex2)
                {
                    result.Error = ex.Message + " Backup also failed: " + ex2.Message;
                    return result;
                }
            }
        }
    }
}
