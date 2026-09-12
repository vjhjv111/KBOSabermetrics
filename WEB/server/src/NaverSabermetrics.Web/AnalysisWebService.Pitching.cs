namespace NaverSabermetrics.Web;

public sealed partial class AnalysisWebService
{
    private static string PaScope(AnalysisRequest r) => $"""
        {GamesFilter} AND pa.IsOfficial=1 AND pa.Status=0
        AND {(r.Role == "pitcher" ? "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)" : "pa.BatterPcode")}=$code
        AND ($team='' OR {(r.Role == "pitcher" ? "pa.FieldingTeamCode" : "pa.BattingTeamCode")}=$team)
        """;

    private static string PaFiltered => $"""
        (($type='' AND $stance='' AND $count='') OR EXISTS(
          SELECT 1 FROM source p WHERE p.PlateAppearanceId=pa.PlateAppearanceId AND p.Terminal=1 AND {PitchFilter}))
        """;

    private async Task<AnalysisResult> TrendAsync(AnalysisRequest r, CancellationToken ct)
    {
        if (r.Code == "") return Selection(r, "경기별 롤링 트렌드");
        var prefix = OutcomeSource(r) + $"""
            , paBase AS (SELECT pa.*,g.GameDate Date FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId WHERE {PaScope(r)}),
            gameBase AS (SELECT DISTINCT GameId,Date FROM source UNION SELECT DISTINCT GameId,Date FROM paBase),
            pitchStats AS (SELECT GameId,COUNT(*) Pitches,SUM(IsSwing) Swings,SUM(IsWhiff) Whiffs
              FROM source p WHERE {PitchFilter} GROUP BY GameId),
            paStats AS (SELECT pa.GameId,COUNT(*) PA,SUM(IsStrikeout) K,SUM(IsWalk=1 OR IsIntentionalWalk=1) BB
              FROM paBase pa WHERE {PaFiltered} GROUP BY pa.GameId),
            stats AS (SELECT b.GameId,b.Date,COALESCE(p.Pitches,0) Pitches,COALESCE(p.Swings,0) Swings,
              COALESCE(p.Whiffs,0) Whiffs,COALESCE(a.PA,0) PA,COALESCE(a.K,0) K,COALESCE(a.BB,0) BB
              FROM gameBase b LEFT JOIN pitchStats p ON p.GameId=b.GameId LEFT JOIN paStats a ON a.GameId=b.GameId),
            rolling AS (SELECT *,COUNT(*) OVER w WindowGames,SUM(Pitches) OVER w RollingPitches,
              100.0*SUM(Whiffs) OVER w/NULLIF(SUM(Swings) OVER w,0) RollingWhiffPct,
              100.0*SUM(K) OVER w/NULLIF(SUM(PA) OVER w,0) RollingKPct,
              100.0*SUM(BB) OVER w/NULLIF(SUM(PA) OVER w,0) RollingBBPct
              FROM stats WINDOW w AS(ORDER BY Date,GameId ROWS BETWEEN ($window-1) PRECEDING AND CURRENT ROW))
            """;
        var rows = await SqlAsync(prefix + """
            SELECT * FROM (SELECT *,100.0*Whiffs/NULLIF(Swings,0) WhiffPct,100.0*K/NULLIF(PA,0) KPct,
              100.0*BB/NULLIF(PA,0) BBPct FROM rolling ORDER BY Date DESC,GameId DESC LIMIT 500) ORDER BY Date,GameId
            """, r, ct);
        var velocity = await SqlAsync(prefix + $"""
            , types AS(SELECT DISTINCT COALESCE(NULLIF(PitchType,''),'미상') Type FROM source p WHERE {PitchFilter}),
            speeds AS(SELECT GameId,COALESCE(NULLIF(PitchType,''),'미상') Type,COUNT(*) Pitches,
              SUM(CASE WHEN SpeedKmh BETWEEN 40 AND 180 THEN 1 ELSE 0 END) SpeedPitches,
              SUM(CASE WHEN SpeedKmh BETWEEN 40 AND 180 THEN SpeedKmh ELSE 0 END) SpeedSum
              FROM source p WHERE {PitchFilter} GROUP BY GameId,Type),
            velocityBase AS(SELECT b.Date,b.GameId,t.Type,COALESCE(s.Pitches,0) Pitches,
              COALESCE(s.SpeedPitches,0) SpeedPitches,COALESCE(s.SpeedSum,0) SpeedSum
              FROM gameBase b CROSS JOIN types t LEFT JOIN speeds s ON s.GameId=b.GameId AND s.Type=t.Type),
            velocityRolling AS(SELECT *,1.0*SpeedSum/NULLIF(SpeedPitches,0) Velocity,
              1.0*SUM(SpeedSum) OVER w/NULLIF(SUM(SpeedPitches) OVER w,0) RollingVelocity
              FROM velocityBase WINDOW w AS(PARTITION BY Type ORDER BY Date,GameId ROWS BETWEEN ($window-1) PRECEDING AND CURRENT ROW))
            SELECT Date,GameId,Type,Pitches,SpeedPitches,Velocity,RollingVelocity FROM
              (SELECT * FROM velocityRolling WHERE Pitches>0 ORDER BY Date DESC,GameId DESC,Type LIMIT 500)
            ORDER BY Date,GameId,Type
            """, r, ct);
        var totals = (await SqlAsync(prefix + "SELECT COUNT(*) Games,SUM(Pitches) Pitches,SUM(PA) PA FROM stats", r, ct))[0];
        return new(r.Section, "경기별 롤링 트렌드", [SeasonNote,
            $"날짜·경기 ID 순서의 최근 {r.Window}경기를 사용합니다. 초반에는 실제 존재하는 경기만 사용하며 비율은 분자·분모를 합산합니다. 필터 구종이 없던 경기도 경기 창에 포함합니다.",
            "K%·BB%의 분모는 공식 완료 PA입니다(볼넷은 고의4구 포함). 투구 조건 적용 시 실제 마지막 투구가 조건을 만족하는 PA만 포함합니다. 구속은 구종별 40~180km/h 유효 관측을 투구 수로 가중합니다.",
            "경기 표와 구종별 구속 표는 각각 최근 최대 500행입니다. 투구 자료가 없는 PA도 투구 조건이 없으면 PA 집계에 포함합니다.", SampleNote],
            Metrics(totals, ("Games", "관측 경기"), ("Pitches", "조건 내 투구"), ("PA", "공식 완료 PA")),
            [new("trend", "경기별 비율과 이동 창", [C("Date", "날짜", "text"), C("GameId", "경기", "text"), C("Pitches", "투구"), C("Swings", "스윙"), C("Whiffs", "헛스윙"), C("WhiffPct", "헛스윙%", "percent"), C("PA", "PA"), C("K", "K"), C("BB", "BB"), C("KPct", "K%", "percent"), C("BBPct", "BB%", "percent"), C("WindowGames", "창 경기"), C("RollingPitches", "창 투구"), C("RollingWhiffPct", "창 헛스윙%", "percent"), C("RollingKPct", "창 K%", "percent"), C("RollingBBPct", "창 BB%", "percent")], rows),
             new("velocity", "구종별 구속", [C("Date", "날짜", "text"), C("GameId", "경기", "text"), C("Type", "구종", "text"), C("Pitches", "투구"), C("SpeedPitches", "구속 표본"), C("Velocity", "평균 km/h", "decimal"), C("RollingVelocity", "창 평균 km/h", "decimal")], velocity)]);
    }

