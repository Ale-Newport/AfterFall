using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Shared runtime materials derived from base material assets in Resources/Materials
    /// (URP Lit / Unlit / Terrain). Deriving from assets guarantees the required shader
    /// variants are included in player builds. Materials are cached per colour so renderers
    /// share them (SRP batcher / instancing friendly).
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _lit, _transparent, _emissive, _water, _terrain, _unlitTransparent;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color = Shader.PropertyToID("_Color");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

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

        public static Material Lit(Color color, float smoothness = 0.12f, float metallic = 0f)
        {
            string key = $"lit:{ColorUtility.ToHtmlStringRGBA(color)}:{smoothness:0.00}:{metallic:0.00}";
            if (Cache.TryGetValue(key, out Material m)) return m;
            m = new Material(Base(ref _lit, "AfterfallLit", false)) { name = key, enableInstancing = true };
            SetColor(m, color);
            m.SetFloat(Smoothness, smoothness);
            m.SetFloat(Metallic, metallic);
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
