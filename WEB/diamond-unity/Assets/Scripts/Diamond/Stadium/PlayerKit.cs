using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diamond.Stadium
{
    /// <summary>
    /// Dresses a skinned player body: splits its single mesh into jersey / sleeves / pants / socks / shoes / skin by the bones the
    /// vertices are weighted to, gives every part its own team-coloured material, and attaches simple equipment (cap or helmet,
    /// bat, glove). Equipment is built from primitives so it needs no assets.
    /// </summary>
    public sealed class PlayerKit : MonoBehaviour
    {
        public enum Role { Fielder, Pitcher, Catcher, Batter, Runner }

        public enum Part { Skin, Jersey, Sleeve, Pants, Socks, Shoes }

        [System.Serializable]
        public struct Look
        {
            public Color jersey, sleeve, pants, socks, cap;
            public static Look Default => new Look
            {
                jersey = new Color(0.92f, 0.92f, 0.92f), sleeve = new Color(0.15f, 0.2f, 0.45f), pants = new Color(0.92f, 0.92f, 0.92f),
                socks = new Color(0.15f, 0.2f, 0.45f), cap = new Color(0.15f, 0.2f, 0.45f),
            };
        }

        static readonly Color Skin = new Color(0.86f, 0.66f, 0.52f);
        static readonly Color Shoe = new Color(0.07f, 0.07f, 0.08f);
        static readonly Color Wood = new Color(0.78f, 0.58f, 0.34f);
        static readonly Color Leather = new Color(0.42f, 0.24f, 0.12f);

        const int PartCount = 6;
        static readonly Dictionary<Mesh, Mesh> SplitCache = new Dictionary<Mesh, Mesh>();

        Material[] _materials;
        Material _capMaterial, _gloveMaterial, _batMaterial;
        public Role Kind { get; private set; }

        // ---- batting equipment placement, tuned from renders (rotation of the bat in the world-aligned T-pose frame) ----
        public static Vector3 BatEuler = new Vector3(0f, 0f, 0f);
        public static Vector3 BatOffset = new Vector3(0f, -0.07f, 0f);

        public static PlayerKit Dress(GameObject go, Role role, Material baseMaterial)
        {
            var kit = go.GetComponent<PlayerKit>() ?? go.AddComponent<PlayerKit>();
            kit.Kind = role;
            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length != 1 || baseMaterial == null)
            {
                // Old two-part mannequin: plain materials, no uniform.
                var joints = Resources.Load<Material>("Diamond/PlayerJoints");
                foreach (var r in renderers) r.sharedMaterial = r.name.Contains("Joints") && joints != null ? joints : baseMaterial;
                return kit;
            }
            var renderer = renderers[0];

            var source = renderer.sharedMesh;
            if (source.isReadable && source.subMeshCount == 1)
            {
                if (!SplitCache.TryGetValue(source, out var split) || split == null) { split = Split(source, renderer.bones); SplitCache[source] = split; }
                renderer.sharedMesh = split;
            }
            kit._materials = new Material[renderer.sharedMesh.subMeshCount];
            for (var i = 0; i < kit._materials.Length; i++) kit._materials[i] = new Material(baseMaterial);
            renderer.sharedMaterials = kit._materials;
            kit._capMaterial = new Material(baseMaterial); kit._gloveMaterial = new Material(baseMaterial) { color = Leather }; kit._batMaterial = new Material(baseMaterial) { color = Wood };
            kit.SetLook(Look.Default);
            kit.AttachEquipment(go, role);
            return kit;
        }

        public void SetLook(Look look)
        {
            if (_materials == null) return;
            Set(Part.Skin, Skin); Set(Part.Jersey, look.jersey); Set(Part.Sleeve, look.sleeve); Set(Part.Pants, look.pants);
            Set(Part.Socks, look.socks); Set(Part.Shoes, Shoe);
            if (_capMaterial != null) _capMaterial.color = look.cap;
        }

        void Set(Part part, Color c)
        {
            var i = (int)part;
            if (i < _materials.Length && _materials[i] != null) _materials[i].color = c;
        }

        // ---- mesh split ---------------------------------------------------------------------------------------------

        static Part PartOf(string boneName)
        {
            var n = boneName;
            var colon = n.LastIndexOf(':');
            if (colon >= 0) n = n.Substring(colon + 1);
            if (n.Contains("Hand") || n == "Head" || n == "Neck" || n.Contains("HeadTop")) return Part.Skin;
            if (n == "Hips" || n.EndsWith("UpLeg")) return Part.Pants;
            if (n == "LeftLeg" || n == "RightLeg") return Part.Socks;
            if (n.EndsWith("Foot") || n.Contains("Toe")) return Part.Shoes;
            if (n.EndsWith("ForeArm")) return Part.Sleeve;
            if (n.StartsWith("Spine") || n.EndsWith("Shoulder") || n == "LeftArm" || n == "RightArm") return Part.Jersey;
            return Part.Skin;
        }

        static Mesh Split(Mesh source, Transform[] bones)
        {
            var parts = new Part[bones.Length];
            for (var i = 0; i < bones.Length; i++) parts[i] = bones[i] != null ? PartOf(bones[i].name) : Part.Skin;
            var weights = source.boneWeights;
            var vertexPart = new Part[source.vertexCount];
            for (var v = 0; v < vertexPart.Length; v++)
            {
                var w = weights[v];
                var sum = new float[PartCount];
                sum[(int)parts[w.boneIndex0]] += w.weight0; sum[(int)parts[w.boneIndex1]] += w.weight1;
                sum[(int)parts[w.boneIndex2]] += w.weight2; sum[(int)parts[w.boneIndex3]] += w.weight3;
                var best = 0;
                for (var p = 1; p < PartCount; p++) if (sum[p] > sum[best]) best = p;
                vertexPart[v] = (Part)best;
            }
            var triangles = source.triangles;
            var lists = new List<int>[PartCount];
            for (var p = 0; p < PartCount; p++) lists[p] = new List<int>();
            for (var t = 0; t < triangles.Length; t += 3)
            {
                var a = vertexPart[triangles[t]]; var b = vertexPart[triangles[t + 1]]; var c = vertexPart[triangles[t + 2]];
                var part = a == b || a == c ? a : b == c ? b : a;
                lists[(int)part].AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
            }
            var mesh = Instantiate(source);
            mesh.name = source.name + " (dressed)";
            mesh.subMeshCount = PartCount;
            for (var p = 0; p < PartCount; p++) mesh.SetTriangles(lists[p], p);
            return mesh;
        }

        // ---- equipment ----------------------------------------------------------------------------------------------

        public static Vector3 CrownOffset = new Vector3(0f, 0.17f, -0.005f);

        void AttachEquipment(GameObject go, Role role) => Rebuild();

        /// <summary>(Re)builds the cap/helmet, bat and glove from the current tuning values.</summary>
        public void Rebuild()
        {
            var go = gameObject; var role = Kind;
            var animator = go.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return;
            foreach (var old in go.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name.StartsWith("Gear ")).ToArray())
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            // The FBX instance comes in the pose of the first animation frame (body turned, mid-stride). Put the avatar in its T-pose facing the
            // actor's +z first so equipment offsets are expressed in a known frame; the animation takes over afterwards.
            var handler = new HumanPoseHandler(animator.avatar, go.transform);
            var tpose = new HumanPose();
            handler.GetHumanPose(ref tpose);
            tpose.muscles = new float[HumanTrait.MuscleCount];
            tpose.bodyRotation = Quaternion.identity;
            tpose.bodyPosition = new Vector3(0f, tpose.bodyPosition.y, 0f);
            handler.SetHumanPose(ref tpose);
            handler.Dispose();
            animator.Rebind();   // without this the animator keeps the posed state and ignores later playable evaluations on the arms
            var rootRotation = go.transform.rotation;
            if (head != null)
            {
                var helmet = role == Role.Batter || role == Role.Runner || role == Role.Catcher;
                var skull = HeadBounds(go, head);   // actor-local bounds of the head mesh in the T-pose
                var headLocal = go.transform.InverseTransformPoint(head.position);
                Vector3 Off(Vector3 actorLocal) => actorLocal - headLocal;
                var crownHeight = helmet ? 0.15f : 0.13f;
                var crown = new Vector3(skull.center.x, skull.max.y - crownHeight * 0.38f, skull.center.z + 0.005f);
                Piece(PrimitiveType.Sphere, head, rootRotation, Off(crown), new Vector3(skull.size.x * 1.12f, crownHeight, skull.size.z * 1.1f), Quaternion.identity, _capMaterial);
                // Brim: a short flat disc over the forehead, pointing forward (+z of the actor in the T-pose).
                var brim = new Vector3(skull.center.x, skull.max.y - crownHeight * 0.75f, skull.max.z + 0.01f);
                Piece(PrimitiveType.Cylinder, head, rootRotation, Off(brim), new Vector3(skull.size.x * 0.85f, 0.004f, helmet ? 0.09f : 0.13f), Quaternion.identity, _capMaterial);
                if (helmet)
                {
                    // Ear flap on the batting side's far ear (model +x for right-handers is the model's left side).
                    var ear = new Vector3(role == Role.Batter ? skull.min.x : skull.max.x, skull.center.y + 0.02f, skull.center.z);
                    Piece(PrimitiveType.Sphere, head, rootRotation, Off(ear), new Vector3(0.03f, 0.08f, 0.09f), Quaternion.identity, _capMaterial);
                }
            }
            if (role == Role.Batter && right != null)
            {
                // Handle and barrel along the hand's grip axis; BatEuler/BatOffset were tuned against the swing clip.
                var rot = Quaternion.Euler(BatEuler);
                Piece(PrimitiveType.Cylinder, right, rootRotation, BatOffset + rot * new Vector3(0f, 0.2f, 0f), new Vector3(0.032f, 0.2f, 0.032f), rot, _batMaterial);
                Piece(PrimitiveType.Cylinder, right, rootRotation, BatOffset + rot * new Vector3(0f, 0.52f, 0f), new Vector3(0.068f, 0.17f, 0.068f), rot, _batMaterial);
            }
            if ((role == Role.Fielder || role == Role.Pitcher || role == Role.Catcher) && left != null)
            {
                var size = role == Role.Catcher ? 0.2f : 0.15f;
                Piece(PrimitiveType.Sphere, left, rootRotation, GloveOffset, new Vector3(size, 0.07f, size), Quaternion.identity, _gloveMaterial);
            }
        }

        /// <summary>Bounds (actor-local) of the vertices weighted mostly to the head bone, in the current (T-)pose.</summary>
        static Bounds HeadBounds(GameObject go, Transform head)
        {
            var renderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var fallback = go.transform.InverseTransformPoint(head.position) + new Vector3(0f, 0.11f, 0f);
            if (renderer == null || !renderer.sharedMesh.isReadable) return new Bounds(fallback, new Vector3(0.16f, 0.22f, 0.2f));
            var baked = new Mesh();
            renderer.BakeMesh(baked);
            var vertices = baked.vertices;
            var weights = renderer.sharedMesh.boneWeights;
            var bones = renderer.bones;
            var index = System.Array.IndexOf(bones, head);
            var bounds = new Bounds(fallback, Vector3.zero); var any = false;
            for (var v = 0; v < vertices.Length && v < weights.Length; v++)
            {
                var w = weights[v];
                var best = w.boneIndex0; var bestW = w.weight0;
                if (w.weight1 > bestW) { best = w.boneIndex1; bestW = w.weight1; }
                if (w.weight2 > bestW) { best = w.boneIndex2; bestW = w.weight2; }
                if (w.weight3 > bestW) { best = w.boneIndex3; }
                if (best != index) continue;
                var p = go.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[v]));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
            Object.DestroyImmediate(baked);
            return any ? bounds : new Bounds(fallback, new Vector3(0.16f, 0.22f, 0.2f));
        }

        public static Vector3 GloveOffset = new Vector3(0f, -0.07f, 0.02f);

        /// <summary>Adds a primitive to <paramref name="bone"/>, positioned and oriented in the actor's (T-pose) frame so it follows the bone from there.</summary>
        static void Piece(PrimitiveType type, Transform bone, Quaternion frame, Vector3 offset, Vector3 worldScale, Quaternion rotation, Material material)
        {
            var g = GameObject.CreatePrimitive(type);
            var collider = g.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            g.transform.SetParent(bone, false);
            g.transform.rotation = frame * rotation;
            g.transform.position = bone.position + frame * offset;
            var s = bone.lossyScale;
            g.transform.localScale = new Vector3(worldScale.x / Mathf.Max(1e-4f, s.x), worldScale.y / Mathf.Max(1e-4f, s.y), worldScale.z / Mathf.Max(1e-4f, s.z));
            g.GetComponent<MeshRenderer>().sharedMaterial = material;
            g.name = "Gear " + type;
        }
    }
}
