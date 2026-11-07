using System;
using System.Globalization;

namespace Afterfall.Core
{
    /// <summary>
    /// Stable identity for every persistent runtime entity (structures, inventories,
    /// resource nodes, drops, zombies...). Values are allocated by <see cref="IdAllocator"/>
    /// and saved verbatim, so relationships survive save/load without object references.
    /// </summary>
    [Serializable]
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public static readonly EntityId None = new EntityId(0);

        public readonly long Value;

        public EntityId(long value)
        {
            Value = value;
        }

        public bool IsValid => Value > 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public override string ToString() => "#" + Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
    }

    /// <summary>Monotonic, saveable id source. Never reuses ids within a world.</summary>
    public sealed class IdAllocator
    {
        private long _next;

        public IdAllocator(long next = 1)
        {
            _next = next < 1 ? 1 : next;
        }

        /// <summary>The value that the next call to <see cref="Allocate"/> will return. Persisted.</summary>
        public long NextValue => _next;

        public EntityId Allocate() => new EntityId(_next++);

        /// <summary>Ensures ids loaded from a save are never handed out again.</summary>
        public void EnsureAbove(EntityId id)
        {
            if (id.Value >= _next) _next = id.Value + 1;
        }
    }
}
