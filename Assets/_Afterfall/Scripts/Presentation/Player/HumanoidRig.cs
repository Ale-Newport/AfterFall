using System.Collections.Generic;
using Afterfall.Domain.Items;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Procedurally modelled and procedurally animated humanoid used for the survivor, the colonists and
    /// the infected (no external assets). The body is sculpted per palette (<see cref="MeshFactory.Character"/>)
    /// as rigid low-poly parts, one mesh per material per bone, on a joint hierarchy: pelvis, chest, head,
    /// upper arms, forearms, thighs, shins and feet. Legs are placed with two-bone IK so the feet plant while
    /// walking, running and crouching; arm swing, breathing, attack swings, flinches and the death fall are
    /// blended in code. The origin is at the feet and the character faces +Z.
    /// </summary>
    public sealed class HumanoidRig : MonoBehaviour
    {
        public enum HairStyle
        {
            Crop,
            Messy,
            Ponytail,
            Bob,
            Balding,
            Long,
            Buzz
        }

        public enum Outfit
        {
            FieldJacket,
            WorkJacket,
            Trucker,
            Vest,
            DressShirt,
            Hoodie
        }

        public enum Headwear
        {
            None,
            Cap,
            Beanie
        }

        public enum Legwear
        {
            Cargo,
            Work,
            Jeans
        }

        public enum Footwear
        {
            Boots,
            Shoes
        }

        /// <summary>
        /// The look of a character. Colours are sRGB: Shirt is the jacket/top, Accent the belt, bag or hat.
        /// Optional colours (Hair, Under for the shirt beneath, Eyes) are derived when left at zero alpha.
        /// </summary>
        public struct Palette
        {
            public Color Skin, Shirt, Pants, Shoes, Accent;
            public Color Hair, Under, Eyes;
            public HairStyle HairStyle;
            public Outfit Outfit;
            public Headwear Headwear;
            public Legwear Legwear;
            public Footwear Footwear;
            public bool Female, Beard, Backpack, Satchel, Bandana;

            /// <summary>Picks per-variant details such as how the infected's clothes are torn.</summary>
            public int Variant;
        }

        /// <summary>Joint angles (degrees) and targets of one frame, so poses can be blended.</summary>
        private struct Pose
        {
            public Vector3 Pelvis;
            public Vector3 PelvisRot, ChestRot, HeadRot;
            public Vector3 ArmL, ArmR;
            public float ForeL, ForeR;
            public Vector3 FootL, FootR;
            public float FootPitchL, FootPitchR;
        }

        private Transform _body, _chest, _head, _armL, _armR, _foreL, _foreR, _legL, _legR, _shinL, _shinR, _footL, _footR, _hand, _held;
        private bool _shambling;
        private bool _fallForward;
        private int _variant;
        private float _hipX;
        private float _phase;
        private float _time;
        private float _swing = -1f;
        private float _deathT;
        private bool _dead;
        private float _crouch;
        private float _air;
        private string _heldItem = "";
        private float _hitFlash;
        private bool _visible = true;
        private float _lastSpeed;
        private bool _lastCrouching, _lastGrounded = true;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Renderer> _heldRenderers = new List<Renderer>();

        public Transform Hand => _hand;

        public static HumanoidRig Create(Transform parent, Palette p, bool shambling)
        {
            var root = new GameObject("Rig");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<HumanoidRig>();
            rig.Build(p, shambling);
            return rig;
        }

        private void Build(Palette p, bool shambling)
        {
            _shambling = shambling;
            _fallForward = shambling || p.Backpack;
            _variant = ((p.Variant % 3) + 3) % 3;
            _time = p.Variant * 1.7f;
            _hipX = CharacterLayout.HipX(p);
            float shoulderX = CharacterLayout.ShoulderX(p, shambling);
            CharacterModel model = MeshFactory.Character(p, shambling);

            _body = Bone("Body", transform, new Vector3(0f, CharacterLayout.PelvisY, 0f), model, "Pelvis");
            _legL = Bone("LegL", _body, new Vector3(-_hipX, CharacterLayout.HipY, 0f), model, "ThighL");
            _shinL = Bone("ShinL", _legL, new Vector3(0f, -CharacterLayout.Thigh, 0f), model, "ShinL");
            _footL = Bone("FootL", _shinL, new Vector3(0f, -CharacterLayout.Shin, 0f), model, "FootL");
            _legR = Bone("LegR", _body, new Vector3(_hipX, CharacterLayout.HipY, 0f), model, "ThighR");
            _shinR = Bone("ShinR", _legR, new Vector3(0f, -CharacterLayout.Thigh, 0f), model, "ShinR");
            _footR = Bone("FootR", _shinR, new Vector3(0f, -CharacterLayout.Shin, 0f), model, "FootR");
            _chest = Bone("Chest", _body, new Vector3(0f, CharacterLayout.WaistY, 0f), model, "Chest");
            _head = Bone("Head", _chest, CharacterLayout.Neck, model, "Head");
            _armL = Bone("ArmL", _chest, new Vector3(-shoulderX, CharacterLayout.ShoulderY, CharacterLayout.ShoulderZ), model, "UpperArmL");
            _foreL = Bone("ForearmL", _armL, new Vector3(0f, -CharacterLayout.UpperArm, 0f), model, "ForearmL");
            _armR = Bone("ArmR", _chest, new Vector3(shoulderX, CharacterLayout.ShoulderY, CharacterLayout.ShoulderZ), model, "UpperArmR");
            _foreR = Bone("ForearmR", _armR, new Vector3(0f, -CharacterLayout.UpperArm, 0f), model, "ForearmR");
            _hand = Bone("Hand", _foreR, CharacterLayout.Grip, null, null);
            Animate(0f, false, true, 0f);
        }

        private Transform Bone(string name, Transform parent, Vector3 localPos, CharacterModel model, string part)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            if (model == null) return t;
            foreach (var piece in model.PartsOf(part))
            {
                GameObject go = Shapes.Mesh(t, part + piece.Key, piece.Value, Material(piece.Key), Vector3.zero, null, piece.Key != CharacterSurface.Gloss && piece.Key != CharacterSurface.Glow);
                _renderers.Add(go.GetComponent<Renderer>());
            }
            return t;
        }

        /// <summary>Shared material of a slot. Colour lives in the vertex colours, so every character shares these few materials.</summary>
        private static Material Material(CharacterSurface s)
        {
            switch (s)
            {
                case CharacterSurface.Skin: return MaterialLibrary.Surface(Art.Surface.Skin, Color.white, objectSpace: true);
                case CharacterSurface.Cloth: return MaterialLibrary.Surface(Art.Surface.Fabric, Color.white, objectSpace: true);
                case CharacterSurface.Knit: return MaterialLibrary.Surface(Art.CharacterSurfaces.Knit, Color.white, objectSpace: true);
                case CharacterSurface.Leather: return MaterialLibrary.Surface(Art.Surface.Leather, Color.white, objectSpace: true, tileScale: 0.4f);
                case CharacterSurface.Canvas: return MaterialLibrary.Surface(Art.Surface.Tarp, Color.white, objectSpace: true, tileScale: 0.35f);
                case CharacterSurface.Hair: return MaterialLibrary.Surface(Art.CharacterSurfaces.Hair, Color.white, objectSpace: true);
                case CharacterSurface.Gloss: return MaterialLibrary.Surface(Art.Surface.Plain, Color.white, 0.82f, objectSpace: true);
                case CharacterSurface.Glow: return MaterialLibrary.Emissive(new Color(1f, 0.86f, 0.42f), new Color(1f, 0.74f, 0.2f) * 3.2f);
                case CharacterSurface.Wood: return MaterialLibrary.Surface(Art.Surface.Wood, Color.white, objectSpace: true, tileScale: 0.2f);
                case CharacterSurface.Steel: return MaterialLibrary.Surface(Art.Surface.Metal, Color.white, objectSpace: true, tileScale: 0.25f);
                case CharacterSurface.Stone: return MaterialLibrary.Surface(Art.Surface.Rock, Color.white, objectSpace: true, tileScale: 0.08f);
                case CharacterSurface.Twine: return MaterialLibrary.Surface(Art.Surface.Fabric, Color.white, objectSpace: true, tileScale: 0.35f);
                default: return MaterialLibrary.Lit(Color.white);
            }
        }

        /// <summary>Shows a model of the held tool, weapon or item in the right fist.</summary>
        public void SetHeldItem(ItemDefinition item)
        {
            string id = item?.Id ?? "";
            if (id == _heldItem) return;
            _heldItem = id;
            if (_held != null) Destroy(_held.gameObject);
            _held = null;
            _heldRenderers.Clear();
            if (item == null) return;
            _held = new GameObject("Held_" + id).transform;
            _held.SetParent(_hand, false);
            // Models are built with the handle along +X and the edge towards −Y; the fist holds along its +Z.
            _held.localRotation = Quaternion.Euler(8f, 0f, 0f) * Quaternion.Euler(0f, -90f, 0f);
            foreach (var piece in MeshFactory.HeldItem(item))
            {
                GameObject go = Shapes.Mesh(_held, piece.Key.ToString(), piece.Value, Material(piece.Key), Vector3.zero);
                go.layer = gameObject.layer;
                var r = go.GetComponent<Renderer>();
                r.enabled = _visible;
                _heldRenderers.Add(r);
            }
            // Take the carrying pose right away (the next Animate may be a frame away, or never for a still).
            Animate(_lastSpeed, _lastCrouching, _lastGrounded, 0f);
        }

        public void PlaySwing() => _swing = 0f;

        public void PlaySwingIfIdle()
        {
            if (_swing < 0f) _swing = 0f;
        }

        public void Flash() => _hitFlash = 0.18f;

        public void SetDead(bool dead)
        {
            if (dead && !_dead) _deathT = 0f;
            _dead = dead;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = visible;
            foreach (Renderer r in _heldRenderers)
                if (r != null) r.enabled = visible;
        }

        // ---------------------------------------------------------------- animation

        /// <param name="speed">Horizontal speed in m/s.</param>
        public void Animate(float speed, bool crouching, bool grounded, float dt)
        {
            if (_body == null) return;
            _time += dt;
            _lastSpeed = speed;
            _lastCrouching = crouching;
            _lastGrounded = grounded;
            if (_dead)
            {
                AnimateDeath(dt);
                return;
            }
            _crouch = Mathf.MoveTowards(_crouch, crouching ? 1f : 0f, dt * 5f);
            _air = Mathf.MoveTowards(_air, grounded ? 0f : 1f, dt * 6f);

            Pose pose = _shambling ? Shamble(speed, dt) : Locomotion(speed, dt);
            if (_swing >= 0f)
            {
                _swing += dt * 3.2f;
                pose = _shambling ? ClawSwing(pose, Mathf.Min(_swing, 1f)) : ToolSwing(pose, Mathf.Min(_swing, 1f));
                if (_swing >= 1f) _swing = -1f;
            }

            Vector3 root = Vector3.zero;
            if (_hitFlash > 0f)
            {
                _hitFlash -= dt;
                float k = Mathf.Max(0f, _hitFlash) / 0.18f;
                root.z = -0.07f * k;
                pose.ChestRot.x -= 12f * k;
                pose.HeadRot.x -= 14f * k;
                pose.ArmL.x -= 10f * k;
                pose.ArmR.x -= 10f * k;
            }
            transform.localRotation = Quaternion.identity;
            transform.localPosition = root;
            Apply(pose);
        }

        private Pose Locomotion(float speed, float dt)
        {
            float stride = Mathf.Clamp01(speed / 2.4f);
            float run = Mathf.Clamp01((speed - 3f) / 3.5f);
            float idle = 1f - Mathf.Clamp01(speed / 0.6f);
            float rate = speed < 0.05f ? 0f : Mathf.Min(10.5f, 3.6f + speed * 1.25f);
            _phase += dt * rate;
            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
            float amp = rate > 0f ? Mathf.Min(0.44f, speed / rate) : 0f;
            float lift = (0.07f + 0.12f * run) * stride;
            float breath = Mathf.Sin(_time * 1.7f);
            bool holding = _heldItem.Length > 0;

            var pose = new Pose();
            float bob = (0.022f + run * 0.03f) * stride * (1f - Mathf.Cos(2f * _phase)) * 0.5f;
            pose.Pelvis = new Vector3(Mathf.Sin(_time * 0.5f) * 0.012f * idle, CharacterLayout.PelvisY - 0.004f - bob - run * 0.035f - _crouch * 0.33f - _air * 0.05f, _crouch * 0.02f);
            pose.PelvisRot = new Vector3(run * 6f + _crouch * 12f, s * 7f * stride, s * 2.5f * stride + Mathf.Sin(_time * 0.5f) * 1.4f * idle);
            pose.ChestRot = new Vector3(run * 7f + _crouch * 18f + breath * 0.9f, -pose.PelvisRot.y * 1.6f, -pose.PelvisRot.z * 0.6f);
            pose.HeadRot = new Vector3(-(pose.PelvisRot.x + pose.ChestRot.x) * 0.75f - breath * 0.5f, -(pose.PelvisRot.y + pose.ChestRot.y) * 0.8f + Mathf.Sin(_time * 0.37f) * 4f * idle, 0f);

            // Feet: stance moves the foot back under the body, swing lifts it forward.
            float spread = _hipX + 0.01f + _crouch * 0.03f;
            float zl = amp * s + _crouch * 0.07f, zr = -amp * s - _crouch * 0.05f;
            pose.FootL = new Vector3(-spread, CharacterLayout.AnkleY + lift * Mathf.Max(0f, c), zl);
            pose.FootR = new Vector3(spread, CharacterLayout.AnkleY + lift * Mathf.Max(0f, -c), zr);
            pose.FootPitchL = stride * (45f * Mathf.Max(0f, -s) * Mathf.Max(0f, c) - 25f * Mathf.Max(0f, s) * Mathf.Max(0f, c));
            pose.FootPitchR = stride * (45f * Mathf.Max(0f, s) * Mathf.Max(0f, -c) - 25f * Mathf.Max(0f, -s) * Mathf.Max(0f, -c));
            if (_air > 0f)
            {
                pose.FootL = Vector3.Lerp(pose.FootL, new Vector3(-spread, CharacterLayout.AnkleY + 0.26f, 0.14f), _air);
                pose.FootR = Vector3.Lerp(pose.FootR, new Vector3(spread, CharacterLayout.AnkleY + 0.16f, -0.12f), _air);
                pose.FootPitchL = Mathf.Lerp(pose.FootPitchL, 10f, _air);
                pose.FootPitchR = Mathf.Lerp(pose.FootPitchR, 25f, _air);
            }

            // Arms swing against the legs, elbows bend more when running.
            float armAmp = Mathf.Lerp(2f, 30f, stride) + run * 24f;
            float elbow = Mathf.Lerp(16f, 20f, stride) + run * 60f;
            float out_ = 6f + breath * 0.6f * idle + _air * 18f;
            pose.ArmL = new Vector3(armAmp * s - 3f * idle - _crouch * 18f - _air * 25f, 0f, -out_);
            pose.ArmR = new Vector3(-armAmp * s - 3f * idle - _crouch * 18f - _air * 25f, 0f, out_);
            pose.ForeL = -(elbow + 14f * Mathf.Max(0f, -s) * stride + _crouch * 25f);
            pose.ForeR = -(elbow + 14f * Mathf.Max(0f, s) * stride + _crouch * 25f);
            if (holding)
            {
                // Carry the tool ready at the hip.
                pose.ArmR = new Vector3(-12f - armAmp * s * 0.35f - _crouch * 12f, -4f, 8f);
                pose.ForeR = -42f - run * 25f;
            }
            return pose;
        }

        /// <summary>The infected: hunched, dragging one leg, arms reaching (each variant a little differently).</summary>
        private Pose Shamble(float speed, float dt)
        {
            float stride = Mathf.Clamp01(speed / 1.2f);
            float rate = speed < 0.05f ? 0f : 2.6f + speed * 1.6f;
            _phase += dt * rate;
            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
            float amp = rate > 0f ? Mathf.Min(0.3f, speed / rate) : 0f;
            float sway = Mathf.Sin(_time * 0.9f + _variant);
            float loll = Mathf.Sin(_time * 0.7f + _variant * 2f);

            var pose = new Pose();
            float bob = 0.03f * stride * (1f - Mathf.Cos(2f * _phase)) * 0.5f;
            pose.Pelvis = new Vector3(s * 0.03f * stride + sway * 0.01f, CharacterLayout.PelvisY - 0.06f - bob, -0.02f);
            pose.PelvisRot = new Vector3(12f, s * 8f * stride, s * 6f * stride + sway * 2f);
            pose.ChestRot = new Vector3(26f + Mathf.Sin(_time * 1.3f) * 2f, -pose.PelvisRot.y * 1.3f + sway * 4f, -7f - pose.PelvisRot.z * 0.5f);
            pose.HeadRot = new Vector3(-26f + loll * 4f, sway * 10f, 10f + loll * 9f);

            float spread = _hipX + 0.035f;
            // The left leg drags: shorter step, barely lifted, toes scraping.
            pose.FootL = new Vector3(-spread, CharacterLayout.AnkleY + 0.012f * stride * Mathf.Max(0f, c), amp * 0.7f * s);
            pose.FootR = new Vector3(spread, CharacterLayout.AnkleY + 0.075f * stride * Mathf.Max(0f, -c), -amp * s);
            pose.FootPitchL = 14f * stride + 6f;
            pose.FootPitchR = stride * 30f * Mathf.Max(0f, s) * Mathf.Max(0f, -c);

            float reach = Mathf.Sin(_phase * 0.5f);
            switch (_variant)
            {
                case 0:
                    pose.ArmL = new Vector3(-72f + reach * 7f + loll * 3f, 10f, -4f);
                    pose.ArmR = new Vector3(6f - s * 12f * stride, 0f, 9f + sway * 3f);
                    pose.ForeL = -14f;
                    pose.ForeR = -8f;
                    break;
                case 1:
                    pose.ArmL = new Vector3(-74f + reach * 6f, 9f, -5f);
                    pose.ArmR = new Vector3(-66f - reach * 8f, -9f, 6f);
                    pose.ForeL = -18f;
                    pose.ForeR = -10f;
                    break;
                default:
                    pose.ArmL = new Vector3(-42f + reach * 6f + sway * 4f, 6f, -10f);
                    pose.ArmR = new Vector3(-50f - reach * 6f, -6f, 10f);
                    pose.ForeL = -30f;
                    pose.ForeR = -24f;
                    break;
            }
            return pose;
        }

        private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        /// <summary>Overhead tool swing: wind-up over the shoulder with a chest twist, fast strike, recovery.</summary>
        private static Pose ToolSwing(Pose b, float t)
        {
            var up = b;
            up.ArmR = new Vector3(-158f, -12f, 14f);
            up.ForeR = -78f;
            up.ArmL = new Vector3(-28f, 0f, -12f);
            up.ForeL = -40f;
            up.ChestRot = new Vector3(b.ChestRot.x - 7f, b.ChestRot.y + 24f, b.ChestRot.z - 3f);
            up.HeadRot = new Vector3(b.HeadRot.x, b.HeadRot.y - 18f, b.HeadRot.z);
            var hit = b;
            hit.ArmR = new Vector3(-38f, -6f, 6f);
            hit.ForeR = -12f;
            hit.ArmL = new Vector3(18f, 0f, -10f);
            hit.ForeL = -30f;
            hit.ChestRot = new Vector3(b.ChestRot.x + 16f, b.ChestRot.y - 20f, b.ChestRot.z + 2f);
            hit.HeadRot = new Vector3(b.HeadRot.x - 10f, b.HeadRot.y + 14f, b.HeadRot.z);
            hit.Pelvis = b.Pelvis + new Vector3(0f, -0.03f, 0.02f);
            if (t < 0.3f) return Blend(b, up, Ease(t / 0.3f));
            if (t < 0.48f) return Blend(up, hit, Mathf.Pow(Mathf.Clamp01((t - 0.3f) / 0.18f), 1.6f));
            return Blend(hit, b, Ease((t - 0.48f) / 0.52f));
        }

        /// <summary>The infected's two-handed claw: arms raised, then slammed down with a lunge.</summary>
        private static Pose ClawSwing(Pose b, float t)
        {
            var up = b;
            up.ArmL = new Vector3(-145f, 14f, -14f);
            up.ArmR = new Vector3(-140f, -14f, 14f);
            up.ForeL = -45f;
            up.ForeR = -45f;
            up.ChestRot = new Vector3(b.ChestRot.x - 14f, b.ChestRot.y, b.ChestRot.z);
            var hit = b;
            hit.ArmL = new Vector3(-40f, 8f, -6f);
            hit.ArmR = new Vector3(-36f, -8f, 6f);
            hit.ForeL = -8f;
            hit.ForeR = -8f;
            hit.ChestRot = new Vector3(b.ChestRot.x + 18f, b.ChestRot.y, b.ChestRot.z);
            hit.HeadRot = new Vector3(b.HeadRot.x - 12f, b.HeadRot.y, b.HeadRot.z);
            hit.Pelvis = b.Pelvis + new Vector3(0f, -0.03f, 0.05f);
            if (t < 0.32f) return Blend(b, up, Ease(t / 0.32f));
            if (t < 0.5f) return Blend(up, hit, Mathf.Pow(Mathf.Clamp01((t - 0.32f) / 0.18f), 1.6f));
            return Blend(hit, b, Ease((t - 0.5f) / 0.5f));
        }

        private static Pose Blend(Pose a, Pose b, float t) => new Pose
        {
            Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t),
            PelvisRot = Vector3.Lerp(a.PelvisRot, b.PelvisRot, t),
            ChestRot = Vector3.Lerp(a.ChestRot, b.ChestRot, t),
            HeadRot = Vector3.Lerp(a.HeadRot, b.HeadRot, t),
            ArmL = Vector3.Lerp(a.ArmL, b.ArmL, t),
            ArmR = Vector3.Lerp(a.ArmR, b.ArmR, t),
            ForeL = Mathf.Lerp(a.ForeL, b.ForeL, t),
            ForeR = Mathf.Lerp(a.ForeR, b.ForeR, t),
            FootL = Vector3.Lerp(a.FootL, b.FootL, t),
            FootR = Vector3.Lerp(a.FootR, b.FootR, t),
            FootPitchL = Mathf.Lerp(a.FootPitchL, b.FootPitchL, t),
            FootPitchR = Mathf.Lerp(a.FootPitchR, b.FootPitchR, t)
        };

        private void Apply(Pose p)
        {
            _body.localPosition = p.Pelvis;
            Quaternion pelvis = Quaternion.Euler(p.PelvisRot);
            _body.localRotation = pelvis;
            _chest.localRotation = Quaternion.Euler(p.ChestRot);
            _head.localRotation = Quaternion.Euler(p.HeadRot);
            _armL.localRotation = Quaternion.Euler(p.ArmL);
            _armR.localRotation = Quaternion.Euler(p.ArmR);
            _foreL.localRotation = Quaternion.Euler(p.ForeL, 0f, 0f);
            _foreR.localRotation = Quaternion.Euler(p.ForeR, 0f, 0f);
            SolveLeg(_legL, _shinL, _footL, p.FootL, p.FootPitchL, pelvis, p.PelvisRot.x);
            SolveLeg(_legR, _shinR, _footR, p.FootR, p.FootPitchR, pelvis, p.PelvisRot.x);
        }

        /// <summary>Two-bone IK in the leg's sagittal plane: puts the ankle on <paramref name="ankle"/> (rig space), knee forward.</summary>
        private void SolveLeg(Transform thigh, Transform shin, Transform foot, Vector3 ankle, float footPitch, Quaternion pelvis, float pelvisPitch)
        {
            Vector3 hip = _body.localPosition + pelvis * thigh.localPosition;
            Vector3 d = Quaternion.Inverse(pelvis) * (ankle - hip);
            float lt = CharacterLayout.Thigh, ls = CharacterLayout.Shin;
            float dist = Mathf.Clamp(new Vector2(d.y, d.z).magnitude, 0.2f, lt + ls - 0.002f);
            float aim = Mathf.Atan2(-d.z, -d.y) * Mathf.Rad2Deg;
            float a = Mathf.Acos(Mathf.Clamp((lt * lt + dist * dist - ls * ls) / (2f * lt * dist), -1f, 1f)) * Mathf.Rad2Deg;
            float b = Mathf.Acos(Mathf.Clamp((ls * ls + dist * dist - lt * lt) / (2f * ls * dist), -1f, 1f)) * Mathf.Rad2Deg;
            float side = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
            float hipPitch = aim - a;
            thigh.localRotation = Quaternion.Euler(hipPitch, 0f, Mathf.Clamp(side, -10f, 10f));
            shin.localRotation = Quaternion.Euler(a + b, 0f, 0f);
            foot.localRotation = Quaternion.Euler(footPitch - (pelvisPitch + hipPitch + a + b), 0f, 0f);
        }

        /// <summary>Knees buckle, then the body topples about the feet and lands sprawled (face down when carrying a pack or infected).</summary>
        private void AnimateDeath(float dt)
        {
            _deathT = Mathf.Min(1f, _deathT + dt * 1.6f);
            float buckle = Ease(_deathT / 0.3f) * (1f - Ease((_deathT - 0.55f) / 0.45f) * 0.8f);
            float f = Mathf.Clamp01((_deathT - 0.12f) / 0.7f);
            float fall = f * f;
            float bounce = _deathT > 0.82f ? Mathf.Sin((_deathT - 0.82f) / 0.18f * Mathf.PI) * 3f : 0f;
            float dir = _fallForward ? 1f : -1f;
            transform.localRotation = Quaternion.Euler(dir * (88f * fall - bounce), 0f, 0f);
            transform.localPosition = new Vector3(0f, 0.12f * fall, 0f);

            float thigh = -45f * buckle - 6f * fall, knee = 90f * buckle + 12f * fall;
            _body.localPosition = new Vector3(0f, CharacterLayout.PelvisY - 0.3f * buckle, 0.05f * buckle);
            _body.localRotation = Quaternion.Euler(10f * buckle, 0f, 0f);
            _chest.localRotation = Quaternion.Euler(28f * buckle * dir * (1f - fall) - 6f * fall, 0f, 4f * fall);
            _head.localRotation = Quaternion.Euler(-12f * buckle - dir * 18f * fall, 38f * fall, 0f);
            _armL.localRotation = Quaternion.Euler(-25f * (1f - fall) - 10f * fall, 0f, -10f - 62f * fall);
            _armR.localRotation = Quaternion.Euler(-35f * (1f - fall) - 30f * fall, 0f, 10f + 48f * fall);
            _foreL.localRotation = Quaternion.Euler(-30f - 20f * fall, 0f, 0f);
            _foreR.localRotation = Quaternion.Euler(-20f - 50f * fall, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(thigh, 0f, -4f * fall);
            _legR.localRotation = Quaternion.Euler(thigh * 0.7f + 4f * fall, 0f, 8f * fall);
            _shinL.localRotation = Quaternion.Euler(knee, 0f, 0f);
            _shinR.localRotation = Quaternion.Euler(knee * 0.6f + 22f * fall, 0f, 0f);
            _footL.localRotation = Quaternion.Euler(-thigh - knee + 30f * fall, 0f, 0f);
            _footR.localRotation = Quaternion.Euler(-thigh - knee + 40f * fall, 0f, 0f);
        }
    }
}
