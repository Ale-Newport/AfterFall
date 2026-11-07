using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Builds the static scene for a world layout: Unity Terrain (heights + splat painting),
    /// lake, map boundary and procedural point-of-interest buildings. Everything is derived
    /// from the deterministic domain layout, so a save only needs the seed to rebuild it.
    /// </summary>
    public static class WorldBuilder
    {
        public static Terrain BuildTerrain(WorldLayout layout, int seed, Transform parent)
        {
            HeightField hf = layout.Height;
            var data = new TerrainData
            {
                heightmapResolution = hf.Resolution,
                alphamapResolution = 512,
                baseMapResolution = 512
            };
            data.size = new Vector3(hf.Size, hf.MaxHeight, hf.Size);
            var heights = new float[hf.Resolution, hf.Resolution];
            for (int z = 0; z < hf.Resolution; z++)
                for (int x = 0; x < hf.Resolution; x++)
                    heights[z, x] = hf.Heights[z * hf.Resolution + x] / hf.MaxHeight;
            data.SetHeights(0, 0, heights);

            data.terrainLayers = new[]
            {
                Layer("Grass", new Color(0.36f, 0.43f, 0.24f), new Color(0.27f, 0.35f, 0.18f), 7f, 11),
                Layer("ForestFloor", new Color(0.29f, 0.27f, 0.18f), new Color(0.2f, 0.22f, 0.13f), 6f, 12),
                Layer("Dirt", new Color(0.42f, 0.35f, 0.26f), new Color(0.33f, 0.27f, 0.2f), 6f, 13),
                Layer("Rock", new Color(0.45f, 0.44f, 0.42f), new Color(0.32f, 0.31f, 0.3f), 9f, 14),
                Layer("Asphalt", new Color(0.24f, 0.24f, 0.25f), new Color(0.18f, 0.18f, 0.19f), 5f, 15),
                Layer("Sand", new Color(0.55f, 0.5f, 0.38f), new Color(0.45f, 0.41f, 0.31f), 6f, 16)
            };
            PaintSplat(layout, seed, data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.layer = Layers.Terrain;
            Terrain t = go.GetComponent<Terrain>();
            t.materialTemplate = MaterialLibrary.Terrain();
            t.heightmapPixelError = 4f;
            t.basemapDistance = 220f;
            t.drawInstanced = true;
            return t;
        }

        private static TerrainLayer Layer(string name, Color a, Color b, float tile, int seed)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Tileable noise: sample the domain noise on a torus-like wrap.
                    float n = 0.5f + 0.5f * Noise.Fbm(seed, x / 16f, y / 16f, 3);
                    float n2 = Noise.Hash01(seed + 7, x, y);
                    float edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y)) / 8f;
                    n = Mathf.Lerp(0.5f, n, Mathf.Clamp01(edge));
                    Color c = Color.Lerp(b, a, Mathf.Clamp01(n * 0.85f + n2 * 0.15f));
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return new TerrainLayer { name = name, diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = 0.05f, metallic = 0f };
        }

        private static void PaintSplat(WorldLayout layout, int seed, TerrainData data)
        {
            int res = data.alphamapResolution;
            int layers = data.terrainLayers.Length;
            var map = new float[res, res, layers];
            float cell = WorldLayout.WorldSize / (res - 1);
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float wx = x * cell, wz = z * cell;
                    float h = layout.HeightAt(wx, wz);
                    float slope = layout.Height.SlopeDegrees(wx, wz);
                    float forest = Noise.Fbm(seed + 77, wx * 0.011f, wz * 0.011f, 3) + (256f - wx) / 256f * 0.25f;
                    var w = new float[layers];
                    w[0] = 1f;
                    w[1] = Mathf.Clamp01((forest + 0.05f) * 2.5f) * 0.9f;
                    w[2] = Mathf.Clamp01(Noise.Fbm(31, wx * 0.05f, wz * 0.05f, 2) * 1.6f - 0.35f);
                    w[3] = Mathf.Clamp01((slope - 22f) / 10f) + Mathf.Clamp01((h - 48f) / 10f);
                    w[5] = Mathf.Clamp01((layout.WaterLevel + 1.6f - h) / 1.6f);

                    float roadD = DistanceToRoad(layout, wx, wz, out bool paved, out float width);
                    if (roadD < width * 0.5f + 1.5f)
                    {
                        float k = Mathf.Clamp01((width * 0.5f + 1.5f - roadD) / 1.5f);
                        for (int i = 0; i < layers; i++) w[i] *= 1f - k;
                        if (paved) w[4] += k;
                        else w[2] += k;
                    }
                    PoiInfo poi = layout.PoiAt(wx, wz);
                    if (poi != null && poi.Kind != "camp")
                    {
                        float d = Vec3.DistanceXZ(poi.Center, new Vec3(wx, 0, wz)) / poi.Radius;
                        float k = Mathf.Clamp01((1f - d) * 2.5f) * (poi.Kind == "quarry" ? 1f : 0.7f);
                        w[poi.Kind == "quarry" ? 3 : 2] += k;
                        w[1] *= 1f - k;
                    }

                    float sum = 0f;
                    for (int i = 0; i < layers; i++) sum += w[i];
                    for (int i = 0; i < layers; i++) map[z, x, i] = w[i] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        private static float DistanceToRoad(WorldLayout l, float x, float z, out bool paved, out float width)
        {
            float best = float.MaxValue;
            paved = false;
            width = 0f;
            foreach (RoadPath r in l.Roads)
            {
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    float d = WorldLayout.DistanceToSegmentXZ(x, z, r.Points[i], r.Points[i + 1], out _);
                    if (d >= best) continue;
                    best = d;
                    paved = r.Paved;
                    width = r.Width;
                }
            }
            return best;
        }

        public static GameObject BuildWater(WorldLayout layout, Transform parent)
        {
            float size = layout.LakeRadius * 3f;
            GameObject water = Shapes.Part(parent, "Lake", MeshFactory.Quad, MaterialLibrary.Water(),
                new Vector3(layout.LakeCenter.X, layout.WaterLevel, layout.LakeCenter.Z), new Vector3(size, size, 1f), Quaternion.Euler(90f, 0f, 0f), false);
            water.layer = 4; // built-in Water layer
            return water;
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
}
