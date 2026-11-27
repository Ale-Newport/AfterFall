namespace Afterfall.Domain.World
{
    public enum DifficultyPreset
    {
        Exploration,
        Survival,
        Hardcore
    }

    /// <summary>
    /// Survival keeps every rule. Creative lifts resource costs, build times and technology
    /// requirements, keeps the survivor unharmed and unlocks the creative panel and free flight.
    /// </summary>
    public enum GameMode
    {
        Survival,
        Creative
    }

    /// <summary>
    /// Independently configurable difficulty parameters. Presets are starting points;
    /// each value can be changed on its own.
    /// </summary>
    public sealed class GameSettings
    {
        public DifficultyPreset Preset { get; set; } = DifficultyPreset.Survival;

        /// <summary>Multiplier on the infected population cap and spawn rate.</summary>
        public float EnemyDensity { get; set; } = 1f;

        /// <summary>Multiplier on loot container quantities.</summary>
        public float LootAbundance { get; set; } = 1f;

        /// <summary>Multiplier on hunger/thirst drain.</summary>
        public float SurvivalDrain { get; set; } = 1f;

        /// <summary>Multiplier on damage dealt by the infected.</summary>
        public float EnemyDamage { get; set; } = 1f;

        /// <summary>Multiplier on the chance of thunderstorms and the depth of cold snaps.</summary>
        public float WeatherSeverity { get; set; } = 1f;

        /// <summary>Allow pausing the simulation while in the strategic camera.</summary>
        public bool TacticalPause { get; set; }

        /// <summary>Hardcore: death ends the run.</summary>
        public bool Permadeath { get; set; }

        /// <summary>Real minutes per in-game day.</summary>
        public float DayLengthMinutes { get; set; } = 48f;

        /// <summary>Survival or Creative. Saves written before modes existed load as Survival.</summary>
        public GameMode Mode { get; set; } = GameMode.Survival;

        /// <summary>
        /// Set for good once developer tools (spawning, teleporting, flight, switching to Creative)
        /// are enabled in a game, so such saves stay distinguishable from untouched survival runs.
        /// </summary>
        public bool DeveloperTools { get; set; }

        /// <summary>Creative mode keeps the survivor fed, warm and unharmed (on by default).</summary>
        public bool CreativeInvulnerable { get; set; } = true;

        /// <summary>
        /// Which world generator built the layout. 1 is the original 512 m valley; saves written
        /// before this field existed load as 1, so their ground never moves under them.
        /// </summary>
        public int WorldVersion { get; set; } = 1;

        // Methods rather than properties, so the save serializer does not write them.
        public bool IsCreative() => Mode == GameMode.Creative;

        /// <summary>Spawning, teleporting and flight are available (Creative, or developer tools enabled).</summary>
        public bool ToolsEnabled() => Mode == GameMode.Creative || DeveloperTools;

        /// <summary>Player-facing summary such as "Survival", "Creative" or "Survival · developer tools".</summary>
        public string ModeLabel() => Mode == GameMode.Creative ? "Creative" : DeveloperTools ? "Survival · developer tools" : "Survival";

        public static GameSettings FromPreset(DifficultyPreset preset)
        {
            switch (preset)
            {
                case DifficultyPreset.Exploration:
                    return new GameSettings
                    {
                        Preset = preset, EnemyDensity = 0.5f, LootAbundance = 1.5f, SurvivalDrain = 0.6f,
                        EnemyDamage = 0.6f, WeatherSeverity = 0.5f, TacticalPause = true, Permadeath = false
                    };
                case DifficultyPreset.Hardcore:
                    return new GameSettings
                    {
                        Preset = preset, EnemyDensity = 1.6f, LootAbundance = 0.6f, SurvivalDrain = 1.4f,
                        EnemyDamage = 1.5f, WeatherSeverity = 1.5f, TacticalPause = false, Permadeath = true
                    };
                default:
                    return new GameSettings
                    {
                        Preset = DifficultyPreset.Survival, EnemyDensity = 1f, LootAbundance = 1f, SurvivalDrain = 1f,
                        EnemyDamage = 1f, WeatherSeverity = 1f, TacticalPause = false, Permadeath = false
                    };
            }
        }
    }
}
