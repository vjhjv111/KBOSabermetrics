using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KboRelayDownloader;

namespace NaverRelayUI.Collection;

public sealed record NaverCollectionResult(string? Json, bool IsComplete, string? RoundCode,
    string? StatusCode, int ExpectedInnings, IReadOnlyList<int> CollectedInnings, IReadOnlyList<string> Errors);

public static class FullGameCollector
{
    public static bool IsEnded(string? status) => status is "RESULT" or "ENDED";
    private static string GamePollingUrl(string gameId, int? inning)
        => $"https://api-gw.sports.naver.com/schedule/games/{Uri.EscapeDataString(gameId)}/game-polling?isHighlight=false"
           + (inning.HasValue ? $"&inning={inning.Value}" : "");

    // Compatibility entry point: callers expecting a full game never receive a partial response.
    public static async Task<string?> CollectFullGameJsonAsync(HttpClient http, string gameId,
        int delayMs = 300, Action<string>? log = null, CancellationToken ct = default)
    {
        var result = await CollectResultAsync(http, gameId, delayMs, log, ct);
        return result.IsComplete ? result.Json : null;
    }

    public static async Task<NaverCollectionResult> CollectResultAsync(HttpClient http, string gameId,
        int delayMs = 300, Action<string>? log = null, CancellationToken ct = default)
    {
        JsonObject root;
        var effectiveGameId = gameId;
        try
        {
            var json = await http.GetStringAsync(GamePollingUrl(effectiveGameId, null), ct);
            root = ReadValidatedRoot(json, effectiveGameId, requireRelay: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        // Older (archived) games are keyed by Naver under the original 13-character ID without the
        // trailing season suffix that current-season game-polling requests use; a 404 on the first
        // try is the signal to retry once under that alternate ID form before giving up.
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound && TryAlternateGameId(gameId, out var alternate))
        {
            log?.Invoke($"  네이버 {gameId}: 기본 응답 404, 연도 접미사 없는 옛 ID({alternate})로 재시도합니다.");
            try
            {
                var json = await http.GetStringAsync(GamePollingUrl(alternate, null), ct);
                root = ReadValidatedRoot(json, alternate, requireRelay: false);
                effectiveGameId = alternate;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex2)
            {
                return new(null, false, null, null, 0, [], [$"기본 응답 수집 실패: {ex2.Message}"]);
            }
        }
        catch (Exception ex)
        {
            return new(null, false, null, null, 0, [], [$"기본 응답 수집 실패: {ex.Message}"]);
        }

        var game = root["result"]!["game"]!.AsObject();
        string? round = String(game["roundCode"]), status = String(game["statusCode"]);
        var relay = root["result"]!["textRelayData"] as JsonObject;
        var errors = new List<string>();
        var collected = new List<int>();
        var expected = ExpectedInnings(game, relay);

        // The caller uses this verified metadata to exclude other series. Never guess seriesId=0.
        if (round != "kbo_r")
            return new(root.ToJsonString(), false, round, status, expected, collected,
                [round is null ? "정규시즌 여부(roundCode)를 확인할 수 없습니다." : $"정규시즌 제외: {round}"]);
        if (!IsEnded(status)) log?.Invoke($"  네이버 {effectiveGameId}: 진행 상태 {status ?? "?"}, 현재까지의 기록을 저장합니다.");
        if (relay is null || expected == 0)
        {
            errors.Add(relay is null ? "문자중계 데이터가 없습니다." : "실제로 진행된 이닝 수를 확인할 수 없습니다.");
            return new(root.ToJsonString(), false, round, status, expected, collected, errors);
        }

        var mergedPlays = new Dictionary<int, JsonNode>();
        for (int inning = 1; inning <= expected; inning++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync(GamePollingUrl(effectiveGameId, inning), ct);
                var inningRoot = ReadValidatedRoot(json, effectiveGameId, requireRelay: true);
                if (String(inningRoot["result"]!["game"]!["roundCode"]) != round)
                    throw new InvalidDataException("이닝 응답의 시즌 구분이 기본 응답과 다릅니다.");
                var plays = inningRoot["result"]!["textRelayData"]!["textRelays"] as JsonArray;
                if (plays is null || plays.Count == 0)
                    throw new InvalidDataException("해당 이닝의 문자중계 배열이 비어 있습니다.");
                var accepted = new Dictionary<int, JsonNode>();
                foreach (var play in plays)
                {
                    if (play is not JsonObject || Number(play["inn"]) != inning)
                        throw new InvalidDataException($"요청한 {inning}회와 다른 이닝의 기록이 반환됐습니다.");
                    int? no = Number(play["no"]);
                    if (!no.HasValue || no < 0)
                        throw new InvalidDataException("기록 순번(no)이 없거나 올바르지 않습니다.");
                    if (accepted.ContainsKey(no.Value) || mergedPlays.ContainsKey(no.Value))
                        throw new InvalidDataException($"기록 순번 {no}가 중복됐습니다.");
                    accepted.Add(no.Value, play.DeepClone());
                }
                foreach (var pair in accepted) mergedPlays.Add(pair.Key, pair.Value);
                collected.Add(inning);
                log?.Invoke($"  네이버 {effectiveGameId} {inning}회: {accepted.Count}개 기록");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var message = $"{inning}회 수집 실패: {ex.Message}";
                errors.Add(message);
                log?.Invoke($"  네이버 {effectiveGameId} {message}");
            }
            if (delayMs > 0 && inning < expected) await Task.Delay(delayMs, ct);
        }

        // Preserve every other Naver field, including statistics, without using it to alter KBO.
        relay["textRelays"] = new JsonArray(mergedPlays.OrderByDescending(p => p.Key).Select(p => p.Value).ToArray());
        bool complete = IsEnded(status) && collected.Count == expected && errors.Count == 0;
        return new(root.ToJsonString(), complete, round, status, expected, collected, errors);
    }

