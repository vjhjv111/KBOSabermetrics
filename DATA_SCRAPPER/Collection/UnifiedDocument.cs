using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KboRelayDownloader;

namespace NaverRelayUI.Collection;

public sealed record CollectionError(string Source, string Message);
public sealed record SourceCollectionStatus(string Naver, string KboOfficial);
public sealed record NaverCollectionEvidence(int ExpectedInnings, IReadOnlyList<int> CollectedInnings, bool IsComplete);
public sealed record UnifiedDocument(string GameId, string NaverGameId, string CanonicalSource,
    string CollectionStatus, DateTimeOffset CollectedAt, IReadOnlyList<CollectionError> Errors,
    RelayDocument? KboOfficial, JsonNode? Naver, SourceCollectionStatus SourceStatus,
    NaverCollectionEvidence? NaverCollection)
{
    public int SchemaVersion { get; init; } = 1;
}

public sealed record CachedSources(NaverCollectionResult? Naver, RelayDocument? KboOfficial, bool IsComplete);

public static class UnifiedDocumentStore
{
    public static bool IsFinalKbo(RelayDocument? document, GameRequest expected)
    {
        if (document is null || document.GameId != expected.GameId || document.LeagueId != "1" ||
            document.SeriesId != "0" || !document.IsComplete || document.Logs is not { Count: > 0 } ||
            document.Count != document.Logs.Count || !document.Logs.Any(p => p.Text == "경기종료")) return false;
        var box = document.BoxScore;
        bool HasTeam(TeamBoxScore? team, string code) => team is not null && team.TeamCode == code &&
            team.Batting is { Rows.Count: > 0 } && team.Pitching is { Rows.Count: > 0 } &&
            team.Batting.Columns is not null && team.Batting.Columns.Contains("타자") && team.Batting.Columns.Contains("타수") &&
            team.Pitching.Columns is not null && team.Pitching.Columns.Contains("투수") && team.Pitching.Columns.Contains("이닝") &&
            team.Innings is { Count: > 0 } && team.Totals is not null &&
            new[] { "R", "H", "E", "B" }.All(team.Totals.ContainsKey);
        return box is not null && HasTeam(box.Away, expected.GameId.Substring(8, 2)) &&
            HasTeam(box.Home, expected.GameId.Substring(10, 2));
    }

    public static CachedSources ReadCache(string json, GameRequest expected, string naverGameId)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("저장된 JSON 객체가 없습니다.");
        // Legacy Naver-only file: validate internal game IDs and all inning coverage first.
        if (root["result"] is JsonObject)
            return new(FullGameCollector.InspectSaved(json, naverGameId), null, false);
        if (FullGameCollector.String(root["gameId"]) != expected.GameId ||
            FullGameCollector.String(root["naverGameId"]) != naverGameId ||
            FullGameCollector.String(root["canonicalSource"]) != "kboOfficial")
            throw new InvalidDataException("기존 파일의 경기/출처가 파일명과 다릅니다. 기존 파일을 유지합니다.");
        NaverCollectionResult? naver = null;
        if (root["naver"] is JsonObject naverNode)
        {
            naver = FullGameCollector.InspectSaved(naverNode.ToJsonString(), naverGameId);
            // A partial API acquisition must never become complete just because every
            // inning happened to have one surviving row in the JSON.
            bool identityMetadataFailed = root["errors"] is JsonArray savedErrors && savedErrors.Any(
                error => error is JsonObject && FullGameCollector.String(error["source"]) == "identityMapping");
            if (FullGameCollector.String(root["sourceStatus"]?["naver"]) != "complete" || identityMetadataFailed)
                naver = naver with { IsComplete = false };
        }
        RelayDocument? kbo = root["kboOfficial"]?.Deserialize<RelayDocument>(RelayClient.JsonOptions);
        if (kbo is not null && (kbo.GameId != expected.GameId || kbo.LeagueId != "1" || kbo.SeriesId != "0"))
            throw new InvalidDataException("기존 KBO 데이터의 경기/시리즈가 다릅니다. 기존 파일을 유지합니다.");
        bool complete = FullGameCollector.String(root["collectionStatus"]) == "complete" &&
            naver is { IsComplete: true } && IsFinalKbo(kbo, expected) && kbo!.IdentityMapping is not null &&
            root["errors"] is JsonArray { Count: 0 };
        return new(naver, kbo, complete);
    }

    public static UnifiedDocument Create(GameRequest expected, string naverGameId,
        NaverCollectionResult? naver, RelayDocument? kbo, IEnumerable<CollectionError>? acquisitionErrors = null)
    {
        var errors = acquisitionErrors?.ToList() ?? [];
        if (naver?.Json is { } raw)
        {
            var inspected = FullGameCollector.InspectSaved(raw, naverGameId);
            if (naver.IsComplete && !inspected.IsComplete)
            {
                naver = naver with { IsComplete = false };
                errors.Add(new("naver", "전체 수집 표시와 실제 저장 데이터의 이닝/종료 상태가 다릅니다."));
            }
        }
        if (kbo is not null && (kbo.GameId != expected.GameId || kbo.LeagueId != "1" || kbo.SeriesId != "0"))
            throw new InvalidDataException("다른 경기의 KBO 데이터를 통합할 수 없습니다.");
        if (kbo is not null && naver?.Json is { } source)
        {
            try { kbo = IdentityMapper.Enrich(kbo, source, naverGameId + ".json"); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException)
            {
                // Remove stale assignments if a previous partial source had different identity data.
                kbo = WithoutIdentities(kbo);
                errors.Add(new("identityMapping", ex.Message));
            }
        }
        bool naverComplete = naver is { IsComplete: true, Json: not null };
        bool kboComplete = IsFinalKbo(kbo, expected);
        if (!naverComplete && !errors.Any(e => e.Source == "naver")) errors.Add(new("naver", "네이버 전체 중계 수집이 완료되지 않았습니다."));
        if (!kboComplete && !errors.Any(e => e.Source == "kboOfficial")) errors.Add(new("kboOfficial", "KBO 공식 종료 기록과 박스스코어 수집이 완료되지 않았습니다."));
        bool complete = naverComplete && kboComplete && errors.Count == 0;
        return new(expected.GameId, naverGameId, "kboOfficial", complete ? "complete" : "partial",
            DateTimeOffset.UtcNow, errors, kbo, naver?.Json is { } json ? JsonNode.Parse(json) : null,
            new(naverComplete ? "complete" : naver?.Json is null ? "missing" : "partial",
                kboComplete ? "complete" : kbo is null ? "missing" : "partial"),
            naver is null ? null : new(naver.ExpectedInnings, naver.CollectedInnings, naverComplete));
    }

    private static RelayDocument WithoutIdentities(RelayDocument document)
    {
        if (document.BoxScore is not { } box) return document with { IdentityMapping = null };
        TeamBoxScore Clear(TeamBoxScore team) => team with
        {
            Batting = team.Batting with { RowIdentities = null }, Pitching = team.Pitching with { RowIdentities = null }
        };
        return document with { IdentityMapping = null, BoxScore = box with { Away = Clear(box.Away), Home = Clear(box.Home) } };
    }

    public static async Task SaveAtomicAsync(string path, string json, CancellationToken ct = default)
    {
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, absolute, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
