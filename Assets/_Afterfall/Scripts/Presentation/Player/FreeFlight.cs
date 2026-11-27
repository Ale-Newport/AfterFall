using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Creative/debug free flight (F7): a spectator camera that carries the survivor's body with
    /// it, hidden, so no walking or running animation ever plays. No gravity, no collision, no
    /// stamina; WASD along the view, Space up, Ctrl/C down, Shift boost, mouse wheel to change
    /// speed. Turning it off lands the survivor on safe ground below the camera.
    /// </summary>
    public sealed class FreeFlight : MonoBehaviour
    {
        public const float MinSpeed = 2f;
        public const float MaxSpeed = 600f;
        public const float DefaultSpeed = 24f;
        public const float BoostFactor = 4f;

        private GameController _gc;
        private CharacterController _cc;
        private Vector3 _velocity;
        private Vector3 _scriptedInput;
        private bool _scriptedBoost;
        private bool _scripted;

        public bool Active { get; private set; }
        public float Speed { get; private set; } = DefaultSpeed;

        /// <summary>Speed of the last frame (m/s), for the HUD.</summary>
        public float CurrentSpeed => _velocity.magnitude;

        public void Initialize(GameController gc, CharacterController cc)
        {
            _gc = gc;
            _cc = cc;
        }

        /// <summary>Starts or stops flying. Returns a player-facing reason when it cannot.</summary>
        public bool Toggle(out string message) => SetActive(!Active, out message);

        public bool SetActive(bool on, out string message)
        {
            message = null;
            if (on == Active) return true;
            if (on)
            {
                if (!_gc.Session.Creative.Enabled)
                {
                    message = "Free flight needs Creative mode or developer tools (F6).";
                    return false;
                }
                if (_gc.IsDead)
                {
                    message = "You are dead.";
                    return false;
                }
                if (_gc.Build.IsPlacing) _gc.Build.Cancel();
                Active = true;
                _cc.enabled = false;
                _velocity = Vector3.zero;
                // Lift off a little so the camera starts above head height.
                transform.position += Vector3.up * 1.7f;
                _gc.Player.Rig.gameObject.SetActive(false);
                _gc.CameraRig.SetFlight(true);
                message = $"Free flight on — WASD, Space/Ctrl up/down, Shift boost, wheel speed ({Speed:0} m/s). F7 to land.";
                return true;
            }

            Vector3 here = transform.position;
            if (!SafeGround.TryFind(_gc, here, here.y, 40f, out Vector3 spot)
                && !SafeGround.TryFind(_gc, here, here.y + 400f, 120f, out spot))
            {
                message = "No safe ground below to land on. Fly somewhere else first.";
                return false;
            }
            Active = false;
            _velocity = Vector3.zero;
            _scripted = false;
            _gc.Player.Rig.gameObject.SetActive(true);
            _gc.CameraRig.SetFlight(false);
            _gc.Player.Teleport(spot);
            message = "Landed.";
            return true;
        }

        /// <summary>Ends flight wherever the body is (death, leaving the game): on safe ground if there is any, else on the terrain.</summary>
        public void ForceLand()
        {
            if (!Active) return;
            if (SetActive(false, out _)) return;
            Vector3 p = transform.position;
            Active = false;
            _scripted = false;
            _gc.Player.Rig.gameObject.SetActive(true);
            _gc.CameraRig.SetFlight(false);
            _gc.Player.Teleport(new Vector3(p.x, _gc.GroundHeight(p) + 0.1f, p.z));
        }

        /// <summary>Moves the flight camera to a point (teleporting while flying).</summary>
        public void MoveTo(Vector3 position)
        {
            transform.position = position;
            _velocity = Vector3.zero;
        }

        /// <summary>Drives flight without a keyboard (the scripted tour): local direction (x right, y up, z forward) and boost.</summary>
        public void SetScriptedInput(Vector3 direction, bool boost)
        {
            _scripted = direction.sqrMagnitude > 0f || boost;
            _scriptedInput = direction;
            _scriptedBoost = boost;
        }

        public void SetSpeed(float speed) => Speed = Mathf.Clamp(speed, MinSpeed, MaxSpeed);

        private void Update()
        {
            if (!Active || _gc?.Session == null) return;
            if (_gc.IsDead)
            {
                ForceLand();
                return;
            }
            // A long frame (a screenshot, a hitch) must not throw the camera hundreds of metres.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Vector3 input = Vector3.zero;
            bool boost = false;
            if (_scripted)
            {
                input = _scriptedInput;
                boost = _scriptedBoost;
            }
            else if (_gc.GameplayInputActive)
            {
                GameInput i = _gc.Input;
                Vector2 move = i.Move.ReadValue<Vector2>();
                float up = (i.Jump.IsPressed() ? 1f : 0f) - (i.Crouch.IsPressed() ? 1f : 0f);
                input = new Vector3(move.x, up, move.y);
                boost = i.Sprint.IsPressed();
                float wheel = i.Zoom.ReadValue<float>();
                if (Mathf.Abs(wheel) > 0.01f) SetSpeed(Speed * (wheel > 0f ? 1.25f : 0.8f));
            }
            if (input.sqrMagnitude > 1f) input.Normalize();

            Transform cam = _gc.CameraRig.ViewTransform;
            Vector3 wish = cam.right * input.x + Vector3.up * input.y + cam.forward * input.z;
            float speed = Speed * (boost ? BoostFactor : 1f);
            // Quick to respond at walking pace, smooth at aircraft speeds.
            float response = 1f - Mathf.Exp(-6f * dt);
            _velocity = Vector3.Lerp(_velocity, wish * speed, response);

            Vector3 p = transform.position + _velocity * dt;
            WorldLayout layout = _gc.Session.World.Layout;
            const float edge = 4f;
            p.x = Mathf.Clamp(p.x, edge, layout.Size - edge);
            p.z = Mathf.Clamp(p.z, edge, layout.Size - edge);
            // No-clip through buildings and trees, but never below the ground or into the sky's far plane.
            p.y = Mathf.Clamp(p.y, layout.HeightAt(p.x, p.z) + 0.6f, layout.MaxHeight + 600f);
            transform.position = p;

            _gc.Session.ReportPlayerTransform(p.ToSim(), cam.eulerAngles.y, new PlayerActivity { Flying = true });
        }
    }
}
