using System.ComponentModel;
using System.Text.RegularExpressions;
using NaverRelay.Application.Queries;

namespace NaverRelay.Application.Statistics;

/// <summary>기록실 수비 탭 한 행. 같은 선수라도 포지션마다 따로 집계합니다.</summary>
public sealed class BatterFieldingRecordRow
{
    [DisplayName("Rank")] public int Rank { get; set; }
    [DisplayName("선수 코드")] public string? Pcode { get; init; }
    [DisplayName("Name")] public string? Name { get; init; }
    [DisplayName("Team")] public string? TeamCode { get; init; }
    [DisplayName("포지션")] public string? Position { get; init; }
    [DisplayName("G")] public int Games { get; init; }
    [DisplayName("GS")] public int GamesStarted { get; init; }
    [DisplayName("Inn")] public double FieldingInnings { get; init; }
    [DisplayName("PO")] public int Putouts { get; init; }
    [DisplayName("A")] public int Assists { get; init; }
    [DisplayName("E")] public int Errors { get; init; }
    [DisplayName("DP")] public int DoublePlays { get; init; }
    [DisplayName("PKO")] public int Pickoffs { get; init; }
    [DisplayName("TC")] public int TotalChances { get; init; }
    [DisplayName("FPCT")] public double? FieldingPercentage { get; init; }
    [DisplayName("RF/9")] public double? RangeFactorPerNine { get; init; }
    [DisplayName("허용 SB")] public int? StolenBasesAllowed { get; init; }
    [DisplayName("저지 CS")] public int? CaughtStealing { get; init; }
    [DisplayName("저지율")] public double? CaughtStealingRate { get; init; }
    [DisplayName("PB")] public int? PassedBalls { get; init; }
}

// 수비 집계에 필요한 경기 원본. DB 조회 계층이 채우고 FieldingRecordBuilder가 소비합니다.
public sealed record FieldingGameSource(string GameId, string? HomeTeamCode, string? AwayTeamCode,
    int? HomeErrors, int? AwayErrors,
    IReadOnlyList<FieldingLineupSource> Lineup, IReadOnlyList<FieldingPlateAppearanceSource> PlateAppearances,
    IReadOnlyList<FieldingRunnerSource> RunnerEvents, IReadOnlyList<FieldingChangeSource> Changes);
public sealed record FieldingLineupSource(string Pcode, string TeamCode, string Name, string? Position, int? LineupSequence);
public sealed record FieldingPlateAppearanceSource(string PlateAppearanceId, int SequenceNumber, int? SourceRelayNo,
    string? FieldingTeamCode, string? BattingTeamCode, string? ResultText, string? PrimaryFielder,
    bool IsOut, int OutsRecorded, bool IsStrikeout);
public sealed record FieldingRunnerSource(string? PlateAppearanceId, int? SourceRelayNo, int? SourceSeqNo,
    string? TeamCode, int Reason, bool IsOut, string? RawText);
public sealed record FieldingChangeSource(int? SourceRelayNo, int? SourceSeqNo, int SourceOptionIndex, string? TeamCode,
    int ChangeType, string? OutPlayerPcode, string? OutPosition, string? InPlayerPcode, string? InPosition,
    string? ShiftPlayerPcode, string? OldPosition, string? NewPosition);

/// <summary>중계 문구에서 뽑은 수비 연결. 실책 토큰 뒤의 수비수(송구 받을 상대)는 버립니다.</summary>
public sealed record FieldingChain(IReadOnlyList<string> OutFielders, string? ErrorFielder, bool HasOutMarker)
{
    public bool IsEmpty => OutFielders.Count == 0 && ErrorFielder is null;
}

public sealed record FieldingDiagnostics(int Games, int TotalOuts, int CreditedPutouts, int UnclassifiedOuts, int OverCreditedOuts,
    int UnclassifiedErrors, int UnmappedFielderCredits, IReadOnlyList<string> Samples,
    int ErrorCheckedTeamGames = 0, int ErrorMismatchTeamGames = 0, int ErrorsOverOfficial = 0, int ErrorsUnderOfficial = 0, int DoublePlayCount = 0)
{
    public const int SampleLimitForDisplay = 20;
}

public sealed record FieldingBuildResult(IReadOnlyList<BatterFieldingRecordRow> Rows, FieldingDiagnostics Diagnostics);

