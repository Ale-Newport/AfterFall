using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Drives a skinned MPFB model (game-engine rig: pelvis, spine_01–03, neck_01, head, clavicles,
    /// arms, hands, thighs, calves, feet) from <see cref="HumanoidRig"/>'s procedural joints, which
    /// stay invisible. At bind time the model is turned to face +Z, scaled so its legs are as long as
    /// the procedural legs (so the foot IK still plants its feet) and set on the ground; arm and leg
    /// bones are re-aimed along the procedural limbs (the model stands in an A-pose). Every frame each
    /// bone takes its driver joint's change of rotation since rest; the spine and neck blend between
    /// neighbouring joints, and the pelvis follows the procedural pelvis.
    /// </summary>
    public sealed class SkinnedBody
    {
        private struct Link
        {
            public Transform Bone, A, B;
            public float T;
            public Quaternion RestA, RestB, RestBone;
        }

        /// <summary>The procedural joints that drive the model.</summary>
        public struct Joints
        {
            public Transform Body, Chest, Head, ArmL, ArmR, ForeL, ForeR, LegL, LegR, ShinL, ShinR, FootL, FootR, Hand;
        }

        private readonly List<Link> _links = new List<Link>();
        private Transform _pelvis, _body;
        private Vector3 _pelvisOffset;

        /// <summary>The grip in the model's right hand (held items hang here).</summary>
        public Transform Grip { get; private set; }

        /// <summary>The model's chest bone (gear such as a backpack hangs here).</summary>
        public Transform Chest { get; private set; }

        /// <summary>Binds <paramref name="model"/> (a child of <paramref name="rig"/>) to the joints at rest; null if bones are missing.</summary>
        public static SkinnedBody Bind(Transform rig, Transform model, Joints j)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            string[] needed =
            {
                "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head", "clavicle_l", "clavicle_r", "upperarm_l", "upperarm_r",
                "lowerarm_l", "lowerarm_r", "hand_l", "hand_r", "middle_01_r", "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r", "ball_l"
            };
            foreach (string n in needed)
                if (!bones.ContainsKey(n))
                {
                    Debug.LogWarning($"[Afterfall] Character model {model.name} has no bone '{n}'; using the procedural body.");
                    return null;
                }
            Transform B(string n) => bones[n];

            // Stand it up, face it along +Z, size its legs to the procedural legs and put it on the ground.
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            Vector3 up = B("head").position - B("pelvis").position;
            model.rotation = Quaternion.FromToRotation(up, rig.up) * model.rotation;
            Vector3 toe = Vector3.ProjectOnPlane(B("ball_l").position - B("foot_l").position, rig.up);
            if (toe.sqrMagnitude > 1e-6f) model.rotation = Quaternion.FromToRotation(toe, rig.forward) * model.rotation;
            float leg = Vector3.Distance(B("thigh_l").position, B("calf_l").position) + Vector3.Distance(B("calf_l").position, B("foot_l").position);
            model.localScale = Vector3.one * ((CharacterLayout.Thigh + CharacterLayout.Shin) / Mathf.Max(0.1f, leg));
            Vector3 pelvis = rig.InverseTransformPoint(B("pelvis").position);
            float ankle = rig.InverseTransformPoint(B("foot_l").position).y;
            model.localPosition = new Vector3(-pelvis.x, CharacterLayout.AnkleY - ankle, -pelvis.z);

            var body = new SkinnedBody { _pelvis = B("pelvis"), _body = j.Body, Chest = B("spine_03") };
            body._pelvisOffset = j.Body.InverseTransformPoint(B("pelvis").position);
            body.Add(B("pelvis"), j.Body);
            body.Add(B("spine_01"), j.Body);
            body.Add(B("spine_02"), j.Body, j.Chest, 0.5f);
            body.Add(B("spine_03"), j.Chest);
            body.Add(B("neck_01"), j.Chest, j.Head, 0.5f);
            body.Add(B("head"), j.Head);
            foreach (string s in new[] { "l", "r" })
            {
                bool left = s == "l";
                Transform arm = left ? j.ArmL : j.ArmR, fore = left ? j.ForeL : j.ForeR;
                Transform thigh = left ? j.LegL : j.LegR, shin = left ? j.ShinL : j.ShinR, foot = left ? j.FootL : j.FootR;
                body.Add(B("clavicle_" + s), j.Chest);
                body.AddAimed(B("upperarm_" + s), B("lowerarm_" + s), arm);
                Quaternion foreAim = body.AddAimed(B("lowerarm_" + s), B("hand_" + s), fore);
                body.Add(B("hand_" + s), fore, null, 0f, foreAim);
                body.AddAimed(B("thigh_" + s), B("calf_" + s), thigh);
                body.AddAimed(B("calf_" + s), B("foot_" + s), shin);
                body.Add(B("foot_" + s), foot);
            }

            // The grip: in the right palm, turned so held items sit as they did in the procedural fist.
            Link hand = body._links.Find(l => l.Bone == B("hand_r"));
            var grip = new GameObject("Grip").transform;
            grip.SetParent(B("hand_r"), false);
            grip.position = Vector3.Lerp(B("hand_r").position, B("middle_01_r").position, 0.55f);
            grip.localRotation = Quaternion.Inverse(hand.RestBone) * j.Hand.rotation;
            body.Grip = grip;
            return body;
        }

        private void Add(Transform bone, Transform a, Transform b = null, float t = 0f, Quaternion? aim = null)
        {
            _links.Add(new Link
            {
                Bone = bone, A = a, B = b, T = t,
                RestA = a.rotation, RestB = b != null ? b.rotation : Quaternion.identity,
                RestBone = (aim ?? Quaternion.identity) * bone.rotation
            });
        }

        /// <summary>A limb bone re-aimed at rest from its own direction (to its child) onto the joint's (down its −Y); returns that turn.</summary>
        private Quaternion AddAimed(Transform bone, Transform child, Transform joint)
        {
            Quaternion aim = Quaternion.FromToRotation(child.position - bone.position, -joint.up);
            Add(bone, joint, null, 0f, aim);
            return aim;
        }

        /// <summary>Poses the model from the joints (call after the joints are posed).</summary>
        public void Sync()
        {
            if (_pelvis == null) return;
            _pelvis.position = _body.TransformPoint(_pelvisOffset);
            for (int i = 0; i < _links.Count; i++)
            {
                Link l = _links[i];
                Quaternion delta = l.A.rotation * Quaternion.Inverse(l.RestA);
                if (l.B != null) delta = Quaternion.Slerp(delta, l.B.rotation * Quaternion.Inverse(l.RestB), l.T);
                l.Bone.rotation = delta * l.RestBone;
            }
        }
    }
}
