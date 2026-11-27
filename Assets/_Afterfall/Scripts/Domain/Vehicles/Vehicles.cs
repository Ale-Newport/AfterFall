using System;
using Afterfall.Core;
using Afterfall.Domain.Items;

namespace Afterfall.Domain.Vehicles
{
    public enum VehicleCategory
    {
        Car,
        Pickup,
        Atv
    }

    /// <summary>Static description of a vehicle type (content: vehicles.json).</summary>
    public sealed class VehicleDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public VehicleCategory Category { get; set; }

        /// <summary>Kilograms (physics).</summary>
        public float Mass { get; set; } = 1300f;

        /// <summary>Total drive torque at the driven wheels (N·m) and brake torque per wheel.</summary>
        public float MotorTorque { get; set; } = 1400f;
        public float BrakeTorque { get; set; } = 2600f;
        public float MaxSpeedKmh { get; set; } = 120f;
        public float MaxSteerDegrees { get; set; } = 32f;
        public bool FourWheelDrive { get; set; }

        /// <summary>Litres in a full tank; litres per kilometre at a steady cruise; litres per hour idling.</summary>
        public float FuelCapacity { get; set; } = 45f;
        public float FuelPerKm { get; set; } = 0.09f;
        public float IdleFuelPerHour { get; set; } = 0.8f;

        public int Seats { get; set; } = 4;
        public int Doors { get; set; } = 4;
        public int StorageSlots { get; set; } = 12;
        public float StorageMaxWeight { get; set; } = 120f;

        /// <summary>Body size in metres (X width, Y height, Z length) and the wheel layout.</summary>
        public float SizeX { get; set; } = 1.8f;
        public float SizeY { get; set; } = 1.45f;
        public float SizeZ { get; set; } = 4.5f;
        public float WheelFrontZ { get; set; } = 1.35f;
        public float WheelRearZ { get; set; } = -1.35f;
        public float WheelHalfTrack { get; set; } = 0.78f;
        public float WheelRadius { get; set; } = 0.32f;

        /// <summary>Seconds of work for one hotwiring attempt and how hard it is (0 easy … 1 very hard).</summary>
        public float HotwireSeconds { get; set; } = 8f;
        public float HotwireDifficulty { get; set; } = 0.4f;

        /// <summary>How far the running engine can be heard by the infected (m).</summary>
        public float EngineNoise { get; set; } = 26f;

        /// <summary>Presentation key that selects the procedural model.</summary>
        public string Visual { get; set; } = "sedan";

