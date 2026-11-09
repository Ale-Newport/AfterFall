namespace Afterfall.Presentation.Art
{
    /// <summary>Surfaces used only by the point-of-interest buildings and world props.</summary>
    public static class PropSurfaces
    {
        public static readonly Surface RollUpDoor = new Surface("prop_rollup", 1.2f, TextureSynth.RollUpSlats, 0.4f, 0.3f, 1f, 0.7f);
        public static readonly Surface FloorTiles = new Surface("prop_floor_tiles", 2.4f, TextureSynth.FloorTiles, 0.45f, 0f, 0.6f, 0.7f);
        public static readonly Surface CeilingTiles = new Surface("prop_ceiling_tiles", 2.4f, TextureSynth.CeilingTiles, 0.1f, 0f, 0.5f, 0.6f);
        public static readonly Surface QuarryRock = new Surface("prop_quarry_rock", 4f, TextureSynth.QuarryRock, 0.12f, 0f, 1.25f, 0.95f);
        /// <summary>Corrugated sheet with horizontal ribs (wall cladding laid sideways).</summary>
        public static readonly Surface CorrugatedHorizontal = new Surface("prop_corrugated_h", 1.2f, n => TextureSynth.Transpose(TextureSynth.Corrugated(n)), 0.35f, 0.5f, 1.4f, 0.7f);
    }
}
