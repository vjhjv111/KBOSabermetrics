using System.Text.Json;
using KboRelayDownloader;
using NaverRelayUI.Collection;

namespace NaverSabermetrics.Web;

public sealed record StarterGame(string Id, string? Time, string Away, string Home, string Status,
    string? AwayStarter, string? HomeStarter, DateTimeOffset? UpdatedAt, bool FetchFailed);
public sealed record StarterDay(string Date, DateTimeOffset UpdatedAt, StarterGame[] Games);
public sealed record BotStartersResponse(string Date, string TimeZone, DateTimeOffset? UpdatedAt,
    bool Available, bool Stale, int RefreshSeconds, StarterGame[] Games, string Text);

// Small persisted snapshots, independent of the season statistics DB/import gate.
public sealed class BotStartersService(SiteOptions options)
{
    private readonly string path = Path.Combine(options.StateDirectory, "bot-starters.json");
    private Dictionary<string, StarterDay> days = Load(Path.Combine(options.StateDirectory, "bot-starters.json"));
    private static Dictionary<string, StarterDay> Load(string path)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, StarterDay>>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public StarterDay? Get(string date) => Volatile.Read(ref days).GetValueOrDefault(date);
    public string Version => string.Join("|", Volatile.Read(ref days).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value.UpdatedAt.UtcTicks}"));
    public async Task SaveAsync(StarterDay day, CancellationToken ct)
    {
        var today = BotGamesService.ParseDate(null).ToString("yyyy-MM-dd");
        var next = Volatile.Read(ref days).Where(x => string.CompareOrdinal(x.Key, today) >= 0).ToDictionary(x => x.Key, x => x.Value);
        next[day.Date] = day;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next), ct);
        File.Move(path + ".tmp", path, true);
        Volatile.Write(ref days, next);
    }
    public BotStartersResponse Query(int offset)
    {
        if (offset is < 0 or > 1) throw new RequestError("dayOffset은 0(오늘) 또는 1(내일)이어야 합니다.");
        var date = BotGamesService.ParseDate(null).AddDays(offset).ToString("yyyy-MM-dd");
        return Format(date, Get(date), DateTimeOffset.UtcNow);
    }
    public static BotStartersResponse Format(string date, StarterDay? day, DateTimeOffset now)
    {
        var rows = day?.Games ?? [];
        var stale = day is null || now - day.UpdatedAt > TimeSpan.FromMinutes(15) || rows.Any(x => x.FetchFailed);
        var lines = new List<string>();
        if (day is null) lines.Add("선발 정보를 아직 수집하지 못했습니다. 잠시 후 다시 확인해 주세요.");
        else if (rows.Length == 0) lines.Add("예정된 경기가 없습니다.");
        foreach (var g in rows)
        {
            if (g.Status == "CANCEL") lines.Add($"{g.Away} vs {g.Home} 취소");
            if (g.Status != "CANCEL")
            {
                var missing = g.FetchFailed ? "조회 실패" : "미발표";
                lines.Add($"{g.Away} {g.AwayStarter ?? missing} vs {g.HomeStarter ?? missing} {g.Home}" +
                    (g.FetchFailed && g.UpdatedAt.HasValue ? " (이전 수집값)" : ""));
            }
        }
        if (stale && day is not null) lines.Add("일부 정보가 오래되었거나 수집이 지연되고 있습니다.");
        return new(date, "Asia/Seoul", day?.UpdatedAt, day is not null, stale, 300, rows, string.Join('\n', lines));
    }
}

public sealed class BotStartersWorker(BotStartersService store, ILogger<BotStartersWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FANZAI-Starters/1.0)");
        while (!ct.IsCancellationRequested)
        {
            var today = BotGamesService.ParseDate(null);
            for (var offset = 0; offset <= 1; offset++)
            {
                var date = today.AddDays(offset).ToString("yyyy-MM-dd");
                try
                {
                    var schedule = await GameIdCollector.CollectAllKboGamesAsync(http, date, date, ct: ct);
                    var rows = new List<StarterGame>();
                    foreach (var g in schedule.Where(g => g.CategoryId == "kbo" && RenderCollectionPolicy.GameDateKey(g) == date.Replace("-", "")))
                    {
                        if (g.GameId is null) continue;
                        var old = store.Get(date)?.Games.FirstOrDefault(x => x.Id == g.GameId);
                        var row = new StarterGame(g.GameId, g.GameDateTime, g.AwayTeamName ?? g.AwayTeamCode ?? "원정", g.HomeTeamName ?? g.HomeTeamCode ?? "홈",
                            g.Cancel ? "CANCEL" : g.StatusCode ?? "BEFORE", null, null, null, false);
                        if (row.Status != "CANCEL")
                        {
                            try
                            {
                                var starters = await ProbableStarterFetcher.TryFetchAsync(http, g.GameId, ct);
                                row = row with { AwayStarter = starters.AwayStarterName, HomeStarter = starters.HomeStarterName, UpdatedAt = DateTimeOffset.UtcNow };
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "예고 선발 조회 실패: {GameId}", g.GameId);
                                row = row with { AwayStarter = old?.AwayStarter, HomeStarter = old?.HomeStarter, UpdatedAt = old?.UpdatedAt, FetchFailed = true };
                            }
                        }
                        rows.Add(row);
                        await Task.Delay(300, ct);
                    }
                    await store.SaveAsync(new(date, DateTimeOffset.UtcNow, rows.OrderBy(x => x.Time).ThenBy(x => x.Id).ToArray()), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "예고 선발 수집 실패: {Date}; 이전 캐시 유지", date); }
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