    private async Task<AnalysisResult> WorkloadAsync(AnalysisRequest r, CancellationToken ct)
    {
        var official = await HasTableAsync("PitchingGameLines", ct);
        var source = official
            ? "SELECT l.GameId,g.GameDate Date,l.Pcode Code,MAX(l.Name) Name,l.TeamCode Team,CASE WHEN MIN(l.AppearanceSequence)=1 THEN '선발' WHEN MIN(l.AppearanceSequence)>1 THEN '구원' ELSE '미상' END Role,CASE WHEN COUNT(l.PitchCount)=COUNT(*) THEN SUM(l.PitchCount) END Pitches FROM PitchingGameLines l JOIN Games g ON g.GameId=l.GameId WHERE g.SeasonYear=$year AND LOWER(TRIM(g.RoundCode))='kbo_r' AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE') AND ($end='' OR g.GameDate<=$end) AND ($code='' OR l.Pcode=$code) AND ($team='' OR l.TeamCode=$team) GROUP BY l.GameId,l.Pcode,l.TeamCode"
            : "SELECT p.GameId,g.GameDate Date,p.PitcherPcode Code,MAX(p.PitcherName) Name,CASE p.BattingSide WHEN 0 THEN g.HomeTeamCode WHEN 1 THEN g.AwayTeamCode END Team,'미상' Role,COUNT(*) Pitches FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE g.SeasonYear=$year AND LOWER(TRIM(g.RoundCode))='kbo_r' AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE') AND ($end='' OR g.GameDate<=$end) AND ($code='' OR p.PitcherPcode=$code) AND ($team='' OR CASE p.BattingSide WHEN 0 THEN g.HomeTeamCode WHEN 1 THEN g.AwayTeamCode END=$team) GROUP BY p.GameId,p.PitcherPcode,Team";
        var prefix = $"""
            WITH anchor AS(SELECT COALESCE(NULLIF($end,''),MAX(g.GameDate)) Day FROM Games g
              WHERE g.SeasonYear=$year AND LOWER(TRIM(g.RoundCode))='kbo_r'
              AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE')),
            appearances AS({source}), eligible AS(SELECT a.*,b.Day FROM appearances a CROSS JOIN anchor b WHERE a.Date<=b.Day)
            """;
        var summary = (await SqlAsync(prefix + "SELECT a.Day Anchor,COUNT(e.GameId) Apps,SUM(e.Pitches IS NULL AND e.GameId IS NOT NULL) Missing,COALESCE(SUM(e.Role='선발'),0) StarterApps7,COALESCE(SUM(e.Role='구원'),0) ReliefApps7,COALESCE(SUM(e.Role='미상'),0) UnknownRoleApps7 FROM anchor a LEFT JOIN eligible e ON e.Date>=DATE(a.Day,'-6 days') GROUP BY a.Day", r, ct))[0];
        var rows = await SqlAsync(prefix + """
            SELECT Code,MAX(Name) Name,GROUP_CONCAT(DISTINCT Team) Team,MAX(Date) LastDate,
              MAX(0,CAST(JULIANDAY(Day)-JULIANDAY(MAX(Date)) AS INTEGER)-1) RestDays,
              CASE WHEN SUM(Date>=DATE(Day,'-2 days') AND Pitches IS NULL)=0 THEN SUM(CASE WHEN Date>=DATE(Day,'-2 days') THEN Pitches ELSE 0 END) END Pitches3,
              SUM(Date>=DATE(Day,'-2 days')) Apps3,
              CASE WHEN SUM(Date>=DATE(Day,'-6 days') AND Pitches IS NULL)=0 THEN SUM(CASE WHEN Date>=DATE(Day,'-6 days') THEN Pitches ELSE 0 END) END Pitches7,
              SUM(Date>=DATE(Day,'-6 days')) Apps7,
              SUM(Date>=DATE(Day,'-6 days') AND Role='선발') StarterApps7,
              SUM(Date>=DATE(Day,'-6 days') AND Role='구원') ReliefApps7
            FROM eligible GROUP BY Code HAVING Code IS NOT NULL AND Code<>'' ORDER BY Pitches7 DESC,Pitches3 DESC,Code LIMIT 100
            """, r, ct);
        var daily = await SqlAsync(prefix + "SELECT Date,GameId,Code,Name,Team,Role,Pitches FROM eligible WHERE Date>=DATE(Day,'-6 days') ORDER BY Date DESC,GameId DESC,Code LIMIT 500", r, ct);
        return new(r.Section, "최근 투구 부하·휴식", [SeasonNote,
            "기준일은 종료일 또는 해당 시즌 DB의 마지막 경기일입니다. 3일은 기준일 포함 3개 달력일, 7일은 기준일 포함 7개 달력일입니다. 더블헤더는 별도 등판입니다.",
            "휴식일은 마지막 등판일과 기준일 사이의 온전한 날짜 수입니다(당일·전날 등판은 0일). 시작일·구종·타자 좌우·카운트 조건은 부하 계산에 적용하지 않습니다.",
            official ? "박스스코어 PitchCount를 경기·투수별 합산합니다. AppearanceSequence=1은 선발, 2 이상은 구원, 그 밖은 미상으로 구분합니다. 선발·구원 투구를 모두 부하에 포함하며 누락된 투구 수가 있는 창의 합계는 —입니다." : "박스스코어 테이블이 없어 수집된 실제 투구를 셉니다. 역할은 미상으로 표시하며 누락된 중계 투구로 인해 실제 부하보다 작을 수 있습니다.",
            "상위 100명과 최근 7일 등판 최대 500행을 표시합니다. 부하는 관측량이며 피로·부상 위험을 추정한 값이 아닙니다.", SampleNote],
            Metrics(summary, ("Anchor", "기준일"), ("Apps", "최근 7일 등판"), ("StarterApps7", "7일 선발 등판"), ("ReliefApps7", "7일 구원 등판"), ("UnknownRoleApps7", "7일 역할 미상"), ("Missing", "투구 수 누락 등판")),
            [new("workload", "투수별 최근 부하", [C("Code", "코드", "text"), C("Name", "투수", "text"), C("Team", "팀", "text"), C("LastDate", "마지막 등판", "text"), C("RestDays", "사이 휴식일"), C("Pitches3", "3일 투구"), C("Apps3", "3일 등판"), C("Pitches7", "7일 투구"), C("Apps7", "7일 등판"), C("StarterApps7", "7일 선발"), C("ReliefApps7", "7일 구원")], rows),
             new("daily", "최근 7일 경기별 부하", [C("Date", "날짜", "text"), C("GameId", "경기", "text"), C("Code", "코드", "text"), C("Name", "투수", "text"), C("Team", "팀", "text"), C("Role", "등판 역할", "text"), C("Pitches", "투구")], daily)]);
    }

