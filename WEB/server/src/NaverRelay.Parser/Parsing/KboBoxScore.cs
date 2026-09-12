using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaverRelay.Parsing;

/// <summary>Official final lines. These never synthesize or redistribute play-by-play events.</summary>
public static class KboBoxScore
{
    public sealed record Identity(int RowNumber, string Name, string? NaverPcode, string? BirthDate, string? BackNumber)
    {
        public IReadOnlyList<string> CandidatePcodes { get; init; } = Array.Empty<string>();
        public bool NeedsLineupEvidence { get; init; }
    }
    public sealed record Batter(string Name, Identity? Identity, OfficialBattingStats Stats);
    public sealed record Pitcher(string Name, Identity? Identity, OfficialPitchingStats Stats);
    public sealed record Team(string TeamCode, int? Runs, int? Hits, IReadOnlyList<Batter> Batting, IReadOnlyList<Pitcher> Pitching);
    public sealed record Document(string GameId, string Json, DateTimeOffset? DownloadedAt, bool HasRowIdentities, Team Away, Team Home);

    public static Document Parse(JsonElement box, string gameId)
    {
        if (box.ValueKind != JsonValueKind.Object) throw new InvalidDataException("공식 박스스코어가 객체가 아닙니다.");
        var time = KboPlayLog.ReadSourceTime(box, "downloadedAt");
        var away = ReadTeam(box.GetProperty("away"), gameId.Substring(8, 2), time);
        var home = ReadTeam(box.GetProperty("home"), gameId.Substring(10, 2), time);
        VerifySum(away.Batting.Select(b => b.Stats.Runs), away.Runs, "원정 타자 득점");
        VerifySum(home.Batting.Select(b => b.Stats.Runs), home.Runs, "홈 타자 득점");
        VerifySum(away.Batting.Select(b => b.Stats.Hits), away.Hits, "원정 타자 안타");
        VerifySum(home.Batting.Select(b => b.Stats.Hits), home.Hits, "홈 타자 안타");
        VerifySum(away.Pitching.Select(p => p.Stats.RunsAllowed), home.Runs, "원정 투수 실점");
        VerifySum(home.Pitching.Select(p => p.Stats.RunsAllowed), away.Runs, "홈 투수 실점");
        VerifySum(away.Pitching.Select(p => p.Stats.HitsAllowed), home.Hits, "원정 투수 피안타");
        VerifySum(home.Pitching.Select(p => p.Stats.HitsAllowed), away.Hits, "홈 투수 피안타");
        VerifyAcross(away.Batting.Select(b=>b.Stats.HomeRuns),home.Pitching.Select(p=>p.Stats.HomeRunsAllowed),"원정 홈런");
        VerifyAcross(home.Batting.Select(b=>b.Stats.HomeRuns),away.Pitching.Select(p=>p.Stats.HomeRunsAllowed),"홈 홈런");
        VerifyAcross(away.Batting.Select(b=>b.Stats.Walks),home.Pitching.Select(p=>p.Stats.Walks),"원정 볼넷");
        VerifyAcross(home.Batting.Select(b=>b.Stats.Walks),away.Pitching.Select(p=>p.Stats.Walks),"홈 볼넷");
        VerifyAcross(away.Batting.Select(b=>b.Stats.Strikeouts),home.Pitching.Select(p=>p.Stats.Strikeouts),"원정 삼진");
        VerifyAcross(home.Batting.Select(b=>b.Stats.Strikeouts),away.Pitching.Select(p=>p.Stats.Strikeouts),"홈 삼진");
        bool identities = away.Batting.Any(b=>b.Identity!=null) || away.Pitching.Any(p=>p.Identity!=null) || home.Batting.Any(b=>b.Identity!=null) || home.Pitching.Any(p=>p.Identity!=null);
        return new(gameId,box.GetRawText(),time,identities,away,home);
    }

