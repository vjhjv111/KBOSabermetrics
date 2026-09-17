using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

internal static class CombinedImportTests
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static int Number(JsonNode row, string key) => int.Parse(row[key]!.GetValue<string>(), CultureInfo.InvariantCulture);
    static int Outs(string value)
    {
        var parts = value.Split('.');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 3 + (parts.Length == 1 ? 0 : int.Parse(parts[1], CultureInfo.InvariantCulture));
    }
    static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    static InputDocument Input(string path, string id) => new() { Id = id, Kind = InputDocumentKind.JsonFile, ContainerPath = path, Length = new FileInfo(path).Length };
    static async Task<long> Scalar(SqliteConnection c, string sql, string? id = null, string? code = null)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        if (id != null) cmd.Parameters.AddWithValue("$id", id);
        if (code != null) cmd.Parameters.AddWithValue("$code", code);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
    static async Task Execute(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    static async Task<string> Snapshot(SqliteConnection c)
    {
        var result = new List<string>();
        foreach (var table in new[] { "Games", "GameMetadata", "ParsedSources", "OfficialPlayLogs", "OfficialBoxScorePlayers", "BatterGameStats", "PitcherGameStats", "Pitches", "AdministrativeEvents" })
        {
            using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT * FROM " + table + " ORDER BY 1,2";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) { var row = new object[r.FieldCount]; r.GetValues(row); result.Add(table + ":" + Serialize(row)); }
        }
        return Serialize(result);
    }
    static (int Hbp, int Ibb, int Doubles, int Triples, int Sh, int Sf) ExtraStats(NormalizedGame game, string code)
    {
        var pa = game.PlateAppearances.Where(x => x.IsOfficialPlateAppearance && x.BatterPcode == code).ToArray();
        return (pa.Count(x => x.Outcome.ResultType == BattingResultType.HitByPitch), pa.Count(x => x.Outcome.IsIntentionalWalk),
            pa.Count(x => x.Outcome.ResultType == BattingResultType.Double), pa.Count(x => x.Outcome.ResultType == BattingResultType.Triple),
            pa.Count(x => x.Outcome.ResultType == BattingResultType.SacrificeBunt), pa.Count(x => x.Outcome.ResultType == BattingResultType.SacrificeFly));
    }

    public static async Task Run(string fixtureFolder, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var runFolder = Path.Combine(outputFolder, "combined-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(runFolder);
        var store = new DatabaseCacheService(Path.Combine(runFolder, "combined.db")); await store.InitializeAsync();
        using var c = new SqliteConnection("Data Source=" + Path.Combine(runFolder, "combined.db")); await c.OpenAsync();
        var files = new[] { "20260911NCHH02026.json", "20260911SKHT02026.json", "20260911WOSS02026.json", "20260911KTLT02026.json" };
        int checkedPlayers = 0, tracked = 0;
        JsonNode? first = null; string? firstPath = null;
        foreach (var file in files)
        {
            var path = Path.Combine(fixtureFolder, file); var json = File.ReadAllText(path); var root = JsonNode.Parse(json)!;
            var naver = RelayParser.ParseJson(root["naver"]!.ToJsonString());
            var initialPitches = Serialize(naver.PitchEvents);
            var initialMetadata = Serialize(new { naver.Winner, naver.StatusCode, naver.AdministrativeEvents, naver.AwayTeam, naver.HomeTeam });
            var official = KboPlayLog.Parse(root["kboOfficial"]!.ToJsonString());
            KboPlayLog.Apply(naver, official);
            var game = RelayParser.ParseJson(json);
            Check(game.GameId == root["naverGameId"]!.GetValue<string>() && game.PlateAppearances.Count > 0, "통합 루트 경기 식별 " + file);
            await store.SaveGameAndSourceAsync(game, Input(path, game.GameId));
            Check(Serialize(game.PitchEvents) == initialPitches, "투구 위치·구속·투구 이벤트 원본 보존 " + file);
            Check(Serialize(new { game.Winner, game.StatusCode, game.AdministrativeEvents, game.AwayTeam, game.HomeTeam }) == initialMetadata, "승패·세이브·홀드 원본 메타데이터 보존 " + file);
            Check(await Scalar(c, "SELECT COUNT(*) FROM Pitches WHERE GameId=$id", game.GameId) == game.PitchEvents.Count, "투구 위치 DB 저장 " + file);
            foreach (var side in new[] { "away", "home" })
            {
                var box = root["kboOfficial"]!["boxScore"]![side]!;
                foreach (var role in new[] { "batting", "pitching" })
                {
                    var rows = box[role]!["rows"]!.AsArray(); var ids = box[role]!["rowIdentities"]!.AsArray();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i]!; var code = ids.Single(x => x!["rowNumber"]!.GetValue<int>() == i + 1)!["naverPcode"]!.GetValue<string>();
                        var name = row[role == "batting" ? "타자" : "투수"]!.GetValue<string>();
                        checkedPlayers++;
                        if (role == "batting")
                        {
                            var line = game.BattingLines.Single(x => x.Pcode == code);
                            Check(line.AtBats == Number(row,"타수") && line.RunsBattedIn == Number(row,"타점") && line.Runs == Number(row,"득점"), "공식 타자 최종행 " + name);
                            foreach (var (column, field) in new[] { ("AB","타수"), ("Runs","득점"), ("H","안타"), ("HR","홈런"), ("RBI","타점"), ("BB","볼넷"), ("SO","삼진"), ("SB","도루"), ("GDP","병살") })
                                Check(await Scalar(c, $"SELECT {column} FROM BatterGameStats WHERE GameId=$id AND Pcode=$code", game.GameId, code) == Number(row,field), "공식 타자 DB " + name + " " + column);
                            var extra = ExtraStats(naver, code);
                            foreach (var (column, value) in new[] { ("HBP",extra.Hbp), ("IBB",extra.Ibb), ("Doubles",extra.Doubles), ("Triples",extra.Triples), ("SH",extra.Sh), ("SF",extra.Sf) })
                                Check(await Scalar(c, $"SELECT {column} FROM BatterGameStats WHERE GameId=$id AND Pcode=$code", game.GameId, code) == value, "공식 박스에 없는 중계 통계 보존 " + name + " " + column);
                        }
                        else
                        {
                            var line = game.PitchingLines.Single(x => x.Pcode == code);
                            Check(line.RunsAllowed == Number(row,"실점") && line.EarnedRuns == Number(row,"자책"), "공식 개인 실점·자책점 " + name);
                            foreach (var (column, field) in new[] { ("RunsAllowed","실점"), ("EarnedRuns","자책"), ("TBF","타자"), ("FinalPitchCount","투구"), ("HitsAllowed","안타"), ("HomeRunsAllowed","홈런"), ("FinalBB","볼넷"), ("FinalSO","삼진") })
                                Check(await Scalar(c, $"SELECT {column} FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code", game.GameId, code) == Number(row,field), "공식 투수 DB " + name + " " + column);
                            Check(await Scalar(c, "SELECT InningsOuts FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code", game.GameId, code) == Outs(row["이닝"]!.GetValue<string>()), "야구 이닝 아웃 수 변환 " + name);
                        }
                    }
                }
            }
            tracked += game.PitchEvents.Count(x => x.HasPtsTracking);
            var repeat = RelayParser.ParseJson(json); await store.SaveGameAndSourceAsync(repeat, Input(path, repeat.GameId));
            Check(await Scalar(c, "SELECT COUNT(*) FROM Pitches WHERE GameId=$id", game.GameId) == game.PitchEvents.Count, "통합 재수집 중복 방지 " + file);
            first ??= root; firstPath ??= path;
        }
        Check(checkedPlayers == 143 && await Scalar(c, "SELECT COUNT(*) FROM Games") == 4, "실제 4경기 / 공식 선수 143행 통합 검증");
        Check(tracked == 1218, "실제 투구 위치 1,218개 보존");
        await InvalidInputTests(store, c, first!, firstPath!);
        await FreshnessAndTransactionTests(store, c, first!, runFolder);
        await SourceSnapshotTests(store,c,first!,runFolder);
        await MissingColumnsFixture(store,c,runFolder);
        await CorrectionTimestampFixture(store,c,runFolder);
        await AtomicDayImportFixture(fixtureFolder,runFolder);
        Console.WriteLine("통합 JSON 검증 DB: " + Path.Combine(runFolder, "combined.db"));
    }

    static async Task AtomicDayImportFixture(string fixtureFolder, string runFolder)
    {
        var dbPath = Path.Combine(runFolder, "atomic-day.db");
        var store = new DatabaseCacheService(dbPath);
        await store.InitializeAsync();
        var files = new[] { "20260911NCHH02026.json", "20260911SKHT02026.json" };
        var inputs = files.Select(file =>
        {
            var path = Path.Combine(fixtureFolder,file);
            return (Game: RelayParser.ParseJson(File.ReadAllText(path)), Document: Input(path,Path.GetFileNameWithoutExtension(path)));
        }).ToArray();
        await using var setup = new SqliteConnection("Data Source=" + dbPath);
        await setup.OpenAsync();
        await Execute(setup,$"CREATE TRIGGER RejectSecondGame BEFORE INSERT ON Games WHEN NEW.GameId='{inputs[1].Game.GameId.Replace("'","''")}' BEGIN SELECT RAISE(ABORT,'forced atomic rollback'); END;");
        try
        {
            await store.SaveGamesAndSourcesAtomicallyAsync(inputs);
            throw new InvalidDataException("날짜 단위 원자 반영이 강제 DB 오류를 반환하지 않았습니다.");
        }
        catch (SqliteException ex) when (ex.Message.Contains("forced atomic rollback",StringComparison.Ordinal)) { }
        Check(await Scalar(setup,"SELECT COUNT(*) FROM Games") == 0,"날짜 단위 경기 반영 전체 롤백");
        Check(await Scalar(setup,"SELECT COUNT(*) FROM OfficialPlayLogs") == 0,"날짜 단위 공식 기록 전체 롤백");
        Check(await Scalar(setup,"SELECT CAST(MetaValue AS INTEGER) FROM Metadata WHERE MetaKey='DataVersion'") == 0,"실패한 날짜 반영은 DB 버전을 올리지 않음");
    }

    static async Task InvalidInputTests(DatabaseCacheService store, SqliteConnection c, JsonNode sample, string path)
    {
        var mutations = new (string Name, Action<JsonNode> Change)[]
        {
            ("다른 경기 ID", x => x["naverGameId"] = "20260910NCHH02026"),
            ("불완전 통합 수집", x => x["collectionStatus"] = "partial"),
            ("진행 중 상태를 완료로 위조", x => x["naver"]!["result"]!["game"]!["statusCode"] = "PLAY"),
            ("불완전 네이버 이닝", x => x["naverCollection"]!["isComplete"] = false),
            ("누락 공식 중계", x => x["kboOfficial"]!["isComplete"] = false),
            ("미해결 선수 매핑", x => x["kboOfficial"]!["boxScore"]!["away"]!["pitching"]!["rowIdentities"]![0]!["matchStatus"] = "unresolved"),
            ("다른 선수 코드", x => x["kboOfficial"]!["boxScore"]!["away"]!["pitching"]!["rowIdentities"]![0]!["naverPcode"] = "00000"),
            ("복수 후보 선수", x => x["kboOfficial"]!["boxScore"]!["away"]!["pitching"]!["rowIdentities"]![0]!["candidatePcodes"]!.AsArray().Add("00000")),
            ("다른 박스 팀", x => x["kboOfficial"]!["boxScore"]!["away"]!["teamCode"] = "WO"),
            ("최종 점수 불일치", x => x["kboOfficial"]!["boxScore"]!["away"]!["totals"]!["R"] = "99"),
            ("잘못된 이닝 소수", x => x["kboOfficial"]!["boxScore"]!["away"]!["pitching"]!["rows"]![0]!["이닝"] = "5.3"),
        };
        foreach (var (name, mutate) in mutations)
        {
            var before = await Snapshot(c); var root = sample.DeepClone(); mutate(root); bool rejected = false;
            try { var game = RelayParser.ParseJson(root.ToJsonString()); await store.SaveGameAndSourceAsync(game, Input(path,game.GameId)); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
            Check(rejected && before == await Snapshot(c), "잘못된 입력 거부 / 기존 DB 보존: " + name);
        }
    }

    static async Task FreshnessAndTransactionTests(DatabaseCacheService store, SqliteConnection c, JsonNode sample, string folder)
    {
        var newer = sample.DeepClone(); var official = newer["kboOfficial"]!;
        official["downloadedAt"] = "2030-01-01T00:00:00Z"; official["boxScore"]!["downloadedAt"] = "2030-01-01T00:00:00Z";
        var box = official["boxScore"]!["away"]!["pitching"]!; var row = box["rows"]![0]!;
        var code = box["rowIdentities"]![0]!["naverPcode"]!.GetValue<string>();
        var correctedEr = Number(row,"자책") - 1; Check(correctedEr >= 0,"개인 자책 정정 fixture"); row["자책"] = correctedEr.ToString(CultureInfo.InvariantCulture);
        var path = Path.Combine(folder,"newer-combined.json"); await File.WriteAllTextAsync(path,newer.ToJsonString());
        var game = RelayParser.ParseJson(newer.ToJsonString()); await store.SaveGameAndSourceAsync(game,Input(path,game.GameId));
        Check(await Scalar(c,"SELECT EarnedRuns FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,code)==correctedEr,"새 공식 개인 자책점 반영");
        var legacyPath=Path.Combine(folder,"legacy-naver.json"); await File.WriteAllTextAsync(legacyPath,sample["naver"]!.ToJsonString());
        var legacy=RelayParser.ParseFile(legacyPath); await store.SaveGameAndSourceAsync(legacy,Input(legacyPath,legacy.GameId));
        Check(await Scalar(c,"SELECT EarnedRuns FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,code)==correctedEr,"네이버 재수집 때 최신 공식 박스 유지");
        var oldPath=Path.Combine(folder,"older-combined.json"); await File.WriteAllTextAsync(oldPath,sample.ToJsonString());
        var old=RelayParser.ParseFile(oldPath); await store.SaveGameAndSourceAsync(old,Input(oldPath,old.GameId));
        Check(await Scalar(c,"SELECT EarnedRuns FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,code)==correctedEr,"과거 통합 파일로 최신 정정 퇴행 방지");
        await store.ImportKboPlayLogAsync(sample["kboOfficial"]!.ToJsonString());
        Check(await Scalar(c,"SELECT EarnedRuns FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,code)==correctedEr,"과거 별도 공식 파일로 최신 박스 퇴행 방지");
        var batting=newer["kboOfficial"]!["boxScore"]!["away"]!["batting"]!;
        var batterCode=batting["rowIdentities"]![0]!["naverPcode"]!.GetValue<string>();
        var batter=game.BattingLines.Single(x=>x.Pcode==batterCode);
        var officialRbi=batter.RunsBattedIn!.Value;
        var daily=new OfficialDailyBatting(game.GameDate!,game.HomeTeam.TeamCode!,batter.PlateAppearances!.Value,batter.AtBats!.Value,batter.Hits!.Value,batter.HomeRuns!.Value,batter.Walks!.Value,batter.HitByPitch!.Value,batter.Strikeouts!.Value,officialRbi+1);
        await store.StoreAndReconcileOfficialRbiAsync(2026,batterCode,[daily],sourceTime:DateTimeOffset.Parse("2029-01-01T00:00:00Z"));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,batterCode)==officialRbi,"오래된 공식 일자별 타점으로 최신 박스 덮어쓰기 방지");
        await store.SaveGameAndSourceAsync(RelayParser.ParseFile(path),Input(path,game.GameId));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,batterCode)==officialRbi,"통합 재수집 때 오래된 캐시 타점 덮어쓰기 방지");
        await store.StoreAndReconcileOfficialRbiAsync(2026,batterCode,[daily],sourceTime:DateTimeOffset.Parse("2031-01-01T00:00:00Z"));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,batterCode)==officialRbi+1,"박스 이후 공식 일자별 타점 정정 반영");
        await store.SaveGameAndSourceAsync(RelayParser.ParseFile(path),Input(path,game.GameId));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",game.GameId,batterCode)==officialRbi+1,"통합 재수집 때 박스 이후 공식 타점 정정 유지");
        var before=await Snapshot(c);
        await Execute(c,"CREATE TRIGGER FailCombinedImport BEFORE INSERT ON PitcherGameStats BEGIN SELECT RAISE(ABORT,'regression injected failure'); END;");
        bool rejected=false;
        try { await store.SaveGameAndSourceAsync(RelayParser.ParseFile(path),Input(path,game.GameId)); } catch(SqliteException){rejected=true;}
        finally {await Execute(c,"DROP TRIGGER FailCombinedImport;");}
        Check(rejected && before==await Snapshot(c),"수집 중간 DB 오류 시 경기/공식 원본/수집 이력 전체 롤백");
    }

    static async Task MissingColumnsFixture(DatabaseCacheService store,SqliteConnection c,string folder)
    {
        const string id="20260910HHSS02026";
        var game=new NormalizedGame{GameId=id,GameDate="2026-09-10",SeasonYear=2026,RoundCode="kbo_r",CompetitionType=GameCompetitionType.RegularSeason,AwayTeam=new(){TeamCode="HH",TeamName="한화",FinalScore=3,FinalHits=3},HomeTeam=new(){TeamCode="SS",TeamName="삼성",FinalScore=3,FinalHits=3}};
        foreach(var (team,batter,pitcher) in new[]{("HH","10001","20002"),("SS","20001","10002")})
        {
            game.BattingLines.Add(new(){GameId=id,TeamCode=team,Pcode=batter,Name=team+"타자",PlateAppearances=7,AtBats=3,Hits=3,HomeRuns=1,Walks=1,HitByPitch=1,Strikeouts=0,Runs=3,RunsBattedIn=3});
            game.PitchingLines.Add(new(){GameId=id,TeamCode=team,Pcode=team=="HH"?"10002":"20002",Name=team+"투수",AppearanceSequence=1,InningsDisplay="9",HitBatters=1,WildPitches=2,RunsAllowed=3,EarnedRuns=2});
            var outcomes=new[]{new BattingOutcome{ResultType=BattingResultType.Double,CountsAsAtBat=true,IsHit=true,TotalBases=2},new BattingOutcome{ResultType=BattingResultType.Triple,CountsAsAtBat=true,IsHit=true,TotalBases=3},new BattingOutcome{ResultType=BattingResultType.HomeRun,CountsAsAtBat=true,IsHit=true,TotalBases=4},new BattingOutcome{ResultType=BattingResultType.HitByPitch},new BattingOutcome{ResultType=BattingResultType.IntentionalWalk,IsWalk=true,IsIntentionalWalk=true},new BattingOutcome{ResultType=BattingResultType.SacrificeBunt,IsSacrifice=true,IsOut=true},new BattingOutcome{ResultType=BattingResultType.SacrificeFly,IsSacrifice=true,IsOut=true}};
            for(int i=0;i<outcomes.Length;i++)game.PlateAppearances.Add(new(){GameId=id,PlateAppearanceId=id+":"+team+":"+i,IsOfficialPlateAppearance=true,Status=PlateAppearanceStatus.Completed,BattingTeamCode=team,FieldingTeamCode=team=="HH"?"SS":"HH",BatterPcode=batter,BatterName=team+"타자",PitcherPcode=pitcher,PitcherName=(team=="HH"?"SS":"HH")+"투수",Outcome=outcomes[i]});
        }
        JsonObject Team(string code)=>new(){["teamCode"]=code,["totals"]=new JsonObject{["R"]="3",["H"]="3"},
            ["batting"]=new JsonObject{["columns"]=new JsonArray("타자","타수","득점","안타","홈런","타점","볼넷","삼진","희타"),["rows"]=new JsonArray(new JsonObject{["타자"]=code+"타자",["타수"]="3",["득점"]="3",["안타"]="3",["홈런"]="1",["타점"]="3",["볼넷"]="1",["삼진"]="0",["희타"]="2"})},
            ["pitching"]=new JsonObject{["columns"]=new JsonArray("투수","이닝","타자","타수","투구","안타","홈런","볼넷","삼진","실점","자책"),["rows"]=new JsonArray(new JsonObject{["투수"]=code+"투수",["이닝"]="9",["타자"]="7",["타수"]="3",["투구"]="22",["안타"]="3",["홈런"]="1",["볼넷"]="1",["삼진"]="0",["실점"]="3",["자책"]="1"})}};
        var box=new JsonObject{["away"]=Team("HH"),["home"]=Team("SS"),["downloadedAt"]="2026-09-11T00:00:00Z"};
        using var doc=JsonDocument.Parse(box.ToJsonString());KboBoxScore.Apply(game,KboBoxScore.Parse(doc.RootElement,id[..13]),id[..13]);
        var path=Path.Combine(folder,"missing-columns.json");await File.WriteAllTextAsync(path,"{}");await store.SaveGameAndSourceAsync(game,Input(path,id));
        foreach(var column in new[]{"Doubles","Triples","HBP","IBB","SH","SF"})Check(await Scalar(c,$"SELECT {column} FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",id,"10001")==1,"결측 박스 열을 0으로 만들지 않음: "+column);
        Check(await Scalar(c,"SELECT TB FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",id,"10001")==9,"2루타·3루타 보존 후 루타수 계산");
        Check(await Scalar(c,"SELECT FinalHBP FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",id,"10002")==1 && await Scalar(c,"SELECT WildPitches FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",id,"10002")==2,"박스에 없는 투수 사구·폭투 보존");
        Check(await Scalar(c,"SELECT EarnedRuns FROM PitcherGameStats WHERE GameId=$id AND Pcode=$code",id,"10002")==1,"기존 개인 자책점 2에서 공식 박스 1로 수정");
        foreach(var side in new[]{"away","home"})
        {
            var batting=box[side]!["batting"]!;batting["rows"]![0]!.AsObject().Remove("타점");
            var rbiColumn=batting["columns"]!.AsArray().Single(x=>x!.GetValue<string>()=="타점");batting["columns"]!.AsArray().Remove(rbiColumn);
            batting["columns"]!.AsArray().Add("타석");batting["rows"]![0]!["타석"]="8";batting["rows"]![0]!["타수"]="4";
            box[side]!["pitching"]!["rows"]![0]!["타자"]="8";box[side]!["pitching"]!["rows"]![0]!["타수"]="4";
        }
        using var noRbi=JsonDocument.Parse(box.ToJsonString());KboBoxScore.Apply(game,KboBoxScore.Parse(noRbi.RootElement,id[..13]),id[..13]);
        await store.SaveGameAndSourceAsync(game,Input(path,id));
        var daily=new OfficialDailyBatting("2026-09-10","SS",8,4,3,1,1,1,0,4);
        await store.StoreAndReconcileOfficialRbiAsync(2026,"10001",[daily],sourceTime:DateTimeOffset.Parse("2026-09-10T00:00:00Z"));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",id,"10001")==4,"박스 타점 열 결측일 때 이전 공식 일자별 타점 사용");
        game.BattingLines.Single(x=>x.Pcode=="10001").RunsBattedIn=3;
        await store.SaveGameAndSourceAsync(game,Input(path,id));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",id,"10001")==4,"일자별 타점 대조는 타석 합계 대신 공식 최종 AB/PA와 일치 검사");
    }

    static async Task SourceSnapshotTests(DatabaseCacheService store,SqliteConnection c,JsonNode sample,string folder)
    {
        var a=sample.DeepClone();var b=sample.DeepClone();
        a["naver"]!["result"]!["game"]!["winPitcherName"]="가나다";
        b["naver"]!["result"]!["game"]!["winPitcherName"]="라마바";
        var path=Path.Combine(folder,"source-snapshot.json");await File.WriteAllTextAsync(path,a.ToJsonString());
        var timestamp=File.GetLastWriteTimeUtc(path);var size=new FileInfo(path).Length;
        var game=RelayParser.ParseFile(path);await File.WriteAllTextAsync(path,b.ToJsonString());File.SetLastWriteTimeUtc(path,timestamp);
        Check(new FileInfo(path).Length==size,"동일 파일 크기 변경 fixture");
        var input=Input(path,game.GameId);await store.SaveGameAndSourceAsync(game,input);
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT WinPitcher FROM GameMetadata WHERE GameId=$id";cmd.Parameters.AddWithValue("$id",game.GameId);Check((string?)await cmd.ExecuteScalarAsync()=="가나다","파싱 이후 원본 파일 변경 시 파싱한 원본의 승패 메타데이터 저장");}
        Check((await store.GetUnchangedSourceKeysAsync([input])).Count==0,"파싱 이후 변경된 파일을 수집 완료로 오인하지 않음");
        await File.WriteAllTextAsync(path,a.ToJsonString());File.SetLastWriteTimeUtc(path,timestamp);
        Check((await store.GetUnchangedSourceKeysAsync([input])).Contains(game.GameId),"파싱한 원문 해시가 저장된 수집 이력과 일치");
        await File.WriteAllTextAsync(path,b.ToJsonString());File.SetLastWriteTimeUtc(path,timestamp);
        Check((await store.GetUnchangedSourceKeysAsync([input])).Count==0,"크기·수정시각이 같아도 내용이 바뀌면 재수집");
    }

    static async Task CorrectionTimestampFixture(DatabaseCacheService store,SqliteConnection c,string folder)
    {
        const string id="20260908HHSS02026";const string code="30001";
        var game=new NormalizedGame{GameId=id,GameDate="2026-09-08",SeasonYear=2026,RoundCode="kbo_r",CompetitionType=GameCompetitionType.RegularSeason,AwayTeam=new(){TeamCode="HH",TeamName="한화",FinalScore=2},HomeTeam=new(){TeamCode="SS",TeamName="삼성",FinalScore=0}};
        game.PlateAppearances.Add(new(){GameId=id,PlateAppearanceId=id+":1",Inning=1,BatOrder=1,IsOfficialPlateAppearance=true,BattingTeamCode="HH",BatterPcode=code,BatterName="정정타자",Outcome=new(){ResultType=BattingResultType.ReachedOnError,CountsAsAtBat=true,ReachedBase=true}});
        game.BattingLines.Add(new(){GameId=id,Pcode=code,TeamCode="HH",Name="정정타자",PlateAppearances=1,AtBats=1,Hits=0,HomeRuns=0,Walks=0,HitByPitch=0,Strikeouts=0,RunsBattedIn=1,OfficialStats=new(){SourceTime=DateTimeOffset.Parse("2026-09-08T12:00:00Z"),AtBats=1,Hits=0,HomeRuns=0,Walks=0,HitByPitch=0,Strikeouts=0,RunsBattedIn=1}});
        var path=Path.Combine(folder,"correction-timestamp.json");await File.WriteAllTextAsync(path,"{}");await store.SaveGameAndSourceAsync(game,Input(path,id));
        var daily=new OfficialDailyBatting("2026-09-08","SS",1,1,0,0,0,0,0,1);
        await store.StoreAndReconcileOfficialRbiAsync(2026,code,[daily],sourceTime:DateTimeOffset.Parse("2026-09-09T00:00:00Z"));
        var notice=new KboCorrection("2026:0:9999",2026,"2026-09-08","한화:삼성","1초","1","정정타자","안타","실책","한화\n정정타자(타점 1→2)","09/10");
        using(var cmd=c.CreateCommand()){cmd.CommandText="CREATE TABLE IF NOT EXISTS OfficialCorrections(Id TEXT PRIMARY KEY,Year INTEGER NOT NULL,Json TEXT NOT NULL,CheckedUtc TEXT NOT NULL); INSERT INTO OfficialCorrections VALUES($id,2026,$json,'2026-09-10T12:00:00Z')";cmd.Parameters.AddWithValue("$id",notice.Id);cmd.Parameters.AddWithValue("$json",Serialize(notice));await cmd.ExecuteNonQueryAsync();}
        await store.SaveGameAndSourceAsync(game,Input(path,id));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",id,code)==2,"최신 정정 공지의 타점을 이전 일자별 캐시가 되돌리지 않음");
        await store.StoreAndReconcileOfficialRbiAsync(2026,code,[daily],sourceTime:DateTimeOffset.Parse("2026-09-09T01:00:00Z"));
        Check(await Scalar(c,"SELECT RBI FROM BatterGameStats WHERE GameId=$id AND Pcode=$code",id,code)==2,"정정 공지 이후 오래된 일자별 자료의 직접 적용 차단");
    }
}
