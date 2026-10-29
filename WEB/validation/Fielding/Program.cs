using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Infrastructure.Sqlite;

// 수비 클래식 지표 검증. 인자 없이 실행하면 합성 사례만, DB 경로를 주면 그 DB의 전체 아웃·PO 대조까지 합니다.
//   dotnet run --project WEB/validation/Fielding -- <season.db>
if (args.Length > 1) throw new ArgumentException("Optionally pass a read-only season DB path.");
ChainCases();
BuildCases();
if (args.Length == 1) await DatabaseCase(Path.GetFullPath(args[0]));
Console.WriteLine("Fielding validation passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL " + message);
    Console.WriteLine("PASS " + message);
}

static void ChainCases()
{
    var c = FieldingRecordBuilder.ParseChain("최형우 : 2루수 땅볼 아웃 (2루수->1루수 송구아웃)");
    Check(c.OutFielders.SequenceEqual(["2B", "1B"]) && c.ErrorFielder is null && c.HasOutMarker, "ground out chain 2B->1B");
    c = FieldingRecordBuilder.ParseChain("1루주자 황성빈 : 도루실패아웃 (투수 견제->1루수->유격수->2루수 태그아웃)");
    Check(c.OutFielders.SequenceEqual(["P", "1B", "SS", "2B"]), "pickoff chain strips 견제 and reads four fielders");
    c = FieldingRecordBuilder.ParseChain("류지혁 : 유격수 땅볼 실책으로 출루 (유격수 포구 실책)");
    Check(c.OutFielders.Count == 0 && c.ErrorFielder == "SS", "fielding error chain");
    c = FieldingRecordBuilder.ParseChain("3루주자 김성윤 : 실책으로 홈인 (투수 송구 실책->포수)");
    Check(c.OutFielders.Count == 0 && c.ErrorFielder == "P", "throwing error drops the receiver after the error");
    c = FieldingRecordBuilder.ParseChain("김호령 : 1루수 병살 실책으로 출루 (3루수->2루수->1루수 실책)");
    Check(c.OutFielders.SequenceEqual(["3B", "2B"]) && c.ErrorFielder == "1B", "error at the end keeps the out chain before it");
    c = FieldingRecordBuilder.ParseChain("박민 : 투수 플라이 아웃 (번트)");
    Check(c.IsEmpty, "non-chain parenthetical is ignored");
    c = FieldingRecordBuilder.ParseChain("1루주자 박재현 : 다른주자수비하는 사이 2루까지 진루 (투수->3루수->포수)");
    Check(c.IsEmpty, "fielder sequence without out or error is ignored");
    Check(FieldingRecordBuilder.ParseChain("강민호 : 좌익수 플라이 아웃").IsEmpty, "no parenthetical gives an empty chain");
    Check(FieldingRecordBuilder.ErrorFielderFromText("1루주자 한승택 : 2루수 실책으로 2루까지 진루") == "2B", "error fielder read from runner body text");
    Check(FieldingRecordBuilder.ErrorFielderFromText("3루주자 김현준 : 실책으로 홈인") is null, "body text without a fielder gives no error fielder");
    Check(FieldingRecordBuilder.ErrorFielderFromText("타자 : 좌익수 앞 1루타 (우익수 포구 실책)") is null, "parenthetical error is not read as body text");
}

