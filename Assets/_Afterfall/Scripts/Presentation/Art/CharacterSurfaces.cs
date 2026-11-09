namespace Afterfall.Presentation.Art
{
    /// <summary>Procedural surfaces used only by characters (hair, knitted wool). Tinted white: the colour comes from vertex colours.</summary>
    public static class CharacterSurfaces
    {
        public static readonly Surface Hair = new Surface("hair", 0.22f, TextureSynth.HairStrands, 0.38f, 0f, 1f, 0.8f, 128);
        public static readonly Surface Knit = new Surface("knit", 0.16f, TextureSynth.Knit, 0.1f, 0f, 1f, 0.8f, 128);
    }
}
