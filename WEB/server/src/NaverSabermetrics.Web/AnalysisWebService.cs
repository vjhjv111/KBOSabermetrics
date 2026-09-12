using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record AnalysisRequest(string Section = "zones", int Year = 2026, string Code = "",
    string Role = "batter", string Team = "", string Start = "", string End = "", string PitchType = "",
    string Stance = "", string Count = "", string GameId = "", string PaId = "", int Page = 1, int Window = 5)
{
    public void Validate()
    {
        static bool Id(string? s, int max) => s is not null && s.Length <= max && s.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-');
        static bool Date(string? s) => s is not null && (s == "" || DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        if (Section is not ("catalog" or "zones" or "sequences" or "trend" or "workload" or "times" or "expectancy" or "replay" or "provenance")
            || Year is < 1900 or > 2200 || Role is not ("batter" or "pitcher") || !Id(Code, 40) || !Id(Team, 10)
            || !Id(GameId, 80) || !Id(PaId, 160) || !Date(Start) || !Date(End)
            || (Start != "" && End != "" && string.CompareOrdinal(Start, End) > 0)
            || PitchType is null || PitchType.Length > 40 || PitchType.Any(char.IsControl)
            || Stance is not ("" or "L" or "R") || Count is null
            || (Count != "" && !(Count.Length == 3 && Count[0] is >= '0' and <= '3' && Count[1] == '-' && Count[2] is >= '0' and <= '2'))
            || Page is < 1 or > 100 || Window is < 1 or > 30)
            throw new RequestError("분석 조회 조건이 잘못되었습니다.");
    }
}

public sealed record AnalysisResult(string Section, string Title, string[] Notes, AnalysisMetric[] Summary,
    AnalysisTable[] Tables, object? Chart = null, bool HasMore = false, int Page = 1);
public sealed record AnalysisMetric(string Label, object? Value);
public sealed record AnalysisTable(string Key, string Title, AnalysisColumn[] Columns, List<Dictionary<string, object?>> Rows);
public sealed record AnalysisColumn(string Key, string Label, string Format = "text");

/// <summary>Bounded, parameterized queries against the read-only season warehouse.</summary>
public sealed partial class AnalysisWebService(DatabaseCacheService db, SiteOptions options)
{
    private const string SeasonNote = "선택 연도 정규시즌(kbo_r)만 포함하며 올스타전·시범경기·포스트시즌은 제외합니다.";
    private const string SampleNote = "0은 관측된 0이며, 분모가 없거나 자료가 누락된 값은 —로 표시합니다. 작은 표본의 비율은 변동이 큽니다.";
    private const string GamesFilter = "g.SeasonYear=$year AND LOWER(TRIM(g.RoundCode))='kbo_r' AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE') AND ($start='' OR g.GameDate >= $start) AND ($end='' OR g.GameDate <= $end)";
    private const string PitchFilter = "($type='' OR COALESCE(NULLIF(p.PitchType,''),'미상')=$type) AND ($stance='' OR p.BatterStance=$stance) AND ($count='' OR CAST(p.BallsBefore AS TEXT)||'-'||CAST(p.StrikesBefore AS TEXT)=$count)";
    private static string PlayerFilter(AnalysisRequest r) => r.Role == "pitcher" ? "p.PitcherPcode=$code" : "p.BatterPcode=$code";
    private static string TeamExpression(AnalysisRequest r) => r.Role == "pitcher"
        ? "CASE p.BattingSide WHEN 0 THEN g.HomeTeamCode WHEN 1 THEN g.AwayTeamCode END"
        : "CASE p.BattingSide WHEN 0 THEN g.AwayTeamCode WHEN 1 THEN g.HomeTeamCode END";
    private static string PitchScope(AnalysisRequest r) => $"{GamesFilter} AND {PlayerFilter(r)} AND ($team='' OR {TeamExpression(r)}=$team)";

    private async Task<List<Dictionary<string, object?>>> SqlAsync(string sql, AnalysisRequest r, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = db.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.QuerySeconds;
        foreach (var (name, value) in new (string, object)[] { ("$year", r.Year), ("$code", r.Code), ("$role", r.Role),
            ("$team", r.Team), ("$start", r.Start), ("$end", r.End), ("$type", r.PitchType), ("$stance", r.Stance),
            ("$count", r.Count), ("$game", r.GameId), ("$pa", r.PaId), ("$offset", (r.Page-1)*50), ("$window", r.Window) })
            command.Parameters.AddWithValue(name, value);
        using var registration = ct.Register(command.Cancel);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (value is double number && !double.IsFinite(number)) value = null;
                row[reader.GetName(i)] = value;
            }
            rows.Add(row);
            // 501 is the pagination sentinel; no query may materialize an unbounded response.
            if (rows.Count > 501) throw new InvalidOperationException("분석 조회의 행 제한을 초과했습니다.");
        }
        return rows;
    }

    private async Task<bool> HasTableAsync(string table, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = db.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name)";
        command.Parameters.AddWithValue("$name", table);
        command.CommandTimeout = options.QuerySeconds;
        using var registration = ct.Register(command.Cancel);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<AnalysisResult> QueryAsync(AnalysisRequest r, CancellationToken ct)
    {
        r.Validate();
        return r.Section switch
        {
            "catalog" => await CatalogAsync(r, ct), "zones" => await ZonesAsync(r, ct),
            "sequences" => await SequencesAsync(r, ct), "trend" => await TrendAsync(r, ct),
            "workload" => await WorkloadAsync(r with { Role = "pitcher" }, ct),
            "times" => await TimesAsync(r with { Role = "pitcher" }, ct),
            "expectancy" => await ExpectancyAsync(r, ct), "replay" => await ReplayAsync(r, ct),
            "provenance" => await ProvenanceAsync(r, ct), _ => throw new RequestError("알 수 없는 분석입니다.")
        };
    }

    private static AnalysisResult Selection(AnalysisRequest r, string title) => new(r.Section, title,
        [SeasonNote, "선수를 선택하면 해당 선수의 실제 기록으로 분석합니다."], [], []);
    private static AnalysisColumn C(string key, string label, string format = "integer") => new(key, label, format);
    private static AnalysisMetric[] Metrics(Dictionary<string, object?> row, params (string Key, string Label)[] fields)
        => fields.Select(f => new AnalysisMetric(f.Label, row.GetValueOrDefault(f.Key))).ToArray();

    private async Task<AnalysisResult> CatalogAsync(AnalysisRequest r, CancellationToken ct)
    {
        var code = r.Role == "pitcher" ? "p.PitcherPcode" : "p.BatterPcode";
        var name = r.Role == "pitcher" ? "p.PitcherName" : "p.BatterName";
        var players = await SqlAsync($"""
            SELECT {code} Code, MAX({name}) Name, GROUP_CONCAT(DISTINCT {TeamExpression(r)}) Team, $role Role
            FROM Pitches p JOIN Games g ON g.GameId=p.GameId
            WHERE {GamesFilter} AND {code} IS NOT NULL AND {code}<>'' AND ($team='' OR {TeamExpression(r)}=$team)
            GROUP BY {code} ORDER BY Name,Code LIMIT 501 OFFSET ($offset*10)
            """, r, ct);
        var types = await SqlAsync($"""
            SELECT COALESCE(NULLIF(p.PitchType,''),'미상') Type, COUNT(*) Pitches FROM Pitches p JOIN Games g ON g.GameId=p.GameId
            WHERE {GamesFilter} AND ($code='' OR {PlayerFilter(r)}) AND ($team='' OR {TeamExpression(r)}=$team)
            GROUP BY p.PitchType ORDER BY Pitches DESC,Type LIMIT 100
            """, r, ct);
        var coverage = (await SqlAsync($"""
            SELECT COUNT(*) Pitches, COALESCE(SUM(p.HasPtsTracking=1),0) Tracked,
              COALESCE(SUM(p.CrossPlateX IS NOT NULL AND p.CalculatedCrossPlateZ IS NOT NULL AND p.TopStrikeZone>p.BottomStrikeZone),0) Coordinates
            FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE {GamesFilter}
              AND ($code='' OR {PlayerFilter(r)}) AND ($team='' OR {TeamExpression(r)}=$team)
            """, r, ct))[0];
        return new(r.Section, "분석 선수·구종", [SeasonNote, "선수 명단은 해당 조건에서 실제 투구에 등장한 선수이며, 팀은 해당 기록 당시 소속입니다."],
            Metrics(coverage, ("Pitches", "투구 수"), ("Tracked", "트래킹 있음"), ("Coordinates", "좌표·존 높이 있음")),
            [new("players", "선수", [C("Code", "코드", "text"), C("Name", "선수", "text"), C("Team", "팀", "text"), C("Role", "역할", "text")], players.Take(500).ToList()),
             new("pitchTypes", "구종", [C("Type", "구종", "text"), C("Pitches", "투구")], types)], HasMore: players.Count > 500, Page: r.Page);
    }

    // Terminal outcomes are matched before pitch filters, so a PA is never attributed to its last *filtered* pitch.
    private static string OutcomeSource(AnalysisRequest r) => $"""
        WITH source AS (
          SELECT p.*, g.GameDate Date, pa.SequenceNumber PaSequence,
            CASE WHEN pa.IsOfficial=1 AND pa.Status=0 AND NOT EXISTS (
              SELECT 1 FROM Pitches last WHERE last.PlateAppearanceId=p.PlateAppearanceId AND
                (last.ActualPitchIndex>p.ActualPitchIndex OR
                 (last.ActualPitchIndex=p.ActualPitchIndex AND last.SourceOptionIndex>p.SourceOptionIndex) OR
                 (last.ActualPitchIndex=p.ActualPitchIndex AND last.SourceOptionIndex=p.SourceOptionIndex AND last.PitchEventId>p.PitchEventId)))
              THEN 1 ELSE 0 END Terminal,
            pa.CountsAsAtBat PaAB, pa.TotalBases PaTB, pa.IsStrikeout PaK,
            CASE WHEN pa.IsWalk=1 OR pa.IsIntentionalWalk=1 THEN 1 ELSE 0 END PaBB
          FROM Pitches p JOIN Games g ON g.GameId=p.GameId
          LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId
          WHERE {PitchScope(r)}
        )
        """;

    private async Task<AnalysisResult> ZonesAsync(AnalysisRequest r, CancellationToken ct)
    {
        if (r.Code == "") return Selection(r, "존·구종 매트릭스");
        const string valid = "p.CrossPlateX BETWEEN -10 AND 10 AND p.CalculatedCrossPlateZ BETWEEN -5 AND 15 AND p.BottomStrikeZone BETWEEN 0 AND 6 AND p.TopStrikeZone>p.BottomStrikeZone AND p.TopStrikeZone<=8";
        var prefix = OutcomeSource(r) + $"""
            , filtered AS (SELECT p.*, CASE WHEN {valid} THEN 1 ELSE 0 END Valid,
                 (p.CalculatedCrossPlateZ-p.BottomStrikeZone)/NULLIF(p.TopStrikeZone-p.BottomStrikeZone,0) Z
              FROM source p WHERE {PitchFilter})
            """;
        var summary = (await SqlAsync(prefix + "SELECT COUNT(*) Pitches, COALESCE(SUM(Valid),0) Located, COUNT(*)-COALESCE(SUM(Valid),0) Excluded, COALESCE(SUM(Terminal),0) PA FROM filtered", r, ct))[0];
        var rows = await SqlAsync(prefix + """
            , binned AS (SELECT *, CASE WHEN CrossPlateX < -0.7083333333 THEN 0 WHEN CrossPlateX < -0.2361111111 THEN 1 WHEN CrossPlateX < 0.2361111111 THEN 2 WHEN CrossPlateX <= 0.7083333333 THEN 3 ELSE 4 END XBin,
                CASE WHEN Z<0 THEN 0 WHEN Z<1.0/3 THEN 1 WHEN Z<2.0/3 THEN 2 WHEN Z<=1 THEN 3 ELSE 4 END ZBin FROM filtered WHERE Valid=1),
              bins(n) AS (VALUES(0),(1),(2),(3),(4)),
              stats AS (SELECT XBin,ZBin,COUNT(*) Pitches,SUM(IsSwing) Swings,SUM(IsWhiff) Whiffs,SUM(IsCalledStrike) CalledStrikes,
                SUM(Terminal) PA,SUM(CASE WHEN Terminal=1 THEN PaAB ELSE 0 END) AB,SUM(CASE WHEN Terminal=1 THEN PaTB ELSE 0 END) TB
                FROM binned GROUP BY XBin,ZBin)
            SELECT x.n XBin,z.n ZBin,COALESCE(s.Pitches,0) Pitches,COALESCE(s.Swings,0) Swings,COALESCE(s.Whiffs,0) Whiffs,
              COALESCE(s.CalledStrikes,0) CalledStrikes,100.0*s.Swings/NULLIF(s.Pitches,0) SwingPct,
              100.0*s.Whiffs/NULLIF(s.Swings,0) WhiffPct,COALESCE(s.PA,0) PA,COALESCE(s.AB,0) AB,COALESCE(s.TB,0) TB,
              1.0*s.TB/NULLIF(s.AB,0) SLG
            FROM bins x CROSS JOIN bins z LEFT JOIN stats s ON s.XBin=x.n AND s.ZBin=z.n ORDER BY z.n DESC,x.n
            """, r, ct);
        return new(r.Section, "존·구종 매트릭스", [SeasonNote, "포수 시점 원본 X(ft), 홈플레이트 좌우 ±0.7083ft와 타자별 하단=0·상단=1 정규화 높이로 나눕니다. 중앙 3×3이 스트라이크존이며 바깥 16칸은 존 밖을 포함합니다.",
            "좌표 또는 존 높이가 없거나 물리 범위를 벗어난 투구는 제외합니다(X ±10ft, Z −5~15ft, 하단 0~6ft, 상단≤8ft). 장타율은 실제 마지막 투구로 종료된 공식 완료 타석의 TB/AB입니다. 헛스윙률=헛스윙/스윙, 스윙률=스윙/투구.", SampleNote],
            Metrics(summary, ("Pitches", "조건 내 투구"), ("Located", "유효 좌표"), ("Excluded", "좌표 제외"), ("PA", "종료 타석")),
            [new("zones", "5×5 구역", [C("XBin", "가로 구역"), C("ZBin", "높이 구역"), C("Pitches", "투구"), C("Swings", "스윙"), C("Whiffs", "헛스윙"), C("CalledStrikes", "루킹 스트라이크"), C("SwingPct", "스윙%", "percent"), C("WhiffPct", "헛스윙%", "percent"), C("PA", "종료 PA"), C("AB", "AB"), C("TB", "TB"), C("SLG", "SLG", "rate")], rows)]);
    }

    private async Task<AnalysisResult> SequencesAsync(AnalysisRequest r, CancellationToken ct)
    {
        if (r.Code == "") return Selection(r, "구종 시퀀스");
        // LAG runs on every pitch of each PA first; current-pitch filters must not invent adjacency.
        var sql = $"""
            WITH selected AS (SELECT DISTINCT p.PlateAppearanceId FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE {PitchScope(r)} AND p.PlateAppearanceId IS NOT NULL),
            ordered AS (SELECT p.*, LAG(PitchType) OVER w Previous, LAG(SpeedKmh) OVER w PreviousSpeed,
              LAG(PitcherPcode) OVER w PreviousPitcher, LAG(BatterPcode) OVER w PreviousBatter,
              LAG(ActualPitchIndex) OVER w PreviousIndex
              FROM Pitches p JOIN selected s ON s.PlateAppearanceId=p.PlateAppearanceId
              WINDOW w AS (PARTITION BY p.PlateAppearanceId ORDER BY ActualPitchIndex,SourceOptionIndex,PitchEventId)),
            filtered AS (SELECT p.* FROM ordered p JOIN Games g ON g.GameId=p.GameId
              WHERE {PitchScope(r)} AND {PitchFilter} AND p.PreviousIndex IS NOT NULL
                AND p.PreviousPitcher=p.PitcherPcode AND p.PreviousBatter=p.BatterPcode
                AND p.ActualPitchIndex=p.PreviousIndex+1)
            SELECT COALESCE(NULLIF(Previous,''),'미상') Previous,COALESCE(NULLIF(PitchType,''),'미상') Current,
              COUNT(*) Pairs,SUM(IsSwing) Swings,SUM(IsWhiff) Whiffs,100.0*SUM(IsWhiff)/NULLIF(SUM(IsSwing),0) WhiffPct,
              SUM(CASE WHEN SpeedKmh BETWEEN 40 AND 180 AND PreviousSpeed BETWEEN 40 AND 180 THEN 1 ELSE 0 END) SpeedPairs,
              AVG(CASE WHEN SpeedKmh BETWEEN 40 AND 180 AND PreviousSpeed BETWEEN 40 AND 180 THEN SpeedKmh-PreviousSpeed END) SpeedGap
            FROM filtered GROUP BY Previous,PitchType ORDER BY Pairs DESC,Previous,Current LIMIT 500
            """;
        var rows = await SqlAsync(sql, r, ct);
        return new(r.Section, "구종 시퀀스", [SeasonNote, "동일 타석에서 실제 연속한 두 투구만 연결합니다. 투수·타자 교체, 투구 순번 누락은 연결하지 않습니다. 구종·좌우·카운트 조건은 두 번째 투구에 적용합니다.",
            "구속 차이는 현재−직전(km/h)이며 양쪽 구속이 40~180km/h인 쌍만 평균냅니다. 헛스윙률은 두 번째 투구의 헛스윙/스윙입니다. 구종 조합별 표본이므로 이를 섞은 평균 구속은 제시하지 않습니다.", SampleNote],
            [new("연속 투구 쌍", rows.Sum(x => Convert.ToInt64(x["Pairs"])))],
            [new("sequences", "직전 → 현재 구종", [C("Previous", "직전", "text"), C("Current", "현재", "text"), C("Pairs", "연속 쌍"), C("Swings", "스윙"), C("Whiffs", "헛스윙"), C("WhiffPct", "헛스윙%", "percent"), C("SpeedPairs", "구속 쌍"), C("SpeedGap", "구속 차이", "decimal")], rows)]);
    }
}
