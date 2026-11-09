using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// Full-colour maps for one Unity terrain layer (or the lake's ripples). Unlike
    /// <see cref="SurfaceMaps"/> the albedo is absolute: URP's terrain shader uses the layer texture
    /// directly, so <see cref="Color"/> holds the final linear colour with crevice shading baked in.
    /// Plain C# so tools/dotnet can preview it.
    /// </summary>
    public sealed class TerrainMaps
    {
        public readonly int Size;
        /// <summary>RGBA, row-major, row 0 at v = 0: linear colour and smoothness (0..1).</summary>
        public readonly float[] Color;
        /// <summary>RGBA: tangent-space normal x, y, z (−1..1) and height (0..1).</summary>
        public readonly float[] Normal;

        public TerrainMaps(int size)
        {
            Size = size;
            Color = new float[size * size * 4];
            Normal = new float[size * size * 4];
        }

        /// <summary>Mean linear colour of the map (r, g, b).</summary>
        public void Mean(out float r, out float g, out float b)
        {
            double sr = 0, sg = 0, sb = 0;
            int n = Size * Size;
            for (int i = 0; i < n; i++)
            {
                sr += Color[i * 4];
                sg += Color[i * 4 + 1];
                sb += Color[i * 4 + 2];
            }
            r = (float)(sr / n);
            g = (float)(sg / n);
            b = (float)(sb / n);
        }
    }

    /// <summary>One ground layer: its recipe, the size of one repeat and the normal strength.</summary>
    public sealed class TerrainLayerArt
    {
        public readonly string Name;
        public readonly float TileMeters;
        public readonly float NormalScale;
        public readonly Func<int, TerrainMaps> Generate;

        public TerrainLayerArt(string name, float tileMeters, float normalScale, Func<int, TerrainMaps> generate)
        {
            Name = name;
            TileMeters = tileMeters;
            NormalScale = normalScale;
            Generate = generate;
        }
    }

    /// <summary>
    /// The terrain's ground layers in splat order. URP draws terrain four layers per pass, so the
    /// layers whose relief matters most (meadow, forest floor, dirt, rock) come first.
    /// </summary>
    public static class TerrainSurfaces
    {
        public const int Meadow = 0;
        public const int ForestFloor = 1;
        public const int Dirt = 2;
        public const int Rock = 3;
        public const int DryGrass = 4;
        public const int Asphalt = 5;
        public const int Gravel = 6;
        public const int Shore = 7;
        public const int Count = 8;

        /// <summary>Texture resolution of every layer.</summary>
        public const int Resolution = 512;

        public static readonly TerrainLayerArt[] Layers =
        {
            new TerrainLayerArt("Meadow", 6f, 0.65f, TextureSynth.TerrainMeadow),
            new TerrainLayerArt("ForestFloor", 5f, 1.1f, TextureSynth.TerrainForestFloor),
            new TerrainLayerArt("Dirt", 5f, 1f, TextureSynth.TerrainDirt),
            new TerrainLayerArt("Rock", 9f, 1.2f, TextureSynth.TerrainRock),
            new TerrainLayerArt("DryGrass", 7.3f, 0.75f, TextureSynth.TerrainDryGrass),
            new TerrainLayerArt("Asphalt", 6f, 0.9f, TextureSynth.TerrainAsphalt),
            new TerrainLayerArt("Gravel", 3f, 1.1f, TextureSynth.TerrainGravel),
            new TerrainLayerArt("Shore", 5f, 1f, TextureSynth.TerrainShore)
        };

        /// <summary>World size of one repeat of the lake ripple normal map.</summary>
        public const float RippleTileMeters = 6f;

        /// <summary>Tileable ripple normals for the lake (two scrolling copies are layered in the shader).</summary>
        public static TerrainMaps WaterRipples(int n) => TextureSynth.TerrainWaterRipples(n);
    }
}