        public string[] Paints { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// A vehicle in the world (saved): where it stands, its fuel and condition, its lock and the
    /// lock's name (shared with its key), whether it was hotwired, what its trunk holds, and what
    /// the player knows about it. The physical body only visualises this; its transform is
    /// written back while it moves.
    /// </summary>
    public sealed class VehicleState
    {
        public EntityId Id { get; set; }
        public string DefId { get; set; } = "";
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public int Paint { get; set; }

        public float Fuel { get; set; }

        /// <summary>Component condition, 0–100.</summary>
        public float Engine { get; set; } = 100f;
        public float Body { get; set; } = 100f;
        public float Tyres { get; set; } = 100f;
        public float Battery { get; set; } = 100f;

        public bool Locked { get; set; }

        /// <summary>The lock's name, written on its key ("Blue sedan · MB-2187").</summary>
        public string LockId { get; set; } = "";

        /// <summary>The key is in the ignition (it starts without carrying a key).</summary>
        public bool KeyInIgnition { get; set; }

        /// <summary>Started by hotwiring: it starts without a key from now on.</summary>
        public bool Hotwired { get; set; }

        /// <summary>A side window was smashed to get in; the doors can no longer be locked.</summary>
        public bool WindowBroken { get; set; }

        public bool EngineOn { get; set; }

        /// <summary>Doors standing open (bit per door, front-left first).</summary>
        public int DoorsOpen { get; set; }

        /// <summary>The player has been in it or inspected it: it shows on the map.</summary>
        public bool Known { get; set; }

        /// <summary>Marked as the settlement's vehicle.</summary>
        public bool Claimed { get; set; }

        public double OdometerMetres { get; set; }

        /// <summary>Spawned from the creative panel (can be deleted or reset there).</summary>
        public bool DebugSpawned { get; set; }

        /// <summary>Stable key of the layout seed that placed it ("" otherwise).</summary>
        public string SeedKey { get; set; } = "";

        public Inventory Storage { get; set; }

        /// <summary>Worst of the components needed to run (engine, tyres): below 10 it will not drive.</summary>
        public bool Drivable => Engine >= 10f && Tyres >= 5f;
    }

    public enum VehicleComponent
    {
        Engine,
        Body,
        Tyres,
        Battery
    }

    /// <summary>
    /// Rules for vehicles: getting in, keys and locks, starting, an abstract hotwiring skill check,
    /// fuel use, refuelling, repairs and damage. Every refusal explains itself.
    /// </summary>
    public static class VehicleRules
    {
        public const string KeyItem = "key";
        public const string HotwireTool = "screwdriver";
        public const string HotwireWire = "copper_wire";
        public const string FuelItem = "fuel_can";
        public const float LitresPerCan = 12f;

        public static bool HasKey(Inventory inv, VehicleState v) => inv != null && !string.IsNullOrEmpty(v.LockId) && inv.HasTagged(KeyItem, v.LockId);

        /// <summary>Opens the doors for someone outside: with the key it unlocks, without it a locked vehicle stays shut.</summary>
        public static OpResult Enter(VehicleState v, VehicleDefinition def, Inventory keys)
        {
            if (v.Locked)
            {
                if (!HasKey(keys, v)) return OpResult.Fail("locked", $"Locked. It needs the key to {Name(v, def)}, or a smashed window.");
                v.Locked = false;
                v.Known = true;
                return OpResult.Success($"Unlocked {Name(v, def)} with its key.");
            }
            v.Known = true;
            return OpResult.Success($"You get into the {def.Name.ToLowerInvariant()}.");
        }

        /// <summary>Locks or unlocks the doors with the key (from inside or out).</summary>
        public static OpResult SetLocked(VehicleState v, VehicleDefinition def, Inventory keys, bool locked)
        {
            if (def.Doors == 0) return OpResult.Fail("no_doors", $"A {def.Name.ToLowerInvariant()} has no doors to lock.");
            if (v.Locked == locked) return OpResult.Success(locked ? "Already locked." : "Already unlocked.");
            if (locked && v.WindowBroken) return OpResult.Fail("window", "With a smashed window, locking it is pointless.");
            if (!HasKey(keys, v)) return OpResult.Fail("no_key", $"You need the key to {Name(v, def)}.");
            v.Locked = locked;
            return OpResult.Success(locked ? $"Locked {Name(v, def)}." : $"Unlocked {Name(v, def)}.");
        }

        /// <summary>Smashes a side window to get into a locked vehicle without its key (noisy, damages the body).</summary>
        public static OpResult SmashWindow(VehicleState v, VehicleDefinition def)
        {
            if (!v.Locked) return OpResult.Fail("unlocked", "It is not locked.");
            if (def.Doors == 0) return OpResult.Fail("no_doors", "There is no window to smash.");
            v.Locked = false;
            v.WindowBroken = true;
            v.Body = Math.Max(0f, v.Body - 4f);
            v.Known = true;
            return OpResult.Success("You smash the side window and unlock the door from inside. That was loud.");
        }

        /// <summary>Why the engine would not start, or "" if it would.</summary>
        public static string StartProblem(VehicleState v, VehicleDefinition def, Inventory keys)
        {
            if (!HasKey(keys, v) && !v.KeyInIgnition && !v.Hotwired) return "No key. Find its key, or hotwire it (screwdriver and copper wire).";
            if (v.Battery < 5f) return "The battery is flat. Replace it with a battery.";
            if (v.Engine < 10f) return "The engine is wrecked. Repair it with mechanical parts and a hammer.";
            if (v.Fuel <= 0.01f) return "The tank is empty. Pour in a fuel can.";
            return "";
        }

        public static OpResult StartEngine(VehicleState v, VehicleDefinition def, Inventory keys)
        {
            if (v.EngineOn) return OpResult.Success("The engine is already running.");
            string problem = StartProblem(v, def, keys);
            if (problem.Length > 0) return OpResult.Fail("cannot_start", problem);
            v.EngineOn = true;
            v.Battery = Math.Max(0f, v.Battery - 0.5f);
            return OpResult.Success(v.Hotwired && !HasKey(keys, v) && !v.KeyInIgnition ? "The hotwired engine coughs into life." : "The engine starts.");
        }

        public static OpResult StopEngine(VehicleState v)
        {
            if (!v.EngineOn) return OpResult.Success("The engine is off.");
            v.EngineOn = false;
            return OpResult.Success("Engine off.");
        }

        /// <summary>Chance (0–1) that one hotwiring attempt works: practice with electrics, a charged battery and a sound vehicle help.</summary>
        public static float HotwireChance(VehicleState v, VehicleDefinition def, bool knowsElectrics)
        {
            float p = 0.45f + (knowsElectrics ? 0.25f : 0f) + (v.Battery - 50f) / 400f + (v.Engine - 50f) / 500f - def.HotwireDifficulty * 0.35f;
            return Math.Max(0.1f, Math.Min(0.95f, p));
        }

        /// <summary>
        /// One abstract hotwiring attempt (the player has already spent <see cref="VehicleDefinition.HotwireSeconds"/>
        /// working under the dashboard). Needs a screwdriver; uses up a length of copper wire whether it
        /// works or not. Success lets the vehicle start without a key; failure sparks, sets the horn off
        /// (the caller makes the noise) and chips the ignition.
        /// </summary>
        public static OpResult Hotwire(VehicleState v, VehicleDefinition def, Inventory inv, bool knowsElectrics, DeterministicRandom rng, out bool loudFailure)
        {
            loudFailure = false;
            if (v.Hotwired || v.KeyInIgnition || HasKey(inv, v)) return OpResult.Fail("not_needed", "No need: it starts with its key.");
            if (v.Locked) return OpResult.Fail("locked", "Get inside first.");
            if (inv == null || inv.Count(HotwireTool) <= 0) return OpResult.Fail("no_tool", "You need a screwdriver to get at the ignition.");
            if (inv.Available(HotwireWire) <= 0) return OpResult.Fail("no_wire", "You need a length of copper wire.");
            if (v.Battery < 5f) return OpResult.Fail("flat", "Nothing happens: the battery is flat.");
            inv.TryRemove(HotwireWire, 1);
            float chance = HotwireChance(v, def, knowsElectrics);
            if (rng.NextFloat() < chance)
            {
                v.Hotwired = true;
                v.Known = true;
                return OpResult.Success($"You get the ignition to turn over without a key ({chance * 100f:0}% chance). It will start from now on.");
            }
            loudFailure = true;
            v.Engine = Math.Max(0f, v.Engine - 4f);
            v.Battery = Math.Max(0f, v.Battery - 6f);
            return OpResult.Fail("failed", $"Sparks — and the horn blares. It did not take ({chance * 100f:0}% chance). Try again with another length of wire.");
        }

        /// <summary>Burns fuel for <paramref name="metres"/> driven at <paramref name="throttle"/> (0–1) plus idling time. Stalls when empty.</summary>
        public static float BurnFuel(VehicleState v, VehicleDefinition def, float metres, float throttle, double seconds)
        {
            if (!v.EngineOn) return 0f;
            float load = 0.6f + 0.4f * Math.Max(0f, Math.Min(1f, throttle));
            float wear = 1f + (100f - v.Engine) / 200f; // a worn engine drinks more
            float litres = (float)(metres / 1000.0 * def.FuelPerKm * load * wear + seconds / 3600.0 * def.IdleFuelPerHour);
            litres = Math.Min(litres, v.Fuel);
            v.Fuel -= litres;
            v.OdometerMetres += metres;
            if (v.Fuel <= 0.0001f)
            {
                v.Fuel = 0f;
                v.EngineOn = false;
            }
            return litres;
        }

        public static OpResult Refuel(VehicleState v, VehicleDefinition def, Inventory from)
        {
            if (from == null || from.Available(FuelItem) <= 0) return OpResult.Fail("no_fuel", "You need a fuel can.");
            float room = def.FuelCapacity - v.Fuel;
            if (room < 1f) return OpResult.Fail("full", "The tank is full.");
            from.TryRemove(FuelItem, 1);
            float added = Math.Min(room, LitresPerCan);
            v.Fuel += added;
            return OpResult.Success($"Poured in {added:0} L ({v.Fuel:0}/{def.FuelCapacity:0} L).");
        }

        /// <summary>What repairing a component costs (a hammer is always needed).</summary>
        public static ItemAmount[] RepairCost(VehicleComponent c)
        {
            switch (c)
            {
                case VehicleComponent.Engine: return new[] { new ItemAmount("mechanical_parts", 2) };
                case VehicleComponent.Body: return new[] { new ItemAmount("scrap_metal", 4) };
                case VehicleComponent.Tyres: return new[] { new ItemAmount("mechanical_parts", 1), new ItemAmount("scrap_metal", 2) };
                default: return new[] { new ItemAmount("battery", 1) };
            }
        }

        public static float Condition(VehicleState v, VehicleComponent c)
        {
            switch (c)
            {
                case VehicleComponent.Engine: return v.Engine;
                case VehicleComponent.Body: return v.Body;
                case VehicleComponent.Tyres: return v.Tyres;
                default: return v.Battery;
            }
        }

        public static OpResult Repair(VehicleState v, VehicleComponent c, Inventory from, bool hasHammer)
        {
            float now = Condition(v, c);
            if (now >= 99.5f) return OpResult.Fail("fine", $"The {c.ToString().ToLowerInvariant()} needs no repair.");
            if (!hasHammer && c != VehicleComponent.Battery) return OpResult.Fail("tool", "Repairs need a hammer.");
            ItemAmount[] cost = RepairCost(c);
            if (from == null || !from.TryConsume(cost))
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (ItemAmount m in from?.Missing(cost) ?? new System.Collections.Generic.List<ItemAmount>(cost)) parts.Add($"{m.Quantity} {from?.Catalog.NameOf(m.ItemId) ?? m.ItemId}");
                return OpResult.Fail("materials", "Repair needs " + string.Join(", ", parts) + ".");
            }
            switch (c)
            {
                case VehicleComponent.Engine: v.Engine = Math.Min(100f, v.Engine + 40f); break;
                case VehicleComponent.Body: v.Body = Math.Min(100f, v.Body + 35f); break;
                case VehicleComponent.Tyres: v.Tyres = Math.Min(100f, v.Tyres + 50f); break;
                default: v.Battery = 100f; break;
            }
            return OpResult.Success($"Repaired the {c.ToString().ToLowerInvariant()} ({Condition(v, c):0}%).");
        }

        /// <summary>Damage from a hit at <paramref name="speed"/> m/s: the body takes most, the engine some once the body is weak.</summary>
        public static void Crash(VehicleState v, float speed)
        {
            if (speed < 4f) return;
            float dmg = (speed - 4f) * 2.2f;
            v.Body = Math.Max(0f, v.Body - dmg);
            if (v.Body < 40f) v.Engine = Math.Max(0f, v.Engine - dmg * 0.5f);
            if (v.Engine < 10f) v.EngineOn = false;
        }

        public static string Name(VehicleState v, VehicleDefinition def) => string.IsNullOrEmpty(v.LockId) ? "this " + def.Name.ToLowerInvariant() : v.LockId;

        /// <summary>One line of condition for prompts and the inspect window.</summary>
        public static string Summary(VehicleState v, VehicleDefinition def) =>
            $"fuel {v.Fuel:0}/{def.FuelCapacity:0} L · engine {v.Engine:0}% · body {v.Body:0}% · tyres {v.Tyres:0}% · battery {v.Battery:0}%";
    }
}
