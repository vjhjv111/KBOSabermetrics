using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Diamond.Model;
using Diamond.Sim;

namespace Diamond.Stadium
{
    /// <summary>
    /// Presents what happens after the ball is put in play: the nearest fielder runs to the ball and catches it or picks it up and
    /// throws, the batter becomes a runner, runners on base advance, and a home run is a trot with a celebration.
    /// The server decides every outcome (out, hit, runs, runners); this class only builds a plausible, consistent replay and
    /// returns how long it lasts. All times are server-clock milliseconds, positions are Unity metres.
    /// </summary>
    public sealed class FieldPlayDirector : MonoBehaviour
    {
        public enum Clip { Idle, Run, Slide, PickUp, Catch, Dive, Throw, Cheer }

        [SerializeField] PitchReplayDemo demo;
        [SerializeField] GameObject actorPrefab;
        [SerializeField] AnimationClip[] clips = new AnimationClip[8];   // indexed by Clip
        [SerializeField] float runSpeed = 7.4f;        // base running, m/s (the web game uses 7.4)
        [SerializeField] float fielderSpeed = 7.5f;    // fielders' nominal sprint speed, m/s
        [SerializeField] float throwSpeed = 27f;       // thrown ball, m/s

        // ---- rigs -----------------------------------------------------------------------------------------------
        sealed class Rig
        {
            public GameObject Go;
            public PlayableGraph Graph;
            public AnimationMixerPlayable Mixer;
            public AnimationClipPlayable[] Playables;
            public Transform Hand;
            public Vector3 Home;
            public readonly List<Seg> Segments = new List<Seg>();
        }

        /// <summary>One stretch of an actor's timeline: a clip, a straight-line move and a facing.</summary>
        sealed class Seg
        {
            public double Start, End;
            public Clip Clip;
            public double ClipFrom, ClipTo;      // clip seconds mapped linearly over [Start, End]; ignored when Loop
            public bool Loop; public double LoopRate = 1.0;
            public Vector3 From, To;
            public bool FaceMove;                // face the direction of travel
            public Vector3 FaceToward;           // otherwise face this point
        }

        sealed class BallSeg
        {
            public double Start, End;
            public Rig Held;                     // attached to this actor's hand, or
            public Vector3 From, To; public float Arc;   // flies from From to To
        }

        Rig[] _fielders = new Rig[7];   // 1B, 2B, SS, 3B, LF, CF, RF
        Rig[] _runners = new Rig[4];    // 0 = batter-runner, 1..3 = runners who started on 1B/2B/3B
        readonly List<BallSeg> _ballSegs = new List<BallSeg>();
        bool _built;
        double _endMs;

        public double EndMs => _endMs;

        static Vector3 Pos(Position3 p) => Field.ToUnity(p);
        static readonly string[] BaseNames = { "home", "first", "second", "third", "home" };
        static Vector3 Base(int i) => Pos(Field.Bases[Mathf.Clamp(i, 0, 4)]);

        // ---- setup ----------------------------------------------------------------------------------------------

        void Start() { Build(); }

        public void Build()
        {
            if (_built || actorPrefab == null) return;
            _built = true;
            for (var i = 0; i < _fielders.Length; i++)
            {
                var spot = Pos(Field.DefensiveSpots[i]);
                _fielders[i] = CreateRig("Fielder " + Field.DefensiveSlots[i], spot);
            }
            for (var i = 0; i < _runners.Length; i++) _runners[i] = CreateRig("Runner " + i, Base(i));
            ResetField();
        }

