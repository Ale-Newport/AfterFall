namespace Afterfall.Domain.World
{
    public enum DifficultyPreset
    {
        Exploration,
        Survival,
        Hardcore
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

        /// <summary>Reserved for weather events (storms, cold) — not simulated yet.</summary>
        public float WeatherSeverity { get; set; } = 1f;

        /// <summary>Allow pausing the simulation while in the strategic camera.</summary>
        public bool TacticalPause { get; set; }

        /// <summary>Hardcore: death ends the run.</summary>
        public bool Permadeath { get; set; }

        /// <summary>Real minutes per in-game day.</summary>
        public float DayLengthMinutes { get; set; } = 48f;

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
