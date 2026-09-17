using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Importing;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;
using NaverRelayUI.Collection;
using NaverRelayUI.Models;
using KboRelayDownloader;

namespace NaverSabermetrics.Web;

public sealed class RenderCollectorOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public int LookbackDays { get; set; } = 1;
    public string JsonDirectory { get; set; } = "/var/data/kbo-json";
    public string HeartbeatPath { get; set; } = "/var/data/kbo-render-collector.heartbeat";
}

public static class RenderCollectionPolicy
{
    public static bool IsFinalStatus(string? status) =>
        string.Equals(status?.Trim(), "RESULT", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), "ENDED", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ScheduleGame> EligibleGames(IEnumerable<ScheduleGame> games, string dateKey) =>
        games.Where(game => game.CategoryId == "kbo" && !game.Cancel && game.StatusCode != "CANCEL" &&
            GameDateKey(game) == dateKey).ToArray();

    public static bool AllGamesFinal(IEnumerable<ScheduleGame> games) =>
        games.Any() && games.All(game => IsFinalStatus(game.StatusCode));

    public static string DatabaseGameId(GameRequest game) => game.GameId + game.Year;

    public static string? GameDateKey(ScheduleGame game)
    {
        if (game.GameId is { Length: >= 8 } id && id.Take(8).All(char.IsDigit)) return id[..8];
        return DateTime.TryParse(game.GameDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : null;
    }
}

/// <summary>
/// Render의 웹 서비스와 같은 프로세스에서 실행됩니다. 별도 Cron/Worker는 웹 서비스의
/// 영구 디스크를 공유할 수 없으므로, 이 작업만 쓰기 연결을 열고 웹 요청은 읽기 전용을 유지합니다.
/// </summary>
public sealed class RenderCollectorWorker : BackgroundService
{
    private readonly ILogger<RenderCollectorWorker> logger;
    private readonly SiteOptions site;
    private readonly RenderCollectorOptions options;
    private readonly TimeZoneInfo korea;
    private readonly HttpClient http;
    private DatabaseCacheService? writer;

    public RenderCollectorWorker(ILogger<RenderCollectorWorker> logger, SiteOptions site, RenderCollectorOptions options)
    {
        this.logger = logger;
        this.site = site;
        this.options = options;
        korea = FindKoreaTimeZone();
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; KBOSabermetrics-RenderCollector/1.0)");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        if (options.IntervalMinutes is < 1 or > 60 || options.LookbackDays is < 0 or > 7)
            throw new InvalidOperationException("RenderCollector 주기 또는 조회 일수 설정이 올바르지 않습니다.");
        if (string.IsNullOrWhiteSpace(site.DatabasePath) || !Path.IsPathFullyQualified(site.DatabasePath))
            throw new InvalidOperationException("RenderCollector에는 절대 DB 경로가 필요합니다.");

