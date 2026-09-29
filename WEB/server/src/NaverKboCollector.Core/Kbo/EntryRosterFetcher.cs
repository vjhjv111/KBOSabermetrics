using System.Text.Json;
using System.Text.Json.Nodes;

namespace KboRelayDownloader;

// Naver's own game-polling endpoint (same host FullGameCollector already uses for the full
// play-by-play collection, unrelated to koreabaseball.com and its robots.txt restrictions).
// "textRelayData.homeEntry"/"awayEntry" carry the team's full registered roster for the game —
// batters AND pitchers, not just the starting 9/1 that KBO's own lineup panel shows. Calling
// this BEFORE first pitch (which the endpoint allows — it answers for a BEFORE-status game too)
// is what makes "entry so far" unambiguous: nobody could have substituted in yet, so whatever
// comes back at that point is simply the day's full announced entry.
public static class EntryRosterFetcher
{
    public sealed record EntryPlayerRow(string Role, string? Pcode, string Name, string? Position);
    public sealed record GameEntryRosters(IReadOnlyList<EntryPlayerRow> Away, IReadOnlyList<EntryPlayerRow> Home);

    public static async Task<GameEntryRosters?> TryFetchAsync(HttpClient http, string naverGameId, CancellationToken ct = default)
    {
        var url = $"https://api-gw.sports.naver.com/schedule/games/{Uri.EscapeDataString(naverGameId)}/game-polling?isHighlight=false";
        string json;
        try { json = await http.GetStringAsync(url, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }

        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return null; }

        var relay = root?["result"]?["textRelayData"];
        if (relay is null) return null;

        var away = ReadTeam(relay["awayEntry"]);
        var home = ReadTeam(relay["homeEntry"]);
        return away.Count == 0 && home.Count == 0 ? null : new(away, home);
    }

    private static List<EntryPlayerRow> ReadTeam(JsonNode? entry)
    {
        var list = new List<EntryPlayerRow>();
        if (entry is null) return list;
        foreach (var role in new[] { "batter", "pitcher" })
        {
            if (entry[role] is not JsonArray array) continue;
            foreach (var node in array)
            {
                var name = node?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var pcode = node?["pcode"]?.GetValue<string>();
                var position = node?["pos"]?.GetValue<string>();
                list.Add(new(role, pcode, name, position));
            }
        }
        return list;
    }
}
