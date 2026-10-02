using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Diamond.Model;
using Diamond.Sim;

namespace Diamond.Stadium
{
    /// <summary>
    /// Replays one pitch end to end with Mixamo motions: the pitcher's clip is time-mapped so its release lands on the
    /// pitch's releaseAt, the ball leaves the pitcher's hand and joins the server trajectory, and the batter's clip is
    /// time-mapped so its contact lands on the contact time. Space replays. Timings come from MotionTiming.
    /// </summary>
    public sealed class PitchReplayDemo : MonoBehaviour
    {
        [SerializeField] GameObject pitcherPrefab;
        [SerializeField] AnimationClip pitchClip;
        [SerializeField] GameObject batterPrefab;
        [SerializeField] AnimationClip hitClip;
        [SerializeField] float slowMotion = 1f;
        [SerializeField] float windupMs = 1800f;     // game's PITCH_WINDUP_MS
        [SerializeField] float swingClipSpeed = 1.6f; // plays the swing faster than the mocap speed
        [SerializeField] float blendFraction = 0.3f;  // share of the flight used to merge hand position into the server path

        sealed class Actor
        {
            public GameObject Go;
            public AnimationClip Clip;
            public PlayableGraph Graph;
            public AnimationClipPlayable Playable;
            public Transform Hand;
        }

        Actor _pitcher, _batter;
        Transform _ball;
        TrailRenderer _trail;
        Pitch _pitch;
        PitchResult _result;
        float _start;
        Vector3 _handAtRelease;
        bool _released;

        public Transform Ball => _ball;
        public Pitch Pitch => _pitch;
        public PitchResult Result => _result;

        void Start()
        {
            Setup();
            Restart();
        }

        void OnDestroy()
        {
            DestroyActor(_pitcher);
            DestroyActor(_batter);
        }

        static void DestroyActor(Actor a)
        {
            if (a != null && a.Graph.IsValid()) a.Graph.Destroy();
        }

        Actor Spawn(GameObject prefab, AnimationClip clip, Vector3 position, float yaw)
        {
            var go = Instantiate(prefab, position, Quaternion.Euler(0, yaw, 0), transform);
            var surface = Resources.Load<Material>("Diamond/Player");
            var joints = Resources.Load<Material>("Diamond/PlayerJoints");
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                r.sharedMaterial = r.name.Contains("Joints") ? joints : surface;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            var graph = PlayableGraph.Create("Actor " + prefab.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "anim", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            graph.Play();
            return new Actor { Go = go, Clip = clip, Graph = graph, Playable = playable, Hand = animator.GetBoneTransform(HumanBodyBones.RightHand) };
        }

        public void Setup()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            var collider = ball.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            ball.transform.SetParent(transform, false);
            ball.transform.localScale = Vector3.one * 0.074f;
            var material = Resources.Load<Material>("Diamond/Ball");
            if (material != null) ball.GetComponent<MeshRenderer>().sharedMaterial = material;
            _ball = ball.transform;
            if (Application.isPlaying)
            {
                _trail = ball.AddComponent<TrailRenderer>();
                _trail.time = 2.5f;
                _trail.widthMultiplier = 0.05f;
                _trail.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.white };
            }

            _pitcher = Spawn(pitcherPrefab, pitchClip, Field.ToUnity(0, Field.MoundHeight, -Field.MoundDistance), 180);
            _batter = Spawn(batterPrefab, hitClip, Field.ToUnity(-0.95, 0, 0), 0);

            _pitch = new Pitch
            {
                id = 1, type = "slider", velocity = 140, releaseAt = 2200, flightMs = 1700,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.44 + 0.12,
                target = new Vec2 { x = 0.4, y = -0.3 }, breakX = 0.42, breakY = 0.18,
            };
            _result = new PitchResult
            {
                trajectory = "fly", exitSpeed = 150, launchAngle = 28, direction = 0.3,
                contact = new Contact { at = _pitch.releaseAt + _pitch.flightMs, position = new Position3 { x = 0.2, y = 1.2, z = 0 } },
            };
        }

        void Restart()
        {
            _start = Time.time;
            _released = false;
            if (_trail != null) _trail.Clear();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Restart();
            var ms = (Time.time - _start) / Mathf.Max(0.05f, slowMotion) * 1000.0;
            if (ms > _result.contact.at + 6000) Restart();
            Evaluate(ms);
        }

        /// <summary>Poses everything for game time <paramref name="ms"/> (the pitch's server clock, ms).</summary>
        public void Evaluate(double ms)
        {
            // Pitcher: clip time runs at a constant ratio so that clip release (1.5 s) meets pitch.releaseAt after windupMs.
            var pitchRate = MotionTiming.Pitch1ReleaseSeconds / (windupMs / 1000.0);
            Pose(_pitcher, MotionTiming.Pitch1ReleaseSeconds + (ms - _pitch.releaseAt) / 1000.0 * pitchRate);

            // Batter: contact of the hitting clip meets contact.at; before the swing starts, hold the stance.
            Pose(_batter, MotionTiming.HitContactSeconds + (ms - _result.contact.at) / 1000.0 * swingClipSpeed);

            // Ball: in the hand until release, then merge from the real hand position into the server path.
            Position3 position;
            if (ms < _pitch.releaseAt)
            {
                _handAtRelease = Vector3.zero;
                position = null;
                _ball.position = _pitcher.Hand != null ? _pitcher.Hand.position : Field.ToUnity(_pitch.releaseX ?? 0, _pitch.releaseY ?? 1.8, _pitch.releaseZ ?? -18.4);
                _released = false;
                return;
            }
            if (!_released)
            {
                _released = true;
                _handAtRelease = _ball.position;
            }
            position = ms >= _result.contact.at ? BallFlight.Batted(_result, ms) : BallFlight.Pitched(_pitch, ms);
            var world = Field.ToUnity(position);
            if (ms < _result.contact.at)
            {
                var u = (float)((ms - _pitch.releaseAt) / (_pitch.flightMs * blendFraction));
                var offset = (_handAtRelease - Field.ToUnity(BallFlight.Pitched(_pitch, _pitch.releaseAt))) * Mathf.Clamp01(1f - u);
                world += offset;
            }
            _ball.position = world;
        }

        static void Pose(Actor a, double clipTime)
        {
            if (a == null) return;
            a.Playable.SetTime(Mathf.Clamp((float)clipTime, 0f, a.Clip.length - 0.001f));
            a.Graph.Evaluate();
        }
    }
}
