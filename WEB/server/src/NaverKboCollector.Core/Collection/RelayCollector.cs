using System.Text.Json;
using System.Text.RegularExpressions;
using KboRelayDownloader;
using NaverRelayUI.Models;

namespace NaverRelayUI.Collection;

public sealed record CollectionSummary(int Complete, int Partial, int Skipped, int Excluded, int Failed)
{
    public IReadOnlyList<string> ExcludedGameIds { get; init; } = [];
}

// Optional seams let contract tests use real saved responses and controlled transport failures.
public sealed class CollectorDependencies
{
    public Func<HttpClient, string, int, Action<string>?, CancellationToken, Task<NaverCollectionResult>>? DownloadNaverAsync { get; init; }
    public Func<GameRequest, CancellationToken, Task<RelayDocument>>? DownloadKboAsync { get; init; }
}

public static class RelayCollector
{
    public static async Task<CollectionSummary> CollectAllAsync(HttpClient http,
        IEnumerable<ScheduleGame> games, string outputDir, int delayMs = 500,
        Action<string>? log = null, IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default, CollectorDependencies? dependencies = null)
    {
        Directory.CreateDirectory(outputDir);
        var list = games.ToList();
        int done = 0, complete = 0, partial = 0, skipped = 0, excluded = 0, failed = 0;
        var excludedGameIds = new List<string>();
        using var kboClient = dependencies?.DownloadKboAsync is null ? new RelayClient() : null;
        var downloadNaver = dependencies?.DownloadNaverAsync ?? FullGameCollector.CollectResultAsync;
        Func<GameRequest, CancellationToken, Task<RelayDocument>> downloadKbo = dependencies?.DownloadKboAsync ??
            (async (game, cancellation) => (await kboClient!.DownloadAsync(game, cancellation)).Document);

        foreach (var scheduled in list)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (scheduled.Cancel || scheduled.StatusCode == "CANCEL" || scheduled.CategoryId != "kbo")
                {
                    log?.Invoke($"건너뜀 {scheduled.GameId ?? "ID 없음"} ({(scheduled.Cancel || scheduled.StatusCode == "CANCEL" ? "경기취소" : "KBO 리그 아님")})");
                    skipped++;
                    continue;
                }
                // Remote schedule values are never allowed to become arbitrary output paths.
                if (scheduled.GameId is null || !Regex.IsMatch(scheduled.GameId, @"^\d{8}[A-Za-z]{4}\d(?:\d{4})?$"))
                    throw new InvalidDataException("일정의 경기 ID 형식이 올바르지 않습니다.");
                var request = GameRequest.Parse(scheduled.GameId);
                var naverGameId = request.GameId + request.Year;
                if ((!string.IsNullOrEmpty(scheduled.AwayTeamCode) && scheduled.AwayTeamCode != request.GameId.Substring(8, 2)) ||
                    (!string.IsNullOrEmpty(scheduled.HomeTeamCode) && scheduled.HomeTeamCode != request.GameId.Substring(10, 2)))
                    throw new InvalidDataException("일정의 팀 코드와 경기 ID가 다릅니다.");
                var path = Path.Combine(outputDir, naverGameId + ".json");
                CachedSources cached = new(null, null, false);
                if (File.Exists(path))
                {
                    // A conflicting or malformed existing file is left in place for inspection.
                    cached = UnifiedDocumentStore.ReadCache(await File.ReadAllTextAsync(path, ct), request, naverGameId);
                    if (cached.IsComplete && !ShouldRefreshNaverMetadata(cached.Naver, scheduled))
                    {
                        log?.Invoke($"건너뜀 {naverGameId} (네이버 + KBO 공식 수집 완료 파일)");
                        skipped++;
                        continue;
                    }
                }

                var errors = new List<CollectionError>();
                var naver = cached.Naver;
                var refreshNaverMetadata = ShouldRefreshNaverMetadata(naver, scheduled);
                var naverMetadataRefreshed = !refreshNaverMetadata;
                if (naver is not { IsComplete: true } || refreshNaverMetadata)
                {
                    log?.Invoke(refreshNaverMetadata
                        ? $"갱신 {naverGameId}: 네이버 ENDED → RESULT 최종 승·패 메타데이터"
                        : $"수집 {naverGameId}: 네이버 전체 이닝");
                    try
                    {
                        var downloaded = await downloadNaver(http, naverGameId, delayMs > 0 ? 300 : 0, log, ct);
                        if (downloaded.Json is { } raw)
                        {
                            // Derive season eligibility from the validated response, not the URL/date.
                            var inspected = FullGameCollector.InspectSaved(raw, naverGameId);
                            naver = downloaded with
                            {
                                RoundCode = inspected.RoundCode, StatusCode = inspected.StatusCode,
                                IsComplete = downloaded.IsComplete && inspected.IsComplete
                            };
                            naverMetadataRefreshed = !refreshNaverMetadata || naver.StatusCode == "RESULT";
                        }
                        foreach (var error in downloaded.Errors) errors.Add(new("naver", error));
                        if (downloaded.Json is null && downloaded.Errors.Count == 0)
                            errors.Add(new("naver", "네이버 서버에서 유효한 중계 데이터를 받지 못했습니다."));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { errors.Add(new("naver", ex.Message)); }
                }
                else log?.Invoke($"재사용 {naverGameId}: 이미 저장된 네이버 전체 중계");

                if (refreshNaverMetadata && !naverMetadataRefreshed)
                {
                    log?.Invoke($"갱신 보류 {naverGameId}: 네이버 RESULT 메타데이터를 받지 못해 기존 완료 파일을 유지합니다.");
                    skipped++;
                    continue;
                }

                if (naver?.RoundCode is { } round && round != "kbo_r")
                {
                    // Do not request seriesId=0 for All-Star/exhibition/postseason games.
                    log?.Invoke($"제외 {naverGameId} (roundCode={round}, 정규시즌만 지원)");
                    excludedGameIds.Add(naverGameId);
                    excluded++;
                    continue;
                }

                var kbo = cached.KboOfficial;
                if (!UnifiedDocumentStore.IsFinalKbo(kbo, request))
                {
                    if (naver is { RoundCode: "kbo_r", IsComplete: true })
                    {
                        try
                        {
                            log?.Invoke($"수집 {naverGameId}: KBO 공식 문자중계 + 박스스코어");
                            var downloaded = await downloadKbo(request, ct);
                            if (downloaded.GameId != request.GameId || downloaded.LeagueId != "1" || downloaded.SeriesId != "0")
                                throw new InvalidDataException("KBO 응답 경기/리그/시리즈가 요청과 다릅니다.");
                            kbo = downloaded;
                            if (!UnifiedDocumentStore.IsFinalKbo(kbo, request))
                                errors.Add(new("kboOfficial", "KBO의 경기종료 기록 또는 전체 박스스코어가 아직 없습니다."));
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex) { errors.Add(new("kboOfficial", ex.Message)); }
                    }
                    else if (naver?.RoundCode == "kbo_r")
                        errors.Add(new("kboOfficial", "네이버 경기가 RESULT/ENDED로 완성된 뒤 KBO 공식 종료 기록을 수집합니다."));
                    else errors.Add(new("kboOfficial", "네이버 경기 메타데이터로 정규시즌을 확인하지 못해 공식 요청을 보류했습니다."));
                }
                else log?.Invoke($"재사용 {naverGameId}: 이미 저장된 KBO 공식 종료 기록");

                var document = UnifiedDocumentStore.Create(request, naverGameId, naver, kbo, errors);
                ct.ThrowIfCancellationRequested();
                // Re-check just before committing so a completed file created by a second run
                // is not replaced by this run's partial result.
                if (File.Exists(path) && !refreshNaverMetadata && UnifiedDocumentStore.ReadCache(await File.ReadAllTextAsync(path, ct), request, naverGameId).IsComplete)
                {
                    skipped++;
                    log?.Invoke($"건너뜀 {naverGameId} (완료된 기존 파일 유지)");
                    continue;
                }
                await UnifiedDocumentStore.SaveAtomicAsync(path, JsonSerializer.Serialize(document, RelayClient.JsonOptions), ct);
                if (document.CollectionStatus == "complete")
                {
                    complete++;
                    log?.Invoke($"통합 저장 완료 {naverGameId}: 네이버 전체 중계 + KBO 공식 {document.KboOfficial!.Count}개 기록/박스스코어");
                }
                else
                {
                    partial++;
                    log?.Invoke($"부분 저장 {naverGameId}: 네이버={document.SourceStatus.Naver}, KBO={document.SourceStatus.KboOfficial}. 같은 날짜 재실행 시 미완료 소스를 재시도합니다.");
                    foreach (var error in document.Errors) log?.Invoke($"  [{error.Source}] {error.Message}");
                }
                if (document.KboOfficial?.IdentityMapping is { Unresolved: > 0 } mapping)
                    log?.Invoke($"  선수 ID {mapping.Matched}명 연결, {mapping.Unresolved}명 미확정(null 유지)");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed++;
                log?.Invoke($"실패 {scheduled.GameId ?? "ID 없음"}: {ex.Message}");
            }
            finally
            {
                done++;
                progress?.Report((done, list.Count));
            }
            if (delayMs > 0 && done < list.Count) await Task.Delay(delayMs, ct);
        }
        return new(complete, partial, skipped, excluded, failed) { ExcludedGameIds = excludedGameIds };
    }

    public static bool ShouldRefreshNaverMetadata(NaverCollectionResult? cached, ScheduleGame scheduled)
        => cached is { IsComplete: true, StatusCode: "ENDED" } && scheduled.StatusCode == "RESULT";
}
