using System.Net;
using System.Text.Json.Nodes;

namespace KboRelayDownloader;

// Extracted from local/war-blend-lineup-20260929. Only announced starters are read,
// never the current relief pitcher. Transport failures must not become "unannounced".
public static class ProbableStarterFetcher
{
    public sealed record LineupPlayer(int Order, string Name, string Position, string? PlayerId);
    public sealed record ProbableStarters(string? HomeStarterName, string? AwayStarterName)
    {
        public LineupPlayer[] HomeLineup { get; init; } = [];
        public LineupPlayer[] AwayLineup { get; init; } = [];
    }

    public static async Task<ProbableStarters> TryFetchAsync(HttpClient http, string gameId, CancellationToken ct = default,
        bool includeLineups = false)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(gameId, @"^\d{8}[A-Z]{4}\d(?:\d{4})?$"))
            throw new ArgumentException("Invalid game ID", nameof(gameId));
        var shortId = gameId[..13];
        foreach (var id in new[] { gameId.Length == 17 ? gameId : shortId + shortId[..4], shortId })
        {
            using var response = await http.GetAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/game-polling?isHighlight=false", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            var data = Parse(await response.Content.ReadAsStringAsync(ct));
            if (!includeLineups) return data;
            // The preview contains the announced starting nine before textRelayData exists.
            // Unlike relay tables, it does not replace starters with in-game substitutions.
            using var preview = await http.GetAsync($"https://api-gw.sports.naver.com/schedule/games/{id}/preview", ct);
            preview.EnsureSuccessStatusCode();
            var lineups = ParsePreview(await preview.Content.ReadAsStringAsync(ct));
            return data with { HomeLineup = lineups.HomeLineup, AwayLineup = lineups.AwayLineup };
        }
        throw new HttpRequestException("예고 선발 경기 정보를 찾지 못했습니다.");
    }

    public static ProbableStarters ParsePreview(string json)
    {
        var root = JsonNode.Parse(json);
        if (root?["result"] is not JsonObject)
            throw new System.Text.Json.JsonException("Missing preview result");
        var preview = root["result"]?["previewData"];
        static LineupPlayer[] Read(JsonNode? node) {
            if (node is not JsonArray array) return [];
            var rows = array.Select(x => new LineupPlayer(
                int.TryParse(x?["batorder"]?.ToString(), out var order) ? order : 0,
                x?["playerName"]?.ToString().Trim() ?? "", x?["positionName"]?.ToString().Trim() ?? "",
                x?["playerCode"]?.ToString()))
                .Where(x => x.Order is >= 1 and <= 9).OrderBy(x => x.Order).ToArray();
            return rows.Length == 9 && rows.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, 9))
                && rows.All(x => x.Name.Length > 0) ? rows : [];
        }
        return new(null, null) {
            HomeLineup = Read(preview?["homeTeamLineUp"]?["fullLineUp"]),
            AwayLineup = Read(preview?["awayTeamLineUp"]?["fullLineUp"])
        };
    }

    public static ProbableStarters Parse(string json)
    {
        var result = JsonNode.Parse(json)?["result"];
        var game = result?["game"]
            ?? throw new System.Text.Json.JsonException("Missing game");
        string? Read(string key) => game[key]?.GetValue<string>() is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        return new(Read("homeStarterName"), Read("awayStarterName"))
        {
            HomeLineup = ReadLineup(result?["textRelayData"]?["homeLineup"]?["batter"]),
            AwayLineup = ReadLineup(result?["textRelayData"]?["awayLineup"]?["batter"])
        };
    }

    private static LineupPlayer[] ReadLineup(JsonNode? node)
    {
        if (node is not JsonArray array) return [];
        static int Number(JsonNode? n) => int.TryParse(n?.ToString(), out var v) ? v : 0;
        // seqno=1 is the original starter, even after he leaves the game (cout=true).
        // Never use the current entry or a substitute to fill a missing starter.
        var rows = array.Where(x => x is not null && Number(x["seqno"]) == 1)
            .Select(x => new LineupPlayer(Number(x!["batOrder"]), x["name"]?.ToString().Trim() ?? "",
                x["posName"]?.ToString().Trim() ?? "", x["pcode"]?.ToString()))
            .OrderBy(x => x.Order).ToArray();
        return rows.Length == 9 && rows.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, 9))
            && rows.All(x => x.Name.Length > 0) ? rows : [];
    }
}
