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
            if (naver.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("통합 JSON의 네이버 자료가 누락됐습니다.");
            // Mirrors UnifiedDocumentStore.RequiresKboOfficial (WEB side): only the current season
            // must be cross-checked against KBO official data before a game counts as importable. A
            // past season that only ever collected Naver's own text relay (including backfilled/legacy
            // games with no kboOfficial at all) must still be able to reach the database.
            var gameIdText = root.TryGetProperty("gameId", out var gameIdEl) ? gameIdEl.GetString() : null;
            var requiresKboOfficial = gameIdText is { Length: >= 4 } &&
                int.TryParse(gameIdText.AsSpan(0, 4), out var gameYear) && gameYear >= DateTime.UtcNow.Year;
            var hasKboOfficial = root.TryGetProperty("kboOfficial", out var kbo) && kbo.ValueKind == JsonValueKind.Object;
            if (requiresKboOfficial && !hasKboOfficial)
                throw new InvalidDataException("통합 JSON의 KBO 공식 자료가 누락됐습니다.");
            // "collectionStatus" is baked into the file at collection time, so an older file collected
            // before this relaxation existed can say "partial" purely because kboOfficial was missing —
            // even though, by today's rule, a past season with complete naver data would count as
            // complete. Accept "partial" too for a past season; the checks below (errors/sourceStatus/
            // naverCollection) are what actually verify naver itself is genuinely complete.
            var collectionStatusValue = root.TryGetProperty("collectionStatus", out var status) ? status.GetString() : null;
            if (collectionStatusValue != "complete" && (requiresKboOfficial || collectionStatusValue != "partial"))
                throw new InvalidDataException("완료된 통합 JSON만 수집할 수 있습니다.");
            if (root.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind != JsonValueKind.Array) throw new InvalidDataException("통합 JSON에 수집 오류가 있습니다.");
                foreach (var error in errors.EnumerateArray())
                {
                    var source = error.TryGetProperty("source", out var src) ? src.GetString() : null;
                    if (source == "kboOfficial" && !requiresKboOfficial) continue;
                    throw new InvalidDataException("통합 JSON에 수집 오류가 있습니다.");
                }
            }
            if (root.TryGetProperty("sourceStatus", out var sources))
            {
                var naverSourceStatus = sources.TryGetProperty("naver", out var ns) ? ns.GetString() : null;
                var kboSourceStatus = sources.TryGetProperty("kboOfficial", out var ks) ? ks.GetString() : null;
                if (naverSourceStatus != "complete" || (requiresKboOfficial && kboSourceStatus != "complete"))
                    throw new InvalidDataException("통합 JSON의 개별 수집이 완료되지 않았습니다.");
            }
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
            KboPlayLog.Document? official = null;
            string id;
            // Parsing kboOfficial at all — even just to read it, before any box-score/identity logic
            // runs — is exactly the step DatabaseCacheService.PrepareGameWriteAsync now skips for a
            // past season. Gate it here too: a past season with kboOfficial present in the file (it was
            // still collected/cached) must still behave as if it were absent, so a malformed/ambiguous
            // historical box score can never throw during parsing, before it even reaches that later gate.
            if (hasKboOfficial && requiresKboOfficial)
            {
                official = KboPlayLog.Parse(kbo.GetRawText());
                id = official.GameId + official.GameId[..4];
                if (root.GetProperty("gameId").GetString() != official.GameId || root.GetProperty("naverGameId").GetString() != id)
                    throw new InvalidDataException("통합 JSON의 경기 ID가 일치하지 않습니다.");
            }
            else
            {
                id = root.TryGetProperty("naverGameId", out var naverIdEl) ? naverIdEl.GetString() ?? "" : "";
            }
            // Naver echoes back whichever gameId FORM its API was actually queried with — the
            // 17-char year-suffixed form for a normal request, but the plain 13-char form for an
            // old/archived game fetched via the 404 fallback (see FullGameCollector.TryAlternateGameId).
            // Our own "id" here is always the 17-char reconstruction, so comparing it byte-for-byte
            // against Naver's own echoed gameId breaks for any fallback-fetched game. Compare on the
            // stable 13-char base instead, the same way FullGameCollector.ValidateGameId already does.
            if (!naver.TryGetProperty("result", out var result) || !result.TryGetProperty("game", out var game) ||
                BaseGameId(game.GetProperty("gameId").GetString()) != BaseGameId(id))
                throw new InvalidDataException("통합 JSON의 경기 ID가 일치하지 않습니다.");
            var finalStatus = game.TryGetProperty("statusCode", out var statusCode) ? statusCode.GetString() : null;
            if (!string.Equals(finalStatus, "RESULT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(finalStatus, "ENDED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"RESULT/ENDED 종료 경기만 DB에 수집할 수 있습니다. (statusCode={finalStatus ?? "없음"})");
            if (result.TryGetProperty("textRelayData", out var relay) && relay.TryGetProperty("gameId", out var relayId) &&
                BaseGameId(relayId.GetString()) != BaseGameId(id))
                throw new InvalidDataException("통합 JSON의 문자중계 경기 ID가 다릅니다.");
            return new(naver.GetRawText(), official, true);
        }
        if (KboPlayLog.IsPlayLog(json)) return new(null, KboPlayLog.Parse(json), false);
        return new(json, null, false);
    }

    // The stable part of a gameId is always the first 13 characters (8-digit date + 4-char team
    // pair + 1 digit); an optional 4-digit year suffix may or may not follow depending on which
    // Naver endpoint form was actually used to fetch the data.
    private static string BaseGameId(string? raw) => raw is { Length: >= 13 } ? raw[..13] : raw ?? "";

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
