using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Content;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Power
{
    /// <summary>Aggregated per-network figures for the energy UI.</summary>
    public sealed class PowerNetworkInfo
    {
        public int Id { get; set; }
        public List<EntityId> Members { get; } = new List<EntityId>();
        public float CapacityWatts { get; set; }
        public float DemandWatts { get; set; }
        public float SuppliedWatts { get; set; }
        public double StoredJoules { get; set; }
        public double StorageCapacityJoules { get; set; }
        public int Generators { get; set; }
        public int RunningGenerators { get; set; }
        public int Consumers { get; set; }
        public int UnpoweredConsumers { get; set; }
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// Electricity as a graph simulation. Topology (connected networks) is rebuilt only
    /// when <see cref="WorldState.PowerTopologyVersion"/> changes; supply/demand balancing
    /// runs every tick with priority-based load shedding, battery buffering and fuel use.
    /// </summary>
    public sealed class PowerSystem : ISimulationSystem
    {
        private readonly WorldState _world;
        private readonly ContentDatabase _content;
        private readonly EventBus _bus;
        private readonly List<PowerNetworkInfo> _networks = new List<PowerNetworkInfo>();
        private int _builtVersion = -1;

        public PowerSystem(WorldState world, ContentDatabase content, EventBus bus)
        {
            _world = world;
            _content = content;
            _bus = bus;
        }

        public string Name => "Power";
        public IReadOnlyList<PowerNetworkInfo> Networks => _networks;
        public int TopologyRebuilds { get; private set; }

        public PowerNetworkInfo NetworkOf(EntityId structureId)
        {
            if (!_world.Structures.TryGetValue(structureId, out Structure s) || s.Power == null) return null;
            foreach (PowerNetworkInfo n in _networks)
                if (n.Id == s.Power.NetworkId) return n;
            return null;
        }

        public void Tick(double dt)
        {
            if (_builtVersion != _world.PowerTopologyVersion) RebuildTopology();
            foreach (PowerNetworkInfo net in _networks) Balance(net, dt);
        }

        /// <summary>Union-find over complete electrical structures and their cables.</summary>
        public void RebuildTopology()
        {
            _builtVersion = _world.PowerTopologyVersion;
            TopologyRebuilds++;
            _networks.Clear();

            var nodes = new List<EntityId>();
            foreach (EntityId id in _world.SortedStructureIds())
            {
                Structure s = _world.Structures[id];
                if (s.Power != null && s.IsComplete) nodes.Add(id);
            }
            var parent = new Dictionary<EntityId, EntityId>();
            foreach (EntityId id in nodes) parent[id] = id;

            EntityId Find(EntityId x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (PowerLink link in _world.PowerLinks)
            {
                if (!parent.ContainsKey(link.A) || !parent.ContainsKey(link.B)) continue;
                EntityId ra = Find(link.A), rb = Find(link.B);
                if (ra == rb) continue;
                if (ra.Value < rb.Value) parent[rb] = ra;
                else parent[ra] = rb;
            }

            var byRoot = new Dictionary<EntityId, PowerNetworkInfo>();
            int nextId = 1;
            foreach (EntityId id in nodes)
            {
                EntityId root = Find(id);
                if (!byRoot.TryGetValue(root, out PowerNetworkInfo net))
                {
                    net = new PowerNetworkInfo { Id = nextId++ };
                    byRoot[root] = net;
                    _networks.Add(net);
                }
                net.Members.Add(id);
                _world.Structures[id].Power.NetworkId = net.Id;
            }
            _bus?.Publish(new PowerNetworkChanged(_networks.Count));
        }

        private struct Consumer
        {
            public Structure S;
            public float Demand;
            public int Priority;
        }

        private void Balance(PowerNetworkInfo net, double dt)
        {
            var generators = new List<Structure>();
            var batteries = new List<Structure>();
            var consumers = new List<Consumer>();
            bool isNight = _world.Clock.Daylight01 < 0.55f;

            foreach (EntityId id in net.Members)
            {
                if (!_world.Structures.TryGetValue(id, out Structure s)) continue;
                BuildingDefinition def = _content.Building(s.DefId);
                if (def == null) continue;
                bool operational = s.Health > 0f && s.Enabled;
                if (s.Generator != null && def.Generator != null && operational && HasFuel(s.Generator)) generators.Add(s);
                if (s.Battery != null && def.Battery != null && s.Health > 0f) batteries.Add(s);

                float demand = 0f;
                int priority = 3;
                if (def.Machine != null && def.Machine.PowerWatts > 0f && s.Machine != null && s.Machine.RequestingPower && operational)
                {
                    demand = def.Machine.PowerWatts;
                    priority = def.Machine.PowerPriority;
                }
                else if (def.Light != null && operational && isNight)
                {
                    demand = def.Light.PowerWatts;
                    priority = def.Light.PowerPriority;
                }
                s.Power.DemandWatts = demand;
                if (demand > 0f) consumers.Add(new Consumer { S = s, Demand = demand, Priority = priority });
                else if (def.Machine != null || def.Light != null)
                {
                    s.Power.Powered = false;
                    s.Power.Reason = !s.Enabled ? "Switched off" : def.Light != null ? "Off (daylight)" : "Not requested";
                }
            }

            float genCapacity = 0f;
            foreach (Structure g in generators) genCapacity += _content.Building(g.DefId).Generator.CapacityWatts;
            float battDischargeCap = 0f;
            float battChargeCap = 0f;
            foreach (Structure b in batteries)
            {
                BatterySpec spec = _content.Building(b.DefId).Battery;
                double capJ = spec.CapacityKJ * 1000.0;
                battDischargeCap += (float)Math.Min(spec.MaxRateWatts, b.Battery.StoredJoules / dt);
                battChargeCap += (float)Math.Min(spec.MaxRateWatts, (capJ - b.Battery.StoredJoules) / dt);
            }

            consumers.Sort((x, y) => x.Priority != y.Priority ? x.Priority.CompareTo(y.Priority) : x.S.Id.CompareTo(y.S.Id));
            float available = genCapacity + battDischargeCap;
            float supplied = 0f;
            float totalDemand = 0f;
            int unpowered = 0;
            foreach (Consumer c in consumers)
            {
                totalDemand += c.Demand;
                if (supplied + c.Demand <= available + 1e-3f)
                {
                    supplied += c.Demand;
                    c.S.Power.Powered = true;
                    c.S.Power.Reason = "Powered";
                }
                else
                {
                    unpowered++;
                    c.S.Power.Powered = false;
                    c.S.Power.Reason = generators.Count == 0 && battDischargeCap <= 0f
                        ? (HasAnyGenerator(net) ? "No power: generator out of fuel or off" : "No power source on this network")
                        : $"Insufficient power: shed by priority ({available:0} W available, {totalDemand:0} W demanded)";
                }
            }

            // Generators cover consumption first; surplus charges batteries; batteries cover any deficit.
            float fromGenerators = Math.Min(genCapacity, supplied);
            float deficit = supplied - fromGenerators;
            float charge = Math.Min(genCapacity - fromGenerators, battChargeCap);
            float generatorOutput = fromGenerators + charge;

            foreach (Structure g in generators)
            {
                float cap = _content.Building(g.DefId).Generator.CapacityWatts;
                float share = genCapacity > 0f ? generatorOutput * (cap / genCapacity) : 0f;
                g.Generator.OutputWatts = share;
                g.Generator.Running = true;
                BurnFuel(g, share * dt);
                g.Generator.Status = share > 0.5f ? $"Running — {share:0} W of {cap:0} W" : "Running — idle (no load)";
            }
            foreach (EntityId id in net.Members)
            {
                if (!_world.Structures.TryGetValue(id, out Structure s) || s.Generator == null || generators.Contains(s)) continue;
                s.Generator.Running = false;
                s.Generator.OutputWatts = 0f;
                s.Generator.Status = s.Health <= 0f ? "Damaged" : !s.Enabled ? "Switched off" : "Out of fuel";
            }

            double stored = 0, capacity = 0;
            foreach (Structure b in batteries)
            {
                BatterySpec spec = _content.Building(b.DefId).Battery;
                double capJ = spec.CapacityKJ * 1000.0;
                float flow = 0f;
                if (charge > 0f && battChargeCap > 0f)
                {
                    float room = (float)Math.Min(spec.MaxRateWatts, (capJ - b.Battery.StoredJoules) / dt);
                    flow = charge * (room / battChargeCap);
                }
                else if (deficit > 0f && battDischargeCap > 0f)
                {
                    float avail = (float)Math.Min(spec.MaxRateWatts, b.Battery.StoredJoules / dt);
                    flow = -deficit * (avail / battDischargeCap);
                }
                b.Battery.StoredJoules = Math.Max(0.0, Math.Min(capJ, b.Battery.StoredJoules + flow * dt));
                b.Battery.FlowWatts = flow;
                stored += b.Battery.StoredJoules;
                capacity += capJ;
            }

            net.CapacityWatts = genCapacity;
            net.DemandWatts = totalDemand;
            net.SuppliedWatts = supplied;
            net.StoredJoules = stored;
            net.StorageCapacityJoules = capacity;
            net.Generators = CountGenerators(net);
            net.RunningGenerators = generators.Count;
            net.Consumers = consumers.Count;
            net.UnpoweredConsumers = unpowered;
            net.Status = unpowered > 0 ? $"{unpowered} consumer(s) without power" : consumers.Count > 0 ? "All consumers powered" : "No load";
        }

        private static bool HasFuel(GeneratorState g)
        {
            if (g.BufferJoules > 0) return true;
            for (int i = 0; i < g.Fuel.SlotCount; i++)
                if (!g.Fuel.GetSlot(i).IsEmpty) return true;
            return false;
        }

        /// <summary>Consumes fuel items as energy is drawn. Fuel units are removed only when actually burned.</summary>
        private void BurnFuel(Structure g, double joules)
        {
            GeneratorState gen = g.Generator;
            double remaining = joules;
            while (remaining > 0)
            {
                if (gen.BufferJoules >= remaining)
                {
                    gen.BufferJoules -= remaining;
                    return;
                }
                remaining -= gen.BufferJoules;
                gen.BufferJoules = 0;
                if (!TryLoadFuelUnit(gen)) return;
            }
        }

        private bool TryLoadFuelUnit(GeneratorState gen)
        {
            for (int i = 0; i < gen.Fuel.SlotCount; i++)
            {
                ItemStack st = gen.Fuel.GetSlot(i);
                if (st.IsEmpty) continue;
                ItemDefinition def = _content.Items.Get(st.ItemId);
                if (def.EnergyKJ <= 0f) continue;
                if (gen.Fuel.TakeFromSlot(i, 1).IsEmpty) continue;
                gen.BufferJoules += def.EnergyKJ * 1000.0;
                return true;
            }
            return false;
        }

        private int CountGenerators(PowerNetworkInfo net)
        {
            int n = 0;
            foreach (EntityId id in net.Members)
                if (_world.Structures.TryGetValue(id, out Structure s) && s.Generator != null) n++;
            return n;
        }

        private bool HasAnyGenerator(PowerNetworkInfo net) => CountGenerators(net) > 0;
    }
}
