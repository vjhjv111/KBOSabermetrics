using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

internal static class BulkCombinedImportTests
{
    static readonly HashSet<string> DroppedThirdStrikeGames = ["20260407KTLT02026","20260419SKNC02026","20260522WOLG02026","20260614LTLG02026","20260618WOSS02026","20260621SKNC02026","20260623SSLG02026","20260624OBHH02026","20260731SKWO02026","20260816OBHT02026"];
    static readonly HashSet<string> IdentityRecoveryGames = ["20260505WOSS02026","20260508KTWO02026","20260509KTWO02026","20260521LTHH02026","20260602HHOB02026","20260607HHLT02026","20260625OBHH02026","20260707NCHH02026","20260804HHSS02026","20260812SSHT02026","20260815HHSS02026","20260825SSWO02026","20260905SSLG02026"];
    static readonly Dictionary<string,(string Name,string Away,int AwayOrder,string Home,int HomeOrder)> SameNames=new(){["20260410OBKT02026"]=("김민석","OB",6,"KT",8),["20260625NCLT02026"]=("박건우","NC",4,"LT",9),["20260908LTNC02026"]=("박건우","LT",8,"NC",2)};
    static void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    public static async Task Run(string folder,string outputRoot,string readonlyCacheDb)
    {
        var started=DateTimeOffset.UtcNow;var run=Path.Combine(outputRoot,"season-"+started.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(run);
        var db=Path.Combine(run,"season.db");var store=new DatabaseCacheService(db);await store.InitializeAsync();
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db,Mode=SqliteOpenMode.ReadWrite}.ToString());await c.OpenAsync();
        var cacheCounts=await CopyOfficialCaches(readonlyCacheDb,c);
        var files=Directory.GetFiles(folder,"*.json").Select(Path.GetFullPath).OrderBy(Path.GetFileName,StringComparer.Ordinal).ToArray();
        var manifest=files.Select(f=>new FileInfo(f)).ToDictionary(f=>f.FullName,f=>(f.Length,f.LastWriteTimeUtc));
        var failures=new List<object>();var deferred=new List<object>();var warnings=new List<object>();var gameTotals=new List<object>();
        var recovered=new List<string>();var strikeouts=new List<string>();var sameNames=new List<string>();var changed=new List<string>();
        int success=0,totalPitches=0,totalPa=0;Console.WriteLine($"START whole season {files.Length} files, caches {JsonSerializer.Serialize(cacheCounts)}, DB {db}");
        foreach(var file in files)
        {
            string stage="Read";NormalizedGame? game=null;
            try
            {
                var json=File.ReadAllText(file);stage="Parse";game=RelayParser.ParseJson(json);
                var official=game.ImportedOfficialSource??throw new InvalidDataException("통합 공식 자료가 없습니다.");
                var sourcePitches=JsonSerializer.Serialize(game.PitchEvents);
                stage="Official plays";KboPlayLog.Apply(game,official);
                stage="Official box";KboBoxScore.Apply(game,official.BoxScore??throw new InvalidDataException("공식 박스가 없습니다."),official.GameId);
                stage="Regression invariants";
                Check(sourcePitches==JsonSerializer.Serialize(game.PitchEvents),"투구 원자료가 공식 대조 중 바뀌었습니다.");
                if(DroppedThirdStrikeGames.Contains(game.GameId))
                {
                    var plays=game.PlateAppearances.Where(x=>x.IsOfficialPlateAppearance&&x.ResultText?.Contains("낫아웃")==true&&x.ResultText.Contains("다른주자")).ToArray();
                    Check(plays.Length>0&&plays.All(x=>x.Outcome.IsStrikeout&&x.Outcome.CountsAsAtBat&&x.Outcome.ReachedBase&&!x.Outcome.IsOut&&!x.Outcome.IsHit),"낫아웃 다른주자 수비 출루는 삼진·타수이고 타자 아웃/안타가 아닙니다.");strikeouts.Add(game.GameId);
                }
                if(IdentityRecoveryGames.Contains(game.GameId))
                {
                    Check(game.Diagnostics.Any(x=>x.Code=="KBO_BOX_ID_RESOLVED"),"공식 동명이인 행의 안전한 ID 복구 진단이 없습니다.");recovered.Add(game.GameId);
                }
                if(SameNames.TryGetValue(game.GameId,out var match))
                {
                    var away=game.BattingLines.Single(x=>x.TeamCode==match.Away&&x.Name==match.Name);var home=game.BattingLines.Single(x=>x.TeamCode==match.Home&&x.Name==match.Name);
                    Check(away.Pcode!=home.Pcode&&away.Pcode!=null&&home.Pcode!=null,"양팀 동명이인 선수 코드가 혼합됐습니다.");
                    foreach(var (team,line,order) in new[]{(match.Away,away,match.AwayOrder),(match.Home,home,match.HomeOrder)})
                    {
                        var plays=game.PlateAppearances.Where(x=>x.IsOfficialPlateAppearance&&x.BatterName==match.Name&&x.BattingTeamCode==team).ToArray();
                        Check(plays.Length>0&&plays.All(x=>x.BatterPcode==line.Pcode&&x.BatOrder==order),"양팀 동명이인 타순/선수 코드가 혼합됐습니다.");
                    }
                    sameNames.Add(game.GameId);
                }
                stage="DB save with official caches";
                // The assertions above inspect a separate normalized game; exercise the actual
                // GUI import order (fresh parse -> save) for the persisted result.
                game=RelayParser.ParseJson(json);
                await store.SaveGameAndSourceAsync(game,new InputDocument{Id=game.GameId,Kind=InputDocumentKind.JsonFile,ContainerPath=file,Length=manifest[file].Length});
                success++;totalPitches+=game.PitchEvents.Count;totalPa+=game.PlateAppearances.Count(x=>x.IsOfficialPlateAppearance);
                var pending=game.Diagnostics.Where(x=>x.Severity!=DiagnosticSeverity.Info&&(x.Code.StartsWith("KBO_")||x.Code.Contains("IDENTITY"))).Select(x=>new{x.Code,x.Message}).ToArray();
                if(pending.Length>0)warnings.Add(new{game.GameId,Diagnostics=pending});
                gameTotals.Add(new{game.GameId,PA=game.PlateAppearances.Count(x=>x.IsOfficialPlateAppearance),Batters=game.BattingLines.Count,Pitchers=game.PitchingLines.Count,Pitches=game.PitchEvents.Count,Warnings=pending.Length});
            }
            catch(GameNotStartedException ex){deferred.Add(new{File=Path.GetFileName(file),ex.GameId,Reason=ex.Message});Console.WriteLine("DEFER "+Path.GetFileName(file)+" "+ex.Message);}
            catch(Exception ex){failures.Add(new{File=Path.GetFileName(file),GameId=game?.GameId,Stage=stage,Exception=ex.GetType().Name,ex.Message});Console.WriteLine("FAIL "+Path.GetFileName(file)+" ["+stage+"] "+ex.Message);}
            var info=new FileInfo(file);if(info.Length!=manifest[file].Length||info.LastWriteTimeUtc!=manifest[file].LastWriteTimeUtc)changed.Add(info.Name);
            if((success+failures.Count+deferred.Count)%25==0)Console.WriteLine($"PROGRESS {success+failures.Count+deferred.Count}/{files.Length} pass={success} fail={failures.Count} deferred={deferred.Count} elapsed={(DateTimeOffset.UtcNow-started).TotalSeconds:F0}s");
        }
        var totals=await ReadTotals(c);var teamTotals=await ReadTeams(c);var allDiagnostics=await ReadWarnings(c);
        foreach(var (file,before) in manifest){var now=new FileInfo(file);if(!now.Exists||now.Length!=before.Length||now.LastWriteTimeUtc!=before.LastWriteTimeUtc)changed.Add(now.Name);}
        var report=new{StartedUtc=started,FinishedUtc=DateTimeOffset.UtcNow,SourceFolder=folder,Database=db,CacheCounts=cacheCounts,InputCount=files.Length,Success=success,FailureCount=failures.Count,Deferred=deferred,TotalPa=totalPa,TotalPitches=totalPitches,Totals=totals,Teams=teamTotals,RecoveredIdentityGames=recovered,DroppedStrikeoutGames=strikeouts,SeparatedSameNameGames=sameNames,Failures=failures,Warnings=warnings,StoredWarnings=allDiagnostics,ChangedSources=changed.Distinct().ToArray(),Games=gameTotals};
        var output=Path.Combine(run,"report.json");await File.WriteAllTextAsync(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        Console.WriteLine("TOTALS "+JsonSerializer.Serialize(totals));Console.WriteLine("REPORT "+output);
        Check(changed.Count==0,"검증 중 원본 파일이 변경되었습니다. 보고서를 확인해주세요.");
        Check(failures.Count==0&&success==622&&deferred.Count==4,"종료 경기 622개 성공 / 미시작 4개 보류를 충족하지 못했습니다.");
        Check(recovered.Count==13&&strikeouts.Count==10&&sameNames.Count==3,"실패 유형 26경기 회귀 검증이 누락되었습니다.");
        foreach(var (metric,expected) in new Dictionary<string,long>{{"G",622},{"PA",49044},{"AB",42790},{"H",11456},{"HR",1161},{"BB",4617},{"SO",9485},{"Runs",6316},{"RBI",5983},{"GDP",910},{"SH",517},{"SF",383}})
            Check(totals[metric]==expected,$"시즌 {metric}: DB {totals[metric]}, 공식 {expected}");
        Console.WriteLine("PASS 전체 622경기 DB 집계 / 4미시작 보류 / 26실패 사례 복구 / 공식 시즌 합계");
        await VerifyPitchers(db);
    }
    public static async Task VerifyPitchers(string db)
    {
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=db,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync();
        var totals=new Dictionary<string,long>();var columns=new[]{"RunsAllowed","EarnedRuns","HitsAllowed","HomeRunsAllowed","FinalBB","FinalHBP","FinalSO","InningsOuts","TBF"};
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT "+string.Join(",",columns.Select(x=>"SUM("+x+")"))+" FROM PitcherGameStats WHERE HasFinalLine=1";using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();for(int i=0;i<columns.Length;i++)totals[columns[i]]=r.GetInt64(i);}
        var cases=new[]{("20260602LTHT02026","최준용",1,0),("20260707NCHH02026","원종혁",2,0),("20260818OBNC02026","손주환",2,2),("20260818OBNC02026","김진호",1,1),("20260705OBWO02026","원종현",3,3)};
        var players=new List<object>();
        foreach(var (id,name,runs,earned) in cases)
        {
            using var cmd=c.CreateCommand();cmd.CommandText="SELECT Pcode,RunsAllowed,EarnedRuns FROM PitcherGameStats WHERE GameId=$id AND Name=$name";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$name",name);using var r=await cmd.ExecuteReaderAsync();Check(await r.ReadAsync(),"개인 투수 검증 대상 누락: "+name);var actualR=r.GetInt32(1);var actualEr=r.GetInt32(2);players.Add(new{GameId=id,Name=name,Pcode=r.GetString(0),Runs=actualR,EarnedRuns=actualEr});Check(actualR==runs&&actualEr==earned,$"개인 공식 투수 보정 {name}: {actualR}/{actualEr}, 기대{runs}/{earned}");Check(!await r.ReadAsync(),"투수 행 중복: "+name);
        }
        var output=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(db))!,"pitcher-summary.json");await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{Totals=totals,Players=players},new JsonSerializerOptions{WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        foreach(var (key,value) in new Dictionary<string,long>{{"RunsAllowed",6316},{"EarnedRuns",5736},{"HitsAllowed",11456},{"HomeRunsAllowed",1161},{"FinalBB",4617},{"FinalSO",9485}})Check(totals[key]==value,$"공식 개인 투수 시즌 합계 {key}: {totals[key]}, 기대 {value}");
        Console.WriteLine("PASS 공식 개인 투수 시즌 합계 / 개인 실점·자책점 수정 5명 "+output);
    }
    public static async Task VerifySacrificeCorrection(string folder,string readonlyCacheDb,string outputRoot)
    {
        var output=Path.Combine(outputRoot,"sacrifice-correction-"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(output);
        var store=new DatabaseCacheService(Path.Combine(output,"check.db"));await store.InitializeAsync();using var c=new SqliteConnection("Data Source="+Path.Combine(output,"check.db"));await c.OpenAsync();await CopyOfficialCaches(readonlyCacheDb,c);
        var path=Path.Combine(folder,"20260701SKHT02026.json");var game=RelayParser.ParseFile(path);await store.SaveGameAndSourceAsync(game,new InputDocument{Id=game.GameId,Kind=InputDocumentKind.JsonFile,ContainerPath=path,Length=new FileInfo(path).Length});
        Check(!game.Diagnostics.Any(d=>d.Code=="KBO_CORRECTION_PENDING"&&d.Message.Contains("정정 전후 유형")),"희생번트 실책 출루를 이미 정정된 실책으로 인식하지 못했습니다.");
        var pa=game.PlateAppearances.Single(p=>p.Inning==11&&p.BattingTeamCode=="HT"&&p.BatterName=="김호령");Check(pa.Outcome.ResultType==BattingResultType.SacrificeBunt&&pa.Outcome.ReachedBase&&!pa.Outcome.CountsAsAtBat&&!pa.Outcome.IsHit,"이미 올바른 희생번트 실책 타석을 변경했습니다.");
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT AB,H,SH FROM BatterGameStats WHERE GameId='20260701SKHT02026' AND Name='김호령'";using var r=await cmd.ExecuteReaderAsync();Check(await r.ReadAsync()&&r.GetInt32(0)==5&&r.GetInt32(1)==2&&r.GetInt32(2)==1,"김호령 공식 AB5/H2/SH1이 유지되지 않았습니다.");}
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT HitsAllowed,RunsAllowed,EarnedRuns FROM PitcherGameStats WHERE GameId='20260701SKHT02026' AND Name='김민'";using var r=await cmd.ExecuteReaderAsync();Check(await r.ReadAsync()&&r.GetInt32(0)==3&&r.GetInt32(1)==2&&r.GetInt32(2)==1,"김민 공식 H3/R2/ER1이 유지되지 않았습니다.");}
        Console.WriteLine("PASS 7/1 희생번트 실책 이미 정정된 AB5/H2/SH1 및 투수 H3/R2/ER1 유지 / 잘못된 유형 경고 없음");
        foreach(var warning in game.Diagnostics.Where(x=>x.Code=="KBO_CORRECTION_PENDING"))Console.WriteLine("INFO 남은 미지원 필드 "+warning.Message);
    }
    static async Task<Dictionary<string,int>> CopyOfficialCaches(string source,SqliteConnection destination)
    {
        using var readonlyDb=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await readonlyDb.OpenAsync();
        var counts=new Dictionary<string,int>();
        foreach(var table in new[]{"OfficialCorrections","OfficialDailyBattingSources"})
        {
            using var schema=readonlyDb.CreateCommand();schema.CommandText="SELECT sql FROM sqlite_master WHERE type='table' AND name=$name";schema.Parameters.AddWithValue("$name",table);var sql=await schema.ExecuteScalarAsync() as string;
            if(sql==null){counts[table]=0;continue;}
            using(var create=destination.CreateCommand()){create.CommandText=sql.Replace("CREATE TABLE ","CREATE TABLE IF NOT EXISTS ");await create.ExecuteNonQueryAsync();}
            using var query=readonlyDb.CreateCommand();query.CommandText="SELECT * FROM "+table;using var rows=await query.ExecuteReaderAsync();
            using var tx=destination.BeginTransaction();int count=0;
            while(await rows.ReadAsync())
            {
                using var insert=destination.CreateCommand();insert.Transaction=tx;insert.CommandText="INSERT OR REPLACE INTO "+table+" VALUES("+string.Join(",",Enumerable.Range(0,rows.FieldCount).Select(i=>"$p"+i))+")";
                for(int i=0;i<rows.FieldCount;i++)insert.Parameters.AddWithValue("$p"+i,rows.GetValue(i));await insert.ExecuteNonQueryAsync();count++;
            }
            await tx.CommitAsync();counts[table]=count;
        }
        return counts;
    }
    static async Task<Dictionary<string,long>> ReadTotals(SqliteConnection c)
    {
        var result=new Dictionary<string,long>();using(var games=c.CreateCommand()){games.CommandText="SELECT COUNT(*) FROM Games";result["G"]=Convert.ToInt64(await games.ExecuteScalarAsync());}
        var columns=new[]{"PA","AB","H","HR","BB","HBP","IBB","SO","Runs","RBI","GDP","SH","SF","Doubles","Triples","TB"};using var cmd=c.CreateCommand();cmd.CommandText="SELECT "+string.Join(",",columns.Select(x=>"COALESCE(SUM("+x+"),0)"))+" FROM BatterGameStats";
        using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();for(int i=0;i<columns.Length;i++)result[columns[i]]=r.GetInt64(i);return result;
    }
    static async Task<List<Dictionary<string,object>>> ReadTeams(SqliteConnection c)
    {
        var result=new List<Dictionary<string,object>>();using var cmd=c.CreateCommand();cmd.CommandText="SELECT TeamCode,SUM(PA) PA,SUM(AB) AB,SUM(H) H,SUM(HR) HR,SUM(BB) BB,SUM(SO) SO,SUM(Runs) R,SUM(RBI) RBI,SUM(GDP) GDP,SUM(SH) SH,SUM(SF) SF FROM BatterGameStats GROUP BY TeamCode ORDER BY TeamCode";using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync()){var row=new Dictionary<string,object>();for(int i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.GetValue(i);result.Add(row);}return result;
    }
    static async Task<List<object>> ReadWarnings(SqliteConnection c)
    {
        var result=new List<object>();using var cmd=c.CreateCommand();cmd.CommandText="SELECT GameId,Code,Message FROM Diagnostics WHERE Severity>0 AND Code LIKE 'KBO_%' ORDER BY GameId,Code";using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())result.Add(new{GameId=r.GetString(0),Code=r.GetString(1),Message=r.GetString(2)});return result;
    }
}
