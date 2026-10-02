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

        async void Start()
        {
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

        void Update()
        {
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
                var swing = _rng.NextDouble() < (inZone ? swingChance : swingChance * 0.35);
                // Human-like timing and aim error so results vary (misses, fouls, grounders, singles, homers).
                var contactMs = arrival + Math.Max(-190, Math.Min(190, Gauss() * 85));
                Debug.Log($"ServerPlay pitch #{pitch.id} {pitch.type} {pitch.velocity:0}km/h target=({pitch.target.x:0.00},{pitch.target.y:0.00}) flight={pitch.flightMs:0}ms zone={inZone} swing={swing}");
                _demo.Play(pitch, swing ? contactMs : (double?)null);
                _active = true;
                _status = $"{view.balls}-{view.strikes}  pitch #{pitch.id} {pitch.type} {pitch.velocity:0}km/h  " + (swing ? "swing" : "take");

                PitchResult result;
                if (swing)
                {
                    await WaitServer(contactMs - SwingContactMs);
                    var aim = new { x = pitch.target.x + Gauss() * 0.3, y = pitch.target.y + Gauss() * 0.3 };
                    view = await _api.Post(new { op = "swing", code, pitchId = pitch.id, inputAt = contactMs - SwingContactMs, aim });
                    result = view.pitch?.reaction ?? view.history.LastOrDefault();
                }
                else
                {
                    await WaitServer(arrival + 900);
                    view = await _api.Get(code);
                    Sync(view);
                    result = view.pitch?.reaction ?? view.history.LastOrDefault();
                }
                if (result == null) { await Task.Delay(300); continue; }

                _demo.SetResult(result);
                _status = $"{view.balls}-{view.strikes}  pitch #{pitch.id}: {result.label} ({result.outcome})" +
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
