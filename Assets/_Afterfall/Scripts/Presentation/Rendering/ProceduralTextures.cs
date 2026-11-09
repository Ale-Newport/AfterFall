using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Turns <see cref="Art.Surface"/> recipes into GPU textures, once per surface. The maps are
    /// computed in plain C#, so <see cref="Prewarm"/> can build many of them on worker threads
    /// while a new game loads; only the texture upload happens on the main thread.
    /// </summary>
    public static class ProceduralTextures
    {
        public struct Pair
        {
            public Texture2D Albedo;
            public Texture2D Detail;
        }

        private static readonly Dictionary<string, Pair> Cache = new Dictionary<string, Pair>();

        public static Pair Get(Art.Surface surface)
        {
            if (Cache.TryGetValue(surface.Key, out Pair p) && p.Albedo != null) return p;
            p = Upload(surface, surface.Generate(surface.Resolution));
            Cache[surface.Key] = p;
            return p;
        }

        private static Task<SurfaceMaps[]> _pending;
        private static List<Art.Surface> _pendingSurfaces;

        /// <summary>Starts generating every declared surface on worker threads as soon as the game launches.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BeginPrewarm()
        {
            if (_pending != null) return;
            List<Art.Surface> all = DeclaredSurfaces();
            _pendingSurfaces = all;
            _pending = Task.Run(() =>
            {
                var maps = new SurfaceMaps[all.Count];
                Parallel.For(0, all.Count, i => maps[i] = all[i].Generate(all[i].Resolution));
                return maps;
            });
        }

        /// <summary>
        /// Makes sure every surface declared as a static field anywhere in the Presentation assembly
        /// (the shared catalogue and each area's own surfaces) is uploaded, so first use never hitches.
        /// Uses the maps generated in the background since launch when they are ready.
        /// </summary>
        public static void PrewarmCatalogue()
        {
            if (_pending != null)
            {
                try
                {
                    SurfaceMaps[] maps = _pending.Result;
                    for (int i = 0; i < maps.Length; i++)
                        if (!Cache.ContainsKey(_pendingSurfaces[i].Key)) Cache[_pendingSurfaces[i].Key] = Upload(_pendingSurfaces[i], maps[i]);
                }
                catch (System.AggregateException e)
                {
                    Debug.LogWarning("[Afterfall] Background texture generation failed, generating now: " + e.InnerException?.Message);
                }
                _pending = null;
                _pendingSurfaces = null;
            }
            Prewarm(DeclaredSurfaces());
        }

        private static List<Art.Surface> DeclaredSurfaces()
        {
            var all = new List<Art.Surface>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            foreach (System.Type t in typeof(Art.Surface).Assembly.GetTypes())
            {
                if (t.ContainsGenericParameters) continue;
                foreach (System.Reflection.FieldInfo f in t.GetFields(flags))
                    if (f.FieldType == typeof(Art.Surface) && !f.IsLiteral && f.GetValue(null) is Art.Surface s) all.Add(s);
            }
            return all;
        }

        /// <summary>Generates the maps of every surface not cached yet in parallel, then uploads them.</summary>
        public static void Prewarm(IEnumerable<Art.Surface> surfaces)
        {
            var todo = new List<Art.Surface>();
            var seen = new HashSet<string>();
            foreach (Art.Surface s in surfaces)
                if (s != null && seen.Add(s.Key) && !Cache.ContainsKey(s.Key)) todo.Add(s);
            if (todo.Count == 0) return;
            var maps = new SurfaceMaps[todo.Count];
            Parallel.For(0, todo.Count, i => maps[i] = todo[i].Generate(todo[i].Resolution));
            for (int i = 0; i < todo.Count; i++) Cache[todo[i].Key] = Upload(todo[i], maps[i]);
        }

        private static Pair Upload(Art.Surface surface, SurfaceMaps maps)
        {
            return new Pair
            {
                Albedo = ToTexture(surface.Key + " albedo", maps.Size, maps.Albedo),
                Detail = ToTexture(surface.Key + " detail", maps.Size, maps.Detail)
            };
        }

        private static Texture2D ToTexture(string name, int size, float[] rgba)
        {
            // Linear: the albedo is a multiplier and the detail map holds vectors, neither is sRGB colour.
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 6
            };
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                int o = i * 4;
                px[i] = new Color32(ToByte(rgba[o]), ToByte(rgba[o + 1]), ToByte(rgba[o + 2]), ToByte(rgba[o + 3]));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