static void BuildCases()
{
    const string H = "HT", A = "OB";
    var lineup = new List<FieldingLineupSource>
    {
        new("p1", H, "투수1", "투수", 1), new("c1", H, "포수1", "포수", 1), new("b1", H, "1루1", "1루수", 1),
        new("b2", H, "2루1", "2루수", 1), new("b3", H, "3루1", "3루수", 1), new("ss1", H, "유격1", "유격수", 1),
        new("lf1", H, "좌익1", "좌익수", 1), new("cf1", H, "중견1", "우익수", 2), // 종료 시점 위치는 우익수, 시퀀스도 1이 아님 new("rf1", H, "우익1", "우익수", 1),
        new("dh1", H, "지명1", "지명타자", 1), new("ss2", H, "유격2", "유격수", 2), new("ph1", H, "대타1", "대타", 2),
        new("ap", A, "원정투수", "투수", 1), new("ab", A, "원정타자", "1루수", 1),
    };
    FieldingPlateAppearanceSource Pa(int seq, string text, bool isOut, int outs, bool strikeout = false, string? primary = null)
        => new($"pa{seq}", seq, seq * 10, H, A, text, primary, isOut, outs, strikeout);
    var plateAppearances = new List<FieldingPlateAppearanceSource>
    {
        Pa(1, "타자 : 삼진 아웃", true, 1, strikeout: true),
        Pa(2, "타자 : 좌익수 플라이 아웃", true, 1, primary: "좌익수"),
        Pa(3, "타자 : 유격수 병살타 아웃 (유격수->2루수->1루수 송구아웃)", true, 2, primary: "1루수"),
        Pa(4, "타자 : 1루수 병살타 아웃 (1루수 1루 터치아웃)", true, 2, primary: "1루수"),
        Pa(5, "타자 : 유격수 땅볼 실책으로 출루 (유격수 송구 실책->1루수)", false, 0, primary: "1루수"),
        Pa(6, "타자 : 2루수 앞 땅볼로 출루", false, 1),
        Pa(7, "타자 : 투수 땅볼 아웃 (투수->1루수 송구아웃)", true, 2),
        Pa(8, "타자 : 볼넷", false, 0),
        Pa(11, "타자 : 투수 앞 땅볼로 출루", false, 0, primary: "투수"),   // 주자 문구만 실책 → 투수 E
        Pa(12, "타자 : 중견수 앞 1루타", false, 0, primary: "중견수"),      // 안타 뒤 주자 실책 진루 → 수비수 불명, 미분류
        Pa(13, "타자 : 타구맞음 아웃", true, 1),                            // 포수 PO
        Pa(14, "타자 : 1루수 병살타 아웃 (1루수 1루 터치아웃)", true, 2, primary: "1루수"), // 1루수 혼자 두 아웃
        Pa(15, "타자 : 볼넷", false, 0),
        Pa(16, "타자 : 3루수 앞 땅볼로 출루", false, 0, primary: "3루수"),      // 3루수 실책 + 2루수 송구 실책 = 2개
        Pa(17, "타자 : 중견수 플라이 아웃", true, 2, primary: "중견수"),         // 더블 오프 → DP
        Pa(9, "타자 : 3루수 땅볼 아웃 (3루수->1루수 송구아웃)", true, 1),   // 유격수 교체 뒤
        Pa(10, "타자 : 중견수 플라이 아웃", true, 2, primary: "중견수"),
    };
    var runners = new List<FieldingRunnerSource>
    {
        new("pa3", 30, 1, A, 10, true, "1루주자 : 포스아웃 (유격수->2루수 2루 터치아웃)"),            // 병살 연결 반복 → 중복 제외
        new("pa4", 40, 1, A, 11, true, "1루주자 : 태그아웃 (1루수->유격수->2루수->유격수 태그아웃)"), // 두 번째 아웃
        new("pa5", 50, 1, A, 5, false, "1루주자 : 실책으로 3루까지 진루"),                            // 타석 실책과 같은 실책
        new("pa8", 80, 3, A, 5, false, "3루주자 : 포수 실책으로 홈인"),                               // 연결 없는 본문 실책 → 포수 E
        new("pa6", 60, 1, A, 10, true, "1루주자 : 포스아웃 (2루수->유격수 2루 터치아웃)"),
        new("pa7", 70, 1, A, 7, true, "1루주자 : 견제사아웃 (투수 견제->1루수 태그아웃)"),
        new("pa11", 110, 3, A, 5, false, "3루주자 : 실책으로 홈인 (투수 송구 실책->포수)"),   // 같은 투수 실책의 연결 표기 → 중복 아님
        new("pa8", 80, 1, A, 2, false, "1루주자 : 도루로 2루까지 진루"),
        new("pa8", 80, 2, A, 4, false, "2루주자 : 포일로 3루까지 진루"),
        new("pa10", 100, 1, A, 3, true, "1루주자 : 도루실패아웃 (포수->유격수 태그아웃)"),
        new("pa11", 110, 1, A, 5, false, "1루주자 : 실책으로 3루까지 진루"),
        new("pa11", 110, 2, A, 5, false, "2루주자 : 실책으로 홈인"),
        new("pa12", 120, 1, A, 5, false, "1루주자 : 실책으로 3루까지 진루"),
        new("pa14", 140, 1, A, 11, true, "1루주자 : 태그아웃 (1루수 태그아웃)"),
        new("pa15", 150, 1, A, 5, false, "1루주자 : 3루수 실책으로 3루까지 진루 (3루수 송구 실책->1루수)"), // 같은 실책, 주자 둘
        new("pa15", 150, 2, A, 5, false, "2루주자 : 3루수 실책으로 홈인 (3루수 송구 실책->1루수)"),
        new("pa16", 160, 1, A, 5, false, "1루주자 : 2루수 실책으로 2루까지 진루 (2루수 송구 실책->1루수)"),
        new("pa17", 170, 1, A, 11, true, "1루주자 : 태그아웃 (중견수->1루수 태그아웃)"),
    };
    var changes = new List<FieldingChangeSource>
    {
        // 8번 타석 중계 번호(80) 뒤, 9번(90) 전에 유격수 교체. 대타는 수비 위치가 없다가 좌익수로 이동.
        new(85, 1, 1, H, 1, "ss1", "유격수", "ss2", "유격수", null, null, null),
        new(85, 2, 2, H, 1, "lf1", "7번타자", "ph1", "대타", null, null, null),
        new(85, 3, 3, H, 2, null, null, null, null, "ph1", "대타", "좌익수"),
        // 경기 마지막 타석 뒤의 이동: 반영되지 않지만 "중견수였다"는 선발 위치 복원에 쓰입니다.
        new(200, 1, 1, H, 2, null, null, null, null, "cf1", "중견수", "우익수"),
    };
    var game = new FieldingGameSource("G1", H, A, HomeErrors: 6, AwayErrors: 0, lineup, plateAppearances, runners, changes);
    var result = FieldingRecordBuilder.Build([game], AnalyticsGrouping.PlayerByTeam);
    var rows = result.Rows.ToDictionary(r => $"{r.Pcode}|{r.Position}");
    BatterFieldingRecordRow Row(string key) => rows.TryGetValue(key, out var r) ? r : throw new Exception("missing row " + key);

    Check(result.Diagnostics.TotalOuts == 17 && result.Diagnostics.CreditedPutouts == 17 && result.Diagnostics.UnclassifiedOuts == 0
        && result.Diagnostics.OverCreditedOuts == 0, $"all 12 outs credited exactly once ({result.Diagnostics})");
    Check(result.Diagnostics.UnmappedFielderCredits == 0, "every credit mapped to a player");
    var c1 = Row("c1|C");
    Check(c1.Putouts == 2 && c1.Assists == 1, "catcher: strikeout PO, batter-hit-by-batted-ball PO, CS throw assist");
    Check(c1.Errors == 1, "catcher error read from runner body text");
    Check(c1.StolenBasesAllowed == 1 && c1.CaughtStealing == 1 && Math.Abs(c1.CaughtStealingRate!.Value - 0.5) < 1e-9 && c1.PassedBalls == 1,
        "catcher SB/CS/CS%/PB");
    Check(c1.DoublePlays == 0, "strikeout with a separate caught stealing is not a double play unless the PA records 2 outs");
    var ss1 = Row("ss1|SS");
    Check(ss1.Putouts == 2 && ss1.Assists == 2 && ss1.Errors == 1 && ss1.DoublePlays == 2, $"starting SS: PO {ss1.Putouts} A {ss1.Assists} E {ss1.Errors} DP {ss1.DoublePlays}");
    Check(Math.Abs(ss1.FieldingInnings - 3.0) < 1e-9, $"starting SS innings = 9 outs before substitution ({ss1.FieldingInnings})");
    var ss2 = Row("ss2|SS");
    Check(ss2.Games == 1 && ss2.GamesStarted == 0 && ss2.Putouts == 1 && Math.Abs(ss2.FieldingInnings - 8 / 3.0) < 1e-9,
        "substitute SS: appears, no start, CS tag putout, 8 outs of innings");
    var b2 = Row("b2|2B");
    Check(b2.Putouts == 1 && b2.Assists == 3 && b2.DoublePlays == 2, $"2B: DP pivot PO+A, rundown A, force PO (PO {b2.Putouts} A {b2.Assists} DP {b2.DoublePlays})");
    var b1 = Row("b1|1B");
    Check(b1.Putouts == 8 && b1.Assists == 1 && b1.Errors == 0 && b1.DoublePlays == 4, $"1B: PO {b1.Putouts} A {b1.Assists} E {b1.Errors} DP {b1.DoublePlays} (identical chains count as two outs)");
    Check(Row("b3|3B").Errors == 2 && Row("b2|2B").Errors == 1, "grounder reach + different fielder's throwing error = two errors");
    Check(Row("cf1|CF").DoublePlays == 1 && Row("b1|1B").DoublePlays == 4, "fly out with a runner doubled off counts as a double play");
    Check(result.Diagnostics.DoublePlayCount == 4, "four double-play plays counted once each");
    var p1 = Row("p1|P");
    Check(p1.Putouts == 0 && p1.Assists == 2 && p1.Errors == 1 && p1.Pickoffs == 1, "pitcher: ground-ball assist, pickoff assist+PKO, one error for grounder+chain on the same play");

    Check(result.Diagnostics.UnclassifiedErrors == 1, "runner error after a hit stays unclassified (no fielder named)");
    Check(result.Diagnostics.ErrorCheckedTeamGames == 2 && result.Diagnostics.ErrorMismatchTeamGames == 0, "computed team errors match the box score (home 6, away 0)");
    var mismatch = FieldingRecordBuilder.Build([game with { HomeErrors = 3 }], AnalyticsGrouping.PlayerByTeam).Diagnostics;
    Check(mismatch.ErrorMismatchTeamGames == 1 && mismatch.ErrorsOverOfficial == 3 && mismatch.Samples.Any(s => s.StartsWith("실책 과잉 G1 HT 계산 6 공식 3")), "box-score mismatch is reported with the error texts");
    Check(Row("lf1|LF").Putouts == 1 && Row("lf1|LF").Games == 1 && Row("ph1|LF").Games == 1 && Row("ph1|LF").Putouts == 0
        && Math.Abs(Row("ph1|LF").FieldingInnings - 8 / 3.0) < 1e-9, "pinch hitter shifted to LF gets innings after the shift");
    Check(!rows.ContainsKey("dh1|DH") && !rows.ContainsKey("ph1|PH"), "DH and pinch hitter are not fielding rows");
    Check(Row("cf1|CF").Errors == 0 && Row("cf1|CF").Putouts == 2, "no CF error implied by a runner advance on a single; two fly putouts");
    Check(Row("b3|3B").Assists == 1 && Row("cf1|CF").GamesStarted == 1 && !rows.ContainsKey("cf1|RF"), "3B assist and CF fly putout; starting position restored from the later shift");
    Check(Math.Abs(Row("b1|1B").FieldingPercentage!.Value - 1.0) < 1e-9 && Row("b1|1B").TotalChances == 9 && Math.Abs(ss1.FieldingPercentage!.Value - 0.8) < 1e-9, "FPCT=(PO+A)/TC");
    Check(Math.Abs(Row("b1|1B").RangeFactorPerNine!.Value - 27.0 * 9 / 17) < 1e-9, "RF/9=27*(PO+A)/outs");

    var team = FieldingRecordBuilder.Build([game], AnalyticsGrouping.Team).Rows.ToDictionary(r => $"{r.TeamCode}|{r.Position}");
    Check(team["HT|SS"].Putouts == 3 && team["HT|SS"].Pcode is null && team["HT|SS"].Name == "HT" && Math.Abs(team["HT|SS"].FieldingInnings - 17 / 3.0) < 1e-9,
        "team grouping merges both shortstops");
}

