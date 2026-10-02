using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diamond.EditorTools
{
    /// <summary>Logs which way a few humanoid muscles move their limbs (model space, character faces +z) so the ready stance can be built from muscle values.</summary>
    public static class CalibrateMuscles
    {
        public static void Run()
        {
            var path = "Assets/Motions/Baseball Idle.fbx";
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            var h = new HumanPoseHandler(a.avatar, go.transform);
            var pose = new HumanPose();
            h.GetHumanPose(ref pose);
            var names = HumanTrait.MuscleName;
            Debug.Log("MUSCLES " + string.Join(" | ", names.Select((n, i) => i + ":" + n)));
            Vector3 P(HumanBodyBones b) => go.transform.InverseTransformPoint(a.GetBoneTransform(b).position);
            var probes = new (string muscle, HumanBodyBones tip, HumanBodyBones origin)[]
            {
                ("Spine Front-Back", HumanBodyBones.Head, HumanBodyBones.Hips),
                ("Chest Front-Back", HumanBodyBones.Head, HumanBodyBones.Hips),
                ("Head Nod Down-Up", HumanBodyBones.Head, HumanBodyBones.Neck),
                ("Left Upper Leg Front-Back", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg),
                ("Left Upper Leg In-Out", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg),
                ("Left Lower Leg Stretch", HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg),
                ("Left Foot Up-Down", HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot),
                ("Left Arm Down-Up", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm),
                ("Left Arm Front-Back", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm),
                ("Left Forearm Stretch", HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm),
            };
            foreach (var pr in probes)
            {
                var idx = System.Array.IndexOf(names, pr.muscle);
                if (idx < 0) { Debug.Log("CAL missing " + pr.muscle); continue; }
                var res = "";
                foreach (var v in new[] { -0.6f, 0f, 0.6f })
                {
                    var q = pose; q.muscles = (float[])pose.muscles.Clone(); q.muscles[idx] = v;
                    h.SetHumanPose(ref q);
                    var d = P(pr.tip) - P(pr.origin);
                    res += $" v={v}: ({d.x:0.00},{d.y:0.00},{d.z:0.00})";
                }
                Debug.Log($"CAL {pr.muscle} [{idx}] orig {pose.muscles[idx]:0.00}{res}");
            }
            h.SetHumanPose(ref pose);
        }
    }
}
