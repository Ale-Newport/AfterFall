using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Finds a collision-safe place for the survivor to stand: on walkable ground or a floor
    /// (never a roof or ceiling, which carry the Not Walkable area), out of deep water, with a
    /// standing capsule's worth of free space. Used when landing from free flight and teleporting.
    /// </summary>
    public static class SafeGround
    {
        public const float Radius = 0.35f;
        public const float Height = 1.8f;

        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static int _notWalkableArea = -2;

        private static int StandMask => Layers.Mask(Layers.Terrain, Layers.Structure, Layers.Prop, Layers.Resource) | 1;

        /// <summary>
        /// Searches rings around <paramref name="target"/> (out to <paramref name="searchRadius"/>)
        /// for a place to stand, casting down from <paramref name="castFromY"/>. Casting from the
        /// current height lands on the floor below; casting from high above lands on the topmost
        /// walkable surface (a building's floor, not its roof).
        /// </summary>
        public static bool TryFind(GameController gc, Vector3 target, float castFromY, float searchRadius, out Vector3 spot)
        {
            if (TryStandAt(gc, target, castFromY, out spot)) return true;
            for (float r = 1.5f; r <= searchRadius; r += 1.5f)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 1.5f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * 2f * Mathf.PI / steps;
                    Vector3 p = target + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (TryStandAt(gc, p, castFromY, out spot)) return true;
                }
            }
            spot = target;
            return false;
        }

        /// <summary>True if a standing survivor fits at <paramref name="feet"/> without touching anything solid.</summary>
        public static bool Fits(Vector3 feet)
        {
            return !Physics.CheckCapsule(feet + Vector3.up * (Radius + 0.06f), feet + Vector3.up * (Height - Radius), Radius, StandMask | Layers.Mask(Layers.Vehicle), QueryTriggerInteraction.Ignore);
        }

        private static bool TryStandAt(GameController gc, Vector3 p, float castFromY, out Vector3 spot)
        {
            spot = p;
            var layout = gc.Session.World.Layout;
            if (!layout.IsInsideMap(p.x, p.z, 3f)) return false;
            float ground = layout.HeightAt(p.x, p.z);
            float top = Mathf.Max(castFromY, ground + 0.5f);
            var origin = new Vector3(p.x, top, p.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, top - ground + 3f, StandMask, QueryTriggerInteraction.Ignore);
            if (n == 0)
            {
                // Nothing below (above the terrain collider's reach): use the height field.
                spot = new Vector3(p.x, ground + 0.05f, p.z);
                return !layout.IsWater(p.x, p.z) && Fits(spot);
            }
            var sorted = new List<RaycastHit>(n);
            for (int i = 0; i < n; i++) sorted.Add(Hits[i]);
            sorted.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in sorted)
            {
                if (hit.normal.y < 0.7f || IsNotWalkable(hit.collider)) continue;
                Vector3 feet = hit.point + Vector3.up * 0.05f;
                if (layout.WaterAt(feet.x, feet.z, out float level) && feet.y < level + 0.3f) return false;
                if (!Fits(feet)) continue;
                spot = feet;
                return true;
            }
            return false;
        }

        /// <summary>Roofs, ceilings and canopies are marked Not Walkable for the NavMesh; nobody lands on them either.</summary>
        public static bool IsNotWalkable(Collider c)
        {
            if (_notWalkableArea == -2) _notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
            NavMeshModifier m = c.GetComponentInParent<NavMeshModifier>();
            return m != null && m.overrideArea && m.area == _notWalkableArea;
        }
    }
}
