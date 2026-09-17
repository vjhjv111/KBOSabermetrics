using System.Text.RegularExpressions;
using NaverRelayUI.Models;

namespace NaverRelayUI.Parsing
{
    // Confirmed against 497 completed 2026-season games (13,722 pitches):
    // every code maps to exactly one Korean text, no collisions.
    public enum PitchResultCode
    {
        Unknown = 0,
        Ball,           // B  볼
        CalledStrike,   // T  스트라이크 (looking — never co-occurs with 헛스윙 text)
        SwingingStrike, // S  헛스윙
        Foul,           // F  파울
        InPlay,         // H  타격 (result itself is on the next event(s))
        BuntFoul,       // W  번트파울
        BuntSwingMiss,  // V  번트헛스윙 (rare — seen once in 13,722 pitches)
    }

    public static class PitchResultCodeExtensions
    {
        public static PitchResultCode ToPitchResultCode(this string? raw) => raw switch
        {
            "B" => PitchResultCode.Ball,
            "T" => PitchResultCode.CalledStrike,
            "S" => PitchResultCode.SwingingStrike,
            "F" => PitchResultCode.Foul,
            "H" => PitchResultCode.InPlay,
            "W" => PitchResultCode.BuntFoul,
            "V" => PitchResultCode.BuntSwingMiss,
            _ => PitchResultCode.Unknown,
        };
    }

    // Normalized row for a "plate_appearances" table — the BATTER's own outcome
    // only. Runner movement (including the batter's own team's baserunners who
    // score or advance on this play) is broken out separately into RunnerAdvance,
    // because a single play can move several runners and STATIZ-style stats need
    // each of those as its own row (RBI attribution, baserunning value, etc.).
    public class PlateAppearance
    {
        public string GameId { get; set; } = "";
        public int PaNo { get; set; } // TextRelayPlay.No — unique per game, use as PK with GameId
        public int Inning { get; set; }
        public string? HomeOrAway { get; set; } // "0"=home batting, "1"=away batting
        public string? BatterPcode { get; set; }
        public string? BatterName { get; set; }
        public int? BatOrder { get; set; }
        public string? PitcherPcode { get; set; } // pitcher as of PA start
        public string? BatterResultText { get; set; } // the batter's own result line, e.g. "박준순 : 좌익수 앞 1루타"
        public int? BatterResultType { get; set; } // Type of that event (usually 23; walks/Ks may differ — unverified)
        public int HomeScoreBefore { get; set; }
        public int AwayScoreBefore { get; set; }
        public int HomeScoreAfter { get; set; }
        public int AwayScoreAfter { get; set; }
        public double? HomeWinRateAfter { get; set; }
        public double? WpaByPlate { get; set; }
        public int PitchCount { get; set; }
    }

    // One runner-movement line within a play: a steal, a score, an out on the
    // bases, or an advance on a hit/error/wild pitch/etc. Multiple rows can
    // share the same PaNo (e.g. a single scores two runners).
    public class RunnerAdvance
    {
        public string GameId { get; set; } = "";
        public int PaNo { get; set; }
        public int Seqno { get; set; }
        public int FromBase { get; set; } // 1/2/3 — the base the text says the runner started from
        public int? RunnerBatOrder { get; set; } // resolved from FromBase via the pre-play base state
        public string? RunnerName { get; set; } // as named in the text (ground truth for the name)
        public string? ToBaseRaw { get; set; } // "2", "3", "HOME", "OUT" — see IsScore/IsOut for the parsed version
        public bool IsScore { get; set; }
        public bool IsOut { get; set; }
        public string Text { get; set; } = "";
    }

    // Normalized row for a "pitch_events" table — one row per actual pitch.
    public class PitchEvent
    {
        public string GameId { get; set; } = "";
        public int PaNo { get; set; }
        public int Seqno { get; set; }
        public int PitchNum { get; set; }
        public string? Stuff { get; set; } // pitch type
        public double? SpeedKmh { get; set; }
        public PitchResultCode Result { get; set; }
        public string? ResultRaw { get; set; } // keep the original code too, in case new ones show up
        public int? BallsAfter { get; set; }
        public int? StrikesAfter { get; set; }
        public string? PtsPitchId { get; set; }
        public double? CrossPlateX { get; set; }
        public double? CrossPlateY { get; set; }
        public double? Vy0 { get; set; }
        public double? Vz0 { get; set; }
        public double? Vx0 { get; set; }
        public string? BatterStance { get; set; }
    }

