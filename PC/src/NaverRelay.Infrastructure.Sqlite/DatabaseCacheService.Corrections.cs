using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaverRelay.Application.Importing;
using NaverRelay.Parsing;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed record KboCorrection(string Id,int Year,string Date,string Match,string Inning,string Order,string Players,string Before,string After,string Content,string Published);
public sealed record CorrectionSyncResult(int Notices,int Reimported,int Pending,string Message)
{
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
}

public sealed partial class DatabaseCacheService
{
    const string CorrectionSchema="CREATE TABLE IF NOT EXISTS OfficialCorrections(Id TEXT PRIMARY KEY,Year INTEGER NOT NULL,Json TEXT NOT NULL,CheckedUtc TEXT NOT NULL);";
    static string Plain(string s)=>WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(s,@"<\s*/?br\s*/?>","\n",RegexOptions.IgnoreCase),"<[^>]+>",""));
    public static IReadOnlyList<KboCorrection> ParseCorrectionPage(string json,int year,out int total)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("RESULT_CD").ToString()!="100")throw new InvalidDataException("KBO 정정 조회 응답 오류");
        var table=root.GetProperty("recordTbl");total=int.Parse(table.GetProperty("totalCnt").ToString());
        return table.GetProperty("rows").EnumerateArray().Select(row=>{
            var cells=row.GetProperty("row").EnumerateArray().Select(c=>Plain(c.GetProperty("Text").GetString()??"")).ToArray();
            if(cells.Length!=12||!Regex.IsMatch(cells[1],@"^\d{4}/\d{2}/\d{2}$"))throw new InvalidDataException("KBO 정정 표 형식 변경");
            return new KboCorrection($"{year}:0:{cells[0]}",year,cells[1].Replace('/','-'),cells[3],cells[5],cells[6],cells[7],cells[8],cells[9],cells[10],cells[11]);
        }).ToArray();
    }
    static readonly Dictionary<string,string> KboTeams=new(){["한화"]="HH",["KIA"]="HT",["KT"]="KT",["LG"]="LG",["롯데"]="LT",["NC"]="NC",["두산"]="OB",["SSG"]="SK",["SK"]="SK",["삼성"]="SS",["키움"]="WO"};
    static bool Matches(KboCorrection n,NormalizedGame game)
    {
        var t=n.Match.Split(':');return game.IsRegularSeason&&game.GameDate?.StartsWith(n.Date)==true&&t.Length==2&&KboTeams.GetValueOrDefault(t[0])==game.AwayTeam.TeamCode&&KboTeams.GetValueOrDefault(t[1])==game.HomeTeam.TeamCode;
    }
    public async Task<CorrectionSyncResult> SyncKboCorrectionsAsync(int year,CancellationToken ct=default,HttpClient? transport=null)
    {
        using var http=transport??new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        http.DefaultRequestHeaders.Referrer=new Uri("https://www.koreabaseball.com/Record/RecordCorrect/RecordCorrect.aspx");
        http.DefaultRequestHeaders.Add("X-Requested-With","XMLHttpRequest");http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        var notices=new List<KboCorrection>();int total=0;
        for(int page=1;page<=100;page++){
            using var response=await http.PostAsync("https://www.koreabaseball.com/ws/Record.asmx/GetRecordCorrectList",new FormUrlEncodedContent(new Dictionary<string,string>{{"pageNo",page.ToString()},{"listCn","10"},{"seasonId",year.ToString()},{"srId","0"},{"month","0"},{"team",""},{"beforeRecord",""},{"afterRecord",""}}),ct);
            response.EnsureSuccessStatusCode();var rows=ParseCorrectionPage(await response.Content.ReadAsStringAsync(ct),year,out total);notices.AddRange(rows);
            if(notices.Count>=total)break;if(rows.Count==0||page==100)throw new InvalidDataException("KBO 정정 목록을 끝까지 읽지 못했습니다.");
        }
        if(notices.Select(x=>x.Id).Distinct().Count()!=total)throw new InvalidDataException("KBO 정정 목록 중복/누락");
        await using(var c=await OpenAsync(ct)){
            await using var cmd=c.CreateCommand();cmd.CommandText=CorrectionSchema;await cmd.ExecuteNonQueryAsync(ct);
            await using var tx=c.BeginTransaction();cmd.Transaction=tx;
            foreach(var n in notices){cmd.CommandText="INSERT INTO OfficialCorrections VALUES($id,$year,$json,$time) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json,CheckedUtc=excluded.CheckedUtc";cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$id",n.Id);cmd.Parameters.AddWithValue("$year",year);cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(n));cmd.Parameters.AddWithValue("$time",DateTime.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);}await tx.CommitAsync(ct);
        }
        var sources=new List<(string Id,string Source)>();
        await using(var c=await OpenAsync(ct))
        {
            await using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT g.GameId,(SELECT p.SourceDisplay FROM ParsedSources p WHERE p.GameId=g.GameId ORDER BY p.ParsedUtc DESC LIMIT 1),g.GameDate,g.AwayTeamCode,g.HomeTeamCode FROM Games g WHERE g.SeasonYear=$year AND LOWER(g.RoundCode)='kbo_r' AND EXISTS(SELECT 1 FROM ParsedSources p WHERE p.GameId=g.GameId)";
            cmd.Parameters.AddWithValue("$year",year);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))
            {
                if(notices.Any(n =>
                {
                    var teams=n.Match.Split(':');
                    return n.Date==r.GetString(2) && teams.Length==2 &&
                        KboTeams.GetValueOrDefault(teams[0])==r.GetString(3) && KboTeams.GetValueOrDefault(teams[1])==r.GetString(4);
                })) sources.Add((r.GetString(0),r.GetString(1)));
            }
        }
        int imported=0,pending=0;
        var details=new List<string>();
        foreach(var source in sources){ct.ThrowIfCancellationRequested();try{
            var parts=source.Source.Split("  >  ",2,StringSplitOptions.None);var input=new InputDocument{Id=source.Id,Kind=parts.Length==1?InputDocumentKind.JsonFile:InputDocumentKind.ZipEntry,ContainerPath=parts[0],EntryName=parts.Length==2?parts[1]:null,Length=File.Exists(parts[0])?new FileInfo(parts[0]).Length:0};
            var game=RelayParser.ParseJson(await input.ReadJsonAsync(ct));if(!notices.Any(n=>Matches(n,game)))continue;
            await SaveGameAndSourceAsync(game,input,ct);imported++;
            var warnings=game.Diagnostics.Where(x=>x.Code.StartsWith("KBO_CORRECTION_PENDING")).Select(x=>x.Message).Distinct().ToArray();
            if(warnings.Length>0){pending++;details.Add($"{game.GameId}: {string.Join(" / ",warnings)}");}
        }catch(OperationCanceledException){throw;}catch(Exception ex){pending++;details.Add($"{source.Id}: {ex.Message} (원본: {source.Source})");}}
        return new(notices.Count,imported,pending,$"KBO 정정 {notices.Count}건 대조 · {imported}경기 재집계 · 검토/원본 확인 필요 {pending}경기") { Details=details };
    }
    async Task ApplyKboCorrectionsAsync(NormalizedGame game,CancellationToken ct,DateTimeOffset? afterSource=null)
    {
        if(!game.IsRegularSeason)return;
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='OfficialCorrections'";if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==0)return;
        cmd.CommandText="SELECT Json FROM OfficialCorrections WHERE Year=$year ORDER BY Id";cmd.Parameters.AddWithValue("$year",game.SeasonYear??0);
        var notices=new List<KboCorrection>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct)){var n=JsonSerializer.Deserialize<KboCorrection>(r.GetString(0))!;if(Matches(n,game))notices.Add(n);}
        // Apply old notices first; each field is guarded by its published before/after values.
        foreach(var n in notices.OrderBy(n=>n.Published,StringComparer.Ordinal).ThenBy(n=>int.Parse(n.Id.Split(':')[2])))
        {
            if(afterSource.HasValue && DateOnly.TryParse(n.Published,out var published) &&
                published < DateOnly.FromDateTime(afterSource.Value.ToOffset(TimeSpan.FromHours(9)).DateTime))continue;
            cmd.CommandText="SELECT COUNT(*) FROM Games WHERE GameDate=$date AND HomeTeamCode=$home AND AwayTeamCode=$away AND GameId<>$id";cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$date",game.GameDate!);cmd.Parameters.AddWithValue("$home",game.HomeTeam.TeamCode!);cmd.Parameters.AddWithValue("$away",game.AwayTeam.TeamCode!);cmd.Parameters.AddWithValue("$id",game.GameId);
            if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))>0){game.Diagnostics.Add(new ParserDiagnostic{GameId=game.GameId,Severity=DiagnosticSeverity.Warning,Code="KBO_CORRECTION_PENDING",Message=$"{n.Id}: 같은 날짜·대진 복수 경기, 경기 식별 검토 필요"});continue;}
            ApplyCorrection(game,n);
        }
    }
    public static void ApplyCorrection(NormalizedGame game,KboCorrection n)
    {
        void Note(string message,bool pending=true)=>game.Diagnostics.Add(new ParserDiagnostic{GameId=game.GameId,Severity=pending?DiagnosticSeverity.Warning:DiagnosticSeverity.Info,Code=pending?"KBO_CORRECTION_PENDING":"KBO_CORRECTION_APPLIED",Message=$"{n.Id}: {message}"});
        var teams=n.Match.Split(':');if(teams.Length!=2)return;
        var inningMatch=Regex.Match(n.Inning,@"^(\d+)(초|말)$");
        if(!inningMatch.Success){Note("이닝 식별 불가");return;}
        var battingTeam=KboTeams.GetValueOrDefault(teams[inningMatch.Groups[2].Value=="초"?0:1]);
        var candidates=game.PlateAppearances.Where(p=>p.Inning==int.Parse(inningMatch.Groups[1].Value)&&p.BattingTeamCode==battingTeam&&p.BatOrder?.ToString()==n.Order&&p.BatterName!=null&&n.Players.Split('\n').Contains(p.BatterName)).ToArray();
        if(candidates.Length!=1){Note("타석을 유일하게 식별할 수 없음");return;}
        var pa=candidates[0];var outcome=pa.Outcome;
        var currentCategory=outcome.IsHit?"안타":outcome.ResultType==BattingResultType.ReachedOnError || (outcome.ResultType==BattingResultType.SacrificeBunt && outcome.ReachedBase && pa.ResultText?.Contains("실책으로 출루")==true)?"실책":outcome.ResultType==BattingResultType.FieldersChoice || (outcome.ResultType==BattingResultType.SacrificeBunt && outcome.ReachedBase && pa.ResultText?.Contains("야수선택")==true)?"야수 선택":null;
        if(currentCategory!=n.Before&&currentCategory!=n.After){Note("현재 타석 결과가 정정 전후 유형과 일치하지 않음");return;}
        var afterHit=n.After=="안타";var beforeHit=n.Before=="안타";
        // Exact hit type is derived from the published batter total-base delta.
        var batterChange=Regex.Match(n.Content,Regex.Escape(pa.BatterName!)+@"\(([^)]*)\)");
        var tb=Regex.Match(batterChange.Groups[1].Value,@"루타(?:수)?\s*(\d+)→(\d+)");
        if(afterHit&&!outcome.IsHit){
            if(!tb.Success||!beforeHit&&int.Parse(tb.Groups[2].Value)-int.Parse(tb.Groups[1].Value) is <1 or >4){Note("안타 종류를 정정 내용으로 확정할 수 없음");return;}
            var bases=int.Parse(tb.Groups[2].Value)-int.Parse(tb.Groups[1].Value);
            outcome.WasRecognized=true;outcome.IsHit=true;outcome.IsOut=false;outcome.IsSacrifice=false;outcome.CountsAsAtBat=true;outcome.ReachedBase=true;outcome.TotalBases=bases;outcome.ResultType=bases switch{1=>BattingResultType.Single,2=>BattingResultType.Double,3=>BattingResultType.Triple,_=>BattingResultType.HomeRun};
        }else if(!afterHit&&outcome.IsHit&&beforeHit){
            outcome.IsHit=false;outcome.IsOut=false;outcome.TotalBases=0;outcome.ReachedBase=true;
            var sacrifice=Regex.IsMatch(batterChange.Groups[1].Value,@"희타\s*0→1");outcome.CountsAsAtBat=!sacrifice;outcome.IsSacrifice=sacrifice;outcome.ResultType=sacrifice?BattingResultType.SacrificeBunt:n.After=="실책"?BattingResultType.ReachedOnError:BattingResultType.FieldersChoice;
        }else if(n.After is not("안타" or "실책" or "야수 선택")){Note("지원하지 않는 정정 유형");return;}
        else if(!afterHit&&currentCategory!=n.After){outcome.ResultType=n.After=="실책"?BattingResultType.ReachedOnError:BattingResultType.FieldersChoice;}
        string? team=null;
        foreach(var line in n.Content.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){
            if(KboTeams.TryGetValue(line,out var code)){team=code;continue;}
            var m=Regex.Match(line,@"^([^()]+)\(([^)]*)\)$");if(!m.Success){Note("정정 문장 해석 불가: "+line);continue;}
            var name=m.Groups[1].Value.Trim();var b=game.BattingLines.Where(x=>x.TeamCode==team&&x.Name==name).ToArray();var p=game.PitchingLines.Where(x=>x.TeamCode==team&&x.Name==name).ToArray();
            foreach(Match change in Regex.Matches(m.Groups[2].Value,@"([^,\d]+?)\s*(\d+)→(\d+)")){
                var metric=change.Groups[1].Value.Trim();int before=int.Parse(change.Groups[2].Value),after=int.Parse(change.Groups[3].Value);
                object? target=null;string? prop=null;
                if(b.Length==1){prop=metric switch{"타수"=>"AtBats","안타"=>"Hits","타점"=>"RunsBattedIn","홈런"=>"HomeRuns","볼넷"=>"Walks","삼진"=>"Strikeouts",_=>null};if(prop!=null)target=b[0];}
                if(target==null&&p.Length==1){prop=metric switch{"피안타"=>"HitsAllowed","자책점"=>"EarnedRuns","실점"=>"RunsAllowed","삼진"=>"Strikeouts","볼넷"=>"Walks",_=>null};if(prop!=null)target=p[0];}
                if(target==null){if(metric is not("루타수" or "루타" or "희타"))Note($"{name} {metric}: 자동 반영 지원 없음");continue;}
                var property=target.GetType().GetProperty(prop!)!;var current=(int?)property.GetValue(target);
                if(current==before)property.SetValue(target,(int?)after);else if(current!=after)Note($"{name} {metric}: 현재 {current}, 기대 {before} 또는 {after}");
                if(prop=="RunsBattedIn" && target is GamePlayerBattingLine corrected && corrected.RunsBattedIn==after)
                {
                    // Notices expose a date, not a publication time. Protect the entire
                    // notice day from an older daily cache; a later fresh check may supersede it.
                    DateTimeOffset? publishedBoundary=DateOnly.TryParse(n.Published,out var day)
                        ? new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(9)) : null;
                    var previous=corrected.OfficialStats ?? new OfficialBattingStats();
                    var effective=previous.SourceTime.HasValue && publishedBoundary.HasValue && previous.SourceTime>publishedBoundary
                        ? previous.SourceTime : publishedBoundary;
                    corrected.OfficialStats=previous with { Source="KBO_CORRECTION",SourceTime=effective,RunsBattedIn=after };
                }
            }
        }
        Note("타석/박스스코어 대조 완료; 원본 중계 문장은 보존",false);
    }
}
