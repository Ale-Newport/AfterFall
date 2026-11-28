using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Entities by 32 m cell, for "what is near here" questions that must not scan every entity
    /// (a city holds tens of thousands of containers and trees). Positions are fixed while indexed.
    /// </summary>
    public sealed class SpatialGrid
    {
        private const float Cell = 32f;
        private readonly Dictionary<long, List<EntityId>> _cells = new Dictionary<long, List<EntityId>>();

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        private static int C(float v) => (int)Math.Floor(v / Cell);

        public void Add(EntityId id, Vec3 p)
        {
            long k = Key(C(p.X), C(p.Z));
            if (!_cells.TryGetValue(k, out List<EntityId> list)) _cells[k] = list = new List<EntityId>(4);
            list.Add(id);
        }

        public void Remove(EntityId id, Vec3 p)
        {
            long k = Key(C(p.X), C(p.Z));
            if (_cells.TryGetValue(k, out List<EntityId> list))
            {
                list.Remove(id);
                if (list.Count == 0) _cells.Remove(k);
            }
        }

        /// <summary>Adds the ids in cells overlapping the square of half-side <paramref name="radius"/> around a point (callers check distance).</summary>
        public void Query(Vec3 p, float radius, List<EntityId> into)
        {
            into.Clear();
            int x0 = C(p.X - radius), x1 = C(p.X + radius), z0 = C(p.Z - radius), z1 = C(p.Z + radius);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (_cells.TryGetValue(Key(x, z), out List<EntityId> list)) into.AddRange(list);
        }

        public void Clear() => _cells.Clear();
    }
}
