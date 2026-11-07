using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Fog-of-war knowledge: which map cells the player has seen and which points of
    /// interest are known. The strategic camera only reveals what is recorded here.
    /// </summary>
    public sealed class ExplorationState
    {
        public const float CellSize = 16f;

        public ExplorationState(float worldSize)
        {
            WorldSize = worldSize;
            CellsPerSide = (int)Math.Ceiling(worldSize / CellSize);
            Revealed = new bool[CellsPerSide * CellsPerSide];
        }

        public float WorldSize { get; }
        public int CellsPerSide { get; }
        public bool[] Revealed { get; }
        public HashSet<string> DiscoveredPois { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Changes whenever new cells are revealed (presentation rebuilds the fog texture).</summary>
        public int Version { get; private set; }

        public bool IsRevealed(float x, float z)
        {
            int cx = (int)(x / CellSize);
            int cz = (int)(z / CellSize);
            if (cx < 0 || cz < 0 || cx >= CellsPerSide || cz >= CellsPerSide) return false;
            return Revealed[cz * CellsPerSide + cx];
        }

        /// <summary>Reveals all cells within <paramref name="radius"/> of a position. Returns true if anything changed.</summary>
        public bool Reveal(Vec3 center, float radius)
        {
            bool changed = false;
            int r = (int)Math.Ceiling(radius / CellSize);
            int ccx = (int)(center.X / CellSize);
            int ccz = (int)(center.Z / CellSize);
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = ccx + dx;
                    int cz = ccz + dz;
                    if (cx < 0 || cz < 0 || cx >= CellsPerSide || cz >= CellsPerSide) continue;
                    float wx = (cx + 0.5f) * CellSize - center.X;
                    float wz = (cz + 0.5f) * CellSize - center.Z;
                    if (wx * wx + wz * wz > (radius + CellSize * 0.5f) * (radius + CellSize * 0.5f)) continue;
                    int idx = cz * CellsPerSide + cx;
                    if (Revealed[idx]) continue;
                    Revealed[idx] = true;
                    changed = true;
                }
            }
            if (changed) Version++;
            return changed;
        }

        public float RevealedFraction
        {
            get
            {
                int n = 0;
                foreach (bool b in Revealed)
                    if (b) n++;
                return n / (float)Revealed.Length;
            }
        }
    }
}