/// <summary>
/// 경기별 수비 배치를 복원하고 아웃·실책 문구의 수비 연결을 읽어 클래식 수비 지표(PO·A·E·DP)를 만듭니다.
/// 공식 기록이 아니라 중계 문구 기반 추정이며, 연결 표기가 없는 아웃은 억지로 배정하지 않고 미분류로 셉니다.
/// </summary>
public static class FieldingRecordBuilder
{
    private static readonly IReadOnlyDictionary<string, string> PositionCodes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["투수"] = "P", ["포수"] = "C", ["1루수"] = "1B", ["2루수"] = "2B", ["3루수"] = "3B",
        ["유격수"] = "SS", ["좌익수"] = "LF", ["중견수"] = "CF", ["우익수"] = "RF",
    };
    private static readonly Regex PositionToken = new("투수|포수|1루수|2루수|3루수|유격수|좌익수|중견수|우익수", RegexOptions.Compiled);
    private static readonly Regex Parenthetical = new(@"\(([^()]*)\)", RegexOptions.Compiled);
    private const int ReasonStolenBase = 2, ReasonCaughtStealing = 3, ReasonPickoff = 7, ReasonForceOut = 10, ReasonTagOut = 11;
    private const int SampleLimit = 40;
    private static readonly Regex ErrorInText = new(@"(투수|포수|1루수|2루수|3루수|유격수|좌익수|중견수|우익수)\s*(?:포구|송구|수비)?\s*실책", RegexOptions.Compiled);

    /// <summary>괄호 연결이 없는 "2루수 실책으로 2루까지 진루"류 본문에서 실책 수비수를 읽습니다.</summary>
    public static string? ErrorFielderFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var body = Parenthetical.Replace(text, "");
        var m = ErrorInText.Match(body);
        return m.Success ? PositionCodes[m.Groups[1].Value] : null;
    }

    public static string? PositionCode(string? korean)
        => korean is not null && PositionCodes.TryGetValue(korean.Trim(), out var code) ? code : null;

    /// <summary>"(유격수->2루수->1루수 송구아웃)"처럼 아웃·실책이 들어간 마지막 괄호를 연결로 읽습니다.</summary>
    public static FieldingChain ParseChain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new([], null, false);
        string? group = null;
        foreach (Match m in Parenthetical.Matches(text))
            if (m.Groups[1].Value.Contains("아웃", StringComparison.Ordinal) || m.Groups[1].Value.Contains("실책", StringComparison.Ordinal))
                group = m.Groups[1].Value;
        if (group is null) return new([], null, false);
        var outFielders = new List<string>();
        string? error = null;
        foreach (var token in group.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var position = PositionToken.Match(token);
            if (!position.Success) continue;
            var code = PositionCodes[position.Value];
            if (token.Contains("실책", StringComparison.Ordinal)) { error = code; break; }
            outFielders.Add(code);
        }
        return new(outFielders, error, group.Contains("아웃", StringComparison.Ordinal));
    }

    public static FieldingBuildResult Build(IEnumerable<FieldingGameSource> games, AnalyticsGrouping grouping)
    {
        var totals = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        int gameCount = 0, totalOuts = 0, credited = 0, unclassifiedOuts = 0, overCredited = 0, unclassifiedErrors = 0, unmapped = 0;
        int errorCheckedTeamGames = 0, errorMismatchTeamGames = 0, errorsOverOfficial = 0, errorsUnderOfficial = 0, doublePlayCount = 0;
        var samples = new List<string>();
        void Sample(string kind, string? text, IEnumerable<string>? extra = null)
        {
            if (samples.Count >= SampleLimit) return;
            var line = kind + ": " + (text ?? "(없음)") + (extra is null ? "" : string.Concat(extra.Select(x => " / " + x)));
            if (!samples.Contains(line)) samples.Add(line);
        }

        foreach (var game in games)
        {
            gameCount++;
            var teamErrors = new Dictionary<string, int>(StringComparer.Ordinal);
            var errorTexts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var alignment = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal); // team -> position -> pcode
            var appeared = new HashSet<string>(StringComparer.Ordinal);
            Tally TallyFor(string team, string position, string pcode)
            {
                names.TryAdd(pcode, pcode);
                var key = grouping switch
                {
                    AnalyticsGrouping.Team => $"{team}|{position}",
                    AnalyticsGrouping.PlayerCareer => $"{pcode}|{position}",
                    _ => $"{pcode}|{team}|{position}",
                };
                if (!totals.TryGetValue(key, out var tally)) totals[key] = tally = new Tally(pcode, team, position);
                if (appeared.Add(key)) tally.Games++;
                return tally;
            }
            Dictionary<string, string> Team(string team)
            {
                if (!alignment.TryGetValue(team, out var map)) alignment[team] = map = new(StringComparer.Ordinal);
                return map;
            }
            static void Remove(Dictionary<string, string> map, string? pcode)
            {
                if (string.IsNullOrEmpty(pcode)) return;
                foreach (var position in map.Where(kv => kv.Value == pcode).Select(kv => kv.Key).ToList()) map.Remove(position);
            }

            var changes = game.Changes.OrderBy(c => c.SourceRelayNo ?? int.MaxValue).ThenBy(c => c.SourceSeqNo ?? int.MaxValue)
                .ThenBy(c => c.SourceOptionIndex).ToList();
            // GamePlayers.Position은 경기 종료 시점 위치이고 LineupSequence도 선발 여부를 보장하지 않습니다.
            // 그래서 선발은 "교체로 들어온 기록이 없는 선수", 선발 위치는 그 선수의 첫 교체·이동 기록에 적힌 이전 위치로 복원합니다.
            var enteredLater = changes.Where(c => c.ChangeType == 1 && !string.IsNullOrEmpty(c.InPlayerPcode)).Select(c => c.InPlayerPcode!).ToHashSet(StringComparer.Ordinal);
            var firstPosition = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var change in changes)
            {
                if (change.ChangeType == 1 && !string.IsNullOrEmpty(change.OutPlayerPcode) && PositionCode(change.OutPosition) is { } outPosition)
                    firstPosition.TryAdd(change.OutPlayerPcode, outPosition);
                else if (change.ChangeType == 2 && !string.IsNullOrEmpty(change.ShiftPlayerPcode) && PositionCode(change.OldPosition) is { } oldPosition)
                    firstPosition.TryAdd(change.ShiftPlayerPcode, oldPosition);
            }
            foreach (var player in game.Lineup)
            {
                names[player.Pcode] = player.Name;
                if (enteredLater.Contains(player.Pcode)) continue;
                var position = firstPosition.GetValueOrDefault(player.Pcode) ?? PositionCode(player.Position);
                if (position is null) continue;
                Team(player.TeamCode)[position] = player.Pcode;
                TallyFor(player.TeamCode, position, player.Pcode).GamesStarted++;
            }

            var runnersByPa = game.RunnerEvents.Where(r => r.PlateAppearanceId is not null)
                .GroupBy(r => r.PlateAppearanceId!).ToDictionary(g => g.Key, g => g.OrderBy(r => r.SourceSeqNo ?? int.MaxValue).ToList(), StringComparer.Ordinal);
            var changeIndex = 0;
            void ApplyChange(FieldingChangeSource change)
            {
                if (string.IsNullOrEmpty(change.TeamCode)) return;
                var map = Team(change.TeamCode);
                if (change.ChangeType == 1)
                {
                    Remove(map, change.OutPlayerPcode);
                    if (PositionCode(change.InPosition) is { } position && !string.IsNullOrEmpty(change.InPlayerPcode))
                    { Remove(map, change.InPlayerPcode); map[position] = change.InPlayerPcode; }
                }
                else if (change.ChangeType == 2 && !string.IsNullOrEmpty(change.ShiftPlayerPcode))
                {
                    Remove(map, change.ShiftPlayerPcode);
                    if (PositionCode(change.NewPosition) is { } position) map[position] = change.ShiftPlayerPcode;
                }
            }

            foreach (var pa in game.PlateAppearances.OrderBy(p => p.SequenceNumber))
            {
                // 같은 중계 번호에 묶인 교체는 그 타석 결과보다 먼저 반영합니다.
                while (changeIndex < changes.Count && (changes[changeIndex].SourceRelayNo ?? int.MaxValue) <= (pa.SourceRelayNo ?? int.MaxValue))
                    ApplyChange(changes[changeIndex++]);
                var fieldingTeam = pa.FieldingTeamCode;
                if (string.IsNullOrEmpty(fieldingTeam))
                    fieldingTeam = pa.BattingTeamCode == game.HomeTeamCode ? game.AwayTeamCode : game.HomeTeamCode;
                if (string.IsNullOrEmpty(fieldingTeam)) { unclassifiedOuts += pa.OutsRecorded; totalOuts += pa.OutsRecorded; continue; }
                var map = Team(fieldingTeam);
                totalOuts += pa.OutsRecorded;

                var creditedHere = new List<Tally>();
                int putoutsHere = 0;
                void Credit(string position, int po, int a, int e)
                {
                    if (!map.TryGetValue(position, out var pcode)) { unmapped += po + a + e; putoutsHere += po; Sample($"담당 미확인 {position}", pa.ResultText); return; }
                    var tally = TallyFor(fieldingTeam, position, pcode);
                    tally.Putouts += po; tally.Assists += a; tally.Errors += e; putoutsHere += po;
                    if (e > 0) teamErrors[fieldingTeam] = teamErrors.GetValueOrDefault(fieldingTeam) + e;
                    if ((po > 0 || a > 0) && !creditedHere.Contains(tally)) creditedHere.Add(tally);
                }
                void CreditChain(IReadOnlyList<string> chain, int outs)
                {
                    if (chain.Count == 0 || outs <= 0) return;
                    var putoutFrom = Math.Max(0, chain.Count - outs);
                    var assisted = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < chain.Count; i++)
                    {
                        var po = i >= putoutFrom ? 1 : 0;
                        var a = i < chain.Count - 1 && assisted.Add(chain[i]) ? 1 : 0;
                        Credit(chain[i], po, a, 0);
                    }
                }

                var paChain = ParseChain(pa.ResultText);
                var paHasError = pa.ResultText?.Contains("실책", StringComparison.Ordinal) == true;
                var runnerOuts = 0; var runnerCaughtStealing = false; var runnerForcedOrTagged = false;
                var runnerErrorChains = new HashSet<string>(StringComparer.Ordinal);
                var passedBall = false;
                // "투수 앞 땅볼로 출루"처럼 안타도 아웃도 아닌 출루 타구에 주자 문구만 "실책으로 진루"면 타구를 처리한 수비수의 실책 1개로 봅니다.
                // 안타·볼넷·아웃 뒤의 "실책으로 진루"는 어느 수비수의 실책인지 문구로 알 수 없어 미분류로 둡니다
                // (안타 뒤 실책을 타구 방향 수비수에게 주면 리그 실책 합계가 공식 기록을 넘었습니다).
                var reachedOnBattedBall = !pa.IsOut && pa.OutsRecorded == 0 && !paHasError && PositionCode(pa.PrimaryFielder) is not null
                    && pa.ResultText is { } reachText && reachText.Contains("로 출루", StringComparison.Ordinal)
                    && !reachText.Contains("안타", StringComparison.Ordinal) && !reachText.Contains("루타", StringComparison.Ordinal);
                var impliedError = false;
                var runnerErrorFielders = new HashSet<string>(StringComparer.Ordinal);
                foreach (var runner in runnersByPa.GetValueOrDefault(pa.PlateAppearanceId) ?? [])
                {
                    var chain = ParseChain(runner.RawText);
                    if (runner.Reason == ReasonStolenBase && map.TryGetValue("C", out var catcher))
                        TallyFor(fieldingTeam, "C", catcher).StolenBasesAllowed++;
                    if (runner.RawText?.Contains("포일", StringComparison.Ordinal) == true) passedBall = true;
                    if (runner.IsOut)
                    {
                        if (runner.Reason == ReasonCaughtStealing) runnerCaughtStealing = true;
                        if (runner.Reason is ReasonForceOut or ReasonTagOut) runnerForcedOrTagged = true;
                        // 병살 타석의 "포스아웃 (유격수->2루수 ...)"는 타석 연결의 앞부분을 되풀이한 것이라 한 번만 셉니다.
                        // 연결이 완전히 같으면("1루수 1루 터치아웃" + "1루수 태그아웃") 같은 수비수가 두 아웃을 잡은 별개 플레이입니다.
                        if (runner.Reason is ReasonForceOut or ReasonTagOut && IsProperPrefix(chain.OutFielders, paChain.OutFielders) && pa.OutsRecorded >= 2) continue;
                        runnerOuts++;
                        if (chain.OutFielders.Count > 0) CreditChain(chain.OutFielders, 1);
                        if (runner.Reason == ReasonCaughtStealing && chain.OutFielders.Count > 0 && map.TryGetValue("C", out var c))
                            TallyFor(fieldingTeam, "C", c).CaughtStealing++;
                    }
                    var runnerError = chain.ErrorFielder ?? ErrorFielderFromText(runner.RawText);
                    // 출루 타구에 주자 실책 문구가 붙으면 타구 처리 수비수의 실책으로 보되, 아래에서 같은 수비수의 연결 실책과는 합칩니다.
                    if (reachedOnBattedBall && runner.RawText?.Contains("실책", StringComparison.Ordinal) == true) impliedError = true;
                    // 같은 실책으로 여러 주자가 진루하면 주자별 문구가 따로 오므로, 연결 괄호(없으면 수비수 코드) 기준으로 한 번만 셉니다.
                    if (runnerError is not null && runnerError != (paChain.ErrorFielder ?? ErrorFielderFromText(pa.ResultText)) && runnerErrorChains.Add(ErrorKey(runner.RawText, runnerError)))
                    { Credit(runnerError, 0, 0, 1); runnerErrorFielders.Add(runnerError); }
                    else if (runnerError is null && runner.RawText?.Contains("실책", StringComparison.Ordinal) == true && !paHasError)
                    {
                        if (!reachedOnBattedBall) { unclassifiedErrors++; Sample("미분류 실책", runner.RawText, [pa.ResultText ?? ""]); }
                    }
                    // 견제사: 연결의 첫 수비수(보통 투수)에게 PKO를 줍니다. 연결 없는 견제사는 투수로 봅니다.
                    if (runner.IsOut && runner.Reason == ReasonPickoff)
                    {
                        var pickoffBy = chain.OutFielders.Count > 0 ? chain.OutFielders[0] : "P";
                        if (map.TryGetValue(pickoffBy, out var pickoffPcode)) TallyFor(fieldingTeam, pickoffBy, pickoffPcode).Pickoffs++;
                    }
                }

                var batterOuts = pa.OutsRecorded - runnerOuts;
                if (batterOuts > 0)
                {
                    if (paChain.OutFielders.Count > 0) CreditChain(paChain.OutFielders, batterOuts);
                    else if (pa.IsOut && pa.IsStrikeout) Credit("C", 1, 0, 0);
                    // 타자가 자기 타구에 맞아 아웃되면 공식 기록 규칙상 포수 자살입니다.
                    else if (pa.IsOut && pa.ResultText?.Contains("타구맞음", StringComparison.Ordinal) == true) Credit("C", 1, 0, 0);
                    else if (pa.IsOut && PositionCode(pa.PrimaryFielder) is { } fielder) Credit(fielder, 1, 0, 0);
                }
                // 같은 플레이의 주자 문구가 이미 그 수비수 실책을 연결로 밝혔으면(예: "투수 송구 실책->포수") 한 번만 셉니다.
                if (impliedError && !runnerErrorFielders.Contains(PositionCode(pa.PrimaryFielder)!)) Credit(PositionCode(pa.PrimaryFielder)!, 0, 0, 1);
                if (paHasError)
                {
                    if ((paChain.ErrorFielder ?? ErrorFielderFromText(pa.ResultText) ?? PositionCode(pa.PrimaryFielder)) is { } fielder) Credit(fielder, 0, 0, 1);
                    else { unclassifiedErrors++; Sample("미분류 실책", pa.ResultText); }
                }
                if (passedBall && map.TryGetValue("C", out var pbCatcher)) TallyFor(fieldingTeam, "C", pbCatcher).PassedBalls++;

                // 병살: 병살 문구, 삼진+도루 저지, 또는 타자 아웃과 같은 플레이의 주자 포스·태그 아웃(라인드라이브 더블 오프 등).
                var doublePlay = pa.OutsRecorded >= 2 && (pa.ResultText?.Contains("병살", StringComparison.Ordinal) == true
                    || pa.IsStrikeout && runnerCaughtStealing || pa.IsOut && runnerForcedOrTagged);
                if (doublePlay) { doublePlayCount++; foreach (var tally in creditedHere) tally.DoublePlays++; }

                credited += Math.Min(putoutsHere, pa.OutsRecorded);
                if (putoutsHere != pa.OutsRecorded)
                {
                    if (putoutsHere < pa.OutsRecorded) unclassifiedOuts += pa.OutsRecorded - putoutsHere;
                    else overCredited += putoutsHere - pa.OutsRecorded;
                    Sample(putoutsHere < pa.OutsRecorded ? $"미분류 아웃 {pa.OutsRecorded}중 {putoutsHere}" : $"중복 배정 {pa.OutsRecorded}중 {putoutsHere}",
                        pa.ResultText, (runnersByPa.GetValueOrDefault(pa.PlateAppearanceId) ?? []).Select(r => r.RawText ?? ""));
                }

                foreach (var (position, pcode) in map)
                    TallyFor(fieldingTeam, position, pcode).Outs += pa.OutsRecorded;

                var runnerTexts = (runnersByPa.GetValueOrDefault(pa.PlateAppearanceId) ?? []).Select(r => r.RawText ?? "").ToList();
                if (pa.ResultText?.Contains("실책", StringComparison.Ordinal) == true || runnerTexts.Any(t => t.Contains("실책", StringComparison.Ordinal)))
                {
                    if (!errorTexts.TryGetValue(fieldingTeam, out var list)) errorTexts[fieldingTeam] = list = new List<string>();
                    list.Add(pa.ResultText + string.Concat(runnerTexts.Where(t => t.Contains("실책", StringComparison.Ordinal)).Select(t => " / " + t)));
                }
            }

            // 박스스코어의 팀 실책 수와 경기 단위로 대조해 과잉·누락 배정이 난 경기를 찾습니다.
            foreach (var (team, official) in new[] { (game.HomeTeamCode, game.HomeErrors), (game.AwayTeamCode, game.AwayErrors) })
            {
                if (string.IsNullOrEmpty(team) || official is null) continue;
                errorCheckedTeamGames++;
                var computed = teamErrors.GetValueOrDefault(team);
                if (computed == official.Value) continue;
                errorMismatchTeamGames++;
                if (computed > official.Value) errorsOverOfficial += computed - official.Value; else errorsUnderOfficial += official.Value - computed;
                Sample($"실책 {(computed > official.Value ? "과잉" : "누락")} {game.GameId} {team} 계산 {computed} 공식 {official.Value}", null, errorTexts.GetValueOrDefault(team));
            }
        }

        var rows = totals.Values.Select(t =>
        {
            var chances = t.Putouts + t.Assists + t.Errors;
            var attempts = t.StolenBasesAllowed + t.CaughtStealing;
            var catcher = t.Position == "C";
            return new BatterFieldingRecordRow
            {
                Pcode = grouping == AnalyticsGrouping.Team ? null : t.Pcode,
                Name = grouping == AnalyticsGrouping.Team ? t.Team : names.GetValueOrDefault(t.Pcode, t.Pcode),
                TeamCode = t.Team, Position = t.Position,
                Games = t.Games, GamesStarted = t.GamesStarted, FieldingInnings = t.Outs / 3.0,
                Putouts = t.Putouts, Assists = t.Assists, Errors = t.Errors, DoublePlays = t.DoublePlays, Pickoffs = t.Pickoffs, TotalChances = chances,
                FieldingPercentage = chances > 0 ? (double)(t.Putouts + t.Assists) / chances : null,
                RangeFactorPerNine = t.Outs > 0 ? 27.0 * (t.Putouts + t.Assists) / t.Outs : null,
                StolenBasesAllowed = catcher ? t.StolenBasesAllowed : null,
                CaughtStealing = catcher ? t.CaughtStealing : null,
                CaughtStealingRate = catcher && attempts > 0 ? (double)t.CaughtStealing / attempts : null,
                PassedBalls = catcher ? t.PassedBalls : null,
            };
        }).OrderByDescending(r => r.FieldingInnings).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        return new(rows, new(gameCount, totalOuts, credited, unclassifiedOuts, overCredited, unclassifiedErrors, unmapped, samples,
            errorCheckedTeamGames, errorMismatchTeamGames, errorsOverOfficial, errorsUnderOfficial, doublePlayCount));
    }

    private static string ErrorKey(string? text, string fielder)
    {
        var m = text is null ? null : Parenthetical.Match(text);
        return m is { Success: true } ? m.Value : fielder;
    }

    private static bool IsProperPrefix(IReadOnlyList<string> shorter, IReadOnlyList<string> longer)
    {
        if (shorter.Count == 0 || shorter.Count >= longer.Count) return false;
        for (var i = 0; i < shorter.Count; i++) if (shorter[i] != longer[i]) return false;
        return true;
    }

    private sealed class Tally(string pcode, string team, string position)
    {
        public string Pcode { get; } = pcode;
        public string Team { get; } = team;
        public string Position { get; } = position;
        public int Games, GamesStarted, Outs, Putouts, Assists, Errors, DoublePlays, Pickoffs, StolenBasesAllowed, CaughtStealing, PassedBalls;
    }
}
