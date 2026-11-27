using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Colonists and doors: a colonist whose path ends at a closed, unlocked door opens it (a
    /// closed door carves the NavMesh, so the path stops there), walks through, and the door is
    /// closed again once nobody is passing. Locked doors stay shut; the colonist then reports
    /// that it cannot find a way.
    /// </summary>
    public sealed class DoorTraffic
    {
        private const float Reach = 2.6f;
        private const float CloseAfter = 4f;

        private readonly GameController _gc;
        private readonly Dictionary<EntityId, float> _opened = new Dictionary<EntityId, float>();
        private readonly List<EntityId> _done = new List<EntityId>();
        private float _scan;

        public DoorTraffic(GameController gc)
        {
            _gc = gc;
        }

        /// <summary>Doors colonists opened and that are waiting to be closed behind them.</summary>
        public int OpenedByColonists => _opened.Count;

        /// <summary>Opens the nearest closed, unlocked door in reach of a stalled colonist. True if one was opened.</summary>
        public bool TryOpenNear(ColonistState c)
        {
            var w = _gc.Session.World;
            Structure best = null;
            float bestD = Reach;
            foreach (Structure s in w.Structures.Values)
            {
                if (!s.IsComplete || s.DoorOpen) continue;
                BuildingDefinition def = _gc.Session.Content.Building(s.DefId);
                if (def == null || !def.IsDoor || Mathf.Abs(s.Position.Y - c.Position.Y) > DoorRules.FloorReach) continue;
                float d = Vec3.DistanceXZ(s.Position, c.Position);
                if (d >= bestD) continue;
                bestD = d;
                best = s;
            }
            if (best == null) return false;
            OpResult r = _gc.Session.OpenDoorForColonist(c.Id, best.Id);
            if (!r.Ok) return false;
            _opened[best.Id] = Time.time;
            return true;
        }

        /// <summary>Closes doors colonists opened once they have passed and nobody stands in the doorway.</summary>
        public void Update()
        {
            _scan -= Time.deltaTime;
            if (_scan > 0f || _opened.Count == 0) return;
            _scan = 0.5f;
            var w = _gc.Session.World;
            _done.Clear();
            foreach (KeyValuePair<EntityId, float> kv in _opened)
            {
                if (!w.Structures.TryGetValue(kv.Key, out Structure door))
                {
                    _done.Add(kv.Key);
                    continue;
                }
                if (!door.DoorOpen)
                {
                    _done.Add(kv.Key);
                    continue;
                }
                if (Time.time - kv.Value < CloseAfter) continue;
                bool busy = Vector3.Distance(_gc.Player.transform.position, door.Position.ToUnity()) < 1.4f;
                foreach (ColonistState c in w.Colonists.Values)
                    if (!c.Vitals.IsDead && Vec3.Distance(c.Position, door.Position) < Reach) busy = true;
                if (busy) continue;
                _gc.Session.CloseDoorBehind(door.Id);
                _done.Add(kv.Key);
            }
            foreach (EntityId id in _done) _opened.Remove(id);
        }
    }
}
