using System.Collections.Generic;
using Afterfall.Domain.Items;
using Afterfall.Presentation.Art;
using UnityEngine;

namespace Afterfall.Presentation.UI
{
    /// <summary>Item icon textures rendered from <see cref="ItemIconArt"/> once per item and cached.</summary>
    public static class ItemIcons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(ItemDefinition def)
        {
            if (def == null) return null;
            if (Cache.TryGetValue(def.Id, out Texture2D tex) && tex != null) return tex;
            IconCanvas art = ItemIconArt.Render(def.Id, def.Category.ToString(), def.Color, ItemIconArt.DefaultSize);
            tex = new Texture2D(art.Size, art.Size, TextureFormat.RGBA32, true)
            {
                name = "icon " + def.Id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0
            };
            tex.SetPixelData(art.ToRgba32(true), 0);
            tex.Apply(true, true);
            Cache[def.Id] = tex;
            return tex;
        }
    }
}
