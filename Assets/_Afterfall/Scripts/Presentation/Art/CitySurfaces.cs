namespace Afterfall.Presentation.Art
{
    /// <summary>Surfaces of the city's buildings: facades whose window grid matches the real buildings (see TextureSynth.Facade), and interior floors.</summary>
    public static class CitySurfaces
    {
        public const float BayMeters = 3.6f;
        public static readonly Surface FacadeBrick = new Surface("city_facade_brick", BayMeters, TextureSynth.FacadeBrick, 0.2f, 0f, 0.8f, 0.6f, 512);
        public static readonly Surface FacadeRender = new Surface("city_facade_render", BayMeters, TextureSynth.FacadeRender, 0.2f, 0f, 0.6f, 0.5f, 512);
        public static readonly Surface FacadeConcrete = new Surface("city_facade_concrete", BayMeters, TextureSynth.FacadeConcrete, 0.25f, 0f, 0.6f, 0.6f, 512);
        public static readonly Surface FacadeStone = new Surface("city_facade_stone", BayMeters, TextureSynth.FacadeStone, 0.2f, 0f, 0.8f, 0.6f, 512);
        public static readonly Surface Carpet = new Surface("city_carpet", 1.5f, TextureSynth.Carpet, 0.05f, 0f, 0.3f, 0.4f);
        public static readonly Surface Lino = new Surface("city_lino", 2f, TextureSynth.Lino, 0.5f, 0f, 0.2f, 0.3f);

        /// <summary>The facade surface of a building's finish ("brick", "render", "concrete", "stone"); null for corrugated sheds.</summary>
        public static Surface FacadeOf(string facade)
        {
            switch (facade)
            {
                case "brick": return FacadeBrick;
                case "render": return FacadeRender;
                case "concrete": return FacadeConcrete;
                case "stone": return FacadeStone;
                default: return null;
            }
        }
    }
}
