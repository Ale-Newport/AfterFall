namespace Afterfall.Presentation.Art
{
    /// <summary>Custom surfaces for resource nodes (see TextureSynth.Nature.cs).</summary>
    public static class NatureSurfaces
    {
        /// <summary>White birch bark with horizontal lenticels; tint near white.</summary>
        public static readonly Surface BirchBark = new Surface("birch_bark", 1.1f, TextureSynth.BirchBark, 0.18f, 0f, 0.9f, 0.8f);

        /// <summary>Cut end grain with growth rings around the tile corner: use in object space, axis at the origin.</summary>
        public static readonly Surface EndGrain = new Surface("end_grain", 1.4f, TextureSynth.EndGrain, 0.15f, 0f, 0.8f, 0.8f);

        /// <summary>Corroded sheet steel without pitting (wrecks, scrap); tint grey-brown.</summary>
        public static readonly Surface Corrosion = new Surface("corrosion", 1.6f, TextureSynth.Corrosion, 0.25f, 0.3f, 0.9f, 0.7f);

        /// <summary>Dark iron-bearing rock with rusty stains and glinting grains; tint dark grey-brown.</summary>
        public static readonly Surface IronOre = new Surface("iron_ore", 2.2f, TextureSynth.IronOre, 0.3f, 0.15f, 1.2f, 0.9f);
    }
}