    static Team ReadTeam(JsonElement source, string expectedCode, DateTimeOffset? time)
    {
        var code = source.GetProperty("teamCode").GetString();
        if (code != expectedCode) throw new InvalidDataException("공식 박스스코어의 홈/원정 팀이 경기 ID와 다릅니다.");
        var totals = source.GetProperty("totals");
        var runs = Number(totals,"R"); var hits = Number(totals,"H");
        if (source.TryGetProperty("innings",out var innings))
        {
            if (innings.ValueKind != JsonValueKind.Array) throw new InvalidDataException("공식 이닝 점수 형식 오류");
            var numbers = new HashSet<int>(); var scores = new List<int?>();
            foreach (var inning in innings.EnumerateArray())
            {
                int number = Number(inning,"inning") ?? throw new InvalidDataException("이닝 번호 누락");
                if(number<1 || !numbers.Add(number)) throw new InvalidDataException("이닝 번호 중복/오류");
                scores.Add(Number(inning,"runs"));
            }
            if (runs.HasValue && scores.Where(s=>s.HasValue).Sum(s=>s!.Value)!=runs.Value)
                throw new InvalidDataException("이닝 득점 합계와 팀 득점이 다릅니다.");
        }
        var batters = new List<Batter>();
        foreach (var (row,id) in ReadRows(source.GetProperty("batting"),"타자"))
        {
            var s = new OfficialBattingStats {
                SourceTime=time, PlateAppearances=Number(row,"타석"), AtBats=Number(row,"타수"), Runs=Number(row,"득점"), Hits=Number(row,"안타"),
                Doubles=Number(row,"2루타"), Triples=Number(row,"3루타"), HomeRuns=Number(row,"홈런"), RunsBattedIn=Number(row,"타점"),
                Walks=Number(row,"볼넷"), IntentionalWalks=Number(row,"고의4구"), HitByPitch=Number(row,"사구"), Strikeouts=Number(row,"삼진"),
                StolenBases=Number(row,"도루"), CaughtStealing=Number(row,"도루실패"), DoublePlays=Number(row,"병살"),
                SacrificeBunts=Number(row,"희생번트"), SacrificeFlies=Number(row,"희생플라이"), Sacrifices=Number(row,"희타")
            };
            AtMost(s.Hits,s.AtBats,"안타/타수"); AtMost(s.HomeRuns,s.Hits,"홈런/안타"); AtMost(s.AtBats,s.PlateAppearances,"타수/타석"); AtMost(s.Strikeouts,s.AtBats,"삼진/타수");
            if(s.Doubles.HasValue&&s.Triples.HasValue&&s.HomeRuns.HasValue)AtMost(s.Doubles+s.Triples+s.HomeRuns,s.Hits,"장타/안타");
            batters.Add(new(Name(row,"타자"),id,s));
        }
        var pitchers = new List<Pitcher>();
        foreach(var (row,id) in ReadRows(source.GetProperty("pitching"),"투수"))
        {
            var s = new OfficialPitchingStats {
                SourceTime=time, InningsOuts=Innings(row), BattersFaced=Number(row,"타자"), AtBats=Number(row,"타수"), PitchCount=Number(row,"투구"),
                HitsAllowed=Number(row,"안타"), HomeRunsAllowed=Number(row,"홈런"), Walks=Number(row,"볼넷"), HitBatters=Number(row,"사구"),
                Strikeouts=Number(row,"삼진"), RunsAllowed=Number(row,"실점"), EarnedRuns=Number(row,"자책"), WildPitches=Number(row,"폭투")
            };
            AtMost(s.EarnedRuns,s.RunsAllowed,"자책/실점"); AtMost(s.HitsAllowed,s.AtBats,"피안타/상대타수"); AtMost(s.HomeRunsAllowed,s.HitsAllowed,"피홈런/피안타");
            AtMost(s.AtBats,s.BattersFaced,"상대타수/상대타자"); AtMost(s.Strikeouts,s.AtBats,"삼진/상대타수");
            pitchers.Add(new(Name(row,"투수"),id,s));
        }
        return new(code!,runs,hits,batters,pitchers);
    }

