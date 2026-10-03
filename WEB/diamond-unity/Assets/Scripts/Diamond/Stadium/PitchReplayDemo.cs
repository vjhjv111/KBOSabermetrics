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
        [SerializeField] GameObject catcherPrefab;     // optional: crouching catcher behind the plate (Baseball Catcher)
        [SerializeField] AnimationClip catcherClip;
        [SerializeField] float catcherDepth = 1.5f;    // metres behind the plate
        [SerializeField] AnimationClip homerunClip;    // swing used when the server calls a home run (Baseball Hit_homerun); every other swing uses hitClip
        [SerializeField] float homerunBlendMs = 140f;  // crossfade from the hit swing into the home-run swing
        [SerializeField] float holdMs = 450f;          // hold the follow-through before returning to the stance
        [SerializeField] float returnMs = 650f;        // blend back to the stance
        [SerializeField] bool autoPlaySample = true;
        [SerializeField] float slowMotion = 1f;
        [SerializeField] float windupMs = 1800f;     // game's PITCH_WINDUP_MS
        [SerializeField] float swingClipSpeed = 1.8f; // plays the swing faster than the mocap speed
        [SerializeField] float blendFraction = 0.3f;  // share of the flight used to merge hand position into the server path
        [SerializeField] float loadClipSeconds = 1.3f; // clip time of the fully loaded stance before the swing starts

        sealed class Actor
        {
            public GameObject Go;
            public AnimationClip Clip;
            public PlayableGraph Graph;
            public AnimationClipPlayable Playable;
            public Transform Hand;
            // Batter only: inputs 0 = hit swing, 1 = miss swing, 2 = stance (hit clip frozen at 0).
            public AnimationMixerPlayable Mixer;
            public AnimationClipPlayable[] Clips;
            public AnimationClip HomerunClip;
            public PlayerKit Kit;
        }

        Actor _pitcher, _batter, _catcher;
        Transform _gloveUpper, _gloveLower, _gloveHand;
        Vector3 _catcherHome;
        Transform[] _pArm;               // throwing arm: upper, lower, hand (model's right arm; mirrored for left-handers)
        Transform _pChest, _pSpine;
        string _pitchStyle = "overhand";  // overhand | sidearm | underhand
        [SerializeField] float sideBendSign = 1f;  // flips the lateral torso lean if it tilts the wrong way
        Transform _ball;
        TrailRenderer _trail;
        Pitch _pitch, _samplePitch;
        PitchResult _result, _sampleResult;
        double? _plannedContactMs;   // when the batter's swing contact happens (null = taking the pitch)
        double? _homerunKnownAtMs;   // server-clock time at which the "home run" verdict arrived
        double _hrContact = 1.2, _hrEnd = 2.2;   // contact instant and end of the follow-through in the home-run clip (measured at spawn)
        double _lastMs;              // last evaluated server time
        float _sampleStart;
        Vector3 _handAtRelease;
        bool _released, _sampleMode;

        public Transform Ball => _ball;
        /// <summary>When set and returning a value, the ball is placed there instead of following the pitch/hit path (used by the field play).</summary>
        public System.Func<double, Vector3?> BallOverride;
        /// <summary>Shows or hides the batter at the plate (hidden once the batter becomes a runner).</summary>
        public void SetBatterVisible(bool visible) { if (_batter != null) _batter.Go.SetActive(visible); }
        /// <summary>Uniforms: <paramref name="offense"/> for the batter, <paramref name="defense"/> for the pitcher and catcher.</summary>
        public void SetLooks(PlayerKit.Look offense, PlayerKit.Look defense)
        {
            _batter?.Kit?.SetLook(offense); _pitcher?.Kit?.SetLook(defense); _catcher?.Kit?.SetLook(defense);
        }
        public PlayerKit BatterKit => _batter?.Kit;
        public Animator BatterAnimator => _batter != null ? _batter.Go.GetComponent<Animator>() : null;
        public Vector3 BatterPosition => _batter != null ? _batter.Go.transform.position : Vector3.zero;
        public double LastMs => _lastMs;
        public int PitchId => _pitch != null ? _pitch.id : 0;
        /// <summary>Milliseconds since bat contact if the current pitch has been hit and is in flight, otherwise null.</summary>
        public double? BattedSince => _result?.contact != null && _lastMs >= _result.contact.at ? _lastMs - _result.contact.at : (double?)null;
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

        Actor Spawn(GameObject prefab, AnimationClip clip, Vector3 position, float yaw, PlayerKit.Role role)
        {
            var go = Instantiate(prefab, position, Quaternion.Euler(0, yaw, 0), transform);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            var kit = PlayerKit.Dress(go, role, Resources.Load<Material>("Diamond/Player"));
            var graph = PlayableGraph.Create("Actor " + prefab.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "anim", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            graph.Play();
            return new Actor { Go = go, Kit = kit, Clip = clip, Graph = graph, Playable = playable, Hand = animator.GetBoneTransform(HumanBodyBones.RightHand) };
        }

        /// <summary>Batter rig: a mixer over the hit swing, the miss swing and the stance so swings can crossfade and return to the stance.</summary>
        Actor SpawnBatter(GameObject prefab, AnimationClip hit, AnimationClip homerun, Vector3 position, float yaw)
        {
            var go = Instantiate(prefab, position, Quaternion.Euler(0, yaw, 0), transform);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            var kit = PlayerKit.Dress(go, PlayerKit.Role.Batter, Resources.Load<Material>("Diamond/Player"));
            if (homerun != null)
            {
                _hrContact = MeasureContact(go, homerun);
                _hrEnd = System.Math.Min(_hrContact + 1.1, homerun.length - 0.05);
                animator.Rebind();   // SampleAnimation left the bones posed; let the playable graph drive them from a clean state
            }
            var graph = PlayableGraph.Create("Batter " + prefab.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "anim", animator);
            var mixer = AnimationMixerPlayable.Create(graph, 3);
            var clips = new[]
            {
                AnimationClipPlayable.Create(graph, hit),
                AnimationClipPlayable.Create(graph, homerun != null ? homerun : hit),
                AnimationClipPlayable.Create(graph, hit),
            };
            for (var i = 0; i < clips.Length; i++) graph.Connect(clips[i], 0, mixer, i);
            mixer.SetInputWeight(0, 1f);
            output.SetSourcePlayable(mixer);
            graph.Play();
            return new Actor
            {
                Go = go, Kit = kit, Clip = hit, HomerunClip = homerun, Graph = graph, Mixer = mixer, Clips = clips,
                Hand = animator.GetBoneTransform(HumanBodyBones.RightHand),
            };
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

            _pitcher = Spawn(pitcherPrefab, pitchClip, Field.ToUnity(0, Field.MoundHeight, -Field.MoundDistance), 180, PlayerKit.Role.Pitcher);
            #if UNITY_EDITOR
            // Scenes built before the home-run swing existed have no clip assigned: pick it up from the project.
            if (homerunClip == null)
                homerunClip = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<AnimationClip>(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Motions/Baseball Hit_homerun.fbx")), c => !c.name.StartsWith("__preview__"));
#endif
            _batter = SpawnBatter(batterPrefab, hitClip, homerunClip, Field.ToUnity(-StrikeZone.BatterX, 0, 0), 0);
            var pitcherAnimator = _pitcher.Go.GetComponent<Animator>();
            _pArm = new[]
            {
                pitcherAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm), pitcherAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                pitcherAnimator.GetBoneTransform(HumanBodyBones.RightHand),
            };
            _pChest = pitcherAnimator.GetBoneTransform(HumanBodyBones.UpperChest) ?? pitcherAnimator.GetBoneTransform(HumanBodyBones.Chest);
            _pSpine = pitcherAnimator.GetBoneTransform(HumanBodyBones.Spine);
            if (catcherPrefab != null && catcherClip != null)
            {
                // The catcher squats behind the plate facing the pitcher (clip faces model +z = towards the mound with yaw 0).
                _catcherHome = Field.ToUnity(0, 0, catcherDepth);
                _catcher = Spawn(catcherPrefab, catcherClip, _catcherHome, 0, PlayerKit.Role.Catcher);
                var animator = _catcher.Go.GetComponent<Animator>();
                _gloveUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _gloveLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _gloveHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }

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
            _homerunKnownAtMs = null;
            _result = null;
            _released = false;
            if (_trail != null) _trail.Clear();
        }

        static PitchResult WithZoneContact(PitchResult r) => new PitchResult
        {
            id = r.id, label = r.label, kind = r.kind, outcome = r.outcome, timing = r.timing, aimError = r.aimError, quality = r.quality,
            distance = r.distance, exitSpeed = r.exitSpeed, launchAngle = r.launchAngle, direction = r.direction, points = r.points,
            plateEnded = r.plateEnded, at = r.at, swingAt = r.swingAt, swingAim = r.swingAim, plateLocation = r.plateLocation,
            bodyHit = r.bodyHit, trajectory = r.trajectory,
            contact = new Contact { at = r.contact.at, position = StrikeZone.FromWeb(r.contact.position) },
        };

        /// <summary>Holds everyone in their stance (used while waiting for the user to throw a pitch).</summary>
        public void ShowStance(double nowMs)
        {
            var idle = new Pitch
            {
                id = 0, type = "fastball", velocity = 140, releaseAt = nowMs + 1e7, flightMs = 900,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.32, target = new Vec2(),
            };
            Play(idle, null);
        }

        public void SetResult(PitchResult result)
        {
            // Contact positions come from the server in the web game's plate mapping; show them in the regulation zone.
            _result = result?.contact == null ? result : WithZoneContact(result);
            // A swing that never made contact is a miss: crossfade into the miss swing from the moment the verdict arrives.
            _homerunKnownAtMs = _plannedContactMs != null && result != null && result.outcome == "HR" && homerunClip != null ? _lastMs : (double?)null;
        }

        /// <summary>Plans the batter's swing for the current pitch (human input): contact happens at <paramref name="contactMs"/> on the server clock.</summary>
        public void PlanSwing(double contactMs) => _plannedContactMs = contactMs;

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

        /// <summary>Clip time of the batting stance in "Baseball Hit": its first frames are the walk into the box (feet sunk in the ground for left-handers).</summary>
        const double StanceSeconds = 0.3;

        double BatterClipTime(double ms)
        {
            var arrival = _pitch.releaseAt + _pitch.flightMs;
            var loadStart = _pitch.releaseAt + 0.15 * _pitch.flightMs;
            if (_plannedContactMs is double contact)
            {
                // Swing: the clip's contact (HitContactSeconds) meets the contact time; the load phase before it is spread over the flight.
                var swingStart = contact - (MotionTiming.HitContactSeconds - loadClipSeconds) / swingClipSpeed * 1000.0;
                // Stop at the end of the follow-through: the rest of the clip is the run to first base.
                if (ms >= swingStart) return System.Math.Min(MotionTiming.HitSwingEndSeconds, loadClipSeconds + (ms - swingStart) / 1000.0 * swingClipSpeed);
                // Load during the flight exactly as when taking the pitch, and ease into the swing's first frame so an early click doesn't snap.
                var pre = StanceSeconds + (loadClipSeconds - StanceSeconds) * Smooth((ms - loadStart) / (0.85 * _pitch.flightMs));
                return pre + (loadClipSeconds - pre) * Smooth((ms - (swingStart - 140)) / 140.0);
            }
            // Taking the pitch: load slightly during the flight, then relax back after it passes.
            var load = 1.0 * Smooth((ms - loadStart) / (0.85 * _pitch.flightMs));
            var relax = Smooth((ms - (arrival + 250)) / 700.0);
            return StanceSeconds + (loadClipSeconds - StanceSeconds) * load * (1.0 - relax);
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
            ApplyDeliveryStyle(ms);
            PoseBatter(ms);
            PoseCatcher(ms);
            _lastMs = ms;

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
            var position = batted ? BallFlight.Batted(_result, ms) : BallFlight.PitchedVisual(_pitch, ms);
            var world = Field.ToUnity(position);
            if (!batted)
            {
                var u = (float)((ms - _pitch.releaseAt) / (_pitch.flightMs * blendFraction));
                var offset = (_handAtRelease - Field.ToUnity(BallFlight.PitchedZone(_pitch, _pitch.releaseAt))) * Mathf.Clamp01(1f - u);
                world += offset;
            }
            _ball.position = world;
            if (BallOverride != null) { var o = BallOverride(ms); if (o.HasValue) _ball.position = o.Value; }
        }

        // --- analysis helpers (used by editor capture tools) ---------------------------------------------------

        public float GloveToBall() => _gloveHand != null ? Vector3.Distance(_gloveHand.position, _ball.position) : -1f;

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

        /// <summary>Poses the batter: hit swing (the home-run swing on a home run), then hold and blend back to the stance.</summary>
        void PoseBatter(double ms)
        {
            var b = _batter;
            if (b == null) return;
            var hitT = BatterClipTime(ms);
            double hrT = 0, wHr = 0, wStance = 0;
            if (_plannedContactMs is double contact)
            {
                var isHr = _homerunKnownAtMs != null;
                // Home-run clip time: its contact meets the same contact time.
                hrT = System.Math.Min(_hrEnd, System.Math.Max(0, _hrContact + (ms - contact) / 1000.0 * swingClipSpeed));
                if (isHr)
                {
                    // Fade over the stretch before the swing starts (both clips share the stance), or from the verdict if it came later.
                    var fadeStart = System.Math.Max(_homerunKnownAtMs.Value, contact - (_hrContact - loadClipSeconds) / swingClipSpeed * 1000.0 - homerunBlendMs);
                    wHr = Smooth((ms - fadeStart) / homerunBlendMs);
                }
                wStance = Smooth((ms - (contact + FollowThroughMs(isHr) + holdMs)) / returnMs);
            }
            b.Clips[0].SetTime(Mathf.Clamp((float)hitT, 0f, b.Clip.length - 0.001f));
            b.Clips[1].SetTime(Mathf.Clamp((float)hrT, 0f, (b.HomerunClip != null ? b.HomerunClip.length : b.Clip.length) - 0.001f));
            b.Clips[2].SetTime((float)StanceSeconds);
            b.Mixer.SetInputWeight(0, (float)((1 - wHr) * (1 - wStance)));
            b.Mixer.SetInputWeight(1, (float)(wHr * (1 - wStance)));
            b.Mixer.SetInputWeight(2, (float)wStance);
            b.Graph.Evaluate();
        }

        double FollowThroughMs(bool homerun) => homerun
            ? (_hrEnd - _hrContact) / swingClipSpeed * 1000.0
            : (MotionTiming.HitSwingEndSeconds - MotionTiming.HitContactSeconds) / swingClipSpeed * 1000.0;

        /// <summary>Milliseconds after bat contact at which the batter's follow-through is over (the director then hands him over to the base-running rig).</summary>
        public double SwingFollowThroughMs(bool homerun) => FollowThroughMs(homerun && homerunClip != null);

        /// <summary>Finds when the hands peak in speed in <paramref name="clip"/> (the bat contact) by sampling it on <paramref name="go"/>.</summary>
        static double MeasureContact(GameObject go, AnimationClip clip)
        {
            var animator = go.GetComponent<Animator>();
            var hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null) return clip.length * 0.5;
            const float dt = 1f / 30f;
            var n = Mathf.FloorToInt(clip.length * 0.75f / dt);
            var best = 0f; var bestT = clip.length * 0.5f;
            Vector3 prev = default;
            for (var i = 0; i < n; i++)
            {
                clip.SampleAnimation(go, i * dt);
                var p = hand.position;
                if (i > 0) { var v = (p - prev).magnitude / dt; if (v > best) { best = v; bestT = i * dt; } }
                prev = p;
            }
            return bestT;
        }

        /// <summary>Holds the crouch and reaches the glove hand to where the pitch will arrive, unless the ball is hit.</summary>
        void PoseCatcher(double ms)
        {
            if (_catcher == null) return;
            var arrival = _pitch.releaseAt + _pitch.flightMs;
            var mitt = Field.ToUnity(BallFlight.PitchedVisual(_pitch, arrival + 1000));
            // Body first (starts early: the catcher reads the pitch), then the glove (IK) on arrival.
            // The catcher reacts late so his movement does not give the pitch location away to the person aiming.
            var body = Smooth((ms - (arrival - 260)) / 220.0) * (1.0 - Smooth((ms - (arrival + 1100)) / 600.0));
            var glove = Smooth((ms - (arrival - 140)) / 140.0) * (1.0 - Smooth((ms - (arrival + 1100)) / 500.0));
            if (_result?.contact != null && ms >= _result.contact.at - 50)
            {
                // Ball was hit: no catch, relax back to the crouch.
                var relax = 1.0 - Smooth((ms - (_result.contact.at - 50)) / 150.0);
                body *= relax; glove *= relax;
            }
            // Slide towards the ball, turn the shoulders to it, and stand up for high pitches.
            var shift = Mathf.Clamp(mitt.x * 0.85f, -0.85f, 0.85f) * (float)body;
            var yaw = Mathf.Clamp(mitt.x * 30f, -28f, 28f) * (float)body;
            var stand = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.0f, 1.5f, mitt.y)) * (float)body;
            var t = (float)MotionTiming.CatcherStanceSeconds + stand * (float)(MotionTiming.CatcherStandSeconds - MotionTiming.CatcherStanceSeconds);
            _catcher.Go.transform.SetPositionAndRotation(_catcherHome + new Vector3(shift, 0, 0), Quaternion.Euler(0, yaw, 0));
            Pose(_catcher, t);
            if (glove <= 0.001) return;
            BoneMath.TwoBoneIk(_catcher.Go.transform, _gloveUpper, _gloveLower, _gloveHand, mitt, (float)glove);
        }

        // --- handedness and delivery style ------------------------------------------------------------------------

        /// <summary>Left-handed players are the same rig mirrored across the x axis (all bone adjustments use model space).</summary>
        public void SetPitcher(bool leftHanded, string delivery)
        {
            _pitchStyle = delivery == "sidearm" || delivery == "underhand" ? delivery : "overhand";
            if (_pitcher != null) _pitcher.Go.transform.localScale = new Vector3(leftHanded ? -1f : 1f, 1f, 1f);
        }

        /// <summary>Puts the batter in the right-handed (x &lt; 0) or left-handed (x &gt; 0) box.</summary>
        public void SetBatter(bool leftHanded)
        {
            if (_batter == null) return;
            _batter.Go.transform.position = Field.ToUnity(leftHanded ? StrikeZone.BatterX : -StrikeZone.BatterX, 0, 0);
            _batter.Go.transform.localScale = new Vector3(leftHanded ? -1f : 1f, 1f, 1f);
        }

        /// <summary>
        /// Adapts the overhand mocap to the pitcher's arm slot: tilts the torso towards the throwing side (more for sidearm and
        /// underhand) and moves the throwing hand to the pitch's actual release point around the release.
        /// </summary>
        void ApplyDeliveryStyle(double ms)
        {
            if (_pitcher == null || _pArm == null || _pArm[0] == null) return;
            var rel = _pitch.releaseAt;
            var w = (float)(Smooth((ms - (rel - 650)) / 450.0) * (1.0 - Smooth((ms - (rel + 100)) / 450.0)));
            if (w <= 0.001f) return;
            var root = _pitcher.Go.transform;
            float side, lean;
            switch (_pitchStyle)
            {
                case "sidearm": side = 22f; lean = 14f; break;
                case "underhand": side = 30f; lean = 50f; break;
                default: side = 0f; lean = 0f; break;
            }
            var release = Field.ToUnity(_pitch.releaseX ?? -0.33, _pitch.releaseY ?? 1.84, _pitch.releaseZ ?? -18.32);

            // Where the animated throwing hand is at the clip's release (tilt included), measured once per pitch and style.
            if (!ReferenceEquals(_relModelFor, _pitch))
            {
                Pose(_pitcher, MotionTiming.Pitch1ReleaseSeconds);
                TiltTorso(root, side, lean, 1f);
                _relModel = root.InverseTransformPoint(_pArm[2].position);
                _relModelFor = _pitch;
                var rate = MotionTiming.Pitch1ReleaseSeconds / (windupMs / 1000.0);
                Pose(_pitcher, MotionTiming.Pitch1ReleaseSeconds + (ms - rel) / 1000.0 * rate);
            }
            TiltTorso(root, side, lean, w);
            // Shift the hand along its own arc by the offset between the clip's release and the game's release point, so the arm keeps
            // swinging through the throw instead of being pinned to one spot.
            var current = root.InverseTransformPoint(_pArm[2].position);
            var offset = root.InverseTransformPoint(release) - _relModel;
            BoneMath.TwoBoneIk(root, _pArm[0], _pArm[1], _pArm[2], root.TransformPoint(current + offset * w), 1f);
        }

        Vector3 _relModel; Pitch _relModelFor;

        /// <summary>Tilts the torso towards the throwing side (model +x for the right-handed rig) and bows it forward (model +z).</summary>
        void TiltTorso(Transform root, float side, float lean, float w)
        {
            if (side <= 0f && lean <= 0f) return;
            var sideBend = Quaternion.AngleAxis(-side * w * 0.5f * sideBendSign, Vector3.forward);
            if (_pSpine != null) BoneMath.RotateInModelSpace(_pSpine, root, sideBend * Quaternion.AngleAxis(lean * w * 0.2f, Vector3.right));
            if (_pChest != null) BoneMath.RotateInModelSpace(_pChest, root, sideBend * Quaternion.AngleAxis(lean * w * 0.2f, Vector3.right));
            // Most of the forward bow comes from the waist: pitch the hips and swing the legs back so the feet stay planted.
            var animator = _pitcher.Go.GetComponent<Animator>();
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null || lean <= 0f) return;
            BoneMath.RotateInModelSpace(hips, root, Quaternion.AngleAxis(lean * w * 0.6f, Vector3.right));
            foreach (var leg in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
            {
                var t = animator.GetBoneTransform(leg);
                if (t != null) BoneMath.RotateInModelSpace(t, root, Quaternion.AngleAxis(-lean * w * 0.6f, Vector3.right));
            }
        }

        public Vector3 ThrowingHandPosition => _pArm != null && _pArm[2] != null ? _pArm[2].position : Vector3.zero;

        public float ThrowingShoulderHeight() => _pArm != null && _pArm[0] != null ? _pArm[0].position.y : -1f;

        static void Pose(Actor a, double clipTime)
        {
            if (a == null) return;
            a.Playable.SetTime(Mathf.Clamp((float)clipTime, 0f, a.Clip.length - 0.001f));
            a.Graph.Evaluate();
        }
    }
}
