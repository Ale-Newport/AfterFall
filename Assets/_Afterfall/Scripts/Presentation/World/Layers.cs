using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Physics layers used by the game (declared in ProjectSettings/TagManager.asset).
    /// Falls back to Default if a layer is missing so the game still runs.
    /// </summary>
    public static class Layers
    {
        public static readonly int Terrain = Get("Terrain");
        public static readonly int Structure = Get("Structure");
        public static readonly int Prop = Get("Prop");
        public static readonly int Resource = Get("Resource");
        public static readonly int Character = Get("Character");
        public static readonly int Zombie = Get("Zombie");
        public static readonly int Interactable = Get("Interactable");
        public static readonly int Ghost = Get("Ghost");

        /// <summary>Solid world geometry the camera and line of sight collide with.</summary>
        public static int SolidMask => Mask(Terrain, Structure, Prop, Resource) | 1;

        /// <summary>Surfaces a blueprint may rest on.</summary>
        public static int PlacementSurfaceMask => Mask(Terrain, Structure) | 1;

        /// <summary>Things that block a placement.</summary>
        public static int PlacementBlockerMask => Mask(Structure, Prop, Resource, Character, Zombie);

        public static int InteractMask => Mask(Structure, Prop, Resource, Interactable, Zombie, Terrain) | 1;

        private static int Get(string name)
        {
            int l = LayerMask.NameToLayer(name);
            return l < 0 ? 0 : l;
        }

        public static int Mask(params int[] layers)
        {
            int m = 0;
            foreach (int l in layers) m |= 1 << l;
            return m;
        }
    }
}
