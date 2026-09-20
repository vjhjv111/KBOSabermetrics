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
    public string ManualTriggerPath { get; set; } = "/var/data/kbo-render-collector.trigger";
    public bool ReconcileOfficialRecords { get; set; } = true;
    public int ReconciliationRetryMinutes { get; set; } = 10;
    public string ReconciliationStatePath { get; set; } = "/var/data/kbo-render-reconciliation.json";
    public bool ScheduleSyncEnabled { get; set; } = true;
    public int ScheduleSyncIntervalMinutes { get; set; } = 180;

    /// <summary>
    /// 과거 데이터 소급 수집을 켤지 여부입니다. 기본은 꺼짐 — 켜면 매 주기마다 한 번씩
    /// BackfillFromDate부터 시작해 BackfillChunkDays 크기로 과거 날짜를 잘라가며 자동으로
    /// 수집·DB 반영을 진행하고, 최근 수집 구간(오늘-LookbackDays)에 닿으면 자동으로 멈춥니다.
    /// </summary>
    public bool BackfillEnabled { get; set; } = false;
    public string BackfillFromDate { get; set; } = "2008-03-01";
    public int BackfillChunkDays { get; set; } = 30;
    public string BackfillStatePath { get; set; } = "/var/data/kbo-render-backfill.json";
}

public sealed record RenderReconciliationState(int Year, string DateKey, DateTimeOffset LastAttemptUtc,
    bool FullRbiAttempted, bool Completed, int CorrectionsPending, int RbiPending,
    int TeamPitchingPending, string[] PendingRbiPlayerCodes, string[] Messages, string DatabaseVersion);

