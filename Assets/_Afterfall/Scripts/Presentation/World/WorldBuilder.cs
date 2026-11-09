using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Core;
using Afterfall.Domain.World;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Builds the static scene for a world layout: Unity Terrain (heights + splat painting),
    /// lake, map boundary and procedural point-of-interest buildings. Everything is derived
    /// from the deterministic domain layout, so a save only needs the seed to rebuild it.
    /// <para>The ground uses eight full-colour layers with normal maps (<see cref="TerrainSurfaces"/>).
    /// The splat is painted at 0.5 m from noise, slope, height, roads, points of interest and the
    /// objects standing on the ground (needle skirts under trees, trodden dirt around buildings), and
    /// a coarse colour map of the result is published for the ground cover shader.</para>
    /// </summary>
    public static class WorldBuilder
    {
        /// <summary>Splat map resolution (texels across the whole map).</summary>
        public const int SplatResolution = 1024;

        /// <summary>Global shader texture holding the average ground colour (sRGB) across the map.</summary>
        public static readonly int GroundTintId = Shader.PropertyToID("_AfterfallGroundTint");
        /// <summary>Global vector mapping world XZ to <see cref="GroundTintId"/> UVs (xy scale, zw offset).</summary>
        public static readonly int GroundTintRectId = Shader.PropertyToID("_AfterfallGroundTintRect");

        private const int TintResolution = 256;

        /// <summary>The splat weights of the terrain built last (read by <see cref="GroundCover"/>).</summary>
        public static GroundWeights Weights { get; private set; }

        private static TerrainLayer[] _layers;
        private static Task<LayerPixels[]> _pendingLayers;
        private static Color[] _layerMeans;
        private static Material _terrainMaterial, _waterMaterial;
        private static Texture2D _ripples;

        public static Terrain BuildTerrain(WorldLayout layout, int seed, Transform parent)
        {
            HeightField hf = layout.Height;
            var data = new TerrainData
            {
                heightmapResolution = hf.Resolution,
                alphamapResolution = SplatResolution,
                baseMapResolution = 1024
            };
            data.size = new Vector3(hf.Size, hf.MaxHeight, hf.Size);
            var heights = new float[hf.Resolution, hf.Resolution];
            for (int z = 0; z < hf.Resolution; z++)
                for (int x = 0; x < hf.Resolution; x++)
                    heights[z, x] = hf.Heights[z * hf.Resolution + x] / hf.MaxHeight;
            data.SetHeights(0, 0, heights);

            // The layer textures (pure C#) are generated while the splat is painted.
            bool fresh = _layers == null || _layers[0] == null || _layers[0].diffuseTexture == null;
            Task<LayerPixels[]> pixels = null;
            if (fresh)
            {
                pixels = _pendingLayers ?? Task.Run(GenerateLayerPixels);
                _pendingLayers = null;
            }
            GroundWeights weights = PaintSplat(layout, seed);
            if (pixels != null) UploadLayers(pixels.Result);
            data.terrainLayers = _layers;
            weights.CopyTo(data);
            PublishGroundTint(weights);
            Weights = weights;

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.layer = Layers.Terrain;
            Terrain t = go.GetComponent<Terrain>();
            t.materialTemplate = TerrainMaterial();
            t.heightmapPixelError = 3f;
            t.basemapDistance = 240f;
            // Instanced terrain needs a hidden normal-map shader that player builds strip, leaving the
            // ground unlit; the regular path lights it from mesh normals.
            t.drawInstanced = false;
            return t;
        }

        // ------------------------------------------------------------------ layers

        private sealed class LayerPixels
        {
            public byte[] Color;
            public byte[] Normal;
            public Color Mean;
        }

        /// <summary>Starts generating the ground textures in the background as soon as the game launches.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void PrewarmLayers()
        {
            if (_layers == null && _pendingLayers == null) _pendingLayers = Task.Run(GenerateLayerPixels);
        }

        private static LayerPixels[] GenerateLayerPixels()
        {
            TerrainLayerArt[] art = TerrainSurfaces.Layers;
            var result = new LayerPixels[art.Length];
            Parallel.For(0, art.Length, i =>
            {
                TerrainMaps m = art[i].Generate(TerrainSurfaces.Resolution);
                int count = m.Size * m.Size;
                var color = new byte[count * 4];
                var normal = new byte[count * 4];
                for (int p = 0; p < count; p++)
                {
                    int o = p * 4;
                    color[o] = ToByte(Mathf.LinearToGammaSpace(m.Color[o]));
                    color[o + 1] = ToByte(Mathf.LinearToGammaSpace(m.Color[o + 1]));
                    color[o + 2] = ToByte(Mathf.LinearToGammaSpace(m.Color[o + 2]));
                    color[o + 3] = ToByte(m.Color[o + 3]);
                    // RGB holds the normal and A is 1, which the AG and RGB unpack paths both read correctly.
                    normal[o] = ToByte(m.Normal[o] * 0.5f + 0.5f);
                    normal[o + 1] = ToByte(m.Normal[o + 1] * 0.5f + 0.5f);
                    normal[o + 2] = ToByte(m.Normal[o + 2] * 0.5f + 0.5f);
                    normal[o + 3] = 255;
                }
                m.Mean(out float r, out float g, out float b);
                result[i] = new LayerPixels { Color = color, Normal = normal, Mean = new Color(r, g, b, 1f) };
            });
            return result;
        }

        private static void UploadLayers(LayerPixels[] pixels)
        {
            TerrainLayerArt[] art = TerrainSurfaces.Layers;
            int size = TerrainSurfaces.Resolution;
            _layers = new TerrainLayer[art.Length];
            _layerMeans = new Color[art.Length];
            for (int i = 0; i < art.Length; i++)
            {
                Texture2D diffuse = Upload(art[i].Name + " diffuse", size, pixels[i].Color, false);
                Texture2D normal = Upload(art[i].Name + " normal", size, pixels[i].Normal, true);
                _layerMeans[i] = pixels[i].Mean;
                // Offset every layer differently so their repeats never line up.
                var offset = new Vector2(TextureSynth.Hash01(i, 0, 811) * art[i].TileMeters, TextureSynth.Hash01(i, 1, 811) * art[i].TileMeters);
                _layers[i] = new TerrainLayer
                {
                    name = art[i].Name,
                    diffuseTexture = diffuse,
                    normalMapTexture = normal,
                    normalScale = art[i].NormalScale,
                    tileSize = new Vector2(art[i].TileMeters, art[i].TileMeters),
                    tileOffset = offset,
                    smoothness = 1f, // the diffuse alpha carries smoothness
                    metallic = 0f
                };
            }
        }

        private static Texture2D Upload(string name, int size, byte[] rgba, bool linear)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true);
            return tex;
        }

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        private static Material TerrainMaterial()
        {
            if (_terrainMaterial != null) return _terrainMaterial;
            // A copy, so enabling the normal-map keyword never touches the asset (the asset enables it
            // too, which is what keeps that shader variant in player builds).
            _terrainMaterial = new Material(MaterialLibrary.Terrain()) { name = "Afterfall Terrain" };
            _terrainMaterial.EnableKeyword("_NORMALMAP");
            return _terrainMaterial;
        }

        // ------------------------------------------------------------------ splat painting

        private struct Segment
        {
            public float Ax, Az, Bx, Bz, HalfWidth;
            public bool Paved;
        }

        /// <summary>Soft masks stamped around objects on the ground, at splat resolution.</summary>
        private sealed class Stamps
        {
            public readonly int Res;
            public readonly float Cell;
            public readonly float[] Forest, Dirt, Gravel, Rock, Asphalt;

            public Stamps(int res)
            {
                Res = res;
                Cell = WorldLayout.WorldSize / (res - 1);
                Forest = new float[res * res];
                Dirt = new float[res * res];
                Gravel = new float[res * res];
                Rock = new float[res * res];
                Asphalt = new float[res * res];
            }

            /// <summary>Max-blends a soft disc: full inside <paramref name="inner"/>, fading out to <paramref name="outer"/>.</summary>
            public void Disc(float[] mask, float cx, float cz, float inner, float outer, float strength, int seed)
            {
                Rect(mask, cx, cz, 0f, 0f, 0f, inner, outer, strength, seed);
            }

            /// <summary>Like <see cref="Rect"/> with a crisper, less ragged edge (paving).</summary>
            public void Paved(float[] mask, float cx, float cz, float hx, float hz, float yaw, float inner, float outer, float strength, int seed)
            {
                Rect(mask, cx, cz, hx, hz, yaw, inner, outer, strength, seed, 0.06f);
            }

            /// <summary>Max-blends a soft rounded rectangle (half extents, yaw in degrees) widened by <paramref name="inner"/> and fading to <paramref name="outer"/>.</summary>
            public void Rect(float[] mask, float cx, float cz, float hx, float hz, float yaw, float inner, float outer, float strength, int seed, float rough = 0.25f)
            {
                float reach = Mathf.Sqrt(hx * hx + hz * hz) + outer + 1f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((cx - reach) / Cell)), x1 = Mathf.Min(Res - 1, Mathf.CeilToInt((cx + reach) / Cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((cz - reach) / Cell)), z1 = Mathf.Min(Res - 1, Mathf.CeilToInt((cz + reach) / Cell));
                // World offset to the prop's local frame (inverse of Unity's yaw rotation).
                float rad = yaw * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x * Cell - cx, dz = z * Cell - cz;
                        float lx = Mathf.Abs(dx * cs - dz * sn) - hx, lz = Mathf.Abs(dx * sn + dz * cs) - hz;
                        float d = Mathf.Sqrt(Mathf.Max(lx, 0f) * Mathf.Max(lx, 0f) + Mathf.Max(lz, 0f) * Mathf.Max(lz, 0f));
                        d += Noise.Value(seed, x * Cell * 0.9f, z * Cell * 0.9f) * outer * rough;
                        float w = Mathf.Clamp01((outer - d) / Mathf.Max(0.01f, outer - inner)) * strength;
                        int i = z * Res + x;
                        if (w > mask[i]) mask[i] = w;
                    }
                }
            }
        }

        private static Stamps BuildStamps(WorldLayout layout, int seed)
        {
            var s = new Stamps(SplatResolution);
            foreach (NodeSeed n in layout.Nodes)
            {
                float x = n.Position.X, z = n.Position.Z, k = n.Scale;
                switch (n.DefId)
                {
                    case "tree_pine": s.Disc(s.Forest, x, z, 1.4f * k, 3.6f * k, 0.95f, seed + 11); break;
                    case "tree_birch": s.Disc(s.Forest, x, z, 0.9f * k, 2.8f * k, 0.8f, seed + 11); break;
                    case "bush_berry": s.Disc(s.Forest, x, z, 0.5f * k, 1.6f * k, 0.7f, seed + 11); break;
                    case "branch_pile": s.Disc(s.Forest, x, z, 0.6f, 1.8f, 0.8f, seed + 11); break;
                    case "rock_small":
                    case "ore_iron":
                        s.Disc(s.Gravel, x, z, 0.3f * k, 1f * k, 0.45f, seed + 12);
                        s.Disc(s.Dirt, x, z, 0.6f * k, 1.8f * k, 0.45f, seed + 13);
                        break;
                    case "wreck_car": s.Rect(s.Dirt, x, z, 0.9f, 2.1f, n.Yaw, 0.6f, 2.4f, 0.8f, seed + 13); break;
                    case "scrap_pile": s.Disc(s.Dirt, x, z, 1.2f, 3f, 0.85f, seed + 13); break;
                    case "transformer_wreck": s.Disc(s.Gravel, x, z, 1f, 2.6f, 0.8f, seed + 12); break;
                }
            }
            foreach (PropPlacement p in layout.Props)
            {
                float x = p.Position.X, z = p.Position.Z, hx = p.Size.X * 0.5f, hz = p.Size.Z * 0.5f;
                switch (p.Kind)
                {
                    case "canopy":
                        // The gas station forecourt reaches out to the road.
                        s.Paved(s.Asphalt, x, z, hx, hz, p.Yaw, 5f, 6.2f, 1f, seed + 15);
                        s.Rect(s.Gravel, x, z, hx, hz, p.Yaw, 6.5f, 10f, 0.85f, seed + 12);
                        break;
                    case "shop":
                        // Wide enough to join the canopy's forecourt into one lot.
                        s.Paved(s.Asphalt, x, z, hx, hz, p.Yaw, 4f, 5.2f, 0.95f, seed + 15);
                        s.Rect(s.Gravel, x, z, hx, hz, p.Yaw, 4f, 7.5f, 0.8f, seed + 12);
                        break;
                    case "garage":
                    case "warehouse":
                        // A worn concrete apron and a gravel yard around the workshop buildings.
                        s.Paved(s.Asphalt, x, z, hx, hz, p.Yaw, 4f, 5.2f, 0.85f, seed + 15);
                        s.Rect(s.Gravel, x, z, hx, hz, p.Yaw, 6f, 10f, 0.85f, seed + 12);
                        s.Rect(s.Dirt, x, z, hx, hz, p.Yaw, 8f, 13f, 0.6f, seed + 13);
                        break;
                    case "house":
                    case "clinic":
                    case "barn":
                    case "shack":
                        s.Rect(s.Dirt, x, z, hx, hz, p.Yaw, 0.8f, 2.8f, 0.85f, seed + 13);
                        s.Rect(s.Gravel, x, z, hx, hz, p.Yaw, 0.2f, 1.1f, 0.6f, seed + 12);
                        break;
                    case "tent": s.Rect(s.Dirt, x, z, hx, hz, p.Yaw, 0.4f, 1.6f, 0.8f, seed + 13); break;
                    case "firepit": s.Disc(s.Dirt, x, z, 1.6f, 3.4f, 1f, seed + 13); break;
                    case "log_seat": s.Disc(s.Dirt, x, z, 0.8f, 1.8f, 0.7f, seed + 13); break;
                    case "silo":
                    case "transformer":
                        s.Rect(s.Gravel, x, z, hx, hz, p.Yaw, 0.6f, 1.8f, 0.9f, seed + 12);
                        break;
                    case "pylon": s.Disc(s.Gravel, x, z, 1.2f, 2.2f, 0.8f, seed + 12); break;
                    case "cliff":
                        s.Rect(s.Rock, x, z, hx, hz, p.Yaw, 0.5f, 2f, 1f, seed + 14);
                        s.Rect(s.Gravel, x, z, hx, hz, p.Yaw, 1.5f, 5f, 0.85f, seed + 12);
                        break;
                    case "power_pole_old":
                    case "sign":
                        s.Disc(s.Dirt, x, z, 0.3f, 1f, 0.6f, seed + 13);
                        break;
                }
            }
            return s;
        }

        /// <summary>Replaces <paramref name="amount"/> of every layer with <paramref name="layer"/> (keeps the sum at 1).</summary>
        private static void Over(float[] w, int layer, float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (amount <= 0f) return;
            for (int i = 0; i < w.Length; i++) w[i] *= 1f - amount;
            w[layer] += amount;
        }

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static GroundWeights PaintSplat(WorldLayout layout, int seed)
        {
            int res = SplatResolution;
            int layers = TerrainSurfaces.Count;
            var map = new GroundWeights(res, layers);
            float cell = WorldLayout.WorldSize / (res - 1);
            Stamps stamps = BuildStamps(layout, seed);
            var segments = new List<Segment>();
            foreach (RoadPath r in layout.Roads)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                    segments.Add(new Segment { Ax = r.Points[i].X, Az = r.Points[i].Z, Bx = r.Points[i + 1].X, Bz = r.Points[i + 1].Z, HalfWidth = r.Width * 0.5f, Paved = r.Paved });
            Segment[] segs = segments.ToArray();

            Parallel.For(0, res, z =>
            {
                var w = new float[layers];
                for (int x = 0; x < res; x++)
                {
                    float wx = x * cell, wz = z * cell;
                    Array.Clear(w, 0, layers);
                    PaintTexel(layout, seed, segs, stamps, x, z, wx, wz, w);
                    map.Set(x, z, w);
                }
            });
            return map;
        }

        private static void PaintTexel(WorldLayout layout, int seed, Segment[] segs, Stamps stamps, int sx, int sz, float x, float z, float[] w)
        {
            float h = layout.HeightAt(x, z);
            float slope = layout.Height.SlopeDegrees(x, z);
            float n1 = Noise.Fbm(seed + 501, x * 0.016f, z * 0.016f, 4);
            float n2 = Noise.Fbm(seed + 502, x * 0.06f, z * 0.06f, 3);
            float n3 = Noise.Fbm(seed + 503, x * 0.28f, z * 0.28f, 2);
            float n4 = Noise.Value(seed + 504, x * 1.3f, z * 1.3f);
            float water = layout.WaterLevel;
            int si = sz * stamps.Res + sx;

            // Natural ground: meadow, drying out in broad patches, higher up and on the slopes.
            w[TerrainSurfaces.Meadow] = 1f;
            float lakeLush = Smooth(water + 6f, water + 2f, h);
            float dry = Smooth(0.12f, 0.45f, n1 * 0.8f + n2 * 0.45f + n3 * 0.12f + Smooth(32f, 60f, h) * 0.35f + Smooth(8f, 22f, slope) * 0.2f - lakeLush * 0.4f);
            Over(w, TerrainSurfaces.DryGrass, dry * 0.8f);

            // Forest floor where the generator plants the forest, and needle skirts under every tree.
            float forestNoise = Noise.Fbm(seed + 77, x * 0.011f, z * 0.011f, 3) + (256f - x) / 256f * 0.25f;
            float forest = Smooth(-0.1f, 0.18f, forestNoise + n3 * 0.08f) * 0.6f;
            forest = Mathf.Max(forest, stamps.Forest[si]);
            Over(w, TerrainSurfaces.ForestFloor, forest);

            // Bare, trampled patches in the open.
            float bare = Smooth(0.4f, 0.62f, n2 + n3 * 0.35f) * 0.55f * (1f - forest);
            Over(w, TerrainSurfaces.Dirt, bare);

            // Rock on steep ground and outcrops along the northern ridge.
            float rock = Smooth(24f, 34f, slope + n3 * 6f + n4 * 2f);
            rock = Mathf.Max(rock, Smooth(52f, 62f, h + n2 * 8f) * Smooth(0.05f, 0.35f, n2 + n3 * 0.3f));
            Over(w, TerrainSurfaces.Gravel, Smooth(16f, 26f, slope + n3 * 6f) * 0.35f * (1f - rock));
            Over(w, TerrainSurfaces.Rock, Mathf.Max(rock, stamps.Rock[si]));

            // Lake shore: silt below and around the waterline, a pebble band right at it.
            float shore = Smooth(water + 1.4f + n3 * 0.6f, water + 0.35f, h);
            Over(w, TerrainSurfaces.Shore, shore);
            float band = Smooth(0.45f, 0.1f, Mathf.Abs(h - (water + 0.05f)) + n4 * 0.12f);
            Over(w, TerrainSurfaces.Gravel, band * 0.55f);

            PaintPoi(layout, x, z, n2, n3, n4, w);

            // Objects standing on the ground.
            Over(w, TerrainSurfaces.Gravel, stamps.Gravel[si]);
            Over(w, TerrainSurfaces.Dirt, stamps.Dirt[si] * (0.75f + n4 * 0.25f));
            Over(w, TerrainSurfaces.Asphalt, stamps.Asphalt[si] * (0.9f + n4 * 0.1f));

            PaintRoad(segs, x, z, n3, n4, w);
        }

        private static void PaintRoad(Segment[] segs, float x, float z, float n3, float n4, float[] w)
        {
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < segs.Length; i++)
            {
                Segment s = segs[i];
                float abx = s.Bx - s.Ax, abz = s.Bz - s.Az;
                float len2 = abx * abx + abz * abz;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((x - s.Ax) * abx + (z - s.Az) * abz) / len2);
                float cx = s.Ax + abx * t - x, cz = s.Az + abz * t - z;
                float d = cx * cx + cz * cz;
                if (d < best)
                {
                    best = d;
                    bi = i;
                }
            }
            if (bi < 0) return;
            float dist = Mathf.Sqrt(best);
            Segment r = segs[bi];
            float half = r.HalfWidth;
            if (dist > half + 6f) return;

            // Unkempt verge of dry grass along every road.
            Over(w, TerrainSurfaces.DryGrass, Smooth(half + 5.5f + n3, half + 2f, dist) * 0.55f);
            if (r.Paved)
            {
                // Gravel shoulder, then old asphalt crumbling at the edge.
                Over(w, TerrainSurfaces.Gravel, Smooth(half + 1.9f + n3 * 0.6f, half + 0.7f, dist) * 0.85f);
                Over(w, TerrainSurfaces.Dirt, Smooth(half + 1.2f, half + 0.3f, dist) * Smooth(0.1f, 0.6f, n4) * 0.5f);
                Over(w, TerrainSurfaces.Asphalt, Smooth(half + 0.2f + n4 * 0.35f, half - 0.3f, dist));
            }
            else
            {
                // A dirt track: two wheel ruts with a grassy strip down the middle.
                float track = Smooth(half + 0.8f + n3 * 0.5f, half - 0.3f, dist);
                float rut = Smooth(0.6f, 0.2f, Mathf.Abs(dist - 1.05f));
                float centre = Smooth(0.5f, 0.15f, dist + n4 * 0.1f);
                Over(w, TerrainSurfaces.Dirt, track * (0.6f + rut * 0.4f) * (1f - centre * 0.7f));
                Over(w, TerrainSurfaces.Gravel, track * rut * Smooth(0f, 0.5f, n4) * 0.35f);
            }
        }

        private static void PaintPoi(WorldLayout layout, float x, float z, float n2, float n3, float n4, float[] w)
        {
            PoiInfo poi = layout.PoiAt(x, z);
            if (poi == null) return;
            float dx = x - poi.Center.X, dz = z - poi.Center.Z;
            float d = Mathf.Sqrt(dx * dx + dz * dz) / poi.Radius;
            float rag = n3 * 0.08f + n4 * 0.03f;
            switch (poi.Kind)
            {
                case "camp":
                    // The clearing: open meadow, trodden around the fire (stamped from the props).
                    Over(w, TerrainSurfaces.Meadow, Smooth(1.1f, 0.6f, d) * 0.85f);
                    break;
                case "residential":
                    // Overgrown lawns and worn ground between the houses.
                    Over(w, TerrainSurfaces.DryGrass, Smooth(1f, 0.7f, d) * 0.35f);
                    Over(w, TerrainSurfaces.Dirt, Smooth(1f, 0.6f, d) * Smooth(0.2f, 0.5f, n2 + n3 * 0.3f) * 0.5f);
                    break;
                case "fuel":
                case "workshop":
                    // Paved aprons are stamped around the buildings; the rest is neglected verge.
                    Over(w, TerrainSurfaces.DryGrass, Smooth(1f, 0.6f, d) * 0.35f);
                    Over(w, TerrainSurfaces.Dirt, Smooth(1f, 0.5f, d) * Smooth(0.15f, 0.45f, n2 + n3 * 0.3f) * 0.45f);
                    break;
                case "substation":
                    // A crisp gravel bed inside the fence.
                    float inside = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    Over(w, TerrainSurfaces.Gravel, Smooth(11.8f, 11.2f, inside + n4 * 0.15f));
                    Over(w, TerrainSurfaces.DryGrass, Smooth(1f, 0.8f, d) * Smooth(11.2f, 12.5f, inside) * 0.5f);
                    break;
                case "quarry":
                    Over(w, TerrainSurfaces.Gravel, Smooth(0.95f + rag, 0.7f, d) * 0.8f);
                    Over(w, TerrainSurfaces.Dirt, Smooth(0.9f + rag, 0.6f, d) * Smooth(-0.2f, 0.25f, n2) * 0.5f);
                    Over(w, TerrainSurfaces.Rock, Smooth(0.85f + rag, 0.6f, d) * Smooth(0.1f, 0.4f, n2 + n3 * 0.4f) * 0.8f);
                    break;
                case "farm":
                    // Ploughed rows in the fibre field and a trodden yard.
                    if (x > 234f && x < 264f && z > 90f && z < 114f)
                    {
                        float edge = Mathf.Min(Mathf.Min(x - 234f, 264f - x), Mathf.Min(z - 90f, 114f - z));
                        float furrow = 0.5f + 0.5f * Mathf.Sin(z * Mathf.PI * 2f / 1.5f + n4 * 0.6f);
                        Over(w, TerrainSurfaces.Dirt, Smooth(0f, 1.5f, edge + n3) * (0.45f + Smooth(0.35f, 0.75f, furrow) * 0.5f));
                    }
                    Over(w, TerrainSurfaces.Dirt, Smooth(0.5f + rag, 0.3f, d) * 0.55f);
                    Over(w, TerrainSurfaces.DryGrass, Smooth(1f, 0.6f, d) * 0.25f);
                    break;
            }
        }

        /// <summary>
        /// Publishes the average ground colour per 2 m cell (from the splat weights and the layers'
        /// mean colours) so ground cover can match the ground it grows from.
        /// </summary>
        private static void PublishGroundTint(GroundWeights splat)
        {
            int res = splat.Resolution, layers = splat.Layers;
            int step = res / TintResolution;
            var px = new Color32[TintResolution * TintResolution];
            Color[] means = _layerMeans;
            Parallel.For(0, TintResolution, ty =>
            {
                for (int tx = 0; tx < TintResolution; tx++)
                {
                    Color sum = Color.black;
                    int n = 0;
                    for (int y = ty * step; y < Mathf.Min(res, (ty + 1) * step); y++)
                    {
                        for (int x = tx * step; x < Mathf.Min(res, (tx + 1) * step); x++)
                        {
                            int o = (y * res + x) * layers;
                            for (int l = 0; l < layers; l++) sum += means[l] * (splat.Data[o + l] * (1f / 255f));
                            n++;
                        }
                    }
                    sum /= Mathf.Max(1, n);
                    px[ty * TintResolution + tx] = new Color(Mathf.LinearToGammaSpace(sum.r), Mathf.LinearToGammaSpace(sum.g), Mathf.LinearToGammaSpace(sum.b), 1f);
                }
            });
            var tex = new Texture2D(TintResolution, TintResolution, TextureFormat.RGBA32, false, false)
            {
                name = "Ground tint",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Shader.SetGlobalTexture(GroundTintId, tex);
            float k = (TintResolution - 1f) / TintResolution / WorldLayout.WorldSize;
            Shader.SetGlobalVector(GroundTintRectId, new Vector4(k, k, 0.5f / TintResolution, 0.5f / TintResolution));
        }

        // ------------------------------------------------------------------ water

        public static GameObject BuildWater(WorldLayout layout, Transform parent)
        {
            float size = layout.LakeRadius * 3f;
            GameObject water = Shapes.Part(parent, "Lake", MeshFactory.Quad, WaterMaterial(),
                new Vector3(layout.LakeCenter.X, layout.WaterLevel, layout.LakeCenter.Z), new Vector3(size, size, 1f), Quaternion.Euler(90f, 0f, 0f), false);
            water.layer = 4; // built-in Water layer
            return water;
        }

        /// <summary>The lake material (Afterfall/Water) with its procedural ripple normal map.</summary>
        private static Material WaterMaterial()
        {
            if (_waterMaterial != null) return _waterMaterial;
            _waterMaterial = new Material(MaterialLibrary.Water()) { name = "Afterfall Lake" };
            if (_ripples == null)
            {
                TerrainMaps m = TerrainSurfaces.WaterRipples(256);
                var px = new byte[m.Size * m.Size * 4];
                for (int p = 0; p < m.Size * m.Size; p++)
                {
                    int o = p * 4;
                    px[o] = ToByte(m.Normal[o] * 0.5f + 0.5f);
                    px[o + 1] = ToByte(m.Normal[o + 1] * 0.5f + 0.5f);
                    px[o + 2] = ToByte(m.Normal[o + 2] * 0.5f + 0.5f);
                    px[o + 3] = ToByte(m.Normal[o + 3]);
                }
                _ripples = Upload("Lake ripples", m.Size, px, true);
            }
            if (_waterMaterial.HasProperty("_RippleMap"))
            {
                _waterMaterial.SetTexture("_RippleMap", _ripples);
                _waterMaterial.SetFloat("_RippleTile", 1f / TerrainSurfaces.RippleTileMeters);
            }
            return _waterMaterial;
        }

        /// <summary>Invisible walls at the map edge so the player cannot leave the playable region.</summary>
        public static void BuildBoundary(Transform parent)
        {
            float s = WorldLayout.WorldSize;
            var root = new GameObject("Boundary").transform;
            root.SetParent(parent, false);
            void Wall(Vector3 c, Vector3 size)
            {
                var go = new GameObject("Edge");
                go.transform.SetParent(root, false);
                go.transform.position = c;
                go.AddComponent<BoxCollider>().size = size;
                go.layer = Layers.Prop;
            }
            Wall(new Vector3(s * 0.5f, 50, -1), new Vector3(s + 4, 200, 2));
            Wall(new Vector3(s * 0.5f, 50, s + 1), new Vector3(s + 4, 200, 2));
            Wall(new Vector3(-1, 50, s * 0.5f), new Vector3(2, 200, s + 4));
            Wall(new Vector3(s + 1, 50, s * 0.5f), new Vector3(2, 200, s + 4));
        }

        public static Transform BuildProps(WorldLayout layout, Transform parent)
        {
            var root = new GameObject("Props").transform;
            root.SetParent(parent, false);
            foreach (PropPlacement p in layout.Props)
            {
                GameObject go = PropBuilder.Build(p);
                if (go == null) continue;
                go.transform.SetParent(root, true);
                Shapes.SetLayerRecursive(go, Layers.Prop);
            }
            return root;
        }
    }

    /// <summary>
    /// The terrain's splat weights as bytes (layer-interleaved, row 0 at z = 0), kept on the CPU so
    /// ground cover can read them without allocating. Each texel's weights sum to about 255.
    /// </summary>
    public sealed class GroundWeights
    {
        public readonly int Resolution;
        public readonly int Layers;
        public readonly byte[] Data;
        /// <summary>World metres between texel centres.</summary>
        public readonly float Texel;

        public GroundWeights(int resolution, int layers)
        {
            Resolution = resolution;
            Layers = layers;
            Data = new byte[resolution * resolution * layers];
            Texel = WorldLayout.WorldSize / (resolution - 1);
        }

        /// <summary>Stores normalised weights for one texel.</summary>
        public void Set(int x, int z, float[] w)
        {
            float sum = 0f;
            for (int l = 0; l < Layers; l++) sum += w[l];
            float k = sum > 1e-5f ? 255f / sum : 0f;
            int o = (z * Resolution + x) * Layers;
            for (int l = 0; l < Layers; l++) Data[o + l] = (byte)Mathf.Clamp(Mathf.RoundToInt(w[l] * k), 0, 255);
        }

        /// <summary>Bilinear weights (0..1) at a world position.</summary>
        public void Sample(float x, float z, float[] w)
        {
            float fx = Mathf.Clamp(x / Texel, 0f, Resolution - 1.001f), fz = Mathf.Clamp(z / Texel, 0f, Resolution - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            int o00 = (iz * Resolution + ix) * Layers, o10 = o00 + Layers, o01 = o00 + Resolution * Layers, o11 = o01 + Layers;
            float w00 = (1f - tx) * (1f - tz) / 255f, w10 = tx * (1f - tz) / 255f, w01 = (1f - tx) * tz / 255f, w11 = tx * tz / 255f;
            for (int l = 0; l < Layers && l < w.Length; l++)
                w[l] = Data[o00 + l] * w00 + Data[o10 + l] * w10 + Data[o01 + l] * w01 + Data[o11 + l] * w11;
        }

        /// <summary>Uploads the weights to the terrain in strips, so no full-size float map is ever allocated.</summary>
        public void CopyTo(TerrainData data)
        {
            const int strip = 128;
            var buffer = new float[strip, Resolution, Layers];
            for (int z0 = 0; z0 < Resolution; z0 += strip)
            {
                int rows = Mathf.Min(strip, Resolution - z0);
                float[,,] map = rows == strip ? buffer : new float[rows, Resolution, Layers];
                for (int z = 0; z < rows; z++)
                    for (int x = 0; x < Resolution; x++)
                    {
                        int o = ((z0 + z) * Resolution + x) * Layers;
                        for (int l = 0; l < Layers; l++) map[z, x, l] = Data[o + l] * (1f / 255f);
                    }
                data.SetAlphamaps(0, z0, map);
            }
        }
    }
}
