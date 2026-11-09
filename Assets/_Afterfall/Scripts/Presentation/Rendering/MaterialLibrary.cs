using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Shared runtime materials derived from base material assets in Resources/Materials
    /// (Afterfall/Surface, URP Lit / Unlit / Terrain). Deriving from assets guarantees the required
    /// shader variants are included in player builds. Materials are cached per parameter set so
    /// renderers share them (SRP batcher friendly).
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _surface, _transparent, _emissive, _water, _terrain, _unlitTransparent;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color = Shader.PropertyToID("_Color");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int OcclusionStrength = Shader.PropertyToID("_OcclusionStrength");
        private static readonly int ObjectSpace = Shader.PropertyToID("_ObjectSpace");
        private static readonly int WindStrength = Shader.PropertyToID("_WindStrength");
        private static readonly int Cull = Shader.PropertyToID("_Cull");

        public static bool IsUrp => GraphicsSettings.currentRenderPipeline != null;

        private static Material Base(ref Material slot, string name, bool transparent)
        {
            if (slot != null) return slot;
            slot = Resources.Load<Material>("Materials/" + name);
            if (slot == null)
            {
                Shader s = Shader.Find(IsUrp ? "Universal Render Pipeline/Lit" : "Standard");
                slot = new Material(s) { name = name + " (fallback)" };
                Debug.LogWarning($"[Afterfall] Material asset Resources/Materials/{name} missing; using fallback shader {s?.name}.");
                if (transparent) slot.renderQueue = 3000;
            }
            return slot;
        }

        /// <summary>A nearly flat surface in one colour (subtle plain detail texture).</summary>
        public static Material Lit(Color color, float smoothness = 0.12f, float metallic = 0f) =>
            Surface(Art.Surface.Plain, color, smoothness, metallic);

        /// <summary>
        /// A textured surface: <paramref name="tint"/> is its average colour (an sRGB colour, as authored;
        /// Unity converts it to linear), the procedural detail maps
        /// add relief and variation. Smoothness and metallic default to the surface's own values.
        /// Use <paramref name="objectSpace"/> for anything that moves or is rotated where the pattern
        /// direction matters (characters, held items, roof slopes, tilted boards): the pattern then
        /// follows the part instead of the world. <paramref name="tileScale"/> scales the pattern size,
        /// <paramref name="wind"/> sways vertices with height above the part's pivot (foliage, grass).
        /// </summary>
        public static Material Surface(Art.Surface surface, Color tint, float? smoothness = null, float? metallic = null,
            bool objectSpace = false, float tileScale = 1f, float wind = 0f, Color? emission = null, bool doubleSided = false)
        {
            float sm = smoothness ?? surface.Smoothness, me = metallic ?? surface.Metallic;
            Color emi = emission ?? UnityEngine.Color.black;
            string key = $"srf:{surface.Key}:{ColorUtility.ToHtmlStringRGBA(tint)}:{sm:0.00}:{me:0.00}:{(objectSpace ? 1 : 0)}:{tileScale:0.###}:{wind:0.##}:{ColorUtility.ToHtmlStringRGBA(emi)}:{(doubleSided ? 1 : 0)}";
            if (Cache.TryGetValue(key, out Material m)) return m;
            ProceduralTextures.Pair maps = ProceduralTextures.Get(surface);
            m = new Material(Base(ref _surface, "AfterfallSurface", false)) { name = key, enableInstancing = true };
            SetColor(m, tint);
            m.SetTexture(BaseMap, maps.Albedo);
            m.SetTexture(BumpMap, maps.Detail);
            float tiling = 1f / Mathf.Max(0.01f, surface.TileMeters * tileScale);
            m.SetTextureScale(BaseMap, new Vector2(tiling, tiling));
            m.SetFloat(BumpScale, surface.NormalStrength);
            m.SetFloat(OcclusionStrength, surface.CavityStrength);
            m.SetFloat(Smoothness, sm);
            m.SetFloat(Metallic, me);
            m.SetFloat(ObjectSpace, objectSpace ? 1f : 0f);
            m.SetFloat(WindStrength, wind);
            m.SetFloat(Cull, doubleSided ? 0f : 2f);
            m.SetColor(EmissionColor, emi);
            Cache[key] = m;
            return m;
        }

        public static Material Emissive(Color color, Color emission)
        {
            string key = $"emi:{ColorUtility.ToHtmlStringRGBA(color)}:{ColorUtility.ToHtmlStringRGBA(emission)}";
            if (Cache.TryGetValue(key, out Material m)) return m;
            m = new Material(Base(ref _emissive, "AfterfallEmissive", false)) { name = key };
            SetColor(m, color);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColor, emission);
            Cache[key] = m;
            return m;
        }

        /// <summary>A unique (non-shared) emissive material whose emission can be animated.</summary>
        public static Material EmissiveInstance(Color color, Color emission)
        {
            var m = new Material(Base(ref _emissive, "AfterfallEmissive", false)) { name = "emissive instance" };
            SetColor(m, color);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColor, emission);
            return m;
        }

        public static void SetEmission(Material m, Color emission) => m.SetColor(EmissionColor, emission);

        public static Material Ghost(Color color)
        {
            string key = $"ghost:{ColorUtility.ToHtmlStringRGBA(color)}";
            if (Cache.TryGetValue(key, out Material m)) return m;
            m = new Material(Base(ref _transparent, "AfterfallTransparent", true)) { name = key };
            SetColor(m, color);
            Cache[key] = m;
            return m;
        }

        public static Material UnlitTransparent(Color color)
        {
            string key = $"unlit:{ColorUtility.ToHtmlStringRGBA(color)}";
            if (Cache.TryGetValue(key, out Material m)) return m;
            m = new Material(Base(ref _unlitTransparent, "AfterfallUnlitTransparent", true)) { name = key };
            SetColor(m, color);
            Cache[key] = m;
            return m;
        }

        /// <summary>Unique unlit transparent material with a texture (fog of war overlay).</summary>
        public static Material UnlitTransparentTextured(Texture texture)
        {
            var m = new Material(Base(ref _unlitTransparent, "AfterfallUnlitTransparent", true)) { name = "unlit textured" };
            SetColor(m, UnityEngine.Color.white);
            m.mainTexture = texture;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            return m;
        }

        public static Material Water()
        {
            if (_water != null) return _water;
            return Base(ref _water, "AfterfallWater", true);
        }

        public static Material Terrain()
        {
            return Base(ref _terrain, "AfterfallTerrain", false);
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c);
            if (m.HasProperty(Color)) m.SetColor(Color, c);
        }
    }
}
