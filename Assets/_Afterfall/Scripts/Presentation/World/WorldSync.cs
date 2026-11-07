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
                Color c = d.IsBelongings ? new Color(0.25f, 0.45f, 0.75f) : new Color(0.45f, 0.36f, 0.24f);
                Shapes.Part(go.transform, "Sack", MeshFactory.Blob(3), MaterialLibrary.Lit(c), new Vector3(0, 0.2f, 0), new Vector3(0.7f, 0.5f, 0.6f));
                if (d.IsBelongings)
                    Shapes.Part(go.transform, "Beacon", MeshFactory.Cylinder(6), MaterialLibrary.Emissive(new Color(0.4f, 0.7f, 1f), new Color(0.3f, 0.6f, 1f) * 2f), Vector3.zero, new Vector3(0.12f, 30f, 0.12f), null, false);
                NodeVisuals.Collider(go.transform, new Vector3(0, 0.4f, 0), new Vector3(1f, 0.8f, 1f), true);
                Shapes.SetLayerRecursive(go, Layers.Interactable);
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
            if (s.IsComplete && def.BlocksMovement && def.Category != BuildingCategory.WorldProp)
            {
                var obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0, def.SizeY * 0.5f, 0);
                obstacle.size = new Vector3(def.SizeX, Mathf.Max(0.5f, def.SizeY), def.SizeZ);
            }
            _structures[s.Id] = view;
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
