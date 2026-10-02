using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Diamond.Model;
using Diamond.Sim;

namespace Diamond.Stadium
{
    /// <summary>
    /// Replays pitches end to end with Mixamo motions: the pitcher's clip is time-mapped so its release lands on the
    /// pitch's releaseAt, the ball leaves the pitcher's hand and joins the server trajectory, and the batter's clip is
    /// time-mapped so its contact lands on the planned/actual contact time. Times are server-clock milliseconds.
    /// Without a server it plays a built-in sample pitch (Space replays). With ServerPlay it is driven through Play/SetResult.
    /// </summary>
    public sealed class PitchReplayDemo : MonoBehaviour
    {
        [SerializeField] GameObject pitcherPrefab;
        [SerializeField] AnimationClip pitchClip;
        [SerializeField] GameObject batterPrefab;
        [SerializeField] AnimationClip hitClip;
        [SerializeField] bool autoPlaySample = true;
        [SerializeField] float slowMotion = 1f;
        [SerializeField] float windupMs = 1800f;     // game's PITCH_WINDUP_MS
        [SerializeField] float swingClipSpeed = 1.6f; // plays the swing faster than the mocap speed
        [SerializeField] float blendFraction = 0.3f;  // share of the flight used to merge hand position into the server path
        [SerializeField] float loadClipSeconds = 1.1f; // clip time of the fully loaded stance before the swing starts

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
        Pitch _pitch, _samplePitch;
        PitchResult _result, _sampleResult;
        double? _plannedContactMs;   // when the batter's swing contact happens (null = taking the pitch)
        float _sampleStart;
        Vector3 _handAtRelease;
        bool _released, _sampleMode;

        public Transform Ball => _ball;
        public Pitch Pitch => _pitch;
        public PitchResult Result => _result;
        public bool AutoPlaySample { get => autoPlaySample; set => autoPlaySample = value; }

        void Start()
        {
            Setup();
            _sampleMode = autoPlaySample;
            if (_sampleMode) RestartSample();
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

            // Built-in sample (used when no server is driving the replay and by the capture tools).
            var sample = new Pitch
            {
                id = 1, type = "slider", velocity = 140, releaseAt = 2200, flightMs = 1700,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.44 + 0.12,
                target = new Vec2 { x = 0.4, y = -0.3 }, breakX = 0.42, breakY = 0.18,
            };
            var contactAt = sample.releaseAt + sample.flightMs;
            _samplePitch = sample;
            _sampleResult = new PitchResult
            {
                trajectory = "fly", exitSpeed = 150, launchAngle = 28, direction = 0.3,
                contact = new Contact { at = contactAt, position = new Position3 { x = 0.2, y = 1.2, z = 0 } },
            };
            Play(_samplePitch, contactAt);
            SetResult(_sampleResult);
        }

        /// <summary>Starts a pitch. <paramref name="swingContactMs"/> is the planned bat-contact time on the server clock, or null to take the pitch.</summary>
        public void Play(Pitch pitch, double? swingContactMs)
        {
            _pitch = pitch;
            _plannedContactMs = swingContactMs;
            _result = null;
            _released = false;
            if (_trail != null) _trail.Clear();
        }

        public void SetResult(PitchResult result) => _result = result;

        /// <summary>Falls back to looping the built-in sample pitch (used when no server is reachable).</summary>
        public void StartSample()
        {
            _sampleMode = true;
            Play(_samplePitch, _samplePitch.releaseAt + _samplePitch.flightMs);
            SetResult(_sampleResult);
            RestartSample();
        }

        void RestartSample()
        {
            _sampleStart = Time.time;
            _released = false;
            if (_trail != null) _trail.Clear();
        }

        void Update()
        {
            if (!_sampleMode) return;
            if (Input.GetKeyDown(KeyCode.Space)) RestartSample();
            var ms = (Time.time - _sampleStart) / Mathf.Max(0.05f, slowMotion) * 1000.0;
            if (ms > (_plannedContactMs ?? 0) + 6000) RestartSample();
            Evaluate(ms);
        }

        // --- timing ------------------------------------------------------------------------------------------