static async Task DatabaseCase(string path)
{
    var db = new DatabaseCacheService(path, true);
    var games = await db.QueryFieldingGamesAsync(new GameQuery { SeasonYear = null, Competition = "전체" });
    Check(games.Count > 0, $"loaded {games.Count} games from {path}");
    var result = FieldingRecordBuilder.Build(games, AnalyticsGrouping.PlayerByTeam);
    var d = result.Diagnostics;
    Console.WriteLine($"INFO outs {d.TotalOuts} credited {d.CreditedPutouts} unclassified {d.UnclassifiedOuts} over {d.OverCreditedOuts} unclassified errors {d.UnclassifiedErrors} unmapped {d.UnmappedFielderCredits}");
    foreach (var sample in d.Samples) Console.WriteLine("SAMPLE " + sample);
    var putouts = result.Rows.Sum(r => r.Putouts);
    Check(putouts == d.CreditedPutouts + d.OverCreditedOuts, $"row PO sum {putouts} = credited {d.CreditedPutouts} + over {d.OverCreditedOuts}, outs {d.TotalOuts}");
    Check(d.UnclassifiedOuts + d.OverCreditedOuts <= Math.Max(2, d.TotalOuts / 50), "unclassified + over-credited outs within 2% of outs");
    Check(d.UnmappedFielderCredits == 0, "no credit landed on an empty position");
    Console.WriteLine($"INFO official error check: team-games {d.ErrorCheckedTeamGames}, mismatches {d.ErrorMismatchTeamGames}, over {d.ErrorsOverOfficial}, under {d.ErrorsUnderOfficial}");
    foreach (var teamRows in result.Rows.GroupBy(r => r.TeamCode))
    {
        var teamOuts = teamRows.Where(r => r.Position == "P").Sum(r => r.FieldingInnings) * 3;
        var catcherOuts = teamRows.Where(r => r.Position == "C").Sum(r => r.FieldingInnings) * 3;
        Check(Math.Abs(teamOuts - catcherOuts) < 1e-6, $"{teamRows.Key}: pitcher innings {teamOuts / 3:0.0} match catcher innings {catcherOuts / 3:0.0}");
    }
    foreach (var row in result.Rows.OrderByDescending(r => r.Putouts + r.Assists).Take(8))
        Console.WriteLine($"INFO {row.TeamCode} {row.Position,-2} {row.Name,-6} G{row.Games} GS{row.GamesStarted} Inn{row.FieldingInnings:0.0} PO{row.Putouts} A{row.Assists} E{row.Errors} DP{row.DoublePlays}");
}
