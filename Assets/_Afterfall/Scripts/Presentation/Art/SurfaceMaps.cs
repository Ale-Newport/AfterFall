using System;

namespace Afterfall.Presentation.Art
{
    /// <summary>
    /// A tileable pair of detail maps produced by <see cref="TextureSynth"/>. Plain C# (no UnityEngine)
    /// so the maps can also be rendered to PNG by tools/dotnet/ArtPreview.
    /// <para>Albedo is a linear-space multiplier normalised to a mean luminance of
    /// <see cref="AlbedoMean"/>; the surface shader scales it back up, so a material's tint is its
    /// average colour. Alpha is a smoothness multiplier. Detail holds the tangent-space normal
    /// (x, y in 0..1), the cavity term (1 = open, lower = crevice) and the height.</para>
    /// </summary>
    public sealed class SurfaceMaps
    {
        /// <summary>Mean albedo luminance after normalisation; the shader multiplies by its inverse.</summary>
        public const float AlbedoMean = 0.8f;

        public readonly int Size;
        /// <summary>RGBA, row-major, row 0 at v = 0 (bottom), linear values in 0..1.</summary>
        public readonly float[] Albedo;
        /// <summary>RGBA: normal x (0..1), normal y (0..1), cavity, height.</summary>
        public readonly float[] Detail;

        public SurfaceMaps(int size)
        {
            Size = size;
            Albedo = new float[size * size * 4];
            Detail = new float[size * size * 4];
        }
    }

    /// <summary>
    /// Describes a procedural surface: how to generate its maps and how big one repeat is in the
    /// world. Static instances below are the shared catalogue; feature code may declare its own
    /// (any unique <see cref="Key"/>) with a custom generator built from <see cref="TextureSynth"/>.
    /// </summary>
    public sealed class Surface
    {
        public readonly string Key;
        /// <summary>World size of one texture repeat, in metres.</summary>
        public readonly float TileMeters;
        public readonly float Smoothness;
        public readonly float Metallic;
        /// <summary>Normal map strength (0 = flat).</summary>
        public readonly float NormalStrength;
        /// <summary>How strongly crevices darken ambient light (0..1).</summary>
        public readonly float CavityStrength;
        public readonly Func<int, SurfaceMaps> Generate;
        /// <summary>Texture resolution (square, power of two).</summary>
        public readonly int Resolution;

        public Surface(string key, float tileMeters, Func<int, SurfaceMaps> generate, float smoothness = 0.2f, float metallic = 0f,
            float normalStrength = 1f, float cavityStrength = 0.8f, int resolution = 256)
        {
            Key = key;
            TileMeters = tileMeters;
            Generate = generate;
            Smoothness = smoothness;
            Metallic = metallic;
            NormalStrength = normalStrength;
            CavityStrength = cavityStrength;
            Resolution = resolution;
        }

        public override string ToString() => Key;

        // Shared catalogue. Boards, siding, bricks and shingles run horizontally on walls;
        // bark furrows and corrugations run vertically.
        public static readonly Surface Plain = new Surface("plain", 2f, TextureSynth.Plain, 0.25f, 0f, 0.4f, 0.4f, 128);
        public static readonly Surface Wood = new Surface("wood", 1.2f, TextureSynth.Wood, 0.22f, 0f, 0.7f);
        public static readonly Surface Planks = new Surface("planks", 2f, TextureSynth.Planks, 0.2f, 0f, 1f, 0.9f);
        public static readonly Surface PlanksVertical = new Surface("planks_vertical", 2f, n => TextureSynth.Transpose(TextureSynth.Planks(n)), 0.2f, 0f, 1f, 0.9f);
        public static readonly Surface Siding = new Surface("siding", 2.4f, TextureSynth.Siding, 0.28f, 0f, 1f, 0.8f);
        public static readonly Surface Bark = new Surface("bark", 1.6f, TextureSynth.Bark, 0.08f, 0f, 1.3f, 1f);
        public static readonly Surface Foliage = new Surface("foliage", 2.2f, TextureSynth.Foliage, 0.18f, 0f, 1.1f, 1f);
        public static readonly Surface Needles = new Surface("needles", 1.4f, TextureSynth.Needles, 0.15f, 0f, 1.1f, 1f);
        public static readonly Surface Rock = new Surface("rock", 3f, TextureSynth.Rock, 0.12f, 0f, 1.2f, 0.9f);
        public static readonly Surface Stone = new Surface("stone", 2f, TextureSynth.StoneBlocks, 0.12f, 0f, 1.1f, 1f);
        public static readonly Surface Concrete = new Surface("concrete", 3f, TextureSynth.Concrete, 0.2f, 0f, 0.6f, 0.6f);
        public static readonly Surface Plaster = new Surface("plaster", 2.5f, TextureSynth.Plaster, 0.15f, 0f, 0.7f, 0.6f);
        public static readonly Surface Brick = new Surface("brick", 2f, TextureSynth.Brick, 0.15f, 0f, 1f, 1f);
        public static readonly Surface Metal = new Surface("metal", 1f, TextureSynth.Metal, 0.55f, 0.85f, 0.4f, 0.5f);
        public static readonly Surface PaintedMetal = new Surface("painted_metal", 1.5f, TextureSynth.PaintedMetal, 0.42f, 0.25f, 0.5f, 0.6f);
        public static readonly Surface Rust = new Surface("rust", 1.5f, TextureSynth.Rust, 0.15f, 0.35f, 0.9f, 0.8f);
        public static readonly Surface Corrugated = new Surface("corrugated", 1.2f, TextureSynth.Corrugated, 0.35f, 0.5f, 1.4f, 0.7f);
        public static readonly Surface Shingles = new Surface("shingles", 2f, TextureSynth.Shingles, 0.12f, 0f, 1.2f, 1f);
        public static readonly Surface Fabric = new Surface("fabric", 0.4f, TextureSynth.Fabric, 0.1f, 0f, 0.8f, 0.6f, 128);
        public static readonly Surface Tarp = new Surface("tarp", 1.2f, TextureSynth.Tarp, 0.1f, 0f, 0.7f, 0.6f);
        public static readonly Surface Leather = new Surface("leather", 0.5f, TextureSynth.Leather, 0.3f, 0f, 0.6f, 0.6f, 128);
        public static readonly Surface Skin = new Surface("skin", 0.35f, TextureSynth.Skin, 0.32f, 0f, 0.25f, 0.3f, 128);
        public static readonly Surface Soil = new Surface("soil", 1.2f, TextureSynth.Soil, 0.08f, 0f, 1.1f, 1f);
        public static readonly Surface Asphalt = new Surface("asphalt", 3f, TextureSynth.Asphalt, 0.18f, 0f, 0.8f, 0.7f);
        public static readonly Surface Gravel = new Surface("gravel", 1.5f, TextureSynth.Gravel, 0.12f, 0f, 1.2f, 1f);
        public static readonly Surface Rubber = new Surface("rubber", 0.6f, TextureSynth.Rubber, 0.25f, 0f, 0.6f, 0.6f, 128);
        public static readonly Surface Glass = new Surface("glass", 2f, TextureSynth.Glass, 0.92f, 0f, 0.15f, 0.2f, 128);
        public static readonly Surface Grass = new Surface("grass", 1.6f, TextureSynth.GrassBlades, 0.12f, 0f, 1f, 0.9f);
    }
}
