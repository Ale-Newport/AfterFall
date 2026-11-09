using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afterfall.Domain.Items;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// Item icon textures rendered from <see cref="ItemIconArt"/> once per item and cached, plus the small
    /// UI textures that go with them (slot shading, glyphs). UI code always asks here and never creates
    /// textures itself, so refreshing a window costs no texture work.
    /// </summary>
    public static class ItemIcons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> Glyphs = new Dictionary<string, Texture2D>();
        private static readonly ConcurrentQueue<KeyValuePair<string, byte[]>> Rendered = new ConcurrentQueue<KeyValuePair<string, byte[]>>();
        private static Texture2D _slotShade;
        private static ItemCatalog _warmCatalog;

        public static Texture2D Get(ItemDefinition def)
        {
            if (def == null) return null;
            if (Cache.TryGetValue(def.Id, out Texture2D tex) && tex != null) return tex;
            // Not warmed up yet: render it right here (a few milliseconds, once).
            IconCanvas art = ItemIconArt.Render(def.Id, def.Category.ToString(), def.Color, ItemIconArt.DefaultSize);
            tex = ToTexture(art.ToRgba32(true), art.Size, "icon " + def.Id);
            Cache[def.Id] = tex;
            return tex;
        }

        /// <summary>The icon for an item id, or null when the catalogue does not know it.</summary>
        public static Texture2D Get(ItemCatalog catalog, string itemId) =>
            catalog != null && catalog.TryGet(itemId, out ItemDefinition def) ? Get(def) : null;

        /// <summary>
        /// Prepares every icon of <paramref name="catalog"/> without stalling the game: the first call starts
        /// a background task that rasterises them (<see cref="IconCanvas"/> is plain C#), and every call turns
        /// up to <paramref name="uploadsPerFrame"/> finished pixel buffers into textures. The HUD calls it each
        /// frame, so the backpack and crafting windows open with all icons ready.
        /// </summary>
        public static void WarmUp(ItemCatalog catalog, int uploadsPerFrame)
        {
            if (catalog == null) return;
            if (_warmCatalog != catalog)
            {
                _warmCatalog = catalog;
                var jobs = new List<ItemDefinition>();
                foreach (ItemDefinition def in catalog.All)
                    if (def != null && !(Cache.TryGetValue(def.Id, out Texture2D t) && t != null)) jobs.Add(def);
                if (jobs.Count > 0) Task.Run(() => RenderAll(jobs));
            }
            for (int i = 0; i < uploadsPerFrame && Rendered.TryDequeue(out KeyValuePair<string, byte[]> done); i++)
            {
                if (Cache.TryGetValue(done.Key, out Texture2D t) && t != null) continue; // drawn on demand meanwhile
                Cache[done.Key] = ToTexture(done.Value, ItemIconArt.DefaultSize, "icon " + done.Key);
            }
        }

        private static void RenderAll(List<ItemDefinition> jobs)
        {
            try
            {
                foreach (ItemDefinition def in jobs)
                {
                    IconCanvas art = ItemIconArt.Render(def.Id, def.Category.ToString(), def.Color, ItemIconArt.DefaultSize);
                    Rendered.Enqueue(new KeyValuePair<string, byte[]>(def.Id, art.ToRgba32(true)));
                }
            }
            catch (Exception e)
            {
                // Icons still render on demand on the main thread; just report why the warm-up stopped.
                Debug.LogWarning("Item icon warm-up stopped: " + e.Message);
            }
        }

        /// <summary>A UI symbol drawn in the icon style ("arrow", "lock", "hand", "clock").</summary>
        public static Texture2D Glyph(string name)
        {
            if (Glyphs.TryGetValue(name, out Texture2D tex) && tex != null) return tex;
            IconCanvas art = ItemIconArt.RenderGlyph(name, 64);
            tex = ToTexture(art.ToRgba32(true), art.Size, "glyph " + name);
            Glyphs[name] = tex;
            return tex;
        }

        /// <summary>
        /// A soft overlay for item slots: a faint sheen at the top, shade toward the bottom and darker
        /// edges, so a slot reads as a recessed well over whatever background colour its state sets.
        /// </summary>
        public static Texture2D SlotShade
        {
            get
            {
                if (_slotShade != null) return _slotShade;
                const int n = 32;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float v = (y + 0.5f) / n; // 0 at the bottom row (Unity textures are bottom-up)
                        float u = (x + 0.5f) / n;
                        float edge = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2f;
                        float vignette = Mathf.SmoothStep(0.55f, 1f, edge) * 0.22f;
                        float bottom = Mathf.Clamp01(0.45f - v) / 0.45f * 0.3f;
                        float top = Mathf.Clamp01(v - 0.6f) / 0.4f * 0.09f;
                        float dark = Mathf.Clamp01(vignette + bottom);
                        // Premultiplied mix of a white sheen over a black shade, stored straight.
                        float a = Mathf.Clamp01(dark + top);
                        float white = a > 0f ? top / a : 0f;
                        px[y * n + x] = new Color32((byte)(white * 255f), (byte)(white * 255f), (byte)(white * 245f), (byte)(a * 255f));
                    }
                }
                _slotShade = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    name = "slot shade",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _slotShade.SetPixels32(px);
                _slotShade.Apply(false, true);
                return _slotShade;
            }
        }

        private static Texture2D ToTexture(byte[] rgba, int size, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true);
            return tex;
        }
    }
}