    static IEnumerable<(JsonElement Row, Identity? Identity)> ReadRows(JsonElement table,string nameColumn)
    {
        var columns = table.GetProperty("columns").EnumerateArray().Select(c=>c.GetString()??"").ToArray();
        if(!columns.Contains(nameColumn)||columns.Length<2||columns.Distinct().Count()!=columns.Length)throw new InvalidDataException("공식 박스스코어 열 이름 오류");
        var rows=table.GetProperty("rows").EnumerateArray().ToArray();
        if(rows.Length==0)throw new InvalidDataException("공식 박스스코어 선수 기록이 비었습니다.");
        Dictionary<int,Identity>? ids=null;
        if(table.TryGetProperty("rowIdentities",out var identities))
        {
            if(identities.ValueKind!=JsonValueKind.Array||identities.GetArrayLength()!=rows.Length)throw new InvalidDataException("공식 선수 연결 자료 누락");
            ids=new();var codes=new HashSet<string>();
            foreach(var item in identities.EnumerateArray())
            {
                var no=item.GetProperty("rowNumber").GetInt32(); var code=Text(item,"naverPcode"); var status=Text(item,"matchStatus");
                var unresolved = status == "ambiguous_kbo_rows" && string.IsNullOrWhiteSpace(code);
                if(no<1||no>rows.Length||(!unresolved&&(string.IsNullOrWhiteSpace(code)||!Regex.IsMatch(code,@"^\d+$")||status?.StartsWith("matched",StringComparison.Ordinal)!=true||!codes.Add(code))))
                    throw new InvalidDataException("공식 선수 ID 연결이 미해결/중복 상태입니다.");
                var candidates=item.TryGetProperty("candidatePcodes",out var candidateArray)
                    ? candidateArray.EnumerateArray().Select(c=>c.GetString()??"").ToArray() : code == null ? Array.Empty<string>() : new[]{code};
                if((unresolved ? candidates.Length<2 : !candidates.Contains(code!))||candidates.Any(c=>!Regex.IsMatch(c,@"^\d+$"))||candidates.Distinct().Count()!=candidates.Length)
                    throw new InvalidDataException("선택한 선수 ID와 후보 목록이 올바르지 않습니다.");
                if(status=="matched_unique_name"&&candidates.Length!=1)
                    throw new InvalidDataException("이름이 유일하다는 선수 연결에 복수 후보가 있습니다.");
                if(!ids.TryAdd(no,new(no,Name(item,"name"),code,Text(item,"birthDate"),Text(item,"backNumber")){CandidatePcodes=candidates,NeedsLineupEvidence=unresolved}))throw new InvalidDataException("선수 연결 행 번호 중복");
            }
        }
        for(int i=0;i<rows.Length;i++)
        {
            var name=Name(rows[i],nameColumn); var id=ids?.GetValueOrDefault(i+1);
            if(id!=null&&id.Name!=name)throw new InvalidDataException("공식 기록 행과 연결 선수 이름이 다릅니다.");
            yield return(rows[i],id);
        }
    }

