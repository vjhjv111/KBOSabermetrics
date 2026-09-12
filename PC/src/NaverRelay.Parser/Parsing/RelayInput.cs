using System.Text.Json;

namespace NaverRelay.Parsing;

/// <summary>One input boundary shared by initial import and source-file reimports.</summary>
public static class RelayInput
{
    public sealed record Input(string? NaverJson, KboPlayLog.Document? Official, bool IsCombined);

    public static Input Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("경기 JSON은 객체여야 합니다.");
        if (root.TryGetProperty("naver", out var naver) || root.TryGetProperty("kboOfficial", out _))
        {
            if (!root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var schema) || schema != 1)
                throw new InvalidDataException("지원하지 않는 통합 JSON 버전입니다.");
            ThrowIfNotStarted(root, naver);
            if (naver.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kboOfficial", out var kbo) || kbo.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("통합 JSON의 네이버/공식 자료가 누락됐습니다.");
            if (!root.TryGetProperty("collectionStatus", out var status) || status.GetString() != "complete")
                throw new InvalidDataException("완료된 통합 JSON만 수집할 수 있습니다.");
            if (root.TryGetProperty("errors", out var errors) && (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() != 0))
                throw new InvalidDataException("통합 JSON에 수집 오류가 있습니다.");
            if (root.TryGetProperty("sourceStatus", out var sources) &&
                (sources.GetProperty("naver").GetString() != "complete" || sources.GetProperty("kboOfficial").GetString() != "complete"))
                throw new InvalidDataException("통합 JSON의 개별 수집이 완료되지 않았습니다.");
            if (root.TryGetProperty("naverCollection", out var collection))
            {
                if (!collection.TryGetProperty("isComplete", out var complete) || complete.ValueKind != JsonValueKind.True)
                    throw new InvalidDataException("네이버 이닝 수집이 완료되지 않았습니다.");
                if (collection.TryGetProperty("expectedInnings", out var expected) && collection.TryGetProperty("collectedInnings", out var innings))
                {
                    var observed = innings.EnumerateArray().Select(i => i.GetInt32()).ToArray();
                    if (expected.GetInt32() < 1 || !Enumerable.Range(1, expected.GetInt32()).SequenceEqual(observed.Order()))
                        throw new InvalidDataException("네이버 이닝 수집에 누락 또는 중복이 있습니다.");
                }
            }
            var official = KboPlayLog.Parse(kbo.GetRawText());
            var id = official.GameId + official.GameId[..4];
            if (root.GetProperty("gameId").GetString() != official.GameId || root.GetProperty("naverGameId").GetString() != id ||
                !naver.TryGetProperty("result", out var result) || !result.TryGetProperty("game", out var game) || game.GetProperty("gameId").GetString() != id)
                throw new InvalidDataException("통합 JSON의 경기 ID가 일치하지 않습니다.");
            if (result.TryGetProperty("textRelayData", out var relay) && relay.TryGetProperty("gameId", out var relayId) && relayId.GetString() != id)
                throw new InvalidDataException("통합 JSON의 문자중계 경기 ID가 다릅니다.");
            return new(naver.GetRawText(), official, true);
        }
        if (KboPlayLog.IsPlayLog(json)) return new(null, KboPlayLog.Parse(json), false);
        return new(json, null, false);
    }

    private static void ThrowIfNotStarted(JsonElement root, JsonElement naver)
    {
        // Only an explicitly identified, empty pregame snapshot is deferred.
        // A completed game with missing sources remains an import error.
        if (naver.ValueKind != JsonValueKind.Object ||
            !naver.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("game", out var game) || game.ValueKind != JsonValueKind.Object ||
            !game.TryGetProperty("statusCode", out var status) || status.GetString() != "BEFORE" ||
            !root.TryGetProperty("collectionStatus", out var collection) || collection.GetString() != "partial" ||
            !root.TryGetProperty("kboOfficial", out var official) || official.ValueKind != JsonValueKind.Null ||
            !result.TryGetProperty("textRelayData", out var relay) || relay.ValueKind != JsonValueKind.Null)
            return;
        var id = root.TryGetProperty("gameId", out var kboId) ? kboId.GetString() : null;
        if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{8}[A-Z]{4}\d$")) return;
        var naverId = id + id[..4];
        if (!root.TryGetProperty("naverGameId", out var combinedId) || combinedId.GetString() != naverId ||
            !game.TryGetProperty("gameId", out var gameId) || gameId.GetString() != naverId) return;
        throw new GameNotStartedException(naverId);
    }
}

public sealed class GameNotStartedException(string gameId)
    : IOException($"경기 전 자료입니다. 경기 종료 후 다시 수집하세요. ({gameId})")
{
    public string GameId { get; } = gameId;
}
