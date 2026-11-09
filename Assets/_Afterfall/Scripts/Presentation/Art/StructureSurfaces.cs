namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Surfaces of player-built structures, machines and world containers that the shared catalogue
    /// lacks: weathered timber in both grain directions and bark for logs lying on their side.
    /// Structures map textures in object space, so "vertical" means along the part's local Y.
    /// </summary>
    public static class StructureSurfaces
    {
        /// <summary>Weathered sawn timber, grain along u (horizontal boards and beams).</summary>
        public static readonly Surface Timber = new Surface("st_timber", 1.6f, TextureSynth.Timber, 0.2f, 0f, 1f, 0.85f);

        /// <summary>The same timber with the grain along v (posts, legs, studs).</summary>
        public static readonly Surface TimberVertical = new Surface("st_timber_vertical", 1.6f, n => TextureSynth.Transpose(TextureSynth.Timber(n)), 0.2f, 0f, 1f, 0.85f);

        /// <summary>Bark with the furrows along u (logs lying along X).</summary>
        public static readonly Surface BarkHorizontal = new Surface("st_bark_horizontal", 1.6f, n => TextureSynth.Transpose(TextureSynth.Bark(n)), 0.08f, 0f, 1.3f, 1f);
    }
}