    public static void Apply(NormalizedGame game,Document document,string gameId)
    {
        if(!Regex.IsMatch(gameId,@"^\d{8}[A-Z]{4}\d$")||document.GameId!=gameId||game.GameId!=gameId+gameId[..4]||game.AwayTeam.TeamCode!=document.Away.TeamCode||game.HomeTeam.TeamCode!=document.Home.TeamCode)
            throw new InvalidDataException("공식 박스스코어와 저장 대상 경기/팀이 다릅니다.");
        var batters=new List<(GamePlayerBattingLine Line,OfficialBattingStats Stats)>();
        var pitchers=new List<(GamePlayerPitchingLine Line,OfficialPitchingStats Stats)>();
        var identityNotes=new List<string>();
        foreach(var team in new[]{document.Away,document.Home})
        {
            var batterIds=ResolveBatterIdentities(game,team,identityNotes);
            var pitcherIds=ResolvePitcherIdentities(game,team,identityNotes);
            foreach(var row in team.Batting)
            {
                var id=row.Identity?.NeedsLineupEvidence==true ? batterIds[row.Identity.RowNumber] : row.Identity;
                var named=game.BattingLines.Where(l=>l.TeamCode==team.TeamCode&&l.Name?.Trim()==row.Name).ToArray();
                ValidateAmbiguousIdentity(id,named.Select(l=>(l.Pcode,l.BirthDateRaw,l.BackNumber)));
                var candidates=named.Where(l=>id==null||l.Pcode==id.NaverPcode).ToArray();
                if(candidates.Length!=1)throw new InvalidDataException($"{team.TeamCode} {row.Name}: 공식 타자 기록을 한 선수에 연결할 수 없습니다.");
                var line=candidates[0];CheckIdentity(id,line.BirthDateRaw,line.BackNumber);batters.Add((line,row.Stats));
            }
            foreach(var row in team.Pitching)
            {
                var id=row.Identity?.NeedsLineupEvidence==true ? pitcherIds[row.Identity.RowNumber] : row.Identity;
                var named=game.PitchingLines.Where(l=>l.TeamCode==team.TeamCode&&l.Name?.Trim()==row.Name).ToArray();
                ValidateAmbiguousIdentity(id,named.Select(l=>(l.Pcode,l.BirthDateRaw,l.BackNumber)));
                var candidates=named.Where(l=>id==null||l.Pcode==id.NaverPcode).ToArray();
                if(candidates.Length!=1)throw new InvalidDataException($"{team.TeamCode} {row.Name}: 공식 투수 기록을 한 선수에 연결할 수 없습니다.");
                var line=candidates[0];CheckIdentity(id,line.BirthDateRaw,line.BackNumber);pitchers.Add((line,row.Stats));
            }
        }
        // Finish all validation before mutating a single player. A failed import must leave the previous game intact.
        foreach(var message in identityNotes)Note(game,"KBO_BOX_ID_RESOLVED",message);
        foreach(var (line,s) in batters)
        {
            var pa=game.PlateAppearances.Where(p=>p.IsOfficialPlateAppearance&&p.BatterPcode==line.Pcode&&p.BattingTeamCode==line.TeamCode).ToArray();
            if((s.AtBats.HasValue&&s.AtBats!=pa.Count(p=>p.Outcome.CountsAsAtBat))||(s.Hits.HasValue&&s.Hits!=pa.Count(p=>p.Outcome.IsHit))||
               (s.HomeRuns.HasValue&&s.HomeRuns!=pa.Count(p=>p.Outcome.ResultType==BattingResultType.HomeRun))||(s.Strikeouts.HasValue&&s.Strikeouts!=pa.Count(p=>p.Outcome.IsStrikeout)))
                Note(game,"KBO_BOX_PA_DIFFERENCE",$"{line.Name}: 공식 박스스코어와 타석 합계가 다릅니다. 공식 최종 기록 적용; 타석/투구 원자료 유지.",DiagnosticSeverity.Warning);
            if(s.RunsBattedIn.HasValue&&line.RunsBattedIn!=s.RunsBattedIn)
                Note(game,"KBO_BOX_RBI_CORRECTED",$"{line.Name}: 타점 {line.RunsBattedIn}→{s.RunsBattedIn}");
            line.OfficialStats=s;
            line.PlateAppearances=s.PlateAppearances??line.PlateAppearances;line.AtBats=s.AtBats??line.AtBats;line.Hits=s.Hits??line.Hits;line.HomeRuns=s.HomeRuns??line.HomeRuns;
            line.Walks=s.Walks??line.Walks;line.HitByPitch=s.HitByPitch??line.HitByPitch;line.Strikeouts=s.Strikeouts??line.Strikeouts;
            line.Runs=s.Runs??line.Runs;line.RunsBattedIn=s.RunsBattedIn??line.RunsBattedIn;
        }
        foreach(var (line,s) in pitchers)
        {
            if((s.RunsAllowed.HasValue&&line.RunsAllowed!=s.RunsAllowed)||(s.EarnedRuns.HasValue&&line.EarnedRuns!=s.EarnedRuns))
                Note(game,"KBO_BOX_PITCHING_CORRECTED",$"{line.Name}: 실점 {line.RunsAllowed}→{s.RunsAllowed}, 자책 {line.EarnedRuns}→{s.EarnedRuns}");
            line.OfficialStats=s;
            if(s.InningsOuts.HasValue)line.InningsDisplay=$"{s.InningsOuts.Value/3}.{s.InningsOuts.Value%3}";
            line.PitchCount=s.PitchCount??line.PitchCount;line.HitsAllowed=s.HitsAllowed??line.HitsAllowed;line.HomeRunsAllowed=s.HomeRunsAllowed??line.HomeRunsAllowed;
            line.Walks=s.Walks??line.Walks;line.HitBatters=s.HitBatters??line.HitBatters;line.Strikeouts=s.Strikeouts??line.Strikeouts;
            line.RunsAllowed=s.RunsAllowed??line.RunsAllowed;line.EarnedRuns=s.EarnedRuns??line.EarnedRuns;line.WildPitches=s.WildPitches??line.WildPitches;
        }
        Note(game,"KBO_BOX_SCORE_APPLIED",$"공식 박스스코어 타자 {batters.Count}명·투수 {pitchers.Count}명 적용. 제공되지 않은 항목과 타석/투구 자료는 유지.");
        game.Summary.WarningCount=game.Diagnostics.Count(d=>d.Severity==DiagnosticSeverity.Warning);
        game.Summary.ErrorCount=game.Diagnostics.Count(d=>d.Severity==DiagnosticSeverity.Error);
    }