        double BatterClipTime(double ms)
        {
            var arrival = _pitch.releaseAt + _pitch.flightMs;
            var loadStart = _pitch.releaseAt + 0.15 * _pitch.flightMs;
            if (_plannedContactMs is double contact)
            {
                // Swing: the clip's contact (HitContactSeconds) meets the contact time; the load phase before it is spread over the flight.
                var swingStart = contact - (MotionTiming.HitContactSeconds - loadClipSeconds) / swingClipSpeed * 1000.0;
                if (ms >= swingStart) return loadClipSeconds + (ms - swingStart) / 1000.0 * swingClipSpeed;
                return loadClipSeconds * Smooth((ms - loadStart) / (swingStart - loadStart));
            }
            // Taking the pitch: load slightly during the flight, then relax back after it passes.
            var load = 0.45 * Smooth((ms - loadStart) / (0.7 * _pitch.flightMs));
            var relax = Smooth((ms - (arrival + 250)) / 700.0);
            return loadClipSeconds * load * (1.0 - relax);
        }

        static double Smooth(double t)
        {
            t = System.Math.Max(0, System.Math.Min(1, t));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Poses everything for server time <paramref name="ms"/> (the pitch's server clock, ms).</summary>
        public void Evaluate(double ms)
        {
            // Pitcher: clip time runs at a constant ratio so that clip release meets pitch.releaseAt after windupMs.
            var pitchRate = MotionTiming.Pitch1ReleaseSeconds / (windupMs / 1000.0);
            Pose(_pitcher, MotionTiming.Pitch1ReleaseSeconds + (ms - _pitch.releaseAt) / 1000.0 * pitchRate);
            Pose(_batter, BatterClipTime(ms));

            // Ball: in the hand until release, then merge from the real hand position into the server path.
            if (ms < _pitch.releaseAt)
            {
                _ball.position = _pitcher.Hand != null ? _pitcher.Hand.position : Field.ToUnity(_pitch.releaseX ?? 0, _pitch.releaseY ?? 1.8, _pitch.releaseZ ?? -18.4);
                _released = false;
                return;
            }
            if (!_released)
            {
                _released = true;
                _handAtRelease = _ball.position;
            }
            var contact = _result?.contact;
            var batted = contact != null && ms >= contact.at;
            var position = batted ? BallFlight.Batted(_result, ms) : BallFlight.Pitched(_pitch, ms);
            var world = Field.ToUnity(position);
            if (!batted)
            {
                var u = (float)((ms - _pitch.releaseAt) / (_pitch.flightMs * blendFraction));
                var offset = (_handAtRelease - Field.ToUnity(BallFlight.Pitched(_pitch, _pitch.releaseAt))) * Mathf.Clamp01(1f - u);
                world += offset;
            }
            _ball.position = world;
        }

        // --- analysis helpers (used by editor capture tools) ---------------------------------------------------

        public Vector3 PitcherHandAt(double clipSeconds)
        {
            Pose(_pitcher, clipSeconds);
            return _pitcher.Hand.position;
        }

        public Vector3 ToeForward(bool pitcher)
        {
            var a = (pitcher ? _pitcher : _batter).Go.GetComponent<Animator>();
            var f = (a.GetBoneTransform(HumanBodyBones.LeftToes).position - a.GetBoneTransform(HumanBodyBones.LeftFoot).position)
                  + (a.GetBoneTransform(HumanBodyBones.RightToes).position - a.GetBoneTransform(HumanBodyBones.RightFoot).position);
            f.y = 0;
            return f.normalized;
        }

        public Vector3 Facing(bool pitcher)
        {
            var a = (pitcher ? _pitcher : _batter).Go.GetComponent<Animator>();
            var r = a.GetBoneTransform(HumanBodyBones.RightUpperArm).position - a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            r.y = 0;
            return Vector3.Cross(r.normalized, Vector3.up);
        }

        static void Pose(Actor a, double clipTime)
        {
            if (a == null) return;
            a.Playable.SetTime(Mathf.Clamp((float)clipTime, 0f, a.Clip.length - 0.001f));
            a.Graph.Evaluate();
        }
    }
}
