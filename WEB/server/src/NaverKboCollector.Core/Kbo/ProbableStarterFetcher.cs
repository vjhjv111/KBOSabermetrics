using System.Net;
using System.Text.Json.Nodes;

namespace KboRelayDownloader;

// Extracted from local/war-blend-lineup-20260929. Only announced starters are read,
// never the current relief pitcher. Transport failures must not become "unannounced".
public static class ProbableStarterFetcher
{
    public sealed record ProbableStarters(string? HomeStarterName, string? AwayStarterName);

    public static async Task<ProbableStarters> TryFetchAsync(HttpClient http, string gameId, CancellationToken ct = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(gameId, @"^\d{8}[A-Z]{4}\d(?:\d{4})?$"))
            throw new ArgumentException("Invalid game ID", nameof(gameId));
        var shortId = gameId[..13];
        foreach (var id in new[] { shortId + shortId[..4], shortId })
        {
            using var response = await http.GetAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/game-polling?isHighlight=false", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        throw new HttpRequestException("예고 선발 경기 정보를 찾지 못했습니다.");
    }

    public static ProbableStarters Parse(string json)
    {
        var game = JsonNode.Parse(json)?["result"]?["game"]
            ?? throw new System.Text.Json.JsonException("Missing game");
        string? Read(string key) => game[key]?.GetValue<string>() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        return new(Read("homeStarterName"), Read("awayStarterName"));
    }
}