    static Dictionary<int,Identity> ResolveBatterIdentities(NormalizedGame game,Team team,List<string> notes)
    {
        var result=new Dictionary<int,Identity>();
        if(!team.Batting.Any(r=>r.Identity?.NeedsLineupEvidence==true))return result;
        var lines=game.BattingLines.Where(l=>l.TeamCode==team.TeamCode).OrderBy(l=>l.BatOrder).ThenBy(l=>l.LineupSequence).ToArray();
        if(lines.Any(l=>l.BatOrder is not (>=1 and <=9)||l.LineupSequence is not >0)||lines.Select(l=>(l.BatOrder,l.LineupSequence)).Distinct().Count()!=lines.Length)
            throw new InvalidDataException($"{team.TeamCode}: 동명이인 확인에 필요한 타순/교체순서가 불완전합니다.");
        ValidateTableOrder(team.Batting.Select(r=>(r.Name,r.Identity)).ToArray(),lines.Select(l=>(l.Name,l.Pcode)).ToArray(),team.TeamCode);
        foreach(var row in team.Batting.Where(r=>r.Identity?.NeedsLineupEvidence==true))
        {
            var id=row.Identity!;var named=lines.Where(l=>l.Name?.Trim()==row.Name).ToArray();
            ValidateUnresolvedCandidates(id,named.Select(l=>l.Pcode));
            var matches=named.Where(l=>BattingIdentityStatsMatch(row.Stats,l)).ToArray();
            var ordered=lines[id.RowNumber-1];
            if(matches.Length!=1||matches[0].Pcode!=ordered.Pcode)
                throw new InvalidDataException($"{team.TeamCode} {row.Name}: 타순/교체순서와 타격 기록으로 동명이인을 유일하게 확인할 수 없습니다.");
            CheckIdentity(id,ordered.BirthDateRaw,ordered.BackNumber);
            result.Add(id.RowNumber,id with{NaverPcode=ordered.Pcode,BirthDate=ordered.BirthDateRaw,BackNumber=ordered.BackNumber,NeedsLineupEvidence=false});
            notes.Add($"{team.TeamCode} {row.Name} 공식 {id.RowNumber}행 → {ordered.Pcode}: 전체 타순/교체순서·확정 선수 ID·AB/H/HR/BB/SO가 동일하고 후보 중 유일함");
        }
        return result;
    }

    static Dictionary<int,Identity> ResolvePitcherIdentities(NormalizedGame game,Team team,List<string> notes)
    {
        var result=new Dictionary<int,Identity>();
        if(!team.Pitching.Any(r=>r.Identity?.NeedsLineupEvidence==true))return result;
        var lines=game.PitchingLines.Where(l=>l.TeamCode==team.TeamCode).OrderBy(l=>l.AppearanceSequence).ToArray();
        if(lines.Any(l=>l.AppearanceSequence is not >0)||lines.Select(l=>l.AppearanceSequence).Distinct().Count()!=lines.Length)
            throw new InvalidDataException($"{team.TeamCode}: 동명이인 확인에 필요한 투수 등판순서가 불완전합니다.");
        ValidateTableOrder(team.Pitching.Select(r=>(r.Name,r.Identity)).ToArray(),lines.Select(l=>(l.Name,l.Pcode)).ToArray(),team.TeamCode);
        foreach(var row in team.Pitching.Where(r=>r.Identity?.NeedsLineupEvidence==true))
        {
            var id=row.Identity!;var named=lines.Where(l=>l.Name?.Trim()==row.Name).ToArray();
            ValidateUnresolvedCandidates(id,named.Select(l=>l.Pcode));
            var matches=named.Where(l=>PitchingIdentityStatsMatch(row.Stats,l)).ToArray();var ordered=lines[id.RowNumber-1];
            if(matches.Length!=1||matches[0].Pcode!=ordered.Pcode)
                throw new InvalidDataException($"{team.TeamCode} {row.Name}: 등판순서와 투구 기록으로 동명이인을 유일하게 확인할 수 없습니다.");
            CheckIdentity(id,ordered.BirthDateRaw,ordered.BackNumber);
            result.Add(id.RowNumber,id with{NaverPcode=ordered.Pcode,BirthDate=ordered.BirthDateRaw,BackNumber=ordered.BackNumber,NeedsLineupEvidence=false});
            notes.Add($"{team.TeamCode} {row.Name} 공식 {id.RowNumber}행 → {ordered.Pcode}: 전체 등판순서·확정 선수 ID·이닝/투구수/H/HR/BB/SO가 동일하고 후보 중 유일함");
        }
        return result;
    }

