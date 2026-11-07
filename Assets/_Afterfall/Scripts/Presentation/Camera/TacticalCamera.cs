using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Elevated strategic view: pan (WASD / middle-drag), rotate (Q/E), zoom (wheel). Moves the
    /// GameObject of a CinemachineCamera without procedural components, so Cinemachine uses
    /// this transform directly and blends to/from the third-person camera.
    /// </summary>
    public sealed class TacticalCamera : MonoBehaviour
    {
        private GameController _gc;
        private Vector3 _focus;
        private float _yaw;
        private float _distance = 55f;
        private const float Pitch = 58f;

        public void Initialize(GameController gc)
        {
            _gc = gc;
        }

        public void Focus(Vector3 point, float yaw)
        {
            _focus = point;
            _yaw = yaw;
            Apply();
        }

        private void LateUpdate()
        {
            if (_gc == null || !_gc.CameraRig.TacticalActive) return;
            float dt = Time.unscaledDeltaTime;
            GameInput input = _gc.Input;
            if (_gc.TacticalInputActive)
            {
                Vector2 pan = input.TacPan.ReadValue<Vector2>();
                float speed = (input.TacFast.IsPressed() ? 2.5f : 1f) * Mathf.Lerp(18f, 70f, Mathf.InverseLerp(20f, 140f, _distance));
                Quaternion yawRot = Quaternion.Euler(0, _yaw, 0);
                _focus += yawRot * new Vector3(pan.x, 0, pan.y) * speed * dt;
                if (input.TacDrag.IsPressed())
                {
                    Vector2 d = input.TacLook.ReadValue<Vector2>();
                    _focus -= yawRot * new Vector3(d.x, 0, d.y) * (_distance * 0.0025f);
                }
                _yaw += input.TacRotate.ReadValue<float>() * 90f * dt;
                float zoom = input.TacZoom.ReadValue<float>();
                if (Mathf.Abs(zoom) > 0.01f && !_gc.Build.IsPlacing) _distance = Mathf.Clamp(_distance * (zoom > 0 ? 0.88f : 1.14f), 18f, 160f);
            }
            float s = WorldLayout.WorldSize;
            _focus.x = Mathf.Clamp(_focus.x, 0f, s);
            _focus.z = Mathf.Clamp(_focus.z, 0f, s);
            _focus.y = _gc.GroundHeight(_focus);
            Apply();
        }

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(Pitch, _yaw, 0);
            transform.SetPositionAndRotation(_focus + rot * new Vector3(0, 0, -_distance), rot);
        }
    }
}
