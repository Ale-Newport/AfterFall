using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Vehicles;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>Options for a vehicle spawned from the creative panel.</summary>
    public sealed class VehicleSpawnOptions
    {
        public int Paint;
        public float FuelFraction = 1f;
        public float Condition = 100f;
        public bool Locked;
        public bool GiveKey = true;
    }

    /// <summary>
    /// Player commands for vehicles and the physical facts the presentation reports (where a moving
    /// vehicle is, how far it went, crashes). Fuel, condition, locks and keys follow
    /// <see cref="VehicleRules"/>; running engines are heard by the infected (<see cref="VehicleSystem"/>).
    /// </summary>
    public sealed class VehicleService
    {
        public const float EnterRange = 3.2f;
        public const float SmashNoise = 22f;
        public const float HornNoise = 32f;

        private readonly GameSession _s;
        private double _lastHitWarning = -1e9;

        public VehicleService(GameSession session)
        {
            _s = session;
        }

        private WorldState World => _s.World;
        private PlayerState Player => _s.Player;

        public VehicleState Get(EntityId id) => World.Vehicles.TryGetValue(id, out VehicleState v) ? v : null;
        public VehicleDefinition Def(VehicleState v) => v != null ? _s.Content.Vehicle(v.DefId) : null;

        /// <summary>The vehicle the survivor is driving, or null.</summary>
        public VehicleState Current => Player.InVehicle ? Get(Player.VehicleId) : null;

        /// <summary>Speed of the driven vehicle last reported by the physics (m/s).</summary>
        public float CurrentSpeed { get; private set; }

        private bool InReach(VehicleState v, VehicleDefinition def) =>
            Vec3.DistanceXZ(v.Position, Player.Position) <= EnterRange + Math.Max(def.SizeX, def.SizeZ) * 0.5f;

        private OpResult Check(EntityId id, out VehicleState v, out VehicleDefinition def, bool needReach = true)
        {
            v = Get(id);
            def = Def(v);
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", "You are dead.");
            if (v == null || def == null) return OpResult.Fail("missing", "There is no vehicle there.");
            if (needReach && Player.VehicleId != id && !InReach(v, def)) return OpResult.Fail("too_far", "Move closer to it.");
            return OpResult.Success();
        }

        // ------------------------------------------------------------ getting in and out

        public OpResult Enter(EntityId id)
        {
            OpResult r = Check(id, out VehicleState v, out VehicleDefinition def);
            if (!r.Ok) return r;
            if (Player.InVehicle) return OpResult.Fail("busy", "You are already in a vehicle.");
            r = VehicleRules.Enter(v, def, Player.Inventory);
            if (!r.Ok) return r;
            Player.VehicleId = v.Id;
            Player.Position = v.Position;
            CurrentSpeed = 0f;
            return r;
        }

        /// <summary>Gets out (only when nearly stopped); the engine is switched off.</summary>
        public OpResult Exit()
        {
            VehicleState v = Current;
            if (v == null) return OpResult.Fail("not_in", "You are not in a vehicle.");
            if (CurrentSpeed > 3f) return OpResult.Fail("moving", "Stop first.");
            bool wasOn = v.EngineOn;
            v.EngineOn = false;
            Player.VehicleId = EntityId.None;
            CurrentSpeed = 0f;
            return OpResult.Success(wasOn ? "You switch the engine off and get out." : "You get out.");
        }

        /// <summary>Out of the vehicle at once, whatever its speed (death, teleports, loading problems).</summary>
        public void ForceExit()
        {
            VehicleState v = Current;
            if (v != null) v.EngineOn = false;
            Player.VehicleId = EntityId.None;
            CurrentSpeed = 0f;
        }

        public OpResult SmashWindow(EntityId id)
        {
            OpResult r = Check(id, out VehicleState v, out VehicleDefinition def);
            if (!r.Ok) return r;
            r = VehicleRules.SmashWindow(v, def);
            if (r.Ok) _s.EmitNoise(v.Position, SmashNoise, "breaking glass");
            return r;
        }

        public OpResult SetLocked(EntityId id, bool locked)
        {
            OpResult r = Check(id, out VehicleState v, out VehicleDefinition def);
            return r.Ok ? VehicleRules.SetLocked(v, def, Player.Inventory, locked) : r;
        }

        // ------------------------------------------------------------ engine

        public OpResult ToggleEngine()
        {
            VehicleState v = Current;
            if (v == null) return OpResult.Fail("not_in", "Get in first.");
            if (v.EngineOn) return VehicleRules.StopEngine(v);
            OpResult r = VehicleRules.StartEngine(v, Def(v), Player.Inventory);
            if (r.Ok) _s.EmitNoise(v.Position, Def(v).EngineNoise, "engine");
            return r;
        }

        /// <summary>The hotwiring prompt's question: could the player attempt it now, and how likely is it to work?</summary>
        public bool CanTryHotwire(out string reason, out float chance)
        {
            chance = 0f;
            VehicleState v = Current;
            reason = "";
            if (v == null) return false;
            VehicleDefinition def = Def(v);
            if (v.Hotwired || v.KeyInIgnition || VehicleRules.HasKey(Player.Inventory, v)) return false;
            chance = VehicleRules.HotwireChance(v, def, _s.World.Tech.IsUnlocked("electricity"));
            if (Player.Inventory.Count(VehicleRules.HotwireTool) <= 0) reason = "needs a screwdriver";
            else if (Player.Inventory.Available(VehicleRules.HotwireWire) <= 0) reason = "needs copper wire";
            return true;
        }

        /// <summary>One hotwiring attempt after the player has worked at it for the vehicle's hotwiring time.</summary>
        public OpResult Hotwire()
        {
            VehicleState v = Current;
            if (v == null) return OpResult.Fail("not_in", "Get in first.");
            VehicleDefinition def = Def(v);
            _s.EmitNoise(v.Position, 8f, "tinkering");
            OpResult r = VehicleRules.Hotwire(v, def, Player.Inventory, World.Tech.IsUnlocked("electricity"), World.SessionRng, out bool loud);
            if (loud) _s.EmitNoise(v.Position, HornNoise, "car horn");
            return r;
        }

        // ------------------------------------------------------------ service

        public OpResult Refuel(EntityId id)
        {
            OpResult r = Check(id, out VehicleState v, out VehicleDefinition def);
            return r.Ok ? VehicleRules.Refuel(v, def, Player.Inventory) : r;
        }

        public OpResult Repair(EntityId id, VehicleComponent c)
        {
            OpResult r = Check(id, out VehicleState v, out _);
            if (!r.Ok) return r;
            if (v.EngineOn && c == VehicleComponent.Engine) return OpResult.Fail("running", "Switch the engine off first.");
            r = VehicleRules.Repair(v, c, Player.Inventory, _s.HasTool(ToolType.Hammer));
            if (r.Ok) _s.EmitNoise(v.Position, 9f, "repairs");
            return r;
        }

        /// <summary>Opens or closes all its doors (not while locked).</summary>
        public OpResult SetDoorsOpen(EntityId id, bool open)
        {
            OpResult r = Check(id, out VehicleState v, out VehicleDefinition def);
            if (!r.Ok) return r;
            if (def.Doors == 0) return OpResult.Fail("no_doors", $"A {def.Name.ToLowerInvariant()} has no doors.");
            if (v.Locked && open) return OpResult.Fail("locked", "It is locked.");
            v.DoorsOpen = open ? (1 << def.Doors) - 1 : 0;
            return OpResult.Success(open ? "Doors open." : "Doors closed.");
        }

        /// <summary>Marks a vehicle as the settlement's (it shows on the map as yours).</summary>
        public OpResult Claim(EntityId id, bool claim)
        {
            OpResult r = Check(id, out VehicleState v, out VehicleDefinition def);
            if (!r.Ok) return r;
            v.Claimed = claim;
            v.Known = true;
            return OpResult.Success(claim ? $"The {def.Name.ToLowerInvariant()} now belongs to your settlement." : "No longer marked as yours.");
        }

        public void MarkInspected(EntityId id)
        {
            VehicleState v = Get(id);
            if (v != null) v.Known = true;
        }

        // ------------------------------------------------------------ facts from the physics

        /// <summary>
        /// The driven or moving vehicle's transform and travel this frame: fuel burns with distance,
        /// throttle and time; the survivor rides along.
        /// </summary>
        public void ReportDriving(EntityId id, Vec3 position, float yaw, float metres, float throttle, double seconds, float speed)
        {
            VehicleState v = Get(id);
            VehicleDefinition def = Def(v);
            if (v == null || def == null) return;
            v.Position = position;
            v.Yaw = yaw;
            bool wasOn = v.EngineOn;
            VehicleRules.BurnFuel(v, def, metres, throttle, seconds);
            if (Player.VehicleId == id)
            {
                CurrentSpeed = speed;
                Player.Position = position;
                Player.Yaw = yaw;
            }
            if (wasOn && !v.EngineOn && v.Fuel <= 0f)
                _s.Events.Publish(new Notification("The engine coughs and dies: the tank is empty. Pour in a fuel can.", Severity.Warning));
        }

        /// <summary>A parked vehicle settled or was pushed: write its resting transform back.</summary>
        public void ReportResting(EntityId id, Vec3 position, float yaw)
        {
            VehicleState v = Get(id);
            if (v == null) return;
            v.Position = position;
            v.Yaw = yaw;
        }

        /// <summary>A collision at <paramref name="speed"/> m/s (relative): the vehicle is damaged, and so is the driver in a hard crash.</summary>
        public void ReportCrash(EntityId id, float speed)
        {
            VehicleState v = Get(id);
            if (v == null || speed < 4f) return;
            bool running = v.EngineOn;
            VehicleRules.Crash(v, speed);
            if (Player.VehicleId == id && speed > 14f) _s.DamagePlayer((speed - 14f) * 2.5f, "a crash");
            if (running && !v.EngineOn) _s.Events.Publish(new Notification("The engine dies in the crash.", Severity.Warning));
        }

        /// <summary>The vehicle ran into an infected at <paramref name="speed"/> m/s.</summary>
        public OpResult HitInfected(EntityId vehicleId, EntityId zombieId, float speed)
        {
            VehicleState v = Get(vehicleId);
            if (v == null || !World.Zombies.TryGetValue(zombieId, out ZombieState z) || z.IsDead || speed < 3f) return OpResult.Fail("none", "");
            // Being hit by a ton of steel: lethal from about 25 km/h.
            float damage = speed * speed * 1.8f;
            bool killed = _s.Zombies.Damage(zombieId, damage, v.Position);
            v.Body = Math.Max(0f, v.Body - speed * 0.25f);
            return OpResult.Success(killed ? "Run down." : "Hit.");
        }

        /// <summary>
        /// The infected attacking someone in a vehicle hit the vehicle instead while its body holds;
        /// a wrecked body lets them reach the driver.
        /// </summary>
        public bool AbsorbAttack(float damage)
        {
            VehicleState v = Current;
            if (v == null || v.Body <= 0f) return false;
            v.Body = Math.Max(0f, v.Body - damage * 0.6f);
            if (_s.Now - _lastHitWarning > 8.0)
            {
                _lastHitWarning = _s.Now;
                _s.Events.Publish(new Notification($"The infected are tearing at the {Def(v).Name.ToLowerInvariant()} (body {v.Body:0}%)!", Severity.Danger));
            }
            return true;
        }

        // ------------------------------------------------------------ creation

        /// <summary>Creates a vehicle in the world (seeding and the creative panel).</summary>
        public static VehicleState Create(WorldState world, Afterfall.Domain.Content.ContentDatabase content, VehicleDefinition def, Vec3 position, float yaw)
        {
            var v = new VehicleState { Id = world.Ids.Allocate(), DefId = def.Id, Position = position, Yaw = yaw, Fuel = def.FuelCapacity * 0.5f };
            v.Storage = new Inventory(world.Ids.Allocate(), content.Items, def.StorageSlots, def.StorageMaxWeight, null, def.Category == VehicleCategory.Atv ? def.Name + " rack" : def.Name + " trunk");
            return v;
        }
    }

    /// <summary>Running engines are noise the infected follow; a running engine with nobody reporting it idles its fuel away.</summary>
    public sealed class VehicleSystem : ISimulationSystem
    {
        private readonly GameSession _s;

        public VehicleSystem(GameSession session)
        {
            _s = session;
        }

        public string Name => "Vehicles";

        public void Tick(double dt)
        {
            foreach (VehicleState v in _s.World.Vehicles.Values)
            {
                if (!v.EngineOn) continue;
                VehicleDefinition def = _s.Content.Vehicle(v.DefId);
                if (def == null) continue;
                _s.EmitNoise(v.Position, def.EngineNoise, "engine");
                // Nobody is driving it (left running): it idles until the tank runs dry.
                if (_s.Player.VehicleId != v.Id) VehicleRules.BurnFuel(v, def, 0f, 0f, dt);
            }
        }
    }
}
