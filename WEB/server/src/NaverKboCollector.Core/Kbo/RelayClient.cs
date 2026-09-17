using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace KboRelayDownloader;

public sealed record GameRequest(string GameId, string LeagueId = "1", string SeriesId = "0")
{
    public string Year => GameId[..4];
    public string Query => $"leagueId={LeagueId}&seriesId={SeriesId}&gameId={GameId}&gyear={Year}";
    public string PageUrl => "https://www.koreabaseball.com/Game/LiveText.aspx?" + Query;
    public string FileName => $"{GameId}_L{LeagueId}_S{SeriesId}_playlog.json";

    public static GameRequest Parse(string input)
    {
        var value = input.Trim();
        string league = "1", series = "0";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (!uri.Host.Equals("www.koreabaseball.com", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.Equals("koreabaseball.com", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("KBO 문자중계 URL을 입력하세요.");
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length == 2 ? Uri.UnescapeDataString(p[1]) : "");
            value = query.GetValueOrDefault("gameId") ?? "";
            league = query.GetValueOrDefault("leagueId") ?? "1";
            series = query.GetValueOrDefault("seriesId") ?? "0";
        }
        else
        {
            value = Path.GetFileNameWithoutExtension(value);
        }
        // Naver filenames append the four-digit season to the KBO ID.
        var match = Regex.Match(value, @"^(?<id>\d{8}[A-Za-z]{4}\d)(?<year>\d{4})?$");
        if (!match.Success) throw new FormatException($"올바르지 않은 경기 ID: {value}");
        var id = match.Groups["id"].Value.ToUpperInvariant();
        if (!DateTime.TryParseExact(id[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FormatException("경기 날짜가 올바르지 않습니다.");
        if (match.Groups["year"].Success && match.Groups["year"].Value != id[..4])
            throw new FormatException("네이버 ID의 시즌과 경기 날짜가 다릅니다.");
        if (!Regex.IsMatch(league, @"^\d{1,2}$") || !Regex.IsMatch(series, @"^\d{1,2}$"))
            throw new FormatException("리그/시리즈 값이 올바르지 않습니다.");
        return new(id, league, series);
    }
}

public sealed record PlayLog(int Sequence, string SourceId, int? Inning, string? Half, string Text);
public sealed record RelayDocument(string GameId, string LeagueId, string SeriesId, string SourceUrl,
    DateTimeOffset DownloadedAt, string Order, bool IsComplete, int Count, IReadOnlyList<PlayLog> Logs)
{
    public int SchemaVersion { get; init; } = 3;
    public BoxScore? BoxScore { get; init; }
    public IdentityMappingSummary? IdentityMapping { get; init; }
}

public sealed class RelayClient : IDisposable
{
    private readonly HttpClient http;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public RelayClient()
    {
        http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        }) { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) KboRelayDownloader/1.0");
    }

    public async Task<(RelayDocument Document, string Html, string ScoreboardHtml)> DownloadAsync(GameRequest game, CancellationToken ct = default)
    {
        var html = await FetchHtmlAsync(game, "LiveTextView2.aspx", ct);
        var document = ParseHtml(game, html);
        var scoreboard = await FetchHtmlAsync(game, "LiveTextView1.aspx", ct);
        // Save only after both responses have been parsed successfully.
        var boxScore = BoxScoreParser.Parse(game, html, scoreboard);
        return (document with { BoxScore = boxScore }, html, scoreboard);
    }

    private async Task<string> FetchHtmlAsync(GameRequest game, string endpoint, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://www.koreabaseball.com/Game/" + endpoint + "?_=" + Guid.NewGuid().ToString("N"));
            request.Headers.Referrer = new Uri(game.PageUrl);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["leagueId"] = game.LeagueId, ["seriesId"] = game.SeriesId,
                ["gameId"] = game.GameId, ["gyear"] = game.Year
            });
            using var response = await http.SendAsync(request, ct);
            // Never treat a bodyless 304 as a successful download.
            if (response.StatusCode == HttpStatusCode.NotModified || (int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
            {
                if (attempt == 2) throw new HttpRequestException($"KBO 응답 HTTP {(int)response.StatusCode}. 파일을 저장하지 않았습니다. 잠시 후 다시 시도하세요.");
                await Task.Delay(TimeSpan.FromSeconds(attempt + 2), ct);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            return html;
        }
        throw new InvalidOperationException();
    }

    public static RelayDocument ParseHtml(GameRequest game, string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var rows = new List<(string Id, string Text)>();
        var seen = new HashSet<string>();
        for (int bucket = 10; bucket >= 1; bucket--)
        {
            foreach (var element in doc.QuerySelectorAll($"#numCont{bucket} [id*='_spanLiveText_']"))
            {
                if (element.Id is not { } id || !seen.Add(id)) continue;
                var text = Regex.Replace(element.TextContent, @"\s+", " ").Trim();
                if (text.Length > 0) rows.Add((id, text));
            }
        }
        if (rows.Count == 0) throw new InvalidDataException("플레이로그 없음: 경기 ID/시리즈를 확인하세요. 경기 전·취소 또는 페이지 구조 변경일 수 있습니다.");
        rows.Reverse();
        var logs = new List<PlayLog>();
        int? inning = null;
        string? half = null;
        foreach (var row in rows)
        {
            var heading = Regex.Match(row.Text, @"^(\d+)회\s*(초|말)\s");
            if (heading.Success) { inning = int.Parse(heading.Groups[1].Value); half = heading.Groups[2].Value == "초" ? "top" : "bottom"; }
            logs.Add(new(logs.Count + 1, row.Id, inning, half, row.Text));
        }
        return new(game.GameId, game.LeagueId, game.SeriesId, game.PageUrl, DateTimeOffset.UtcNow,
            "chronological", logs.Any(l => l.Text == "경기종료"), logs.Count, logs);
    }

    public static async Task<string> SaveAsync(GameRequest game, RelayDocument document, string directory, CancellationToken ct)
        => await SaveJsonAsync(game, JsonSerializer.Serialize(document, JsonOptions), directory, ct);

    public static async Task<string> SaveJsonAsync(GameRequest game, string json, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, game.FileName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return path;
    }

    public static async Task<bool> HasBoxScoreAsync(string path, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
            var root = json.RootElement;
            if (!root.TryGetProperty("boxScore", out var box) || box.ValueKind != JsonValueKind.Object) return false;
            foreach (var side in new[] { "away", "home" })
            {
                if (!box.TryGetProperty(side, out var team) || team.ValueKind != JsonValueKind.Object) return false;
                foreach (var table in new[] { "batting", "pitching" })
                    if (!team.TryGetProperty(table, out var t) || t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0) return false;
                if (!team.TryGetProperty("totals", out var totals) || totals.ValueKind != JsonValueKind.Object ||
                    !team.TryGetProperty("innings", out var innings) || innings.ValueKind != JsonValueKind.Array || innings.GetArrayLength() == 0) return false;
            }
            return root.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Array && logs.GetArrayLength() > 0;
        }
        catch (JsonException) { return false; }
    }

    public void Dispose() => http.Dispose();
}
