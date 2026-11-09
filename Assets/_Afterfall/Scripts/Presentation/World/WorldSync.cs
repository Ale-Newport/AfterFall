using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.AI;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Construction;
using Afterfall.Domain.World;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Keeps GameObjects in sync with the authoritative world state. Views are created,
    /// rebuilt or destroyed when <see cref="WorldState.Version"/> changes; domain state is
    /// never derived from GameObjects (except physical positions reported back).
    /// </summary>
    public sealed class WorldSync : MonoBehaviour
    {
        public const float ZombieViewRadius = 140f;

        private GameController _gc;
        private Transform _nodesRoot, _structuresRoot, _dropsRoot, _zombiesRoot;
        private readonly Dictionary<EntityId, ResourceNodeView> _nodes = new Dictionary<EntityId, ResourceNodeView>();
        private readonly Dictionary<EntityId, StructureView> _structures = new Dictionary<EntityId, StructureView>();
        private readonly Dictionary<EntityId, DropView> _drops = new Dictionary<EntityId, DropView>();
        private readonly Dictionary<EntityId, ZombieView> _zombies = new Dictionary<EntityId, ZombieView>();
        private readonly Dictionary<EntityId, ColonistView> _colonists = new Dictionary<EntityId, ColonistView>();
        private Transform _colonistsRoot;
        private int _version = -1;
        private float _zombieScan;

        public IReadOnlyDictionary<EntityId, StructureView> Structures => _structures;
        public IReadOnlyDictionary<EntityId, ZombieView> Zombies => _zombies;
        public IReadOnlyDictionary<EntityId, ColonistView> Colonists => _colonists;
        public ColonistView ColonistViewOf(EntityId id) => _colonists.TryGetValue(id, out ColonistView v) ? v : null;

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _nodesRoot = Root("Resources");
            _structuresRoot = Root("Structures");
            _dropsRoot = Root("Drops");
            _zombiesRoot = Root("Infected");
            _colonistsRoot = Root("Survivors");
            Reconcile();
        }

        private Transform Root(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        public ZombieView ZombieViewOf(EntityId id) => _zombies.TryGetValue(id, out ZombieView v) ? v : null;
        public StructureView StructureViewOf(EntityId id) => _structures.TryGetValue(id, out StructureView v) ? v : null;

        private void LateUpdate()
        {
            if (_gc == null || _gc.Session == null) return;
            if (_gc.Session.World.Version != _version) Reconcile();

            float dt = Time.deltaTime;
            foreach (KeyValuePair<EntityId, StructureView> kv in _structures)
                if (_gc.Session.World.Structures.TryGetValue(kv.Key, out Structure s)) kv.Value.RefreshVisualState(s, dt);

            _zombieScan -= dt;
            if (_zombieScan <= 0f)
            {
                _zombieScan = 0.5f;
                SyncZombieViews();
            }
        }

        public void Reconcile()
        {
            WorldState w = _gc.Session.World;
            _version = w.Version;

            // Resource nodes
            RemoveMissing(_nodes, w.ResourceNodes);
            foreach (ResourceNodeState n in w.ResourceNodes.Values)
            {
                if (_nodes.TryGetValue(n.Id, out ResourceNodeView view))
                {
                    if (view.BuiltDepleted == n.Depleted) continue;
                    Destroy(view.gameObject);
                    _nodes.Remove(n.Id);
                }
                ResourceNodeDefinition def = _gc.Session.Content.Node(n.DefId);
                if (def == null) continue;
                var go = new GameObject($"{n.DefId}{n.Id}");
                go.transform.SetParent(_nodesRoot, false);
                go.transform.SetPositionAndRotation(n.Position.ToUnity(), Quaternion.Euler(0, n.Yaw, 0));
                go.transform.localScale = Vector3.one * n.Scale;
                NodeVisuals.Build(go.transform, def, n);
                Shapes.SetLayerRecursive(go, Layers.Resource);
                var v = go.AddComponent<ResourceNodeView>();
                v.Id = n.Id;
                v.Def = def;
                v.BuiltDepleted = n.Depleted;
                _nodes[n.Id] = v;
            }

            // Structures
            RemoveMissing(_structures, w.Structures);
            foreach (Structure s in w.Structures.Values)
            {
                if (_structures.TryGetValue(s.Id, out StructureView view))
                {
                    if (view.BuiltComplete == s.IsComplete) continue;
                    Destroy(view.gameObject);
                    _structures.Remove(s.Id);
                }
                CreateStructureView(s);
            }

            // Drops
            RemoveMissing(_drops, w.Drops);
            foreach (WorldDrop d in w.Drops.Values)
            {
                if (_drops.ContainsKey(d.Id)) continue;
                var go = new GameObject($"Drop{d.Id}");
                go.transform.SetParent(_dropsRoot, false);
                go.transform.position = d.Position.ToUnity();
                NodeVisuals.BuildDrop(go.transform, d.IsBelongings);
                Shapes.SetLayerRecursive(go, Layers.Interactable);
                Transform beacon = go.transform.Find("Beacon");
                if (beacon != null) beacon.gameObject.layer = 0; // Default: not distance-culled, visible from the strategic view
                go.AddComponent<DropView>().Id = d.Id;
                _drops[d.Id] = go.GetComponent<DropView>();
            }

            // Survivors and colonists
            RemoveMissing(_colonists, w.Colonists);
            foreach (ColonistState c in w.Colonists.Values)
                if (!_colonists.ContainsKey(c.Id)) _colonists[c.Id] = ColonistView.Create(_colonistsRoot, c, _gc);

            SyncZombieViews();
        }

        private void CreateStructureView(Structure s)
        {
            BuildingDefinition def = _gc.Session.Content.Building(s.DefId);
            if (def == null) return;
            var go = new GameObject($"{s.DefId}{s.Id}");
            go.transform.SetParent(_structuresRoot, false);
            go.transform.SetPositionAndRotation(s.Position.ToUnity(), Quaternion.Euler(0, s.Yaw, 0));
            StructureVisuals.Build(go.transform, def, s.IsComplete ? null : MaterialLibrary.Ghost(StructureVisuals.BlueprintTint), s.IsComplete);
            Shapes.SetLayerRecursive(go, Layers.Structure);
            var view = go.AddComponent<StructureView>();
            view.Id = s.Id;
            view.Def = def;
            view.BuiltComplete = s.IsComplete;
            view.CacheParts();
            if (s.IsComplete && def.BlocksMovement && def.Category != BuildingCategory.WorldProp) AddObstacles(go, def);
            _structures[s.Id] = view;
        }

        /// <summary>Parts lower than this are stepped onto (foundations); parts starting higher are overhead (lintels, roofs).</summary>
        private const float StepHeight = 0.6f, HeadHeight = 1.9f;

        /// <summary>
        /// Carves the NavMesh with one obstacle per solid box part that blocks a walking person.
        /// A single footprint box sealed doorway openings and turned foundations into holes.
        /// </summary>
        private static void AddObstacles(GameObject go, BuildingDefinition def)
        {
            Transform root = go.transform;
            bool any = false;
            foreach (BoxCollider box in go.GetComponentsInChildren<BoxCollider>())
            {
                if (box.isTrigger || box.GetComponent<UnityEngine.AI.NavMeshObstacle>() != null) continue;
                float centerY = root.InverseTransformPoint(box.transform.TransformPoint(box.center)).y;
                float halfY = 0.5f * box.size.y * Mathf.Abs(box.transform.lossyScale.y / root.lossyScale.y);
                if (centerY + halfY < StepHeight || centerY - halfY > HeadHeight) continue;
                var obstacle = box.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = box.size;
                any = true;
            }
            if (any || def.SizeY < StepHeight) return;
            var whole = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            whole.carving = true;
            whole.carveOnlyStationary = true;
            whole.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            whole.center = new Vector3(0, def.SizeY * 0.5f, 0);
            whole.size = new Vector3(def.SizeX, Mathf.Max(0.5f, def.SizeY), def.SizeZ);
        }

        private void SyncZombieViews()
        {
            WorldState w = _gc.Session.World;
            Vector3 player = w.Player.Position.ToUnity();
            var remove = new List<EntityId>();
            foreach (KeyValuePair<EntityId, ZombieView> kv in _zombies)
            {
                if (!w.Zombies.TryGetValue(kv.Key, out ZombieState z) || Vector3.Distance(z.Position.ToUnity(), player) > ZombieViewRadius + 20f)
                    remove.Add(kv.Key);
            }
            foreach (EntityId id in remove)
            {
                if (_zombies[id] != null) Destroy(_zombies[id].gameObject);
                _zombies.Remove(id);
            }
            foreach (ZombieState z in w.Zombies.Values)
            {
                if (_zombies.ContainsKey(z.Id)) continue;
                if (Vector3.Distance(z.Position.ToUnity(), player) > ZombieViewRadius) continue;
                _zombies[z.Id] = ZombieView.Create(_zombiesRoot, z, _gc);
            }
        }

        private static void RemoveMissing<TView, TState>(Dictionary<EntityId, TView> views, Dictionary<EntityId, TState> states) where TView : Component
        {
            List<EntityId> gone = null;
            foreach (EntityId id in views.Keys)
                if (!states.ContainsKey(id)) (gone ?? (gone = new List<EntityId>())).Add(id);
            if (gone == null) return;
            foreach (EntityId id in gone)
            {
                if (views[id] != null) Destroy(views[id].gameObject);
                views.Remove(id);
            }
        }
    }
}