    static void ValidateTableOrder((string Name,Identity? Id)[] rows,(string? Name,string? Code)[] lines,string team)
    {
        if(rows.Length!=lines.Length||lines.Any(l=>l.Code==null||!Regex.IsMatch(l.Code,@"^\d+$"))||lines.Select(l=>l.Code).Distinct().Count()!=lines.Length)
            throw new InvalidDataException($"{team}: 동명이인 검증용 공식/네이버 선수 명단의 크기 또는 ID가 다릅니다.");
        for(var i=0;i<rows.Length;i++)
            if(rows[i].Name!=lines[i].Name?.Trim()||rows[i].Id?.RowNumber!=i+1||
                (rows[i].Id?.NeedsLineupEvidence!=true&&rows[i].Id?.NaverPcode!=lines[i].Code))
                throw new InvalidDataException($"{team}: 공식 표 행 순서와 네이버 타순/교체·등판순서 또는 확정 ID가 다릅니다.");
    }
    static void ValidateUnresolvedCandidates(Identity id,IEnumerable<string?> codes)
    {
        var all=codes.ToArray();
        if(all.Length<2||all.Length!=id.CandidatePcodes.Count||!all.ToHashSet().SetEquals(id.CandidatePcodes))
            throw new InvalidDataException($"{id.Name}: 미해결 ID 후보가 같은 경기·팀·역할의 동명이인 명단과 다릅니다.");
    }
    static bool BattingIdentityStatsMatch(OfficialBattingStats s,GamePlayerBattingLine l)=>
        SameKnown(s.AtBats,l.AtBats)&&SameKnown(s.Hits,l.Hits)&&SameKnown(s.HomeRuns,l.HomeRuns)&&
        SameKnown(s.Walks,l.Walks)&&SameKnown(s.Strikeouts,l.Strikeouts);
    static bool PitchingIdentityStatsMatch(OfficialPitchingStats s,GamePlayerPitchingLine l)=>
        SameKnown(s.InningsOuts,LineInningsOuts(l.InningsDisplay))&&SameKnown(s.PitchCount,l.PitchCount)&&SameKnown(s.HitsAllowed,l.HitsAllowed)&&
        SameKnown(s.HomeRunsAllowed,l.HomeRunsAllowed)&&SameKnown(s.Walks,l.Walks)&&SameKnown(s.Strikeouts,l.Strikeouts);
    static bool SameKnown(int? first,int? second)=>first.HasValue&&second.HasValue&&first.Value==second.Value;
    static int? LineInningsOuts(string? value)
    {
        var m=Regex.Match(value??"",@"^(\d+)(?:\.([012]))?$");
        return m.Success ? checked(int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*3+(m.Groups[2].Success?int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture):0)) : null;
    }

    static void CheckIdentity(Identity? id,string? birth,string? back)
    {
        if(id==null)return;
        string Digits(string? s)=>new((s??"").Where(char.IsDigit).ToArray());
        var first=Digits(id.BirthDate);var second=Digits(birth);
        if(first.Length==8&&second.Length==8&&first!=second)throw new InvalidDataException($"{id.Name}: 연결 선수 생년월일이 다릅니다.");
        if(int.TryParse(id.BackNumber,out var a)&&int.TryParse(back,out var b)&&a!=b)throw new InvalidDataException($"{id.Name}: 연결 선수 등번호가 다릅니다.");
    }
    static void ValidateAmbiguousIdentity(Identity? id,IEnumerable<(string? Code,string? Birth,string? Back)> candidates)
    {
        if(id==null)return;
        var rows=candidates.ToArray();
        if(id.CandidatePcodes.Any(code=>!rows.Any(r=>r.Code==code)))
            throw new InvalidDataException($"{id.Name}: 후보가 같은 경기·팀·역할의 선수 명단과 다릅니다.");
        if(rows.Length<=1&&id.CandidatePcodes.Count<=1)return;
        string Digits(string? value)=>new((value??"").Where(char.IsDigit).ToArray());
        var birth=Digits(id.BirthDate);var hasBirth=birth.Length==8;var hasBack=int.TryParse(id.BackNumber,out var back);
        if(!hasBirth&&!hasBack)throw new InvalidDataException($"{id.Name}: 동명이인 구분에 필요한 생년월일/등번호가 없습니다.");
        // Unknown candidate attributes cannot rule that candidate out.
        var possible=rows.Where(row=>
            (!hasBirth||Digits(row.Birth).Length!=8||Digits(row.Birth)==birth)&&
            (!hasBack||!int.TryParse(row.Back,out var rowBack)||rowBack==back)).ToArray();
        if(possible.Length!=1||possible[0].Code!=id.NaverPcode||
            !(hasBirth&&Digits(possible[0].Birth)==birth||hasBack&&int.TryParse(possible[0].Back,out var foundBack)&&foundBack==back))
            throw new InvalidDataException($"{id.Name}: 생년월일/등번호로도 동명이인을 유일하게 연결할 수 없습니다.");
    }
    static void Note(NormalizedGame game,string code,string message,DiagnosticSeverity severity=DiagnosticSeverity.Info)=>game.Diagnostics.Add(new(){GameId=game.GameId,Code=code,Message=message,Severity=severity});
    static string Name(JsonElement row,string field)=>Text(row,field)?.Trim() is {Length:>0} value ? value : throw new InvalidDataException("공식 선수 이름이 없습니다.");
    static string? Text(JsonElement row,string field)=>row.TryGetProperty(field,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString():null;
    static int? Number(JsonElement row,string field)
    {
        if(!row.TryGetProperty(field,out var item)||item.ValueKind==JsonValueKind.Null)return null;
        var value=item.ValueKind==JsonValueKind.String?item.GetString()?.Trim():item.GetRawText();
        if(string.IsNullOrEmpty(value)||value=="-")return null;
        if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var number)||number<0)throw new InvalidDataException($"공식 {field} 값 오류: {value}");
        return number;
    }
    static int? Innings(JsonElement row)
    {
        if(!row.TryGetProperty("이닝",out var item)||item.ValueKind==JsonValueKind.Null)return null;
        var text=item.ValueKind==JsonValueKind.String?item.GetString()?.Trim():item.GetRawText();
        if(string.IsNullOrEmpty(text)||text=="-")return null;
        // Baseball decimal notation denotes outs, never tenths of an inning.
        var m=Regex.Match(text,@"^(\d+)(?:\.([012]))?$");
        if(m.Success)return checked(int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*3+(m.Groups[2].Success?int.Parse(m.Groups[2].Value):0));
        m=Regex.Match(text,@"^(?:(\d+)\s+)?([12])/3$");
        if(m.Success)return checked((m.Groups[1].Success?int.Parse(m.Groups[1].Value):0)*3+int.Parse(m.Groups[2].Value));
        throw new InvalidDataException("공식 투구 이닝 형식 오류: "+text);
    }
    static void AtMost(int? a,int? b,string label){if(a.HasValue&&b.HasValue&&a>b)throw new InvalidDataException("공식 박스스코어 "+label+" 관계 오류");}
    static void VerifySum(IEnumerable<int?> values,int? expected,string label)
    {var items=values.ToArray();if(expected.HasValue&&items.Length>0&&items.All(x=>x.HasValue)&&items.Sum(x=>x!.Value)!=expected)throw new InvalidDataException(label+" 합계 불일치");}
    static void VerifyAcross(IEnumerable<int?> left,IEnumerable<int?> right,string label)
    {var a=left.ToArray();var b=right.ToArray();if(a.All(v=>v.HasValue)&&b.All(v=>v.HasValue))VerifySum(a,b.Sum(v=>v!.Value),label);}
}
