using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Diamond.Model;
using Diamond.Stadium;

namespace Diamond.Net
{
    /// <summary>
    /// Plays a live AI-pitcher vs. automated-batter at-bat sequence against the site's local server: creates a game,
    /// asks for pitches, replays each one (pitcher windup, release, ball flight), decides whether to swing, sends the
    /// swing to the server and replays the server's verdict (miss, foul, hit, out...). The server stays authoritative.
    /// </summary>
    [RequireComponent(typeof(PitchReplayDemo))]
    public sealed class ServerPlay : MonoBehaviour
    {
        [SerializeField] string baseUrl = "http://127.0.0.1:5080";
        [SerializeField] string pace = "practice";           // practice = slow pitches, real, full
        [SerializeField, Range(0f, 1f)] float swingChance = 0.85f;
        [SerializeField] bool autoStart = true;
        [SerializeField] bool humanBatter = true;            // true: aim with the mouse and click to swing; false: automated batter
        [SerializeField] bool simulateHumanClicks = false;   // test hook: click automatically around the arrival time (exercises the human path)
        [SerializeField] int quitAfterPitches = 0;           // >0: exit the editor after N pitches (automated smoke test)

        const double SwingContactMs = 95; // DiamondEngine.SwingContactMs on the server

        PitchReplayDemo _demo;
        DiamondApiClient _api;
        double _clockOffset;           // server ms - local ms
        bool _active;
        string _status = "starting...";
        string _line = "";
        readonly System.Random _rng = new System.Random();

        double ServerNow => Time.realtimeSinceStartupAsDouble * 1000.0 + _clockOffset;

        void Awake()
        {
            _demo = GetComponent<PitchReplayDemo>();
            _demo.AutoPlaySample = false;
        }

        void CreateVisualsIfHuman() { if (humanBatter) CreateAimVisuals(); }

        async void Start()
        {
            CreateVisualsIfHuman();
            if (!autoStart) return;
            try { await Run(); }
            catch (Exception e)
            {
                _status = "server unavailable (" + e.Message + ") - showing sample pitch";
                Debug.LogWarning("ServerPlay: " + e.Message);
                _active = false;
                _demo.StartSample();
                SmokeExit(1);
            }
        }

        // --- human batter input ---------------------------------------------------------------------------------
        // Aim space matches the web game: zone edges at +/-1, the bat's contact point is (x*0.5 m, 1.05 + y*0.55 m).
        const float ZoneHalfWidth = 0.5f, ZoneCentreY = 1.05f, ZoneHalfHeight = 0.55f;
        Transform _reticle;
        LineRenderer _zoneBox;
        Vector2 _aim;
        double? _clickedAt;            // server-clock ms of the swing click for the current pitch
        Vector2 _clickAim;
        bool _awaitingInput;           // a pitch is live and no swing has been made yet

        void CreateAimVisuals()
        {
            var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var box = new GameObject("Strike zone");
            box.transform.SetParent(transform, false);
            _zoneBox = box.AddComponent<LineRenderer>();
            _zoneBox.useWorldSpace = true;
            _zoneBox.loop = true;
            _zoneBox.widthMultiplier = 0.012f;
            _zoneBox.material = new Material(unlit) { color = new Color(1f, 1f, 1f, 0.8f) };
            _zoneBox.positionCount = 4;
            _zoneBox.SetPositions(new[]
            {
                new Vector3(-ZoneHalfWidth, ZoneCentreY - ZoneHalfHeight, 0), new Vector3(ZoneHalfWidth, ZoneCentreY - ZoneHalfHeight, 0),
                new Vector3(ZoneHalfWidth, ZoneCentreY + ZoneHalfHeight, 0), new Vector3(-ZoneHalfWidth, ZoneCentreY + ZoneHalfHeight, 0),
            });
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Aim reticle";
            Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.07f;
            dot.GetComponent<MeshRenderer>().sharedMaterial = new Material(unlit) { color = new Color(1f, 0.25f, 0.2f) };
            _reticle = dot.transform;
        }

        void UpdateAim()
        {
            if (!humanBatter || _reticle == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out var distance))
            {
                var hit = ray.GetPoint(distance);
                _aim = new Vector2(Mathf.Clamp(hit.x / ZoneHalfWidth, -2f, 2f), Mathf.Clamp((hit.y - ZoneCentreY) / ZoneHalfHeight, -2f, 2f));
            }
            _reticle.position = new Vector3(_aim.x * ZoneHalfWidth, ZoneCentreY + _aim.y * ZoneHalfHeight, 0);
            if (_awaitingInput && _clickedAt == null && Input.GetMouseButtonDown(0))
            {
                _clickedAt = ServerNow;
                _clickAim = _aim;
            }
        }

