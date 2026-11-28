using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>
    /// The insides of the city's buildings in the simulation. A building near the survivor is
    /// <see cref="Activate"/>d (its plan joins the layout, so its rooms count as shelter and have
    /// names); its doors, containers and furniture are made a floor at a time as someone reaches
    /// that floor (<see cref="SeedFloors"/>), each keyed and rolled from its own key, so a floor made
    /// again is the same. Floors whose things nobody touched are dropped again far from everyone.
    /// </summary>
    public sealed class CityInteriors
    {
        /// <summary>Seeded floors further than this from the survivor and every colonist may be dropped.</summary>
        public const float KeepRadius = 260f;

        private readonly GameSession _session;

        public CityInteriors(GameSession session)
        {
            _session = session;
        }

        private WorldState World => _session.World;

        public static string FloorKey(string planId, int floor) => $"{planId}:f{floor}";

        /// <summary>Adds a city building's plan to the layout (rooms, shelter) if it is not there.</summary>
        public void Activate(PlacedPlan building)
        {
            List<PlacedPlan> plans = World.Layout.Plans;
            foreach (PlacedPlan p in plans)
                if (p.Plan.Id == building.Plan.Id) return;
            plans.Add(building);
        }

        public void Deactivate(string planId) => World.Layout.Plans.RemoveAll(p => p.Plan.Id == planId && p.Plan.Id.StartsWith(BuildingPlans.CityBlockKind + "|", StringComparison.Ordinal));

        /// <summary>Makes the doors and furniture of floors <paramref name="from"/>..<paramref name="to"/> that are not made yet; returns how many things were made.</summary>
        public int SeedFloors(PlacedPlan building, int from, int to)
        {
            BuildingPlan plan = building.Plan;
            from = Math.Max(0, from);
            to = Math.Min(plan.Floors - 1, to);
            var seeds = new List<FixtureSeed>();
            var floors = new List<string>();
            for (int f = from; f <= to; f++)
            {
                string key = FloorKey(plan.Id, f);
                if (World.SeededFloors.Contains(key)) continue;
                floors.Add(key);
                foreach (PlanFixture fx in plan.Fixtures)
                {
                    if (fx.Floor != f) continue;
                    var seed = new FixtureSeed
                    {
                        Key = fx.Key, DefId = fx.DefId, Position = building.ToWorld(fx.X, fx.Z, fx.Floor), Yaw = building.Yaw + fx.Yaw, PoiId = building.PoiId,
                        LootTable = fx.LootTable, LockId = fx.LockId, Locked = fx.Locked
                    };
                    seed.KeysInside.AddRange(fx.KeysInside);
                    seed.ItemsInside.AddRange(fx.ItemsInside);
                    seeds.Add(seed);
                }
            }
            if (floors.Count == 0) return 0;
            int made = WorldSeeding.SeedFixtures(World, _session.Content, seeds, new List<VehicleSeed>());
            foreach (string key in floors) World.SeededFloors.Add(key);
            World.MarkChanged();
            return made;
        }

        /// <summary>
        /// Drops seeded floors far from the survivor and every colonist whose doors and furniture are
        /// all as they were made (contents, searched, doors, damage); a touched floor keeps everything.
        /// </summary>
        public void Prune(Vec3 survivor)
        {
            WorldState w = World;
            if (w.SeededFloors.Count == 0) return;
            var keep = new List<Vec3> { survivor };
            foreach (var c in w.Colonists.Values)
                if (c.Recruited) keep.Add(c.Position);
            var byFloor = new Dictionary<string, List<Structure>>(StringComparer.Ordinal);
            foreach (Structure s in w.Structures.Values)
            {
                if (s.SeedKey.Length == 0 || !s.SeedKey.StartsWith(BuildingPlans.CityBlockKind + "|", StringComparison.Ordinal)) continue;
                int colon = s.SeedKey.IndexOf(':');
                int second = colon < 0 ? -1 : s.SeedKey.IndexOf(':', colon + 1);
                if (second < 0) continue;
                string floor = s.SeedKey.Substring(0, second);
                if (!byFloor.TryGetValue(floor, out List<Structure> list)) byFloor[floor] = list = new List<Structure>();
                list.Add(s);
            }
            var dropFloors = new List<string>();
            foreach (string floor in w.SeededFloors)
            {
                // A floor whose things are all gone (destroyed) stays seeded, so they are not made again.
                if (!byFloor.TryGetValue(floor, out List<Structure> items) || items.Count == 0) continue;
                bool far = true, untouched = true;
                foreach (Structure s in items)
                {
                    foreach (Vec3 k in keep)
                        if (Vec3.DistanceXZ(k, s.Position) < KeepRadius) far = false;
                    BuildingDefinition def = _session.Content.Building(s.DefId);
                    if (s.Touched || s.Searched || s.DoorOpen || !s.IsComplete || def == null || s.Health < def.MaxHealth) untouched = false;
                }
                if (!far || !untouched) continue;
                foreach (Structure s in items) w.RemoveStructure(s.Id);
                dropFloors.Add(floor);
            }
            foreach (string f in dropFloors) w.SeededFloors.Remove(f);
        }
    }
}