    private static AnalysisColumn[] TimesColumns => [C("Group", "구간", "text"), C("Pitches", "투구"), C("Swings", "스윙"), C("Whiffs", "헛스윙"), C("WhiffPct", "헛스윙%", "percent"), C("PA", "PA"), C("K", "K"), C("BB", "BB"), C("KPct", "K%", "percent"), C("BBPct", "BB%", "percent"), C("AB", "AB"), C("TB", "TB"), C("SLG", "SLG", "rate")];
    private const string TimesRates = "100.0*Whiffs/NULLIF(Swings,0) WhiffPct,100.0*K/NULLIF(PA,0) KPct,100.0*BB/NULLIF(PA,0) BBPct,1.0*TB/NULLIF(AB,0) SLG";

    private async Task<AnalysisResult> TimesAsync(AnalysisRequest r, CancellationToken ct)
    {
        if (r.Code == "") return Selection(r, "투구 순번·동일 타자 재대결");
        var prefix = OutcomeSource(r) + $"""
            , paBase AS(SELECT pa.* FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId WHERE {PaScope(r)}),
            meetings AS(SELECT PlateAppearanceId FROM source WHERE PlateAppearanceId IS NOT NULL UNION SELECT PlateAppearanceId FROM paBase),
            encounters AS(SELECT pa.PlateAppearanceId,ROW_NUMBER() OVER(PARTITION BY pa.GameId,pa.BatterPcode ORDER BY pa.SequenceNumber,pa.PlateAppearanceId) Encounter
              FROM meetings m JOIN PlateAppearances pa ON pa.PlateAppearanceId=m.PlateAppearanceId),
            numbered AS(SELECT p.*,ROW_NUMBER() OVER(PARTITION BY p.GameId ORDER BY n.ChronologicalIndex,p.ActualPitchIndex,p.SourceOptionIndex,p.PitchEventId) PitchNumber,
              e.Encounter FROM source p JOIN NormalizedEvents n ON n.EventId=p.SourceEventId
              LEFT JOIN encounters e ON e.PlateAppearanceId=p.PlateAppearanceId),
            filtered AS(SELECT p.*,CASE WHEN PitchNumber<=25 THEN 1 WHEN PitchNumber<=50 THEN 2 WHEN PitchNumber<=75 THEN 3 WHEN PitchNumber<=100 THEN 4 ELSE 5 END Band,
              CASE WHEN Encounter>=4 THEN 4 ELSE Encounter END Meeting FROM numbered p WHERE {PitchFilter})
            """;
        var bands = await SqlAsync(prefix + $"""
            , aggregates AS(SELECT Band,COUNT(*) Pitches,SUM(IsSwing) Swings,SUM(IsWhiff) Whiffs,SUM(Terminal) PA,
              SUM(CASE WHEN Terminal=1 THEN PaK ELSE 0 END) K,SUM(CASE WHEN Terminal=1 THEN PaBB ELSE 0 END) BB,
              SUM(CASE WHEN Terminal=1 THEN PaAB ELSE 0 END) AB,SUM(CASE WHEN Terminal=1 THEN PaTB ELSE 0 END) TB FROM filtered GROUP BY Band)
            SELECT CASE Band WHEN 1 THEN '1–25' WHEN 2 THEN '26–50' WHEN 3 THEN '51–75' WHEN 4 THEN '76–100' ELSE '101+' END [Group],
              Pitches,Swings,Whiffs,PA,K,BB,AB,TB,{TimesRates} FROM aggregates ORDER BY Band
            """, r, ct);
        var meetings = await SqlAsync(prefix + $"""
            , pitchStats AS(SELECT Meeting,COUNT(*) Pitches,SUM(IsSwing) Swings,SUM(IsWhiff) Whiffs FROM filtered WHERE Meeting IS NOT NULL GROUP BY Meeting),
            paStats AS(SELECT CASE WHEN e.Encounter>=4 THEN 4 ELSE e.Encounter END Meeting,COUNT(*) PA,SUM(pa.IsStrikeout) K,
              SUM(pa.IsWalk=1 OR pa.IsIntentionalWalk=1) BB,SUM(pa.CountsAsAtBat) AB,SUM(pa.TotalBases) TB
              FROM paBase pa JOIN encounters e ON e.PlateAppearanceId=pa.PlateAppearanceId WHERE {PaFiltered} GROUP BY Meeting),
            keys AS(SELECT Meeting FROM pitchStats UNION SELECT Meeting FROM paStats),
            aggregates AS(SELECT k.Meeting,COALESCE(p.Pitches,0) Pitches,COALESCE(p.Swings,0) Swings,COALESCE(p.Whiffs,0) Whiffs,
              COALESCE(a.PA,0) PA,COALESCE(a.K,0) K,COALESCE(a.BB,0) BB,COALESCE(a.AB,0) AB,COALESCE(a.TB,0) TB FROM keys k
              LEFT JOIN pitchStats p ON p.Meeting=k.Meeting LEFT JOIN paStats a ON a.Meeting=k.Meeting)
            SELECT CASE Meeting WHEN 4 THEN '4+' ELSE CAST(Meeting AS TEXT) END [Group],Pitches,Swings,Whiffs,PA,K,BB,AB,TB,{TimesRates}
            FROM aggregates ORDER BY Meeting
            """, r, ct);
        var totals = (await SqlAsync(prefix + """
            SELECT (SELECT COUNT(*) FROM source) Pitches,(SELECT COUNT(*) FROM numbered) OrderedPitches,
              (SELECT COUNT(*) FROM paBase) PA,(SELECT COALESCE(SUM(Terminal),0) FROM source) TerminalPA
            """, r, ct))[0];
        return new(r.Section, "투구 순번·동일 타자 재대결", [SeasonNote,
            "투구 순번은 경기별 해당 투수의 모든 수집 투구를 실제 이벤트 시간순으로 센 뒤 조건을 적용합니다. 타석마다 초기화되는 ActualPitchIndex를 경기 투구 수로 사용하지 않습니다. 이벤트 순서가 없는 투구는 순번 분석에서 제외합니다.",
            "재대결은 같은 경기에서 같은 투수가 같은 타자를 만난 타석 순번입니다. 타순 한 바퀴(TTO)와 다르며 교체 타자는 별도 선수입니다. 중단 타석도 만남에는 포함하지만 공식 완료 PA 분모에는 제외합니다.",
            "투구 구간의 PA는 마지막 투구가 속한 구간에 한 번만 배정합니다. 무투구 PA는 구간에서 제외하고, 재대결 PA에는 투구 조건이 없으면 포함합니다. K%·BB%는 완료 PA, SLG는 AB, 헛스윙%는 스윙을 분모로 합니다.", SampleNote],
            Metrics(totals, ("Pitches", "선수 전체 투구"), ("OrderedPitches", "순번 확인 투구"), ("PA", "공식 완료 PA"), ("TerminalPA", "마지막 투구 연결 PA")),
            [new("pitchBands", "경기 내 투구 순번 구간", TimesColumns, bands), new("encounters", "동일 타자 재대결", TimesColumns, meetings)]);
    }
}
