using System.Globalization;
using System.Text.Json;
using NaverRelay.Parsing;

namespace NaverSabermetrics.Web;

public sealed partial class AnalysisWebService
{
    // A relay group is the finest stored boundary with score/out snapshots. Runner events
    // inside it cannot be assigned an individual RE24 without inventing intermediate states.
    private const string ExpectancyCte = """
        WITH eligible_games AS MATERIALIZED (
          SELECT GameId FROM Games WHERE SeasonYear=$year AND LOWER(TRIM(RoundCode))='kbo_r'
            AND UPPER(HomeTeamCode) NOT IN ('EA','WE') AND UPPER(AwayTeamCode) NOT IN ('EA','WE')
            AND ($start='' OR GameDate>=$start) AND ($end='' OR GameDate<=$end)
            AND ($game='' OR GameId=$game)
            AND ($team='' OR HomeTeamCode=$team OR AwayTeamCode=$team)
        ), runners AS MATERIALIZED (
          SELECT re.RelayGroupId, COUNT(*) RunnerEvents,
            SUM(CASE WHEN re.Reason IN (2,3) OR re.EventType=5 THEN 1 ELSE 0 END) StealAttempts,
            SUM(CASE WHEN re.Reason=2 AND re.IsOut=0 AND re.WasParsed=1 THEN 1 ELSE 0 END) Steals,
            SUM(re.IsRun) RunnerRuns,
            MAX(CASE WHEN $code='' OR re.RunnerPcode=$code THEN 1 ELSE 0 END) PlayerRunner
          FROM RunnerEvents re JOIN eligible_games eg ON eg.GameId=re.GameId GROUP BY re.RelayGroupId
        ), groups AS MATERIALIZED (
          SELECT rg.*, pa.BatterPcode, COALESCE(pa.FinalPitcherPcode,pa.PitcherPcode) Pitcher,
            CASE WHEN pa.ResultType IN (3,16,18) OR pa.BattedBallType=5 THEN 1 ELSE 0 END Bunt,
            COALESCE(re.RunnerEvents,0) RunnerEvents, COALESCE(re.StealAttempts,0) StealAttempts,
            COALESCE(re.Steals,0) Steals,COALESCE(re.RunnerRuns,0) RunnerRuns,COALESCE(re.PlayerRunner,0) PlayerRunner,
            CASE WHEN rg.BattingSide=0 THEN rg.BeforeAwayScore ELSE rg.BeforeHomeScore END BeforeScore,
            CASE WHEN rg.BattingSide=0 THEN rg.AfterAwayScore ELSE rg.AfterHomeScore END AfterScore,
            (CASE WHEN COALESCE(NULLIF(rg.BeforeFirstRunnerPcode,''),NULLIF(rg.BeforeFirstRunnerName,'')) IS NOT NULL THEN 1 ELSE 0 END
             + CASE WHEN COALESCE(NULLIF(rg.BeforeSecondRunnerPcode,''),NULLIF(rg.BeforeSecondRunnerName,'')) IS NOT NULL THEN 2 ELSE 0 END
             + CASE WHEN COALESCE(NULLIF(rg.BeforeThirdRunnerPcode,''),NULLIF(rg.BeforeThirdRunnerName,'')) IS NOT NULL THEN 4 ELSE 0 END) BaseMask,
            CASE WHEN rg.PlateAppearanceId IS NOT NULL OR COALESCE(re.RunnerEvents,0)>0
              OR rg.AfterOuts<>rg.BeforeOuts OR rg.AfterHomeScore<>rg.BeforeHomeScore OR rg.AfterAwayScore<>rg.BeforeAwayScore
              THEN 1 ELSE 0 END IsObservation
          FROM RelayGroups rg JOIN eligible_games eg ON eg.GameId=rg.GameId
          LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=rg.PlateAppearanceId
          LEFT JOIN runners re ON re.RelayGroupId=rg.RelayGroupId
          WHERE rg.Inning>0 AND rg.BattingSide IN (0,1) AND rg.GroupType NOT IN (1,7)
        ), ordered AS MATERIALIZED (
          SELECT *, ROW_NUMBER() OVER w Ordinal, ROW_NUMBER() OVER (PARTITION BY GameId,Inning,BattingSide ORDER BY ChronologicalIndex DESC,RelayGroupId DESC) ReverseOrdinal,
            LEAD(BeforeOuts) OVER w NextOuts, LEAD(BeforeScore) OVER w NextScore,
            LEAD(BaseMask) OVER w NextMask,LEAD(BeforeHomeScore) OVER w NextHome,LEAD(BeforeAwayScore) OVER w NextAway,
            LEAD(RelayGroupId) OVER w NextId
          FROM groups WINDOW w AS (PARTITION BY GameId,Inning,BattingSide ORDER BY ChronologicalIndex,RelayGroupId)
        ), halves AS MATERIALIZED (
          SELECT GameId,Inning,BattingSide,
            MAX(CASE WHEN ReverseOrdinal=1 THEN AfterScore END) EndScore,
            CASE WHEN MAX(CASE WHEN Ordinal=1 THEN BeforeOuts END)=0
               AND MAX(CASE WHEN Ordinal=1 THEN BaseMask END)=0
               AND MAX(CASE WHEN ReverseOrdinal=1 THEN AfterOuts END)=3
               AND SUM(CASE WHEN BeforeOuts IS NULL OR AfterOuts IS NULL OR BeforeScore IS NULL OR AfterScore IS NULL
                     OR BeforeHomeScore IS NULL OR BeforeAwayScore IS NULL OR AfterHomeScore IS NULL OR AfterAwayScore IS NULL
                     OR BeforeOuts NOT BETWEEN 0 AND 3 OR AfterOuts NOT BETWEEN BeforeOuts AND 3 OR AfterScore<BeforeScore
                     OR (NextId IS NOT NULL AND (NextOuts<>AfterOuts OR NextScore<>AfterScore OR NextHome<>AfterHomeScore OR NextAway<>AfterAwayScore))
                     THEN 1 ELSE 0 END)=0 THEN 1 ELSE 0 END Complete
          FROM ordered GROUP BY GameId,Inning,BattingSide
        ), observations AS MATERIALIZED (
          SELECT o.*,h.EndScore-o.BeforeScore RemainingRuns
          -- Preserve this join order: SQLite otherwise scans all 11k halves for every group.
          FROM halves h CROSS JOIN ordered o USING(GameId,Inning,BattingSide)
          WHERE h.Complete=1 AND o.IsObservation=1 AND o.BeforeOuts BETWEEN 0 AND 2 AND h.EndScore>=o.BeforeScore
        ), expectancy AS MATERIALIZED (
          SELECT BeforeOuts Outs, BaseMask,COUNT(*) N, AVG(1.0*RemainingRuns) RE
          FROM observations GROUP BY BeforeOuts,BaseMask
        ), actions AS (
          SELECT o.*,
            CASE WHEN Bunt=1 THEN '번트 포함 그룹' WHEN StealAttempts>0 THEN '도루 포함 그룹' ELSE '진루 포함 그룹' END Action,
            CASE WHEN AfterOuts=3 THEN 0.0 WHEN NextOuts=AfterOuts AND NextScore=AfterScore THEN after_re.RE END AfterRE,
            before_re.RE BeforeRE
          FROM observations o JOIN expectancy before_re ON before_re.Outs=o.BeforeOuts AND before_re.BaseMask=o.BaseMask
          LEFT JOIN expectancy after_re ON after_re.Outs=o.NextOuts AND after_re.BaseMask=o.NextMask
          WHERE (Bunt=1 OR RunnerEvents>0)
            AND ($code='' OR ($role='pitcher' AND o.Pitcher=$code)
                 OR ($role<>'pitcher' AND (o.BatterPcode=$code OR o.PlayerRunner=1)))
            AND ($team='' OR ($role='pitcher' AND o.BattingTeamCode<>$team) OR ($role<>'pitcher' AND o.BattingTeamCode=$team))
        )
        """;

