using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Cinemachine 3 camera setup: a third-person shoulder camera with obstacle avoidance and
    /// zoom, and an elevated strategic camera. The brain blends smoothly between them.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        private GameController _gc;
        private Camera _camera;
        private CinemachineBrain _brain;
        private CinemachineCamera _thirdPerson;
        private CinemachineThirdPersonFollow _follow;
        private CinemachineCamera _tacticalCam;
        private Transform _target;
        private float _yaw, _pitch = 12f;
        private float _distance = 4.2f;
        private float _shake;

        public Camera Camera => _camera;
        public Transform ViewTransform => _camera.transform;
        public TacticalCamera Tactical { get; private set; }
        public bool TacticalActive { get; private set; }

        public static CameraRig Create(GameController gc, Transform player, AppSettings settings)
        {
            var root = new GameObject("CameraRig");
            var rig = root.AddComponent<CameraRig>();
            rig._gc = gc;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(root.transform, false);
            rig._camera = camGo.AddComponent<Camera>();
            rig._camera.nearClipPlane = 0.1f;
            rig._camera.farClipPlane = 650f;
            var cull = new float[32];
            cull[Layers.Resource] = 260f;
            cull[Layers.Interactable] = 120f;
            cull[Layers.Zombie] = 160f;
            rig._camera.layerCullDistances = cull;
            camGo.AddComponent<AudioListener>();
            rig._brain = camGo.AddComponent<CinemachineBrain>();
            rig._brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.9f);

            rig._target = new GameObject("CameraTarget").transform;
            rig._target.SetParent(root.transform, false);
            rig._target.position = player.position + Vector3.up * 1.6f;
            rig._yaw = player.eulerAngles.y;

            var tpGo = new GameObject("ThirdPersonCamera");
            tpGo.transform.SetParent(root.transform, false);
            rig._thirdPerson = tpGo.AddComponent<CinemachineCamera>();
            rig._thirdPerson.Follow = rig._target;
            rig._thirdPerson.Priority = 10;
            rig._thirdPerson.Lens = LensSettings.Default;
            rig._follow = tpGo.AddComponent<CinemachineThirdPersonFollow>();
            rig._follow.ShoulderOffset = new Vector3(0.55f, 0.2f, 0f);
            rig._follow.VerticalArmLength = 0.25f;
            rig._follow.CameraSide = 1f;
            rig._follow.CameraDistance = rig._distance;
            rig._follow.Damping = new Vector3(0.1f, 0.25f, 0.3f);
            CinemachineThirdPersonFollow.ObstacleSettings obstacles = rig._follow.AvoidObstacles;
            obstacles.Enabled = true;
            obstacles.CollisionFilter = Layers.Mask(Layers.Terrain, Layers.Structure, Layers.Prop) | 1;
            obstacles.IgnoreTag = "Player";
            obstacles.CameraRadius = 0.25f;
            obstacles.DampingIntoCollision = 0.05f;
            obstacles.DampingFromCollision = 0.4f;
            rig._follow.AvoidObstacles = obstacles;

            var tacGo = new GameObject("StrategicCamera");
            tacGo.transform.SetParent(root.transform, false);
            rig._tacticalCam = tacGo.AddComponent<CinemachineCamera>();
            rig._tacticalCam.Priority = 0;
            rig._tacticalCam.Lens = LensSettings.Default;
            rig.Tactical = tacGo.AddComponent<TacticalCamera>();
            rig.Tactical.Initialize(gc);
            rig.ApplySettings(settings);
            return rig;
        }

        public void ApplySettings(AppSettings s)
        {
            LensSettings lens = _thirdPerson.Lens;
            lens.FieldOfView = s.FieldOfView;
            lens.FarClipPlane = 650f;
            lens.NearClipPlane = 0.1f;
            _thirdPerson.Lens = lens;
            LensSettings tl = _tacticalCam.Lens;
            tl.FieldOfView = 50f;
            tl.FarClipPlane = 900f;
            tl.NearClipPlane = 0.5f;
            _tacticalCam.Lens = tl;
        }

        public void SetTactical(bool on, Vector3 focus)
        {
            TacticalActive = on;
            if (on) Tactical.Focus(focus, _yaw);
            _tacticalCam.Priority = on ? 20 : 0;
            _thirdPerson.Priority = on ? 0 : 10;
        }

        public void Shake(float amount)
        {
            if (_gc.Settings.CameraShake) _shake = Mathf.Max(_shake, amount);
        }

        private void LateUpdate()
        {
            if (_gc?.Player == null) return;
            float dt = Time.deltaTime;
            Vector3 anchor = _gc.Player.transform.position + Vector3.up * (_gc.Player.IsCrouching ? 1.15f : 1.6f);
            _target.position = Vector3.Lerp(_target.position, anchor, 1f - Mathf.Exp(-20f * dt));

            if (!TacticalActive && _gc.LookInputActive && Mouse.current != null)
            {
                Vector2 look = _gc.Input.Look.ReadValue<Vector2>() * (0.09f * _gc.Settings.MouseSensitivity);
                _yaw += look.x;
                _pitch += _gc.Settings.InvertY ? look.y : -look.y;
                _pitch = Mathf.Clamp(_pitch, -40f, 70f);
                float zoom = _gc.Input.Zoom.ReadValue<float>();
                if (Mathf.Abs(zoom) > 0.01f && !_gc.Build.IsPlacing) _distance = Mathf.Clamp(_distance - Mathf.Sign(zoom) * 0.6f, 1.8f, 9f);
            }
            _follow.CameraDistance = Mathf.Lerp(_follow.CameraDistance, _distance, 1f - Mathf.Exp(-10f * dt));

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - dt * 2.5f);
                rot *= Quaternion.Euler(Random.Range(-1f, 1f) * _shake * 2f, Random.Range(-1f, 1f) * _shake * 2f, 0);
            }
            _target.rotation = rot;
        }

        /// <summary>Ray through the screen centre (third person) or the mouse cursor (strategic / menus).</summary>
        public Ray AimRay(bool useCursor)
        {
            if (useCursor && Mouse.current != null) return _camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            return _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }
    }
}