        Rig CreateRig(string name, Vector3 home)
        {
            var go = Instantiate(actorPrefab, home, Quaternion.identity, transform);
            go.name = name;
            var surface = Resources.Load<Material>("Diamond/Player");
            var joints = Resources.Load<Material>("Diamond/PlayerJoints");
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.sharedMaterial = r.name.Contains("Joints") ? joints : surface;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            var graph = PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "anim", animator);
            var mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            var playables = new AnimationClipPlayable[clips.Length];
            for (var i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i] != null ? clips[i] : clips[0]);
                graph.Connect(playables[i], 0, mixer, i);
            }
            output.SetSourcePlayable(mixer);
            graph.Play();
            return new Rig { Go = go, Graph = graph, Mixer = mixer, Playables = playables, Hand = animator.GetBoneTransform(HumanBodyBones.RightHand), Home = home };
        }

        void OnDestroy()
        {
            foreach (var r in _fielders.Concat(_runners)) if (r != null && r.Graph.IsValid()) r.Graph.Destroy();
        }

        // ---- public API -----------------------------------------------------------------------------------------

        /// <summary>Puts every fielder in position and hides the runners; call at the start of each plate appearance.</summary>
        public void ResetField()
        {
            if (!_built) Build();
            if (!_built) return;
            _ballSegs.Clear();
            _endMs = 0;
            demo.BallOverride = null;
            demo.SetBatterVisible(true);
            foreach (var f in _fielders)
            {
                f.Segments.Clear();
                f.Go.SetActive(true);
                Place(f, f.Home, Quaternion.LookRotation(Base(0) - f.Home), Clip.Idle, 0, true);
            }
            foreach (var r in _runners) { r.Segments.Clear(); r.Go.SetActive(false); }
        }

        /// <summary>Shows runners standing on the bases at the start of a plate appearance (ids on first, second, third; null = empty).</summary>
        public void SetRunnersOnBase(string[] ids)
        {
            if (!_built) return;
            for (var b = 1; b <= 3; b++)
            {
                var rig = _runners[b];
                rig.Segments.Clear();
                var occupied = ids != null && ids.Length >= b && ids[b - 1] != null;
                rig.Go.SetActive(occupied);
                if (occupied) Place(rig, Base(b) + LeadOffset(b), Quaternion.LookRotation(Base(b + 1) - Base(b)), Clip.Idle, 0, true);
            }
        }

        static Vector3 LeadOffset(int b) => (Base(Mathf.Min(b + 1, 4)) - Base(b)).normalized * 1.2f;

        // ---- planning -------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the replay of a ball put in play. <paramref name="basesBefore"/>/<paramref name="basesAfter"/> hold the player ids on
        /// first/second/third before and after the play (null arrays = unknown, only the batter is shown); <paramref name="runsScored"/>
        /// is the number of runs the play produced. Returns the server time at which the play is over.
        /// </summary>
        public double Plan(PitchResult r, string batterId, string[] basesBefore, string[] basesAfter, int runsScored)
        {
            if (!_built) Build();
            if (!_built || r == null) return 0;
            ResetField();
            _ballSegs.Clear();

            var walk = r.outcome == "BB" || r.outcome == "HBP";
            var inPlay = r.contact != null && r.trajectory != "foul" && r.kind != "foul" && !walk;
            if (!inPlay && !walk) return 0;

            double t0 = inPlay ? r.contact.at : r.at;
            var destination = r.outcome switch { "1B" => 1, "2B" => 2, "3B" => 3, "HR" => 4, _ => walk ? 1 : 1 };
            var isHit = destination >= 1 && (r.outcome == "1B" || r.outcome == "2B" || r.outcome == "3B" || r.outcome == "HR");
            var out_ = !isHit && !walk;

            // --- batter becomes a runner ---
            var runStart = t0 + (walk ? 400 : 450);
            if (inPlay) demo.SetBatterVisible(false);
            var batterRunner = _runners[0];
            batterRunner.Go.SetActive(true);
            var batterStart = inPlay ? demo.BatterPosition : Base(0) + new Vector3(-1.15f, 0, 0);
            batterStart.y = 0;
            var batterDest = out_ ? 1 : destination;
            var runnerEnd = BuildRun(batterRunner, batterStart, 0, batterDest, runStart, walk ? 3.2f : runSpeed, afterOut: out_, jogToStop: out_ && r.trajectory != "ground");
            var end = runnerEnd;

            // --- runners already on base ---
            if (basesBefore != null && basesAfter != null)
            {
                for (var b = 1; b <= 3; b++)
                {
                    var id = basesBefore.Length >= b ? basesBefore[b - 1] : null;
                    if (id == null) continue;
                    var rig = _runners[b];
                    rig.Go.SetActive(true);
                    var now = Array.IndexOf(basesAfter, id);
                    int dest;
                    if (now >= 0) dest = now + 1;                       // still on base (advanced or held)
                    else if (runsScored > 0) { dest = 4; runsScored--; } // scored
                    else dest = b;                                       // out or unknown: stays (the play ends there)
                    end = Math.Max(end, BuildRun(rig, Base(b) + LeadOffset(b), b, dest, runStart, walk ? 3.2f : runSpeed, false, false));
                }
            }

            if (inPlay && r.outcome != "HR")
            {
                end = Math.Max(end, BuildFielding(r, t0, destination, out_));
            }
            else if (r.outcome == "HR")
            {
                // Fielders watch the ball leave the park.
                foreach (var f in _fielders) f.Segments.Clear();
            }

            if (_ballSegs.Count > 0) demo.BallOverride = BallPath;
            // A full home-run trot takes about 15 s: cut to the next batter after 6.5 s of it.
            _endMs = r.outcome == "HR" ? Math.Min(end + 600, t0 + 6500) : end + 600;
            return _endMs;
        }

        /// <summary>Runs <paramref name="rig"/> from base <paramref name="from"/> through the bases to <paramref name="to"/> (4 = home).</summary>
        double BuildRun(Rig rig, Vector3 start, int from, int to, double startMs, float speed, bool afterOut, bool jogToStop)
        {
            rig.Segments.Clear();
            var pos = start;
            var t = startMs;
            // Leave the stance quickly, then run leg by leg.
            var legs = new List<Vector3>();
            if (jogToStop)
            {
                // A fly-ball out: a few steps towards first base, then stop.
                legs.Add(pos + (Base(1) - pos).normalized * 6f);
            }
            else
            {
                for (var b = from + 1; b <= to; b++) legs.Add(Base(b));
            }
            foreach (var target in legs)
            {
                var dist = Vector3.Distance(pos, target);
                if (dist < 0.05f) continue;
                var dur = dist / speed * 1000.0;
                rig.Segments.Add(new Seg { Start = t, End = t + dur, Clip = Clip.Run, Loop = true, LoopRate = 1.0, From = pos, To = target, FaceMove = true });
                pos = target; t += dur;
            }
            // Arrived: a runner who scored celebrates, others stand on the bag.
            var last = rig.Segments.Count > 0 ? rig.Segments[rig.Segments.Count - 1] : null;
            var facing = last != null ? last.To + (last.To - last.From).normalized : Base(0);
            if (to == 4) rig.Segments.Add(new Seg { Start = t, End = t + 2900, Clip = Clip.Cheer, ClipFrom = 0, ClipTo = 2.9, From = pos, To = pos, FaceToward = pos + Vector3.forward * 5f });
            else rig.Segments.Add(new Seg { Start = t, End = t + 1e7, Clip = Clip.Idle, Loop = true, From = pos, To = pos, FaceToward = facing });
            return t;
        }

        // ---- fielding -------------------------------------------------------------------------------------------

        /// <summary>Finds the fielder who gets to the ball first and the time/place of the interception.</summary>
        bool FindIntercept(PitchResult r, double t0, out int fielder, out double tI, out Vector3 pI)
        {
            fielder = -1; tI = 0; pI = Vector3.zero;
            var ground = r.trajectory == "ground";
            var prevY = float.MaxValue;
            for (var dt = 200; dt <= 7000; dt += 40)
            {
                var t = t0 + dt;
                var p3 = BallFlight.Batted(r, t);
                if (p3 == null) continue;
                var p = Pos(p3);
                var descending = p.y <= prevY;
                prevY = p.y;
                var catchable = ground ? p.y < 0.4f : (p.y <= 1.6f && descending);
                if (!catchable) continue;
                var best = -1; var bestDist = float.MaxValue;
                for (var i = 0; i < _fielders.Length; i++)
                {
                    var d = Vector2.Distance(new Vector2(_fielders[i].Home.x, _fielders[i].Home.z), new Vector2(p.x, p.z));
                    if (d / fielderSpeed + 0.25f <= dt / 1000f && d < bestDist) { best = i; bestDist = d; }
                }
                if (best >= 0) { fielder = best; tI = t; pI = new Vector3(p.x, 0, p.z); return true; }
            }
            // Nobody can get there in time physically: send the nearest fielder to the landing spot anyway.
            for (var dt = 400; dt <= 7000; dt += 40)
            {
                var p3 = BallFlight.Batted(r, t0 + dt);
                if (p3 == null) continue;
                var p = Pos(p3);
                if (p.y > 0.4f && dt < 6900) continue;
                var best = 0; var bestDist = float.MaxValue;
                for (var i = 0; i < _fielders.Length; i++)
                {
                    var d = Vector2.Distance(new Vector2(_fielders[i].Home.x, _fielders[i].Home.z), new Vector2(p.x, p.z));
                    if (d < bestDist) { best = i; bestDist = d; }
                }
                fielder = best; tI = t0 + dt; pI = new Vector3(p.x, 0, p.z);
                return true;
            }
            return false;
        }

        double BuildFielding(PitchResult r, double t0, int destination, bool outPlay)
        {
            if (!FindIntercept(r, t0, out var fi, out var tI, out var pI)) return t0;
            var fielder = _fielders[fi];
            var flyBall = r.trajectory != "ground";
            var from = fielder.Home;
            var reaction = t0 + 250;
            var home = Base(0);

            if (flyBall && outPlay)
            {
                // Run under the ball and catch it above the head.
                var arrive = tI - 520;
                var dist = Vector3.Distance(from, pI);
                if (arrive <= reaction + 100) arrive = reaction + 100;
                AddRunSeg(fielder, from, pI, reaction, arrive);
                var catchSeg = new Seg { Start = arrive, End = arrive + 1270, Clip = Clip.Catch, ClipFrom = 0, ClipTo = 1.267, From = pI, To = pI, FaceToward = pI + (pI - from).normalized * 4f };
                // Settle the clip so the hands are up when the ball arrives (clip 0.5 s = hands at their highest).
                catchSeg.Start = tI - 500; catchSeg.End = catchSeg.Start + 1270;
                fielder.Segments.Add(catchSeg);
                fielder.Segments.Add(new Seg { Start = catchSeg.End, End = catchSeg.End + 1e7, Clip = Clip.Idle, Loop = true, From = pI, To = pI, FaceToward = home });
                _ballSegs.Add(new BallSeg { Start = tI, End = tI + 1e7, Held = fielder });
                return catchSeg.End;
            }

            // Ground ball, or a ball that drops in for a hit: run to it, pick it up, throw.
            var pickDur = 520.0;
            var runEnd = Math.Max(reaction + 100, tI - pickDur);
            AddRunSeg(fielder, from, pI, reaction, runEnd);
            var pick = new Seg { Start = runEnd, End = runEnd + pickDur, Clip = Clip.PickUp, ClipFrom = 0.8, ClipTo = 2.25, From = pI, To = pI, FaceToward = pI + (pI - from).normalized * 3f };
            fielder.Segments.Add(pick);
            var tP = pick.End;                 // ball in hand
            _ballSegs.Add(new BallSeg { Start = tP, End = tP + 900, Held = fielder });

            // Where does the throw go? Outs on grounders go to first; hits are thrown in to the base ahead of the runner.
            var targetBase = outPlay ? 1 : Mathf.Clamp(destination + 1, 1, 3);
            var receiverIndex = targetBase == 1 ? 0 : targetBase == 2 ? 1 : 3;
            var target = Base(targetBase);
            var receiver = _fielders[receiverIndex];
            // The fielder who gathered it is also the receiver when he is the first baseman covering his own bag.
            var throwStart = tP;
            var windup = 900.0;
            var releaseAt = throwStart + windup;
            fielder.Segments.Add(new Seg { Start = throwStart, End = releaseAt, Clip = Clip.Throw, ClipFrom = 0.35, ClipTo = 1.5, From = pI, To = pI, FaceToward = target });
            fielder.Segments.Add(new Seg { Start = releaseAt, End = releaseAt + 600, Clip = Clip.Throw, ClipFrom = 1.5, ClipTo = 2.2, From = pI, To = pI, FaceToward = target });
            fielder.Segments.Add(new Seg { Start = releaseAt + 600, End = releaseAt + 1e7, Clip = Clip.Idle, Loop = true, From = pI, To = pI, FaceToward = target });

            var flight = Vector3.Distance(pI, target) / throwSpeed * 1000.0;
            var arrival = releaseAt + flight;
            var catchPoint = target + new Vector3(0, 1.2f, 0);
            _ballSegs.Add(new BallSeg { Start = releaseAt, End = arrival, From = pI + Vector3.up * 1.6f, To = catchPoint, Arc = Mathf.Clamp(Vector3.Distance(pI, target) * 0.08f, 0.3f, 3f) });

            if (receiver != fielder)
            {
                // The baseman covers the bag and catches the throw.
                receiver.Segments.Clear();
                var baseSpot = target + (Base(0) - target).normalized * 0.6f;
                var rReact = t0 + 400;
                var rArrive = Math.Min(arrival - 600, rReact + Vector3.Distance(receiver.Home, baseSpot) / fielderSpeed * 1000.0);
                AddRunSeg(receiver, receiver.Home, baseSpot, rReact, Math.Max(rReact + 150, rArrive));
                var catchStart = arrival - 500;
                receiver.Segments.Add(new Seg { Start = Math.Max(rReact + 150, rArrive), End = catchStart, Clip = Clip.Idle, Loop = true, From = baseSpot, To = baseSpot, FaceToward = pI });
                receiver.Segments.Add(new Seg { Start = catchStart, End = catchStart + 1270, Clip = Clip.Catch, ClipFrom = 0, ClipTo = 1.267, From = baseSpot, To = baseSpot, FaceToward = pI });
                receiver.Segments.Add(new Seg { Start = catchStart + 1270, End = catchStart + 1e7, Clip = Clip.Idle, Loop = true, From = baseSpot, To = baseSpot, FaceToward = pI });
                _ballSegs.Add(new BallSeg { Start = arrival, End = arrival + 1e7, Held = receiver });
            }
            else
            {
                _ballSegs.Add(new BallSeg { Start = arrival, End = arrival + 1e7, From = catchPoint, To = catchPoint });
            }
            return arrival;
        }

        void AddRunSeg(Rig rig, Vector3 from, Vector3 to, double start, double end)
        {
            if (Vector3.Distance(from, to) < 0.1f || end <= start)
            {
                rig.Segments.Add(new Seg { Start = start, End = Math.Max(start + 1, end), Clip = Clip.Idle, Loop = true, From = to, To = to, FaceToward = to });
                return;
            }
            // The nominal run speed is stretched when the server's result needs the fielder to arrive earlier than he physically could.
            rig.Segments.Add(new Seg { Start = start, End = end, Clip = Clip.Run, Loop = true, LoopRate = 1.0, From = from, To = to, FaceMove = true });
        }

        // ---- evaluation -----------------------------------------------------------------------------------------

        void Place(Rig rig, Vector3 pos, Quaternion rot, Clip clip, double clipTime, bool loop)
        {
            rig.Go.transform.SetPositionAndRotation(pos, rot);
            for (var i = 0; i < rig.Playables.Length; i++) rig.Mixer.SetInputWeight(i, i == (int)clip ? 1f : 0f);
            var p = rig.Playables[(int)clip];
            var length = p.GetAnimationClip().length;
            p.SetTime(loop ? clipTime % Math.Max(0.01, length) : Math.Min(clipTime, length - 0.001));
            rig.Graph.Evaluate();
        }

        void EvaluateRig(Rig rig, double ms)
        {
            if (rig == null || !rig.Go.activeSelf || rig.Segments.Count == 0) return;
            Seg seg = null;
            foreach (var s in rig.Segments) if (s.Start <= ms) seg = s; else break;
            if (seg == null)
            {
                // Before the first segment: stand still at the first position.
                var first = rig.Segments[0];
                Place(rig, first.From, rig.Go.transform.rotation, Clip.Idle, ms / 1000.0, true);
                return;
            }
            var u = seg.End > seg.Start ? Mathf.Clamp01((float)((ms - seg.Start) / (seg.End - seg.Start))) : 1f;
            var pos = Vector3.Lerp(seg.From, seg.To, u);
            Quaternion rot;
            if (seg.FaceMove && (seg.To - seg.From).sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(new Vector3(seg.To.x - seg.From.x, 0, seg.To.z - seg.From.z));
            else
            {
                var d = seg.FaceToward - seg.To; d.y = 0;
                rot = d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : rig.Go.transform.rotation;
            }
            double ct;
            if (seg.Loop) ct = (ms - seg.Start) / 1000.0 * seg.LoopRate;
            else ct = seg.ClipFrom + (seg.ClipTo - seg.ClipFrom) * u;
            // Smooth turns between segments.
            rot = Quaternion.Slerp(rig.Go.transform.rotation, rot, 0.35f);
            Place(rig, pos, rot, seg.Clip, ct, seg.Loop);
        }

        /// <summary>Poses all fielders and runners for server time <paramref name="ms"/>.</summary>
        public void Evaluate(double ms)
        {
            if (!_built) return;
            foreach (var f in _fielders) EvaluateRig(f, ms);
            foreach (var r in _runners) EvaluateRig(r, ms);
        }

        Vector3? BallPath(double ms)
        {
            BallSeg seg = null;
            foreach (var s in _ballSegs) if (s.Start <= ms) seg = s; else break;
            if (seg == null) return null;
            if (seg.Held != null)
            {
                var hand = seg.Held.Hand != null ? seg.Held.Hand.position : seg.Held.Go.transform.position + Vector3.up;
                return hand;
            }
            var u = seg.End > seg.Start ? Mathf.Clamp01((float)((ms - seg.Start) / (seg.End - seg.Start))) : 1f;
            var p = Vector3.Lerp(seg.From, seg.To, u);
            p.y += seg.Arc * 4f * u * (1f - u);
            return p;
        }
    }
}
