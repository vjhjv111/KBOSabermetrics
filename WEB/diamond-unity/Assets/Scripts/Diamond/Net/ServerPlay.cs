using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Diamond.Model;
using Diamond.Stadium;

namespace Diamond.Net
{
    /// <summary>
    /// Drives the 3D replay from the site's local server. Two modes:
    ///  - practice: an endless AI-pitcher practice game (6 plate appearances per game);
    ///  - full match: a friendly match against an AI club. When the user's club bats, each pitch is replayed and the batter swings
    ///    (mouse aim + click, or automated); when it fields, the half-inning is simulated by the server.
    /// The server is authoritative for every result; this class only replays and presents them.
    /// </summary>
    [RequireComponent(typeof(PitchReplayDemo))]
    public sealed class ServerPlay : MonoBehaviour
    {
        [SerializeField] string baseUrl = "http://127.0.0.1:5080";
        [SerializeField] string pace = "practice";           // practice = slow pitches, real, full
        [SerializeField] bool fullMatch = true;              // false: endless practice game
        [SerializeField] string hostTeam = "";               // full match clubs (codes such as LG, HT); empty = random
        [SerializeField] string guestTeam = "";
        [SerializeField, Range(0f, 1f)] float swingChance = 0.85f;
        [SerializeField] bool autoStart = true;
        [SerializeField] bool humanBatter = true;            // true: aim with the mouse and click to swing; false: automated batter
        [SerializeField] bool humanPitcher = true;           // full match: pitch yourself when your club is in the field (else the half-inning is simulated)
        [SerializeField] bool simulateHumanClicks = false;   // test hook: click automatically around the arrival time (exercises the human path)
        [SerializeField] int quitAfterPitches = 0;           // >0: exit the editor after N pitches (automated smoke test)
        [SerializeField] bool captureShots = false;          // test hook: save game-view screenshots (with the HUD) to Captures/
        int _shotCount;

        const double SwingContactMs = 95; // DiamondEngine.SwingContactMs on the server

        PitchReplayDemo _demo;
        GameHud _hud;
        DiamondApiClient _api;
        MatchClient _match;
        string _practiceCode;
        double _clockOffset;           // server ms - local ms
        bool _active;
        int _pitchesPlayed;
        string _shownBatter, _shownPitcher;
        FieldPlayDirector _field;
        string _planBatter; string[] _planBasesBefore; int _planRunsBefore;
        readonly System.Random _rng = new System.Random();

        double ServerNow => Time.realtimeSinceStartupAsDouble * 1000.0 + _clockOffset;

        void Awake()
        {
            _demo = GetComponent<PitchReplayDemo>();
            _demo.AutoPlaySample = false;
            _hud = GetComponent<GameHud>() ?? gameObject.AddComponent<GameHud>();
            _field = GetComponent<FieldPlayDirector>();
        }

        async void Start()
        {
            if (humanBatter || humanPitcher) CreateAimVisuals();
            if (!autoStart) return;
            try { await Run(); }
            catch (Exception e)
            {
                _hud.SetStatus("server unavailable (" + e.Message + ") - showing sample pitch");
                Debug.LogWarning("ServerPlay: " + e.Message);
                _active = false;
                _demo.StartSample();
                SmokeExit(1);
            }
        }

        // --- human batter input ---------------------------------------------------------------------------------
        // Aim space matches the web game: zone edges at +/-1, mapped to the regulation-sized zone (see StrikeZone).
        const float ZoneHalfWidth = Diamond.Sim.StrikeZone.HalfWidth, ZoneCentreY = Diamond.Sim.StrikeZone.CenterY, ZoneHalfHeight = Diamond.Sim.StrikeZone.HalfHeight;
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

        // --- human pitcher input ----------------------------------------------------------------------------------
        // Same controls as the web game: pick a pitch, aim on the zone, then hold the button and release at the centre of
        // the 1.4 s gauge (quality = 1 - |position - 0.5| * 2).
        const float GaugeSeconds = 1.4f;
        bool _awaitingPitchInput;
        string[] _pitchTypes = new string[0];
        int _pitchTypeIndex;
        bool _charging;
        float _chargeStart;
        bool _pitchSubmitted;
        Vector2 _pitchAim;
        double _pitchQuality;

