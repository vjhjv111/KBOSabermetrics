using Microsoft.Data.Sqlite;
using KboRelayDownloader;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;
using NaverRelayUI.Models;
using NaverSabermetrics.Web;

var checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new InvalidDataException(name);
    Console.WriteLine("PASS " + name);
}

var date = "20260917";
var schedule = new[]
{
    new ScheduleGame { GameId=date+"HHKT0", GameDate="2026-09-17", CategoryId="kbo", StatusCode="ENDED" },
    new ScheduleGame { GameId=date+"LGOB0", GameDate="2026-09-17", CategoryId="kbo", StatusCode="RESULT" },
    new ScheduleGame { GameId=date+"LTNC0", GameDate="2026-09-17", CategoryId="kbo", StatusCode="CANCEL", Cancel=true },
    new ScheduleGame { GameId="20260916SSWO0", GameDate="2026-09-16", CategoryId="kbo", StatusCode="PLAY" },
};
var eligible = RenderCollectionPolicy.EligibleGames(schedule,date);
Check(eligible.Count == 2,"취소·다른 날짜 제외");
Check(RenderCollectionPolicy.AllGamesFinal(eligible),"RESULT와 ENDED를 모두 종료로 인정");
Check(RenderCollectionPolicy.DatabaseGameId(GameRequest.Parse(eligible[0].GameId!)) == "20260917HHKT02026",
    "DB 검증은 네이버 저장 경기 ID(연도 접미사 포함) 사용");
var endedCache = new NaverRelayUI.Collection.NaverCollectionResult("{}",true,"kbo_r","ENDED",9,[1,2,3,4,5,6,7,8,9],[]);
Check(NaverRelayUI.Collection.RelayCollector.ShouldRefreshNaverMetadata(endedCache,eligible[1]),
    "ENDED 완료 캐시는 일정이 RESULT가 되면 네이버 최종 승·패 메타데이터 갱신");
Check(!NaverRelayUI.Collection.RelayCollector.ShouldRefreshNaverMetadata(endedCache,eligible[0]),
    "일정도 ENDED인 동안에는 완료 캐시를 반복 수집하지 않음");
eligible[0].StatusCode = "PLAY";
Check(!RenderCollectionPolicy.AllGamesFinal(eligible),"진행 중 경기가 하나라도 있으면 날짜 반영 보류");

var scratch = Path.Combine(Path.GetTempPath(),"render-collector-validation-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var triggerPath=Path.Combine(scratch,"collector.trigger");
    await File.WriteAllTextAsync(triggerPath,string.Empty);
    Check(RenderCollectionPolicy.TryConsumeManualTrigger(triggerPath),"SSH 수동 실행 트리거 감지");
    Check(!File.Exists(triggerPath),"감지한 SSH 트리거 파일 제거");
    Check(!RenderCollectionPolicy.TryConsumeManualTrigger(triggerPath),"처리한 트리거는 중복 실행하지 않음");

    var path = Path.Combine(scratch,"cache.db");
    var writer = new DatabaseCacheService(path);
    await writer.InitializeAsync();
    var reader = new DatabaseCacheService(path,webReadOnly:true);
    await reader.InitializeAsync();
    Check(await reader.TryLoadComputedAsync<string>("sample") is null,"초기 웹 계산 캐시 없음");
    await reader.SaveComputedAsync("sample","old");
    Check(await reader.TryLoadComputedAsync<string>("sample") == "old","동일 DB 버전 메모리 캐시 사용");
    await using (var connection = new SqliteConnection("Data Source="+path))
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Metadata SET MetaValue='1' WHERE MetaKey='DataVersion'";
        await command.ExecuteNonQueryAsync();
    }
    Check(await reader.TryLoadComputedAsync<string>("sample") is null,"DB 버전 변경 시 웹 캐시 즉시 무효화");

    var atomicPath = Path.Combine(scratch,"atomic.db");
    var atomic = new DatabaseCacheService(atomicPath);
    await atomic.InitializeAsync();
    var source1 = Path.Combine(scratch,"game1.json");
    var source2 = Path.Combine(scratch,"game2.json");
    await File.WriteAllTextAsync(source1,"{}");
    await File.WriteAllTextAsync(source2,"{}");
    NormalizedGame Game(string id,string away,string home) => new()
    {
        GameId=id, SeasonYear=2026, GameDate="2026-09-17", StatusCode="ENDED",
        RoundCode="kbo_r", CompetitionType=GameCompetitionType.RegularSeason,
        AwayTeam=new TeamMetadata { TeamCode=away, TeamName=away, FinalScore=1 },
        HomeTeam=new TeamMetadata { TeamCode=home, TeamName=home, FinalScore=2 },
    };
    InputDocument Input(string id,string path) => new()
    {
        Id=id, Kind=InputDocumentKind.JsonFile, ContainerPath=path, Length=new FileInfo(path).Length,
    };
    var batch = new[]
    {
        (Game:Game("20260917HHKT0","HH","KT"),Document:Input("game1",source1)),
        (Game:Game("20260917LGOB0","LG","OB"),Document:Input("game2",source2)),
    };
    await using var atomicConnection = new SqliteConnection("Data Source="+atomicPath);
    await atomicConnection.OpenAsync();
    await using (var trigger = atomicConnection.CreateCommand())
    {
        trigger.CommandText = "CREATE TRIGGER RejectSecondGame BEFORE INSERT ON Games WHEN NEW.GameId='20260917LGOB0' BEGIN SELECT RAISE(ABORT,'forced atomic rollback'); END;";
        await trigger.ExecuteNonQueryAsync();
    }
    try
    {
        await atomic.SaveGamesAndSourcesAtomicallyAsync(batch);
        throw new InvalidDataException("원자 반영 강제 오류가 반환되지 않았습니다.");
    }
    catch (SqliteException ex) when (ex.Message.Contains("forced atomic rollback",StringComparison.Ordinal)) { }
    async Task<long> Scalar(string sql)
    {
        await using var command = atomicConnection.CreateCommand();
        command.CommandText=sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    Check(await Scalar("SELECT COUNT(*) FROM Games") == 0,"날짜 단위 DB 반영 전체 롤백");
    Check(await Scalar("SELECT CAST(MetaValue AS INTEGER) FROM Metadata WHERE MetaKey='DataVersion'") == 0,"실패한 날짜 반영은 DB 버전을 올리지 않음");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(scratch,true);
}

Console.WriteLine($"Render collector validation passed ({checks} checks).");
