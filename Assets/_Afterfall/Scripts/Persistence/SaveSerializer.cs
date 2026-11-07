using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Afterfall.Domain.Content;
using Afterfall.Domain.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Afterfall.Persistence
{
    public sealed class SaveCorruptException : Exception
    {
        public SaveCorruptException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Ordered schema migrations applied to the raw JSON payload before it is bound to DTOs.
    /// Register a step for every schema bump: Register(1, payload => { ...; return payload; }).
    /// </summary>
    public static class SaveMigrations
    {
        private static readonly SortedDictionary<int, Func<JObject, JObject>> Steps = new SortedDictionary<int, Func<JObject, JObject>>();

        public static void Register(int fromVersion, Func<JObject, JObject> step) => Steps[fromVersion] = step;
        public static void Unregister(int fromVersion) => Steps.Remove(fromVersion);

        public static JObject Migrate(JObject payload, int fromVersion, int toVersion, List<string> log)
        {
            int v = fromVersion;
            while (v < toVersion)
            {
                if (!Steps.TryGetValue(v, out Func<JObject, JObject> step))
                    throw new SaveCorruptException($"No migration from save schema {v} to {v + 1}.");
                payload = step(payload);
                log?.Add($"Migrated save schema {v} → {v + 1}.");
                v++;
            }
            return payload;
        }
    }

    /// <summary>
    /// Save file envelope: { format, schema, checksum, payload }. The checksum (SHA-256 of
    /// the canonical payload JSON) detects truncated or edited files.
    /// </summary>
    public static class SaveSerializer
    {
        public const string Format = "afterfall-save";
        public const int CurrentSchemaVersion = 1;
        public const string GameVersion = "0.1.0-prototype";

        private static readonly JsonSerializerSettings Settings = JsonDefaults.Create(false);

        public static string Serialize(WorldState world, string slot, double playtimeSeconds)
        {
            var file = new SaveFile
            {
                SchemaVersion = CurrentSchemaVersion,
                Header = new SaveHeader
                {
                    Slot = slot,
                    SavedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Day = world.Clock.Day,
                    TimeOfDay = world.Clock.FormatTime(),
                    PlaytimeSeconds = playtimeSeconds,
                    Difficulty = world.Settings.Preset.ToString(),
                    Seed = world.Seed,
                    GameVersion = GameVersion
                },
                World = WorldStateMapper.ToDto(world)
            };
            JsonSerializer serializer = JsonSerializer.Create(Settings);
            JObject payload = JObject.FromObject(file, serializer);
            var envelope = new JObject
            {
                ["format"] = Format,
                ["schema"] = CurrentSchemaVersion,
                ["checksum"] = Checksum(payload),
                ["payload"] = payload
            };
            return envelope.ToString(Formatting.Indented);
        }

        /// <summary>Reads only the header (for save slot lists). Returns null if unreadable.</summary>
        public static SaveHeader ReadHeader(string text)
        {
            try
            {
                JObject env = ParseObject(text);
                return env["payload"]?["Header"]?.ToObject<SaveHeader>(JsonSerializer.Create(Settings));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static WorldState Deserialize(string text, ContentDatabase content, List<string> warnings)
        {
            SaveFile file = Parse(text, warnings);
            if (file.World == null) throw new SaveCorruptException("Save has no world data.");
            WorldState world = WorldStateMapper.FromDto(file.World, content, warnings);
            List<string> problems = SaveValidator.Validate(world);
            if (problems.Count > 0) throw new SaveCorruptException("Save failed validation: " + string.Join("; ", problems));
            return world;
        }

        public static SaveFile Parse(string text, List<string> warnings)
        {
            JObject env;
            try
            {
                env = ParseObject(text);
            }
            catch (JsonException ex)
            {
                throw new SaveCorruptException("Save file is not valid JSON (truncated or damaged).", ex);
            }
            if ((string)env["format"] != Format) throw new SaveCorruptException("Not an AFTERFALL save file.");
            if (!(env["payload"] is JObject payload)) throw new SaveCorruptException("Save file has no payload.");
            int schema = (int?)env["schema"] ?? 0;
            if (schema > CurrentSchemaVersion) throw new SaveCorruptException($"Save was made by a newer version (schema {schema}).");
            string expected = (string)env["checksum"];
            if (string.IsNullOrEmpty(expected) || !string.Equals(expected, Checksum(payload), StringComparison.Ordinal))
                throw new SaveCorruptException("Save checksum mismatch — the file is damaged or was edited.");
            if (schema < CurrentSchemaVersion) payload = SaveMigrations.Migrate(payload, schema, CurrentSchemaVersion, warnings);
            try
            {
                return payload.ToObject<SaveFile>(JsonSerializer.Create(Settings));
            }
            catch (JsonException ex)
            {
                throw new SaveCorruptException("Save data could not be read: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Parses without Newtonsoft's implicit date conversion so that every value keeps its
        /// exact textual form and the checksum is computed over the same canonical JSON.
        /// </summary>
        public static JObject ParseObject(string text)
        {
            using (var reader = new JsonTextReader(new System.IO.StringReader(text)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                JObject obj = JObject.Load(reader);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment) throw new JsonReaderException("Unexpected content after the save object.");
                }
                return obj;
            }
        }

        public static string Checksum(JObject payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
