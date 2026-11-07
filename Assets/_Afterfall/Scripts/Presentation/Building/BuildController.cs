using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Blueprint placement: a ghost preview follows the aim point, snaps to foundations and
    /// walls, rotates, and is validated every frame. Geometric facts are measured with physics
    /// and passed to the domain <see cref="PlacementRules"/>, which decides and explains.
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        private GameController _gc;
        private BuildingDefinition _def;
        private GameObject _ghost;
        private Material _validMat, _invalidMat;
        private float _yaw;
        private Vector3 _position;
        private PlacementProbe _probe = new PlacementProbe();
        private readonly Collider[] _overlaps = new Collider[16];

        public bool IsPlacing => _def != null;
        public BuildingDefinition Current => _def;
        public PlacementResult LastResult { get; private set; }
        public bool HasPosition { get; private set; }

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _validMat = MaterialLibrary.Ghost(StructureVisuals.GhostValid);
            _invalidMat = MaterialLibrary.Ghost(StructureVisuals.GhostInvalid);
        }

        public void Begin(BuildingDefinition def)
        {
            Cancel();
            _def = def;
            _ghost = new GameObject("Ghost_" + def.Id);
            StructureVisuals.Build(_ghost.transform, def, _validMat, false);
            foreach (Collider c in _ghost.GetComponentsInChildren<Collider>()) Destroy(c);
            Shapes.SetLayerRecursive(_ghost, Layers.Ghost);
            _gc.Toast($"Placing {def.Name}: LMB place · R rotate · RMB/Esc cancel", Severity.Info);
        }

        public void Cancel()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _def = null;
            LastResult = null;
        }

        private void Update()
        {
            if (_def == null || _gc?.Session == null) return;
            bool tactical = _gc.CameraRig.TacticalActive;
            GameInput input = _gc.Input;
            if (!_gc.PlacementInputActive) return;

            if ((tactical ? input.TacRotateBuild : input.Rotate).WasPressedThisFrame()) _yaw = (_yaw + (_def.Snap == SnapMode.Free ? 45f : 90f)) % 360f;
            float wheel = tactical ? input.TacZoom.ReadValue<float>() : input.Zoom.ReadValue<float>();
            if (Mathf.Abs(wheel) > 0.01f) _yaw = (_yaw + Mathf.Sign(wheel) * 15f + 360f) % 360f;

            UpdatePlacement(tactical);
            if ((tactical ? input.TacCommand : input.Secondary).WasPressedThisFrame())
            {
                Cancel();
                return;
            }
            if ((tactical ? input.TacSelect : input.Primary).WasPressedThisFrame() && HasPosition) TryPlace();
        }

        private void UpdatePlacement(bool tactical)
        {
            Ray ray = _gc.CameraRig.AimRay(tactical);
            HasPosition = Physics.Raycast(ray, out RaycastHit hit, tactical ? 600f : 40f, Layers.PlacementSurfaceMask, QueryTriggerInteraction.Ignore);
            if (!HasPosition)
            {
                _ghost.SetActive(false);
                LastResult = null;
                return;
            }
            _ghost.SetActive(true);
            Structure support = SupportFromHit(hit);
            Vector3 pos = hit.point;
            float yaw = _yaw;
            bool onSupport = false;

            switch (_def.Snap)
            {
                case SnapMode.Foundation:
                    pos = SnapFoundation(pos, ref yaw);
                    break;
                case SnapMode.Wall:
                    onSupport = SnapWall(ref pos, ref yaw);
                    break;
                case SnapMode.Roof:
                    onSupport = SnapRoof(ref pos, ref yaw);
                    break;
                default:
                    pos = new Vector3(Mathf.Round(pos.x * 4f) / 4f, pos.y, Mathf.Round(pos.z * 4f) / 4f);
                    if (support != null && IsSupportPiece(support))
                    {
                        onSupport = true;
                        pos.y = support.Position.Y + _gc.Session.Content.Building(support.DefId).SizeY;
                    }
                    else pos.y = _gc.GroundHeight(pos);
                    break;
            }

            _position = pos;
            _ghost.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            _yawApplied = yaw;
            BuildProbe(pos, yaw, onSupport, tactical);
            LastResult = _gc.Session.EvaluatePlacement(_def.Id, _probe);
            if (LastResult.Valid && DuplicateAt(pos)) LastResult.Reasons.Add("Something is already planned here.");
            bool valid = LastResult.Valid;
            foreach (Renderer r in _ghost.GetComponentsInChildren<Renderer>()) r.sharedMaterial = valid ? _validMat : _invalidMat;
        }

        private float _yawApplied;

        private void TryPlace()
        {
            if (LastResult == null || !LastResult.Valid)
            {
                _gc.Toast(LastResult == null ? "No valid surface." : string.Join(" ", LastResult.Reasons), Severity.Warning);
                return;
            }
            OpResult r = _gc.Session.PlaceBlueprint(_def.Id, _position.ToSim(), _yawApplied, _probe, out Structure s);
            if (!r.Ok)
            {
                _gc.Toast(r.Message, Severity.Warning);
                return;
            }
            List<ItemAmount> missing = _gc.Session.Construction.RemainingCost(s);
            _gc.Toast(missing.Count > 0 ? $"{_def.Name} blueprint placed — bring the materials and hold E to build." : $"{_def.Name} blueprint placed — hold E to build.", Severity.Success);
        }

        private void BuildProbe(Vector3 pos, float yaw, bool onSupport, bool tactical)
        {
            WorldLayout layout = _gc.Session.World.Layout;
            _probe = new PlacementProbe
            {
                InsideMap = layout.IsInsideMap(pos.x, pos.z, 3f),
                OnSupport = onSupport,
                HasGround = true,
                SlopeDegrees = onSupport ? 0f : FootprintSlope(pos, yaw),
                InWater = !onSupport && layout.HeightAt(pos.x, pos.z) < layout.WaterLevel + 0.1f,
                WaterNearby = WaterNear(layout, pos, 6f),
                Distance = tactical ? 0f : Vector3.Distance(_gc.Player.transform.position, pos),
                MaxDistance = 12f
            };

            Vector3 half = new Vector3(_def.SizeX, Mathf.Max(0.2f, _def.SizeY), _def.SizeZ) * 0.5f;
            half = new Vector3(Mathf.Max(0.05f, half.x - 0.12f), Mathf.Max(0.05f, half.y - 0.08f), Mathf.Max(0.05f, half.z - 0.12f));
            Vector3 center = pos + Vector3.up * (half.y + 0.1f);
            int n = Physics.OverlapBoxNonAlloc(center, half, _overlaps, Quaternion.Euler(0, yaw, 0), Layers.PlacementBlockerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _overlaps[i];
                if (c.GetComponentInParent<PlayerCharacter>() != null && _def.Snap != SnapMode.Free) continue;
                EntityView view = c.GetComponentInParent<EntityView>();
                if (view is StructureView sv && sv.Def != null && sv.Def.ProvidesSupport && onSupport) continue;
                _probe.Blocked = true;
                _probe.BlockedBy = Describe(c);
                break;
            }
        }

        private string Describe(Collider c)
        {
            if (c.GetComponentInParent<PlayerCharacter>() != null) return "you";
            if (c.GetComponentInParent<ZombieView>() != null) return "an infected";
            if (c.GetComponentInParent<ResourceNodeView>() is ResourceNodeView n) return n.Def.Name;
            if (c.GetComponentInParent<StructureView>() is StructureView s) return s.Def.Name;
            return "scenery";
        }

        private float FootprintSlope(Vector3 pos, float yaw)
        {
            WorldLayout l = _gc.Session.World.Layout;
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            float hx = _def.SizeX * 0.5f, hz = _def.SizeZ * 0.5f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 c in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), Vector3.zero })
            {
                Vector3 p = pos + r * c;
                float h = l.HeightAt(p.x, p.z);
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            float span = Mathf.Max(0.5f, Mathf.Max(_def.SizeX, _def.SizeZ));
            return Mathf.Max(l.Height.SlopeDegrees(pos.x, pos.z), Mathf.Atan2(max - min, span) * Mathf.Rad2Deg);
        }

        private static bool WaterNear(WorldLayout l, Vector3 p, float radius)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                if (l.IsWater(p.x + Mathf.Cos(a) * radius, p.z + Mathf.Sin(a) * radius)) return true;
                if (l.IsWater(p.x + Mathf.Cos(a) * radius * 0.5f, p.z + Mathf.Sin(a) * radius * 0.5f)) return true;
            }
            return false;
        }

        private Structure SupportFromHit(RaycastHit hit)
        {
            StructureView v = hit.collider.GetComponentInParent<StructureView>();
            if (v == null) return null;
            _gc.Session.World.Structures.TryGetValue(v.Id, out Structure s);
            return s;
        }

        private bool IsSupportPiece(Structure s)
        {
            BuildingDefinition d = _gc.Session.Content.Building(s.DefId);
            return d != null && d.ProvidesSupport;
        }

        private IEnumerable<Structure> Nearby(Vector3 p, float radius, System.Func<BuildingDefinition, bool> filter)
        {
            foreach (Structure s in _gc.Session.World.Structures.Values)
            {
                BuildingDefinition d = _gc.Session.Content.Building(s.DefId);
                if (d == null || !filter(d)) continue;
                if (Vector3.Distance(s.Position.ToUnity(), p) <= radius) yield return s;
            }
        }

        private Vector3 SnapFoundation(Vector3 pos, ref float yaw)
        {
            Structure best = null;
            float bestD = 4.6f;
            foreach (Structure s in Nearby(pos, 6f, d => d.Snap == SnapMode.Foundation))
            {
                float d = Vector3.Distance(Flat(s.Position.ToUnity()), Flat(pos));
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            if (best == null)
            {
                Vector3 g = new Vector3(Mathf.Round(pos.x * 2f) / 2f, 0, Mathf.Round(pos.z * 2f) / 2f);
                g.y = MaxGround(g, yaw) - 0.05f;
                return g;
            }
            yaw = best.Yaw;
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            Vector3 local = Quaternion.Inverse(r) * (pos - best.Position.ToUnity());
            float size = _def.SizeX;
            Vector3 offset = Mathf.Abs(local.x) > Mathf.Abs(local.z) ? new Vector3(Mathf.Sign(local.x) * size, 0, 0) : new Vector3(0, 0, Mathf.Sign(local.z) * size);
            return best.Position.ToUnity() + r * offset;
        }

        private float MaxGround(Vector3 c, float yaw)
        {
            WorldLayout l = _gc.Session.World.Layout;
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            float hx = _def.SizeX * 0.5f, hz = _def.SizeZ * 0.5f;
            float max = float.MinValue;
            foreach (Vector3 o in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), Vector3.zero })
            {
                Vector3 p = c + r * o;
                max = Mathf.Max(max, l.HeightAt(p.x, p.z));
            }
            return max;
        }

        private bool SnapWall(ref Vector3 pos, ref float yaw)
        {
            Structure best = null;
            Vector3 bestEdge = pos;
            float bestYaw = yaw, bestD = 2.6f;
            Vector3 p = pos;
            foreach (Structure s in Nearby(pos, 5f, d => d.Snap == SnapMode.Foundation))
            {
                BuildingDefinition d = _gc.Session.Content.Building(s.DefId);
                Quaternion r = Quaternion.Euler(0, s.Yaw, 0);
                float top = s.Position.Y + d.SizeY;
                float h = d.SizeX * 0.5f;
                var edges = new[] { (new Vector3(0, 0, h), 0f), (new Vector3(0, 0, -h), 180f), (new Vector3(h, 0, 0), 90f), (new Vector3(-h, 0, 0), 270f) };
                foreach ((Vector3 local, float addYaw) in edges)
                {
                    Vector3 e = s.Position.ToUnity() + r * local;
                    e.y = top;
                    float dist = Vector3.Distance(Flat(e), Flat(p));
                    if (dist >= bestD) continue;
                    bestD = dist;
                    bestEdge = e;
                    bestYaw = s.Yaw + addYaw;
                    best = s;
                }
            }
            if (best == null) return false;
            pos = bestEdge;
            yaw = bestYaw;
            return true;
        }

        private bool SnapRoof(ref Vector3 pos, ref float yaw)
        {
            Structure best = null;
            float bestD = 3.2f;
            foreach (Structure s in Nearby(pos, 6f, d => d.Snap == SnapMode.Foundation))
            {
                float d = Vector3.Distance(Flat(s.Position.ToUnity()), Flat(pos));
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            if (best == null) return false;
            BuildingDefinition fd = _gc.Session.Content.Building(best.DefId);
            pos = best.Position.ToUnity() + Vector3.up * (fd.SizeY + 3f);
            yaw = best.Yaw;
            // A roof needs at least one wall standing on its foundation.
            foreach (Structure w in Nearby(pos, 6f, d => d.Snap == SnapMode.Wall))
            {
                if (Vector3.Distance(Flat(w.Position.ToUnity()), Flat(best.Position.ToUnity())) <= fd.SizeX * 0.5f + 0.2f) return true;
            }
            return false;
        }

        private bool DuplicateAt(Vector3 pos)
        {
            foreach (Structure s in _gc.Session.World.Structures.Values)
            {
                if (s.DefId != _def.Id) continue;
                if (Vector3.Distance(s.Position.ToUnity(), pos) < 0.3f && Mathf.Abs(Mathf.DeltaAngle(s.Yaw, _yawApplied)) < 5f) return true;
            }
            return false;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
