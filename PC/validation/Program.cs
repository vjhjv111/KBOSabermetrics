using System.Reflection;
using NaverRelay.Parsing;
using NaverRelay.Infrastructure.Sqlite;

if (args.Length == 2 && args[0] == "--woba-preview")
{
    await WobaRe24Preview.Run(args[1]);
    return;
}

if (args.Length == 4 && args[0] == "--automation")
{
    await AutomationTests.Run(args[1], args[2], args[3]);
    return;
}

if (args.Length == 3 && args[0] == "--cache-preflight")
{
    await CachePreflightTests.Run(args[1], args[2]);
    return;
}
if (args.Length == 4 && args[0] == "--cache-benchmark")
{
    await CachePreflightTests.Benchmark(args[1], args[2], args[3]);
    return;
}

if (args.Length == 4 && args[0] == "--sacrifice-correction")
{
    await BulkCombinedImportTests.VerifySacrificeCorrection(args[1],args[2],args[3]);
    return;
}

if (args.Length == 2 && args[0] == "--season-pitchers")
{
    await BulkCombinedImportTests.VerifyPitchers(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "--identity-fixtures")
{
    IdentityRecoveryTests.Run(args[1]);
    return;
}

if (args.Length == 4 && args[0] == "--combined-season")
{
    await BulkCombinedImportTests.Run(args[1], args[2], args[3]);
    return;
}

if (args.Length == 2 && args[0] == "--team-er")
{
    await TeamErTests.Run(args[1]);
    return;
}

if (args.Length > 0 && args[0] == "--combined")
{
    if (args.Length != 3) throw new ArgumentException("--combined <통합 JSON 폴더> <검증 결과 폴더>");
    await CombinedImportTests.Run(args[1], args[2]);
    await TeamErTests.Run(args[2]);
    return;
}

void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
var classify=typeof(RelayParser).Assembly.GetType("NaverRelay.Parsing.BatterResultClassifier")!.GetMethod("Classify",BindingFlags.Static|BindingFlags.Public)!;
BattingOutcome Outcome(string text)=>(BattingOutcome)classify.Invoke(null,[text])!;
foreach(var text in new[]{"스트라이크 낫아웃 폭투","스트라이크 낫아웃 포일","포수 스트라이크 낫아웃 다른주자 수비로 출루","포수 스트라이크 낫아웃 실책으로 출루"}){
    var o=Outcome("선수 : "+text);Check(o.IsStrikeout&&o.CountsAsAtBat&&!o.IsOut&&o.ReachedBase&&!o.IsHit,text);
}

if (args.Length > 1)
{
    foreach (var (id, name, hits, so) in new[] { ("20260415WOHT", "이주형", 2, 0), ("20260429WOLT", "오선진", 1, 0), ("20260826OBKT", "최원준", 2, 1), ("20260627LGLT", "신민재", 1, 0) })
    {
        var g = RelayParser.ParseFile(Path.Combine(args[0], id + "02026.json"));
        var pitches = System.Text.Json.JsonSerializer.Serialize(g.PitchEvents);
        var doc = KboPlayLog.Parse(File.ReadAllText(Path.Combine(args[1], id + "0_playlog.json")));
        KboPlayLog.Apply(g, doc);
        var b = g.BattingLines.Single(b => b.Name == name);
        Check(b.Hits == hits && b.Strikeouts == so, "KBO 박스스코어 대조 " + name);
        Check(pitches == System.Text.Json.JsonSerializer.Serialize(g.PitchEvents), "투구 원본 보존 " + name);
        var count = g.PlateAppearances.Count;
        KboPlayLog.Apply(g, doc);
        Check(g.PlateAppearances.Count == count && b.Hits == hits, "KBO 재실행 멱등성 " + name);
        if (name == "이주형") Check(b.AtBats == 5, "누락 첫 타석 복원");
        if (name == "오선진") Check(b.AtBats == 4, "희생번트 타수 제외");
        if (args.Length > 2)
        {
            var store = new DatabaseCacheService(args[2]); await store.InitializeAsync();
            var path = Path.Combine(args[0], id + "02026.json");
            var input = new NaverRelay.Application.Importing.InputDocument { Id = id, Kind = NaverRelay.Application.Importing.InputDocumentKind.JsonFile, ContainerPath = path, Length = new FileInfo(path).Length };
            await store.SaveGameAndSourceAsync(RelayParser.ParseFile(path), input);
            var imported = await store.ImportKboPlayLogAsync(doc.Json);
            Check(imported.BattingLines.Single(b => b.Name == name).Hits == hits, "DB 공식 추가 수집 " + name);
            var again = RelayParser.ParseFile(path); await store.SaveGameAndSourceAsync(again, input);
            Check(again.BattingLines.Single(b => b.Name == name).Hits == hits && again.BattingLines.Single(b => b.Name == name).Strikeouts == so, "네이버 재수집 시 공식 보정 유지 " + name);
        }
    }
    var sample = File.ReadAllText(Path.Combine(args[1], "20260415WOHT0_playlog.json"));
    bool rejected = false;
    try { KboPlayLog.Parse(sample.Replace("경기종료", "진행중")); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "불완전 공식 파일 거부");
}
foreach(var text in new[]{"포수 스트라이크 낫 아웃 (포수 태그아웃)","포수 쓰리번트 아웃","삼진 아웃"}){var o=Outcome("선수 : "+text);Check(o.IsStrikeout&&o.IsOut&&!o.ReachedBase,text);}
Check(!Outcome("선수 : 3루수 희생번트 아웃").IsStrikeout,"희생번트는 삼진 아님");
foreach (var text in new[] { "유격수 야수선택으로 출루 (유격수->2루수)", "3루수 병살타로 출루" })
    Check(Outcome("선수 : "+text) is { CountsAsAtBat:true, ReachedBase:true, IsOut:false },"출루 타수 판정 "+text);
Check(Outcome("선수 : 중견수 희생플라이 (중견수 실책)") is { CountsAsAtBat:false,IsSacrifice:true,ReachedBase:true },"실책 동반 희생플라이");
Check(Outcome("선수 : 투수 병살 실책으로 출루 (1루수->유격수->투수 실책)") is { ResultType:BattingResultType.GroundedIntoDoublePlay,IsOut:false },"병살 실책 타자 생존 유지");
Check(Outcome("선수 : 3루수 삼중살 아웃 (3루수->2루수->1루수 송구아웃)").ResultType==BattingResultType.GroundedIntoDoublePlay,"땅볼 삼중살 GDP 집계");
Check(Outcome("선수 : 유격수 라인드라이브 아웃").ResultType!=BattingResultType.GroundedIntoDoublePlay,"직선타는 GDP로 추정하지 않음");

var game=new NormalizedGame{GameId="20260601HHSS02026",GameDate="2026-06-01",RoundCode="kbo_r",AwayTeam=new(){TeamCode="HH"},HomeTeam=new(){TeamCode="SS"}};
game.PlateAppearances.Add(new PlateAppearance{Inning=2,BattingTeamCode="SS",BatOrder=3,BatterName="테스트",Outcome=Outcome("테스트 : 유격수 실책으로 출루")});
game.BattingLines.Add(new GamePlayerBattingLine{TeamCode="SS",Name="테스트",Hits=1});
var notice=new KboCorrection("2026:0:1",2026,"2026-06-01","한화:삼성","2말","3","테스트\n(투수)","실책","안타","삼성\n테스트(안타 1→2, 루타수 1→2)","06/02");
DatabaseCacheService.ApplyCorrection(game,notice);Check(game.PlateAppearances[0].Outcome.IsHit&&game.BattingLines[0].Hits==2,"공식 전후값 적용");
DatabaseCacheService.ApplyCorrection(game,notice);Check(game.BattingLines[0].Hits==2&&game.PlateAppearances[0].Outcome.TotalBases==1,"정정 재실행 멱등성");
game.BattingLines[0].Hits=5;DatabaseCacheService.ApplyCorrection(game,notice);Check(game.BattingLines[0].Hits==5&&game.Diagnostics.Any(d=>d.Code=="KBO_CORRECTION_PENDING"),"예상과 다른 수치 보존 및 검토 표시");
game.PlateAppearances.Add(game.PlateAppearances[0]);game.Diagnostics.Clear();DatabaseCacheService.ApplyCorrection(game,notice);Check(game.Diagnostics.Any(d=>d.Code=="KBO_CORRECTION_PENDING"),"타석 매칭 모호성 차단");

if(args.Length>0){
    foreach(var (id,expected) in new[]{("20260513NCLT02026",82),("20260612SKSS02026",0)}){
        var g=RelayParser.ParseFile(Path.Combine(args[0],id+".json"));
        if(expected>0)Check(g.PlateAppearances.Count(p=>p.IsOfficialPlateAppearance)==expected,"반복 중계 구간 제거");
        else Check(g.PlateAppearances.Count(p=>p.BatterName=="박계범"&&p.Outcome.IsStrikeout)==1,"동일 투구 ID 타석 중복 제거");
    }
    foreach(var (id,name) in new[]{("20260418SKNC02026","고명준"),("20260617HHNC02026","천재환"),("20260902HHKT02026","장준원"),("20260908OBHH02026","이도윤")}){
        var g=RelayParser.ParseFile(Path.Combine(args[0],id+".json"));Check(g.Diagnostics.Any(d=>d.Code=="TWO_STRIKE_SUBSTITUTION"&&d.Message.Contains(name)),"2스트라이크 교체 귀속 "+id);
    }
}
if(args.Length > 3) await OfficialBattingTests.Run(args[0],args[3],Path.Combine(args[3],"official-rbi-regression.db"));