        static string PitchLabel(string type, double velocity) => type switch
        {
            "fastball" => "직구", "slider" => "슬라이더", "curve" => "커브", "changeup" => "체인지업",
            "splitter" => "스플리터", "sinker" => "싱커", "cutter" => "커터", _ => type,
        } + (velocity > 0 ? $"  {velocity:0}km/h" : "");

        void SelectPitch(int index)
        {
            if (index < 0 || index >= _pitchTypes.Length) return;
            _pitchTypeIndex = index;
            RefreshPitchMenu();
        }

        string[] _pitchMenuLabels = new string[0];
        void RefreshPitchMenu() => _hud.SetPitchMenu(_pitchMenuLabels, _pitchTypeIndex);

        void UpdatePitchInput()
        {
            if (!_awaitingPitchInput || _pitchSubmitted) return;
            for (var i = 0; i < _pitchTypes.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SelectPitch(i);
            if (Input.mouseScrollDelta.y != 0 && _pitchTypes.Length > 0)
                SelectPitch((_pitchTypeIndex + (Input.mouseScrollDelta.y < 0 ? 1 : _pitchTypes.Length - 1)) % _pitchTypes.Length);
            if (Input.GetMouseButtonDown(0))
            {
                if (_hud.OverPitchMenu(Input.mousePosition, out var picked)) { SelectPitch(picked); }
                else { _charging = true; _chargeStart = Time.realtimeSinceStartup; }
            }
            if (_charging)
            {
                var pos = ((Time.realtimeSinceStartup - _chargeStart) % GaugeSeconds) / GaugeSeconds;
                _hud.SetGauge(true, pos);
                if (Input.GetMouseButtonUp(0))
                {
                    _charging = false;
                    _hud.SetGauge(false, 0);
                    _pitchQuality = Mathf.Clamp01(1f - Mathf.Abs(pos - 0.5f) * 2f);
                    _pitchAim = _aim;
                    _pitchSubmitted = true;
                }
            }
        }

        void UpdateAim()
        {
            if (_reticle == null) return;
            var aiming = (humanBatter && _awaitingInput) || _awaitingPitchInput;
            _reticle.gameObject.SetActive(aiming);
            if (_zoneBox != null) _zoneBox.enabled = aiming;
            if (!aiming) return;
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out var distance))
            {
                var hit = ray.GetPoint(distance);
                _aim = new Vector2(Mathf.Clamp((float)Diamond.Sim.StrikeZone.AimX(hit.x), -2f, 2f), Mathf.Clamp((hit.y - ZoneCentreY) / ZoneHalfHeight, -2f, 2f));
            }
            _reticle.position = new Vector3((float)Diamond.Sim.StrikeZone.X(_aim.x), ZoneCentreY + _aim.y * ZoneHalfHeight, 0);
            if (_awaitingInput && _clickedAt == null && Input.GetMouseButtonDown(0))
            {
                _clickedAt = ServerNow;
                _clickAim = _aim;
            }
            UpdatePitchInput();
        }

        void Update()
        {
            UpdateAim();
            if (_active) { _demo.Evaluate(ServerNow); if (_field != null) _field.Evaluate(ServerNow); }
        }

