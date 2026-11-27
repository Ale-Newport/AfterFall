using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.Power;
using Afterfall.Domain.Survival;
using Afterfall.Domain.Technology;
using Afterfall.Domain.Threats;
using Afterfall.Domain.Weather;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>
    /// Application layer for one running game. Owns the world state and every simulation
    /// system, and exposes player commands. Presentation code calls these commands instead of
    /// mutating domain objects directly, so all rules (range, stamina, transactions) apply.
    /// </summary>
    public sealed class GameSession
    {
        public const float InteractRange = 3.6f;
        public const float StationRange = 5f;
        public const float ContainerAccessRange = 4.5f;
        public const float RevealRadius = 48f;

        private readonly PlayerSystem _playerSystem;
        private double _lastMoveNoiseAt = -1;

        public GameSession(ContentDatabase content, WorldState world)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (World.Layout == null) World.Layout = WorldGenerator.Generate(World.Seed);
            Events = new EventBus();
            Scheduler = new TickScheduler();
            Rng = World.SessionRng;

            Tech = new TechnologyService(Content.Techs, World.Tech, Events);
            Construction = new ConstructionService(World, Content, Events, Tech);
            Power = new PowerSystem(World, Content, Events);
            Machines = new MachineSystem(World, Content, Events, Tech);
            Zombies = new ZombieSystem(World, Content, Events);
            Threats = new ThreatDirector(World, Content, Events, Zombies);
            Colony = new ColonySystem(World, Content, Events, Construction);
            Zombies.ColonistHit += (id, dmg) => Colony.DamageColonist(id, dmg, "the infected");
            Farming = new Afterfall.Domain.Farming.FarmingSystem(World, Content, Events, Tech);
            Colony.Farming = Farming;
            Farming.PumpHasPower = s =>
            {
                PowerNetworkInfo net = Power.NetworkOf(s.Id);
                return net != null && (net.RunningGenerators > 0 || net.StoredJoules > 0);
            };
            if (Colony.Movement is DirectColonistMovement walk) walk.GroundHeight = p => World.Layout.HeightAt(p.X, p.Z);
            Weather = new WeatherSystem(World, Content, Events);
            Weather.EmitNoise = (p, r, src) => Zombies.Noise.Emit(p, r, Now, src);
            Colony.EmitNoise = (p, r, src) => Zombies.Noise.Emit(p, r, Now, src);
            Heaters = new HeaterSystem(World, Content);
            Zombies.SightFactor = () => WeatherReadout.Of(World).SightFactor;
            Zombies.HearingFactor = () => WeatherReadout.Of(World).HearingFactor;
            _playerSystem = new PlayerSystem(this);
            Creative = new CreativeTools(this);
            Vehicles = new VehicleService(this);

            Zombies.PlayerHit += (z, dmg) =>
            {
                // Inside a vehicle the infected tear at the vehicle until its body gives way.
                if (!Vehicles.AbsorbAttack(dmg)) DamagePlayer(dmg, "the infected");
            };
            Zombies.Construction = Construction;
            if (Zombies.Adapter is DirectZombieAdapter direct) direct.IsBlocked = p => Zombies.IsInsideBlocker(p, 0.35f);

            // Order matters within a tick: power is balanced before machines consume it.
            Scheduler.Register(_playerSystem, 10);
            Scheduler.Register(Power, 4);
            Scheduler.Register(Machines, 4);
            Scheduler.Register(Colony, 4);
            Scheduler.Register(Farming, 1);
            Scheduler.Register(Zombies, 10);
            Scheduler.Register(Threats, 0.5);
            Scheduler.Register(Weather, 1);
            Scheduler.Register(Heaters, 2);
            Scheduler.Register(new VehicleSystem(this), 1);
            Scheduler.Register(new ResourceRespawnSystem(World, Content), 0.2);
            Scheduler.RestorePhases(World.TickPhases);
        }

        public ContentDatabase Content { get; }
        public WorldState World { get; }
        public EventBus Events { get; }
        public TickScheduler Scheduler { get; }
        public DeterministicRandom Rng { get; }
        public TechnologyService Tech { get; }
        public ConstructionService Construction { get; }
        public PowerSystem Power { get; }
        public MachineSystem Machines { get; }
        public ZombieSystem Zombies { get; }
        public ThreatDirector Threats { get; }
        public ColonySystem Colony { get; }
        public Afterfall.Domain.Farming.FarmingSystem Farming { get; }
        public WeatherSystem Weather { get; }
        public HeaterSystem Heaters { get; }

        /// <summary>Creative and developer commands (item spawning, teleports, modes).</summary>
        public CreativeTools Creative { get; }

        /// <summary>Vehicle commands and the physics facts about vehicles.</summary>
        public VehicleService Vehicles { get; }

        /// <summary>The weather right now (derived from the saved state and the clock).</summary>
        public WeatherReadout WeatherNow => WeatherReadout.Of(World);

        /// <summary>Temperature, shelter and heat at a spot (the player's by default).</summary>
        public ClimateReading ClimateAt(Vec3 position) => Climate.At(World, Content, position, WeatherNow);

        public PlayerState Player => World.Player;
        public double Now => World.Clock.ElapsedSeconds;

        /// <summary>Pauses simulation (menus, optional tactical pause).</summary>
        private const string DeadReason = "You are dead. Wake up or load a save first.";

        public bool Paused { get; set; }

        /// <summary>Set when a permadeath run ends.</summary>
        public bool GameOver { get; private set; }

        public void Update(double dt)
        {
            if (Paused || dt <= 0) return;
            dt = Math.Min(dt, 0.25); // a long frame hitch must not fast-forward the world
            World.Clock.Advance(dt);
            Scheduler.Advance(dt);
            Scheduler.StorePhases(World.TickPhases);
        }

        // ================================================================== player state from presentation

        /// <summary>Called every frame by the character controller with the authoritative physical transform.</summary>
        public void ReportPlayerTransform(Vec3 position, float yaw, PlayerActivity activity)
        {
            if (Player == null) return;
            Player.Position = position;
            Player.Yaw = yaw;
            Player.Activity = activity;
            if (Player.Vitals.IsDead || !activity.Moving || Now - _lastMoveNoiseAt < 0.3) return;
            _lastMoveNoiseAt = Now;
            float radius = activity.Crouching ? 0f : activity.Sprinting ? 13f : 4.5f;
            if (radius > 0f) Zombies.Noise.Emit(position, radius, Now, "footsteps");
        }

        public void EmitNoise(Vec3 position, float radius, string source) => Zombies.Noise.Emit(position, radius, Now, source);

        public float CarriedWeight => Player.Inventory.TotalWeight;
        public float SpeedMultiplier => SurvivalRules.EncumbranceSpeedFactor(CarriedWeight, PlayerState.CarryComfortKg, PlayerState.CarryMaxKg);

        public bool CanSprint => !Player.Vitals.IsDead && Player.Vitals.Stamina > 1f;

        // ================================================================== items

        public ItemDefinition EquippedItem
        {
            get
            {
                ItemStack st = Player.EquippedStack;
                return st.IsEmpty ? null : Content.Items.Get(st.ItemId);
            }
        }

        public void SelectHotbar(int index)
        {
            if (index < 0 || index >= PlayerState.HotbarSize) return;
            Player.SelectedHotbar = index;
        }

        public OpResult UseItem(int slot)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", "You are dead.");
            ItemStack st = Player.Inventory.GetSlot(slot);
            if (st.IsEmpty) return OpResult.Fail("empty", "Nothing there.");
            ItemDefinition def = Content.Items.Get(st.ItemId);
            if (def.Consumable == null)
            {
                if (def.Tool != null && slot >= PlayerState.HotbarSize)
                {
                    int free = FirstFreeHotbarSlot();
                    if (free < 0) return OpResult.Fail("hotbar_full", "Hotbar is full.");
                    Player.Inventory.MoveSlot(slot, free);
                    Player.SelectedHotbar = free;
                    return OpResult.Success($"Equipped {def.Name}.");
                }
                if (def.Tool != null && slot < PlayerState.HotbarSize)
                {
                    Player.SelectedHotbar = slot;
                    return OpResult.Success($"Equipped {def.Name}.");
                }
                return OpResult.Fail("not_usable", $"{def.Name} cannot be used directly.");
            }
            if (Player.Inventory.TakeFromSlot(slot, 1).IsEmpty) return OpResult.Fail("reserved", "Item is reserved.");
            SurvivalRules.Apply(Player.Vitals, def.Consumable);
            if (!string.IsNullOrEmpty(def.Consumable.Returns))
                GiveToPlayerOrDrop(new ItemAmount(def.Consumable.Returns, 1), "Dropped items");
            Events.Publish(new Notification($"Used {def.Name}.", Severity.Info));
            return OpResult.Success($"Used {def.Name}.");
        }

        public OpResult DropFromSlot(int slot, int quantity)
        {
            ItemStack taken = Player.Inventory.TakeFromSlot(slot, quantity);
            if (taken.IsEmpty) return OpResult.Fail("empty", "Nothing to drop.");
            Vec3 pos = InFrontOfPlayer(0.9f);
            Construction.CreateDrop(new List<ItemStack> { taken }, pos, Content.Items.NameOf(taken.ItemId), false);
            return OpResult.Success($"Dropped {taken.Quantity} {Content.Items.NameOf(taken.ItemId)}.");
        }

        public OpResult PickUpDrop(EntityId dropId)
        {
            if (!World.Drops.TryGetValue(dropId, out WorldDrop drop)) return OpResult.Fail("missing", "It is gone.");
            if (Vec3.Distance(drop.Position, Player.Position) > InteractRange) return OpResult.Fail("too_far", "Too far away.");
            int before = CountUnits(drop.Contents);
            InventoryTransfers.TransferAll(drop.Contents, Player.Inventory);
            int moved = before - CountUnits(drop.Contents);
            if (drop.Contents.IsEmpty) World.RemoveDrop(drop.Id);
            if (moved == 0) return OpResult.Fail("inventory_full", "Inventory full.");
            return drop.Contents.IsEmpty ? OpResult.Success($"Picked up {moved} item(s).") : OpResult.Success($"Picked up {moved} item(s); some did not fit.");
        }

        public OpResult DrinkFromWater()
        {
            if (World.Layout == null) return OpResult.Fail("no_water", "No water here.");
            Vec3 p = Player.Position;
            bool near = false;
            for (int i = 0; i < 8 && !near; i++)
            {
                double a = i * Math.PI / 4;
                near = World.Layout.IsWater(p.X + (float)Math.Cos(a) * 2.5f, p.Z + (float)Math.Sin(a) * 2.5f);
            }
            if (!near) return OpResult.Fail("no_water", "Move closer to the water's edge.");
            Player.Vitals.Thirst = Math.Min(100f, Player.Vitals.Thirst + 20f);
            return OpResult.Success("You drink from the lake.");
        }

        // ================================================================== gathering & combat

        public OpResult Harvest(EntityId nodeId, List<ItemAmount> gained = null)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", "You are dead.");
            if (!World.ResourceNodes.TryGetValue(nodeId, out ResourceNodeState node)) return OpResult.Fail("missing", "Nothing to harvest.");
            if (Vec3.DistanceXZ(node.Position, Player.Position) > InteractRange + node.Scale) return OpResult.Fail("too_far", "Too far away.");
            if (Now < Player.NextActionTime) return OpResult.Fail("busy", "");
            ResourceNodeDefinition def = Content.Node(node.DefId);
            ItemDefinition tool = EquippedItem;
            if (tool?.Tool == null) tool = null;
            const float staminaCost = 4f;
            if (Player.Vitals.Stamina < staminaCost) return OpResult.Fail("exhausted", "Too exhausted — catch your breath.");

            var got = gained ?? new List<ItemAmount>();
            OpResult r = HarvestRules.Harvest(World, Content, node, tool, Player.Inventory, Rng, got);
            if (!r.Ok) return r;
            SurvivalRules.TrySpendStamina(Player.Vitals, staminaCost);
            Player.NextActionTime = Now + HarvestRules.ActionSeconds(def, tool);
            if (tool != null) WearEquipped(tool.Tool.WearPerUse);
            EmitNoise(node.Position, def.NoiseRadius, "harvest");
            var parts = new List<string>();
            foreach (ItemAmount a in got)
            {
                parts.Add($"+{a.Quantity} {Content.Items.NameOf(a.ItemId)}");
                Events.Publish(new ItemsGathered(node.Id, a.ItemId, a.Quantity));
            }
            if (node.Depleted) parts.Add($"({def.Name} exhausted)");
            return OpResult.Success(parts.Count > 0 ? string.Join("  ", parts) : "Nothing useful this time.");
        }

        public float AttackRange => EquippedItem?.Tool?.Range ?? 1.6f;

        /// <summary>
        /// Melee attack. The presentation determines candidate targets with a physics overlap;
        /// the domain validates range, stamina and cooldown and applies damage.
        /// </summary>
        public OpResult MeleeAttack(IReadOnlyList<EntityId> zombieTargets)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            if (Now < Player.NextActionTime) return OpResult.Fail("busy", "");
            ItemDefinition weapon = EquippedItem;
            ToolStats stats = weapon?.Tool;
            float damage = stats?.Damage ?? 7f;
            float cost = stats?.StaminaCost ?? 6f;
            float cooldown = stats?.Cooldown ?? 0.6f;
            float range = stats?.Range ?? 1.6f;
            if (!SurvivalRules.TrySpendStamina(Player.Vitals, cost)) return OpResult.Fail("exhausted", "Too exhausted to swing.");
            Player.NextActionTime = Now + cooldown;
            EmitNoise(Player.Position, 9f, "combat");
            int hits = 0, kills = 0;
            if (zombieTargets != null)
            {
                foreach (EntityId id in zombieTargets)
                {
                    if (!World.Zombies.TryGetValue(id, out ZombieState z) || z.IsDead) continue;
                    if (Vec3.Distance(z.Position, Player.Position) > range + 1.0f) continue;
                    hits++;
                    if (Zombies.Damage(id, damage, Player.Position)) kills++;
                }
            }
            if (hits > 0 && weapon?.Tool != null) WearEquipped(weapon.Tool.WearPerUse);
            return OpResult.Success(kills > 0 ? "Killed!" : hits > 0 ? "Hit" : "Miss");
        }

        public void DamagePlayer(float amount, string source)
        {
            if (Player == null || Player.Vitals.IsDead) return;
            if (World.Settings.IsCreative() && World.Settings.CreativeInvulnerable) return;
            Events.Publish(new PlayerDamaged(amount, source));
            if (SurvivalRules.Damage(Player.Vitals, amount)) HandleDeath(source);
        }

        /// <summary>Set once a death has been processed (drop, counters, events) until the survivor respawns.</summary>
        private bool _deathHandled;

        /// <summary>Drops the belongings and announces the death; safe to call again for the same death.</summary>
        internal void HandleDeath(string cause)
        {
            if (_deathHandled) return;
            _deathHandled = true;
            PlayerState p = Player;
            var stacks = new List<ItemStack>();
            for (int i = 0; i < p.Inventory.SlotCount; i++)
            {
                ItemStack st = p.Inventory.GetSlot(i);
                if (!st.IsEmpty) stacks.Add(st);
            }
            // Refund in-progress crafting so nothing is destroyed by dying.
            foreach (CraftJob job in p.Crafting.Jobs)
            {
                RecipeDefinition r = Content.Recipe(job.RecipeId);
                if (r == null) continue;
                foreach (ItemAmount a in r.Inputs) stacks.AddRange(SplitStacks(a));
            }
            p.Crafting.Jobs.Clear();
            p.Inventory.ClearAll();
            EntityId dropId = EntityId.None;
            if (stacks.Count > 0) dropId = Construction.CreateDrop(stacks, p.Position, "Your belongings", true).Id;
            p.Deaths++;
            World.Metrics.Deaths++;
            Events.Publish(new PlayerDied(p.Position, cause, dropId));
            Events.Publish(new Notification(World.Settings.Permadeath ? "You died. This was a hardcore run." : "You died. Your belongings remain where you fell.", Severity.Danger));
            if (World.Settings.Permadeath) GameOver = true;
        }

        public OpResult Respawn()
        {
            if (!Player.Vitals.IsDead) return OpResult.Fail("alive", "You are alive.");
            if (GameOver) return OpResult.Fail("game_over", "Hardcore run ended.");
            Vec3 spawn = Player.WorldSpawn;
            if (Player.SpawnStructureId.IsValid && World.Structures.TryGetValue(Player.SpawnStructureId, out Structure bed) && bed.IsComplete)
                spawn = bed.Position + new Vec3(1.2f, 0.2f, 0f);
            Player.Position = spawn;
            Player.Vitals.ResetForRespawn();
            _deathHandled = false;
            Events.Publish(new PlayerRespawned(spawn));
            return OpResult.Success("You wake up, sore but alive.");
        }

        // ================================================================== crafting

        public HashSet<string> StationsInRange()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Structure s in World.Structures.Values)
            {
                if (!s.IsComplete) continue;
                BuildingDefinition def = Content.Building(s.DefId);
                if (def == null || string.IsNullOrEmpty(def.CraftingStation)) continue;
                // A fire is a cooking station only while it burns.
                if (s.Heater != null && !s.Heater.Lit) continue;
                if (Vec3.Distance(s.Position, Player.Position) <= StationRange) set.Add(def.CraftingStation);
            }
            return set;
        }

        public CraftCheck CheckRecipe(string recipeId)
        {
            RecipeDefinition r = Content.Recipe(recipeId);
            return r == null ? new CraftCheck { Reason = "Unknown recipe" } : CraftingRules.Check(r, Player.Inventory, World.Tech, StationsInRange(), Tech);
        }

        public OpResult Craft(string recipeId)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            RecipeDefinition r = Content.Recipe(recipeId);
            if (r == null) return OpResult.Fail("unknown", "Unknown recipe.");
            return Player.Crafting.Enqueue(r, Player.Inventory, World.Tech, StationsInRange(), Tech);
        }

        public OpResult CancelCraft(int index) => Player.Crafting.Cancel(index, Player.Inventory, Content.Recipes);

        /// <summary>Hand/station recipes the player can see in the crafting menu.</summary>
        public List<RecipeDefinition> PlayerRecipes()
        {
            var stations = new HashSet<string>(StringComparer.Ordinal) { "" };
            foreach (BuildingDefinition b in Content.Buildings.Values)
                if (!string.IsNullOrEmpty(b.CraftingStation)) stations.Add(b.CraftingStation);
            var list = new List<RecipeDefinition>();
            foreach (RecipeDefinition r in Content.Recipes.Values)
                if (stations.Contains(r.Station ?? "")) list.Add(r);
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Station ?? "", b.Station ?? "");
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            return list;
        }

        // ================================================================== construction

        public PlacementResult EvaluatePlacement(string defId, PlacementProbe probe)
        {
            BuildingDefinition def = Content.Building(defId);
            if (def == null)
            {
                var bad = new PlacementResult();
                bad.Reasons.Add("Unknown building.");
                return bad;
            }
            return PlacementRules.Evaluate(def, probe, World.Tech, Tech, Player.Inventory, World.Settings.IsCreative());
        }

        public OpResult PlaceBlueprint(string defId, Vec3 position, float yaw, PlacementProbe probe, out Structure structure)
        {
            structure = null;
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            return Construction.PlaceBlueprint(defId, position, yaw, probe, Player.Inventory, out structure);
        }

        /// <summary>Delivers carried materials and performs <paramref name="seconds"/> of construction work.</summary>
        public OpResult BuildStep(EntityId structureId, float seconds)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            if (!World.Structures.TryGetValue(structureId, out Structure s)) return OpResult.Fail("missing", "Nothing to build.");
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + FootprintRadius(s)) return OpResult.Fail("too_far", "Move closer to build.");
            bool hammer = HasTool(ToolType.Hammer);
            OpResult r = Construction.Work(s, seconds * (hammer ? 2f : 1f), Player.Inventory);
            if (r.Ok) EmitNoise(s.Position, 7f, "construction");
            return r;
        }

        public OpResult Demolish(EntityId structureId)
        {
            if (!World.Structures.TryGetValue(structureId, out Structure s)) return OpResult.Fail("missing", "Nothing there.");
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + FootprintRadius(s)) return OpResult.Fail("too_far", "Move closer.");
            return Construction.Demolish(s, Player.Inventory);
        }

        public OpResult Repair(EntityId structureId)
        {
            if (!World.Structures.TryGetValue(structureId, out Structure s)) return OpResult.Fail("missing", "Nothing there.");
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + FootprintRadius(s)) return OpResult.Fail("too_far", "Move closer.");
            if (!HasTool(ToolType.Hammer)) return OpResult.Fail("tool_required", "Repairing requires a hammer.");
            return Construction.Repair(s, Player.Inventory);
        }

        // ================================================================== map

        /// <summary>Sets the single active waypoint the minimap and HUD point to.</summary>
        public OpResult SetWaypoint(Vec3 position, string label)
        {
            if (!World.Layout.IsInsideMap(position.X, position.Z)) return OpResult.Fail("outside", "That is outside the map.");
            Vec3 ground = World.Layout.Ground(position.X, position.Z);
            World.Navigation.SetWaypoint(ground, label);
            return OpResult.Success($"Waypoint set: {World.Navigation.WaypointLabel}, {MapGeometry.Distance(Vec3.DistanceXZ(Player.Position, ground))} away.");
        }

        public OpResult ClearWaypoint()
        {
            if (!World.Navigation.HasWaypoint) return OpResult.Fail("none", "No waypoint is set.");
            World.Navigation.ClearWaypoint();
            return OpResult.Success("Waypoint removed.");
        }

        public OpResult AddMapMarker(string name, Vec3 position)
        {
            if (!World.Layout.IsInsideMap(position.X, position.Z)) return OpResult.Fail("outside", "That is outside the map.");
            return World.Navigation.AddMarker(World.Ids.Allocate(), name, World.Layout.Ground(position.X, position.Z), MarkerKind.Custom, out _);
        }

        public OpResult RemoveMapMarker(EntityId id)
        {
            return World.Navigation.RemoveMarker(id) ? OpResult.Success("Marker removed.") : OpResult.Fail("missing", "No such marker.");
        }

        public OpResult SetSpawnPoint(EntityId bedId)
        {
            if (!World.Structures.TryGetValue(bedId, out Structure s) || !s.IsComplete || Content.Building(s.DefId)?.IsBed != true)
                return OpResult.Fail("not_bed", "That is not a finished bed.");
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + 1f) return OpResult.Fail("too_far", "Move closer.");
            Player.SpawnStructureId = s.Id;
            return OpResult.Success("Respawn point set. You will wake up here.");
        }

        /// <summary>Opens or closes a door within reach (a locked one opens only with its key, which unlocks it).</summary>
        public OpResult ToggleDoor(EntityId id)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            if (!World.Structures.TryGetValue(id, out Structure s)) return OpResult.Fail("missing", "Nothing there.");
            BuildingDefinition def = Content.Building(s.DefId);
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + 1f) return OpResult.Fail("too_far", "Move closer.");
            OpResult r = DoorRules.Toggle(s, def, Player.Inventory);
            if (r.Ok) EmitNoise(s.Position, 4f, "door");
            return r;
        }

        /// <summary>Locks or unlocks a door within reach with its key.</summary>
        public OpResult SetDoorLocked(EntityId id, bool locked)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            if (!World.Structures.TryGetValue(id, out Structure s)) return OpResult.Fail("missing", "Nothing there.");
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + 1f) return OpResult.Fail("too_far", "Move closer.");
            return DoorRules.SetLocked(s, Content.Building(s.DefId), Player.Inventory, locked);
        }

        /// <summary>A colonist opens an unlocked door in their way (presentation calls this when one blocks their path).</summary>
        public OpResult OpenDoorForColonist(EntityId colonistId, EntityId doorId)
        {
            if (!World.Colonists.TryGetValue(colonistId, out var c) || !World.Structures.TryGetValue(doorId, out Structure s)) return OpResult.Fail("missing", "");
            if (Vec3.DistanceXZ(c.Position, s.Position) > 3.5f) return OpResult.Fail("too_far", "");
            return DoorRules.OpenForColonist(s, Content.Building(s.DefId));
        }

        /// <summary>Closes a door that a colonist opened once nobody is passing (presentation decides when).</summary>
        public void CloseDoorBehind(EntityId doorId)
        {
            if (!World.Structures.TryGetValue(doorId, out Structure s)) return;
            BuildingDefinition def = Content.Building(s.DefId);
            if (!DoorRules.IsDoor(s, def) || !s.DoorOpen || DoorRules.IsBroken(s, def)) return;
            s.DoorOpen = false;
        }

        // ================================================================== inventories & machines

        /// <summary>Whether the player is physically close enough to access an inventory.</summary>
        public bool CanAccess(EntityId inventoryId, out string reason)
        {
            reason = "";
            Inventory inv = World.FindInventory(inventoryId);
            if (inv == null)
            {
                reason = "That container no longer exists.";
                return false;
            }
            if (inv == Player.Inventory) return true;
            EntityId owner = World.OwnerOf(inventoryId);
            Vec3 pos;
            float extra = 0f;
            if (World.Structures.TryGetValue(owner, out Structure s))
            {
                pos = s.Position;
                extra = FootprintRadius(s);
            }
            else if (World.Drops.TryGetValue(owner, out WorldDrop d)) pos = d.Position;
            else if (World.Vehicles.TryGetValue(owner, out Afterfall.Domain.Vehicles.VehicleState veh))
            {
                pos = veh.Position;
                var vdef = Content.Vehicle(veh.DefId);
                extra = vdef != null ? Math.Max(vdef.SizeX, vdef.SizeZ) * 0.5f : 2f;
                if (veh.Locked && Player.VehicleId != veh.Id)
                {
                    reason = "It is locked.";
                    return false;
                }
            }
            else
            {
                reason = "Unknown container.";
                return false;
            }
            if (Vec3.Distance(pos, Player.Position) > ContainerAccessRange + extra)
            {
                reason = "Too far away — walk to it first.";
                return false;
            }
            return true;
        }

        public OpResult Transfer(EntityId fromInventory, int slot, EntityId toInventory, int quantity)
        {
            if (Player.Vitals.IsDead) return OpResult.Fail("dead", DeadReason);
            if (!CanAccess(fromInventory, out string r1)) return OpResult.Fail("no_access", r1);
            if (!CanAccess(toInventory, out string r2)) return OpResult.Fail("no_access", r2);
            Inventory from = World.FindInventory(fromInventory);
            Inventory to = World.FindInventory(toInventory);
            if (IsMachineOutput(toInventory)) return OpResult.Fail("output_only", "Output slots only hold finished products.");
            ItemStack st = from.GetSlot(slot);
            OpResult r = InventoryTransfers.TryTransferSlot(from, slot, to, quantity);
            if (r.Ok)
            {
                MarkSearched(fromInventory);
                Events.Publish(new ItemTransferred(from.Id, to.Id, st.ItemId, Math.Min(quantity, st.Quantity)));
                EntityId owner = World.OwnerOf(fromInventory);
                if (World.Drops.ContainsKey(owner) && from.IsEmpty) World.RemoveDrop(owner);
            }
            return r;
        }

        public OpResult TakeAll(EntityId fromInventory)
        {
            if (!CanAccess(fromInventory, out string reason)) return OpResult.Fail("no_access", reason);
            Inventory from = World.FindInventory(fromInventory);
            int moved = InventoryTransfers.TransferAll(from, Player.Inventory);
            MarkSearched(fromInventory);
            EntityId owner = World.OwnerOf(fromInventory);
            if (World.Drops.ContainsKey(owner) && from.IsEmpty) World.RemoveDrop(owner);
            if (moved == 0) return OpResult.Fail(from.IsEmpty ? "empty" : "inventory_full", from.IsEmpty ? "Nothing to take." : "Inventory full.");
            return OpResult.Success($"Took {moved} item(s).");
        }

        public void MarkSearched(EntityId inventoryId)
        {
            if (World.Structures.TryGetValue(World.OwnerOf(inventoryId), out Structure s)) s.Searched = true;
        }

        public bool IsMachineOutput(EntityId inventoryId)
        {
            if (!World.Structures.TryGetValue(World.OwnerOf(inventoryId), out Structure s)) return false;
            return s.Machine != null && s.Machine.Output.Id == inventoryId;
        }

        public OpResult SetMachineRecipe(EntityId structureId, string recipeId)
        {
            if (!World.Structures.TryGetValue(structureId, out Structure s)) return OpResult.Fail("missing", "Machine not found.");
            return Machines.SetRecipe(s, recipeId);
        }

        public OpResult ToggleEnabled(EntityId structureId)
        {
            if (!World.Structures.TryGetValue(structureId, out Structure s) || !s.PlayerBuilt) return OpResult.Fail("missing", "Not found.");
            s.Enabled = !s.Enabled;
            return OpResult.Success(s.Enabled ? "Switched on." : "Switched off.");
        }

        public OpResult ConnectPower(EntityId a, EntityId b)
        {
            World.Structures.TryGetValue(a, out Structure sa);
            World.Structures.TryGetValue(b, out Structure sb);
            return PowerLinker.Link(World, Content, sa, sb);
        }

        public OpResult DisconnectPower(EntityId a, EntityId b) => PowerLinker.Unlink(World, a, b);

        // ================================================================== farming

        private OpResult WithPlot(EntityId id, System.Func<Structure, OpResult> action)
        {
            if (!World.Structures.TryGetValue(id, out Structure s)) return OpResult.Fail("missing", "Nothing there.");
            if (Vec3.Distance(s.Position, Player.Position) > InteractRange + FootprintRadius(s)) return OpResult.Fail("too_far", "Move closer.");
            return action(s);
        }

        public OpResult PlantCrop(EntityId plot) => WithPlot(plot, s => Farming.Plant(s, Player.Inventory));
        public OpResult WaterCrop(EntityId plot) => WithPlot(plot, s => Farming.Water(s, Player.Inventory));
        public OpResult HarvestCrop(EntityId plot) => WithPlot(plot, s => Farming.Harvest(s, Player.Inventory));

        // ================================================================== colony

        public OpResult Recruit(EntityId colonistId) => Colony.Recruit(colonistId, Player.Position);

        public OpResult SetWorkPriority(EntityId colonistId, JobType type, int priority)
        {
            if (!World.Colonists.TryGetValue(colonistId, out ColonistState c) || !c.Recruited) return OpResult.Fail("missing", "No such colonist.");
            if (type == JobType.Eat || type == JobType.Rest || type == JobType.WarmUp) return OpResult.Fail("personal", "Eating, resting and warming up are personal needs.");
            c.Work.Priorities[type] = Math.Max(0, Math.Min(4, priority));
            return OpResult.Success(priority == 0 ? $"{c.Name} will no longer {type.ToString().ToLowerInvariant()}." : $"{c.Name}: {type} priority {priority}.");
        }

        public const int MaxWorkZones = 6;

        /// <summary>
        /// Marks a gathering area centred on an explored spot. Colonists with Gather work cut, mine and pick
        /// what the zone allows there; the order is carried out by people walking out with real tools.
        /// </summary>
        public OpResult AddWorkZone(Vec3 center, float radius, out WorkZone zone)
        {
            zone = null;
            if (World.WorkZones.Count >= MaxWorkZones) return OpResult.Fail("too_many", $"At most {MaxWorkZones} gathering areas. Remove one first.");
            if (center.X < 0 || center.Z < 0 || center.X > WorldLayout.WorldSize || center.Z > WorldLayout.WorldSize) return OpResult.Fail("outside", "Outside the valley.");
            if (!World.Exploration.IsRevealed(center.X, center.Z)) return OpResult.Fail("unexplored", "You have not explored there yet.");
            if (World.Layout != null && World.Layout.IsWater(center.X, center.Z)) return OpResult.Fail("water", "That is open water.");
            zone = new WorkZone
            {
                Id = World.Ids.Allocate(), Center = center,
                Radius = Math.Max(WorkZone.MinRadius, Math.Min(WorkZone.MaxRadius, radius))
            };
            World.WorkZones[zone.Id] = zone;
            return OpResult.Success($"Gathering area marked ({zone.Radius:0} m). Colonists with Gather work will collect {GatherRules.Describe(zone.Gather)} there.");
        }

        public OpResult SetZoneGather(EntityId zoneId, GatherKinds kind, bool allowed)
        {
            if (!World.WorkZones.TryGetValue(zoneId, out WorkZone z)) return OpResult.Fail("missing", "No such gathering area.");
            z.Gather = allowed ? z.Gather | kind : z.Gather & ~kind;
            return OpResult.Success($"Gathering area: {GatherRules.Describe(z.Gather)}.");
        }

        /// <summary>Removes a gathering area. Work not yet started there is cancelled; colonists already out finish their trip.</summary>
        public OpResult RemoveWorkZone(EntityId zoneId)
        {
            if (!World.WorkZones.TryGetValue(zoneId, out WorkZone z)) return OpResult.Fail("missing", "No such gathering area.");
            World.WorkZones.Remove(zoneId);
            var cancel = new List<EntityId>();
            foreach (Job j in World.Jobs.Values)
            {
                if (j.Type != JobType.Gather || j.State != JobState.Open || !World.ResourceNodes.TryGetValue(j.TargetNode, out ResourceNodeState n) || !z.Contains(n.Position)) continue;
                bool elsewhere = false;
                foreach (WorkZone other in World.WorkZones.Values) elsewhere |= other.Contains(n.Position);
                if (!elsewhere) cancel.Add(j.Id);
            }
            foreach (EntityId id in cancel) World.Jobs.Remove(id);
            return OpResult.Success("Gathering area removed.");
        }

        public int RecruitedCount
        {
            get
            {
                int n = 0;
                foreach (ColonistState c in World.Colonists.Values)
                    if (c.Recruited && c.Activity != ColonistActivity.Down) n++;
                return n;
            }
        }

        // ================================================================== overview (strategic UI)

        /// <summary>Totals across the player's backpack and all player-built storage.</summary>
        public Dictionary<string, int> ResourceOverview()
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(Inventory inv)
            {
                if (inv == null) return;
                foreach (KeyValuePair<string, int> kv in inv.Totals())
                {
                    totals.TryGetValue(kv.Key, out int c);
                    totals[kv.Key] = c + kv.Value;
                }
            }
            Add(Player.Inventory);
            foreach (Structure s in World.Structures.Values)
                if (s.PlayerBuilt && s.IsComplete) Add(s.Storage);
            foreach (ColonistState c in World.Colonists.Values)
                if (c.Recruited) Add(c.Carry);
            return totals;
        }

        // ================================================================== helpers

        public bool HasTool(ToolType type)
        {
            ItemDefinition eq = EquippedItem;
            if (eq?.Tool != null && eq.Tool.Type == type) return true;
            for (int i = 0; i < Player.Inventory.SlotCount; i++)
            {
                ItemStack st = Player.Inventory.GetSlot(i);
                if (st.IsEmpty) continue;
                ItemDefinition d = Content.Items.Get(st.ItemId);
                if (d.Tool != null && d.Tool.Type == type) return true;
            }
            return false;
        }

        public float FootprintRadius(Structure s)
        {
            BuildingDefinition def = Content.Building(s.DefId);
            return def == null ? 0.5f : 0.5f * Math.Max(def.SizeX, def.SizeZ);
        }

        private void WearEquipped(int amount)
        {
            int slot = Player.SelectedHotbar;
            ItemStack st = Player.Inventory.GetSlot(slot);
            if (st.IsEmpty) return;
            string name = Content.Items.NameOf(st.ItemId);
            if (Player.Inventory.Wear(slot, amount)) Events.Publish(new Notification($"Your {name} broke!", Severity.Warning));
        }

        private int FirstFreeHotbarSlot()
        {
            for (int i = 0; i < PlayerState.HotbarSize; i++)
                if (Player.Inventory.GetSlot(i).IsEmpty) return i;
            return -1;
        }

        private Vec3 InFrontOfPlayer(float distance)
        {
            double r = Player.Yaw * Math.PI / 180.0;
            var p = new Vec3(Player.Position.X + (float)Math.Sin(r) * distance, Player.Position.Y, Player.Position.Z + (float)Math.Cos(r) * distance);
            if (World.Layout != null) p.Y = Math.Max(p.Y, World.Layout.HeightAt(p.X, p.Z));
            return p;
        }

        internal void GiveToPlayerOrDrop(ItemAmount item, string label)
        {
            int added = Player.Inventory.AddUpTo(item.ItemId, item.Quantity);
            if (added < item.Quantity)
            {
                var stacks = SplitStacks(new ItemAmount(item.ItemId, item.Quantity - added));
                Construction.CreateDrop(stacks, InFrontOfPlayer(0.8f), label, false);
            }
        }

        private List<ItemStack> SplitStacks(ItemAmount a)
        {
            var result = new List<ItemStack>();
            ItemDefinition def = Content.Items.Get(a.ItemId);
            int max = def.HasDurability ? 1 : Math.Max(1, def.MaxStack);
            int remaining = a.Quantity;
            while (remaining > 0)
            {
                int q = Math.Min(max, remaining);
                result.Add(new ItemStack(a.ItemId, q));
                remaining -= q;
            }
            return result;
        }

        private static int CountUnits(Inventory inv)
        {
            int n = 0;
            for (int i = 0; i < inv.SlotCount; i++) n += inv.GetSlot(i).Quantity;
            return n;
        }
    }
}
