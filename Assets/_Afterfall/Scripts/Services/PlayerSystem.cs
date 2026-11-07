using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.Technology;
using Afterfall.Domain.World;

namespace Afterfall.Services
{
    /// <summary>
    /// Per-player simulation at 10 Hz: survival needs, the crafting queue, exploration
    /// (fog of war and point-of-interest discovery), item-acquisition knowledge and death.
    /// </summary>
    internal sealed class PlayerSystem : ISimulationSystem
    {
        private readonly GameSession _session;
        private double _sinceReveal;
        private bool _deathHandled;
        private float _lastHungerWarn = 100f;
        private float _lastThirstWarn = 100f;

        public PlayerSystem(GameSession session)
        {
            _session = session;
        }

        public string Name => "Player";

        public void Tick(double dt)
        {
            WorldState w = _session.World;
            PlayerState p = w.Player;
            if (p == null) return;

            if (p.Vitals.IsDead)
            {
                if (!_deathHandled)
                {
                    _deathHandled = true;
                    _session.HandleDeath(p.Vitals.Hunger <= 0 ? "starvation" : p.Vitals.Thirst <= 0 ? "dehydration" : "injuries");
                }
                return;
            }
            _deathHandled = false;

            float loss = SurvivalRules.Tick(p.Vitals, _session.Content.Survival, p.Activity, dt, w.Clock.SecondsPerGameHour, w.Settings.SurvivalDrain);
            if (loss > 0f && p.Vitals.IsDead)
            {
                _deathHandled = true;
                _session.HandleDeath(p.Vitals.Hunger <= 0 ? "starvation" : "dehydration");
                return;
            }
            WarnNeeds(p.Vitals);

            RecipeDefinition done = p.Crafting.Tick(dt, p.Inventory, _session.Content.Recipes);
            if (done != null)
            {
                w.Metrics.ItemsCrafted++;
                _session.Events.Publish(new RecipeCompleted(EntityId.None, done.Id));
                _session.Events.Publish(new Notification($"Crafted {done.Name}.", Severity.Success));
                foreach (ItemAmount o in done.Outputs) _session.Tech.Record(UnlockTrigger.CraftItem, o.ItemId, o.Quantity);
            }

            RecordAcquisitions(p.Inventory);

            _sinceReveal += dt;
            if (_sinceReveal >= 0.5)
            {
                _sinceReveal = 0;
                w.Exploration.Reveal(p.Position, GameSession.RevealRadius);
                DiscoverPois(w, p);
            }
        }

        private void RecordAcquisitions(Inventory inv)
        {
            TechnologyState tech = _session.World.Tech;
            List<string> fresh = null;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                ItemStack st = inv.GetSlot(i);
                if (st.IsEmpty || tech.GetProgress(UnlockTrigger.AcquireItem, st.ItemId) > 0) continue;
                (fresh ?? (fresh = new List<string>())).Add(st.ItemId);
            }
            if (fresh == null) return;
            foreach (string id in fresh)
                if (tech.GetProgress(UnlockTrigger.AcquireItem, id) == 0) _session.Tech.Record(UnlockTrigger.AcquireItem, id);
        }

        private void DiscoverPois(WorldState w, PlayerState p)
        {
            if (w.Layout == null) return;
            foreach (PoiInfo poi in w.Layout.Pois)
            {
                if (w.Exploration.DiscoveredPois.Contains(poi.Id)) continue;
                if (Vec3.DistanceXZ(poi.Center, p.Position) > poi.Radius + 25f) continue;
                w.Exploration.DiscoveredPois.Add(poi.Id);
                _session.Events.Publish(new PoiDiscovered(poi.Id, poi.Name));
                _session.Events.Publish(new Notification($"Discovered: {poi.Name} — {poi.Description}", Severity.Info));
                _session.Tech.Record(UnlockTrigger.DiscoverPoi, poi.Id);
            }
        }

        private void WarnNeeds(SurvivalState v)
        {
            float low = _session.Content.Survival.LowThreshold;
            if (v.Hunger < low && _lastHungerWarn >= low)
                _session.Events.Publish(new Notification("You are starving. Eat something soon.", Severity.Warning));
            if (v.Thirst < low && _lastThirstWarn >= low)
                _session.Events.Publish(new Notification("You are very thirsty. Find water.", Severity.Warning));
            _lastHungerWarn = v.Hunger;
            _lastThirstWarn = v.Thirst;
        }
    }
}