        options.JsonDirectory = Path.GetFullPath(options.JsonDirectory);
        options.HeartbeatPath = Path.GetFullPath(options.HeartbeatPath);
        Directory.CreateDirectory(options.JsonDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(options.HeartbeatPath)!);
        logger.LogInformation("Render 수집기 시작: interval={Interval}m json={Json} db={Database}",
            options.IntervalMinutes, options.JsonDirectory, site.DatabasePath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
                await TouchHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Render 수집 주기 실패; 다음 주기에 다시 시도합니다."); }

            try { await Task.Delay(TimeSpan.FromMinutes(options.IntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken token)
    {
        if (!File.Exists(site.DatabasePath))
        {
            logger.LogWarning("Render 수집 DB가 아직 없습니다: {Database}", site.DatabasePath);
            return;
        }

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, korea);
        var from = now.Date.AddDays(-options.LookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        void Log(string message) => logger.LogInformation("[collector] {Message}", message);

        var games = await GameIdCollector.CollectAllKboGamesAsync(http, from, to, delayMs: 300, log: Log, ct: token);
        if (games.Count == 0)
        {
            logger.LogInformation("Render 수집기: {From}~{To} KBO 일정 없음", from, to);
            return;
        }

        var summary = await RelayCollector.CollectAllAsync(http, games, options.JsonDirectory,
            delayMs: 500, log: Log, ct: token);
        logger.LogInformation("Render JSON 수집: 완료={Complete} 부분={Partial} 기존/취소={Skipped} 제외={Excluded} 실패={Failed}",
            summary.Complete, summary.Partial, summary.Skipped, summary.Excluded, summary.Failed);

        var excluded = summary.ExcludedGameIds.ToHashSet(StringComparer.Ordinal);
        foreach (var dateKey in games.Select(RenderCollectionPolicy.GameDateKey).Where(x => x is not null)
                     .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var dayGames = RenderCollectionPolicy.EligibleGames(games, dateKey!);
            if (dayGames.Count == 0) continue;
            if (!RenderCollectionPolicy.AllGamesFinal(dayGames))
            {
                var waiting = dayGames.Where(x => !RenderCollectionPolicy.IsFinalStatus(x.StatusCode))
                    .Select(x => $"{x.GameId}:{x.StatusCode ?? "?"}");
                logger.LogInformation("{Date} DB 반영 대기: 종료 전 경기 {Games}", dateKey, string.Join(", ", waiting));
                continue;
            }

            var expected = dayGames.Select(game => GameRequest.Parse(game.GameId!))
                .Select(game => (Game: game, NaverId: RenderCollectionPolicy.DatabaseGameId(game)))
                .Where(x => !excluded.Contains(x.NaverId)).ToArray();
            if (expected.Length == 0) continue;

            var documents = new List<InputDocument>(expected.Length);
            var incomplete = new List<string>();
            foreach (var item in expected)
            {
                var path = Path.Combine(options.JsonDirectory, item.NaverId + ".json");
                if (!File.Exists(path)) { incomplete.Add(item.NaverId + ":파일없음"); continue; }
                try
                {
                    var json = await File.ReadAllTextAsync(path, token);
                    if (!UnifiedDocumentStore.ReadCache(json, item.Game, item.NaverId).IsComplete)
                    { incomplete.Add(item.NaverId + ":partial"); continue; }
                    var info = new FileInfo(path);
                    documents.Add(new InputDocument { Id=item.NaverId, Kind=InputDocumentKind.JsonFile,
                        ContainerPath=info.FullName, Length=info.Length });
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                { incomplete.Add(item.NaverId + ":" + ex.Message); }
            }
            if (incomplete.Count > 0 || documents.Count != expected.Length)
            {
                logger.LogInformation("{Date} DB 반영 대기: 공식 종료 JSON 미완성 {Games}", dateKey, string.Join(" | ", incomplete));
                continue;
            }

            await PublishDayAsync(dateKey!, documents, expected.Select(x => x.NaverId).ToArray(), token);
        }
    }

    private async Task PublishDayAsync(string dateKey, IReadOnlyList<InputDocument> documents,
        IReadOnlyList<string> expectedGameIds, CancellationToken token)
    {
        writer ??= new DatabaseCacheService(site.DatabasePath);
        await writer.InitializeAsync(token);
        var unchanged = await writer.GetUnchangedSourceKeysAsync(documents, token);
        var changed = documents.Where(x => !unchanged.Contains(x.Id)).ToArray();
        if (changed.Length == 0)
        {
            await VerifyPublishedGamesAsync(expectedGameIds, token);
            logger.LogInformation("{Date} DB는 이미 최신입니다.", dateKey);
            return;
        }

        var inputs = new List<(NormalizedGame Game, InputDocument Document)>(changed.Length);
        foreach (var document in changed)
        {
            var json = await document.ReadJsonAsync(token);
            var game = RelayParser.ParseJson(json);
            if (!RenderCollectionPolicy.IsFinalStatus(game.StatusCode))
                throw new InvalidDataException($"종료 상태가 아닌 경기를 DB에 반영할 수 없습니다: {game.GameId}/{game.StatusCode}");
            inputs.Add((game, document));
        }

        await writer.SaveGamesAndSourcesAtomicallyAsync(inputs, token);
        await VerifyPublishedGamesAsync(expectedGameIds, token);
        logger.LogInformation("{Date} RESULT/ENDED 경기 {Count}건을 실사용 DB에 한 트랜잭션으로 반영했습니다. Render 재시작은 필요하지 않습니다.",
            dateKey, inputs.Count);
    }

    private async Task VerifyPublishedGamesAsync(IReadOnlyList<string> gameIds, CancellationToken token)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource=site.DatabasePath, Mode=SqliteOpenMode.ReadOnly, Pooling=false, DefaultTimeout=10 }.ToString());
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        var parameters = new List<string>(gameIds.Count);
        for (var i = 0; i < gameIds.Count; i++)
        {
            var name = "$id" + i; parameters.Add(name); command.Parameters.AddWithValue(name, gameIds[i]);
        }
        command.CommandText = $"SELECT COUNT(*) FROM Games WHERE GameId IN ({string.Join(',', parameters)}) AND UPPER(StatusCode) IN ('RESULT','ENDED')";
        var found = Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        if (found != gameIds.Count) throw new InvalidDataException($"DB 반영 확인 실패: expected={gameIds.Count}, found={found}");
    }

    private async Task TouchHeartbeatAsync(CancellationToken token)
    {
        var temp = options.HeartbeatPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), new UTF8Encoding(false), token);
            File.Move(temp, options.HeartbeatPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static TimeZoneInfo FindKoreaTimeZone()
    {
        foreach (var id in new[] { "Asia/Seoul", "Korea Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new TimeZoneNotFoundException("한국 시간대를 찾을 수 없습니다.");
    }

    public override void Dispose()
    {
        http.Dispose();
        base.Dispose();
    }
}