    private async Task<AnalysisResult> ExpectancyAsync(AnalysisRequest r, CancellationToken ct)
    {
        if (!await HasTableAsync("RelayGroups", ct) || !await HasTableAsync("RunnerEvents", ct))
            return ContextUnavailable("expectancy", "득점 기대값 RE24", "이 DB에는 주자·중계 상태 테이블이 없어 RE24를 계산할 수 없습니다.");
        var rows = await SqlAsync(ExpectancyCte + """
            SELECT 'state' Kind,Outs,BaseMask,N,RE,NULL Action,NULL ObservedRuns,NULL DeltaRE,NULL Matched,
              NULL StealAttempts,NULL Steals,NULL RunnerRuns FROM expectancy
            UNION ALL
            SELECT 'action',BeforeOuts,BaseMask,COUNT(*),AVG(RemainingRuns),Action,AVG(1.0*(AfterScore-BeforeScore)),
              AVG(AfterScore-BeforeScore+AfterRE-BeforeRE),COUNT(AfterRE),SUM(StealAttempts),SUM(Steals),SUM(RunnerRuns)
            FROM actions GROUP BY Action,BeforeOuts,BaseMask
            UNION ALL
            SELECT 'coverage',NULL,NULL,COUNT(*),SUM(Complete),NULL,NULL,NULL,NULL,NULL,NULL,NULL FROM halves
            """, r, ct);
        var stateRows = new List<Dictionary<string, object?>>();
        for (var outs=0; outs<3; outs++) for (var mask=0; mask<8; mask++)
        {
            var found=rows.FirstOrDefault(x=>ContextText(x,"Kind")=="state" && ContextNumber(x,"Outs")==outs && ContextNumber(x,"BaseMask")==mask);
            stateRows.Add(new() { ["Outs"]=outs,["BaseMask"]=mask,["State"]=ContextBases(mask),["N"]=found?.GetValueOrDefault("N")??0L,["RE"]=found?.GetValueOrDefault("RE") });
        }
        var actionRows=rows.Where(x=>ContextText(x,"Kind")=="action").ToList();
        foreach(var row in actionRows) row["State"]=ContextBases((int)(ContextNumber(row,"BaseMask")??0));
        var coverage=rows.FirstOrDefault(x=>ContextText(x,"Kind")=="coverage");
        var halves=coverage==null?0:ContextNumber(coverage,"N")??0;
        var complete=coverage==null?0:ContextNumber(coverage,"RE")??0;
        return new("expectancy","득점 기대값 RE24",
            ["정규시즌의 0아웃·주자 없음에서 시작하여 3아웃으로 끝난 공수 이닝만 사용합니다. 끝내기·미완료·상태/점수 불연속 이닝은 제외합니다.",
             "RE = 관측 시점부터 같은 공수 이닝 종료까지 득점의 평균. 모든 중계 그룹과 주자 이벤트의 점수 변화를 반영하며, 타석·주자 플레이가 있는 그룹 시작 상태를 표본으로 씁니다.",
             "RE 기준표는 선택 연도·기간·팀 경기·경기 ID의 전체 선수 표본입니다. 선수 필터는 아래 행동 관측에 적용합니다. 구종·타석방향·볼카운트는 RE 분석에 적용하지 않습니다.",
             "투수 선택 시 타석에 연결되지 않은 주자 전용 그룹은 투수에게 귀속하지 않습니다. 이런 그룹도 리그 RE와 이닝 종료 득점 계산에는 포함합니다.",
             "중계 그룹의 마지막 주자 상태는 다음 그룹의 시작 상태와 아웃·양팀 점수가 일치할 때만 연결합니다. 3아웃 이후 RE는 0입니다.",
             "행동표는 번트/도루/진루가 포함된 그룹 전체의 관측 결과입니다. 한 그룹 내 타격·복수 주자 플레이를 분리할 상태가 없어 개별 행동의 인과 효과나 성공 전략으로 해석할 수 없습니다."],
            [new("완결 공수 이닝",complete),new("제외 공수 이닝",halves-complete),new("상태 표본",stateRows.Sum(x=>ContextNumber(x,"N")??0))],
            [new("states","24개 주자·아웃 상태",[new("Outs","아웃","integer"),new("State","주자"),new("N","표본","integer"),new("RE","잔여 기대 득점","decimal")],stateRows),
             new("actions","행동 포함 그룹의 관측 결과",[new("Action","그룹 유형"),new("Outs","아웃","integer"),new("State","시작 주자"),new("N","그룹 수","integer"),new("RE","실제 이닝 잔여 득점 평균","decimal"),new("ObservedRuns","그룹 득점 평균","decimal"),new("Matched","전후 RE 연결 수","integer"),new("DeltaRE","그룹 RE 변화 평균","decimal"),new("StealAttempts","도루 관련 이벤트","integer"),new("Steals","파싱된 도루 성공","integer"),new("RunnerRuns","주자 득점 이벤트","integer")],actionRows)],
            new { kind="expectancy",states=stateRows.Select(x=>new { outs=x["Outs"],baseMask=x["BaseMask"],n=x["N"],re=x["RE"] }) });
    }

