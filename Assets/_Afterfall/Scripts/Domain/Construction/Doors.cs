using Afterfall.Core;
using Afterfall.Domain.Items;

namespace Afterfall.Domain.Construction
{
    /// <summary>
    /// Rules for doors: structures whose definition has <see cref="BuildingDefinition.IsDoor"/>.
    /// A door is open or closed, may be locked with a named lock (opened by a key item tagged
    /// with the same lock id), and breaks below 30 % health, after which it stays passable
    /// until repaired. Closed doors block the infected, who batter them; colonists open unlocked ones.
    /// </summary>
    public static class DoorRules
    {
        /// <summary>The key item; each key's <see cref="ItemStack.Tag"/> names the lock it opens.</summary>
        public const string KeyItem = "key";

        /// <summary>Doors further above or below than this are on another floor.</summary>
        public const float FloorReach = 2.4f;

        public static bool IsDoor(Structure s, BuildingDefinition def) => s != null && def != null && def.IsDoor && s.IsComplete;

        public static bool IsBroken(Structure s, BuildingDefinition def) => def.IsBroken(s.Health);

        /// <summary>Open, or broken off its hinges: people and the infected can pass.</summary>
        public static bool IsPassable(Structure s, BuildingDefinition def) => s.DoorOpen || IsBroken(s, def);

        public static bool HasKey(Inventory inv, Structure s) => inv != null && !string.IsNullOrEmpty(s.LockId) && inv.HasTagged(KeyItem, s.LockId);

        /// <summary>
        /// Opens or closes a door. A locked door opens only for someone holding its key (which
        /// unlocks it); a broken door cannot be closed.
        /// </summary>
        public static OpResult Toggle(Structure s, BuildingDefinition def, Inventory keys)
        {
            if (!IsDoor(s, def)) return OpResult.Fail("not_door", "That is not a door.");
            if (IsBroken(s, def)) return OpResult.Fail("broken", $"The {def.Name.ToLowerInvariant()} is broken off its hinges. Repair it with a hammer to close it again.");
            if (s.DoorOpen)
            {
                s.DoorOpen = false;
                return OpResult.Success($"{def.Name} closed.");
            }
            if (s.DoorLocked)
            {
                if (!HasKey(keys, s)) return OpResult.Fail("locked", $"Locked. It needs the key to {LockName(s)}.");
                s.DoorLocked = false;
                s.DoorOpen = true;
                s.Touched = true; // a seeded door no longer as it was made
                return OpResult.Success($"Unlocked {LockName(s)} with its key and opened the door.");
            }
            s.DoorOpen = true;
            return OpResult.Success($"{def.Name} opened.");
        }

        /// <summary>Locks a closed door (or unlocks it) with its key.</summary>
        public static OpResult SetLocked(Structure s, BuildingDefinition def, Inventory keys, bool locked)
        {
            if (!IsDoor(s, def)) return OpResult.Fail("not_door", "That is not a door.");
            if (string.IsNullOrEmpty(s.LockId)) return OpResult.Fail("no_lock", $"The {def.Name.ToLowerInvariant()} has no lock.");
            if (s.DoorLocked == locked) return OpResult.Success(locked ? "Already locked." : "Already unlocked.");
            if (IsBroken(s, def)) return OpResult.Fail("broken", "The lock is useless while the door is broken.");
            if (locked && s.DoorOpen) return OpResult.Fail("open", "Close the door before locking it.");
            if (!HasKey(keys, s)) return OpResult.Fail("no_key", $"You need the key to {LockName(s)}.");
            s.DoorLocked = locked;
            s.Touched = true;
            return OpResult.Success(locked ? $"Locked {LockName(s)}." : $"Unlocked {LockName(s)}.");
        }

        /// <summary>
        /// A colonist opening a door on their way: only unlocked, closed, intact doors. Returns a
        /// reason when they cannot (for the colonist's recent problems).
        /// </summary>
        public static OpResult OpenForColonist(Structure s, BuildingDefinition def)
        {
            if (!IsDoor(s, def)) return OpResult.Fail("not_door", "That is not a door.");
            if (IsPassable(s, def)) return OpResult.Success();
            if (s.DoorLocked) return OpResult.Fail("locked", $"the door to {LockName(s)} is locked");
            s.DoorOpen = true;
            return OpResult.Success();
        }

        public static string LockName(Structure s) => string.IsNullOrEmpty(s.LockId) ? "this door" : s.LockId;
    }
}
