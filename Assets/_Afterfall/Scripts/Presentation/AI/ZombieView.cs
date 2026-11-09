using System;
using Afterfall.Core;
using Afterfall.Domain.AI;
using UnityEngine;
using UnityEngine.AI;

namespace Afterfall.Presentation
{
    /// <summary>Physical body of a nearby infected: NavMeshAgent movement, rig animation, hit volume.</summary>
    public sealed class ZombieView : EntityView
    {
        public NavMeshAgent Agent;
        public HumanoidRig Rig;
        public Vector3 LastDestination = new Vector3(float.NaN, 0, 0);
        public float StuckTime;
        public bool PathFailed;
        private GameController _gc;
        private Vector3 _lastPos;
        private float _speed;
        private bool _dead;

        /// <summary>Look of an infected; <paramref name="variant"/> is 0..2 (office worker, hoodie, labourer).</summary>
        public static HumanoidRig.Palette PaletteFor(int variant)
        {
            var p = new HumanoidRig.Palette
            {
                Skin = new Color(0.55f, 0.6f, 0.49f),
                Accent = new Color(0.22f, 0.17f, 0.13f),
                Variant = ((variant % 3) + 3) % 3
            };
            switch (p.Variant)
            {
                case 0:
                    p.Shirt = new Color(0.62f, 0.6f, 0.52f);
                    p.Pants = new Color(0.2f, 0.2f, 0.22f);
                    p.Shoes = new Color(0.09f, 0.08f, 0.08f);
                    p.Hair = new Color(0.26f, 0.23f, 0.19f);
                    p.Outfit = HumanoidRig.Outfit.DressShirt;
                    p.Legwear = HumanoidRig.Legwear.Work;
                    p.Footwear = HumanoidRig.Footwear.Shoes;
                    p.HairStyle = HumanoidRig.HairStyle.Balding;
                    break;
                case 1:
                    p.Shirt = new Color(0.27f, 0.31f, 0.37f);
                    p.Pants = new Color(0.26f, 0.31f, 0.4f);
                    p.Shoes = new Color(0.34f, 0.33f, 0.32f);
                    p.Hair = new Color(0.2f, 0.16f, 0.12f);
                    p.Outfit = HumanoidRig.Outfit.Hoodie;
                    p.Legwear = HumanoidRig.Legwear.Jeans;
                    p.Footwear = HumanoidRig.Footwear.Shoes;
                    p.HairStyle = HumanoidRig.HairStyle.Long;
                    p.Female = true;
                    break;
                default:
                    p.Shirt = new Color(0.42f, 0.31f, 0.22f);
                    p.Pants = new Color(0.29f, 0.28f, 0.23f);
                    p.Shoes = new Color(0.2f, 0.15f, 0.1f);
                    p.Hair = new Color(0.15f, 0.13f, 0.1f);
                    p.Outfit = HumanoidRig.Outfit.WorkJacket;
                    p.Legwear = HumanoidRig.Legwear.Cargo;
                    p.HairStyle = HumanoidRig.HairStyle.Crop;
                    break;
            }
            return p;
        }