    private const string ContextGameFilter = """
        g.SeasonYear=$year AND LOWER(TRIM(g.RoundCode))='kbo_r'
        AND UPPER(g.HomeTeamCode) NOT IN ('EA','WE') AND UPPER(g.AwayTeamCode) NOT IN ('EA','WE')
        AND ($start='' OR g.GameDate>=$start) AND ($end='' OR g.GameDate<=$end)
        AND ($game='' OR g.GameId=$game) AND ($team='' OR g.HomeTeamCode=$team OR g.AwayTeamCode=$team)
        """;
    private const string ContextPaPlayerFilter = """
        ($code='' OR ($role='pitcher' AND (pa.PitcherPcode=$code OR pa.FinalPitcherPcode=$code)) OR ($role<>'pitcher' AND pa.BatterPcode=$code))
        """;

    private async Task<AnalysisResult> ReplayAsync(AnalysisRequest r,CancellationToken ct)
    {
        var appearances=await SqlAsync($"""
            SELECT pa.GameId,pa.PlateAppearanceId PaId,g.GameDate Date,pa.Inning||CASE pa.BattingSide WHEN 0 THEN '초' ELSE '말' END Inning,
              pa.BatterName Batter,COALESCE(pa.FinalPitcherName,pa.PitcherName) Pitcher,pa.ResultText Result,pa.ActualPitchCount Pitches
            FROM PlateAppearances pa JOIN Games g ON g.GameId=pa.GameId
            WHERE {ContextGameFilter} AND {ContextPaPlayerFilter}
              AND (($type='' AND $stance='' AND $count='') OR EXISTS (
                SELECT 1 FROM Pitches p WHERE p.PlateAppearanceId=pa.PlateAppearanceId
                  AND ($type='' OR p.PitchType=$type) AND ($stance='' OR p.BatterStance=$stance)
                  AND ($count='' OR CAST(p.BallsBefore AS TEXT)||'-'||CAST(p.StrikesBefore AS TEXT)=$count)))
            ORDER BY g.GameDate DESC,pa.GameId DESC,pa.SequenceNumber DESC LIMIT 51 OFFSET $offset
            """,r,ct);
        bool more=appearances.Count>50;
        if(more) appearances.RemoveAt(50);
        var selected=r;
        if(string.IsNullOrEmpty(selected.PaId) && appearances.Count>0)
            selected=r with { PaId=ContextText(appearances[0],"PaId"),GameId=ContextText(appearances[0],"GameId") };
        var pitches=string.IsNullOrEmpty(selected.PaId)?new List<Dictionary<string,object?>>():await SqlAsync($"""
            SELECT p.PitchEventId Id,p.ActualPitchIndex "Index",p.GameId,p.PlateAppearanceId PaId,
              CAST(p.BallsBefore AS TEXT)||'-'||CAST(p.StrikesBefore AS TEXT) "Count",p.PitchType Type,p.SpeedKmh Speed,p.PitchResult ResultCode,
              COALESCE(p.CalculatedCrossPlateX,p.CrossPlateX) PlateX,p.CalculatedCrossPlateZ PlateZ,
              p.TopStrikeZone Top,p.BottomStrikeZone Bottom,p.HasPtsTracking Tracking,
              p.X0,p.Y0,p.Z0,p.Vx0,p.Vy0,p.Vz0,p.Ax,p.Ay,p.Az,p.TimeToPlateSeconds Duration,p.CrossPlateY TargetY
            FROM Pitches p JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId JOIN Games g ON g.GameId=p.GameId
            WHERE {ContextGameFilter} AND {ContextPaPlayerFilter} AND p.PlateAppearanceId=$pa
            ORDER BY p.ActualPitchIndex,p.SourceOptionIndex,p.PitchEventId LIMIT 41
            """,selected,ct);
        var truncated=pitches.Count>40;
        if(truncated) pitches.RemoveAt(40);
        var tracks=new List<object>();
        var tracked=0;
        foreach(var pitch in pitches)
        {
            pitch["Result"]=ContextPitchResult((int)(ContextNumber(pitch,"ResultCode")??0));
            var points=ContextTrajectory(pitch);
            if(points.Count>0) tracked++;
            tracks.Add(new { id=pitch["Id"],index=pitch["Index"],type=pitch["Type"],speed=pitch["Speed"],result=pitch["Result"],points,
                plateX=pitch["PlateX"],plateZ=pitch["PlateZ"],top=pitch["Top"],bottom=pitch["Bottom"] });
            pitch["Tracking"]=points.Count>0?"구간 재생 가능":"유효 계수 없음";
            foreach(var key in new[]{"X0","Y0","Z0","Vx0","Vy0","Vz0","Ax","Ay","Az","Duration","TargetY","ResultCode"}) pitch.Remove(key);
        }
        var notes=new List<string>{"최근 타석을 50개씩 조회합니다. 타석을 선택하면 해당 타석의 모든 투구를 순서대로 표시합니다. 구종·방향·카운트 필터는 타석 목록을 고릅니다.",
            "PTS 좌표는 feet(ft), 시간은 초(s)입니다. x는 좌우, y는 홈 방향 거리, z는 높이입니다. 저장된 CrossPlateY는 높이가 아닌 홈의 앞뒤 기준면입니다.",
            "궤적은 x0 + vx0·t + ½ax·t² 식을 각 축에 적용한 측정 적합 구간입니다. 저장 기준점(보통 y0=50ft)부터 TimeToPlateSeconds까지 재생하며, 실제 릴리스나 팔 동작을 모션 캡처한 자료가 아닙니다.",
            "투구별 최대 25점·타석별 최대 40구. 계수 누락, 비정상 시간, 홈 기준면/종점 불일치 궤적은 숨기고 실제 투구 행은 남깁니다."};
        if(truncated) notes.Add("이 타석은 40구를 초과하여 첫 40구만 제공합니다.");
        if(appearances.Count==0) notes.Add("선택 조건에 해당하는 타석이 없습니다.");
        if(!string.IsNullOrEmpty(r.PaId) && pitches.Count==0) notes.Add("선택 타석에 일치하는 투구가 없습니다. 필터 불일치 또는 공식 보완 타석의 PTS 미제공일 수 있습니다.");
        return new("replay","실제 투구 리플레이",notes.ToArray(),[new("현재 페이지 타석",appearances.Count),new("선택 타석 투구",pitches.Count),new("유효 궤적",tracked)],
            [new("appearances","최근 타석 · 선택하여 재생",[new("Date","날짜"),new("GameId","경기 ID"),new("PaId","타석 ID"),new("Inning","이닝"),new("Batter","타자"),new("Pitcher","투수"),new("Result","타석 결과"),new("Pitches","투구","integer")],appearances),
             new("pitches","선택 타석의 투구 순서",[new("Index","순서","integer"),new("Count","볼-스트라이크"),new("Type","구종"),new("Speed","km/h","decimal"),new("Result","투구 결과"),new("PlateX","홈 X(ft)","decimal"),new("PlateZ","홈 높이(ft)","decimal"),new("Tracking","궤적")],pitches)],
            new { kind="replay",gameId=pitches.FirstOrDefault()?.GetValueOrDefault("GameId")??selected.GameId,paId=selected.PaId,unit="ft",description="PTS 적합 구간 · 실제 릴리스 아님",pitches=tracks },more,r.Page);
    }

