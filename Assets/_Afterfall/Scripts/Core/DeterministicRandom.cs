using System;

namespace Afterfall.Core
{
    /// <summary>
    /// Small, fast, fully deterministic PRNG (xorshift64*). Its entire state is a single
    /// ulong so it can be persisted and resumed exactly. Never use System.Random or
    /// UnityEngine.Random for simulation decisions that must be reproducible.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong State
        {
            get => _state;
            set => _state = value == 0 ? 0x9E3779B97F4A7C15UL : value;
        }

        public ulong NextULong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1.0f / (1UL << 24));

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            ulong span = (ulong)(maxExclusive - min);
            return min + (int)(NextULong() % span);
        }

        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>Derives an independent stream (e.g. per POI) without disturbing this one.</summary>
        public static DeterministicRandom Derive(int seed, string salt)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL ^ (ulong)(uint)seed;
                foreach (char c in salt)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }
                return new DeterministicRandom(h);
            }
        }
    }
}
