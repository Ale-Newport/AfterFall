using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Third-person survivor movement: camera-relative walking, running, stamina-limited
    /// sprinting, crouching, jumping and gravity on a CharacterController. The physical
    /// transform is reported to the simulation every frame.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerCharacter : MonoBehaviour
    {
        public const float WalkSpeed = 2.4f;
        public const float RunSpeed = 4.6f;
        public const float SprintSpeed = 7.2f;
        public const float CrouchSpeed = 1.7f;
        public const float JumpHeight = 1.05f;
        public const float Gravity = -22f;
        private const float StandHeight = 1.8f;
        private const float CrouchHeight = 1.15f;

        private GameController _gc;
        private CharacterController _cc;
        private Vector3 _velocity;
        private float _verticalSpeed;
        private bool _crouching;
        private bool _walkToggle;
        private float _yaw;
        private float _faceAimUntil;

        public HumanoidRig Rig { get; private set; }
        public bool IsCrouching => _crouching;
        public bool IsSprinting { get; private set; }
        public float HorizontalSpeed { get; private set; }

        public static PlayerCharacter Create(GameController gc, Vector3 position, float yaw)
        {
            var go = new GameObject("Player") { tag = "Player" };
            go.layer = Layers.Character;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var cc = go.AddComponent<CharacterController>();
            cc.height = StandHeight;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, StandHeight * 0.5f, 0);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.45f;
            cc.skinWidth = 0.04f;
            var p = go.AddComponent<PlayerCharacter>();
            p._gc = gc;
            p._cc = cc;
            p._yaw = yaw;
            p.Rig = HumanoidRig.Create(go.transform, new HumanoidRig.Palette
            {
                Skin = new Color(0.78f, 0.62f, 0.48f),
                Shirt = new Color(0.33f, 0.4f, 0.27f),
                Pants = new Color(0.25f, 0.27f, 0.3f),
                Shoes = new Color(0.2f, 0.15f, 0.1f),
                Accent = new Color(0.42f, 0.32f, 0.22f)
            }, false);
            Shapes.SetLayerRecursive(p.Rig.gameObject, Layers.Character);
            return p;
        }

        public void Teleport(Vector3 position)
        {
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            _velocity = Vector3.zero;
            _verticalSpeed = 0f;
        }

        /// <summary>Turn the body toward the camera aim for a moment (attacking, harvesting).</summary>
        public void FaceAim(float seconds = 0.6f) => _faceAimUntil = Time.time + seconds;

        private void Update()
        {
            if (_gc?.Session == null) return;
            float dt = Time.deltaTime;
            PlayerState state = _gc.Session.Player;
            bool dead = state.Vitals.IsDead;
            Rig.SetDead(dead);
            Rig.SetHeldItem(_gc.Session.EquippedItem);

            Vector2 move = Vector2.zero;
            bool wantSprint = false, jump = false;
            if (!dead && _gc.GameplayInputActive)
            {
                GameInput input = _gc.Input;
                move = input.Move.ReadValue<Vector2>();
                wantSprint = input.Sprint.IsPressed();
                jump = input.Jump.WasPressedThisFrame();
                if (input.Crouch.WasPressedThisFrame()) ToggleCrouch();
            }
            if (move.sqrMagnitude > 1f) move.Normalize();

            Transform cam = _gc.CameraRig.ViewTransform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 wish = fwd * move.y + right * move.x;

            bool moving = wish.sqrMagnitude > 0.01f;
            IsSprinting = moving && wantSprint && !_crouching && _gc.Session.CanSprint && move.y > -0.2f;
            float speed = _crouching ? CrouchSpeed : IsSprinting ? SprintSpeed : _walkToggle ? WalkSpeed : RunSpeed;
            speed *= _gc.Session.SpeedMultiplier;
            Vector3 target = wish * speed;
            float accel = _cc.isGrounded ? 14f : 3f;
            _velocity = Vector3.MoveTowards(_velocity, target, accel * speed * dt + 0.01f);

            if (_cc.isGrounded)
            {
                if (_verticalSpeed < 0f) _verticalSpeed = -2f;
                if (jump && !_crouching && _gc.Session.Player.Vitals.Stamina > 5f)
                {
                    _verticalSpeed = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    _gc.Session.Player.Vitals.Stamina -= 5f;
                }
            }
            _verticalSpeed += Gravity * dt;

            if (!dead) _cc.Move((_velocity + Vector3.up * _verticalSpeed) * dt);
            else if (_cc.enabled) _cc.Move(Vector3.up * _verticalSpeed * dt);

            HorizontalSpeed = new Vector3(_cc.velocity.x, 0, _cc.velocity.z).magnitude;
            if (!dead)
            {
                Vector3 face = Time.time < _faceAimUntil || _gc.Build.IsPlacing ? fwd : _velocity;
                face.y = 0;
                if (face.sqrMagnitude > 0.04f)
                {
                    float targetYaw = Quaternion.LookRotation(face).eulerAngles.y;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, 720f * dt);
                    transform.rotation = Quaternion.Euler(0, _yaw, 0);
                }
            }
            Rig.Animate(HorizontalSpeed, _crouching, _cc.isGrounded, dt);

            if (transform.position.y < -50f) Teleport(_gc.Session.Player.WorldSpawn.ToUnity() + Vector3.up);

            _gc.Session.ReportPlayerTransform(transform.position.ToSim(), _yaw, new PlayerActivity
            {
                Moving = HorizontalSpeed > 0.3f,
                Sprinting = IsSprinting && HorizontalSpeed > 0.3f,
                Crouching = _crouching
            });
        }

        private void ToggleCrouch()
        {
            if (_crouching)
            {
                // Only stand up if there is room above.
                Vector3 bottom = transform.position + Vector3.up * (_cc.radius + 0.05f);
                if (Physics.SphereCast(bottom, _cc.radius * 0.9f, Vector3.up, out _, StandHeight - _cc.radius, Layers.SolidMask, QueryTriggerInteraction.Ignore)) return;
                _crouching = false;
                _cc.height = StandHeight;
            }
            else
            {
                _crouching = true;
                _cc.height = CrouchHeight;
            }
            _cc.center = new Vector3(0, _cc.height * 0.5f, 0);
        }
    }
}
