using System.Collections.Generic;
using UnityEngine;

namespace Diamond.Stadium
{
    /// <summary>
    /// Bone adjustments computed in the actor's model space (its root's local frame). Left-handed players are drawn by
    /// mirroring the root (scale x = -1); working in model space keeps rotations and IK correct for mirrored and normal actors.
    /// </summary>
    public static class BoneMath
    {
        /// <summary>Rotation of <paramref name="bone"/> in the model space of <paramref name="root"/> (product of local rotations).</summary>
        public static Quaternion ModelRotation(Transform bone, Transform root)
        {
            var chain = new List<Transform>();
            for (var t = bone; t != null && t != root; t = t.parent) chain.Add(t);
            var q = Quaternion.identity;
            for (var i = chain.Count - 1; i >= 0; i--) q *= chain[i].localRotation;
            return q;
        }

        public static Vector3 ModelPoint(Transform root, Vector3 world) => root.InverseTransformPoint(world);

        /// <summary>Applies <paramref name="delta"/> (a rotation expressed in model space) to <paramref name="bone"/>.</summary>
        public static void RotateInModelSpace(Transform bone, Transform root, Quaternion delta)
        {
            var parent = ModelRotation(bone.parent, root);
            bone.localRotation = Quaternion.Inverse(parent) * delta * parent * bone.localRotation;
        }

        /// <summary>Blends a bone's local rotation back towards <paramref name="original"/> (weight 1 keeps the new rotation).</summary>
        public static void BlendLocal(Transform bone, Quaternion original, float weight) =>
            bone.localRotation = Quaternion.Slerp(original, bone.localRotation, weight);

        /// <summary>
        /// Analytic two-bone IK (law of cosines) in model space: reaches <paramref name="worldTarget"/> with the hand, clamped to the arm's
        /// reach, keeping the elbow on its animated side, blended by <paramref name="weight"/>.
        /// </summary>
        public static void TwoBoneIk(Transform root, Transform upper, Transform lower, Transform hand, Vector3 worldTarget, float weight)
        {
            if (root == null || upper == null || lower == null || hand == null || weight <= 0f) return;
            var upperLocal = upper.localRotation; var lowerLocal = lower.localRotation;
            var target = ModelPoint(root, worldTarget);
            var a = ModelPoint(root, upper.position); var b = ModelPoint(root, lower.position); var c = ModelPoint(root, hand.position);
            var lenAb = (b - a).magnitude; var lenBc = (c - b).magnitude;
            var toTarget = target - a;
            var dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lenAb - lenBc) + 0.001f, lenAb + lenBc - 0.001f);
            var dir = toTarget.normalized;
            var cos = (lenAb * lenAb + dist * dist - lenBc * lenBc) / (2f * lenAb * dist);
            var angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
            var pole = Vector3.ProjectOnPlane(b - a, dir);
            pole = pole.sqrMagnitude < 1e-8f ? Vector3.down : pole.normalized;
            var elbow = a + (dir * Mathf.Cos(angle) + pole * Mathf.Sin(angle)) * lenAb;
            RotateInModelSpace(upper, root, Quaternion.FromToRotation(b - a, elbow - a));
            b = ModelPoint(root, lower.position); c = ModelPoint(root, hand.position);
            var reach = a + dir * dist;
            RotateInModelSpace(lower, root, Quaternion.FromToRotation(c - b, reach - b));
            BlendLocal(upper, upperLocal, weight);
            BlendLocal(lower, lowerLocal, weight);
        }
    }
}
