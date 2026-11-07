using Afterfall.Core;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Events;
using UnityEngine;
using UnityEngine.AI;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>Physical body of a survivor/colonist: NavMesh movement, rig animation, talk interaction.</summary>
    public sealed class ColonistView : EntityView, IInteractable
    {
        public NavMeshAgent Agent;
        public HumanoidRig Rig;
        public Vector3 LastDestination = new Vector3(float.NaN, 0, 0);

        /// <summary>Seconds spent standing at the end of a path that does not reach the destination.</summary>
        public float StalledTime;
        private GameController _gc;
        private Vector3 _lastPos;
        private float _speed;

        public bool HoldToInteract => false;
        public bool PrimaryHarvests => false;
        public Vector3 FocusPoint => transform.position + Vector3.up * 1.4f;
        public bool OnNavMesh => Agent != null && Agent.enabled && Agent.isOnNavMesh;

        public static ColonistView Create(Transform parent, ColonistState c, GameController gc)
        {
            var go = new GameObject($"Colonist_{c.Name}");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(c.Position.ToUnity(), Quaternion.Euler(0, c.Yaw, 0));
            go.layer = Layers.Character;
            var view = go.AddComponent<ColonistView>();
            view.Id = c.Id;
            view._gc = gc;
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.height = 1.8f;
            col.radius = 0.3f;
            int v = (int)(c.Id.Value % 3);
            Color[] shirts = { new Color(0.55f, 0.35f, 0.22f), new Color(0.25f, 0.38f, 0.5f), new Color(0.45f, 0.42f, 0.25f) };
            view.Rig = HumanoidRig.Create(go.transform, new HumanoidRig.Palette
            {
                Skin = new Color(0.72f, 0.55f, 0.42f),
                Shirt = shirts[v],
                Pants = new Color(0.3f, 0.28f, 0.25f),
                Shoes = new Color(0.18f, 0.14f, 0.1f),
                Accent = new Color(0.5f, 0.4f, 0.3f)
            }, false);
            Shapes.SetLayerRecursive(view.Rig.gameObject, Layers.Character);
            // Interaction hitbox (trigger) so the survivor can be aimed at.
            var hit = new GameObject("Hitbox");
            hit.transform.SetParent(go.transform, false);
            hit.layer = Layers.Interactable;
            var bc = hit.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0, 0.9f, 0);
            bc.size = new Vector3(0.8f, 1.8f, 0.8f);

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.speed = ColonySystem.WalkSpeed;
            agent.acceleration = 12f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 0.8f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false;
            view.Agent = agent;
            go.SetActive(true);
            view._lastPos = go.transform.position;
            return view;
        }

        private void Update()
        {
            if (_gc?.Session == null || !_gc.Session.World.Colonists.TryGetValue(Id, out ColonistState c)) return;
            bool down = c.Activity == ColonistActivity.Down;
            Rig.SetDead(down);
            if (down)
            {
                if (Agent.enabled) Agent.enabled = false;
                Rig.Animate(0f, false, true, Time.deltaTime);
                return;
            }
            if (!Agent.enabled && _gc.NavReady && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                Agent.enabled = true;
                Agent.Warp(hit.position);
            }
            if (_gc.Session.Paused)
            {
                if (OnNavMesh) Agent.isStopped = true;
                return;
            }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (OnNavMesh)
            {
                c.Position = transform.position.ToSim();
                if (Agent.velocity.sqrMagnitude > 0.05f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(Agent.velocity.x, 0, Agent.velocity.z)), dt * 8f);
                    c.Yaw = transform.eulerAngles.y;
                }
            }
            else
            {
                Vector3 target = c.Position.ToUnity();
                target.y = _gc.GroundHeight(target);
                transform.position = Vector3.MoveTowards(transform.position, target, 8f * dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, c.Yaw, 0), dt * 6f);
            }
            Vector3 pos = transform.position;
            _speed = Mathf.Lerp(_speed, new Vector3(pos.x - _lastPos.x, 0, pos.z - _lastPos.z).magnitude / dt, dt * 8f);
            _lastPos = pos;
            Rig.Animate(_speed, c.Activity == ColonistActivity.Resting, true, dt);
            if (c.Activity == ColonistActivity.Working && _speed < 0.2f) Rig.PlaySwingIfIdle();
        }

        public string Prompt(GameController gc)
        {
            if (!gc.Session.World.Colonists.TryGetValue(Id, out ColonistState c)) return null;
            if (c.Activity == ColonistActivity.Down) return $"{c.Name} (dead)";
            if (!c.Recruited) return $"[E] Talk to {c.Name} — invite them to your settlement (needs a free bedroll)";
            return $"[E] {c.Name}: {c.Status}  · food {c.Vitals.Hunger:0}% · water {c.Vitals.Thirst:0}%";
        }

        public void Interact(GameController gc)
        {
            if (!gc.Session.World.Colonists.TryGetValue(Id, out ColonistState c)) return;
            if (!c.Recruited)
            {
                OpResult r = gc.Session.Recruit(Id);
                gc.Toast(r.Message, r.Ok ? Severity.Success : Severity.Warning);
                return;
            }
            string fails = c.RecentFailures.Count > 0 ? $" Last problem: {c.RecentFailures[0]}" : "";
            gc.Toast($"{c.Name}: \"{c.Status}.\"{fails}", Severity.Info);
        }

        public void InteractHeld(GameController gc, float dt) { }
    }

    /// <summary>Moves colonists with their NavMeshAgent; falls back to straight lines without a view or NavMesh.</summary>
    public sealed class NavColonistMovement : IColonistMovement
    {
        private readonly GameController _gc;
        private readonly DirectColonistMovement _fallback = new DirectColonistMovement();

        public NavColonistMovement(GameController gc)
        {
            _gc = gc;
            _fallback.GroundHeight = p => gc.Session.World.Layout.HeightAt(p.X, p.Z);
        }

        public bool Step(ColonistState c, float speed, float arriveDistance, float dt)
        {
            ColonistView v = _gc.Sync != null ? _gc.Sync.ColonistViewOf(c.Id) : null;
            if (v == null || !v.OnNavMesh) return _fallback.Step(c, speed, arriveDistance, dt);
            Vector3 target = c.MoveTarget.ToUnity();
            Vector3 flat = v.transform.position - target;
            flat.y = 0f;
            if (flat.magnitude <= arriveDistance)
            {
                v.Agent.isStopped = true;
                return true;
            }
            v.Agent.isStopped = false; // also clears a pause stop
            v.Agent.speed = speed;
            if (float.IsNaN(v.LastDestination.x) || (target - v.LastDestination).sqrMagnitude > 0.25f)
            {
                v.LastDestination = target;
                v.StalledTime = 0f;
                if (!v.Agent.SetDestination(target)) return _fallback.Step(c, speed, arriveDistance, dt);
            }
            bool stalled = !v.Agent.pathPending && v.Agent.pathStatus != NavMeshPathStatus.PathComplete && v.Agent.velocity.sqrMagnitude < 0.04f;
            v.StalledTime = stalled ? v.StalledTime + dt : 0f;
            // A target inside a carved obstacle (crate, machine) can only be approached to its edge.
            return !v.Agent.pathPending && v.Agent.remainingDistance <= Mathf.Max(v.Agent.stoppingDistance, arriveDistance) && flat.magnitude <= arriveDistance + 1.5f;
        }

        public bool CannotReach(ColonistState c)
        {
            ColonistView v = _gc.Sync != null ? _gc.Sync.ColonistViewOf(c.Id) : null;
            return v != null && v.OnNavMesh && v.StalledTime > StallLimit;
        }

        /// <summary>Game seconds a colonist may stand at the end of a partial path before the job is given up.</summary>
        public const float StallLimit = 4f;
    }
}