        async Task Shot(string name)
        {
            if (!captureShots || _shotCount >= 10) return;
            _shotCount++;
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Captures"));
            System.IO.Directory.CreateDirectory(dir);
            await Task.Yield();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"play_{_shotCount:00}_{name}.png"));
            await Task.Delay(300);
        }

        void SmokeExit(int code)
        {
#if UNITY_EDITOR
            if (quitAfterPitches > 0) UnityEditor.EditorApplication.Exit(code);
#endif
        }

        void Sync(long serverNow) => _clockOffset = serverNow - Time.realtimeSinceStartupAsDouble * 1000.0;
        void Sync(ActionView view) => Sync(view.serverNow);

        async Task WaitServer(double targetMs)
        {
            while (ServerNow < targetMs) await Task.Yield();
        }

        static float ClampAim(double v) => (float)Math.Max(-2.0, Math.Min(2.0, v));

        double Gauss() => Math.Sqrt(-2 * Math.Log(Math.Max(1e-9, _rng.NextDouble()))) * Math.Cos(2 * Math.PI * _rng.NextDouble());

        // --- server operations: the same pitch loop runs against the practice game or the full match ----------------

        async Task<ActionView> OpReady(ActionView v)
        {
            if (_match != null) return (await _match.Command("ready", new { previousPitch = v.pitchCount })).Action;
            return await _api.Post(new { op = "ready", code = _practiceCode, previousPitch = v.pitchCount });
        }

        async Task<ActionView> OpSwing(int pitchId, double inputAt, object aim)
        {
            if (_match != null) return (await _match.Command("swing", new { pitchId, inputAt, aim })).Action;
            return await _api.Post(new { op = "swing", code = _practiceCode, pitchId, inputAt, aim });
        }

        async Task<ActionView> OpPoll()
        {
            if (_match != null) return (await _match.Refresh()).Action;
            return await _api.Get(_practiceCode);
        }

        async Task Run()
        {
            _api = new DiamondApiClient(baseUrl);
            _hud.SetStatus("connecting to " + baseUrl);
            await _api.EnsureSession();
            var roster = await _api.GetRoster();
            if (fullMatch) await RunMatch(roster); else await RunPractice(roster);
        }

        // --- practice: endless 6-plate-appearance games with random players --------------------------------------

        async Task RunPractice(JObject roster)
        {
            var season = roster.Value<int>("season");
            async Task<ActionView> NewGame()
            {
                var batter = Pick(roster["batters"]);
                var pitcher = Pick(roster["pitchers"]);
                var created = await _api.Post(new { op = "create", mode = "ai", role = "batter", batter = batter.Value<string>("id"), pitcher = pitcher.Value<string>("id"), pace, season });
                Sync(created);
                _practiceCode = created.code;
                var pb = batter["profile"]; var pp = pitcher["profile"];
                ApplyPlayers(batter.Value<string>("name"), pb?.Value<string>("bats"), pitcher.Value<string>("name"), pp?.Value<string>("throws"), pp?.Value<string>("delivery"),
                    batter.Value<string>("team"), pitcher.Value<string>("team"));
                return created;
            }

            var view = await NewGame();
            while (true)
            {
                // A practice game ends after 6 plate appearances: start a fresh one.
                if (view.done) { _hud.SetStatus($"game over (score {view.score}) - starting a new game"); view = await NewGame(); }
                var (next, _) = await PlayPitch(view);
                view = next;
                if (QuitReached()) return;
            }
        }

        JToken Pick(JToken list)
        {
            var items = list.ToArray();
            return items[_rng.Next(items.Length)];
        }

        // --- full match -----------------------------------------------------------------------------------------

        async Task RunMatch(JObject roster)
        {
            var season = roster.Value<int>("season");
            var codes = roster["teams"].Select(t => t.Value<string>("code")).ToArray();
            _match = new MatchClient(_api);

            async Task<MatchState> NewMatch()
            {
                var host = !string.IsNullOrEmpty(hostTeam) ? hostTeam : codes[_rng.Next(codes.Length)];
                var guest = !string.IsNullOrEmpty(guestTeam) && guestTeam != host ? guestTeam : codes.Where(c => c != host).OrderBy(_ => _rng.Next()).First();
                var created = await _match.Create(season, host, guest, pace);
                Sync(created.ServerNow);
                _shownBatter = _shownPitcher = null;
                return created;
            }

            var state = await NewMatch();
            while (true)
            {
                PresentState(state);
                if (state.Complete)
                {
                    var winner = state.HomeRuns > state.AwayRuns ? state.TeamName(state.HomeTeam) : state.AwayRuns > state.HomeRuns ? state.TeamName(state.AwayTeam) : "무승부";
                    _hud.ShowBanner($"경기 종료  {state.AwayRuns} : {state.HomeRuns}  ({winner})", new Color(1f, 0.85f, 0.3f), 8f);
                    _hud.SetStatus("경기 종료 - 잠시 후 새 경기를 시작합니다");
                    await Task.Delay(8000);
                    state = await NewMatch();
                    continue;
                }
                var action = state.Action;
                if (humanPitcher && action != null && action.role == "pitcher")
                {
                    // The user's club is in the field: pitch yourself.
                    state = await PlayHumanPitch(state);
                    if (QuitReached()) return;
                    continue;
                }
                SetPitcherView(false);
                if (action == null || action.role != "batter")
                {
                    // The user's club is in the field but pitching is automated: the half-inning is played by the server.
                    _active = false;
                    _hud.SetStatus($"{state.Inning}회 {(state.TopHalf ? "초" : "말")} 수비 — 서버가 이닝을 진행합니다");
                    state = await _match.Command("sim-half");
                    Sync(state.ServerNow);
                    var events = state.RecentEvents(3);
                    if (events.Length > 0) _hud.ShowBanner(events[events.Length - 1], Color.white, 2.5f);
                    continue;
                }

                var upBatter = state.UpcomingBatter ?? action.batter; var upPitcher = state.UpcomingPitcher ?? action.pitcher;
                ApplyPlayers(state.PlayerName(upBatter), state.Bats(upBatter), state.PlayerName(upPitcher), state.Throws(upPitcher), state.Delivery(upPitcher),
                    state.TeamName(state.BattingTeam), state.TeamName(state.BattingTeam == state.HomeTeam ? state.AwayTeam : state.HomeTeam), state.BattingTeam == state.HomeTeam);
                var (_, result) = await PlayPitch(action);
                state = _match.State;
                if (QuitReached()) return;
            }
        }

        void PresentState(MatchState s)
        {
            if (!s.HasGame) return;
            _hud.SetLineScore(s.TeamName(s.AwayTeam), s.TeamName(s.HomeTeam), s.AwayLine, s.HomeLine, s.AwayRuns, s.HomeRuns, s.AwayHits, s.HomeHits);
            var a = s.Action;
            var r = s.BaseRunners;
            _hud.SetSituation(s.Inning, s.TopHalf, a?.balls ?? 0, a?.strikes ?? 0, s.Outs, r[0], r[1], r[2]);
            _hud.SetLog(s.RecentEvents(4));
        }

        void ApplyPlayers(string batterName, string bats, string pitcherName, string throws, string delivery, string batterTeam, string pitcherTeam, bool battingHome = false)
        {
            var offense = TeamLooks.For(batterTeam, battingHome); var defense = TeamLooks.For(pitcherTeam, !battingHome);
            _demo.SetLooks(offense, defense);
            _field?.SetLooks(offense, defense);
            var pitcherLeft = throws == "L";
            // Switch hitters bat from the side opposite the pitcher's throwing hand.
            var batterLeft = bats == "L" || (bats == "S" && !pitcherLeft);
            _demo.SetPitcher(pitcherLeft, delivery);
            _demo.SetBatter(batterLeft);
            var form = delivery == "sidearm" ? "사이드암" : delivery == "underhand" ? "언더핸드" : "오버핸드";
            _hud.SetPlayers($"타자  {batterName} ({batterTeam})  {(batterLeft ? "좌타" : "우타")}{(bats == "S" ? " (양타)" : "")}",
                            $"투수  {pitcherName} ({pitcherTeam})  {(pitcherLeft ? "좌투" : "우투")} {form}");
        }

        bool QuitReached()
        {
            if (quitAfterPitches > 0 && ++_pitchesPlayed >= quitAfterPitches)
            {
                Debug.Log("ServerPlay smoke test ok: " + _pitchesPlayed + " pitches");
                SmokeExit(0);
                return true;
            }
            return false;
        }

        // --- one pitch: replay, swing decision, server verdict ------------------------------------------------------

        async Task<(ActionView view, PitchResult result)> PlayPitch(ActionView view)
        {
            // Next batter steps in right away: fielders, runners and the batter (unhidden, in his box, in the stance) are set before the server round trip.
            BeginPlay(view);
            _demo.ShowStance(ServerNow);
            _active = true;
            view = await OpReady(view);
            Sync(view);
            var pitch = view.pitch;
            if (pitch == null) { await Task.Delay(300); return (view, null); }

            var arrival = pitch.releaseAt + pitch.flightMs;
            var inZone = Math.Abs(pitch.target.x) <= 1.0 && Math.Abs(pitch.target.y) <= 1.0;
            _clickedAt = null;
            _awaitingInput = humanBatter;
            BeginPlay(view);
            _demo.Play(pitch, null);
            _active = true;
            if (_match != null) PresentState(_match.State);
            _hud.SetSituation(_match?.State.Inning ?? 1, _match?.State.TopHalf ?? true, view.balls, view.strikes, _match?.State.Outs ?? 0,
                              _match?.State.BaseRunners[0], _match?.State.BaseRunners[1], _match?.State.BaseRunners[2]);

            double? swingInputAt = null;
            object swingAim = null;
            if (humanBatter)
            {
                _hud.SetStatus($"{pitch.type} {pitch.velocity:0}km/h — 마우스로 조준, 클릭으로 스윙");
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
                    swingAim = new { x = (double)_clickAim.x, y = (double)_clickAim.y };
                    _demo.PlanSwing(clicked + SwingContactMs);
                }
            }
            else
            {
                var swing = _rng.NextDouble() < (inZone ? swingChance : swingChance * 0.35);
                // Human-like timing and aim error so results vary (misses, fouls, grounders, singles, homers).
                var contactMs = arrival + Math.Max(-190, Math.Min(190, Gauss() * 85));
                _hud.SetStatus($"{pitch.type} {pitch.velocity:0}km/h — " + (swing ? "자동 스윙" : "자동 take"));
                if (swing)
                {
                    _demo.PlanSwing(contactMs);
                    await WaitServer(contactMs - SwingContactMs);
                    swingInputAt = contactMs - SwingContactMs;
                    swingAim = new { x = (double)ClampAim(pitch.target.x + Gauss() * 0.3), y = (double)ClampAim(pitch.target.y + Gauss() * 0.3) };
                }
            }
            _awaitingInput = false;

            ActionView after;
            if (swingInputAt is double inputAt)
            {
                after = await OpSwing(pitch.id, inputAt, swingAim);
            }
            else
            {
                await WaitServer(arrival + 900);
                after = await OpPoll();
            }
            return await FinishPitch(view, after, arrival);
        }

        /// <summary>Replays the server's verdict for a pitch, then waits for the play to finish.</summary>
        async Task<(ActionView view, PitchResult result)> FinishPitch(ActionView before, ActionView after, double arrival)
        {
            if (after != null) Sync(after);
            // Only this pitch's own verdict counts: the history's last entry can belong to an earlier pitch (for example a previous
            // hit-by-pitch) while this pitch is still being resolved, which showed a wrong result for a pitch down the middle.
            var wanted = before?.pitch?.id ?? 0;
            PitchResult Verdict(ActionView v)
            {
                if (v?.pitch?.reaction != null && (wanted == 0 || v.pitch.reaction.id == wanted || v.pitch.id == wanted)) return v.pitch.reaction;
                var last = v?.history.LastOrDefault();
                return last != null && (wanted == 0 || last.id == wanted) ? last : null;
            }
            var result = Verdict(after);
            for (var retry = 0; result == null && _match != null && after?.pitch != null && wanted != 0 && after.pitch.id == wanted && retry < 5; retry++)
            {
                await Task.Delay(350);
                after = (await _match.Refresh()).Action;
                Sync(after);
                result = Verdict(after);
            }

            if (result == null)
            {
                // The server already moved on (for example the half-inning ended); show the play-by-play line instead.
                if (_match != null) PresentState(_match.State);
                var events = _match?.State.RecentEvents(1);
                if (events != null && events.Length > 0) _hud.ShowBanner(events[0], Color.white, 3f);
                await WaitServer(arrival + 2500);
                return (after ?? before, null);
            }

            FitToFairTerritory(result);
            _demo.SetResult(result);
            var playEnd = PlanField(result);
            // The verdict (banner, outs, score, log) appears when the play visibly decides it, not at the moment of contact.
            var verdictAt = _field != null ? _field.VerdictMs : 0;
            if (verdictAt > 0) await WaitServer(verdictAt);
            if (_match != null) PresentState(_match.State);
            Announce(result, after);
            if (captureShots) { await Task.Delay(400); await Shot("result"); }
            Debug.Log("ServerPlay result: " + result.label + $" ({result.outcome})" + (result.timing != null ? $" timing {result.timing:0}ms aim error {result.aimError:0.00}" : "") +
                      (result.exitSpeed > 0 ? $" exit {result.exitSpeed:0}km/h launch {result.launchAngle:0}° dist {result.distance:0}m" : ""));
            await WaitServer(Math.Max((result.contact?.at ?? arrival) + 3800, playEnd + 800));
            return (after ?? before, result);
        }

        /// <summary>Starts a plate appearance's pitch: fielders to their spots, runners on their bases, and remember the situation.</summary>
        void BeginPlay(ActionView view)
        {
            var st = _match?.State;
            _planBatter = view?.batter;
            _planBasesBefore = st != null && st.HasGame ? st.BaseRunners.ToArray() : null;
            _planRunsBefore = st != null && st.HasGame ? (st.BattingTeam == st.HomeTeam ? st.HomeRuns : st.AwayRuns) : 0;
            if (_field == null) return;
            _field.ResetField();
            _field.SetRunnersOnBase(_planBasesBefore);
        }

        /// <summary>Plans the post-hit presentation (fielders, runners, thrown ball) and returns when it ends.</summary>
        double PlanField(PitchResult result)
        {
            if (_field == null) return 0;
            var st = _match?.State;
            string[] after = null; var runs = 0;
            if (st != null && st.HasGame && _planBasesBefore != null)
            {
                after = st.BaseRunners.ToArray();
                // The batting team's runs may have been charged to the other half already if the inning ended; never go negative.
                var now = st.BattingTeam == st.HomeTeam ? st.HomeRuns : st.AwayRuns;
                runs = Math.Max(0, now - _planRunsBefore);
            }
            return _field.Plan(result, _planBatter, _planBasesBefore != null ? _planBasesBefore : null, after, runs);
        }

        /// <summary>
        /// The server spreads balls in play over +-75 degrees but fair territory is +-45, so plays it scores as outs or hits could land
        /// in foul ground. The replay shows them squeezed into the fair wedge (the order of pull and opposite-field balls is kept).
        /// </summary>
        static void FitToFairTerritory(PitchResult r)
        {
            if (r == null || r.directionFitted || r.contact == null || r.kind == "foul" || r.trajectory == "foul") return;
            r.directionFitted = true;
            r.direction = r.direction * 0.74 / 1.3;
        }

        void SetPitcherView(bool on)
        {
            var gc = Camera.main != null ? Camera.main.GetComponent<GameCamera>() : null;
            if (gc != null) gc.SetPitcherView(on);
        }

        /// <summary>
        /// The user pitches: choose a pitch type, aim on the zone, hold and release the gauge. The server throws the pitch and the AI
        /// batter decides whether to swing; the replay and the verdict follow as for batting.
        /// </summary>
        async Task<MatchState> PlayHumanPitch(MatchState state)
        {
            var action = state.Action;
            // The action still names the previous batter after a plate appearance ended; the lineup knows who is up.
            var batterId = state.UpcomingBatter ?? action.batter; var pitcherId = state.UpcomingPitcher ?? action.pitcher;
            ApplyPlayers(state.PlayerName(batterId), state.Bats(batterId), state.PlayerName(pitcherId), state.Throws(pitcherId), state.Delivery(pitcherId),
                state.TeamName(state.BattingTeam), state.TeamName(state.BattingTeam == state.HomeTeam ? state.AwayTeam : state.HomeTeam), state.BattingTeam == state.HomeTeam);
            PresentState(state);
            SetPitcherView(true);

            var arsenal = (state.Player(pitcherId)?["arsenal"] as JArray)?.Select(a => (type: a.Value<string>("type"), velocity: a.Value<double?>("velocity") ?? 0)).ToArray()
                          ?? new[] { ("fastball", 140.0) };
            if (arsenal.Length == 0) arsenal = new[] { ("fastball", 140.0) };
            _pitchTypes = arsenal.Select(a => a.type).ToArray();
            _pitchMenuLabels = arsenal.Select(a => PitchLabel(a.type, a.velocity)).ToArray();
            _pitchTypeIndex = 0;
            RefreshPitchMenu();
            _hud.PitchMenuAnchor = new Vector3(0f, 1.1f, 18.44f);

            // Hold everyone in the stance while the user prepares the pitch.
            BeginPlay(action);
            _demo.ShowStance(ServerNow);
            _active = true;
            _pitchSubmitted = false; _charging = false;
            _awaitingPitchInput = true;
            _hud.SetStatus("구종을 고르고 존을 조준한 뒤, 마우스를 누르고 있다가 게이지 가운데에서 놓으세요");
            if (captureShots) { await Task.Delay(500); _hud.SetGauge(true, 0.5f); await Shot("pitch_input"); _hud.SetGauge(false, 0); }

            string type; double ax, ay, quality;
            if (simulateHumanClicks)
            {
                await Task.Delay(900);
                var i = _rng.Next(_pitchTypes.Length);
                type = _pitchTypes[i];
                ax = ClampAim(Gauss() * 0.7); ay = ClampAim(Gauss() * 0.7); quality = 0.8 + 0.2 * _rng.NextDouble();
            }
            else
            {
                while (!_pitchSubmitted) await Task.Yield();
                type = _pitchTypes[_pitchTypeIndex];
                ax = _pitchAim.x; ay = _pitchAim.y; quality = _pitchQuality;
            }
            _awaitingPitchInput = false;
            _hud.SetPitchMenu(null, 0);
            _hud.SetGauge(false, 0);

            var thrown = await _match.Command("pitch", new { previousPitch = action.pitchCount, type, aim = new { x = ax, y = ay }, quality });
            Sync(thrown.ServerNow);
            var view = thrown.Action;
            var pitch = view?.pitch;
            if (pitch == null) { await Task.Delay(300); return _match.State; }

            // The AI batter's swing was prepared with the pitch: replay it (its contact time) or take.
            var arrival = pitch.releaseAt + pitch.flightMs;
            BeginPlay(view);
            _demo.Play(pitch, pitch.aiBatterSwing != null ? pitch.aiBatterSwing.at : (double?)null);
            _active = true;
            _hud.SetStatus($"{PitchLabel(type, pitch.velocity)}  품질 {quality:0.00}");
            // The server resolves the pitch after the arrival window (or shortly after the batter's contact).
            await WaitServer(Math.Max(arrival + 260, (pitch.aiBatterSwing?.at ?? 0) + 60) + 250);
            var after = (await _match.Refresh()).Action;
            await FinishPitch(view, after, arrival);
            return _match.State;
        }

        void Announce(PitchResult r, ActionView view)
        {
            Color c;
            switch (r.outcome)
            {
                case "HR": c = new Color(1f, 0.85f, 0.2f); break;
                case "1B": case "2B": case "3B": case "BB": case "HBP": c = new Color(0.45f, 0.95f, 0.5f); break;
                case "OUT": case "K": c = new Color(1f, 0.45f, 0.4f); break;
                default: c = Color.white; break;
            }
            var detail = r.timing != null ? $"   타이밍 {r.timing:0}ms · 조준 오차 {r.aimError:0.00}" : "";
            if (r.plateLocation != null) detail += $"   실제 코스 {r.plateLocation.x:0.0}, {r.plateLocation.y:0.0}";
            if (r.exitSpeed > 0) detail += $"   타구 {r.exitSpeed:0}km/h · {r.launchAngle:0}° · {r.distance:0}m";
            _hud.ShowBanner(r.label, c, 3.2f);
            _hud.SetStatus($"{view.balls}-{view.strikes}  #{r.id}  {r.label}{detail}");
        }
    }
}