    public static JsonObject ReadValidatedRoot(string json, string expectedGameId, bool requireRelay)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("네이버 JSON 객체가 없습니다.");
        if (root["success"] is JsonValue success && success.TryGetValue<bool>(out var ok) && !ok)
            throw new InvalidDataException("네이버 API가 success=false를 반환했습니다.");
        if (Number(root["code"]) is int code && code != 200)
            throw new InvalidDataException($"네이버 API 오류 code={code}");
        var game = root["result"]?["game"] as JsonObject ?? throw new InvalidDataException("네이버 game 객체가 없습니다.");
        var expected = GameRequest.Parse(expectedGameId);
        ValidateGameId(game["gameId"], expected.GameId);
        if (String(game["awayTeamCode"]) != expected.GameId.Substring(8, 2) ||
            String(game["homeTeamCode"]) != expected.GameId.Substring(10, 2))
            throw new InvalidDataException("네이버 홈/원정 팀 코드와 요청 경기 ID가 다릅니다.");
        if (String(game["categoryId"]) is { } category && category != "kbo")
            throw new InvalidDataException("KBO 리그에 해당하지 않는 네이버 응답입니다.");
        var relay = root["result"]?["textRelayData"] as JsonObject;
        if (relay is null && requireRelay) throw new InvalidDataException("네이버 textRelayData가 없습니다.");
        if (relay is not null) ValidateGameId(relay["gameId"], expected.GameId);
        return root;
    }

    // Also validates legacy files from the original collector before reusing them.
    public static NaverCollectionResult InspectSaved(string json, string expectedGameId)
    {
        var root = ReadValidatedRoot(json, expectedGameId, requireRelay: false);
        var game = root["result"]!["game"]!.AsObject();
        var status = String(game["statusCode"]);
        var round = String(game["roundCode"]);
        var expected = ExpectedInnings(game, root["result"]!["textRelayData"] as JsonObject);
        var errors = new List<string>();
        var innings = new HashSet<int>();
        var ids = new HashSet<int>();
        if (root["result"]!["textRelayData"]?["textRelays"] is JsonArray plays)
        {
            foreach (var play in plays)
            {
                var inning = play is JsonObject ? Number(play["inn"]) : null;
                var no = play is JsonObject ? Number(play["no"]) : null;
                if (inning is null || inning < 1 || inning > expected || no is null || no < 0 || !ids.Add(no.Value))
                    errors.Add("저장된 네이버 기록의 이닝/순번이 올바르지 않습니다.");
                else innings.Add(inning.Value);
            }
        }
        if (!IsEnded(status)) errors.Add("네이버 경기가 종료되지 않았습니다.");
        if (round != "kbo_r") errors.Add("네이버 정규시즌 경기로 확인되지 않았습니다.");
        if (expected == 0 || innings.Count != expected) errors.Add("전체 이닝 기록이 없습니다.");
        return new(json, errors.Count == 0, round, status, expected, innings.Order().ToArray(), errors);
    }

    public static int ExpectedInnings(JsonObject game, JsonObject? relay = null)
    {
        int CountPlayed(JsonNode? node)
        {
            if (node is not JsonArray scores) return 0;
            for (int i = scores.Count - 1; i >= 0; i--)
            {
                var value = String(scores[i]);
                if (value is not null && int.TryParse(value.TrimEnd('X', 'x'), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var runs) && runs >= 0) return i + 1;
            }
            return 0;
        }
        var played = Math.Max(CountPlayed(game["homeTeamScoreByInning"]), CountPlayed(game["awayTeamScoreByInning"]));
        // During play the current inning can still be '-' in the scoreboard.
        // Use the observed current-inning metadata so its pitches are collected too.
        if (!IsEnded(String(game["statusCode"])) && String(game["statusCode"]) != "BEFORE")
        {
            var current = Number(relay?["inn"]) ?? 0;
            var marker = System.Text.RegularExpressions.Regex.Match(String(game["currentInning"]) ?? "", @"^(\d{1,2})회(?:초|말)");
            if (marker.Success) current = Math.Max(current, int.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture));
            if (current is > 0 and <= 99) played = Math.Max(played, current);
        }
        return played;
    }

    // Toggles between Naver's two known gameId forms for the same game: the plain 13-character
    // KBO-style ID used by older/archived games, and that same ID with the 4-digit season
    // repeated at the end, used by current-season game-polling requests. Returns false if the
    // input doesn't match either shape.
    private static readonly Regex GameIdShapeRegex = new(@"^(?<id>\d{8}[A-Za-z]{4}\d)(?<year>\d{4})?$");
    internal static bool TryAlternateGameId(string gameId, out string alternate)
    {
        var match = GameIdShapeRegex.Match(gameId);
        if (!match.Success)
        {
            alternate = "";
            return false;
        }
        var id = match.Groups["id"].Value;
        alternate = match.Groups["year"].Success ? id : id + id[..4];
        return true;
    }

    private static void ValidateGameId(JsonNode? node, string expected)
    {
        try
        {
            if (GameRequest.Parse(String(node) ?? "").GameId == expected) return;
        }
        catch (FormatException) { }
        throw new InvalidDataException("네이버 응답의 경기 ID가 요청한 경기와 다릅니다.");
    }

    internal static string? String(JsonNode? node)
        => node is JsonValue value && (value.TryGetValue<string>(out var text)) ? text : node is JsonValue ? node.ToJsonString() : null;
    internal static int? Number(JsonNode? node)
        => int.TryParse(String(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
