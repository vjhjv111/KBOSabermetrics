using System.Globalization;
using KboRelayDownloader;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelayUI.Collection;
using NaverRelayUI.Models;

namespace NaverSabermetrics.Web;

public sealed class DailyLineupOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;
}

/// <summary>
/// 매일 KBO 경기가 있는 구단의 그날 선발 라인업/선발투수를 상시로 수집합니다. Render 웹 서비스와
/// 같은 프로세스에서 RenderCollectorWorker와 동일한 패턴(BackgroundService, 같은 영구 디스크의
/// DB에 직접 씀)으로 동작합니다.
///
/// KBO가 그날 공식 타자 라인업을 아직 발표하지 않았으면, 그보다 훨씬 먼저(며칠 전부터) 따로
/// 발표되는 "예고선발"만 있으면 그것만 먼저 적어 둡니다(IsOfficial=0, Source="probable_starter";
/// ProbableStarterFetcher, 네이버 game-polling의 homeStarterName/awayStarterName). 예고선발도
/// 없으면 추정치로 채우지 않고 그냥 비워 둡니다. 이후 주기마다 다시 확인해서 공식 라인업이 뜨면
/// 그 값으로 완전히 채웁니다(IsOfficial=1, Source="official"). 한 번 공식으로 확정된 값은 이후
/// 주기에서 다시 비우거나 덮어쓰지 않습니다 — ApplyTeamAsync가 그 경우 아무 것도 하지 않고
/// 건너뜁니다.
/// </summary>
public sealed class DailyLineupWorker : BackgroundService
{
    private readonly ILogger<DailyLineupWorker> logger;
    private readonly SiteOptions site;
    private readonly DailyLineupOptions options;
    private readonly TimeZoneInfo korea;
    private readonly HttpClient http;

    public DailyLineupWorker(ILogger<DailyLineupWorker> logger, SiteOptions site, DailyLineupOptions options)
    {
        this.logger = logger;
        this.site = site;
        this.options = options;
        korea = FindKoreaTimeZone();
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; KBOSabermetrics-DailyLineup/1.0)");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        if (options.IntervalMinutes is < 5 or > 120)
            throw new InvalidOperationException("DailyLineup 주기 설정이 올바르지 않습니다.");
        if (string.IsNullOrWhiteSpace(site.DatabasePath) || !Path.IsPathFullyQualified(site.DatabasePath))
            throw new InvalidOperationException("DailyLineup에는 절대 DB 경로가 필요합니다.");