    public static class RelayParser
    {
        // Matches e.g. "1루주자 박찬호 : 도루로 2루까지 진루" — group1=base, group2=name
        private static readonly Regex RunnerLine = new(@"^(1|2|3)루주자 (\S+?) ?:", RegexOptions.Compiled);

        // Event types that represent "normal" game flow (pitch, PA start, pitching
        // change, no-pitch events like mound visits/pickoffs) as opposed to result
        // commentary (batted-ball outcome, runner advances). Used to find the game
        // state as of just before a cascading result sequence starts.
        //
        // KNOWN LIMITATION (measured at ~96% accuracy against 6,584 real cases):
        // plays involving errors ("실책"), "다른주자수비하는 사이", tag-outs, or
        // "주자의 재치로" describe MULTIPLE runners against a single shared final
        // state, and the true pre-play anchor for the 2nd/3rd runner in that chain
        // is further back than "the last normal-flow event". Good enough to ship;
        // revisit if RBI/runs-scored attribution on those specific play types needs
        // to be exact.
        private static readonly HashSet<int> NormalFlowTypes = new() { 1, 2, 7, 8 };

        public static (List<PlateAppearance> plateAppearances,
                       List<PitchEvent> pitchEvents,
                       List<RunnerAdvance> runnerAdvances) Flatten(NaverRelayResponse response)
        {
            var plateAppearances = new List<PlateAppearance>();
            var pitchEvents = new List<PitchEvent>();
            var runnerAdvances = new List<RunnerAdvance>();

            var gameId = response.Result?.Game?.GameId ?? response.Result?.TextRelayData?.GameId ?? "";
            var trd = response.Result?.TextRelayData;
            var plays = trd?.TextRelays;
            if (trd == null || plays == null) return (plateAppearances, pitchEvents, runnerAdvances);

            // textRelays[] arrives newest-first; walk it in chronological order
            foreach (var play in plays.AsEnumerable().Reverse())
            {
                var options = play.TextOptions ?? new List<TextOption>();
                if (options.Count == 0) continue;

                var startEvent = options.FirstOrDefault(o => o.Type == 8);
                var firstState = options[0].CurrentGameState;

                // batOrder -> name for whichever team is currently batting, so we
                // can label who's on base. Uses the FINAL boxscore lineup, so a
                // batOrder that changed hands mid-game (pinch hitter/runner) may
                // show the wrong name for earlier plays at that slot — a real gap,
                // noted rather than silently guessed around.
                var battingLineup = play.HomeOrAway == "0" ? trd.HomeLineup : trd.AwayLineup;
                var batOrderToName = (battingLineup?.Batter ?? new List<LineupBatter>())
                    .Where(b => b.BatOrder is >= 1 and <= 9)
                    .GroupBy(b => b.BatOrder)
                    .ToDictionary(g => g.Key, g => g.Last().Name);

                var ptsByPitchId = (play.PtsOptions ?? new List<PtsOption>())
                    .Where(p => p.PitchId != null)
                    .ToDictionary(p => p.PitchId!, p => p);

                CurrentGameState? preResultState = firstState; // state just before the current cascade
                TextOption? batterResultEvent = null;

                foreach (var opt in options)
                {
                    var text = opt.Text ?? "";
                    var runnerMatch = RunnerLine.Match(text);

                    if (NormalFlowTypes.Contains(opt.Type))
                    {
                        preResultState = opt.CurrentGameState ?? preResultState;
                    }
                    else if (runnerMatch.Success)
                    {
                        var fromBase = int.Parse(runnerMatch.Groups[1].Value);
                        var runnerName = runnerMatch.Groups[2].Value;
                        var baseVal = fromBase switch
                        {
                            1 => preResultState?.Base1,
                            2 => preResultState?.Base2,
                            3 => preResultState?.Base3,
                            _ => null,
                        };

                        runnerAdvances.Add(new RunnerAdvance
                        {
                            GameId = gameId,
                            PaNo = play.No,
                            Seqno = opt.Seqno,
                            FromBase = fromBase,
                            RunnerBatOrder = int.TryParse(baseVal, out var bo) ? bo : null,
                            RunnerName = runnerName,
                            IsScore = text.Contains("홈인"),
                            IsOut = text.Contains("아웃"),
                            ToBaseRaw = text.Contains("홈인") ? "HOME"
                                : text.Contains("아웃") ? "OUT"
                                : Regex.Match(text, @"(\d)루까지").Groups[1].Value is { Length: > 0 } b ? b
                                : null,
                            Text = text,
                        });
                    }
                    else
                    {
                        // Not a pitch/PA-start/pitching-change/no-pitch event, and not a
                        // runner-advance line — this is the batter's own result (usually
                        // type 23). Last one wins in the rare case a play logs more than one.
                        batterResultEvent = opt;
                    }
                }

                var lastState = options[^1].CurrentGameState;

                plateAppearances.Add(new PlateAppearance
                {
                    GameId = gameId,
                    PaNo = play.No,
                    Inning = play.Inn,
                    HomeOrAway = play.HomeOrAway,
                    BatterPcode = startEvent?.BatterRecord?.Pcode,
                    BatterName = startEvent?.BatterRecord?.Name,
                    BatOrder = startEvent?.BatterRecord?.BatOrder,
                    PitcherPcode = firstState?.Pitcher,
                    BatterResultText = batterResultEvent?.Text,
                    BatterResultType = batterResultEvent?.Type,
                    HomeScoreBefore = ParseIntOrZero(firstState?.HomeScore),
                    AwayScoreBefore = ParseIntOrZero(firstState?.AwayScore),
                    HomeScoreAfter = ParseIntOrZero(lastState?.HomeScore),
                    AwayScoreAfter = ParseIntOrZero(lastState?.AwayScore),
                    HomeWinRateAfter = play.MetricOption?.HomeTeamWinRate,
                    WpaByPlate = play.MetricOption?.WpaByPlate,
                    PitchCount = options.Count(o => o.PitchNum.HasValue),
                });

                foreach (var ev in options.Where(o => o.PitchNum.HasValue))
                {
                    PtsOption? pts = null;
                    if (ev.PtsPitchId != null)
                        ptsByPitchId.TryGetValue(ev.PtsPitchId, out pts);

                    pitchEvents.Add(new PitchEvent
                    {
                        GameId = gameId,
                        PaNo = play.No,
                        Seqno = ev.Seqno,
                        PitchNum = ev.PitchNum!.Value,
                        Stuff = ev.Stuff,
                        SpeedKmh = ParseDoubleOrNull(ev.Speed),
                        Result = ev.PitchResult.ToPitchResultCode(),
                        ResultRaw = ev.PitchResult,
                        BallsAfter = ParseIntOrNull(ev.CurrentGameState?.Ball),
                        StrikesAfter = ParseIntOrNull(ev.CurrentGameState?.Strike),
                        PtsPitchId = ev.PtsPitchId,
                        CrossPlateX = pts?.CrossPlateX,
                        CrossPlateY = pts?.CrossPlateY,
                        Vy0 = pts?.Vy0,
                        Vz0 = pts?.Vz0,
                        Vx0 = pts?.Vx0,
                        BatterStance = pts?.Stance,
                    });
                }
            }

            return (plateAppearances, pitchEvents, runnerAdvances);
        }

        private static int ParseIntOrZero(string? s) => int.TryParse(s, out var v) ? v : 0;
        private static int? ParseIntOrNull(string? s) => int.TryParse(s, out var v) ? v : null;
        private static double? ParseDoubleOrNull(string? s) => double.TryParse(s, out var v) ? v : null;
    }
}