        void Update()
        {
            UpdateAim();
            if (_active) _demo.Evaluate(ServerNow);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 8, 900, 24), "Diamond 3D  |  " + _status);
            if (_line.Length > 0) GUI.Label(new Rect(12, 30, 900, 24), _line);
        }

        int _pitchesPlayed;

        void SmokeExit(int code)
        {
#if UNITY_EDITOR
            if (quitAfterPitches > 0) UnityEditor.EditorApplication.Exit(code);
#endif
        }

        void Sync(ActionView view) => _clockOffset = view.serverNow - Time.realtimeSinceStartupAsDouble * 1000.0;

        async Task WaitServer(double targetMs)
        {
            while (ServerNow < targetMs) await Task.Yield();
        }

        static float ClampAim(double v) => (float)Math.Max(-2.0, Math.Min(2.0, v));

        double Gauss() => Math.Sqrt(-2 * Math.Log(Math.Max(1e-9, _rng.NextDouble()))) * Math.Cos(2 * Math.PI * _rng.NextDouble());

        async Task Run()
        {
            _api = new DiamondApiClient(baseUrl);
            _status = "connecting to " + baseUrl;
            await _api.EnsureSession();
            var roster = await _api.GetRoster();
            var season = roster.Value<int>("season");
            // The prototype has right-handed batting and overhand pitching motions only.
            var batter = roster["batters"].First(b => b["profile"]?.Value<string>("bats") == "R");
            var pitcher = roster["pitchers"].First(p => p["profile"]?.Value<string>("throws") == "R" && p["profile"]?.Value<string>("delivery") == "overhand");
            _line = $"batter {batter.Value<string>("name")} ({batter.Value<string>("team")})  vs  pitcher {pitcher.Value<string>("name")} ({pitcher.Value<string>("team")})  season {season}";

            async Task<ActionView> NewGame()
            {
                var created = await _api.Post(new { op = "create", mode = "ai", role = "batter", batter = batter.Value<string>("id"), pitcher = pitcher.Value<string>("id"), pace, season });
                Sync(created);
                return created;
            }

            var view = await NewGame();
            var code = view.code;

            while (true)
            {
                // A practice game ends after 6 plate appearances: start a fresh one.
                if (view.done)
                {
                    _status = $"game over (score {view.score}) - starting a new game";
                    view = await NewGame();
                    code = view.code;
                }
                view = await _api.Post(new { op = "ready", code, previousPitch = view.pitchCount });
                Sync(view);
                var pitch = view.pitch;
                if (pitch == null) { await Task.Delay(300); continue; }

                var arrival = pitch.releaseAt + pitch.flightMs;
                var inZone = Math.Abs(pitch.target.x) <= 1.0 && Math.Abs(pitch.target.y) <= 1.0;
                _clickedAt = null;
                _awaitingInput = humanBatter;
                _demo.Play(pitch, null);
                _active = true;

                PitchResult result;
                double? swingContact = null;      // server-clock contact time of the swing that was sent, if any
                object swingAim = null;
                double? swingInputAt = null;
                if (humanBatter)
                {
                    _status = $"{view.balls}-{view.strikes}  pitch #{pitch.id} {pitch.type} {pitch.velocity:0}km/h  -  move the mouse to aim, click to swing";
                    // Wait for a click or for the pitch to pass (the server resolves a take after the arrival window).
                    var simulatedClick = arrival - SwingContactMs + Gauss() * 70;
                    while (_clickedAt == null && ServerNow < arrival + 900)
                    {
                        if (simulateHumanClicks && ServerNow >= simulatedClick)
                        {
                            _clickedAt = ServerNow;
                            _clickAim = new Vector2(ClampAim(pitch.target.x + Gauss() * 0.3), ClampAim(pitch.target.y + Gauss() * 0.3));
                        }
                        await Task.Yield();
                    }
                    if (_clickedAt is double clicked)
                    {
                        swingInputAt = clicked;
                        swingContact = clicked + SwingContactMs;
                        swingAim = new { x = (double)_clickAim.x, y = (double)_clickAim.y };
                        _demo.PlanSwing(swingContact.Value);
                    }
                }
                else
                {
                    var swing = _rng.NextDouble() < (inZone ? swingChance : swingChance * 0.35);
                    // Human-like timing and aim error so results vary (misses, fouls, grounders, singles, homers).
                    var contactMs = arrival + Math.Max(-190, Math.Min(190, Gauss() * 85));
                    Debug.Log($"ServerPlay pitch #{pitch.id} {pitch.type} {pitch.velocity:0}km/h target=({pitch.target.x:0.00},{pitch.target.y:0.00}) flight={pitch.flightMs:0}ms zone={inZone} swing={swing}");
                    _status = $"{view.balls}-{view.strikes}  pitch #{pitch.id} {pitch.type} {pitch.velocity:0}km/h  " + (swing ? "swing" : "take");
                    if (swing)
                    {
                        _demo.PlanSwing(contactMs);
                        await WaitServer(contactMs - SwingContactMs);
                        swingInputAt = contactMs - SwingContactMs;
                        swingContact = contactMs;
                        swingAim = new { x = (double)ClampAim(pitch.target.x + Gauss() * 0.3), y = (double)ClampAim(pitch.target.y + Gauss() * 0.3) };
                    }
                }
                _awaitingInput = false;

                if (swingInputAt is double inputAt)
                {
                    view = await _api.Post(new { op = "swing", code, pitchId = pitch.id, inputAt, aim = swingAim });
                    result = view.pitch?.reaction ?? view.history.LastOrDefault();
                }
                else
                {
                    await WaitServer(arrival + 900);
                    view = await _api.Get(code);
                    Sync(view);
                    result = view.pitch?.reaction ?? view.history.LastOrDefault();
                }

                _demo.SetResult(result);
                _status = $"{view.balls}-{view.strikes}  pitch #{pitch.id}: {result.label} ({result.outcome})" + (result.timing != null ? $"  timing {result.timing:0}ms aim error {result.aimError:0.00}" : "") +
                          (result.exitSpeed > 0 ? $"  exit {result.exitSpeed:0}km/h launch {result.launchAngle:0}° dist {result.distance:0}m" : "");
                Debug.Log("ServerPlay result: " + _status);
                await WaitServer((result.contact?.at ?? arrival) + 3800);
                if (quitAfterPitches > 0 && ++_pitchesPlayed >= quitAfterPitches)
                {
                    Debug.Log("ServerPlay smoke test ok: " + _pitchesPlayed + " pitches");
                    SmokeExit(0);
                    return;
                }
            }
        }
    }
}
