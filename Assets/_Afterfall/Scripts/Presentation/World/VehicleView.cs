using Afterfall.Core;
using Afterfall.Domain.Events;
using Afterfall.Domain.Vehicles;
using UnityEngine;
using UnityEngine.AI;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>
    /// A vehicle in the scene: its model, hinged doors and turning wheels, a rigidbody on four
    /// wheel colliders, and the driving controls while the survivor is at the wheel. The domain
    /// <see cref="VehicleState"/> is authoritative; this reports where the body goes (driving
    /// telemetry, resting place) and what it hits (crashes, the infected). Far from the survivor
    /// the body is kinematic and still.
    /// </summary>
    public sealed class VehicleView : EntityView, IInteractable
    {
        private const float ActiveRange = 90f;
        private const float DoorOpenDegrees = 62f;

        private GameController _gc;
        private Rigidbody _rb;
        private readonly WheelCollider[] _wheels = new WheelCollider[4];
        private readonly Transform[] _wheelVisuals = new Transform[4];
        private Transform[] _doors = new Transform[0];
        private float[] _doorSides = new float[0];
        private float[] _doorAngles = new float[0];
        private NavMeshObstacle _obstacle;
        private Vector3 _lastPos;
        private float _restReport, _crashCooldown, _upsideDown;
        private float _doorHold;
        private Collider[] _colliders;

        public VehicleDefinition Def { get; private set; }
        public Transform Seat { get; private set; }

        /// <summary>Throttle (−1 reverse … 1), steering (−1 … 1) and handbrake from the driver this frame.</summary>
        public float Throttle, Steer;
        public bool Handbrake;

        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;
        public float ForwardSpeed => _rb != null ? Vector3.Dot(_rb.linearVelocity, transform.forward) : 0f;
        public bool Driven => _gc != null && _gc.Session.Player.VehicleId == Id;

        public static VehicleView Create(GameController gc, VehicleState v, VehicleDefinition def, Transform parent)
        {
            var go = new GameObject($"Vehicle_{def.Id}{v.Id}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(v.Position.ToUnity() + Vector3.up * 0.05f, Quaternion.Euler(0f, v.Yaw, 0f));
            go.layer = Layers.Vehicle;
            var view = go.AddComponent<VehicleView>();
            view._gc = gc;
            view.Id = v.Id;
            view.Def = def;
            view.Build(v);
            return view;
        }

        private static Material PaintMat => MaterialLibrary.Surface(Art.Surface.PaintedMetal, new Color(0.82f, 0.82f, 0.82f), 0.45f, 0.1f);
        private static Material TrimMat => MaterialLibrary.Surface(Art.Surface.Metal, new Color(0.6f, 0.6f, 0.6f), 0.4f, 0.6f);
        private static Material GlassMat => MaterialLibrary.Surface(Art.Surface.Glass, new Color(0.14f, 0.17f, 0.19f), 0.92f);
        private static Material RubberMat => MaterialLibrary.Surface(Art.Surface.Rubber, new Color(0.14f, 0.13f, 0.13f));

        private void Build(VehicleState v)
        {
            Color paint = new Color(0.4f, 0.4f, 0.42f);
            if (Def.Paints != null && Def.Paints.Length > 0) ColorUtility.TryParseHtmlString(Def.Paints[Mathf.Abs(v.Paint) % Def.Paints.Length], out paint);
            VehicleMeshes m = MeshFactory.Vehicle(Def.Visual, paint, Def.WheelRadius);
            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            Shapes.Mesh(model, "Body", m.Body[0], PaintMat, Vector3.zero);
            Shapes.Mesh(model, "Trim", m.Body[1], TrimMat, Vector3.zero);
            Shapes.Mesh(model, "Glass", m.Body[2], GlassMat, Vector3.zero);
            Shapes.Mesh(model, "Interior", m.Body[3], RubberMat, Vector3.zero);

            _doors = new Transform[m.Doors.Count];
            _doorSides = new float[m.Doors.Count];
            _doorAngles = new float[m.Doors.Count];
            for (int i = 0; i < m.Doors.Count; i++)
            {
                DoorMesh d = m.Doors[i];
                var door = new GameObject("Door" + i).transform;
                door.SetParent(model, false);
                door.localPosition = d.Hinge;
                Shapes.Mesh(door, "Panel", d.Paint, PaintMat, Vector3.zero);
                if (d.Glass != null && d.Glass.vertexCount > 0) Shapes.Mesh(door, "Window", d.Glass, GlassMat, Vector3.zero);
                var col = new GameObject("DoorCollider");
                col.transform.SetParent(door, false);
                col.layer = Layers.Vehicle;
                var box = col.AddComponent<BoxCollider>();
                float len = d.Paint.bounds.size.z;
                box.center = new Vector3(0f, 0.1f, -len * 0.5f);
                box.size = new Vector3(0.08f, 0.8f, len * 0.9f);
                _doors[i] = door;
                _doorSides[i] = d.Side;
            }

            float[] zs = { Def.WheelFrontZ, Def.WheelFrontZ, Def.WheelRearZ, Def.WheelRearZ };
            float[] xs = { -Def.WheelHalfTrack, Def.WheelHalfTrack, -Def.WheelHalfTrack, Def.WheelHalfTrack };
            for (int i = 0; i < 4; i++)
            {
                var wgo = new GameObject("WheelCollider" + i);
                wgo.transform.SetParent(transform, false);
                wgo.transform.localPosition = new Vector3(xs[i], Def.WheelRadius + 0.18f, zs[i]);
                wgo.layer = Layers.Vehicle;
                WheelCollider w = wgo.AddComponent<WheelCollider>();
                w.radius = Def.WheelRadius;
                w.mass = Def.Mass * 0.015f;
                w.suspensionDistance = 0.24f;
                w.forceAppPointDistance = 0.1f;
                JointSpring spring = w.suspensionSpring;
                spring.spring = Def.Mass * 26f;
                spring.damper = Def.Mass * 2.6f;
                spring.targetPosition = 0.45f;
                w.suspensionSpring = spring;
                WheelFrictionCurve f = w.forwardFriction;
                f.stiffness = 1.6f;
                w.forwardFriction = f;
                WheelFrictionCurve s = w.sidewaysFriction;
                s.stiffness = 2.1f;
                w.sidewaysFriction = s;
                _wheels[i] = w;
                var vis = new GameObject("Wheel" + i).transform;
                vis.SetParent(model, false);
                vis.localPosition = wgo.transform.localPosition;
                Shapes.Mesh(vis, "Tyre", m.WheelRubber, RubberMat, Vector3.zero);
                Shapes.Mesh(vis, "Rim", m.WheelRim, TrimMat, Vector3.zero);
                _wheelVisuals[i] = vis;
            }

            // Body colliders: the lower body and the cabin (a quad bike is one low box).
            if (Def.Category == VehicleCategory.Atv)
                Box("Body", new Vector3(0f, 0.62f, 0f), new Vector3(Def.SizeX * 0.9f, 0.5f, Def.SizeZ * 0.9f));
            else
            {
                Box("Body", new Vector3(0f, 0.68f, 0f), new Vector3(Def.SizeX, 0.62f, Def.SizeZ * 0.95f));
                Box("Cabin", new Vector3(0f, 1.18f, Def.Category == VehicleCategory.Pickup ? 0.15f : -0.2f), new Vector3(Def.SizeX * 0.86f, 0.5f, Def.Category == VehicleCategory.Pickup ? 1.5f : 2.2f));
            }

            Seat = new GameObject("Seat").transform;
            Seat.SetParent(transform, false);
            Seat.localPosition = m.Seat;

            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.mass = Def.Mass;
            _rb.linearDamping = 0.05f;
            _rb.angularDamping = 0.6f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.centerOfMass = new Vector3(0f, Def.Category == VehicleCategory.Atv ? 0.35f : 0.45f, 0.05f);

            _obstacle = gameObject.AddComponent<NavMeshObstacle>();
            _obstacle.shape = NavMeshObstacleShape.Box;
            _obstacle.center = new Vector3(0f, 0.7f, 0f);
            _obstacle.size = new Vector3(Def.SizeX + 0.2f, 1.4f, Def.SizeZ + 0.2f);
            _obstacle.carving = true;
            _obstacle.carveOnlyStationary = true;

            _colliders = GetComponentsInChildren<Collider>(true);
            Shapes.SetLayerRecursive(gameObject, Layers.Vehicle);
            _lastPos = transform.position;
            SetActivePhysics(false);
        }

        private void Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>Near the survivor (or driven) the body is simulated; far away it is kinematic and still.</summary>
        private void SetActivePhysics(bool on)
        {
            if (_rb == null || _rb.isKinematic == !on) return;
            _rb.isKinematic = !on;
            _rb.collisionDetectionMode = on ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            foreach (WheelCollider w in _wheels) w.enabled = on;
        }

        /// <summary>Puts the body at a saved or moved transform (loading, creative moves).</summary>
        public void Place(Vec3 position, float yaw)
        {
            _rb.position = position.ToUnity() + Vector3.up * 0.05f;
            _rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(_rb.position, _rb.rotation);
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _lastPos = transform.position;
        }

        private void Update()
        {
            // (Unity's null check: the controller may already be destroyed while a reload replaces it.)
            if (_gc == null || _gc.Player == null || _gc.Session == null || !_gc.Session.World.Vehicles.TryGetValue(Id, out VehicleState v)) return;
            float dt = Time.deltaTime;
            bool driven = Driven;
            float dist = Vector3.Distance(_gc.Player.transform.position, transform.position);
            SetActivePhysics(driven || dist < ActiveRange);

            // Doors: the driver's door swings open while someone gets in or out, others follow the saved state.
            _doorHold -= dt;
            for (int i = 0; i < _doors.Length; i++)
            {
                bool open = (v.DoorsOpen & (1 << i)) != 0 || (i == 0 && _doorHold > 0f);
                _doorAngles[i] = Mathf.MoveTowards(_doorAngles[i], open ? DoorOpenDegrees : 0f, 200f * dt);
                _doors[i].localRotation = Quaternion.Euler(0f, -_doorSides[i] * _doorAngles[i], 0f);
            }

            for (int i = 0; i < 4; i++)
            {
                if (!_wheels[i].enabled) continue;
                _wheels[i].GetWorldPose(out Vector3 p, out Quaternion q);
                _wheelVisuals[i].SetPositionAndRotation(p, q);
            }

            if (driven)
            {
                float metres = Vector3.Distance(new Vector3(_lastPos.x, 0f, _lastPos.z), new Vector3(transform.position.x, 0f, transform.position.z));
                _gc.Session.Vehicles.ReportDriving(Id, transform.position.ToSim(), transform.eulerAngles.y, metres, Mathf.Abs(Throttle), dt, Speed);
                _lastPos = transform.position;
                _obstacle.enabled = false;
            }
            else
            {
                _obstacle.enabled = true;
                _restReport -= dt;
                if (_restReport <= 0f && !_rb.isKinematic)
                {
                    // A parked vehicle settling or pushed: keep the saved transform where it really is.
                    _restReport = 0.5f;
                    if ((transform.position - v.Position.ToUnity()).sqrMagnitude > 0.01f)
                        _gc.Session.Vehicles.ReportResting(Id, transform.position.ToSim(), transform.eulerAngles.y);
                }
                _lastPos = transform.position;
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic || _gc == null || _gc.Player == null || _gc.Session == null || !_gc.Session.World.Vehicles.TryGetValue(Id, out VehicleState v)) return;
            bool driven = Driven;
            float fwd = ForwardSpeed;
            float motor = 0f, brake = 0f, steer = 0f;
            bool hand = !driven || Handbrake;
            if (driven)
            {
                float condition = Mathf.Lerp(0.45f, 1f, v.Engine / 100f);
                if (Throttle > 0.05f)
                {
                    if (fwd < -0.6f) brake = Def.BrakeTorque * Throttle;
                    else if (v.EngineOn && fwd * 3.6f < Def.MaxSpeedKmh) motor = Throttle * Def.MotorTorque * condition;
                }
                else if (Throttle < -0.05f)
                {
                    if (fwd > 0.6f) brake = Def.BrakeTorque * -Throttle;
                    else if (v.EngineOn && -fwd * 3.6f < 25f) motor = Throttle * Def.MotorTorque * 0.55f * condition;
                }
                else if (Mathf.Abs(fwd) < 0.4f) brake = Def.BrakeTorque * 0.2f;
                float sens = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(fwd) / 28f));
                steer = Steer * Def.MaxSteerDegrees * sens;
            }
            float grip = Mathf.Lerp(0.6f, 1f, v.Tyres / 100f);
            int driven4 = Def.FourWheelDrive ? 4 : 2;
            for (int i = 0; i < 4; i++)
            {
                WheelCollider w = _wheels[i];
                bool front = i < 2;
                if (front) w.steerAngle = steer;
                // Front-wheel drive for cars, all four for 4x4s.
                bool powered = Def.FourWheelDrive || front;
                w.motorTorque = powered ? motor / driven4 : 0f;
                w.brakeTorque = brake + (hand && !front ? Def.BrakeTorque * 0.8f : 0f) + (!driven ? Def.BrakeTorque * 0.4f : 0f);
                WheelFrictionCurve s = w.sidewaysFriction;
                s.stiffness = 2.1f * grip;
                w.sidewaysFriction = s;
            }
            // Downforce and a gentle self-righting torque keep it planted.
            _rb.AddForce(-transform.up * Mathf.Abs(fwd) * Def.Mass * 0.08f);
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            if (tilt > 25f) _rb.AddTorque(Vector3.Cross(transform.up, Vector3.up) * Def.Mass * 4f * (tilt - 25f) / 45f, ForceMode.Force);
            _upsideDown = tilt > 75f && _rb.linearVelocity.magnitude < 1f ? _upsideDown + Time.fixedDeltaTime : 0f;
            if (_upsideDown > 2.5f)
            {
                // Stuck on its side or roof: set it back on its wheels where it is.
                _upsideDown = 0f;
                _rb.position += Vector3.up * 1.2f;
                _rb.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                _rb.angularVelocity = Vector3.zero;
            }
            _crashCooldown -= Time.fixedDeltaTime;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (_gc == null || _gc.Session == null) return;
            float speed = c.relativeVelocity.magnitude;
            ZombieView z = c.collider.GetComponentInParent<ZombieView>();
            if (z != null)
            {
                OpResult hit = _gc.Session.Vehicles.HitInfected(Id, z.Id, speed);
                if (hit.Ok)
                {
                    z.Rig.Flash();
                    _gc.Audio?.PlayAt("hit_flesh", z.transform.position + Vector3.up, 1f);
                }
                return;
            }
            if (speed < 6f || _crashCooldown > 0f) return;
            _crashCooldown = 0.6f;
            _gc.Session.Vehicles.ReportCrash(Id, speed);
            if (Driven) _gc.CameraRig.Shake(Mathf.Clamp01(speed / 20f));
            _gc.Audio?.PlayAt("swing", c.GetContact(0).point, Mathf.Clamp01(speed / 15f));
        }

        /// <summary>Holds the driver's door open for a moment (getting in or out).</summary>
        public void SwingDriverDoor() => _doorHold = 1.1f;

        /// <summary>Where someone getting out stands: beside the driver's door, or the other side if that is blocked.</summary>
        public bool ExitSpot(out Vector3 spot)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = transform.TransformPoint(new Vector3(side * (Def.SizeX * 0.5f + 0.7f), 0f, Seat.localPosition.z));
                if (SafeGround.TryFind(_gc, p, p.y + 1.6f, 1.6f, out spot)) return true;
            }
            Vector3 behind = transform.TransformPoint(new Vector3(0f, 0f, -(Def.SizeZ * 0.5f + 1.2f)));
            return SafeGround.TryFind(_gc, behind, behind.y + 2f, 6f, out spot);
        }

        /// <summary>The camera ignores the driven vehicle's own colliders.</summary>
        public void SetCameraIgnored(bool ignored)
        {
            foreach (Collider c in _colliders)
                if (c != null) c.gameObject.tag = ignored ? "Player" : "Untagged";
        }

        // ------------------------------------------------------------ interaction

        public bool HoldToInteract => false;
        public bool PrimaryHarvests => false;
        public Vector3 FocusPoint => transform.position + Vector3.up * 0.9f;

        public string Prompt(GameController gc)
        {
            if (!gc.Session.World.Vehicles.TryGetValue(Id, out VehicleState v)) return null;
            string state = $"{Def.Name}: {VehicleRules.Summary(v, Def)}";
            if (v.Locked)
            {
                bool key = VehicleRules.HasKey(gc.Session.Player.Inventory, v);
                return key ? $"[E] Unlock and get in (you have the key to {v.LockId}) · [R] Inspect\n{state}"
                    : $"Locked — needs the key to {v.LockId} · [Hold X] Smash a window (loud) · [R] Inspect\n{state}";
            }
            return $"[E] Get in · [R] Inspect and trunk\n{state}";
        }

        public void Interact(GameController gc) => gc.EnterVehicle(this);

        public void InteractHeld(GameController gc, float dt) { }
    }
}
