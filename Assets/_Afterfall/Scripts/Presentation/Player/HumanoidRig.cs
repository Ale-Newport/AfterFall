using Afterfall.Domain.Items;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedurally assembled and procedurally animated humanoid used for the survivor and
    /// the infected (no external animation assets yet). Limbs pivot at shoulders and hips;
    /// walk/run/crouch/swing/death poses are blended in code.
    /// </summary>
    public sealed class HumanoidRig : MonoBehaviour
    {
        public struct Palette
        {
            public Color Skin, Shirt, Pants, Shoes, Accent;
        }

        private Transform _body, _head, _armL, _armR, _legL, _legR, _hand, _held;
        private float _phase;
        private float _swing = -1f;
        private float _deathT;
        private bool _dead;
        private float _crouch;
        private string _heldItem = "";
        private float _hitFlash;
        private Renderer[] _renderers;

        public Transform Hand => _hand;

        public static HumanoidRig Create(Transform parent, Palette p, bool shambling)
        {
            var root = new GameObject("Rig");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<HumanoidRig>();
            rig.Build(p, shambling);
            return rig;
        }

        private bool _shambling;

        private void Build(Palette p, bool shambling)
        {
            _shambling = shambling;
            Material skin = MaterialLibrary.Lit(p.Skin);
            Material shirt = MaterialLibrary.Lit(p.Shirt);
            Material pants = MaterialLibrary.Lit(p.Pants);
            Material shoes = MaterialLibrary.Lit(p.Shoes);
            Material accent = MaterialLibrary.Lit(p.Accent);

            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _body.localPosition = new Vector3(0, 0.95f, 0);
            Shapes.Part(_body, "Torso", MeshFactory.Cube, shirt, new Vector3(0, 0.33f, 0), new Vector3(0.46f, 0.62f, 0.26f));
            Shapes.Part(_body, "Belt", MeshFactory.Cube, accent, new Vector3(0, 0.03f, 0), new Vector3(0.48f, 0.08f, 0.28f));
            if (!shambling) Shapes.Part(_body, "Backpack", MeshFactory.Cube, accent, new Vector3(0, 0.35f, -0.2f), new Vector3(0.36f, 0.42f, 0.16f));

            _head = new GameObject("Head").transform;
            _head.SetParent(_body, false);
            _head.localPosition = new Vector3(0, 0.66f, 0);
            Shapes.Part(_head, "Skull", MeshFactory.Cube, skin, new Vector3(0, 0.13f, 0), new Vector3(0.24f, 0.27f, 0.25f));
            Shapes.Part(_head, "Hair", MeshFactory.Cube, MaterialLibrary.Lit(shambling ? p.Skin * 0.6f : new Color(0.2f, 0.15f, 0.1f)), new Vector3(0, 0.27f, -0.01f), new Vector3(0.26f, 0.06f, 0.27f));
            Shapes.Part(_head, "Eyes", MeshFactory.Cube, shambling ? MaterialLibrary.Emissive(new Color(0.8f, 0.8f, 0.4f), new Color(0.5f, 0.45f, 0.1f)) : MaterialLibrary.Lit(new Color(0.1f, 0.1f, 0.1f)), new Vector3(0, 0.15f, 0.126f), new Vector3(0.16f, 0.035f, 0.01f), null, false);

            _armL = Limb("ArmL", _body, new Vector3(-0.3f, 0.58f, 0), shirt, skin, 0.13f, 0.62f);
            _armR = Limb("ArmR", _body, new Vector3(0.3f, 0.58f, 0), shirt, skin, 0.13f, 0.62f);
            _legL = Limb("LegL", transform, new Vector3(-0.12f, 0.95f, 0), pants, shoes, 0.17f, 0.92f);
            _legR = Limb("LegR", transform, new Vector3(0.12f, 0.95f, 0), pants, shoes, 0.17f, 0.92f);

            _hand = new GameObject("Hand").transform;
            _hand.SetParent(_armR, false);
            _hand.localPosition = new Vector3(0, -0.6f, 0.04f);
            _renderers = GetComponentsInChildren<Renderer>();
        }

        private static Transform Limb(string name, Transform parent, Vector3 pivot, Material upper, Material end, float thickness, float length)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pivot;
            Shapes.Part(t, "Upper", MeshFactory.Cube, upper, new Vector3(0, -length * 0.4f, 0), new Vector3(thickness, length * 0.8f, thickness));
            Shapes.Part(t, "End", MeshFactory.Cube, end, new Vector3(0, -length * 0.9f, 0.02f), new Vector3(thickness * 1.05f, length * 0.2f, thickness * 1.3f));
            return t;
        }

        /// <summary>Shows a simple model of the held tool/weapon.</summary>
        public void SetHeldItem(ItemDefinition item)
        {
            string id = item?.Id ?? "";
            if (id == _heldItem) return;
            _heldItem = id;
            if (_held != null) Destroy(_held.gameObject);
            _held = null;
            if (item == null) return;
            _held = new GameObject("Held_" + id).transform;
            _held.SetParent(_hand, false);
            _held.localRotation = Quaternion.Euler(90, 0, 0);
            Material handle = MaterialLibrary.Lit(new Color(0.42f, 0.3f, 0.2f));
            Material metal = MaterialLibrary.Lit(new Color(0.65f, 0.67f, 0.7f), 0.4f, 0.6f);
            Color itemColor = Conversions.ParseColor(item.Color, Color.gray);
            ToolType type = item.Tool?.Type ?? ToolType.None;
            switch (type)
            {
                case ToolType.Axe:
                    Shapes.Part(_held, "Handle", MeshFactory.Cube, handle, new Vector3(0, 0.3f, 0), new Vector3(0.05f, 0.7f, 0.05f), null, false);
                    Shapes.Part(_held, "Head", MeshFactory.Cube, item.Id.StartsWith("stone") ? MaterialLibrary.Lit(new Color(0.45f, 0.44f, 0.42f)) : metal, new Vector3(0.08f, 0.6f, 0), new Vector3(0.18f, 0.14f, 0.04f), null, false);
                    break;
                case ToolType.Pickaxe:
                    Shapes.Part(_held, "Handle", MeshFactory.Cube, handle, new Vector3(0, 0.3f, 0), new Vector3(0.05f, 0.75f, 0.05f), null, false);
                    Shapes.Part(_held, "Head", MeshFactory.Cube, item.Id.StartsWith("stone") ? MaterialLibrary.Lit(new Color(0.45f, 0.44f, 0.42f)) : metal, new Vector3(0, 0.66f, 0), new Vector3(0.42f, 0.06f, 0.05f), null, false);
                    break;
                case ToolType.Hammer:
                    Shapes.Part(_held, "Handle", MeshFactory.Cube, handle, new Vector3(0, 0.2f, 0), new Vector3(0.04f, 0.45f, 0.04f), null, false);
                    Shapes.Part(_held, "Head", MeshFactory.Cube, metal, new Vector3(0, 0.42f, 0), new Vector3(0.16f, 0.07f, 0.07f), null, false);
                    break;
                case ToolType.Weapon:
                case ToolType.Knife:
                    Shapes.Part(_held, "Grip", MeshFactory.Cube, handle, new Vector3(0, 0.08f, 0), new Vector3(0.05f, 0.18f, 0.05f), null, false);
                    Shapes.Part(_held, "Blade", MeshFactory.Cube, item.Id == "lead_pipe" ? MaterialLibrary.Lit(new Color(0.4f, 0.42f, 0.44f)) : metal, new Vector3(0, 0.45f, 0), new Vector3(item.Id == "lead_pipe" ? 0.06f : 0.09f, 0.6f, 0.02f), null, false);
                    break;
                default:
                    Shapes.Part(_held, "Item", MeshFactory.Cube, MaterialLibrary.Lit(itemColor), new Vector3(0, 0.08f, 0), Vector3.one * 0.14f, null, false);
                    break;
            }
        }

        public void PlaySwing() => _swing = 0f;

        public void PlaySwingIfIdle()
        {
            if (_swing < 0f) _swing = 0f;
        }
        public void Flash() => _hitFlash = 0.15f;

        public void SetDead(bool dead)
        {
            if (dead && !_dead) _deathT = 0f;
            _dead = dead;
        }

        /// <param name="speed">Horizontal speed in m/s.</param>
        public void Animate(float speed, bool crouching, bool grounded, float dt)
        {
            if (_body == null) return;
            if (_dead)
            {
                _deathT = Mathf.Min(1f, _deathT + dt * 2.2f);
                transform.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-88f, 0, 0), Mathf.SmoothStep(0, 1, _deathT));
                transform.localPosition = new Vector3(0, Mathf.Lerp(0f, 0.15f, _deathT), 0);
                return;
            }
            transform.localRotation = Quaternion.identity;
            transform.localPosition = Vector3.zero;

            _crouch = Mathf.MoveTowards(_crouch, crouching ? 1f : 0f, dt * 5f);
            float stride = Mathf.Clamp01(speed / 3.5f);
            _phase += dt * Mathf.Lerp(2f, 9f, Mathf.Clamp01(speed / 7f)) * (speed > 0.1f ? 1f : 0f);
            float s = Mathf.Sin(_phase);
            float legAmp = Mathf.Lerp(0f, _shambling ? 25f : 38f, stride);
            float armAmp = Mathf.Lerp(4f, _shambling ? 10f : 35f, stride);
            if (!grounded) legAmp = 15f;

            _legL.localRotation = Quaternion.Euler(s * legAmp - _crouch * 50f, 0, 0);
            _legR.localRotation = Quaternion.Euler(-s * legAmp - _crouch * 50f, 0, 0);
            float bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.04f * stride;
            _body.localPosition = new Vector3(0, 0.95f - _crouch * 0.32f + bob, _crouch * 0.12f);
            _body.localRotation = Quaternion.Euler(_crouch * 22f + (_shambling ? 14f : stride * 6f), 0, _shambling ? Mathf.Sin(_phase * 0.5f) * 6f : 0f);

            if (_shambling)
            {
                // The infected reach forward with both arms.
                _armL.localRotation = Quaternion.Euler(-78f + s * armAmp, 0, -6f);
                _armR.localRotation = Quaternion.Euler(-82f - s * armAmp, 0, 6f);
                _head.localRotation = Quaternion.Euler(10f, 0, Mathf.Sin(_phase * 0.7f) * 12f);
            }
            else
            {
                _armL.localRotation = Quaternion.Euler(-s * armAmp, 0, -4f);
                _armR.localRotation = Quaternion.Euler(s * armAmp * 0.6f - 20f, 0, 4f);
                _head.localRotation = Quaternion.identity;
            }

            if (_swing >= 0f)
            {
                _swing += dt * 3.2f;
                float k = _swing < 0.35f ? Mathf.SmoothStep(0, 1, _swing / 0.35f) : 1f - Mathf.SmoothStep(0, 1, (_swing - 0.35f) / 0.65f);
                _armR.localRotation = Quaternion.Euler(Mathf.Lerp(-150f, 20f, 1f - k) * (k > 0 ? 1 : 0) + (k <= 0 ? 0 : 0), 0, 4f);
                if (_shambling) _armL.localRotation = _armR.localRotation;
                if (_swing >= 1f) _swing = -1f;
            }

            if (_hitFlash > 0f)
            {
                _hitFlash -= dt;
                float k = Mathf.Max(0f, _hitFlash) / 0.15f;
                transform.localPosition = new Vector3(0, 0, -0.06f * k);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = visible;
        }
    }
}
