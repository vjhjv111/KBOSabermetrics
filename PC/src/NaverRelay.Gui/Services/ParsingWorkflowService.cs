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
                var game = await Task.Run(() => RelayParser.ParseJson(json), cancellationToken);
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

                if (databaseCache is not null)
                {
                    Report(progress, document, sequence, documents.Count, WorkflowStage.Saving, "SQLite DB 저장 중");
                    await databaseCache.SaveGameAndSourceAsync(game, document, cancellationToken);
                }

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
                    $"완료: PA {game.Summary.CompletedPlateAppearanceCount}, 투구 {game.Summary.PitchEventCount}");
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