public sealed record RenderBackfillState(string NextFromDate, bool Completed);

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

    public static bool TryConsumeManualTrigger(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
    }

    public static string? GameDateKey(ScheduleGame game)
    {
        if (game.GameId is { Length: >= 8 } id && id.Take(8).All(char.IsDigit)) return id[..8];
        return DateTime.TryParse(game.GameDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : null;
    }

    public static bool ReconciliationDue(RenderReconciliationState? state, int year, string dateKey,
        string databaseVersion, DateTimeOffset now, TimeSpan retry)
    {
        if (state is null || state.Year != year) return true;
        var order = string.CompareOrdinal(state.DateKey, dateKey);
        if (order > 0) return false;
        if (order < 0) return true;
        if (!string.Equals(state.DatabaseVersion, databaseVersion, StringComparison.Ordinal)) return true;
        if (state.Completed) return false;
        return now - state.LastAttemptUtc >= retry;
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
    private DateTimeOffset? lastScheduleSyncUtc;

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
        if (options.IntervalMinutes is < 1 or > 60 || options.LookbackDays is < 0 or > 7 ||
            options.ReconciliationRetryMinutes is < 1 or > 1440 ||
            options.ScheduleSyncIntervalMinutes is < 10 or > 1440)
            throw new InvalidOperationException("RenderCollector 주기 또는 조회 일수 설정이 올바르지 않습니다.");
        if (string.IsNullOrWhiteSpace(site.DatabasePath) || !Path.IsPathFullyQualified(site.DatabasePath))
            throw new InvalidOperationException("RenderCollector에는 절대 DB 경로가 필요합니다.");
        if (options.BackfillChunkDays is < 1 or > 90)
            throw new InvalidOperationException("RenderCollector 과거 수집 청크 일수(BackfillChunkDays) 설정이 올바르지 않습니다.");
        if (options.BackfillEnabled && !DateTime.TryParseExact(options.BackfillFromDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidOperationException("RenderCollector 과거 수집 시작일(BackfillFromDate) 형식이 올바르지 않습니다 (yyyy-MM-dd).");

        options.JsonDirectory = Path.GetFullPath(options.JsonDirectory);
        options.HeartbeatPath = Path.GetFullPath(options.HeartbeatPath);
        options.ManualTriggerPath = Path.GetFullPath(options.ManualTriggerPath);
        options.ReconciliationStatePath = Path.GetFullPath(options.ReconciliationStatePath);
        options.BackfillStatePath = Path.GetFullPath(options.BackfillStatePath);
        Directory.CreateDirectory(options.JsonDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(options.HeartbeatPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.ManualTriggerPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.ReconciliationStatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.BackfillStatePath)!);
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

            try { await WaitForNextCycleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task WaitForNextCycleAsync(CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(options.IntervalMinutes);
        while (true)
        {
            if (RenderCollectionPolicy.TryConsumeManualTrigger(options.ManualTriggerPath))
            {
                logger.LogInformation("Render 수집기 SSH 수동 실행 요청을 감지했습니다.");
                return;
            }
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2), token);
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
        await CollectAndPublishAsync(from, to, token);

        if (options.BackfillEnabled)
        {
            try { await RunBackfillChunkAsync(now, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "[backfill] 과거 데이터 수집 보류; 다음 주기에 재시도합니다."); }
        }

        if (options.ScheduleSyncEnabled && (lastScheduleSyncUtc is null ||
                DateTimeOffset.UtcNow - lastScheduleSyncUtc >= TimeSpan.FromMinutes(options.ScheduleSyncIntervalMinutes)))
        {
            try { await SyncUpcomingScheduleAsync(now, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "예정 경기 일정 반영 보류; 다음 주기에 재시도합니다."); }
            lastScheduleSyncUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// [from, to] 구간(양 끝 포함, yyyy-MM-dd)의 KBO 일정을 수집해 종료된 경기를 실사용 DB에
    /// 반영합니다. 매일 돌아가는 최근 구간 수집과, 아래 과거 소급 수집(RunBackfillChunkAsync)이
    /// 이 메서드 하나를 공유합니다 — 날짜 구간 외에는 동일한 경로(네이버+KBO 공식 대조,
    /// roundCode 검증, 중복/부분 저장 방지)를 그대로 탑니다.
    /// </summary>
    private async Task CollectAndPublishAsync(string from, string to, CancellationToken token)
    {
        void Log(string message) => logger.LogInformation("[collector] {Message}", message);

        var games = await GameIdCollector.CollectAllKboGamesAsync(http, from, to, delayMs: 300, log: Log, ct: token);
        if (games.Count == 0)
        {
            logger.LogInformation("Render 수집기: {From}~{To} KBO 일정 없음", from, to);
            return;
        }

        var summary = await RelayCollector.CollectAllAsync(http, games, options.JsonDirectory,
            delayMs: 500, log: Log, ct: token);
        logger.LogInformation("Render JSON 수집 {From}~{To}: 완료={Complete} 부분={Partial} 기존/취소={Skipped} 제외={Excluded} 실패={Failed}",
            from, to, summary.Complete, summary.Partial, summary.Skipped, summary.Excluded, summary.Failed);

        var excluded = summary.ExcludedGameIds.ToHashSet(StringComparer.Ordinal);
        (int Year, string DateKey)? readyForReconciliation = null;
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
            var year = int.Parse(dateKey![..4], CultureInfo.InvariantCulture);
            if (readyForReconciliation is null || string.CompareOrdinal(dateKey, readyForReconciliation.Value.DateKey) > 0)
                readyForReconciliation = (year, dateKey);
        }

        if (options.ReconcileOfficialRecords && readyForReconciliation is { } target)
            await ReconcileOfficialRecordsAsync(target.Year, target.DateKey, token);
    }

    /// <summary>
    /// 과거 소급 수집을 한 주기당 한 청크(BackfillChunkDays)씩 진행합니다. 진행 상태는
    /// BackfillStatePath에 저장해 Render 재시작/재배포 후에도 이어서 진행되며, 다음 시작일이
    /// 매일 수집 구간(오늘-LookbackDays)에 닿으면 자동으로 완료 처리하고 더 이상 아무 일도
    /// 하지 않습니다.
    /// </summary>
    private async Task RunBackfillChunkAsync(DateTimeOffset now, CancellationToken token)
    {
        var state = await LoadBackfillStateAsync(token);
        if (state is { Completed: true }) return;

        var lookbackStart = now.Date.AddDays(-options.LookbackDays);
        var nextFrom = DateTime.ParseExact(state?.NextFromDate ?? options.BackfillFromDate,
            "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (nextFrom >= lookbackStart)
        {
            await SaveBackfillStateAsync(new RenderBackfillState(
                nextFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), true), token);
            logger.LogInformation("[backfill] {From}부터의 과거 수집이 최근 수집 구간에 도달해 완료 처리합니다.", options.BackfillFromDate);
            return;
        }

        var chunkEnd = nextFrom.AddDays(options.BackfillChunkDays - 1);
        if (chunkEnd >= lookbackStart) chunkEnd = lookbackStart.AddDays(-1);

        var from = nextFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = chunkEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        logger.LogInformation("[backfill] {From} ~ {To} 과거 데이터 수집 시작", from, to);

        await CollectAndPublishAsync(from, to, token);

        var nextStart = chunkEnd.AddDays(1);
        var completed = nextStart >= lookbackStart;
        var nextStartText = nextStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await SaveBackfillStateAsync(new RenderBackfillState(nextStartText, completed), token);
        logger.LogInformation("[backfill] {From} ~ {To} 처리 완료. 다음 시작일={Next} 완료여부={Completed}",
            from, to, nextStartText, completed);
    }

    private async Task<RenderBackfillState?> LoadBackfillStateAsync(CancellationToken token)
    {
        if (!File.Exists(options.BackfillStatePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<RenderBackfillState>(
                await File.ReadAllTextAsync(options.BackfillStatePath, token));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Render 과거 수집 진행 상태 파일을 읽지 못했습니다. 처음(BackfillFromDate)부터 다시 시작합니다.");
            return null;
        }
    }

    private async Task SaveBackfillStateAsync(RenderBackfillState state, CancellationToken token)
    {
        var temp = options.BackfillStatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), token);
            File.Move(temp, options.BackfillStatePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>
    /// 아직 시작하지 않은 남은 정규시즌 경기를 경기일정 화면에 표시할 수 있도록 자리표시자
    /// 행으로 DB에 반영합니다. 팀·통계 페이지가 쓰는 RoundCode='kbo_r' 실제 경기와는
    /// 완전히 분리된 값(<see cref="DatabaseCacheService.ScheduledPlaceholderRoundCode"/>)을
    /// 쓰므로 WAR·순위·리그 평균 등 기존 계산에는 영향이 없습니다. 실제로 경기가 열리면
    /// 이 자리표시자는 평소 결과 반영 경로(삭제 후 재삽입)로 자동 교체됩니다.
    /// </summary>
    private async Task SyncUpcomingScheduleAsync(DateTimeOffset now, CancellationToken token)
    {
        var from = now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = new DateTime(now.Year, 12, 31).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        void Log(string message) => logger.LogInformation("[schedule] {Message}", message);

        var games = await GameIdCollector.CollectAllKboGamesAsync(http, from, to, delayMs: 300, log: Log, ct: token);
        var candidates = games
            .Where(g => !g.Cancel && !g.Suspended && g.StatusCode == "BEFORE" &&
                !string.IsNullOrWhiteSpace(g.GameId) &&
                !string.IsNullOrWhiteSpace(g.HomeTeamCode) && !string.IsNullOrWhiteSpace(g.AwayTeamCode) &&
                g.HomeTeamCode!.ToUpperInvariant() is not ("EA" or "WE") &&
                g.AwayTeamCode!.ToUpperInvariant() is not ("EA" or "WE"))
            .OrderBy(g => g.GameId, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            logger.LogInformation("[schedule] {From}~{To} 예정 경기 없음", from, to);
            return;
        }

        writer ??= new DatabaseCacheService(site.DatabasePath);
        await writer.InitializeAsync(token);

        var placeholders = new List<ScheduledGamePlaceholder>();
        var usedHomeGames = new Dictionary<(string Home, string Away), int>();
        foreach (var game in candidates)
        {
            GameRequest parsed;
            try { parsed = GameRequest.Parse(game.GameId!); }
            catch (FormatException) { continue; }

            var home = game.HomeTeamCode!.ToUpperInvariant();
            var away = game.AwayTeamCode!.ToUpperInvariant();
            var year = int.Parse(parsed.Year, CultureInfo.InvariantCulture);

            int quota;
            try { quota = PlayoffSchedule2026.HomeGames(home, away); }
            catch (ArgumentException) { continue; } // 알 수 없는 팀 코드 — 안전하게 건너뜀

            var key = (home, away);
            if (!usedHomeGames.TryGetValue(key, out var used))
            {
                used = await writer.CountRegularSeasonHomeGamesAsync(year, home, away, token);
                usedHomeGames[key] = used;
            }
            // 정규시즌 홈/원정 공식 배정 한도를 넘으면 포스트시즌(또는 그 밖의 비정규 라운드)
            // 경기로 간주해 제외합니다 — 이 목록 API에는 roundCode가 없어 직접 구분할 수
            // 없기 때문입니다.
            if (used >= quota) continue;
            usedHomeGames[key] = used + 1;

            placeholders.Add(new ScheduledGamePlaceholder(
                RenderCollectionPolicy.DatabaseGameId(parsed), year,
                game.GameDate ?? parsed.GameId[..8], game.GameDateTime,
                home, away, null, game.StatusCode ?? "BEFORE"));
        }

        if (placeholders.Count == 0)
        {
            logger.LogInformation("[schedule] {From}~{To} 반영할 정규시즌 예정 경기 없음 (포스트시즌 등 제외)", from, to);
            return;
        }

        var inserted = await writer.UpsertScheduledGamesAsync(placeholders, token);
        logger.LogInformation("[schedule] {From}~{To} 예정 경기 {Count}건 확인, 신규 {Inserted}건 반영",
            from, to, placeholders.Count, inserted);
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

    private async Task ReconcileOfficialRecordsAsync(int year, string dateKey, CancellationToken token)
    {
        var prior = await LoadReconciliationStateAsync(token);
        var databaseVersion = await writer!.GetWebSourceVersionAsync(token);
        var now = DateTimeOffset.UtcNow;
        if (!RenderCollectionPolicy.ReconciliationDue(prior, year, dateKey, databaseVersion, now,
                TimeSpan.FromMinutes(options.ReconciliationRetryMinutes))) return;

        var sameTarget = prior is not null && prior.Year == year && prior.DateKey == dateKey &&
            string.Equals(prior.DatabaseVersion, databaseVersion, StringComparison.Ordinal);
        var messages = new List<string>();
        var correctionsPending = 1;
        var rbiPending = 1;
        var teamPitchingPending = 1;
        var fullRbiAttempted = sameTarget && prior!.FullRbiAttempted;
        string[] pendingPlayerCodes = sameTarget ? prior!.PendingRbiPlayerCodes ?? [] : [];
        var progress = new InlineProgress(message => logger.LogInformation("[reconcile] {Message}", message));

        logger.LogInformation("{Date} KBO 공식 시즌 기록 대조 시작: fullRbi={FullRbi}", dateKey, !fullRbiAttempted);

        if (sameTarget && prior!.CorrectionsPending == 0)
        {
            correctionsPending = 0;
            messages.Add("KBO 정정 대조 완료 상태 유지");
        }
        else try
        {
            var result = await writer!.SyncKboCorrectionsAsync(year, token);
            correctionsPending = result.Pending;
            messages.Add(result.Message);
            logger.LogInformation("[reconcile] {Message}", result.Message);
            foreach (var detail in result.Details) logger.LogWarning("[reconcile] {Detail}", detail);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            messages.Add("KBO 정정 대조 보류: " + ex.Message);
            logger.LogWarning(ex, "{Date} KBO 정정 대조 보류; 다음 주기에 재시도합니다.", dateKey);
        }

        if (sameTarget && prior!.FullRbiAttempted && prior.RbiPending == 0)
        {
            rbiPending = 0;
            pendingPlayerCodes = [];
            messages.Add("KBO 일자별 타점 대조 완료 상태 유지");
        }
        else try
        {
            IReadOnlyCollection<string>? retryPlayers = fullRbiAttempted ? pendingPlayerCodes : null;
            var result = await writer!.SyncOfficialRbiAsync(year, progress, token, playerCodes: retryPlayers);
            if (!fullRbiAttempted) fullRbiAttempted = true;
            rbiPending = result.Pending;
            pendingPlayerCodes = result.PendingPlayerCodes.ToArray();
            messages.Add(result.Message);
            logger.LogInformation("[reconcile] {Message}", result.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            messages.Add("KBO 일자별 타점 대조 보류: " + ex.Message);
            logger.LogWarning(ex, "{Date} KBO 일자별 타점 대조 보류; 다음 주기에 재시도합니다.", dateKey);
        }

        if (sameTarget && prior!.TeamPitchingPending == 0)
        {
            teamPitchingPending = 0;
            messages.Add("KBO 팀 자책점 대조 완료 상태 유지");
        }
        else try
        {
            var result = await writer!.SyncOfficialTeamPitchingAsync(year, progress, token);
            teamPitchingPending = result.Pending;
            messages.Add(result.Message);
            logger.LogInformation("[reconcile] {Message}", result.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            messages.Add("KBO 팀 자책점 대조 보류: " + ex.Message);
            logger.LogWarning(ex, "{Date} KBO 팀 자책점 대조 보류; 다음 주기에 재시도합니다.", dateKey);
        }

        var completed = correctionsPending == 0 && rbiPending == 0 && teamPitchingPending == 0;
        var finalDatabaseVersion = await writer.GetWebSourceVersionAsync(token);
        var state = new RenderReconciliationState(year, dateKey, DateTimeOffset.UtcNow, fullRbiAttempted,
            completed, correctionsPending, rbiPending, teamPitchingPending, pendingPlayerCodes, messages.ToArray(),
            finalDatabaseVersion);
        await SaveReconciliationStateAsync(state, token);
        if (completed)
            logger.LogInformation("{Date} KBO 공식 정정·일자별 타점·팀 자책점 대조 완료", dateKey);
        else
            logger.LogWarning("{Date} KBO 공식 기록 대조 보류: corrections={Corrections}, rbi={Rbi}, teamPitching={TeamPitching}; 다음 주기에 재시도합니다.",
                dateKey, correctionsPending, rbiPending, teamPitchingPending);
    }

    private async Task<RenderReconciliationState?> LoadReconciliationStateAsync(CancellationToken token)
    {
        if (!File.Exists(options.ReconciliationStatePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<RenderReconciliationState>(
                await File.ReadAllTextAsync(options.ReconciliationStatePath, token));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Render 공식 기록 대조 상태 파일을 읽지 못했습니다. 전체 대조를 다시 실행합니다.");
            return null;
        }
    }

    private async Task SaveReconciliationStateAsync(RenderReconciliationState state, CancellationToken token)
    {
        var temp = options.ReconciliationStatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), token);
            File.Move(temp, options.ReconciliationStatePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
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