        logger.LogInformation("일일 라인업 수집기 시작: interval={Interval}m db={Database}", options.IntervalMinutes, site.DatabasePath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "일일 라인업 수집 중 오류"); }

            try { await Task.Delay(TimeSpan.FromMinutes(options.IntervalMinutes), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var todayKorea = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, korea);
        var gameDate = todayKorea.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateKey = todayKorea.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        List<ScheduleGame> games;
        try
        {
            games = await GameIdCollector.CollectAllKboGamesAsync(http, gameDate, gameDate, delayMs: 300,
                log: message => logger.LogInformation("{Message}", message), ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "오늘({Date}) KBO 일정 조회 실패", gameDate);
            return;
        }

        var eligible = RenderCollectionPolicy.EligibleGames(games, dateKey);
        if (eligible.Count == 0)
        {
            logger.LogInformation("오늘({Date}) 예정된 KBO 정규시즌 경기가 없습니다.", gameDate);
            return;
        }

        var writer = new DatabaseCacheService(site.DatabasePath);
        await writer.InitializeAsync(ct).ConfigureAwait(false);
        try
        {
            var removed = await writer.CleanupOldLineupOptimizerResultsAsync(ct: ct).ConfigureAwait(false);
            if (removed > 0) logger.LogInformation("라인업 최적화 결과 캐시 {Count}건 정리(14일 초과)", removed);
        }
        catch (Exception ex) { logger.LogWarning(ex, "라인업 최적화 결과 캐시 정리 실패"); }
        using var relay = new RelayClient();

        // KBO 공식 "전체 등록 현황"(Player/RegisterAll.aspx)은 경기별이 아니라 전체 팀을 한 번에
        // 보여주므로, 경기 루프 밖에서 이번 주기에 딱 한 번만 조회/파싱합니다. 이 값 자체가 그날
        // 기준 "그 팀의 현재 로스터"이므로 공식/추정 구분 없이 그대로 반영합니다. 페이지엔 pcode가
        // 없어서(이름+등번호만) 이 앱 DB의 Players 테이블에서 이름+팀으로 pcode를 일괄 조회합니다.
        // 실패하면(페이지 구조 변경, 일시적 접속 오류 등) 이번 주기는 기존 DB 값을 그대로 두고
        // 건너뜁니다.
        IReadOnlyDictionary<string, IReadOnlyList<EntryRosterPlayer>> currentRosterByTeam =
            new Dictionary<string, IReadOnlyList<EntryRosterPlayer>>();
        try
        {
            var registerAllHtml = await RegisterAllRosterFetcher.FetchHtmlAsync(http, ct).ConfigureAwait(false);
            var parsedRoster = RegisterAllRosterFetcher.ParseRoster(registerAllHtml);
            var keys = parsedRoster.SelectMany(kv => kv.Value.Select(p => (Team: kv.Key, p.Name, p.BackNo))).Distinct().ToList();
            var pcodes = await writer.ResolvePcodesByTeamAndNameAsync(keys, ct).ConfigureAwait(false);

            var byTeam = new Dictionary<string, List<EntryRosterPlayer>>();
            var missing = new List<string>();
            foreach (var (team, players) in parsedRoster)
            {
                foreach (var p in players)
                {
                    if (!pcodes.TryGetValue((team, p.Name, p.BackNo), out var pcode) || string.IsNullOrWhiteSpace(pcode))
                    {
                        missing.Add($"{team} {p.Name}(#{p.BackNo})");
                        continue;
                    }
                    var role = p.Position == "투수" ? "pitcher" : "batter";
                    if (!byTeam.TryGetValue(team, out var list)) byTeam[team] = list = new();
                    list.Add(new EntryRosterPlayer(role, pcode, p.Name, p.Position));
                }
            }
            currentRosterByTeam = byTeam.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<EntryRosterPlayer>)kv.Value);
            logger.LogInformation("KBO 등록 현황 조회 완료: {TeamCount}개 팀, pcode 매칭 실패 {Missing}명{MissingList}",
                currentRosterByTeam.Count, missing.Count, missing.Count > 0 ? " — " + string.Join(", ", missing) : "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "KBO 등록 현황(RegisterAll) 조회/파싱 실패 — 이번 주기는 건너뜁니다."); }

        foreach (var scheduled in eligible)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(scheduled.GameId)) continue;

            GameRequest request;
            try { request = GameRequest.Parse(scheduled.GameId); }
            catch (Exception ex) { logger.LogWarning(ex, "경기 ID 파싱 실패: {GameId}", scheduled.GameId); continue; }
            if (request.GameId.Length < 12) continue;

            LineupPreview? preview = null;
            try
            {
                var html = await relay.FetchLiveTextHtmlAsync(request, ct).ConfigureAwait(false);
                preview = LineupPreviewParser.TryParse(request, html);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "라인업 조회 실패: {GameId}", request.GameId); }

            // 그날 공식 라인업(타자 전체)이 아직 없을 때 쓸 "예고선발" — 라인업 패널보다 훨씬
            // 먼저(며칠 전부터) 채워지는 네이버 game-polling의 homeStarterName/awayStarterName.
            ProbableStarterFetcher.ProbableStarters? probable = null;
            try { probable = await ProbableStarterFetcher.TryFetchAsync(http, request.GameId, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "예고선발 조회 실패: {GameId}", request.GameId); }

            var awayCode = request.GameId.Substring(8, 2);
            var homeCode = request.GameId.Substring(10, 2);

            string? ProbablePcode(string teamCode, string? name) =>
                !string.IsNullOrWhiteSpace(name) && currentRosterByTeam.TryGetValue(teamCode, out var roster)
                    ? roster.FirstOrDefault(p => p.Name == name)?.Pcode
                    : null;

            // 경기 하나(팀 하나)에서 예외가 나도 이번 주기의 나머지 경기들은 계속 처리하도록, 각
            // 팀 단위로 개별적으로 잡습니다 — 그렇지 않으면(과거 실제로 발생) 그날 첫 번째로 처리된
            // 경기의 라인업 파싱이 한 번 꼬이는 것만으로 그날 전체 주기가 중단돼, 다른 모든 경기의
            // 라인업까지 통째로 안 채워지는 문제가 있었습니다.
            try
            {
                await ApplyTeamAsync(writer, gameDate, awayCode, homeCode, request.GameId, preview?.Away,
                    probable?.AwayStarterName, ProbablePcode(awayCode, probable?.AwayStarterName), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "라인업 반영 실패: {Date} {Team}", gameDate, awayCode); }

            try
            {
                await ApplyTeamAsync(writer, gameDate, homeCode, awayCode, request.GameId, preview?.Home,
                    probable?.HomeStarterName, ProbablePcode(homeCode, probable?.HomeStarterName), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "라인업 반영 실패: {Date} {Team}", gameDate, homeCode); }

            try
            {
                await ApplyEntryRosterAsync(writer, gameDate, awayCode, currentRosterByTeam.GetValueOrDefault(awayCode), ct).ConfigureAwait(false);
                await ApplyEntryRosterAsync(writer, gameDate, homeCode, currentRosterByTeam.GetValueOrDefault(homeCode), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "등록 로스터 반영 실패: {Date} {Away}/{Home}", gameDate, awayCode, homeCode); }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    private async Task ApplyTeamAsync(DatabaseCacheService writer, string gameDate, string teamCode, string opponentCode,
        string gameId, TeamLineupPreview? official, string? probableStarterName, string? probableStarterPcode, CancellationToken ct)
    {
        if (official is not null && (official.Batters.Count > 0 || official.StartingPitcher is not null))
        {
            await writer.UpsertOfficialDailyLineupAsync(gameDate, teamCode, opponentCode, gameId, official, ct).ConfigureAwait(false);
            logger.LogInformation("공식 라인업 확정: {Date} {Team} (타자 {Batters}명, 선발 {Pitcher})",
                gameDate, teamCode, official.Batters.Count, official.StartingPitcher?.Name ?? "-");
            return;
        }

        // 이미 공식 라인업이 반영돼 있으면 건드리지 않습니다.
        if (await writer.HasOfficialDailyLineupAsync(gameDate, teamCode, ct).ConfigureAwait(false)) return;

        // 공식 타자 라인업은 아직이지만 예고선발은 발표돼 있으면 그것만 우선 적어 둡니다
        // (IsOfficial=0, Source="probable_starter" — 추측이 아니라 실제 발표값이지만 그날 최종
        // 확정 라인업은 아니라서 그렇게 표시합니다. 로테이션이 바뀌면 다음 주기에 새 이름으로
        // 덮어써집니다).
        if (!string.IsNullOrWhiteSpace(probableStarterName))
        {
            await writer.UpsertProbableStarterAsync(gameDate, teamCode, opponentCode, gameId, probableStarterPcode, probableStarterName, ct).ConfigureAwait(false);
            logger.LogInformation("예고선발 반영: {Date} {Team} {Pitcher}", gameDate, teamCode, probableStarterName);
            return;
        }

        // 공식 라인업도 예고선발도 없으면 추정치로 채우지 않고 비워 둡니다.
        await writer.ClearDailyLineupAsync(gameDate, teamCode, ct).ConfigureAwait(false);
        logger.LogInformation("공식 라인업/예고선발 모두 없음 — 비움: {Date} {Team}", gameDate, teamCode);
    }

    // 후보까지 포함한 팀의 현재 로스터. KBO 공식 "전체 등록 현황"(Player/RegisterAll.aspx)이 매일
    // 갱신되는 그날 기준 등록 선수 전체 명단이라, 이 값 자체가 곧 그 팀의 현재 로스터입니다 —
    // "공식/추정"을 나눌 필요 없이 그대로 덮어씁니다. 이번 주기에 KBO 등록 현황 조회/파싱 자체가
    // 실패했으면(위 RunCycleAsync에서 잡힘) roster가 null/빈 목록으로 들어오는데, 그때는 잘못된
    // 값으로 기존 DB 값을 덮어쓰지 않고 그대로 둡니다.
    private async Task ApplyEntryRosterAsync(DatabaseCacheService writer, string gameDate, string teamCode,
        IReadOnlyList<EntryRosterPlayer>? roster, CancellationToken ct)
    {
        if (roster is not { Count: > 0 })
        {
            logger.LogInformation("KBO 등록 현황에 이 팀 데이터 없음 — 로스터 갱신 건너뜀: {Date} {Team}", gameDate, teamCode);
            return;
        }
        await writer.UpsertOfficialEntryRosterAsync(gameDate, teamCode, roster, ct).ConfigureAwait(false);
        logger.LogInformation("로스터 반영(KBO 등록 현황): {Date} {Team} ({Count}명)", gameDate, teamCode, roster.Count);
    }

    private static TimeZoneInfo FindKoreaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
    }
}