        public static ZombieView Create(Transform parent, ZombieState z, GameController gc)
        {
            // Built inactive so the NavMeshAgent is not placed before we know a NavMesh exists.
            var go = new GameObject($"Zombie{z.Id}");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(z.Position.ToUnity(), Quaternion.Euler(0, z.Yaw, 0));
            go.layer = Layers.Zombie;
            var view = go.AddComponent<ZombieView>();
            view.Id = z.Id;
            view._gc = gc;
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.height = 1.8f;
            col.radius = 0.35f;
            view.Rig = HumanoidRig.Create(go.transform, PaletteFor((int)(z.Id.Value % 3)), true);
            Shapes.SetLayerRecursive(view.Rig.gameObject, Layers.Zombie);

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = 1f;
            agent.angularSpeed = 300f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 1.1f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            view.Agent = agent;
            agent.enabled = false;
            go.SetActive(true);
            if (gc.NavReady && NavMesh.SamplePosition(go.transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                agent.enabled = true;
                agent.Warp(hit.position);
            }
            view._lastPos = go.transform.position;
            if (z.IsDead) view.SetDead();
            return view;
        }

        public bool OnNavMesh => Agent != null && Agent.enabled && Agent.isOnNavMesh;

        public void SetDead()
        {
            if (_dead) return;
            _dead = true;
            Rig.SetDead(true);
            if (Agent != null && Agent.enabled)
            {
                if (Agent.isOnNavMesh) Agent.isStopped = true;
                Agent.enabled = false;
            }
            var c = GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }

        private void Update()
        {
            if (_gc?.Session == null) return;
            if (!_gc.Session.World.Zombies.TryGetValue(Id, out ZombieState z)) return;
            if (z.IsDead) SetDead();
            if (_gc.Session.Paused)
            {
                // The simulation is frozen: agents must not keep walking to their last destination.
                if (OnNavMesh) Agent.isStopped = true;
                return;
            }

            // Late navmesh: enable the agent once the navmesh has been baked.
            if (!_dead && Agent != null && !Agent.enabled && _gc.NavReady && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                Agent.enabled = true;
                Agent.Warp(hit.position);
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!_dead && !OnNavMesh)
            {
                // Fallback locomotion: follow the domain position, glued to the terrain.
                Vector3 target = z.Position.ToUnity();
                target.y = _gc.GroundHeight(target);
                transform.position = Vector3.MoveTowards(transform.position, target, 6f * dt);
            }
            transform.rotation = OnNavMesh && Agent.velocity.sqrMagnitude > 0.05f
                ? Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(Agent.velocity.x, 0, Agent.velocity.z)), dt * 8f)
                : Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, z.Yaw, 0), dt * 6f);

            Vector3 pos = transform.position;
            _speed = Mathf.Lerp(_speed, new Vector3(pos.x - _lastPos.x, 0, pos.z - _lastPos.z).magnitude / dt, dt * 8f);
            _lastPos = pos;
            if (OnNavMesh)
            {
                // The physical body is authoritative for position.
                z.Position = pos.ToSim();
                z.Yaw = transform.eulerAngles.y;
            }
            Rig.Animate(_speed, false, true, dt);
        }
    }

    /// <summary>
    /// Connects the pure zombie AI to Unity: line of sight by physics linecast, movement and
    /// path status from NavMeshAgents. Infected without a view (far away) keep moving in
    /// simplified straight lines so the world stays alive without full simulation cost.
    /// </summary>
    public sealed class NavZombieAdapter : IZombieWorldAdapter
    {
        private readonly GameController _gc;
        private readonly DirectZombieAdapter _fallback = new DirectZombieAdapter();

        public NavZombieAdapter(GameController gc)
        {
            _gc = gc;
        }

        public bool HasLineOfSight(ZombieState zombie, Vec3 target)
        {
            if (_gc.Sync.ZombieViewOf(zombie.Id) == null) return false;
            Vector3 eye = zombie.Position.ToUnity() + Vector3.up * 1.6f;
            Vector3 chest = target.ToUnity() + Vector3.up * 1.3f;
            return !Physics.Linecast(eye, chest, Layers.SolidMask, QueryTriggerInteraction.Ignore);
        }

        public ZombieNavStatus GetNavStatus(ZombieState zombie)
        {
            ZombieView v = _gc.Sync.ZombieViewOf(zombie.Id);
            if (v == null || !v.OnNavMesh) return _fallback.GetNavStatus(zombie);
            NavMeshAgent a = v.Agent;
            bool arrived = !a.pathPending && a.remainingDistance <= a.stoppingDistance + 0.4f;
            // The end of a partial path (a wall or barricade in the way) is not the goal: keep pursuing,
            // so the stall turns into a breach instead of the infected giving up and searching.
            Vector3 end = a.pathEndPosition - v.LastDestination;
            bool shortOfGoal = a.pathStatus == NavMeshPathStatus.PathPartial && !float.IsNaN(v.LastDestination.x) && new Vector2(end.x, end.z).magnitude > 1.0f;
            var s = new ZombieNavStatus
            {
                ReachedDestination = arrived && !shortOfGoal,
                PathFailed = v.PathFailed
            };
            v.PathFailed = false;
            return s;
        }

        public void Apply(ZombieState zombie, ZombieArchetype archetype, ZombieDecision decision, float dt)
        {
            ZombieView v = _gc.Sync.ZombieViewOf(zombie.Id);
            if (v == null || !v.OnNavMesh)
            {
                _fallback.Apply(zombie, archetype, decision, dt);
                if (_gc.Session.World.Layout != null)
                {
                    Vec3 p = zombie.Position;
                    p.Y = _gc.Session.World.Layout.HeightAt(p.X, p.Z);
                    zombie.Position = p;
                }
                if (decision.Attack && v != null)
                {
                    v.Rig.PlaySwing();
                    _gc.Audio?.ZombieAttack(v.transform.position);
                }
                return;
            }

            NavMeshAgent a = v.Agent;
            if (decision.Move)
            {
                a.isStopped = false;
                a.speed = decision.Speed;
                Vector3 dest = decision.Destination.ToUnity();
                // Avoid flooding the pathfinder: only re-path when the goal moved meaningfully.
                if (float.IsNaN(v.LastDestination.x) || (dest - v.LastDestination).sqrMagnitude > 0.6f)
                {
                    v.LastDestination = dest;
                    if (!a.SetDestination(dest)) v.PathFailed = true;
                }
                if (!a.pathPending && a.pathStatus == NavMeshPathStatus.PathInvalid) v.PathFailed = true;
                bool stuck = a.velocity.sqrMagnitude < 0.01f && a.remainingDistance > a.stoppingDistance + 0.5f && !a.pathPending;
                v.StuckTime = stuck ? v.StuckTime + dt : 0f;
                if (v.StuckTime > 3f)
                {
                    v.PathFailed = true;
                    v.StuckTime = 0f;
                    v.LastDestination = new Vector3(float.NaN, 0, 0);
                }
            }
            else
            {
                a.isStopped = true;
            }
            if (decision.FaceTarget)
            {
                Vector3 dir = decision.FacePosition.ToUnity() - v.transform.position;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) zombie.Yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            }
            if (decision.Attack)
            {
                v.Rig.PlaySwing();
                _gc.Audio?.ZombieAttack(v.transform.position);
            }
        }
    }
}