    internal static List<Dictionary<string,double>> ContextTrajectory(Dictionary<string,object?> pitch)
    {
        var keys=new[]{"X0","Y0","Z0","Vx0","Vy0","Vz0","Ax","Ay","Az","Duration","TargetY"};
        var values=keys.Select(k=>ContextNumber(pitch,k)).ToArray();
        if(values.Any(v=>!v.HasValue || !double.IsFinite(v.Value))) return [];
        var v=values.Select(x=>x!.Value).ToArray();
        if(v[9] is <=0 or >1.5 || v[1] is <1 or >100 || v[4]>=0 || Math.Abs(v[10])>10) return [];
        double At(int axis,double t)=>v[axis]+v[axis+3]*t+0.5*v[axis+6]*t*t;
        if(Math.Abs(At(1,v[9])-v[10])>0.05) return [];
        var px=ContextNumber(pitch,"PlateX"); var pz=ContextNumber(pitch,"PlateZ");
        if(pz.HasValue && Math.Abs(At(2,v[9])-pz.Value)>0.05 || px.HasValue && Math.Abs(At(0,v[9])-px.Value)>0.3) return [];
        var points=new List<Dictionary<string,double>>();
        for(var i=0;i<=24;i++)
        {
            var t=v[9]*i/24; var x=At(0,t);var y=At(1,t);var z=At(2,t);
            if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z)||Math.Abs(x)>20||z is <-5 or >20||y is <-10 or >100) return [];
            points.Add(new(){["t"]=t,["x"]=x,["y"]=y,["z"]=z});
        }
        return points;
    }

    private async Task<AnalysisResult> ProvenanceAsync(AnalysisRequest r,CancellationToken ct)
    {
        var games=await SqlAsync($"""
            SELECT g.GameId,g.GameDate Date,g.AwayTeamName Away,g.HomeTeamName Home
            FROM Games g WHERE {ContextGameFilter}
              AND ($code='' OR EXISTS(SELECT 1 FROM PlateAppearances pa WHERE pa.GameId=g.GameId AND {ContextPaPlayerFilter}))
            ORDER BY g.GameDate DESC,g.GameId DESC LIMIT 51 OFFSET $offset
            """,r,ct);
        var more=games.Count>50;
        if(more) games.RemoveAt(50);
        var game=games.FirstOrDefault();
        if(game==null) return new("provenance","원본·공식·최종 기록 대조",["선택 조건에 해당하는 경기가 없습니다."],[],[],Page:r.Page);
        var selected=r with { GameId=ContextText(game,"GameId") };
        var notes=new List<string>{"전체 네이버 원본 JSON과 최초 박스스코어 스냅샷은 이 관계형 DB에 보존되지 않습니다. 최종 DB 기록을 네이버 원본으로 표시하지 않습니다.",
            "기존 타석의 ResultEventId에 연결된 NormalizedEvents.RawText만 보존된 네이버 원문으로 대조합니다. KBO가 추가한 타석은 네이버 원문 없음으로 표시합니다.",
            "공식 중계는 이닝·공수·타순·선수와 공식 타석 순서가 모두 일치할 때 연결합니다. 공식 박스스코어의 제공된 필드만 최종 집계와 비교하며, 누락값은 0으로 바꾸지 않습니다.",
            "공식 정정 이후 최종 기록이 다운로드 당시 공식 기록과 다를 수 있습니다. 아래 정정 공지와 저장된 반영 상태를 함께 확인하세요. 구종·방향·카운트 필터는 출처 대조에 적용하지 않습니다."};
        var sources=new List<Dictionary<string,object?>>();
        KboPlayLog.Document? official=null;
        if(await HasTableAsync("OfficialPlayLogs",ct))
        {
            var logs=await SqlAsync("SELECT Json,ImportedUtc FROM OfficialPlayLogs WHERE GameId=$game LIMIT 1",selected,ct);
            if(logs.Count>0)
            {
                try { official=KboPlayLog.Parse(ContextText(logs[0],"Json")); }
                catch(Exception ex) when(ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
                { notes.Add("저장된 공식 문자중계 형식을 검증할 수 없어 타석 대조에 사용하지 않았습니다."); }
                sources.Add(new(){["Source"]="KBO 공식 문자중계",["Status"]=official==null?"형식 검증 실패":"저장됨",["Imported"]=logs[0]["ImportedUtc"],
                    ["Url"]=official==null?null:ContextOfficialLink(official.GameId,selected.Year)});
            }
            else sources.Add(new(){["Source"]="KBO 공식 문자중계",["Status"]="선택 경기 저장본 없음"});
        }
        else sources.Add(new(){["Source"]="KBO 공식 문자중계",["Status"]="이 DB 버전에 테이블 없음"});

        var hasEvents=await HasTableAsync("NormalizedEvents",ct);
        var paRows=await SqlAsync($"""
            SELECT pa.GameId,pa.PlateAppearanceId PaId,pa.SequenceNumber Sequence,pa.OfficialSequenceNumber OfficialSequence,
              pa.Inning,pa.BattingSide,pa.BatOrder,pa.BatterName Batter,COALESCE(pa.FinalPitcherName,pa.PitcherName) Pitcher,
              pa.ResultType,pa.ResultText FinalText,
              {(hasEvents?"CASE WHEN ne.EventId NOT LIKE '%:kbo:%' AND ne.EventType=6 THEN ne.RawText END":"NULL")} Naver
            FROM PlateAppearances pa
            {(hasEvents?"LEFT JOIN NormalizedEvents ne ON ne.EventId=pa.ResultEventId":"")}
            WHERE pa.GameId=$game AND {ContextPaPlayerFilter} AND ($pa='' OR pa.PlateAppearanceId=$pa)
            ORDER BY pa.SequenceNumber LIMIT 200
            """,selected,ct);
        foreach(var row in paRows)
        {
            KboPlayLog.Entry? entry=null;
            var sequence=(int)(ContextNumber(row,"OfficialSequence")??0);
            if(official!=null && sequence>0 && sequence<=official.Entries.Count)
            {
                var candidate=official.Entries[sequence-1];
                if(candidate.Inning==ContextNumber(row,"Inning") && (int)candidate.Side==ContextNumber(row,"BattingSide")
                    && candidate.Order==ContextNumber(row,"BatOrder") && candidate.Name==ContextText(row,"Batter")) entry=candidate;
            }
            var original=ContextText(row,"Naver");
            var officialResult=entry==null?null:ContextOfficialResult(official!.GameId,entry);
            var finalResult=(int)(ContextNumber(row,"ResultType")??0);
            row["Official"]=entry?.Text;
            row["OfficialResult"]=officialResult.HasValue?ContextBattingResult(officialResult.Value):null;
            row["Final"]=ContextBattingResult(finalResult);
            row["Status"]=officialResult.HasValue && officialResult.Value!=finalResult?"공식·최종 분류 차이":original.Length==0?"네이버 원문 미보존":entry==null?"공식 연결 불가":ContextComparable(original)==ContextComparable(entry.Text)?"원문·공식 문장 일치":"원문·공식 문장 차이";
            row["Reason"]=officialResult.HasValue && officialResult.Value!=finalResult?"저장 공식 문장과 최종 분류가 다름 · 이후 정정 공지 확인":entry==null?"공식 자료 없거나 식별 조건 불일치":original.Length==0?"공식 보완 등 원문 확인 불가":"최종 결과는 공식 중계·정정·재집계 적용값";
            // Expose only explicitly named, public comparison fields, never source JSON.
            foreach(var key in new[]{"OfficialSequence","BattingSide","BatOrder","ResultType","FinalText"}) row.Remove(key);
        }
        sources.Add(new(){["Source"]="네이버 타석 원문",["Status"]=$"선택 타석 {paRows.Count}개 중 {paRows.Count(x=>!string.IsNullOrEmpty(ContextText(x,"Naver")))}개 보존"});
        sources.Add(new(){["Source"]="네이버 최초 박스스코어",["Status"]="DB에 최초 스냅샷 미보존 · 대조 불가"});

        var comparisons=new List<Dictionary<string,object?>>();
        var statTable=selected.Role=="pitcher"?"PitcherGameStats":"BatterGameStats";
        if(await HasTableAsync(statTable,ct))
        {
            var final=await SqlAsync(selected.Role=="pitcher"?"""
                SELECT Pcode,Name,TeamCode,TBF BattersFaced,InningsOuts,FinalPitchCount PitchCount,HitsAllowed,HomeRunsAllowed,
                  FinalBB Walks,FinalHBP HitBatters,FinalSO Strikeouts,RunsAllowed,EarnedRuns,WildPitches
                FROM PitcherGameStats WHERE GameId=$game AND ($code='' OR Pcode=$code) AND ($team='' OR TeamCode=$team) ORDER BY TeamCode,Pcode LIMIT 60
                """:"""
                SELECT Pcode,Name,TeamCode,PA PlateAppearances,AB AtBats,Runs,H Hits,Doubles,Triples,HR HomeRuns,RBI RunsBattedIn,
                  BB Walks,IBB IntentionalWalks,HBP HitByPitch,SO Strikeouts,SB StolenBases,CS CaughtStealing,GDP DoublePlays,SH SacrificeBunts,SF SacrificeFlies
                FROM BatterGameStats WHERE GameId=$game AND ($code='' OR Pcode=$code) AND ($team='' OR TeamCode=$team) ORDER BY TeamCode,Pcode LIMIT 60
                """,selected,ct);
            var boxRows=await HasTableAsync("OfficialBoxScorePlayers",ct)
                ?await SqlAsync("SELECT Pcode,TeamCode,Role,DownloadedUtc,StatsJson FROM OfficialBoxScorePlayers WHERE GameId=$game AND Role=$role AND ($code='' OR Pcode=$code) AND ($team='' OR TeamCode=$team) LIMIT 60",selected,ct)
                :new List<Dictionary<string,object?>>();
            sources.Add(new(){["Source"]="KBO 공식 선수 박스스코어",["Status"]=$"필터에 해당하는 저장 기록 {boxRows.Count}개"});
            foreach(var player in final)
            {
                var code=ContextText(player,"Pcode");var team=ContextText(player,"TeamCode");
                var source=boxRows.FirstOrDefault(x=>ContextText(x,"Pcode")==code && ContextText(x,"TeamCode")==team);
                using var json=ContextParseJson(source==null?null:ContextText(source,"StatsJson"));
                foreach(var metric in ContextBoxMetrics.Where(x=>player.ContainsKey(x.Key)))
                {
                    object? officialValue=null;
                    if(json!=null && json.RootElement.TryGetProperty(metric.Key,out var field) && field.ValueKind==JsonValueKind.Number && field.TryGetInt64(out var number)) officialValue=number;
                    var finalValue=player[metric.Key];
                    var sourceName=json!=null && json.RootElement.TryGetProperty("Source",out var sourceField) && sourceField.ValueKind==JsonValueKind.String?sourceField.GetString():null;
                    comparisons.Add(new(){["GameId"]=selected.GameId,["Code"]=code,["Player"]=player["Name"],["Team"]=team,["Metric"]=metric.Value,
                        ["Naver"]=null,["Official"]=officialValue,["Final"]=finalValue,["Status"]=officialValue==null?"공식 필드 미제공":Convert.ToInt64(finalValue,CultureInfo.InvariantCulture)==(long)officialValue?"일치":"차이 · 정정/반영 상태 확인",
                        ["Source"]=sourceName,["Imported"]=source?.GetValueOrDefault("DownloadedUtc")});
                    if(comparisons.Count>=500) break;
                }
                if(comparisons.Count>=500) { notes.Add("선수 기록 비교는 최대 500개 필드입니다. 선수 필터로 범위를 좁힐 수 있습니다."); break; }
            }
        }
        else notes.Add("이 DB에 최종 선수별 경기 집계 테이블이 없어 박스스코어 필드 대조를 생략했습니다.");

        var diagnostics=await HasTableAsync("Diagnostics",ct)?await SqlAsync("SELECT Code,Message FROM Diagnostics WHERE GameId=$game AND (Code LIKE 'KBO_CORRECTION_%' OR Code='KBO_PLAYLOG_APPLIED' OR Code LIKE 'KBO_BOX_%') ORDER BY DiagnosticId LIMIT 200",selected,ct):[];
        var corrections=new List<Dictionary<string,object?>>();
        if(await HasTableAsync("OfficialCorrections",ct))
        {
            // Filter before limiting: an old selected game must not disappear behind a
            // season's newer notices. CASE protects the optional legacy source JSON.
            var saved=await SqlAsync("""
                SELECT Id,Json,CheckedUtc FROM OfficialCorrections WHERE Year=$year
                  AND CASE WHEN json_valid(Json) THEN json_extract(Json,'$.Date') END=(SELECT GameDate FROM Games WHERE GameId=$game)
                ORDER BY Id DESC LIMIT 501
                """,selected,ct);
            if(saved.Count>500) { saved.RemoveAt(500);notes.Add("선택 날짜의 정정 공지가 500건을 초과하여 첫 500건만 대조했습니다."); }
            var identities=await SqlAsync("""
                SELECT g.GameDate,g.HomeTeamCode,g.AwayTeamCode,
                  (SELECT COUNT(*) FROM Games other WHERE other.GameDate=g.GameDate AND other.HomeTeamCode=g.HomeTeamCode
                    AND other.AwayTeamCode=g.AwayTeamCode AND LOWER(TRIM(other.RoundCode))=LOWER(TRIM(g.RoundCode))) MatchingGames
                FROM Games g WHERE g.GameId=$game LIMIT 1
                """,selected,ct);
            var ambiguousGame=ContextNumber(identities[0],"MatchingGames")>1;
            var names=string.IsNullOrEmpty(selected.Code)?Array.Empty<string>():await ContextPlayerNamesAsync(selected,ct);
            foreach(var item in saved)
            {
                using var doc=ContextParseJson(ContextText(item,"Json"));
                if(doc==null) continue;
                string Read(string key)=>doc.RootElement.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
                var identity=identities[0];var teams=Read("Match").Split(':');
                if(Read("Date")!=ContextText(identity,"GameDate") || teams.Length!=2
                    || ContextTeamCode(teams[0])!=ContextText(identity,"AwayTeamCode") || ContextTeamCode(teams[1])!=ContextText(identity,"HomeTeamCode")) continue;
                if(names.Length>0 && !names.Any(name=>Read("Players").Split('\n').Any(p=>p.Trim(' ','\r','(',')')==name))) continue;
                if(!string.IsNullOrEmpty(selected.Code) && names.Length==0) continue;
                var id=ContextText(item,"Id");
                var related=diagnostics.Where(d=>ContextText(d,"Message").StartsWith(id+":",StringComparison.Ordinal)).ToArray();
                var pending=related.Any(d=>ContextText(d,"Code")=="KBO_CORRECTION_PENDING");
                corrections.Add(new(){["Id"]=id,["Date"]=Read("Date"),["Inning"]=Read("Inning"),["Players"]=Read("Players").Trim(),["Before"]=Read("Before"),["After"]=Read("After"),["Reason"]=Read("Content"),
                    ["Status"]=ambiguousGame?"경기 식별 불가 · 동일 날짜·대진 복수 경기":pending?"검토 필요":related.Any(d=>ContextText(d,"Code")=="KBO_CORRECTION_APPLIED")?"대조 완료":"저장 공지 · 적용 상태 미확인",
                    ["Checked"]=item["CheckedUtc"],["Url"]="https://www.koreabaseball.com/Record/RecordCorrect/RecordCorrect.aspx"});
            }
            if(ambiguousGame && corrections.Count>0) notes.Add("정정 공지에는 날짜·대진만 있고 해당 경기 ID가 없습니다. 같은 날짜·대진의 경기가 여러 개이면 후보 공지로만 표시하며 특정 경기에 적용됐다고 단정하지 않습니다.");
            sources.Add(new(){["Source"]="KBO 정정 공지",["Status"]=$"{(ambiguousGame?"같은 날짜·대진 후보":"선택 경기/선수")} {corrections.Count}건",["Url"]="https://www.koreabaseball.com/Record/RecordCorrect/RecordCorrect.aspx"});
        }
        else notes.Add("이 DB 버전에는 공식 정정 공지 테이블이 없습니다.");
        var diffs=paRows.Count(x=>ContextText(x,"Status").Contains("차이",StringComparison.Ordinal))+comparisons.Count(x=>ContextText(x,"Status").StartsWith("차이",StringComparison.Ordinal));
        return new("provenance","원본·공식·최종 기록 대조",notes.ToArray(),[new("선택 경기",selected.GameId),new("확인된 차이",diffs),new("정정 공지",corrections.Count)],
            [new("games","경기 선택",[new("Date","날짜"),new("GameId","경기 ID"),new("Away","원정"),new("Home","홈")],games),
             new("sources","보존된 출처와 확인 범위",[new("Source","출처"),new("Status","상태"),new("Imported","저장 시각"),new("Url","공식 링크")],sources),
             new("plateComparisons","보존 원문 · 공식 타석 · 최종 분류",[new("Sequence","타석 순서","integer"),new("Batter","타자"),new("Pitcher","투수"),new("Naver","보존 네이버 문장"),new("Official","저장 공식 문장"),new("OfficialResult","공식 문장 분류"),new("Final","최종 결과 분류"),new("Status","대조 상태"),new("Reason","설명")],paRows),
             new("boxComparisons","공식 선수 기록 · 최종 DB 집계",[new("Player","선수"),new("Team","팀"),new("Metric","항목"),new("Naver","네이버 최초값 · 미보존"),new("Official","저장 공식값","integer"),new("Final","최종 DB값","integer"),new("Status","대조 상태"),new("Source","적용 출처"),new("Imported","공식 자료 시각")],comparisons),
             new("corrections","해당 경기·선수의 공식 정정 공지",[new("Id","공지 ID"),new("Inning","이닝"),new("Players","선수"),new("Before","정정 전"),new("After","정정 후"),new("Reason","정정 내용"),new("Status","저장 반영 상태"),new("Checked","확인 시각"),new("Url","공식 링크")],corrections)],HasMore:more,Page:r.Page);
    }

    private async Task<string[]> ContextPlayerNamesAsync(AnalysisRequest r,CancellationToken ct)
    {
        var rows=await SqlAsync("SELECT Name FROM GamePlayers WHERE GameId=$game AND Pcode=$code LIMIT 4",r,ct);
        return rows.Select(x=>ContextText(x,"Name")).Where(x=>x.Length>0).Distinct().ToArray();
    }
    private static JsonDocument? ContextParseJson(string? json)
    {
        if(string.IsNullOrWhiteSpace(json)) return null;
        try { var doc=JsonDocument.Parse(json);if(doc.RootElement.ValueKind==JsonValueKind.Object)return doc;doc.Dispose();return null; }
        catch(JsonException) { return null; }
    }
    private static int? ContextOfficialResult(string gameId,KboPlayLog.Entry entry)
    {
        // Reuse the public official parser on an isolated in-memory PA. This never imports
        // or writes the warehouse, and avoids a divergent duplicate Korean result classifier.
        var pa=new PlateAppearance { Inning=entry.Inning,BattingSide=entry.Side,BatOrder=entry.Order,
            BatterName=entry.Name,ResultText=entry.Text,IsOfficialPlateAppearance=true };
        var game=new NormalizedGame { GameId=gameId+gameId[..4],PlateAppearances=[pa] };
        KboPlayLog.Apply(game,new KboPlayLog.Document(gameId,"",[entry]));
        return pa.Outcome.WasRecognized?(int)pa.Outcome.ResultType:null;
    }
    private static string ContextComparable(string text)=>string.Join(" ",text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    private static string? ContextOfficialLink(string gameId,int year)=>gameId.Length==13 && gameId.All(c=>char.IsAsciiLetterOrDigit(c))
        ?$"https://www.koreabaseball.com/Game/LiveText.aspx?leagueId=1&seriesId=0&gameId={Uri.EscapeDataString(gameId)}&gyear={year}":null;
    private static string? ContextTeamCode(string team)=>team.Trim() switch {"한화"=>"HH","KIA"=>"HT","KT"=>"KT","LG"=>"LG","롯데"=>"LT","NC"=>"NC","두산"=>"OB","SSG" or "SK"=>"SK","삼성"=>"SS","키움"=>"WO",_=>null};
    private static string ContextBattingResult(int result)=>result switch {1=>"단타",2=>"내야 안타",3=>"번트 안타",4=>"2루타",5=>"3루타",6=>"홈런",7=>"볼넷",8=>"고의4구",9=>"몸에 맞는 공",10=>"삼진",11=>"땅볼 아웃",12=>"플라이 아웃",13=>"직선타 아웃",14=>"인필드 플라이",15=>"파울 플라이",16=>"번트 아웃",17=>"희생 플라이",18=>"희생 번트",19=>"병살타",20=>"실책 출루",21=>"야수 선택",22=>"기타 아웃",_=>"미분류"};
    private static readonly Dictionary<string,string> ContextBoxMetrics=new()
    {
        ["PlateAppearances"]="PA",["AtBats"]="AB",["Runs"]="R",["Hits"]="H",["Doubles"]="2B",["Triples"]="3B",["HomeRuns"]="HR",["RunsBattedIn"]="RBI",
        ["Walks"]="BB",["IntentionalWalks"]="IBB",["HitByPitch"]="HBP",["Strikeouts"]="SO",["StolenBases"]="SB",["CaughtStealing"]="CS",["DoublePlays"]="GDP",["SacrificeBunts"]="SH",["SacrificeFlies"]="SF",
        ["BattersFaced"]="TBF",["InningsOuts"]="투구 아웃",["PitchCount"]="투구 수",["HitsAllowed"]="피안타",["HomeRunsAllowed"]="피홈런",["HitBatters"]="사구",["RunsAllowed"]="실점",["EarnedRuns"]="자책점",["WildPitches"]="폭투"
    };

    private static AnalysisResult ContextUnavailable(string section,string title,string note)=>new(section,title,[note],[],[]);
    private static string ContextText(Dictionary<string,object?> row,string key)=>Convert.ToString(row.GetValueOrDefault(key),CultureInfo.InvariantCulture)??"";
    private static double? ContextNumber(Dictionary<string,object?> row,string key)=>row.GetValueOrDefault(key) is {} value
        && double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:null;
    private static string ContextBases(int mask)=>mask==0?"주자 없음":string.Join("·",Enumerable.Range(1,3).Where(b=>(mask&(1<<(b-1)))!=0).Select(b=>$"{b}루"));
    private static string ContextPitchResult(int result)=>result switch {1=>"볼",2=>"파울",3=>"인플레이",4=>"헛스윙",5=>"루킹 스트라이크",6=>"번트 파울",_=>"미분류"};
}
