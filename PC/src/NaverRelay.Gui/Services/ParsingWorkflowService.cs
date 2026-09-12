using NaverRelay.Application.Importing;
using NaverRelay.Gui.Models;
using NaverRelay.Infrastructure.Sqlite;
using NaverRelay.Parsing;

namespace NaverRelay.Gui.Services;

internal static class ParsingWorkflowService
{
    public static async Task<ParsingWorkflowResult> RunAsync(
        IReadOnlyList<InputDocument> documents,
        string? outputDirectory,
        bool saveAsYouGo,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken,
        DatabaseCacheService? databaseCache = null)
    {
        var result = new ParsingWorkflowResult { OutputDirectory = outputDirectory };
        var retainGamesInMemory = documents.Count <= 25;

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            var sequence = index + 1;

            try
            {
                Report(progress, document, sequence, documents.Count, WorkflowStage.Reading, "JSON 읽는 중");
                var json = await document.ReadJsonAsync(cancellationToken);

                Report(progress, document, sequence, documents.Count, WorkflowStage.Parsing, "정규화 파싱 중");
                var isOfficial = KboPlayLog.IsPlayLog(json);
                if (isOfficial && databaseCache is null) throw new InvalidDataException("공식 문자중계는 SQLite DB 수집에서 네이버 JSON 수집 후 추가해주세요.");
                var game = isOfficial
                    ? await databaseCache!.ImportKboPlayLogAsync(json, cancellationToken)
                    : await Task.Run(() => RelayParser.ParseJson(json), cancellationToken);
                if (databaseCache is not null && !isOfficial)
                {
                    Report(progress, document, sequence, documents.Count, WorkflowStage.Saving, "SQLite DB 저장 중");
                    await databaseCache.SaveGameAndSourceAsync(game, document, cancellationToken);
                }
                else if (databaseCache is null && game.ImportedOfficialSource is { } embedded)
                {
                    KboPlayLog.Apply(game, embedded);
                    if (embedded.BoxScore is { } box) KboBoxScore.Apply(game, box, embedded.GameId);
                }
                if (retainGamesInMemory) result.Games.Add(game);
                result.ParsedGameCount++;
                result.CompletedPlateAppearanceCount += game.Summary.CompletedPlateAppearanceCount;
                result.PitchCount += game.Summary.PitchEventCount;
                result.Summaries.Add(new LightweightParsedGameSummary
                {
                    GameId = game.GameId,
                    GameDate = game.GameDate,
                    AwayTeamCode = game.AwayTeam.TeamCode,
                    HomeTeamCode = game.HomeTeam.TeamCode,
                    Summary = game.Summary,
                });

                if (saveAsYouGo && !string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Report(progress, document, sequence, documents.Count, WorkflowStage.Saving, "정규화 JSON 저장 중");
                    await NormalizedOutputWriter.SaveGameAsync(
                        game,
                        outputDirectory,
                        document.DisplayName,
                        cancellationToken);
                }

                Report(
                    progress,
                    document,
                    sequence,
                    documents.Count,
                    WorkflowStage.Completed,
                    isOfficial
                        ? string.Join(" / ", game.Diagnostics.Where(d => d.Code == "KBO_PLAYLOG_APPLIED").Select(d => d.Message))
                        : $"완료: PA {game.Summary.CompletedPlateAppearanceCount}, 투구 {game.Summary.PitchEventCount}");
            }
            catch (GameNotStartedException ex)
            {
                result.Deferred.Add(new ParsingFailure { DocumentId = document.Id, SourceName = document.SourceDisplay, Error = ex.Message });
                Report(progress, document, sequence, documents.Count, WorkflowStage.Deferred, ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failures.Add(new ParsingFailure
                {
                    DocumentId = document.Id,
                    SourceName = document.SourceDisplay,
                    Error = ex.Message,
                });
                Report(progress, document, sequence, documents.Count, WorkflowStage.Failed, ex.Message);
            }
        }

        if (databaseCache is not null)
        {
            var years = result.Summaries.Select(x=>int.TryParse(x.GameId[..Math.Min(4,x.GameId.Length)],out var y)?y:0).Where(y=>y>=2022).Distinct().ToArray();
            if (years.Length == 0)
                years = (await databaseCache.GetRegularSeasonYearsAsync(cancellationToken)).Take(1).ToArray();
            foreach(var year in years)
            {
                try
                {
                    var sync=await databaseCache.SyncKboCorrectionsAsync(year,cancellationToken);
                    ReportOfficial(progress,sync.Message);
                    foreach(var detail in sync.Details)
                        ReportOfficial(progress,"KBO 정정 보류 "+detail);
                    if(sync.Pending>0)result.ReviewWarnings.Add(new ParsingFailure{DocumentId="KBO",SourceName=$"KBO {year} 정정 대조",Error=sync.Message});
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){result.ReviewWarnings.Add(new ParsingFailure{DocumentId="KBO",SourceName=$"KBO {year} 정정 대조",Error="수집 DB는 보존했습니다. 정정 조회 실패: "+ex.Message});}
                try
                {
                    var rbiProgress = new Progress<string>(message => { ReportOfficial(progress,message); });
                    var rbi = await databaseCache.SyncOfficialRbiAsync(year,rbiProgress,cancellationToken);
                    ReportOfficial(progress,rbi.Message);
                    if (rbi.Pending > 0) result.ReviewWarnings.Add(new ParsingFailure { DocumentId="KBO-RBI",SourceName=$"KBO {year} 타점 대조",Error=rbi.Message });
                }
                catch(OperationCanceledException) { throw; }
                catch(Exception ex) { result.ReviewWarnings.Add(new ParsingFailure { DocumentId="KBO-RBI",SourceName=$"KBO {year} 타점 대조",Error="기존 기록 유지. 공식 타점 대조 실패: "+ex.Message }); }
                try
                {
                    var teamProgress = new Progress<string>(message => { ReportOfficial(progress,message); });
                    var team = await databaseCache.SyncOfficialTeamPitchingAsync(year,teamProgress,cancellationToken);
                    ReportOfficial(progress,team.Message);
                    if (team.Pending > 0) result.ReviewWarnings.Add(new ParsingFailure { DocumentId="KBO-TEAM",SourceName=$"KBO {year} 팀 자책점 대조",Error=team.Message });
                }
                catch(OperationCanceledException) { throw; }
                catch(Exception ex) { result.ReviewWarnings.Add(new ParsingFailure { DocumentId="KBO-TEAM",SourceName=$"KBO {year} 팀 자책점 대조",Error="기존 기록 유지. 공식 팀 기록 조회 실패: "+ex.Message }); }
            }
        }

        if (saveAsYouGo && !string.IsNullOrWhiteSpace(outputDirectory))
        {
            await NormalizedOutputWriter.SaveAggregateSummaryAsync(
                result.Summaries,
                result.Failures.Select(failure => (failure.SourceName, failure.Error)).ToList(),
                outputDirectory,
                cancellationToken);
        }

        return result;
    }

    private static void ReportOfficial(IProgress<WorkflowProgress>? progress, string message)
    {
        progress?.Report(new WorkflowProgress
        {
            DocumentId = "", DocumentName = "공식 기록 대조", Stage = WorkflowStage.Reconciling, Message = message,
        });
    }

    private static void Report(
        IProgress<WorkflowProgress>? progress,
        InputDocument document,
        int currentIndex,
        int totalCount,
        WorkflowStage stage,
        string message)
    {
        progress?.Report(new WorkflowProgress
        {
            DocumentId = document.Id,
            DocumentName = document.DisplayName,
            CurrentIndex = currentIndex,
            TotalCount = totalCount,
            Stage = stage,
            Message = message,
        });
    }
}
